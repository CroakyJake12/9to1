using Avalonia.Threading;
using Avalonia;
using Avalonia.Media.Imaging;
using CakeOS.Apps.Boards.Contract;
using Xunit;

namespace CakeOS.Apps.Boards.App.Tests;

[Collection(BoardSessionTestCollection.Name)]
public sealed class BoardsRelativeInsertionTests
{
    [Theory]
    [InlineData("style:missing")]
    [InlineData("image")]
    [InlineData("attachment")]
    [InlineData("freeform")]
    [InlineData("ink")]
    [InlineData("unknown-kind")]
    public Task Failed_replacement_preserves_original_data_revision_and_history(string kind) => Run(async (adapter, vm) =>
    {
        var before = adapter.Snapshot; var version = adapter.DocumentVersion;
        OnUi(() => vm.InsertKindRelativeToAsync(kind, "anchor", "replace"));
        await adapter.SaveAsync();
        Assert.Equal(before, adapter.Snapshot);
        Assert.Equal(version, adapter.DocumentVersion);
        Assert.False(adapter.CanUndo);
    });

    [Theory]
    [InlineData("above")]
    [InlineData("replace")]
    public Task Relative_style_inserts_at_the_chosen_position_in_one_undoable_edit(string placement) => Run(async (adapter, vm) =>
    {
        var before = adapter.Snapshot;
        OnUi(() => vm.InsertKindRelativeToAsync("style:heading-1", "anchor", placement));
        await adapter.SaveAsync();
        var blocks = adapter.Document.Sections[0].Pages[0].Blocks;
        Assert.Equal(placement == "replace" ? 3 : 4, blocks.Count);
        Assert.Equal("heading", blocks[1].Kind);
        Assert.Equal("heading-1", blocks[1].StyleId);
        Assert.NotEqual("anchor", blocks[1].Id);
        Assert.Equal("first", blocks[0].Id); Assert.Equal("last", blocks[^1].Id);
        Assert.True(await adapter.UndoAsync());
        Assert.Equal(before, adapter.Snapshot); Assert.False(adapter.CanUndo);
    });

    [Theory]
    [InlineData("above", false)]
    [InlineData("below", false)]
    [InlineData("replace", false)]
    [InlineData("above", true)]
    [InlineData("below", true)]
    [InlineData("replace", true)]
    public Task List_boundaries_preserve_items_and_insert_a_distinct_kind_with_exact_undo(string placement, bool whole) =>
        Run(async (adapter, vm) =>
        {
            var before = adapter.Snapshot;
            OnUi(() => vm.InsertKindRelativeToAsync("numbered", whole ? "anchor" : "anchor:two", placement));
            var saved = await ReadSaved(adapter);
            var page = saved.Sections[0].Pages[0];
            Assert.Equal("first", page.Blocks[0].Id); Assert.Equal("last", page.Blocks[^1].Id);
            var inserted = Assert.Single(page.Blocks, block => block.Kind == HavenRichBlockKind.NumberedList);
            Assert.Equal("New item", Assert.Single(inserted.Items).Text);
            var originals = page.Blocks.Where(block => block.Kind == HavenRichBlockKind.BulletList).ToArray();
            var retained = originals.SelectMany(block => block.Items).ToArray();
            Assert.Equal(whole && placement == "replace" ? Array.Empty<string>() :
                !whole && placement == "replace" ? new[] { "one", "three" } : new[] { "one", "two", "three" },
                retained.Select(item => item.Id));
            Assert.All(originals, block => Assert.Equal("quote", block.StyleId));
            Assert.All(retained, item => Assert.True(item.Checked));
            var order = page.Blocks.SelectMany(block => block.Kind == HavenRichBlockKind.BulletList
                ? block.Items.Select(item => item.Id) : new[] { block == inserted ? "INSERT" : block.Id }).ToArray();
            var expected = whole
                ? placement switch
                { "above" => new[] { "first", "INSERT", "one", "two", "three", "last" },
                  "below" => new[] { "first", "one", "two", "three", "INSERT", "last" },
                  _ => new[] { "first", "INSERT", "last" } }
                : placement switch
                { "above" => new[] { "first", "one", "INSERT", "two", "three", "last" },
                  "below" => new[] { "first", "one", "two", "INSERT", "three", "last" },
                  _ => new[] { "first", "one", "INSERT", "three", "last" } };
            Assert.Equal(expected, order);
            Assert.True(await adapter.UndoAsync()); Assert.Equal(before, adapter.Snapshot); Assert.False(adapter.CanUndo);
        }, rich =>
        {
            var list = rich.Sections[0].Pages[0].Blocks[1]; list.Kind = HavenRichBlockKind.BulletList; list.StyleId = "quote";
            list.Items = new[] { "one", "two", "three" }.Select(id => new HavenRichListItem
                { Id = id, Text = "Keep " + id, Checked = true }).ToList();
        });

