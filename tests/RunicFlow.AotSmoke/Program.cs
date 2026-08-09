using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using RunicFlow.ApplicationBridge;
using RunicFlow.Operations;
using RunicFlow.Processes;
using RunicToolkit.ApplicationBridge;

namespace RunicFlow.AotSmoke;

internal static class Program
{
    public static async Task<int> Main()
    {
        ProcessDefinition<State, Command, string> definition = new(
            new ProcessKey("aot.setup"),
            1,
            static (context, command, cancellationToken) =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                ProcessDecision<State, string> decision = command.Kind switch
                {
                    "advance" => ProcessDecision<State, string>.Accept(context.State with { Step = 2 }),
                    "complete" => ProcessDecision<State, string>.Complete(context.State, "done"),
                    _ => ProcessDecision<State, string>.Reject("unsupported"),
                };
                return ValueTask.FromResult(decision);
            });
        await using var process = new ProcessSession<State, Command, string>(definition, new State(1));
        ProcessTransition<State, string> advanced = await process
            .DispatchAsync(new Command("advance"), expectedVersion: 0)
            .ConfigureAwait(false);
        ProcessCheckpoint checkpoint = process.CreateCheckpoint(new Codec());
        await using ProcessSession<State, Command, string> restored = ProcessCheckpointing.Restore(
            definition,
            checkpoint,
            new Codec());
        ProcessTransition<State, string> completed = await restored
            .DispatchAsync(new Command("complete"), expectedVersion: 1)
            .ConfigureAwait(false);

        var runner = new OperationRunner();
        using var bridge = new Factory(Guid.Parse("8ed985db-78d6-4c58-a3ef-b966402cb5de"));
        BridgeOperationId bridgeId = bridge.StartFlowOperation(
            runner,
            new OperationRequest(new OperationKey("aot.install")),
            static (_, context, _) =>
            {
                context.Report(new OperationProgress(1, new OperationStage("complete")));
                return ValueTask.CompletedTask;
            });
        await bridge.Completion.ConfigureAwait(false);
        OperationSnapshot operation = runner.GetSnapshots()[0];

        bool valid = advanced.Snapshot.State.Step == 2
            && checkpoint.Version == 1
            && completed.Kind == ProcessTransitionKind.Completed
            && completed.Snapshot.Result == "done"
            && operation.Id.Value == bridgeId.Value
            && operation.State == OperationState.Succeeded
            && operation.Progress?.Stage == new OperationStage("complete");
        if (!valid)
        {
            Console.Error.WriteLine("FAIL: Flow NativeAOT headless process scenario produced an unexpected result.");
            return 1;
        }

        Console.WriteLine("PASS: Flow NativeAOT process, checkpoint, operation, and Application Bridge integration.");
        return 0;
    }

    private sealed record State(int Step);

    private sealed record Command(string Kind);

    private sealed class Codec : IProcessCheckpointCodec<State>
    {
        public byte[] Encode(State state) => Encoding.UTF8.GetBytes(
            state.Step.ToString(System.Globalization.CultureInfo.InvariantCulture));

        public State Decode(ReadOnlyMemory<byte> payload) =>
            new(int.Parse(Encoding.UTF8.GetString(payload.Span), System.Globalization.CultureInfo.InvariantCulture));
    }

    private sealed class Factory(Guid value) : IBridgeOperationFactory, IDisposable
    {
        public Task Completion { get; private set; } = Task.CompletedTask;

        public BridgeOperationId Start(
            Func<BridgeOperationId, CancellationToken, ValueTask> operation,
            CancellationToken cancellationToken = default)
        {
            var id = new BridgeOperationId(value);
            Completion = operation(id, cancellationToken).AsTask();
            return id;
        }

        public void Dispose()
        {
        }
    }
}
