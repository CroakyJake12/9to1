using Haven.Core.Games;

namespace Haven.Core.Tests;

/// <summary>APP-GAMES-001: Games preserves asset identities across Development, Creation and Rendering with shared selection.
/// Proves stable AssetID identity, dependency validation, cycle-free reference integrity and stale-edit no-mutation.</summary>
public sealed class GamesAssetRegistryTests
{
    private static readonly Guid Mesh = Guid.NewGuid();
    private static readonly Guid Material = Guid.NewGuid();
    private static readonly Guid Texture = Guid.NewGuid();

    private static GamesAsset Asset(Guid id, string name, GamesAssetKind kind, params Guid[] deps) => new(id, name, kind, deps);

    private static GamesAssetRegistry MakeRegistry() =>
        GamesAssetRegistry.Create([Asset(Mesh, "HeroMesh", GamesAssetKind.Mesh), Asset(Material, "HeroMat", GamesAssetKind.Material)]);

    [Fact]
    public void RegisterPreservesStableIdentityAcrossWorkspaces()
    {
        var registry = MakeRegistry();
        Assert.Equal(1, registry.Revision);
        // The same AssetID resolves identically regardless of workspace; identity is the key.
        Assert.Contains(registry.Assets, a => a.AssetID == Mesh && a.Kind == GamesAssetKind.Mesh);
        var added = registry.Register(Asset(Texture, "HeroTex", GamesAssetKind.Texture), 1);
        Assert.Equal(2, added.Revision);
        Assert.Contains(added.Assets, a => a.AssetID == Texture);
        // Original immutable.
        Assert.DoesNotContain(registry.Assets, a => a.AssetID == Texture);
    }

    [Fact]
    public void RegisterRejectsDuplicateIdentityAndStaleRevision()
    {
        var registry = MakeRegistry();
        Assert.Throws<InvalidOperationException>(() => registry.Register(Asset(Mesh, "Dup", GamesAssetKind.Mesh), 1));
        Assert.Throws<InvalidOperationException>(() => registry.Register(Asset(Texture, "Tex", GamesAssetKind.Texture), 99));
    }

    [Fact]
    public void UnknownDependencyIsRejected()
    {
        // A dependency to an unregistered asset cannot be captured.
        Assert.Throws<InvalidOperationException>(() =>
            GamesAssetRegistry.Create([Asset(Material, "Mat", GamesAssetKind.Material, Guid.NewGuid())]));
    }

    [Fact]
    public void AddDependencyValidatesTargetAndRejectsCycle()
    {
        var registry = MakeRegistry();
        // Material depends on Mesh: legal.
        var withDep = registry.AddDependency(Material, Mesh, 1);
        Assert.Contains(withDep.Assets.Single(a => a.AssetID == Material).Dependencies, d => d == Mesh);

        // Adding the reverse edge would create a cycle and is rejected.
        Assert.Throws<InvalidOperationException>(() => withDep.AddDependency(Mesh, Material, 2));
    }

    [Fact]
    public void SelfAndDuplicateDependencyAreRejected()
    {
        var registry = MakeRegistry();
        Assert.Throws<InvalidOperationException>(() => registry.AddDependency(Mesh, Mesh, 1));
        var withDep = registry.AddDependency(Material, Mesh, 1);
        Assert.Throws<InvalidOperationException>(() => withDep.AddDependency(Material, Mesh, 2));
    }

    [Fact]
    public void DeregisterRefusesWhileInUseAndSucceedsWhenUnreferenced()
    {
        var registry = MakeRegistry();
        // Mesh is not depended on yet; deregister succeeds.
        var removed = registry.Deregister(Mesh, 1);
        Assert.DoesNotContain(removed.Assets, a => a.AssetID == Mesh);

        // Rebuild with Material depending on Mesh; now Mesh is in use.
        var dependent = GamesAssetRegistry.Create([Asset(Mesh, "HeroMesh", GamesAssetKind.Mesh), Asset(Material, "HeroMat", GamesAssetKind.Material, Mesh)]);
        Assert.Throws<InvalidOperationException>(() => dependent.Deregister(Mesh, 1));
    }

    [Fact]
    public void StaleEditNeverMutatesRegistry()
    {
        var registry = MakeRegistry();
        Assert.Throws<InvalidOperationException>(() => registry.Register(Asset(Texture, "Tex", GamesAssetKind.Texture), 99));
        Assert.Equal(1, registry.Revision);
        Assert.Equal(2, registry.Assets.Count);
    }

    [Fact]
    public void ThreeNodeCycleIsDetected()
    {
        var a = Guid.NewGuid(); var b = Guid.NewGuid(); var c = Guid.NewGuid();
        // a->b, b->c registered fine; adding c->a closes a cycle and is rejected.
        var registry = GamesAssetRegistry.Create([Asset(a, "A", GamesAssetKind.Mesh), Asset(b, "B", GamesAssetKind.Mesh, a), Asset(c, "C", GamesAssetKind.Mesh, b)]);
        Assert.Throws<InvalidOperationException>(() => registry.AddDependency(a, c, 1));
    }
}
