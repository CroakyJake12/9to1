using System.Text.Json.Serialization;

namespace Haven.Core.Games;

public sealed record GamesVector3(float X, float Y, float Z)
{
    [JsonIgnore]
    public bool IsValid => float.IsFinite(X) && float.IsFinite(Y) && float.IsFinite(Z)
        && Math.Abs(X) <= 1_000_000 && Math.Abs(Y) <= 1_000_000 && Math.Abs(Z) <= 1_000_000;
}
public sealed record GamesSpatialComponent(Guid ComponentID, GamesVector3 Position);
public sealed record GamesMeshGeometry(IReadOnlyList<GamesVector3> Vertices, IReadOnlyList<int> TriangleIndices);
public sealed record GamesMeshResource(Guid ResourceID, GamesVector3 Size, GamesMeshGeometry? Geometry = null)
{
    public GamesMeshResource Capture() => this with { Geometry = Geometry is null ? null : new(
        Geometry.Vertices?.ToArray() ?? throw new InvalidDataException("Mesh vertices are missing."),
        Geometry.TriangleIndices?.ToArray() ?? throw new InvalidDataException("Mesh indices are missing.")) };
    public void Validate()
    {
        if (ResourceID == Guid.Empty || Size is null || !Size.IsValid || Size.X <= 0 || Size.Y <= 0 || Size.Z <= 0)
            throw new InvalidDataException("Games mesh resource is invalid.");
        if (Geometry is { } geometry && (geometry.Vertices is null || geometry.TriangleIndices is null
            || geometry.Vertices.Count is < 3 or > 65536 || geometry.TriangleIndices.Count is < 3 or > 393216
            || geometry.TriangleIndices.Count % 3 != 0 || geometry.Vertices.Any(vertex => vertex is null || !vertex.IsValid)
            || geometry.TriangleIndices.Any(index => index < 0 || index >= geometry.Vertices.Count)))
            throw new InvalidDataException("Games mesh geometry is invalid or exceeds configured limits.");
    }
}
public sealed record GamesSceneNode(Guid NodeID, Guid EntityID, Guid? ParentNodeID, string Name,
    GamesSpatialComponent Spatial, Guid? MeshResourceID = null);
public sealed record GamesSceneSnapshot(Guid ProjectID, Guid SceneID, long Revision,
    IReadOnlyList<GamesSceneNode> Nodes, IReadOnlyList<GamesMeshResource> Meshes)
{
    public GamesSceneSnapshot Capture()
    {
        if (Nodes is null || Meshes is null) throw new InvalidDataException("Games scene structure is missing.");
        var snapshot = this with { Nodes = Nodes.ToArray(), Meshes = Meshes.Select(mesh => mesh?.Capture()
            ?? throw new InvalidDataException("Games scene contains a missing mesh resource.")).ToArray() };
        snapshot.Validate();
        return snapshot;
    }
    public void Validate()
    {
        if (ProjectID == Guid.Empty || SceneID == Guid.Empty || Revision < 1 || Nodes is null || Meshes is null
            || Nodes.Count is < 1 or > 256 || Meshes.Count > 128)
            throw new InvalidDataException("Games scene identity or configured structure limit is invalid.");
        var nodes = new HashSet<Guid>();
        var components = new HashSet<Guid>();
        foreach (var node in Nodes)
            if (node is null || node.NodeID == Guid.Empty || !nodes.Add(node.NodeID)
                || node.EntityID == Guid.Empty
                || string.IsNullOrWhiteSpace(node.Name) || node.Name.Length > 256 || node.Spatial is null
                || node.Spatial.ComponentID == Guid.Empty || !components.Add(node.Spatial.ComponentID)
                || node.Spatial.Position is null || !node.Spatial.Position.IsValid)
                throw new InvalidDataException("Games scene contains invalid or duplicate canonical identities.");
        if (Nodes.Count(node => node.ParentNodeID is null) != 1)
            throw new InvalidDataException("Games scene requires exactly one root.");
        var resources = new HashSet<Guid>();
        foreach (var mesh in Meshes)
        {
            if (mesh is null || !resources.Add(mesh.ResourceID))
                throw new InvalidDataException("Games mesh resource is invalid.");
            mesh.Validate();
        }
        if (Meshes.Sum(mesh => (long)(mesh.Geometry?.Vertices.Count ?? 8)) > 65536
            || Meshes.Sum(mesh => (long)(mesh.Geometry?.TriangleIndices.Count ?? 36)) > 393216)
            throw new InvalidDataException("Games scene geometry exceeds configured limits.");
        var byID = Nodes.ToDictionary(node => node.NodeID);
        foreach (var node in Nodes)
        {
            if (node.MeshResourceID is { } resource && !resources.Contains(resource))
                throw new InvalidDataException("Games node references a missing resource.");
            var visited = new HashSet<Guid> { node.NodeID };
            var parent = node.ParentNodeID;
            while (parent is { } parentID)
            {
                if (!byID.TryGetValue(parentID, out var ancestor) || !visited.Add(parentID) || visited.Count > 64)
                    throw new InvalidDataException("Games scene hierarchy is missing, cyclic or exceeds the depth limit.");
                parent = ancestor.ParentNodeID;
            }
        }
    }
}

