using System.Collections;
using System.Text.Json;
using Haven.Application;
using Haven.Core;
using HavenOS.Apps.Assistants.Contracts;
using HavenOS.Apps.Assistants.Core;
using Xunit;

namespace HavenOS.Apps.Assistants.NativeUI.Tests;

/// <summary>Typed presentation observations only; no native page, provider call or product authority is fabricated.</summary>
public sealed class AssistantsPromptProjectionTests
{
    private static readonly string[] RepeatSources =
    [
        "Assistants", "RecentConversations", "SelectedConversations", "DraftAttachments", "Models",
        "EffortChoices", "ConfigurationSettings", "Resources", "ActiveWork", "SelectedWork",
        "TaskPlanRows", "TaskSteerRows", "TaskQueueRows", "ConversationBranches"
    ];

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Typing_keeps_unrelated_rows_and_original_snapshot_without_repeat_notifications(bool runningTask)
    {
        var (bindings, snapshot, _) = Create(runningTask ? ConversationKind.Task : ConversationKind.Chat,
            runningTask ? TaskExecutionLifecycle.Running : null);
        var retained = RepeatSources.ToDictionary(name => name, name => Value<object>(bindings, name));
        var originalRows = retained.ToDictionary(pair => pair.Key,
            pair => Assert.IsAssignableFrom<IEnumerable>(pair.Value).Cast<object>().ToArray());
        var notifications = new List<string>(); var promptNotifications = 0;
        bindings.PropertyChanged += (_, args) => notifications.Add(args.PropertyName ?? "<all>");
        bindings.PromptChanged += () =>
        {
            promptNotifications++;
            Assert.Equal(bindings.Prompt, Value<string>(bindings, "Prompt"));
            notifications.Add("PromptChanged");
        };

        foreach (var prompt in new[] { "One", "One more", "One more", " \t", "Current typed draft" })
        {
            notifications.Clear();
            Assert.True(bindings.TrySetValue("Prompt", prompt));
            Assert.Equal(prompt, bindings.Prompt);
            Assert.Equal("PromptChanged", notifications.Last());
            Assert.DoesNotContain(notifications, name => RepeatSources.Contains(name, StringComparer.Ordinal));
            foreach (var (name, source) in retained)
            {
                Assert.Same(source, Value<object>(bindings, name));
                var rows = Assert.IsAssignableFrom<IEnumerable>(source).Cast<object>().ToArray();
                Assert.Equal(originalRows[name].Length, rows.Length);
                for (var index = 0; index < rows.Length; index++) Assert.Same(originalRows[name][index], rows[index]);
            }
            Assert.Same(snapshot, bindings.Snapshot);
            Assert.Same(snapshot.ConversationBinding, bindings.Snapshot.ConversationBinding);
            Assert.Equal("Saved draft", snapshot.Conversation!.Draft!.Content);
            Assert.True(Value<bool>(bindings, "CanSaveDraft"));
        }
        Assert.Equal(5, promptNotifications); // Accepted equal text still raises the original notification.
    }

    [Fact]
    public void Chat_text_updates_Send_and_empty_guidance_while_Save_keeps_its_existing_gate()
    {
        var (bindings, _, _) = Create();
        Assert.True(bindings.TrySetValue("Prompt", " \t"));
        Assert.False(Value<bool>(bindings, "CanSend"));
        Assert.False(bindings.IsActionAvailable("assistants.send"));
        Assert.Equal("Write a message to begin.", Value<string>(bindings, "ComposerAvailability"));
        Assert.True(bindings.TrySetValue("Prompt", "Use this original draft"));
        Assert.True(Value<bool>(bindings, "CanSend"));
        Assert.True(bindings.IsActionAvailable("assistants.send"));
        Assert.False(Value<bool>(bindings, "CanStartTask"));
        Assert.False(Value<bool>(bindings, "CanSteer"));
        Assert.Equal("", Value<string>(bindings, "ComposerAvailability"));
        Assert.True(Value<bool>(bindings, "CanSaveDraft"));
        Assert.True(bindings.IsActionAvailable("assistants.draft.save"));
        Assert.False(bindings.ClearSubmittedPrompt("An earlier draft"));
        Assert.Equal("Use this original draft", bindings.Prompt);
        Assert.True(bindings.ClearSubmittedPrompt("Use this original draft"));
        Assert.False(Value<bool>(bindings, "CanSend"));
        Assert.Equal("Write a message to begin.", Value<string>(bindings, "ComposerAvailability"));
    }

