using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using WebUIToolkit.MVVM.Flow;

namespace WebUIToolkit.MVVM.Workflows;

/// <summary>Identifies the observable result of a workflow mutation.</summary>
public enum WorkflowTransitionKind
{
    /// <summary>The workflow stayed on its current step.</summary>
    Stayed,

    /// <summary>The workflow moved to another step.</summary>
    Moved,

    /// <summary>The workflow completed with a typed result.</summary>
    Completed,

    /// <summary>The workflow was cancelled.</summary>
    Cancelled,

    /// <summary>The workflow was abandoned.</summary>
    Abandoned,
}

/// <summary>Represents the immutable observable state of one workflow session.</summary>
public sealed record WorkflowSnapshot
{
    /// <summary>Initializes a snapshot.</summary>
    public WorkflowSnapshot(
        FlowSessionId sessionId,
        WorkflowKey workflow,
        StepKey? currentStep,
        IReadOnlyList<StepKey> visitedHistory,
        IReadOnlyList<WorkflowValidationIssue>? validationIssues = null)
    {
        ArgumentNullException.ThrowIfNull(visitedHistory);
        SessionId = sessionId;
        Workflow = workflow;
        CurrentStep = currentStep;
        VisitedHistory = new ReadOnlyCollection<StepKey>([.. visitedHistory]);
        ValidationIssues = validationIssues is null
            ? ReadOnlyCollection<WorkflowValidationIssue>.Empty
            : new ReadOnlyCollection<WorkflowValidationIssue>([.. validationIssues]);
    }

    /// <summary>Gets the workflow session identifier.</summary>
    public FlowSessionId SessionId { get; }

    /// <summary>Gets the workflow key.</summary>
    public WorkflowKey Workflow { get; }

    /// <summary>Gets the current step, or null after termination.</summary>
    public StepKey? CurrentStep { get; }

    /// <summary>Gets the actual visited history.</summary>
    public IReadOnlyList<StepKey> VisitedHistory { get; }

    /// <summary>Gets the current structured validation issues.</summary>
    public IReadOnlyList<WorkflowValidationIssue> ValidationIssues { get; }
}

/// <summary>Represents one workflow mutation and any terminal outcome.</summary>
public sealed record WorkflowTransition<TResult>
{
    /// <summary>Initializes a transition result.</summary>
    public WorkflowTransition(
        WorkflowTransitionKind kind,
        WorkflowSnapshot snapshot,
        WorkflowOutcome<TResult>? outcome = null)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        Kind = kind;
        Snapshot = snapshot;
        Outcome = outcome;
    }

    /// <summary>Gets the transition kind.</summary>
    public WorkflowTransitionKind Kind { get; }

    /// <summary>Gets the committed snapshot.</summary>
    public WorkflowSnapshot Snapshot { get; }

    /// <summary>Gets the terminal outcome, when the workflow ended.</summary>
    public WorkflowOutcome<TResult>? Outcome { get; }
}

/// <summary>Identifies the mutation requested by a custom workflow action.</summary>
public enum WorkflowActionResultKind
{
    /// <summary>Remain on the current step.</summary>
    Stay,

    /// <summary>Move to a specific step.</summary>
    GoTo,

    /// <summary>Finish the workflow.</summary>
    Finish,

    /// <summary>Cancel the workflow.</summary>
    Cancel,
}

/// <summary>Represents a declarative custom-action decision.</summary>
public readonly record struct WorkflowActionResult
{
    private WorkflowActionResult(WorkflowActionResultKind kind, StepKey? target)
    {
        Kind = kind;
        Target = target;
    }

    /// <summary>Gets the requested mutation.</summary>
    public WorkflowActionResultKind Kind { get; }

    /// <summary>Gets the target for <see cref="WorkflowActionResultKind.GoTo"/>.</summary>
    public StepKey? Target { get; }

    /// <summary>Creates a Stay decision.</summary>
    public static WorkflowActionResult Stay() => new(WorkflowActionResultKind.Stay, null);

    /// <summary>Creates a GoTo decision.</summary>
    public static WorkflowActionResult GoTo(StepKey target) =>
        new(WorkflowActionResultKind.GoTo, target);

    /// <summary>Creates a Finish decision.</summary>
    public static WorkflowActionResult Finish() => new(WorkflowActionResultKind.Finish, null);

    /// <summary>Creates a Cancel decision.</summary>
    public static WorkflowActionResult Cancel() => new(WorkflowActionResultKind.Cancel, null);
}

/// <summary>Provides stable keys for built-in workflow actions.</summary>
public static class WorkflowActionKeys
{
    /// <summary>Gets the visited-history Back action key.</summary>
    public static ActionKey Back { get; } = new("back");

    /// <summary>Gets the graph Next action key.</summary>
    public static ActionKey Next { get; } = new("next");

    /// <summary>Gets the Finish action key.</summary>
    public static ActionKey Finish { get; } = new("finish");

    /// <summary>Gets the Cancel action key.</summary>
    public static ActionKey Cancel { get; } = new("cancel");
}

/// <summary>Handles a custom action without directly mutating workflow runtime state.</summary>
public interface IWorkflowActionHandler<in TContext>
{
    /// <summary>Returns the declarative mutation requested for an action.</summary>
    ValueTask<WorkflowActionResult> HandleActionAsync(
        ActionKey action,
        TContext context,
        CancellationToken cancellationToken);
}
