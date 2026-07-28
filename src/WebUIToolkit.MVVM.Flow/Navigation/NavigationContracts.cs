using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using WebUIToolkit.MVVM.Flow;

namespace WebUIToolkit.MVVM.Navigation;

/// <summary>Identifies the stack mutation performed by a navigation request.</summary>
public enum NavigationMode
{
    /// <summary>Add the target above the current entry.</summary>
    Push,
    /// <summary>Replace the current entry without increasing stack depth.</summary>
    Replace,
    /// <summary>Replace the complete stack with the target.</summary>
    Reset,
}

/// <summary>Controls how a page is kept when another page is pushed above it.</summary>
public enum NavigationRetention
{
    /// <summary>Keep the page scope and ViewModel alive for Back.</summary>
    RetainInBackStack,
    /// <summary>Release the page and create a new session when Back returns to it.</summary>
    RecreateOnBack,
}

/// <summary>Controls concurrent mutation requests for one region.</summary>
public enum NavigationConcurrency
{
    /// <summary>Serialize requests in first-in, first-out order.</summary>
    Queue,
    /// <summary>Return a busy result instead of waiting.</summary>
    RejectWhileBusy,
}

/// <summary>Identifies the observable outcome of a navigation request.</summary>
public enum NavigationResultKind
{
    /// <summary>The region committed a new immutable snapshot.</summary>
    Navigated,
    /// <summary>A guard or region invariant rejected the request.</summary>
    Rejected,
    /// <summary>The region was busy and configured to reject concurrent work.</summary>
    Busy,
    /// <summary>The request referred to a session that is no longer current.</summary>
    Stale,
    /// <summary>The request required no state change.</summary>
    NoOp,
    /// <summary>The navigation service is shutting down.</summary>
    ShuttingDown,
}

/// <summary>Supplies options for a navigation request.</summary>
public sealed record NavigationOptions
{
    /// <summary>Gets or initializes the stack mutation. The default is Push.</summary>
    public NavigationMode Mode { get; init; } = NavigationMode.Push;
}

/// <summary>Represents an immutable guard decision.</summary>
public readonly record struct NavigationGuardResult
{
    private NavigationGuardResult(bool isAllowed, string? reason)
    {
        if (!isAllowed && string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException("A denied navigation guard result requires a reason.", nameof(reason));
        }

        IsAllowed = isAllowed;
        Reason = reason;
    }

    /// <summary>Gets whether navigation may continue.</summary>
    public bool IsAllowed { get; }

    /// <summary>Gets the bounded consumer reason for a denial.</summary>
    public string? Reason { get; }

    /// <summary>Creates an allowed result.</summary>
    public static NavigationGuardResult Allow() => new(true, null);

    /// <summary>Creates a denied result.</summary>
    public static NavigationGuardResult Deny(string reason) => new(false, reason);
}

/// <summary>Describes an attempted transition to the current page's leave guard.</summary>
public sealed record NavigationGuardContext
{
    /// <summary>Initializes guard context.</summary>
    public NavigationGuardContext(
        RegionKey region,
        RouteKey currentRoute,
        FlowSessionId currentSessionId,
        RouteKey? targetRoute,
        NavigationMode? mode)
    {
        ValidateRegion(region);
        ValidateRoute(currentRoute, nameof(currentRoute));
        if (targetRoute is RouteKey target)
        {
            ValidateRoute(target, nameof(targetRoute));
        }

        Region = region;
        CurrentRoute = currentRoute;
        CurrentSessionId = currentSessionId;
        TargetRoute = targetRoute;
        Mode = mode;
    }

    /// <summary>Gets the region being mutated.</summary>
    public RegionKey Region { get; }
    /// <summary>Gets the current route.</summary>
    public RouteKey CurrentRoute { get; }
    /// <summary>Gets the current content session.</summary>
    public FlowSessionId CurrentSessionId { get; }
    /// <summary>Gets the target route, or null for Back or Clear.</summary>
    public RouteKey? TargetRoute { get; }
    /// <summary>Gets the requested mutation, or null for Back or Clear.</summary>
    public NavigationMode? Mode { get; }

    private static void ValidateRegion(RegionKey region)
    {
        if (string.IsNullOrEmpty(region.Value))
        {
            throw new ArgumentException("A navigation region key cannot be empty.", nameof(region));
        }
    }

