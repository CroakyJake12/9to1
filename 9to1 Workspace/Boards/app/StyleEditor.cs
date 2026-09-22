// StyleEditor: code-built dialog Window for style create/edit/duplicate/delete.
// No AXAML: all controls are constructed here and wired to BoardsViewModel
// style CRUD, which persists through ContractSessionAdapter into the contract.

using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using CakeOS.Apps.Boards.Contract;

namespace CakeOS.Apps.Boards.App;

/// <summary>Editable style DTO exchanged between the dialog and the ViewModel.</summary>
public sealed class StyleEditModel
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = "Custom style";
    public bool IsBuiltIn { get; set; }
    public string BlockKind { get; set; } = "paragraph";
    public bool Bold { get; set; }
    public bool Italic { get; set; }
    public bool Underline { get; set; }
    public bool Strike { get; set; }
    public string Baseline { get; set; } = "normal";
    public string FontFamily { get; set; } = string.Empty;
    public double FontSize { get; set; }
    public string Foreground { get; set; } = string.Empty;
    public string Background { get; set; } = string.Empty;
    public string Alignment { get; set; } = "inherit";
    public double LineSpacing { get; set; }
    public double SpaceBefore { get; set; }
    public double SpaceAfter { get; set; }
    public int IndentLevel { get; set; } = -1;

    public static StyleEditModel FromContract(HavenRichStyle style) => new()
    {
        Id = style.Id,
        Name = style.Name,
        IsBuiltIn = style.IsBuiltIn,
        BlockKind = style.BlockKind.ToString().ToLowerInvariant(),
        Bold = style.Bold,
        Italic = style.Italic,
        Underline = style.Underline,
        Strike = style.StrikeThrough,
        Baseline = style.Baseline.ToString().ToLowerInvariant(),
        FontFamily = style.FontFamily,
        FontSize = style.FontSize,
        Foreground = style.Foreground,
        Background = style.Background,
        Alignment = style.Alignment.ToString().ToLowerInvariant(),
        LineSpacing = style.LineSpacing,
        SpaceBefore = style.SpaceBefore,
        SpaceAfter = style.SpaceAfter,
        IndentLevel = style.IndentLevel,
    };

    public void ApplyTo(HavenRichStyle style)
    {
        style.Name = string.IsNullOrWhiteSpace(Name) ? style.Name : Name.Trim();
        style.Bold = Bold;
        style.Italic = Italic;
        style.Underline = Underline;
        style.StrikeThrough = Strike;
        style.Baseline = Baseline?.ToLowerInvariant() switch
        {
            "subscript" => HavenRichBaseline.Subscript,
            "superscript" => HavenRichBaseline.Superscript,
            _ => HavenRichBaseline.Normal,
        };
        style.FontFamily = FontFamily?.Trim() ?? string.Empty;
        style.FontSize = FontSize;
        style.Foreground = Foreground?.Trim() ?? string.Empty;
        style.Background = Background?.Trim() ?? string.Empty;
        style.Alignment = Alignment?.ToLowerInvariant() switch
        {
            "left" => HavenRichAlignment.Left,
            "center" => HavenRichAlignment.Center,
            "right" => HavenRichAlignment.Right,
            "justify" => HavenRichAlignment.Justify,
            _ => HavenRichAlignment.Inherit,
        };
        style.LineSpacing = LineSpacing;
        style.SpaceBefore = SpaceBefore;
        style.SpaceAfter = SpaceAfter;
        style.IndentLevel = IndentLevel;
    }
}

public static class StyleEditor
{
    public static async Task ShowAsync(Window owner, BoardsViewModel vm, string? styleId = null)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(vm);

        var styles = vm.StyleList;
        var selectedBlock = styleId is null ? vm.FindBlock(vm.SelectedBlockId) : null;
        var initial = styles.FirstOrDefault(s => s.Id == styleId)
            ?? styles.FirstOrDefault(s => s.Id == selectedBlock?.StyleId)
            ?? styles.FirstOrDefault();
        if (initial is null)
            return;

        // The projection only carries Id/Name/Kind/IsBuiltIn, so the dialog
        // starts from catalog identity; run/para fields are written on Save.
        var model = new StyleEditModel { Id = initial.Id, Name = initial.Name, IsBuiltIn = initial.IsBuiltIn };

        var dialog = new Window
        {
            Title = "Manage styles",
            Width = 420,
            Height = 620,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
        };

