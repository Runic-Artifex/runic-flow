using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using WebUIToolkit.MVVM.Flow;
using WebUIToolkit.MVVM.Navigation;

namespace WebUIToolkit.MVVM.Flow.Tests.WaveB;

internal static class NavigationTests
{
    private static readonly RegionKey MainRegion = new("main");
    private static readonly RouteKey HomeRoute = new("home");
    private static readonly RouteKey DetailsRoute = new("details");
    private static readonly RouteKey SettingsRoute = new("settings");

    public static async ValueTask StartPushReplaceResetAndClearCommitExpectedStacks()
    {
        var scopes = new List<RecordingScope>();
        var presenter = new RecordingNavigationPresenter();
        NavigationRegistry registry = BaseBuilder(requireContent: false)
            .AddPage<HomeViewModel>(HomeRoute, new ViewContract("page/home"), _ => NewContent<HomeViewModel>(scopes))
            .AddPage<DetailsViewModel>(DetailsRoute, new ViewContract("page/details"), _ => NewContent<DetailsViewModel>(scopes))
            .AddPage<SettingsViewModel>(SettingsRoute, new ViewContract("page/settings"), _ => NewContent<SettingsViewModel>(scopes))
            .Build();
        await using var service = new NavigationService(registry, presenter);

        await service.StartAsync();
        AssertRoutes(service.GetSnapshot(MainRegion), HomeRoute);
        NavigationResult push = await service.NavigateAsync<DetailsViewModel>(MainRegion);
        TestAssert.Equal(NavigationResultKind.Navigated, push.Kind);
        AssertRoutes(push.Snapshot, HomeRoute, DetailsRoute);

        NavigationResult replace = await service.NavigateAsync<SettingsViewModel>(
            MainRegion,
            new NavigationOptions { Mode = NavigationMode.Replace });
        AssertRoutes(replace.Snapshot, HomeRoute, SettingsRoute);
        TestAssert.Equal(1, scopes[1].DisposeCount);

        NavigationResult reset = await service.NavigateAsync<DetailsViewModel>(
            MainRegion,
            new NavigationOptions { Mode = NavigationMode.Reset });
        AssertRoutes(reset.Snapshot, DetailsRoute);
        TestAssert.Equal(1, scopes[0].DisposeCount);
        TestAssert.Equal(1, scopes[2].DisposeCount);

        NavigationResult clear = await service.ClearAsync(MainRegion);
        TestAssert.Equal(NavigationResultKind.Navigated, clear.Kind);
        TestAssert.Equal(0, clear.Snapshot.Entries.Count);
        TestAssert.Equal(1, scopes[3].DisposeCount);
        TestAssert.SequenceEqual(
            new List<int> { 1, 2, 2, 1, 0 },
            presenter.ResultingDepths);
    }

    public static async ValueTask BackRetainsOrRecreatesAccordingToRegistration()
    {
        int retainedActivations = 0;
        int recreatedActivations = 0;
        var presenter = new RecordingNavigationPresenter();
        NavigationRegistry registry = BaseBuilder(requireContent: false)
            .AddPage<HomeViewModel>(
                HomeRoute,
                new ViewContract("page/home"),
                _ =>
                {
                    retainedActivations++;
                    return NewContent<HomeViewModel>();
                })
            .AddPage<DetailsViewModel>(DetailsRoute, new ViewContract("page/details"), _ => NewContent<DetailsViewModel>())
            .AddPage<RecreatedViewModel>(
                SettingsRoute,
                new ViewContract("page/recreated"),
                _ =>
                {
                    recreatedActivations++;
                    return NewContent<RecreatedViewModel>();
                },
                NavigationRetention.RecreateOnBack)
            .Build();
        await using var service = new NavigationService(registry, presenter);
        await service.StartAsync();
        FlowSessionId retainedId = service.GetSnapshot(MainRegion).Current!.SessionId!.Value;

        _ = await service.NavigateAsync<DetailsViewModel>(MainRegion);
        _ = await service.BackAsync(MainRegion);
        TestAssert.Equal(retainedId, service.GetSnapshot(MainRegion).Current!.SessionId);
        TestAssert.Equal(1, retainedActivations);

        _ = await service.NavigateAsync<RecreatedViewModel>(MainRegion);
        FlowSessionId firstRecreatedId = service.GetSnapshot(MainRegion).Current!.SessionId!.Value;
        _ = await service.NavigateAsync<DetailsViewModel>(MainRegion);
        NavigationSnapshot stacked = service.GetSnapshot(MainRegion);
        TestAssert.Equal<FlowSessionId?>(null, stacked.Entries[^2].SessionId);
        _ = await service.BackAsync(MainRegion);
        FlowSessionId secondRecreatedId = service.GetSnapshot(MainRegion).Current!.SessionId!.Value;

        TestAssert.Equal(2, recreatedActivations);
        TestAssert.False(firstRecreatedId == secondRecreatedId);
    }