    private static void ValidateRoute(RouteKey route, string parameterName)
    {
        if (string.IsNullOrEmpty(route.Value))
        {
            throw new ArgumentException("A navigation route key cannot be empty.", parameterName);
        }
    }
}

/// <summary>Allows a current ViewModel to reject an attempted leave.</summary>
public interface INavigationGuard
{
    /// <summary>Determines whether a region transition may continue.</summary>
    ValueTask<NavigationGuardResult> CanLeaveAsync(
        NavigationGuardContext context,
        CancellationToken cancellationToken);
}

/// <summary>Describes one logical stack entry without exposing mutable engine state.</summary>
public sealed record NavigationEntrySnapshot
{
    /// <summary>Initializes an entry snapshot.</summary>
    public NavigationEntrySnapshot(
        RouteKey route,
        ViewContract contract,
        Type viewModelType,
        NavigationRetention retention,
        FlowSessionId? sessionId,
        bool isCurrent)
    {
        ArgumentNullException.ThrowIfNull(viewModelType);
        Route = route;
        Contract = contract;
        ViewModelType = viewModelType;
        Retention = retention;
        SessionId = sessionId;
        IsCurrent = isCurrent;
    }

    /// <summary>Gets the route.</summary>
    public RouteKey Route { get; }
    /// <summary>Gets the logical presentation contract.</summary>
    public ViewContract Contract { get; }
    /// <summary>Gets the route's declared ViewModel type.</summary>
    public Type ViewModelType { get; }
    /// <summary>Gets the route retention policy.</summary>
    public NavigationRetention Retention { get; }
    /// <summary>Gets the live session ID, or null when this entry will be recreated.</summary>
    public FlowSessionId? SessionId { get; }
    /// <summary>Gets whether this is the current stack entry.</summary>
    public bool IsCurrent { get; }
}

/// <summary>Describes one frozen route registration without exposing its factory delegate.</summary>
public sealed record NavigationRouteDescriptor
{
    /// <summary>Initializes a route descriptor.</summary>
    public NavigationRouteDescriptor(
        RouteKey route,
        ViewContract contract,
        Type viewModelType,
        Type? parameterType,
        NavigationRetention retention)
    {
        ArgumentNullException.ThrowIfNull(viewModelType);
        Route = route;
        Contract = contract;
        ViewModelType = viewModelType;
        ParameterType = parameterType;
        Retention = retention;
    }

    /// <summary>Gets the route key.</summary>
    public RouteKey Route { get; }
    /// <summary>Gets the logical presentation contract.</summary>
    public ViewContract Contract { get; }
    /// <summary>Gets the declared ViewModel type.</summary>
    public Type ViewModelType { get; }
    /// <summary>Gets the declared parameter type, or null for a parameterless route.</summary>
    public Type? ParameterType { get; }
    /// <summary>Gets the back-stack retention policy.</summary>
    public NavigationRetention Retention { get; }
}

/// <summary>Represents one immutable region snapshot.</summary>
public sealed record NavigationSnapshot
{
    /// <summary>Initializes a snapshot and defensively copies its stack.</summary>
    public NavigationSnapshot(RegionKey region, long version, IReadOnlyList<NavigationEntrySnapshot> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        if (string.IsNullOrEmpty(region.Value))
        {
            throw new ArgumentException("A navigation region key cannot be empty.", nameof(region));
        }

        ArgumentOutOfRangeException.ThrowIfNegative(version);

        Region = region;
        Version = version;
        NavigationEntrySnapshot[] copy = new NavigationEntrySnapshot[entries.Count];
        for (int index = 0; index < entries.Count; index++)
        {
            copy[index] = entries[index] ??
                throw new ArgumentException("A navigation snapshot cannot contain null entries.", nameof(entries));
        }

        Entries = new ReadOnlyCollection<NavigationEntrySnapshot>(copy);
    }

    /// <summary>Gets the region.</summary>
    public RegionKey Region { get; }
    /// <summary>Gets the monotonically increasing committed version.</summary>
    public long Version { get; }
    /// <summary>Gets entries ordered from root to current.</summary>
    public IReadOnlyList<NavigationEntrySnapshot> Entries { get; }
    /// <summary>Gets the current entry, or null when the region is empty.</summary>
    public NavigationEntrySnapshot? Current => Entries.Count == 0 ? null : Entries[^1];
}

