using System;

namespace RunicFlow.Processes;

/// <summary>Provides an immutable observation of one process instance.</summary>
public sealed record ProcessSnapshot<TState, TResult>(
    ProcessId Id,
    ProcessKey Process,
    int SchemaVersion,
    long Version,
    ProcessStatus Status,
    TState State,
    TResult? Result,
    DateTimeOffset UpdatedAt);

/// <summary>Reports one completed command dispatch and its authoritative process snapshot.</summary>
public sealed record ProcessTransition<TState, TResult>(
    ProcessTransitionKind Kind,
    ProcessSnapshot<TState, TResult> Snapshot,
    string? Reason = null);

/// <summary>Provides the old and committed process snapshots after a mutation.</summary>
public sealed class ProcessSnapshotChangedEventArgs<TState, TResult> : EventArgs
{
    /// <summary>Initializes snapshot-change arguments.</summary>
    public ProcessSnapshotChangedEventArgs(
        ProcessSnapshot<TState, TResult> previous,
        ProcessSnapshot<TState, TResult> current)
    {
        Previous = previous ?? throw new ArgumentNullException(nameof(previous));
        Current = current ?? throw new ArgumentNullException(nameof(current));
    }

    /// <summary>Gets the snapshot before the commit.</summary>
    public ProcessSnapshot<TState, TResult> Previous { get; }

    /// <summary>Gets the committed snapshot.</summary>
    public ProcessSnapshot<TState, TResult> Current { get; }
}
