using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using RunicFlow;
using RunicFlow.Workflows;

namespace RunicFlow.Tests.WaveB;

internal static class WorkflowTests
{
    public static ValueTask BuilderRejectsInvalidGraphs()
    {
        StepKey start = new("start");
        StepKey missing = new("missing");

        TestAssert.True(Throws<WorkflowGraphException>(() =>
            new WorkflowDefinitionBuilder<TestContext, int>(new WorkflowKey("empty"), 1).Build()));
        TestAssert.True(Throws<WorkflowGraphException>(() =>
            new WorkflowDefinitionBuilder<TestContext, int>(new WorkflowKey("missing-start"), 1)
                .AddStep<PlainViewModel>(start, new ViewContract("flow/start"), Activate<PlainViewModel>)
                .FinishWith(static _ => 0)
                .Build()));
        TestAssert.True(Throws<WorkflowGraphException>(() =>
            new WorkflowDefinitionBuilder<TestContext, int>(new WorkflowKey("missing-edge"), 1)
                .AddStep<PlainViewModel>(start, new ViewContract("flow/start"), Activate<PlainViewModel>)
                .AddTransition(start, missing)
                .StartWith(start)
                .FinishWith(static _ => 0)
                .Build()));
        TestAssert.True(Throws<WorkflowGraphException>(() =>
        {
            var builder = new WorkflowDefinitionBuilder<TestContext, int>(new WorkflowKey("duplicate"), 1);
            builder.AddStep<PlainViewModel>(start, new ViewContract("flow/start"), Activate<PlainViewModel>);
            builder.AddStep<PlainViewModel>(start, new ViewContract("flow/start-2"), Activate<PlainViewModel>);
        }));
        return ValueTask.CompletedTask;
    }

    public static async ValueTask ConditionalStepsAreSkippedAndRetainedHistoryIsReused()
    {
        var context = new TestContext { IncludeOptional = false };
        StepKey start = new("start");
        StepKey optional = new("optional");
        StepKey review = new("review");
        int startActivations = 0;
        int reviewDisposals = 0;
        object? originalStart = null;
        WorkflowDefinition<TestContext, int> definition =
            new WorkflowDefinitionBuilder<TestContext, int>(new WorkflowKey("branch"), 1)
                .AddStep<PlainViewModel>(
                    start,
                    new ViewContract("flow/start"),
                    (_, _) =>
                    {
                        startActivations++;
                        originalStart = new PlainViewModel();
                        return ValueTask.FromResult(new WorkflowStepActivation(
                            originalStart,
                            new RecordingScope()));
                    },
                    retention: WorkflowStepRetention.RetainVisited)
                .AddStep<PlainViewModel>(
                    optional,
                    new ViewContract("flow/optional"),
                    Activate<PlainViewModel>,
                    includeWhen: static state => state.IncludeOptional)
                .AddStep<PlainViewModel>(
                    review,
                    new ViewContract("flow/review"),
                    (_, _) => ValueTask.FromResult(new WorkflowStepActivation(
                        new PlainViewModel(),
                        new RecordingScope(() => reviewDisposals++))))
                .AddTransition(start, optional)
                .AddTransition(optional, review)
                .StartWith(start)
                .FinishWith(static state => state.Value)
                .Build();
        var presenter = new RecordingWorkflowPresenter();
        await using var session = new WorkflowSession<TestContext, int>(definition, context, presenter);

        _ = await session.StartAsync();
        WorkflowTransition<int> forward = await session.NextAsync();

        TestAssert.Equal(WorkflowTransitionKind.Moved, forward.Kind);
        TestAssert.Equal(review, session.Snapshot.CurrentStep);
        TestAssert.SequenceEqual(new[] { start, review }, session.Snapshot.VisitedHistory);
        TestAssert.SequenceEqual(new[] { start, review }, presenter.PresentedSteps);

        WorkflowTransition<int> back = await session.BackAsync();
        TestAssert.Equal(WorkflowTransitionKind.Moved, back.Kind);
        TestAssert.Equal(start, session.Snapshot.CurrentStep);
        TestAssert.Equal(1, startActivations);
        TestAssert.True(ReferenceEquals(originalStart, presenter.Descriptors[^1].ViewModel));
        TestAssert.Equal(1, reviewDisposals);
    }

