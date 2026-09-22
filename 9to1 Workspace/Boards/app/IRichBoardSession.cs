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

/// <summary>One editable block: heading, paragraph, checklist item, table, ink, image, graph, divider.</summary>
public sealed class RichBoardBlock
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Kind { get; set; } = "paragraph";
    public string Text { get; set; } = string.Empty;
    public string StyleId { get; set; } = "normal";
    public bool IsChecked { get; set; }
    public int Level { get; set; }
    public bool Bold { get; set; }
    public bool Italic { get; set; }
    public bool Underline { get; set; }
    public bool Strike { get; set; }
    public string Baseline { get; set; } = "normal";
    public string FontFamily { get; set; } = string.Empty;
    public double FontSize { get; set; }
    public string Foreground { get; set; } = string.Empty;
    public string Background { get; set; } = string.Empty;
    public string Alignment { get; set; } = "inherit";
    public double LineSpacing { get; set; }
    public double SpaceBefore { get; set; }
    public double SpaceAfter { get; set; }
    public int IndentLevel { get; set; } = -1;
    public Dictionary<string, string> TableCells { get; set; } = new(StringComparer.Ordinal);
    public RichTableStyleView TableStyle { get; set; } = new();
    public Dictionary<string, RichCellStyleView> CellStyles { get; set; } = new(StringComparer.Ordinal);
    public int TableRows { get; set; }
    public int TableCols { get; set; }
    public List<double> ColumnWidths { get; set; } = [];
    public RichImageView? Image { get; set; }
    public RichDividerView? Divider { get; set; }
    public RichGraphView? Graph { get; set; }
    public string? AttachmentId { get; set; }
    public string? AttachmentName { get; set; }
    public long AttachmentSize { get; set; }
    public int InkStrokeCount { get; set; }
}

/// <summary>Table-level formatting surfaced to the editor.</summary>
public sealed class RichTableStyleView
{
    public string Background { get; set; } = string.Empty;
    public string BorderColor { get; set; } = string.Empty;
    public double BorderWidth { get; set; }
    public string BorderStyle { get; set; } = "inherit";
    public double CornerRadius { get; set; }
    public bool AlternatingRows { get; set; }
}

/// <summary>Per-cell formatting surfaced to the editor.</summary>
public sealed class RichCellStyleView
{
    public string AlignH { get; set; } = "inherit";
    public string AlignV { get; set; } = "inherit";
    public string Background { get; set; } = string.Empty;
    public string Foreground { get; set; } = string.Empty;
    public bool Bold { get; set; }
    public bool Italic { get; set; }
    public bool Underline { get; set; }
    public string Baseline { get; set; } = "normal";
}

/// <summary>Image payload surfaced to the editor (bytes only while importing).</summary>
public sealed class RichImageView
{
    public string DisplayName { get; set; } = "image";
    public string MediaType { get; set; } = "image/png";
    public string? DataBase64 { get; set; }
    public double Width { get; set; }
    public string Alignment { get; set; } = "inherit";
    public string AltText { get; set; } = string.Empty;
}

public sealed class RichDividerView
{
    public double Thickness { get; set; } = 1;
    public string LineStyle { get; set; } = "solid";
    public string Color { get; set; } = "#FF5F6368";
}

/// <summary>Editable graph state surfaced to the editor (never a flattened image).</summary>
public sealed class RichGraphView
{
    public List<RichGraphExpressionView> Expressions { get; set; } = [];
    public double XMin { get; set; } = -10;
    public double XMax { get; set; } = 10;
    public double YMin { get; set; } = -10;
    public double YMax { get; set; } = 10;
    public bool ShowGrid { get; set; } = true;
    public bool ShowAxes { get; set; } = true;
    public string XLabel { get; set; } = "x";
    public string YLabel { get; set; } = "y";
    public List<RichGraphPointView> Points { get; set; } = [];
}

public sealed class RichGraphExpressionView
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Text { get; set; } = "y = x";
    public bool Visible { get; set; } = true;
    public string Color { get; set; } = "#FF1A73E8";
    public double LineWidth { get; set; } = 2;
    public double? DomainMin { get; set; }
    public double? DomainMax { get; set; }
}

public sealed class RichGraphPointView
{
    public double X { get; set; }
    public double Y { get; set; }
    public string Label { get; set; } = string.Empty;
}

/// <summary>Style catalog entry for the style/add menus.</summary>
public sealed class RichStyleView
{
    public string Id { get; set; } = "normal";
    public string Name { get; set; } = "Paragraph";
    public bool IsBuiltIn { get; set; } = true;
    public string BlockKind { get; set; } = "paragraph";
    public bool Bold { get; set; }
    public bool Italic { get; set; }
    public bool Underline { get; set; }
    public bool Strike { get; set; }
    public string Baseline { get; set; } = "normal";
    public string FontFamily { get; set; } = string.Empty;
    public double FontSize { get; set; }
    public string Foreground { get; set; } = string.Empty;
    public string Background { get; set; } = string.Empty;
    public string Alignment { get; set; } = "inherit";
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
    IReadOnlyList<RichStyleView> Styles { get; }
    bool CanUndo { get; }
    bool CanRedo { get; }
    ValueTask<bool> UndoAsync(CancellationToken cancellationToken = default);
    ValueTask<bool> RedoAsync(CancellationToken cancellationToken = default);
}
