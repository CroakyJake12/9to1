using System.Text.Json;
using Dulche.Runtime;
using Haven.Application;
using Haven.Core;
using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;
using Xunit;
using NineToOne.Cui.AI;

namespace HavenOS.Home.Tests;

public sealed class HomeModelPickerFeatureProviderTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "astra-model-owner-" + Guid.NewGuid().ToString("N"));
    private readonly HomeLocalProfileIdentity _profiles;
    private readonly Principal _principal = new();
    private readonly FileHomeCoreStateStore _store;
    private readonly HomeVersionedModelRouteRepository _routes;
    private readonly HomePermissionTrustService _permissions;
    private readonly HomePersonalModelRoutes _personal;
    private readonly HomeModelPickerFeatureProvider _provider;
    private readonly Catalogue _catalogue = new();
    private readonly Privacy _privacy = new();
    public HomeModelPickerFeatureProviderTests()
    {
        Directory.CreateDirectory(_root);
        var store = _store = new FileHomeCoreStateStore(Path.Combine(_root, "home.json"));
        _profiles = new(store, _principal); _routes = new(store);
        var policies = new HomeModelRouteActionPolicies();
        _permissions = new(store, policies.TryGet);
        var resources = new ResourceAuthorizationService(_profiles, [new HomeModelRouteOwner(_profiles, _routes), new HomeModelRouteProfileOwner(_profiles)]);
        _personal = new(_profiles, _routes, resources);
        _provider = new(_profiles, _routes, _catalogue, _privacy, resources, new(resources, _permissions));
    }

    [Fact]
    public async Task Original_route_input_bounded_actual_enumeration_denies_before_Home_review()
    {
        var original = (await _profiles.GetCurrentAsync(default))!;
        var draft = Assert.Single((await _provider.GetSnapshotAsync("User", "Chat")).Value!.Routes);
        var values = new LyingCandidates();
        var before = await File.ReadAllBytesAsync(Path.Combine(_root, "home.json"));
        var result = await _provider.UpdateRouteForActorAsync(original, new(draft with { Version = 1, Candidates = values }, 0));
        Assert.False(result.Succeeded); Assert.Equal(257, values.Consumed);
        Assert.Empty((await _permissions.GetSnapshotAsync()).PendingRequests);
        Assert.Equal(before, await File.ReadAllBytesAsync(Path.Combine(_root, "home.json")));
    }
    private sealed class LyingCandidates : IReadOnlyList<HomeModelRouteCandidate>
    {
        public int Count => 1; public int Consumed;
        public HomeModelRouteCandidate this[int index] => throw new NotSupportedException();
        public IEnumerator<HomeModelRouteCandidate> GetEnumerator()
        {
            for (var i = 0; i < 1_000_000; i++) { Consumed++; yield return new("local", "one", null, true, i); }
        }
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    [Fact]
    public async Task Original_route_actor_port_denies_changed_principal_before_creating_review()
    {
        var original = (await _profiles.GetCurrentAsync(default))!;
        var draft = Assert.Single((await _provider.GetSnapshotAsync("User", "Chat")).Value!.Routes);
        var edit = new HomeModelRouteEdit(draft with { Version = 1, Candidates = [new("local", "one", null, true, 0)] }, 0);
        _principal.Value = "different-real-fixture-principal";
        var denied = await _provider.UpdateRouteForActorAsync(original, edit);
        Assert.False(denied.Succeeded);
        Assert.Empty((await _permissions.GetSnapshotAsync()).PendingRequests);
        Assert.Null(await _routes.GetAsync(draft.RouteId, default));
    }

    [Fact]
    public async Task Original_route_actor_port_creates_only_same_actor_exact_review()
    {
        var original = (await _profiles.GetCurrentAsync(default))!;
        var draft = Assert.Single((await _provider.GetSnapshotAsync("User", "Chat")).Value!.Routes);
        var edit = new HomeModelRouteEdit(draft with { Version = 1, Candidates = [new("local", "one", null, true, 0)] }, 0);
        var pending = await _provider.UpdateRouteForActorAsync(original, edit);
        Assert.Equal("ApprovalRequired", pending.Code);
        var request = Assert.Single((await _permissions.GetSnapshotAsync()).PendingRequests);
        Assert.Equal(original.ActorId, request.Caller.CallerId);
        Assert.Equal(original.AuthenticationRevision, request.Caller.IdentityVersion);
        Assert.Null(await _routes.GetAsync(draft.RouteId, default));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Confirmed_route_save_retains_exact_audit_retry_without_repeating_guarded_CAS(bool afterPublication)
    {
        var fault = new RouteAuditFaultStore(_store) { AfterPublication = afterPublication };
        var routes = new HomeVersionedModelRouteRepository(fault);
        var permissions = new HomePermissionTrustService(fault, new HomeModelRouteActionPolicies().TryGet);
        var resources = new ResourceAuthorizationService(_profiles,
            [new HomeModelRouteOwner(_profiles, routes), new HomeModelRouteProfileOwner(_profiles)]);
        var provider = new HomeModelPickerFeatureProvider(_profiles, routes, _catalogue, _privacy, resources, new(resources, permissions));
        var draft = Assert.Single((await provider.GetSnapshotAsync("User", "Chat")).Value!.Routes);
        var edit = new HomeModelRouteEdit(draft with { Version = 1, Candidates = [new("local", "one", null, true, 0)] }, 0);
        var pending = await provider.UpdateRouteAsync(edit);
        var requestId = pending.Value!.PendingApprovalRequestId!;
        Assert.True((await permissions.DecideAsync(requestId, HomeApprovalChoice.Accept)).Succeeded);
        fault.FailNextCompletion = true;
        var saved = await provider.UpdateRouteAsync(edit with { ApprovalRequestId = requestId });
        Assert.True(saved.Succeeded);
        Assert.Equal("SavedAuditPending", saved.Code);
        Assert.Equal(requestId, saved.Value!.PendingAuditRequestId);
        Assert.Equal(requestId, (await provider.GetSnapshotAsync("User", "Chat")).Value!.PendingAuditRequestId);
        var canonical = JsonSerializer.Serialize(await routes.GetAsync(draft.RouteId, default));
        Assert.Equal(1, fault.RouteWrites);
        Assert.Equal("AuditPending", (await provider.UpdateRouteAsync(edit with { ApprovalRequestId = requestId })).Code);
        // The original outcome audit may be settled after authentication changes; it conveys no new route authority.
        _principal.Value = "changed-principal";
        var retried = await provider.RetryAuditAsync(requestId);
        Assert.True(retried.Succeeded);
        Assert.Null(retried.Value);
        Assert.Equal(1, fault.RouteWrites);
        Assert.Equal(canonical, JsonSerializer.Serialize(await routes.GetAsync(draft.RouteId, default)));
        Assert.Equal(HomePermissionRequestState.Succeeded, (await permissions.GetAuthorizationAsync(requestId)).State);
        Assert.Equal("AuditNotOwned", (await provider.RetryAuditAsync(requestId)).Code);
        Assert.Equal("AuditNotOwned", (await provider.RetryAuditAsync("fictional-imported-request")).Code);
    }

    private sealed class RouteAuditFaultStore(IHomeCoreStateStore inner) : IHomeCoreStateStore
    {
        public bool FailNextCompletion;
        public bool AfterPublication;
        public int RouteWrites;
        public Task<HomeStateReadResult> ReadAsync(CancellationToken ct = default) => inner.ReadAsync(ct);
        public Task<HomeStateWriteResult> WriteGuardedAsync(HomeCoreStateRecord record, long expected,
            AuthenticatedResourceActor actor, IHomeStateCommitActorGuard guard, CancellationToken ct = default)
        {
            if (record.RecordType == "home.model-route") RouteWrites++;
            return inner.WriteGuardedAsync(record, expected, actor, guard, ct);
        }
        public async Task<HomeStateWriteResult> WriteAsync(HomeCoreStateRecord record, long expected, CancellationToken ct = default)
        {
            if (FailNextCompletion && record.Payload.GetRawText().Contains("HOME_MODEL_ROUTE_COMMITTED", StringComparison.Ordinal))
            {
                FailNextCompletion = false;
                if (AfterPublication) Assert.True((await inner.WriteAsync(record, expected, ct)).IsSuccess);
                throw new UnauthorizedAccessException("Controlled durable completion audit acknowledgment failure.");
            }
            return await inner.WriteAsync(record, expected, ct);
        }
    }

    [Fact]
    public async Task Approved_route_does_not_commit_if_real_principal_changes_while_Home_lease_waits()
    {
        var paused = new PausedRoutes(_routes, Path.Combine(_root, "home.json"));
        var resources = new ResourceAuthorizationService(_profiles, [new HomeModelRouteOwner(_profiles, _routes), new HomeModelRouteProfileOwner(_profiles)]);
        var provider = new HomeModelPickerFeatureProvider(_profiles, paused, _catalogue, _privacy, resources,
            new(resources, _permissions));
        var draft = Assert.Single((await provider.GetSnapshotAsync("User", "Chat")).Value!.Routes);
        var edit = new HomeModelRouteEdit(draft with { Version = 1, Candidates = [new("local", "one", null, true, 0)] }, 0);
        var pending = await provider.UpdateRouteAsync(edit);
        var request = pending.Value!.PendingApprovalRequestId!;
        Assert.True((await _permissions.DecideAsync(request, HomeApprovalChoice.Accept)).Succeeded);
        var saving = provider.UpdateRouteAsync(edit with { ApprovalRequestId = request });
        await paused.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        _principal.Value = "different-os-principal";
        paused.Release.TrySetResult();
        Assert.False((await saving).Succeeded);
        Assert.Empty(await _routes.ListAsync(default));
        Assert.Equal(HomePermissionRequestState.Failed, (await _permissions.GetAuthorizationAsync(request)).State);
    }

    private sealed class PausedRoutes(HomeVersionedModelRouteRepository inner, string path) : IHomeGuardedModelRouteRepository
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<ConfiguredModelRoute?> GetAsync(string id, CancellationToken ct) => inner.GetAsync(id, ct);
        public Task<IReadOnlyList<ConfiguredModelRoute>> ListAsync(CancellationToken ct) => inner.ListAsync(ct);
        public Task<bool> TrySaveAsync(ConfiguredModelRoute route, long revision, CancellationToken ct) => inner.TrySaveAsync(route, revision, ct);
        public async Task<bool> TrySaveGuardedAsync(ConfiguredModelRoute route, long revision, AuthenticatedResourceActor actor,
            IHomeStateCommitActorGuard guard, CancellationToken ct)
        {
            using var held = new FileStream(path + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            var saving = inner.TrySaveGuardedAsync(route, revision, actor, guard, ct);
            Entered.TrySetResult();
            await Release.Task.WaitAsync(ct);
            held.Dispose();
            return await saving;
        }
    }

    [Fact]
    public async Task Approval_displays_exact_candidate_order_enabled_state_and_before_after_privacy_policy()
    {
        var draft = Assert.Single((await _provider.GetSnapshotAsync("User", "Voice")).Value!.Routes);
        var first = new HomeModelRouteEdit(draft with { Version = 1,
            Candidates = [new("local", "one", null, true, 0), new("remote", "two", null, false, 1)],
            Policy = JsonSerializer.SerializeToElement(new ProviderPolicy(AllowRemote: false)) }, 0);
        var pending = await _provider.UpdateRouteAsync(first);
        var firstId = pending.Value!.PendingApprovalRequestId!;
        var initialPreview = (await _permissions.GetSnapshotAsync()).PendingRequests.Single(request => request.RequestId == firstId).Impact.ChangePreview!;
        Assert.Contains("No saved route.", initialPreview);
        Assert.Contains("Priority 0: enabled provider \"local\", model \"one\"", initialPreview);
        Assert.Contains("Priority 1: disabled provider \"remote\", model \"two\"", initialPreview);
        Assert.Contains("\"AllowRemote\":false", initialPreview);
        Assert.True((await _permissions.DecideAsync(firstId, HomeApprovalChoice.Accept)).Succeeded);
        Assert.True((await _provider.UpdateRouteAsync(first with { ApprovalRequestId = firstId })).Succeeded);
        var next = first with { ExpectedRevision = 1, Route = first.Route with { Version = 2,
            Candidates = [new("remote", "two", null, true, 0), new("local", "one", null, true, 1)],
            Policy = JsonSerializer.SerializeToElement(new ProviderPolicy(AllowRemote: true, AllowCloud: true, AllowPrivateContextToCloud: true)) } };
        var changed = await _provider.UpdateRouteAsync(next);
        var preview = (await _permissions.GetSnapshotAsync()).PendingRequests.Single(request => request.RequestId == changed.Value!.PendingApprovalRequestId).Impact.ChangePreview!;
        Assert.NotEqual(initialPreview, preview);
        Assert.Contains("Before:\nRevision 1", preview);
        Assert.Contains("After:\nRevision 2", preview);
        Assert.Contains("Priority 0: enabled provider \"remote\", model \"two\"", preview);
        Assert.Contains("\"AllowRemote\":false", preview);
        Assert.Contains("\"AllowRemote\":true", preview);
        Assert.Contains("\"AllowPrivateContextToCloud\":true", preview);
        Assert.Equal(1, (await _routes.GetAsync(draft.RouteId, default))!.Revision);
    }

    [Fact]
    public async Task New_profile_is_a_real_owned_draft_and_save_requires_exact_Home_approval_then_disk_CAS()
    {
        var snapshot = Assert.IsType<HomeModelPickerSnapshot>((await _provider.GetSnapshotAsync("User", "Active")).Value);
        var draft = Assert.Single(snapshot.Routes);
        Assert.Equal(0, snapshot.Revision); Assert.Empty(await _routes.ListAsync(default));
        Assert.Null(await _personal.GetAsync(ModelCapabilityCategory.Active));
        var actor = await _profiles.GetCurrentAsync(default);
        Assert.Equal(actor!.ProfileId, draft.ScopeId);
        var edit = new HomeModelRouteEdit(draft with { Version = 1, Candidates = [new("local", "text", null, true, 0)] }, 0);
        var pending = await _provider.UpdateRouteAsync(edit);
        Assert.Equal("ApprovalRequired", pending.Code);
        var requestId = Assert.IsType<string>(pending.Value?.PendingApprovalRequestId);
        Assert.Empty(await _routes.ListAsync(default));
        Assert.True((await _permissions.DecideAsync(requestId, HomeApprovalChoice.Accept)).Succeeded);
        var saved = await _provider.UpdateRouteAsync(edit with { ApprovalRequestId = requestId });
        Assert.True(saved.Succeeded); Assert.Equal(1, saved.Revision);
        Assert.Equal(HomePermissionRequestState.Succeeded, (await _permissions.GetAuthorizationAsync(requestId)).State);
        var persisted = Assert.IsType<ConfiguredModelRoute>(await _routes.GetAsync(draft.RouteId, default));
        Assert.Equal(actor.ProfileId, persisted.ScopeId);
        var bridge = new HomeAppAiServices(_catalogue, _store,
            new(actor.ActorId, "Current native profile", "os", actor.AuthenticationRevision, true), new Graph(), new Invocations(), personalRoutes: _personal);
        Assert.Equal("local:text", (await bridge.GetSelectionAsync(default))!.ModelId);
        Assert.False(await bridge.SelectAsync("local:text", default));
        Assert.Equal(persisted.Revision, (await _routes.GetAsync(draft.RouteId, default))!.Revision);
        Assert.Equal(0, _catalogue.RemoteQueries);
        var consumed = Assert.IsType<ConfiguredModelRoute>(await _personal.GetAsync(ModelCapabilityCategory.Active));
        Assert.Equal(persisted.RouteId, consumed.RouteId); Assert.Equal(persisted.Revision, consumed.Revision);
        Assert.Equal("text", Assert.Single(consumed.Candidates).Model.ModelId);
        Assert.Null(Assert.Single(persisted.Candidates).Model.ArtifactRevision);
        Assert.Equal("Conflict", (await _provider.UpdateRouteAsync(edit with { ApprovalRequestId = requestId })).Code);
    }

    [Fact]
    public async Task Foreign_scope_unsupported_owner_and_changed_approved_arguments_cannot_write()
    {
        var draft = Assert.Single((await _provider.GetSnapshotAsync("User", "Background")).Value!.Routes);
        var edit = new HomeModelRouteEdit(draft with { Version = 1, Candidates = [new("local", "text", null, true, 0)] }, 0);
        Assert.False((await _provider.UpdateRouteAsync(edit with { Route = edit.Route with { ScopeId = "foreign-profile" } })).Succeeded);
        Assert.Equal("OwnerUnavailable", (await _provider.GetSnapshotAsync("Agent", "Background")).Code);
        var pending = await _provider.UpdateRouteAsync(edit);
        var requestId = pending.Value!.PendingApprovalRequestId!;
        Assert.True((await _permissions.DecideAsync(requestId, HomeApprovalChoice.Accept)).Succeeded);
        var changed = edit with { ApprovalRequestId = requestId, Route = edit.Route with { Candidates = [new("remote", "text", null, true, 0)] } };
        Assert.False((await _provider.UpdateRouteAsync(changed)).Succeeded);
        Assert.Empty(await _routes.ListAsync(default));
    }

    [Fact]
    public async Task Local_only_filters_before_catalogue_discovery_and_does_not_invent_artifact_versions()
    {
        _privacy.Current = PrivacyPreferences.Default with { LocalOnlyMode = true };
        var local = await _provider.GetCatalogueAsync();
        Assert.True(local.Succeeded);
        Assert.Equal(0, _catalogue.RemoteQueries);
        Assert.All(local.Value!.Items, item => { Assert.True(item.IsLocal); Assert.Null(item.ArtifactRevision); });
        _privacy.Current = PrivacyPreferences.Default;
        var available = await _provider.GetCatalogueAsync();
        Assert.Equal(1, _catalogue.RemoteQueries);
        Assert.Equal(2, available.Value!.Items.Count);
    }

    [Fact]
    public async Task Legacy_unowned_route_is_preserved_and_never_claimed_by_the_current_profile()
    {
        var legacy = new ConfiguredModelRoute("home.active", 1, ModelRouteScope.User, "native-user", ModelCapabilityCategory.Active,
            [new(new("local", "legacy"))], new());
        Assert.True(await _routes.TrySaveAsync(legacy, 0, default));
        var snapshot = (await _provider.GetSnapshotAsync("User", "Active")).Value!;
        Assert.DoesNotContain(snapshot.Routes, route => route.RouteId == legacy.RouteId);
        Assert.Equal("native-user", (await _routes.GetAsync("home.active", default))!.ScopeId);
        Assert.Null(await _personal.GetAsync(ModelCapabilityCategory.Active));
        Assert.False((await _provider.PreviewResolutionAsync(new("home.active", "Text", null, null, JsonSerializer.SerializeToElement(new { })))).Succeeded);
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
    private sealed class Principal : ITrustedHostPrincipalSource
    {
        public string Value { get; set; } = "fixture-os-principal";
        public ValueTask<string?> GetPrincipalAsync(CancellationToken cancellationToken) => ValueTask.FromResult<string?>(Value);
    }
    private sealed class Privacy : IPrivacyPreferenceStore
    {
        public PrivacyPreferences Current { get; set; } = PrivacyPreferences.Default;
        public Task UpdateAsync(PrivacyPreferences preferences, CancellationToken cancellationToken) { Current = preferences; return Task.CompletedTask; }
    }
    private sealed class Catalogue : IModelProviderRegistry
    {
        public int RemoteQueries { get; private set; }
        public IReadOnlyList<IModelProvider> Providers => [];
        public IModelProvider? Find(string providerId) => null;
        public IModelProvider GetRequired(string providerId) => throw new NotSupportedException();
        public Task<IReadOnlyList<ProviderModelDescriptor>> GetModelsAsync(CancellationToken cancellationToken) => throw new InvalidOperationException("Unfiltered discovery is forbidden in this fixture.");
        public Task<IReadOnlyList<ProviderModelDescriptor>> GetModelsAsync(ModelCataloguePolicy policy, CancellationToken cancellationToken)
        {
            var result = new List<ProviderModelDescriptor>();
            if (policy.AllowLocal) result.Add(Model("local", true));
            if (policy.AllowRemote) { RemoteQueries++; result.Add(Model("remote", false)); }
            return Task.FromResult<IReadOnlyList<ProviderModelDescriptor>>(result);
        }
        private static ProviderModelDescriptor Model(string provider, bool local) => new(provider, local,
            new("text", 0, "fixture", "unknown", "unknown", new HashSet<ToolCapability> { ToolCapability.Text }, DateTimeOffset.UnixEpoch));
    }
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
}
