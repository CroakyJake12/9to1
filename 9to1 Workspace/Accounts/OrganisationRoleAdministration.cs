using System.Collections.Frozen;
using System.Security.Cryptography;
using System.Text.Json;

namespace NineToOne.Accounts;

public sealed partial class OrganisationService
{
    public OrganisationRoleMutationResult UpdateRole(Guid actorID, Guid orgID, Guid roleID,
        long expectedRevision, string idempotencyKey, string name, IReadOnlySet<string> grants,
        IReadOnlySet<string> denials, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 256 || name.Any(char.IsControl))
            throw new ArgumentException("bounded_role_name_required");
        var capturedGrants = CaptureRoleCapabilities(grants);
        var capturedDenials = CaptureRoleCapabilities(denials);
        var fingerprint = RoleRequestFingerprint(roleID, name.Trim(), capturedGrants, capturedDenials);
        return MutateAdministrationRole(actorID, orgID, roleID, expectedRevision, idempotencyKey,
            "Admin.Roles.Update", fingerprint, (org, original) =>
            {
                DemandRoleDelegation(org, actorID, original.Grants);
                DemandRoleDelegation(org, actorID, capturedGrants);
                if (capturedGrants.Contains("*")) throw new UnauthorizedAccessException("custom_role_cannot_grant_owner_wildcard");
                return original with { Name = name.Trim(), Grants = capturedGrants, Denials = capturedDenials,
                    Revision = checked(original.Revision + 1) };
            }, cancellationToken);
    }

    public OrganisationRoleMutationResult DeleteRole(Guid actorID, Guid orgID, Guid roleID,
        long expectedRevision, string idempotencyKey, CancellationToken cancellationToken = default) =>
        MutateAdministrationRole(actorID, orgID, roleID, expectedRevision, idempotencyKey,
            "Admin.Roles.Delete", RoleRequestFingerprint(roleID, null, [], []), (org, original) =>
            {
                DemandRoleDelegation(org, actorID, original.Grants);
                if (org.Members.Any(member => member.RoleIDs.Contains(roleID)))
                    throw new AdministrationConflictException(AdministrationConflictCode.RoleInUse);
                return null;
            }, cancellationToken);

    public OrganisationEffectiveRolePermissions GetEffectiveRolePermissions(Guid actorID, Guid orgID,
        Guid targetAccountID, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var lease = DurableState.Acquire(statePath);
        cancellationToken.ThrowIfCancellationRequested();
        var org = Read().Organisations.Single(o => o.OrgID == orgID);
        Demand(org, actorID, "Admin.Roles.GetEffectivePermissions");
        var member = org.Members.SingleOrDefault(m => m.AccountID == targetAccountID && m.State == OrganisationMemberState.Active)
            ?? throw new OrganisationAccessException("PermissionDenied");
        var roles = org.Roles.Where(role => member.RoleIDs.Contains(role.RoleID)).ToArray();
        var denied = roles.SelectMany(role => role.Denials).Concat(org.Policy.BlockedCapabilities)
            .ToFrozenSet(StringComparer.Ordinal);
        var wildcard = roles.Any(role => role.Grants.Contains("*")) && !denied.Contains("*");
        var candidates = roles.SelectMany(role => role.Grants)
            .Where(key => key != "*" && !denied.Contains("*") && !denied.Contains(key)).ToFrozenSet(StringComparer.Ordinal);
        // This describes role/policy restrictions only. Entitlement, caller trust,
        // object ACL, Home and endpoint controls are intersected at actual dispatch.
        return new(orgID, targetAccountID, org.Revision, org.Policy.Revision, candidates, denied, wildcard);
    }

    private OrganisationRoleMutationResult MutateAdministrationRole(Guid actorID, Guid orgID,
        Guid roleID, long expectedRevision, string key, string action, string fingerprint,
        Func<Organisation, OrganisationRole, OrganisationRole?> mutation, CancellationToken ct)
    {
        ValidateLifecycleKey(key); ValidateLifecycleRevision(expectedRevision);
        if (roleID == Guid.Empty) throw new ArgumentException("canonical_role_id_required");
        ct.ThrowIfCancellationRequested();
        using var lease = DurableState.Acquire(statePath);
        ct.ThrowIfCancellationRequested();
        var state = Read(); var org = state.Organisations.Single(o => o.OrgID == orgID);
        Demand(org, actorID, action);
        var prior = (state.RoleMutationReceipts ?? []).SingleOrDefault(receipt => receipt.OrgID == orgID &&
            receipt.ActorID == actorID && receipt.Action == action && receipt.IdempotencyKey == key);
        if (prior is not null)
        {
            if (prior.ExpectedRevision != expectedRevision || prior.RoleID != roleID || prior.RequestFingerprint != fingerprint)
                throw new AdministrationConflictException(AdministrationConflictCode.IdempotencyConflict);
            var current = org.Roles.SingleOrDefault(role => role.RoleID == roleID);
            return new(orgID, org.Revision, current is null ? null : DetachRole(current), prior, true);
        }
        if (org.Revision != expectedRevision) throw new AdministrationConflictException(AdministrationConflictCode.RevisionConflict);
        var original = org.Roles.SingleOrDefault(role => role.RoleID == roleID)
            ?? throw new KeyNotFoundException("role_not_found");
        if (original.IsOwner) throw new UnauthorizedAccessException("protected_owner_role_requires_ownership_workflow");
        var replacement = mutation(org, original);
        var roles = replacement is null ? org.Roles.Where(role => role.RoleID != roleID).ToArray() :
            org.Roles.Select(role => role.RoleID == roleID ? replacement : role).ToArray();
        var next = org with { Roles = roles, Revision = checked(org.Revision + 1) };
        EnsureOwner(next);
        var receipt = new OrganisationRoleMutationReceipt(orgID, actorID, action, key, expectedRevision,
            next.Revision, roleID, fingerprint, replacement?.Revision ?? original.Revision);
        ct.ThrowIfCancellationRequested();
        Save(state with { Organisations = state.Organisations.Select(item => item.OrgID == orgID ? next : item).ToArray(),
            RoleMutationReceipts = (state.RoleMutationReceipts ?? []).Append(receipt).ToArray(),
            Audit = state.Audit.Append(new(Guid.NewGuid(), orgID, actorID, action, roleID,
                org.Revision, next.Revision, LifecycleNow, null)).ToArray() });
        return new(orgID, next.Revision, replacement is null ? null : DetachRole(replacement), receipt, false);
    }

    private static OrganisationRole DetachRole(OrganisationRole value) => value with
    { Grants = value.Grants.ToFrozenSet(StringComparer.Ordinal), Denials = value.Denials.ToFrozenSet(StringComparer.Ordinal) };
    private static IReadOnlySet<string> CaptureRoleCapabilities(IReadOnlySet<string> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (values.Count > 1024 || values.Any(value => string.IsNullOrWhiteSpace(value) || value.Length > 256 || value.Any(char.IsControl)))
            throw new ArgumentException("bounded_role_capability_set_required");
        return values.ToFrozenSet(StringComparer.Ordinal);
    }
    private static void DemandRoleDelegation(Organisation org, Guid actorID, IReadOnlySet<string> grants)
    { foreach (var grant in grants) Demand(org, actorID, grant); }
    private static string RoleRequestFingerprint(Guid roleID, string? name,
        IEnumerable<string> grants, IEnumerable<string> denials) => Convert.ToHexString(SHA256.HashData(
            JsonSerializer.SerializeToUtf8Bytes(new { RoleID = roleID, Name = name,
                Grants = grants.Order(StringComparer.Ordinal).ToArray(), Denials = denials.Order(StringComparer.Ordinal).ToArray() })));
}
