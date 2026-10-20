using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CakeOS.Cui.Themes;
using System.Threading;
using Xunit;

namespace CakeOS.Cui.Runtime.Tests;

/// <summary>
/// Lane-3 coverage for the CUI popup backing fix. The donor PopupRoot default white Background and
/// the TopLevel white TransparencyBackgroundFallback painted an opaque white rectangle behind native
/// popups. CUI now owns both layers: a transparent popup-root backing (rounded surface + shadow
/// margin stay transparent), a distinct guaranteed-Opaque semantic fallback brush (never the material
/// surface, whose ISolidColorBrush.Color is Transparent), and an alpha-aware projection that keeps
/// the fallback unpainted through theme changes whenever real per-pixel alpha was negotiated. These
/// tests assert the host-matrix policy, the alpha-aware paint projection, and the rendered popup-root
/// template. Execution is slot-gated / NOT RUN here.
/// </summary>
public sealed class CuiPopupBackingTests
{
    [Theory]
    [InlineData(CuiPopupPresentation.AlphaCapability.Available, CuiPopupPresentation.Airspace.Clear, false)]
    [InlineData(CuiPopupPresentation.AlphaCapability.Available, CuiPopupPresentation.Airspace.Present, false)]
    [InlineData(CuiPopupPresentation.AlphaCapability.Available, CuiPopupPresentation.Airspace.Unknown, false)]
    [InlineData(CuiPopupPresentation.AlphaCapability.Unavailable, CuiPopupPresentation.Airspace.Clear, true)]
    [InlineData(CuiPopupPresentation.AlphaCapability.Unavailable, CuiPopupPresentation.Airspace.Present, false)]
    [InlineData(CuiPopupPresentation.AlphaCapability.Unavailable, CuiPopupPresentation.Airspace.Unknown, false)]
    [InlineData(CuiPopupPresentation.AlphaCapability.Unknown, CuiPopupPresentation.Airspace.Clear, false)]
    [InlineData(CuiPopupPresentation.AlphaCapability.Unknown, CuiPopupPresentation.Airspace.Present, false)]
    [InlineData(CuiPopupPresentation.AlphaCapability.Unknown, CuiPopupPresentation.Airspace.Unknown, false)]
    public void Host_policy_matrix_retains_native_except_verified_clear_overlay(
        CuiPopupPresentation.AlphaCapability alpha, CuiPopupPresentation.Airspace airspace, bool expected)
    {
        Assert.Equal(expected, CuiPopupPresentation.PreferOverlayHost(alpha, airspace));
    }

    [Fact]
    public void Unknown_signals_never_infer_overlay_safety()
    {
        Assert.False(CuiPopupPresentation.PreferOverlayHostUnknown());
    }

    [Theory]
    [InlineData(nameof(WindowTransparencyLevel.Transparent))]
    [InlineData(nameof(WindowTransparencyLevel.Blur))]
    [InlineData(nameof(WindowTransparencyLevel.AcrylicBlur))]
    [InlineData(nameof(WindowTransparencyLevel.Mica))]
    public void Alpha_capable_destinations_keep_fallback_null(string levelName)
    {
        var level = levelName switch
        {
            nameof(WindowTransparencyLevel.Transparent) => WindowTransparencyLevel.Transparent,
            nameof(WindowTransparencyLevel.Blur) => WindowTransparencyLevel.Blur,
            nameof(WindowTransparencyLevel.AcrylicBlur) => WindowTransparencyLevel.AcrylicBlur,
            nameof(WindowTransparencyLevel.Mica) => WindowTransparencyLevel.Mica,
            _ => throw new ArgumentOutOfRangeException(nameof(levelName)),
        };
        var opaque = new SolidColorBrush(Colors.DarkSlateGray);
        Assert.Null(CuiPopupPresentation.ResolveOpaquePaint(level, opaque));
    }

    [Fact]
    public void Opaque_destination_paints_latest_guaranteed_opaque_color()
    {
        var opaque = new SolidColorBrush(Colors.DarkSlateGray);
        Assert.Same(opaque, CuiPopupPresentation.ResolveOpaquePaint(WindowTransparencyLevel.None, opaque));
    }

