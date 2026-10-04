using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.HavenUI.Backend;
using Haven.Desktop.Views.Shell.TopRail;
using Haven.UI;

namespace Haven.Desktop.Tests;

public sealed class AppLauncherCoverageTests
{
    [Fact]
    public void Built_in_App_categories_use_registry_tags_before_key_fallbacks()
    {
        Assert.Equal("Media & creativity", AppLauncherControl.CategoryFor(Find("imagine")));
        Assert.Equal("Productivity", AppLauncherControl.CategoryFor(Find("data")));
        Assert.Equal("General", AppLauncherControl.CategoryFor(Find("go")));
    }

    [AvaloniaFact]
    public void Every_enabled_App_is_shown_exactly_once_in_the_Haven_owned_launcher()
    {
        var apps = BuiltInModeSeed.Modes.ToArray();
        var pinned = apps.Take(3).Select(item => item.Id).ToHashSet();
        var launcher = new AppLauncherControl();
        var window = new Window { Content = launcher };
        try
        {
            launcher.Configure(apps, pinned, false, (_, _) => { }, () => { });
            window.Show();
            window.UpdateLayout();

            var labels = launcher.HavenScene.AppButtons
                .Select(button => button.Content)
                .Where(text => apps.Any(app => app.Name.Equals(text, StringComparison.Ordinal)))
                .ToArray();

            foreach (var app in apps)
                Assert.Equal(1, labels.Count(label => app.Name.Equals(label, StringComparison.Ordinal)));

            Assert.IsType<HavenSceneControl>(launcher.Content);
            Assert.Same(launcher.HavenScene.Root, launcher.SceneHost.Root);
            Assert.True(launcher.FocusSearch());
            Assert.True(launcher.HavenScene.Search.State.HasFlag(HavenElementState.Focused));
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Pins_from_different_categories_render_first_and_keep_the_supplied_order()
    {
        var data = Find("data");
        var imagine = Find("imagine");
        var plan = Find("plan");
        var go = Find("go");
        var apps = new[] { data, imagine, plan, go };
        var pinned = new HashSet<Guid> { data.Id, imagine.Id };
        var launcher = new AppLauncherControl();
        var window = new Window { Content = launcher };
        try
        {
            launcher.Configure(apps, pinned, false, (_, _) => { }, () => { });
            window.Show();
            window.UpdateLayout();

            Assert.Equal(
                new[] { data.Name, imagine.Name, plan.Name, go.Name },
                launcher.HavenScene.AppButtons.Select(button => button.Content));

            var sectionNames = launcher.HavenScene.Root.DescendantsAndSelf()
                .Where(element => element.Name is "HeaderDropdown.Apps.Section.Pinned" or "HeaderDropdown.Apps.Section.Available")
                .Select(element => element.Name)
                .ToArray();
            Assert.Equal(
                new[]
                {
                    "HeaderDropdown.Apps.Section.Pinned",
                    "HeaderDropdown.Apps.Section.Available"
                },
                sectionNames);
        }
        finally
        {
            window.Close();
        }
    }

    private static ModeDefinition Find(string key) =>
        Assert.Single(BuiltInModeSeed.Modes, item => item.Key.Equals(key, StringComparison.Ordinal));
}
