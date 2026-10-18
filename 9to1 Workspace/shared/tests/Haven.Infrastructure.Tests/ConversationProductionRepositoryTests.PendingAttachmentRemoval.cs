using Haven.Application;
using Haven.Core;

namespace Haven.Infrastructure.Tests;

public sealed partial class ConversationProductionRepositoryTests
{
    [Fact]
    public async Task PendingAttachmentRemovalAtomicallyChecksClaimOwnerAndCurrentBranch()
    {
        var database = await CreateDatabaseAsync();
        var conversations = new ConversationRepository(database);
        var production = CreateProduction(database, conversations);
        var now = DateTimeOffset.UtcNow;
        var conversation = ConversationAt(now);
        var other = ConversationAt(now);
        await conversations.UpsertConversationAsync(conversation, default);
        await conversations.UpsertConversationAsync(other, default);
        var branch = await production.EnsureRootBranchAsync(conversation.Id, default);
        var otherBranch = await production.EnsureRootBranchAsync(other.Id, default);
        var attachment = new MessageAttachment(Guid.NewGuid(), conversation.Id, null, branch.Id,
            "pending.txt", "pending.txt", "text/plain", MessageAttachmentKind.PlainText,
            1, "hash", AttachmentProcessingState.Ready, AttachmentAnalysisMethod.TextExtracted,
            "draft", "{}", now, now);
        await production.UpsertAttachmentAsync(attachment, default);
        Assert.False(await production.TryDeletePendingAttachmentAsync(attachment.Id, other.Id, otherBranch.Id, default));
        Assert.False(await production.TryDeletePendingAttachmentAsync(attachment.Id, conversation.Id, otherBranch.Id, default));
        // This is the stale snapshot held by a composer before a concurrent send.
        var stale = Assert.Single(await production.GetAttachmentsAsync(conversation.Id, null, default));
        Assert.Null(stale.MessageId);
        var committed = new ChatMessage(Guid.NewGuid(), conversation.Id, MessageRole.User, "Sent", null, null, null, now);
        await conversations.AddMessageAsync(committed, default);
        await production.UpsertAttachmentAsync(attachment with { MessageId = committed.Id }, default);
        Assert.False(await production.TryDeletePendingAttachmentAsync(stale.Id, stale.ConversationId, branch.Id, default));
        Assert.Equal(committed.Id, Assert.Single(await production.GetAttachmentsAsync(conversation.Id, null, default)).MessageId);
        var scopedPending = attachment with { Id = Guid.NewGuid() };
        await production.UpsertAttachmentAsync(scopedPending, default);
        var pending = attachment with { Id = Guid.NewGuid(), BranchId = null };
        await production.UpsertAttachmentAsync(pending, default);
        var next = await production.CreateBranchAsync(conversation.Id, branch.Id, null, "Other branch", ConversationBranchReason.Manual, default);
        await production.SetCurrentBranchAsync(conversation.Id, next.Id, default);
        Assert.False(await production.TryDeletePendingAttachmentAsync(scopedPending.Id, conversation.Id, next.Id, default));
        Assert.False(await production.TryDeletePendingAttachmentAsync(pending.Id, conversation.Id, branch.Id, default));
        Assert.True(await production.TryDeletePendingAttachmentAsync(pending.Id, conversation.Id, next.Id, default));
        Assert.False(await production.TryDeletePendingAttachmentAsync(pending.Id, conversation.Id, next.Id, default));
        var remaining = await production.GetAttachmentsAsync(conversation.Id, null, default);
        Assert.Equal(2, remaining.Count);
        Assert.Equal(committed.Id, Assert.Single(remaining, item => item.Id == attachment.Id).MessageId);
        Assert.Null(Assert.Single(remaining, item => item.Id == scopedPending.Id).MessageId);
    }
}
