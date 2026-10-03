using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Styling;
using CakeOS.Cui.Language;
using CakeOS.Cui.Runtime;
using CakeOS.Cui.Themes;
using Xunit;
namespace CakeOS.Cui.Runtime.Tests;
[Collection("CuiNativeBackend")]
public sealed class CuiSceneAppearanceTests
{
    [Theory]
    [InlineData("Write", CuiAppearance.Bright)]
    [InlineData("Data", CuiAppearance.SuperDark)]
    public async Task Scene_surface_and_canonical_appearance_reach_native_templates(string surface, CuiAppearance appearance)
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiNativeRenderingTests.RenderApplication));
        await session.Dispatch(async () =>
        {
            using var host = new CuiSceneHost(); var model = new CuiViewModel();
            await host.ShowAsync(new("fixture", "Fixture", surface, new CuiRichParser().Parse(
                "<Cui><StackPanel><Button content=\"Canonical colour\" width=\"160\" height=\"44\" /></StackPanel></Cui>"), model, model, new Ready()) { Appearance = appearance });
            var window = new Window { Width = 300, Height = 160, Content = host }; window.Show();
            try
            {
                using var frame = window.CaptureRenderedFrame(); Assert.NotNull(frame);
                var palette = CuiSurfacePaletteCatalog.For(surface, appearance);
                Assert.Equal(palette.TideBase, Assert.IsType<SolidColorBrush>(host.Background).Color);
                Assert.Equal(CuiSceneVisualResources.Variant(appearance), host.GetValue(ThemeVariantScope.RequestedThemeVariantProperty));
                var button = Assert.IsType<Button>(Assert.Single(Assert.IsType<StackPanel>(host.Content).Children));
                Assert.Equal(palette.Button, Assert.IsType<SolidColorBrush>(button.Background).Color);
            }
            finally { window.Close(); }
            return true;
        }, default);
    }
    private sealed class Ready : ICuiSceneReadiness
    {
        public ValueTask<CuiSceneAvailability> CheckAsync(CancellationToken ct) => ValueTask.FromResult(new CuiSceneAvailability(CuiSceneAvailabilityState.Ready, "ready", "Ready"));
    }
}
