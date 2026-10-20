using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using CakeOS.Cui.Language;
using CakeOS.Cui.Themes;
using System.Threading;
using Xunit;

namespace CakeOS.Cui.Runtime.Tests;

/// <summary>
/// Focused regressions for the pure-local Toggle slice: a document-root bool variable drives a
/// same-scene floating toolbar's IsVisible through a Button, entirely without an external action
/// dispatcher. Also proves per-loader isolation and that local bindings never consult the host
/// binding context (no silent fallback to a same-named host property).
/// </summary>
public sealed class CuiLocalStateRuntimeTests
{
    private const string Markup = """
        <Cui>
          <Variable Type="bool" ID="FormattingOpen">False</Variable>
          <Actions><Action name="toggle-formatting"><Toggle Target="FormattingOpen" /></Action></Actions>
          <Container Type="Vertical Stack">
            <Container id="toolbar" Type="Horizontal Stack" IsVisible="{Binding FormattingOpen}">
              <Text text="toolbar" />
            </Container>
            <Button id="aa" Action="toggle-formatting" Content="Aa" />
          </Container>
        </Cui>
        """;

    [Fact]
    public async Task Local_toggle_button_flips_bound_toolbar_visibility_without_external_dispatcher()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        await session.Dispatch<bool>(async () =>
        {
            using var loader = new CuiControlLoader();
            loader.SetSurface("Imagine");
            loader.SetAppearance(CuiAppearance.Dark);
            // Deliberately no external dispatcher: pure-local toggles must still dispatch.
            var (root, diagnostics) = loader.LoadMarkup(Markup);
            Assert.Empty(diagnostics);
            var surface = Assert.IsType<CuiContainerControl>(root!);
            loader.WireBindings(surface);

            var toolbar = Assert.Single(surface.AuthoredChildren.OfType<CuiContainerControl>(), c => c.Name == "toolbar");
            var aa = Assert.Single(surface.AuthoredChildren.OfType<Button>(), b => b.Name == "aa");

            var window = new Window { Content = root, Width = 500, Height = 320 };
            window.Show(); Pump();
            try
            {
                Assert.False(toolbar.IsVisible, "toolbar must start hidden (False)");
                aa.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await loader.WhenActionsIdleAsync(); Pump();
                Assert.True(toolbar.IsVisible, "toggle must open the toolbar");
                aa.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await loader.WhenActionsIdleAsync(); Pump();
                Assert.False(toolbar.IsVisible, "second toggle must close the toolbar");
            }
            finally { window.Close(); }
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Local_toggle_state_isolates_across_two_loaders()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        await session.Dispatch<bool>(async () =>
        {
            using var loaderA = new CuiControlLoader();
            using var loaderB = new CuiControlLoader();
            loaderA.SetSurface("Imagine"); loaderB.SetSurface("Imagine");
            loaderA.SetAppearance(CuiAppearance.Dark); loaderB.SetAppearance(CuiAppearance.Dark);

            var (rootA, diagA) = loaderA.LoadMarkup(Markup);
            var (rootB, diagB) = loaderB.LoadMarkup(Markup);
            Assert.Empty(diagA); Assert.Empty(diagB);

            var surfaceA = Assert.IsType<CuiContainerControl>(rootA!);
            var surfaceB = Assert.IsType<CuiContainerControl>(rootB!);
            loaderA.WireBindings(surfaceA);
            loaderB.WireBindings(surfaceB);

            var toolbarA = Assert.Single(surfaceA.AuthoredChildren.OfType<CuiContainerControl>(), c => c.Name == "toolbar");
            var aaA = Assert.Single(surfaceA.AuthoredChildren.OfType<Button>(), b => b.Name == "aa");
            var toolbarB = Assert.Single(surfaceB.AuthoredChildren.OfType<CuiContainerControl>(), c => c.Name == "toolbar");
            var aaB = Assert.Single(surfaceB.AuthoredChildren.OfType<Button>(), b => b.Name == "aa");

            var window = new Window { Content = rootA, Width = 500, Height = 320 };
            window.Show(); Pump();
            try
            {
                aaA.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await loaderA.WhenActionsIdleAsync(); Pump();
                Assert.True(toolbarA.IsVisible, "loader A toolbar must open");
                Assert.False(toolbarB.IsVisible, "loader B toolbar must stay closed (isolated state)");

                aaA.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await loaderA.WhenActionsIdleAsync(); Pump();
                Assert.False(toolbarA.IsVisible);

                // Loader B toggles independently.
                aaB.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await loaderB.WhenActionsIdleAsync(); Pump();
                Assert.True(toolbarB.IsVisible);
                Assert.False(toolbarA.IsVisible);
            }
            finally { window.Close(); }
            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Local_binding_never_consults_host_context_with_same_name()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        await session.Dispatch<bool>(async () =>
        {
            var host = new CuiViewModel();
            host.Set("FormattingOpen", true); // host claims true; local False must win
            using var loader = new CuiControlLoader();
            loader.SetSurface("Imagine");
            loader.SetAppearance(CuiAppearance.Dark);
            loader.SetBindingContext(host);
            var (root, diagnostics) = loader.LoadMarkup(Markup);
            Assert.Empty(diagnostics);
            var surface = Assert.IsType<CuiContainerControl>(root!);
            loader.WireBindings(surface);
            var toolbar = Assert.Single(surface.AuthoredChildren.OfType<CuiContainerControl>(), c => c.Name == "toolbar");
            var aa = Assert.Single(surface.AuthoredChildren.OfType<Button>(), b => b.Name == "aa");

            var window = new Window { Content = root, Width = 500, Height = 320 };
            window.Show(); Pump();
            try
            {
                // The document-root local False must mask the host's true.
                Assert.False(toolbar.IsVisible);
                aa.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await loader.WhenActionsIdleAsync(); Pump();
                Assert.True(toolbar.IsVisible);
            }
            finally { window.Close(); }
            return true;
        }, CancellationToken.None);
    }

    private static void Pump() => Dispatcher.UIThread.RunJobs();
}
