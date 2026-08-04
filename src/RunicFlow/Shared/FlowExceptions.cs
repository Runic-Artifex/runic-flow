using System;
using System.Collections.Generic;

namespace RunicFlow
{
    /// <summary>Identifies the Flow feature associated with a failure.</summary>
    public enum FlowFeature
    {
        /// <summary>No more specific feature is available.</summary>
        Shared,
        /// <summary>Navigation.</summary>
        Navigation,
        /// <summary>Dialogs.</summary>
        Dialog,
        /// <summary>Operations.</summary>
        Operation,
        /// <summary>Workflows.</summary>
        Workflow,
        /// <summary>Presentation adapter interaction.</summary>
        Presentation,
    }

    /// <summary>Identifies a lifecycle stage without exposing adapter-specific state.</summary>
    public enum FlowLifecycleStage
    {
        /// <summary>No lifecycle stage applies.</summary>
        None,
        /// <summary>Typed initialization.</summary>
        Initializing,
        /// <summary>Pre-presentation activation.</summary>
        Activating,
        /// <summary>Presentation creation.</summary>
        Presenting,
        /// <summary>Post-commit activation.</summary>
        Activated,
        /// <summary>Post-commit deactivation.</summary>
        Deactivating,
        /// <summary>Presentation closure.</summary>
        Closing,
        /// <summary>Post-closure deactivation.</summary>
        Deactivated,
        /// <summary>Owned-resource disposal.</summary>
        Disposing,
    }

    /// <summary>Base class for failures carrying bounded Flow metadata.</summary>
    public abstract class FlowException : Exception
    {
        /// <summary>Initializes a Flow exception.</summary>
        protected FlowException(
            string message,
            FlowFeature feature,
            string? logicalKey = null,
            FlowSessionId? sessionId = null,
            FlowLifecycleStage stage = FlowLifecycleStage.None,
            Exception? innerException = null)
            : base(message, innerException)
        {
            Feature = feature;
            LogicalKey = logicalKey;
            SessionId = sessionId;
            Stage = stage;
        }

        /// <summary>Gets the feature that failed.</summary>
        public FlowFeature Feature { get; }

        /// <summary>Gets the bounded logical key, when one applies.</summary>
        public string? LogicalKey { get; }

        /// <summary>Gets the content session, when one had been created.</summary>
        public FlowSessionId? SessionId { get; }

        /// <summary>Gets the lifecycle stage, or <see cref="FlowLifecycleStage.None"/>.</summary>
        public FlowLifecycleStage Stage { get; }
    }

    /// <summary>Represents an invalid or duplicate Flow registration.</summary>
    public sealed class FlowRegistrationException : FlowException
    {
        /// <summary>Initializes a registration exception.</summary>
        public FlowRegistrationException(string message, string? logicalKey = null, Exception? innerException = null)
            : base(message, FlowFeature.Shared, logicalKey, innerException: innerException)
        {
        }
    }

    /// <summary>Represents a failure discovered while freezing and validating Flow registrations.</summary>
    public sealed class FlowValidationException : FlowException
    {
        /// <summary>Initializes a validation exception.</summary>
        public FlowValidationException(string message, string? logicalKey = null, Exception? innerException = null)
            : base(message, FlowFeature.Shared, logicalKey, innerException: innerException)
        {
        }
    }

    /// <summary>Represents one or more ordered lifecycle callback failures.</summary>
    public sealed class FlowLifecycleException : FlowException
    {
        /// <summary>Initializes a lifecycle exception.</summary>
        public FlowLifecycleException(
            string message,
            FlowFeature feature,
            FlowLifecycleStage stage,
            IReadOnlyList<Exception> failures,
            string? logicalKey = null,
            FlowSessionId? sessionId = null)
            : base(message, feature, logicalKey, sessionId, stage, CreateAggregate(failures))
        {
            Failures = CopyFailures(failures);
        }

        /// <summary>Gets lifecycle failures in their observable callback order.</summary>
        public IReadOnlyList<Exception> Failures { get; }

        private static AggregateException CreateAggregate(IReadOnlyList<Exception> failures) =>
            new(CopyFailures(failures));

        private static Exception[] CopyFailures(IReadOnlyList<Exception> failures)
        {
            ArgumentNullException.ThrowIfNull(failures);
            if (failures.Count == 0)
            {
                throw new ArgumentException("At least one lifecycle failure is required.", nameof(failures));
            }

            Exception[] copy = new Exception[failures.Count];
            for (int index = 0; index < failures.Count; index++)
            {
                copy[index] = failures[index] ??
                    throw new ArgumentException("Lifecycle failures cannot contain null.", nameof(failures));
            }

            return copy;
        }
    }