    [Fact]
    public void Unstarted_Task_text_updates_Start_without_enabling_Chat_or_follow_up_controls()
    {
        var (bindings, _, _) = Create(ConversationKind.Task);
        Assert.True(bindings.TrySetValue("Prompt", ""));
        Assert.False(Value<bool>(bindings, "CanStartTask"));
        Assert.False(bindings.IsActionAvailable("assistants.task.start"));
        Assert.True(bindings.TrySetValue("Prompt", "Review this saved project"));
        Assert.True(Value<bool>(bindings, "CanStartTask"));
        Assert.True(bindings.IsActionAvailable("assistants.task.start"));
        Assert.False(Value<bool>(bindings, "CanSend"));
        Assert.False(Value<bool>(bindings, "CanSteer"));
        Assert.False(bindings.IsActionAvailable("assistants.task.queue"));
        Assert.True(Value<bool>(bindings, "CanSaveDraft"));
    }

    [Theory]
    [InlineData(TaskExecutionLifecycle.Running)]
    [InlineData(TaskExecutionLifecycle.WaitingSafeBoundary)]
    public void Active_Task_text_updates_both_Steer_and_Queue_without_starting_another_Task(TaskExecutionLifecycle state)
    {
        var (bindings, _, _) = Create(ConversationKind.Task, state);
        foreach (var prompt in new[] { "", "First review the introduction", " \t" })
        {
            Assert.True(bindings.TrySetValue("Prompt", prompt));
            var allowed = !string.IsNullOrWhiteSpace(prompt);
            Assert.Equal(allowed, Value<bool>(bindings, "CanSteer"));
            Assert.Equal(allowed, bindings.IsActionAvailable("assistants.task.steer"));
            Assert.Equal(allowed, bindings.IsActionAvailable("assistants.task.queue"));
            Assert.False(Value<bool>(bindings, "CanStartTask"));
            Assert.False(Value<bool>(bindings, "CanSend"));
            Assert.True(Value<bool>(bindings, "CanSaveDraft"));
        }
    }

    [Theory]
    [InlineData("missing-model", true, "choose an authorized model")]
    [InlineData("loading", false, "Wait for the current operation")]
    [InlineData("saving", false, "Wait for the current operation")]
    [InlineData("streaming", true, "Your Assistant is responding.")]
    public void Typing_preserves_model_busy_and_streaming_action_gates(string gate, bool canSave, string notice)
    {
        var (bindings, snapshot, _) = Create();
        bindings.ApplySnapshot(snapshot with
        {
            Revision = snapshot.Revision + 1,
            Models = gate == "missing-model" ? [] : snapshot.Models,
            IsLoading = gate == "loading", IsSaving = gate == "saving"
        });
        if (gate == "streaming") bindings.SetStreaming(true);
        Assert.True(bindings.TrySetValue("Prompt", "The next draft remains editable"));
        Assert.False(Value<bool>(bindings, "CanSend"));
        Assert.False(bindings.IsActionAvailable("assistants.send"));
        Assert.False(Value<bool>(bindings, "CanStartTask"));
        Assert.Equal(canSave, Value<bool>(bindings, "CanSaveDraft"));
        Assert.Equal(canSave, bindings.IsActionAvailable("assistants.draft.save"));
        Assert.Contains(notice, Value<string>(bindings, "ComposerAvailability"));
    }

    [Theory]
    [InlineData(false, false, "Enable Assistant")]
    [InlineData(true, true, "Restore Assistant")]
    [InlineData(false, true, "Restore Assistant")]
    public void Typing_preserves_the_identity_owners_final_disabled_or_archived_guidance(bool enabled, bool archived, string action)
    {
        var (bindings, _, _) = Create(enabled: enabled, archived: archived);
        var originalNotice = Value<string>(bindings, "ComposerAvailability");
        Assert.Contains(action, originalNotice);
        foreach (var prompt in new[] { "Another draft", "", "Saved for later" })
        {
            Assert.True(bindings.TrySetValue("Prompt", prompt));
            Assert.Equal(originalNotice, Value<string>(bindings, "ComposerAvailability"));
            Assert.False(Value<bool>(bindings, "CanSend"));
            Assert.False(bindings.IsActionAvailable("assistants.send"));
            Assert.True(bindings.IsActionAvailable("assistants.draft.save"));
        }
    }

    [Theory]
    [InlineData("retiring")]
    [InlineData("not-current")]
    [InlineData("revoked")]
    [InlineData("branch-busy")]
    public void Rejected_input_does_not_mutate_the_original_prompt_or_notify(string gate)
    {
        var current = true;
        var (bindings, snapshot, _) = Create(isCurrent: () => current);
        if (gate == "retiring") bindings.ApplySnapshot(snapshot with { Revision = 2, IsRetiring = true });
        if (gate == "not-current") current = false;
        if (gate == "revoked") bindings.Revoke();
        if (gate == "branch-busy") bindings.SetConversationBranchBusy(true);
        var before = bindings.OriginalPrompt; var changed = 0;
        bindings.PromptChanged += () => changed++;
        Assert.False(bindings.TrySetValue("Prompt", "Must not replace the saved input"));
        Assert.Equal(before, bindings.OriginalPrompt);
        Assert.Equal(0, changed);
    }

