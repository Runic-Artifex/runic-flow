using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using RunicFlow.Dialogs;
using RunicFlow;
using RunicFlow.Navigation;
using RunicFlow.Operations;
using RunicFlow.Workflows;

namespace RunicFlow.PackageConsumer;

internal static class Program
{
    public static async Task<int> Main()
    {
        bool contractsValid = VerifyContracts();
        bool registrationsValid = await VerifyNavigationAndWorkflowAsync().ConfigureAwait(false);
        bool dialogValid = await VerifyDialogAsync().ConfigureAwait(false);
        bool operationValid = await VerifyOperationAsync().ConfigureAwait(false);

        if (!contractsValid || !registrationsValid || !dialogValid || !operationValid)
        {
            Console.Error.WriteLine("FAIL: packaged Flow public-kernel scenario produced an unexpected result.");
            return 1;
        }

        Console.WriteLine("PASS: packaged Flow navigation, dialog, operation, workflow, and checkpoint scenarios.");
        return 0;
    }

    private static bool VerifyContracts()
    {
        var viewModel = new ConsumerViewModel("ready");
        var descriptor = new FlowContentDescriptor(
            FlowSessionId.Create(),
            new ViewContract("consumer.detail"),
            viewModel,
            typeof(ConsumerViewModel),
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["source"] = "package-consumer",
            });

        DialogOutcome<string> dialog = DialogOutcome<string>.Completed("accepted");
        OperationOutcome<int> operation = OperationOutcome<int>.Succeeded(42);
        WorkflowOutcome<string> workflow = WorkflowOutcome<string>.Completed("finished");
        var action = new FlowAction(
            new ActionKey("consumer.continue"),
            "Continue",
            ActionRole.Primary,
            SemanticTone.Success,
            isDefault: true);

