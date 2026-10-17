using Haven.Core;
using Xunit;

namespace Haven.Application.Tests;

public sealed partial class ChatPersistentMemoryRequestConstraintTests
{
    [Fact]
    public async Task Branch_input_acknowledgement_waits_for_original_write_and_blocks_dispatch_until_callback_finishes()
    {
        var repository = new BranchRepository();
        var (chat, conversation, model, provider) = BranchRig(repository);
        var attachmentId = Guid.NewGuid();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var acknowledgements = 0;
        var sending = ConsumeAsync();
        await entered.Task.WaitAsync(TestContext.Current.CancellationToken);
        Assert.Empty(provider.Requests);
        Assert.False(sending.IsCompleted);
        release.SetResult();
        await sending;
        Assert.Equal(1, acknowledgements);
        Assert.Single(provider.Requests);
        Assert.True(Assert.Single(repository.Invocations).Closed);

        async Task ConsumeAsync()
        {
            await foreach (var _ in chat.SendOnBranchAsync(repository.Branch, conversation, "attached input", model,
                EffortLevel.Medium, [], "Controlled", "", DuoMode.Solo, null, null, null, null, TestContext.Current.CancellationToken,
                attachmentIds: [attachmentId], inputAccepted: async (message, token) =>
                {
                    var original = Assert.Single(repository.Invocations);
                    Assert.Same(message, Assert.Single(original.Messages));
                    Assert.Equal([attachmentId], original.AttachmentIds);
                    acknowledgements++;
                    entered.SetResult();
                    await release.Task.WaitAsync(token);
                })) { }
        }
    }

    [Fact]
    public async Task Branch_write_refusal_never_acknowledges_input_or_dispatches_provider()
    {
        var repository = new BranchRepository { WriteFailure = new IOException("Original SQL refusal") };
        var (chat, conversation, model, provider) = BranchRig(repository);
        var acknowledged = false;
        var failure = await Record.ExceptionAsync(async () =>
        {
            await foreach (var _ in chat.SendOnBranchAsync(repository.Branch, conversation, "refused input", model,
                EffortLevel.Medium, [], "Controlled", "", DuoMode.Solo, null, null, null, null, TestContext.Current.CancellationToken,
                inputAccepted: (_, _) => { acknowledged = true; return Task.CompletedTask; })) { }
        });
        Assert.NotNull(failure);
        Assert.False(acknowledged);
        Assert.Empty(provider.Requests);
        Assert.True(Assert.Single(repository.Invocations).Closed);
    }

    [Fact]
    public async Task Explicit_branch_send_uses_original_handle_for_context_and_all_message_writes()
    {
        var repository = new BranchRepository();
        var (chat, conversation, model, provider) = BranchRig(repository);
        var events = new List<ChatStreamEvent>();
        await foreach (var item in chat.SendOnBranchAsync(repository.Branch, conversation, "bound input", model,
            EffortLevel.Medium, [], "Controlled", "", DuoMode.Solo, null, null, null, null, TestContext.Current.CancellationToken))
        {
            events.Add(item);
            if (item.Kind == ChatStreamEventKind.AssistantStarted) repository.CurrentBranch = Guid.NewGuid();
        }
        var invocation = Assert.Single(repository.Invocations);
        Assert.Equal(repository.Branch, invocation.BranchId);
        Assert.True(invocation.Closed);
        Assert.True(invocation.Reads > 0);
        Assert.Collection(invocation.Messages, message => Assert.Equal(MessageRole.User, message.Role), message => Assert.Equal(MessageRole.Assistant, message.Role));
        Assert.Single(provider.Requests);
        Assert.Contains(events, item => item.Kind == ChatStreamEventKind.AssistantCompleted);
    }

    [Fact]
    public async Task Explicit_regeneration_reuses_canonical_input_and_writes_only_the_response()
    {
        var repository = new BranchRepository();
        var (chat, conversation, model, _) = BranchRig(repository);
        var input = new ChatMessage(Guid.NewGuid(), conversation.Id, MessageRole.User, "existing input", null, null, null, DateTimeOffset.UtcNow);
        repository.Existing = input;
        var events = new List<ChatStreamEvent>();
        await foreach (var item in chat.SendOnBranchAsync(repository.Branch, conversation, input.Content, model,
            EffortLevel.Medium, [], "Controlled", "", DuoMode.Solo, null, null, null, null, TestContext.Current.CancellationToken, existingUserInput: input))
            events.Add(item);
        var invocation = Assert.Single(repository.Invocations);
        Assert.Equal(input, invocation.AdmittedExisting);
        Assert.Equal(MessageRole.Assistant, Assert.Single(invocation.Messages).Role);
        Assert.DoesNotContain(events, item => item.Kind == ChatStreamEventKind.UserMessage);
        Assert.True(invocation.Closed);
    }

