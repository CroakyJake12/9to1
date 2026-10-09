#if !ANDROID
using System.Runtime.ExceptionServices;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Haven.Application;
using Haven.Core;
using Haven.Infrastructure;
using HavenOS.Apps.Assistants.Contracts;
using Xunit;

namespace HavenOS.Apps.Assistants.Memory.Tests;

public sealed partial class AssistantOriginalMemoryTests
{
    [AvaloniaFact]
    public async Task Maintained_native_Task_actor_stays_distinct_from_Home_while_actual_Task_controls_remain_usable()
    {
        var actualTaskActors = new HostLocalTaskActorSource();
        TaskProgressGraph? graph = null;
        var rig = new Rig(configuredTaskFactory: actual => (graph = new(actual, actualTaskActors)).Tasks,
            closeConfiguredTasks: () => graph?.CloseAsync() ?? Task.CompletedTask);
        var failures = new List<Exception>();
        try
        {
            await rig.InitializeAsync(true);
            var homeActor = (await rig.Profiles.GetCurrentAsync(Token))!;
            var taskActor = (await actualTaskActors.GetCurrentAsync(Token))!;
            Assert.NotNull(taskActor); Assert.NotEqual(homeActor.ActorId, taskActor.ActorId);
            var definition = await rig.Bridge.CreateAsync(ConfiguredIdentityKind.Assistant,
                new() { Name = "Fictional native Task helper" }, Guid.NewGuid(), Token);
            var binding = await rig.Bridge.CreateConversationAsync(definition.Identity, definition.Revision,
                Guid.NewGuid(), "Fictional native work", Guid.NewGuid(), Token, AssistantConversationKind.Task);
            var begin = graph!.Tasks.BeginAuthorizedAsync(binding.Conversation.Id, Guid.NewGuid(),
                "Review a fictional local draft", TaskExecutionDurability.PersistedPlan, [], Token);
            rig.Retain(begin); var begun = await begin;
            Assert.Equal(taskActor.ActorId, begun.OwnerBinding!.ActorId);
            Assert.Equal(taskActor.AuthenticationRevision, begun.OwnerBinding.AuthenticationRevision);
            Assert.NotEqual(homeActor.ActorId, begun.OwnerBinding.ActorId);
            await WithActualMemorySurface(rig, async (window, surface, controller, management) =>
            {
                window.Width = 1320; window.Height = 960;
                await OpenActualSavedMemoryConversation(surface, definition.Identity, binding.Conversation.Id, window);
                Assert.False(controller.Snapshot.Work!.RequiresOwnerRenewal);
                Assert.NotNull(controller.Snapshot.Work.Controls);
                Assert.False(surface.Bindings.IsActionAvailable("assistants.task.recovery.review"));
                AssertOriginalComposer(window).Text = "Start with the fictional introduction.";
                await ClickCanonicalCaptureControl(window, surface, "assistant-steer", () =>
                    controller.Snapshot.Work?.CanonicalTask?.Steers.Count == 1 &&
                    surface.Bindings.TryGetValue("TaskFollowUpResult", out var result) &&
                    Equals(result, "Steering applied: Start with the fictional introduction.") &&
                    surface.Bindings.IsActionAvailable("assistants.task.steer") == true);
                var current = controller.Snapshot.Work!.CanonicalTask!;
                Assert.Equal(begun.TaskId, current.TaskId); Assert.Equal(begun.ExecutionId, current.ExecutionId);
                Assert.Equal(begun.OwnerBinding, current.OwnerBinding); Assert.Empty(current.Attempts);
                Assert.Equal("Start with the fictional introduction.", Assert.Single(current.Steers).Summary);
                Assert.Equal(homeActor, await rig.Profiles.GetCurrentAsync(Token));
                // A saved prompt is not the enclosing action's terminal receipt.
                // Wait for the actual owner's full preparation boundary before
                // the unchanged fixture asserts PrepareToCloseAsync succeeds.
                await ClickCanonicalCaptureControl(window, surface, "assistant-save-draft", () => surface.IsOriginalClosePrepared);
            });
        }
        catch (Exception cause) { failures.Add(cause); }
        Task? close = null;
        try { close = rig.CloseAsync(); rig.Retain(close); await close; }
        catch (Exception cause) { failures.Add(close?.Exception ?? cause); }
        if (failures.Count != 0)
            lock (FailedTaskProgressOwners) FailedTaskProgressOwners.Add([rig, graph!, actualTaskActors, failures]);
        if (failures.Count == 1) ExceptionDispatchInfo.Capture(failures[0]).Throw();
        if (failures.Count != 0) throw new AggregateException("Actual native Task actor fixture retained its sources.", failures);
    }