    public static async ValueTask GuardDenialDoesNotCreateOrPresentTarget()
    {
        int targetFactories = 0;
        var guarded = new GuardedNavigationViewModel(allow: false);
        var presenter = new RecordingNavigationPresenter();
        NavigationRegistry registry = BaseBuilder(requireContent: true)
            .AddPage<GuardedNavigationViewModel>(
                HomeRoute,
                new ViewContract("page/guarded"),
                _ => ValueTask.FromResult(new NavigationRouteContent(guarded, new RecordingScope())))
            .AddPage<DetailsViewModel>(
                DetailsRoute,
                new ViewContract("page/details"),
                _ =>
                {
                    targetFactories++;
                    return NewContent<DetailsViewModel>();
                })
            .Build();
        await using var service = new NavigationService(registry, presenter);
        await service.StartAsync();

        NavigationResult result = await service.NavigateAsync<DetailsViewModel>(MainRegion);

        TestAssert.Equal(NavigationResultKind.Rejected, result.Kind);
        TestAssert.Equal("denied", result.Reason);
        AssertRoutes(result.Snapshot, HomeRoute);
        TestAssert.Equal(0, targetFactories);
        TestAssert.Equal(1, presenter.PresentCount);
        TestAssert.Equal(1, guarded.GuardCalls);
    }

    public static async ValueTask PresenterFailureRollsBackAndDisposesTarget()
    {
        var failedScope = new RecordingScope();
        var presenter = new RecordingNavigationPresenter(failContract: new ViewContract("page/details"));
        NavigationRegistry registry = BaseBuilder(requireContent: false)
            .AddPage<HomeViewModel>(HomeRoute, new ViewContract("page/home"), _ => NewContent<HomeViewModel>())
            .AddPage<DetailsViewModel>(
                DetailsRoute,
                new ViewContract("page/details"),
                _ => ValueTask.FromResult(new NavigationRouteContent(new DetailsViewModel(), failedScope)))
            .Build();
        await using var service = new NavigationService(registry, presenter);
        await service.StartAsync();
        FlowSessionId homeSession = service.GetSnapshot(MainRegion).Current!.SessionId!.Value;

        _ = await TestAssert.ThrowsAsync<FlowPresenterException>(async () =>
            _ = await service.NavigateAsync<DetailsViewModel>(MainRegion));

        NavigationSnapshot snapshot = service.GetSnapshot(MainRegion);
        AssertRoutes(snapshot, HomeRoute);
        TestAssert.Equal(homeSession, snapshot.Current!.SessionId);
        TestAssert.Equal(1, failedScope.DisposeCount);
        TestAssert.True(service.IsCurrentSession(MainRegion, homeSession));
    }

