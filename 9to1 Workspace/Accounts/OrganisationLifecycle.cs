namespace NineToOne.Accounts;

/// <summary>Canonical organisation metadata only. No personal artifacts, domain content,
/// credentials or purchases move when its owner or recoverable lifecycle changes.</summary>
public sealed partial class OrganisationService
{
    private static readonly IReadOnlySet<string> ArchivedManagementActions = new HashSet<string>(StringComparer.Ordinal)
    {
        "Admin.Organisations.Get", "Admin.Organisations.Restore", "Admin.Audit.List"
    };
    private DateTimeOffset LifecycleNow => (lifecycleTimeProvider ?? TimeProvider.System).GetUtcNow();

    public OrganisationLifecycleResult UpdateIdentity(Guid actorID, Guid orgID, long expectedRevision, string idempotencyKey, string name, CancellationToken cancellationToken = default)
    {
        ValidateLifecycleRevision(expectedRevision); ValidateLifecycleKey(idempotencyKey);
        if (string.IsNullOrWhiteSpace(name) || name.Length > 1000) throw new ArgumentException("organisation_name_required_or_too_long");
        return MutateLifecycle(actorID, orgID, expectedRevision, idempotencyKey, "Admin.Organisations.Update", name.Trim(), null, cancellationToken);
    }

    public OrganisationLifecycleResult Archive(Guid actorID, Guid orgID, long expectedRevision, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        ValidateLifecycleRevision(expectedRevision); ValidateLifecycleKey(idempotencyKey);
        return MutateLifecycle(actorID, orgID, expectedRevision, idempotencyKey, "Admin.Organisations.Archive", null, OrganisationLifecycleState.Archived, cancellationToken);
    }

    public OrganisationLifecycleResult Restore(Guid actorID, Guid orgID, long expectedRevision, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        ValidateLifecycleRevision(expectedRevision); ValidateLifecycleKey(idempotencyKey);
        return MutateLifecycle(actorID, orgID, expectedRevision, idempotencyKey, "Admin.Organisations.Restore", null, OrganisationLifecycleState.Active, cancellationToken);
    }

    private OrganisationLifecycleResult MutateLifecycle(Guid actorID, Guid orgID, long expectedRevision,
        string key, string action, string? name, OrganisationLifecycleState? lifecycle, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var lease = DurableState.Acquire(statePath);
        cancellationToken.ThrowIfCancellationRequested();
        var state = Read(); var org = state.Organisations.Single(o => o.OrgID == orgID);
        var existing = (state.LifecycleReceipts ?? []).SingleOrDefault(r => r.OrgID == orgID &&
            r.ActorID == actorID && r.Action == action && r.IdempotencyKey == key);
        if (existing is not null)
        {
            // Receipt retrieval ignores only the archived mutation stop. Current entitlement,
            // membership, roles and denials for this exact capability remain authoritative.
            Demand(org with { Lifecycle = OrganisationLifecycleState.Active }, actorID, action);
            if (existing.ExpectedRevision != expectedRevision || existing.Name != name || existing.Lifecycle != lifecycle)
                throw new InvalidOperationException("idempotency_conflict");
            return new(org, existing, true);
        }
        Demand(org, actorID, action);
        if (org.Revision != expectedRevision) throw new InvalidOperationException("revision_conflict");
        var next = org with { Name = name ?? org.Name, Lifecycle = lifecycle ?? org.Lifecycle,
            Revision = checked(org.Revision + 1), SchemaVersion = 2 };
        EnsureOwner(next);
        var receipt = new OrganisationLifecycleReceipt(orgID, actorID, action, key, expectedRevision,
            next.Revision, name, lifecycle);
        cancellationToken.ThrowIfCancellationRequested();
        Save(state with
        {
            Organisations = state.Organisations.Select(o => o.OrgID == orgID ? next : o).ToArray(),
            LifecycleReceipts = (state.LifecycleReceipts ?? []).Append(receipt).ToArray(),
            Audit = state.Audit.Append(new(Guid.NewGuid(), orgID, actorID, action, orgID,
                org.Revision, next.Revision, LifecycleNow, null)).ToArray()
        });
        return new(next, receipt, false);
    }

