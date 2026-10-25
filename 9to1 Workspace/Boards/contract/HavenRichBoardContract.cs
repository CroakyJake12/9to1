using Haven.Application;

namespace CakeOS.Apps.Boards.Contract;

/// <summary>
/// Self-contained rich-notes payload stored inside <c>.9to1board</c> schema v2.
/// Mirrors the semantics of the mature shared <c>NotesDocument</c> model
/// (sections/pages/blocks/runs/lists/tables/ink/canvas/attachments) without
/// importing its document model. Cross-app pages use only the immutable shared artifact identity.
/// </summary>
public sealed class HavenRichNotes
{
    public string Title { get; set; } = "Untitled board";
    public long Version { get; set; } = 1;
    public List<HavenRichSection> Sections { get; set; } = [new HavenRichSection()];
    public List<HavenRichStyle> Styles { get; set; } = HavenRichStyles.BuiltIns();

    public static HavenRichNotes Create(string? title = null)
    {
        var notes = new HavenRichNotes();
        if (!string.IsNullOrWhiteSpace(title))
            notes.Title = title.Trim();
        notes.Sections = [new HavenRichSection { Title = "Notes", Pages = [new HavenRichPage { Title = "Start here" }] }];
        return notes;
    }
}

public sealed class HavenRichSection
{
    public string Id { get; set; } = "sec-" + Guid.NewGuid().ToString("N")[..12];
    public string Title { get; set; } = "Section 1";
    public List<HavenRichPage> Pages { get; set; } = [new HavenRichPage()];
}

public sealed class HavenRichPage
{
    public string Id { get; set; } = "page-" + Guid.NewGuid().ToString("N")[..12];
    public string? ParentPageId { get; set; }
    public string Title { get; set; } = "Page 1";
    /// <summary>Page presentation owner. Native is the default for existing board documents.</summary>
    public string PageType { get; set; } = HavenRichPageTypes.Native;
    /// <summary>Stable, revision-pinned owner identity for an app-backed page.</summary>
    public BoardEmbeddedArtifactReference? EmbeddedArtifact { get; set; }
    public int Order { get; set; }
    public bool IsCanvasLocked { get; set; }
    /// <summary>
    /// Flow placement policy for native blocks. Legacy documents omit this
    /// property and therefore retain the safe vertical-flow default.
    /// </summary>
    public string LayoutMode { get; set; } = HavenRichPageLayoutModes.Locked;
    public double CanvasWidth { get; set; } = 1200;
    public double CanvasHeight { get; set; } = 900;
    public List<HavenRichBlock> Blocks { get; set; } = [new HavenRichBlock()];
    public List<HavenRichInkStroke> Ink { get; set; } = [];
    public HavenRichInkView InkView { get; set; } = new();
    public List<HavenRichCanvasObject> Canvas { get; set; } = [];
}

/// <summary>Stable page type identifiers persisted in the rich board document.</summary>
public static class HavenRichPageTypes
{
    public const string Native = "Native";
    public const string Write = "Write";
    public const string Canvas = "Canvas";
}

/// <summary>Canonical native page layout modes persisted in rich board documents.</summary>
public static class HavenRichPageLayoutModes
{
    public const string Locked = "Locked";
    public const string Unlocked = "Unlocked";
    public static bool IsSupported(string? value) => value is Locked or Unlocked;
}

/// <summary>Persisted placement semantics for a native block.</summary>
public static class HavenRichBlockLayouts
{
    public const string Flow = "Flow";
    public const string Absolute = "Absolute";
    public static bool IsSupported(string? value) => value is Flow or Absolute;
}

/// <summary>Bounds for persisted native block placement geometry.</summary>
public static class HavenRichBlockLayoutGeometry
{
    public const double CoordinateLimit = 100_000;
    public const int ZOrderLimit = 100_000;
}

public enum HavenRichBlockKind
{
    Paragraph = 0,
    Heading = 1,
    BulletList = 2,
    NumberedList = 3,
    Checklist = 4,
    Table = 5,
    Image = 6,
    Divider = 7,
    Graph = 8
}

public sealed class HavenRichBlock
{
    public string Id { get; set; } = "block-" + Guid.NewGuid().ToString("N")[..12];
    public HavenRichBlockKind Kind { get; set; } = HavenRichBlockKind.Paragraph;
    public int Order { get; set; }
    /// <summary>Flow blocks remain in document order; Absolute blocks use their persisted page geometry.</summary>
    public string Layout { get; set; } = HavenRichBlockLayouts.Flow;
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; } = 320;
    public double Height { get; set; } = 120;
    public int ZOrder { get; set; }
    public string StyleId { get; set; } = "normal";
    public string PlainText { get; set; } = string.Empty;
    public List<HavenRichTextRun> Runs { get; set; } = [];
    public List<HavenRichListItem> Items { get; set; } = [];
    public HavenRichTable? Table { get; set; }
    public HavenRichAttachmentRef? Attachment { get; set; }
    public HavenRichImage? Image { get; set; }
    public HavenRichGraph? Graph { get; set; }
    public HavenRichDivider? Divider { get; set; }
    /// <summary>Paragraph alignment override; <see cref="HavenRichAlignment.Inherit"/> resolves from the block style.</summary>
    public HavenRichAlignment Alignment { get; set; } = HavenRichAlignment.Inherit;
    /// <summary>Line spacing multiplier override; 0 inherits from the block style.</summary>
    public double LineSpacing { get; set; }
    public double SpaceBefore { get; set; }
    public double SpaceAfter { get; set; }
    /// <summary>Indentation level override; -1 inherits from the block style.</summary>
    public int IndentLevel { get; set; } = -1;

    public static HavenRichBlock Paragraph(string text = "") => new() { PlainText = text };
    public static HavenRichBlock Heading(string text = "Heading") =>
        new() { Kind = HavenRichBlockKind.Heading, PlainText = text, StyleId = "heading-1" };
}

public sealed class HavenRichTextRun
{
    public string Text { get; set; } = string.Empty;
    public bool Bold { get; set; }
    public bool Italic { get; set; }
    public bool Underline { get; set; }
    public bool StrikeThrough { get; set; }
    public HavenRichBaseline Baseline { get; set; } = HavenRichBaseline.Normal;
    /// <summary>Font family override; empty inherits.</summary>
    public string FontFamily { get; set; } = string.Empty;
    /// <summary>Font size in points; 0 inherits.</summary>
    public double FontSize { get; set; }
    /// <summary>Text colour override (#AARRGGBB); empty inherits.</summary>
    public string Foreground { get; set; } = string.Empty;
    /// <summary>Highlight/background override (#AARRGGBB); empty inherits.</summary>
    public string Background { get; set; } = string.Empty;
}

public sealed class HavenRichListItem
{
    public string Id { get; set; } = "item-" + Guid.NewGuid().ToString("N")[..12];
    public string Text { get; set; } = string.Empty;
    public bool Checked { get; set; }
    public int Level { get; set; }
    public bool Bold { get; set; }
    public bool Italic { get; set; }
    public bool Underline { get; set; }
    public bool StrikeThrough { get; set; }
    public HavenRichBaseline Baseline { get; set; } = HavenRichBaseline.Normal;
    public string Foreground { get; set; } = string.Empty;
}

