using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace RunicFlow.Operations;

internal sealed class SlotCoordinator(string name)
{
    private readonly object _sync = new();
    private readonly HashSet<SlotAdmission> _active = [];
    private readonly Queue<Waiter> _waiters = [];

    public string Name { get; } = name;

    public bool IsEmpty
    {
        get
        {
            lock (_sync)
            {
                return _active.Count == 0 && _waiters.Count == 0;
            }
        }
    }

    public SlotAcquireResult Acquire(
        OperationRequest request,
        OperationCancellation executionCancellation,
        Action<SlotCoordinator> emptyCallback,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_sync)
        {
            if (request.Concurrency == OperationConcurrency.Reject && (_active.Count != 0 || _waiters.Count != 0))
            {
                throw new OperationBusyException(
                    $"Operation slot '{Name}' is busy.",
                    request.Key,
                    Name);
            }

            if (request.Concurrency == OperationConcurrency.Allow ||
                (request.Concurrency == OperationConcurrency.Reject && _active.Count == 0) ||
                (_active.Count == 0 && _waiters.Count == 0))
            {
                SlotAdmission admission = new(this, executionCancellation, emptyCallback);
                _active.Add(admission);
                return new SlotAcquireResult(ValueTask.FromResult(admission), []);
            }

            SlotAdmission[] cancelAfterLocks = request.Concurrency == OperationConcurrency.CancelPrevious
                ? [.. _active]
                : [];

            Waiter waiter = new(this, executionCancellation, emptyCallback, cancellationToken);
            _waiters.Enqueue(waiter);
            waiter.RegisterCancellation();
            return new SlotAcquireResult(new ValueTask<SlotAdmission>(waiter.Task), cancelAfterLocks);
        }
    }

    public void Release(SlotAdmission admission, Action<SlotCoordinator> emptyCallback)
    {
        bool empty;
        lock (_sync)
        {
            if (!_active.Remove(admission))
            {
                return;
            }

            while (_active.Count == 0 && _waiters.Count > 0)
            {
                Waiter waiter = _waiters.Dequeue();
                if (waiter.TryAdmit(out SlotAdmission? next))
                {
                    _active.Add(next!);
                    break;
                }
            }

            empty = _active.Count == 0 && _waiters.Count == 0;
        }

        if (empty)
        {
            emptyCallback(this);
        }
    }

    private sealed class Waiter
    {
        private readonly SlotCoordinator _owner;
        private readonly OperationCancellation _executionCancellation;
        private readonly CancellationToken _cancellationToken;
        private readonly Action<SlotCoordinator> _emptyCallback;
        private readonly TaskCompletionSource<SlotAdmission> _completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private CancellationTokenRegistration _registration;
        private int _completed;

        public Waiter(
            SlotCoordinator owner,
            OperationCancellation executionCancellation,
            Action<SlotCoordinator> emptyCallback,
            CancellationToken cancellationToken)
        {
            _owner = owner;
            _executionCancellation = executionCancellation;
            _cancellationToken = cancellationToken;
            _emptyCallback = emptyCallback;
        }

        public Task<SlotAdmission> Task => _completion.Task;

        public void RegisterCancellation()
        {
            _registration = _cancellationToken.Register(static state => ((Waiter)state!).Cancel(), this);
            if (Volatile.Read(ref _completed) != 0)
            {
                _registration.Dispose();
            }
        }

        public bool TryAdmit(out SlotAdmission? admission)
        {
            admission = null;
            if (Interlocked.CompareExchange(ref _completed, 1, 0) != 0)
            {
                return false;
            }

            _registration.Dispose();
            admission = new SlotAdmission(_owner, _executionCancellation, _emptyCallback);
            _completion.TrySetResult(admission);
            return true;
        }

        private void Cancel()
        {
            if (Interlocked.CompareExchange(ref _completed, 1, 0) == 0)
            {
                _completion.TrySetCanceled(_cancellationToken);
            }
        }
    }
}

internal readonly record struct SlotAcquireResult(
    ValueTask<SlotAdmission> Admission,
    SlotAdmission[] CancelAfterLocks);

internal sealed class SlotAdmission : IDisposable
{
    private readonly SlotCoordinator _owner;
    private readonly OperationCancellation _executionCancellation;
    private Action<SlotCoordinator>? _emptyCallback;

    public SlotAdmission(
        SlotCoordinator owner,
        OperationCancellation executionCancellation,
        Action<SlotCoordinator> emptyCallback)
    {
        _owner = owner;
        _executionCancellation = executionCancellation;
        _emptyCallback = emptyCallback;
    }

    public void RequestCancellation()
    {
        if (_executionCancellation.TryReserve(OperationCancellationReason.Replaced))
        {
            _executionCancellation.Signal();
        }
    }

    public void Dispose()
    {
        Action<SlotCoordinator>? callback = Interlocked.Exchange(ref _emptyCallback, null);
        if (callback is not null)
        {
            _owner.Release(this, callback);
        }
    }
}

internal sealed class OperationCancellation : IDisposable
{
    private readonly object _sync = new();
    private readonly CancellationTokenSource _source = new();
    private readonly Action<OperationCancellationReason> _recordReason;
    private OperationCancellationReason _reason;
    private bool _disposeRequested;
    private bool _disposed;
    private bool _signalStarted;
    private bool _signalCompleted;

    public OperationCancellation(Action<OperationCancellationReason> recordReason)
    {
        _recordReason = recordReason;
    }

    public CancellationToken Token => _source.Token;

    public OperationCancellationReason Reason
    {
        get
        {
            lock (_sync)
            {
                return _reason;
            }
        }
    }

    public bool TryReserve(OperationCancellationReason reason)
    {
        lock (_sync)
        {
            if (_disposeRequested || _reason != OperationCancellationReason.None)
            {
                return false;
            }

            _reason = reason;
            _recordReason(reason);
            return true;
        }
    }

    public void Signal()
    {
        lock (_sync)
        {
            if (_reason == OperationCancellationReason.None || _signalStarted)
            {
                return;
            }

            _signalStarted = true;
        }

        try
        {
            _source.Cancel();
        }
        catch (AggregateException)
        {
            // A consumer cancellation callback cannot undo the accepted request
            // or destabilize slot cleanup and replacement admission.
        }
        finally
        {
            lock (_sync)
            {
                _signalCompleted = true;
                if (_disposeRequested)
                {
                    DisposeSource();
                }
            }
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposeRequested)
            {
                return;
            }

            _disposeRequested = true;
            if (_reason == OperationCancellationReason.None || _signalCompleted)
            {
                DisposeSource();
            }
        }
    }

    private void DisposeSource()
    {
        if (!_disposed)
        {
            _disposed = true;
            _source.Dispose();
        }
    }
}
