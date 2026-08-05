using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using RunicFlow.Navigation;
using RunicFlow.RunicToolkit.Navigation;
using RunicToolkit.Desktop;

namespace RunicFlow.RunicToolkit.Tests;

internal static class Program
{
    private static async Task<int> Main()
    {
        var calls = new List<string>();
        var primary = new PrimaryGuardedViewModel(calls, allow: true);
        var detail = new DetailGuardedViewModel(calls, allow: false);
        RegionKey primaryRegion = new("primary");
        RegionKey detailRegion = new("detail");
        RouteKey primaryRoute = new("primary.home");
        RouteKey detailRoute = new("detail.home");
        NavigationRegistry registry = new NavigationRegistryBuilder()
            .AddPage<PrimaryGuardedViewModel>(
                primaryRoute,
                new ViewContract("primary.home"),
                _ => ValueTask.FromResult(
                    new NavigationRouteContent(primary, new EmptyScope())))
            .AddPage<DetailGuardedViewModel>(
                detailRoute,
                new ViewContract("detail.home"),
                _ => ValueTask.FromResult(
                    new NavigationRouteContent(detail, new EmptyScope())))
            .AddRegion(new NavigationRegionRegistration(primaryRegion, primaryRoute, requireContent: true))
            .AddRegion(new NavigationRegionRegistration(detailRegion, detailRoute, requireContent: true))
            .Build();

        await using var navigation = new NavigationService(registry, new Presenter());
        await navigation.StartAsync().ConfigureAwait(false);
        var closeGuard = new NavigationDesktopCloseGuard(
            navigation,
            [primaryRegion, detailRegion]);

        DesktopCloseDecision denied = await closeGuard.CanCloseAsync(
            new DesktopCloseRequest(DesktopCloseReason.NativeWindow),
            CancellationToken.None).ConfigureAwait(false);
        if (denied.IsAllowed || denied.Reason != "detail denied" ||
            calls.Count != 1 || calls[0] != "detail")
        {
            Console.Error.WriteLine("FAIL: Flow close guard did not short-circuit in reverse region order.");
            return 1;
        }

        calls.Clear();
        detail.Allow = true;
        DesktopCloseDecision allowed = await closeGuard.CanCloseAsync(
            new DesktopCloseRequest(DesktopCloseReason.HostShutdown),
            CancellationToken.None).ConfigureAwait(false);
        if (!allowed.IsAllowed || allowed.Reason is not null ||
            calls.Count != 2 || calls[0] != "detail" || calls[1] != "primary")
        {
            Console.Error.WriteLine("FAIL: Flow close guard did not evaluate all regions in reverse order.");
            return 1;
        }

        Console.WriteLine("PASS: RunicFlow.RunicToolkit desktop close integration.");
        return 0;
    }

    private abstract class GuardedViewModel : INavigationGuard
    {
        private readonly string _name;
        private readonly List<string> _calls;

        public GuardedViewModel(string name, List<string> calls, bool allow)
        {
            _name = name;
            _calls = calls;
            Allow = allow;
        }

        public bool Allow { get; set; }

        public ValueTask<NavigationGuardResult> CanLeaveAsync(
            NavigationGuardContext context,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _calls.Add(_name);
            return ValueTask.FromResult(
                Allow ? NavigationGuardResult.Allow() : NavigationGuardResult.Deny($"{_name} denied"));
        }
    }

    private sealed class PrimaryGuardedViewModel : GuardedViewModel
    {
        public PrimaryGuardedViewModel(List<string> calls, bool allow)
            : base("primary", calls, allow)
        {
        }
    }

    private sealed class DetailGuardedViewModel : GuardedViewModel
    {
        public DetailGuardedViewModel(List<string> calls, bool allow)
            : base("detail", calls, allow)
        {
        }
    }

    private sealed class Presenter : INavigationRegionPresenter
    {
        public ValueTask<IFlowPresentationLease> PresentAsync(
            RegionKey region,
            FlowContentDescriptor content,
            NavigationPresentationContext context,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult<IFlowPresentationLease>(new Lease());

        public ValueTask ClearAsync(RegionKey region, CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;
    }

    private sealed class Lease : IFlowPresentationLease
    {
        public ValueTask CloseAsync(CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class EmptyScope : IDisposable
    {
        public void Dispose()
        {
        }
    }
}
