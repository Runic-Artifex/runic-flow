using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using RunicFlow;

namespace RunicFlow.Tests;

internal static class SessionOwnershipTests
{
    public static ValueTask DescriptorFreezesMetadata()
    {
        var metadata = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["surface"] = "main",
        };
        var viewModel = new object();
        var descriptor = new FlowContentDescriptor(
            FlowSessionId.Create(),
            new ViewContract("views/home"),
            viewModel,
            typeof(object),
            metadata);

        metadata["surface"] = "changed";
        metadata["new"] = "value";

        TestAssert.True(ReferenceEquals(viewModel, descriptor.ViewModel));
        TestAssert.Equal(typeof(object), descriptor.DeclaredViewModelType);
        TestAssert.Equal(1, descriptor.Metadata.Count);
        TestAssert.Equal("main", descriptor.Metadata["surface"]);
        return ValueTask.CompletedTask;
    }

    public static async ValueTask PocoSmokeHasOrderedTeardown()
    {
        var trace = new List<string>();
        var viewModel = new DualRecordingDisposable("viewmodel", trace);
        var scope = new DualRecordingDisposable("scope", trace);
        var lease = new RecordingLease("lease", trace);
        var session = CreateSession("smoke", viewModel, scope, ownsViewModel: true);
        CancellationToken lifetime = session.Lifetime;
        using CancellationTokenRegistration registration = lifetime.Register(
            () => trace.Add("lifetime.cancel"));

        session.AttachPresenterLease(lease);
        await session.DisposeAsync().ConfigureAwait(false);

        TestAssert.SequenceEqual(
            [
                "lifetime.cancel",
                "lease.close",
                "lease.dispose",
                "viewmodel.dispose-async",
                "scope.dispose-async",
            ],
            trace);
        TestAssert.True(session.IsDisposalStarted);
        TestAssert.True(lifetime.IsCancellationRequested);
        TestAssert.Equal(0, viewModel.DisposeCount);
        TestAssert.Equal(1, viewModel.DisposeAsyncCount);
        TestAssert.Equal(0, scope.DisposeCount);
        TestAssert.Equal(1, scope.DisposeAsyncCount);
        TestAssert.Equal(1, lease.CloseCount);
        TestAssert.Equal(1, lease.DisposeAsyncCount);
    }

    public static async ValueTask ChildrenDisposeInReverseCreationOrder()
    {
        var trace = new List<string>();
        var parent = CreateSession(
            "parent",
            new object(),
            new AsyncRecordingDisposable("parent.scope", trace),
            ownsViewModel: false);
        CancellationToken parentLifetime = parent.Lifetime;
        var first = CreateSession(
            "first",
            new object(),
            new CallbackAsyncDisposable(
                "first.scope",
                trace,
                () => TestAssert.True(parentLifetime.IsCancellationRequested)),
            ownsViewModel: false);
        var second = CreateSession(
            "second",
            new object(),
            new CallbackAsyncDisposable(
                "second.scope",
                trace,
                () => TestAssert.True(parentLifetime.IsCancellationRequested)),
            ownsViewModel: false);
        CancellationToken firstLifetime = first.Lifetime;
        CancellationToken secondLifetime = second.Lifetime;

        parent.AddChild(first);
        parent.AddChild(second);
        await parent.DisposeAsync().ConfigureAwait(false);

        TestAssert.SequenceEqual(
            ["second.scope.dispose-async", "first.scope.dispose-async", "parent.scope.dispose-async"],
            trace);
        TestAssert.True(firstLifetime.IsCancellationRequested);
        TestAssert.True(secondLifetime.IsCancellationRequested);
    }

    public static async ValueTask ConcurrentDisposalIsExactlyOnce()
    {
        var trace = new List<string>();
        var viewModel = new DualRecordingDisposable("viewmodel", trace);
        var scope = new DualRecordingDisposable("scope", trace);
        var lease = new RecordingLease("lease", trace);
        var session = CreateSession("concurrent", viewModel, scope, ownsViewModel: true);
        session.AttachPresenterLease(lease);

        Task[] disposalAttempts = new Task[32];
        for (int index = 0; index < disposalAttempts.Length; index++)
        {
            disposalAttempts[index] = Task.Run(async () =>
                await session.DisposeAsync().ConfigureAwait(false));
        }

        await Task.WhenAll(disposalAttempts).ConfigureAwait(false);

        TestAssert.Equal(1, lease.CloseCount);
        TestAssert.Equal(1, lease.DisposeAsyncCount);
        TestAssert.Equal(1, viewModel.DisposeAsyncCount);
        TestAssert.Equal(1, scope.DisposeAsyncCount);
        TestAssert.Equal(0, viewModel.DisposeCount);
        TestAssert.Equal(0, scope.DisposeCount);
    }

    public static async ValueTask CleanupFailuresAreOrdered()
    {
        var trace = new List<string>();
        var viewModel = new ThrowingAsyncDisposable("viewmodel", trace);
        var scope = new AsyncRecordingDisposable("scope", trace);
        var lease = new FaultingCloseLease("lease", trace);
        var session = CreateSession("faulting-cleanup", viewModel, scope, ownsViewModel: true);
        FlowSessionId sessionId = session.Descriptor.SessionId;
        session.AttachPresenterLease(lease);

        FlowCleanupException exception = await TestAssert.ThrowsAsync<FlowCleanupException>(
            session.DisposeAsync).ConfigureAwait(false);

        TestAssert.Equal(sessionId, exception.SessionId);
        TestAssert.Equal(2, exception.Failures.Count);
        TestAssert.Equal("lease.close.failure", exception.Failures[0].Message);
        TestAssert.Equal("viewmodel.failure", exception.Failures[1].Message);
        if (exception.InnerException is not AggregateException aggregate)
        {
            throw new InvalidOperationException("Cleanup failures must be exposed through an aggregate inner exception.");
        }

        TestAssert.Equal(2, aggregate.InnerExceptions.Count);
        TestAssert.True(ReferenceEquals(exception.Failures[0], aggregate.InnerExceptions[0]));
        TestAssert.True(ReferenceEquals(exception.Failures[1], aggregate.InnerExceptions[1]));
        TestAssert.SequenceEqual(
            [
                "lease.close",
                "lease.dispose",
                "viewmodel.dispose-async",
                "scope.dispose-async",
            ],
            trace);

        FlowCleanupException repeated = await TestAssert.ThrowsAsync<FlowCleanupException>(
            session.DisposeAsync).ConfigureAwait(false);
        TestAssert.True(ReferenceEquals(exception, repeated));
        TestAssert.Equal(1, lease.CloseCount);
        TestAssert.Equal(1, lease.DisposeAsyncCount);
    }

    public static async ValueTask ScopeOwnedViewModelIsNotDisposedIndependently()
    {
        var trace = new List<string>();
        var viewModel = new DualRecordingDisposable("viewmodel", trace);
        var scope = new AsyncRecordingDisposable("scope", trace);
        var session = CreateSession("scope-owned", viewModel, scope, ownsViewModel: false);

        await session.DisposeAsync().ConfigureAwait(false);

        TestAssert.Equal(0, viewModel.DisposeCount);
        TestAssert.Equal(0, viewModel.DisposeAsyncCount);
        TestAssert.SequenceEqual(["scope.dispose-async"], trace);
    }

    public static async ValueTask SessionAcceptsOnlyOneLease()
    {
        var trace = new List<string>();
        var first = new RecordingLease("first", trace);
        var rejected = new RecordingLease("rejected", trace);
        var session = CreateSession(
            "single-lease",
            new object(),
            new AsyncRecordingDisposable("scope", trace),
            ownsViewModel: false);

        bool rejectedSecondLease = false;
        session.AttachPresenterLease(first);
        try
        {
            session.AttachPresenterLease(rejected);
        }
        catch (InvalidOperationException)
        {
            rejectedSecondLease = true;
        }

        await session.DisposeAsync().ConfigureAwait(false);

        TestAssert.True(rejectedSecondLease);
        TestAssert.Equal(1, first.CloseCount);
        TestAssert.Equal(1, first.DisposeAsyncCount);
        TestAssert.Equal(0, rejected.CloseCount);
        TestAssert.Equal(0, rejected.DisposeAsyncCount);
    }

    private static FlowContentSession CreateSession(
        string contract,
        object viewModel,
        object ownedScope,
        bool ownsViewModel) =>
        new(
            FlowSessionId.Create(),
            new ViewContract($"views/{contract}"),
            viewModel,
            viewModel.GetType(),
            metadata: null,
            ownedScope,
            ownsViewModel);

    private sealed class RecordingLease : IFlowPresentationLease
    {
        private readonly string _name;
        private readonly ICollection<string> _trace;
        private int _closeCount;
        private int _disposeAsyncCount;

        public RecordingLease(string name, ICollection<string> trace)
        {
            _name = name;
            _trace = trace;
        }

        public int CloseCount => Volatile.Read(ref _closeCount);

        public int DisposeAsyncCount => Volatile.Read(ref _disposeAsyncCount);

        public ValueTask CloseAsync(CancellationToken cancellationToken)
        {
            TestAssert.False(cancellationToken.IsCancellationRequested);
            Interlocked.Increment(ref _closeCount);
            _trace.Add($"{_name}.close");
            return ValueTask.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            Interlocked.Increment(ref _disposeAsyncCount);
            _trace.Add($"{_name}.dispose");
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FaultingCloseLease : IFlowPresentationLease
    {
        private readonly string _name;
        private readonly ICollection<string> _trace;
        private int _closeCount;
        private int _disposeAsyncCount;

        public FaultingCloseLease(string name, ICollection<string> trace)
        {
            _name = name;
            _trace = trace;
        }

        public int CloseCount => Volatile.Read(ref _closeCount);

        public int DisposeAsyncCount => Volatile.Read(ref _disposeAsyncCount);

        public ValueTask CloseAsync(CancellationToken cancellationToken)
        {
            TestAssert.False(cancellationToken.IsCancellationRequested);
            Interlocked.Increment(ref _closeCount);
            _trace.Add($"{_name}.close");
            return ValueTask.FromException(new InvalidOperationException($"{_name}.close.failure"));
        }

        public ValueTask DisposeAsync()
        {
            Interlocked.Increment(ref _disposeAsyncCount);
            _trace.Add($"{_name}.dispose");
            return ValueTask.CompletedTask;
        }
    }

    private sealed class DualRecordingDisposable : IDisposable, IAsyncDisposable
    {
        private readonly string _name;
        private readonly ICollection<string> _trace;
        private int _disposeCount;
        private int _disposeAsyncCount;

        public DualRecordingDisposable(string name, ICollection<string> trace)
        {
            _name = name;
            _trace = trace;
        }

        public int DisposeCount => Volatile.Read(ref _disposeCount);

        public int DisposeAsyncCount => Volatile.Read(ref _disposeAsyncCount);

        public void Dispose()
        {
            Interlocked.Increment(ref _disposeCount);
            _trace.Add($"{_name}.dispose");
        }

        public ValueTask DisposeAsync()
        {
            Interlocked.Increment(ref _disposeAsyncCount);
            _trace.Add($"{_name}.dispose-async");
            return ValueTask.CompletedTask;
        }
    }

    private sealed class AsyncRecordingDisposable : IAsyncDisposable
    {
        private readonly string _name;
        private readonly ICollection<string> _trace;

        public AsyncRecordingDisposable(string name, ICollection<string> trace)
        {
            _name = name;
            _trace = trace;
        }

        public ValueTask DisposeAsync()
        {
            _trace.Add($"{_name}.dispose-async");
            return ValueTask.CompletedTask;
        }
    }

    private sealed class CallbackAsyncDisposable : IAsyncDisposable
    {
        private readonly string _name;
        private readonly ICollection<string> _trace;
        private readonly Action _callback;

        public CallbackAsyncDisposable(string name, ICollection<string> trace, Action callback)
        {
            _name = name;
            _trace = trace;
            _callback = callback;
        }

        public ValueTask DisposeAsync()
        {
            _callback();
            _trace.Add($"{_name}.dispose-async");
            return ValueTask.CompletedTask;
        }
    }

    private sealed class ThrowingAsyncDisposable : IAsyncDisposable
    {
        private readonly string _name;
        private readonly ICollection<string> _trace;

        public ThrowingAsyncDisposable(string name, ICollection<string> trace)
        {
            _name = name;
            _trace = trace;
        }

        public ValueTask DisposeAsync()
        {
            _trace.Add($"{_name}.dispose-async");
            return ValueTask.FromException(new InvalidOperationException($"{_name}.failure"));
        }
    }
}
