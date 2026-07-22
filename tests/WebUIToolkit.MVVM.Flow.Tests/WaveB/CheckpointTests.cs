using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using WebUIToolkit.MVVM.Flow;
using WebUIToolkit.MVVM.Workflows;

namespace WebUIToolkit.MVVM.Flow.Tests.WaveB;

internal static class CheckpointTests
{
    public static ValueTask EnvelopeDefensivelyCopiesInputs()
    {
        StepKey originalStep = new("start");
        StepKey[] visited = [originalStep];
        byte[] payload = [1, 2, 3];
        var envelope = new WorkflowCheckpointEnvelope(
            new WorkflowKey("setup"),
            1,
            originalStep,
            visited,
            payload);

        visited[0] = new StepKey("changed");
        payload[0] = 9;

        TestAssert.Equal(originalStep, envelope.VisitedSteps[0]);
        TestAssert.Equal((byte)1, envelope.Payload.Span[0]);
        TestAssert.Equal(WorkflowCheckpointLimits.CurrentFormatVersion, envelope.FormatVersion);
        return ValueTask.CompletedTask;
    }

    public static ValueTask EnvelopeRejectsInvalidBounds()
    {
        TestAssert.True(Throws<ArgumentOutOfRangeException>(() =>
            _ = new WorkflowCheckpointEnvelope(
                0,
                new WorkflowKey("setup"),
                1,
                new StepKey("start"),
                [],
                ReadOnlyMemory<byte>.Empty)));
        TestAssert.True(Throws<ArgumentOutOfRangeException>(() =>
            _ = new WorkflowCheckpointEnvelope(
                new WorkflowKey("setup"),
                0,
                new StepKey("start"),
                [],
                ReadOnlyMemory<byte>.Empty)));
        TestAssert.True(Throws<ArgumentOutOfRangeException>(() =>
            _ = new WorkflowCheckpointEnvelope(
                new WorkflowKey("setup"),
                1,
                new StepKey("start"),
                [],
                new byte[WorkflowCheckpointLimits.MaximumPayloadLength + 1])));
        return ValueTask.CompletedTask;
    }

    public static async ValueTask ValidEnvelopeRoundTripsThroughValidation()
    {
        WorkflowKey workflow = new("setup");
        StepKey start = new("start");
        StepKey review = new("review");
        var envelope = new WorkflowCheckpointEnvelope(
            workflow,
            3,
            review,
            [start, review, start, review],
            new byte[] { 4, 5, 6 });

        WorkflowCheckpointEnvelope validated = await WorkflowCheckpointRestoreValidator.ValidateAsync(
            envelope,
            workflow,
            3,
            new HashSet<StepKey> { start, review });

        TestAssert.True(ReferenceEquals(envelope, validated));
        TestAssert.SequenceEqual(
            new StepKey[] { start, review, start, review },
            validated.VisitedSteps);
    }

    public static async ValueTask RestoreRejectionsAreDeterministic()
    {
        WorkflowKey expected = new("setup");
        StepKey start = new("start");
        var defined = new HashSet<StepKey> { start };

        await AssertRejectionAsync(
            new WorkflowCheckpointEnvelope(new WorkflowKey("other"), 1, start, [], ReadOnlyMemory<byte>.Empty),
            expected,
            1,
            defined,
            WorkflowCheckpointRejection.WorkflowMismatch);
        await AssertRejectionAsync(
            new WorkflowCheckpointEnvelope(expected, 2, start, [], ReadOnlyMemory<byte>.Empty),
            expected,
            1,
            defined,
            WorkflowCheckpointRejection.SchemaMismatch);
        await AssertRejectionAsync(
            new WorkflowCheckpointEnvelope(expected, 1, new StepKey("missing"), [], ReadOnlyMemory<byte>.Empty),
            expected,
            1,
            defined,
            WorkflowCheckpointRejection.UnknownCurrentStep);
        await AssertRejectionAsync(
            new WorkflowCheckpointEnvelope(expected, 1, start, [new StepKey("missing")], ReadOnlyMemory<byte>.Empty),
            expected,
            1,
            defined,
            WorkflowCheckpointRejection.UnknownVisitedStep);
    }