public sealed class HavenRichTable
{
    public List<HavenRichTableRow> Rows { get; set; } = [];
    public string Background { get; set; } = string.Empty;
    public string BorderColor { get; set; } = string.Empty;
    public double BorderWidth { get; set; }
    public HavenRichBorderStyle BorderStyle { get; set; } = HavenRichBorderStyle.Inherit;
    public double CornerRadius { get; set; }
    public bool AlternatingRows { get; set; }
    public bool HasHeaderRow { get; set; }
    public bool HasHeaderColumn { get; set; }
    /// <summary>Explicit table width in device-independent pixels; zero lets the containing page size it.</summary>
    public double Width { get; set; }
    public HavenRichAlignment Alignment { get; set; } = HavenRichAlignment.Inherit;
    public List<double> ColumnWidths { get; set; } = [];
    public List<double> RowHeights { get; set; } = [];

    public static HavenRichTable Create(int rows, int columns)
    {
        var table = new HavenRichTable { HasHeaderRow = true };
        for (var r = 0; r < Math.Clamp(rows, 1, 100); r++)
        {
            var row = new HavenRichTableRow();
            for (var c = 0; c < Math.Clamp(columns, 1, 50); c++)
                row.Cells.Add(new HavenRichTableCell { Text = r == 0 ? $"Column {c + 1}" : string.Empty });
            table.Rows.Add(row);
        }
        return table;
    }
}

public sealed class HavenRichTableRow
{
    public List<HavenRichTableCell> Cells { get; set; } = [];
}

public sealed class HavenRichTableCell
{
    public string Id { get; set; } = "cell-" + Guid.NewGuid().ToString("N")[..12];
    public int RowSpan { get; set; } = 1;
    public int ColumnSpan { get; set; } = 1;
    /// <summary>Stable ID of the visible anchor cell when this structural slot is covered by a merge.</summary>
    public string? CoveredByCellId { get; set; }
    public string Text { get; set; } = string.Empty;
    public List<HavenRichTextRun> Runs { get; set; } = [];
    public HavenRichAlignment AlignmentH { get; set; } = HavenRichAlignment.Inherit;
    public HavenRichCellVerticalAlignment AlignmentV { get; set; } = HavenRichCellVerticalAlignment.Inherit;
    public string Background { get; set; } = string.Empty;
    public string Foreground { get; set; } = string.Empty;
    public bool Bold { get; set; }
    public bool Italic { get; set; }
    public bool Underline { get; set; }
    public HavenRichBaseline Baseline { get; set; } = HavenRichBaseline.Normal;
}

public sealed class HavenRichInkPoint
{
    public double X { get; set; }
    public double Y { get; set; }
    /// <summary>Pen pressure 0..1; defaults to 0.5 for RC1-era strokes.</summary>
    public double Pressure { get; set; } = 0.5;
}

public sealed class HavenRichInkStroke
{
    public List<HavenRichInkPoint> Points { get; set; } = [];
    public double Width { get; set; } = 2.5;
    public string Color { get; set; } = "#FF1A73E8";
    public HavenRichInkTool Tool { get; set; } = HavenRichInkTool.Pen;
    public bool Selected { get; set; }
}

/// <summary>Persisted ink-canvas viewport so pan/zoom survives reopen.</summary>
public sealed class HavenRichInkView
{
    public double PanX { get; set; }
    public double PanY { get; set; }
    public double Zoom { get; set; } = 1;
}

public sealed class HavenRichCanvasObject
{
    public string Id { get; set; } = "canvas-" + Guid.NewGuid().ToString("N")[..12];
    public string Kind { get; set; } = "Text";
    public string Text { get; set; } = string.Empty;
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; } = 260;
    public double Height { get; set; } = 160;
}

/// <summary>
/// Attachment embedded in the board document so a copied <c>.9to1board</c> carries its files.
/// Small files embed bytes directly; larger ones keep a content-addressed sidecar reference.
/// </summary>
public sealed class HavenRichAttachmentRef
{
    public string Id { get; set; } = "att-" + Guid.NewGuid().ToString("N")[..12];
    public string DisplayName { get; set; } = "attachment";
    public string MediaType { get; set; } = "application/octet-stream";
    public string? DataBase64 { get; set; }
    public string? LocalReference { get; set; }
    /// <summary>Exact byte size of the attachment payload.</summary>
    public long SizeBytes { get; set; }
}

/// <summary>Structural validation for the rich-notes payload. Fail-closed: any error throws.</summary>
public static class HavenRichNotesValidator
{
    public const double InkCoordinateLimit = 100_000;

