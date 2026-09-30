namespace NineToOne.Accounts;

public enum ChargeFeeRounding { Exact, NearestMinorUnit, CeilingMinorUnit }
public sealed record ApprovedCostModel(string Version,string Currency,long? DustPer1x,decimal? AICostPerDust,
    decimal? StorageCostPerByte,decimal? InfrastructureCostPerDust,decimal? InfrastructureCostPerByte,
    decimal? SharedOperatingCost,decimal? PaymentFixedFee,decimal? BillingFixedFee,decimal? PaymentFeeRate,
    decimal? BillingFeeRate,decimal? TaxRate,bool? PercentageFeesApplyTaxInclusiveCharge,
    long? MaximumMonthlyDust,long? MaximumStorageBytes,bool Approved,
    bool? PaymentFeeAppliesTaxInclusiveCharge=null,bool? BillingFeeAppliesTaxInclusiveCharge=null,ChargeFeeRounding? FeeRounding=null,string? FeeModelKind=null,string? DustDefinitionReference=null,string? FullUseCostDefinitionReference=null,string? OperationalServiceLimitsReference=null);
public sealed record BuilderSelection(decimal? AIMultiplier,long? CustomDust,long StorageBytes);
public sealed record PricingBreakdown(decimal AIAllowanceCost,decimal StorageCost,decimal InfrastructureCost,
    decimal SharedOperatingCost,decimal FixedFees,decimal ChargeDependentFees,decimal Margin,decimal Tax,
    decimal TaxExclusiveMonthlyPrice,decimal FinalMonthlyCharge,decimal PaymentFees=0,decimal BillingFees=0);
public sealed record SubscriptionQuote(Guid QuoteID,string CostModelVersion,string Currency,BuilderSelection Selection,
    long ActualMonthlyDust,PricingBreakdown Breakdown,DateTimeOffset ExpiresAt);
public sealed record PricingBlocker(string Field,string Reason);
public sealed record PricingResult(SubscriptionQuote? Quote,IReadOnlyList<PricingBlocker> Blockers);

