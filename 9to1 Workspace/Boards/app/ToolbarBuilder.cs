// ToolbarBuilder constructs the restrained writing toolbar: style gallery
// with live previews, icon format toggles with selected state, font/size,
// color palettes, list + alignment + indent controls, and the Insert popup.
// Rebuilt on selection change (separate subtree — editor focus is preserved).

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;

namespace CakeOS.Apps.Boards.App;

public static class ToolbarBuilder
{
    private static readonly (string Name, string Hex)[] TextColors =
    [
        ("Black", "#FF111111"), ("Dark grey", "#FF5F6368"), ("Grey", "#FF9AA0A6"),
        ("White", "#FFFFFFFF"), ("Red", "#FFD32F2F"), ("Orange", "#FFE8710A"),
        ("Yellow", "#FFF9AB00"), ("Green", "#FF1E8E3E"), ("Blue", "#FF1A73E8"),
        ("Purple", "#FF9334E6"), ("Navy", "#FF1B4F72"), ("Brown", "#FF795548"),
    ];

    private static readonly (string Name, string Hex)[] Highlights =
    [
        ("None", ""), ("Yellow", "#FFFFFF00"), ("Green", "#FFCCFF90"),
        ("Blue", "#FFB3E5FC"), ("Pink", "#FFF8BBD0"), ("Orange", "#FFFFE0B2"),
        ("Grey", "#FFE0E0E0"), ("Peach", "#FFFFF3C4"),
    ];

    private static readonly string[] FontFamilies =
        ["Default", "Inter", "Georgia", "Cascadia Mono", "Segoe UI", "Times New Roman"];

    private static readonly double[] FontSizes = [11, 12, 14, 16, 18, 20, 24, 26, 28, 32];

    public static void Rebuild(StackPanel host, BoardsViewModel vm)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(vm);
        host.Children.Clear();

