using Haven.Application;
using System.Text.Json;
using HavenOS.Home.Core;
using Xunit;

namespace HavenOS.Home.Tests;

public sealed class HomeNativeWidgetRegistryTests
{
    [Fact]
    public void Surface_rejects_large_raw_values_before_copying_and_detaches_accepted_data()
    {
        var reference = new HomeNativeWidgetReference("clock", Guid.NewGuid(), "signed", "clock", "revision1");
        using var small = JsonDocument.Parse("{\"value\":\"original\"}");
        var accepted = HomeNativeWidgetSurface.Capture(reference, "clock.surface.v1", "<Text />",
            new Dictionary<string, JsonElement> { ["value"] = small.RootElement.GetProperty("value") });
        using var large = JsonDocument.Parse(JsonSerializer.Serialize(new string('x', 4 * 1024 * 1024)));
        var input = new Dictionary<string, JsonElement> { ["value"] = large.RootElement };
        var before = GC.GetAllocatedBytesForCurrentThread();
        Assert.Throws<InvalidDataException>(() => HomeNativeWidgetSurface.Capture(reference,
            "clock.surface.v1", "<Text />", input));
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.True(allocated < 1024 * 1024, $"Rejected raw data allocated {allocated} bytes during capture.");
        small.Dispose();
        Assert.Equal("original", accepted.Data["value"].GetString());
    }

    private static readonly HomeNativeObservedPeer Observed = new(123, "unix-euid:1000");
    private static HomeNativeWidgetDefinition Definition() => new("clock", "revision1", "Clock", new(1, 1), new(2, 2),
        new(4, 4), "clock.configuration.v1", "clock.surface.v1", HomeNativeWidgetUpdateMode.Event, null,
        ["clock.open"], [new("fixture.clock", "clock1", "r1", ResourceAccess.Read)]);

    [Fact]
    public async Task Unavailable_verifier_never_promotes_declared_widget_or_copied_locator_to_live_owner()
    {
        var actors = new Actors();
        var registry = new HomeNativeWidgetRegistry(new UnavailableHomeNativeInstalledPeerVerifier(), actors, new(actors, [new Resolver()]));
        Assert.Null(await registry.RegisterAsync(Observed, [Definition()]));
        Assert.Empty(await registry.ListAsync());
        Assert.Null(await registry.ResolveAsync(new("clock", Guid.NewGuid(), "signed", "clock", "revision1")));
    }

    [Fact]
    public async Task Current_verified_owner_resource_revision_actor_and_live_lifetime_are_all_required()
    {
        var actors = new Actors(); var verifier = new Verifier(); var resolver = new Resolver();
        var registry = new HomeNativeWidgetRegistry(verifier, actors, new(actors, [resolver]));
        using var registration = await registry.RegisterAsync(Observed, [Definition()]);
        Assert.NotNull(registration);
        var item = Assert.Single(await registry.ListAsync());
        Assert.Equal(verifier.Owner.InstalledApplicationId, item.Reference.InstalledApplicationId);
        Assert.Equal(Definition().ActionIds, item.Definition.ActionIds);
        Assert.NotNull(await registry.ResolveAsync(item.Reference));
        resolver.Allowed = false;
        Assert.Empty(await registry.ListAsync());
        Assert.Null(await registry.ResolveAsync(item.Reference));
        resolver.Allowed = true; resolver.Revision = "r2";
        Assert.Null(await registry.ResolveAsync(item.Reference));
        resolver.Revision = "r1";
        var oldOwner = verifier.Owner;
        verifier.Owner = oldOwner with { InstallationRevision = "replacement" };
        Assert.Null(await registry.ResolveAsync(item.Reference));
        verifier.Owner = oldOwner with { AllowedServiceIds = new HashSet<string>() };
        Assert.Null(await registry.ResolveAsync(item.Reference));
        verifier.Owner = oldOwner;
        actors.Current = actors.Current with { AuthenticationRevision = "new-session" };
        Assert.Null(await registry.ResolveAsync(item.Reference));
        actors.Current = actors.Current with { AuthenticationRevision = "session" };
        Assert.NotNull(await registry.ResolveAsync(item.Reference));
        registration!.Dispose();
        Assert.Null(await registry.ResolveAsync(item.Reference));
    }

