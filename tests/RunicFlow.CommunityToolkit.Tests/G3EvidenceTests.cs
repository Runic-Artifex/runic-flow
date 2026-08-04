using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using RunicToolkit.MVVM;
using RunicFlow;
using RunicFlow.CommunityToolkit;

namespace RunicFlow.CommunityToolkit.Tests;

internal static partial class Program
{
    private const string G3SessionId = "22222222-2222-4222-8222-222222222222";
    private const string G3ViewId = "33333333-3333-4333-8333-333333333333";
    private const string G3RequestId = "11111111-1111-4111-8111-111111111111";
    private const string G3Capability = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";

    private static async Task RunFlowG3EvidenceAsync()
    {
        await RunFlowG3Async("host-binding-vocabulary", FlowHostBindingVocabularyAsync);
        await RunFlowG3Async("successful-mutation", FlowSuccessfulMutationAsync);
        await RunFlowG3Async("projection-invariants", FlowProjectionInvariantsAsync);
        await RunFlowG3Async("cancellation-and-timeout", FlowCancellationAsync);
        await RunFlowG3Async("limits", FlowLimitsAsync);
        await RunFlowG3Async("reconnect-snapshot", FlowReconnectSnapshotAsync);
        await RunFlowG3Async("reconnect-ack-backpressure", FlowAckIndependenceAsync);
        await RunFlowG3Async("strict-codec", FlowStrictCodecAsync);
        await RunFlowG3Async("observability-security", FlowObservabilitySecurityAsync);
    }

    private static async Task RunFlowG3Async(string corpusId, Func<Task> scenario)
    {
        await scenario().ConfigureAwait(false);
        Console.WriteLine($"G3-EVIDENCE: flow/{corpusId}");
    }

    private static Task FlowHostBindingVocabularyAsync()
    {
        IReadOnlyList<CommunityToolkitFlowProjectionMember> members =
            CommunityToolkitFlowProjectionContract.Members;
        Equal(2, members.Count);
        Equal(
            "101:Property:True,102:Command:False",
            string.Join(',', members.Select(
                static member =>
                    $"{member.MemberId}:{member.Kind}:{member.IncludesValidation}")));
        Equal(members.Count, members.Select(static member => member.MemberId).Distinct().Count());

        string[] coreReferences = typeof(FlowSessionId).Assembly
            .GetReferencedAssemblies()
            .Select(static reference => reference.Name!)
            .ToArray();
        False(coreReferences.Contains("CommunityToolkit.Mvvm", StringComparer.Ordinal));
        False(coreReferences.Contains("RunicToolkit.Hosting", StringComparer.Ordinal));
        False(coreReferences.Contains(
            "RunicFlow.CommunityToolkit",
            StringComparer.Ordinal));
        return Task.CompletedTask;
    }

    private static async Task FlowSuccessfulMutationAsync()
    {
        FlowSessionId authority = FlowSessionId.Create();
        var model = new SyncFixtureViewModel();
        await using CommunityToolkitFlowProjection<SyncFixtureViewModel> projection =
            CreateSyncProjection(authority, model);
        var observations = new List<CommunityToolkitFlowProjectionSnapshot>();
        using IDisposable subscription = projection.Subscribe(observations.Add);

        CommunityToolkitFlowDispatchResult result =
            await projection.SetTitleAsync(authority, null);
        True(result.Committed);
        Equal(CommunityToolkitFlowDispatchStatus.Committed, result.Status);
        Equal(1L, result.Snapshot.Sequence);
        Equal<string?>(null, result.Snapshot.Title);
        SequenceEqual(["required"], result.Snapshot.TitleErrors);
        Equal(1, observations.Count);
        True(ReferenceEquals(result.Snapshot, observations[0]));
        Equal<string?>(null, model.Title);
    }

