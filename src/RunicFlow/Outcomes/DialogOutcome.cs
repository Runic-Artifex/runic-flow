using System;

namespace RunicFlow.Dialogs;

/// <summary>Identifies how a dialog conversation ended.</summary>
public enum DialogOutcomeKind
{
    /// <summary>The dialog produced a typed result.</summary>
    Completed,

    /// <summary>The dialog was cancelled by its caller, an action, or shutdown.</summary>
    Cancelled,

    /// <summary>The presentation was dismissed without a typed result.</summary>
    Dismissed,
}

/// <summary>Represents the ordinary, non-faulting outcome of a dialog.</summary>
/// <typeparam name="T">The dialog result type.</typeparam>
/// <remarks>
/// <see cref="Kind"/>, rather than a null check on <see cref="Value"/>, determines whether
/// the dialog completed. A completed dialog may legitimately carry a null result.
/// </remarks>
public readonly record struct DialogOutcome<T>(DialogOutcomeKind Kind, T? Value)
{
    /// <summary>Creates a completed outcome, including a nullable result when <typeparamref name="T"/> permits it.</summary>
    public static DialogOutcome<T> Completed(T value) => new(DialogOutcomeKind.Completed, value);

    /// <summary>Creates a cancelled outcome.</summary>
    public static DialogOutcome<T> Cancelled() => new(DialogOutcomeKind.Cancelled, default);

    /// <summary>Creates a dismissed outcome.</summary>
    public static DialogOutcome<T> Dismissed() => new(DialogOutcomeKind.Dismissed, default);
}
