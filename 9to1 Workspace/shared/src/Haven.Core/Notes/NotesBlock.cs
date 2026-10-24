namespace Haven.Core;

/// <summary>
/// A block within a notes page.
/// </summary>
public sealed class NotesBlock
{
    /// <summary>
    /// Gets or sets the block id.
    /// </summary>
    public Guid Id { get; set; } = Guid.NewGuid();
    /// <summary>
    /// Gets or sets the block kind.
    /// </summary>
    public NotesBlockKind Kind { get; set; } = NotesBlockKind.Paragraph;
    /// <summary>
    /// Gets or sets the block order.
    /// </summary>
    public int Order { get; set; }
    /// <summary>
    /// Gets or sets the style id.
    /// </summary>
    public string StyleId { get; set; } = "normal";
    /// <summary>
    /// Gets or sets the plain text content.
    /// </summary>
    public string PlainText { get; set; } = string.Empty;
    /// <summary>
    /// Gets or sets the rich text runs.
    /// </summary>
    public List<NotesTextRun> Runs { get; set; } = [];
    /// <summary>
    /// Gets or sets the paragraph format.
    /// </summary>
    public NotesParagraphFormat Paragraph { get; set; } = new();
    /// <summary>
    /// Gets or sets the list data.
    /// </summary>
    public NotesListData? List { get; set; }
    /// <summary>
    /// Gets or sets the table data.
    /// </summary>
    public NotesTableData? Table { get; set; }
    /// <summary>
    /// Gets or sets the media data.
    /// </summary>
    public NotesMediaData? Media { get; set; }
    /// <summary>
    /// Gets or sets the equation data.
    /// </summary>
    public NotesEquationData? Equation { get; set; }
    /// <summary>
    /// Gets or sets the HTML data.
    /// </summary>
    public NotesHtmlData? Html { get; set; }
    /// <summary>
    /// Gets or sets the canvas data.
    /// </summary>
    public NotesCanvasData? Canvas { get; set; }
    /// <summary>Full canonical Home drawing bundles. Null retains the legacy canvas model;
    /// an empty collection is a new blank shared drawing, never a demonstration stroke.</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public List<System.Text.Json.JsonElement>? SharedProductivityBundles { get; set; }

