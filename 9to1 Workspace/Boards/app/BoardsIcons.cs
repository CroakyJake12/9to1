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
    public const string Grip = "grip";
    public const string ChevronDown = "chevron-down";
    public const string ChevronRight = "chevron-right";
    public const string ChevronLeft = "chevron-left";
    public const string Bold = "bold";
    public const string Italic = "italic";
    public const string Underline = "underline";
    public const string Strikethrough = "strikethrough";
    public const string BulletedList = "bulleted-list";
    public const string NumberedList = "numbered-list";
    public const string Edit = "edit";
    public const string Image = "image";
    public const string Attach = "paperclip";
    public const string Settings = "settings";
    public const string Page = "file";
    public const string Section = "section";
    public const string Table = "table";
    public const string Chart = "chart";
    public const string Ink = "pen";
    public const string AlignLeft = "align-left";
    public const string AlignCenter = "align-center";
    public const string AlignRight = "align-right";
    public const string IndentIncrease = "indent-increase";
    public const string IndentDecrease = "indent-decrease";
    public const string Subscript = "subscript";
    public const string Superscript = "superscript";
    public const string Theme = "palette";
    public const string File = "file";
    public const string Info = "info";

    public static Control Glyph(string glyph, double size = 14, string? accessibleName = null)
    {
        var icon = new Image
        {
            Source = BoardsCuiIconResolver.CreateIcon(glyph, BoardsCuiIconResolver.SemanticBrush(glyph)),
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