    public static async ValueTask MigrationRunsOnceAndIsRevalidated()
    {
        WorkflowKey workflow = new("setup");
        StepKey start = new("start");
        var checkpoint = new WorkflowCheckpointEnvelope(
            workflow,
            1,
            start,
            [start],
            new byte[] { 1 });
        var migration = new RecordingMigration(static (source, target) =>
            new WorkflowCheckpointEnvelope(
                source.Workflow,
                target,
                source.CurrentStep,
                source.VisitedSteps,
                new byte[] { 2 }));

        WorkflowCheckpointEnvelope migrated = await WorkflowCheckpointRestoreValidator.ValidateAsync(
            checkpoint,
            workflow,
            2,
            new HashSet<StepKey> { start },
            migration);

        TestAssert.Equal(1, migration.CallCount);
        TestAssert.Equal(2, migrated.SchemaVersion);
        TestAssert.Equal((byte)2, migrated.Payload.Span[0]);

        var invalidMigration = new RecordingMigration(static (source, target) =>
            new WorkflowCheckpointEnvelope(
                new WorkflowKey("other"),
                target,
                source.CurrentStep,
                source.VisitedSteps,
                source.Payload));
        WorkflowCheckpointException exception = await TestAssert.ThrowsAsync<WorkflowCheckpointException>(async () =>
        {
            _ = await WorkflowCheckpointRestoreValidator.ValidateAsync(
                checkpoint,
                workflow,
                2,
                new HashSet<StepKey> { start },
                invalidMigration);
        });
        TestAssert.Equal(WorkflowCheckpointRejection.InvalidMigrationResult, exception.Rejection);
        TestAssert.Equal(1, invalidMigration.CallCount);
    }

    public static async ValueTask MigrationObservesCallerCancellation()
    {
        WorkflowKey workflow = new("setup");
        StepKey start = new("start");
        var checkpoint = new WorkflowCheckpointEnvelope(
            workflow,
            1,
            start,
            [],
            ReadOnlyMemory<byte>.Empty);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var migration = new RecordingMigration((source, target) =>
        {
            cancellation.Token.ThrowIfCancellationRequested();
            return source;
        });

        _ = await TestAssert.ThrowsAsync<OperationCanceledException>(async () =>
        {
            _ = await WorkflowCheckpointRestoreValidator.ValidateAsync(
                checkpoint,
                workflow,
                2,
                new HashSet<StepKey> { start },
                migration,
                cancellation.Token);
        });
        TestAssert.Equal(1, migration.CallCount);
    }

    public static async ValueTask GeneratedValidEnvelopesPreserveHistory()
    {
        WorkflowKey workflow = new("property-workflow");
        StepKey[] steps = [new("a"), new("b"), new("c"), new("d")];
        var defined = new HashSet<StepKey>(steps);

        for (int length = 1; length <= 128; length++)
        {
            var history = new StepKey[length];
            for (int index = 0; index < history.Length; index++)
            {
                history[index] = steps[(index * 17 + length) % steps.Length];
            }

            StepKey current = history[^1];
            var envelope = new WorkflowCheckpointEnvelope(
                workflow,
                1,
                current,
                history,
                new byte[length]);
            WorkflowCheckpointEnvelope validated = await WorkflowCheckpointRestoreValidator.ValidateAsync(
                envelope,
                workflow,
                1,
                defined);

            TestAssert.Equal(current, validated.CurrentStep);
            TestAssert.SequenceEqual(history, validated.VisitedSteps);
            TestAssert.Equal(length, validated.Payload.Length);
        }
    }

    public static async ValueTask RestoreRejectsEmptyOrInconsistentHistory()
    {
        WorkflowKey workflow = new("history");
        StepKey start = new("start");
        StepKey review = new("review");
        var defined = new HashSet<StepKey> { start, review };

        await AssertRejectionAsync(
            new WorkflowCheckpointEnvelope(workflow, 1, start, [], ReadOnlyMemory<byte>.Empty),
            workflow,
            1,
            defined,
            WorkflowCheckpointRejection.InconsistentHistory);
        await AssertRejectionAsync(
            new WorkflowCheckpointEnvelope(workflow, 1, review, [start], ReadOnlyMemory<byte>.Empty),
            workflow,
            1,
            defined,
            WorkflowCheckpointRejection.InconsistentHistory);
    }

    private static async ValueTask AssertRejectionAsync(
        WorkflowCheckpointEnvelope checkpoint,
        WorkflowKey workflow,
        int schemaVersion,
        IReadOnlySet<StepKey> definedSteps,
        WorkflowCheckpointRejection expected)
    {
        WorkflowCheckpointException exception = await TestAssert.ThrowsAsync<WorkflowCheckpointException>(async () =>
        {
            _ = await WorkflowCheckpointRestoreValidator.ValidateAsync(
                checkpoint,
                workflow,
                schemaVersion,
                definedSteps);
        });
        TestAssert.Equal(expected, exception.Rejection);
        TestAssert.Equal(workflow, exception.Workflow);
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

    private sealed class RecordingMigration : IWorkflowCheckpointMigration
    {
        private readonly Func<WorkflowCheckpointEnvelope, int, WorkflowCheckpointEnvelope> _migrate;

        public RecordingMigration(Func<WorkflowCheckpointEnvelope, int, WorkflowCheckpointEnvelope> migrate)
        {
            _migrate = migrate;
        }

        public int CallCount { get; private set; }

        public ValueTask<WorkflowCheckpointEnvelope> MigrateAsync(
            WorkflowCheckpointEnvelope checkpoint,
            int targetSchemaVersion,
            CancellationToken cancellationToken)
        {
            CallCount++;
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(_migrate(checkpoint, targetSchemaVersion));
        }
    }
}
