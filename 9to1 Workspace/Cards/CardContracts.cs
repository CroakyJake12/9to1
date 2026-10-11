using System.Text.Json;
using System.Text.Json.Serialization;

namespace HavenOS.Apps.Cards;

// This package owns card identity and mutation semantics only.
// Authentication, Home permissions, the Shared Productivity Engine, Files persistence,
// and Study evidence submission must be supplied by their existing canonical owners.
public enum CardInteractionMode { View, Edit }
public enum CardReviewRating { Red, Amber, Green }
public enum CardGroupingKind { Set, Subject, Topic, Custom }
public enum CardNavigationDirection { Vertical, Horizontal }

public enum CardFailureCode
{
    InvalidState,
    InvalidContent,
    CardNotFound,
    CardAlreadyDeleted,
    RevisionConflict,
    ViewIsReadOnly,
    InvalidOrdering,
    DuplicateCard,
    UnsupportedSchema,
    InvalidPage,
    DuplicateAsset,
}

public sealed class CardOperationException : Exception
{
    public CardOperationException(CardFailureCode code, string message) : base(message) => Code = code;
    public CardFailureCode Code { get; }
}

// Structured shared-engine document payloads MUST NOT be flattened into Markdown/text.
// SearchText is only a derived hint; Document remains the authoritative rich content.
// Unknown metadata is retained for lossless JSON-level forwards-compatible round trips.
public sealed record CardSide
{
    public required JsonElement Document { get; init; }
    public required string DocumentFormat { get; init; }
    public string SearchText { get; init; } = string.Empty;
    public string? FontFamily { get; init; }
    public string? BackgroundColor { get; init; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extensions { get; init; }

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(DocumentFormat) || Document.ValueKind != JsonValueKind.Object)
            throw new CardOperationException(CardFailureCode.InvalidContent, "A card side requires an identified structured document object.");
    }
}

public sealed record CardEntry
{
    public required Guid CardId { get; init; }
    public required CardSide Front { get; init; }
    public required CardSide Back { get; init; }
    public int Revision { get; init; } = 1;
    public string? SubjectId { get; init; }
    public string? TopicId { get; init; }
    public IReadOnlyDictionary<string, string>? CustomGroups { get; init; }
    public bool IsDeleted { get; init; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extensions { get; init; }
}

// The Files owner resolves and verifies these references; registering one
// never uploads bytes, grants permissions or implies an accessible asset.
public sealed record CardAssetReference
{
    public required Guid AssetId { get; init; }
    public required string CanonicalFileId { get; init; }
    public required string MediaType { get; init; }
    public required string Sha256 { get; init; }
    public long ByteLength { get; init; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extensions { get; init; }

    public void Validate()
    {
        if (AssetId == Guid.Empty || string.IsNullOrWhiteSpace(CanonicalFileId)
            || string.IsNullOrWhiteSpace(MediaType) || ByteLength < 0
            || string.IsNullOrWhiteSpace(Sha256) || Sha256.Length != 64 || !Sha256.All(Uri.IsHexDigit))
            throw new CardOperationException(CardFailureCode.InvalidContent,
                "An asset requires a nonempty identity, canonical Files reference, MIME type, size and SHA-256.");
    }
}

public sealed record CardSet
{
    public int SchemaVersion { get; init; } = 1;
    public required Guid SetId { get; init; }
    public required Guid ArtifactId { get; init; }
    public required string OwnerScope { get; init; }
    public required string Title { get; init; }
    public long Revision { get; init; } = 1;
    // Array order is the membership order. Tombstones keep stable IDs for recovery.
    public IReadOnlyList<CardEntry> Cards { get; init; } = [];
    public IReadOnlyList<CardAssetReference> Assets { get; init; } = [];

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extensions { get; init; }
}

// A learner's review is separate from shared deck data and MUST be saved through
// an authenticated principal-specific owner, not serialised inside CardSet.
public sealed record CardReviewRecord(
    string PrincipalId,
    Guid CardId,
    Guid SetId,
    string? TopicId,
    CardReviewRating Rating,
    string EvidenceType,
    DateTimeOffset ReviewedAt);

// A new card must arrive from the canonical shared rich-content editor.
// No empty placeholder, Markdown flattening or fabricated media objects.
public sealed record CardDraft(CardSide Front, CardSide Back);

// One atomic replacement of any selected side(s), checked against the specific
// Card revision as well as the enclosing Set revision. Null means "unchanged".
public sealed record CardSideEdit(
    Guid CardId,
    int ExpectedCardRevision,
    CardSide? Front = null,
    CardSide? Back = null);

// A bounded viewport over the canonical ordered membership. TotalCount is
// deliberately not fabricated by an unbounded count/query.
public sealed record CardPage(
    Guid SetId,
    long SetRevision,
    int Offset,
    IReadOnlyList<CardEntry> Cards,
    bool HasMore);

public sealed record CardDeletePreview(long ExpectedSetRevision, IReadOnlyList<Guid> CardIds)
{
    public int Count => CardIds.Count;
}

public sealed record CardViewPreferences(
    CardNavigationDirection Navigation = CardNavigationDirection.Vertical,
    CardGroupingKind Grouping = CardGroupingKind.Set,
    string? CustomGroupKey = null);