    public OrganisationOwnershipTransfer RequestOwnershipTransfer(Guid authenticatedOwnerID, Guid orgID,
        long expectedRevision, string idempotencyKey, Guid recipientAccountID, DateTimeOffset expiresAt, CancellationToken cancellationToken = default)
    {
        ValidateLifecycleKey(idempotencyKey); ValidateLifecycleRevision(expectedRevision);
        if (recipientAccountID == Guid.Empty || recipientAccountID == authenticatedOwnerID || expiresAt <= LifecycleNow)
            throw new ArgumentException("ownership_transfer_recipient_and_future_expiry_required");
        cancellationToken.ThrowIfCancellationRequested();
        using var lease = DurableState.Acquire(statePath);
        cancellationToken.ThrowIfCancellationRequested();
        var state = Read(); var org = state.Organisations.Single(o => o.OrgID == orgID);
        Demand(org, authenticatedOwnerID, "Admin.Organisations.TransferOwnership");
        RequireCurrentOwner(org, authenticatedOwnerID);
        var existing = (state.OwnershipTransfers ?? []).SingleOrDefault(t => t.OrgID == orgID &&
            t.InitiatorAccountID == authenticatedOwnerID && t.RequestIdempotencyKey == idempotencyKey);
        if (existing is not null)
        {
            if (existing.RecipientAccountID != recipientAccountID || existing.ExpiresAt != expiresAt || existing.OrganisationRevision != expectedRevision + 1)
                throw new InvalidOperationException("idempotency_conflict");
            return existing;
        }
        if (org.Revision != expectedRevision) throw new InvalidOperationException("revision_conflict");
        if (expiresAt <= LifecycleNow) throw new UnauthorizedAccessException("ownership_transfer_expired_before_admission");
        RequireActiveRecipient(org, recipientAccountID);
        RequireFundedOwnershipTransition(org, recipientAccountID);
        var next = org with { Revision = checked(org.Revision + 1), SchemaVersion = 2 };
        var transfer = new OrganisationOwnershipTransfer(Guid.NewGuid(), orgID, authenticatedOwnerID, recipientAccountID,
            next.Revision, 1, expiresAt, OrganisationOwnershipTransferState.Pending, idempotencyKey);
        cancellationToken.ThrowIfCancellationRequested();
        Save(state with
        {
            Organisations = state.Organisations.Select(o => o.OrgID == orgID ? next : o).ToArray(),
            OwnershipTransfers = (state.OwnershipTransfers ?? []).Append(transfer).ToArray(),
            Audit = state.Audit.Append(new(Guid.NewGuid(), orgID, authenticatedOwnerID, "Admin.Organisations.TransferOwnership.Requested",
                transfer.TransferID, org.Revision, next.Revision, LifecycleNow, null)).ToArray()
        });
        return transfer;
    }

