using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;

namespace RunicFlow.Workflows;

/// <summary>Identifies the severity of a workflow validation issue.</summary>
public enum WorkflowValidationSeverity
{
    /// <summary>The issue is informational and does not prevent a transition.</summary>
    Information,

    /// <summary>The issue is a warning and does not prevent a transition.</summary>
    Warning,

    /// <summary>The issue prevents the requested transition.</summary>
    Error,
}

/// <summary>Describes one structured workflow validation issue.</summary>
public sealed record WorkflowValidationIssue
{
    /// <summary>Initializes a validation issue.</summary>
    public WorkflowValidationIssue(
        string code,
        string message,
        WorkflowValidationSeverity severity = WorkflowValidationSeverity.Error)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        Code = code;
        Message = message;
        Severity = severity;
    }

    /// <summary>Gets the stable consumer-defined issue code.</summary>
    public string Code { get; }

    /// <summary>Gets the display-neutral issue message.</summary>
    public string Message { get; }

    /// <summary>Gets the issue severity.</summary>
    public WorkflowValidationSeverity Severity { get; }
}

/// <summary>Represents the immutable result of step validation.</summary>
public sealed record WorkflowValidationResult
{
    private WorkflowValidationResult(IReadOnlyList<WorkflowValidationIssue> issues)
    {
        Issues = issues;
        IsValid = !HasErrors(issues);
    }

    /// <summary>Gets a valid result containing no issues.</summary>
    public static WorkflowValidationResult Valid { get; } =
        new(ReadOnlyCollection<WorkflowValidationIssue>.Empty);

    /// <summary>Gets whether the transition may continue.</summary>
    public bool IsValid { get; }

    /// <summary>Gets an immutable snapshot of validation issues.</summary>
    public IReadOnlyList<WorkflowValidationIssue> Issues { get; }

    /// <summary>Creates a result from structured issues.</summary>
    public static WorkflowValidationResult FromIssues(
        IEnumerable<WorkflowValidationIssue> issues)
    {
        ArgumentNullException.ThrowIfNull(issues);
        List<WorkflowValidationIssue> copy = [];
        foreach (WorkflowValidationIssue issue in issues)
        {
            copy.Add(issue ?? throw new ArgumentException(
                "Workflow validation issues cannot contain null.", nameof(issues)));
        }

        return copy.Count == 0
            ? Valid
            : new WorkflowValidationResult(new ReadOnlyCollection<WorkflowValidationIssue>(copy));
    }

    private static bool HasErrors(IReadOnlyList<WorkflowValidationIssue> issues)
    {
        for (int index = 0; index < issues.Count; index++)
        {
            if (issues[index].Severity == WorkflowValidationSeverity.Error)
            {
                return true;
            }
        }

        return false;
    }
}

/// <summary>Validates the current step before a forward transition or finish.</summary>
public interface IWorkflowStepValidator<in TContext>
{
    /// <summary>Validates the current application-owned context.</summary>
    ValueTask<WorkflowValidationResult> ValidateAsync(
        TContext context,
        CancellationToken cancellationToken);
}

/// <summary>Commits application-owned state before leaving or finishing a step.</summary>
public interface IWorkflowStepCommit<in TContext>
{
    /// <summary>Commits the current step.</summary>
    ValueTask CommitAsync(TContext context, CancellationToken cancellationToken);
}

/// <summary>Determines whether an ordinary workflow cancellation may proceed.</summary>
public interface IWorkflowCancelGuard<in TContext>
{
    /// <summary>Returns <see langword="true"/> when cancellation may proceed.</summary>
    ValueTask<bool> CanCancelAsync(TContext context, CancellationToken cancellationToken);
}
