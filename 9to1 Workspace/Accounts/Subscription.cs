using System.Text.Json;

namespace NineToOne.Accounts;

public enum PlanLevel { Free, Boost, Plus, Pro, Ultra, UltraPlus, Max }
public sealed record PlanThreshold(string PlanID, PlanLevel PlanLevel, decimal PriceMonthly, string ModelBand);
public sealed record Resources(long AIDustAllocated, long StorageAllocatedBytes, int HostedSiteLimit,
    int SinglePageSiteLimit, IReadOnlyDictionary<string, JsonElement> AdditionalResources);
public sealed record SubscriptionState(Guid AccountID, string? PresetID, int? PresetVersion, bool IsCustomised,
    decimal EntitlementPriceMonthly, Resources Resources, long Revision);
public sealed record Entitlements(IReadOnlyList<PlanThreshold> EffectiveThresholds, string ModelBand,
    bool PersonalAPI, Resources Resources);
public sealed record PresetDefinition(string PresetID, int Version, PlanLevel Level, decimal Price,
    Resources? Allocation, int? KnownFullSiteLimit, int? KnownSinglePageLimit);

/// <summary>Trusted backend evaluation. UI labels never grant access; allocations remain independent.</summary>
public static class SubscriptionPolicy
{
    public const string ModelBandVersion = "2026-09-29";
    public static readonly IReadOnlyList<PlanThreshold> Thresholds = Array.AsReadOnly(new[] {
        new PlanThreshold("free", PlanLevel.Free, 0m, "free"),
        new PlanThreshold("boost", PlanLevel.Boost, 7.99m, "boost"),
        new PlanThreshold("plus", PlanLevel.Plus, 19.99m, "boost"),
        new PlanThreshold("pro", PlanLevel.Pro, 34.99m, "pro"),
        new PlanThreshold("ultra", PlanLevel.Ultra, 69.99m, "ultra"),
        new PlanThreshold("ultra-plus", PlanLevel.UltraPlus, 99.99m, "ultra"),
        new PlanThreshold("max", PlanLevel.Max, 149.99m, "ultra") });
    // Null allocations are explicit unresolved commercial inputs, never invented quotas.
    public static IReadOnlyList<PresetDefinition> ListPresets() => Thresholds.Select(t =>
        new PresetDefinition(t.PlanID, 1, t.PlanLevel, t.PriceMonthly, null,
            t.PlanLevel == PlanLevel.Boost ? 1 : t.PlanLevel == PlanLevel.Plus ? 3 : null,
            t.PlanLevel == PlanLevel.Boost ? 3 : t.PlanLevel == PlanLevel.Plus ? 5 : null)).ToArray();
    public static Entitlements Evaluate(SubscriptionState state)
    {
        Validate(state.Resources);
        if (state.EntitlementPriceMonthly < 0) throw new ArgumentOutOfRangeException(nameof(state));
        var unlocked = Thresholds.Where(t => state.EntitlementPriceMonthly >= t.PriceMonthly).ToArray();
        return new(unlocked, unlocked[^1].ModelBand,
            state.Resources.AIDustAllocated >= 80_000, state.Resources);
    }
    public static void Validate(Resources resources)
    {
        if (resources.AIDustAllocated < 0 || resources.StorageAllocatedBytes < 0 ||
            resources.HostedSiteLimit < 0 || resources.SinglePageSiteLimit < 0)
            throw new ArgumentOutOfRangeException(nameof(resources));
    }
    public static bool FreeSiteOnline(DateTimeOffset createdAt, DateTimeOffset lastSignIn, DateTimeOffset now)
    {
        if (lastSignIn > now || createdAt > now || lastSignIn < createdAt) return false;
        return now < createdAt.AddDays(120) ? now <= lastSignIn.AddDays(30) : now <= lastSignIn.AddYears(1);
    }
}
