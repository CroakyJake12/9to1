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

/// <summary>Authoritative, owner-backed continuation binding. This is the ONLY descriptor the registry consumes for
/// resume: the registered owning adapter must reload/claim/validate the persisted Run and the current caller before
/// any resumed effects, then return/refuse this detached binding. A deserialized envelope, a public checksum/MAC
/// string, or a public DTO/bool is never authority. SQL and actual approval issuance are lane6/Home integration;
/// this contract lets those owners supply real checks without a private competing runtime.</summary>
public sealed record GraphContinuationAdmission(
    string OwnerRunId,
    GraphContinuationKind Kind,
    Guid NodeId,
    DateTimeOffset? ResumeAt,
    JsonElement Condition,
    JsonElement OwnerState,
    int AuthorizedStepLimit,
    int StepsConsumedBeforeResume);

/// <summary>Deterministic SHA-256 over a CANONICAL JSON encoding of the full resumed binding: graph/revision/profile/
/// schema snapshot, pinned variables, and every continuation field (owner run, kind, node, resume-at, condition,
/// owner state, consumed/limit). JSON with fixed, distinct keys gives an unambiguous tuple encoding (no delimiter
/// concatenation of user-owned strings). Digest comparison is integrity evidence only, never permission.</summary>
public static class GraphContinuationDigest
{
    public static string Compute(GraphDocument graph, JsonElement variables, GraphContinuationAdmission binding)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(binding);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteNumber("schemaVersion", graph.SchemaVersion);
            writer.WriteString("graphId", graph.GraphId.ToString("D"));
            writer.WriteNumber("revision", graph.Revision);
            writer.WriteString("capabilityProfileId", graph.CapabilityProfileId);
            writer.WriteNumber("state", (int)graph.State);
            writer.WritePropertyName("nodes");
            writer.WriteStartArray();
            foreach (var node in graph.Nodes.OrderBy(node => node.NodeId))
            {
                writer.WriteStartObject();
                writer.WriteString("nodeId", node.NodeId.ToString("D"));
                writer.WriteString("typeId", node.TypeId);
                writer.WriteNumber("typeVersion", node.TypeVersion);
                writer.WriteString("appId", node.AppId);
                writer.WriteString("capabilityId", node.CapabilityId);
                if (node.EntityId is { } entity) writer.WriteString("entityId", entity);
                writer.WritePropertyName("configuration");
                node.Configuration.WriteTo(writer);
                writer.WritePropertyName("ports");
                writer.WriteStartArray();
                foreach (var port in node.Ports.OrderBy(port => port.PortId))
                {
                    writer.WriteStartObject();
                    writer.WriteString("portId", port.PortId.ToString("D"));
                    writer.WriteString("key", port.Key);
                    writer.WriteNumber("direction", (int)port.Direction);
                    writer.WriteString("dataType", port.DataType);
                    writer.WriteBoolean("multiple", port.Multiple);
                    writer.WriteEndObject();
                }
                writer.WriteEndArray();
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WritePropertyName("connections");
            writer.WriteStartArray();
            foreach (var connection in graph.Connections.OrderBy(connection => connection.ConnectionId))
            {
                writer.WriteStartObject();
                writer.WriteString("connectionId", connection.ConnectionId.ToString("D"));
                writer.WriteString("outputPortId", connection.OutputPortId.ToString("D"));
                writer.WriteString("inputPortId", connection.InputPortId.ToString("D"));
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WritePropertyName("variables");
            variables.WriteTo(writer);
            writer.WriteString("ownerRunId", binding.OwnerRunId);
            writer.WriteString("kind", binding.Kind.ToString());
            writer.WriteString("nodeId", binding.NodeId.ToString("D"));
            if (binding.ResumeAt is { } at) writer.WriteString("resumeAt", at.ToString("O"));
            WriteElement(writer, "condition", binding.Condition);
            WriteElement(writer, "ownerState", binding.OwnerState);
            writer.WriteNumber("stepsConsumed", binding.StepsConsumedBeforeResume);
            writer.WriteNumber("stepLimit", binding.AuthorizedStepLimit);
            writer.WriteEndObject();
        }
        return Convert.ToHexString(SHA256.HashData(stream.ToArray())).ToLowerInvariant();
    }

    private static void WriteElement(Utf8JsonWriter writer, string name, JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Undefined) return; // absent vs present null distinguished
        writer.WritePropertyName(name);
        element.WriteTo(writer);
    }
}
