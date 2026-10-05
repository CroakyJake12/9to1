using System.Runtime.CompilerServices;
using System.Reflection;
using System.Text.Json;
using Haven.Application;
using Haven.Core;
using Xunit;

namespace Haven.Desktop.Tests;

/// <summary>Actual Chat producer and coordinator/frame owner; controlled repository/actor/provider only, never cloud or installed authority proof.</summary>
public sealed class ChatCanonicalContextProducerTests : IDisposable
{
    private static readonly AsyncLocal<ChatCanonicalContextProducerTests?> OriginalFixtureOwner = new();
    private readonly List<string> _ownedWorkspaces = [];
    public ChatCanonicalContextProducerTests() => OriginalFixtureOwner.Value = this;
    public void Dispose()
    {
        foreach (var root in _ownedWorkspaces) Directory.Delete(root, recursive: true);
        if (ReferenceEquals(OriginalFixtureOwner.Value, this)) OriginalFixtureOwner.Value = null;
    }

    [Fact]
    public async Task Persisted_capture_uses_actual_saved_conversation_and_original_selected_history_on_same_request()
    {
        var h = Harness.Create(temporary: false);
        var old = new ChatMessage(Guid.NewGuid(), h.Conversation.Id, MessageRole.User, new string('x', 9000),
            null, null, null, h.Conversation.CreatedAt);
        var recent = new ChatMessage(Guid.NewGuid(), h.Conversation.Id, MessageRole.Assistant, "original selected record",
            "actual-agent", h.Model.Name, "{\"source\":\"original\"}", h.Conversation.CreatedAt.AddMinutes(1));
        h.Conversations.Messages.AddRange([old, recent]);
        await h.RunAsync("hello", new GenerationOptions(ContextLimit: 2000));
        var capture = Assert.Single(h.Capture.Chat);
        Assert.Same(h.Provider.ChatRequest, capture.Request);
        Assert.Same(h.Conversations.Current, capture.Inventory.OriginalConversation);
        Assert.NotEqual(h.Conversation.UpdatedAt, capture.Inventory.OriginalConversation.UpdatedAt);
        Assert.Contains(capture.Inventory.OriginalHistory, value => ReferenceEquals(value, recent));
        Assert.DoesNotContain(capture.Inventory.OriginalHistory, value => value.Id == old.Id);
        Assert.Contains(capture.Request.Messages, value => value.Content == recent.Content);
        Assert.Empty(capture.Inventory.SelectedBackgroundLearning);
        Assert.Null(capture.Inventory.OriginalBackgroundScopes);
        Assert.Equal(h.Service.CurrentCanonicalTask!.TaskId, capture.Current.TaskId);
        Assert.Equal(TaskExecutionLifecycle.Completed, h.Service.CurrentCanonicalTask.State);
        Assert.Equal(1, h.Provider.Frames);
        Assert.Equal(1, h.Authority.Lease!.Disposes);
        await h.Runtime.CloseAndDrainAsync();
    }

    [Fact]
    public async Task Temporary_agent_context_remains_actual_transient_object_without_invented_persistence()
    {
        var h = Harness.Create(temporary: true);
        await h.RunAsync("temporary context");
        var capture = Assert.Single(h.Capture.Chat);
        Assert.Same(h.Conversation, capture.Inventory.OriginalConversation);
        Assert.True(capture.Inventory.OriginalConversation.IsTemporary);
        Assert.Equal(0, h.Conversations.Writes);
        Assert.Equal(0, h.Conversations.Reads);
        Assert.Same(h.Provider.ChatRequest, capture.Request);
        Assert.Equal(TaskExecutionLifecycle.Completed, h.Service.CurrentCanonicalTask!.State);
        await h.Runtime.CloseAndDrainAsync();
    }

    [Fact]
    public async Task Unknown_effective_model_refuses_canonical_tool_calls_before_any_owner_or_workspace_effect()
    {
        var h = Harness.Create(temporary: true, tools: true);
        await Assert.ThrowsAsync<InvalidOperationException>(() => h.RunAsync("Create the file"));
        var capture = Assert.Single(h.Capture.Tools);
        Assert.Same(h.Provider.ToolRequest, capture.Request);
        Assert.NotNull(capture.Request.ExecutionContext);
        Assert.Equal(h.Service.CurrentCanonicalTask!.TaskId, capture.Request.ExecutionContext!.TaskId);
        Assert.Equal(0, h.ToolOwner.Preparations);
        Assert.Equal(0, h.Workspace.Effects);
        Assert.Equal(0, h.Provider.CompatibilityCompletions);
        // This source proves the pre-effect refusal, not yet the separate orchestration suspension successor.
        Assert.NotEqual(TaskExecutionLifecycle.Completed, (await h.Coordinator.GetAsync(capture.Current.TaskId, default))!.State);
        await h.Runtime.CloseAndDrainAsync();
    }

    [Fact]
    public async Task Context_owner_refusal_prevents_actual_provider_body_and_preserves_original_failure()
    {
        var h = Harness.Create(temporary: true);
        var failure = new UnauthorizedAccessException("actual controlled context-owner refusal");
        h.Capture.Refusal = failure;
        var observed = await Record.ExceptionAsync(() => h.RunAsync("private context"));
        Assert.Same(failure, observed);
        Assert.Equal(0, h.Provider.Frames);
        Assert.Null(h.Provider.ChatRequest);
        Assert.Empty(h.Service.CurrentCanonicalTask!.Attempts);
        Assert.Equal(0, h.Workspace.Effects);
        await h.Runtime.CloseAndDrainAsync();
    }

    [Fact]
    public async Task Revoked_context_call_publishes_suspension_from_original_custody_without_new_actor_or_attempt()
    {
        var h = Harness.Create(temporary: true);
        var actual = new UnauthorizedAccessException("Actual original actor/context revoked");
        h.Capture.BeforeCapture = () => h.Authority.AllowCurrentActor = false;
        h.Capture.Refusal = actual;
        Assert.Same(actual, await Record.ExceptionAsync(() => h.RunAsync("owned private context")));
        var saved = await h.Coordinator.GetAsync(h.Service.CurrentCanonicalTask!.TaskId, default);
        Assert.Equal(TaskExecutionLifecycle.Suspended, saved!.State);
        Assert.Equal(h.Service.CurrentCanonicalTask.ExecutionId, saved.ExecutionId);
        Assert.Equal(TaskRunOriginalSettlementOutcome.NoAttemptAdmissionWasInvoked, saved.RecoveryObservation!.SettlementOutcome);
        Assert.Equal(TaskRunIteratorTerminalOutcome.Failed, saved.RecoveryObservation.IteratorOutcome);
        Assert.Contains(saved.RecoveryObservation.Causes, cause => cause.Message == actual.Message);
        Assert.Equal(0, h.Authority.AttemptChecks);
        Assert.Equal(0, h.Provider.Frames);
        await Assert.ThrowsAsync<InvalidOperationException>(() => h.Coordinator.StartAttemptAsync(saved.TaskId, saved.ExecutionId,
            new TaskRunRouteCandidate("synthetic-route", 1, "synthetic", h.Model.Name, null, false, ["Text"]), default));
        await h.Runtime.CloseAndDrainAsync();
    }

