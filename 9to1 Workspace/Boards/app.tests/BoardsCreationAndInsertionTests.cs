using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Interactivity;
using CakeOS.Apps.Boards.Contract;
using Xunit;

namespace CakeOS.Apps.Boards.App.Tests;

[Collection(BoardSessionTestCollection.Name)]
public sealed class BoardsCreationAndInsertionTests
{
    [Fact]
    public async Task New_menu_routes_notes_page_section_and_board_and_disables_canvas_honestly()
    {
        await using var session = new InMemoryRichBoardSession();
        await session.OpenAsync("memory://boards/new-menu-actions");
        var viewModel = new BoardsViewModel(session);
        var boardRequests = 0;
        viewModel.NewBoardRequested += () => boardRequests++;

        var result = TestUiThread.Run(() =>
        {
            var menu = BoardsCreationMenu.Create(viewModel);
            var items = menu.Items.OfType<MenuItem>().ToArray();
            MenuItem Find(string header) => Assert.Single(items, item => item.Header?.ToString() == header);

            var notesPage = Find("Notes Page");
            var board = Find("Board");
            var canvas = Find("Canvas (coming later)");
            var section = Find("Section");
            var initialPages = viewModel.Session.Document.Sections[0].Pages.Count;
            var initialSections = viewModel.Session.Document.Sections.Count;

            notesPage.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            board.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            section.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));

            return (
                NotesPageName: AutomationProperties.GetName(notesPage),
                BoardName: AutomationProperties.GetName(board),
                CanvasName: AutomationProperties.GetName(canvas),
                CanvasEnabled: canvas.IsEnabled,
                PageCount: viewModel.Session.Document.Sections[0].Pages.Count,
                InitialPages: initialPages,
                SectionCount: viewModel.Session.Document.Sections.Count,
                InitialSections: initialSections);
        });

        Assert.Equal("Create a notes page", result.NotesPageName);
        Assert.Equal("Create a separate board file", result.BoardName);
        Assert.Equal("Canvas unavailable, coming later", result.CanvasName);
        Assert.False(result.CanvasEnabled);
        Assert.Equal(result.InitialPages + 1, result.PageCount);
        Assert.Equal(result.InitialSections + 1, result.SectionCount);
        Assert.Equal(1, boardRequests);
    }

    [Fact]
    public async Task New_board_dispatch_requests_a_separate_document_without_mutating_current_board()
    {
        await using var session = new InMemoryRichBoardSession();
        await session.OpenAsync("memory://boards/new-board-safety");
        var viewModel = new BoardsViewModel(session);
        var before = session.Snapshot;
        var requests = 0;
        viewModel.NewBoardRequested += () => requests++;

        await viewModel.DispatchAsync("NewBoard", null);

        Assert.Equal(1, requests);
        Assert.Equal(before, session.Snapshot);
    }

    [Fact]
    public async Task Formatting_an_empty_new_page_creates_an_editable_paragraph_without_stale_selection()
    {
        await using var session = new InMemoryRichBoardSession();
        await session.OpenAsync("memory://boards/empty-page-formatting");
        var viewModel = new BoardsViewModel(session);
        var firstPage = session.Document.Sections[0].Pages[0];

        await viewModel.DispatchAsync("AddPage", null);
        var addedPage = session.Document.Sections[0].Pages[1];

        Assert.Empty(addedPage.Blocks);
        Assert.NotEqual(firstPage.Blocks[0].Id, viewModel.SelectedBlockId);
        Assert.Equal(string.Empty, viewModel.SelectedBlockId);
        Assert.Equal("No block", viewModel.Get("SelectedBlockInfo"));

        viewModel.SelectPage(session.Document.Sections[0].Id, firstPage.Id);
        viewModel.SelectPage(session.Document.Sections[0].Id, addedPage.Id);
        Assert.Equal(string.Empty, viewModel.SelectedBlockId);

        await viewModel.DispatchAsync("ToggleBold", null);

        var paragraph = Assert.Single(addedPage.Blocks);
        Assert.Equal("paragraph", paragraph.Kind);
        Assert.Equal(string.Empty, paragraph.Text);
        Assert.True(paragraph.Bold);
        Assert.Equal(paragraph.Id, viewModel.SelectedBlockId);
        Assert.Equal("Unsaved changes", viewModel.Get("SaveStateText"));
    }

    [Fact]
    public async Task Creating_distinct_board_preserves_current_file_and_refuses_existing_target()
    {
        var root = TempDirectory();
        try
        {
            var sourcePath = Path.Combine(root, "current.9to1board");
            var newPath = Path.Combine(root, "separate.9to1board");
            using var store = new JsonFileHavenBoardStore(root);
            await using (var current = await ContractSessionAdapter.OpenAsync(store, sourcePath))
            {
                current.Document.Title = "Saved current board";
                current.Document.Sections[0].Pages[0].Blocks[0].Text = "Keep this note";
                current.MarkDirty();
                await current.SaveAsync();
            }

            var originalBytes = await File.ReadAllBytesAsync(sourcePath);
            await using (var created = await ContractSessionAdapter.CreateNewAtPathAsync(store, newPath))
            {
                Assert.Equal(Path.GetFullPath(newPath), created.FilePath);
                Assert.Equal("separate", created.Document.Title);
            }

            await Assert.ThrowsAsync<IOException>(async () =>
                await ContractSessionAdapter.CreateNewAtPathAsync(store, sourcePath));
            Assert.Equal(originalBytes, await File.ReadAllBytesAsync(sourcePath));
            Assert.True(File.Exists(newPath));

            await using var reopened = await ContractSessionAdapter.OpenAsync(store, sourcePath);
            Assert.Equal("Saved current board", reopened.Document.Title);
            Assert.Equal("Keep this note", reopened.Document.Sections[0].Pages[0].Blocks[0].Text);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task Inline_insert_after_clicked_block_survives_save_and_reopen()
    {
        var root = TempDirectory();
        try
        {
            var path = Path.Combine(root, "insert-order.9to1board");
            using var store = new JsonFileHavenBoardStore(root);
            await using (var adapter = await ContractSessionAdapter.OpenAsync(store, path))
            {
                var page = adapter.Document.Sections[0].Pages[0];
                await adapter.DeleteViewBlockAsync(page.Id, page.Blocks[0].Id);
                page = adapter.Document.Sections[0].Pages[0];
                page.Blocks.Add(new RichBoardBlock { Id = "block-a", Kind = "paragraph", Text = "A" });
                page.Blocks.Add(new RichBoardBlock { Id = "block-b", Kind = "paragraph", Text = "B" });
                adapter.MarkDirty();
                await adapter.SaveAsync();

                var viewModel = new BoardsViewModel(adapter);
                viewModel.FocusBlock("block-b");
                await viewModel.InsertKindAsync("paragraph", "block-a");
                var inserted = Assert.Single(viewModel.PageBlocks, block => block.Id != "block-a" && block.Id != "block-b");
                inserted.Text = "X";
                adapter.MarkDirty();
                await adapter.SaveAsync();

                Assert.Equal(new[] { "A", "X", "B" }, viewModel.PageBlocks.Select(block => block.Text));
            }

            await using var reopened = await ContractSessionAdapter.OpenAsync(store, path);
            Assert.Equal(
                new[] { "A", "X", "B" },
                reopened.Document.Sections[0].Pages[0].Blocks.Select(block => block.Text));
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task Freeform_insertion_creates_only_a_page_object_and_survives_reopen()
    {
        var root = TempDirectory();
        try
        {
            var path = Path.Combine(root, "freeform-only.9to1board");
            using var store = new JsonFileHavenBoardStore(root);
            string pageId;
            string[] originalBlockIds;
            await using (var adapter = await ContractSessionAdapter.OpenAsync(store, path))
            {
                var viewModel = new BoardsViewModel(adapter);
                pageId = viewModel.SelectedPageId;
                originalBlockIds = viewModel.PageBlocks.Select(block => block.Id).ToArray();

                await viewModel.InsertKindAsync("freeform");

                Assert.Equal(originalBlockIds, viewModel.PageBlocks.Select(block => block.Id));
                Assert.Equal("New note", Assert.Single(await viewModel.GetCanvasObjectsAsync()).Text);
                await adapter.SaveAsync();
            }

            await using var reopened = await ContractSessionAdapter.OpenAsync(store, path);
            Assert.Equal(originalBlockIds, reopened.Document.Sections[0].Pages[0].Blocks.Select(block => block.Id));
            Assert.Equal("New note", Assert.Single(await reopened.GetCanvasObjectsAsync(pageId)).Text);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task Cancelled_or_unavailable_attachment_picker_does_not_insert_a_paragraph()
    {
        await using var session = new InMemoryRichBoardSession();
        await session.OpenAsync("memory://boards/cancel-attachment");
        var viewModel = new BoardsViewModel(session);
        var originalBlockIds = viewModel.PageBlocks.Select(block => block.Id).ToArray();

        await viewModel.InsertKindAsync("attachment");
        Assert.Equal("Attachment pick needs the desktop host", viewModel.Get("StatusText"));
        Assert.Equal(originalBlockIds, viewModel.PageBlocks.Select(block => block.Id));

        var pickerCalls = 0;
        viewModel.PickFileAsync = kind =>
        {
            Assert.Equal("attach", kind);
            pickerCalls++;
            return Task.FromResult<string?>(null);
        };
        await viewModel.InsertKindAsync("attachment");

        Assert.Equal(1, pickerCalls);
        Assert.Equal(originalBlockIds, viewModel.PageBlocks.Select(block => block.Id));
    }

    [Fact]
    public async Task Attachment_insertion_uses_one_picker_and_persists_the_selected_file()
    {
        var root = TempDirectory();
        try
        {
            var attachmentPath = Path.Combine(root, "source.txt");
            await File.WriteAllTextAsync(attachmentPath, "Portable attachment");
            var boardPath = Path.Combine(root, "attachment-insertion.9to1board");
            using var store = new JsonFileHavenBoardStore(root);
            await using (var adapter = await ContractSessionAdapter.OpenAsync(store, boardPath))
            {
                var viewModel = new BoardsViewModel(adapter);
                var existingCount = viewModel.PageBlocks.Count;
                var pickerCalls = 0;
                viewModel.PickFileAsync = kind =>
                {
                    Assert.Equal("attach", kind);
                    pickerCalls++;
                    return Task.FromResult<string?>(attachmentPath);
                };

                await viewModel.InsertKindAsync("attachment");

                Assert.Equal(1, pickerCalls);
                Assert.Equal(existingCount + 1, viewModel.PageBlocks.Count);
                Assert.Equal("source.txt", Assert.Single(viewModel.PageBlocks, block => block.AttachmentId is not null).AttachmentName);
                await adapter.SaveAsync();
            }

            await using var reopened = await ContractSessionAdapter.OpenAsync(store, boardPath);
            var attached = Assert.Single(reopened.Document.Sections[0].Pages[0].Blocks,
                block => block.AttachmentId is not null);
            Assert.Equal("source.txt", attached.AttachmentName);
            var resolution = await reopened.ResolveAttachmentAsync(attached.AttachmentId!);
            Assert.Equal(HavenAttachmentStatus.Available, resolution.Status);
            Assert.Equal("Portable attachment", System.Text.Encoding.UTF8.GetString(Assert.IsType<byte[]>(resolution.EmbeddedBytes)));
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task Attachment_picker_does_not_insert_on_a_different_page_after_navigation()
    {
        var root = TempDirectory();
        try
        {
            var attachmentPath = Path.Combine(root, "selected.txt");
            await File.WriteAllTextAsync(attachmentPath, "Selected before navigation");
            await using var session = new InMemoryRichBoardSession();
            await session.OpenAsync("memory://boards/attachment-page-change");
            var viewModel = new BoardsViewModel(session);
            var firstPage = session.Document.Sections[0].Pages[0];
            var firstBlockIds = firstPage.Blocks.Select(block => block.Id).ToArray();
            viewModel.PickFileAsync = async kind =>
            {
                Assert.Equal("attach", kind);
                await viewModel.DispatchAsync("AddPage", null);
                return attachmentPath;
            };

            await viewModel.InsertKindAsync("attachment");

            Assert.Equal(firstBlockIds, firstPage.Blocks.Select(block => block.Id));
            Assert.Empty(session.Document.Sections[0].Pages[1].Blocks);
            Assert.Contains("page changed", viewModel.Get("StatusText")?.ToString(), StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    private static string TempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "boards-create-insert-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
