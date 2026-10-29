using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using CakeOS.Apps.Boards.Contract;
using Xunit;

namespace CakeOS.Apps.Boards.App.Tests;

[Collection(BoardSessionTestCollection.Name)]
public sealed class BoardsCreationAndInsertionTests
{
    // The owner's plus-control menu of 29 October: Add Block Above / Add Block Below / Replace Current
    // Block. These drive the real view model against a real in-memory board document and then read the
    // page back, so the assertions are about where the block actually landed.
    [Fact]
    public async Task Plus_menu_destinations_put_the_new_block_exactly_where_the_owner_chose()
    {
        await using var session = new InMemoryRichBoardSession();
        await session.OpenAsync("memory://boards/plus-destinations");
        var viewModel = new BoardsViewModel(session);
        // The view model already opens in edit mode (BoardsViewModel._isEditMode = true), so no toggle
        // is needed - dispatching ToggleEditView here would switch editing *off*.
        Assert.True(TestUiThread.Run(() => viewModel.IsEditMode), "the board must be in edit mode for block edits");
        var middle = TestUiThread.Run(() =>
        {
            var page = session.Document.Sections[0].Pages[0];
            page.Blocks.Clear();
            foreach (var text in new[] { "first", "middle", "last" })
                page.Blocks.Add(new RichBoardBlock { Kind = "paragraph", Text = text });
            return page.Blocks[1].Id;
        });

        PumpOnUi(TestUiThread.Run(() => viewModel.InsertKindRelativeToAsync("table", middle, "above")));
        Assert.Equal(new[] { "paragraph", "table", "paragraph", "paragraph" }, Kinds(session));

        var first = TestUiThread.Run(() => session.Document.Sections[0].Pages[0].Blocks[0].Id);
        PumpOnUi(TestUiThread.Run(() => viewModel.InsertKindRelativeToAsync("divider", first, "below")));
        Assert.Equal(new[] { "paragraph", "divider", "table", "paragraph", "paragraph" }, Kinds(session));
    }

    [Fact]
    public async Task Replace_current_block_keeps_the_position_and_leaves_only_the_new_block_there()
    {
        await using var session = new InMemoryRichBoardSession();
        await session.OpenAsync("memory://boards/plus-replace");
        var viewModel = new BoardsViewModel(session);
        // The view model already opens in edit mode (BoardsViewModel._isEditMode = true), so no toggle
        // is needed - dispatching ToggleEditView here would switch editing *off*.
        Assert.True(TestUiThread.Run(() => viewModel.IsEditMode), "the board must be in edit mode for block edits");
        var middle = TestUiThread.Run(() =>
        {
            var page = session.Document.Sections[0].Pages[0];
            page.Blocks.Clear();
            foreach (var text in new[] { "first", "middle", "last" })
                page.Blocks.Add(new RichBoardBlock { Kind = "paragraph", Text = text });
            return page.Blocks[1].Id;
        });

        PumpOnUi(TestUiThread.Run(() => viewModel.InsertKindRelativeToAsync("heading", middle, "replace")));

        // Same slot, one block, the replaced one gone: replacing must not grow the page or shift order.
        Assert.Equal(new[] { "paragraph", "heading", "paragraph" }, Kinds(session));
        var ids = TestUiThread.Run(() => session.Document.Sections[0].Pages[0].Blocks.Select(item => item.Id).ToArray());
        Assert.DoesNotContain(middle, ids);
        Assert.Equal(3, ids.Distinct().Count());
    }

    [Fact]
    public async Task Plus_menu_refuses_an_unknown_destination_and_a_vanished_block_without_editing_anything()
    {
        await using var session = new InMemoryRichBoardSession();
        await session.OpenAsync("memory://boards/plus-refusals");
        var viewModel = new BoardsViewModel(session);
        // The view model already opens in edit mode (BoardsViewModel._isEditMode = true), so no toggle
        // is needed - dispatching ToggleEditView here would switch editing *off*.
        Assert.True(TestUiThread.Run(() => viewModel.IsEditMode), "the board must be in edit mode for block edits");
        TestUiThread.Run(() =>
        {
            var page = session.Document.Sections[0].Pages[0];
            page.Blocks.Clear();
            page.Blocks.Add(new RichBoardBlock { Kind = "paragraph", Text = "only" });
            return true;
        });

        PumpOnUi(TestUiThread.Run(() => viewModel.InsertKindRelativeToAsync("table", "missing-block", "above")));
        Assert.Equal(new[] { "paragraph" }, Kinds(session));
        Assert.Contains("no longer on this page", StatusText(viewModel), StringComparison.Ordinal);

        // Read the id at top level. TestUiThread.Run is a single-threaded queue, so a Run nested
        // inside another Run's argument list would wait on the pump that is already pumping.
        var only = TestUiThread.Run(() => session.Document.Sections[0].Pages[0].Blocks[0].Id);
        PumpOnUi(TestUiThread.Run(() => viewModel.InsertKindRelativeToAsync("table", only, "sideways")));
        Assert.Equal(new[] { "paragraph" }, Kinds(session));
        Assert.Contains("Add Block Above, Add Block Below or Replace Current Block", StatusText(viewModel), StringComparison.Ordinal);
    }