    [Theory]
    [InlineData("accept")]
    [InlineData("large")]
    [InlineData("cancel")]
    [InlineData("missing-file")]
    [InlineData("corrupt-file")]
    [InlineData("page-away-and-back")]
    [InlineData("view")]
    public Task Image_is_prepared_before_atomic_replacement_and_revalidates_its_origin(string outcome) =>
        Run(async (adapter, vm) =>
        {
            var before = adapter.Snapshot; var version = adapter.DocumentVersion;
            var imagePath = Path.Combine(Path.GetDirectoryName(adapter.FilePath!)!, "image.png");
            TestUiThread.Run(() =>
            {
                using var bitmap = new RenderTargetBitmap(new PixelSize(outcome == "large" ? 9001 : 2, 2), new Vector(96, 96));
                bitmap.Save(imagePath);
                if (outcome == "corrupt-file") File.WriteAllText(imagePath, "This is not an image.");
                vm.PickFileAsync = kind =>
                {
                    if (outcome == "page-away-and-back")
                    {
                        var page = vm.SelectedPageId;
                        vm.SelectPage(vm.SelectedSectionId, "other"); vm.SelectPage(vm.SelectedSectionId, page);
                    }
                    if (outcome == "view") _ = vm.DispatchAsync("ToggleEditView", null);
                    return Task.FromResult<string?>(outcome switch
                    { "cancel" => null, "missing-file" => imagePath + ".missing", _ => imagePath });
                };
            });
            OnUi(() => vm.InsertKindRelativeToAsync("image", "anchor", "replace"));
            var saved = await ReadSaved(adapter);
            if (outcome is "accept" or "large")
            {
                var block = saved.Sections[0].Pages[0].Blocks[1];
                Assert.Equal(HavenRichBlockKind.Image, block.Kind); Assert.NotEmpty(block.Image!.DataBase64!);
                Assert.Equal(0, block.Image.Width); Assert.Equal(0, block.Image.Height);
                Assert.True(await adapter.UndoAsync()); Assert.Equal(before, adapter.Snapshot); Assert.False(adapter.CanUndo);
            }
            else
            {
                Assert.Equal(before, adapter.Snapshot); Assert.Equal(version, adapter.DocumentVersion); Assert.False(adapter.CanUndo);
            }
        }, rich => rich.Sections[0].Pages.Add(new HavenRichPage { Id = "other", Title = "Other" }));

    [Fact]
    public Task Whole_list_replacement_refuses_a_later_item_draft_changed_during_image_preparation() =>
        Run(async (adapter, vm) =>
        {
            var path = Path.Combine(Path.GetDirectoryName(adapter.FilePath!)!, "list.png");
            TestUiThread.Run(() =>
            {
                using var bitmap = new RenderTargetBitmap(new PixelSize(2, 2), new Vector(96, 96)); bitmap.Save(path);
                var picked = new TaskCompletionSource<string?>();
                vm.PickFileAsync = _ => picked.Task;
                var insertion = vm.InsertKindRelativeToAsync("image", "anchor", "replace");
                Assert.False(insertion.IsCompleted);
                var later = adapter.Document.Sections[0].Pages[0].Blocks.Single(block => block.Id == "anchor:two");
                later.Text = "Keep this later draft";
                picked.SetResult(path);
                AwaitOnUi(insertion);
            });
            var saved = await ReadSaved(adapter);
            var list = saved.Sections[0].Pages[0].Blocks.Single(block => block.Id == "anchor");
            Assert.Equal(HavenRichBlockKind.BulletList, list.Kind);
            Assert.Equal("Keep this later draft", list.Items.Single(item => item.Id == "two").Text);
            Assert.DoesNotContain(saved.Sections[0].Pages[0].Blocks, block => block.Kind == HavenRichBlockKind.Image);
        }, rich =>
        {
            var list = rich.Sections[0].Pages[0].Blocks[1]; list.Kind = HavenRichBlockKind.BulletList;
            list.Items = new[] { "one", "two" }.Select(id => new HavenRichListItem { Id = id, Text = "Keep " + id }).ToList();
        });

