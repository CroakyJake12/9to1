using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CakeOS.Cui.Runtime;
using CakeOS.Apps.Boards.Contract;
using Xunit;

namespace CakeOS.Apps.Boards.App.Tests;

[Collection(BoardSessionTestCollection.Name)]
public sealed class BoardsLosslessConversionTests
{
    [Theory]
    [InlineData(HavenRichBlockKind.Checklist)]
    [InlineData(HavenRichBlockKind.BulletList)]
    [InlineData(HavenRichBlockKind.NumberedList)]
    public async Task Text_to_list_preserves_geometry_runs_marks_and_siblings_through_history_and_reopen(HavenRichBlockKind kind)
    {
        await WithBoardAsync(StyledText(), async (store, adapter, path, original) =>
        {
            var before = adapter.Snapshot;
            var target = adapter.CaptureBlockAction(PageId(adapter), "target")!;
            Assert.True(adapter.GetBlockConversionCapability(target, kind).CanApply);
            var result = await adapter.ApplyBlockConversionAsync(target, kind, () => true);
            Assert.True(result.Applied);
            await adapter.SaveAsync();
            var saved = await LoadTargetAsync(store, path);
            Assert.Equal(kind, saved.Kind);
            var item = Assert.Single(saved.Items);
            Assert.Equal("target:" + item.Id, result.SelectedBlockId);
            Assert.Equal(original.PlainText, item.Text);
            Assert.Equal(original.Runs[0].Bold, item.Bold);
            Assert.Equal(original.Runs[0].Italic, item.Italic);
            Assert.Equal(original.Runs[0].Underline, item.Underline);
            Assert.Equal(original.Runs[0].StrikeThrough, item.StrikeThrough);
            Assert.Equal(original.Runs[0].Baseline, item.Baseline);
            Assert.Equal(original.Runs[0].FontFamily, item.FontFamily);
            Assert.Equal(original.Runs[0].FontSize, item.FontSize);
            Assert.Equal(original.Runs[0].Foreground, item.Foreground);
            Assert.Equal(original.Runs[0].Background, item.Background);
            Assert.Equal(0, item.LineSpacing); // Still inherits the retained block override.
            saved.Kind = original.Kind; saved.Items.Clear();
            Assert.Equal(JsonSerializer.Serialize(original), JsonSerializer.Serialize(saved));
            await AssertHistoryAndReopenAsync(store, adapter, path, before);
        });
    }

    [Theory]
    [InlineData(HavenRichBlockKind.Heading)]
    [InlineData(HavenRichBlockKind.Checklist)]
    [InlineData(HavenRichBlockKind.BulletList)]
    [InlineData(HavenRichBlockKind.NumberedList)]
    public async Task Legacy_toolbar_conversion_uses_the_same_lossless_mapping(HavenRichBlockKind kind)
    {
        await WithBoardAsync(StyledText(), async (store, adapter, path, original) =>
        {
            var before = adapter.Snapshot;
            Assert.True(await adapter.ConvertBlockKindAsync(PageId(adapter), "target", kind));
            await adapter.SaveAsync();
            var saved = await LoadTargetAsync(store, path);
            Assert.Equal(kind, saved.Kind);
            saved.Kind = original.Kind; saved.Items.Clear();
            Assert.Equal(JsonSerializer.Serialize(original), JsonSerializer.Serialize(saved));
            await AssertHistoryAndReopenAsync(store, adapter, path, before);
        });
    }

    [Theory]
    [InlineData(HavenRichBlockKind.Checklist, HavenRichBlockKind.NumberedList)]
    [InlineData(HavenRichBlockKind.NumberedList, HavenRichBlockKind.BulletList)]
    [InlineData(HavenRichBlockKind.BulletList, HavenRichBlockKind.Checklist)]
    public async Task Explicit_whole_list_conversion_preserves_all_rows_and_checked_state(HavenRichBlockKind from, HavenRichBlockKind to)
    {
        var block = StyledList(from);
        block.Items.Add(new HavenRichListItem { Id = "second", Text = "Keep nested checked row", Level = 2,
            Checked = true, FontFamily = "Verdana", FontSize = 23, Background = "#FFABCDEF", LineSpacing = 2.1 });
        await WithBoardAsync(block, async (store, adapter, path, original) =>
        {
            var before = adapter.Snapshot;
            Assert.True(await adapter.ConvertBlockKindAsync(PageId(adapter), "target", to));
            await adapter.SaveAsync();
            var saved = await LoadTargetAsync(store, path);
            Assert.Equal(to, saved.Kind); saved.Kind = from;
            Assert.Equal(JsonSerializer.Serialize(original), JsonSerializer.Serialize(saved));
            await AssertHistoryAndReopenAsync(store, adapter, path, before);
        });
    }

