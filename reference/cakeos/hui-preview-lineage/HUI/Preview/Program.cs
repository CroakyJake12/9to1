using System.Text.Json;
using Haven.UI;
using Haven.UI.Components;

var root = BuildScene();
var viewport = new HavenSize(960, 600);
var layout = new HavenLayoutEngine();
layout.Layout(root, viewport, HavenPlatform.Linux, new PreviewMeasureContext());

var commands = new HavenSceneRenderer().Render(root);
if (commands.Count == 0)
{
    Console.Error.WriteLine("HUI render produced no draw commands.");
    return 1;
}

var summary = new
{
    platform = HavenPlatform.Linux.ToString(),
    viewport = new { width = viewport.Width, height = viewport.Height },
    root = root.Name,
    drawCommandCount = commands.Count,
    drawCommands = commands
        .GroupBy(command => command.GetType().Name)
        .OrderBy(group => group.Key, StringComparer.Ordinal)
        .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal),
};

Console.WriteLine(JsonSerializer.Serialize(summary, new JsonSerializerOptions { WriteIndented = true }));
return 0;

static Page BuildScene()
{
    var root = new Page
    {
        Name = "CakeOS.HuiPreview.Root",
        Layout = HavenLayout.Vertical,
    };
    root.SetValue(HavenProperties.Width, HavenLength.Percent(100));
    root.SetValue(HavenProperties.Height, HavenLength.Percent(100));
    root.SetValue(HavenProperties.Padding, HavenThickness.Parse("32px"));
    root.SetValue(HavenProperties.Gap, HavenLength.Px(16));

    var title = new Haven.UI.Components.Text
    {
        Name = "CakeOS.HuiPreview.Title",
        Content = "CakeOS HUI preview",
    };
    title.SetValue(HavenProperties.FontSize, 28d);

    var status = new Haven.UI.Components.Text
    {
        Name = "CakeOS.HuiPreview.Status",
        Content = "Linux HUI scene/layout/render smoke path is active.",
    };
    status.SetValue(HavenProperties.FontSize, 16d);

    var action = new Button
    {
        Name = "CakeOS.HuiPreview.Action",
        Content = "HUI is rendering",
    };
    action.SetValue(HavenProperties.Width, HavenLength.Px(180));
    action.SetValue(HavenProperties.Height, HavenLength.Px(44));

    root.Add(title);
    root.Add(status);
    root.Add(action);
    return root;
}

file sealed class PreviewMeasureContext : IHavenMeasureContext
{
    public HavenSize MeasureLeaf(HavenElement element, HavenSize available)
    {
        return element switch
        {
            Haven.UI.Components.Text text => FitText(text.Content, text.GetValue(HavenProperties.FontSize), available),
            Button button => new HavenSize(Math.Min(available.Width, Math.Max(120, button.Content.Length * 9 + 36)), Math.Min(available.Height, 44)),
            _ => new HavenSize(Math.Min(available.Width, 48), Math.Min(available.Height, 48)),
        };
    }

    private static HavenSize FitText(string text, double fontSize, HavenSize available)
    {
        var size = fontSize <= 0 ? 14 : fontSize;
        var width = Math.Max(24, text.Length * size * 0.58);
        return new HavenSize(Math.Min(available.Width, width), Math.Min(available.Height, size * 1.4));
    }
}