/// <summary>Trusted configurable calculator shared by option displays, combined quotes, custom allocation and checkout.</summary>
public sealed class SubscriptionBuilder(ApprovedCostModel model)
{
    public const decimal TargetMargin=0.15m;
    public static IReadOnlyList<decimal> AIMultipliers { get; }=Array.AsReadOnly(new[]{0.5m,1m,3m,5m,15m,25m,50m,100m});
    public static IReadOnlyList<long> StorageOptionsBytes { get; }=Array.AsReadOnly(new[]{20L,100,250,500,1000,3000,5000,15000}.Select(gb=>gb*1_000_000_000L).ToArray());
    public PricingResult Quote(BuilderSelection selection)
    {
        try { return Calculate(selection); }
        catch(OverflowException){return new(null,[new("PriceRange","The requested allowance or cost calculation exceeds the representable monetary range.")]);}
    }
    private PricingResult Calculate(BuilderSelection selection)
    {
        var blockers=ValidateModel().ToList();
        if(selection.AIMultiplier is not null && selection.CustomDust is not null)
            blockers.Add(new("AISelection","Choose a multiplier or Custom Dust, not both."));
        if(selection.AIMultiplier is null && selection.CustomDust is null)
            blockers.Add(new("AISelection","An AI usage selection is required."));
        if(selection.AIMultiplier is {} multiplier && !AIMultipliers.Contains(multiplier))
            blockers.Add(new("AIMultiplier","Select a listed multiplier; other quantities use Custom Dust."));
        if(selection.CustomDust is <0 || selection.StorageBytes<0)blockers.Add(new("Resources","Resource quantities cannot be negative."));
        if(blockers.Count>0)return new(null,blockers);
        var unroundedDust=selection.AIMultiplier is {} factor ? model.DustPer1x!.Value*factor : selection.CustomDust!.Value;
        if(decimal.Truncate(unroundedDust)!=unroundedDust || unroundedDust>long.MaxValue)
            return new(null,[new("DustPer1x","Selected multiplier must resolve to an integral representable Dust allowance.")]);
        var dust=(long)unroundedDust;
        if(dust>model.MaximumMonthlyDust || selection.StorageBytes>model.MaximumStorageBytes)
            return new(null,[new("ServiceLimits","Selected quantities exceed the approved full-allowance service limits.")]);
        var ai=dust*model.AICostPerDust!.Value;var storage=selection.StorageBytes*model.StorageCostPerByte!.Value;
        var infrastructure=dust*model.InfrastructureCostPerDust!.Value+selection.StorageBytes*model.InfrastructureCostPerByte!.Value;
        var operating=model.SharedOperatingCost!.Value;var fixedFees=model.PaymentFixedFee!.Value+model.BillingFixedFee!.Value;
        var feeRate=model.PaymentFeeRate!.Value+model.BillingFeeRate!.Value;var taxRate=model.TaxRate!.Value;
        var paymentInclusive=model.PaymentFeeAppliesTaxInclusiveCharge??model.PercentageFeesApplyTaxInclusiveCharge!.Value;
        var billingInclusive=model.BillingFeeAppliesTaxInclusiveCharge??model.PercentageFeesApplyTaxInclusiveCharge!.Value;
        var denominator=1-TargetMargin-model.PaymentFeeRate.Value*(paymentInclusive?1+taxRate:1)-model.BillingFeeRate.Value*(billingInclusive?1+taxRate:1);
        if(denominator<=0)return new(null,[new("FeeRates","Fees and target margin leave no positive revenue denominator.")]);
        var cost=ai+storage+infrastructure+operating;
        // Round up to currency minor units so rounding never erodes the requested minimum margin.
        var price=decimal.Ceiling((cost+fixedFees)/denominator*100)/100;
        if(price==0)return new(null,[new("ZeroCharge","This configured paid builder calculation has zero revenue, so a 15% margin is undefined. Existing free/local entitlements are unchanged; configure an explicit billing treatment.")]);
        var tax=decimal.Round(price*taxRate,2,MidpointRounding.AwayFromZero);
        var finalCharge=price+tax;
        decimal RoundFee(decimal fee)=>model.FeeRounding switch {ChargeFeeRounding.NearestMinorUnit=>decimal.Round(fee,2,MidpointRounding.AwayFromZero),ChargeFeeRounding.CeilingMinorUnit=>decimal.Ceiling(fee*100)/100,_=>fee};
        decimal Fees(decimal basePrice,decimal final)=>RoundFee(model.PaymentFeeRate.Value*(paymentInclusive?final:basePrice))+RoundFee(model.BillingFeeRate.Value*(billingInclusive?final:basePrice));
        var dependentFees=Fees(price,finalCharge);
        var margin=price-cost-fixedFees-dependentFees;
        // Tax rounding can affect fees on the final inclusive charge. Correct by cents using the same full-use model.
        var adjustments=0;
        while(price>0&&margin/price<TargetMargin)
        {if(++adjustments>128)return new(null,[new("FeeModel","This fee/tax model cannot reach the target margin within bounded currency rounding; revise the approved model/service limits.")]);
            price+=0.01m;tax=decimal.Round(price*taxRate,2,MidpointRounding.AwayFromZero);finalCharge=price+tax;
            dependentFees=Fees(price,finalCharge);margin=price-cost-fixedFees-dependentFees;}
        return new(new(Guid.NewGuid(),model.Version,model.Currency,selection,dust,
            new(ai,storage,infrastructure,operating,fixedFees,dependentFees,margin,tax,price,finalCharge,
                model.PaymentFixedFee.Value+RoundFee(model.PaymentFeeRate.Value*(paymentInclusive?finalCharge:price)),
                model.BillingFixedFee.Value+RoundFee(model.BillingFeeRate.Value*(billingInclusive?finalCharge:price))),DateTimeOffset.UtcNow.AddMinutes(15)),[]);
    }
    public IReadOnlyList<PricingResult> AIOptionQuotes(long selectedStorageBytes)=>AIMultipliers.Select(m=>Quote(new(m,null,selectedStorageBytes))).ToArray();
    public IReadOnlyList<PricingResult> StorageOptionQuotes(decimal? multiplier,long? customDust)=>StorageOptionsBytes.Select(b=>Quote(new(multiplier,customDust,b))).ToArray();
    public PricingResult RevalidateForCheckout(SubscriptionQuote quote)
    {
        if(quote.ExpiresAt<=DateTimeOffset.UtcNow || quote.CostModelVersion!=model.Version || quote.Currency!=model.Currency)
            return new(null,[new("Quote","Quote expired or its authoritative cost model changed.")]);
        var fresh=Quote(quote.Selection);
        if(fresh.Quote is {} actual && (actual.ActualMonthlyDust!=quote.ActualMonthlyDust||actual.Breakdown!=quote.Breakdown))
            return new(null,[new("Quote","Quote contents do not match the server calculation.")]);
        return fresh.Quote is null?fresh:new(quote,[]);
    }
    private IEnumerable<PricingBlocker> ValidateModel()
    {
        if(model.FeeModelKind!="FlatFixedAndPercentage")yield return new("FeeModelKind","Explicitly approve FlatFixedAndPercentage. Minimum, capped, tiered and currency-conversion fee models require a corresponding supported calculator; they cannot be represented as flat rates.");
        foreach(var definition in new[]{("DustDefinitionReference",model.DustDefinitionReference),("FullUseCostDefinitionReference",model.FullUseCostDefinitionReference),("OperationalServiceLimitsReference",model.OperationalServiceLimitsReference)})
            if(string.IsNullOrWhiteSpace(definition.Item2))yield return new(definition.Item1,"Configure the approved immutable definition/provenance covering actual 1x Dust, full purchased-allowance costing and operational workload/duration/IO/egress service limits where applicable; selection ceilings alone do not establish cost coverage.");
        if(!model.Approved)yield return new("ApprovedCostModel","An approved all-in cost model is required.");
        if(string.IsNullOrWhiteSpace(model.Version))yield return new("Version","Cost model version is required.");
        if(model.Currency is not ("GBP" or "USD" or "EUR"))yield return new("Currency","Configure a supported currency with two decimal minor units.");
        if(model.DustPer1x is null or <=0)yield return new("DustPer1x","Define actual monthly AI Dust for 1x.");
        var fields=new Dictionary<string,decimal?>{["AICostPerDust"]=model.AICostPerDust,["StorageCostPerByte"]=model.StorageCostPerByte,
            ["InfrastructureCostPerDust"]=model.InfrastructureCostPerDust,["InfrastructureCostPerByte"]=model.InfrastructureCostPerByte,
            ["SharedOperatingCost"]=model.SharedOperatingCost,["PaymentFixedFee"]=model.PaymentFixedFee,["BillingFixedFee"]=model.BillingFixedFee,
            ["PaymentFeeRate"]=model.PaymentFeeRate,["BillingFeeRate"]=model.BillingFeeRate,["TaxRate"]=model.TaxRate};
        foreach(var field in fields)if(field.Value is null or <0)yield return new(field.Key,"Configure a nonnegative approved cost/rate; zero must be explicit.");
        if(model.PercentageFeesApplyTaxInclusiveCharge is null && (model.PaymentFeeAppliesTaxInclusiveCharge is null || model.BillingFeeAppliesTaxInclusiveCharge is null))yield return new("FeeBasis","Specify whether charge-dependent fees apply to the final tax-inclusive charge.");
        if(model.FeeRounding is null || !Enum.IsDefined(model.FeeRounding.Value))yield return new("FeeRounding","Configure a supported actual payment/billing charge-dependent fee rounding policy.");
        foreach(var fee in new[]{("PaymentFixedFee",model.PaymentFixedFee),("BillingFixedFee",model.BillingFixedFee)})
            if(fee.Item2 is {} value && decimal.Round(value,2)!=value)yield return new(fee.Item1,"Fixed fees must be explicitly denominated in supported currency minor units; fractional-minor-unit fixed fee models are unsupported.");
        if(model.MaximumMonthlyDust is null or <=0 || model.MaximumStorageBytes is null or <=0)
            yield return new("ServiceLimits","Define full-allowance supported AI and storage quantities.");
    }
}
