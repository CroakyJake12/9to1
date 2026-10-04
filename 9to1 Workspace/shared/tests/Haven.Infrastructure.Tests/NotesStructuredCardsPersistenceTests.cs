using System.Runtime.ExceptionServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Haven.Application;
using Haven.Core;
using Haven.Core.Mathematics;
using Haven.Infrastructure;
using Xunit;

namespace Haven.Infrastructure.Tests;

/// <summary>Real existing Notes files/CAS and native JSON format. These cases grant no private Home/root authority.</summary>
public sealed class NotesStructuredCardsPersistenceTests
{
    private static readonly JsonSerializerOptions LegacyOptions = new(JsonSerializerDefaults.Web)
    { WriteIndented = true, PropertyNameCaseInsensitive = true };

    [Fact]
    public Task Complete_rich_faces_survive_physical_save_old_codec_migration_native_export_import_and_fresh_owner_search() =>
        WithPhysicalAsync(async fixture =>
        {
            var document = Document(); var card = Card(document);
            var payload = card.Metadata[NotesCardContentCodec.MetadataKey];
            var first = await fixture.Create().SaveAsync(document, "Owning rich card", fixture.Token);
            Assert.Equal(1, first.Version); Assert.True(first.VersionHistoryComplete);
            var reopened = Assert.IsType<NotesDocument>(await fixture.Create().LoadAsync(document.Id, fixture.Token));
            Assert.Equal(payload, Card(reopened).Metadata[NotesCardContentCodec.MetadataKey]);
            Assert.Equal(document.Id, reopened.Id); Assert.Equal(0, document.Version); Assert.Equal(first.Version, reopened.Version);
            ExactFaces(card, Card(reopened));

            // Existing model bodies/schema are unchanged: its old ordinary serializer retains the opaque metadata string.
            var oldCodec = Assert.IsType<NotesDocument>(JsonSerializer.Deserialize<NotesDocument>(
                await File.ReadAllBytesAsync(first.CurrentPath, fixture.Token), LegacyOptions));
            Assert.Equal(1, oldCodec.SchemaVersion);
            Assert.Equal(payload, Card(oldCodec).Metadata[NotesCardContentCodec.MetadataKey]);
            oldCodec.Title = "An ordinary old-model title edit";
            var oldSave = await fixture.Create().SaveAsync(oldCodec, "Metadata-preserving old-model edit", fixture.Token);
            Assert.Equal(2, oldSave.Version);
            var migrated = await new NotesDocumentMigrator().ReadAndMigrateAsync(oldSave.CurrentPath, fixture.Token);
            Assert.Equal(1, migrated.SourceSchemaVersion); Assert.Equal(1, migrated.TargetSchemaVersion);
            Assert.Equal(card.Id, Card(migrated.Document).Id);
            Assert.Equal(card.Flashcard!.CardId, Card(migrated.Document).Flashcard!.CardId);
            Assert.Equal(payload, Card(migrated.Document).Metadata[NotesCardContentCodec.MetadataKey]);
            Assert.True(new NotesDocumentValidator().Validate(migrated.Document).IsValid);

            var format = new NotesImportExportService(new NotesDocumentValidator(), fixture.Diagnostics!);
            var exported = Path.Combine(fixture.Paths.DataDirectory, "editable.haven-notes.json");
            await format.ExportAsync(migrated.Document, exported, fixture.Token);
            var imported = await format.ImportAsync(exported, fixture.Token);
            Assert.NotEqual(document.Id, imported.Id); Assert.Equal(0, imported.Version);
            Assert.Equal(payload, Card(imported).Metadata[NotesCardContentCodec.MetadataKey]);
            ExactFaces(card, Card(imported));
            var importedSave = await fixture.Create().SaveAsync(imported, "Actual new imported artifact", fixture.Token);
            Assert.Equal(imported.Id, importedSave.DocumentId); Assert.Equal(1, importedSave.Version);
            ExactFaces(card, Card(Assert.IsType<NotesDocument>(await fixture.Create().LoadAsync(imported.Id, fixture.Token))));

            var matches = await fixture.Create().SearchAsync("rich-only-A2-witness", fixture.Token);
            Assert.Contains(matches, hit => hit.DocumentId == document.Id && hit.BlockId == card.Id);
            Assert.Contains(matches, hit => hit.DocumentId == imported.Id && hit.BlockId == card.Id);
            Assert.Contains("rich-only-A2-witness", NotesTextStatistics.EnumerateText(migrated.Document));
            Assert.True(NotesTextStatistics.Calculate(migrated.Document).Words > 0);
        });

