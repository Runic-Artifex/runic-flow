using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace RunicFlow.Tests;

internal sealed class ManualTimeProvider(DateTimeOffset initialUtcNow) : TimeProvider
{
    private readonly object _gate = new();
    private readonly List<ManualTimer> _timers = [];
    private DateTimeOffset _utcNow = initialUtcNow;

    public override DateTimeOffset GetUtcNow()
    {
        lock (_gate)
        {
            return _utcNow;
        }
    }

    public override ITimer CreateTimer(
        TimerCallback callback,
        object? state,
        TimeSpan dueTime,
        TimeSpan period)
    {
        ArgumentNullException.ThrowIfNull(callback);
        var timer = new ManualTimer(this, callback, state);
        lock (_gate)
        {
            _timers.Add(timer);
            timer.ChangeCore(dueTime, period, _utcNow);
        }

        return timer;
    }

    public void Advance(TimeSpan duration)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(duration, TimeSpan.Zero);
        List<(TimerCallback Callback, object? State)> callbacks = [];
        lock (_gate)
        {
            _utcNow += duration;
            foreach (ManualTimer timer in _timers.ToArray())
            {
                timer.CollectDueCallbacks(_utcNow, callbacks);
            }
        }

        foreach ((TimerCallback callback, object? state) in callbacks)
        {
            callback(state);
        }
    }

    private void Remove(ManualTimer timer)
    {
        lock (_gate)
        {
            _timers.Remove(timer);
        }
    }

    private sealed class ManualTimer(
        ManualTimeProvider provider,
        TimerCallback callback,
        object? state) : ITimer
    {
        private DateTimeOffset? _next;
        private TimeSpan _period;
        private bool _disposed;

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            lock (provider._gate)
            {
                if (_disposed)
                {
                    return false;
                }

                ChangeCore(dueTime, period, provider._utcNow);
                return true;
            }
        }

        public void Dispose()
        {
            lock (provider._gate)
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
                _next = null;
                provider.Remove(this);
            }
        }

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }

        internal void ChangeCore(TimeSpan dueTime, TimeSpan period, DateTimeOffset utcNow)
        {
            Validate(dueTime, nameof(dueTime));
            Validate(period, nameof(period));
            _period = period;
            _next = dueTime == Timeout.InfiniteTimeSpan ? null : utcNow + dueTime;
        }

        internal void CollectDueCallbacks(
            DateTimeOffset utcNow,
            List<(TimerCallback Callback, object? State)> callbacks)
        {
            if (_disposed || _next is null || _next > utcNow)
            {
                return;
            }

            callbacks.Add((callback, state));
            _next = _period == Timeout.InfiniteTimeSpan ? null : utcNow + _period;
        }

        private static void Validate(TimeSpan duration, string parameterName)
        {
            if (duration < TimeSpan.Zero && duration != Timeout.InfiniteTimeSpan)
            {
                throw new ArgumentOutOfRangeException(parameterName);
            }
        }
    }
}
