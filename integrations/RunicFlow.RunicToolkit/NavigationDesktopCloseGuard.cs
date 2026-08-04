using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using WebUIToolkit.Desktop;
using WebUIToolkit.MVVM.Flow;

namespace WebUIToolkit.MVVM.Navigation;

/// <summary>
/// Maps application close onto the existing current-page navigation guards without
/// introducing a second guard contract into migrated ViewModels.
/// </summary>
public sealed class NavigationDesktopCloseGuard : IDesktopCloseGuard
{
    private readonly INavigationService _navigation;
    private readonly RegionKey[] _regions;

    /// <summary>Creates a close guard over an explicit ordered region set.</summary>
    public NavigationDesktopCloseGuard(
        INavigationService navigation,
        IReadOnlyList<RegionKey> regions)
    {
        _navigation = navigation ?? throw new ArgumentNullException(nameof(navigation));
        ArgumentNullException.ThrowIfNull(regions);
        if (regions.Count == 0)
        {
            throw new ArgumentException(
                "At least one navigation region is required.",
                nameof(regions));
        }

        _regions = new RegionKey[regions.Count];
        HashSet<RegionKey> unique = [];
        for (int index = 0; index < regions.Count; index++)
        {
            RegionKey region = regions[index];
            if (string.IsNullOrEmpty(region.Value) || !unique.Add(region))
            {
                throw new ArgumentException(
                    "Navigation close regions must be non-empty and unique.",
                    nameof(regions));
            }

            _regions[index] = region;
        }
    }

    /// <inheritdoc />
    public async ValueTask<DesktopCloseDecision> CanCloseAsync(
        DesktopCloseRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        for (int index = _regions.Length - 1; index >= 0; index--)
        {
            NavigationGuardResult result = await _navigation
                .CanLeaveAsync(_regions[index], cancellationToken)
                .ConfigureAwait(false);
            if (!result.IsAllowed)
            {
                return DesktopCloseDecision.Deny(
                    result.Reason ?? $"Navigation region '{_regions[index]}' denied close.");
            }
        }

        return DesktopCloseDecision.Allow();
    }
}
