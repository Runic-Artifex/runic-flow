using System;
using System.Collections.Generic;
using RunicFlow;

namespace RunicFlow.Operations;

/// <summary>Identifies the observable execution phase.</summary>
public enum OperationState
{
    /// <summary>The operation is waiting for admission.</summary>
    Queued,
    /// <summary>The operation delegate is running.</summary>
    Running,
    /// <summary>The operation completed successfully.</summary>
    Succeeded,
    /// <summary>The operation observed coordinated cancellation.</summary>
    Cancelled,
    /// <summary>The operation failed.</summary>
    Faulted,
}

/// <summary>Identifies the coordinated cancellation source.</summary>
public enum OperationCancellationReason
{
    /// <summary>No cancellation was observed.</summary>
    None,
    /// <summary>The caller cancelled.</summary>
    Caller,
    /// <summary>A monitor consumer requested cancellation.</summary>
    Requested,
    /// <summary>The configured timeout elapsed.</summary>
    Timeout,
    /// <summary>A newer operation replaced this slot owner.</summary>
    Replaced,
}

/// <summary>Provides an immutable, exception-free operation observation.</summary>
public sealed record OperationSnapshot(
    OperationId Id,
    OperationKey Key,
    OperationState State,
    DateTimeOffset QueuedAt,
    OperationConcurrency Concurrency,
    string? Slot,
    bool CanCancel,
    string? CorrelationId,
    DateTimeOffset? StartedAt = null,
    DateTimeOffset? CompletedAt = null,
    OperationProgress? Progress = null,
    OperationOutcomeKind? Outcome = null,
    bool IsCancellationRequested = false,
    OperationCancellationReason CancellationReason = OperationCancellationReason.None);

/// <summary>Observes operations and requests cooperative cancellation.</summary>
public interface IOperationMonitor : IObservable<OperationSnapshot>
{
    /// <summary>Gets an enqueue-ordered point-in-time snapshot.</summary>
    IReadOnlyList<OperationSnapshot> GetSnapshots();

    /// <summary>Gets one operation when it is retained.</summary>
    bool TryGetSnapshot(OperationId id, out OperationSnapshot? snapshot);

    /// <summary>Requests cancellation when the operation permits it.</summary>
    bool RequestCancellation(OperationId id);
}

/// <summary>Controls bounded retention of terminal operations.</summary>
public sealed record OperationRunnerOptions
{
    /// <summary>Gets the default options.</summary>
    public static OperationRunnerOptions Default { get; } = new();

    /// <summary>Gets the maximum terminal snapshots retained by the monitor.</summary>
    public int RetainedFinishedOperationLimit { get; init; } = 128;
}

/// <summary>Indicates that a reject-policy operation could not acquire its slot.</summary>
public sealed class OperationBusyException : InvalidOperationException
{
    /// <summary>Initializes the exception.</summary>
    public OperationBusyException(string message, OperationKey operation, string slot)
        : base(message)
    {
        Operation = operation;
        Slot = slot;
    }

    /// <summary>Gets the rejected operation kind.</summary>
    public OperationKey Operation { get; }

    /// <summary>Gets the occupied slot.</summary>
    public string Slot { get; }
}
