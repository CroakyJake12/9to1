using System.Text.Json;
using Haven.Application;

namespace Haven.Application.NodeGraph;

/// <summary>Owning-store metadata, never an access grant. StoreId must come from the actual owner adapter.</summary>
public sealed record GraphOwnerDescriptor(string AppId, Guid StoreId, string EntityKind, Guid EntityId)
{
    public string CanonicalEntityId => EntityKind + "/" + StoreId.ToString("D") + "/" + EntityId.ToString("D");
}
public enum GraphPublicationKind { SaveDraft, Activate }
public sealed record GraphPublicationReceipt(Guid OperationId, string ArgumentsDigest, GraphPublicationKind Kind,
    GraphOwnerDescriptor Owner, Guid GraphId, long ExpectedGraphRevision, long CommittedGraphRevision);

/// <summary>Detached proposal only. Capture performs structural/schema admission; it cannot issue permission or a run permit.</summary>
public sealed class GraphPublicationIntent
{
    private GraphPublicationIntent(GraphOwnerDescriptor owner, AuthenticatedResourceActor actor, Guid operationId,
        GraphPublicationKind kind, GraphDocument graph, long expectedRevision, IReadOnlyList<ResourceScope> scopes)
    { Owner = owner; OriginalActor = actor; OperationId = operationId; Kind = kind; Graph = graph;
      ExpectedGraphRevision = expectedRevision; Scopes = scopes; }
    public GraphOwnerDescriptor Owner { get; }
    public AuthenticatedResourceActor OriginalActor { get; }
    public Guid OperationId { get; }
    public GraphPublicationKind Kind { get; }
    public GraphDocument Graph { get; }
    public long ExpectedGraphRevision { get; }
    public IReadOnlyList<ResourceScope> Scopes { get; }
    public JsonElement Arguments => JsonSerializer.SerializeToElement(new
    { SchemaVersion = 1, Owner, OriginalActor, OperationId, Kind, Graph, ExpectedGraphRevision, Scopes });

    public static GraphPublicationIntent Capture(GraphOwnerDescriptor owner, AuthenticatedResourceActor originalActor,
        Guid operationId, GraphPublicationKind kind, GraphDocument graph, long expectedGraphRevision,
        IReadOnlyList<ResourceScope> scopes, NodeGraphSchemaRegistry schemas, GraphConfigurationCaptureLimits configurationLimits)
    {
        ArgumentNullException.ThrowIfNull(owner); ArgumentNullException.ThrowIfNull(originalActor);
        ArgumentNullException.ThrowIfNull(graph); ArgumentNullException.ThrowIfNull(scopes);
        ArgumentNullException.ThrowIfNull(schemas);
        if (string.IsNullOrWhiteSpace(owner.AppId) || string.IsNullOrWhiteSpace(owner.EntityKind) ||
            owner.StoreId == Guid.Empty || owner.EntityId == Guid.Empty || operationId == Guid.Empty ||
            !Enum.IsDefined(kind) || expectedGraphRevision < 0 || graph.State != GraphRevisionState.Draft ||
            kind == GraphPublicationKind.SaveDraft && (expectedGraphRevision == long.MaxValue || graph.Revision != expectedGraphRevision + 1) ||
            kind == GraphPublicationKind.Activate && (expectedGraphRevision == 0 || graph.Revision != expectedGraphRevision))
            throw new ArgumentException("Exact canonical graph publication proposal required.");
        var configurations = new GraphConfigurationCapture(configurationLimits);
        var allowed = schemas.ListNodeTypes(graph.CapabilityProfileId);
        var nodes = new List<GraphNode>();
        foreach (var node in graph.Nodes ?? throw new ArgumentException("Graph nodes required."))
        {
            if (nodes.Count == 10000 || node is null) throw new ArgumentException("Graph node admission exceeded.");
            var types = allowed.Where(type => type.TypeId == node.TypeId && type.Version == node.TypeVersion).ToArray();
            if (types.Length != 1) throw new ArgumentException("Trusted registered node type required.");
            var ports = new List<GraphPort>();
            foreach (var port in node.Ports ?? throw new ArgumentException("Node ports required."))
            {
                if (ports.Count == types[0].Ports.Count || port is null) throw new ArgumentException("Registered port shape exceeded.");
                ports.Add(port);
            }
            nodes.Add(node with { Configuration = configurations.Capture(node.Configuration), Ports = Array.AsReadOnly(ports.ToArray()) });
        }
        var connections = new List<GraphConnection>();
        foreach (var connection in graph.Connections ?? throw new ArgumentException("Graph connections required."))
        {
            if (connections.Count == 50000 || connection is null) throw new ArgumentException("Graph connection admission exceeded.");
            connections.Add(connection);
        }
        var capturedGraph = graph with { Nodes = Array.AsReadOnly(nodes.ToArray()), Connections = Array.AsReadOnly(connections.ToArray()) };
        if (schemas.Validate(capturedGraph).Count != 0) throw new ArgumentException("Graph schema admission failed.");
        var capturedScopes = new List<ResourceScope>();
        foreach (var scope in scopes)
        {
            if (capturedScopes.Count == 1000 || scope is null || string.IsNullOrWhiteSpace(scope.Kind) ||
                string.IsNullOrWhiteSpace(scope.Id) || string.IsNullOrWhiteSpace(scope.Revision) || !Enum.IsDefined(scope.Access))
                throw new ArgumentException("Exact bounded resource scope required.");
            capturedScopes.Add(scope);
        }
        if (capturedScopes.Count == 0) throw new ArgumentException("Owning resource scopes required.");
        return new(owner, originalActor, operationId, kind, capturedGraph, expectedGraphRevision,
            Array.AsReadOnly(capturedScopes.ToArray()));
    }
}