/// <summary>Reports one completed navigation request.</summary>
public sealed record NavigationResult
{
    /// <summary>Initializes a result.</summary>
    public NavigationResult(NavigationResultKind kind, NavigationSnapshot snapshot, string? reason = null)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        Kind = kind;
        Snapshot = snapshot;
        Reason = reason;
    }

    /// <summary>Gets the result kind.</summary>
    public NavigationResultKind Kind { get; }
    /// <summary>Gets the authoritative snapshot after the request.</summary>
    public NavigationSnapshot Snapshot { get; }
    /// <summary>Gets a consumer-facing rejection reason when available.</summary>
    public string? Reason { get; }
}

/// <summary>Provides the old and new immutable snapshots after a commit.</summary>
public sealed class NavigationSnapshotChangedEventArgs : EventArgs
{
    /// <summary>Initializes snapshot-change arguments.</summary>
    public NavigationSnapshotChangedEventArgs(NavigationSnapshot previous, NavigationSnapshot current)
    {
        ArgumentNullException.ThrowIfNull(previous);
        ArgumentNullException.ThrowIfNull(current);
        Previous = previous;
        Current = current;
    }

    /// <summary>Gets the snapshot before commit.</summary>
    public NavigationSnapshot Previous { get; }
    /// <summary>Gets the committed snapshot.</summary>
    public NavigationSnapshot Current { get; }
}

/// <summary>Controls transactional logical navigation regions.</summary>
public interface INavigationService
{
    /// <summary>Occurs once for each committed region mutation.</summary>
    event EventHandler<NavigationSnapshotChangedEventArgs>? SnapshotChanged;

    /// <summary>Starts every configured root region that has a start route.</summary>
    ValueTask StartAsync(CancellationToken cancellationToken = default);

    /// <summary>Navigates to the route registered for a parameterless ViewModel.</summary>
    ValueTask<NavigationResult> NavigateAsync<TViewModel>(
        RegionKey region,
        NavigationOptions? options = null,
        CancellationToken cancellationToken = default)
        where TViewModel : class;

    /// <summary>Navigates to the route registered for a typed ViewModel parameter.</summary>
    ValueTask<NavigationResult> NavigateAsync<TViewModel, TParameter>(
        RegionKey region,
        TParameter parameter,
        NavigationOptions? options = null,
        CancellationToken cancellationToken = default)
        where TViewModel : class;

    /// <summary>Navigates to an explicitly identified registered route.</summary>
    ValueTask<NavigationResult> NavigateRouteAsync(
        RegionKey region,
        RouteKey route,
        object? parameter = null,
        NavigationOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>Navigates only if the supplied session is still current.</summary>
    ValueTask<NavigationResult> NavigateRouteAsync(
        RegionKey region,
        FlowSessionId expectedCurrentSession,
        RouteKey route,
        object? parameter = null,
        NavigationOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>Returns to the previous entry.</summary>
    ValueTask<NavigationResult> BackAsync(
        RegionKey region,
        CancellationToken cancellationToken = default);

    /// <summary>Returns only if the supplied session is still current.</summary>
    ValueTask<NavigationResult> BackAsync(
        RegionKey region,
        FlowSessionId expectedCurrentSession,
        CancellationToken cancellationToken = default);

    /// <summary>Clears a region when its registration permits an empty state.</summary>
    ValueTask<NavigationResult> ClearAsync(
        RegionKey region,
        CancellationToken cancellationToken = default);

    /// <summary>Clears only if the supplied session is still current.</summary>
    ValueTask<NavigationResult> ClearAsync(
        RegionKey region,
        FlowSessionId expectedCurrentSession,
        CancellationToken cancellationToken = default);

    /// <summary>Gets the latest immutable snapshot.</summary>
    NavigationSnapshot GetSnapshot(RegionKey region);

    /// <summary>Determines whether an adapter event still targets current content.</summary>
    bool IsCurrentSession(RegionKey region, FlowSessionId sessionId);

    /// <summary>Asks the current ViewModel guard whether application close may leave a region.</summary>
    ValueTask<NavigationGuardResult> CanLeaveAsync(
        RegionKey region,
        CancellationToken cancellationToken = default);

    /// <summary>Rejects new work, cancels queued requests, and tears down all regions.</summary>
    ValueTask ShutdownAsync(CancellationToken cancellationToken = default);
}