        var root = new StackPanel { Orientation = Orientation.Vertical, Margin = new Thickness(16) };
        root.Children.Add(new TextBlock
        {
            Text = "Style",
            FontSize = 16,
            FontWeight = FontWeight.Bold,
            Margin = new Thickness(0, 0, 0, 8),
        });

        var picker = new ComboBox { Margin = new Thickness(0, 0, 0, 8) };
        AutomationProperties.SetName(picker, "Style to edit");
        var nameBox = new TextBox { Margin = new Thickness(0, 0, 0, 8) };
        AutomationProperties.SetName(nameBox, "Style name");
        ToolTip.SetTip(nameBox, "Style name (1–128 characters)");
        root.Children.Add(picker);
        root.Children.Add(nameBox);

        var flags = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        var boldBox = NewFlag(flags, "Bold");
        var italicBox = NewFlag(flags, "Italic");
        var underlineBox = NewFlag(flags, "Underline");
        var strikeBox = NewFlag(flags, "Strike");
        root.Children.Add(flags);

        var baselineBox = NewLabeledCombo(root, "Baseline", ["normal", "subscript", "superscript"], model.Baseline);
        var fontFamilyBox = NewLabeledText(root, "Font family", model.FontFamily);
        var fontSizeBox = NewLabeledText(root, "Font size", model.FontSize.ToString("0.##"));
        var foregroundBox = NewLabeledText(root, "Foreground (hex)", model.Foreground);
        var backgroundBox = NewLabeledText(root, "Background (hex)", model.Background);
        var alignmentBox = NewLabeledCombo(root, "Alignment", ["inherit", "left", "center", "right", "justify"], model.Alignment);
        var lineSpacingBox = NewLabeledText(root, "Line spacing", model.LineSpacing.ToString("0.##"));
        var spaceBeforeBox = NewLabeledText(root, "Space before", model.SpaceBefore.ToString("0.##"));
        var spaceAfterBox = NewLabeledText(root, "Space after", model.SpaceAfter.ToString("0.##"));
        var indentBox = NewLabeledText(root, "Indent level", model.IndentLevel.ToString());

