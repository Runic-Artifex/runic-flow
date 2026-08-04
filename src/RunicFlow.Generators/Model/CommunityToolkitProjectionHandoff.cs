using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace RunicFlow.Generators;

/// <summary>Identifies the closed member kind proved by a Flow projection fixture.</summary>
public enum FlowProjectionMemberKind
{
    /// <summary>An identity-only mapping does not declare runtime member metadata.</summary>
    Unspecified = 0,

    /// <summary>The fixture projects one generated observable property.</summary>
    Property = 1,

    /// <summary>The fixture projects one generated relay command.</summary>
    Command = 2,
}

/// <summary>Immutable identity mapping reserved for the later CommunityToolkit Flow projection adapter.</summary>
public sealed class FlowProjectionFixtureMapping
{
    /// <summary>Initializes an identity-only producer-proof to Flow-projection handoff.</summary>
    /// <remarks>
    /// New schema-v1 mappings should use the full constructor. This overload preserves the
    /// original metadata-only handoff surface for consumers that record additional fixture IDs.
    /// </remarks>
    public FlowProjectionFixtureMapping(
        string communityToolkitProofFixtureId,
        string flowProjectionFixtureId)
    {
        CommunityToolkitProofFixtureId = communityToolkitProofFixtureId ??
            throw new ArgumentNullException(nameof(communityToolkitProofFixtureId));
        FlowProjectionFixtureId = flowProjectionFixtureId ??
            throw new ArgumentNullException(nameof(flowProjectionFixtureId));
        GeneratedMemberName = string.Empty;
    }

    /// <summary>Initializes one producer-proof to Flow-projection fixture handoff.</summary>
    public FlowProjectionFixtureMapping(
        string communityToolkitProofFixtureId,
        string flowProjectionFixtureId,
        int memberId,
        string generatedMemberName,
        FlowProjectionMemberKind memberKind,
        bool includesValidation)
    {
        CommunityToolkitProofFixtureId = communityToolkitProofFixtureId ?? throw new ArgumentNullException(nameof(communityToolkitProofFixtureId));
        FlowProjectionFixtureId = flowProjectionFixtureId ?? throw new ArgumentNullException(nameof(flowProjectionFixtureId));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(memberId);

        GeneratedMemberName = generatedMemberName ?? throw new ArgumentNullException(nameof(generatedMemberName));
        if (memberKind is < FlowProjectionMemberKind.Property or > FlowProjectionMemberKind.Command)
        {
            throw new ArgumentOutOfRangeException(nameof(memberKind));
        }

        MemberId = memberId;
        MemberKind = memberKind;
        IncludesValidation = includesValidation;
    }

    /// <summary>Gets the Stage 2 CommunityToolkit generated-member proof fixture identity.</summary>
    public string CommunityToolkitProofFixtureId { get; }

    /// <summary>Gets the reserved later Flow projection fixture identity.</summary>
    public string FlowProjectionFixtureId { get; }

    /// <summary>Gets the stable neutral projection member identifier.</summary>
    public int MemberId { get; }

    /// <summary>Gets the exact generated public member name proved by the producer fixture.</summary>
    public string GeneratedMemberName { get; }

    /// <summary>Gets the closed projected member kind.</summary>
    public FlowProjectionMemberKind MemberKind { get; }

    /// <summary>Gets whether the member projection includes validation state.</summary>
    public bool IncludesValidation { get; }
}

/// <summary>
/// Records the Stage 3 metadata-only handoff to the future CommunityToolkit Flow projection adapter.
/// </summary>
/// <remarks>
/// This contract deliberately contains strings rather than CommunityToolkit or compiler assembly references.
/// It reserves fixture identities only; it neither loads CommunityToolkit nor implements a projection.
/// </remarks>
public static class CommunityToolkitProjectionHandoff
{
    /// <summary>Gets the immutable Flow projection contract schema version.</summary>
    public const int ProjectionSchemaVersion = 1;

    /// <summary>Gets the stable runtime projection adapter identity.</summary>
    public const string ProjectionAdapterIdentity =
        "runic.flow.communitytoolkit/1";

    /// <summary>Gets the exact producer package identifier proved by Stage 2.</summary>
    public const string CommunityToolkitPackageId = "CommunityToolkit.Mvvm";

    /// <summary>Gets the exact producer package version proved by Stage 2.</summary>
    public const string CommunityToolkitPackageVersion = "8.4.2";

    private static readonly ReadOnlyCollection<FlowProjectionFixtureMapping> FixtureMappingsValue =
        Array.AsReadOnly(new[]
        {
            new FlowProjectionFixtureMapping(
                "communitytoolkit.generated-member.title.v1",
                "flow.projection.communitytoolkit.title.v1",
                101,
                "Title",
                FlowProjectionMemberKind.Property,
                includesValidation: true),
            new FlowProjectionFixtureMapping(
                "communitytoolkit.generated-member.submit-command.v1",
                "flow.projection.communitytoolkit.submit-command.v1",
                102,
                "SubmitCommand",
                FlowProjectionMemberKind.Command,
                includesValidation: false),
        });

    /// <summary>Gets the one-to-one Stage 2 proof to future Flow projection fixture mappings.</summary>
    public static IReadOnlyList<FlowProjectionFixtureMapping> FixtureMappings => FixtureMappingsValue;
}
