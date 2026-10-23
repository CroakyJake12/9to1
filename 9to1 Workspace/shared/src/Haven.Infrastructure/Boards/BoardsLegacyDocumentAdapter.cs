using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CakeOS.Apps.Boards.Contract;
using Haven.Application;
using Haven.Core;

namespace Haven.Infrastructure;

/// <summary>Already-authorised bytes only. The retained portable contract supplies validation;
/// its file store, migration writers, sessions and UI are never constructed. Original export
/// returns the original source, not a serialization of its partial editable projection.</summary>
public sealed class BoardsLegacyDocumentAdapter : IBoardsLegacyDocumentCodec
{
    public const int DefaultMaximumDocumentBytes = 256 * 1024 * 1024;
    public const int DefaultMaximumInlineArchiveBytes = 8 * 1024 * 1024;
    public const string ArchiveKey = "boards.legacy.original.v1";
    public const string ProvenanceKey = "boards.legacy.provenance.v1";
    public const string IdentityMapKey = "boards.legacy.identities.v1";
    public const string ProjectionIssuesKey = "boards.legacy.projection-issues.v1";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly int _maximumDocumentBytes;
    private readonly int _maximumInlineArchiveBytes;

    public BoardsLegacyDocumentAdapter(int maximumDocumentBytes = DefaultMaximumDocumentBytes,
        int maximumInlineArchiveBytes = DefaultMaximumInlineArchiveBytes)
    {
        if (maximumDocumentBytes <= 0) throw new ArgumentOutOfRangeException(nameof(maximumDocumentBytes));
        if (maximumInlineArchiveBytes <= 0) throw new ArgumentOutOfRangeException(nameof(maximumInlineArchiveBytes));
        _maximumDocumentBytes = maximumDocumentBytes;
        _maximumInlineArchiveBytes = Math.Min(maximumDocumentBytes, maximumInlineArchiveBytes);
    }

    private sealed class Inspection(BoardsLegacyDocumentAdapter issuer, byte[]? bytes,
        long length, string hash, BoardsLegacySourceIdentity? source) : IBoardsLegacyDocumentInspection
    {
        internal BoardsLegacyDocumentAdapter Issuer { get; } = issuer;
        internal byte[]? Bytes { get; } = bytes;
        internal JsonElement Root { get; set; }
        internal List<BoardsLegacyDocumentIssue> MutableIssues { get; } = [];
        public BoardsLegacyFormat Format { get; internal set; }
        public int? SchemaVersion { get; internal set; }
        public Guid? LegacyDocumentId { get; internal set; }
        public BoardsLegacySourceIdentity? Source { get; } = source;
        public long OriginalByteLength { get; } = length;
        public string OriginalSha256 { get; } = hash;
        public bool OriginalBytesRetained => Bytes is not null;
        public IReadOnlyList<BoardsLegacyDocumentIssue> Issues => MutableIssues.AsReadOnly();
    }

