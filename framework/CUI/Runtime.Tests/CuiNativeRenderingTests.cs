using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.VisualTree;
using CakeOS.Cui.Language;
using CakeOS.Cui.Runtime;
using Xunit;

namespace CakeOS.Cui.Runtime.Tests;

[Collection("CuiNativeBackend")]
public sealed class CuiNativeRenderingTests
{
    [Fact]
    public async Task Native_primitive_templates_render_and_pointer_hit_testing_dispatches_action()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(RenderApplication));
        await session.Dispatch(async () =>
        {
            var model = new CuiViewModel(); var clicks = 0; model.On("Run", _ => clicks++);
            using var host = new CuiSceneHost();
            await host.ShowAsync(new("render", "Render", "Home", new CuiRichParser().Parse(
                "<Cui><StackPanel><Button content=\"Run action\" action=\"Run\" width=\"160\" height=\"44\"/><TextBox text=\"Editable text\" width=\"180\" height=\"44\" /></StackPanel></Cui>"), model, model, new Ready()));
            var window = new Window { Width = 300, Height = 160, Content = host }; window.Show();
            try
            {
                using var frame = window.CaptureRenderedFrame();
                Assert.NotNull(frame); Assert.True(frame.PixelSize.Width > 0);
                var stack = Assert.IsType<StackPanel>(host.Content);
                var button = Assert.IsType<Button>(stack.Children[0]);
                var text = Assert.IsType<TextBox>(stack.Children[1]);
                Assert.NotNull(button.Template); Assert.NotNull(text.Template);
                Assert.Equal("Inter", Avalonia.Media.FontManager.Current.DefaultFontFamily.Name);
                Assert.NotEmpty(button.GetVisualDescendants()); Assert.NotEmpty(text.GetVisualDescendants());
                var point = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), window)!.Value;
                window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left);
                Assert.Equal(1, clicks);
                button.IsEnabled = false;
                window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left);
                Assert.Equal(1, clicks);
            }
            finally { window.Close(); }
            return true;
        }, default);
    }
    public sealed class RenderApplication : Application
    {
        public static AppBuilder BuildAvaloniaApp() => CuiNativeHost.ConfigureFonts(AppBuilder.Configure<RenderApplication>().UseSkia())
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
        public override void Initialize() => CuiNativeHost.InitialisePrimitiveTheme(this);
    }
    private sealed class Ready : ICuiSceneReadiness
    {
        public ValueTask<CuiSceneAvailability> CheckAsync(CancellationToken ct) => ValueTask.FromResult(new CuiSceneAvailability(CuiSceneAvailabilityState.Ready, "ready", "Ready"));
    }
}