    [Fact]
    public async Task Registration_captures_mutable_metadata_before_await_and_returns_immutable_collections()
    {
        var actors = new Actors(); var verifier = new Verifier();
        var actions = new List<string> { "clock.open" };
        var scopes = new List<ResourceScope> { new("fixture.clock", "clock1", "r1", ResourceAccess.Read) };
        var definitions = new List<HomeNativeWidgetDefinition> { Definition() with { ActionIds = actions, DataScopes = scopes } };
        verifier.OnVerify = () => { actions[0] = "unreviewed.write"; scopes.Clear(); definitions.Clear(); };
        var registry = new HomeNativeWidgetRegistry(verifier, actors, new(actors, [new Resolver()]));
        using var registration = await registry.RegisterAsync(Observed, definitions);
        var item = Assert.Single(await registry.ListAsync());
        Assert.Equal("clock.open", Assert.Single(item.Definition.ActionIds));
        Assert.Single(item.Definition.DataScopes);
        Assert.Throws<NotSupportedException>(() => ((IList<string>)item.Definition.ActionIds)[0] = "changed");
    }

    [Fact]
    public async Task Malformed_or_duplicate_declarations_are_rejected_and_duplicate_live_owners_are_ambiguous()
    {
        var actors = new Actors(); var registry = new HomeNativeWidgetRegistry(new Verifier(), actors, new(actors, [new Resolver()]));
        await Assert.ThrowsAsync<InvalidDataException>(() => registry.RegisterAsync(Observed, [Definition(), Definition()]).AsTask());
        await Assert.ThrowsAsync<InvalidDataException>(() => registry.RegisterAsync(Observed,
            [Definition() with { UpdateMode = HomeNativeWidgetUpdateMode.Interval, IntervalSeconds = 1 }]).AsTask());
        await Assert.ThrowsAsync<InvalidDataException>(() => registry.RegisterAsync(Observed,
            [Definition() with { DataScopes = [new("fixture.clock", "clock1", "r1", ResourceAccess.Write)] }]).AsTask());
        using var first = await registry.RegisterAsync(Observed, [Definition()]);
        var item = Assert.Single(await registry.ListAsync());
        using var second = await registry.RegisterAsync(Observed, [Definition()]);
        Assert.Empty(await registry.ListAsync());
        Assert.Null(await registry.ResolveAsync(item.Reference));
        second!.Dispose();
        Assert.NotNull(await registry.ResolveAsync(item.Reference));
    }

    [Fact]
    public async Task Owner_change_during_registration_or_resource_check_denies_delivery()
    {
        var actors = new Actors(); var verifier = new Verifier(); var resolver = new Resolver();
        var registry = new HomeNativeWidgetRegistry(verifier, actors, new(actors, [resolver]));
        verifier.OnVerify = () => verifier.Owner = verifier.Owner with { InstallationRevision = Guid.NewGuid().ToString() };
        Assert.Null(await registry.RegisterAsync(Observed, [Definition()]));
        verifier.OnVerify = null;
        using var registration = await registry.RegisterAsync(Observed, [Definition()]);
        var item = Assert.Single(await registry.ListAsync());
        resolver.OnRead = () => verifier.Owner = verifier.Owner with { ExecutableIdentity = "replaced" };
        Assert.Null(await registry.ResolveAsync(item.Reference));
    }

