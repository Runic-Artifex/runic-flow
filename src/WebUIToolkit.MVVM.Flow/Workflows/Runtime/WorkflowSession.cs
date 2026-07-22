using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using WebUIToolkit.MVVM.Flow;

namespace WebUIToolkit.MVVM.Workflows;

/// <summary>
/// Executes one serialized instance of a typed workflow definition.
/// </summary>
/// <remarks>
/// The context belongs to the application. Back changes visited presentation history and does
/// not roll back mutations or side effects performed by step commits.
/// </remarks>
public sealed class WorkflowSession<TContext, TResult> : IAsyncDisposable
{
    private static readonly TimeSpan DefaultTeardownTimeout = TimeSpan.FromSeconds(30);
    private static readonly AsyncLocal<WorkflowSession<TContext, TResult>?> ExecutingSession = new();
    private readonly WorkflowDefinition<TContext, TResult> _definition;
    private readonly TContext _context;
    private readonly IWorkflowPresenter _presenter;
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _teardownTimeout;
    private readonly object _stateGate = new();
    private readonly SemaphoreSlim _mutex = new(1, 1);
    private readonly CancellationTokenSource _lifetimeSource = new();
    private readonly List<HistoryEntry> _history = [];
    private readonly Lazy<Task> _disposeOperation;
    private WorkflowStepSession? _current;
    private WorkflowSnapshot _snapshot;
    private WorkflowOutcome<TResult>? _outcome;
    private int _finishFactoryState;
    private int _disposeRequested;
    private bool _finishPrepared;
    private bool _started;
    private bool _terminated;

    /// <summary>Initializes a workflow session.</summary>
    public WorkflowSession(
        WorkflowDefinition<TContext, TResult> definition,
        TContext context,
        IWorkflowPresenter presenter)
        : this(definition, context, presenter, TimeProvider.System, DefaultTeardownTimeout)
    {
    }

    /// <summary>Initializes a workflow session with a bounded, clock-driven teardown policy.</summary>
    /// <remarks>
    /// When a teardown stage times out, its dependent resources are deliberately abandoned because
    /// the timed-out operation may still be using them. Late task faults are observed and the timeout
    /// is reported through the normal lifecycle or cleanup exception boundary.
    /// </remarks>
    public WorkflowSession(
        WorkflowDefinition<TContext, TResult> definition,
        TContext context,
        IWorkflowPresenter presenter,
        TimeProvider timeProvider,
        TimeSpan teardownTimeout)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(presenter);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(teardownTimeout, TimeSpan.Zero);

