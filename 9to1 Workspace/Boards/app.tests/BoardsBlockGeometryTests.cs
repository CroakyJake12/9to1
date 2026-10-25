using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using CakeOS.Apps.Boards.Contract;
using Xunit;

namespace CakeOS.Apps.Boards.App.Tests;

[Collection(BoardSessionTestCollection.Name)]
public sealed class BoardsBlockGeometryTests
{
    [Fact]
    public async Task Block_gutter_targets_stay_compact_and_top_aligned_beside_a_tall_table()
    {
        await using var session = new InMemoryRichBoardSession();
        await session.OpenAsync("memory://boards/block-gutter-geometry");
        var viewModel = new BoardsViewModel(session);
        var page = session.Document.Sections[0].Pages[0];
        page.Blocks.Clear();
        page.Blocks.Add(new RichBoardBlock { Id = "short-note", Kind = "paragraph", Text = "Short note" });
        var table = new RichBoardBlock
        {
            Id = "tall-table",
            Kind = "table",
            TableRows = 8,
            TableCols = 2,
            TableCells = new Dictionary<string, string>(),
        };
        for (var row = 0; row < table.TableRows; row++)
            table.TableCells[$"{row},0"] = string.Join(" ", Enumerable.Repeat("A table cell with enough text to wrap onto several lines.", 5));
        page.Blocks.Add(table);
        viewModel.FocusBlock("tall-table");

        var actual = TestUiThread.Run(() =>
        {
            var host = new StackPanel { Width = 680 };
            BlockRenderer.Rebuild(host, viewModel, []);
            var window = new Window { Width = 700, Height = 1000, Content = host };
            window.Show();
            window.Measure(new Size(700, 1000));
            window.Arrange(new Rect(0, 0, 700, 1000));
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();

            Border Row(string id) => Assert.Single(host.GetLogicalDescendants().OfType<Border>(), border =>
                border.Tag is BlockReorderRow row && row.BlockId == id);
            (double Height, double InsertHeight, double InsertTop, double GripHeight, double GripTop, double Opacity,
                string InsertLayout) MeasureRow(string id)
            {
                var border = Row(id);
                var grid = Assert.IsType<Grid>(border.Child);
                var insert = Assert.Single(grid.Children.OfType<Button>(), button =>
                    AutomationProperties.GetName(button) == "Insert");
                var gripButton = Assert.Single(border.GetLogicalDescendants().OfType<Button>(), button => button.Name == "BlockGripToggle");
                var insertLayout = $"Width={insert.Width}, Height={insert.Height}, MinHeight={insert.MinHeight}, " +
                    $"MaxHeight={insert.MaxHeight}, Bounds={insert.Bounds}, Padding={insert.Padding}, " +
                    $"BorderThickness={insert.BorderThickness}, Margin={insert.Margin}";
                return (border.Bounds.Height, insert.Bounds.Height, insert.Bounds.Top,
                    gripButton.Bounds.Height, gripButton.Bounds.Top, insert.Opacity, insertLayout);
            }

            var shortRow = MeasureRow("short-note");
            var tallRow = MeasureRow("tall-table");
            var shortInsert = Assert.Single(Row("short-note").GetLogicalDescendants().OfType<Button>(), button =>
                AutomationProperties.GetName(button) == "Insert");
            Assert.True(shortInsert.Focus());
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            var focusedOpacity = shortInsert.Opacity;
            window.Close();
            return (shortRow, tallRow, focusedOpacity);
        });

        Assert.True(actual.tallRow.Height > actual.shortRow.Height + 100,
            $"Expected table row to be substantially taller; short={actual.shortRow.Height}, tall={actual.tallRow.Height}.");
        Assert.True(actual.shortRow.InsertHeight == 32,
            $"Expected a 32px insert target beside a short block; actual={actual.shortRow.InsertHeight}; {actual.shortRow.InsertLayout}.");
        Assert.True(actual.tallRow.InsertHeight == 32,
            $"Expected a 32px insert target beside a tall block; actual={actual.tallRow.InsertHeight}; {actual.tallRow.InsertLayout}.");
        Assert.Equal(32, actual.tallRow.GripHeight);
        Assert.InRange(actual.tallRow.InsertTop, 0, 2);
        Assert.InRange(actual.tallRow.GripTop, 0, 2);
        Assert.Equal(0.56, actual.shortRow.Opacity, precision: 2);
        Assert.Equal(1, actual.tallRow.Opacity);
        Assert.Equal(1, actual.focusedOpacity);
    }