    public OrganisationOwnershipTransferResult AcceptOwnershipTransfer(Guid authenticatedRecipientID, Guid orgID,
        Guid transferID, long expectedRevision, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        ValidateLifecycleKey(idempotencyKey); ValidateLifecycleRevision(expectedRevision);
        cancellationToken.ThrowIfCancellationRequested();
        using var lease = DurableState.Acquire(statePath);
        cancellationToken.ThrowIfCancellationRequested();
        var state = Read(); var org = state.Organisations.Single(o => o.OrgID == orgID);
        var transfer = (state.OwnershipTransfers ?? []).SingleOrDefault(t => t.TransferID == transferID && t.OrgID == orgID)
            ?? throw new UnauthorizedAccessException("ownership_transfer_unavailable");
        if (authenticatedRecipientID == Guid.Empty || transfer.RecipientAccountID != authenticatedRecipientID)
            throw new UnauthorizedAccessException("ownership_transfer_intended_recipient_required");
        RequireActiveRecipient(org, authenticatedRecipientID);
        if (org.Lifecycle != OrganisationLifecycleState.Active) throw new OrganisationAccessException("OrganisationArchived");
        // Acceptance is a narrowly identified owner invitation, never a general grant supplied by the recipient.
        if (transfer.State == OrganisationOwnershipTransferState.Accepted)
        {
            if (transfer.AcceptedIdempotencyKey != idempotencyKey || expectedRevision != transfer.OrganisationRevision)
                throw new InvalidOperationException("idempotency_conflict");
            Demand(org, authenticatedRecipientID, "Admin.Organisations.Get");
            RequireCurrentOwner(org, authenticatedRecipientID);
            return new(org, transfer);
        }
        Demand(org, transfer.InitiatorAccountID, "Admin.Organisations.TransferOwnership");
        RequireCurrentOwner(org, transfer.InitiatorAccountID);
        if (transfer.State != OrganisationOwnershipTransferState.Pending || transfer.ExpiresAt <= LifecycleNow)
            throw new UnauthorizedAccessException("ownership_transfer_expired_or_resolved");
        if (org.Revision != expectedRevision || transfer.OrganisationRevision != expectedRevision)
            throw new InvalidOperationException("revision_conflict");
        RequireFundedOwnershipTransition(org, authenticatedRecipientID);
        var ownerRoles = org.Roles.Where(r => r.IsOwner).Select(r => r.RoleID).ToHashSet();
        var ownerRole = org.Roles.First(r => r.IsOwner).RoleID;
        var members = org.Members.Select(member => member.AccountID == transfer.InitiatorAccountID
            ? member with { RoleIDs = member.RoleIDs.Where(role => !ownerRoles.Contains(role)).ToArray(), Revision = checked(member.Revision + 1) }
            : member.AccountID == authenticatedRecipientID
                ? member with { RoleIDs = member.RoleIDs.Append(ownerRole).Distinct().ToArray(), Revision = checked(member.Revision + 1) }
                : member).ToArray();
        var next = org with { Members = members, Revision = checked(org.Revision + 1), SchemaVersion = 2 };
        EnsureOwner(next);
        var accepted = transfer with { State = OrganisationOwnershipTransferState.Accepted, Revision = checked(transfer.Revision + 1),
            AcceptedOrganisationRevision = next.Revision, AcceptedIdempotencyKey = idempotencyKey };
        cancellationToken.ThrowIfCancellationRequested();
        Save(state with
        {
            Organisations = state.Organisations.Select(o => o.OrgID == orgID ? next : o).ToArray(),
            OwnershipTransfers = (state.OwnershipTransfers ?? []).Select(t => t.TransferID == transferID ? accepted : t).ToArray(),
            Audit = state.Audit.Append(new(Guid.NewGuid(), orgID, authenticatedRecipientID, "Admin.Organisations.TransferOwnership.Accepted",
                transferID, org.Revision, next.Revision, LifecycleNow, null)).ToArray()
        });
        return new(next, accepted);
    }

