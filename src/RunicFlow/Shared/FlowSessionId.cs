using System;

namespace RunicFlow;

/// <summary>
/// Identifies one logical Flow content session.
/// </summary>
public readonly record struct FlowSessionId
{
    /// <summary>
    /// Initializes a session identifier.
    /// </summary>
    /// <param name="value">The non-empty identifier value.</param>
    /// <exception cref="ArgumentException"><paramref name="value"/> is empty.</exception>
    public FlowSessionId(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("A Flow session identifier cannot be empty.", nameof(value));
        }

        Value = value;
    }

    /// <summary>
    /// Gets the identifier value.
    /// </summary>
    public Guid Value { get; }

    /// <summary>
    /// Creates a new session identifier.
    /// </summary>
    /// <returns>A new non-empty session identifier.</returns>
    public static FlowSessionId Create() => new(Guid.NewGuid());

    /// <inheritdoc />
    public override string ToString() => Value.ToString("D");
}
