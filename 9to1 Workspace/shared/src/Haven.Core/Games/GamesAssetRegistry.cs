namespace Haven.Core.Games;

/// <summary>The authored asset families whose identities must be preserved across Development, Creation and Rendering.</summary>
public enum GamesAssetKind { Mesh, Material, Texture, Audio, Animation }

/// <summary>A stable authored asset identity. The AssetID is the cross-workspace identity shared by hierarchy, viewport, components and assets.
/// Dependencies are declared references to other registered assets; they must resolve and form no cycle.</summary>
public sealed record GamesAsset(Guid AssetID, string Name, GamesAssetKind Kind, IReadOnlyList<Guid> Dependencies)
{
    public const int MaximumAssets = 8192;
    public const int MaximumDependenciesPerAsset = 512;

    public GamesAsset Capture()
    {
        if (AssetID == Guid.Empty) throw new InvalidDataException("Games asset identity is missing.");
        if (string.IsNullOrWhiteSpace(Name)) throw new InvalidDataException("Games asset name is required.");
        if (!Enum.IsDefined(Kind)) throw new InvalidDataException("Games asset kind is unsupported.");
        if (Dependencies is null) throw new InvalidDataException("Games asset dependencies are missing.");
        var snapshot = Dependencies.ToArray();
        if (snapshot.Length > MaximumDependenciesPerAsset) throw new InvalidDataException("Games asset exceeds the configured dependency limit.");
        if (snapshot.Contains(AssetID)) throw new InvalidOperationException("GamesAssetSelfDependency");
        if (snapshot.Distinct().Count() != snapshot.Length) throw new InvalidOperationException("GamesAssetDuplicateDependency");
        return this with { Dependencies = snapshot };
    }
}

/// <summary>The canonical authored asset registry. It preserves stable AssetIDs across editor workspaces and validates the dependency graph.
/// Revision-aware immutable transactions never mutate the original.</summary>
public sealed record GamesAssetRegistry(int Revision, IReadOnlyList<GamesAsset> Assets)
{
    public const int CurrentSchemaVersion = 1;

    public GamesAssetRegistry Capture()
    {
        if (Revision < 1) throw new InvalidDataException("Games asset registry revision must be positive.");
        if (Assets is null) throw new InvalidDataException("Games asset registry assets are missing.");
        var snapshot = Assets.Select(asset => asset?.Capture() ?? throw new InvalidDataException("Games asset registry contains a missing asset.")).ToArray();
        if (snapshot.Length > GamesAsset.MaximumAssets) throw new InvalidDataException("Games asset registry exceeds the configured asset limit.");
        if (snapshot.Select(a => a.AssetID).Distinct().Count() != snapshot.Length) throw new InvalidOperationException("GamesAssetDuplicateIdentity");
        var byID = snapshot.ToDictionary(a => a.AssetID);
        foreach (var asset in snapshot)
            foreach (var dependency in asset.Dependencies)
                if (!byID.ContainsKey(dependency)) throw new InvalidOperationException("GamesAssetUnknownDependency");
        ValidateAcyclic(snapshot, byID);
        return this with { Assets = snapshot };
    }

    public static GamesAssetRegistry Create(IEnumerable<GamesAsset> assets) => new GamesAssetRegistry(1, assets.ToArray()).Capture();

    private GamesAssetRegistry Next(int expectedRevision, IReadOnlyList<GamesAsset> assets)
    {
        if (Revision != expectedRevision) throw new InvalidOperationException("GamesAssetRegistryRevisionConflict");
        return new GamesAssetRegistry(checked(Revision + 1), assets).Capture();
    }

    public GamesAssetRegistry Register(GamesAsset asset, int expectedRevision)
    {
        ArgumentNullException.ThrowIfNull(asset);
        if (Assets.Any(existing => existing.AssetID == asset.AssetID)) throw new InvalidOperationException("GamesAssetAlreadyRegistered");
        return Next(expectedRevision, Assets.Append(asset).ToArray());
    }

    /// <summary>Deregister refuses while another registered asset still depends on the target, preserving reference integrity.</summary>
    public GamesAssetRegistry Deregister(Guid assetID, int expectedRevision)
    {
        if (!Assets.Any(asset => asset.AssetID == assetID)) throw new KeyNotFoundException("GamesAssetNotFound");
        if (Assets.Any(asset => asset.AssetID != assetID && asset.Dependencies.Contains(assetID)))
            throw new InvalidOperationException("GamesAssetInUse");
        return Next(expectedRevision, Assets.Where(asset => asset.AssetID != assetID).ToArray());
    }

    public GamesAssetRegistry AddDependency(Guid assetID, Guid dependencyID, int expectedRevision)
    {
        if (assetID == dependencyID) throw new InvalidOperationException("GamesAssetSelfDependency");
        var assets = Assets.Select(asset =>
        {
            if (asset.AssetID != assetID) return asset;
            if (asset.Dependencies.Contains(dependencyID)) throw new InvalidOperationException("GamesAssetDuplicateDependency");
            return asset with { Dependencies = [.. asset.Dependencies, dependencyID] };
        }).ToArray();
        if (!Assets.Any(asset => asset.AssetID == assetID)) throw new KeyNotFoundException("GamesAssetNotFound");
        return Next(expectedRevision, assets);
    }

    /// <summary>Depth-first cycle detection over the declared dependency graph.</summary>
    private static void ValidateAcyclic(GamesAsset[] assets, Dictionary<Guid, GamesAsset> byID)
    {
        var state = new Dictionary<Guid, int>(); // 0 unvisited, 1 visiting, 2 done
        foreach (var asset in assets)
            Visit(asset.AssetID, state, byID);
    }

    private static void Visit(Guid id, Dictionary<Guid, int> state, Dictionary<Guid, GamesAsset> byID)
    {
        if (state.TryGetValue(id, out var status))
        {
            if (status == 1) throw new InvalidOperationException("GamesAssetDependencyCycle");
            return;
        }
        state[id] = 1;
        foreach (var dependency in byID[id].Dependencies) Visit(dependency, state, byID);
        state[id] = 2;
    }
}
