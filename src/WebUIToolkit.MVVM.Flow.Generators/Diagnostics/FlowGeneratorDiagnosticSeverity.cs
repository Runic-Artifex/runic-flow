namespace WebUIToolkit.MVVM.Flow.Generators;

/// <summary>Describes the frozen default severity of a Flow generator diagnostic.</summary>
public enum FlowGeneratorDiagnosticSeverity
{
    /// <summary>The diagnostic permits generation to continue.</summary>
    Warning,

    /// <summary>The diagnostic prevents valid source emission.</summary>
    Error,
}
