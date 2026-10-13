using System.Reflection;
using Haven.Application;
using Haven.Core;
using HavenOS.Apps.Spaces.Chat;
using Xunit;

namespace HavenOS.Apps.Spaces.Tests;

public sealed class HavenChatSpaceBackendTests : IDisposable
{
    private readonly CancellationTokenSource _testLifetime = new(TimeSpan.FromSeconds(30));
    public void Dispose() => _testLifetime.Dispose();
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Production_draft_save_and_delete_preserve_faulted_original_cancellation(bool emptyDraft)
    {
        using var owner = new CancellationTokenSource();
        var originalFailure = new OperationCanceledException("faulted raw production acknowledgement", owner.Token);
        var production = DispatchProxy.Create<IConversationProductionRepository, Production>();
        var held = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        ((Production)production).Read = name =>
        {
            Assert.Equal(emptyDraft ? nameof(IConversationProductionRepository.DeleteDraftAsync) : nameof(IConversationProductionRepository.SaveDraftAsync), name);
            calls++; return held.Task;
        };
        var backend = new HavenChatSpaceBackend(new Repository(), production, null!, null!, null!, null!);
        var save = backend.SaveDraftAsync(Guid.NewGuid(), null, emptyDraft ? "" : "original draft", [], owner.Token);
        Assert.False(save.IsCompleted);
        owner.Cancel(); held.SetException(originalFailure);
        var error = await Assert.ThrowsAsync<AggregateException>(() => save);
        Assert.True(save.IsFaulted);
        Assert.Same(originalFailure, Assert.Single(error.InnerExceptions));
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task Production_read_joins_original_history_after_synchronous_attachment_failure()
    {
        var repository = new Repository();
        var now = DateTimeOffset.UtcNow;
        var conversation = new Conversation(Guid.NewGuid(), HavenMode.Chat, ConversationKind.Chat, "Read original",
            null, null, false, false, now, now, SpaceId: SpaceRegistry.ChatSpaceId);
        repository.Stored.Add(conversation.Id, conversation);
        var held = new TaskCompletionSource<IReadOnlyList<ChatMessage>>(TaskCreationOptions.RunContinuationsAsynchronously);
        repository.MessageRead = () => held.Task;
        var historyFailure = new IOException("original history failed late");
        var attachmentFailure = new AggregateException();
        var production = DispatchProxy.Create<IConversationProductionRepository, Production>();
        var branch = new ConversationBranch(Guid.NewGuid(), conversation.Id, null, null, "Main", ConversationBranchReason.Root, true, now, now);
        ((Production)production).Read = name => name switch
        {
            nameof(IConversationProductionRepository.GetCurrentBranchAsync) => Task.FromResult<ConversationBranch?>(branch),
            nameof(IConversationProductionRepository.GetAttachmentsAsync) => throw attachmentFailure,
            nameof(IConversationProductionRepository.GetBranchesAsync) => Task.FromResult<IReadOnlyList<ConversationBranch>>([branch]),
            nameof(IConversationProductionRepository.GetDraftAsync) => Task.FromResult<ConversationDraft?>(null),
            _ => throw new InvalidOperationException("Unexpected actual production call: " + name)
        };
        var backend = new HavenChatSpaceBackend(repository, production, null!, null!, null!, null!);
        var read = backend.GetConversationAsync(conversation.Id, _testLifetime.Token);
        Assert.False(read.IsCompleted);
        held.SetException(historyFailure);
        var error = await Assert.ThrowsAsync<AggregateException>(() => read);
        Assert.Equal(2, error.InnerExceptions.Count);
        Assert.Single(error.InnerExceptions, cause => ReferenceEquals(cause, historyFailure));
        Assert.Single(error.InnerExceptions, cause => ReferenceEquals(cause, attachmentFailure));
        Assert.Equal(1, repository.MessageReads);
    }

    [Fact]
    public async Task Lost_creation_acknowledgement_recovers_same_committed_identity_without_second_write()
    {
        var repository = new Repository { LoseNextWriteAcknowledgement = true };
        var production = DispatchProxy.Create<IConversationProductionRepository, Production>();
        var backend = new HavenChatSpaceBackend(repository, production, null!, null!, null!, null!);
        var token = _testLifetime.Token;
        Assert.Same(repository.LostAcknowledgement, await Assert.ThrowsAsync<IOException>(() => backend.CreateChatAsync(token)));
        var actual = Assert.Single(repository.Stored.Values);
        var recovered = await backend.CreateChatAsync(token);
        Assert.Equal(actual.Id, recovered.Id);
        Assert.Equal(1, repository.Writes);
        Assert.Equal(actual.Id, Assert.Single(((Production)production).RootRequests));
        var separate = await backend.CreateChatAsync(token);
        Assert.NotEqual(actual.Id, separate.Id);
        Assert.Equal(2, repository.Stored.Count);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task Temporary_or_archived_descriptor_cannot_hydrate_private_durable_history(bool temporary, bool archived)
    {
        var repository = new Repository();
        var actual = new Conversation(Guid.NewGuid(), HavenMode.Chat, ConversationKind.Chat, "Unavailable",
            null, null, false, temporary, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, IsArchived: archived,
            SpaceId: SpaceRegistry.ChatSpaceId);
        repository.Stored.Add(actual.Id, actual);
        var production = DispatchProxy.Create<IConversationProductionRepository, Production>();
        var backend = new HavenChatSpaceBackend(repository, production, null!, null!, null!, null!);
        await Assert.ThrowsAsync<InvalidOperationException>(() => backend.GetConversationAsync(actual.Id, _testLifetime.Token));
        Assert.Equal(0, repository.MessageReads);
        Assert.Empty(((Production)production).RootRequests);
    }

    public class Production : DispatchProxy
    {
        internal readonly List<Guid> RootRequests = [];
        internal Func<string, object?>? Read;
        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            if (Read is { } read) return read(method!.Name);
            if (method?.Name != nameof(IConversationProductionRepository.EnsureRootBranchAsync))
                throw new InvalidOperationException("Unexpected production read before descriptor validation.");
            var id = (Guid)args![0]!; RootRequests.Add(id); var now = DateTimeOffset.UtcNow;
            return Task.FromResult(new ConversationBranch(Guid.NewGuid(), id, null, null, "Main", ConversationBranchReason.Root, true, now, now));
        }
    }
    private sealed class Repository : IConversationRepository
    {
        internal readonly Dictionary<Guid, Conversation> Stored = [];
        internal bool LoseNextWriteAcknowledgement;
        internal readonly IOException LostAcknowledgement = new("Commit succeeded but original acknowledgement was lost.");
        internal int Writes, MessageReads;
        internal Func<Task<IReadOnlyList<ChatMessage>>>? MessageRead;
        public Task<IReadOnlyList<Conversation>> GetRecentAsync(HavenMode? mode, int limit, CancellationToken token) => Task.FromResult<IReadOnlyList<Conversation>>(Stored.Values.ToArray());
        public Task<Conversation?> GetAsync(Guid id, CancellationToken token) => Task.FromResult(Stored.GetValueOrDefault(id));
        public Task<IReadOnlyList<ChatMessage>> GetMessagesAsync(Guid id, CancellationToken token) { MessageReads++; return MessageRead is { } read ? read() : Task.FromResult<IReadOnlyList<ChatMessage>>([]); }
        public Task UpsertConversationAsync(Conversation conversation, CancellationToken token)
        {
            Stored[conversation.Id] = conversation; Writes++;
            if (LoseNextWriteAcknowledgement) { LoseNextWriteAcknowledgement = false; return Task.FromException(LostAcknowledgement); }
            return Task.CompletedTask;
        }
        public Task AddMessageAsync(ChatMessage message, CancellationToken token) => throw new NotSupportedException();
        public Task DeleteConversationAsync(Guid id, CancellationToken token) => throw new NotSupportedException();
    }
}
