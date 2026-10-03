using System.Text.Json;
using Dulche.Runtime;
using Haven.Core;
using HavenOS.Home.PermissionsTrustNotifications;
using Avalonia.Headless.XUnit;
using Haven.Application;
using HavenOS.Home.Core;
using HavenOS.Home.NativeUI;

namespace Haven.Desktop.Tests;

// Controlled native presentation boundary, not an installed provider, actual Home grant or durable audit proof.
public sealed class HomeModelPickerOriginalSessionTests
{
    [AvaloniaFact]
    public async Task Actor_change_removes_private_items_but_retains_original_audit_only_finish()
    {
        var actors = new Actors(); var owner = new Owner();
        using var session = new HomeModelPickerOriginalSessionProvider(owner, actors);
        Assert.True(await session.BeginAsync());
        using var bindings = new HomeModelPickerBindings(session);
        await bindings.OpenAsync();
        Assert.True(bindings.TryGetItemValue(owner.Route, "RouteId", out _));
        actors.Current = actors.Current with { AuthenticationRevision = "new-session" };
        Assert.False(await bindings.RevalidateAsync());
        Assert.False(bindings.TryGetItemValue(owner.Route, "RouteId", out _));
        Assert.False(bindings.TrySetValue("Query", "private-query"));
        Assert.True(bindings.TryGetValue("Routes", out var routes));
        Assert.Empty(Assert.IsAssignableFrom<System.Collections.IEnumerable>(routes).Cast<object>());
        Assert.True(bindings.TryGetValue("CanFinishAudit", out var finish)); Assert.Equal(true, finish);
        await bindings.DispatchAsync("Refresh", null); Assert.Equal(1, owner.Reads);
        await bindings.DispatchAsync("FinishAudit", null);
        Assert.Equal("original-pending-audit", owner.AuditRequest);
        Assert.Equal(0, owner.Updates);
        Assert.True(bindings.TryGetValue("CanFinishAudit", out finish)); Assert.Equal(false, finish);
    }
    [AvaloniaFact]
    public async Task Suspended_owner_read_cannot_publish_after_original_actor_changes()
    {
        var actors = new Actors(); var owner = new Owner { Suspend = true };
        using var session = new HomeModelPickerOriginalSessionProvider(owner, actors);
        Assert.True(await session.BeginAsync());
        using var bindings = new HomeModelPickerBindings(session);
        var open = bindings.OpenAsync(); await owner.Entered.Task;
        actors.Current = actors.Current with { AuthenticationRevision = "replacement" };
        owner.Release.TrySetResult(); await open;
        Assert.False(bindings.TryGetItemValue(owner.Route, "RouteId", out _));
        Assert.True(bindings.TryGetValue("Routes", out var routes));
        Assert.Empty(Assert.IsAssignableFrom<System.Collections.IEnumerable>(routes).Cast<object>());
        Assert.Equal(0, owner.Updates);
    }
    [AvaloniaFact]
    public async Task Disposed_native_bindings_cannot_repopulate_from_delayed_owner_read()
    {
        var actors = new Actors(); var owner = new Owner { Suspend = true };
        using var session = new HomeModelPickerOriginalSessionProvider(owner, actors);
        Assert.True(await session.BeginAsync());
        using var bindings = new HomeModelPickerBindings(session);
        var open = bindings.OpenAsync(); await owner.Entered.Task;
        bindings.Dispose(); owner.Release.TrySetResult();
        try { await open; } catch (OperationCanceledException) { }
        Assert.False(bindings.TryGetItemValue(owner.Route, "RouteId", out _));
        Assert.True(bindings.TryGetValue("Routes", out var routes));
        Assert.Empty(Assert.IsAssignableFrom<System.Collections.IEnumerable>(routes).Cast<object>());
    }
    [AvaloniaFact]
    public async Task Actual_Home_committed_route_audit_survives_native_session_expiry_without_second_CAS()
    {
        var root = Path.Combine(Path.GetTempPath(), "astra-original-model-native-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var physical = new FileHomeCoreStateStore(Path.Combine(root, "home.json"));
            var principal = new Principal(); var profiles = new HomeLocalProfileIdentity(physical, principal);
            var fault = new RouteAuditFaultStore(physical);
            var routes = new HomeVersionedModelRouteRepository(fault);
            var permissions = new HomePermissionTrustService(fault, new HomeModelRouteActionPolicies().TryGet);
            var resources = new ResourceAuthorizationService(profiles,
                [new HomeModelRouteOwner(profiles, routes), new HomeModelRouteProfileOwner(profiles)]);
            var provider = new HomeModelPickerFeatureProvider(profiles, routes, new Catalogue(), new Privacy(), resources,
                new HomeResourceOperationBroker(resources, permissions));
            using var session = new HomeModelPickerOriginalSessionProvider(provider, profiles);
            Assert.True(await session.BeginAsync());
            var original = (await profiles.GetCurrentAsync(default))!;
            var draft = Assert.Single((await session.GetSnapshotAsync("User", "Active")).Value!.Routes);
            var edit = new HomeModelRouteEdit(draft with { Version = 1, Candidates = [new("local", "text", null, true, 0)] }, 0);
            var pending = await session.UpdateRouteAsync(edit);
            var request = pending.Value!.PendingApprovalRequestId!;
            Assert.True((await permissions.DecideAsync(request, HomeApprovalChoice.Accept)).Succeeded);
            fault.FailNextCompletion = true;
            var saved = await session.UpdateRouteAsync(edit with { ApprovalRequestId = request });
            Assert.True(saved.Succeeded); Assert.Equal("SavedAuditPending", saved.Code);
            Assert.Equal(request, saved.Value!.PendingAuditRequestId);
            var canonical = JsonSerializer.Serialize(await routes.GetAsync(draft.RouteId, default));
            using var bindings = new HomeModelPickerBindings(session);
            await bindings.OpenAsync();
            Assert.True(bindings.TryGetValue("CanFinishAudit", out var finish)); Assert.Equal(true, finish);
            principal.Value = "replacement-fixture-host-principal";
            await Assert.ThrowsAsync<UnauthorizedAccessException>(async () => await profiles.GetCurrentAsync(default));
            Assert.False(await bindings.RevalidateAsync());
            await bindings.DispatchAsync("Save", null);
            Assert.Equal(1, fault.RouteWrites);
            await bindings.DispatchAsync("FinishAudit", null);
            Assert.Equal(1, fault.RouteWrites);
            Assert.Equal(canonical, JsonSerializer.Serialize(await routes.GetAsync(draft.RouteId, default)));
            Assert.Equal(HomePermissionRequestState.Succeeded, (await permissions.GetAuthorizationAsync(request)).State);
            Assert.Empty((await permissions.GetSnapshotAsync()).PendingRequests);
            Assert.True(bindings.TryGetValue("CanFinishAudit", out finish)); Assert.Equal(false, finish);
        }
        finally { Directory.Delete(root, true); }
    }
    private sealed class Actors : IAuthenticatedResourceActorSource
    {
        public AuthenticatedResourceActor Current = new("actor", "profile", null, null, "original");
        public ValueTask<AuthenticatedResourceActor?> GetCurrentAsync(CancellationToken token) => ValueTask.FromResult<AuthenticatedResourceActor?>(Current);
    }
    private sealed class Owner : IHomeModelPickerFeatureProvider
    {
        public readonly HomeModelRouteContract Route = new("private-route", 1, "User", "Active", null, null,
            [new("controlled-provider", "private-model", null, true, 0)], JsonSerializer.SerializeToElement(new { }));
        public bool Suspend; public int Reads; public int Updates; public string? AuditRequest;
        public readonly TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<HomeCoreOperationResult<HomeModelPickerSnapshot>> GetSnapshotAsync(string scope, string category, CancellationToken cancellationToken = default)
        {
            Reads++; Entered.TrySetResult(); if (Suspend) await Release.Task;
            return new(true, "Ready", "Controlled fixture", new(1, scope, category, [Route]) { PendingAuditRequestId = "original-pending-audit" });
        }
        public Task<HomeCoreOperationResult<HomeModelCataloguePage>> GetCatalogueAsync(string? query = null, CancellationToken cancellationToken = default)
            => Task.FromResult(new HomeCoreOperationResult<HomeModelCataloguePage>(true, "Ready", "Controlled fixture", new([], 0, 20, false, 1)));
        public Task<HomeCoreOperationResult<HomeModelPickerSnapshot>> UpdateRouteAsync(HomeModelRouteEdit edit, CancellationToken cancellationToken = default)
        { Updates++; throw new InvalidOperationException("Legacy current-actor mutation must not be used."); }
        public Task<HomeCoreOperationResult<HomeModelRoutePreview>> PreviewResolutionAsync(HomeModelRoutePreviewRequest request, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("No model execution in this fixture.");
        public Task<HomeCoreOperationResult<object>> RetryAuditAsync(string requestId, CancellationToken cancellationToken = default)
        { AuditRequest = requestId; return Task.FromResult(new HomeCoreOperationResult<object>(true, "AuditRecorded", "Controlled acknowledgement", new object())); }
    }
    private sealed class RouteAuditFaultStore(IHomeCoreStateStore inner) : IHomeCoreStateStore
    {
        public bool FailNextCompletion;
        public bool AfterPublication = false;
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

}
