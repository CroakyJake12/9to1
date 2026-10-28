using System.Text;
using Haven.Core;

namespace HavenOS.Apps.Browse;

/// <summary>Search presentation data only; the Home Library still authorizes every result.</summary>
public static class BrowseOmniboxSearchRoute
{
    public static AppContentAddress Create(string text, bool filesOnly)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        text = text.Trim();
        if (text.Length > 256 || text.Any(char.IsControl))
            throw new ArgumentException("Use a search of at most 256 characters without control characters.", nameof(text));
        var encoded = Convert.ToBase64String(new UTF8Encoding(false, true).GetBytes(text)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        if (encoded.Length > 512) encoded = encoded[..512] + "/" + encoded[512..];
        return AppContentAddress.Parse("browse://home/library/search/" + (filesOnly ? "files/" : "all/") + encoded);
    }

    public static bool TryParse(AppContentAddress address, out string text, out bool filesOnly)
    {
        text = string.Empty; filesOnly = false;
        var parts = address.ResourceSegments;
        if (address.Namespace != AppContentNamespace.Browse || address.Authority != "home" || parts.Count is not (4 or 5) ||
            parts[0] != "library" || parts[1] != "search" || parts[2] is not ("all" or "files")) return false;
        try
        {
            var encoded = string.Concat(parts.Skip(3)).Replace('-', '+').Replace('_', '/');
            var decoded = new UTF8Encoding(false, true).GetString(Convert.FromBase64String(encoded.PadRight((encoded.Length + 3) / 4 * 4, '=')));
            var canonical = Create(decoded, parts[2] == "files");
            if (canonical != address) return false;
            text = decoded; filesOnly = parts[2] == "files"; return true;
        }
        catch (Exception error) when (error is ArgumentException or FormatException)
        { return false; }
    }
}
