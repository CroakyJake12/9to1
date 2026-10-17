namespace Haven.Core;

/// <summary>The four public content namespaces have different owners and privileges.</summary>
public enum AppContentNamespace { Web = 0, Browse = 1, Cui = 2, NativeApp = 3 }

/// <summary>
/// A content address, not an authority grant. In particular, the public 9to1://
/// namespace cannot be represented by System.Uri, whose scheme must start with
/// a letter. Native resource interpretation belongs to the registered owning app.
/// </summary>
public sealed record AppContentAddress
{
    private AppContentAddress(AppContentNamespace kind, string value, string authority,
        IReadOnlyList<string> segments, Uri? webUri)
    { Namespace = kind; Value = value; Authority = authority; ResourceSegments = segments; WebUri = webUri; }

    public AppContentNamespace Namespace { get; }
    public string Value { get; }
    public string Authority { get; }
    public IReadOnlyList<string> ResourceSegments { get; }
    public Uri? WebUri { get; }
    public bool IsWeb => Namespace == AppContentNamespace.Web;
    public override string ToString() => Value;
    public bool Equals(AppContentAddress? other) => other is not null
        && string.Equals(Value, other.Value, StringComparison.Ordinal);
    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Value);

    public static AppContentAddress Parse(string value) => TryParse(value, out var address)
        ? address! : throw new FormatException("The content address is not a supported, unambiguous route.");

    /// <summary>Parses addresses only. Ordinary search text is handled by the browser search policy.</summary>
    public static bool TryParse(string? value, out AppContentAddress? address)
    {
        address = null;
        if (string.IsNullOrWhiteSpace(value) || value.Length > 8192) return false;
        var text = value.Trim();
        if (text.Any(char.IsControl) || text.Contains('\\')) return false;
        var separator = text.IndexOf("://", StringComparison.Ordinal);
        if (separator <= 0) return false;
        var scheme = text[..separator].ToLowerInvariant();
        if (scheme is "http" or "https")
        {
            if (!Uri.TryCreate(text, UriKind.Absolute, out var uri) || string.IsNullOrEmpty(uri.Host)
                || !string.IsNullOrEmpty(uri.UserInfo) || uri.Scheme is not ("http" or "https")) return false;
            address = new(AppContentNamespace.Web, uri.AbsoluteUri, uri.IdnHost, Array.Empty<string>(), uri);
            return true;
        }
        var kind = scheme switch
        {
            "browse" => AppContentNamespace.Browse,
            "cui" => AppContentNamespace.Cui,
            "9to1" => AppContentNamespace.NativeApp,
            _ => (AppContentNamespace?)null
        };
        if (kind is null) return false;
        var route = text[(separator + 3)..];
        // Local routes have no credentials, port, query or fragment. Resource
        // segments are opaque IDs, not filesystem paths or a nested URL.
        if (route.IndexOfAny(['?', '#', '@', ':']) >= 0) return false;
        var parts = route.Split('/');
        var authority = parts[0].ToLowerInvariant();
        if (authority.Length is 0 or > 128 || !IsName(authority)) return false;
        var segments = new List<string>();
        for (var index = 1; index < parts.Length; index++)
        {
            var segment = parts[index];
            if (segment.Length == 0 && index == parts.Length - 1) continue;
            if (segment.Length is 0 or > 512 || segment is "." or ".." || !IsResourceId(segment)) return false;
            segments.Add(segment);
        }
        var canonical = scheme + "://" + authority + (segments.Count == 0 ? string.Empty : "/" + string.Join('/', segments));
        address = new(kind.Value, canonical, authority, segments.AsReadOnly(), null);
        return true;
    }

    /// <summary>Malformed/unknown privileged routes still must not become external search queries.</summary>
    public static bool IsReserved(string? value)
    {
        if (value is null) return false;
        var candidate = value.TrimStart();
        return candidate.StartsWith("9to1:", StringComparison.OrdinalIgnoreCase)
            || candidate.StartsWith("browse:", StringComparison.OrdinalIgnoreCase)
            || candidate.StartsWith("cui:", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsName(string value) => char.IsAsciiLetterOrDigit(value[0])
        && value.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.');
    private static bool IsResourceId(string value) => value.All(character => char.IsAsciiLetterOrDigit(character)
        || character is '-' or '_' or '.' or '~');
}