    public static async ValueTask QueueIsFifoAndQueuedCancellationRemovesRequest()
    {
        var presenter = new BlockingNavigationPresenter(new ViewContract("page/details"));
        int cancelledFactories = 0;
        NavigationRegistry registry = BaseBuilder(requireContent: false)
            .AddPage<HomeViewModel>(HomeRoute, new ViewContract("page/home"), _ => NewContent<HomeViewModel>())
            .AddPage<DetailsViewModel>(DetailsRoute, new ViewContract("page/details"), _ => NewContent<DetailsViewModel>())
            .AddPage<SettingsViewModel>(
                SettingsRoute,
                new ViewContract("page/settings"),
                _ =>
                {
                    cancelledFactories++;
                    return NewContent<SettingsViewModel>();
                })
            .AddPage<FinalViewModel>(new RouteKey("final"), new ViewContract("page/final"), _ => NewContent<FinalViewModel>())
            .Build();
        await using var service = new NavigationService(registry, presenter);
        await service.StartAsync();

        Task<NavigationResult> first = service.NavigateAsync<DetailsViewModel>(MainRegion).AsTask();
        await presenter.Blocked.Task.ConfigureAwait(false);
        using var queuedCancellation = new CancellationTokenSource();
        Task<NavigationResult> cancelled = service.NavigateAsync<SettingsViewModel>(
            MainRegion,
            cancellationToken: queuedCancellation.Token).AsTask();
        Task<NavigationResult> last = service.NavigateAsync<FinalViewModel>(MainRegion).AsTask();
        queuedCancellation.Cancel();
        presenter.Release.TrySetResult();

        TestAssert.Equal(NavigationResultKind.Navigated, (await first.ConfigureAwait(false)).Kind);
        _ = await TestAssert.ThrowsAsync<OperationCanceledException>(async () =>
            _ = await cancelled.ConfigureAwait(false));
        TestAssert.Equal(NavigationResultKind.Navigated, (await last.ConfigureAwait(false)).Kind);
        TestAssert.Equal(0, cancelledFactories);
        AssertRoutes(service.GetSnapshot(MainRegion), HomeRoute, DetailsRoute, new RouteKey("final"));
    }

    public static async ValueTask RejectWhileBusyReturnsBusyWithoutFactory()
    {
        var presenter = new BlockingNavigationPresenter(new ViewContract("page/details"));
        int rejectedFactories = 0;
        NavigationRegistry registry = new NavigationRegistryBuilder()
            .AddRegion(new NavigationRegionRegistration(
                MainRegion,
                HomeRoute,
                concurrency: NavigationConcurrency.RejectWhileBusy))
            .AddPage<HomeViewModel>(HomeRoute, new ViewContract("page/home"), _ => NewContent<HomeViewModel>())
            .AddPage<DetailsViewModel>(DetailsRoute, new ViewContract("page/details"), _ => NewContent<DetailsViewModel>())
            .AddPage<SettingsViewModel>(
                SettingsRoute,
                new ViewContract("page/settings"),
                _ =>
                {
                    rejectedFactories++;
                    return NewContent<SettingsViewModel>();
                })
            .Build();
        await using var service = new NavigationService(registry, presenter);
        await service.StartAsync();

        Task<NavigationResult> active = service.NavigateAsync<DetailsViewModel>(MainRegion).AsTask();
        await presenter.Blocked.Task.ConfigureAwait(false);
        NavigationResult rejected = await service.NavigateAsync<SettingsViewModel>(MainRegion);

        TestAssert.Equal(NavigationResultKind.Busy, rejected.Kind);
        TestAssert.Equal(0, rejectedFactories);
        presenter.Release.TrySetResult();
        _ = await active.ConfigureAwait(false);
    }

    public static async ValueTask ReentrantGuardMutationThrowsAndPreservesSnapshot()
    {
        var reentrant = new ReentrantGuardViewModel();
        var presenter = new RecordingNavigationPresenter();
        NavigationRegistry registry = BaseBuilder(requireContent: true)
            .AddPage<ReentrantGuardViewModel>(
                HomeRoute,
                new ViewContract("page/reentrant"),
                _ => ValueTask.FromResult(new NavigationRouteContent(reentrant, new RecordingScope())))
            .AddPage<DetailsViewModel>(DetailsRoute, new ViewContract("page/details"), _ => NewContent<DetailsViewModel>())
            .Build();
        await using var service = new NavigationService(registry, presenter);
        reentrant.Service = service;
        await service.StartAsync();

        _ = await TestAssert.ThrowsAsync<FlowReentrancyException>(async () =>
            _ = await service.NavigateAsync<DetailsViewModel>(MainRegion));
        AssertRoutes(service.GetSnapshot(MainRegion), HomeRoute);
        TestAssert.Equal(1, presenter.PresentCount);
    }

