using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NineToOne.Dulche.Context;

/// <summary>Strict UCML v1 interchange codec. Plaintext interchange is not encryption.</summary>
public static class UcmlCodec
{
    public const int MaximumDocumentBytes = 512 * 1024;
    public const int MaximumMetadataBytes = 64 * 1024;
    public const int MaximumMarkdownCharacters = 256 * 1024;
    private static readonly UTF8Encoding Utf8 = new(false, true);
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        RespectNullableAnnotations = true,
        RespectRequiredConstructorParameters = true,
        MaxDepth = 16,
        WriteIndented = true
    };

    public static UcmlDocument Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (Utf8.GetByteCount(text) > MaximumDocumentBytes || text.Contains('\0'))
            throw new FormatException("UCML exceeds the bounded UTF-8 document limit or contains NUL.");
        var start = text.StartsWith("---ucml\r\n", StringComparison.Ordinal) ? 9 :
            text.StartsWith("---ucml\n", StringComparison.Ordinal) ? 8 :
            throw new FormatException("UCML must begin with the ---ucml marker.");
        var end = text.IndexOf("\n---\n", start, StringComparison.Ordinal);
        var delimiterLength = 5;
        if (end < 0) { end = text.IndexOf("\r\n---\r\n", start, StringComparison.Ordinal); delimiterLength = 7; }
        if (end < 0) throw new FormatException("UCML metadata needs its closing --- line.");
        var metadata = ParseMetadata(text[start..end]);
        var document = new UcmlDocument(metadata, text[(end + delimiterLength)..]);
        Validate(document);
        return document;
    }

    public static UcmlMetadata ParseMetadata(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        if (Utf8.GetByteCount(json) > MaximumMetadataBytes) throw new FormatException("UCML metadata is too large.");
        try
        {
            using var parsed = JsonDocument.Parse(json, new() { MaxDepth = 16 });
            RejectDuplicateProperties(parsed.RootElement);
            var metadata = JsonSerializer.Deserialize<UcmlMetadata>(json, Options)
                ?? throw new FormatException("UCML metadata must be an object.");
            ValidateMetadata(metadata);
            return metadata;
        }
        catch (JsonException error) { throw new FormatException("Invalid UCML metadata: " + error.Message, error); }
    }

    public static string SerializeMetadata(UcmlMetadata metadata)
    {
        ValidateMetadata(metadata);
        var json = JsonSerializer.Serialize(metadata, Options);
        if (Utf8.GetByteCount(json) > MaximumMetadataBytes) throw new FormatException("UCML metadata is too large.");
        return json;
    }

    public static string Serialize(UcmlDocument document)
    {
        Validate(document);
        var text = "---ucml\n" + SerializeMetadata(document.Metadata) + "\n---\n" + document.Markdown;
        if (Utf8.GetByteCount(text) > MaximumDocumentBytes) throw new FormatException("UCML document is too large.");
        return text;
    }

    public static void Validate(UcmlDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        ValidateMetadata(document.Metadata);
        if (string.IsNullOrWhiteSpace(document.Markdown) || document.Markdown.Length > MaximumMarkdownCharacters || document.Markdown.Contains('\0'))
            throw new FormatException("UCML Markdown must be nonempty and within its size limit.");
        _ = Utf8.GetByteCount(document.Markdown);
    }

    private static void ValidateMetadata(UcmlMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        if (metadata.SchemaVersion != 1) throw new FormatException("Unsupported UCML schema version.");
        if (metadata.Id == Guid.Empty || metadata.Revision < 1 || metadata.SupersedesId == Guid.Empty || metadata.SupersedesId == metadata.Id)
            throw new FormatException("UCML needs valid, distinct stable identities and a positive revision.");
        Text(metadata.Title, 160); Text(metadata.OwnerId, 256); Text(metadata.Category, 128);
        if (metadata.Scope is null || metadata.Consent is null) throw new FormatException("UCML scope and consent are required.");
        Text(metadata.Scope.Id, 256);
        Choice(metadata.Scope.Kind, "personal", "assistant", "conversation", "space", "team", "organisation");
        Choice(metadata.Classification, "public", "private", "restricted");
        Choice(metadata.Retention, "until-revoked", "until-expiry", "session");
        Choice(metadata.Consent.Status, "explicit", "review-required", "revoked");
        Strings(metadata.Consent.Apps); Strings(metadata.Consent.Purposes); Strings(metadata.Consent.Recipients);
        if (metadata.CreatedAt == default || metadata.UpdatedAt < metadata.CreatedAt ||
            metadata.ExpiresAt is { } expires && expires <= metadata.CreatedAt ||
            metadata.Retention == "until-expiry" && metadata.ExpiresAt is null ||
            !double.IsFinite(metadata.Confidence) || metadata.Confidence is < 0 or > 1)
            throw new FormatException("Invalid UCML timestamps, retention or confidence.");
        if (metadata.Sources is null || metadata.Sources.Count is < 1 or > 64)
            throw new FormatException("UCML needs 1–64 provenance sources.");
        var identities = new HashSet<string>(StringComparer.Ordinal);
        foreach (var source in metadata.Sources)
        {
            if (source is null) throw new FormatException("Null UCML source.");
            Text(source.Id, 512); Text(source.Title, 512); Text(source.Kind, 128);
            if (!identities.Add(source.Id) || source.ObservedAt == default) throw new FormatException("Duplicate or undated UCML source.");
            if (source.Uri is { } uri)
            {
                Text(uri, 2048);
                if (!System.Uri.TryCreate(uri, UriKind.Absolute, out var parsed) ||
                    parsed.Scheme is not ("https" or "http" or "urn") || !string.IsNullOrEmpty(parsed.UserInfo))
                    throw new FormatException("Source URI must be a credential-free http(s) or urn reference.");
            }
        }
    }

    private static void Text(string value, int maximum)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > maximum || value.Any(char.IsControl))
            throw new FormatException("UCML metadata text is empty, oversized or contains control characters.");
    }
    private static void Choice(string value, params string[] allowed)
    { if (!allowed.Contains(value, StringComparer.Ordinal)) throw new FormatException("Unsupported UCML metadata value."); }
    private static void Strings(IReadOnlyList<string> values)
    {
        if (values is null || values.Count is < 1 or > 32) throw new FormatException("UCML restrictions need 1–32 explicit values.");
        var unique = new HashSet<string>(StringComparer.Ordinal);
        foreach (var value in values)
        {
            Text(value, 256);
            if (value.Contains('*') || !unique.Add(value)) throw new FormatException("UCML restrictions reject wildcards and duplicates.");
        }
    }
    private static void RejectDuplicateProperties(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in value.EnumerateObject())
            {
                if (!names.Add(property.Name)) throw new FormatException("Duplicate UCML metadata property: " + property.Name);
                RejectDuplicateProperties(property.Value);
            }
        }
        else if (value.ValueKind == JsonValueKind.Array)
            foreach (var child in value.EnumerateArray()) RejectDuplicateProperties(child);
    }
}
