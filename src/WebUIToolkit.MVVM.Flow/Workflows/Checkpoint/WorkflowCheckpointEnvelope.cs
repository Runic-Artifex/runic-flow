using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using WebUIToolkit.MVVM.Flow;

namespace WebUIToolkit.MVVM.Workflows;

/// <summary>Contains stable bounds for workflow checkpoint envelopes.</summary>
public static class WorkflowCheckpointLimits
{
    /// <summary>Gets the envelope format emitted by this version of the library.</summary>
    public const int CurrentFormatVersion = 1;

    /// <summary>Gets the oldest envelope format accepted by this version of the library.</summary>
    public const int OldestSupportedFormatVersion = 1;

    /// <summary>Gets the maximum number of visited step keys retained in one envelope.</summary>
    public const int MaximumVisitedStepCount = 4096;

    /// <summary>Gets the maximum opaque consumer payload size, in bytes.</summary>
    public const int MaximumPayloadLength = 1024 * 1024;
}

/// <summary>
/// Represents an immutable, serialization-neutral workflow checkpoint.
/// </summary>
/// <remarks>
/// The payload is consumer-owned opaque data. It must not contain ViewModels, services,
/// commands, delegates, or presenter state. Construction defensively copies both visited
/// steps and payload bytes.
/// </remarks>
public sealed class WorkflowCheckpointEnvelope
{
    private readonly ReadOnlyCollection<StepKey> _visitedSteps;
    private readonly byte[] _payload;

    /// <summary>Initializes a checkpoint using the current library envelope format.</summary>
    /// <param name="workflow">The workflow definition key.</param>
    /// <param name="schemaVersion">The positive consumer workflow schema version.</param>
    /// <param name="currentStep">The step that was current when the checkpoint was captured.</param>
    /// <param name="visitedSteps">The ordered workflow history. Repeated keys are permitted.</param>
    /// <param name="payload">Opaque consumer state encoded by an AOT-safe consumer codec.</param>
    public WorkflowCheckpointEnvelope(
        WorkflowKey workflow,
        int schemaVersion,
        StepKey currentStep,
        IReadOnlyList<StepKey> visitedSteps,
        ReadOnlyMemory<byte> payload)
        : this(
            WorkflowCheckpointLimits.CurrentFormatVersion,
            workflow,
            schemaVersion,
            currentStep,
            visitedSteps,
            payload)
    {
    }

    /// <summary>Initializes a checkpoint with an explicit library envelope format.</summary>
    /// <param name="formatVersion">The library envelope format version.</param>
    /// <param name="workflow">The workflow definition key.</param>
    /// <param name="schemaVersion">The positive consumer workflow schema version.</param>
    /// <param name="currentStep">The step that was current when the checkpoint was captured.</param>
    /// <param name="visitedSteps">The ordered workflow history. Repeated keys are permitted.</param>
    /// <param name="payload">Opaque consumer state encoded by an AOT-safe consumer codec.</param>
    public WorkflowCheckpointEnvelope(
        int formatVersion,
        WorkflowKey workflow,
        int schemaVersion,
        StepKey currentStep,
        IReadOnlyList<StepKey> visitedSteps,
        ReadOnlyMemory<byte> payload)
    {
        WorkflowCheckpointValidation.ValidateFormatVersion(formatVersion, nameof(formatVersion));
        WorkflowCheckpointValidation.ValidateWorkflowKey(workflow, nameof(workflow));
        WorkflowCheckpointValidation.ValidateSchemaVersion(schemaVersion, nameof(schemaVersion));
        WorkflowCheckpointValidation.ValidateStepKey(currentStep, nameof(currentStep));
        ArgumentNullException.ThrowIfNull(visitedSteps);

        ArgumentOutOfRangeException.ThrowIfGreaterThan(
            visitedSteps.Count,
            WorkflowCheckpointLimits.MaximumVisitedStepCount,
            nameof(visitedSteps));

        StepKey[] visitedCopy = new StepKey[visitedSteps.Count];
        for (int index = 0; index < visitedSteps.Count; index++)
        {
            StepKey step = visitedSteps[index];
            WorkflowCheckpointValidation.ValidateStepKey(step, nameof(visitedSteps));
            visitedCopy[index] = step;
        }

        ArgumentOutOfRangeException.ThrowIfGreaterThan(
            payload.Length,
            WorkflowCheckpointLimits.MaximumPayloadLength,
            nameof(payload));

        FormatVersion = formatVersion;
        Workflow = workflow;
        SchemaVersion = schemaVersion;
        CurrentStep = currentStep;
        _visitedSteps = Array.AsReadOnly(visitedCopy);
        _payload = payload.ToArray();
    }

    /// <summary>Gets the library envelope format version.</summary>
    public int FormatVersion { get; }

    /// <summary>Gets the workflow definition key.</summary>
    public WorkflowKey Workflow { get; }

    /// <summary>Gets the consumer workflow schema version.</summary>
    public int SchemaVersion { get; }

    /// <summary>Gets the step that was current when the checkpoint was captured.</summary>
    public StepKey CurrentStep { get; }

    /// <summary>Gets the ordered workflow history.</summary>
    public IReadOnlyList<StepKey> VisitedSteps => _visitedSteps;

    /// <summary>Gets an immutable copy of the opaque consumer payload.</summary>
    public ReadOnlyMemory<byte> Payload => (byte[])_payload.Clone();
}

internal static class WorkflowCheckpointValidation
{
    public static void ValidateFormatVersion(int value, string parameterName)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(
            value,
            WorkflowCheckpointLimits.OldestSupportedFormatVersion,
            parameterName);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(
            value,
            WorkflowCheckpointLimits.CurrentFormatVersion,
            parameterName);
    }

    public static void ValidateSchemaVersion(int value, string parameterName)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value, parameterName);
    }

    public static void ValidateWorkflowKey(WorkflowKey value, string parameterName)
    {
        try
        {
            _ = new WorkflowKey(value.Value);
        }
        catch (ArgumentException exception)
        {
            throw new ArgumentException("A workflow checkpoint must contain a valid workflow key.", parameterName, exception);
        }
    }

    public static void ValidateStepKey(StepKey value, string parameterName)
    {
        try
        {
            _ = new StepKey(value.Value);
        }
        catch (ArgumentException exception)
        {
            throw new ArgumentException("A workflow checkpoint must contain valid step keys.", parameterName, exception);
        }
    }
}
