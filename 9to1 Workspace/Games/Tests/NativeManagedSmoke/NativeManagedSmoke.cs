using System;
using System.Linq;
using System.Text.Json;
using Godot;
using Haven.Core.Games;

// An actual public C# game component. This project contains no GDScript dependency.
public partial class NativeManagedSmoke : Node3D
{
    [Export]
    public float Speed { get; set; } = 3;

    public override void _Ready()
    {
        var projectID = Guid.Parse("e8f16d83-b914-4912-a24e-50c30147c735");
        var sceneID = Guid.Parse("2b2a3c75-2931-4f3e-856c-e4c5334a8296");
        var parentID = Guid.Parse("f369e64c-2cbf-4293-8c67-54e4cc199db6");
        var childID = Guid.Parse("84d87ea0-b85b-4ca4-a1bd-6dd205d321f4");
        var entityID = Guid.Parse("48e0a416-6ff8-4a93-83ed-432727230d72");
        var componentID = Guid.Parse("fc86a273-a49a-4a6d-a89b-4d697c83a6f8");
        var resourceID = Guid.Parse("853fc9b6-1547-4f68-9e51-7fc3e031b5d6");
        var source = new GamesSceneSnapshot(projectID, sceneID, 1,
            [new(parentID, entityID, null, "Parent", new(Guid.Parse("bcce9a03-8b1b-4d0c-bc83-98669332a2ce"), new(1, 2, 3))),
                new(childID, entityID, parentID, "Inspectable C# component", new(componentID, new(4, 5, 6)), resourceID)],
            [new(resourceID, new(2, 3, 4))]);
        var edited = GamesSceneEdits.SetPosition(source, 1, childID, new(7, 8, 9));
        var parent = new Node3D { Name = "Parent", Position = new(1, 2, 3) };
        AddChild(parent);
        var child = new MeshInstance3D { Name = "InspectableChild", Position = new(4, 5, 6), Mesh = new BoxMesh { Size = new(2, 3, 4) } };
        child.SetMeta("_9to1_node_id", childID.ToString("D"));
        child.SetMeta("_9to1_component_id", componentID.ToString("D"));
        child.Mesh.SetMeta("_9to1_resource_id", resourceID.ToString("D"));
        parent.AddChild(child);
        var position = edited.Nodes.Single(node => node.NodeID == childID).Spatial.Position;
        child.Position = new(position.X, position.Y, position.Z);
        var hasExportedProperty = GetPropertyList().Any(property => property["name"].AsString() == nameof(Speed));
        var nativePosition = child.GlobalPosition;
        var vertices = child.Mesh.GetFaces().Length;
        var success = nativePosition == new Vector3(8, 10, 12) && vertices == 36 && Speed == 3.5f && hasExportedProperty
            && child.GetMeta("_9to1_node_id").AsString() == childID.ToString("D")
            && child.Mesh.GetMeta("_9to1_resource_id").AsString() == resourceID.ToString("D");
        GD.Print("ASTRA_GAMES_MANAGED_SMOKE=" + JsonSerializer.Serialize(new
        {
            success, projectID, sceneID, nodeID = childID, componentID, resourceID, revision = edited.Revision,
            observedEngineVersion = Engine.GetVersionInfo()["string"].AsString(),
            managedRuntimeVersion = System.Environment.Version.ToString(), speed = Speed, hasExportedProperty,
            worldPosition = new { x = nativePosition.X, y = nativePosition.Y, z = nativePosition.Z }, meshVertexCount = vertices
        }));
        GetTree().Quit(success ? 0 : 2);
    }
}