    [Theory]
    [InlineData(HavenRichBlockKind.Checklist)]
    [InlineData(HavenRichBlockKind.BulletList)]
    [InlineData(HavenRichBlockKind.NumberedList)]
    public async Task Single_unchecked_row_to_text_carries_typography_and_effective_spacing(HavenRichBlockKind from)
    {
        await WithBoardAsync(StyledList(from), async (store, adapter, path, original) =>
        {
            var before = adapter.Snapshot;
            Assert.True(await adapter.ConvertBlockKindAsync(PageId(adapter), "target", HavenRichBlockKind.Paragraph));
            await adapter.SaveAsync();
            var saved = await LoadTargetAsync(store, path);
            var item = original.Items.Single(); var run = Assert.Single(saved.Runs);
            Assert.Equal(item.Text, saved.PlainText); Assert.Equal(item.Text, run.Text);
            Assert.Equal(item.Bold, run.Bold); Assert.Equal(item.Italic, run.Italic);
            Assert.Equal(item.Underline, run.Underline); Assert.Equal(item.StrikeThrough, run.StrikeThrough);
            Assert.Equal(item.Baseline, run.Baseline); Assert.Equal(item.FontFamily, run.FontFamily);
            Assert.Equal(item.FontSize, run.FontSize); Assert.Equal(item.Foreground, run.Foreground);
            Assert.Equal(item.Background, run.Background); Assert.Equal(item.LineSpacing, saved.LineSpacing);
            saved.Kind = from; saved.PlainText = original.PlainText; saved.Runs = original.Runs;
            saved.LineSpacing = original.LineSpacing;
            Assert.Equal(JsonSerializer.Serialize(original), JsonSerializer.Serialize(saved));
            await AssertHistoryAndReopenAsync(store, adapter, path, before);
        });
    }

    [Fact]
    public async Task Unedited_text_list_round_trip_keeps_run_segmentation_and_retained_item_identity()
    {
        var block = StyledText();
        block.Runs = [BoardTextRanges.Copy(block.Runs[0], "Alpha"), BoardTextRanges.Copy(block.Runs[0], "Beta")];
        await WithBoardAsync(block, async (store, adapter, path, original) =>
        {
            var pageId = PageId(adapter);
            Assert.True(await adapter.ConvertBlockKindAsync(pageId, "target", HavenRichBlockKind.BulletList));
            await adapter.SaveAsync();
            var itemId = (await LoadTargetAsync(store, path)).Items.Single().Id;
            Assert.True(await adapter.ConvertBlockKindAsync(pageId, "target", HavenRichBlockKind.Paragraph));
            await adapter.SaveAsync();
            var text = await LoadTargetAsync(store, path);
            Assert.Equal(JsonSerializer.Serialize(original.Runs), JsonSerializer.Serialize(text.Runs));
            Assert.True(await adapter.ConvertBlockKindAsync(pageId, "target", HavenRichBlockKind.NumberedList));
            await adapter.SaveAsync();
            Assert.Equal(itemId, (await LoadTargetAsync(store, path)).Items.Single().Id);
        });
    }

