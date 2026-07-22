using System;

namespace WebUIToolkit.MVVM.Flow;

/// <summary>Describes an action's semantic role.</summary>
public enum ActionRole
{
    /// <summary>A primary positive action.</summary>
    Primary,

    /// <summary>A secondary action.</summary>
    Secondary,

    /// <summary>An action that cancels the current conversation.</summary>
    Cancel,

    /// <summary>An action that performs a destructive operation.</summary>
    Destructive,
}

/// <summary>Describes adapter-neutral emphasis and status semantics.</summary>
public enum SemanticTone
{
    /// <summary>The adapter default.</summary>
    Default,
    /// <summary>Primary emphasis.</summary>
    Primary,
    /// <summary>Secondary emphasis.</summary>
    Secondary,
    /// <summary>Success status.</summary>
    Success,
    /// <summary>Informational status.</summary>
    Info,
    /// <summary>Warning status.</summary>
    Warning,
    /// <summary>Danger or error status.</summary>
    Danger,
    /// <summary>Light visual treatment.</summary>
    Light,
    /// <summary>Dark visual treatment.</summary>
    Dark,
}

/// <summary>Describes the logical placement of an action.</summary>
public enum ActionPlacement
{
    /// <summary>Place the action before the main action group.</summary>
    Leading,
    /// <summary>Place the action after the main action group.</summary>
    Trailing,
    /// <summary>Place the action in an overflow group.</summary>
    Overflow,
}

/// <summary>Provides adapter-neutral metadata for a logical action.</summary>
public sealed record FlowAction
{
    /// <summary>Initializes action metadata.</summary>
    public FlowAction(
        ActionKey key,
        string label,
        ActionRole role = ActionRole.Secondary,
        SemanticTone tone = SemanticTone.Default,
        IconKey? icon = null,
        ActionPlacement placement = ActionPlacement.Trailing,
        bool isDefault = false,
        bool isCancel = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);

        Key = key;
        Label = label;
        Role = role;
        Tone = tone;
        Icon = icon;
        Placement = placement;
        IsDefault = isDefault;
        IsCancel = isCancel;
    }

    /// <summary>Gets the action key.</summary>
    public ActionKey Key { get; }

    /// <summary>Gets the consumer-provided action label.</summary>
    public string Label { get; }

    /// <summary>Gets the semantic role.</summary>
    public ActionRole Role { get; }

    /// <summary>Gets the semantic tone.</summary>
    public SemanticTone Tone { get; }

    /// <summary>Gets the optional logical icon.</summary>
    public IconKey? Icon { get; }

    /// <summary>Gets the logical placement.</summary>
    public ActionPlacement Placement { get; }

    /// <summary>Gets whether this is the default action.</summary>
    public bool IsDefault { get; }

    /// <summary>Gets whether this is the cancellation action.</summary>
    public bool IsCancel { get; }
}
