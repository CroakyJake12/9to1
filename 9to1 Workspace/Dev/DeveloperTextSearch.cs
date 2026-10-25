using System.Text;

namespace HavenOS.Apps.Dev;

public sealed record DeveloperTextMatch(int Start, int Length, bool Wrapped);
public sealed record DeveloperTextReplacement(string Text, int Matches, string? Error = null);

/// <summary>Literal editing within an already authorised draft. This is not symbol rename or a
/// language refactor. It never reads or writes files and never truncates a replacement.</summary>
public static class DeveloperTextSearch
{
    public static DeveloperTextMatch? Find(string text, string query, int start, bool previous, bool matchCase,
        CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(text); ArgumentException.ThrowIfNullOrEmpty(query);
        if (start < 0 || start > text.Length) throw new ArgumentOutOfRangeException(nameof(start));
        token.ThrowIfCancellationRequested();
        var comparison = matchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        var offset = previous ? text.AsSpan(0, start).LastIndexOf(query.AsSpan(), comparison) : text.IndexOf(query, start, comparison);
        var wrapped = false;
        if (offset < 0)
        {
            token.ThrowIfCancellationRequested();
            offset = previous ? text.LastIndexOf(query, comparison) : text.IndexOf(query, comparison);
            wrapped = offset >= 0;
        }
        token.ThrowIfCancellationRequested();
        return offset < 0 ? null : new(offset, query.Length, wrapped);
    }

    public static DeveloperTextReplacement ReplaceAll(string text, string query, string replacement, bool matchCase,
        CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(text); ArgumentException.ThrowIfNullOrEmpty(query); ArgumentNullException.ThrowIfNull(replacement);
        token.ThrowIfCancellationRequested();
        if (text.Length > DeveloperSourceReviewSession.MaximumTextCharacters || replacement.IndexOf('\0') >= 0)
            return new(text, 0, "A complete bounded text draft is required; binary content is not substituted.");
        var comparison = matchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        var output = new StringBuilder(text.Length); var cursor = 0; var matches = 0;
        while (true)
        {
            var found = text.IndexOf(query, cursor, comparison);
            if (found < 0) break;
            token.ThrowIfCancellationRequested();
            var tail = text.Length - found - query.Length;
            if ((long)output.Length + found - cursor + replacement.Length + tail > DeveloperSourceReviewSession.MaximumTextCharacters)
                return new(text, 0, "The complete replacement exceeds the editor text limit. The original draft was preserved.");
            output.Append(text, cursor, found - cursor); output.Append(replacement);
            cursor = found + query.Length; matches++;
        }
        token.ThrowIfCancellationRequested();
        if (matches == 0) return new(text, 0);
        output.Append(text, cursor, text.Length - cursor);
        var revised = output.ToString(); token.ThrowIfCancellationRequested();
        return new(revised, matches);
    }

    public static DeveloperTextReplacement ReplaceMatch(string text, string query, string replacement, DeveloperTextMatch match,
        bool matchCase, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(text); ArgumentException.ThrowIfNullOrEmpty(query); ArgumentNullException.ThrowIfNull(replacement);
        ArgumentNullException.ThrowIfNull(match); token.ThrowIfCancellationRequested();
        if (match.Start < 0 || match.Length != query.Length || (long)match.Start + match.Length > text.Length ||
            !text.AsSpan(match.Start, match.Length).Equals(query.AsSpan(), matchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase))
            return new(text, 0, "The found match changed. Find it again before replacing.");
        if ((long)text.Length - match.Length + replacement.Length > DeveloperSourceReviewSession.MaximumTextCharacters || replacement.Contains('\0'))
            return new(text, 0, "The complete replacement is not valid bounded text. The original draft was preserved.");
        var revised = string.Concat(text.AsSpan(0, match.Start), replacement.AsSpan(), text.AsSpan(match.Start + match.Length));
        token.ThrowIfCancellationRequested();
        return new(revised, 1);
    }
}
