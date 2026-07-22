using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WebUIToolkit.MVVM.Flow;
using WebUIToolkit.MVVM.Operations;

namespace WebUIToolkit.MVVM.Flow.Tests.WaveB;

internal static class OperationTests
{
    private static readonly int[] ExpectedQueueOrder = [1, 3];

    public static async ValueTask SuccessPublishesOrderedSnapshotsAndProgress()
    {
        DateTimeOffset start = new(2040, 1, 2, 3, 4, 5, TimeSpan.Zero);
        var clock = new ManualTimeProvider(start);
        var runner = new OperationRunner(clock);
        var observer = new RecordingObserver();
        using IDisposable subscription = runner.Subscribe(observer);

        int value = await runner.RunAsync(
            new OperationRequest(new OperationKey("import")),
            (context, _) =>
            {
                context.Report(new OperationProgress(.75, "Almost done"));
                clock.Advance(TimeSpan.FromSeconds(2));
                return ValueTask.FromResult(42);
            });

        TestAssert.Equal(42, value);
        TestAssert.SequenceEqual(
            new[]
            {
                OperationState.Queued,
                OperationState.Starting,
                OperationState.Running,
                OperationState.Running,
                OperationState.Succeeded,
                OperationState.Finished,
            },
            observer.Values.Select(static value => value.State).ToArray());
        OperationSnapshot final = runner.GetSnapshots()[0];
        TestAssert.Equal(start, final.QueuedAt);
        TestAssert.Equal(start, final.StartedAt);
        TestAssert.Equal(start.AddSeconds(2), final.CompletedAt);
        TestAssert.Equal(OperationOutcomeKind.Succeeded, final.Outcome);
        TestAssert.Equal(.75, final.Progress!.Fraction);
    }

