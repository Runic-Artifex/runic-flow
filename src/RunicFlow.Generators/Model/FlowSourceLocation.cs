using System;

namespace RunicFlow.Generators;

/// <summary>Identifies an exact source location without depending on a compiler object model.</summary>
public sealed class FlowSourceLocation : IEquatable<FlowSourceLocation>
{
    /// <summary>Initializes a source location.</summary>
    /// <param name="path">A logical source path. Directory separators are normalized to <c>/</c>.</param>
    /// <param name="start">The zero-based UTF-16 start offset.</param>
    /// <param name="length">The UTF-16 span length.</param>
    /// <param name="line">The zero-based start line.</param>
    /// <param name="column">The zero-based start column.</param>
    public FlowSourceLocation(string path, int start, int length, int line, int column)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentOutOfRangeException.ThrowIfNegative(start);
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        ArgumentOutOfRangeException.ThrowIfNegative(line);
        ArgumentOutOfRangeException.ThrowIfNegative(column);

        Path = path.Replace('\\', '/');
        Start = start;
        Length = length;
        Line = line;
        Column = column;
    }

    /// <summary>Gets the normalized logical path.</summary>
    public string Path { get; }

    /// <summary>Gets the zero-based UTF-16 start offset.</summary>
    public int Start { get; }

    /// <summary>Gets the UTF-16 span length.</summary>
    public int Length { get; }

    /// <summary>Gets the zero-based start line.</summary>
    public int Line { get; }

    /// <summary>Gets the zero-based start column.</summary>
    public int Column { get; }

    /// <inheritdoc />
    public bool Equals(FlowSourceLocation? other) =>
        other is not null &&
        StringComparer.Ordinal.Equals(Path, other.Path) &&
        Start == other.Start &&
        Length == other.Length &&
        Line == other.Line &&
        Column == other.Column;

    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as FlowSourceLocation);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        unchecked
        {
            int hash = StringComparer.Ordinal.GetHashCode(Path);
            hash = (hash * 397) ^ Start;
            hash = (hash * 397) ^ Length;
            hash = (hash * 397) ^ Line;
            return (hash * 397) ^ Column;
        }
    }
}