        _definition = definition;
        _context = context;
        _presenter = presenter;
        _timeProvider = timeProvider;
        _teardownTimeout = teardownTimeout;
        SessionId = FlowSessionId.Create();
        _snapshot = new WorkflowSnapshot(
            SessionId, definition.Key, currentStep: null, ReadOnlyCollection<StepKey>.Empty);
        _disposeOperation = new Lazy<Task>(DisposeCoreAsync, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    /// <summary>Gets the unique workflow session identifier.</summary>
    public FlowSessionId SessionId { get; }

    /// <summary>Gets the latest atomically committed snapshot.</summary>
    public WorkflowSnapshot Snapshot => Volatile.Read(ref _snapshot);

    /// <summary>Gets the terminal outcome after completion, cancellation, or abandonment.</summary>
    public WorkflowOutcome<TResult>? Outcome
    {
        get
        {
            lock (_stateGate)
            {
                return _outcome;
            }
        }
    }

    /// <summary>Raised synchronously after an immutable snapshot has been committed.</summary>
    public event EventHandler<WorkflowSnapshot>? SnapshotChanged;

    /// <summary>Starts the workflow at the first included step reachable from the configured start.</summary>
    public ValueTask<WorkflowTransition<TResult>> StartAsync(
        CancellationToken cancellationToken = default) =>
        MutateAsync(ct => StartCoreAsync(ct), cancellationToken);

    /// <summary>Moves over the first available outgoing edge, skipping excluded steps.</summary>
    public ValueTask<WorkflowTransition<TResult>> NextAsync(
        CancellationToken cancellationToken = default) =>
        MutateAsync(ct => MoveForwardAsync(requestedTarget: null, WorkflowPresentationReason.Forward, ct),
            cancellationToken);

    /// <summary>Moves to a specific graph step, skipping excluded steps through graph edges.</summary>
    public ValueTask<WorkflowTransition<TResult>> GoToAsync(
        StepKey target,
        CancellationToken cancellationToken = default) =>
        MutateAsync(ct => MoveForwardAsync(target, WorkflowPresentationReason.Redirect, ct),
            cancellationToken);

    /// <summary>Dispatches a built-in or ViewModel-provided custom action.</summary>
    public ValueTask<WorkflowTransition<TResult>> DispatchActionAsync(
        ActionKey action,
        CancellationToken cancellationToken = default) =>
        MutateAsync(ct => DispatchActionCoreAsync(action, ct), cancellationToken);

    /// <summary>Moves to the most recent still-included step in actual visited history.</summary>
    public ValueTask<WorkflowTransition<TResult>> BackAsync(
        CancellationToken cancellationToken = default) =>
        MutateAsync(BackCoreAsync, cancellationToken);

    /// <summary>Validates, commits, and completes using the typed result factory.</summary>
    public ValueTask<WorkflowTransition<TResult>> FinishAsync(
        CancellationToken cancellationToken = default) =>
        MutateAsync(FinishCoreAsync, cancellationToken);

    /// <summary>Requests ordinary cancellation and consults the current cancel guard.</summary>
    public ValueTask<WorkflowTransition<TResult>> CancelAsync(
        CancellationToken cancellationToken = default) =>
        CancelAsync(bypassGuard: false, cancellationToken);

    /// <summary>Requests cancellation, optionally bypassing the guard for host shutdown.</summary>
    public ValueTask<WorkflowTransition<TResult>> CancelAsync(
        bool bypassGuard,
        CancellationToken cancellationToken = default) =>
        MutateAsync(ct => CancelCoreAsync(bypassGuard, ct), cancellationToken);

    /// <summary>Ends the workflow as abandoned without invoking a cancel guard or result factory.</summary>
    public ValueTask<WorkflowTransition<TResult>> AbandonAsync() =>
        MutateAsync(_ => AbandonCoreAsync(), CancellationToken.None);

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        if (ReferenceEquals(ExecutingSession.Value, this))
        {
            return ValueTask.FromException(new FlowReentrancyException(
                "A workflow session cannot dispose itself from one of its mutations.",
                FlowFeature.Workflow,
                _definition.Key.Value,
                SessionId));
        }

        if (Interlocked.Exchange(ref _disposeRequested, 1) == 0)
        {
            CancelLifetime();
        }

        return new ValueTask(_disposeOperation.Value);
    }

    private async ValueTask<WorkflowTransition<TResult>> StartCoreAsync(CancellationToken cancellationToken)
    {
        if (_started)
        {
            throw new InvalidOperationException("The workflow session has already started.");
        }

        ThrowIfTerminated();
        StepKey start = ResolveIncludedTarget(_definition.Start);
        WorkflowStepSession target = await CreateAndPresentAsync(
            start, WorkflowPresentationReason.Start, [start], cancellationToken).ConfigureAwait(false);

        _started = true;
        _current = target;
        _history.Add(new HistoryEntry(start));
        List<Exception> postCommitFailures = PublishSnapshot(start, []);
        await CapturePostCommitAsync(
            () => InvokeActivatedAsync(target, CancellationToken.None), postCommitFailures)
            .ConfigureAwait(false);
        ThrowPostCommitFailures(postCommitFailures);
        return Transition(WorkflowTransitionKind.Moved);
    }

    private async ValueTask<WorkflowTransition<TResult>> MoveForwardAsync(
        StepKey? requestedTarget,
        WorkflowPresentationReason reason,
        CancellationToken cancellationToken)
    {
        _finishPrepared = false;
        WorkflowStepSession current = RequireCurrent();
        WorkflowValidationResult validation = await ValidateAsync(current, cancellationToken).ConfigureAwait(false);
        if (!validation.IsValid)
        {
            ThrowPostCommitFailures(PublishSnapshot(current.Step.Key, validation.Issues));
            return Transition(WorkflowTransitionKind.Stayed);
        }

        if (requestedTarget is null && !_definition.Edges.ContainsKey(current.Step.Key))
        {
            ThrowPostCommitFailures(PublishSnapshot(current.Step.Key, validation.Issues));
            return Transition(WorkflowTransitionKind.Stayed);
        }

        // Commit may mutate the application-owned context used by edge and include predicates.
        await CommitAndDeactivateAsync(current, cancellationToken).ConfigureAwait(false);
        StepKey? initialTarget = requestedTarget ?? SelectOutgoing(current.Step.Key);
        if (initialTarget is null)
        {
            ThrowPostCommitFailures(PublishSnapshot(current.Step.Key, validation.Issues));
            return Transition(WorkflowTransitionKind.Stayed);
        }

        StepKey targetKey = ResolveIncludedTarget(initialTarget.Value);
        List<StepKey> targetHistory = CurrentHistoryKeys();
        targetHistory.Add(targetKey);
        WorkflowStepSession target = await CreateAndPresentAsync(
            targetKey, reason, targetHistory, cancellationToken).ConfigureAwait(false);

        _current = target;
        HistoryEntry oldEntry = _history[^1];
        _history.Add(new HistoryEntry(targetKey));
        List<Exception> postCommitFailures = PublishSnapshot(targetKey, []);
        await CapturePostCommitAsync(
            () => InvokeDeactivatedAsync(current, CancellationToken.None),
            postCommitFailures).ConfigureAwait(false);
        await CapturePostCommitAsync(
            () => InvokeActivatedAsync(target, CancellationToken.None),
            postCommitFailures).ConfigureAwait(false);
        await ReleasePreviousAsync(current, oldEntry, postCommitFailures).ConfigureAwait(false);
        ThrowPostCommitFailures(postCommitFailures);
        return Transition(WorkflowTransitionKind.Moved);
    }

