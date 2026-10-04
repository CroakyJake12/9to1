using System.Text.Json;
using NineToOne.Cui.AI;

namespace Haven.Application.NodeGraph;

public enum GraphPortDirection { Input, Output }
public enum GraphRevisionState { Draft, Active, NeedsAttention }
public sealed record GraphPort(Guid PortId, string Key, GraphPortDirection Direction, string DataType, bool Multiple);
public sealed record GraphNode(Guid NodeId, string TypeId, int TypeVersion, string AppId, string CapabilityId,
    string? EntityId, JsonElement Configuration, IReadOnlyList<GraphPort> Ports);
public sealed record GraphConnection(Guid ConnectionId, Guid OutputPortId, Guid InputPortId);
public sealed record GraphDocument(int SchemaVersion, Guid GraphId, long Revision, string CapabilityProfileId,
    GraphRevisionState State, IReadOnlyList<GraphNode> Nodes, IReadOnlyList<GraphConnection> Connections)
{
    public const int CurrentSchemaVersion = 1;
}
public sealed record GraphNodeType(string TypeId, int Version, string AppId, string CapabilityId,
    JsonElement ConfigurationSchema, IReadOnlyList<GraphPortTemplate> Ports);
public sealed record GraphPortTemplate(string Key, GraphPortDirection Direction, string DataType, bool Multiple = false);
public sealed record GraphCapabilityProfile(string ProfileId, IReadOnlySet<string> AllowedNodeTypes,
    IReadOnlySet<string> AllowedCapabilityIds);
public sealed record GraphDiagnostic(string Code, Guid? NodeId = null, Guid? ConnectionId = null);

/// <summary>Shared schema validation only. Owning apps/Dulche execute authorised actions; this registry never executes a graph.</summary>
public sealed class NodeGraphSchemaRegistry
{
    private readonly GraphNodeType[] _types;
    private readonly GraphCapabilityProfile[] _profiles;
    public NodeGraphSchemaRegistry(IEnumerable<GraphNodeType> types, IEnumerable<GraphCapabilityProfile> profiles)
    {
        // Clone mutable collections and JSON at the trusted registration boundary.
        _types = types.Select(type => type with { ConfigurationSchema = type.ConfigurationSchema.Clone(), Ports = type.Ports.ToArray() }).ToArray();
        _profiles = profiles.Select(profile => profile with
        { AllowedNodeTypes = new HashSet<string>(profile.AllowedNodeTypes, StringComparer.Ordinal),
          AllowedCapabilityIds = new HashSet<string>(profile.AllowedCapabilityIds, StringComparer.Ordinal) }).ToArray();
    }
    public IReadOnlyList<GraphNodeType> ListNodeTypes(string profileId)
    {
        var profiles = _profiles.Where(profile => profile.ProfileId == profileId).ToArray();
        if (profiles.Length != 1) return [];
        var profile = profiles[0];
        return _types.Where(type => profile.AllowedNodeTypes.Contains(type.TypeId) && profile.AllowedCapabilityIds.Contains(type.CapabilityId)
            && _types.Count(other => other.TypeId == type.TypeId && other.Version == type.Version) == 1)
            .Select(type => type with { ConfigurationSchema = type.ConfigurationSchema.Clone(), Ports = type.Ports.ToArray() }).ToArray();
    }
    public IReadOnlyList<GraphDiagnostic> Validate(GraphDocument graph)
    {
        var errors = new List<GraphDiagnostic>();
        if (graph is null || graph.Nodes is null || graph.Connections is null || graph.Nodes.Count > 10000 || graph.Connections.Count > 50000) return [new("graph.structure.invalid")];
        if (graph.SchemaVersion != GraphDocument.CurrentSchemaVersion) return [new("graph.schema.migration-required")];
        if (graph.GraphId == Guid.Empty || graph.Revision < 1 || !Enum.IsDefined(graph.State)) errors.Add(new("graph.identity.invalid"));
        var available = ListNodeTypes(graph.CapabilityProfileId);
        if (_profiles.Count(profile => profile.ProfileId == graph.CapabilityProfileId) != 1) errors.Add(new("graph.profile.unknown"));
        var nodes = new HashSet<Guid>(); var ports = new Dictionary<Guid, GraphPort>();
        foreach (var node in graph.Nodes)
        {
            if (node is null || node.Ports is null) { errors.Add(new("graph.node.structure.invalid")); continue; }
            if (node.NodeId == Guid.Empty || !nodes.Add(node.NodeId)) errors.Add(new("graph.node.identity.invalid", node.NodeId));
            var matches = available.Where(type => type.TypeId == node.TypeId && type.Version == node.TypeVersion).ToArray();
            if (matches.Length != 1) { errors.Add(new("graph.node.migration-or-permission-required", node.NodeId)); continue; }
            var definition = matches[0];
            if (node.AppId != definition.AppId || node.CapabilityId != definition.CapabilityId) errors.Add(new("graph.node.authority.invalid", node.NodeId));
            if (!ActionJsonSchemaValidator.Validate(definition.ConfigurationSchema.GetRawText(), node.Configuration, out _)) errors.Add(new("graph.node.configuration.invalid", node.NodeId));
            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var port in node.Ports)
            {
                if (port is null) { errors.Add(new("graph.port.schema.invalid", node.NodeId)); continue; }
                if (port.PortId == Guid.Empty || !ports.TryAdd(port.PortId, port) || !keys.Add(port.Key)) errors.Add(new("graph.port.identity.invalid", node.NodeId));
                if (!definition.Ports.Any(template => template.Key == port.Key && template.Direction == port.Direction && template.DataType == port.DataType && template.Multiple == port.Multiple))
                    errors.Add(new("graph.port.schema.invalid", node.NodeId));
            }
            if (node.Ports.Count != definition.Ports.Count) errors.Add(new("graph.port.schema.invalid", node.NodeId));
        }
        var connections = new HashSet<Guid>(); var inputs = new HashSet<Guid>(); var outputs = new HashSet<Guid>();
        foreach (var connection in graph.Connections)
        {
            if (connection is null) { errors.Add(new("graph.connection.structure.invalid")); continue; }
            if (connection.ConnectionId == Guid.Empty || !connections.Add(connection.ConnectionId)) errors.Add(new("graph.connection.identity.invalid", null, connection.ConnectionId));
            if (!ports.TryGetValue(connection.OutputPortId, out var output) || !ports.TryGetValue(connection.InputPortId, out var input))
            { errors.Add(new("graph.connection.port.missing", null, connection.ConnectionId)); continue; }
            if (output.Direction != GraphPortDirection.Output || input.Direction != GraphPortDirection.Input || output.DataType != input.DataType)
                errors.Add(new("graph.connection.type.invalid", null, connection.ConnectionId));
            if ((!inputs.Add(input.PortId) && !input.Multiple) || (!outputs.Add(output.PortId) && !output.Multiple))
                errors.Add(new("graph.connection.cardinality.invalid", null, connection.ConnectionId));
        }
        return errors;
    }
}
