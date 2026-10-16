namespace NineToOne.Dulche.Context;

/// <summary>Portable provenance and requested restrictions, never an access grant.</summary>
public sealed record UcmlDocument(UcmlMetadata Metadata, string Markdown);
public sealed record UcmlScope(string Kind, string Id);
public sealed record UcmlConsent(string Status, IReadOnlyList<string> Apps,
    IReadOnlyList<string> Purposes, IReadOnlyList<string> Recipients);
public sealed record UcmlSource(string Id, string Title, string Kind, string? Uri, DateTimeOffset ObservedAt);
public sealed record UcmlMetadata
{
    public required int SchemaVersion { get; init; }
    public required Guid Id { get; init; }
    public required long Revision { get; init; }
    public required string Title { get; init; }
    public required string OwnerId { get; init; }
    public required UcmlScope Scope { get; init; }
    public required string Category { get; init; }
    public required string Classification { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public required DateTimeOffset UpdatedAt { get; init; }
    public DateTimeOffset? ExpiresAt { get; init; }
    public required string Retention { get; init; }
    public required UcmlConsent Consent { get; init; }
    public required IReadOnlyList<UcmlSource> Sources { get; init; }
    public required double Confidence { get; init; }
    public Guid? SupersedesId { get; init; }
}