    /// <summary>
    /// Gets or sets native editable vector geometry for a shape block.
    /// </summary>
    public DocumentVectorShape? VectorShape { get; set; }
    /// <summary>
    /// Gets or sets the flashcard data.
    /// </summary>
    public NotesFlashcardData? Flashcard { get; set; }
    /// <summary>
    /// Gets or sets additional metadata.
    /// </summary>
    public Dictionary<string, string> Metadata { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Creates a paragraph block.
    /// </summary>
    public static NotesBlock CreateParagraph(string text = "") => new() { PlainText = text };
    /// <summary>
    /// Creates a heading block.
    /// </summary>
    public static NotesBlock Heading(string text = "Heading") => new() { Kind = NotesBlockKind.Heading, PlainText = text, StyleId = "heading-1" };
    /// <summary>
    /// Creates an equation block.
    /// </summary>
    public static NotesBlock EquationBlock() => new()
    {
        Kind = NotesBlockKind.Equation,
        Equation = new NotesEquationData { Source = "x^2 + y^2 = z^2", AccessibleAlternative = "x squared plus y squared equals z squared" }
    };
    /// <summary>
    /// Creates an HTML widget block.
    /// </summary>
    public static NotesBlock HtmlBlock() => new()
    {
        Kind = NotesBlockKind.HtmlWidget,
        Html = new NotesHtmlData { HtmlSource = "<section><h2>Interactive note</h2><p>Edit the source safely.</p></section>" }
    };
    /// <summary>
    /// Creates a table block.
    /// </summary>
    public static NotesBlock TableBlock(int rows = 3, int columns = 3) => new()
    {
        Kind = NotesBlockKind.Table,
        Table = NotesTableData.Create(rows, columns)
    };
    /// <summary>
    /// Creates a canvas block.
    /// </summary>
    public static NotesBlock CanvasBlock() => new()
    {
        Kind = NotesBlockKind.Canvas,
        Canvas = new NotesCanvasData()
    };
    /// <summary>
    /// Creates a flashcard block.
    /// </summary>
    public static NotesBlock FlashcardBlock() => new()
    {
        Kind = NotesBlockKind.Flashcard,
        Flashcard = new NotesFlashcardData { Front = "Question", Back = "Answer" }
    };
}

/// <summary>
/// A rich text run within a block.
/// </summary>
public sealed class NotesTextRun
{
    /// <summary>
    /// Gets or sets the run id.
    /// </summary>
    public Guid Id { get; set; } = Guid.NewGuid();
    /// <summary>
    /// Gets or sets the text content.
    /// </summary>
    public string Text { get; set; } = string.Empty;
    /// <summary>
    /// Gets or sets the font family.
    /// </summary>
    public string FontFamily { get; set; } = "Montserrat";
    /// <summary>
    /// Gets or sets the font size.
    /// </summary>
    public double FontSize { get; set; } = 14;
    /// <summary>
    /// Gets or sets whether bold is applied.
    /// </summary>
    public bool Bold { get; set; }
    /// <summary>
    /// Gets or sets whether italic is applied.
    /// </summary>
    public bool Italic { get; set; }
    /// <summary>
    /// Gets or sets whether underline is applied.
    /// </summary>
    public bool Underline { get; set; }
    /// <summary>
    /// Gets or sets whether strikethrough is applied.
    /// </summary>
    public bool StrikeThrough { get; set; }
    /// <summary>
    /// Gets or sets the foreground colour.
    /// </summary>
    public string Foreground { get; set; } = "#FFEEEEEE";
    /// <summary>
    /// Gets or sets the background colour.
    /// </summary>
    public string Background { get; set; } = "#00000000";
    /// <summary>
    /// Gets or sets the link URL.
    /// </summary>
    public string? Link { get; set; }
    /// <summary>
    /// Gets or sets the language.
    /// </summary>
    public string? Language { get; set; }
}

/// <summary>
/// Paragraph formatting options.
/// </summary>
public sealed class NotesParagraphFormat
{
    /// <summary>
    /// Gets or sets the text alignment.
    /// </summary>
    public NotesTextAlignment Alignment { get; set; }
    /// <summary>
    /// Gets or sets the line spacing.
    /// </summary>
    public double LineSpacing { get; set; } = 1.25;
    /// <summary>
    /// Gets or sets the space before the paragraph.
    /// </summary>
    public double SpaceBefore { get; set; }
    /// <summary>
    /// Gets or sets the space after the paragraph.
    /// </summary>
    public double SpaceAfter { get; set; } = 8;
    /// <summary>
    /// Gets or sets the left indent.
    /// </summary>
    public double IndentLeft { get; set; }
    /// <summary>
    /// Gets or sets the right indent.
    /// </summary>
    public double IndentRight { get; set; }
    /// <summary>
    /// Gets or sets the first line indent.
    /// </summary>
    public double FirstLineIndent { get; set; }
    /// <summary>
    /// Gets or sets whether to keep with the next paragraph.
    /// </summary>
    public bool KeepWithNext { get; set; }
    /// <summary>
    /// Gets or sets whether to insert a page break before.
    /// </summary>
    public bool PageBreakBefore { get; set; }
}

/// <summary>
/// List configuration for a block.
/// </summary>
public sealed class NotesListData
{
    /// <summary>
    /// Gets or sets the list kind.
    /// </summary>
    public NotesListKind Kind { get; set; }
    /// <summary>
    /// Gets or sets the starting number for numbered lists.
    /// </summary>
    public int StartNumber { get; set; } = 1;
    /// <summary>
    /// Gets or sets the list items.
    /// </summary>
    public List<NotesListItem> Items { get; set; } = [];
}

/// <summary>
/// An item within a list.
/// </summary>
public sealed class NotesListItem
{
    /// <summary>
    /// Gets or sets the item id.
    /// </summary>
    public Guid Id { get; set; } = Guid.NewGuid();
    /// <summary>
    /// Gets or sets the item text.
    /// </summary>
    public string Text { get; set; } = string.Empty;
    /// <summary>
    /// Gets or sets whether the item is checked.
    /// </summary>
    public bool Checked { get; set; }
    /// <summary>
    /// Gets or sets the nesting level.
    /// </summary>
    public int Level { get; set; }
}

/// <summary>
/// Table data within a block.
/// </summary>
public sealed class NotesTableData
{
    /// <summary>
    /// Gets or sets the table rows.
    /// </summary>
    public List<NotesTableRow> Rows { get; set; } = [];
    /// <summary>
    /// Gets or sets whether the header row is shown.
    /// </summary>
    public bool HeaderRow { get; set; } = true;
    /// <summary>
    /// Gets or sets whether the header repeats on each page.
    /// </summary>
    public bool RepeatHeader { get; set; }
    /// <summary>
    /// Gets or sets the table style.
    /// </summary>
    public string Style { get; set; } = "grid";

