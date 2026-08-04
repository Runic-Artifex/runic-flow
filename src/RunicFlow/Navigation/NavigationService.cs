using System;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using RunicFlow;

namespace RunicFlow.Navigation;

/// <summary>
/// Provides a BCL-first transactional navigation engine over an immutable registry.
/// </summary>
public sealed class NavigationService : INavigationService, IAsyncDisposable
{
    private static readonly TimeSpan MaximumTeardownTimeout = TimeSpan.FromMilliseconds(4_294_967_294d);
    private readonly NavigationRegistry _registry;
    private readonly INavigationRegionPresenter _presenter;
    private readonly Dictionary<RegionKey, RegionState> _regions = [];
    private readonly List<RegionState> _regionOrder = [];
    private readonly AsyncLocal<MutationContext?> _activeMutation = new();
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _teardownTimeout;
    private readonly Lazy<Task> _startOperation;
    private readonly Lazy<Task> _shutdownOperation;
    private int _shutdownStarted;

    /// <summary>Initializes one scoped logical navigation service.</summary>
    public NavigationService(NavigationRegistry registry, INavigationRegionPresenter presenter)
        : this(registry, presenter, TimeProvider.System, DefaultTeardownTimeout)
    {
    }

    /// <summary>Initializes navigation with a deterministic clock and the default teardown timeout.</summary>
    public NavigationService(
        NavigationRegistry registry,
        INavigationRegionPresenter presenter,
        TimeProvider timeProvider)
        : this(registry, presenter, timeProvider, DefaultTeardownTimeout)
    {
    }

    /// <summary>Initializes navigation with the system clock and a custom teardown timeout.</summary>
    public NavigationService(
        NavigationRegistry registry,
        INavigationRegionPresenter presenter,
        TimeSpan teardownTimeout)
        : this(registry, presenter, TimeProvider.System, teardownTimeout)
    {
    }

    /// <summary>Initializes navigation with a deterministic clock and bounded teardown policy.</summary>
    public NavigationService(
        NavigationRegistry registry,
        INavigationRegionPresenter presenter,
        TimeProvider? timeProvider,
        TimeSpan? teardownTimeout)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(presenter);
        TimeSpan validatedTimeout = teardownTimeout ?? DefaultTeardownTimeout;
        if (validatedTimeout <= TimeSpan.Zero || validatedTimeout > MaximumTeardownTimeout)
        {
            throw new ArgumentOutOfRangeException(
                nameof(teardownTimeout),
                teardownTimeout,
                "The navigation teardown timeout must be positive and supported by TimeProvider timers.");
        }

        _registry = registry;
        _presenter = presenter;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _teardownTimeout = validatedTimeout;

        foreach (NavigationRegionRegistration registration in registry.Regions)
        {
            RegionState state = new(registration);
            _regions.Add(registration.Key, state);
            _regionOrder.Add(state);
        }

