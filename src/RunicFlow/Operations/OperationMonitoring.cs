using System;
using System.Collections.Generic;
using RunicFlow;

namespace RunicFlow.Operations;

/// <summary>Identifies the observable execution phase of an operation.</summary>
public enum OperationState
{
    /// <summary>The operation is waiting for slot admission.</summary>
    Queued,
    /// <summary>The operation has slot admission and is preparing presentation.</summary>
    Starting,
    /// <summary>The user delegate is running.</summary>
    Running,
    /// <summary>The delegate completed successfully.</summary>
    Succeeded,
    /// <summary>The operation observed runner-controlled cancellation.</summary>
    Cancelled,
    /// <summary>The operation failed.</summary>
    Faulted,
    /// <summary>All cleanup attempts finished.</summary>
    Finished,
}

/// <summary>Identifies the runner-controlled reason an operation was cancelled.</summary>
public enum OperationCancellationReason
{
    /// <summary>No cancellation was observed.</summary>
    None,
    /// <summary>The caller token requested cancellation.</summary>
    Caller,
    /// <summary>A monitor consumer requested cancellation.</summary>
    Requested,
    /// <summary>The configured test-clock-aware timeout elapsed.</summary>
    Timeout,
    /// <summary>A cancel-previous operation requested replacement.</summary>
    Replaced,
}

/// <summary>Provides an immutable, exception-free view of an operation invocation.</summary>
public sealed record OperationSnapshot
{
    /// <summary>Initializes an operation snapshot.</summary>
    public OperationSnapshot(
        OperationId id,
        OperationKey key,
        OperationState state,
        DateTimeOffset queuedAt,
        string? title = null,
        string? message = null,
        PresenterKey? presenter = null,
        OperationConcurrency concurrency = OperationConcurrency.Allow,
        string? slot = null,
        bool canCancel = true,
        string? correlationId = null,
        DateTimeOffset? startedAt = null,
        DateTimeOffset? completedAt = null,
        OperationProgress? progress = null,
        OperationOutcomeKind? outcome = null,
        bool isCancellationRequested = false,
        OperationCancellationReason cancellationReason = OperationCancellationReason.None)
    {
        Id = id;
        Key = key;
        State = state;
        QueuedAt = queuedAt;
        Title = title;
        Message = message;
        Presenter = presenter;
        Concurrency = concurrency;
        Slot = slot;
        CanCancel = canCancel;
        CorrelationId = correlationId;
        StartedAt = startedAt;
        CompletedAt = completedAt;
        Progress = progress;
        Outcome = outcome;
        IsCancellationRequested = isCancellationRequested;
        CancellationReason = cancellationReason;
    }

    /// <summary>Gets the unique invocation identifier.</summary>
    public OperationId Id { get; }
    /// <summary>Gets the logical operation key.</summary>
    public OperationKey Key { get; }
    /// <summary>Gets the current observable state.</summary>
    public OperationState State { get; init; }
    /// <summary>Gets the enqueue timestamp from the configured clock.</summary>
    public DateTimeOffset QueuedAt { get; }
    /// <summary>Gets consumer-provided display metadata.</summary>
    public string? Title { get; }
    /// <summary>Gets consumer-provided status metadata.</summary>
    public string? Message { get; }
    /// <summary>Gets the requested presenter key.</summary>
    public PresenterKey? Presenter { get; }
    /// <summary>Gets the slot concurrency behavior.</summary>
    public OperationConcurrency Concurrency { get; }
    /// <summary>Gets the bounded slot.</summary>
    public string? Slot { get; }
    /// <summary>Gets whether monitor-initiated cancellation is available.</summary>
    public bool CanCancel { get; }
    /// <summary>Gets the bounded consumer correlation value.</summary>
    public string? CorrelationId { get; }
    /// <summary>Gets the work-start timestamp.</summary>
    public DateTimeOffset? StartedAt { get; init; }
    /// <summary>Gets the terminal timestamp.</summary>
    public DateTimeOffset? CompletedAt { get; init; }
    /// <summary>Gets the latest immutable progress value.</summary>
    public OperationProgress? Progress { get; init; }
    /// <summary>Gets the terminal outcome after one has been selected.</summary>
    public OperationOutcomeKind? Outcome { get; init; }
    /// <summary>Gets whether cancellation has been requested.</summary>
    public bool IsCancellationRequested { get; init; }
    /// <summary>Gets the deterministic cancellation source once cancellation was observed.</summary>
    public OperationCancellationReason CancellationReason { get; init; }
}

/// <summary>Observes immutable operation state and requests cooperative cancellation.</summary>
public interface IOperationMonitor : IObservable<OperationSnapshot>
{
    /// <summary>Gets an immutable point-in-time copy ordered by enqueue time.</summary>
    IReadOnlyList<OperationSnapshot> GetSnapshots();

    /// <summary>Requests cancellation when the operation advertised that capability.</summary>
    bool RequestCancellation(OperationId id);
}

/// <summary>Controls bounded retention of finished monitor entries.</summary>
public sealed record OperationRunnerOptions
{
    /// <summary>Gets the default options.</summary>
    public static OperationRunnerOptions Default { get; } = new();

    /// <summary>Gets the maximum number of finished snapshots retained by the monitor.</summary>
    public int RetainedFinishedOperationLimit { get; init; } = 128;

    /// <summary>
    /// Gets an optional total presenter close/disposal budget. When omitted, a request
    /// timeout is reused; requests without one use the runner's bounded default.
    /// </summary>
    public TimeSpan? CleanupTimeout { get; init; }
}