    [Theory]
    [InlineData("mixed-runs")]
    [InlineData("mismatched-runs")]
    [InlineData("dormant-items")]
    [InlineData("attachment")]
    [InlineData("checked")]
    [InlineData("nested")]
    [InlineData("multiple")]
    [InlineData("dormant-runs")]
    [InlineData("composite")]
    [InlineData("unsupported")]
    [InlineData("no-change")]
    public async Task Unrepresentable_conversion_refuses_without_mutation_or_undo_entry(string scenario)
    {
        var block = StyledText();
        var destination = HavenRichBlockKind.BulletList;
        if (scenario == "mixed-runs")
            block.Runs = [new() { Text = "Alpha", Bold = true }, new() { Text = "Beta", Bold = false }];
        if (scenario == "mismatched-runs") block.Runs[0].Text = "Old text";
        if (scenario == "dormant-items") block.Items = [new() { Text = "Preserve hidden row" }];
        if (scenario == "attachment") block.Attachment = new() { Id = "attached-file", DisplayName = "Keep visible",
            MediaType = "application/octet-stream", SizeBytes = 3, DataBase64 = "AQID" };
        if (scenario is "checked" or "nested" or "multiple" or "dormant-runs" or "composite")
        {
            block = StyledList(HavenRichBlockKind.Checklist); destination = HavenRichBlockKind.Paragraph;
            if (scenario == "checked") block.Items[0].Checked = true;
            if (scenario == "nested") block.Items[0].Level = 1;
            if (scenario is "multiple" or "composite") block.Items.Add(new() { Text = "Other row" });
            if (scenario == "dormant-runs") block.Runs = [new() { Text = "Keep dormant runs" }];
        }
        if (scenario == "unsupported") destination = HavenRichBlockKind.Image;
        if (scenario == "no-change") destination = block.Kind;
        await WithBoardAsync(block, async (store, adapter, path, original) =>
        {
            var before = adapter.Snapshot;
            var id = scenario == "composite" ? "target:row" : "target";
            Assert.False(adapter.GetBlockConversionCapability(PageId(adapter), id, destination).CanApply);
            Assert.False(await adapter.ConvertBlockKindAsync(PageId(adapter), id, destination));
            Assert.Equal(before, adapter.Snapshot); Assert.False(adapter.CanUndo);
            await adapter.SaveAsync();
            Assert.Equal(JsonSerializer.Serialize(original), JsonSerializer.Serialize(await LoadTargetAsync(store, path)));
        });
    }

    [Theory]
    [InlineData(HavenRichBlockKind.Table)]
    [InlineData(HavenRichBlockKind.Divider)]
    public async Task Empty_text_becomes_real_object_with_geometry_and_format_retained(HavenRichBlockKind kind)
    {
        var block = StyledText(); block.PlainText = ""; block.Runs[0].Text = "";
        await WithBoardAsync(block, async (store, adapter, path, original) =>
        {
            var before = adapter.Snapshot;
            var target = adapter.CaptureBlockAction(PageId(adapter), "target")!;
            Assert.True((await adapter.ApplyBlockConversionAsync(target, kind, () => true)).Applied);
            await adapter.SaveAsync();
            var saved = await LoadTargetAsync(store, path);
            if (kind == HavenRichBlockKind.Table)
            {
                Assert.NotNull(saved.Table); Assert.Equal(3, saved.Table.Rows.Count);
                Assert.All(saved.Table.Rows, row => Assert.All(row.Cells, cell => Assert.Equal("", cell.Text)));
            }
            else Assert.NotNull(saved.Divider);
            saved.Kind = original.Kind; saved.Table = null; saved.Divider = null;
            Assert.Equal(JsonSerializer.Serialize(original), JsonSerializer.Serialize(saved));
            await AssertHistoryAndReopenAsync(store, adapter, path, before);
        });
    }

    [Theory]
    [InlineData(" ")]
    [InlineData("\n")]
    [InlineData("Text")]
    public async Task Object_conversion_never_hides_nonempty_text(string text)
    {
        var block = StyledText(); block.PlainText = text; block.Runs[0].Text = text;
        await WithBoardAsync(block, async (_, adapter, _, _) =>
        {
            var before = adapter.Snapshot;
            Assert.False(await adapter.ConvertBlockKindAsync(PageId(adapter), "target", HavenRichBlockKind.Table));
            Assert.False(await adapter.ConvertBlockKindAsync(PageId(adapter), "target", HavenRichBlockKind.Divider));
            Assert.Equal(before, adapter.Snapshot); Assert.False(adapter.CanUndo);
        });
    }

