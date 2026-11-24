using System.Text.Json;
using Haven.Application;
using Haven.Core;
using Haven.Infrastructure;
using Microsoft.Data.Sqlite;

namespace Haven.Infrastructure.Tests;

public sealed partial class ConversationProductionRepositoryTests
{
    [Fact]
    public async Task TemporaryChatSavePublishesOriginalMessagesVersionsTurnAndDraftTogetherWithoutUsageReplay()
    {
        var database = await CreateDatabaseAsync();
        var conversations = new ConversationRepository(database);
        var production = CreateProduction(database, conversations);
        // Exercise the exact adapter that production resolves. Saving old history cannot
        // touch any of the dependencies used by model-usage tracking.
        ITemporaryChatSaveRepository storage = new UsageTrackingConversationRepository(conversations, null!, null!, null!, null!);
        var request = StartedTemporaryChatSave();

        var receipt = await storage.SaveTemporaryChatAsync(request, default);

        Assert.Equal(request.Original.Id, receipt.Conversation.Id);
        Assert.False(receipt.Conversation.IsTemporary);
        Assert.Equal(receipt.Conversation, await conversations.GetAsync(request.Original.Id, default));
        Assert.Equal(request.History.ToArray(), (await conversations.GetMessagesAsync(request.Original.Id, default)).ToArray());
        Assert.Equal(receipt.BranchId, Assert.Single(await production.GetBranchesAsync(request.Original.Id, default)).Id);
        var turn = Assert.Single(await production.GetTurnsAsync(request.Original.Id, receipt.BranchId, default));
        Assert.Equal(request.History[0].Id, turn.UserMessageId);
        Assert.Equal(request.History[1].Id, turn.AssistantMessageId);
        foreach (var message in request.History)
            Assert.Equal(message.Content, Assert.Single(await production.GetVersionsAsync(message.Id, receipt.BranchId, default)).Content);
        Assert.Equal(request.DraftText, (await production.GetDraftAsync(request.Original.Id, receipt.BranchId, default))?.Content);
        Assert.True(request.Original.IsTemporary);
    }

    [Fact]
    public async Task TemporaryChatSaveRollsBackAllWritesWhenSecondMessageCollidesWithForeignChat()
    {
        var database = await CreateDatabaseAsync();
        var conversations = new ConversationRepository(database);
        var production = CreateProduction(database, conversations);
        var request = StartedTemporaryChatSave();
        var other = ConversationAt(DateTimeOffset.UtcNow);
        var foreign = request.History[1] with { ConversationId = other.Id, Content = "other chat" };
        await conversations.UpsertConversationAsync(other, default);
        await conversations.AddMessageAsync(foreign, default);

        await Assert.ThrowsAsync<SqliteException>(() => conversations.SaveTemporaryChatAsync(request, default));

        Assert.Null(await conversations.GetAsync(request.Original.Id, default));
        Assert.Empty(await conversations.GetMessagesAsync(request.Original.Id, default));
        Assert.Empty(await production.GetBranchesAsync(request.Original.Id, default));
        Assert.Equal(foreign, Assert.Single(await conversations.GetMessagesAsync(other.Id, default)));
        // The first ID would collide with the failed attempt if its earlier insert escaped.
        var retry = request with { History = [request.History[0], request.History[1] with { Id = Guid.NewGuid() }] };
        await conversations.SaveTemporaryChatAsync(retry, default);
        Assert.Equal(retry.History.ToArray(), (await conversations.GetMessagesAsync(request.Original.Id, default)).ToArray());
    }

