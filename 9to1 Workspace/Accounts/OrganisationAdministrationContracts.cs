namespace NineToOne.Accounts;

/// <summary>A detached, permission-filtered observation of one current canonical
/// organisation collection. The public cursor supplies pagination position only;
/// every later read independently checks the current actor and policy.</summary>
public sealed record OrganisationCollectionPage<T>(Guid OrgID, long OrganisationRevision,
    IReadOnlyList<T> Items, int TotalCount, string? NextCursor, DateTimeOffset ObservedAt);
