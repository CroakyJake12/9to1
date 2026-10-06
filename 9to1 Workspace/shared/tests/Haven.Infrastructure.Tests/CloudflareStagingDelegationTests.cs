using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using Haven.Application;
using Haven.Core;
using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;
using Xunit;
namespace Haven.Infrastructure.Tests;

// Actual Home store/profile/broker/Accept/guarded CAS; synthetic signed-in principal and SDK read.
// No credentials, remote call, installed app authority or local-agent cloud acceptance is claimed.
public sealed partial class CloudflareStagingDelegationTests
{
    [Fact] public Task Pending_original_setup_does_not_read_or_save_a_borrowed_namespace() => Run(async rig =>
    {
        await rig.Configure(); var review = await rig.Own(rig.Owner.PrepareStagingDelegationAsync(rig.Selection, 0, default)); rig.Reviews.Add(review);
        var observed = await rig.Own(review.SubmitOriginalAsync(default));
        Assert.False(observed.Configured); Assert.Equal(0, rig.Client.Reads);
        Assert.DoesNotContain((await rig.State()).Records, x => x.RecordType == "home.cloudflare.staging");
    });
    [Fact] public Task Actual_Accept_uses_the_private_read_entry_and_guard_CAS_once() => Run(async rig =>
    {
        await rig.Configure(); var review = await rig.Own(rig.Owner.PrepareStagingDelegationAsync(rig.Selection, 0, default)); rig.Reviews.Add(review);
        await rig.Own(review.SubmitOriginalAsync(default)); Assert.True((await rig.Own(rig.Permissions.DecideAsync(review.RequestId, HomeApprovalChoice.Accept))).Succeeded);
        var actual = rig.Own(review.CommitOriginalAsync(default)); var saved = await actual;
        Assert.Same(actual, review.CommitOriginalAsync(default)); Assert.True(saved.Configured); Assert.Equal(1, rig.Client.Reads); Assert.True(rig.Client.EntryClosed);
        var record = Assert.Single((await rig.State()).Records, x => x.RecordType == "home.cloudflare.staging");
        Assert.Equal(HomeDataScope.DeviceLocal, record.Scope); Assert.Equal(HomeRecordAuthority.LocalCanonical, record.Authority);
        Assert.Contains(rig.Client.Namespace, record.Payload.GetRawText()); Assert.DoesNotContain("access_token", record.Payload.GetRawText());
        Assert.DoesNotContain((await rig.State()).Records, x => x.RecordType == "home.cloudflare.namespace");
        var service = await rig.Own(rig.Owner.AcquireOriginalAsync(default));
        var marker = CloudflareTypedToolCatalogue.CompileOriginal(service, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Call("cloudflare_kv_put_task_marker", rig.Client.Namespace));
        await rig.Own(rig.Owner.DemandOriginalNamespaceAsync(marker, default).AsTask());
        var destroy = CloudflareTypedToolCatalogue.CompileOriginal(service, marker.TaskId, marker.ExecutionId, Guid.NewGuid(), Call("cloudflare_kv_delete_isolated", rig.Client.Namespace));
        var refusal = rig.Own(rig.Owner.DemandOriginalNamespaceAsync(destroy, default).AsTask());
        await Assert.ThrowsAsync<CloudflareSetupRequiredException>(() => refusal); rig.Expected.Add(refusal); Assert.Equal(1, rig.Client.Reads);
    });
    [Fact] public Task Saved_connection_change_after_Accept_prevents_original_binding_read() => Run(async rig =>
    {
        await rig.Configure(); var review = await rig.Own(rig.Owner.PrepareStagingDelegationAsync(rig.Selection, 0, default)); rig.Reviews.Add(review);
        await rig.Own(review.SubmitOriginalAsync(default)); Assert.True((await rig.Own(rig.Permissions.DecideAsync(review.RequestId, HomeApprovalChoice.Accept))).Succeeded);
        rig.Connections.Value = rig.Connections.Value with { UpdatedAt = rig.Connections.Value.UpdatedAt.AddSeconds(1) };
        var actual = rig.Own(review.CommitOriginalAsync(default)); await Assert.ThrowsAsync<AggregateException>(() => actual); rig.Expected.Add(actual); rig.ExpectedReviews.Add(review);
        Assert.Equal(0, rig.Client.Reads); Assert.DoesNotContain((await rig.State()).Records, x => x.RecordType == "home.cloudflare.staging");
    });
    [Fact] public Task Retirement_seals_review_and_close_returns_the_same_actual_task() => Run(async rig =>
    {
        rig.Owner.RequestOriginalStagingRetirement();
        Assert.Throws<ObjectDisposedException>(() => { _ = rig.Owner.PrepareStagingDelegationAsync(rig.Selection, 0, default); });
        var actual = rig.Own(rig.Owner.CloseAndDrainOriginalStagingReviewsAsync()); Assert.Same(actual, rig.Owner.CloseAndDrainOriginalStagingReviewsAsync()); await actual;
        Assert.Equal(0, rig.Client.Reads); Assert.True(actual.IsCompletedSuccessfully);
    });
    [Fact] public Task Retained_SDK_fault_causes_block_save_and_survive_original_close() => Run(async rig =>
    {
        await rig.Configure(); var review = await rig.Own(rig.Owner.PrepareStagingDelegationAsync(rig.Selection, 0, default)); rig.Reviews.Add(review);
        await rig.Own(review.SubmitOriginalAsync(default)); Assert.True((await rig.Own(rig.Permissions.DecideAsync(review.RequestId, HomeApprovalChoice.Accept))).Succeeded);
        var first = new OperationCanceledException("faulted fixed binding read"); var second = new IOException("independent original read fault");
        var original = new TaskCompletionSource<CloudflareWorkerBindingObservation>(TaskCreationOptions.RunContinuationsAsynchronously); original.SetException([first, second]); rig.Client.OriginalRead = original.Task;
        var actual = rig.Own(review.CommitOriginalAsync(default)); var failure = await Assert.ThrowsAsync<AggregateException>(() => actual); rig.Expected.Add(actual); rig.ExpectedReviews.Add(review);
        Assert.True(actual.IsFaulted); Assert.Same(original.Task, rig.Client.LastRead); Assert.Contains(first, Leaves(failure)); Assert.Contains(second, Leaves(failure));
        var close = rig.Own(review.DisposeAsync().AsTask()); var cleanup = await Assert.ThrowsAsync<AggregateException>(() => close); rig.Expected.Add(close);
        Assert.Contains(first, Leaves(cleanup)); Assert.Contains(second, Leaves(cleanup)); Assert.DoesNotContain((await rig.State()).Records, x => x.RecordType == "home.cloudflare.staging");
    });
    [Fact] public Task Held_original_read_is_joined_by_retirement_and_cannot_save_after_seal() => Run(async rig =>
    {
        await rig.Configure(); var review = await rig.Own(rig.Owner.PrepareStagingDelegationAsync(rig.Selection, 0, default)); rig.Reviews.Add(review);
        await rig.Own(review.SubmitOriginalAsync(default)); Assert.True((await rig.Own(rig.Permissions.DecideAsync(review.RequestId, HomeApprovalChoice.Accept))).Succeeded);
        var held = new TaskCompletionSource<CloudflareWorkerBindingObservation>(TaskCreationOptions.RunContinuationsAsynchronously);
        rig.Client.OriginalRead = held.Task;
        var commit = rig.Own(review.CommitOriginalAsync(default)); Task? close = null;
        try
        {
            await rig.Client.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10)); Assert.Same(held.Task, rig.Client.LastRead);
            rig.Owner.RequestOriginalStagingRetirement(); close = rig.Own(rig.Owner.CloseAndDrainOriginalStagingReviewsAsync());
            Assert.False(commit.IsCompleted); Assert.False(close.IsCompleted); Assert.True(rig.Client.EntryClosed);
        }
        finally
        {
            held.TrySetResult(new CloudflareWorkerBindingObservation(rig.Client.Namespace,
                JsonSerializer.SerializeToElement(new { ok = true, worker = rig.Selection.WorkerName, binding = rig.Selection.BindingName, namespace_id = rig.Client.Namespace }), [], []));
            try { await commit; } catch { rig.Expected.Add(commit); rig.ExpectedReviews.Add(review); }
            if (close is not null) try { await close; } catch { rig.Expected.Add(close); }
        }
        Assert.True(commit.IsCompleted); Assert.False(commit.IsCompletedSuccessfully); Assert.NotNull(close); Assert.True(close.IsFaulted); Assert.DoesNotContain((await rig.State()).Records, x => x.RecordType == "home.cloudflare.staging");
    });
    [Theory] [InlineData("missing")] [InlineData("wrong_worker")] [InlineData("wrong_binding")] [InlineData("invalid_namespace")] [InlineData("extra")]
    public void Public_or_malformed_binding_projection_does_not_establish_the_selected_binding(string scenario)
    {
        var selection = new CloudflareStagingBindingSelection("staging-worker", "MARKERS");
        var values = new Dictionary<string, object?> { ["ok"] = true, ["worker"] = selection.WorkerName, ["binding"] = selection.BindingName, ["namespace_id"] = new string('b', 32) };
        if (scenario == "missing") values.Remove("namespace_id"); if (scenario == "wrong_worker") values["worker"] = "production-worker";
        if (scenario == "wrong_binding") values["binding"] = "AUTH"; if (scenario == "invalid_namespace") values["namespace_id"] = "foreign"; if (scenario == "extra") values["secret"] = "not allowed";
        Assert.Throws<CloudflareSetupRequiredException>(() => CloudflareStagingBindingContract.DemandNamespace(JsonSerializer.SerializeToElement(values), selection));
    }
    private static OllamaToolCall Call(string name, string ns) => new(name, new Dictionary<string, JsonElement> { ["namespace_id"] = JsonSerializer.SerializeToElement(ns) });
    private static IEnumerable<Exception> Leaves(Exception error) => error is AggregateException group ? group.InnerExceptions.SelectMany(Leaves) : [error];
    private static async Task Run(Func<Rig, Task> body)
    {
        var rig = new Rig(); var failures = new List<Exception>();
        try { await body(rig); } catch (Exception cause) { failures.Add(cause); }
        foreach (var review in rig.Reviews)
            try { await review.DisposeAsync(); } catch (Exception cause) { if (!rig.ExpectedReviews.Contains(review)) failures.Add(cause); }
        foreach (var actual in rig.Originals)
            try { await actual; } catch (Exception cause) { if (!rig.Expected.Contains(actual)) failures.Add(cause); _ = actual.Exception; }
        try { Directory.Delete(rig.Root, true); } catch (Exception cause) { failures.Add(cause); }
        if (failures.Count == 1) ExceptionDispatchInfo.Capture(failures[0]).Throw();
        if (failures.Count != 0) throw new AggregateException("Owning staging source/control cleanup failed.", failures);
    }
    private sealed class Rig
    {
        internal readonly string Root = Path.Combine(Path.GetTempPath(), "haven-cf-staging-" + Guid.NewGuid().ToString("N"));
        internal readonly FileHomeCoreStateStore Store; internal readonly HomePermissionTrustService Permissions;
        internal readonly HomeCloudflareServiceOwner Owner; internal readonly Connections Connections = new(); internal readonly BindingClient Client = new();
        internal readonly CloudflareStagingBindingSelection Selection = new("staging-worker", "MARKERS");
        internal readonly List<Task> Originals = []; internal readonly HashSet<Task> Expected = new(ReferenceEqualityComparer.Instance);
        internal readonly List<ICloudflareOriginalStagingReview> Reviews = []; internal readonly HashSet<ICloudflareOriginalStagingReview> ExpectedReviews = new(ReferenceEqualityComparer.Instance);
        internal Rig()
        {
            Directory.CreateDirectory(Root); Store = new(Path.Combine(Root, "home.json")); var profiles = new HomeLocalProfileIdentity(Store, new Principal());
            var policies = new HomeCloudflareActionPolicySource(); Permissions = new(Store, policies.TryGet); HomeCloudflareServiceOwner? actual = null;
            var broker = new HomeResourceOperationBroker(new ResourceAuthorizationService(profiles, [new HomeCloudflareResourceResolver(() => actual!)]), Permissions);
            Owner = actual = new(Store, profiles, broker, Permissions, Connections, new Claims(), Client);
        }
        internal T Own<T>(T actual) where T : Task { Originals.Add(actual); return actual; }
        internal async Task<HomeCoreStoredState> State() => (await Own(Store.ReadAsync())).State!;
        internal async Task Configure()
        {
            var selection = new CloudflareSetupSelection(Connections.Value.Id, "https://mcp.cloudflare.com/mcp", new string('a', 32), "execute");
            var setup = await Own(Owner.PrepareSetupAsync(selection, 0, default)); await Own(setup.SubmitOriginalAsync(default));
            Assert.True((await Own(Permissions.DecideAsync(setup.RequestId, HomeApprovalChoice.Accept))).Succeeded); await Own(setup.CommitOriginalAsync(default));
        }
    }
    private sealed class Principal : ITrustedHostPrincipalSource { public ValueTask<string?> GetPrincipalAsync(CancellationToken token) => ValueTask.FromResult<string?>("synthetic-staging-principal"); }
    private sealed class Connections : IExternalConnectionRepository
    {
        internal ExternalConnection Value = new(Guid.NewGuid(), "synthetic OAuth metadata", "mcp", ExternalConnectionKind.Mcp, "cloudflare", true, ExternalConnectionState.Ready, "fixture",
            JsonSerializer.Serialize(new McpConnectionConfiguration(McpTransportKind.StreamableHttp, "https://mcp.cloudflare.com/mcp", UseOAuth: true)), null, null, null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        public Task<ExternalConnection?> GetAsync(Guid id, CancellationToken token) => Task.FromResult<ExternalConnection?>(id == Value.Id ? Value : null);
        public Task<IReadOnlyList<ExternalConnection>> GetAllAsync(CancellationToken token) => Task.FromResult<IReadOnlyList<ExternalConnection>>([Value]);
        public Task UpsertAsync(ExternalConnection value, CancellationToken token) => throw new NotSupportedException();
        public Task DeleteAsync(Guid id, CancellationToken token) => throw new NotSupportedException();
    }
    private sealed class Claims : ITaskRunOriginalActionAdmissionSource
    {
        public TaskRunOriginalActionAdmission RequireOriginalActionAdmission(ITaskRunToolActionPreparation prep, TaskRunAttemptAdmission attempt) => throw new NotSupportedException();
        public Task ValidateOriginalActionAdmissionAsync(TaskRunOriginalActionAdmission admission, ITaskRunToolActionPreparation prep, TaskRunAttemptAdmission attempt, CancellationToken token) => throw new NotSupportedException();
        public void DemandOriginalActionAdmission(TaskRunOriginalActionAdmission admission, ITaskRunToolActionPreparation prep, TaskRunAttemptAdmission attempt) => throw new NotSupportedException();
    }
    private sealed class BindingClient : ICloudflareMcpInvocationClient, ICloudflareWorkerBindingClient
    {
        internal readonly TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly string Namespace = new('b', 32); internal int Reads; internal bool EntryClosed;
        internal Task<CloudflareWorkerBindingObservation>? OriginalRead, LastRead;
        internal Action<ICloudflareOriginalWorkerReadAuthority>? BeforeAdmission;
        private readonly ConditionalWeakTable<CloudflareWorkerBindingObservation, Pair> _issued = new();
        private sealed record Pair(CloudflareSavedService Service, CloudflareStagingBindingSelection Selection);
        public Task<CloudflareMcpDispatchResult> InvokeOriginalAsync(CloudflareOriginalTaskBinding binding, ICloudflareSavedServiceSource services, ITaskRunOriginalActionAdmissionSource claims, ICloudflareOriginalPermission permission, CancellationToken token) => throw new NotSupportedException();
        public bool IsIssuedOriginalResult(CloudflareCompiledInvocation invocation, CloudflareMcpDispatchResult result) => false;
        public bool IsIssuedOriginalWorkerBinding(CloudflareSavedService service, CloudflareStagingBindingSelection selection, CloudflareWorkerBindingObservation observation) =>
            _issued.TryGetValue(observation, out var pair) && ReferenceEquals(pair.Service, service) && ReferenceEquals(pair.Selection, selection);
        public async Task<CloudflareWorkerBindingObservation> ReadOriginalWorkerBindingAsync(CloudflareSavedService service, CloudflareStagingBindingSelection selection,
            ICloudflareOriginalWorkerReadAuthority authority, Action<Action>? callerScope, CancellationToken token)
        {
            BeforeAdmission?.Invoke(authority);
            var entry = await authority.EnterOriginalWorkerReadAsync(service, selection, token); Task<CloudflareWorkerBindingObservation>? read = null;
            var stages = new CloudflareOriginalTaskLedger();
            try
            {
                authority.DemandOriginalWorkerReadEntry(service, selection, entry);
                read = entry.RunOriginalRead(() => { Reads++; return LastRead = OriginalRead ?? Task.FromResult(new CloudflareWorkerBindingObservation(Namespace,
                    JsonSerializer.SerializeToElement(new { ok = true, worker = selection.WorkerName, binding = selection.BindingName, namespace_id = Namespace }), [], [])); }, token);
            }
            finally { await entry.DisposeAsync(); EntryClosed = true; Entered.TrySetResult(); }
            var result = await stages.AwaitAsync(read!); _issued.Add(result, new(service, selection)); return result;
        }
    }
}
