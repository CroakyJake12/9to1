using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Haven.Application.NodeGraph;

/// <summary>Strictly versioned, bounded codec for the detached continuation envelope. It preserves the distinction
/// between false, present null, absent, and redacted/unavailable/error representations rather than coercing them.
/// Corruption, duplicate/unknown fields, unknown enums, invalid dates, malformed typed state and oversized payloads
/// fail closed with structured errors. The envelope remains descriptive state — deserializing it never grants
/// permission or authorizes execution.</summary>
public sealed class GraphRuntimeContinuationCodec
{
    /// <summary>Bounded envelope size (1 MiB). Payloads strictly larger than this are rejected before any adapter effect.</summary>
    public const int MaximumSerializedBytes = 1 << 20;

    private static readonly HashSet<string> AllowedKeys = new(StringComparer.Ordinal)
    {
        "protocolVersion", "graphType", "ownerRunId", "graphId", "revision", "capabilityProfileId", "schemaVersion",
        "startNodeId", "eventType", "sessionId", "entityId", "participantId", "kind", "nodeId", "resumeAt",
        "condition", "ownerState", "stepsConsumed", "stepLimit", "digest",
    };

    public string Serialize(GraphRuntimeContinuation continuation)
    {
        ArgumentNullException.ThrowIfNull(continuation);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteNumber("protocolVersion", continuation.ProtocolVersion);
            writer.WriteString("graphType", continuation.GraphType);
            writer.WriteString("ownerRunId", continuation.OwnerRunId);
            writer.WriteString("graphId", continuation.GraphId.ToString("D"));
            writer.WriteNumber("revision", continuation.Revision);
            writer.WriteString("capabilityProfileId", continuation.CapabilityProfileId);
            writer.WriteNumber("schemaVersion", continuation.SchemaVersion);
            writer.WriteString("startNodeId", continuation.StartNodeId.ToString("D"));
            writer.WriteString("eventType", continuation.EventType);
            writer.WriteString("sessionId", continuation.SessionId);
            writer.WriteString("entityId", continuation.EntityId);
            writer.WriteString("participantId", continuation.ParticipantId);
            writer.WriteString("kind", continuation.Kind.ToString());
            writer.WriteString("nodeId", continuation.NodeId.ToString("D"));
            if (continuation.ResumeAt is { } at) writer.WriteString("resumeAt", at.ToString("O"));
            WriteElement(writer, "condition", continuation.Condition);
            WriteElement(writer, "ownerState", continuation.OwnerState);
            writer.WriteNumber("stepsConsumed", continuation.StepsConsumed);
            writer.WriteNumber("stepLimit", continuation.StepLimit);
            writer.WriteString("digest", continuation.Digest);
            writer.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    public GraphRuntimeContinuation Deserialize(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        if (Encoding.UTF8.GetByteCount(json) > MaximumSerializedBytes)
            throw new JsonException("Continuation envelope exceeds the maximum encoded size.");
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object) throw new JsonException("Continuation envelope must be a JSON object.");
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in root.EnumerateObject())
        {
            if (!seen.Add(property.Name)) throw new JsonException("Continuation envelope contains duplicate fields.");
            if (!AllowedKeys.Contains(property.Name)) throw new JsonException($"Continuation envelope contains unknown field '{property.Name}'.");
        }
        var protocolVersion = ReadInt(root, "protocolVersion", seen);
        if (protocolVersion != GraphRuntimeContinuation.CurrentProtocolVersion)
            throw new JsonException($"Unsupported continuation protocol version {protocolVersion}.");
        return new GraphRuntimeContinuation(
            protocolVersion,
            ReadString(root, "graphType", seen),
            ReadString(root, "ownerRunId", seen),
            ReadGuid(root, "graphId", seen),
            ReadLong(root, "revision", seen),
            ReadString(root, "capabilityProfileId", seen),
            ReadInt(root, "schemaVersion", seen),
            ReadGuid(root, "startNodeId", seen),
            ReadString(root, "eventType", seen),
            ReadString(root, "sessionId", seen),
            ReadString(root, "entityId", seen),
            ReadString(root, "participantId", seen),
            ReadEnum<GraphContinuationKind>(root, "kind", seen),
            ReadGuid(root, "nodeId", seen),
            ReadDateTime(root, "resumeAt", seen),
            ReadStateElement(root, "condition", seen),
            ReadStateElement(root, "ownerState", seen),
            ReadInt(root, "stepsConsumed", seen),
            ReadInt(root, "stepLimit", seen),
            ReadString(root, "digest", seen));
    }

    public bool TryDeserialize(string json, out GraphRuntimeContinuation? continuation, out string? error)
    {
        try
        {
            continuation = Deserialize(json);
            error = null;
            return true;
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException or FormatException or OverflowException)
        {
            continuation = null;
            error = exception.Message;
            return false;
        }
    }

    private static void WriteElement(Utf8JsonWriter writer, string name, JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Undefined) return; // absent stays absent; null/false/object preserved
        writer.WritePropertyName(name);
        element.WriteTo(writer);
    }

    private static string ReadString(JsonElement root, string name, HashSet<string> seen) =>
        root.TryGetProperty(name, out var element) && element.ValueKind == JsonValueKind.String ? element.GetString()!
            : throw new JsonException($"Continuation envelope is missing or invalid '{name}'.");

    private static int ReadInt(JsonElement root, string name, HashSet<string> seen) =>
        root.TryGetProperty(name, out var element) && element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out var value)
            ? value : throw new JsonException($"Continuation envelope is missing or invalid '{name}'.");

    private static long ReadLong(JsonElement root, string name, HashSet<string> seen) =>
        root.TryGetProperty(name, out var element) && element.ValueKind == JsonValueKind.Number && element.TryGetInt64(out var value)
            ? value : throw new JsonException($"Continuation envelope is missing or invalid '{name}'.");

    private static Guid ReadGuid(JsonElement root, string name, HashSet<string> seen) =>
        Guid.TryParse(ReadString(root, name, seen), out var guid) ? guid
            : throw new JsonException($"Continuation envelope has an invalid '{name}'.");

    private static DateTimeOffset? ReadDateTime(JsonElement root, string name, HashSet<string> seen)
    {
        if (!root.TryGetProperty(name, out var element)) return null; // absent optional ResumeAt is valid
        if (element.ValueKind != JsonValueKind.String)
            throw new JsonException($"Continuation envelope '{name}' must be a JSON string when present.");
        var text = element.GetString()!;
        if (!DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var value))
            throw new JsonException($"Continuation envelope '{name}' is not a valid timestamp.");
        return value;
    }

    private static JsonElement ReadStateElement(JsonElement root, string name, HashSet<string> seen)
    {
        if (!root.TryGetProperty(name, out var element)) return default;
        if (element.ValueKind is not (JsonValueKind.Object or JsonValueKind.Null))
            throw new JsonException($"Continuation envelope '{name}' must be an object, null, or absent.");
        return element.Clone();
    }

    private static TEnum ReadEnum<TEnum>(JsonElement root, string name, HashSet<string> seen) where TEnum : struct, Enum
    {
        var text = ReadString(root, name, seen);
        // The wire contract uses named enums. Reject numeric spellings and undefined values.
        if (long.TryParse(text, out _)) throw new JsonException($"Continuation envelope '{name}' must be a named enum, not numeric.");
        if (!Enum.TryParse<TEnum>(text, ignoreCase: false, out var value) || !Enum.IsDefined(value))
            throw new JsonException($"Continuation envelope has an unsupported '{name}'.");
        return value;
    }
