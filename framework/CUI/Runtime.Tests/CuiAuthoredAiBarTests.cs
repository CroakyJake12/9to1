using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using CakeOS.Cui.Language;
using CakeOS.Cui.Runtime;
using NineToOne.Cui.AI;
using Xunit;
namespace CakeOS.Cui.Runtime.Tests;
[Collection("CuiNativeBackend")]
public sealed class CuiAuthoredAiBarTests
{
    [Fact]
    public async Task Packaged_authored_bar_renders_and_submit_uses_host_dispatcher()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiNativeRenderingTests.RenderApplication));
        await session.Dispatch(async () =>
        {
            var model = new CuiViewModel();
            model.Set("AccessModeLabel", "Read-only"); model.Set("ModelPickerLabel", "Local model"); model.Set("ModelPickerAvailable", true);
            model.Set("IsCollapsed", false); model.Set("IsExpanded", true); model.Set("IsStreaming", false); model.Set("HasError", false);
            model.Set("ContextLabel", "Current document"); model.Set("Prompt", "Summarise"); model.Set("RequestStateLabel", "Ready");
            model.Set("Response", ""); model.Set("Error", ""); var submissions = 0; model.On("Submit", _ => submissions++);
            using var host = new CuiSceneHost();
            await host.ShowAsync(new("fixture", "Fixture", "Home", new CuiRichParser().Parse(FloatingAiBarScene.ReadSource()), model, model, new Ready()));
            var window = new Window { Width = 850, Height = 420, Content = host }; window.Show();
            try
            {
                using var frame = window.CaptureRenderedFrame(); Assert.NotNull(frame);
                var root = Assert.IsType<Border>(host.Content); var panel = Assert.IsType<StackPanel>(root.Child);
                var expanded = Assert.IsType<StackPanel>(panel.Children[1]);
                var prompt = Assert.IsType<TextBox>(expanded.Children[1]); Assert.Equal("Summarise", prompt.Text);
                var send = Assert.IsType<Button>(Assert.IsType<StackPanel>(expanded.Children[3]).Children[0]);
                var point = send.TranslatePoint(new Avalonia.Point(send.Bounds.Width / 2, send.Bounds.Height / 2), window)!.Value;
                window.MouseDown(point, MouseButton.Left); window.MouseUp(point, MouseButton.Left);
                Assert.Equal(1, submissions);
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