    public static void Validate(HavenRichNotes notes)
    {
        ArgumentNullException.ThrowIfNull(notes);
        if (string.IsNullOrWhiteSpace(notes.Title))
            throw new InvalidOperationException("Rich board title must be non-empty.");
        if (notes.Sections.Count == 0)
            throw new InvalidOperationException("Rich board must contain at least one section.");
        HavenRichStyles.ValidateStyles(notes);
        var allPageIds = new HashSet<string>(StringComparer.Ordinal);
        var allGraphIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var section in notes.Sections)
        {
            RequireId(section.Id, "Section");            if (string.IsNullOrWhiteSpace(section.Title))
                throw new InvalidOperationException("Rich board sections must have non-empty titles.");
            if (section.Pages.Count == 0)
                throw new InvalidOperationException($"Section '{section.Title}' must contain at least one page.");

            foreach (var page in section.Pages)
            {
                RequireId(page.Id, "Page");
                if (!allPageIds.Add(page.Id))
                    throw new InvalidOperationException($"Page ID '{page.Id}' is duplicated in the board.");
                if (string.IsNullOrWhiteSpace(page.Title))
                    throw new InvalidOperationException("Rich board pages must have non-empty titles.");
                if (!HavenRichPageLayoutModes.IsSupported(page.LayoutMode))
                    throw new InvalidOperationException($"Page layout mode '{page.LayoutMode}' is not supported.");
                if (page.PageType != HavenRichPageTypes.Native &&
                    page.LayoutMode != HavenRichPageLayoutModes.Locked)
                    throw new InvalidOperationException("Only Native Board pages can enable free placement.");
                ValidatePageArtifact(page);
                var blockIds = new HashSet<string>(StringComparer.Ordinal);
                foreach (var block in page.Blocks)
                {
                    RequireId(block.Id, "Block");
                    if (!blockIds.Add(block.Id))
                        throw new InvalidOperationException($"Block ID '{block.Id}' is duplicated on page '{page.Title}'.");
                    if (block.Graph is not null && !allGraphIds.Add(block.Graph.GraphId))
                        throw new InvalidOperationException($"GraphID '{block.Graph.GraphId}' is duplicated in the board.");
                    ValidateBlock(block);
                }

                foreach (var stroke in page.Ink)
                {
                    if (stroke.Points.Count == 0)
                        throw new InvalidOperationException("Ink strokes must contain at least one point.");
                    foreach (var point in stroke.Points)
                    {
                        if (!double.IsFinite(point.X) || !double.IsFinite(point.Y) ||
                            Math.Abs(point.X) > InkCoordinateLimit || Math.Abs(point.Y) > InkCoordinateLimit)
                            throw new InvalidOperationException("Ink coordinates are outside the supported finite range.");
                    }
                }
            }

            var pagesById = section.Pages.ToDictionary(page => page.Id, StringComparer.Ordinal);
            foreach (var page in section.Pages)
            {
                var seen = new HashSet<string>(StringComparer.Ordinal) { page.Id };
                var parentId = page.ParentPageId;
                while (parentId is not null)
                {
                    if (!pagesById.TryGetValue(parentId, out var parent))
                        throw new InvalidOperationException($"Page '{page.Title}' has a missing or cross-section parent.");
                    if (!seen.Add(parent.Id))
                        throw new InvalidOperationException($"Page hierarchy contains a cycle at '{parent.Title}'.");
                    parentId = parent.ParentPageId;
                }
            }
        }
    }

    internal static void ValidatePageArtifact(HavenRichPage page)
    {
        var expectedApplication = page.PageType switch
        {
            HavenRichPageTypes.Native => null,
            HavenRichPageTypes.Write => "write",
            HavenRichPageTypes.Canvas => "canvas",
            _ => throw new InvalidOperationException($"Board page type '{page.PageType}' is not supported.")
        };
        if (expectedApplication is null)
        {
            if (page.EmbeddedArtifact is not null)
                throw new InvalidOperationException("A Native Board page cannot contain an external artifact reference.");
            return;
        }
        if (page.EmbeddedArtifact is null)
        {
            throw new InvalidOperationException($"A {page.PageType} Board page requires an owner artifact reference.");
        }
        page.EmbeddedArtifact.Validate();
        if (!string.Equals(page.EmbeddedArtifact.ApplicationId, expectedApplication, StringComparison.Ordinal))
            throw new InvalidOperationException($"A {page.PageType} Board page must reference a {expectedApplication} artifact.");
    }

    private static void ValidateBlock(HavenRichBlock block)
    {
        if (!HavenRichBlockLayouts.IsSupported(block.Layout))
            throw new InvalidOperationException($"Block layout '{block.Layout}' is not supported.");
        if (!double.IsFinite(block.X) || !double.IsFinite(block.Y) ||
            Math.Abs(block.X) > HavenRichBlockLayoutGeometry.CoordinateLimit ||
            Math.Abs(block.Y) > HavenRichBlockLayoutGeometry.CoordinateLimit ||
            !double.IsFinite(block.Width) || !double.IsFinite(block.Height) ||
            block.Width is <= 0 or > HavenRichBlockLayoutGeometry.CoordinateLimit ||
            block.Height is <= 0 or > HavenRichBlockLayoutGeometry.CoordinateLimit ||
            Math.Abs((long)block.ZOrder) > HavenRichBlockLayoutGeometry.ZOrderLimit)
            throw new InvalidOperationException("Block placement geometry is outside the supported range.");
        if (block.Kind == HavenRichBlockKind.Table)
        {
            if (block.Table is null || block.Table.Rows.Count == 0)
                throw new InvalidOperationException("Table blocks must contain at least one row.");
            var width = block.Table.Rows[0].Cells.Count;
            if (width == 0)
                throw new InvalidOperationException("Table rows must contain at least one cell.");
            if (block.Table.Rows.Any(row => row.Cells.Count != width))
                throw new InvalidOperationException("Table rows must be rectangular.");
            ValidateTableMerges(block.Table);
            if (block.Table.BorderWidth < 0 || block.Table.BorderWidth > 32)
                throw new InvalidOperationException("Table border width is outside the supported range.");
            if (block.Table.CornerRadius < 0 || block.Table.CornerRadius > 128)
                throw new InvalidOperationException("Table corner radius is outside the supported range.");
            if (!double.IsFinite(block.Table.Width) || block.Table.Width is < 0 or > 5000 ||
                !Enum.IsDefined(block.Table.Alignment))
                throw new InvalidOperationException("Table width or alignment is outside the supported range.");
            if (block.Table.ColumnWidths.Any(w => w is < 0 or > 5000) || block.Table.RowHeights.Any(h => h is < 0 or > 5000))
                throw new InvalidOperationException("Table track sizes are outside the supported range.");
        }
        if (block.Kind == HavenRichBlockKind.Image)
        {
            if (block.Image is null)
                throw new InvalidOperationException("Image blocks must carry image data.");
            HavenRichImage.Validate(block.Image);
        }
        if (block.Kind == HavenRichBlockKind.Graph)
        {
            if (block.Graph is null)
                throw new InvalidOperationException("Graph blocks must carry graph data.");
            HavenRichGraphValidator.Validate(block.Graph);
        }
        if (block.IndentLevel < -1 || block.IndentLevel > 12)
            throw new InvalidOperationException("Block indent level is outside the supported range.");
        if (block.LineSpacing is < 0 or > 10)
            throw new InvalidOperationException("Block line spacing is outside the supported range.");
        if (block.SpaceBefore is < 0 or > 1000 || block.SpaceAfter is < 0 or > 1000)
            throw new InvalidOperationException("Block paragraph spacing is outside the supported range.");
    }

    private static void ValidateTableMerges(HavenRichTable table)
    {
        var rows = table.Rows.Count;
        var columns = table.Rows[0].Cells.Count;
        var cellsById = new Dictionary<string, (int Row, int Column, HavenRichTableCell Cell)>(StringComparer.Ordinal);
        for (var row = 0; row < rows; row++)
            for (var column = 0; column < columns; column++)
            {
                var cell = table.Rows[row].Cells[column];
                RequireId(cell.Id, "Table cell");
                if (!cellsById.TryAdd(cell.Id, (row, column, cell)))
                    throw new InvalidOperationException($"Table cell ID '{cell.Id}' is duplicated.");
                if (cell.Text is null || cell.Runs is null)
                    throw new InvalidOperationException("Table cells must contain valid text and run collections.");
                if (cell.RowSpan < 1 || cell.ColumnSpan < 1 || cell.RowSpan > rows || cell.ColumnSpan > columns)
                    throw new InvalidOperationException("Table cell spans are outside the table dimensions.");
                if (cell.CoveredByCellId is not null && (cell.RowSpan != 1 || cell.ColumnSpan != 1))
                    throw new InvalidOperationException("Covered table slots cannot define their own spans.");
            }

        var coveredByAnchor = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (anchorId, entry) in cellsById)
        {
            var anchor = entry.Cell;
            if (anchor.CoveredByCellId is not null) continue;
            if (entry.Row + anchor.RowSpan > rows || entry.Column + anchor.ColumnSpan > columns)
                throw new InvalidOperationException("A merged table cell extends outside the table.");
            for (var row = entry.Row; row < entry.Row + anchor.RowSpan; row++)
                for (var column = entry.Column; column < entry.Column + anchor.ColumnSpan; column++)
                {
                    if (row == entry.Row && column == entry.Column) continue;
                    var covered = table.Rows[row].Cells[column];
                    if (!string.Equals(covered.CoveredByCellId, anchorId, StringComparison.Ordinal))
                        throw new InvalidOperationException("Merged table cells must cover one exact rectangular region.");
                    coveredByAnchor.Add(covered.Id, anchorId);
                }
        }

        foreach (var entry in cellsById.Values)
        {
            var cell = entry.Cell;
            if (cell.CoveredByCellId is null) continue;
            if (!cellsById.TryGetValue(cell.CoveredByCellId, out var anchor) ||
                anchor.Cell.CoveredByCellId is not null ||
                !coveredByAnchor.TryGetValue(cell.Id, out var expectedAnchor) ||
                !string.Equals(expectedAnchor, cell.CoveredByCellId, StringComparison.Ordinal))
                throw new InvalidOperationException("Covered table slots must belong to a valid merged anchor.");
        }
    }

    private static void RequireId(string value, string label)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 128)
            throw new InvalidOperationException($"{label} ID must contain 1 to 128 characters.");
    }
}