    public static async ValueTask ValidationFailureStaysWithoutCommitOrPresentation()
    {
        StepKey start = new("start");
        StepKey next = new("next");
        var viewModel = new ValidatingViewModel(
            WorkflowValidationResult.FromIssues(
                [new WorkflowValidationIssue("required", "A value is required.")]));
        WorkflowDefinition<TestContext, int> definition = TwoStepDefinition(
            "validation",
            start,
            next,
            (_, _) => ValueTask.FromResult(new WorkflowStepActivation(viewModel, new RecordingScope())));
        var presenter = new RecordingWorkflowPresenter();
        await using var session = new WorkflowSession<TestContext, int>(definition, new TestContext(), presenter);
        _ = await session.StartAsync();

        WorkflowTransition<int> transition = await session.NextAsync();

        TestAssert.Equal(WorkflowTransitionKind.Stayed, transition.Kind);
        TestAssert.Equal(start, transition.Snapshot.CurrentStep);
        TestAssert.Equal(1, transition.Snapshot.ValidationIssues.Count);
        TestAssert.Equal(0, viewModel.CommitCount);
        TestAssert.Equal(1, presenter.PresentedSteps.Count);
    }

    public static async ValueTask CommitAndPresenterFaultsLeaveCurrentSnapshot()
    {
        StepKey start = new("start");
        StepKey next = new("next");
        var commitFailure = new InvalidOperationException("commit failed");
        var committing = new ValidatingViewModel(WorkflowValidationResult.Valid, commitFailure);
        WorkflowDefinition<TestContext, int> commitDefinition = TwoStepDefinition(
            "commit-fault",
            start,
            next,
            (_, _) => ValueTask.FromResult(new WorkflowStepActivation(committing, new RecordingScope())));
        var presenter = new RecordingWorkflowPresenter();
        await using (var session = new WorkflowSession<TestContext, int>(commitDefinition, new TestContext(), presenter))
        {
            _ = await session.StartAsync();
            InvalidOperationException actual = await TestAssert.ThrowsAsync<InvalidOperationException>(async () =>
                _ = await session.NextAsync());
            TestAssert.True(ReferenceEquals(commitFailure, actual));
            TestAssert.Equal(start, session.Snapshot.CurrentStep);
            TestAssert.Equal(1, presenter.PresentedSteps.Count);
        }

        int failedTargetDisposals = 0;
        WorkflowDefinition<TestContext, int> presenterDefinition =
            new WorkflowDefinitionBuilder<TestContext, int>(new WorkflowKey("present-fault"), 1)
                .AddStep<PlainViewModel>(start, new ViewContract("flow/start"), Activate<PlainViewModel>)
                .AddStep<PlainViewModel>(
                    next,
                    new ViewContract("flow/next"),
                    (_, _) => ValueTask.FromResult(new WorkflowStepActivation(
                        new PlainViewModel(),
                        new RecordingScope(() => failedTargetDisposals++))))
                .AddTransition(start, next)
                .StartWith(start)
                .FinishWith(static _ => 0)
                .Build();
        var failingPresenter = new RecordingWorkflowPresenter(next);
        await using var failedSession = new WorkflowSession<TestContext, int>(
            presenterDefinition,
            new TestContext(),
            failingPresenter);
        _ = await failedSession.StartAsync();

        _ = await TestAssert.ThrowsAsync<InvalidOperationException>(async () =>
            _ = await failedSession.NextAsync());
        TestAssert.Equal(start, failedSession.Snapshot.CurrentStep);
        TestAssert.Equal(1, failedTargetDisposals);
    }

