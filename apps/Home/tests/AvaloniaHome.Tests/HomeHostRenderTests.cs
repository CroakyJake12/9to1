#nullable enable
using System;
using System.IO;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;
using System.Threading.Tasks;
using System.Linq;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CakeOS.Cui.Runtime;
using CakeOS.Cui.Themes;
using Haven.CUI.DevTools;
using HavenOS.Home;
using HavenOS.Home.Core;
using Haven.Application;
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
        HomeNativeWindowsComposition? originalComposition = null;
        HomeApp? originalApp = null;
        var failures = new List<Exception>();
        try
        {
            originalComposition = new HomeNativeWindowsComposition(
                new FileHomeCoreStateStore(Path.Combine(dataDirectory, "home-core-state.json")),
                new OperatingSystemPrincipalSource(), new TestPaths(dataDirectory),
                new HomeNativeWindowsEndpoint("9to1.home.render." + Guid.NewGuid().ToString("N")));
            AppBuilder.Configure(() =>
                {
                    var app = new HomeApp(originalComposition);
                    originalApp = app;
                    return app;
                })
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
                Assert.Equal(19, source.Source.Span.StartLine);
                Assert.Equal("Button", source.ComponentType);
                Assert.Equal("nav-settings", source.AuthoredId);
                var action = Assert.Single(inspection.GetActions(inspectedSettings.ElementId));
                Assert.Equal("NavigateSettings", action.Command);
                Assert.True(action.DispatcherConnected);
                Assert.True(action.DispatchWired);
                Assert.True(action.HandlerRegistered);
                Assert.True(action.HostAvailable);
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
                WaitFor(() => window.GetVisualDescendants().Any(control => control.Name == "home-settings-page"),
                    "The original Home Settings route did not open.");
                var settingsStatus = Assert.Single(window.GetVisualDescendants().OfType<TextBlock>(),
                    control => control.Name == "home-layout-status");
                Assert.Contains("Saved revision 0", settingsStatus.Text, StringComparison.Ordinal);
                Assert.Contains("CAKE ID sign-in is separate from Home permissions", Assert.Single(window.GetVisualDescendants().OfType<TextBlock>(),
                    control => control.Name == "home-profile-status").Text, StringComparison.Ordinal);
                var allowTiles = Assert.Single(window.GetVisualDescendants().OfType<Button>(),
                    control => control.Name == "settings-allow-ai-tiles");
                allowTiles.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                WaitFor(() => Assert.Single(window.GetVisualDescendants().OfType<TextBlock>(),
                    control => control.Name == "home-layout-status").Text?.Contains("AI tiles: allowed", StringComparison.Ordinal) == true,
                    "The original Home layout operation did not persist its preference.");
                var persisted = originalComposition.StateStore.ReadAsync().GetAwaiter().GetResult();
                Assert.True(persisted.IsSuccess);
                Assert.Single(persisted.State!.Records, record => record.RecordId == "home.dashboard.layout");
                Assert.False(originalComposition.InstalledPeerAdmissionConfigured);
                var homeButton = Assert.Single(window.GetVisualDescendants().OfType<Button>(), control => control.Name == "nav-home");
                homeButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                WaitFor(() => window.GetVisualDescendants().Any(control => control.Name == "dashboard-page"),
                    "The original Home dashboard route did not reopen.");
                var add = Assert.Single(window.GetVisualDescendants().OfType<Button>(), control => control.Name == "add-dashboard-tile");
                add.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                WaitFor(() => Assert.Single(window.GetVisualDescendants().OfType<ListBox>(),
                    control => control.Name == "dashboard-tiles").ItemCount == 1,
                    "The original Core tile did not appear after saving.");
                var updated = Assert.IsType<CuiLiveTreeInspector>(originalApp!.CaptureDiagnostics(window));
                var operationStatus = Assert.Single(window.GetVisualDescendants().OfType<TextBlock>(), control => control.Name == "operation-status");
                var operationId = Assert.Single(updated.Tree.Search("operation-status")).ElementId;
                Assert.Equal(operationStatus.Text, Assert.Single(updated.GetBindingTraces(operationId)).NativeValue);

                // Exercise missing and faulted binding observations against the actual Home document.
                var fallbackLoader = new CuiControlLoader();
                fallbackLoader.SetBindingContext(CreateRouteOnlyBindingContext());
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
            var reopen = originalApp!.ShowOriginalShellAsync();
            Assert.NotNull(reopen);
            WaitFor(() => reopen.IsCompleted, "The same owning Home reopen driver did not settle.");
            reopen.GetAwaiter().GetResult();
            var reopened = Assert.IsType<Window>(originalApp.OriginalShellWindow);
            Assert.NotSame(window, reopened);
            Assert.True(reopened.IsVisible);
            try
            {
                Assert.Equal(CuiTheme.Retro, CuiSurfacePaletteCatalog.ActiveTheme);
                Assert.Equal(CuiTheme.Retro, Application.Current!.Resources["CuiTheme"]);
                reopened.Show();
                reopened.Measure(new Size(1200, 800));
                reopened.Arrange(new Rect(0, 0, 1200, 800));
                reopened.UpdateLayout();
                Assert.Equal(1, Assert.Single(reopened.GetVisualDescendants().OfType<ListBox>(),
                    control => control.Name == "dashboard-tiles").ItemCount);
                var reopenedSettings = Assert.Single(reopened.GetVisualDescendants().OfType<Button>(), control => control.Name == "nav-settings");
                reopenedSettings.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                WaitFor(() => reopened.GetVisualDescendants().Any(control => control.Name == "home-layout-status"),
                    "Reopened Settings was unavailable.");
                Assert.Contains("AI tiles: allowed", Assert.Single(reopened.GetVisualDescendants().OfType<TextBlock>(),
                    control => control.Name == "home-layout-status").Text, StringComparison.Ordinal);
                Assert.Single(reopened.GetVisualDescendants().OfType<Button>(), control => control.Name == "nav-home")
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                WaitFor(() => reopened.GetVisualDescendants().Any(control => control.Name == "home-hero"),
                    "Reopened dashboard was unavailable.");
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
        catch (Exception original) { failures.Add(original); }
        finally
        {
            // Always join the SAME original host sources, even when a UI assertion failed.
            try
            {
                var close = originalApp?.CloseAndDrainOriginalHostAsync() ?? originalComposition?.CloseAndDrainAsync();
                if (close is not null)
                {
                    while (!close.IsCompleted) { Dispatcher.UIThread.RunJobs(); Thread.Yield(); }
                    try { close.GetAwaiter().GetResult(); }
                    catch when (close.IsFaulted) { throw close.Exception!; }
                }
            }
            catch (Exception cleanup) { failures.Add(cleanup); }
            Environment.SetEnvironmentVariable("HAVEN_DATA_DIR", previousDataDirectory);
            CuiSurfacePaletteCatalog.ActiveTheme = CuiTheme.Glow;
            try { Directory.Delete(dataDirectory, recursive: true); }
            catch (Exception cleanup) { failures.Add(cleanup); }
        }
        if (failures.Count == 1) ExceptionDispatchInfo.Capture(failures[0]).Throw();
        if (failures.Count > 1) throw new AggregateException("Original Home render and cleanup failures.", failures);
    }

    private static void WaitFor(Func<bool> completed, string message) => Assert.True(SpinWait.SpinUntil(() =>
    {
        Dispatcher.UIThread.RunJobs();
        return completed();
    }, TimeSpan.FromSeconds(10)), message);

    private sealed class TestPaths(string root) : IAppPaths
    {
        public string DataDirectory => root;
        public string DatabasePath => Path.Combine(root, "haven.db");
        public string BrowserProfileDirectory => Path.Combine(root, "browser-profile");
        public string AttachmentsDirectory => Path.Combine(root, "attachments");
        public string LogsDirectory => Path.Combine(root, "logs");
        public string LegacyStatePath => Path.Combine(root, "state.json");
    }

    private static CuiViewModel CreateRouteOnlyBindingContext()
    {
        var routes = HomeCuiSurface.LoadDefault();
        var context = new CuiViewModel();
        context.Set(nameof(HomeCuiSurface.IsDashboard), routes.IsDashboard);
        context.Set(nameof(HomeCuiSurface.IsLibrary), routes.IsLibrary);
        context.Set(nameof(HomeCuiSurface.IsEvents), routes.IsEvents);
        return context;
    }

    private sealed class FaultyHomeBindingContext : CakeOS.Cui.ICuiBindingContext
    {
        private readonly CuiViewModel _routes = CreateRouteOnlyBindingContext();

        public bool TryGetValue(string path, out object? value)
        {
            if (_routes.TryGetValue(path, out value)) return true;
            value = null;
            if (path == "CatalogSummary") throw new InvalidOperationException("Catalog unavailable");
            return false;
        }
    }
}
