using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using RunicFlow.Operations;
using RunicFlow.Processes;

namespace RunicFlow.Tests;

internal static class HeadlessFlowTests
{
    public static IReadOnlyList<(string Name, Func<ValueTask> Run)> All { get; } =
    [
        ("Process commits, rejects, guards stale versions, and completes", ProcessLifecycleAsync),
        ("Process handler failure preserves authoritative state", ProcessFailureAsync),
        ("Process rejects recursive dispatch", ProcessReentrancyAsync),
        ("Checkpoint is defensive and restores state", CheckpointAsync),
        ("Operation reports progress and preserves success", OperationSuccessAsync),
        ("Operation rejects an occupied slot", OperationRejectAsync),
        ("Operation cancel-previous cancels the prior owner", OperationCancelPreviousAsync),
        ("Operation monitor cancellation is cooperative", OperationCancellationAsync),
        ("Operation timeout uses the configured clock", OperationTimeoutAsync),
    ];

    private static async ValueTask ProcessLifecycleAsync()
    {
        ProcessDefinition<SetupState, SetupCommand, string> definition = Definition();
        await using var process = new ProcessSession<SetupState, SetupCommand, string>(
            definition,
            new SetupState("Welcome", false));
        int changes = 0;
        process.SnapshotChanged += (_, _) => changes++;

        ProcessTransition<SetupState, string> rejected = await process
            .DispatchAsync(new SetupCommand("Navigate", "Features"))
            .ConfigureAwait(false);
        Equal(ProcessTransitionKind.Rejected, rejected.Kind);
        Equal(0L, rejected.Snapshot.Version);

        ProcessTransition<SetupState, string> selected = await process
            .DispatchAsync(new SetupCommand("SelectDestination"), expectedVersion: 0)
            .ConfigureAwait(false);
        Equal(ProcessTransitionKind.Accepted, selected.Kind);
        Equal(1L, selected.Snapshot.Version);

        ProcessTransition<SetupState, string> stale = await process
            .DispatchAsync(new SetupCommand("Navigate", "Features"), expectedVersion: 0)
            .ConfigureAwait(false);
        Equal(ProcessTransitionKind.Stale, stale.Kind);
        Equal(1L, stale.Snapshot.Version);

        ProcessTransition<SetupState, string> moved = await process
            .DispatchAsync(new SetupCommand("Navigate", "Features"), expectedVersion: 1)
            .ConfigureAwait(false);
        Equal("Features", moved.Snapshot.State.View);

        ProcessTransition<SetupState, string> completed = await process
            .DispatchAsync(new SetupCommand("Complete"), expectedVersion: 2)
            .ConfigureAwait(false);
        Equal(ProcessTransitionKind.Completed, completed.Kind);
        Equal(ProcessStatus.Completed, completed.Snapshot.Status);
        Equal("installed", completed.Snapshot.Result);

        ProcessTransition<SetupState, string> terminal = await process
            .DispatchAsync(new SetupCommand("Navigate", "Welcome"))
            .ConfigureAwait(false);
        Equal(ProcessTransitionKind.Terminal, terminal.Kind);
        Equal(3, changes);
    }

    private static async ValueTask ProcessFailureAsync()
    {
        ProcessDefinition<int, string, int> definition = new(
            new ProcessKey("test.failure"),
            1,
            static (_, _, _) => throw new InvalidOperationException("failure"));
        await using var process = new ProcessSession<int, string, int>(definition, 7);
        await ThrowsAsync<InvalidOperationException>(async () =>
        {
            _ = await process.DispatchAsync("fail").ConfigureAwait(false);
        }).ConfigureAwait(false);
        Equal(0L, process.Snapshot.Version);
        Equal(7, process.Snapshot.State);
    }

