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
    public void Fallback_through_theme_changes_stays_null_only_when_alpha_negotiated()
    {
        // The projection is deterministic per level: a Transparent level ignores any later brush
        // emission (theme change), while an opaque destination always uses the latest brush.
        var first = new SolidColorBrush(Colors.Black);
        var second = new SolidColorBrush(Colors.White);
        Assert.Null(CuiPopupPresentation.ResolveOpaquePaint(TransparencyLevel.Transparent, first));
        Assert.Null(CuiPopupPresentation.ResolveOpaquePaint(TransparencyLevel.Transparent, second));
        Assert.Same(second, CuiPopupPresentation.ResolveOpaquePaint(TransparencyLevel.None, second));
    }

    [Fact]
    public async Task Native_popup_root_uses_transparent_backing_and_distinct_opaque_fallback()
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
                // The CUI PopupRoot template must be the one applied (it owns PART_TransparencyFallback).
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
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }
}
