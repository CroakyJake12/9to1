using Avalonia.Controls;
using Avalonia.Headless;
using CakeOS.Cui.Language;
using CakeOS.Cui.Runtime;
using Xunit;

namespace CakeOS.Cui.Runtime.Tests;

[CollectionDefinition("CuiNativeBackend", DisableParallelization = true)]
public sealed class CuiNativeBackendCollection { }

[Collection("CuiNativeBackend")]
public sealed class CuiSceneHostTests
{
    [Fact]
    public async Task Real_retained_scene_bindings_actions_and_disposal_run_on_native_headless_backend()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiRuntimeTestApplication));
        await session.Dispatch(async () =>
        {
            var model = new CuiViewModel();
            model.Set("Status", "Before");
            var invoked = false;
            model.On("Save", _ => invoked = true);
            using var host = new CuiSceneHost();
            var scene = new CuiNativeScene("fixture", "Fixture", "Home", new CuiRichParser().Parse(
                "<Cui><StackPanel><TextBlock id=\"status\" text=\"{Binding Status}\" /><Button id=\"save\" action=\"Save\" content=\"Save\" /></StackPanel></Cui>"),
                model, model, new Ready(CuiSceneAvailabilityState.Ready));
            Assert.Equal(CuiSceneAvailabilityState.Ready, (await host.ShowAsync(scene)).State);
            var panel = Assert.IsAssignableFrom<Panel>(host.Content);
            var status = Assert.IsType<TextBlock>(panel.Children[0]);
            Assert.Equal("Before", status.Text);
            model.Set("Status", "After");
            Assert.Equal("After", status.Text);
            var button = Assert.IsType<Button>(panel.Children[1]);
            button.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Assert.True(invoked);
            host.Dispose();
            invoked = false;
            button.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Assert.False(invoked);
            await Assert.ThrowsAsync<ObjectDisposedException>(() => host.ShowAsync(scene));
            return true;
        }, default);
    }

    [Fact]
    public async Task Failed_handshake_shows_real_status_without_mounting_authorised_actions()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiRuntimeTestApplication));
        await session.Dispatch(async () =>
        {
            var model = new CuiViewModel();
            using var host = new CuiSceneHost();
            var scene = new CuiNativeScene("fixture", "Fixture", "Home", new CuiRichParser().Parse(
                "<Cui><Button id=\"private-action\" action=\"Delete\" content=\"Delete\" /></Cui>"),
                model, model, new Ready(CuiSceneAvailabilityState.Unavailable));
            Assert.Equal(CuiSceneAvailabilityState.Unavailable, (await host.ShowAsync(scene)).State);
            var panel = Assert.IsAssignableFrom<Panel>(host.Content);
            var status = Assert.IsType<TextBlock>(Assert.Single(panel.Children));
            Assert.Equal("Required service unavailable", status.Text);
            return true;
        }, default);
    }

    private sealed class Ready(CuiSceneAvailabilityState state) : ICuiSceneReadiness
    {
        public ValueTask<CuiSceneAvailability> CheckAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult(new CuiSceneAvailability(state, "fixture-readiness", "Required service unavailable"));
    }
}
