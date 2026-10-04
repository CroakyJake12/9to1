using Haven.UI;
using Haven.UI.Components;

namespace CakeOS.Hui.Renderer;

/// <summary>
/// Built-in HUI demonstration scene. This is TEST INFRASTRUCTURE, not an
/// application: it exercises pointer/keyboard activation through the shared
/// input router. Applications (Canvas, Boards) mount their own roots.
/// </summary>
public static class HuiDemoScene
{
    public static (Page Root, Button Action, Text Status) Build(string backendLabel, string title, string body)
    {
        var root = new Page { Name = "PreviewRoot", Layout = HavenLayout.Vertical };
        root.SetValue(HavenProperties.Width, HavenLength.Percent(100));
        root.SetValue(HavenProperties.Height, HavenLength.Percent(100));
        root.SetValue(HavenProperties.Padding, HavenThickness.Parse("42px"));
        root.SetValue(HavenProperties.Gap, HavenLength.Px(18));

        var eyebrow = new Text { Content = backendLabel };
        eyebrow.SetValue(HavenProperties.FontSize, 13d);
        eyebrow.SetValue(HavenProperties.Foreground, "TextSecondary");

        var titleText = new Text { Content = title };
        titleText.SetValue(HavenProperties.FontSize, 30d);
        titleText.SetValue(HavenProperties.Foreground, "TextPrimary");

        var bodyText = new Text { Content = body };
        bodyText.SetValue(HavenProperties.FontSize, 16d);
        bodyText.SetValue(HavenProperties.Foreground, "TextSecondary");

        var action = new Button { Name = "Action", Content = "Test HUI input" };
        action.SetValue(HavenProperties.Width, HavenLength.Px(190));
        action.SetValue(HavenProperties.Height, HavenLength.Px(46));
        action.ClickActions.Add(HavenAction.Parse("Name.Action -> Selected=True"));

        var status = new Text { Name = "Status", Content = "Ready for pointer or keyboard input" };
        status.SetValue(HavenProperties.FontSize, 15d);
        status.SetValue(HavenProperties.Foreground, "TextSecondary");

        root.Add(eyebrow);
        root.Add(titleText);
        root.Add(bodyText);
        root.Add(action);
        root.Add(status);
        return (root, action, status);
    }
}