    private static void RequireCurrentOwner(Organisation org, Guid accountID)
    {
        if (!org.Members.Any(m => m.AccountID == accountID && m.State == OrganisationMemberState.Active &&
            m.RoleIDs.Any(id => org.Roles.Any(r => r.RoleID == id && r.IsOwner))))
            throw new UnauthorizedAccessException("current_owner_required");
    }
    private static void RequireActiveRecipient(Organisation org, Guid accountID)
    {
        if (!org.Members.Any(m => m.AccountID == accountID && m.State == OrganisationMemberState.Active))
            throw new UnauthorizedAccessException("ownership_transfer_active_member_required");
    }
    private void RequireFundedOwnershipTransition(Organisation org, Guid recipientID)
    {
        // A grant follows Jacob's verified account, never the mutable organisation owner label.
        if (org.AddOn.MonthlyPrice == 0 && !profiles.HasAccountBoundFreeBusiness(recipientID))
            throw new InvalidOperationException("ownership_transfer_requires_authorised_billing_transition");
    }
    private static void ValidateLifecycleRevision(long expectedRevision)
    {
        if (expectedRevision < 1 || expectedRevision == long.MaxValue) throw new ArgumentException("bounded_expected_revision_required");
    }
    private static void ValidateLifecycleKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key) || key.Length > 256 || key.Any(char.IsControl)) throw new ArgumentException("bounded_idempotency_key_required");
    }
    private static void ValidateOwnershipTransfers(OrganisationState state)
    {
        var transfers = state.OwnershipTransfers ?? [];
        if (transfers.Any(t => t is null) || transfers.Select(t => t.TransferID).Distinct().Count() != transfers.Count ||
            transfers.GroupBy(t => (t.OrgID, t.InitiatorAccountID, t.RequestIdempotencyKey)).Any(g => g.Count() > 1))
            throw new InvalidDataException("corrupt_ownership_transfer_identity");
        foreach (var t in transfers)
        {
            var org = state.Organisations.SingleOrDefault(o => o.OrgID == t.OrgID);
            if (org is null || t.TransferID == Guid.Empty || t.InitiatorAccountID == Guid.Empty || t.RecipientAccountID == Guid.Empty ||
                t.InitiatorAccountID == t.RecipientAccountID || t.OrganisationRevision < 2 || t.OrganisationRevision > org.Revision ||
                t.Revision < 1 || t.ExpiresAt == default || !Enum.IsDefined(t.State) || string.IsNullOrWhiteSpace(t.RequestIdempotencyKey) ||
                t.RequestIdempotencyKey.Length > 256 || !org.Members.Any(m => m.AccountID == t.InitiatorAccountID) ||
                !org.Members.Any(m => m.AccountID == t.RecipientAccountID) ||
                (t.State == OrganisationOwnershipTransferState.Accepted
                    ? t.AcceptedOrganisationRevision is not { } accepted || accepted <= t.OrganisationRevision || accepted > org.Revision ||
                        string.IsNullOrWhiteSpace(t.AcceptedIdempotencyKey) || t.AcceptedIdempotencyKey.Length > 256
                    : t.AcceptedOrganisationRevision is not null || t.AcceptedIdempotencyKey is not null))
                throw new InvalidDataException("corrupt_ownership_transfer_binding");
        }
    }

    private static void ValidateLifecycleReceipts(OrganisationState state)
    {
        var receipts = state.LifecycleReceipts ?? [];
        if (receipts.Any(r => r is null) || receipts.GroupBy(r => (r.OrgID, r.ActorID, r.Action, r.IdempotencyKey)).Any(g => g.Count() > 1))
            throw new InvalidDataException("corrupt_organisation_lifecycle_identity");
        foreach (var r in receipts)
        {
            var org = state.Organisations.SingleOrDefault(o => o.OrgID == r.OrgID);
            var payloadValid = r.Action switch
            {
                "Admin.Organisations.Update" => !string.IsNullOrWhiteSpace(r.Name) && r.Name.Length <= 1000 && r.Name == r.Name.Trim() && r.Lifecycle is null,
                "Admin.Organisations.Archive" => r.Name is null && r.Lifecycle == OrganisationLifecycleState.Archived,
                "Admin.Organisations.Restore" => r.Name is null && r.Lifecycle == OrganisationLifecycleState.Active,
                _ => false
            };
            if (org is null || r.ActorID == Guid.Empty || !org.Members.Any(m => m.AccountID == r.ActorID) ||
                r.ExpectedRevision < 1 || r.ExpectedRevision == long.MaxValue || r.ResultingRevision != r.ExpectedRevision + 1 ||
                r.ResultingRevision > org.Revision || string.IsNullOrWhiteSpace(r.IdempotencyKey) || r.IdempotencyKey.Length > 256 || !payloadValid)
                throw new InvalidDataException("corrupt_organisation_lifecycle_binding");
        }
    }
}
