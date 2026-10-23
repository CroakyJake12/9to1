using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Haven.Application;
using Haven.Core;
using Haven.Infrastructure;
using Xunit;

namespace Haven.Infrastructure.Tests;

/// <summary>Codec/component controls only. These supplied-byte fixtures do not impersonate
/// Files admission, a Notes commit, an actual CUI review or application/native acceptance.</summary>
public sealed class BoardsLegacyDocumentAdapterTests
{
    private static readonly Guid DocumentId = Guid.Parse("1dfe13a7-4d37-4b63-b1c7-61c34e1052bb");
    private static readonly BoardsLegacySourceIdentity Source = new(
        Guid.Parse("452ac458-58d2-4e26-8779-5874f4bf6edc"),
        Guid.Parse("9e431248-32bd-4d78-b7d9-efad07653634"), "original-revision-1");
    private static readonly DateTimeOffset ObservedAt = DateTimeOffset.Parse("2026-10-23T04:00:00Z");

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void Physical_schema_inputs_and_backup_remain_byte_exact_without_migration_writes(int schema)
    {
        var directory = Path.Combine(Path.GetTempPath(), "boards-legacy-codec-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var primary = Path.Combine(directory, "Revision notes.9to1board");
            var backup = primary + ".bak";
            var data = Envelope(schema);
            data["futureEnvelope"] = JsonNode.Parse("{\"order\":[3,1],\"large\":12345678901234567890,\"unknown\":null}");
            var original = new byte[] { 0xef, 0xbb, 0xbf }.Concat(Encoding.UTF8.GetBytes(data.ToJsonString(new() { WriteIndented = true }) + "\n  ")).ToArray();
            var prior = Encoding.UTF8.GetBytes("{unread backup is intentionally invalid}");
            File.WriteAllBytes(primary, original);
            File.WriteAllBytes(backup, prior);
            var codec = new BoardsLegacyDocumentAdapter();
            var inspection = codec.Inspect(File.ReadAllBytes(primary), Source);

            Assert.Equal(schema, inspection.SchemaVersion);
            Assert.Equal(original, codec.ExportOriginal(inspection));
            Assert.Equal(original, File.ReadAllBytes(primary));
            Assert.Equal(prior, File.ReadAllBytes(backup));
            Assert.Equal(2, Directory.GetFiles(directory).Length);
            Assert.Contains(inspection.Issues, issue => issue.Code == "UnknownFieldPreserved" && issue.Pointer == "/futureEnvelope");
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public void All_nine_kinds_keep_rich_originals_and_expose_specific_partial_projection_gaps()
    {
        var codec = new BoardsLegacyDocumentAdapter();
        var bytes = Bytes(Envelope());
        var inspection = codec.Inspect(bytes, Source);
        var candidate = codec.PrepareCanonicalImport(inspection, ObservedAt);
        var note = Assert.IsType<NotesDocument>(candidate.CopyDocument());
        var blocks = Assert.Single(Assert.Single(note.Sections).Pages).Blocks;

        Assert.Equal(DocumentId, note.Id);
        Assert.Equal("Revision notebook", note.Title);
        Assert.Equal("boards", note.Metadata[BoardsWorkspaceService.ProductKey]);
        Assert.Equal(7, blocks.Count); // Graph and unbound image are retained, not fake editable blocks.
        Assert.Contains(blocks, block => block.Kind == NotesBlockKind.Heading && block.PlainText == "Topic heading");
        Assert.Equal(new[] { NotesListKind.Bulleted, NotesListKind.Numbered, NotesListKind.Checklist },
            blocks.Where(block => block.List is not null).Select(block => block.List!.Kind));
        Assert.Equal("Remember this", Assert.Single(blocks.First(block => block.List?.Kind == NotesListKind.Checklist).List!.Items).Text);
        Assert.True(Assert.Single(blocks.First(block => block.List?.Kind == NotesListKind.Checklist).List!.Items).Checked);
        Assert.Equal("Cell text", blocks.Single(block => block.Table is not null).Table!.Rows[0].Cells[0].Text);
        Assert.Contains(blocks, block => block.Kind == NotesBlockKind.Divider);
        Assert.Contains(candidate.Issues, issue => issue.Code == "UnsupportedBlockPreserved" && issue.Pointer.EndsWith("block/graph", StringComparison.Ordinal));
        Assert.Contains(candidate.Issues, issue => issue.Code == "DividerFormattingPreservedOnly");
        Assert.Contains(candidate.Issues, issue => issue.Code == "TableFormattingPreservedOnly");
        Assert.Contains(candidate.Issues, issue => issue.Code == "BaselinePreservedOnly");
        Assert.Contains(candidate.Issues, issue => issue.Code == "CanvasAndInkPreservedOnly");
        Assert.Contains(candidate.Issues, issue => issue.Code == "AssetBindingRequired");
        Assert.True(new NotesDocumentValidator().Validate(note).IsValid);
        Assert.Equal(bytes, Convert.FromBase64String(note.Metadata[BoardsLegacyDocumentAdapter.ArchiveKey]));
        using var retained = JsonDocument.Parse(Convert.FromBase64String(note.Metadata[BoardsLegacyDocumentAdapter.ArchiveKey]));
        var originalBlocks = retained.RootElement.GetProperty("richNotes").GetProperty("sections")[0].GetProperty("pages")[0].GetProperty("blocks");
        Assert.Equal(Enumerable.Range(0, 9), originalBlocks.EnumerateArray().Select(block => block.GetProperty("kind").GetInt32()));
        Assert.Equal(3.75, originalBlocks[7].GetProperty("divider").GetProperty("thickness").GetDouble());
        Assert.Equal(2, originalBlocks[8].GetProperty("graph").GetProperty("expressions").GetArrayLength());
        Assert.Equal(bytes, codec.ExportOriginal(inspection));
    }

    [Fact]
    public void Caller_buffers_export_buffers_and_candidate_copies_cannot_replace_the_retained_review_data()
    {
        var original = Bytes(Envelope());
        var supplied = (byte[])original.Clone();
        var codec = new BoardsLegacyDocumentAdapter();
        var inspection = codec.Inspect(supplied, Source);
        System.Array.Fill(supplied, (byte)'X');
        var exported = codec.ExportOriginal(inspection);
        System.Array.Fill(exported, (byte)'Y');
        var candidate = codec.PrepareCanonicalImport(inspection, ObservedAt);
        var changed = candidate.CopyDocument()!;
        changed.Title = "Substituted after review";
        changed.Sections.Clear();
        changed.Metadata.Clear();

        Assert.Equal(original, codec.ExportOriginal(inspection));
        Assert.Equal("Revision notebook", candidate.CopyDocument()!.Title);
        Assert.Single(candidate.CopyDocument()!.Sections);
        Assert.Equal(candidate.CandidateSha256, codec.PrepareCanonicalImport(inspection, ObservedAt).CandidateSha256);
    }

    [Fact]
    public void Names_and_source_revision_changes_preserve_document_and_entity_identity()
    {
        var first = Envelope();
        var second = (JsonObject)first.DeepClone();
        second["modifiedUtc"] = "2026-10-22T12:00:00Z";
        second["richNotes"]!["title"] = "Renamed notebook";
        second["richNotes"]!["sections"]![0]!["title"] = "Renamed section";
        second["richNotes"]!["sections"]![0]!["pages"]![0]!["title"] = "Renamed page";
        var codec = new BoardsLegacyDocumentAdapter();
        var a = codec.PrepareCanonicalImport(codec.Inspect(Bytes(first), Source), ObservedAt).CopyDocument()!;
        var b = codec.PrepareCanonicalImport(codec.Inspect(Bytes(second), Source with { Revision = "revision-after-rename-and-move" }), ObservedAt).CopyDocument()!;

        Assert.Equal(DocumentId, a.Id);
        Assert.Equal(a.Id, b.Id);
        Assert.Equal(a.Sections[0].Id, b.Sections[0].Id);
        Assert.Equal(a.Sections[0].Pages[0].Id, b.Sections[0].Pages[0].Id);
        Assert.Equal(a.Sections[0].Pages[0].Blocks.Select(block => block.Id), b.Sections[0].Pages[0].Blocks.Select(block => block.Id));
        Assert.Equal(a.Metadata[BoardsLegacyDocumentAdapter.IdentityMapKey], b.Metadata[BoardsLegacyDocumentAdapter.IdentityMapKey]);
        Assert.NotEqual(a.Metadata[BoardsLegacyDocumentAdapter.ProvenanceKey], b.Metadata[BoardsLegacyDocumentAdapter.ProvenanceKey]);
    }

    [Fact]
    public void Raw_and_schema_zero_identity_uses_stable_Files_identity_but_does_not_invent_task_semantics()
    {
        var codec = new BoardsLegacyDocumentAdapter();
        var raw = Snapshot();
        var renamed = (JsonObject)raw.DeepClone(); renamed["title"] = "Different display name";
        var a = codec.PrepareCanonicalImport(codec.Inspect(Bytes(raw), Source), ObservedAt);
        var b = codec.PrepareCanonicalImport(codec.Inspect(Bytes(renamed), Source with { Revision = "new-revision" }), ObservedAt);
        var zero = codec.PrepareCanonicalImport(codec.Inspect(Bytes(Envelope(0)), Source), ObservedAt);
        var other = codec.PrepareCanonicalImport(codec.Inspect(Bytes(raw), Source with { FileId = Guid.Parse("9db56f83-9fb1-4b1c-8901-719a03d98ca1") }), ObservedAt);

        Assert.NotNull(a.TargetDocumentId);
        Assert.Equal(a.TargetDocumentId, b.TargetDocumentId);
        Assert.Equal(a.TargetDocumentId, zero.TargetDocumentId);
        Assert.NotEqual(a.TargetDocumentId, other.TargetDocumentId);
        Assert.False(a.IsCanonicalProjectionAvailable);
        Assert.Contains(a.Issues, issue => issue.Code == "SnapshotRequiresCanonicalMapping");
        Assert.Contains(codec.PrepareCanonicalImport(codec.Inspect(Bytes(raw)), ObservedAt).Issues, issue => issue.Code == "SourceIdentityUnavailable");
    }

    [Theory]
    [InlineData("section")]
    [InlineData("page")]
    [InlineData("block")]
    [InlineData("item")]
    [InlineData("cell")]
    [InlineData("style")]
    [InlineData("image")]
    [InlineData("attachment")]
    [InlineData("canvas")]
    [InlineData("expression")]
    public void Missing_nested_IDs_are_detected_before_any_legacy_initializer_can_invent_them(string entity)
    {
        var data = Envelope();
        var rich = data["richNotes"]!;
        var page = rich["sections"]![0]!["pages"]![0]!;
        var blocks = page["blocks"]!;
        var target = entity switch
        {
            "section" => rich["sections"]![0]!, "page" => page, "block" => blocks[0]!,
            "item" => blocks[2]!["items"]![0]!, "cell" => blocks[5]!["table"]!["rows"]![0]!["cells"]![0]!,
            "style" => rich["styles"]![0]!, "image" => blocks[6]!["image"]!,
            "attachment" => blocks[0]!["attachment"]!, "canvas" => page["canvas"]![0]!,
            _ => blocks[8]!["graph"]!["expressions"]![0]!
        };
        Assert.True(target.AsObject().Remove("id"));
        var bytes = Bytes(data);
        var codec = new BoardsLegacyDocumentAdapter();
        var inspection = codec.Inspect(bytes, Source);

        Assert.Contains(inspection.Issues, issue => issue.Code == "MissingIdentity" && issue.PreventsProjection);
        Assert.False(codec.PrepareCanonicalImport(inspection, ObservedAt).IsCanonicalProjectionAvailable);
        Assert.Equal(bytes, codec.ExportOriginal(inspection));
    }

    [Theory]
    [InlineData("omit")]
    [InlineData("null")]
    [InlineData("empty")]
    [InlineData("duplicate")]
    public void Root_structure_null_empty_and_duplicate_source_identity_are_not_repaired(string mutation)
    {
        var data = Envelope();
        var rich = data["richNotes"]!.AsObject();
        if (mutation == "omit") rich.Remove("sections");
        if (mutation == "null") rich["sections"] = null;
        if (mutation == "empty") rich["sections"] = new JsonArray();
        if (mutation == "duplicate") rich["sections"]!.AsArray().Add(rich["sections"]![0]!.DeepClone());
        var bytes = Bytes(data);
        var codec = new BoardsLegacyDocumentAdapter();
        var inspection = codec.Inspect(bytes, Source);

        Assert.Contains(inspection.Issues, issue => issue.PreventsProjection);
        Assert.False(codec.PrepareCanonicalImport(inspection, ObservedAt).IsCanonicalProjectionAvailable);
        Assert.Equal(bytes, codec.ExportOriginal(inspection));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Explicit_null_or_empty_ID_is_not_replaced_by_a_random_ID(string? id)
    {
        var data = Envelope();
        data["richNotes"]!["sections"]![0]!["pages"]![0]!["blocks"]![0]!["id"] = id;
        var bytes = Bytes(data);
        var codec = new BoardsLegacyDocumentAdapter();
        var inspection = codec.Inspect(bytes, Source);
        Assert.Contains(inspection.Issues, issue => issue.Code == "MissingIdentity");
        Assert.False(codec.PrepareCanonicalImport(inspection, ObservedAt).IsCanonicalProjectionAvailable);
        Assert.Equal(bytes, codec.ExportOriginal(inspection));
    }

    [Fact]
    public void Duplicate_JSON_members_refuse_last_member_wins_and_unknown_block_values_are_preserved()
    {
        var codec = new BoardsLegacyDocumentAdapter();
        var original = Bytes(Envelope());
        var text = Encoding.UTF8.GetString(original);
        var duplicate = Encoding.UTF8.GetBytes(text.Replace("\"schemaVersion\":3", "\"schemaVersion\":3,\"schemaVersion\":2", StringComparison.Ordinal));
        var ambiguous = codec.Inspect(duplicate, Source);
        Assert.Contains(ambiguous.Issues, issue => issue.Code == "AmbiguousProperty" && issue.PreventsProjection);
        Assert.False(codec.PrepareCanonicalImport(ambiguous, ObservedAt).IsCanonicalProjectionAvailable);
        Assert.Equal(duplicate, codec.ExportOriginal(ambiguous));

        var unknown = Envelope();
        unknown["richNotes"]!["sections"]![0]!["pages"]![0]!["blocks"]![0]!["kind"] = 999;
        var unknownBytes = Bytes(unknown);
        var inspection = codec.Inspect(unknownBytes, Source);
        var proposal = codec.PrepareCanonicalImport(inspection, ObservedAt);
        Assert.Contains(proposal.Issues, issue => issue.Code == "UnsupportedBlockPreserved" && issue.Pointer.EndsWith("/paragraph", StringComparison.Ordinal));
        Assert.DoesNotContain(proposal.CopyDocument()!.Sections[0].Pages[0].Blocks, block => block.PlainText == "Paragraph text");
        Assert.Equal(unknownBytes, codec.ExportOriginal(inspection));
    }

    [Theory]
    [InlineData("title")]
    [InlineData("font-1")]
    [InlineData("font-2")]
    [InlineData("font-3")]
    [InlineData("spacing")]
    [InlineData("page-size")]
    [InlineData("page-order")]
    [InlineData("timestamps")]
    public void Valid_legacy_values_outside_Notes_contract_refuse_projection_without_clamping(string boundary)
    {
        var data = Envelope();
        var rich = data["richNotes"]!;
        var page = rich["sections"]![0]!["pages"]![0]!;
        if (boundary == "title") rich["title"] = new string('t', 241);
        if (boundary.StartsWith("font-", StringComparison.Ordinal)) page["blocks"]![0]!["runs"]![0]!["fontSize"] = int.Parse(boundary[5..]);
        if (boundary == "spacing") page["blocks"]![0]!["lineSpacing"] = 0.49;
        if (boundary == "page-size") page["canvasWidth"] = 99;
        if (boundary == "page-order") page["order"] = -1;
        if (boundary == "timestamps") data["createdUtc"] = "2026-10-23T12:00:00Z";
        var bytes = Bytes(data);
        var codec = new BoardsLegacyDocumentAdapter();
        var inspection = codec.Inspect(bytes, Source);
        var candidate = codec.PrepareCanonicalImport(inspection, ObservedAt);

        Assert.DoesNotContain(inspection.Issues, issue => issue.PreventsProjection);
        Assert.False(candidate.IsCanonicalProjectionAvailable);
        Assert.Contains(candidate.Issues, issue => issue.Code == "CanonicalProjectionUnavailable");
        Assert.Equal(bytes, codec.ExportOriginal(inspection));
    }

    [Fact]
    public void Future_schema_and_corrupt_JSON_remain_original_data_without_empty_replacement()
    {
        var future = Envelope(); future["schemaVersion"] = 77;
        var inputs = new[] { Bytes(future), Encoding.UTF8.GetBytes("{\"format\":\"9to1.board\",invalid"), new byte[] { 0xff, 0x00 } };
        foreach (var input in inputs)
        {
            var codec = new BoardsLegacyDocumentAdapter();
            var inspection = codec.Inspect(input, Source);
            Assert.Contains(inspection.Issues, issue => issue.PreventsProjection);
            Assert.False(codec.PrepareCanonicalImport(inspection, ObservedAt).IsCanonicalProjectionAvailable);
            Assert.Equal(input, codec.ExportOriginal(inspection));
        }
    }

    [Fact]
    public void Earlier_unknown_style_reference_is_not_rewritten_to_Paragraph()
    {
        var data = Envelope(2);
        data["richNotes"]!["sections"]![0]!["pages"]![0]!["blocks"]![0]!["styleId"] = "unresolved-custom-style";
        var bytes = Bytes(data);
        var codec = new BoardsLegacyDocumentAdapter();
        var inspection = codec.Inspect(bytes, Source);

        Assert.Contains(inspection.Issues, issue => issue.Code == "UnresolvedStyle" && issue.PreventsProjection);
        Assert.False(codec.PrepareCanonicalImport(inspection, ObservedAt).IsCanonicalProjectionAvailable);
        Assert.Equal(bytes, codec.ExportOriginal(inspection));
    }

    [Fact]
    public void Unknown_nested_payloads_and_opaque_asset_references_survive_without_external_IO()
    {
        var data = Envelope();
        var blocks = data["richNotes"]!["sections"]![0]!["pages"]![0]!["blocks"]!;
        blocks[0]!["runs"]![0]!["futureMark"] = JsonNode.Parse("{\"semantic\":[false,null,\"retained\"]}");
        blocks[5]!["table"]!["rows"]![0]!["cells"]![0]!["futureFormula"] = "=KEEP()";
        blocks[8]!["graph"]!["expressions"]![0]!["futureSlider"] = JsonNode.Parse("{\"min\":-3,\"max\":8}");
        blocks[0]!["attachment"]!["localReference"] = "../../not-an-authorised-path";
        blocks[0]!["attachment"]!["dataBase64"] = "invalid-base64!";
        var bytes = Bytes(data);
        var codec = new BoardsLegacyDocumentAdapter();
        var inspection = codec.Inspect(bytes, Source);

        Assert.Contains(inspection.Issues, issue => issue.Code == "UnknownFieldPreserved" && issue.Pointer.EndsWith("/futureMark", StringComparison.Ordinal));
        Assert.Contains(inspection.Issues, issue => issue.Code == "UnknownFieldPreserved" && issue.Pointer.EndsWith("/futureFormula", StringComparison.Ordinal));
        Assert.Contains(inspection.Issues, issue => issue.Code == "UnknownFieldPreserved" && issue.Pointer.EndsWith("/futureSlider", StringComparison.Ordinal));
        Assert.Contains(inspection.Issues, issue => issue.Code == "InvalidEmbeddedAsset");
        Assert.Contains(inspection.Issues, issue => issue.Code == "ExternalAssetReferencePreserved");
        Assert.Equal(bytes, codec.ExportOriginal(inspection));
    }

    [Fact]
    public void Size_refusal_never_truncates_and_another_codec_cannot_replace_original_observations()
    {
        var input = Bytes(Envelope());
        var codec = new BoardsLegacyDocumentAdapter(maximumDocumentBytes: 64);
        var inspection = codec.Inspect(input, Source);
        Assert.False(inspection.OriginalBytesRetained);
        Assert.Equal(input.Length, inspection.OriginalByteLength);
        Assert.Contains(inspection.Issues, issue => issue.Code == "SizeLimitExceeded" && issue.PreventsProjection);
        Assert.Throws<InvalidOperationException>(() => codec.ExportOriginal(inspection));
        Assert.Throws<ArgumentException>(() => new BoardsLegacyDocumentAdapter().ExportOriginal(inspection));
    }

    [Fact]
    public void A_large_inline_archive_requires_canonical_Files_storage_without_losing_its_original()
    {
        var input = Bytes(Envelope());
        var codec = new BoardsLegacyDocumentAdapter(maximumInlineArchiveBytes: 64);
        var inspection = codec.Inspect(input, Source);
        var candidate = codec.PrepareCanonicalImport(inspection, ObservedAt);
        Assert.True(inspection.OriginalBytesRetained);
        Assert.False(candidate.IsCanonicalProjectionAvailable);
        Assert.Contains(candidate.Issues, issue => issue.Code == "CanonicalArchiveStorageRequired" && issue.PreventsProjection);
        Assert.Equal(input, codec.ExportOriginal(inspection));
    }

    private static byte[] Bytes(JsonNode node) => Encoding.UTF8.GetBytes(node.ToJsonString());

    private static JsonObject Snapshot() => JsonNode.Parse("""
        {"id":"task-board","title":"Task facet","version":9,"groups":[{"id":"g","title":"To revise","cards":[
          {"id":"parent","title":"Subject"},{"id":"child","title":"Topic","parentCardId":"parent","attachments":[
          {"id":"file","displayName":"Source","localReference":"sha256:unknown","availability":"Missing"}]}]}],
          "freeform":{"items":[{"cardId":"child","x":-17.25,"y":82.5,"width":280,"height":160,"zIndex":4}]}}
        """)!.AsObject();

    private static JsonObject Envelope(int schema = 3)
    {
        var result = new JsonObject { ["format"] = "9to1.board", ["schemaVersion"] = schema, ["snapshot"] = Snapshot() };
        if (schema > 0)
        {
            result["documentId"] = DocumentId.ToString("D");
            result["createdUtc"] = "2026-10-20T12:00:00Z";
            result["modifiedUtc"] = "2026-10-21T12:00:00Z";
        }
        if (schema < 2) return result;
        result["richNotes"] = JsonNode.Parse("""
        {"title":"Revision notebook","version":42,"styles":[{"id":"normal","name":"Paragraph","fontSize":14},
          {"id":"heading-6","name":"Heading six","fontSize":12,"bold":true}],"sections":[{"id":"sec/subject","title":"Subject","pages":[
          {"id":"page-topic","title":"Topic","order":0,"canvasWidth":1200,"canvasHeight":900,"blocks":[
            {"id":"paragraph","kind":0,"order":0,"plainText":"Paragraph text","runs":[{"text":"selected","bold":true,"baseline":2}],
              "attachment":{"id":"attachment","displayName":"Source","dataBase64":"AQID","sizeBytes":3}},
            {"id":"heading","kind":1,"order":1,"styleId":"heading-6","plainText":"Topic heading"},
            {"id":"bullets","kind":2,"order":2,"items":[{"id":"bullet-item","text":"Point","level":1}]},
            {"id":"numbered","kind":3,"order":3,"items":[{"id":"number-item","text":"Step"}]},
            {"id":"checklist","kind":4,"order":4,"items":[{"id":"check-item","text":"Remember this","checked":true}]},
            {"id":"table","kind":5,"order":5,"table":{"borderWidth":2,"columnWidths":[140],"rows":[{"cells":[
              {"id":"cell","text":"Cell text","bold":true,"runs":[{"text":"Cell text","italic":true}]}]}]}},
            {"id":"image","kind":6,"order":6,"image":{"id":"image-payload","mediaType":"image/png","dataBase64":"AQID","sizeBytes":3,"altText":"Diagram"}},
            {"id":"divider","kind":7,"order":7,"divider":{"thickness":3.75,"lineStyle":3,"color":"#FF1199AA"}},
            {"id":"graph","kind":8,"order":8,"graph":{"expressions":[{"id":"quadratic","text":"y=x^2"},{"id":"linear","text":"y=2x"}],
              "viewport":{"xMin":-8,"xMax":8,"yMin":-5,"yMax":12},"points":[{"x":1,"y":1,"label":"P"}]}}
          ],"ink":[{"tool":1,"width":4,"color":"#FF1199AA","points":[{"x":-7.25,"y":12.5,"pressure":0.8}]}],
          "inkView":{"panX":3,"panY":-2,"zoom":1.5},"canvas":[{"id":"canvas-note","kind":"Text","text":"Spatial note","x":19.5,"y":-22,"width":260,"height":160}]}]}]}
        """);
        return result;
    }
}
