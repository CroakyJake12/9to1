using System.Linq;
using Haven.Core;

namespace Haven.Core.Tests;

public sealed class ReasoningScaleTests
{
    [Theory]
    [InlineData(EffortLevel.Low, 25)]
    [InlineData(EffortLevel.Medium, 50)]
    [InlineData(EffortLevel.High, 75)]
    [InlineData(EffortLevel.Max, 100)]
    public void EffortMapsToExpectedPercentage(EffortLevel effort, int percentage)
    {
        Assert.Equal(percentage, ReasoningScale.ToPercentage(effort));
        Assert.Equal(effort, ReasoningScale.FromPercentage(percentage));
    }

    [Theory]
    [InlineData(1, 25)]
    [InlineData(37, 25)]
    [InlineData(38, 50)]
    [InlineData(62, 50)]
    [InlineData(63, 75)]
    [InlineData(87, 75)]
    [InlineData(88, 100)]
    [InlineData(140, 100)]
    public void SliderSnapsToFourSteps(double value, int expected)
    {
        Assert.Equal(expected, ReasoningScale.SnapPercentage(value));
    }

    [Fact]
    public void MaximumProfilePreservesRequestedContextAndPreloads()
    {
        var profile = LocalInferenceRuntimeProfile.Create(EffortLevel.Max, 131_072);

        Assert.Equal(131_072, profile.ContextLimit);
        Assert.True(profile.PreloadModel);
        Assert.Equal("45m", profile.KeepAlive);
        Assert.True(profile.PreserveExactWeights);
    }

    [Theory]
    [InlineData(EffortLevel.Low, 8_192)]
    [InlineData(EffortLevel.Medium, 16_384)]
    [InlineData(EffortLevel.High, 32_768)]
    public void LowerProfilesRemainLatencyBounded(EffortLevel effort, int expectedLimit)
    {
        var profile = LocalInferenceRuntimeProfile.Create(effort, 131_072);

        Assert.Equal(expectedLimit, profile.ContextLimit);
        Assert.False(profile.PreloadModel);
        Assert.True(profile.PreserveExactWeights);
    }

    [Theory]
    [InlineData(3, "35%", "70%", "100%")]
    [InlineData(4, "25%", "50%", "75%", "100%")]
    [InlineData(5, "20%", "40%", "60%", "80%", "100%")]
    public void Fixed_tier_scales_are_bijective(int levelCount, params string[] expected)
    {
        var tiers = ReasoningScale.TierPercentages(levelCount);
        Assert.Equal(levelCount, tiers.Count);
        Assert.Equal(expected.Select(e => int.Parse(e.TrimEnd('%'))), tiers);
        for (var ordinal = 0; ordinal < levelCount; ordinal++)
        {
            Assert.True(ReasoningScale.TryTierPercentage(levelCount, ordinal, out var percentage));
            Assert.Equal(tiers[ordinal], percentage);
        }
        Assert.False(ReasoningScale.TryTierPercentage(levelCount, levelCount, out _));
        Assert.False(ReasoningScale.TryTierPercentage(levelCount, -1, out _));
    }

    [Theory]
    [InlineData(2)]
    [InlineData(6)]
    [InlineData(8)]
    [InlineData(10)]
    public void Other_tier_scales_are_distinct_evenly_distributed_and_stable(int levelCount)
    {
        var tiers = ReasoningScale.TierPercentages(levelCount);
        Assert.Equal(levelCount, tiers.Count);
        // Distinct, strictly ascending, within (0,100], and ending at 100.
        Assert.Equal(levelCount, tiers.Distinct().Count());
        Assert.Equal(tiers.OrderBy(t => t), tiers);
        Assert.All(tiers, t => Assert.InRange(t, 1, 100));
        Assert.Equal(100, tiers[^1]);
        for (var ordinal = 0; ordinal < levelCount; ordinal++)
        {
            Assert.True(ReasoningScale.TryTierPercentage(levelCount, ordinal, out var percentage));
            Assert.Equal(tiers[ordinal], percentage);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void Too_small_or_invalid_scales_stay_unknown(int levelCount)
    {
        Assert.Empty(ReasoningScale.TierPercentages(levelCount));
        Assert.False(ReasoningScale.TryTierPercentage(levelCount, 0, out _));
    }
}