    [Fact]
    public async Task Body_and_tracker_callback_cleanup_causes_survive_as_separate_original_siblings()
    {
        var h = Harness.Create(temporary: true);
        var body = new UnauthorizedAccessException("Exact original context failure");
        var cleanup = new IOException("Exact original tracker Cancel observer failure");
        h.Capture.Refusal = body;
        h.Service.ExecutionChanged += snapshot =>
        {
            if (snapshot.Stage == ChatExecutionStage.Cancelled) throw cleanup;
        };
        var observed = await Record.ExceptionAsync(() => h.RunAsync("actual source call"));
        Assert.IsType<AggregateException>(observed);
        Assert.Contains(OriginalCauses(observed!), cause => ReferenceEquals(cause, body));
        Assert.Contains(OriginalCauses(observed!), cause => ReferenceEquals(cause, cleanup));
        var saved = await h.Coordinator.GetAsync(h.Service.CurrentCanonicalTask!.TaskId, default);
        Assert.Equal(TaskExecutionLifecycle.Suspended, saved!.State);
        Assert.Contains(saved.RecoveryObservation!.Causes, cause => cause.Message == body.Message);
        Assert.Contains(saved.RecoveryObservation.Causes, cause => cause.Message == cleanup.Message);
        Assert.Equal(0, h.Provider.Frames);
        await h.Runtime.CloseAndDrainAsync();
    }

    [Fact]
    public async Task Actual_early_iterator_dispose_suspends_original_run_without_fabricated_provider_or_completed_state()
    {
        var h = Harness.Create(temporary: true);
        var iterator = h.Service.SendAsync(h.Conversation, "actual queued input", h.Model, EffortLevel.Medium, [], "controlled", "",
            DuoMode.Solo, "synthetic-workspace", null, null, null, default,
            taskExecutionIntent: TaskRunExecutionIntent.CanonicalAgenticTask).GetAsyncEnumerator();
        while (await iterator.MoveNextAsync())
            if (iterator.Current.Kind == ChatStreamEventKind.AssistantStarted) break;
        var originalDispose = iterator.DisposeAsync().AsTask();
        await originalDispose;
        Assert.True(originalDispose.IsCompletedSuccessfully);
        var saved = await h.Coordinator.GetAsync(h.Service.CurrentCanonicalTask!.TaskId, default);
        Assert.Equal(TaskExecutionLifecycle.Suspended, saved!.State);
        Assert.Equal(TaskRunIteratorTerminalOutcome.ClosedBeforeEnd, saved.RecoveryObservation!.IteratorOutcome);
        Assert.Equal(TaskRunOriginalSettlementOutcome.NoAttemptAdmissionWasInvoked, saved.RecoveryObservation.SettlementOutcome);
        Assert.Empty(saved.Attempts);
        Assert.Equal(0, h.Provider.Frames);
        Assert.Equal(0, h.Workspace.Effects);
        await h.Runtime.CloseAndDrainAsync();
    }

    [Fact]
    public async Task Accepted_action_checkpoint_survives_unknown_effective_model_and_actual_lease_cleanup_failure()
    {
        var h = Harness.Create(temporary: true, tools: true);
        var cleanup = new IOException("Exact issued lease cleanup failure");
        var acceptedAction = Guid.NewGuid();
        Guid originalTask = default, originalRun = default, originalAttempt = default;
        h.Provider.BeforeToolResponse = async admission =>
        {
            originalTask = admission.Snapshot.TaskId; originalRun = admission.Snapshot.ExecutionId; originalAttempt = admission.AttemptId;
            h.Authority.AcceptedAction = acceptedAction; h.Authority.AcceptedAttempt = admission.AttemptId;
            await h.Coordinator.RegisterActionAsync(originalTask, acceptedAction, null, "controlled owner accepted original work",
                TaskActionInterruptionPolicy.AtomicCommit, null, [], default, expectedAttemptId: admission.AttemptId);
            await h.Coordinator.AcceptActionAsync(originalTask, originalRun, originalAttempt, acceptedAction,
                "controlled-exact-owner-receipt", default);
            h.Authority.Lease!.CleanupFailure = cleanup;
        };
        var observed = await Record.ExceptionAsync(() => h.RunAsync("Create the file"));
        Assert.Contains(OriginalCauses(observed!), cause => ReferenceEquals(cause, cleanup));
        Assert.Contains(OriginalCauses(observed!), cause => cause.Message.Contains("effective model is unknown", StringComparison.Ordinal));
        var saved = await h.Coordinator.GetAsync(originalTask, default);
        Assert.Equal(originalTask, saved!.TaskId); Assert.Equal(originalRun, saved.ExecutionId);
        Assert.Equal(originalAttempt, Assert.Single(saved.Attempts).Id);
        Assert.Equal(TaskPlanNodeState.Completed, Assert.Single(saved.Plan).State);
        Assert.NotNull(saved.Plan.Single().Acceptance);
        Assert.Equal(acceptedAction, saved.LastCheckpointActionId);
        Assert.Equal(TaskExecutionLifecycle.Suspended, saved.State);
        Assert.Equal(TaskRunOriginalSettlementOutcome.Failed, saved.RecoveryObservation!.SettlementOutcome);
        Assert.Equal(1, h.Authority.Lease!.Disposes);
        await Assert.ThrowsAsync<InvalidOperationException>(() => h.Coordinator.ResumeAttemptAsync(originalTask, originalRun, originalAttempt,
            new TaskRunRouteCandidate("another-route", 2, "synthetic", h.Model.Name, null, false, ["Text"]), default));
        Assert.Contains(OriginalCauses((await Record.ExceptionAsync(() => h.Runtime.CloseAndDrainAsync()))!),
            cause => ReferenceEquals(cause, cleanup));
    }

