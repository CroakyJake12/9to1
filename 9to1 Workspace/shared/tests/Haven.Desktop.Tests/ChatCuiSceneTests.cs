using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CakeOS.Cui.Runtime;
using CakeOS.Cui.Themes;
using Haven.Core;
using Haven.Desktop.HavenUI.Backend;
using Haven.Desktop.Views.Pages.Chat;

namespace Haven.Desktop.Tests;

/// <summary>Explicit presentation fixtures. Product acceptance separately binds
/// these original callbacks to the actual NewChatPage/SQLite/provider owners.</summary>
public sealed class ChatCuiSceneTests
{
    [AvaloniaFact]
    public async Task Inflight_draft_is_retained_without_any_delayed_send_and_stop_is_an_exact_owner_action()
    {
        var sends = 0; var stops = 0;
        await using var view = new ChatCuiScene(Actions() with
        {
            Send = () => { sends++; return Task.CompletedTask; },
            Stop = () => { stops++; return Task.CompletedTask; }
        }, new HavenAvaloniaNativeControlResolver());
        view.SetSending(true, true); view.Instruction.Text = "Retained draft";
        await view.DispatchAsync("chat.send", null);
        Assert.Equal("Retained draft", view.Instruction.Text); Assert.Equal(0, sends);
        view.SetSending(false, true); Dispatcher.UIThread.RunJobs();
        Assert.Equal(0, sends); Assert.Equal("Retained draft", view.Instruction.Text);
        view.SetSending(true, true); await view.DispatchAsync("chat.stop", null);
        Assert.Equal(1, stops); Assert.Equal("Retained draft", view.Instruction.Text);
    }

    [AvaloniaFact]
    public async Task Real_composer_enter_joins_original_send_and_shift_enter_preserves_draft_editing()
    {
        var original = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var count = 0;
        var view = new ChatCuiScene(Actions() with { Send = () => { count++; return original.Task; } }, new HavenAvaloniaNativeControlResolver());
        var window = new Window { Width = 900, Height = 800, Content = view.Host };
        try
        {
            view.SetSending(false, true); window.Show(); window.UpdateLayout();
            view.Instruction.Control.Focus(); window.KeyTextInput("Actual draft");
            window.KeyPress(Key.Enter, RawInputModifiers.Shift, PhysicalKey.Enter, null);
            window.KeyRelease(Key.Enter, RawInputModifiers.Shift, PhysicalKey.Enter, null);
            Assert.Equal(0, count);
            Assert.Contains("Actual draft", view.Instruction.Text);
            window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            window.KeyRelease(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            Assert.Equal(1, count);
            var close = view.CloseAsync(); Assert.False(close.IsCompleted);
            original.SetResult(); await close;
        }
        finally { original.TrySetResult(); await view.CloseAsync(); window.Content = null; window.Close(); }
    }

    [AvaloniaFact]
    public async Task Keyed_transcript_updates_reuse_markdown_and_dispatch_exact_current_message_action()
    {
        ChatMessageActionRequest? received = null;
        await using var view = new ChatCuiScene(Actions() with { MessageAction = request => { received = request; return Task.CompletedTask; } }, new HavenAvaloniaNativeControlResolver());
        var id = Guid.NewGuid();
        view.SyncMessages([new(id, MessageRole.Assistant, "Before", "Fixture model", false, "")]);
        var window = new Window { Width = 900, Height = 800, Content = view.Host }; window.Show(); window.UpdateLayout();
        try
        {
            var markdown = Assert.Single(view.Root.GetVisualDescendants().OfType<CuiMarkdownView>());
            view.UpdateMessage(new(id, MessageRole.Assistant, "After", "Fixture model", false, ""));
            Assert.Same(markdown, Assert.Single(view.Root.GetVisualDescendants().OfType<CuiMarkdownView>())); Assert.Equal("After", markdown.Text);
            await view.DispatchAsync("chat.message-copy", id.ToString("D"));
            Assert.Equal(id, received!.MessageId); Assert.Equal(ChatMessageAction.Copy, received.Action);
        }
        finally { window.Content = null; window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData("Professional")]
    [InlineData("Glow")]
    public async Task Canonical_workspace_renders_readable_transcript_and_floating_composer_at_desktop_and_compact_sizes(string theme)
    {
        using var context = new CuiThemePreferenceContext(new FixtureTheme(theme));
        await using var view = new ChatCuiScene(Actions(), new HavenAvaloniaNativeControlResolver(), context);
        view.SetConversationHeader("Explicit presentation fixture", "Normal", "Local", "Fixture model");
        view.SetSending(false, true);
        view.SyncMessages([
            new(Guid.NewGuid(), MessageRole.User, "Explain the current source ownership.", "", false, ""),
            new(Guid.NewGuid(), MessageRole.Assistant, "The actual conversation owner retains messages, tools and persistence.\n\nThis is a **presentation fixture**, not a provider response.", "Fixture model", false, "Recorded fixture status")]);
        var window = new Window { Content = view.Host }; window.Show();
        try
        {
            foreach (var width in new[] { 1200, 800, 390 })
            {
                window.Width = width; window.Height = 860; window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
                var input = view.Instruction.Control;
                Assert.True(input.Bounds.Width > 100);
                var point = input.TranslatePoint(new Point(0, 0), window)!.Value;
                Assert.True(point.X >= 0 && point.X + input.Bounds.Width <= width + 1);
                Assert.True(point.Y >= 0 && point.Y + input.Bounds.Height <= 860);
                var directory = Environment.GetEnvironmentVariable("HAVEN_VISUAL_CAPTURE_DIR");
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    Directory.CreateDirectory(directory);
                    using var frame = window.CaptureRenderedFrame();
                    Assert.NotNull(frame);
                    frame.Save(Path.Combine(directory, $"fixture-native-cui-chat-{theme}-{width}.png"));
                }
            }
        }
        finally { window.Content = null; window.Close(); }
    }

    private static ChatCuiActions Actions() => new(() => Task.CompletedTask, () => Task.CompletedTask, () => Task.CompletedTask,
        _ => Task.CompletedTask, _ => Task.CompletedTask, _ => Task.CompletedTask, _ => Task.CompletedTask,
        _ => Task.CompletedTask, _ => Task.CompletedTask, () => Task.CompletedTask, () => Task.CompletedTask, _ => Task.CompletedTask);
    private sealed class FixtureTheme(string name) : ICuiThemePreferenceSource
    {
        public CuiThemePreferenceSnapshot Current { get; } = new(name, 0, "explicit-ui-fixture", false, new(), CuiAppearance.SuperDark);
        public event EventHandler? Changed { add { } remove { } }
    }
}
