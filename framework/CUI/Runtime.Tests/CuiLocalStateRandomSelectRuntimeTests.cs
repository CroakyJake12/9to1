using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CakeOS.Cui.Language;
using CakeOS.Cui.Themes;
using System.Threading;
using Xunit;

namespace CakeOS.Cui.Runtime.Tests;

/// <summary>
/// Focused regressions for the source-driven choose-once (Selection="RandomOnce") local variable.
/// The selected item is chosen exactly once at mount from the host binding candidate pool, retained
/// across ordinary refresh, and isolated per loader instance. An empty pool fails strictly.
/// </summary>
public sealed class CuiLocalStateRandomSelectRuntimeTests
{
    private const string Markup = """
        <Cui>
          <Variable Type="string" ID="Chosen" Source="{Binding Candidates}" Selection="RandomOnce"/>
          <Container Type="Vertical Stack">
            <Button id="show" content="{Binding Chosen}" />
          </Container>
        </Cui>
        """;

    private static CuiViewModel Host(params string[] candidates)
    {
        var host = new CuiViewModel();
        host.Set("Candidates", candidates);
        return host;
    }

    [Fact]
    public async Task RandomOnce_chooses_from_pool_via_injected_chooser_and_survives_refresh()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        await session.Dispatch<bool>(async () =>
        {
            using var loader = new CuiControlLoader();
            loader.SetSurface("Imagine");
            loader.SetAppearance(CuiAppearance.Dark);
            loader.SetBindingContext(Host("😀", "😎", "🎯"));
            loader.RandomSelectionOverride = list => list[0];

            var (root, diagnostics) = loader.LoadMarkup(Markup);
            Assert.Empty(diagnostics);
            var surface = Assert.IsType<CuiContainerControl>(root!);
            loader.WireBindings(surface);

            var window = new Window { Content = root, Width = 400, Height = 200 };
            window.Show(); Pump();
            try
            {
                var button = Assert.Single(window.GetVisualDescendants().OfType<Button>(), b => AutomationProperties.GetAutomationId(b) == "show");
                Assert.Equal("😀", Assert.IsType<string>(button.Content));

                // Ordinary binding refresh must NOT resample the retained selection.
                loader.RefreshBindings(); Pump();
                Assert.Equal("😀", Assert.IsType<string>(button.Content));

                // A different chooser policy is still honored only at mount; refresh keeps it.
                loader.RandomSelectionOverride = list => list[list.Count - 1];
                loader.RefreshBindings(); Pump();
                Assert.Equal("😀", Assert.IsType<string>(button.Content));
            }
            finally { window.Close(); }
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task RandomOnce_instances_isolate_across_two_loaders()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        await session.Dispatch<bool>(async () =>
        {
            using var loaderA = new CuiControlLoader();
            using var loaderB = new CuiControlLoader();
            loaderA.SetSurface("Imagine"); loaderB.SetSurface("Imagine");
            loaderA.SetAppearance(CuiAppearance.Dark); loaderB.SetAppearance(CuiAppearance.Dark);
            loaderA.SetBindingContext(Host("A1", "A2"));
            loaderB.SetBindingContext(Host("B1", "B2"));
            loaderA.RandomSelectionOverride = list => list[0];
            loaderB.RandomSelectionOverride = list => list[list.Count - 1];

            var (rootA, diagA) = loaderA.LoadMarkup(Markup);
            var (rootB, diagB) = loaderB.LoadMarkup(Markup);
            Assert.Empty(diagA); Assert.Empty(diagB);
            loaderA.WireBindings(Assert.IsType<CuiContainerControl>(rootA!));
            loaderB.WireBindings(Assert.IsType<CuiContainerControl>(rootB!));

            var windowA = new Window { Content = rootA, Width = 400, Height = 200 };
            var windowB = new Window { Content = rootB, Width = 400, Height = 200 };
            windowA.Show(); windowB.Show(); Pump();
            try
            {
                var buttonA = Assert.Single(windowA.GetVisualDescendants().OfType<Button>(), b => AutomationProperties.GetAutomationId(b) == "show");
                var buttonB = Assert.Single(windowB.GetVisualDescendants().OfType<Button>(), b => AutomationProperties.GetAutomationId(b) == "show");
                Assert.Equal("A1", Assert.IsType<string>(buttonA.Content));
                Assert.Equal("B2", Assert.IsType<string>(buttonB.Content));
            }
            finally { windowA.Close(); windowB.Close(); }
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task RandomOnce_empty_pool_fails_strictly()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        await session.Dispatch<bool>(async () =>
        {
            using var loader = new CuiControlLoader();
            loader.SetSurface("Imagine");
            loader.SetAppearance(CuiAppearance.Dark);
            loader.SetBindingContext(Host());

            var (root, diagnostics) = loader.LoadMarkup(Markup);
            Assert.Null(root);
            Assert.Contains(diagnostics, diagnostic => diagnostic.Code == "CUIR071");
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task RandomOnce_missing_binding_context_fails_strictly()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        await session.Dispatch<bool>(async () =>
        {
            using var loader = new CuiControlLoader();
            loader.SetSurface("Imagine");
            loader.SetAppearance(CuiAppearance.Dark);

            var (root, diagnostics) = loader.LoadMarkup(Markup);
            Assert.Null(root);
            Assert.Contains(diagnostics, diagnostic => diagnostic.Code == "CUIR071");
            return true;
        }, CancellationToken.None);
    }

    private static void Pump() => Dispatcher.UIThread.RunJobs();
}
