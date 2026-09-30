using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Godot;
using Haven.Core.Games;

/// <summary>Fixed first-party managed driver. It loads canonical data only; project/user C# code is not loaded.
/// The owning host must establish current Home/Files authority before invoking this process.</summary>
public partial class CanonicalSceneDriver : Node3D
{
    public override void _Ready()
    {
        try
        {
            var arguments = OS.GetCmdlineUserArgs();
            if (arguments.Length != 2 || !Guid.TryParse(arguments[1], out var sceneID))
                throw new InvalidDataException("A canonical project data file and scene identity are required.");
            var info = new FileInfo(arguments[0]);
            if (!info.Exists || info.Length is < 2 or > GamesProjectCodec.MaximumDocumentBytes)
                throw new InvalidDataException("The project data exceeds configured limits.");
            using var input = new FileStream(info.FullName, FileMode.Open, System.IO.FileAccess.Read, FileShare.Read);
            if (input.Length is < 2 or > GamesProjectCodec.MaximumDocumentBytes) throw new InvalidDataException("Project length changed before read.");
            var bytes = new byte[checked((int)input.Length)];
            input.ReadExactly(bytes);
            if (input.ReadByte() != -1) throw new InvalidDataException("Project grew during read.");
            var project = GamesProjectCodec.Decode(bytes);
            var scene = project.Scenes.Single(item => item.SceneID == sceneID);
            var meshes = new Dictionary<Guid, Mesh>();
            foreach (var mesh in scene.Meshes)
            {
                Mesh native;
                if (mesh.Geometry is { } geometry)
                {
                    var arrays = new Godot.Collections.Array();
                    arrays.Resize((int)Mesh.ArrayType.Max);
                    arrays[(int)Mesh.ArrayType.Vertex] = geometry.Vertices.Select(Vector).ToArray();
                    arrays[(int)Mesh.ArrayType.Index] = geometry.TriangleIndices.ToArray();
                    var arrayMesh = new ArrayMesh();
                    arrayMesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
                    native = arrayMesh;
                }
                else native = new BoxMesh { Size = Vector(mesh.Size) };
                native.SetMeta("_9to1_resource_id", mesh.ResourceID.ToString("D"));
                meshes.Add(mesh.ResourceID, native);
            }
            var nodes = new Dictionary<Guid, Node3D>();
            var remaining = scene.Nodes.ToList();
            while (remaining.Count > 0)
            {
                var progressed = false;
                foreach (var node in remaining.ToArray())
                {
                    if (node.ParentNodeID is { } parentID && !nodes.ContainsKey(parentID)) continue;
                    Node3D native = node.MeshResourceID is { } meshID ? new MeshInstance3D { Mesh = meshes[meshID] } : new Node3D();
                    native.Name = "n" + node.NodeID.ToString("N");
                    native.Position = Vector(node.Spatial.Position);
                    native.SetMeta("_9to1_node_id", node.NodeID.ToString("D"));
                    native.SetMeta("_9to1_entity_id", node.EntityID.ToString("D"));
                    native.SetMeta("_9to1_component_id", node.Spatial.ComponentID.ToString("D"));
                    (node.ParentNodeID is { } parent ? nodes[parent] : this).AddChild(native);
                    nodes.Add(node.NodeID, native); remaining.Remove(node); progressed = true;
                }
                if (!progressed) throw new InvalidDataException("Canonical scene hierarchy cannot be constructed.");
            }
            var observations = scene.Nodes.Select(node =>
            {
                var native = nodes[node.NodeID];
                var world = native.GlobalPosition;
                return new GamesNativeNodeObservation(node.NodeID, node.EntityID, node.Spatial.ComponentID,
                    node.Spatial.Position, new(world.X, world.Y, world.Z), node.MeshResourceID,
                    native is MeshInstance3D mesh ? mesh.Mesh.GetFaces().Length : 0);
            }).ToArray();
            GD.Print("ASTRA_GAMES_MANAGED_SCENE=" + JsonSerializer.Serialize(new
            {
                projectID = project.ProjectID, projectRevision = project.Revision, sceneID = scene.SceneID,
                revision = scene.Revision, observedEngineVersion = Engine.GetVersionInfo()["string"].AsString(),
                managedRuntimeVersion = System.Environment.Version.ToString(), nodes = observations
            }));
            GetTree().Quit(0);
        }
        catch (Exception exception)
        {
            GD.PushError("Canonical managed scene failed: " + exception.Message);
            GetTree().Quit(2);
        }
    }
    private static Vector3 Vector(GamesVector3 value) => new(value.X, value.Y, value.Z);
}
