using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace WebUIToolkit.MVVM.Flow.Generators;

/// <summary>A formatted diagnostic produced by a compiler-independent Flow generator kernel.</summary>
public sealed class FlowGeneratorDiagnostic
{
    private readonly ReadOnlyCollection<FlowSourceLocation> relatedLocations;

    /// <summary>Initializes a formatted diagnostic.</summary>
    public FlowGeneratorDiagnostic(
        FlowGeneratorDiagnosticDescriptor descriptor,
        FlowSourceLocation location,
        IEnumerable<FlowSourceLocation> relatedLocations,
        params object?[] messageArguments)
    {
        Descriptor = descriptor ?? throw new ArgumentNullException(nameof(descriptor));
        Location = location ?? throw new ArgumentNullException(nameof(location));
        ArgumentNullException.ThrowIfNull(relatedLocations);

        List<FlowSourceLocation> copiedLocations = new List<FlowSourceLocation>();
        foreach (FlowSourceLocation relatedLocation in relatedLocations)
        {
            if (relatedLocation is null)
            {
                throw new ArgumentException("Related locations cannot contain null elements.", nameof(relatedLocations));
            }

            copiedLocations.Add(relatedLocation);
        }

        this.relatedLocations = copiedLocations.AsReadOnly();
        Message = descriptor.FormatMessage(messageArguments);
    }

    /// <summary>Gets the stable descriptor.</summary>
    public FlowGeneratorDiagnosticDescriptor Descriptor { get; }

    /// <summary>Gets the primary exact source location.</summary>
    public FlowSourceLocation Location { get; }

    /// <summary>Gets related locations in deterministic producer order.</summary>
    public IReadOnlyList<FlowSourceLocation> RelatedLocations => relatedLocations;

    /// <summary>Gets the invariant formatted message.</summary>
    public string Message { get; }
}
