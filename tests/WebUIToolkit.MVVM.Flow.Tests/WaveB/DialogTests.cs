using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WebUIToolkit.MVVM.Dialogs;
using WebUIToolkit.MVVM.Flow;

namespace WebUIToolkit.MVVM.Flow.Tests.WaveB;

internal static class DialogTests
{
    private static readonly string[] ExpectedTeardownTrace =
    [
        "initialize:payload",
        "activating",
        "present",
        "activated",
        "children",
        "deactivating",
        "lease-close",
        "deactivated",
        "lease-dispose",
        "viewmodel-dispose",
        "scope-dispose",
    ];

    public static async ValueTask ConcurrentCompletionSelectsExactlyOneOutcome()
    {
        var presenter = new RecordingDialogPresenter();
        var scope = new RecordingScope();
        DialogService service = CreateService<PlainDialogViewModel>(
            presenter,
            (_, _, _) => ValueTask.FromResult(
                new DialogContent<PlainDialogViewModel>(new PlainDialogViewModel(), scope)));
        await using (service)
        {
            Task<DialogOutcome<string?>> show = service
                .ShowAsync<PlainDialogViewModel, string, string?>("request")
                .AsTask();
            await presenter.Opened.Task.ConfigureAwait(false);
            IDialogController<string?> controller = presenter.GetController<string?>();

            Task<bool>[] racers = new Task<bool>[96];
            for (int index = 0; index < racers.Length; index++)
            {
                int contestant = index;
                racers[index] = Task.Run(async () => (contestant % 3) switch
                {
                    0 => await controller.CompleteAsync(null),
                    1 => await controller.CancelAsync(),
                    _ => await controller.DismissAsync(),
                });
            }

            bool[] claims = await Task.WhenAll(racers).ConfigureAwait(false);
            DialogOutcome<string?> outcome = await show.ConfigureAwait(false);
            TestAssert.Equal(1, claims.Count(static claimed => claimed));
            TestAssert.True(Enum.IsDefined(outcome.Kind));
            TestAssert.Equal(1, presenter.Lease!.CloseCount);
            TestAssert.Equal(1, presenter.Lease.DisposeCount);
            TestAssert.Equal(1, scope.DisposeCount);
        }
    }

    public static async ValueTask GuardDenialReleasesClaimForRetry()
    {
        var presenter = new RecordingDialogPresenter();
        var guarded = new GuardedDialogViewModel(false, true);
        DialogService service = CreateService<GuardedDialogViewModel>(
            presenter,
            (_, _, _) => ValueTask.FromResult(
                new DialogContent<GuardedDialogViewModel>(guarded, new RecordingScope())));
        await using (service)
        {
            Task<DialogOutcome<string?>> show = service
                .ShowAsync<GuardedDialogViewModel, string, string?>("request")
                .AsTask();
            await presenter.Opened.Task.ConfigureAwait(false);
            IDialogController<string?> controller = presenter.GetController<string?>();

            TestAssert.False(await controller.CompleteAsync("first"));
            TestAssert.False(controller.IsCompletionRequested);
            TestAssert.False(show.IsCompleted);
            TestAssert.True(await controller.CompleteAsync("second"));

            DialogOutcome<string?> outcome = await show.ConfigureAwait(false);
            TestAssert.Equal(DialogOutcomeKind.Completed, outcome.Kind);
            TestAssert.Equal("second", outcome.Value);
            TestAssert.Equal(2, guarded.GuardCalls);
        }
    }

    public static async ValueTask CallerCancellationRequestsOutcomeAndStillCleansUp()
    {
        var presenter = new RecordingDialogPresenter();
        var scope = new RecordingScope();
        DialogService service = CreateService<PlainDialogViewModel>(
            presenter,
            (_, _, _) => ValueTask.FromResult(
                new DialogContent<PlainDialogViewModel>(new PlainDialogViewModel(), scope)));
        await using (service)
        using (var cancellation = new CancellationTokenSource())
        {
            Task<DialogOutcome<string?>> show = service
                .ShowAsync<PlainDialogViewModel, string, string?>("request", cancellation.Token)
                .AsTask();
            await presenter.Opened.Task.ConfigureAwait(false);
            cancellation.Cancel();

            DialogOutcome<string?> outcome = await show.ConfigureAwait(false);
            TestAssert.Equal(DialogOutcomeKind.Cancelled, outcome.Kind);
            TestAssert.Equal(1, presenter.Lease!.CloseCount);
            TestAssert.False(presenter.Lease.CloseObservedCancellation);
            TestAssert.Equal(1, scope.DisposeCount);
        }
    }

