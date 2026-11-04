using Haven.Infrastructure.Emoji;

namespace Haven.Infrastructure.Tests;

/// <summary>
/// Pins the Unicode emoji catalogue that both the chat composer picker and the Boards icon picker read.
/// These are the properties the pickers depend on: the dataset is real Unicode data rather than an
/// invented list, search answers in terms of names and groups, and a skin-tone request can only ever
/// produce a sequence this Unicode version actually qualifies.
/// </summary>
public sealed class EmojiCatalogueTests
{
    [Fact]
    public void Catalogue_is_the_full_fully_qualified_unicode_17_set_in_group_order()
    {
        Assert.True(EmojiCatalogue.Entries.Count > 3_900,
            $"expected the full Unicode set, observed {EmojiCatalogue.Entries.Count}");
        Assert.All(EmojiCatalogue.Entries, entry =>
        {
            Assert.False(string.IsNullOrEmpty(entry.Text));
            Assert.False(string.IsNullOrEmpty(entry.Name));
            Assert.False(string.IsNullOrEmpty(entry.Group));
            Assert.Equal(entry.Text, string.Concat(entry.CodePoints.Split(' ')
                .Select(hex => char.ConvertFromUtf32(Convert.ToInt32(hex, 16)))));
        });
        Assert.Contains("Smileys & Emotion", EmojiCatalogue.Groups);
        Assert.Contains("Flags", EmojiCatalogue.Groups);
        Assert.Equal(EmojiCatalogue.Groups.Count, EmojiCatalogue.Groups.Distinct().Count());
        // File order is group order, which is what lets a category listing be built without re-sorting.
        Assert.Equal("Smileys & Emotion", EmojiCatalogue.Entries[0].Group);
    }

    [Fact]
    public void Search_matches_names_and_groups_not_only_a_fixed_vocabulary()
    {
        Assert.Contains(EmojiCatalogue.Search("book"), entry => entry.Name == "open book");
        // The donor picker showed a category header with nothing under it for this query; the same
        // query here must answer with real rows, because the group name itself matches.
        var scales = EmojiCatalogue.Search("scales");
        Assert.NotEmpty(scales);
        Assert.Contains(scales, entry => entry.Name == "balance scale");
        Assert.NotEmpty(EmojiCatalogue.Search("SMILEY"));
        Assert.Empty(EmojiCatalogue.Search("zzzzzzzzzz-not-an-emoji"));
        Assert.Equal(EmojiCatalogue.Entries.Count, EmojiCatalogue.Search(null).Count);
    }

    [Fact]
    public void Membership_is_exact_so_an_icon_slot_cannot_carry_arbitrary_text()
    {
        Assert.True(EmojiCatalogue.Contains("⚖"));
        Assert.True(EmojiCatalogue.Contains("👨‍👩‍👧"));
        Assert.False(EmojiCatalogue.Contains("hello"));
        Assert.False(EmojiCatalogue.Contains("a⚖"));
        Assert.False(EmojiCatalogue.Contains(""));
        Assert.False(EmojiCatalogue.Contains(null));
    }

    [Fact]
    public void Skin_tone_only_ever_resolves_to_a_sequence_unicode_qualifies()
    {
        var waving = EmojiCatalogue.Search("waving hand").First(entry => entry.Name == "waving hand");
        Assert.True(EmojiCatalogue.SupportsSkinTone(waving));
        Assert.Equal(4, EmojiCatalogue.VariantsOf(waving).Count);
        var medium = EmojiCatalogue.Resolve(waving, "🏽");
        Assert.Equal("1F44B 1F3FD", string.Join(' ', medium.EnumerateRunes()
            .Select(rune => rune.Value.ToString("X"))));
        Assert.True(EmojiCatalogue.Contains(medium));
        // No tone, or an unmodifiable sequence: the stored value is exactly what was offered.
        Assert.Equal(waving.Text, EmojiCatalogue.Resolve(waving, null));
        var book = EmojiCatalogue.Search("open book").First(entry => entry.Name == "open book");
        Assert.False(EmojiCatalogue.SupportsSkinTone(book));
        Assert.Equal(book.Text, EmojiCatalogue.Resolve(book, "🏽"));
    }

    [Fact]
    public void Categories_partition_the_same_entries_the_search_engine_uses()
    {
        var objects = EmojiCatalogue.InGroup("Objects");
        Assert.NotEmpty(objects);
        Assert.All(objects, entry => Assert.Equal("Objects", entry.Group, StringComparer.OrdinalIgnoreCase));
        Assert.Equal(EmojiCatalogue.Entries.Count,
            EmojiCatalogue.Groups.Sum(group => EmojiCatalogue.InGroup(group).Count));
        Assert.Empty(EmojiCatalogue.InGroup("No such category"));
    }
}
