using System;
using System.Threading.Tasks;
using WebUIToolkit.MVVM.Flow;

namespace WebUIToolkit.MVVM.Flow.Tests.WaveB;

internal static class SessionHierarchyTests
{
    public static async ValueTask AncestorCycleIsRejectedBeforeOwnershipChanges()
    {
        FlowContentSession parent = CreateSession("parent");
        FlowContentSession child = CreateSession("child");
        parent.AddChild(child);

        bool rejected = false;
        try
        {
            child.AddChild(parent);
        }
        catch (InvalidOperationException)
        {
            rejected = true;
        }
        catch (ArgumentException)
        {
            rejected = true;
        }

        TestAssert.True(rejected, "A child must not be able to acquire one of its ancestors.");
        await parent.DisposeAsync().ConfigureAwait(false);
    }

    private static FlowContentSession CreateSession(string key)
    {
        object viewModel = new();
        return new FlowContentSession(
            FlowSessionId.Create(),
            new ViewContract($"cycle/{key}"),
            viewModel,
            viewModel.GetType(),
            metadata: null,
            new EmptyScope(),
            ownsViewModel: false);
    }

    private sealed class EmptyScope : IDisposable
    {
        public void Dispose()
        {
        }
    }
}
