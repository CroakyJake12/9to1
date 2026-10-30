// ToolbarBuilder constructs the restrained writing toolbar: style gallery
// with live previews, icon format toggles with selected state, font/size,
// color palettes, list + alignment + indent controls, and the Insert popup.
// Rebuilt on selection change (separate subtree — editor focus is preserved).

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using CakeOS.Cui.Runtime;
using System.Globalization;
using System.Runtime.CompilerServices;
using Avalonia.VisualTree;

namespace CakeOS.Apps.Boards.App;

public static class ToolbarBuilder
{
    private static readonly ConditionalWeakTable<Control, InsertMenuContext> InsertMenus = new();
    internal static InsertMenuContext? InsertMenuFor(Control control) =>
        InsertMenus.TryGetValue(control, out var context) ? context : null;

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
        Rebuild((Panel)host, vm);
    }

    public static void Rebuild(WrapPanel host, BoardsViewModel vm)
    {
        ArgumentNullException.ThrowIfNull(host);
        Rebuild((Panel)host, vm);
    }

    private static void Rebuild(Panel host, BoardsViewModel vm)
    {
        ArgumentNullException.ThrowIfNull(vm);
        vm = vm.ToolbarTarget;
        host.Children.Clear();

        // The authored ToolbarHost is a single-row CUI scroll host. Related commands
        // stay grouped, with the CUI-owned edge arrows preserving access to every tool.
        if (vm.IsDrawMode)
        {
            BuildDrawToolbar(host, vm);
            return;
        }
        host.Children.Add(Group(BuildStyleGallery(vm), Separator()));
        var selected = vm.FindBlock(vm.SelectedBlockId);
        // B/I/U/S range commands and mixed-state labels are authored in Boards.cui.
        host.Children.Add(Group(FontCombo(vm, selected), SizeCombo(vm, selected)));
        host.Children.Add(Group(
            Toggle(vm, BoardsIcons.Subscript, "Subscript", "Toggle subscript",
                selected?.Baseline == "subscript", async () => await vm.DispatchAsync("ToggleSub", null), iconSize: 18),
            Toggle(vm, BoardsIcons.Superscript, "Superscript", "Toggle superscript",
                selected?.Baseline == "superscript", async () => await vm.DispatchAsync("ToggleSup", null), iconSize: 18),
            Separator()));
        host.Children.Add(Group(
            ColorPopupButton(vm, BoardsIcons.TextColor, "Text colour", isHighlight: false),
            ColorPopupButton(vm, BoardsIcons.HighlightColor, "Highlight colour", isHighlight: true),
            Separator()));
        host.Children.Add(Group(
            Toggle(vm, BoardsIcons.BulletedList, "Bulleted list", "Toggle bulleted list",
                selected?.Kind == "bulleted", async () => await vm.ConvertSelectedKindAsync("bulleted"), iconSize: 18),
            Toggle(vm, BoardsIcons.NumberedList, "Numbered list", "Toggle numbered list",
                selected?.Kind == "numbered", async () => await vm.ConvertSelectedKindAsync("numbered"), iconSize: 18),
            Separator()));
        host.Children.Add(Group(AlignGroup(vm, selected), Separator()));
        host.Children.Add(Group(
            IndentButton(vm, BoardsIcons.IndentDecrease, "Decrease indent", () => vm.ShiftSelectedIndent(-1)),
            IndentButton(vm, BoardsIcons.IndentIncrease, "Increase indent", () => vm.ShiftSelectedIndent(1)),
            Separator()));
        host.Children.Add(Group(DrawButton(vm), CreateInsertButton(vm)));
        ThemeApplier.ApplyToolbarInputSurface(host);
    }

    private static StackPanel Group(params Control[] controls)
    {
        var group = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        foreach (var control in controls) group.Children.Add(control);
        return group;
    }

    private static Control Separator()
    {
        var separator = new Border
        {
            Width = 1,
            Margin = new Thickness(6, 4),
            VerticalAlignment = VerticalAlignment.Stretch,
        };
        CuiPropertyPresentation.Set(separator, "Background", BoardsTheme.BorderBrush,
            CuiPropertyLevel.Instance, "Boards toolbar group divider");
        return separator;
    }

    private static ToggleButton Toggle(
        BoardsViewModel vm, string glyph, string name, string tip, bool active, Func<Task> tapped,
        bool textFallback = false, double iconSize = 15)
    {
        var toggle = new ToggleButton
        {
            IsChecked = active,
            Padding = new Thickness(8, 6),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Margin = new Thickness(1, 0),
        };
        SetQuietToolbarControl(toggle, active, "Boards toolbar toggle");
        toggle.Content = textFallback
            ? (object)new TextBlock
            {
                Text = name,
                FontSize = 13,
                FontWeight = FontWeight.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
            }
            : BoardsIcons.Glyph(glyph, iconSize, name);
        ToolTip.SetTip(toggle, tip);
        Avalonia.Automation.AutomationProperties.SetName(toggle, name);
        toggle.Click += async (_, _) => await tapped();
        return toggle;
    }

    private static Control FontCombo(BoardsViewModel vm, RichBoardBlock? selected)
    {
        var combo = new ComboBox
        {
            Width = 132,
            IsEditable = true,
            FontSize = 12,
            FontWeight = FontWeight.SemiBold,
            Margin = new Thickness(4, 0),
        };
        ToolTip.SetTip(combo, "Font family");
        Avalonia.Automation.AutomationProperties.SetName(combo, "Font family");
        combo.ItemsSource = FontFamilies;
        var current = string.IsNullOrEmpty(selected?.FontFamily) ? "Default" : selected!.FontFamily;
        var targetBlock = vm.SelectedBlockId;
        combo.SelectedItem = FontFamilies.FirstOrDefault(f => string.Equals(f, current, StringComparison.Ordinal));
        combo.Text = current;
        void ApplyTypedFont()
        {
            if (!string.Equals(vm.SelectedBlockId, targetBlock, StringComparison.Ordinal)) return;
            var family = (combo.Text ?? string.Empty).Trim();
            if (family.Length > 100 || family.Any(char.IsControl))
            {
                combo.Text = string.IsNullOrWhiteSpace(selected?.FontFamily) ? "Default" : selected!.FontFamily;
                return;
            }
            vm.SetSelectedFontFamily(string.Equals(family, "Default", StringComparison.OrdinalIgnoreCase)
                ? string.Empty : family);
        }
        combo.SelectionChanged += (_, _) =>
        {
            if (combo.SelectedItem is string family && string.Equals(vm.SelectedBlockId, targetBlock, StringComparison.Ordinal))
                vm.SetSelectedFontFamily(family == "Default" ? string.Empty : family);
        };
        combo.KeyDown += (_, args) =>
        {
            if (args.Key != Avalonia.Input.Key.Enter) return;
            ApplyTypedFont();
            args.Handled = true;
        };
        combo.LostFocus += (_, _) => ApplyTypedFont();
        return combo;
    }

    private static Control SizeCombo(BoardsViewModel vm, RichBoardBlock? selected)
    {
        var combo = new ComboBox
        {
            Width = 76,
            IsEditable = true,
            FontSize = 12,
            FontWeight = FontWeight.SemiBold,
            Margin = new Thickness(0, 0, 4, 0),
        };
        ToolTip.SetTip(combo, "Font size");
        Avalonia.Automation.AutomationProperties.SetName(combo, "Font size");
        combo.ItemsSource = FontSizes.Select(s => s.ToString("0", CultureInfo.InvariantCulture)).ToArray();
        var current = selected?.FontSize > 0 ? selected!.FontSize : 14;
        var currentText = current.ToString("0.##", CultureInfo.InvariantCulture);
        var targetBlock = vm.SelectedBlockId;
        combo.SelectedItem = combo.ItemsSource.Cast<string>()
            .FirstOrDefault(value => double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var size)
                                     && Math.Abs(size - current) < 0.01);
        combo.Text = currentText;
        void ApplyTypedSize()
        {
            if (!string.Equals(vm.SelectedBlockId, targetBlock, StringComparison.Ordinal)) return;
            if (double.TryParse(combo.Text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var size) &&
                double.IsFinite(size) && size is >= 8 and <= 96)
            {
                vm.SetSelectedFontSize(size);
                return;
            }
            var active = vm.FindBlock(targetBlock)?.FontSize ?? current;
            combo.Text = (active > 0 ? active : 14).ToString("0.##", CultureInfo.InvariantCulture);
        }
        combo.SelectionChanged += (_, _) =>
        {
            if (combo.SelectedItem is string value && string.Equals(vm.SelectedBlockId, targetBlock, StringComparison.Ordinal) &&
                double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var size))
                vm.SetSelectedFontSize(size);
        };
        combo.KeyDown += (_, args) =>
        {
            if (args.Key != Avalonia.Input.Key.Enter) return;
            ApplyTypedSize();
            args.Handled = true;
        };
        combo.LostFocus += (_, _) => ApplyTypedSize();
        return combo;
    }

    private static Control ColorPopupButton(BoardsViewModel vm, string glyph, string tip, bool isHighlight)
    {
        var selectedBlock = vm.FindBlock(vm.SelectedBlockId);
        var previewColor = isHighlight ? selectedBlock?.Background : selectedBlock?.Foreground;
        var preview = new Border
        {
            Width = 16,
            Height = 3,
            HorizontalAlignment = HorizontalAlignment.Center,
            CornerRadius = new CornerRadius(1.5),
        };
        var previewBrush = !string.IsNullOrWhiteSpace(previewColor) && BoardsTheme.TryBrush(previewColor, out var parsed)
            ? parsed
            : isHighlight ? Brushes.Transparent : BoardsTheme.TextBrush;
        CuiPropertyPresentation.Set(preview, "Background", previewBrush, CuiPropertyLevel.Instance,
            "Boards selected text colour preview");
        var content = new StackPanel { Orientation = Orientation.Vertical, Spacing = 2 };
        content.Children.Add(BoardsIcons.Glyph(glyph, 18, tip));
        content.Children.Add(preview);
        var button = new Button
        {
            Content = content,
            Padding = new Thickness(8, 6),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Margin = new Thickness(1, 0),
        };
        SetQuietToolbarControl(button, selected: false, "Boards toolbar colour control");
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
            selected?.Alignment == "left", async () => await vm.DispatchAsync("AlignLeft", null), iconSize: 18));
        row.Children.Add(Toggle(vm, BoardsIcons.AlignCenter, "Align center", "Align center",
            selected?.Alignment == "center", async () => await vm.DispatchAsync("AlignCenter", null), iconSize: 18));
        row.Children.Add(Toggle(vm, BoardsIcons.AlignRight, "Align right", "Align right",
            selected?.Alignment == "right", async () => await vm.DispatchAsync("AlignRight", null), iconSize: 18));
        return row;
    }

    private static Control IndentButton(BoardsViewModel vm, string glyph, string tip, Action tapped)
    {
        var button = BoardsIcons.IconButton(glyph, tip, tip, 18);
        button.Margin = new Thickness(1, 0);
        SetQuietToolbarControl(button, selected: false, "Boards toolbar indent control");
        ToolTip.SetTip(button, tip);
        Avalonia.Automation.AutomationProperties.SetName(button, tip);
        button.Click += (_, _) => tapped();
        return button;
    }

    private static Control BuildStyleGallery(BoardsViewModel vm) => BuildStyleGalleryInternal(vm);

    private static Control BuildStyleGalleryInternal(BoardsViewModel vm)
    {
        var combo = new ComboBox { Width = 150, Margin = new Thickness(0, 0, 4, 0) };
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
                // The gallery is painted with the current UI card surface. A document
                // style's text colour may be black on a dark card, so use the semantic
                // control foreground here and leave document colour previews to the editor.
                Foreground = BoardsTheme.TextBrush,
            };
            if (!string.IsNullOrEmpty(style.FontFamily))
                preview.FontFamily = new FontFamily(style.FontFamily);
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
            Content = BoardsIcons.Glyph(BoardsIcons.Ink, 18, "Draw"),
            IsChecked = vm.IsDrawMode,
            Padding = new Thickness(8, 6),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
        };
        SetQuietToolbarControl(button, vm.IsDrawMode, "Boards toolbar drawing mode");
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

    public static Control CreateInsertButton(
        BoardsViewModel vm, string? tooltip = null, string? insertAfterBlockId = null, string? previewBlockId = null)
    {
        var button = BoardsIcons.IconButton(BoardsIcons.Add, "Insert", tooltip ?? "Insert a block (grouped menu with search)");
        SetQuietToolbarControl(button, selected: false, "Boards toolbar insert action");
        var popup = new Popup { PlacementTarget = button, Placement = PlacementMode.Bottom,
            IsLightDismissEnabled = true, ShouldUseOverlayLayer = true };
        // A manually owned Popup needs the trigger's logical theme ancestry.
        button.AttachedToVisualTree += (_, _) => ((ISetLogicalParent)popup).SetParent(button);
        ((ISetLogicalParent)popup).SetParent(button);
        CuiPopupPresentation.RequestTransparency(popup);
        var panel = new Grid { RowDefinitions = new RowDefinitions("Auto,*"), MinWidth = 300, MaxWidth = 420,
            Height = 384, MaxHeight = 384 };
        var search = new TextBox { PlaceholderText = "Search blocks and styles…", Height = 36, MinHeight = 36,
            Margin = new Thickness(0, 0, 0, 8) };
        CuiPropertyPresentation.Set(search, "Height", 36d, CuiPropertyLevel.Instance, "Boards insert search geometry");
        CuiPropertyPresentation.Set(search, "MinHeight", 36d, CuiPropertyLevel.Instance, "Boards insert search geometry");
        CuiPropertyPresentation.Set(search, "MaxHeight", 36d, CuiPropertyLevel.Instance, "Boards insert search geometry");
        var context = new InsertMenuContext(vm, popup, search)
            { AnchorBlockId = insertAfterBlockId, RowBlockId = previewBlockId ?? insertAfterBlockId };
        button.Tag = context;
        InsertMenus.Add(button, context);
        popup.Closed += (_, _) => context.Close();
        button.DetachedFromVisualTree += (_, _) => { context.Dispose(); ((ISetLogicalParent)popup).SetParent(null); };
        panel.Tag = button.Tag;
        Avalonia.Automation.AutomationProperties.SetName(search, "Search insert menu");
        var list = new StackPanel { Orientation = Orientation.Vertical, Tag = button.Tag };
        panel.Children.Add(search);
        var scroll = new ScrollViewer { Content = list,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Visible };
        Grid.SetRow(scroll, 1);
        panel.Children.Add(scroll);
        panel.AddHandler(InputElement.KeyDownEvent, (_, args) =>
        {
            if (args.Handled || !popup.IsOpen || args.KeyModifiers != KeyModifiers.None) return;
            if (args.Key == Key.Escape) { context.Close(); button.Focus(); args.Handled = true; return; }
            var choices = list.GetVisualDescendants().OfType<Control>()
                .Where(item => item is Button or MenuItem && item.IsEffectivelyEnabled &&
                    item.IsEffectivelyVisible && item.IsHitTestVisible && item.Focusable).ToArray();
            if (choices.Length == 0) return;
            var current = Array.FindIndex(choices, item => item.IsKeyboardFocusWithin);
            if (args.Key == Key.Enter && (current >= 0 || search.IsKeyboardFocusWithin))
            {
                var choice = choices[Math.Max(0, current)];
                choice.RaiseEvent(new RoutedEventArgs(choice is MenuItem ? MenuItem.ClickEvent : Button.ClickEvent));
                args.Handled = true; return;
            }
            var next = args.Key switch { Key.Down => Math.Min(current + 1, choices.Length - 1),
                Key.Up => current < 0 ? choices.Length - 1 : Math.Max(0, current - 1),
                Key.Home => 0, Key.End => choices.Length - 1, _ => -1 };
            if (next < 0) return;
            choices[next].Focus(); choices[next].BringIntoView(); args.Handled = true;
        }, RoutingStrategies.Tunnel);
        void Fill(string filter)
        {
            context.RetireDestination();
            list.Children.Clear();
            if (context.AnchorBlockId is not null && context.Owner is null) return;
            if (context.AnchorBlockId is not null && context.Placement is null)
            {
                list.Children.Add(context.CreateDestination());
                return;
            }
            if (context.AnchorBlockId is not null && context.Placement is not null)
            {
                list.Children.Add(DestinationHeader(context.Placement, () =>
                {
                    context.Placement = null;
                    Fill(search.Text ?? string.Empty);
                    search.Focus();
                }));
            }
            if (vm.DatabaseBlocks.IsAvailable)
            {
                AddGroup(list, "Database", [("New database", "database"), ("Tab View", "database-tabs"),
                    ("Timeline View", "database-timeline")], vm, popup, filter, context);
                if (vm.HasPendingDatabaseLink)
                    AddGroup(list, "Recovery", [("Link created database", "database-retry")], vm, popup, filter, context);
            }
            if (vm.CanAddTabber) AddGroup(list, "Containers", [("Tabber", "tabber")], vm, popup, filter, context);
            AddGroup(list, "Text", [
                ("Paragraph", "paragraph"), ("Title", "style:title"), ("Header", "style:heading-1"),
                ("Quote", "style:quote"), ("Code", "style:code"),
            ], vm, popup, filter, context);
            var customs = vm.StyleList.Where(s => !s.IsBuiltIn).ToList();
            if (customs.Count > 0)
                AddGroup(list, "Custom styles", customs.Select(s => (s.Name, "style:" + s.Id)).ToList(), vm, popup, filter, context);
            AddGroup(list, "Content", [
                ("Checklist", "checklist"), ("Bulleted list", "bulleted"), ("Numbered list", "numbered"),
                ("Table", "table"), ("Graph", "graph"), ("Image…", "image"), ("Attachment…", "attachment"),
            ], vm, popup, filter, context);
            AddGroup(list, "Other", [("Freeform box", "freeform"), ("Divider", "divider")], vm, popup, filter, context);
        }
        context.Refill = () => Fill(search.Text ?? string.Empty);
        search.TextChanged += (_, _) => Fill(search.Text ?? string.Empty);
        Fill(string.Empty);
        popup.Child = context.CreateSurface(panel);
        button.Click += (_, _) =>
        {
            OpenInsertMenu(button);
        };
        return button;
    }

    public static bool OpenInsertMenu(Control button)
    {
        ArgumentNullException.ThrowIfNull(button);
        return InsertMenuFor(button)?.Open() == true;
    }

    public static bool HasInsertMenu(Control button) => InsertMenuFor(button) is not null;

    private static void AddGroup(
        StackPanel list, string heading, IReadOnlyList<(string Label, string Tag)> entries,
        BoardsViewModel vm, Popup popup, string filter, InsertMenuContext context)
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
            SetQuietToolbarControl(item, selected: false, "Boards insertion menu item");
            CuiPropertyPresentation.Attach(item);
            Avalonia.Automation.AutomationProperties.SetName(item, "Insert " + label);
            if (context.AnchorBlockId is not null &&
                vm.RelativeInsertionUnavailableReason(tag, context.AnchorBlockId, context.Placement) is { } unavailable)
            {
                item.IsEnabled = false;
                ToolTip.SetTip(item, unavailable);
                Avalonia.Automation.AutomationProperties.SetHelpText(item, unavailable);
            }
            var opening = context.Opening;
            var contentVersion = context.ContentVersion;
            var owner = context.Owner;
            var placement = context.Placement;
            item.Click += (_, _) =>
            {
                context.Commit(tag, opening, contentVersion, owner, placement);
            };
            list.Children.Add(item);
        }
    }


    private static Control DestinationHeader(string placement, Action changeBack)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 0, 0, 6) };
        row.Children.Add(new TextBlock
        {
            Text = placement switch
            {
                "above" => "ABOVE THIS BLOCK",
                "below" => "BELOW THIS BLOCK",
                _ => "REPLACE THIS BLOCK",
            },
            FontSize = 11,
            FontWeight = FontWeight.SemiBold,
            Foreground = BoardsTheme.AccentBrush,
            VerticalAlignment = VerticalAlignment.Center,
        });
        var back = new Button
        {
            Content = "Change",
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(6, 2),
            FontSize = 11,
        };
        SetQuietToolbarControl(back, selected: false, "Boards insert destination change");
        CuiPropertyPresentation.Attach(back);
        Avalonia.Automation.AutomationProperties.SetName(back, "Change the insert destination");
        back.Click += (_, _) => changeBack();
        row.Children.Add(back);
        return row;
    }

    private static void BuildDrawToolbar(Panel bar, BoardsViewModel vm)
    {
        bar.Children.Add(Group(DrawButton(vm), Separator()));
        var tools = Group();
        // Keep the drawing silhouettes optically consistent with the writing toolbar.
        // Tooltips describe each action; the control exposes its stable tool name.
        foreach (var tool in new[]
        {
            (Value: "Pen", Icon: BoardsIcons.Ink, Tip: "Pen — draw freehand ink"),
            (Value: "Highlighter", Icon: BoardsIcons.Highlighter, Tip: "Highlighter — translucent wide ink"),
            (Value: "Eraser", Icon: BoardsIcons.Eraser, Tip: "Eraser — remove ink it touches"),
            (Value: "Select", Icon: BoardsIcons.SelectTool, Tip: "Select — pick a stroke to move or restyle it"),
        })
        {
            var button = new ToggleButton
            {
                Content = BoardsIcons.Glyph(tool.Icon, 18, tool.Value),
                MinWidth = 32, MinHeight = 32,
                IsChecked = string.Equals(vm.InkTool, tool.Value, StringComparison.OrdinalIgnoreCase),
                Padding = new Thickness(8, 6),
                Margin = new Thickness(1, 0),
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
            };
            SetQuietToolbarControl(button, button.IsChecked == true, "Boards drawing tool selection");
            ToolTip.SetTip(button, tool.Tip);
            Avalonia.Automation.AutomationProperties.SetName(button, tool.Value + " drawing tool");
            button.Click += (_, _) => vm.SetInkTool(tool.Value);
            tools.Children.Add(button);
        }
        tools.Children.Add(Separator());
        bar.Children.Add(tools);
        var colors = Group();
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
            CuiPropertyPresentation.Set(swatch, "Background", BoardsTheme.Brush(hex), CuiPropertyLevel.Instance,
                "Boards ink colour well");
            CuiPropertyPresentation.Set(swatch, "BorderWidth", new Thickness(
                string.Equals(vm.InkColor, hex, StringComparison.OrdinalIgnoreCase) ? 2 : 0), CuiPropertyLevel.Instance,
                "Boards ink colour well");
            CuiPropertyPresentation.Set(swatch, "BorderBrush", BoardsTheme.TextBrush, CuiPropertyLevel.Instance,
                "Boards ink colour well");
            ToolTip.SetTip(swatch, name + " ink");
            Avalonia.Automation.AutomationProperties.SetName(swatch, name + " ink");
            swatch.Click += (_, _) => vm.SetInkColor(hex);
            colors.Children.Add(swatch);
        }
        colors.Children.Add(Separator());
        bar.Children.Add(colors);
        var widths = Group();
        foreach (var width in new[] { 2d, 4d, 8d, 12d })
        {
            var stroke = new ToggleButton
            {
                Content = width.ToString("0") + " px", IsChecked = Math.Abs(vm.InkWidth - width) < 0.01,
                Padding = new Thickness(7, 6), Margin = new Thickness(1, 0),
                Background = Brushes.Transparent, BorderThickness = new Thickness(0),
            };
            SetQuietToolbarControl(stroke, stroke.IsChecked == true, "Boards stroke width selection");
            ToolTip.SetTip(stroke, "Stroke width " + width.ToString("0"));
            Avalonia.Automation.AutomationProperties.SetName(stroke, "Stroke width " + width.ToString("0"));
            stroke.Click += (_, _) => vm.SetInkWidth(width);
            widths.Children.Add(stroke);
        }
        widths.Children.Add(Separator());
        bar.Children.Add(widths);
        var undo = BoardsIcons.IconButton(BoardsIcons.Undo, "Undo drawing", "Undo the last drawing action");
        SetQuietToolbarControl(undo, selected: false, "Boards drawing toolbar action");
        undo.Click += async (_, _) => await vm.DispatchAsync("Undo", null);
        var redo = BoardsIcons.IconButton(BoardsIcons.Redo, "Redo drawing", "Redo the last drawing action");
        SetQuietToolbarControl(redo, selected: false, "Boards drawing toolbar action");
        redo.Click += async (_, _) => await vm.DispatchAsync("Redo", null);
        var viewActions = Group(undo, redo);
        var zoomOut = new Button { Content = "Zoom −", Padding = new Thickness(7, 6), Margin = new Thickness(2, 0) };
        ToolTip.SetTip(zoomOut, "Zoom drawing out");
        Avalonia.Automation.AutomationProperties.SetName(zoomOut, "Zoom drawing out");
        zoomOut.Click += async (_, _) => await vm.SetInkZoomAsync(vm.InkZoom / 1.25);
        var zoomIn = new Button { Content = "Zoom +", Padding = new Thickness(7, 6), Margin = new Thickness(2, 0) };
        ToolTip.SetTip(zoomIn, "Zoom drawing in");
        Avalonia.Automation.AutomationProperties.SetName(zoomIn, "Zoom drawing in");
        zoomIn.Click += async (_, _) => await vm.SetInkZoomAsync(vm.InkZoom * 1.25);
        var reset = new Button { Content = "Reset view", Padding = new Thickness(7, 6), Margin = new Thickness(2, 0) };
        SetQuietToolbarControl(zoomOut, selected: false, "Boards drawing viewport action");
        SetQuietToolbarControl(zoomIn, selected: false, "Boards drawing viewport action");
        SetQuietToolbarControl(reset, selected: false, "Boards drawing viewport action");
        ToolTip.SetTip(reset, "Reset drawing view");
        Avalonia.Automation.AutomationProperties.SetName(reset, "Reset drawing view");
        reset.Click += async (_, _) => await vm.ResetInkViewAsync();
        viewActions.Children.Add(zoomOut);
        viewActions.Children.Add(zoomIn);
        viewActions.Children.Add(reset);
        bar.Children.Add(viewActions);
    }

    private static void SetQuietToolbarControl(Control control, bool selected, string source)
    {
        CuiPropertyPresentation.Set(control, "Foreground", BoardsTheme.TextBrush,
            CuiPropertyLevel.Instance, source + " text");
        CuiPropertyPresentation.Set(control, "Background", selected ? BoardsTheme.CardBrush : Brushes.Transparent,
            CuiPropertyLevel.Instance, source + " surface");
        CuiPropertyPresentation.Set(control, "BorderWidth", new Thickness(0),
            CuiPropertyLevel.Instance, source + " border");
        CuiPropertyPresentation.Set(control, "CornerRadius", new CornerRadius(8),
            CuiPropertyLevel.Instance, source + " shape");
    }
}

