using System.Collections.Generic;

namespace WebUIToolkit.MVVM.Flow.Tests;

internal static class ContractTestCatalog
{
    public static IReadOnlyList<ContractTest> All { get; } =
    [
        new("Flow keys are validated and compared ordinally", SharedPrimitiveTests.KeysAreValidatedAndOrdinal),
        new("Flow actions validate labels and preserve semantics", SharedPrimitiveTests.ActionsValidateLabelsAndPreserveSemantics),
        new("typed dialog outcomes preserve nullable completion", OutcomeTests.DialogOutcomesPreserveKind),
        new("typed workflow outcomes preserve nullable completion", OutcomeTests.WorkflowOutcomesPreserveKind),
        new("typed operation outcomes preserve result and fault", OutcomeTests.OperationOutcomesPreserveKind),
        new("completion claims may be denied and retried", CompletionTests.DeniedClaimMayBeRetried),
        new("completion races accept exactly one result", CompletionTests.ConcurrentResultRaceCompletesExactlyOnce),
        new("completion preserves winning cancellation token", CompletionTests.CancellationWinsExactlyOnce),
        new("completion preserves winning fault", CompletionTests.FaultWinsExactlyOnce),
        new("content descriptor freezes presentation metadata", SessionOwnershipTests.DescriptorFreezesMetadata),
        new("POCO session smoke has ordered teardown", SessionOwnershipTests.PocoSmokeHasOrderedTeardown),
        new("content session disposes children in reverse creation order", SessionOwnershipTests.ChildrenDisposeInReverseCreationOrder),
        new("content session disposal is exactly once under concurrency", SessionOwnershipTests.ConcurrentDisposalIsExactlyOnce),
        new("content session continues cleanup and reports ordered failures", SessionOwnershipTests.CleanupFailuresAreOrdered),
        new("scope-owned ViewModel is not disposed independently", SessionOwnershipTests.ScopeOwnedViewModelIsNotDisposedIndependently),
        new("content session accepts only one presenter lease", SessionOwnershipTests.SessionAcceptsOnlyOneLease),
        new("clock delay completes only after manual advancement", TimeoutTests.DelayUsesProvidedClock),
        new("timeout cancellation uses provided clock", TimeoutTests.CancellationSourceUsesProvidedClock),
        new("timeout cancellation links caller cancellation", TimeoutTests.CancellationSourceLinksCallerToken),
        new("Flow diagnostics use the reserved WUTFLOW range", DiagnosticsTests.IdentifiersUseReservedRange),
    ];
}
