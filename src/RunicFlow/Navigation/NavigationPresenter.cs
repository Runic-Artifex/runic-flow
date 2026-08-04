using System;
using System.Threading;
using System.Threading.Tasks;
using RunicFlow;

namespace RunicFlow.Navigation;

/// <summary>Describes the logical transition requested from a region presenter.</summary>
public sealed record NavigationPresentationContext
{
    /// <summary>Initializes presentation context.</summary>
    public NavigationPresentationContext(
        NavigationMode mode,
        FlowSessionId? previousSessionId,
        int resultingDepth,
        bool isBackNavigation)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(resultingDepth);

        Mode = mode;
        PreviousSessionId = previousSessionId;
        ResultingDepth = resultingDepth;
        IsBackNavigation = isBackNavigation;
    }

    /// <summary>Gets the logical stack mutation.</summary>
    public NavigationMode Mode { get; }
    /// <summary>Gets the content session replaced visually, when present.</summary>
    public FlowSessionId? PreviousSessionId { get; }
    /// <summary>Gets the stack depth after commit.</summary>
    public int ResultingDepth { get; }
    /// <summary>Gets whether an existing history entry is being revisited.</summary>
    public bool IsBackNavigation { get; }
}

/// <summary>Presents logical navigation content without exposing frontend types.</summary>
public interface INavigationRegionPresenter
{
    /// <summary>
    /// Atomically prepares and presents target content while retaining or restoring old content on failure.
    /// </summary>
    ValueTask<IFlowPresentationLease> PresentAsync(
        RegionKey region,
        FlowContentDescriptor content,
        NavigationPresentationContext context,
        CancellationToken cancellationToken);

    /// <summary>Clears the logical region outlet before an empty snapshot commits.</summary>
    ValueTask ClearAsync(RegionKey region, CancellationToken cancellationToken);
}
