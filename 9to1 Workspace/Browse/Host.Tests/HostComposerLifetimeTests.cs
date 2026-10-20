using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CakeOS.Cui;
using CakeOS.Cui.Language;
using CakeOS.Cui.Runtime;
using Haven.Desktop.Views.Pages.Chat;
using System.Threading;
using Xunit;

namespace HavenOS.Browse.Host.Tests;

/// <summary>
/// Executable actual-page portable regression coverage for the single shared CUI composer. It
/// exercises the guarded initialization, real input wiring (SelectionStart/End, AcceptsReturn,
/// caret), async action settlement, close/self-join and watcher detach against the real
/// <see cref="ChatComposerCuiView"/>, plus mounting that same view inside a CUI host (the ChatSpace
/// route composition).
/// </summary>
public sealed class HostComposerLifetimeTests
{
    private sealed class RecordingSource : IChatComposerSource
    {
        public string Draft { get; set; } = string.Empty;
        public string Placeholder => "Ask Haven anything";
        public bool IsSending { get; set; }
        public bool IsSafetyLocked { get; set; }
        public bool HasAdd { get; set; } = true;
        public bool CanSend { get; set; } = true;
        public bool IsEditing { get; set; }
        public IReadOnlyList<ChatComposerChip> Chips { get; set; } = [];
        public int SendCount;
        public int FormatCount;
        public int EmojiInsertions;
        public int StopCount;

        public ValueTask Send() { SendCount++; return ValueTask.CompletedTask; }
        public ValueTask Stop() { StopCount++; return ValueTask.CompletedTask; }
        public ValueTask Add() => ValueTask.CompletedTask;
        public ValueTask OpenSettings() => ValueTask.CompletedTask;
        public ValueTask CloseSettings() => ValueTask.CompletedTask;
        public ValueTask SetActionMode(string mode) => ValueTask.CompletedTask;
        public ValueTask SetResponseMode(string mode) => ValueTask.CompletedTask;
        public ValueTask InvokeChip(string id) => ValueTask.CompletedTask;
        public ValueTask RemoveChip(string id) => ValueTask.CompletedTask;
        public ValueTask FormatSelection(string command) { FormatCount++; return ValueTask.CompletedTask; }
        public ValueTask InsertEmoji(string emoji) { EmojiInsertions++; return ValueTask.CompletedTask; }
        public ValueTask CancelEdit() => ValueTask.CompletedTask;
    }

    private sealed class EmptyBindings : ICuiBindingContext, ICuiActionDispatcher, ICuiActionAvailability
    {
        public static readonly EmptyBindings Instance = new();
        public bool TryGetValue(string path, out object? value) { value = null; return false; }
        public bool? IsActionAvailable(string command) => false;
        public ValueTask DispatchAsync(string command, object? parameter, CancellationToken cancellationToken = default) =>
            ValueTask.FromException(new InvalidOperationException($"Empty test bindings command '{command}'."));
    }