/// <summary>
/// Mutations over <see cref="HavenRichNotes"/>, mirroring the shared
/// <c>BoardsWorkspaceService</c> hierarchy/list/table/canvas semantics.
/// Every mutation bumps <see cref="HavenRichNotes.Version"/> so stale autosaves can be rejected.
/// </summary>
public static partial class HavenRichNotesOps
{
    public static HavenRichSection AddSection(HavenRichNotes notes, string? title = null)
    {
        ArgumentNullException.ThrowIfNull(notes);
        var section = new HavenRichSection
        {
            Title = string.IsNullOrWhiteSpace(title) ? $"Section {notes.Sections.Count + 1}" : title.Trim(),
            Pages = [new HavenRichPage { Title = "New page" }]
        };
        notes.Sections.Add(section);
        Touch(notes);
        return section;
    }

    public static HavenRichPage AddPage(HavenRichNotes notes, string sectionId, string? title = null)
    {
        var section = RequireSection(notes, sectionId);
        var page = new HavenRichPage
        {
            Title = string.IsNullOrWhiteSpace(title) ? $"Page {section.Pages.Count + 1}" : title.Trim(),
            Order = section.Pages.Count
        };
        section.Pages.Add(page);
        NormalizePages(section);
        Touch(notes);
        return page;
    }

    /// <summary>Adds a page that references an owner-managed app artifact without copying its content.</summary>
    public static HavenRichPage AddEmbeddedArtifactPage(HavenRichNotes notes, string sectionId,
        string pageType, BoardEmbeddedArtifactReference reference, string? title = null,
        string? parentPageId = null)
    {
        ArgumentNullException.ThrowIfNull(notes);
        ArgumentNullException.ThrowIfNull(reference);
        var section = RequireSection(notes, sectionId);
        var detachedReference = CopyArtifactReference(reference);
        HavenRichNotesValidator.ValidatePageArtifact(new HavenRichPage
        {
            PageType = pageType,
            EmbeddedArtifact = detachedReference
        });
        if (parentPageId is not null && !section.Pages.Any(page => page.Id == parentPageId))
            throw new KeyNotFoundException("Parent page was not found in the selected section.");

        var page = new HavenRichPage
        {
            Title = string.IsNullOrWhiteSpace(title) ? $"Page {section.Pages.Count + 1}" : title.Trim(),
            Order = section.Pages.Count,
            ParentPageId = parentPageId,
            PageType = pageType,
            EmbeddedArtifact = detachedReference,
            Blocks = []
        };
        section.Pages.Add(page);
        NormalizePages(section);
        Touch(notes);
        return page;
    }

    /// <summary>Replaces a page's pinned app reference after the caller has obtained it from its owner.</summary>
    public static void SetEmbeddedArtifact(HavenRichNotes notes, string pageId,
        string pageType, BoardEmbeddedArtifactReference? reference)
    {
        ArgumentNullException.ThrowIfNull(notes);
        var page = RequirePage(notes, pageId);
        var detachedReference = reference is null ? null : CopyArtifactReference(reference);
        HavenRichNotesValidator.ValidatePageArtifact(new HavenRichPage { PageType = pageType, EmbeddedArtifact = detachedReference });
        if (string.Equals(page.PageType, pageType, StringComparison.Ordinal) &&
            Equals(page.EmbeddedArtifact, detachedReference))
            return;
        page.PageType = pageType;
        page.EmbeddedArtifact = detachedReference;
        Touch(notes);
    }

    public static HavenRichPage AddSubpage(HavenRichNotes notes, string sectionId, string parentPageId, string? title = null)
    {
        var section = RequireSection(notes, sectionId);
        _ = section.Pages.FirstOrDefault(page => page.Id == parentPageId)
            ?? throw new KeyNotFoundException("Parent page was not found in the selected section.");
        var page = AddPageWithoutTouch(section, title);
        page.ParentPageId = parentPageId;
        Touch(notes);
        return page;
    }

    /// <summary>Changes a page's parent while keeping its stable ID and rejecting cycles/cross-section links.</summary>
    public static void SetPageParent(HavenRichNotes notes, string pageId, string? parentPageId)
    {
        ArgumentNullException.ThrowIfNull(notes);
        var section = notes.Sections.FirstOrDefault(candidate => candidate.Pages.Any(page => page.Id == pageId))
            ?? throw new KeyNotFoundException("Rich board page was not found.");
        var page = section.Pages.Single(item => item.Id == pageId);
        if (parentPageId is not null)
        {
            _ = section.Pages.FirstOrDefault(item => item.Id == parentPageId)
                ?? throw new KeyNotFoundException("Parent page was not found in the selected section.");
            for (var ancestor = parentPageId; ancestor is not null; )
            {
                if (ancestor == pageId)
                    throw new InvalidOperationException("A page cannot be nested beneath itself or its descendant.");
                ancestor = section.Pages.First(item => item.Id == ancestor).ParentPageId;
            }
        }
        if (page.ParentPageId == parentPageId) return;
        page.ParentPageId = parentPageId;
        Touch(notes);
    }

    public static void SetCanvasLocked(HavenRichNotes notes, string pageId, bool locked)
    {
        var page = RequirePage(notes, pageId);
        if (page.IsCanvasLocked == locked) return;
        page.IsCanvasLocked = locked;
        Touch(notes);
    }

    /// <summary>Changes placement capability without converting blocks or changing their saved geometry.</summary>
    public static void SetPageLayoutMode(HavenRichNotes notes, string pageId, string layoutMode)
    {
        var page = RequirePage(notes, pageId);
        if (page.PageType != HavenRichPageTypes.Native)
            throw new InvalidOperationException("Layout mode applies only to Native Board pages.");
        if (!HavenRichPageLayoutModes.IsSupported(layoutMode))
            throw new ArgumentOutOfRangeException(nameof(layoutMode), layoutMode, "Unsupported page layout mode.");
        if (page.LayoutMode == layoutMode) return;
        page.LayoutMode = layoutMode;
        Touch(notes);
    }

    /// <summary>Explicitly places or repositions a block in page coordinates.</summary>
    public static void PlaceBlock(
        HavenRichNotes notes, string pageId, string blockId,
        double x, double y, double width, double height, int zOrder)
    {
        var page = RequirePage(notes, pageId);
        if (page.PageType != HavenRichPageTypes.Native)
            throw new InvalidOperationException("Free placement applies only to Native Board pages.");
        if (page.LayoutMode != HavenRichPageLayoutModes.Unlocked)
            throw new InvalidOperationException("Free placement is available only in Unlocked layout mode.");
        ValidateBlockPlacement(x, y, width, height, zOrder);
        var block = RequireBlock(notes, pageId, blockId);
        if (block.Layout == HavenRichBlockLayouts.Absolute &&
            block.X == x && block.Y == y && block.Width == width && block.Height == height && block.ZOrder == zOrder)
            return;
        block.Layout = HavenRichBlockLayouts.Absolute;
        block.X = x;
        block.Y = y;
        block.Width = width;
        block.Height = height;
        block.ZOrder = zOrder;
        Touch(notes);
    }

