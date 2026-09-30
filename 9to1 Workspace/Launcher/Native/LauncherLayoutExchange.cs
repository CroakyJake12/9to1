using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NineToOne.Launcher;

/// <summary>A portable layout description. Import still passes through the current Home owner and CAS.</summary>
public static class LauncherLayoutExchange
{
    public const int MaximumBytes = 4 * 1024 * 1024;
    private sealed record Document(string Format, int Version, string AuthorityId, LauncherLayout Layout);
    private static readonly JsonSerializerOptions Options = new()
    { MaxDepth = 32, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, WriteIndented = true };

    public static string Export(LauncherStoredLayout source)
    {
        source.Current.Validate();
        if (string.IsNullOrWhiteSpace(source.AuthorityId)) throw new InvalidDataException("Launcher profile ownership is missing.");
        var text = JsonSerializer.Serialize(new Document("9to1.launcher.layout", 6, source.AuthorityId, LauncherLayoutEdits.Clone(source.Current)), Options);
        if (Encoding.UTF8.GetByteCount(text) > MaximumBytes) throw new InvalidDataException("Launcher backup exceeds the supported size.");
        return text;
    }

    public static LauncherLayout Import(string text, string currentAuthorityId)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length > MaximumBytes || Encoding.UTF8.GetByteCount(text) > MaximumBytes)
            throw new InvalidDataException("Select a launcher backup of at most 4 MiB.");
        Document document;
        try { document = JsonSerializer.Deserialize<Document>(text, Options) ?? throw new JsonException(); }
        catch (JsonException ex) { throw new InvalidDataException("Unsupported launcher backup. The current layout was preserved.", ex); }
        if (document.Format != "9to1.launcher.layout" || document.Version is not (1 or 2 or 3 or 4 or 5 or 6) || document.Layout is null)
            throw new InvalidDataException("This launcher backup format requires a compatible version.");
        // Canonical application IDs are profile-owned. Cross-profile imports need an explicit owner mapping service.
        if (string.IsNullOrWhiteSpace(currentAuthorityId) || document.AuthorityId != currentAuthorityId)
            throw new UnauthorizedAccessException("This backup belongs to a different Home profile. No application identities were remapped.");
        return LauncherLayoutEdits.Clone(LauncherLayout.UpgradeKnownSchema(document.Layout));
    }
}
