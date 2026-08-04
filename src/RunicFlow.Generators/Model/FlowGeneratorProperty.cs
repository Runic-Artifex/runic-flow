using System;

namespace RunicFlow.Generators;

/// <summary>Represents one generator declaration property as stable text.</summary>
public sealed class FlowGeneratorProperty
{
    /// <summary>Initializes a generator property.</summary>
    public FlowGeneratorProperty(string name, string value)
    {
        if (string.IsNullOrEmpty(name))
        {
            throw new ArgumentException("A property name is required.", nameof(name));
        }

        Name = name;
        Value = value ?? throw new ArgumentNullException(nameof(value));
    }

    /// <summary>Gets the ordinal property name.</summary>
    public string Name { get; }

    /// <summary>Gets the invariant textual value.</summary>
    public string Value { get; }
}