    private static string[] Kinds(InMemoryRichBoardSession session) => TestUiThread.Run(() =>
        session.Document.Sections[0].Pages[0].Blocks.Select(block => block.Kind).ToArray());

    private static string StatusText(BoardsViewModel viewModel) =>
        TestUiThread.Run(() => viewModel.Get("StatusText")?.ToString() ?? string.Empty);

    /// <summary>Pumps a Boards UI action to completion on the app dispatcher. Accepts ValueTask too,
    /// because the view model's dispatch path returns one and a bare Task parameter will not bind.</summary>
    private static void PumpOnUi(object awaitable) => TestUiThread.Run(() =>
    {
        var task = awaitable is Task concrete ? concrete : ((ValueTask)awaitable).AsTask();
        PumpTask(task);
    });

    private static void PumpTask(Task task)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (!task.IsCompleted && DateTime.UtcNow < deadline)
        {
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(1);
        }
        Dispatcher.UIThread.RunJobs();
        if (!task.IsCompleted) throw new TimeoutException("The Boards insert action did not complete on its dispatcher.");
        if (task.IsFaulted) throw task.Exception!.GetBaseException();
    }

    [Fact]
    public async Task New_menu_routes_notes_page_section_and_board_and_disables_owner_pages_without_a_host()
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
            var attachCanvas = Find("Attach existing Canvas…");
            var createCanvas = Find("New Canvas…");
            var section = Find("Section");
            var initialPages = viewModel.Session.Document.Sections[0].Pages.Count;
            var initialSections = viewModel.Session.Document.Sections.Count;

            notesPage.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            board.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            section.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));

            return (
                NotesPageName: AutomationProperties.GetName(notesPage),
                BoardName: AutomationProperties.GetName(board),
                AttachCanvasName: AutomationProperties.GetName(attachCanvas),
                CreateCanvasName: AutomationProperties.GetName(createCanvas),
                CanvasEnabled: attachCanvas.IsEnabled || createCanvas.IsEnabled,
                PageCount: viewModel.Session.Document.Sections[0].Pages.Count,
                InitialPages: initialPages,
                SectionCount: viewModel.Session.Document.Sections.Count,
                InitialSections: initialSections);
        });

        Assert.Equal("Create a notes page", result.NotesPageName);
        Assert.Equal("Create a separate board file", result.BoardName);
        Assert.Equal("Attach an existing Canvas as a page", result.AttachCanvasName);
        Assert.Equal("Create a Canvas as a page", result.CreateCanvasName);
        Assert.False(result.CanvasEnabled);
        Assert.Equal(result.InitialPages + 1, result.PageCount);
        Assert.Equal(result.InitialSections + 1, result.SectionCount);
        Assert.Equal(1, boardRequests);
    }

    [Fact]
    public async Task Saved_canvas_objects_use_the_native_spatial_layer_without_a_second_viewport()
    {
        await using var session = new InMemoryRichBoardSession();
        await session.OpenAsync("memory://boards/freeform-viewport-bounds");
        var viewModel = new BoardsViewModel(session);
        var dimensions = TestUiThread.Run(() =>
        {
            var host = new StackPanel();
            var layer = new Canvas();
            BlockRenderer.Rebuild(host, viewModel,
            [
                new CanvasBoxView
                {
                    Id = "far-object", Kind = "Text", Text = "Saved offscreen",
                    X = 1480, Y = 1080, Width = 220, Height = 140,
                }
            ], layer);
            var saved = Assert.Single(layer.Children.OfType<Border>());
            Assert.Equal("Saved offscreen", Assert.IsType<TextBlock>(saved.Child).Text);
            Assert.Equal("canvas_far-object", saved.Name);
            Assert.Equal(1480, Canvas.GetLeft(saved));
            Assert.Equal(1080, Canvas.GetTop(saved));
            Assert.Empty(host.GetLogicalDescendants().OfType<ScrollViewer>());
            Assert.DoesNotContain(host.GetLogicalDescendants().OfType<TextBlock>(),
                text => (text.Text ?? "").Contains("Freeform canvas"));
            return (Width: layer.MinWidth, Height: layer.Height);
        });

        Assert.True(dimensions.Width >= 1700);
        Assert.True(dimensions.Height >= 1220);
    }

    [Fact]
    public async Task Empty_native_page_has_no_extra_canvas_controls_or_spatial_extent()
    {
        await using var session = new InMemoryRichBoardSession();
        await session.OpenAsync("memory://boards/empty-native");
        var vm = new BoardsViewModel(session);
        TestUiThread.Run(() =>
        {
            var host = new StackPanel();
            var layer = new Canvas();
            BlockRenderer.Rebuild(host, vm, [], layer);
            Assert.Empty(layer.Children);
            Assert.Equal(0, layer.MinWidth);
            Assert.True(double.IsNaN(layer.Height));
            Assert.DoesNotContain(host.GetLogicalDescendants().OfType<TextBlock>(),
                text => (text.Text ?? "").Contains("Freeform"));
            Assert.DoesNotContain(host.GetLogicalDescendants().OfType<Button>(), button => button.Content?.ToString() == "+ Box");
        });
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
    public async Task Adapter_page_and_section_creation_use_the_shared_undo_redo_history()
    {
        var root = TempDirectory();
        try
        {
            var path = Path.Combine(root, "structure-history.9to1board");
            using var store = new JsonFileHavenBoardStore(root);
            await using var adapter = await ContractSessionAdapter.OpenAsync(store, path);
            var viewModel = new BoardsViewModel(adapter);
            var originalSectionId = adapter.Document.Sections[0].Id;
            var originalPageId = adapter.Document.Sections[0].Pages[0].Id;
            var originalBlockId = adapter.Document.Sections[0].Pages[0].Blocks[0].Id;
            adapter.Document.Sections[0].Pages[0].Blocks[0].Text = "Keep this content";
            adapter.MarkDirty();
            await adapter.SaveAsync();

            await viewModel.DispatchAsync("AddPage", null);
            var addedPageId = viewModel.SelectedPageId;
            Assert.Equal(2, adapter.Document.Sections[0].Pages.Count);
            Assert.Equal(adapter.Document.Sections[0].Pages[1].Blocks[0].Id, viewModel.SelectedBlockId);
            Assert.True(adapter.CanUndo);

            await viewModel.DispatchAsync("Undo", null);
            Assert.Single(adapter.Document.Sections[0].Pages);
            Assert.Equal(originalPageId, adapter.Document.Sections[0].Pages[0].Id);
            Assert.Equal(originalPageId, viewModel.SelectedPageId);
            Assert.Equal(originalBlockId, viewModel.SelectedBlockId);
            Assert.Equal("Keep this content", adapter.Document.Sections[0].Pages[0].Blocks[0].Text);

            await viewModel.DispatchAsync("Redo", null);
            Assert.Equal(new[] { originalPageId, addedPageId }, adapter.Document.Sections[0].Pages.Select(page => page.Id));
            Assert.Contains(adapter.Document.Sections.SelectMany(section => section.Pages), page => page.Id == viewModel.SelectedPageId);

            await viewModel.DispatchAsync("AddSection", null);
            var addedSectionId = viewModel.Session.Document.Sections.Last().Id;
            Assert.Equal(2, adapter.Document.Sections.Count);
            await viewModel.DispatchAsync("Undo", null);
            Assert.Single(adapter.Document.Sections);
            Assert.Equal(originalSectionId, adapter.Document.Sections[0].Id);
            await viewModel.DispatchAsync("Redo", null);
            Assert.Equal(new[] { originalSectionId, addedSectionId }, adapter.Document.Sections.Select(section => section.Id));
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task Adapter_page_and_section_renames_use_the_shared_undo_redo_history_and_persist()
    {
        var root = TempDirectory();
        try
        {
            var path = Path.Combine(root, "rename-history.9to1board");
            using var store = new JsonFileHavenBoardStore(root);
            var sectionId = string.Empty;
            var pageId = string.Empty;
            await using (var adapter = await ContractSessionAdapter.OpenAsync(store, path))
            {
                var viewModel = new BoardsViewModel(adapter);
                sectionId = adapter.Document.Sections[0].Id;
                pageId = adapter.Document.Sections[0].Pages[0].Id;

                await viewModel.RenamePageAsync(sectionId, pageId, "Renamed page");
                Assert.Equal("Renamed page", adapter.Document.Sections[0].Pages[0].Title);
                await viewModel.DispatchAsync("Undo", null);
                Assert.Equal("Start here", adapter.Document.Sections[0].Pages[0].Title);
                Assert.Equal(pageId, viewModel.SelectedPageId);
                await viewModel.DispatchAsync("Redo", null);
                Assert.Equal("Renamed page", adapter.Document.Sections[0].Pages[0].Title);

                await viewModel.RenameSectionAsync(sectionId, "Renamed section");
                Assert.Equal("Renamed section", adapter.Document.Sections[0].Title);
                await viewModel.DispatchAsync("Undo", null);
                Assert.Equal("Notes", adapter.Document.Sections[0].Title);
                await viewModel.DispatchAsync("Redo", null);
                Assert.Equal("Renamed section", adapter.Document.Sections[0].Title);
                await adapter.SaveAsync();
            }

            await using var reopened = await ContractSessionAdapter.OpenAsync(store, path);
            Assert.Equal("Renamed section", reopened.Document.Sections.Single(section => section.Id == sectionId).Title);
            Assert.Equal("Renamed page", reopened.Document.Sections.Single(section => section.Id == sectionId)
                .Pages.Single(page => page.Id == pageId).Title);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [Fact]
    public async Task Adapter_toolbar_block_formatting_undoes_each_action_and_persists_runs()
    {
        var root = TempDirectory();
        try
        {
            var path = Path.Combine(root, "toolbar-history.9to1board");
            using var store = new JsonFileHavenBoardStore(root);
            var pageId = string.Empty;
            var blockId = string.Empty;
            await using (var adapter = await ContractSessionAdapter.OpenAsync(store, path))
            {
                pageId = adapter.Document.Sections[0].Pages[0].Id;
                var block = adapter.Document.Sections[0].Pages[0].Blocks[0];
                blockId = block.Id;
                block.Text = "Formatting survives revisions";
                adapter.MarkDirty();
                await adapter.SaveAsync();

                var viewModel = new BoardsViewModel(adapter);
                await viewModel.DispatchAsync("ToggleBold", null);
                await viewModel.DispatchAsync("ToggleItalic", null);
                var runs = adapter.Document.Sections[0].Pages[0].Blocks[0].Runs;
                Assert.All(runs, run => Assert.True(run.Bold && run.Italic));

                await viewModel.DispatchAsync("Undo", null);
                runs = adapter.Document.Sections[0].Pages[0].Blocks[0].Runs;
                Assert.All(runs, run => Assert.True(run.Bold && !run.Italic));
                await viewModel.DispatchAsync("Undo", null);
                runs = adapter.Document.Sections[0].Pages[0].Blocks[0].Runs;
                Assert.All(runs, run => Assert.False(run.Bold || run.Italic));

                await viewModel.DispatchAsync("Redo", null);
                await viewModel.DispatchAsync("Redo", null);
                runs = adapter.Document.Sections[0].Pages[0].Blocks[0].Runs;
                Assert.All(runs, run => Assert.True(run.Bold && run.Italic));
                Assert.Equal("Formatting survives revisions", adapter.Document.Sections[0].Pages[0].Blocks[0].Text);
                await adapter.SaveAsync();
            }

            await using var reopened = await ContractSessionAdapter.OpenAsync(store, path);
            var persisted = Assert.Single(reopened.Document.Sections.Single(section => section.Pages.Any(page => page.Id == pageId))
                .Pages.Single(page => page.Id == pageId).Blocks, block => block.Id == blockId);
            Assert.Equal("Formatting survives revisions", persisted.Text);
            Assert.All(persisted.Runs, run => Assert.True(run.Bold && run.Italic));
        }
        finally
        {
            DeleteDirectory(root);
        }
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
