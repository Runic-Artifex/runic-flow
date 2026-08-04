using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using RunicFlow;

namespace RunicFlow.Tests;

internal static class DiagnosticsTests
{
    public static ValueTask IdentifiersUseReservedRange()
    {
        IReadOnlyList<string> identifiers =
        [
            FlowDiagnosticIds.DuplicateLogicalKey,
            FlowDiagnosticIds.InvalidLogicalKey,
            FlowDiagnosticIds.InvalidStart,
            FlowDiagnosticIds.InvalidDialogTypes,
            FlowDiagnosticIds.InvalidWorkflowGraph,
            FlowDiagnosticIds.DuplicateSpecialAction,
            FlowDiagnosticIds.UnprovenViewModelRegistration,
            FlowDiagnosticIds.UnsupportedViewModel,
            FlowDiagnosticIds.MissingCodec,
            FlowDiagnosticIds.GeneratedIdentifierCollision,
        ];

        var unique = new HashSet<string>(StringComparer.Ordinal);
        foreach (string identifier in identifiers)
        {
            TestAssert.True(identifier.StartsWith("RFLOW", StringComparison.Ordinal));
            TestAssert.Equal(9, identifier.Length);
            TestAssert.True(unique.Add(identifier), $"Duplicate Flow diagnostic '{identifier}'.");
        }

        return ValueTask.CompletedTask;
    }
}
