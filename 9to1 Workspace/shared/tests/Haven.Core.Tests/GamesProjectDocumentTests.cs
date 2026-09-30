using System.Text;
using Haven.Core.Games;

namespace Haven.Core.Tests;

public sealed class GamesProjectDocumentTests
{
    [Fact]
    public void Development_and_creation_edit_the_same_scene_and_mesh_IDs_through_codec_restart()
    {
        var project = Create();
        var scene = project.Scenes[0];
        var node = scene.Nodes[0];
        var creation = GamesProjectEdits.ChangeWorkspace(project, 1, GamesWorkspaceMode.CreationRendering);
        var moved = GamesSceneEdits.SetPosition(creation.Scenes[0], 1, node.NodeID, new(1, 2, 3));
        var edited = GamesProjectEdits.SetScene(creation, 2, 1, moved);
        var restarted = GamesProjectCodec.Decode(GamesProjectCodec.Encode(edited));
        var development = GamesProjectEdits.ChangeWorkspace(restarted, 3, GamesWorkspaceMode.Development);
        Assert.Equal(project.ProjectID, development.ProjectID);
        Assert.Equal(scene.SceneID, development.ActiveSceneID);
        Assert.Equal(scene.SceneID, development.Scenes[0].SceneID);
        Assert.Equal(node.NodeID, development.Scenes[0].Nodes[0].NodeID);
        Assert.Equal(node.Spatial.ComponentID, development.Scenes[0].Nodes[0].Spatial.ComponentID);
        Assert.Equal(node.MeshResourceID, development.Scenes[0].Nodes[0].MeshResourceID);
        Assert.Equal(new GamesVector3(1, 2, 3), development.Scenes[0].Nodes[0].Spatial.Position);
        Assert.Equal(4, development.Revision);
        Assert.Equal(2, development.Scenes[0].Revision);
        Assert.Equal(new GamesVector3(0, 0, 0), project.Scenes[0].Nodes[0].Spatial.Position);
    }

    [Fact]
    public void Future_schema_unknown_fields_cross_project_and_stale_scene_edits_fail_closed()
    {
        var project = Create();
        var bytes = GamesProjectCodec.Encode(project);
        var text = Encoding.UTF8.GetString(bytes);
        Assert.Throws<InvalidDataException>(() => GamesProjectCodec.Decode(Encoding.UTF8.GetBytes(text.Replace("\"SchemaVersion\":1", "\"SchemaVersion\":2", StringComparison.Ordinal))));
        Assert.Throws<InvalidDataException>(() => GamesProjectCodec.Decode(Encoding.UTF8.GetBytes(text[..^1] + ",\"UnsupportedLayout\":true}")));
        Assert.Throws<InvalidDataException>(() => GamesProjectCodec.Encode(project with { Scenes = [project.Scenes[0] with { ProjectID = Guid.NewGuid() }] }));
        var edited = GamesSceneEdits.SetPosition(project.Scenes[0], 1, project.Scenes[0].Nodes[0].NodeID, new(1, 2, 3));
        Assert.Throws<InvalidOperationException>(() => GamesProjectEdits.SetScene(project, 2, 1, edited));
        Assert.Throws<InvalidOperationException>(() => GamesProjectEdits.SetScene(project, 1, 2, edited));
        Assert.Equal(text, Encoding.UTF8.GetString(bytes));
    }

    private static GamesProjectDocument Create()
    {
        var projectID = Guid.NewGuid();
        var sceneID = Guid.NewGuid();
        var resourceID = Guid.NewGuid();
        return new(1, projectID, 1, GamesWorkspaceMode.Development, sceneID,
            [new(projectID, sceneID, 1, [new(Guid.NewGuid(), Guid.NewGuid(), null, "Model",
                new(Guid.NewGuid(), new(0, 0, 0)), resourceID)], [new(resourceID, new(1, 1, 1))])]);
    }
}
