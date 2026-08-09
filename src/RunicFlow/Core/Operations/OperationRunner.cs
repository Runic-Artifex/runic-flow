using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;

namespace RunicFlow.Operations;

/// <summary>Coordinates headless operation admission, progress, cancellation, timeouts, and outcomes.</summary>
public sealed class OperationRunner : IOperationRunner, IOperationMonitor
{
    private const int MaximumBoundedValueLength = 128;
    private const string UnlinkedCancellationDataKey = "RunicFlow.UnlinkedCancellation";
    private readonly object _sync = new();
    private readonly Dictionary<OperationId, Entry> _entries = [];
    private readonly Dictionary<string, SlotCoordinator> _slots = new(StringComparer.Ordinal);
    private readonly List<IObserver<OperationSnapshot>> _observers = [];
    private readonly Queue<OperationSnapshot> _notifications = [];
    private readonly TimeProvider _timeProvider;
    private readonly int _retainedFinishedLimit;
    private bool _publishing;

    /// <summary>Initializes an operation runner.</summary>
    public OperationRunner(TimeProvider? timeProvider = null, OperationRunnerOptions? options = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
        options ??= OperationRunnerOptions.Default;
        ArgumentOutOfRangeException.ThrowIfNegative(options.RetainedFinishedOperationLimit);
        _retainedFinishedLimit = options.RetainedFinishedOperationLimit;
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
            return OperationOutcome<T>.Succeeded(
                await RunAsync(request, operation, cancellationToken).ConfigureAwait(false));
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
    public bool TryGetSnapshot(OperationId id, out OperationSnapshot? snapshot)
    {
        lock (_sync)
        {
            if (_entries.TryGetValue(id, out Entry? entry))
            {
                snapshot = entry.Snapshot;
                return true;
            }
        }

        snapshot = null;
        return false;
    }

    /// <inheritdoc />
    public bool RequestCancellation(OperationId id)
    {
        Entry entry;
        bool drain;
        lock (_sync)
        {
            if (!_entries.TryGetValue(id, out Entry? found) ||
                !found.Snapshot.CanCancel ||
                IsTerminal(found.Snapshot.State) ||
                !found.Cancellation.TryReserve(OperationCancellationReason.Requested))
            {
                return false;
            }

            entry = found;
            entry.Snapshot = entry.Snapshot with { IsCancellationRequested = true };
            drain = QueueNotification(entry.Snapshot);
        }

        entry.Cancellation.Signal();
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
        OperationId id = validated.Request.Id ?? OperationId.New();
        CancellationReasonTracker reason = new();
        using Entry entry = new(
            new OperationSnapshot(
                id,
                validated.Request.Key,
                OperationState.Queued,
                _timeProvider.GetUtcNow(),
                validated.Request.Concurrency,
                validated.Slot,
                validated.Request.CanCancel,
                validated.Request.CorrelationId),
            reason);
        using CancellationTokenSource? timeout = validated.Request.Timeout is { } duration
            ? new CancellationTokenSource(duration, _timeProvider)
            : null;
        using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(
            callerToken,
            entry.Cancellation.Token,
            timeout?.Token ?? default);
        using CancellationTokenRegistration callerRegistration = callerToken.Register(
            static state => ((CancellationReasonTracker)state!).TrySet(OperationCancellationReason.Caller),
            reason);
        using CancellationTokenRegistration timeoutRegistration = timeout?.Token.Register(
            static state => ((CancellationReasonTracker)state!).TrySet(OperationCancellationReason.Timeout),
            reason) ?? default;
        AddEntry(entry);

        SlotAdmission? admission = null;
        Exception? failure = null;
        T? result = default;
        try
        {
            admission = await AcquireSlotAsync(validated, entry, linked.Token).ConfigureAwait(false);
            linked.Token.ThrowIfCancellationRequested();
            Update(entry, snapshot => snapshot with
            {
                State = OperationState.Running,
                StartedAt = _timeProvider.GetUtcNow(),
            });
            var context = new OperationContext(id, validated.Request, progress => ReportProgress(entry, progress));
            result = await operation(context, linked.Token).ConfigureAwait(false);
            Update(entry, snapshot => snapshot with
            {
                State = OperationState.Succeeded,
                Outcome = OperationOutcomeKind.Succeeded,
                CompletedAt = _timeProvider.GetUtcNow(),
            });
        }
        catch (OperationCanceledException exception) when (linked.IsCancellationRequested)
        {
            failure = exception;
            OperationCancellationReason observed = reason.Reason == OperationCancellationReason.None
                ? OperationCancellationReason.Caller
                : reason.Reason;
            Update(entry, snapshot => snapshot with
            {
                State = OperationState.Cancelled,
                Outcome = OperationOutcomeKind.Cancelled,
                CompletedAt = _timeProvider.GetUtcNow(),
                IsCancellationRequested = true,
                CancellationReason = observed,
            });
        }
        catch (Exception exception)
        {
            if (exception is OperationCanceledException)
            {
                exception.Data[UnlinkedCancellationDataKey] = true;
            }

            failure = exception;
            Update(entry, snapshot => snapshot with
            {
                State = OperationState.Faulted,
                Outcome = OperationOutcomeKind.Faulted,
                CompletedAt = _timeProvider.GetUtcNow(),
            });
        }
        finally
        {
            admission?.Dispose();
            Finish(entry);
        }

        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
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

        SlotAcquireResult result;
        lock (_sync)
        {
            if (!_slots.TryGetValue(validated.Slot, out SlotCoordinator? coordinator))
            {
                coordinator = new SlotCoordinator(validated.Slot);
                _slots.Add(validated.Slot, coordinator);
            }

            result = coordinator.Acquire(
                validated.Request,
                entry.Cancellation,
                OnSlotEmpty,
                cancellationToken);
        }

        foreach (SlotAdmission current in result.CancelAfterLocks)
        {
            current.RequestCancellation();
        }

        return await result.Admission.ConfigureAwait(false);
    }

    private void OnSlotEmpty(SlotCoordinator coordinator)
    {
        lock (_sync)
        {
            if (coordinator.IsEmpty &&
                _slots.TryGetValue(coordinator.Name, out SlotCoordinator? current) &&
                ReferenceEquals(current, coordinator))
            {
                _slots.Remove(coordinator.Name);
            }
        }
    }

    private void ReportProgress(Entry entry, OperationProgress progress)
    {
        if (progress.Fraction is { } fraction && (!double.IsFinite(fraction) || fraction is < 0 or > 1))
        {
            throw new ArgumentOutOfRangeException(nameof(progress), "Progress must be finite and between zero and one.");
        }

        bool drain;
        lock (_sync)
        {
            if (entry.Snapshot.State != OperationState.Running)
            {
                throw new InvalidOperationException("Progress can be reported only while operation work is running.");
            }

            entry.Snapshot = entry.Snapshot with { Progress = progress };
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
            if (!_entries.TryAdd(entry.Snapshot.Id, entry))
            {
                throw new InvalidOperationException($"Operation '{entry.Snapshot.Id}' is already registered.");
            }

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

    private void Finish(Entry entry)
    {
        lock (_sync)
        {
            OperationSnapshot[] terminal = _entries.Values
                .Select(static candidate => candidate.Snapshot)
                .Where(static snapshot => IsTerminal(snapshot.State))
                .OrderByDescending(static snapshot => snapshot.CompletedAt)
                .ThenByDescending(static snapshot => snapshot.Id.Value)
                .ToArray();
            for (int index = _retainedFinishedLimit; index < terminal.Length; index++)
            {
                _entries.Remove(terminal[index].Id);
            }
        }
    }

    private bool QueueNotification(OperationSnapshot snapshot)
    {
        _notifications.Enqueue(snapshot);
        if (_publishing)
        {
            return false;
        }

        _publishing = true;
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
                if (_notifications.Count == 0)
                {
                    _publishing = false;
                    return;
                }

                snapshot = _notifications.Dequeue();
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
                    // Observation cannot influence operation semantics.
                }
            }
        }
    }

