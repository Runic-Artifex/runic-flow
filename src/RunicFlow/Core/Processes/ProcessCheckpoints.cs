using System;
using System.Threading;
using System.Threading.Tasks;

namespace RunicFlow.Processes;

/// <summary>Defines the maximum encoded state size accepted by the process kernel.</summary>
public sealed record ProcessCheckpointLimits
{
    /// <summary>Gets the default checkpoint limits.</summary>
    public static ProcessCheckpointLimits Default { get; } = new();

    /// <summary>Gets the maximum encoded state size in bytes.</summary>
    public int MaxPayloadBytes { get; init; } = 1_048_576;
}

/// <summary>Encodes and decodes consumer-owned process state without serializer discovery.</summary>
public interface IProcessCheckpointCodec<TState>
{
    /// <summary>Encodes immutable application state.</summary>
    byte[] Encode(TState state);

    /// <summary>Decodes immutable application state.</summary>
    TState Decode(ReadOnlyMemory<byte> payload);
}

/// <summary>Contains one defensive, versioned active-process checkpoint.</summary>
public sealed class ProcessCheckpoint
{
    private readonly byte[] _payload;

    /// <summary>Initializes a checkpoint and copies its payload.</summary>
    public ProcessCheckpoint(
        ProcessId processId,
        ProcessKey process,
        int schemaVersion,
        long version,
        byte[] payload)
    {
        if (processId.Value == Guid.Empty || string.IsNullOrEmpty(process.Value))
        {
            throw new ArgumentException("Checkpoint identities must be initialized.");
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(schemaVersion);
        ArgumentOutOfRangeException.ThrowIfNegative(version);
        ArgumentNullException.ThrowIfNull(payload);
        ProcessId = processId;
        Process = process;
        SchemaVersion = schemaVersion;
        Version = version;
        _payload = [.. payload];
    }

    /// <summary>Gets the process instance.</summary>
    public ProcessId ProcessId { get; }

    /// <summary>Gets the process definition identity.</summary>
    public ProcessKey Process { get; }

    /// <summary>Gets the consumer-owned state schema version.</summary>
    public int SchemaVersion { get; }

    /// <summary>Gets the process-local version.</summary>
    public long Version { get; }

    /// <summary>Gets a defensive payload copy.</summary>
    public ReadOnlyMemory<byte> Payload => new([.. _payload]);
}

/// <summary>Persists opaque process checkpoints.</summary>
public interface IProcessCheckpointStore
{
    /// <summary>Loads a checkpoint when one exists.</summary>
    ValueTask<ProcessCheckpoint?> LoadAsync(ProcessId processId, CancellationToken cancellationToken = default);

    /// <summary>Saves the latest checkpoint.</summary>
    ValueTask SaveAsync(ProcessCheckpoint checkpoint, CancellationToken cancellationToken = default);

    /// <summary>Deletes a checkpoint after terminal completion or explicit abandonment.</summary>
    ValueTask DeleteAsync(ProcessId processId, CancellationToken cancellationToken = default);
}

/// <summary>Creates and restores active process checkpoints.</summary>
public static class ProcessCheckpointing
{
    /// <summary>Creates a bounded checkpoint from the latest active snapshot.</summary>
    public static ProcessCheckpoint CreateCheckpoint<TState, TCommand, TResult>(
        this ProcessSession<TState, TCommand, TResult> session,
        IProcessCheckpointCodec<TState> codec,
        ProcessCheckpointLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(codec);
        ProcessSnapshot<TState, TResult> snapshot = session.Snapshot;
        if (snapshot.Status != ProcessStatus.Active)
        {
            throw new InvalidOperationException("Only an active process can be checkpointed.");
        }

        byte[] payload = codec.Encode(snapshot.State) ??
            throw new InvalidOperationException("A process checkpoint codec returned no payload.");
        ValidatePayload(payload, limits);
        return new ProcessCheckpoint(
            snapshot.Id,
            snapshot.Process,
            snapshot.SchemaVersion,
            snapshot.Version,
            payload);
    }

    /// <summary>Restores one active process after validating its definition identity and payload bound.</summary>
    public static ProcessSession<TState, TCommand, TResult> Restore<TState, TCommand, TResult>(
        ProcessDefinition<TState, TCommand, TResult> definition,
        ProcessCheckpoint checkpoint,
        IProcessCheckpointCodec<TState> codec,
        ProcessCheckpointLimits? limits = null,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(checkpoint);
        ArgumentNullException.ThrowIfNull(codec);
        if (checkpoint.Process != definition.Key || checkpoint.SchemaVersion != definition.SchemaVersion)
        {
            throw new InvalidOperationException("The checkpoint is incompatible with the process definition.");
        }

        byte[] payload = checkpoint.Payload.ToArray();
        ValidatePayload(payload, limits);
        TState state = codec.Decode(payload);
        TimeProvider clock = timeProvider ?? TimeProvider.System;
        return new ProcessSession<TState, TCommand, TResult>(
            definition,
            new ProcessSnapshot<TState, TResult>(
                checkpoint.ProcessId,
                checkpoint.Process,
                checkpoint.SchemaVersion,
                checkpoint.Version,
                ProcessStatus.Active,
                state,
                default,
                clock.GetUtcNow()),
            clock);
    }

    private static void ValidatePayload(byte[] payload, ProcessCheckpointLimits? limits)
    {
        int maximum = (limits ?? ProcessCheckpointLimits.Default).MaxPayloadBytes;
        if (maximum is < 1 or > 16_777_216)
        {
            throw new ArgumentOutOfRangeException(nameof(limits), "The checkpoint payload limit is invalid.");
        }

        if (payload.Length > maximum)
        {
            throw new InvalidOperationException("The process checkpoint payload exceeds the configured limit.");
        }
    }
}
