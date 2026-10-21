using System.Linq;
using HavenOS.Home;
using Xunit;

namespace HavenOS.Home.Tests;

/// <summary>
/// Canonical label projection for advertised reasoning-level IDs. The label derives ONLY from the
/// level's ordinal and the model's advertised level count (3=35/70/100, 4=25/50/75/100,
/// 5=20/40/60/80/100). Unknown counts and out-of-range ordinals stay a truthful unknown (null), never
/// a fake 100 or an invented on/off or enum mapping.
/// </summary>
public sealed class HomeModelPickerReasoningLabelsTests
{
    [Theory]
    [InlineData(3, 0, "35%")]
    [InlineData(3, 1, "70%")]
    [InlineData(3, 2, "100%")]
    [InlineData(4, 0, "25%")]
    [InlineData(4, 1, "50%")]
    [InlineData(4, 2, "75%")]
    [InlineData(4, 3, "100%")]
    [InlineData(5, 0, "20%")]
    [InlineData(5, 1, "40%")]
    [InlineData(5, 2, "60%")]
    [InlineData(5, 3, "80%")]
    [InlineData(5, 4, "100%")]
    public void Label_for_level_maps_ordinal_to_canonical_tier(int count, int ordinal, string expected)
    {
        var ids = Enumerable.Range(0, count).Select(i => "lvl" + i).ToArray();
        var label = HomeModelPickerReasoningLabels.LabelForLevel(ids, ids[ordinal]);
        Assert.Equal(expected, label);
    }

    [Theory]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void Out_of_range_unknown_and_blank_levels_stay_truthful_unknown(int count)
    {
        var ids = Enumerable.Range(0, count).Select(i => "lvl" + i).ToArray();
        Assert.Null(HomeModelPickerReasoningLabels.LabelForLevel(ids, "nope"));
        Assert.Null(HomeModelPickerReasoningLabels.LabelForLevel(ids, null));
        Assert.Null(HomeModelPickerReasoningLabels.LabelForLevel(ids, ""));
        Assert.Null(HomeModelPickerReasoningLabels.LabelForLevel(ids, "   "));
    }

    [Fact]
    public void Empty_or_null_advertised_ids_yield_truthful_unknown()
    {
        Assert.Null(HomeModelPickerReasoningLabels.LabelForLevel([], "lvl0"));
        Assert.Null(HomeModelPickerReasoningLabels.LabelForLevel(null!, "lvl0"));
        Assert.Null(HomeModelPickerReasoningLabels.LabelForLevel(["lvl0"], null));
    }
}