    public IBoardsLegacyDocumentInspection Inspect(ReadOnlyMemory<byte> alreadyAuthorisedBytes,
        BoardsLegacySourceIdentity? source = null)
    {
        var captured = alreadyAuthorisedBytes.Length <= _maximumDocumentBytes ? alreadyAuthorisedBytes.ToArray() : null;
        var hash = Convert.ToHexString(SHA256.HashData(captured is null ? alreadyAuthorisedBytes.Span : captured.AsSpan())).ToLowerInvariant();
        var inspection = new Inspection(this, captured,
            alreadyAuthorisedBytes.Length, hash, source is null ? null : source with { });
        if (!inspection.OriginalBytesRetained)
        {
            Add(inspection.MutableIssues, "SizeLimitExceeded", "", "The codec did not copy this oversized input. The original source must remain with its owner; nothing was truncated or imported.", true);
            return inspection;
        }
        try
        {
            var bytes = inspection.Bytes!;
            var offset = bytes.AsSpan().StartsWith(new byte[] { 0xef, 0xbb, 0xbf }) ? 3 : 0;
            // Reject invalid UTF-8 instead of accepting replacement characters. BOM and all
            // lexical detail remain in Bytes, independently from the JSON parsing view.
            _ = new UTF8Encoding(false, true).GetCharCount(bytes, offset, bytes.Length - offset);
            using var document = JsonDocument.Parse(bytes.AsMemory(offset));
            inspection.Root = document.RootElement.Clone();
            if (inspection.Root.ValueKind != JsonValueKind.Object)
            {
                Add(inspection.MutableIssues, "InvalidDocument", "", "A legacy Board must be a JSON object.", true);
                return inspection;
            }
            RejectDuplicateProperties(inspection.Root, "", inspection.MutableIssues);
            var root = inspection.Root;
            if (!Try(root, "format", out _) && Try(root, "id", out _) && Try(root, "groups", out _))
            {
                inspection.Format = BoardsLegacyFormat.RawSnapshot;
                ValidateSnapshot(root, "/snapshot", inspection.MutableIssues);
                ScanUnknown(root, typeof(HavenBoardSnapshot), "", inspection.MutableIssues);
            }
            else
            {
                inspection.Format = BoardsLegacyFormat.Envelope;
                if (!Try(root, "format", out var format) || format.ValueKind != JsonValueKind.String ||
                    format.GetString() != HavenBoardDocument.FormatIdentity ||
                    !Try(root, "schemaVersion", out var version) || !version.TryGetInt32(out var number))
                {
                    Add(inspection.MutableIssues, "InvalidFormat", "", "The required format identity and integer schema version are absent or invalid.", true);
                    return inspection;
                }
                inspection.SchemaVersion = number;
                if (number is < 0 or > HavenBoardDocument.CurrentSchemaVersion)
                {
                    Add(inspection.MutableIssues, "UnsupportedSchema", "/schemaVersion", "This schema is preserved without guessing, migrating or falling back to another file.", true);
                    return inspection;
                }
                if (number > 0)
                {
                    if (Try(root, "documentId", out var id) && id.ValueKind == JsonValueKind.String &&
                        id.TryGetGuid(out var value) && value != Guid.Empty) inspection.LegacyDocumentId = value;
                    else Add(inspection.MutableIssues, "MissingIdentity", "/documentId", "This envelope requires its original nonempty document UUID.", true);
                    RequireTimestamp(root, "createdUtc", inspection.MutableIssues);
                    RequireTimestamp(root, "modifiedUtc", inspection.MutableIssues);
                }
                var snapshot = Try(root, "snapshot", out var task) && task.ValueKind != JsonValueKind.Null;
                var rich = Try(root, "richNotes", out var notes) && notes.ValueKind != JsonValueKind.Null;
                if (snapshot) ValidateSnapshot(task, "/snapshot", inspection.MutableIssues);
                if (rich && number >= 2) ValidateRich(notes, number, inspection.MutableIssues);
                if ((!snapshot && !rich) || (number < 2 && !snapshot))
                    Add(inspection.MutableIssues, "MissingStructure", "", "The original schema has no required content facet.", true);
                if (rich && number < 2)
                    Add(inspection.MutableIssues, "UnsupportedFacetPreserved", "/richNotes", "Rich content in this older schema remains original data; it is not guessed into a newer schema.");
                ScanUnknown(root, typeof(HavenBoardDocument), "", inspection.MutableIssues);
            }
            Add(inspection.MutableIssues, "OriginalSourcePreserved", "", "Exact source bytes are retained. Original export excludes subsequent edits to an imported canonical snapshot.");
        }
        catch (Exception error) when (error is JsonException or DecoderFallbackException or InvalidOperationException or NotSupportedException or ArgumentException)
        {
            Add(inspection.MutableIssues, "InvalidDocument", "", "The source cannot be safely projected: " + error.Message, true);
        }
        return inspection;
    }

    public byte[] ExportOriginal(IBoardsLegacyDocumentInspection inspection)
    {
        var original = DemandOriginal(inspection);
        if (original.Bytes is null) throw new InvalidOperationException("The oversized original remains with its source owner; no retained export is available.");
        return (byte[])original.Bytes.Clone();
    }

