using System.Reflection;
using Haven.Application;
using Haven.Core;
using HavenOS.Apps.Spaces.Chat;
using Xunit;

namespace HavenOS.Apps.Spaces.Tests;

/// <summary>Action-time adapter checks; actual persistence admission is covered by the SQLite owner tests.</summary>
public sealed class HavenChatSpaceBackendAcceptanceTests
{
    [Theory]
    [InlineData("stale-branch")]
    [InlineData("locked")]
    [InlineData("missing-model")]
    [InlineData("foreign-attachment")]
    [InlineData("already-associated")]
    [InlineData("unapproved-existing-input")]
    public async Task Send_refuses_changed_action_time_ownership_before_entering_the_canonical_executor(string reason)
    {
        var rig = new Rig();
        var request = rig.Request();
        switch (reason)
        {
            case "stale-branch": request = request with { BranchId = Guid.NewGuid() }; break;
            case "locked": rig.Locked = true; break;
            case "missing-model": rig.Models = []; break;
            case "foreign-attachment": request = request with { AttachmentIds = [Guid.NewGuid()] }; break;
            case "already-associated": rig.Attachment = rig.Attachment with { MessageId = Guid.NewGuid() }; break;
            case "unapproved-existing-input": request = request with { ExistingUserInput = new(Guid.NewGuid(), rig.Conversation.Id, MessageRole.User, "not admitted by regeneration", null, null, null, DateTimeOffset.UtcNow) }; break;
        }
        var failure = Assert.IsAssignableFrom<InvalidOperationException>(await Record.ExceptionAsync(() => rig.Backend.SendAsync(request, null, CancellationToken.None)));
        var expected = reason switch
        {
            "stale-branch" => "original conversation branch changed",
            "locked" => "safety-locked",
            "missing-model" => "selected model is no longer available",
            "unapproved-existing-input" => "Existing input is admitted only",
            _ => "submitted attachment is no longer owned"
        };
        Assert.Contains(expected, failure.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(rig.Executed);
    }

    [Fact]
    public async Task Send_uses_current_canonical_records_and_fresh_governed_model_without_losing_acceptance_callback()
    {
        var rig = new Rig();
        Func<ChatMessage, CancellationToken, Task> observer = (_, _) => Task.CompletedTask;
        var request = rig.Request() with { Conversation = rig.Conversation with { Title = "stale title" },
            Model = rig.Models[0] with { Family = "stale descriptor" }, InputAccepted = observer };
        await rig.Backend.SendAsync(request, null, CancellationToken.None);
        var actual = Assert.Single(rig.Executed);
        Assert.Same(rig.Conversation, actual.Conversation);
        Assert.Same(rig.Models[0], actual.Model);
        Assert.Equal(rig.Branch.Id, actual.BranchId);
        Assert.Equal([rig.Attachment.Id], actual.AttachmentIds);
        Assert.Same(observer, actual.InputAccepted);
        Assert.Contains("chat.send", rig.SafetyAdmissions);
    }

    [Theory]
    [InlineData("foreign-conversation")]
    [InlineData("non-assistant")]
    [InlineData("empty-content")]
    public async Task Stopped_partial_write_rejects_invalid_message_identity_before_safety_or_storage(string invalid)
    {
        var rig = new Rig();
        var role = invalid == "non-assistant" ? MessageRole.User : MessageRole.Assistant;
        var conversationId = invalid == "foreign-conversation" ? Guid.NewGuid() : rig.Conversation.Id;
        var content = invalid == "empty-content" ? "   " : "partial reply";
        var message = new ChatMessage(Guid.NewGuid(), conversationId, role, content, "Assistant Main", "model-a", null, DateTimeOffset.UtcNow);
        var acceptedInput = AddAcceptedInput(rig);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            rig.Backend.PersistStoppedPartialAsync(rig.Conversation.Id, rig.Branch.Id, acceptedInput, message, CancellationToken.None));

        Assert.Empty(rig.SafetyAdmissions);
    }

    [Fact]
    public async Task Stopped_partial_write_requires_the_original_current_branch_and_unlocked_safety()
    {
        var rig = new Rig();
        var acceptedInput = AddAcceptedInput(rig);
        var message = new ChatMessage(Guid.NewGuid(), rig.Conversation.Id, MessageRole.Assistant,
            "partial reply", "Assistant Main", "model-a", null, DateTimeOffset.UtcNow);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            rig.Backend.PersistStoppedPartialAsync(rig.Conversation.Id, Guid.NewGuid(), acceptedInput, message, CancellationToken.None));
        Assert.Empty(rig.SafetyAdmissions);

