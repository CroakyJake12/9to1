using System.Text.Json;
using System.Text.Json.Nodes;
using Haven.Application;
using Haven.Browser;
using HavenOS.Apps.Browse;
using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;
using Xunit;
namespace HavenOS.Home.Tests;

/// <summary>Actual FileHome/profile/permissions/resource registry/BrowserSession. Native host and OS principal are controlled;
/// these tests establish local approval/lease protocol, not actual JS emission or an external tool effect.</summary>
public sealed class HomeWebMcpBrowserLeaseTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "astra-browser-home-" + Guid.NewGuid().ToString("N"));
    private readonly FileHomeCoreStateStore _store;
    private readonly Principal _principal = new();
    private readonly HomeLocalProfileIdentity _profiles;
    private readonly HomePermissionTrustService _permissions;
    private readonly HomeWebMcpOriginalActorApproval _owner;
    private readonly BrowserSessionService _browser;
    private readonly BrowseOwnedDocumentRegistry _registry;
    private static readonly WebMcpDocument Document = new("https://example.test", "controlled-document", "controlled-browser", "navigator.modelContextTesting", true, false);
    private static WebMcpTool Tool() => new("controlled-tool", "Untrusted description", JsonSerializer.SerializeToElement(new { type = "object" }));
    private static WebMcpInvocationRequest Request() => new(Document.Origin, Document.DocumentID, Document.BrowserVersion,
        Document.Capability, Document.Supported, Tool().Name, Tool().InputSchema, JsonSerializer.SerializeToElement(new { }));
    public HomeWebMcpBrowserLeaseTests()
    {
        Directory.CreateDirectory(_root); _store = new(Path.Combine(_root, "home.json")); _profiles = new(_store, _principal);
        _browser = new(new Paths()); _browser.Attach(new Host()); _registry = new(_browser, _profiles);
        _permissions = new(_store, new HomeWebMcpActionPolicySource().TryGet);
        var broker = new HomeResourceOperationBroker(new ResourceAuthorizationService(_profiles, [_registry]), _permissions);
        _owner = new(_store, _profiles, broker, _permissions);
    }
    private async Task<(IWebMcpOriginalActorReview Review, IBrowseOwnedDocumentSelection Selection)> Prepare()
    {
        var actor = (await _profiles.GetCurrentAsync(default))!;
        var selected = await _registry.LoadForDisplayAsync(actor, Document, Tool());
        return (_owner.PrepareReview(actor, Request(), [selected.Scope]), selected);
    }
    private async Task<(IWebMcpOriginalDispatch Dispatch, IWebMcpOriginalActorReview Review, IBrowseOwnedDocumentSelection Selection)> Claim()
    {
        var prepared = await Prepare();
        Assert.Equal(WebMcpPreparedReviewState.PendingApproval, (await prepared.Review.SubmitPreparedAsync(default)).State);
        Assert.Equal(WebMcpDispatchBeginState.PendingApproval, (await prepared.Review.BeginDispatchAsync(Request(), default)).State);
        Assert.True((await _permissions.DecideAsync(prepared.Review.RequestId, HomeApprovalChoice.Accept)).Succeeded);
        var began = await prepared.Review.BeginDispatchAsync(Request(), default);
        var dispatch = Assert.IsAssignableFrom<IWebMcpOriginalDispatch>(began.Dispatch);
        Assert.Equal(WebMcpDispatchBeginState.Ready, began.State);
        Assert.Same(dispatch, (await prepared.Review.BeginDispatchAsync(Request(), default)).Dispatch);
        Assert.Equal(HomePermissionRequestState.Executing, (await _permissions.ReadRequestObservationAsync(prepared.Review.RequestId))!.State);
        return (dispatch, prepared.Review, prepared.Selection);
    }
    [Fact]
    public async Task Actual_individual_approval_claim_and_held_Home_lease_serialize_canonical_profile_replacement()
    {
        var issued = await Claim(); using var selection = issued.Selection;
        var state = (await _store.ReadAsync()).State!;
        var profileRecord = Assert.Single(state.Records, value => value.RecordId == "home.local-profile");
        var profile = profileRecord.Payload.Deserialize<HomeLocalProfile>()!;
        var lease = Assert.IsAssignableFrom<IWebMcpFinalDispatchLease>(await issued.Dispatch.EnterFinalDispatchAsync(Request(), default));
        try
        {
            Assert.True(await lease.CheckAsync(default));
            var writer = _store.WriteAsync(profileRecord with { Payload = JsonSerializer.SerializeToElement(profile with { ProfileId = Guid.NewGuid() }) }, profileRecord.Revision);
            Assert.False(writer.IsCompleted);
            await lease.DisposeAsync(); Assert.True((await writer.WaitAsync(TimeSpan.FromSeconds(10))).IsSuccess);
            Assert.False(await lease.CheckAsync(default));
            Assert.Null(await issued.Dispatch.EnterFinalDispatchAsync(Request(), default)); // Native entry cannot replay.
        }
        finally { await lease.DisposeAsync(); }
    }
    [Fact]
    public async Task Changed_actual_principal_after_claim_denies_final_entry_and_releases_actual_Home_lock()
    {
        var issued = await Claim(); using var selection = issued.Selection;
        var before = await File.ReadAllBytesAsync(Path.Combine(_root, "home.json"));
        _principal.Value = "replacement-controlled-OS-principal";
        Assert.Null(await issued.Dispatch.EnterFinalDispatchAsync(Request(), default));
        Assert.True((await _store.ReadAsync().WaitAsync(TimeSpan.FromSeconds(10))).IsSuccess);
        Assert.Equal(before, await File.ReadAllBytesAsync(Path.Combine(_root, "home.json")));
    }
    [Fact]
    public async Task Actual_executing_full_resource_tuple_replacement_before_entry_denies_even_with_same_profile()
    {
        var issued = await Claim(); using var selection = issued.Selection;
        var record = Assert.Single((await _store.ReadAsync()).State!.Records, value => value.RecordId == "home.permissions-trust");
        var json = JsonNode.Parse(record.Payload.GetRawText())!;
        json["Requests"]!.AsArray()[0]!["Impact"]!["ResourceBinding"]!["Scopes"]!.AsArray()[0]!["Revision"] = "substituted-actual-resource-revision";
        Assert.True((await _store.WriteAsync(record with { Payload = JsonSerializer.SerializeToElement(json) }, record.Revision)).IsSuccess);
        var before = await File.ReadAllBytesAsync(Path.Combine(_root, "home.json"));
        Assert.Null(await issued.Dispatch.EnterFinalDispatchAsync(Request(), default));
        Assert.Equal(before, await File.ReadAllBytesAsync(Path.Combine(_root, "home.json")));
        Assert.True((await _store.ReadAsync().WaitAsync(TimeSpan.FromSeconds(10))).IsSuccess);
    }
    [Fact]
    public async Task Actual_completion_rejection_preserves_known_outcome_without_false_audit_acknowledgement_or_redispatch()
    {
        var issued = await Claim(); using var selection = issued.Selection;
        await using (var lease = (await issued.Dispatch.EnterFinalDispatchAsync(Request(), default))!)
            Assert.True(await lease.CheckAsync(default));
        Assert.True((await _permissions.RecordExecutionAsync(issued.Review.RequestId,
            new(HomePermissionRequestState.Cancelled, "OTHER_ACTUAL_HOME_TERMINAL", "Controlled protocol stopped without external execution.", []))).Succeeded);
        var before = await File.ReadAllBytesAsync(Path.Combine(_root, "home.json"));
        var result = await issued.Dispatch.CompleteObservedAsync(new(issued.Dispatch.InvocationId, WebMcpObservedOutcomeKind.Cancelled,
            "ORIGINAL_OWNER_CANCELLED", "Controlled owner observed no external execution."), default);
        Assert.True(result.OutcomeKnown); Assert.False(result.AuditRecorded);
        var retried = await issued.Dispatch.FinishAuditAsync(default);
        Assert.True(retried.OutcomeKnown); Assert.False(retried.AuditRecorded);
        Assert.Null(await issued.Dispatch.EnterFinalDispatchAsync(Request(), default));
        Assert.Equal(before, await File.ReadAllBytesAsync(Path.Combine(_root, "home.json")));
    }
    [Fact]
    public async Task First_known_correlated_outcome_survives_cancelled_audit_wait_and_finishes_only_original_audit_after_actor_change()
    {
        var issued = await Claim(); using var selection = issued.Selection;
        await using (var lease = (await issued.Dispatch.EnterFinalDispatchAsync(Request(), default))!)
            Assert.True(await lease.CheckAsync(default));
        var before = await File.ReadAllBytesAsync(Path.Combine(_root, "home.json"));
        var outcome = new WebMcpObservedOutcome(issued.Dispatch.InvocationId, WebMcpObservedOutcomeKind.Cancelled,
            "CONTROLLED_OWNER_CANCELLED", "No actual native or external tool execution was performed.");
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await issued.Dispatch.CompleteObservedAsync(outcome, cancelled.Token));
        Assert.Equal(before, await File.ReadAllBytesAsync(Path.Combine(_root, "home.json")));
        _principal.Value = "replacement-controlled-OS-principal";
        var finished = await issued.Dispatch.FinishAuditAsync(default);
        Assert.True(finished.OutcomeKnown); Assert.True(finished.AuditRecorded);
        var saved = await File.ReadAllBytesAsync(Path.Combine(_root, "home.json"));
        Assert.True((await issued.Dispatch.FinishAuditAsync(default)).AuditRecorded);
        Assert.Equal(saved, await File.ReadAllBytesAsync(Path.Combine(_root, "home.json")));
        var actual = (await _permissions.ReadRequestObservationAsync(issued.Review.RequestId))!;
        Assert.Equal(HomePermissionRequestState.Cancelled, actual.State); Assert.Equal(outcome.Code, actual.ResultCode);
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await issued.Dispatch.CompleteObservedAsync(
            outcome with { Kind = WebMcpObservedOutcomeKind.Succeeded, Code = "CHANGED_OUTCOME" }, default));
        Assert.Null(await issued.Dispatch.EnterFinalDispatchAsync(Request(), default));
        Assert.Equal(saved, await File.ReadAllBytesAsync(Path.Combine(_root, "home.json")));
    }
    private sealed class Principal : ITrustedHostPrincipalSource
    {
        public string Value = "controlled-browser-home-OS-principal";
        public ValueTask<string?> GetPrincipalAsync(CancellationToken ct) { ct.ThrowIfCancellationRequested(); return ValueTask.FromResult<string?>(Value); }
    }
    private sealed class Host : IOriginalSessionOwnedBrowserHost, IBrowserNativeEntryObservation
    {
        public BrowserSnapshot State { get; } = new(null, "controlled", false, false, false, "controlled");
        public event EventHandler<BrowserSnapshot>? StateChanged { add { } remove { } }
        public async Task<string?> ExecuteOwnedScriptAsync(string script, IBrowserOwnedScriptDispatchAdmission admission, CancellationToken ct)
        {
            await using var lease = await admission.AcquireAsync(this, ct) ?? throw new UnauthorizedAccessException();
            if (!await lease.CheckAsync(ct)) throw new UnauthorizedAccessException();
            var observed = JsonSerializer.Serialize(new { Document, ToolName = Tool().Name, InputSchema = Tool().InputSchema });
            if (!await lease.CheckAsync(ct)) throw new UnauthorizedAccessException(); return observed;
        }
        public Task<string?> EvaluateObservationAsync(string script, CancellationToken ct) => throw new NotSupportedException("No nested native entry.");
        public Task<string?> ExecuteScriptGuardedAsync(string script, IBrowserScriptDispatchAdmission admission, CancellationToken ct) => throw new NotSupportedException();
        public Task<string?> ExecuteScriptAsync(string script, CancellationToken ct) => throw new InvalidOperationException("No legacy fallback or actual external tool.");
        public Task NavigateAsync(Uri address, CancellationToken ct) => Task.CompletedTask;
        public Task GoBackAsync(CancellationToken ct) => Task.CompletedTask;
        public Task GoForwardAsync(CancellationToken ct) => Task.CompletedTask;
        public Task ReloadAsync(CancellationToken ct) => Task.CompletedTask;
        public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
        public Task OpenDeveloperToolsAsync(CancellationToken ct) => Task.CompletedTask;
    }
    private sealed class Paths : IAppPaths
    {
        public string DataDirectory => "/unused"; public string DatabasePath => "/unused/database";
        public string BrowserProfileDirectory => "/unused/browser"; public string AttachmentsDirectory => "/unused/attachments";
        public string LogsDirectory => "/unused/logs"; public string LegacyStatePath => "/unused/legacy";
    }
    public void Dispose() { _browser.Dispose(); if (Directory.Exists(_root)) Directory.Delete(_root, true); }
}