    public BoardsLegacyImportCandidate PrepareCanonicalImport(IBoardsLegacyDocumentInspection inspection,
        DateTimeOffset importObservedAt)
    {
        var original = DemandOriginal(inspection);
        var issues = original.Issues.ToList();
        Guid? documentId = original.LegacyDocumentId;
        var source = original.Source;
        if (source is null || source.StoreId == Guid.Empty || source.FileId == Guid.Empty || string.IsNullOrWhiteSpace(source.Revision))
            Add(issues, "SourceIdentityUnavailable", "", "A stable, actual Files store/file/revision is required; a path or content hash cannot replace it.", true);
        else documentId ??= StableGuid(source.StoreId, "legacy-file/" + source.FileId.ToString("D"));
        if (importObservedAt == default)
            Add(issues, "ImportObservationMissing", "", "The actual import observation time must be supplied separately from legacy provenance.", true);
        Add(issues, "CanonicalIdentityReviewRequired", "", "A proposed document UUID is not proof of an absent or matching canonical document. The same Notes/Home owner must review provenance, collisions and the exact candidate before any commit.");
        if (original.OriginalByteLength > _maximumInlineArchiveBytes)
            Add(issues, "CanonicalArchiveStorageRequired", "", "This original is retained for inspection/export, but needs a genuine canonical Files archive/asset reference before import. It is not truncated or dropped to fit inline Notes metadata.", true);
        if (issues.Any(issue => issue.PreventsProjection)) return new(documentId, null, issues);
        if (original.SchemaVersion is null or < 2 || !Try(original.Root, "richNotes", out var rawRich) || rawRich.ValueKind == JsonValueKind.Null)
        {
            Add(issues, "SnapshotRequiresCanonicalMapping", "/snapshot", "The task facet is preserved. Its groups, hierarchy, freeform placement and attachments require canonical Data/Boards semantics; a generated checklist is not substituted.", true);
            return new(documentId, null, issues);
        }
        var rich = rawRich.Deserialize<HavenRichNotes>(Json)!;
        SupplyEarlierStyleDefaults(rich, original.SchemaVersion.Value);
        Add(issues, "PartialEditableProjection", "/richNotes", "This is a canonical snapshot proposal, not a lossless editable-format conversion. Original rich payload and direct/inherited formatting remain separately retained.");
        var map = new Dictionary<string, Guid>(StringComparer.Ordinal);
        var unmaterialized = new List<string>();
        Guid Id(string key)
        {
            if (!map.TryGetValue(key, out var id)) map.Add(key, id = StableGuid(documentId!.Value, key));
            return id;
        }
        var created = original.Root.GetPropertyCaseInsensitive("createdUtc").GetDateTimeOffset();
        var modified = original.Root.GetPropertyCaseInsensitive("modifiedUtc").GetDateTimeOffset();
        var result = new NotesDocument
        {
            Id = documentId!.Value, Title = rich.Title, CreatedAt = created, UpdatedAt = modified,
            Version = 0, Sections = [], Styles = [], Revisions = [], Metadata = new(StringComparer.OrdinalIgnoreCase),
            Collaboration = new NotesCollaborationState { OwnerId = string.Empty }
        };
        result.Metadata[BoardsWorkspaceService.ProductKey] = "boards";
        foreach (var style in rich.Styles)
        {
            var styleKey = "style/" + Escape(style.Id);
            result.Styles.Add(new NotesNamedStyle
            {
                Id = style.Id, Name = style.Name,
                Character = Run(new HavenRichTextRun
                {
                    Bold = style.Bold, Italic = style.Italic, Underline = style.Underline, StrikeThrough = style.StrikeThrough,
                    FontFamily = style.FontFamily, FontSize = style.FontSize, Foreground = style.Foreground, Background = style.Background
                }, style, Id(styleKey + "/character")),
                Paragraph = Paragraph(style.Alignment, style.LineSpacing, style.SpaceBefore, style.SpaceAfter, style.IndentLevel)
            });
            if (style.Baseline != HavenRichBaseline.Normal)
                Add(issues, "BaselinePreservedOnly", "/richNotes/styles/" + Escape(style.Id), "Subscript/superscript remain in the original; the canonical run schema cannot represent them.");
        }
        foreach (var section in rich.Sections)
        {
            var sectionKey = "rich/section/" + Escape(section.Id);
            var targetSection = new NotesSection { Id = Id(sectionKey), Title = section.Title, Pages = [] };
            foreach (var page in section.Pages)
            {
                var pageKey = sectionKey + "/page/" + Escape(page.Id);
                var targetPage = new NotesPage
                {
                    Id = Id(pageKey), Title = page.Title, Order = page.Order,
                    CanvasWidth = page.CanvasWidth, CanvasHeight = page.CanvasHeight, Blocks = [], CanvasObjects = []
                };
                foreach (var block in page.Blocks)
                {
                    var key = pageKey + "/block/" + Escape(block.Id);
                    var blockId = Id(key); // Retain provenance even when this kind cannot be projected.
                    if (!Enum.IsDefined(block.Kind) || block.Kind == HavenRichBlockKind.Graph)
                    {
                        unmaterialized.Add(key);
                        Add(issues, "UnsupportedBlockPreserved", key, "This block remains fully in the original. It is not flattened into text, an image or another block kind.");
                        continue;
                    }
                    var style = rich.Styles.Single(item => item.Id == block.StyleId);
                    var projected = new NotesBlock
                    {
                        Id = blockId, Order = block.Order, StyleId = block.StyleId, PlainText = block.PlainText,
                        Kind = block.Kind switch
                        {
                            HavenRichBlockKind.Heading => NotesBlockKind.Heading,
                            HavenRichBlockKind.BulletList or HavenRichBlockKind.NumberedList or HavenRichBlockKind.Checklist => NotesBlockKind.List,
                            HavenRichBlockKind.Table => NotesBlockKind.Table,
                            HavenRichBlockKind.Image => NotesBlockKind.Image,
                            HavenRichBlockKind.Divider => NotesBlockKind.Divider,
                            _ => NotesBlockKind.Paragraph
                        },
                        Runs = block.Runs.Select((run, index) => Run(run, style, Id(key + "/run/" + index))).ToList(),
                        Paragraph = Paragraph(block.Alignment == HavenRichAlignment.Inherit ? style.Alignment : block.Alignment,
                            block.LineSpacing == 0 ? style.LineSpacing : block.LineSpacing,
                            block.SpaceBefore, block.SpaceAfter, block.IndentLevel < 0 ? style.IndentLevel : block.IndentLevel),
                        Metadata = new(StringComparer.OrdinalIgnoreCase) { ["boards.legacy.source-key"] = key }
                    };
                    if (block.Runs.Any(run => run.Baseline != HavenRichBaseline.Normal))
                        Add(issues, "BaselinePreservedOnly", key, "Subscript/superscript source formatting cannot be represented by canonical text runs.");
                    if (projected.Kind == NotesBlockKind.List)
                    {
                        projected.List = new NotesListData
                        {
                            Kind = block.Kind switch { HavenRichBlockKind.NumberedList => NotesListKind.Numbered, HavenRichBlockKind.Checklist => NotesListKind.Checklist, _ => NotesListKind.Bulleted },
                            Items = block.Items.Select(item => new NotesListItem { Id = Id(key + "/item/" + Escape(item.Id)), Text = item.Text, Checked = item.Checked, Level = item.Level }).ToList()
                        };
                        Add(issues, "ListFormattingPreservedOnly", key, "Per-item rich formatting remains in the original; canonical list items retain values and hierarchy.");
                    }
                    if (projected.Kind == NotesBlockKind.Table)
                    {
                        projected.Table = new NotesTableData
                        {
                            Rows = block.Table!.Rows.Select((row, index) => new NotesTableRow
                            {
                                Id = Id(key + "/row/" + index),
                                Cells = row.Cells.Select(cell => new NotesTableCell
                                {
                                    Id = Id(key + "/cell/" + Escape(cell.Id)), Text = cell.Text,
                                    Background = string.IsNullOrEmpty(cell.Background) ? "#00000000" : cell.Background,
                                    VerticalAlignment = cell.AlignmentV switch { HavenRichCellVerticalAlignment.Middle => "Center", HavenRichCellVerticalAlignment.Bottom => "Bottom", _ => "Top" }
                                }).ToList()
                            }).ToList()
                        };
                        Add(issues, "TableFormattingPreservedOnly", key, "Rich cell runs, borders and track formatting remain in the original; editable table values are a partial projection.");
                    }
                    if (projected.Kind == NotesBlockKind.Image)
                    {
                        unmaterialized.Add(key);
                        // No synthetic StoredPath, attachment owner or Available state.
                        Add(issues, "AssetBindingRequired", key, "The original image bytes/reference are retained. A genuine canonical Files/Notes asset binding is required before projecting media.");
                        continue;
                    }
                    if (projected.Kind == NotesBlockKind.Divider)
                        Add(issues, "DividerFormattingPreservedOnly", key, "Divider thickness, line style and colour remain in the original; canonical Divider has no equivalent dedicated payload.");
                    if (block.Attachment is not null)
                        Add(issues, "AssetBindingRequired", key + "/attachment", "Attachment bytes, size and opaque local reference remain original data; no path or URI is followed.");
                    targetPage.Blocks.Add(projected);
                }
                if (page.Ink.Count != 0 || page.Canvas.Count != 0)
                    Add(issues, "CanvasAndInkPreservedOnly", pageKey, "Ink pressure/tool/points, canvas geometry and viewport remain in the original. This codec does not claim shared Rnote/CUI editing parity.");
                targetSection.Pages.Add(targetPage);
            }
            result.Sections.Add(targetSection);
        }
        if (Try(original.Root, "snapshot", out var snapshot) && snapshot.ValueKind != JsonValueKind.Null)
            Add(issues, "TaskFacetPreservedOnly", "/snapshot", "The complementary task facet is retained independently, not merged by title or recreated as a private database.");
        var validation = new NotesDocumentValidator().Validate(result);
        if (!validation.IsValid)
        {
            foreach (var issue in validation.Issues.Where(issue => issue.IsError))
                Add(issues, "CanonicalProjectionUnavailable", issue.Path, issue.Message + " The original value was not clamped, truncated, reordered or repaired.", true);
            return new(documentId, null, issues);
        }
        result.Metadata[ArchiveKey] = Convert.ToBase64String(original.Bytes!);
        result.Metadata[ProvenanceKey] = JsonSerializer.Serialize(new
        {
            version = 1, original.Format, original.SchemaVersion, original.LegacyDocumentId, source,
            original.OriginalSha256, original.OriginalByteLength, importObservedAt,
            mode = "IndependentSnapshot", originalExportIncludesCanonicalEdits = false,
            unidentifiedMembersRequireReview = true
        }, Json);
        result.Metadata[IdentityMapKey] = JsonSerializer.Serialize(new { version = 1, sourceToCandidateIds = map, unmaterializedKeys = unmaterialized }, Json);
        result.Metadata[ProjectionIssuesKey] = JsonSerializer.Serialize(issues, Json);
        return new(documentId, result, issues);
    }

