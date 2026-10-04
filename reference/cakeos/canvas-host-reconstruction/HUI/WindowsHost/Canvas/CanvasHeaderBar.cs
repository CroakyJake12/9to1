using Haven.UI;
using Haven.UI.Components;
using HuiButton = Haven.UI.Components.Button;
using HuiText = Haven.UI.Components.Text;

namespace CakeOS.HuiWindowsHost.Canvas;

/// <summary>
/// Rnote header-bar equivalent: File / Edit / View / Help menus plus zoom and
/// document status. Menus are light-dismiss popups built from the existing
/// shared PopupMenu; the controller owns item wiring so this bar stays a
/// pure view. Shortcuts and About surface here (dialogs remain NEEDS-FROM-W4
/// and are rendered as status + popup content in Phase 1).
/// </summary>
internal sealed class CanvasHeaderBar : Container
{
    public CanvasHeaderBar()
    {
        Name = "Canvas.Header";
        Layout = HavenLayout.Horizontal;
        SetValue(HavenProperties.Width, HavenLength.Percent(100));
        SetValue(HavenProperties.Height, HavenLength.Px(46));
        SetValue(HavenProperties.Gap, HavenLength.Px(8));
        SetValue(HavenProperties.VerticalAlignment, HavenVerticalAlignment.Center);

        FileButton = MakeMenuButton("Canvas.Menu.File", "File");
        EditButton = MakeMenuButton("Canvas.Menu.Edit", "Edit");
        ViewButton = MakeMenuButton("Canvas.Menu.View", "View");
        HelpButton = MakeMenuButton("Canvas.Menu.Help", "Help");

        FileButton.Invoked += (_, _) => MenuRequested?.Invoke(this, "File");
        EditButton.Invoked += (_, _) => MenuRequested?.Invoke(this, "Edit");
        ViewButton.Invoked += (_, _) => MenuRequested?.Invoke(this, "View");
        HelpButton.Invoked += (_, _) => MenuRequested?.Invoke(this, "Help");

        ZoomOutButton = MakeMenuButton("Canvas.Header.ZoomOut", "−");
        ZoomInButton = MakeMenuButton("Canvas.Header.ZoomIn", "+");
        ZoomOutButton.Invoked += (_, _) => ZoomRequested?.Invoke(this, -1);
        ZoomInButton.Invoked += (_, _) => ZoomRequested?.Invoke(this, +1);

        ZoomLabel = new HuiText { Name = "Canvas.Header.Zoom", Content = "4x" };
        ZoomLabel.SetValue(HavenProperties.FontSize, 13d);
        ZoomLabel.SetValue(HavenProperties.Foreground, "TextSecondary");
        ZoomLabel.SetValue(HavenProperties.Width, HavenLength.Px(56));

        DocumentLabel = new HuiText { Name = "Canvas.Header.Document", Content = "Untitled.rnote" };
        DocumentLabel.SetValue(HavenProperties.FontSize, 13d);
        DocumentLabel.SetValue(HavenProperties.Foreground, "TextPrimary");
        DocumentLabel.SetValue(HavenProperties.Width, HavenLength.Percent(100));

        Add(FileButton);
        Add(EditButton);
        Add(ViewButton);
        Add(HelpButton);
        Add(ZoomOutButton);
        Add(ZoomLabel);
        Add(ZoomInButton);
        Add(DocumentLabel);
    }

    public HuiButton FileButton { get; }
    public HuiButton EditButton { get; }
    public HuiButton ViewButton { get; }
    public HuiButton HelpButton { get; }
    public HuiButton ZoomOutButton { get; }
    public HuiButton ZoomInButton { get; }
    public HuiText ZoomLabel { get; }
    public HuiText DocumentLabel { get; }

    public event EventHandler<string>? MenuRequested;
    public event EventHandler<int>? ZoomRequested;

    public void SetZoom(double zoom) => ZoomLabel.Content = $"{zoom:0.##}x";

    public void SetDocument(string name, bool dirty) =>
        DocumentLabel.Content = dirty ? $"{name} •" : name;

    private static HuiButton MakeMenuButton(string name, string content)
    {
        var button = new HuiButton { Name = name, Content = content, Variant = ButtonVariant.Ghost };
        button.SetValue(HavenProperties.Height, HavenLength.Px(36));
        button.SetValue(HavenProperties.MinHeight, HavenLength.Px(36));
        button.SetValue(HavenProperties.Padding, HavenThickness.Parse("0px 12px"));
        button.SetValue(HavenProperties.FontSize, 13d);
        button.Accessibility.AccessibleName = content;
        return button;
    }
}
