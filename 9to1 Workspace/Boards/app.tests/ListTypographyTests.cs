using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Media.TextFormatting;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CakeOS.Apps.Boards.Contract;
using CakeOS.Cui.Runtime;
using Xunit;

namespace CakeOS.Apps.Boards.App.Tests;

[Collection(BoardSessionTestCollection.Name)]
public sealed class ListTypographyTests
{
    [Theory]
    [InlineData(HavenRichBlockKind.Checklist)]
    [InlineData(HavenRichBlockKind.BulletList)]
    [InlineData(HavenRichBlockKind.NumberedList)]
    public async Task Projected_row_format_saves_reopens_and_has_reversible_history(HavenRichBlockKind kind)
    {
        var root = Path.Combine(Path.GetTempPath(), "list-projection-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            using var store = new JsonFileHavenBoardStore(root); var path = Path.Combine(root, "list.9to1board");
            await using (var seed = await RichBoardSession.CreateNewAsync(store, "Typography"))
            {
                await seed.MutateAsync(notes => notes.Sections[0].Pages[0].Blocks =
                    [new HavenRichBlock { Id = "list", Kind = kind, LineSpacing = 1.5,
                        Items = [new HavenRichListItem { Id = "row", Text = "Original" }] }]);
                await seed.SaveAsAsync(path);
            }
            await using (var adapter = await ContractSessionAdapter.OpenAsync(store, path))
            {
                var before = adapter.Snapshot;
                var row = adapter.Document.Sections[0].Pages[0].Blocks.Single();
                Assert.Equal(1.5, row.LineSpacing);
                row.FontFamily = "Georgia"; row.FontSize = 24; row.Background = "#FFFFFF00";
                row.Bold = row.Italic = row.Underline = row.Strike = true;
                row.Baseline = "superscript"; row.Foreground = "#FF123456";
                adapter.MarkDirty(); await adapter.SaveAsync();
                var raw = (await adapter.GetDetachedContractDocumentAsync()).RichNotes!.Sections[0].Pages[0].Blocks.Single().Items.Single();
                Assert.Equal(0, raw.LineSpacing); // Unrelated formatting must retain parent inheritance.
                var formatted = adapter.Snapshot;
                Assert.True(await adapter.UndoAsync()); Assert.Equal(before, adapter.Snapshot);
                Assert.True(await adapter.RedoAsync()); Assert.Equal(formatted, adapter.Snapshot);
                row = adapter.Document.Sections[0].Pages[0].Blocks.Single(); row.LineSpacing = 2;
                adapter.MarkDirty(); await adapter.SaveAsync();
            }
            await using var reopened = await ContractSessionAdapter.OpenAsync(store, path);
            var actual = reopened.Document.Sections[0].Pages[0].Blocks.Single();
            Assert.Equal("Georgia", actual.FontFamily); Assert.Equal(24, actual.FontSize);
            Assert.Equal("#FFFFFF00", actual.Background); Assert.Equal(2, actual.LineSpacing);
            Assert.True(actual.Bold); Assert.True(actual.Italic); Assert.True(actual.Underline); Assert.True(actual.Strike);
            Assert.Equal("superscript", actual.Baseline); Assert.Equal("#FF123456", actual.Foreground);
        }
        finally { Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData("checklist", false)]
    [InlineData("bulleted", false)]
    [InlineData("numbered", false)]
    [InlineData("checklist", true)]
    public async Task Native_list_presenter_shapes_marks_highlight_and_line_metrics(string kind, bool isChecked)
    {
        await using var session = new InMemoryRichBoardSession();
        await session.OpenAsync("memory://list-typography");
        TestUiThread.Run(() =>
        {
            using var vm = new BoardsViewModel(session);
            var row = new RichBoardBlock { Id = "list:row", Kind = kind, Text = "Ag\nAg", FontFamily = "Georgia",
                FontSize = 24, Foreground = "#FF123456", Background = "#FFFFFF00", LineSpacing = 2,
                Bold = true, Italic = true, Underline = true, Strike = true, Baseline = "superscript", IsChecked = isChecked };
            session.Document.Sections[0].Pages[0].Blocks = [row];
            var host = new StackPanel(); BlockRenderer.Rebuild(host, vm, []);
            var window = new Window { Content = host, Width = 700, Height = 500 };
            try
            {
                window.Show(); Pump(window);
                var input = Assert.Single(host.GetVisualDescendants().OfType<TextBox>());
                var presenter = Assert.Single(input.GetVisualDescendants().OfType<CuiFormattedTextPresenter>());
                var shaped = presenter.TextLayout.TextLines.SelectMany(line => line.TextRuns).OfType<ShapedTextRun>().First().Properties;
                Assert.Equal(24, shaped.FontRenderingEmSize); Assert.Equal("Georgia", shaped.Typeface.FontFamily.Name);
                Assert.Equal(FontWeight.Bold, shaped.Typeface.Weight); Assert.Equal(FontStyle.Italic, shaped.Typeface.Style);
                Assert.Equal(BaselineAlignment.Superscript, shaped.BaselineAlignment);
                Assert.Contains(shaped.TextDecorations!, mark => mark.Location == TextDecorationLocation.Underline);
                Assert.Contains(shaped.TextDecorations!, mark => mark.Location == TextDecorationLocation.Strikethrough);
                Assert.Equal(Colors.Yellow, Assert.IsAssignableFrom<ISolidColorBrush>(shaped.BackgroundBrush).Color);
                var expectedForeground = isChecked
                    ? Assert.IsAssignableFrom<ISolidColorBrush>(BoardsTheme.SecondaryTextBrush).Color : Color.Parse("#FF123456");
                Assert.Equal(expectedForeground, Assert.IsAssignableFrom<ISolidColorBrush>(shaped.ForegroundBrush).Color);
                var spaced = presenter.TextLayout.TextLines[0].Height;
                input.LineHeight = double.NaN; Pump(window);
                var natural = presenter.TextLayout.TextLines[0].Height;
                Assert.InRange(spaced, natural * 2 - .01, natural * 2 + .01);
                BlockRenderer.UpdateTextPresentation(input, row, vm.StyleList.Single(style => style.Id == "normal")); Pump(window);
                var first = presenter.TextLayout.HitTestTextPosition(0);
                var second = presenter.TextLayout.HitTestTextPosition(3);
                Assert.InRange(second.Y - first.Y, spaced - .01, spaced + .01);
                using var bitmap = new RenderTargetBitmap(new PixelSize(700, 500), new Vector(96, 96));
                bitmap.Render(window); // Exercise the native glyph/highlight/decorations paint path.
                Assert.Equal(kind == "checklist" ? 1 : 0, host.GetVisualDescendants().OfType<CheckBox>().Count());
            }
            finally { window.Close(); }
        });
    }

    [Theory]
    [InlineData("checklist")]
    [InlineData("bulleted")]
    [InlineData("numbered")]
    public void Fresh_insertion_and_imported_page_preserve_list_kind_and_full_row_format(string kind)
    {
        var row = new RichBoardBlock { Id = "list:row", Kind = kind, Text = "Imported", FontFamily = "Georgia", FontSize = 24,
            Background = "#FFFFFF00", Foreground = "#FF123456", LineSpacing = 2, Bold = true, Underline = true, Baseline = "subscript" };
        var inserted = Assert.IsType<HavenRichBlock>(MergeDeltas.PrepareInsertion(row));
        Assert.Equal(kind == "numbered" ? HavenRichBlockKind.NumberedList : kind == "bulleted" ? HavenRichBlockKind.BulletList : HavenRichBlockKind.Checklist, inserted.Kind);
        AssertItem(inserted.Items.Single());
        var rich = HavenRichNotes.Create(); var baseline = new RichBoardDocument(); Projector.Project(rich, baseline);
        var view = new RichBoardDocument(); Projector.Project(rich, view);
        view.Sections[0].Pages.Add(new RichBoardPage { Id = "imported", Title = "Imported", Blocks = [row] });
        MergeDeltas.Apply(rich, view, baseline);
        var imported = rich.Sections[0].Pages.Single(page => page.Id == "imported").Blocks.Single();
        Assert.Equal(inserted.Kind, imported.Kind); AssertItem(imported.Items.Single());
        static void AssertItem(HavenRichListItem item)
        {
            Assert.Equal("Georgia", item.FontFamily); Assert.Equal(24, item.FontSize); Assert.Equal(2, item.LineSpacing);
            Assert.Equal("#FFFFFF00", item.Background); Assert.Equal("#FF123456", item.Foreground);
            Assert.True(item.Bold); Assert.True(item.Underline); Assert.Equal(HavenRichBaseline.Subscript, item.Baseline);
        }
    }

    [Theory]
    [InlineData(HavenRichBlockKind.Checklist)]
    [InlineData(HavenRichBlockKind.BulletList)]
    [InlineData(HavenRichBlockKind.NumberedList)]
    public void Parent_spacing_surrounds_the_list_once_and_survives_page_import(HavenRichBlockKind kind)
    {
        var rich = HavenRichNotes.Create();
        rich.Sections[0].Pages[0].Blocks = [new HavenRichBlock { Id = "list", Kind = kind,
            SpaceBefore = 12, SpaceAfter = 20, IndentLevel = 2,
            Items = [new HavenRichListItem { Id = "a", Text = "A" }, new HavenRichListItem { Id = "b", Text = "B" },
                new HavenRichListItem { Id = "c", Text = "C" }] }];
        var view = new RichBoardDocument(); Projector.Project(rich, view);
        var rows = view.Sections[0].Pages[0].Blocks;
        Assert.Equal(new double[] { 12, 0, 0 }, rows.Select(row => row.SpaceBefore));
        Assert.Equal(new double[] { 0, 0, 20 }, rows.Select(row => row.SpaceAfter));
        Assert.All(rows, row => Assert.Equal(2, row.IndentLevel));
        var imported = HavenRichNotes.Create(); var baseline = new RichBoardDocument(); Projector.Project(imported, baseline);
        var target = new RichBoardDocument(); Projector.Project(imported, target);
        target.Sections[0].Pages.Add(new RichBoardPage { Title = "Imported list", Blocks = rows });
        MergeDeltas.Apply(imported, target, baseline);
        var list = imported.Sections[0].Pages.Last().Blocks.Single();
        Assert.Equal(12, list.SpaceBefore); Assert.Equal(20, list.SpaceAfter); Assert.Equal(2, list.IndentLevel);
    }

    [Theory]
    [InlineData("paragraph")]
    [InlineData("heading")]
    [InlineData("checklist")]
    [InlineData("bulleted")]
    [InlineData("numbered")]
    public async Task Actual_editor_margins_use_explicit_spacing_and_inherited_parent_indent(string kind)
    {
        await using var session = new InMemoryRichBoardSession();
        await session.OpenAsync("memory://list-margins");
        TestUiThread.Run(() =>
        {
            using var vm = new BoardsViewModel(session);
            var isList = kind is "checklist" or "bulleted" or "numbered";
            var row = new RichBoardBlock { Id = isList ? "list:row" : "text", Kind = kind, Text = "Margin",
                SpaceBefore = 12, SpaceAfter = 20, IndentLevel = -1, Level = 1 };
            session.Document.Sections[0].Pages[0].Blocks = [row];
            vm.StyleList.Single(style => style.Id == "normal").IndentLevel = 2;
            var host = new StackPanel(); BlockRenderer.Rebuild(host, vm, []);
            var window = new Window { Content = host, Width = 700, Height = 500 };
            try
            {
                window.Show(); Pump(window);
                var input = Assert.Single(host.GetVisualDescendants().OfType<TextBox>());
                var owner = isList ? input.GetVisualAncestors().OfType<Grid>().First() : (Control)input;
                Assert.Equal(isList ? 56 : 40, owner.Margin.Left);
                Assert.Equal(isList ? 15 : 12, owner.Margin.Top);
                Assert.Equal(isList ? 23 : 20, owner.Margin.Bottom);
                row.SpaceBefore = row.SpaceAfter = 0; row.IndentLevel = 0;
                BlockRenderer.Rebuild(host, vm, []); Pump(window);
                input = Assert.Single(host.GetVisualDescendants().OfType<TextBox>());
                owner = isList ? input.GetVisualAncestors().OfType<Grid>().First() : (Control)input;
                Assert.Equal(isList ? 16 : 0, owner.Margin.Left);
                Assert.Equal(isList ? 3 : 0, owner.Margin.Top);
                Assert.Equal(isList ? 3 : 0, owner.Margin.Bottom);
            }
            finally { window.Close(); }
        });
    }

    [Theory]
    [InlineData(HavenRichBlockKind.Checklist)]
    [InlineData(HavenRichBlockKind.BulletList)]
    [InlineData(HavenRichBlockKind.NumberedList)]
    public async Task Enter_split_keeps_raw_parent_spacing_inheritance(HavenRichBlockKind kind)
    {
        var root = Path.Combine(Path.GetTempPath(), "list-split-inherit-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            using var store = new JsonFileHavenBoardStore(root); var path = Path.Combine(root, "split.9to1board");
            await using (var seed = await RichBoardSession.CreateNewAsync(store, "Split"))
            {
                await seed.MutateAsync(notes => notes.Sections[0].Pages[0].Blocks =
                    [new HavenRichBlock { Id = "list", Kind = kind, LineSpacing = 2,
                        Items = [new HavenRichListItem { Id = "row", Text = "AB" }] }]);
                await seed.SaveAsAsync(path);
            }
            await using (var adapter = await ContractSessionAdapter.OpenAsync(store, path))
            {
                using var vm = new BoardsViewModel(adapter);
                if (!vm.IsEditMode) await vm.DispatchAsync("ToggleEditView", null);
                Assert.True(await vm.SplitTextBlockAtAsync("list:row", 1));
                await adapter.SaveAsync();
                var rows = (await adapter.GetDetachedContractDocumentAsync()).RichNotes!.Sections[0].Pages[0].Blocks.Single().Items;
                Assert.Equal(2, rows.Count); Assert.All(rows, row => Assert.Equal(0, row.LineSpacing));
            }
            await using (var owner = await RichBoardSession.OpenAtPathAsync(store, path))
            {
                await owner.MutateAsync(notes => notes.Sections[0].Pages[0].Blocks.Single().LineSpacing = 3);
                await owner.SaveAsync();
            }
            await using var reopened = await ContractSessionAdapter.OpenAsync(store, path);
            Assert.All(reopened.Document.Sections[0].Pages[0].Blocks, row => Assert.Equal(3, row.LineSpacing));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task Existing_list_marks_remain_explicit_when_style_has_different_marks()
    {
        await using var session = new InMemoryRichBoardSession();
        await session.OpenAsync("memory://list-explicit-marks");
        TestUiThread.Run(() =>
        {
            using var vm = new BoardsViewModel(session);
            var row = new RichBoardBlock { Id = "list:row", Kind = "bulleted", Text = "Explicit" };
            session.Document.Sections[0].Pages[0].Blocks = [row];
            var style = vm.StyleList.Single(value => value.Id == "normal");
            style.Bold = style.Italic = style.Underline = style.Strike = true; style.Baseline = "superscript";
            var host = new StackPanel(); BlockRenderer.Rebuild(host, vm, []);
            var window = new Window { Content = host, Width = 700, Height = 500 };
            try
            {
                window.Show(); Pump(window);
                var input = Assert.Single(host.GetVisualDescendants().OfType<TextBox>());
                var presenter = Assert.Single(input.GetVisualDescendants().OfType<CuiFormattedTextPresenter>());
                var shaped = presenter.TextLayout.TextLines.SelectMany(line => line.TextRuns).OfType<ShapedTextRun>().First().Properties;
                Assert.Equal(FontWeight.Normal, shaped.Typeface.Weight); Assert.Equal(FontStyle.Normal, shaped.Typeface.Style);
                Assert.Equal(BaselineAlignment.Baseline, shaped.BaselineAlignment);
                Assert.True(shaped.TextDecorations is null || shaped.TextDecorations.Count == 0);
            }
            finally { window.Close(); }
        });
    }

    private static void Pump(Window window)
    {
        window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
    }
}