    private Inspection DemandOriginal(IBoardsLegacyDocumentInspection inspection) =>
        inspection is Inspection original && ReferenceEquals(original.Issuer, this)
            ? original : throw new ArgumentException("Retain the inspection issued by this codec. Descriptive copies are not original observations.", nameof(inspection));

    private static void ValidateSnapshot(JsonElement value, string pointer, List<BoardsLegacyDocumentIssue> issues)
    {
        var count = issues.Count;
        ValidateDeclaredShape(value, typeof(HavenBoardSnapshot), pointer, issues);
        RequireId(value, pointer, issues);
        if (Array(value, "groups", pointer, issues) is { } groups)
            IdArray(groups, pointer + "/groups", issues, group =>
            {
                if (Array(group, "cards", pointer + "/groups", issues) is { } cards)
                    IdArray(cards, pointer + "/cards", issues, card =>
                    {
                        if (Try(card, "attachments", out var attachments) && attachments.ValueKind != JsonValueKind.Null)
                            IdArray(attachments, pointer + "/attachments", issues);
                    });
            });
        if (issues.Skip(count).Any(issue => issue.PreventsProjection)) return;
        try { HavenBoardReducer.Validate(value.Deserialize<HavenBoardSnapshot>(Json)!); }
        catch (Exception error) when (error is InvalidOperationException or ArgumentException or JsonException)
        { Add(issues, "InvalidSnapshot", pointer, error.Message, true); }
    }

