using CakeOS.Apps.Boards.Contract;
using Xunit;

namespace CakeOS.Apps.Boards.App.Tests;

// Basic feature smoke against the actual existing session and physical store.
// Native input/rendering, full acceptance and retained Write adoption are separate.
[Collection(BoardSessionTestCollection.Name)]
public sealed class BoardsSelectedRangeTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Partial_mixed_range_uses_original_history_and_survives_save_reopen(bool backwards)
    {
        var root = Path.Combine(Path.GetTempPath(), "boards-range-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, "same-owner.9to1board");
            using var store = new JsonFileHavenBoardStore(root);
            await SeedAsync(store, path);
            string pageId, blockId; Guid id;
            await using (var adapter = await ContractSessionAdapter.OpenAsync(store, path))
            {
                var page = adapter.Document.Sections.Single().Pages.Single();
                var block = page.Blocks.Single(); pageId = page.Id; blockId = block.Id;
                var before = adapter.Snapshot;
                Assert.False(adapter.CanUndo);
                var selected = adapter.CaptureTextSelection(pageId, blockId, backwards ? 6 : 2, backwards ? 2 : 6);
                Assert.NotNull(selected);
                Assert.Equal(BoardTextMark.Mixed, selected!.Bold);
                Assert.Equal(before, adapter.Snapshot);
                Assert.True((await adapter.ToggleTextSelectionAsync(selected, BoardTextFormat.Bold, () => true)).Applied);
                var formatted = adapter.Snapshot;
                Assert.Equal(new[] { false, false, true, true, true, true, false, false },
                    adapter.Document.Sections.Single().Pages.Single().Blocks.Single().Runs
                        .SelectMany(run => Enumerable.Repeat(run.Bold, run.Text.Length)));
                Assert.Equal(new[] { HavenRichBaseline.Normal, HavenRichBaseline.Normal,
                    HavenRichBaseline.Superscript, HavenRichBaseline.Superscript, HavenRichBaseline.Normal,
                    HavenRichBaseline.Normal, HavenRichBaseline.Normal, HavenRichBaseline.Normal },
                    adapter.Document.Sections.Single().Pages.Single().Blocks.Single().Runs
                        .SelectMany(run => Enumerable.Repeat(run.Baseline, run.Text.Length)));
                Assert.True(await adapter.UndoAsync());
                Assert.Equal(before, adapter.Snapshot);
                Assert.False(adapter.CanUndo); // exactly one formatting transaction
                Assert.True(await adapter.RedoAsync());
                Assert.Equal(formatted, adapter.Snapshot);
                // A subsequent normal whole-block font change preserves the
                // newly selected bold range and the original Baseline fields.
                adapter.Document.Sections.Single().Pages.Single().Blocks.Single().FontSize = 18;
                await adapter.SaveAsync();
                Assert.Equal(new[] { false, false, true, true, true, true, false, false },
                    adapter.Document.Sections.Single().Pages.Single().Blocks.Single().Runs
                        .SelectMany(run => Enumerable.Repeat(run.Bold, run.Text.Length)));
                Assert.All(adapter.Document.Sections.Single().Pages.Single().Blocks.Single().Runs,
                    run => Assert.Equal(18d, run.FontSize));
                id = (await store.LoadDocumentAtPathAsync(path))!.DocumentId;
            }
            await using (var fresh = await ContractSessionAdapter.OpenAsync(store, path))
            {
                var page = fresh.Document.Sections.Single().Pages.Single();
                var block = page.Blocks.Single();
                Assert.Equal(pageId, page.Id); Assert.Equal(blockId, block.Id);
                Assert.Equal("AAbbccDD", block.Text);
                Assert.Equal(new[] { false, false, true, true, true, true, false, false },
                    block.Runs.SelectMany(run => Enumerable.Repeat(run.Bold, run.Text.Length)));
                Assert.Equal(new[] { "Aptos", "Aptos", "Georgia", "Georgia", "Inter", "Inter", "Inter", "Inter" },
                    block.Runs.SelectMany(run => Enumerable.Repeat(run.FontFamily, run.Text.Length)));
                Assert.All(block.Runs, run => Assert.Equal(18d, run.FontSize));
                Assert.Equal(HavenRichBaseline.Superscript, block.Runs.Single(run => run.Text == "bb").Baseline);
                Assert.Equal(id, (await store.LoadDocumentAtPathAsync(path))!.DocumentId);
            }
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task Stale_selection_and_denied_origin_do_not_create_content_or_history()
    {
        var root = Path.Combine(Path.GetTempPath(), "boards-range-deny-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, "denied.9to1board");
            using var store = new JsonFileHavenBoardStore(root);
            await SeedAsync(store, path);
            await using var adapter = await ContractSessionAdapter.OpenAsync(store, path);
            var page = adapter.Document.Sections.Single().Pages.Single(); var block = page.Blocks.Single();
            var before = adapter.Snapshot; var disk = await File.ReadAllBytesAsync(path);
            var stale = adapter.CaptureTextSelection(page.Id, block.Id, 2, 6)!;
            var current = adapter.CaptureTextSelection(page.Id, block.Id, 6, 2)!;
            Assert.False((await adapter.ToggleTextSelectionAsync(stale, BoardTextFormat.Bold, () => true)).Applied);
            // Same deny-only admission predicate used for readonly/input/lifetime
            // changes; this is actual owner refusal, not native UI evidence.
            Assert.False((await adapter.ToggleTextSelectionAsync(current, BoardTextFormat.Bold, () => false)).Applied);
            Assert.Equal(before, adapter.Snapshot); Assert.False(adapter.CanUndo); Assert.False(adapter.CanRedo);
            Assert.Equal(disk, await File.ReadAllBytesAsync(path));
            Assert.True((await adapter.ToggleTextSelectionAsync(current, BoardTextFormat.Bold, () => true)).Applied);
            Assert.True(await adapter.UndoAsync()); Assert.False(adapter.CanUndo); Assert.Equal(before, adapter.Snapshot);
            Assert.False((await adapter.ToggleTextSelectionAsync(current, BoardTextFormat.Bold, () => true)).Applied);
            Assert.False(adapter.CanUndo); // equal-content Undo cannot revive the old ticket
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static async Task SeedAsync(JsonFileHavenBoardStore store, string path)
    {
        await using var seed = await RichBoardSession.CreateNewAsync(store, "Original rich owner");
        await seed.MutateAsync(rich =>
        {
            var block = rich.Sections.Single().Pages.Single().Blocks.Single();
            HavenRichNotesOps.SetBlockRuns(rich, rich.Sections.Single().Pages.Single().Id, block.Id,
            [
                new() { Text = "AA", FontFamily = "Aptos", Foreground = "#FF112233" },
                new() { Text = "bb", Bold = true, Baseline = HavenRichBaseline.Superscript, FontFamily = "Georgia" },
                new() { Text = "cc", Italic = true, FontFamily = "Inter" },
                new() { Text = "DD", Underline = true, FontFamily = "Inter" }
            ]);
        });
        await seed.SaveAsAsync(path);
    }
}
