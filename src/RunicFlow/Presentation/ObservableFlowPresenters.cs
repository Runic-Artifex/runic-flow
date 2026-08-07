using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using RunicFlow.Dialogs;
using RunicFlow.Navigation;

namespace RunicFlow.Presentation;

/// <summary>Contains the current content and logical transition for one navigation outlet.</summary>
public sealed record NavigationOutletSnapshot
{
    /// <summary>Creates one immutable outlet observation.</summary>
    public NavigationOutletSnapshot(
        RegionKey region,
        long version,
        FlowContentDescriptor? content,
        NavigationPresentationContext? transition)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(version);
        Region = region;
        Version = version;
        Content = content;
        Transition = transition;
    }

    /// <summary>Gets the logical region.</summary>
    public RegionKey Region { get; }
    /// <summary>Gets the monotonically increasing presentation version.</summary>
    public long Version { get; }
    /// <summary>Gets the current content, or null when the outlet is empty.</summary>
    public FlowContentDescriptor? Content { get; }
    /// <summary>Gets the transition that produced current content.</summary>
    public NavigationPresentationContext? Transition { get; }
}

/// <summary>Reports one committed navigation outlet change.</summary>
public sealed class NavigationOutletChangedEventArgs : EventArgs
{
    /// <summary>Creates change arguments.</summary>
    public NavigationOutletChangedEventArgs(
        NavigationOutletSnapshot previous,
        NavigationOutletSnapshot current)
    {
        Previous = previous ?? throw new ArgumentNullException(nameof(previous));
        Current = current ?? throw new ArgumentNullException(nameof(current));
    }

    /// <summary>Gets the previous outlet observation.</summary>
    public NavigationOutletSnapshot Previous { get; }
    /// <summary>Gets the current outlet observation.</summary>
    public NavigationOutletSnapshot Current { get; }
}

/// <summary>
/// Presents Flow navigation into observable, frontend-neutral outlets. Generated
/// .NET or TypeScript adapters can project these observations through CsWebUi.
/// </summary>
public sealed class ObservableNavigationPresenter : INavigationRegionPresenter
{
    private readonly object _gate = new();
    private readonly Dictionary<RegionKey, NavigationOutletSnapshot> _outlets = [];

    /// <summary>Occurs after one outlet presentation commits.</summary>
    public event EventHandler<NavigationOutletChangedEventArgs>? Changed;

    /// <summary>Gets the latest immutable outlet observation.</summary>
    public NavigationOutletSnapshot GetSnapshot(RegionKey region)
    {
        lock (_gate)
        {
            return _outlets.TryGetValue(region, out NavigationOutletSnapshot? snapshot)
                ? snapshot
                : new NavigationOutletSnapshot(region, 0, content: null, transition: null);
        }
    }

    /// <inheritdoc />
    public ValueTask<IFlowPresentationLease> PresentAsync(
        RegionKey region,
        FlowContentDescriptor content,
        NavigationPresentationContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        NavigationOutletSnapshot previous;
        NavigationOutletSnapshot current;
        lock (_gate)
        {
            previous = GetSnapshotLocked(region);
            current = new NavigationOutletSnapshot(
                region,
                checked(previous.Version + 1),
                content,
                context);
            _outlets[region] = current;
        }

        Publish(previous, current);
        return ValueTask.FromResult<IFlowPresentationLease>(
            new NavigationOutletLease(this, region, content.SessionId));
    }

    /// <inheritdoc />
    public ValueTask ClearAsync(RegionKey region, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Clear(region, expectedSession: null);
        return ValueTask.CompletedTask;
    }

    private void Clear(RegionKey region, FlowSessionId? expectedSession)
    {
        NavigationOutletSnapshot previous;
        NavigationOutletSnapshot current;
        lock (_gate)
        {
            previous = GetSnapshotLocked(region);
            if (expectedSession is FlowSessionId session
                && previous.Content?.SessionId != session)
            {
                return;
            }

            if (previous.Content is null)
            {
                return;
            }

            current = new NavigationOutletSnapshot(
                region,
                checked(previous.Version + 1),
                content: null,
                transition: null);
            _outlets[region] = current;
        }

        Publish(previous, current);
    }

    private NavigationOutletSnapshot GetSnapshotLocked(RegionKey region) =>
        _outlets.TryGetValue(region, out NavigationOutletSnapshot? snapshot)
            ? snapshot
            : new NavigationOutletSnapshot(region, 0, content: null, transition: null);

    private void Publish(
        NavigationOutletSnapshot previous,
        NavigationOutletSnapshot current)
    {
        EventHandler<NavigationOutletChangedEventArgs>? handlers = Changed;
        if (handlers is null)
        {
            return;
        }

        var args = new NavigationOutletChangedEventArgs(previous, current);
        foreach (EventHandler<NavigationOutletChangedEventArgs> handler
                 in handlers.GetInvocationList())
        {
            try
            {
                handler(this, args);
            }
            catch (Exception)
            {
                // Frontend subscriptions cannot invalidate an already committed Flow transition.
            }
        }
    }

    private sealed class NavigationOutletLease(
        ObservableNavigationPresenter owner,
        RegionKey region,
        FlowSessionId sessionId) : IFlowPresentationLease
    {
        private int _closed;

        public ValueTask CloseAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Interlocked.Exchange(ref _closed, 1) == 0)
            {
                owner.Clear(region, sessionId);
            }

