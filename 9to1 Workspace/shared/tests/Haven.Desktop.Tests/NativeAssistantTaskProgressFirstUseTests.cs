#if !ANDROID
using System.Collections;
using System.Runtime.ExceptionServices;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Haven.Application;
using Haven.Core;
using Haven.Infrastructure;
using HavenOS.Apps.Assistants.Contracts;
using HavenOS.Apps.Assistants.NativeUI;
using Xunit;

namespace HavenOS.Apps.Assistants.Memory.Tests;

public sealed partial class AssistantOriginalMemoryTests
{
    private static readonly List<object[]> FailedTaskProgressOwners = [];
    [AvaloniaFact]
    public async Task Real_task_steering_and_queue_reopen_in_the_same_Home_session_without_a_model_attempt()
    {
        TaskProgressGraph? graph = null;
        var rig = new Rig(configuredTaskFactory: actual => (graph = new(actual)).Tasks,
            closeConfiguredTasks: () => graph?.CloseAsync() ?? Task.CompletedTask);
        var failures = new List<Exception>();
        try
        {
            await rig.InitializeAsync(true);
            var definition = await rig.Bridge.CreateAsync(ConfiguredIdentityKind.Assistant,
                new() { Name = "Fictional progress helper" }, Guid.NewGuid(), Token);
            var binding = await rig.Bridge.CreateConversationAsync(definition.Identity, definition.Revision,
                Guid.NewGuid(), "Fictional planned work", Guid.NewGuid(), Token, AssistantConversationKind.Task);
            var begin = graph!.Tasks.BeginAuthorizedAsync(binding.Conversation.Id, Guid.NewGuid(),
                "Review a fictional draft when a model becomes available", TaskExecutionDurability.PersistedPlan, [], Token);
            rig.Retain(begin); var begun = await begin;
            Assert.NotNull(begun.OwnerBinding); Assert.Empty(begun.Attempts); Assert.Empty(begun.Plan);
            Assert.Equal((await rig.Profiles.GetCurrentAsync(Token))!.ActorId, begun.OwnerBinding!.ActorId);
            Guid steerId = Guid.Empty, queueId = Guid.Empty;
            await WithActualMemorySurface(rig, async (window, surface, controller, management) =>
            {
                window.Width = 1320; window.Height = 960;
                await OpenActualSavedMemoryConversation(surface, definition.Identity, binding.Conversation.Id, window);
                AssertTaskText(window, begun.PromptSummary); AssertTaskText(window, "No model attempt has started.");
                AssertTaskText(window, "No recovery checkpoint has been saved.");
                var input = AssertOriginalComposer(window);
                input.Text = "First review the introduction.";
                await ClickCanonicalCaptureControl(window, surface, "assistant-steer", () =>
                    controller.Snapshot.Work?.CanonicalTask?.Steers.Count == 1 &&
                    surface.Bindings.TryGetValue("TaskFollowUpResult", out var result) && Equals(result, "Steering applied: First review the introduction.") &&
                    surface.Bindings.IsActionAvailable("assistants.task.queue") == true);
                await FlushNativeMemoryUi(window);
                AssertTaskText(window, "Steering applied: First review the introduction.");
                var steered = controller.Snapshot.Work!.CanonicalTask!;
                steerId = Assert.Single(steered.Steers).Id;
                Assert.Equal(SteerInstructionState.Applied, steered.Steers[0].State);
                Assert.True(steered.PersistenceRevision > begun.PersistenceRevision);
                input.Text = "Then review the conclusion.";
                await ClickCanonicalCaptureControl(window, surface, "assistant-queue", () =>
                    controller.Snapshot.Work?.CanonicalTask?.Queue.Count == 1 &&
                    surface.Bindings.TryGetValue("TaskFollowUpResult", out var result) && Equals(result, "Follow-up queued: Then review the conclusion.") &&
                    surface.Bindings.IsActionAvailable("assistants.task.steer") == true);
                await FlushNativeMemoryUi(window);
                AssertTaskText(window, "Follow-up queued: Then review the conclusion.");
                var queued = controller.Snapshot.Work!.CanonicalTask!;
                queueId = Assert.Single(queued.Queue).TaskId;
                Assert.Equal(begun.TaskId, queued.TaskId); Assert.Equal(begun.ExecutionId, queued.ExecutionId);
                Assert.Empty(queued.Attempts); Assert.Null(queued.CheckpointId);
                Assert.True(surface.Bindings.TryGetValue("TaskQueueRows", out var rows));
                var actualRow = Assert.Single(Assert.IsAssignableFrom<IEnumerable>(rows).Cast<object>());
                Assert.True(surface.Bindings.TryGetItemValue(actualRow, "Summary", out var summary));
                Assert.Equal("Then review the conclusion.", summary);
                Assert.False(surface.Bindings.TryGetItemValue(new { Id = queueId, Summary = summary }, "Summary", out _));
                await ClickCanonicalCaptureControl(window, surface, "assistant-save-draft", () => !surface.HasUnsavedChanges);
                await ClickCanonicalCaptureControl(window, surface, "new-conversation", () =>
                    controller.Snapshot.ConversationBinding is { } opened && opened.Conversation.Id != binding.Conversation.Id &&
                    surface.Bindings.TryGetValue("HasTaskProgress", out var shown) && shown is false);
                Assert.False(surface.Bindings.TryGetItemValue(actualRow, "Summary", out _));
                Assert.True(surface.Bindings.TryGetValue("HasTaskProgress", out var progress)); Assert.False(Assert.IsType<bool>(progress));
                // Reopen the saved conversation in the SAME actual authenticated
                // Home session. A fresh process actor needs canonical renewal;
                // this UI control does not forge that separate recovery proof.
                await OpenActualSavedMemoryConversation(surface, definition.Identity, binding.Conversation.Id, window);
                var current = controller.Snapshot.Work!.CanonicalTask!;
                Assert.Equal(begun.TaskId, current.TaskId); Assert.Equal(begun.ExecutionId, current.ExecutionId);
                Assert.Equal(steerId, Assert.Single(current.Steers).Id); Assert.Equal(queueId, Assert.Single(current.Queue).TaskId);
                AssertTaskText(window, "First review the introduction."); AssertTaskText(window, "Then review the conclusion.");
                Assert.Empty(current.Attempts); Assert.Null(current.CheckpointId);
                Assert.Empty(controller.Snapshot.Conversation!.Messages);
                Assert.Equal("Then review the conclusion.", AssertOriginalComposer(window).Text);
            });
        }
        catch (Exception cause) { failures.Add(cause); }
        Task? close = null;
        try { close = rig.CloseAsync(); rig.Retain(close); await close; }
        catch (Exception cause) { failures.Add(close?.Exception ?? cause); }
        if (failures.Count != 0)
            lock (FailedTaskProgressOwners) FailedTaskProgressOwners.Add([rig, graph!, failures]);
        if (failures.Count == 1) ExceptionDispatchInfo.Capture(failures[0]).Throw();
        if (failures.Count != 0) throw new AggregateException("Actual Task progress fixture and sources remain retained.", failures);
    }

