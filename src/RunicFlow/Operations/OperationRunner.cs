using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using RunicFlow;

namespace RunicFlow.Operations;

/// <summary>BCL-first operation runner and immutable monitor.</summary>
public sealed class OperationRunner : IOperationRunner, IOperationMonitor
{
    private const int MaximumMetadataLength = 1024;
    private const int MaximumBoundedValueLength = 128;
    private static readonly TimeSpan DefaultCleanupTimeout = TimeSpan.FromSeconds(30);
    private const string CleanupExceptionDataKey = "RunicFlow.CleanupException";
    private const string UnlinkedCancellationDataKey = "RunicFlow.UnlinkedCancellation";
    private readonly object _sync = new();
    private readonly Dictionary<OperationId, Entry> _entries = [];
    private readonly Dictionary<string, SlotCoordinator> _slots = new(StringComparer.Ordinal);
    private readonly List<IObserver<OperationSnapshot>> _observers = [];
    private readonly Queue<OperationSnapshot> _pendingNotifications = [];
    private readonly TimeProvider _timeProvider;
    private readonly IOperationPresenterRegistry? _presenters;
    private readonly int _retainedFinishedLimit;
    private readonly TimeSpan? _cleanupTimeout;
    private bool _isPublishing;

    /// <summary>Initializes an operation runner.</summary>
    public OperationRunner(
        TimeProvider? timeProvider = null,
        IOperationPresenterRegistry? presenters = null,
        OperationRunnerOptions? options = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
        _presenters = presenters;
        options ??= OperationRunnerOptions.Default;
        ArgumentOutOfRangeException.ThrowIfNegative(options.RetainedFinishedOperationLimit, nameof(options));
        if (options.CleanupTimeout is { } cleanupTimeout)
        {
            ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(cleanupTimeout, TimeSpan.Zero, nameof(options));
        }

        _retainedFinishedLimit = options.RetainedFinishedOperationLimit;
        _cleanupTimeout = options.CleanupTimeout;
    }

