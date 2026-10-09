using System.Reflection;
using Haven.Application;
using Haven.Core;
using Xunit;

namespace Haven.Desktop.Tests;

/// <summary>Actual initial Chat/coordinator/source Ask and real observer drivers. Existing Rig actors,
/// catalogue, repositories and transport are controlled; no installed model/account/provider acceptance.</summary>
public sealed partial class ChatCloudPermissionCallerTests
{
    [Fact]
    public async Task Initial_host_projects_actual_user_and_permission_events_with_same_acknowledged_context()
    {
        await using var rig = new Rig(temporary: false);
        var conversation = await PersistInitialTaskConversationAsync(rig);
        var lease = await StartInitialTaskObservationAsync(rig.Service, rig, conversation, TestContext.Current.CancellationToken);
        Assert.True(rig.Service.IsIssuedOriginalTaskObservation(lease));
        Assert.Equal(conversation.Id, lease.ConversationId);
        var events = new List<ChatStreamEvent>();
        await foreach (var value in lease.ObserveOriginalEventsAsync(TestContext.Current.CancellationToken)) events.Add(value);
        var result = await lease.WaitForOriginalObservationAsync(TestContext.Current.CancellationToken);
        Assert.Equal(TaskRunInitialChatObservationDisposition.ProducerTerminal, result.Disposition);
        var required = Assert.Single(events, value => value.Kind == ChatStreamEventKind.PermissionRequired);
        var actual = Assert.IsType<TaskExecutionSnapshot>(await rig.Tasks.GetByContextAsync(conversation.Id, TestContext.Current.CancellationToken));
        var context = Assert.IsType<ProviderExecutionContext>(lease.CurrentAcknowledgedContext);
        Assert.Equal(actual.TaskId, context.TaskId);
        Assert.Equal(actual.ExecutionId, context.ExecutionId);
        Assert.Equal(conversation.Id, context.ContextId);
        Assert.Equal(actual.PersistenceRevision, context.PersistenceRevision);
        Assert.Equal(context, result.AcknowledgedContext);
        Assert.Equal(context, required.CanonicalTaskContext);
        Assert.Equal(TaskExecutionLifecycle.Suspended, actual.State);
        Assert.Single(rig.Conversations.Messages, message => message.Role == MessageRole.User);
        Assert.DoesNotContain(events, value => value.Kind == ChatStreamEventKind.AssistantCompleted);
        Assert.Equal(0, rig.Client.Dispatches);
        Assert.Equal(0, rig.Workspace.Effects);
        await lease.DetachAndDrainAsync();
        Assert.True(rig.Service.IsIssuedOriginalTaskObservation(lease));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Initial_source_owns_new_or_title_changed_empty_draft_persistence_before_actual_send(bool persistedDraft)
    {
        await using var rig = new Rig(temporary: false);
        var draft = rig.Conversation with { Mode = HavenMode.Tasks, Kind = ConversationKind.Task, IsTemporary = false };
        if (persistedDraft) await rig.Conversations.UpsertConversationAsync(draft, TestContext.Current.CancellationToken);
        var submitted = draft with { Title = "same initial hosted input", UpdatedAt = draft.UpdatedAt.AddMinutes(1) };
        var lease = await StartInitialTaskObservationAsync(rig.Service, rig, submitted, TestContext.Current.CancellationToken);
        _ = await lease.WaitForOriginalObservationAsync(TestContext.Current.CancellationToken);
        var actual = (await rig.Tasks.GetByContextAsync(submitted.Id, TestContext.Current.CancellationToken))!;
        Assert.Equal(submitted.Id, actual.ContextId);
        Assert.Equal(submitted.Title, rig.Conversations.Current!.Title);
        Assert.Equal(submitted.CreatedAt, rig.Conversations.Current.CreatedAt);
        Assert.Equal(submitted.Kind, rig.Conversations.Current.Kind);
        Assert.Single(rig.Conversations.Messages, message => message.Role == MessageRole.User);
        Assert.Equal(TaskExecutionLifecycle.Suspended, actual.State);
        Assert.Equal(0, rig.Client.Dispatches);
        await lease.DetachAndDrainAsync();
    }

    [Fact]
    public async Task Initial_observer_detaches_while_same_business_provider_disposal_stays_pending_and_uncancelled()
    {
        await using var rig = new Rig(temporary: false);
        rig.Capture.AskDuringCapture = false;
        var held = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        rig.Client.OriginalDispose = held.Task;
        var conversation = await PersistInitialTaskConversationAsync(rig);
        var lease = await StartInitialTaskObservationAsync(rig.Service, rig, conversation, TestContext.Current.CancellationToken);
        var producer = InitialHostedProducer(lease);
        var reader = lease.ObserveOriginalEventsAsync(TestContext.Current.CancellationToken)
            .GetAsyncEnumerator(TestContext.Current.CancellationToken);
        try
        {
            Assert.True(await reader.MoveNextAsync());
            var sameFirstEvent = reader.Current;
            var pendingMove = reader.MoveNextAsync().AsTask();
            await rig.Client.DisposeEntered.Task.WaitAsync(TestContext.Current.CancellationToken);
            Assert.False(producer.IsCompleted);
            Assert.False(held.Task.IsCompleted);
            lease.RequestOriginalObservationRetirement();
            Assert.False(await pendingMove);
            Assert.Same(sameFirstEvent, reader.Current);
            var detached = await lease.WaitForOriginalObservationAsync(TestContext.Current.CancellationToken);
            Assert.Equal(TaskRunInitialChatObservationDisposition.ObservationDetached, detached.Disposition);
            await lease.DetachAndDrainAsync();
            Assert.False(producer.IsCompleted);
            Assert.False(held.Task.IsCanceled);
            var sameRunning = Assert.IsType<TaskExecutionSnapshot>(await rig.Tasks.GetByContextAsync(conversation.Id, TestContext.Current.CancellationToken));
            Assert.Equal(TaskExecutionLifecycle.Running, sameRunning.State);
            Assert.Equal(sameRunning.TaskId, lease.CurrentAcknowledgedContext!.TaskId);
            Assert.Equal(sameRunning.ExecutionId, lease.CurrentAcknowledgedContext.ExecutionId);
            held.SetResult();
            await producer;
            var sameSuspended = (await rig.Tasks.GetByContextAsync(conversation.Id, TestContext.Current.CancellationToken))!;
            Assert.Equal(sameRunning.TaskId, sameSuspended.TaskId);
            Assert.Equal(sameRunning.ExecutionId, sameSuspended.ExecutionId);
            Assert.Equal(TaskExecutionLifecycle.Suspended, sameSuspended.State);
            Assert.Equal(1, rig.Client.StreamDisposals);
            Assert.Single(rig.Conversations.Messages, message => message.Role == MessageRole.User);
        }
        finally
        {
            held.TrySetResult();
            await reader.DisposeAsync();
            await lease.DetachAndDrainAsync();
            await producer;
        }
    }

    [Fact]
    public async Task Initial_caller_wait_cancellation_withdraws_only_observation_and_retains_actual_business_driver()
    {
        await using var rig = new Rig(temporary: false);
        rig.Capture.AskDuringCapture = false;
        var held = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        rig.Client.OriginalDispose = held.Task;
        var conversation = await PersistInitialTaskConversationAsync(rig);
        var lease = await StartInitialTaskObservationAsync(rig.Service, rig, conversation, TestContext.Current.CancellationToken);
        var producer = InitialHostedProducer(lease);
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        try
        {
            await rig.Client.DisposeEntered.Task.WaitAsync(TestContext.Current.CancellationToken);
            var actualWait = lease.WaitForOriginalObservationAsync(caller.Token);
            caller.Cancel();
            _ = await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => { _ = await actualWait; });
            Assert.True(actualWait.IsCanceled);
            await lease.DetachAndDrainAsync();
            Assert.False(producer.IsCompleted);
            Assert.False(held.Task.IsCanceled);
            Assert.True(rig.Service.IsIssuedOriginalTaskObservation(lease));
            held.SetResult(); await producer;
            var actual = (await rig.Tasks.GetByContextAsync(conversation.Id, TestContext.Current.CancellationToken))!;
            Assert.Equal(TaskExecutionLifecycle.Suspended, actual.State);
            Assert.Equal(actual.TaskId, lease.CurrentAcknowledgedContext!.TaskId);
            Assert.Equal(actual.ExecutionId, lease.CurrentAcknowledgedContext.ExecutionId);
            Assert.Equal(0, rig.Client.Dispatches);
        }
        finally { held.TrySetResult(); await lease.DetachAndDrainAsync(); await producer; }
    }

