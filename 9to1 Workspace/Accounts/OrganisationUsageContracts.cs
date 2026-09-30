namespace NineToOne.Accounts;

/// <summary>Canonical invocation metadata supplied by the trusted quote issuer; never document/message content.</summary>
public sealed record OrganisationUsageAttribution(string AppID,string ModelID,string? AgentID,string? AutomationID);
public sealed record OrganisationFundingRequest(Guid OrgID,long ExpectedPolicyRevision,string ModelRouteID,
    Guid CostQuoteID,string OperationID);
public sealed record OrganisationFundingSlice(Guid SourceAllocationID,long Dust);
public sealed record OrganisationFundingReservation(Guid ReservationID,Guid AccountID,Guid OrgID,Guid PoolID,
    string PeriodID,string OperationID,string ModelRouteID,Guid CostQuoteID,long PolicyRevision,long ReservedDust,
    DateTimeOffset ExpiresAt,IReadOnlyList<OrganisationFundingSlice> FundingSources,long AuthorisationRevision,string? RegisteredClientID=null,OrganisationUsageAttribution? Attribution=null);
/// <summary>Final trusted observation; ProviderUsageReference is unique to the actual provider/operation usage report.</summary>
public sealed record OrganisationObservedUsage(Guid ReservationID,string SettlementID,long ActualDust,
    string ProviderUsageReference);

/// <summary>Trusted gateway port. Token is verified against current server session; no caller roles, quotas or personal fallback.</summary>
public interface IOrganisationCloudFundingAuthority
{
    ValueTask<OrganisationFundingReservation> ReserveAsync(string sessionAccessToken,OrganisationFundingRequest request,CancellationToken ct);
    ValueTask<OrganisationFundingReservation> RecheckDispatchAsync(string sessionAccessToken,Guid reservationID,long expectedPolicyRevision,CancellationToken ct);
    // These reports come only from the trusted provider/gateway observer, never a public client usage counter.
    ValueTask SettleObservedAsync(OrganisationObservedUsage observed,CancellationToken ct);
    ValueTask CancelAsync(Guid reservationID,string cancellationID,CancellationToken ct);
}

public sealed record VerifiedOrganisationCostQuote(Guid QuoteID,Guid AccountID,Guid OrgID,Guid PoolID,string ModelRouteID,
    long PolicyRevision,long MaximumDust,DateTimeOffset ExpiresAt,string? RegisteredClientID=null,OrganisationUsageAttribution? Attribution=null);
public interface IOrganisationCostQuoteAuthority
{
    ValueTask<VerifiedOrganisationCostQuote?> ResolveAsync(Guid quoteID,CancellationToken ct);
}
public sealed record VerifiedOrganisationAllocation(Guid SourceAllocationID,Guid OrgID,string PeriodID,long Dust,
    DateTimeOffset AcquiredAt,DateTimeOffset ExpiresAt,string FundingReference);
public interface IOrganisationAllocationFundingAuthority
{
    ValueTask<VerifiedOrganisationAllocation?> ResolveAsync(string fundedAllocationReference,CancellationToken ct);
}