    [Fact]
    public Task Actual_legacy_fallback_edit_is_explicit_conflict_without_quarantine_backup_adoption_or_overwrite() =>
        WithPhysicalAsync(async fixture =>
        {
            var document = Document(); var saved = await fixture.Create().SaveAsync(document, "Original", fixture.Token);
            // A genuine old-model save keeps the rich metadata but does not understand its bound fallback.
            var legacy = Assert.IsType<NotesDocument>(JsonSerializer.Deserialize<NotesDocument>(
                await File.ReadAllBytesAsync(saved.CurrentPath, fixture.Token), LegacyOptions));
            Card(legacy).Flashcard!.Front = "Actual legacy scalar replacement";
            await File.WriteAllBytesAsync(saved.CurrentPath, JsonSerializer.SerializeToUtf8Bytes(legacy, LegacyOptions), fixture.Token);
            var before = await fixture.SnapshotAsync();
            var loadFailure = await Assert.ThrowsAsync<NotesCardContentException>(() =>
                fixture.Create().LoadAsync(document.Id, fixture.Token));
            Assert.Equal("RevisionConflict", loadFailure.Code);
            var saveFailure = await Assert.ThrowsAsync<NotesCardContentException>(() =>
                fixture.Create().SaveAsync(document, "Must not replace changed original", fixture.Token));
            Assert.Equal("RevisionConflict", saveFailure.Code);
            Assert.Equal(before, await fixture.SnapshotAsync());
            Assert.Equal(0, document.Version);
            Assert.DoesNotContain(Directory.GetFiles(Path.GetDirectoryName(saved.CurrentPath)!), path =>
                Path.GetFileName(path).Contains("corrupt-current", StringComparison.Ordinal));
        });

    [Theory]
    [InlineData("9to1.Cards.content.v2")]
    [InlineData("9to1.cards.content.v1")]
    public Task Unknown_version_or_case_alias_on_actual_current_is_retained_and_never_recovered_as_flat_legacy(string key) =>
        WithPhysicalAsync(async fixture =>
        {
            var document = Document(); var saved = await fixture.Create().SaveAsync(document, "Original", fixture.Token);
            var current = Assert.IsType<NotesDocument>(JsonSerializer.Deserialize<NotesDocument>(
                await File.ReadAllBytesAsync(saved.CurrentPath, fixture.Token), LegacyOptions));
            var card = Card(current); var originalPayload = card.Metadata[NotesCardContentCodec.MetadataKey];
            card.Metadata.Remove(NotesCardContentCodec.MetadataKey); card.Metadata.Add(key, originalPayload);
            await File.WriteAllBytesAsync(saved.CurrentPath, JsonSerializer.SerializeToUtf8Bytes(current, LegacyOptions), fixture.Token);
            var before = await fixture.SnapshotAsync();
            var error = await Assert.ThrowsAsync<NotesCardContentException>(() => fixture.Create().LoadAsync(document.Id, fixture.Token));
            Assert.Equal("UnsupportedCardContent", error.Code);
            await Assert.ThrowsAsync<NotesCardContentException>(() => fixture.Create().SaveAsync(document, "No future-schema replacement", fixture.Token));
            Assert.Equal(before, await fixture.SnapshotAsync());
        });

