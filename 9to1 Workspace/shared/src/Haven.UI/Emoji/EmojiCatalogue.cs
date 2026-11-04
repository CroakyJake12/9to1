using System.Globalization;

namespace Haven.UI.Emoji;

/// <summary>
/// One fully-qualified Unicode emoji sequence with its CLDR name and Unicode group. This is the single
/// emoji dataset Haven ships: the chat composer picker and the Boards icon picker both read it, so a
/// search for "book" finds the same rows and the same accessible names in both surfaces.
/// </summary>
/// <param name="Text">The grapheme cluster to insert or display, exactly as Unicode qualifies it.</param>
/// <param name="Name">CLDR name used for search and accessible naming ("open book").</param>
/// <param name="Group">Unicode group heading used for picker categories ("Objects").</param>
/// <param name="CodePoints">Uppercase hex code points separated by single spaces, as in the source data.</param>
public sealed record EmojiEntry(string Text, string Name, string Group, string CodePoints);

/// <summary>
/// The canonical Unicode 17 emoji catalogue, read once from the embedded <c>emoji-test-17.0.txt</c>.
/// Only <c>fully-qualified</c> sequences are exposed, so every entry is something a conformant font can
/// paint; minimally-qualified, unqualified and RGI-variation-selector forms in the same file are skipped.
/// Skin-tone variants are not synthesised here: the Unicode file already lists each variant as its own
/// fully-qualified sequence with its own CLDR name, and this catalogue groups them by their base sequence.
/// </summary>
public static class EmojiCatalogue
{
    /// <summary>The four Unicode skin-tone modifiers plus the explicit "none" choice.</summary>
    public static IReadOnlyList<string> SkinToneModifiers { get; } =
        ["🏻", "🏼", "🏽", "🏾", "🏿"];

    private static readonly Lazy<IReadOnlyList<EmojiEntry>> Loaded = new(Read);

    /// <summary>Every fully-qualified sequence, in Unicode file order (which is group order).</summary>
    public static IReadOnlyList<EmojiEntry> Entries => Loaded.Value;

    /// <summary>Distinct group names in first-appearance order, used as picker category headings.</summary>
    public static IReadOnlyList<string> Groups { get; } = Loaded.Value
        .Select(entry => entry.Group)
        .Distinct(StringComparer.Ordinal)
        .ToArray();

    /// <summary>True when the text is exactly one sequence this catalogue can display.</summary>
    public static bool Contains(string? text) =>
        !string.IsNullOrEmpty(text) && ByCodePoints.Value.ContainsKey(CanonicalKey(text));

    /// <summary>
    /// Looks a sequence up, accepting the unqualified form a user may paste and returning the
    /// fully-qualified sequence the catalogue actually lists, so stored icon data is always canonical.
    /// </summary>
    public static EmojiEntry? Find(string? text) =>
        string.IsNullOrEmpty(text) ? null
        : ByCodePoints.Value.TryGetValue(CanonicalKey(text), out var entry) ? entry : null;

    /// <summary>Entries whose group matches, case-insensitively; used to build a category section.</summary>
    public static IReadOnlyList<EmojiEntry> InGroup(string group) =>
        _byGroup.Value.TryGetValue(group, out var entries) ? entries : Array.Empty<EmojiEntry>();

