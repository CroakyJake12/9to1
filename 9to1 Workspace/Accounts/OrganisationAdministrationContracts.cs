namespace NineToOne.Accounts;

/// <summary>A detached, permission-filtered observation of one current canonical
/// organisation collection. The public cursor supplies pagination position only;
/// every later read independently checks the current actor and policy.</summary>
public sealed record OrganisationCollectionPage<T>(Guid OrgID, long OrganisationRevision,
    IReadOnlyList<T> Items, int TotalCount, string? NextCursor, DateTimeOffset ObservedAt);
public enum OrganisationInvitationStatus { Pending, Accepted, Revoked, Expired, InvalidIdentity, InvalidRole }
public sealed record OrganisationInvitationSummary(Guid InvitationID, Guid InviterID, Guid? IntendedAccountID,
    IReadOnlyList<Guid> RoleIDs, DateTimeOffset ExpiresAt, OrganisationInvitationStatus Status,
    DateTimeOffset? RevokedAt, int SchemaVersion);
public sealed record OrganisationInvitationPage(Guid OrgID, long OrganisationRevision,
    long InvitationCollectionRevision, IReadOnlyList<OrganisationInvitationSummary> Items,
    int TotalCount, string? NextCursor, DateTimeOffset ObservedAt,string? SearchQuery=null,
    Guid? IntendedAccountFilter=null,OrganisationInvitationStatus? StatusFilter=null);
