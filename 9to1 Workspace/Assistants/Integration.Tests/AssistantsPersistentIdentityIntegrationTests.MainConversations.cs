using HavenOS.Apps.Assistants.Contracts;
using NineToOne.Dulche.Den;
using Xunit;

namespace HavenOS.Apps.Assistants.Tests;

public sealed partial class AssistantsPersistentIdentityIntegrationTests
{
    [Fact]
    public Task Main_uses_same_canonical_identity_across_secondary_thread_navigation_and_real_owner_reopen() =>
        RunOriginalAsync(async rig =>
        {
            var first = rig.CreateController();
            var created = await rig.Retain(first.CreateAsync(new() { Name = "Main owner" }, Guid.NewGuid(), Token));
            var identity = created.SelectedAssistant!.Identity;
            var opened = await rig.Retain(first.OpenAssistantAsync(identity, Token));
            var main = Assert.Single(opened.Conversations);
            Assert.True(main.IsMain); Assert.Equal(main.ConversationId, opened.ConversationBinding!.Conversation.Id);
            await rig.Retain(first.SaveDraftAsync(null, "PERSIST MAIN DRAFT", [], Token));
            var secondaryId = Guid.NewGuid();
            await rig.Retain(first.NewConversationAsync(secondaryId, "Main", Guid.NewGuid(), Token)); // A name grants no Main role.
            await rig.Retain(first.SaveDraftAsync(null, "PERSIST SECONDARY DRAFT", [], Token));
            Assert.False(Assert.Single(first.Snapshot.Conversations.Where(row => row.ConversationId == secondaryId)).IsMain);
            await rig.ReopenActualOwnersAsync();
            var second = rig.CreateController();
            var restored = await rig.Retain(second.OpenAssistantAsync(identity, Token));
            Assert.Equal(main.ConversationId, restored.ConversationBinding!.Conversation.Id);
            Assert.Equal("PERSIST MAIN DRAFT", restored.Conversation!.Draft!.Content);
            Assert.Equal(2, restored.Conversations.Count);
            Assert.Equal(main.ConversationId, Assert.Single(restored.Conversations.Where(row => row.IsMain)).ConversationId);
            var secondary = await rig.Retain(second.OpenConversationAsync(secondaryId, Token));
            Assert.Equal("PERSIST SECONDARY DRAFT", secondary.Conversation!.Draft!.Content);
            var back = await rig.Retain(second.OpenAssistantAsync(identity, Token));
            Assert.Equal(main.ConversationId, back.ConversationBinding!.Conversation.Id);
            Assert.Equal(identity, back.ConversationBinding.Definition.Identity); Assert.Equal(0, rig.ProviderInvocations);
        });

    [Fact]
    public Task Repeated_Main_open_and_definition_rename_never_create_duplicate_membership_or_replace_history() =>
        RunOriginalAsync(async rig =>
        {
            var controller = rig.CreateController();
            var created = await rig.Retain(controller.CreateAsync(new() { Name = "Rename helper" }, Guid.NewGuid(), Token));
            var identity = created.SelectedAssistant!.Identity;
            var first = await rig.Retain(controller.OpenAssistantAsync(identity, Token));
            var id = first.ConversationBinding!.Conversation.Id;
            await rig.Retain(controller.SaveDraftAsync(null, "ACKNOWLEDGED MAIN DRAFT", [], Token));
            var configured = await rig.Retain(controller.ConfigureAsync(identity, first.SelectedAssistant!.Revision,
                first.SelectedAssistant.Configuration with { Name = "Renamed helper", Instructions = "New saved instructions" }, Guid.NewGuid(), Token));
            Assert.True(configured.SelectedAssistant!.Revision > first.SelectedAssistant.Revision);
            for (var index = 0; index < 3; index++)
            {
                var next = await rig.Retain(controller.OpenAssistantAsync(identity, Token));
                Assert.Equal(id, next.ConversationBinding!.Conversation.Id);
                Assert.Equal("ACKNOWLEDGED MAIN DRAFT", next.Conversation!.Draft!.Content);
                Assert.True(Assert.Single(next.Conversations).IsMain);
            }
            var home = await rig.Retain(rig.Factory.OpenAsync(Token));
            var session = Assert.Single(await rig.Retain(home.Den.ListAsync<SessionRecord>(identity.NamespaceId, Token)));
            Assert.Equal(id.ToString("D"), session.ConversationId);
            Assert.NotNull(await rig.Retain(rig.Conversations.GetAsync(id, Token))); Assert.Equal(0, rig.ProviderInvocations);
        });

    [Fact]
    public Task Protected_Main_removal_guard_refuses_before_any_real_store_write() =>
        RunOriginalAsync(async rig =>
        {
            var controller = rig.CreateController();
            var created = await rig.Retain(controller.CreateAsync(new() { Name = "Protected helper" }, Guid.NewGuid(), Token));
            var identity = created.SelectedAssistant!.Identity;
            var opened = await rig.Retain(controller.OpenAssistantAsync(identity, Token));
            var mainId = opened.ConversationBinding!.Conversation.Id;
            await rig.Retain(controller.SaveDraftAsync(null, "DO NOT LOSE THIS MAIN", [], Token));
            var refusalTask = rig.Retain(controller.VerifyConversationRemovalAllowedAsync(mainId, Token));
            var refusal = await Assert.ThrowsAsync<AssistantCommandRefusedException>(() => refusalTask);
            Assert.Contains("protected Main", refusal.Message, StringComparison.Ordinal);
            Assert.True(controller.IsAcknowledgedOriginalCommandRefusal(refusalTask)); rig.Expect(refusalTask, refusal);
            Assert.NotNull(await rig.Retain(rig.Conversations.GetAsync(mainId, Token)));
            var reopened = await rig.Retain(controller.OpenAssistantAsync(identity, Token));
            Assert.Equal(mainId, reopened.ConversationBinding!.Conversation.Id);
            Assert.Equal("DO NOT LOSE THIS MAIN", reopened.Conversation!.Draft!.Content);
            Assert.True(Assert.Single(reopened.Conversations).IsMain); Assert.Equal(0, rig.ProviderInvocations);
        });

    [Fact]
    public Task Ordinary_new_thread_cannot_reuse_reserved_Main_identity_or_adopt_its_membership() =>
        RunOriginalAsync(async rig =>
        {
            var controller = rig.CreateController();
            var created = await rig.Retain(controller.CreateAsync(new() { Name = "Reserved helper" }, Guid.NewGuid(), Token));
            var identity = created.SelectedAssistant!.Identity;
            var opened = await rig.Retain(controller.OpenAssistantAsync(identity, Token));
            var mainId = opened.ConversationBinding!.Conversation.Id;
            var rejected = rig.Retain(controller.NewConversationAsync(mainId, "Replacement", Guid.NewGuid(), Token));
            var refusal = await Assert.ThrowsAsync<AssistantCommandRefusedException>(() => rejected);
            Assert.Contains("reserved", refusal.Message, StringComparison.Ordinal);
            Assert.True(controller.IsAcknowledgedOriginalCommandRefusal(rejected)); rig.Expect(rejected, refusal);
            var retained = await rig.Retain(controller.OpenAssistantAsync(identity, Token));
            Assert.Equal(mainId, retained.ConversationBinding!.Conversation.Id);
            Assert.Equal("Main", retained.Conversation.Conversation.Title);
            Assert.True(Assert.Single(retained.Conversations).IsMain);
        });
}
