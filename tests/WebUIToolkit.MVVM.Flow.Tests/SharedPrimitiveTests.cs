using System;
using System.Globalization;
using System.Threading.Tasks;
using WebUIToolkit.MVVM.Flow;

namespace WebUIToolkit.MVVM.Flow.Tests;

internal static class SharedPrimitiveTests
{
    public static async ValueTask KeysAreValidatedAndOrdinal()
    {
        CultureInfo originalCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("tr-TR");

            var upper = new RouteKey("I/home-1_test");
            var lower = new RouteKey("i/home-1_test");

            TestAssert.Equal("I/home-1_test", upper.Value);
            TestAssert.False(upper.Equals(lower));
            TestAssert.Equal("I/home-1_test", upper.ToString());

            await AssertInvalidKey(string.Empty).ConfigureAwait(false);
            await AssertInvalidKey(" leading").ConfigureAwait(false);
            await AssertInvalidKey("trailing ").ConfigureAwait(false);
            await AssertInvalidKey("not:allowed").ConfigureAwait(false);
            await AssertInvalidKey("café").ConfigureAwait(false);
            await AssertInvalidKey(new string('a', 129)).ConfigureAwait(false);
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }
    }

    public static async ValueTask ActionsValidateLabelsAndPreserveSemantics()
    {
        var action = new FlowAction(
            new ActionKey("delete"),
            "Delete",
            ActionRole.Destructive,
            SemanticTone.Danger,
            new IconKey("trash"),
            ActionPlacement.Overflow,
            isDefault: false,
            isCancel: false);

        TestAssert.Equal(new ActionKey("delete"), action.Key);
        TestAssert.Equal(ActionRole.Destructive, action.Role);
        TestAssert.Equal(SemanticTone.Danger, action.Tone);
        TestAssert.Equal(new IconKey("trash"), action.Icon);
        TestAssert.Equal(ActionPlacement.Overflow, action.Placement);

        await TestAssert.ThrowsAsync<ArgumentException>(
            () => new ValueTask(Task.FromException(
                CaptureException(() => _ = new FlowAction(new ActionKey("invalid"), " ")))))
            .ConfigureAwait(false);
    }

    private static async ValueTask AssertInvalidKey(string value)
    {
        await TestAssert.ThrowsAsync<ArgumentException>(
            () => new ValueTask(Task.FromException(
                CaptureException(() => _ = new RouteKey(value)))))
            .ConfigureAwait(false);
    }

    private static Exception CaptureException(Action action)
    {
        try
        {
            action();
        }
        catch (Exception exception)
        {
            return exception;
        }

        return new InvalidOperationException("Expected the action to throw.");
    }
}
