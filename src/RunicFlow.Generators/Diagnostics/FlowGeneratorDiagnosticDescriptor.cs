using System;
using System.Globalization;

namespace RunicFlow.Generators;

/// <summary>Stable metadata for a public Flow generator diagnostic.</summary>
public sealed class FlowGeneratorDiagnosticDescriptor
{
    private readonly bool isConfigurable;
    private readonly string versionIntroduced;

    internal FlowGeneratorDiagnosticDescriptor(
        string id,
        string title,
        string messageFormat,
        string locationPolicy,
        string category,
        FlowGeneratorDiagnosticSeverity defaultSeverity)
    {
        Id = id;
        Title = title;
        MessageFormat = messageFormat;
        LocationPolicy = locationPolicy;
        Category = category;
        DefaultSeverity = defaultSeverity;
        isConfigurable = false;
        versionIntroduced = FlowGeneratorDiagnosticCatalog.ContractVersion;
        HelpLinkUri = FlowGeneratorDiagnosticCatalog.DocumentationBaseUri + id + ".md";
    }

    /// <summary>Gets the emitted <c>RFLOW####</c> identity.</summary>
    public string Id { get; }

    /// <summary>Gets the stable diagnostic title.</summary>
    public string Title { get; }

    /// <summary>Gets the invariant-culture composite message format.</summary>
    public string MessageFormat { get; }

    /// <summary>Gets the frozen rule for selecting the primary source location.</summary>
    public string LocationPolicy { get; }

    /// <summary>Gets the stable diagnostic category.</summary>
    public string Category { get; }

    /// <summary>Gets the frozen default severity.</summary>
    public FlowGeneratorDiagnosticSeverity DefaultSeverity { get; }

    /// <summary>Gets whether build configuration may change this diagnostic's severity.</summary>
    public bool IsConfigurable => isConfigurable;

    /// <summary>Gets the diagnostic contract version that introduced the identity.</summary>
    public string VersionIntroduced => versionIntroduced;

    /// <summary>Gets the stable remediation documentation URI.</summary>
    public string HelpLinkUri { get; }

    /// <summary>Formats a message using invariant culture.</summary>
    public string FormatMessage(params object?[] arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        return string.Format(CultureInfo.InvariantCulture, MessageFormat, arguments);
    }
}
