using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace RunicFlow.Generators;

/// <summary>A compiler-independent declaration discovered by a Flow generator front end.</summary>
public sealed class FlowGeneratorDeclaration
{
    private readonly ReadOnlyCollection<FlowGeneratorProperty> properties;

    /// <summary>Initializes a Flow generator declaration.</summary>
    public FlowGeneratorDeclaration(
        FlowGeneratorDeclarationKind kind,
        string key,
        string typeName,
        string contract,
        FlowSourceLocation location,
        IEnumerable<FlowGeneratorProperty> properties)
    {
        Kind = kind;
        Key = key ?? throw new ArgumentNullException(nameof(key));
        TypeName = typeName ?? throw new ArgumentNullException(nameof(typeName));
        Contract = contract ?? throw new ArgumentNullException(nameof(contract));
        Location = location ?? throw new ArgumentNullException(nameof(location));
        ArgumentNullException.ThrowIfNull(properties);

        List<FlowGeneratorProperty> copied = new List<FlowGeneratorProperty>();
        foreach (FlowGeneratorProperty property in properties)
        {
            if (property is null)
            {
                throw new ArgumentException("Properties cannot contain null elements.", nameof(properties));
            }

            copied.Add(property);
        }

        copied.Sort(CompareProperties);
        this.properties = copied.AsReadOnly();
    }

    /// <summary>Gets the target registry kind.</summary>
    public FlowGeneratorDeclarationKind Kind { get; }

    /// <summary>Gets the logical registration key exactly as declared.</summary>
    public string Key { get; }

    /// <summary>Gets the fully qualified ViewModel or workflow type name.</summary>
    public string TypeName { get; }

    /// <summary>Gets the declared view contract, or an empty string when it does not apply.</summary>
    public string Contract { get; }

    /// <summary>Gets the primary declaration location.</summary>
    public FlowSourceLocation Location { get; }

    /// <summary>Gets additional declaration properties in ordinal name/value order.</summary>
    public IReadOnlyList<FlowGeneratorProperty> Properties => properties;

    private static int CompareProperties(FlowGeneratorProperty left, FlowGeneratorProperty right)
    {
        int result = StringComparer.Ordinal.Compare(left.Name, right.Name);
        return result != 0 ? result : StringComparer.Ordinal.Compare(left.Value, right.Value);
    }
}
