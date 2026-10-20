using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CakeOS.Cui.Language;
using CakeOS.Cui.Themes;
using System.Threading;
using Xunit;

namespace CakeOS.Cui.Runtime.Tests;

/// <summary>
/// Focused regressions for the visible composer checkpoint: canonical Aa menu (click-open,
/// scope-preserving compound focus, genuine command dispatch), embedded-input no-inner-adorner
/// with outer compound focus, root backdrop normalization, and menu lifecycle cleanup. Every
/// headless body awaits the real Dispatch Task so assertion errors actually surface.
/// </summary>
public sealed class CuiCompoundComposerTests
{
    [Theory]
    [InlineData("owns-backdrop=\"true\"", true)]
    [InlineData("owns-backdrop=\"false\"", false)]
    [InlineData("", false)]
    public void Root_owns_backdrop_normalizes_declared_boolean_only(string attribute, bool expected)
    {
        using var loader = new CuiControlLoader();
        var (root, diagnostics) = loader.LoadMarkup($"<Container Type=\"Vertical Stack\" {attribute}><Text text=\"label\" /></Container>");
        Assert.Empty(diagnostics);
        Assert.NotNull(root);
        Assert.Equal(expected, loader.RootOwnsBackdrop);
    }

    [Fact]
    public void Root_owns_backdrop_rejects_a_malformed_literal()
    {
        using var loader = new CuiControlLoader();
        var (root, diagnostics) = loader.LoadMarkup(
            "<Container Type=\"Vertical Stack\" owns-backdrop=\"garbage\"><Text text=\"label\" /></Container>");
        Assert.Null(root);
        Assert.Contains(diagnostics, diagnostic => diagnostic.Severity == CuiDiagnosticSeverity.Error);
    }

    [Fact]
    public void Root_owns_backdrop_rejects_a_bound_value_even_when_it_resolves_true()
    {
        var bindings = new CuiViewModel();
        bindings.Set("Backdrop", true);
        using var loader = new CuiControlLoader();
        loader.SetBindingContext(bindings);
        var (root, diagnostics) = loader.LoadMarkup(
            "<Container Type=\"Vertical Stack\" owns-backdrop=\"{Binding Backdrop}\"><Text text=\"label\" /></Container>");
        // A bound value is not a declared literal boolean; a static bool contract must reject it
        // rather than silently normalizing to false.
        Assert.Null(root);
        Assert.Contains(diagnostics, diagnostic => diagnostic.Severity == CuiDiagnosticSeverity.Error);
    }

    [Fact]
    public void Root_owns_backdrop_rejects_nested_placement()
    {
        using var loader = new CuiControlLoader();
        var (root, diagnostics) = loader.LoadMarkup(
            "<Container Type=\"Vertical Stack\"><Container Type=\"Vertical Stack\" owns-backdrop=\"true\" /></Container>");
        // Backdrop ownership belongs to the actual document root, not an arbitrary nested container.
        Assert.Null(root);
        Assert.Contains(diagnostics, diagnostic => diagnostic.Severity == CuiDiagnosticSeverity.Error);
    }

    [Fact]
    public void Invalid_load_does_not_poison_same_loader_valid_root_retry()
    {
        using var loader = new CuiControlLoader();
        // A failed invalid-root load must not leave stale _loadingDepth behind: the SAME loader
        // must still accept a valid root with OwnsBackdrop=true (exception-safe depth reset).
        var (badRoot, badDiagnostics) = loader.LoadMarkup(
            "<Container Type=\"Vertical Stack\" owns-backdrop=\"garbage\"><Text text=\"label\" /></Container>");
        Assert.Null(badRoot);
        Assert.Contains(badDiagnostics, diagnostic => diagnostic.Severity == CuiDiagnosticSeverity.Error);

        var (root, diagnostics) = loader.LoadMarkup(
            "<Container Type=\"Vertical Stack\" owns-backdrop=\"true\"><Text text=\"label\" /></Container>");
        Assert.Empty(diagnostics);
        Assert.NotNull(root);
        Assert.True(loader.RootOwnsBackdrop);
    }

