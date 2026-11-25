namespace Haven.Core.Games;

/// <summary>The four distinct graph families. Each uses its own legal node set, compiler and runtime — they are NOT one universal execution engine.
/// A Material (shader) graph must never expose a Gameplay-only node such as SendMail.</summary>
public enum GamesGraphKind { Gameplay, Material, Animation, Generation }

/// <summary>A registered node type. Every node declares which graph kinds it is legal in. This is the shared editing infrastructure: all graphs add/connect/validate nodes the same way, but legality is per-kind.</summary>
public sealed record GamesNodeType(string Name, IReadOnlyList<GamesGraphKind> LegalKinds)
{
    public GamesNodeType Capture()
    {
        if (string.IsNullOrWhiteSpace(Name)) throw new InvalidDataException("Games graph node type name is required.");
        if (LegalKinds is null || LegalKinds.Count == 0) throw new InvalidDataException("Games graph node type must declare at least one legal graph kind.");
        if (LegalKinds.Any(kind => !Enum.IsDefined(kind))) throw new InvalidDataException("Games graph node type declares an unsupported graph kind.");
        return this;
    }

    public bool IsLegalIn(GamesGraphKind kind) => LegalKinds.Contains(kind);
}

/// <summary>A placed node instance inside a graph document.</summary>
public sealed record GamesGraphNode(Guid NodeID, string TypeName)
{
    public GamesGraphNode Capture()
    {
        if (NodeID == Guid.Empty) throw new InvalidDataException("Games graph node identity is missing.");
        if (string.IsNullOrWhiteSpace(TypeName)) throw new InvalidDataException("Games graph node type name is required.");
        return this;
    }
}

/// <summary>A directed connection between two node ports.</summary>
public sealed record GamesGraphConnection(Guid ConnectionID, Guid FromNodeID, string FromPort, Guid ToNodeID, string ToPort)
{
    public GamesGraphConnection Capture()
    {
        if (ConnectionID == Guid.Empty) throw new InvalidDataException("Games graph connection identity is missing.");
        if (FromNodeID == Guid.Empty || ToNodeID == Guid.Empty) throw new InvalidDataException("Games graph connection endpoints are required.");
        if (string.IsNullOrWhiteSpace(FromPort) || string.IsNullOrWhiteSpace(ToPort)) throw new InvalidDataException("Games graph connection ports are required.");
        return this;
    }
}

/// <summary>A pure authored graph document. It carries its kind and the registered node-type table that governs legality, so a single shared editing
/// contract serves all four families while each enforces its own legal nodes. Revision-aware immutable transactions never mutate the original.</summary>
public sealed record GamesGraphDocument(Guid GraphID, GamesGraphKind Kind, int Revision,
    IReadOnlyList<GamesNodeType> RegisteredNodeTypes, IReadOnlyList<GamesGraphNode> Nodes, IReadOnlyList<GamesGraphConnection> Connections)
{
    public const int CurrentSchemaVersion = 1;

    public GamesGraphDocument Capture()
    {
        if (GraphID == Guid.Empty) throw new InvalidDataException("Games graph identity is missing.");
        if (!Enum.IsDefined(Kind)) throw new InvalidDataException("Games graph kind is unsupported.");
        if (Revision < 1) throw new InvalidDataException("Games graph revision must be positive.");
        if (RegisteredNodeTypes is null || Nodes is null || Connections is null) throw new InvalidDataException("Games graph structure is missing.");
        var types = RegisteredNodeTypes.Select(t => t?.Capture() ?? throw new InvalidDataException("Games graph contains a missing node type.")).ToArray();
        if (types.Select(t => t.Name).Distinct(StringComparer.Ordinal).Count() != types.Length) throw new InvalidDataException("Games graph contains duplicate registered node types.");
        var legal = types.ToDictionary(t => t.Name, StringComparer.Ordinal);
        var nodes = Nodes.Select(n => n?.Capture() ?? throw new InvalidDataException("Games graph contains a missing node.")).ToArray();
        if (nodes.Select(n => n.NodeID).Distinct().Count() != nodes.Length) throw new InvalidDataException("Games graph contains duplicate node identities.");
        foreach (var node in nodes)
            if (!legal.TryGetValue(node.TypeName, out var type) || !type.IsLegalIn(Kind))
                throw new InvalidOperationException("GamesGraphNodeNotLegalForKind");
        var connections = Connections.Select(c => c?.Capture() ?? throw new InvalidDataException("Games graph contains a missing connection.")).ToArray();
        if (connections.Select(c => c.ConnectionID).Distinct().Count() != connections.Length) throw new InvalidDataException("Games graph contains duplicate connection identities.");
        var nodeIDs = nodes.Select(n => n.NodeID).ToHashSet();
        foreach (var connection in connections)
            if (!nodeIDs.Contains(connection.FromNodeID) || !nodeIDs.Contains(connection.ToNodeID))
                throw new InvalidOperationException("GamesGraphConnectionDangling");
        return this with { RegisteredNodeTypes = types, Nodes = nodes, Connections = connections };
    }

    public static GamesGraphDocument Create(Guid graphID, GamesGraphKind kind, IEnumerable<GamesNodeType> registeredNodeTypes)
        => (new GamesGraphDocument(graphID, kind, 1, registeredNodeTypes.ToArray(), Array.Empty<GamesGraphNode>(), Array.Empty<GamesGraphConnection>())).Capture();

    private GamesGraphDocument Next(int expectedRevision, IReadOnlyList<GamesGraphNode> nodes, IReadOnlyList<GamesGraphConnection> connections)
    {
        if (Revision != expectedRevision) throw new InvalidOperationException("GamesGraphRevisionConflict");
        return new GamesGraphDocument(GraphID, Kind, checked(Revision + 1), RegisteredNodeTypes, nodes, connections).Capture();
    }

    public GamesGraphDocument AddNode(GamesGraphNode node, int expectedRevision)
    {
        ArgumentNullException.ThrowIfNull(node);
        if (Nodes.Any(existing => existing.NodeID == node.NodeID)) throw new InvalidOperationException("GamesGraphNodeAlreadyPresent");
        return Next(expectedRevision, Nodes.Append(node).ToArray(), Connections);
    }

    public GamesGraphDocument RemoveNode(Guid nodeID, int expectedRevision)
    {
        if (!Nodes.Any(node => node.NodeID == nodeID)) throw new KeyNotFoundException("GamesGraphNodeNotFound");
        var nodes = Nodes.Where(node => node.NodeID != nodeID).ToArray();
        // Removing a node also removes any connection that referenced it so no dangling edge survives.
        var connections = Connections.Where(connection => connection.FromNodeID != nodeID && connection.ToNodeID != nodeID).ToArray();
        return Next(expectedRevision, nodes, connections);
    }

    public GamesGraphDocument Connect(GamesGraphConnection connection, int expectedRevision)
    {
        ArgumentNullException.ThrowIfNull(connection);
        if (Connections.Any(existing => existing.ConnectionID == connection.ConnectionID)) throw new InvalidOperationException("GamesGraphConnectionAlreadyPresent");
        return Next(expectedRevision, Nodes, Connections.Append(connection).ToArray());
    }

    public GamesGraphDocument Disconnect(Guid connectionID, int expectedRevision)
    {
        if (!Connections.Any(connection => connection.ConnectionID == connectionID)) throw new KeyNotFoundException("GamesGraphConnectionNotFound");
        return Next(expectedRevision, Nodes, Connections.Where(connection => connection.ConnectionID != connectionID).ToArray());
    }
}
