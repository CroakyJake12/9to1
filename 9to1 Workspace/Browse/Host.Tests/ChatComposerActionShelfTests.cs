using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Haven.Desktop.Views.Pages.Chat;
using Haven.Desktop;
using Avalonia.Input;
using Avalonia.Interactivity;
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
        await Assert.ThrowsAsync<InvalidOperationException>(() => bindings.DispatchAsync(command, null,
            TestContext.Current.CancellationToken).AsTask());
        Assert.Equal(0, source.Sends);
        Assert.Equal(0, source.Stops);
        Assert.Equal("ordinary follow up", source.Draft);
    }

    [Fact]
    public async Task Retired_actions_cannot_invoke_a_source_or_clear_its_draft()
    {
        var source = new Source { Draft = "retain me" };
        var bindings = new ChatComposerBindings(source);
        await Assert.ThrowsAsync<InvalidOperationException>(() => bindings.DispatchAsync("composer.stop", null,
            TestContext.Current.CancellationToken).AsTask());
        await Assert.ThrowsAsync<InvalidOperationException>(() => bindings.DispatchAsync("composer.queue", null,
            TestContext.Current.CancellationToken).AsTask());
        source.IsSending = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => bindings.DispatchAsync("composer.send", null,
            TestContext.Current.CancellationToken).AsTask());
        Assert.Equal(0, source.Sends);
        Assert.Equal(0, source.Stops);
        Assert.Equal("retain me", source.Draft);
    }

    [AvaloniaTheory]
    [InlineData("composer.add")]
    [InlineData("composer.send")]
    [InlineData("composer.queue")]
    [InlineData("composer.submit")]
    [InlineData("composer.steer")]
    [InlineData("composer.resume")]
    [InlineData("composer.voice")]
    [InlineData("composer.format")]
    public async Task Action_issued_before_safety_lock_cannot_mutate_or_admit_work_after_the_lock(string command)
    {
        var sender = new TextBox();
        var source = new Source
        {
            Draft = "Keep this entire draft", IsSending = command == "composer.queue",
            CanSendOverride = true, CanSteer = true, CanResume = true, CanEnterVoice = true
        };
        var bindings = new ChatComposerBindings(source, candidate => ReferenceEquals(candidate, sender));
        Assert.True(bindings.IsActionAvailable(command));
        source.IsSafetyLocked = true; // The previously issued action now observes the current authority.
        Assert.False(bindings.IsActionAvailable(command));
        object parameter = command == "composer.submit"
            ? new CuiInputSubmitRequest(sender, Key.Enter, CuiInputSubmitModifiers.None) : "Bold";

        await Assert.ThrowsAsync<InvalidOperationException>(() => bindings.DispatchAsync(command, parameter,
            TestContext.Current.CancellationToken).AsTask());

        Assert.Equal(0, source.Sends);
        Assert.Equal(0, source.Mutations);
        Assert.Equal(0, source.Stops);
        Assert.Equal("Keep this entire draft", source.Draft);
    }

    [Fact]
    public void Draft_writeback_observes_the_current_lock_without_overwriting_retained_input()
    {
        var source = new Source { Draft = "Original draft" };
        var bindings = new ChatComposerBindings(source);
        Assert.True(bindings.TrySetValue("Composer.Draft", "Latest admitted draft"));
        Assert.Equal("Latest admitted draft", source.Draft);
        source.IsSafetyLocked = true;

        Assert.False(bindings.TrySetValue("Composer.Draft", "Stale input writeback"));
        Assert.Equal("Latest admitted draft", source.Draft);

        source.IsSafetyLocked = false;
        Assert.True(bindings.TrySetValue("Composer.Draft", "New unlocked edit"));
        Assert.Equal("New unlocked edit", source.Draft);
    }

    [Fact]
    public async Task Withdrawn_add_capability_refuses_an_already_issued_add_action()
    {
        var source = new Source { Draft = "Keep this draft" };
        var bindings = new ChatComposerBindings(source);
        Assert.True(bindings.IsActionAvailable("composer.add"));
        source.HasAdd = false;

        await Assert.ThrowsAsync<InvalidOperationException>(() => bindings.DispatchAsync("composer.add", null,
            TestContext.Current.CancellationToken).AsTask());

        Assert.Equal(0, source.Mutations);
        Assert.Equal("Keep this draft", source.Draft);
    }

    [AvaloniaFact]
    public async Task Locked_native_composer_blocks_editing_and_add_but_joins_the_exact_owned_stop()
    {
        var held = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var source = new Source
        {
            Draft = "Retain while stopping", IsSending = true, IsSafetyLocked = true,
            CanSendOverride = true, StopOperation = held.Task
        };
        var view = new ChatComposerCuiView(source);
        var window = new Window { Width = 500, Height = 360, Content = view };
        try
        {
            window.Show();
            await view.InitializeAsync(TestContext.Current.CancellationToken);
            Refresh();
            var input = Assert.Single(view.GetVisualDescendants().OfType<TextBox>(), control =>
                AutomationProperties.GetAutomationId(control) == "composer-input");
            Assert.False(input.IsEffectivelyEnabled);
            Assert.False(Find("add-button")!.IsEffectivelyEnabled);
            var stop = Find("send-button")!;
            Assert.True(stop.IsEffectivelyEnabled);
            Assert.Equal("Cancel response, 1 of 1", AutomationProperties.GetName(stop));
            Assert.False(Find("action-shelf-next")?.IsVisible ?? false);

            stop.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, stop));
            var settlement = view.CaptureOriginalActionSettlement();
            Assert.Equal(1, source.Stops);
            Assert.False(settlement.IsCompleted);
            Assert.Equal("Retain while stopping", source.Draft);
            held.SetResult();
            await settlement.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            Refresh();
            Assert.Equal(1, source.Stops);
            Assert.Equal(0, source.Sends);
            Assert.Equal(0, source.Mutations);
            Assert.False(Find("send-button")?.IsVisible ?? false);
            Assert.Equal("Retain while stopping", source.Draft);
        }
        finally
        {
            held.TrySetResult();
            await view.CloseAsync().WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            window.Content = null; window.Close();
        }

        Button? Find(string id) => view.GetVisualDescendants().OfType<Button>()
            .SingleOrDefault(control => AutomationProperties.GetAutomationId(control) == id);
        void Refresh()
        {
            view.RefreshSurface();
            for (var pass = 0; pass < 3; pass++)
            {
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();
            }
        }
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
        var admitted = bindings.DispatchAsync("composer.queue", null, TestContext.Current.CancellationToken).AsTask();
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
    public async Task Shared_authored_shelf_overlaps_actual_eligible_commands_and_browsing_preserves_draft()
    {
        var source = new Source();
        var view = new ChatComposerCuiView(source);
        var window = new Window { Width = 360, Height = 480, Content = view };
        window.Show();
        try
        {
            await view.InitializeAsync().WaitAsync(TimeSpan.FromSeconds(10));
            Refresh();
            Assert.False(Find("send-button")?.IsVisible ?? false);
            source.Draft = "next message";
            Refresh();
            var front = Find("send-button")!;
            Assert.True(front.IsVisible);
            Assert.Equal("Send, 1 of 1", AutomationProperties.GetName(front));
            Assert.False(Find("action-shelf-next")?.IsVisible ?? false);
            source.IsSending = true;
            Refresh();
            Assert.Equal("Cancel response, 1 of 2", AutomationProperties.GetName(front));
            var rear = Find("action-shelf-next")!;
            Assert.True(rear.IsVisible);
            Assert.Equal("Show Queue, next action", AutomationProperties.GetName(rear));
            Assert.Equal(56, front.Bounds.Width);
            var frontPoint = front.TranslatePoint(default, window)!.Value;
            var rearPoint = rear.TranslatePoint(default, window)!.Value;
            Assert.Equal(14, rearPoint.X - frontPoint.X, 3);
            Assert.Equal(frontPoint.Y, rearPoint.Y, 3);
            Assert.Equal(0, source.Stops);
            rear.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, rear));
            await view.CaptureOriginalActionSettlement().WaitAsync(TimeSpan.FromSeconds(10));
            Refresh();
            Assert.Equal("Queue, 2 of 2", AutomationProperties.GetName(front));
            Assert.Equal("next message", source.Draft);
            Assert.Equal(0, source.Sends);
            Assert.Equal(0, source.Stops);
            source.IsSafetyLocked = true;
            Refresh();
            Assert.Equal("Cancel response, 1 of 1", AutomationProperties.GetName(front));
            Assert.False(rear.IsVisible);
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

    [AvaloniaFact]
    public async Task Native_scene_routes_the_task_submit_port_without_admitting_a_legacy_fifo_message()
    {
        using var scene = new ChatHavenScene();
        var held = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var tasks = new NativeTaskPort(held.Task);
        Task? submission = null;
        try
        {
            await scene.ComposerInitialization.WaitAsync(TimeSpan.FromSeconds(10));
            scene.ComposerSource.TaskActions = tasks;
            scene.SetSending(true, true);
            scene.Input.Text = "same task follow-up";
            Assert.True(scene.ComposerView.Bindings.IsActionAvailable("composer.queue"));
            Assert.True(scene.ComposerView.Bindings.IsActionAvailable("composer.steer"));
            Assert.False(scene.ComposerView.Bindings.IsActionAvailable("composer.send"));
            submission = scene.HandleComposerSendAsync().AsTask(); // Same path exposed to submit callers.
            Assert.Same(held.Task, submission);
            Assert.False(submission.IsCompleted);
            Assert.Equal(1, tasks.Queues);
            Assert.False(scene.HasQueuedComposerMessages);
            await scene.ComposerView.Bindings.DispatchAsync("composer.steer", null);
            Assert.Equal(1, tasks.Steers);
            tasks.IsApplicable = false;
            Assert.False(scene.ComposerView.Bindings.IsActionAvailable("composer.steer"));
        }
        finally
        {
            held.TrySetResult();
            if (submission is not null) await submission;
            await scene.StartComposerClose();
            await scene.JoinComposerCloseAndReleaseAsync();
        }
    }
    private sealed class NativeTaskPort(Task queue) : IChatComposerTaskActions
    {
        public bool IsApplicable { get; set; } = true;
        public bool CanSteer => IsApplicable;
        public bool CanQueue => IsApplicable;
        public bool CanStop => IsApplicable;
        public TaskFollowUpMode? PreferredSendMode => TaskFollowUpMode.Queue;
        public int Queues, Steers;
        public ValueTask Steer() { Steers++; return ValueTask.CompletedTask; }
        public ValueTask Queue() { Queues++; return new(queue); }
        public ValueTask Stop() => ValueTask.CompletedTask;
    }

    private sealed class Source : IChatComposerSource
    {
        public string Draft { get; set; } = "";
        public string Placeholder => "Message";
        public bool IsSending { get; set; }
        public bool Stopping { get; set; }
        public bool CanStop => IsSending && !Stopping;
        public bool IsSafetyLocked { get; set; }
        public bool HasAdd { get; set; } = true;
        public bool? CanSendOverride { get; set; }
        public bool CanSend => CanSendOverride ?? (!IsSafetyLocked && !string.IsNullOrWhiteSpace(Draft));
        public bool CanSteer { get; set; }
        public bool CanResume { get; set; }
        public bool CanEnterVoice { get; set; }
        public bool IsEditing => false;
        public IReadOnlyList<ChatComposerChip> Chips => [];
        public Task Operation { get; set; } = Task.CompletedTask;
        public Task StopOperation { get; set; } = Task.CompletedTask;
        public int Sends;
        public int Stops;
        public int Mutations;
        public Exception? StopFailure;
        public ValueTask Send() { Sends++; return new(Operation); }
        public ValueTask Stop()
        {
            Stops++;
            Stopping = true;
            return StopFailure is null ? new ValueTask(StopOperation) : ValueTask.FromException(StopFailure);
        }
        public ValueTask Add() { Mutations++; return ValueTask.CompletedTask; }
        public ValueTask Steer() { Mutations++; return ValueTask.CompletedTask; }
        public ValueTask Resume() { Mutations++; return ValueTask.CompletedTask; }
        public ValueTask EnterVoice() { Mutations++; return ValueTask.CompletedTask; }
        public ValueTask OpenSettings() => ValueTask.CompletedTask;
        public ValueTask CloseSettings() => ValueTask.CompletedTask;
        public ValueTask SetActionMode(string mode) => ValueTask.CompletedTask;
        public ValueTask SetResponseMode(string mode) => ValueTask.CompletedTask;
        public ValueTask InvokeChip(string id) => ValueTask.CompletedTask;
        public ValueTask RemoveChip(string id) => ValueTask.CompletedTask;
        public ValueTask FormatSelection(string command) { Mutations++; return ValueTask.CompletedTask; }
        public ValueTask InsertEmoji(string emoji) => ValueTask.CompletedTask;
        public ValueTask CancelEdit() => ValueTask.CompletedTask;
    }
}