    [AvaloniaFact]
    public async Task Shared_composer_guarded_init_wires_real_input_then_close_detaches_watchers()
    {
        var source = new RecordingSource();
        var view = new ChatComposerCuiView(source);
        var window = new Window { Width = 900, Height = 700, Content = view };
        window.Show();
        try
        {
            await view.InitializeAsync().WaitAsync(TimeSpan.FromSeconds(10));
            PumpLayout(window);

            // Real input wiring: typed text round-trips through the canonical typed port and the
            // underlying TextBox advertises the multiline AcceptsReturn contract.
            view.Input.Text = "hello";
            Assert.Equal("hello", view.Input.Text);
            var box = FindInput(view);
            Assert.NotNull(box);
            Assert.True(box!.AcceptsReturn);

            // Selection semantics use the real endpoints and preserve forward/backward selection.
            view.Input.SetSelection(0, 2);
            Assert.Equal(0, view.Input.SelectionStart);
            Assert.Equal(2, view.Input.SelectionLength);
            Assert.Equal("he", view.Input.SelectedText);
            view.Input.PlaceCaretAtEnd();
            Assert.Equal(5, view.Input.CaretIndex);
            Assert.Equal(0, view.Input.SelectionLength);
            // Backward-directed selection is normalized to the bounded region with the correct
            // length and selected text.
            view.Input.SetSelection(3, 1);
            Assert.Equal(1, view.Input.SelectionStart);
            Assert.Equal(2, view.Input.SelectionLength);
            Assert.Equal("el", view.Input.SelectedText);
            view.Input.ClearSelection();
            Assert.Equal(0, view.Input.SelectionLength);

            // Async action settlement returns the real admitted source operation.
            _ = view.Bindings.DispatchAsync("composer.send", null, CancellationToken.None);
            Assert.Equal(1, source.SendCount);
            _ = view.Bindings.DispatchAsync("composer.stop", null, CancellationToken.None);
            Assert.Equal(1, source.StopCount);
            _ = view.Bindings.DispatchAsync("composer.format", "Bold", CancellationToken.None);
            Assert.Equal(1, source.FormatCount);
            _ = view.Bindings.DispatchAsync("composer.emoji-insert", "😀", CancellationToken.None);
            Assert.Equal(1, source.EmojiInsertions);

            // Close/self-join: the published close completes and detaches every watcher so no
            // late write or delayed attachment can publish text after close.
            await view.CloseAsync().WaitAsync(TimeSpan.FromSeconds(10));
            view.Input.Text = "should not publish";
            Assert.Equal(string.Empty, view.Input.Text);
            Assert.Null(FindInput(view));
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task Shared_composer_mounts_inside_a_cui_host_with_responsive_input_bounds()
    {
        var composer = new ChatComposerCuiView(new RecordingSource());
        var registry = new CuiControlRegistry();
        registry.BindAuthoredControl("composer-mount", _ => composer);
        var host = new CuiSceneHost(registry);
        var document = new CuiRichParser().Parse(
            "<Cui><Container id=\"surface\" Type=\"Vertical Stack\"><Container id=\"composer-mount\" Type=\"Vertical Stack\" /></Container></Cui>");
        var window = new Window { Width = 800, Height = 600, Content = host };
        window.Show();
        try
        {
            await host.ShowAsync(new CuiNativeScene("spaces.chat", "Chat Space", "Chat",
                document, EmptyBindings.Instance, EmptyBindings.Instance, ChatSpacePage.ImmediateReadinessInstance)
            { IsPublicationCurrent = () => true }).WaitAsync(TimeSpan.FromSeconds(10));
            await composer.InitializeAsync().WaitAsync(TimeSpan.FromSeconds(10));
            PumpLayout(window);

            var box = FindInput(composer);
            Assert.NotNull(box);
            Assert.True(box!.AcceptsReturn);
            Assert.InRange(box.Bounds.Height, 44, double.MaxValue);
            Assert.True(box.Bounds.Width > 0);
            Assert.InRange(box.Bounds.Y + box.Bounds.Height, 1, 600);
            Assert.InRange(box.Bounds.X + box.Bounds.Width, 1, 800);

            await host.CloseOriginalAsync().WaitAsync(TimeSpan.FromSeconds(10));
        }
        finally { window.Close(); }
    }

    /// <summary>Runs a few dispatcher/layout passes so headless composition builds the visual tree
    /// and real bounds before assertions inspect the real rendered input.</summary>
    private static void PumpLayout(Window window)
    {
        for (var pass = 0; pass < 3; pass++)
        {
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            window.Measure(new Size(window.Width, window.Height));
            window.Arrange(new Rect(0, 0, window.Width, window.Height));
        }
    }

    private static TextBox? FindInput(ContentControl root) =>
        root.GetVisualDescendants().OfType<TextBox>().FirstOrDefault(box =>
            AutomationProperties.GetAutomationId(box) == "composer-input");
}