    /// <summary>Explicitly returns an absolute block to ordered flow while retaining its last geometry.</summary>
    public static void SetBlockFlowLayout(HavenRichNotes notes, string pageId, string blockId)
    {
        var page = RequirePage(notes, pageId);
        if (page.PageType != HavenRichPageTypes.Native)
            throw new InvalidOperationException("Block placement applies only to Native Board pages.");
        var block = RequireBlock(notes, pageId, blockId);
        if (block.Layout == HavenRichBlockLayouts.Flow) return;
        block.Layout = HavenRichBlockLayouts.Flow;
        Touch(notes);
    }

    private static void ValidateBlockPlacement(double x, double y, double width, double height, int zOrder)
    {
        if (!double.IsFinite(x) || !double.IsFinite(y) ||
            Math.Abs(x) > HavenRichBlockLayoutGeometry.CoordinateLimit ||
            Math.Abs(y) > HavenRichBlockLayoutGeometry.CoordinateLimit ||
            !double.IsFinite(width) || !double.IsFinite(height) ||
            width is <= 0 or > HavenRichBlockLayoutGeometry.CoordinateLimit ||
            height is <= 0 or > HavenRichBlockLayoutGeometry.CoordinateLimit ||
            Math.Abs((long)zOrder) > HavenRichBlockLayoutGeometry.ZOrderLimit)
            throw new ArgumentOutOfRangeException(nameof(width), "Block placement geometry is outside the supported range.");
    }

    private static HavenRichPage AddPageWithoutTouch(HavenRichSection section, string? title)
    {
        var page = new HavenRichPage
        {
            Title = string.IsNullOrWhiteSpace(title) ? $"Page {section.Pages.Count + 1}" : title.Trim(),
            Order = section.Pages.Count
        };
        section.Pages.Add(page);
        NormalizePages(section);
        return page;
    }

    private static BoardEmbeddedArtifactReference CopyArtifactReference(BoardEmbeddedArtifactReference reference)
    {
        reference.Validate();
        var revision = reference.ExpectedRevision.Sequence is { } sequence
            ? new BoardEmbeddedArtifactRevision(sequence, null)
            : new BoardEmbeddedArtifactRevision(null, reference.ExpectedRevision.VersionId);
        return new BoardEmbeddedArtifactReference(reference.ApplicationId, reference.ArtifactId, revision);
    }

    public static void RenameSection(HavenRichNotes notes, string sectionId, string title)
    {
        RequireSection(notes, sectionId).Title =
            string.IsNullOrWhiteSpace(title) ? "Untitled section" : title.Trim();
        Touch(notes);
    }

    /// <summary>Removes a section; refuses the final section so the document stays valid.</summary>
    public static bool RemoveSection(HavenRichNotes notes, string sectionId)
    {
        ArgumentNullException.ThrowIfNull(notes);
        if (notes.Sections.Count <= 1)
            return false;
        var removed = notes.Sections.RemoveAll(s => s.Id == sectionId) > 0;
        if (removed) Touch(notes);
        return removed;
    }

    /// <summary>Deep-copies a section with fresh stable ids.</summary>
    public static HavenRichSection DuplicateSection(HavenRichNotes notes, string sectionId)
    {
        var source = RequireSection(notes, sectionId);
        var copy = CloneWithFreshIds(source);
        copy.Title = source.Title + " copy";
        notes.Sections.Insert(notes.Sections.IndexOf(source) + 1, copy);
        Touch(notes);
        return copy;
    }

    /// <summary>Removes a page; refuses the final page of its section.</summary>
    public static bool RemovePage(HavenRichNotes notes, string pageId)
    {
        ArgumentNullException.ThrowIfNull(notes);
        var section = notes.Sections.FirstOrDefault(s => s.Pages.Any(p => p.Id == pageId));
        if (section is null)
            return false;
        if (section.Pages.Count <= 1)
            return false;
        var removedPage = section.Pages.Single(page => page.Id == pageId);
        foreach (var child in section.Pages.Where(page => page.ParentPageId == pageId))
            child.ParentPageId = removedPage.ParentPageId;
        var removed = section.Pages.RemoveAll(p => p.Id == pageId) > 0;
        if (removed)
        {
            NormalizePages(section);
            Touch(notes);
        }
        return removed;
    }

    /// <summary>Deep-copies a page with fresh stable ids.</summary>
    public static HavenRichPage DuplicatePage(HavenRichNotes notes, string pageId)
    {
        var section = notes.Sections.FirstOrDefault(s => s.Pages.Any(p => p.Id == pageId))
            ?? throw new KeyNotFoundException("Rich board page was not found.");
        var source = section.Pages.Single(p => p.Id == pageId);
        var copy = CloneWithFreshIds(source);
        copy.Title = source.Title + " copy";
        copy.Order = section.Pages.Count;
        section.Pages.Add(copy);
        NormalizePages(section);
        Touch(notes);
        return copy;
    }

    private static readonly System.Text.Json.JsonSerializerOptions CloneJson = new(System.Text.Json.JsonSerializerDefaults.Web);

