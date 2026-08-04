using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RunicFlow;
using RunicFlow.CommunityToolkit;

namespace RunicFlow.CommunityToolkit.PackageConsumer;

internal static partial class Program
{
    public static async Task<int> Main()
    {
        FlowSessionId authority = FlowSessionId.Create();
        var viewModel = new ConsumerViewModel();
        await using CommunityToolkitFlowProjection<ConsumerViewModel> projection =
            CommunityToolkitFlowProjection.CreateAsync(
                authority,
                viewModel,
                static model => model.Title,
                static (model, value) => model.Title = value,
                static model => model.SubmitCommand,
                static model => model.Title is null ? ["required"] : Array.Empty<string>());

        CommunityToolkitFlowDispatchResult title =
            await projection.SetTitleAsync(authority, null);
        var states = new List<CommunityToolkitFlowProjectionSnapshot>();
        using IDisposable subscription = projection.Subscribe(states.Add);
        using var cancellation = new CancellationTokenSource();
        Task<CommunityToolkitFlowDispatchResult> submit = projection
            .SubmitAsync(authority, cancellation.Token)
            .AsTask();
        await viewModel.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();
        bool cancelled;
        try
        {
            await submit.ConfigureAwait(false);
            cancelled = false;
        }
        catch (OperationCanceledException)
        {
            cancelled = true;
        }

        bool noGeneratorRuntime = AppDomain.CurrentDomain.GetAssemblies().All(static assembly =>
            !string.Equals(
                assembly.GetName().Name,
                "RunicFlow.Generators",
                StringComparison.Ordinal)) &&
            !File.Exists(Path.Combine(
                AppContext.BaseDirectory,
                "RunicFlow.Generators.dll"));
        bool succeeded = title.Committed
            && title.Snapshot.Title is null
            && title.Snapshot.TitleErrors.SequenceEqual(["required"], StringComparer.Ordinal)
            && states.Any(static state => state.SubmitCommand.IsRunning)
            && cancelled
            && viewModel.Cancelled
            && noGeneratorRuntime
            && CommunityToolkitFlowProjectionContract.SchemaVersion == 1
            && CommunityToolkitFlowProjectionContract.Members.Count == 2;
        if (!succeeded)
        {
            Console.Error.WriteLine("FAIL: packaged Flow CommunityToolkit projection consumer.");
            return 1;
        }

        Console.WriteLine("PASS: packaged Flow CommunityToolkit projection consumer.");
        return 0;
    }

    internal sealed partial class ConsumerViewModel : ObservableObject
    {
        [ObservableProperty]
        private string? title = "Before";

        public TaskCompletionSource Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool Cancelled { get; private set; }

        [RelayCommand(FlowExceptionsToTaskScheduler = true)]
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