    [Fact]
    public async Task Suspension_CAS_loss_preserves_actual_fault_and_concurrent_queue_and_refuses_live_original_replay()
    {
        var h = Harness.Create(temporary: true);
        var body = new UnauthorizedAccessException("Exact context-owner refusal before any frame");
        h.Capture.Refusal = body;
        h.TaskRepository.BeforeSuspension = async () =>
        {
            await h.Coordinator.SubmitFollowUpAsync(h.Service.CurrentCanonicalTask!.TaskId, "Actual concurrent follow-up",
                TaskFollowUpMode.Queue, null, [], default);
        };
        var observed = await Record.ExceptionAsync(() => h.RunAsync("actual original"));
        Assert.Contains(OriginalCauses(observed!), cause => ReferenceEquals(cause, body));
        Assert.Contains(OriginalCauses(observed!), cause => cause is TaskExecutionRevisionConflictException);
        var saved = await h.Coordinator.GetAsync(h.Service.CurrentCanonicalTask!.TaskId, default);
        Assert.Single(saved!.Queue);
        Assert.Null(saved.RecoveryObservation); // A lost CAS is not represented as an acknowledged projection.
        Assert.NotEqual(TaskExecutionLifecycle.Completed, saved.State);
        Assert.Contains(h.Coordinator.ObservationFailures, cause => cause.OriginalException is TaskExecutionRevisionConflictException);
        await Assert.ThrowsAsync<InvalidOperationException>(() => h.Coordinator.StartAttemptAsync(saved.TaskId, saved.ExecutionId,
            new TaskRunRouteCandidate("synthetic-route", 1, "synthetic", h.Model.Name, null, false, ["Text"]), default));
        Assert.Equal(0, h.Provider.Frames);
        Assert.Equal(0, h.Authority.AttemptChecks);
        await h.Runtime.CloseAndDrainAsync();
    }

    [Fact]
    public async Task Tracker_close_joins_same_original_timer_and_retains_callback_fault_on_exact_idempotent_close_task()
    {
        var tracker = new ChatExecutionTracker();
        var actual = new IOException("Actual original changed-callback close fault");
        tracker.Changed += _ => throw actual;
        var original = tracker.DisposeAsync().AsTask();
        var repeated = tracker.DisposeAsync().AsTask();
        Assert.Same(original, repeated);
        Assert.Same(actual, await Record.ExceptionAsync(() => original));
        Assert.Same(actual, await Record.ExceptionAsync(() => repeated));
        Assert.True(original.IsCompleted);
        Assert.Equal(ChatExecutionStage.Cancelled, tracker.Snapshot.Stage);
    }

    [Fact]
    public async Task Tracker_callback_OCE_is_original_faulted_cause_on_same_close_task_not_owner_cancellation()
    {
        var tracker = new ChatExecutionTracker();
        var actual = new OperationCanceledException("Exact synchronous callback OCE, not canceled owner evidence");
        tracker.Changed += _ => throw actual;
        var original = tracker.DisposeAsync().AsTask();
        var repeated = tracker.DisposeAsync().AsTask();
        var observed = await Record.ExceptionAsync(() => original);
        Assert.Same(original, repeated);
        Assert.True(original.IsFaulted);
        Assert.False(original.IsCanceled);
        Assert.Contains(OriginalCauses(observed!), cause => ReferenceEquals(cause, actual));
        Assert.Contains(original.Exception!.Flatten().InnerExceptions, cause => ReferenceEquals(cause, actual));
    }

    [Fact]
    public async Task Actual_faulted_context_task_OCE_stays_faulted_and_exact_cause_is_durable_without_provider_effect()
    {
        var h = Harness.Create(temporary: true);
        var actual = new OperationCanceledException("Exact original faulted context Task cause");
        var actualCapture = Task.FromException(actual);
        h.Capture.OriginalFailureTask = actualCapture;
        var original = h.RunAsync("actual faulted capture");
        var observed = await Record.ExceptionAsync(() => original);
        Assert.True(actualCapture.IsFaulted);
        Assert.True(original.IsFaulted);
        Assert.False(original.IsCanceled);
        Assert.Contains(OriginalCauses(observed!), cause => ReferenceEquals(cause, actual));
        var current = (await h.Coordinator.GetAsync(h.Service.CurrentCanonicalTask!.TaskId, default))!;
        Assert.Equal(TaskExecutionLifecycle.Suspended, current.State);
        Assert.Contains(current.RecoveryObservation!.Causes, cause => cause.Message == actual.Message);
        Assert.Equal(0, h.Provider.Frames);
        await h.Runtime.CloseAndDrainAsync();
    }

    [Fact]
    public async Task Genuine_canceled_context_task_remains_cancellation_and_never_becomes_completed()
    {
        var h = Harness.Create(temporary: true);
        using var stopped = new CancellationTokenSource();
        stopped.Cancel();
        var actualCapture = Task.FromCanceled(stopped.Token);
        h.Capture.OriginalFailureTask = actualCapture;
        var original = h.RunAsync("actual canceled capture");
        var observed = await Record.ExceptionAsync(() => original);
        Assert.True(actualCapture.IsCanceled);
        Assert.True(original.IsCanceled);
        Assert.IsAssignableFrom<OperationCanceledException>(observed);
        Assert.Equal(TaskExecutionLifecycle.Suspended, h.Service.CurrentCanonicalTask!.State);
        Assert.Equal(0, h.Provider.Frames);
        await h.Runtime.CloseAndDrainAsync();
    }

    [Fact]
    public async Task Canonical_completed_task_and_final_response_wait_for_actual_provider_finally_and_lease_cleanup()
    {
        var h = Harness.Create(temporary: true);
        var bodyFinallyEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseBodyFinally = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var leaseCloseEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseLeaseClose = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        h.Provider.OnStreamFinally = async () => { bodyFinallyEntered.SetResult(); await releaseBodyFinally.Task; };
        h.Authority.OnLeaseClose = async () => { leaseCloseEntered.SetResult(); await releaseLeaseClose.Task; };
        var completed = new List<ChatExecutionSnapshot>();
        h.Service.ExecutionChanged += snapshot => { if (snapshot.Stage == ChatExecutionStage.Completed) completed.Add(snapshot); };
        var original = h.RunAsync("held owning originals");
        try
        {
            await bodyFinallyEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(original.IsCompleted);
            Assert.Empty(completed);
            Assert.DoesNotContain(h.Events, value => value.Kind == ChatStreamEventKind.AssistantCompleted);
            Assert.Equal(TaskExecutionLifecycle.Running, (await h.Coordinator.GetAsync(h.Service.CurrentCanonicalTask!.TaskId, default))!.State);
            releaseBodyFinally.SetResult();
            await leaseCloseEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(original.IsCompleted);
            Assert.Empty(completed);
            Assert.DoesNotContain(h.Events, value => value.Kind == ChatStreamEventKind.AssistantCompleted);
            Assert.Equal(TaskExecutionLifecycle.Running, (await h.Coordinator.GetAsync(h.Service.CurrentCanonicalTask!.TaskId, default))!.State);
        }
        finally
        {
            releaseBodyFinally.TrySetResult();
            releaseLeaseClose.TrySetResult();
            await original;
        }
        Assert.Equal(TaskExecutionLifecycle.Completed, h.Service.CurrentCanonicalTask!.State);
        Assert.Single(completed);
        Assert.Single(h.Events.Where(value => value.Kind == ChatStreamEventKind.AssistantCompleted));
        Assert.Equal(0, h.Coordinator.LiveOriginalInvocationCount);
        await h.Runtime.CloseAndDrainAsync();
    }