    private static void ValidateRich(JsonElement value, int schema, List<BoardsLegacyDocumentIssue> issues)
    {
        var count = issues.Count;
        ValidateDeclaredShape(value, typeof(HavenRichNotes), "/richNotes", issues);
        if (Array(value, "sections", "/richNotes", issues) is { } sections)
            IdArray(sections, "/richNotes/sections", issues, section =>
            {
                if (Array(section, "pages", "/richNotes/sections", issues) is { } pages)
                    IdArray(pages, "/richNotes/pages", issues, page =>
                    {
                        if (Array(page, "blocks", "/richNotes/pages", issues) is { } blocks)
                            IdArray(blocks, "/richNotes/blocks", issues, block =>
                            {
                                OptionalIds(block, "items", "/richNotes/blocks", issues);
                                OptionalObjectId(block, "image", issues);
                                OptionalObjectId(block, "attachment", issues);
                                InspectAsset(block, "image", issues);
                                InspectAsset(block, "attachment", issues);
                                if (Try(block, "table", out var table) && table.ValueKind != JsonValueKind.Null &&
                                    Array(table, "rows", "/richNotes/table", issues) is { } rows)
                                    foreach (var row in rows.EnumerateArray())
                                        if (Array(row, "cells", "/richNotes/table/rows", issues) is { } cells)
                                            IdArray(cells, "/richNotes/table/cells", issues);
                                if (Try(block, "graph", out var graph) && graph.ValueKind != JsonValueKind.Null)
                                {
                                    if (Array(graph, "expressions", "/richNotes/graph", issues) is { } expressions)
                                        IdArray(expressions, "/richNotes/graph/expressions", issues);
                                }
                            });
                        OptionalIds(page, "canvas", "/richNotes/pages", issues);
                    });
            });
        OptionalIds(value, "styles", "/richNotes", issues);
        if (issues.Skip(count).Any(issue => issue.PreventsProjection)) return;
        try
        {
            var rich = value.Deserialize<HavenRichNotes>(Json)!;
            SupplyEarlierStyleDefaults(rich, schema);
            HavenRichNotesValidator.Validate(rich);
            foreach (var block in rich.Sections.SelectMany(section => section.Pages).SelectMany(page => page.Blocks))
                if (!rich.Styles.Any(style => style.Id == block.StyleId))
                    Add(issues, "UnresolvedStyle", "/richNotes/blocks/" + Escape(block.Id) + "/styleId", "The original style reference has no definition. It was not rewritten to Paragraph.", true);
        }
        catch (Exception error) when (error is InvalidOperationException or ArgumentException or JsonException)
        { Add(issues, "InvalidRichContent", "/richNotes", error.Message, true); }
    }

