using System;
using System.Threading;
using System.Threading.Tasks;
using WebUIToolkit.Desktop;
using WebUIToolkit.MVVM.Dialogs;
using WebUIToolkit.MVVM.Flow.Presentation;
using WebUIToolkit.MVVM.Navigation;

namespace WebUIToolkit.MVVM.Flow.Tests;

internal static class ObservablePresenterTests
{
    public static async ValueTask NavigationOutletsIgnoreStaleLeaseClosure()
    {
        var presenter = new ObservableNavigationPresenter();
        var region = new RegionKey("main");
        int observations = 0;
        presenter.Changed += (_, _) => observations++;
        presenter.Changed += (_, _) => throw new InvalidOperationException("consumer failure");

        FlowContentDescriptor first = Content("first");
        FlowContentDescriptor second = Content("second");
        IFlowPresentationLease firstLease = await presenter.PresentAsync(
            region,
            first,
            new NavigationPresentationContext(
                NavigationMode.Push,
                previousSessionId: null,
                resultingDepth: 1,
                isBackNavigation: false),
            CancellationToken.None);
        IFlowPresentationLease secondLease = await presenter.PresentAsync(
            region,
            second,
            new NavigationPresentationContext(
                NavigationMode.Push,
                first.SessionId,
                resultingDepth: 2,
                isBackNavigation: false),
            CancellationToken.None);

        await firstLease.CloseAsync(CancellationToken.None);
        TestAssert.Equal(second.SessionId, presenter.GetSnapshot(region).Content!.SessionId);
        TestAssert.Equal(2, observations);

        await secondLease.DisposeAsync();
        NavigationOutletSnapshot empty = presenter.GetSnapshot(region);
        TestAssert.True(empty.Content is null);
        TestAssert.Equal(3L, empty.Version);
        TestAssert.Equal(3, observations);
        await firstLease.DisposeAsync();
    }

    public static async ValueTask DialogOutletsPreserveTypedControllers()
    {
        var presenter = new ObservableDialogPresenter();
        var controller = new RecordingDialogController<string>();
        FlowContentDescriptor content = Content("dialog");
        var presentation = new DialogPresentation<string>(
            new DialogKey("confirm"),
            content,
            controller,
            [
                new FlowAction(
                    new ActionKey("cancel"),
                    "Cancel",
                    ActionRole.Cancel,
                    isCancel: true),
            ]);
        int observations = 0;
        presenter.Changed += (_, args) =>
        {
            observations++;
            _ = args.Active.Count;
        };

        IFlowPresentationLease lease = await presenter.PresentAsync(
            presentation,
            CancellationToken.None);
        DialogOutletSnapshot snapshot = presenter.Active[0];
        TestAssert.Equal(content.SessionId, snapshot.Content.SessionId);
        TestAssert.True(await snapshot.CancelAsync());
        TestAssert.Equal(1, controller.CancelCount);
        TestAssert.True(await snapshot.DismissAsync());
        TestAssert.Equal(1, controller.DismissCount);

        await lease.CloseAsync(CancellationToken.None);
        TestAssert.Equal(0, presenter.Active.Count);
        TestAssert.Equal(2, observations);
        await lease.DisposeAsync();
    }

    public static async ValueTask DesktopCloseReusesNavigationGuards()
    {
        var region = new RegionKey("main");
        var route = new RouteKey("home");
        var viewModel = new GuardedViewModel();
        NavigationRegistry registry = new NavigationRegistryBuilder()
            .AddRegion(new NavigationRegionRegistration(region, route, requireContent: true))
            .AddPage<GuardedViewModel>(
                route,
                new ViewContract("home"),
                _ => ValueTask.FromResult(
                    new NavigationRouteContent(
                        viewModel,
                        new EmptyScope())))
            .Build();
        await using var navigation = new NavigationService(
            registry,
            new ObservableNavigationPresenter());
        await navigation.StartAsync();
        var closeGuard = new NavigationDesktopCloseGuard(navigation, [region]);

        DesktopCloseDecision denied = await closeGuard.CanCloseAsync(
            new DesktopCloseRequest(DesktopCloseReason.Application),
            CancellationToken.None);
        TestAssert.False(denied.IsAllowed);
        TestAssert.Equal("unsaved work", denied.Reason);

        viewModel.AllowClose = true;
        DesktopCloseDecision allowed = await closeGuard.CanCloseAsync(
            new DesktopCloseRequest(DesktopCloseReason.Application),
            CancellationToken.None);
        TestAssert.True(allowed.IsAllowed);
    }

    private static FlowContentDescriptor Content(string contract) =>
        new(
            FlowSessionId.Create(),
            new ViewContract(contract),
            new object(),
            typeof(object));

    private sealed class RecordingDialogController<TResult> : IDialogController<TResult>
    {
        internal int CancelCount { get; private set; }
        internal int DismissCount { get; private set; }

        public bool IsCompletionRequested => false;

        public ValueTask<bool> CompleteAsync(
            TResult result,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(true);
        }

        public ValueTask<bool> CancelAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CancelCount++;
            return ValueTask.FromResult(true);
        }

        public ValueTask<bool> DismissAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            DismissCount++;
            return ValueTask.FromResult(true);
        }
    }

    private sealed class GuardedViewModel : INavigationGuard
    {
        internal bool AllowClose { get; set; }

        public ValueTask<NavigationGuardResult> CanLeaveAsync(
            NavigationGuardContext context,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(
                AllowClose
                    ? NavigationGuardResult.Allow()
                    : NavigationGuardResult.Deny("unsaved work"));
        }
    }

    private sealed class EmptyScope : IDisposable
    {
        public void Dispose()
        {
        }
    }
}
