using Haven.Application;
using Haven.Core;
using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;
using NineToOne.Cui.AI;
using Xunit;

namespace HavenOS.Home.Tests;

public sealed class HomeNativeActionPolicyTests
{
    [Fact]
    public async Task Native_catalogue_operates_without_model_session_and_duplicate_owner_denies()
    {
        var root = Path.Combine(Path.GetTempPath(), "astra-native-policy-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new FileHomeCoreStateStore(Path.Combine(root, "home.json"));
            HomePermissionCallerIdentity caller = new("profile", "Native profile", "os", "session", true);
            HomePermissionRequestSubmission request = new(null, caller, "session", new("files", "files.save", [new("file", "file-1")]), HomePermissionImpactPreview.Unknown);
            var service = new HomeAppAiServices(new ModelProviderRegistry([]), store, caller, new Graph(), new Invocations(), [new Policy()]);
            var pending = await service.Permissions.AuthorizeAsync(request);
            Assert.Equal(HomePermissionRequestState.PendingApproval, pending.State);
            Assert.True((await service.Permissions.DecideAsync(pending.RequestId, HomeApprovalChoice.Accept)).Succeeded);
            Assert.True((await service.Permissions.BeginExecutionAsync(pending.RequestId)).IsAllowed);
            var replay = await service.Permissions.BeginExecutionAsync(pending.RequestId);
            Assert.Equal(HomePermissionRequestState.Executing, replay.State);
            Assert.Equal("HOME_PERMISSION_NOT_AUTHORIZED", replay.Code);
            Assert.False(replay.IsAllowed);
            var duplicate = new HomeAppAiServices(new ModelProviderRegistry([]), store, caller, new Graph(), new Invocations(), [new Policy(), new Policy()]);
            Assert.Equal(HomePermissionRequestState.Denied, (await duplicate.Permissions.AuthorizeAsync(request)).State);
            Assert.Equal(HomePermissionRequestState.Denied, (await service.Permissions.AuthorizeAsync(request with
            { Scope = new("files", "invented.action", [new("file", "file-1")]) })).State);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
    [Fact]
    public async Task Local_only_home_factory_never_discovers_remote_catalogue()
    {
        var root = Path.Combine(Path.GetTempPath(), "astra-local-catalogue-" + Guid.NewGuid().ToString("N"));
        try
        {
            var remote = new RemoteProvider();
            var service = new HomeAppAiServices(new ModelProviderRegistry([remote]),
                new FileHomeCoreStateStore(Path.Combine(root, "home.json")),
                new("profile", "Profile", "os", "session", true), new Graph(), new Invocations());
            Assert.Empty(await service.GetModelsAsync(default));
            Assert.False(await service.SelectAsync("remote:model", default));
            Assert.Equal(0, remote.CatalogueCalls);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task Exact_original_completion_recovers_audit_without_execution_replay(bool afterPublication, bool rejectAdmission)
    {
        var root = Path.Combine(Path.GetTempPath(), "astra-cui-completion-" + Guid.NewGuid().ToString("N"));
        using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        try
        {
            var fault = new CompletionFaultStore(new FileHomeCoreStateStore(Path.Combine(root, "home.json")));
            var presenter = new ControlledCompletionPresenter();
            var service = new HomeAppAiServices(new ModelProviderRegistry([]), fault,
                new("profile", "Native profile", "os", "session", true), new Graph(), new Invocations(), [new Policy()], promptPresenter: presenter);
            var arguments = System.Text.Json.JsonSerializer.SerializeToElement(new { exact = "payload" });
            var context = new AppAiContextSnapshot("files", "test", "file-1", "Controlled owner", null,
                new Dictionary<string, System.Text.Json.JsonElement>(), AppAiDataSensitivity.UserContent, DateTimeOffset.UtcNow);
            var descriptor = new AppAiActionDescriptor("files.save", "Save", "Controlled save", AppAiActionRisk.ReversibleChange,
                true, "{\"type\":\"object\"}", AffectedObjectIds: ["file-1"]);
            var approval = service.RequestAsync(new("profile", context, descriptor, true, true, "Save", null, "case", arguments), lifetime.Token).AsTask();
            string? requestId = null;
            for (var attempt = 0; attempt < 200 && requestId is null; attempt++)
            {
                requestId = (await service.Permissions.GetSnapshotAsync(cancellationToken: lifetime.Token)).PendingRequests.SingleOrDefault()?.RequestId;
                if (requestId is null) await Task.Delay(10, lifetime.Token);
            }
            Assert.NotNull(requestId);
            Assert.True((await service.Permissions.DecideAsync(requestId!, HomeApprovalChoice.Accept, cancellationToken: lifetime.Token)).Succeeded);
            presenter.Displayed.TrySetResult(true);
            var decision = await approval;
            var request = new AppAiActionRequest("files", "files.save", arguments, decision.ApprovalToken, "case", AppAiAccessMode.Write);
            var unknown = await service.CompleteRejectedVerificationAsync(request with { ApprovalToken = "not-issued" }, lifetime.Token);
            Assert.False(unknown.AuditRecorded); Assert.Null(unknown.Recovery);
            var unissued = await service.CompleteWithRecoveryAsync(request with { ApprovalToken = null },
                AppAiActionResult.Success("Caller prose is not an issued owner result"), lifetime.Token);
            Assert.False(unissued.AuditRecorded); Assert.Null(unissued.Recovery);
            // A wrong target must not consume the retained original approval.
            Assert.False(await service.VerifyAsync("wrong", "files.save", decision.ApprovalToken!, lifetime.Token));
            if (rejectAdmission)
            {
                fault.Fail = true; fault.AfterPublication = afterPublication; fault.Marker = "HOME_EXECUTION_STARTED";
                Assert.False(await service.VerifyRequestAsync(request, lifetime.Token));
                fault.Fail = true; fault.Marker = "HOME_ACTION_ADMISSION_REJECTED";
                var rejected = await service.CompleteRejectedVerificationAsync(request, lifetime.Token);
                Assert.False(rejected.AuditRecorded); Assert.NotNull(rejected.Recovery);
                Assert.False(await service.VerifyRequestAsync(request, lifetime.Token));
                Assert.True((await rejected.Recovery!.FinishAsync(lifetime.Token)).AuditRecorded);
                Assert.True((await rejected.Recovery.FinishAsync(lifetime.Token)).AuditRecorded);
                Assert.Equal(HomePermissionRequestState.Failed, (await service.Permissions.GetAuthorizationAsync(requestId!)).State);
                Assert.Single((await service.Permissions.GetSnapshotAsync(cancellationToken: lifetime.Token)).RecentAuditEvents,
                    entry => entry.ResultCode == "HOME_ACTION_ADMISSION_REJECTED");
                return;
            }
            Assert.True(await service.VerifyRequestAsync(request, lifetime.Token));
            fault.Fail = true; fault.AfterPublication = afterPublication;
            var observed = await service.CompleteWithRecoveryAsync(request, AppAiActionResult.Success("Owner already committed"), lifetime.Token);
            Assert.False(observed.AuditRecorded); Assert.NotNull(observed.Recovery);
            await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await service.CompleteWithRecoveryAsync(request, AppAiActionResult.Rejected("Forged later failure", "different-outcome"), lifetime.Token));
            await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await service.CompleteWithRecoveryAsync(request with { Arguments = System.Text.Json.JsonSerializer.SerializeToElement(new { exact = "changed" }) },
                    AppAiActionResult.Success("Owner already committed"), lifetime.Token));
            Assert.False(await service.VerifyRequestAsync(request, lifetime.Token));
            Assert.True((await observed.Recovery!.FinishAsync(lifetime.Token)).AuditRecorded);
            Assert.True((await observed.Recovery.FinishAsync(lifetime.Token)).AuditRecorded);
            Assert.Equal(HomePermissionRequestState.Succeeded, (await service.Permissions.GetAuthorizationAsync(requestId!)).State);
            var audit = (await service.Permissions.GetSnapshotAsync(cancellationToken: lifetime.Token)).RecentAuditEvents;
            Assert.Single(audit, entry => entry.ResultCode == "HOME_ACTION_SUCCEEDED");
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public async Task Actual_ai_bridge_without_presenter_returns_required_and_never_begins_execution()
    {
        var root = Path.Combine(Path.GetTempPath(), "astra-ai-missing-presenter-" + Guid.NewGuid().ToString("N"));
        using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        try
        {
            var service = new HomeAppAiServices(new ModelProviderRegistry([]),
                new FileHomeCoreStateStore(Path.Combine(root, "home.json")),
                new("profile", "Native profile", "os", "session", true), new Graph(), new Invocations(), [new Policy()]);
            var context = new AppAiContextSnapshot("files", "test", "file-1", "Controlled owner", null,
                new Dictionary<string, System.Text.Json.JsonElement>(), AppAiDataSensitivity.UserContent, DateTimeOffset.UtcNow);
            var descriptor = new AppAiActionDescriptor("files.save", "Save", "Controlled save", AppAiActionRisk.ReversibleChange,
                true, "{\"type\":\"object\"}", AffectedObjectIds: ["file-1"]);
            var decision = await service.RequestAsync(new("profile", context, descriptor, true, true, "Save", null, "case"), lifetime.Token);
            Assert.Equal(AppAiApprovalOutcome.Pending, decision.Outcome);
            Assert.Null(decision.ApprovalToken); Assert.Equal("HOME_PERMISSION_REQUIRED", decision.Code);
            var snapshot = await service.Permissions.GetSnapshotAsync(cancellationToken: lifetime.Token);
            var pending = Assert.Single(snapshot.PendingRequests);
            Assert.Equal(HomePermissionRequestState.PendingApproval, pending.State);
            Assert.False((await service.Permissions.BeginExecutionAsync(pending.RequestId, lifetime.Token)).IsAllowed);
            Assert.DoesNotContain(snapshot.RecentAuditEvents,
                entry => entry.Kind is HomePermissionAuditKind.ApprovalPromptShown or HomePermissionAuditKind.DecisionMade or HomePermissionAuditKind.ExecutionStarted);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    // Controlled frontend observation for this unit completion test only. It cannot decide,
    // acknowledge display or execute; genuine mounted native display is tested separately.
    private sealed class ControlledCompletionPresenter : IHomeApprovalPromptPresenter
    {
        internal readonly TaskCompletionSource<bool> Displayed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async ValueTask<bool> ShowPendingRequestAsync(string requestId, CancellationToken ct) =>
            await Displayed.Task.WaitAsync(ct);
    }

    private sealed class CompletionFaultStore(IHomeCoreStateStore inner) : IHomeCoreStateStore
    {
        public bool Fail; public bool AfterPublication;
        public string Marker = "HOME_ACTION_SUCCEEDED";
        public Task<HomeStateReadResult> ReadAsync(CancellationToken ct = default) => inner.ReadAsync(ct);
        public async Task<HomeStateWriteResult> WriteAsync(HomeCoreStateRecord record, long expected, CancellationToken ct = default)
        {
            if (Fail && record.Payload.GetRawText().Contains(Marker, StringComparison.Ordinal))
            {
                Fail = false;
                if (AfterPublication) Assert.True((await inner.WriteAsync(record, expected, ct)).IsSuccess);
                throw new UnauthorizedAccessException("Controlled Home completion acknowledgment failure.");
            }
            return await inner.WriteAsync(record, expected, ct);
        }
    }

    private sealed class RemoteProvider : IModelProvider
    {
        public int CatalogueCalls;
        public string Id => "remote";
        public string DisplayName => "Remote";
        public ModelProviderKind Kind => ModelProviderKind.Ollama;
        public bool IsLocal => false;
        public bool CanManageModels => false;
        public Task<IReadOnlyList<ProviderModelDescriptor>> GetModelsAsync(CancellationToken ct)
        { CatalogueCalls++; return Task.FromResult<IReadOnlyList<ProviderModelDescriptor>>([]); }
        public Task<ProviderHealthStatus> CheckHealthAsync(CancellationToken ct) => throw new NotSupportedException();
        public IAsyncEnumerable<string> StreamChatAsync(OllamaChatRequest request, CancellationToken ct) => throw new NotSupportedException();
        public Task<string> CompleteAsync(OllamaChatRequest request, CancellationToken ct) => throw new NotSupportedException();
        public Task<OllamaToolResponse> ChatWithToolsAsync(OllamaToolRequest request, CancellationToken ct) => throw new NotSupportedException();
    }
    private sealed class Policy : IHomeActionPolicySource
    {
        public HomePermissionActionPolicy? TryGet(string app, string action) => app == "files" && action == "files.save"
            ? new(HavenOS.Home.PermissionsTrustNotifications.HomePermissionRisk.High, true, false, true) : null;
    }
    private sealed class Invocations : IInvocationResolver
    {
        public ValueTask<IReadOnlyList<InvocationToken>> ResolveAsync(IReadOnlyList<InvocationToken> tokens, CancellationToken ct) => ValueTask.FromResult(tokens);
    }
    private sealed class Graph : IExecutionEventRepository
    {
        public Task AppendAsync(IReadOnlyList<ExecutionEvent> events, CancellationToken ct) => Task.CompletedTask;
        public Task<IReadOnlyList<ExecutionEvent>> GetExecutionAsync(Guid id, CancellationToken ct) => Task.FromResult<IReadOnlyList<ExecutionEvent>>([]);
        public Task<IReadOnlyList<ExecutionSummary>> SearchExecutionsAsync(string? query, int limit, CancellationToken ct) => Task.FromResult<IReadOnlyList<ExecutionSummary>>([]);
    }
}