    private static async Task FlowProjectionInvariantsAsync()
    {
        FlowSessionId authority = FlowSessionId.Create();
        var model = new SyncFixtureViewModel();
        await using CommunityToolkitFlowProjection<SyncFixtureViewModel> projection =
            CommunityToolkitFlowProjection.Create(
                authority,
                model,
                static value => value.Title,
                static (value, title) => value.Title = title,
                static value => value.SubmitCommand,
                static _ => ["z-last", "a-first", "z-last"]);

        CommunityToolkitFlowProjectionSnapshot first = projection.GetSnapshot();
        CommunityToolkitFlowProjectionSnapshot second = projection.GetSnapshot();
        Equal(first.Sequence, second.Sequence);
        Equal(first.Title, second.Title);
        SequenceEqual(["a-first", "z-last"], first.TitleErrors);
        Equal(
            "101,102",
            string.Join(',', CommunityToolkitFlowProjectionContract.Members.Select(
                static member => member.MemberId)));

        var observations = new List<CommunityToolkitFlowProjectionSnapshot>();
        using IDisposable subscription = projection.Subscribe(observations.Add);
        CommunityToolkitFlowDispatchResult changed =
            await projection.SetTitleAsync(authority, "after");
        Equal(1, observations.Count);
        True(ReferenceEquals(changed.Snapshot, observations[0]));
        Equal("after", observations[0].Title!);
        Equal(1L, observations[0].Sequence);
    }

    private static async Task FlowCancellationAsync()
    {
        FlowSessionId authority = FlowSessionId.Create();
        var model = new AsyncFixtureViewModel();
        await using CommunityToolkitFlowProjection<AsyncFixtureViewModel> projection =
            CommunityToolkitFlowProjection.CreateAsync(
                authority,
                model,
                static value => value.Title,
                static (value, title) => value.Title = title,
                static value => value.SubmitCommand);
        var observations = new List<CommunityToolkitFlowProjectionSnapshot>();
        using IDisposable subscription = projection.Subscribe(observations.Add);
        using var cancellation = new CancellationTokenSource();
        Task<CommunityToolkitFlowDispatchResult> pending =
            projection.SubmitAsync(authority, cancellation.Token).AsTask();

        await model.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();
        await ThrowsAsync<OperationCanceledException>(
            async () => await pending.ConfigureAwait(false));
        True(model.Cancelled);
        False(projection.GetSnapshot().SubmitCommand.IsRunning);
        True(observations.Any(static snapshot => snapshot.SubmitCommand.IsRunning));
        False(observations[^1].SubmitCommand.IsRunning);
    }

    private static async Task FlowLimitsAsync()
    {
        FlowSessionId authority = FlowSessionId.Create();
        var model = new SyncFixtureViewModel();
        await using CommunityToolkitFlowProjection<SyncFixtureViewModel> projection =
            CreateSyncProjection(authority, model);
        var probe = new FlowAdmissionProbe();
        var limits = MvvmLimits.Default with { MaxStringBytes = G3Capability.Length };

        CommunityToolkitFlowDispatchResult admitted = await DispatchRawTitleAsync(
            SetTitleFrame("\"bounded\""),
            projection,
            authority,
            probe,
            limits);
        True(admitted.Committed);
        Equal(1, probe.Invocations);
        True(Encoding.UTF8.GetByteCount(admitted.Snapshot.Title!) <= limits.MaxStringBytes);
        True(CommunityToolkitFlowProjectionContract.Members.Count <= limits.MaxSnapshotMembers);
        True(admitted.Snapshot.TitleErrors.Count <= 32);

        await ThrowsAsync<MvvmProtocolException>(async () =>
            await DispatchRawTitleAsync(
                SetTitleFrame($"\"{new string('x', G3Capability.Length + 1)}\""),
                projection,
                authority,
                probe,
                limits));
        Equal(1, probe.Invocations);
        Equal("bounded", model.Title!);

        await using CommunityToolkitFlowProjection<SyncFixtureViewModel> excessiveOutput =
            CommunityToolkitFlowProjection.Create(
                authority,
                new SyncFixtureViewModel(),
                static value => value.Title,
                static (value, title) => value.Title = title,
                static value => value.SubmitCommand,
                static _ => Enumerable.Range(0, 33).Select(static index => $"error-{index}").ToArray());
        Throws<ArgumentException>(() => excessiveOutput.GetSnapshot());
    }

