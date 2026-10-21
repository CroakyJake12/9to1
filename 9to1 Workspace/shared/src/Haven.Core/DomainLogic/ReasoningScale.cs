using System.Linq;

namespace Haven.Core;

/// <summary>
/// Keeps the user-facing reasoning percentages and the provider effort enum in sync.
/// The current product scale has exactly four steps: 25%, 50%, 75%, and 100%.
/// </summary>
public static class ReasoningScale
{
    public const int MinimumPercentage = 25;
    public const int MaximumPercentage = 100;
    public const int StepSize = 25;

    public static IReadOnlyList<int> Percentages { get; } =
        [MinimumPercentage, 50, 75, MaximumPercentage];

    public static int ToPercentage(EffortLevel effort) => effort switch
    {
        EffortLevel.Low => 25,
        EffortLevel.Medium => 50,
        EffortLevel.High => 75,
        EffortLevel.Max => 100,
        _ => throw new ArgumentOutOfRangeException(nameof(effort), effort, null)
    };

    public static EffortLevel FromPercentage(double percentage) =>
        SnapPercentage(percentage) switch
        {
            25 => EffortLevel.Low,
            50 => EffortLevel.Medium,
            75 => EffortLevel.High,
            100 => EffortLevel.Max,
            _ => throw new InvalidOperationException("Reasoning percentage snapping returned an unsupported value.")
        };

    public static int SnapPercentage(double percentage)
    {
        if (double.IsNaN(percentage) || double.IsInfinity(percentage))
        {
            throw new ArgumentOutOfRangeException(nameof(percentage));
        }

        var clamped = Math.Clamp(percentage, MinimumPercentage, MaximumPercentage);
        var step = (int)Math.Round(clamped / StepSize, MidpointRounding.AwayFromZero);
        return Math.Clamp(step * StepSize, MinimumPercentage, MaximumPercentage);
    }

    /// <summary>
    /// The large-model, accuracy-preserving runtime profile is reserved for the top step.
    /// This is the discrete equivalent of the previous 80-100% threshold.
    /// </summary>
    public static bool UsesHighReasoningRuntime(EffortLevel effort) =>
        effort == EffortLevel.Max;

    /// <summary>
    /// The bijective tier percentages for a model's advertised reasoning-effort level count.
    /// The exact model/route advertises how many levels it supports; the selected level's ordinal
    /// maps one-to-one onto a tier so the top-bar percentage is never fabricated or derived from
    /// CPU usage, quota, context fill or health. 3/4/5-level scales are fixed by product definition;
    /// any other sufficient count (&gt;= 2) gets distinct, evenly distributed labels via a stable
    /// ID/ordinal bijection (round(100*(i+1)/N)). Counts too small (&lt; 2) or whose even
    /// distribution would collide remain a truthful unknown (empty), never an invented scale.
    /// </summary>
    public static IReadOnlyList<int> TierPercentages(int levelCount)
    {
        if (levelCount < 2) return [];
        IReadOnlyList<int>? fixedTiers = levelCount switch
        {
            3 => [35, 70, 100],
            4 => [25, 50, 75, 100],
            5 => [20, 40, 60, 80, 100],
            _ => null
        };
        if (fixedTiers is not null) return fixedTiers;
        // General N: distinct, evenly distributed labels with a stable bijection.
        var tiers = new int[levelCount];
        for (var i = 0; i < levelCount; i++)
            tiers[i] = (int)Math.Round(100.0 * (i + 1) / levelCount, MidpointRounding.AwayFromZero);
        // Distinctness is required for a stable ID/ordinal bijection; a colliding (pathological)
        // scale stays a truthful unknown rather than presenting ambiguous labels.
        return tiers.Distinct().Count() == levelCount ? tiers : [];
    }

    /// <summary>Resolves the bijective tier percentage for an ordinal (0-based level index) on a
    /// model's advertised level count. Returns false when the count is not a known scale or the
    /// ordinal is out of range, so callers render a truthful unknown rather than a guess.</summary>
    public static bool TryTierPercentage(int levelCount, int ordinal, out int percentage)
    {
        var tiers = TierPercentages(levelCount);
        if (ordinal < 0 || ordinal >= tiers.Count) { percentage = 0; return false; }
        percentage = tiers[ordinal];
        return true;
    }
}
