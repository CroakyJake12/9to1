using System.Text.Json;
using System.Text.Json.Serialization;
using Haven.Core;

namespace Haven.Application.Automations;

/// <summary>Descriptive reference to Home's canonical graph, not a second graph,
/// a current revision attestation, owner admission, activation or run grant.</summary>
public sealed record CanonicalAutomationGraphReference(int Version, Guid GraphId, long DraftRevision, long? ActiveRevision)
{
    [JsonIgnore]
    public AutomationDefinitionGraphBinding Binding => new(GraphId, DraftRevision, ActiveRevision);
}
public static class CanonicalAutomationGraphReferenceCodec
{
    private const string Prefix = "haven:canonical-graph:v1:";
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
    public static bool IsReference(string? instruction) => instruction?.StartsWith("haven:canonical-graph:", StringComparison.Ordinal) == true;
    public static string Serialize(CanonicalAutomationGraphReference reference)
    {
        ArgumentNullException.ThrowIfNull(reference);
        if (!Valid(reference)) throw new ArgumentException("A valid canonical graph reference is required.");
        return Prefix + JsonSerializer.Serialize(reference, Options);
    }
    public static bool TryDeserialize(string? instruction, out CanonicalAutomationGraphReference reference)
    {
        reference = new(1, Guid.Empty, 0, null);
        if (instruction?.StartsWith(Prefix, StringComparison.Ordinal) != true || instruction.Length > 4096) return false;
        try
        {
            var actual = JsonSerializer.Deserialize<CanonicalAutomationGraphReference>(instruction[Prefix.Length..], Options);
            if (actual is null || !Valid(actual)) return false;
            reference = actual; return true;
        }
        catch (JsonException) { return false; }
    }
    private static bool Valid(CanonicalAutomationGraphReference value) => value.Version == 1 && value.GraphId != Guid.Empty &&
        value.DraftRevision >= 1 && (value.ActiveRevision is null || value.ActiveRevision >= 1 && value.ActiveRevision <= value.DraftRevision);
}