    [Fact]
    public async Task TemporaryChatSaveReusesOnlyOwnedPlaceholderBranchDraftAndPendingAttachment()
    {
        var database = await CreateDatabaseAsync();
        var conversations = new ConversationRepository(database);
        var production = CreateProduction(database, conversations);
        var request = StartedTemporaryChatSave();
        var placeholder = request.Original with { Title = "Empty Chat", IsTemporary = false };
        await conversations.UpsertConversationAsync(placeholder, default);
        var branch = await production.EnsureRootBranchAsync(placeholder.Id, default);
        var attachment = new MessageAttachment(Guid.NewGuid(), placeholder.Id, null, branch.Id,
            "pending.txt", "pending.txt", "text/plain", MessageAttachmentKind.PlainText,
            1, "hash", AttachmentProcessingState.Ready, AttachmentAnalysisMethod.TextExtracted,
            "draft", "{}", placeholder.CreatedAt, placeholder.UpdatedAt);
        await production.UpsertAttachmentAsync(attachment, default);
        var oldDraft = new ConversationDraft(placeholder.Id, branch.Id, "Older typing", JsonSerializer.Serialize(new[] { attachment.Id }), placeholder.UpdatedAt);
        await production.SaveDraftAsync(oldDraft, default);

        await Assert.ThrowsAsync<InvalidOperationException>(() => conversations.SaveTemporaryChatAsync(request, default));
        var owned = request with { ExpectedPlaceholder = placeholder, ExpectedDraft = oldDraft, ExpectedBranchId = branch.Id };
        await Assert.ThrowsAsync<InvalidOperationException>(() => conversations.SaveTemporaryChatAsync(owned, default));
        var named = owned with { PendingAttachmentIds = [attachment.Id] };
        await Assert.ThrowsAsync<InvalidOperationException>(() => conversations.SaveTemporaryChatAsync(named with { ExpectedBranchId = Guid.NewGuid(), ExpectedDraft = null }, default));
        var other = ConversationAt(DateTimeOffset.UtcNow);
        await conversations.UpsertConversationAsync(other, default);
        var otherBranch = await production.EnsureRootBranchAsync(other.Id, default);
        var foreignAttachment = attachment with { Id = Guid.NewGuid(), ConversationId = other.Id, BranchId = otherBranch.Id };
        await production.UpsertAttachmentAsync(foreignAttachment, default);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            conversations.SaveTemporaryChatAsync(owned with { PendingAttachmentIds = [attachment.Id, foreignAttachment.Id] }, default));
        var changedDraft = oldDraft with { Content = "Newer draft from another view" };
        await production.SaveDraftAsync(changedDraft, default);
        await Assert.ThrowsAsync<InvalidOperationException>(() => conversations.SaveTemporaryChatAsync(named, default));
        Assert.Equal(changedDraft, await production.GetDraftAsync(placeholder.Id, branch.Id, default));
        Assert.Empty(await conversations.GetMessagesAsync(placeholder.Id, default));
        Assert.Equal(placeholder, await conversations.GetAsync(placeholder.Id, default));

        var receipt = await conversations.SaveTemporaryChatAsync(owned with
            { ExpectedDraft = changedDraft, PendingAttachmentIds = [attachment.Id] }, default);

        Assert.Equal(branch.Id, receipt.BranchId);
        Assert.Equal(attachment, Assert.Single(await production.GetAttachmentsAsync(placeholder.Id, null, default)));
        var savedDraft = Assert.IsType<ConversationDraft>(await production.GetDraftAsync(placeholder.Id, branch.Id, default));
        Assert.Equal(request.DraftText, savedDraft.Content);
        Assert.Equal(new[] { attachment.Id }, JsonSerializer.Deserialize<Guid[]>(savedDraft.AttachmentIdsJson));
    }

    [Fact]
    public async Task TemporaryChatSaveRefusesAnExistingRegularHistoryEvenIfCallerNamesThatRow()
    {
        var database = await CreateDatabaseAsync();
        var conversations = new ConversationRepository(database);
        var request = StartedTemporaryChatSave();
        var placeholder = request.Original with { IsTemporary = false };
        var originalMessage = request.History[0] with { Id = Guid.NewGuid(), Content = "Regular original" };
        await conversations.UpsertConversationAsync(placeholder, default);
        await conversations.AddMessageAsync(originalMessage, default);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            conversations.SaveTemporaryChatAsync(request with { ExpectedPlaceholder = placeholder }, default));
        Assert.Equal(placeholder, await conversations.GetAsync(placeholder.Id, default));
        Assert.Equal(originalMessage, Assert.Single(await conversations.GetMessagesAsync(placeholder.Id, default)));
    }

    [Fact]
    public async Task TemporaryChatSaveRejectsUnstartedOrCancelledAndAcknowledgesOnlyOneConcurrentSave()
    {
        var database = await CreateDatabaseAsync();
        var conversations = new ConversationRepository(database);
        var request = StartedTemporaryChatSave();
        await Assert.ThrowsAsync<ArgumentException>(() => conversations.SaveTemporaryChatAsync(request with { History = [] }, default));
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => conversations.SaveTemporaryChatAsync(request, cancelled.Token));
        Assert.Null(await conversations.GetAsync(request.Original.Id, default));

        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var attempts = Enumerable.Range(0, 2).Select(_ => Task.Run(async () =>
        {
            await start.Task;
            try { return await conversations.SaveTemporaryChatAsync(request, default); }
            catch (InvalidOperationException) { return null; }
        })).ToArray();
        start.SetResult();
        var receipt = Assert.IsType<TemporaryChatSaveReceipt>(Assert.Single(await Task.WhenAll(attempts), item => item is not null));
        Assert.Equal(receipt.Conversation, await conversations.GetAsync(request.Original.Id, default));
        Assert.Equal(request.History.Count, (await conversations.GetMessagesAsync(request.Original.Id, default)).Count);
    }

    private static TemporaryChatSaveRequest StartedTemporaryChatSave()
    {
        var now = DateTimeOffset.UtcNow;
        var original = ConversationAt(now) with { IsTemporary = true, SpaceId = Guid.NewGuid() };
        ChatMessage User(string text) => new(Guid.NewGuid(), original.Id, MessageRole.User, text, null, null, null, now);
        var assistant = new ChatMessage(Guid.NewGuid(), original.Id, MessageRole.Assistant, "Original reply", "Haven", "qwen", null, now);
        return new(original, [User("Original question"), assistant], " Keep exact unsent text ", []);
    }
}
