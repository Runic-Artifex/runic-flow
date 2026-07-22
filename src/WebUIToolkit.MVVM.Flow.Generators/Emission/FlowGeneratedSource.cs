using System;

namespace WebUIToolkit.MVVM.Flow.Generators;

/// <summary>A generated UTF-16 C# source and its deterministic hint name.</summary>
public sealed class FlowGeneratedSource
{
    /// <summary>Initializes a generated source result.</summary>
    public FlowGeneratedSource(string hintName, string sourceText)
    {
        if (string.IsNullOrEmpty(hintName))
        {
            throw new ArgumentException("A generated-source hint name is required.", nameof(hintName));
        }

        HintName = hintName;
        SourceText = FlowDeterministicEmission.NormalizeSourceText(
            sourceText ?? throw new ArgumentNullException(nameof(sourceText)));
    }

    /// <summary>Gets the stable, machine-path-independent hint name.</summary>
    public string HintName { get; }

    /// <summary>Gets generated C# normalized to line-feed endings and one final line feed.</summary>
    public string SourceText { get; }
}