    private static ValidatedRequest Validate(OperationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrEmpty(request.Key.Value))
        {
            throw new ArgumentException("The operation key must be initialized.", nameof(request));
        }

        if (!Enum.IsDefined(request.Concurrency))
        {
            throw new ArgumentOutOfRangeException(nameof(request));
        }

        string? slot = string.IsNullOrEmpty(request.Slot) ? null : request.Slot;
        ValidateBounded(slot, nameof(request.Slot));
        ValidateBounded(request.CorrelationId, nameof(request.CorrelationId));
        if (request.Concurrency != OperationConcurrency.Allow && slot is null)
        {
            throw new ArgumentException("A coordinated concurrency policy requires a slot.", nameof(request));
        }

        if (request.Timeout is { } timeout && timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(request));
        }

        return new ValidatedRequest(request, slot);
    }

    private static void ValidateBounded(string? value, string parameterName)
    {
        if (value is null)
        {
            return;
        }

        if (value.Length > MaximumBoundedValueLength ||
            !string.Equals(value, value.Trim(), StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"The value must be trimmed and no longer than {MaximumBoundedValueLength} characters.",
                parameterName);
        }
    }

    private void Unsubscribe(IObserver<OperationSnapshot> observer)
    {
        lock (_sync)
        {
            _observers.Remove(observer);
        }
    }

    private static bool IsTerminal(OperationState state) =>
        state is OperationState.Succeeded or OperationState.Cancelled or OperationState.Faulted;

    private sealed class Entry(OperationSnapshot snapshot, CancellationReasonTracker reason) : IDisposable
    {
        public OperationSnapshot Snapshot { get; set; } = snapshot;

        public OperationCancellation Cancellation { get; } = new(reason.TrySet);

        public void Dispose() => Cancellation.Dispose();
    }

    private sealed class Subscription(OperationRunner owner, IObserver<OperationSnapshot> observer) : IDisposable
    {
        private OperationRunner? _owner = owner;

        public void Dispose() => Interlocked.Exchange(ref _owner, null)?.Unsubscribe(observer);
    }

    private sealed record ValidatedRequest(OperationRequest Request, string? Slot);

    private sealed class CancellationReasonTracker
    {
        private int _reason;

        public OperationCancellationReason Reason => (OperationCancellationReason)Volatile.Read(ref _reason);

        public void TrySet(OperationCancellationReason reason) =>
            Interlocked.CompareExchange(ref _reason, (int)reason, (int)OperationCancellationReason.None);
    }
}
