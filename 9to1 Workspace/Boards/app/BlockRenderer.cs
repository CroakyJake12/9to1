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
using Avalonia.VisualTree;
using CakeOS.Apps.Boards.Contract;
using CakeOS.Cui.Runtime;

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
                var runStart = index;
                while (index < blocks.Count && IsListKind(blocks[index].Kind))
                    run.Add(blocks[index++]);
                // Keep each contract block as its own row so pointer reordering
                // and the authored per-block keyboard actions address the same
                // stable identity. The shared add-item affordance remains after
                // the contiguous list run.
                for (var offset = 0; offset < run.Count; offset++)
                    host.Children.Add(WithSelection(vm, run[offset].Id, BuildChecklistRow(vm, run[offset]),
                        run[offset].Id, runStart + offset, host));
                host.Children.Add(BuildChecklistAddButton(vm, run[0].Id));
                continue;
            }
            host.Children.Add(WithSelection(vm, block.Id, BuildBlock(vm, block), block.Id, index, host));
            index++;
        }

        host.Children.Add(BuildFreeformSection(vm, canvasBoxes ?? []));
        vm.ApplyAuthoringState(host);
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
    private static Control WithSelection(
        BoardsViewModel vm, string selectId, Control inner, string insertAfterBlockId, int blockIndex, StackPanel host)
    {
        var selected = string.Equals(vm.SelectedBlockId, selectId, StringComparison.Ordinal);
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*") };
        Control? gripControl = null;
        if (vm.IsEditMode)
        {
            var insert = ToolbarBuilder.CreateInsertButton(
                vm, "Insert a block after this note", insertAfterBlockId);
            insert.Margin = new Thickness(0, 2, 2, 0);
            insert.Opacity = selected ? 1 : 0.65;
            Grid.SetColumn(insert, 0);
            row.Children.Add(insert);

            var grip = BlockGripCui.Build(vm, selectId);
            gripControl = grip;
            grip.Margin = new Thickness(0, 2, 4, 0);
            Grid.SetColumn(grip, 1);
            row.Children.Add(grip);
        }
        Grid.SetColumn(inner, 2);
        row.Children.Add(inner);
        var border = new Border
        {
            Child = row,
            BorderThickness = new Thickness(3, 0, 0, 0),
            BorderBrush = selected ? BoardsTheme.AccentBrush : Brushes.Transparent,
            Padding = new Thickness(9, 2, 0, 2),
            Margin = new Thickness(0, 0, 0, 10),
            Background = Brushes.Transparent,
        };
        border.Tag = new BlockReorderRow(selectId, blockIndex);
        if (gripControl is not null)
            BlockReorderPointer.Attach(gripControl, host, vm, selectId, blockIndex);
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
        _ => BuildTextBlock(vm, block),
    };

    private static bool IsListKind(string kind) =>
        kind is "checklist" or "bulleted" or "numbered";

    private static bool TryOpenInlineInsertMenu(TextBox editor)
    {
        foreach (var row in editor.GetVisualAncestors().OfType<Grid>())
        {
            var button = row.Children.OfType<Button>().FirstOrDefault(ToolbarBuilder.HasInsertMenu);
            if (button is not null && ToolbarBuilder.OpenInsertMenu(button)) return true;
        }
        return false;
    }

    private static StackPanel BlockShell(BoardsViewModel vm, RichBoardBlock block)
    {
        // No visible header: blocks read as document content. Management lives
        // in the block context menu (right-click) and the contextual strip.
        var shell = new StackPanel { Orientation = Orientation.Vertical };
        if (vm.IsEditMode)
        {
            var menu = new ContextMenu();
            var delete = new MenuItem { Header = "Delete block" };
            Avalonia.Automation.AutomationProperties.SetName(delete, "Delete block");
            var captured = block.Id;
            delete.Click += async (_, _) => await vm.DeleteBlockAsync(captured);
            menu.Items.Add(delete);
            shell.ContextMenu = menu;
        }
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
            IsReadOnly = !vm.IsEditMode,
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
        ThemeApplier.SetDocumentTextSurface(box, box.Background ?? Brushes.Transparent);
        ToolTip.SetTip(box, (isHeading ? "Heading" : "Paragraph") + " — select to format");
        AutomationProperties.SetName(box, (isHeading ? "Heading" : "Paragraph") + " editor");
        var captured = block.Id;
        box.TextChanged += (_, _) =>
        {
            if (vm.IsEditMode && string.Equals(box.Text, "/", StringComparison.Ordinal) &&
                box.CaretIndex == 1 && box.SelectionStart == box.SelectionEnd &&
                TryOpenInlineInsertMenu(box))
            {
                // The slash triggers the existing searchable insertion menu but
                // never becomes document content; clearing it raises this handler
                // with the empty value before search receives subsequent typing.
                box.Text = string.Empty;
                box.CaretIndex = 0;
                return;
            }
            vm.EditText("t_" + captured, box.Text ?? string.Empty);
        };
        box.GotFocus += (_, _) => vm.FocusBlock(captured);
        box.KeyDown += async (_, args) =>
        {
            if (!vm.IsEditMode) return;
            if (args.Key == Key.Enter && (args.KeyModifiers.HasFlag(KeyModifiers.Control) ||
                                          box.SelectionStart == box.SelectionEnd))
            {
                args.Handled = true;
                if (box.SelectionStart != box.SelectionEnd)
                {
                    vm.Set("StatusText", "Collapse the selection before splitting this text block.");
                    return;
                }
                await vm.SplitTextBlockAtAsync(captured, box.CaretIndex);
            }
            else if (args.Key == Key.Back && box.SelectionStart == box.SelectionEnd && box.CaretIndex == 0)
            {
                if (await vm.MergeTextBlockWithPreviousAsync(captured))
                    args.Handled = true;
            }
        };
        box.AttachedToVisualTree += (_, _) =>
        {
            if (!vm.TryConsumePendingTextFocus(vm.SelectedPageId, captured, out var caretIndex)) return;
            box.Focus();
            box.CaretIndex = Math.Clamp(caretIndex, 0, (box.Text ?? string.Empty).Length);
        };
        vm.BindTextSelection(box, captured);
        shell.Children.Add(box);
        return shell;
    }

    // Renderer projection only: the SAME persisted RichTextRun values shape the
    // existing input. No parallel document/editor or visual-only text overlay.
    internal static void UpdateTextPresentation(TextBox input, RichBoardBlock block, RichStyleView? style)
    {
        if (!BoardTextRanges.Matches(block) || !string.Equals(input.Text ?? string.Empty, block.Text, StringComparison.Ordinal))
        {
            CuiFormattedTextInput.SetPresentation(input, null);
            return;
        }
        static Color? Colour(string? value) => Color.TryParse(value, out var colour) ? colour : null;
        static BaselineAlignment Baseline(string value) => value switch
        {
            "subscript" => BaselineAlignment.Subscript, "superscript" => BaselineAlignment.Superscript,
            _ => BaselineAlignment.Baseline
        };
        var runs = new List<CuiFormattedTextRun>();
        if (block.Runs.Count > 0)
        {
            foreach (var run in block.Runs.Where(run => run.Text.Length > 0))
                runs.Add(new CuiFormattedTextRun(run.Text,
                    run.Bold ? FontWeight.Bold : FontWeight.Normal, run.Italic ? FontStyle.Italic : FontStyle.Normal,
                    run.FontFamily, run.FontSize, run.Underline, run.StrikeThrough,
                    Baseline(run.Baseline.ToString().ToLowerInvariant()), Colour(run.Foreground), Colour(run.Background)));
        }
        else if (block.Text.Length > 0)
        {
            // Empty run lists use the existing block/named-style presentation.
            // Persisted explicit run booleans override style booleans above;
            // empty/zero run font and colour values inherit the input defaults.
            runs.Add(new CuiFormattedTextRun(block.Text, input.FontWeight, input.FontStyle,
                Underline: block.Underline || style?.Underline == true,
                StrikeThrough: block.Strike || style?.Strike == true,
                Baseline: Baseline(block.Baseline != "normal" ? block.Baseline : style?.Baseline ?? "normal")));
        }
        var current = CuiFormattedTextInput.GetPresentation(input);
        if (current is not null && current.Text == block.Text && current.Runs.SequenceEqual(runs)) return;
        CuiFormattedTextInput.SetPresentation(input, new CuiFormattedText(block.Text, runs));
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

    private static Control BuildChecklistAddButton(BoardsViewModel vm, string firstBlockId)
    {
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
        add.IsVisible = vm.IsEditMode;
        ToolTip.SetTip(add, "Add a checklist item");
        AutomationProperties.SetName(add, "Add checklist item");
        add.Click += async (_, _) => await vm.AddChecklistItemAsync(ParentKey(firstBlockId));
        return add;
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
            IsReadOnly = !vm.IsEditMode,
            FontSize = 14,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(2, 4),
            FontWeight = item.Bold ? FontWeight.Bold : FontWeight.Normal,
            FontStyle = item.Italic ? FontStyle.Italic : FontStyle.Normal,
            Foreground = item.IsChecked ? BoardsTheme.SecondaryTextBrush : BoardsTheme.TextBrush,
        };
        ThemeApplier.SetDocumentTextSurface(text, Brushes.Transparent);
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
            IsVisible = vm.IsEditMode,
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
        {
            var height = r < block.RowHeights.Count ? block.RowHeights[r] : 0;
            grid.RowDefinitions.Add(new RowDefinition(height > 0 ? new GridLength(height) : GridLength.Auto));
        }
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
                block.CellSpans.TryGetValue(key, out var cellSpan);
                if (cellSpan?.CoveredByCellId is not null) continue;
                var rowSpan = Math.Clamp(cellSpan?.RowSpan ?? 1, 1, rows - r);
                var columnSpan = Math.Clamp(cellSpan?.ColumnSpan ?? 1, 1, cols - c);
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
                ThemeApplier.SetDocumentTextSurface(box, box.Background ?? Brushes.Transparent);
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
                box.GotFocus += (_, _) => vm.FocusCell(captured, rr, cc);
                var frame = new Border
                {
                    Child = box,
                    BorderBrush = BoardsTheme.BorderBrush,
                    BorderThickness = new Thickness(0, 0, c + columnSpan < cols ? 1 : 0, r + rowSpan < rows ? 1 : 0),
                    Background = box.Background,
                };
                box.Background = Brushes.Transparent;
                Grid.SetRow(frame, r);
                Grid.SetColumn(frame, c);
                Grid.SetRowSpan(frame, rowSpan);
                Grid.SetColumnSpan(frame, columnSpan);
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
        var height = image?.Height ?? 0;
        var keepAspect = image?.KeepAspectRatio != false;
        var rotation = image?.RotationDegrees ?? 0;
        var cropLeft = image?.CropLeft ?? 0;
        var cropTop = image?.CropTop ?? 0;
        var cropRight = image?.CropRight ?? 0;
        var cropBottom = image?.CropBottom ?? 0;
        var fitMode = image?.FitMode ?? "contain";
        var caption = image?.Caption ?? string.Empty;
        frame.PointerPressed += (_, _) => vm.FocusBlock(captured);
        _ = LoadImageIntoAsync(frame, vm, captured, alignment, alt, displayName, width, height,
            keepAspect, rotation, cropLeft, cropTop, cropRight, cropBottom, fitMode, caption);
        shell.Children.Add(frame);
        return shell;
    }

    private static async Task LoadImageIntoAsync(
        Border frame, BoardsViewModel vm, string blockId,
        string? alignment, string? altText, string displayName, double width, double height,
        bool keepAspect, double rotation, double cropLeft, double cropTop, double cropRight, double cropBottom,
        string fitMode, string caption)
    {
        // Embedded bytes resolve synchronously (cached per document version);
        // only sidecar payloads take the async path below.
        var immediate = vm.TryResolveImageBytes(blockId);
        if (immediate is not null)
        {
            SetImageContent(frame, immediate, alignment, altText, displayName, width, height,
                keepAspect, rotation, cropLeft, cropTop, cropRight, cropBottom, fitMode, caption);
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
            SetImageContent(frame, captured, alignment, altText, displayName, width, height,
                keepAspect, rotation, cropLeft, cropTop, cropRight, cropBottom, fitMode, caption);
        else
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                SetImageContent(frame, captured, alignment, altText, displayName, width, height,
                    keepAspect, rotation, cropLeft, cropTop, cropRight, cropBottom, fitMode, caption));
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
        Border frame, byte[] bytes, string? alignment, string? altText, string displayName, double width,
        double height, bool keepAspect, double rotation, double cropLeft, double cropTop,
        double cropRight, double cropBottom, string fitMode, string caption)
    {
        try
        {
            using var stream = new MemoryStream(bytes);
            var bitmap = new Bitmap(stream);
            IImage source = bitmap;
            var left = Math.Clamp((int)Math.Round(bitmap.PixelSize.Width * cropLeft), 0, bitmap.PixelSize.Width - 1);
            var top = Math.Clamp((int)Math.Round(bitmap.PixelSize.Height * cropTop), 0, bitmap.PixelSize.Height - 1);
            var right = Math.Clamp((int)Math.Round(bitmap.PixelSize.Width * (1 - cropRight)), left + 1, bitmap.PixelSize.Width);
            var bottom = Math.Clamp((int)Math.Round(bitmap.PixelSize.Height * (1 - cropBottom)), top + 1, bitmap.PixelSize.Height);
            if (left > 0 || top > 0 || right < bitmap.PixelSize.Width || bottom < bitmap.PixelSize.Height)
                source = new CroppedBitmap(bitmap, new PixelRect(left, top, right - left, bottom - top));
            var stretch = fitMode switch
            {
                "cover" => Stretch.UniformToFill,
                "fill" => Stretch.Fill,
                "natural" => Stretch.None,
                _ => Stretch.Uniform
            };
            var fitHeight = height > 0 ? height : fitMode == "cover" && width > 0
                ? Math.Min(720, width * 0.67)
                : double.NaN;
            var control = new Image
            {
                Source = source,
                Width = fitMode == "natural" || width <= 0 ? double.NaN : width,
                Height = fitMode == "cover" ? fitHeight : keepAspect || height <= 0 ? double.NaN : height,
                MaxWidth = 720,
                MaxHeight = 720,
                Stretch = stretch,
                HorizontalAlignment = alignment switch
                {
                    "center" => HorizontalAlignment.Center,
                    "right" => HorizontalAlignment.Right,
                    _ => HorizontalAlignment.Left,
                },
                RenderTransform = Math.Abs(rotation) < 0.001 ? null : new RotateTransform(rotation),
                RenderTransformOrigin = RelativePoint.Center,
                Margin = new Thickness(0, 4, 0, 4),
            };
            AutomationProperties.SetName(control, "Image: " +
                (!string.IsNullOrEmpty(altText) ? altText : displayName));
            var content = new StackPanel { Spacing = 4 };
            content.Children.Add(control);
            if (!string.IsNullOrWhiteSpace(caption))
                content.Children.Add(new TextBlock
                {
                    Text = caption,
                    TextWrapping = TextWrapping.Wrap,
                    FontSize = 12,
                    Foreground = BoardsTheme.SecondaryTextBrush,
                    HorizontalAlignment = control.HorizontalAlignment,
                });
            frame.Child = content;
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
        var graph = vm.GetGraphForRendering(block.Id) ?? new RichGraphView();
        // Rendered plot dominates; editing lives in chips + collapsed settings.
        var graphVariables = vm.GetGraphSliderValues(block.Id);
        var graphCanvas = BuildGraphCanvas(graph, graphVariables);
        shell.Children.Add(graphCanvas);
        var capturedBlock = block.Id;
        var nav = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
        AddSmallButton(nav, "◀", "Pan graph left", () => vm.GraphPanZoom(capturedBlock, -0.2, 0, 1));
        AddSmallButton(nav, "▶", "Pan graph right", () => vm.GraphPanZoom(capturedBlock, 0.2, 0, 1));
        AddSmallButton(nav, "▲", "Pan graph up", () => vm.GraphPanZoom(capturedBlock, 0, 0.2, 1));
        AddSmallButton(nav, "▼", "Pan graph down", () => vm.GraphPanZoom(capturedBlock, 0, -0.2, 1));
        AddSmallButton(nav, "+", "Zoom graph in", () => vm.GraphPanZoom(capturedBlock, 0, 0, 1.5));
        AddSmallButton(nav, "−", "Zoom graph out", () => vm.GraphPanZoom(capturedBlock, 0, 0, 1 / 1.5));
        shell.Children.Add(nav);
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
        add.Click += (_, _) => vm.GraphAddExpression(capturedBlock);
        chips.Children.Add(add);
        chips.IsVisible = vm.IsEditMode;
        shell.Children.Add(chips);

        if (vm.IsEditMode && graph.Expressions.Count == 0)
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
            IsVisible = vm.IsEditMode,
        };
        var panel = new StackPanel { Orientation = Orientation.Vertical };
        foreach (var expr in graph.Expressions)
            panel.Children.Add(BuildGraphExpressionRow(vm, block.Id, expr));

        if (vm.IsEditMode)
        {
            foreach (var slider in graph.Sliders)
                panel.Children.Add(BuildGraphSliderEditor(vm, block.Id, slider));
            var addSlider = new Button { Content = "+ Slider", Margin = new Thickness(0, 4, 0, 4) };
            ToolTip.SetTip(addSlider, "Add a variable slider to this graph");
            AutomationProperties.SetName(addSlider, "Add graph slider");
            addSlider.Click += (_, _) => vm.AddGraphSlider(block.Id);
            panel.Children.Add(addSlider);
        }
        else
        {
            if (graph.Sliders.Count > 0)
            {
                shell.Children.Add(GraphSliderCui.Build(vm, block.Id, graph, () =>
                {
                    var updated = BuildGraphCanvas(graph, vm.GetGraphSliderValues(block.Id));
                    var currentIndex = shell.Children.IndexOf(graphCanvas);
                    if (currentIndex >= 0)
                    {
                        shell.Children.RemoveAt(currentIndex);
                        shell.Children.Insert(currentIndex, updated);
                        graphCanvas = updated;
                    }
                }));
            }
        }

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

    private static Control BuildGraphSliderEditor(BoardsViewModel vm, string blockId, RichGraphSliderView slider)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 2) };
        TextBox Field(string text, double width, string accessibleName)
        {
            var input = new TextBox { Text = text, Width = width, Margin = new Thickness(0, 0, 5, 0) };
            AutomationProperties.SetName(input, accessibleName);
            return input;
        }
        var name = Field(slider.Name, 42, "Graph slider variable name");
        var minimum = Field(slider.Minimum.ToString("G"), 76, "Graph slider minimum");
        var maximum = Field(slider.Maximum.ToString("G"), 76, "Graph slider maximum");
        var step = Field(slider.Step.ToString("G"), 76, "Graph slider step");
        var defaultValue = Field(slider.DefaultValue.ToString("G"), 76, "Graph slider default value");
        ToolTip.SetTip(name, "Variable used in expressions, e.g. a");
        ToolTip.SetTip(minimum, "Minimum value");
        ToolTip.SetTip(maximum, "Maximum value");
        ToolTip.SetTip(step, "Slider step");
        ToolTip.SetTip(defaultValue, "Saved default value");
        void Commit()
        {
            if (double.TryParse(minimum.Text, out var min) && double.TryParse(maximum.Text, out var max) &&
                double.TryParse(step.Text, out var increment) && double.TryParse(defaultValue.Text, out var initial))
                vm.ConfigureGraphSlider(blockId, slider.VariableId, name.Text ?? string.Empty,
                    min, max, increment, initial);
        }
        name.LostFocus += (_, _) => Commit();
        minimum.LostFocus += (_, _) => Commit();
        maximum.LostFocus += (_, _) => Commit();
        step.LostFocus += (_, _) => Commit();
        defaultValue.LostFocus += (_, _) => Commit();
        var remove = new Button { Content = "Remove", Padding = new Thickness(6, 2) };
        ToolTip.SetTip(remove, "Remove this slider definition");
        AutomationProperties.SetName(remove, $"Remove graph slider {slider.Name}");
        remove.Click += (_, _) => vm.RemoveGraphSlider(blockId, slider.VariableId);
        row.Children.Add(new TextBlock { Text = "Variable", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 5, 0) });
        row.Children.Add(name);
        row.Children.Add(minimum);
        row.Children.Add(maximum);
        row.Children.Add(step);
        row.Children.Add(defaultValue);
        row.Children.Add(remove);
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

    private static Control BuildGraphCanvas(RichGraphView graph, IReadOnlyDictionary<string, double>? variables = null)
    {
        var canvas = new Canvas
        {
            Width = GraphWidth,
            Height = GraphHeight,
            ClipToBounds = true,
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
                expr.Text, expr.DomainMin, expr.DomainMax, graph.XMin, graph.XMax, 200, variables);
            if (samples.Count == 0)
            {
                var mid = (graph.XMin + graph.XMax) / 2;
                if (!HavenGraphExpression.TryEvaluate(expr.Text, mid, out _, variables))
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
        remove.IsVisible = vm.IsEditMode;
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
        if (vm.IsCanvasLocked)
            shell.Children.Add(new TextBlock
            {
                Text = "Layout locked · unlock to move boxes; drawing remains available.",
                FontSize = 12,
                Foreground = BoardsTheme.SecondaryTextBrush,
                Margin = new Thickness(0, 0, 0, 4),
            });

        if (boxes.Count == 0)
        {
            shell.Children.Add(new TextBlock
            {
                Text = vm.IsCanvasLocked
                    ? "No freeform objects yet — unlock the layout to add a box."
                    : "No freeform objects yet — add a box to arrange ideas freely.",
                FontSize = 13,
                Foreground = BoardsTheme.SecondaryTextBrush,
                Margin = new Thickness(0, 0, 0, 4),
            });
        }

        string? selectedId = null;
        Border? selectedBorder = null;
        // The contract canvas starts at 1200×900 and expands as objects move.
        // Size from persisted object bounds so saved content beyond the first
        // viewport remains reachable after reopen instead of being clipped.
        var canvasWidth = Math.Clamp(
            Math.Max(1200, Math.Max(boxes.Select(box => box.CanvasWidth).DefaultIfEmpty(1200).Max(),
                boxes.Select(box => box.X + box.Width + 40).DefaultIfEmpty(1200).Max())),
            1200, 100000);
        var canvasHeight = Math.Clamp(
            Math.Max(900, Math.Max(boxes.Select(box => box.CanvasHeight).DefaultIfEmpty(900).Max(),
                boxes.Select(box => box.Y + box.Height + 40).DefaultIfEmpty(900).Max())),
            900, 100000);
        var canvas = new Canvas
        {
            Name = "FreeformCanvas",
            Width = canvasWidth,
            Height = canvasHeight,
            Background = BoardsTheme.CardBrush,
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
                IsHitTestVisible = vm.IsEditMode && !vm.IsCanvasLocked,
                Focusable = vm.IsEditMode && !vm.IsCanvasLocked,
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
                if (!vm.IsEditMode || vm.IsCanvasLocked) return;
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
                if (!dragging || vm.IsCanvasLocked)
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
                if (!vm.IsEditMode || vm.IsCanvasLocked) return;
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
        var viewport = new ScrollViewer
        {
            Content = canvas,
            Height = 360,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Margin = new Thickness(0, 4, 0, 4),
        };
        AutomationProperties.SetName(viewport, "Freeform canvas viewport");
        shell.Children.Add(viewport);

        var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 0) };
        var addBox = new Button
        {
            Content = "+ Box",
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Foreground = BoardsTheme.SecondaryTextBrush,
            FontSize = 13,
            Padding = new Thickness(8, 4),
            IsVisible = vm.IsEditMode && !vm.IsCanvasLocked,
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
            IsVisible = vm.IsEditMode && !vm.IsCanvasLocked,
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
            Text = vm.IsCanvasLocked ? "Layout locked" : "Drag to move · arrow keys nudge",
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

