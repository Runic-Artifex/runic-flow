using System;

namespace RunicFlow;

/// <summary>Identifies an adapter-owned mapping boundary.</summary>
public enum FlowAdapterMappingKind
{
    /// <summary>A logical View contract to frontend content mapping.</summary>
    ViewContract,

    /// <summary>A navigation-region presenter mapping.</summary>
    NavigationPresenter,

    /// <summary>A dialog presenter mapping.</summary>
    DialogPresenter,

    /// <summary>An operation presenter mapping.</summary>
    OperationPresenter,

    /// <summary>A workflow presenter mapping.</summary>
    WorkflowPresenter,

    /// <summary>A logical icon mapping.</summary>
    Icon,

    /// <summary>A deep-link parameter codec mapping.</summary>
    ParameterCodec,

    /// <summary>A workflow checkpoint codec mapping.</summary>
    CheckpointCodec,
}

/// <summary>Identifies one adapter mapping using ordinal, case-sensitive semantics.</summary>
public readonly record struct FlowAdapterMappingIdentity
{
    /// <summary>Initializes an adapter mapping identity.</summary>
    public FlowAdapterMappingIdentity(FlowAdapterMappingKind kind, string logicalKey)
    {
        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind));
        }

        LogicalKey = FlowKey.Validate(logicalKey, nameof(logicalKey));
        Kind = kind;
    }

    /// <summary>Gets the mapping category.</summary>
    public FlowAdapterMappingKind Kind { get; }

    /// <summary>Gets the mapped logical key.</summary>
    public string LogicalKey { get; }

    /// <inheritdoc />
    public override string ToString() => $"{Kind}:{LogicalKey}";
}

/// <summary>Declares one mapping reported by an installed frontend adapter.</summary>
public sealed record FlowAdapterMapping
{
    /// <summary>Initializes an adapter mapping declaration.</summary>
    public FlowAdapterMapping(
        FlowAdapterMappingKind kind,
        string logicalKey,
        string adapterName,
        FlowRegistrationLocation location = default)
    {
        Identity = new FlowAdapterMappingIdentity(kind, logicalKey);
        AdapterName = FlowKey.Validate(adapterName, nameof(adapterName));
        Location = location;
    }

    /// <summary>Gets the composite mapping identity.</summary>
    public FlowAdapterMappingIdentity Identity { get; }

    /// <summary>Gets the stable adapter name.</summary>
    public string AdapterName { get; }

    /// <summary>Gets the mapping declaration location.</summary>
    public FlowRegistrationLocation Location { get; }
}
