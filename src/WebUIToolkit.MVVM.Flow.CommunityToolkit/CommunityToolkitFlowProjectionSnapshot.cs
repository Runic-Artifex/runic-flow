using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using WebUIToolkit.MVVM.Flow;

namespace WebUIToolkit.MVVM.Flow.CommunityToolkit;

/// <summary>Describes the generated <c>SubmitCommand</c> state.</summary>
public readonly record struct CommunityToolkitFlowCommandState(bool CanExecute, bool IsRunning);

/// <summary>An immutable snapshot of the schema-v1 generated-member projection.</summary>
public sealed class CommunityToolkitFlowProjectionSnapshot
{
    /// <summary>Initializes an immutable projection snapshot.</summary>
    public CommunityToolkitFlowProjectionSnapshot(
        FlowSessionId sessionId,
        long sequence,
        string? title,
        IEnumerable<string> titleErrors,
        CommunityToolkitFlowCommandState submitCommand)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(sequence);
        ArgumentNullException.ThrowIfNull(titleErrors);
        var errors = new SortedSet<string>(StringComparer.Ordinal);
        foreach (string error in titleErrors)
        {
            ArgumentNullException.ThrowIfNull(error);
            if (error.Length > 4_096)
            {
                throw new ArgumentException(
                    "A projected validation error cannot exceed 4096 characters.",
                    nameof(titleErrors));
            }

            errors.Add(error);
            if (errors.Count > 32)
            {
                throw new ArgumentException(
                    "A projected property cannot contain more than 32 validation errors.",
                    nameof(titleErrors));
            }
        }

        SessionId = sessionId;
        Sequence = sequence;
        Title = title;
        TitleErrors = new ReadOnlyCollection<string>([.. errors]);
        SubmitCommand = submitCommand;
    }

    /// <summary>Gets the authoritative Flow content session.</summary>
    public FlowSessionId SessionId { get; }

    /// <summary>Gets the monotonic owner-local observation sequence.</summary>
    public long Sequence { get; }

    /// <summary>Gets the directly projected generated <c>Title</c> value.</summary>
    public string? Title { get; }

    /// <summary>Gets the bounded, ordinally ordered validation projection for <c>Title</c>.</summary>
    public IReadOnlyList<string> TitleErrors { get; }

    /// <summary>Gets the directly projected generated <c>SubmitCommand</c> state.</summary>
    public CommunityToolkitFlowCommandState SubmitCommand { get; }
}

/// <summary>Identifies the result of a generated-member projection dispatch.</summary>
public enum CommunityToolkitFlowDispatchStatus
{
    /// <summary>The generated member committed its mutation.</summary>
    Committed = 1,

    /// <summary>The supplied Flow session no longer owns the projection.</summary>
    StaleSession = 2,

    /// <summary>The relay command rejected execution through <c>CanExecute</c>.</summary>
    CannotExecute = 3,
}

/// <summary>Returns one terminal result and its authoritative projection snapshot.</summary>
public sealed record CommunityToolkitFlowDispatchResult(
    CommunityToolkitFlowDispatchStatus Status,
    CommunityToolkitFlowProjectionSnapshot Snapshot)
{
    /// <summary>Gets whether the mutation committed.</summary>
    public bool Committed => Status == CommunityToolkitFlowDispatchStatus.Committed;
}
