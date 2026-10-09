using System.Text.Json;
using Haven.Core;
using HavenOS.Apps.Assistants.Contracts;
using HavenOS.Apps.Assistants.Core;
using Xunit;
namespace HavenOS.Apps.Assistants.NativeUI.Tests;

public sealed class AssistantsDraftAttachmentProjectionTests
{
    [Fact]
    public void Detached_retained_records_are_not_restored_to_the_draft_or_send_selection()
    {
        var data = Data(out var kept, out var detached);
        Assert.Equal(new[] { kept.Id }, AssistantDraftAttachmentProjection.DemandIds(data));
        var detachedDraft = data with { Draft = data.Draft! with { AttachmentIdsJson = "[]" } };
        Assert.Empty(AssistantDraftAttachmentProjection.DemandIds(detachedDraft));
        Assert.Contains(detached, detachedDraft.Attachments); Assert.Contains(kept, detachedDraft.Attachments);
        Assert.Empty(AssistantDraftAttachmentProjection.DemandIds(data with { Draft = null }));
    }
    [Fact]
    public void Corrupt_duplicate_or_foreign_draft_references_do_not_become_selection()
    {
        var data = Data(out var kept, out _);
        foreach (var ids in new[] { "[", JsonSerializer.Serialize(new[] { kept.Id, kept.Id }), JsonSerializer.Serialize(new[] { Guid.NewGuid() }) })
        {
            var invalid = data with { Draft = data.Draft! with { AttachmentIdsJson = ids } };
            Assert.False(AssistantDraftAttachmentProjection.TryRead(invalid, out _));
            Assert.Throws<InvalidDataException>(() => AssistantDraftAttachmentProjection.DemandIds(invalid));
        }
    }
    [Fact]
    public async Task Only_the_current_typed_attachment_row_dispatches_remove()
    {
        var calls = new List<object?>();
        var bindings = new AssistantsCuiBindings((_, row, _) => { calls.Add(row); return ValueTask.CompletedTask; }, body => body(), () => true);
        var data = Data(out var kept, out _); var definition = new AssistantDefinitionSnapshot(new("display-den", "personal", "display-assistant"), 1,
            ConfiguredIdentityKind.Assistant, new() { Name = "Display only" }, []);
        var binding = new AssistantConversationBinding(new object(), definition, "display-session", 1, data.Conversation);
        var snapshot = AssistantsWorkspaceSnapshot.Empty with { Revision = 1, SelectedAssistant = definition, ConversationBinding = binding, Conversation = data };
        bindings.ApplySnapshot(snapshot); Assert.True(bindings.TryGetValue("DraftAttachments", out var projected));
        var original = Assert.Single(Assert.IsAssignableFrom<IReadOnlyList<AssistantsCuiBindings.AttachmentRow>>(projected));
        Assert.True(bindings.TryGetItemValue(original, "Name", out var name)); Assert.Equal(kept.OriginalName, name);
        await bindings.DispatchAsync("assistants.attachment.detach", new AssistantsCuiBindings.AttachmentRow(kept)); Assert.Empty(calls);
        await bindings.DispatchAsync("assistants.attachment.detach", original); Assert.Same(original, Assert.Single(calls));
        bindings.ApplySnapshot(snapshot with { Revision = 2 });
        Assert.False(bindings.TryGetItemValue(original, "Name", out _));
        await bindings.DispatchAsync("assistants.attachment.detach", original); Assert.Single(calls);
    }
    private static AssistantConversationData Data(out MessageAttachment kept, out MessageAttachment detached)
    {
        var now = DateTimeOffset.UtcNow; var id = Guid.NewGuid();
        var conversation = new Conversation(id, HavenMode.Chat, ConversationKind.Chat, "Display only", null, null, false, false, now, now);
        kept = new(Guid.NewGuid(), id, null, null, "kept.txt", "", "text/plain", MessageAttachmentKind.PlainText,
            4, new string('a', 64), AttachmentProcessingState.Ready, AttachmentAnalysisMethod.TextExtracted, "kept", "{}", now, now);
        detached = kept with { Id = Guid.NewGuid(), OriginalName = "detached.txt" };
        return new(conversation, [], [kept, detached], [], new(id, null, "draft", JsonSerializer.Serialize(new[] { kept.Id }), now), null);
    }
}
