using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using RunicFlow;

namespace RunicFlow.Tests;

internal static class CompletionTests
{
    public static async ValueTask DeniedClaimMayBeRetried()
    {
        var completion = new CompletionSource<string>();

        TestAssert.True(completion.TryClaim());
        TestAssert.True(completion.IsCompletionRequested);
        TestAssert.False(completion.TrySetResult("blocked"));
        TestAssert.True(completion.ReleaseClaim());
        TestAssert.False(completion.IsCompletionRequested);
        TestAssert.True(completion.TrySetResult("accepted"));
        TestAssert.False(completion.ReleaseClaim());
        TestAssert.Equal("accepted", await completion.Completion.ConfigureAwait(false));
    }

    public static async ValueTask ConcurrentResultRaceCompletesExactlyOnce()
    {
        var completion = new CompletionSource<int>();
        Task<bool>[] attempts = Enumerable.Range(0, 64)
            .Select(candidate => Task.Run(() => completion.TrySetResult(candidate)))
            .ToArray();

        bool[] accepted = await Task.WhenAll(attempts).ConfigureAwait(false);
        int result = await completion.Completion.ConfigureAwait(false);

        TestAssert.Equal(1, accepted.Count(value => value));
        TestAssert.True(result >= 0 && result < attempts.Length);
        TestAssert.False(completion.TrySetResult(100));
        TestAssert.False(completion.TrySetException(new InvalidOperationException("late")));
        TestAssert.False(completion.TrySetCanceled(CancellationToken.None));
    }

    public static async ValueTask CancellationWinsExactlyOnce()
    {
        var completion = new CompletionSource<int>();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        TestAssert.True(completion.TrySetCanceled(cancellation.Token));
        TestAssert.False(completion.TrySetResult(42));

        OperationCanceledException exception = await TestAssert.ThrowsAsync<OperationCanceledException>(
            async () => await completion.Completion.ConfigureAwait(false)).ConfigureAwait(false);
        TestAssert.Equal(cancellation.Token, exception.CancellationToken);
    }

    public static async ValueTask FaultWinsExactlyOnce()
    {
        var completion = new CompletionSource<int>();
        var expected = new InvalidOperationException("expected");

        TestAssert.True(completion.TrySetException(expected));
        TestAssert.False(completion.TrySetException(new InvalidOperationException("late")));

        InvalidOperationException actual = await TestAssert.ThrowsAsync<InvalidOperationException>(
            async () => await completion.Completion.ConfigureAwait(false)).ConfigureAwait(false);
        TestAssert.True(ReferenceEquals(expected, actual));
    }
}
