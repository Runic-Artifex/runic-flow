using System;
using System.Threading;
using System.Threading.Tasks;

namespace RunicFlow.Processes;

/// <summary>
/// Serializes application commands and commits immutable process snapshots without owning UI or transport state.
/// </summary>
public sealed class ProcessSession<TState, TCommand, TResult> : IAsyncDisposable
{
    private static readonly AsyncLocal<ProcessSession<TState, TCommand, TResult>?> ExecutingSession = new();
    private readonly ProcessDefinition<TState, TCommand, TResult> _definition;
    private readonly SemaphoreSlim _mutation = new(1, 1);
    private readonly TimeProvider _timeProvider;
    private readonly object _snapshotGate = new();
    private ProcessSnapshot<TState, TResult> _snapshot;
    private int _disposed;

    /// <summary>Creates an active process with version zero.</summary>
    public ProcessSession(
        ProcessDefinition<TState, TCommand, TResult> definition,
        TState initialState,
        TimeProvider? timeProvider = null)
        : this(
            definition,
            new ProcessSnapshot<TState, TResult>(
                ProcessId.New(),
                definition?.Key ?? default,
                definition?.SchemaVersion ?? 0,
                0,
                ProcessStatus.Active,
                initialState,
                default,
                (timeProvider ?? TimeProvider.System).GetUtcNow()),
            timeProvider)
    {
    }

    internal ProcessSession(
        ProcessDefinition<TState, TCommand, TResult> definition,
        ProcessSnapshot<TState, TResult> snapshot,
        TimeProvider? timeProvider = null)
    {
        _definition = definition ?? throw new ArgumentNullException(nameof(definition));
        _snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
        _timeProvider = timeProvider ?? TimeProvider.System;
        ValidateSnapshot(snapshot, definition);
    }

    /// <summary>Occurs after an accepted or terminal decision commits.</summary>
    public event EventHandler<ProcessSnapshotChangedEventArgs<TState, TResult>>? SnapshotChanged;

    /// <summary>Gets the latest immutable process snapshot.</summary>
    public ProcessSnapshot<TState, TResult> Snapshot
    {
        get
        {
            lock (_snapshotGate)
            {
                return _snapshot;
            }
        }
    }

    /// <summary>Dispatches one application command with an optional process-local stale-version guard.</summary>
    public async ValueTask<ProcessTransition<TState, TResult>> DispatchAsync(
        TCommand command,
        long? expectedVersion = null,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        if (ReferenceEquals(ExecutingSession.Value, this))
        {
            throw new InvalidOperationException("A process command handler cannot dispatch recursively into its own session.");
        }

        await _mutation.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            ProcessSnapshot<TState, TResult> current = Snapshot;
            if (current.Status != ProcessStatus.Active)
            {
                return new ProcessTransition<TState, TResult>(ProcessTransitionKind.Terminal, current);
            }

            if (expectedVersion is long expected && expected != current.Version)
            {
                return new ProcessTransition<TState, TResult>(
                    ProcessTransitionKind.Stale,
                    current,
                    "The command was based on a stale process version.");
            }

            ProcessDecision<TState, TResult> decision;
            ProcessSession<TState, TCommand, TResult>? previousSession = ExecutingSession.Value;
            ExecutingSession.Value = this;
            try
            {
                decision = await _definition.Handler(
                    new ProcessCommandContext<TState>(
                        current.Id,
                        current.Process,
                        current.SchemaVersion,
                        current.Version,
                        current.State),
                    command,
                    cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                ExecutingSession.Value = previousSession;
            }

            if (decision is null)
            {
                throw new InvalidOperationException("A process command handler returned no decision.");
            }

            if (decision.Kind == ProcessDecisionKind.Reject)
            {
                return new ProcessTransition<TState, TResult>(
                    ProcessTransitionKind.Rejected,
                    current,
                    decision.Reason);
            }

            ProcessStatus status = decision.Kind switch
            {
                ProcessDecisionKind.Accept => ProcessStatus.Active,
                ProcessDecisionKind.Complete => ProcessStatus.Completed,
                ProcessDecisionKind.Cancel => ProcessStatus.Cancelled,
                _ => throw new InvalidOperationException("The process decision kind is invalid."),
            };
            TState nextState = decision.State!;
            var next = current with
            {
                Version = checked(current.Version + 1),
                Status = status,
                State = nextState,
                Result = decision.Kind == ProcessDecisionKind.Complete ? decision.Result : default,
                UpdatedAt = _timeProvider.GetUtcNow(),
            };
            lock (_snapshotGate)
            {
                _snapshot = next;
            }

            SnapshotChanged?.Invoke(this, new ProcessSnapshotChangedEventArgs<TState, TResult>(current, next));
            ProcessTransitionKind kind = decision.Kind switch
            {
                ProcessDecisionKind.Accept => ProcessTransitionKind.Accepted,
                ProcessDecisionKind.Complete => ProcessTransitionKind.Completed,
                ProcessDecisionKind.Cancel => ProcessTransitionKind.Cancelled,
                _ => throw new InvalidOperationException("The process decision kind is invalid."),
            };
            return new ProcessTransition<TState, TResult>(kind, next);
        }
        finally
        {
            _mutation.Release();
        }
    }

    /// <summary>Stops accepting commands after all currently executing handler work has left the session.</summary>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        await _mutation.WaitAsync().ConfigureAwait(false);
        _mutation.Release();
        _mutation.Dispose();
    }

    private static void ValidateSnapshot(
        ProcessSnapshot<TState, TResult> snapshot,
        ProcessDefinition<TState, TCommand, TResult> definition)
    {
        if (snapshot.Id.Value == Guid.Empty ||
            snapshot.Process != definition.Key ||
            snapshot.SchemaVersion != definition.SchemaVersion ||
            snapshot.Version < 0 ||
            !Enum.IsDefined(snapshot.Status))
        {
            throw new ArgumentException("The process snapshot is incompatible with the definition.", nameof(snapshot));
        }
    }
}
