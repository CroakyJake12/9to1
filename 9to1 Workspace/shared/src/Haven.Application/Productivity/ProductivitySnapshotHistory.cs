using System.Security.Cryptography;
using System.Text.Json.Serialization;

namespace Haven.Application;

/// <summary>App-neutral durable semantic history data. Hashes detect corrupt/mismatched data;
/// they authenticate no actor and grant no access. The owning app validates every payload
/// and publishes the complete document/history together through its normal guarded store.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ProductivitySnapshotHistory
{
    public const int CurrentSchemaVersion = 1;
    public const int MaximumEntries = 128;
    public const long MaximumPayloadBytes = 256L * 1024 * 1024;
    [JsonRequired] public int SchemaVersion { get; init; } = CurrentSchemaVersion;
    [JsonRequired] public required string OwnerFormat { get; init; }
    [JsonRequired] public Guid ArtifactId { get; init; }
    /// <summary>Exact owning revision representation (GUID or sequence), never a hash-derived substitute.</summary>
    [JsonRequired] public required string CurrentRevision { get; init; }
    [JsonRequired] public required string CurrentSnapshotHash { get; init; }
    [JsonRequired] public IReadOnlyList<ProductivityHistorySnapshot> Undo { get; init; } = [];
    [JsonRequired] public IReadOnlyList<ProductivityHistorySnapshot> Redo { get; init; } = [];
    /// <summary>Lower bound of discarded earlier entries, saturating at Int64.MaxValue.
    /// Distinct from an empty original history; it is not a complete action count.</summary>
    [JsonRequired] public long DiscardedEarlierEntries { get; init; }
    /// <summary>Actual source-issued operation metadata where retained. It is
    /// descriptive provenance, not caller authentication or an access grant.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DocumentOperationMetadata? LastOperation { get; init; }

    public static ProductivityHistorySnapshot Capture(ReadOnlySpan<byte> payload, string revision)
    {
        if (string.IsNullOrWhiteSpace(revision) || revision.Length > 128 || payload.IsEmpty || payload.Length > MaximumPayloadBytes)
            throw new InvalidDataException("History snapshot identity or size is unsupported.");
        return new(revision, Convert.ToHexString(SHA256.HashData(payload)), Convert.ToBase64String(payload));
    }

    public void Validate(string ownerFormat, Guid artifactId, string revision, ReadOnlySpan<byte> currentSnapshot,
        Func<ReadOnlyMemory<byte>, (Guid ArtifactId, string Revision)> validateOwnerSnapshot)
    {
        ArgumentNullException.ThrowIfNull(validateOwnerSnapshot);
        if (SchemaVersion != CurrentSchemaVersion || string.IsNullOrWhiteSpace(ownerFormat) || ownerFormat.Length > 128 || currentSnapshot.IsEmpty || OwnerFormat != ownerFormat || ArtifactId != artifactId ||
            ArtifactId == Guid.Empty || CurrentRevision != revision || string.IsNullOrWhiteSpace(revision) || revision.Length > 128 ||
            CurrentSnapshotHash != Convert.ToHexString(SHA256.HashData(currentSnapshot)) ||
            DiscardedEarlierEntries < 0 || Undo is null || Redo is null || Undo.Count + (long)Redo.Count > MaximumEntries)
            throw new InvalidDataException("History schema, current document binding, or retention state is invalid.");
        ValidateMetadata(LastOperation);
        var operationIds = new HashSet<Guid>();
        long total = 0;
        foreach (var frame in Undo.Concat(Redo))
        {
            if (frame is null || string.IsNullOrWhiteSpace(frame.Revision) || frame.Revision.Length > 128 || frame.PayloadBase64 is null ||
                frame.PayloadBase64.Length > (MaximumPayloadBytes + 2) / 3 * 4)
                throw new InvalidDataException("History frame is invalid or exceeds supported limits.");
            ValidateMetadata(frame.Operation);
            if (frame.Operation is { } operation && !operationIds.Add(operation.Id))
                throw new InvalidDataException("History operation identities must be distinct across retained entries.");
            byte[] bytes;
            try { bytes = Convert.FromBase64String(frame.PayloadBase64); }
            catch (FormatException exception) { throw new InvalidDataException("History snapshot encoding is invalid.", exception); }
            total = checked(total + bytes.Length);
            if (bytes.Length == 0 || total > MaximumPayloadBytes ||
                frame.ContentHash != Convert.ToHexString(SHA256.HashData(bytes)))
                throw new InvalidDataException("History payload size or digest is invalid.");
            var identity = validateOwnerSnapshot(bytes);
            if (identity.ArtifactId != ArtifactId || identity.Revision != frame.Revision)
                throw new InvalidDataException("History snapshot belongs to another artifact or revision.");
        }
    }

    private static void ValidateMetadata(DocumentOperationMetadata? metadata)
    {
        if (metadata is not null && (metadata.Id == Guid.Empty || string.IsNullOrWhiteSpace(metadata.Name) ||
            metadata.Name.Length > 4096 || !Enum.IsDefined(metadata.Origin) || metadata.Actor?.Length > 4096))
            throw new InvalidDataException("Retained history operation metadata is invalid.");
    }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ProductivityHistorySnapshot(
    [property: JsonRequired] string Revision,
    [property: JsonRequired] string ContentHash,
    [property: JsonRequired] string PayloadBase64)
{
    /// <summary>Metadata of the original entry represented by this undo/redo
    /// frame. Absent for older snapshots; never reconstructed as an original.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DocumentOperationMetadata? Operation { get; init; }
}
