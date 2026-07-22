using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using WebUIToolkit.MVVM.Flow;

namespace WebUIToolkit.MVVM.Workflows;

/// <summary>Describes why a workflow step is being presented.</summary>
public enum WorkflowPresentationReason
{
    /// <summary>The workflow is starting.</summary>
    Start,

    /// <summary>The workflow is moving forward.</summary>
    Forward,

    /// <summary>The workflow is moving through visited history.</summary>
    Back,

    /// <summary>A custom action selected a target step.</summary>
    Redirect,
}

/// <summary>Provides logical workflow state to a presenter.</summary>
public sealed record WorkflowPresentationContext
{
    /// <summary>Initializes a presentation context.</summary>
    public WorkflowPresentationContext(
        WorkflowKey workflow,
        StepKey step,
        WorkflowPresentationReason reason,
        IReadOnlyList<StepKey> visitedHistory)
    {
        ArgumentNullException.ThrowIfNull(visitedHistory);
        Workflow = workflow;
        Step = step;
        Reason = reason;
        VisitedHistory = new ReadOnlyCollection<StepKey>([.. visitedHistory]);
    }

    /// <summary>Gets the workflow key.</summary>
    public WorkflowKey Workflow { get; }

    /// <summary>Gets the step being presented.</summary>
    public StepKey Step { get; }

    /// <summary>Gets the transition reason.</summary>
    public WorkflowPresentationReason Reason { get; }

    /// <summary>Gets the actual visited history including the target.</summary>
    public IReadOnlyList<StepKey> VisitedHistory { get; }
}

/// <summary>Presents workflow step content and returns its owned lease.</summary>
public interface IWorkflowPresenter
{
    /// <summary>Presents one activated workflow step.</summary>
    ValueTask<IFlowPresentationLease> PresentAsync(
        FlowContentDescriptor content,
        WorkflowPresentationContext context,
        CancellationToken cancellationToken);
}