        return descriptor.Contract == new ViewContract("consumer.detail")
            && ReferenceEquals(descriptor.ViewModel, viewModel)
            && descriptor.Metadata["source"] == "package-consumer"
            && dialog == DialogOutcome<string>.Completed("accepted")
            && operation == OperationOutcome<int>.Succeeded(42)
            && workflow == WorkflowOutcome<string>.Completed("finished")
            && action.Key == new ActionKey("consumer.continue")
            && action.Label == "Continue"
            && action.IsDefault;
    }

    private static async ValueTask<bool> VerifyNavigationAndWorkflowAsync()
    {
        RegionKey region = new("consumer.main");
        RouteKey route = new("consumer.home");
        RouteKey detailsRoute = new("consumer.details");
        NavigationRegistry navigation = new NavigationRegistryBuilder()
            .AddPage<ConsumerViewModel>(
                route,
                new ViewContract("consumer.home"),
                static _ => ValueTask.FromResult(
                    new NavigationRouteContent(new ConsumerViewModel("home"), new ConsumerScope())))
            .AddPage<ConsumerDetailsViewModel>(
                detailsRoute,
                new ViewContract("consumer.details"),
                static _ => ValueTask.FromResult(
                    new NavigationRouteContent(new ConsumerDetailsViewModel(42), new ConsumerScope())))
            .AddRegion(new NavigationRegionRegistration(region, route, requireContent: true))
            .Build();

        var navigationPresenter = new ConsumerNavigationPresenter();
        await using var navigationService = new NavigationService(navigation, navigationPresenter);
        await navigationService.StartAsync().ConfigureAwait(false);
        NavigationResult pushed = await navigationService
            .NavigateAsync<ConsumerDetailsViewModel>(region)
            .ConfigureAwait(false);
        NavigationResult backed = await navigationService.BackAsync(region).ConfigureAwait(false);
        NavigationSnapshot beforeShutdown = navigationService.GetSnapshot(region);
        await navigationService.ShutdownAsync().ConfigureAwait(false);

        StepKey first = new("consumer.first");
        StepKey second = new("consumer.second");
        WorkflowDefinition<WorkflowContext, string> workflow =
            new WorkflowDefinitionBuilder<WorkflowContext, string>(new WorkflowKey("consumer.setup"), 1)
                .AddStep<ConsumerViewModel>(
                    first,
                    new ViewContract("consumer.first"),
                    static (_, _) => ValueTask.FromResult(
                        new WorkflowStepActivation(new ConsumerViewModel("first"), new ConsumerScope())))
                .AddStep<ConsumerViewModel>(
                    second,
                    new ViewContract("consumer.second"),
                    static (_, _) => ValueTask.FromResult(
                        new WorkflowStepActivation(new ConsumerViewModel("second"), new ConsumerScope())))
                .AddTransition(first, second)
                .StartWith(first)
                .FinishWith(static context => context.Result)
                .Build();

        byte[] mutablePayload = [1, 2, 3];
        var checkpoint = new WorkflowCheckpointEnvelope(
            workflow.Key,
            workflow.SchemaVersion,
            first,
            [first],
            mutablePayload);
        mutablePayload[0] = 99;
        WorkflowCheckpointEnvelope validatedCheckpoint = await WorkflowCheckpointRestoreValidator
            .ValidateAsync(checkpoint, workflow)
            .ConfigureAwait(false);

        var workflowPresenter = new ConsumerWorkflowPresenter();
        await using var workflowSession = new WorkflowSession<WorkflowContext, string>(
            workflow,
            new WorkflowContext("workflow-result"),
            workflowPresenter);
        WorkflowTransition<string> started = await workflowSession.StartAsync().ConfigureAwait(false);
        WorkflowTransition<string> advanced = await workflowSession.NextAsync().ConfigureAwait(false);
        WorkflowTransition<string> finished = await workflowSession.FinishAsync().ConfigureAwait(false);

        return navigation.ContainsRoute(route)
            && navigation.ContainsRoute(detailsRoute)
            && navigation.Regions.Count == 1
            && pushed.Kind == NavigationResultKind.Navigated
            && pushed.Snapshot.Entries.Count == 2
            && backed.Kind == NavigationResultKind.Navigated
            && beforeShutdown.Current?.Route == route
            && navigationPresenter.PresentationCount >= 3
            && workflow.Start == first
            && workflow.Steps.Count == 2
            && workflow.Edges[first].Count == 1
            && validatedCheckpoint.Payload.ToArray()[0] == 1
            && started.Kind == WorkflowTransitionKind.Moved
            && advanced.Kind == WorkflowTransitionKind.Moved
            && advanced.Snapshot.CurrentStep == second
            && finished.Kind == WorkflowTransitionKind.Completed
            && finished.Outcome == WorkflowOutcome<string>.Completed("workflow-result")
            && workflowPresenter.PresentationCount == 2;
    }

    private static async ValueTask<bool> VerifyDialogAsync()
    {
        var presenter = new CompletingDialogPresenter();
        PresenterKey presenterKey = new("consumer.modal");
        DialogKey dialogKey = new("consumer.confirm");
        DialogRegistry registry = new DialogRegistryBuilder()
            .Add(new DialogRegistration<ConsumerViewModel, string, bool>(
                dialogKey,
                new ViewContract("consumer.confirm"),
                presenterKey,
                static (request, _, _) => ValueTask.FromResult(
                    new DialogContent<ConsumerViewModel>(new ConsumerViewModel(request), new ConsumerScope()))))
            .Build();
        var presenters = new DialogPresenterRegistry(
            new Dictionary<PresenterKey, IDialogPresenter>
            {
                [presenterKey] = presenter,
            });

        await using var service = new DialogService(registry, presenters);
        DialogOutcome<bool> outcome = await service
            .ShowAsync<ConsumerViewModel, string, bool>(dialogKey, "confirm")
            .ConfigureAwait(false);

        return outcome == DialogOutcome<bool>.Completed(false)
            && presenter.FirstCompletionAccepted
            && !presenter.SecondCompletionAccepted
            && presenter.Lease.CloseCount == 1
            && presenter.Lease.DisposeCount == 1;
    }

    private static async ValueTask<bool> VerifyOperationAsync()
    {
        var runner = new OperationRunner(options: new OperationRunnerOptions
        {
            RetainedFinishedOperationLimit = 4,
        });
        OperationOutcome<int> outcome = await runner.TryRunAsync(
            new OperationRequest(new OperationKey("consumer.load"), CorrelationId: "package-consumer"),
            static (context, _) =>
            {
                context.Report(new OperationProgress(1, "complete"));
                return ValueTask.FromResult(42);
            }).ConfigureAwait(false);
        IReadOnlyList<OperationSnapshot> snapshots = runner.GetSnapshots();

        return outcome == OperationOutcome<int>.Succeeded(42)
            && snapshots.Count == 1
            && snapshots[0].State == OperationState.Finished
            && snapshots[0].Outcome == OperationOutcomeKind.Succeeded
            && snapshots[0].Progress?.Fraction == 1;
    }

    private sealed record ConsumerViewModel(string State);

    private sealed record ConsumerDetailsViewModel(int Id);

    private sealed record WorkflowContext(string Result);

    private sealed class ConsumerScope : IDisposable
    {
        public void Dispose()
        {
        }
    }

    private sealed class CompletingDialogPresenter : IDialogPresenter
    {
        public CountingLease Lease { get; } = new();

        public bool FirstCompletionAccepted { get; private set; }

        public bool SecondCompletionAccepted { get; private set; }

        public async ValueTask<IFlowPresentationLease> PresentAsync<TResult>(
            DialogPresentation<TResult> presentation,
            CancellationToken cancellationToken)
        {
            FirstCompletionAccepted = await presentation.Controller
                .CompleteAsync(default!, cancellationToken)
                .ConfigureAwait(false);
            SecondCompletionAccepted = await presentation.Controller
                .CompleteAsync(default!, cancellationToken)
                .ConfigureAwait(false);
            return Lease;
        }
    }

    private sealed class ConsumerNavigationPresenter : INavigationRegionPresenter
    {
        public int PresentationCount { get; private set; }

        public ValueTask<IFlowPresentationLease> PresentAsync(
            RegionKey region,
            FlowContentDescriptor content,
            NavigationPresentationContext context,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            PresentationCount++;
            return ValueTask.FromResult<IFlowPresentationLease>(new CountingLease());
        }

        public ValueTask ClearAsync(RegionKey region, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class ConsumerWorkflowPresenter : IWorkflowPresenter
    {
        public int PresentationCount { get; private set; }

        public ValueTask<IFlowPresentationLease> PresentAsync(
            FlowContentDescriptor content,
            WorkflowPresentationContext context,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            PresentationCount++;
            return ValueTask.FromResult<IFlowPresentationLease>(new CountingLease());
        }
    }

    private sealed class CountingLease : IFlowPresentationLease
    {
        public int CloseCount { get; private set; }

        public int DisposeCount { get; private set; }

        public ValueTask CloseAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CloseCount++;
            return ValueTask.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            DisposeCount++;
            return ValueTask.CompletedTask;
        }
    }
}