    private static void AssertTaskText(Window window, string text) => Assert.Contains(
        window.GetVisualDescendants().OfType<TextBlock>(), value => value.IsEffectivelyVisible && value.Text == text);

    // The production authority issues the real Task owner from the SAME current
    // Home actor. No provider, attempt, tool result or permission is invented.
    private sealed class TaskProgressGraph
    {
        internal TaskExecutionCoordinator Tasks { get; }
        private readonly TaskRunPermissionAuthority _authority;
        private readonly ExecutionEventHub _events;
        private readonly ModelProviderRegistry _registry = new([]);
        private readonly ProviderConfigurationStore _providers;
        private readonly List<Task> _originals = [];
        private Task? _close;
        internal TaskProgressGraph(Rig rig, IAuthenticatedResourceActorSource? actualTaskActors = null)
        {
            var paths = new Paths(rig.Root);
            _providers = new(paths);
            _authority = new(actualTaskActors ?? rig.Profiles, _registry, _providers, new PrivacyPreferenceStore(paths),
                new ModelPermissionEvaluator(new VersionedModelPermissionStore(new VersionedAtomicSettingsStore(paths))));
            _events = new(new ExecutionEventRepository(rig.Database));
            Tasks = new(new TaskExecutionRepository(rig.Database), _events, admissionAuthority: _authority);
        }
        internal Task CloseAsync() => _close ??= CloseBody();
        private async Task CloseBody()
        {
            // Seal new admission even when an existing producer's join fails.
            // Unknown producers retain their dependent event/registry owners.
            _authority.RequestOriginalAdmissionSeal();
            await Join(Tasks.CloseAndSuspendOriginalProducersAsync);
            await Join(() => _events.DisposeAsync().AsTask());
            await Join(_registry.CloseOriginalCataloguesAndDrainAsync);
            _providers.Dispose();
        }
        private async Task Join(Func<Task> acquire)
        {
            var actual = acquire(); _originals.Add(actual);
            try { await actual; }
            catch (Exception cause) { ExceptionDispatchInfo.Capture(actual.Exception ?? cause).Throw(); throw; }
        }
    }
}
#endif
