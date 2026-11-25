using Haven.Core.Games;

namespace Haven.Core.Tests;

/// <summary>ED-GAMES-002: graph types share editing infrastructure but use their own legal nodes — not one universal engine.
/// Proves a Material (shader) graph rejects a Gameplay-only node, same-kind legality, connection validation and stale-edit no-mutation.</summary>
public sealed class GamesGraphTests
{
    private static readonly Guid GraphID = Guid.NewGuid();

    // A gameplay-only node (e.g. SendMail) is legal only in Gameplay graphs.
    private static GamesNodeType SendMail() => new("SendMail", [GamesGraphKind.Gameplay]);
    // A material-only node (e.g. PBRShader) is legal only in Material graphs.
    private static GamesNodeType PbrShader() => new("PBRShader", [GamesGraphKind.Material]);
    // A shared primitive is legal across kinds, demonstrating common editing infrastructure.
    private static GamesNodeType Constant() => new("Constant", [GamesGraphKind.Gameplay, GamesGraphKind.Material, GamesGraphKind.Animation, GamesGraphKind.Generation]);

    private static GamesGraphDocument GameplayGraph() =>
        GamesGraphDocument.Create(GraphID, GamesGraphKind.Gameplay, [SendMail(), Constant()]);
    private static GamesGraphDocument MaterialGraph() =>
        GamesGraphDocument.Create(GraphID, GamesGraphKind.Material, [PbrShader(), Constant()]);

    [Fact]
    public void ShaderGraphRejectsGameplayOnlyNode()
    {
        var material = MaterialGraph();
        // A shader graph must not expose an unrelated gameplay node such as SendMail.
        Assert.Throws<InvalidOperationException>(() => material.AddNode(new GamesGraphNode(Guid.NewGuid(), "SendMail"), 1));
    }

    [Fact]
    public void SameKindNodeIsAccepted()
    {
        var material = MaterialGraph();
        var added = material.AddNode(new GamesGraphNode(Guid.NewGuid(), "PBRShader"), 1);
        Assert.Equal(2, added.Revision);
        Assert.Single(added.Nodes);
        // Original is immutable.
        Assert.Empty(material.Nodes);
    }

    [Fact]
    public void SharedPrimitiveIsLegalAcrossKinds()
    {
        var gameplay = GameplayGraph().AddNode(new GamesGraphNode(Guid.NewGuid(), "Constant"), 1);
        var material = MaterialGraph().AddNode(new GamesGraphNode(Guid.NewGuid(), "Constant"), 1);
        Assert.Single(gameplay.Nodes);
        Assert.Single(material.Nodes);
    }

    [Fact]
    public void ConnectRejectsDanglingEndpointsAndStaleRevision()
    {
        var graph = GameplayGraph();
        var a = new GamesGraphNode(Guid.NewGuid(), "SendMail");
        var b = new GamesGraphNode(Guid.NewGuid(), "Constant");
        var withNodes = graph.AddNode(a, 1).AddNode(b, 2);

        // A connection to an unknown node is rejected as dangling.
        Assert.Throws<InvalidOperationException>(() => withNodes.Connect(new GamesGraphConnection(Guid.NewGuid(), a.NodeID, "out", Guid.NewGuid(), "in"), 3));
        // Stale revision is rejected without mutation.
        Assert.Throws<InvalidOperationException>(() => withNodes.Connect(new GamesGraphConnection(Guid.NewGuid(), a.NodeID, "out", b.NodeID, "in"), 99));

        var connected = withNodes.Connect(new GamesGraphConnection(Guid.NewGuid(), a.NodeID, "out", b.NodeID, "in"), 3);
        Assert.Single(connected.Connections);
    }

    [Fact]
    public void RemoveNodeCascadesItsConnections()
    {
        var graph = GameplayGraph();
        var a = new GamesGraphNode(Guid.NewGuid(), "SendMail");
        var b = new GamesGraphNode(Guid.NewGuid(), "Constant");
        var withNodes = graph.AddNode(a, 1).AddNode(b, 2);
        var connected = withNodes.Connect(new GamesGraphConnection(Guid.NewGuid(), a.NodeID, "out", b.NodeID, "in"), 3);

        var removed = connected.RemoveNode(a.NodeID, 4);
        Assert.Single(removed.Nodes);
        // The connection referencing the removed node is gone; no dangling edge remains.
        Assert.Empty(removed.Connections);
    }

    [Fact]
    public void AddNodeRejectsDuplicateIdentityAndUnknownType()
    {
        var graph = GameplayGraph();
        var a = new GamesGraphNode(Guid.NewGuid(), "SendMail");
        var withA = graph.AddNode(a, 1);
        Assert.Throws<InvalidOperationException>(() => withA.AddNode(a, 2));
        // A node type not registered for this graph kind is rejected.
        Assert.Throws<InvalidOperationException>(() => withA.AddNode(new GamesGraphNode(Guid.NewGuid(), "PBRShader"), 2));
    }

    [Fact]
    public void StaleEditNeverMutatesOriginal()
    {
        var graph = GameplayGraph();
        Assert.Throws<InvalidOperationException>(() => graph.AddNode(new GamesGraphNode(Guid.NewGuid(), "SendMail"), 99));
        Assert.Equal(1, graph.Revision);
        Assert.Empty(graph.Nodes);
    }
}
