using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Haven.Application.Automations;

/// <summary>Authenticated pinned execution snapshot captured at suspension time. It retains the EXACT
/// graph that was executed for this Run (never the current Active payload), the canonical identity
/// (GraphId / pinned definition revision / schema / source digest), the trigger node and a self
/// integrity digest over the stored graph bytes. A resume reloads exactly this snapshot, revalidates it
/// against the Run's pin and recomputes/checks the source digest, schema and projection before executing;
/// the current task payload is never substituted under an old label.</summary>
public sealed record AutomationPinnedExecutionSnapshot(
    Guid GraphId,
    long Revision,
    int SchemaVersion,
    string SourceDigest,
    Guid TriggerNodeId,
    string? CapabilityProfileId,
    string GraphJson,
    string GraphDigest)
{
    /// <summary>Deterministic SHA-256 over the exact graph bytes. It is a tamper/consistency check, not
    /// an authentication grant — live authority is always revalidated on resume.</summary>
    public static string ComputeDigest(string graphJson) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(graphJson))).ToLowerInvariant();

    /// <summary>True when the stored graph bytes still match the recorded self-integrity digest.</summary>
    public bool VerifyIntegrity() =>
        !string.IsNullOrWhiteSpace(GraphJson) && string.Equals(GraphDigest, ComputeDigest(GraphJson), StringComparison.Ordinal);
}

/// <summary>Strict, versioned codec for <see cref="AutomationPinnedExecutionSnapshot"/>. Deserialization
/// verifies structural invariants and round-trip determinism and recomputes the self-integrity digest, so
/// a corrupt/oversized/tampered snapshot is refused before any side effect.</summary>
public static class AutomationPinnedExecutionSnapshotCodec
{
    private const string Prefix = "haven:automation-pinned-execution-snapshot:v1:";
    private const int MaximumSerializedCharacters = 262_144;
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static string Serialize(AutomationPinnedExecutionSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (snapshot.GraphId == Guid.Empty || snapshot.Revision < 1 || snapshot.SchemaVersion < 1 ||
            string.IsNullOrWhiteSpace(snapshot.GraphJson) || string.IsNullOrWhiteSpace(snapshot.SourceDigest) ||
            string.IsNullOrWhiteSpace(snapshot.GraphDigest) || snapshot.TriggerNodeId == Guid.Empty)
            throw new InvalidOperationException("A pinned execution snapshot must carry full identity and graph content.");
        var json = JsonSerializer.Serialize(snapshot, Options);
        if (json.Length > MaximumSerializedCharacters) throw new InvalidOperationException("Pinned execution snapshot exceeds the maximum serialized size.");
        return Prefix + json;
    }

    public static bool TryDeserialize(string? value, out AutomationPinnedExecutionSnapshot snapshot)
    {
        snapshot = null!;
        if (string.IsNullOrWhiteSpace(value) || !value.StartsWith(Prefix, StringComparison.Ordinal)) return false;
        var payload = value![Prefix.Length..];
        if (payload.Length > MaximumSerializedCharacters) return false;
        try
        {
            var parsed = JsonSerializer.Deserialize<AutomationPinnedExecutionSnapshot>(payload, Options);
            if (parsed is null || parsed.GraphId == Guid.Empty || parsed.Revision < 1 || parsed.SchemaVersion < 1 ||
                string.IsNullOrWhiteSpace(parsed.GraphJson) || string.IsNullOrWhiteSpace(parsed.SourceDigest) ||
                string.IsNullOrWhiteSpace(parsed.GraphDigest) || parsed.TriggerNodeId == Guid.Empty)
                return false;
            if (!string.Equals(JsonSerializer.Serialize(parsed, Options), payload, StringComparison.Ordinal)) return false;
            if (!parsed.VerifyIntegrity()) return false;
            snapshot = parsed;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }
}
