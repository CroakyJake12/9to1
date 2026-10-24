using System.Text.Json.Serialization;

namespace HavenOS.Files;

/// <summary>The trusted Home/account adapter resolves RecipientPrincipalId before calling Files.
/// RecipientEmail is an address for this grant, never a principal, bearer token or proof of identity.
/// This API neither sends mail nor grants Mail.Send. Only Viewer and Editor are delegable.</summary>
public sealed record FilesEmailShareRequest(
    Guid StoreId,
    HostedItemId ItemId,
    FilesRevisionId ExpectedRevision,
    string RecipientPrincipalId,
    string RecipientEmail,
    FilesAccessRole Role,
    DateTimeOffset? ExpiresAt,
    FilesOperationId OperationId);

/// <summary>A canonical grant as it applies to the selected item. Inherited rows retain the source grant
/// and identify its ancestor; they are informational and cannot be changed from the child item.</summary>
public sealed record FilesEmailShare(FilesShare Share, string RecipientEmail, string GrantedByPrincipalId)
{
    [JsonIgnore] public FilesGrantOrigin Origin { get; init; } = FilesGrantOrigin.Direct;
    [JsonIgnore] public HostedItemId? InheritedFrom { get; init; }
}

/// <summary>Exact request/result custody for idempotent permission mutations in the owning Files state.</summary>
public sealed record FilesShareJournalEntry(FilesOperationId OperationId, string ActorId, string Action,
    FilesShareId ShareId, FilesEmailShareRequest? GrantRequest, FilesRevisionId ExpectedRevision,
    FilesEmailShare Result);
