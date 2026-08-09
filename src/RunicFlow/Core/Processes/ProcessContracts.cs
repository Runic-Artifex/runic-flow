using System;
using System.Threading;
using System.Threading.Tasks;
using RunicFlow;

namespace RunicFlow.Processes;

/// <summary>Identifies one long-lived process instance.</summary>
public readonly record struct ProcessId
{
    /// <summary>Initializes a process identifier.</summary>
    public ProcessId(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("A process identifier cannot be empty.", nameof(value));
        }

        Value = value;
    }

    /// <summary>Gets the identifier value.</summary>
    public Guid Value { get; }

    /// <summary>Creates a new process identifier.</summary>
    public static ProcessId New() => new(Guid.NewGuid());

    /// <inheritdoc />
    public override string ToString() => Value.ToString("D");
}

/// <summary>Identifies whether a process accepts more commands.</summary>
public enum ProcessStatus
{
    /// <summary>The process accepts commands.</summary>
    Active,
    /// <summary>The process completed with a typed result.</summary>
    Completed,
    /// <summary>The process was cancelled at an application-defined safe point.</summary>
    Cancelled,
}

/// <summary>Identifies the decision returned by a process command handler.</summary>
public enum ProcessDecisionKind
{
    /// <summary>Accept the command and commit its next state.</summary>
    Accept,
    /// <summary>Reject the command without changing state.</summary>
    Reject,
    /// <summary>Commit the next state and complete the process.</summary>
    Complete,
    /// <summary>Commit the next state and cancel the process.</summary>
    Cancel,
}

/// <summary>Represents the immutable decision produced by one command handler.</summary>
public sealed record ProcessDecision<TState, TResult>
{
    private ProcessDecision(ProcessDecisionKind kind, TState? state, TResult? result, string? reason)
    {
        Kind = kind;
        State = state;
        Result = result;
        Reason = reason;
    }

    /// <summary>Gets the decision kind.</summary>
    public ProcessDecisionKind Kind { get; }

    /// <summary>Gets the state committed by an accepted or terminal decision.</summary>
    public TState? State { get; }

    /// <summary>Gets the typed completion result when <see cref="Kind"/> is <see cref="ProcessDecisionKind.Complete"/>.</summary>
    public TResult? Result { get; }

    /// <summary>Gets the bounded application reason for a rejection.</summary>
    public string? Reason { get; }

    /// <summary>Accepts a command and commits its next state.</summary>
    public static ProcessDecision<TState, TResult> Accept(TState state) =>
        new(ProcessDecisionKind.Accept, state, default, null);

    /// <summary>Rejects a command without changing state.</summary>
    public static ProcessDecision<TState, TResult> Reject(string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        if (reason.Length > 1024)
        {
            throw new ArgumentException("A process rejection reason cannot exceed 1024 characters.", nameof(reason));
        }

        return new(ProcessDecisionKind.Reject, default, default, reason);
    }

    /// <summary>Commits a final state and typed result.</summary>
    public static ProcessDecision<TState, TResult> Complete(TState state, TResult result) =>
        new(ProcessDecisionKind.Complete, state, result, null);

    /// <summary>Commits a final cancelled state.</summary>
    public static ProcessDecision<TState, TResult> Cancel(TState state) =>
        new(ProcessDecisionKind.Cancel, state, default, null);
}

/// <summary>Provides stable process metadata and the current state to a command handler.</summary>
public sealed record ProcessCommandContext<TState>(
    ProcessId ProcessId,
    ProcessKey Process,
    int SchemaVersion,
    long Version,
    TState State);

/// <summary>Handles one application-defined command without knowing about presentation or transport.</summary>
public delegate ValueTask<ProcessDecision<TState, TResult>> ProcessCommandHandler<TState, in TCommand, TResult>(
    ProcessCommandContext<TState> context,
    TCommand command,
    CancellationToken cancellationToken);

/// <summary>Defines one closed, headless application process.</summary>
public sealed class ProcessDefinition<TState, TCommand, TResult>
{
    /// <summary>Initializes a process definition.</summary>
    public ProcessDefinition(
        ProcessKey key,
        int schemaVersion,
        ProcessCommandHandler<TState, TCommand, TResult> handler)
    {
        if (string.IsNullOrEmpty(key.Value))
        {
            throw new ArgumentException("A process key must be initialized.", nameof(key));
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(schemaVersion);
        Key = key;
        SchemaVersion = schemaVersion;
        Handler = handler ?? throw new ArgumentNullException(nameof(handler));
    }

    /// <summary>Gets the stable process identity.</summary>
    public ProcessKey Key { get; }

    /// <summary>Gets the consumer-owned checkpoint schema version.</summary>
    public int SchemaVersion { get; }

    internal ProcessCommandHandler<TState, TCommand, TResult> Handler { get; }
}

/// <summary>Identifies the observable outcome of dispatching a process command.</summary>
public enum ProcessTransitionKind
{
    /// <summary>The process committed a new active state.</summary>
    Accepted,
    /// <summary>The application handler rejected the command.</summary>
    Rejected,
    /// <summary>The caller supplied a stale process version.</summary>
    Stale,
    /// <summary>The process completed with a typed result.</summary>
    Completed,
    /// <summary>The process committed cancellation.</summary>
    Cancelled,
    /// <summary>The process was already terminal.</summary>
    Terminal,
}