    private static async ValueTask ProcessReentrancyAsync()
    {
        ProcessSession<int, string, int>? process = null;
        ProcessDefinition<int, string, int> definition = new(
            new ProcessKey("test.reentrant"),
            1,
            async (context, command, cancellationToken) =>
            {
                if (context.Version != 0 || command != "outer")
                {
                    throw new InvalidOperationException("Unexpected reentrant test input.");
                }

                await ThrowsAsync<InvalidOperationException>(async () =>
                {
                    ProcessTransition<int, int> nested = await process!.DispatchAsync(
                        "nested",
                        cancellationToken: cancellationToken).ConfigureAwait(false);
                    _ = nested;
                }).ConfigureAwait(false);
                return ProcessDecision<int, int>.Accept(1);
            });
        await using (process = new ProcessSession<int, string, int>(definition, 0))
        {
            ProcessTransition<int, int> transition = await process.DispatchAsync("outer").ConfigureAwait(false);
            Equal(ProcessTransitionKind.Accepted, transition.Kind);
        }
    }

    private static async ValueTask CheckpointAsync()
    {
        ProcessDefinition<SetupState, SetupCommand, string> definition = Definition();
        await using var original = new ProcessSession<SetupState, SetupCommand, string>(
            definition,
            new SetupState("Welcome", false));
        _ = await original.DispatchAsync(new SetupCommand("SelectDestination")).ConfigureAwait(false);
        var codec = new SetupCodec();
        ProcessCheckpoint checkpoint = original.CreateCheckpoint(codec);
        byte[] copy = checkpoint.Payload.ToArray();
        copy[0] = (byte)'x';
        True(checkpoint.Payload.Span[0] != (byte)'x');

        await using ProcessSession<SetupState, SetupCommand, string> restored =
            ProcessCheckpointing.Restore(definition, checkpoint, codec);
        Equal(original.Snapshot.Id, restored.Snapshot.Id);
        Equal(original.Snapshot.Version, restored.Snapshot.Version);
        Equal(original.Snapshot.State, restored.Snapshot.State);
    }

    private static async ValueTask OperationSuccessAsync()
    {
        var runner = new OperationRunner();
        OperationOutcome<int> outcome = await runner.TryRunAsync(
            new OperationRequest(new OperationKey("test.success")),
            static (context, _) =>
            {
                context.Report(new OperationProgress(1, new OperationStage("complete")));
                return ValueTask.FromResult(42);
            }).ConfigureAwait(false);
        Equal(OperationOutcomeKind.Succeeded, outcome.Kind);
        Equal(42, outcome.Value);
        OperationSnapshot snapshot = runner.GetSnapshots()[0];
        Equal(OperationState.Succeeded, snapshot.State);
        Equal(1d, snapshot.Progress?.Fraction);
        Equal(new OperationStage("complete"), snapshot.Progress?.Stage);
    }

    private static async ValueTask OperationRejectAsync()
    {
        var runner = new OperationRunner();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task first = runner.RunAsync(
            new OperationRequest(new OperationKey("test.first"), OperationConcurrency.Queue, "install"),
            async (_, cancellationToken) =>
            {
                entered.SetResult();
                await release.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
                return true;
            }).AsTask();
        await entered.Task.ConfigureAwait(false);
        await ThrowsAsync<OperationBusyException>(async () =>
        {
            _ = await runner.RunAsync(
                new OperationRequest(new OperationKey("test.second"), OperationConcurrency.Reject, "install"),
                static (_, _) => ValueTask.FromResult(true)).ConfigureAwait(false);
        }).ConfigureAwait(false);
        release.SetResult();
        await first.ConfigureAwait(false);
    }

    private static async ValueTask OperationCancelPreviousAsync()
    {
        var runner = new OperationRunner();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<OperationOutcome<bool>> first = runner.TryRunAsync(
            new OperationRequest(new OperationKey("test.old"), OperationConcurrency.Queue, "install"),
            async (_, cancellationToken) =>
            {
                entered.SetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
                return true;
            }).AsTask();
        await entered.Task.ConfigureAwait(false);
        OperationOutcome<bool> replacement = await runner.TryRunAsync(
            new OperationRequest(new OperationKey("test.new"), OperationConcurrency.CancelPrevious, "install"),
            static (_, _) => ValueTask.FromResult(true)).ConfigureAwait(false);
        Equal(OperationOutcomeKind.Cancelled, (await first.ConfigureAwait(false)).Kind);
        Equal(OperationOutcomeKind.Succeeded, replacement.Kind);
        OperationSnapshot cancelled = Find(runner, new OperationKey("test.old"));
        Equal(OperationCancellationReason.Replaced, cancelled.CancellationReason);
    }