    [Fact]
    public async Task Checklist_label_wraps_in_its_own_column_with_a_compact_touchable_checkbox_gap()
    {
        await using var session = new InMemoryRichBoardSession();
        await session.OpenAsync("memory://boards/checklist-label-geometry");
        var viewModel = new BoardsViewModel(session);
        var label = string.Join(" ", Enumerable.Repeat("A wrapped checklist label stays aligned with its first line.", 8));
        session.Document.Sections[0].Pages[0].Blocks.Clear();
        session.Document.Sections[0].Pages[0].Blocks.Add(new RichBoardBlock
        {
            Id = "long-checklist-label", Kind = "checklist", Text = label,
        });

        var actual = TestUiThread.Run(() =>
        {
            var host = new StackPanel { Width = 380 };
            BlockRenderer.Rebuild(host, viewModel, []);
            var window = new Window { Width = 400, Height = 420, Content = host };
            window.Show();
            window.Measure(new Size(400, 420));
            window.Arrange(new Rect(0, 0, 400, 420));
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            var check = Assert.Single(host.GetLogicalDescendants().OfType<CheckBox>(), item => item.Name == "done_long-checklist-label");
            var text = Assert.Single(host.GetLogicalDescendants().OfType<TextBox>(), item => item.Name == "chk_long-checklist-label");
            var row = Assert.IsType<Grid>(check.Parent);
            var gap = text.Bounds.Left - check.Bounds.Right;
            var textHeight = text.Bounds.Height;
            var textColumn = Grid.GetColumn(text);
            var columnDefinition = row.ColumnDefinitions[textColumn];
            var checkWidth = check.Bounds.Width;
            window.Close();
            return (Gap: gap, TextHeight: textHeight, ColumnWidth: columnDefinition.Width, CheckWidth: checkWidth,
                TextWrapping: text.TextWrapping);
        });

        Assert.InRange(actual.Gap, 8, 12);
        Assert.Equal(24, actual.CheckWidth);
        Assert.True(actual.TextHeight > 36, $"Expected the long label to wrap; height={actual.TextHeight}.");
        Assert.Equal(GridUnitType.Star, actual.ColumnWidth.GridUnitType);
        Assert.Equal(Avalonia.Media.TextWrapping.Wrap, actual.TextWrapping);
    }

    [Fact]
    public async Task View_mode_reserves_the_gutter_without_rendering_edit_actions()
    {
        await using var session = new InMemoryRichBoardSession();
        await session.OpenAsync("memory://boards/view-gutter-geometry");
        var viewModel = new BoardsViewModel(session);
        await viewModel.DispatchAsync("ToggleEditView", null);
        Assert.False(viewModel.IsEditMode);

        var actual = TestUiThread.Run(() =>
        {
            var host = new StackPanel { Width = 520 };
            BlockRenderer.Rebuild(host, viewModel, []);
            var row = Assert.Single(host.GetLogicalDescendants().OfType<Border>(), border =>
                border.Tag is BlockReorderRow block && block.BlockId == viewModel.PageBlocks[0].Id);
            var grid = Assert.IsType<Grid>(row.Child);
            var slots = grid.Children.OfType<Border>().ToArray();
            Assert.Equal(2, slots.Length);
            return (Column: grid.ColumnDefinitions[0].Width, FirstSlotWidth: slots[0].Width,
                SecondSlotWidth: slots[1].Width,
                HasInsert: grid.GetLogicalDescendants().OfType<Button>().Any(button => AutomationProperties.GetName(button) == "Insert"),
                HasGrip: grid.GetLogicalDescendants().OfType<Button>().Any(button => button.Name == "BlockGripToggle"));
        });

        Assert.Equal(32, actual.Column.Value);
        Assert.Equal(32, actual.FirstSlotWidth);
        Assert.Equal(36, actual.SecondSlotWidth);
        Assert.False(actual.HasInsert);
        Assert.False(actual.HasGrip);
    }
}
