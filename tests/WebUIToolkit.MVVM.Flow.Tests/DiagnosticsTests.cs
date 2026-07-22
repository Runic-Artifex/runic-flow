using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using WebUIToolkit.MVVM.Flow;

namespace WebUIToolkit.MVVM.Flow.Tests;

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
            TestAssert.True(identifier.StartsWith("WUTFLOW", StringComparison.Ordinal));
            TestAssert.Equal(11, identifier.Length);
            TestAssert.True(unique.Add(identifier), $"Duplicate Flow diagnostic '{identifier}'.");
        }

        return ValueTask.CompletedTask;
    }
}
