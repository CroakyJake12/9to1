using System.Runtime.ExceptionServices;
using System.Text.Json;
using Haven.Application;
using Haven.Core;
using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;
using Xunit;
namespace Haven.Infrastructure.Tests;

// Real FileHome/profile/permission/broker producer, synthetic principal/connection only.
// These controls grant no installed authority and make no network request.
public sealed partial class CloudflareProductionSetupTests
{
    [Fact] public Task Missing_configuration_is_explicit_and_does_not_invent_a_connection() => Run(async rig =>
    {
        var state = await rig.Own(rig.Owner.GetSetupAsync(default));
        Assert.False(state.Configured); Assert.Equal("CF_SETUP_REQUIRED", state.Code);
        Assert.Equal(0, rig.Connections.Upserts); Assert.Equal(0, rig.Mcp.Calls);
        Assert.DoesNotContain((await rig.State()).Records, x => x.RecordId == "home.cloudflare.connection");
    });
    [Fact] public Task Non_OAuth_selection_is_refused_before_review_or_write() => Run(async rig =>
    {
        rig.Connections.Value = rig.Connections.Value with { ConfigurationJson = JsonSerializer.Serialize(new McpConnectionConfiguration(McpTransportKind.StreamableHttp, rig.Selection.Endpoint, UseOAuth: false)) };
        var actual = rig.Own(rig.Owner.PrepareSetupAsync(rig.Selection, 0, default));
        var error = await Assert.ThrowsAsync<CloudflareSetupRequiredException>(() => actual); rig.Expect(actual);
        Assert.Equal(CloudflareSetupStage.SavedOAuthConnectionRequired, error.Stage);
        Assert.DoesNotContain((await rig.State()).Records, x => x.RecordId == "home.cloudflare.connection"); Assert.Equal(0, rig.Mcp.Calls);
    });
    [Fact] public Task Pending_Home_review_does_not_save_or_dispatch() => Run(async rig =>
    {
        var review = await rig.Own(rig.Owner.PrepareSetupAsync(rig.Selection, 0, default));
        var submitted = await rig.Own(review.SubmitOriginalAsync(default));
        Assert.False(submitted.Configured); Assert.Equal(review.RequestId, submitted.ReviewRequestId);
        Assert.Equal(HomePermissionRequestState.PendingApproval, (await rig.Own(rig.Permissions.GetAuthorizationAsync(review.RequestId))).State);
        Assert.DoesNotContain((await rig.State()).Records, x => x.RecordId == "home.cloudflare.connection"); Assert.Equal(0, rig.Mcp.Calls);
    });
    [Fact] public Task Actual_Accept_saves_nonsecret_selection_once_and_issues_private_service() => Run(async rig =>
    {
        var review = await rig.Own(rig.Owner.PrepareSetupAsync(rig.Selection, 0, default));
        await rig.Own(review.SubmitOriginalAsync(default));
        Assert.True((await rig.Own(rig.Permissions.DecideAsync(review.RequestId, HomeApprovalChoice.Accept))).Succeeded);
        var actual = rig.Own(review.CommitOriginalAsync(default)); var saved = await actual;
        Assert.Same(actual, review.CommitOriginalAsync(default)); Assert.True(saved.Configured); Assert.Equal(1, saved.Revision);
        var record = Assert.Single((await rig.State()).Records, x => x.RecordId == "home.cloudflare.connection");
        Assert.Equal(HomeDataScope.DeviceLocal, record.Scope); Assert.Equal(HomeRecordAuthority.LocalCanonical, record.Authority);
        Assert.DoesNotContain("access_token", record.Payload.GetRawText()); Assert.DoesNotContain("refresh_token", record.Payload.GetRawText());
        var service = await rig.Own(rig.Owner.AcquireOriginalAsync(default));
        await rig.Own(rig.Owner.RevalidateOriginalAsync(service, default));
        Assert.Equal(rig.Selection.ConnectionId, service.Connection.Id); Assert.Equal(rig.Selection.AccountId, service.AccountId);
        Assert.Equal(0, rig.Mcp.Calls); Assert.Equal(0, rig.Connections.Upserts);
    });
    [Fact] public Task Changed_saved_connection_after_approval_prevents_commit() => Run(async rig =>
    {
        var review = await rig.Own(rig.Owner.PrepareSetupAsync(rig.Selection, 0, default)); await rig.Own(review.SubmitOriginalAsync(default));
        Assert.True((await rig.Own(rig.Permissions.DecideAsync(review.RequestId, HomeApprovalChoice.Accept))).Succeeded);
        rig.Connections.Value = rig.Connections.Value with { UpdatedAt = rig.Connections.Value.UpdatedAt.AddSeconds(1) };
        var actual = rig.Own(review.CommitOriginalAsync(default)); await Assert.ThrowsAsync<UnauthorizedAccessException>(() => actual); rig.Expect(actual);
        Assert.DoesNotContain((await rig.State()).Records, x => x.RecordId == "home.cloudflare.connection"); Assert.Equal(0, rig.Mcp.Calls);
    });
    [Fact] public Task Expected_configuration_revision_conflict_refuses_before_review() => Run(async rig =>
    {
        var actual = rig.Own(rig.Owner.PrepareSetupAsync(rig.Selection, 12, default));
        var error = await Assert.ThrowsAsync<CloudflareSetupRequiredException>(() => actual); rig.Expect(actual);
        Assert.Equal("CF_CONFIGURATION_CONFLICT", error.Code); Assert.Equal(0, rig.Mcp.Calls);
    });
    [Fact] public Task Public_saved_configuration_observation_cannot_substitute_for_private_issuance() => Run(async rig =>
    {
        var copied = CloudflareSavedService.ObserveSavedConfiguration(rig.Connections.Value, rig.Selection.AccountId, rig.Selection.ExecuteTool);
        var actual = rig.Own(rig.Owner.RevalidateOriginalAsync(copied, default));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => actual); rig.Expect(actual); Assert.Equal(0, rig.Mcp.Calls);
    });
    [Fact] public Task Faulted_connection_task_preserves_all_direct_original_causes() => Run(async rig =>
    {
        var first = new OperationCanceledException("faulted original connection read"); var second = new IOException("independent original read cause");
        var returned = new TaskCompletionSource<ExternalConnection?>(TaskCreationOptions.RunContinuationsAsynchronously); returned.SetException([first, second]);
        rig.Connections.ReadOriginal = returned.Task;
        var actual = rig.Own(rig.Owner.PrepareSetupAsync(rig.Selection, 0, default)); var error = await Assert.ThrowsAsync<AggregateException>(() => actual); rig.Expect(actual);
        Assert.True(actual.IsFaulted); Assert.Contains(first, error.InnerExceptions); Assert.Contains(second, error.InnerExceptions);
        Assert.Same(returned.Task, rig.Connections.LastRead); Assert.Equal(0, rig.Mcp.Calls);
    });
    [Fact] public void Raw_marker_GET_requires_the_exact_official_saved_transport()
    {
        var get = Assert.Single(CloudflareTypedToolCatalogue.Descriptors, x => x.Kind == CloudflareOperationKind.KvMarkerGet);
        Assert.True(get.IsImplemented);
        var connection = Connection(); var service = CloudflareSavedService.ObserveSavedConfiguration(connection, new string('a', 32), "execute");
        Assert.Throws<CloudflareSetupRequiredException>(() => CloudflareTypedToolCatalogue.CompileOriginal(service, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            new OllamaToolCall("cloudflare_kv_get_task_marker", new Dictionary<string, JsonElement> { ["namespace_id"] = JsonSerializer.SerializeToElement(new string('b', 32)) })));
    }
    [Fact] public void Finite_physical_guard_rejects_self_join_even_with_suppressed_context()
    {
        var owner = new object(); using var phase = CloudflareOriginalExecutionGuard.EnterOriginal(owner);
        using (ExecutionContext.SuppressFlow())
            CloudflareOriginalExecutionGuard.InvokeOriginal(owner, () => { Assert.Throws<InvalidOperationException>(() => CloudflareOriginalExecutionGuard.DemandExternalJoin(owner)); return true; });
        Assert.Throws<InvalidOperationException>(() => CloudflareOriginalExecutionGuard.DemandExternalJoin(owner));
    }
    [Fact] public Task Namespace_title_or_public_compilation_cannot_issue_recovery_ownership() => Run(async rig =>
    {
        var service = CloudflareSavedService.ObserveSavedConfiguration(rig.Connections.Value, rig.Selection.AccountId, rig.Selection.ExecuteTool);
        var original = CloudflareTypedToolCatalogue.CompileOriginal(service, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), new("cloudflare_kv_create_isolated", new Dictionary<string, JsonElement>()));
        var error = Assert.Throws<CloudflareSetupRequiredException>(() => { _ = rig.Owner.PrepareKnownCreateReconciliationAsync(original, default); });
        Assert.Equal("CF_ORIGINAL_CREATE_NOT_RETAINED", error.Code); Assert.Equal(0, rig.Mcp.Calls);
        Assert.DoesNotContain((await rig.State()).Records, row => row.RecordType == "home.cloudflare.namespace");
    });
    [Fact] public Task Reconciliation_refuses_a_read_operation_without_dispatch_or_record_write() => Run(async rig =>
    {
        var service = CloudflareSavedService.ObserveSavedConfiguration(rig.Connections.Value, rig.Selection.AccountId, rig.Selection.ExecuteTool);
        var original = CloudflareTypedToolCatalogue.CompileOriginal(service, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), new("cloudflare_kv_list", new Dictionary<string, JsonElement>()));
        Assert.Throws<UnauthorizedAccessException>(() => { _ = rig.Owner.PrepareKnownCreateReconciliationAsync(original, default); });
        Assert.Equal(0, rig.Mcp.Calls); Assert.DoesNotContain((await rig.State()).Records, row => row.RecordType == "home.cloudflare.namespace");
    });
    [Fact] public Task Recovery_request_seal_refuses_new_originals_without_namespace_adoption() => Run(async rig =>
    {
        rig.Owner.RequestOriginalRecoveryRetirement();
        var service = CloudflareSavedService.ObserveSavedConfiguration(rig.Connections.Value, rig.Selection.AccountId, rig.Selection.ExecuteTool);
        var original = CloudflareTypedToolCatalogue.CompileOriginal(service, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), new("cloudflare_kv_create_isolated", new Dictionary<string, JsonElement>()));
        Assert.Throws<ObjectDisposedException>(() => { _ = rig.Owner.PrepareKnownCreateReconciliationAsync(original, default); });
        await rig.Own(rig.Owner.CloseAndDrainOriginalRecoveriesAsync()); Assert.Equal(0, rig.Mcp.Calls);
    });
    [Fact] public Task Actual_empty_recovery_close_coalesces_and_seals_before_any_new_review() => Run(async rig =>
    {
        var actual = rig.Own(rig.Owner.CloseAndDrainOriginalRecoveriesAsync());
        Assert.Same(actual, rig.Owner.CloseAndDrainOriginalRecoveriesAsync()); await actual; Assert.True(actual.IsCompletedSuccessfully);
        Assert.Equal(0, rig.Mcp.Calls); Assert.DoesNotContain((await rig.State()).Records, row => row.RecordType == "home.cloudflare.namespace");
    });
    private static ExternalConnection Connection() => new(Guid.NewGuid(), "synthetic saved CF OAuth", "mcp", ExternalConnectionKind.Mcp, "cloudflare", true,
        ExternalConnectionState.Ready, "fixture metadata", JsonSerializer.Serialize(new McpConnectionConfiguration(McpTransportKind.StreamableHttp, "https://example.invalid/mcp", UseOAuth: true)),
        null, null, null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
    private static async Task Run(Func<Rig, Task> body)
    {
        var rig = new Rig(); Exception? original = null;
        try { await body(rig); } catch (Exception error) { original = error; }
        var errors = new List<Exception>();
        foreach (var actual in rig.Originals.ToArray()) try { await actual; } catch (Exception error) { if (!rig.Expected.Contains(actual)) errors.Add(error); _ = actual.Exception; }
        try { Directory.Delete(rig.Root, true); } catch (Exception error) { errors.Add(error); }
        if (original is not null && errors.Count == 0) ExceptionDispatchInfo.Capture(original).Throw();
        if (original is not null) errors.Insert(0, original);
        if (errors.Count != 0) throw new AggregateException("Owning setup control and original cleanup failed.", errors);
    }
    private sealed class Rig
    {
        internal string Root { get; } = Path.Combine(Path.GetTempPath(), "haven-cf-setup-" + Guid.NewGuid().ToString("N"));
        internal FileHomeCoreStateStore Store; internal HomePermissionTrustService Permissions; internal HomeCloudflareServiceOwner Owner;
        internal Connections Connections = new(); internal Mcp Mcp = new(); internal CloudflareSetupSelection Selection;
        internal List<Task> Originals = []; internal HashSet<Task> Expected = new(ReferenceEqualityComparer.Instance);
        internal Rig()
        {
            Directory.CreateDirectory(Root); Store = new(Path.Combine(Root, "home.json")); var profiles = new HomeLocalProfileIdentity(Store, new Principal());
            var policies = new HomeCloudflareActionPolicySource(); Permissions = new(Store, policies.TryGet);
            HomeCloudflareServiceOwner? current = null;
            var resolver = new HomeCloudflareResourceResolver(() => current ?? throw new InvalidOperationException("Owner not composed."));
            var broker = new HomeResourceOperationBroker(new ResourceAuthorizationService(profiles, [resolver]), Permissions);
            Owner = current = new(Store, profiles, broker, Permissions, Connections, new Actions(), Mcp);
            Selection = new(Connections.Value.Id, "https://example.invalid/mcp", new string('a', 32), "execute");
        }
        internal T Own<T>(T actual) where T : Task { Originals.Add(actual); return actual; }
        internal void Expect(Task actual) => Expected.Add(actual);
        internal async Task<HomeCoreStoredState> State() => (await Own(Store.ReadAsync())).State!;
    }
    private sealed class Principal : ITrustedHostPrincipalSource { public ValueTask<string?> GetPrincipalAsync(CancellationToken token) => ValueTask.FromResult<string?>("synthetic-test-principal"); }
    private sealed class Connections : IExternalConnectionRepository
    {
        internal ExternalConnection Value = Connection(); internal int Upserts; internal Task<ExternalConnection?>? ReadOriginal; internal Task<ExternalConnection?>? LastRead;
        internal Action? BeforeRead;
        public Task<ExternalConnection?> GetAsync(Guid id, CancellationToken token)
        { BeforeRead?.Invoke(); return LastRead = ReadOriginal ?? Task.FromResult<ExternalConnection?>(id == Value.Id ? Value : null); }
        public Task<IReadOnlyList<ExternalConnection>> GetAllAsync(CancellationToken token) => Task.FromResult<IReadOnlyList<ExternalConnection>>([Value]);
        public Task UpsertAsync(ExternalConnection value, CancellationToken token) { Upserts++; Value = value; return Task.CompletedTask; }
        public Task DeleteAsync(Guid id, CancellationToken token) => throw new NotSupportedException();
    }
    private sealed class Actions : ITaskRunOriginalActionAdmissionSource
    {
        public TaskRunOriginalActionAdmission RequireOriginalActionAdmission(ITaskRunToolActionPreparation prep, TaskRunAttemptAdmission attempt) => throw new NotSupportedException("Setup is not Task action admission.");
        public Task ValidateOriginalActionAdmissionAsync(TaskRunOriginalActionAdmission receipt, ITaskRunToolActionPreparation prep, TaskRunAttemptAdmission attempt, CancellationToken token) => throw new NotSupportedException();
        public void DemandOriginalActionAdmission(TaskRunOriginalActionAdmission receipt, ITaskRunToolActionPreparation prep, TaskRunAttemptAdmission attempt) => throw new NotSupportedException();
    }
    private sealed class Mcp : ICloudflareMcpInvocationClient
    {
        internal int Calls;
        public Task<CloudflareMcpDispatchResult> InvokeOriginalAsync(CloudflareOriginalTaskBinding original, ICloudflareSavedServiceSource services, ITaskRunOriginalActionAdmissionSource actions, ICloudflareOriginalPermission permission, CancellationToken token) { Calls++; throw new NotSupportedException("No network in setup controls."); }
        public bool IsIssuedOriginalResult(CloudflareCompiledInvocation original, CloudflareMcpDispatchResult result) => false;
    }
}
