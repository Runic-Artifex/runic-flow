namespace RunicFlow.Workflows;

/// <summary>Identifies how a workflow session ended.</summary>
public enum WorkflowOutcomeKind
{
    /// <summary>The workflow finished and produced its typed result.</summary>
    Completed,

    /// <summary>The workflow was cancelled by an action, its caller, or shutdown.</summary>
    Cancelled,

    /// <summary>The workflow presentation disappeared or its parent closed without finishing.</summary>
    Abandoned,
}

/// <summary>Represents the ordinary, non-faulting outcome of a workflow.</summary>
/// <typeparam name="T">The workflow result type.</typeparam>
/// <remarks>
/// <see cref="Kind"/>, rather than a null check on <see cref="Value"/>, determines whether
/// the workflow completed. A completed workflow may legitimately carry a null result.
/// </remarks>
public readonly record struct WorkflowOutcome<T>(WorkflowOutcomeKind Kind, T? Value)
{
    /// <summary>Creates a completed outcome, including a nullable result when <typeparamref name="T"/> permits it.</summary>
    public static WorkflowOutcome<T> Completed(T value) => new(WorkflowOutcomeKind.Completed, value);

    /// <summary>Creates a cancelled outcome.</summary>
    public static WorkflowOutcome<T> Cancelled() => new(WorkflowOutcomeKind.Cancelled, default);

    /// <summary>Creates an abandoned outcome.</summary>
    public static WorkflowOutcome<T> Abandoned() => new(WorkflowOutcomeKind.Abandoned, default);
}
