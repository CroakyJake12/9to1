using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using Godot;
using Haven.Core.Games;
using HavenOS.Games.Runtime;
/// <summary>Must execute under an actual Godot managed engine, not a copied toolkit.</summary>
public partial class CanonicalMeshNormalsWitness:Node
{
    public override void _Ready()
    {
        try
        {
            var resourceID=Guid.NewGuid();var source=new GamesMeshResource(resourceID,new(1,1,1),new(new GamesVector3[]{new(0,0,0),new(1,0,0),new(0,1,0)},new[]{0,1,2}));
            var sourceBytes=JsonSerializer.SerializeToUtf8Bytes(source);
            using var first=CanonicalTriangleMesher.Create(source);var arrays=first.SurfaceGetArrays(0);
            var normals=arrays[(int)Mesh.ArrayType.Normal].AsVector3Array();
            var expected=new Plane(new Vector3(0,0,0),new Vector3(1,0,0),new Vector3(0,1,0)).Normal;
            if(normals.Length!=3||normals.Any(normal=>!normal.IsEqualApprox(expected))||first.GetMeta("_9to1_resource_id").AsString()!=resourceID.ToString("D"))throw new InvalidDataException("Actual donor normals or stable resource identity disagree.");
            var reversed=source with{Geometry=new(source.Geometry!.Vertices,new[]{0,2,1})};using var second=CanonicalTriangleMesher.Create(reversed);
            var reverseNormals=second.SurfaceGetArrays(0)[(int)Mesh.ArrayType.Normal].AsVector3Array();
            if(reverseNormals.Length!=3||reverseNormals.Any(normal=>!normal.IsEqualApprox(-expected)))throw new InvalidDataException("Reversing canonical winding did not reverse native normals: "+JsonSerializer.Serialize(new{expected=new{x=expected.X,y=expected.Y,z=expected.Z},original=normals.Select(v=>new{x=v.X,y=v.Y,z=v.Z}),reversed=reverseNormals.Select(v=>new{x=v.X,y=v.Y,z=v.Z}),vertices=second.SurfaceGetArrays(0)[(int)Mesh.ArrayType.Vertex].AsVector3Array().Select(v=>new{x=v.X,y=v.Y,z=v.Z}),indices=second.SurfaceGetArrays(0)[(int)Mesh.ArrayType.Index].AsInt32Array(),faces=second.GetFaces().Select(v=>new{x=v.X,y=v.Y,z=v.Z}),actualEngine=Engine.GetVersionInfo()["string"].AsString()}));
            var degenerate=source with{Geometry=new(source.Geometry!.Vertices,new[]{0,0,1})};
            var refused=false;try{using var invalid=CanonicalTriangleMesher.Create(degenerate);}catch(CanonicalMeshPreparationException error){refused=error.ResourceID==resourceID&&error.Code=="GamesMeshNormalsUnavailable";}
            if(!refused||!sourceBytes.SequenceEqual(JsonSerializer.SerializeToUtf8Bytes(source)))throw new InvalidDataException("Normal refusal changed canonical data.");
            GD.Print("ASTRA_GAMES_CANONICAL_NORMALS="+JsonSerializer.Serialize(new{success=true,resourceID,actualEngine=Engine.GetVersionInfo()["string"].AsString(),normals=normals.Select(v=>new{x=v.X,y=v.Y,z=v.Z}),cases=new[]{"actual-normal-and-identity","winding-reversal","degenerate-refusal-source-unchanged"}}));
            GetTree().Quit(0);
        }
        catch(Exception error){GD.PushError("Canonical normal witness failed: "+error);GetTree().Quit(2);}
    }
}
