namespace WebUIToolkit.MVVM.Flow.Generators;

/// <summary>Defines the compiler-independent boundary implemented by a Flow generation kernel.</summary>
/// <remarks>
/// A future Roslyn incremental-generator adapter converts compiler symbols into
/// <see cref="FlowGeneratorInput"/>, invokes this boundary, and translates the result back to
/// compiler diagnostics and generated sources.
/// </remarks>
public interface IFlowGeneratorKernel
{
    /// <summary>Validates a frozen input model and emits deterministic results.</summary>
    FlowGenerationResult Generate(FlowGeneratorInput input);
}