    [Fact]
    public void Non_opaque_levels_never_paint_regardless_of_brush_emission()
    {
        // A non-None level (including Transparent trust alpha-capable surfaces) must keep the
        // fallback null even when the opaque brush is re-emitted by a later theme change: opacity is
        // never inferred from a level that is not positively None.
        var opaque = new SolidColorBrush(Colors.DarkSlateGray);
        Assert.Null(CuiPopupPresentation.ResolveOpaquePaint(WindowTransparencyLevel.Transparent, opaque));
        Assert.Null(CuiPopupPresentation.ResolveOpaquePaint(WindowTransparencyLevel.Blur, opaque));
        Assert.Null(CuiPopupPresentation.ResolveOpaquePaint(WindowTransparencyLevel.AcrylicBlur, opaque));
        Assert.Null(CuiPopupPresentation.ResolveOpaquePaint(WindowTransparencyLevel.Mica, opaque));
    }

    [Fact]
    public void Fallback_paint_is_driven_by_positively_observed_destination_level()
    {
        // The pure projection treats a positively opaque destination as painting the latest opaque
        // semantic brush, and every alpha-capable level as keeping the fallback null (unpainted).
        var opaque = new SolidColorBrush(Colors.DarkSlateGray);
        Assert.Same(opaque, CuiPopupPresentation.ResolveOpaquePaint(WindowTransparencyLevel.None, opaque));
        foreach (var level in new[]
        {
            WindowTransparencyLevel.Transparent, WindowTransparencyLevel.Blur,
            WindowTransparencyLevel.AcrylicBlur, WindowTransparencyLevel.Mica
        })
            Assert.Null(CuiPopupPresentation.ResolveOpaquePaint(level, opaque));
    }