    public static async ValueTask StaleEventsAndShutdownCannotMutateState()
    {
        var guarded = new GuardedNavigationViewModel(allow: false);
        var presenter = new RecordingNavigationPresenter();
        NavigationRegistry registry = BaseBuilder(requireContent: true)
            .AddPage<HomeViewModel>(HomeRoute, new ViewContract("page/home"), _ => NewContent<HomeViewModel>())
            .AddPage<GuardedNavigationViewModel>(
                DetailsRoute,
                new ViewContract("page/guarded"),
                _ => ValueTask.FromResult(new NavigationRouteContent(guarded, new RecordingScope())))
            .Build();
        await using var service = new NavigationService(registry, presenter);
        await service.StartAsync();
        FlowSessionId stale = service.GetSnapshot(MainRegion).Current!.SessionId!.Value;
        _ = await service.NavigateAsync<GuardedNavigationViewModel>(MainRegion);

        NavigationResult staleResult = await service.BackAsync(MainRegion, stale);
        TestAssert.Equal(NavigationResultKind.Stale, staleResult.Kind);
        TestAssert.False(service.IsCurrentSession(MainRegion, stale));
        TestAssert.Equal(0, guarded.GuardCalls);

        await service.ShutdownAsync();
        TestAssert.Equal(0, service.GetSnapshot(MainRegion).Entries.Count);
        TestAssert.Equal(0, guarded.GuardCalls);
        NavigationResult afterShutdown = await service.NavigateAsync<HomeViewModel>(MainRegion);
        TestAssert.Equal(NavigationResultKind.ShuttingDown, afterShutdown.Kind);
    }

