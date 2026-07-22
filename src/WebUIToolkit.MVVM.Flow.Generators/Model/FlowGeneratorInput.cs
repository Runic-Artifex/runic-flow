using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace WebUIToolkit.MVVM.Flow.Generators;

/// <summary>An immutable, deterministically ordered input to a Flow generator kernel.</summary>
public sealed class FlowGeneratorInput
{
    private readonly ReadOnlyCollection<FlowGeneratorDeclaration> declarations;

    /// <summary>Initializes generator input and sorts declarations by stable semantic identity.</summary>
    public FlowGeneratorInput(string moduleNamespace, string moduleName, IEnumerable<FlowGeneratorDeclaration> declarations)
    {
        ModuleNamespace = moduleNamespace ?? throw new ArgumentNullException(nameof(moduleNamespace));
        ModuleName = moduleName ?? throw new ArgumentNullException(nameof(moduleName));
        if (declarations is null)
        {
            throw new ArgumentNullException(nameof(declarations));
        }

        List<FlowGeneratorDeclaration> copied = new List<FlowGeneratorDeclaration>();
        foreach (FlowGeneratorDeclaration declaration in declarations)
        {
            if (declaration is null)
            {
                throw new ArgumentException("Declarations cannot contain null elements.", nameof(declarations));
            }

            copied.Add(declaration);
        }

        copied.Sort(CompareDeclarations);
        this.declarations = copied.AsReadOnly();
    }

    /// <summary>Gets the namespace for the generated module.</summary>
    public string ModuleNamespace { get; }

    /// <summary>Gets the unqualified generated module name.</summary>
    public string ModuleName { get; }

    /// <summary>Gets declarations in kind, key, type, contract, and source order.</summary>
    public IReadOnlyList<FlowGeneratorDeclaration> Declarations => declarations;

    private static int CompareDeclarations(FlowGeneratorDeclaration left, FlowGeneratorDeclaration right)
    {
        int result = left.Kind.CompareTo(right.Kind);
        if (result != 0)
        {
            return result;
        }

        result = StringComparer.Ordinal.Compare(left.Key, right.Key);
        if (result != 0)
        {
            return result;
        }

        result = StringComparer.Ordinal.Compare(left.TypeName, right.TypeName);
        if (result != 0)
        {
            return result;
        }

        result = StringComparer.Ordinal.Compare(left.Contract, right.Contract);
        if (result != 0)
        {
            return result;
        }

        result = StringComparer.Ordinal.Compare(left.Location.Path, right.Location.Path);
        return result != 0 ? result : left.Location.Start.CompareTo(right.Location.Start);
    }
}