            return ValueTask.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _closed, 1) == 0)
            {
                owner.Clear(region, sessionId);
            }

            return ValueTask.CompletedTask;
        }
    }
}

/// <summary>Contains one active typed-dialog presentation without erasing its close guards.</summary>
public sealed class DialogOutletSnapshot
{
    private readonly Func<CancellationToken, ValueTask<bool>> _cancel;
    private readonly Func<CancellationToken, ValueTask<bool>> _dismiss;

    internal DialogOutletSnapshot(
        DialogKey dialog,
        FlowContentDescriptor content,
        IReadOnlyList<FlowAction> actions,
        Func<CancellationToken, ValueTask<bool>> cancel,
        Func<CancellationToken, ValueTask<bool>> dismiss)
    {
        Dialog = dialog;
        Content = content;
        Actions = actions;
        _cancel = cancel;
        _dismiss = dismiss;
    }

    /// <summary>Gets the logical dialog identity.</summary>
    public DialogKey Dialog { get; }
    /// <summary>Gets the frontend-neutral typed content.</summary>
    public FlowContentDescriptor Content { get; }
    /// <summary>Gets immutable semantic actions.</summary>
    public IReadOnlyList<FlowAction> Actions { get; }

    /// <summary>Requests guarded cancellation through the typed Flow controller.</summary>
    public ValueTask<bool> CancelAsync(CancellationToken cancellationToken = default) =>
        _cancel(cancellationToken);

    /// <summary>Requests guarded surface dismissal through the typed Flow controller.</summary>
    public ValueTask<bool> DismissAsync(CancellationToken cancellationToken = default) =>
        _dismiss(cancellationToken);
}

/// <summary>Reports the complete active dialog stack after one committed change.</summary>
public sealed class DialogOutletsChangedEventArgs : EventArgs
{
    /// <summary>Creates a frozen active-dialog observation.</summary>
    public DialogOutletsChangedEventArgs(IReadOnlyList<DialogOutletSnapshot> active)
    {
        ArgumentNullException.ThrowIfNull(active);
        DialogOutletSnapshot[] copy = new DialogOutletSnapshot[active.Count];
        for (int index = 0; index < active.Count; index++)
        {
            copy[index] = active[index] ??
                throw new ArgumentException("Active dialogs cannot contain null.", nameof(active));
        }

        Active = new ReadOnlyCollection<DialogOutletSnapshot>(copy);
    }

    /// <summary>Gets active dialogs in open order.</summary>
    public IReadOnlyList<DialogOutletSnapshot> Active { get; }
}

/// <summary>
/// Presents typed Flow dialogs as an observable stack while retaining each closed
/// generic controller for result completion, cancellation, dismissal, and guards.
/// </summary>
public sealed class ObservableDialogPresenter : IDialogPresenter
{
    private readonly object _gate = new();
    private readonly List<DialogOutletSnapshot> _active = [];

    /// <summary>Occurs after the active dialog stack changes.</summary>
    public event EventHandler<DialogOutletsChangedEventArgs>? Changed;

    /// <summary>Gets a frozen active-dialog stack.</summary>
    public IReadOnlyList<DialogOutletSnapshot> Active
    {
        get
        {
            lock (_gate)
            {
                return Array.AsReadOnly(_active.ToArray());
            }
        }
    }

    /// <inheritdoc />
    public ValueTask<IFlowPresentationLease> PresentAsync<TResult>(
        DialogPresentation<TResult> presentation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(presentation);
        cancellationToken.ThrowIfCancellationRequested();
        var snapshot = new DialogOutletSnapshot(
            presentation.Dialog,
            presentation.Content,
            presentation.Actions,
            presentation.Controller.CancelAsync,
            presentation.Controller.DismissAsync);
        lock (_gate)
        {
            _active.Add(snapshot);
        }

        Publish();
        return ValueTask.FromResult<IFlowPresentationLease>(
            new DialogOutletLease(this, snapshot));
    }

    private void Remove(DialogOutletSnapshot snapshot)
    {
        lock (_gate)
        {
            if (!_active.Remove(snapshot))
            {
                return;
            }
        }

        Publish();
    }

    private void Publish()
    {
        EventHandler<DialogOutletsChangedEventArgs>? handlers = Changed;
        if (handlers is null)
        {
            return;
        }

        var args = new DialogOutletsChangedEventArgs(Active);
        foreach (EventHandler<DialogOutletsChangedEventArgs> handler
                 in handlers.GetInvocationList())
        {
            try
            {
                handler(this, args);
            }
            catch (Exception)
            {
                // Frontend subscriptions cannot invalidate an already committed dialog transition.
            }
        }
    }

    private sealed class DialogOutletLease(
        ObservableDialogPresenter owner,
        DialogOutletSnapshot snapshot) : IFlowPresentationLease
    {
        private int _closed;

        public ValueTask CloseAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Interlocked.Exchange(ref _closed, 1) == 0)
            {
                owner.Remove(snapshot);
            }

            return ValueTask.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _closed, 1) == 0)
            {
                owner.Remove(snapshot);
            }

            return ValueTask.CompletedTask;
        }
    }
}
