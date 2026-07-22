using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace WebUIToolkit.MVVM.Flow.Generators;

/// <summary>Frozen, deterministically ordered Flow generator diagnostic descriptors.</summary>
public static class FlowGeneratorDiagnosticCatalog
{
    /// <summary>Gets the current diagnostic contract version.</summary>
    public const string ContractVersion = "1.0";

    /// <summary>Gets the stable category shared by generator diagnostics.</summary>
    public const string Category = "WebUIToolkit.MVVM.Flow.Generation";

    /// <summary>Gets the base URI for remediation documents.</summary>
    public const string DocumentationBaseUri =
        "https://github.com/ViktorJannicke/WebUIToolkit/blob/main/docs/diagnostics/";

    /// <summary>Gets <c>WUTFLOW0001</c>.</summary>
    public static readonly FlowGeneratorDiagnosticDescriptor DuplicateLogicalKey = Create(
        "WUTFLOW0001",
        "Duplicate logical key",
        "Logical key '{0}' is registered more than once in the {1} registry.",
        "the duplicate key argument; the first declaration is related",
        FlowGeneratorDiagnosticSeverity.Error);

    /// <summary>Gets <c>WUTFLOW0002</c>.</summary>
    public static readonly FlowGeneratorDiagnosticDescriptor InvalidLogicalKey = Create(
        "WUTFLOW0002",
        "Invalid logical key or contract",
        "{0} '{1}' is empty or is not a valid Flow identifier.",
        "the invalid key or contract argument",
        FlowGeneratorDiagnosticSeverity.Error);

    /// <summary>Gets <c>WUTFLOW0003</c>.</summary>
    public static readonly FlowGeneratorDiagnosticDescriptor InvalidStart = Create(
        "WUTFLOW0003",
        "Invalid start declaration",
        "{0} requires exactly one start declaration; found {1}.",
        "the ambiguous start declaration, registry declaration, or type declaration when missing",
        FlowGeneratorDiagnosticSeverity.Error);

    /// <summary>Gets <c>WUTFLOW0004</c>.</summary>
    public static readonly FlowGeneratorDiagnosticDescriptor InvalidDialogTypes = Create(
        "WUTFLOW0004",
        "Invalid dialog type contract",
        "Dialog '{0}' has an invalid request, result, or controller type: {1}.",
        "the invalid type argument or conflicting controller declaration",
        FlowGeneratorDiagnosticSeverity.Error);

    /// <summary>Gets <c>WUTFLOW0005</c>.</summary>
    public static readonly FlowGeneratorDiagnosticDescriptor InvalidWorkflowGraph = Create(
        "WUTFLOW0005",
        "Invalid workflow graph",
        "Workflow '{0}' has an invalid graph: {1}.",
        "the missing edge target, or workflow declaration when no finish path exists",
        FlowGeneratorDiagnosticSeverity.Error);

    /// <summary>Gets <c>WUTFLOW0006</c>.</summary>
    public static readonly FlowGeneratorDiagnosticDescriptor DuplicateSpecialAction = Create(
        "WUTFLOW0006",
        "Duplicate special action",
        "{0} declares more than one {1} action.",
        "the duplicate default or cancel action; the first declaration is related",
        FlowGeneratorDiagnosticSeverity.Error);

    /// <summary>Gets <c>WUTFLOW0007</c>.</summary>
    public static readonly FlowGeneratorDiagnosticDescriptor UnprovenViewModelRegistration = Create(
        "WUTFLOW0007",
        "ViewModel registration cannot be proven",
        "Generated registration for ViewModel '{0}' cannot be proven from the current compilation.",
        "the ViewModel type declaration or attribute",
        FlowGeneratorDiagnosticSeverity.Warning);

    /// <summary>Gets <c>WUTFLOW0008</c>.</summary>
    public static readonly FlowGeneratorDiagnosticDescriptor UnsupportedViewModel = Create(
        "WUTFLOW0008",
        "Unsupported ViewModel type",
        "ViewModel '{0}' cannot be generated because it is {1}.",
        "the ViewModel type declaration",
        FlowGeneratorDiagnosticSeverity.Error);

