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
    [InlineData(WindowTransparencyLevel.Transparent)]
    [InlineData(WindowTransparencyLevel.Blur)]
    [InlineData(WindowTransparencyLevel.AcrylicBlur)]
    [InlineData(WindowTransparencyLevel.Mica)]
    public void Alpha_capable_destinations_keep_fallback_null(WindowTransparencyLevel level)
    {
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
                // actual fallback border.
                CuiPopupPresentation.ObserveDestinationAlpha(popupRoot, WindowTransparencyLevel.None);
                Assert.NotNull(fallback.Background);
                var painted = Assert.IsAssignableFrom<ISolidColorBrush>(fallback.Background!);
                Assert.Equal(255, painted.Color.A);

                // A later transparent negotiation clears the fallback (never repaints through).
                CuiPopupPresentation.ObserveDestinationAlpha(popupRoot, WindowTransparencyLevel.Transparent);
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

                // Drive the ACTUAL TopLevel negotiation callback (the private method wired to
                // ITopLevelImpl.TransparencyLevelChanged and OnApplyTemplate), not a manual injection.
                var handle = typeof(TopLevel).GetMethod("HandleTransparencyLevelChanged",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                    ?? throw new InvalidOperationException("HandleTransparencyLevelChanged not found");

                // A genuine first None report paints the latest guaranteed-opaque semantic color.
                handle.Invoke(popupRoot, new object[] { WindowTransparencyLevel.None });
                Assert.NotNull(fallback.Background);
                var painted = Assert.IsAssignableFrom<ISolidColorBrush>(fallback.Background!);
                Assert.Equal(255, painted.Color.A);

                // An UNCHANGED None→None report must still be observed (the old projection only saw
                // property changes and missed it); the fallback stays painted.
                handle.Invoke(popupRoot, new object[] { WindowTransparencyLevel.None });
                Assert.NotNull(fallback.Background);

                // A negotiated-through surface keeps the fallback null.
                handle.Invoke(popupRoot, new object[] { WindowTransparencyLevel.Transparent });
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

                // Opaque negotiated → painted. A later opaque-brush re-emission must NOT clear it
                // (the causal regression: an unobserved projection cleared the fallback on resource
                // emission). Re-emitting a NEW opaque brush keeps the latest guaranteed-opaque color.
                CuiPopupPresentation.ObserveDestinationAlpha(popupRoot, WindowTransparencyLevel.None);
                Assert.NotNull(fallback.Background);
                popupRoot.Resources["CuiPopupOpaqueFallbackBrush"] = new SolidColorBrush(Colors.DarkSlateGray);
                Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
                Assert.NotNull(fallback.Background);
                Assert.Equal(255, Assert.IsAssignableFrom<ISolidColorBrush>(fallback.Background!).Color.A);

                // Transparent negotiated → null; a later opaque-brush emission must still keep it null.
                CuiPopupPresentation.ObserveDestinationAlpha(popupRoot, WindowTransparencyLevel.Transparent);
                Assert.Null(fallback.Background);
                popupRoot.Resources["CuiPopupOpaqueFallbackBrush"] = new SolidColorBrush(Colors.DarkSlateGray);
                Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
                Assert.Null(fallback.Background);
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
