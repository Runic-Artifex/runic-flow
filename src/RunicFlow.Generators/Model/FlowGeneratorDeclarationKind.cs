namespace RunicFlow.Generators;

/// <summary>Identifies the Flow registry targeted by a generator declaration.</summary>
public enum FlowGeneratorDeclarationKind
{
    /// <summary>A navigation page declaration.</summary>
    Page,

    /// <summary>A typed dialog declaration.</summary>
    Dialog,

    /// <summary>An operation declaration.</summary>
    Operation,

    /// <summary>A workflow declaration.</summary>
    Workflow,

    /// <summary>A step belonging to a workflow graph.</summary>
    WorkflowStep,
}