    [Fact]
    public async Task Live_popup_root_fallback_tracks_observed_destination_level_and_resource_changes()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        await session.Dispatch(() =>
        {
            using var loader = new CuiControlLoader(); loader.SetAppearance(CuiAppearance.Dark);
            var loaded = loader.LoadMarkup("""
                <Theme Name="Professional"><Container Type="Menu"><Button Type="Menu Item" Content="File">
                  <Button Type="Menu Item" Content="Open"/>
                </Button></Container></Theme>
                """);
            Assert.Empty(loaded.Diagnostics);
            var window = new Window { Content = loaded.Root, Width = 600, Height = 300 };
            try
            {
                window.Show(); window.UpdateLayout();
                var owner = Assert.Single(loaded.Root!.GetVisualDescendants().OfType<CuiMenuItem>());
                owner.IsSubMenuOpen = true; Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
                var item = owner.Items.OfType<CuiMenuItem>().First();

                var popupRoot = Assert.Single(item.GetVisualAncestors().OfType<PopupRoot>());
                var fallback = Assert.Single(popupRoot.GetVisualDescendants().OfType<Border>(),
                    border => border.Name == "PART_TransparencyFallback");
                var templateRoot = fallback.GetVisualParent();

                // The transparent root frame is the sibling of the fallback in the template root; it
                // must NOT paint the donor white Background (transparent backing keeps round cutouts
                // and shadow margin transparent).
                var frame = Assert.Single(templateRoot!.GetVisualChildren().OfType<CuiMaterialBorder>());
                Assert.Null(frame.Background);

                // The semantic fallback is distinct and guaranteed-opaque (A=255): never the material
                // surface whose ISolidColorBrush.Color is Transparent.
                var fallbackBrush = Assert.IsAssignableFrom<ISolidColorBrush>(popupRoot.TransparencyBackgroundFallback);
                Assert.Equal(255, fallbackBrush.Color.A);
                Assert.False(fallbackBrush.Color.A == 0 || fallbackBrush.Color == Colors.White);

                // Unobserved/unknown: no positive receipt yet, so the ACTUAL fallback border stays
                // unpainted regardless of the brush resource.
                Assert.Null(fallback.Background);

                // Positive opaque receipt paints the latest guaranteed-opaque semantic color on the
                // actual fallback border. Report through the real ITopLevelImpl.TransparencyLevelChanged
                // delegate, not a manual receipt injection.
                popupRoot.PlatformImpl?.TransparencyLevelChanged?.Invoke(WindowTransparencyLevel.None);
                Assert.NotNull(fallback.Background);
                var painted = Assert.IsAssignableFrom<ISolidColorBrush>(fallback.Background!);
                Assert.Equal(255, painted.Color.A);

                // A later transparent negotiation clears the fallback (never repaints through).
                popupRoot.PlatformImpl?.TransparencyLevelChanged?.Invoke(WindowTransparencyLevel.Transparent);
                Assert.Null(fallback.Background);

                // Close/reopen: the fresh popup root starts unobserved (unknown) again, and the prior
                // projection is detached with the closed popup.
                owner.IsSubMenuOpen = false; Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
                owner.IsSubMenuOpen = true; Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
                var reopenedRoot = Assert.Single(item.GetVisualAncestors().OfType<PopupRoot>());
                var reopenedFallback = Assert.Single(reopenedRoot.GetVisualDescendants().OfType<Border>(),
                    border => border.Name == "PART_TransparencyFallback");
                Assert.Null(reopenedFallback.Background);
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Actual_negotiation_callback_drives_the_projection_including_unchanged_none_to_none()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        await session.Dispatch(() =>
        {
            using var loader = new CuiControlLoader(); loader.SetAppearance(CuiAppearance.Dark);
            var loaded = loader.LoadMarkup("""
                <Theme Name="Professional"><Container Type="Menu"><Button Type="Menu Item" Content="File">
                  <Button Type="Menu Item" Content="Open"/>
                </Button></Container></Theme>
                """);
            Assert.Empty(loaded.Diagnostics);
            var window = new Window { Content = loaded.Root, Width = 600, Height = 300 };
            try
            {
                window.Show(); window.UpdateLayout();
                var owner = Assert.Single(loaded.Root!.GetVisualDescendants().OfType<CuiMenuItem>());
                owner.IsSubMenuOpen = true; Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
                var item = owner.Items.OfType<CuiMenuItem>().First();

                var popupRoot = Assert.Single(item.GetVisualAncestors().OfType<PopupRoot>());
                var fallback = Assert.Single(popupRoot.GetVisualDescendants().OfType<Border>(),
                    border => border.Name == "PART_TransparencyFallback");

                // Drive the ACTUAL platform negotiation callback delegate — ITopLevelImpl.
                // TransparencyLevelChanged, which TopLevel wires to HandleTransparencyLevelChanged — so
                // the real report pipeline (including unchanged re-reports) is exercised, not a private
                // method invocation.
                void Report(WindowTransparencyLevel level) =>
                    popupRoot.PlatformImpl?.TransparencyLevelChanged?.Invoke(level);

                // A genuine first None report paints the latest guaranteed-opaque semantic color.
                Report(WindowTransparencyLevel.None);
                Assert.NotNull(fallback.Background);
                var painted = Assert.IsAssignableFrom<ISolidColorBrush>(fallback.Background!);
                Assert.Equal(255, painted.Color.A);

                // An UNCHANGED None→None report must still be observed (the old projection only saw
                // property changes and missed it); the fallback stays painted.
                Report(WindowTransparencyLevel.None);
                Assert.NotNull(fallback.Background);

                // A negotiated-through surface keeps the fallback null.
                Report(WindowTransparencyLevel.Transparent);
                Assert.Null(fallback.Background);
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Request_transparency_applies_to_each_fresh_destination_before_show()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        await session.Dispatch(() =>
        {
            using var loader = new CuiControlLoader(); loader.SetAppearance(CuiAppearance.Dark);
            var loaded = loader.LoadMarkup("""
                <Theme Name="Professional"><Container Type="Vertical Stack">
                  <Input Type="Select"><Text>Alpha</Text><Text>Beta</Text></Input>
                </Container></Theme>
                """);
            Assert.Empty(loaded.Diagnostics);
            var window = new Window { Content = loaded.Root, Width = 500, Height = 300 };
            try
            {
                window.Show(); window.UpdateLayout();
                var combo = Assert.Single(loaded.Root!.GetVisualDescendants().OfType<ComboBox>());

                // First open: the real Select producer requests transparency on the fresh destination.
                combo.IsDropDownOpen = true; Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
                var firstRoot = PopupRootOf(combo);
                Assert.Contains(WindowTransparencyLevel.Transparent, firstRoot.TransparencyLevelHint);

                // Reopen: a FRESH destination is created and receives its own request (the request is
                // applied before Show, never only on the first open).
                combo.IsDropDownOpen = false; Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
                combo.IsDropDownOpen = true; Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
                var secondRoot = PopupRootOf(combo);
                Assert.NotSame(firstRoot, secondRoot);
                Assert.Contains(WindowTransparencyLevel.Transparent, secondRoot.TransparencyLevelHint);
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Later_resource_emission_never_clears_a_negotiated_opaque_fallback_nor_paints_alpha_capable()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        await session.Dispatch(() =>
        {
            using var loader = new CuiControlLoader(); loader.SetAppearance(CuiAppearance.Dark);
            var loaded = loader.LoadMarkup("""
                <Theme Name="Professional"><Container Type="Menu"><Button Type="Menu Item" Content="File">
                  <Button Type="Menu Item" Content="Open"/>
                </Button></Container></Theme>
                """);
            Assert.Empty(loaded.Diagnostics);
            var window = new Window { Content = loaded.Root, Width = 600, Height = 300 };
            try
            {
                window.Show(); window.UpdateLayout();
                var owner = Assert.Single(loaded.Root!.GetVisualDescendants().OfType<CuiMenuItem>());
                owner.IsSubMenuOpen = true; Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
                var item = owner.Items.OfType<CuiMenuItem>().First();
                var popupRoot = Assert.Single(item.GetVisualAncestors().OfType<PopupRoot>());
                var fallback = Assert.Single(popupRoot.GetVisualDescendants().OfType<Border>(),
                    border => border.Name == "PART_TransparencyFallback");

                // Drive real platform negotiation reports through ITopLevelImpl.TransparencyLevelChanged.
                void Report(WindowTransparencyLevel level) =>
                    popupRoot.PlatformImpl?.TransparencyLevelChanged?.Invoke(level);

                // Opaque negotiated → painted. A later opaque-brush re-emission must NOT clear it
                // (the causal regression: an unobserved projection cleared the fallback on resource
                // emission). Re-emitting a NEW opaque brush paints the LATEST exact color (not merely
                // any A=255 value).
                Report(WindowTransparencyLevel.None);
                Assert.NotNull(fallback.Background);
                var firstColor = new SolidColorBrush(Colors.DarkSlateGray);
                popupRoot.Resources["CuiPopupOpaqueFallbackBrush"] = firstColor;
                Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
                Assert.NotNull(fallback.Background);
                Assert.Same(firstColor, Assert.IsAssignableFrom<ISolidColorBrush>(fallback.Background!));

                // Re-emit a DIFFERENT latest opaque color; the painted fallback must now be that exact
                // latest color (the projection reads the live resource, not a once-captured value).
                var latestColor = new SolidColorBrush(Color.FromArgb(255, 23, 142, 97));
                popupRoot.Resources["CuiPopupOpaqueFallbackBrush"] = latestColor;
                Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
                Assert.Same(latestColor, Assert.IsAssignableFrom<ISolidColorBrush>(fallback.Background!));

                // Transparent negotiated → null; a later opaque-brush emission must still keep it null.
                Report(WindowTransparencyLevel.Transparent);
                Assert.Null(fallback.Background);
                popupRoot.Resources["CuiPopupOpaqueFallbackBrush"] = new SolidColorBrush(Colors.DarkSlateGray);
                Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
                Assert.Null(fallback.Background);
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Authored_context_menu_popup_routes_through_the_transparency_seam_per_open()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        await session.Dispatch(() =>
        {
            using var loader = new CuiControlLoader(); loader.SetAppearance(CuiAppearance.Dark);
            var loaded = loader.LoadMarkup("""
                <Cui><Container Type="Page"><Button Content="Owner"><Container Type="Context Menu">
                  <Button Type="Menu Item" Content="Open"/>
                </Container></Button></Container></Cui>
                """);
            Assert.Empty(loaded.Diagnostics);
            var window = new Window { Content = loaded.Root, Width = 600, Height = 300 };
            try
            {
                window.Show(); window.UpdateLayout();
                var owner = Assert.Single(loaded.Root!.GetVisualDescendants().OfType<Button>());
                var menu = Assert.IsType<ContextMenu>(owner.ContextMenu!);
                var item = Assert.IsAssignableFrom<MenuItem>(Assert.Single(menu.Items));

                // First open: the authored ContextMenu's retained popup is configured through the seam.
                menu.Open(owner); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
                var firstRoot = Assert.Single(item.GetVisualAncestors().OfType<PopupRoot>());
                Assert.Contains(WindowTransparencyLevel.Transparent, firstRoot.TransparencyLevelHint);

                // Second open (reopen): a fresh destination receives the request again.
                menu.Close(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
                menu.Open(owner); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
                var secondRoot = Assert.Single(item.GetVisualAncestors().OfType<PopupRoot>());
                Assert.NotSame(firstRoot, secondRoot);
                Assert.Contains(WindowTransparencyLevel.Transparent, secondRoot.TransparencyLevelHint);
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Authored_tooltip_popup_is_configured_before_first_show_via_per_control_path()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        await session.Dispatch(() =>
        {
            using var loader = new CuiControlLoader(); loader.SetAppearance(CuiAppearance.Dark);
            var loaded = loader.LoadMarkup("""
                <Cui><Container Type="Page"><Button Content="Save" tooltip="Save (Ctrl+S)"/>
                <Button Content="Plain"/></Container></Cui>
                """);
            Assert.Empty(loaded.Diagnostics);
            // A control WITHOUT an authored tooltip must NOT be configured (no process-wide leakage).
            var buttons = loaded.Root!.GetVisualDescendants().OfType<Button>().ToArray();
            var authoredButton = Assert.Single(buttons, b => (string?)b.Content == "Save");
            var plainButton = Assert.Single(buttons, b => (string?)b.Content == "Plain");
            Assert.Null(ToolTip.GetConfigurePopup(plainButton));
            // The authored control carries the narrow per-control request path (established by the loader).
            Assert.True(ToolTip.GetConfigurePopup(authoredButton) == CuiPopupPresentation.RequestTransparency,
                "the loader must attach the narrow per-control tooltip request path");

            var window = new Window { Content = loaded.Root, Width = 500, Height = 250 };
            try
            {
                window.Show(); window.UpdateLayout();
                var tooltip = new ToolTip { Content = ToolTip.GetTip(authoredButton) };
                ToolTip.SetTip(authoredButton, tooltip);
                ToolTip.SetIsOpen(authoredButton, true); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();

                // The configured has been carried onto the retained ToolTip instance BEFORE its popup's
                // first show, and the fresh destination received the transparency hint.
                Assert.NotNull(tooltip.ConfigurePopup);
                var root = Assert.Single(tooltip.GetVisualAncestors().OfType<PopupRoot>());
                Assert.Contains(WindowTransparencyLevel.Transparent, root.TransparencyLevelHint);

                ToolTip.SetIsOpen(authoredButton, false); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    private static PopupRoot PopupRootOf(ComboBox combo)
    {
        var popup = Assert.Single(combo.GetVisualDescendants().OfType<Popup>());
        var content = Assert.IsAssignableFrom<Control>(popup.Child);
        return Assert.Single(content.GetVisualAncestors().OfType<PopupRoot>());
    }
}
