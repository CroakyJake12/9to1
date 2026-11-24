using Haven.Application;
using Haven.Core;
using Haven.Infrastructure;

namespace Haven.Infrastructure.Tests;

public sealed partial class ConversationProductionRepositoryTests
{
    [Fact]
    public async Task TemporaryTransferRefusesEveryExportFormatEvenWithLegacyPersistedMessages()
    {
        var database = await CreateDatabaseAsync();
        var history = new ConversationRepository(database);
        var production = CreateProduction(database, history);
        var exports = new ConversationExportService(production);
        var temporary = ConversationAt(DateTimeOffset.UtcNow) with { IsTemporary = true };
        // Reproduce historical rows left by old callers, including a real message.
        var legacyMessage = new ChatMessage(Guid.NewGuid(), temporary.Id, MessageRole.User, "Private legacy transcript",
            null, null, null, temporary.CreatedAt);
        await history.UpsertConversationAsync(temporary, default);
        await history.AddMessageAsync(legacyMessage, default);

        await Assert.ThrowsAsync<TemporaryChatMustBeSavedException>(() => production.BuildExportAsync(temporary.Id, default));
        await Assert.ThrowsAsync<TemporaryChatMustBeSavedException>(() => exports.ExportMarkdownAsync(temporary.Id, default));
        await Assert.ThrowsAsync<TemporaryChatMustBeSavedException>(() => exports.ExportPlainTextAsync(temporary.Id, default));
        await Assert.ThrowsAsync<TemporaryChatMustBeSavedException>(() => exports.ExportJsonAsync(temporary.Id, default));
        Assert.Equal(legacyMessage, Assert.Single(await history.GetMessagesAsync(temporary.Id, default)));
    }

    [Fact]
    public async Task TemporaryTransferLanStartRefusesBeforeTouchingShareOrNetwork()
    {
        var database = await CreateDatabaseAsync();
        var history = new ConversationRepository(database);
        var temporary = ConversationAt(DateTimeOffset.UtcNow) with { IsTemporary = true };
        await history.UpsertConversationAsync(temporary, default);
        // This dependency is intentionally absent: even stopping an existing share before
        // admission would dereference it. This path must never reach listener creation.
        await using var service = new LocalConversationShareService(null!, history);

        await Assert.ThrowsAsync<TemporaryChatMustBeSavedException>(() => service.StartAsync(temporary.Id, TimeSpan.FromMinutes(5), default));
    }

    [Fact]
    public async Task TemporaryTransferShareStorageRejectsTemporaryAndNeverRevivesAConflictingShareId()
    {
        var database = await CreateDatabaseAsync();
        var history = new ConversationRepository(database);
        var production = CreateProduction(database, history);
        var now = DateTimeOffset.UtcNow;
        var old = ConversationAt(now);
        var foreign = ConversationAt(now);
        await history.UpsertConversationAsync(old, default);
        await history.UpsertConversationAsync(foreign, default);
        var legacy = TemporaryTransferShare(old);
        await production.UpsertShareAsync(legacy, default);
        // A legacy caller may have marked the row Temporary while a share was installed.
        await history.UpsertConversationAsync(old with { IsTemporary = true }, default);
        var rejected = TemporaryTransferShare(old);

        await Assert.ThrowsAsync<TemporaryChatMustBeSavedException>(() => production.UpsertShareAsync(rejected, default));
        await Assert.ThrowsAsync<InvalidOperationException>(() => production.UpsertShareAsync(
            legacy with { ConversationId = foreign.Id, ExpiresAt = now.AddHours(1) }, default));
        Assert.Equal(legacy, await production.GetActiveShareAsync(old.Id, default));
        Assert.Null(await production.GetActiveShareAsync(foreign.Id, default));
        await production.StopShareAsync(legacy.Id, now, default);
        Assert.Null(await production.GetActiveShareAsync(old.Id, default));
    }

    [Fact]
    public async Task TemporaryTransferAllowsSameConversationAfterAtomicLocalSave()
    {
        var database = await CreateDatabaseAsync();
        var history = new ConversationRepository(database);
        var production = CreateProduction(database, history);
        var request = StartedTemporaryChatSave();
        await history.UpsertConversationAsync(request.Original, default);
        var share = TemporaryTransferShare(request.Original);
        await Assert.ThrowsAsync<TemporaryChatMustBeSavedException>(() => production.BuildExportAsync(request.Original.Id, default));
        await Assert.ThrowsAsync<TemporaryChatMustBeSavedException>(() => production.UpsertShareAsync(share, default));
        Assert.Null(await production.GetActiveShareAsync(request.Original.Id, default));

        var saved = await history.SaveTemporaryChatAsync(request with { ExpectedPlaceholder = request.Original }, default);
        var exported = await production.BuildExportAsync(request.Original.Id, default);
        await production.UpsertShareAsync(share, default);

        Assert.Equal(saved.Conversation, exported.Conversation);
        Assert.Equal(request.History.ToArray(), exported.Messages.ToArray());
        Assert.Equal(share, await production.GetActiveShareAsync(request.Original.Id, default));
    }

    [Fact]
    public async Task TemporaryTransferRechecksStorageIfAnExportIsHeldAcrossLegacyReclassification()
    {
        var database = await CreateDatabaseAsync();
        var history = new ConversationRepository(database);
        var saved = ConversationAt(DateTimeOffset.UtcNow);
        await history.UpsertConversationAsync(saved, default);
        await history.AddMessageAsync(
            new ChatMessage(Guid.NewGuid(), saved.Id, MessageRole.User, "History fixture", null, null, null, saved.CreatedAt), default);
        var held = new HeldExportHistory(history);
        var production = new ConversationProductionRepository(database, held);
        var export = production.BuildExportAsync(saved.Id, default);
        try
        {
            await held.MessagesEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            // An older toggle can still replace the flag while this release is gathering its
            // projections. Update the real SQLite row, not the export's already-read record.
            await history.UpsertConversationAsync(saved with { IsTemporary = true }, default);
        }
        finally
        {
            held.ReleaseMessages.TrySetResult();
        }

        await Assert.ThrowsAsync<TemporaryChatMustBeSavedException>(() => export);
    }

    private sealed class HeldExportHistory(ConversationRepository history) : IConversationRepository
    {
        public TaskCompletionSource MessagesEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseMessages { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<Conversation?> GetAsync(Guid id, CancellationToken cancellationToken) => history.GetAsync(id, cancellationToken);

        public async Task<IReadOnlyList<ChatMessage>> GetMessagesAsync(Guid id, CancellationToken cancellationToken)
        {
            MessagesEntered.TrySetResult();
            await ReleaseMessages.Task.WaitAsync(cancellationToken);
            return await history.GetMessagesAsync(id, cancellationToken);
        }

        public Task<IReadOnlyList<Conversation>> GetRecentAsync(HavenMode? mode, int limit, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task UpsertConversationAsync(Conversation conversation, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task AddMessageAsync(ChatMessage message, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task DeleteConversationAsync(Guid id, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private static SharedSession TemporaryTransferShare(Conversation conversation)
    {
        var now = DateTimeOffset.UtcNow;
        return new SharedSession(Guid.NewGuid(), conversation.Id, "fixture-token-hash", "127.0.0.1", 55988,
            SharedSessionState.Active, now, now.AddMinutes(5), null);
    }
}
