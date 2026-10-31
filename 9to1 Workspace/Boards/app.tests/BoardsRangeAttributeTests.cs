using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CakeOS.Apps.Boards.Contract;
using CakeOS.Cui.Runtime;
using Xunit;

namespace CakeOS.Apps.Boards.App.Tests;

[Collection(BoardSessionTestCollection.Name)]
public sealed class BoardsRangeAttributeTests
{
    [Theory]
    [InlineData("family", false)]
    [InlineData("size", true)]
    [InlineData("colors", false)]
    [InlineData("foreground", true)]
    [InlineData("background", false)]
    [InlineData("baseline", true)]
    [InlineData("scale", false)]
    [InlineData("clear", true)]
    public Task Selected_attributes_preserve_other_runs_and_have_one_durable_history_entry(string attribute, bool backwards) =>
        Run(async (documents, id, adapter, vm, window) =>
        {
            var before = adapter.Snapshot;
            var original = Characters(vm.PageBlocks.Single().Runs);
            TestUiThread.Run(() =>
            {
                var input = window.GetVisualDescendants().OfType<TextBox>().Single();
                input.Focus(); input.SelectionStart = backwards ? 6 : 2; input.SelectionEnd = backwards ? 2 : 6;
                Apply(vm, attribute);
                Assert.True(input.IsFocused);
                Assert.Equal(backwards ? 6 : 2, input.SelectionStart);
                Assert.Equal(backwards ? 2 : 6, input.SelectionEnd);
                // A later real render still retains the active range/caret.
                var host = Assert.IsType<StackPanel>(window.Content);
                BlockRenderer.Rebuild(host, vm, []);
                Dispatcher.UIThread.RunJobs();
                var replacement = window.GetVisualDescendants().OfType<TextBox>().Single();
                Assert.True(replacement.IsFocused);
                Assert.Equal(backwards ? 6 : 2, replacement.SelectionStart);
                Assert.Equal(backwards ? 2 : 6, replacement.SelectionEnd);
            });
            await adapter.SaveAsync();
            var formatted = adapter.Snapshot;
            var actual = Characters(adapter.Document.Sections[0].Pages[0].Blocks.Single().Runs);
            Assert.Equal("AAbbccDD", string.Concat(actual.Select(run => run.Text)));
            for (var i = 0; i < actual.Length; i++)
            {
                if (i is < 2 or >= 6)
                {
                    Assert.Equal(JsonSerializer.Serialize(original[i]), JsonSerializer.Serialize(actual[i]));
                    continue;
                }
                var expected = BoardTextRanges.Copy(original[i]);
                switch (attribute)
                {
                    case "family": expected.FontFamily = "Georgia"; break;
                    case "size": expected.FontSize = 24; break;
                    case "colors": expected.Foreground = "#FF123456"; expected.Background = "#FFABCDEF"; break;
                    case "foreground": expected.Foreground = "#FF123456"; break;
                    case "background": expected.Background = "#FFABCDEF"; break;
                    case "baseline": expected.Baseline = HavenRichBaseline.Subscript; break;
                    case "scale": expected.FontSize += 2; break;
                    case "clear":
                        Assert.False(actual[i].Bold); Assert.False(actual[i].Italic);
                        Assert.False(actual[i].Underline); Assert.False(actual[i].StrikeThrough);
                        Assert.Equal(HavenRichBaseline.Normal, actual[i].Baseline);
                        Assert.True(actual[i].FontSize > 0);
                        continue;
                }
                Assert.Equal(JsonSerializer.Serialize(expected), JsonSerializer.Serialize(actual[i]));
            }
            Assert.True(await adapter.UndoAsync());
            Assert.Equal(before, adapter.Snapshot); Assert.False(adapter.CanUndo);
            Assert.True(await adapter.RedoAsync()); Assert.Equal(formatted, adapter.Snapshot);
            await adapter.SaveAsync();
            await using var reopened = await documents.OpenAsync(id);
            Assert.Equal(JsonSerializer.Serialize(actual), JsonSerializer.Serialize(Characters(reopened.Document.Sections[0].Pages[0].Blocks.Single().Runs)));
        });

