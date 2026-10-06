using System.Reflection;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.Services;
using Haven.Desktop.Views.Pages.Chat;
using Xunit;

namespace Haven.Desktop.Tests;

// The complete Data initial-host and maintained caller test partials are actual
// owning inputs. This managed UI-child journey does not instantiate a native page.
public sealed partial class ChatCloudPermissionCallerTests
{
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
