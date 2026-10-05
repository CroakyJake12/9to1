using Dulche.Runtime.Agents;
using Xunit;

namespace Dulche.Runtime.Agents.Tests;

public sealed class AgentBudgetConservativeChargeTests
{
    [Fact]
    public void Mixed_usage_preserves_measured_usage_and_conservative_reservation_charge_in_each_ancestor()
    {
        var ledger = new AgentBudgetLedger();
        Assert.True(ledger.RegisterRoot("root", new(MaxTokens: 100, MaxCost: 10)).Succeeded);
        Assert.True(ledger.RegisterChild("root", "child", new(MaxTokens: 100, MaxCost: 10)).Succeeded);
        Assert.True(ledger.Reserve("child", "measured", new(Tokens: 20, Cost: 3), DateTimeOffset.UnixEpoch).Succeeded);
        var measured = new BudgetSettlement(UsageValue<long>.Measured(15), UsageValue<TimeSpan>.Empty(),
            UsageValue<long>.Empty(), UsageValue<long>.Empty(), UsageValue<decimal>.Measured(2.5m));
        Assert.True(ledger.Settle("child", "measured", measured, DateTimeOffset.UnixEpoch).Succeeded);
        Assert.True(ledger.Reserve("child", "omitted", new(Tokens: 40, Cost: 3), DateTimeOffset.UnixEpoch).Succeeded);
        var omitted = new BudgetSettlement(UsageValue<long>.Unavailable("provider omitted usage"), UsageValue<TimeSpan>.Empty(),
            UsageValue<long>.Empty(), UsageValue<long>.Empty(), UsageValue<decimal>.Unavailable("provider omitted cost"));
        var first = ledger.Settle("child", "omitted", omitted, DateTimeOffset.UnixEpoch);
        var repeated = ledger.Settle("child", "omitted", omitted, DateTimeOffset.UnixEpoch.AddSeconds(1));
        Assert.True(first.Succeeded);
        Assert.Equal(first.Value, repeated.Value);
        foreach (var scope in new[] { "child", "root" })
        {
            var usage = ledger.Snapshot(scope).Value!;
            Assert.Equal(UsageAvailability.ProviderUnavailable, usage.Tokens.Availability);
            Assert.Null(usage.Tokens.Value);
            Assert.Equal(55, usage.KnownTokenSubtotal);
            Assert.Equal(15, usage.MeasuredTokenSubtotal);
            Assert.Equal(40, usage.UnmeasuredTokenReservationCharge);
            Assert.Equal("measured-plus-unmeasured-reservation-charge", usage.KnownTokenSubtotalBasis);
            Assert.Null(usage.Cost.Value);
            Assert.Equal(UsageAvailability.ProviderUnavailable, usage.Cost.Availability);
            Assert.Equal(2.5m, usage.KnownCostSubtotal);
            Assert.Equal(2, usage.IncludedInvocationCount);
            Assert.Equal(0, usage.ReservedTokens);
        }
        Assert.Equal(AgentFailureCode.BudgetExceeded,
            ledger.Reserve("child", "third", new(Tokens: 46), DateTimeOffset.UnixEpoch).Error?.Code);
    }

    [Fact]
    public void Measured_only_charge_has_no_unmeasured_reservation_provenance()
    {
        var ledger = new AgentBudgetLedger();
        Assert.True(ledger.RegisterRoot("root", new(MaxTokens: 100)).Succeeded);
        Assert.True(ledger.Reserve("root", "one", new(Tokens: 40), DateTimeOffset.UnixEpoch).Succeeded);
        var settled = new BudgetSettlement(UsageValue<long>.Measured(12), UsageValue<TimeSpan>.Empty(),
            UsageValue<long>.Empty(), UsageValue<long>.Empty(), UsageValue<decimal>.Empty());
        Assert.True(ledger.Settle("root", "one", settled, DateTimeOffset.UnixEpoch).Succeeded);
        var usage = ledger.Snapshot("root").Value!;
        Assert.Equal(12, usage.Tokens.Value);
        Assert.Equal(12, usage.KnownTokenSubtotal);
        Assert.Equal(12, usage.MeasuredTokenSubtotal);
        Assert.Equal(0, usage.UnmeasuredTokenReservationCharge);
        Assert.Equal("measured-usage", usage.KnownTokenSubtotalBasis);
    }
}
