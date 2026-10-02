using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Godot;
using Haven.Core.Games;
namespace HavenOS.Games.Runtime;
/// <summary>Actual Godot triangle surface preparation. No source mutation, script load or resource grant.</summary>
public static class CanonicalTriangleMesher
{
    public static ArrayMesh Create(GamesMeshResource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        try{return CreateCore(source);}
        catch(Exception error) when(error is InvalidDataException or NotSupportedException or ArgumentException)
        {throw new CanonicalMeshPreparationException(source.ResourceID,"GamesMeshNormalsUnavailable",error);}
    }
    private static ArrayMesh CreateCore(GamesMeshResource source)
    {
        var geometry=source.Geometry??throw new NotSupportedException("This path requires canonical triangle geometry.");
        var vertices=new List<GamesVector3>();foreach(var value in geometry.Vertices??throw new InvalidDataException("Missing vertices."))
        {if(vertices.Count==65536)throw new InvalidDataException("Canonical vertex budget exceeded.");vertices.Add(value);}
        var indices=new List<int>();foreach(var value in geometry.TriangleIndices??throw new InvalidDataException("Missing indices."))
        {if(indices.Count==393216)throw new InvalidDataException("Canonical index budget exceeded.");indices.Add(value);}
        var captured=source with{Geometry=new(vertices.ToArray(),indices.ToArray())};captured.Validate();
        var nativeVertices=vertices.Select(value=>new Vector3(value.X,value.Y,value.Z)).ToArray();var nativeIndices=indices.ToArray();
        for(var i=0;i<nativeIndices.Length;i+=3)
        {
            var a=nativeVertices[nativeIndices[i]];var b=nativeVertices[nativeIndices[i+1]];var c=nativeVertices[nativeIndices[i+2]];
            var cross=(b-a).Cross(c-a);
            if(!cross.IsFinite()||cross.LengthSquared()==0)throw new NotSupportedException("A degenerate/nonfinite triangle has no supported surface normal.");
        }
        var arrays=new Godot.Collections.Array();arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex]=nativeVertices;arrays[(int)Mesh.ArrayType.Index]=nativeIndices;
        using var tool=new SurfaceTool();tool.CreateFromArrays(arrays,Mesh.PrimitiveType.Triangles);tool.GenerateNormals(false);
        var mesh=tool.Commit();if(mesh is null)throw new InvalidDataException("Godot did not create the canonical triangle mesh.");
        try
        {
            if(mesh.GetSurfaceCount()!=1)throw new InvalidDataException("Godot changed the canonical surface set.");
            var faces=mesh.GetFaces();
            if(faces.Length!=nativeIndices.Length||!faces.SequenceEqual(nativeIndices.Select(index=>nativeVertices[index])))
                throw new InvalidDataException("Godot changed canonical triangle order or geometry during normal generation.");
            var result=mesh.SurfaceGetArrays(0);var actualVertices=result[(int)Mesh.ArrayType.Vertex].AsVector3Array();
            var normals=result[(int)Mesh.ArrayType.Normal].AsVector3Array();
            if(normals.Length!=actualVertices.Length||normals.Length==0||normals.Any(normal=>!normal.IsFinite()||!normal.IsNormalized()))
                throw new InvalidDataException("Godot produced missing/nonfinite/nonunit normals.");
            mesh.SetMeta("_9to1_resource_id",source.ResourceID.ToString("D"));return mesh;
        }
        catch{mesh.Dispose();throw;}
    }
}

/// <summary>Typed app-owned preparation refusal, carrying canonical ResourceID; no owning writes occurred.</summary>
public sealed class CanonicalMeshPreparationException:Exception
{
    public CanonicalMeshPreparationException(Guid resourceID,string code,Exception cause):base($"Resource {resourceID:D}: {cause.Message}",cause)
    {ResourceID=resourceID;Code=code;}
    public Guid ResourceID{get;} public string Code{get;}
}
