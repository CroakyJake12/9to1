using System.Security.Cryptography;
using Haven.Core.Games;
using Haven.Infrastructure.Games;

namespace Haven.Infrastructure.Tests;

public sealed class GodotManagedProjectRuntimeTests
{
    [GamesManagedRuntimeFact]
    public async Task Actual_pinned_managed_driver_observes_canonical_project_revision_and_native_geometry()
    {
        var executable = Environment.GetEnvironmentVariable("ASTRA_GODOT_RUNTIME")!;
        var module = Environment.GetEnvironmentVariable("ASTRA_GAMES_MANAGED_MODULE")!;
        var executableHash = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(executable)));
        var moduleHash = await GodotManagedProjectRuntime.FingerprintModuleAsync(module);
        var runtime = new GodotManagedProjectRuntime(executable, module, executableHash, moduleHash);
        var projectID = Guid.NewGuid(); var sceneID = Guid.NewGuid(); var root = Guid.NewGuid(); var child = Guid.NewGuid(); var resource = Guid.NewGuid();
        var scene = new GamesSceneSnapshot(projectID, sceneID, 7,
            [GodotSceneRuntimeTests.Node(root, null, "Root", new(1, 2, 3)), GodotSceneRuntimeTests.Node(child, root, "Triangle", new(4, 5, 6), resource)],
            [new(resource, new(1, 1, 1), new([new(0, 0, 0), new(1, 0, 0), new(0, 1, 0)], [0, 1, 2]))]);
        var project = new GamesProjectDocument(1, projectID, 11, GamesWorkspaceMode.CreationRendering, sceneID, [scene]);
        var before = GamesProjectCodec.Encode(project);
        var observed = await runtime.ObserveAsync(project, sceneID);
        Assert.Equal(projectID, observed.ProjectID);
        Assert.Equal(11, observed.ProjectRevision);
        Assert.Equal(7, observed.SceneRevision);
        Assert.StartsWith("4.7.2", observed.ObservedEngineVersion);
        Assert.Equal(moduleHash, observed.ModuleSha256);
        var node = Assert.Single(observed.Nodes, node => node.NodeID == child);
        Assert.Equal(new GamesVector3(5, 7, 9), node.WorldPosition);
        Assert.Equal(3, node.MeshVertexCount);
        Assert.Equal(scene.Nodes[1].EntityID, node.EntityID);
        Assert.Equal(scene.Nodes[1].Spatial.ComponentID, node.ComponentID);
        Assert.Equal(resource, node.MeshResourceID);
        Assert.Equal(before, GamesProjectCodec.Encode(project));
        await Assert.ThrowsAsync<InvalidDataException>(() => new GodotManagedProjectRuntime(executable, module, executableHash, new string('0', 64)).ObserveAsync(project, sceneID));
    }
}

public sealed class GamesManagedRuntimeFactAttribute : FactAttribute
{
    public GamesManagedRuntimeFactAttribute()
    {
        if (!File.Exists(Environment.GetEnvironmentVariable("ASTRA_GODOT_RUNTIME")) || !Directory.Exists(Environment.GetEnvironmentVariable("ASTRA_GAMES_MANAGED_MODULE")))
            Skip = "Requires the actual controlled Godot Mono runtime and built/imported first-party managed Games module.";
    }
}
