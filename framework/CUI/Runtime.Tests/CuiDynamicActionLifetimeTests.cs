using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Headless;
using CakeOS.Cui.Language;
using CakeOS.Cui.Runtime;
using Xunit;

namespace CakeOS.Cui.Runtime.Tests;

[Collection("CuiNativeBackend")]
public sealed class CuiDynamicActionLifetimeTests
{
    [Fact]
    public async Task Removed_repeat_button_cannot_dispatch_and_new_item_is_wired_once()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiRuntimeTestApplication));
        await session.Dispatch(async () =>
        {
            var items = new ObservableCollection<Dictionary<string, object?>> { new() { ["ID"] = "first", ["Label"] = "First" } };
            var model = new CuiViewModel(); model.Set("Items", items);
            var calls = 0; model.On("Delete", _ => calls++);
            using var host = new CuiSceneHost();
            var scene = new CuiNativeScene("fixture", "Fixture", "Home", new CuiRichParser().Parse(
                "<Cui><StackPanel><Repeat Source=\"{Binding Items}\" As=\"item\" Key=\"{Binding item.ID}\"><Button action=\"Delete\" content=\"{Binding item.Label}\" /></Repeat></StackPanel></Cui>"), model, model, new Ready());
            await host.ShowAsync(scene);
            var root = Assert.IsType<StackPanel>(host.Content);
            var repeat = Assert.IsType<StackPanel>(Assert.Single(root.Children));
            var removed = Assert.IsType<Button>(Assert.Single(Assert.IsType<Panel>(Assert.Single(repeat.Children)).Children));
            removed.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(1, calls);
            items.Clear();
            Assert.Empty(repeat.Children);
            removed.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(1, calls);
            items.Add(new() { ["ID"] = "next", ["Label"] = "Next" });
            var added = Assert.IsType<Button>(Assert.Single(Assert.IsType<Panel>(Assert.Single(repeat.Children)).Children));
            added.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(2, calls);
            return true;
        }, default);
    }

    [Fact]
    public async Task Removed_conditional_branch_cannot_dispatch_and_new_branch_is_wired_once()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiRuntimeTestApplication));
        await session.Dispatch(async () =>
        {
            var model = new CuiViewModel(); model.Set("Active", true);
            var calls = 0; model.On("Delete", _ => calls++);
            using var host = new CuiSceneHost();
            var scene = new CuiNativeScene("fixture", "Fixture", "Home", new CuiRichParser().Parse(
                "<Cui><StackPanel><If Condition=\"{Binding Active}\"><Button action=\"Delete\" content=\"Delete\" /></If><Else><TextBlock text=\"Unavailable\" /></Else></StackPanel></Cui>"), model, model, new Ready());
            await host.ShowAsync(scene);
            var root = Assert.IsType<StackPanel>(host.Content);
            var conditional = Assert.IsType<ContentControl>(Assert.Single(root.Children));
            var removed = Assert.IsType<Button>(Assert.Single(Assert.IsType<Panel>(conditional.Content).Children));
            removed.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(1, calls);
            model.Set("Active", false);
            removed.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(1, calls);
            model.Set("Active", true);
            var added = Assert.IsType<Button>(Assert.Single(Assert.IsType<Panel>(conditional.Content).Children));
            added.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(2, calls);
            return true;
        }, default);
    }
    [Fact]
    public async Task Failed_action_has_safe_visible_status_without_private_exception_text()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiRuntimeTestApplication));
        await session.Dispatch(async () =>
        {
            var model = new CuiViewModel();
            model.On("Fail", _ => throw new InvalidOperationException("private-account-secret"));
            using var host = new CuiSceneHost();
            await host.ShowAsync(new("fixture", "Fixture", "Home", new CuiRichParser().Parse(
                "<Cui><Button action=\"Fail\" content=\"Run\" /></Cui>"), model, model, new Ready()));
            var button = Assert.IsType<Button>(host.Content);
            button.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Assert.Equal("CUIA_FAILED", host.LastActionFailure?.Code);
            var layout = Assert.IsType<Grid>(host.Content);
            var banner = Assert.IsType<TextBlock>(layout.Children[0]);
            Assert.Contains("could not complete", banner.Text);
            Assert.DoesNotContain("private-account-secret", banner.Text);
            Assert.All(host.Diagnostics, item => Assert.DoesNotContain("private-account-secret", item.Message));
            return true;
        }, default);
    }

    [Fact]
    public async Task Disposed_scene_cancels_inflight_action_and_retained_button_cannot_dispatch()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiRuntimeTestApplication));
        await session.Dispatch(async () =>
        {
            var dispatcher = new WaitingAction();
            var host = new CuiSceneHost();
            await host.ShowAsync(new("fixture", "Fixture", "Home", new CuiRichParser().Parse(
                "<Cui><Button action=\"Wait\" content=\"Run\" /></Cui>"), new CuiViewModel(), dispatcher, new Ready()));
            var button = Assert.IsType<Button>(host.Content);
            button.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            await dispatcher.Entered.Task;
            host.Dispose();
            await dispatcher.Cancelled.Task;
            button.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(1, dispatcher.Calls);
            Assert.Null(host.LastActionFailure);
            return true;
        }, default);
    }

    private sealed class WaitingAction : CakeOS.Cui.ICuiActionDispatcher, CakeOS.Cui.ICuiActionAvailability
    {
        public int Calls { get; private set; }
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Cancelled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool? IsActionAvailable(string command) => true;
        public async ValueTask DispatchAsync(string command, object? parameter, CancellationToken cancellationToken = default)
        {
            Calls++; Entered.TrySetResult();
            try { await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken); }
            catch (OperationCanceledException) { Cancelled.TrySetResult(); throw; }
        }
    }

    private sealed class Ready : ICuiSceneReadiness
    {
        public ValueTask<CuiSceneAvailability> CheckAsync(CancellationToken ct) => ValueTask.FromResult(new CuiSceneAvailability(CuiSceneAvailabilityState.Ready, "ready", "Ready"));
    }
}
