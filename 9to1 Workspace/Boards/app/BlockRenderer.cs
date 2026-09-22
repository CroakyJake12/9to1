// BlockRenderer builds per-block editors into BlocksHost from BoardsViewModel state.
// Control names encode ids (t_{blockId}, done_/chk_ checklist names,
// cell_{blockId}_{r}_{c}, gx_/gv_/gc_/gvp_/iw_/ialt_/dvth_/dvcl_) so the
// ViewModel routes edits without hardcoding. Contract block/item ids never
// contain '_', so names split on the LAST '_' (see BoardsViewModel).

using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using CakeOS.Apps.Boards.Contract;

namespace CakeOS.Apps.Boards.App;

public static class BlockRenderer
{
    private const double GraphWidth = 420;
    private const double GraphHeight = 260;

    public static async Task RebuildAsync(StackPanel host, BoardsViewModel vm)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(vm);
        var canvas = await vm.GetCanvasObjectsAsync().ConfigureAwait(false);
        Rebuild(host, vm, canvas);
    }

    public static void Rebuild(StackPanel host, BoardsViewModel vm, IReadOnlyList<CanvasBoxView> canvasBoxes)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(vm);
        host.Children.Clear();

        var blocks = vm.PageBlocks;
        var index = 0;
        while (index < blocks.Count)
        {
            var block = blocks[index];
            if (block.Kind == "checklist")
            {
                var run = new List<RichBoardBlock>();
                while (index < blocks.Count && blocks[index].Kind == "checklist")
                    run.Add(blocks[index++]);
                host.Children.Add(BuildChecklistRun(vm, run));
                continue;
            }
            host.Children.Add(BuildBlock(vm, block));
            index++;
        }

        host.Children.Add(BuildFreeformSection(vm, canvasBoxes ?? []));
    }

    // ----- Block dispatch -----

    private static Control BuildBlock(BoardsViewModel vm, RichBoardBlock block) => block.Kind switch
    {
        "heading" or "paragraph" => BuildTextBlock(vm, block),
        "table" => BuildTable(vm, block),
        "divider" => BuildDivider(vm, block),
        "image" => BuildImage(vm, block),
        "graph" => BuildGraph(vm, block),
        "ink" => BuildInk(vm, block),
        _ => BuildTextBlock(vm, block),
    };

    private static StackPanel BlockShell(BoardsViewModel vm, RichBoardBlock block, string title)
    {
        var shell = new StackPanel { Orientation = Orientation.Vertical, Margin = new Thickness(0, 0, 0, 12) };
        var header = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
        var label = new TextBlock
        {
            Text = title,
            FontSize = 13,
            FontWeight = FontWeight.SemiBold,
            Foreground = new SolidColorBrush(Color.Parse("#FF757575")),
            VerticalAlignment = VerticalAlignment.Center,
        };
        AutomationProperties.SetName(label, title + " section");
        var styleBadge = new TextBlock
        {
            Text = "  [" + (vm.StyleName(block.StyleId) ?? block.StyleId) + "]",
            FontSize = 12,
            Foreground = new SolidColorBrush(Color.Parse("#FF9AA0A6")),
            VerticalAlignment = VerticalAlignment.Center,
        };
        var delete = new Button { Content = "Delete", Margin = new Thickness(12, 0, 0, 0) };
        ToolTip.SetTip(delete, "Delete this block");
        AutomationProperties.SetName(delete, "Delete " + title + " block");
        delete.Click += async (_, _) => await vm.DeleteBlockAsync(block.Id);
        header.Children.Add(label);
        header.Children.Add(styleBadge);
        header.Children.Add(delete);
        shell.Children.Add(header);
        if (!string.IsNullOrEmpty(block.AttachmentId))
            shell.Children.Add(BuildAttachmentRow(vm, block));
        return shell;
    }

    // ----- Text -----

    private static Control BuildTextBlock(BoardsViewModel vm, RichBoardBlock block)
    {
        var isHeading = block.Kind == "heading";
        var shell = BlockShell(vm, block, isHeading ? "Heading" : "Paragraph");
        var box = new TextBox
        {
            Name = "t_" + block.Id,
            Text = block.Text ?? string.Empty,
            FontSize = block.FontSize > 0 ? block.FontSize : isHeading ? 20 : 14,
            FontWeight = block.Bold || isHeading ? FontWeight.Bold : FontWeight.Normal,
            FontStyle = block.Italic ? FontStyle.Italic : FontStyle.Normal,
            AcceptsReturn = !isHeading,
            TextWrapping = TextWrapping.Wrap,
            MinWidth = 500,
        };
        if (!string.IsNullOrEmpty(block.FontFamily))
            box.FontFamily = new FontFamily(block.FontFamily);
        box.TextAlignment = ParseAlignment(block.Alignment);
        if (TryBrush(block.Background, out var background))
            box.Background = background;
        if (TryBrush(block.Foreground, out var foreground))
            box.Foreground = foreground;
        ToolTip.SetTip(box, (isHeading ? "Heading" : "Paragraph") + " text");
        AutomationProperties.SetName(box, (isHeading ? "Heading" : "Paragraph") + " editor");
        var captured = block.Id;
        box.TextChanged += (_, _) => vm.EditText("t_" + captured, box.Text ?? string.Empty);
        box.GotFocus += (_, _) => vm.FocusBlock(captured);
        shell.Children.Add(box);
        var flags = new List<string>();
        if (block.Strike) flags.Add("strike");
        if (block.Underline) flags.Add("underline");
        if (block.Baseline is "subscript" or "superscript") flags.Add(block.Baseline);
        if (block.IndentLevel >= 0) flags.Add("indent " + block.IndentLevel);
        if (flags.Count > 0)
            shell.Children.Add(new TextBlock
            {
                Text = string.Join(" · ", flags),
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.Parse("#FF9AA0A6")),
            });
        return shell;
    }

    // ----- Checklist -----

    private static Control BuildChecklistRun(BoardsViewModel vm, List<RichBoardBlock> run)
    {
        var shell = new StackPanel { Orientation = Orientation.Vertical, Margin = new Thickness(0, 0, 0, 12) };
        var header = new TextBlock
        {
            Text = "Checklist (" + run.Count + " items)",
            FontSize = 13,
            FontWeight = FontWeight.SemiBold,
            Foreground = new SolidColorBrush(Color.Parse("#FF757575")),
            Margin = new Thickness(0, 0, 0, 4),
        };
        AutomationProperties.SetName(header, "Checklist with " + run.Count + " items");
        shell.Children.Add(header);
        foreach (var item in run)
            shell.Children.Add(BuildChecklistRow(vm, item));
        var parentKey = ParentKey(run[0].Id);
        var add = new Button { Content = "+ Add item", Margin = new Thickness(0, 4, 0, 0) };
        ToolTip.SetTip(add, "Add a checklist item");
        AutomationProperties.SetName(add, "Add checklist item");
        add.Click += async (_, _) => await vm.AddChecklistItemAsync(parentKey);
        shell.Children.Add(add);
        return shell;
    }

    private static Control BuildChecklistRow(BoardsViewModel vm, RichBoardBlock item)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 2) };
        var suffix = NameSuffix(item.Id);
        var check = new CheckBox
        {
            Name = "done_" + suffix,
            IsChecked = item.IsChecked,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0),
        };
        AutomationProperties.SetName(check, "Checklist item done");
        var text = new TextBox
        {
            Name = "chk_" + suffix,
            Text = item.Text ?? string.Empty,
            FontSize = 14,
            Width = 460,
            FontWeight = item.Bold ? FontWeight.Bold : FontWeight.Normal,
            FontStyle = item.Italic ? FontStyle.Italic : FontStyle.Normal,
        };
        ToolTip.SetTip(text, "Checklist item text (Enter adds next, empty Backspace removes)");
        AutomationProperties.SetName(text, "Checklist item text");
        var erase = new Button { Content = "✕", Margin = new Thickness(8, 0, 0, 0) };
        ToolTip.SetTip(erase, "Remove this item");
        AutomationProperties.SetName(erase, "Remove checklist item");
        var viewId = item.Id;
        check.IsCheckedChanged += (_, _) => vm.EditCheck("done_" + suffix, check.IsChecked == true);
        text.TextChanged += (_, _) => vm.EditText("chk_" + suffix, text.Text ?? string.Empty);
        text.GotFocus += (_, _) => vm.FocusBlock(viewId);
        text.KeyDown += async (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                await vm.ChecklistEnterAsync(viewId);
            }
            else if (e.Key == Key.Back && string.IsNullOrEmpty(text.Text))
            {
                e.Handled = true;
                await vm.ChecklistBackspaceAsync(viewId, text.Text ?? string.Empty);
            }
            else if (e.Key == Key.Tab)
            {
                e.Handled = true;
                vm.ChecklistIndent(viewId, e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? -1 : 1);
            }
        };
        erase.Click += async (_, _) => await vm.DeleteBlockAsync(viewId);
        row.Children.Add(check);
        row.Children.Add(text);
        row.Children.Add(erase);
        return row;
    }

    // ----- Table -----

    private static Control BuildTable(BoardsViewModel vm, RichBoardBlock block)
    {
        var shell = BlockShell(vm, block, "Table");
        var rows = block.TableRows > 0 ? block.TableRows : DerivedMax(block, row: true) + 1;
        var cols = block.TableCols > 0 ? block.TableCols : DerivedMax(block, row: false) + 1;
        rows = Math.Clamp(rows, 1, 100);
        cols = Math.Clamp(cols, 1, 50);

        var grid = new Grid { Margin = new Thickness(0, 4, 0, 4) };
        for (var r = 0; r < rows; r++)
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        for (var c = 0; c < cols; c++)
            grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));

        for (var r = 0; r < rows; r++)
            for (var c = 0; c < cols; c++)
            {
                var cellPanel = new StackPanel { Orientation = Orientation.Vertical, Margin = new Thickness(2) };
                var key = $"{r},{c}";
                block.TableCells.TryGetValue(key, out var cellText);
                block.CellStyles.TryGetValue(key, out var cellStyle);
                var box = new TextBox
                {
                    Name = $"cell_{block.Id}_{r}_{c}",
                    Text = cellText ?? string.Empty,
                    FontSize = 14,
                    Width = 130,
                    FontWeight = cellStyle?.Bold == true ? FontWeight.Bold : FontWeight.Normal,
                    FontStyle = cellStyle?.Italic == true ? FontStyle.Italic : FontStyle.Normal,
                };
                AutomationProperties.SetName(box, $"Table cell row {r + 1} column {c + 1}");
                if (cellStyle is not null && TryBrush(cellStyle.Background, out var cellBackground))
                    box.Background = cellBackground;
                var rr = r;
                var cc = c;
                var captured = block.Id;
                box.TextChanged += (_, _) => vm.EditText($"cell_{captured}_{rr}_{cc}", box.Text ?? string.Empty);
                box.GotFocus += (_, _) => vm.FocusBlock(captured);
                var tools = new StackPanel { Orientation = Orientation.Horizontal };
                var align = new ComboBox
                {
                    ItemsSource = new List<string> { "inherit", "left", "center", "right", "justify" },
                    SelectedItem = string.IsNullOrEmpty(cellStyle?.AlignH) ? "inherit" : cellStyle!.AlignH,
                    Width = 88,
                    Margin = new Thickness(0, 2, 4, 0),
                };
                ToolTip.SetTip(align, "Cell horizontal alignment");
                AutomationProperties.SetName(align, $"Cell {r + 1},{c + 1} alignment");
                align.SelectionChanged += (_, _) =>
                    vm.SetCellAlign(captured, rr, cc, align.SelectedItem?.ToString() ?? "inherit");
                var bold = new Button { Content = "B", Margin = new Thickness(0, 2, 0, 0) };
                ToolTip.SetTip(bold, "Toggle cell bold");
                AutomationProperties.SetName(bold, $"Cell {r + 1},{c + 1} bold");
                bold.Click += (_, _) => vm.ToggleCellBold(captured, rr, cc);
                tools.Children.Add(align);
                tools.Children.Add(bold);
                cellPanel.Children.Add(box);
                cellPanel.Children.Add(tools);
                Grid.SetRow(cellPanel, r);
                Grid.SetColumn(cellPanel, c);
                grid.Children.Add(cellPanel);
            }
        shell.Children.Add(grid);

        var structure = new StackPanel { Orientation = Orientation.Horizontal };
        AddSmallButton(structure, "+ Row", "Add table row", () => vm.TableAddRow(block.Id));
        AddSmallButton(structure, "− Row", "Delete last table row", () => vm.TableDelRow(block.Id));
        AddSmallButton(structure, "+ Col", "Add table column", () => vm.TableAddCol(block.Id));
        AddSmallButton(structure, "− Col", "Delete last table column", () => vm.TableDelCol(block.Id));
        shell.Children.Add(structure);
        return shell;
    }

    // ----- Divider -----

    private static Control BuildDivider(BoardsViewModel vm, RichBoardBlock block)
    {
        var shell = BlockShell(vm, block, "Divider");
        var divider = block.Divider ?? new RichDividerView();
        if (divider.LineStyle != "none")
        {
            if (divider.LineStyle is "dashed" or "dotted")
            {
                shell.Children.Add(new TextBlock
                {
                    Text = divider.LineStyle == "dashed" ? "— — — — — — —" : "· · · · · · · · ·",
                    FontSize = 14,
                    Foreground = ParseBrushOrDefault(divider.Color, "#FF5F6368"),
                    Margin = new Thickness(0, 4, 0, 4),
                });
            }
            else
            {
                shell.Children.Add(new Rectangle
                {
                    Height = Math.Clamp(divider.Thickness, 1, 12),
                    Fill = ParseBrushOrDefault(divider.Color, "#FF5F6368"),
                    Margin = new Thickness(0, 4, 0, 4),
                    Width = 560,
                    HorizontalAlignment = HorizontalAlignment.Left,
                });
            }
        }
        var editors = new StackPanel { Orientation = Orientation.Horizontal };
        var thickness = new TextBox
        {
            Name = "dvth_" + block.Id,
            Text = divider.Thickness.ToString("0.##"),
            Width = 70,
            Margin = new Thickness(0, 0, 8, 0),
        };
        ToolTip.SetTip(thickness, "Divider thickness");
        AutomationProperties.SetName(thickness, "Divider thickness");
        var captured = block.Id;
        thickness.TextChanged += (_, _) => vm.EditText("dvth_" + captured, thickness.Text ?? string.Empty);
        var style = new ComboBox
        {
            ItemsSource = new List<string> { "solid", "dashed", "dotted", "none" },
            SelectedItem = divider.LineStyle,
            Width = 100,
            Margin = new Thickness(0, 0, 8, 0),
        };
        ToolTip.SetTip(style, "Divider line style");
        AutomationProperties.SetName(style, "Divider line style");
        style.SelectionChanged += (_, _) => vm.SetDividerStyle(captured, style.SelectedItem?.ToString() ?? "solid");
        var color = new TextBox
        {
            Name = "dvcl_" + block.Id,
            Text = divider.Color,
            Width = 130,
        };
        ToolTip.SetTip(color, "Divider color (hex)");
        AutomationProperties.SetName(color, "Divider color");
        color.TextChanged += (_, _) => vm.EditText("dvcl_" + captured, color.Text ?? string.Empty);
        editors.Children.Add(thickness);
        editors.Children.Add(style);
        editors.Children.Add(color);
        shell.Children.Add(editors);
        return shell;
    }

    // ----- Image -----

    private static Control BuildImage(BoardsViewModel vm, RichBoardBlock block)
    {
        var shell = BlockShell(vm, block, "Image");
        var image = block.Image;
        if (image?.DataBase64 is { Length: > 0 } data)
        {
            try
            {
                var bytes = Convert.FromBase64String(data);
                using var stream = new MemoryStream(bytes);
                var bitmap = new Bitmap(stream);
                var control = new Image
                {
                    Source = bitmap,
                    Width = image.Width > 0 ? image.Width : double.NaN,
                    MaxWidth = 560,
                    HorizontalAlignment = image.Alignment switch
                    {
                        "center" => HorizontalAlignment.Center,
                        "right" => HorizontalAlignment.Right,
                        _ => HorizontalAlignment.Left,
                    },
                    Margin = new Thickness(0, 4, 0, 4),
                };
                AutomationProperties.SetName(control, "Image: " + (image.AltText is { Length: > 0 } altText ? altText : image.DisplayName));
                shell.Children.Add(control);
            }
            catch
            {
                shell.Children.Add(new TextBlock
                {
                    Text = "Image bytes are not decodable: " + image.DisplayName,
                    FontSize = 13,
                    Margin = new Thickness(0, 4, 0, 4),
                });
            }
        }
        else
        {
            shell.Children.Add(new TextBlock
            {
                Text = "No image bytes yet — use Replace to choose a file.",
                FontSize = 13,
                Margin = new Thickness(0, 4, 0, 4),
            });
        }
        var editors = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
        var width = new TextBox
        {
            Name = "iw_" + block.Id,
            Text = (image?.Width ?? 0).ToString("0.##"),
            Width = 80,
            Margin = new Thickness(0, 0, 8, 0),
        };
        ToolTip.SetTip(width, "Image display width (0 = natural)");
        AutomationProperties.SetName(width, "Image width");
        var captured = block.Id;
        width.TextChanged += (_, _) => vm.EditText("iw_" + captured, width.Text ?? string.Empty);
        var align = new ComboBox
        {
            ItemsSource = new List<string> { "inherit", "left", "center", "right" },
            SelectedItem = string.IsNullOrEmpty(image?.Alignment) ? "inherit" : image!.Alignment,
            Width = 100,
            Margin = new Thickness(0, 0, 8, 0),
        };
        ToolTip.SetTip(align, "Image alignment");
        AutomationProperties.SetName(align, "Image alignment");
        align.SelectionChanged += (_, _) => vm.SetImageAlignment(captured, align.SelectedItem?.ToString() ?? "inherit");
        var alt = new TextBox
        {
            Name = "ialt_" + block.Id,
            Text = image?.AltText ?? string.Empty,
            Width = 200,
            Margin = new Thickness(0, 0, 8, 0),
        };
        ToolTip.SetTip(alt, "Image alt text");
        AutomationProperties.SetName(alt, "Image alt text");
        alt.TextChanged += (_, _) => vm.EditText("ialt_" + captured, alt.Text ?? string.Empty);
        editors.Children.Add(width);
        editors.Children.Add(align);
        editors.Children.Add(alt);
        shell.Children.Add(editors);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
        AddSmallButton(actions, "Replace…", "Replace image from a file", async () => await vm.ReplaceImageAsync(captured));
        AddSmallButton(actions, "Remove image", "Remove this image block", async () => await vm.DeleteBlockAsync(captured));
        shell.Children.Add(actions);
        return shell;
    }

    // ----- Graph -----

    private static Control BuildGraph(BoardsViewModel vm, RichBoardBlock block)
    {
        var shell = BlockShell(vm, block, "Graph");
        var graph = block.Graph ?? new RichGraphView();
        foreach (var expr in graph.Expressions)
            shell.Children.Add(BuildGraphExpressionRow(vm, block.Id, expr));
        var add = new Button { Content = "+ Add expression", Margin = new Thickness(0, 4, 0, 4) };
        ToolTip.SetTip(add, "Add a graph expression");
        AutomationProperties.SetName(add, "Add graph expression");
        var captured = block.Id;
        add.Click += (_, _) => vm.GraphAddExpression(captured);
        shell.Children.Add(add);

        var viewport = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 4) };
        AddViewportField(vm, viewport, captured, "XMin", graph.XMin);
        AddViewportField(vm, viewport, captured, "XMax", graph.XMax);
        AddViewportField(vm, viewport, captured, "YMin", graph.YMin);
        AddViewportField(vm, viewport, captured, "YMax", graph.YMax);
        var apply = new Button { Content = "Apply", Margin = new Thickness(8, 0, 0, 0) };
        ToolTip.SetTip(apply, "Apply viewport and redraw curves");
        AutomationProperties.SetName(apply, "Apply graph viewport");
        apply.Click += (_, _) => vm.RefreshAfterEdit();
        viewport.Children.Add(apply);
        shell.Children.Add(viewport);

        var nav = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
        AddSmallButton(nav, "◀", "Pan graph left", () => vm.GraphPanZoom(captured, -0.2, 0, 1));
        AddSmallButton(nav, "▶", "Pan graph right", () => vm.GraphPanZoom(captured, 0.2, 0, 1));
        AddSmallButton(nav, "▲", "Pan graph up", () => vm.GraphPanZoom(captured, 0, 0.2, 1));
        AddSmallButton(nav, "▼", "Pan graph down", () => vm.GraphPanZoom(captured, 0, -0.2, 1));
        AddSmallButton(nav, "+", "Zoom graph in", () => vm.GraphPanZoom(captured, 0, 0, 1.5));
        AddSmallButton(nav, "−", "Zoom graph out", () => vm.GraphPanZoom(captured, 0, 0, 1 / 1.5));
        shell.Children.Add(nav);

        shell.Children.Add(BuildGraphCanvas(graph));
        return shell;
    }

    private static Control BuildGraphExpressionRow(BoardsViewModel vm, string blockId, RichGraphExpressionView expr)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 2) };
        var visible = new CheckBox
        {
            Name = $"gv_{blockId}_{expr.Id}",
            IsChecked = expr.Visible,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0),
        };
        AutomationProperties.SetName(visible, "Graph expression visible");
        var text = new TextBox
        {
            Name = $"gx_{blockId}_{expr.Id}",
            Text = expr.Text,
            Width = 260,
            Margin = new Thickness(0, 0, 8, 0),
        };
        ToolTip.SetTip(text, "Graph expression, e.g. y = sin(x)");
        AutomationProperties.SetName(text, "Graph expression");
        var color = new TextBox
        {
            Name = $"gc_{blockId}_{expr.Id}",
            Text = expr.Color,
            Width = 110,
            Margin = new Thickness(0, 0, 8, 0),
        };
        ToolTip.SetTip(color, "Expression color (hex)");
        AutomationProperties.SetName(color, "Graph expression color");
        var erase = new Button { Content = "✕" };
        ToolTip.SetTip(erase, "Remove this expression");
        AutomationProperties.SetName(erase, "Remove graph expression");
        var exprId = expr.Id;
        visible.IsCheckedChanged += (_, _) => vm.EditCheck($"gv_{blockId}_{exprId}", visible.IsChecked == true);
        text.TextChanged += (_, _) => vm.EditText($"gx_{blockId}_{exprId}", text.Text ?? string.Empty);
        text.GotFocus += (_, _) => vm.FocusBlock(blockId);
        color.TextChanged += (_, _) => vm.EditText($"gc_{blockId}_{exprId}", color.Text ?? string.Empty);
        erase.Click += async (_, _) => await vm.GraphDeleteExpressionAsync(blockId, exprId);
        row.Children.Add(visible);
        row.Children.Add(text);
        row.Children.Add(color);
        row.Children.Add(erase);
        return row;
    }

    private static void AddViewportField(BoardsViewModel vm, Panel parent, string blockId, string field, double value)
    {
        parent.Children.Add(new TextBlock
        {
            Text = field,
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 4, 0),
        });
        var box = new TextBox
        {
            Name = $"gvp_{field}_{blockId}",
            Text = value.ToString("0.##"),
            Width = 64,
        };
        AutomationProperties.SetName(box, "Graph viewport " + field);
        box.TextChanged += (_, _) => vm.EditText($"gvp_{field}_{blockId}", box.Text ?? string.Empty);
        box.GotFocus += (_, _) => vm.FocusBlock(blockId);
        parent.Children.Add(box);
    }

    private static Control BuildGraphCanvas(RichGraphView graph)
    {
        var canvas = new Canvas
        {
            Width = GraphWidth,
            Height = GraphHeight,
            Background = new SolidColorBrush(Color.Parse("#FFFFFFFF")),
            Margin = new Thickness(0, 4, 0, 0),
        };
        AutomationProperties.SetName(canvas, "Graph plot");
        const double pad = 14;
        double mapX(double x) => pad + ((x - graph.XMin) / (graph.XMax - graph.XMin)) * (GraphWidth - 2 * pad);
        double mapY(double y) => GraphHeight - pad - ((y - graph.YMin) / (graph.YMax - graph.YMin)) * (GraphHeight - 2 * pad);

        if (graph.ShowGrid)
        {
            for (var i = 1; i < 10; i++)
            {
                var gx = pad + ((GraphWidth - 2 * pad) * i / 10);
                var gy = pad + ((GraphHeight - 2 * pad) * i / 10);
                canvas.Children.Add(new Line
                {
                    StartPoint = new Point(gx, pad),
                    EndPoint = new Point(gx, GraphHeight - pad),
                    Stroke = new SolidColorBrush(Color.Parse("#FFE0E0E0")),
                    StrokeThickness = 1,
                });
                canvas.Children.Add(new Line
                {
                    StartPoint = new Point(pad, gy),
                    EndPoint = new Point(GraphWidth - pad, gy),
                    Stroke = new SolidColorBrush(Color.Parse("#FFE0E0E0")),
                    StrokeThickness = 1,
                });
            }
        }
        if (graph.ShowAxes && graph.XMin < 0 && graph.XMax > 0)
            canvas.Children.Add(new Line
            {
                StartPoint = new Point(mapX(0), pad),
                EndPoint = new Point(mapX(0), GraphHeight - pad),
                Stroke = Brushes.Gray,
                StrokeThickness = 1,
            });
        if (graph.ShowAxes && graph.YMin < 0 && graph.YMax > 0)
            canvas.Children.Add(new Line
            {
                StartPoint = new Point(pad, mapY(0)),
                EndPoint = new Point(GraphWidth - pad, mapY(0)),
                Stroke = Brushes.Gray,
                StrokeThickness = 1,
            });

        var errors = new List<string>();
        foreach (var expr in graph.Expressions)
        {
            if (!expr.Visible)
                continue;
            var samples = HavenGraphExpression.Sample(
                expr.Text, expr.DomainMin, expr.DomainMax, graph.XMin, graph.XMax, 200);
            if (samples.Count == 0)
            {
                var mid = (graph.XMin + graph.XMax) / 2;
                if (!HavenGraphExpression.TryEvaluate(expr.Text, mid, out _))
                    errors.Add("Invalid expression \"" + expr.Text + "\" — curve hidden.");
                else
                    errors.Add("Expression \"" + expr.Text + "\" has no points in this viewport.");
                continue;
            }
            var stroke = ParseBrushOrDefault(expr.Color, "#FF1A73E8");
            List<Point>? run = null;
            void flush()
            {
                if (run is { Count: > 1 })
                    canvas.Children.Add(new Polyline
                    {
                        Points = run,
                        Stroke = stroke,
                        StrokeThickness = Math.Clamp(expr.LineWidth, 1, 8),
                    });
                run = null;
            }
            foreach (var (x, y) in samples)
            {
                var px = mapX(x);
                var py = mapY(y);
                if (!double.IsFinite(px) || !double.IsFinite(py)
                    || py < -GraphHeight || py > GraphHeight * 2)
                {
                    flush();
                    continue;
                }
                run ??= [];
                run.Add(new Point(px, py));
            }
            flush();
        }

        if (errors.Count > 0)
        {
            var wrap = new StackPanel { Orientation = Orientation.Vertical };
            wrap.Children.Add(canvas);
            foreach (var error in errors)
                wrap.Children.Add(new TextBlock
                {
                    Text = error,
                    FontSize = 12,
                    Foreground = new SolidColorBrush(Color.Parse("#FFB00020")),
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 2, 0, 0),
                });
            return wrap;
        }
        return canvas;
    }

    // ----- Attachment -----

    private static Control BuildAttachmentRow(BoardsViewModel vm, RichBoardBlock block)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
        row.Children.Add(new TextBlock
        {
            Text = "Attachment: " + (block.AttachmentName ?? block.AttachmentId)
                + $" ({block.AttachmentSize / 1024.0:0.#} KB)",
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0),
        });
        var captured = block.Id;
        AddSmallButton(row, "Open", "Resolve and open the attachment", async () => await vm.OpenAttachmentAsync(captured));
        AddSmallButton(row, "Remove", "Detach the attachment", async () => await vm.RemoveAttachmentAsync(captured));
        return row;
    }

    // ----- Ink -----

    private static Control BuildInk(BoardsViewModel vm, RichBoardBlock block)
    {
        var shell = BlockShell(vm, block, "Ink");
        shell.Children.Add(new TextBlock
        {
            Text = $"{block.InkStrokeCount} stroke(s) — {block.Text}",
            FontSize = 13,
            Margin = new Thickness(0, 0, 0, 4),
        });
        var tools = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
        tools.Children.Add(new TextBlock
        {
            Text = "Tool",
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 4, 0),
        });
        var toolBox = new ComboBox
        {
            ItemsSource = new List<string> { "Pen", "Highlighter", "Eraser" },
            SelectedItem = vm.InkTool,
            Width = 120,
            Margin = new Thickness(0, 0, 8, 0),
        };
        ToolTip.SetTip(toolBox, "Ink tool (eraser tap erases strokes at that point)");
        AutomationProperties.SetName(toolBox, "Ink tool");
        toolBox.SelectionChanged += (_, _) => vm.SetInkTool(toolBox.SelectedItem?.ToString() ?? "Pen");
        tools.Children.Add(toolBox);
        var width = new TextBox
        {
            Name = "InkWidthBox",
            Text = vm.InkWidth.ToString("0.##"),
            Width = 64,
            Margin = new Thickness(0, 0, 8, 0),
        };
        ToolTip.SetTip(width, "Stroke width");
        AutomationProperties.SetName(width, "Ink stroke width");
        width.TextChanged += (_, _) => vm.EditText("InkWidthBox", width.Text ?? string.Empty);
        tools.Children.Add(width);
        var color = new TextBox
        {
            Name = "InkColorBox",
            Text = vm.InkColor,
            Width = 110,
            Margin = new Thickness(0, 0, 8, 0),
        };
        ToolTip.SetTip(color, "Stroke color (hex)");
        AutomationProperties.SetName(color, "Ink stroke color");
        color.TextChanged += (_, _) => vm.EditText("InkColorBox", color.Text ?? string.Empty);
        tools.Children.Add(color);
        AddSmallButton(tools, "Clear", "Clear all ink on this page", async () => await vm.ClearInkAsync());
        shell.Children.Add(tools);

        var view = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
        AddSmallButton(view, "Zoom −", "Zoom ink out", async () => await vm.SetInkZoomAsync(vm.InkZoom / 1.25));
        AddSmallButton(view, "Zoom +", "Zoom ink in", async () => await vm.SetInkZoomAsync(vm.InkZoom * 1.25));
        AddSmallButton(view, "Reset view", "Reset ink pan and zoom", async () => await vm.ResetInkViewAsync());
        shell.Children.Add(view);

        var canvas = new Canvas
        {
            Name = "ink_" + block.Id,
            Width = 600,
            Height = 200,
            Background = new SolidColorBrush(Color.Parse("#FFFFFFFF")),
            Margin = new Thickness(0, 4, 0, 0),
            RenderTransform = new ScaleTransform(vm.InkZoom, vm.InkZoom),
        };
        ToolTip.SetTip(canvas, "Draw with the pointer; one gesture commits one stroke");
        AutomationProperties.SetName(canvas, "Ink canvas");
        shell.Children.Add(canvas);
        return shell;
    }

    // ----- Freeform -----

    private static Control BuildFreeformSection(BoardsViewModel vm, IReadOnlyList<CanvasBoxView> boxes)
    {
        var shell = new StackPanel { Orientation = Orientation.Vertical, Margin = new Thickness(0, 8, 0, 0) };
        var header = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
        header.Children.Add(new TextBlock
        {
            Text = "Freeform canvas (" + boxes.Count + " objects)",
            FontSize = 13,
            FontWeight = FontWeight.SemiBold,
            Foreground = new SolidColorBrush(Color.Parse("#FF757575")),
            VerticalAlignment = VerticalAlignment.Center,
        });
        if (!vm.SupportsFreeform)
            header.Children.Add(new TextBlock
            {
                Text = "  [needs the contract session]",
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.Parse("#FF9AA0A6")),
                VerticalAlignment = VerticalAlignment.Center,
            });
        shell.Children.Add(header);

        string? selectedId = null;
        Border? selectedBorder = null;
        var canvas = new Canvas
        {
            Name = "FreeformCanvas",
            Width = 600,
            Height = 300,
            Background = new SolidColorBrush(Color.Parse("#FFFFFFFF")),
            Margin = new Thickness(0, 4, 0, 4),
        };
        ToolTip.SetTip(canvas, "Drag a box; the move commits once on release");
        AutomationProperties.SetName(canvas, "Freeform canvas");
        foreach (var box in boxes)
        {
            var border = new Border
            {
                Name = "canvas_" + box.Id,
                Width = Math.Clamp(box.Width, 24, 5000),
                Height = Math.Clamp(box.Height, 24, 5000),
                Background = new SolidColorBrush(Color.Parse("#FFE8EDF3")),
                BorderBrush = new SolidColorBrush(Color.Parse("#FF4A6FA5")),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(6),
            };
            border.Child = new TextBlock
            {
                Text = string.IsNullOrEmpty(box.Text) ? box.Kind : box.Text,
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
            };
            ToolTip.SetTip(border, "Drag to move; nudge with the arrow buttons");
            AutomationProperties.SetName(border, "Canvas object " + (box.Text is { Length: > 0 } t ? t : box.Kind));
            Canvas.SetLeft(border, box.X);
            Canvas.SetTop(border, box.Y);
            var captured = box;
            var dragging = false;
            var dragOffset = new Point();
            var dragOrigin = new Point();
            border.PointerPressed += (_, e) =>
            {
                dragging = true;
                var position = e.GetPosition(canvas);
                dragOffset = new Point(position.X - captured.X, position.Y - captured.Y);
                dragOrigin = new Point(captured.X, captured.Y);
                e.Pointer.Capture(border);
                selectedId = captured.Id;
                if (selectedBorder is not null)
                    selectedBorder.BorderThickness = new Thickness(1);
                selectedBorder = border;
                border.BorderThickness = new Thickness(3);
                e.Handled = true;
            };
            border.PointerMoved += (_, e) =>
            {
                if (!dragging)
                    return;
                var position = e.GetPosition(canvas);
                Canvas.SetLeft(border, Math.Max(0, position.X - dragOffset.X));
                Canvas.SetTop(border, Math.Max(0, position.Y - dragOffset.Y));
            };
            border.PointerReleased += async (_, args) =>
            {
                if (!dragging)
                    return;
                dragging = false;
                args.Pointer.Capture(null);
                // ONE commit per drag gesture on release.
                var left = Canvas.GetLeft(border);
                var top = Canvas.GetTop(border);
                if (Math.Abs(left - dragOrigin.X) > 0.5 || Math.Abs(top - dragOrigin.Y) > 0.5)
                    await vm.MoveCanvasBoxAsync(captured.Id, left, top);
            };
            canvas.Children.Add(border);
        }
        shell.Children.Add(canvas);

        var actions = new StackPanel { Orientation = Orientation.Horizontal };
        AddSmallButton(actions, "Add box", "Add a freeform box", async () => await vm.AddCanvasBoxAsync());
        AddSmallButton(actions, "←", "Nudge selected box left", async () =>
        {
            var current = boxes.FirstOrDefault(b => b.Id == selectedId);
            if (current is not null)
                await vm.MoveCanvasBoxAsync(current.Id, current.X - 8, current.Y);
        });
        AddSmallButton(actions, "→", "Nudge selected box right", async () =>
        {
            var current = boxes.FirstOrDefault(b => b.Id == selectedId);
            if (current is not null)
                await vm.MoveCanvasBoxAsync(current.Id, current.X + 8, current.Y);
        });
        AddSmallButton(actions, "↑", "Nudge selected box up", async () =>
        {
            var current = boxes.FirstOrDefault(b => b.Id == selectedId);
            if (current is not null)
                await vm.MoveCanvasBoxAsync(current.Id, current.X, current.Y - 8);
        });
        AddSmallButton(actions, "↓", "Nudge selected box down", async () =>
        {
            var current = boxes.FirstOrDefault(b => b.Id == selectedId);
            if (current is not null)
                await vm.MoveCanvasBoxAsync(current.Id, current.X, current.Y + 8);
        });
        AddSmallButton(actions, "Remove selected", "Remove the selected box", async () =>
        {
            if (selectedId is not null)
                await vm.RemoveCanvasBoxAsync(selectedId);
        });
        shell.Children.Add(actions);
        return shell;
    }

    // ----- Helpers -----

    private static void AddSmallButton(Panel parent, string content, string tip, Action tapped)
    {
        var button = new Button { Content = content, Margin = new Thickness(0, 0, 8, 0) };
        ToolTip.SetTip(button, tip);
        AutomationProperties.SetName(button, tip);
        button.Click += (_, _) => tapped();
        parent.Children.Add(button);
    }

    private static void AddSmallButton(Panel parent, string content, string tip, Func<Task> tapped)
    {
        var button = new Button { Content = content, Margin = new Thickness(0, 0, 8, 0) };
        ToolTip.SetTip(button, tip);
        AutomationProperties.SetName(button, tip);
        button.Click += async (_, _) => await tapped();
        parent.Children.Add(button);
    }

    private static int DerivedMax(RichBoardBlock block, bool row)
    {
        var best = 0;
        foreach (var key in block.TableCells.Keys)
        {
            var cut = key.IndexOf(',');
            if (cut < 0)
                continue;
            var part = row ? key[..cut] : key[(cut + 1)..];
            if (int.TryParse(part, out var value))
                best = Math.Max(best, value);
        }
        return best;
    }

    private static TextAlignment ParseAlignment(string alignment) => alignment?.ToLowerInvariant() switch
    {
        "center" => TextAlignment.Center,
        "right" => TextAlignment.Right,
        "justify" => TextAlignment.Justify,
        _ => TextAlignment.Left,
    };

    private static bool TryBrush(string? color, out IBrush? brush)
    {
        brush = null;
        if (string.IsNullOrWhiteSpace(color))
            return false;
        try
        {
            brush = new SolidColorBrush(Color.Parse(color.Trim()));
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static IBrush ParseBrushOrDefault(string? color, string fallback)
    {
        if (TryBrush(color, out var brush) && brush is not null)
            return brush;
        return new SolidColorBrush(Color.Parse(fallback));
    }

    /// <summary>Checklist view ids are "parentId:itemId"; plain ids map to themselves with '_' separators.</summary>
    private static string NameSuffix(string viewId) => viewId.Replace(':', '_');

    private static string ParentKey(string viewId)
    {
        var colon = viewId.IndexOf(':');
        return colon > 0 ? viewId[..colon] : viewId;
    }
}
