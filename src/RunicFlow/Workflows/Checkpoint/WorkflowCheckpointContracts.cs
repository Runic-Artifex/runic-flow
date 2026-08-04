using System;
using System.Threading;
using System.Threading.Tasks;
using RunicFlow;

namespace RunicFlow.Workflows;

/// <summary>Encodes and decodes the consumer-owned context payload of a workflow checkpoint.</summary>
/// <typeparam name="TContext">The workflow context type.</typeparam>
/// <remarks>
/// Implementations own serialization and compatibility policy and should use source-generated
/// serialization metadata when applicable. The Flow runtime performs no reflection-based serialization.
/// </remarks>
public interface IWorkflowCheckpointCodec<TContext>
{
    /// <summary>Encodes a context into bounded opaque checkpoint bytes.</summary>
    /// <param name="context">The application-owned workflow context.</param>
    /// <param name="cancellationToken">Cancels encoding.</param>
    /// <returns>The encoded payload. The envelope copies the returned bytes.</returns>
    ValueTask<ReadOnlyMemory<byte>> EncodeAsync(TContext context, CancellationToken cancellationToken);

    /// <summary>Decodes a previously encoded context payload.</summary>
    /// <param name="payload">The untrusted opaque payload.</param>
    /// <param name="cancellationToken">Cancels decoding.</param>
    /// <returns>The decoded application-owned workflow context.</returns>
    ValueTask<TContext> DecodeAsync(ReadOnlyMemory<byte> payload, CancellationToken cancellationToken);
}

/// <summary>Persists workflow checkpoint envelopes without prescribing a storage technology.</summary>
/// <remarks>Encryption, authentication, expiration, concurrency, and durability are consumer policy.</remarks>
public interface IWorkflowCheckpointStore
{
    /// <summary>Loads the checkpoint for a workflow, or returns <see langword="null"/> when none exists.</summary>
    /// <param name="workflow">The workflow definition key.</param>
    /// <param name="cancellationToken">Cancels the storage operation.</param>
    /// <returns>The stored checkpoint, or <see langword="null"/>.</returns>
    ValueTask<WorkflowCheckpointEnvelope?> LoadAsync(
        WorkflowKey workflow,
        CancellationToken cancellationToken);

    /// <summary>Saves a workflow checkpoint.</summary>
    /// <param name="checkpoint">The immutable checkpoint envelope.</param>
    /// <param name="cancellationToken">Cancels the storage operation.</param>
    ValueTask SaveAsync(
        WorkflowCheckpointEnvelope checkpoint,
        CancellationToken cancellationToken);

    /// <summary>Deletes the checkpoint for a workflow.</summary>
    /// <param name="workflow">The workflow definition key.</param>
    /// <param name="cancellationToken">Cancels the storage operation.</param>
    ValueTask DeleteAsync(
        WorkflowKey workflow,
        CancellationToken cancellationToken);
}

/// <summary>Migrates a consumer workflow schema without prescribing payload serialization.</summary>
/// <remarks>
/// An implementation must produce the requested schema version while preserving the expected
/// workflow identity. The restore validator verifies the returned envelope before any scope is created.
/// </remarks>
public interface IWorkflowCheckpointMigration
{
    /// <summary>Migrates a checkpoint to a requested consumer schema version.</summary>
    /// <param name="checkpoint">The validated source envelope.</param>
    /// <param name="targetSchemaVersion">The positive target consumer schema version.</param>
    /// <param name="cancellationToken">Cancels migration.</param>
    /// <returns>A migrated immutable envelope.</returns>
    ValueTask<WorkflowCheckpointEnvelope> MigrateAsync(
        WorkflowCheckpointEnvelope checkpoint,
        int targetSchemaVersion,
        CancellationToken cancellationToken);
}
