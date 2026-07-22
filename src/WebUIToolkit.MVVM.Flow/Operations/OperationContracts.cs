using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using WebUIToolkit.MVVM.Flow;

namespace WebUIToolkit.MVVM.Operations;

/// <summary>Controls admission when operations share a non-empty slot.</summary>
public enum OperationConcurrency
{
    /// <summary>Runs without waiting for other operations in the slot.</summary>
    Allow,

    /// <summary>Rejects the operation while another operation occupies the slot.</summary>
    Reject,

    /// <summary>Waits in first-in, first-out order for the slot.</summary>
    Queue,

    /// <summary>Cancels current operations and waits for their complete teardown.</summary>
    CancelPrevious,
}

/// <summary>Describes a request to run monitored work.</summary>
public sealed record OperationRequest(
    OperationKey Key,
    string? Title = null,
    string? Message = null,
    PresenterKey? Presenter = null,
    OperationConcurrency Concurrency = OperationConcurrency.Allow,
    string? Slot = null,
    bool CanCancel = true,
    TimeSpan? Timeout = null,
    string? CorrelationId = null);

/// <summary>Identifies one invocation of an operation.</summary>
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

    /// <summary>Creates a unique operation identifier.</summary>
    public static OperationId Create() => new(Guid.NewGuid());

    /// <inheritdoc />
    public override string ToString() => Value.ToString("D");
}

/// <summary>Describes progress for one independently meaningful part of an operation.</summary>
public sealed record OperationSegment(
    string Name,
    double? Fraction,
    string? Message = null,
    SemanticTone Tone = SemanticTone.Default);

/// <summary>Describes the latest progress reported by an operation.</summary>
public sealed record OperationProgress(
    double? Fraction,
    string? Message = null,
    SemanticTone Tone = SemanticTone.Default,
    IReadOnlyList<OperationSegment>? Segments = null);

/// <summary>Provides an operation delegate with its identity and progress sink.</summary>
public sealed class OperationContext
{
    private readonly Action<OperationProgress> _report;

    internal OperationContext(OperationId id, OperationRequest request, Action<OperationProgress> report)
    {
        Id = id;
        Request = request;
        _report = report;
    }

    /// <summary>Gets the unique invocation identifier.</summary>
    public OperationId Id { get; }

    /// <summary>Gets the immutable invocation request.</summary>
    public OperationRequest Request { get; }

    /// <summary>Publishes validated progress for this invocation.</summary>
    public void Report(OperationProgress progress)
    {
        ArgumentNullException.ThrowIfNull(progress);
        _report(progress);
    }
}

/// <summary>Runs cancellable, monitored operations.</summary>
public interface IOperationRunner
{
    /// <summary>Runs work, returning its value or preserving its cancellation/failure.</summary>
    ValueTask<T> RunAsync<T>(
        OperationRequest request,
        Func<OperationContext, CancellationToken, ValueTask<T>> operation,
        CancellationToken cancellationToken = default);

    /// <summary>Runs work and projects cancellation/failure into a typed outcome.</summary>
    ValueTask<OperationOutcome<T>> TryRunAsync<T>(
        OperationRequest request,
        Func<OperationContext, CancellationToken, ValueTask<T>> operation,
        CancellationToken cancellationToken = default);
}

/// <summary>Presents an operation without owning or invoking its work delegate.</summary>
public interface IOperationPresenter
{
    /// <summary>Shows the supplied immutable operation state and returns its owning lease.</summary>
    ValueTask<IFlowPresentationLease> ShowAsync(
        OperationSnapshot operation,
        CancellationToken cancellationToken);
}

/// <summary>Resolves logical operation presenter keys at the adapter boundary.</summary>
public interface IOperationPresenterRegistry
{
    /// <summary>Gets a presenter, or returns <see langword="false"/> when the key is not registered.</summary>
    bool TryGetPresenter(PresenterKey key, out IOperationPresenter? presenter);
}