    private static T CloneWithFreshIds<T>(T value)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(value, CloneJson);
        var copy = System.Text.Json.JsonSerializer.Deserialize<T>(json, CloneJson)
            ?? throw new InvalidDataException("Section/page clone failed.");
        if (copy is HavenRichSection section)
            FreshSectionIds(section);
        else if (copy is HavenRichPage page)
            FreshPageIds(page);
        else if (copy is HavenRichBlock block)
            FreshBlockIds(block);
        return copy;
    }

    private static void FreshSectionIds(HavenRichSection section)
    {
        section.Id = "sec-" + Guid.NewGuid().ToString("N")[..12];
        var pageIds = section.Pages.ToDictionary(page => page.Id,
            _ => "page-" + Guid.NewGuid().ToString("N")[..12], StringComparer.Ordinal);
        foreach (var page in section.Pages)
        {
            var oldId = page.Id;
            var oldParent = page.ParentPageId;
            FreshPageIds(page);
            page.Id = pageIds[oldId];
            page.ParentPageId = oldParent is not null && pageIds.TryGetValue(oldParent, out var parentId)
                ? parentId : null;
        }
    }

    private static void FreshPageIds(HavenRichPage page)
    {
        page.Id = "page-" + Guid.NewGuid().ToString("N")[..12];
        foreach (var block in page.Blocks)
            FreshBlockIds(block);
        foreach (var canvasObject in page.Canvas)
            canvasObject.Id = "canvas-" + Guid.NewGuid().ToString("N")[..12];
    }

    // Shared by existing page/section duplication and block duplication.
    // Style IDs and content-addressed payload references are references, not new
    // owned identities: preserve them exactly while deep-copying their metadata.
    private static void FreshBlockIds(HavenRichBlock block)
    {
        block.Id = "block-" + Guid.NewGuid().ToString("N")[..12];
        foreach (var item in block.Items)
            item.Id = "item-" + Guid.NewGuid().ToString("N")[..12];
        if (block.Table is not null)
        {
            var cells = block.Table.Rows.SelectMany(row => row.Cells).ToArray();
            var cellIds = cells.ToDictionary(cell => cell.Id,
                _ => "cell-" + Guid.NewGuid().ToString("N")[..12], StringComparer.Ordinal);
            foreach (var cell in cells)
            {
                cell.Id = cellIds[cell.Id];
                if (cell.CoveredByCellId is { } anchorId && cellIds.TryGetValue(anchorId, out var newAnchorId))
                    cell.CoveredByCellId = newAnchorId;
            }
        }
        if (block.Image is not null)
            block.Image.Id = "img-" + Guid.NewGuid().ToString("N")[..12];
        if (block.Graph is not null)
        {
            block.Graph.GraphId = "graph-" + Guid.NewGuid().ToString("N")[..12];
            foreach (var expression in block.Graph.Expressions)
                expression.Id = "expr-" + Guid.NewGuid().ToString("N")[..12];
            foreach (var slider in block.Graph.Sliders)
                slider.VariableId = "variable-" + Guid.NewGuid().ToString("N")[..12];
        }
        if (block.Attachment is not null)
            block.Attachment.Id = "att-" + Guid.NewGuid().ToString("N")[..12];
    }

    public static void RenamePage(HavenRichNotes notes, string pageId, string title)
    {
        RequirePage(notes, pageId).Title =
            string.IsNullOrWhiteSpace(title) ? "Untitled page" : title.Trim();
        Touch(notes);
    }

    public static void MoveSection(HavenRichNotes notes, string sectionId, int targetIndex)
    {
        ArgumentNullException.ThrowIfNull(notes);
        var index = notes.Sections.FindIndex(s => s.Id == sectionId);
        if (index < 0) throw new KeyNotFoundException("Rich board section was not found.");
        var section = notes.Sections[index];
        notes.Sections.RemoveAt(index);
        notes.Sections.Insert(Math.Clamp(targetIndex, 0, notes.Sections.Count), section);
        Touch(notes);
    }

    public static void MovePage(HavenRichNotes notes, string pageId, string targetSectionId, int targetIndex)
    {
        ArgumentNullException.ThrowIfNull(notes);
        var source = notes.Sections.FirstOrDefault(s => s.Pages.Any(p => p.Id == pageId))
            ?? throw new KeyNotFoundException("Rich board page was not found.");
        var target = RequireSection(notes, targetSectionId);
        var page = source.Pages.Single(p => p.Id == pageId);
        source.Pages.Remove(page);
        target.Pages.Insert(Math.Clamp(targetIndex, 0, target.Pages.Count), page);
        NormalizePages(source);
        if (!ReferenceEquals(source, target)) NormalizePages(target);
        Touch(notes);
    }

    public static HavenRichBlock AddBlock(HavenRichNotes notes, string pageId, HavenRichBlockKind kind, string? text = null)
    {
        var page = RequirePage(notes, pageId);
        HavenRichBlock block = kind switch
        {
            HavenRichBlockKind.Heading => HavenRichBlock.Heading(
                string.IsNullOrWhiteSpace(text) ? "Heading" : text.Trim()),
            HavenRichBlockKind.Table => new HavenRichBlock { Kind = kind, Table = HavenRichTable.Create(3, 3) },
            HavenRichBlockKind.Checklist or HavenRichBlockKind.BulletList or HavenRichBlockKind.NumberedList =>
                new HavenRichBlock
                {
                    Kind = kind,
                    Items = [new HavenRichListItem { Text = string.IsNullOrWhiteSpace(text) ? "New item" : text.Trim() }]
                },
            _ => HavenRichBlock.Paragraph(text ?? string.Empty)
        };
        block.Order = page.Blocks.Count;
        page.Blocks.Add(block);
        Touch(notes);
        return block;
    }

    /// <summary>Deep-copy a stored block immediately after its source, assigning
    /// fresh owned identities while preserving its full payload and references.</summary>
    public static HavenRichBlock DuplicateBlock(HavenRichNotes notes, string pageId, string blockId)
    {
        var page = RequirePage(notes, pageId);
        var source = RequireBlock(notes, pageId, blockId);
        var copy = CloneWithFreshIds(source);
        page.Blocks.Insert(page.Blocks.IndexOf(source) + 1, copy);
        for (var order = 0; order < page.Blocks.Count; order++) page.Blocks[order].Order = order;
        Touch(notes);
        return copy;
    }

    public static void MoveBlock(HavenRichNotes notes, string pageId, string blockId, int targetIndex)
    {
        var page = RequirePage(notes, pageId);
        var index = page.Blocks.FindIndex(b => b.Id == blockId);
        if (index < 0) throw new KeyNotFoundException("Rich board block was not found.");
        var block = page.Blocks[index];
        page.Blocks.RemoveAt(index);
        page.Blocks.Insert(Math.Clamp(targetIndex, 0, page.Blocks.Count), block);
        for (var order = 0; order < page.Blocks.Count; order++)
            page.Blocks[order].Order = order;
        Touch(notes);
    }

    public static HavenRichBlock SplitTextBlockAt(HavenRichNotes notes, string pageId, string blockId, int caretIndex)
    {
        var page = RequirePage(notes, pageId);
        var block = RequireBlock(notes, pageId, blockId);
        if (block.Kind is not (HavenRichBlockKind.Paragraph or HavenRichBlockKind.Heading))
            throw new InvalidOperationException("Only paragraph and heading text blocks can be split.");
        var text = block.PlainText ?? string.Empty;
        if (caretIndex < 0 || caretIndex > text.Length)
            throw new ArgumentOutOfRangeException(nameof(caretIndex), "The split point is outside the text block.");

        var leftText = text[..caretIndex];
        var rightText = text[caretIndex..];
        List<HavenRichTextRun> leftRuns = [];
        List<HavenRichTextRun> rightRuns = [];
        if (block.Runs.Count > 0)
        {
            if (!string.Equals(string.Concat(block.Runs.Select(run => run.Text)), text, StringComparison.Ordinal))
                throw new InvalidOperationException("Text formatting ranges do not match the block text.");
            SplitRuns(block.Runs, caretIndex, leftRuns, rightRuns);
        }
        block.PlainText = leftText;
        block.Runs = leftRuns;
        var following = new HavenRichBlock
        {
            Kind = block.Kind,
            StyleId = block.StyleId,
            PlainText = rightText,
            Runs = rightRuns,
            Alignment = block.Alignment,
            LineSpacing = block.LineSpacing,
            SpaceBefore = block.SpaceBefore,
            SpaceAfter = block.SpaceAfter,
            IndentLevel = block.IndentLevel,
        };
        page.Blocks.Insert(page.Blocks.IndexOf(block) + 1, following);
        NormalizeBlockOrder(page);
        Touch(notes);
        return following;
    }

    public static string MergeTextBlockWithPrevious(HavenRichNotes notes, string pageId, string blockId)
    {
        var page = RequirePage(notes, pageId);
        var index = page.Blocks.FindIndex(block => block.Id == blockId);
        if (index <= 0)
            throw new InvalidOperationException("There is no previous text block to merge with.");
        var previous = page.Blocks[index - 1];
        var current = page.Blocks[index];
        if (previous.Kind is not (HavenRichBlockKind.Paragraph or HavenRichBlockKind.Heading) ||
            current.Kind is not (HavenRichBlockKind.Paragraph or HavenRichBlockKind.Heading) ||
            previous.Attachment is not null || current.Attachment is not null)
            throw new InvalidOperationException("Only unattached paragraph or heading blocks can be merged.");

        var leftText = previous.PlainText ?? string.Empty;
        var rightText = current.PlainText ?? string.Empty;
        var mergedRuns = EffectiveRuns(notes, previous);
        mergedRuns.AddRange(EffectiveRuns(notes, current));
        previous.PlainText = leftText + rightText;
        previous.Runs = mergedRuns;
        page.Blocks.RemoveAt(index);
        NormalizeBlockOrder(page);
        Touch(notes);
        return previous.Id;
    }

    private static void SplitRuns(IReadOnlyList<HavenRichTextRun> runs, int caretIndex,
        List<HavenRichTextRun> left, List<HavenRichTextRun> right)
    {
        var offset = 0;
        foreach (var run in runs)
        {
            var split = Math.Clamp(caretIndex - offset, 0, run.Text.Length);
            if (split > 0) left.Add(CloneRun(run, run.Text[..split]));
            if (split < run.Text.Length) right.Add(CloneRun(run, run.Text[split..]));
            offset += run.Text.Length;
        }
    }

    private static List<HavenRichTextRun> EffectiveRuns(HavenRichNotes notes, HavenRichBlock block)
    {
        var style = HavenRichStyleResolver.Resolve(notes, block);
        if (block.Runs.Count == 0)
            return string.IsNullOrEmpty(block.PlainText) ? [] :
            [new HavenRichTextRun
            {
                Text = block.PlainText,
                Bold = style.Bold,
                Italic = style.Italic,
                Underline = style.Underline,
                StrikeThrough = style.StrikeThrough,
                Baseline = style.Baseline,
                FontFamily = style.FontFamily,
                FontSize = style.FontSize,
                Foreground = style.Foreground,
                Background = style.Background,
            }];

        if (!string.Equals(string.Concat(block.Runs.Select(run => run.Text)), block.PlainText, StringComparison.Ordinal))
            throw new InvalidOperationException("Text formatting ranges do not match the block text.");

        return block.Runs.Select(run =>
        {
            var copy = CloneRun(run);
            if (!HasStyle(run))
            {
                copy.Bold = style.Bold;
                copy.Italic = style.Italic;
                copy.Underline = style.Underline;
                copy.StrikeThrough = style.StrikeThrough;
                copy.Baseline = style.Baseline;
            }
            if (string.IsNullOrEmpty(copy.FontFamily)) copy.FontFamily = style.FontFamily;
            if (copy.FontSize <= 0) copy.FontSize = style.FontSize;
            if (string.IsNullOrEmpty(copy.Foreground)) copy.Foreground = style.Foreground;
            if (string.IsNullOrEmpty(copy.Background)) copy.Background = style.Background;
            return copy;
        }).ToList();
    }

    private static HavenRichTextRun CloneRun(HavenRichTextRun run, string? text = null) => new()
    {
        Text = text ?? run.Text,
        Bold = run.Bold,
        Italic = run.Italic,
        Underline = run.Underline,
        StrikeThrough = run.StrikeThrough,
        Baseline = run.Baseline,
        FontFamily = run.FontFamily,
        FontSize = run.FontSize,
        Foreground = run.Foreground,
        Background = run.Background,
    };

    private static void NormalizeBlockOrder(HavenRichPage page)
    {
        for (var order = 0; order < page.Blocks.Count; order++) page.Blocks[order].Order = order;
    }

    public static void UpdateParagraphText(HavenRichNotes notes, string pageId, string blockId, string text)
    {
        var block = RequireBlock(notes, pageId, blockId);
        block.PlainText = text ?? string.Empty;
        if (block.Runs.Count == 1 && !HasStyle(block.Runs[0]))
            block.Runs[0].Text = block.PlainText;
        Touch(notes);
    }

    public static void SetBlockRuns(HavenRichNotes notes, string pageId, string blockId, List<HavenRichTextRun> runs)
    {
        ArgumentNullException.ThrowIfNull(runs);
        var block = RequireBlock(notes, pageId, blockId);
        block.Runs = runs;
        block.PlainText = string.Concat(runs.Select(r => r.Text));
        Touch(notes);
    }

    public static void ToggleRunStyle(HavenRichNotes notes, string pageId, string blockId, int runIndex, string style)
    {
        var block = RequireBlock(notes, pageId, blockId);
        if (runIndex < 0 || runIndex >= block.Runs.Count)
            throw new ArgumentOutOfRangeException(nameof(runIndex));
        var run = block.Runs[runIndex];
        switch (style.ToLowerInvariant())
        {
            case "bold": run.Bold = !run.Bold; break;
            case "italic": run.Italic = !run.Italic; break;
            case "underline": run.Underline = !run.Underline; break;
            case "strikethrough": run.StrikeThrough = !run.StrikeThrough; break;
            default: throw new ArgumentException($"Unknown text style '{style}'.", nameof(style));
        }
        Touch(notes);
    }

    public static HavenRichListItem AddListItem(HavenRichNotes notes, string pageId, string blockId, string text)
    {
        var block = RequireBlock(notes, pageId, blockId);
        if (block.Kind is not (HavenRichBlockKind.Checklist or HavenRichBlockKind.BulletList or HavenRichBlockKind.NumberedList))
            throw new InvalidOperationException("List items require a list block.");
        var item = new HavenRichListItem { Text = text ?? string.Empty };
        block.Items.Add(item);
        Touch(notes);
        return item;
    }

    public static bool UpdateListItem(
        HavenRichNotes notes, string pageId, string blockId, string itemId, string? text = null, bool? isChecked = null)
    {
        var block = RequireBlock(notes, pageId, blockId);
        var item = block.Items.FirstOrDefault(i => i.Id == itemId);
        if (item is null) return false;
        if (text is not null) item.Text = text;
        if (isChecked is bool check) item.Checked = check;
        Touch(notes);
        return true;
    }

    public static bool UpdateTableCell(HavenRichNotes notes, string pageId, string blockId, string cellId, string text)
    {
        var block = RequireBlock(notes, pageId, blockId);
        var cell = block.Table?.Rows.SelectMany(r => r.Cells).FirstOrDefault(c => c.Id == cellId);
        if (cell is null) return false;
        cell.Text = text ?? string.Empty;
        Touch(notes);
        return true;
    }

    public static HavenRichInkStroke AddInkStroke(HavenRichNotes notes, string pageId, HavenRichInkStroke stroke)
    {
        ArgumentNullException.ThrowIfNull(stroke);
        var page = RequirePage(notes, pageId);
        page.Ink.Add(stroke);
        Touch(notes);
        return stroke;
    }

    public static void ClearInk(HavenRichNotes notes, string pageId)
    {
        RequirePage(notes, pageId).Ink.Clear();
        Touch(notes);
    }

    public static HavenRichCanvasObject AddCanvasObject(
        HavenRichNotes notes, string pageId, string kind, string? text, double x, double y,
        double width = 260, double height = 160)
    {
        var page = RequirePage(notes, pageId);
        var value = new HavenRichCanvasObject
        {
            Kind = string.IsNullOrWhiteSpace(kind) ? "Text" : kind.Trim(),
            Text = text?.Trim() ?? string.Empty,
            X = Math.Max(0, x),
            Y = Math.Max(0, y),
            Width = Math.Clamp(width, 24, 5000),
            Height = Math.Clamp(height, 24, 5000)
        };
        page.Canvas.Add(value);
        page.CanvasWidth = Math.Max(page.CanvasWidth, value.X + value.Width + 40);
        page.CanvasHeight = Math.Max(page.CanvasHeight, value.Y + value.Height + 40);
        Touch(notes);
        return value;
    }

    public static void AttachToBlock(
        HavenRichNotes notes, string pageId, string blockId, HavenRichAttachmentRef attachment)
    {
        ArgumentNullException.ThrowIfNull(attachment);
        RequireBlock(notes, pageId, blockId).Attachment = attachment;
        Touch(notes);
    }

    /// <summary>
    /// Inserts a fully-formed section preserving caller-supplied stable IDs.
    /// Used by UI merge paths that already own identity (e.g. CUI editor state).
    /// </summary>
    public static HavenRichSection ImportSection(HavenRichNotes notes, string id, string title)
    {
        ArgumentNullException.ThrowIfNull(notes);
        RequireImportId(id, "Section");
        if (notes.Sections.Any(s => s.Id == id))
            throw new InvalidOperationException($"Section '{id}' already exists.");
        var section = new HavenRichSection
        {
            Id = id,
            Title = string.IsNullOrWhiteSpace(title) ? "Untitled section" : title.Trim(),
            Pages = []
        };
        notes.Sections.Add(section);
        Touch(notes);
        return section;
    }

    /// <summary>Inserts a fully-formed page preserving caller-supplied stable IDs.</summary>
    public static HavenRichPage ImportPage(HavenRichSection section, string id, string title)
    {
        ArgumentNullException.ThrowIfNull(section);
        RequireImportId(id, "Page");
        if (section.Pages.Any(p => p.Id == id))
            throw new InvalidOperationException($"Page '{id}' already exists.");
        var page = new HavenRichPage
        {
            Id = id,
            Title = string.IsNullOrWhiteSpace(title) ? "Untitled page" : title.Trim(),
            Order = section.Pages.Count,
            Blocks = []
        };
        section.Pages.Add(page);
        return page;
    }

    /// <summary>Inserts a fully-formed block preserving caller-supplied stable IDs.</summary>
    public static HavenRichBlock ImportBlock(HavenRichPage page, HavenRichBlockKind kind, string id, string? text = null)
    {
        ArgumentNullException.ThrowIfNull(page);
        RequireImportId(id, "Block");
        if (page.Blocks.Any(b => b.Id == id))
            throw new InvalidOperationException($"Block '{id}' already exists.");
        var block = new HavenRichBlock { Id = id, Kind = kind, Order = page.Blocks.Count };
        if (kind == HavenRichBlockKind.Table)
            block.Table = HavenRichTable.Create(3, 3);
        if (text is not null)
            block.PlainText = text;
        page.Blocks.Add(block);
        return block;
    }

    /// <summary>Inserts a list item preserving caller-supplied stable IDs.</summary>
    public static HavenRichListItem ImportListItem(HavenRichBlock block, string id, string text, bool isChecked)
    {
        ArgumentNullException.ThrowIfNull(block);
        RequireImportId(id, "List item");
        if (block.Items.Any(i => i.Id == id))
            throw new InvalidOperationException($"List item '{id}' already exists.");
        var item = new HavenRichListItem { Id = id, Text = text ?? string.Empty, Checked = isChecked };
        block.Items.Add(item);
        return item;
    }

    private static void RequireImportId(string value, string label)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 128)
            throw new InvalidOperationException($"{label} ID must contain 1 to 128 characters.");
    }

    private static bool HasStyle(HavenRichTextRun run) =>
        run.Bold || run.Italic || run.Underline || run.StrikeThrough ||
        run.Baseline != HavenRichBaseline.Normal ||
        !string.IsNullOrEmpty(run.FontFamily) || run.FontSize > 0 ||
        !string.IsNullOrEmpty(run.Foreground) || !string.IsNullOrEmpty(run.Background);

    private static HavenRichSection RequireSection(HavenRichNotes notes, string sectionId)
    {
        ArgumentNullException.ThrowIfNull(notes);
        return notes.Sections.FirstOrDefault(s => s.Id == sectionId)
            ?? throw new KeyNotFoundException("Rich board section was not found.");
    }

    private static HavenRichPage RequirePage(HavenRichNotes notes, string pageId)
    {
        ArgumentNullException.ThrowIfNull(notes);
        return notes.Sections.SelectMany(s => s.Pages).FirstOrDefault(p => p.Id == pageId)
            ?? throw new KeyNotFoundException("Rich board page was not found.");
    }

    private static HavenRichBlock RequireBlock(HavenRichNotes notes, string pageId, string blockId)
    {
        var block = RequirePage(notes, pageId).Blocks.FirstOrDefault(b => b.Id == blockId);
        return block ?? throw new KeyNotFoundException("Rich board block was not found.");
    }

    private static void NormalizePages(HavenRichSection section)
    {
        for (var order = 0; order < section.Pages.Count; order++)
            section.Pages[order].Order = order;
    }

    private static void Touch(HavenRichNotes notes) => notes.Version = checked(notes.Version + 1);

    /// <summary>Advances the monotonic revision after direct structural imports.</summary>
    public static void TouchNotes(HavenRichNotes notes)
    {
        ArgumentNullException.ThrowIfNull(notes);
        Touch(notes);
    }
}