    [Fact]
    public async Task Initial_source_refuses_existing_context_or_foreign_lease_without_second_user_or_replacement_run()
    {
        await using var rig = new Rig(temporary: false);
        await using var foreign = new Rig();
        var conversation = await PersistInitialTaskConversationAsync(rig);
        var lease = await StartInitialTaskObservationAsync(rig.Service, rig, conversation, TestContext.Current.CancellationToken);
        _ = await lease.WaitForOriginalObservationAsync(TestContext.Current.CancellationToken);
        await lease.DetachAndDrainAsync();
        var same = (await rig.Tasks.GetByContextAsync(conversation.Id, TestContext.Current.CancellationToken))!;
        _ = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        { _ = await StartInitialTaskObservationAsync(rig.Service, rig, rig.Conversations.Current!, TestContext.Current.CancellationToken); });
        Assert.False(foreign.Service.IsIssuedOriginalTaskObservation(lease));
        var copied = (TaskRunOriginalInitialChatObservationLease)typeof(object)
            .GetMethod("MemberwiseClone", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(lease, null)!;
        Assert.False(rig.Service.IsIssuedOriginalTaskObservation(copied));
        _ = Assert.Throws<UnauthorizedAccessException>(() =>
        { _ = rig.Service.StopObservedOriginalTaskAsync(copied, TestContext.Current.CancellationToken); });
        _ = Assert.Throws<UnauthorizedAccessException>(() =>
        { _ = foreign.Service.StopObservedOriginalTaskAsync(lease, TestContext.Current.CancellationToken); });
        var after = (await rig.Tasks.GetByContextAsync(conversation.Id, TestContext.Current.CancellationToken))!;
        Assert.Equal(same.TaskId, after.TaskId);
        Assert.Equal(same.ExecutionId, after.ExecutionId);
        Assert.Equal(same.PersistenceRevision, after.PersistenceRevision);
        Assert.Single(rig.Conversations.Messages, message => message.Role == MessageRole.User);
        Assert.Single(await rig.TaskRows.GetResumableAsync(TestContext.Current.CancellationToken));
        Assert.Equal(0, rig.Client.Dispatches);
    }

