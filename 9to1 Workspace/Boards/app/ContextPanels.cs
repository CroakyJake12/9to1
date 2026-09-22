// ContextPanels builds the contextual strip shown beneath the toolbar for the
// current selection: table, image, divider and freeform controls. Table cells
// and block fields edit the view model directly (merge persists them); the
// strip itself rebuilds on selection change, never per keystroke.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace CakeOS.Apps.Boards.App;

public static class ContextPanels
{
    private static readonly (string Name, string Hex)[] Surfaces =
    [
        ("None", ""), ("White", "#FFFFFFFF"), ("Mist", "#FFF1F3F4"),
        ("Peach", "#FFFFF3C4"), ("Mint", "#FFE6F4EA"), ("Sky", "#FFE8F0FE"),
        ("Lavender", "#FFF3E8FD"), ("Rose", "#FFFCE8E6"), ("Slate", "#FFE8EDF3"),
    ];

    public static void Rebuild(StackPanel host, BoardsViewModel vm)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(vm);
        host.Children.Clear();

        var selected = vm.FindBlock(vm.SelectedBlockId);
        Control? panel = selected?.Kind switch
        {
            "table" => BuildTablePanel(vm, selected),
            "image" => BuildImagePanel(vm, selected),
            "divider" => BuildDividerPanel(vm, selected),
            _ => null,
        };
        if (panel is null)
            return;
        var wrap = new Border
        {
            Background = BoardsTheme.SurfaceBrush,
            BorderBrush = BoardsTheme.BorderBrush,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(12, 6),
            Child = panel,
        };
        host.Children.Add(wrap);
    }

    // ----- Table -----

    private static Control BuildTablePanel(BoardsViewModel vm, RichBoardBlock block)
    {
        var root = new WrapPanel { Orientation = Orientation.Horizontal };
        root.Children.Add(GroupLabel("Table"));
        SmallButton(root, "+ Row", "Add table row", () => vm.TableAddRow(block.Id));
        SmallButton(root, "− Row", "Delete last table row", () => vm.TableDelRow(block.Id));
        SmallButton(root, "+ Col", "Add table column", () => vm.TableAddCol(block.Id));
        SmallButton(root, "− Col", "Delete last table column", () => vm.TableDelCol(block.Id));
        root.Children.Add(GroupLabel("Cell"));
        var cell = vm.SelectedCell;
        var cellKey = cell is { } c && c.BlockId == block.Id ? $"{c.Row},{c.Col}" : null;
        var align = new ComboBox { Width = 96, Margin = new Thickness(2, 0) };
        ToolTip.SetTip(align, "Selected cell horizontal alignment");
        Avalonia.Automation.AutomationProperties.SetName(align, "Cell horizontal alignment");
        align.ItemsSource = new List<string> { "inherit", "left", "center", "right" };
        align.SelectedItem = cellKey is not null && block.CellStyles.TryGetValue(cellKey, out var cs)
            ? cs.AlignH : "inherit";
        align.SelectionChanged += (_, _) =>
        {
            if (cellKey is null || align.SelectedItem is not string value)
                return;
            var parts = cellKey.Split(',');
            vm.SetCellAlign(block.Id, int.Parse(parts[0]), int.Parse(parts[1]), value);
            vm.RequestRebuild();
        };
        root.Children.Add(align);
        var valign = new ComboBox { Width = 96, Margin = new Thickness(2, 0) };
        ToolTip.SetTip(valign, "Selected cell vertical alignment");
        Avalonia.Automation.AutomationProperties.SetName(valign, "Cell vertical alignment");
        valign.ItemsSource = new List<string> { "inherit", "top", "middle", "bottom" };
        valign.SelectedItem = cellKey is not null && block.CellStyles.TryGetValue(cellKey, out var vs)
            ? vs.AlignV : "inherit";
        valign.SelectionChanged += (_, _) =>
        {
            if (cellKey is null || valign.SelectedItem is not string value)
                return;
            SetCellVertical(vm, block, cellKey, value);
        };
        root.Children.Add(valign);
        SmallButton(root, "B", "Toggle selected cell bold", () =>
        {
            if (cellKey is null)
                return;
            var parts = cellKey.Split(',');
            vm.ToggleCellBold(block.Id, int.Parse(parts[0]), int.Parse(parts[1]));
            vm.RequestRebuild();
        });
        root.Children.Add(GroupLabel("Style"));
        var bg = new ComboBox { Width = 110, Margin = new Thickness(2, 0) };
        ToolTip.SetTip(bg, "Table background");
        Avalonia.Automation.AutomationProperties.SetName(bg, "Table background");
        bg.ItemsSource = Surfaces.Select(s => s.Name).ToList();
        bg.SelectedItem = Surfaces.FirstOrDefault(s => s.Hex == block.TableStyle.Background).Name ?? "None";
        bg.SelectionChanged += (_, _) =>
        {
            if (bg.SelectedItem is string name)
            {
                block.TableStyle.Background = Surfaces.First(s => s.Name == name).Hex;
                vm.CommitViewEdit();
            }
        };
        root.Children.Add(bg);
        var border = new ComboBox { Width = 96, Margin = new Thickness(2, 0) };
        ToolTip.SetTip(border, "Table border style");
        Avalonia.Automation.AutomationProperties.SetName(border, "Table border style");
        border.ItemsSource = new List<string> { "inherit", "none", "solid", "dashed", "dotted" };
        border.SelectedItem = block.TableStyle.BorderStyle;
        border.SelectionChanged += (_, _) =>
        {
            if (border.SelectedItem is string value)
            {
                block.TableStyle.BorderStyle = value;
                vm.CommitViewEdit();
            }
        };
        SmallButton(root, "Alt rows", "Toggle alternating row style", () =>
        {
            block.TableStyle.AlternatingRows = !block.TableStyle.AlternatingRows;
            vm.CommitViewEdit();
        });
        SmallButton(root, "R+ ", "Rounder table corners", () =>
        {
            block.TableStyle.CornerRadius = Math.Min(32, block.TableStyle.CornerRadius + 2);
            vm.CommitViewEdit();
        });
        var hint = new TextBlock
        {
            Text = cellKey is null ? "Focus a cell for cell tools." : $"Cell {cellKey}",
            FontSize = 12,
            Foreground = BoardsTheme.SecondaryTextBrush,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 0, 0),
        };
        root.Children.Add(hint);
        return root;
    }

    private static void SetCellVertical(BoardsViewModel vm, RichBoardBlock block, string cellKey, string value)
    {
        var style = block.CellStyles.TryGetValue(cellKey, out var existing)
            ? existing
            : block.CellStyles[cellKey] = new RichCellStyleView();
        style.AlignV = value;
        vm.CommitViewEdit();
    }

    // ----- Image -----

    private static Control BuildImagePanel(BoardsViewModel vm, RichBoardBlock block)
    {
        var root = new WrapPanel { Orientation = Orientation.Horizontal };
        root.Children.Add(GroupLabel("Image"));
        SmallButton(root, "W−", "Narrower image", () => StepImageWidth(vm, block, -40));
        SmallButton(root, "W+", "Wider image", () => StepImageWidth(vm, block, 40));
        SmallButton(root, "Natural", "Natural image size", () =>
        {
            if (block.Image is null)
                return;
            block.Image.Width = 0;
            vm.CommitViewEdit();
        });
        var align = new ComboBox { Width = 100, Margin = new Thickness(2, 0) };
        ToolTip.SetTip(align, "Image alignment");
        Avalonia.Automation.AutomationProperties.SetName(align, "Image alignment");
        align.ItemsSource = new List<string> { "inherit", "left", "center", "right" };
        align.SelectedItem = string.IsNullOrEmpty(block.Image?.Alignment) ? "inherit" : block.Image!.Alignment;
        align.SelectionChanged += (_, _) =>
        {
            if (align.SelectedItem is string value)
                vm.SetImageAlignment(block.Id, value);
        };
        root.Children.Add(align);
        SmallButton(root, "Replace…", "Replace image from a file", async () => await vm.ReplaceImageAsync(block.Id));
        SmallButton(root, "Remove", "Remove this image", async () => await vm.DeleteBlockAsync(block.Id));
        return root;
    }

    private static void StepImageWidth(BoardsViewModel vm, RichBoardBlock block, double delta)
    {
        if (block.Image is null)
            return;
        var current = block.Image.Width > 0 ? block.Image.Width : 320;
        block.Image.Width = Math.Clamp(current + delta, 40, 1600);
        vm.CommitViewEdit();
    }

    // ----- Divider -----

    private static Control BuildDividerPanel(BoardsViewModel vm, RichBoardBlock block)
    {
        var root = new WrapPanel { Orientation = Orientation.Horizontal };
        root.Children.Add(GroupLabel("Divider"));
        SmallButton(root, "−", "Thinner line", () => StepDivider(vm, block, -1));
        SmallButton(root, "+", "Thicker line", () => StepDivider(vm, block, 1));
        var style = new ComboBox { Width = 100, Margin = new Thickness(2, 0) };
        ToolTip.SetTip(style, "Divider line style");
        Avalonia.Automation.AutomationProperties.SetName(style, "Divider line style");
        style.ItemsSource = new List<string> { "solid", "dashed", "dotted", "none" };
        style.SelectedItem = block.Divider?.LineStyle ?? "solid";
        style.SelectionChanged += (_, _) =>
        {
            if (style.SelectedItem is string value)
                vm.SetDividerStyle(block.Id, value);
        };
        root.Children.Add(style);
        foreach (var (name, hex) in Surfaces.Where(s => s.Hex.Length > 0).Take(6))
        {
            var swatch = new Button
            {
                Width = 26,
                Height = 26,
                Margin = new Thickness(2, 0),
                Padding = new Thickness(0),
                Background = BoardsTheme.Brush(hex),
                BorderBrush = BoardsTheme.BorderBrush,
                BorderThickness = new Thickness(1),
            };
            ToolTip.SetTip(swatch, "Divider color " + name);
            Avalonia.Automation.AutomationProperties.SetName(swatch, "Divider color " + name);
            swatch.Click += (_, _) => SetDividerColor(vm, block, hex);
            root.Children.Add(swatch);
        }
        return root;
    }

    private static void StepDivider(BoardsViewModel vm, RichBoardBlock block, double delta)
    {
        if (block.Divider is null)
            return;
        block.Divider.Thickness = Math.Clamp(block.Divider.Thickness + delta, 1, 12);
        vm.CommitViewEdit();
    }

    private static void SetDividerColor(BoardsViewModel vm, RichBoardBlock block, string hex)
    {
        if (block.Divider is null)
            return;
        block.Divider.Color = hex;
        vm.CommitViewEdit();
    }

    // ----- Shared -----

    private static TextBlock GroupLabel(string text) => new()
    {
        Text = text,
        FontSize = 12,
        FontWeight = FontWeight.SemiBold,
        Foreground = BoardsTheme.SecondaryTextBrush,
        VerticalAlignment = VerticalAlignment.Center,
        Margin = new Thickness(8, 0, 4, 0),
    };

    private static void SmallButton(Panel parent, string content, string tip, Action tapped)
    {
        var button = new Button
        {
            Content = content,
            Padding = new Thickness(8, 4),
            Margin = new Thickness(2, 0),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
        };
        ToolTip.SetTip(button, tip);
        Avalonia.Automation.AutomationProperties.SetName(button, tip);
        button.Click += (_, _) => tapped();
        parent.Children.Add(button);
    }
}