    public static async ValueTask TeardownOrderIsDeterministic()
    {
        var trace = new List<string>();
        var presenter = new RecordingDialogPresenter(trace: trace);
        var viewModel = new LifecycleDialogViewModel(trace);
        var children = new RecordingChildren(trace);
        var scope = new RecordingScope(() => trace.Add("scope-dispose"));
        DialogService service = CreateService<LifecycleDialogViewModel>(
            presenter,
            (_, _, _) => ValueTask.FromResult(
                new DialogContent<LifecycleDialogViewModel>(
                    viewModel,
                    scope,
                    ownsViewModel: true,
                    children: children)));
        await using (service)
        {
            Task<DialogOutcome<string?>> show = service
                .ShowAsync<LifecycleDialogViewModel, string, string?>("payload")
                .AsTask();
            await presenter.Opened.Task.ConfigureAwait(false);
            TestAssert.True(await presenter.GetController<string?>().CompleteAsync(null));
            DialogOutcome<string?> outcome = await show.ConfigureAwait(false);

            TestAssert.Equal(DialogOutcomeKind.Completed, outcome.Kind);
            TestAssert.SequenceEqual(ExpectedTeardownTrace, trace);
        }
    }

    public static async ValueTask AcceptedOutcomeSurvivesCloseFailureMetadata()
    {
        var closeFailure = new InvalidOperationException("close failed");
        var presenter = new RecordingDialogPresenter(closeFailure);
        DialogService service = CreateService<PlainDialogViewModel>(
            presenter,
            (_, _, _) => ValueTask.FromResult(
                new DialogContent<PlainDialogViewModel>(
                    new PlainDialogViewModel(),
                    new RecordingScope())));
        await using (service)
        {
            Task<DialogOutcome<string?>> show = service
                .ShowAsync<PlainDialogViewModel, string, string?>("request")
                .AsTask();
            await presenter.Opened.Task.ConfigureAwait(false);
            TestAssert.True(await presenter.GetController<string?>().CompleteAsync("accepted"));

            DialogTeardownException<string?> exception =
                await TestAssert.ThrowsAsync<DialogTeardownException<string?>>(async () =>
                    _ = await show.ConfigureAwait(false));
            TestAssert.Equal(DialogOutcomeKind.Completed, exception.AcceptedOutcome.Kind);
            TestAssert.Equal("accepted", exception.AcceptedOutcome.Value);
            TestAssert.Equal(1, exception.Failures.Count);
            TestAssert.True(ReferenceEquals(closeFailure, exception.Failures[0]));
            TestAssert.Equal(1, presenter.Lease!.DisposeCount);
        }
    }

    public static async ValueTask PresenterOpenFailureDisposesPreCommitContent()
    {
        var openFailure = new InvalidOperationException("open failed");
        var presenter = new RecordingDialogPresenter(openFailure: openFailure);
        var scope = new RecordingScope();
        bool workAfterPresentation = false;
        DialogService service = CreateService<PlainDialogViewModel>(
            presenter,
            (_, controller, _) =>
            {
                workAfterPresentation = controller.IsCompletionRequested;
                return ValueTask.FromResult(
                    new DialogContent<PlainDialogViewModel>(new PlainDialogViewModel(), scope));
            });
        await using (service)
        {
            InvalidOperationException actual = await TestAssert.ThrowsAsync<InvalidOperationException>(async () =>
                _ = await service.ShowAsync<PlainDialogViewModel, string, string?>("request"));
            TestAssert.True(ReferenceEquals(openFailure, actual));
            TestAssert.False(workAfterPresentation);
            TestAssert.Equal(1, scope.DisposeCount);
        }
    }

