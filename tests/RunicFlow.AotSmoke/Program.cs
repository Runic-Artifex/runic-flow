using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using RunicFlow.Dialogs;
using RunicFlow;
using RunicFlow.Navigation;
using RunicFlow.Operations;
using RunicFlow.Workflows;

namespace RunicFlow.AotSmoke;

internal static class Program
{
    public static async Task<int> Main()
    {
        RegionKey region = new("aot.main");
        RouteKey route = new("aot.home");
        RouteKey detailsRoute = new("aot.details");
        NavigationRegistry navigation = new NavigationRegistryBuilder()
            .AddPage<AotViewModel>(
                route,
                new ViewContract("aot.home"),
                static _ => ValueTask.FromResult(
                    new NavigationRouteContent(new AotViewModel(7), new AotScope())))
            .AddPage<AotDetailsViewModel>(
                detailsRoute,
                new ViewContract("aot.details"),
                static _ => ValueTask.FromResult(
                    new NavigationRouteContent(new AotDetailsViewModel(8), new AotScope())))
            .AddRegion(new NavigationRegionRegistration(region, route, requireContent: true))
            .Build();

        var navigationPresenter = new AotNavigationPresenter();
        await using var navigationService = new NavigationService(navigation, navigationPresenter);
        await navigationService.StartAsync().ConfigureAwait(false);
        NavigationResult pushed = await navigationService
            .NavigateAsync<AotDetailsViewModel>(region)
            .ConfigureAwait(false);
        NavigationResult backed = await navigationService.BackAsync(region).ConfigureAwait(false);
        await navigationService.ShutdownAsync().ConfigureAwait(false);

        StepKey step = new("aot.step");
        WorkflowDefinition<AotContext, int> workflow =
            new WorkflowDefinitionBuilder<AotContext, int>(new WorkflowKey("aot.workflow"), 1)
                .AddStep<AotViewModel>(
                    step,
                    new ViewContract("aot.step"),
                    static (_, _) => ValueTask.FromResult(
                        new WorkflowStepActivation(new AotViewModel(8), new AotScope())))
                .StartWith(step)
                .FinishWith(static context => context.Result)
                .Build();
        var checkpoint = new WorkflowCheckpointEnvelope(
            workflow.Key,
            workflow.SchemaVersion,
            step,
            [step],
            new byte[] { 7, 8, 9 });
        WorkflowCheckpointEnvelope validatedCheckpoint = await WorkflowCheckpointRestoreValidator
            .ValidateAsync(checkpoint, workflow)
            .ConfigureAwait(false);

        var workflowPresenter = new AotWorkflowPresenter();
        await using var workflowSession = new WorkflowSession<AotContext, int>(
            workflow,
            new AotContext(8),
            workflowPresenter);
        WorkflowTransition<int> started = await workflowSession.StartAsync().ConfigureAwait(false);
        WorkflowTransition<int> finished = await workflowSession.FinishAsync().ConfigureAwait(false);

        var operationRunner = new OperationRunner();
        OperationOutcome<int> operation = await operationRunner.TryRunAsync(
            new OperationRequest(new OperationKey("aot.operation")),
            static (context, _) =>
            {
                context.Report(new OperationProgress(1));
                return ValueTask.FromResult(9);
            }).ConfigureAwait(false);

        var presenter = new AotDialogPresenter();
        PresenterKey presenterKey = new("aot.modal");
        DialogRegistry dialogs = new DialogRegistryBuilder()
            .Add(new DialogRegistration<AotViewModel, int, int>(
                new DialogKey("aot.confirm"),
                new ViewContract("aot.confirm"),
                presenterKey,
                static (request, _, _) => ValueTask.FromResult(
                    new DialogContent<AotViewModel>(new AotViewModel(request), new AotScope()))))
            .Build();
        var presenters = new DialogPresenterRegistry(
            new Dictionary<PresenterKey, IDialogPresenter>
            {
                [presenterKey] = presenter,
            });
        await using var dialogService = new DialogService(dialogs, presenters);
        DialogOutcome<int> dialog = await dialogService
            .ShowAsync<AotViewModel, int, int>(7)
            .ConfigureAwait(false);

        IReadOnlyList<OperationSnapshot> snapshots = operationRunner.GetSnapshots();
        bool valid = navigation.ContainsRoute(route)
            && navigation.ContainsRoute(detailsRoute)
            && navigation.Regions.Count == 1
            && pushed.Kind == NavigationResultKind.Navigated
            && pushed.Snapshot.Entries.Count == 2
            && backed.Kind == NavigationResultKind.Navigated
            && backed.Snapshot.Current?.Route == route
            && navigationPresenter.PresentationCount >= 3
            && workflow.Start == step
            && workflow.Steps.Count == 1
            && validatedCheckpoint.Payload.Length == 3
            && started.Kind == WorkflowTransitionKind.Moved
            && finished.Kind == WorkflowTransitionKind.Completed
            && finished.Outcome == WorkflowOutcome<int>.Completed(8)
            && workflowPresenter.PresentationCount == 1
            && operation == OperationOutcome<int>.Succeeded(9)
            && snapshots.Count == 1
            && snapshots[0].State == OperationState.Finished
            && dialog == DialogOutcome<int>.Completed(0)
            && presenter.FirstCompletionAccepted
            && !presenter.SecondCompletionAccepted
            && presenter.Lease.CloseCount == 1
            && presenter.Lease.DisposeCount == 1;

        if (!valid)
        {
            Console.Error.WriteLine("FAIL: Flow Native-AOT public-kernel scenario produced an unexpected result.");
            return 1;
        }

        Console.WriteLine("PASS: Flow Native-AOT navigation, dialog, operation, workflow, and checkpoint scenarios.");
        return 0;
    }

    private sealed record AotViewModel(int Value);

    private sealed record AotDetailsViewModel(int Value);

    private sealed record AotContext(int Result);

    private sealed class AotScope : IDisposable
    {
        public void Dispose()
        {
        }
    }

    private sealed class AotDialogPresenter : IDialogPresenter
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

    private sealed class AotNavigationPresenter : INavigationRegionPresenter
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

    private sealed class AotWorkflowPresenter : IWorkflowPresenter
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