        var status = new TextBlock { FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 8) };
        root.Children.Add(status);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        var create = new Button { Content = "New", Margin = new Thickness(0, 0, 8, 0) };
        var save = new Button { Content = "Save", Margin = new Thickness(0, 0, 8, 0) };
        var duplicate = new Button { Content = "Duplicate", Margin = new Thickness(0, 0, 8, 0) };
        var delete = new Button { Content = "Delete", Margin = new Thickness(0, 0, 8, 0) };
        var close = new Button { Content = "Close" };
        AutomationProperties.SetName(create, "New style");
        AutomationProperties.SetName(save, "Save style");
        AutomationProperties.SetName(duplicate, "Duplicate style");
        AutomationProperties.SetName(delete, "Delete style");
        AutomationProperties.SetName(close, "Close style editor");
        buttons.Children.Add(create);
        buttons.Children.Add(save);
        buttons.Children.Add(duplicate);
        buttons.Children.Add(delete);
        buttons.Children.Add(close);
        root.Children.Add(buttons);

        var scroll = new ScrollViewer { Content = root };
        dialog.Content = scroll;

        void LoadModel(StyleEditModel next)
        {
            model = next;
            nameBox.Text = next.Name;
            boldBox.IsChecked = next.Bold;
            italicBox.IsChecked = next.Italic;
            underlineBox.IsChecked = next.Underline;
            strikeBox.IsChecked = next.Strike;
            baselineBox.SelectedItem = next.Baseline;
            fontFamilyBox.Text = next.FontFamily;
            fontSizeBox.Text = next.FontSize.ToString("0.##");
            foregroundBox.Text = next.Foreground;
            backgroundBox.Text = next.Background;
            alignmentBox.SelectedItem = next.Alignment;
            lineSpacingBox.Text = next.LineSpacing.ToString("0.##");
            spaceBeforeBox.Text = next.SpaceBefore.ToString("0.##");
            spaceAfterBox.Text = next.SpaceAfter.ToString("0.##");
            indentBox.Text = next.IndentLevel.ToString();
            delete.IsEnabled = !next.IsBuiltIn && !string.IsNullOrEmpty(next.Id);
        }

        void ReadFields()
        {
            model.Name = nameBox.Text?.Trim() ?? string.Empty;
            model.Bold = boldBox.IsChecked == true;
            model.Italic = italicBox.IsChecked == true;
            model.Underline = underlineBox.IsChecked == true;
            model.Strike = strikeBox.IsChecked == true;
            model.Baseline = baselineBox.SelectedItem?.ToString() ?? "normal";
            model.FontFamily = fontFamilyBox.Text?.Trim() ?? string.Empty;
            if (double.TryParse(fontSizeBox.Text, out var size))
                model.FontSize = Math.Clamp(size, 0, 256);
            model.Foreground = foregroundBox.Text?.Trim() ?? string.Empty;
            model.Background = backgroundBox.Text?.Trim() ?? string.Empty;
            model.Alignment = alignmentBox.SelectedItem?.ToString() ?? "inherit";
            if (double.TryParse(lineSpacingBox.Text, out var spacing))
                model.LineSpacing = spacing;
            if (double.TryParse(spaceBeforeBox.Text, out var before))
                model.SpaceBefore = before;
            if (double.TryParse(spaceAfterBox.Text, out var after))
                model.SpaceAfter = after;
            if (int.TryParse(indentBox.Text, out var indent))
                model.IndentLevel = Math.Clamp(indent, -1, 8);
        }

        void RefreshPicker()
        {
            picker.ItemsSource = vm.StyleList
                .Select(s => new ComboBoxItem { Content = s.Name + (s.IsBuiltIn ? "" : " (custom)"), Tag = s.Id })
                .ToList();
            var current = picker.ItemsSource.Cast<ComboBoxItem>().FirstOrDefault(i => (string?)i.Tag == model.Id);
            picker.SelectedItem = current;
        }

        RefreshPicker();
        LoadModel(model);

        picker.SelectionChanged += (_, _) =>
        {
            if (picker.SelectedItem is ComboBoxItem selected && selected.Tag is string id)
            {
                var entry = vm.StyleList.FirstOrDefault(s => s.Id == id);
                if (entry is not null)
                    LoadModel(new StyleEditModel { Id = entry.Id, Name = entry.Name, IsBuiltIn = entry.IsBuiltIn });
            }
        };

        create.Click += (_, _) =>
        {
            LoadModel(new StyleEditModel { Id = string.Empty, Name = "Custom style" });
            status.Text = "New style — Save to create.";
        };
        save.Click += async (_, _) =>
        {
            ReadFields();
            if (string.IsNullOrWhiteSpace(model.Name))
            {
                status.Text = "Style names must contain 1 to 128 characters.";
                return;
            }
            try
            {
                await vm.ApplyStyleEditAsync(model);
                status.Text = string.IsNullOrEmpty(model.Id) ? "Style created." : "Style saved.";
                RefreshPicker();
            }
            catch (Exception error)
            {
                status.Text = "Save failed: " + error.Message.Split('\n')[0];
            }
        };
        duplicate.Click += async (_, _) =>
        {
            if (string.IsNullOrEmpty(model.Id))
            {
                status.Text = "Save the style before duplicating it.";
                return;
            }
            var copy = await vm.StyleDuplicateAsync(model.Id);
            status.Text = copy is null ? "Duplicate needs the contract session." : "Duplicated.";
            RefreshPicker();
        };
        delete.Click += async (_, _) =>
        {
            if (string.IsNullOrEmpty(model.Id))
            {
                status.Text = "Nothing to delete.";
                return;
            }
            await vm.StyleDeleteAsync(model.Id);
            status.Text = vm.Get("StatusText")?.ToString() ?? "Deleted.";
            model = new StyleEditModel();
            RefreshPicker();
        };
        close.Click += (_, _) => dialog.Close();

        await dialog.ShowDialog(owner);
    }

    private static CheckBox NewFlag(Panel parent, string content)
    {
        var box = new CheckBox { Content = content, Margin = new Thickness(0, 0, 12, 0) };
        AutomationProperties.SetName(box, "Style " + content);
        parent.Children.Add(box);
        return box;
    }

    private static TextBox NewLabeledText(Panel parent, string label, string value)
    {
        parent.Children.Add(new TextBlock { Text = label, FontSize = 12, Margin = new Thickness(0, 4, 0, 2) });
        var box = new TextBox { Text = value, Margin = new Thickness(0, 0, 0, 4) };
        AutomationProperties.SetName(box, label);
        parent.Children.Add(box);
        return box;
    }

    private static ComboBox NewLabeledCombo(Panel parent, string label, string[] items, string selected)
    {
        parent.Children.Add(new TextBlock { Text = label, FontSize = 12, Margin = new Thickness(0, 4, 0, 2) });
        var box = new ComboBox { ItemsSource = items.ToList(), SelectedItem = selected, Margin = new Thickness(0, 0, 0, 4) };
        AutomationProperties.SetName(box, label);
        parent.Children.Add(box);
        return box;
    }
}
