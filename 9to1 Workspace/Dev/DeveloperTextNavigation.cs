using System.Globalization;

namespace HavenOS.Apps.Dev;

/// <summary>Navigation within already authorised editor text. Lines and columns are one-based;
/// columns and offsets use UTF-16, as the maintained native editor and LSP do. No text is changed.</summary>
public static class DeveloperTextNavigation
{
    public static bool TryResolve(string text, string location, out int offset, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(text); ArgumentNullException.ThrowIfNull(location);
        offset = 0;
        var parts = location.Trim().Split(':');
        if (parts.Length is < 1 or > 2 ||
            !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var line) || line < 1) return false;
        var column = 1;
        if (parts.Length == 2 && (!int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out column) || column < 1)) return false;
        token.ThrowIfCancellationRequested();
        var start = 0; var current = 1;
        for (var index = 0; index < text.Length && current < line; index++)
        {
            if ((index & 1023) == 0) token.ThrowIfCancellationRequested();
            if (text[index] is not ('\r' or '\n')) continue;
            if (text[index] == '\r' && index + 1 < text.Length && text[index + 1] == '\n') index++;
            start = index + 1; current++;
        }
        if (current != line) return false;
        var end = start;
        while (end < text.Length && text[end] is not ('\r' or '\n'))
        { if ((end & 1023) == 0) token.ThrowIfCancellationRequested(); end++; }
        if ((long)column - 1 > end - start) return false;
        offset = start + column - 1;
        // A character position cannot split a UTF-16 surrogate pair.
        if (offset > start && offset < end && char.IsLowSurrogate(text[offset]) && char.IsHighSurrogate(text[offset - 1])) return false;
        return true;
    }
}
