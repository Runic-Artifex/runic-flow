using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using RunicFlow.Operations;
using RunicToolkit.ApplicationBridge;

namespace RunicFlow.ApplicationBridge.Tests;

internal static class Program
{
    public static async Task<int> Main()
    {
        try
        {
            await ReusesBridgeIdentityAsync().ConfigureAwait(false);
            await PreservesBridgeCancellationAsync().ConfigureAwait(false);
            await SetupVertical.RunAsync().ConfigureAwait(false);
            Console.WriteLine("PASS Application Bridge owns identity while Flow owns operation policy.");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
    }

    private static async ValueTask ReusesBridgeIdentityAsync()
    {
        using var factory = new FakeFactory(Guid.Parse("6bc59b82-6d0c-4d63-9b51-0cb21c3f8d44"));
        var runner = new OperationRunner();
        OperationId observed = default;
        BridgeOperationId id = factory.StartFlowOperation(
            runner,
            new OperationRequest(new OperationKey("setup.install"), OperationConcurrency.Queue, "setup.install"),
            (_, context, _) =>
            {
                observed = context.Id;
                context.Report(new OperationProgress(1, new OperationStage("complete")));
                return ValueTask.CompletedTask;
            });
        await factory.Completion.ConfigureAwait(false);
        Equal(id.Value, observed.Value);
        OperationSnapshot snapshot = runner.GetSnapshots()[0];
        Equal(id.Value, snapshot.Id.Value);
        Equal(id.Value.ToString("D"), snapshot.CorrelationId);
    }

    private static async ValueTask PreservesBridgeCancellationAsync()
    {
        using var factory = new FakeFactory(Guid.Parse("1546bd90-bd36-462d-af2d-5ca7f6032748"));
        var runner = new OperationRunner();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _ = factory.StartFlowOperation(
            runner,
            new OperationRequest(new OperationKey("setup.cancel")),
            async (_, _, cancellationToken) =>
            {
                entered.SetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
            });
        await entered.Task.ConfigureAwait(false);
        factory.Cancel();
        try
        {
            await factory.Completion.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }

        OperationSnapshot snapshot = runner.GetSnapshots()[0];
        Equal(OperationState.Cancelled, snapshot.State);
        Equal(OperationCancellationReason.Caller, snapshot.CancellationReason);
    }

    private static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"Expected '{expected}', got '{actual}'.");
        }
    }

    private sealed class FakeFactory(Guid id) : IBridgeOperationFactory, IDisposable
    {
        private readonly CancellationTokenSource _cancellation = new();
        private Task _completion = Task.CompletedTask;

        public Task Completion => _completion;

        public BridgeOperationId Start(
            Func<BridgeOperationId, CancellationToken, ValueTask> operation,
            CancellationToken cancellationToken = default)
        {
            var operationId = new BridgeOperationId(id);
            var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _cancellation.Token);
            _completion = RunAsync(operation, operationId, linked);
            return operationId;
        }

        public void Cancel() => _cancellation.Cancel();

        public void Dispose() => _cancellation.Dispose();

        private static async Task RunAsync(
            Func<BridgeOperationId, CancellationToken, ValueTask> operation,
            BridgeOperationId operationId,
            CancellationTokenSource cancellation)
        {
            using (cancellation)
            {
                await operation(operationId, cancellation.Token).ConfigureAwait(false);
            }
        }
    }
}
