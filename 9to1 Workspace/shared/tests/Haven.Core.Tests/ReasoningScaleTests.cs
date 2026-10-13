using Haven.Core;

namespace Haven.Core.Tests;

public sealed class ReasoningScaleTests
{
    [Theory]
    [InlineData(3, "35%,70%,100%")]
    [InlineData(4, "25%,50%,75%,100%")]
    [InlineData(5, "20%,40%,60%,80%,100%")]
    [InlineData(6, "17%,33%,50%,67%,83%,100%")]
    public void AdvertisedLevelsKeepTheirExactOrderAndCanonicalLabels(int count, string expectedLabels)
    {
        var levels = Enumerable.Range(0, count).Select(index => "provider-level-" + index).ToArray();

        var presets = ReasoningScale.CreatePresets(levels);

        Assert.Equal(expectedLabels, string.Join(',', presets.Select(preset => preset.Label)));
        Assert.Equal(levels, presets.Select(preset => preset.LevelId));
        Assert.Equal(Enumerable.Range(0, count), presets.Select(preset => preset.Ordinal));
    }

    [Fact]
    public void UnknownAndEmptyCapabilitiesDoNotInventPresets()
    {
        Assert.Empty(ReasoningScale.CreatePresets(null));
        Assert.Empty(ReasoningScale.CreatePresets([]));
    }

    [Fact]
    public void InvalidOrDuplicateLevelIdentitiesAreRejected()
    {
        Assert.Throws<ArgumentException>(() => ReasoningScale.CreatePresets(["low", "low"]));
        Assert.Throws<ArgumentException>(() => ReasoningScale.CreatePresets(["low", " "]));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(101)]
    [InlineData(1001)]
    public void OtherCardinalitiesRemainBijectiveAndSnapshotTheProviderList(int count)
    {
        var levels = Enumerable.Range(0, count).Select(index => "level-" + index).ToArray();
        var presets = ReasoningScale.CreatePresets(levels);
        levels[0] = "changed-after-capture";

        Assert.Equal("level-0", presets[0].LevelId);
        Assert.Equal(count, presets.Select(preset => preset.Percentage).Distinct().Count());
        Assert.Equal(100m, presets[^1].Percentage);
        Assert.Equal(presets.Select(preset => preset.Percentage).Order(), presets.Select(preset => preset.Percentage));
    }

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
}