    public static async ValueTask HungLeaseCloseIsBoundedByManualClock()
    {
        TimeSpan timeout = TimeSpan.FromMinutes(2);
        var clock = new ManualTimeProvider(new DateTimeOffset(2040, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var presenter = new HangingNavigationPresenter(new ViewContract("page/home"));
        var homeScope = new RecordingScope();
        NavigationRegistry registry = BaseBuilder(requireContent: false)
            .AddPage<HomeViewModel>(
                HomeRoute,
                new ViewContract("page/home"),
                _ => ValueTask.FromResult(new NavigationRouteContent(new HomeViewModel(), homeScope)))
            .AddPage<DetailsViewModel>(DetailsRoute, new ViewContract("page/details"), _ => NewContent<DetailsViewModel>())
            .Build();
        await using var service = new NavigationService(registry, presenter, clock, timeout);
        await service.StartAsync();

        Task<NavigationResult> pending = service.NavigateAsync<DetailsViewModel>(MainRegion).AsTask();
        await presenter.HungLease!.CloseEntered.Task.ConfigureAwait(false);
        TestAssert.False(pending.IsCompleted);
        clock.Advance(timeout);

        FlowCleanupException exception = await TestAssert.ThrowsAsync<FlowCleanupException>(async () =>
            _ = await pending.ConfigureAwait(false));
        TestAssert.True(ContainsTimeout(exception.Failures));
        TestAssert.Equal(0, presenter.HungLease!.DisposeCount);
        TestAssert.Equal(0, homeScope.DisposeCount);
        TestAssert.Equal(DetailsRoute, service.GetSnapshot(MainRegion).Current!.Route);
    }

    private static NavigationRegistryBuilder BaseBuilder(bool requireContent) =>
        new NavigationRegistryBuilder().AddRegion(
            new NavigationRegionRegistration(MainRegion, HomeRoute, requireContent));

    private static ValueTask<NavigationRouteContent> NewContent<TViewModel>(
        List<RecordingScope>? scopes = null)
        where TViewModel : class, new()
    {
        var scope = new RecordingScope();
        scopes?.Add(scope);
        return ValueTask.FromResult(new NavigationRouteContent(new TViewModel(), scope));
    }

    private static void AssertRoutes(NavigationSnapshot snapshot, params RouteKey[] expected)
    {
        var actual = new List<RouteKey>(snapshot.Entries.Count);
        foreach (NavigationEntrySnapshot entry in snapshot.Entries)
        {
            actual.Add(entry.Route);
        }

        TestAssert.SequenceEqual(expected, actual);
    }

    private static bool ContainsTimeout(IReadOnlyList<Exception> failures)
    {
        foreach (Exception failure in failures)
        {
            if (failure is TimeoutException)
            {
                return true;
            }
        }

        return false;
    }

    private sealed class HomeViewModel;
    private sealed class DetailsViewModel;
    private sealed class SettingsViewModel;
    private sealed class RecreatedViewModel;
    private sealed class FinalViewModel;

    private sealed class GuardedNavigationViewModel(bool allow) : INavigationGuard
    {
        public int GuardCalls { get; private set; }

        public ValueTask<NavigationGuardResult> CanLeaveAsync(
            NavigationGuardContext context,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            GuardCalls++;
            return ValueTask.FromResult(
                allow ? NavigationGuardResult.Allow() : NavigationGuardResult.Deny("denied"));
        }
    }

    private sealed class ReentrantGuardViewModel : INavigationGuard
    {
        public NavigationService? Service { get; set; }

        public async ValueTask<NavigationGuardResult> CanLeaveAsync(
            NavigationGuardContext context,
            CancellationToken cancellationToken)
        {
            NavigationService service = Service ??
                throw new InvalidOperationException("The test service was not assigned.");
            _ = await service.NavigateAsync<DetailsViewModel>(MainRegion, cancellationToken: cancellationToken);
            return NavigationGuardResult.Allow();
        }
    }

    private sealed class RecordingScope : IAsyncDisposable
    {
        private int _disposeCount;
        public int DisposeCount => Volatile.Read(ref _disposeCount);

        public ValueTask DisposeAsync()
        {
            Interlocked.Increment(ref _disposeCount);
            return ValueTask.CompletedTask;
        }
    }

    private class RecordingNavigationPresenter(ViewContract? failContract = null)
        : INavigationRegionPresenter
    {
        private readonly List<int> _resultingDepths = [];

        public int PresentCount { get; private set; }
        public List<int> ResultingDepths => _resultingDepths;

        public virtual ValueTask<IFlowPresentationLease> PresentAsync(
            RegionKey region,
            FlowContentDescriptor content,
            NavigationPresentationContext context,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            PresentCount++;
            if (content.Contract == failContract)
            {
                return ValueTask.FromException<IFlowPresentationLease>(
                    new InvalidOperationException("present failed"));
            }

            _resultingDepths.Add(context.ResultingDepth);
            return ValueTask.FromResult<IFlowPresentationLease>(new EmptyLease());
        }

        public ValueTask ClearAsync(RegionKey region, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _resultingDepths.Add(0);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class BlockingNavigationPresenter(ViewContract blockedContract)
        : RecordingNavigationPresenter
    {
        public TaskCompletionSource Blocked { get; } = NewSignal();
        public TaskCompletionSource Release { get; } = NewSignal();

        public override async ValueTask<IFlowPresentationLease> PresentAsync(
            RegionKey region,
            FlowContentDescriptor content,
            NavigationPresentationContext context,
            CancellationToken cancellationToken)
        {
            if (content.Contract == blockedContract)
            {
                Blocked.TrySetResult();
                await Release.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            }

            return await base.PresentAsync(region, content, context, cancellationToken).ConfigureAwait(false);
        }
    }

    private sealed class HangingNavigationPresenter(ViewContract hungContract)
        : RecordingNavigationPresenter
    {
        public HangingCloseLease? HungLease { get; private set; }

        public override ValueTask<IFlowPresentationLease> PresentAsync(
            RegionKey region,
            FlowContentDescriptor content,
            NavigationPresentationContext context,
            CancellationToken cancellationToken)
        {
            if (content.Contract == hungContract)
            {
                HungLease = new HangingCloseLease();
                return ValueTask.FromResult<IFlowPresentationLease>(HungLease);
            }

            return base.PresentAsync(region, content, context, cancellationToken);
        }
    }

    private sealed class HangingCloseLease : IFlowPresentationLease
    {
        private readonly TaskCompletionSource _never = NewSignal();
        public TaskCompletionSource CloseEntered { get; } = NewSignal();
        public int DisposeCount { get; private set; }

        public ValueTask CloseAsync(CancellationToken cancellationToken)
        {
            CloseEntered.TrySetResult();
            return new ValueTask(_never.Task);
        }

        public ValueTask DisposeAsync()
        {
            DisposeCount++;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class EmptyLease : IFlowPresentationLease
    {
        public ValueTask CloseAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private static TaskCompletionSource NewSignal() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);
}
