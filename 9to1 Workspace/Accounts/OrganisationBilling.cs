namespace NineToOne.Accounts;

/// <summary>Provider adapter output for an existing add-on only. Its approved policy supplies actual effective dates;
/// this event does not purchase, change price, or grant a different add-on.</summary>
public sealed record VerifiedBusinessBillingTransition(string EventID, Guid AccountID, Guid OrgID, long ExpectedRevision,
    string AddOnID, int DefinitionVersion, BusinessBillingState State, DateTimeOffset EffectiveFrom,
    DateTimeOffset? EffectiveUntil, string PolicyVersion);
public sealed record OrganisationBillingConfiguration(Guid OrgID, long Revision, OrganisationAddOn AddOn, int ActiveSeats, string Name);

public interface ITrustedBusinessBillingVerifier
{
    /// <summary>Verify provider signature, merchant, existing subscription/account/organisation binding and settlement status.
    /// Recovery requires actual verified paid entitlement or the existing account-bound free grant; no caller assertion is trusted.</summary>
    ValueTask<VerifiedBusinessBillingTransition?> VerifyAsync(ReadOnlyMemory<byte> providerEvent,
        IReadOnlyDictionary<string, string> signatureHeaders, CancellationToken cancellationToken);
}

public sealed class OrganisationBillingService(OrganisationService organisations, Guid authenticatedAccountID,
    ITrustedBusinessBillingVerifier? verifier)
{
    public async ValueTask<OrganisationBillingConfiguration> ProcessProviderEventAsync(ReadOnlyMemory<byte> providerEvent,
        IReadOnlyDictionary<string, string> signatureHeaders, CancellationToken cancellationToken = default)
    {
        if (verifier is null) throw new InvalidOperationException("business_billing_verifier_unconfigured");
        ArgumentNullException.ThrowIfNull(signatureHeaders);
        var bytes = providerEvent.ToArray();
        var headers = new System.Collections.ObjectModel.ReadOnlyDictionary<string, string>(new Dictionary<string, string>(signatureHeaders, StringComparer.Ordinal));
        var verified = await verifier.VerifyAsync(bytes, headers, cancellationToken).ConfigureAwait(false)
            ?? throw new UnauthorizedAccessException("business_billing_event_unverified");
        cancellationToken.ThrowIfCancellationRequested();
        if (verified.AccountID != authenticatedAccountID) throw new UnauthorizedAccessException("business_billing_account_mismatch");
        return organisations.ApplyVerifiedBillingTransition(authenticatedAccountID, verified);
    }
}

public sealed partial class OrganisationService
{
    public OrganisationBillingConfiguration GetBillingConfiguration(Guid accountID, Guid orgID)
    {
        using var lease = DurableState.Acquire(statePath);
        var org = Read().Organisations.Single(o => o.OrgID == orgID);
        Demand(org, accountID, "Admin.Billing.GetConfiguration");
        return BillingView(org);
    }

    internal OrganisationBillingConfiguration ApplyVerifiedBillingTransition(Guid accountID, VerifiedBusinessBillingTransition change)
    {
        if (change.AccountID != accountID || string.IsNullOrWhiteSpace(change.EventID) || string.IsNullOrWhiteSpace(change.PolicyVersion) ||
            !Enum.IsDefined(change.State) || change.ExpectedRevision < 1 || change.EffectiveFrom == default || change.EffectiveFrom > DateTimeOffset.UtcNow ||
            change.EffectiveUntil is {} end && end <= change.EffectiveFrom)
            throw new InvalidOperationException("invalid_business_billing_transition");
        using var lease = DurableState.Acquire(statePath);
        var state = Read(); var org = state.Organisations.Single(o => o.OrgID == change.OrgID);
        Demand(org, accountID, "Admin.Billing.GetConfiguration");
        var receipts = state.BillingTransitions ?? [];
        var previous = receipts.SingleOrDefault(r => r.EventID == change.EventID);
        if (previous is not null)
        {
            if (previous != change) throw new InvalidOperationException("business_billing_replay_conflict");
            return BillingView(org); // A replay never reactivates an older grant after a later cancellation.
        }
        if (org.Revision != change.ExpectedRevision || org.AddOn.AddOnID != change.AddOnID || org.AddOn.DefinitionVersion != change.DefinitionVersion)
            throw new InvalidOperationException("business_billing_revision_or_addon_conflict");
        if (change.State == BusinessBillingState.Active && org.AddOn.SeatLimit is {} limit && org.Members.Count(m => m.State == OrganisationMemberState.Active) > limit)
            throw new InvalidOperationException("business_seat_remediation_required");
        var next = org with { AddOn = org.AddOn with { State = change.State, EffectiveFrom = change.EffectiveFrom, EffectiveUntil = change.EffectiveUntil }, Revision = checked(org.Revision + 1) };
        Save(state with { Organisations = state.Organisations.Select(o => o.OrgID == org.OrgID ? next : o).ToArray(),
            BillingTransitions = receipts.Append(change).ToArray(),
            Audit = state.Audit.Append(new(Guid.NewGuid(), org.OrgID, accountID, "Admin.Billing.VerifiedLifecycle", null, org.Revision, next.Revision,
                DateTimeOffset.UtcNow, change.EventID)).ToArray() });
        return BillingView(next);
    }
    private static OrganisationBillingConfiguration BillingView(Organisation org)
        => new(org.OrgID, org.Revision, org.AddOn, org.Members.Count(m => m.State == OrganisationMemberState.Active), org.Name);
}