    [Theory]
    [InlineData("inherited", HavenRichBlockKind.Checklist)]
    [InlineData("inherited", HavenRichBlockKind.BulletList)]
    [InlineData("inherited", HavenRichBlockKind.NumberedList)]
    [InlineData("explicit-clear", HavenRichBlockKind.Checklist)]
    [InlineData("explicit-clear", HavenRichBlockKind.BulletList)]
    [InlineData("explicit-clear", HavenRichBlockKind.NumberedList)]
    [InlineData("heading-fallback", HavenRichBlockKind.Checklist)]
    [InlineData("heading-fallback", HavenRichBlockKind.BulletList)]
    [InlineData("heading-fallback", HavenRichBlockKind.NumberedList)]
    public async Task Conversion_preserves_actual_shaped_style_marks_and_heading_defaults(string scenario, HavenRichBlockKind kind)
    {
        var block = StyledText(); block.StyleId = "conversion-style"; block.Layout = HavenRichBlockLayouts.Flow;
        block.Runs = scenario == "explicit-clear" ? [new() { Text = block.PlainText }] : [];
        block.Kind = scenario == "heading-fallback" ? HavenRichBlockKind.Heading : HavenRichBlockKind.Paragraph;
        await WithBoardAsync(block, async (store, adapter, path, _) =>
        {
            var before = RenderedTextFormat(adapter);
            Assert.True(await adapter.ConvertBlockKindAsync(PageId(adapter), "target", kind));
            Assert.Equal(before, RenderedTextFormat(adapter));
            await adapter.SaveAsync();
            await using var reopened = await ContractSessionAdapter.OpenAsync(store, path);
            Assert.Equal(before, RenderedTextFormat(reopened));
            var saved = await LoadTargetAsync(store, path); var item = saved.Items.Single();
            Assert.Equal(scenario != "explicit-clear", item.Bold);
            Assert.Equal(scenario == "inherited", item.Italic);
            Assert.Equal(scenario == "inherited", item.Underline);
            Assert.Equal(scenario == "inherited", item.StrikeThrough);
            Assert.Equal(scenario == "inherited" ? HavenRichBaseline.Superscript : HavenRichBaseline.Normal, item.Baseline);
            if (scenario == "heading-fallback") Assert.Equal(26, item.FontSize);
        }, notes => notes.Styles.Add(new HavenRichStyle
        {
            Id = "conversion-style", Name = "Conversion style", FontFamily = "Georgia",
            FontSize = scenario == "heading-fallback" ? 0 : 21,
            Bold = scenario != "heading-fallback", Italic = scenario != "heading-fallback",
            Underline = scenario != "heading-fallback", StrikeThrough = scenario != "heading-fallback",
            Baseline = scenario == "heading-fallback" ? HavenRichBaseline.Normal : HavenRichBaseline.Superscript,
            Foreground = "#FF123456", Background = "#FF654321"
        }));
    }

    private static string RenderedTextFormat(ContractSessionAdapter adapter) => TestUiThread.Run(() =>
    {
        using var vm = new BoardsViewModel(adapter);
        var panel = new StackPanel(); BlockRenderer.Rebuild(panel, vm, []);
        var window = new Window { Content = panel, Width = 900, Height = 650 };
        try
        {
            window.Show(); window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            var input = panel.GetVisualDescendants().OfType<TextBox>().Single(value => value.Text == "AlphaBeta");
            var presenter = Assert.Single(input.GetVisualDescendants().OfType<CuiFormattedTextPresenter>());
            var format = presenter.TextLayout.TextLines.SelectMany(line => line.TextRuns).OfType<ShapedTextRun>().First().Properties;
            return JsonSerializer.Serialize(new
            {
                Size = format.FontRenderingEmSize, Family = format.Typeface.FontFamily.Name,
                Weight = format.Typeface.Weight.ToString(), Style = format.Typeface.Style.ToString(),
                Baseline = format.BaselineAlignment.ToString(),
                Underline = format.TextDecorations?.Any(value => value.Location == TextDecorationLocation.Underline) == true,
                Strike = format.TextDecorations?.Any(value => value.Location == TextDecorationLocation.Strikethrough) == true,
                Foreground = (format.ForegroundBrush as ISolidColorBrush)?.Color.ToString(),
                Background = ((format.BackgroundBrush ?? input.Background) as ISolidColorBrush)?.Color.ToString()
            });
        }
        finally { window.Close(); }
    });