    private static void SupplyEarlierStyleDefaults(HavenRichNotes rich, int schema)
    {
        if (schema >= 3) return;
        foreach (var builtIn in HavenRichStyles.BuiltIns())
            if (!rich.Styles.Any(style => style.Id == builtIn.Id)) rich.Styles.Add(builtIn);
        // The old migration also rewrites unknown style IDs. That destructive step is
        // intentionally absent; unresolved references refuse projection instead.
    }

    private static NotesTextRun Run(HavenRichTextRun run, HavenRichStyle style, Guid id) => new()
    {
        Id = id, Text = run.Text, Bold = run.Bold, Italic = run.Italic, Underline = run.Underline, StrikeThrough = run.StrikeThrough,
        FontFamily = string.IsNullOrEmpty(run.FontFamily) ? string.IsNullOrEmpty(style.FontFamily) ? "Montserrat" : style.FontFamily : run.FontFamily,
        FontSize = run.FontSize == 0 ? style.FontSize == 0 ? 14 : style.FontSize : run.FontSize,
        Foreground = string.IsNullOrEmpty(run.Foreground) ? string.IsNullOrEmpty(style.Foreground) ? "#FFEEEEEE" : style.Foreground : run.Foreground,
        Background = string.IsNullOrEmpty(run.Background) ? string.IsNullOrEmpty(style.Background) ? "#00000000" : style.Background : run.Background
    };

    private static NotesParagraphFormat Paragraph(HavenRichAlignment alignment, double spacing, double before, double after, int indent) => new()
    {
        Alignment = alignment switch { HavenRichAlignment.Center => NotesTextAlignment.Center, HavenRichAlignment.Right => NotesTextAlignment.Right, HavenRichAlignment.Justify => NotesTextAlignment.Justify, _ => NotesTextAlignment.Left },
        LineSpacing = spacing == 0 ? 1.25 : spacing, SpaceBefore = before, SpaceAfter = after, IndentLeft = indent < 0 ? 0 : indent
    };