    /// <summary>Gets <c>WUTFLOW0009</c>.</summary>
    public static readonly FlowGeneratorDiagnosticDescriptor MissingCodec = Create(
        "WUTFLOW0009",
        "Missing Flow codec",
        "{0} '{1}' uses RecreateOnBack but has no registered deep-link or checkpoint codec.",
        "the RecreateOnBack argument or affected declaration",
        FlowGeneratorDiagnosticSeverity.Warning);

    /// <summary>Gets <c>WUTFLOW0010</c>.</summary>
    public static readonly FlowGeneratorDiagnosticDescriptor GeneratedIdentifierCollision = Create(
        "WUTFLOW0010",
        "Generated identifier collision",
        "Declarations '{0}' and '{1}' both normalize to generated identifier '{2}'.",
        "the later colliding declaration; the first declaration is related",
        FlowGeneratorDiagnosticSeverity.Error);

    private static readonly ReadOnlyCollection<FlowGeneratorDiagnosticDescriptor> OrderedDescriptorsValue =
        Array.AsReadOnly(new[]
        {
            DuplicateLogicalKey,
            InvalidLogicalKey,
            InvalidStart,
            InvalidDialogTypes,
            InvalidWorkflowGraph,
            DuplicateSpecialAction,
            UnprovenViewModelRegistration,
            UnsupportedViewModel,
            MissingCodec,
            GeneratedIdentifierCollision,
        });

    private static readonly ReadOnlyDictionary<string, FlowGeneratorDiagnosticDescriptor> DescriptorMap =
        CreateDescriptorMap();

    /// <summary>Gets every descriptor in ascending diagnostic-ID order.</summary>
    public static IReadOnlyList<FlowGeneratorDiagnosticDescriptor> OrderedDescriptors => OrderedDescriptorsValue;

    /// <summary>Gets every descriptor keyed with ordinal identity semantics.</summary>
    public static IReadOnlyDictionary<string, FlowGeneratorDiagnosticDescriptor> Descriptors => DescriptorMap;

    /// <summary>Looks up a frozen descriptor by identity.</summary>
    public static bool TryGetDescriptor(string? id, out FlowGeneratorDiagnosticDescriptor? descriptor)
    {
        if (id is null)
        {
            descriptor = null;
            return false;
        }

        return DescriptorMap.TryGetValue(id, out descriptor);
    }

    /// <summary>Determines whether a string has a currently reserved Flow diagnostic identity.</summary>
    public static bool IsReservedId(string? id)
    {
        if (id is null || id.Length != 11 || !id.StartsWith("WUTFLOW", StringComparison.Ordinal))
        {
            return false;
        }

        for (int index = 7; index < id.Length; index++)
        {
            if (id[index] < '0' || id[index] > '9')
            {
                return false;
            }
        }

        return string.CompareOrdinal(id, "WUTFLOW0001") >= 0 &&
            string.CompareOrdinal(id, "WUTFLOW0999") <= 0;
    }

    private static FlowGeneratorDiagnosticDescriptor Create(
        string id,
        string title,
        string messageFormat,
        string locationPolicy,
        FlowGeneratorDiagnosticSeverity severity) =>
        new FlowGeneratorDiagnosticDescriptor(id, title, messageFormat, locationPolicy, Category, severity);

    private static ReadOnlyDictionary<string, FlowGeneratorDiagnosticDescriptor> CreateDescriptorMap()
    {
        Dictionary<string, FlowGeneratorDiagnosticDescriptor> descriptors =
            new Dictionary<string, FlowGeneratorDiagnosticDescriptor>(StringComparer.Ordinal);
        foreach (FlowGeneratorDiagnosticDescriptor descriptor in OrderedDescriptorsValue)
        {
            descriptors.Add(descriptor.Id, descriptor);
        }

        return new ReadOnlyDictionary<string, FlowGeneratorDiagnosticDescriptor>(descriptors);
    }
}
