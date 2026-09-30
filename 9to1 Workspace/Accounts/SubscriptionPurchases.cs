namespace NineToOne.Accounts;

public enum SubscriptionPurchaseState { AwaitingSettlement, Activated }
public sealed record SubscriptionPurchase(Guid PurchaseID, Guid AccountID, Guid? OrganisationID,
    SubscriptionQuote Quote, long ExpectedSubscriptionRevision, DateTimeOffset CreatedAt,
    SubscriptionPurchaseState State, string? SettlementID = null, DateTimeOffset? SettledAt = null);
public sealed record VerifiedBillingSettlement(Guid PurchaseID, Guid AccountID, string SettlementID, string Currency,
    decimal ChargedAmount, DateTimeOffset SettledAt);

/// <summary>Server-configured billing adapter validates provider signature, merchant, status and final charge. No client receipt is trusted.</summary>
public interface ITrustedBillingSettlementVerifier
{
    ValueTask<VerifiedBillingSettlement?> VerifyAsync(ReadOnlyMemory<byte> providerEvent,
        IReadOnlyDictionary<string,string> signatureHeaders, CancellationToken cancellationToken);
}

/// <summary>Approved commercial policy resolves monthly threshold price, including cadence/discount/FX rules; no default is invented.</summary>
public interface IEntitlementMonthlyPricePolicy
{
    decimal Resolve(SubscriptionPurchase purchase, VerifiedBillingSettlement settlement);
}

public sealed class SubscriptionPurchaseService(AccountLedger ledger, SubscriptionQuotes ownedQuotes,
    Guid authenticatedAccountID, Guid? organisationID, ITrustedBillingSettlementVerifier? verifier,
    IEntitlementMonthlyPricePolicy? monthlyPricePolicy)
{
    public SubscriptionPurchase Begin(Guid quoteID)
    {
        if(verifier is null)throw new InvalidOperationException("billing_provider_verifier_unconfigured");
        if(monthlyPricePolicy is null)throw new InvalidOperationException("entitlement_monthly_price_policy_unconfigured");
        if(ownedQuotes.OwnerAccountID!=authenticatedAccountID||ownedQuotes.OwnerOrganisationID!=organisationID)throw new InvalidOperationException("checkout_context_mismatch");
        if(organisationID is not null)throw new InvalidOperationException("organisation_resource_purchase_authority_unconfigured");
        var checkedQuote=ownedQuotes.Checkout(quoteID);
        if(checkedQuote.Quote is null)throw new InvalidOperationException("owned_checkout_quote_unavailable");
        return ledger.BeginPurchase(authenticatedAccountID,organisationID,checkedQuote.Quote);
    }

    public async ValueTask<SubscriptionPurchase> ProcessProviderEventAsync(ReadOnlyMemory<byte> providerEvent,
        IReadOnlyDictionary<string,string> signatureHeaders,CancellationToken cancellationToken=default)
    {
        if(verifier is null)throw new InvalidOperationException("billing_provider_verifier_unconfigured");
        if(monthlyPricePolicy is null)throw new InvalidOperationException("entitlement_monthly_price_policy_unconfigured");
        ArgumentNullException.ThrowIfNull(signatureHeaders);
        // Capture the exact provider envelope before the adapter can await. Caller-owned
        // buffers and dictionaries must not change the signed event being verified.
        var capturedEvent=providerEvent.ToArray();
        var capturedHeaders=new System.Collections.ObjectModel.ReadOnlyDictionary<string,string>(
            new Dictionary<string,string>(signatureHeaders,StringComparer.Ordinal));
        var receipt=await verifier.VerifyAsync(capturedEvent,capturedHeaders,cancellationToken).ConfigureAwait(false)
            ?? throw new UnauthorizedAccessException("billing_event_not_verified");
        if(receipt.AccountID!=authenticatedAccountID)throw new UnauthorizedAccessException("billing_account_mismatch");
        return ledger.ActivateVerifiedPurchase(authenticatedAccountID,receipt,monthlyPricePolicy);
    }
}

