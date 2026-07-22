using System;
using System.Threading;
using System.Threading.Tasks;

namespace WebUIToolkit.MVVM.Flow;

/// <summary>Centralizes clock-driven delays and timeout cancellation for deterministic tests.</summary>
internal static class FlowTimeout
{
    /// <summary>Waits using the supplied clock rather than wall-clock time.</summary>
    public static ValueTask DelayAsync(
        TimeProvider timeProvider,
        TimeSpan delay,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);

        return new ValueTask(Task.Delay(delay, timeProvider, cancellationToken));
    }

    /// <summary>
    /// Creates a cancellation source driven by the supplied clock and optionally linked to a caller token.
    /// The caller owns and must dispose the returned source.
    /// </summary>
    public static FlowTimeoutCancellation CreateCancellationSource(
        TimeProvider timeProvider,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);

        CancellationTokenSource timeoutSource = new(timeout, timeProvider);
        CancellationTokenSource linkedSource;
        try
        {
            linkedSource = cancellationToken.CanBeCanceled
                ? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutSource.Token)
                : CancellationTokenSource.CreateLinkedTokenSource(timeoutSource.Token);
        }
        catch
        {
            timeoutSource.Dispose();
            throw;
        }

        return new FlowTimeoutCancellation(linkedSource, timeoutSource);
    }
}

/// <summary>Owns linked caller and clock-driven timeout cancellation sources.</summary>
internal sealed class FlowTimeoutCancellation : IDisposable
{
    private readonly CancellationTokenSource _linkedSource;
    private readonly CancellationTokenSource _timeoutSource;
    private int _disposed;

    internal FlowTimeoutCancellation(
        CancellationTokenSource linkedSource,
        CancellationTokenSource timeoutSource)
    {
        _linkedSource = linkedSource;
        _timeoutSource = timeoutSource;
    }

    /// <summary>Gets the combined caller and timeout token.</summary>
    public CancellationToken Token => _linkedSource.Token;

    /// <summary>Gets whether the clock-driven timeout was the source of cancellation.</summary>
    public bool IsTimeoutCancellationRequested => _timeoutSource.IsCancellationRequested;

    /// <inheritdoc />
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            _linkedSource.Dispose();
            _timeoutSource.Dispose();
        }
    }
}
