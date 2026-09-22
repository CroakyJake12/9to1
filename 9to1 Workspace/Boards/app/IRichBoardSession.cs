// Minimal rich-notes session contract for the Boards CUI app.
// The persistence worker owns the real `RichBoardSession` (schema v2 `.9to1board`).
// This interface keeps all persistence access behind a one-line swap:
// register the real session where the app currently registers InMemoryRichBoardSession.

namespace CakeOS.Apps.Boards.App;

/// <summary>Document model surfaced to the CUI Boards editor.</summary>
public sealed class RichBoardDocument
{
    public string Title { get; set; } = "Untitled board";
    public List<RichBoardSection> Sections { get; set; } = [];
}

/// <summary>A section (group of pages) in the rich-notes document.</summary>
public sealed class RichBoardSection
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Title { get; set; } = "Untitled section";
    public List<RichBoardPage> Pages { get; set; } = [];
}

/// <summary>A page holding an ordered block list.</summary>
public sealed class RichBoardPage
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Title { get; set; } = "Untitled page";
    public List<RichBoardBlock> Blocks { get; set; } = [];
}

/// <summary>One editable block: heading, paragraph, checklist item, table, or ink.</summary>
public sealed class RichBoardBlock
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Kind { get; set; } = "paragraph";
    public string Text { get; set; } = string.Empty;
    public bool IsChecked { get; set; }
    public bool Bold { get; set; }
    public bool Italic { get; set; }
    public bool Underline { get; set; }
    public Dictionary<string, string> TableCells { get; set; } = new(StringComparer.Ordinal);
    public int InkStrokeCount { get; set; }
}

/// <summary>
/// Minimal session surface the Boards app codes against.
/// Expected real implementation: `RichBoardSession` in
/// `CakeOS.Apps.Boards.Contract` with OpenAsync/SaveAsync/DisposeAsync,
/// Document/Snapshot/Status, and a status event.
/// </summary>
public interface IRichBoardSession : IAsyncDisposable
{
    RichBoardDocument Document { get; }
    string Snapshot { get; }
    string Status { get; }
    string? FilePath { get; }
    event EventHandler<string>? StatusChanged;
    ValueTask OpenAsync(string? path, CancellationToken cancellationToken = default);
    /// <summary>Durable save. Sets Status to "Saved HH:MM:SS" on success,
    /// "Save failed: ..." on error, and raises StatusChanged in both cases.</summary>
    ValueTask SaveAsync(CancellationToken cancellationToken = default);
    ValueTask SaveAsAsync(string path, CancellationToken cancellationToken = default);
    /// <summary>Mark the document dirty; the session debounces a background autosave.</summary>
    void MarkDirty();
}
