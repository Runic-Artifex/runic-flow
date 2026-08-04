using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using RunicFlow;

namespace RunicFlow.Dialogs;

/// <summary>Runs typed dialog conversations for one interaction session.</summary>
public sealed class DialogService : IDialogService, IDialogShutdown, IAsyncDisposable
{
    private static readonly TimeSpan DefaultTeardownTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan MaximumTeardownTimeout =
        TimeSpan.FromMilliseconds(4_294_967_294d);

    private readonly object _gate = new();
    private readonly DialogRegistry _dialogs;
    private readonly DialogPresenterRegistry _presenters;
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _teardownTimeout;
    private readonly List<IActiveDialog> _active = [];
    private bool _shutdown;

    /// <summary>Initializes a dialog service from immutable registries.</summary>
    public DialogService(
        DialogRegistry dialogs,
        DialogPresenterRegistry presenters,
        TimeProvider? timeProvider = null,
        TimeSpan? teardownTimeout = null)
    {
        ArgumentNullException.ThrowIfNull(dialogs);
        ArgumentNullException.ThrowIfNull(presenters);
        _dialogs = dialogs;
        _presenters = presenters;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _teardownTimeout = ValidateTeardownTimeout(teardownTimeout);
    }

    /// <summary>Gets the maximum duration allowed for one complete teardown sequence.</summary>
    public TimeSpan TeardownTimeout => _teardownTimeout;

    /// <inheritdoc />
    public ValueTask<DialogOutcome<TResult>> ShowAsync<TViewModel, TRequest, TResult>(
        TRequest request,
        CancellationToken cancellationToken = default)
        where TViewModel : class =>
        ShowCoreAsync(
            _dialogs.Get<TViewModel, TRequest, TResult>(),
            request,
            cancellationToken);

    /// <inheritdoc />
    public ValueTask<DialogOutcome<TResult>> ShowAsync<TViewModel, TRequest, TResult>(
        DialogKey dialog,
        TRequest request,
        CancellationToken cancellationToken = default)
        where TViewModel : class =>
        ShowCoreAsync(
            _dialogs.Get<TViewModel, TRequest, TResult>(dialog),
            request,
            cancellationToken);

