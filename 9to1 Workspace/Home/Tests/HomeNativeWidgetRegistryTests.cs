using Haven.Application;
using HavenOS.Home.Core;
using Xunit;

namespace HavenOS.Home.Tests;

public sealed class HomeNativeWidgetRegistryTests
{
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

    private sealed class Actors : IAuthenticatedResourceActorSource
    {
        public AuthenticatedResourceActor Current = new("actor", "profile", null, null, "session");
        public ValueTask<AuthenticatedResourceActor?> GetCurrentAsync(CancellationToken ct) => ValueTask.FromResult<AuthenticatedResourceActor?>(Current);
    }
    private sealed class Verifier : IHomeNativeInstalledPeerVerifier
    {
        public HomeNativeInstalledPeer Owner = new("clock", Guid.NewGuid(), "install-r1", "exact-executable",
            new HashSet<string> { HomeNativeWidgetRegistry.ServiceId });
        public Action? OnVerify;
        public ValueTask<HomeNativeInstalledPeer?> VerifyAsync(HomeNativeObservedPeer observed, CancellationToken ct)
        {
            OnVerify?.Invoke();
            return ValueTask.FromResult<HomeNativeInstalledPeer?>(observed == Observed ? Owner : null);
        }
    }
    private sealed class Resolver : ICanonicalResourceAccessResolver
    {
        public string ResourceKind => "fixture.clock";
        public bool Allowed = true; public string Revision = "r1"; public Action? OnRead;
        public ValueTask<ResourceAccessDecision> EvaluateAsync(AuthenticatedResourceActor actor, string actionId, ResourceScope scope, CancellationToken ct)
        {
            OnRead?.Invoke();
            return ValueTask.FromResult(new ResourceAccessDecision(Allowed && actionId == HomeNativeWidgetRegistry.RenderActionId,
                "fixture", actor.ActorId, Revision, actor.OrganisationId));
        }
    }
}
