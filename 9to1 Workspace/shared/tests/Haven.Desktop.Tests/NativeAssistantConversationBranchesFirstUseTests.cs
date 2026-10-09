#if !ANDROID
using System.Collections;
using System.ComponentModel;
using System.Reflection;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using CakeOS.Cui;
using Haven.Application;
using Haven.Core;
using HavenOS.Apps.Assistants.Contracts;
using HavenOS.Apps.Assistants.NativeUI;
using Xunit;

namespace HavenOS.Apps.Assistants.Memory.Tests;

public sealed partial class AssistantOriginalMemoryTests
{
    [AvaloniaFact]
    public Task Rendered_conversation_branch_preserves_original_history_and_independent_drafts_after_reopen() => RunAsync(async rig =>
    {
        var original = await rig.CreateAsync(new AssistantConfiguration { Name = "Fictional branching Assistant", Instructions = "Keep the same identity." });
        var first = new ChatMessage(Guid.NewGuid(), original.Conversation.Id, MessageRole.User, "First saved direction.", null, null, null, DateTimeOffset.UtcNow);
        var later = new ChatMessage(Guid.NewGuid(), original.Conversation.Id, MessageRole.User, "Later saved direction.", null, null, null, first.CreatedAt.AddSeconds(1));
        foreach (var message in new[] { first, later })
        { var write = rig.OriginalConversations.AddMessageAsync(message, Token); rig.Retain(write); await write; }
        Guid main = Guid.Empty, branch = Guid.Empty;
        const string mainDraft = "Keep the original draft here.", branchDraft = "A different direction on this branch.";
        await WithActualProactivitySurface(rig, async (window, surface, controller) =>
        {
            await OpenActualSavedMemoryConversation(surface, original.Definition.Identity, original.Conversation.Id, window);
            main = Assert.Single(controller.Snapshot.Conversation!.Branches, value => value.IsCurrent).Id;
            var message = Assert.Single(BranchMessageRows(surface), row => row.Id == first.Id);
            var copiedMessage = message with { };
            await surface.Bindings.DispatchAsync("assistants.branch.create", copiedMessage, Token);
            Assert.Single(controller.Snapshot.Conversation!.Branches);
            Assert.True(surface.Bindings.TrySetValue("Prompt", mainDraft));
            var continueButton = BranchActionButton(window, "message", message);
            var observedBusyComposer = false; var acceptedDuringBranch = false;
            PropertyChangedEventHandler observeBranch = (_, change) =>
            {
                if (observedBusyComposer || change.PropertyName != "CanEditPrompt" ||
                    !surface.Bindings.TryGetValue("CanEditPrompt", out var canEdit) || !Equals(canEdit, false)) return;
                observedBusyComposer = true;
                acceptedDuringBranch = surface.Bindings.TrySetValue("Prompt", "Must not replace a branch draft.") |
                    surface.Bindings.TrySetValue("DraftDescription", "Must not edit a switching identity.");
            };
            surface.Bindings.PropertyChanged += observeBranch;
            try
            {
                await ClickCanonicalCaptureControl(window, surface, continueButton, () =>
                    controller.Snapshot.Conversation?.Branches.Count == 2 && surface.IsOriginalClosePrepared);
            }
            finally { surface.Bindings.PropertyChanged -= observeBranch; }
            Assert.True(observedBusyComposer); Assert.False(acceptedDuringBranch);
            var actual = controller.Snapshot.Conversation!;
            var current = Assert.Single(actual.Branches, value => value.IsCurrent); branch = current.Id;
            Assert.Equal(main, current.ParentBranchId); Assert.Equal(first.Id, current.ForkedFromMessageId);
            Assert.Equal(first, Assert.Single(actual.Messages)); Assert.Equal("", surface.Bindings.Prompt);
            Assert.False(surface.Bindings.TryGetItemValue(message, "Content", out _));
            await surface.Bindings.DispatchAsync("assistants.branch.create", message, Token);
            Assert.Equal(2, controller.Snapshot.Conversation!.Branches.Count);
            Assert.True(surface.Bindings.TrySetValue("Prompt", branchDraft));
            var oldMainRow = Assert.Single(BranchRows(surface), value => value.Id == main);
            var copiedBranch = typeof(object).GetMethod("MemberwiseClone", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(oldMainRow, null)!;
            await surface.Bindings.DispatchAsync("assistants.branch.switch", copiedBranch, Token);
            Assert.Equal(branch, Assert.Single(controller.Snapshot.Conversation!.Branches, value => value.IsCurrent).Id);
            await ClickCanonicalCaptureControl(window, surface, BranchActionButton(window, "branch", oldMainRow), () =>
                controller.Snapshot.Conversation?.Branches.SingleOrDefault(value => value.IsCurrent)?.Id == main && surface.IsOriginalClosePrepared);
            Assert.Equal(mainDraft, surface.Bindings.Prompt);
            Assert.Equal(new[] { first.Id, later.Id }, controller.Snapshot.Conversation!.Messages.Select(value => value.Id));
            Assert.False(surface.Bindings.TryGetItemValue(oldMainRow, "Label", out _));
            await surface.Bindings.DispatchAsync("assistants.configuration.open", null, Token);
            Assert.True(surface.Bindings.TrySetValue("DraftDescription", "Unsaved preferences"));
            Assert.False(surface.Bindings.IsActionAvailable("assistants.branch.switch") == true);
            Assert.False(surface.Bindings.IsActionAvailable("assistants.branch.create") == true);
            await surface.Bindings.DispatchAsync("assistants.configuration.discard", null, Token);
            await surface.Bindings.DispatchAsync("assistants.configuration.back", null, Token);
            await FlushNativeMemoryUi(window);
            await ClickCanonicalCaptureControl(window, surface, BranchActionButton(window, "branch", Assert.Single(BranchRows(surface), value => value.Id == branch)), () =>
                controller.Snapshot.Conversation?.Branches.SingleOrDefault(value => value.IsCurrent)?.Id == branch && surface.IsOriginalClosePrepared);
            Assert.Equal(branchDraft, surface.Bindings.Prompt); Assert.Equal(first, Assert.Single(controller.Snapshot.Conversation!.Messages));
            Assert.Equal(original.Definition.Identity, controller.Snapshot.SelectedAssistant!.Identity);
            Assert.Equal(original.Definition.Revision, controller.Snapshot.SelectedAssistant!.Revision);
            Assert.Equal(JsonSerializer.Serialize(original.Definition.Configuration), JsonSerializer.Serialize(controller.Snapshot.SelectedAssistant!.Configuration));
            Assert.Single(controller.Snapshot.Conversations); Assert.Null(controller.Snapshot.Conversation!.CanonicalTask); Assert.Empty(controller.Snapshot.Models);
        });
        await rig.ReopenAsync();
        await WithActualProactivitySurface(rig, async (window, surface, controller) =>
        {
            await OpenActualSavedMemoryConversation(surface, original.Definition.Identity, original.Conversation.Id, window);
            Assert.Equal(branch, Assert.Single(controller.Snapshot.Conversation!.Branches, value => value.IsCurrent).Id);
            Assert.Equal(branchDraft, surface.Bindings.Prompt); Assert.Equal(first, Assert.Single(controller.Snapshot.Conversation!.Messages));
            await ClickCanonicalCaptureControl(window, surface, BranchActionButton(window, "branch", Assert.Single(BranchRows(surface), value => value.Id == main)), () =>
                controller.Snapshot.Conversation?.Branches.SingleOrDefault(value => value.IsCurrent)?.Id == main && surface.IsOriginalClosePrepared);
            Assert.Equal(mainDraft, surface.Bindings.Prompt);
            Assert.Equal(new[] { first.Id, later.Id }, controller.Snapshot.Conversation!.Messages.Select(value => value.Id));
            Assert.Equal(original.Definition.Identity, controller.Snapshot.SelectedAssistant!.Identity);
        });
    }, importMemory: false);

    [AvaloniaFact]
    public async Task Real_Task_started_after_the_published_snapshot_blocks_branch_change_without_changing_its_owner_or_history()
    {
        TaskProgressGraph? graph = null;
        var rig = new Rig(configuredTaskFactory: actual => (graph = new(actual)).Tasks,
            closeConfiguredTasks: () => graph?.CloseAsync() ?? Task.CompletedTask);
        var errors = new List<Exception>(); FailedTaskProgressOwners.Add([rig, errors]);
        try
        {
            await rig.InitializeAsync(true, importMemory: false);
            var definition = await rig.Bridge.CreateAsync(ConfiguredIdentityKind.Assistant, new() { Name = "Fictional active Task" }, Guid.NewGuid(), Token);
            var binding = await rig.Bridge.CreateConversationAsync(definition.Identity, definition.Revision, Guid.NewGuid(),
                "Unstarted work", Guid.NewGuid(), Token, AssistantConversationKind.Task);
            var message = new ChatMessage(Guid.NewGuid(), binding.Conversation.Id, MessageRole.User, "Retain this task history.", null, null, null, DateTimeOffset.UtcNow);
            var write = rig.OriginalConversations.AddMessageAsync(message, Token); rig.Retain(write); await write;
            await WithActualProactivitySurface(rig, async (window, surface, controller) =>
            {
                await OpenActualSavedMemoryConversation(surface, definition.Identity, binding.Conversation.Id, window);
                var actual = Assert.Single(BranchMessageRows(surface));
                var action = BranchActionButton(window, "message", actual);
                Assert.True(action.IsEnabled); Assert.Null(controller.Snapshot.Work!.CanonicalTask);
                var begin = graph!.Tasks.BeginAuthorizedAsync(binding.Conversation.Id, Guid.NewGuid(), "No model execution requested",
                    TaskExecutionDurability.PersistedPlan, [], Token); rig.Retain(begin); var begun = await begin;
                Assert.NotNull(begun.OwnerBinding); Assert.Empty(begun.Attempts);
                Assert.Null(controller.Snapshot.Work!.CanonicalTask); // The existing view has not observed this independent producer yet.
                await ClickCanonicalCaptureControl(window, surface, action, () =>
                    controller.Snapshot.Work?.CanonicalTask?.TaskId == begun.TaskId && surface.IsOriginalClosePrepared);
                Assert.False(surface.Bindings.IsActionAvailable("assistants.branch.create") == true);
                Assert.False(surface.Bindings.IsActionAvailable("assistants.branch.switch") == true);
                Assert.False(BranchActionButton(window, "message", actual).IsEnabled);
                Assert.Single(controller.Snapshot.Conversation!.Branches);
                Assert.Equal(message, Assert.Single(controller.Snapshot.Conversation!.Messages));
                var task = Assert.IsType<TaskExecutionSnapshot>(controller.Snapshot.Work!.CanonicalTask);
                Assert.Equal(begun.TaskId, task.TaskId); Assert.Equal(begun.ExecutionId, task.ExecutionId);
                Assert.Equal(begun.OwnerBinding, task.OwnerBinding); Assert.Empty(task.Attempts);
            });
        }
        catch (Exception cause) { errors.Add(cause); }
        finally
        {
            Task? close = null;
            try { close = rig.CloseAsync(); rig.Retain(close); await close; }
            catch (Exception cause) { errors.Add(close?.Exception ?? cause); }
        }
        if (errors.Count != 0) throw new AggregateException("Actual Task branch guard and original source retirement.", errors);
    }
    private static AssistantMessagePresentation[] BranchMessageRows(AssistantsNativeCuiSurface surface)
    {
        Assert.True(surface.Bindings.TryGetValue("Messages", out var rows));
        return Assert.IsAssignableFrom<IEnumerable>(rows).Cast<AssistantMessagePresentation>().ToArray();
    }
    private static AssistantsCuiBindings.ConversationBranchRow[] BranchRows(AssistantsNativeCuiSurface surface)
    {
        Assert.True(surface.Bindings.TryGetValue("ConversationBranches", out var rows));
        return Assert.IsAssignableFrom<IEnumerable>(rows).Cast<AssistantsCuiBindings.ConversationBranchRow>().ToArray();
    }
    private static Button BranchActionButton(Window window, string variable, object actual) => Assert.Single(
        window.GetVisualDescendants().OfType<Button>(), button => button.IsEffectivelyVisible &&
            button.DataContext is ICuiBindingContext binding && binding.TryGetValue(variable, out var row) && ReferenceEquals(row, actual) &&
            (variable == "branch" || Equals(button.Content, "Continue from here")));
}
#endif