        rig.Locked = true;
        var locked = await Assert.ThrowsAsync<ConversationSafetyLockException>(() =>
            rig.Backend.PersistStoppedPartialAsync(rig.Conversation.Id, rig.Branch.Id, acceptedInput, message, CancellationToken.None));
        Assert.Equal("chat.persist-stopped-partial", locked.Operation);
        Assert.Contains("chat.persist-stopped-partial", rig.SafetyAdmissions);
    }

    [Fact]
    public async Task Stopped_partial_write_refuses_a_branch_change_during_safety_admission()
    {
        var rig = new Rig { ChangeBranchDuringSafety = true };
        var originalBranch = rig.Branch.Id;
        var acceptedInput = AddAcceptedInput(rig);
        var message = new ChatMessage(Guid.NewGuid(), rig.Conversation.Id, MessageRole.Assistant,
            "partial reply", "Assistant Main", "model-a", null, DateTimeOffset.UtcNow);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            rig.Backend.PersistStoppedPartialAsync(rig.Conversation.Id, originalBranch, acceptedInput, message, CancellationToken.None));

        Assert.Contains("branch changed", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("chat.persist-stopped-partial", rig.SafetyAdmissions);
        Assert.DoesNotContain(rig.Messages, stored => stored.Role == MessageRole.Assistant);
    }

    [Fact]
    public async Task Stopped_partial_write_uses_the_original_branch_bound_conversation_store()
    {
        var rig = new Rig();
        var acceptedInput = AddAcceptedInput(rig);
        var message = new ChatMessage(Guid.NewGuid(), rig.Conversation.Id, MessageRole.Assistant,
            "partial reply", "Assistant Main", "model-a", "{\"responseState\":\"Stopped\",\"isPartial\":true}", DateTimeOffset.UtcNow);

        await rig.Backend.PersistStoppedPartialAsync(rig.Conversation.Id, rig.Branch.Id, acceptedInput, message, CancellationToken.None);

        Assert.Equal(2, rig.Messages.Count);
        Assert.Same(message, Assert.Single(rig.Messages, stored => stored.Id == message.Id));
        Assert.Equal(["chat.persist-stopped-partial", "chat.persist-stopped-partial", "chat.persist-stopped-partial"], rig.SafetyAdmissions);
    }

    [Fact]
    public async Task Branch_bound_store_rejects_assistant_persistence_until_exact_input_is_admitted()
    {
        var rig = new Rig();
        await using var invocation = rig.BranchStore.BindInvocation(rig.Conversation.Id, rig.Branch.Id);
        var assistant = new ChatMessage(Guid.NewGuid(), rig.Conversation.Id, MessageRole.Assistant,
            "partial reply", "Assistant Main", "model-a", null, DateTimeOffset.UtcNow);

        await Assert.ThrowsAsync<InvalidOperationException>(() => invocation.AddMessageAsync(assistant, CancellationToken.None));

        Assert.Empty(rig.Messages);
    }

    [Fact]
    public async Task Stopped_partial_write_does_not_guess_or_substitute_a_different_last_user_message()
    {
        var rig = new Rig();
        var acceptedInput = AddAcceptedInput(rig);
        var wrongInput = acceptedInput with { Id = Guid.NewGuid(), Content = "A different request" };
        var partial = new ChatMessage(Guid.NewGuid(), rig.Conversation.Id, MessageRole.Assistant,
            "partial reply", "Assistant Main", "model-a", null, DateTimeOffset.UtcNow);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            rig.Backend.PersistStoppedPartialAsync(rig.Conversation.Id, rig.Branch.Id, wrongInput, partial, CancellationToken.None));

        Assert.Single(rig.Messages);
        Assert.Same(acceptedInput, rig.Messages[0]);
    }

    [Fact]
    public async Task Stopped_partial_write_refuses_the_old_input_when_branch_history_has_advanced()
    {
        var rig = new Rig();
        var acceptedInput = AddAcceptedInput(rig);
        rig.Messages.Add(new ChatMessage(Guid.NewGuid(), rig.Conversation.Id, MessageRole.User,
            "A later accepted message", null, null, null, DateTimeOffset.UtcNow));
        var partial = new ChatMessage(Guid.NewGuid(), rig.Conversation.Id, MessageRole.Assistant,
            "partial reply", "Assistant Main", "model-a", null, DateTimeOffset.UtcNow);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            rig.Backend.PersistStoppedPartialAsync(rig.Conversation.Id, rig.Branch.Id, acceptedInput, partial, CancellationToken.None));

        Assert.Equal(2, rig.Messages.Count);
        Assert.DoesNotContain(rig.Messages, stored => stored.Id == partial.Id);
    }

    [Fact]
    public async Task Stopped_partial_write_refuses_a_branch_revision_change_at_the_store_boundary()
    {
        var rig = new Rig { ChangeBranchAtBoundWrite = true };
        var acceptedInput = AddAcceptedInput(rig);
        var message = new ChatMessage(Guid.NewGuid(), rig.Conversation.Id, MessageRole.Assistant,
            "partial reply", "Assistant Main", "model-a", null, DateTimeOffset.UtcNow);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            rig.Backend.PersistStoppedPartialAsync(rig.Conversation.Id, rig.Branch.Id, acceptedInput, message, CancellationToken.None));

        Assert.Contains("chat.persist-stopped-partial", rig.SafetyAdmissions);
        Assert.Single(rig.Messages, stored => stored.Id == acceptedInput.Id);
    }

    [Fact]
    public async Task Stopped_partial_write_never_overwrites_an_existing_completed_message_id()
    {
        var rig = new Rig();
        var acceptedInput = AddAcceptedInput(rig);
        var completed = new ChatMessage(Guid.NewGuid(), rig.Conversation.Id, MessageRole.Assistant,
            "Completed answer", "Assistant Main", "model-a", "{\"responseState\":\"Completed\"}", DateTimeOffset.UtcNow);
        rig.Messages.Add(completed);
        var duplicate = completed with { Content = "partial replacement", MetadataJson = "{\"responseState\":\"Stopped\",\"isPartial\":true}" };

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            rig.Backend.PersistStoppedPartialAsync(rig.Conversation.Id, rig.Branch.Id, acceptedInput, duplicate, CancellationToken.None));

        Assert.Contains("already exists", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(rig.SafetyAdmissions);
        var retained = Assert.Single(rig.Messages, stored => stored.Id == completed.Id);
        Assert.Equal("Completed answer", retained.Content);
        Assert.Equal("Completed", retained.Metadata["responseState"].GetString());
    }

    [Fact]
    public async Task Stopped_partial_write_preserves_completed_metadata_when_duplicate_appears_at_insert_boundary()
    {
        var rig = new Rig();
        var acceptedInput = AddAcceptedInput(rig);
        var partial = new ChatMessage(Guid.NewGuid(), rig.Conversation.Id, MessageRole.Assistant,
            "partial replacement", "Assistant Main", "model-a", "{\"responseState\":\"Stopped\",\"isPartial\":true}", DateTimeOffset.UtcNow);
        var completed = partial with { Content = "Completed answer", MetadataJson = "{\"responseState\":\"Completed\"}" };
        rig.DuplicateAtBoundWrite = completed;

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            rig.Backend.PersistStoppedPartialAsync(rig.Conversation.Id, rig.Branch.Id, acceptedInput, partial, CancellationToken.None));

        Assert.Equal(2, rig.Messages.Count);
        var retained = Assert.Single(rig.Messages, stored => stored.Id == completed.Id);
        Assert.Equal("Completed answer", retained.Content);
        Assert.Equal("Completed", retained.Metadata["responseState"].GetString());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Removing_an_attachment_requires_pending_branch_ownership_and_current_safety(bool locked)
    {
        var rig = new Rig { Locked = locked };
        if (!locked) rig.Attachment = rig.Attachment with { MessageId = Guid.NewGuid() };
        var failure = Assert.IsAssignableFrom<InvalidOperationException>(await Record.ExceptionAsync(() =>
            rig.Backend.RemoveAttachmentAsync(rig.Conversation.Id, rig.Branch.Id, rig.Attachment.Id, CancellationToken.None)));
        Assert.Contains(locked ? "safety-locked" : "Only a pending attachment", failure.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(rig.Deleted);
    }

    private static ChatMessage AddAcceptedInput(Rig rig)
    {
        var input = new ChatMessage(Guid.NewGuid(), rig.Conversation.Id, MessageRole.User,
            "Canonical accepted input", null, null, null, DateTimeOffset.UtcNow);
        rig.Messages.Add(input);
        return input;
    }

    private sealed class Rig
    {
        internal readonly Conversation Conversation;
        internal ConversationBranch Branch;
        internal MessageAttachment Attachment;
        internal ModelDescriptor[] Models;
        internal bool Locked;
        internal bool ChangeBranchDuringSafety;
        internal bool ChangeBranchAtBoundWrite;
        internal ChatMessage? DuplicateAtBoundWrite;
        internal readonly List<ChatMessage> Messages = [];
        internal readonly IConversationBranchBoundRepository BranchStore;
        internal readonly List<ChatSpaceTurnRequest> Executed = [];
        internal readonly List<Guid> Deleted = [];
        internal readonly List<string> SafetyAdmissions = [];
        internal readonly HavenChatSpaceBackend Backend;
        internal Rig()
        {
            var now = DateTimeOffset.UtcNow; var id = Guid.NewGuid();
            Conversation = new(id, HavenMode.Chat, ConversationKind.Chat, "Canonical title", null, null, false, false, now, now, SpaceId: SpaceRegistry.ChatSpaceId);
            Branch = new(Guid.NewGuid(), id, null, null, "Main", ConversationBranchReason.Root, true, now, now);
            Attachment = new(Guid.NewGuid(), id, null, Branch.Id, "selected.txt", "selected.txt", "text/plain", MessageAttachmentKind.PlainText,
                4, "controlled-hash", AttachmentProcessingState.Ready, AttachmentAnalysisMethod.TextExtracted, "text", "{}", now, now);
            Models = [new("model-a", 1, "fresh descriptor", "controlled", "controlled", new HashSet<ToolCapability> { ToolCapability.Text }, now)];
            var conversations = new BranchAwareStore(this);
            BranchStore = conversations;
            var production = StrictProxy.Create<IConversationProductionRepository>((method, _) => method switch
            {
                nameof(IConversationProductionRepository.GetCurrentBranchAsync) => Task.FromResult<ConversationBranch?>(Branch),
                nameof(IConversationProductionRepository.GetAttachmentsAsync) => Task.FromResult<IReadOnlyList<MessageAttachment>>([Attachment]),
                nameof(IConversationProductionRepository.GetBranchesAsync) => Task.FromResult<IReadOnlyList<ConversationBranch>>([Branch]),
                nameof(IConversationProductionRepository.GetDraftAsync) => Task.FromResult<ConversationDraft?>(null),
                _ => throw Unexpected(method)
            });
            var modelCatalog = StrictProxy.Create<IChatSpaceModelCatalog>((method, _) => method == nameof(IChatSpaceModelCatalog.GetInventoryAsync)
                ? Task.FromResult(new ChatSpaceModelInventory(Models, "model-a")) : throw Unexpected(method));
            var turns = StrictProxy.Create<IChatSpaceTurnExecutor>((method, args) =>
            {
                if (method != nameof(IChatSpaceTurnExecutor.ExecuteAsync)) throw Unexpected(method);
                Executed.Add((ChatSpaceTurnRequest)args[0]!); return Task.CompletedTask;
            });
            var attachments = StrictProxy.Create<IMessageAttachmentService>((method, args) =>
            {
                if (method != nameof(IMessageAttachmentService.DeleteAsync)) throw Unexpected(method);
                Deleted.Add((Guid)args[0]!); return Task.CompletedTask;
            });
            var safety = StrictProxy.Create<IConversationSafetyService>((method, args) =>
            {
                if (method != nameof(IConversationSafetyService.EnsureMayActAsync)) throw Unexpected(method);
                SafetyAdmissions.Add((string)args[1]!);
                if (ChangeBranchDuringSafety) Branch = Branch with { Id = Guid.NewGuid(), UpdatedAt = DateTimeOffset.UtcNow };
                return Locked ? Task.FromException(new ConversationSafetyLockException((Guid)args[0]!, (string)args[1]!)) : Task.CompletedTask;
            });
            Backend = new(conversations, production, StrictProxy.Create<IConversationVersioningService>((method, _) => throw Unexpected(method)), attachments, modelCatalog, turns, safety);
        }
        internal ChatSpaceTurnRequest Request() => new(Conversation, "Question", Models[0], [Attachment.Id], BranchId: Branch.Id);
        private static Exception Unexpected(string method) => new InvalidOperationException("Unexpected controlled boundary call: " + method);
    }

    private sealed class BranchAwareStore(Rig rig) : IConversationRepository, IConversationBranchBoundRepository
    {
        public Task<IReadOnlyList<Conversation>> GetRecentAsync(HavenMode? mode, int limit, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Conversation>>([rig.Conversation]);
        public Task<Conversation?> GetAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult<Conversation?>(id == rig.Conversation.Id ? rig.Conversation : null);
        public Task<IReadOnlyList<ChatMessage>> GetMessagesAsync(Guid conversationId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ChatMessage>>(rig.Messages.ToArray());
        public Task UpsertConversationAsync(Conversation conversation, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task AddMessageAsync(ChatMessage message, CancellationToken cancellationToken)
        { rig.Messages.Add(message); return Task.CompletedTask; }
        public Task DeleteConversationAsync(Guid id, CancellationToken cancellationToken) => Task.CompletedTask;
        public IConversationBranchInvocation BindInvocation(Guid conversationId, Guid branchId)
        {
            if (conversationId != rig.Conversation.Id) throw new InvalidOperationException("Wrong conversation for bound write.");
            if (rig.ChangeBranchAtBoundWrite) rig.Branch = rig.Branch with { Id = Guid.NewGuid(), UpdatedAt = DateTimeOffset.UtcNow };
            if (rig.DuplicateAtBoundWrite is { } duplicate) rig.Messages.Add(duplicate);
            return new BoundInvocation(rig, conversationId, branchId);
        }

        private sealed class BoundInvocation(Rig owner, Guid conversationId, Guid branchId) : IConversationBranchInvocation
        {
            private bool _disposed;
            private bool _admitted;
            public Guid ConversationId { get; } = conversationId;
            public Guid BranchId { get; } = branchId;
            public Task AdmitExistingUserInputAsync(ChatMessage message, CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (_disposed || _admitted || message.ConversationId != conversationId || message.Role != MessageRole.User ||
                    owner.Branch.Id != branchId || !owner.Branch.IsCurrent || owner.Messages.LastOrDefault(item => item.Role == MessageRole.User) != message)
                    throw new InvalidOperationException("The original accepted user input does not match current canonical branch history.");
                _admitted = true;
                return Task.CompletedTask;
            }
            public Task AddUserMessageWithAttachmentsAsync(ChatMessage message, IReadOnlyList<Guid> attachmentIds, CancellationToken cancellationToken) =>
                throw new NotSupportedException();
            public Task AddMessageAsync(ChatMessage message, CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (_disposed || owner.Branch.Id != branchId || !owner.Branch.IsCurrent || message.ConversationId != conversationId)
                    throw new InvalidOperationException("The original conversation branch is no longer current.");
                if (!_admitted && message.Role != MessageRole.User)
                    throw new InvalidOperationException("The original user input must be admitted before response persistence.");
                if (_admitted && message.Role == MessageRole.User)
                    throw new InvalidOperationException("The invocation has already admitted its original user input.");
                if (owner.Messages.Any(existing => existing.Id == message.Id))
                    throw new InvalidOperationException("The message ID already exists.");
                owner.Messages.Add(message);
                return Task.CompletedTask;
            }
            public Task<IReadOnlyList<ChatMessage>> GetContextMessagesAsync(CancellationToken cancellationToken) =>
                Task.FromResult<IReadOnlyList<ChatMessage>>(owner.Messages.ToArray());
            public ValueTask DisposeAsync() { _disposed = true; return ValueTask.CompletedTask; }
        }
    }

    public class StrictProxy : DispatchProxy
    {
        private Func<string, object?[], object?> _invoke = null!;
        internal static T Create<T>(Func<string, object?[], object?> invoke) where T : class
        {
            var value = DispatchProxy.Create<T, StrictProxy>();
            ((StrictProxy)(object)value)._invoke = invoke;
            return value;
        }
        protected override object? Invoke(MethodInfo? method, object?[]? args) =>
            _invoke((method ?? throw new InvalidOperationException("Missing method")).Name, args ?? []);
    }
}