    [Fact]
    public async Task Actual_lease_cleanup_fault_prevents_any_completed_task_or_final_response_publication()
    {
        var h = Harness.Create(temporary: true);
        var actual = new IOException("Actual lease close failure after generated body");
        h.Authority.OnLeaseClose = () => Task.FromException(actual);
        var completed = new List<ChatExecutionSnapshot>();
        h.Service.ExecutionChanged += snapshot => { if (snapshot.Stage == ChatExecutionStage.Completed) completed.Add(snapshot); };
        var observed = await Record.ExceptionAsync(() => h.RunAsync("generated output with failed cleanup"));
        Assert.Contains(OriginalCauses(observed!), cause => ReferenceEquals(cause, actual));
        Assert.Empty(completed);
        Assert.DoesNotContain(h.Events, value => value.Kind == ChatStreamEventKind.AssistantCompleted);
        Assert.Equal(TaskExecutionLifecycle.Suspended, h.Service.CurrentCanonicalTask!.State);
        Assert.Contains(h.Service.CurrentCanonicalTask.RecoveryObservation!.Causes, cause => cause.Message == actual.Message);
        Assert.Contains(OriginalCauses((await Record.ExceptionAsync(() => h.Runtime.CloseAndDrainAsync()))!),
            cause => ReferenceEquals(cause, actual));
    }

    [Fact]
    public async Task Completion_observer_fault_after_ack_is_reported_without_undoing_or_replaying_accepted_completion()
    {
        var h = Harness.Create(temporary: true);
        var actual = new IOException("Exact acknowledged completed UI observer failure");
        h.Service.ExecutionChanged += snapshot => { if (snapshot.Stage == ChatExecutionStage.Completed) throw actual; };
        await h.RunAsync("actual final observer");
        Assert.Equal(TaskExecutionLifecycle.Completed, h.Service.CurrentCanonicalTask!.State);
        Assert.Null(h.Service.CurrentCanonicalTask.RecoveryObservation);
        Assert.Single(h.Events.Where(value => value.Kind == ChatStreamEventKind.AssistantCompleted));
        Assert.Contains(h.Coordinator.ObservationFailures, value => ReferenceEquals(value.OriginalException, actual));
        Assert.Equal(1, h.Provider.Frames);
        Assert.Equal(0, h.Coordinator.LiveOriginalInvocationCount);
        await h.Runtime.CloseAndDrainAsync();
    }

    [Fact]
    public async Task More_than_128_healthy_original_chat_invocations_retire_actual_custody_and_keep_working()
    {
        var h = Harness.Create(temporary: true);
        for (var index = 0; index < TaskExecutionCoordinator.OriginalInvocationCapacity + 8; index++)
        {
            h.Capture.Chat.Clear();
            h.Events.Clear();
            await h.RunAsync("healthy original " + index);
            Assert.Equal(TaskExecutionLifecycle.Completed, h.Service.CurrentCanonicalTask!.State);
            Assert.Equal(0, h.Coordinator.LiveOriginalInvocationCount);
            Assert.Equal(1, h.Authority.Lease!.Disposes);
        }
        Assert.Equal(TaskExecutionCoordinator.OriginalInvocationCapacity + 8, h.Provider.Frames);
        Assert.Equal(h.Provider.Frames, h.TaskRepository.RowCount);
        await h.Runtime.CloseAndDrainAsync();
    }

    [Fact]
    public async Task Retained_128_failed_originals_refuse_further_task_admission_before_authority_or_effect()
    {
        var h = Harness.Create(temporary: true);
        var exact = new UnauthorizedAccessException("Exact original unresolved context failure");
        h.Capture.Refusal = exact;
        for (var index = 0; index < TaskExecutionCoordinator.OriginalInvocationCapacity; index++)
            Assert.Same(exact, await Record.ExceptionAsync(() => h.RunAsync("unresolved original " + index)));
        Assert.Equal(TaskExecutionCoordinator.OriginalInvocationCapacity, h.Coordinator.LiveOriginalInvocationCount);
        Assert.Equal(TaskExecutionCoordinator.OriginalInvocationCapacity, h.TaskRepository.RowCount);
        Assert.Equal(TaskExecutionCoordinator.OriginalInvocationCapacity, h.Authority.StartChecks);
        await Assert.ThrowsAsync<InvalidOperationException>(() => h.RunAsync("refused extra original"));
        Assert.Equal(TaskExecutionCoordinator.OriginalInvocationCapacity, h.Coordinator.LiveOriginalInvocationCount);
        Assert.Equal(TaskExecutionCoordinator.OriginalInvocationCapacity, h.TaskRepository.RowCount);
        Assert.Equal(TaskExecutionCoordinator.OriginalInvocationCapacity, h.Authority.StartChecks);
        Assert.Equal(0, h.Provider.Frames);
        await h.Runtime.CloseAndDrainAsync();
    }

    [Fact]
    public async Task Actual_visibility_timer_callback_OCE_is_retained_fault_before_async_cancellation_boundary()
    {
        var tracker = new ChatExecutionTracker();
        var actual = new OperationCanceledException("Exact actual visibility callback OCE");
        var invoked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        tracker.Changed += snapshot =>
        {
            if (snapshot.IsVisible && snapshot.Stage != ChatExecutionStage.Cancelled)
            { invoked.TrySetResult(); throw actual; }
        };
        Task? originalClose = null;
        Exception? observed = null;
        try { await invoked.Task.WaitAsync(ChatExecutionTracker.VisibilityDelay + TimeSpan.FromSeconds(5)); }
        finally
        {
            originalClose = tracker.DisposeAsync().AsTask();
            observed = await Record.ExceptionAsync(() => originalClose);
        }
        var actualClose = originalClose ?? throw new InvalidOperationException("The owning close was not invoked.");
        Assert.True(actualClose.IsFaulted);
        Assert.False(actualClose.IsCanceled);
        Assert.Contains(OriginalCauses(observed!), cause => ReferenceEquals(cause, actual));
        Assert.Same(actualClose, tracker.DisposeAsync().AsTask());
    }

