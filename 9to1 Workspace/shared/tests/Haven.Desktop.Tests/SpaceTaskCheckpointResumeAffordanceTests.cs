using System.Reflection;
using System.Text.Json;
using Avalonia.Headless.XUnit;
using CakeOS.Cui.Runtime;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.Views.Pages.Tasks;
using HavenOS.Apps.Spaces.Tasks;
using Xunit;

namespace Haven.Desktop.Tests;

public sealed partial class ChatCloudPermissionCallerTests
{
    // Actual controlled source and widget. Copied availability observes a control;
    // it cannot issue the missing private router/local checkpoint witness.
    [AvaloniaFact]
    public async Task Checkpoint_resume_affordance_does_not_grant_actual_host_dispatch_from_copied_availability()
    {
        var token = TestContext.Current.CancellationToken;
        await using var rig = new Rig(temporary: false);
        var spaces = new SpaceRegistry(new CheckpointAffordanceSpaceSettings());
        var space = await spaces.CreateAsync("Actual controlled Space", cancellationToken: token);
        var conversation = rig.Conversation with
        {
            Mode = HavenMode.Tasks, Kind = ConversationKind.Task, SpaceId = space.Id, IsTemporary = false
        };
        await rig.Conversations.UpsertConversationAsync(conversation, token);
        rig.Capture.AskDuringCapture = false;
        var rawProvider = new TaskCompletionSource<OllamaToolResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        rig.Client.OriginalToolFailure = rawProvider.Task;
        var providerCause = new IOException("Actual failed tool request lacks a genuine router continuation witness.");
        SpaceTaskWidgetPage? widget = null;
        Task? activation = null, dispatch = null, close = null;
        var owning = RunActualTaskConversationAsync();
        try
        {
            await rig.Client.ToolRequestEntered.Task.WaitAsync(token);
            var initial = rig.Service.CurrentCanonicalTask!;
            var actualCheckpoint = ActualToolCheckpoint(rig, initial.TaskId);
            await CheckpointField<TaskCompletionSource>(actualCheckpoint, "ActualCallCaptured").Task.WaitAsync(token);
            rawProvider.SetException(providerCause);
            Assert.NotNull(await Record.ExceptionAsync(() => owning.WaitAsync(token)));
            var before = (await rig.Tasks.GetAsync(initial.TaskId, token))!;
            var actualAvailability = await rig.Tasks.GetOriginalRunControlAvailabilityAsync(before.TaskId, before.ExecutionId, token);
            Assert.False(actualAvailability.CanResumeUnstartedOriginal);
            Assert.False(actualAvailability.CanResumeOriginalToolCheckpoint);
            var user = Assert.Single(rig.Conversations.Messages, message => message.Role == MessageRole.User);
            var dispatches = rig.Client.Dispatches;
            var readiness = new CheckpointAffordanceUnavailableReadiness();
            widget = new(new SpaceTaskWorkspaceService(spaces, rig.Conversations, rig.Tasks), rig.Tasks,
                space.Id, conversation.Id, readiness,
                expectedOriginalTaskId: before.TaskId, expectedOriginalExecutionId: before.ExecutionId);
            activation = widget.ActivateAsync(token);
            _ = await Record.ExceptionAsync(() => activation.WaitAsync(token));
            Assert.True(activation.IsCompleted);
            Assert.Equal(1, readiness.Calls);
            Assert.False(widget.IsActionAvailable("task.resume-original") == true);

            typeof(SpaceTaskWidgetPage).GetField("_runControlAvailability", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(widget, actualAvailability with { CanResumeUnstartedOriginal = true });
            Assert.True(widget.IsActionAvailable("task.resume-original") == true);
            Assert.False(actualAvailability.CanResumeUnstartedOriginal);
            Assert.False(actualAvailability.CanResumeOriginalToolCheckpoint);

            // Deliberately adversarial detached observation. No private source, row,
            // issuer, route receipt, attempt or accepted input is constructed here.
            typeof(SpaceTaskWidgetPage).GetField("_runControlAvailability", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(widget, actualAvailability with { CanResumeOriginalToolCheckpoint = true });
            Assert.True(widget.IsActionAvailable("task.resume-original") == true);
            Assert.True(widget.TryGetValue("CanResumeOriginal", out var affordance));
            Assert.Equal(true, affordance);
            dispatch = widget.DispatchAsync("task.resume-original", null, token).AsTask();
            var refusal = await Record.ExceptionAsync(() => dispatch.WaitAsync(token));
            Assert.NotNull(refusal);
            Assert.True(dispatch.IsFaulted);
            Assert.False(dispatch.IsCanceled);
            Assert.Null(widget.OriginalRunResumeObservation);
            Assert.Null(widget.AcknowledgedOriginalRunControl);
            var after = (await rig.Tasks.GetAsync(before.TaskId, token))!;
            Assert.Equal(before.TaskId, after.TaskId);
            Assert.Equal(before.ExecutionId, after.ExecutionId);
            Assert.Equal(conversation.Id, after.ContextId);
            Assert.Equal(before.PersistenceRevision, after.PersistenceRevision);
            Assert.Equal(JsonSerializer.Serialize(before.RecoveryObservation), JsonSerializer.Serialize(after.RecoveryObservation));
            Assert.Equal(JsonSerializer.Serialize(before.RecoveryHistory), JsonSerializer.Serialize(after.RecoveryHistory));
            Assert.Equal(user, Assert.Single(rig.Conversations.Messages, message => message.Role == MessageRole.User));
            Assert.Equal(dispatches, rig.Client.Dispatches);
            Assert.Equal(0, rig.Workspace.Effects);
            Assert.True(rawProvider.Task.IsFaulted);
            Assert.Same(providerCause, rawProvider.Task.Exception!.InnerExceptions[0]);
            Assert.DoesNotContain(rig.Stream, value => value.Kind == ChatStreamEventKind.AssistantCompleted);
            widget.RequestRetirement();
            close = widget.CloseAndDrainAsync();
            var closeFailure = await Record.ExceptionAsync(() => close.WaitAsync(token));
            Assert.NotNull(closeFailure);
            Assert.True(close.IsFaulted);
            Assert.Same(close, widget.CloseAndDrainAsync());
            Assert.Contains(Leaves(closeFailure!), cause => Leaves(refusal!).Any(original => ReferenceEquals(original, cause)));
            Assert.Equal(before.ExecutionId, (await rig.Tasks.GetAsync(before.TaskId, token))!.ExecutionId);
        }
        finally
        {
            rawProvider.TrySetException(providerCause);
            _ = await Record.ExceptionAsync(() => owning);
            if (activation is not null) _ = await Record.ExceptionAsync(() => activation);
            if (dispatch is not null) _ = await Record.ExceptionAsync(() => dispatch);
            if (widget is not null)
            {
                widget.RequestRetirement();
                _ = await Record.ExceptionAsync(() => close ??= widget.CloseAndDrainAsync());
            }
        }

        async Task RunActualTaskConversationAsync()
        {
            var root = Assert.IsType<string>(typeof(Rig).GetField("_root", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(rig));
            var model = rig.Provider.Model.Model with
            {
                Name = rig.Provider.Model.Key, Capabilities = new HashSet<ToolCapability> { ToolCapability.Text, ToolCapability.Tools }
            };
            await foreach (var value in rig.Service.SendAsync(conversation, "Create the file", model,
                EffortLevel.Medium, [], "controlled", "", DuoMode.Solo, root, null, null, null, token,
                taskExecutionIntent: TaskRunExecutionIntent.CanonicalAgenticTask)) rig.Stream.Add(value);
        }
    }

    private sealed class CheckpointAffordanceUnavailableReadiness : ICuiSceneReadiness
    {
        internal int Calls;
        public ValueTask<CuiSceneAvailability> CheckAsync(CancellationToken token)
        {
            Calls++;
            return ValueTask.FromResult(new CuiSceneAvailability(CuiSceneAvailabilityState.Unavailable,
                "NO_ACTUAL_NATIVE_FRAME", "This refusal control cannot issue native readiness."));
        }
    }

    // Only the existing SpaceRegistry's settings input is controlled. The actual
    // Task/Run, conversation, private producer and source authority are the maintained Rig.
    private sealed class CheckpointAffordanceSpaceSettings : IVersionedSettingsStore
    {
        private readonly Dictionary<string, object> _values = [];
        public Task<T?> GetAsync<T>(string key, CancellationToken token) where T : class => Task.FromResult(_values.GetValueOrDefault(key) as T);
        public Task SetAsync<T>(string key, T value, CancellationToken token) where T : class { _values[key] = value; return Task.CompletedTask; }
        public Task RemoveAsync(string key, CancellationToken token) { _values.Remove(key); return Task.CompletedTask; }
        public Task<SettingsExportManifest> ExportAsync(CancellationToken token) => throw new NotSupportedException();
        public Task<SettingsImportResult> ImportAsync(SettingsExportManifest value, CancellationToken token) => throw new NotSupportedException();
    }
}