    [Theory]
    [InlineData("1e-29")]
    [InlineData("0.10000000000000000000000000001")]
    public Task Original_graph_decimal_tokens_are_refused_before_rounding_and_before_actual_owner_bytes_change(string literal) =>
        WithPhysicalAsync(async fixture =>
        {
            var document = Document(); var saved = await fixture.Create().SaveAsync(document, "Original", fixture.Token);
            var changed = Assert.IsType<NotesDocument>(await fixture.Create().LoadAsync(document.Id, fixture.Token));
            var card = Card(changed); var raw = card.Metadata[NotesCardContentCodec.MetadataKey];
            Assert.Equal(2, raw.Split("\"position\":{\"x\":1.25", StringSplitOptions.None).Length);
            card.Metadata[NotesCardContentCodec.MetadataKey] = raw.Replace("\"position\":{\"x\":1.25", "\"position\":{\"x\":" + literal, StringComparison.Ordinal);
            var before = await fixture.SnapshotAsync();
            var error = await Assert.ThrowsAsync<NotesCardContentException>(() =>
                fixture.Create().SaveAsync(changed, "Reject original lossy decimal", fixture.Token));
            Assert.Equal("UnsupportedCardContent", error.Code);
            Assert.Equal(before, await fixture.SnapshotAsync()); Assert.Equal(saved.Version, changed.Version);
            ExactFaces(Card(document), Card(Assert.IsType<NotesDocument>(await fixture.Create().LoadAsync(document.Id, fixture.Token))));
        });

    [Fact]
    public Task Exactly_representable_scientific_graph_token_commits_and_fresh_owner_reopens_exact_body() =>
        WithPhysicalAsync(async fixture =>
        {
            var document = Document(); var original = await fixture.Create().SaveAsync(document, "Original", fixture.Token);
            var current = Assert.IsType<NotesDocument>(await fixture.Create().LoadAsync(document.Id, fixture.Token));
            var card = Card(current); var raw = card.Metadata[NotesCardContentCodec.MetadataKey];
            Assert.Equal(2, raw.Split("\"position\":{\"x\":1.25", StringSplitOptions.None).Length);
            var scientific = raw.Replace("\"position\":{\"x\":1.25", "\"position\":{\"x\":125e-2", StringComparison.Ordinal);
            card.Metadata[NotesCardContentCodec.MetadataKey] = scientific;
            var saved = await fixture.Create().SaveAsync(current, "Exact scientific source", fixture.Token);
            Assert.Equal(original.Version + 1, saved.Version);
            var reopened = Assert.IsType<NotesDocument>(await fixture.Create().LoadAsync(document.Id, fixture.Token));
            Assert.Equal(scientific, Card(reopened).Metadata[NotesCardContentCodec.MetadataKey]);
            Assert.Equal(1.25m, Assert.IsType<GraphPoint>(NotesCardContentCodec.Read(Card(reopened))!.Front.Items[1].Graph!.Primitives[0]).Position.X);
            ExactFaces(Card(document), Card(reopened));
        });

    [Theory]
    [InlineData("duplicate-property")]
    [InlineData("unknown-property")]
    [InlineData("hidden-nested-card")]
    [InlineData("canonical-block-rebind")]
    public Task Malformed_recursive_or_rebound_payloads_refuse_actual_save_without_rewriting_any_original(string kind) =>
        WithPhysicalAsync(async fixture =>
        {
            var document = Document(); await fixture.Create().SaveAsync(document, "Original", fixture.Token);
            var changed = Assert.IsType<NotesDocument>(await fixture.Create().LoadAsync(document.Id, fixture.Token));
            var card = Card(changed); var raw = card.Metadata[NotesCardContentCodec.MetadataKey];
            if (kind == "duplicate-property")
                raw = raw.Replace("\"schemaVersion\":1", "\"schemaVersion\":1,\"schemaVersion\":1", StringComparison.Ordinal);
            else if (kind == "unknown-property")
                raw = raw.Insert(1, "\"unknownOwningEngine\":true,");
            else
            {
                var node = JsonNode.Parse(raw)!;
                if (kind == "hidden-nested-card")
                    node["front"]!["items"]![0]!["block"]!["flashcard"] =
                        JsonSerializer.SerializeToNode(new NotesFlashcardData { Front = "Hidden", Back = "Recursive" }, LegacyOptions);
                else node["blockId"] = Guid.NewGuid();
                raw = node.ToJsonString();
            }
            card.Metadata[NotesCardContentCodec.MetadataKey] = raw;
            var before = await fixture.SnapshotAsync();
            await Assert.ThrowsAsync<NotesCardContentException>(() => fixture.Create().SaveAsync(changed, "Refuse malformed payload", fixture.Token));
            Assert.Equal(before, await fixture.SnapshotAsync());
        });

