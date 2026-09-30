using Haven.Application;
using Haven.Application.Games;
using Haven.Core.Games;
using HavenOS.Games;
using Xunit;

namespace HavenOS.Games.Tests;

public sealed class GamesCuiWorkspaceTests
{
    [Fact]
    public async Task Real_Cui_document_and_actions_edit_same_canonical_project_and_keep_previous_state_on_conflict()
    {
        Assert.NotNull(GamesCuiWorkspace.LoadDocument());
        var store = new Store();
        var surface = new GamesCuiWorkspace(new(store), new(new(new Actor(), []), store, new Runtime()), () => store.Saved.FileID, _ => true);
        await surface.DispatchAsync("9to1.Games.Open", null, CancellationToken.None);
        Assert.True(surface.TrySetValue("PositionX", "2.5"));
        Assert.True(surface.TrySetValue("PositionY", "3"));
        Assert.True(surface.TrySetValue("PositionZ", "4"));
        await surface.DispatchAsync("9to1.Games.SetPosition", null, CancellationToken.None);
        Assert.Equal(new GamesVector3(2.5f, 3, 4), store.Saved.Project.Scenes[0].Nodes[0].Spatial.Position);
        var projectID = store.Saved.Project.ProjectID;
        var nodeID = store.Saved.Project.Scenes[0].Nodes[0].NodeID;
        await surface.DispatchAsync("9to1.Games.Development", null, CancellationToken.None);
        Assert.Equal(GamesWorkspaceMode.Development, store.Saved.Project.Workspace);
        Assert.Equal(projectID, store.Saved.Project.ProjectID);
        Assert.Equal(nodeID, store.Saved.Project.Scenes[0].Nodes[0].NodeID);
        var beforeConflict = store.Saved;
        store.RejectSave = true;
        Assert.True(surface.TrySetValue("PositionX", "99"));
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await surface.DispatchAsync("9to1.Games.SetPosition", null, CancellationToken.None));
        Assert.Same(beforeConflict, store.Saved);
        Assert.True(surface.TryGetValue("Status", out var status));
        Assert.Equal("GamesProjectRevisionConflict", status);
        Assert.False(surface.TrySetValue("Project", Guid.NewGuid().ToString()));
    }

    [Fact]
    public async Task Availability_is_rechecked_at_dispatch_and_raw_arguments_cannot_retarget_actions()
    {
        var store = new Store();
        var allowed = true;
        var surface = new GamesCuiWorkspace(new(store), new(new(new Actor(), []), store, new Runtime()), () => store.Saved.FileID, _ => allowed);
        await surface.DispatchAsync("9to1.Games.Open", null, CancellationToken.None);
        allowed = false;
        Assert.False(surface.IsActionAvailable("9to1.Games.SetPosition"));
        Assert.True(surface.TryGetValue("PositionX", out var beforeDeniedEdit));
        Assert.False(surface.TrySetValue("PositionX", "999"));
        Assert.True(surface.TryGetValue("PositionX", out var afterDeniedEdit));
        Assert.Equal(beforeDeniedEdit, afterDeniedEdit);
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await surface.DispatchAsync("9to1.Games.SetPosition", null, CancellationToken.None));
        allowed = true;
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await surface.DispatchAsync("9to1.Games.Open", new { FileID = Guid.NewGuid() }, CancellationToken.None));
        Assert.Equal(1, store.Saved.Project.Revision);
    }
    private sealed class Actor : IAuthenticatedResourceActorSource
    {
        public ValueTask<AuthenticatedResourceActor?> GetCurrentAsync(CancellationToken cancellationToken) => ValueTask.FromResult<AuthenticatedResourceActor?>(null);
    }
    private sealed class Runtime : IGamesSceneRuntime
    {
        public Task<GamesNativeSceneObservation> ObserveAsync(GamesSceneSnapshot scene, CancellationToken cancellationToken = default) => throw new InvalidOperationException("No native invocation is authorised in this fixture.");
    }
    private sealed class Store : ICanonicalGamesProjectStore, ICanonicalGamesSceneSource
    {
        public GamesStoredProject Saved { get; private set; }
        public bool RejectSave { get; set; }
        public Store()
        {
            var projectID = Guid.NewGuid(); var sceneID = Guid.NewGuid();
            var scene = new GamesSceneSnapshot(projectID, sceneID, 1,
                [new(Guid.NewGuid(), Guid.NewGuid(), null, "Node", new(Guid.NewGuid(), new(0, 0, 0)))], []);
            Saved = new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), new(1, projectID, 1, GamesWorkspaceMode.CreationRendering, sceneID, [scene]));
        }
        public Task<GamesStoredProject> OpenAsync(Guid fileID, CancellationToken cancellationToken = default)
        { Assert.Equal(Saved.FileID, fileID); return Task.FromResult(Saved with { Project = Saved.Project.Capture() }); }
        public Task<GamesStoredProject> SaveAsync(Guid fileID, Guid expectedStructuralRevisionID, long expectedProjectRevision, GamesProjectDocument project, CancellationToken cancellationToken = default)
        {
            if (RejectSave || expectedStructuralRevisionID != Saved.StructuralRevisionID || expectedProjectRevision != Saved.Project.Revision)
                throw new InvalidOperationException("GamesProjectRevisionConflict");
            Assert.Equal(Saved.FileID, fileID);
            Saved = new(fileID, Guid.NewGuid(), Guid.NewGuid(), project.Capture()); return Task.FromResult(Saved);
        }
        public Task<GamesSceneSnapshot?> GetAsync(Guid projectID, Guid sceneID, CancellationToken cancellationToken) => Task.FromResult<GamesSceneSnapshot?>(Saved.Project.Scenes.Single());
    }
}
