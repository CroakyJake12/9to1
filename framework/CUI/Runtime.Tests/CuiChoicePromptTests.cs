using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Xunit;

namespace CakeOS.Cui.Runtime.Tests;

public sealed class CuiChoicePromptTests
{
    [Fact]
    public async Task Three_authored_choices_render_and_selected_callback_is_awaited_once()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        await session.Dispatch(async () =>
        {
            var source = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var calls = new int[3];
            await using var prompt = new CuiChoicePrompt(Options(
                () => { calls[0]++; return source.Task; },
                () => { calls[1]++; return Task.CompletedTask; },
                () => { calls[2]++; return Task.CompletedTask; }), "Spaces");
            var overlay = new ContentControl();
            var window = new Window { Width = 390, Height = 700, Content = overlay };
            window.Show();
            try
            {
                var completion = prompt.ShowAsync(overlay, CancellationToken.None);
                window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
                var root = Assert.IsAssignableFrom<Control>(overlay.Content);
                Assert.Equal("Read Memory", Find<Button>(root, "choice-primary").Content);
                Assert.Equal("Don't read Memory", Find<Button>(root, "choice-secondary").Content);
                Assert.True(Find<Button>(root, "choice-cancel").IsFocused);
                Find<Button>(root, "choice-primary").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal([1, 0, 0], calls); Assert.False(completion.IsCompleted);
                Find<Button>(root, "choice-secondary").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal([1, 0, 0], calls);
                source.SetResult(); Assert.Equal(CuiChoiceDecision.Primary, await completion);
                var close = prompt.CloseAsync(); await close;
                Assert.Same(close, prompt.OriginalClose); Assert.Null(overlay.Content);
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Escape_invokes_only_cancel_and_restores_the_actual_previous_focus()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        await session.Dispatch(async () =>
        {
            var cancelled = 0;
            await using var prompt = new CuiChoicePrompt(Options(
                () => throw new InvalidOperationException("Primary must not run."),
                () => throw new InvalidOperationException("Secondary must not run."),
                () => { cancelled++; return Task.CompletedTask; }), "Spaces");
            var input = new TextBox { Text = "Preserved draft" };
            var overlay = new ContentControl(); var grid = new Grid(); grid.Children.Add(input); grid.Children.Add(overlay);
            var window = new Window { Width = 800, Height = 600, Content = grid }; window.Show(); input.Focus();
            try
            {
                var completion = prompt.ShowAsync(overlay, CancellationToken.None);
                window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
                window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
                window.KeyRelease(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
                Assert.Equal(CuiChoiceDecision.Cancel, await completion);
                await prompt.CloseAsync();
                Assert.Equal(1, cancelled); Assert.True(input.IsFocused); Assert.Equal("Preserved draft", input.Text);
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Owner_cancellation_before_choice_invokes_cancel_without_loading_an_authorising_choice()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        await session.Dispatch(async () =>
        {
            var calls = new int[3]; using var lifetime = new CancellationTokenSource(); lifetime.Cancel();
            await using var prompt = new CuiChoicePrompt(Options(
                () => { calls[0]++; return Task.CompletedTask; },
                () => { calls[1]++; return Task.CompletedTask; },
                () => { calls[2]++; return Task.CompletedTask; }), "Spaces");
            var overlay = new ContentControl();
            Assert.Equal(CuiChoiceDecision.Cancel, await prompt.ShowAsync(overlay, lifetime.Token));
            await prompt.CloseAsync(); Assert.Equal([0, 0, 1], calls); Assert.Null(overlay.Content);
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Close_joins_accepted_callback_and_never_replaces_it_with_cancel()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        await session.Dispatch(async () =>
        {
            var original = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var cancel = 0;
            var prompt = new CuiChoicePrompt(Options(() => original.Task, () => Task.CompletedTask,
                () => { cancel++; return Task.CompletedTask; }), "Spaces");
            var overlay = new ContentControl(); var window = new Window { Content = overlay }; window.Show();
            try
            {
                var completion = prompt.ShowAsync(overlay, CancellationToken.None);
                var action = prompt.DispatchAsync("Primary", null).AsTask();
                var close = prompt.CloseAsync(); Assert.False(close.IsCompleted);
                original.SetResult(); await action; await close;
                Assert.Equal(CuiChoiceDecision.Primary, await completion); Assert.Equal(0, cancel);
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Empty_overlay_does_not_intercept_actual_pointer_input_before_or_after_choice()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        await session.Dispatch(async () =>
        {
            var input = new TextBox { Width = 220, Height = 50, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top };
            var other = new Button { Content = "Other focus", Width = 220, Height = 50, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Bottom };
            var overlay = new ContentControl(); var grid = new Grid();
            grid.Children.Add(input); grid.Children.Add(other); grid.Children.Add(overlay);
            var window = new Window { Width = 390, Height = 500, Content = grid }; window.Show(); window.UpdateLayout();
            try
            {
                other.Focus(); Click(input); window.KeyTextInput("Before"); Assert.Equal("Before", input.Text);
                await using var prompt = new CuiChoicePrompt(Options(() => Task.CompletedTask, () => Task.CompletedTask, () => Task.CompletedTask), "Spaces");
                var completion = prompt.ShowAsync(overlay, CancellationToken.None);
                window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
                Click(Find<Button>((Control)overlay.Content!, "choice-cancel"));
                Assert.Equal(CuiChoiceDecision.Cancel, await completion); await prompt.CloseAsync();
                other.Focus(); Click(input); window.KeyTextInput("After"); Assert.Contains("After", input.Text);
            }
            finally { window.Close(); }
            void Click(Control target)
            {
                var point = target.TranslatePoint(new Avalonia.Point(target.Bounds.Width / 2, target.Bounds.Height / 2), window)!.Value;
                window.MouseDown(point, Avalonia.Input.MouseButton.Left);
                window.MouseUp(point, Avalonia.Input.MouseButton.Left);
            }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Replacing_parent_overlay_cancels_only_original_prompt_and_preserves_new_owner()
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(CuiBundledFontTestApplication));
        await session.Dispatch(async () =>
        {
            var cancelled = 0;
            await using var prompt = new CuiChoicePrompt(Options(() => Task.CompletedTask, () => Task.CompletedTask,
                () => { cancelled++; return Task.CompletedTask; }), "Spaces");
            var host = new ContentControl(); var window = new Window { Content = host }; window.Show();
            try
            {
                var completion = prompt.ShowAsync(host, CancellationToken.None);
                var replacement = new TextBlock { Text = "Another owner's overlay" };
                host.Content = replacement; Dispatcher.UIThread.RunJobs();
                Assert.Equal(CuiChoiceDecision.Cancel, await completion);
                await prompt.CloseAsync(); Assert.Same(replacement, host.Content); Assert.Equal(1, cancelled);
            }
            finally { window.Close(); }
        }, CancellationToken.None);
    }

    private static T Find<T>(Control root, string name) where T : Control =>
        root.GetVisualDescendants().OfType<T>().Single(control => control.Name == name);
    private static CuiChoicePromptOptions Options(Func<Task> primary, Func<Task> secondary, Func<Task> cancel) =>
        new("Read Memory for this request?", "Temporary chat remains local. Choose whether this request may read permitted Memory.",
            "Read Memory", "Don't read Memory", "Cancel", primary, secondary, cancel);
}
