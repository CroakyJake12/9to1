using Haven.Core.Games;
using Haven.Infrastructure.Games;

namespace Haven.Infrastructure.Tests;

public sealed class GodotSceneRuntimeTests
{
    [GodotRuntimeFact]
    public async Task Actual_native_scene_retains_IDs_hierarchy_mesh_resource_and_CSharp_position_edits()
    {
        var rootID = Guid.NewGuid();
        var childID = Guid.NewGuid();
        var meshID = Guid.NewGuid();
        var projectID = Guid.NewGuid();
        var scene = new GamesSceneSnapshot(projectID, Guid.NewGuid(), 1,
            [Node(rootID, null, "Renamable root", new(1, 2, 3)), Node(childID, rootID, "Quoted \" mesh\n[node injected]", new(4, 5, 6), meshID)],
            [new(meshID, new(2, 3, 4))]);
        var runtime = new GodotSceneRuntime(Environment.GetEnvironmentVariable("ASTRA_GODOT_RUNTIME")!);
        var observed = await runtime.ObserveAsync(scene);
        Assert.Equal(projectID, observed.ProjectID);
        Assert.Equal(scene.SceneID, observed.SceneID);
        Assert.Equal(1, observed.Revision);
        Assert.NotEmpty(observed.ObservedEngineVersion);
        Assert.Equal(64, observed.ExecutableSha256.Length);
        var child = Assert.Single(observed.Nodes, item => item.NodeID == childID);
        Assert.Equal(new GamesVector3(4, 5, 6), child.Position);
        Assert.Equal(new GamesVector3(5, 7, 9), child.WorldPosition);
        Assert.Equal(meshID, child.MeshResourceID);
        Assert.Equal(36, child.MeshVertexCount); // Actual native BoxMesh triangle faces, not a prefab label.
        var edited = GamesSceneEdits.SetPosition(scene, 1, childID, new(7, 8, 9));
        var moved = await runtime.ObserveAsync(edited);
        Assert.Equal(2, moved.Revision);
        var changed = Assert.Single(moved.Nodes, item => item.NodeID == childID);
        Assert.Equal(child.EntityID, changed.EntityID);
        Assert.Equal(child.ComponentID, changed.ComponentID);
        Assert.Equal(meshID, changed.MeshResourceID);
        Assert.Equal(new GamesVector3(8, 10, 12), changed.WorldPosition);
        Assert.Equal(new GamesVector3(4, 5, 6), scene.Nodes[1].Spatial.Position);
        Assert.Throws<InvalidOperationException>(() => GamesSceneEdits.SetPosition(edited, 1, childID, new(0, 0, 0)));
    }

    [Fact]
    public async Task Missing_native_backend_and_cyclic_or_missing_resource_scenes_fail_closed()
    {
        var id = Guid.NewGuid();
        var runtime = new GodotSceneRuntime(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")));
        var scene = new GamesSceneSnapshot(Guid.NewGuid(), Guid.NewGuid(), 1, [Node(id, null, "root", new(0, 0, 0))], []);
        await Assert.ThrowsAsync<FileNotFoundException>(() => runtime.ObserveAsync(scene));
        var cycle = scene with { Nodes = [Node(id, id, "cycle", new(0, 0, 0))] };
        await Assert.ThrowsAsync<InvalidDataException>(() => runtime.ObserveAsync(cycle));
        var missing = scene with { Nodes = [Node(id, null, "missing", new(0, 0, 0), Guid.NewGuid())] };
        await Assert.ThrowsAsync<InvalidDataException>(() => runtime.ObserveAsync(missing));
    }
    internal static GamesSceneNode Node(Guid id, Guid? parent, string name, GamesVector3 position, Guid? mesh = null) =>
        new(id, Guid.NewGuid(), parent, name, new(Guid.NewGuid(), position), mesh);
}

public sealed class GodotRuntimeFactAttribute : FactAttribute
{
    public GodotRuntimeFactAttribute()
    {
        if (!File.Exists(Environment.GetEnvironmentVariable("ASTRA_GODOT_RUNTIME")))
            Skip = "Requires an explicitly configured actual Godot runtime; no native semantics claim without it.";
    }
}
