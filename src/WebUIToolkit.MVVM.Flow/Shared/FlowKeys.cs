using System;

namespace WebUIToolkit.MVVM.Flow;

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
            bool isAsciiLetter = character is >= 'A' and <= 'Z' or >= 'a' and <= 'z';
            bool isAsciiDigit = character is >= '0' and <= '9';
            bool isSeparator = character is '.' or '-' or '_' or '/';
            if (!isAsciiLetter && !isAsciiDigit && !isSeparator)
            {
                throw new ArgumentException(
                    "A Flow key may contain only ASCII letters, digits, '.', '-', '_', or '/'.",
                    parameterName);
            }
        }

        return value;
    }
}

/// <summary>Identifies a registered navigation route.</summary>
public readonly record struct RouteKey
{
    /// <summary>Initializes a route key.</summary>
    public RouteKey(string value) => Value = FlowKey.Validate(value, nameof(value));

    /// <summary>Gets the ordinal, case-sensitive key value.</summary>
    public string Value { get; }

    /// <inheritdoc />
    public override string ToString() => Value;
}

/// <summary>Identifies the logical presentation contract for content.</summary>
public readonly record struct ViewContract
{
    /// <summary>Initializes a View contract.</summary>
    public ViewContract(string value) => Value = FlowKey.Validate(value, nameof(value));

    /// <summary>Gets the ordinal, case-sensitive contract value.</summary>
    public string Value { get; }

    /// <inheritdoc />
    public override string ToString() => Value;
}

/// <summary>Identifies a navigation region.</summary>
public readonly record struct RegionKey
{
    /// <summary>Initializes a region key.</summary>
    public RegionKey(string value) => Value = FlowKey.Validate(value, nameof(value));

    /// <summary>Gets the ordinal, case-sensitive key value.</summary>
    public string Value { get; }

    /// <inheritdoc />
    public override string ToString() => Value;
}

/// <summary>Identifies a registered dialog.</summary>
public readonly record struct DialogKey
{
    /// <summary>Initializes a dialog key.</summary>
    public DialogKey(string value) => Value = FlowKey.Validate(value, nameof(value));

    /// <summary>Gets the ordinal, case-sensitive key value.</summary>
    public string Value { get; }

    /// <inheritdoc />
    public override string ToString() => Value;
}

/// <summary>Identifies a logical presentation policy.</summary>
public readonly record struct PresenterKey
{
    /// <summary>Initializes a presenter key.</summary>
    public PresenterKey(string value) => Value = FlowKey.Validate(value, nameof(value));

    /// <summary>Gets the ordinal, case-sensitive key value.</summary>
    public string Value { get; }

    /// <inheritdoc />
    public override string ToString() => Value;
}

/// <summary>Identifies an operation.</summary>
public readonly record struct OperationKey
{
    /// <summary>Initializes an operation key.</summary>
    public OperationKey(string value) => Value = FlowKey.Validate(value, nameof(value));

    /// <summary>Gets the ordinal, case-sensitive key value.</summary>
    public string Value { get; }

    /// <inheritdoc />
    public override string ToString() => Value;
}

/// <summary>Identifies a workflow definition.</summary>
public readonly record struct WorkflowKey
{
    /// <summary>Initializes a workflow key.</summary>
    public WorkflowKey(string value) => Value = FlowKey.Validate(value, nameof(value));

    /// <summary>Gets the ordinal, case-sensitive key value.</summary>
    public string Value { get; }

    /// <inheritdoc />
    public override string ToString() => Value;
}

/// <summary>Identifies a workflow step.</summary>
public readonly record struct StepKey
{
    /// <summary>Initializes a workflow step key.</summary>
    public StepKey(string value) => Value = FlowKey.Validate(value, nameof(value));

    /// <summary>Gets the ordinal, case-sensitive key value.</summary>
    public string Value { get; }

    /// <inheritdoc />
    public override string ToString() => Value;
}

/// <summary>Identifies an action dispatched to a Flow session.</summary>
public readonly record struct ActionKey
{
    /// <summary>Initializes an action key.</summary>
    public ActionKey(string value) => Value = FlowKey.Validate(value, nameof(value));

    /// <summary>Gets the ordinal, case-sensitive key value.</summary>
    public string Value { get; }

    /// <inheritdoc />
    public override string ToString() => Value;
}

/// <summary>Identifies a logical icon mapped by a presentation adapter.</summary>
public readonly record struct IconKey
{
    /// <summary>Initializes an icon key.</summary>
    public IconKey(string value) => Value = FlowKey.Validate(value, nameof(value));

    /// <summary>Gets the ordinal, case-sensitive key value.</summary>
    public string Value { get; }

    /// <inheritdoc />
    public override string ToString() => Value;
}
