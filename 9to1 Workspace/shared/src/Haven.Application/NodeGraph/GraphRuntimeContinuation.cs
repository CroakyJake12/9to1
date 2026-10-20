using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Haven.Application.NodeGraph;

/// <summary>Detached, strictly versioned continuation envelope. It is descriptive state carried between yields —
/// never permission, a Home lease, an approved workflow decision, or a current actor receipt. The registry builds
/// it from an admitted graph/revision/profile/schema snapshot and the consumed budget; the owning engine supplies
/// only the detached owner payload (run identity, kind, node, resume metadata, frontier state). It carries generic,
/// bounded data needed by an owning engine and defines no parallel executable graph and no Automations table
/// semantics. JsonElement/collections are cloned at capture and publication.</summary>
public sealed record GraphRuntimeContinuation(
    int ProtocolVersion,
    string GraphType,
    string OwnerRunId,
    Guid GraphId,
    long Revision,
    string CapabilityProfileId,
    int SchemaVersion,
    Guid StartNodeId,
    string EventType,
    string SessionId,
    string EntityId,
    string ParticipantId,
    GraphContinuationKind Kind,
    Guid NodeId,
    DateTimeOffset? ResumeAt,
    JsonElement Condition,
    JsonElement OwnerState,
    int StepsConsumed,
    int StepLimit,
    string Digest)
{
    /// <summary>Envelope protocol version. Deserialization and resume reject any other version before effects.</summary>
    public const int CurrentProtocolVersion = 1;
}

/// <summary>Deterministic SHA-256 digest over the canonical graph/revision/profile/schema snapshot and the pinned
/// variables. Used to detect stale/tampered envelopes before any resumed callback reaches an owning adapter. It is
/// not a permission or an authority grant — it only authenticates that the resumed binding matches the admitted state.</summary>
public static class GraphContinuationDigest
{
    public static string Compute(GraphDocument graph, JsonElement variables)
    {
        ArgumentNullException.ThrowIfNull(graph);
        var sb = new StringBuilder();
        sb.Append(graph.SchemaVersion).Append('|');
        sb.Append(graph.GraphId.ToString("D")).Append('|');
        sb.Append(graph.Revision).Append('|');
        sb.Append(graph.CapabilityProfileId).Append('|');
        sb.Append((int)graph.State).Append('|');
        foreach (var node in graph.Nodes.OrderBy(node => node.NodeId))
        {
            sb.Append(node.NodeId.ToString("D")).Append('|');
            sb.Append(node.TypeId).Append('|');
            sb.Append(node.TypeVersion).Append('|');
            sb.Append(node.AppId).Append('|');
            sb.Append(node.CapabilityId).Append('|');
            sb.Append(node.EntityId).Append('|');
            sb.Append(node.Configuration.GetRawText()).Append('|');
            foreach (var port in node.Ports.OrderBy(port => port.PortId))
            {
                sb.Append(port.PortId.ToString("D")).Append('|');
                sb.Append(port.Key).Append('|');
                sb.Append((int)port.Direction).Append('|');
                sb.Append(port.DataType).Append('|');
                sb.Append(port.Multiple).Append('|');
            }
            sb.Append(';');
        }
        sb.Append('@');
        foreach (var connection in graph.Connections.OrderBy(connection => connection.ConnectionId))
        {
            sb.Append(connection.ConnectionId.ToString("D")).Append('|');
            sb.Append(connection.OutputPortId.ToString("D")).Append('|');
            sb.Append(connection.InputPortId.ToString("D")).Append(';');
        }
        sb.Append('#');
        sb.Append(variables.GetRawText());
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()))).ToLowerInvariant();
    }
}
