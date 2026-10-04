using System.Security.Cryptography;
using System.Text.Json;
using Haven.Core;
using Haven.Application.NodeGraph;

namespace Haven.Application.Automations;

/// <summary>Detached publication proposal, not graph authority or permission to run.
/// Actual guarded graph publication and SQL linked-row commit remain separate owner operations.</summary>
public sealed class AutomationGraphPublicationPreparation
{
    private readonly byte[] _snapshot;
    private AutomationGraphPublicationPreparation(byte[] snapshot)
    {
        _snapshot = snapshot;
        Digest = Convert.ToHexString(SHA256.HashData(_snapshot));
    }
    /// <summary>Bounded detached inspection metadata only; not an issuer selection or publication grant.</summary>
    public static AutomationGraphPublicationPreparation FromBoundedSnapshot(ReadOnlySpan<byte> snapshot)
    {
        if (snapshot.Length == 0 || snapshot.Length > 2 * 1024 * 1024)
            throw new InvalidDataException("The publication proposal exceeds its bound.");
        _ = JsonSerializer.Deserialize<GraphPublicationSnapshot>(snapshot)
            ?? throw new InvalidDataException("The publication proposal is unavailable.");
        return new(snapshot.ToArray());
    }
    public string Digest { get; }
    public GraphPublicationSnapshot Read() => JsonSerializer.Deserialize<GraphPublicationSnapshot>(_snapshot)
        ?? throw new InvalidDataException("The captured graph publication proposal is unavailable.");
}

public sealed record GraphPublicationSnapshot(Guid OperationId, ReusableTaskDefinition Reusable,
    AutomationDefinition? LinkedSchedule, GraphDocument Draft, long ExpectedGraphRevision,
    long? OriginalActiveRevision);

