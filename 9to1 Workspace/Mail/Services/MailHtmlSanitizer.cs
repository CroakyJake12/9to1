using Ganss.Xss;

namespace HavenOS.Mail.Services;

/// <summary>Sanitizes provider HTML with an allowlist and blocks external resource schemes until user trust.</summary>
public sealed class MailHtmlSanitizer
{
    private static readonly string[] SafeSchemes = ["cid", "mailto"];

    public string Sanitize(string? html, bool allowRemoteContent)
    {
        if (string.IsNullOrEmpty(html)) return string.Empty;
        var sanitizer = new HtmlSanitizer();
        sanitizer.AllowedSchemes.Clear();
        sanitizer.AllowedSchemes.UnionWith(SafeSchemes);
        if (allowRemoteContent)
        {
            sanitizer.AllowedSchemes.Add("http");
            sanitizer.AllowedSchemes.Add("https");
        }
        sanitizer.AllowedAttributes.Remove("srcdoc");
        sanitizer.AllowedAttributes.Remove("formaction");
        sanitizer.AllowedTags.Remove("iframe");
        sanitizer.AllowedTags.Remove("object");
        sanitizer.AllowedTags.Remove("embed");
        sanitizer.AllowedTags.Remove("form");
        return sanitizer.Sanitize(html);
    }

    public string ToPlainText(string? html)
    {
        if (string.IsNullOrWhiteSpace(html)) return string.Empty;
        var sanitizer = new HtmlSanitizer();
        sanitizer.AllowedAttributes.Clear();
        sanitizer.AllowedCssProperties.Clear();
        sanitizer.AllowedSchemes.Clear();
        var safe = sanitizer.Sanitize(html);
        return System.Net.WebUtility.HtmlDecode(System.Text.RegularExpressions.Regex.Replace(safe, "<[^>]+>", " "))
            .Replace('\u00a0', ' ')
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Aggregate(string.Empty, (text, word) => text.Length == 0 ? word : text + " " + word);
    }
}
