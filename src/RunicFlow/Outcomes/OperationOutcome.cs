using System;

namespace RunicFlow.Operations;

/// <summary>Identifies how an operation ended.</summary>
public enum OperationOutcomeKind
{
    /// <summary>The operation produced a typed result.</summary>
    Succeeded,

    /// <summary>The operation observed cancellation.</summary>
    Cancelled,

    /// <summary>The operation failed with an exception.</summary>
    Faulted,
}

/// <summary>Represents the typed outcome returned by a non-throwing operation API.</summary>
/// <typeparam name="T">The operation result type.</typeparam>
public readonly record struct OperationOutcome<T>(OperationOutcomeKind Kind, T? Value, Exception? Exception)
{
    /// <summary>Creates a successful outcome, including a nullable result when <typeparamref name="T"/> permits it.</summary>
    public static OperationOutcome<T> Succeeded(T value) =>
        new(OperationOutcomeKind.Succeeded, value, null);

    /// <summary>Creates a cancelled outcome.</summary>
    public static OperationOutcome<T> Cancelled() =>
        new(OperationOutcomeKind.Cancelled, default, null);

    /// <summary>Creates a faulted outcome and preserves the original exception.</summary>
    public static OperationOutcome<T> Faulted(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return new OperationOutcome<T>(OperationOutcomeKind.Faulted, default, exception);
    }
}
