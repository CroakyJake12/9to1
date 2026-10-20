using System.Reflection;
using System.Runtime.ExceptionServices;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using CakeOS.Cui;
using CakeOS.Cui.Runtime;
using Haven.Application;
using Haven.Core;
using Haven.Desktop;
using Haven.SharedHeader;
using HavenOS.Home.Core;
using Microsoft.Extensions.DependencyInjection;

namespace HavenOS.Browse.Host.Tests;

/// <summary>
/// Causal integration coverage for the native shared-panel host discovery and the truthful
/// mount/refusal contract. The shell's nested panel host starts hidden (HasOpenPanel=false),
/// so a live visual/logical-descendant search misses it and the panel "opens" without ever
/// being shown. This suite proves the retained authored CUI graph locates the exact host even
/// while closed, and that mounting reports the real outcome instead of silent success.
/// </summary>
public sealed class NativeSharedPanelIntegrationTests
{
    [AvaloniaFact]
    public void Retained_closed_panel_host_is_found_by_authored_graph_but_not_visual_descendants()
    {
        using var loader = new CuiControlLoader();
        // The authored shell starts CLOSED: HasOpenPanel resolves false, so the backdrop and the
        // nested panel host are hidden before any panel ever opens.
        loader.SetBindingContext(new ClosedShellBindings());
        var shell = HavenSharedHeaderDocuments.LoadShell();
        var loaded = loader.TryLoad(shell);
        Assert.NotNull(loaded.Root);
        Assert.DoesNotContain(loaded.Diagnostics, diagnostic => diagnostic.Severity == CuiDiagnosticSeverity.Error);

        // Regression guard: the previous implementation searched live visual/logical descendants
        // and therefore missed the closed nested panel host; this is exactly why the native
        // Notifications click reported ACTION_INVOKED_ONLY with no visible panel.
        var viaVisualDescendants = loaded.Root!.GetLogicalDescendants()
            .OfType<CuiContainerControl>()
            .FirstOrDefault(control => control.Name == "shared-panel-host");
        Assert.Null(viaVisualDescendants);

        // The fixed finder walks the RETAINED authored CUI graph and locates the exact named host
        // even while the shell is closed.
        var viaAuthoredGraph = CallFindNamedContainer(loaded.Root, "shared-panel-host");
        Assert.NotNull(viaAuthoredGraph);
        Assert.Equal("shared-panel-host", viaAuthoredGraph!.Name);

        // The scene host (always visible) is found by both strategies.
        var sceneHost = CallFindNamedContainer(loaded.Root, "shared-scene-host");
        Assert.NotNull(sceneHost);
    }

    [AvaloniaFact]
    public void ShowSharedPanel_refuses_and_retires_when_authored_host_is_missing()
    {
        var rig = new Rig();
        try
        {
            var host = rig.Host;
            // Force the validated-host to be absent (e.g. degrade path), then prove mounting is a
            // real refusal, never a silent success.
            Set(host, "_sharedPanelHost", null);
            using var loader = new CuiControlLoader();
            var loaded = loader.TryLoad(HavenSharedHeaderDocuments.LoadNotifications());
            Assert.NotNull(loaded.Root);
            var mounted = CallShowSharedPanel(host, loader, loaded.Root!);
            Assert.False(mounted);
            // The incoming loader was retired (no host to mount into) and no panel state was set.
            Assert.Null(Field<CuiContainerControl?>(host, "_sharedPanelHost"));
            Assert.Null(Field<CuiControlLoader?>(host, "_sharedPanelLoader"));
        }
        finally
        {
            rig.FinishAsync().GetAwaiter().GetResult();
        }
    }