        var bar = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(12, 8, 12, 8) };
        if (vm.IsDrawMode)
        {
            BuildDrawToolbar(bar, vm);
            host.Children.Add(bar);
            return;
        }
        bar.Children.Add(BuildStyleGallery(vm));
        bar.Children.Add(Separator());
        var selected = vm.FindBlock(vm.SelectedBlockId);
        bar.Children.Add(Toggle(vm, BoardsIcons.Bold, "Bold", "Toggle bold (Ctrl+B)",
            selected?.Bold == true, async () => await vm.DispatchAsync("ToggleBold", null)));
        bar.Children.Add(Toggle(vm, BoardsIcons.Italic, "Italic", "Toggle italic (Ctrl+I)",
            selected?.Italic == true, async () => await vm.DispatchAsync("ToggleItalic", null)));
        bar.Children.Add(Toggle(vm, BoardsIcons.Underline, "Underline", "Toggle underline (Ctrl+U)",
            selected?.Underline == true, async () => await vm.DispatchAsync("ToggleUnderline", null)));
        bar.Children.Add(Toggle(vm, "S", "Strikethrough", "Toggle strikethrough",
            selected?.Strike == true, async () => await vm.DispatchAsync("ToggleStrike", null), textFallback: true));
        bar.Children.Add(Toggle(vm, BoardsIcons.Subscript, "Subscript", "Toggle subscript",
            selected?.Baseline == "subscript", async () => await vm.DispatchAsync("ToggleSub", null)));
        bar.Children.Add(Toggle(vm, BoardsIcons.Superscript, "Superscript", "Toggle superscript",
            selected?.Baseline == "superscript", async () => await vm.DispatchAsync("ToggleSup", null)));
        bar.Children.Add(Separator());
        bar.Children.Add(FontCombo(vm, selected));
        bar.Children.Add(SizeCombo(vm, selected));
        bar.Children.Add(ColorPopupButton(vm, "A", "Text colour", isHighlight: false));
        bar.Children.Add(ColorPopupButton(vm, "H", "Highlight colour", isHighlight: true));
        bar.Children.Add(Separator());
        bar.Children.Add(Toggle(vm, BoardsIcons.BulletedList, "Bulleted list", "Toggle bulleted list",
            selected?.Kind == "bulleted", async () => await vm.ConvertSelectedKindAsync("bulleted")));
        bar.Children.Add(Toggle(vm, BoardsIcons.NumberedList, "Numbered list", "Toggle numbered list",
            selected?.Kind == "numbered", async () => await vm.ConvertSelectedKindAsync("numbered")));
        bar.Children.Add(AlignGroup(vm, selected));
        bar.Children.Add(IndentButton(vm, "−", "Decrease indent", () => vm.ShiftSelectedIndent(-1)));
        bar.Children.Add(IndentButton(vm, "+", "Increase indent", () => vm.ShiftSelectedIndent(1)));
        bar.Children.Add(Separator());
        bar.Children.Add(DrawButton(vm));
        bar.Children.Add(CreateInsertButton(vm));
        host.Children.Add(bar);
    }

    private static Control Separator() => new Border
    {
        Width = 1,
        Background = BoardsTheme.BorderBrush,
        Margin = new Thickness(6, 4),
        VerticalAlignment = VerticalAlignment.Stretch,
    };

    private static ToggleButton Toggle(
        BoardsViewModel vm, string glyph, string name, string tip, bool active, Func<Task> tapped,
        bool textFallback = false)
    {
        var toggle = new ToggleButton
        {
            IsChecked = active,
            Padding = new Thickness(8, 6),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Margin = new Thickness(1, 0),
        };
        toggle.Content = textFallback
            ? (object)new TextBlock
            {
                Text = name,
                FontSize = 13,
                FontWeight = FontWeight.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
            }
            : BoardsIcons.Glyph(glyph, 15, name);
        ToolTip.SetTip(toggle, tip);
        Avalonia.Automation.AutomationProperties.SetName(toggle, name);
        toggle.Click += async (_, _) => await tapped();
        return toggle;
    }

    private static Control FontCombo(BoardsViewModel vm, RichBoardBlock? selected)
    {
        var combo = new ComboBox { Width = 128, Margin = new Thickness(4, 0) };
        ToolTip.SetTip(combo, "Font family");
        Avalonia.Automation.AutomationProperties.SetName(combo, "Font family");
        combo.ItemsSource = FontFamilies.Select(f => new ComboBoxItem { Content = f }).ToList();
        var current = string.IsNullOrEmpty(selected?.FontFamily) ? "Default" : selected!.FontFamily;
        combo.SelectedItem = combo.ItemsSource.Cast<ComboBoxItem>()
            .FirstOrDefault(i => (string?)i.Content == current);
        combo.SelectionChanged += (_, _) =>
        {
            if (combo.SelectedItem is ComboBoxItem item && (string?)item.Content is string family)
                vm.SetSelectedFontFamily(family == "Default" ? string.Empty : family);
        };
        return combo;
    }

    private static Control SizeCombo(BoardsViewModel vm, RichBoardBlock? selected)
    {
        var combo = new ComboBox { Width = 72, Margin = new Thickness(0, 0, 4, 0) };
        ToolTip.SetTip(combo, "Font size");
        Avalonia.Automation.AutomationProperties.SetName(combo, "Font size");
        combo.ItemsSource = FontSizes.Select(s => new ComboBoxItem { Content = s.ToString("0") }).ToList();
        var current = selected?.FontSize > 0 ? selected!.FontSize : 14;
        ComboBoxItem? best = null;
        foreach (var item in combo.ItemsSource.Cast<ComboBoxItem>())
        {
            if (double.TryParse((string?)item.Content, out var size) && size <= current)
                best = item;
        }
        combo.SelectedItem = best;
        combo.SelectionChanged += (_, _) =>
        {
            if (combo.SelectedItem is ComboBoxItem item &&
                double.TryParse((string?)item.Content, out var size))
                vm.SetSelectedFontSize(size);
        };
        return combo;
    }

    private static Control ColorPopupButton(BoardsViewModel vm, string label, string tip, bool isHighlight)
    {
        var button = new Button
        {
            Content = new TextBlock
            {
                Text = label,
                FontSize = 13,
                FontWeight = FontWeight.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
            },
            Padding = new Thickness(8, 6),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Margin = new Thickness(1, 0),
        };
        ToolTip.SetTip(button, tip);
        Avalonia.Automation.AutomationProperties.SetName(button, tip);
        var popup = new Popup { PlacementTarget = button, Placement = PlacementMode.Bottom };
        var wrap = new WrapPanel { Orientation = Orientation.Horizontal, MaxWidth = 220 };
        var palette = isHighlight ? Highlights : TextColors;
        foreach (var (name, hex) in palette)
        {
            var swatch = new Button
            {
                Width = 32,
                Height = 32,
                Margin = new Thickness(3),
                Padding = new Thickness(0),
                Background = string.IsNullOrEmpty(hex) ? Brushes.Transparent : BoardsTheme.Brush(hex),
                BorderBrush = BoardsTheme.BorderBrush,
                BorderThickness = new Thickness(1),
            };
            ToolTip.SetTip(swatch, name);
            Avalonia.Automation.AutomationProperties.SetName(swatch, (isHighlight ? "Highlight " : "Text colour ") + name);
            swatch.Click += (_, _) =>
            {
                if (isHighlight)
                    vm.SetSelectedColors(vm.FindBlock(vm.SelectedBlockId)?.Foreground ?? string.Empty, hex);
                else
                    vm.SetSelectedColors(hex, vm.FindBlock(vm.SelectedBlockId)?.Background ?? string.Empty);
                popup.Close();
            };
            wrap.Children.Add(swatch);
        }
        popup.Child = new Border
        {
            Background = BoardsTheme.CardBrush,
            BorderBrush = BoardsTheme.BorderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(8),
            Child = wrap,
        };
        button.Click += (_, _) => popup.Open();
        return button;
    }

    private static Control AlignGroup(BoardsViewModel vm, RichBoardBlock? selected)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(Toggle(vm, BoardsIcons.AlignLeft, "Align left", "Align left",
            selected?.Alignment == "left", async () => await vm.DispatchAsync("AlignLeft", null)));
        row.Children.Add(Toggle(vm, BoardsIcons.AlignCenter, "Align center", "Align center",
            selected?.Alignment == "center", async () => await vm.DispatchAsync("AlignCenter", null)));
        row.Children.Add(Toggle(vm, BoardsIcons.AlignRight, "Align right", "Align right",
            selected?.Alignment == "right", async () => await vm.DispatchAsync("AlignRight", null)));
        return row;
    }

    private static Control IndentButton(BoardsViewModel vm, string label, string tip, Action tapped)
    {
        var button = new Button
        {
            Content = new TextBlock { Text = label, FontSize = 13, FontWeight = FontWeight.SemiBold },
            Padding = new Thickness(8, 6),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Margin = new Thickness(1, 0),
        };
        ToolTip.SetTip(button, tip);
        Avalonia.Automation.AutomationProperties.SetName(button, tip);
        button.Click += (_, _) => tapped();
        return button;
    }

    private static Control BuildStyleGallery(BoardsViewModel vm) => BuildStyleGalleryInternal(vm);

    private static Control BuildStyleGalleryInternal(BoardsViewModel vm)
    {
        var combo = new ComboBox { Width = 190, Margin = new Thickness(0, 0, 4, 0) };
        ToolTip.SetTip(combo, "Paragraph style (custom styles appear here)");
        Avalonia.Automation.AutomationProperties.SetName(combo, "Paragraph style");
        var items = new List<ComboBoxItem>();
        foreach (var style in vm.StyleList)
        {
            var preview = new TextBlock
            {
                Text = style.Name,
                FontSize = style.FontSize > 0 ? Math.Clamp(style.FontSize, 11, 22) : 14,
                FontWeight = style.Bold ? FontWeight.Bold : FontWeight.Normal,
                FontStyle = style.Italic ? FontStyle.Italic : FontStyle.Normal,
                VerticalAlignment = VerticalAlignment.Center,
            };
            if (!string.IsNullOrEmpty(style.FontFamily))
                preview.FontFamily = new FontFamily(style.FontFamily);
            if (BoardsTheme.TryBrush(style.Foreground, out var foreground))
                preview.Foreground = foreground;
            var item = new ComboBoxItem { Content = preview, Tag = style.Id };
            ToolTip.SetTip(item, (style.IsBuiltIn ? "Built-in style: " : "Custom style: ") + style.Name);
            items.Add(item);
        }
        var selected = vm.FindBlock(vm.SelectedBlockId)?.StyleId;
        combo.ItemsSource = items;
        combo.SelectedItem = items.FirstOrDefault(i => (string?)i.Tag == selected);
        combo.SelectionChanged += (_, _) =>
        {
            if (combo.SelectedItem is ComboBoxItem item && (string?)item.Tag is string styleId)
                _ = vm.ApplyStyleToSelectedAsync(styleId);
        };
        return combo;
    }

    private static Control DrawButton(BoardsViewModel vm)
    {
        var button = new ToggleButton
        {
            Content = BoardsIcons.Glyph(BoardsIcons.Ink, 16, "Draw"),
            IsChecked = vm.IsDrawMode,
            Padding = new Thickness(8, 6),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
        };
        ToolTip.SetTip(button, "Draw — show pen, highlighter, eraser and colour tools");
        Avalonia.Automation.AutomationProperties.SetName(button, "Draw");
        button.Click += async (_, _) =>
        {
            if (vm.IsDrawMode)
                vm.ExitDrawingMode();
            else
                await vm.ActivateDrawingAsync();
        };
        return button;
    }

    public static Control CreateInsertButton(BoardsViewModel vm, string? tooltip = null)
    {
        var button = BoardsIcons.IconButton(BoardsIcons.Add, "Insert", tooltip ?? "Insert a block (grouped menu with search)");
        var popup = new Popup { PlacementTarget = button, Placement = PlacementMode.Bottom };
        var panel = new StackPanel { Orientation = Orientation.Vertical, MinWidth = 260 };
        var search = new TextBox { PlaceholderText = "Search blocks and styles…", Margin = new Thickness(0, 0, 0, 8) };
        Avalonia.Automation.AutomationProperties.SetName(search, "Search insert menu");
        var list = new StackPanel { Orientation = Orientation.Vertical };
        panel.Children.Add(search);
        panel.Children.Add(list);
        void Fill(string filter)
        {
            list.Children.Clear();
            AddGroup(list, "Text", [
                ("Paragraph", "paragraph"), ("Title", "style:title"), ("Header", "style:heading-1"),
                ("Quote", "style:quote"), ("Code", "style:code"),
            ], vm, popup, filter);
            var customs = vm.StyleList.Where(s => !s.IsBuiltIn).ToList();
            if (customs.Count > 0)
                AddGroup(list, "Custom styles", customs.Select(s => (s.Name, "style:" + s.Id)).ToList(), vm, popup, filter);
            AddGroup(list, "Content", [
                ("Checklist", "checklist"), ("Bulleted list", "bulleted"), ("Numbered list", "numbered"),
                ("Table", "table"), ("Graph", "graph"), ("Image…", "image"), ("Attachment…", "attachment"),
            ], vm, popup, filter);
            AddGroup(list, "Drawing", [("Drawing", "ink"), ("Freeform box", "freeform")], vm, popup, filter);
            AddGroup(list, "Document types", [("Canvas document — coming soon", "")], vm, popup, filter);
            AddGroup(list, "Other", [("Divider", "divider")], vm, popup, filter);
        }
        search.TextChanged += (_, _) => Fill(search.Text ?? string.Empty);
        Fill(string.Empty);
        popup.Child = new Border
        {
            Background = BoardsTheme.CardBrush,
            BorderBrush = BoardsTheme.BorderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12),
            MaxHeight = 420,
            Child = new ScrollViewer { Content = panel },
        };
        button.Click += (_, _) =>
        {
            search.Text = string.Empty;
            Fill(string.Empty);
            popup.Open();
        };
        return button;
    }

    private static void AddGroup(
        StackPanel list, string heading, IReadOnlyList<(string Label, string Tag)> entries,
        BoardsViewModel vm, Popup popup, string filter)
    {
        var matches = string.IsNullOrWhiteSpace(filter)
            ? entries
            : entries.Where(e => e.Label.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToList();
        if (matches.Count == 0)
            return;
        list.Children.Add(new TextBlock
        {
            Text = heading.ToUpperInvariant(),
            FontSize = 11,
            FontWeight = FontWeight.SemiBold,
            Foreground = BoardsTheme.SecondaryTextBrush,
            Margin = new Thickness(0, 6, 0, 2),
        });
        foreach (var (label, tag) in matches)
        {
            var item = new Button
            {
                Content = label,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(8, 6),
            };
            Avalonia.Automation.AutomationProperties.SetName(item, "Insert " + label);
            if (string.IsNullOrEmpty(tag))
            {
                item.IsEnabled = false;
                ToolTip.SetTip(item, "Canvas documents are reserved for the future Canvas workspace");
            }
            item.Click += (_, _) =>
            {
                if (string.IsNullOrEmpty(tag))
                    return;
                popup.Close();
                _ = vm.InsertKindAsync(tag);
            };
            list.Children.Add(item);
        }
    }

    private static void BuildDrawToolbar(WrapPanel bar, BoardsViewModel vm)
    {
        bar.Children.Add(DrawButton(vm));
        bar.Children.Add(Separator());
        foreach (var tool in new[] { "Pen", "Highlighter", "Eraser", "Select" })
        {
            var button = new ToggleButton
            {
                Content = tool,
                IsChecked = string.Equals(vm.InkTool, tool, StringComparison.OrdinalIgnoreCase),
                Padding = new Thickness(8, 6),
                Margin = new Thickness(1, 0),
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
            };
            ToolTip.SetTip(button, tool + " drawing tool");
            Avalonia.Automation.AutomationProperties.SetName(button, tool + " drawing tool");
            button.Click += (_, _) => vm.SetInkTool(tool);
            bar.Children.Add(button);
        }
        bar.Children.Add(Separator());
        foreach (var (name, hex) in new[]
        {
            ("Black", "#FF111111"), ("Blue", "#FF1A73E8"), ("Red", "#FFD32F2F"),
            ("Green", "#FF1E8E3E"), ("Orange", "#FFE8710A"), ("Purple", "#FF9334E6")
        })
        {
            var swatch = new Button
            {
                Width = 24, Height = 24, Padding = new Thickness(0), Margin = new Thickness(2, 0),
                Background = BoardsTheme.Brush(hex), BorderBrush = BoardsTheme.BorderBrush,
                BorderThickness = new Thickness(string.Equals(vm.InkColor, hex, StringComparison.OrdinalIgnoreCase) ? 3 : 1),
                CornerRadius = new CornerRadius(12), VerticalAlignment = VerticalAlignment.Center,
            };
            ToolTip.SetTip(swatch, name + " ink");
            Avalonia.Automation.AutomationProperties.SetName(swatch, name + " ink");
            swatch.Click += (_, _) => vm.SetInkColor(hex);
            bar.Children.Add(swatch);
        }
        bar.Children.Add(Separator());
        foreach (var width in new[] { 2d, 4d, 8d, 12d })
        {
            var stroke = new ToggleButton
            {
                Content = width.ToString("0") + " px", IsChecked = Math.Abs(vm.InkWidth - width) < 0.01,
                Padding = new Thickness(7, 6), Margin = new Thickness(1, 0),
                Background = Brushes.Transparent, BorderThickness = new Thickness(0),
            };
            ToolTip.SetTip(stroke, "Stroke width " + width.ToString("0"));
            Avalonia.Automation.AutomationProperties.SetName(stroke, "Stroke width " + width.ToString("0"));
            stroke.Click += (_, _) => vm.SetInkWidth(width);
            bar.Children.Add(stroke);
        }
        bar.Children.Add(Separator());
        var undo = BoardsIcons.IconButton(BoardsIcons.Undo, "Undo drawing", "Undo the last drawing action");
        undo.Click += async (_, _) => await vm.DispatchAsync("Undo", null);
        var redo = BoardsIcons.IconButton(BoardsIcons.Redo, "Redo drawing", "Redo the last drawing action");
        redo.Click += async (_, _) => await vm.DispatchAsync("Redo", null);
        bar.Children.Add(undo);
        bar.Children.Add(redo);
        var zoomOut = new Button { Content = "Zoom −", Padding = new Thickness(7, 6), Margin = new Thickness(2, 0) };
        ToolTip.SetTip(zoomOut, "Zoom drawing out");
        Avalonia.Automation.AutomationProperties.SetName(zoomOut, "Zoom drawing out");
        zoomOut.Click += async (_, _) => await vm.SetInkZoomAsync(vm.InkZoom / 1.25);
        var reset = new Button { Content = "Reset view", Padding = new Thickness(7, 6), Margin = new Thickness(2, 0) };
        ToolTip.SetTip(reset, "Reset drawing view");
        Avalonia.Automation.AutomationProperties.SetName(reset, "Reset drawing view");
        reset.Click += async (_, _) => await vm.ResetInkViewAsync();
        bar.Children.Add(zoomOut);
        bar.Children.Add(reset);
    }
}

