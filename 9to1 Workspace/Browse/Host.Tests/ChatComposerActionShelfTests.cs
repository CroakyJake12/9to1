using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Haven.Desktop.Views.Pages.Chat;
using Haven.Desktop.Views.Pages.Tasks;
using Haven.Application;
using Haven.Core;
using CakeOS.Cui.Runtime;
using HavenOS.Apps.Spaces.Tasks;

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

    [Theory]
    [InlineData("composer.steer")]
    [InlineData("composer.pause")]
    [InlineData("composer.resume")]
    public async Task Ordinary_sources_cannot_advertise_or_invoke_task_commands(string command)
    {
        var source = new Source { IsSending = true, Draft = "ordinary follow up" };
        var bindings = new ChatComposerBindings(source);
        Assert.False(bindings.IsActionAvailable(command));
        await Assert.ThrowsAsync<InvalidOperationException>(() => bindings.DispatchAsync(command, null).AsTask());
        Assert.Equal(0, source.Sends);
        Assert.Equal(0, source.Stops);
        Assert.Equal("ordinary follow up", source.Draft);
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
    public void Stopping_or_noninterruptible_work_cannot_advertise_queue_from_busy_state_alone()
    {
        var source = new Source { IsSending = true, Draft = "follow up", Stopping = true };
        var bindings = new ChatComposerBindings(source);
        Assert.False(bindings.IsActionAvailable("composer.queue"));
        Assert.False(bindings.IsActionAvailable("composer.stop"));
        Assert.True(bindings.TryGetValue("Composer.CanQueue", out var queue));
        Assert.Equal(false, queue);
        Assert.Equal("follow up", source.Draft);
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
    public async Task Stop_projects_actual_retirement_and_preserves_the_original_source_failure()
    {
        var failure = new IOException("original stop admission failed");
        var source = new Source { IsSending = true, Draft = "keep", StopFailure = failure };
        var bindings = new ChatComposerBindings(source);
        var refreshes = 0;
        bindings.PropertyChanged += (_, _) => refreshes++;
        var actual = await Assert.ThrowsAsync<IOException>(() => bindings.DispatchAsync("composer.stop", null).AsTask());
        Assert.Same(failure, actual);
        Assert.Equal(1, source.Stops);
        Assert.True(refreshes > 0);
        Assert.False(bindings.IsActionAvailable("composer.stop"));
        Assert.False(bindings.IsActionAvailable("composer.queue"));
        Assert.Equal("keep", source.Draft);
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
        public bool Stopping { get; set; }
        public bool CanStop => IsSending && !Stopping;
        public bool IsSafetyLocked { get; set; }
        public bool HasAdd => true;
        public bool CanSend => !IsSafetyLocked && !string.IsNullOrWhiteSpace(Draft);
        public bool IsEditing => false;
        public IReadOnlyList<ChatComposerChip> Chips => [];
        public Task Operation { get; set; } = Task.CompletedTask;
        public int Sends;
        public int Stops;
        public Exception? StopFailure;
        public ValueTask Send() { Sends++; return new(Operation); }
        public ValueTask Stop()
        {
            Stops++;
            Stopping = true;
            return StopFailure is null ? ValueTask.CompletedTask : ValueTask.FromException(StopFailure);
        }
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

// Real canonical widget/coordinator calls with a controlled repository/authority. These
// assertions establish adapter admission and draft custody, not native release acceptance.
public sealed class CanonicalTaskComposerTests
{
    [AvaloniaFact]
    public async Task Queue_preserves_newer_typing_and_rejects_a_second_click_while_the_original_ack_is_pending()
    {
        await using var rig = await Rig.CreateAsync();
        var bindings = rig.Page.OriginalComposer.Bindings;
        Assert.True(bindings.TrySetValue("Composer.Draft", "first instruction"));
        rig.Tasks.HoldWrite = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending = bindings.DispatchAsync("composer.queue", null).AsTask();
        await rig.Tasks.WriteEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(pending.IsCompleted);
        Assert.False(bindings.IsActionAvailable("composer.queue"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => bindings.DispatchAsync("composer.queue", null).AsTask());
        Assert.True(bindings.TrySetValue("Composer.Draft", "newer unsent instruction"));
        rig.Tasks.HoldWrite.SetResult();
        await pending.WaitAsync(TimeSpan.FromSeconds(5));
        var queued = Assert.Single(rig.Tasks.Current.Queue);
        Assert.Equal("first instruction", queued.Summary);
        Assert.Equal(rig.Initial.TaskId, rig.Tasks.Current.TaskId);
        Assert.Equal(rig.Initial.ExecutionId, rig.Tasks.Current.ExecutionId);
        Assert.Equal(rig.Initial.ContextId, rig.Tasks.Current.ContextId);
        Assert.Equal("newer unsent instruction", bindings.Source.Draft);
        Assert.Equal(1, rig.Tasks.Writes);
        Assert.True(bindings.IsActionAvailable("composer.queue"));
        Assert.Same(rig.Page.AcknowledgedFollowUp!.Snapshot, rig.Tasks.Current);
    }

    [AvaloniaTheory]
    [InlineData("composer.queue", TaskFollowUpMode.Queue)]
    [InlineData("composer.steer", TaskFollowUpMode.Steer)]
    public async Task Actual_task_ack_clears_only_the_submitted_draft_and_never_creates_a_new_run(string command, TaskFollowUpMode mode)
    {
        await using var rig = await Rig.CreateAsync();
        var bindings = rig.Page.OriginalComposer.Bindings;
        Assert.True(bindings.TrySetValue("Composer.Draft", "use the existing source"));
        Assert.False(bindings.IsActionAvailable("composer.send"));
        Assert.False(bindings.Source.CanResume); // No private original/cold continuation exists.
        await bindings.DispatchAsync(command, null);
        var actual = Assert.IsType<FollowUpDecision>(rig.Page.AcknowledgedFollowUp);
        Assert.Equal(mode, actual.Mode);
        Assert.Equal(rig.Initial.TaskId, actual.Snapshot.TaskId);
        Assert.Equal(rig.Initial.ExecutionId, actual.Snapshot.ExecutionId);
        Assert.Equal(rig.Initial.ContextId, actual.Snapshot.ContextId);
        Assert.Equal("", bindings.Source.Draft);
        Assert.False(bindings.IsActionAvailable("composer.queue"));
        Assert.False(bindings.IsActionAvailable("composer.steer"));
        Assert.Equal(1, rig.Tasks.Writes);
        Assert.Empty(actual.Snapshot.Attempts);
        Assert.Contains(mode == TaskFollowUpMode.Queue ? "task:queue" : "task:steer", rig.Authority.Commands);
    }

    private sealed class Rig : IAsyncDisposable
    {
        internal SpaceTaskWidgetPage Page = null!;
        internal TaskExecutionSnapshot Initial = null!;
        internal Tasks Tasks = new();
        internal Authority Authority = new();
        internal static async Task<Rig> CreateAsync()
        {
            var rig = new Rig();
            var spaces = new SpaceRegistry(new Settings());
            var space = await spaces.CreateAsync("Controlled composer task");
            var now = DateTimeOffset.UtcNow;
            var conversation = new Conversation(Guid.NewGuid(), HavenMode.Tasks, ConversationKind.Task,
                "Actual task", null, null, false, false, now, now, SpaceId: space.Id);
            var task = Guid.NewGuid(); var run = Guid.NewGuid();
            rig.Initial = new(task, conversation.Id, run, "Existing objective", TaskExecutionLifecycle.Suspended,
                TaskExecutionDurability.PersistedPlan, 1, [], [], [], [], null, now, now)
                { PersistenceRevision = 2, OwnerBinding = new(task, conversation.Id, run,
                    "controlled actor", "controlled profile", null, null, "revision", "test-only observation") };
            rig.Tasks.Current = rig.Initial;
            var coordinator = new TaskExecutionCoordinator(rig.Tasks, new Events(), admissionAuthority: rig.Authority);
            rig.Page = new(new SpaceTaskWorkspaceService(spaces, new Conversations(conversation), coordinator), coordinator,
                space.Id, conversation.Id, new UnavailableReadiness(), expectedOriginalTaskId: task, expectedOriginalExecutionId: run);
            try
            {
                await rig.Page.ActivateAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));
                var composer = await rig.Page.OriginalComposer.InitializeAsync().WaitAsync(TimeSpan.FromSeconds(10));
                Assert.Equal(CuiSceneAvailabilityState.Ready, composer.State);
                return rig;
            }
            catch
            {
                rig.Page.RequestRetirement();
                _ = await Record.ExceptionAsync(() => rig.Page.CloseAndDrainAsync());
                throw;
            }
        }
        public async ValueTask DisposeAsync()
        {
            Tasks.HoldWrite?.TrySetResult();
            Page.RequestRetirement();
            await Page.CloseAndDrainAsync().WaitAsync(TimeSpan.FromSeconds(10));
        }
    }
    private sealed class Authority : ITaskRunCommandAuthority
    {
        internal List<string> Commands = [];
        public Task ValidateTaskCommandAsync(TaskExecutionSnapshot task, string command, CancellationToken token)
        { token.ThrowIfCancellationRequested(); Commands.Add(command); return Task.CompletedTask; }
        public Task<TaskExecutionOwnerBinding> AuthorizeStartAsync(TaskExecutionSnapshot task, CancellationToken token) => throw new NotSupportedException();
        public Task<ITaskRunAdmissionLease> AuthorizeAttemptAsync(TaskExecutionSnapshot task, Guid id, TaskRunRouteCandidate route, Guid? previous, CancellationToken token) => throw new NotSupportedException();
        public Task ValidateAcceptedActionAsync(TaskExecutionSnapshot task, Guid attempt, Guid action, string receipt, CancellationToken token) => throw new NotSupportedException();
    }
    private sealed class Tasks : ITaskExecutionRepository
    {
        internal TaskExecutionSnapshot Current = null!;
        internal TaskCompletionSource? HoldWrite;
        internal TaskCompletionSource WriteEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal int Writes;
        public Task<TaskExecutionSnapshot?> GetAsync(Guid id, CancellationToken token) => Task.FromResult<TaskExecutionSnapshot?>(id == Current.TaskId ? Current : null);
        public Task<TaskExecutionSnapshot?> GetByContextAsync(Guid id, CancellationToken token) => Task.FromResult<TaskExecutionSnapshot?>(id == Current.ContextId ? Current : null);
        public async Task UpsertAsync(TaskExecutionSnapshot value, CancellationToken token)
        {
            WriteEntered.TrySetResult();
            if (HoldWrite is { } held) await held.Task.WaitAsync(token);
            token.ThrowIfCancellationRequested(); Current = value; Writes++;
        }
        public Task<IReadOnlyList<TaskExecutionSnapshot>> GetResumableAsync(CancellationToken token) => throw new NotSupportedException();
    }
    private sealed class Conversations(Conversation row) : IConversationRepository
    {
        public Task<Conversation?> GetAsync(Guid id, CancellationToken token) => Task.FromResult<Conversation?>(id == row.Id ? row : null);
        public Task<IReadOnlyList<Conversation>> GetBySpaceAsync(Guid id, int limit, CancellationToken token) => Task.FromResult<IReadOnlyList<Conversation>>([row]);
        public Task<IReadOnlyList<Conversation>> GetRecentAsync(HavenMode? mode, int limit, CancellationToken token) => throw new NotSupportedException();
        public Task<IReadOnlyList<ChatMessage>> GetMessagesAsync(Guid id, CancellationToken token) => throw new NotSupportedException();
        public Task UpsertConversationAsync(Conversation value, CancellationToken token) => throw new NotSupportedException();
        public Task AddMessageAsync(ChatMessage value, CancellationToken token) => throw new NotSupportedException();
        public Task DeleteConversationAsync(Guid id, CancellationToken token) => throw new NotSupportedException();
    }
    private sealed class Settings : IVersionedSettingsStore
    {
        private readonly Dictionary<string, object> _values = [];
        public Task<T?> GetAsync<T>(string key, CancellationToken token) where T : class => Task.FromResult(_values.GetValueOrDefault(key) as T);
        public Task SetAsync<T>(string key, T value, CancellationToken token) where T : class { _values[key] = value; return Task.CompletedTask; }
        public Task RemoveAsync(string key, CancellationToken token) { _values.Remove(key); return Task.CompletedTask; }
        public Task<SettingsExportManifest> ExportAsync(CancellationToken token) => throw new NotSupportedException();
        public Task<SettingsImportResult> ImportAsync(SettingsExportManifest value, CancellationToken token) => throw new NotSupportedException();
    }
    private sealed class Events : IExecutionEventSink { public bool TryPublish(ExecutionEvent value) => true; }
    private sealed class UnavailableReadiness : ICuiSceneReadiness
    {
        public ValueTask<CuiSceneAvailability> CheckAsync(CancellationToken token) => new(new CuiSceneAvailability(
            CuiSceneAvailabilityState.Unavailable, "CONTROLLED_WIDGET", "No real native task frame is claimed by this test."));
    }
}