    private async ValueTask<WorkflowTransition<TResult>> BackCoreAsync(CancellationToken cancellationToken)
    {
        _finishPrepared = false;
        WorkflowStepSession current = RequireCurrent();
        if (_history.Count <= 1)
        {
            return Transition(WorkflowTransitionKind.Stayed);
        }

        await CommitAndDeactivateAsync(current, cancellationToken).ConfigureAwait(false);

        int targetIndex = _history.Count - 2;
        while (targetIndex >= 0 && !_definition.Steps[_history[targetIndex].Key].IsIncluded(_context))
        {
            targetIndex--;
        }

        if (targetIndex < 0)
        {
            throw new WorkflowGraphException(
                $"Workflow '{_definition.Key}' has no included visited step to return to.",
                _definition.Key,
                current.Step.Key,
                SessionId);
        }

        HistoryEntry targetEntry = _history[targetIndex];
        List<StepKey> targetHistory = [];
        for (int index = 0; index <= targetIndex; index++)
        {
            targetHistory.Add(_history[index].Key);
        }

        WorkflowStepSession target;
        if (targetEntry.Retained is not null)
        {
            target = targetEntry.Retained!;
            await target.PresentAsync(
                _presenter,
                new WorkflowPresentationContext(
                    _definition.Key, target.Step.Key, WorkflowPresentationReason.Back, targetHistory),
                cancellationToken).ConfigureAwait(false);
        }
        else
        {
            target = await CreateAndPresentAsync(
                targetEntry.Key,
                WorkflowPresentationReason.Back,
                targetHistory,
                cancellationToken).ConfigureAwait(false);
        }

        List<HistoryEntry> removedEntries = _history.GetRange(
            targetIndex + 1, _history.Count - targetIndex - 1);
        if (targetEntry.Retained is not null)
        {
            targetEntry.TakeRetained();
        }

        _history.RemoveRange(targetIndex + 1, _history.Count - targetIndex - 1);
        _current = target;
        List<Exception> failures = PublishSnapshot(target.Step.Key, []);
        await CapturePostCommitAsync(
            () => InvokeDeactivatedAsync(current, CancellationToken.None), failures).ConfigureAwait(false);
        await CapturePostCommitAsync(
            () => InvokeActivatedAsync(target, CancellationToken.None), failures).ConfigureAwait(false);
        await CapturePostCommitAsync(current.DisposeAsync, failures).ConfigureAwait(false);
        for (int index = removedEntries.Count - 1; index >= 0; index--)
        {
            await CapturePostCommitAsync(
                removedEntries[index].DisposeRetainedAsync, failures).ConfigureAwait(false);
        }
        ThrowPostCommitFailures(failures);
        return Transition(WorkflowTransitionKind.Moved);
    }

    private async ValueTask<WorkflowTransition<TResult>> DispatchActionCoreAsync(
        ActionKey action,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(action.Value))
        {
            throw new ArgumentException("A workflow action key cannot be empty.", nameof(action));
        }

        if (action == WorkflowActionKeys.Back)
        {
            return await BackCoreAsync(cancellationToken).ConfigureAwait(false);
        }

        if (action == WorkflowActionKeys.Next)
        {
            return await MoveForwardAsync(
                requestedTarget: null, WorkflowPresentationReason.Forward, cancellationToken)
                .ConfigureAwait(false);
        }

        if (action == WorkflowActionKeys.Finish)
        {
            return await FinishCoreAsync(cancellationToken).ConfigureAwait(false);
        }

        if (action == WorkflowActionKeys.Cancel)
        {
            return await CancelCoreAsync(bypassGuard: false, cancellationToken).ConfigureAwait(false);
        }

