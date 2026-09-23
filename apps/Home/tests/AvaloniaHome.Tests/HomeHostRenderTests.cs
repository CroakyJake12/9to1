using System;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Xunit;

namespace AvaloniaHome.Tests;

public sealed class HomeHostRenderTests
{
    [Fact]
    public void Native_home_host_loads_canonical_cui_into_a_measured_window()
    {
        AppBuilder.Configure<HomeApp>()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions())
            .UseSkia()
            .SetupWithoutStarting();

        var path = Program.FindCuiFile();
        Assert.NotNull(path);
        Assert.EndsWith(Path.Combine("UI", "Home.cui"), path, StringComparison.OrdinalIgnoreCase);

        var window = Assert.IsType<HomeApp>(Application.Current).BuildWindow();
        try
        {
            window.Show();
            window.Measure(new Size(1200, 800));
            window.Arrange(new Rect(0, 0, 1200, 800));
            window.UpdateLayout();

            var root = Assert.IsType<StackPanel>(window.Content);
            Assert.True(root.Bounds.Width > 0 && root.Bounds.Height > 0, $"Home root was not arranged: {root.Bounds}");
            var controls = window.GetVisualDescendants().OfType<Control>().ToArray();
            Assert.True(controls.Any(control => control.Name == "nav-home"),
                "Home navigation control was absent: " + string.Join(", ", controls.Select(control => control.Name ?? control.GetType().Name)));
            Assert.Contains(controls, control => control.Name == "home-hero");
            Assert.Contains(controls, control => control.Name == "app-cards");
            var catalogStatus = Assert.Single(controls.OfType<TextBlock>(), control => control.Name == "catalog-status");
            Assert.Contains("not configured", catalogStatus.Text, StringComparison.OrdinalIgnoreCase);
            var runtimeStatus = Assert.Single(controls.OfType<TextBlock>(), control => control.Name == "runtime-status");
            Assert.Contains("not configured", runtimeStatus.Text, StringComparison.OrdinalIgnoreCase);

            var settings = Assert.Single(controls.OfType<Button>(), control => control.Name == "nav-settings");
            settings.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            var operationStatus = Assert.Single(controls.OfType<TextBlock>(), control => control.Name == "operation-status");
            Assert.Contains("not connected", operationStatus.Text, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            window.Close();
        }
    }
}
