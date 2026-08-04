using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using RunicFlow.Generators;

namespace RunicFlow.Tests.WaveB;

internal static class GeneratorContractTests
{
    private static readonly string[] ExpectedDiagnosticIds =
    [
        "RFLOW0001",
        "RFLOW0002",
        "RFLOW0003",
        "RFLOW0004",
        "RFLOW0005",
        "RFLOW0006",
        "RFLOW0007",
        "RFLOW0008",
        "RFLOW0009",
        "RFLOW0010",
    ];

    public static ValueTask DiagnosticCatalogIsOrderedAndStable()
    {
        IReadOnlyList<FlowGeneratorDiagnosticDescriptor> descriptors =
            FlowGeneratorDiagnosticCatalog.OrderedDescriptors;
        TestAssert.Equal(ExpectedDiagnosticIds.Length, descriptors.Count);
        for (int index = 0; index < ExpectedDiagnosticIds.Length; index++)
        {
            TestAssert.Equal(ExpectedDiagnosticIds[index], descriptors[index].Id);
            TestAssert.Equal(FlowGeneratorDiagnosticCatalog.Category, descriptors[index].Category);
            TestAssert.False(descriptors[index].IsConfigurable);
            TestAssert.True(descriptors[index].HelpLinkUri.EndsWith(
                ExpectedDiagnosticIds[index] + ".md",
                StringComparison.Ordinal));
        }

        TestAssert.Equal(
            FlowGeneratorDiagnosticSeverity.Warning,
            FlowGeneratorDiagnosticCatalog.UnprovenViewModelRegistration.DefaultSeverity);
        TestAssert.Equal(
            FlowGeneratorDiagnosticSeverity.Warning,
            FlowGeneratorDiagnosticCatalog.MissingCodec.DefaultSeverity);
        TestAssert.True(FlowGeneratorDiagnosticCatalog.IsReservedId("RFLOW0001"));
        TestAssert.True(FlowGeneratorDiagnosticCatalog.IsReservedId("RFLOW0999"));
        TestAssert.False(FlowGeneratorDiagnosticCatalog.IsReservedId("RFLOW0000"));
        TestAssert.False(FlowGeneratorDiagnosticCatalog.IsReservedId("wutflow0001"));
        TestAssert.True(FlowGeneratorDiagnosticCatalog.TryGetDescriptor(
            "RFLOW0010",
            out FlowGeneratorDiagnosticDescriptor? collision));
        TestAssert.True(ReferenceEquals(
            FlowGeneratorDiagnosticCatalog.GeneratedIdentifierCollision,
            collision));
        return ValueTask.CompletedTask;
    }

    public static ValueTask EmissionIsCultureAndLineEndingIndependent()
    {
        CultureInfo originalCulture = CultureInfo.CurrentCulture;
        CultureInfo originalUiCulture = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("tr-TR");
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("tr-TR");
            string turkishIdentifier = FlowDeterministicEmission.NormalizeIdentifier("1I-iı");
            string turkishLiteral = FlowDeterministicEmission.ToStringLiteral("a\né\t\"");

            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("ar-SA");
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("ar-SA");
            TestAssert.Equal(turkishIdentifier, FlowDeterministicEmission.NormalizeIdentifier("1I-iı"));
            TestAssert.Equal(turkishLiteral, FlowDeterministicEmission.ToStringLiteral("a\né\t\""));
            TestAssert.Equal("_0031I_002Di_0131", turkishIdentifier);
            TestAssert.Equal("\"a\\n\\u00E9\\t\\\"\"", turkishLiteral);
            TestAssert.Equal("one\ntwo\nthree\n", FlowDeterministicEmission.NormalizeSourceText("one\r\ntwo\rthree"));
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
            CultureInfo.CurrentUICulture = originalUiCulture;
        }

        return ValueTask.CompletedTask;
    }

    public static ValueTask IdentifierCollisionsAndHintNamesAreDeterministic()
    {
        string encoded = FlowDeterministicEmission.NormalizeIdentifier("a-b");
        string alreadyEncoded = FlowDeterministicEmission.NormalizeIdentifier("a_002Db");
        TestAssert.Equal(encoded, alreadyEncoded);
        TestAssert.Equal("a_002Db", encoded);
        TestAssert.Equal(
            "RunicFlow_002EModule_002DOne.g.cs",
            FlowDeterministicEmission.CreateHintName("RunicFlow.Module-One"));
        TestAssert.Equal("_class", FlowDeterministicEmission.NormalizeIdentifier("class"));
        TestAssert.Equal("_", FlowDeterministicEmission.NormalizeIdentifier(string.Empty));
        return ValueTask.CompletedTask;
    }
}
