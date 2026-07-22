using System;
using System.Threading;
using System.Threading.Tasks;

namespace WebUIToolkit.MVVM.Flow;

/// <summary>
/// Coordinates a completion request that may need asynchronous validation before it is accepted.
/// </summary>
/// <typeparam name="T">The accepted result type.</typeparam>
internal sealed class CompletionSource<T>
{
    private const int Pending = 0;
    private const int Claimed = 1;
    private const int Completed = 2;

    private readonly TaskCompletionSource<T> _completion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _state;

    /// <summary>Gets the task that completes when a result, cancellation, or exception is accepted.</summary>
    public Task<T> Completion => _completion.Task;

    /// <summary>Gets whether a completion attempt is being validated or has already completed.</summary>
    public bool IsCompletionRequested => Volatile.Read(ref _state) != Pending;

    /// <summary>Attempts to reserve the single completion slot for asynchronous validation.</summary>
    public bool TryClaim() => Interlocked.CompareExchange(ref _state, Claimed, Pending) == Pending;

    /// <summary>
    /// Releases a pending claim after validation denies it, allowing a later completion request to retry.
    /// </summary>
    public bool ReleaseClaim() => Interlocked.CompareExchange(ref _state, Pending, Claimed) == Claimed;

    /// <summary>Attempts to complete directly, without a separate validation phase.</summary>
    public bool TrySetResult(T result)
    {
        if (!TryClaim())
        {
            return false;
        }

        return TrySetClaimedResult(result);
    }

    /// <summary>Accepts the result associated with the current claim.</summary>
    public bool TrySetClaimedResult(T result)
    {
        if (Interlocked.CompareExchange(ref _state, Completed, Claimed) != Claimed)
        {
            return false;
        }

        return _completion.TrySetResult(result);
    }

    /// <summary>Attempts to fault directly, without a separate validation phase.</summary>
    public bool TrySetException(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        if (!TryClaim())
        {
            return false;
        }

        return TrySetClaimedException(exception);
    }

    /// <summary>Accepts the exception associated with the current claim.</summary>
    public bool TrySetClaimedException(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        if (Interlocked.CompareExchange(ref _state, Completed, Claimed) != Claimed)
        {
            return false;
        }

        return _completion.TrySetException(exception);
    }

    /// <summary>Attempts to cancel directly, preserving the token that won the race.</summary>
    public bool TrySetCanceled(CancellationToken cancellationToken)
    {
        if (!TryClaim())
        {
            return false;
        }

        return TrySetClaimedCanceled(cancellationToken);
    }

    /// <summary>Accepts cancellation associated with the current claim.</summary>
    public bool TrySetClaimedCanceled(CancellationToken cancellationToken)
    {
        if (Interlocked.CompareExchange(ref _state, Completed, Claimed) != Claimed)
        {
            return false;
        }

        return _completion.TrySetCanceled(cancellationToken);
    }
}