    /// <inheritdoc />
    public async ValueTask ShutdownAsync(CancellationToken cancellationToken = default)
    {
        IActiveDialog[] active;
        lock (_gate)
        {
            _shutdown = true;
            active = _active.ToArray();
        }

        using FlowTimeoutCancellation deadline = FlowTimeout.CreateCancellationSource(
            _timeProvider,
            _teardownTimeout,
            cancellationToken);
        for (int index = active.Length - 1; index >= 0; index--)
        {
            deadline.Token.ThrowIfCancellationRequested();
            await active[index].RequestShutdownAsync().ConfigureAwait(false);
        }

        for (int index = active.Length - 1; index >= 0; index--)
        {
            try
            {
                await active[index].Closed.WaitAsync(deadline.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (deadline.IsTimeoutCancellationRequested)
            {
                throw new TimeoutException(
                    $"Dialog shutdown exceeded the {_teardownTimeout} limit while draining active dialogs.");
            }
        }
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync() => ShutdownAsync();

    private async ValueTask<DialogOutcome<TResult>> ShowCoreAsync<TViewModel, TRequest, TResult>(
        DialogRegistration<TViewModel, TRequest, TResult> registration,
        TRequest request,
        CancellationToken cancellationToken)
        where TViewModel : class
    {
        ThrowIfShutdown();
        if (cancellationToken.IsCancellationRequested)
        {
            return DialogOutcome<TResult>.Cancelled();
        }

        FlowSessionId sessionId = FlowSessionId.Create();
        DialogController<TResult> controller = new(registration.Key, sessionId);
        ActiveDialog<TResult> active = new(controller);
        DialogContent<TViewModel>? content = null;
        IFlowPresentationLease? lease = null;
        bool committed = false;
        bool activeRegistered = TryRegisterActive(active);
        if (!activeRegistered)
        {
            active.Dispose();
            throw new ObjectDisposedException(nameof(DialogService));
        }

        List<Exception>? postCommitFailures = null;
        CallerCancellationRequest<TResult>? callerRequest = null;
        CancellationTokenRegistration callerCancellation = default;
        CancellationTokenSource? dialogCancellation = null;
        long started = _timeProvider.GetTimestamp();
        using Activity? activity = FlowTelemetry.ActivitySource.StartActivity(FlowTelemetry.DialogActivityName);
        activity?.SetTag("flow.dialog.key", registration.Key.Value);
        activity?.SetTag("flow.session.id", sessionId.Value);

        try
        {
            CancellationToken stageCancellationToken;
            using (CancellationTokenSource contentFactoryCancellation =
                CancellationTokenSource.CreateLinkedTokenSource(
                    active.ShutdownToken,
                    cancellationToken))
            {
                stageCancellationToken = contentFactoryCancellation.Token;
                content = await registration.ContentFactory(request, controller, stageCancellationToken)
                    .ConfigureAwait(false);
            }
            if (content is null)
            {
                throw new InvalidOperationException("A dialog content factory returned null.");
            }

            controller.ConfigureGuard(content.ViewModel as IDialogCloseGuard<TResult>);
            callerRequest = new CallerCancellationRequest<TResult>(controller);
            callerCancellation = cancellationToken.Register(
                static state => ((CallerCancellationRequest<TResult>)state!).Request(),
                callerRequest);
            dialogCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                active.ShutdownToken,
                callerRequest.AcceptedCancellation);
            stageCancellationToken = dialogCancellation.Token;

            DialogCompletion<TResult>? preCommitCompletion =
                await GetPreCommitCompletionAsync(callerRequest, controller).ConfigureAwait(false);
            if (preCommitCompletion is DialogCompletion<TResult> beforeInitialization)
            {
                DialogContent<TViewModel> ownedContent = content;
                content = null;
                return await CompleteBeforeCommitAsync(
                    registration.Key,
                    sessionId,
                    beforeInitialization.Outcome,
                    ownedContent,
                    controller).ConfigureAwait(false);
            }

            FlowContentDescriptor descriptor = new(
                sessionId,
                registration.Contract,
                content.ViewModel,
                typeof(TViewModel),
                content.Metadata);

            if (content.ViewModel is IFlowInitializable<TRequest> initializable)
            {
                await initializable.InitializeAsync(request, stageCancellationToken).ConfigureAwait(false);
            }

            preCommitCompletion =
                await GetPreCommitCompletionAsync(callerRequest, controller).ConfigureAwait(false);
            if (preCommitCompletion is DialogCompletion<TResult> afterInitialization)
            {
                DialogContent<TViewModel> ownedContent = content;
                content = null;
                return await CompleteBeforeCommitAsync(
                    registration.Key,
                    sessionId,
                    afterInitialization.Outcome,
                    ownedContent,
                    controller).ConfigureAwait(false);
            }

            if (content.ViewModel is IFlowActivation lifecycle)
            {
                await lifecycle.ActivatingAsync(
                    new FlowActivationContext(sessionId, registration.Contract),
                    stageCancellationToken).ConfigureAwait(false);
            }

            preCommitCompletion =
                await GetPreCommitCompletionAsync(callerRequest, controller).ConfigureAwait(false);
            if (preCommitCompletion is DialogCompletion<TResult> afterActivation)
            {
                DialogContent<TViewModel> ownedContent = content;
                content = null;
                return await CompleteBeforeCommitAsync(
                    registration.Key,
                    sessionId,
                    afterActivation.Outcome,
                    ownedContent,
                    controller).ConfigureAwait(false);
            }

            DialogPresentation<TResult> presentation = new(
                registration.Key,
                descriptor,
                controller,
                registration.Actions);
            controller.EnableRequests();
            lease = await _presenters.Get(registration.Presenter)
                .PresentAsync(presentation, stageCancellationToken)
                .ConfigureAwait(false);
            if (lease is null)
            {
                throw new InvalidOperationException("A dialog presenter returned a null lease.");
            }

            committed = true;

            if (content.ViewModel is IFlowActivation activatedLifecycle)
            {
                try
                {
                    await activatedLifecycle.ActivatedAsync(
                        new FlowActivationContext(sessionId, registration.Contract),
                        CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    (postCommitFailures ??= []).Add(exception);
                }
            }

            DialogCompletion<TResult> accepted = await controller.Completion.ConfigureAwait(false);
            IReadOnlyList<Exception> failures = await TeardownCommittedAsync(
                registration,
                content,
                lease,
                sessionId,
                controller,
                postCommitFailures).ConfigureAwait(false);
            lease = null;
            content = null;

            if (failures.Count > 0)
            {
                throw new DialogTeardownException<TResult>(
                    registration.Key,
                    sessionId,
                    accepted.Outcome,
                    failures);
            }

            FlowTelemetry.Outcomes.Add(1, new("flow.feature", "dialog"), new("flow.outcome", accepted.Outcome.Kind.ToString()));
            activity?.SetTag("flow.dialog.outcome", accepted.Outcome.Kind.ToString());
            return accepted.Outcome;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested && content is null)
        {
            FlowTelemetry.Outcomes.Add(
                1,
                new KeyValuePair<string, object?>("flow.feature", "dialog"),
                new KeyValuePair<string, object?>("flow.outcome", DialogOutcomeKind.Cancelled.ToString()));
            return DialogOutcome<TResult>.Cancelled();
        }
        catch (OperationCanceledException) when (callerRequest?.IsAccepted == true && content is not null)
        {
            DialogCompletion<TResult> accepted = await controller.Completion.ConfigureAwait(false);
            IReadOnlyList<Exception> cleanupFailures = committed
                ? await TeardownCommittedAsync(
                    registration,
                    content,
                    lease,
                    sessionId,
                    controller,
                    postCommitFailures).ConfigureAwait(false)
                : await TeardownPreCommitAsync(content, lease, controller).ConfigureAwait(false);
            content = null;
            lease = null;
            if (cleanupFailures.Count > 0)
            {
                throw new DialogTeardownException<TResult>(
                    registration.Key,
                    sessionId,
                    accepted.Outcome,
                    cleanupFailures);
            }

            FlowTelemetry.Outcomes.Add(
                1,
                new KeyValuePair<string, object?>("flow.feature", "dialog"),
                new KeyValuePair<string, object?>("flow.outcome", accepted.Outcome.Kind.ToString()));
            return accepted.Outcome;
        }
        catch (Exception primaryException)
        {
            FlowTelemetry.Faults.Add(
                1,
                new KeyValuePair<string, object?>("flow.feature", "dialog"));
            if (content is not null)
            {
                IReadOnlyList<Exception> cleanupFailures = committed
                    ? await TeardownCommittedAsync(
                        registration,
                        content,
                        lease,
                        sessionId,
                        controller,
                        postCommitFailures).ConfigureAwait(false)
                    : await TeardownPreCommitAsync(content, lease, controller).ConfigureAwait(false);
                content = null;
                lease = null;
                if (cleanupFailures.Count > 0)
                {
                    activity?.SetStatus(ActivityStatusCode.Error);
                    throw new FlowCleanupException(
                        $"Dialog '{registration.Key}' failed and encountered additional teardown failures.",
                        FlowFeature.Dialog,
                        cleanupFailures,
                        registration.Key.Value,
                        sessionId,
                        primaryException);
                }
            }

            throw;
        }
        finally
        {
            callerCancellation.Dispose();
            dialogCancellation?.Dispose();
            callerRequest?.Dispose();
            if (activeRegistered)
            {
                UnregisterActive(active);
            }

            active.SignalClosed();
            active.Dispose();
            FlowTelemetry.Duration.Record(
                _timeProvider.GetElapsedTime(started).TotalSeconds,
                new KeyValuePair<string, object?>("flow.feature", "dialog"));
        }
    }

    private static async ValueTask<DialogCompletion<TResult>?> GetPreCommitCompletionAsync<TResult>(
        CallerCancellationRequest<TResult> callerRequest,
        DialogController<TResult> controller)
    {
        if (!callerRequest.IsRequested)
        {
            return null;
        }

        await Task.WhenAny(callerRequest.Finished, controller.Completion).ConfigureAwait(false);
        return controller.Completion.IsCompleted
            ? await controller.Completion.ConfigureAwait(false)
            : null;
    }

    private async ValueTask<DialogOutcome<TResult>> CompleteBeforeCommitAsync<TViewModel, TResult>(
        DialogKey dialog,
        FlowSessionId sessionId,
        DialogOutcome<TResult> acceptedOutcome,
        DialogContent<TViewModel> content,
        DialogController<TResult> controller)
        where TViewModel : class
    {
        IReadOnlyList<Exception> failures = await TeardownPreCommitAsync(
            content,
            lease: null,
            controller)
            .ConfigureAwait(false);
        if (failures.Count > 0)
        {
            throw new DialogTeardownException<TResult>(
                dialog,
                sessionId,
                acceptedOutcome,
                failures);
        }

        return acceptedOutcome;
    }

    private async ValueTask<IReadOnlyList<Exception>> TeardownCommittedAsync<TViewModel, TRequest, TResult>(
        DialogRegistration<TViewModel, TRequest, TResult> registration,
        DialogContent<TViewModel> content,
        IFlowPresentationLease? lease,
        FlowSessionId sessionId,
        DialogController<TResult> controller,
        List<Exception>? priorFailures)
        where TViewModel : class
    {
        List<Exception> failures = priorFailures is null ? [] : [.. priorFailures];
        using FlowTimeoutCancellation deadline = FlowTimeout.CreateCancellationSource(
            _timeProvider,
            _teardownTimeout);
        await TryAwaitBoundedAsync(
            controller.GuardDrained,
            "dialog close guard",
            deadline,
            failures).ConfigureAwait(false);

        if (content.Children is not null)
        {
            await TryInvokeBoundedAsync(
                cancellationToken => content.Children.CloseChildrenAsync(cancellationToken),
                "child dialog cleanup",
                deadline,
                failures).ConfigureAwait(false);
        }

        FlowDeactivationContext context = new(sessionId, registration.Contract);
        if (content.ViewModel is IFlowActivation lifecycle)
        {
            await TryInvokeBoundedAsync(
                cancellationToken => lifecycle.DeactivatingAsync(context, cancellationToken),
                "dialog deactivation",
                deadline,
                failures).ConfigureAwait(false);
        }

        if (lease is not null)
        {
            await TryInvokeBoundedAsync(
                cancellationToken => lease.CloseAsync(cancellationToken),
                "presenter lease close",
                deadline,
                failures).ConfigureAwait(false);
        }

        if (content.ViewModel is IFlowActivation deactivatedLifecycle)
        {
            await TryInvokeBoundedAsync(
                cancellationToken => deactivatedLifecycle.DeactivatedAsync(context, cancellationToken),
                "dialog deactivated notification",
                deadline,
                failures).ConfigureAwait(false);
        }

        await DisposeResourcesAsync(content, lease, deadline, failures).ConfigureAwait(false);
        return failures;
    }

    private async ValueTask<IReadOnlyList<Exception>> TeardownPreCommitAsync<TViewModel, TResult>(
        DialogContent<TViewModel> content,
        IFlowPresentationLease? lease,
        DialogController<TResult> controller)
        where TViewModel : class
    {
        List<Exception> failures = [];
        using FlowTimeoutCancellation deadline = FlowTimeout.CreateCancellationSource(
            _timeProvider,
            _teardownTimeout);
        await TryAwaitBoundedAsync(
            controller.GuardDrained,
            "dialog close guard",
            deadline,
            failures).ConfigureAwait(false);
        await DisposeResourcesAsync(content, lease, deadline, failures).ConfigureAwait(false);
        return failures;
    }

    private async ValueTask DisposeResourcesAsync<TViewModel>(
        DialogContent<TViewModel> content,
        IFlowPresentationLease? lease,
        FlowTimeoutCancellation deadline,
        List<Exception> failures)
        where TViewModel : class
    {
        if (lease is not null)
        {
            await TryDisposeBoundedAsync(lease, "presenter lease disposal", deadline, failures)
                .ConfigureAwait(false);
        }

        if (content.OwnsViewModel && !ReferenceEquals(content.ViewModel, content.OwnedScope))
        {
            await TryDisposeBoundedAsync(content.ViewModel, "ViewModel disposal", deadline, failures)
                .ConfigureAwait(false);
        }

        await TryDisposeBoundedAsync(content.OwnedScope, "dialog scope disposal", deadline, failures)
            .ConfigureAwait(false);
    }

    private async ValueTask TryInvokeBoundedAsync(
        Func<CancellationToken, ValueTask> callback,
        string stage,
        FlowTimeoutCancellation deadline,
        List<Exception> failures)
    {
        if (deadline.Token.IsCancellationRequested)
        {
            AddTeardownTimeout(stage, failures);
            return;
        }

        Task operation;
        try
        {
            operation = Task.Run(
                async () => await callback(deadline.Token).ConfigureAwait(false));
        }
        catch (Exception exception)
        {
            failures.Add(exception);
            return;
        }

        await TryAwaitBoundedAsync(operation, stage, deadline, failures).ConfigureAwait(false);
    }

    private async ValueTask TryDisposeBoundedAsync(
        object resource,
        string stage,
        FlowTimeoutCancellation deadline,
        List<Exception> failures)
    {
        if (deadline.Token.IsCancellationRequested)
        {
            AddTeardownTimeout(stage, failures);
            return;
        }

        Task operation;
        try
        {
            if (resource is IAsyncDisposable asyncDisposable)
            {
                operation = Task.Run(
                    async () => await asyncDisposable.DisposeAsync().ConfigureAwait(false));
            }
            else if (resource is IDisposable disposable)
            {
                operation = Task.Run(disposable.Dispose);
            }
            else
            {
                return;
            }
        }
        catch (Exception exception)
        {
            failures.Add(exception);
            return;
        }

        await TryAwaitBoundedAsync(operation, stage, deadline, failures).ConfigureAwait(false);
    }

    private async ValueTask TryAwaitBoundedAsync(
        Task operation,
        string stage,
        FlowTimeoutCancellation deadline,
        List<Exception> failures)
    {
        if (deadline.Token.IsCancellationRequested)
        {
            operation.ObserveFault();
            AddTeardownTimeout(stage, failures);
            return;
        }

        try
        {
            await operation.WaitAsync(deadline.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (deadline.IsTimeoutCancellationRequested)
        {
            operation.ObserveFault();
            AddTeardownTimeout(stage, failures);
        }
        catch (Exception exception)
        {
            failures.Add(exception);
        }
    }

    private void AddTeardownTimeout(string stage, List<Exception> failures)
    {
        failures.Add(new TimeoutException(
            $"Dialog teardown exceeded the {_teardownTimeout} limit while waiting for {stage}."));
    }

    private bool TryRegisterActive(IActiveDialog active)
    {
        lock (_gate)
        {
            if (_shutdown)
            {
                return false;
            }

            _active.Add(active);
            return true;
        }
    }

    private void UnregisterActive(IActiveDialog active)
    {
        lock (_gate)
        {
            _active.Remove(active);
        }
    }

    private void ThrowIfShutdown()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_shutdown, this);
        }
    }

    private static TimeSpan ValidateTeardownTimeout(TimeSpan? timeout)
    {
        TimeSpan value = timeout ?? DefaultTeardownTimeout;
        if (value <= TimeSpan.Zero || value > MaximumTeardownTimeout)
        {
            throw new ArgumentOutOfRangeException(
                nameof(timeout),
                timeout,
                $"A dialog teardown timeout must be between one tick and {MaximumTeardownTimeout}.");
        }

        return value;
    }
}

/// <summary>Reports teardown failures without discarding the accepted typed dialog outcome.</summary>
/// <typeparam name="TResult">The registered dialog result type.</typeparam>
public sealed class DialogTeardownException<TResult> : FlowException
{
    /// <summary>Initializes a dialog teardown exception.</summary>
    public DialogTeardownException(
        DialogKey dialog,
        FlowSessionId sessionId,
        DialogOutcome<TResult> acceptedOutcome,
        IReadOnlyList<Exception> failures)
        : base(
            $"Dialog '{dialog}' accepted an outcome but encountered {GetFailureCount(failures)} teardown failure(s).",
            FlowFeature.Dialog,
            dialog.Value,
            sessionId,
            FlowLifecycleStage.Disposing,
            new AggregateException(failures))
    {
        AcceptedOutcome = acceptedOutcome;
        Failures = CopyFailures(failures);
    }

    /// <summary>Gets the outcome accepted before teardown began.</summary>
    public DialogOutcome<TResult> AcceptedOutcome { get; }

    /// <summary>Gets failures in observable teardown order.</summary>
    public IReadOnlyList<Exception> Failures { get; }

    private static int GetFailureCount(IReadOnlyList<Exception> failures)
    {
        ArgumentNullException.ThrowIfNull(failures);
        if (failures.Count == 0)
        {
            throw new ArgumentException("At least one teardown failure is required.", nameof(failures));
        }

        return failures.Count;
    }

    private static Exception[] CopyFailures(IReadOnlyList<Exception> failures)
    {
        Exception[] copy = new Exception[failures.Count];
        for (int index = 0; index < failures.Count; index++)
        {
            copy[index] = failures[index] ??
                throw new ArgumentException("Teardown failures cannot contain null.", nameof(failures));
        }

        return copy;
    }
}

internal interface IActiveDialog
{
    Task Closed { get; }

    ValueTask<bool> RequestShutdownAsync();
}

internal sealed class ActiveDialog<TResult> : IActiveDialog, IDisposable
{
    private readonly object _gate = new();
    private readonly DialogController<TResult> _controller;
    private readonly TaskCompletionSource _closed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly CancellationTokenSource _shutdown = new();
    private bool _shutdownRequested;
    private bool _disposeRequested;
    private bool _disposed;
    private int _cancellationUsers;

    internal ActiveDialog(DialogController<TResult> controller) => _controller = controller;

    public Task Closed => _closed.Task;

    internal CancellationToken ShutdownToken => _shutdown.Token;

    public ValueTask<bool> RequestShutdownAsync()
    {
        ValueTask<bool> accepted = _controller.RequestShutdownAsync();
        bool cancel = false;
        lock (_gate)
        {
            if (!_disposed && !_shutdownRequested)
            {
                _shutdownRequested = true;
                _cancellationUsers++;
                cancel = true;
            }
        }

        if (cancel)
        {
            _ = Task.Run(CancelShutdownToken);
        }

        return accepted;
    }

    private void CancelShutdownToken()
    {
        try
        {
            _shutdown.Cancel(throwOnFirstException: false);
        }
        catch (AggregateException)
        {
            // Cancellation callbacks are untrusted admission work. The controller's
            // accepted shutdown remains authoritative and teardown still drains.
        }
        finally
        {
            ReleaseCancellationUser();
        }
    }

    internal void SignalClosed() => _closed.TrySetResult();

    public void Dispose()
    {
        bool dispose;
        lock (_gate)
        {
            _disposeRequested = true;
            dispose = TryClaimDisposal();
        }

        if (dispose)
        {
            _shutdown.Dispose();
            _controller.Dispose();
        }
    }

    private void ReleaseCancellationUser()
    {
        bool dispose;
        lock (_gate)
        {
            _cancellationUsers--;
            dispose = TryClaimDisposal();
        }

        if (dispose)
        {
            _shutdown.Dispose();
            _controller.Dispose();
        }
    }

    private bool TryClaimDisposal()
    {
        if (_disposeRequested && !_disposed && _cancellationUsers == 0)
        {
            _disposed = true;
            return true;
        }

        return false;
    }
}

internal static class DialogTaskExtensions
{
    public static async void ObserveFault(this Task task)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        catch
        {
            // The controller's completion task carries the same fault to ShowAsync.
        }
    }
}

internal sealed class CallerCancellationRequest<TResult> : IDisposable
{
    private readonly object _gate = new();
    private readonly DialogController<TResult> _controller;
    private readonly CancellationTokenSource _acceptedCancellation = new();
    private readonly TaskCompletionSource _finished =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool _disposeRequested;
    private bool _disposed;
    private bool _running;
    private int _accepted;
    private int _requested;