    public static async ValueTask FinishFactoryMayRetryAfterFaultAndCompletesOnce()
    {
        StepKey start = new("start");
        int factoryCalls = 0;
        WorkflowDefinition<TestContext, string?> definition =
            new WorkflowDefinitionBuilder<TestContext, string?>(new WorkflowKey("finish-retry"), 1)
                .AddStep<PlainViewModel>(start, new ViewContract("flow/start"), Activate<PlainViewModel>)
                .StartWith(start)
                .FinishWith(_ => ++factoryCalls == 1
                    ? throw new InvalidOperationException("retry")
                    : null)
                .Build();
        var presenter = new RecordingWorkflowPresenter();
        await using var session = new WorkflowSession<TestContext, string?>(
            definition,
            new TestContext(),
            presenter);
        _ = await session.StartAsync();

        _ = await TestAssert.ThrowsAsync<InvalidOperationException>(async () =>
            _ = await session.FinishAsync());
        TestAssert.Equal(start, session.Snapshot.CurrentStep);
        TestAssert.Equal(1, factoryCalls);

        WorkflowTransition<string?> completed = await session.FinishAsync();
        TestAssert.Equal(WorkflowTransitionKind.Completed, completed.Kind);
        TestAssert.Equal(WorkflowOutcomeKind.Completed, completed.Outcome!.Value.Kind);
        TestAssert.Equal<string?>(null, completed.Outcome.Value.Value);
        TestAssert.Equal(2, factoryCalls);
        TestAssert.Equal<StepKey?>(null, session.Snapshot.CurrentStep);
    }

    public static async ValueTask CancelGuardDenialCanBeBypassedByShutdown()
    {
        StepKey start = new("start");
        var guarded = new GuardedViewModel();
        WorkflowDefinition<TestContext, int> definition =
            new WorkflowDefinitionBuilder<TestContext, int>(new WorkflowKey("guarded"), 1)
                .AddStep<GuardedViewModel>(
                    start,
                    new ViewContract("flow/start"),
                    (_, _) => ValueTask.FromResult(new WorkflowStepActivation(guarded, new RecordingScope())))
                .StartWith(start)
                .FinishWith(static _ => 0)
                .Build();
        await using var session = new WorkflowSession<TestContext, int>(
            definition,
            new TestContext(),
            new RecordingWorkflowPresenter());
        _ = await session.StartAsync();

        WorkflowTransition<int> denied = await session.CancelAsync();
        TestAssert.Equal(WorkflowTransitionKind.Stayed, denied.Kind);
        TestAssert.Equal(start, session.Snapshot.CurrentStep);
        TestAssert.Equal(1, guarded.GuardCalls);

        WorkflowTransition<int> cancelled = await session.CancelAsync(bypassGuard: true);
        TestAssert.Equal(WorkflowTransitionKind.Cancelled, cancelled.Kind);
        TestAssert.Equal(WorkflowOutcomeKind.Cancelled, cancelled.Outcome!.Value.Kind);
        TestAssert.Equal(1, guarded.GuardCalls);
    }

    public static async ValueTask ConcurrentTransitionsAreSerialized()
    {
        StepKey start = new("start");
        StepKey middle = new("middle");
        StepKey end = new("end");
        WorkflowDefinition<TestContext, int> definition =
            new WorkflowDefinitionBuilder<TestContext, int>(new WorkflowKey("serialized"), 1)
                .AddStep<PlainViewModel>(start, new ViewContract("flow/start"), Activate<PlainViewModel>)
                .AddStep<PlainViewModel>(middle, new ViewContract("flow/middle"), Activate<PlainViewModel>)
                .AddStep<PlainViewModel>(end, new ViewContract("flow/end"), Activate<PlainViewModel>)
                .AddTransition(start, middle)
                .AddTransition(middle, end)
                .StartWith(start)
                .FinishWith(static _ => 0)
                .Build();
        var presenter = new BlockingWorkflowPresenter(middle);
        await using var session = new WorkflowSession<TestContext, int>(definition, new TestContext(), presenter);
        _ = await session.StartAsync();

        Task<WorkflowTransition<int>> first = session.NextAsync().AsTask();
        await presenter.Blocked.Task.ConfigureAwait(false);
        Task<WorkflowTransition<int>> second = session.NextAsync().AsTask();
        TestAssert.False(second.IsCompleted);
        presenter.Release.TrySetResult();

        TestAssert.Equal(WorkflowTransitionKind.Moved, (await first.ConfigureAwait(false)).Kind);
        TestAssert.Equal(WorkflowTransitionKind.Moved, (await second.ConfigureAwait(false)).Kind);
        TestAssert.Equal(end, session.Snapshot.CurrentStep);
        TestAssert.SequenceEqual(new[] { start, middle, end }, session.Snapshot.VisitedHistory);
    }