    private static void OptionalObjectId(JsonElement parent, string property, List<BoardsLegacyDocumentIssue> issues)
    {
        if (Try(parent, property, out var value) && value.ValueKind != JsonValueKind.Null)
            RequireId(value, "/richNotes/" + property, issues);
    }

    private static void InspectAsset(JsonElement block, string property, List<BoardsLegacyDocumentIssue> issues)
    {
        if (!Try(block, property, out var asset) || asset.ValueKind != JsonValueKind.Object) return;
        var pointer = "/richNotes/" + property;
        if (Try(asset, "dataBase64", out var data) && data.ValueKind == JsonValueKind.String && !string.IsNullOrEmpty(data.GetString()))
        {
            // Validate embedded data without following references or creating an asset store.
            try
            {
                var size = EmbeddedByteLength(data.GetString()!);
                if (!Try(asset, "sizeBytes", out var declared) || !declared.TryGetInt64(out var expected) || size != expected)
                    Add(issues, "InvalidAssetMetadata", pointer, "Embedded bytes and declared size differ; the original is retained and no Available asset is fabricated.");
            }
            catch (FormatException)
            { Add(issues, "InvalidEmbeddedAsset", pointer, "The embedded base64 is invalid. Original bytes remain unchanged; no attachment is materialised."); }
        }
        if (Try(asset, "localReference", out var reference) && reference.ValueKind == JsonValueKind.String && !string.IsNullOrEmpty(reference.GetString()))
            Add(issues, "ExternalAssetReferencePreserved", pointer + "/localReference", "This opaque reference is preserved, not opened, fetched or granted authority.");
    }

    private static long EmbeddedByteLength(string value)
    {
        long symbols = 0;
        var padding = 0;
        foreach (var character in value)
        {
            if (character is ' ' or '\t' or '\r' or '\n') continue;
            symbols++;
            if (character == '=') { if (++padding > 2) throw new FormatException(); }
            else if (padding != 0 || !(char.IsAsciiLetterOrDigit(character) || character is '+' or '/')) throw new FormatException();
        }
        if (symbols % 4 != 0 || symbols == 0 && padding != 0) throw new FormatException();
        return symbols / 4 * 3 - padding;
    }

    private static void OptionalIds(JsonElement parent, string property, string pointer, List<BoardsLegacyDocumentIssue> issues)
    {
        if (Try(parent, property, out var value)) IdArray(value, pointer + "/" + property, issues);
    }

    private static JsonElement? Array(JsonElement parent, string property, string pointer, List<BoardsLegacyDocumentIssue> issues)
    {
        if (Try(parent, property, out var array) && array.ValueKind == JsonValueKind.Array) return array;
        Add(issues, "MissingStructure", pointer + "/" + property, "The original array must be present; a default collection is not invented.", true);
        return null;
    }

    private static void IdArray(JsonElement array, string pointer, List<BoardsLegacyDocumentIssue> issues, Action<JsonElement>? visit = null)
    {
        if (array.ValueKind != JsonValueKind.Array)
        { Add(issues, "MissingStructure", pointer, "An array was required; null or another value is not replaced by a default.", true); return; }
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var index = 0;
        foreach (var value in array.EnumerateArray())
        {
            var location = pointer + "/" + index++;
            var id = RequireId(value, location, issues);
            if (id is not null && !ids.Add(id)) Add(issues, "AmbiguousIdentity", location, "Duplicate source identity is not deduplicated or renamed.", true);
            if (value.ValueKind == JsonValueKind.Object) visit?.Invoke(value);
        }
    }

    private static string? RequireId(JsonElement value, string pointer, List<BoardsLegacyDocumentIssue> issues)
    {
        if (Try(value, "id", out var id) && id.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(id.GetString())) return id.GetString();
        Add(issues, "MissingIdentity", pointer + "/id", "The original nonempty string ID must be present before typed defaults run.", true);
        return null;
    }

    private static void RequireTimestamp(JsonElement root, string property, List<BoardsLegacyDocumentIssue> issues)
    {
        if (!Try(root, property, out var value) || value.ValueKind != JsonValueKind.String || !value.TryGetDateTimeOffset(out var timestamp) || timestamp == default)
            Add(issues, "InvalidTimestamp", "/" + property, "The original timestamp is absent or invalid; a clock value is not substituted.", true);
    }

