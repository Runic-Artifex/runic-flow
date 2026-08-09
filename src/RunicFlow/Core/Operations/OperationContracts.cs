using System;
using System.Threading;
using System.Threading.Tasks;
using RunicFlow;

namespace RunicFlow.Operations;

/// <summary>Controls admission when operations share a logical slot.</summary>
public enum OperationConcurrency
{
    /// <summary>Runs without coordinating with other operations.</summary>
    Allow,
    /// <summary>Rejects while another operation occupies the slot.</summary>
    Reject,
    /// <summary>Waits for the slot.</summary>
    Queue,
    /// <summary>Cancels current slot owners and waits for their teardown.</summary>
    CancelPrevious,
}

/// <summary>Identifies one operation invocation.</summary>
public readonly record struct OperationId
{
    /// <summary>Initializes an operation identifier.</summary>
    public OperationId(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("An operation identifier cannot be empty.", nameof(value));
        }

        Value = value;
    }

    /// <summary>Gets the identifier value.</summary>
    public Guid Value { get; }

    /// <summary>Creates a new identifier.</summary>
    public static OperationId New() => new(Guid.NewGuid());

    /// <inheritdoc />
    public override string ToString() => Value.ToString("D");
}

/// <summary>Describes headless execution policy for one operation.</summary>
public sealed record OperationRequest(
    OperationKey Key,
    OperationConcurrency Concurrency = OperationConcurrency.Allow,
    string? Slot = null,
    bool CanCancel = true,
    TimeSpan? Timeout = null,
    string? CorrelationId = null,
    OperationId? Id = null);

/// <summary>Describes transport-neutral operation progress.</summary>
public sealed record OperationProgress(double? Fraction, OperationStage? Stage = null);

/// <summary>Provides operation work with its identity and monitor sink.</summary>
public sealed class OperationContext
{
    private readonly Action<OperationProgress> _report;

    internal OperationContext(OperationId id, OperationRequest request, Action<OperationProgress> report)
    {
        Id = id;
        Request = request;
        _report = report;
    }

    /// <summary>Gets the invocation identifier.</summary>
    public OperationId Id { get; }

    /// <summary>Gets the execution policy.</summary>
    public OperationRequest Request { get; }

    /// <summary>Commits the latest validated progress value.</summary>
    public void Report(OperationProgress progress)
    {
        ArgumentNullException.ThrowIfNull(progress);
        _report(progress);
    }
}

/// <summary>Runs cancellable, monitored operations.</summary>
public interface IOperationRunner
{
    /// <summary>Runs work and preserves its success, cancellation, or failure semantics.</summary>
    ValueTask<T> RunAsync<T>(
        OperationRequest request,
        Func<OperationContext, CancellationToken, ValueTask<T>> operation,
        CancellationToken cancellationToken = default);

    /// <summary>Runs work and projects terminal semantics into a typed outcome.</summary>
    ValueTask<OperationOutcome<T>> TryRunAsync<T>(
        OperationRequest request,
        Func<OperationContext, CancellationToken, ValueTask<T>> operation,
        CancellationToken cancellationToken = default);
}

/// <summary>Identifies how an operation ended.</summary>
public enum OperationOutcomeKind
{
    /// <summary>The operation produced a result.</summary>
    Succeeded,
    /// <summary>The operation observed coordinated cancellation.</summary>
    Cancelled,
    /// <summary>The operation failed.</summary>
    Faulted,
}

/// <summary>Represents a typed, non-throwing operation outcome.</summary>
public readonly record struct OperationOutcome<T>(OperationOutcomeKind Kind, T? Value, Exception? Exception)
{
    /// <summary>Creates a successful outcome.</summary>
    public static OperationOutcome<T> Succeeded(T value) => new(OperationOutcomeKind.Succeeded, value, null);

    /// <summary>Creates a cancelled outcome.</summary>
    public static OperationOutcome<T> Cancelled() => new(OperationOutcomeKind.Cancelled, default, null);

    /// <summary>Creates a faulted outcome while preserving the original exception.</summary>
    public static OperationOutcome<T> Faulted(Exception exception) =>
        new(OperationOutcomeKind.Faulted, default, exception ?? throw new ArgumentNullException(nameof(exception)));
}
