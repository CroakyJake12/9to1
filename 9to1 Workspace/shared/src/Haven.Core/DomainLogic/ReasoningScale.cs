namespace Haven.Core;

/// <summary>
/// Keeps legacy four-step effort labels consistent and projects actual provider level identities
/// to discrete display presets. Display percentages never imply provider capabilities.
/// </summary>
public static class ReasoningScale
{
    public const int MinimumPercentage = 25;
    public const int MaximumPercentage = 100;
    public const int StepSize = 25;

    public static IReadOnlyList<int> Percentages { get; } =
        Array.AsReadOnly(new[] { MinimumPercentage, 50, 75, MaximumPercentage });

    /// <summary>
    /// Preserves the provider's advertised ordering and exact level identities. Missing metadata
    /// produces no presets; it never invents a reasoning control or an enabled/disabled switch.
    /// </summary>
    public static IReadOnlyList<ReasoningLevelPreset> CreatePresets(IReadOnlyList<string>? supportedLevels)
    {
        if (supportedLevels is null || supportedLevels.Count == 0) return [];
        var levels = supportedLevels.ToArray();
        if (levels.Any(string.IsNullOrWhiteSpace) || levels.Distinct(StringComparer.Ordinal).Count() != levels.Length)
            throw new ArgumentException("Advertised reasoning levels require distinct nonempty identities.", nameof(supportedLevels));

        // Preserve distinct, legible labels even for providers advertising more than 100 levels.
        var decimals = Math.Max(0, (int)Math.Ceiling(Math.Log10(levels.Length)) - 2);
        return Array.AsReadOnly(levels.Select((level, ordinal) => new ReasoningLevelPreset(level, ordinal,
            levels.Length == 3 ? new[] { 35m, 70m, 100m }[ordinal]
                : decimal.Round(100m * (ordinal + 1) / levels.Length, decimals, MidpointRounding.AwayFromZero))).ToArray());
    }

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
}

/// <summary>A display label bound to one actual advertised level; selection retains LevelId.</summary>
public sealed record ReasoningLevelPreset(string LevelId, int Ordinal, decimal Percentage)
{
    public string Label => Percentage.ToString("0.########", System.Globalization.CultureInfo.InvariantCulture) + "%";
}
