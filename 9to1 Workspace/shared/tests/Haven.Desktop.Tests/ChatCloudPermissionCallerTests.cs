using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Haven.Application;
using Haven.Core;
using Xunit;

namespace Haven.Desktop.Tests;

/// <summary>Actual canonical Chat + central source Ask + Task issuer + remediation owner.
/// Actor/catalogue/repos are explicitly synthetic; no account, credential, network or billing acceptance.</summary>
public sealed partial class ChatCloudPermissionCallerTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Source_Ask_during_actual_chat_or_tool_capture_returns_only_waiting_metadata_after_same_run_suspension(bool tools)
    {
        await using var rig = new Rig();
        await rig.RunAsync(tools);
        var required = Assert.Single(rig.Stream, item => item.Kind == ChatStreamEventKind.PermissionRequired);
        var metadata = Assert.IsType<RemediationRequest>(required.PermissionRequest);
        Assert.Equal(RemediationState.Waiting, metadata.State);
        Assert.Equal(metadata.Id, Assert.Single(rig.RemediationRows.Rows.Values).Id);
        Assert.Empty(rig.Policy.Grants);
        Assert.DoesNotContain(rig.Stream, item => item.Kind == ChatStreamEventKind.AssistantCompleted);
        Assert.Equal(0, rig.Client.Dispatches);
        Assert.Equal(0, rig.Provider.Starts);
        Assert.Equal(0, rig.Workspace.Effects);
        Assert.Equal(0, rig.ToolOwner.Preparations);
        Assert.Equal(tools ? 1 : 0, rig.Capture.ToolCaptures);
        Assert.Equal(tools ? 0 : 1, rig.Capture.ChatCaptures);
        var same = Assert.IsType<TaskExecutionSnapshot>(rig.Service.CurrentCanonicalTask);
        Assert.Equal(TaskExecutionLifecycle.Suspended, same.State);
        Assert.Equal(same.TaskId, rig.Capture.OriginalAsk!.OriginalRequest!.OriginalOwner.TaskId);
        Assert.Equal(same.ExecutionId, rig.Capture.OriginalAsk.OriginalRequest.OriginalOwner.ExecutionId);
        Assert.Equal(same.ContextId, rig.Conversation.Id);
        Assert.NotNull(same.RecoveryObservation);
        Assert.Empty(same.Attempts);
        Assert.Equal(1, rig.Tasks.LiveOriginalInvocationCount);
        var inspection = await rig.Tasks.InspectOriginalRecoveryAsync(same.TaskId, same.ExecutionId, TestContext.Current.CancellationToken);
        Assert.Contains(inspection.OriginalWork, value => value.Stage == "permission.request" && value.Status == TaskStatus.RanToCompletion);
        Assert.NotEmpty(inspection.Causes);
    }

    [Fact]
    public async Task Actual_stream_frame_Ask_disposes_same_iterator_before_permission_metadata_and_suspension()
    {
        await using var rig = new Rig(); rig.Capture.AskDuringCapture = false;
        await rig.RunAsync(false);
        Assert.Equal(1, rig.Client.StreamDisposals);
        Assert.Equal(0, rig.Client.Dispatches);
        Assert.Single(rig.Stream, value => value.Kind == ChatStreamEventKind.PermissionRequired);
        var same = rig.Service.CurrentCanonicalTask!;
        Assert.Equal(TaskExecutionLifecycle.Suspended, same.State);
        var observation = await rig.Tasks.InspectOriginalRecoveryAsync(same.TaskId, same.ExecutionId, TestContext.Current.CancellationToken);
        Assert.Contains(observation.OriginalWork, value => value.Stage == "provider.stream.move" && value.Status == TaskStatus.Faulted);
        Assert.Contains(observation.OriginalWork, value => value.Stage == "provider.stream.dispose" && value.Status == TaskStatus.RanToCompletion);
        Assert.DoesNotContain(rig.Stream, value => value.Kind == ChatStreamEventKind.AssistantCompleted);
        Assert.Empty(rig.Policy.Grants);
    }

    [Fact]
    public async Task Held_actual_stream_disposal_withholds_permission_event_until_original_close_and_suspension_acknowledge()
    {
        await using var rig = new Rig(); rig.Capture.AskDuringCapture = false;
        var held = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        rig.Client.OriginalDispose = held.Task;
        var actual = rig.RunAsync(false);
        try
        {
            await rig.Client.DisposeEntered.Task;
            Assert.False(actual.IsCompleted);
            Assert.DoesNotContain(rig.Stream, value => value.Kind == ChatStreamEventKind.PermissionRequired);
            Assert.Empty(rig.RemediationRows.Rows);
            Assert.Equal(TaskExecutionLifecycle.Running, rig.Service.CurrentCanonicalTask!.State);
            held.SetResult(); await actual;
            Assert.Single(rig.Stream, value => value.Kind == ChatStreamEventKind.PermissionRequired);
            Assert.Equal(TaskExecutionLifecycle.Suspended, rig.Service.CurrentCanonicalTask!.State);
            Assert.Equal(1, rig.Client.StreamDisposals);
        }
        finally { held.TrySetResult(); await Record.ExceptionAsync(() => actual); }
    }

    [Fact]
    public async Task Actual_Ask_plus_original_stream_cleanup_fault_retains_both_and_never_returns_a_permission_card_or_completion()
    {
        await using var rig = new Rig(); rig.Capture.AskDuringCapture = false;
        var cleanup = new IOException("same original stream cleanup fault");
        rig.Client.OriginalDispose = Task.FromException(cleanup);
        var actual = await Record.ExceptionAsync(() => rig.RunAsync(false)); Assert.NotNull(actual);
        Assert.Contains(Leaves(actual!), value => ReferenceEquals(value, cleanup));
        Assert.Contains(Leaves(actual!), value => ReferenceEquals(value, rig.Capture.OriginalAsk));
        Assert.DoesNotContain(rig.Stream, value => value.Kind is ChatStreamEventKind.PermissionRequired or ChatStreamEventKind.AssistantCompleted);
        Assert.Empty(rig.RemediationRows.Rows); Assert.Empty(rig.Policy.Grants);
        Assert.Equal(TaskExecutionLifecycle.Suspended, rig.Service.CurrentCanonicalTask!.State);
        Assert.Equal(0, rig.Client.Dispatches); Assert.Equal(1, rig.Client.StreamDisposals);
    }

    [Fact]
    public async Task Permission_publication_without_original_suspension_cas_ack_never_returns_metadata_as_a_success()
    {
        await using var rig = new Rig(); rig.TaskRows.RefuseSuspension = true;
        var actual = await Record.ExceptionAsync(() => rig.RunAsync(false)); Assert.NotNull(actual);
        Assert.Contains(Leaves(actual!), value => value is TaskExecutionRevisionConflictException);
        Assert.Contains(Leaves(actual!), value => ReferenceEquals(value, rig.Capture.OriginalAsk));
        Assert.Single(rig.RemediationRows.Rows);
        Assert.DoesNotContain(rig.Stream, value => value.Kind is ChatStreamEventKind.PermissionRequired or ChatStreamEventKind.AssistantCompleted);
        Assert.Equal(TaskExecutionLifecycle.Running, (await rig.Tasks.GetAsync(rig.Service.CurrentCanonicalTask!.TaskId, TestContext.Current.CancellationToken))!.State);
        Assert.Equal(1, rig.Tasks.LiveOriginalInvocationCount);
        Assert.Equal(0, rig.Client.Dispatches); Assert.Empty(rig.Policy.Grants);
    }

    [Fact]
    public async Task Unsupported_tool_schema_keeps_every_actual_raw_task_sibling_before_the_canonical_refusal()
    {
        await using var rig = new Rig(); rig.Capture.AskDuringCapture = false;
        var schema = new HttpRequestException("actual provider does not support tools");
        var sibling = new IOException("second actual raw tool frame cause");
        var original = new TaskCompletionSource<OllamaToolResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        original.SetException([schema, sibling]); rig.Client.OriginalToolFailure = original.Task;
        var actual = await Record.ExceptionAsync(() => rig.RunAsync(true)); Assert.NotNull(actual);
        Assert.Contains(Leaves(actual!), value => ReferenceEquals(value, schema));
        Assert.Contains(Leaves(actual!), value => ReferenceEquals(value, sibling));
        Assert.True(original.Task.IsFaulted);
        Assert.DoesNotContain(rig.Stream, value => value.Kind is ChatStreamEventKind.PermissionRequired or ChatStreamEventKind.AssistantCompleted);
        Assert.Empty(rig.RemediationRows.Rows); Assert.Empty(rig.Policy.Grants);
        Assert.Equal(TaskExecutionLifecycle.Suspended, rig.Service.CurrentCanonicalTask!.State);
        Assert.Equal(0, rig.Client.Dispatches); Assert.Equal(0, rig.Workspace.Effects);
    }

    [Fact]
    public async Task The_same_exact_Ask_faulted_original_settlement_is_not_a_successful_permission_return()
    {
        await using var rig = new Rig(); rig.Capture.AskDuringCapture = false;
        rig.Capture.OpenActualAttemptBeforeAsk = true; rig.Settlement.FailWithOriginalAsk = true;
        var actual = await Record.ExceptionAsync(() => rig.RunAsync(false)); Assert.NotNull(actual);
        Assert.Contains(Leaves(actual!), value => ReferenceEquals(value, rig.Capture.OriginalAsk));
        Assert.Equal(1, rig.Settlement.Calls);
        Assert.True(rig.Settlement.ActualReturned!.IsFaulted);
        Assert.Single(rig.RemediationRows.Rows);
        Assert.DoesNotContain(rig.Stream, value => value.Kind is ChatStreamEventKind.PermissionRequired or ChatStreamEventKind.AssistantCompleted);
        var same = rig.Service.CurrentCanonicalTask!;
        Assert.Equal(TaskRunOriginalSettlementOutcome.Failed, same.RecoveryObservation!.SettlementOutcome);
        Assert.Equal(TaskExecutionLifecycle.Suspended, same.State);
        var inspection = await rig.Tasks.InspectOriginalRecoveryAsync(same.TaskId, same.ExecutionId, TestContext.Current.CancellationToken);
        Assert.Contains(inspection.OriginalWork, value => value.Stage == "runtime.settlement" && value.Status == TaskStatus.Faulted);
        Assert.Equal(0, rig.Client.Dispatches); Assert.Empty(rig.Policy.Grants);
    }

    [Fact]
    public void Selected_old_full_arity_25_constructor_and_nine_field_stream_event_clr_signatures_remain_available()
    {
        var constructors = typeof(ChatSessionService).GetConstructors();
        Assert.Contains(constructors, value => value.GetParameters().Length == 22);
        Assert.Contains(constructors, value => value.GetParameters().Length == 24);
        Assert.Contains(constructors, value => value.GetParameters().Length == 25
            && value.GetParameters()[24].ParameterType == typeof(ITaskRunProviderContextCapture));
        Assert.Contains(constructors, value => value.GetParameters().Length == 26
            && value.GetParameters()[25].ParameterType == typeof(TaskRunCloudPermissionRemediationOwner));
        Assert.Contains(typeof(ChatStreamEvent).GetConstructors(), value => value.GetParameters().Length == 9);
        Assert.Equal(6, (int)ChatStreamEventKind.PreflightFailed);
        Assert.Equal(7, (int)ChatStreamEventKind.PermissionRequired);
    }

    [Fact]
    public async Task Permission_event_keeps_the_same_acknowledged_run_observation_when_a_later_queue_write_advances_revision()
    {
        await using var rig = new Rig();
        await rig.RunAsync(false);
        var required = Assert.Single(rig.Stream, value => value.Kind == ChatStreamEventKind.PermissionRequired);
        var observation = Assert.IsType<ProviderExecutionContext>(required.CanonicalTaskContext);
        var acknowledged = Assert.IsType<TaskExecutionSnapshot>(rig.Service.CurrentCanonicalTask);
        Assert.Equal(acknowledged.TaskId, observation.TaskId);
        Assert.Equal(acknowledged.ContextId, observation.ContextId);
        Assert.Equal(acknowledged.ExecutionId, observation.ExecutionId);
        Assert.Equal(acknowledged.PersistenceRevision, observation.PersistenceRevision);
        Assert.Null(observation.AttemptId);
        Assert.Null(observation.RequestedCandidate);
        Assert.Null(observation.SelectedCandidate);
        Assert.Equal(TaskExecutionLifecycle.Suspended, acknowledged.State);
        await rig.Tasks.SubmitFollowUpAsync(acknowledged.TaskId, "preserve this queued follow-up", TaskFollowUpMode.Queue,
            null, null, TestContext.Current.CancellationToken);
        var later = (await rig.Tasks.GetAsync(acknowledged.TaskId, TestContext.Current.CancellationToken))!;
        Assert.True(later.PersistenceRevision > observation.PersistenceRevision);
        Assert.Equal(acknowledged.PersistenceRevision, required.CanonicalTaskContext!.PersistenceRevision);
        Assert.Equal(observation, required.CanonicalTaskContext);
        Assert.Single(later.Queue);
        Assert.Equal(TaskExecutionLifecycle.Suspended, later.State);
        Assert.Empty(rig.Policy.Grants);
        Assert.Equal(0, rig.Client.Dispatches);
    }

    [Fact]
    public async Task Explicit_original_source_approval_continues_the_same_live_run_without_duplicating_the_accepted_user_message()
    {
        await using var rig = new Rig(temporary: false);
        await rig.RunAsync(false);
        var original = rig.Service.CurrentCanonicalTask!;
        var user = Assert.Single(rig.Conversations.Messages);
        var waiting = Assert.Single(rig.Stream, value => value.Kind == ChatStreamEventKind.PermissionRequired).PermissionRequest!;
        await rig.Owner.ApproveOriginalAsync(waiting.Id, TestContext.Current.CancellationToken);
        Assert.Equal(0, rig.Client.Dispatches); // The approval callback did not dispatch.
        rig.Client.RunApprovedOwnedFrame = true;
        var continued = new List<ChatStreamEvent>();
        await foreach (var value in rig.Service.ContinueUnstartedOriginalAsync(original.TaskId, original.ExecutionId, TestContext.Current.CancellationToken)) continued.Add(value);
        var completed = rig.Service.CurrentCanonicalTask!;
        Assert.Equal(original.TaskId, completed.TaskId);
        Assert.Equal(original.ExecutionId, completed.ExecutionId);
        Assert.Equal(original.ContextId, completed.ContextId);
        Assert.Equal(TaskExecutionLifecycle.Completed, completed.State);
        Assert.Null(completed.RecoveryObservation);
        Assert.Equal(JsonSerializer.Serialize(original.RecoveryObservation), JsonSerializer.Serialize(Assert.Single(completed.RecoveryHistory)));
        Assert.Single(completed.Attempts);
        Assert.Equal(TaskRunAttemptState.Completed, completed.Attempts[0].State);
        Assert.Equal(user, Assert.Single(rig.Conversations.Messages, value => value.Role == MessageRole.User));
        Assert.DoesNotContain(continued, value => value.Kind == ChatStreamEventKind.UserMessage);
        Assert.Single(continued, value => value.Kind == ChatStreamEventKind.AssistantCompleted);
        Assert.Equal(1, rig.Client.Dispatches);
        Assert.Equal(0, rig.Workspace.Effects);
    }

    [Fact]
    public async Task Refused_preapproval_preparation_keeps_actual_diagnostics_then_real_approval_continues_the_same_run()
    {
        await using var rig = new Rig(temporary: false);
        await rig.RunAsync(false);
        var original = rig.Service.CurrentCanonicalTask!;
        var acceptedUser = Assert.Single(rig.Conversations.Messages);
        var refusal = await Record.ExceptionAsync(async () =>
        { await foreach (var _ in rig.Service.ContinueUnstartedOriginalAsync(original.TaskId, original.ExecutionId, TestContext.Current.CancellationToken)) { } });
        Assert.NotNull(refusal);
        Assert.Equal(0, rig.Client.Dispatches);
        Assert.Equal(JsonSerializer.Serialize(original.RecoveryObservation), JsonSerializer.Serialize((await rig.Tasks.GetAsync(original.TaskId, TestContext.Current.CancellationToken))!.RecoveryObservation));
        var inspection = await rig.Tasks.InspectOriginalRecoveryAsync(original.TaskId, original.ExecutionId, TestContext.Current.CancellationToken);
        Assert.Contains(inspection.OriginalWork, value => value.Stage == "continuation.preparation" && value.Status == TaskStatus.Faulted);
        var waiting = Assert.Single(rig.Stream, value => value.Kind == ChatStreamEventKind.PermissionRequired).PermissionRequest!;
        await rig.Owner.ApproveOriginalAsync(waiting.Id, TestContext.Current.CancellationToken);
        rig.Client.RunApprovedOwnedFrame = true;
        await foreach (var _ in rig.Service.ContinueUnstartedOriginalAsync(original.TaskId, original.ExecutionId, TestContext.Current.CancellationToken)) { }
        Assert.Equal(original.TaskId, rig.Service.CurrentCanonicalTask!.TaskId);
        Assert.Equal(original.ExecutionId, rig.Service.CurrentCanonicalTask.ExecutionId);
        Assert.Equal(TaskExecutionLifecycle.Completed, rig.Service.CurrentCanonicalTask.State);
        Assert.Equal(acceptedUser, Assert.Single(rig.Conversations.Messages, value => value.Role == MessageRole.User));
        Assert.Equal(1, rig.Client.Dispatches);
    }

    [Fact]
    public async Task Replaced_physical_accepted_input_refuses_continuation_before_task_cas_or_provider_dispatch()
    {
        await using var rig = new Rig(temporary: false);
        await rig.RunAsync(false);
        var original = rig.Service.CurrentCanonicalTask!;
        var waiting = Assert.Single(rig.Stream, value => value.Kind == ChatStreamEventKind.PermissionRequired).PermissionRequest!;
        await rig.Owner.ApproveOriginalAsync(waiting.Id, TestContext.Current.CancellationToken);
        rig.Conversations.Messages[0] = rig.Conversations.Messages[0] with { Content = "foreign replacement" };
        var refusal = await Record.ExceptionAsync(async () =>
        { await foreach (var _ in rig.Service.ContinueUnstartedOriginalAsync(original.TaskId, original.ExecutionId, TestContext.Current.CancellationToken)) { } });
        Assert.NotNull(refusal);
        var current = (await rig.Tasks.GetAsync(original.TaskId, TestContext.Current.CancellationToken))!;
        Assert.Equal(original.PersistenceRevision, current.PersistenceRevision);
        Assert.Equal(JsonSerializer.Serialize(original.RecoveryObservation), JsonSerializer.Serialize(current.RecoveryObservation));
        Assert.Empty(current.RecoveryHistory);
        Assert.Equal(0, rig.Client.Dispatches);
        Assert.Single(rig.Conversations.Messages);
    }

    [Fact]
    public async Task Invoked_attempt_and_faulted_original_settlement_never_receive_the_no_attempt_continuation()
    {
        await using var rig = new Rig(); rig.Capture.AskDuringCapture = false;
        rig.Capture.OpenActualAttemptBeforeAsk = true; rig.Settlement.FailWithOriginalAsk = true;
        _ = await Record.ExceptionAsync(() => rig.RunAsync(false));
        var original = rig.Service.CurrentCanonicalTask!;
        var waiting = Assert.Single(rig.RemediationRows.Rows.Values);
        await rig.Owner.ApproveOriginalAsync(waiting.Id, TestContext.Current.CancellationToken);
        var refusal = await Record.ExceptionAsync(async () =>
        { await foreach (var _ in rig.Service.ContinueUnstartedOriginalAsync(original.TaskId, original.ExecutionId, TestContext.Current.CancellationToken)) { } });
        Assert.NotNull(refusal);
        var current = (await rig.Tasks.GetAsync(original.TaskId, TestContext.Current.CancellationToken))!;
        Assert.Equal(original.PersistenceRevision, current.PersistenceRevision);
        Assert.Single(current.Attempts);
        Assert.Equal(TaskRunOriginalSettlementOutcome.Failed, current.RecoveryObservation!.SettlementOutcome);
        Assert.Empty(current.RecoveryHistory);
        Assert.Equal(0, rig.Client.Dispatches);
    }

    [Fact]
    public async Task Invoked_continuation_cas_fault_keeps_original_history_and_never_dispatches_or_blindly_retries()
    {
        await using var rig = new Rig();
        await rig.RunAsync(false);
        var original = rig.Service.CurrentCanonicalTask!;
        var waiting = Assert.Single(rig.Stream, value => value.Kind == ChatStreamEventKind.PermissionRequired).PermissionRequest!;
        await rig.Owner.ApproveOriginalAsync(waiting.Id, TestContext.Current.CancellationToken);
        var first = new IOException("actual continuation CAS first cause");
        var second = new OperationCanceledException("actual continuation CAS faulted OCE sibling");
        var actual = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        actual.SetException([first, second]); rig.TaskRows.ContinuationWrite = actual.Task;
        var failed = await Record.ExceptionAsync(async () =>
        { await foreach (var _ in rig.Service.ContinueUnstartedOriginalAsync(original.TaskId, original.ExecutionId, TestContext.Current.CancellationToken)) { } });
        Assert.NotNull(failed);
        Assert.Contains(Leaves(failed!), value => ReferenceEquals(value, first));
        Assert.Contains(Leaves(failed!), value => ReferenceEquals(value, second));
        var retry = await Record.ExceptionAsync(async () =>
        { await foreach (var _ in rig.Service.ContinueUnstartedOriginalAsync(original.TaskId, original.ExecutionId, TestContext.Current.CancellationToken)) { } });
        Assert.NotNull(retry);
        Assert.Equal(1, rig.TaskRows.ContinuationWriteCalls);
        Assert.Equal(0, rig.Client.Dispatches);
        Assert.Equal(JsonSerializer.Serialize(original.RecoveryObservation), JsonSerializer.Serialize((await rig.Tasks.GetAsync(original.TaskId, TestContext.Current.CancellationToken))!.RecoveryObservation));
        var inspection = await rig.Tasks.InspectOriginalRecoveryAsync(original.TaskId, original.ExecutionId, TestContext.Current.CancellationToken);
        Assert.Contains(inspection.OriginalWork, value => value.Stage == "continuation.task-write" && value.Status == TaskStatus.Faulted);
    }

    [Fact]
    public async Task An_actual_raw_provider_callback_Ask_without_recorded_attempt_is_not_a_no_dispatcher_absence_witness()
    {
        await using var rig = new Rig(); rig.Capture.AskDuringCapture = false;
        await rig.RunAsync(false);
        var original = rig.Service.CurrentCanonicalTask!;
        Assert.Empty(original.Attempts);
        var waiting = Assert.Single(rig.Stream, value => value.Kind == ChatStreamEventKind.PermissionRequired).PermissionRequest!;
        await rig.Owner.ApproveOriginalAsync(waiting.Id, TestContext.Current.CancellationToken);
        rig.Client.RunApprovedOwnedFrame = true;
        var refused = await Record.ExceptionAsync(async () =>
        { await foreach (var _ in rig.Service.ContinueUnstartedOriginalAsync(original.TaskId, original.ExecutionId, TestContext.Current.CancellationToken)) { } });
        Assert.NotNull(refused);
        var current = (await rig.Tasks.GetAsync(original.TaskId, TestContext.Current.CancellationToken))!;
        Assert.Equal(original.PersistenceRevision, current.PersistenceRevision);
        Assert.Empty(current.RecoveryHistory);
        Assert.Equal(0, rig.Client.Dispatches);
        Assert.Equal(1, rig.Client.StreamDisposals);
        Assert.Equal(JsonSerializer.Serialize(original.RecoveryObservation), JsonSerializer.Serialize(current.RecoveryObservation));
    }

    [Fact]
    public async Task Canonical_initial_and_resumed_requests_use_one_detached_input_when_caller_lists_and_model_capabilities_change()
    {
        await using var rig = new Rig(temporary: false);
        var capabilities = new List<ActiveCapability>();
        var images = new List<string>();
        var modelCapabilities = new HashSet<ToolCapability> { ToolCapability.Text };
        var selected = rig.Provider.Model.Model with { Name = rig.Provider.Model.Key, Capabilities = modelCapabilities };
        var original = rig.Service.SendAsync(rig.Conversation, "immutable original input", selected, EffortLevel.Medium,
            capabilities, "controlled", "", DuoMode.Solo, null, null, null, images, TestContext.Current.CancellationToken,
            taskExecutionIntent: TaskRunExecutionIntent.CanonicalAgenticTask);
        // These mutations occur BEFORE first enumeration, not just after the initial request.
        capabilities.Add(new("foreign", "foreign", "", "foreign instruction", "", ""));
        images.Add("foreign image"); modelCapabilities.Clear();
        await foreach (var value in original) rig.Stream.Add(value);
        var task = rig.Service.CurrentCanonicalTask!;
        var first = Assert.Single(rig.Capture.ChatWire);
        Assert.DoesNotContain("foreign instruction", first, StringComparison.Ordinal);
        Assert.DoesNotContain("foreign image", first, StringComparison.Ordinal);
        var waiting = Assert.Single(rig.Stream, value => value.Kind == ChatStreamEventKind.PermissionRequired).PermissionRequest!;
        await rig.Owner.ApproveOriginalAsync(waiting.Id, TestContext.Current.CancellationToken);
        rig.Client.RunApprovedOwnedFrame = true;
        await foreach (var _ in rig.Service.ContinueUnstartedOriginalAsync(task.TaskId, task.ExecutionId, TestContext.Current.CancellationToken)) { }
        Assert.Collection(rig.Capture.ChatWire, value => Assert.Equal(first, value), value => Assert.Equal(first, value));
        Assert.Single(rig.Conversations.Messages, value => value.Role == MessageRole.User);
        Assert.Equal(task.ExecutionId, rig.Service.CurrentCanonicalTask!.ExecutionId);
        Assert.Equal(TaskExecutionLifecycle.Completed, rig.Service.CurrentCanonicalTask.State);
    }

    [Fact]
    public async Task Faulted_actual_accepted_history_read_preserves_all_direct_causes_before_any_permission_scope_or_task_write()
    {
        await using var rig = new Rig(temporary: false);
        await rig.RunAsync(false);
        var original = rig.Service.CurrentCanonicalTask!;
        var oce = new OperationCanceledException("actual faulted history read first cause");
        var sibling = new IOException("actual faulted history read sibling");
        var actual = new TaskCompletionSource<IReadOnlyList<ChatMessage>>(TaskCreationOptions.RunContinuationsAsynchronously);
        actual.SetException([oce, sibling]); rig.Conversations.OverrideHistory = actual.Task;
        var continued = DrainAsync();
        var failed = await Record.ExceptionAsync(() => continued);
        Assert.NotNull(failed);
        Assert.True(continued.IsFaulted);
        Assert.False(continued.IsCanceled);
        Assert.Contains(Leaves(failed!), value => ReferenceEquals(value, oce));
        Assert.Contains(Leaves(failed!), value => ReferenceEquals(value, sibling));
        var current = (await rig.Tasks.GetAsync(original.TaskId, TestContext.Current.CancellationToken))!;
        Assert.Equal(original.PersistenceRevision, current.PersistenceRevision);
        Assert.Equal(JsonSerializer.Serialize(original.RecoveryObservation), JsonSerializer.Serialize(current.RecoveryObservation));
        Assert.Empty(current.RecoveryHistory);
        Assert.Equal(0, rig.Client.Dispatches);
        Assert.Empty(rig.Policy.Grants);
        var inspected = await rig.Tasks.InspectOriginalRecoveryAsync(original.TaskId, original.ExecutionId, TestContext.Current.CancellationToken);
        Assert.Contains(inspected.OriginalWork, value => value.Stage == "continuation.input-history" && value.Status == TaskStatus.Faulted);
        Assert.Contains(inspected.OriginalWork, value => value.Stage == "continuation.preparation" && value.Status == TaskStatus.Faulted);
        async Task DrainAsync()
        { await foreach (var _ in rig.Service.ContinueUnstartedOriginalAsync(original.TaskId, original.ExecutionId, default)) { } }
    }

    [Fact]
    public async Task Genuine_canceled_actual_history_read_stays_canceled_without_dispatch_or_losing_its_original_status()
    {
        await using var rig = new Rig(temporary: false);
        await rig.RunAsync(false);
        var original = rig.Service.CurrentCanonicalTask!;
        using var withdrawal = new CancellationTokenSource(); withdrawal.Cancel();
        rig.Conversations.OverrideHistory = Task.FromCanceled<IReadOnlyList<ChatMessage>>(withdrawal.Token);
        var continued = DrainAsync();
        var refusal = await Record.ExceptionAsync(() => continued);
        Assert.IsAssignableFrom<OperationCanceledException>(refusal);
        Assert.True(continued.IsCanceled);
        Assert.False(continued.IsFaulted);
        var current = (await rig.Tasks.GetAsync(original.TaskId, TestContext.Current.CancellationToken))!;
        Assert.Equal(original.PersistenceRevision, current.PersistenceRevision);
        Assert.Empty(current.RecoveryHistory);
        Assert.Equal(0, rig.Client.Dispatches);
        Assert.Empty(rig.Policy.Grants);
        var inspected = await rig.Tasks.InspectOriginalRecoveryAsync(original.TaskId, original.ExecutionId, TestContext.Current.CancellationToken);
        Assert.Contains(inspected.OriginalWork, value => value.Stage == "continuation.input-history" && value.Status == TaskStatus.Canceled);
        Assert.Contains(inspected.OriginalWork, value => value.Stage == "continuation.preparation" && value.Status == TaskStatus.Canceled);
        async Task DrainAsync()
        { await foreach (var _ in rig.Service.ContinueUnstartedOriginalAsync(original.TaskId, original.ExecutionId, default)) { } }
    }

    [Fact]
    public async Task One_actual_service_completes_136_source_approved_same_run_continuations_without_leaking_original_admission_capacity()
    {
        await using var rig = new Rig();
        var completedTasks = new HashSet<Guid>();
        var completedRuns = new HashSet<Guid>();
        for (var index = 0; index < 136; index++)
        {
            rig.Stream.Clear(); rig.Client.RunApprovedOwnedFrame = false;
            await rig.RunAsync(false);
            var original = rig.Service.CurrentCanonicalTask!;
            var required = Assert.Single(rig.Stream, value => value.Kind == ChatStreamEventKind.PermissionRequired);
            await rig.Owner.ApproveOriginalAsync(required.PermissionRequest!.Id, TestContext.Current.CancellationToken);
            rig.Client.RunApprovedOwnedFrame = true;
            var actual = new List<ChatStreamEvent>();
            await foreach (var value in rig.Service.ContinueUnstartedOriginalAsync(original.TaskId, original.ExecutionId, TestContext.Current.CancellationToken)) actual.Add(value);
            var completed = rig.Service.CurrentCanonicalTask!;
            Assert.Equal(original.TaskId, completed.TaskId); Assert.Equal(original.ExecutionId, completed.ExecutionId);
            Assert.Equal(original.ContextId, completed.ContextId); Assert.Equal(TaskExecutionLifecycle.Completed, completed.State);
            Assert.True(completedTasks.Add(completed.TaskId)); Assert.True(completedRuns.Add(completed.ExecutionId));
            Assert.Equal(JsonSerializer.Serialize(original.RecoveryObservation), JsonSerializer.Serialize(Assert.Single(completed.RecoveryHistory)));
            Assert.Single(actual, value => value.Kind == ChatStreamEventKind.AssistantCompleted);
            Assert.DoesNotContain(actual, value => value.Kind == ChatStreamEventKind.UserMessage);
            Assert.Null(rig.Owner.GetOriginalOwner(required.PermissionRequest.Id)); // Actual resolved source record retired.
        }
        Assert.Equal(136, completedTasks.Count); Assert.Equal(136, completedRuns.Count);
        Assert.Equal(136, rig.Client.Dispatches);
        Assert.Equal(136, rig.Events.Observed.Count(value => value.SafeMetadata?.GetValueOrDefault("resolutionKind") == "live-never-started-original"));
        Assert.Equal(0, rig.Workspace.Effects);
    }

    [Fact]
    public async Task Held_then_faulted_actual_resumed_cleanup_refuses_retirement_and_preserves_original_recovery_history()
    {
        await using var rig = new Rig();
        await rig.RunAsync(false);
        var original = rig.Service.CurrentCanonicalTask!;
        var required = Assert.Single(rig.Stream, value => value.Kind == ChatStreamEventKind.PermissionRequired);
        await rig.Owner.ApproveOriginalAsync(required.PermissionRequest!.Id, TestContext.Current.CancellationToken);
        var held = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cause = new IOException("actual resumed stream cleanup failed");
        var resumedDisposeEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        rig.Client.NextDisposeEntered = resumedDisposeEntered;
        rig.Client.RunApprovedOwnedFrame = true; rig.Client.OriginalDispose = held.Task;
        var resumed = DrainAsync();
        try
        {
            await resumedDisposeEntered.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            Assert.True(Assert.Single(rig.Capture.ApprovedRunningOriginals).IsCompletedSuccessfully);
            Assert.False(resumed.IsCompleted);
            var waiting = (await rig.Tasks.GetAsync(original.TaskId, TestContext.Current.CancellationToken))!;
            Assert.Equal(TaskExecutionLifecycle.Running, waiting.State);
            Assert.Equal(original.TaskId, waiting.TaskId); Assert.Equal(original.ExecutionId, waiting.ExecutionId);
            Assert.Equal(TaskRunAttemptState.Running, Assert.Single(waiting.Attempts).State);
            Assert.DoesNotContain(rig.Stream, value => value.Kind == ChatStreamEventKind.AssistantCompleted);
            Assert.Single(waiting.RecoveryHistory);
            Assert.DoesNotContain(rig.Events.Observed, value => value.SafeMetadata?.GetValueOrDefault("resolutionKind") == "live-never-started-original");
            Assert.NotNull(rig.Owner.GetOriginalOwner(required.PermissionRequest.Id));
        }
        finally { held.TrySetException(cause); }
        var failure = await Record.ExceptionAsync(() => resumed);
        Assert.NotNull(failure); Assert.Contains(Leaves(failure!), value => ReferenceEquals(value, cause));
        var current = (await rig.Tasks.GetAsync(original.TaskId, TestContext.Current.CancellationToken))!;
        Assert.Equal(TaskExecutionLifecycle.Suspended, current.State);
        Assert.NotNull(current.RecoveryObservation);
        Assert.Equal(JsonSerializer.Serialize(original.RecoveryObservation), JsonSerializer.Serialize(Assert.Single(current.RecoveryHistory)));
        Assert.DoesNotContain(rig.Events.Observed, value => value.SafeMetadata?.GetValueOrDefault("resolutionKind") == "live-never-started-original");
        Assert.NotNull(rig.Owner.GetOriginalOwner(required.PermissionRequest.Id));
        async Task DrainAsync()
        { await foreach (var _ in rig.Service.ContinueUnstartedOriginalAsync(original.TaskId, original.ExecutionId, default)) { } }
    }

    private static IEnumerable<Exception> Leaves(Exception cause)
    {
        if (cause is AggregateException aggregate)
            foreach (var direct in aggregate.InnerExceptions) foreach (var leaf in Leaves(direct)) yield return leaf;
        else yield return cause;
    }

    private sealed class Rig : IAsyncDisposable
    {
        public Actors Actors { get; } = new(); public Provider Provider { get; } = new();
        public TaskRows TaskRows { get; } = new(); public RemediationRows RemediationRows { get; } = new();
        public Events Events { get; } = new(); public PermissionDecisionEngine Policy { get; } = new();
        public RemediationContinuationRegistry Registry { get; } = new();
        public TaskRunCentralCloudUsePermissionSource Source { get; }
        public TaskRunPermissionAuthority Authority { get; }
        public TaskRunOriginalFrameOwner Runtime { get; }
        public ControlledSettlement Settlement { get; }
        public TaskExecutionCoordinator Tasks { get; }
        public RemediationCoordinator Remediation { get; }
        public TaskRunCloudPermissionRemediationOwner Owner { get; }
        public Capture Capture { get; }
        public Client Client { get; }
        public Workspace Workspace { get; } = new(); public ToolsOwner ToolOwner { get; } = new();
        public Conversation Conversation { get; } = new(Guid.NewGuid(), HavenMode.Studio, ConversationKind.StudioChat,
            "synthetic caller", null, null, false, true, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch);
        public Conversations Conversations { get; } = new();
        public ChatSessionService Service { get; }
        public List<ChatStreamEvent> Stream { get; } = [];
        private readonly string _root = Path.Combine(Path.GetTempPath(), "astra-ask-caller-" + Guid.NewGuid().ToString("N"));
        public Rig(bool temporary = true)
        {
            Conversation = Conversation with { IsTemporary = temporary };
            Directory.CreateDirectory(_root);
            Source = new(Actors, Policy);
            Authority = new(Actors, new ProviderRegistry(Provider), new Configurations(), new Privacy(), new(new Permissions()), cloud: new ControlledCloudAdmission());
            TaskExecutionCoordinator? tasks = null;
            Runtime = new((task, run, attempt, token) => tasks!.TryGetIssuedAttemptAsync(task, run, attempt, token));
            Capture? originalCapture = null;
            Settlement = new(Runtime, () => originalCapture?.OriginalAsk);
            Tasks = tasks = new(TaskRows, Events, admissionAuthority: Authority, runtimeSettlement: Settlement);
            Remediation = new(RemediationRows, new Secrets(), Events, Registry);
            Owner = new(Source, Authority, () => Tasks, Remediation, RemediationRows, Registry, Events);
            Capture = originalCapture = new(Tasks, Authority, Source, Provider, Runtime);
            Client = new(Capture);
            Service = new(Conversations, Client, new CapabilityPreflightService(), new Safety(),
                new WorkspaceToolRuntime(Workspace), new ComputerToolRuntime(new Computer()),
                taskCoordinator: Tasks, taskToolOwner: ToolOwner, taskProviderContextCapture: Capture, taskCloudPermissionRemediation: Owner);
        }
        public async Task RunAsync(bool tools)
        {
            var selected = Provider.Model.Model with { Name = Provider.Model.Key,
                Capabilities = tools ? new HashSet<ToolCapability> { ToolCapability.Text, ToolCapability.Tools } : new HashSet<ToolCapability> { ToolCapability.Text } };
            await foreach (var value in Service.SendAsync(Conversation, tools ? "Create the file" : "synthetic permission request", selected,
                EffortLevel.Medium, [], "controlled", "", DuoMode.Solo, _root, null, null, null, default,
                taskExecutionIntent: TaskRunExecutionIntent.CanonicalAgenticTask))
            {
                if (value.Kind == ChatStreamEventKind.PermissionRequired)
                    Assert.Equal(TaskExecutionLifecycle.Suspended, Service.CurrentCanonicalTask!.State);
                Stream.Add(value);
            }
        }
        public async ValueTask DisposeAsync()
        {
            var errors = new List<Exception>();
            try { await Owner.CloseAndDrainAsync(); } catch (Exception cause) { errors.Add(cause); }
            try { await Runtime.CloseAndDrainAsync(); } catch (Exception cause) { errors.Add(cause); }
            try { Remediation.Dispose(); } catch (Exception cause) { errors.Add(cause); }
            try { Directory.Delete(_root, true); } catch (Exception cause) { errors.Add(cause); }
            if (errors.Count > 0) throw new AggregateException("Actual owning test close failed.", errors);
        }
    }
    private sealed class Capture(TaskExecutionCoordinator tasks, TaskRunPermissionAuthority authority,
        TaskRunCentralCloudUsePermissionSource source, Provider provider, TaskRunOriginalFrameOwner runtime) : ITaskRunProviderContextCapture
    {
        public bool AskDuringCapture = true;
        public int ChatCaptures; public int ToolCaptures;
        public Task? OriginalToolCapture = null;
        public TaskCompletionSource ToolCaptureEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public List<string> ChatWire { get; } = [];
        public bool OpenActualAttemptBeforeAsk;
        public TaskRunCloudPermissionRequiredException? OriginalAsk;
        public List<Task<TaskExecutionSnapshot>> ApprovedRunningOriginals { get; } = [];
        public Func<CancellationToken, Task<bool>>? ApprovedProviderBody = null;
        public async Task AskAsync(ProviderExecutionContext observation, CancellationToken token)
        {
            var actual = await tasks.GetAsync(observation.TaskId, token) ?? throw new InvalidOperationException();
            var candidate = await authority.CaptureSelectedRouteAsync(actual, provider.Model, [ToolCapability.Text], [], token);
            if (OpenActualAttemptBeforeAsk)
            {
                var issued = await tasks.StartAttemptAsync(actual.TaskId, actual.ExecutionId, candidate, token);
                await runtime.RegisterOriginalAttemptAsync(issued, token);
            }
            try { await using var gate = await source.AcquireOriginalAsync(actual.OwnerBinding!, candidate, token); }
            catch (TaskRunCloudPermissionRequiredException ask) { OriginalAsk = ask; throw; }
        }
        public async Task<bool> RunApprovedOriginalFrameAsync(ProviderExecutionContext observation, Action actualDispatch, CancellationToken token)
        {
            var current = (await tasks.GetAsync(observation.TaskId, token))!;
            var candidate = await authority.CaptureSelectedRouteAsync(current, provider.Model, [ToolCapability.Text], [], token);
            await using var scope = await source.AcquireOriginalAsync(current.OwnerBinding!, candidate, token);
            var issued = await tasks.StartAttemptAsync(current.TaskId, current.ExecutionId, candidate, token);
            await runtime.RegisterOriginalAttemptAsync(issued, token);
            var actualRunning = tasks.MarkAttemptRunningAsync(current.TaskId, current.ExecutionId, issued.AttemptId, token);
            ApprovedRunningOriginals.Add(actualRunning);
            await actualRunning.ConfigureAwait(false);
            var actual = scope.RunOriginalInvocation(() => runtime.StartOriginalFrameAsync(issued, _ =>
            { actualDispatch(); return ApprovedProviderBody is { } originalBody ? originalBody(_) : Task.FromResult(false); }, token));
            return await actual;
        }
        public ValueTask CaptureOriginalAsync(TaskExecutionSnapshot current, OllamaChatRequest request, TaskRunContextInventory inventory, CancellationToken token)
        {
            ChatCaptures++;
            ChatWire.Add(JsonSerializer.Serialize(new { request.Model, request.Messages, request.Effort, request.SystemPrompt, request.EnableTools, request.Options }));
            return AskDuringCapture ? new(AskAsync(request.ExecutionContext!, token)) : ValueTask.CompletedTask;
        }
        public ValueTask CaptureOriginalAsync(TaskExecutionSnapshot current, OllamaToolRequest request, TaskRunContextInventory inventory, CancellationToken token)
        {
            ToolCaptures++;
            ToolCaptureEntered.TrySetResult();
            return OriginalToolCapture is { } original ? new(original)
                : AskDuringCapture ? new(AskAsync(request.ExecutionContext!, token)) : ValueTask.CompletedTask;
        }
    }
    private sealed class Client(Capture capture) : IOllamaClient
    {
        private readonly Capture _capture = capture;
        public int Dispatches; public int StreamDisposals;
        public bool RunApprovedOwnedFrame;
        public Task? OriginalDispose;
        public TaskCompletionSource? NextDisposeEntered = null;
        public Task<OllamaToolResponse>? OriginalToolFailure;
        public TaskCompletionSource<OllamaToolRequest> ToolRequestEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Action? OriginalToolCallback = null;
        public TaskCompletionSource DisposeEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<bool> IsAvailableAsync(CancellationToken token) => Task.FromResult(true);
        public Task<IReadOnlyList<ModelDescriptor>> GetModelsAsync(CancellationToken token) => Task.FromResult<IReadOnlyList<ModelDescriptor>>([]);
        public Task<string> CompleteAsync(OllamaChatRequest request, CancellationToken token) => throw new InvalidOperationException("No nonowned compatibility provider call");
        public Task<OllamaToolResponse> ChatWithToolsAsync(OllamaToolRequest request, CancellationToken token)
        {
            ToolRequestEntered.TrySetResult(request);
            OriginalToolCallback?.Invoke();
            return OriginalToolFailure ?? AskToolAsync(request, token);
        }
        private async Task<OllamaToolResponse> AskToolAsync(OllamaToolRequest request, CancellationToken token)
        { await _capture.AskAsync(request.ExecutionContext!, token); Dispatches++; throw new InvalidOperationException("No approved provider fixture starts"); }
        public IAsyncEnumerable<string> StreamChatAsync(OllamaChatRequest request, CancellationToken token) => new Stream(this, request, token);
        private sealed class Stream(Client owner, OllamaChatRequest request, CancellationToken token) : IAsyncEnumerable<string>, IAsyncEnumerator<string>
        {
            public string Current => "unreachable provider effect";
            public IAsyncEnumerator<string> GetAsyncEnumerator(CancellationToken ignored = default) => this;
            public ValueTask<bool> MoveNextAsync() => new(MoveOriginalAsync());
            private async Task<bool> MoveOriginalAsync()
            {
                if (owner.RunApprovedOwnedFrame)
                    return await owner._capture.RunApprovedOriginalFrameAsync(request.ExecutionContext!, () => owner.Dispatches++, token);
                await owner._capture.AskAsync(request.ExecutionContext!, token); owner.Dispatches++; return false;
            }
            public ValueTask DisposeAsync()
            { owner.StreamDisposals++; owner.DisposeEntered.TrySetResult(); owner.NextDisposeEntered?.TrySetResult(); return owner.OriginalDispose is { } actual ? new(actual) : ValueTask.CompletedTask; }
        }

    }
    /// <summary>Controlled failure of the actual settlement PORT Task; the real registry remains retained
    /// and is actually joined during Rig disposal. This is not production settlement authority.</summary>
    private sealed class ControlledSettlement(TaskRunOriginalFrameOwner original, Func<Exception?> exactAsk)
        : ITaskRunRuntimeSettlement, ITaskRunOriginalAttemptRetirement, ITaskRunOriginalAttemptRegistrationSource
    {
        public TaskRunOriginalAttemptRegistrationDisposition RegisterOriginalAttemptDisposition(TaskRunAttemptAdmission sameOriginal, CancellationToken token) =>
            ((ITaskRunOriginalAttemptRegistrationSource)original).RegisterOriginalAttemptDisposition(sameOriginal, token);
        public bool IsIssuedOriginalAttemptRegistrationDisposition(TaskRunOriginalAttemptRegistrationDisposition receipt, TaskRunAttemptAdmission sameOriginal) =>
            ((ITaskRunOriginalAttemptRegistrationSource)original).IsIssuedOriginalAttemptRegistrationDisposition(receipt, sameOriginal);

        public bool FailWithOriginalAsk; public int Calls; public Task? ActualReturned;
        public ValueTask RetireAcknowledgedOriginalAttemptAsync(TaskRunOriginalRetirementAcknowledgment receipt, CancellationToken token) =>
            original.RetireAcknowledgedOriginalAttemptAsync(receipt, token); // Forward SAME private actual receipt to real owner.

        public Task AwaitSettlementAsync(Guid task, Guid run, Guid attempt, CancellationToken token)
        {
            Calls++;
            return ActualReturned = FailWithOriginalAsk
                ? Task.FromException(exactAsk() ?? throw new InvalidOperationException("Missing actual source-issued Ask"))
                : original.AwaitSettlementAsync(task, run, attempt, token);
        }
    }
    /// <summary>Synthetic admission for a NO-PROVIDER/EFFECT owning negative. This grants no real credential,
    /// monetary, network, context or account access and is never registered in production.</summary>
    private sealed class ControlledCloudAdmission : ITaskRunCloudAdmissionSource
    {
        public ValueTask<ITaskRunCloudAdmissionLease?> AcquireOriginalAsync(TaskExecutionOwnerBinding owner,
            ProviderModelDescriptor model, ProviderConfiguration config, TaskRunRouteCandidate candidate, CancellationToken token) =>
            ValueTask.FromResult<ITaskRunCloudAdmissionLease?>(new ControlledCloudLease());
        private sealed class ControlledCloudLease : ITaskRunCloudAdmissionLease
        {
            public ValueTask RevalidateAsync(CancellationToken token) { token.ThrowIfCancellationRequested(); return ValueTask.CompletedTask; }
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
    private sealed class TaskRows : ITaskExecutionRepository
    {
        private readonly Dictionary<Guid, string> _rows = [];
        public Task<TaskExecutionSnapshot?>? OverrideRead = null;
        public bool RefuseSuspension;
        public Task? ContinuationWrite; public int ContinuationWriteCalls;
        public TaskCompletionSource OverrideReadEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task UpsertAsync(TaskExecutionSnapshot value, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (value.RecoveryObservation is null && value.RecoveryHistory.Count > 0 && ContinuationWrite is { } actual)
            { ContinuationWriteCalls++; return actual; }
            if (RefuseSuspension && value.State == TaskExecutionLifecycle.Suspended) throw new TaskExecutionRevisionConflictException(value.TaskId, value.PersistenceRevision - 1, value.PersistenceRevision);
            var before = _rows.TryGetValue(value.TaskId, out var json) ? JsonSerializer.Deserialize<TaskExecutionSnapshot>(json) : null;
            if (value.PersistenceRevision != (before?.PersistenceRevision ?? 0) + 1) throw new TaskExecutionRevisionConflictException(value.TaskId, value.PersistenceRevision - 1, before?.PersistenceRevision ?? 0);
            _rows[value.TaskId] = JsonSerializer.Serialize(value); return Task.CompletedTask;
        }
        public Task<TaskExecutionSnapshot?> GetAsync(Guid id, CancellationToken token)
        {
            if (OverrideRead is { } actual) { OverrideReadEntered.TrySetResult(); return actual; }
            return Task.FromResult(_rows.TryGetValue(id, out var row) ? JsonSerializer.Deserialize<TaskExecutionSnapshot>(row) : null);
        }
        public async Task<TaskExecutionSnapshot?> GetByContextAsync(Guid id, CancellationToken token) => (await GetResumableAsync(token)).FirstOrDefault(value => value.ContextId == id);
        public Task<IReadOnlyList<TaskExecutionSnapshot>> GetResumableAsync(CancellationToken token) => Task.FromResult<IReadOnlyList<TaskExecutionSnapshot>>(_rows.Values.Select(value => JsonSerializer.Deserialize<TaskExecutionSnapshot>(value)!).ToArray());
    }
    private sealed class RemediationRows : IRemediationRepository
    {
        public readonly Dictionary<Guid, RemediationRequest> Rows = []; public int Writes;
        public Task UpsertAsync(RemediationRequest value, CancellationToken token) { token.ThrowIfCancellationRequested(); Writes++; Rows[value.Id] = value; return Task.CompletedTask; }
        public Task<RemediationRequest?> GetAsync(Guid id, CancellationToken token) => Task.FromResult(Rows.GetValueOrDefault(id));
        public Task<IReadOnlyList<RemediationRequest>> GetWaitingAsync(CancellationToken token) => Task.FromResult<IReadOnlyList<RemediationRequest>>(Rows.Values.Where(value => value.State is RemediationState.Waiting or RemediationState.InProgress).ToArray());
    }
    private sealed class Events : IExecutionEventSink
    { public Action<ExecutionEvent>? OnEvent = null; public List<ExecutionEvent> Observed { get; } = [];
      public bool TryPublish(ExecutionEvent value) { OnEvent?.Invoke(value); Observed.Add(value); return true; } }
    private sealed class Actors : IAuthenticatedResourceActorSource
    {
        public AuthenticatedResourceActor? Current = new("synthetic-task-owner", "synthetic-task-profile", null, null, "current-revision");
        public ValueTask<AuthenticatedResourceActor?> GetCurrentAsync(CancellationToken token) { token.ThrowIfCancellationRequested(); return ValueTask.FromResult(Current); }
    }
    private sealed class Provider : IModelProvider
    {
        public string Id => "synthetic-provider"; public string DisplayName => Id; public bool IsLocal => false; public bool CanManageModels => false;
        public ModelProviderKind Kind => ModelProviderKind.OpenAI; public int Starts;
        public ProviderModelDescriptor Model = new("synthetic-provider", false, new ModelDescriptor("synthetic-model", 10, "synthetic-family", "7B", "Q8", new HashSet<ToolCapability> { ToolCapability.Text, ToolCapability.Tools }, DateTimeOffset.UnixEpoch));
        public Task<IReadOnlyList<ProviderModelDescriptor>> GetModelsAsync(CancellationToken token) => Task.FromResult<IReadOnlyList<ProviderModelDescriptor>>(new[] { Model });
        public Task<ProviderHealthStatus> CheckHealthAsync(CancellationToken token) => Task.FromResult(new ProviderHealthStatus(Id, true, "synthetic", TimeSpan.Zero, DateTimeOffset.UnixEpoch));
        public IAsyncEnumerable<string> StreamChatAsync(OllamaChatRequest request, CancellationToken token) { Starts++; throw new NotSupportedException(); }
        public Task<string> CompleteAsync(OllamaChatRequest request, CancellationToken token) { Starts++; throw new NotSupportedException(); }
        public Task<OllamaToolResponse> ChatWithToolsAsync(OllamaToolRequest request, CancellationToken token) { Starts++; throw new NotSupportedException(); }
    }
    private sealed class ProviderRegistry(IModelProvider original) : IModelProviderRegistry
    {
        public IReadOnlyList<IModelProvider> Providers => new[] { original };
        public IModelProvider? Find(string id) => original.Id == id ? original : null;
        public IModelProvider GetRequired(string id) => Find(id) ?? throw new InvalidOperationException();
        public Task<IReadOnlyList<ProviderModelDescriptor>> GetModelsAsync(CancellationToken token) => original.GetModelsAsync(token);
    }
    private sealed class Configurations : IProviderConfigurationStore
    {
        private readonly ProviderConfiguration _value = new("synthetic-provider", ModelProviderKind.OpenAI, "synthetic-provider", "https://synthetic.invalid", true, false, false, new Dictionary<string, string>(), DateTimeOffset.UnixEpoch);
        public Task<ProviderConfiguration?> GetAsync(string id, CancellationToken token) => Task.FromResult<ProviderConfiguration?>(id == _value.Id ? _value : null);
        public Task<IReadOnlyList<ProviderConfiguration>> GetAllAsync(CancellationToken token) => Task.FromResult<IReadOnlyList<ProviderConfiguration>>(new[] { _value });
        public Task UpsertAsync(ProviderConfiguration value, CancellationToken token) => throw new NotSupportedException();
        public Task DeleteAsync(string id, CancellationToken token) => throw new NotSupportedException();
    }
    private sealed class Privacy : IPrivacyPreferenceStore
    { public PrivacyPreferences Current => PrivacyPreferences.Default with { LocalOnlyMode = false }; public Task UpdateAsync(PrivacyPreferences value, CancellationToken token) => throw new NotSupportedException(); }
    private sealed class Permissions : IModelPermissionStore
    { public Task<ModelPermissionPolicy> GetPolicyAsync(CancellationToken token) => Task.FromResult(ModelPermissionPolicy.Empty); public Task SavePolicyAsync(ModelPermissionPolicy value, CancellationToken token) => throw new NotSupportedException(); }
    private sealed class Secrets : IProviderSecretStore
    {
        public Task<string?> GetAsync(string provider, string name, CancellationToken token) => Task.FromResult<string?>(null);
        public Task SetAsync(string provider, string name, string value, CancellationToken token) => throw new NotSupportedException();
        public Task DeleteAsync(string provider, string name, CancellationToken token) => throw new NotSupportedException();
    }
    private sealed class Conversations : IConversationRepository
    {
        public Conversation? Current; public int Writes; public int Reads; public List<ChatMessage> Messages { get; } = [];
        public Task<IReadOnlyList<ChatMessage>>? OverrideHistory = null;
        public Task<Conversation?> GetAsync(Guid id, CancellationToken token) { Reads++; return Task.FromResult(Current?.Id == id ? Current : null); }
        public Task<IReadOnlyList<Conversation>> GetRecentAsync(HavenMode? mode, int count, CancellationToken token) => Task.FromResult<IReadOnlyList<Conversation>>(Current is null ? [] : [Current]);
        public Task<IReadOnlyList<ChatMessage>> GetMessagesAsync(Guid id, CancellationToken token) => OverrideHistory
            ?? Task.FromResult<IReadOnlyList<ChatMessage>>(Messages.Where(value => value.ConversationId == id).ToArray());
        public Task UpsertConversationAsync(Conversation value, CancellationToken token) { Writes++; Current = value; return Task.CompletedTask; }
        public Task AddMessageAsync(ChatMessage message, CancellationToken token) { Writes++; Messages.Add(message); return Task.CompletedTask; }
        public Task DeleteConversationAsync(Guid id, CancellationToken token) => throw new NotSupportedException();
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
