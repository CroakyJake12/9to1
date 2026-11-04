using Haven.UI.Emoji;

namespace CakeOS.Apps.Boards.App;

/// <summary>One emoji as the picker shows it: the sequence, its CLDR name and the search hint.</summary>
public sealed record EmojiIconChoiceView(string Character, string Name, string Group);

/// <summary>A named run of emoji, drawn with a heading in the picker.</summary>
public sealed record EmojiIconSectionView(string Heading, IReadOnlyList<EmojiIconChoiceView> Items);

/// <summary>
/// The emoji tab of the Boards icon picker, kept separate from the view model so its rules can be tested
/// without a surface. Three rules come straight from the reference application the owner showed, and one is
/// a deliberate departure:
/// <list type="bullet">
/// <item>categories come from the Unicode groups of the shared catalogue, in catalogue order;</item>
/// <item>a chosen skin tone re-renders every modifier-capable row, and only rows that really have the
/// variant in this Unicode version - the request can never invent a sequence;</item>
/// <item>recent picks form a leading section, like the donor's Recent category;</item>
/// <item>departure: an empty search result reports itself. The donor leaves a category heading with nothing
/// under it and says nothing, which is the one behaviour in that picker not being copied.</item>
/// </list>
/// A category is capped so opening the popover cannot build thousands of controls at once on a machine that
/// is already carrying the resident model host; the cap is surfaced as a section title suffix and as a
/// "narrow it down" hint, so the limit is visible rather than something the owner has to discover.
/// </summary>
public static class IconPickerEmojiModel
{
    public const int SectionCap = 40;
    public const string RecentTitle = "Recent";

    /// <summary>Builds the visible sections for a query and skin tone.</summary>
    public static IReadOnlyList<EmojiIconSectionView> Sections(string? query, string? skinTone,
        IReadOnlyList<string> recentSequences)
    {
        var trimmed = query?.Trim();
        // Browsing shows one row per emoji; a chosen skin tone is applied to that row. Searching by name may
        // legitimately ask for a spelling ("hand: dark skin tone"), so a query keeps the variant rows.
        var matched = string.IsNullOrEmpty(trimmed)
            ? EmojiCatalogue.Entries.Where(entry => !EmojiCatalogue.IsSkinToneVariant(entry)).ToList()
            : (System.Collections.Generic.IReadOnlyList<EmojiEntry>)EmojiCatalogue.Search(trimmed);
        if (matched.Count == 0) return Array.Empty<EmojiIconSectionView>();

        var sections = new List<EmojiIconSectionView>();
        if (string.IsNullOrEmpty(trimmed) && recentSequences.Count > 0)
        {
            var recent = recentSequences
                .Select(sequence => EmojiCatalogue.Find(sequence))
                .Where(entry => entry is not null)
                .Select(entry => View(entry!, skinTone))
                .ToArray();
            if (recent.Length > 0) sections.Add(new EmojiIconSectionView(RecentTitle, recent));
        }

        foreach (var group in matched.GroupBy(entry => entry.Group, StringComparer.Ordinal))
        {
            var rows = group.Take(SectionCap).ToArray();
            if (rows.Length == 0) continue;
            var title = rows.Length < group.Count()
                ? $"{group.Key} · first {rows.Length} of {group.Count()} — narrow it down by searching"
                : group.Key;
            sections.Add(new EmojiIconSectionView(title, rows.Select(entry => View(entry, skinTone)).ToArray()));
        }
        return sections;
    }

    /// <summary>True when the query asked for something and nothing answered it.</summary>
    public static bool HasNoMatches(string? query) =>
        !string.IsNullOrWhiteSpace(query) && EmojiCatalogue.Search(query).Count == 0;

    /// <summary>
    /// A random emoji, honouring the current skin tone, as the donor's shuffle control behaves. Drawn from
    /// the full catalogue, not the visible window, so the control means what it says.
    /// </summary>
    public static EmojiIconChoiceView PickRandom(string? skinTone)
    {
        var entries = EmojiCatalogue.Entries;
        return View(entries[Random.Shared.Next(entries.Count)], skinTone);
    }

    private static EmojiIconChoiceView View(EmojiEntry entry, string? skinTone) =>
        new(EmojiCatalogue.Resolve(entry, skinTone), entry.Name, entry.Group);
}