    [AvaloniaFact]
    public async Task Fresh_Home_session_reads_saved_Task_history_but_requires_real_cold_recovery_before_controls()
    {
        TaskProgressGraph? graph = null;
        var graphs = new List<TaskProgressGraph>();
        var rig = new Rig(configuredTaskFactory: actual =>
            { graph = new(actual); graphs.Add(graph); return graph.Tasks; },
            closeConfiguredTasks: () => graph?.CloseAsync() ?? Task.CompletedTask);
        var failures = new List<Exception>();
        try
        {
            await rig.InitializeAsync(true);
            var definition = await rig.Bridge.CreateAsync(ConfiguredIdentityKind.Assistant,
                new() { Name = "Fictional saved work helper" }, Guid.NewGuid(), Token);
            var originalBinding = await rig.Bridge.CreateConversationAsync(definition.Identity, definition.Revision,
                Guid.NewGuid(), "Fictional work across sessions", Guid.NewGuid(), Token, AssistantConversationKind.Task);
            var begin = graph!.Tasks.BeginAuthorizedAsync(originalBinding.Conversation.Id, Guid.NewGuid(),
                "Review this fictional draft after setup", TaskExecutionDurability.PersistedPlan, [], Token);
            rig.Retain(begin); var begun = await begin;
            Assert.NotNull(begun.OwnerBinding); Assert.Empty(begun.Attempts);
            var before = await rig.Bridge.ReadWorkAsync(originalBinding, Token);
            Assert.False(before.RequiresOwnerRenewal); Assert.NotNull(before.Controls);
            await rig.Bridge.SaveConversationDraftAsync(originalBinding, null,
                "Keep this unsent draft through recovery review.", [], Token);
            // The real Home identity source opens a fresh authentication session.
            // No old actor revision, Task owner or recovery capsule is rewritten.
            await rig.ReopenAsync();
            var actor = (await rig.Profiles.GetCurrentAsync(Token))!;
            Assert.Equal(begun.OwnerBinding!.ActorId, actor.ActorId);
            Assert.NotEqual(begun.OwnerBinding.AuthenticationRevision, actor.AuthenticationRevision);
            await WithActualMemorySurface(rig, async (window, surface, controller, management) =>
            {
                window.Width = 1320; window.Height = 960;
                await OpenActualSavedMemoryConversation(surface, definition.Identity, originalBinding.Conversation.Id, window);
                var actualWork = controller.Snapshot.Work!;
                Assert.True(actualWork.RequiresOwnerRenewal); Assert.Null(actualWork.Controls);
                Assert.Equal(begun.TaskId, actualWork.CanonicalTask!.TaskId);
                Assert.Equal(begun.ExecutionId, actualWork.CanonicalTask.ExecutionId);
                Assert.Equal(begun.OwnerBinding, actualWork.CanonicalTask.OwnerBinding);
                AssertTaskText(window, begun.PromptSummary);
                AssertTaskText(window, "Review recovery to continue this Task in your current session.");
                Assert.Equal("Keep this unsent draft through recovery review.", AssertOriginalComposer(window).Text);
                Assert.False(surface.Bindings.IsActionAvailable("assistants.task.steer"));
                Assert.False(surface.Bindings.IsActionAvailable("assistants.task.queue"));
                Assert.False(surface.Bindings.IsActionAvailable("assistants.resume"));
                Assert.False(surface.Bindings.IsActionAvailable("assistants.pause"));
                Assert.False(surface.Bindings.IsActionAvailable("assistants.task.stop"));
                await ClickCanonicalCaptureControl(window, surface, "assistant-cold-task-review", () =>
                    surface.Bindings.TryGetValue("ColdTaskRecoveryReason", out var reason) &&
                    Equals(reason, "Recovery is not configured for this host. Your saved Task and conversation remain available.") &&
                    surface.Bindings.IsActionAvailable("assistants.task.recovery.review") == true);
                await FlushNativeMemoryUi(window);
                AssertTaskText(window, "Recovery is not configured for this host. Your saved Task and conversation remain available.");
                Assert.False(surface.Bindings.IsActionAvailable("assistants.task.recovery.resume"));
                var resume = OriginalComposerButton(window, "assistant-cold-task-resume");
                Assert.False(resume.IsEffectivelyEnabled);
                var read = graph!.Tasks.GetAsync(begun.TaskId, Token); rig.Retain(read);
                var persisted = (await read)!;
                Assert.Equal(begun.TaskId, persisted.TaskId); Assert.Equal(begun.ExecutionId, persisted.ExecutionId);
                Assert.Equal(begun.OwnerBinding, persisted.OwnerBinding); Assert.Empty(persisted.Attempts);
                Assert.Empty(persisted.Plan); Assert.Null(persisted.CheckpointId);
                Assert.Empty(controller.Snapshot.Conversation!.Messages);
                Assert.False(surface.HasUnsavedChanges);
            });
        }
        catch (Exception cause) { failures.Add(cause); }
        Task? close = null;
        try { close = rig.CloseAsync(); rig.Retain(close); await close; }
        catch (Exception cause) { failures.Add(close?.Exception ?? cause); }
        if (failures.Count != 0)
            lock (FailedTaskProgressOwners) FailedTaskProgressOwners.Add([rig, graphs, failures]);
        if (failures.Count == 1) ExceptionDispatchInfo.Capture(failures[0]).Throw();
        if (failures.Count != 0) throw new AggregateException("Actual cold Task history fixture retained its sources.", failures);
    }
}
#endif
