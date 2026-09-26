using System.Globalization;
using System.Text.RegularExpressions;

namespace HavenOS.Apps.Sites.Domain;

public sealed record NormalizedHostname(string DisplayName, string AsciiName);

public static partial class SiteAddressRules
{
    public const string FirstPartyHost = "sites.9to1.uk";
    public const int MaximumPathLength = 2048;

    private static readonly HashSet<string> ReservedSegments = new(StringComparer.Ordinal)
    {
        "admin", "api", "assets", "auth", "health", "login", "logout", "robots-txt", "sitemap-xml", "system"
    };

    [GeneratedRegex("^[a-z]+(?:-[a-z]+)*$", RegexOptions.CultureInvariant)]
    private static partial Regex SlugPattern();

    public static string NormalizeSlug(string input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var slug = input.Trim();
        if (!SlugPattern().IsMatch(slug))
            throw Invalid("A first-party site slug must contain lowercase letters and single internal hyphens only.", "slug");
        if (IsReserved(slug))
            throw Invalid("This first-party site slug is reserved by the platform.", "slug");
        return slug;
    }

    public static IReadOnlyList<string> NormalizeRoutePath(string input)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (input.Length > MaximumPathLength)
            throw Invalid("The route path exceeds the maximum supported length.", "route");
        var path = input.Trim();
        if (path.Length == 0 || path[0] != '/' || path.Contains('\\') || path.Contains('?') || path.Contains('#'))
            throw Invalid("A Sites route must be an absolute path without query, fragment, or backslash characters.", "route");
        if (path == "/") return Array.Empty<string>();
        var segments = path[1..].Split('/');
        if (segments.Any(segment => segment.Length == 0))
            throw Invalid("A Sites route cannot contain empty or repeated path segments.", "route");
        foreach (var segment in segments)
        {
            if (segment.StartsWith(':') && segment.Length > 1)
            {
                if (!SlugPattern().IsMatch(segment[1..]))
                    throw Invalid("A route parameter name must contain lowercase letters and single internal hyphens only.", "route");
            }
            else if (!SlugPattern().IsMatch(segment))
            {
                throw Invalid("Each Sites route segment must contain lowercase letters and single internal hyphens only.", "route");
            }
        }
        return segments;
    }

    public static Uri FirstPartyUrl(string slug)
    {
        var normalized = NormalizeSlug(slug);
        return new Uri($"https://{FirstPartyHost}/{normalized}", UriKind.Absolute);
    }

    public static NormalizedHostname NormalizeHostname(string input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var value = input.Trim();
        if (value.Length is 0 or > 253 || value.Contains('/') || value.Contains('@') || value.Contains(':') || value.Contains('?') || value.Contains('#'))
            throw Invalid("Enter a hostname without a scheme, port, path, credentials, query, or fragment.", "domain");

        value = value.TrimEnd('.');
        if (value.Length == 0 || value.Contains("..", StringComparison.Ordinal))
            throw Invalid("The hostname contains an empty label.", "domain");

        try
        {
            var idn = new IdnMapping { UseStd3AsciiRules = true };
            var labels = value.Split('.');
            var asciiLabels = labels.Select(label =>
            {
                if (label.Length == 0) throw new ArgumentException("Empty hostname label.");
                var ascii = idn.GetAscii(label).ToLowerInvariant();
                if (ascii.Length is 0 or > 63 || ascii[0] == '-' || ascii[^1] == '-')
                    throw new ArgumentException("Invalid hostname label.");
                return ascii;
            });
            var asciiName = string.Join('.', asciiLabels);
            if (asciiName.Length > 253) throw new ArgumentException("Hostname exceeds DNS length.");
            var display = string.Join('.', labels.Select(label => idn.GetUnicode(idn.GetAscii(label))));
            return new NormalizedHostname(display, asciiName);
        }
        catch (ArgumentException ex)
        {
            throw Invalid("The hostname is not a valid IDN/DNS name.", "domain", ex);
        }
    }

    public static string DomainChallengeRecordName(string hostname)
    {
        var canonical = NormalizeHostname(hostname).AsciiName;
        return $"_9to1-site-verification.{canonical}";
    }

    private static bool IsReserved(string segment) =>
        ReservedSegments.Contains(segment) || segment == "sites" || segment.StartsWith("9to1-", StringComparison.Ordinal);

    private static SiteOperationException Invalid(string message, string target, Exception? inner = null) =>
        new(new SiteApiError("InvalidInput", message, target, false, Detail: inner?.GetType().Name));
}