    public static async ValueTask ExcludedRedirectLoopFaultsWithoutChangingCurrentStep()
    {
        StepKey start = new("start");
        StepKey firstExcluded = new("excluded-a");
        StepKey secondExcluded = new("excluded-b");
        WorkflowDefinition<TestContext, int> definition =
            new WorkflowDefinitionBuilder<TestContext, int>(new WorkflowKey("redirect-loop"), 1)
                .AddStep<PlainViewModel>(start, new ViewContract("flow/start"), Activate<PlainViewModel>)
                .AddStep<PlainViewModel>(
                    firstExcluded,
                    new ViewContract("flow/a"),
                    Activate<PlainViewModel>,
                    includeWhen: static _ => false)
                .AddStep<PlainViewModel>(
                    secondExcluded,
                    new ViewContract("flow/b"),
                    Activate<PlainViewModel>,
                    includeWhen: static _ => false)
                .AddTransition(start, firstExcluded)
                .AddTransition(firstExcluded, secondExcluded)
                .AddTransition(secondExcluded, firstExcluded)
                .StartWith(start)
                .FinishWith(static _ => 0)
                .Build();
        await using var session = new WorkflowSession<TestContext, int>(
            definition,
            new TestContext(),
            new RecordingWorkflowPresenter());
        _ = await session.StartAsync();

        _ = await TestAssert.ThrowsAsync<WorkflowGraphException>(async () =>
            _ = await session.NextAsync());
        TestAssert.Equal(start, session.Snapshot.CurrentStep);
        TestAssert.SequenceEqual(new[] { start }, session.Snapshot.VisitedHistory);
    }

    public static async ValueTask BranchSelectionUsesContextAfterCommit()
    {
        StepKey start = new("start");
        StepKey beforeCommit = new("before-commit");
        StepKey afterCommit = new("after-commit");
        var context = new TestContext();
        var committing = new BranchingCommitViewModel();
        WorkflowDefinition<TestContext, int> definition =
            new WorkflowDefinitionBuilder<TestContext, int>(new WorkflowKey("post-commit-branch"), 1)
                .AddStep<BranchingCommitViewModel>(
                    start,
                    new ViewContract("flow/start"),
                    (_, _) => ValueTask.FromResult(new WorkflowStepActivation(
                        committing,
                        new RecordingScope())))
                .AddStep<PlainViewModel>(
                    beforeCommit,
                    new ViewContract("flow/before"),
                    Activate<PlainViewModel>)
                .AddStep<PlainViewModel>(
                    afterCommit,
                    new ViewContract("flow/after"),
                    Activate<PlainViewModel>)
                .AddTransition(start, beforeCommit, static state => !state.Committed)
                .AddTransition(start, afterCommit, static state => state.Committed)
                .StartWith(start)
                .FinishWith(static _ => 0)
                .Build();
        await using var session = new WorkflowSession<TestContext, int>(
            definition,
            context,
            new RecordingWorkflowPresenter());
        _ = await session.StartAsync();

        WorkflowTransition<int> transition = await session.NextAsync();

        TestAssert.Equal(WorkflowTransitionKind.Moved, transition.Kind);
        TestAssert.True(context.Committed);
        TestAssert.Equal(afterCommit, transition.Snapshot.CurrentStep);
    }

