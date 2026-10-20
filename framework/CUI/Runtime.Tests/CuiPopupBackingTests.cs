using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CakeOS.Cui.Themes;
using Xunit;

namespace CakeOS.Cui.Runtime.Tests;

/// <summary>
/// Lane-3 coverage for the CUI popup backing fix. The donor PopupRoot default white Background and
/// the TopLevel white TransparencyBackgroundFallback painted an opaque white rectangle behind native
/// popups. CUI now owns both layers: a transparent popup-root backing (rounded surface + shadow
/// margin stay transparent) and a truthful semantic fallback brush. These tests assert the matrix
/// policy and the rendered popup-root template. Execution is slot-gated / NOT RUN here.
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

    [Fact]
    public async Task Native_popup_root_uses_transparent_backing_and_semantic_fallback()
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
                // The semantic fallback replaces the white default: TopLevel will paint this brush
                // only when the compositor cannot negotiate alpha, and clear it otherwise.
                var fallbackBrush = Assert.IsAssignableFrom<ISolidColorBrush>(popupRoot.TransparencyBackgroundFallback);
                Assert.NotEqual(Colors.White, fallbackBrush.Color);
                Assert.True(fallbackBrush.Color.A > 0, "fallback must be a truthful opaque brush when alpha is unavailable");
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }
}