    private static HavenRichBlock StyledText() => new()
    {
        Id = "target", PlainText = "AlphaBeta", Kind = HavenRichBlockKind.Paragraph,
        Layout = HavenRichBlockLayouts.Absolute, X = 42, Y = 93, Width = 416, Height = 137, ZOrder = 4,
        StyleId = "quote", Alignment = HavenRichAlignment.Right, IndentLevel = 2,
        LineSpacing = 1.7, SpaceBefore = 8, SpaceAfter = 11,
        Runs = [new() { Text = "AlphaBeta", Bold = true, Italic = true, Underline = true,
            StrikeThrough = true, Baseline = HavenRichBaseline.Subscript, FontFamily = "Georgia", FontSize = 19,
            Foreground = "#FF123456", Background = "#FF654321" }]
    };

    private static HavenRichBlock StyledList(HavenRichBlockKind kind)
    {
        var block = StyledText(); block.Kind = kind; block.Runs.Clear(); block.PlainText = "";
        block.Items = [new() { Id = "row", Text = "Keep all row typography", Bold = true, Italic = true,
            Underline = true, StrikeThrough = true, Baseline = HavenRichBaseline.Superscript,
            FontFamily = "Georgia", FontSize = 21, Foreground = "#FF135791", Background = "#FF246802", LineSpacing = 1.9 }];
        return block;
    }

    private static string PageId(ContractSessionAdapter adapter) => adapter.Document.Sections.Single().Pages.Single().Id;
    private static async Task<HavenRichBlock> LoadTargetAsync(JsonFileHavenBoardStore store, string path) =>
        (await store.LoadDocumentAtPathAsync(path))!.RichNotes!.Sections.Single().Pages.Single().Blocks.Single(block => block.Id == "target");

    private static async Task AssertHistoryAndReopenAsync(JsonFileHavenBoardStore store,
        ContractSessionAdapter adapter, string path, string before)
    {
        var after = adapter.Snapshot;
        Assert.True(await adapter.UndoAsync()); Assert.Equal(before, adapter.Snapshot); Assert.False(adapter.CanUndo);
        Assert.True(await adapter.RedoAsync()); Assert.Equal(after, adapter.Snapshot);
        await adapter.SaveAsync();
        await using var reopened = await ContractSessionAdapter.OpenAsync(store, path);
        Assert.Equal(after, reopened.Snapshot);
        var sibling = (await store.LoadDocumentAtPathAsync(path))!.RichNotes!.Sections.Single().Pages.Single()
            .Blocks.Single(block => block.Id == "untouched");
        Assert.Equal("Never change me", sibling.PlainText); Assert.True(sibling.Runs.Single().Underline);
    }

    private static async Task WithBoardAsync(HavenRichBlock block,
        Func<JsonFileHavenBoardStore, ContractSessionAdapter, string, HavenRichBlock, Task> check,
        Action<HavenRichNotes>? configure = null)
    {
        var root = Path.Combine(Path.GetTempPath(), "boards-lossless-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            using var store = new JsonFileHavenBoardStore(root);
            var path = Path.Combine(root, "conversion.9to1board");
            await using (var owner = await RichBoardSession.CreateNewAsync(store, "Lossless conversions"))
            {
                await owner.MutateAsync(rich =>
                {
                    configure?.Invoke(rich);
                    var page = rich.Sections.Single().Pages.Single(); page.LayoutMode = HavenRichPageLayoutModes.Unlocked;
                    page.Blocks = [block, new HavenRichBlock { Id = "untouched", PlainText = "Never change me",
                        Runs = [new() { Text = "Never change me", Underline = true }] }];
                });
                await owner.SaveAsAsync(path);
            }
            var original = await LoadTargetAsync(store, path);
            await using var adapter = await ContractSessionAdapter.OpenAsync(store, path);
            await check(store, adapter, path, original);
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
