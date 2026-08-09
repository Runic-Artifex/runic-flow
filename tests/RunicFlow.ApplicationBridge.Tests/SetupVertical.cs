using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using RunicFlow.Operations;
using RunicFlow.Processes;
using RunicToolkit.ApplicationBridge;

namespace RunicFlow.ApplicationBridge.Tests;

internal static class SetupVertical
{
    private static readonly Guid DestinationId =
        Guid.Parse("7e510a78-3c9a-4bed-8c31-2d93e5bbb835");

    public static async ValueTask RunAsync()
    {
        await using var dispatcher = new SetupDispatcher();
        await using var bridge = new ApplicationBridgeSession(dispatcher);
        var events = new List<BridgeHostEnvelope>();
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        bridge.EventProduced += (_, envelope) =>
        {
            events.Add(envelope);
            if (envelope.Payload.TryGetProperty("_tag", out JsonElement tag) &&
                tag.GetString() == "OperationCompleted")
            {
                completed.TrySetResult();
            }
        };

        BridgeHostEnvelope initialized = await bridge.DispatchAsync(
            Envelope("initialize", new { _tag = "InitializeApplication" })).ConfigureAwait(false);
        Equal("snapshot", initialized.Kind);
        Equal("Welcome", initialized.Payload.GetProperty("viewId").GetString());

        BridgeHostEnvelope selected = await bridge.DispatchAsync(
            Envelope("dispatch", new { _tag = "SelectDestination" }, bridge.Id.Value, 0)).ConfigureAwait(false);
        Equal("receipt", selected.Kind);
        Equal("DestinationSelected", selected.Payload.GetProperty("_tag").GetString());
        Equal(1L, selected.Revision);

        BridgeHostEnvelope navigated = await bridge.DispatchAsync(
            Envelope("dispatch", new { _tag = "Navigate", target = "Features" }, bridge.Id.Value, 1)).ConfigureAwait(false);
        Equal("Features", navigated.Payload.GetProperty("snapshot").GetProperty("viewId").GetString());

        BridgeHostEnvelope started = await bridge.DispatchAsync(
            Envelope("dispatch", new { _tag = "StartInstallation" }, bridge.Id.Value, 2)).ConfigureAwait(false);
        Equal("InstallationStarted", started.Payload.GetProperty("_tag").GetString());
        Guid operationId = started.Payload.GetProperty("operationId").GetGuid();
        Equal(operationId, started.OperationId);
        await completed.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);

        True(events.Exists(static envelope =>
            envelope.Payload.TryGetProperty("_tag", out JsonElement tag) &&
            tag.GetString() == "OperationProgress"));
        Equal("Complete", dispatcher.Snapshot.ViewId);
        Equal(operationId, dispatcher.LastFlowOperationId.Value);