    // These are controlled transport-boundary fixtures, not actual installed-peer/platform proof.
    [Fact]
    public async Task Runtime_capture_uses_same_unique_entry_bounds_and_detached_authored_data()
    {
        var actors = new Actors(); var verifier = new Verifier();
        var registry = new HomeNativeWidgetRegistry(verifier, actors, new(actors, [new Resolver()]));
        using var metadata = await registry.RegisterAsync(Observed, [Definition()]);
        var reference = Assert.Single(await registry.ListAsync()).Reference;
        Assert.Null(await registry.CaptureAsync(reference, actors.Current, new(2, 2), 200, 150));
        metadata!.Dispose();
        var endpoint = new RuntimeEndpoint();
        using var registration = await registry.RegisterRuntimeAsync(Observed, [Definition()], endpoint);
        Assert.Null(await registry.CaptureAsync(reference, actors.Current, new(5, 2), 200, 150));
        Assert.Null(await registry.CaptureAsync(reference, actors.Current, new(2, 2), double.NaN, 150));
        Assert.Equal(0, endpoint.Calls);
        var surface = await registry.CaptureAsync(reference, actors.Current, new(2, 2), 200, 150);
        Assert.NotNull(surface);
        Assert.Equal("fixture only", surface!.Data["caption"].GetString());
        Assert.Equal(1, endpoint.Calls);
        using var duplicate = await registry.RegisterAsync(Observed, [Definition()]);
        Assert.Null(await registry.CaptureAsync(reference, actors.Current, new(2, 2), 200, 150));
        Assert.Equal(1, endpoint.Calls);
    }

    [Theory]
    [InlineData("actor")]
    [InlineData("acl")]
    [InlineData("owner")]
    [InlineData("foreign-surface")]
    public async Task Runtime_capture_rechecks_original_actor_owner_and_resources_after_endpoint_await(string change)
    {
        var actors = new Actors(); var original = actors.Current; var verifier = new Verifier(); var resolver = new Resolver();
        var registry = new HomeNativeWidgetRegistry(verifier, actors, new(actors, [resolver]));
        var endpoint = new RuntimeEndpoint { WaitForRelease = true };
        using var registration = await registry.RegisterRuntimeAsync(Observed, [Definition()], endpoint);
        var reference = Assert.Single(await registry.ListAsync()).Reference;
        var capture = registry.CaptureAsync(reference, original, new(2, 2), 200, 150).AsTask();
        await endpoint.Entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        if (change == "actor") actors.Current = original with { AuthenticationRevision = "switched" };
        if (change == "acl") resolver.Allowed = false;
        if (change == "owner") verifier.Owner = verifier.Owner with { InstallationRevision = "replacement" };
        if (change == "foreign-surface") endpoint.ForeignSurface = true;
        endpoint.Release.TrySetResult(true);
        Assert.Null(await capture);
        Assert.Equal(1, endpoint.Calls);
    }

