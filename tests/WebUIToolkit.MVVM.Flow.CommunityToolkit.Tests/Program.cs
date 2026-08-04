using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WebUIToolkit.MVVM.Flow;
using WebUIToolkit.MVVM.Flow.CommunityToolkit;
using WebUIToolkit.MVVM.Flow.Generators;

namespace WebUIToolkit.MVVM.Flow.CommunityToolkit.Tests;

internal static partial class Program
{
    private static int _passed;

    public static async Task<int> Main()
    {
        try
        {
            await RunAsync(
                "flow.projection.communitytoolkit.contract.v1",
                ContractMatchesApprovedHandoffAsync);
            await RunAsync(
                "flow.projection.communitytoolkit.title.v1",
                TitleProjectionAsync);
            await RunAsync(
                "flow.projection.communitytoolkit.submit-command.v1",
                SubmitCommandProjectionAsync);
            await RunAsync(
                "flow.projection.communitytoolkit.async-command.v1",
                AsyncCommandCancellationAndDisposalAsync);
            Console.WriteLine($"PASS: {_passed} Flow CommunityToolkit fixtures");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"FAIL after {_passed} Flow CommunityToolkit fixtures: {exception}");
            return 1;
        }
    }

    private static Task ContractMatchesApprovedHandoffAsync()
    {
        Equal(
            CommunityToolkitProjectionHandoff.ProjectionSchemaVersion,
            CommunityToolkitFlowProjectionContract.SchemaVersion);
        Equal(
            CommunityToolkitProjectionHandoff.ProjectionAdapterIdentity,
            CommunityToolkitFlowProjectionContract.AdapterIdentity);
        Equal(
            CommunityToolkitProjectionHandoff.CommunityToolkitPackageVersion,
            CommunityToolkitFlowProjectionContract.CommunityToolkitPackageVersion);
        Equal(
            CommunityToolkitProjectionHandoff.FixtureMappings.Count,
            CommunityToolkitFlowProjectionContract.Members.Count);
        for (int index = 0; index < CommunityToolkitProjectionHandoff.FixtureMappings.Count; index++)
        {
            FlowProjectionFixtureMapping handoff =
                CommunityToolkitProjectionHandoff.FixtureMappings[index];
            CommunityToolkitFlowProjectionMember runtime =
                CommunityToolkitFlowProjectionContract.Members[index];
            Equal(handoff.CommunityToolkitProofFixtureId, runtime.ProducerFixtureId);
            Equal(handoff.FlowProjectionFixtureId, runtime.ProjectionFixtureId);
            Equal(handoff.MemberId, runtime.MemberId);
            Equal(handoff.GeneratedMemberName, runtime.GeneratedMemberName);
            Equal((int)handoff.MemberKind, (int)runtime.Kind);
            Equal(handoff.IncludesValidation, runtime.IncludesValidation);
        }

        string[] coreReferences = typeof(FlowSessionId).Assembly
            .GetReferencedAssemblies()
            .Select(static reference => reference.Name!)
            .ToArray();
        False(coreReferences.Contains("CommunityToolkit.Mvvm", StringComparer.Ordinal));
        string[] adapterReferences = typeof(CommunityToolkitFlowProjectionContract).Assembly
            .GetReferencedAssemblies()
            .Select(static reference => reference.Name!)
            .ToArray();
        True(adapterReferences.Contains("CommunityToolkit.Mvvm", StringComparer.Ordinal));
        True(adapterReferences.Contains("WebUIToolkit.MVVM.Flow", StringComparer.Ordinal));
        False(adapterReferences.Contains("WebUIToolkit.MVVM.Flow.Generators", StringComparer.Ordinal));

        return Task.CompletedTask;
    }

    private static async Task TitleProjectionAsync()
    {
        FlowSessionId authority = FlowSessionId.Create();
        var viewModel = new SyncFixtureViewModel();
        await using CommunityToolkitFlowProjection<SyncFixtureViewModel> projection =
            CreateSyncProjection(authority, viewModel);
        var observations = new List<CommunityToolkitFlowProjectionSnapshot>();
        using IDisposable subscription = projection.Subscribe(observations.Add);

        CommunityToolkitFlowDispatchResult committed = await projection.SetTitleAsync(
            authority,
            null);
        Equal(CommunityToolkitFlowDispatchStatus.Committed, committed.Status);
        Equal<string?>(null, committed.Snapshot.Title);
        SequenceEqual(["required"], committed.Snapshot.TitleErrors);
        Equal(1, observations.Count);
        Equal(101, CommunityToolkitFlowProjectionContract.Members[0].MemberId);

        CommunityToolkitFlowDispatchResult stale = await projection.SetTitleAsync(
            FlowSessionId.Create(),
            "stale");
        Equal(CommunityToolkitFlowDispatchStatus.StaleSession, stale.Status);
        Equal<string?>(null, viewModel.Title);
        Equal(1, observations.Count);
    }

    private static async Task SubmitCommandProjectionAsync()
    {
        FlowSessionId authority = FlowSessionId.Create();
        var viewModel = new SyncFixtureViewModel();
        await using CommunityToolkitFlowProjection<SyncFixtureViewModel> projection =
            CreateSyncProjection(authority, viewModel);

        CommunityToolkitFlowDispatchResult submitted =
            await projection.SubmitAsync(authority);
        True(submitted.Committed);
        Equal(1, viewModel.SubmissionCount);
        False(submitted.Snapshot.SubmitCommand.IsRunning);
        Equal(102, CommunityToolkitFlowProjectionContract.Members[1].MemberId);

        viewModel.CanSubmit = false;
        CommunityToolkitFlowDispatchResult unavailable =
            await projection.SubmitAsync(authority);
        Equal(CommunityToolkitFlowDispatchStatus.CannotExecute, unavailable.Status);
        Equal(1, viewModel.SubmissionCount);

        CommunityToolkitFlowDispatchResult stale =
            await projection.SubmitAsync(FlowSessionId.Create());
        Equal(CommunityToolkitFlowDispatchStatus.StaleSession, stale.Status);
    }

    private static async Task AsyncCommandCancellationAndDisposalAsync()
    {
        FlowSessionId authority = FlowSessionId.Create();
        var viewModel = new AsyncFixtureViewModel();
        CommunityToolkitFlowProjection<AsyncFixtureViewModel> projection =
            CommunityToolkitFlowProjection.CreateAsync(
                authority,
                viewModel,
                static model => model.Title,
                static (model, value) => model.Title = value,
                static model => model.SubmitCommand);
        var observations = new List<CommunityToolkitFlowProjectionSnapshot>();
        using IDisposable subscription = projection.Subscribe(observations.Add);
        using var cancellation = new CancellationTokenSource();
        Task<CommunityToolkitFlowDispatchResult> dispatch = projection
            .SubmitAsync(authority, cancellation.Token)
            .AsTask();
        await viewModel.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        True(observations.Any(static snapshot => snapshot.SubmitCommand.IsRunning));
        cancellation.Cancel();
        await ThrowsAsync<OperationCanceledException>(async () =>
            await dispatch.ConfigureAwait(false));
        True(viewModel.Cancelled);

        await projection.DisposeAsync();
        await projection.DisposeAsync();
        await ThrowsAsync<ObjectDisposedException>(() =>
        {
            projection.GetSnapshot();
            return Task.CompletedTask;
        });

        int observationCount = observations.Count;
        viewModel.Title = "after-disposal";
        Equal(observationCount, observations.Count);

        var disposalViewModel = new AsyncFixtureViewModel();
        CommunityToolkitFlowProjection<AsyncFixtureViewModel> disposalProjection =
            CommunityToolkitFlowProjection.CreateAsync(
                authority,
                disposalViewModel,
                static model => model.Title,
                static (model, value) => model.Title = value,
                static model => model.SubmitCommand);
        Task<CommunityToolkitFlowDispatchResult> activeDispatch = disposalProjection
            .SubmitAsync(authority)
            .AsTask();
        await disposalViewModel.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Task firstDisposal = disposalProjection.DisposeAsync().AsTask();
        Task secondDisposal = disposalProjection.DisposeAsync().AsTask();
        await Task.WhenAll(firstDisposal, secondDisposal).WaitAsync(TimeSpan.FromSeconds(5));
        await ThrowsAsync<OperationCanceledException>(async () =>
            await activeDispatch.ConfigureAwait(false));
        True(disposalViewModel.Cancelled);
    }

    private static CommunityToolkitFlowProjection<SyncFixtureViewModel> CreateSyncProjection(
        FlowSessionId authority,
        SyncFixtureViewModel viewModel) =>
        CommunityToolkitFlowProjection.Create(
            authority,
            viewModel,
            static model => model.Title,
            static (model, value) => model.Title = value,
            static model => model.SubmitCommand,
            static model => model.Title is null ? ["required"] : Array.Empty<string>());

    private static async Task RunAsync(string fixtureId, Func<Task> test)
    {
        await test().ConfigureAwait(false);
        _passed++;
        Console.WriteLine($"PASS: {fixtureId}");
    }

    private static async Task ThrowsAsync<TException>(Func<Task> action)
        where TException : Exception
    {
        try
        {
            await action().ConfigureAwait(false);
        }
        catch (TException)
        {
            return;
        }

        throw new InvalidOperationException($"Expected {typeof(TException).Name}.");
    }

    private static void True(bool value)
    {
        if (!value)
        {
            throw new InvalidOperationException("Expected true.");
        }
    }

    private static void False(bool value) => True(!value);

    private static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"Expected '{expected}', got '{actual}'.");
        }
    }

    private static void SequenceEqual<T>(IReadOnlyList<T> expected, IReadOnlyList<T> actual)
    {
        Equal(expected.Count, actual.Count);
        for (int index = 0; index < expected.Count; index++)
        {
            Equal(expected[index], actual[index]);
        }
    }

    internal sealed partial class SyncFixtureViewModel : ObservableObject
    {
        [ObservableProperty]
        private string? title = "Before";

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(SubmitCommand))]
        private bool canSubmit = true;

        public int SubmissionCount { get; private set; }

        [RelayCommand(CanExecute = nameof(CanSubmit))]
        private void Submit() => SubmissionCount++;
    }

    internal sealed partial class AsyncFixtureViewModel : ObservableObject
    {
        [ObservableProperty]
        private string? title = "Before";

        public TaskCompletionSource Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool Cancelled { get; private set; }

        [RelayCommand]
        private async Task SubmitAsync(CancellationToken cancellationToken)
        {
            Started.TrySetResult();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                Cancelled = true;
                throw;
            }
        }
    }
}