    /// <summary>Represents failures encountered while releasing owned resources.</summary>
    public sealed class FlowCleanupException : FlowException
    {
        /// <summary>Initializes a cleanup exception for a content session.</summary>
        public FlowCleanupException(FlowSessionId sessionId, IReadOnlyList<Exception> cleanupFailures)
            : this(
                $"Flow session '{sessionId}' encountered {GetCount(cleanupFailures)} failure(s) during cleanup.",
                FlowFeature.Shared,
                cleanupFailures,
                sessionId: sessionId)
        {
        }

        /// <summary>Initializes a cleanup exception with feature metadata and optional primary-failure precedence.</summary>
        public FlowCleanupException(
            string message,
            FlowFeature feature,
            IReadOnlyList<Exception> cleanupFailures,
            string? logicalKey = null,
            FlowSessionId? sessionId = null,
            Exception? primaryException = null)
            : base(
                message,
                feature,
                logicalKey,
                sessionId,
                FlowLifecycleStage.Disposing,
                primaryException ?? CreateAggregate(cleanupFailures))
        {
            CleanupFailures = CopyFailures(cleanupFailures);
            PrimaryException = primaryException;
        }

        /// <summary>Gets cleanup failures in attempted teardown order.</summary>
        public IReadOnlyList<Exception> CleanupFailures { get; }

        /// <summary>Gets cleanup failures in attempted teardown order.</summary>
        public IReadOnlyList<Exception> Failures => CleanupFailures;

        /// <summary>Gets the work or lifecycle exception that takes precedence over cleanup, when present.</summary>
        public Exception? PrimaryException { get; }

        private static int GetCount(IReadOnlyList<Exception> failures)
        {
            ArgumentNullException.ThrowIfNull(failures);
            return failures.Count;
        }

        private static AggregateException CreateAggregate(IReadOnlyList<Exception> failures) =>
            new(CopyFailures(failures));

        private static Exception[] CopyFailures(IReadOnlyList<Exception> failures)
        {
            ArgumentNullException.ThrowIfNull(failures);
            if (failures.Count == 0)
            {
                throw new ArgumentException("At least one cleanup failure is required.", nameof(failures));
            }

            Exception[] copy = new Exception[failures.Count];
            for (int index = 0; index < failures.Count; index++)
            {
                copy[index] = failures[index] ??
                    throw new ArgumentException("Cleanup failures cannot contain null.", nameof(failures));
            }

            return copy;
        }
    }

    /// <summary>Represents an attempt to mutate a serialized Flow state machine re-entrantly.</summary>
    public sealed class FlowReentrancyException : FlowException
    {
        /// <summary>Initializes a re-entrancy exception.</summary>
        public FlowReentrancyException(
            string message,
            FlowFeature feature,
            string? logicalKey = null,
            FlowSessionId? sessionId = null,
            Exception? innerException = null)
            : base(message, feature, logicalKey, sessionId, innerException: innerException)
        {
        }
    }

    /// <summary>Represents a failure at the logical presentation boundary.</summary>
    public sealed class FlowPresenterException : FlowException
    {
        /// <summary>Initializes a presenter exception.</summary>
        public FlowPresenterException(
            string message,
            FlowFeature feature,
            FlowLifecycleStage stage,
            string? logicalKey = null,
            FlowSessionId? sessionId = null,
            Exception? innerException = null)
            : base(message, feature, logicalKey, sessionId, stage, innerException)
        {
        }
    }
}

namespace RunicFlow.Operations
{
    using RunicFlow;

    /// <summary>Represents rejection by an operation slot's busy policy.</summary>
    public sealed class OperationBusyException : FlowException
    {
        /// <summary>Initializes an operation-busy exception.</summary>
        public OperationBusyException(
            string message,
            OperationKey operation,
            string? slot = null,
            Exception? innerException = null)
            : base(message, FlowFeature.Operation, operation.Value, innerException: innerException)
        {
            Operation = operation;
            Slot = slot;
        }

        /// <summary>Gets the operation whose slot was busy.</summary>
        public OperationKey Operation { get; }

        /// <summary>Gets the bounded consumer-defined slot name, when present.</summary>
        public string? Slot { get; }
    }
}

namespace RunicFlow.Workflows
{
    using RunicFlow;

    /// <summary>Represents an invalid workflow graph or a runtime redirect loop.</summary>
    public sealed class WorkflowGraphException : FlowException
    {
        /// <summary>Initializes a workflow graph exception.</summary>
        public WorkflowGraphException(
            string message,
            WorkflowKey workflow,
            StepKey? step = null,
            FlowSessionId? sessionId = null,
            Exception? innerException = null)
            : base(message, FlowFeature.Workflow, workflow.Value, sessionId, innerException: innerException)
        {
            Workflow = workflow;
            Step = step;
        }

        /// <summary>Gets the workflow whose graph failed.</summary>
        public WorkflowKey Workflow { get; }

        /// <summary>Gets the implicated workflow step, when available.</summary>
        public StepKey? Step { get; }
    }
}
