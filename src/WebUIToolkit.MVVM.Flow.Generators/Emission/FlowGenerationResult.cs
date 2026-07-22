using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace WebUIToolkit.MVVM.Flow.Generators;

/// <summary>An immutable generator-kernel result ready for a compiler-specific adapter.</summary>
public sealed class FlowGenerationResult
{
    private readonly ReadOnlyCollection<FlowGeneratedSource> sources;
    private readonly ReadOnlyCollection<FlowGeneratorDiagnostic> diagnostics;

    /// <summary>Initializes a generation result and freezes its ordered content.</summary>
    public FlowGenerationResult(
        IEnumerable<FlowGeneratedSource> sources,
        IEnumerable<FlowGeneratorDiagnostic> diagnostics)
    {
        if (sources is null)
        {
            throw new ArgumentNullException(nameof(sources));
        }

        if (diagnostics is null)
        {
            throw new ArgumentNullException(nameof(diagnostics));
        }

        List<FlowGeneratedSource> copiedSources = new List<FlowGeneratedSource>();
        foreach (FlowGeneratedSource source in sources)
        {
            if (source is null)
            {
                throw new ArgumentException("Sources cannot contain null elements.", nameof(sources));
            }

            copiedSources.Add(source);
        }

        List<FlowGeneratorDiagnostic> copiedDiagnostics = new List<FlowGeneratorDiagnostic>();
        foreach (FlowGeneratorDiagnostic diagnostic in diagnostics)
        {
            if (diagnostic is null)
            {
                throw new ArgumentException("Diagnostics cannot contain null elements.", nameof(diagnostics));
            }

            copiedDiagnostics.Add(diagnostic);
        }

        this.sources = copiedSources.AsReadOnly();
        this.diagnostics = copiedDiagnostics.AsReadOnly();
    }

    /// <summary>Gets emitted sources in deterministic producer order.</summary>
    public IReadOnlyList<FlowGeneratedSource> Sources => sources;

    /// <summary>Gets diagnostics in deterministic producer order.</summary>
    public IReadOnlyList<FlowGeneratorDiagnostic> Diagnostics => diagnostics;

    /// <summary>Gets whether any diagnostic has error severity.</summary>
    public bool HasErrors
    {
        get
        {
            foreach (FlowGeneratorDiagnostic diagnostic in diagnostics)
            {
                if (diagnostic.Descriptor.DefaultSeverity == FlowGeneratorDiagnosticSeverity.Error)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