    [Fact]
    public async Task Typing_retains_current_row_actions_but_a_new_snapshot_still_refuses_old_rows()
    {
        var (bindings, snapshot, calls) = Create();
        var row = Value<AssistantsCuiBindings.AssistantRow[]>(bindings, "Assistants")[0];
        Assert.True(bindings.TrySetValue("Prompt", "A changed draft"));
        Assert.True(bindings.TryGetItemValue(row, "Name", out _));
        await bindings.DispatchAsync("assistants.open", row);
        Assert.Same(row, Assert.Single(calls).Parameter);

        bindings.ApplySnapshot(snapshot with { Revision = 2 });
        Assert.False(bindings.TryGetItemValue(row, "Name", out _));
        await bindings.DispatchAsync("assistants.open", row);
        Assert.Single(calls);
        var fresh = Value<AssistantsCuiBindings.AssistantRow[]>(bindings, "Assistants")[0];
        Assert.NotSame(row, fresh);
        await bindings.DispatchAsync("assistants.open", fresh);
        Assert.Equal(2, calls.Count);
        Assert.Same(fresh, calls[1].Parameter);
        Assert.Equal("A changed draft", bindings.Prompt);
    }

    private static T Value<T>(AssistantsCuiBindings bindings, string name)
    {
        Assert.True(bindings.TryGetValue(name, out var value));
        return Assert.IsAssignableFrom<T>(value);
    }

    private static (AssistantsCuiBindings Bindings, AssistantsWorkspaceSnapshot Snapshot,
        List<(string Command, object? Parameter)> Calls) Create(ConversationKind kind = ConversationKind.Chat,
        TaskExecutionLifecycle? state = null, bool enabled = true, bool archived = false, Func<bool>? isCurrent = null)
    {
        var now = DateTimeOffset.UnixEpoch;
        var definition = new AssistantDefinitionSnapshot(new("display-den", "personal", "display-assistant"), 1,
            ConfiguredIdentityKind.Assistant, new() { Name = "Display fixture", Enabled = enabled,
                Archived = archived, KnowledgeResourceIds = ["display-reference"] }, []);
        var conversation = new Conversation(Guid.NewGuid(), HavenMode.Chat, kind, "Display conversation",
            null, null, false, false, now, now);
        var attachment = new MessageAttachment(Guid.NewGuid(), conversation.Id, null, null, "current.txt", "", "text/plain",
            MessageAttachmentKind.PlainText, 4, new string('a', 64), AttachmentProcessingState.Ready,
            AttachmentAnalysisMethod.TextExtracted, "text", "{}", now, now);
        TaskExecutionSnapshot? task = state is { } actualState ? new(Guid.NewGuid(), conversation.Id, Guid.NewGuid(),
            "Current saved task", actualState, TaskExecutionDurability.PersistedPlan, 1, [], [], [], [], null, now, now) : null;
        var data = new AssistantConversationData(conversation, [], [attachment], [],
            new(conversation.Id, null, "Saved draft", JsonSerializer.Serialize(new[] { attachment.Id }), now), task);
        // These typed observations exercise presentation gates only. The recorder below never
        // substitutes for a real controller, Home authorization, model invocation or store write.
        var binding = new AssistantConversationBinding(new object(), definition, "display-session", 1, conversation);
        var model = new AssistantModelChoice(new("fixture:model", 1, "family", "parameters", "quantization",
            new HashSet<ToolCapability> { ToolCapability.Text }, now), "fixture");
        var snapshot = AssistantsWorkspaceSnapshot.Empty with
        {
            Revision = 1, SelectedAssistant = definition,
            Assistants = Enumerable.Range(0, 6).Select(index => index == 0 ? definition : definition with
                { Identity = definition.Identity with { DefinitionId = "display-" + index },
                    Configuration = definition.Configuration with { Name = "Display " + index } }).ToArray(),
            Conversations = Enumerable.Range(0, 12).Select(index => new AssistantConversationSummary(
                index == 0 ? conversation.Id : Guid.NewGuid(), "Conversation " + index, now.AddMinutes(index), false, null)
                { IsOrdinarySecondary = true }).ToArray(),
            ConversationBinding = binding, Conversation = data, Models = [model],
            Work = task is null ? null : new(task, new(new(task.TaskId, task.ContextId, task.ExecutionId, null, 1), true, true, false), [])
        };
        var calls = new List<(string Command, object? Parameter)>();
        var bindings = new AssistantsCuiBindings((command, parameter, _) =>
            { calls.Add((command, parameter)); return ValueTask.CompletedTask; }, body => body(), isCurrent ?? (() => true));
        bindings.ShowWork(); bindings.ApplySnapshot(snapshot);
        return (bindings, snapshot, calls);
    }
}
