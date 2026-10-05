using System.Collections.Frozen;
using System.Text.Json;

namespace NineToOne.Accounts;

public sealed partial class OrganisationService
{
    /// <summary>The registered transport supplies the current authenticated account.
    /// Membership alone is insufficient to list other members. Invitation reservations
    /// are separate canonical records and are not counted as active membership here.</summary>
    public OrganisationCollectionPage<OrganisationMembership> ListMembers(Guid authenticatedAccountID,
        Guid organisationID, string? cursor = null, int limit = 50,
        CancellationToken cancellationToken = default) => ReadAdministrationPage(
            authenticatedAccountID, organisationID, "Admin.Members.List", cursor, limit,
            org => org.Members.OrderBy(item => item.MembershipID)
                .Select(item => item with { RoleIDs = Array.AsReadOnly(item.RoleIDs.ToArray()) }).ToArray(),
            cancellationToken);

    public OrganisationCollectionPage<OrganisationRole> ListRoles(Guid authenticatedAccountID,
        Guid organisationID, string? cursor = null, int limit = 50,
        CancellationToken cancellationToken = default) => ReadAdministrationPage(
            authenticatedAccountID, organisationID, "Admin.Roles.List", cursor, limit,
            org => org.Roles.OrderBy(item => item.RoleID).Select(item => item with
            {
                Grants = item.Grants.ToFrozenSet(StringComparer.Ordinal),
                Denials = item.Denials.ToFrozenSet(StringComparer.Ordinal)
            }).ToArray(), cancellationToken);

    private OrganisationCollectionPage<T> ReadAdministrationPage<T>(Guid accountID, Guid orgID,
        string action, string? cursor, int limit, Func<Organisation, T[]> capture,
        CancellationToken cancellationToken)
    {
        if (limit is < 1 or > 200) throw new ArgumentOutOfRangeException(nameof(limit));
        cancellationToken.ThrowIfCancellationRequested();
        using var lease = DurableState.Acquire(statePath);
        cancellationToken.ThrowIfCancellationRequested();
        var organisation = Read().Organisations.SingleOrDefault(item => item.OrgID == orgID);
        // Unknown organisations and inaccessible organisations disclose no collection.
        if (organisation is null || accountID == Guid.Empty)
            throw new OrganisationAccessException("PermissionDenied");
        Demand(organisation, accountID, action);
        var offset = cursor is null ? 0 : ReadCollectionCursor(cursor, accountID, organisation, action, limit);
        var snapshot = capture(organisation);
        if (offset > snapshot.Length) throw new ArgumentException("invalid_collection_cursor");
        var items = snapshot.Skip(offset).Take(limit).ToArray();
        var nextOffset = checked(offset + items.Length);
        var next = nextOffset < snapshot.Length
            ? WriteCollectionCursor(accountID, organisation, action, nextOffset, limit) : null;
        cancellationToken.ThrowIfCancellationRequested();
        return new(orgID, organisation.Revision, Array.AsReadOnly(items), snapshot.Length, next, LifecycleNow);
    }

    private sealed record CollectionCursor(int SchemaVersion, Guid OrgID, Guid AccountID,
        long OrganisationRevision, string Action, int Offset, int Limit);

    private static string WriteCollectionCursor(Guid accountID, Organisation org, string action, int offset, int limit) =>
        Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(
            new CollectionCursor(1, org.OrgID, accountID, org.Revision, action, offset, limit)))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static int ReadCollectionCursor(string value, Guid accountID, Organisation org, string action, int limit)
    {
        if (value.Length is < 1 or > 512 || value.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('-' or '_')))
            throw new ArgumentException("invalid_collection_cursor");
        CollectionCursor decoded;
        try
        {
            var encoded = value.Replace('-', '+').Replace('_', '/');
            encoded = encoded.PadRight(encoded.Length + (4 - encoded.Length % 4) % 4, '=');
            using var document = JsonDocument.Parse(Convert.FromBase64String(encoded), new JsonDocumentOptions { MaxDepth = 4 });
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                throw new JsonException("cursor_object_required");
            var names = document.RootElement.EnumerateObject().Select(item => item.Name).ToArray();
            string[] fields = ["SchemaVersion", "OrgID", "AccountID", "OrganisationRevision", "Action", "Offset", "Limit"];
            if (names.Length != fields.Length || names.Distinct(StringComparer.Ordinal).Count() != names.Length ||
                !names.ToHashSet(StringComparer.Ordinal).SetEquals(fields)) throw new JsonException("cursor_schema_required");
            decoded = document.RootElement.Deserialize<CollectionCursor>() ?? throw new JsonException("cursor_required");
        }
        catch (Exception error) when (error is JsonException or FormatException)
        { throw new ArgumentException("invalid_collection_cursor", error); }
        if (decoded.SchemaVersion != 1 || decoded.OrgID != org.OrgID || decoded.AccountID != accountID ||
            decoded.Action != action || decoded.Offset < 1 || decoded.Limit != limit)
            throw new ArgumentException("invalid_collection_cursor");
        if (decoded.OrganisationRevision != org.Revision)
            throw new InvalidOperationException("revision_conflict");
        return decoded.Offset;
    }
}
