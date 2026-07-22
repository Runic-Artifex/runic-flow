namespace WebUIToolkit.MVVM.Flow;

/// <summary>Stable compiler and registration diagnostic identifiers reserved for Flow.</summary>
public static class FlowDiagnosticIds
{
    /// <summary>Duplicate logical key in a registry (draft FLOW001).</summary>
    public const string DuplicateLogicalKey = "WUTFLOW0001";
    /// <summary>Invalid or empty key or contract (draft FLOW002).</summary>
    public const string InvalidLogicalKey = "WUTFLOW0002";
    /// <summary>Missing or ambiguous start route or step (draft FLOW003).</summary>
    public const string InvalidStart = "WUTFLOW0003";
    /// <summary>Invalid closed dialog types or controller conflict (draft FLOW004).</summary>
    public const string InvalidDialogTypes = "WUTFLOW0004";
    /// <summary>Missing workflow edge target or no finish path (draft FLOW005).</summary>
    public const string InvalidWorkflowGraph = "WUTFLOW0005";
    /// <summary>Duplicate default or cancel action (draft FLOW006).</summary>
    public const string DuplicateSpecialAction = "WUTFLOW0006";
    /// <summary>ViewModel registration cannot be proven (draft FLOW007).</summary>
    public const string UnprovenViewModelRegistration = "WUTFLOW0007";
    /// <summary>Unsupported ViewModel type shape (draft FLOW008).</summary>
    public const string UnsupportedViewModel = "WUTFLOW0008";
    /// <summary>Missing deep-link or checkpoint codec (draft FLOW009).</summary>
    public const string MissingCodec = "WUTFLOW0009";
    /// <summary>Generated identifier collision after normalization (draft FLOW010).</summary>
    public const string GeneratedIdentifierCollision = "WUTFLOW0010";
}