    /// <summary>
    /// Creates a table with the given dimensions.
    /// </summary>
    public static NotesTableData Create(int rows, int columns)
    {
        var table = new NotesTableData();
        for (var row = 0; row < Math.Clamp(rows, 1, 100); row++)
        {
            var value = new NotesTableRow();
            for (var column = 0; column < Math.Clamp(columns, 1, 50); column++)
                value.Cells.Add(new NotesTableCell { Text = row == 0 ? $"Column {column + 1}" : string.Empty });
            table.Rows.Add(value);
        }
        return table;
    }
}

/// <summary>
/// A row within a table.
/// </summary>
public sealed class NotesTableRow
{
    /// <summary>
    /// Gets or sets the row id.
    /// </summary>
    public Guid Id { get; set; } = Guid.NewGuid();
    /// <summary>
    /// Gets or sets the cells.
    /// </summary>
    public List<NotesTableCell> Cells { get; set; } = [];
    /// <summary>
    /// Gets or sets whether this is a header row.
    /// </summary>
    public bool IsHeader { get; set; }
}

/// <summary>
/// A cell within a table row.
/// </summary>
public sealed class NotesTableCell
{
    /// <summary>
    /// Gets or sets the cell id.
    /// </summary>
    public Guid Id { get; set; } = Guid.NewGuid();
    /// <summary>
    /// Gets or sets the cell text.
    /// </summary>
    public string Text { get; set; } = string.Empty;
    /// <summary>
    /// Gets or sets the row span.
    /// </summary>
    public int RowSpan { get; set; } = 1;
    /// <summary>
    /// Gets or sets the column span.
    /// </summary>
    public int ColumnSpan { get; set; } = 1;
    /// <summary>
    /// Gets or sets the background colour.
    /// </summary>
    public string Background { get; set; } = "#00000000";
    /// <summary>
    /// Gets or sets the vertical alignment.
    /// </summary>
    public string VerticalAlignment { get; set; } = "Top";
}

/// <summary>
/// Media attachment data for a block.
/// </summary>
public sealed class NotesMediaData
{
    /// <summary>
    /// Gets or sets the attachment id.
    /// </summary>
    public Guid AttachmentId { get; set; } = Guid.NewGuid();
    /// <summary>
    /// Gets or sets the original file name.
    /// </summary>
    public string OriginalName { get; set; } = string.Empty;
    /// <summary>
    /// Gets or sets the stored file path.
    /// </summary>
    public string StoredPath { get; set; } = string.Empty;
    /// <summary>
    /// Gets or sets the media type.
    /// </summary>
    public string MediaType { get; set; } = "application/octet-stream";
    /// <summary>
    /// Gets or sets the file size in bytes.
    /// </summary>
    public long SizeBytes { get; set; }
    /// <summary>
    /// Gets or sets the SHA-256 hash.
    /// </summary>
    public string Sha256 { get; set; } = string.Empty;
    /// <summary>
    /// Gets or sets the alt text.
    /// </summary>
    public string AltText { get; set; } = string.Empty;
    /// <summary>
    /// Gets or sets the caption.
    /// </summary>
    public string Caption { get; set; } = string.Empty;
    /// <summary>
    /// Gets or sets the wrapping mode.
    /// </summary>
    public string Wrapping { get; set; } = "Inline";
    /// <summary>
    /// Gets or sets the width.
    /// </summary>
    public double Width { get; set; } = 400;
    /// <summary>
    /// Gets or sets the height.
    /// </summary>
    public double Height { get; set; } = 300;
    /// <summary>
    /// Gets or sets the rotation.
    /// </summary>
    public double Rotation { get; set; }
    /// <summary>
    /// Gets or sets the left crop.
    /// </summary>
    public double CropLeft { get; set; }
    /// <summary>
    /// Gets or sets the top crop.
    /// </summary>
    public double CropTop { get; set; }
    /// <summary>
    /// Gets or sets the right crop.
    /// </summary>
    public double CropRight { get; set; }
    /// <summary>
    /// Gets or sets the bottom crop.
    /// </summary>
    public double CropBottom { get; set; }
}

/// <summary>
/// Equation data for a block.
/// </summary>
public sealed class NotesEquationData
{
    /// <summary>
    /// Gets or sets the view mode.
    /// </summary>
    public NotesEquationViewMode ViewMode { get; set; } = NotesEquationViewMode.Split;
    /// <summary>
    /// Gets or sets the LaTeX source.
    /// </summary>
    public string Source { get; set; } = string.Empty;
    /// <summary>
    /// Gets or sets the visual structure JSON.
    /// </summary>
    public string VisualStructureJson { get; set; } = "{}";
    /// <summary>
    /// Gets or sets the rendered text.
    /// </summary>
    public string RenderedText { get; set; } = string.Empty;
    /// <summary>
    /// Gets or sets the error message.
    /// </summary>
    public string Error { get; set; } = string.Empty;
    /// <summary>
    /// Gets or sets the accessible alternative text.
    /// </summary>
    public string AccessibleAlternative { get; set; } = string.Empty;
    /// <summary>
    /// Gets or sets whether the equation is numbered.
    /// </summary>
    public bool Numbered { get; set; }
    /// <summary>
    /// Gets or sets the equation number.
    /// </summary>
    public int? Number { get; set; }
    /// <summary>
    /// Gets or sets the equation label.
    /// </summary>
    public string Label { get; set; } = string.Empty;
    /// <summary>
    /// Gets or sets the macros.
    /// </summary>
    public Dictionary<string, string> Macros { get; set; } = new(StringComparer.Ordinal);
    /// <summary>
    /// Gets or sets the references.
    /// </summary>
    public List<string> References { get; set; } = [];
    /// <summary>
    /// Gets or sets the source ink strokes.
    /// </summary>
    public List<NotesInkStroke> SourceStrokes { get; set; } = [];
}

/// <summary>
/// HTML widget data for a block.
/// </summary>
public sealed class NotesHtmlData
{
    /// <summary>
    /// Gets or sets the view mode.
    /// </summary>
    public NotesHtmlViewMode ViewMode { get; set; } = NotesHtmlViewMode.Split;
    /// <summary>
    /// Gets or sets the HTML source.
    /// </summary>
    public string HtmlSource { get; set; } = string.Empty;
    /// <summary>
    /// Gets or sets the CSS source.
    /// </summary>
    public string CssSource { get; set; } = string.Empty;
    /// <summary>
    /// Gets or sets the JavaScript source.
    /// </summary>
    public string JavaScriptSource { get; set; } = string.Empty;
    /// <summary>
    /// Gets or sets whether scripts are allowed.
    /// </summary>
    public bool AllowScripts { get; set; }
    /// <summary>
    /// Gets or sets whether network access is allowed.
    /// </summary>
    public bool AllowNetwork { get; set; }
    /// <summary>
    /// Gets or sets whether forms are allowed.
    /// </summary>
    public bool AllowForms { get; set; }
    /// <summary>
    /// Gets or sets whether popups are allowed.
    /// </summary>
    public bool AllowPopups { get; set; }
    /// <summary>
    /// Gets or sets the fallback text.
    /// </summary>
    public string FallbackText { get; set; } = string.Empty;
    /// <summary>
    /// Gets or sets the snapshot path.
    /// </summary>
    public string SnapshotPath { get; set; } = string.Empty;
    /// <summary>
    /// Gets or sets the width.
    /// </summary>
    public double Width { get; set; } = 640;
    /// <summary>
    /// Gets or sets the height.
    /// </summary>
    public double Height { get; set; } = 360;
    /// <summary>
    /// Gets or sets the last security error.
    /// </summary>
    public string LastSecurityError { get; set; } = string.Empty;
}

/// <summary>
/// Canvas data for a block.
/// </summary>
public sealed class NotesCanvasData
{
    /// <summary>
    /// Gets or sets the width.
    /// </summary>
    public double Width { get; set; } = 1200;
    /// <summary>
    /// Gets or sets the height.
    /// </summary>
    public double Height { get; set; } = 900;
    /// <summary>
    /// Gets or sets the zoom level.
    /// </summary>
    public double Zoom { get; set; } = 1;
    /// <summary>
    /// Gets or sets the X offset.
    /// </summary>
    public double OffsetX { get; set; }
    /// <summary>
    /// Gets or sets the Y offset.
    /// </summary>
    public double OffsetY { get; set; }
    /// <summary>
    /// Gets or sets whether the canvas is infinite.
    /// </summary>
    public bool Infinite { get; set; }
    /// <summary>
    /// Gets or sets the canvas objects.
    /// </summary>
    public List<NotesCanvasObject> Objects { get; set; } = [];
    /// <summary>
    /// Gets or sets the ink strokes.
    /// </summary>
    public List<NotesInkStroke> Strokes { get; set; } = [];
    /// <summary>
    /// Gets or sets the ghost layers.
    /// </summary>
    public List<NotesGhostLayer> GhostLayers { get; set; } = [];
}

/// <summary>
/// An object on the canvas.
/// </summary>
public sealed class NotesCanvasObject
{
    /// <summary>
    /// Gets or sets the object id.
    /// </summary>
    public Guid Id { get; set; } = Guid.NewGuid();
    /// <summary>
    /// Gets or sets the object kind.
    /// </summary>
    public NotesCanvasObjectKind Kind { get; set; }
    /// <summary>
    /// Gets or sets the text content.
    /// </summary>
    public string Text { get; set; } = string.Empty;
    /// <summary>
    /// Gets or sets the X position.
    /// </summary>
    public double X { get; set; }
    /// <summary>
    /// Gets or sets the Y position.
    /// </summary>
    public double Y { get; set; }
    /// <summary>
    /// Gets or sets the width.
    /// </summary>
    public double Width { get; set; } = 160;
    /// <summary>
    /// Gets or sets the height.
    /// </summary>
    public double Height { get; set; } = 100;
    /// <summary>
    /// Gets or sets the rotation.
    /// </summary>
    public double Rotation { get; set; }
    /// <summary>
    /// Gets or sets the Z-index.
    /// </summary>
    public int ZIndex { get; set; }
    /// <summary>
    /// Gets or sets whether the object is locked.
    /// </summary>
    public bool Locked { get; set; }
    /// <summary>
    /// Gets or sets the group id.
    /// </summary>
    public Guid? GroupId { get; set; }
    /// <summary>
    /// Gets or sets the source connector object id.
    /// </summary>
    public Guid? FromObjectId { get; set; }
    /// <summary>
    /// Gets or sets the target connector object id.
    /// </summary>
    public Guid? ToObjectId { get; set; }
    /// <summary>
    /// Gets or sets the style JSON.
    /// </summary>
    public string StyleJson { get; set; } = "{}";
    /// <summary>Native editable vector geometry when this object is a custom shape.</summary>
    public DocumentVectorShape? VectorShape { get; set; }
}

/// <summary>Versioned Boards page extension stored in the EXISTING Notes metadata
/// dictionary, which older schema1 writers preserve opaquely. The stable Ink
/// CanvasObject owns placement; Home owns all drawing bundles. No DTO field or
/// private document store is introduced. Unknown raw envelope fields round-trip.</summary>
[System.Text.Json.Serialization.JsonConverter(typeof(NotesSharedInkLayer.Converter))]
public sealed class NotesSharedInkLayer
{
    private readonly System.Text.Json.JsonElement _raw;
    public const int CurrentVersion = 1;
    public Guid PageId { get; }
    public Guid LayerId { get; }
    public IReadOnlyList<System.Text.Json.JsonElement> Bundles { get; }
    private NotesSharedInkLayer(System.Text.Json.JsonElement source)
    {
        if (source.ValueKind != System.Text.Json.JsonValueKind.Object || source.GetRawText().Length > 16 * 1024 * 1024 ||
            source.EnumerateObject().GroupBy(field => field.Name, StringComparer.Ordinal).Any(group => group.Count() != 1) ||
            !source.TryGetProperty("version", out var version) || version.ValueKind != System.Text.Json.JsonValueKind.Number || !version.TryGetInt32(out var number) || number != CurrentVersion ||
            !source.TryGetProperty("pageId", out var page) || page.ValueKind != System.Text.Json.JsonValueKind.String || !page.TryGetGuid(out var pageId) || pageId == Guid.Empty ||
            !source.TryGetProperty("layerId", out var layer) || layer.ValueKind != System.Text.Json.JsonValueKind.String || !layer.TryGetGuid(out var layerId) || layerId == Guid.Empty ||
            !source.TryGetProperty("bundles", out var bundles) || bundles.ValueKind != System.Text.Json.JsonValueKind.Array || bundles.GetArrayLength() > 256)
            throw new InvalidDataException("Unsupported or malformed page drawing layer; preserve its original document.");
        var captured = bundles.EnumerateArray().Select(value => value.ValueKind == System.Text.Json.JsonValueKind.Object
            ? value.Clone() : throw new InvalidDataException("A page drawing requires complete Home object bundles.")).ToArray();
        PageId = pageId; LayerId = layerId; Bundles = Array.AsReadOnly(captured); _raw = source.Clone();
    }
    public const string MetadataPrefix = "boards.page-ink.";
    public static string MetadataKey(Guid pageId) => pageId != Guid.Empty ? MetadataPrefix + pageId.ToString("D") : throw new ArgumentException("A stable page ID is required.");
    public static string? ReadMetadataKey(NotesDocument document, Guid pageId)
    {
        var key = MetadataKey(pageId);
        var keys = document.Metadata.Keys.Where(value => string.Equals(value, key, StringComparison.OrdinalIgnoreCase)).Take(2).ToArray();
        if (keys.Length == 0) return null;
        if (keys.Length != 1) throw new InvalidDataException("The retained page ink metadata key is ambiguous; no keys were changed.");
        return keys[0];
    }
    public static string? ReadMetadata(NotesDocument document, Guid pageId)
    {
        var key = ReadMetadataKey(document, pageId);
        if (key is null) return null;
        var raw = document.Metadata[key];
        if (raw is null || raw.Length > 16 * 1024 * 1024)
            throw new InvalidDataException("The retained page ink extension exceeds its supported bound.");
        return raw;
    }
    public static IReadOnlyList<NotesSharedInkLayer> ReadAll(NotesDocument document)
    {
        var result = new List<NotesSharedInkLayer>(); var pages = new HashSet<Guid>();
        var knownPages = document.Sections.SelectMany(section => section.Pages).Select(page => page.Id).ToHashSet();
        foreach (var key in document.Metadata.Keys.Where(value => value.StartsWith(MetadataPrefix, StringComparison.OrdinalIgnoreCase)))
        {
            if (!Guid.TryParseExact(key[MetadataPrefix.Length..], "D", out var pageId) || pageId == Guid.Empty || !pages.Add(pageId) ||
                !knownPages.Contains(pageId))
                throw new InvalidDataException("A retained page ink extension has an invalid, duplicate or absent page binding. Repair explicitly; its original payload is preserved.");
            var raw = document.Metadata[key];
            if (raw is null || raw.Length > 16 * 1024 * 1024) throw new InvalidDataException("The retained page ink extension exceeds its supported bound.");
            using var parsed = System.Text.Json.JsonDocument.Parse(raw);
            var value = Capture(parsed.RootElement);
            if (value.PageId != pageId) throw new InvalidDataException("The retained page ink key and envelope identify different pages.");
            result.Add(value);
        }
        return result.AsReadOnly();
    }
    public static NotesSharedInkLayer? Read(NotesDocument document, Guid pageId)
    {
        var raw = ReadMetadata(document, pageId);
        if (raw is null) return null;
        using var parsed = System.Text.Json.JsonDocument.Parse(raw);
        var value = Capture(parsed.RootElement);
        if (value.PageId != pageId) throw new InvalidDataException("The page ink extension belongs to another page; no automatic rebinding is allowed.");
        return value;
    }
    public static NotesSharedInkLayer Capture(System.Text.Json.JsonElement source) => new(source);
    public System.Text.Json.JsonElement ToJson() => _raw.Clone();
    public static NotesSharedInkLayer Create(Guid pageId, Guid layerId, IReadOnlyList<System.Text.Json.JsonElement> bundles) =>
        new(System.Text.Json.JsonSerializer.SerializeToElement(new { version = CurrentVersion, pageId, layerId, bundles = CaptureBundles(bundles) }));
    public NotesSharedInkLayer WithBundles(IReadOnlyList<System.Text.Json.JsonElement> bundles)
    {
        var copy = System.Text.Json.Nodes.JsonNode.Parse(_raw.GetRawText())!.AsObject();
        copy["bundles"] = System.Text.Json.JsonSerializer.SerializeToNode(CaptureBundles(bundles));
        return new(System.Text.Json.JsonSerializer.SerializeToElement(copy));
    }
    private static IReadOnlyList<System.Text.Json.JsonElement> CaptureBundles(IReadOnlyList<System.Text.Json.JsonElement> source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var captured = new List<System.Text.Json.JsonElement>(); var characters = 0L;
        foreach (var value in source)
        {
            if (captured.Count == 256 || value.ValueKind != System.Text.Json.JsonValueKind.Object ||
                (characters += value.GetRawText().Length) > 16 * 1024 * 1024)
                throw new InvalidDataException("The page drawing exceeds its supported Home bundle bound.");
            captured.Add(value.Clone());
        }
        return captured.AsReadOnly();
    }
    public sealed class Converter : System.Text.Json.Serialization.JsonConverter<NotesSharedInkLayer>
    {
        public override NotesSharedInkLayer Read(ref System.Text.Json.Utf8JsonReader reader, Type type, System.Text.Json.JsonSerializerOptions options)
        { using var document = System.Text.Json.JsonDocument.ParseValue(ref reader); return Capture(document.RootElement); }
        public override void Write(System.Text.Json.Utf8JsonWriter writer, NotesSharedInkLayer value, System.Text.Json.JsonSerializerOptions options) => value._raw.WriteTo(writer);
    }
}
