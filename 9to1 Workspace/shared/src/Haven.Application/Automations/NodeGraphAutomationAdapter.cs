using System.Security.Cryptography;
using System.Text.Json;
using Haven.Application.NodeGraph;

namespace Haven.Application.Automations;

/// <summary>Compiled owning-app bindings, never a catalogue supplied by graph JSON or model output.</summary>
public sealed record AutomationNodeGraphBinding(string TypeId, int TypeVersion, string AppId, string CapabilityId,
    Func<string?, bool> SupportsEntity, Func<GraphNode, AutomationGraphNodeDefinition?> Project);

/// <summary>An execution projection receipt is not permission to activate or dispatch its graph.</summary>
public sealed record AutomationGraphProjection(Guid GraphId, long SourceRevision, int SourceSchemaVersion,
    string CapabilityProfileId, string SourceDigest, AutomationGraphDefinition Definition);

/// <summary>Uses the canonical Home graph schema and the existing Automation executor model.
/// Dispatch owners must revalidate the current canonical graph, its digest, policy and resource ACL.</summary>
public sealed class NodeGraphAutomationAdapter(NodeGraphSchemaRegistry schemas,
    IEnumerable<AutomationNodeGraphBinding> trustedBindings)
{
    private readonly AutomationNodeGraphBinding[] _bindings = trustedBindings.ToArray();

    public AutomationGraphProjection Compile(GraphDocument source)
    {
        ArgumentNullException.ThrowIfNull(source);
        // Snapshot before validation so mutation of caller-owned lists cannot alter the checked projection.
        var bytes = JsonSerializer.SerializeToUtf8Bytes(source);
        var graph = JsonSerializer.Deserialize<GraphDocument>(bytes) ?? throw new InvalidDataException("The canonical graph is missing.");
        var issues = schemas.Validate(graph);
        if (issues.Count != 0)
            throw new InvalidDataException("The canonical graph does not conform to its registered schema: " +
                string.Join(", ", issues.Select(issue => issue.Code)));
        var nodes = new List<AutomationGraphNodeDefinition>();
        var ports = new Dictionary<Guid, (Guid NodeId, string Key)>();
        foreach (var node in graph.Nodes)
        {
            var matches = _bindings.Where(binding => binding.TypeId == node.TypeId && binding.TypeVersion == node.TypeVersion &&
                binding.AppId == node.AppId && binding.CapabilityId == node.CapabilityId).ToArray();
            if (matches.Length != 1 || !matches[0].SupportsEntity(node.EntityId))
                throw new InvalidDataException("The node has no unambiguous owning Automation binding.");
            var projected = matches[0].Project(node);
            if (projected is null || projected.Id != node.NodeId || string.IsNullOrWhiteSpace(projected.Category))
                throw new InvalidDataException("The owning binding changed or omitted the canonical node identity.");
            if (projected.EffectivePorts.Count != node.Ports.Count)
                throw new InvalidDataException("The owning binding changed the canonical port schema.");
            foreach (var port in node.Ports)
            {
                var candidates = projected.EffectivePorts.Where(value => value.Id == port.Key).ToArray();
                if (candidates.Length != 1 || candidates[0].DataType != port.DataType ||
                    candidates[0].AllowsMultipleConnections != port.Multiple ||
                    candidates[0].Direction != (port.Direction == GraphPortDirection.Input
                        ? AutomationGraphPortDirection.Input : AutomationGraphPortDirection.Output))
                    throw new InvalidDataException("The owning binding changed a canonical port identity or type.");
                ports.Add(port.PortId, (node.NodeId, port.Key));
            }
            nodes.Add(projected);
        }
        var edges = graph.Connections.Select(connection =>
        {
            var output = ports[connection.OutputPortId];
            var input = ports[connection.InputPortId];
            return new AutomationGraphEdgeDefinition(output.NodeId, input.NodeId)
            {
                Id = connection.ConnectionId, FromPortId = output.Key, ToPortId = input.Key
            };
        }).ToArray();
        // The existing codec supplies the Automation model's structural validation and detached payload.
        var definition = new AutomationGraphDefinition(AutomationGraphDefinition.CurrentVersion, nodes, edges);
        if (!AutomationGraphCodec.TryDeserialize(AutomationGraphCodec.Serialize(definition), out var detached))
            throw new InvalidDataException("The owning Automation projection cannot be represented by its canonical codec.");
        return new(graph.GraphId, graph.Revision, graph.SchemaVersion, graph.CapabilityProfileId,
            Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), detached);
    }
}
