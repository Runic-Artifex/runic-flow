using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace WebUIToolkit.MVVM.Flow;

/// <summary>
/// Owns the resources associated with one activated logical content entry.
/// </summary>
internal sealed class FlowContentSession : IAsyncDisposable
{
    private readonly object _gate = new();
    private readonly object _ownedScope;
    private readonly bool _ownsViewModel;
    private readonly CancellationTokenSource _lifetimeSource = new();
    private readonly List<FlowContentSession> _children = [];
    private readonly Lazy<Task> _disposeOperation;
    private IFlowPresentationLease? _presenterLease;
    private bool _teardownStarted;
    private int _hasParent;

    /// <summary>
    /// Initializes a content session. The scope is always owned and is disposed last.
    /// </summary>
    internal FlowContentSession(
        FlowSessionId sessionId,
        ViewContract contract,
        object viewModel,
        Type declaredViewModelType,
        IReadOnlyDictionary<string, string>? metadata,
        object ownedScope,
        bool ownsViewModel)
    {
        ArgumentNullException.ThrowIfNull(ownedScope);
        EnsureDisposable(ownedScope, nameof(ownedScope));

        Descriptor = new FlowContentDescriptor(
            sessionId,
            contract,
            viewModel,
            declaredViewModelType,
            metadata);
        _ownedScope = ownedScope;
        _ownsViewModel = ownsViewModel;
        _disposeOperation = new Lazy<Task>(DisposeCoreAsync, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    /// <summary>
    /// Gets the immutable public presentation descriptor.
    /// </summary>
    internal FlowContentDescriptor Descriptor { get; }

    /// <summary>
    /// Gets a token cancelled when teardown starts.
    /// </summary>
    internal CancellationToken Lifetime => _lifetimeSource.Token;

    /// <summary>
    /// Gets whether session teardown has started.
    /// </summary>
    internal bool IsDisposalStarted
    {
        get
        {
            lock (_gate)
            {
                return _teardownStarted;
            }
        }
    }

    /// <summary>
    /// Transfers ownership of a newly created child session to this session.
    /// </summary>
    internal void AddChild(FlowContentSession child)
    {
        ArgumentNullException.ThrowIfNull(child);
        if (ReferenceEquals(this, child))
        {
            throw new ArgumentException("A content session cannot own itself.", nameof(child));
        }

        lock (_gate)
        {
            ThrowIfTeardownStarted();
            child.ClaimParent();

            try
            {
                _children.Add(child);
            }
            catch
            {
                child.ReleaseParentClaim();
                throw;
            }
        }
    }

    /// <summary>
    /// Transfers ownership of the presenter's lease to this session.
    /// </summary>
    internal void AttachPresenterLease(IFlowPresentationLease presenterLease)
    {
        ArgumentNullException.ThrowIfNull(presenterLease);

        lock (_gate)
        {
            ThrowIfTeardownStarted();
            if (_presenterLease is not null)
            {
                throw new InvalidOperationException("The content session already owns a presenter lease.");
            }

            _presenterLease = presenterLease;
        }
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync() => new(_disposeOperation.Value);

    private async Task DisposeCoreAsync()
    {
        FlowContentSession[] children;
        IFlowPresentationLease? lease;

        lock (_gate)
        {
            _teardownStarted = true;
            children = _children.ToArray();
            _children.Clear();
            lease = _presenterLease;
            _presenterLease = null;
        }

        List<Exception>? failures = null;
        TryCancelLifetime(ref failures);

        for (int index = children.Length - 1; index >= 0; index--)
        {
            await TryDisposeAsync(children[index], failures ??= []).ConfigureAwait(false);
        }

        if (lease is not null)
        {
            try
            {
                await lease.CloseAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                (failures ??= []).Add(exception);
            }

            await TryDisposeAsync(lease, failures ??= []).ConfigureAwait(false);
        }

        if (_ownsViewModel && !ReferenceEquals(Descriptor.ViewModel, _ownedScope))
        {
            await TryDisposeAsync(Descriptor.ViewModel, failures ??= []).ConfigureAwait(false);
        }

        await TryDisposeAsync(_ownedScope, failures ??= []).ConfigureAwait(false);
        _lifetimeSource.Dispose();

        if (failures is { Count: > 0 })
        {
            throw new FlowCleanupException(
                "The Flow content session encountered one or more ordered cleanup failures.",
                FlowFeature.Shared,
                failures,
                logicalKey: Descriptor.Contract.Value,
                sessionId: Descriptor.SessionId);
        }
    }

    private void ClaimParent()
    {
        ObjectDisposedException.ThrowIf(IsDisposalStarted, this);

        if (Interlocked.CompareExchange(ref _hasParent, 1, 0) != 0)
        {
            throw new InvalidOperationException("A content session can have only one owning parent.");
        }

        bool disposalStarted = IsDisposalStarted;
        if (disposalStarted)
        {
            ReleaseParentClaim();
        }

        ObjectDisposedException.ThrowIf(disposalStarted, this);
    }

    private void ReleaseParentClaim() => Volatile.Write(ref _hasParent, 0);

    private void ThrowIfTeardownStarted()
    {
        ObjectDisposedException.ThrowIf(_teardownStarted, this);
    }

    private void TryCancelLifetime(ref List<Exception>? failures)
    {
        try
        {
            _lifetimeSource.Cancel(throwOnFirstException: false);
        }
        catch (Exception exception)
        {
            (failures ??= []).Add(exception);
        }
    }

    private static async ValueTask TryDisposeAsync(object resource, List<Exception> failures)
    {
        try
        {
            if (resource is IAsyncDisposable asyncDisposable)
            {
                await asyncDisposable.DisposeAsync().ConfigureAwait(false);
            }
            else if (resource is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }
        catch (Exception exception)
        {
            failures.Add(exception);
        }
    }

    private static void EnsureDisposable(object resource, string parameterName)
    {
        if (resource is not IAsyncDisposable && resource is not IDisposable)
        {
            throw new ArgumentException(
                "A Flow content scope must implement IAsyncDisposable or IDisposable.",
                parameterName);
        }
    }
}