    internal CallerCancellationRequest(DialogController<TResult> controller) => _controller = controller;

    internal bool IsRequested => Volatile.Read(ref _requested) != 0;

    internal bool IsAccepted => Volatile.Read(ref _accepted) != 0;

    internal CancellationToken AcceptedCancellation => _acceptedCancellation.Token;

    internal Task Finished => _finished.Task;

    internal void Request()
    {
        if (Interlocked.Exchange(ref _requested, 1) == 0)
        {
            lock (_gate)
            {
                _running = true;
            }

            RequestCoreAsync();
        }
    }

    public void Dispose()
    {
        bool dispose;
        lock (_gate)
        {
            _disposeRequested = true;
            dispose = TryClaimDisposal();
        }

        if (dispose)
        {
            _acceptedCancellation.Dispose();
        }
    }

    private async void RequestCoreAsync()
    {
        try
        {
            bool accepted = await _controller.RequestCallerCancellationAsync().ConfigureAwait(false);
            if (accepted)
            {
                Volatile.Write(ref _accepted, 1);
                try
                {
                    _acceptedCancellation.Cancel(throwOnFirstException: false);
                }
                catch (AggregateException)
                {
                    // Cancellation callback faults cannot overturn the accepted outcome.
                }
            }

            _finished.TrySetResult();
        }
        catch (Exception exception)
        {
            _finished.TrySetException(exception);
        }
        finally
        {
            bool dispose;
            lock (_gate)
            {
                _running = false;
                dispose = TryClaimDisposal();
            }

            if (dispose)
            {
                _acceptedCancellation.Dispose();
            }
        }
    }

    private bool TryClaimDisposal()
    {
        if (_disposeRequested && !_disposed && !_running)
        {
            _disposed = true;
            return true;
        }

        return false;
    }
}
