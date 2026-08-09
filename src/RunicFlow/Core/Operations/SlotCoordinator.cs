using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace RunicFlow.Operations;

internal sealed class SlotCoordinator(string name)
{
    private readonly object _sync = new();
    private readonly List<SlotAdmission> _active = [];
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
        OperationCancellation cancellation,
        Action<SlotCoordinator> onEmpty,
        CancellationToken cancellationToken)
    {
        lock (_sync)
        {
            if (request.Concurrency == OperationConcurrency.Reject && (_active.Count != 0 || _waiters.Count != 0))
            {
                throw new OperationBusyException($"Operation slot '{Name}' is busy.", request.Key, Name);
            }

            if (request.Concurrency == OperationConcurrency.Allow || _active.Count == 0)
            {
                var admission = new SlotAdmission(this, cancellation, onEmpty);
                _active.Add(admission);
                return new SlotAcquireResult(ValueTask.FromResult<SlotAdmission?>(admission), []);
            }

            var waiter = new Waiter(this, cancellation, onEmpty, cancellationToken);
            _waiters.Enqueue(waiter);
            SlotAdmission[] cancel = request.Concurrency == OperationConcurrency.CancelPrevious
                ? [.. _active]
                : [];
            return new SlotAcquireResult(new ValueTask<SlotAdmission?>(waiter.Task), cancel);
        }
    }

    public void Release(SlotAdmission admission, Action<SlotCoordinator> onEmpty)
    {
        Waiter? promote = null;
        bool empty;
        lock (_sync)
        {
            _active.Remove(admission);
            while (_active.Count == 0 && _waiters.Count > 0)
            {
                Waiter candidate = _waiters.Dequeue();
                if (!candidate.IsCancelled)
                {
                    promote = candidate;
                    break;
                }
            }

            if (promote is not null)
            {
                SlotAdmission next = promote.CreateAdmission();
                _active.Add(next);
                promote.Complete(next);
            }

            empty = _active.Count == 0 && _waiters.Count == 0;
        }

        if (empty)
        {
            onEmpty(this);
        }
    }

    private sealed class Waiter
    {
        private readonly SlotCoordinator _owner;
        private readonly OperationCancellation _cancellation;
        private readonly Action<SlotCoordinator> _onEmpty;
        private readonly TaskCompletionSource<SlotAdmission?> _completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly CancellationTokenRegistration _registration;

        public Waiter(
            SlotCoordinator owner,
            OperationCancellation cancellation,
            Action<SlotCoordinator> onEmpty,
            CancellationToken cancellationToken)
        {
            _owner = owner;
            _cancellation = cancellation;
            _onEmpty = onEmpty;
            _registration = cancellationToken.Register(
                static state => ((Waiter)state!).Cancel(),
                this);
        }

        public Task<SlotAdmission?> Task => _completion.Task;

        public bool IsCancelled => _completion.Task.IsCanceled;

        public SlotAdmission CreateAdmission() => new(_owner, _cancellation, _onEmpty);

        public void Complete(SlotAdmission admission)
        {
            _registration.Dispose();
            _completion.TrySetResult(admission);
        }

        private void Cancel()
        {
            _completion.TrySetCanceled();
            _registration.Dispose();
        }
    }
}

internal sealed record SlotAcquireResult(
    ValueTask<SlotAdmission?> Admission,
    IReadOnlyList<SlotAdmission> CancelAfterLocks);

internal sealed class SlotAdmission(
    SlotCoordinator owner,
    OperationCancellation cancellation,
    Action<SlotCoordinator> onEmpty) : IDisposable
{
    private int _disposed;

    public void RequestCancellation()
    {
        if (cancellation.TryReserve(OperationCancellationReason.Replaced))
        {
            cancellation.Signal();
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            owner.Release(this, onEmpty);
        }
    }
}

internal sealed class OperationCancellation : IDisposable
{
    private readonly CancellationTokenSource _source = new();
    private readonly Action<OperationCancellationReason> _recordReason;
    private int _reserved;

    public OperationCancellation(Action<OperationCancellationReason> recordReason) =>
        _recordReason = recordReason;

    public CancellationToken Token => _source.Token;

    public bool TryReserve(OperationCancellationReason reason)
    {
        if (Interlocked.CompareExchange(ref _reserved, 1, 0) != 0)
        {
            return false;
        }

        _recordReason(reason);
        return true;
    }

    public void Signal()
    {
        try
        {
            _source.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    public void Dispose() => _source.Dispose();
}