public sealed partial class AccountLedger
{
    internal SubscriptionPurchase BeginPurchase(Guid authenticatedAccountID, Guid? organisationID, SubscriptionQuote serverOwnedQuote)
    {
        if(serverOwnedQuote.ExpiresAt <= clock.GetUtcNow()) throw new InvalidOperationException("quote_expired");
        lock(gate)
        {
            using var lease=DurableState.Acquire(directory);
            if(serverOwnedQuote.ExpiresAt<=clock.GetUtcNow())throw new InvalidOperationException("quote_expired");
            var account=Read(authenticatedAccountID);
            var purchases=account.Purchases??[];
            var existing=purchases.SingleOrDefault(p=>p.Quote.QuoteID==serverOwnedQuote.QuoteID);
            if(existing is not null)
            {
                if(existing.OrganisationID!=organisationID||existing.Quote!=serverOwnedQuote)throw new InvalidOperationException("purchase_binding_conflict");
                return existing;
            }
            var purchase=new SubscriptionPurchase(Guid.NewGuid(),authenticatedAccountID,organisationID,serverOwnedQuote,
                account.Subscription.Revision,clock.GetUtcNow(),SubscriptionPurchaseState.AwaitingSettlement);
            Write(account with{Purchases=purchases.Append(purchase).ToArray()});
            return purchase;
        }
    }

    internal SubscriptionPurchase ActivateVerifiedPurchase(Guid accountID, VerifiedBillingSettlement receipt, IEntitlementMonthlyPricePolicy policy)
    {
        lock(gate)
        {
            using var lease=DurableState.Acquire(directory);
            var account=Read(accountID);var purchases=account.Purchases??[];
            var purchase=purchases.SingleOrDefault(p=>p.PurchaseID==receipt.PurchaseID)??throw new InvalidOperationException("purchase_not_found");
            if(receipt.AccountID!=accountID||string.IsNullOrWhiteSpace(receipt.SettlementID)||receipt.SettledAt<purchase.CreatedAt||receipt.Currency!=purchase.Quote.Currency||
                receipt.ChargedAmount!=purchase.Quote.Breakdown.FinalMonthlyCharge)throw new InvalidOperationException("settlement_binding_mismatch");
            if(purchase.State==SubscriptionPurchaseState.Activated)
            {
                if(purchase.SettlementID!=receipt.SettlementID||purchase.SettledAt!=receipt.SettledAt)throw new InvalidOperationException("settlement_replay_conflict");
                return purchase;
            }
            if(purchases.Any(p=>p.SettlementID==receipt.SettlementID)||account.Subscription.Revision!=purchase.ExpectedSubscriptionRevision)
                throw new InvalidOperationException("settlement_revision_or_identity_conflict");
            var monthlyPrice=policy.Resolve(purchase,receipt);
            if(monthlyPrice<0)throw new InvalidOperationException("invalid_entitlement_price_policy");
            var resources=account.Subscription.Resources with{AIDustAllocated=purchase.Quote.ActualMonthlyDust,StorageAllocatedBytes=purchase.Quote.Selection.StorageBytes};
            var next=account.Subscription with{PresetID=null,PresetVersion=null,IsCustomised=true,EntitlementPriceMonthly=monthlyPrice,
                Resources=resources,Revision=checked(account.Subscription.Revision+1)};
            SubscriptionPolicy.Evaluate(next);
            var activated=purchase with{State=SubscriptionPurchaseState.Activated,SettlementID=receipt.SettlementID,SettledAt=receipt.SettledAt};
            // Purchase receipt and resource activation share one atomic account-file transaction. Usage and site allowances are preserved.
            Write(account with{Subscription=next,Purchases=purchases.Select(p=>p.PurchaseID==purchase.PurchaseID?activated:p).ToArray()});
            return activated;
        }
    }

    private static void ValidatePurchases(AccountRecord account)
    {
        var purchases=account.Purchases??[];
        if(purchases.Any(p=>p is null||p.AccountID!=account.Subscription.AccountID||p.PurchaseID==Guid.Empty||p.Quote is null||p.Quote.QuoteID==Guid.Empty||
            p.Quote.Selection is null||p.Quote.Breakdown is null||p.Quote.ActualMonthlyDust<0||p.Quote.Selection.StorageBytes<0||p.Quote.Breakdown.FinalMonthlyCharge<0||
            p.ExpectedSubscriptionRevision<1||p.CreatedAt==default||!Enum.IsDefined(p.State)||
            p.State==SubscriptionPurchaseState.AwaitingSettlement&&(p.SettlementID is not null||p.SettledAt is not null)||
            p.State==SubscriptionPurchaseState.Activated&&(string.IsNullOrWhiteSpace(p.SettlementID)||p.SettledAt is null||p.SettledAt<p.CreatedAt))||
            purchases.Select(p=>p.PurchaseID).Distinct().Count()!=purchases.Count||purchases.Select(p=>p.Quote.QuoteID).Distinct().Count()!=purchases.Count||
            purchases.Where(p=>p.SettlementID is not null).Select(p=>p.SettlementID).Distinct(StringComparer.Ordinal).Count()!=purchases.Count(p=>p.SettlementID is not null))
            throw new InvalidDataException("invalid_purchase_state");
    }
}
