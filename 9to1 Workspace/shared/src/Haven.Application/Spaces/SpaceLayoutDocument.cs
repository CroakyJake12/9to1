namespace Haven.Application;

public enum SpaceLayoutPortDirection
{
    Input = 0,
    Output = 1
}

// Values align with the shared NodeEditor contract; unknown values remain rejectable on restore.
public enum SpaceLayoutRevisionState { Draft = 0, Active = 1 }

public sealed record SpaceLayoutPort(
    string Id,
    string Label,
    SpaceLayoutPortDirection Direction,
    string DataType = "flow",
    bool AllowsMultipleConnections = true);

public sealed record SpaceLayoutNode(Guid Id, string Category, string Title)
{
    public string TypeId { get; init; } = string.Empty; // Empty is structural-only and has no executable node schema.
    public int SchemaVersion { get; init; } = 1;
    public string Subtitle { get; init; } = string.Empty;
    public double X { get; init; }
    public double Y { get; init; }
    public double Width { get; init; } = 220;
    public double Height { get; init; } = 118;
    public IReadOnlyList<SpaceLayoutPort> Ports { get; init; } = [];
    public IReadOnlyDictionary<string, string> Metadata { get; init; } = new Dictionary<string, string>(StringComparer.Ordinal);
}

public sealed record SpaceLayoutEdge(Guid Id, Guid FromNodeId, string FromPortId, Guid ToNodeId, string ToPortId)
{
    public string Label { get; init; } = string.Empty;
    public string Branch { get; init; } = string.Empty;
    public IReadOnlyDictionary<string, string> Metadata { get; init; } = new Dictionary<string, string>(StringComparer.Ordinal);
}

public sealed record SpaceLayoutDocument(IReadOnlyList<SpaceLayoutNode> Nodes, IReadOnlyList<SpaceLayoutEdge> Edges)
{
    public const int MaximumNodes = 500;
    public const int MaximumEdges = 2000;
    public const int CurrentSchemaVersion = 1;

    /// <summary>Stable graph identity; Guid.Empty is the legacy/no-id sentinel normalized before editing or persistence.</summary>
    public Guid GraphId { get; init; }
    public int SchemaVersion { get; init; } = CurrentSchemaVersion;
    public long Revision { get; init; }
    public SpaceLayoutRevisionState State { get; init; } = SpaceLayoutRevisionState.Draft;
    public string? ConnectionProfileId { get; init; }
    public IReadOnlyList<SpaceLayoutGroup> Groups { get; init; } = [];
    public IReadOnlyList<SpaceLayoutComment> Comments { get; init; } = [];

    public static SpaceLayoutDocument Empty => new([], []);
}

public sealed record SpaceLayoutGroup(Guid Id, string Title, IReadOnlyList<Guid> NodeIds, string? Color = null);

public sealed record SpaceLayoutComment(Guid Id, string Text, double X, double Y, double Width = 220, double Height = 100);
