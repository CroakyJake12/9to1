// BoardsIcons: one consistent glyph system for the Boards chrome.
// Glyphs come from the Windows system icon fonts (Segoe Fluent Icons with
// Segoe MDL2 Assets fallback) — no new assets, no emoji in production UI.
// Every icon use site must also set ToolTip + AutomationProperties.Name.

using Avalonia.Controls;
using Avalonia.Media;

namespace CakeOS.Apps.Boards.App;

public static class BoardsIcons
{
    public const string FontStack = "Segoe Fluent Icons, Segoe MDL2 Assets";

    public const string Save = "\uE74E";
    public const string Undo = "\uE7A7";
    public const string Redo = "\uE7A6";
    public const string Add = "\uE710";
    public const string Delete = "\uE74D";
    public const string Close = "\uE711";
    public const string Check = "\uE73E";
    public const string Search = "\uE721";
    public const string More = "\uE712";
    public const string ChevronDown = "\uE70D";
    public const string ChevronRight = "\uE76C";
    public const string ChevronLeft = "\uE76B";
    public const string Bold = "\uE8DD";
    public const string Italic = "\uE8DB";
    public const string Underline = "\uE8DC";
    public const string Strikethrough = "\uE8DE";
    public const string BulletedList = "\uE8FD";
    public const string NumberedList = "\uE80A";
    public const string Edit = "\uE70F";
    public const string Image = "\uE8B9";
    public const string Attach = "\uE723";
    public const string Settings = "\uE713";
    public const string Page = "\uE7C3";
    public const string Section = "\uE8B7";
    public const string Table = "\uE9F9";
    public const string Chart = "\uE9D2";
    public const string Ink = "\uE76D";
    public const string AlignLeft = "\uE8E4";
    public const string AlignCenter = "\uE8E8";
    public const string AlignRight = "\uE8EA";
    public const string Subscript = "\uE8EE";
    public const string Superscript = "\uE8ED";
    public const string Theme = "\uE790";
    public const string File = "\uE7C3";
    public const string Info = "\uE946";

    public static TextBlock Glyph(string glyph, double size = 14, string? accessibleName = null)
    {
        var text = new TextBlock
        {
            Text = glyph,
            FontFamily = new FontFamily(FontStack),
            FontSize = size,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
        };
        if (!string.IsNullOrEmpty(accessibleName))
            Avalonia.Automation.AutomationProperties.SetName(text, accessibleName);
        return text;
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