    private static readonly Lazy<Dictionary<string, IReadOnlyList<EmojiEntry>>> _byGroup = new(() =>
        Loaded.Value.GroupBy(entry => entry.Group, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<EmojiEntry>)g.ToArray(), StringComparer.OrdinalIgnoreCase));

    private static readonly Lazy<Dictionary<string, EmojiEntry>> ByCodePoints = new(() =>
    {
        var map = new Dictionary<string, EmojiEntry>(StringComparer.Ordinal);
        foreach (var entry in Loaded.Value) map[entry.CodePoints] = entry;
        return map;
    });

    /// <summary>Search by CLDR name or group, and by literal sequence when the query is an emoji itself.</summary>
    public static IReadOnlyList<EmojiEntry> Search(string? query)
    {
        var trimmed = query?.Trim();
        if (string.IsNullOrEmpty(trimmed)) return Entries;
        return Entries.Where(entry =>
            entry.Name.Contains(trimmed, StringComparison.OrdinalIgnoreCase)
            || entry.Group.Contains(trimmed, StringComparison.OrdinalIgnoreCase)
            || entry.Text == trimmed).ToArray();
    }

    /// <summary>
    /// True when this sequence has skin-tone variants in the catalogue. Detection is data-driven: it looks
    /// for a listed sequence whose code points are this one plus a modifier, so a font or Unicode-version
    /// difference cannot produce a control that appears to work and then inserts an unsupported combination.
    /// </summary>
    public static bool SupportsSkinTone(EmojiEntry entry) => VariantsOf(entry).Count > 0;

    /// <summary>
    /// The requested skin-tone form of a sequence, or the sequence itself when the tone is "none", the
    /// sequence has no variants, or the variant is absent from this Unicode version. Never fabricates.
    /// </summary>
    public static string Resolve(EmojiEntry entry, string? skinToneModifier)
    {
        if (string.IsNullOrEmpty(skinToneModifier) || !SupportsSkinTone(entry)) return entry.Text;
        var key = entry.CodePoints + " " + CodePointKey(skinToneModifier);
        return ByCodePoints.Value.TryGetValue(key, out var variant) ? variant.Text : entry.Text;
    }

    /// <summary>Modifier-bearing catalogue rows derived from one base sequence, in modifier order.</summary>
    public static IReadOnlyList<EmojiEntry> VariantsOf(EmojiEntry entry) =>
        _variants.Value.TryGetValue(entry.CodePoints, out var list) ? list : Array.Empty<EmojiEntry>();

    private static readonly Lazy<Dictionary<string, IReadOnlyList<EmojiEntry>>> _variants = new(() =>
    {
        var map = new Dictionary<string, List<EmojiEntry>>(StringComparer.Ordinal);
        foreach (var entry in Loaded.Value)
        {
            var codePoints = entry.CodePoints.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (codePoints.Length == 0 || !IsSkinToneModifier(codePoints[^1])) continue;
            var baseKey = string.Join(' ', codePoints[..^1]);
            if (!map.TryGetValue(baseKey, out var list)) map[baseKey] = list = [];
            list.Add(entry);
        }
        foreach (var list in map.Values) list.Sort((a, b) => a.CodePoints.CompareTo(b.CodePoints));
        return (Dictionary<string, IReadOnlyList<EmojiEntry>>)map.ToDictionary(
            pair => pair.Key, pair => (IReadOnlyList<EmojiEntry>)pair.Value);
    });

    private static bool IsSkinToneModifier(string hexCodePoint) =>
        int.TryParse(hexCodePoint, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value)
        && value is >= 0x1F3FB and <= 0x1F3FF;

    private static string CodePointKey(string text) =>
        string.Join(' ', text.EnumerateRunes()
            .Select(rune => rune.Value.ToString("X", CultureInfo.InvariantCulture)));

    /// <summary>
    /// Catalogue key for typed or pasted text. A sequence whose emoji presentation needs U+FE0F is listed
    /// fully-qualified (for example "2696 FE0F" for scales), and a font or input method may hand back the
    /// bare base code point; that is resolved to the qualified entry so the board stores one canonical form
    /// instead of keeping whatever spelling arrived.
    /// </summary>
    private static string CanonicalKey(string text)
    {
        var key = CodePointKey(text);
        if (ByCodePoints.Value.ContainsKey(key)) return key;
        var qualified = key.Length == 0 ? key : key + " FE0F";
        return ByCodePoints.Value.ContainsKey(qualified) ? qualified : key;
    }

    private static IReadOnlyList<EmojiEntry> Read()
    {
        using var stream = typeof(EmojiCatalogue).Assembly
            .GetManifestResourceStream("Haven.UI.Resources.emoji-test-17.0.txt")
            ?? throw new InvalidOperationException("The Unicode emoji catalogue resource is missing.");
        using var reader = new StreamReader(stream);
        var result = new List<EmojiEntry>();
        var group = "";
        while (reader.ReadLine() is { } line)
        {
            if (line.StartsWith("# group: ", StringComparison.Ordinal)) { group = line[9..]; continue; }
            if (line.StartsWith('#') || !line.Contains("; fully-qualified", StringComparison.Ordinal)) continue;
            var separator = line.IndexOf(';');
            var comment = line.IndexOf('#', separator);
            if (comment < 0) throw new InvalidDataException("Unicode emoji data is malformed.");
            var codePoints = line[..separator].Trim();
            var description = line[(comment + 1)..].Trim();
            var version = description.IndexOf(" E", StringComparison.Ordinal);
            var nameStart = version < 0 ? -1 : description.IndexOf(' ', version + 2);
            if (nameStart < 0) throw new InvalidDataException("Unicode emoji data has no accessible description.");
            var text = string.Concat(codePoints.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Select(value => char.ConvertFromUtf32(int.Parse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture))));
            result.Add(new EmojiEntry(text, description[(nameStart + 1)..], group,
                string.Join(' ', codePoints.Split(' ', StringSplitOptions.RemoveEmptyEntries))));
        }
        return result.AsReadOnly();
    }
}