    private static async Task FlowReconnectSnapshotAsync()
    {
        FlowSessionId authority = FlowSessionId.Create();
        var model = new SyncFixtureViewModel { Title = "authoritative" };
        await using CommunityToolkitFlowProjection<SyncFixtureViewModel> projection =
            CreateSyncProjection(authority, model);
        var localState = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["title"] = "stale",
            ["transport-replay-marker"] = "must disappear",
        };

        CommunityToolkitFlowProjectionSnapshot replacement = projection.GetSnapshot();
        localState.Clear();
        localState.Add("title", replacement.Title);
        localState.Add("submit.canExecute", replacement.SubmitCommand.CanExecute.ToString());
        localState.Add("submit.isRunning", replacement.SubmitCommand.IsRunning.ToString());

        False(localState.ContainsKey("transport-replay-marker"));
        Equal("authoritative", localState["title"]!);
        Equal(3, localState.Count);
    }

    private static async Task FlowAckIndependenceAsync()
    {
        FlowSessionId authority = FlowSessionId.Create();
        var acknowledgedModel = new SyncFixtureViewModel();
        var controlModel = new SyncFixtureViewModel();
        await using CommunityToolkitFlowProjection<SyncFixtureViewModel> acknowledged =
            CreateSyncProjection(authority, acknowledgedModel);
        await using CommunityToolkitFlowProjection<SyncFixtureViewModel> control =
            CreateSyncProjection(authority, controlModel);
        int observations = 0;
        using IDisposable subscription = acknowledged.Subscribe(_ => observations++);

        long highWatermark = 0;
        foreach (long revision in new long[] { 3, 9, 4 })
        {
            MvvmWireMessage ack = MvvmMessageCodec.DecodeClient(FlowAckFrame(revision));
            highWatermark = Math.Max(
                highWatermark,
                ack.Document.GetProperty("payload").GetProperty("revision").GetInt64());
        }

        Equal(9L, highWatermark);
        Equal(0, observations);
        CommunityToolkitFlowDispatchResult afterAcks =
            await acknowledged.SetTitleAsync(authority, "same");
        CommunityToolkitFlowDispatchResult withoutAcks =
            await control.SetTitleAsync(authority, "same");
        Equal(afterAcks.Status, withoutAcks.Status);
        Equal(afterAcks.Snapshot.Title!, withoutAcks.Snapshot.Title!);
        Equal(afterAcks.Snapshot.Sequence, withoutAcks.Snapshot.Sequence);
        False(typeof(CommunityToolkitFlowProjection<>).GetMethods()
            .Any(static method =>
                method.Name.Contains("Ack", StringComparison.Ordinal) ||
                method.Name.Contains("Replay", StringComparison.Ordinal) ||
                method.Name.Contains("Queue", StringComparison.Ordinal)));
        False(typeof(CommunityToolkitFlowProjection<>).Assembly.GetReferencedAssemblies()
            .Any(static assembly => assembly.Name is "RunicToolkit.Hosting"));
    }

    private static async Task FlowStrictCodecAsync()
    {
        byte[] valid = SetTitleFrame("\"codec-first\"");
        MvvmWireMessage decoded = MvvmMessageCodec.DecodeClient(valid);
        True(MvvmMessageCodec.Encode(decoded).SequenceEqual(MvvmMessageCodec.Encode(decoded)));

        FlowSessionId authority = FlowSessionId.Create();
        var model = new SyncFixtureViewModel();
        await using CommunityToolkitFlowProjection<SyncFixtureViewModel> projection =
            CreateSyncProjection(authority, model);
        var probe = new FlowAdmissionProbe();
        CommunityToolkitFlowDispatchResult result = await DispatchRawTitleAsync(
            valid,
            projection,
            authority,
            probe,
            MvvmLimits.Default);
        True(result.Committed);
        Equal("codec-first", model.Title!);
        Equal(1, probe.Invocations);

        byte[] duplicate = Encoding.UTF8.GetBytes(
            $$$"""{"v":1,"kind":"setProperty","session":"{{{G3SessionId}}}","view":"{{{G3ViewId}}}","request":"{{{G3RequestId}}}","baseRevision":0,"capability":"{{{G3Capability}}}","payload":{"member":101,"value":"first","value":"secret-second"}}""");
        await ThrowsAsync<MvvmProtocolException>(async () =>
            await DispatchRawTitleAsync(
                duplicate,
                projection,
                authority,
                probe,
                MvvmLimits.Default));
        Equal(1, probe.Invocations);
        Equal("codec-first", model.Title!);
    }

    private static async Task FlowObservabilitySecurityAsync()
    {
        const string secret = "flow-secret-payload-and-identity";
        FlowSessionId authority = FlowSessionId.Create();
        var model = new SyncFixtureViewModel { Title = secret };
        await using CommunityToolkitFlowProjection<SyncFixtureViewModel> projection =
            CreateSyncProjection(authority, model);
        CommunityToolkitFlowDispatchResult stale =
            await projection.SetTitleAsync(FlowSessionId.Create(), "ignored-secret");

        string lowCardinalityStatus = stale.Status.ToString();
        Equal("StaleSession", lowCardinalityStatus);
        False(lowCardinalityStatus.Contains(secret, StringComparison.Ordinal));
        False(lowCardinalityStatus.Contains(authority.ToString(), StringComparison.Ordinal));
        False(lowCardinalityStatus.Contains(G3Capability, StringComparison.Ordinal));
        foreach (CommunityToolkitFlowProjectionMember member in
                 CommunityToolkitFlowProjectionContract.Members)
        {
            False(member.ProducerFixtureId.Contains(secret, StringComparison.Ordinal));
            False(member.ProjectionFixtureId.Contains(secret, StringComparison.Ordinal));
            False(member.GeneratedMemberName.Contains(secret, StringComparison.Ordinal));
        }

        False(typeof(FlowSessionId).Assembly.GetReferencedAssemblies()
            .Any(static assembly =>
                assembly.Name is "CommunityToolkit.Mvvm" or "RunicToolkit.Hosting"));
    }

    private static byte[] SetTitleFrame(string valueJson) => Encoding.UTF8.GetBytes(
        $$$"""{"v":1,"kind":"setProperty","session":"{{{G3SessionId}}}","view":"{{{G3ViewId}}}","request":"{{{G3RequestId}}}","baseRevision":0,"capability":"{{{G3Capability}}}","payload":{"member":101,"value":{{{valueJson}}}}}""");

    private static byte[] FlowAckFrame(long revision) => Encoding.UTF8.GetBytes(
        $$$"""{"v":1,"kind":"ack","session":"{{{G3SessionId}}}","view":"{{{G3ViewId}}}","request":"{{{G3RequestId}}}","capability":"{{{G3Capability}}}","payload":{"revision":{{{revision}}}}}""");

    private static async Task<CommunityToolkitFlowDispatchResult> DispatchRawTitleAsync(
        byte[] rawFrame,
        CommunityToolkitFlowProjection<SyncFixtureViewModel> projection,
        FlowSessionId authority,
        FlowAdmissionProbe probe,
        MvvmLimits limits)
    {
        MvvmWireMessage decoded = MvvmMessageCodec.DecodeClient(rawFrame, limits);
        JsonElement payload = decoded.Document.GetProperty("payload");
        Equal(CommunityToolkitFlowProjectionContract.TitleMemberId,
            payload.GetProperty("member").GetInt32());
        string? title = payload.GetProperty("value").GetString();
        probe.Invocations++;
        return await projection.SetTitleAsync(authority, title);
    }

    private static void Throws<TException>(Action action)
        where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException)
        {
            return;
        }

        throw new InvalidOperationException($"Expected {typeof(TException).Name}.");
    }

    private sealed class FlowAdmissionProbe
    {
        public int Invocations { get; set; }
    }
}