    [Fact]
    public void Pre_retirement_invalid_rejection_is_still_enforced()
    {
        using var loader = new CuiControlLoader();
        var (root, diagnostics) = loader.LoadMarkup(
            "<Container Type=\"Vertical Stack\"><Container Type=\"Vertical Stack\" owns-backdrop=\"true\" /></Container>");
        Assert.Null(root);
        Assert.Contains(diagnostics, diagnostic => diagnostic.Severity == CuiDiagnosticSeverity.Error);
    }

    [Fact]
    public async Task Embedded_input_has_no_inner_focus_adorner_and_outer_compound_ring_holds_focus()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        await session.Dispatch(() =>
        {
            using var loader = new CuiControlLoader();
            loader.SetSurface("Imagine");
            loader.SetAppearance(CuiAppearance.Dark);
            var (root, diagnostics) = loader.LoadMarkup("""
                <Container Type="Vertical Stack" compound-focus="true">
                  <Input Type="Text" embedded-input="true" Value="draft" />
                </Container>
                """);
            Assert.Empty(diagnostics);
            var surface = Assert.IsType<CuiContainerControl>(root);
            var input = Assert.Single(surface.AuthoredChildren.OfType<TextBox>());
            Assert.True(surface.CompoundFocusEnabled);
            // The generic embedded-input owns no inner focus box at all.
            Assert.Null(input.GetValue(Control.FocusAdornerProperty));
            // Unfocused: the outer ring is transparent.
            Assert.Same(Brushes.Transparent, surface.BorderBrush);
            var window = new Window { Content = root, Width = 500, Height = 320 };
            window.Show(); Pump();
            // Focus is only genuine once the input is actually attached to a visual tree.
            input.Focus(); Pump();
            Assert.True(input.IsKeyboardFocusWithin);
            // Focused: the outer compound ring paints a real, theme-owned brush.
            Assert.NotSame(Brushes.Transparent, surface.BorderBrush);
            Assert.NotNull(surface.BorderBrush);
            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Embedded_input_validation_relays_to_the_outer_compound_ring()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        await session.Dispatch(() =>
        {
            using var loader = new CuiControlLoader();
            loader.SetSurface("Imagine");
            loader.SetAppearance(CuiAppearance.Dark);
            var (root, diagnostics) = loader.LoadMarkup("""
                <Container Type="Vertical Stack" compound-focus="true">
                  <Input Type="Text" embedded-input="true" Value="draft" />
                </Container>
                """);
            Assert.Empty(diagnostics);
            var surface = Assert.IsType<CuiContainerControl>(root);
            var input = Assert.Single(surface.AuthoredChildren.OfType<TextBox>());
            var window = new Window { Content = root, Width = 500, Height = 320 };
            window.Show(); Pump();
            // No error and not focused: the outer ring is transparent.
            Assert.Same(Brushes.Transparent, surface.BorderBrush);
            // A truthful descendant validation error must reach the outer compound indication.
            CuiInputValidationProperties.SetHasError(input, true);
            Pump();
            Assert.NotNull(surface.BorderBrush);
            Assert.NotSame(Brushes.Transparent, surface.BorderBrush);
            // Clearing the descendant error restores the transparent rest state.
            CuiInputValidationProperties.SetHasError(input, false);
            Pump();
            Assert.Same(Brushes.Transparent, surface.BorderBrush);
            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Initial_invalid_child_relays_validation_immediately_after_attach()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        await session.Dispatch(() =>
        {
            using var loader = new CuiControlLoader();
            loader.SetSurface("Imagine");
            loader.SetAppearance(CuiAppearance.Dark);
            var (root, diagnostics) = loader.LoadMarkup("""
                <Container Type="Vertical Stack" compound-focus="true">
                  <Input Type="Text" embedded-input="true" Value="draft" />
                </Container>
                """);
            Assert.Empty(diagnostics);
            var surface = Assert.IsType<CuiContainerControl>(root);
            var input = Assert.Single(surface.AuthoredChildren.OfType<TextBox>());
            // Mark invalid BEFORE the scope attaches to a visual tree: recompute must publish immediately.
            CuiInputValidationProperties.SetHasError(input, true);
            var window = new Window { Content = root, Width = 500, Height = 320 };
            window.Show(); Pump();
            Assert.True(surface.CompoundFocusScope!.HasValidationError);
            Assert.NotSame(Brushes.Transparent, surface.BorderBrush);
            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Removing_last_invalid_child_recomputes_validation_immediately()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        await session.Dispatch(() =>
        {
            using var loader = new CuiControlLoader();
            loader.SetSurface("Imagine");
            loader.SetAppearance(CuiAppearance.Dark);
            var (root, diagnostics) = loader.LoadMarkup("""
                <Container Type="Vertical Stack" compound-focus="true">
                  <Input Type="Text" embedded-input="true" Value="draft" />
                </Container>
                """);
            Assert.Empty(diagnostics);
            var surface = Assert.IsType<CuiContainerControl>(root);
            var input = Assert.Single(surface.AuthoredChildren.OfType<TextBox>());
            var window = new Window { Content = root, Width = 500, Height = 320 };
            window.Show(); Pump();
            CuiInputValidationProperties.SetHasError(input, true);
            Pump();
            Assert.True(surface.CompoundFocusScope!.HasValidationError);
            // Removing the last invalid child must recompute to clean immediately.
            surface.RemoveChild(input);
            Pump();
            Assert.False(surface.CompoundFocusScope!.HasValidationError);
            Assert.Same(Brushes.Transparent, surface.BorderBrush);
            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Owned_menu_popup_keeps_compound_scope_and_is_scope_bounded_across_owners()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        await session.Dispatch(() =>
        {
            using var loader = new CuiControlLoader();
            loader.SetSurface("Imagine");
            loader.SetAppearance(CuiAppearance.Dark);
            var (root, diagnostics) = loader.LoadMarkup("""
                <Container Type="Vertical Stack">
                  <Container Type="Vertical Stack" ID="A" compound-focus="true">
                    <Container Type="Menu"><Button Type="Menu Item" ID="aaA" Content="Aa">
                      <Button Type="Menu Item" Content="Bold" Action="composer.format" />
                    </Button></Container>
                    <Input Type="Text" ID="aInput" embedded-input="true" />
                  </Container>
                  <Container Type="Vertical Stack" ID="B" compound-focus="true">
                    <Container Type="Menu"><Button Type="Menu Item" ID="aaB" Content="Aa">
                      <Button Type="Menu Item" Content="Italic" Action="other.format" />
                    </Button></Container>
                    <Input Type="Text" ID="bInput" embedded-input="true" />
                  </Container>
                </Container>
                """);
            Assert.Empty(diagnostics);
            var outer = Assert.IsType<CuiContainerControl>(root);
            var scopeA = Assert.Single(outer.AuthoredChildren.OfType<CuiContainerControl>(), container => container.Name == "A");
            var scopeB = Assert.Single(outer.AuthoredChildren.OfType<CuiContainerControl>(), container => container.Name == "B");
            // Container Type=Menu lowers DIRECTLY to a CuiMenu (not a CuiContainerControl).
            var menuA = Assert.IsType<CuiMenu>(scopeA.AuthoredChildren[0]);
            var topA = Assert.IsType<CuiMenuItem>(menuA.Items[0]);
            var inputA = Assert.Single(scopeA.AuthoredChildren.OfType<TextBox>());
            var window = new Window { Content = root, Width = 500, Height = 360 };
            window.Show(); Pump();
            inputA.Focus(); Pump();
            Assert.True(scopeA.CompoundFocusScope!.IsFocused);
            Assert.False(scopeB.CompoundFocusScope!.IsFocused);
            Click(topA); Pump();
            Assert.True(topA.IsSubMenuOpen);
            // The open popup is owned by scope A but not by scope B: the per-container scope keeps
            // focus only when its OWN descendant menu holds it. No global menu registry is involved.
            Assert.True(scopeA.CompoundFocusScope!.IsFocused);
            Assert.False(scopeB.CompoundFocusScope!.IsFocused);
            // Escape dismisses the popup and returns focus to the editor, keeping the scope focused.
            PressKey(window, Key.Escape); Pump();
            Assert.False(topA.IsSubMenuOpen);
            Assert.True(scopeA.CompoundFocusScope!.IsFocused);
            Assert.False(scopeB.CompoundFocusScope!.IsFocused);
            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Still_open_owned_popup_does_not_keep_scope_focused_after_focus_moves_outside()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        await session.Dispatch(() =>
        {
            using var loader = new CuiControlLoader();
            loader.SetSurface("Imagine");
            loader.SetAppearance(CuiAppearance.Dark);
            var (root, diagnostics) = loader.LoadMarkup("""
                <Container Type="Vertical Stack">
                  <Container Type="Vertical Stack" ID="A" compound-focus="true">
                    <Container Type="Menu"><Button Type="Menu Item" ID="aaA" Content="Aa">
                      <Button Type="Menu Item" Content="Bold" Action="composer.format" />
                    </Button></Container>
                    <Input Type="Text" embedded-input="true" />
                  </Container>
                  <Input Type="Text" ID="unrelated" />
                </Container>
                """);
            Assert.Empty(diagnostics);
            var outer = Assert.IsType<CuiContainerControl>(root);
            var scopeA = Assert.Single(outer.AuthoredChildren.OfType<CuiContainerControl>(), container => container.Name == "A");
            var menuA = Assert.IsType<CuiMenu>(scopeA.AuthoredChildren[0]);
            var topA = Assert.IsType<CuiMenuItem>(menuA.Items[0]);
            var inputA = Assert.Single(scopeA.AuthoredChildren.OfType<TextBox>());
            var unrelated = Assert.Single(outer.AuthoredChildren.OfType<TextBox>(), box => box.Name == "unrelated");
            var window = new Window { Content = root, Width = 500, Height = 360 };
            window.Show(); Pump();
            inputA.Focus(); Pump();
            Assert.True(scopeA.CompoundFocusScope!.IsFocused);
            topA.Open(); Pump();
            Assert.True(topA.IsSubMenuOpen);
            Assert.True(scopeA.CompoundFocusScope!.IsFocused);
            // While the popup remains open, focus genuinely moves to an unrelated field.
            unrelated.Focus(); Pump();
            Assert.False(scopeA.CompoundFocusScope!.IsFocused);
            Assert.True(topA.IsSubMenuOpen); // the popup is still open
            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task A_a_menu_dispatch_sends_a_genuine_command_without_replacing_the_input()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        await session.Dispatch(() =>
        {
            var dispatcher = new RecordingDispatcher();
            using var loader = new CuiControlLoader();
            loader.SetSurface("Imagine");
            loader.SetAppearance(CuiAppearance.Dark);
            loader.SetActionDispatcher(dispatcher);
            var (root, diagnostics) = loader.LoadMarkup("""
                <Container Type="Vertical Stack" compound-focus="true">
                  <Container Type="Menu"><Button Type="Menu Item" Content="Aa">
                    <Button Type="Menu Item" Content="Bold" Action="composer.format" />
                  </Button></Container>
                  <Input Type="Text" embedded-input="true" Value="keep me" />
                </Container>
                """);
            Assert.Empty(diagnostics);
            var surface = Assert.IsType<CuiContainerControl>(root);
            loader.WireBindings(surface);
            var menu = Assert.IsType<CuiMenu>(surface.AuthoredChildren[0]);
            var top = Assert.IsType<CuiMenuItem>(menu.Items[0]);
            var leaf = Assert.IsType<CuiMenuItem>(top.Items[0]);
            var input = Assert.Single(surface.AuthoredChildren.OfType<TextBox>());
            var window = new Window { Content = root, Width = 500, Height = 320 };
            window.Show(); Pump();
            input.Focus(); Pump();
            top.Open(); Pump();
            Assert.True(top.IsSubMenuOpen);
            // A genuine command via the real Click pipeline on the owned toolbar leaf (the popup
            // content is a separately hosted surface whose pointer hit-testing is unreliable here,
            // so dispatch through the authored leaf's own routed click).
            leaf.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Pump();
            Assert.Contains(dispatcher.Calls, call => call.Command == "composer.format");
            Assert.Equal("keep me", input.Text);
            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Open_menu_scope_is_cleared_when_the_loader_tears_down()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        await session.Dispatch(() =>
        {
            var loader = new CuiControlLoader();
            loader.SetSurface("Imagine");
            loader.SetAppearance(CuiAppearance.Dark);
            var (root, diagnostics) = loader.LoadMarkup("""
                <Container Type="Vertical Stack" compound-focus="true">
                  <Container Type="Menu"><Button Type="Menu Item" Content="Aa">
                    <Button Type="Menu Item" Content="Bold" Action="composer.format" />
                  </Button></Container>
                  <Input Type="Text" embedded-input="true" />
                </Container>
                """);
            Assert.Empty(diagnostics);
            var surface = Assert.IsType<CuiContainerControl>(root);
            var scope = surface.CompoundFocusScope!;
            var menu = Assert.IsType<CuiMenu>(surface.AuthoredChildren[0]);
            var top = Assert.IsType<CuiMenuItem>(menu.Items[0]);
            var input = Assert.Single(surface.AuthoredChildren.OfType<TextBox>());
            var window = new Window { Content = root, Width = 500, Height = 320 };
            window.Show(); Pump();
            input.Focus(); Pump();
            Assert.True(scope.IsFocused);
            top.Open(); Pump();
            Assert.True(top.IsSubMenuOpen);
            Assert.True(scope.IsFocused);
            loader.Dispose(); Pump();
            Assert.False(scope.IsFocused);
            window.Close();
            loader.Dispose();
        }, CancellationToken.None);
    }

    private static void Click(Control control)
    {
        var root = TopLevel.GetTopLevel(control)
            ?? throw new InvalidOperationException($"Click requires an attached control; none for {control.GetType().Name}");
        root.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        // Commit/capture the SAME actual TopLevel before hit testing: with the Skia compositing host,
        // InputHitTest consults committed presentation readback, so a rendered frame must exist first.
        using (var frame = root.CaptureRenderedFrame())
        {
            Assert.NotNull(frame);
        }
        var point = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), root)!.Value;
        var hit = root.InputHitTest(point) as Visual;
        if (!(ReferenceEquals(hit, control) || hit?.GetVisualAncestors().Contains(control) == true))
        {
            var encountered = hit is null ? "<null>" : $"{hit.GetType().Name} name={hit.Name}";
            throw new InvalidOperationException($"Click hit mismatch for {control.GetType().Name} name={control.Name} bounds={control.Bounds} point={point} hit={encountered}");
        }
        root.MouseMove(point); root.MouseDown(point, MouseButton.Left); root.MouseUp(point, MouseButton.Left);
        Pump();
    }

    private static void PressKey(Window window, Key key)
    {
        window.KeyPress(key, RawInputModifiers.None, PhysicalKey.None, null);
        window.KeyRelease(key, RawInputModifiers.None, PhysicalKey.None, null);
    }

    private static void Pump()
    {
        Dispatcher.UIThread.RunJobs();
    }

    private sealed class RecordingDispatcher : ICuiActionDispatcher
    {
        public List<(string Command, object? Parameter)> Calls { get; } = [];
        public ValueTask DispatchAsync(string command, object? parameter, CancellationToken cancellationToken = default)
        {
            Calls.Add((command, parameter));
            return ValueTask.CompletedTask;
        }
    }
}
