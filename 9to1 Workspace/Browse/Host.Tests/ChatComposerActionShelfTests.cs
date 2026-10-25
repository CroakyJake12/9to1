using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Haven.Desktop.Views.Pages.Chat;

namespace HavenOS.Browse.Host.Tests;

public sealed class ChatComposerActionShelfTests
{
    [Theory]
    [InlineData(false, false, false, false, false)]
    [InlineData(false, true, false, true, false)]
    [InlineData(true, false, false, false, false)]
    [InlineData(true, true, false, false, true)]
    [InlineData(true, true, true, false, false)]
    public void Actions_follow_live_run_draft_and_safety_state(bool running, bool draft, bool locked, bool send, bool queue)
    {
        var source = new Source { IsSending = running, Draft = draft ? "follow up" : "", IsSafetyLocked = locked };
        var bindings = new ChatComposerBindings(source);
        Assert.Equal(send, bindings.IsActionAvailable("composer.send"));
        Assert.Equal(queue, bindings.IsActionAvailable("composer.queue"));
        Assert.Equal(running, bindings.IsActionAvailable("composer.stop"));
        Assert.True(bindings.TryGetValue("Composer.CanQueue", out var projectedQueue));
        Assert.Equal(queue, projectedQueue);
    }

    [Fact]
    public async Task Retired_actions_cannot_invoke_a_source_or_clear_its_draft()
    {
        var source = new Source { Draft = "retain me" };
        var bindings = new ChatComposerBindings(source);
        await Assert.ThrowsAsync<InvalidOperationException>(() => bindings.DispatchAsync("composer.stop", null).AsTask());
        await Assert.ThrowsAsync<InvalidOperationException>(() => bindings.DispatchAsync("composer.queue", null).AsTask());
        source.IsSending = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => bindings.DispatchAsync("composer.send", null).AsTask());
        Assert.Equal(0, source.Sends);
        Assert.Equal(0, source.Stops);
        Assert.Equal("retain me", source.Draft);
    }

    [Fact]
    public async Task Queue_joins_the_existing_source_operation_once_and_returns_its_failure()
    {
        var original = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var source = new Source { IsSending = true, Draft = "follow up", Operation = original.Task };
        var bindings = new ChatComposerBindings(source);
        var admitted = bindings.DispatchAsync("composer.queue", null).AsTask();
        Assert.Equal(1, source.Sends);
        Assert.False(admitted.IsCompleted);
        original.SetException(new IOException("canonical admission failed"));
        var error = await Assert.ThrowsAsync<IOException>(() => admitted);
        Assert.Equal("canonical admission failed", error.Message);
        Assert.Equal("follow up", source.Draft);
    }

    [AvaloniaFact]
    public async Task Shared_authored_shelf_removes_inactive_controls_and_retains_live_stop_and_queue()
    {
        var source = new Source();
        var view = new ChatComposerCuiView(source);
        var window = new Window { Width = 360, Height = 480, Content = view };
        window.Show();
        try
        {
            await view.InitializeAsync().WaitAsync(TimeSpan.FromSeconds(10));
            Refresh();
            Assert.Null(Find("send-button"));
            Assert.Null(Find("stop-button"));
            Assert.Null(Find("queue-button"));
            source.Draft = "next message";
            Refresh();
            Assert.NotNull(Find("send-button"));
            Assert.Null(Find("stop-button"));
            source.IsSending = true;
            Refresh();
            Assert.Null(Find("send-button"));
            Assert.Equal("Stop current response", AutomationProperties.GetName(Find("stop-button")!));
            Assert.Equal("Queue message", AutomationProperties.GetName(Find("queue-button")!));
            source.IsSafetyLocked = true;
            Refresh();
            Assert.Null(Find("queue-button"));
            Assert.NotNull(Find("stop-button"));
        }
        finally
        {
            await view.CloseAsync().WaitAsync(TimeSpan.FromSeconds(10));
            window.Close();
        }

        Button? Find(string id) => view.GetVisualDescendants().OfType<Button>()
            .SingleOrDefault(button => AutomationProperties.GetAutomationId(button) == id);
        void Refresh()
        {
            view.Bindings.Refresh();
            for (var pass = 0; pass < 3; pass++)
            {
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();
                window.Measure(new Size(window.Width, window.Height));
                window.Arrange(new Rect(0, 0, window.Width, window.Height));
            }
        }
    }

    private sealed class Source : IChatComposerSource
    {
        public string Draft { get; set; } = "";
        public string Placeholder => "Message";
        public bool IsSending { get; set; }
        public bool IsSafetyLocked { get; set; }
        public bool HasAdd => true;
        public bool CanSend => !IsSafetyLocked && !string.IsNullOrWhiteSpace(Draft);
        public bool IsEditing => false;
        public IReadOnlyList<ChatComposerChip> Chips => [];
        public Task Operation { get; set; } = Task.CompletedTask;
        public int Sends;
        public int Stops;
        public ValueTask Send() { Sends++; return new(Operation); }
        public ValueTask Stop() { Stops++; return ValueTask.CompletedTask; }
        public ValueTask Add() => ValueTask.CompletedTask;
        public ValueTask OpenSettings() => ValueTask.CompletedTask;
        public ValueTask CloseSettings() => ValueTask.CompletedTask;
        public ValueTask SetActionMode(string mode) => ValueTask.CompletedTask;
        public ValueTask SetResponseMode(string mode) => ValueTask.CompletedTask;
        public ValueTask InvokeChip(string id) => ValueTask.CompletedTask;
        public ValueTask RemoveChip(string id) => ValueTask.CompletedTask;
        public ValueTask FormatSelection(string command) => ValueTask.CompletedTask;
        public ValueTask InsertEmoji(string emoji) => ValueTask.CompletedTask;
        public ValueTask CancelEdit() => ValueTask.CompletedTask;
    }
}