    public static async ValueTask FinishDeactivatesExactlyOnceInOrder()
    {
        StepKey start = new("start");
        var trace = new List<string>();
        var viewModel = new FinishLifecycleViewModel(trace);
        WorkflowDefinition<TestContext, int> definition =
            new WorkflowDefinitionBuilder<TestContext, int>(new WorkflowKey("finish-order"), 1)
                .AddStep<FinishLifecycleViewModel>(
                    start,
                    new ViewContract("flow/start"),
                    (_, _) => ValueTask.FromResult(new WorkflowStepActivation(
                        viewModel,
                        new RecordingScope(() => trace.Add("scope-dispose")))))
                .StartWith(start)
                .FinishWith(_ =>
                {
                    trace.Add("result-factory");
                    return 7;
                })
                .Build();
        await using var session = new WorkflowSession<TestContext, int>(
            definition,
            new TestContext(),
            new TraceWorkflowPresenter(trace));
        _ = await session.StartAsync();
        trace.Clear();

        WorkflowTransition<int> transition = await session.FinishAsync();

        TestAssert.Equal(WorkflowTransitionKind.Completed, transition.Kind);
        TestAssert.Equal(1, Count(trace, "deactivating"));
        TestAssert.True(trace.IndexOf("commit") < trace.IndexOf("deactivating"));
        TestAssert.True(trace.IndexOf("deactivating") < trace.IndexOf("result-factory"));
        TestAssert.True(trace.IndexOf("result-factory") < trace.IndexOf("deactivated"));
        TestAssert.True(trace.IndexOf("deactivated") < trace.IndexOf("lease-close"));
        TestAssert.True(trace.IndexOf("lease-close") < trace.IndexOf("scope-dispose"));
    }

    public static async ValueTask StayAfterFinishFaultResetsPreparationPhase()
    {
        StepKey start = new("start");
        int resultFactoryCalls = 0;
        var viewModel = new CountingActionViewModel();
        WorkflowDefinition<TestContext, int> definition =
            new WorkflowDefinitionBuilder<TestContext, int>(new WorkflowKey("finish-reset"), 1)
                .AddStep<CountingActionViewModel>(
                    start,
                    new ViewContract("flow/start"),
                    (_, _) => ValueTask.FromResult(new WorkflowStepActivation(
                        viewModel,
                        new RecordingScope())))
                .StartWith(start)
                .FinishWith(_ => ++resultFactoryCalls == 1
                    ? throw new InvalidOperationException("factory failed")
                    : 9)
                .Build();
        await using var session = new WorkflowSession<TestContext, int>(
            definition,
            new TestContext(),
            new RecordingWorkflowPresenter());
        _ = await session.StartAsync();

        _ = await TestAssert.ThrowsAsync<InvalidOperationException>(async () =>
            _ = await session.FinishAsync());
        WorkflowTransition<int> stayed = await session.DispatchActionAsync(new ActionKey("refresh"));
        TestAssert.Equal(WorkflowTransitionKind.Stayed, stayed.Kind);
        WorkflowTransition<int> completed = await session.FinishAsync();

        TestAssert.Equal(WorkflowTransitionKind.Completed, completed.Kind);
        TestAssert.Equal(2, viewModel.ValidationCount);
        TestAssert.Equal(2, viewModel.CommitCount);
        TestAssert.Equal(2, resultFactoryCalls);
    }

    public static async ValueTask HungLeaseCloseAbandonsDependentTeardown()
    {
        TimeSpan timeout = TimeSpan.FromMinutes(5);
        var clock = new ManualTimeProvider(new DateTimeOffset(2040, 1, 1, 0, 0, 0, TimeSpan.Zero));
        StepKey start = new("start");
        var scope = new RecordingScope();
        var presenter = new HangingWorkflowPresenter();
        WorkflowDefinition<TestContext, int> definition =
            new WorkflowDefinitionBuilder<TestContext, int>(new WorkflowKey("hung-teardown"), 1)
                .AddStep<PlainViewModel>(
                    start,
                    new ViewContract("flow/start"),
                    (_, _) => ValueTask.FromResult(new WorkflowStepActivation(
                        new PlainViewModel(),
                        scope)))
                .StartWith(start)
                .FinishWith(static _ => 1)
                .Build();
        await using var session = new WorkflowSession<TestContext, int>(
            definition,
            new TestContext(),
            presenter,
            clock,
            timeout);
        _ = await session.StartAsync();

        Task<WorkflowTransition<int>> pending = session.FinishAsync().AsTask();
        await presenter.Lease.CloseStarted.Task.ConfigureAwait(false);
        TestAssert.False(pending.IsCompleted);
        clock.Advance(timeout);

        FlowLifecycleException exception = await TestAssert.ThrowsAsync<FlowLifecycleException>(async () =>
            _ = await pending.ConfigureAwait(false));
        TestAssert.True(ContainsTimeout(exception.Failures));
        TestAssert.Equal(0, presenter.Lease.DisposeCount);
        TestAssert.Equal(0, scope.DisposeCount);
        TestAssert.Equal(WorkflowOutcomeKind.Completed, session.Outcome!.Value.Kind);
    }