    [Theory]
    [InlineData("one", "above", "INSERT,one,two,three")]
    [InlineData("one", "below", "one,INSERT,two,three")]
    [InlineData("one", "replace", "INSERT,two,three")]
    [InlineData("three", "above", "one,two,INSERT,three")]
    [InlineData("three", "below", "one,two,three,INSERT")]
    [InlineData("three", "replace", "one,two,INSERT")]
    public Task First_and_last_list_item_destinations_preserve_formatting_and_reload_order(
        string itemId, string placement, string expected) => Run(async (adapter, vm) =>
    {
        var before = adapter.Snapshot;
        OnUi(() => vm.InsertKindRelativeToAsync("table", "anchor:" + itemId, placement));
        var saved = await ReadSaved(adapter);
        var middle = saved.Sections[0].Pages[0].Blocks.Where(block => block.Id is not ("first" or "last")).ToArray();
        Assert.Equal(expected.Split(','), middle.SelectMany(block => block.Kind == HavenRichBlockKind.Table
            ? new[] { "INSERT" } : block.Items.Select(item => item.Id)));
        Assert.All(middle.SelectMany(block => block.Items), item =>
        {
            Assert.True(item.Bold); Assert.True(item.Italic); Assert.True(item.Checked);
            Assert.Equal(1, item.Level); Assert.Equal("#FF123456", item.Foreground);
            Assert.Equal("Keep " + item.Id, item.Text);
        });
        Assert.True(await adapter.UndoAsync()); Assert.Equal(before, adapter.Snapshot); Assert.False(adapter.CanUndo);
    }, rich =>
    {
        var list = rich.Sections[0].Pages[0].Blocks[1]; list.Kind = HavenRichBlockKind.Checklist;
        list.Items = new[] { "one", "two", "three" }.Select(id => new HavenRichListItem
        { Id = id, Text = "Keep " + id, Bold = true, Italic = true, Checked = true, Level = 1, Foreground = "#FF123456" }).ToList();
    });

    [Theory]
    [InlineData("above")]
    [InlineData("below")]
    [InlineData("replace")]
    public Task Positioned_block_replacement_preserves_geometry_and_unsupported_flow_edges_refuse(string placement) =>
        Run(async (adapter, vm) =>
        {
            var before = adapter.Snapshot; var version = adapter.DocumentVersion;
            OnUi(() => vm.InsertKindRelativeToAsync("table", "anchor", placement));
            var saved = await ReadSaved(adapter);
            if (placement != "replace")
            {
                Assert.Equal(before, adapter.Snapshot); Assert.Equal(version, adapter.DocumentVersion); Assert.False(adapter.CanUndo);
                return;
            }
            var block = saved.Sections[0].Pages[0].Blocks[1]; Assert.Equal(HavenRichBlockKind.Table, block.Kind);
            Assert.Equal(HavenRichBlockLayouts.Absolute, block.Layout);
            Assert.Equal(45, block.X); Assert.Equal(71, block.Y); Assert.Equal(400, block.Width);
            Assert.Equal(260, block.Height); Assert.Equal(3, block.ZOrder);
            Assert.True(await adapter.UndoAsync()); Assert.Equal(before, adapter.Snapshot); Assert.False(adapter.CanUndo);
        }, rich =>
        {
            var page = rich.Sections[0].Pages[0]; page.LayoutMode = HavenRichPageLayoutModes.Unlocked;
            var block = page.Blocks[1]; block.Layout = HavenRichBlockLayouts.Absolute;
            block.X = 45; block.Y = 71; block.Width = 400; block.Height = 260; block.ZOrder = 3;
        });

    internal static async Task<HavenRichNotes> ReadSaved(ContractSessionAdapter adapter)
    {
        await adapter.SaveAsync();
        using var store = new JsonFileHavenBoardStore(Path.GetDirectoryName(adapter.FilePath!)!);
        await using var reopened = await RichBoardSession.OpenAtPathAsync(store, adapter.FilePath!);
        return reopened.Rich;
    }

    internal static async Task Run(Func<ContractSessionAdapter, BoardsViewModel, Task> action,
        Action<HavenRichNotes>? seed = null)
    {
        var root = Path.Combine(Path.GetTempPath(), "boards-relative-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            using var store = new JsonFileHavenBoardStore(root);
            var path = Path.Combine(root, "board.9to1board");
            await using (var owner = await RichBoardSession.CreateNewAsync(store, "Insertion owner"))
            {
                await owner.MutateAsync(rich =>
                {
                    var page = rich.Sections.Single().Pages.Single(); page.Blocks.Clear();
                    foreach (var id in new[] { "first", "anchor", "last" })
                    {
                        var block = HavenRichBlock.Paragraph("Preserve " + id); block.Id = id;
                        block.Order = page.Blocks.Count; page.Blocks.Add(block);
                    }
                    seed?.Invoke(rich);
                });
                await owner.SaveAsAsync(path);
            }
            await using var adapter = await ContractSessionAdapter.OpenAsync(store, path);
            var vm = TestUiThread.Run(() => new BoardsViewModel(adapter));
            try { await action(adapter, vm); }
            finally { TestUiThread.Run(vm.Dispose); }
        }
        finally { Directory.Delete(root, true); }
    }

    private static void OnUi(Func<Task> action) => TestUiThread.Run(() => AwaitOnUi(action()));

    private static void AwaitOnUi(Task task)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!task.IsCompleted && DateTime.UtcNow < deadline)
        { Dispatcher.UIThread.RunJobs(); Thread.Sleep(1); }
        Assert.True(task.IsCompleted, "Relative insertion did not settle."); task.GetAwaiter().GetResult();
    }
}
