using System.Text.Json;
using System.Text.Json.Serialization;

namespace Haven.Core.Media;

/// <summary>A persisted reference to existing canonical media and retained Files content.
/// Integrity hints never establish ownership; no path, URL or operation lease is persisted.</summary>
public sealed record MediaAssetReference(
    [property: JsonConverter(typeof(MediaReferenceAssetIdConverter))] MediaAssetId AssetID,
    Guid FileID, string RevisionID, string? ExpectedSHA256 = null, long? ExpectedSizeBytes = null)
{
    public void Validate()
    {
        if (AssetID.IsEmpty || FileID == Guid.Empty || string.IsNullOrWhiteSpace(RevisionID)
            || RevisionID.Length > 256 || RevisionID.Any(char.IsControl))
            throw new InvalidDataException("Media requires an existing asset, Files identity and exact retained revision.");
        if (ExpectedSHA256 is { } hash && (hash.Length != 64 || hash.Any(character => !char.IsAsciiHexDigit(character)))
            || ExpectedSizeBytes is < 0)
            throw new InvalidDataException("Invalid expected media integrity.");
    }

    public bool MatchesIdentity(MediaAssetSource source) => source.AssetId == AssetID && source.HostedItemId == FileID
        && string.Equals(source.SourceRevisionId, RevisionID, StringComparison.Ordinal);
}

public sealed class MediaReferenceAssetIdConverter : JsonConverter<MediaAssetId>
{
    public override MediaAssetId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.TokenType == JsonTokenType.String && reader.TryGetGuid(out var id)
            ? new(id) : throw new JsonException("Media AssetID must be a UUID.");
    public override void Write(Utf8JsonWriter writer, MediaAssetId value, JsonSerializerOptions options) => writer.WriteStringValue(value.Value);
}

/// <summary>The actual Files owner resolves an exact retained revision under current ownership and
/// resource authority. Current-only resolution must not silently substitute newer content.</summary>
public interface IMediaRetainedAssetSourceResolver : IMediaAssetSourceResolver
{
    Task<MediaEngineResult<MediaAssetReadLease>> ResolveRetainedAsync(string fileID, MediaAssetId assetID,
        string expectedRevision, CancellationToken cancellationToken = default);
}