    private static void RejectDuplicateProperties(JsonElement value, string pointer, List<BoardsLegacyDocumentIssue> issues)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var property in value.EnumerateObject())
            {
                var key = pointer + "/" + Escape(property.Name);
                if (!names.Add(property.Name)) Add(issues, "AmbiguousProperty", key, "Duplicate JSON properties cannot be projected with last-member-wins semantics.", true);
                RejectDuplicateProperties(property.Value, key, issues);
            }
        }
        else if (value.ValueKind == JsonValueKind.Array)
        {
            var index = 0;
            foreach (var item in value.EnumerateArray()) RejectDuplicateProperties(item, pointer + "/" + index++, issues);
        }
    }

    private static void ScanUnknown(JsonElement value, Type type, string pointer, List<BoardsLegacyDocumentIssue> issues)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;
        if (value.ValueKind == JsonValueKind.Array && type.IsGenericType)
        {
            var index = 0;
            foreach (var item in value.EnumerateArray()) ScanUnknown(item, type.GetGenericArguments()[0], pointer + "/" + index++, issues);
        }
        else if (value.ValueKind == JsonValueKind.Object && type != typeof(JsonElement))
        {
            var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .ToDictionary(property => JsonNamingPolicy.CamelCase.ConvertName(property.Name), StringComparer.OrdinalIgnoreCase);
            foreach (var property in value.EnumerateObject())
            {
                var key = pointer + "/" + Escape(property.Name);
                if (properties.TryGetValue(property.Name, out var definition)) ScanUnknown(property.Value, definition.PropertyType, key, issues);
                else Add(issues, "UnknownFieldPreserved", key, "This unknown value is retained in the exact original bytes; it is not discarded by typed serialization.");
            }
        }
    }

    private static void ValidateDeclaredShape(JsonElement value, Type type, string pointer,
        List<BoardsLegacyDocumentIssue> issues, bool allowsNull = false)
    {
        if (value.ValueKind == JsonValueKind.Null)
        {
            if (!allowsNull) Add(issues, "InvalidStructure", pointer, "Explicit null is not replaced by a typed initializer or default collection.", true);
            return;
        }
        type = Nullable.GetUnderlyingType(type) ?? type;
        if (value.ValueKind == JsonValueKind.Array && type.IsGenericType)
        {
            var index = 0;
            foreach (var item in value.EnumerateArray())
                ValidateDeclaredShape(item, type.GetGenericArguments()[0], pointer + "/" + index++, issues);
        }
        else if (value.ValueKind == JsonValueKind.Object)
        {
            var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .ToDictionary(property => JsonNamingPolicy.CamelCase.ConvertName(property.Name), StringComparer.OrdinalIgnoreCase);
            var nullability = new NullabilityInfoContext();
            foreach (var property in value.EnumerateObject())
                if (properties.TryGetValue(property.Name, out var definition))
                    ValidateDeclaredShape(property.Value, definition.PropertyType, pointer + "/" + Escape(property.Name), issues,
                        nullability.Create(definition).ReadState == NullabilityState.Nullable);
        }
    }

    private static Guid StableGuid(Guid space, string key)
    {
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(space.ToString("D") + "\n" + key));
        return new Guid(digest.AsSpan(0, 16)); // Identity mapping only, never authentication.
    }
    private static string Escape(string value) => value.Replace("~", "~0", StringComparison.Ordinal).Replace("/", "~1", StringComparison.Ordinal);
    private static bool Try(JsonElement value, string property, out JsonElement found)
    {
        if (value.ValueKind == JsonValueKind.Object)
            foreach (var item in value.EnumerateObject())
                if (string.Equals(item.Name, property, StringComparison.OrdinalIgnoreCase)) { found = item.Value; return true; }
        found = default; return false;
    }
    private static void Add(List<BoardsLegacyDocumentIssue> issues, string code, string pointer, string message, bool prevents = false)
        => issues.Add(new(code, pointer, message, prevents));
}

internal static class BoardsLegacyJsonProperty
{
    internal static JsonElement GetPropertyCaseInsensitive(this JsonElement value, string name) =>
        value.EnumerateObject().First(property => string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase)).Value;
}