    public static async ValueTask ShutdownBypassesGuardAndRejectsNewDialogs()
    {
        var presenter = new RecordingDialogPresenter();
        var guarded = new GuardedDialogViewModel(false);
        DialogService service = CreateService<GuardedDialogViewModel>(
            presenter,
            (_, _, _) => ValueTask.FromResult(
                new DialogContent<GuardedDialogViewModel>(guarded, new RecordingScope())));
        await using (service)
        {
            Task<DialogOutcome<string?>> show = service
                .ShowAsync<GuardedDialogViewModel, string, string?>("request")
                .AsTask();
            await presenter.Opened.Task.ConfigureAwait(false);

            await service.ShutdownAsync();
            DialogOutcome<string?> outcome = await show.ConfigureAwait(false);
            TestAssert.Equal(DialogOutcomeKind.Cancelled, outcome.Kind);
            TestAssert.Equal(0, guarded.GuardCalls);
            _ = await TestAssert.ThrowsAsync<ObjectDisposedException>(async () =>
                _ = await service.ShowAsync<GuardedDialogViewModel, string, string?>("again"));
        }
    }

    public static async ValueTask ShutdownWaitsForPendingGuardBeforeDisposal()
    {
        var presenter = new RecordingDialogPresenter();
        var guarded = new BlockingGuardDialogViewModel();
        DialogService service = CreateService<BlockingGuardDialogViewModel>(
            presenter,
            (_, _, _) => ValueTask.FromResult(
                new DialogContent<BlockingGuardDialogViewModel>(
                    guarded,
                    new RecordingScope(),
                    ownsViewModel: true)));
        await using (service)
        {
            Task<DialogOutcome<string?>> show = service
                .ShowAsync<BlockingGuardDialogViewModel, string, string?>("request")
                .AsTask();
            await presenter.Opened.Task.ConfigureAwait(false);
            Task<bool> pendingGuard = presenter.GetController<string?>().CancelAsync().AsTask();
            await guarded.GuardEntered.Task.ConfigureAwait(false);

            Task shutdown = service.ShutdownAsync().AsTask();
            await guarded.GuardCancelled.Task.ConfigureAwait(false);
            bool disposedWhileGuardRunning = guarded.IsDisposed;
            bool shutdownCompletedWhileGuardRunning = shutdown.IsCompleted;
            guarded.ReleaseGuard.TrySetResult();
            TestAssert.False(await pendingGuard.ConfigureAwait(false));
            await shutdown.ConfigureAwait(false);
            DialogOutcome<string?> outcome = await show.ConfigureAwait(false);
            TestAssert.False(
                disposedWhileGuardRunning,
                "Shutdown disposed a ViewModel while its close guard was running.");
            TestAssert.False(
                shutdownCompletedWhileGuardRunning,
                "Shutdown must drain the in-flight close guard before teardown.");
            TestAssert.Equal(DialogOutcomeKind.Cancelled, outcome.Kind);
            TestAssert.True(guarded.IsDisposed);
        }
    }

    public static async ValueTask CallerCancellationDuringInitializationReturnsOutcomeAfterCleanup()
    {
        var presenter = new RecordingDialogPresenter();
        var viewModel = new BlockingInitializeDialogViewModel();
        var scope = new RecordingScope();
        DialogService service = CreateService<BlockingInitializeDialogViewModel>(
            presenter,
            (_, _, _) => ValueTask.FromResult(
                new DialogContent<BlockingInitializeDialogViewModel>(viewModel, scope)));
        await using (service)
        using (var cancellation = new CancellationTokenSource())
        {
            Task<DialogOutcome<string?>> show = service
                .ShowAsync<BlockingInitializeDialogViewModel, string, string?>(
                    "request",
                    cancellation.Token)
                .AsTask();
            await viewModel.InitializeStarted.Task.ConfigureAwait(false);
            cancellation.Cancel();

            DialogOutcome<string?> outcome = await show.ConfigureAwait(false);
            TestAssert.Equal(DialogOutcomeKind.Cancelled, outcome.Kind);
            TestAssert.Equal(1, scope.DisposeCount);
            TestAssert.Equal(0, presenter.Lease?.CloseCount ?? 0);
        }
    }

