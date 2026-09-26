namespace Haven.UI.Components;

public enum NodeEditorPortDirection
{
    Input = 0,
    Output = 1
}

public sealed record NodeEditorPort(
    string Id,
    string Label,
    NodeEditorPortDirection Direction,
    string DataType = "flow",
    bool AllowsMultipleConnections = true);

public sealed record NodeEditorNode(Guid Id, string Category, string Title)
{
    public string TypeId { get; init; } = string.Empty;
    public int SchemaVersion { get; init; } = 1;
    public string Subtitle { get; init; } = string.Empty;
    public double X { get; init; }
    public double Y { get; init; }
    public double Width { get; init; } = 220;
    public double Height { get; init; } = 118;
    public IReadOnlyList<NodeEditorPort> Ports { get; init; } = [];
    public IReadOnlyDictionary<string, string> Metadata { get; init; } = new Dictionary<string, string>(StringComparer.Ordinal);
}

public sealed record NodeEditorEdge(Guid Id, Guid FromNodeId, string FromPortId, Guid ToNodeId, string ToPortId)
{
    public string Label { get; init; } = string.Empty;
    public string Branch { get; init; } = string.Empty;
    public IReadOnlyDictionary<string, string> Metadata { get; init; } = new Dictionary<string, string>(StringComparer.Ordinal);
}

public sealed record NodeEditorDocument(IReadOnlyList<NodeEditorNode> Nodes, IReadOnlyList<NodeEditorEdge> Edges)
{
    public Guid GraphId { get; init; } = Guid.NewGuid();
    public int SchemaVersion { get; init; } = 1;
    public long Revision { get; init; }
    public NodeEditorRevisionState State { get; init; } = NodeEditorRevisionState.Draft;
    public IReadOnlyList<NodeEditorGroup> Groups { get; init; } = [];
    public IReadOnlyList<NodeEditorComment> Comments { get; init; } = [];
    public IReadOnlyDictionary<Guid, NodeEditorExecutionState> ExecutionStates { get; init; } = new Dictionary<Guid, NodeEditorExecutionState>();
    public string? ConnectionProfileId { get; init; }
    public static NodeEditorDocument Empty { get; } = new([], []);
}

public enum NodeEditorRevisionState { Draft, Active }
public enum NodeEditorExecutionState { Idle, Queued, Running, Succeeded, Failed, Skipped }
public sealed record NodeEditorGroup(Guid Id, string Title, IReadOnlyList<Guid> NodeIds, string? Color = null);
public sealed record NodeEditorComment(Guid Id, string Text, double X, double Y, double Width = 220, double Height = 100);
public sealed record NodeEditorNodeSchema(string TypeId, int SchemaVersion, IReadOnlyList<NodeEditorPort> Ports,
    IReadOnlySet<string> RequiredCapabilities, bool IsConversionNode = false);
public sealed record NodeEditorConnectionProfile(string Id, IReadOnlySet<string> AllowedTypes,
    IReadOnlySet<string> RequiredCapabilities);

public sealed class NodeEditorSchemaRegistry
{
    private readonly Dictionary<string, NodeEditorNodeSchema> _schemas = new(StringComparer.Ordinal);
    private readonly Dictionary<string, NodeEditorConnectionProfile> _profiles = new(StringComparer.Ordinal);
    public void Register(NodeEditorNodeSchema schema)
    {
        ArgumentNullException.ThrowIfNull(schema);
        if (string.IsNullOrWhiteSpace(schema.TypeId) || schema.SchemaVersion < 1)
            throw new ArgumentException("Graph node schemas require a stable type ID and positive version.", nameof(schema));
        if (_schemas.TryGetValue(schema.TypeId, out var prior) && schema.SchemaVersion < prior.SchemaVersion)
            throw new InvalidOperationException("Graph node schema versions cannot move backwards.");
        _schemas[schema.TypeId] = schema;
    }
    public void Register(NodeEditorConnectionProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (string.IsNullOrWhiteSpace(profile.Id)) throw new ArgumentException("Connection profiles need a stable ID.", nameof(profile));
        _profiles[profile.Id] = profile;
    }
    public NodeEditorNodeSchema? GetSchema(string typeId) => _schemas.GetValueOrDefault(typeId);
    public NodeEditorConnectionProfile? GetProfile(string profileId) => _profiles.GetValueOrDefault(profileId);
}

/// <summary>Typed boundary for consumers such as Automation and Dulche; UI gesture state is excluded.</summary>
public interface INodeEditorGraphApi
{
    NodeEditorDocument GetGraph();
    IReadOnlyList<NodeEditorDiagnostic> Validate();
    bool TryActivate(long expectedRevision, out NodeEditorDocument activeGraph);
    bool TryApplyExecutionState(Guid nodeId, NodeEditorExecutionState state);
}

public sealed record NodeEditorTemplate(
    string Category,
    string Title,
    string Subtitle,
    IReadOnlyList<NodeEditorPort> Ports,
    IReadOnlyDictionary<string, string>? Metadata = null);

public sealed record NodeEditorDiagnostic(string Code, string Message, Guid? NodeId = null, Guid? EdgeId = null);

internal sealed record NodeEditorClipboardPayload(IReadOnlyList<NodeEditorNode> Nodes, IReadOnlyList<NodeEditorEdge> Edges);
