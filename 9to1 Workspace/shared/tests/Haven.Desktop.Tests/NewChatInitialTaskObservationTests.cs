using System.Reflection;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.Services;
using Haven.Desktop.Views.Pages.Chat;
using Xunit;
using Avalonia.Headless.XUnit;
using Haven.Desktop.Events;

namespace Haven.Desktop.Tests;

// The complete Data initial-host and maintained caller test partials are actual
// owning inputs. This managed UI-child journey does not instantiate a native page.
public sealed partial class ChatCloudPermissionCallerTests
{
    [AvaloniaFact]
    public async Task Ordinary_shared_composer_send_survives_delayed_safety_check_and_commits_before_cleanup()
    {
        var token = TestContext.Current.CancellationToken;
        await using var rig = new Rig(temporary: false);
        var offered = rig.Provider.Model.Model with
        {
            Name = rig.Provider.Model.Key,
            Capabilities = new HashSet<ToolCapability> { ToolCapability.Text }
        };
        rig.Client.AvailableModels = [offered];
        var safety = new HeldSendSafety();
        var directory = Path.Combine(Path.GetTempPath(), "ordinary-composer-send-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var drafts = new NativeComposerDraftStore();
        NewChatPage? page = null;
        Task? sending = null;
        try
        {
            page = CreateNativeComposerPage(rig, directory, drafts, safety);
            await page.OriginalInitialization.WaitAsync(TimeSpan.FromSeconds(10), token);
            await page.RefreshModelsAsync();
            page.SelectModel(offered);
            const string prompt = "Ordinary shared composer delayed-send regression";
            page.SetDraft(prompt);

            // This is the production page entry into ChatHavenScene's shared composer
            // send operation, not the Tasks-only action or a direct submission helper.
            page.RequestComposerSend();
            sending = Assert.IsAssignableFrom<Task>(typeof(NewChatPage)
                .GetField("_originalSend", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(page));
            await safety.SendCheckEntered.Task.WaitAsync(TimeSpan.FromSeconds(10), token);
            Assert.False(sending.IsCompleted);
            Assert.Equal(prompt, typeof(NewChatPage).GetField("_originalComposerText", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(page));
            Assert.DoesNotContain(rig.Conversations.Messages, message => message.Role == MessageRole.User);
            Assert.Equal(0, rig.Client.Dispatches);

            // Resume the actual awaited safety dependency from a worker thread. The
            // accepted send must marshal UI-owned composer/timer work and still follow
            // the ordinary model-gated, committed-input session path exactly once.
            await Task.Run(() => safety.ReleaseSendCheck.TrySetResult());
            await sending.WaitAsync(TimeSpan.FromSeconds(20), token);

            var committed = Assert.Single(rig.Conversations.Messages, message => message.Role == MessageRole.User);
            Assert.Equal(prompt, committed.Content);
            Assert.True(rig.Client.Dispatches > 0);
            Assert.False(page.IsSending);
            Assert.Null(drafts.Last);
        }
        finally
        {
            safety.ReleaseSendCheck.TrySetResult();
            if (page is not null)
            {
                if (sending is not null) await sending;
                page.RequestRetirement();
                await page.CloseAndDrainAsync();
            }
            Directory.Delete(directory, true);
        }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Native_task_followup_held_ack_then_retirement_retires_only_the_accepted_draft(bool newerTyping)
    {
        await using var rig = new Rig(temporary: false);
        rig.Capture.AskDuringCapture = false;
        rig.Client.RunApprovedOwnedFrame = true;
        var offered = rig.Provider.Model.Model with { Name = rig.Provider.Model.Key,
            Capabilities = new HashSet<ToolCapability> { ToolCapability.Text } };
        rig.Client.AvailableModels = [offered];
        var providerDisposal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        rig.Client.OriginalDispose = providerDisposal.Task;
        var directory = Path.Combine(Path.GetTempPath(), "native-task-composer-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var drafts = new NativeComposerDraftStore();
        NewChatPage? page = null;
        Task? command = null, close = null, producer = null;
        try
        {
            page = CreateNativeComposerPage(rig, directory, drafts);
            await page.OriginalInitialization.WaitAsync(TimeSpan.FromSeconds(10));
            page.ConfigureTaskMode();
            page.SelectModel(offered);
            await page.SubmitOriginalInstructionAsync("Review the existing task");
            await rig.Client.DisposeEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var lease = NativeComposerIssuedLease(page);
            producer = InitialHostedProducer(lease);
            Assert.True(rig.Service.IsIssuedOriginalTaskObservation(lease));
            Assert.True(rig.Service.CanSubmitObservedOriginalTaskFollowUp(lease));
            var initial = (await rig.Tasks.GetByContextAsync(page.ConversationId, TestContext.Current.CancellationToken))!;
            page.SetDraft("accepted follow-up");
            var bindings = page.OriginalTaskComposer.Bindings;
            Assert.True(bindings.IsActionAvailable("composer.queue"));
            Assert.True(bindings.IsActionAvailable("composer.steer"));
            Assert.False(bindings.IsActionAvailable("composer.send"));
            rig.TaskRows.HeldFollowUpWrite = new(TaskCreationOptions.RunContinuationsAsynchronously);
            command = bindings.DispatchAsync("composer.queue", null).AsTask();
            await rig.TaskRows.FollowUpWriteEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.False(command.IsCompleted);
            Assert.False(bindings.IsActionAvailable("composer.queue"));
            if (newerTyping) page.SetDraft("newer unsent draft");
            page.RequestRetirement();
            close = page.CloseAndDrainAsync();
            Assert.False(close.IsCompleted);
            rig.TaskRows.HeldFollowUpWrite.SetResult(); // Genuine controlled durable ACK after withdrawal.
            await command.WaitAsync(TimeSpan.FromSeconds(10));
            await close.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.NotNull(page.CanonicalTaskFollowUp);
            var actual = (await rig.Tasks.GetByContextAsync(initial.ContextId, TestContext.Current.CancellationToken))!;
            Assert.Equal(initial.TaskId, actual.TaskId);
            Assert.Equal(initial.ExecutionId, actual.ExecutionId);
            Assert.Equal("accepted follow-up", Assert.Single(actual.Queue).Summary);
            Assert.Equal(newerTyping ? "newer unsent draft" : "", drafts.Last?.Content ?? "");
            Assert.Equal(1, rig.Conversations.Messages.Count(message => message.Role == MessageRole.User));
            Assert.False(providerDisposal.Task.IsCanceled);
        }
        finally
        {
            rig.TaskRows.HeldFollowUpWrite?.TrySetResult();
            providerDisposal.TrySetResult();
            if (command is not null) _ = await Record.ExceptionAsync(() => command);
            if (page is not null)
            {
                page.RequestRetirement();
                _ = await Record.ExceptionAsync(() => close ??= page.CloseAndDrainAsync());
            }
            if (producer is not null) _ = await Record.ExceptionAsync(() => producer);
            Directory.Delete(directory, true);
        }
    }

    [AvaloniaFact]
    public async Task Task_permission_native_composer_observes_external_actual_response_without_automatic_resume_and_retires_its_watch()
    {
        var token = TestContext.Current.CancellationToken;
        await using var hub = new ExecutionEventHub(new ObservationEventRows());
        await using var rig = new Rig(temporary: false, originalEvents: hub);
        var offered = rig.Provider.Model.Model with { Name = rig.Provider.Model.Key,
            Capabilities = new HashSet<ToolCapability> { ToolCapability.Text } };
        rig.Client.AvailableModels = [offered];
        var directory = Path.Combine(Path.GetTempPath(), "native-permission-publication-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var drafts = new NativeComposerDraftStore();
        var page = CreateNativeComposerPage(rig, directory, drafts);
        ExecutionEvent? decisionPublication = null;
        EventHandler<ExecutionEvent> observed = (_, value) =>
        { if (value.SafeMetadata?.GetValueOrDefault("permissionPhase") == "decision") decisionPublication = value; };
        hub.Published += observed;
        try
        {
            await page.OriginalInitialization.WaitAsync(TimeSpan.FromSeconds(10), token);
            page.ConfigureTaskMode(); page.SelectModel(offered);
            await page.SubmitOriginalInstructionAsync("Keep this actual task suspended until explicit resume");
            var sending = (Task)typeof(NewChatPage).GetField("_originalSend", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(page)!;
            await sending.WaitAsync(TimeSpan.FromSeconds(10), token);
            await JoinNativePermissionRefreshesAsync(page, token);
            var waiting = Assert.IsType<ChatCanonicalPermissionWaitingObservation>(page.CanonicalPermissionWaiting);
            var bindings = page.OriginalTaskComposer.Bindings;
            Assert.False(bindings.IsActionAvailable("composer.resume"));
            await rig.Owner.ApproveOriginalAsync(waiting.PermissionRequest.Id, token); // Another genuine response surface.
            await JoinNativePermissionRefreshesAsync(page, token);
            Assert.Null(page.CanonicalPermissionWaiting);
            Assert.True(bindings.IsActionAvailable("composer.resume"));
            var same = (await rig.Tasks.GetByContextAsync(page.ConversationId, token))!;
            Assert.Equal(waiting.CanonicalTaskContext.TaskId, same.TaskId);
            Assert.Equal(waiting.CanonicalTaskContext.ExecutionId, same.ExecutionId);
            Assert.Equal(TaskExecutionLifecycle.Suspended, same.State); Assert.Empty(same.Attempts);
            Assert.Equal(0, rig.Client.Dispatches); Assert.Equal(0, rig.Workspace.Effects);
            page.SetDraft("retain the unsent draft after permission response");
            var last = page.ActualNativePermissionRefresh;
            page.RequestRetirement(); await page.CloseAndDrainAsync().WaitAsync(TimeSpan.FromSeconds(10), token);
            Assert.NotNull(decisionPublication);
            Assert.True(hub.TryPublish(decisionPublication! with { EventId = Guid.NewGuid() }));
            Assert.Same(last, page.ActualNativePermissionRefresh);
            Assert.Equal("retain the unsent draft after permission response", drafts.Last?.Content);
        }
        finally
        {
            page.RequestRetirement(); await page.CloseAndDrainAsync().WaitAsync(TimeSpan.FromSeconds(10), token);
            hub.Published -= observed; Directory.Delete(directory, true);
        }
    }
    private static async Task JoinNativePermissionRefreshesAsync(NewChatPage page, CancellationToken token)
    {
        for (var index = 0; index < 8; index++)
        {
            var actual = Assert.IsAssignableFrom<Task>(page.ActualNativePermissionRefresh);
            await actual.WaitAsync(TimeSpan.FromSeconds(10), token);
            if (ReferenceEquals(actual, page.ActualNativePermissionRefresh)) return;
        }
        throw new InvalidOperationException("Actual native permission refresh originals did not settle.");
    }

    [Fact]
    public async Task Actual_completion_admitted_before_pause_keeps_its_original_completion_acknowledgement()
    {
        var token = TestContext.Current.CancellationToken;
        await using var rig = new Rig(temporary: false);
        await rig.RunAsync(tools: false);
        var initial = rig.Service.CurrentCanonicalTask!;
        var permission = Assert.Single(rig.Stream, value => value.Kind == ChatStreamEventKind.PermissionRequired).PermissionRequest!;
        await rig.Owner.ApproveOriginalAsync(permission.Id, token);
        rig.Client.RunApprovedOwnedFrame = true;
        rig.TaskRows.HeldCompletionWrite = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var lease = await rig.Tasks.StartObservedOriginalRunResumeAsync(initial.TaskId, initial.ExecutionId, token);
        Task? pausing = null;
        try
        {
            await rig.TaskRows.CompletionWriteEntered.Task.WaitAsync(TimeSpan.FromSeconds(10), token);
            // The real original completion CAS is already invoked and held. A later
            // genuine withdrawal must join it, never rewrite that success as suspension.
            var withdrawal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using var registration = rig.Client.OriginalStreamToken.Register(() => withdrawal.TrySetResult());
            var actualPause = rig.Tasks.PauseOriginalRunAsync(initial.TaskId, initial.ExecutionId, token);
            pausing = actualPause;
            await withdrawal.Task.WaitAsync(TimeSpan.FromSeconds(10), token);
            Assert.False(actualPause.IsCompleted);
            rig.TaskRows.HeldCompletionWrite.SetResult();
            var ack = await actualPause.WaitAsync(TimeSpan.FromSeconds(10), token);
            Assert.Equal(TaskRunOriginalRunControlDisposition.AlreadyCompletedByOriginal, ack.Disposition);
            Assert.Equal(TaskExecutionLifecycle.Completed, ack.State);
            var completed = (await rig.Tasks.GetAsync(initial.TaskId, token))!;
            Assert.Equal(initial.ExecutionId, completed.ExecutionId);
            Assert.Equal(TaskExecutionLifecycle.Completed, completed.State);
            _ = await lease.WaitAsync();
        }
        finally
        {
            rig.TaskRows.HeldCompletionWrite.TrySetResult();
            if (pausing is not null) _ = await Record.ExceptionAsync(() => pausing);
            _ = await Record.ExceptionAsync(() => ActualHostedProducer(lease));
            await lease.DetachAndDrainAsync();
        }
    }

    [AvaloniaFact]
    public async Task Native_task_pause_waits_for_its_actual_owner_ack_and_preserves_the_same_draft_and_run()
    {
        var token = TestContext.Current.CancellationToken;
        await using var rig = new Rig(temporary: false);
        var offered = rig.Provider.Model.Model with { Name = rig.Provider.Model.Key,
            Capabilities = new HashSet<ToolCapability> { ToolCapability.Text } };
        rig.Client.AvailableModels = [offered];
        var disposal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var providerEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var providerRelease = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var withdrawal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationTokenRegistration withdrawalRegistration = default;
        var directory = Path.Combine(Path.GetTempPath(), "native-task-pause-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var drafts = new NativeComposerDraftStore();
        var page = CreateNativeComposerPage(rig, directory, drafts);
        Task? pausing = null, producer = null, resuming = null;
        var invalidation = new TaskCompletionSource<TaskRunActivityInvalidation>(TaskCreationOptions.RunContinuationsAsynchronously);
        var listenerFailure = new IOException("actual activity subscriber failure must not change Pause or cleanup");
        EventHandler<TaskRunActivityInvalidation>? activityChanged = null;
        try
        {
            await page.OriginalInitialization.WaitAsync(TimeSpan.FromSeconds(10));
            page.ConfigureTaskMode();
            page.SelectModel(offered);
            await page.SubmitOriginalInstructionAsync("Review this same task");
            var initialSend = (Task)typeof(NewChatPage).GetField("_originalSend", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(page)!;
            await initialSend.WaitAsync(TimeSpan.FromSeconds(10), token);
            var waiting = Assert.IsType<ChatCanonicalPermissionWaitingObservation>(page.CanonicalPermissionWaiting);
            await rig.Owner.ApproveOriginalAsync(waiting.PermissionRequest.Id, token);
            var bindings = page.OriginalTaskComposer.Bindings;
            await bindings.DispatchAsync("composer.refresh-task-controls", null, token);
            Assert.True(bindings.IsActionAvailable("composer.resume"));
            rig.Client.RunApprovedOwnedFrame = true;
            rig.Client.OriginalDispose = disposal.Task;
            rig.Capture.ApprovedProviderBody = actualToken =>
            {
                withdrawalRegistration = actualToken.Register(() => withdrawal.TrySetResult());
                providerEntered.TrySetResult(); return providerRelease.Task;
            };
            resuming = bindings.DispatchAsync("composer.resume", null, token).AsTask();
            await providerEntered.Task.WaitAsync(TimeSpan.FromSeconds(10), token);
            var lease = NativeComposerIssuedLease(page);
            producer = ActualHostedProducer(NativeComposerIssuedResumeLease(page));
            var initial = (await rig.Tasks.GetByContextAsync(page.ConversationId, token))!;
            var copied = (TaskRunOriginalInitialChatObservationLease)typeof(object)
                .GetMethod("MemberwiseClone", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(lease, null)!;
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => rig.Service.PauseObservedTaskComposerAsync(copied, token));
            page.SetDraft("queued instruction to preserve");
            await bindings.DispatchAsync("composer.queue", null);
            page.SetDraft("Preserve this unsent instruction");
            activityChanged = (_, hint) =>
            {
                if (hint.TaskId == initial.TaskId && hint.ExecutionId == initial.ExecutionId && hint.ContextId == initial.ContextId)
                { invalidation.TrySetResult(hint); throw listenerFailure; }
            };
            rig.Tasks.RunActivityInvalidated += activityChanged;
            Assert.True((await rig.Tasks.ObserveOriginalRunActivityAsync(initial.TaskId, initial.ExecutionId, token)).IsLive);
            Assert.True(bindings.IsActionAvailable("composer.pause"));
            Assert.False(bindings.IsActionAvailable("composer.resume"));
            pausing = bindings.DispatchAsync("composer.pause", null).AsTask();
            Assert.False(pausing.IsCompleted);
            Assert.Null(page.CanonicalTaskPauseAcknowledgement);
            Assert.False(bindings.IsActionAvailable("composer.end-pass"));
            Assert.False(bindings.IsActionAvailable("composer.pause"));
            Assert.False(bindings.IsActionAvailable("composer.resume"));
            Assert.False(disposal.Task.IsCanceled);
            // A Stop request is not admission. Hold the actual provider body until
            // its real owner token observes withdrawal, then independently hold cleanup.
            await withdrawal.Task.WaitAsync(TimeSpan.FromSeconds(10), token);
            providerRelease.SetResult(false);
            await rig.Client.DisposeEntered.Task.WaitAsync(TimeSpan.FromSeconds(10), token);
            Assert.False(pausing.IsCompleted);
            var hint = await invalidation.Task.WaitAsync(TimeSpan.FromSeconds(10), token);
            Assert.Equal(initial.TaskId, hint.TaskId); Assert.Equal(initial.ExecutionId, hint.ExecutionId);
            Assert.False((await rig.Tasks.ObserveOriginalRunActivityAsync(initial.TaskId, initial.ExecutionId, token)).IsLive);
            Assert.Contains(rig.Tasks.ObservationFailures, failure => failure.ObservationStage == "run-activity-observer" &&
                ReferenceEquals(failure.OriginalException, listenerFailure));
            disposal.SetResult();
            await pausing.WaitAsync(TimeSpan.FromSeconds(10));
            var ack = Assert.IsType<TaskRunOriginalRunControlResult>(page.CanonicalTaskPauseAcknowledgement);
            Assert.Equal(TaskRunOriginalRunControlKind.Pause, ack.Kind);
            Assert.Equal(TaskRunOriginalRunControlDisposition.Suspended, ack.Disposition);
            Assert.Equal(TaskExecutionLifecycle.Suspended, ack.State);
            var actual = (await rig.Tasks.GetByContextAsync(initial.ContextId, token))!;
            Assert.Equal(initial.TaskId, actual.TaskId);
            Assert.Equal(initial.ExecutionId, actual.ExecutionId);
            Assert.True(bindings.TryGetValue("Composer.Draft", out var draft));
            Assert.Equal("Preserve this unsent instruction", draft);
            var controls = (await rig.Service.GetObservedTaskComposerControlsAsync(lease, token))!;
            Assert.Equal(controls.CanResumeUnstartedOriginal || controls.CanResumeOriginalToolCheckpoint || controls.CanResumeColdOriginal,
                bindings.IsActionAvailable("composer.resume"));
            Assert.NotEmpty(actual.Attempts); // Actual invoked provider, not a synthetic never-started status.
            Assert.True(controls.CanEndPass);
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => rig.Service.EndObservedTaskComposerPassAsync(copied, controls, token));
            Assert.True(bindings.IsActionAvailable("composer.end-pass"));
            await bindings.DispatchAsync("composer.end-pass", null);
            var ended = Assert.IsType<TaskRunOriginalRunControlResult>(page.CanonicalTaskEndPassAcknowledgement);
            Assert.Equal(TaskRunOriginalRunControlKind.EndPass, ended.Kind);
            Assert.Equal(TaskRunOriginalRunControlDisposition.EndedAfterOriginalPause, ended.Disposition);
            Assert.Equal(TaskExecutionLifecycle.Cancelled, ended.State);
            var terminal = (await rig.Tasks.GetByContextAsync(initial.ContextId, token))!;
            Assert.Equal(initial.TaskId, terminal.TaskId); Assert.Equal(initial.ExecutionId, terminal.ExecutionId);
            Assert.Equal("queued instruction to preserve", Assert.Single(terminal.Queue).Summary);
            Assert.Equal(QueuedFollowUpState.Queued, terminal.Queue[0].State);
            Assert.Equal("Preserve this unsent instruction", page.OriginalTaskComposer.Input.Text);
            Assert.False(bindings.IsActionAvailable("composer.resume"));
            Assert.False(bindings.IsActionAvailable("composer.end-pass"));
            await page.CloseAndDrainAsync().WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal("Preserve this unsent instruction", drafts.Last?.Content);
            Assert.Single(rig.Conversations.Messages, value => value.Role == MessageRole.User);
        }
        finally
        {
            providerRelease.TrySetResult(false);
            disposal.TrySetResult();
            if (pausing is not null) _ = await Record.ExceptionAsync(() => pausing);
            if (resuming is not null) _ = await Record.ExceptionAsync(() => resuming);
            page.RequestRetirement();
            _ = await Record.ExceptionAsync(() => page.CloseAndDrainAsync());
            if (producer is not null) _ = await Record.ExceptionAsync(() => producer);
            if (activityChanged is not null) rig.Tasks.RunActivityInvalidated -= activityChanged;
            withdrawalRegistration.Dispose();
            Directory.Delete(directory, true);
        }
    }

    [AvaloniaFact]
    public async Task Native_issued_continuation_accepts_live_queue_and_keyboard_stop_uses_its_real_pause_owner()
    {
        var token = TestContext.Current.CancellationToken;
        await using var rig = new Rig(temporary: false);
        var offered = rig.Provider.Model.Model with { Name = rig.Provider.Model.Key,
            Capabilities = new HashSet<ToolCapability> { ToolCapability.Text } };
        rig.Client.AvailableModels = [offered];
        var directory = Path.Combine(Path.GetTempPath(), "native-resumed-controls-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var drafts = new NativeComposerDraftStore();
        var page = CreateNativeComposerPage(rig, directory, drafts);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var withdrawal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationTokenRegistration withdrawalRegistration = default;
        Task? resumed = null;
        try
        {
            await page.OriginalInitialization.WaitAsync(TimeSpan.FromSeconds(10));
            page.ConfigureTaskMode(); page.SelectModel(offered);
            await page.SubmitOriginalInstructionAsync("Keep this original task identity");
            var initialSend = (Task)typeof(NewChatPage).GetField("_originalSend", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(page)!;
            await initialSend.WaitAsync(TimeSpan.FromSeconds(10));
            var waiting = Assert.IsType<ChatCanonicalPermissionWaitingObservation>(page.CanonicalPermissionWaiting);
            var initial = (await rig.Tasks.GetByContextAsync(page.ConversationId, token))!;
            Assert.False((await rig.Tasks.ObserveOriginalRunActivityAsync(initial.TaskId, initial.ExecutionId, token)).IsLive);
            await rig.Owner.ApproveOriginalAsync(waiting.PermissionRequest.Id, token);
            var bindings = page.OriginalTaskComposer.Bindings;
            await bindings.DispatchAsync("composer.refresh-task-controls", null);
            Assert.True(bindings.IsActionAvailable("composer.resume"));
            rig.Client.RunApprovedOwnedFrame = true;
            rig.Capture.ApprovedProviderBody = actualToken =>
            {
                withdrawalRegistration = actualToken.Register(() => withdrawal.TrySetResult());
                entered.TrySetResult(); return release.Task;
            };
            resumed = bindings.DispatchAsync("composer.resume", null).AsTask();
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var liveActivity = await rig.Tasks.ObserveOriginalRunActivityAsync(initial.TaskId, initial.ExecutionId, token);
            Assert.True(liveActivity.IsLive);
            Assert.Equal(initial.TaskId, liveActivity.TaskId);
            Assert.Equal(initial.ExecutionId, liveActivity.ExecutionId);
            page.SetDraft("live continued follow-up");
            Assert.False(resumed.IsCompleted);
            Assert.True(page.IsSending);
            Assert.True(bindings.IsActionAvailable("composer.queue"));
            Assert.True(bindings.IsActionAvailable("composer.steer"));
            Assert.True(bindings.IsActionAvailable("composer.pause"));
            Assert.False(bindings.IsActionAvailable("composer.send"));
            await bindings.DispatchAsync("composer.queue", null);
            Assert.Equal("live continued follow-up", page.CanonicalTaskFollowUp?.QueuedTask?.Summary);
            Assert.Null(page.CanonicalPermissionWaiting);
            page.SetDraft("retain this draft while pausing");
            Assert.True(page.TryStopResponse()); // Keyboard/public Stop is the same real resumable pause.
            Assert.Null(page.CanonicalTaskPauseAcknowledgement);
            Assert.False(bindings.IsActionAvailable("composer.pause"));
            await withdrawal.Task.WaitAsync(TimeSpan.FromSeconds(10), token);
            release.SetResult(false);
            await resumed.WaitAsync(TimeSpan.FromSeconds(10));
            var pausing = OriginalNativePauseDriver(page);
            await pausing.WaitAsync(TimeSpan.FromSeconds(10));
            var ack = Assert.IsType<TaskRunOriginalRunControlResult>(page.CanonicalTaskPauseAcknowledgement);
            Assert.Equal(TaskRunOriginalRunControlKind.Pause, ack.Kind);
            Assert.Equal(TaskRunOriginalRunControlDisposition.Suspended, ack.Disposition);
            var same = (await rig.Tasks.GetByContextAsync(initial.ContextId, token))!;
            Assert.False((await rig.Tasks.ObserveOriginalRunActivityAsync(same.TaskId, same.ExecutionId, token)).IsLive);
            Assert.Equal(initial.TaskId, same.TaskId); Assert.Equal(initial.ExecutionId, same.ExecutionId);
            Assert.Equal("live continued follow-up", Assert.Single(same.Queue).Summary);
            Assert.Equal("retain this draft while pausing", page.OriginalTaskComposer.Input.Text);
            Assert.Single(rig.Conversations.Messages, value => value.Role == MessageRole.User);
            await page.CloseAndDrainAsync().WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal("retain this draft while pausing", drafts.Last?.Content);
        }
        finally
        {
            release.TrySetResult(false);
            page.RequestRetirement();
            _ = await Record.ExceptionAsync(() => page.CloseAndDrainAsync());
            if (resumed is not null) _ = await Record.ExceptionAsync(() => resumed);
            withdrawalRegistration.Dispose();
            Directory.Delete(directory, true);
        }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Native_resumed_followup_held_ack_then_retirement_preserves_its_actual_acceptance_and_newer_draft(bool newerTyping)
    {
        var token = TestContext.Current.CancellationToken;
        await using var rig = new Rig(temporary: false);
        var offered = rig.Provider.Model.Model with { Name = rig.Provider.Model.Key,
            Capabilities = new HashSet<ToolCapability> { ToolCapability.Text } };
        rig.Client.AvailableModels = [offered];
        var directory = Path.Combine(Path.GetTempPath(), "native-resumed-followup-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var drafts = new NativeComposerDraftStore();
        var page = CreateNativeComposerPage(rig, directory, drafts);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken actualProducerToken = default;
        Task? resumed = null, command = null, close = null, producer = null;
        try
        {
            await page.OriginalInitialization.WaitAsync(TimeSpan.FromSeconds(10));
            page.ConfigureTaskMode(); page.SelectModel(offered);
            await page.SubmitOriginalInstructionAsync("Keep the original accepted task");
            var initialSend = (Task)typeof(NewChatPage).GetField("_originalSend", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(page)!;
            await initialSend.WaitAsync(TimeSpan.FromSeconds(10));
            var waiting = Assert.IsType<ChatCanonicalPermissionWaitingObservation>(page.CanonicalPermissionWaiting);
            await rig.Owner.ApproveOriginalAsync(waiting.PermissionRequest.Id, token);
            var bindings = page.OriginalTaskComposer.Bindings;
            await bindings.DispatchAsync("composer.refresh-task-controls", null);
            rig.Client.RunApprovedOwnedFrame = true;
            rig.Capture.ApprovedProviderBody = actual => { actualProducerToken = actual; entered.TrySetResult(); return release.Task; };
            resumed = bindings.DispatchAsync("composer.resume", null).AsTask();
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var lease = NativeComposerIssuedResumeLease(page);
            producer = ActualHostedProducer(lease);
            Assert.True(rig.Service.IsIssuedObservedTaskComposerResume(NativeComposerIssuedLease(page), lease));
            var initial = (await rig.Tasks.GetByContextAsync(page.ConversationId, token))!;
            page.SetDraft("accepted continued instruction");
            Assert.True(bindings.IsActionAvailable("composer.queue"));
            rig.TaskRows.HeldFollowUpWrite = new(TaskCreationOptions.RunContinuationsAsynchronously);
            command = bindings.DispatchAsync("composer.queue", null).AsTask();
            await rig.TaskRows.FollowUpWriteEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.False(command.IsCompleted);
            if (newerTyping) page.SetDraft("newer continued draft");
            page.RequestRetirement();
            close = page.CloseAndDrainAsync();
            Assert.False(close.IsCompleted);
            rig.TaskRows.HeldFollowUpWrite.SetResult();
            await command.WaitAsync(TimeSpan.FromSeconds(10));
            await close.WaitAsync(TimeSpan.FromSeconds(10));
            await resumed.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.False(actualProducerToken.IsCancellationRequested);
            Assert.False(producer.IsCompleted);
            Assert.Equal(TaskRunOriginalResumeObservationDisposition.ObservationDetached, (await lease.WaitAsync()).Disposition);
            var actual = (await rig.Tasks.GetByContextAsync(initial.ContextId, token))!;
            Assert.Equal(initial.TaskId, actual.TaskId); Assert.Equal(initial.ExecutionId, actual.ExecutionId);
            Assert.Equal("accepted continued instruction", Assert.Single(actual.Queue).Summary);
            Assert.Equal(newerTyping ? "newer continued draft" : "", drafts.Last?.Content ?? "");
            Assert.Single(rig.Conversations.Messages, value => value.Role == MessageRole.User);
            release.SetResult(false);
            await producer.WaitAsync(TimeSpan.FromSeconds(10));
        }
        finally
        {
            rig.TaskRows.HeldFollowUpWrite?.TrySetResult();
            release.TrySetResult(false);
            if (command is not null) _ = await Record.ExceptionAsync(() => command);
            page.RequestRetirement();
            _ = await Record.ExceptionAsync(() => close ??= page.CloseAndDrainAsync());
            if (resumed is not null) _ = await Record.ExceptionAsync(() => resumed);
            if (producer is not null) _ = await Record.ExceptionAsync(() => producer);
            Directory.Delete(directory, true);
        }
    }
    private static TaskRunOriginalResumeObservationLease NativeComposerIssuedResumeLease(NewChatPage page)
    {
        var entries = (System.Collections.IDictionary)typeof(NewChatPage).GetField("_nativeTaskLiveResumes", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(page)!;
        var entry = Assert.Single(entries.Values.Cast<object>());
        return Assert.IsType<TaskRunOriginalResumeObservationLease>(entry.GetType().GetProperty("Lease")!.GetValue(entry));
    }

    private static Task OriginalNativePauseDriver(NewChatPage page) =>
        Assert.IsAssignableFrom<Task>(typeof(NewChatPage).GetField("_actualNativeTaskPause", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(page));

    [AvaloniaFact]
    public async Task Native_voice_entry_requires_its_actual_host_and_preserves_the_same_draft_and_selection()
    {
        await using var rig = new Rig(temporary: false);
        var offered = rig.Provider.Model.Model with { Name = rig.Provider.Model.Key,
            Capabilities = new HashSet<ToolCapability> { ToolCapability.Text } };
        rig.Client.AvailableModels = [offered];
        var directory = Path.Combine(Path.GetTempPath(), "native-voice-entry-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var page = CreateNativeComposerPage(rig, directory, new NativeComposerDraftStore());
        try
        {
            await page.OriginalInitialization.WaitAsync(TimeSpan.FromSeconds(10));
            page.SelectModel(offered);
            page.SetDraft("keep this draft");
            var view = page.OriginalTaskComposer;
            view.Input.SetSelection(2, 7);
            var selection = (view.Input.CaretIndex, view.Input.SelectionStart, view.Input.SelectionLength);
            Assert.False(view.Bindings.IsActionAvailable("composer.voice"));
            var actualOwner = new NativeVoiceEntryOwner(page.ConversationId, offered);
            page.BindOriginalComposerVoiceOwner(actualOwner);
            Assert.True(view.Bindings.IsActionAvailable("composer.voice"));
            Assert.True(view.Bindings.TryGetValue("Composer.ActionShelf", out var value));
            Assert.Equal(new[] { "send", "voice" }, Assert.IsAssignableFrom<IReadOnlyList<CakeOS.Cui.Runtime.CuiActionShelfItem>>(value).Select(item => item.Id));
            Assert.Equal(0, actualOwner.Entries);
            await view.Bindings.DispatchAsync("composer.voice", null);
            Assert.Equal(1, actualOwner.Entries);
            Assert.Equal("keep this draft", view.Input.Text);
            Assert.Equal(selection, (view.Input.CaretIndex, view.Input.SelectionStart, view.Input.SelectionLength));
            Assert.Equal(0, rig.Client.Dispatches); // Opening the real workflow is not capture/provider invocation.
            actualOwner.Available = false;
            Assert.False(view.Bindings.IsActionAvailable("composer.voice"));
            await Assert.ThrowsAsync<InvalidOperationException>(() => view.Bindings.DispatchAsync("composer.voice", null).AsTask());
            Assert.Equal(1, actualOwner.Entries);
            page.BindOriginalComposerVoiceOwner(null);
            Assert.False(view.Bindings.IsActionAvailable("composer.voice"));
        }
        finally
        {
            page.RequestRetirement();
            _ = await Record.ExceptionAsync(() => page.CloseAndDrainAsync());
            Directory.Delete(directory, true);
        }
    }

    private sealed class NativeVoiceEntryOwner(Guid conversationId, ModelDescriptor model) : IChatComposerVoiceActions
    {
        public bool Available = true;
        public int Entries;
        public bool CanEnter(Guid actualConversationId, ModelDescriptor actualModel) =>
            Available && actualConversationId == conversationId && actualModel == model;
        public ValueTask Enter(Guid actualConversationId, ModelDescriptor actualModel)
        {
            Assert.True(CanEnter(actualConversationId, actualModel));
            Entries++;
            return ValueTask.CompletedTask;
        }
    }

    [AvaloniaFact]
    public async Task Existing_voice_widget_start_passes_its_exact_attached_conversation_to_the_actual_call_owner()
    {
        await using var rig = new Rig(temporary: false);
        var conversation = rig.Conversation;
        rig.Conversations.Current = conversation;
        var offered = rig.Provider.Model.Model;
        CallStartOptions? requested = null;
        var starts = 0;
        var call = new CallSession(Guid.NewGuid(), conversation.Id, offered.Name, null, null, null,
            CallInputMode.HandsFree, false, CallSessionStatus.Active, DateTimeOffset.UtcNow);
        var capabilities = new CallCapabilities(false, false, false, "Controlled capture unavailable", null, null, [], [], []);
        var actualOwner = NativeComposerProxy<ICallCoordinator>((name, arguments) => name switch
        {
            "get_Capabilities" => capabilities,
            "get_IsActive" => starts != 0,
            "get_IsMuted" or "get_IsScreenSharing" => false,
            "get_State" => CallState.Listening,
            "get_CurrentSession" => starts == 0 ? null : call,
            "get_CurrentConversation" => starts == 0 ? null : conversation,
            "StartAsync" => Start((CallStartOptions)arguments![0]!),
            _ when name.StartsWith("add_", StringComparison.Ordinal) || name.StartsWith("remove_", StringComparison.Ordinal) => null,
            _ => throw new InvalidOperationException("Unexpected call owner operation: " + name)
        });
        Task<CallSession> Start(CallStartOptions options) { starts++; requested = options; return Task.FromResult(call); }
        using var widget = new Haven.Desktop.ViewModels.InChatCallWidgetViewModel(actualOwner, rig.Conversations);
        widget.AttachConversation(conversation.Id, offered);
        widget.Open();
        Assert.Equal(0, starts);
        await widget.StartCallCommand.ExecuteAsync();
        Assert.NotNull(requested);
        Assert.Equal(conversation.Id, requested.ConversationId);
        Assert.Equal(offered, requested.Model);
        Assert.Equal(conversation.Id, widget.LinkedCallConversationId);
        Assert.Equal(1, starts);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Native_task_stream_publication_withdrawal_preserves_only_actual_callback_failures(bool foreignFailure)
    {
        await using var rig = new Rig(temporary: false);
        var directory = Path.Combine(Path.GetTempPath(), "native-task-publication-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var page = CreateNativeComposerPage(rig, directory, new NativeComposerDraftStore());
        try
        {
            await page.OriginalInitialization.WaitAsync(TimeSpan.FromSeconds(10));
            var publications = 0;
            var actualFailure = new OperationCanceledException("Actual foreign callback cancellation", new CancellationToken(true));
            var publish = typeof(NewChatPage).GetMethod("PublishOriginalInitialTaskUiAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var run = typeof(NewChatPage).GetMethods(BindingFlags.Instance | BindingFlags.NonPublic)
                .Single(method => method.Name == "RunChatOriginalAsync" && !method.IsGenericMethod &&
                    method.GetParameters()[0].ParameterType == typeof(Func<Task>));
            // Actual admitted page work and its actual dispatcher callback. Withdrawal inside
            // the callback deterministically exercises the successful-await/retirement boundary.
            Func<Task> body = () => (Task)publish.Invoke(page, [(Action)(() =>
            {
                publications++;
                page.RequestRetirement();
                if (foreignFailure) throw actualFailure;
            })])!;
            var command = (Task)run.Invoke(page, [body])!;
            if (foreignFailure)
            {
                var commandFailure = await Assert.ThrowsAnyAsync<Exception>(() => command.WaitAsync(TimeSpan.FromSeconds(10)));
                var closeFailure = await Assert.ThrowsAnyAsync<Exception>(() => page.CloseAndDrainAsync().WaitAsync(TimeSpan.FromSeconds(10)));
                Assert.Contains(Leaves(commandFailure), cause => ReferenceEquals(cause, actualFailure));
                Assert.Contains(Leaves(closeFailure), cause => ReferenceEquals(cause, actualFailure));
            }
            else
            {
                await command.WaitAsync(TimeSpan.FromSeconds(10));
                await page.CloseAndDrainAsync().WaitAsync(TimeSpan.FromSeconds(10));
            }
            Assert.Equal(1, publications);
        }
        finally
        {
            page.RequestRetirement();
            _ = await Record.ExceptionAsync(() => page.CloseAndDrainAsync());
            Directory.Delete(directory, true);
        }
    }

    private static TaskRunOriginalInitialChatObservationLease NativeComposerIssuedLease(NewChatPage page)
    {
        var presentation = typeof(NewChatPage).GetField("_currentOriginalInitialTaskPresentation", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(page)!;
        var owner = (NewChatOriginalInitialTaskObservation)presentation.GetType().GetProperty("Owner")!.GetValue(presentation)!;
        return InitialUiCapturedLease(owner) ?? throw new InvalidOperationException("No actual source-issued lease was captured.");
    }
    private static NewChatPage CreateNativeComposerPage(Rig rig, string directory, NativeComposerDraftStore drafts,
        IConversationSafetyService? safety = null)
    {
        var paths = NativeComposerProxy<IAppPaths>((name, _) => name == "get_DataDirectory" ? directory : Path.Combine(directory, name));
        var local = new GenUiLocalActionRegistry(); var instances = new GenUiInstanceStore();
        return new(new HavenEventBus(), rig.Conversations, rig.Client, rig.Service, safety ?? new Safety(),
            NativeComposerProxy<IConversationVersioningService>((name, _) => throw new InvalidOperationException("Unexpected versioning: " + name)),
            new UserPreferencesService(paths),
            new GenerativeUiEventRouter([], new NativeComposerAudit(), instances), instances,
            new CalculatorTemplateRuntime(local, instances), new StructuredFormTemplateRuntime(local, instances),
            new ChoicePromptTemplateRuntime(local), new ChecklistTemplateRuntime(local, instances), new DataGridTemplateRuntime(local),
            new CardDeckTemplateRuntime(local, instances), new GraphTemplateRuntime(local), new TaskListTemplateRuntime(local),
            new DashboardTemplateRuntime(local), new AssessmentTemplateRuntime(local), new WorkflowTemplateRuntime(local),
            new CustomTemplateRuntime(local, instances), conversationProduction: drafts.Source);
    }
    private sealed class NativeComposerDraftStore
    {
        internal ConversationDraft? Last;
        private readonly Dictionary<Guid, ConversationBranch> _branches = [];
        internal IConversationProductionRepository Source { get; }
        internal NativeComposerDraftStore() => Source = NativeComposerProxy<IConversationProductionRepository>((name, args) => name switch
        {
            "GetCurrentBranchAsync" => Task.FromResult<ConversationBranch?>(_branches.GetValueOrDefault((Guid)args![0]!)),
            "EnsureRootBranchAsync" => Task.FromResult(Root((Guid)args![0]!)),
            "GetDraftAsync" => Task.FromResult(Last),
            "SaveDraftAsync" => Save((ConversationDraft)args![0]!),
            "DeleteDraftAsync" => Delete(),
            _ => throw new InvalidOperationException("Unexpected draft operation: " + name)
        });
        private ConversationBranch Root(Guid conversation)
        {
            if (_branches.TryGetValue(conversation, out var branch)) return branch;
            return _branches[conversation] = new(Guid.NewGuid(), conversation, null, null, "Main", ConversationBranchReason.Root, true, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        }
        private Task Save(ConversationDraft value) { Last = value; return Task.CompletedTask; }
        private Task Delete() { Last = null; return Task.CompletedTask; }
    }
    private sealed class NativeComposerAudit : IGenUiEventAuditSink
    {
        public ValueTask RecordAsync(GenUiEvent value, GenUiActionResult result, CancellationToken token) =>
            ValueTask.FromException(new InvalidOperationException("No generated UI event was requested."));
    }
    private sealed class HeldSendSafety : IConversationSafetyService
    {
        internal readonly TaskCompletionSource SendCheckEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource ReleaseSendCheck = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<ConversationSafetySnapshot> GetSnapshotAsync(Guid id, CancellationToken token) =>
            Task.FromResult(new ConversationSafetySnapshot(id, 0, ConversationSafetyState.Active, null, 0));
        public Task<ConversationSafetyFlagResult> RecordConfirmedFlagAsync(Guid id, ConfirmedSafetyFlag flag, CancellationToken token) =>
            throw new NotSupportedException();
        public async Task EnsureMayActAsync(Guid id, string operation, CancellationToken token)
        {
            if (operation != "chat.send") return;
            SendCheckEntered.TrySetResult();
            await ReleaseSendCheck.Task.WaitAsync(token);
        }
    }
    private static T NativeComposerProxy<T>(Func<string, object?[]?, object?> call) where T : class
    {
        var actual = DispatchProxy.Create<T, NativeComposerIo>();
        ((NativeComposerIo)(object)actual).Call = call; return actual;
    }
    public class NativeComposerIo : DispatchProxy
    {
        public Func<string, object?[]?, object?> Call = null!;
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => Call(targetMethod!.Name, args);
    }

    [Fact]
    public async Task Tasks_ui_observation_retires_while_same_business_disposal_and_Task_Run_remain_owned()
    {
        await using var rig = new Rig(temporary: false);
        rig.Capture.AskDuringCapture = false;
        var held = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        rig.Client.OriginalDispose = held.Task;
        var conversation = await PersistInitialTaskConversationAsync(rig);
        NewChatOriginalInitialTaskObservation? child = null;
        Task<TaskRunOriginalInitialChatObservationLease>? rawAcquisition = null;
        Task? childClose = null;
        var retiring = false;
        var events = new List<ChatStreamEvent>();
        var parent = new DesktopOriginalWorkLifetime(() =>
        {
            retiring = true;
            child!.RequestRetirement();
            childClose = child.CloseAndDrainAsync();
            return childClose;
        }, () => Task.CompletedTask);
        var original = parent.RunAsync(async pageOriginal =>
        {
            child = new(rig.Service, pageOriginal, conversation.Id,
                () => rawAcquisition = StartInitialTaskObservationAsync(rig.Service, rig, conversation, TestContext.Current.CancellationToken),
                value => { events.Add(value); return Task.CompletedTask; }, () => retiring,
                rig.Service.DemandExternalOriginalTaskObservationSourceJoin);
            child.BeginOriginalAcquisition();
            _ = await pageOriginal.AwaitAsync(child.ActualObservation);
        });
        Task? parentClose = null;
        Task? producer = null;
        try
        {
            await rig.Client.DisposeEntered.Task.WaitAsync(TestContext.Current.CancellationToken);
            var lease = await rawAcquisition!;
            producer = InitialHostedProducer(lease);
            var before = (await rig.Tasks.GetByContextAsync(conversation.Id, TestContext.Current.CancellationToken))!;
            Assert.False(producer.IsCompleted);
            Assert.False(held.Task.IsCompleted);
            child!.DemandExternalClose();
            parentClose = parent.CloseAndDrainAsync();
            await parentClose;
            Assert.True(parentClose.IsCompletedSuccessfully);
            Assert.True(original.IsCompletedSuccessfully);
            Assert.True(childClose!.IsCompletedSuccessfully);
            Assert.Same(childClose, child.CloseAndDrainAsync());
            Assert.Equal(TaskRunInitialChatObservationDisposition.ObservationDetached, (await child.ActualObservation)!.Disposition);
            Assert.False(producer.IsCompleted);
            Assert.False(held.Task.IsCanceled);
            Assert.True(rig.Service.IsIssuedOriginalTaskObservation(lease));
            var after = (await rig.Tasks.GetByContextAsync(conversation.Id, TestContext.Current.CancellationToken))!;
            Assert.Equal(before.TaskId, after.TaskId);
            Assert.Equal(before.ExecutionId, after.ExecutionId);
            Assert.Equal(TaskExecutionLifecycle.Running, after.State);
            Assert.DoesNotContain(events, value => value.Kind == ChatStreamEventKind.AssistantCompleted);
            held.SetResult();
            await producer;
            Assert.Equal(1, rig.Client.StreamDisposals);
            Assert.Single(rig.Conversations.Messages, message => message.Role == MessageRole.User);
        }
        finally
        {
            held.TrySetResult();
            child?.RequestRetirement();
            try { await (parentClose ?? parent.CloseAndDrainAsync()); } catch (Exception) { }
            if (child is not null) try { await child.CloseAndDrainAsync(); } catch (Exception) { }
            if (producer is not null) try { await producer; } catch (Exception) { }
        }
    }

    [Fact]
    public async Task Tasks_ui_late_actual_source_acquisition_is_captured_and_detached_after_view_seal()
    {
        await using var rig = new Rig(temporary: false);
        var conversation = await PersistInitialTaskConversationAsync(rig);
        var held = new TaskCompletionSource<IReadOnlyList<ChatMessage>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var probe = new InitialObservationConversationProbe(rig.Conversations) { HeldFirstHistory = held.Task };
        var service = new ChatSessionService(probe, rig.Client, new CapabilityPreflightService(), new Safety(),
            new WorkspaceToolRuntime(rig.Workspace), new ComputerToolRuntime(new Computer()),
            taskCoordinator: rig.Tasks, taskToolOwner: rig.ToolOwner, taskProviderContextCapture: rig.Capture,
            taskCloudPermissionRemediation: rig.Owner);
        NewChatOriginalInitialTaskObservation? child = null;
        Task<TaskRunOriginalInitialChatObservationLease>? rawAcquisition = null;
        Task? actualChildClose = null;
        var retiring = false;
        var publications = 0;
        var parent = new DesktopOriginalWorkLifetime(() =>
        {
            retiring = true; child!.RequestRetirement();
            actualChildClose = child.CloseAndDrainAsync(); return actualChildClose;
        }, () => Task.CompletedTask);
        var original = parent.RunAsync(async pageOriginal =>
        {
            child = new(service, pageOriginal, conversation.Id,
                () => rawAcquisition = StartInitialTaskObservationAsync(service, rig, conversation, TestContext.Current.CancellationToken),
                value => { publications++; return Task.CompletedTask; }, () => retiring,
                service.DemandExternalOriginalTaskObservationSourceJoin);
            child.BeginOriginalAcquisition();
            _ = await pageOriginal.AwaitAsync(child.ActualObservation);
        });
        Task? parentClose = null;
        TaskRunOriginalInitialChatObservationLease? lease = null;
        try
        {
            await probe.HistoryEntered.Task.WaitAsync(TestContext.Current.CancellationToken);
            Assert.False(rawAcquisition!.IsCompleted);
            parentClose = parent.CloseAndDrainAsync();
            Assert.False(parentClose.IsCompleted);
            Assert.False(actualChildClose!.IsCompleted);
            Assert.False(original.IsCompleted);
            Assert.Equal(0, publications);
            held.SetResult([]);
            lease = await rawAcquisition;
            await parentClose;
            Assert.True(service.IsIssuedOriginalTaskObservation(lease));
            Assert.False(rig.Service.IsIssuedOriginalTaskObservation(lease));
            Assert.True(original.IsCompletedSuccessfully);
            Assert.True(actualChildClose.IsCompletedSuccessfully);
            Assert.Equal(0, publications);
            Assert.Same(lease, InitialUiCapturedLease(child!));
            Assert.Equal(TaskRunInitialChatObservationDisposition.ObservationDetached, (await child!.ActualObservation)!.Disposition);
            Assert.Same(actualChildClose, child.CloseAndDrainAsync());
        }
        finally
        {
            held.TrySetResult([]);
            child?.RequestRetirement();
            try { await (parentClose ?? parent.CloseAndDrainAsync()); } catch (Exception) { }
            if (lease is not null)
            {
                try { await lease.DetachAndDrainAsync(); } catch (Exception) { }
                try { await InitialHostedProducer(lease); } catch (Exception) { }
            }
        }
    }

    [Fact]
    public async Task Tasks_ui_pending_source_guard_refuses_restored_context_after_actual_history_await()
    {
        await using var rig = new Rig(temporary: false);
        var conversation = await PersistInitialTaskConversationAsync(rig);
        var held = new TaskCompletionSource<IReadOnlyList<ChatMessage>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var probe = new InitialObservationConversationProbe(rig.Conversations) { HeldFirstHistory = held.Task };
        var service = new ChatSessionService(probe, rig.Client, new CapabilityPreflightService(), new Safety(),
            new WorkspaceToolRuntime(rig.Workspace), new ComputerToolRuntime(new Computer()),
            taskCoordinator: rig.Tasks, taskToolOwner: rig.ToolOwner, taskProviderContextCapture: rig.Capture,
            taskCloudPermissionRemediation: rig.Owner);
        var restored = ExecutionContext.Capture()!;
        NewChatOriginalInitialTaskObservation? child = null;
        Task? parentClose = null;
        Exception? refusal = null;
        var challenged = false;
        var retiring = false;
        var parent = new DesktopOriginalWorkLifetime(() =>
        { retiring = true; child!.RequestRetirement(); return child.CloseAndDrainAsync(); }, () => Task.CompletedTask);
        probe.AfterFirstHistoryGet = () =>
        {
            if (challenged) return;
            challenged = true;
            ExecutionContext.Run(restored, state =>
            {
                try { child!.DemandExternalClose(); parent.CloseAndDrainAsync().GetAwaiter().GetResult(); }
                catch (Exception cause) { refusal = cause; }
            }, null);
        };
        var original = parent.RunAsync(async pageOriginal =>
        {
            child = new(service, pageOriginal, conversation.Id,
                () => StartInitialTaskObservationAsync(service, rig, conversation, TestContext.Current.CancellationToken),
                value => Task.CompletedTask, () => retiring, service.DemandExternalOriginalTaskObservationSourceJoin);
            child.BeginOriginalAcquisition();
            _ = await pageOriginal.AwaitAsync(child.ActualObservation);
        });
        try
        {
            await probe.HistoryEntered.Task.WaitAsync(TestContext.Current.CancellationToken);
            Assert.False(original.IsCompleted);
            Assert.False(retiring);
            held.SetResult([]);
            await original;
            Assert.IsType<InvalidOperationException>(refusal);
            Assert.False(retiring);
            Assert.Single(rig.Conversations.Messages, message => message.Role == MessageRole.User);
            parentClose = parent.CloseAndDrainAsync();
            await parentClose;
            Assert.True(parentClose.IsCompletedSuccessfully);
            Assert.Equal(0, rig.Client.Dispatches);
        }
        finally
        {
            held.TrySetResult([]);
            child?.RequestRetirement();
            try { await (parentClose ?? parent.CloseAndDrainAsync()); } catch (Exception) { }
        }
    }

    [Fact]
    public async Task Tasks_ui_source_compound_faults_remain_faulted_and_every_actual_cause_survives_close()
    {
        await using var rig = new Rig(temporary: false);
        var conversation = await PersistInitialTaskConversationAsync(rig);
        var first = new OperationCanceledException("Actual source fault, not view cancellation");
        var second = new IOException("Independent raw source sibling");
        var history = new TaskCompletionSource<IReadOnlyList<ChatMessage>>(TaskCreationOptions.RunContinuationsAsynchronously);
        history.SetException([first, second]);
        rig.Conversations.OverrideHistory = history.Task;
        NewChatOriginalInitialTaskObservation? child = null;
        Task<TaskRunOriginalInitialChatObservationLease>? rawAcquisition = null;
        var parent = new DesktopOriginalWorkLifetime(() =>
        { child!.RequestRetirement(); return child.CloseAndDrainAsync(); }, () => Task.CompletedTask);
        var original = parent.RunAsync(async pageOriginal =>
        {
            child = new(rig.Service, pageOriginal, conversation.Id,
                () => rawAcquisition = StartInitialTaskObservationAsync(rig.Service, rig, conversation, TestContext.Current.CancellationToken),
                value => Task.CompletedTask, () => parent.IsRetiring, rig.Service.DemandExternalOriginalTaskObservationSourceJoin);
            child.BeginOriginalAcquisition();
            _ = await pageOriginal.AwaitAsync(child.ActualObservation);
        });
        Task? close = null;
        try
        {
            var bodyFailure = await Assert.ThrowsAnyAsync<Exception>(() => original);
            Assert.True(original.IsFaulted);
            Assert.True(rawAcquisition!.IsFaulted);
            Assert.True(history.Task.IsFaulted);
            Assert.Contains(Leaves(bodyFailure), cause => ReferenceEquals(cause, first));
            Assert.Contains(Leaves(bodyFailure), cause => ReferenceEquals(cause, second));
            close = parent.CloseAndDrainAsync();
            var closeFailure = await Assert.ThrowsAnyAsync<Exception>(() => close);
            Assert.True(close.IsFaulted);
            Assert.Contains(Leaves(closeFailure), cause => ReferenceEquals(cause, first));
            Assert.Contains(Leaves(closeFailure), cause => ReferenceEquals(cause, second));
            Assert.Empty(rig.Conversations.Messages);
            Assert.Empty(await rig.TaskRows.GetResumableAsync(TestContext.Current.CancellationToken));
            Assert.Equal(0, rig.Capture.ChatCaptures);
            Assert.Equal(0, rig.Client.Dispatches);
        }
        finally
        {
            child?.RequestRetirement();
            try { await (close ?? parent.CloseAndDrainAsync()); } catch (Exception) { }
        }
    }

    [Fact]
    public async Task Tasks_ui_parent_linked_cancellation_keeps_late_acquisition_until_actual_detach_ack()
    {
        await using var rig = new Rig(temporary: false);
        var conversation = await PersistInitialTaskConversationAsync(rig);
        var held = new TaskCompletionSource<IReadOnlyList<ChatMessage>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var probe = new InitialObservationConversationProbe(rig.Conversations) { HeldFirstHistory = held.Task };
        var service = new ChatSessionService(probe, rig.Client, new CapabilityPreflightService(), new Safety(),
            new WorkspaceToolRuntime(rig.Workspace), new ComputerToolRuntime(new Computer()),
            taskCoordinator: rig.Tasks, taskToolOwner: rig.ToolOwner, taskProviderContextCapture: rig.Capture,
            taskCloudPermissionRemediation: rig.Owner);
        NewChatOriginalInitialTaskObservation? child = null;
        Task<TaskRunOriginalInitialChatObservationLease>? rawAcquisition = null;
        Task? childClose = null;
        CancellationTokenSource? actualObservationToken = null;
        var retiring = false;
        var publications = 0;
        var parent = new DesktopOriginalWorkLifetime(() =>
        { retiring = true; child!.RequestRetirement(); childClose = child.CloseAndDrainAsync(); return childClose; },
            () => Task.CompletedTask);
        var original = parent.RunAsync(async pageOriginal =>
        {
            actualObservationToken = CancellationTokenSource.CreateLinkedTokenSource(pageOriginal.Token, TestContext.Current.CancellationToken);
            try
            {
                child = new(service, pageOriginal, conversation.Id,
                    () => rawAcquisition = StartInitialTaskObservationAsync(service, rig, conversation, actualObservationToken.Token),
                    value => { publications++; return Task.CompletedTask; }, () => retiring,
                    service.DemandExternalOriginalTaskObservationSourceJoin);
                child.BeginOriginalAcquisition();
                _ = await pageOriginal.AwaitAsync(child.ActualObservation);
            }
            finally { actualObservationToken.Dispose(); }
        });
        Task? parentClose = null;
        TaskRunOriginalInitialChatObservationLease? lease = null;
        Task? producer = null;
        try
        {
            await probe.HistoryEntered.Task.WaitAsync(TestContext.Current.CancellationToken);
            parentClose = parent.CloseAndDrainAsync();
            Assert.True(actualObservationToken!.IsCancellationRequested);
            Assert.False(rawAcquisition!.IsCompleted);
            Assert.False(childClose!.IsCompleted);
            Assert.False(parentClose.IsCompleted);
            Assert.False(held.Task.IsCanceled);
            Assert.Equal(0, publications);
            held.SetResult([]);
            lease = await rawAcquisition;
            producer = InitialHostedProducer(lease);
            await parentClose;
            Assert.True(rawAcquisition.IsCompletedSuccessfully);
            Assert.True(original.IsCompletedSuccessfully);
            Assert.True(parentClose.IsCompletedSuccessfully);
            Assert.True(childClose.IsCompletedSuccessfully);
            Assert.Same(lease, InitialUiCapturedLease(child!));
            Assert.True(service.IsIssuedOriginalTaskObservation(lease));
            Assert.Equal(TaskRunInitialChatObservationDisposition.ObservationDetached, (await child!.ActualObservation)!.Disposition);
            Assert.Equal(0, publications);
            await producer;
            var actual = (await rig.Tasks.GetByContextAsync(conversation.Id, TestContext.Current.CancellationToken))!;
            Assert.Equal(actual.TaskId, lease.CurrentAcknowledgedContext!.TaskId);
            Assert.Equal(actual.ExecutionId, lease.CurrentAcknowledgedContext.ExecutionId);
            Assert.Equal(TaskExecutionLifecycle.Suspended, actual.State);
            Assert.Single(rig.Conversations.Messages, value => value.Role == MessageRole.User);
            Assert.Equal(0, rig.Client.Dispatches);
        }
        finally
        {
            held.TrySetResult([]);
            child?.RequestRetirement();
            try { await (parentClose ?? parent.CloseAndDrainAsync()); } catch (Exception) { }
            if (child is not null) try { await child.CloseAndDrainAsync(); } catch (Exception) { }
            if (producer is not null) try { await producer; } catch (Exception) { }
        }
    }

    [Fact]
    public async Task Tasks_ui_natural_terminal_observation_keeps_admitted_final_presentation_before_cleanup()
    {
        await using var rig = new Rig(temporary: false);
        var conversation = await PersistInitialTaskConversationAsync(rig);
        NewChatOriginalInitialTaskObservation? child = null;
        var publications = new List<ChatStreamEvent>();
        var postTerminalPublications = 0;
        var parent = new DesktopOriginalWorkLifetime(() =>
        { child!.RequestRetirement(); return child.CloseAndDrainAsync(); }, () => Task.CompletedTask);
        var original = parent.RunAsync(async pageOriginal =>
        {
            child = new(rig.Service, pageOriginal, conversation.Id,
                () => StartInitialTaskObservationAsync(rig.Service, rig, conversation, TestContext.Current.CancellationToken),
                value => { publications.Add(value); return Task.CompletedTask; }, () => parent.IsRetiring,
                rig.Service.DemandExternalOriginalTaskObservationSourceJoin);
            child.BeginOriginalAcquisition();
            var result = await pageOriginal.AwaitAsync(child.ActualObservation);
            Assert.Equal(TaskRunInitialChatObservationDisposition.ProducerTerminal, result!.Disposition);
            Assert.False(child.IsPresentationRetiring);
            pageOriginal.DemandPublication();
            child.InvokeOriginalPresentationCallback(() => postTerminalPublications++);
            child.RequestRetirement();
            await pageOriginal.AwaitAsync(child.CloseAndDrainAsync());
        });
        Task? close = null;
        try
        {
            await original;
            Assert.True(original.IsCompletedSuccessfully);
            Assert.Equal(1, postTerminalPublications);
            var permission = Assert.Single(publications, value => value.Kind == ChatStreamEventKind.PermissionRequired);
            Assert.Equal(conversation.Id, permission.CanonicalTaskContext!.ContextId);
            Assert.True(child!.CanPruneHealthy);
            close = parent.CloseAndDrainAsync();
            await close;
            Assert.True(close.IsCompletedSuccessfully);
            var same = (await rig.Tasks.GetByContextAsync(conversation.Id, TestContext.Current.CancellationToken))!;
            Assert.Equal(TaskExecutionLifecycle.Suspended, same.State);
            Assert.Single(rig.Conversations.Messages, value => value.Role == MessageRole.User);
            Assert.DoesNotContain(publications, value => value.Kind == ChatStreamEventKind.AssistantCompleted);
            Assert.Equal(0, rig.Client.Dispatches);
        }
        finally
        {
            child?.RequestRetirement();
            try { await (close ?? parent.CloseAndDrainAsync()); } catch (Exception) { }
            if (child is not null) try { await child.CloseAndDrainAsync(); } catch (Exception) { }
        }
    }

    [Fact]
    public async Task Tasks_ui_business_snapshot_callback_requests_view_retirement_without_business_join_dependency()
    {
        await using var rig = new Rig(temporary: false);
        rig.Capture.AskDuringCapture = false;
        var held = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        rig.Client.OriginalDispose = held.Task;
        var conversation = await PersistInitialTaskConversationAsync(rig);
        NewChatOriginalInitialTaskObservation? child = null;
        Task<TaskRunOriginalInitialChatObservationLease>? rawAcquisition = null;
        Task? childClose = null;
        Task? producer = null;
        var retiring = false;
        var callbackObservedActualPendingProducer = false;
        var parent = new DesktopOriginalWorkLifetime(() =>
        { retiring = true; child!.RequestRetirement(); childClose = child.CloseAndDrainAsync(); return childClose; },
            () => Task.CompletedTask);
        void OnActualSnapshot(object? sender, TaskExecutionSnapshot actual)
        {
            if (actual.ContextId != conversation.Id || actual.State != TaskExecutionLifecycle.Suspended || producer is null) return;
            callbackObservedActualPendingProducer = !producer.IsCompleted;
            child!.DemandExternalClose(); // Observer joins do not acquire the live business producer.
            parent.RequestRetirement(); // The actual callback returns; an external caller joins its parent later.
        }
        rig.Tasks.SnapshotChanged += OnActualSnapshot;
        var original = parent.RunAsync(async pageOriginal =>
        {
            child = new(rig.Service, pageOriginal, conversation.Id,
                () => rawAcquisition = StartInitialTaskObservationAsync(rig.Service, rig, conversation, TestContext.Current.CancellationToken),
                value => Task.CompletedTask, () => retiring, rig.Service.DemandExternalOriginalTaskObservationSourceJoin);
            child.BeginOriginalAcquisition();
            _ = await pageOriginal.AwaitAsync(child.ActualObservation);
        });
        Task? close = null;
        try
        {
            await rig.Client.DisposeEntered.Task.WaitAsync(TestContext.Current.CancellationToken);
            var lease = await rawAcquisition!;
            producer = InitialHostedProducer(lease);
            Assert.False(producer.IsCompleted);
            Assert.False(original.IsCompleted);
            held.SetResult();
            await producer;
            Assert.True(callbackObservedActualPendingProducer);
            Assert.True(retiring);
            close = parent.CloseAndDrainAsync();
            await close;
            Assert.True(close.IsCompletedSuccessfully);
            Assert.True(original.IsCompletedSuccessfully);
            Assert.True(childClose!.IsCompletedSuccessfully);
            Assert.Equal(TaskRunInitialChatObservationDisposition.ObservationDetached, (await child!.ActualObservation)!.Disposition);
            Assert.Same(childClose, child.CloseAndDrainAsync());
            var actual = (await rig.Tasks.GetByContextAsync(conversation.Id, TestContext.Current.CancellationToken))!;
            Assert.Equal(TaskExecutionLifecycle.Suspended, actual.State);
            Assert.Equal(actual.TaskId, lease.CurrentAcknowledgedContext!.TaskId);
            Assert.Equal(actual.ExecutionId, lease.CurrentAcknowledgedContext.ExecutionId);
            Assert.Equal(1, rig.Client.StreamDisposals);
        }
        finally
        {
            rig.Tasks.SnapshotChanged -= OnActualSnapshot;
            held.TrySetResult();
            child?.RequestRetirement();
            try { await (close ?? parent.CloseAndDrainAsync()); } catch (Exception) { }
            if (producer is not null) try { await producer; } catch (Exception) { }
        }
    }

    private static TaskRunOriginalInitialChatObservationLease? InitialUiCapturedLease(NewChatOriginalInitialTaskObservation actual) =>
        (TaskRunOriginalInitialChatObservationLease?)typeof(NewChatOriginalInitialTaskObservation)
            .GetField("_lease", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(actual);
}