    [Fact]
    public async Task Initial_source_history_compound_fault_retains_same_raw_task_and_all_direct_causes_before_begin()
    {
        await using var rig = new Rig(temporary: false);
        var conversation = await PersistInitialTaskConversationAsync(rig);
        var first = new OperationCanceledException("Faulted original history read, live caller token.");
        var second = new IOException("Independent original history cause.");
        var raw = new TaskCompletionSource<IReadOnlyList<ChatMessage>>(TaskCreationOptions.RunContinuationsAsynchronously);
        raw.SetException(new Exception[] { first, second });
        rig.Conversations.OverrideHistory = raw.Task;
        var actual = StartInitialTaskObservationAsync(rig.Service, rig, conversation, TestContext.Current.CancellationToken);
        var thrown = await Assert.ThrowsAnyAsync<Exception>(async () => { _ = await actual; });
        Assert.True(actual.IsFaulted);
        Assert.True(raw.Task.IsFaulted);
        Assert.Contains(Leaves(thrown), cause => ReferenceEquals(cause, first));
        Assert.Contains(Leaves(thrown), cause => ReferenceEquals(cause, second));
        var stages = Assert.IsAssignableFrom<System.Collections.IEnumerable>(
            typeof(TaskExecutionCoordinator).GetField("_originalProcessStages", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(rig.Tasks));
        var stage = Assert.Single(stages.Cast<object>());
        var driver = Assert.IsAssignableFrom<Task>(stage.GetType().GetField("ActualDriver", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(stage));
        var sources = Assert.IsAssignableFrom<IReadOnlyList<Task>>(stage.GetType().GetProperty("ActualSources", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(stage));
        Assert.Contains(sources, source => ReferenceEquals(source, raw.Task));
        Assert.True(driver.IsFaulted);
        Assert.Empty(await rig.TaskRows.GetResumableAsync(TestContext.Current.CancellationToken));
        Assert.Empty(rig.Conversations.Messages);
        Assert.Equal(0, rig.Capture.ChatCaptures);
        Assert.Equal(0, rig.Client.Dispatches);
    }

    [Fact]
    public async Task Initial_pending_source_guard_refuses_restored_context_join_after_actual_history_await()
    {
        await using var rig = new Rig(temporary: false);
        var conversation = await PersistInitialTaskConversationAsync(rig);
        var held = new TaskCompletionSource<IReadOnlyList<ChatMessage>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var probe = new InitialObservationConversationProbe(rig.Conversations) { HeldFirstHistory = held.Task };
        var restored = ExecutionContext.Capture()!;
        Exception? selfJoin = null;
        ChatSessionService? service = null;
        probe.AfterFirstHistoryGet = () => ExecutionContext.Run(restored, _ =>
        { selfJoin = Assert.Throws<InvalidOperationException>(() => service!.DemandExternalOriginalTaskObservationSourceJoin()); }, null);
        service = new(probe, rig.Client, new CapabilityPreflightService(), new Safety(),
            new WorkspaceToolRuntime(rig.Workspace), new ComputerToolRuntime(new Computer()),
            taskCoordinator: rig.Tasks, taskToolOwner: rig.ToolOwner, taskProviderContextCapture: rig.Capture,
            taskCloudPermissionRemediation: rig.Owner);
        var actual = StartInitialTaskObservationAsync(service, rig, conversation, TestContext.Current.CancellationToken);
        try
        {
            await probe.HistoryEntered.Task.WaitAsync(TestContext.Current.CancellationToken);
            Assert.False(actual.IsCompleted);
            held.SetResult([]);
            var lease = await actual;
            Assert.IsType<InvalidOperationException>(selfJoin);
            _ = await lease.WaitForOriginalObservationAsync(TestContext.Current.CancellationToken);
            await lease.DetachAndDrainAsync();
            Assert.True(service.IsIssuedOriginalTaskObservation(lease));
            Assert.False(rig.Service.IsIssuedOriginalTaskObservation(lease));
            Assert.Single(rig.Conversations.Messages, message => message.Role == MessageRole.User);
        }
        finally { held.TrySetResult([]); }
    }

    [Fact]
    public async Task Initial_explicit_stop_facade_uses_same_acknowledged_no_attempt_run_and_fresh_actor()
    {
        await using var rig = new Rig(temporary: false);
        var conversation = await PersistInitialTaskConversationAsync(rig);
        var lease = await StartInitialTaskObservationAsync(rig.Service, rig, conversation, TestContext.Current.CancellationToken);
        _ = await lease.WaitForOriginalObservationAsync(TestContext.Current.CancellationToken);
        var same = lease.CurrentAcknowledgedContext!;
        var owner = rig.Actors.Current;
        rig.Actors.Current = null;
        _ = await Assert.ThrowsAnyAsync<Exception>(async () =>
        { _ = await rig.Service.StopObservedOriginalTaskAsync(lease, TestContext.Current.CancellationToken); });
        var unchanged = (await rig.Tasks.GetByContextAsync(conversation.Id, TestContext.Current.CancellationToken))!;
        Assert.Equal(same.TaskId, unchanged.TaskId);
        Assert.Equal(same.ExecutionId, unchanged.ExecutionId);
        Assert.Equal(TaskExecutionLifecycle.Suspended, unchanged.State);
        rig.Actors.Current = owner;
        _ = await rig.Service.StopObservedOriginalTaskAsync(lease, TestContext.Current.CancellationToken);
        var cancelled = (await rig.Tasks.GetByContextAsync(conversation.Id, TestContext.Current.CancellationToken))!;
        Assert.Equal(same.TaskId, cancelled.TaskId);
        Assert.Equal(same.ExecutionId, cancelled.ExecutionId);
        Assert.Equal(TaskExecutionLifecycle.Cancelled, cancelled.State);
        Assert.Empty(cancelled.Attempts);
        Assert.Equal(0, rig.Client.Dispatches);
        await lease.DetachAndDrainAsync();
    }

    [Fact]
    public async Task Initial_process_close_joins_same_detached_business_driver_and_held_cleanup_before_fault_acknowledgment()
    {
        await using var rig = new Rig(temporary: false);
        rig.Capture.AskDuringCapture = false;
        var held = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        rig.Client.OriginalDispose = held.Task;
        var conversation = await PersistInitialTaskConversationAsync(rig);
        var lease = await StartInitialTaskObservationAsync(rig.Service, rig, conversation, TestContext.Current.CancellationToken);
        var producer = InitialHostedProducer(lease);
        Task? processClose = null;
        try
        {
            await rig.Client.DisposeEntered.Task.WaitAsync(TestContext.Current.CancellationToken);
            lease.RequestOriginalObservationRetirement();
            await lease.DetachAndDrainAsync();
            Assert.False(producer.IsCompleted);
            rig.Tasks.RequestOriginalProcessRetirement();
            processClose = rig.Tasks.CloseAndSuspendOriginalProducersAsync();
            Assert.False(processClose.IsCompleted);
            Assert.False(held.Task.IsCompleted);
            _ = Assert.Throws<InvalidOperationException>(() =>
            { _ = lease.ObserveOriginalEventsAsync(TestContext.Current.CancellationToken).GetAsyncEnumerator(TestContext.Current.CancellationToken); });
            Assert.Same(processClose, rig.Tasks.CloseAndSuspendOriginalProducersAsync());
            held.SetResult();
            _ = await Assert.ThrowsAnyAsync<Exception>(async () => { await processClose; });
            Assert.True(processClose.IsFaulted);
            Assert.True(producer.IsFaulted); // Actual new Move refusal after process seal remains an original fault.
            Assert.False(held.Task.IsCanceled);
            Assert.Equal(1, rig.Client.StreamDisposals);
            Assert.Equal(0, rig.Client.Dispatches);
            Assert.True(rig.Service.IsIssuedOriginalTaskObservation(lease));
        }
        finally
        {
            held.TrySetResult();
            if (processClose is not null)
                _ = await Record.ExceptionAsync(async () => { await processClose; });
            _ = await Record.ExceptionAsync(async () => { await producer; });
            await lease.DetachAndDrainAsync();
        }
    }

    [Fact]
    public async Task Initial_actual_business_snapshot_callback_can_request_and_capture_observer_detach_without_business_join()
    {
        await using var rig = new Rig(temporary: false);
        rig.Capture.AskDuringCapture = false;
        var held = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        rig.Client.OriginalDispose = held.Task;
        var conversation = await PersistInitialTaskConversationAsync(rig);
        TaskRunOriginalInitialChatObservationLease? issued = null;
        Task? actualDetach = null;
        rig.Tasks.SnapshotChanged += (_, actual) =>
        {
            if (actual.State == TaskExecutionLifecycle.Suspended && issued is { } sameLease)
            {
                sameLease.DemandExternalOriginalObservationJoin();
                sameLease.RequestOriginalObservationRetirement();
                actualDetach = sameLease.DetachAndDrainAsync(); // Observation-only; does not join the current business callback.
            }
        };
        issued = await StartInitialTaskObservationAsync(rig.Service, rig, conversation, TestContext.Current.CancellationToken);
        var producer = InitialHostedProducer(issued);
        try
        {
            await rig.Client.DisposeEntered.Task.WaitAsync(TestContext.Current.CancellationToken);
            Assert.False(producer.IsCompleted);
            held.SetResult();
            await producer;
            var detach = Assert.IsAssignableFrom<Task>(actualDetach);
            Assert.Same(detach, issued.DetachAndDrainAsync());
            await detach;
            var result = await issued.WaitForOriginalObservationAsync(TestContext.Current.CancellationToken);
            Assert.Equal(TaskRunInitialChatObservationDisposition.ObservationDetached, result.Disposition);
            var actual = (await rig.Tasks.GetByContextAsync(conversation.Id, TestContext.Current.CancellationToken))!;
            Assert.Equal(TaskExecutionLifecycle.Suspended, actual.State);
            Assert.Equal(actual.TaskId, issued.CurrentAcknowledgedContext!.TaskId);
            Assert.Equal(actual.ExecutionId, issued.CurrentAcknowledgedContext.ExecutionId);
            Assert.True(rig.Service.IsIssuedOriginalTaskObservation(issued));
        }
        finally { held.TrySetResult(); await issued.DetachAndDrainAsync(); await producer; }
    }

    [Fact]
    public async Task Initial_parent_observation_cancellation_retains_pending_acquisition_until_real_late_lease_and_detach_ack()
    {
        await using var rig = new Rig(temporary: false);
        var conversation = await PersistInitialTaskConversationAsync(rig);
        var held = new TaskCompletionSource<IReadOnlyList<ChatMessage>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var probe = new InitialObservationConversationProbe(rig.Conversations) { HeldFirstHistory = held.Task };
        var service = new ChatSessionService(probe, rig.Client, new CapabilityPreflightService(), new Safety(),
            new WorkspaceToolRuntime(rig.Workspace), new ComputerToolRuntime(new Computer()),
            taskCoordinator: rig.Tasks, taskToolOwner: rig.ToolOwner, taskProviderContextCapture: rig.Capture,
            taskCloudPermissionRemediation: rig.Owner);
        using var parentObservation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var actualAcquisition = StartInitialTaskObservationAsync(service, rig, conversation, parentObservation.Token);
        TaskRunOriginalInitialChatObservationLease? lease = null;
        try
        {
            await probe.HistoryEntered.Task.WaitAsync(TestContext.Current.CancellationToken);
            parentObservation.Cancel();
            Assert.False(actualAcquisition.IsCompleted);
            Assert.False(held.Task.IsCanceled);
            Assert.Empty(await rig.TaskRows.GetResumableAsync(TestContext.Current.CancellationToken));
            held.SetResult([]);
            lease = await actualAcquisition;
            Assert.True(actualAcquisition.IsCompletedSuccessfully);
            Assert.True(service.IsIssuedOriginalTaskObservation(lease));
            var detach = lease.DetachAndDrainAsync();
            await detach;
            Assert.Same(detach, lease.DetachAndDrainAsync());
            Assert.Equal(TaskRunInitialChatObservationDisposition.ObservationDetached,
                (await lease.WaitForOriginalObservationAsync(TestContext.Current.CancellationToken)).Disposition);
            await InitialHostedProducer(lease);
            var actual = (await rig.Tasks.GetByContextAsync(conversation.Id, TestContext.Current.CancellationToken))!;
            Assert.Equal(TaskExecutionLifecycle.Suspended, actual.State);
            Assert.Equal(actual.TaskId, lease.CurrentAcknowledgedContext!.TaskId);
            Assert.Equal(actual.ExecutionId, lease.CurrentAcknowledgedContext.ExecutionId);
            Assert.Single(rig.Conversations.Messages, message => message.Role == MessageRole.User);
            Assert.Equal(0, rig.Client.Dispatches);
        }
        finally
        {
            held.TrySetResult([]);
            lease ??= await actualAcquisition;
            await lease.DetachAndDrainAsync();
            await InitialHostedProducer(lease);
        }
    }

    [Fact]
    public async Task Initial_held_raw_draft_write_stays_enrolled_across_real_process_seal_and_preserves_all_fault_causes()
    {
        await using var rig = new Rig(temporary: false);
        var conversation = rig.Conversation with { Mode = HavenMode.Tasks, Kind = ConversationKind.Task, IsTemporary = false };
        var rawWrite = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var probe = new InitialObservationConversationProbe(rig.Conversations) { HeldInitialWrite = rawWrite.Task };
        var service = new ChatSessionService(probe, rig.Client, new CapabilityPreflightService(), new Safety(),
            new WorkspaceToolRuntime(rig.Workspace), new ComputerToolRuntime(new Computer()),
            taskCoordinator: rig.Tasks, taskToolOwner: rig.ToolOwner, taskProviderContextCapture: rig.Capture,
            taskCloudPermissionRemediation: rig.Owner);
        var acquisition = StartInitialTaskObservationAsync(service, rig, conversation, TestContext.Current.CancellationToken);
        Task? close = null;
        var first = new OperationCanceledException("Faulted actual draft write, independently held source.");
        var second = new IOException("Second original draft write failure.");
        try
        {
            await probe.WriteEntered.Task.WaitAsync(TestContext.Current.CancellationToken);
            rig.Tasks.RequestOriginalProcessRetirement();
            close = rig.Tasks.CloseAndSuspendOriginalProducersAsync();
            Assert.False(close.IsCompleted);
            Assert.False(rawWrite.Task.IsCompleted);
            Assert.False(acquisition.IsCompleted);
            rawWrite.SetException(new Exception[] { first, second });
            var sourceFailure = await Assert.ThrowsAnyAsync<Exception>(async () => { _ = await acquisition; });
            var closeFailure = await Assert.ThrowsAnyAsync<Exception>(async () => { await close; });
            Assert.True(rawWrite.Task.IsFaulted);
            Assert.True(acquisition.IsFaulted);
            Assert.True(close.IsFaulted);
            Assert.Contains(Leaves(sourceFailure), cause => ReferenceEquals(cause, first));
            Assert.Contains(Leaves(sourceFailure), cause => ReferenceEquals(cause, second));
            Assert.Contains(Leaves(closeFailure), cause => ReferenceEquals(cause, first));
            Assert.Contains(Leaves(closeFailure), cause => ReferenceEquals(cause, second));
            var stages = Assert.IsAssignableFrom<System.Collections.IEnumerable>(typeof(TaskExecutionCoordinator)
                .GetField("_originalProcessStages", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(rig.Tasks));
            var stage = Assert.Single(stages.Cast<object>());
            var sources = Assert.IsAssignableFrom<IReadOnlyList<Task>>(stage.GetType()
                .GetProperty("ActualSources", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(stage));
            Assert.Contains(sources, original => ReferenceEquals(original, rawWrite.Task));
            Assert.Contains(sources, original => ReferenceEquals(original, acquisition));
            Assert.Empty(await rig.TaskRows.GetResumableAsync(TestContext.Current.CancellationToken));
            Assert.Empty(rig.Conversations.Messages);
            Assert.Equal(0, rig.Client.Dispatches);
        }
        finally
        {
            rawWrite.TrySetException(new Exception[] { first, second });
            _ = await Record.ExceptionAsync(async () => { _ = await acquisition; });
            if (close is not null) _ = await Record.ExceptionAsync(async () => { await close; });
        }
    }

    [Fact]
    public async Task Initial_actual_canceled_history_source_stays_distinct_from_faulted_OCE_with_live_caller_token()
    {
        await using var rig = new Rig(temporary: false);
        var conversation = await PersistInitialTaskConversationAsync(rig);
        using var canceledSource = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        canceledSource.Cancel();
        var sameRaw = Task.FromCanceled<IReadOnlyList<ChatMessage>>(canceledSource.Token);
        rig.Conversations.OverrideHistory = sameRaw;
        var acquisition = StartInitialTaskObservationAsync(rig.Service, rig, conversation, TestContext.Current.CancellationToken);
        _ = await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => { _ = await acquisition; });
        Assert.True(sameRaw.IsCanceled);
        Assert.True(acquisition.IsCanceled);
        Assert.False(acquisition.IsFaulted);
        var stages = Assert.IsAssignableFrom<System.Collections.IEnumerable>(typeof(TaskExecutionCoordinator)
            .GetField("_originalProcessStages", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(rig.Tasks));
        var stage = Assert.Single(stages.Cast<object>());
        var sources = Assert.IsAssignableFrom<IReadOnlyList<Task>>(stage.GetType()
            .GetProperty("ActualSources", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(stage));
        Assert.Contains(sources, original => ReferenceEquals(original, sameRaw));
        Assert.Contains(sources, original => ReferenceEquals(original, acquisition));
        Assert.Empty(await rig.TaskRows.GetResumableAsync(TestContext.Current.CancellationToken));
        Assert.Empty(rig.Conversations.Messages);
        Assert.Equal(0, rig.Client.Dispatches);
    }

    private static async Task<Conversation> PersistInitialTaskConversationAsync(Rig rig)
    {
        var conversation = rig.Conversation with { Mode = HavenMode.Tasks, Kind = ConversationKind.Task, IsTemporary = false };
        await rig.Conversations.UpsertConversationAsync(conversation, TestContext.Current.CancellationToken);
        return conversation;
    }

    private static Task<TaskRunOriginalInitialChatObservationLease> StartInitialTaskObservationAsync(
        ChatSessionService service, Rig rig, Conversation conversation, CancellationToken token) =>
        service.StartObservedOriginalTaskSendAsync(conversation, "same initial hosted input",
            rig.Provider.Model.Model with { Name = rig.Provider.Model.Key, Capabilities = new HashSet<ToolCapability> { ToolCapability.Text } },
            EffortLevel.Medium, [], "controlled", "", DuoMode.Solo, null, null, null, null, token);

    private static Task InitialHostedProducer(TaskRunOriginalInitialChatObservationLease lease)
    {
        var original = lease.GetType().GetField("Original", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(lease)!;
        return Assert.IsAssignableFrom<Task>(original.GetType().GetField("Producer", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(original));
    }

    private sealed class InitialObservationConversationProbe(IConversationRepository sameRepository) : IConversationRepository
    {
        public Task<IReadOnlyList<ChatMessage>>? HeldFirstHistory = null;
        public Action? AfterFirstHistoryGet = null;
        public Task? HeldInitialWrite = null;
        public TaskCompletionSource WriteEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource HistoryEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _historyReads;
        public Task<Conversation?> GetAsync(Guid id, CancellationToken token)
        { if (_historyReads > 0) AfterFirstHistoryGet?.Invoke(); return sameRepository.GetAsync(id, token); }
        public Task<IReadOnlyList<Conversation>> GetRecentAsync(HavenMode? mode, int count, CancellationToken token) => sameRepository.GetRecentAsync(mode, count, token);
        public Task<IReadOnlyList<ChatMessage>> GetMessagesAsync(Guid id, CancellationToken token)
        {
            if (++_historyReads == 1 && HeldFirstHistory is { } original) { HistoryEntered.TrySetResult(); return original; }
            return sameRepository.GetMessagesAsync(id, token);
        }
        public Task UpsertConversationAsync(Conversation value, CancellationToken token)
        { if (HeldInitialWrite is { } original) { WriteEntered.TrySetResult(); return original; } return sameRepository.UpsertConversationAsync(value, token); }
        public Task AddMessageAsync(ChatMessage message, CancellationToken token) => sameRepository.AddMessageAsync(message, token);
        public Task DeleteConversationAsync(Guid id, CancellationToken token) => sameRepository.DeleteConversationAsync(id, token);
    }
}
