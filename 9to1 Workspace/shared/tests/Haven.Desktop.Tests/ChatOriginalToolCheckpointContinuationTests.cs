using System.Collections;
using System.Reflection;
using System.Text.Json;
using Haven.Application;
using Haven.Core;
using Xunit;

namespace Haven.Desktop.Tests;

public sealed partial class ChatCloudPermissionCallerTests
{
    // These are actual controlled producer/refusal controls. The controlled client owns
    // no genuine router/native-dispatch/local-selection source and cannot authorize takeover.
    [Fact]
    public async Task Captured_failed_tool_step_without_original_router_witness_cannot_resume_or_clear_history()
    {
        var token = TestContext.Current.CancellationToken;
        await using var rig = new Rig(temporary: false);
        rig.Capture.AskDuringCapture = false;
        var oce = new OperationCanceledException("same faulted provider payload");
        var io = new IOException("same provider sibling");
        var call = new TaskCompletionSource<OllamaToolResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        rig.Client.OriginalToolFailure = call.Task;
        var owning = rig.RunAsync(true);
        try
        {
            await rig.Client.ToolRequestEntered.Task.WaitAsync(token);
            var before = rig.Service.CurrentCanonicalTask!;
            var boundary = ActualToolCheckpoint(rig, before.TaskId);
            await CheckpointField<TaskCompletionSource>(boundary, "ActualCallCaptured").Task.WaitAsync(token);
            call.SetException([oce, io]);
            var outward = await Record.ExceptionAsync(() => owning.WaitAsync(token));
            Assert.NotNull(outward);
            var suspended = (await rig.Tasks.GetAsync(before.TaskId, token))!;
            var availability = await rig.Tasks.GetOriginalRunControlAvailabilityAsync(before.TaskId, before.ExecutionId, token);
            Assert.False(availability.CanResumeUnstartedOriginal);
            Assert.False(availability.CanResumeOriginalToolCheckpoint);
            var user = Assert.Single(rig.Conversations.Messages, item => item.Role == MessageRole.User);
            var unavailable = await Record.ExceptionAsync(() => rig.Tasks.StartObservedOriginalRunResumeAsync(before.TaskId, before.ExecutionId, token));
            Assert.NotNull(unavailable);
            var after = (await rig.Tasks.GetAsync(before.TaskId, token))!;
            Assert.Equal(suspended.PersistenceRevision, after.PersistenceRevision);
            Assert.Equal(JsonSerializer.Serialize(suspended.RecoveryObservation), JsonSerializer.Serialize(after.RecoveryObservation));
            Assert.Empty(after.RecoveryHistory);
            Assert.Equal(before.TaskId, after.TaskId);
            Assert.Equal(before.ContextId, after.ContextId);
            Assert.Equal(before.ExecutionId, after.ExecutionId);
            Assert.Equal(user, Assert.Single(rig.Conversations.Messages, item => item.Role == MessageRole.User));
            Assert.Equal(1, rig.Tasks.LiveOriginalInvocationCount);
            Assert.Equal(0, rig.Workspace.Effects);
            Assert.True(call.Task.IsFaulted);
            Assert.Same(oce, call.Task.Exception!.InnerExceptions[0]);
            Assert.Same(io, call.Task.Exception.InnerExceptions[1]);
            Assert.DoesNotContain(rig.Stream, item => item.Kind == ChatStreamEventKind.AssistantCompleted);
        }
        finally { call.TrySetException(io); _ = await Record.ExceptionAsync(() => owning); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Actual_checkpoint_preparation_read_preserves_faulted_group_or_genuine_cancellation_before_cas(bool canceled)
    {
        var token = TestContext.Current.CancellationToken;
        await using var rig = new Rig();
        rig.Capture.AskDuringCapture = false;
        var provider = new TaskCompletionSource<OllamaToolResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        rig.Client.OriginalToolFailure = provider.Task;
        var owning = rig.RunAsync(true);
        var read = new TaskCompletionSource<TaskExecutionSnapshot?>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var rawCancellation = new CancellationTokenSource();
        Task? preparation = null;
        try
        {
            await rig.Client.ToolRequestEntered.Task.WaitAsync(token);
            var before = rig.Service.CurrentCanonicalTask!;
            var boundary = ActualToolCheckpoint(rig, before.TaskId);
            await CheckpointField<TaskCompletionSource>(boundary, "ActualCallCaptured").Task.WaitAsync(token);
            provider.SetException(new IOException("actual original provider failure"));
            Assert.NotNull(await Record.ExceptionAsync(() => owning.WaitAsync(token)));
            var original = boundary.GetType().GetField("Custody", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(boundary)!;
            rig.TaskRows.OverrideRead = read.Task;
            var method = typeof(TaskExecutionCoordinator).GetMethod("PrepareOriginalToolCheckpointContinuationAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
            // Call the actual issuer with its SAME genuine retained custody; inspect its
            // real issued pending binding. Reflection creates no receipt or replay authority.
            preparation = Assert.IsAssignableFrom<Task>(method.Invoke(rig.Tasks, [original, rig.Service, token]));
            await rig.TaskRows.OverrideReadEntered.Task.WaitAsync(token);
            var retained = (IDictionary)typeof(TaskExecutionCoordinator).GetField("_toolCheckpointContinuations", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(rig.Tasks)!;
            var actualBinding = retained[before.TaskId]!;
            Assert.Same(preparation, CheckpointField<Task>(actualBinding, "Preparation"));
            Assert.Null(actualBinding.GetType().GetField("Resolution", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(actualBinding));
            Assert.False(CheckpointField<bool>(actualBinding, "Transferred"));
            Assert.False(CheckpointField<bool>(actualBinding, "Claimed"));
            Assert.False(CheckpointField<bool>(actualBinding, "Bound"));
            Assert.Equal(2, rig.Tasks.LiveOriginalInvocationCount);
            Assert.False(preparation.IsCompleted);
            var oce = new OperationCanceledException("actual faulted read payload");
            var io = new IOException("actual read sibling");
            if (canceled)
            {
                rawCancellation.Cancel();
                read.SetCanceled(rawCancellation.Token);
            }
            else read.SetException([oce, io]);
            var failure = await Record.ExceptionAsync(() => preparation.WaitAsync(token));
            Assert.NotNull(failure);
            if (canceled)
            {
                Assert.True(read.Task.IsCanceled);
                Assert.True(preparation.IsCanceled);
                Assert.False(preparation.IsFaulted);
            }
            else
            {
                Assert.True(read.Task.IsFaulted);
                Assert.True(preparation.IsFaulted);
                Assert.False(preparation.IsCanceled);
                Assert.Contains(Leaves(failure!), cause => ReferenceEquals(cause, oce));
                Assert.Contains(Leaves(failure!), cause => ReferenceEquals(cause, io));
            }
            Assert.Equal(1, rig.Tasks.LiveOriginalInvocationCount);
            Assert.True(provider.Task.IsFaulted);
            Assert.Null(actualBinding.GetType().GetField("OriginalRecoveryWrite", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(actualBinding));
            Assert.Null(actualBinding.GetType().GetField("Resolution", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(actualBinding));
            Assert.Equal(0, rig.Workspace.Effects);
        }
        finally
        {
            read.TrySetException(new IOException("controlled independent read join"));
            provider.TrySetException(new IOException("controlled independent provider join"));
            if (preparation is not null) _ = await Record.ExceptionAsync(() => preparation);
            rig.TaskRows.OverrideRead = null;
            _ = await Record.ExceptionAsync(() => owning);
        }
    }

    [Fact]
    public async Task Copied_durable_tool_checkpoint_ids_cannot_replace_the_same_private_run_producer()
    {
        var token = TestContext.Current.CancellationToken;
        await using var rig = new Rig(temporary: false);
        rig.Capture.AskDuringCapture = false;
        var call = new TaskCompletionSource<OllamaToolResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        rig.Client.OriginalToolFailure = call.Task;
        var owning = rig.RunAsync(true);
        try
        {
            await rig.Client.ToolRequestEntered.Task.WaitAsync(token);
            var initial = rig.Service.CurrentCanonicalTask!;
            var boundary = ActualToolCheckpoint(rig, initial.TaskId);
            await CheckpointField<TaskCompletionSource>(boundary, "ActualCallCaptured").Task.WaitAsync(token);
            call.SetException(new IOException("same actual failed request"));
            Assert.NotNull(await Record.ExceptionAsync(() => owning.WaitAsync(token)));
            var original = (await rig.Tasks.GetAsync(initial.TaskId, token))!;
            // Controlled external row replacement carries the genuine observed IDs/history
            // but supplies a different run. It creates no source-owned input/settlement receipt.
            var copied = original with { ExecutionId = Guid.NewGuid(), PersistenceRevision = original.PersistenceRevision + 1 };
            await rig.TaskRows.UpsertAsync(copied, token);
            var failure = await Record.ExceptionAsync(() => rig.Tasks.StartObservedOriginalRunResumeAsync(copied.TaskId, copied.ExecutionId, token));
            Assert.NotNull(failure);
            var after = (await rig.Tasks.GetAsync(copied.TaskId, token))!;
            Assert.Equal(copied.PersistenceRevision, after.PersistenceRevision);
            Assert.Equal(copied.ExecutionId, after.ExecutionId);
            Assert.Equal(JsonSerializer.Serialize(copied.RecoveryObservation), JsonSerializer.Serialize(after.RecoveryObservation));
            Assert.Empty(after.RecoveryHistory);
            Assert.Equal(1, rig.Tasks.LiveOriginalInvocationCount);
            Assert.Single(rig.Conversations.Messages, message => message.Role == MessageRole.User);
            Assert.Equal(0, rig.Workspace.Effects);
            Assert.True(call.Task.IsFaulted);
            Assert.Same(call.Task, CheckpointField<Task>(boundary, "ActualCall"));
            Assert.False(CheckpointField<bool>(boundary, "ResponseDelivered"));
            Assert.DoesNotContain(rig.Stream, item => item.Kind == ChatStreamEventKind.AssistantCompleted);
        }
        finally { call.TrySetException(new IOException("controlled final join")); _ = await Record.ExceptionAsync(() => owning); }
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Genuine_checkpoint_input_stage_refuses_replaced_conversation_or_held_user_history(bool conversationChanged)
    {
        var token = TestContext.Current.CancellationToken;
        await using var rig = new Rig(temporary: false);
        rig.Capture.AskDuringCapture = false;
        var provider = new TaskCompletionSource<OllamaToolResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        rig.Client.OriginalToolFailure = provider.Task;
        var owning = rig.RunAsync(true);
        var history = new TaskCompletionSource<IReadOnlyList<ChatMessage>>(TaskCreationOptions.RunContinuationsAsynchronously);
        Task? validation = null;
        try
        {
            await rig.Client.ToolRequestEntered.Task.WaitAsync(token);
            var initial = rig.Service.CurrentCanonicalTask!;
            var boundary = ActualToolCheckpoint(rig, initial.TaskId);
            await CheckpointField<TaskCompletionSource>(boundary, "ActualCallCaptured").Task.WaitAsync(token);
            var original = boundary.GetType().GetField("Custody", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(boundary)!;
            var failure = new IOException("actual retained provider failure");
            provider.SetException(failure);
            Assert.NotNull(await Record.ExceptionAsync(() => owning.WaitAsync(token)));
            var expected = (await rig.Tasks.GetAsync(initial.TaskId, token))!;
            var acceptedUser = Assert.Single(rig.Conversations.Messages, message => message.Role == MessageRole.User);
            if (conversationChanged)
                rig.Conversations.Current = rig.Conversations.Current! with { Title = "replacement accepted conversation" };
            else rig.Conversations.OverrideHistory = history.Task;
            var method = typeof(TaskExecutionCoordinator).GetMethod("ValidateOriginalToolCheckpointInputAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
            validation = Assert.IsAssignableFrom<Task>(method.Invoke(rig.Tasks, [original, expected, token]));
            if (!conversationChanged)
            {
                // The exact retained raw history Task is the phase barrier. This is
                // actual input custody, not an eligible local-selector witness.
                while (!CheckpointOwnsActualSource(original, history.Task))
                { token.ThrowIfCancellationRequested(); await Task.Yield(); }
                Assert.False(validation.IsCompleted);
                var index = rig.Conversations.Messages.FindIndex(message => message.Id == acceptedUser.Id);
                rig.Conversations.Messages[index] = acceptedUser with { Content = "replacement accepted user input" };
                history.SetResult(rig.Conversations.Messages.ToArray());
            }
            Assert.NotNull(await Record.ExceptionAsync(() => validation.WaitAsync(token)));
            Assert.True(validation.IsFaulted);
            var after = (await rig.Tasks.GetAsync(initial.TaskId, token))!;
            Assert.Equal(expected.PersistenceRevision, after.PersistenceRevision);
            Assert.Equal(JsonSerializer.Serialize(expected.RecoveryObservation), JsonSerializer.Serialize(after.RecoveryObservation));
            Assert.Empty(after.RecoveryHistory);
            Assert.Equal(initial.TaskId, after.TaskId);
            Assert.Equal(initial.ContextId, after.ContextId);
            Assert.Equal(initial.ExecutionId, after.ExecutionId);
            Assert.Equal(1, rig.Tasks.LiveOriginalInvocationCount);
            Assert.True(provider.Task.IsFaulted);
            Assert.Same(failure, provider.Task.Exception!.InnerExceptions[0]);
            Assert.Equal(0, rig.Workspace.Effects);
            Assert.DoesNotContain(rig.Stream, item => item.Kind == ChatStreamEventKind.AssistantCompleted);
        }
        finally
        {
            history.TrySetResult(rig.Conversations.Messages.ToArray());
            provider.TrySetException(new IOException("controlled independent provider join"));
            if (validation is not null) _ = await Record.ExceptionAsync(() => validation);
            rig.Conversations.OverrideHistory = null;
            _ = await Record.ExceptionAsync(() => owning);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Genuine_stage_seal_or_capacity_refusal_releases_only_never_published_checkpoint_reservation(bool sealedProcess)
    {
        var token = TestContext.Current.CancellationToken;
        await using var rig = new Rig(temporary: false);
        rig.Capture.AskDuringCapture = false;
        var provider = new TaskCompletionSource<OllamaToolResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        rig.Client.OriginalToolFailure = provider.Task;
        var owning = rig.RunAsync(true);
        var heldRead = new TaskCompletionSource<TaskExecutionSnapshot?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var admitted = new List<Task>();
        TaskExecutionSnapshot? expected = null;
        try
        {
            await rig.Client.ToolRequestEntered.Task.WaitAsync(token);
            var initial = rig.Service.CurrentCanonicalTask!;
            var boundary = ActualToolCheckpoint(rig, initial.TaskId);
            await CheckpointField<TaskCompletionSource>(boundary, "ActualCallCaptured").Task.WaitAsync(token);
            var original = boundary.GetType().GetField("Custody", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(boundary)!;
            provider.SetException(new IOException("actual retained provider failure"));
            Assert.NotNull(await Record.ExceptionAsync(() => owning.WaitAsync(token)));
            expected = (await rig.Tasks.GetAsync(initial.TaskId, token))!;
            if (sealedProcess) rig.Tasks.RequestOriginalProcessRetirement();
            else
            {
                rig.TaskRows.OverrideRead = heldRead.Task;
                var stages = (IList)typeof(TaskExecutionCoordinator).GetField("_originalProcessStages", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(rig.Tasks)!;
                while (stages.Count < TaskExecutionCoordinator.OriginalInvocationCapacity)
                    admitted.Add(rig.Tasks.GetOriginalRunControlAvailabilityAsync(initial.TaskId, initial.ExecutionId, token));
                await rig.TaskRows.OverrideReadEntered.Task.WaitAsync(token);
                Assert.All(admitted, driver => Assert.False(driver.IsCompleted));
                Assert.Equal(TaskExecutionCoordinator.OriginalInvocationCapacity, stages.Count);
            }
            Assert.Equal(1, rig.Tasks.LiveOriginalInvocationCount);
            var prepare = typeof(TaskExecutionCoordinator).GetMethod("PrepareOriginalToolCheckpointContinuationAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var wrapper = Assert.Throws<TargetInvocationException>(() => { _ = prepare.Invoke(rig.Tasks, [original, rig.Service, token]); });
            var actualRefusal = Assert.IsType<InvalidOperationException>(wrapper.InnerException);
            Assert.Equal(1, rig.Tasks.LiveOriginalInvocationCount);
            var retained = (IDictionary)typeof(TaskExecutionCoordinator).GetField("_toolCheckpointContinuations", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(rig.Tasks)!;
            Assert.False(retained.Contains(initial.TaskId));
            var refusals = Assert.IsAssignableFrom<IReadOnlyList<Exception>>(original.GetType().GetProperty("OriginalToolCheckpointPublicationRefusals", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(original));
            Assert.Same(actualRefusal, Assert.Single(refusals));
            Assert.Same(provider.Task, CheckpointField<Task>(boundary, "ActualCall"));
            Assert.True(provider.Task.IsFaulted);
            Assert.Equal(0, rig.Workspace.Effects);
            Assert.DoesNotContain(rig.Stream, item => item.Kind == ChatStreamEventKind.AssistantCompleted);
        }
        finally
        {
            heldRead.TrySetResult(expected);
            foreach (var driver in admitted) _ = await Record.ExceptionAsync(() => driver);
            rig.TaskRows.OverrideRead = null;
            provider.TrySetException(new IOException("controlled independent provider join"));
            _ = await Record.ExceptionAsync(() => owning);
            if (sealedProcess) _ = await Record.ExceptionAsync(() => rig.Tasks.CloseAndSuspendOriginalProducersAsync());
        }
    }

    private static bool CheckpointOwnsActualSource(object original, Task sameActual)
    {
        var capture = original.GetType().GetMethod("CaptureRecoveryOriginals", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(original, null)!;
        var sources = (IEnumerable)capture.GetType().GetProperty("Sources", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.GetValue(capture)!;
        foreach (var source in sources)
            if (ReferenceEquals(source!.GetType().GetProperty("Actual", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.GetValue(source), sameActual)) return true;
        return false;
    }
}