/// <summary>Pure scene edits. Persistence/authority remain with the canonical project owner.</summary>
public static class GamesSceneEdits
{
    public static GamesSceneSnapshot SetMesh(GamesSceneSnapshot scene, long expectedRevision, GamesMeshResource mesh)
    {
        var snapshot = scene.Capture();
        if (snapshot.Revision != expectedRevision) throw new InvalidOperationException("GamesSceneRevisionConflict");
        var resource = mesh.Capture();
        resource.Validate();
        if (!snapshot.Meshes.Any(candidate => candidate.ResourceID == resource.ResourceID))
            throw new KeyNotFoundException("GamesMeshNotFound");
        var edited = snapshot with { Revision = checked(snapshot.Revision + 1),
            Meshes = snapshot.Meshes.Select(candidate => candidate.ResourceID == resource.ResourceID ? resource : candidate).ToArray() };
        edited.Validate();
        return edited;
    }
    public static GamesSceneSnapshot SetPosition(GamesSceneSnapshot scene, long expectedRevision, Guid nodeID, GamesVector3 position)
    {
        var snapshot = scene.Capture();
        if (snapshot.Revision != expectedRevision) throw new InvalidOperationException("GamesSceneRevisionConflict");
        if (position is null || !position.IsValid) throw new ArgumentOutOfRangeException(nameof(position));
        if (!snapshot.Nodes.Any(node => node.NodeID == nodeID)) throw new KeyNotFoundException("GamesNodeNotFound");
        return snapshot with { Revision = checked(snapshot.Revision + 1), Nodes = snapshot.Nodes.Select(node => node.NodeID == nodeID
            ? node with { Spatial = node.Spatial with { Position = position } } : node).ToArray() };
    }
}

public sealed record GamesNativeNodeObservation(Guid NodeID, Guid EntityID, Guid ComponentID,
    GamesVector3 Position, GamesVector3 WorldPosition, Guid? MeshResourceID, int MeshVertexCount);
public sealed record GamesNativeSceneObservation(Guid ProjectID, Guid SceneID, long Revision,
    string ObservedEngineVersion, string ExecutableSha256, IReadOnlyList<GamesNativeNodeObservation> Nodes);
public interface IGamesSceneRuntime
{
    /// <summary>Runs only generated data-only scenes. It does not authorise project reads or execute user scripts.</summary>
    Task<GamesNativeSceneObservation> ObserveAsync(GamesSceneSnapshot scene, CancellationToken cancellationToken = default);
}

public sealed record GamesMeshModelingResult(GamesMeshResource Resource, string ObservedRuntimeVersion, string ExecutableSha256);
public interface IGamesMeshModeller
{
    /// <summary>Operation-scoped modelling over the same canonical resource ID; project writes remain separately authorised.</summary>
    Task<GamesMeshModelingResult> SubdivideAsync(GamesMeshResource resource, int levels, CancellationToken cancellationToken = default);
}