    [Fact]
    public async Task Runtime_disconnect_cancels_active_and_serialized_waiting_capture_without_delivering_stale_surface()
    {
        var actors = new Actors();
        var registry = new HomeNativeWidgetRegistry(new Verifier(), actors, new(actors, [new Resolver()]));
        var endpoint = new RuntimeEndpoint { WaitForCancellation = true };
        var registration = await registry.RegisterRuntimeAsync(Observed, [Definition()], endpoint);
        var reference = Assert.Single(await registry.ListAsync()).Reference;
        var first = registry.CaptureAsync(reference, actors.Current, new(2, 2), 200, 150).AsTask();
        await endpoint.Entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        var second = registry.CaptureAsync(reference, actors.Current, new(2, 2), 200, 150).AsTask();
        registration!.Dispose();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => second);
        Assert.Equal(1, endpoint.Calls);
        Assert.Null(await registry.CaptureAsync(reference, actors.Current, new(2, 2), 200, 150));
    }

    [Fact]
    public async Task A_duplicate_owner_registered_during_capture_prevents_original_surface_delivery()
    {
        var actors = new Actors();
        var registry = new HomeNativeWidgetRegistry(new Verifier(), actors, new(actors, [new Resolver()]));
        var endpoint = new RuntimeEndpoint { WaitForRelease = true };
        using var registration = await registry.RegisterRuntimeAsync(Observed, [Definition()], endpoint);
        var reference = Assert.Single(await registry.ListAsync()).Reference;
        var capture = registry.CaptureAsync(reference, actors.Current, new(2, 2), 200, 150).AsTask();
        await endpoint.Entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        using var duplicate = await registry.RegisterAsync(Observed, [Definition()]);
        endpoint.Release.TrySetResult(true);
        Assert.Null(await capture);
    }

    [Fact]
    public async Task Actual_Home_original_actor_retirement_denies_before_any_controlled_owner_resource_or_endpoint_observation()
    {
        var root = Path.Combine(Path.GetTempPath(), "widget-original-home-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root); var path = Path.Combine(root, "home.json");
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30)); var ct = deadline.Token;
        try
        {
            var home = new FileHomeCoreStateStore(path); var actors = new HomeLocalProfileIdentity(home, new OperatingSystemPrincipalSource());
            var original = (await actors.GetCurrentAsync(ct))!; var verifier = new Verifier(); var resolver = new Resolver();
            var endpoint = new RuntimeEndpoint(); var registry = new HomeNativeWidgetRegistry(verifier, actors, new(actors, [resolver]));
            using var registered = await registry.RegisterRuntimeAsync(Observed, [Definition()], endpoint, ct); Assert.NotNull(registered);
            var reference = Assert.Single(await registry.ListForActorAsync(original, ct)).Reference;
            await ReplaceActualProfileAsync(home, ct); var before = await File.ReadAllBytesAsync(path, ct);
            var ownerCalls = verifier.Calls; var resourceCalls = resolver.Calls;
            Assert.Null(await registry.ResolveForActorAsync(reference, original, ct));
            Assert.Empty(await registry.ListForActorAsync(original, ct));
            Assert.Null(await registry.CaptureAsync(reference, original, new(2, 2), 200, 150, ct));
            Assert.Equal(ownerCalls, verifier.Calls); Assert.Equal(resourceCalls, resolver.Calls); Assert.Equal(0, endpoint.Calls);
            Assert.Equal(before, await File.ReadAllBytesAsync(path, ct));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task Actual_Home_actor_change_during_controlled_owner_verification_cannot_read_resources_or_rebind_original_definition()
    {
        var root = Path.Combine(Path.GetTempPath(), "widget-original-home-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root); var path = Path.Combine(root, "home.json");
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30)); var ct = deadline.Token;
        try
        {
            var home = new FileHomeCoreStateStore(path); var actors = new HomeLocalProfileIdentity(home, new OperatingSystemPrincipalSource());
            var original = (await actors.GetCurrentAsync(ct))!; var verifier = new Verifier(); var resolver = new Resolver();
            var registry = new HomeNativeWidgetRegistry(verifier, actors, new(actors, [resolver]));
            using var registered = await registry.RegisterAsync(Observed, [Definition()], ct); Assert.NotNull(registered);
            var reference = Assert.Single(await registry.ListForActorAsync(original, ct)).Reference; var resourceCalls = resolver.Calls;
            verifier.OnVerify = () => { verifier.OnVerify = null; ReplaceActualProfileAsync(home, ct).GetAwaiter().GetResult(); };
            Assert.Null(await registry.ResolveForActorAsync(reference, original, ct));
            Assert.Equal(resourceCalls, resolver.Calls);
            var changed = await actors.GetCurrentAsync(ct); Assert.NotEqual(original, changed);
            Assert.Null(await registry.ResolveForActorAsync(reference, changed!, ct)); // The original entry cannot be adopted by the replacement profile.
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task Original_registration_actor_is_checked_before_verifier_and_cannot_rebind_after_retirement()
    {
        var root = Path.Combine(Path.GetTempPath(), "widget-register-original-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root); var path = Path.Combine(root, "home.json");
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30)); var ct = deadline.Token;
        try
        {
            var home = new FileHomeCoreStateStore(path); var actors = new HomeLocalProfileIdentity(home, new OperatingSystemPrincipalSource());
            var original = (await actors.GetCurrentAsync(ct))!; var verifier = new Verifier(); var endpoint = new RuntimeEndpoint();
            var registry = new HomeNativeWidgetRegistry(verifier, actors, new(actors, [new Resolver()]));
            await ReplaceActualProfileAsync(home, ct); var before = await File.ReadAllBytesAsync(path, ct);
            Assert.Null(await registry.RegisterRuntimeForActorAsync(Observed, [Definition()], endpoint, original, ct));
            Assert.Null(await registry.RegisterForActorAsync(Observed, [Definition()], original, ct));
            Assert.Equal(0, verifier.Calls); Assert.Equal(0, endpoint.Calls);
            Assert.Equal(before, await File.ReadAllBytesAsync(path, ct));
            Assert.Empty(await registry.ListAsync(ct));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task Actual_profile_change_during_original_registration_verification_never_publishes_under_replacement()
    {
        var root = Path.Combine(Path.GetTempPath(), "widget-register-during-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root); var path = Path.Combine(root, "home.json");
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30)); var ct = deadline.Token;
        try
        {
            var home = new FileHomeCoreStateStore(path); var actors = new HomeLocalProfileIdentity(home, new OperatingSystemPrincipalSource());
            var original = (await actors.GetCurrentAsync(ct))!; var verifier = new Verifier(); var endpoint = new RuntimeEndpoint();
            var registry = new HomeNativeWidgetRegistry(verifier, actors, new(actors, [new Resolver()]));
            verifier.OnVerify = () => { verifier.OnVerify = null; ReplaceActualProfileAsync(home, ct).GetAwaiter().GetResult(); };
            Assert.Null(await registry.RegisterRuntimeForActorAsync(Observed, [Definition()], endpoint, original, ct));
            Assert.Equal(1, verifier.Calls); Assert.Equal(0, endpoint.Calls);
            Assert.Empty(await registry.ListAsync(ct)); var replacement = (await actors.GetCurrentAsync(ct))!;
            Assert.NotEqual(original, replacement);
            using var admitted = await registry.RegisterRuntimeForActorAsync(Observed, [Definition()], endpoint, replacement, ct);
            Assert.NotNull(admitted); Assert.Single(await registry.ListForActorAsync(replacement, ct));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task Missing_original_peer_verifier_denies_without_legacy_observation_or_registration()
    {
        var root = Path.Combine(Path.GetTempPath(), "widget-missing-original-verifier-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root); var path = Path.Combine(root, "home.json");
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30)); var ct = deadline.Token;
        try
        {
            var home = new FileHomeCoreStateStore(path); var actors = new HomeLocalProfileIdentity(home, new OperatingSystemPrincipalSource());
            var original = (await actors.GetCurrentAsync(ct))!; var before = await File.ReadAllBytesAsync(path, ct);
            var verifier = new LegacyOnlyVerifier(); var endpoint = new RuntimeEndpoint();
            var registry = new HomeNativeWidgetRegistry(verifier, actors, new(actors, [new Resolver()]));
            Assert.Null(await registry.RegisterRuntimeForActorAsync(Observed, [Definition()], endpoint, original, ct));
            Assert.Null(await registry.RegisterAsync(Observed, [Definition()], ct));
            Assert.Empty(await registry.ListForActorAsync(original, ct));
            Assert.Equal(0, verifier.Calls); Assert.Equal(0, endpoint.Calls);
            Assert.Equal(before, await File.ReadAllBytesAsync(path, ct));
        }
        finally { Directory.Delete(root, true); }
    }
    private sealed class LegacyOnlyVerifier : IHomeNativeInstalledPeerVerifier
    {
        public int Calls;
        public ValueTask<HomeNativeInstalledPeer?> VerifyAsync(HomeNativeObservedPeer observed, CancellationToken ct)
        { Calls++; return ValueTask.FromResult<HomeNativeInstalledPeer?>(null); }
    }

    private static async Task ReplaceActualProfileAsync(FileHomeCoreStateStore home, CancellationToken ct)
    {
        var state = await home.ReadAsync(ct); Assert.True(state.IsSuccess);
        var record = Assert.Single(state.State!.Records, record => record.RecordId == "home.local-profile");
        var profile = record.Payload.Deserialize<HomeLocalProfile>()!;
        var written = await home.WriteAsync(record with { Revision = record.Revision + 1,
            Payload = JsonSerializer.SerializeToElement(profile with { ProfileId = Guid.NewGuid() }) }, record.Revision, ct);
        Assert.True(written.IsSuccess);
    }

    private sealed class RuntimeEndpoint : IHomeNativeWidgetRuntimeEndpoint
    {
        public int Calls;
        public bool ForeignSurface, WaitForCancellation, WaitForRelease;
        public TaskCompletionSource<bool> Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async ValueTask<HomeNativeWidgetSurface?> CaptureAsync(HomeNativeWidgetCaptureRequest request, CancellationToken ct)
        {
            Calls++; Entered.TrySetResult(true);
            if (WaitForCancellation) await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            if (WaitForRelease) await Release.Task.WaitAsync(ct);
            return HomeNativeWidgetSurface.Capture(request.Reference,
                ForeignSurface ? "foreign.surface" : request.SurfaceReference,
                "StackPanel { TextBlock Text: @caption }",
                new Dictionary<string, JsonElement> { ["caption"] = JsonSerializer.SerializeToElement("fixture only") });
        }
    }

    private sealed class Actors : IAuthenticatedResourceActorSource
    {
        public AuthenticatedResourceActor Current = new("actor", "profile", null, null, "session");
        public ValueTask<AuthenticatedResourceActor?> GetCurrentAsync(CancellationToken ct) => ValueTask.FromResult<AuthenticatedResourceActor?>(Current);
    }
    private sealed class Verifier : IHomeNativeInstalledPeerOriginalActorVerifier
    {
        public HomeNativeInstalledPeer Owner = new("clock", Guid.NewGuid(), "install-r1", "exact-executable",
            new HashSet<string> { HomeNativeWidgetRegistry.ServiceId });
        public Action? OnVerify; public int Calls;
        public ValueTask<HomeNativeInstalledPeer?> VerifyForActorAsync(HomeNativeObservedPeer observed,
            AuthenticatedResourceActor expectedActor, CancellationToken ct) => VerifyAsync(observed, ct); // Controlled protocol fixture only.
        public ValueTask<HomeNativeInstalledPeer?> VerifyAsync(HomeNativeObservedPeer observed, CancellationToken ct)
        {
            Calls++; OnVerify?.Invoke();
            return ValueTask.FromResult<HomeNativeInstalledPeer?>(observed == Observed ? Owner : null);
        }
    }
    private sealed class Resolver : ICanonicalResourceAccessResolver
    {
        public string ResourceKind => "fixture.clock";
        public bool Allowed = true; public string Revision = "r1"; public Action? OnRead; public int Calls;
        public ValueTask<ResourceAccessDecision> EvaluateAsync(AuthenticatedResourceActor actor, string actionId, ResourceScope scope, CancellationToken ct)
        {
            Calls++; OnRead?.Invoke();
            return ValueTask.FromResult(new ResourceAccessDecision(Allowed && actionId == HomeNativeWidgetRegistry.RenderActionId,
                "fixture", actor.ActorId, Revision, actor.OrganisationId));
        }
    }
}
