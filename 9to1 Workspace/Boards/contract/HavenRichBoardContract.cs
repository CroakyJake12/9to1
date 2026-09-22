namespace CakeOS.Apps.Boards.Contract;

/// <summary>
/// Self-contained rich-notes payload stored inside <c>.9to1board</c> schema v2.
/// Mirrors the semantics of the mature shared <c>NotesDocument</c> model
/// (sections/pages/blocks/runs/lists/tables/ink/canvas/attachments) without
/// taking a dependency on the shared tree, keeping the contract isolated.
/// </summary>
public sealed class HavenRichNotes
{
    public string Title { get; set; } = "Untitled board";
    public long Version { get; set; } = 1;
    public List<HavenRichSection> Sections { get; set; } = [new HavenRichSection()];

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
    public string Title { get; set; } = "Page 1";
    public int Order { get; set; }
    public double CanvasWidth { get; set; } = 1200;
    public double CanvasHeight { get; set; } = 900;
    public List<HavenRichBlock> Blocks { get; set; } = [new HavenRichBlock()];
    public List<HavenRichInkStroke> Ink { get; set; } = [];
    public List<HavenRichCanvasObject> Canvas { get; set; } = [];
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
    Divider = 7
}

public sealed class HavenRichBlock
{
    public string Id { get; set; } = "block-" + Guid.NewGuid().ToString("N")[..12];
    public HavenRichBlockKind Kind { get; set; } = HavenRichBlockKind.Paragraph;
    public int Order { get; set; }
    public string StyleId { get; set; } = "normal";
    public string PlainText { get; set; } = string.Empty;
    public List<HavenRichTextRun> Runs { get; set; } = [];
    public List<HavenRichListItem> Items { get; set; } = [];
    public HavenRichTable? Table { get; set; }
    public HavenRichAttachmentRef? Attachment { get; set; }

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
}

public sealed class HavenRichListItem
{
    public string Id { get; set; } = "item-" + Guid.NewGuid().ToString("N")[..12];
    public string Text { get; set; } = string.Empty;
    public bool Checked { get; set; }
    public int Level { get; set; }
}

public sealed class HavenRichTable
{
    public List<HavenRichTableRow> Rows { get; set; } = [];

    public static HavenRichTable Create(int rows, int columns)
    {
        var table = new HavenRichTable();
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
    public string Text { get; set; } = string.Empty;
}

public sealed class HavenRichInkPoint
{
    public double X { get; set; }
    public double Y { get; set; }
}

public sealed class HavenRichInkStroke
{
    public List<HavenRichInkPoint> Points { get; set; } = [];
    public double Width { get; set; } = 2.5;
    public string Color { get; set; } = "#FF1A73E8";
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

        foreach (var section in notes.Sections)
        {
            RequireId(section.Id, "Section");
            if (string.IsNullOrWhiteSpace(section.Title))
                throw new InvalidOperationException("Rich board sections must have non-empty titles.");
            if (section.Pages.Count == 0)
                throw new InvalidOperationException($"Section '{section.Title}' must contain at least one page.");

            foreach (var page in section.Pages)
            {
                RequireId(page.Id, "Page");
                if (string.IsNullOrWhiteSpace(page.Title))
                    throw new InvalidOperationException("Rich board pages must have non-empty titles.");
                var blockIds = new HashSet<string>(StringComparer.Ordinal);
                foreach (var block in page.Blocks)
                {
                    RequireId(block.Id, "Block");
                    if (!blockIds.Add(block.Id))
                        throw new InvalidOperationException($"Block ID '{block.Id}' is duplicated on page '{page.Title}'.");
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
        }
    }

    private static void ValidateBlock(HavenRichBlock block)
    {
        if (block.Kind == HavenRichBlockKind.Table)
        {
            if (block.Table is null || block.Table.Rows.Count == 0)
                throw new InvalidOperationException("Table blocks must contain at least one row.");
            var width = block.Table.Rows[0].Cells.Count;
            if (width == 0)
                throw new InvalidOperationException("Table rows must contain at least one cell.");
            if (block.Table.Rows.Any(row => row.Cells.Count != width))
                throw new InvalidOperationException("Table rows must be rectangular.");
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
public static class HavenRichNotesOps
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

    public static void RenameSection(HavenRichNotes notes, string sectionId, string title)
    {
        RequireSection(notes, sectionId).Title =
            string.IsNullOrWhiteSpace(title) ? "Untitled section" : title.Trim();
        Touch(notes);
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
        run.Bold || run.Italic || run.Underline || run.StrikeThrough;

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