        // Any non-Finish action invalidates a prepared finish because a custom Stay handler may
        // mutate the application-owned context before the caller retries.
        _finishPrepared = false;
        WorkflowStepSession current = RequireCurrent();
        if (current.Activation.ViewModel is not IWorkflowActionHandler<TContext> handler)
        {
            return Transition(WorkflowTransitionKind.Stayed);
        }

        WorkflowActionResult result = await handler
            .HandleActionAsync(action, _context, cancellationToken).ConfigureAwait(false);
        return result.Kind switch
        {
            WorkflowActionResultKind.Stay => Transition(WorkflowTransitionKind.Stayed),
            WorkflowActionResultKind.GoTo when result.Target is StepKey target =>
                await MoveForwardAsync(target, WorkflowPresentationReason.Redirect, cancellationToken)
                    .ConfigureAwait(false),
            WorkflowActionResultKind.Finish =>
                await FinishCoreAsync(cancellationToken).ConfigureAwait(false),
            WorkflowActionResultKind.Cancel =>
                await CancelCoreAsync(bypassGuard: false, cancellationToken).ConfigureAwait(false),
            _ => throw new InvalidOperationException("The workflow action returned an invalid decision."),
        };
    }

    private async ValueTask<WorkflowTransition<TResult>> FinishCoreAsync(CancellationToken cancellationToken)
    {
        WorkflowStepSession current = RequireCurrent();
        if (!_finishPrepared)
        {
            WorkflowValidationResult validation = await ValidateAsync(current, cancellationToken).ConfigureAwait(false);
            if (!validation.IsValid)
            {
                ThrowPostCommitFailures(PublishSnapshot(current.Step.Key, validation.Issues));
                return Transition(WorkflowTransitionKind.Stayed);
            }

            await CommitAndDeactivateAsync(current, cancellationToken).ConfigureAwait(false);
            _finishPrepared = true;
        }
        if (Interlocked.CompareExchange(ref _finishFactoryState, 1, 0) != 0)
        {
            return Transition(WorkflowTransitionKind.Completed, _outcome);
        }

        TResult result;
        try
        {
            result = _definition.CreateResult(_context);
            Volatile.Write(ref _finishFactoryState, 2);
        }
        catch
        {
            Volatile.Write(ref _finishFactoryState, 0);
            throw;
        }

        WorkflowOutcome<TResult> outcome = WorkflowOutcome<TResult>.Completed(result);
        _finishPrepared = false;
        await TerminateAsync(outcome, currentAlreadyDeactivated: true).ConfigureAwait(false);
        return Transition(WorkflowTransitionKind.Completed, outcome);
    }

    private async ValueTask<WorkflowTransition<TResult>> CancelCoreAsync(
        bool bypassGuard,
        CancellationToken cancellationToken)
    {
        _finishPrepared = false;
        WorkflowStepSession current = RequireCurrent();
        if (!bypassGuard && current.Activation.ViewModel is IWorkflowCancelGuard<TContext> guard &&
            !await guard.CanCancelAsync(_context, cancellationToken).ConfigureAwait(false))
        {
            return Transition(WorkflowTransitionKind.Stayed);
        }

        WorkflowOutcome<TResult> outcome = WorkflowOutcome<TResult>.Cancelled();
        await TerminateAsync(outcome).ConfigureAwait(false);
        return Transition(WorkflowTransitionKind.Cancelled, outcome);
    }

    private async ValueTask<WorkflowTransition<TResult>> AbandonCoreAsync()
    {
        RequireCurrent();
        WorkflowOutcome<TResult> outcome = WorkflowOutcome<TResult>.Abandoned();
        await TerminateAsync(outcome).ConfigureAwait(false);
        return Transition(WorkflowTransitionKind.Abandoned, outcome);
    }

    private async ValueTask TerminateAsync(
        WorkflowOutcome<TResult> outcome,
        bool currentAlreadyDeactivated = false)
    {
        _terminated = true;
        SetOutcome(outcome);
        WorkflowStepSession? current = _current;
        _current = null;
        List<Exception> failures = PublishSnapshot(currentStep: null, []);
        if (current is not null)
        {
            if (!currentAlreadyDeactivated)
            {
                await CapturePostCommitAsync(
                    () => InvokeDeactivatingAsync(current, CancellationToken.None), failures)
                    .ConfigureAwait(false);
            }

            await CapturePostCommitAsync(
                () => InvokeDeactivatedAsync(current, CancellationToken.None), failures).ConfigureAwait(false);
            await CapturePostCommitAsync(current.DisposeAsync, failures).ConfigureAwait(false);
        }

        for (int index = _history.Count - 1; index >= 0; index--)
        {
            await CapturePostCommitAsync(_history[index].DisposeRetainedAsync, failures).ConfigureAwait(false);
        }

        _history.Clear();
        ThrowPostCommitFailures(failures);
    }

    private async ValueTask<WorkflowStepSession> CreateAndPresentAsync(
        StepKey stepKey,
        WorkflowPresentationReason reason,
        IReadOnlyList<StepKey> history,
        CancellationToken cancellationToken)
    {
        WorkflowStepDefinition<TContext> step = _definition.Steps[stepKey];
        WorkflowStepActivation activation = await step.ActivateAsync(_context, cancellationToken)
            .ConfigureAwait(false);
        if (!step.ViewModelType.IsInstanceOfType(activation.ViewModel))
        {
            await DisposeActivationAsync(
                activation, _timeProvider, _teardownTimeout).ConfigureAwait(false);
            throw new InvalidOperationException(
                $"Workflow step '{step.Key}' activated '{activation.ViewModel.GetType()}', " +
                $"which is not assignable to declared type '{step.ViewModelType}'.");
        }

        var session = new WorkflowStepSession(
            SessionId, step, activation, _timeProvider, _teardownTimeout);
        try
        {
            if (activation.ViewModel is IFlowInitializable<TContext> initializable)
            {
                await initializable.InitializeAsync(_context, cancellationToken).ConfigureAwait(false);
            }

            await InvokeActivatingAsync(session, cancellationToken).ConfigureAwait(false);
            await session.PresentAsync(
                _presenter,
                new WorkflowPresentationContext(_definition.Key, stepKey, reason, history),
                cancellationToken).ConfigureAwait(false);
            return session;
        }
        catch (Exception primaryException)
        {
            try
            {
                await session.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception cleanupException)
            {
                throw new FlowCleanupException(
                    "A workflow step activation failed and cleanup also failed.",
                    FlowFeature.Workflow,
                    [cleanupException],
                    _definition.Key.Value,
                    SessionId,
                    primaryException);
            }

            throw;
        }
    }

    private StepKey ResolveIncludedTarget(StepKey initialTarget)
    {
        HashSet<StepKey> visited = [];
        StepKey candidate = initialTarget;
        while (true)
        {
            if (!_definition.Steps.TryGetValue(candidate, out WorkflowStepDefinition<TContext>? step))
            {
                throw new WorkflowGraphException(
                    $"Workflow '{_definition.Key}' selected missing step '{candidate}'.",
                    _definition.Key, candidate, SessionId);
            }

            if (!visited.Add(candidate))
            {
                throw new WorkflowGraphException(
                    $"Workflow '{_definition.Key}' encountered a predicate redirect loop at '{candidate}'.",
                    _definition.Key, candidate, SessionId);
            }

            if (step.IsIncluded(_context))
            {
                return candidate;
            }

            StepKey? next = SelectOutgoing(candidate);
            if (next is null)
            {
                throw new WorkflowGraphException(
                    $"Excluded workflow step '{candidate}' has no available outgoing edge.",
                    _definition.Key, candidate, SessionId);
            }

            candidate = next.Value;
        }
    }

    private StepKey? SelectOutgoing(StepKey source)
    {
        if (!_definition.Edges.TryGetValue(source, out IReadOnlyList<WorkflowEdge<TContext>>? outgoing))
        {
            return null;
        }

        for (int index = 0; index < outgoing.Count; index++)
        {
            if (outgoing[index].IsAvailable(_context))
            {
                return outgoing[index].To;
            }
        }

        return null;
    }

    private async ValueTask<WorkflowValidationResult> ValidateAsync(
        WorkflowStepSession current,
        CancellationToken cancellationToken)
    {
        if (current.Activation.ViewModel is not IWorkflowStepValidator<TContext> validator)
        {
            return WorkflowValidationResult.Valid;
        }

        return await validator.ValidateAsync(_context, cancellationToken).ConfigureAwait(false) ??
            throw new InvalidOperationException("A workflow validator returned null.");
    }

    private async ValueTask CommitAndDeactivateAsync(
        WorkflowStepSession current,
        CancellationToken cancellationToken)
    {
        if (current.Activation.ViewModel is IWorkflowStepCommit<TContext> commit)
        {
            await commit.CommitAsync(_context, cancellationToken).ConfigureAwait(false);
        }

        await InvokeDeactivatingAsync(current, cancellationToken).ConfigureAwait(false);
    }

    private static async ValueTask ReleasePreviousAsync(
        WorkflowStepSession previous,
        HistoryEntry entry,
        List<Exception> failures)
    {
        if (previous.Step.Retention == WorkflowStepRetention.RetainVisited)
        {
            await CapturePostCommitAsync(previous.ClosePresentationAsync, failures).ConfigureAwait(false);
            entry.Retain(previous);
        }
        else
        {
            await CapturePostCommitAsync(previous.DisposeAsync, failures).ConfigureAwait(false);
        }
    }

    private async ValueTask<WorkflowTransition<TResult>> MutateAsync(
        Func<CancellationToken, ValueTask<WorkflowTransition<TResult>>> mutation,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(
            Volatile.Read(ref _disposeRequested) != 0,
            this);

        if (ReferenceEquals(ExecutingSession.Value, this))
        {
            throw new FlowReentrancyException(
                "A workflow session cannot be mutated re-entrantly.",
                FlowFeature.Workflow,
                _definition.Key.Value,
                SessionId);
        }

        using CancellationTokenSource linkedSource = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, _lifetimeSource.Token);
        await _mutex.WaitAsync(linkedSource.Token).ConfigureAwait(false);
        ExecutingSession.Value = this;
        try
        {
            return await mutation(linkedSource.Token).ConfigureAwait(false);
        }
        finally
        {
            ExecutingSession.Value = null;
            _mutex.Release();
        }
    }

    private WorkflowStepSession RequireCurrent()
    {
        if (!_started)
        {
            throw new InvalidOperationException("The workflow session has not started.");
        }

        ThrowIfTerminated();
        return _current ?? throw new InvalidOperationException("The workflow has no current step.");
    }

    private void ThrowIfTerminated()
    {
        if (_terminated)
        {
            throw new InvalidOperationException("The workflow session has already terminated.");
        }
    }

    private List<Exception> PublishSnapshot(
        StepKey? currentStep,
        IReadOnlyList<WorkflowValidationIssue> validationIssues)
    {
        var snapshot = new WorkflowSnapshot(
            SessionId, _definition.Key, currentStep, CurrentHistoryKeys(), validationIssues);
        Volatile.Write(ref _snapshot, snapshot);
        List<Exception> failures = [];
        EventHandler<WorkflowSnapshot>? handlers = SnapshotChanged;
        if (handlers is not null)
        {
            foreach (Delegate handler in handlers.GetInvocationList())
            {
                try
                {
                    ((EventHandler<WorkflowSnapshot>)handler)(this, snapshot);
                }
                catch (Exception exception)
                {
                    failures.Add(exception);
                }
            }
        }

        return failures;
    }

    private List<StepKey> CurrentHistoryKeys()
    {
        List<StepKey> keys = new(_history.Count);
        for (int index = 0; index < _history.Count; index++)
        {
            keys.Add(_history[index].Key);
        }

        return keys;
    }

    private WorkflowTransition<TResult> Transition(
        WorkflowTransitionKind kind,
        WorkflowOutcome<TResult>? outcome = null) =>
        new(kind, Snapshot, outcome);

    private static ValueTask InvokeActivatingAsync(
        WorkflowStepSession session,
        CancellationToken cancellationToken) =>
        session.Activation.ViewModel is IFlowActivation lifecycle
            ? lifecycle.ActivatingAsync(
                new FlowActivationContext(session.Descriptor.SessionId, session.Step.Contract),
                cancellationToken)
            : ValueTask.CompletedTask;

    private static ValueTask InvokeActivatedAsync(
        WorkflowStepSession session,
        CancellationToken cancellationToken) =>
        session.Activation.ViewModel is IFlowActivation lifecycle
            ? lifecycle.ActivatedAsync(
                new FlowActivationContext(session.Descriptor.SessionId, session.Step.Contract),
                cancellationToken)
            : ValueTask.CompletedTask;

    private static ValueTask InvokeDeactivatingAsync(
        WorkflowStepSession session,
        CancellationToken cancellationToken) =>
        session.Activation.ViewModel is IFlowActivation lifecycle
            ? lifecycle.DeactivatingAsync(
                new FlowDeactivationContext(session.Descriptor.SessionId, session.Step.Contract),
                cancellationToken)
            : ValueTask.CompletedTask;

    private static ValueTask InvokeDeactivatedAsync(
        WorkflowStepSession session,
        CancellationToken cancellationToken) =>
        session.Activation.ViewModel is IFlowActivation lifecycle
            ? lifecycle.DeactivatedAsync(
                new FlowDeactivationContext(session.Descriptor.SessionId, session.Step.Contract),
                cancellationToken)
            : ValueTask.CompletedTask;

    private static async ValueTask CapturePostCommitAsync(
        Func<ValueTask> callback,
        List<Exception> failures)
    {
        try
        {
            await callback().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            failures.Add(exception);
        }
    }

    private void ThrowPostCommitFailures(List<Exception>? failures)
    {
        if (failures is { Count: > 0 })
        {
            throw new FlowLifecycleException(
                "One or more workflow callbacks failed after the state commit.",
                FlowFeature.Workflow,
                FlowLifecycleStage.Deactivated,
                failures,
                _definition.Key.Value,
                SessionId);
        }
    }

    private async Task DisposeCoreAsync()
    {
        bool acquired = false;
        using var admissionTimeoutSource = new CancellationTokenSource(
            _teardownTimeout, _timeProvider);
        try
        {
            await _mutex.WaitAsync(admissionTimeoutSource.Token).ConfigureAwait(false);
            acquired = true;
        }
        catch (OperationCanceledException) when (admissionTimeoutSource.IsCancellationRequested)
        {
            var timeout = new TimeoutException(
                "Workflow disposal could not acquire mutation ownership within the configured teardown timeout.");
            throw new FlowCleanupException(
                "Workflow disposal timed out while waiting for an active mutation to release ownership.",
                FlowFeature.Workflow,
                [timeout],
                _definition.Key.Value,
                SessionId);
        }

        try
        {
            List<Exception> failures = [];
            if (!_terminated)
            {
                _terminated = true;
                SetOutcome(_started ? WorkflowOutcome<TResult>.Abandoned() : null);
            }

            if (_current is not null)
            {
                await CapturePostCommitAsync(_current.DisposeAsync, failures).ConfigureAwait(false);
                _current = null;
            }

            for (int index = _history.Count - 1; index >= 0; index--)
            {
                await CapturePostCommitAsync(
                    _history[index].DisposeRetainedAsync, failures).ConfigureAwait(false);
            }

            _history.Clear();
            failures.AddRange(PublishSnapshot(currentStep: null, []));

            _lifetimeSource.Dispose();
            if (failures.Count > 0)
            {
                throw new FlowCleanupException(
                    "The workflow session encountered one or more cleanup failures.",
                    FlowFeature.Workflow,
                    failures,
                    _definition.Key.Value,
                    SessionId);
            }
        }
        finally
        {
            if (acquired)
            {
                _mutex.Release();
                _mutex.Dispose();
            }
        }
    }

    private void CancelLifetime()
    {
        try
        {
            _lifetimeSource.Cancel(throwOnFirstException: false);
        }
        catch (ObjectDisposedException)
        {
            // Disposal has already completed.
        }
    }

    private void SetOutcome(WorkflowOutcome<TResult>? outcome)
    {
        lock (_stateGate)
        {
            _outcome = outcome;
        }
    }

    private static async ValueTask DisposeActivationAsync(
        WorkflowStepActivation activation,
        TimeProvider timeProvider,
        TimeSpan teardownTimeout)
    {
        List<Exception> failures = [];
        bool timedOut = false;
        if (activation.OwnsViewModel && !ReferenceEquals(activation.ViewModel, activation.Scope))
        {
            try
            {
                await DisposeResourceAsync(
                    activation.ViewModel, timeProvider, teardownTimeout).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                failures.Add(exception);
                timedOut = exception is TimeoutException;
            }
        }

        if (!timedOut)
        {
            try
            {
                await DisposeResourceAsync(
                    activation.Scope, timeProvider, teardownTimeout).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }
        }

        if (failures.Count == 1)
        {
            throw failures[0];
        }

        if (failures.Count > 1)
        {
            throw new AggregateException(failures);
        }
    }

    private static async ValueTask DisposeResourceAsync(
        object resource,
        TimeProvider timeProvider,
        TimeSpan teardownTimeout)
    {
        if (resource is IAsyncDisposable asyncDisposable)
        {
            await WaitForTeardownAsync(
                asyncDisposable.DisposeAsync(), timeProvider, teardownTimeout).ConfigureAwait(false);
        }
        else if (resource is IDisposable disposable)
        {
            await WaitForTeardownAsync(
                new ValueTask(Task.Run(disposable.Dispose)), timeProvider, teardownTimeout)
                .ConfigureAwait(false);
        }
    }

    private static async ValueTask WaitForTeardownAsync(
        ValueTask operation,
        TimeProvider timeProvider,
        TimeSpan teardownTimeout)
    {
        Task task = operation.AsTask();
        try
        {
            await task.WaitAsync(teardownTimeout, timeProvider).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            ObserveLateFault(task);
            throw;
        }
    }

    private static async ValueTask WaitForTeardownAsync(
        ValueTask operation,
        CancellationToken timeoutToken)
    {
        Task task = operation.AsTask();
        try
        {
            await task.WaitAsync(timeoutToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (timeoutToken.IsCancellationRequested)
        {
            ObserveLateFault(task);
            throw new TimeoutException("Workflow resource teardown exceeded its configured timeout.");
        }
    }

    private static void ObserveLateFault(Task task)
    {
        _ = task.ContinueWith(
            static completed => _ = completed.Exception,
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private sealed class HistoryEntry
    {
        internal HistoryEntry(StepKey key) => Key = key;

        internal StepKey Key { get; }

        internal WorkflowStepSession? Retained { get; private set; }

        internal void Retain(WorkflowStepSession session) => Retained = session;

        internal WorkflowStepSession TakeRetained()
        {
            WorkflowStepSession retained = Retained ??
                throw new InvalidOperationException("The history entry has no retained session.");
            Retained = null;
            return retained;
        }

        internal async ValueTask DisposeRetainedAsync()
        {
            WorkflowStepSession? retained = Retained;
            Retained = null;
            if (retained is not null)
            {
                await retained.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    private sealed class WorkflowStepSession : IAsyncDisposable
    {
        private readonly Lazy<Task> _disposeOperation;
        private readonly TimeProvider _timeProvider;
        private readonly TimeSpan _teardownTimeout;
        private IFlowPresentationLease? _lease;
        private bool _teardownTimedOut;

        internal WorkflowStepSession(
            FlowSessionId workflowSessionId,
            WorkflowStepDefinition<TContext> step,
            WorkflowStepActivation activation,
            TimeProvider timeProvider,
            TimeSpan teardownTimeout)
        {
            Step = step;
            Activation = activation;
            _timeProvider = timeProvider;
            _teardownTimeout = teardownTimeout;
            Descriptor = new FlowContentDescriptor(
                FlowSessionId.Create(),
                step.Contract,
                activation.ViewModel,
                step.ViewModelType,
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["workflow.session"] = workflowSessionId.Value.ToString("D"),
                    ["workflow.step"] = step.Key.Value,
                });
            _disposeOperation = new Lazy<Task>(DisposeCoreAsync, LazyThreadSafetyMode.ExecutionAndPublication);
        }

        internal WorkflowStepDefinition<TContext> Step { get; }

        internal WorkflowStepActivation Activation { get; }

        internal FlowContentDescriptor Descriptor { get; }

        internal async ValueTask PresentAsync(
            IWorkflowPresenter presenter,
            WorkflowPresentationContext context,
            CancellationToken cancellationToken)
        {
            if (_lease is not null)
            {
                throw new InvalidOperationException("The workflow step is already presented.");
            }

            _lease = await presenter.PresentAsync(Descriptor, context, cancellationToken)
                .ConfigureAwait(false) ?? throw new InvalidOperationException(
                    "A workflow presenter returned a null lease.");
        }

        internal async ValueTask ClosePresentationAsync()
        {
            IFlowPresentationLease? lease = Interlocked.Exchange(ref _lease, null);
            if (lease is null)
            {
                return;
            }

            List<Exception> failures = [];
            using var timeoutSource = new CancellationTokenSource(
                _teardownTimeout, _timeProvider);
            try
            {
                await WaitForTeardownAsync(
                    lease.CloseAsync(timeoutSource.Token), timeoutSource.Token).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                failures.Add(exception);
                _teardownTimedOut = exception is TimeoutException;
            }

            if (!_teardownTimedOut)
            {
                try
                {
                    await WaitForTeardownAsync(
                        lease.DisposeAsync(), timeoutSource.Token).ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    failures.Add(exception);
                    _teardownTimedOut = exception is TimeoutException;
                }
            }

            if (failures.Count == 1)
            {
                throw failures[0];
            }

            if (failures.Count > 1)
            {
                throw new AggregateException(failures);
            }
        }

        public ValueTask DisposeAsync() => new(_disposeOperation.Value);

        private async Task DisposeCoreAsync()
        {
            List<Exception> failures = [];
            await CapturePostCommitAsync(ClosePresentationAsync, failures).ConfigureAwait(false);
            if (!_teardownTimedOut)
            {
                await CapturePostCommitAsync(
                    () => DisposeActivationAsync(
                        Activation, _timeProvider, _teardownTimeout), failures).ConfigureAwait(false);
            }
            if (failures.Count == 1)
            {
                throw failures[0];
            }

            if (failures.Count > 1)
            {
                throw new AggregateException(failures);
            }
        }
    }
}
