using System.Text.Json;

namespace Haven.Application.Automations;

public enum AutomationGraphPortDirection
{
    Input = 0,
    Output = 1
}

public sealed record AutomationGraphPortDefinition(
    string Id,
    string Label,
    AutomationGraphPortDirection Direction,
    string DataType = "any",
    bool AllowsMultipleConnections = false);

public sealed record AutomationGraphDefinition(int Version, IReadOnlyList<AutomationGraphNodeDefinition> Nodes, IReadOnlyList<AutomationGraphEdgeDefinition> Edges)
{
    public const int CurrentVersion = 1;
    public static AutomationGraphDefinition Empty { get; } = new(CurrentVersion, [], []);
}

public sealed record AutomationGraphNodeDefinition(Guid Id, string Category, DeviceTargetDescriptor? DeviceTarget, string? ActionKey, IReadOnlyDictionary<string, string> Parameters)
{
    private static readonly IReadOnlyList<AutomationGraphPortDefinition> DefaultPorts =
    [
        new("in", "In", AutomationGraphPortDirection.Input, "flow", true),
        new("out", "Out", AutomationGraphPortDirection.Output, "flow", true)
    ];

    public double X { get; init; }
    public double Y { get; init; }
    public double Width { get; init; } = 220;
    public double Height { get; init; } = 118;
    public string Title { get; init; } = string.Empty;
    public string Subtitle { get; init; } = string.Empty;
    public IReadOnlyList<AutomationGraphPortDefinition> Ports { get; init; } = [];
    public IReadOnlyDictionary<string, string> Metadata { get; init; } = new Dictionary<string, string>(StringComparer.Ordinal);
    public IReadOnlyList<AutomationGraphPortDefinition> EffectivePorts => Ports is { Count: > 0 } ? Ports : DefaultPorts;

    public static AutomationGraphNodeDefinition FromDevice(DeviceAutomationNodeDefinition node) =>
        new(node.Id, DeviceAutomationNodeCategory.Key, node.Target, node.ActionKey, node.Parameters)
        {
            Title = node.ActionKey,
            Subtitle = node.Target.DisplayName,
            Ports = DefaultPorts
        };

    public DeviceAutomationNodeDefinition? ToDevice() =>
        string.Equals(Category, DeviceAutomationNodeCategory.Key, StringComparison.OrdinalIgnoreCase)
        && DeviceTarget is not null
        && !string.IsNullOrWhiteSpace(ActionKey)
            ? new DeviceAutomationNodeDefinition(
                Id,
                DeviceTarget,
                ActionKey,
                Parameters ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase))
            : null;
}

public sealed record AutomationGraphEdgeDefinition(Guid FromNodeId, Guid ToNodeId)
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string FromPortId { get; init; } = "out";
    public string ToPortId { get; init; } = "in";
    public string Branch { get; init; } = string.Empty;
    public string Label { get; init; } = string.Empty;
    public IReadOnlyDictionary<string, string> Metadata { get; init; } = new Dictionary<string, string>(StringComparer.Ordinal);
}

public static class AutomationGraphCodec
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static string Serialize(AutomationGraphDefinition graph)
    {
        ArgumentNullException.ThrowIfNull(graph);
        Validate(graph);
        return JsonSerializer.Serialize(graph, Options);
    }

    public static bool TryDeserialize(string? json, out AutomationGraphDefinition graph)
    {
        graph = AutomationGraphDefinition.Empty;
        if (string.IsNullOrWhiteSpace(json)) return true;
        try
        {
            using var document = JsonDocument.Parse(json);
            if (!HasExplicitStableIds(document.RootElement)) return false;
            var parsed = JsonSerializer.Deserialize<AutomationGraphDefinition>(json, Options);
            if (parsed is null) return false;
            Validate(parsed);
            graph = parsed;
            return true;
        }
        catch (JsonException) { return false; }
        catch (InvalidOperationException) { return false; }
    }

    private static void Validate(AutomationGraphDefinition graph)
    {
        if (graph.Version != AutomationGraphDefinition.CurrentVersion)
            throw new InvalidOperationException($"Unsupported Automation graph version {graph.Version}.");
        if (graph.Nodes is null || graph.Edges is null || graph.Nodes.Any(node => node is null) || graph.Edges.Any(edge => edge is null))
            throw new InvalidOperationException("Automation graph node and connection collections must contain actual values.");
        if (graph.Nodes.Any(node => node.Id == Guid.Empty))
            throw new InvalidOperationException("Automation graph node IDs must be non-empty.");
        if (graph.Edges.Any(edge => edge.Id == Guid.Empty))
            throw new InvalidOperationException("Automation graph connection IDs must be non-empty.");
        if (graph.Nodes.Select(node => node.Id).Distinct().Count() != graph.Nodes.Count)
            throw new InvalidOperationException("Automation graph node IDs must be unique.");
        if (graph.Edges.Select(edge => edge.Id).Distinct().Count() != graph.Edges.Count)
            throw new InvalidOperationException("Automation graph connection IDs must be unique.");
        var ids = graph.Nodes.Select(node => node.Id).ToHashSet();
        if (graph.Edges.Any(edge => !ids.Contains(edge.FromNodeId) || !ids.Contains(edge.ToNodeId)))
            throw new InvalidOperationException("Automation graph edges must reference existing nodes.");
    }

    private static bool HasExplicitStableIds(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object || !TryProperty(root, "nodes", out var nodes) || nodes.ValueKind != JsonValueKind.Array ||
            !TryProperty(root, "edges", out var edges) || edges.ValueKind != JsonValueKind.Array) return false;
        foreach (var node in nodes.EnumerateArray())
            if (!HasStableId(node)) return false;
        foreach (var edge in edges.EnumerateArray())
            if (!HasStableId(edge)) return false;
        return true;
    }

    private static bool HasStableId(JsonElement item) => item.ValueKind == JsonValueKind.Object &&
        TryProperty(item, "id", out var id) && id.ValueKind == JsonValueKind.String &&
        Guid.TryParse(id.GetString(), out var parsed) && parsed != Guid.Empty;

    private static bool TryProperty(JsonElement item, string name, out JsonElement value)
    {
        var found = false;
        foreach (var property in item.EnumerateObject())
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                if (found) { value = default; return false; }
                value = property.Value;
                found = true;
            }
        if (!found) value = default;
        return found;
    }
}
