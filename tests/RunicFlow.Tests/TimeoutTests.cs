using System;
using System.Threading;
using System.Threading.Tasks;
using RunicFlow;

namespace RunicFlow.Tests;

internal static class TimeoutTests
{
    private static readonly DateTimeOffset Epoch = new(2026, 7, 22, 0, 0, 0, TimeSpan.Zero);

    public static async ValueTask DelayUsesProvidedClock()
    {
        var clock = new ManualTimeProvider(Epoch);
        ValueTask delay = FlowTimeout.DelayAsync(clock, TimeSpan.FromMinutes(5));

        TestAssert.False(delay.IsCompleted);
        clock.Advance(TimeSpan.FromMinutes(4));
        TestAssert.False(delay.IsCompleted);
        clock.Advance(TimeSpan.FromMinutes(1));
        await delay.ConfigureAwait(false);
        TestAssert.Equal(Epoch + TimeSpan.FromMinutes(5), clock.GetUtcNow());
    }

    public static ValueTask CancellationSourceUsesProvidedClock()
    {
        var clock = new ManualTimeProvider(Epoch);
        using FlowTimeoutCancellation cancellation = FlowTimeout.CreateCancellationSource(
            clock,
            TimeSpan.FromSeconds(30));

        TestAssert.False(cancellation.Token.IsCancellationRequested);
        TestAssert.False(cancellation.IsTimeoutCancellationRequested);
        clock.Advance(TimeSpan.FromSeconds(29));
        TestAssert.False(cancellation.Token.IsCancellationRequested);
        clock.Advance(TimeSpan.FromSeconds(1));
        TestAssert.True(cancellation.Token.IsCancellationRequested);
        TestAssert.True(cancellation.IsTimeoutCancellationRequested);
        return ValueTask.CompletedTask;
    }

    public static ValueTask CancellationSourceLinksCallerToken()
    {
        var clock = new ManualTimeProvider(Epoch);
        using var caller = new CancellationTokenSource();
        using FlowTimeoutCancellation cancellation = FlowTimeout.CreateCancellationSource(
            clock,
            TimeSpan.FromHours(1),
            caller.Token);

        caller.Cancel();

        TestAssert.True(cancellation.Token.IsCancellationRequested);
        TestAssert.False(cancellation.IsTimeoutCancellationRequested);
        TestAssert.Equal(Epoch, clock.GetUtcNow());
        return ValueTask.CompletedTask;
    }
}
