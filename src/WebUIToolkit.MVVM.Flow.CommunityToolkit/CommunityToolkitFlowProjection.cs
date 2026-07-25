using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using WebUIToolkit.MVVM.Flow;

namespace WebUIToolkit.MVVM.Flow.CommunityToolkit;

/// <summary>Creates closed schema-v1 projections over CommunityToolkit-generated members.</summary>
public static class CommunityToolkitFlowProjection
{
    /// <summary>Creates the schema-v1 projection over a synchronous generated relay command.</summary>
    public static CommunityToolkitFlowProjection<TViewModel> Create<TViewModel>(
        FlowSessionId sessionId,
        TViewModel viewModel,
        Func<TViewModel, string?> getTitle,
        Action<TViewModel, string?> setTitle,
        Func<TViewModel, IRelayCommand> getSubmitCommand,
        Func<TViewModel, IReadOnlyList<string>>? getTitleErrors = null)
        where TViewModel : class
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        ArgumentNullException.ThrowIfNull(getTitle);
        ArgumentNullException.ThrowIfNull(setTitle);
        ArgumentNullException.ThrowIfNull(getSubmitCommand);
        IRelayCommand command = getSubmitCommand(viewModel) ??
            throw new ArgumentException("The generated SubmitCommand cannot be null.", nameof(getSubmitCommand));
        return new(
            sessionId,
            viewModel,
            getTitle,
            setTitle,
            getTitleErrors ?? (static _ => Array.Empty<string>()),
            command,
            asyncSubmitCommand: null);
    }

    /// <summary>Creates the schema-v1 projection over an asynchronous generated relay command.</summary>
    public static CommunityToolkitFlowProjection<TViewModel> CreateAsync<TViewModel>(
        FlowSessionId sessionId,
        TViewModel viewModel,
        Func<TViewModel, string?> getTitle,
        Action<TViewModel, string?> setTitle,
        Func<TViewModel, IAsyncRelayCommand> getSubmitCommand,
        Func<TViewModel, IReadOnlyList<string>>? getTitleErrors = null)
        where TViewModel : class
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        ArgumentNullException.ThrowIfNull(getTitle);
        ArgumentNullException.ThrowIfNull(setTitle);
        ArgumentNullException.ThrowIfNull(getSubmitCommand);
        IAsyncRelayCommand command = getSubmitCommand(viewModel) ??
            throw new ArgumentException("The generated SubmitCommand cannot be null.", nameof(getSubmitCommand));
        return new(
            sessionId,
            viewModel,
            getTitle,
            setTitle,
            getTitleErrors ?? (static _ => Array.Empty<string>()),
            command,
            command);
    }
}

