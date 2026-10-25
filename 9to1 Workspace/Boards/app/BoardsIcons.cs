// Boards icons resolve through Haven's maintained vector catalog. No icon
// depends on a platform font containing a private-use glyph.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace CakeOS.Apps.Boards.App;

public static class BoardsIcons
{
    public const string Save = "save";
    public const string Undo = "undo";
    public const string Redo = "redo";
    public const string Add = "plus";
    public const string Delete = "delete";
    public const string Close = "close";
    public const string Check = "check";
    public const string Search = "search";
    public const string More = "more";
    public const string Grip = "more";
    public const string ChevronDown = "chevron-down";
    public const string ChevronRight = "chevron-right";
    public const string ChevronLeft = "chevron-left";
    public const string Bold = "bold";
    public const string Italic = "italic";
    public const string Underline = "underline";
    public const string Strikethrough = "text";
    public const string BulletedList = "tasks";
    public const string NumberedList = "tasks";
    public const string Edit = "properties";
    public const string Image = "file";
    public const string Attach = "copy";
    public const string Settings = "settings";
    public const string Page = "file";
    public const string Section = "boards";
    public const string Table = "data";
    public const string Chart = "data";
    public const string Ink = "pen";
    public const string AlignLeft = "align-text";
    public const string AlignCenter = "align-text";
    public const string AlignRight = "align-text";
    public const string Subscript = "text";
    public const string Superscript = "text";
    public const string Theme = "palette";
    public const string File = "file";
    public const string Info = "properties";

    public static Control Glyph(string glyph, double size = 14, string? accessibleName = null)
    {
        var icon = new Image
        {
            Source = BoardsCuiIconResolver.CreateIcon(glyph, BoardsTheme.TextBrush),
            Width = size,
            Height = size,
            Stretch = Avalonia.Media.Stretch.Uniform,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
        };
        if (!string.IsNullOrEmpty(accessibleName))
            Avalonia.Automation.AutomationProperties.SetName(icon, accessibleName);
        return icon;
    }

    public static Button IconButton(string glyph, string name, string tooltip, double size = 15)
    {
        var button = new Button
        {
            Content = Glyph(glyph, size, name),
            Padding = new Avalonia.Thickness(8, 6),
            Background = Brushes.Transparent,
            BorderThickness = new Avalonia.Thickness(0),
        };
        ToolTip.SetTip(button, tooltip);
        Avalonia.Automation.AutomationProperties.SetName(button, name);
        return button;
    }
}
