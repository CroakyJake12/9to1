using System.Text.Json;
using System.Text.Json.Serialization;

namespace NineToOne.Os.Shell;

/// <summary>Portable configuration contains references/presentation, never canonical app objects or secrets.</summary>
public static class DesktopSpaceExchange
{
    private sealed record Document(string Format, int SchemaVersion, DesktopSpace Space);
    private static readonly JsonSerializerOptions Json = new()
    { WriteIndented = true, MaxDepth = 32, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };
    public static string Export(DesktopSpace space)
    {
        new ShellConfiguration(1, space.Id, [space]).Validate();
        var text = JsonSerializer.Serialize(new Document("9to1.desktop-space", 1, space), Json);
        if (text.Length > 2 * 1024 * 1024) throw new InvalidDataException("Desktop Space exceeds the portable document limit.");
        return text;
    }
    public static ShellConfiguration Import(ShellConfiguration current, string text)
    {
        current.Validate();
        if (string.IsNullOrWhiteSpace(text) || text.Length > 2 * 1024 * 1024) throw new InvalidDataException("A bounded Desktop Space document is required.");
        Document document;
        try { document = JsonSerializer.Deserialize<Document>(text, Json) ?? throw new JsonException(); }
        catch (JsonException ex) { throw new InvalidDataException("This is not a supported Desktop Space document.", ex); }
        if (document.Format != "9to1.desktop-space" || document.SchemaVersion != 1 || document.Space is null) throw new InvalidDataException("Unsupported Desktop Space document; existing state was preserved.");
        var imported = new ShellConfiguration(1, document.Space.Id, [document.Space]); imported.Validate();
        var remapped = ShellEdits.DuplicateSpace(imported, document.Space.Name).ActiveSpace;
        var next = current with { ActiveSpaceId = remapped.Id, Spaces = [.. current.Spaces, remapped] };
        next.Validate(); return next;
    }
}
