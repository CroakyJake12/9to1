#nullable enable
using System;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CakeOS.Cui.Runtime;
using CakeOS.Cui.Themes;
using Haven.CUI.DevTools;
using Xunit;

namespace AvaloniaHome.Tests;

public sealed class HomeHostRenderTests
{
    [Fact]
    public void Native_home_host_loads_canonical_cui_into_a_measured_window()
    {
        var previousDataDirectory = Environment.GetEnvironmentVariable("HAVEN_DATA_DIR");
        var dataDirectory = Path.Combine(Path.GetTempPath(), "home-theme-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dataDirectory);
        Environment.SetEnvironmentVariable("HAVEN_DATA_DIR", dataDirectory);
        File.WriteAllText(Path.Combine(dataDirectory, "preferences.json"), """{"havenUiThemeName":"Bubble"}""");
        try
        {
            AppBuilder.Configure<HomeApp>()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions())
                .UseSkia()
                .SetupWithoutStarting();

            var path = Program.FindCuiFile();
            Assert.NotNull(path);
            Assert.EndsWith(Path.Combine("UI", "Home.cui"), path, StringComparison.OrdinalIgnoreCase);

            var window = Assert.IsType<HomeApp>(Application.Current).BuildWindow();
            var bubbleRadius = string.Empty;
            try
            {
                Assert.Equal(CuiTheme.Bubble, CuiSurfacePaletteCatalog.ActiveTheme);
                Assert.Equal(CuiTheme.Bubble, Application.Current!.Resources["CuiTheme"]);
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
                var inspection = Assert.IsType<CuiLiveTreeInspector>(Assert.IsType<HomeApp>(Application.Current).CaptureDiagnostics(window));
                var inspectedSettings = Assert.Single(inspection.Tree.Search("nav-settings"));
                Assert.Contains("nav-settings", inspectedSettings.Selector, StringComparison.Ordinal);
                Assert.True(inspection.GetLayout(inspectedSettings.ElementId)!.Bounds.Width > 0);
                var accessibility = Assert.IsType<AccessibilitySnapshot>(inspection.GetAccessibility(inspectedSettings.ElementId));
                Assert.Equal("Button", accessibility.Role);
                Assert.Equal("Settings", accessibility.Name);
                Assert.Equal("True", accessibility.States["Enabled"]);
                var source = Assert.IsType<CuiAuthoredControlTrace>(inspection.GetSource(inspectedSettings.ElementId));
                Assert.EndsWith("Home.cui", source.Source.FilePath, StringComparison.OrdinalIgnoreCase);
                Assert.Equal(20, source.Source.Span.StartLine);
                Assert.Equal("Button", source.ComponentType);
                Assert.Equal("nav-settings", source.AuthoredId);
                var action = Assert.Single(inspection.GetActions(inspectedSettings.ElementId));
                Assert.Equal("NavigateSettings", action.Command);
                Assert.True(action.DispatcherConnected);
                Assert.True(action.DispatchWired);
                Assert.True(action.HandlerRegistered);
                Assert.False(action.HostAvailable);
                Assert.True(action.NativeEnabled);

                var heroId = Assert.Single(inspection.Tree.Search("home-hero")).ElementId;
                var radius = Assert.Single(inspection.GetResources(heroId).Values, value => value.Key == "CuiCardRadius");
                Assert.True(radius.WasFound);
                bubbleRadius = radius.Value;
                var background = Assert.Single(inspection.GetVisualProperties(heroId), value => value.Property == "Background");
                Assert.Equal("{Resource CuiPanel2Brush}", background.AuthoredExpression);
                Assert.False(string.IsNullOrWhiteSpace(background.EffectiveValue));
                Assert.True(Assert.Single(inspection.GetResources(heroId).Values, value => value.Key == "CuiPanel2Brush").WasFound);
                var rendering = Assert.IsType<CuiRenderingTrace>(inspection.GetRendering(heroId));
                Assert.True(rendering.Bounds.Width > 0);
                Assert.True(rendering.IsVisible);
                Assert.True(rendering.RenderScaling > 0);
                var catalogStatus = Assert.Single(controls.OfType<TextBlock>(), control => control.Name == "catalog-status");
                Assert.Contains("not configured", catalogStatus.Text, StringComparison.OrdinalIgnoreCase);
                var catalogId = Assert.Single(inspection.Tree.Search("catalog-status")).ElementId;
                var catalogBinding = Assert.Single(inspection.GetBindingTraces(catalogId));
                Assert.Equal("CatalogSummary", catalogBinding.Path);
                Assert.Equal("App catalogue is not connected", catalogBinding.Fallback);
                Assert.Equal(BindingStatus.Active, catalogBinding.Status);
                Assert.Equal(catalogStatus.Text, catalogBinding.NativeValue);
                Assert.Equal(catalogStatus.Text, catalogBinding.ResolvedValue);
                var runtimeStatus = Assert.Single(controls.OfType<TextBlock>(), control => control.Name == "runtime-status");
                Assert.Contains("not configured", runtimeStatus.Text, StringComparison.OrdinalIgnoreCase);

                var settings = Assert.Single(controls.OfType<Button>(), control => control.Name == "nav-settings");
                settings.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Dispatcher.UIThread.RunJobs();
                var operationStatus = Assert.Single(controls.OfType<TextBlock>(), control => control.Name == "operation-status");
                Assert.Contains("not connected", operationStatus.Text, StringComparison.OrdinalIgnoreCase);
                var updated = Assert.IsType<CuiLiveTreeInspector>(Assert.IsType<HomeApp>(Application.Current).CaptureDiagnostics(window));
                var operationId = Assert.Single(updated.Tree.Search("operation-status")).ElementId;
                Assert.Equal(operationStatus.Text, Assert.Single(updated.GetBindingTraces(operationId)).NativeValue);

                // Exercise missing and faulted binding observations against the actual Home document.
                var fallbackLoader = new CuiControlLoader();
                fallbackLoader.SetBindingContext(new CuiViewModel());
                var (fallbackRoot, fallbackDiagnostics) = fallbackLoader.LoadFile(path);
                Assert.Empty(fallbackDiagnostics);
                var fallbackInspection = CuiLiveTreeInspector.Capture(Assert.IsType<StackPanel>(fallbackRoot), fallbackLoader);
                var fallbackId = Assert.Single(fallbackInspection.Tree.Search("catalog-status")).ElementId;
                var fallback = Assert.Single(fallbackInspection.GetBindingTraces(fallbackId));
                Assert.Equal(BindingStatus.MissingSource, fallback.Status);
                Assert.True(fallback.UsedFallback);
                Assert.Equal("App catalogue is not connected", fallback.NativeValue);

                var faultyLoader = new CuiControlLoader();
                faultyLoader.SetBindingContext(new FaultyHomeBindingContext());
                var (faultyRoot, faultyDiagnostics) = faultyLoader.LoadFile(path);
                Assert.Empty(faultyDiagnostics);
                var faultyInspection = CuiLiveTreeInspector.Capture(Assert.IsType<StackPanel>(faultyRoot), faultyLoader);
                var faultyId = Assert.Single(faultyInspection.Tree.Search("catalog-status")).ElementId;
                var fault = Assert.Single(faultyInspection.GetBindingTraces(faultyId));
                Assert.Equal(BindingStatus.Faulted, fault.Status);
                Assert.True(fault.UsedFallback);
                Assert.Equal("App catalogue is not connected", fault.NativeValue);
                Assert.Contains("InvalidOperationException", fault.Error);
            }
            finally
            {
                window.Close();
            }
            File.WriteAllText(Path.Combine(dataDirectory, "preferences.json"), """{"havenUiThemeName":"Retro"}""");
            var reopened = Assert.IsType<HomeApp>(Application.Current).BuildWindow();
            try
            {
                Assert.Equal(CuiTheme.Retro, CuiSurfacePaletteCatalog.ActiveTheme);
                Assert.Equal(CuiTheme.Retro, Application.Current!.Resources["CuiTheme"]);
                reopened.Show();
                reopened.Measure(new Size(1200, 800));
                reopened.Arrange(new Rect(0, 0, 1200, 800));
                var retroInspection = Assert.IsType<CuiLiveTreeInspector>(Assert.IsType<HomeApp>(Application.Current).CaptureDiagnostics(reopened));
                var retroHero = Assert.Single(retroInspection.Tree.Search("home-hero")).ElementId;
                Assert.NotEqual(bubbleRadius, Assert.Single(retroInspection.GetResources(retroHero).Values,
                    value => value.Key == "CuiCardRadius").Value);
            }
            finally
            {
                reopened.Close();
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable("HAVEN_DATA_DIR", previousDataDirectory);
            CuiSurfacePaletteCatalog.ActiveTheme = CuiTheme.Glow;
            Directory.Delete(dataDirectory, recursive: true);
        }
    }

    private sealed class FaultyHomeBindingContext : CakeOS.Cui.ICuiBindingContext
    {
        public bool TryGetValue(string path, out object? value)
        {
            value = null;
            if (path == "CatalogSummary") throw new InvalidOperationException("Catalog unavailable");
            return false;
        }
    }
}
