#if !ANDROID
using System.Text.Json;
using Avalonia.Headless.XUnit;
using Haven.Core;
using HavenOS.Apps.Assistants.Contracts;
using Xunit;

namespace HavenOS.Apps.Assistants.Memory.Tests;

public sealed partial class AssistantOriginalMemoryTests
{
    [AvaloniaFact]
    public Task Disabled_Assistant_reopens_with_visible_enable_and_keeps_identity_history_and_saved_draft() => RunAsync(async rig =>
    {
        var original = await rig.CreateAsync(new AssistantConfiguration { Name = "Fictional recoverable Assistant", Instructions = "Preserve my saved preferences." });
        var message = new ChatMessage(Guid.NewGuid(), original.Conversation.Id, MessageRole.User,
            "Fictional retained user history.", null, null, null, DateTimeOffset.UtcNow);
        var write = rig.OriginalConversations.AddMessageAsync(message, Token); rig.Retain(write); await write;
        const string draft = "Continue this saved draft later.";
        long disabledRevision = 0;
        await WithActualProactivitySurface(rig, async (window, surface, controller) =>
        {
            await OpenActualSavedMemoryConversation(surface, original.Definition.Identity, original.Conversation.Id, window);
            Assert.True(surface.Bindings.TrySetValue("Prompt", draft));
            await ClickCanonicalCaptureControl(window, surface, "assistant-disable", () =>
                controller.Snapshot.SelectedAssistant?.Configuration.Enabled == false && surface.IsOriginalClosePrepared);
            var selected = controller.Snapshot.SelectedAssistant!; disabledRevision = selected.Revision;
            Assert.True(disabledRevision > original.Definition.Revision);
            AssertAvailabilityConfiguration(original.Definition.Configuration with { Enabled = false }, selected.Configuration);
            Assert.True(OriginalComposerButton(window, "assistant-enable").IsEnabled);
            Assert.False(surface.Bindings.IsActionAvailable("assistants.conversation.new") == true);
            Assert.False(surface.Bindings.IsActionAvailable("assistants.send") == true);
            var data = await rig.Bridge.ReadConversationAsync(controller.Snapshot.ConversationBinding!, Token);
            Assert.Equal(message, Assert.Single(data.Messages)); Assert.Equal(draft, data.Draft!.Content);
            Assert.Null(data.CanonicalTask); Assert.Single(controller.Snapshot.Conversations);
        });
        await rig.ReopenAsync();
        await WithActualProactivitySurface(rig, async (window, surface, controller) =>
        {
            await OpenActualSavedMemoryConversation(surface, original.Definition.Identity, original.Conversation.Id, window);
            Assert.False(controller.Snapshot.SelectedAssistant!.Configuration.Enabled);
            Assert.Equal(draft, surface.Bindings.Prompt);
            await ClickCanonicalCaptureControl(window, surface, "assistant-enable", () =>
                controller.Snapshot.SelectedAssistant?.Configuration.Enabled == true && surface.IsOriginalClosePrepared);
            var selected = controller.Snapshot.SelectedAssistant!;
            Assert.Equal(original.Definition.Identity, selected.Identity); Assert.True(selected.Revision > disabledRevision);
            AssertAvailabilityConfiguration(original.Definition.Configuration, selected.Configuration);
            Assert.Equal(original.Conversation.Id, controller.Snapshot.ConversationBinding!.Conversation.Id);
            Assert.True(surface.Bindings.IsActionAvailable("assistants.conversation.new") == true);
            Assert.True(OriginalComposerButton(window, "assistant-disable").IsEnabled);
            Assert.Empty(controller.Snapshot.Models); // Enable does not manufacture a model grant.
            var data = await rig.Bridge.ReadConversationAsync(controller.Snapshot.ConversationBinding!, Token);
            Assert.Equal(message, Assert.Single(data.Messages)); Assert.Equal(draft, data.Draft!.Content);
            Assert.Null(data.CanonicalTask); Assert.Single(controller.Snapshot.Conversations);
        });
    }, importMemory: false);

    [AvaloniaFact]
    public Task Archived_Assistant_restores_after_reopen_without_enabling_it_or_replacing_unsaved_configuration() => RunAsync(async rig =>
    {
        var original = await rig.CreateAsync(new AssistantConfiguration { Name = "Fictional archived Assistant", Description = "Keep this description." });
        await WithActualProactivitySurface(rig, async (window, surface, controller) =>
        {
            await OpenActualSavedMemoryConversation(surface, original.Definition.Identity, original.Conversation.Id, window);
            await surface.Bindings.DispatchAsync("assistants.configuration.open", null, Token);
            Assert.True(surface.Bindings.TrySetValue("DraftDescription", "Unsaved description stays in the form."));
            Assert.False(surface.Bindings.IsActionAvailable("assistants.disable") == true);
            Assert.False(surface.Bindings.IsActionAvailable("assistants.archive") == true);
            Assert.Equal(original.Definition.Configuration.Description, controller.Snapshot.SelectedAssistant!.Configuration.Description);
            Assert.True(surface.Bindings.Draft!.IsDirty);
            await surface.Bindings.DispatchAsync("assistants.configuration.discard", null, Token);
            await surface.Bindings.DispatchAsync("assistants.configuration.back", null, Token);
            await ClickCanonicalCaptureControl(window, surface, "assistant-disable", () =>
                controller.Snapshot.SelectedAssistant?.Configuration.Enabled == false && surface.IsOriginalClosePrepared);
            await ClickCanonicalCaptureControl(window, surface, "assistant-archive", () =>
                controller.Snapshot.SelectedAssistant?.Configuration.Archived == true && surface.IsOriginalClosePrepared);
            Assert.True(OriginalComposerButton(window, "assistant-restore").IsEnabled);
            Assert.False(surface.Bindings.IsActionAvailable("assistants.enable") == true);
            Assert.False(surface.Bindings.IsActionAvailable("assistants.task.new") == true);
            Assert.Single(controller.Snapshot.Conversations);
        });
        await rig.ReopenAsync();
        await WithActualProactivitySurface(rig, async (window, surface, controller) =>
        {
            await OpenActualSavedMemoryConversation(surface, original.Definition.Identity, original.Conversation.Id, window);
            Assert.True(controller.Snapshot.SelectedAssistant!.Configuration.Archived);
            await ClickCanonicalCaptureControl(window, surface, "assistant-restore", () =>
                controller.Snapshot.SelectedAssistant?.Configuration.Archived == false && surface.IsOriginalClosePrepared);
            var selected = controller.Snapshot.SelectedAssistant!;
            Assert.False(selected.Configuration.Enabled);
            Assert.Equal(original.Definition.Identity, selected.Identity);
            AssertAvailabilityConfiguration(original.Definition.Configuration with { Enabled = false }, selected.Configuration);
            Assert.Equal(original.Conversation.Id, controller.Snapshot.ConversationBinding!.Conversation.Id);
            Assert.True(OriginalComposerButton(window, "assistant-enable").IsEnabled);
            Assert.False(surface.Bindings.IsActionAvailable("assistants.send") == true);
            Assert.Null(controller.Snapshot.Conversation!.CanonicalTask);
            Assert.Single(controller.Snapshot.Conversations);
        });
    }, importMemory: false);
    private static void AssertAvailabilityConfiguration(AssistantConfiguration expected, AssistantConfiguration actual) =>
        Assert.Equal(JsonSerializer.Serialize(expected), JsonSerializer.Serialize(actual));
}
#endif