    [Fact]
    public async Task Canonical_tracker_callback_OCE_survives_as_exact_public_cause_and_nested_durable_diagnostic()
    {
        var h = Harness.Create(temporary: true);
        var actualBody = new UnauthorizedAccessException("Actual context rejection before any provider");
        var actualCallback = new OperationCanceledException("Exact original Cancel observer OCE, not cancellation authority");
        h.Capture.Refusal = actualBody;
        h.Service.ExecutionChanged += snapshot => { if (snapshot.Stage == ChatExecutionStage.Cancelled) throw actualCallback; };
        var original = h.RunAsync("actual callback and body causes");
        var observed = await Record.ExceptionAsync(() => original);
        Assert.True(original.IsFaulted);
        Assert.Contains(OriginalCauses(observed!), cause => ReferenceEquals(cause, actualBody));
        Assert.Contains(OriginalCauses(observed!), cause => ReferenceEquals(cause, actualCallback));
        Assert.Equal(TaskExecutionLifecycle.Suspended, h.Service.CurrentCanonicalTask!.State);
        Assert.Contains(h.Service.CurrentCanonicalTask.RecoveryObservation!.Causes, value => value.Message == actualCallback.Message);
        Assert.Contains(h.Service.CurrentCanonicalTask.RecoveryObservation.Causes, value => value.Message == actualBody.Message);
        Assert.Equal(0, h.Provider.Frames);
        await h.Runtime.CloseAndDrainAsync();
    }