/// <summary>
/// Projects the approved generated <c>Title</c> and <c>SubmitCommand</c> members by direct access.
/// </summary>
/// <typeparam name="TViewModel">The concrete CommunityToolkit-generated ViewModel type.</typeparam>
/// <remarks>
/// This adapter never discovers or resolves members. Consumer or generated code supplies closed
/// delegates that compile to direct calls to the generated members.
/// </remarks>
public sealed class CommunityToolkitFlowProjection<TViewModel> : IAsyncDisposable
    where TViewModel : class
{
    private readonly object _gate = new();
    private readonly FlowSessionId _sessionId;
    private readonly TViewModel _viewModel;
    private readonly Func<TViewModel, string?> _getTitle;
    private readonly Action<TViewModel, string?> _setTitle;
    private readonly Func<TViewModel, IReadOnlyList<string>> _getTitleErrors;
    private readonly IRelayCommand _submitCommand;
    private readonly IAsyncRelayCommand? _asyncSubmitCommand;
    private readonly List<Action<CommunityToolkitFlowProjectionSnapshot>> _subscribers = [];
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private TaskCompletionSource? _idle;
    private Task? _disposeTask;
    private long _sequence;
    private int _activeDispatches;
    private int _suppressProducerNotifications;
    private bool _disposing;

    internal CommunityToolkitFlowProjection(
        FlowSessionId sessionId,
        TViewModel viewModel,
        Func<TViewModel, string?> getTitle,
        Action<TViewModel, string?> setTitle,
        Func<TViewModel, IReadOnlyList<string>> getTitleErrors,
        IRelayCommand submitCommand,
        IAsyncRelayCommand? asyncSubmitCommand)
    {
        _sessionId = sessionId;
        _viewModel = viewModel;
        _getTitle = getTitle;
        _setTitle = setTitle;
        _getTitleErrors = getTitleErrors;
        _submitCommand = submitCommand;
        _asyncSubmitCommand = asyncSubmitCommand;
        SubscribeToProducer();
    }

    /// <summary>Gets the authoritative snapshot through closed generated-member delegates.</summary>
    public CommunityToolkitFlowProjectionSnapshot GetSnapshot()
    {
        ThrowIfDisposing();
        return CaptureSnapshot();
    }

    /// <summary>Subscribes to complete immutable state observations.</summary>
    /// <returns>An exactly-once subscription lease owned by the caller.</returns>
    public IDisposable Subscribe(Action<CommunityToolkitFlowProjectionSnapshot> subscriber)
    {
        ArgumentNullException.ThrowIfNull(subscriber);
        lock (_gate)
        {
            ThrowIfDisposingLocked();
            _subscribers.Add(subscriber);
        }

        return new Subscription(this, subscriber);
    }

    /// <summary>Sets the generated <c>Title</c> when the supplied session remains authoritative.</summary>
    public ValueTask<CommunityToolkitFlowDispatchResult> SetTitleAsync(
        FlowSessionId authority,
        string? title,
        CancellationToken cancellationToken = default)
    {
        EnterDispatch();
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (authority != _sessionId)
            {
                return ValueTask.FromResult(new CommunityToolkitFlowDispatchResult(
                    CommunityToolkitFlowDispatchStatus.StaleSession,
                    CaptureSnapshot()));
            }

            Interlocked.Increment(ref _suppressProducerNotifications);
            try
            {
                _setTitle(_viewModel, title);
            }
            finally
            {
                Interlocked.Decrement(ref _suppressProducerNotifications);
            }

            CommunityToolkitFlowProjectionSnapshot snapshot = PublishSnapshot();
            return ValueTask.FromResult(new CommunityToolkitFlowDispatchResult(
                CommunityToolkitFlowDispatchStatus.Committed,
                snapshot));
        }
        finally
        {
            ExitDispatch();
        }
    }

    /// <summary>
    /// Executes the generated <c>SubmitCommand</c>, forwarding cancellation to async relay commands.
    /// </summary>
    public async ValueTask<CommunityToolkitFlowDispatchResult> SubmitAsync(
        FlowSessionId authority,
        CancellationToken cancellationToken = default)
    {
        EnterDispatch();
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (authority != _sessionId)
            {
                return new(
                    CommunityToolkitFlowDispatchStatus.StaleSession,
                    CaptureSnapshot());
            }

            if (!_submitCommand.CanExecute(null))
            {
                return new(
                    CommunityToolkitFlowDispatchStatus.CannotExecute,
                    CaptureSnapshot());
            }

            if (_asyncSubmitCommand is null)
            {
                Interlocked.Increment(ref _suppressProducerNotifications);
                try
                {
                    _submitCommand.Execute(null);
                }
                finally
                {
                    Interlocked.Decrement(ref _suppressProducerNotifications);
                }
            }
            else
            {
                using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken,
                    _lifetimeCancellation.Token);
                using CancellationTokenRegistration registration = linkedCancellation.Token.Register(
                    static state => ((IAsyncRelayCommand)state!).Cancel(),
                    _asyncSubmitCommand);
                Task execution = _asyncSubmitCommand.ExecuteAsync(null);
                PublishSnapshot();
                await execution.ConfigureAwait(false);
            }

            return new(
                CommunityToolkitFlowDispatchStatus.Committed,
                PublishSnapshot());
        }
        finally
        {
            ExitDispatch();
        }
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        TaskCompletionSource completion;
        Task waitForDispatches;
        lock (_gate)
        {
            if (_disposeTask is not null)
            {
                return new ValueTask(_disposeTask);
            }

            _disposing = true;
            UnsubscribeFromProducer();
            _subscribers.Clear();
            if (_activeDispatches == 0)
            {
                waitForDispatches = Task.CompletedTask;
            }
            else
            {
                _idle ??= new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                waitForDispatches = _idle.Task;
            }

            completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _disposeTask = completion.Task;
        }

        _lifetimeCancellation.Cancel();
        _asyncSubmitCommand?.Cancel();
        _ = CompleteDisposalAsync(waitForDispatches, completion);
        return new ValueTask(_disposeTask);
    }

    private async Task CompleteDisposalAsync(
        Task waitForDispatches,
        TaskCompletionSource completion)
    {
        try
        {
            await waitForDispatches.ConfigureAwait(false);
            _lifetimeCancellation.Dispose();
            completion.TrySetResult();
        }
        catch (Exception exception)
        {
            completion.TrySetException(exception);
        }
    }

    private void SubscribeToProducer()
    {
        if (_viewModel is INotifyPropertyChanged notifyingViewModel)
        {
            notifyingViewModel.PropertyChanged += OnProducerPropertyChanged;
        }

        if (_viewModel is INotifyDataErrorInfo validation)
        {
            validation.ErrorsChanged += OnProducerErrorsChanged;
        }

        _submitCommand.CanExecuteChanged += OnCommandCanExecuteChanged;
        if (_asyncSubmitCommand is INotifyPropertyChanged notifyingCommand)
        {
            notifyingCommand.PropertyChanged += OnCommandPropertyChanged;
        }
    }

    private void UnsubscribeFromProducer()
    {
        if (_viewModel is INotifyPropertyChanged notifyingViewModel)
        {
            notifyingViewModel.PropertyChanged -= OnProducerPropertyChanged;
        }

        if (_viewModel is INotifyDataErrorInfo validation)
        {
            validation.ErrorsChanged -= OnProducerErrorsChanged;
        }

        _submitCommand.CanExecuteChanged -= OnCommandCanExecuteChanged;
        if (_asyncSubmitCommand is INotifyPropertyChanged notifyingCommand)
        {
            notifyingCommand.PropertyChanged -= OnCommandPropertyChanged;
        }
    }

    private void OnProducerPropertyChanged(object? sender, PropertyChangedEventArgs e) => TryPublishSnapshot();

    private void OnProducerErrorsChanged(object? sender, DataErrorsChangedEventArgs e) => TryPublishSnapshot();

    private void OnCommandCanExecuteChanged(object? sender, EventArgs e) => TryPublishSnapshot();

    private void OnCommandPropertyChanged(object? sender, PropertyChangedEventArgs e) => TryPublishSnapshot();

    private void TryPublishSnapshot()
    {
        if (Volatile.Read(ref _suppressProducerNotifications) != 0)
        {
            return;
        }

        lock (_gate)
        {
            if (_disposing)
            {
                return;
            }
        }

        PublishSnapshot();
    }

    private CommunityToolkitFlowProjectionSnapshot PublishSnapshot()
    {
        CommunityToolkitFlowProjectionSnapshot snapshot = CaptureSnapshot(incrementSequence: true);
        Action<CommunityToolkitFlowProjectionSnapshot>[] subscribers;
        lock (_gate)
        {
            if (_disposing)
            {
                return snapshot;
            }

            subscribers = [.. _subscribers];
        }

        foreach (Action<CommunityToolkitFlowProjectionSnapshot> subscriber in subscribers)
        {
            try
            {
                subscriber(snapshot);
            }
            catch
            {
                // Projection observers cannot change Flow authority or command completion.
            }
        }

        return snapshot;
    }

    private CommunityToolkitFlowProjectionSnapshot CaptureSnapshot(bool incrementSequence = false)
    {
        long sequence = incrementSequence
            ? Interlocked.Increment(ref _sequence)
            : Interlocked.Read(ref _sequence);
        return new(
            _sessionId,
            sequence,
            _getTitle(_viewModel),
            _getTitleErrors(_viewModel),
            new CommunityToolkitFlowCommandState(
                _submitCommand.CanExecute(null),
                _asyncSubmitCommand?.IsRunning ?? false));
    }

    private void EnterDispatch()
    {
        lock (_gate)
        {
            ThrowIfDisposingLocked();
            _activeDispatches++;
        }
    }

    private void ExitDispatch()
    {
        TaskCompletionSource? idle = null;
        lock (_gate)
        {
            _activeDispatches--;
            if (_disposing && _activeDispatches == 0)
            {
                idle = _idle;
            }
        }

        idle?.TrySetResult();
    }

    private void RemoveSubscriber(Action<CommunityToolkitFlowProjectionSnapshot> subscriber)
    {
        lock (_gate)
        {
            _subscribers.Remove(subscriber);
        }
    }

    private void ThrowIfDisposing()
    {
        lock (_gate)
        {
            ThrowIfDisposingLocked();
        }
    }

    private void ThrowIfDisposingLocked() =>
        ObjectDisposedException.ThrowIf(_disposing, this);

    private sealed class Subscription : IDisposable
    {
        private CommunityToolkitFlowProjection<TViewModel>? _owner;
        private Action<CommunityToolkitFlowProjectionSnapshot>? _subscriber;

        public Subscription(
            CommunityToolkitFlowProjection<TViewModel> owner,
            Action<CommunityToolkitFlowProjectionSnapshot> subscriber)
        {
            _owner = owner;
            _subscriber = subscriber;
        }

        public void Dispose()
        {
            CommunityToolkitFlowProjection<TViewModel>? owner =
                Interlocked.Exchange(ref _owner, null);
            Action<CommunityToolkitFlowProjectionSnapshot>? subscriber =
                Interlocked.Exchange(ref _subscriber, null);
            if (owner is not null && subscriber is not null)
            {
                owner.RemoveSubscriber(subscriber);
            }
        }
    }
}
