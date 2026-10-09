using System.Reflection;
using System.Text.Json;
using Haven.Application;
using Haven.Core;
using Xunit;

namespace Haven.Desktop.Tests;

public sealed partial class ChatCloudPermissionCallerTests
{
    [Fact]
    public async Task Process_seal_during_actual_context_capture_refuses_the_later_provider_factory()
    {
        var token = TestContext.Current.CancellationToken;
        await using var rig = new Rig();
        rig.Capture.AskDuringCapture = false;
        var capture = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        rig.Capture.OriginalToolCapture = capture.Task;
        var callbacks = 0;
        rig.Client.OriginalToolCallback = () => callbacks++;
        var owning = rig.RunAsync(true);
        try
        {
            await rig.Capture.ToolCaptureEntered.Task.WaitAsync(token);
            var current = rig.Service.CurrentCanonicalTask!;
            var boundary = ActualToolCheckpoint(rig, current.TaskId);
            Assert.Null(boundary.GetType().GetField("ActualCall", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(boundary));
            rig.Tasks.RequestOriginalProcessRetirement();
            capture.SetResult();
            var failure = await Record.ExceptionAsync(() => owning.WaitAsync(token));
            Assert.NotNull(failure);
            Assert.Equal(0, callbacks);
            Assert.False(rig.Client.ToolRequestEntered.Task.IsCompleted);
            Assert.Null(boundary.GetType().GetField("ActualCall", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(boundary));
            Assert.False(CheckpointField<bool>(boundary, "ResponseDelivered"));
            Assert.Equal(0, rig.Workspace.Effects);
            Assert.Equal(current.TaskId, rig.Service.CurrentCanonicalTask!.TaskId);
            Assert.Equal(current.ExecutionId, rig.Service.CurrentCanonicalTask.ExecutionId);
            Assert.DoesNotContain(rig.Stream, item => item.Kind == ChatStreamEventKind.AssistantCompleted);
        }
        finally
        {
            capture.TrySetResult();
            _ = await Record.ExceptionAsync(() => owning);
            _ = await Record.ExceptionAsync(() => rig.Tasks.CloseAndSuspendOriginalProducersAsync());
        }
    }

    private static object ActualToolCheckpoint(Rig rig, Guid taskId)
    {
        var originals = typeof(TaskExecutionCoordinator).GetField("_originalInvocations", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(rig.Tasks)!;
        object?[] arguments = [taskId, null];
        Assert.True((bool)originals.GetType().GetMethod("TryGetValue")!.Invoke(originals, arguments)!);
        var actual = arguments[1]!;
        return actual.GetType().GetField("OriginalToolCheckpoint", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(actual)!;
    }

    private static T CheckpointField<T>(object original, string field) =>
        (T)original.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(original)!;

    [Fact]
    public async Task Actual_failed_tool_request_retains_same_raw_task_transcript_and_fault_siblings_without_minting_recovery()
    {
        var token = TestContext.Current.CancellationToken;
        await using var rig = new Rig();
        rig.Capture.AskDuringCapture = false;
        var call = new TaskCompletionSource<OllamaToolResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        rig.Client.OriginalToolFailure = call.Task;
        var owning = rig.RunAsync(true);
        try
        {
            var actualRequest = await rig.Client.ToolRequestEntered.Task.WaitAsync(token);
            var current = rig.Service.CurrentCanonicalTask!;
            var boundary = ActualToolCheckpoint(rig, current.TaskId);
            await CheckpointField<TaskCompletionSource>(boundary, "ActualCallCaptured").Task.WaitAsync(token);
            Assert.Same(actualRequest, CheckpointField<OllamaToolRequest>(boundary, "OriginalRequest"));
            Assert.Same(call.Task, CheckpointField<Task>(boundary, "ActualCall"));
            Assert.Equal(current.TaskId, actualRequest.ExecutionContext!.TaskId);
            Assert.Equal(current.ExecutionId, actualRequest.ExecutionContext.ExecutionId);
            Assert.Equal(current.ContextId, actualRequest.ExecutionContext.ContextId);
            Assert.False(CheckpointField<bool>(boundary, "ResponseDelivered"));
            var sameWire = JsonSerializer.Serialize(actualRequest);
            var messages = Assert.IsAssignableFrom<IList<OllamaToolTurn>>(actualRequest.Messages);
            Assert.Throws<NotSupportedException>(() => { messages[0] = new("user", "replacement input"); });
            Assert.Equal(sameWire, JsonSerializer.Serialize(actualRequest));
            var oce = new OperationCanceledException("actual faulted tool response");
            var io = new IOException("actual raw tool-response sibling");
            call.SetException([oce, io]);
            var failure = await Record.ExceptionAsync(() => owning);
            Assert.NotNull(failure);
            Assert.True(owning.IsFaulted);
            Assert.True(call.Task.IsFaulted);
            Assert.Contains(Leaves(failure!), error => ReferenceEquals(error, oce));
            Assert.Contains(Leaves(failure!), error => ReferenceEquals(error, io));
            Assert.Same(oce, CheckpointField<Exception>(boundary, "ActualOutwardFailure"));
            Assert.False(CheckpointField<bool>(boundary, "ResponseDelivered"));
            var suspended = rig.Service.CurrentCanonicalTask!;
            Assert.Equal(current.TaskId, suspended.TaskId);
            Assert.Equal(current.ExecutionId, suspended.ExecutionId);
            Assert.Equal(TaskExecutionLifecycle.Suspended, suspended.State);
            Assert.NotNull(suspended.RecoveryObservation);
            Assert.Empty(suspended.RecoveryHistory);
            Assert.Equal(0, rig.Workspace.Effects);
            Assert.DoesNotContain(rig.Stream, item => item.Kind == ChatStreamEventKind.AssistantCompleted);
            // This controlled client supplies no genuine router/frame/native-effect witness.
            // Capturing its actual failure is deliberately not a resume grant.
            await Assert.ThrowsAsync<InvalidOperationException>(() => rig.Tasks.StartObservedOriginalRunResumeAsync(current.TaskId, current.ExecutionId, token));
        }
        finally { call.TrySetException(new IOException("controlled final join")); _ = await Record.ExceptionAsync(() => owning); }
    }

    [Fact]
    public void Detached_tool_transcript_preserves_nested_call_ids_arguments_schema_images_and_candidate_observations()
    {
        var arguments = new Dictionary<string, JsonElement> { ["path"] = JsonSerializer.SerializeToElement("original.txt") };
        var calls = new List<OllamaToolCall> { new("read_file", arguments, "SAME original tool id") };
        var images = new List<string> { "original image" };
        var turns = new List<OllamaToolTurn> { new("assistant", "partial text", calls, Images: images), new("tool", "original accepted output", ToolName: "read_file") };
        var required = new List<string> { "path" };
        var property = new Dictionary<string, object> { ["type"] = "string" };
        var properties = new Dictionary<string, object> { ["path"] = property };
        var definitions = new List<OllamaToolDefinition> { new("read_file", "read", properties, required) };
        var capabilities = new List<string> { "Text", "Tools" };
        var proposed = new OllamaToolRequest("original model", turns, definitions, EffortLevel.Medium)
        {
            ExecutionContext = new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 12)
            { SelectedCandidate = new("route", 3, "provider", "model", null, false, capabilities) }
        };
        var detach = typeof(ChatSessionService).GetMethod("DetachOriginalToolRequest", BindingFlags.Static | BindingFlags.NonPublic)!;
        var actual = Assert.IsType<OllamaToolRequest>(detach.Invoke(null, [proposed]));
        var originalWire = JsonSerializer.Serialize(actual);
        arguments["path"] = JsonSerializer.SerializeToElement("replacement.txt");
        calls.Clear(); images[0] = "replacement image"; turns.Clear(); required.Clear(); property["type"] = "integer"; properties.Clear(); definitions.Clear(); capabilities.Clear();
        Assert.Equal(originalWire, JsonSerializer.Serialize(actual));
        Assert.Equal("SAME original tool id", Assert.Single(actual.Messages[0].ToolCalls!).Id);
        Assert.Equal("original.txt", Assert.Single(actual.Messages[0].ToolCalls!).Arguments["path"].GetString());
        Assert.Equal("original accepted output", actual.Messages[1].Content);
        Assert.Equal("original image", Assert.Single(actual.Messages[0].Images!));
        Assert.Equal("path", Assert.Single(Assert.Single(actual.Tools).Required));
        Assert.Equal(new[] { "Text", "Tools" }, actual.ExecutionContext!.SelectedCandidate!.RequiredCapabilities);
        // Pure detachment of a DTO is not private issuance, owner validation or continuation.
    }

    [Fact]
    public async Task Delivered_actual_response_cannot_be_relabelled_as_an_unfinished_tool_request()
    {
        var token = TestContext.Current.CancellationToken;
        await using var rig = new Rig();
        rig.Capture.AskDuringCapture = false;
        var call = new TaskCompletionSource<OllamaToolResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        rig.Client.OriginalToolFailure = call.Task;
        var owning = rig.RunAsync(true);
        Task<OllamaToolResponse>? actualFrame = null;
        try
        {
            var actualRequest = await rig.Client.ToolRequestEntered.Task.WaitAsync(token);
            var current = rig.Service.CurrentCanonicalTask!;
            var boundary = ActualToolCheckpoint(rig, current.TaskId);
            await CheckpointField<TaskCompletionSource>(boundary, "ActualCallCaptured").Task.WaitAsync(token);
            // This controlled delivery case owns a real attempt/frame around the SAME raw
            // response Task. It proves delivery/completion custody, not provider routing.
            var candidate = await rig.Authority.CaptureSelectedRouteAsync(current, rig.Provider.Model,
                [ToolCapability.Text, ToolCapability.Tools], [], token);
            var admission = await rig.Tasks.StartAttemptAsync(current.TaskId, current.ExecutionId, candidate, token);
            var registration = rig.Runtime.RegisterOriginalAttemptAsync(admission, token);
            await registration;
            var running = rig.Tasks.MarkAttemptRunningAsync(current.TaskId, current.ExecutionId, admission.AttemptId, token);
            await running;
            actualFrame = rig.Runtime.StartOriginalFrameAsync(admission, _ => call.Task, token);
            call.SetResult(new("actual final content", []));
            await actualFrame.WaitAsync(token);
            await owning.WaitAsync(token);
            Assert.Same(actualRequest, CheckpointField<OllamaToolRequest>(boundary, "OriginalRequest"));
            Assert.Same(call.Task, CheckpointField<Task>(boundary, "ActualCall"));
            Assert.True(CheckpointField<bool>(boundary, "ResponseDelivered"));
            Assert.Null(boundary.GetType().GetField("ActualOutwardFailure", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(boundary));
            Assert.Equal(TaskExecutionLifecycle.Completed, rig.Service.CurrentCanonicalTask!.State);
            Assert.Equal(current.TaskId, rig.Service.CurrentCanonicalTask.TaskId);
            Assert.Equal(current.ExecutionId, rig.Service.CurrentCanonicalTask.ExecutionId);
            Assert.Equal("actual final content", Assert.Single(rig.Stream, item => item.Kind == ChatStreamEventKind.AssistantCompleted).Message!.Content);
            Assert.Equal(0, rig.Workspace.Effects);
            Assert.Equal(0, rig.Tasks.LiveOriginalInvocationCount);
        }
        finally
        {
            call.TrySetResult(new("controlled final join", []));
            if (actualFrame is not null) _ = await Record.ExceptionAsync(() => actualFrame);
            _ = await Record.ExceptionAsync(() => owning);
        }
    }

    [Fact]
    public async Task Actual_tool_response_acquisition_guards_restored_context_reentry_and_preserves_synchronous_oce_as_fault()
    {
        var token = TestContext.Current.CancellationToken;
        await using var rig = new Rig();
        rig.Capture.AskDuringCapture = false;
        var before = ExecutionContext.Capture()!;
        var oce = new OperationCanceledException("actual synchronous tool-response source callback");
        Task? improperJoin = null;
        rig.Client.OriginalToolCallback = () => ExecutionContext.Run(before, _ =>
        {
            Assert.Throws<InvalidOperationException>(() => { improperJoin = rig.Tasks.CloseAndSuspendOriginalProducersAsync(); });
            throw oce;
        }, null);
        var owning = rig.RunAsync(true);
        var failure = await Record.ExceptionAsync(() => owning.WaitAsync(token));
        Assert.NotNull(failure);
        Assert.True(owning.IsFaulted);
        Assert.False(owning.IsCanceled);
        Assert.Null(improperJoin);
        Assert.Contains(Leaves(failure!), error => ReferenceEquals(error, oce));
        var current = rig.Service.CurrentCanonicalTask!;
        var boundary = ActualToolCheckpoint(rig, current.TaskId);
        Assert.Null(boundary.GetType().GetField("ActualCall", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(boundary));
        Assert.False(CheckpointField<bool>(boundary, "ResponseDelivered"));
        Assert.Equal(TaskExecutionLifecycle.Suspended, current.State);
        Assert.NotNull(current.RecoveryObservation);
        Assert.Equal(0, rig.Workspace.Effects);
        Assert.DoesNotContain(rig.Stream, item => item.Kind == ChatStreamEventKind.AssistantCompleted);
    }
}
