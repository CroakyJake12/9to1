using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.VisualTree;
using CakeOS.Apps.Boards.Contract;
using CakeOS.Cui.Runtime;
using CakeOS.Cui.Themes;
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
            var addItem = Assert.Single(host.GetLogicalDescendants().OfType<Button>(), button =>
                AutomationProperties.GetName(button) == "Add checklist item");
            var addItemHiddenOpacity = addItem.Opacity;
            Assert.True(addItem.Focus());
            var addItemFocusedOpacity = addItem.Opacity;
            var gap = text.Bounds.Left - check.Bounds.Right;
            var textHeight = text.Bounds.Height;
            var textColumn = Grid.GetColumn(text);
            var columnDefinition = row.ColumnDefinitions[textColumn];
            var checkboxColumnWidth = row.ColumnDefinitions[Grid.GetColumn(check)].Width.Value;
            var checkWidth = check.Bounds.Width;
            var checkTop = check.Bounds.Top;
            var textTop = text.Bounds.Top;
            var textPaddingLeft = text.Padding.Left;
            window.Close();
            return (Gap: gap, TextHeight: textHeight, ColumnWidth: columnDefinition.Width,
                CheckboxColumnWidth: checkboxColumnWidth, CheckWidth: checkWidth, TextPaddingLeft: textPaddingLeft,
                CheckTop: checkTop, TextTop: textTop, TextWrapping: text.TextWrapping,
                AddItemHiddenOpacity: addItemHiddenOpacity, AddItemFocusedOpacity: addItemFocusedOpacity);
        });

        Assert.InRange(actual.Gap, 5.5, 6.5);
        Assert.Equal(24, actual.CheckWidth);
        Assert.Equal(30, actual.CheckboxColumnWidth);
        Assert.Equal(0, actual.TextPaddingLeft);
        Assert.Equal(actual.TextTop + 7, actual.CheckTop);
        Assert.Equal(0, actual.AddItemHiddenOpacity);
        Assert.Equal(1, actual.AddItemFocusedOpacity);
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
            return (Column: grid.ColumnDefinitions[0].Width, SecondColumn: grid.ColumnDefinitions[1].Width, FirstSlotWidth: slots[0].Width,
                SecondSlotWidth: slots[1].Width,
                HasInsert: grid.GetLogicalDescendants().OfType<Button>().Any(button => AutomationProperties.GetName(button) == "Insert"),
                HasGrip: grid.GetLogicalDescendants().OfType<Button>().Any(button => button.Name == "BlockGripToggle"));
        });

        Assert.Equal(32, actual.Column.Value);
        Assert.Equal(32, actual.SecondColumn.Value);
        Assert.Equal(32, actual.FirstSlotWidth);
        // View reserves the same two 32-unit targets as Edit, without active controls.
        Assert.Equal(32, actual.SecondSlotWidth);
        Assert.False(actual.HasInsert);
        Assert.False(actual.HasGrip);
    }

    [Fact]
    public async Task Absolute_blocks_render_in_the_free_layer_while_flow_blocks_keep_document_order()
    {
        await using var session = new InMemoryRichBoardSession();
        await session.OpenAsync("memory://boards/absolute-block-layer");
        var page = session.Document.Sections[0].Pages[0];
        page.LayoutMode = HavenRichPageLayoutModes.Unlocked;
        var absolute = page.Blocks[0];
        absolute.Id = "absolute-note";
        absolute.Text = "Placed note";
        absolute.Layout = HavenRichBlockLayouts.Absolute;
        absolute.X = 84;
        absolute.Y = 124;
        absolute.Width = 320;
        absolute.Height = 144;
        absolute.ZOrder = 2;
        page.Blocks.Clear();
        page.Blocks.Add(absolute);
        page.Blocks.Add(new RichBoardBlock
        {
            Id = "flow-note",
            Kind = "paragraph",
            Text = "Flow note",
        });
        page.Blocks.Add(new RichBoardBlock
        {
            Id = "flow-note-later",
            Kind = "paragraph",
            Text = "Later flow note",
        });
        var viewModel = new BoardsViewModel(session);

        var actual = TestUiThread.Run(() =>
        {
            var flowHost = new StackPanel();
            var absoluteContainer = new CuiContainerControl("Absolute");
            Assert.True(CuiLayoutProperties.SetLiteral(absoluteContainer, "XInherited", "True"));
            Assert.True(CuiLayoutProperties.SetLiteral(absoluteContainer, "YInherited", "True"));
            var absoluteHost = absoluteContainer.Layout;
            var surface = new Grid { RowDefinitions = new RowDefinitions("Auto") };
            surface.Children.Add(flowHost);
            surface.Children.Add(absoluteContainer);
            var window = new Window { Width = 620, Height = 520, Content = surface };
            BlockRenderer.Rebuild(flowHost, viewModel, [], absoluteHost);
            window.Show();
            window.Measure(new Size(620, 520));
            window.Arrange(new Rect(0, 0, 620, 520));
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();

            var flowRow = Assert.Single(flowHost.GetVisualDescendants().OfType<Border>(), border =>
                border.Tag is BlockReorderRow row && row.BlockId == "flow-note");
            Assert.DoesNotContain(flowHost.GetVisualDescendants().OfType<Border>(), border =>
                border.Tag is BlockReorderRow row && row.BlockId == "absolute-note");
            var absoluteRow = Assert.Single(absoluteHost.Children.OfType<Border>(), border =>
                border.Tag is BlockReorderRow row && row.BlockId == "absolute-note");
            Assert.DoesNotContain(absoluteHost.Children.OfType<Border>(), border =>
                border.Tag is BlockReorderRow row && row.BlockId == "flow-note");
            var absoluteText = Assert.Single(absoluteRow.GetVisualDescendants().OfType<TextBox>(), box => box.Name == "t_absolute-note");
            var initialTextPoint = absoluteText.TranslatePoint(new Point(0, 0), absoluteHost);
            Assert.NotNull(initialTextPoint);
            var initialHostSize = absoluteHost.Bounds.Size;

            window.Width = 820;
            window.Height = 620;
            window.Measure(new Size(820, 620));
            window.Arrange(new Rect(0, 0, 820, 620));
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            var resizedTextPoint = absoluteText.TranslatePoint(new Point(0, 0), absoluteHost);
            Assert.NotNull(resizedTextPoint);
            var result = (FlowText: Assert.Single(flowRow.GetVisualDescendants().OfType<TextBox>(), box => box.Name == "t_flow-note").Text,
                InitialTextX: initialTextPoint.Value.X, InitialTextY: initialTextPoint.Value.Y,
                ResizedTextX: resizedTextPoint.Value.X, ResizedTextY: resizedTextPoint.Value.Y,
                InitialHostWidth: initialHostSize.Width, ResizedHostWidth: absoluteHost.Bounds.Width,
                AbsoluteWidth: absoluteRow.Bounds.Width, AbsoluteHeight: absoluteRow.Bounds.Height,
                FlowBlockIds: flowHost.GetVisualDescendants().OfType<Border>()
                    .Select(border => border.Tag).OfType<BlockReorderRow>().Select(row => row.BlockId).ToArray(),
                AbsoluteCount: absoluteHost.Children.OfType<Border>().Count(border => border.Tag is BlockReorderRow));
            window.Close();
            return result;
        });

        Assert.Equal("Flow note", actual.FlowText);
        Assert.Equal(84, actual.InitialTextX);
        Assert.Equal(124, actual.InitialTextY);
        Assert.Equal(actual.InitialTextX, actual.ResizedTextX);
        Assert.Equal(actual.InitialTextY, actual.ResizedTextY);
        Assert.True(actual.ResizedHostWidth > actual.InitialHostWidth);
        Assert.Equal(396, actual.AbsoluteWidth);
        Assert.Equal(148, actual.AbsoluteHeight);
        Assert.Equal(new[] { "flow-note", "flow-note-later" }, actual.FlowBlockIds);
        Assert.Equal(1, actual.AbsoluteCount);
    }

    [Fact]
    public async Task Table_defaults_follow_active_CUI_palette_and_keep_authored_cell_colours()
    {
        var priorMode = BoardsTheme.Mode;
        var priorTheme = CuiSurfacePaletteCatalog.ActiveTheme;
        try
        {
            CuiSurfacePaletteCatalog.ActiveTheme = CuiTheme.Glow;
            BoardsTheme.SetMode(BoardsThemeMode.Dark);
            await using var session = new InMemoryRichBoardSession();
            await session.OpenAsync("memory://boards/table-active-theme");
            var viewModel = new BoardsViewModel(session);
            var block = new RichBoardBlock
            {
                Id = "theme-table",
                Kind = "table",
                TableRows = 2,
                TableCols = 2,
                TableStyle = new RichTableStyleView { HasHeaderRow = true, AlternatingRows = true },
                TableCells = new Dictionary<string, string>
                {
                    ["0,0"] = "Header", ["0,1"] = "Header 2", ["1,0"] = "Alternate", ["1,1"] = "Authored",
                },
                CellStyles = new Dictionary<string, RichCellStyleView>
                {
                    ["1,1"] = new() { Background = "#FF123456", Foreground = "#FFAABBCC" },
                },
            };
            var overrideBlock = new RichBoardBlock
            {
                Id = "override-table",
                Kind = "table",
                TableRows = 2,
                TableCols = 2,
                TableStyle = new RichTableStyleView
                {
                    Background = "#FF654321",
                    HasHeaderRow = true,
                    AlternatingRows = true,
                },
                TableCells = new Dictionary<string, string>
                {
                    ["0,0"] = "Header", ["0,1"] = "Header 2", ["1,0"] = "Inherited", ["1,1"] = "Cell override",
                },
                CellStyles = new Dictionary<string, RichCellStyleView>
                {
                    ["1,1"] = new() { Background = "#FF222222" },
                },
            };
            session.Document.Sections[0].Pages[0].Blocks.Clear();
            session.Document.Sections[0].Pages[0].Blocks.Add(block);
            session.Document.Sections[0].Pages[0].Blocks.Add(overrideBlock);
            var palette = BoardsTheme.SharedPalette;
            var actual = TestUiThread.Run(() =>
            {
                var host = new StackPanel { Width = 680 };
                BlockRenderer.Rebuild(host, viewModel, []);
                var window = new Window { Width = 700, Height = 500, Content = host };
                window.Show();
                window.Measure(new Size(700, 500));
                window.Arrange(new Rect(0, 0, 700, 500));
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                var cells = host.GetVisualDescendants().OfType<TextBox>()
                    .Where(box => box.Name?.StartsWith("cell_", StringComparison.Ordinal) == true)
                    .ToDictionary(box => box.Name!, StringComparer.Ordinal);
                var cellsByAppearance = cells.ToDictionary(pair => pair.Key, pair =>
                    (Background: ColorOf(((Border)pair.Value.Parent!).Background),
                        Foreground: ColorOf(pair.Value.Foreground)),
                    StringComparer.Ordinal);
                var tableText = ColorOf(BoardsTheme.ContrastText("#FF654321"));
                var cellText = ColorOf(BoardsTheme.ContrastText("#FF222222"));
                var tableFrame = Assert.Single(host.GetVisualDescendants().OfType<Border>(), border =>
                    border.Name == "BoardTableFrame_override-table");
                var tableClip = Assert.IsType<Border>(tableFrame.Child);
                var frameClips = tableFrame.ClipToBounds;
                var contentClips = tableClip.ClipToBounds;
                var outerBounds = tableFrame.Bounds;
                var innerBounds = tableClip.Bounds;
                window.Close();
                return (Cells: cellsByAppearance, TableText: tableText, CellText: cellText,
                    OuterClips: frameClips, InnerClips: contentClips, OuterBounds: outerBounds, InnerBounds: innerBounds);
            });

            Assert.Equal<Color?>(palette.AccentSoft, actual.Cells["cell_theme-table_0_0"].Background);
            Assert.Equal<Color?>(palette.Panel3, actual.Cells["cell_theme-table_1_0"].Background);
            Assert.Equal<Color?>(Color.Parse("#FF123456"), actual.Cells["cell_theme-table_1_1"].Background);
            Assert.Equal<Color?>(Color.Parse("#FFAABBCC"), actual.Cells["cell_theme-table_1_1"].Foreground);

            Assert.Equal<Color?>(Color.Parse("#FF654321"), actual.Cells["cell_override-table_0_0"].Background);
            Assert.Equal<Color?>(Color.Parse("#FF654321"), actual.Cells["cell_override-table_1_0"].Background);
            Assert.Equal<Color?>(Color.Parse("#FF222222"), actual.Cells["cell_override-table_1_1"].Background);
            Assert.Equal(actual.TableText, actual.Cells["cell_override-table_0_0"].Foreground);
            Assert.Equal(actual.CellText, actual.Cells["cell_override-table_1_1"].Foreground);
            Assert.False(actual.OuterClips);
            Assert.True(actual.InnerClips);
            Assert.True(actual.OuterBounds.Width >= actual.InnerBounds.Width);
            Assert.True(actual.OuterBounds.Height >= actual.InnerBounds.Height);
        }
        finally
        {
            BoardsTheme.SetMode(priorMode);
            CuiSurfacePaletteCatalog.ActiveTheme = priorTheme;
        }
    }

    private static Color? ColorOf(IBrush? brush) => brush is ISolidColorBrush solid ? solid.Color : null;
}
