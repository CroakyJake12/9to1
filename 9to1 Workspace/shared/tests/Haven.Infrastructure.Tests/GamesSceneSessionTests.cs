using Haven.Application;
using Haven.Application.Games;
using Haven.Core.Games;

namespace Haven.Infrastructure.Tests;

public sealed class GamesSceneSessionTests
{
    [Fact]
    public async Task Unknown_resource_owner_denies_before_project_read_or_native_invocation()
    {
        var source = new SceneSource();
        var runtime = new Runtime();
        var service = new GamesSceneSessionService(new(new Actors(), []), source, runtime);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.ObserveAsync(source.Scene.ProjectID, source.Scene.SceneID, 1));
        Assert.Equal(0, source.Reads);
        Assert.Equal(0, runtime.Calls);
    }

    [Theory]
    [InlineData(2, 0)]
    [InlineData(3, 1)]
    public async Task Revocation_before_or_during_native_observation_withholds_result(int denyAt, int expectedRuntimeCalls)
    {
        var source = new SceneSource();
        var runtime = new Runtime();
        var owner = new Owner(denyAt);
        var service = new GamesSceneSessionService(new(new Actors(), [owner]), source, runtime);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.ObserveAsync(source.Scene.ProjectID, source.Scene.SceneID, 1));
        Assert.Equal(1, source.Reads);
        Assert.Equal(expectedRuntimeCalls, runtime.Calls);
        Assert.Equal(denyAt, owner.Calls);
    }

    [Fact]
    public async Task Wrong_canonical_project_or_backend_identity_is_not_returned_as_an_authorised_scene()
    {
        var source = new SceneSource();
        var runtime = new Runtime();
        var service = new GamesSceneSessionService(new(new Actors(), [new Owner(100)]), source, runtime);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ObserveAsync(Guid.NewGuid(), source.Scene.SceneID, 1));
        Assert.Equal(0, runtime.Calls);
        runtime.SubstituteIdentity = true;
        await Assert.ThrowsAsync<InvalidDataException>(() => service.ObserveAsync(source.Scene.ProjectID, source.Scene.SceneID, 1));
        Assert.Equal(1, runtime.Calls);
    }

    private sealed class Actors : IAuthenticatedResourceActorSource
    {
        // Explicit isolated authority fixture, not a production Home identity/grant.
        public ValueTask<AuthenticatedResourceActor?> GetCurrentAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult<AuthenticatedResourceActor?>(new("fixture-actor", "fixture-profile", null, null, "fixture-authentication-revision"));
    }
    private sealed class Owner(int denyAt) : ICanonicalResourceAccessResolver
    {
        public string ResourceKind => "games.scene";
        public int Calls { get; private set; }
        public ValueTask<ResourceAccessDecision> EvaluateAsync(AuthenticatedResourceActor actor, string actionId, ResourceScope scope,
            CancellationToken cancellationToken)
        {
            Assert.Equal("games.scene.observe", actionId);
            Assert.Equal(ResourceAccess.Read, scope.Access);
            return ValueTask.FromResult(new ResourceAccessDecision(++Calls < denyAt, "fixture", actor.ActorId, scope.Revision, null));
        }
    }
    private sealed class SceneSource : ICanonicalGamesSceneSource
    {
        public GamesSceneSnapshot Scene { get; } = new(Guid.NewGuid(), Guid.NewGuid(), 1,
            [GodotSceneRuntimeTests.Node(Guid.NewGuid(), null, "Root", new(0, 0, 0))], []);
        public int Reads { get; private set; }
        public Task<GamesSceneSnapshot?> GetAsync(Guid projectID, Guid sceneID, CancellationToken cancellationToken)
        { Reads++; return Task.FromResult<GamesSceneSnapshot?>(Scene); }
    }
    private sealed class Runtime : IGamesSceneRuntime
    {
        public int Calls { get; private set; }
        public bool SubstituteIdentity { get; set; }
        public Task<GamesNativeSceneObservation> ObserveAsync(GamesSceneSnapshot scene, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(new GamesNativeSceneObservation(SubstituteIdentity ? Guid.NewGuid() : scene.ProjectID,
                scene.SceneID, scene.Revision, "fixture-only", new string('0', 64), []));
        }
    }
}
