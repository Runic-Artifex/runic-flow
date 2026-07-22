using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using WebUIToolkit.MVVM.Flow;

namespace WebUIToolkit.MVVM.Workflows;

/// <summary>Identifies a deterministic checkpoint restore rejection.</summary>
public enum WorkflowCheckpointRejection
{
    /// <summary>The stored workflow key differs from the requested workflow.</summary>
    WorkflowMismatch,

    /// <summary>The consumer schema is unsupported and no migration was supplied.</summary>
    SchemaMismatch,

    /// <summary>The current step does not exist in the workflow definition.</summary>
    UnknownCurrentStep,

    /// <summary>A visited step does not exist in the workflow definition.</summary>
    UnknownVisitedStep,

    /// <summary>A migration returned an invalid or inconsistent envelope.</summary>
    InvalidMigrationResult,

    /// <summary>The visited history is empty or does not end at the current step.</summary>
    InconsistentHistory,
}

/// <summary>Represents an explicit workflow checkpoint restore rejection.</summary>
public sealed class WorkflowCheckpointException : FlowException
{
    /// <summary>Initializes a workflow checkpoint exception.</summary>
    /// <param name="message">A bounded, deterministic description.</param>
    /// <param name="rejection">The rejection category.</param>
    /// <param name="workflow">The workflow being restored.</param>
    /// <param name="step">The implicated step, when one applies.</param>
    /// <param name="innerException">The migration failure, when one applies.</param>
    public WorkflowCheckpointException(
        string message,
        WorkflowCheckpointRejection rejection,
        WorkflowKey workflow,
        StepKey? step = null,
        Exception? innerException = null)
        : base(message, FlowFeature.Workflow, workflow.Value, innerException: innerException)
    {
        Rejection = rejection;
        Workflow = workflow;
        Step = step;
    }

    /// <summary>Gets the stable rejection category.</summary>
    public WorkflowCheckpointRejection Rejection { get; }

    /// <summary>Gets the requested workflow key.</summary>
    public WorkflowKey Workflow { get; }

    /// <summary>Gets the implicated step, when one applies.</summary>
    public StepKey? Step { get; }
}

/// <summary>Validates checkpoint identity, schema, and graph membership before workflow restoration.</summary>
public static class WorkflowCheckpointRestoreValidator
{
    /// <summary>
    /// Validates a checkpoint against a typed immutable workflow definition and optionally migrates it.
    /// </summary>
    /// <typeparam name="TContext">The workflow context type.</typeparam>
    /// <typeparam name="TResult">The workflow result type.</typeparam>
    /// <param name="checkpoint">The untrusted checkpoint envelope.</param>
    /// <param name="definition">The registered workflow definition being restored.</param>
    /// <param name="migration">An optional consumer migration for a schema mismatch.</param>
    /// <param name="cancellationToken">Cancels migration.</param>
    /// <returns>The original or migrated envelope, fully validated for restore.</returns>
    public static ValueTask<WorkflowCheckpointEnvelope> ValidateAsync<TContext, TResult>(
        WorkflowCheckpointEnvelope checkpoint,
        WorkflowDefinition<TContext, TResult> definition,
        IWorkflowCheckpointMigration? migration = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(definition);
        HashSet<StepKey> definedSteps = new(definition.Steps.Keys);
        return ValidateAsync(
            checkpoint,
            definition.Key,
            definition.SchemaVersion,
            definedSteps,
            migration,
            cancellationToken);
    }