// Real canonical widget/coordinator calls with a controlled repository/authority. These
// assertions establish adapter admission and draft custody, not native release acceptance.
public sealed class CanonicalTaskComposerTests
{
    [AvaloniaTheory]
    [InlineData(SpaceSubmitMode.Steer, KeyModifiers.None, TaskFollowUpMode.Steer)]
    [InlineData(SpaceSubmitMode.Steer, KeyModifiers.Alt, TaskFollowUpMode.Queue)]
    [InlineData(SpaceSubmitMode.Queue, KeyModifiers.None, TaskFollowUpMode.Queue)]
    [InlineData(SpaceSubmitMode.Queue, KeyModifiers.Alt, TaskFollowUpMode.Steer)]
    [InlineData(SpaceSubmitMode.Queue, KeyModifiers.Control, TaskFollowUpMode.Queue)]
    public async Task Actual_input_submit_uses_saved_space_mode_and_alt_changes_only_this_instruction(
        SpaceSubmitMode preferred, KeyModifiers modifiers, TaskFollowUpMode expected)
    {
        using var preferences = new PreferencesOwner();
        await using var rig = await Rig.CreateAsync(preferences.Service);
        preferences.Service.SetSpaceSubmitMode(rig.SpaceId, preferred);
        var view = rig.Page.OriginalComposer;
        var window = new Window { Width = 500, Height = 360, Content = view };
        window.Show();
        try
        {
            rig.Page.OriginalComposer.Bindings.TrySetValue("Composer.Draft", "keyboard follow-up");
            Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            var input = view.GetVisualDescendants().OfType<TextBox>().Single(box => AutomationProperties.GetAutomationId(box) == "composer-input");
            input.Focus();
            var key = new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter, KeyModifiers = modifiers, Source = input };
            input.RaiseEvent(key);
            Assert.True(key.Handled);
            await view.CaptureOriginalActionSettlement().WaitAsync(TestContext.Current.CancellationToken);
            Assert.Equal(expected, rig.Page.AcknowledgedFollowUp!.Mode);
            Assert.Equal(preferred, preferences.Service.GetSpaceSubmitMode(rig.SpaceId, false));
            Assert.Equal(rig.Initial.TaskId, rig.Tasks.Current.TaskId);
            Assert.Equal(rig.Initial.ExecutionId, rig.Tasks.Current.ExecutionId);
            Assert.Equal(1, rig.Tasks.Writes);
            Assert.Equal("", view.Source.Draft);
        }
        finally { window.Content = null; window.Close(); }
    }

    [AvaloniaFact]
    public async Task Preference_choice_is_real_and_a_foreign_submit_sender_cannot_dispatch_it()
    {
        using var preferences = new PreferencesOwner();
        await using var rig = await Rig.CreateAsync(preferences.Service);
        var bindings = rig.Page.OriginalComposer.Bindings;
        bindings.TrySetValue("Composer.Draft", "retain this instruction");
        Assert.Null(preferences.Service.GetSpaceSubmitMode(rig.SpaceId, false));
        Assert.False(bindings.IsActionAvailable("composer.submit")); // No invented DEC-006 default.
        await bindings.DispatchAsync("composer.prefer-queue", null, TestContext.Current.CancellationToken);
        Assert.Equal(SpaceSubmitMode.Queue, preferences.Service.GetSpaceSubmitMode(rig.SpaceId, false));
        Assert.True(bindings.IsActionAvailable("composer.submit"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => bindings.DispatchAsync("composer.submit",
            new CuiInputSubmitRequest(new TextBox(), Key.Enter, CuiInputSubmitModifiers.None), TestContext.Current.CancellationToken).AsTask());
        Assert.Equal(0, rig.Tasks.Writes);
        Assert.Equal("retain this instruction", bindings.Source.Draft);
        var reloaded = new UserPreferencesService(preferences.Paths);
        Assert.Equal(SpaceSubmitMode.Queue, reloaded.GetSpaceSubmitMode(rig.SpaceId, false));
    }

    private sealed class PreferencesOwner : IDisposable
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(), "9to1-submit-mode-" + Guid.NewGuid().ToString("N"));
        internal IAppPaths Paths { get; }
        internal UserPreferencesService Service { get; }
        internal PreferencesOwner()
        {
            Directory.CreateDirectory(_directory); Paths = new PathsOwner(_directory); Service = new(Paths);
        }
        public void Dispose() => Directory.Delete(_directory, true);
        private sealed class PathsOwner(string directory) : IAppPaths
        {
            public string DataDirectory => directory;
            public string DatabasePath => Path.Combine(directory, "data.db");
            public string BrowserProfileDirectory => Path.Combine(directory, "browser");
            public string AttachmentsDirectory => Path.Combine(directory, "attachments");
            public string LogsDirectory => Path.Combine(directory, "logs");
            public string LegacyStatePath => Path.Combine(directory, "legacy");
        }
    }

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
        var published = Assert.Single(rig.Events.Published);
        Assert.Equal(mode == TaskFollowUpMode.Queue ? ExecutionActionType.Queue : ExecutionActionType.Steer, published.ActionType);
        Assert.Equal(rig.Initial.TaskId, published.TaskId);
        Assert.Equal(rig.Initial.ExecutionId, published.ExecutionId);
        Assert.Equal("use the existing source", published.SafeDetail);
    }

    private sealed class Rig : IAsyncDisposable
    {
        internal SpaceTaskWidgetPage Page = null!;
        internal TaskExecutionSnapshot Initial = null!;
        internal Guid SpaceId;
        internal Tasks Tasks = new();
        internal Events Events = new();
        internal static async Task<Rig> CreateAsync(UserPreferencesService? preferences = null)
        {
            var rig = new Rig();
            var spaces = new SpaceRegistry(new Settings());
            var space = await spaces.CreateAsync("Controlled composer task");
            rig.SpaceId = space.Id;
            var now = DateTimeOffset.UtcNow;
            var conversation = new Conversation(Guid.NewGuid(), HavenMode.Tasks, ConversationKind.Task,
                "Actual task", null, null, false, false, now, now, SpaceId: space.Id);
            var task = Guid.NewGuid(); var run = Guid.NewGuid();
            rig.Initial = new(task, conversation.Id, run, "Existing objective", TaskExecutionLifecycle.Suspended,
                TaskExecutionDurability.PersistedPlan, 1, [], [], [], [], null, now, now)
                { PersistenceRevision = 2, OwnerBinding = new(task, conversation.Id, run,
                    "controlled actor", "controlled profile", null, null, "revision", "test-only observation") };
            rig.Tasks.Current = rig.Initial;
            // Recorded owner provenance alone cannot expose current task commands. Use
            // the real observation/command authority with an independently supplied
            // current actor; provider/route capabilities remain deliberately absent.
            var unused = new UnusedModelDependencies();
            var authority = new TaskRunPermissionAuthority(new Actors(), unused, unused, unused, new(unused));
            var coordinator = new TaskExecutionCoordinator(rig.Tasks, rig.Events, admissionAuthority: authority);
            var observation = await coordinator.ReadAuthorizedTaskObservationAsync(task, TestContext.Current.CancellationToken);
            Assert.Equal(TaskObservationDisposition.Authorized, observation.Disposition);
            Assert.True(observation.HasCurrentCommandIdentity);
            Assert.Same(rig.Initial, observation.Snapshot);
            Assert.Equal(0, rig.Tasks.Writes);
            rig.Page = new(new SpaceTaskWorkspaceService(spaces, new Conversations(conversation), coordinator), coordinator,
                space.Id, conversation.Id, new UnavailableReadiness(), expectedOriginalTaskId: task, expectedOriginalExecutionId: run);
            if (preferences is not null) rig.Page.BindOriginalComposerPreferences(preferences);
            try
            {
                await rig.Page.ActivateAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));
                await rig.Page.OriginalComposer.InitializeAsync().WaitAsync(TimeSpan.FromSeconds(10));
                var composerHost = Assert.IsType<CuiSceneHost>(rig.Page.OriginalComposer.Content);
                Assert.Equal(CuiSceneAvailabilityState.Ready, composerHost.Availability?.State);
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
    private sealed class Actors : IAuthenticatedResourceActorSource
    {
        public ValueTask<AuthenticatedResourceActor?> GetCurrentAsync(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            return new(new AuthenticatedResourceActor("controlled actor", "controlled profile", null, null, "revision"));
        }
    }
    private sealed class UnusedModelDependencies : IModelProviderRegistry, IProviderConfigurationStore,
        IPrivacyPreferenceStore, IModelPermissionStore
    {
        public IReadOnlyList<IModelProvider> Providers => throw new NotSupportedException();
        public IModelProvider? Find(string providerId) => throw new NotSupportedException();
        public IModelProvider GetRequired(string providerId) => throw new NotSupportedException();
        public Task<IReadOnlyList<ProviderModelDescriptor>> GetModelsAsync(CancellationToken token) => throw new NotSupportedException();
        public Task<IReadOnlyList<ProviderConfiguration>> GetAllAsync(CancellationToken token) => throw new NotSupportedException();
        public Task<ProviderConfiguration?> GetAsync(string providerId, CancellationToken token) => throw new NotSupportedException();
        public Task UpsertAsync(ProviderConfiguration value, CancellationToken token) => throw new NotSupportedException();
        public Task DeleteAsync(string providerId, CancellationToken token) => throw new NotSupportedException();
        public PrivacyPreferences Current => throw new NotSupportedException();
        public Task UpdateAsync(PrivacyPreferences value, CancellationToken token) => throw new NotSupportedException();
        public Task<ModelPermissionPolicy> GetPolicyAsync(CancellationToken token) => throw new NotSupportedException();
        public Task SavePolicyAsync(ModelPermissionPolicy value, CancellationToken token) => throw new NotSupportedException();
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
    private sealed class Events : IExecutionEventSink
    {
        internal List<ExecutionEvent> Published { get; } = [];
        public bool TryPublish(ExecutionEvent value) { Published.Add(value); return true; }
    }
    private sealed class UnavailableReadiness : ICuiSceneReadiness
    {
        public ValueTask<CuiSceneAvailability> CheckAsync(CancellationToken token) => new(new CuiSceneAvailability(
            CuiSceneAvailabilityState.Unavailable, "CONTROLLED_WIDGET", "No real native task frame is claimed by this test."));
    }
}
