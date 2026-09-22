// Integration tests: ContractSessionAdapter merge between the CUI working
// model and the durable contract session. Proves the executable app path
// persists through physical .9to1board files without dropping data.

using CakeOS.Apps.Boards.Contract;
using Xunit;

namespace CakeOS.Apps.Boards.App.Tests;

public sealed class ContractAdapterTests
{
    [Fact]
    public async Task Adapter_edit_save_dispose_reopen_preserves_notes_ink_and_table()
    {
        var root = TempDirectory();
        try
        {
            var path = Path.Combine(root, "adapter.9to1board");
            using var store = new JsonFileHavenBoardStore(root);

            await using (var adapter = await ContractSessionAdapter.OpenAsync(store, path))
            {
                adapter.Document.Title = "Adapter board";
                var page = adapter.Document.Sections[0].Pages[0];
                var para = page.Blocks.First(b => b.Kind == "paragraph");
                para.Text = "Adapter line one\nAdapter line two";
                para.Bold = true;
                page.Blocks.Add(new RichBoardBlock { Kind = "checklist", Text = "Adapter checklist", IsChecked = true });
                adapter.MarkDirty();
                await adapter.CommitInkStrokeAsync([(5, 5), (50, 25), (90, 10)]);
                await adapter.SaveAsync();
                Assert.StartsWith("Saved", adapter.Status, StringComparison.Ordinal);
            }

            await using (var reopened = await ContractSessionAdapter.OpenAsync(store, path))
            {
                Assert.Equal("Adapter board", reopened.Document.Title);
                var para = reopened.Document.Sections[0].Pages[0].Blocks.First(b => b.Kind == "paragraph");
                Assert.Equal("Adapter line one\nAdapter line two", para.Text);
                Assert.True(para.Bold);
                var check = reopened.Document.Sections[0].Pages[0].Blocks.First(b => b.Kind == "checklist");
                Assert.Equal("Adapter checklist", check.Text);
                Assert.True(check.IsChecked);
                var ink = await reopened.GetInkStrokesAsync();
                Assert.Single(ink);
            }

            // Raw contract read proves the bytes hold real ink points, not just a counter.
            var raw = await store.LoadDocumentAtPathAsync(path);
            Assert.NotNull(raw?.RichNotes);
            var stroke = Assert.Single(raw!.RichNotes!.Sections[0].Pages[0].Ink);
            Assert.Equal(3, stroke.Points.Count);
            Assert.Equal(90, stroke.Points[2].X);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task Adapter_merge_preserves_contract_only_state()
    {
        var root = TempDirectory();
        try
        {
            var path = Path.Combine(root, "preserve.9to1board");
            using var store = new JsonFileHavenBoardStore(root);

            // Seed contract-only richness the editor UI cannot express.
            await using (var seed = await RichBoardSession.CreateNewAsync(store, "Seeded"))
            {
                await seed.SaveAsAsync(path);
                var pageId = seed.Rich.Sections[0].Pages[0].Id;
                await seed.MutateAsync(rich =>
                {
                    var page = rich.Sections[0].Pages[0];
                    page.Blocks.Clear();
                    var styled = HavenRichBlock.Paragraph("ignored");
                    styled.Runs =
                    [
                        new HavenRichTextRun { Text = "plain-", Bold = false },
                        new HavenRichTextRun { Text = "bold", Bold = true }
                    ];
                    page.Blocks.Add(styled);
                    var table = new HavenRichBlock { Kind = HavenRichBlockKind.Table, Table = HavenRichTable.Create(3, 3) };
                    table.Table.Rows[2].Cells[2].Text = "extra";
                    page.Blocks.Add(table);
                    page.Canvas.Add(new HavenRichCanvasObject { Kind = "Text", Text = "canvas-keep", X = 10, Y = 10 });
                    var attachBlock = HavenRichBlock.Paragraph("with file");
                    attachBlock.Attachment = new HavenRichAttachmentRef
                    {
                        DisplayName = "keep.txt",
                        DataBase64 = Convert.ToBase64String("keep"u8.ToArray())
                    };
                    page.Blocks.Add(attachBlock);
                });
                await seed.SaveAsync();
            }

            // Edit only the title and plain text through the adapter working model.
            await using (var adapter = await ContractSessionAdapter.OpenAsync(store, path))
            {
                adapter.Document.Title = "Retitled";
                var para = adapter.Document.Sections[0].Pages[0].Blocks.First(b => b.Kind == "paragraph");
                para.Text = "plain-bold";
                adapter.MarkDirty();
                await adapter.SaveAsync();
            }

            var raw = await store.LoadDocumentAtPathAsync(path);
            Assert.NotNull(raw?.RichNotes);
            var page = raw!.RichNotes!.Sections[0].Pages[0];
            Assert.Equal("Retitled", raw.RichNotes.Title);
            var styled = page.Blocks.First(b => b.Kind == HavenRichBlockKind.Paragraph);
            Assert.Equal("plain-bold", styled.PlainText);
            Assert.Equal(2, styled.Runs.Count);
            Assert.True(styled.Runs[1].Bold);
            Assert.Equal("extra", page.Blocks
                .First(b => b.Kind == HavenRichBlockKind.Table).Table!.Rows[2].Cells[2].Text);
            Assert.Equal("canvas-keep", Assert.Single(page.Canvas).Text);
            Assert.Equal("keep.txt", page.Blocks
                .First(b => b.Attachment is not null).Attachment!.DisplayName);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task Adapter_structural_adds_create_stable_contract_entities()
    {
        var root = TempDirectory();
        try
        {
            var path = Path.Combine(root, "structural.9to1board");
            using var store = new JsonFileHavenBoardStore(root);

            string sectionId, pageId;
            await using (var adapter = await ContractSessionAdapter.OpenAsync(store, path))
            {
                var section = new RichBoardSection { Title = "Added section" };
                section.Pages.Add(new RichBoardPage { Title = "Added page" });
                adapter.Document.Sections.Add(section);
                sectionId = section.Id;
                pageId = section.Pages[0].Id;
                adapter.MarkDirty();
                await adapter.SaveAsync();
            }

            await using var reopened = await ContractSessionAdapter.OpenAsync(store, path);
            Assert.Contains(reopened.Document.Sections, s => s.Id == sectionId && s.Title == "Added section");
            var added = reopened.Document.Sections.First(s => s.Id == sectionId);
            Assert.Contains(added.Pages, p => p.Id == pageId && p.Title == "Added page");
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task Adapter_eraser_hit_test_removes_only_nearby_strokes()
    {
        var root = TempDirectory();
        try
        {
            var path = Path.Combine(root, "eraser.9to1board");
            using var store = new JsonFileHavenBoardStore(root);

            await using (var adapter = await ContractSessionAdapter.OpenAsync(store, path))
            {
                await adapter.CommitInkStrokeAsync([(10, 10), (20, 20)]);
                await adapter.CommitInkStrokeAsync([(400, 400), (410, 410)]);
                Assert.Equal(0, await adapter.EraseInkAtCurrentPageAsync(200, 200, 5));
                Assert.Equal(1, await adapter.EraseInkAtCurrentPageAsync(12, 12, 12));
                await adapter.SaveAsync();
            }

            await using var reopened = await ContractSessionAdapter.OpenAsync(store, path);
            var ink = await reopened.GetInkStrokesAsync();
            Assert.Single(ink);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task InMemory_dispose_flushes_pending_edits_without_save_call()
    {
        InMemoryRichBoardSession.ClearStore();
        const string path = "memory://boards/dispose-flush-test";
        await using (var session = new InMemoryRichBoardSession())
        {
            await session.OpenAsync(path);
            session.Document.Title = "Flushed on close";
            session.MarkDirty();
            // No explicit SaveAsync: DisposeAsync must still persist.
        }

        await using var reopened = new InMemoryRichBoardSession();
        await reopened.OpenAsync(path);
        Assert.Equal("Flushed on close", reopened.Document.Title);
    }

    [Fact]
    public async Task Adapter_normalizes_extension_on_create_and_prefers_sibling_on_open()
    {
        var root = TempDirectory();
        try
        {
            using var store = new JsonFileHavenBoardStore(root);
            var bare = Path.Combine(root, "My Board");

            await using (var created = await ContractSessionAdapter.OpenAsync(store, bare))
            {
                Assert.Equal(bare + ".9to1board", created.FilePath);
                await created.SaveAsync();
            }
            Assert.True(File.Exists(bare + ".9to1board"));
            Assert.False(File.Exists(bare));

            await using var reopened = await ContractSessionAdapter.OpenAsync(store, bare);
            Assert.Equal(bare + ".9to1board", reopened.FilePath);
            Assert.Equal("My Board", reopened.Document.Title);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task Adapter_canvas_drag_move_persists_across_reopen()
    {
        var root = TempDirectory();
        try
        {
            var path = Path.Combine(root, "drag.9to1board");
            using var store = new JsonFileHavenBoardStore(root);

            string boxId;
            await using (var adapter = await ContractSessionAdapter.OpenAsync(store, path))
            {
                boxId = await adapter.AddCanvasObjectAsync("Text", "Drag me", 10, 10);
                // One committed move per drag gesture, as the pointer handler does on release.
                Assert.True(await adapter.MoveCanvasObjectAsync(boxId, 300, 220));
                await adapter.SaveAsync();
            }

            await using var reopened = await ContractSessionAdapter.OpenAsync(store, path);
            var boxes = await reopened.GetCanvasObjectsAsync();
            var box = Assert.Single(boxes);
            Assert.Equal(boxId, box.Id);
            Assert.Equal(300, box.X);
            Assert.Equal(220, box.Y);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task Adapter_ink_view_pan_zoom_persists_across_reopen()
    {
        var root = TempDirectory();
        try
        {
            var path = Path.Combine(root, "inkview.9to1board");
            using var store = new JsonFileHavenBoardStore(root);

            await using (var adapter = await ContractSessionAdapter.OpenAsync(store, path))
            {
                var session = Assert.IsType<ContractSessionAdapter>(adapter);
                await session.SetInkViewAsync(120, -40, 2);
                await adapter.SaveAsync();
            }

            var raw = await store.LoadDocumentAtPathAsync(path);
            var view = raw!.RichNotes!.Sections[0].Pages[0].InkView;
            Assert.Equal(120, view.PanX);
            Assert.Equal(-40, view.PanY);
            Assert.Equal(2, view.Zoom);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    private static string TempDirectory()
    {
        var root = Path.Combine(Path.GetTempPath(), "cakeos-adapter-tests", Guid.NewGuid().ToString("N"));
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