    [Fact]
    public Task Document_global_face_identity_and_canonical_graph_body_conflicts_refuse_current_owner_save() =>
        WithPhysicalAsync(async fixture =>
        {
            var document = Document(); await fixture.Create().SaveAsync(document, "Original", fixture.Token);
            var changed = Assert.IsType<NotesDocument>(await fixture.Create().LoadAsync(document.Id, fixture.Token));
            var original = Card(changed); var content = NotesCardContentCodec.Read(original)!;
            var second = NotesBlock.FlashcardBlock(); second.Order = 1;
            var child = content.Front.Items[0].Block!;
            var graph = content.Front.Items[1].Graph!;
            var duplicateIds = new NotesCardStructuredContent(1, second.Id, second.Flashcard!.CardId,
                NotesCardContentCodec.FallbackFingerprint(second.Flashcard.Front),
                NotesCardContentCodec.FallbackFingerprint(second.Flashcard.Back),
                content.Front, content.Back);
            second.Metadata[NotesCardContentCodec.MetadataKey] = NotesCardContentCodec.Encode(second, duplicateIds);
            changed.Sections[0].Pages[0].Blocks.Add(second);
            var before = await fixture.SnapshotAsync();
            await Assert.ThrowsAsync<NotesCardContentException>(() => fixture.Create().SaveAsync(changed, "Duplicate reachable IDs", fixture.Token));
            Assert.Equal(before, await fixture.SnapshotAsync());

            var freshFront = new NotesCardFace([
                new(Guid.NewGuid(), NotesCardFaceItemKind.Graph, null, graph with { AccessibleDescription = "Different body, same canonical graph" })], "#FFFFFFFF");
            var freshBackBlock = NotesBlock.CreateParagraph("Independent back");
            var freshBack = new NotesCardFace([new(freshBackBlock.Id, NotesCardFaceItemKind.Block, freshBackBlock, null)], "#FFFFFFFF");
            var conflictingGraph = duplicateIds with { Front = freshFront, Back = freshBack };
            second.Metadata[NotesCardContentCodec.MetadataKey] = NotesCardContentCodec.Encode(second, conflictingGraph);
            await Assert.ThrowsAsync<NotesCardContentException>(() => fixture.Create().SaveAsync(changed, "Conflicting canonical graph body", fixture.Token));
            Assert.Equal(before, await fixture.SnapshotAsync());
            var originalExpression = graph.Expressions[0];
            var otherGraph = graph with { GraphID = Guid.NewGuid(), Expressions =
                [originalExpression with { LaTeX = "x+1" }] };
            var expressionConflict = conflictingGraph with { Front = new([
                new(Guid.NewGuid(), NotesCardFaceItemKind.Graph, null, otherGraph)], "#FFFFFFFF") };
            second.Metadata[NotesCardContentCodec.MetadataKey] = NotesCardContentCodec.Encode(second, expressionConflict);
            var expressionFailure = await Assert.ThrowsAsync<NotesCardContentException>(() =>
                fixture.Create().SaveAsync(changed, "Conflicting canonical expression body", fixture.Token));
            Assert.Equal("RevisionConflict", expressionFailure.Code);
            Assert.Equal(before, await fixture.SnapshotAsync()); Assert.NotEqual(Guid.Empty, child.Id);
        });