    /// <summary>
    /// Validates a checkpoint for restore and optionally applies one consumer migration.
    /// </summary>
    /// <param name="checkpoint">The untrusted checkpoint envelope.</param>
    /// <param name="expectedWorkflow">The workflow definition being restored.</param>
    /// <param name="expectedSchemaVersion">The positive current consumer schema version.</param>
    /// <param name="definedSteps">The immutable definition's complete step-key set.</param>
    /// <param name="migration">An optional consumer migration for a schema mismatch.</param>
    /// <param name="cancellationToken">Cancels migration.</param>
    /// <returns>The original or migrated envelope, fully validated for restore.</returns>
    /// <exception cref="WorkflowCheckpointException">The checkpoint cannot be restored safely.</exception>
    public static async ValueTask<WorkflowCheckpointEnvelope> ValidateAsync(
        WorkflowCheckpointEnvelope checkpoint,
        WorkflowKey expectedWorkflow,
        int expectedSchemaVersion,
        IReadOnlySet<StepKey> definedSteps,
        IWorkflowCheckpointMigration? migration = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        WorkflowCheckpointValidation.ValidateWorkflowKey(expectedWorkflow, nameof(expectedWorkflow));
        WorkflowCheckpointValidation.ValidateSchemaVersion(expectedSchemaVersion, nameof(expectedSchemaVersion));
        ArgumentNullException.ThrowIfNull(definedSteps);

        ValidateWorkflow(checkpoint, expectedWorkflow);

        WorkflowCheckpointEnvelope candidate = checkpoint;
        if (candidate.SchemaVersion != expectedSchemaVersion)
        {
            // Consumer migrations are forward-only. A newer stored schema may contain
            // semantics this runtime cannot understand and must never be downgraded.
            if (candidate.SchemaVersion > expectedSchemaVersion || migration is null)
            {
                throw new WorkflowCheckpointException(
                    $"Workflow checkpoint schema {candidate.SchemaVersion} cannot restore schema {expectedSchemaVersion}.",
                    WorkflowCheckpointRejection.SchemaMismatch,
                    expectedWorkflow);
            }

            try
            {
                candidate = await migration
                    .MigrateAsync(candidate, expectedSchemaVersion, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                throw new WorkflowCheckpointException(
                    "Workflow checkpoint migration failed.",
                    WorkflowCheckpointRejection.InvalidMigrationResult,
                    expectedWorkflow,
                    innerException: exception);
            }

            if (candidate is null ||
                candidate.FormatVersion != WorkflowCheckpointLimits.CurrentFormatVersion ||
                candidate.Workflow != expectedWorkflow ||
                candidate.SchemaVersion != expectedSchemaVersion)
            {
                throw new WorkflowCheckpointException(
                    "Workflow checkpoint migration returned an inconsistent envelope.",
                    WorkflowCheckpointRejection.InvalidMigrationResult,
                    expectedWorkflow);
            }
        }

        ValidateGraph(candidate, expectedWorkflow, definedSteps);
        return candidate;
    }

    private static void ValidateWorkflow(
        WorkflowCheckpointEnvelope checkpoint,
        WorkflowKey expectedWorkflow)
    {
        if (checkpoint.Workflow != expectedWorkflow)
        {
            throw new WorkflowCheckpointException(
                $"Workflow checkpoint '{checkpoint.Workflow}' cannot restore workflow '{expectedWorkflow}'.",
                WorkflowCheckpointRejection.WorkflowMismatch,
                expectedWorkflow);
        }
    }

    private static void ValidateGraph(
        WorkflowCheckpointEnvelope checkpoint,
        WorkflowKey expectedWorkflow,
        IReadOnlySet<StepKey> definedSteps)
    {
        if (!definedSteps.Contains(checkpoint.CurrentStep))
        {
            throw new WorkflowCheckpointException(
                $"Workflow checkpoint current step '{checkpoint.CurrentStep}' is not defined.",
                WorkflowCheckpointRejection.UnknownCurrentStep,
                expectedWorkflow,
                checkpoint.CurrentStep);
        }

        for (int index = 0; index < checkpoint.VisitedSteps.Count; index++)
        {
            StepKey visited = checkpoint.VisitedSteps[index];
            if (!definedSteps.Contains(visited))
            {
                throw new WorkflowCheckpointException(
                    $"Workflow checkpoint visited step '{visited}' at index {index} is not defined.",
                    WorkflowCheckpointRejection.UnknownVisitedStep,
                    expectedWorkflow,
                    visited);
            }
        }

        if (checkpoint.VisitedSteps.Count == 0)
        {
            throw new WorkflowCheckpointException(
                "Workflow checkpoint visited history must contain the current step.",
                WorkflowCheckpointRejection.InconsistentHistory,
                expectedWorkflow,
                checkpoint.CurrentStep);
        }

        StepKey lastVisited = checkpoint.VisitedSteps[^1];
        if (lastVisited != checkpoint.CurrentStep)
        {
            throw new WorkflowCheckpointException(
                "Workflow checkpoint visited history must end at the current step.",
                WorkflowCheckpointRejection.InconsistentHistory,
                expectedWorkflow,
                checkpoint.CurrentStep);
        }
    }
}
