using System.Text;
using System.Text.Json;
using CakeOS.Apps.Boards.Contract;
using Xunit;

namespace CakeOS.Apps.Boards.Tests;

/// <summary>
/// RC data-safety matrix for rich revision notes in <c>.9to1board</c> schema v2,
/// exercised through the production <see cref="RichBoardSession"/> + store path.
/// </summary>
public sealed class HavenRichBoardTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task Student_acceptance_ALevel_Maths_survives_close_reopen_edit_and_copy()
    {
        var root = TempDirectory();
        try
        {
            var boardPath = Path.Combine(root, "A-Level Maths.9to1board");
            var store = new JsonFileHavenBoardStore(root);
            Guid documentId;

            // Create the board through the production session path.
            await using (var session = await RichBoardSession.CreateNewAsync(store, "A-Level Maths"))
            {
                await session.SaveAsAsync(boardPath);
                documentId = session.Document.DocumentId;
                Assert.NotEqual(Guid.Empty, documentId);
                Assert.True(File.Exists(boardPath));

                await session.MutateAsync(rich =>
                {
                    rich.Sections.Clear();
                    var pure = new HavenRichSection { Title = "Pure Mathematics" };
                    var stats = new HavenRichSection { Title = "Statistics" };
                    var mech = new HavenRichSection { Title = "Mechanics" };
                    rich.Sections.Add(pure);
                    rich.Sections.Add(stats);
                    rich.Sections.Add(mech);

                    foreach (var (section, name) in new[] { (pure, "Trigonometry"), (stats, "Differentiation"), (mech, "Integration") })
                    {
                        section.Pages.Clear();
                        section.Pages.Add(new HavenRichPage { Title = name });
                    }
                    pure.Pages.Add(new HavenRichPage { Title = "Functions", Order = 1 });
                });

                // Trigonometry page content: headings, multiline text, formatting, checklist, table, ink.
                await session.MutateAsync(rich =>
                {
                    var trig = rich.Sections[0].Pages[0];
                    trig.Blocks.Clear();
                    trig.Blocks.Add(new HavenRichBlock
                    {
                        Kind = HavenRichBlockKind.Heading,
                        PlainText = "Trigonometry identities",
                        Runs = [new HavenRichTextRun { Text = "Trigonometry identities", Bold = true }]
                    });
                    trig.Blocks.Add(new HavenRichBlock
                    {
                        Kind = HavenRichBlockKind.Paragraph,
                        PlainText = "sin²θ + cos²θ = 1\ntanθ = sinθ / cosθ\nUse the CAST diagram for signs.",
                        Runs =
                        [
                            new HavenRichTextRun { Text = "sin²θ + cos²θ = 1", Bold = true },
                            new HavenRichTextRun { Text = "\ntanθ = sinθ / cosθ", Italic = true },
                            new HavenRichTextRun { Text = "\nUse the CAST diagram for signs.", Underline = true }
                        ]
                    });
                    trig.Blocks.Add(new HavenRichBlock
                    {
                        Kind = HavenRichBlockKind.Checklist,
                        Items =
                        [
                            new HavenRichListItem { Text = "Learn exact values", Checked = true },
                            new HavenRichListItem { Text = "Practise R-formulae", Checked = false }
                        ]
                    });
                    var table = new HavenRichBlock { Kind = HavenRichBlockKind.Table, Table = HavenRichTable.Create(3, 3) };
                    table.Table.Rows[0].Cells[0].Text = "Angle";
                    table.Table.Rows[0].Cells[1].Text = "sin";
                    table.Table.Rows[0].Cells[2].Text = "cos";
                    table.Table.Rows[1].Cells[0].Text = "30°";
                    table.Table.Rows[1].Cells[1].Text = "1/2";
                    table.Table.Rows[1].Cells[2].Text = "√3/2";
                    table.Table.Rows[2].Cells[0].Text = "45°";
                    table.Table.Rows[2].Cells[1].Text = "√2/2";
                    table.Table.Rows[2].Cells[2].Text = "√2/2";
                    trig.Blocks.Add(table);
                    trig.Ink.Add(new HavenRichInkStroke
                    {
                        Points = [new() { X = 10, Y = 10 }, new() { X = 120, Y = 60 }, new() { X = 200, Y = 30 }],
                        Width = 3,
                        Color = "#FFFF0000"
                    });
                    trig.Canvas.Add(new HavenRichCanvasObject { Kind = "Text", Text = "Unit circle sketch", X = 40, Y = 300 });
                });

                await session.SaveAsync();
                Assert.False(session.HasUnsavedChanges);
                Assert.NotNull(session.LastSavedUtc);
                Assert.StartsWith("Saved", session.Status, StringComparison.Ordinal);
            }

            // Dispose-all then reopen FROM THE PHYSICAL FILE and verify everything exactly.
            {
                using var reopenStore = new JsonFileHavenBoardStore(root);
                await using var reopened = await RichBoardSession.OpenAtPathAsync(reopenStore, boardPath);
                Assert.Equal(documentId, reopened.Document.DocumentId);
                var rich = reopened.Rich;
                Assert.Equal("A-Level Maths", rich.Title);
                Assert.Equal(["Pure Mathematics", "Statistics", "Mechanics"], rich.Sections.Select(s => s.Title));
                var trig = rich.Sections[0].Pages[0];
                Assert.Equal("Trigonometry", trig.Title);
                Assert.Equal("Trigonometry identities", trig.Blocks[0].PlainText);
                Assert.True(trig.Blocks[0].Runs[0].Bold);
                Assert.Contains("CAST diagram", trig.Blocks[1].PlainText);
                Assert.True(trig.Blocks[1].Runs[0].Bold);
                Assert.True(trig.Blocks[1].Runs[1].Italic);
                Assert.True(trig.Blocks[1].Runs[2].Underline);
                var checklist = trig.Blocks[2];
                Assert.Equal("Learn exact values", checklist.Items[0].Text);
                Assert.True(checklist.Items[0].Checked);
                Assert.False(checklist.Items[1].Checked);
                Assert.Equal("√3/2", trig.Blocks[3].Table!.Rows[1].Cells[2].Text);
                Assert.Equal("45°", trig.Blocks[3].Table!.Rows[2].Cells[0].Text);
                var stroke = Assert.Single(trig.Ink);
                Assert.Equal(3, stroke.Points.Count);
                Assert.Equal("#FFFF0000", stroke.Color);
                Assert.Equal("Unit circle sketch", Assert.Single(trig.Canvas).Text);

                // Edit after reopen, autosave, dispose, reopen again, verify second edit.
                await reopened.MutateAsync(r => r.Sections[0].Pages[0].Blocks[1].PlainText += "\nSecond-edit note.");
                await reopened.FlushAsync();
                Assert.False(reopened.HasUnsavedChanges);
            }

            {
                using var thirdStore = new JsonFileHavenBoardStore(root);
                await using var third = await RichBoardSession.OpenAtPathAsync(thirdStore, boardPath);
                Assert.Equal(documentId, third.Document.DocumentId);
                Assert.Contains("Second-edit note.", third.Rich.Sections[0].Pages[0].Blocks[1].PlainText);
            }

            // A filesystem copy opens with identical content and the same stable identity.
            var copyPath = Path.Combine(root, "A-Level Maths (copy).9to1board");
            File.Copy(boardPath, copyPath);
            {
                using var copyStore = new JsonFileHavenBoardStore(root);
                await using var copy = await RichBoardSession.OpenAtPathAsync(copyStore, copyPath);
                Assert.Equal(documentId, copy.Document.DocumentId);
                Assert.Equal("A-Level Maths", copy.Rich.Title);
                Assert.Equal(3, copy.Rich.Sections.Count);
            }

            store.Dispose();
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task Rename_title_and_filename_preserve_stable_identity()
    {
        await WithStoreAsync(async (store, root) =>
        {
            var path = Path.Combine(root, "notes.9to1board");
            await using (var session = await RichBoardSession.CreateNewAsync(store, "Before"))
            {
                await session.SaveAsAsync(path);
                await session.MutateAsync(r => r.Title = "A-Level Law");
                await session.SaveAsync();
            }

            var renamed = Path.Combine(root, "A-Level Law.9to1board");
            File.Move(path, renamed);

            using var reopenStore = new JsonFileHavenBoardStore(root);
            await using var reopened = await RichBoardSession.OpenAtPathAsync(reopenStore, renamed);
            var firstId = reopened.Document.DocumentId;
            Assert.Equal("A-Level Law", reopened.Rich.Title);

            await reopened.MutateAsync(r => r.Title = "Computer Science");
            await reopened.SaveAsync();
            await using var again = await RichBoardSession.OpenAtPathAsync(reopenStore, renamed);
            Assert.Equal(firstId, again.Document.DocumentId);
            Assert.Equal("Computer Science", again.Rich.Title);
        });
    }

    [Fact]
    public async Task Section_and_page_reorder_survive_reopen()
    {
        await WithStoreAsync(async (store, root) =>
        {
            var path = Path.Combine(root, "order.9to1board");
            string sectionId, pageId;
            await using (var session = await RichBoardSession.CreateNewAsync(store, "Order"))
            {
                await session.SaveAsAsync(path);
                await session.MutateAsync(r =>
                {
                    r.Sections.Clear();
                    r.Sections.Add(new HavenRichSection { Title = "One" });
                    r.Sections.Add(new HavenRichSection { Title = "Two" });
                    r.Sections[0].Pages.Clear();
                    r.Sections[0].Pages.Add(new HavenRichPage { Title = "A" });
                    r.Sections[0].Pages.Add(new HavenRichPage { Title = "B" });
                });
                sectionId = session.Rich.Sections[1].Id;
                pageId = session.Rich.Sections[0].Pages[1].Id;
                await session.MutateAsync(r =>
                {
                    HavenRichNotesOps.MoveSection(r, sectionId, 0);
                    HavenRichNotesOps.MovePage(r, pageId, r.Sections[1].Id, 0);
                });
                await session.SaveAsync();
            }

            using var reopenStore = new JsonFileHavenBoardStore(root);
            await using var reopened = await RichBoardSession.OpenAtPathAsync(reopenStore, path);
            Assert.Equal("Two", reopened.Rich.Sections[0].Title);
            Assert.Equal("B", reopened.Rich.Sections[1].Pages[0].Title);
        });
    }

    [Fact]
    public async Task Rapid_consecutive_autosaves_cannot_regress_content()
    {
        await WithStoreAsync(async (store, root) =>
        {
            var path = Path.Combine(root, "rapid.9to1board");
            await using (var session = await RichBoardSession.CreateNewAsync(store, "Rapid"))
            {
                await session.SaveAsAsync(path);
                var pageId = session.Rich.Sections[0].Pages[0].Id;
                for (var i = 0; i < 10; i++)
                {
                    var capture = i;
                    await session.MutateAsync(r =>
                        r.Sections[0].Pages[0].Blocks.Add(HavenRichBlock.Paragraph($"Edit {capture}")));
                }
                await session.FlushAsync();
                Assert.Contains("Edit 9", session.Rich.Sections[0].Pages[0].Blocks.Last().PlainText);
            }

            using var reopenStore = new JsonFileHavenBoardStore(root);
            await using var reopened = await RichBoardSession.OpenAtPathAsync(reopenStore, path);
            Assert.Contains(reopened.Rich.Sections[0].Pages[0].Blocks, b => b.PlainText == "Edit 9");
            Assert.Equal(11, reopened.Rich.Sections[0].Pages[0].Blocks.Count);
        });
    }

    [Fact]
    public async Task Invalid_save_is_rejected_and_previous_valid_file_survives()
    {
        await WithStoreAsync(async (store, root) =>
        {
            var path = Path.Combine(root, "guard.9to1board");
            await using (var session = await RichBoardSession.CreateNewAsync(store, "Known good"))
            {
                await session.SaveAsAsync(path);
                await session.SaveAsync();
            }
            var goodBytes = await File.ReadAllBytesAsync(path);

            var corrupt = new HavenBoardDocument(
                HavenBoardDocument.FormatIdentity, HavenBoardDocument.CurrentSchemaVersion,
                Guid.NewGuid(), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow,
                Snapshot: null, RichNotes: new HavenRichNotes { Title = "" });
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                store.SaveDocumentAtPathAsync(corrupt, path));

            Assert.Equal(goodBytes, await File.ReadAllBytesAsync(path));
            await using var reopened = await RichBoardSession.OpenAtPathAsync(store, path);
            Assert.Equal("Known good", reopened.Rich.Title);
        });
    }

    [Fact]
    public async Task Corrupt_primary_recovers_backup_and_preserves_corrupt_bytes()
    {
        await WithStoreAsync(async (store, root) =>
        {
            var path = Path.Combine(root, "recover.9to1board");
            await using (var session = await RichBoardSession.CreateNewAsync(store, "Backup"))
            {
                await session.SaveAsAsync(path);
                await session.SaveAsync();
                await session.MutateAsync(r => r.Title = "Primary");
                await session.SaveAsync();
            }

            const string corrupt = "{ not valid json";
            await File.WriteAllTextAsync(path, corrupt);

            await using var recovered = await RichBoardSession.OpenAtPathAsync(store, path);
            Assert.Equal("Backup", recovered.Rich.Title);
            Assert.Equal(HavenBoardLoadDisposition.RecoveredFromBackup, store.LastLoadDisposition);
            Assert.Equal(corrupt, await File.ReadAllTextAsync(path));
        });
    }

    [Fact]
    public async Task Unsupported_future_schema_fails_loudly_not_blank()
    {
        await WithStoreAsync(async (store, root) =>
        {
            var path = Path.Combine(root, "future.9to1board");
            var future = new
            {
                format = HavenBoardDocument.FormatIdentity,
                schemaVersion = HavenBoardDocument.CurrentSchemaVersion + 1,
                documentId = Guid.NewGuid(),
                createdUtc = DateTimeOffset.UtcNow,
                modifiedUtc = DateTimeOffset.UtcNow,
                snapshot = (object?)null,
                richNotes = (object?)null
            };
            await File.WriteAllTextAsync(path, JsonSerializer.Serialize(future));

            var error = await Assert.ThrowsAsync<UnsupportedHavenBoardDocumentVersionException>(() =>
                RichBoardSession.OpenAtPathAsync(store, path));
            Assert.Equal(HavenBoardDocument.CurrentSchemaVersion + 1, error.ActualVersion);
        });
    }

    [Fact]
    public async Task Schema_v1_file_migrates_with_snapshot_intact_and_original_preserved_until_save()
    {
        await WithStoreAsync(async (store, root) =>
        {
            var path = Path.Combine(root, "v1board.9to1board");
            var snapshot = HavenBoardSnapshot.CreateDefault() with { Title = "V1 board" };
            var v1 = new
            {
                format = HavenBoardDocument.FormatIdentity,
                schemaVersion = 1,
                documentId = Guid.NewGuid(),
                createdUtc = DateTimeOffset.UtcNow,
                modifiedUtc = DateTimeOffset.UtcNow,
                snapshot
            };
            var originalBytes = JsonSerializer.Serialize(v1, Json);
            await File.WriteAllTextAsync(path, originalBytes);

            HavenBoardDocument? loaded;
            await using (var session = await RichBoardSession.OpenAtPathAsync(store, path))
            {
                Assert.Equal(HavenBoardLoadDisposition.MigratedSchema, store.LastLoadDisposition);
                Assert.Equal("V1 board", session.Rich.Title);
                Assert.NotEmpty(session.Rich.Sections);
                loaded = session.Document;
                Assert.NotNull(loaded.Snapshot);
                Assert.Equal("V1 board", loaded.Snapshot!.Title);
                // Original bytes untouched by a read-only open.
                Assert.Equal(originalBytes, await File.ReadAllTextAsync(path));
                await session.SaveAsync();
            }

            using var json = JsonDocument.Parse(await File.ReadAllTextAsync(path));
            Assert.Equal(2, json.RootElement.GetProperty("schemaVersion").GetInt32());
            Assert.NotNull(loaded!.RichNotes);

            await using var reopened = await RichBoardSession.OpenAtPathAsync(store, path);
            Assert.Equal("V1 board", reopened.Rich.Title);
            Assert.Equal("V1 board", reopened.Document.Snapshot!.Title);
        });
    }

    [Fact]
    public async Task Task_facet_save_preserves_rich_facet_and_vice_versa()
    {
        await WithStoreAsync(async (store, _) =>
        {
            var snapshot = HavenBoardSnapshot.CreateDefault() with { Id = "facet-board" };
            await store.SaveAsync(snapshot);
            var taskOnly = await store.LoadDocumentAsync("facet-board");
            Assert.NotNull(taskOnly);
            Assert.NotNull(taskOnly!.Snapshot);
            Assert.Null(taskOnly.RichNotes);

            var withRich = taskOnly with { RichNotes = HavenRichNotes.Create("Facets") };
            await store.SaveDocumentAsync(withRich);

            var reloaded = await store.LoadDocumentAsync("facet-board");
            Assert.NotNull(reloaded);
            Assert.Equal("Facets", reloaded!.RichNotes!.Title);
            Assert.Equal(snapshot.Groups.Count, reloaded.Snapshot!.Groups.Count);

            await store.SaveAsync(reloaded.Snapshot with { Title = "Task edited" });
            var final = await store.LoadDocumentAsync("facet-board");
            Assert.Equal("Task edited", final!.Snapshot!.Title);
            Assert.Equal("Facets", final.RichNotes!.Title);
        });
    }

    [Fact]
    public async Task Embedded_attachment_survives_reopen_with_identical_bytes()
    {
        await WithStoreAsync(async (store, root) =>
        {
            var source = Path.Combine(root, "source.txt");
            var payload = Encoding.UTF8.GetBytes("revision attachment payload");
            await File.WriteAllBytesAsync(source, payload);
            var path = Path.Combine(root, "attached.9to1board");

            await using (var session = await RichBoardSession.CreateNewAsync(store, "Attached"))
            {
                await session.SaveAsAsync(path);
                var attachment = await session.ImportAttachmentAsync(source);
                var pageId = session.Rich.Sections[0].Pages[0].Id;
                string blockId = "";
                await session.MutateAsync(r =>
                {
                    var block = HavenRichNotesOps.AddBlock(r, pageId, HavenRichBlockKind.Paragraph, "See attached");
                    HavenRichNotesOps.AttachToBlock(r, pageId, block.Id, attachment);
                    blockId = block.Id;
                });
                await session.SaveAsync();
                Assert.Equal(blockId, session.Rich.Sections[0].Pages[0].Blocks.Last().Id);
            }

            await using var reopened = await RichBoardSession.OpenAtPathAsync(store, path);
            var reopenedAttachment = reopened.Rich.Sections[0].Pages[0].Blocks.Last().Attachment;
            Assert.NotNull(reopenedAttachment);
            Assert.Equal("source.txt", reopenedAttachment!.DisplayName);
            Assert.Equal(payload, Convert.FromBase64String(reopenedAttachment.DataBase64!));
        });
    }

    private static async Task WithStoreAsync(Func<JsonFileHavenBoardStore, string, Task> test)
    {
        var root = Path.Combine(Path.GetTempPath(), "cakeos-rich-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            using var store = new JsonFileHavenBoardStore(root);
            await test(store, root);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    private static string TempDirectory()
    {
        var root = Path.Combine(Path.GetTempPath(), "cakeos-rich-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static void DeleteDirectory(string root)
    {
        try { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