    /// <inheritdoc />
    public async ValueTask<T> RunAsync<T>(
        OperationRequest request,
        Func<OperationContext, CancellationToken, ValueTask<T>> operation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ValidatedRequest validated = Validate(request);
        return await RunCoreAsync(validated, operation, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask<OperationOutcome<T>> TryRunAsync<T>(
        OperationRequest request,
        Func<OperationContext, CancellationToken, ValueTask<T>> operation,
        CancellationToken cancellationToken = default)
    {
        try
        {
            T result = await RunAsync(request, operation, cancellationToken).ConfigureAwait(false);
            return OperationOutcome<T>.Succeeded(result);
        }
        catch (OperationCanceledException exception) when (!exception.Data.Contains(UnlinkedCancellationDataKey))
        {
            return OperationOutcome<T>.Cancelled();
        }
        catch (Exception exception)
        {
            return OperationOutcome<T>.Faulted(exception);
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<OperationSnapshot> GetSnapshots()
    {
        lock (_sync)
        {
            return new ReadOnlyCollection<OperationSnapshot>(
                _entries.Values.Select(static entry => entry.Snapshot)
                    .OrderBy(static snapshot => snapshot.QueuedAt)
                    .ThenBy(static snapshot => snapshot.Id.Value)
                    .ToArray());
        }
    }

    /// <inheritdoc />
    public bool RequestCancellation(OperationId id)
    {
        Entry entry;
        bool drain;
        lock (_sync)
        {
            if (!_entries.TryGetValue(id, out Entry? found) || !found.Snapshot.CanCancel || found.IsFinished ||
                !found.ExecutionCancellation.TryReserve(OperationCancellationReason.Requested))
            {
                return false;
            }

            entry = found;
            entry.Snapshot = entry.Snapshot with { IsCancellationRequested = true };
            drain = QueueNotification(entry.Snapshot);
        }

        entry.ExecutionCancellation.Signal();
        if (drain)
        {
            DrainNotifications();
        }

        return true;
    }

    /// <inheritdoc />
    public IDisposable Subscribe(IObserver<OperationSnapshot> observer)
    {
        ArgumentNullException.ThrowIfNull(observer);
        lock (_sync)
        {
            _observers.Add(observer);
        }

        return new Subscription(this, observer);
    }

    private async ValueTask<T> RunCoreAsync<T>(
        ValidatedRequest validated,
        Func<OperationContext, CancellationToken, ValueTask<T>> operation,
        CancellationToken callerToken)
    {
        OperationId id = OperationId.Create();
        DateTimeOffset queuedAt = _timeProvider.GetUtcNow();
        using CancellationTokenSource? timeoutCancellation = CreateTimeout(validated.Request.Timeout);
        CancellationReasonTracker cancellationReason = new();
        OperationSnapshot initial = new(
            id,
            validated.Request.Key,
            OperationState.Queued,
            queuedAt,
            validated.Request.Title,
            validated.Request.Message,
            validated.Request.Presenter,
            validated.Request.Concurrency,
            validated.Slot,
            validated.Request.CanCancel,
            validated.Request.CorrelationId);
        using Entry entry = new(initial, cancellationReason);
        using CancellationTokenSource linkedCancellation = CreateLinkedCancellation(
            callerToken,
            entry.ExecutionCancellation.Token,
            timeoutCancellation?.Token ?? default);
        using CancellationTokenRegistration callerCancellationRegistration = callerToken.Register(
            static state => ((CancellationReasonTracker)state!).TrySet(OperationCancellationReason.Caller),
            cancellationReason);
        using CancellationTokenRegistration timeoutCancellationRegistration = timeoutCancellation?.Token.Register(
            static state => ((CancellationReasonTracker)state!).TrySet(OperationCancellationReason.Timeout),
            cancellationReason) ?? default;
        AddEntry(entry);

        SlotAdmission? admission = null;
        IFlowPresentationLease? presentationLease = null;
        Exception? primaryFailure = null;
        OperationOutcomeKind outcome = OperationOutcomeKind.Faulted;
        bool userWorkInvoked = false;
        T? result = default;

        try
        {
            admission = await AcquireSlotAsync(validated, entry, linkedCancellation.Token).ConfigureAwait(false);
            Update(entry, snapshot => snapshot with { State = OperationState.Starting });
            linkedCancellation.Token.ThrowIfCancellationRequested();

            if (validated.Presenter is not null)
            {
                try
                {
                    presentationLease = await validated.Presenter.ShowAsync(entry.Snapshot, linkedCancellation.Token)
                        .ConfigureAwait(false);
                    if (presentationLease is null)
                    {
                        throw new InvalidOperationException("An operation presenter returned a null lease.");
                    }
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    throw new FlowPresenterException(
                        $"Presenter '{validated.Request.Presenter}' failed to show operation '{validated.Request.Key}'.",
                        FlowFeature.Operation,
                        FlowLifecycleStage.Presenting,
                        validated.Request.Key.Value,
                        innerException: exception);
                }
            }

            linkedCancellation.Token.ThrowIfCancellationRequested();
            DateTimeOffset startedAt = _timeProvider.GetUtcNow();
            Update(entry, snapshot => snapshot with { State = OperationState.Running, StartedAt = startedAt });
            OperationContext context = new(id, validated.Request, progress => ReportProgress(entry, progress));
            userWorkInvoked = true;
            result = await operation(context, linkedCancellation.Token).ConfigureAwait(false);
            outcome = OperationOutcomeKind.Succeeded;
            Update(entry, snapshot => snapshot with
            {
                State = OperationState.Succeeded,
                Outcome = outcome,
                CompletedAt = _timeProvider.GetUtcNow(),
            });
        }
        catch (OperationCanceledException exception) when (linkedCancellation.IsCancellationRequested)
        {
            primaryFailure = exception;
            outcome = OperationOutcomeKind.Cancelled;
            OperationCancellationReason observedReason = cancellationReason.Reason == OperationCancellationReason.None
                ? OperationCancellationReason.Caller
                : cancellationReason.Reason;
            Update(entry, snapshot => snapshot with
            {
                State = OperationState.Cancelled,
                Outcome = outcome,
                CompletedAt = _timeProvider.GetUtcNow(),
                IsCancellationRequested = true,
                CancellationReason = observedReason,
            });
        }
        catch (Exception exception)
        {
            if (exception is OperationCanceledException)
            {
                exception.Data[UnlinkedCancellationDataKey] = true;
            }

            primaryFailure = exception;
            outcome = OperationOutcomeKind.Faulted;
            Update(entry, snapshot => snapshot with
            {
                State = OperationState.Faulted,
                Outcome = outcome,
                CompletedAt = _timeProvider.GetUtcNow(),
            });
        }

        List<Exception> cleanupFailures = [];
        if (presentationLease is not null)
        {
            TimeSpan cleanupTimeout = _cleanupTimeout ?? validated.Request.Timeout ?? DefaultCleanupTimeout;
            using CancellationTokenSource cleanupCancellation = new(cleanupTimeout, _timeProvider);
            CleanupAttemptResult closeResult = await TryCleanupAsync(
                () => presentationLease.CloseAsync(cleanupCancellation.Token),
                "close",
                cleanupTimeout,
                cleanupFailures,
                cleanupCancellation.Token).ConfigureAwait(false);
            if (closeResult != CleanupAttemptResult.TimedOut)
            {
                _ = await TryCleanupAsync(
                    presentationLease.DisposeAsync,
                    "dispose",
                    cleanupTimeout,
                    cleanupFailures,
                    cleanupCancellation.Token).ConfigureAwait(false);
            }
        }

        Finish(entry, outcome);
        admission?.Dispose();

        if (cleanupFailures.Count > 0)
        {
            FlowCleanupException cleanupException = new(
                $"Operation '{validated.Request.Key}' encountered {cleanupFailures.Count} cleanup failure(s).",
                FlowFeature.Operation,
                cleanupFailures,
                validated.Request.Key.Value,
                primaryException: primaryFailure);
            if (primaryFailure is null)
            {
                throw cleanupException;
            }

            primaryFailure.Data[CleanupExceptionDataKey] = cleanupException;
        }

        if (primaryFailure is not null)
        {
            ExceptionDispatchInfo.Capture(primaryFailure).Throw();
        }

        if (!userWorkInvoked)
        {
            throw new InvalidOperationException("The operation completed without invoking user work.");
        }

        return result!;
    }

    private async ValueTask<SlotAdmission?> AcquireSlotAsync(
        ValidatedRequest validated,
        Entry entry,
        CancellationToken cancellationToken)
    {
        if (validated.Slot is null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return null;
        }

        SlotAcquireResult acquireResult;
        lock (_sync)
        {
            SlotCoordinator coordinator;
            if (!_slots.TryGetValue(validated.Slot, out coordinator!))
            {
                coordinator = new SlotCoordinator(validated.Slot);
                _slots.Add(validated.Slot, coordinator);
            }

            // Admission starts while the registry lock is held so an empty coordinator
            // cannot be removed between lookup and the coordinator's own state change.
            acquireResult = coordinator.Acquire(
                validated.Request,
                entry.ExecutionCancellation,
                OnSlotEmpty,
                cancellationToken);
        }

        foreach (SlotAdmission current in acquireResult.CancelAfterLocks)
        {
            current.RequestCancellation();
        }

        return await acquireResult.Admission.ConfigureAwait(false);
    }

    private void OnSlotEmpty(SlotCoordinator coordinator)
    {
        lock (_sync)
        {
            if (coordinator.IsEmpty && _slots.TryGetValue(coordinator.Name, out SlotCoordinator? current) &&
                ReferenceEquals(current, coordinator))
            {
                _slots.Remove(coordinator.Name);
            }
        }
    }

    private void ReportProgress(Entry entry, OperationProgress progress)
    {
        OperationProgress immutable = ValidateProgress(progress);
        bool drain;
        lock (_sync)
        {
            if (entry.IsFinished || entry.Snapshot.State != OperationState.Running)
            {
                throw new InvalidOperationException("Progress can be reported only while operation work is running.");
            }

            entry.Snapshot = entry.Snapshot with { Progress = immutable };
            drain = QueueNotification(entry.Snapshot);
        }

        if (drain)
        {
            DrainNotifications();
        }
    }

    private void AddEntry(Entry entry)
    {
        bool drain;
        lock (_sync)
        {
            _entries.Add(entry.Snapshot.Id, entry);
            drain = QueueNotification(entry.Snapshot);
        }

        if (drain)
        {
            DrainNotifications();
        }
    }

    private void Update(Entry entry, Func<OperationSnapshot, OperationSnapshot> update)
    {
        bool drain;
        lock (_sync)
        {
            entry.Snapshot = update(entry.Snapshot);
            drain = QueueNotification(entry.Snapshot);
        }

        if (drain)
        {
            DrainNotifications();
        }
    }

    private void Finish(Entry entry, OperationOutcomeKind outcome)
    {
        bool drain;
        lock (_sync)
        {
            entry.IsFinished = true;
            entry.Snapshot = entry.Snapshot with
            {
                State = OperationState.Finished,
                Outcome = outcome,
                CompletedAt = entry.Snapshot.CompletedAt ?? _timeProvider.GetUtcNow(),
            };
            PruneFinishedEntries();
            drain = QueueNotification(entry.Snapshot);
        }

        if (drain)
        {
            DrainNotifications();
        }
    }

    private void PruneFinishedEntries()
    {
        Entry[] finished = _entries.Values.Where(static entry => entry.IsFinished)
            .OrderByDescending(static entry => entry.Snapshot.CompletedAt)
            .ThenByDescending(static entry => entry.Snapshot.Id.Value)
            .ToArray();
        for (int index = _retainedFinishedLimit; index < finished.Length; index++)
        {
            _entries.Remove(finished[index].Snapshot.Id);
        }
    }

    private bool QueueNotification(OperationSnapshot snapshot)
    {
        _pendingNotifications.Enqueue(snapshot);
        if (_isPublishing)
        {
            return false;
        }

        _isPublishing = true;
        return true;
    }

    private void DrainNotifications()
    {
        while (true)
        {
            OperationSnapshot snapshot;
            IObserver<OperationSnapshot>[] observers;
            lock (_sync)
            {
                if (_pendingNotifications.Count == 0)
                {
                    _isPublishing = false;
                    return;
                }

                snapshot = _pendingNotifications.Dequeue();
                observers = [.. _observers];
            }

            foreach (IObserver<OperationSnapshot> observer in observers)
            {
                try
                {
                    observer.OnNext(snapshot);
                }
                catch
                {
                    // Monitor consumers cannot change operation state or outcome.
                }
            }
        }
    }

    private ValidatedRequest Validate(OperationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrEmpty(request.Key.Value))
        {
            throw new ArgumentException("The operation key must be initialized.", nameof(request));
        }

        if (!Enum.IsDefined(request.Concurrency))
        {
            throw new ArgumentOutOfRangeException(nameof(request), "The operation concurrency value is invalid.");
        }

        ValidateMetadata(request.Title, nameof(request.Title), MaximumMetadataLength);
        ValidateMetadata(request.Message, nameof(request.Message), MaximumMetadataLength);
        ValidateMetadata(request.CorrelationId, nameof(request.CorrelationId), MaximumBoundedValueLength);
        string? slot = request.Slot;
        if (slot is not null)
        {
            if (slot.Length == 0)
            {
                slot = null;
            }
            else
            {
                ValidateMetadata(slot, nameof(request.Slot), MaximumBoundedValueLength);
            }
        }

        if (request.Timeout is { } timeout && timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(request), "The operation timeout must be positive.");
        }

        IOperationPresenter? presenter = null;
        if (request.Presenter is { } presenterKey && string.IsNullOrEmpty(presenterKey.Value))
        {
            throw new ArgumentException("The presenter key must be initialized.", nameof(request));
        }

        if (request.Presenter is { } presenterValue &&
            (_presenters is null || !_presenters.TryGetPresenter(presenterValue, out presenter) || presenter is null))
        {
            throw new FlowValidationException(
                $"Operation presenter '{presenterValue}' is not registered.",
                presenterValue.Value);
        }

        return new ValidatedRequest(request, slot, presenter);
    }

    private static void ValidateMetadata(string? value, string parameterName, int maximumLength)
    {
        if (value is null)
        {
            return;
        }

        if (value.Length > maximumLength)
        {
            throw new ArgumentException($"The value cannot exceed {maximumLength} characters.", parameterName);
        }

        if (!string.Equals(value, value.Trim(), StringComparison.Ordinal))
        {
            throw new ArgumentException("The value cannot have leading or trailing whitespace.", parameterName);
        }
    }

    private static OperationProgress ValidateProgress(OperationProgress progress)
    {
        ValidateFraction(progress.Fraction, nameof(progress));
        ValidateMetadata(progress.Message, nameof(progress), MaximumMetadataLength);
        if (!Enum.IsDefined(progress.Tone))
        {
            throw new ArgumentOutOfRangeException(nameof(progress), "The progress tone is invalid.");
        }

        if (progress.Segments is null)
        {
            return progress;
        }

        OperationSegment[] segments = new OperationSegment[progress.Segments.Count];
        for (int index = 0; index < segments.Length; index++)
        {
            OperationSegment segment = progress.Segments[index] ??
                throw new ArgumentException("Progress segments cannot contain null.", nameof(progress));
            ArgumentException.ThrowIfNullOrWhiteSpace(segment.Name);
            ValidateMetadata(segment.Name, nameof(progress), MaximumBoundedValueLength);
            ValidateMetadata(segment.Message, nameof(progress), MaximumMetadataLength);
            ValidateFraction(segment.Fraction, nameof(progress));
            if (!Enum.IsDefined(segment.Tone))
            {
                throw new ArgumentOutOfRangeException(nameof(progress), "A progress segment tone is invalid.");
            }

            segments[index] = segment;
        }

        return progress with { Segments = new ReadOnlyCollection<OperationSegment>(segments) };
    }

    private static void ValidateFraction(double? fraction, string parameterName)
    {
        if (fraction is { } value && (!double.IsFinite(value) || value < 0 || value > 1))
        {
            throw new ArgumentOutOfRangeException(parameterName, "Progress fractions must be finite and between zero and one.");
        }
    }

    private static async ValueTask<CleanupAttemptResult> TryCleanupAsync(
        Func<ValueTask> cleanup,
        string operation,
        TimeSpan timeout,
        List<Exception> failures,
        CancellationToken cleanupToken)
    {
        Task? cleanupTask = null;
        try
        {
            ValueTask pending = cleanup();
            if (pending.IsCompletedSuccessfully)
            {
                pending.GetAwaiter().GetResult();
            }
            else
            {
                cleanupTask = pending.AsTask();
                await cleanupTask.WaitAsync(cleanupToken).ConfigureAwait(false);
            }

            return CleanupAttemptResult.Completed;
        }
        catch (OperationCanceledException exception) when (cleanupToken.IsCancellationRequested)
        {
            if (cleanupTask is not null)
            {
                ObserveLateFault(cleanupTask);
            }

            failures.Add(new TimeoutException(
                $"Operation presenter {operation} exceeded the cleanup budget of {timeout}.",
                exception));
            return CleanupAttemptResult.TimedOut;
        }
        catch (Exception exception)
        {
            failures.Add(exception);
            return CleanupAttemptResult.Faulted;
        }
    }

    private static void ObserveLateFault(Task cleanupTask)
    {
        _ = cleanupTask.ContinueWith(
            static completed => _ = completed.Exception,
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private CancellationTokenSource? CreateTimeout(TimeSpan? timeout) =>
        timeout is { } value ? new CancellationTokenSource(value, _timeProvider) : null;

    private static CancellationTokenSource CreateLinkedCancellation(
        CancellationToken caller,
        CancellationToken requested,
        CancellationToken timeout) =>
        CancellationTokenSource.CreateLinkedTokenSource(caller, requested, timeout);

    private void Unsubscribe(IObserver<OperationSnapshot> observer)
    {
        lock (_sync)
        {
            _observers.Remove(observer);
        }
    }

    private sealed class Entry(OperationSnapshot snapshot, CancellationReasonTracker cancellationReason) : IDisposable
    {
        public OperationSnapshot Snapshot { get; set; } = snapshot;
        public OperationCancellation ExecutionCancellation { get; } = new(cancellationReason.TrySet);
        public bool IsFinished { get; set; }

        public void Dispose() => ExecutionCancellation.Dispose();
    }

    private sealed class Subscription(OperationRunner owner, IObserver<OperationSnapshot> observer) : IDisposable
    {
        private OperationRunner? _owner = owner;

        public void Dispose() => Interlocked.Exchange(ref _owner, null)?.Unsubscribe(observer);
    }

    private sealed record ValidatedRequest(OperationRequest Request, string? Slot, IOperationPresenter? Presenter);

    private enum CleanupAttemptResult
    {
        Completed,
        Faulted,
        TimedOut,
    }

    private sealed class CancellationReasonTracker
    {
        private int _reason;

        public OperationCancellationReason Reason => (OperationCancellationReason)Volatile.Read(ref _reason);

        public void TrySet(OperationCancellationReason reason) =>
            Interlocked.CompareExchange(ref _reason, (int)reason, (int)OperationCancellationReason.None);
    }
}