    [Fact]
    public Task Whole_block_formatting_updates_existing_run_overrides() => Run(async (_, _, adapter, vm, _) =>
    {
        TestUiThread.Run(() => vm.ApplyFontFamilyAttributes("Georgia"));
        await adapter.SaveAsync();
        Assert.All(adapter.Document.Sections[0].Pages[0].Blocks.Single().Runs, run => Assert.Equal("Georgia", run.FontFamily));
        Assert.True(await adapter.UndoAsync()); Assert.False(adapter.CanUndo);
    });

    [Fact]
    public Task Whole_block_scaling_preserves_mixed_run_sizes_in_one_durable_edit() =>
        Run(async (documents, id, adapter, vm, _) =>
        {
            var before = adapter.Snapshot;
            var expected = Characters(adapter.Document.Sections[0].Pages[0].Blocks.Single().Runs);
            foreach (var run in expected) run.FontSize += 2;
            TestUiThread.Run(() => vm.ScaleFontAttributes(2));
            await adapter.SaveAsync();
            var scaled = adapter.Snapshot;
            Assert.Equal(JsonSerializer.Serialize(expected), JsonSerializer.Serialize(
                Characters(adapter.Document.Sections[0].Pages[0].Blocks.Single().Runs)));
            Assert.True(await adapter.UndoAsync());
            Assert.Equal(before, adapter.Snapshot); Assert.False(adapter.CanUndo);
            Assert.True(await adapter.RedoAsync()); Assert.Equal(scaled, adapter.Snapshot);
            await adapter.SaveAsync();
            await using var reopened = await documents.OpenAsync(id);
            Assert.Equal(JsonSerializer.Serialize(expected), JsonSerializer.Serialize(
                Characters(reopened.Document.Sections[0].Pages[0].Blocks.Single().Runs)));
        });