        BridgeHostEnvelope reconnected = await bridge.DispatchAsync(
            Envelope("initialize", new { _tag = "InitializeApplication" })).ConfigureAwait(false);
        Equal("Complete", reconnected.Payload.GetProperty("viewId").GetString());
    }

    private static BridgeClientEnvelope Envelope(
        string kind,
        object payload,
        Guid? sessionId = null,
        long? expectedRevision = null) => new()
        {
            Protocol = "runic.flow.tests.setup",
            Version = 1,
            Kind = kind,
            CommandId = Guid.NewGuid(),
            SessionId = sessionId,
            ExpectedRevision = expectedRevision,
            Payload = JsonSerializer.SerializeToElement(payload),
        };

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

    private sealed class SetupDispatcher : IApplicationBridgeDispatcher, IAsyncDisposable
    {
        private readonly OperationRunner _operations = new();
        private readonly ProcessSession<SetupState, SetupCommand, bool> _process;
        private readonly TaskCompletionSource _operationBound =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public SetupDispatcher()
        {
            var definition = new ProcessDefinition<SetupState, SetupCommand, bool>(
                new ProcessKey("setup.install"),
                1,
                HandleAsync);
            _process = new ProcessSession<SetupState, SetupCommand, bool>(
                definition,
                new SetupState("Welcome", false, null));
        }

        public string ProtocolIdentity => "runic.flow.tests.setup";

        public int ProtocolVersion => 1;

        public string ManifestFingerprint => "setup-test";

        public SetupState Snapshot => _process.Snapshot.State;

        public OperationId LastFlowOperationId { get; private set; }

        public async ValueTask<BridgeDispatchResult> DispatchAsync(
            JsonElement command,
            BridgeCommandContext context,
            CancellationToken cancellationToken)
        {
            string tag = command.GetProperty("_tag").GetString() ?? string.Empty;
            if (tag == "InitializeApplication")
            {
                return Result(new { _tag = "ApplicationInitialized", snapshot = PublicSnapshot() });
            }

            if (tag == "SelectDestination")
            {
                ProcessTransition<SetupState, bool> transition = await _process
                    .DispatchAsync(new SelectDestination(), cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
                RequireAccepted(transition);
                return Result(new
                {
                    _tag = "DestinationSelected",
                    destination = new
                    {
                        selectionId = DestinationId,
                        displayName = "Recommended local destination",
                        availableBytes = 12_000_000_000L,
                    },
                    revision = context.CurrentRevision + 1,
                }, advancesRevision: true);
            }

            if (tag == "Navigate")
            {
                string target = command.GetProperty("target").GetString() ?? string.Empty;
                ProcessTransition<SetupState, bool> transition = await _process
                    .DispatchAsync(new Navigate(target), cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
                RequireAccepted(transition);
                return Result(
                    new { _tag = "NavigationAccepted", snapshot = PublicSnapshot() },
                    advancesRevision: true);
            }

            if (tag == "StartInstallation")
            {
                RequireAccepted(await _process
                    .DispatchAsync(new BeginInstallation(), cancellationToken: cancellationToken)
                    .ConfigureAwait(false));
                BridgeOperationId operationId = context.Operations.StartFlowOperation(
                    _operations,
                    new OperationRequest(
                        new OperationKey("setup.install"),
                        OperationConcurrency.Reject,
                        "setup.install"),
                    (_, flow, token) => InstallAsync(context, flow, token),
                    cancellationToken);
                LastFlowOperationId = new OperationId(operationId.Value);
                RequireAccepted(await _process
                    .DispatchAsync(new BindOperation(operationId.Value), cancellationToken: cancellationToken)
                    .ConfigureAwait(false));
                _operationBound.TrySetResult();
                return Result(
                    new
                    {
                        _tag = "InstallationStarted",
                        commandId = context.CommandId.Value,
                        operationId = operationId.Value,
                        revision = context.CurrentRevision + 1,
                    },
                    advancesRevision: true,
                    operationId);
            }

            throw new InvalidOperationException("Unsupported Setup command.");
        }

        public ValueTask DisposeAsync() => _process.DisposeAsync();

        private async ValueTask InstallAsync(
            BridgeCommandContext bridge,
            OperationContext flow,
            CancellationToken cancellationToken)
        {
            await _operationBound.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            flow.Report(new OperationProgress(0.5, new OperationStage("installing")));
            await bridge.Events.PublishAsync(
                new BridgeEventPayload(
                    JsonSerializer.SerializeToElement(new
                    {
                        _tag = "OperationProgress",
                        operationId = flow.Id.Value,
                        completed = 1,
                        total = 2,
                        stage = "installing",
                    }),
                    OperationId: new BridgeOperationId(flow.Id.Value)),
                cancellationToken).ConfigureAwait(false);
            flow.Report(new OperationProgress(1, new OperationStage("complete")));
            RequireAccepted(await _process
                .DispatchAsync(new CompleteInstallation(flow.Id.Value), cancellationToken: cancellationToken)
                .ConfigureAwait(false));
            await bridge.Events.PublishAsync(
                new BridgeEventPayload(
                    JsonSerializer.SerializeToElement(new
                    {
                        _tag = "OperationCompleted",
                        operationId = flow.Id.Value,
                    }),
                    AdvancesRevision: true,
                    OperationId: new BridgeOperationId(flow.Id.Value)),
                cancellationToken).ConfigureAwait(false);
        }

        private static ValueTask<ProcessDecision<SetupState, bool>> HandleAsync(
            ProcessCommandContext<SetupState> context,
            SetupCommand command,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SetupState state = context.State;
            ProcessDecision<SetupState, bool> decision = command switch
            {
                SelectDestination => ProcessDecision<SetupState, bool>.Accept(
                    state with { HasDestination = true }),
                Navigate { Target: "Features" } when state.HasDestination =>
                    ProcessDecision<SetupState, bool>.Accept(state with { ViewId = "Features" }),
                Navigate => ProcessDecision<SetupState, bool>.Reject("The requested transition is not available."),
                BeginInstallation when state.ViewId == "Features" && state.HasDestination =>
                    ProcessDecision<SetupState, bool>.Accept(state with { ViewId = "Installing" }),
                BindOperation bind when state.ViewId == "Installing" =>
                    ProcessDecision<SetupState, bool>.Accept(state with { ActiveOperationId = bind.OperationId }),
                CompleteInstallation complete when state.ActiveOperationId == complete.OperationId =>
                    ProcessDecision<SetupState, bool>.Complete(
                        state with { ViewId = "Complete", ActiveOperationId = null },
                        true),
                _ => ProcessDecision<SetupState, bool>.Reject("The command is not valid in the current state."),
            };
            return ValueTask.FromResult(decision);
        }

        private object PublicSnapshot() => new
        {
            viewId = Snapshot.ViewId,
            destination = Snapshot.HasDestination ? new { selectionId = DestinationId } : null,
            activeOperationId = Snapshot.ActiveOperationId,
            canNavigateBack = Snapshot.ViewId is "Destination" or "Features",
            canNavigateNext = Snapshot.ViewId == "Welcome" ||
                (Snapshot.ViewId == "Destination" && Snapshot.HasDestination) ||
                Snapshot.ViewId == "Features",
        };

        private static BridgeDispatchResult Result(
            object value,
            bool advancesRevision = false,
            BridgeOperationId? operationId = null) =>
            new(JsonSerializer.SerializeToElement(value), advancesRevision, operationId);

        private static void RequireAccepted(ProcessTransition<SetupState, bool> transition)
        {
            if (transition.Kind is not ProcessTransitionKind.Accepted and not ProcessTransitionKind.Completed)
            {
                throw new InvalidOperationException(transition.Reason ?? "The Setup process rejected a command.");
            }
        }
    }

    internal sealed record SetupState(string ViewId, bool HasDestination, Guid? ActiveOperationId);

    private abstract record SetupCommand;

    private sealed record SelectDestination : SetupCommand;

    private sealed record Navigate(string Target) : SetupCommand;

    private sealed record BeginInstallation : SetupCommand;

    private sealed record BindOperation(Guid OperationId) : SetupCommand;

    private sealed record CompleteInstallation(Guid OperationId) : SetupCommand;
}