    [Fact]
    public async Task Explicit_branch_send_closes_original_handle_when_consumer_stops_before_input_persistence()
    {
        var repository = new BranchRepository();
        var (chat, conversation, model, provider) = BranchRig(repository);
        await foreach (var _ in chat.SendOnBranchAsync(repository.Branch, conversation, "stop", model,
            EffortLevel.Medium, [], "Controlled", "", DuoMode.Solo, null, null, null, null, TestContext.Current.CancellationToken))
            break;
        Assert.True(Assert.Single(repository.Invocations).Closed);
        Assert.Empty(provider.Requests);
    }

    private static (ChatSessionService Chat, Conversation Conversation, ModelDescriptor Model, Provider Provider) BranchRig(BranchRepository repository)
    {
        var now = DateTimeOffset.UtcNow;
        var conversation = new Conversation(Guid.NewGuid(), HavenMode.Chat, ConversationKind.Chat, "Original branch", null, null, false, false, now, now);
        var model = new ModelDescriptor("controlled-branch-model", 1, "controlled", "controlled", "controlled", new HashSet<ToolCapability> { ToolCapability.Text }, now);
        var provider = new Provider(model);
        var chat = new ChatSessionService(repository, provider, new CapabilityPreflightService(), new Safety(),
            new WorkspaceToolRuntime(new Workspace()), new ComputerToolRuntime(new Computer()));
        return (chat, conversation, model, provider);
    }

    private sealed class BranchRepository : IConversationRepository, IConversationBranchBoundRepository
    {
        internal readonly Guid Branch = Guid.NewGuid();
        internal Guid CurrentBranch;
        internal ChatMessage? Existing;
        internal Exception? WriteFailure;
        internal readonly List<Invocation> Invocations = [];
        internal BranchRepository() => CurrentBranch = Branch;
        public IConversationBranchInvocation BindInvocation(Guid conversationId, Guid branchId)
        {
            var invocation = new Invocation(this, conversationId, branchId); Invocations.Add(invocation); return invocation;
        }
        public Task<IReadOnlyList<Conversation>> GetRecentAsync(HavenMode? mode, int limit, CancellationToken token) => Task.FromResult<IReadOnlyList<Conversation>>([]);
        public Task<Conversation?> GetAsync(Guid id, CancellationToken token) => Task.FromResult<Conversation?>(null);
        public Task<IReadOnlyList<ChatMessage>> GetMessagesAsync(Guid id, CancellationToken token) => throw new InvalidOperationException("Unbound history is forbidden in this regression.");
        public Task UpsertConversationAsync(Conversation conversation, CancellationToken token) => Task.CompletedTask;
        public Task AddMessageAsync(ChatMessage message, CancellationToken token) => throw new InvalidOperationException("Unbound persistence is forbidden in this regression.");
        public Task DeleteConversationAsync(Guid id, CancellationToken token) => throw new InvalidOperationException("Unexpected delete.");

        internal sealed class Invocation(BranchRepository owner, Guid conversation, Guid branch) : IConversationBranchInvocation
        {
            internal readonly List<ChatMessage> Messages = [];
            internal bool Closed;
            internal int Reads;
            internal ChatMessage? AdmittedExisting;
            public Guid ConversationId => conversation;
            public Guid BranchId => branch;
            public Task AdmitExistingUserInputAsync(ChatMessage message, CancellationToken cancellationToken)
            {
                Assert.False(Closed); Assert.Equal(owner.CurrentBranch, branch); Assert.Equal(owner.Existing, message);
                AdmittedExisting = message; return Task.CompletedTask;
            }
            public Task AddMessageAsync(ChatMessage message, CancellationToken cancellationToken)
            {
                Assert.False(Closed); Assert.Equal(conversation, message.ConversationId);
                if (owner.WriteFailure is { } failure) return Task.FromException(failure);
                if (message.Role == MessageRole.User) Assert.Equal(owner.CurrentBranch, branch);
                Messages.Add(message); return Task.CompletedTask;
            }
            public Task AddUserMessageWithAttachmentsAsync(ChatMessage message, IReadOnlyList<Guid> attachmentIds, CancellationToken cancellationToken)
            {
                AttachmentIds = attachmentIds.ToArray();
                return AddMessageAsync(message, cancellationToken);
            }
            internal IReadOnlyList<Guid> AttachmentIds = [];
            public Task<IReadOnlyList<ChatMessage>> GetContextMessagesAsync(CancellationToken cancellationToken)
            {
                Assert.False(Closed); Reads++;
                return Task.FromResult<IReadOnlyList<ChatMessage>>(AdmittedExisting is { } input ? new[] { input }.Concat(Messages).ToArray() : Messages.ToArray());
            }
            public ValueTask DisposeAsync() { Closed = true; return ValueTask.CompletedTask; }
        }
    }
}