    private static async ValueTask OperationCancellationAsync()
    {
        var runner = new OperationRunner();
        var entered = new TaskCompletionSource<OperationId>(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<OperationOutcome<bool>> pending = runner.TryRunAsync(
            new OperationRequest(new OperationKey("test.cancel")),
            async (context, cancellationToken) =>
            {
                entered.SetResult(context.Id);
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
                return true;
            }).AsTask();
        OperationId id = await entered.Task.ConfigureAwait(false);
        True(runner.RequestCancellation(id));
        Equal(OperationOutcomeKind.Cancelled, (await pending.ConfigureAwait(false)).Kind);
        True(!runner.RequestCancellation(id));
    }

    private static async ValueTask OperationTimeoutAsync()
    {
        var clock = new ManualTimeProvider(new DateTimeOffset(2026, 8, 9, 12, 0, 0, TimeSpan.Zero));
        var runner = new OperationRunner(clock);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<OperationOutcome<bool>> pending = runner.TryRunAsync(
            new OperationRequest(new OperationKey("test.timeout"), Timeout: TimeSpan.FromMinutes(1)),
            async (_, cancellationToken) =>
            {
                entered.SetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
                return true;
            }).AsTask();
        await entered.Task.ConfigureAwait(false);
        clock.Advance(TimeSpan.FromMinutes(1));
        Equal(OperationOutcomeKind.Cancelled, (await pending.ConfigureAwait(false)).Kind);
        OperationSnapshot snapshot = runner.GetSnapshots()[0];
        Equal(OperationCancellationReason.Timeout, snapshot.CancellationReason);
        Equal(clock.GetUtcNow(), snapshot.CompletedAt);
    }

    private static ProcessDefinition<SetupState, SetupCommand, string> Definition() => new(
        new ProcessKey("setup.install"),
        1,
        static (context, command, cancellationToken) =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            SetupState state = context.State;
            ProcessDecision<SetupState, string> decision = command.Kind switch
            {
                "SelectDestination" => ProcessDecision<SetupState, string>.Accept(state with { HasDestination = true }),
                "Navigate" when command.Target == "Features" && !state.HasDestination =>
                    ProcessDecision<SetupState, string>.Reject("A destination is required."),
                "Navigate" => ProcessDecision<SetupState, string>.Accept(state with { View = command.Target! }),
                "Complete" => ProcessDecision<SetupState, string>.Complete(state with { View = "Complete" }, "installed"),
                _ => ProcessDecision<SetupState, string>.Reject("Unsupported command."),
            };
            return ValueTask.FromResult(decision);
        });

    private static OperationSnapshot Find(OperationRunner runner, OperationKey key)
    {
        foreach (OperationSnapshot snapshot in runner.GetSnapshots())
        {
            if (snapshot.Key == key)
            {
                return snapshot;
            }
        }

        throw new InvalidOperationException($"Operation '{key}' was not retained.");
    }

    private static async ValueTask ThrowsAsync<TException>(Func<ValueTask> action)
        where TException : Exception
    {
        try
        {
            await action().ConfigureAwait(false);
        }
        catch (TException)
        {
            return;
        }

        throw new InvalidOperationException($"Expected {typeof(TException).Name}.");
    }

    private static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"Expected '{expected}', got '{actual}'.");
        }
    }

    private static void True(bool value)
    {
        if (!value)
        {
            throw new InvalidOperationException("Expected true.");
        }
    }

    private sealed record SetupState(string View, bool HasDestination);

    private sealed record SetupCommand(string Kind, string? Target = null);

    private sealed class SetupCodec : IProcessCheckpointCodec<SetupState>
    {
        public byte[] Encode(SetupState state) => Encoding.UTF8.GetBytes($"{state.View}|{state.HasDestination}");

        public SetupState Decode(ReadOnlyMemory<byte> payload)
        {
            string[] parts = Encoding.UTF8.GetString(payload.Span).Split('|');
            return new SetupState(parts[0], bool.Parse(parts[1]));
        }
    }
}
