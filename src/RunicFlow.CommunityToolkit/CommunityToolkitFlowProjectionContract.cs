using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace RunicFlow.CommunityToolkit;

/// <summary>Identifies a closed member in the CommunityToolkit Flow projection schema.</summary>
public enum CommunityToolkitFlowProjectionMemberKind
{
    /// <summary>The generated <c>Title</c> property.</summary>
    Property = 1,

    /// <summary>The generated <c>SubmitCommand</c> relay command.</summary>
    Command = 2,
}

/// <summary>Describes one immutable generated-member projection.</summary>
public sealed record CommunityToolkitFlowProjectionMember
{
    /// <summary>Initializes one generated-member projection descriptor.</summary>
    public CommunityToolkitFlowProjectionMember(
        string producerFixtureId,
        string projectionFixtureId,
        int memberId,
        string generatedMemberName,
        CommunityToolkitFlowProjectionMemberKind kind,
        bool includesValidation)
    {
        ArgumentException.ThrowIfNullOrEmpty(producerFixtureId);
        ArgumentException.ThrowIfNullOrEmpty(projectionFixtureId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(memberId);
        ArgumentException.ThrowIfNullOrEmpty(generatedMemberName);
        if (kind is < CommunityToolkitFlowProjectionMemberKind.Property or
            > CommunityToolkitFlowProjectionMemberKind.Command)
        {
            throw new ArgumentOutOfRangeException(nameof(kind));
        }

        ProducerFixtureId = producerFixtureId;
        ProjectionFixtureId = projectionFixtureId;
        MemberId = memberId;
        GeneratedMemberName = generatedMemberName;
        Kind = kind;
        IncludesValidation = includesValidation;
    }

    /// <summary>Gets the accepted generated-member proof fixture identity.</summary>
    public string ProducerFixtureId { get; }

    /// <summary>Gets the Flow projection fixture identity.</summary>
    public string ProjectionFixtureId { get; }

    /// <summary>Gets the stable neutral projection member identifier.</summary>
    public int MemberId { get; }

    /// <summary>Gets the exact generated public member name.</summary>
    public string GeneratedMemberName { get; }

    /// <summary>Gets the projected member kind.</summary>
    public CommunityToolkitFlowProjectionMemberKind Kind { get; }

    /// <summary>Gets whether validation state accompanies the principal member.</summary>
    public bool IncludesValidation { get; }
}

/// <summary>
/// Defines the exact schema-v1 projection handoff consumed by the runtime adapter.
/// </summary>
public static class CommunityToolkitFlowProjectionContract
{
    /// <summary>The only supported projection schema version.</summary>
    public const int SchemaVersion = 1;

    /// <summary>The stable adapter identity for schema version 1.</summary>
    public const string AdapterIdentity = "runic.flow.communitytoolkit/1";

    /// <summary>The exact supported CommunityToolkit package version.</summary>
    public const string CommunityToolkitPackageVersion = "8.4.2";

    /// <summary>The stable member identifier for <c>Title</c>.</summary>
    public const int TitleMemberId = 101;

    /// <summary>The stable member identifier for <c>SubmitCommand</c>.</summary>
    public const int SubmitCommandMemberId = 102;

    private static readonly ReadOnlyCollection<CommunityToolkitFlowProjectionMember> MembersValue =
        Array.AsReadOnly(new[]
        {
            new CommunityToolkitFlowProjectionMember(
                "communitytoolkit.generated-member.title.v1",
                "flow.projection.communitytoolkit.title.v1",
                TitleMemberId,
                "Title",
                CommunityToolkitFlowProjectionMemberKind.Property,
                includesValidation: true),
            new CommunityToolkitFlowProjectionMember(
                "communitytoolkit.generated-member.submit-command.v1",
                "flow.projection.communitytoolkit.submit-command.v1",
                SubmitCommandMemberId,
                "SubmitCommand",
                CommunityToolkitFlowProjectionMemberKind.Command,
                includesValidation: false),
        });

    /// <summary>Gets the two members in stable member-identifier order.</summary>
    public static IReadOnlyList<CommunityToolkitFlowProjectionMember> Members => MembersValue;
}
