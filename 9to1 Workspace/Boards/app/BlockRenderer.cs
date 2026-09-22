// BlockRenderer builds per-block editors into BlocksHost from BoardsViewModel state.
// Control names encode ids (t_{blockId}, done_/chk_ checklist names,
// cell_{blockId}_{r}_{c}, gx_/gv_/gc_/gvp_/iw_/ialt_/dvth_/dvcl_) so the
// ViewModel routes edits without hardcoding. Contract block/item ids never
// contain '_', so names split on the LAST '_' (see BoardsViewModel).

using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
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
        if (blocks.Count == 0)
        {
            host.Children.Add(BuildEmptyPageHint(vm));
        }
        var index = 0;
        while (index < blocks.Count)
        {
            var block = blocks[index];
            if (IsListKind(block.Kind))
            {
                var run = new List<RichBoardBlock>();
                while (index < blocks.Count && IsListKind(blocks[index].Kind))
                    run.Add(blocks[index++]);
                host.Children.Add(WithSelection(vm, run[0].Id, BuildChecklistRun(vm, run)));
                continue;
            }
            host.Children.Add(WithSelection(vm, block.Id, BuildBlock(vm, block)));
            index++;
        }

        host.Children.Add(BuildFreeformSection(vm, canvasBoxes ?? []));
    }

    private static Control BuildEmptyPageHint(BoardsViewModel vm)
    {
        var panel = new StackPanel { Orientation = Orientation.Vertical, Margin = new Thickness(0, 24, 0, 24) };
        panel.Children.Add(new TextBlock
        {
            Text = "Start typing or press + to add content.",
            FontSize = 15,
            Foreground = BoardsTheme.SecondaryTextBrush,
            HorizontalAlignment = HorizontalAlignment.Center,
            TextAlignment = TextAlignment.Center,
        });
        var add = new Button
        {
            Content = "+ Add your first block",
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 12, 0, 0),
            Padding = new Thickness(16, 8),
        };
        ToolTip.SetTip(add, "Add a paragraph block");
        Avalonia.Automation.AutomationProperties.SetName(add, "Add your first block");
        add.Click += (_, _) => _ = vm.DispatchAsync("AddBlock", null);
        panel.Children.Add(add);
        return panel;
    }

    /// <summary>Subtle selection outline: transparent until selected, then an accent bar.</summary>
    private static Control WithSelection(BoardsViewModel vm, string selectId, Control inner)    {
        var selected = string.Equals(vm.SelectedBlockId, selectId, StringComparison.Ordinal);
        var border = new Border
        {
            Child = inner,
            BorderThickness = new Thickness(3, 0, 0, 0),
            BorderBrush = selected ? BoardsTheme.AccentBrush : Brushes.Transparent,
            Padding = new Thickness(9, 2, 0, 2),
            Margin = new Thickness(0, 0, 0, 10),
            Background = Brushes.Transparent,
        };
        return border;
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

    private static bool IsListKind(string kind) =>
        kind is "checklist" or "bulleted" or "numbered";

    private static StackPanel BlockShell(BoardsViewModel vm, RichBoardBlock block)
    {
        // No visible header: blocks read as document content. Management lives
        // in the block context menu (right-click) and the contextual strip.
        var shell = new StackPanel { Orientation = Orientation.Vertical };
        var menu = new ContextMenu();
        var delete = new MenuItem { Header = "Delete block" };
        Avalonia.Automation.AutomationProperties.SetName(delete, "Delete block");
        var captured = block.Id;
        delete.Click += async (_, _) => await vm.DeleteBlockAsync(captured);
        menu.Items.Add(delete);
        shell.ContextMenu = menu;
        if (!string.IsNullOrEmpty(block.AttachmentId))
            shell.Children.Add(BuildAttachmentRow(vm, block));
        return shell;
    }

    // ----- Text -----

    private static Control BuildTextBlock(BoardsViewModel vm, RichBoardBlock block)
    {
        var isHeading = block.Kind == "heading";
        var style = vm.StyleList.FirstOrDefault(s => s.Id == block.StyleId);
        var shell = BlockShell(vm, block);
        var box = new TextBox
        {
            Name = "t_" + block.Id,
            Text = block.Text ?? string.Empty,
            FontSize = ResolveFontSize(block, style, isHeading ? 26 : 14),
            FontWeight = block.Bold || (style?.Bold == true) || isHeading ? FontWeight.Bold : FontWeight.Normal,
            FontStyle = block.Italic || (style?.Italic == true) ? FontStyle.Italic : FontStyle.Normal,
            AcceptsReturn = !isHeading,
            TextWrapping = TextWrapping.Wrap,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(2, 6),
            Margin = new Thickness(Math.Max(0, block.IndentLevel) * 20, 0, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        var family = !string.IsNullOrEmpty(block.FontFamily) ? block.FontFamily : style?.FontFamily;
        if (!string.IsNullOrEmpty(family))
            box.FontFamily = new FontFamily(family);
        box.TextAlignment = ParseAlignment(block.Alignment != "inherit" ? block.Alignment : style?.Alignment ?? "inherit");
        if (TryBrush(block.Background, out var background))
            box.Background = background;
        else if (TryBrush(style?.Background, out var styleBackground))
            box.Background = styleBackground;
        if (TryBrush(block.Foreground, out var foreground))
            box.Foreground = foreground;
        else if (TryBrush(style?.Foreground, out var styleForeground))
            box.Foreground = styleForeground;
        else
            box.Foreground = BoardsTheme.ContrastText(
                !string.IsNullOrEmpty(block.Background) ? block.Background : style?.Background);
        ToolTip.SetTip(box, (isHeading ? "Heading" : "Paragraph") + " — select to format");
        AutomationProperties.SetName(box, (isHeading ? "Heading" : "Paragraph") + " editor");
        var captured = block.Id;
        box.TextChanged += (_, _) => vm.EditText("t_" + captured, box.Text ?? string.Empty);
        box.GotFocus += (_, _) => vm.FocusBlock(captured);
        shell.Children.Add(box);
        // Extended flags that plain text cannot show stay as a whisper, not chrome.
        var quiet = new List<string>();
        if (block.Strike) quiet.Add("strikethrough");
        if (block.Baseline is "subscript" or "superscript") quiet.Add(block.Baseline);
        if (quiet.Count > 0)
            shell.Children.Add(new TextBlock
            {
                Text = string.Join(" · ", quiet),
                FontSize = 11,
                Foreground = BoardsTheme.SecondaryTextBrush,
                Margin = new Thickness(2, 0, 0, 2),
            });
        return shell;
    }

    private static double ResolveFontSize(RichBoardBlock block, RichStyleView? style, double fallback)
    {
        if (block.FontSize > 0)
            return block.FontSize;
        if (style is not null && style.FontSize > 0)
            return style.FontSize;
        return fallback;
    }

    // ----- Checklist -----

    private static Control BuildChecklistRun(BoardsViewModel vm, List<RichBoardBlock> run)
    {
        var shell = new StackPanel { Orientation = Orientation.Vertical, Margin = new Thickness(0, 4, 0, 4) };
        foreach (var item in run)
            shell.Children.Add(BuildChecklistRow(vm, item));
        var parentKey = ParentKey(run[0].Id);
        var add = new Button
        {
            Content = "+ Add item",
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Foreground = BoardsTheme.SecondaryTextBrush,
            FontSize = 13,
            Padding = new Thickness(28, 4, 8, 4),
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        ToolTip.SetTip(add, "Add a checklist item");
        AutomationProperties.SetName(add, "Add checklist item");
        add.Click += async (_, _) => await vm.AddChecklistItemAsync(parentKey);
        shell.Children.Add(add);
        return shell;
    }

    private static Control BuildChecklistRow(BoardsViewModel vm, RichBoardBlock item)
    {
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(Math.Max(0, item.Level) * 16, 3, 0, 3),
        };
        var suffix = NameSuffix(item.Id);
        var check = new CheckBox
        {
            Name = "done_" + suffix,
            IsChecked = item.IsChecked,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 10, 0),
        };
        AutomationProperties.SetName(check, "Checklist item done");
        var text = new TextBox
        {
            Name = "chk_" + suffix,
            Text = item.Text ?? string.Empty,
            FontSize = 14,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(2, 4),
            FontWeight = item.Bold ? FontWeight.Bold : FontWeight.Normal,
            FontStyle = item.Italic ? FontStyle.Italic : FontStyle.Normal,
            Foreground = item.IsChecked ? BoardsTheme.SecondaryTextBrush : BoardsTheme.TextBrush,
        };
        ToolTip.SetTip(text, "Checklist item text (Enter adds next, empty Backspace removes)");
        AutomationProperties.SetName(text, "Checklist item text");
        var erase = new Button
        {
            Content = BoardsIcons.Glyph(BoardsIcons.Close, 11, "Remove checklist item"),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Foreground = BoardsTheme.SecondaryTextBrush,
            Padding = new Thickness(6, 2),
            VerticalAlignment = VerticalAlignment.Center,
            Opacity = 0,
        };
        ToolTip.SetTip(erase, "Remove this item");
        AutomationProperties.SetName(erase, "Remove checklist item");
        var viewId = item.Id;
        check.IsCheckedChanged += (_, _) => vm.EditCheck("done_" + suffix, check.IsChecked == true);
        text.TextChanged += (_, _) => vm.EditText("chk_" + suffix, text.Text ?? string.Empty);
        text.GotFocus += (_, _) =>
        {
            vm.FocusBlock(viewId);
            erase.Opacity = 1;
        };
        text.LostFocus += (_, _) => erase.Opacity = 0;
        row.PointerEntered += (_, _) => erase.Opacity = 1;
        row.PointerExited += (_, _) =>
        {
            if (!text.IsFocused)
                erase.Opacity = 0;
        };
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
        var shell = BlockShell(vm, block);
        var rows = block.TableRows > 0 ? block.TableRows : DerivedMax(block, row: true) + 1;
        var cols = block.TableCols > 0 ? block.TableCols : DerivedMax(block, row: false) + 1;
        rows = Math.Clamp(rows, 1, 100);
        cols = Math.Clamp(cols, 1, 50);

        var tableBorder = new Border
        {
            BorderBrush = TableBorderBrush(block),
            BorderThickness = new Thickness(Math.Clamp(block.TableStyle.BorderWidth > 0 ? block.TableStyle.BorderWidth : 1, 0.5, 8)),
            CornerRadius = new CornerRadius(Math.Clamp(block.TableStyle.CornerRadius, 0, 16)),
            Margin = new Thickness(0, 4, 0, 4),
            ClipToBounds = true,
        };
        if (TryBrush(block.TableStyle.Background, out var tableBackground))
            tableBorder.Background = tableBackground;
        else
            tableBorder.Background = BoardsTheme.CardBrush;

        var grid = new Grid();
        for (var r = 0; r < rows; r++)
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        for (var c = 0; c < cols; c++)
        {
            var width = c < block.ColumnWidths.Count && block.ColumnWidths[c] > 0
                ? block.ColumnWidths[c]
                : double.NaN;
            grid.ColumnDefinitions.Add(new ColumnDefinition(
                double.IsNaN(width) ? GridLength.Star : new GridLength(width)));
        }

        for (var r = 0; r < rows; r++)
            for (var c = 0; c < cols; c++)
            {
                var key = $"{r},{c}";
                block.TableCells.TryGetValue(key, out var cellText);
                block.CellStyles.TryGetValue(key, out var cellStyle);
                var box = new TextBox
                {
                    Name = $"cell_{block.Id}_{r}_{c}",
                    Text = cellText ?? string.Empty,
                    FontSize = 14,
                    AcceptsReturn = true,
                    TextWrapping = TextWrapping.Wrap,
                    Background = Brushes.Transparent,
                    BorderThickness = new Thickness(0),
                    Padding = new Thickness(8, 6),
                    FontWeight = cellStyle?.Bold == true ? FontWeight.Bold : FontWeight.Normal,
                    FontStyle = cellStyle?.Italic == true ? FontStyle.Italic : FontStyle.Normal,
                    MinHeight = 30,
                };
                if (cellStyle is not null && TryBrush(cellStyle.Background, out var cellBackground))
                    box.Background = cellBackground;
                else if (block.TableStyle.AlternatingRows && r % 2 == 1)
                    box.Background = BoardsTheme.Brush(BoardsTheme.Mode == BoardsThemeMode.Dark ? "#FF2A2E34" : "#FFF7F8FA");
                if (cellStyle is not null && TryBrush(cellStyle.Foreground, out var cellForeground))
                    box.Foreground = cellForeground;
                else
                    box.Foreground = BoardsTheme.ContrastText(cellStyle?.Background);
                box.TextAlignment = ParseAlignment(cellStyle?.AlignH ?? "inherit");
                box.VerticalAlignment = cellStyle?.AlignV switch
                {
                    "top" => VerticalAlignment.Top,
                    "bottom" => VerticalAlignment.Bottom,
                    _ => VerticalAlignment.Center,
                };
                AutomationProperties.SetName(box, $"Table cell row {r + 1} column {c + 1}");
                var rr = r;
                var cc = c;
                var captured = block.Id;
                box.TextChanged += (_, _) => vm.EditText($"cell_{captured}_{rr}_{cc}", box.Text ?? string.Empty);
                box.GotFocus += (_, _) => vm.FocusBlock(captured);
                var frame = new Border
                {
                    Child = box,
                    BorderBrush = BoardsTheme.BorderBrush,
                    BorderThickness = new Thickness(0, 0, c < cols - 1 ? 1 : 0, r < rows - 1 ? 1 : 0),
                    Background = box.Background,
                };
                box.Background = Brushes.Transparent;
                Grid.SetRow(frame, r);
                Grid.SetColumn(frame, c);
                grid.Children.Add(frame);
            }
        tableBorder.Child = grid;
        shell.Children.Add(tableBorder);
        return shell;
    }

    // ----- Divider -----

    private static Control BuildDivider(BoardsViewModel vm, RichBoardBlock block)
    {
        var shell = BlockShell(vm, block);
        var divider = block.Divider ?? new RichDividerView();
        // Line only; thickness/style/color live in the contextual strip when selected.
        if (divider.LineStyle != "none")
        {
            if (divider.LineStyle is "dashed" or "dotted")
            {
                shell.Children.Add(new TextBlock
                {
                    Text = divider.LineStyle == "dashed" ? "— — — — — — —" : "· · · · · · · · ·",
                    FontSize = 14,
                    Foreground = ParseBrushOrDefault(divider.Color, "#FF5F6368"),
                    Margin = new Thickness(0, 8, 0, 8),
                });
            }
            else
            {
                shell.Children.Add(new Rectangle
                {
                    Height = Math.Clamp(divider.Thickness, 1, 12),
                    Fill = ParseBrushOrDefault(divider.Color, "#FF5F6368"),
                    Margin = new Thickness(0, 8, 0, 8),
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                });
            }
        }
        return shell;
    }

    // ----- Image -----

    private static Control BuildImage(BoardsViewModel vm, RichBoardBlock block)
    {
        var shell = BlockShell(vm, block);
        var image = block.Image;
        var selected = vm.SelectedBlockId == block.Id;
        // Bytes resolve asynchronously from the contract (the view never hauls
        // payloads); a placeholder shows until the bitmap lands.
        var frame = new Border
        {
            BorderBrush = BoardsTheme.AccentBrush,
            BorderThickness = new Thickness(selected ? 2 : 0),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(selected ? 4 : 0),
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 4, 0, 4),
            Child = new TextBlock
            {
                Text = "Loading image…",
                FontSize = 13,
                Foreground = BoardsTheme.SecondaryTextBrush,
            },
        };
        var captured = block.Id;
        var alignment = image?.Alignment;
        var alt = image?.AltText;
        var displayName = image?.DisplayName ?? "image";
        var width = image?.Width ?? 0;
        frame.PointerPressed += (_, _) => vm.FocusBlock(captured);
        _ = LoadImageIntoAsync(frame, vm, captured, alignment, alt, displayName, width);
        shell.Children.Add(frame);
        return shell;
    }

    private static async Task LoadImageIntoAsync(
        Border frame, BoardsViewModel vm, string blockId,
        string? alignment, string? altText, string displayName, double width)
    {
        // Embedded bytes resolve synchronously (cached per document version);
        // only sidecar payloads take the async path below.
        var immediate = vm.TryResolveImageBytes(blockId);
        if (immediate is not null)
        {
            SetImageContent(frame, immediate, alignment, altText, displayName, width);
            return;
        }
        byte[]? bytes = null;
        try
        {
            bytes = await vm.ResolveImageBytesAsync(blockId).ConfigureAwait(false);
        }
        catch
        {
            bytes = null;
        }
        if (bytes is null)
        {
            SetImagePlaceholder(frame, displayName);
            return;
        }
        var captured = bytes;
        if (Avalonia.Threading.Dispatcher.UIThread.CheckAccess())
            SetImageContent(frame, captured, alignment, altText, displayName, width);
        else
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                SetImageContent(frame, captured, alignment, altText, displayName, width));
    }

    private static void SetImagePlaceholder(Border frame, string displayName) =>
        frame.Child = new TextBlock
        {
            Text = displayName == "image (choose a file)"
                ? "No image bytes yet — use Replace to choose a file."
                : "Image unavailable: " + displayName,
            FontSize = 13,
            Foreground = BoardsTheme.SecondaryTextBrush,
            Margin = new Thickness(0, 4, 0, 4),
        };

    private static void SetImageContent(
        Border frame, byte[] bytes, string? alignment, string? altText, string displayName, double width)
    {
        try
        {
            using var stream = new MemoryStream(bytes);
            var bitmap = new Bitmap(stream);
            var control = new Image
            {
                Source = bitmap,
                Width = width > 0 ? width : double.NaN,
                MaxWidth = 720,
                Stretch = Stretch.Uniform,
                HorizontalAlignment = alignment switch
                {
                    "center" => HorizontalAlignment.Center,
                    "right" => HorizontalAlignment.Right,
                    _ => HorizontalAlignment.Left,
                },
                Margin = new Thickness(0, 4, 0, 4),
            };
            AutomationProperties.SetName(control, "Image: " +
                (!string.IsNullOrEmpty(altText) ? altText : displayName));
            frame.Child = control;
        }
        catch
        {
            frame.Child = new TextBlock
            {
                Text = "Image bytes are not decodable: " + displayName,
                FontSize = 13,
                Foreground = BoardsTheme.ErrorBrush,
                Margin = new Thickness(0, 4, 0, 4),
            };
        }
    }

    // ----- Graph -----

    private static Control BuildGraph(BoardsViewModel vm, RichBoardBlock block)
    {
        var shell = BlockShell(vm, block);
        var graph = block.Graph ?? new RichGraphView();
        // Rendered plot dominates; editing lives in chips + collapsed settings.
        shell.Children.Add(BuildGraphCanvas(graph));
        var chips = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 2) };
        foreach (var expr in graph.Expressions)
            chips.Children.Add(BuildGraphChip(vm, block.Id, expr));
        var add = new Button
        {
            Content = "+ Expression",
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Foreground = BoardsTheme.SecondaryTextBrush,
            FontSize = 13,
            Padding = new Thickness(8, 4),
            Margin = new Thickness(0, 0, 0, 4),
        };
        ToolTip.SetTip(add, "Add a graph expression");
        AutomationProperties.SetName(add, "Add graph expression");
        var capturedBlock = block.Id;
        add.Click += (_, _) => vm.GraphAddExpression(capturedBlock);
        chips.Children.Add(add);
        shell.Children.Add(chips);

        if (graph.Expressions.Count == 0)
            shell.Children.Add(new TextBlock
            {
                Text = "Add an expression such as y = sin(x).",
                FontSize = 13,
                Foreground = BoardsTheme.SecondaryTextBrush,
                Margin = new Thickness(0, 0, 0, 4),
            });

        var settings = new Expander
        {
            Header = "Graph settings",
            IsExpanded = false,
            Margin = new Thickness(0, 2, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        var panel = new StackPanel { Orientation = Orientation.Vertical };
        foreach (var expr in graph.Expressions)
            panel.Children.Add(BuildGraphExpressionRow(vm, block.Id, expr));

        var viewport = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 4) };
        AddViewportField(vm, viewport, capturedBlock, "XMin", graph.XMin);
        AddViewportField(vm, viewport, capturedBlock, "XMax", graph.XMax);
        AddViewportField(vm, viewport, capturedBlock, "YMin", graph.YMin);
        AddViewportField(vm, viewport, capturedBlock, "YMax", graph.YMax);
        var apply = new Button { Content = "Apply", Margin = new Thickness(8, 0, 0, 0) };
        ToolTip.SetTip(apply, "Apply viewport and redraw curves");
        AutomationProperties.SetName(apply, "Apply graph viewport");
        apply.Click += (_, _) => vm.RefreshAfterEdit();
        viewport.Children.Add(apply);
        panel.Children.Add(viewport);

        var nav = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
        AddSmallButton(nav, "◀", "Pan graph left", () => vm.GraphPanZoom(capturedBlock, -0.2, 0, 1));
        AddSmallButton(nav, "▶", "Pan graph right", () => vm.GraphPanZoom(capturedBlock, 0.2, 0, 1));
        AddSmallButton(nav, "▲", "Pan graph up", () => vm.GraphPanZoom(capturedBlock, 0, 0.2, 1));
        AddSmallButton(nav, "▼", "Pan graph down", () => vm.GraphPanZoom(capturedBlock, 0, -0.2, 1));
        AddSmallButton(nav, "+", "Zoom graph in", () => vm.GraphPanZoom(capturedBlock, 0, 0, 1.5));
        AddSmallButton(nav, "−", "Zoom graph out", () => vm.GraphPanZoom(capturedBlock, 0, 0, 1 / 1.5));
        panel.Children.Add(nav);
        settings.Content = panel;
        shell.Children.Add(settings);
        return shell;
    }

    private static Control BuildGraphChip(BoardsViewModel vm, string blockId, RichGraphExpressionView expr)
    {
        var chip = new Border
        {
            Background = expr.Visible ? BoardsTheme.CardBrush : Brushes.Transparent,
            BorderBrush = BoardsTheme.BorderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(8, 4),
            Margin = new Thickness(0, 0, 6, 4),
        };
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(new Ellipse
        {
            Width = 10,
            Height = 10,
            Margin = new Thickness(0, 0, 6, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Fill = TryBrush(expr.Color, out var dot) && dot is not null
                ? dot
                : BoardsTheme.AccentBrush,
        });
        row.Children.Add(new TextBlock
        {
            Text = expr.Text,
            FontSize = 13,
            Foreground = expr.Visible ? BoardsTheme.TextBrush : BoardsTheme.SecondaryTextBrush,
            VerticalAlignment = VerticalAlignment.Center,
        });
        var eye = new Button
        {
            Content = new TextBlock
            {
                Text = expr.Visible ? "Hide" : "Show",
                FontSize = 12,
                Foreground = BoardsTheme.SecondaryTextBrush,
            },
            Padding = new Thickness(8, 0, 0, 0),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        var eyeTip = expr.Visible ? "Hide expression" : "Show expression";
        ToolTip.SetTip(eye, eyeTip);
        Avalonia.Automation.AutomationProperties.SetName(eye, eyeTip);
        var exprId = expr.Id;
        var visible = expr.Visible;
        eye.Click += (_, _) => vm.ToggleGraphExpression(blockId, exprId);
        var erase = new Button
        {
            Content = BoardsIcons.Glyph(BoardsIcons.Close, 10),
            Padding = new Thickness(8, 0, 0, 0),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        ToolTip.SetTip(erase, "Remove this expression");
        Avalonia.Automation.AutomationProperties.SetName(erase, "Remove graph expression");
        erase.Click += async (_, _) => await vm.GraphDeleteExpressionAsync(blockId, exprId);
        row.Children.Add(eye);
        row.Children.Add(erase);
        chip.Child = row;
        return chip;
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
        var card = new Border
        {
            Background = BoardsTheme.CardBrush,
            BorderBrush = BoardsTheme.BorderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12, 8),
            Margin = new Thickness(0, 0, 0, 8),
            MaxWidth = 480,
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(BoardsIcons.Glyph(BoardsIcons.File, 22, "Attachment"));
        var text = new StackPanel { Orientation = Orientation.Vertical, Margin = new Thickness(10, 0, 0, 0) };
        text.Children.Add(new TextBlock
        {
            Text = block.AttachmentName ?? "Attachment",
            FontSize = 13,
            FontWeight = FontWeight.SemiBold,
            Foreground = BoardsTheme.TextBrush,
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        text.Children.Add(new TextBlock
        {
            Text = AttachmentMeta(block),
            FontSize = 12,
            Foreground = BoardsTheme.SecondaryTextBrush,
        });
        row.Children.Add(text);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        var open = new Button
        {
            Content = "Open",
            Padding = new Thickness(10, 4),
            Margin = new Thickness(0, 0, 4, 0),
        };
        ToolTip.SetTip(open, "Resolve and open the attachment");
        Avalonia.Automation.AutomationProperties.SetName(open, "Open attachment");
        var captured = block.Id;
        open.Click += async (_, _) => await vm.OpenAttachmentAsync(captured);
        var remove = new Button
        {
            Content = BoardsIcons.Glyph(BoardsIcons.More, 13, "Attachment options"),
            Padding = new Thickness(8, 4),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
        };
        ToolTip.SetTip(remove, "Detach the attachment");
        Avalonia.Automation.AutomationProperties.SetName(remove, "Detach attachment");
        remove.Click += async (_, _) => await vm.RemoveAttachmentAsync(captured);
        actions.Children.Add(open);
        actions.Children.Add(remove);
        row.Children.Add(actions);
        card.Child = row;
        return card;
    }

    private static string AttachmentMeta(RichBoardBlock block)
    {
        var ext = System.IO.Path.GetExtension(block.AttachmentName ?? string.Empty).TrimStart('.').ToUpperInvariant();
        if (string.IsNullOrEmpty(ext))
            ext = "File";
        return $"{ext} · {FormatBytes(block.AttachmentSize)}";
    }

    internal static string FormatBytes(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:0.#} KB",
        < 1024L * 1024 * 1024 => $"{bytes / (1024.0 * 1024):0.#} MB",
        _ => $"{bytes / (1024.0 * 1024 * 1024):0.#} GB",
    };

    // ----- Ink -----

    private static readonly (string Name, string Hex)[] InkPalette =
    [
        ("Ink black", "#FF111111"), ("Slate", "#FF5F6368"), ("Blue", "#FF1A73E8"),
        ("Red", "#FFD32F2F"), ("Green", "#FF1E8E3E"), ("Orange", "#FFE8710A"),
        ("Purple", "#FF9334E6"), ("Teal", "#FF00897B"),
    ];

    private static readonly double[] InkThicknesses = [2, 4, 8, 12];

    private static Control BuildInk(BoardsViewModel vm, RichBoardBlock block)
    {
        var shell = BlockShell(vm, block);
        shell.Children.Add(new TextBlock
        {
            Text = $"{block.InkStrokeCount} stroke(s)" + (string.IsNullOrEmpty(block.Text) ? string.Empty : $" — {block.Text}"),
            FontSize = 13,
            Foreground = BoardsTheme.SecondaryTextBrush,
            Margin = new Thickness(0, 0, 0, 6),
        });
        var tools = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
        foreach (var tool in new[] { "Pen", "Highlighter", "Eraser", "Select" })
        {
            var toggle = new ToggleButton
            {
                Content = tool,
                IsChecked = string.Equals(vm.InkTool, tool, StringComparison.OrdinalIgnoreCase),
                FontSize = 13,
                Padding = new Thickness(12, 6),
                Margin = new Thickness(0, 0, 4, 0),
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
            };
            ToolTip.SetTip(toggle, tool + " tool");
            Avalonia.Automation.AutomationProperties.SetName(toggle, "Ink tool " + tool);
            toggle.Click += (_, _) => vm.SetInkTool(tool);
            tools.Children.Add(toggle);
        }
        foreach (var (name, hex) in InkPalette)
        {
            var swatch = new Button
            {
                Width = 26,
                Height = 26,
                Margin = new Thickness(2, 0),
                Padding = new Thickness(0),
                Background = BoardsTheme.Brush(hex),
                BorderBrush = BoardsTheme.BorderBrush,
                BorderThickness = new Thickness(string.Equals(vm.InkColor, hex, StringComparison.OrdinalIgnoreCase) ? 3 : 1),
                CornerRadius = new CornerRadius(13),
                VerticalAlignment = VerticalAlignment.Center,
            };
            ToolTip.SetTip(swatch, "Ink color " + name);
            Avalonia.Automation.AutomationProperties.SetName(swatch, "Ink color " + name);
            swatch.Click += (_, _) => vm.SetInkColor(hex);
            tools.Children.Add(swatch);
        }
        foreach (var thickness in InkThicknesses)
        {
            var pill = new ToggleButton
            {
                Content = new Ellipse
                {
                    Width = Math.Clamp(thickness, 2, 12),
                    Height = Math.Clamp(thickness, 2, 12),
                    Fill = BoardsTheme.TextBrush,
                },
                IsChecked = Math.Abs(vm.InkWidth - thickness) < 0.01,
                Padding = new Thickness(10, 6),
                Margin = new Thickness(2, 0),
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                VerticalAlignment = VerticalAlignment.Center,
            };
            ToolTip.SetTip(pill, $"Stroke width {thickness:0}");
            Avalonia.Automation.AutomationProperties.SetName(pill, $"Stroke width {thickness:0}");
            pill.Click += (_, _) => vm.SetInkWidth(thickness);
            tools.Children.Add(pill);
        }
        var clear = new Button
        {
            Content = "Clear",
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Foreground = BoardsTheme.SecondaryTextBrush,
            FontSize = 13,
            Padding = new Thickness(8, 6),
        };
        ToolTip.SetTip(clear, "Clear all ink on this page");
        Avalonia.Automation.AutomationProperties.SetName(clear, "Clear ink");
        clear.Click += async (_, _) => await vm.ClearInkAsync();
        tools.Children.Add(clear);
        shell.Children.Add(tools);

        var details = new Expander
        {
            Header = "Ink details",
            IsExpanded = false,
            Margin = new Thickness(0, 0, 0, 4),
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        var detailPanel = new StackPanel { Orientation = Orientation.Horizontal };
        var width = new TextBox
        {
            Name = "InkWidthBox",
            Text = vm.InkWidth.ToString("0.##"),
            Width = 64,
            Margin = new Thickness(0, 0, 8, 0),
        };
        ToolTip.SetTip(width, "Exact stroke width");
        Avalonia.Automation.AutomationProperties.SetName(width, "Ink stroke width");
        width.TextChanged += (_, _) => vm.EditText("InkWidthBox", width.Text ?? string.Empty);
        var color = new TextBox
        {
            Name = "InkColorBox",
            Text = vm.InkColor,
            Width = 110,
        };
        ToolTip.SetTip(color, "Exact stroke color (hex)");
        Avalonia.Automation.AutomationProperties.SetName(color, "Ink stroke color");
        color.TextChanged += (_, _) => vm.EditText("InkColorBox", color.Text ?? string.Empty);
        detailPanel.Children.Add(width);
        detailPanel.Children.Add(color);
        details.Content = detailPanel;
        shell.Children.Add(details);

        var view = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
        AddSmallButton(view, "Zoom −", "Zoom ink out", async () => await vm.SetInkZoomAsync(vm.InkZoom / 1.25));
        AddSmallButton(view, "Zoom +", "Zoom ink in", async () => await vm.SetInkZoomAsync(vm.InkZoom * 1.25));
        AddSmallButton(view, "←", "Pan ink left", async () => await vm.PanInkViewAsync(-60, 0));
        AddSmallButton(view, "→", "Pan ink right", async () => await vm.PanInkViewAsync(60, 0));
        AddSmallButton(view, "↑", "Pan ink up", async () => await vm.PanInkViewAsync(0, -60));
        AddSmallButton(view, "↓", "Pan ink down", async () => await vm.PanInkViewAsync(0, 60));
        AddSmallButton(view, "Reset view", "Reset ink pan and zoom", async () => await vm.ResetInkViewAsync());
        shell.Children.Add(view);

        var canvas = new Canvas
        {
            Name = "ink_" + block.Id,
            MinWidth = 240,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Height = 220,
            Background = BoardsTheme.CardBrush,
            Margin = new Thickness(0, 4, 0, 0),
            RenderTransform = new TransformGroup
            {
                Children =
                [
                    new ScaleTransform(vm.InkZoom, vm.InkZoom),
                    new TranslateTransform(vm.InkPanX, vm.InkPanY)
                ]
            },
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
            Text = boxes.Count == 0 ? "Freeform canvas" : $"Freeform canvas · {boxes.Count}",
            FontSize = 13,
            FontWeight = FontWeight.SemiBold,
            Foreground = BoardsTheme.SecondaryTextBrush,
            VerticalAlignment = VerticalAlignment.Center,
        });
        if (!vm.SupportsFreeform)
            header.Children.Add(new TextBlock
            {
                Text = "  Save the board to enable freeform.",
                FontSize = 12,
                Foreground = BoardsTheme.SecondaryTextBrush,
                VerticalAlignment = VerticalAlignment.Center,
            });
        shell.Children.Add(header);

        if (boxes.Count == 0)
        {
            shell.Children.Add(new TextBlock
            {
                Text = "No freeform objects yet — add a box to arrange ideas freely.",
                FontSize = 13,
                Foreground = BoardsTheme.SecondaryTextBrush,
                Margin = new Thickness(0, 0, 0, 4),
            });
        }

        string? selectedId = null;
        Border? selectedBorder = null;
        var canvas = new Canvas
        {
            Name = "FreeformCanvas",
            MinWidth = 240,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Height = 300,
            Background = BoardsTheme.CardBrush,
            Margin = new Thickness(0, 4, 0, 4),
        };
        ToolTip.SetTip(canvas, "Drag a card; the move commits once on release");
        AutomationProperties.SetName(canvas, "Freeform canvas");
        foreach (var box in boxes)
        {
            var border = new Border
            {
                Name = "canvas_" + box.Id,
                Width = Math.Clamp(box.Width, 24, 5000),
                Height = Math.Clamp(box.Height, 24, 5000),
                Background = BoardsTheme.CardBrush,
                BorderBrush = BoardsTheme.BorderBrush,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(10),
            };
            border.Child = new TextBlock
            {
                Text = string.IsNullOrEmpty(box.Text) ? box.Kind : box.Text,
                FontSize = 13,
                Foreground = BoardsTheme.TextBrush,
                TextWrapping = TextWrapping.Wrap,
            };
            ToolTip.SetTip(border, "Drag to move; arrow keys nudge the selected card");
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
                {
                    selectedBorder.BorderBrush = BoardsTheme.BorderBrush;
                    selectedBorder.BorderThickness = new Thickness(1);
                }
                selectedBorder = border;
                border.BorderBrush = BoardsTheme.AccentBrush;
                border.BorderThickness = new Thickness(2);
                border.Focus();
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
            border.KeyDown += async (_, e) =>
            {
                var step = e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? 1 : 8;
                var (dx, dy) = e.Key switch
                {
                    Key.Left => (-step, 0),
                    Key.Right => (step, 0),
                    Key.Up => (0, -step),
                    Key.Down => (0, step),
                    _ => (0, 0),
                };
                if (dx == 0 && dy == 0)
                    return;
                e.Handled = true;
                await vm.MoveCanvasBoxAsync(captured.Id, captured.X + dx, captured.Y + dy);
            };
            canvas.Children.Add(border);
        }
        shell.Children.Add(canvas);

        var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 0) };
        var addBox = new Button
        {
            Content = "+ Box",
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Foreground = BoardsTheme.SecondaryTextBrush,
            FontSize = 13,
            Padding = new Thickness(8, 4),
        };
        ToolTip.SetTip(addBox, "Add a freeform box");
        Avalonia.Automation.AutomationProperties.SetName(addBox, "Add freeform box");
        addBox.Click += async (_, _) => await vm.AddCanvasBoxAsync();
        actions.Children.Add(addBox);
        var removeBox = new Button
        {
            Content = "Remove selected",
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Foreground = BoardsTheme.SecondaryTextBrush,
            FontSize = 13,
            Padding = new Thickness(8, 4),
        };
        ToolTip.SetTip(removeBox, "Remove the selected box");
        Avalonia.Automation.AutomationProperties.SetName(removeBox, "Remove selected freeform box");
        removeBox.Click += async (_, _) =>
        {
            if (selectedId is not null)
                await vm.RemoveCanvasBoxAsync(selectedId);
        };
        actions.Children.Add(removeBox);
        actions.Children.Add(new TextBlock
        {
            Text = "Drag to move · arrow keys nudge",
            FontSize = 12,
            Foreground = BoardsTheme.SecondaryTextBrush,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 0, 0),
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

    private static IBrush TableBorderBrush(RichBoardBlock block) =>
        TryBrush(block.TableStyle.BorderColor, out var brush) && brush is not null
            ? brush
            : BoardsTheme.BorderBrush;

    /// <summary>Checklist view ids are "parentId:itemId"; plain ids map to themselves with '_' separators.</summary>
    private static string NameSuffix(string viewId) => viewId.Replace(':', '_');

    private static string ParentKey(string viewId)
    {
        var colon = viewId.IndexOf(':');
        return colon > 0 ? viewId[..colon] : viewId;
    }
}

