using System.Text.Json;
using Haven.Application;
using Haven.Core;
using Xunit;

namespace Haven.Desktop.Tests;

/// <summary>Actual canonical coordinator, Task authority, original-frame owner and saved-Agent/Chat
/// producer over synthetic actors, local model bodies and JSON CAS controls. No real model, account,
/// native task launch, cloud or domain effect is claimed.</summary>
public sealed partial class ChatCloudPermissionCallerTests
{
    [Fact]
    public async Task Delegation_parent_intent_is_acknowledged_before_child_creation_and_duplicate_request_keeps_fixed_ids()
    {
        await using var rig = new ChildRig(); var parent = await rig.ParentAsync();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        rig.Rows.BeforeWrite = next => next.Delegations.Count == 0 ? null : HoldAsync(next);
        var actual = rig.Tasks.RegisterOriginalDelegationIntentAsync(parent, "fixed-once", new string('a', 64), "Child", [], TestContext.Current.CancellationToken);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken); Assert.False(actual.IsCompleted);
            Assert.Empty(rig.Rows.ChildCreated);
            Assert.Same(actual, rig.Tasks.RegisterOriginalDelegationIntentAsync(parent, "fixed-once", new string('a', 64), "Child", [], TestContext.Current.CancellationToken));
        }
        finally { release.TrySetResult(); }
        var intent = await actual; rig.Rows.BeforeWrite = null;
        Assert.NotEqual(parent.Snapshot.TaskId, intent.Intent.ChildTaskId);
        var creation = await rig.Tasks.CreateOriginalDelegatedChildAsync(intent, TestContext.Current.CancellationToken);
        Assert.Same(creation, await rig.Tasks.CreateOriginalDelegatedChildAsync(intent, TestContext.Current.CancellationToken));
        Assert.Equal(intent.Intent.ChildTaskId, creation.Child.TaskId); Assert.Single(rig.Rows.ChildCreated);
        Assert.Equal(TaskRunDelegationState.IntentAcknowledged, Assert.Single((await rig.Tasks.GetAsync(parent.Snapshot.TaskId, TestContext.Current.CancellationToken))!.Delegations).State);
        async Task HoldAsync(TaskExecutionSnapshot next)
        { entered.TrySetResult(); await release.Task; rig.Rows.Commit(next); }
    }

    [Fact]
    public async Task Delegation_copied_parent_admission_refuses_intent_and_child_effects()
    {
        await using var rig = new ChildRig(); var parent = await rig.ParentAsync();
        var actual = rig.Tasks.RegisterOriginalDelegationIntentAsync(parent with { }, "foreign", new string('a', 64), "Child", [], TestContext.Current.CancellationToken);
        Assert.NotNull(await Record.ExceptionAsync(() => actual));
        Assert.True(actual.IsFaulted); Assert.Empty(rig.Rows.ChildCreated);
        Assert.Empty((await rig.Tasks.GetAsync(parent.Snapshot.TaskId, TestContext.Current.CancellationToken))!.Delegations);
    }

    [Fact]
    public async Task Delegation_unknown_child_cas_keeps_same_original_task_and_preserves_full_raw_faults_without_parent_link()
    {
        await using var rig = new ChildRig(); var parent = await rig.ParentAsync();
        var intent = await rig.Tasks.RegisterOriginalDelegationIntentAsync(parent, "uncertain", new string('b', 64), "Child", [], TestContext.Current.CancellationToken);
        var first = new OperationCanceledException("FAULTED child write, not caller cancellation");
        var second = new IOException("Independent actual child write sibling");
        var raw = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); raw.SetException([first, second]);
        rig.Rows.BeforeWrite = next => next.ParentDelegation is null ? null : raw.Task;
        var actual = rig.Tasks.CreateOriginalDelegatedChildAsync(intent, TestContext.Current.CancellationToken);
        Assert.NotNull(await Record.ExceptionAsync(() => actual)); Assert.True(actual.IsFaulted); Assert.False(actual.IsCanceled);
        Assert.Contains(Leaves(actual.Exception!), cause => ReferenceEquals(cause, first));
        Assert.Contains(Leaves(actual.Exception!), cause => ReferenceEquals(cause, second));
        Assert.Same(actual, rig.Tasks.CreateOriginalDelegatedChildAsync(intent, TestContext.Current.CancellationToken));
        Assert.Empty(rig.Rows.ChildCreated); Assert.Equal(0, rig.Client.Dispatches);
        Assert.Equal(TaskRunDelegationState.IntentAcknowledged, Assert.Single((await rig.Tasks.GetAsync(parent.Snapshot.TaskId, TestContext.Current.CancellationToken))!.Delegations).State);
    }

    [Fact]
    public async Task Delegation_parent_link_cas_loss_reconciles_same_child_without_second_creation_or_dispatch()
    {
        await using var rig = new ChildRig(); var parent = await rig.ParentAsync();
        var intent = await rig.Tasks.RegisterOriginalDelegationIntentAsync(parent, "link-retry", new string('c', 64), "Child", [], TestContext.Current.CancellationToken);
        var creation = await rig.Tasks.CreateOriginalDelegatedChildAsync(intent, TestContext.Current.CancellationToken);
        var cause = new TaskExecutionRevisionConflictException(parent.Snapshot.TaskId, 1, 2);
        rig.Rows.BeforeWrite = next => next.ParentDelegation is null && next.Delegations.Any(value => value.State == TaskRunDelegationState.ChildLinked)
            ? Task.FromException(cause) : null;
        var failed = rig.Tasks.LinkOriginalDelegatedChildAsync(creation, TestContext.Current.CancellationToken);
        Assert.NotNull(await Record.ExceptionAsync(() => failed)); Assert.True(failed.IsFaulted);
        Assert.Contains(Leaves(failed.Exception!), value => ReferenceEquals(value, cause));
        rig.Rows.BeforeWrite = null;
        var linked = await rig.Tasks.LinkOriginalDelegatedChildAsync(creation, TestContext.Current.CancellationToken);
        Assert.Equal(creation.Child.TaskId, linked.Child.TaskId); Assert.Equal(creation.Child.ExecutionId, linked.Child.ExecutionId);
        Assert.Equal(TaskRunDelegationState.ChildLinked, Assert.Single(linked.Parent.Delegations).State);
        Assert.Single(rig.Rows.ChildCreated); Assert.Equal(0, rig.Client.Dispatches);
    }

    [Fact]
    public async Task Delegation_task_owned_intent_rebinds_after_real_parent_settlement_and_preserves_queue_and_child_ids()
    {
        await using var rig = new ChildRig(); var parent = await rig.ParentAsync();
        var intent = await rig.Tasks.RegisterOriginalDelegationIntentAsync(parent, "after-fallback", new string('d', 64), "Child", [], TestContext.Current.CancellationToken);
        await rig.Tasks.SubmitFollowUpAsync(parent.Snapshot.TaskId, "After children, inspect results", TaskFollowUpMode.Queue, null, null, TestContext.Current.CancellationToken);
        var before = (await rig.Tasks.GetAsync(parent.Snapshot.TaskId, TestContext.Current.CancellationToken))!;
        var failed = await rig.Tasks.RecordAttemptFailureAsync(before.TaskId, before.ExecutionId, parent.AttemptId,
            new("synthetic-provider-unavailable", "No original provider was invoked", "Known zero-frame parent attempt"), TestContext.Current.CancellationToken);
        var candidate = await rig.Authority.CaptureSelectedRouteAsync(failed, rig.Provider.Model, [ToolCapability.Text], [], TestContext.Current.CancellationToken);
        var fresh = await rig.Tasks.ResumeAttemptAsync(failed.TaskId, failed.ExecutionId, parent.AttemptId, candidate, TestContext.Current.CancellationToken);
        Assert.NotEqual(parent.AttemptId, fresh.AttemptId);
        Assert.NotNull(await Record.ExceptionAsync(() => parent.Lease.RevalidateAsync(TestContext.Current.CancellationToken).AsTask()));
        var creation = await rig.Tasks.CreateOriginalDelegatedChildAsync(intent, TestContext.Current.CancellationToken);
        var linked = await rig.Tasks.LinkOriginalDelegatedChildAsync(creation, TestContext.Current.CancellationToken);
        Assert.Equal(before.TaskId, linked.Parent.TaskId); Assert.Equal(before.ExecutionId, linked.Parent.ExecutionId);
        Assert.Equal(intent.Intent.ChildTaskId, linked.Child.TaskId); Assert.Equal(intent.Intent.ChildExecutionId, linked.Child.ExecutionId);
        Assert.Equal(parent.AttemptId, Assert.Single(linked.Parent.Delegations).CreatedByAttemptId);
        Assert.Equal(JsonSerializer.Serialize(before.Queue), JsonSerializer.Serialize(linked.Parent.Queue));
        Assert.Single(rig.Rows.ChildCreated); Assert.Equal(0, rig.Client.Dispatches);
    }

    [Fact]
    public async Task Delegation_unfinished_actual_child_link_refuses_parent_completion()
    {
        await using var rig = new ChildRig(); var parent = await rig.ParentAsync();
        var intent = await rig.Tasks.RegisterOriginalDelegationIntentAsync(parent, "unfinished", new string('e', 64), "Child", [], TestContext.Current.CancellationToken);
        var creation = await rig.Tasks.CreateOriginalDelegatedChildAsync(intent, TestContext.Current.CancellationToken);
        await rig.Tasks.LinkOriginalDelegatedChildAsync(creation, TestContext.Current.CancellationToken);
        Assert.NotNull(await Record.ExceptionAsync(() => rig.Tasks.CompleteAttemptAsync(parent.Snapshot.TaskId,
            parent.Snapshot.ExecutionId, parent.AttemptId, TestContext.Current.CancellationToken)));
        Assert.Equal(TaskExecutionLifecycle.Running, (await rig.Tasks.GetAsync(parent.Snapshot.TaskId, TestContext.Current.CancellationToken))!.State);
        Assert.Equal(0, rig.Client.Dispatches); Assert.Single(rig.Rows.ChildCreated);
    }

    [Fact]
    public async Task Delegation_actual_saved_agent_runs_same_fixed_child_then_parent_accepts_only_whole_child_completion()
    {
        await using var rig = new ChildRig(); var parent = await rig.ParentAsync();
        var run = await rig.Agents.RunDelegatedAsync(parent, rig.Definition.Id, "actual-child", "Actual fixed child input", [], TestContext.Current.CancellationToken);
        Assert.Equal(AgentRunStatus.Completed, run.Status); Assert.Equal(100, run.ProgressPercent);
        var binding = Assert.IsType<AgentRunCanonicalBinding>(run.CanonicalTask);
        var currentParent = (await rig.Tasks.GetAsync(parent.Snapshot.TaskId, TestContext.Current.CancellationToken))!;
        var intent = Assert.Single(currentParent.Delegations);
        Assert.Equal(TaskRunDelegationState.ChildCompleted, intent.State);
        Assert.Equal(intent.ChildTaskId, binding.TaskId); Assert.Equal(intent.ChildContextId, binding.ContextId);
        Assert.Equal(intent.ChildExecutionId, binding.ExecutionId); Assert.Equal(TaskExecutionLifecycle.Completed, binding.State);
        Assert.Equal(binding.TaskId, run.Id); Assert.Equal(1, rig.Client.Dispatches); Assert.Single(rig.Rows.ChildCreated);
        Assert.Single(rig.AgentRows.Values); Assert.NotNull(await rig.Agents.GetRecordedInvocationEvidenceAsync(run.Id, TestContext.Current.CancellationToken));
        var same = await rig.Agents.RunDelegatedAsync(parent, rig.Definition.Id, "actual-child", "Actual fixed child input", [], TestContext.Current.CancellationToken);
        Assert.Equal(run, same); Assert.Equal(1, rig.Client.Dispatches); Assert.Single(rig.Rows.ChildCreated);
        var complete = await rig.Tasks.CompleteAttemptAsync(currentParent.TaskId, currentParent.ExecutionId, parent.AttemptId, TestContext.Current.CancellationToken);
        Assert.Equal(TaskExecutionLifecycle.Completed, complete.State);
    }

    [Fact]
    public async Task Delegation_real_child_cleanup_fault_keeps_child_recovery_and_unfinished_parent_without_completion_receipt()
    {
        await using var rig = new ChildRig(); var parent = await rig.ParentAsync();
        var cause = new IOException("Exact actual finite child stream Dispose failure");
        rig.Client.OriginalDispose = Task.FromException(cause);
        var run = await rig.Agents.RunDelegatedAsync(parent, rig.Definition.Id, "cleanup-failed", "Child input", [], TestContext.Current.CancellationToken);
        Assert.Equal(AgentRunStatus.Suspended, run.Status); Assert.Null(run.CompletedAt);
        Assert.Contains(cause.Message, run.Error, StringComparison.Ordinal);
        var child = (await rig.Tasks.GetAsync(run.CanonicalTask!.TaskId, TestContext.Current.CancellationToken))!;
        Assert.Equal(TaskExecutionLifecycle.Suspended, child.State); Assert.NotNull(child.RecoveryObservation);
        Assert.Equal(TaskRunDelegationState.ChildLinked, Assert.Single((await rig.Tasks.GetAsync(parent.Snapshot.TaskId, TestContext.Current.CancellationToken))!.Delegations).State);
        Assert.Null(await rig.Agents.GetRecordedInvocationEvidenceAsync(run.Id, TestContext.Current.CancellationToken));
        Assert.NotNull(await Record.ExceptionAsync(() => rig.Tasks.CompleteAttemptAsync(parent.Snapshot.TaskId, parent.Snapshot.ExecutionId, parent.AttemptId, TestContext.Current.CancellationToken)));
        Assert.Equal(1, rig.Client.Dispatches); Assert.Single(rig.Rows.ChildCreated);
    }

    [Fact]
    public async Task Delegation_copied_parent_cannot_retrieve_already_acknowledged_intent_or_completed_child_result()
    {
        await using var rig = new ChildRig(); var parent = await rig.ParentAsync();
        var intent = await rig.Tasks.RegisterOriginalDelegationIntentAsync(parent, "private-retrieval", new string('f', 64), "Child", [], TestContext.Current.CancellationToken);
        Assert.Throws<InvalidOperationException>(() =>
        { _ = rig.Tasks.RegisterOriginalDelegationIntentAsync(parent with { }, "private-retrieval", new string('f', 64), "Child", [], TestContext.Current.CancellationToken); });
        Assert.Same(intent, await rig.Tasks.RegisterOriginalDelegationIntentAsync(parent, "private-retrieval", new string('f', 64), "Child", [], TestContext.Current.CancellationToken));
        var run = await rig.Agents.RunDelegatedAsync(parent, rig.Definition.Id, "private-child", "Child input", [], TestContext.Current.CancellationToken);
        Assert.Equal(AgentRunStatus.Completed, run.Status); Assert.Equal(1, rig.Client.Dispatches);
        Assert.Throws<InvalidOperationException>(() =>
        { _ = rig.Agents.RunDelegatedAsync(parent with { }, rig.Definition.Id, "private-child", "Child input", [], TestContext.Current.CancellationToken); });
        Assert.Equal(run, await rig.Agents.RunDelegatedAsync(parent, rig.Definition.Id, "private-child", "Child input", [], TestContext.Current.CancellationToken));
        Assert.Equal(1, rig.Client.Dispatches); Assert.Single(rig.Rows.ChildCreated);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Delegation_actual_raw_parent_read_preserves_compound_fault_and_distinct_genuine_cancellation(bool canceled)
    {
        await using var rig = new ChildRig(); var parent = await rig.ParentAsync();
        var first = new OperationCanceledException("Actual faulted read, not token withdrawal");
        var second = new IOException("Exact raw parent-read sibling");
        using var withdrawal = new CancellationTokenSource(); withdrawal.Cancel();
        var fault = new TaskCompletionSource<TaskExecutionSnapshot?>(TaskCreationOptions.RunContinuationsAsynchronously);
        fault.SetException([first, second]);
        var raw = canceled ? Task.FromCanceled<TaskExecutionSnapshot?>(withdrawal.Token) : fault.Task;
        rig.Rows.ReadOriginal = (_, _) => raw;
        var actual = rig.Tasks.RegisterOriginalDelegationIntentAsync(parent, "read-custody", new string('1', 64), "Child", [], TestContext.Current.CancellationToken);
        try { Assert.NotNull(await Record.ExceptionAsync(() => actual)); }
        finally { rig.Rows.ReadOriginal = null; }
        if (canceled)
        { Assert.True(raw.IsCanceled); Assert.True(actual.IsCanceled); Assert.False(actual.IsFaulted); }
        else
        {
            Assert.True(raw.IsFaulted); Assert.True(actual.IsFaulted); Assert.False(actual.IsCanceled);
            Assert.Contains(Leaves(actual.Exception!), value => ReferenceEquals(value, first));
            Assert.Contains(Leaves(actual.Exception!), value => ReferenceEquals(value, second));
        }
        Assert.Empty((await rig.Tasks.GetAsync(parent.Snapshot.TaskId, TestContext.Current.CancellationToken))!.Delegations);
        Assert.Empty(rig.Rows.ChildCreated); Assert.Equal(0, rig.Client.Dispatches);
    }

    [Fact]
    public async Task Delegation_remote_permission_approval_continues_same_actual_child_without_second_creation_begin_or_user_input()
    {
        await using var rig = new ChildRig(remoteAsk: true); var parent = await rig.ParentAsync();
        var paused = await rig.Agents.RunDelegatedAsync(parent, rig.Definition.Id, "remote-child", "Same private child input", [], TestContext.Current.CancellationToken);
        Assert.Equal(AgentRunStatus.Suspended, paused.Status); Assert.Null(paused.CompletedAt);
        var initial = (await rig.Tasks.GetAsync(paused.CanonicalTask!.TaskId, TestContext.Current.CancellationToken))!;
        Assert.Empty(initial.Attempts); Assert.NotNull(initial.ParentDelegation);
        Assert.Equal(TaskRunOriginalSettlementOutcome.NoAttemptAdmissionWasInvoked, initial.RecoveryObservation!.SettlementOutcome);
        Assert.Equal(TaskRunDelegationState.ChildLinked, Assert.Single((await rig.Tasks.GetAsync(parent.Snapshot.TaskId, TestContext.Current.CancellationToken))!.Delegations).State);
        Assert.False(rig.Agents.HasOriginalUnstartedRetrySource(paused.Id));
        var user = await ReadActualChildUserAsync(rig, initial);
        var waiting = Assert.Single(rig.PermissionRows.Rows.Values);
        Assert.Equal(0, rig.Client.Dispatches); Assert.Single(rig.Rows.ChildCreated);
        await rig.PermissionOwner!.ApproveOriginalAsync(waiting.Id, TestContext.Current.CancellationToken);
        Assert.True(rig.Agents.HasOriginalUnstartedRetrySource(paused.Id)); Assert.Equal(0, rig.Client.Dispatches);
        var complete = await rig.Agents.RetryAsync(paused.Id, TestContext.Current.CancellationToken);
        Assert.Equal(paused.Id, complete.Id); Assert.Equal(AgentRunStatus.Completed, complete.Status);
        var child = (await rig.Tasks.GetAsync(initial.TaskId, TestContext.Current.CancellationToken))!;
        Assert.Equal(initial.TaskId, child.TaskId); Assert.Equal(initial.ContextId, child.ContextId); Assert.Equal(initial.ExecutionId, child.ExecutionId);
        Assert.Equal(initial.ParentDelegation, child.ParentDelegation); Assert.Equal(TaskExecutionLifecycle.Completed, child.State);
        Assert.Null(child.RecoveryObservation); Assert.Equal(JsonSerializer.Serialize(initial.RecoveryObservation), JsonSerializer.Serialize(Assert.Single(child.RecoveryHistory)));
        Assert.Single(child.Attempts); Assert.Single(rig.Rows.ChildCreated); Assert.Equal(1, rig.Client.Dispatches);
        Assert.Collection(rig.RemoteCapture!.ActualUserMessages, first => Assert.Single(first), next => Assert.Single(next));
        Assert.Equal(rig.RemoteCapture.ActualUserMessages[0], rig.RemoteCapture.ActualUserMessages[1]);
        var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        var records = typeof(AgentTaskRuntimeService).GetField("_recordedObservations", flags)!.GetValue(rig.Agents)!;
        var values = (System.Collections.IEnumerable)records.GetType().GetProperty("Values")!.GetValue(records)!;
        var record = Assert.Single(values.Cast<object>());
        var original = record.GetType().GetField("Item3")!.GetValue(record)!;
        var actualChat = original.GetType().GetField("Chat", flags)!.GetValue(original)!;
        var actualCustody = actualChat.GetType().GetField("Original", flags)!.GetValue(actualChat)!;
        Assert.Same(user, actualCustody.GetType().GetField("OriginalUserMessage", flags)!.GetValue(actualCustody));
        var acknowledgedParent = (await rig.Tasks.GetAsync(parent.Snapshot.TaskId, TestContext.Current.CancellationToken))!;
        Assert.Equal(TaskRunDelegationState.ChildCompleted, Assert.Single(acknowledgedParent.Delegations).State);
        Assert.NotNull(await rig.Agents.GetRecordedInvocationEvidenceAsync(complete.Id, TestContext.Current.CancellationToken));
        Assert.Equal(TaskExecutionLifecycle.Completed, (await rig.Tasks.CompleteAttemptAsync(parent.Snapshot.TaskId,
            parent.Snapshot.ExecutionId, parent.AttemptId, TestContext.Current.CancellationToken)).State);
    }

    [Fact]
    public async Task Delegation_public_task_ids_cannot_replace_private_saved_child_retry_after_real_approval()
    {
        await using var rig = new ChildRig(remoteAsk: true); var parent = await rig.ParentAsync();
        var paused = await rig.Agents.RunDelegatedAsync(parent, rig.Definition.Id, "public-child", "Same child", [], TestContext.Current.CancellationToken);
        var before = (await rig.Tasks.GetAsync(paused.CanonicalTask!.TaskId, TestContext.Current.CancellationToken))!;
        await rig.PermissionOwner!.ApproveOriginalAsync(Assert.Single(rig.PermissionRows.Rows.Values).Id, TestContext.Current.CancellationToken);
        var refused = await Record.ExceptionAsync(async () =>
        { await foreach (var _ in rig.Chat.ContinueUnstartedOriginalAsync(before.TaskId, before.ExecutionId, TestContext.Current.CancellationToken)) { } });
        Assert.IsType<InvalidOperationException>(refused); Assert.Equal(0, rig.Client.Dispatches); Assert.Single(rig.Rows.ChildCreated);
        Assert.Equal(JsonSerializer.Serialize(before), JsonSerializer.Serialize(await rig.Tasks.GetAsync(before.TaskId, TestContext.Current.CancellationToken)));
        Assert.Equal(TaskRunDelegationState.ChildLinked, Assert.Single((await rig.Tasks.GetAsync(parent.Snapshot.TaskId, TestContext.Current.CancellationToken))!.Delegations).State);
    }

    [Fact]
    public async Task Delegation_unknown_original_child_cleanup_refuses_same_run_remote_continuation_even_after_real_approval()
    {
        await using var rig = new ChildRig(remoteAsk: true); var parent = await rig.ParentAsync();
        // Ask is raised in capture before any provider call. The actual tracker close is
        // separately faulted after that source Ask; no empty durable history can waive it.
        var originalFailure = new IOException("Exact child tracker cleanup failure");
        rig.Chat.ExecutionChanged += value => { if (value.Stage == ChatExecutionStage.Cancelled) throw originalFailure; };
        var paused = await rig.Agents.RunDelegatedAsync(parent, rig.Definition.Id, "unknown-child", "Same child", [], TestContext.Current.CancellationToken);
        Assert.Equal(AgentRunStatus.Suspended, paused.Status); Assert.Contains(originalFailure.Message, paused.Error, StringComparison.Ordinal);
        var before = (await rig.Tasks.GetAsync(paused.CanonicalTask!.TaskId, TestContext.Current.CancellationToken))!;
        Assert.Empty(before.Attempts); Assert.NotNull(before.RecoveryObservation);
        var waiting = Assert.Single(rig.PermissionRows.Rows.Values);
        await rig.PermissionOwner!.ApproveOriginalAsync(waiting.Id, TestContext.Current.CancellationToken);
        Assert.False(rig.Agents.HasOriginalUnstartedRetrySource(paused.Id));
        Assert.NotNull(await Record.ExceptionAsync(() => rig.Agents.RetryAsync(paused.Id, TestContext.Current.CancellationToken)));
        Assert.Equal(JsonSerializer.Serialize(before.RecoveryObservation), JsonSerializer.Serialize((await rig.Tasks.GetAsync(before.TaskId, TestContext.Current.CancellationToken))!.RecoveryObservation));
        Assert.Empty((await rig.Tasks.GetAsync(before.TaskId, TestContext.Current.CancellationToken))!.RecoveryHistory);
        Assert.Equal(0, rig.Client.Dispatches); Assert.Single(rig.Rows.ChildCreated);
        Assert.Equal(TaskRunDelegationState.ChildLinked, Assert.Single((await rig.Tasks.GetAsync(parent.Snapshot.TaskId, TestContext.Current.CancellationToken))!.Delegations).State);
    }

    private static async Task<ChatMessage> ReadActualChildUserAsync(ChildRig rig, TaskExecutionSnapshot snapshot)
    {
        var inspection = await rig.Tasks.InspectOriginalRecoveryAsync(snapshot.TaskId, snapshot.ExecutionId, default);
        var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        var actual = inspection.GetType().GetField("OriginalCustody", flags)!.GetValue(inspection)!;
        return Assert.IsType<ChatMessage>(actual.GetType().GetField("OriginalUserMessage", flags)!.GetValue(actual));
    }

    private sealed class ChildRig : IAsyncDisposable
    {
        internal readonly ChildActors Actors = new(); internal readonly ChildProvider Provider;
        internal readonly ChildRows Rows = new(); internal readonly AgentRows AgentRows = new();
        internal readonly TaskRunPermissionAuthority Authority; internal readonly TaskRunOriginalFrameOwner Runtime;
        internal readonly TaskExecutionCoordinator Tasks; internal readonly ChildClient Client;
        internal readonly AgentDefinition Definition; internal readonly AgentTaskRuntimeService Agents;
        internal readonly ChatSessionService Chat;
        internal readonly RemediationRows PermissionRows = new(); internal readonly Events PermissionEvents = new();
        internal TaskRunCloudPermissionRemediationOwner? PermissionOwner;
        internal RemediationCoordinator? PermissionCoordinator;
        internal RemoteChildCapture? RemoteCapture;
        internal ChildRig(bool remoteAsk = false)
        {
            Provider = new(remoteAsk);
            TaskExecutionCoordinator? actual = null;
            Authority = new(Actors, new ChildRegistry(Provider), new ChildConfigurations(Provider), new ChildPrivacy(remoteAsk),
                new(new ChildPermissions()), cloud: remoteAsk ? new ControlledCloudAdmission() : null, originalTasks: () => actual ?? throw new InvalidOperationException());
            Runtime = new((task, run, attempt, token) => actual!.TryGetIssuedAttemptAsync(task, run, attempt, token));
            Tasks = actual = new(Rows, new Sink(), admissionAuthority: Authority, runtimeSettlement: Runtime);
            TaskRunCentralCloudUsePermissionSource? remoteSource = null;
            if (remoteAsk)
            {
                remoteSource = new(Actors, new PermissionDecisionEngine());
                var continuations = new RemediationContinuationRegistry();
                PermissionCoordinator = new(PermissionRows, new Secrets(), PermissionEvents, continuations);
                PermissionOwner = new(remoteSource, Authority, () => Tasks, PermissionCoordinator, PermissionRows, continuations, PermissionEvents);
                RemoteCapture = new(Authority, remoteSource, Provider);
            }
            Client = new(Tasks, Authority, Runtime, Provider, remoteSource);
            var chat = Chat = new ChatSessionService(new Conversations(), Client, new CapabilityPreflightService(), new Safety(),
                new WorkspaceToolRuntime(new Workspace()), new ComputerToolRuntime(new Computer()), taskCoordinator: Tasks,
                taskToolOwner: new ToolsOwner(), taskProviderContextCapture: (ITaskRunProviderContextCapture?)RemoteCapture ?? new ChildCapture(), taskCloudPermissionRemediation: PermissionOwner);
            Definition = new(Guid.NewGuid(), "Actual synthetic child Agent", "No account/model acceptance", "Inspect same child",
                "agent", Provider.Model.Model.Name, null, "[]", "{}", false, true, DateTimeOffset.UnixEpoch);
            Agents = new(new AgentCatalog(Definition), AgentRows, Client, new CapabilityRegistryService(new EmptyAgentCapabilities()),
                chat, new PermissionDecisionEngine());
        }
        internal async Task<TaskRunAttemptAdmission> ParentAsync()
        {
            var parent = await Tasks.BeginAuthorizedAsync(Guid.NewGuid(), Guid.NewGuid(), "Actual synthetic parent",
                TaskExecutionDurability.PersistedPlan, [], default);
            var candidate = await Authority.CaptureSelectedRouteAsync(parent, Provider.Model, [ToolCapability.Text], [], default);
            return await Tasks.StartAttemptAsync(parent.TaskId, parent.ExecutionId, candidate, default);
        }
        public async ValueTask DisposeAsync()
        {
            var failures = new List<Exception>();
            if (PermissionOwner is not null) try { await PermissionOwner.CloseAndDrainAsync(); } catch (Exception cause) { failures.Add(cause); }
            try { await Runtime.CloseAndDrainAsync(); } catch (Exception cause) { failures.Add(cause); }
            try { PermissionCoordinator?.Dispose(); } catch (Exception cause) { failures.Add(cause); }
            if (failures.Count > 0) throw new AggregateException("Actual child fixture owner close failed.", failures);
        }
    }

    private sealed class ChildRows : ITaskExecutionRepository
    {
        private readonly Dictionary<Guid, string> _rows = []; internal readonly List<Guid> ChildCreated = [];
        internal Func<TaskExecutionSnapshot, Task?>? BeforeWrite = null;
        internal Func<Guid, TaskExecutionSnapshot?, Task<TaskExecutionSnapshot?>>? ReadOriginal = null;
        public Task UpsertAsync(TaskExecutionSnapshot next, CancellationToken token)
        { token.ThrowIfCancellationRequested(); if (BeforeWrite?.Invoke(next) is { } actual) return actual; Commit(next); return Task.CompletedTask; }
        internal void Commit(TaskExecutionSnapshot next)
        {
            var before = _rows.TryGetValue(next.TaskId, out var raw) ? JsonSerializer.Deserialize<TaskExecutionSnapshot>(raw) : null;
            if (next.PersistenceRevision != (before?.PersistenceRevision ?? 0) + 1)
                throw new TaskExecutionRevisionConflictException(next.TaskId, next.PersistenceRevision - 1, before?.PersistenceRevision ?? 0);
            _rows[next.TaskId] = JsonSerializer.Serialize(next); if (before is null && next.ParentDelegation is not null) ChildCreated.Add(next.TaskId);
        }
        public Task<TaskExecutionSnapshot?> GetAsync(Guid id, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            var current = _rows.TryGetValue(id, out var raw) ? JsonSerializer.Deserialize<TaskExecutionSnapshot>(raw) : null;
            return ReadOriginal is { } original ? original(id, current) : Task.FromResult(current);
        }
        public async Task<TaskExecutionSnapshot?> GetByContextAsync(Guid id, CancellationToken token) =>
            (await GetResumableAsync(token)).FirstOrDefault(value => value.ContextId == id);
        public Task<IReadOnlyList<TaskExecutionSnapshot>> GetResumableAsync(CancellationToken token) =>
            Task.FromResult<IReadOnlyList<TaskExecutionSnapshot>>(_rows.Values.Select(raw => JsonSerializer.Deserialize<TaskExecutionSnapshot>(raw)!).ToArray());
    }

    private sealed class ChildClient(TaskExecutionCoordinator tasks, TaskRunPermissionAuthority authority,
        TaskRunOriginalFrameOwner runtime, ChildProvider provider, TaskRunCentralCloudUsePermissionSource? remoteSource = null) : IOllamaClient
    {
        private readonly TaskExecutionCoordinator _tasks = tasks;
        private readonly TaskRunPermissionAuthority _authority = authority;
        private readonly TaskRunOriginalFrameOwner _runtime = runtime;
        private readonly ChildProvider _provider = provider;
        private readonly TaskRunCentralCloudUsePermissionSource? _remoteSource = remoteSource;
        internal int Dispatches; internal Task? OriginalDispose = null;
        public Task<bool> IsAvailableAsync(CancellationToken token) => Task.FromResult(true);
        public Task<IReadOnlyList<ModelDescriptor>> GetModelsAsync(CancellationToken token) => Task.FromResult<IReadOnlyList<ModelDescriptor>>([_provider.Model.Model]);
        public Task<string> CompleteAsync(OllamaChatRequest request, CancellationToken token) => throw new NotSupportedException();
        public Task<OllamaToolResponse> ChatWithToolsAsync(OllamaToolRequest request, CancellationToken token) => throw new NotSupportedException();
        public IAsyncEnumerable<string> StreamChatAsync(OllamaChatRequest request, CancellationToken token) => new OriginalStream(this, request, token);
        private sealed class OriginalStream(ChildClient owner, OllamaChatRequest request, CancellationToken token) : IAsyncEnumerable<string>, IAsyncEnumerator<string>
        {
            private bool _moved; public string Current => string.Empty;
            public IAsyncEnumerator<string> GetAsyncEnumerator(CancellationToken ignored = default) => this;
            public ValueTask<bool> MoveNextAsync() => new(MoveOriginalAsync());
            private async Task<bool> MoveOriginalAsync()
            {
                if (_moved) return false; _moved = true;
                var observation = request.ExecutionContext ?? throw new InvalidOperationException("Actual child context absent");
                var current = await owner._tasks.GetAsync(observation.TaskId, token) ?? throw new InvalidOperationException();
                var candidate = await owner._authority.CaptureSelectedRouteAsync(current, owner._provider.Model, [ToolCapability.Text], [], token);
                await using var remote = owner._remoteSource is null ? null
                    : await owner._remoteSource.AcquireOriginalAsync(current.OwnerBinding!, candidate, token);
                var actual = await owner._tasks.StartAttemptAsync(current.TaskId, current.ExecutionId, candidate, token);
                var originalRegistration = owner._runtime.RegisterOriginalAttemptAsync(actual, token);
                await originalRegistration.ConfigureAwait(false); // SAME retained registration before Running ACK or provider body.
                var originalRunning = owner._tasks.MarkAttemptRunningAsync(current.TaskId, current.ExecutionId, actual.AttemptId, token);
                await originalRunning.ConfigureAwait(false); // Real routing phase, never a fabricated Running record.
                Task<bool> StartOriginalProvider() => owner._runtime.StartOriginalFrameAsync(actual,
                    _ => { owner.Dispatches++; return Task.FromResult(false); }, token);
                return await (remote is null ? StartOriginalProvider() : remote.RunOriginalInvocation(StartOriginalProvider));
            }
            public ValueTask DisposeAsync() => owner.OriginalDispose is { } original ? new(original) : ValueTask.CompletedTask;
        }
    }
    private sealed class RemoteChildCapture(TaskRunPermissionAuthority authority,
        TaskRunCentralCloudUsePermissionSource source, ChildProvider provider) : ITaskRunProviderContextCapture
    {
        internal readonly List<string[]> ActualUserMessages = [];
        private async Task CaptureAsync(TaskExecutionSnapshot current, CancellationToken token)
        {
            var candidate = await authority.CaptureSelectedRouteAsync(current, provider.Model, [ToolCapability.Text], [], token);
            await using var gate = await source.AcquireOriginalAsync(current.OwnerBinding!, candidate, token);
        }
        public ValueTask CaptureOriginalAsync(TaskExecutionSnapshot current, OllamaChatRequest request, TaskRunContextInventory inventory, CancellationToken token)
        {
            ActualUserMessages.Add(request.Messages.Where(value => value.Role == "user").Select(value => value.Content).ToArray());
            return new(CaptureAsync(current, token));
        }
        public ValueTask CaptureOriginalAsync(TaskExecutionSnapshot current, OllamaToolRequest request, TaskRunContextInventory inventory, CancellationToken token) =>
            new(CaptureAsync(current, token));
    }

    private sealed class ChildCapture : ITaskRunProviderContextCapture
    {
        public ValueTask CaptureOriginalAsync(TaskExecutionSnapshot current, OllamaChatRequest actual, TaskRunContextInventory inventory, CancellationToken token)
        { token.ThrowIfCancellationRequested(); return ValueTask.CompletedTask; }
        public ValueTask CaptureOriginalAsync(TaskExecutionSnapshot current, OllamaToolRequest actual, TaskRunContextInventory inventory, CancellationToken token)
        { token.ThrowIfCancellationRequested(); return ValueTask.CompletedTask; }
    }
    private sealed class ChildActors : IAuthenticatedResourceActorSource
    {
        public ValueTask<AuthenticatedResourceActor?> GetCurrentAsync(CancellationToken token)
        { token.ThrowIfCancellationRequested(); return ValueTask.FromResult<AuthenticatedResourceActor?>(new("synthetic-child-owner", "synthetic-child-profile", null, null, "synthetic-live-authority")); }
    }
    private sealed class ChildProvider(bool remote = false) : IModelProvider
    {
        public string Id => remote ? "synthetic-child-remote" : "ollama"; public string DisplayName => "Synthetic child"; public bool IsLocal => !remote;
        public bool CanManageModels => false; public ModelProviderKind Kind => remote ? ModelProviderKind.OpenAI : ModelProviderKind.Ollama;
        internal ProviderModelDescriptor Model { get; } = new(remote ? "synthetic-child-remote" : "ollama", !remote, new("synthetic-child", 1, "synthetic", "7B", "Q8", new HashSet<ToolCapability> { ToolCapability.Text }, DateTimeOffset.UnixEpoch));
        public Task<IReadOnlyList<ProviderModelDescriptor>> GetModelsAsync(CancellationToken token) => Task.FromResult<IReadOnlyList<ProviderModelDescriptor>>([Model]);
        public Task<ProviderHealthStatus> CheckHealthAsync(CancellationToken token) => Task.FromResult(new ProviderHealthStatus(Id, true, "Synthetic", TimeSpan.Zero, DateTimeOffset.UnixEpoch));
        public IAsyncEnumerable<string> StreamChatAsync(OllamaChatRequest request, CancellationToken token) => throw new NotSupportedException();
        public Task<string> CompleteAsync(OllamaChatRequest request, CancellationToken token) => throw new NotSupportedException();
        public Task<OllamaToolResponse> ChatWithToolsAsync(OllamaToolRequest request, CancellationToken token) => throw new NotSupportedException();
    }
    private sealed class ChildRegistry(IModelProvider actual) : IModelProviderRegistry
    {
        public IReadOnlyList<IModelProvider> Providers => [actual]; public IModelProvider? Find(string id) => id == actual.Id ? actual : null;
        public IModelProvider GetRequired(string id) => Find(id) ?? throw new InvalidOperationException();
        public Task<IReadOnlyList<ProviderModelDescriptor>> GetModelsAsync(CancellationToken token) => actual.GetModelsAsync(token);
    }
    private sealed class ChildConfigurations(ChildProvider provider) : IProviderConfigurationStore
    {
        private ProviderConfiguration Current => new(provider.Id, provider.Kind, provider.DisplayName, provider.IsLocal ? "http://127.0.0.1:11434" : "https://synthetic.invalid", true, provider.IsLocal, false, new Dictionary<string, string>(), DateTimeOffset.UnixEpoch);
        public Task<ProviderConfiguration?> GetAsync(string id, CancellationToken token) => Task.FromResult<ProviderConfiguration?>(id == provider.Id ? Current : null);
        public Task<IReadOnlyList<ProviderConfiguration>> GetAllAsync(CancellationToken token) => Task.FromResult<IReadOnlyList<ProviderConfiguration>>([Current]);
        public Task UpsertAsync(ProviderConfiguration value, CancellationToken token) => throw new NotSupportedException();
        public Task DeleteAsync(string id, CancellationToken token) => throw new NotSupportedException();
    }
    private sealed class ChildPrivacy(bool remote = false) : IPrivacyPreferenceStore
    { public PrivacyPreferences Current => PrivacyPreferences.Default with { LocalOnlyMode = !remote }; public Task UpdateAsync(PrivacyPreferences value, CancellationToken token) => throw new NotSupportedException(); }
    private sealed class ChildPermissions : IModelPermissionStore
    {
        public Task<ModelPermissionPolicy> GetPolicyAsync(CancellationToken token) => Task.FromResult(ModelPermissionPolicy.Empty);
        public Task SavePolicyAsync(ModelPermissionPolicy value, CancellationToken token) => throw new NotSupportedException();
    }
}
