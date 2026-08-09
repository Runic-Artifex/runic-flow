using System;

namespace RunicFlow;

internal static class FlowKey
{
    public static string Validate(string value, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(value, parameterName);
        if (value.Length is 0 or > 128)
        {
            throw new ArgumentException("A Flow key must contain between 1 and 128 characters.", parameterName);
        }

        if (!string.Equals(value, value.Trim(), StringComparison.Ordinal))
        {
            throw new ArgumentException("A Flow key must not contain leading or trailing whitespace.", parameterName);
        }

        foreach (char character in value)
        {
            bool letter = character is >= 'A' and <= 'Z' or >= 'a' and <= 'z';
            bool digit = character is >= '0' and <= '9';
            if (!letter && !digit && character is not ('.' or '-' or '_' or '/'))
            {
                throw new ArgumentException(
                    "A Flow key may contain only ASCII letters, digits, '.', '-', '_', or '/'.",
                    parameterName);
            }
        }

        return value;
    }
}

/// <summary>Identifies a headless application-process definition.</summary>
public readonly record struct ProcessKey
{
    /// <summary>Initializes a process key.</summary>
    public ProcessKey(string value) => Value = FlowKey.Validate(value, nameof(value));

    /// <summary>Gets the ordinal, case-sensitive value.</summary>
    public string Value { get; }

    /// <inheritdoc />
    public override string ToString() => Value;
}

/// <summary>Identifies a logical operation kind.</summary>
public readonly record struct OperationKey
{
    /// <summary>Initializes an operation key.</summary>
    public OperationKey(string value) => Value = FlowKey.Validate(value, nameof(value));

    /// <summary>Gets the ordinal, case-sensitive value.</summary>
    public string Value { get; }

    /// <inheritdoc />
    public override string ToString() => Value;
}

/// <summary>Identifies a consumer-defined operation stage without carrying presentation text.</summary>
public readonly record struct OperationStage
{
    /// <summary>Initializes an operation stage.</summary>
    public OperationStage(string value) => Value = FlowKey.Validate(value, nameof(value));

    /// <summary>Gets the ordinal, case-sensitive value.</summary>
    public string Value { get; }

    /// <inheritdoc />
    public override string ToString() => Value;
}