    [Fact]
    public Task Independent_rich_card_editors_keep_physical_version_CAS_and_pre_cancel_does_not_write() =>
        WithPhysicalAsync(async fixture =>
        {
            var document = Document(); var saved = await fixture.Create().SaveAsync(document, "Original", fixture.Token);
            var left = Assert.IsType<NotesDocument>(await fixture.Create().LoadAsync(document.Id, fixture.Token));
            var stale = Assert.IsType<NotesDocument>(await fixture.Create().LoadAsync(document.Id, fixture.Token));
            left.Title = "Acknowledged rich editor"; var committed = await fixture.Create().SaveAsync(left, "Independent commit", fixture.Token);
            Assert.Equal(saved.Version + 1, committed.Version); var before = await fixture.SnapshotAsync();
            stale.Title = "Stale rich editor";
            await Assert.ThrowsAsync<NotesRevisionConflictException>(() => fixture.Create().SaveAsync(stale, "Stale actual CAS", fixture.Token));
            Assert.Equal(before, await fixture.SnapshotAsync()); Assert.Equal(saved.Version, stale.Version);
            using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
            var current = Assert.IsType<NotesDocument>(await fixture.Create().LoadAsync(document.Id, fixture.Token));
            var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                fixture.Create().SaveAsync(current, "Cancelled before owning write", cancelled.Token));
            Assert.Equal(cancelled.Token, error.CancellationToken); Assert.Equal(before, await fixture.SnapshotAsync());
            ExactFaces(Card(document), Card(current));
        });

    [Fact]
    public Task Rich_request_is_detached_before_waiting_for_the_same_actual_root_and_later_source_edits_are_not_written_or_overwritten() =>
        WithPhysicalAsync(async fixture =>
        {
            var document = Document(); var seed = await fixture.Create().SaveAsync(document, "Original", fixture.Token);
            var request = Assert.IsType<NotesDocument>(await fixture.Create().LoadAsync(document.Id, fixture.Token));
            var originalPayload = Card(request).Metadata[NotesCardContentCodec.MetadataKey];
            var holder = new HeldActualValidator();
            var holdingRepository = new NotesRepository(fixture.Paths, holder, fixture.Diagnostics!);
            var originalRead = Task.Run(() => holdingRepository.LoadAsync(document.Id, fixture.Token));
            Task<NotesSaveResult>? originalSave = null; var failures = new List<Exception>();
            void Add(Exception error) { if (!failures.Any(item => ReferenceEquals(item, error))) failures.Add(error); }
            try
            {
                await holder.Entered.Task.WaitAsync(fixture.Token);
                originalSave = fixture.Create().SaveAsync(request, "Captured before actual root admission", fixture.Token);
                Assert.False(originalSave.IsCompleted);
                Card(request).Metadata.Remove(NotesCardContentCodec.MetadataKey);
                Card(request).Metadata.Add("9to1.Cards.content.v2", "A later unsupported caller draft");
                request.Title = "Newer caller edit";
                holder.Release.TrySetResult();
                var committed = await originalSave;
                Assert.Equal(seed.Version + 1, committed.Version); Assert.Equal(document.Id, committed.DocumentId);
                Assert.Equal(seed.Version, request.Version); Assert.Equal("Newer caller edit", request.Title);
                Assert.Equal("A later unsupported caller draft", Card(request).Metadata["9to1.Cards.content.v2"]);
                var actual = Assert.IsType<NotesDocument>(await fixture.Create().LoadAsync(document.Id, fixture.Token));
                Assert.Equal(committed.Version, actual.Version); Assert.Equal(document.Title, actual.Title);
                Assert.Equal(originalPayload, Card(actual).Metadata[NotesCardContentCodec.MetadataKey]);
                ExactFaces(Card(document), Card(actual));
            }
            catch (Exception error) { Add(error); }
            finally
            {
                holder.Release.TrySetResult();
                try { await originalRead; } catch (Exception error) { Add(error); }
                if (originalSave is not null)
                    try { await originalSave; } catch (Exception error) { Add(error); }
            }
            if (failures.Count == 1) ExceptionDispatchInfo.Capture(failures[0]).Throw();
            if (failures.Count > 1) throw new AggregateException("Original owning root read/save and independent cleanup failures.", failures);
        });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task Retained_original_card_cannot_silently_drop_rich_metadata_or_rebind_CardId(bool rebindCard)
        => WithPhysicalAsync(async fixture =>
        {
            var original = Document(); var acknowledged = await fixture.Create().SaveAsync(original, "Original rich card", fixture.Token);
            var current = Assert.IsType<NotesDocument>(await fixture.Create().LoadAsync(original.Id, fixture.Token));
            var oldBlockId = Card(current).Id; var oldCardId = Card(current).Flashcard!.CardId;
            var before = await fixture.SnapshotAsync();
            Assert.True(Card(current).Metadata.Remove(NotesCardContentCodec.MetadataKey));
            if (rebindCard) Card(current).Flashcard!.CardId = Guid.NewGuid();
            var error = await Assert.ThrowsAsync<NotesCardContentException>(() =>
                fixture.Create().SaveAsync(current, "Implicit source loss", fixture.Token));
            Assert.Equal("RevisionConflict", error.Code);
            Assert.Equal(before, await fixture.SnapshotAsync()); Assert.Equal(acknowledged.Version, current.Version);
            var reopened = Assert.IsType<NotesDocument>(await fixture.Create().LoadAsync(original.Id, fixture.Token));
            Assert.Equal(oldBlockId, Card(reopened).Id); Assert.Equal(oldCardId, Card(reopened).Flashcard!.CardId);
            ExactFaces(Card(original), Card(reopened)); Assert.Equal(acknowledged.Version, reopened.Version);
        });

    [Fact]
    public Task Original_payload_bytes_and_face_count_bounds_refuse_without_changing_actual_owner_files() =>
        WithPhysicalAsync(async fixture =>
        {
            var document = Document(); await fixture.Create().SaveAsync(document, "Original", fixture.Token);
            var current = Assert.IsType<NotesDocument>(await fixture.Create().LoadAsync(document.Id, fixture.Token));
            var card = Card(current); var content = NotesCardContentCodec.Read(card)!;
            var before = await fixture.SnapshotAsync();
            var excessiveFace = new NotesCardFace(Enumerable.Range(0, NotesCardContentCodec.MaximumFaceItems + 1)
                .Select(index =>
                {
                    var child = NotesBlock.CreateParagraph("Explicit bounded face " + index);
                    return new NotesCardFaceItem(child.Id, NotesCardFaceItemKind.Block, child, null);
                }).ToArray(), "#FFFFFFFF");
            Assert.Throws<NotesCardContentException>(() =>
                NotesCardContentCodec.Encode(card, content with { Front = excessiveFace }));
            Assert.Equal(before, await fixture.SnapshotAsync());
            card.Metadata[NotesCardContentCodec.MetadataKey] = new string('x', NotesCardContentCodec.MaximumContentBytes + 1);
            var error = await Assert.ThrowsAsync<NotesCardContentException>(() =>
                fixture.Create().SaveAsync(current, "Refuse oversized original payload", fixture.Token));
            Assert.Equal("UnsupportedCardContent", error.Code);
            Assert.Equal(before, await fixture.SnapshotAsync());
        });

    [Theory]
    [InlineData(".txt")]
    [InlineData(".md")]
    [InlineData(".html")]
    [InlineData(".docx")]
    [InlineData(".pdf")]
    public Task Unsupported_rich_projection_export_refuses_before_destination_directory_or_bytes_exist(string extension) =>
        WithPhysicalAsync(async fixture =>
        {
            var document = Document(); await fixture.Create().SaveAsync(document, "Original", fixture.Token);
            var format = new NotesImportExportService(new NotesDocumentValidator(), fixture.Diagnostics!);
            var parent = Path.Combine(fixture.Paths.DataDirectory, "uncreated-export");
            var destination = Path.Combine(parent, "editable" + extension);
            var before = await fixture.SnapshotAsync();
            var error = await Assert.ThrowsAsync<NotesCardContentException>(() => format.ExportAsync(document, destination, fixture.Token));
            Assert.Equal("UnsupportedCardContent", error.Code);
            Assert.False(Directory.Exists(parent)); Assert.False(File.Exists(destination));
            Assert.Equal(before, await fixture.SnapshotAsync());
        });

    [Fact]
    public Task Complete_canonical_graph_text_uses_same_card_bound_before_actual_owner_write()
        => WithPhysicalAsync(async fixture =>
        {
            var original = Document(); var saved = await fixture.Create().SaveAsync(original, "Original rich card", fixture.Token);
            var changed = Assert.IsType<NotesDocument>(await fixture.Create().LoadAsync(original.Id, fixture.Token));
            var card = Card(changed); var node = JsonNode.Parse(card.Metadata[NotesCardContentCodec.MetadataKey])!;
            // Every expression is structurally valid under the unchanged canonical MathServiceLimits.
            // Its two actual text fields add up to more than the complete owning card's 1 MiB limit.
            var expressions = Enumerable.Range(0, 129).Select(_ =>
                new MathExpression(Guid.NewGuid(), 1, new string('x', 4096), new string('a', 4096))).ToArray();
            var graph = new GraphDefinition(Guid.NewGuid(), 1, new(-10, 10, -10, 10), expressions, [], [],
                "Exact canonical graph text cap witness");
            var graphBytes = MathObjectCodec.Encode(graph);
            Assert.True(graphBytes.Length < NotesCardContentCodec.MaximumContentBytes);
            Assert.True(expressions.Sum(expression => (long)expression.LaTeX.Length +
                expression.AccessibleDescription.Length) > NotesCardContentCodec.MaximumTextCharacters);
            node["front"]!["items"]![1]!["graph"] = JsonSerializer.SerializeToNode(graph, LegacyOptions);
            var excessive = node.ToJsonString(); Assert.True(System.Text.Encoding.UTF8.GetByteCount(excessive) <
                NotesCardContentCodec.MaximumContentBytes);
            card.Metadata[NotesCardContentCodec.MetadataKey] = excessive;
            var before = await fixture.SnapshotAsync();
            var failure = await Assert.ThrowsAsync<NotesCardContentException>(() =>
                fixture.Create().SaveAsync(changed, "Complete graph text must refuse", fixture.Token));
            Assert.Equal("UnsupportedCardContent", failure.Code);
            Assert.Equal(saved.Version, changed.Version); Assert.Equal(before, await fixture.SnapshotAsync());
            ExactFaces(Card(original), Card(Assert.IsType<NotesDocument>(
                await fixture.Create().LoadAsync(original.Id, fixture.Token))));
        });

    private static NotesDocument Document()
    {
        var document = NotesDocument.Create("Owning structured cards");
        var page = document.Sections[0].Pages[0]; page.Blocks.Clear();
        var card = NotesBlock.FlashcardBlock(); page.Blocks.Add(card);
        var paragraph = NotesBlock.CreateParagraph("rich-only-A2-witness");
        paragraph.Runs.Add(new NotesTextRun { Text = "bold owned text", Bold = true });
        var canvas = NotesBlock.CanvasBlock();
        canvas.Canvas!.Strokes.Add(new NotesInkStroke { Points = [new() { X = 1.25, Y = 2.5, Pressure = .7 }] });
        var expression = new MathExpression(Guid.NewGuid(), 1, "\\frac{1}{x-1}", "One divided by x minus one");
        var graph = new GraphDefinition(Guid.NewGuid(), 1, new(-10, 10, -10, 10), [expression],
            [new GraphPoint(Guid.NewGuid(), new(1.25m, 2.5m)),
             new GraphFunction(Guid.NewGuid(), new(expression.ExpressionID, expression.Revision), "x", new(-4, 4))],
            [], "Exact canonical graph body and original function domain");
        var content = new NotesCardStructuredContent(1, card.Id, card.Flashcard!.CardId,
            NotesCardContentCodec.FallbackFingerprint(card.Flashcard.Front),
            NotesCardContentCodec.FallbackFingerprint(card.Flashcard.Back),
            new([new(paragraph.Id, NotesCardFaceItemKind.Block, paragraph, null),
                 new(Guid.NewGuid(), NotesCardFaceItemKind.Graph, null, graph)], "#FFFFFFFF"),
            new([new(canvas.Id, NotesCardFaceItemKind.Block, canvas, null)], "#FF102030"));
        card.Metadata[NotesCardContentCodec.MetadataKey] = NotesCardContentCodec.Encode(card, content);
        return document;
    }

    private static NotesBlock Card(NotesDocument document) => document.Sections[0].Pages[0].Blocks[0];
    private static void ExactFaces(NotesBlock original, NotesBlock current)
    {
        Assert.Equal(original.Id, current.Id); Assert.Equal(original.Flashcard!.CardId, current.Flashcard!.CardId);
        var left = NotesCardContentCodec.Read(original)!; var right = NotesCardContentCodec.Read(current)!;
        Assert.Equal(NotesCardContentCodec.Encode(original, left), NotesCardContentCodec.Encode(current, right));
        Assert.Equal(MathObjectCodec.Encode(left.Front.Items[1].Graph!), MathObjectCodec.Encode(right.Front.Items[1].Graph!));
        Assert.Equal(new GraphDomain(-4, 4), Assert.IsType<GraphFunction>(right.Front.Items[1].Graph!.Primitives[1]).Domain);
        Assert.Equal(.7, right.Back.Items[0].Block!.Canvas!.Strokes[0].Points[0].Pressure);
    }

    private static async Task WithPhysicalAsync(Func<Fixture, Task> body)
    {
        var fixture = new Fixture(); var failures = new List<Exception>(); Task? original = null; var created = false;
        void Add(Exception error) { if (!failures.Any(item => ReferenceEquals(item, error))) failures.Add(error); }
        try
        {
            if (Directory.Exists(fixture.Paths.DataDirectory) || File.Exists(fixture.Paths.DataDirectory))
                throw new InvalidOperationException("The exact private fixture path already exists.");
            Directory.CreateDirectory(fixture.Paths.DataDirectory); created = true;
            fixture.Diagnostics = new ProductionDiagnostics(fixture.Paths);
            original = body(fixture);
            await original.WaitAsync(fixture.Token);
        }
        catch (Exception error) { Add(error); }
        finally
        {
            try { fixture.Deadline.Cancel(); } catch (Exception error) { Add(error); }
            if (original is not null)
                try { await original; } catch (Exception error) { Add(error); }
            if (fixture.Diagnostics is not null)
                try { await fixture.Diagnostics.DisposeAsync(); } catch (Exception error) { Add(error); }
            if (created && Directory.Exists(fixture.Paths.DataDirectory))
                try { Directory.Delete(fixture.Paths.DataDirectory, recursive: true); } catch (Exception error) { Add(error); }
            try { fixture.Deadline.Dispose(); } catch (Exception error) { Add(error); }
        }
        if (failures.Count == 1) ExceptionDispatchInfo.Capture(failures[0]).Throw();
        if (failures.Count > 1) throw new AggregateException("Original physical Cards operation and independent cleanup failures.", failures);
    }

    // Controls only the existing validator port after real owning file deserialization while the real root gate is held.
    // Every result still comes from the actual whole NotesDocumentValidator; this is no authority or persistence replacement.
    private sealed class HeldActualValidator : INotesDocumentValidator
    {
        private int _held;
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public NotesValidationResult Validate(NotesDocument document)
        {
            var result = new NotesDocumentValidator().Validate(document);
            if (Interlocked.Exchange(ref _held, 1) == 0)
            {
                Entered.TrySetResult();
                Release.Task.GetAwaiter().GetResult();
            }
            return result;
        }
    }

    private sealed class Fixture
    {
        public Paths Paths { get; } = new();
        public CancellationTokenSource Deadline { get; } = new(TimeSpan.FromSeconds(90));
        public CancellationToken Token => Deadline.Token;
        public ProductionDiagnostics? Diagnostics { get; set; }
        public NotesRepository Create() => new(Paths, new NotesDocumentValidator(), Diagnostics!);
        public async Task<string[]> SnapshotAsync()
        {
            var result = new List<string>();
            foreach (var path in Directory.GetFiles(Path.Combine(Paths.DataDirectory, "Notes"), "*", SearchOption.AllDirectories)
                .Order(StringComparer.Ordinal))
                result.Add(Path.GetRelativePath(Paths.DataDirectory, path) + ":" +
                    Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(await File.ReadAllBytesAsync(path, Token))));
            return result.ToArray();
        }
    }

    private sealed class Paths : IAppPaths
    {
        public string DataDirectory { get; } = Path.Combine(Path.GetTempPath(), "astra-le12-cards-" + Guid.NewGuid().ToString("N"));
        public string DatabasePath => Path.Combine(DataDirectory, "app.db");
        public string BrowserProfileDirectory => Path.Combine(DataDirectory, "browser");
        public string AttachmentsDirectory => Path.Combine(DataDirectory, "attachments");
        public string LegacyStatePath => Path.Combine(DataDirectory, "legacy.json");
        public string LogsDirectory => Path.Combine(DataDirectory, "logs");
    }
}
