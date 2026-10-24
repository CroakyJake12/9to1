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

    /// <summary>Preserves the original positional cancellation-token contract for unfiltered listing.</summary>
    public OrganisationInvitationPage ListInvitations(Guid authenticatedAccountID, Guid organisationID,
        string? cursor, int limit, CancellationToken cancellationToken) =>
        ListInvitations(authenticatedAccountID, organisationID, cursor, limit, search: null,
            cancellationToken: cancellationToken);

    /// <summary>Lists current permission-filtered invitation metadata, with optional bounded search over stable invitation,
    /// inviter, intended-account and role IDs. Bearer tokens and token hashes are never returned; each continuation
    /// rechecks the actor and invitation revision.</summary>
    public OrganisationInvitationPage ListInvitations(Guid authenticatedAccountID,Guid organisationID,
        string? cursor=null,int limit=50,string? search=null,CancellationToken cancellationToken=default,
        Guid? intendedAccountIDFilter=null,OrganisationInvitationStatus? statusFilter=null)
    {
        if(limit is <1 or >200)throw new ArgumentOutOfRangeException(nameof(limit));
        var normalizedSearch=NormalizeInvitationSearch(search);
        if(intendedAccountIDFilter==Guid.Empty||statusFilter is { } selectedStatus&&!Enum.IsDefined(selectedStatus))
            throw new ArgumentException("valid_invitation_filters_required");
        cancellationToken.ThrowIfCancellationRequested();
        using var lease=DurableState.Acquire(statePath);
        cancellationToken.ThrowIfCancellationRequested();
        var state=Read();
        var organisation=state.Organisations.SingleOrDefault(item=>item.OrgID==organisationID);
        if(organisation is null||authenticatedAccountID==Guid.Empty)
            throw new OrganisationAccessException("PermissionDenied");
        Demand(organisation,authenticatedAccountID,"Admin.Members.List");
        var now=LifecycleNow;
        var decodedCursor=cursor is null?null:ReadInvitationCursor(cursor,authenticatedAccountID,organisationID,
            organisation.InvitationRevision,limit,normalizedSearch,intendedAccountIDFilter,statusFilter);
        if(decodedCursor is not null&&decodedCursor.ObservedAt>now)
            throw new ArgumentException("invalid_invitation_cursor");
        if(decodedCursor is not null&&statusFilter is { } filter&&InvitationFilterMembershipChanged(
            state.Invitations??[],organisation,normalizedSearch,intendedAccountIDFilter,filter,decodedCursor.ObservedAt,now))
            throw new AdministrationConflictException(AdministrationConflictCode.InvitationFilterStale);
        var offset=decodedCursor?.Offset??0;
        var observedAt=decodedCursor is not null&&statusFilter is not null?decodedCursor.ObservedAt:now;
        var snapshot=(state.Invitations??[]).Where(item=>item.OrgID==organisationID)
            .Where(item=>MatchesInvitationSearch(item,normalizedSearch))
            .Where(item=>intendedAccountIDFilter is null||item.IntendedAccountID==intendedAccountIDFilter)
            .OrderBy(item=>item.InvitationID).Select(item=>
            {
                var status=InvitationStatusAt(item,organisation,observedAt);
                return new OrganisationInvitationSummary(item.InvitationID,item.InviterID,item.IntendedAccountID,
                    Array.AsReadOnly(item.RoleIDs.ToArray()),item.ExpiresAt,status,item.RevokedAt,item.SchemaVersion);
            }).Where(item=>statusFilter is null||item.Status==statusFilter).ToArray();
        if(offset>snapshot.Length)throw new ArgumentException("invalid_invitation_cursor");
        var items=snapshot.Skip(offset).Take(limit).ToArray();
        var nextOffset=checked(offset+items.Length);
        var next=nextOffset<snapshot.Length?WriteInvitationCursor(authenticatedAccountID,organisationID,
            organisation.InvitationRevision,nextOffset,limit,normalizedSearch,intendedAccountIDFilter,statusFilter,observedAt):null;
        cancellationToken.ThrowIfCancellationRequested();
        return new(organisationID,organisation.Revision,organisation.InvitationRevision,
            Array.AsReadOnly(items),snapshot.Length,next,observedAt,normalizedSearch,intendedAccountIDFilter,statusFilter);
    }

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

    private sealed record InvitationCursor(int SchemaVersion,Guid OrgID,Guid AccountID,
        long InvitationCollectionRevision,string Action,int Offset,int Limit,string? Search,
        Guid? IntendedAccountIDFilter,OrganisationInvitationStatus? StatusFilter,DateTimeOffset ObservedAt);

    private static string? NormalizeInvitationSearch(string? search)
    {
        if(string.IsNullOrWhiteSpace(search))return null;
        var normalized=search.Trim();
        if(normalized.Length>36||normalized.Any(char.IsControl))throw new ArgumentException("bounded_invitation_search_required");
        return normalized;
    }

    private static bool MatchesInvitationSearch(OrganisationInvitation invitation,string? search)
    {
        if(search is null)return true;
        static bool Matches(Guid id,string value)=>id.ToString("D").StartsWith(value,StringComparison.OrdinalIgnoreCase)||
            id.ToString("N").StartsWith(value,StringComparison.OrdinalIgnoreCase);
        return Matches(invitation.InvitationID,search)||Matches(invitation.InviterID,search)||
            invitation.IntendedAccountID is { } intended&&Matches(intended,search)||
            invitation.RoleIDs.Any(roleID=>Matches(roleID,search));
    }

    private static OrganisationInvitationStatus InvitationStatusAt(OrganisationInvitation invitation,
        Organisation organisation,DateTimeOffset observedAt)
    {
        var invalidRoleAssignment=invitation.RoleIDs.Distinct().Count()!=invitation.RoleIDs.Count||
            invitation.RoleIDs.Any(roleID=>!organisation.Roles.Any(role=>role.RoleID==roleID&&!role.IsOwner));
        return invitation.Revoked?OrganisationInvitationStatus.Revoked:
            invitation.Accepted?OrganisationInvitationStatus.Accepted:
            invitation.ExpiresAt<=observedAt?OrganisationInvitationStatus.Expired:
            invitation.IntendedAccountID is null||invitation.IntendedAccountID==Guid.Empty?OrganisationInvitationStatus.InvalidIdentity:
            invalidRoleAssignment?OrganisationInvitationStatus.InvalidRole:OrganisationInvitationStatus.Pending;
    }

    private static bool InvitationFilterMembershipChanged(IReadOnlyList<OrganisationInvitation> invitations,
        Organisation organisation,string? search,Guid? intendedAccountID,OrganisationInvitationStatus statusFilter,
        DateTimeOffset originalObservedAt,DateTimeOffset currentObservedAt)
    {
        foreach(var invitation in invitations.Where(item=>item.OrgID==organisation.OrgID&&
            MatchesInvitationSearch(item,search)&&(intendedAccountID is null||item.IntendedAccountID==intendedAccountID)))
        {
            var originalMatch=InvitationStatusAt(invitation,organisation,originalObservedAt)==statusFilter;
            var currentMatch=InvitationStatusAt(invitation,organisation,currentObservedAt)==statusFilter;
            if(originalMatch!=currentMatch)return true;
        }
        return false;
    }

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

    private static string WriteInvitationCursor(Guid accountID,Guid orgID,long invitationRevision,int offset,int limit,
        string? search,Guid? intendedAccountIDFilter,OrganisationInvitationStatus? statusFilter,DateTimeOffset observedAt)=>
        Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(new InvitationCursor(1,orgID,accountID,
            invitationRevision,"Admin.Members.List",offset,limit,search,intendedAccountIDFilter,statusFilter,observedAt)))
            .TrimEnd('=').Replace('+','-').Replace('/','_');

    private static InvitationCursor ReadInvitationCursor(string value,Guid accountID,Guid orgID,long invitationRevision,
        int limit,string? search,Guid? intendedAccountIDFilter,OrganisationInvitationStatus? statusFilter)
    {
        if(value.Length is <1 or >512||value.Any(character=>!char.IsAsciiLetterOrDigit(character)&&character is not ('-' or '_')))
            throw new ArgumentException("invalid_invitation_cursor");
        InvitationCursor decoded;
        try
        {
            var encoded=value.Replace('-','+').Replace('_','/');
            encoded=encoded.PadRight(encoded.Length+(4-encoded.Length%4)%4,'=');
            using var document=JsonDocument.Parse(Convert.FromBase64String(encoded),new JsonDocumentOptions{MaxDepth=4});
            if(document.RootElement.ValueKind!=JsonValueKind.Object)throw new JsonException("cursor_object_required");
            var names=document.RootElement.EnumerateObject().Select(item=>item.Name).ToArray();
            string[] fields=["SchemaVersion","OrgID","AccountID","InvitationCollectionRevision","Action","Offset","Limit",
                "Search","IntendedAccountIDFilter","StatusFilter","ObservedAt"];
            if(names.Length!=fields.Length||names.Distinct(StringComparer.Ordinal).Count()!=names.Length||
                !names.ToHashSet(StringComparer.Ordinal).SetEquals(fields))throw new JsonException("cursor_schema_required");
            decoded=document.RootElement.Deserialize<InvitationCursor>()??throw new JsonException("cursor_required");
        }
        catch(Exception error) when(error is JsonException or FormatException)
        {throw new ArgumentException("invalid_invitation_cursor",error);}
        if(decoded.SchemaVersion!=1||decoded.OrgID!=orgID||decoded.AccountID!=accountID||
            decoded.Action!="Admin.Members.List"||decoded.Offset<1||decoded.Limit!=limit||decoded.Search!=search||
            decoded.IntendedAccountIDFilter!=intendedAccountIDFilter||decoded.StatusFilter!=statusFilter||decoded.ObservedAt==default)
            throw new ArgumentException("invalid_invitation_cursor");
        if(decoded.InvitationCollectionRevision!=invitationRevision)
            throw new InvalidOperationException("revision_conflict");
        return decoded;
    }
}