    [Fact]
    public async Task Actual_close_callback_self_join_is_refused_without_deadlock_and_external_close_keeps_same_task()
    {
        var tracker = new ChatExecutionTracker();
        Exception? actualRefusal = null;
        tracker.Changed += _ =>
        {
            actualRefusal = Record.Exception(() => tracker.DisposeAsync().AsTask().GetAwaiter().GetResult());
            tracker.RequestStop();
        };
        var actualClose = tracker.DisposeAsync().AsTask();
        await actualClose.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.IsType<InvalidOperationException>(actualRefusal);
        Assert.Same(actualClose, tracker.DisposeAsync().AsTask());
        Assert.True(actualClose.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task Actual_timer_callback_requests_stop_without_joining_itself_and_external_close_joins_original()
    {
        var tracker = new ChatExecutionTracker();
        var invoked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Exception? actualRefusal = null;
        tracker.Changed += snapshot =>
        {
            if (!snapshot.IsVisible || snapshot.Stage == ChatExecutionStage.Cancelled) return;
            actualRefusal = Record.Exception(() => tracker.DisposeAsync().AsTask().GetAwaiter().GetResult());
            tracker.RequestStop();
            invoked.TrySetResult();
        };
        Task? actualClose = null;
        try { await invoked.Task.WaitAsync(ChatExecutionTracker.VisibilityDelay + TimeSpan.FromSeconds(5)); }
        finally
        {
            actualClose = tracker.DisposeAsync().AsTask();
            await actualClose.WaitAsync(TimeSpan.FromSeconds(5));
        }
        Assert.IsType<InvalidOperationException>(actualRefusal);
        Assert.Same(actualClose, tracker.DisposeAsync().AsTask());
        Assert.True(actualClose!.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task Canonical_synchronous_progress_OCE_is_faulted_exact_cause_before_provider_or_iterator_cancellation()
    {
        var h = Harness.Create(temporary: true);
        var actual = new OperationCanceledException("Exact synchronous canonical LoadingModel observer OCE");
        h.Service.ExecutionChanged += snapshot => { if (snapshot.Stage == ChatExecutionStage.LoadingModel) throw actual; };
        var original = h.RunAsync("actual progress observer");
        var observed = await Record.ExceptionAsync(() => original);
        Assert.True(original.IsFaulted);
        Assert.False(original.IsCanceled);
        Assert.Contains(OriginalCauses(observed!), cause => ReferenceEquals(cause, actual));
        Assert.Equal(TaskExecutionLifecycle.Suspended, h.Service.CurrentCanonicalTask!.State);
        Assert.Contains(h.Service.CurrentCanonicalTask.RecoveryObservation!.Causes, value => value.Message == actual.Message);
        Assert.Equal(0, h.Provider.Frames);
        Assert.Empty(h.Capture.Chat);
        await h.Runtime.CloseAndDrainAsync();
    }

    [Fact]
    public async Task Ordinary_synchronous_progress_OCE_keeps_original_cancellation_semantics_without_canonical_task()
    {
        var h = Harness.Create(temporary: true);
        var actual = new OperationCanceledException("Actual ordinary progress callback OCE");
        h.Service.ExecutionChanged += snapshot => { if (snapshot.Stage == ChatExecutionStage.LoadingModel) throw actual; };
        async Task RunOrdinaryAsync()
        {
            await foreach (var _ in h.Service.SendAsync(h.Conversation, "ordinary progress", h.Model, EffortLevel.Medium,
                [], "controlled", "", DuoMode.Solo, h.WorkspaceRoot, null, null, null, default)) { }
        }
        var original = RunOrdinaryAsync();
        var observed = await Record.ExceptionAsync(() => original);
        Assert.True(original.IsCanceled);
        Assert.IsAssignableFrom<OperationCanceledException>(observed);
        Assert.Null(h.Service.CurrentCanonicalTask);
        Assert.Equal(0, h.TaskRepository.RowCount);
        Assert.Equal(0, h.Provider.Frames);
        await h.Runtime.CloseAndDrainAsync();
    }

    [Fact]
    public async Task Actual_withdrawal_is_published_once_before_callbacks_and_external_close_waits_its_original()
    {
        var tracker = new ChatExecutionTracker();
        var actualCts = OriginalLifetime(tracker);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Exception? actualRefusal = null;
        var callbacks = 0;
        using var registration = actualCts.Token.Register(() =>
        {
            Interlocked.Increment(ref callbacks);
            actualRefusal = Record.Exception(() => { _ = tracker.DisposeAsync(); });
            tracker.RequestStop(); // Reentrant request must coalesce, never wait or acquire another CTS original.
            entered.TrySetResult();
            release.Task.WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
        });
        tracker.RequestStop();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var actualWithdrawal = OriginalWithdrawal(tracker);
        var actualClose = tracker.DisposeAsync().AsTask();
        try
        {
            tracker.RequestStop();
            Assert.Same(actualWithdrawal, OriginalWithdrawal(tracker));
            Assert.False(actualClose.IsCompleted);
            Assert.IsType<InvalidOperationException>(actualRefusal);
            Assert.Equal(1, callbacks);
        }
        finally { release.TrySetResult(); await actualClose.WaitAsync(TimeSpan.FromSeconds(5)); }
        Assert.True(actualWithdrawal.IsCompletedSuccessfully);
        Assert.Same(actualClose, tracker.DisposeAsync().AsTask());
        Assert.Throws<ObjectDisposedException>(() => { _ = actualCts.Token; });
    }

    [Fact]
    public async Task Same_thread_suppressed_registration_context_cannot_join_its_own_actual_withdrawal()
    {
        var tracker = new ChatExecutionTracker();
        var actualCts = OriginalLifetime(tracker);
        Exception? actualRefusal = null;
        var callbacks = 0;
        using var registration = actualCts.Token.UnsafeRegister(_ =>
        {
            Interlocked.Increment(ref callbacks);
            actualRefusal = Record.Exception(() => { _ = tracker.DisposeAsync(); });
            tracker.RequestStop();
        }, null);
        tracker.RequestStop();
        var actualClose = tracker.DisposeAsync().AsTask();
        await actualClose.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.IsType<InvalidOperationException>(actualRefusal);
        Assert.Equal(1, callbacks);
        Assert.True(OriginalWithdrawal(tracker).IsCompletedSuccessfully);
        Assert.Same(actualClose, tracker.DisposeAsync().AsTask());
    }

    [Fact]
    public async Task Actual_timer_and_withdrawal_callback_faults_are_both_joined_and_retained_before_CTS_disposal()
    {
        var tracker = new ChatExecutionTracker();
        var actualCts = OriginalLifetime(tracker);
        var timerFailure = new IOException("Exact actual visibility timer callback failure");
        var withdrawalFailure = new OperationCanceledException("Exact actual synchronous withdrawal callback OCE");
        var timerEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        tracker.Changed += snapshot =>
        {
            if (snapshot.IsVisible && snapshot.Stage != ChatExecutionStage.Cancelled)
            { timerEntered.TrySetResult(); throw timerFailure; }
        };
        using var registration = actualCts.Token.Register(() => throw withdrawalFailure);
        Task? actualClose = null;
        Exception? observed = null;
        try { await timerEntered.Task.WaitAsync(ChatExecutionTracker.VisibilityDelay + TimeSpan.FromSeconds(5)); }
        finally
        {
            tracker.RequestStop();
            actualClose = tracker.DisposeAsync().AsTask();
            observed = await Record.ExceptionAsync(() => actualClose);
        }
        Assert.True(actualClose!.IsFaulted);
        Assert.Contains(OriginalCauses(observed!), cause => ReferenceEquals(cause, timerFailure));
        Assert.Contains(OriginalCauses(observed!), cause => ReferenceEquals(cause, withdrawalFailure));
        Assert.True(OriginalWithdrawal(tracker).IsFaulted);
        Assert.Throws<ObjectDisposedException>(() => { _ = actualCts.Token; });
    }

    [Fact]
    public async Task Inherited_original_close_context_is_retired_before_late_descendant_external_join()
    {
        var tracker = new ChatExecutionTracker();
        var releaseDescendant = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<Task>? actualDescendant = null;
        tracker.Changed += _ =>
        {
            actualDescendant = Task.Run(async () =>
            {
                await releaseDescendant.Task;
                var sameClose = tracker.DisposeAsync().AsTask();
                await sameClose;
                return sameClose;
            });
        };
        var actualClose = tracker.DisposeAsync().AsTask();
        await actualClose.WaitAsync(TimeSpan.FromSeconds(5));
        releaseDescendant.TrySetResult();
        Assert.NotNull(actualDescendant);
        Assert.Same(actualClose, await actualDescendant!.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.True(actualClose.IsCompletedSuccessfully);
    }

    private static CancellationTokenSource OriginalLifetime(ChatExecutionTracker tracker) =>
        (CancellationTokenSource)typeof(ChatExecutionTracker).GetField("_lifetime", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(tracker)!;
    private static Task OriginalWithdrawal(ChatExecutionTracker tracker) =>
        (Task)typeof(ChatExecutionTracker).GetField("_originalWithdrawal", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(tracker)!;

    private static IEnumerable<Exception> OriginalCauses(Exception cause)
    {
        yield return cause;
        if (cause is AggregateException aggregate)
            foreach (var direct in aggregate.InnerExceptions)
                foreach (var error in OriginalCauses(direct)) yield return error;
    }

    private sealed class Harness
    {
        public required string WorkspaceRoot;
        public required Conversation Conversation;
        public required ModelDescriptor Model;
        public required Conversations Conversations;
        public required TaskExecutionCoordinator Coordinator;
        public required Tasks TaskRepository;
        public required TaskRunOriginalFrameOwner Runtime;
        public required Authority Authority;
        public required Capture Capture;
        public required Provider Provider;
        public required ToolsOwner ToolOwner;
        public required Workspace Workspace;
        public required ChatSessionService Service;
        public List<ChatStreamEvent> Events { get; } = [];
        public static Harness Create(bool temporary, bool tools = false)
        {
            var fixtureOwner = OriginalFixtureOwner.Value ?? throw new InvalidOperationException("The actual test workspace owner is unavailable.");
            var actualWorkspaceRoot = Path.Combine(Path.GetTempPath(), "astra-task-context-fixture-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(actualWorkspaceRoot);
            fixtureOwner._ownedWorkspaces.Add(actualWorkspaceRoot);
            var now = DateTimeOffset.UtcNow.AddHours(-1);
            var conversation = new Conversation(Guid.NewGuid(), HavenMode.Studio, ConversationKind.StudioChat,
                "controlled original", null, null, false, temporary, now, now);
            var model = new ModelDescriptor("synthetic-model", 1, "controlled", "controlled", "controlled",
                tools ? new HashSet<ToolCapability> { ToolCapability.Text, ToolCapability.Tools }
                    : new HashSet<ToolCapability> { ToolCapability.Text }, now);
            var authority = new Authority();
            TaskExecutionCoordinator? coordinator = null;
            var runtime = new TaskRunOriginalFrameOwner((task, run, attempt, token) =>
                coordinator!.TryGetIssuedAttemptAsync(task, run, attempt, token));
            var taskRepository = new Tasks();
            coordinator = new TaskExecutionCoordinator(taskRepository, new Sink(), admissionAuthority: authority, runtimeSettlement: runtime);
            var capture = new Capture(); var conversations = new Conversations(); var workspace = new Workspace(); var owner = new ToolsOwner();
            var provider = new Provider(model, coordinator, runtime, capture);
            return new Harness
            {
                WorkspaceRoot = actualWorkspaceRoot,
                Conversation = conversation, Model = model, Conversations = conversations, Coordinator = coordinator,
                Runtime = runtime, Authority = authority, Capture = capture, Provider = provider, ToolOwner = owner, Workspace = workspace, TaskRepository = taskRepository,
                Service = new ChatSessionService(conversations, provider, new CapabilityPreflightService(), new Safety(),
                    new WorkspaceToolRuntime(workspace), new ComputerToolRuntime(new Computer()),
                    taskCoordinator: coordinator, taskToolOwner: owner, taskProviderContextCapture: capture)
            };
        }
        public async Task RunAsync(string prompt, GenerationOptions? options = null)
        {
            await foreach (var _ in Service.SendAsync(Conversation, prompt, Model, EffortLevel.Medium, [], "controlled", "",
                DuoMode.Solo, WorkspaceRoot, null, null, null, default, generationOptions: options,
                taskExecutionIntent: TaskRunExecutionIntent.CanonicalAgenticTask)) { Events.Add(_); }
        }
    }

    private sealed class Capture : ITaskRunProviderContextCapture
    {
        public sealed record ChatItem(TaskExecutionSnapshot Current, OllamaChatRequest Request, TaskRunContextInventory Inventory);
        public sealed record ToolItem(TaskExecutionSnapshot Current, OllamaToolRequest Request, TaskRunContextInventory Inventory);
        public List<ChatItem> Chat { get; } = []; public List<ToolItem> Tools { get; } = [];
        public Exception? Refusal;
        public Action? BeforeCapture;
        public Task? OriginalFailureTask;
        public ValueTask CaptureOriginalAsync(TaskExecutionSnapshot current, OllamaChatRequest request, TaskRunContextInventory inventory, CancellationToken token)
        { BeforeCapture?.Invoke(); if (OriginalFailureTask is { } original) return new ValueTask(original); if (Refusal is { } failure) return ValueTask.FromException(failure); Chat.Add(new(current, request, inventory)); return ValueTask.CompletedTask; }
        public ValueTask CaptureOriginalAsync(TaskExecutionSnapshot current, OllamaToolRequest request, TaskRunContextInventory inventory, CancellationToken token)
        { BeforeCapture?.Invoke(); if (OriginalFailureTask is { } original) return new ValueTask(original); if (Refusal is { } failure) return ValueTask.FromException(failure); Tools.Add(new(current, request, inventory)); return ValueTask.CompletedTask; }
    }
    private sealed class Provider(ModelDescriptor model, TaskExecutionCoordinator coordinator, TaskRunOriginalFrameOwner runtime, Capture capture) : IOllamaClient
    {
        public OllamaChatRequest? ChatRequest; public OllamaToolRequest? ToolRequest; public int Frames; public int CompatibilityCompletions;
        public Func<TaskRunAttemptAdmission, Task>? BeforeToolResponse;
        public Func<Task>? OnStreamFinally;
        public Task<bool> IsAvailableAsync(CancellationToken token) => Task.FromResult(true);
        public Task<IReadOnlyList<ModelDescriptor>> GetModelsAsync(CancellationToken token) => Task.FromResult<IReadOnlyList<ModelDescriptor>>([model]);
        private async Task<TaskRunAttemptAdmission> OpenAsync(ProviderExecutionContext? context, CancellationToken token)
        {
            var exact = context ?? throw new InvalidOperationException("Missing canonical context");
            var admission = await coordinator.StartAttemptAsync(exact.TaskId, exact.ExecutionId,
                new TaskRunRouteCandidate("synthetic-route", 1, "synthetic", model.Name, null, false, ["Text"]), token);
            await runtime.RegisterOriginalAttemptAsync(admission, token);
            await coordinator.MarkAttemptRunningAsync(exact.TaskId, exact.ExecutionId, admission.AttemptId, token);
            return admission;
        }
        public async IAsyncEnumerable<string> StreamChatAsync(OllamaChatRequest request, [EnumeratorCancellation] CancellationToken token)
        {
            Assert.Same(request, Assert.Single(capture.Chat).Request); ChatRequest = request;
            var admission = await OpenAsync(request.ExecutionContext, token);
            var original = runtime.StartOriginalFrameAsync(admission, _ => { Frames++; return Task.FromResult("actual controlled reply"); }, token);
            try { yield return await original; }
            finally { if (OnStreamFinally is { } close) await close(); }
        }
        public Task<string> CompleteAsync(OllamaChatRequest request, CancellationToken token)
        { CompatibilityCompletions++; throw new InvalidOperationException("No unowned canonical compatibility call"); }
        public async Task<OllamaToolResponse> ChatWithToolsAsync(OllamaToolRequest request, CancellationToken token)
        {
            Assert.Same(request, Assert.Single(capture.Tools).Request); ToolRequest = request;
            var admission = await OpenAsync(request.ExecutionContext, token);
            if (BeforeToolResponse is not null) await BeforeToolResponse(admission);
            return await runtime.StartOriginalFrameAsync(admission, _ =>
            {
                Frames++;
                using var json = JsonDocument.Parse("{\"path\":\"actual.txt\",\"content\":\"actual\"}");
                var args = json.RootElement.EnumerateObject().ToDictionary(value => value.Name, value => value.Value.Clone());
                return Task.FromResult(new OllamaToolResponse("", [new OllamaToolCall("write_file", args)]));
            }, token);
        }
    }
    private sealed class Tasks : ITaskExecutionRepository
    {
        private readonly Dictionary<Guid, TaskExecutionSnapshot> _rows = [];
        public int RowCount => _rows.Count;
        public Func<Task>? BeforeSuspension;
        public async Task UpsertAsync(TaskExecutionSnapshot next, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (next.State == TaskExecutionLifecycle.Suspended && BeforeSuspension is { } before)
            { BeforeSuspension = null; await before(); }
            if (_rows.TryGetValue(next.TaskId, out var prior) ? prior.PersistenceRevision != next.PersistenceRevision - 1 : next.PersistenceRevision != 1)
                throw new TaskExecutionRevisionConflictException(next.TaskId, next.PersistenceRevision - 1, next.PersistenceRevision);
            _rows[next.TaskId] = next;
        }
        public Task<TaskExecutionSnapshot?> GetAsync(Guid id, CancellationToken token) => Task.FromResult(_rows.GetValueOrDefault(id));
        public Task<TaskExecutionSnapshot?> GetByContextAsync(Guid id, CancellationToken token) => Task.FromResult(_rows.Values.FirstOrDefault(row => row.ContextId == id));
        public Task<IReadOnlyList<TaskExecutionSnapshot>> GetResumableAsync(CancellationToken token) => Task.FromResult<IReadOnlyList<TaskExecutionSnapshot>>(_rows.Values.ToArray());
    }
    private sealed class Conversations : IConversationRepository
    {
        public Conversation? Current; public int Writes; public int Reads; public List<ChatMessage> Messages { get; } = [];
        public Task<Conversation?> GetAsync(Guid id, CancellationToken token) { Reads++; return Task.FromResult(Current?.Id == id ? Current : null); }
        public Task<IReadOnlyList<Conversation>> GetRecentAsync(HavenMode? mode, int count, CancellationToken token) => Task.FromResult<IReadOnlyList<Conversation>>(Current is null ? [] : [Current]);
        public Task<IReadOnlyList<ChatMessage>> GetMessagesAsync(Guid id, CancellationToken token) => Task.FromResult<IReadOnlyList<ChatMessage>>(Messages.Where(value => value.ConversationId == id).ToArray());
        public Task UpsertConversationAsync(Conversation value, CancellationToken token) { Writes++; Current = value; return Task.CompletedTask; }
        public Task AddMessageAsync(ChatMessage message, CancellationToken token) { Writes++; Messages.Add(message); return Task.CompletedTask; }
        public Task DeleteConversationAsync(Guid id, CancellationToken token) => throw new NotSupportedException();
    }
    private sealed class Authority : ITaskRunCommandAuthority
    {
        public Lease? Lease;
        public bool AllowCurrentActor = true;
        public int AttemptChecks;
        public int StartChecks;
        public Func<Task>? OnLeaseClose;
        public Guid? AcceptedAction; public Guid? AcceptedAttempt;
        public Task<TaskExecutionOwnerBinding> AuthorizeStartAsync(TaskExecutionSnapshot current, CancellationToken token)
        { StartChecks++; return Task.FromResult(new TaskExecutionOwnerBinding(current.TaskId, current.ContextId, current.ExecutionId,
            "controlled-actor", "controlled-profile", null, null, "controlled-auth", "controlled-start")); }
        public Task<ITaskRunAdmissionLease> AuthorizeAttemptAsync(TaskExecutionSnapshot current, Guid id, TaskRunRouteCandidate candidate, Guid? old, CancellationToken token)
        { AttemptChecks++; if (!AllowCurrentActor) throw new UnauthorizedAccessException("Actual controlled actor revoked"); Lease = new Lease(current.OwnerBinding!, id, candidate) { OriginalCloseBody = OnLeaseClose }; return Task.FromResult<ITaskRunAdmissionLease>(Lease); }
        public Task ValidateTaskCommandAsync(TaskExecutionSnapshot current, string command, CancellationToken token)
        { if (!AllowCurrentActor || current.OwnerBinding?.ActorId != "controlled-actor") throw new UnauthorizedAccessException("Actual controlled actor revoked"); return Task.CompletedTask; }
        public Task ValidateAcceptedActionAsync(TaskExecutionSnapshot current, Guid attempt, Guid action, string receipt, CancellationToken token)
        { if (attempt != AcceptedAttempt || action != AcceptedAction || receipt != "controlled-exact-owner-receipt") throw new UnauthorizedAccessException(); return Task.CompletedTask; }
    }
    private sealed class Lease(TaskExecutionOwnerBinding owner, Guid id, TaskRunRouteCandidate candidate) : ITaskRunAdmissionLease
    {
        public TaskExecutionOwnerBinding Owner => owner; public Guid AttemptId => id; public TaskRunRouteCandidate Candidate => candidate;
        public string ReceiptReference => "controlled-live-lease"; public int Disposes; public Exception? CleanupFailure;
        public Func<Task>? OriginalCloseBody;
        public ValueTask RevalidateAsync(CancellationToken token) { token.ThrowIfCancellationRequested(); if (Disposes != 0) throw new ObjectDisposedException(nameof(Lease)); return ValueTask.CompletedTask; }
        public ValueTask DisposeAsync()
        { Disposes++; if (OriginalCloseBody is { } body) return new ValueTask(body()); return CleanupFailure is { } failure ? ValueTask.FromException(failure) : ValueTask.CompletedTask; }
    }
    private sealed class ToolsOwner : ITaskRunToolActionOwner
    {
        public int Preparations;
        public bool SupportsCanonicalInvocation(ToolRuntimeKind runtime, string name) => runtime == ToolRuntimeKind.Workspace;
        public Task<ITaskRunToolActionPreparation> PrepareOriginalAsync(TaskRunAttemptAdmission original, TaskExecutionSnapshot current, Guid action,
            OllamaToolCall call, ToolRuntimeKind runtime, PermissionMode permission, string? root, CancellationToken token)
        { Preparations++; throw new InvalidOperationException("Unexpected pre-effect dispatch"); }
        public Task<TaskRunToolActionResult> ExecuteOriginalAsync(ITaskRunToolActionPreparation original, Func<CancellationToken, Task<WorkspaceToolResult>> body, CancellationToken token) => throw new NotSupportedException();
        public ValueTask ValidateOriginalPreparationAsync(ITaskRunToolActionPreparation original, TaskExecutionSnapshot current, CancellationToken token) => throw new NotSupportedException();
        public ValueTask ValidateOriginalResultAsync(ITaskRunToolActionPreparation original, TaskRunToolActionResult result, CancellationToken token) => throw new NotSupportedException();
        public ValueTask RetireAcknowledgedOriginalAsync(ITaskRunToolActionPreparation original, TaskExecutionSnapshot current, CancellationToken token) => throw new NotSupportedException();
    }
    private sealed class Workspace : IWorkspaceToolService
    {
        public int Effects;
        public string ResolveWorkspacePath(string root, string path) => Path.Combine(root, path);
        public Task<string> ReadTextAsync(string root, string path, CancellationToken token) { Effects++; throw new InvalidOperationException("Unexpected effect"); }
        public Task WriteTextAtomicAsync(string root, string path, string body, CancellationToken token) { Effects++; throw new InvalidOperationException("Unexpected effect"); }
        public Task<IReadOnlyList<string>> SearchFilesAsync(string root, string pattern, CancellationToken token) { Effects++; throw new InvalidOperationException("Unexpected effect"); }
        public Task<ProcessResult> RunProcessAsync(ProcessRequest request, CancellationToken token) { Effects++; throw new InvalidOperationException("Unexpected effect"); }
    }
    private sealed class Computer : IComputerToolService
    {
        public bool IsSupported => false;
        public Task<string> SnapshotAsync(CancellationToken token) => throw new NotSupportedException();
        public Task<string> ListWindowsAsync(CancellationToken token) => throw new NotSupportedException();
        public Task<string> LaunchAppAsync(string name, CancellationToken token) => throw new NotSupportedException();
        public Task<string> FocusWindowAsync(string title, CancellationToken token) => throw new NotSupportedException();
        public Task<string> InvokeAsync(string window, string name, string id, CancellationToken token) => throw new NotSupportedException();
        public Task<string> ClickAsync(string window, int x, int y, string button, CancellationToken token) => throw new NotSupportedException();
        public Task<string> TypeAsync(string window, string text, CancellationToken token) => throw new NotSupportedException();
        public Task<string> PressAsync(string window, string keys, CancellationToken token) => throw new NotSupportedException();
        public Task<string> CloseWindowAsync(string title, CancellationToken token) => throw new NotSupportedException();
    }
    private sealed class Safety : IConversationSafetyService
    {
        public Task<ConversationSafetySnapshot> GetSnapshotAsync(Guid id, CancellationToken token) => Task.FromResult(new ConversationSafetySnapshot(id, 0, ConversationSafetyState.Active, null, 0));
        public Task<ConversationSafetyFlagResult> RecordConfirmedFlagAsync(Guid id, ConfirmedSafetyFlag flag, CancellationToken token) => throw new NotSupportedException();
        public Task EnsureMayActAsync(Guid id, string operation, CancellationToken token) => Task.CompletedTask;
    }
    private sealed class Sink : IExecutionEventSink { public bool TryPublish(ExecutionEvent value) => true; }
}