    [AvaloniaFact]
    public void ShowSharedPanel_mounts_real_panel_into_validated_host_and_opens()
    {
        var rig = new Rig();
        try
        {
            var host = rig.Host;
            using var loader = new CuiControlLoader();
            var loaded = loader.TryLoad(HavenSharedHeaderDocuments.LoadNotifications());
            Assert.NotNull(loaded.Root);
            // A real nested container is the authored host; mounting must succeed and open.
            var panelHost = new CuiContainerControl("Vertical Stack");
            Set(host, "_sharedPanelHost", panelHost);
            var mounted = CallShowSharedPanel(host, loader, loaded.Root!);
            Assert.True(mounted);
            Assert.Same(loader, Field<CuiControlLoader?>(host, "_sharedPanelLoader"));
            Assert.Same(loaded.Root, Field<Control?>(host, "_sharedPanelRoot"));
        }
        finally
        {
            rig.FinishAsync().GetAwaiter().GetResult();
        }
    }

    /// <summary>Reflects the fixed retained-authored-graph finder on the real UnifiedWorkspaceHost.</summary>
    private static CuiContainerControl? CallFindNamedContainer(Control root, string name)
    {
        var hostType = typeof(App).GetNestedType("UnifiedWorkspaceHost", BindingFlags.NonPublic)!;
        var method = hostType.GetMethod("FindNamedContainer", BindingFlags.Static | BindingFlags.NonPublic)!;
        try { return (CuiContainerControl?)method.Invoke(null, [root, name]); }
        catch (TargetInvocationException failure) when (failure.InnerException is { } actual)
        { ExceptionDispatchInfo.Capture(actual).Throw(); throw; }
    }

    private static bool CallShowSharedPanel(App.UnifiedWorkspaceHost host, CuiControlLoader loader, Control root)
    {
        var method = typeof(App.UnifiedWorkspaceHost)
            .GetMethod("ShowSharedPanel", BindingFlags.Instance | BindingFlags.NonPublic)!;
        try { return (bool)method.Invoke(host, [loader, root])!; }
        catch (TargetInvocationException failure) when (failure.InnerException is { } actual)
        { ExceptionDispatchInfo.Capture(actual).Throw(); throw; }
    }

    private static T Field<T>(object owner, string name) =>
        (T)owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(owner)!;

    private static void Set(object owner, string name, object? value) =>
        owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(owner, value);

    /// <summary>Mirrors the authored shell's initial closed state: HasOpenPanel is false.</summary>
    private sealed class ClosedShellBindings : ICuiBindingContext
    {
        public bool TryGetValue(string path, out object? value)
        {
            value = path == "HasOpenPanel" ? false : null;
            return value is not null;
        }
    }

    private sealed class Rig
    {
        internal readonly App App = new();
        internal readonly Paths Paths = new();
        internal readonly ServiceProvider Services;
        internal readonly HomeNativeWindowsComposition Home;
        internal readonly App.UnifiedWorkspaceHost Host;
        internal Rig()
        {
            Assert.True(OperatingSystem.IsWindows(), "This owning host fixture requires actual Windows Home; Linux is unvalidated.");
            Directory.CreateDirectory(Paths.DataDirectory);
            Services = new ServiceCollection().BuildServiceProvider();
            Home = new(new FileHomeCoreStateStore(Path.Combine(Paths.DataDirectory, "home.json")),
                new OperatingSystemPrincipalSource(), Paths, new("9to1.host-lifetime." + Guid.NewGuid().ToString("N")));
            Host = new(App, Services, Home, CancellationToken.None, CancellationToken.None);
        }
        internal async Task FinishAsync()
        {
            await Home.CloseAndDrainAsync();
            await Services.DisposeAsync();
            Directory.Delete(Paths.DataDirectory, true);
        }
    }

    private sealed class Paths : IAppPaths
    {
        public string DataDirectory { get; } = Path.Combine(Path.GetTempPath(), "native-shared-panel-" + Guid.NewGuid().ToString("N"));
        public string DatabasePath => Path.Combine(DataDirectory, "haven.db");
        public string BrowserProfileDirectory => Path.Combine(DataDirectory, "browser");
        public string AttachmentsDirectory => Path.Combine(DataDirectory, "attachments");
        public string LogsDirectory => Path.Combine(DataDirectory, "logs");
        public string LegacyStatePath => Path.Combine(DataDirectory, "legacy.json");
    }
}