        _startOperation = new Lazy<Task>(StartCoreAsync, LazyThreadSafetyMode.ExecutionAndPublication);
        _shutdownOperation = new Lazy<Task>(ShutdownCoreAsync, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    /// <summary>Gets the default maximum duration of each teardown operation.</summary>
    public static TimeSpan DefaultTeardownTimeout { get; } = TimeSpan.FromSeconds(30);

    /// <summary>Gets the maximum duration of each lease or content-session teardown operation.</summary>
    public TimeSpan TeardownTimeout => _teardownTimeout;

    /// <inheritdoc />
    public event EventHandler<NavigationSnapshotChangedEventArgs>? SnapshotChanged;

    /// <inheritdoc />
    public ValueTask StartAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _shutdownStarted) != 0, this);
        if (HasActiveMutation())
        {
            throw new FlowReentrancyException(
                "Navigation startup cannot be awaited from inside a navigation lifecycle callback.",
                FlowFeature.Navigation);
        }

        return new ValueTask(_startOperation.Value.WaitAsync(cancellationToken));
    }

    /// <inheritdoc />
    public ValueTask<NavigationResult> NavigateAsync<TViewModel>(
        RegionKey region,
        NavigationOptions? options = null,
        CancellationToken cancellationToken = default)
        where TViewModel : class =>
        NavigateRouteCoreAsync(
            GetRegion(region),
            _registry.GetRoute(typeof(TViewModel), parameterType: null),
            parameter: null,
            options ?? new NavigationOptions(),
            cancellationToken,
            expectedCurrentSession: null);

    /// <inheritdoc />
    public ValueTask<NavigationResult> NavigateRouteAsync(
        RegionKey region,
        FlowSessionId expectedCurrentSession,
        RouteKey route,
        object? parameter = null,
        NavigationOptions? options = null,
        CancellationToken cancellationToken = default) =>
        NavigateRouteCoreAsync(
            GetRegion(region),
            _registry.GetRoute(route),
            parameter,
            options ?? new NavigationOptions(),
            cancellationToken,
            expectedCurrentSession);

    /// <inheritdoc />
    public ValueTask<NavigationResult> NavigateAsync<TViewModel, TParameter>(
        RegionKey region,
        TParameter parameter,
        NavigationOptions? options = null,
        CancellationToken cancellationToken = default)
        where TViewModel : class =>
        NavigateRouteCoreAsync(
            GetRegion(region),
            _registry.GetRoute(typeof(TViewModel), typeof(TParameter)),
            parameter,
            options ?? new NavigationOptions(),
            cancellationToken);

    /// <inheritdoc />
    public ValueTask<NavigationResult> NavigateRouteAsync(
        RegionKey region,
        RouteKey route,
        object? parameter = null,
        NavigationOptions? options = null,
        CancellationToken cancellationToken = default) =>
        NavigateRouteCoreAsync(
            GetRegion(region),
            _registry.GetRoute(route),
            parameter,
            options ?? new NavigationOptions(),
            cancellationToken);

    /// <inheritdoc />
    public ValueTask<NavigationResult> BackAsync(
        RegionKey region,
        CancellationToken cancellationToken = default) =>
        BackCoreAsync(GetRegion(region), expectedCurrentSession: null, cancellationToken);

    /// <inheritdoc />
    public ValueTask<NavigationResult> BackAsync(
        RegionKey region,
        FlowSessionId expectedCurrentSession,
        CancellationToken cancellationToken = default) =>
        BackCoreAsync(GetRegion(region), expectedCurrentSession, cancellationToken);

    /// <inheritdoc />
    public ValueTask<NavigationResult> ClearAsync(
        RegionKey region,
        CancellationToken cancellationToken = default) =>
        ClearCoreAsync(GetRegion(region), expectedCurrentSession: null, cancellationToken);

    /// <inheritdoc />
    public ValueTask<NavigationResult> ClearAsync(
        RegionKey region,
        FlowSessionId expectedCurrentSession,
        CancellationToken cancellationToken = default) =>
        ClearCoreAsync(GetRegion(region), expectedCurrentSession, cancellationToken);

    /// <inheritdoc />
    public NavigationSnapshot GetSnapshot(RegionKey region) => GetRegion(region).Snapshot;

    /// <inheritdoc />
    public bool IsCurrentSession(RegionKey region, FlowSessionId sessionId)
    {
        NavigationEntrySnapshot? current = GetRegion(region).Snapshot.Current;
        return current?.SessionId == sessionId;
    }

    /// <inheritdoc />
    public async ValueTask<NavigationGuardResult> CanLeaveAsync(
        RegionKey region,
        CancellationToken cancellationToken = default)
    {
        RegionState state = GetRegion(region);
        NavigationResult result = await RunMutationAsync(
            state,
            async () =>
            {
                NavigationEntry? current =
                    state.Stack.Count == 0 ? null : state.Stack[^1];
                NavigationResult? rejection = await CheckGuardAsync(
                    state,
                    current,
                    targetRoute: null,
                    mode: null,
                    cancellationToken).ConfigureAwait(false);
                return rejection ??
                    new NavigationResult(NavigationResultKind.NoOp, state.Snapshot);
            },
            cancellationToken).ConfigureAwait(false);

        return result.Kind switch
        {
            NavigationResultKind.Rejected =>
                NavigationGuardResult.Deny(result.Reason ?? "Navigation close was denied."),
            NavigationResultKind.Busy =>
                NavigationGuardResult.Deny("Navigation is busy."),
            _ => NavigationGuardResult.Allow(),
        };
    }

    /// <inheritdoc />
    public ValueTask ShutdownAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfShutdownReentrant();
        return new ValueTask(_shutdownOperation.Value.WaitAsync(cancellationToken));
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        ThrowIfShutdownReentrant();
        return new ValueTask(_shutdownOperation.Value);
    }

    private async ValueTask<NavigationResult> NavigateRouteCoreAsync(
        RegionState state,
        NavigationRouteRegistration route,
        object? parameter,
        NavigationOptions options,
        CancellationToken cancellationToken,
        FlowSessionId? expectedCurrentSession = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (!route.Accepts(parameter))
        {
            throw new ArgumentException(
                $"Parameter supplied for route '{route.Route}' is not assignable to its registered parameter type.",
                nameof(parameter));
        }

        return await RunMutationAsync(
            state,
            async () =>
            {
                if (!MatchesExpectedSession(state, expectedCurrentSession))
                {
                    return new NavigationResult(NavigationResultKind.Stale, state.Snapshot);
                }

                NavigationEntry? old = state.Stack.Count == 0 ? null : state.Stack[^1];
                NavigationResult? rejection = await CheckGuardAsync(
                    state,
                    old,
                    route.Route,
                    options.Mode,
                    cancellationToken).ConfigureAwait(false);
                if (rejection is not null)
                {
                    return rejection;
                }

                NavigationEntry target = await CreateEntryAsync(route, parameter, cancellationToken)
                    .ConfigureAwait(false);
                try
                {
                    await InvokeActivatingAsync(target, cancellationToken).ConfigureAwait(false);
                    int resultingDepth = options.Mode switch
                    {
                        NavigationMode.Push => state.Stack.Count + 1,
                        NavigationMode.Replace => Math.Max(1, state.Stack.Count),
                        NavigationMode.Reset => 1,
                        _ => throw new ArgumentOutOfRangeException(nameof(options)),
                    };
                    target.Lease = await PresentAsync(
                        state,
                        target,
                        new NavigationPresentationContext(
                            options.Mode,
                            old?.Session?.Descriptor.SessionId,
                            resultingDepth,
                            isBackNavigation: false),
                        cancellationToken).ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    await DisposeAfterPreCommitFailureAsync(
                        target,
                        exception,
                        state.Registration.Key.Value).ConfigureAwait(false);
                    throw;
                }

                NavigationSnapshot previous = state.Snapshot;
                List<NavigationEntry> removed = [];
                switch (options.Mode)
                {
                    case NavigationMode.Push:
                        state.Stack.Add(target);
                        break;
                    case NavigationMode.Replace:
                        if (state.Stack.Count != 0)
                        {
                            removed.Add(state.Stack[^1]);
                            state.Stack[^1] = target;
                        }
                        else
                        {
                            state.Stack.Add(target);
                        }

                        break;
                    case NavigationMode.Reset:
                        removed.AddRange(state.Stack);
                        state.Stack.Clear();
                        state.Stack.Add(target);
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(options));
                }

                NavigationSnapshot current = CommitSnapshot(state);
                List<Exception> lifecycleFailures = [];
                PublishSnapshot(previous, current, lifecycleFailures);
                if (old is not null)
                {
                    await InvokeDeactivationAsync(old, lifecycleFailures).ConfigureAwait(false);
                }

                await InvokeActivatedAsync(target, lifecycleFailures).ConfigureAwait(false);

                List<Exception> cleanupFailures = [];
                if (options.Mode == NavigationMode.Push && old is not null)
                {
                    await ReleaseForBackStackAsync(old, cleanupFailures).ConfigureAwait(false);
                }
                else
                {
                    for (int index = removed.Count - 1; index >= 0; index--)
                    {
                        await DisposeEntryAsync(removed[index], cleanupFailures).ConfigureAwait(false);
                    }
                }

                ThrowPostCommitFailures(state, target, lifecycleFailures, cleanupFailures);
                return new NavigationResult(NavigationResultKind.Navigated, current);
            },
            cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask<NavigationResult> BackCoreAsync(
        RegionState state,
        FlowSessionId? expectedCurrentSession,
        CancellationToken cancellationToken) =>
        await RunMutationAsync(
            state,
            async () =>
            {
                if (!MatchesExpectedSession(state, expectedCurrentSession))
                {
                    return new NavigationResult(NavigationResultKind.Stale, state.Snapshot);
                }

                if (state.Stack.Count <= 1)
                {
                    return new NavigationResult(NavigationResultKind.NoOp, state.Snapshot);
                }

                NavigationEntry old = state.Stack[^1];
                NavigationEntry target = state.Stack[^2];
                NavigationResult? rejection = await CheckGuardAsync(
                    state,
                    old,
                    target.Route.Route,
                    mode: null,
                    cancellationToken).ConfigureAwait(false);
                if (rejection is not null)
                {
                    return rejection;
                }

                bool created = target.Session is null;
                if (created)
                {
                    target.Session = (await CreateEntryAsync(target.Route, target.Parameter, cancellationToken)
                        .ConfigureAwait(false)).Session;
                }

                try
                {
                    await InvokeActivatingAsync(target, cancellationToken).ConfigureAwait(false);
                    target.Lease = await PresentAsync(
                        state,
                        target,
                        new NavigationPresentationContext(
                            NavigationMode.Replace,
                            old.Session?.Descriptor.SessionId,
                            state.Stack.Count - 1,
                            isBackNavigation: true),
                        cancellationToken).ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    if (created)
                    {
                        await DisposeAfterPreCommitFailureAsync(
                            target,
                            exception,
                            state.Registration.Key.Value).ConfigureAwait(false);
                    }

                    throw;
                }

                NavigationSnapshot previous = state.Snapshot;
                state.Stack.RemoveAt(state.Stack.Count - 1);
                NavigationSnapshot current = CommitSnapshot(state);
                List<Exception> lifecycleFailures = [];
                PublishSnapshot(previous, current, lifecycleFailures);
                await InvokeDeactivationAsync(old, lifecycleFailures).ConfigureAwait(false);
                await InvokeActivatedAsync(target, lifecycleFailures).ConfigureAwait(false);

                List<Exception> cleanupFailures = [];
                await DisposeEntryAsync(old, cleanupFailures).ConfigureAwait(false);
                ThrowPostCommitFailures(state, target, lifecycleFailures, cleanupFailures);
                return new NavigationResult(NavigationResultKind.Navigated, current);
            },
            cancellationToken).ConfigureAwait(false);

    private async ValueTask<NavigationResult> ClearCoreAsync(
        RegionState state,
        FlowSessionId? expectedCurrentSession,
        CancellationToken cancellationToken) =>
        await RunMutationAsync(
            state,
            async () =>
            {
                if (!MatchesExpectedSession(state, expectedCurrentSession))
                {
                    return new NavigationResult(NavigationResultKind.Stale, state.Snapshot);
                }

                if (state.Registration.RequireContent)
                {
                    return new NavigationResult(
                        NavigationResultKind.Rejected,
                        state.Snapshot,
                        "The region requires content.");
                }

                if (state.Stack.Count == 0)
                {
                    return new NavigationResult(NavigationResultKind.NoOp, state.Snapshot);
                }

                NavigationEntry old = state.Stack[^1];
                NavigationResult? rejection = await CheckGuardAsync(
                    state,
                    old,
                    targetRoute: null,
                    mode: null,
                    cancellationToken).ConfigureAwait(false);
                if (rejection is not null)
                {
                    return rejection;
                }

                await ClearPresenterAsync(
                    state,
                    old,
                    preserveCancellation: true,
                    cancellationToken).ConfigureAwait(false);

                NavigationSnapshot previous = state.Snapshot;
                List<NavigationEntry> removed = [.. state.Stack];
                state.Stack.Clear();
                NavigationSnapshot current = CommitSnapshot(state);
                List<Exception> lifecycleFailures = [];
                PublishSnapshot(previous, current, lifecycleFailures);
                await InvokeDeactivationAsync(old, lifecycleFailures).ConfigureAwait(false);

                List<Exception> cleanupFailures = [];
                for (int index = removed.Count - 1; index >= 0; index--)
                {
                    await DisposeEntryAsync(removed[index], cleanupFailures).ConfigureAwait(false);
                }

                ThrowPostCommitFailures(state, old, lifecycleFailures, cleanupFailures);
                return new NavigationResult(NavigationResultKind.Navigated, current);
            },
            cancellationToken).ConfigureAwait(false);

    private async ValueTask<NavigationResult> RunMutationAsync(
        RegionState state,
        Func<ValueTask<NavigationResult>> mutation,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (IsActiveMutation(state))
        {
            throw new FlowReentrancyException(
                $"Navigation region '{state.Registration.Key}' cannot be mutated re-entrantly.",
                FlowFeature.Navigation,
                state.Registration.Key.Value,
                state.Snapshot.Current?.SessionId);
        }

        if (Volatile.Read(ref state.ShuttingDown) != 0 || Volatile.Read(ref _shutdownStarted) != 0)
        {
            return new NavigationResult(NavigationResultKind.ShuttingDown, state.Snapshot);
        }

        RegionGate.Lease? lease = await state.Gate.AcquireAsync(
            state.Registration.Concurrency == NavigationConcurrency.RejectWhileBusy,
            cancellationToken).ConfigureAwait(false);
        if (lease is null)
        {
            return new NavigationResult(NavigationResultKind.Busy, state.Snapshot);
        }

        await using (lease.ConfigureAwait(false))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Volatile.Read(ref state.ShuttingDown) != 0 || Volatile.Read(ref _shutdownStarted) != 0)
            {
                return new NavigationResult(NavigationResultKind.ShuttingDown, state.Snapshot);
            }

            MutationContext? previous = _activeMutation.Value;
            MutationContext current = new(state, previous);
            _activeMutation.Value = current;
            try
            {
                return await mutation().ConfigureAwait(false);
            }
            finally
            {
                current.IsActive = false;
                _activeMutation.Value = previous;
            }
        }
    }

    private async Task ShutdownCoreAsync()
    {
        if (Interlocked.Exchange(ref _shutdownStarted, 1) != 0)
        {
            return;
        }

        foreach (RegionState state in _regionOrder)
        {
            Volatile.Write(ref state.ShuttingDown, 1);
            state.Gate.CancelQueued();
        }

        List<Exception> failures = [];
        for (int regionIndex = _regionOrder.Count - 1; regionIndex >= 0; regionIndex--)
        {
            RegionState state = _regionOrder[regionIndex];
            RegionGate.Lease lease;
            using (FlowTimeoutCancellation gateTimeout = FlowTimeout.CreateCancellationSource(
                _timeProvider,
                _teardownTimeout))
            {
                try
                {
                    lease = await state.Gate.AcquireForShutdownAsync(gateTimeout.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (gateTimeout.IsTimeoutCancellationRequested)
                {
                    failures.Add(new TimeoutException(
                        $"Navigation shutdown timed out waiting for region '{state.Registration.Key}' to finish its active mutation."));
                    continue;
                }
            }

            await using (lease.ConfigureAwait(false))
            {
                if (state.Stack.Count == 0)
                {
                    continue;
                }

                NavigationEntry old = state.Stack[^1];
                try
                {
                    await ClearPresenterAsync(
                        state,
                        old,
                        preserveCancellation: false,
                        CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    failures.Add(exception);
                    if (IsTimeoutFailure(exception))
                    {
                        continue;
                    }
                }

                NavigationSnapshot previous = state.Snapshot;
                List<NavigationEntry> removed = [.. state.Stack];
                state.Stack.Clear();
                NavigationSnapshot current = CommitSnapshot(state);
                PublishSnapshot(previous, current, failures);
                await InvokeDeactivationAsync(old, failures).ConfigureAwait(false);
                for (int index = removed.Count - 1; index >= 0; index--)
                {
                    await DisposeEntryAsync(removed[index], failures).ConfigureAwait(false);
                }
            }
        }

        if (failures.Count != 0)
        {
            throw new FlowCleanupException(
                "Navigation shutdown encountered one or more ordered failures.",
                FlowFeature.Navigation,
                failures);
        }
    }

    private async Task StartCoreAsync()
    {
        foreach (RegionState state in _regionOrder)
        {
            if (state.Registration.StartRoute is RouteKey startRoute)
            {
                NavigationResult result = await NavigateRouteCoreAsync(
                    state,
                    _registry.GetRoute(startRoute),
                    parameter: null,
                    new NavigationOptions { Mode = NavigationMode.Reset },
                    CancellationToken.None).ConfigureAwait(false);
                if (result.Kind == NavigationResultKind.ShuttingDown)
                {
                    throw new ObjectDisposedException(
                        nameof(NavigationService),
                        "Navigation shutdown began before startup completed.");
                }
            }
        }
    }

    private async ValueTask<NavigationEntry> CreateEntryAsync(
        NavigationRouteRegistration route,
        object? parameter,
        CancellationToken cancellationToken)
    {
        NavigationRouteContent content = await route.Factory(parameter, cancellationToken).ConfigureAwait(false);
        FlowSessionId sessionId = FlowSessionId.Create();
        FlowContentSession session;
        try
        {
            session = new FlowContentSession(
                sessionId,
                route.Contract,
                content.ViewModel,
                route.ViewModelType,
                content.Metadata,
                content.OwnedScope,
                content.OwnsViewModel);
        }
        catch (Exception exception)
        {
            await DisposeFactoryContentAfterFailureAsync(
                content,
                exception,
                route.Route.Value).ConfigureAwait(false);
            throw;
        }

        NavigationEntry entry = new(route, parameter, session);
        try
        {
            await route.Initializer(content.ViewModel, parameter, cancellationToken).ConfigureAwait(false);
            return entry;
        }
        catch (Exception exception)
        {
            await DisposeAfterPreCommitFailureAsync(
                entry,
                exception,
                route.Route.Value).ConfigureAwait(false);
            throw;
        }
    }

    private async ValueTask<IFlowPresentationLease> PresentAsync(
        RegionState state,
        NavigationEntry target,
        NavigationPresentationContext context,
        CancellationToken cancellationToken)
    {
        try
        {
            IFlowPresentationLease lease = await _presenter.PresentAsync(
                state.Registration.Key,
                target.Session!.Descriptor,
                context,
                cancellationToken).ConfigureAwait(false);
            return lease ?? throw new InvalidOperationException("A navigation presenter returned a null lease.");
        }
        catch (Exception exception) when (
            exception is not FlowPresenterException and not OperationCanceledException)
        {
            throw new FlowPresenterException(
                $"The presenter failed to display route '{target.Route.Route}'.",
                FlowFeature.Navigation,
                FlowLifecycleStage.Presenting,
                target.Route.Route.Value,
                target.Session?.Descriptor.SessionId,
                exception);
        }
    }

    private async ValueTask ClearPresenterAsync(
        RegionState state,
        NavigationEntry old,
        bool preserveCancellation,
        CancellationToken cancellationToken)
    {
        List<Exception> presenterFailures = [];
        await TryTeardownAsync(
            () => _presenter.ClearAsync(state.Registration.Key, cancellationToken),
            $"presenter clear for region '{state.Registration.Key}'",
            presenterFailures).ConfigureAwait(false);
        if (presenterFailures.Count == 0)
        {
            return;
        }

        if (preserveCancellation &&
            presenterFailures.Count == 1 &&
            presenterFailures[0] is OperationCanceledException cancellationException)
        {
            ExceptionDispatchInfo.Throw(cancellationException);
        }

        Exception innerException = presenterFailures.Count == 1
            ? presenterFailures[0]
            : new AggregateException(presenterFailures);
        throw new FlowPresenterException(
            $"The presenter failed to clear navigation region '{state.Registration.Key}'.",
            FlowFeature.Navigation,
            FlowLifecycleStage.Closing,
            state.Registration.Key.Value,
            old.Session?.Descriptor.SessionId,
            innerException);
    }

    private static async ValueTask<NavigationResult?> CheckGuardAsync(
        RegionState state,
        NavigationEntry? old,
        RouteKey? targetRoute,
        NavigationMode? mode,
        CancellationToken cancellationToken)
    {
        if (old?.Session?.Descriptor.ViewModel is not INavigationGuard guard)
        {
            return null;
        }

        NavigationGuardResult result = await guard.CanLeaveAsync(
            new NavigationGuardContext(
                state.Registration.Key,
                old.Route.Route,
                old.Session.Descriptor.SessionId,
                targetRoute,
                mode),
            cancellationToken).ConfigureAwait(false);
        return result.IsAllowed
            ? null
            : new NavigationResult(NavigationResultKind.Rejected, state.Snapshot, result.Reason);
    }

    private static async ValueTask InvokeActivatingAsync(
        NavigationEntry entry,
        CancellationToken cancellationToken)
    {
        FlowContentSession session = entry.Session!;
        if (session.Descriptor.ViewModel is IFlowActivation activation)
        {
            await activation.ActivatingAsync(
                new FlowActivationContext(session.Descriptor.SessionId, session.Descriptor.Contract),
                cancellationToken).ConfigureAwait(false);
        }
    }

    private static async ValueTask InvokeActivatedAsync(
        NavigationEntry entry,
        List<Exception> failures)
    {
        FlowContentSession session = entry.Session!;
        if (session.Descriptor.ViewModel is not IFlowActivation activation)
        {
            return;
        }

        try
        {
            await activation.ActivatedAsync(
                new FlowActivationContext(session.Descriptor.SessionId, session.Descriptor.Contract),
                CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            failures.Add(exception);
        }
    }

    private static async ValueTask InvokeDeactivationAsync(
        NavigationEntry entry,
        List<Exception> failures)
    {
        FlowContentSession? session = entry.Session;
        if (session?.Descriptor.ViewModel is not IFlowActivation activation)
        {
            return;
        }

        FlowDeactivationContext context = new(session.Descriptor.SessionId, session.Descriptor.Contract);
        try
        {
            await activation.DeactivatingAsync(context, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            failures.Add(exception);
        }

        try
        {
            await activation.DeactivatedAsync(context, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            failures.Add(exception);
        }
    }

    private async ValueTask ReleaseForBackStackAsync(
        NavigationEntry entry,
        List<Exception> failures)
    {
        bool leaseFinished = await DisposeLeaseAsync(entry, failures).ConfigureAwait(false);
        if (!leaseFinished)
        {
            entry.Session = null;
            return;
        }

        if (entry.Route.Retention == NavigationRetention.RecreateOnBack)
        {
            await DisposeSessionAsync(entry, failures).ConfigureAwait(false);
        }
    }

    private async ValueTask DisposeEntryAsync(
        NavigationEntry entry,
        List<Exception> failures)
    {
        bool leaseFinished = await DisposeLeaseAsync(entry, failures).ConfigureAwait(false);
        if (!leaseFinished)
        {
            entry.Session = null;
            return;
        }

        await DisposeSessionAsync(entry, failures).ConfigureAwait(false);
    }

    private async ValueTask<bool> DisposeLeaseAsync(
        NavigationEntry entry,
        List<Exception> failures)
    {
        IFlowPresentationLease? lease = entry.Lease;
        entry.Lease = null;
        if (lease is null)
        {
            return true;
        }

        bool closeFinished = await TryTeardownAsync(
            () => lease.CloseAsync(CancellationToken.None),
            $"lease close for route '{entry.Route.Route}'",
            failures).ConfigureAwait(false);
        if (!closeFinished)
        {
            failures.Add(new InvalidOperationException(
                "Navigation skipped dependent lease disposal and content-session teardown because lease closure timed out."));
            return false;
        }

        bool disposalFinished = await TryTeardownAsync(
            lease.DisposeAsync,
            $"lease disposal for route '{entry.Route.Route}'",
            failures).ConfigureAwait(false);
        if (!disposalFinished)
        {
            failures.Add(new InvalidOperationException(
                "Navigation skipped dependent content-session teardown because lease disposal timed out."));
        }

        return disposalFinished;
    }

    private async ValueTask DisposeSessionAsync(
        NavigationEntry entry,
        List<Exception> failures)
    {
        FlowContentSession? session = entry.Session;
        entry.Session = null;
        if (session is null)
        {
            return;
        }

        await TryTeardownAsync(
            session.DisposeAsync,
            $"content-session disposal for route '{entry.Route.Route}'",
            failures).ConfigureAwait(false);
    }

    private async ValueTask DisposeAfterPreCommitFailureAsync(
        NavigationEntry entry,
        Exception primaryException,
        string logicalKey)
    {
        List<Exception> cleanupFailures = [];
        await DisposeEntryAsync(entry, cleanupFailures).ConfigureAwait(false);
        if (cleanupFailures.Count != 0)
        {
            throw new FlowCleanupException(
                "A pre-commit navigation rollback encountered cleanup failures.",
                FlowFeature.Navigation,
                cleanupFailures,
                logicalKey,
                primaryException: primaryException);
        }
    }

    private async ValueTask DisposeFactoryContentAfterFailureAsync(
        NavigationRouteContent content,
        Exception primaryException,
        string logicalKey)
    {
        List<Exception> cleanupFailures = [];
        if (content.OwnsViewModel && !ReferenceEquals(content.ViewModel, content.OwnedScope))
        {
            bool viewModelFinished = await TryTeardownAsync(
                () => DisposeResourceAsync(content.ViewModel),
                $"factory ViewModel disposal for route '{logicalKey}'",
                cleanupFailures).ConfigureAwait(false);
            if (!viewModelFinished)
            {
                cleanupFailures.Add(new InvalidOperationException(
                    "Navigation skipped dependent factory-scope teardown because ViewModel disposal timed out."));
                ThrowFactoryCleanupFailure(cleanupFailures, primaryException, logicalKey);
                return;
            }
        }

        await TryTeardownAsync(
            () => DisposeResourceAsync(content.OwnedScope),
            $"factory scope disposal for route '{logicalKey}'",
            cleanupFailures).ConfigureAwait(false);

        ThrowFactoryCleanupFailure(cleanupFailures, primaryException, logicalKey);
    }

    private static ValueTask DisposeResourceAsync(object resource)
    {
        if (resource is IAsyncDisposable asyncDisposable)
        {
            return asyncDisposable.DisposeAsync();
        }

        if (resource is IDisposable disposable)
        {
            disposable.Dispose();
        }

        return ValueTask.CompletedTask;
    }

    private async ValueTask<bool> TryTeardownAsync(
        Func<ValueTask> teardown,
        string operation,
        List<Exception> failures)
    {
        using CancellationTokenSource timeoutCancellation = new();
        Task timeoutTask = Task.Delay(_teardownTimeout, _timeProvider, timeoutCancellation.Token);
        Task teardownTask = Task.Run(
            async () => await teardown().ConfigureAwait(false),
            CancellationToken.None);
        Task completed = await Task.WhenAny(teardownTask, timeoutTask).ConfigureAwait(false);
        if (ReferenceEquals(completed, teardownTask))
        {
            await timeoutCancellation.CancelAsync().ConfigureAwait(false);
            try
            {
                await teardownTask.ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }

            return true;
        }

        failures.Add(new TimeoutException(
            $"Navigation teardown operation '{operation}' exceeded the configured timeout."));
        _ = teardownTask.ContinueWith(
            static completedTask => _ = completedTask.Exception,
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously | TaskContinuationOptions.OnlyOnFaulted,
            TaskScheduler.Default);
        return false;
    }

    private static void ThrowFactoryCleanupFailure(
        List<Exception> cleanupFailures,
        Exception primaryException,
        string logicalKey)
    {
        if (cleanupFailures.Count != 0)
        {
            throw new FlowCleanupException(
                "A failed navigation content activation encountered cleanup failures.",
                FlowFeature.Navigation,
                cleanupFailures,
                logicalKey,
                primaryException: primaryException);
        }
    }

    private static bool IsTimeoutFailure(Exception exception)
    {
        if (exception is TimeoutException)
        {
            return true;
        }

        if (exception is AggregateException aggregate)
        {
            foreach (Exception innerException in aggregate.InnerExceptions)
            {
                if (IsTimeoutFailure(innerException))
                {
                    return true;
                }
            }

            return false;
        }

        return exception.InnerException is not null && IsTimeoutFailure(exception.InnerException);
    }

    private static NavigationSnapshot CommitSnapshot(RegionState state)
    {
        NavigationEntrySnapshot[] entries = new NavigationEntrySnapshot[state.Stack.Count];
        for (int index = 0; index < state.Stack.Count; index++)
        {
            NavigationEntry entry = state.Stack[index];
            bool isCurrent = index == state.Stack.Count - 1;
            FlowSessionId? sessionId = isCurrent || entry.Route.Retention == NavigationRetention.RetainInBackStack
                ? entry.Session?.Descriptor.SessionId
                : null;
            entries[index] = new NavigationEntrySnapshot(
                entry.Route.Route,
                entry.Route.Contract,
                entry.Route.ViewModelType,
                entry.Route.Retention,
                sessionId,
                isCurrent);
        }

        NavigationSnapshot snapshot = new(state.Registration.Key, ++state.Version, entries);
        Volatile.Write(ref state.Snapshot, snapshot);
        return snapshot;
    }

    private void PublishSnapshot(
        NavigationSnapshot previous,
        NavigationSnapshot current,
        List<Exception> failures)
    {
        EventHandler<NavigationSnapshotChangedEventArgs>? handlers = SnapshotChanged;
        if (handlers is null)
        {
            return;
        }

        NavigationSnapshotChangedEventArgs arguments = new(previous, current);
        foreach (Delegate handler in handlers.GetInvocationList())
        {
            try
            {
                ((EventHandler<NavigationSnapshotChangedEventArgs>)handler)(this, arguments);
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }
        }
    }

    private static void ThrowPostCommitFailures(
        RegionState state,
        NavigationEntry entry,
        List<Exception> lifecycleFailures,
        List<Exception> cleanupFailures)
    {
        FlowLifecycleException? lifecycleException = lifecycleFailures.Count == 0
            ? null
            : new FlowLifecycleException(
                "One or more post-commit navigation callbacks failed.",
                FlowFeature.Navigation,
                FlowLifecycleStage.Activated,
                lifecycleFailures,
                state.Registration.Key.Value,
                entry.Session?.Descriptor.SessionId);

        if (cleanupFailures.Count != 0)
        {
            throw new FlowCleanupException(
                "A committed navigation transition encountered cleanup failures.",
                FlowFeature.Navigation,
                cleanupFailures,
                state.Registration.Key.Value,
                entry.Session?.Descriptor.SessionId,
                lifecycleException);
        }

        if (lifecycleException is not null)
        {
            throw lifecycleException;
        }
    }

    private static bool MatchesExpectedSession(RegionState state, FlowSessionId? expected) =>
        expected is null || state.Snapshot.Current?.SessionId == expected;

    private RegionState GetRegion(RegionKey region) =>
        _regions.TryGetValue(region, out RegionState? state)
            ? state
            : throw new KeyNotFoundException($"Navigation region '{region}' is not registered.");

    private bool IsActiveMutation(RegionState state)
    {
        for (MutationContext? context = _activeMutation.Value; context is not null; context = context.Parent)
        {
            if (context.IsActive && ReferenceEquals(context.State, state))
            {
                return true;
            }
        }

        return false;
    }

    private bool HasActiveMutation()
    {
        for (MutationContext? context = _activeMutation.Value; context is not null; context = context.Parent)
        {
            if (context.IsActive)
            {
                return true;
            }
        }

        return false;
    }

    private void ThrowIfShutdownReentrant()
    {
        if (HasActiveMutation())
        {
            throw new FlowReentrancyException(
                "Navigation shutdown cannot be awaited from inside a navigation lifecycle callback.",
                FlowFeature.Navigation,
                _activeMutation.Value?.State.Registration.Key.Value,
                _activeMutation.Value?.State.Snapshot.Current?.SessionId);
        }
    }

    private sealed class MutationContext
    {
        internal MutationContext(RegionState state, MutationContext? parent)
        {
            State = state;
            Parent = parent;
        }

        internal RegionState State { get; }
        internal MutationContext? Parent { get; }
        internal bool IsActive { get; set; } = true;
    }

    private sealed class NavigationEntry
    {
        internal NavigationEntry(
            NavigationRouteRegistration route,
            object? parameter,
            FlowContentSession session)
        {
            Route = route;
            Parameter = parameter;
            Session = session;
        }

        internal NavigationRouteRegistration Route { get; }
        internal object? Parameter { get; }
        internal FlowContentSession? Session { get; set; }
        internal IFlowPresentationLease? Lease { get; set; }
    }

    private sealed class RegionState
    {
        internal RegionState(NavigationRegionRegistration registration)
        {
            Registration = registration;
            Snapshot = new NavigationSnapshot(registration.Key, 0, []);
        }

        internal NavigationRegionRegistration Registration { get; }
        internal RegionGate Gate { get; } = new();
        internal List<NavigationEntry> Stack { get; } = [];
        internal NavigationSnapshot Snapshot;
        internal long Version;
        internal int ShuttingDown;
    }

    private sealed class RegionGate
    {
        private readonly object _gate = new();
        private readonly LinkedList<Waiter> _waiters = [];
        private bool _held;

        internal ValueTask<Lease?> AcquireAsync(bool rejectWhileBusy, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (_gate)
            {
                if (!_held)
                {
                    _held = true;
                    return ValueTask.FromResult<Lease?>(new Lease(this));
                }

                if (rejectWhileBusy)
                {
                    return ValueTask.FromResult<Lease?>(null);
                }

                Waiter waiter = new();
                waiter.Node = _waiters.AddLast(waiter);
                waiter.Register(this, cancellationToken);
                return AwaitWaiterAsync(waiter);
            }
        }

        internal async ValueTask<Lease> AcquireForShutdownAsync(CancellationToken cancellationToken)
        {
            Lease? lease = await AcquireAsync(rejectWhileBusy: false, cancellationToken)
                .ConfigureAwait(false);
            if (cancellationToken.IsCancellationRequested)
            {
                if (lease is not null)
                {
                    await lease.DisposeAsync().ConfigureAwait(false);
                }

                cancellationToken.ThrowIfCancellationRequested();
            }

            return lease!;
        }

        internal void CancelQueued()
        {
            Waiter[] waiters;
            lock (_gate)
            {
                waiters = new Waiter[_waiters.Count];
                _waiters.CopyTo(waiters, 0);
                _waiters.Clear();
                foreach (Waiter waiter in waiters)
                {
                    waiter.Node = null;
                }
            }

            foreach (Waiter waiter in waiters)
            {
                waiter.CancelForShutdown();
            }
        }

        private static async ValueTask<Lease?> AwaitWaiterAsync(Waiter waiter)
        {
            try
            {
                return await waiter.Completion.Task.ConfigureAwait(false);
            }
            finally
            {
                waiter.DisposeRegistration();
            }
        }

        private void Cancel(Waiter waiter, CancellationToken cancellationToken)
        {
            bool removed = false;
            lock (_gate)
            {
                if (waiter.Node is not null)
                {
                    _waiters.Remove(waiter.Node);
                    waiter.Node = null;
                    removed = true;
                }
            }

            if (removed)
            {
                waiter.Completion.TrySetCanceled(cancellationToken);
            }
        }

        private void Release()
        {
            Waiter? next = null;
            lock (_gate)
            {
                if (_waiters.First is LinkedListNode<Waiter> node)
                {
                    next = node.Value;
                    _waiters.RemoveFirst();
                    next.Node = null;
                }
                else
                {
                    _held = false;
                }
            }

            next?.Completion.TrySetResult(new Lease(this));
        }

        internal sealed class Lease : IAsyncDisposable
        {
            private RegionGate? _owner;

            internal Lease(RegionGate owner) => _owner = owner;

            public ValueTask DisposeAsync()
            {
                Interlocked.Exchange(ref _owner, null)?.Release();
                return ValueTask.CompletedTask;
            }
        }

        private sealed class Waiter
        {
            private CancellationTokenRegistration _registration;

            internal Waiter()
            {
                Completion = new TaskCompletionSource<Lease>(TaskCreationOptions.RunContinuationsAsynchronously);
            }

            internal void Register(RegionGate owner, CancellationToken cancellationToken)
            {
                _registration = cancellationToken.Register(
                    static state =>
                    {
                        (RegionGate Owner, Waiter Waiter, CancellationToken Token) tuple =
                            ((RegionGate, Waiter, CancellationToken))state!;
                        tuple.Owner.Cancel(tuple.Waiter, tuple.Token);
                    },
                    (owner, this, cancellationToken));
            }

            internal TaskCompletionSource<Lease> Completion { get; }
            internal LinkedListNode<Waiter>? Node { get; set; }

            internal void CancelForShutdown() =>
                Completion.TrySetCanceled(new CancellationToken(canceled: true));

            internal void DisposeRegistration() => _registration.Dispose();
        }
    }
}