    private static WorkflowDefinition<TestContext, int> TwoStepDefinition(
        string key,
        StepKey start,
        StepKey next,
        Func<TestContext, CancellationToken, ValueTask<WorkflowStepActivation>> startFactory) =>
        new WorkflowDefinitionBuilder<TestContext, int>(new WorkflowKey(key), 1)
            .AddStep<ValidatingViewModel>(start, new ViewContract("flow/start"), startFactory)
            .AddStep<PlainViewModel>(next, new ViewContract("flow/next"), Activate<PlainViewModel>)
            .AddTransition(start, next)
            .StartWith(start)
            .FinishWith(static state => state.Value)
            .Build();

    private static ValueTask<WorkflowStepActivation> Activate<TViewModel>(
        TestContext context,
        CancellationToken cancellationToken)
        where TViewModel : new()
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(new WorkflowStepActivation(new TViewModel(), new RecordingScope()));
    }

    private static bool Throws<TException>(Action action)
        where TException : Exception
    {
        try
        {
            action();
            return false;
        }
        catch (TException)
        {
            return true;
        }
    }

    private static int Count(List<string> values, string expected)
    {
        int count = 0;
        foreach (string value in values)
        {
            if (string.Equals(value, expected, StringComparison.Ordinal))
            {
                count++;
            }
        }

        return count;
    }

    private static bool ContainsTimeout(IReadOnlyList<Exception> failures)
    {
        foreach (Exception failure in failures)
        {
            if (failure is TimeoutException ||
                failure is AggregateException aggregate && aggregate.InnerException is TimeoutException)
            {
                return true;
            }
        }

        return false;
    }

    private sealed class TestContext
    {
        public bool IncludeOptional { get; init; }
        public int Value { get; init; } = 7;
        public bool Committed { get; set; }
    }

    private sealed class PlainViewModel;

    private sealed class ValidatingViewModel(
        WorkflowValidationResult validation,
        Exception? commitFailure = null)
        : IWorkflowStepValidator<TestContext>, IWorkflowStepCommit<TestContext>
    {
        public int CommitCount { get; private set; }

        public ValueTask<WorkflowValidationResult> ValidateAsync(
            TestContext context,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(validation);
        }

        public ValueTask CommitAsync(TestContext context, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CommitCount++;
            return commitFailure is null
                ? ValueTask.CompletedTask
                : ValueTask.FromException(commitFailure);
        }
    }

    private sealed class GuardedViewModel : IWorkflowCancelGuard<TestContext>
    {
        public int GuardCalls { get; private set; }

        public ValueTask<bool> CanCancelAsync(TestContext context, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            GuardCalls++;
            return ValueTask.FromResult(false);
        }
    }

    private sealed class BranchingCommitViewModel : IWorkflowStepCommit<TestContext>
    {
        public ValueTask CommitAsync(TestContext context, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            context.Committed = true;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FinishLifecycleViewModel(List<string> trace)
        : IWorkflowStepCommit<TestContext>, IFlowActivation
    {
        public ValueTask CommitAsync(TestContext context, CancellationToken cancellationToken)
        {
            trace.Add("commit");
            return ValueTask.CompletedTask;
        }

        public ValueTask ActivatingAsync(
            FlowActivationContext context,
            CancellationToken cancellationToken) => ValueTask.CompletedTask;

        public ValueTask ActivatedAsync(
            FlowActivationContext context,
            CancellationToken cancellationToken) => ValueTask.CompletedTask;

        public ValueTask DeactivatingAsync(
            FlowDeactivationContext context,
            CancellationToken cancellationToken)
        {
            trace.Add("deactivating");
            return ValueTask.CompletedTask;
        }

        public ValueTask DeactivatedAsync(
            FlowDeactivationContext context,
            CancellationToken cancellationToken)
        {
            trace.Add("deactivated");
            return ValueTask.CompletedTask;
        }
    }

    private sealed class CountingActionViewModel
        : IWorkflowStepValidator<TestContext>,
          IWorkflowStepCommit<TestContext>,
          IWorkflowActionHandler<TestContext>
    {
        public int ValidationCount { get; private set; }
        public int CommitCount { get; private set; }

        public ValueTask<WorkflowValidationResult> ValidateAsync(
            TestContext context,
            CancellationToken cancellationToken)
        {
            ValidationCount++;
            return ValueTask.FromResult(WorkflowValidationResult.Valid);
        }

        public ValueTask CommitAsync(TestContext context, CancellationToken cancellationToken)
        {
            CommitCount++;
            return ValueTask.CompletedTask;
        }

        public ValueTask<WorkflowActionResult> HandleActionAsync(
            ActionKey action,
            TestContext context,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(WorkflowActionResult.Stay());
    }

    private sealed class RecordingScope(Action? onDispose = null) : IAsyncDisposable
    {
        private int _disposeCount;

        public int DisposeCount => Volatile.Read(ref _disposeCount);

        public ValueTask DisposeAsync()
        {
            if (Interlocked.Increment(ref _disposeCount) == 1)
            {
                onDispose?.Invoke();
            }

            return ValueTask.CompletedTask;
        }
    }

    private class RecordingWorkflowPresenter(StepKey? failOn = null) : IWorkflowPresenter
    {
        private readonly List<StepKey> _presentedSteps = [];
        private readonly List<FlowContentDescriptor> _descriptors = [];

        public List<StepKey> PresentedSteps => _presentedSteps;
        public List<FlowContentDescriptor> Descriptors => _descriptors;

        public virtual ValueTask<IFlowPresentationLease> PresentAsync(
            FlowContentDescriptor content,
            WorkflowPresentationContext context,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (failOn == context.Step)
            {
                return ValueTask.FromException<IFlowPresentationLease>(
                    new InvalidOperationException("present failed"));
            }

            _presentedSteps.Add(context.Step);
            _descriptors.Add(content);
            return ValueTask.FromResult<IFlowPresentationLease>(new EmptyLease());
        }
    }

    private sealed class BlockingWorkflowPresenter(StepKey blockedStep) : RecordingWorkflowPresenter
    {
        public TaskCompletionSource Blocked { get; } = NewSignal();
        public TaskCompletionSource Release { get; } = NewSignal();

        public override async ValueTask<IFlowPresentationLease> PresentAsync(
            FlowContentDescriptor content,
            WorkflowPresentationContext context,
            CancellationToken cancellationToken)
        {
            if (context.Step == blockedStep)
            {
                Blocked.TrySetResult();
                await Release.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            }

            return await base.PresentAsync(content, context, cancellationToken).ConfigureAwait(false);
        }
    }

    private sealed class TraceWorkflowPresenter(List<string> trace) : IWorkflowPresenter
    {
        public ValueTask<IFlowPresentationLease> PresentAsync(
            FlowContentDescriptor content,
            WorkflowPresentationContext context,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult<IFlowPresentationLease>(new TraceLease(trace));
    }

    private sealed class HangingWorkflowPresenter : IWorkflowPresenter
    {
        public HangingWorkflowLease Lease { get; } = new();

        public ValueTask<IFlowPresentationLease> PresentAsync(
            FlowContentDescriptor content,
            WorkflowPresentationContext context,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult<IFlowPresentationLease>(Lease);
    }

    private sealed class HangingWorkflowLease : IFlowPresentationLease
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
            return ValueTask.CompletedTask;
        }
    }

    private sealed class TraceLease(List<string> trace) : IFlowPresentationLease
    {
        public ValueTask CloseAsync(CancellationToken cancellationToken)
        {
            trace.Add("lease-close");
            return ValueTask.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            trace.Add("lease-dispose");
            return ValueTask.CompletedTask;
        }
    }

    private sealed class EmptyLease : IFlowPresentationLease
    {
        public ValueTask CloseAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private static TaskCompletionSource NewSignal() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);
}