    public static ValueTask RegistrationRejectsAmbiguousActions()
    {
        PresenterKey presenter = new("modal");
        FlowAction first = new(new ActionKey("one"), "One", isDefault: true);
        FlowAction second = new(new ActionKey("two"), "Two", isDefault: true);
        TestAssert.True(Throws<FlowRegistrationException>(() =>
            _ = new DialogRegistration<PlainDialogViewModel, string, string?>(
                new DialogKey("ambiguous"),
                new ViewContract("dialog/ambiguous"),
                presenter,
                static (_, _, _) => ValueTask.FromResult(
                    new DialogContent<PlainDialogViewModel>(
                        new PlainDialogViewModel(),
                        new RecordingScope())),
                [first, second])));
        return ValueTask.CompletedTask;
    }

    public static ValueTask RegistrationRejectsDefaultLogicalIdentities()
    {
        TestAssert.True(RejectsInvalidIdentity(() =>
            _ = CreateRegistration(default, new ViewContract("dialog/test"), new PresenterKey("modal"))));
        TestAssert.True(RejectsInvalidIdentity(() =>
            _ = CreateRegistration(new DialogKey("test"), default, new PresenterKey("modal"))));
        TestAssert.True(RejectsInvalidIdentity(() =>
            _ = CreateRegistration(new DialogKey("test"), new ViewContract("dialog/test"), default)));
        return ValueTask.CompletedTask;
    }