    [Fact]
    public Task Detached_selection_cannot_change_content_or_history() => Run(async (_, _, adapter, vm, window) =>
    {
        var before = adapter.Snapshot;
        TestUiThread.Run(() =>
        {
            var input = window.GetVisualDescendants().OfType<TextBox>().Single();
            input.Focus(); input.SelectionStart = 2; input.SelectionEnd = 6;
            vm.Detach();
            vm.ApplyFontSizeAttributes(24); vm.ClearTextAttributes();
        });
        await adapter.SaveAsync();
        Assert.Equal(before, adapter.Snapshot); Assert.False(adapter.CanUndo);
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task Clearing_uniform_highlight_or_font_updates_live_runs_and_survives_reopen(bool font) =>
        Run(async (documents, id, adapter, vm, window) =>
        {
            var before = adapter.Snapshot;
            string? defaultFont = null;
            TestUiThread.Run(() =>
            {
                var input = window.GetVisualDescendants().OfType<TextBox>().Single();
                input.Focus(); input.SelectionStart = 2; input.SelectionEnd = 6;
                if (font) vm.ApplyFontFamilyAttributes(string.Empty);
                else vm.ApplyBackgroundAttributes(string.Empty);
                void AssertPresentation(TextBox editor)
                {
                    Assert.Equal(0, Assert.IsAssignableFrom<ISolidColorBrush>(editor.Background).Color.A);
                    var presentation = Assert.IsType<CuiFormattedText>(CuiFormattedTextInput.GetPresentation(editor));
                    var chars = presentation.Runs.SelectMany(run => Enumerable.Repeat(run, run.Text.Length)).ToArray();
                    for (var i = 0; i < chars.Length; i++)
                    {
                        if (font)
                        {
                            if (i is >= 2 and < 6) { Assert.False(string.IsNullOrEmpty(chars[i].FontFamily)); Assert.NotEqual("Georgia", chars[i].FontFamily); defaultFont = chars[i].FontFamily; }
                            else Assert.Equal("Georgia", chars[i].FontFamily);
                        }
                        else if (i is >= 2 and < 6)
                        {
                            Assert.True(chars[i].Background.HasValue);
                            Assert.Equal(0, chars[i].Background.GetValueOrDefault().A);
                        }
                        else Assert.Equal(Color.Parse("#FFCCDDEE"), chars[i].Background);
                    }
                }
                AssertPresentation(input);
                BlockRenderer.Rebuild(Assert.IsType<StackPanel>(window.Content), vm, []);
                AssertPresentation(window.GetVisualDescendants().OfType<TextBox>().Single());
            });
            await adapter.SaveAsync();
            var saved = adapter.Snapshot;
            Assert.True(await adapter.UndoAsync()); Assert.Equal(before, adapter.Snapshot); Assert.False(adapter.CanUndo);
            Assert.True(await adapter.RedoAsync()); Assert.Equal(saved, adapter.Snapshot);
            await adapter.SaveAsync();
            await using var reopened = await documents.OpenAsync(id);
            var chars = Characters(reopened.Document.Sections[0].Pages[0].Blocks.Single().Runs);
            for (var i = 0; i < chars.Length; i++)
                if (font) Assert.Equal(i is >= 2 and < 6 ? defaultFont : "Georgia", chars[i].FontFamily);
                else Assert.Equal(i is >= 2 and < 6 ? "#00000000" : "#FFCCDDEE", chars[i].Background);
        }, uniformFont: true);

    private static void Apply(BoardsViewModel vm, string attribute)
    {
        switch (attribute)
        {
            case "family": vm.ApplyFontFamilyAttributes("Georgia"); break;
            case "size": vm.ApplyFontSizeAttributes(24); break;
            case "colors": vm.ApplyColorAttributes("#FF123456", "#FFABCDEF"); break;
            case "foreground": vm.ApplyForegroundAttributes("#FF123456"); break;
            case "background": vm.ApplyBackgroundAttributes("#FFABCDEF"); break;
            case "baseline": vm.ApplyBaselineAttributes("subscript"); break;
            case "scale": vm.ScaleFontAttributes(2); break;
            case "clear": vm.ClearTextAttributes(); break;
            default: throw new ArgumentOutOfRangeException(nameof(attribute));
        }
    }

    private static HavenRichTextRun[] Characters(IEnumerable<HavenRichTextRun> runs) =>
        runs.SelectMany(run => run.Text.Select(character => BoardTextRanges.Copy(run, character.ToString()))).ToArray();

    private static async Task Run(Func<BoardsLocalDocuments, Guid, ContractSessionAdapter, BoardsViewModel, Window, Task> action, bool uniformFont = false)
    {
        TestUiThread.Run(() => { });
        var root = Path.Combine(Path.GetTempPath(), "boards-range-attributes-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            using var documents = new BoardsLocalDocuments("range-attributes", root);
            var id = await documents.CreateAsync();
            await using (var seed = await documents.OpenAsync(id))
            {
                var block = seed.Document.Sections[0].Pages[0].Blocks[0];
                block.Text = "AAbbccDD";
                block.Runs = Enumerable.Range(0, 4).Select(i => new HavenRichTextRun
                {
                    Text = block.Text.Substring(i * 2, 2), FontFamily = uniformFont ? "Georgia" : "Inter", FontSize = 11 + i * 2,
                    Bold = i % 2 == 0, Italic = true, Underline = i == 1, StrikeThrough = i == 2,
                    Baseline = i == 1 ? HavenRichBaseline.Superscript : HavenRichBaseline.Normal,
                    Foreground = "#FF112233", Background = "#FFCCDDEE"
                }).ToList();
                seed.MarkDirty(); await seed.SaveAsync();
            }
            await using var adapter = await documents.OpenAsync(id);
            BoardsViewModel? vm = null; Window? window = null;
            try
            {
                TestUiThread.Run(() =>
                {
                    vm = new BoardsViewModel(adapter);
                    var host = new StackPanel(); vm.Attach(host);
                    BlockRenderer.Rebuild(host, vm, []);
                    window = new Window { Content = host, Width = 800, Height = 600 }; window.Show();
                });
                await action(documents, id, adapter, vm!, window!);
            }
            finally { TestUiThread.Run(() => { window?.Close(); vm?.Dispose(); }); }
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
