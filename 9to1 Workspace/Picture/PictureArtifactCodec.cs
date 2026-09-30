using System.Text.Json;
using System.Text.Json.Serialization;

namespace HavenOS.Images;

/// <summary>Canonical raw source asset reference, separate from the editable Picture artifact's backing FileID.</summary>
public sealed record PictureSourceAssetReference(
    [property: JsonRequired] Guid FileId,
    [property: JsonRequired] Guid RevisionId,
    [property: JsonRequired] string ContentHash,
    [property: JsonRequired] long SizeBytes,
    [property: JsonRequired] Guid AssetId);

public sealed record PictureArtifactEnvelope
{
    public const string FormatId = "9to1.Picture";
    public const int CurrentSchemaVersion = 1;
    [JsonRequired] public string Format { get; init; } = FormatId;
    [JsonRequired] public int SchemaVersion { get; init; } = CurrentSchemaVersion;
    [JsonRequired] public Guid BackingFileId { get; init; }
    [JsonRequired] public required PictureDocument Document { get; init; }
    public PictureSourceAssetReference? SourceAsset { get; init; }
}

/// <summary>Portable editable envelope; it never embeds a machine path or silently turns a source asset into its backing artifact.</summary>
public static class PictureArtifactCodec
{
    public const int MaximumPayloadBytes = 256 * 1024 * 1024;
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    public static byte[] Serialize(PictureArtifactEnvelope envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        envelope = Validate(envelope);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(envelope, Options);
        if (bytes.Length > MaximumPayloadBytes) throw new InvalidDataException("The editable Picture artifact exceeds supported payload limits.");
        return bytes;
    }

    public static PictureArtifactEnvelope Deserialize(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length is 0 or > MaximumPayloadBytes) throw new InvalidDataException("The editable Picture artifact is outside supported payload limits.");
        var envelope = JsonSerializer.Deserialize<PictureArtifactEnvelope>(bytes, Options)
            ?? throw new InvalidDataException("The editable Picture artifact is empty.");
        return Validate(envelope);
    }

    private static PictureArtifactEnvelope Validate(PictureArtifactEnvelope envelope)
    {
        if (envelope.Format != PictureArtifactEnvelope.FormatId || envelope.SchemaVersion != PictureArtifactEnvelope.CurrentSchemaVersion)
            throw new NotSupportedException("The editable Picture artifact requires an explicit format/schema migration.");
        if (envelope.BackingFileId == Guid.Empty || envelope.Document is null)
            throw new InvalidDataException("The editable Picture artifact has no canonical backing identity or document.");
        // Validate and own the graph that will be emitted, rather than checking
        // one enumeration then serializing the caller's mutable graph again.
        envelope = envelope with { Document = PictureDocument.Deserialize(envelope.Document.Serialize()) };
        if (envelope.Document.SourcePath is not null)
            throw new InvalidDataException("Canonical Picture state cannot contain a legacy materialized machine path.");
        var source = envelope.SourceAsset;
        if (source is null)
        {
            if (envelope.Document.FileId is not null || envelope.Document.SourceRevision is not null)
                throw new InvalidDataException("A linked Picture source requires an exact retained Files asset revision.");
            return envelope;
        }
        if (source.AssetId == Guid.Empty || source.FileId == Guid.Empty || source.RevisionId == Guid.Empty || source.FileId == envelope.BackingFileId ||
            source.SizeBytes <= 0 || source.ContentHash is null || source.ContentHash.Length != 64 || !source.ContentHash.All(Uri.IsHexDigit) ||
            !Guid.TryParse(envelope.Document.FileId, out var documentSourceId) || documentSourceId != source.FileId ||
            !Guid.TryParse(envelope.Document.SourceRevision, out var documentSourceRevision) || documentSourceRevision != source.RevisionId)
            throw new InvalidDataException("Picture source identity, revision or integrity differs from the linked canonical Files asset.");
        return envelope;
    }
}