    public static async ValueTask HungLeaseCloseTimesOutWithAcceptedOutcome()
    {
        TimeSpan timeout = TimeSpan.FromMinutes(4);
        var clock = new ManualTimeProvider(new DateTimeOffset(2040, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var presenter = new HangingCloseDialogPresenter();
        var scope = new RecordingScope();
        DialogService service = CreateService<PlainDialogViewModel>(
            presenter,
            (_, _, _) => ValueTask.FromResult(
                new DialogContent<PlainDialogViewModel>(new PlainDialogViewModel(), scope)),
            clock,
            timeout);
        await using (service)
        {
            Task<DialogOutcome<string?>> show = service
                .ShowAsync<PlainDialogViewModel, string, string?>("request")
                .AsTask();
            await presenter.Opened.Task.ConfigureAwait(false);
            TestAssert.True(await presenter.GetController<string?>().CompleteAsync("accepted"));
            await presenter.Lease.CloseStarted.Task.ConfigureAwait(false);
            TestAssert.False(show.IsCompleted);

            clock.Advance(timeout);
            DialogTeardownException<string?> exception =
                await TestAssert.ThrowsAsync<DialogTeardownException<string?>>(async () =>
                    _ = await show.ConfigureAwait(false));
            TestAssert.Equal(DialogOutcomeKind.Completed, exception.AcceptedOutcome.Kind);
            TestAssert.Equal("accepted", exception.AcceptedOutcome.Value);
            TestAssert.True(exception.Failures[0] is TimeoutException);
            TestAssert.Equal(0, presenter.Lease.DisposeCount);
            TestAssert.Equal(0, scope.DisposeCount);
            await service.ShutdownAsync();
        }
    }

    private static DialogService CreateService<TViewModel>(
        RecordingDialogPresenter presenter,
        DialogContentFactory<TViewModel, string, string?> factory,
        TimeProvider? timeProvider = null,
        TimeSpan? teardownTimeout = null)
        where TViewModel : class
    {
        PresenterKey presenterKey = new("modal");
        DialogRegistry dialogs = new DialogRegistryBuilder()
            .Add(new DialogRegistration<TViewModel, string, string?>(
                new DialogKey("test"),
                new ViewContract("dialog/test"),
                presenterKey,
                factory))
            .Build();
        var presenters = new DialogPresenterRegistry(
            new Dictionary<PresenterKey, IDialogPresenter> { [presenterKey] = presenter });
        return new DialogService(dialogs, presenters, timeProvider, teardownTimeout);
    }

    private static DialogService CreateService<TViewModel>(
        HangingCloseDialogPresenter presenter,
        DialogContentFactory<TViewModel, string, string?> factory,
        TimeProvider timeProvider,
        TimeSpan teardownTimeout)
        where TViewModel : class
    {
        PresenterKey presenterKey = new("modal");
        DialogRegistry dialogs = new DialogRegistryBuilder()
            .Add(new DialogRegistration<TViewModel, string, string?>(
                new DialogKey("test"),
                new ViewContract("dialog/test"),
                presenterKey,
                factory))
            .Build();
        var presenters = new DialogPresenterRegistry(
            new Dictionary<PresenterKey, IDialogPresenter> { [presenterKey] = presenter });
        return new DialogService(dialogs, presenters, timeProvider, teardownTimeout);
    }

    private static DialogRegistration<PlainDialogViewModel, string, string?> CreateRegistration(
        DialogKey key,
        ViewContract contract,
        PresenterKey presenter) =>
        new(
            key,
            contract,
            presenter,
            static (_, _, _) => ValueTask.FromResult(
                new DialogContent<PlainDialogViewModel>(
                    new PlainDialogViewModel(),
                    new RecordingScope())));

    private static bool RejectsInvalidIdentity(Action action)
    {
        try
        {
            action();
            return false;
        }
        catch (ArgumentException)
        {
            return true;
        }
        catch (FlowRegistrationException)
        {
            return true;
        }
    }

    private static bool Throws<TException>(Action action)
        where TException : Exception
    {
        try
        {
            action();
            return false;
        }
        catch (TException)
        {
            return true;
        }
    }

    private sealed class PlainDialogViewModel;

    private sealed class GuardedDialogViewModel(params bool[] decisions) : IDialogCloseGuard<string?>
    {
        private int _index;

        public int GuardCalls => Volatile.Read(ref _index);

        public ValueTask<bool> CanCloseAsync(
            DialogCloseGuardContext<string?> context,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int index = Interlocked.Increment(ref _index) - 1;
            return ValueTask.FromResult(index < decisions.Length && decisions[index]);
        }
    }

    private sealed class BlockingGuardDialogViewModel : IDialogCloseGuard<string?>, IAsyncDisposable
    {
        private int _disposed;

        public TaskCompletionSource GuardEntered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseGuard { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource GuardCancelled { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool IsDisposed => Volatile.Read(ref _disposed) != 0;

        public async ValueTask<bool> CanCloseAsync(
            DialogCloseGuardContext<string?> context,
            CancellationToken cancellationToken)
        {
            GuardEntered.TrySetResult();
            using CancellationTokenRegistration registration = cancellationToken.Register(
                static state => ((TaskCompletionSource)state!).TrySetResult(),
                GuardCancelled);
            await ReleaseGuard.Task.ConfigureAwait(false);
            TestAssert.False(IsDisposed, "The guard resumed after its ViewModel was disposed.");
            return true;
        }

        public ValueTask DisposeAsync()
        {
            Volatile.Write(ref _disposed, 1);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class BlockingInitializeDialogViewModel : IFlowInitializable<string>
    {
        public TaskCompletionSource InitializeStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async ValueTask InitializeAsync(string parameter, CancellationToken cancellationToken)
        {
            InitializeStarted.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
        }
    }

    private sealed class LifecycleDialogViewModel(List<string> trace)
        : IFlowInitializable<string>, IFlowActivation, IAsyncDisposable
    {
        public ValueTask InitializeAsync(string parameter, CancellationToken cancellationToken)
        {
            trace.Add($"initialize:{parameter}");
            return ValueTask.CompletedTask;
        }

        public ValueTask ActivatingAsync(
            FlowActivationContext context,
            CancellationToken cancellationToken)
        {
            trace.Add("activating");
            return ValueTask.CompletedTask;
        }

        public ValueTask ActivatedAsync(
            FlowActivationContext context,
            CancellationToken cancellationToken)
        {
            trace.Add("activated");
            return ValueTask.CompletedTask;
        }

        public ValueTask DeactivatingAsync(
            FlowDeactivationContext context,
            CancellationToken cancellationToken)
        {
            trace.Add("deactivating");
            return ValueTask.CompletedTask;
        }

        public ValueTask DeactivatedAsync(
            FlowDeactivationContext context,
            CancellationToken cancellationToken)
        {
            trace.Add("deactivated");
            return ValueTask.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            trace.Add("viewmodel-dispose");
            return ValueTask.CompletedTask;
        }
    }

    private sealed class RecordingChildren(List<string> trace) : IDialogChildOwner
    {
        public ValueTask CloseChildrenAsync(CancellationToken cancellationToken)
        {
            trace.Add("children");
            return ValueTask.CompletedTask;
        }
    }

    private sealed class RecordingScope(Action? onDispose = null) : IAsyncDisposable
    {
        private int _disposeCount;

        public int DisposeCount => Volatile.Read(ref _disposeCount);

        public ValueTask DisposeAsync()
        {
            if (Interlocked.Increment(ref _disposeCount) == 1)
            {
                onDispose?.Invoke();
            }

            return ValueTask.CompletedTask;
        }
    }

    private sealed class RecordingDialogPresenter(
        Exception? closeFailure = null,
        Exception? openFailure = null,
        List<string>? trace = null) : IDialogPresenter
    {
        private object? _controller;

        public TaskCompletionSource Opened { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public RecordingLease? Lease { get; private set; }

        public ValueTask<IFlowPresentationLease> PresentAsync<TResult>(
            DialogPresentation<TResult> presentation,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (openFailure is not null)
            {
                Opened.TrySetResult();
                return ValueTask.FromException<IFlowPresentationLease>(openFailure);
            }

            _controller = presentation.Controller;
            trace?.Add("present");
            Lease = new RecordingLease(closeFailure, trace);
            Opened.TrySetResult();
            return ValueTask.FromResult<IFlowPresentationLease>(Lease);
        }

        public IDialogController<TResult> GetController<TResult>() =>
            (IDialogController<TResult>)(_controller ??
                throw new InvalidOperationException("The dialog is not open."));
    }

    private sealed class HangingCloseDialogPresenter : IDialogPresenter
    {
        private object? _controller;
        public TaskCompletionSource Opened { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public HangingDialogLease Lease { get; } = new();

        public ValueTask<IFlowPresentationLease> PresentAsync<TResult>(
            DialogPresentation<TResult> presentation,
            CancellationToken cancellationToken)
        {
            _controller = presentation.Controller;
            Opened.TrySetResult();
            return ValueTask.FromResult<IFlowPresentationLease>(Lease);
        }

        public IDialogController<TResult> GetController<TResult>() =>
            (IDialogController<TResult>)(_controller ??
                throw new InvalidOperationException("The dialog is not open."));
    }

    private sealed class HangingDialogLease : IFlowPresentationLease
    {
        private readonly TaskCompletionSource _never =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource CloseStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int DisposeCount { get; private set; }

        public ValueTask CloseAsync(CancellationToken cancellationToken)
        {
            CloseStarted.TrySetResult();
            return new ValueTask(_never.Task);
        }

        public ValueTask DisposeAsync()
        {
            DisposeCount++;
            return new ValueTask(_never.Task);
        }
    }

    private sealed class RecordingLease(Exception? closeFailure, List<string>? trace) : IFlowPresentationLease
    {
        public int CloseCount { get; private set; }
        public int DisposeCount { get; private set; }
        public bool CloseObservedCancellation { get; private set; }

        public ValueTask CloseAsync(CancellationToken cancellationToken)
        {
            CloseCount++;
            CloseObservedCancellation = cancellationToken.IsCancellationRequested;
            trace?.Add("lease-close");
            return closeFailure is null
                ? ValueTask.CompletedTask
                : ValueTask.FromException(closeFailure);
        }

        public ValueTask DisposeAsync()
        {
            DisposeCount++;
            trace?.Add("lease-dispose");
            return ValueTask.CompletedTask;
        }
    }
}
