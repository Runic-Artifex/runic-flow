using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using RunicFlow.Operations;
using RunicFlow.Processes;

namespace RunicFlow.PackageConsumer;

internal static class Program
{
    public static async Task<int> Main()
    {
        ProcessDefinition<State, string, int> definition = new(
            new ProcessKey("consumer.setup"),
            1,
            static (context, command, _) => ValueTask.FromResult(
                command == "finish"
                    ? ProcessDecision<State, int>.Complete(context.State, context.State.Value)
                    : ProcessDecision<State, int>.Accept(context.State with { Value = context.State.Value + 1 })));
        await using var process = new ProcessSession<State, string, int>(definition, new State(41));
        _ = await process.DispatchAsync("increment").ConfigureAwait(false);
        ProcessCheckpoint checkpoint = process.CreateCheckpoint(new Codec());
        await using ProcessSession<State, string, int> restored = ProcessCheckpointing.Restore(
            definition,
            checkpoint,
            new Codec());
        ProcessTransition<State, int> completed = await restored.DispatchAsync("finish").ConfigureAwait(false);

        var runner = new OperationRunner();
        OperationOutcome<int> operation = await runner.TryRunAsync(
            new OperationRequest(new OperationKey("consumer.install")),
            static (context, _) =>
            {
                context.Report(new OperationProgress(1, new OperationStage("complete")));
                return ValueTask.FromResult(42);
            }).ConfigureAwait(false);
        IReadOnlyList<OperationSnapshot> snapshots = runner.GetSnapshots();

        bool valid = completed.Kind == ProcessTransitionKind.Completed
            && completed.Snapshot.Result == 42
            && operation == OperationOutcome<int>.Succeeded(42)
            && snapshots.Count == 1
            && snapshots[0].State == OperationState.Succeeded;
        if (!valid)
        {
            Console.Error.WriteLine("FAIL: packaged Flow headless scenario produced an unexpected result.");
            return 1;
        }

        Console.WriteLine("PASS: packaged Flow process, checkpoint, and operation scenario.");
        return 0;
    }

    private sealed record State(int Value);

    private sealed class Codec : IProcessCheckpointCodec<State>
    {
        public byte[] Encode(State state) => Encoding.UTF8.GetBytes(
            state.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));

        public State Decode(ReadOnlyMemory<byte> payload) =>
            new(int.Parse(Encoding.UTF8.GetString(payload.Span), System.Globalization.CultureInfo.InvariantCulture));
    }
}