/// <summary>
/// Seeds a v2 rich payload from a v1 task snapshot so migrated boards keep working content.
/// Group titles become section titles; cards become checklist items on one page per group.
/// The original snapshot facet is preserved verbatim alongside the seed.
/// </summary>
public static class HavenRichNotesSeeder
{
    public static HavenRichNotes SeedFromSnapshot(HavenBoardSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var notes = new HavenRichNotes { Title = snapshot.Title, Version = snapshot.Version };
        notes.Sections.Clear();
        foreach (var group in snapshot.Groups)
        {
            var section = new HavenRichSection { Id = "sec-" + group.Id, Title = group.Title };
            section.Pages.Clear();
            var page = new HavenRichPage { Id = "page-" + group.Id, Title = group.Title };
            page.Blocks.Clear();
            var index = 0;
            foreach (var card in group.Cards)
            {
                page.Blocks.Add(new HavenRichBlock
                {
                    Id = "block-" + card.Id,
                    Kind = HavenRichBlockKind.Checklist,
                    Order = index++,
                    PlainText = card.Title,
                    Items = [new HavenRichListItem { Id = "item-" + card.Id, Text = card.Title }]
                });
            }
            if (page.Blocks.Count == 0)
                page.Blocks.Add(HavenRichBlock.Paragraph());
            section.Pages.Add(page);
            notes.Sections.Add(section);
        }
        if (notes.Sections.Count == 0)
            notes.Sections.Add(new HavenRichSection());
        HavenRichNotesValidator.Validate(notes);
        return notes;
    }
}
