using Haven.Application;
using Haven.Core;

namespace Haven.Infrastructure.Tests;

public sealed partial class ConversationProductionRepositoryTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task PendingAttachmentRemovalAtomicallyChecksClaimOwnerAndCurrentBranch(int wrapperDepth)
    {
        var database = await CreateDatabaseAsync();
        var conversations = new ConversationRepository(database);
        var production = CreateProduction(database, conversations);
        var raw = new MessageAttachmentService(_paths, production, new LocalMediaToolLocator());
        var safe = new Haven.Infrastructure.SafeMessageAttachmentService(raw, _paths);
        IMessageAttachmentService service = wrapperDepth switch
        {
            0 => raw,
            1 => safe,
            _ => new OdsAwareMessageAttachmentService(safe, _paths, database, new UnexpectedRemovalRetrieval())
        };
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
        Assert.False(await service.TryDeletePendingAsync(attachment.Id, other.Id, otherBranch.Id, default));
        Assert.False(await service.TryDeletePendingAsync(attachment.Id, conversation.Id, otherBranch.Id, default));
        // This is the stale snapshot held by a composer before a concurrent send.
        var stale = Assert.Single(await production.GetAttachmentsAsync(conversation.Id, null, default));
        Assert.Null(stale.MessageId);
        var committed = new ChatMessage(Guid.NewGuid(), conversation.Id, MessageRole.User, "Sent", null, null, null, now);
        await conversations.AddMessageAsync(committed, default);
        await production.UpsertAttachmentAsync(attachment with { MessageId = committed.Id }, default);
        Assert.False(await service.TryDeletePendingAsync(stale.Id, stale.ConversationId, branch.Id, default));
        Assert.Equal(committed.Id, Assert.Single(await production.GetAttachmentsAsync(conversation.Id, null, default)).MessageId);
        var scopedPending = attachment with { Id = Guid.NewGuid() };
        await production.UpsertAttachmentAsync(scopedPending, default);
        var pending = attachment with { Id = Guid.NewGuid(), BranchId = null };
        await production.UpsertAttachmentAsync(pending, default);
        var next = await production.CreateBranchAsync(conversation.Id, branch.Id, null, "Other branch", ConversationBranchReason.Manual, default);
        await production.SetCurrentBranchAsync(conversation.Id, next.Id, default);
        Assert.False(await service.TryDeletePendingAsync(scopedPending.Id, conversation.Id, next.Id, default));
        Assert.False(await service.TryDeletePendingAsync(pending.Id, conversation.Id, branch.Id, default));
        Assert.True(await service.TryDeletePendingAsync(pending.Id, conversation.Id, next.Id, default));
        Assert.False(await service.TryDeletePendingAsync(pending.Id, conversation.Id, next.Id, default));
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.TryDeletePendingAsync(scopedPending.Id, conversation.Id, next.Id, cancelled.Token));
        var remaining = await production.GetAttachmentsAsync(conversation.Id, null, default);
        Assert.Equal(2, remaining.Count);
        Assert.Equal(committed.Id, Assert.Single(remaining, item => item.Id == attachment.Id).MessageId);
        Assert.Null(Assert.Single(remaining, item => item.Id == scopedPending.Id).MessageId);
    }

    private sealed class UnexpectedRemovalRetrieval : IRetrievalIndexService
    {
        public Task<RetrievalDocument> IndexTextAsync(RetrievalScope scope, string sourceType,
            string sourceId, string title, string text, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Pending removal must not index content.");
        public Task RemoveSourceAsync(RetrievalScope scope, string sourceType, string sourceId,
            CancellationToken cancellationToken) => throw new InvalidOperationException("Unexpected retrieval mutation.");
        public Task<IReadOnlyList<RetrievalDocument>> GetDocumentsAsync(RetrievalScope scope,
            CancellationToken cancellationToken) => throw new InvalidOperationException("Unexpected retrieval access.");
    }

}
