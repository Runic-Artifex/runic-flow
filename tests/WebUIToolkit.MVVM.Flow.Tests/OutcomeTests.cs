using System;
using System.Threading.Tasks;
using WebUIToolkit.MVVM.Dialogs;
using WebUIToolkit.MVVM.Operations;
using WebUIToolkit.MVVM.Workflows;

namespace WebUIToolkit.MVVM.Flow.Tests;

internal static class OutcomeTests
{
    public static ValueTask DialogOutcomesPreserveKind()
    {
        DialogOutcome<string?> completed = DialogOutcome<string?>.Completed(null);
        DialogOutcome<string?> cancelled = DialogOutcome<string?>.Cancelled();
        DialogOutcome<string?> dismissed = DialogOutcome<string?>.Dismissed();

        TestAssert.Equal(DialogOutcomeKind.Completed, completed.Kind);
        TestAssert.Equal<string?>(null, completed.Value);
        TestAssert.Equal(DialogOutcomeKind.Cancelled, cancelled.Kind);
        TestAssert.Equal<string?>(null, cancelled.Value);
        TestAssert.Equal(DialogOutcomeKind.Dismissed, dismissed.Kind);
        TestAssert.Equal<string?>(null, dismissed.Value);
        TestAssert.False(completed.Equals(cancelled));
        return ValueTask.CompletedTask;
    }

    public static ValueTask WorkflowOutcomesPreserveKind()
    {
        WorkflowOutcome<string?> completed = WorkflowOutcome<string?>.Completed(null);
        WorkflowOutcome<string?> cancelled = WorkflowOutcome<string?>.Cancelled();
        WorkflowOutcome<string?> abandoned = WorkflowOutcome<string?>.Abandoned();

        TestAssert.Equal(WorkflowOutcomeKind.Completed, completed.Kind);
        TestAssert.Equal<string?>(null, completed.Value);
        TestAssert.Equal(WorkflowOutcomeKind.Cancelled, cancelled.Kind);
        TestAssert.Equal<string?>(null, cancelled.Value);
        TestAssert.Equal(WorkflowOutcomeKind.Abandoned, abandoned.Kind);
        TestAssert.Equal<string?>(null, abandoned.Value);
        TestAssert.False(completed.Equals(cancelled));
        return ValueTask.CompletedTask;
    }

    public static ValueTask OperationOutcomesPreserveKind()
    {
        OperationOutcome<string?> succeeded = OperationOutcome<string?>.Succeeded(null);
        OperationOutcome<string?> cancelled = OperationOutcome<string?>.Cancelled();
        var failure = new InvalidOperationException("expected");
        OperationOutcome<string?> faulted = OperationOutcome<string?>.Faulted(failure);

        TestAssert.Equal(OperationOutcomeKind.Succeeded, succeeded.Kind);
        TestAssert.Equal<string?>(null, succeeded.Value);
        TestAssert.Equal<Exception?>(null, succeeded.Exception);
        TestAssert.Equal(OperationOutcomeKind.Cancelled, cancelled.Kind);
        TestAssert.Equal<Exception?>(null, cancelled.Exception);
        TestAssert.Equal(OperationOutcomeKind.Faulted, faulted.Kind);
        TestAssert.True(ReferenceEquals(failure, faulted.Exception));
        return ValueTask.CompletedTask;
    }
}
