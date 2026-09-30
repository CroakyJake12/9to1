using Haven.Core.Games;
using Haven.Infrastructure.Games;

namespace Haven.Infrastructure.Tests;

public sealed class BlenderGodotMeshTests
{
    [GamesModellingRuntimeFact]
    public async Task Actual_Blender_modelling_and_Godot_use_the_same_canonical_resource_without_user_export_import()
    {
        var resource = new GamesMeshResource(Guid.NewGuid(), new(2, 3, 4));
        var modeller = new BlenderMeshModeller(Environment.GetEnvironmentVariable("ASTRA_BLENDER_RUNTIME")!);
        var modelled = await modeller.SubdivideAsync(resource, 1);
        Assert.Equal(resource.ResourceID, modelled.Resource.ResourceID);
        Assert.NotEmpty(modelled.ObservedRuntimeVersion);
        Assert.Equal(64, modelled.ExecutableSha256.Length);
        var geometry = modelled.Resource.Geometry!;
        Assert.Equal(26, geometry.Vertices.Count);
        Assert.Equal(144, geometry.TriangleIndices.Count);
        Assert.InRange(geometry.Vertices.Min(vertex => vertex.X), -1 - 0.000001f, -1 + 0.000001f);
        Assert.InRange(geometry.Vertices.Max(vertex => vertex.X), 1 - 0.000001f, 1 + 0.000001f);
        Assert.InRange(geometry.Vertices.Min(vertex => vertex.Y), -1.5f - 0.000001f, -1.5f + 0.000001f);
        Assert.InRange(geometry.Vertices.Max(vertex => vertex.Y), 1.5f - 0.000001f, 1.5f + 0.000001f);
        Assert.InRange(geometry.Vertices.Min(vertex => vertex.Z), -2 - 0.000001f, -2 + 0.000001f);
        Assert.InRange(geometry.Vertices.Max(vertex => vertex.Z), 2 - 0.000001f, 2 + 0.000001f);
        Assert.Null(resource.Geometry);
        var root = GodotSceneRuntimeTests.Node(Guid.NewGuid(), null, "Root", new(1, 2, 3));
        var mesh = GodotSceneRuntimeTests.Node(Guid.NewGuid(), root.NodeID, "Model", new(4, 5, 6), resource.ResourceID)
            with { EntityID = root.EntityID }; // Inspectable native composition can share one entity.
        var scene = new GamesSceneSnapshot(Guid.NewGuid(), Guid.NewGuid(), 1, [root, mesh], [resource]);
        var edited = GamesSceneEdits.SetMesh(scene, 1, modelled.Resource);
        Assert.Equal(2, edited.Revision);
        Assert.Equal(resource.ResourceID, edited.Meshes[0].ResourceID);
        Assert.Null(scene.Meshes[0].Geometry);
        var observed = await new GodotSceneRuntime(Environment.GetEnvironmentVariable("ASTRA_GODOT_RUNTIME")!).ObserveAsync(edited);
        var native = Assert.Single(observed.Nodes, node => node.NodeID == mesh.NodeID);
        Assert.Equal(resource.ResourceID, native.MeshResourceID);
        Assert.Equal(geometry.TriangleIndices.Count, native.MeshVertexCount);
        Assert.Equal(root.EntityID, native.EntityID);
        Assert.Equal(new GamesVector3(5, 7, 9), native.WorldPosition);
        // Already modelled canonical geometry can be edited again, rather than going through a second asset identity.
        var roundTrip = await modeller.SubdivideAsync(modelled.Resource, 0);
        Assert.Equal(resource.ResourceID, roundTrip.Resource.ResourceID);
        Assert.Equal(geometry.Vertices, roundTrip.Resource.Geometry!.Vertices);
        Assert.Equal(geometry.TriangleIndices.Count, roundTrip.Resource.Geometry.TriangleIndices.Count);
    }

    [Fact]
    public async Task Invalid_geometry_and_excessive_subdivision_reject_before_launching_native_tools()
    {
        var backend = new BlenderMeshModeller(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")));
        var invalid = new GamesMeshResource(Guid.NewGuid(), new(1, 1, 1),
            new([new(0, 0, 0), new(1, 0, 0), new(0, 1, 0)], [0, 1, 9]));
        await Assert.ThrowsAsync<InvalidDataException>(() => backend.SubdivideAsync(invalid, 0));
        var geometry = invalid with { Geometry = new(invalid.Geometry!.Vertices, Enumerable.Repeat(0, 3072).ToArray()) };
        await Assert.ThrowsAsync<InvalidDataException>(() => backend.SubdivideAsync(geometry, 3));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => backend.SubdivideAsync(new(Guid.NewGuid(), new(1, 1, 1)), 4));
    }
}

public sealed class GamesModellingRuntimeFactAttribute : FactAttribute
{
    public GamesModellingRuntimeFactAttribute()
    {
        if (!File.Exists(Environment.GetEnvironmentVariable("ASTRA_GODOT_RUNTIME"))
            || !File.Exists(Environment.GetEnvironmentVariable("ASTRA_BLENDER_RUNTIME")))
            Skip = "Requires explicitly configured actual Godot and Blender runtimes; no integrated modelling claim otherwise.";
    }
}
