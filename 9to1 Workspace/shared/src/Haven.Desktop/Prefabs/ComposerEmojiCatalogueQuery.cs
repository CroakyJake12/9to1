namespace Haven.Desktop.Prefabs;

/// <summary>
/// The narrow, non-visual catalogue query engine behind the composer's emoji picker. It is a pure
/// function over the canonical Unicode catalogue: given a search query and an opaque cursor, it
/// returns a read-only window of matching items plus truthful total/count metadata and opaque
/// next/previous cursor tokens. It owns NO per-view presentation state, page cache or control
/// trees; the authored CUI/binding layer owns the current query and cursor, and only advances them
/// through the opaque cursors returned here.
/// </summary>
public static class ComposerEmojiCatalogueQuery
{
    /// <summary>Bounded window size: only one window is ever materialised at a time while the full
    /// catalogue (3,944 entries) stays reachable through the opaque cursors.</summary>
    public const int PageSize = 48;

    /// <summary>Opaque pagination cursor. Only the engine constructs/decodes cursors; callers treat
    /// them as opaque tokens and never inspect or derive page positions from them.</summary>
    public readonly struct Cursor
    {
        internal readonly int Offset;
        internal Cursor(int offset) => Offset = offset;
    }

    /// <summary>The result of one bounded catalogue query: a read-only window of matched items, the
    /// truthful total match count, the zero-based start offset of this window, and opaque prev/next
    /// cursor tokens (null when the window is at the corresponding boundary).</summary>
    public sealed record Result(
        IReadOnlyList<ComposerEmoji> Items,
        int TotalCount,
        int StartOffset,
        Cursor? Prev,
        Cursor? Next);

    /// <summary>Evaluates the catalogue for the given query and cursor. Query filtering is data-only
    /// and operates on the FULL catalogue (including flags and every trigger-excluded sequence); the
    /// neutral allowlist/flag/suggestive filters apply only to the random-trigger icon pool, never to
    /// the picker/search path. Returns only the bounded window, never the full match set.</summary>
    public static Result Evaluate(string? query, Cursor? cursor)
    {
        var all = Filter(query);
        var total = all.Count;
        if (total == 0) return new Result(Array.Empty<ComposerEmoji>(), 0, 0, null, null);

        var start = cursor is { } c ? Math.Clamp(c.Offset, 0, total - 1) : 0;
        start = start / PageSize * PageSize; // align to the page boundary (bounded decode)
        var count = Math.Min(PageSize, total - start);
        var items = new ComposerEmoji[count];
        for (var i = 0; i < count; i++) items[i] = all[start + i];
        return new Result(items, total, start,
            start > 0 ? new Cursor(start - PageSize) : null,
            start + count < total ? new Cursor(start + PageSize) : null);
    }

    private static IReadOnlyList<ComposerEmoji> Filter(string? query)
    {
        var trimmed = query?.Trim();
        if (string.IsNullOrEmpty(trimmed)) return ComposerEmojiCatalogue.Entries;
        return ComposerEmojiCatalogue.Entries.Where(entry =>
            entry.Name.Contains(trimmed, StringComparison.OrdinalIgnoreCase)
            || entry.Group.Contains(trimmed, StringComparison.OrdinalIgnoreCase)
            || entry.Text == trimmed).ToArray();
    }
}