    public static async ValueTask InvalidProgressFractionsFaultWithoutEscapingBounds()
    {
        double[] invalid =
        [
            double.NaN,
            double.NegativeInfinity,
            -.000001,
            1.000001,
            double.PositiveInfinity,
        ];

        foreach (double fraction in invalid)
        {
            var runner = new OperationRunner();
            _ = await TestAssert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
            {
                _ = await runner.RunAsync(
                    new OperationRequest(new OperationKey("progress")),
                    (context, _) =>
                    {
                        context.Report(new OperationProgress(fraction));
                        return ValueTask.FromResult(0);
                    });
            });
            OperationSnapshot snapshot = runner.GetSnapshots()[0];
            TestAssert.Equal(OperationState.Finished, snapshot.State);
            TestAssert.Equal(OperationOutcomeKind.Faulted, snapshot.Outcome);
            TestAssert.Equal<OperationProgress?>(null, snapshot.Progress);
        }
    }

    public static async ValueTask QueueIsFifoAndQueuedCancellationSkipsWork()
    {
        var runner = new OperationRunner();
        var firstRelease = NewSignal();
        var firstStarted = NewSignal();
        var executionOrder = new List<int>();
        var request = new OperationRequest(
            new OperationKey("queued"),
            Concurrency: OperationConcurrency.Queue,
            Slot: "exclusive");

        Task<int> first = runner.RunAsync(request, async (_, _) =>
        {
            executionOrder.Add(1);
            firstStarted.TrySetResult();
            await firstRelease.Task.ConfigureAwait(false);
            return 1;
        }).AsTask();
        await firstStarted.Task.ConfigureAwait(false);

        using var queuedCancellation = new CancellationTokenSource();
        bool cancelledWorkInvoked = false;
        Task<int> cancelled = runner.RunAsync(
            request,
            (_, _) =>
            {
                cancelledWorkInvoked = true;
                executionOrder.Add(2);
                return ValueTask.FromResult(2);
            },
            queuedCancellation.Token).AsTask();
        Task<int> third = runner.RunAsync(request, (_, _) =>
        {
            executionOrder.Add(3);
            return ValueTask.FromResult(3);
        }).AsTask();

        queuedCancellation.Cancel();
        firstRelease.TrySetResult();

        TestAssert.Equal(1, await first.ConfigureAwait(false));
        _ = await TestAssert.ThrowsAsync<OperationCanceledException>(async () =>
            _ = await cancelled.ConfigureAwait(false));
        TestAssert.Equal(3, await third.ConfigureAwait(false));
        TestAssert.False(cancelledWorkInvoked);
        TestAssert.SequenceEqual(ExpectedQueueOrder, executionOrder);
    }

    public static async ValueTask RejectNeverInvokesWorkOrPresenter()
    {
        var firstRelease = NewSignal();
        var firstStarted = NewSignal();
        var presenter = new RecordingPresenter();
        var presenterKey = new PresenterKey("overlay");
        var runner = new OperationRunner(
            presenters: new SinglePresenterRegistry(presenterKey, presenter));
        var occupied = new OperationRequest(
            new OperationKey("first"),
            Concurrency: OperationConcurrency.Queue,
            Slot: "exclusive");
        Task<int> first = runner.RunAsync(occupied, async (_, _) =>
        {
            firstStarted.TrySetResult();
            await firstRelease.Task.ConfigureAwait(false);
            return 1;
        }).AsTask();
        await firstStarted.Task.ConfigureAwait(false);

        bool invoked = false;
        var rejected = new OperationRequest(
            new OperationKey("second"),
            Presenter: presenterKey,
            Concurrency: OperationConcurrency.Reject,
            Slot: "exclusive");
        _ = await TestAssert.ThrowsAsync<OperationBusyException>(async () =>
        {
            _ = await runner.RunAsync(rejected, (_, _) =>
            {
                invoked = true;
                return ValueTask.FromResult(2);
            });
        });

        TestAssert.False(invoked);
        TestAssert.Equal(0, presenter.ShowCount);
        firstRelease.TrySetResult();
        _ = await first.ConfigureAwait(false);
    }

    public static async ValueTask CancelPreviousWaitsForPriorCleanup()
    {
        var runner = new OperationRunner();
        var firstStarted = NewSignal();
        var firstCancelled = NewSignal();
        var cleanupRelease = NewSignal();
        var replacementStarted = NewSignal();
        var request = new OperationRequest(
            new OperationKey("replace"),
            Concurrency: OperationConcurrency.Queue,
            Slot: "exclusive");

        Task<OperationOutcome<int>> first = runner.TryRunAsync(request, async (_, token) =>
        {
            firstStarted.TrySetResult();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                firstCancelled.TrySetResult();
                await cleanupRelease.Task.ConfigureAwait(false);
                throw;
            }

            return 1;
        }).AsTask();
        await firstStarted.Task.ConfigureAwait(false);

        var replacementRequest = request with { Concurrency = OperationConcurrency.CancelPrevious };
        Task<int> replacement = runner.RunAsync(replacementRequest, (_, _) =>
        {
            replacementStarted.TrySetResult();
            return ValueTask.FromResult(2);
        }).AsTask();
        await firstCancelled.Task.ConfigureAwait(false);

        TestAssert.False(replacementStarted.Task.IsCompleted);
        cleanupRelease.TrySetResult();
        TestAssert.Equal(OperationOutcomeKind.Cancelled, (await first.ConfigureAwait(false)).Kind);
        TestAssert.Equal(2, await replacement.ConfigureAwait(false));
        TestAssert.True(replacementStarted.Task.IsCompleted);
    }

    public static async ValueTask TimeoutUsesManualClock()
    {
        var clock = new ManualTimeProvider(new DateTimeOffset(2040, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var runner = new OperationRunner(clock);
        var started = NewSignal();
        ValueTask<OperationOutcome<int>> pending = runner.TryRunAsync(
            new OperationRequest(new OperationKey("timeout"), Timeout: TimeSpan.FromMinutes(5)),
            async (_, token) =>
            {
                started.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, token).ConfigureAwait(false);
                return 1;
            });
        await started.Task.ConfigureAwait(false);

        TestAssert.False(pending.IsCompleted);
        clock.Advance(TimeSpan.FromMinutes(4));
        TestAssert.False(pending.IsCompleted);
        clock.Advance(TimeSpan.FromMinutes(1));

        OperationOutcome<int> outcome = await pending.ConfigureAwait(false);
        TestAssert.Equal(OperationOutcomeKind.Cancelled, outcome.Kind);
        TestAssert.Equal(OperationState.Finished, runner.GetSnapshots()[0].State);
    }

    public static async ValueTask ConcurrentProgressReportsRemainConsistent()
    {
        var runner = new OperationRunner();
        const int reports = 512;

        _ = await runner.RunAsync(
            new OperationRequest(new OperationKey("parallel-progress")),
            (context, _) =>
            {
                Parallel.For(0, reports, index =>
                    context.Report(new OperationProgress((double)index / reports)));
                return ValueTask.FromResult(0);
            });

        OperationSnapshot snapshot = runner.GetSnapshots()[0];
        TestAssert.Equal(OperationState.Finished, snapshot.State);
        OperationProgress progress = snapshot.Progress ??
            throw new InvalidOperationException("A running operation did not retain its latest progress.");
        TestAssert.True(progress.Fraction is >= 0 and <= 1);
    }

    public static async ValueTask PresenterCleanupFailureObeysPrecedence()
    {
        var cleanupFailure = new InvalidOperationException("close failed");
        var presenterKey = new PresenterKey("overlay");
        var presenter = new RecordingPresenter(closeFailure: cleanupFailure);
        var runner = new OperationRunner(
            presenters: new SinglePresenterRegistry(presenterKey, presenter));
        var request = new OperationRequest(new OperationKey("cleanup"), Presenter: presenterKey);

        FlowCleanupException cleanup = await TestAssert.ThrowsAsync<FlowCleanupException>(async () =>
            _ = await runner.RunAsync(request, static (_, _) => ValueTask.FromResult(7)));
        TestAssert.Equal(1, cleanup.Failures.Count);
        TestAssert.True(ReferenceEquals(cleanupFailure, cleanup.Failures[0]));
        TestAssert.Equal(1, presenter.Lease!.CloseCount);
        TestAssert.Equal(1, presenter.Lease.DisposeCount);

        var workFailure = new InvalidOperationException("work failed");
        InvalidOperationException primary = await TestAssert.ThrowsAsync<InvalidOperationException>(async () =>
            _ = await runner.RunAsync<int>(request, (_, _) => ValueTask.FromException<int>(workFailure)));
        TestAssert.True(ReferenceEquals(workFailure, primary));
        TestAssert.True(primary.Data.Contains("WebUIToolkit.MVVM.Flow.CleanupException"));
    }

    public static async ValueTask HungPresenterCleanupReleasesSlotAfterManualTimeout()
    {
        TimeSpan timeout = TimeSpan.FromMinutes(3);
        var clock = new ManualTimeProvider(new DateTimeOffset(2040, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var presenterKey = new PresenterKey("overlay");
        var presenter = new HangingOperationPresenter();
        var runner = new OperationRunner(
            clock,
            new SinglePresenterRegistry(presenterKey, presenter),
            new OperationRunnerOptions { CleanupTimeout = timeout });
        var request = new OperationRequest(
            new OperationKey("hung-cleanup"),
            Presenter: presenterKey,
            Concurrency: OperationConcurrency.Reject,
            Slot: "exclusive");

        Task<int> pending = runner.RunAsync(request, static (_, _) => ValueTask.FromResult(1)).AsTask();
        await presenter.Lease.CloseStarted.Task.ConfigureAwait(false);
        TestAssert.False(pending.IsCompleted);
        clock.Advance(timeout);

        FlowCleanupException exception = await TestAssert.ThrowsAsync<FlowCleanupException>(async () =>
            _ = await pending.ConfigureAwait(false));
        TestAssert.True(exception.Failures[0] is TimeoutException);
        TestAssert.Equal(0, presenter.Lease.DisposeCount);
        TestAssert.Equal(OperationState.Finished, runner.GetSnapshots()[0].State);
        int replacement = await runner.RunAsync(
            request with { Presenter = null },
            static (_, _) => ValueTask.FromResult(2));
        TestAssert.Equal(2, replacement);
    }

    private static TaskCompletionSource NewSignal() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private sealed class RecordingObserver : IObserver<OperationSnapshot>
    {
        private readonly object _sync = new();
        private readonly List<OperationSnapshot> _values = [];

        public IReadOnlyList<OperationSnapshot> Values
        {
            get
            {
                lock (_sync)
                {
                    return _values.ToArray();
                }
            }
        }

        public void OnCompleted()
        {
        }

        public void OnError(Exception error)
        {
        }

        public void OnNext(OperationSnapshot value)
        {
            lock (_sync)
            {
                _values.Add(value);
            }
        }
    }

    private sealed class SinglePresenterRegistry(PresenterKey key, IOperationPresenter presenter)
        : IOperationPresenterRegistry
    {
        public bool TryGetPresenter(PresenterKey requested, out IOperationPresenter? result)
        {
            result = requested == key ? presenter : null;
            return result is not null;
        }
    }

    private sealed class RecordingPresenter(Exception? closeFailure = null) : IOperationPresenter
    {
        public int ShowCount { get; private set; }
        public RecordingLease? Lease { get; private set; }

        public ValueTask<IFlowPresentationLease> ShowAsync(
            OperationSnapshot operation,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ShowCount++;
            Lease = new RecordingLease(closeFailure);
            return ValueTask.FromResult<IFlowPresentationLease>(Lease);
        }
    }

    private sealed class HangingOperationPresenter : IOperationPresenter
    {
        public HangingOperationLease Lease { get; } = new();

        public ValueTask<IFlowPresentationLease> ShowAsync(
            OperationSnapshot operation,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult<IFlowPresentationLease>(Lease);
    }

    private sealed class HangingOperationLease : IFlowPresentationLease
    {
        private readonly TaskCompletionSource _never = NewSignal();
        public TaskCompletionSource CloseStarted { get; } = NewSignal();
        public int DisposeCount { get; private set; }

        public ValueTask CloseAsync(CancellationToken cancellationToken)
        {
            CloseStarted.TrySetResult();
            return new ValueTask(_never.Task);
        }
        public ValueTask DisposeAsync()
        {
            DisposeCount++;
            return new ValueTask(_never.Task);
        }
    }

    private sealed class RecordingLease(Exception? closeFailure) : IFlowPresentationLease
    {
        public int CloseCount { get; private set; }
        public int DisposeCount { get; private set; }

        public ValueTask CloseAsync(CancellationToken cancellationToken)
        {
            CloseCount++;
            return closeFailure is null
                ? ValueTask.CompletedTask
                : ValueTask.FromException(closeFailure);
        }

        public ValueTask DisposeAsync()
        {
            DisposeCount++;
            return ValueTask.CompletedTask;
        }
    }
}
