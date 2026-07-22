using System;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using WebUIToolkit.MVVM.Flow;

namespace WebUIToolkit.MVVM.Dialogs;

internal readonly record struct DialogCompletion<TResult>(
    DialogOutcome<TResult> Outcome,
    DialogCloseReason Reason);

internal sealed class DialogController<TResult> : IDialogController<TResult>, IDisposable
{
    private const int Pending = 0;
    private const int Claimed = 1;
    private const int Completed = 2;

    private readonly DialogKey _dialog;
    private readonly FlowSessionId _sessionId;
    private readonly object _guardGate = new();
    private readonly CancellationTokenSource _guardShutdown = new();
    private readonly TaskCompletionSource<DialogCompletion<TResult>> _completion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private IDialogCloseGuard<TResult>? _guard;
    private TaskCompletionSource? _guardDrain;
    private int _configured;
    private int _ready;
    private int _state;

    internal DialogController(DialogKey dialog, FlowSessionId sessionId)
    {
        _dialog = dialog;
        _sessionId = sessionId;
    }

    public bool IsCompletionRequested => Volatile.Read(ref _state) != Pending;

    internal Task<DialogCompletion<TResult>> Completion => _completion.Task;

    internal Task GuardDrained
    {
        get
        {
            lock (_guardGate)
            {
                return _guardDrain?.Task ?? Task.CompletedTask;
            }
        }
    }

    public ValueTask<bool> CompleteAsync(
        TResult result,
        CancellationToken cancellationToken = default) =>
        RequestAsync(
            new DialogCompletion<TResult>(DialogOutcome<TResult>.Completed(result), DialogCloseReason.Complete),
            result,
            bypassGuard: false,
            requireReady: true,
            cancellationToken);

    public ValueTask<bool> CancelAsync(CancellationToken cancellationToken = default) =>
        RequestAsync(
            new DialogCompletion<TResult>(DialogOutcome<TResult>.Cancelled(), DialogCloseReason.Cancel),
            default,
            bypassGuard: false,
            requireReady: true,
            cancellationToken);

    public ValueTask<bool> DismissAsync(CancellationToken cancellationToken = default) =>
        RequestAsync(
            new DialogCompletion<TResult>(DialogOutcome<TResult>.Dismissed(), DialogCloseReason.Dismiss),
            default,
            bypassGuard: false,
            requireReady: true,
            cancellationToken);

    internal void ConfigureGuard(IDialogCloseGuard<TResult>? guard)
    {
        if (Volatile.Read(ref _configured) != 0)
        {
            throw new InvalidOperationException("The dialog close guard has already been configured.");
        }

        _guard = guard;
        Volatile.Write(ref _configured, 1);
    }

    internal void EnableRequests() => Volatile.Write(ref _ready, 1);

    internal ValueTask<bool> RequestCallerCancellationAsync() =>
        RequestAsync(
            new DialogCompletion<TResult>(DialogOutcome<TResult>.Cancelled(), DialogCloseReason.Cancel),
            default,
            bypassGuard: false,
            requireReady: false,
            CancellationToken.None);

    internal ValueTask<bool> RequestShutdownAsync()
    {
        int previous = Interlocked.Exchange(ref _state, Completed);
        if (previous == Completed)
        {
            return new ValueTask<bool>(false);
        }

        bool accepted = _completion.TrySetResult(
            new DialogCompletion<TResult>(DialogOutcome<TResult>.Cancelled(), DialogCloseReason.Shutdown));
        try
        {
            _guardShutdown.Cancel(throwOnFirstException: false);
        }
        catch (AggregateException)
        {
            // The guard observes cancellation through its token. Its own cancellation
            // callbacks cannot overturn the already accepted shutdown outcome.
        }

        return new ValueTask<bool>(accepted);
    }

    public void Dispose() => _guardShutdown.Dispose();

    private async ValueTask<bool> RequestAsync(
        DialogCompletion<TResult> completion,
        TResult? result,
        bool bypassGuard,
        bool requireReady,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (Volatile.Read(ref _configured) == 0)
        {
            throw new InvalidOperationException(
                "The dialog controller cannot accept requests until its ViewModel and close guard are configured.");
        }

        if (requireReady && Volatile.Read(ref _ready) == 0)
        {
            throw new InvalidOperationException(
                "The dialog controller cannot accept presentation requests before the dialog opens.");
        }

        if (Interlocked.CompareExchange(ref _state, Claimed, Pending) != Pending)
        {
            return false;
        }

        try
        {
            IDialogCloseGuard<TResult>? guard = _guard;
            if (!bypassGuard && guard is not null)
            {
                CancellationTokenSource? guardCancellation = BeginGuard(cancellationToken);
                if (guardCancellation is null)
                {
                    return false;
                }

                DialogCloseGuardContext<TResult> context = new(
                    _dialog,
                    _sessionId,
                    completion.Reason,
                    result);
                bool mayClose;
                try
                {
                    mayClose = await guard.CanCloseAsync(context, guardCancellation.Token)
                        .ConfigureAwait(false);
                }
                finally
                {
                    guardCancellation.Dispose();
                    EndGuard();
                }

                if (!mayClose)
                {
                    Interlocked.CompareExchange(ref _state, Pending, Claimed);
                    return false;
                }
            }

            if (Interlocked.CompareExchange(ref _state, Completed, Claimed) != Claimed)
            {
                return false;
            }

            return _completion.TrySetResult(completion);
        }
        catch (Exception exception)
        {
            if (Interlocked.CompareExchange(ref _state, Completed, Claimed) != Claimed)
            {
                return false;
            }

            _completion.TrySetException(exception);
            ExceptionDispatchInfo.Capture(exception).Throw();
            return false;
        }
    }

    private CancellationTokenSource? BeginGuard(CancellationToken cancellationToken)
    {
        lock (_guardGate)
        {
            if (Volatile.Read(ref _state) != Claimed)
            {
                return null;
            }

            _guardDrain = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            return CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                _guardShutdown.Token);
        }
    }

    private void EndGuard()
    {
        TaskCompletionSource? drain;
        lock (_guardGate)
        {
            drain = _guardDrain;
            _guardDrain = null;
        }

        drain?.TrySetResult();
    }
}
