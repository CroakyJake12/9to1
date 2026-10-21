using Avalonia.Headless.XUnit;
using CakeOS.Cui.Language;
using CakeOS.Cui.Runtime;
using Haven.Browser;
using Xunit;

namespace HavenOS.Apps.Browse.Tests;

public sealed partial class BrowseNativeUiTests
{
    // Diagnostic preflight for the SAME bare production scene used by the failing tab fixtures.
    // No fake renderer, fallback root or ignored diagnostics: report the original failure in full.
    [AvaloniaFact]
    public async Task Actual_Browse_scene_preflight_reports_original_load_diagnostics()
    {
        var chrome = await BrowseChrome.CreateAsync(new Paths());
        var workspace = new BrowseNativeWorkspace(chrome, _ => true);
        try
        {
            var scene = BrowseNativeSurface.CreateScene(workspace, new FixtureReadiness());
            RegisterProductionImageResolver(scene.ControlRegistry!);
            using var loader = new CuiControlLoader(scene.ControlRegistry!);
            loader.SetSurface(scene.Surface); // Same canonical accent catalogue resolution used by CuiSceneHost.
            loader.SetBindingContext(scene.Bindings);
            loader.SetActionDispatcher(scene.Actions);
            var loaded = loader.TryLoad(BrowseNativeWorkspace.LoadDocument());
            Assert.True(loaded.Root is not null && !loaded.Diagnostics.Any(item => item.Severity == CuiDiagnosticSeverity.Error),
                "Original production Browse scene diagnostics:" + Environment.NewLine +
                string.Join(Environment.NewLine, loaded.Diagnostics.Select(item => item.ToString())));
        }
        finally { await workspace.DisposeAsync().AsTask(); }
    }
}
