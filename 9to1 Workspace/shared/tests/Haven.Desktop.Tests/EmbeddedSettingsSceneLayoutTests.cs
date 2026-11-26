using Avalonia;
using Avalonia.Headless.XUnit;
using Haven.Desktop.HavenUI.Backend;
using Haven.Desktop.Views.Pages.Settings;
using Haven.UI;

namespace Haven.Desktop.Tests;

public sealed class EmbeddedSettingsSceneLayoutTests
{
    [AvaloniaFact]
    public void Embedded_permissions_settings_measures_and_arranges_after_sidebar_is_hidden()
    {
        using var scene = new SettingsHavenScene();
        scene.ShowAtlasSection("permissions");
        var host = new HavenSceneControl { Root = scene.Root };

        // Native embedding must parse and arrange the actual grid tracks. Merely
        // checking visibility misses an invalid track that crashes first measure.
        host.Measure(new Size(900, 640));
        host.Arrange(new Rect(0, 0, 900, 640));

        Assert.Equal("permissions", scene.ActiveSection);
        Assert.Equal(HavenVisibility.Collapsed, scene.Sidebar.GetValue(HavenProperties.Visibility));
        Assert.False(scene.Sidebar.IsIncluded);
        Assert.Equal(0d, scene.Content.Bounds.X);
        Assert.Equal(900d, scene.Content.Bounds.Width);
        Assert.True(scene.FilePermissionSelect.IsIncluded);
        Assert.True(scene.FilePermissionSelect.Bounds.Width > 0);
        Assert.True(scene.FilePermissionSelect.Bounds.Height > 0);
        Assert.True(scene.SavePermissionsButton.IsIncluded);
        Assert.True(scene.SavePermissionsButton.Bounds.Width > 0);
    }
}
