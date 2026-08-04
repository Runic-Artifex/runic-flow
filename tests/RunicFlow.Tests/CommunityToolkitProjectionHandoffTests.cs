using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using RunicFlow.Generators;

namespace RunicFlow.Tests;

internal static class CommunityToolkitProjectionHandoffTests
{
    public static ValueTask RecordsTheStageThreeHandoffWithoutRuntimeDependency()
    {
        TestAssert.Equal("CommunityToolkit.Mvvm", CommunityToolkitProjectionHandoff.CommunityToolkitPackageId);
        TestAssert.Equal("8.4.2", CommunityToolkitProjectionHandoff.CommunityToolkitPackageVersion);
        TestAssert.Equal(1, CommunityToolkitProjectionHandoff.ProjectionSchemaVersion);
        TestAssert.Equal(
            "runic.flow.communitytoolkit/1",
            CommunityToolkitProjectionHandoff.ProjectionAdapterIdentity);
        IReadOnlyList<FlowProjectionFixtureMapping> mappings = CommunityToolkitProjectionHandoff.FixtureMappings;
        TestAssert.Equal(2, mappings.Count);
        TestAssert.Equal("communitytoolkit.generated-member.title.v1", mappings[0].CommunityToolkitProofFixtureId);
        TestAssert.Equal("flow.projection.communitytoolkit.title.v1", mappings[0].FlowProjectionFixtureId);
        TestAssert.Equal(101, mappings[0].MemberId);
        TestAssert.Equal("Title", mappings[0].GeneratedMemberName);
        TestAssert.Equal(FlowProjectionMemberKind.Property, mappings[0].MemberKind);
        TestAssert.True(mappings[0].IncludesValidation);
        TestAssert.Equal("communitytoolkit.generated-member.submit-command.v1", mappings[1].CommunityToolkitProofFixtureId);
        TestAssert.Equal("flow.projection.communitytoolkit.submit-command.v1", mappings[1].FlowProjectionFixtureId);
        TestAssert.Equal(102, mappings[1].MemberId);
        TestAssert.Equal("SubmitCommand", mappings[1].GeneratedMemberName);
        TestAssert.Equal(FlowProjectionMemberKind.Command, mappings[1].MemberKind);
        TestAssert.False(mappings[1].IncludesValidation);

        var proofFixtureIds = new HashSet<string>(StringComparer.Ordinal);
        var projectionFixtureIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (FlowProjectionFixtureMapping mapping in mappings)
        {
            TestAssert.True(proofFixtureIds.Add(mapping.CommunityToolkitProofFixtureId));
            TestAssert.True(projectionFixtureIds.Add(mapping.FlowProjectionFixtureId));
        }

        AssemblyName[] references = typeof(CommunityToolkitProjectionHandoff).Assembly.GetReferencedAssemblies();
        foreach (AssemblyName reference in references)
        {
            TestAssert.False(
                string.Equals(reference.Name, CommunityToolkitProjectionHandoff.CommunityToolkitPackageId, StringComparison.Ordinal),
                "The metadata-only Flow handoff must not reference CommunityToolkit runtime code.");
        }

        return ValueTask.CompletedTask;
    }
}
