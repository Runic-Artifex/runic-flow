namespace RunicFlow;

/// <summary>Stable compiler and registration diagnostic identifiers reserved for Flow.</summary>
public static class FlowDiagnosticIds
{
    /// <summary>Duplicate logical key in a registry (draft FLOW001).</summary>
    public const string DuplicateLogicalKey = "RFLOW0001";
    /// <summary>Invalid or empty key or contract (draft FLOW002).</summary>
    public const string InvalidLogicalKey = "RFLOW0002";
    /// <summary>Missing or ambiguous start route or step (draft FLOW003).</summary>
    public const string InvalidStart = "RFLOW0003";
    /// <summary>Invalid closed dialog types or controller conflict (draft FLOW004).</summary>
    public const string InvalidDialogTypes = "RFLOW0004";
    /// <summary>Missing workflow edge target or no finish path (draft FLOW005).</summary>
    public const string InvalidWorkflowGraph = "RFLOW0005";
    /// <summary>Duplicate default or cancel action (draft FLOW006).</summary>
    public const string DuplicateSpecialAction = "RFLOW0006";
    /// <summary>ViewModel registration cannot be proven (draft FLOW007).</summary>
    public const string UnprovenViewModelRegistration = "RFLOW0007";
    /// <summary>Unsupported ViewModel type shape (draft FLOW008).</summary>
    public const string UnsupportedViewModel = "RFLOW0008";
    /// <summary>Missing deep-link or checkpoint codec (draft FLOW009).</summary>
    public const string MissingCodec = "RFLOW0009";
    /// <summary>Generated identifier collision after normalization (draft FLOW010).</summary>
    public const string GeneratedIdentifierCollision = "RFLOW0010";
}
