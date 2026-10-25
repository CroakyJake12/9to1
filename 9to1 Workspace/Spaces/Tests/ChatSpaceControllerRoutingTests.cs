using Haven.Application;
using Haven.Core;
using HavenOS.Apps.Spaces.Chat;
using Xunit;

namespace HavenOS.Apps.Spaces.Tests;

/// <summary>
/// Merge stage M3 shell wiring: the operations the native Chat Space page dispatches CUI
/// commands to.
///
/// The CUI document can only raise commands the host owns. These tests pin the controller side
/// of that contract against a recording backend, so a document change that names a command, or a
/// binding that reads state the backend never produces, is caught here rather than discovered on
/// screen. The behaviours asserted are the ones the behavioural delta depends on: composer gating,
/// draft round-trip, attachment lifecycle, and model governance.
/// </summary>
public sealed class ChatSpaceControllerRoutingTests
{
    private sealed class RecordingBackend : IChatSpaceBackend
    {
        private readonly Dictionary<Guid, ChatSpaceConversationData> _conversations = [];

        internal List<string> Calls { get; } = [];
        internal List<ChatSpaceTurnRequest> Sent { get; } = [];
        internal Guid? CurrentBranch { get; private set; }
        internal ChatSpaceTurnRequest? LastRegenerate { get; private set; }
        internal Guid? CreatedBranchParent { get; private set; }
        internal Guid? SwitchedBranch { get; private set; }
        internal List<Guid> RemovedAttachments { get; } = [];
        internal string? SavedDraftContent { get; private set; }
        internal IReadOnlyList<Guid> SavedDraftAttachments { get; private set; } = [];
        internal MessageAttachment? NextAttachment { get; set; }
        internal ChatSpaceModelInventory Inventory { get; set; } = new([Model("model-a"), Model("model-b")], "model-a");

        private static Conversation Record(Guid id, string title) =>
            new(id, HavenMode.Chat, ConversationKind.Chat, title, null, null, false, false,
                DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, SpaceId: SpaceRegistry.ChatSpaceId);

        internal Conversation Seed(Guid id, string title, IReadOnlyList<ChatMessage>? messages = null)
        {
            var conversation = Record(id, title);
            _conversations[id] = new(conversation, messages ?? [], [], [], null);
            return conversation;
        }

        internal void SetDraft(Guid conversationId, string content, IReadOnlyList<Guid> attachments)
        {
            var existing = _conversations[conversationId];
            _conversations[conversationId] = existing with
            {
                Draft = new ConversationDraft(conversationId, CurrentBranch, content, "[]", DateTimeOffset.UtcNow)
            };
            _ = attachments;
        }

        public Task<IReadOnlyList<Conversation>> GetRecentChatsAsync(int limit, CancellationToken cancellationToken)
        {
            Calls.Add(nameof(GetRecentChatsAsync));
            return Task.FromResult<IReadOnlyList<Conversation>>(
                _conversations.Values.Select(data => data.Conversation).Take(limit).ToArray());
        }

        public Task<ChatSpaceModelInventory> GetModelInventoryAsync(CancellationToken cancellationToken)
        {
            Calls.Add(nameof(GetModelInventoryAsync));
            return Task.FromResult(Inventory);
        }

        public Task<Conversation> CreateChatAsync(CancellationToken cancellationToken)
        {
            Calls.Add(nameof(CreateChatAsync));
            var conversation = Seed(Guid.NewGuid(), "New chat");
            return Task.FromResult(conversation);
        }

        public Task<ChatSpaceConversationData> GetConversationAsync(Guid conversationId, CancellationToken cancellationToken)
        {
            Calls.Add(nameof(GetConversationAsync));
            return Task.FromResult(_conversations[conversationId]);
        }

        public Task SaveDraftAsync(Guid conversationId, Guid? branchId, string content,
            IReadOnlyList<Guid> attachmentIds, CancellationToken cancellationToken)
        {
            Calls.Add(nameof(SaveDraftAsync));
            SavedDraftContent = content;
            SavedDraftAttachments = attachmentIds.ToArray();
            var existing = _conversations[conversationId];
            _conversations[conversationId] = existing with
            {
                Draft = string.IsNullOrWhiteSpace(content) && attachmentIds.Count == 0
                    ? null
                    : new ConversationDraft(conversationId, branchId, content, "[]", DateTimeOffset.UtcNow)
            };
            return Task.CompletedTask;
        }

        public Task<MessageAttachment> ImportAttachmentAsync(Guid conversationId, Guid? branchId, string path,
            CancellationToken cancellationToken)
        {
            Calls.Add(nameof(ImportAttachmentAsync));
            return Task.FromResult(NextAttachment ?? throw new InvalidOperationException("no attachment configured"));
        }

        public Task RemoveAttachmentAsync(Guid conversationId, Guid? branchId, Guid attachmentId, CancellationToken cancellationToken)
        {
            Calls.Add(nameof(RemoveAttachmentAsync));
            RemovedAttachments.Add(attachmentId);
            return Task.CompletedTask;
        }

        public Task SendAsync(ChatSpaceTurnRequest request, IProgress<ChatStreamEvent>? progress, CancellationToken cancellationToken)
        {
            Calls.Add(nameof(SendAsync));
            Sent.Add(request);
            return Task.CompletedTask;
        }

        public Task<ConversationSafetySnapshot> GetSafetySnapshotAsync(Guid conversationId, CancellationToken cancellationToken) =>
            Task.FromResult(new ConversationSafetySnapshot(conversationId, 0, ConversationSafetyState.Active, null, 0));
        public Task<string> GetMessageContentAsync(Guid conversationId, Guid messageId, CancellationToken cancellationToken) =>
            Task.FromResult(_conversations[conversationId].Messages.Single(message => message.Id == messageId).Content);
        public Task DeleteMessageAsync(Guid conversationId, Guid messageId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task ForgetMessageAsync(Guid conversationId, Guid messageId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task EditUserMessageAsync(Guid conversationId, Guid messageId, string content, MessageEditMode mode, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<DualModelRun> RunDualAsync(string prompt, ModelDescriptor first, string secondModelName, DuoMode mode, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<ConversationBranch> CreateBranchAsync(Guid conversationId, Guid messageId, string? name,
            CancellationToken cancellationToken)
        {
            Calls.Add(nameof(CreateBranchAsync));
            CreatedBranchParent = messageId;
            return Task.FromResult(Branch(Guid.NewGuid(), conversationId, name ?? "Branch", true));
        }

        public Task SwitchBranchAsync(Guid conversationId, Guid branchId, CancellationToken cancellationToken)
        {
            Calls.Add(nameof(SwitchBranchAsync));
            SwitchedBranch = branchId;
            return Task.CompletedTask;
        }

        public Task RegenerateAsync(Guid conversationId, Guid assistantMessageId, ModelDescriptor model,
            CancellationToken cancellationToken)
        {
            Calls.Add(nameof(RegenerateAsync));
            LastRegenerate = new(new Conversation(conversationId, HavenMode.Chat, ConversationKind.Chat,
                "Space chat", null, null, false, false, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow),
                string.Empty, model, []);
            return Task.CompletedTask;
        }
    }

    private static ModelDescriptor Model(string name) =>
        new(name, 1_000_000, "llama", "7B", "Q4", new HashSet<ToolCapability>(), DateTimeOffset.UtcNow);

    private static ConversationBranch Branch(Guid id, Guid conversationId, string name, bool isCurrent) =>
        new(id, conversationId, null, null, name, ConversationBranchReason.Manual, isCurrent,
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

    private static MessageAttachment Attachment(Guid id, Guid conversationId, string name) =>
        new(id, conversationId, null, null, name, $"stored/{name}", "text/plain",
            MessageAttachmentKind.PlainText, 10, new string('a', 64),
            AttachmentProcessingState.Ready, AttachmentAnalysisMethod.None, string.Empty, "{}",
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

    private static async Task<(RecordingBackend Backend, ChatSpaceController Controller)> OpenAsync(
        Guid? conversationId = null)
    {
        var backend = new RecordingBackend();
        var controller = new ChatSpaceController(backend);
        await controller.InitializeAsync(conversationId);
        return (backend, controller);
    }

    [Fact]
    public async Task Opening_publishes_recent_chats_models_and_a_status_the_document_can_bind()
    {
        var (backend, controller) = await OpenAsync();
        var id = backend.Seed(Guid.NewGuid(), "Existing chat").Id;

        await controller.OpenChatAsync(id);

        Assert.Equal(id, controller.State.Conversation?.Id);
        Assert.Contains(controller.State.RecentChats, chat => chat.Id == id);
        Assert.Equal(2, controller.State.Models.Count);
        // The composer may only become sendable once a governed model is resolved.
        Assert.Equal("model-a", controller.State.Composer.SelectedModelName);
    }

    [Fact]
    public async Task Send_is_refused_until_a_governed_model_is_selected()
    {
        var (backend, controller) = await OpenAsync();
        var id = backend.Seed(Guid.NewGuid(), "Existing chat").Id;
        await controller.OpenChatAsync(id);
        controller.UpdateDraft("a prompt");

        // model-a is the resolved preference, so send is admitted once there is a draft.
        Assert.True(controller.State.Composer.CanSend);

        var unknown = new ModelDescriptor("not-installed", 1, "llama", "7B", "Q4",
            new HashSet<ToolCapability>(), DateTimeOffset.UtcNow);
        Assert.Throws<ArgumentException>(() => controller.SelectModel(unknown.Name));
        // The refusal must leave the selection untouched rather than half-applied.
        Assert.Equal("model-a", controller.State.Composer.SelectedModelName);
    }

    [Fact]
    public async Task Unavailable_space_default_keeps_draft_and_eligible_choices_for_explicit_override()
    {
        var (backend, controller) = await OpenAsync();
        var id = backend.Seed(Guid.NewGuid(), "Space policy chat").Id;
        backend.Inventory = new([Model("model-a"), Model("model-b")], null,
            PreventImplicitFallback: true,
            PolicyNotice: "The configured Space default is unavailable; no substitute was selected.");

        await controller.OpenChatAsync(id);
        controller.UpdateDraft("keep this draft");

        Assert.Null(controller.State.Composer.SelectedModelName);
        Assert.False(controller.State.Composer.CanSend);
        Assert.Equal(2, controller.State.Models.Count);
        Assert.Contains("no substitute was selected", controller.State.Status.Message, StringComparison.Ordinal);
        Assert.Equal("keep this draft", controller.State.Composer.Draft);

        controller.SelectModel("model-b");
        Assert.Equal("model-b", controller.State.Composer.SelectedModelName);
        Assert.True(controller.State.Composer.IsModelSelectionExplicit);
        Assert.True(controller.State.Composer.CanSend);
        await controller.SendAsync();
        Assert.Equal("model-b", Assert.Single(backend.Sent).Model.Name);
    }

    [Fact]
    public async Task Explicit_model_is_cleared_when_a_refresh_makes_it_ineligible()
    {
        var (backend, controller) = await OpenAsync();
        var id = backend.Seed(Guid.NewGuid(), "Space policy chat").Id;
        await controller.OpenChatAsync(id);
        controller.SelectModel("model-b");
        controller.UpdateDraft("retain this draft");
        backend.Inventory = new([Model("model-a")], "model-a");

        await controller.OpenChatAsync(id);

        Assert.Equal(new[] { "model-a" }, controller.State.Models.Select(model => model.Name));
        Assert.Null(controller.State.Composer.SelectedModelName);
        Assert.False(controller.State.Composer.IsModelSelectionExplicit);
        Assert.False(controller.State.Composer.CanSend);
        Assert.Equal("retain this draft", controller.State.Composer.Draft);
        Assert.Contains("no longer eligible", controller.State.Status.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Sending_forwards_the_selected_model_and_clears_the_composer()
    {
        var (backend, controller) = await OpenAsync();
        var id = backend.Seed(Guid.NewGuid(), "Existing chat").Id;
        await controller.OpenChatAsync(id);
        controller.SelectModel("model-b");
        controller.UpdateDraft("  explain this  ");

        await controller.SendAsync();

        var sent = Assert.Single(backend.Sent);
        Assert.Equal("model-b", sent.Model.Name);
        Assert.Equal("explain this", sent.Prompt);
        Assert.Equal(id, sent.Conversation.Id);
        Assert.Equal(SpaceRegistry.ChatSpaceId, sent.Conversation.SpaceId);
        // The composer must be released for the next turn, not left armed with a stale draft.
        Assert.Equal(string.Empty, controller.State.Composer.Draft);
        Assert.False(controller.State.Composer.IsSending);
    }

    [Fact]
    public async Task An_empty_composer_with_an_attachment_is_still_sendable()
    {
        var backend = new RecordingBackend();
        var id = backend.Seed(Guid.NewGuid(), "Existing chat").Id;
        var attachment = Attachment(Guid.NewGuid(), id, "notes.txt");
        backend.NextAttachment = attachment;
        var controller = new ChatSpaceController(backend);
        await controller.InitializeAsync(id);

        await controller.AddAttachmentAsync("/tmp/notes.txt");

        // Attachment-only turns are legitimate: the document's Send button must stay available
        // with an empty draft precisely because CanSend accounts for the attachment.
        Assert.True(controller.State.Composer.CanSend);
        Assert.Equal("notes.txt", Assert.Single(controller.State.Composer.Attachments).OriginalName);
        Assert.Equal([attachment.Id], backend.SavedDraftAttachments);
    }

    [Fact]
    public async Task Removing_an_attachment_is_reflected_in_the_published_composer_and_the_saved_draft()
    {
        var backend = new RecordingBackend();
        var id = backend.Seed(Guid.NewGuid(), "Existing chat").Id;
        var first = Attachment(Guid.NewGuid(), id, "a.txt");
        var second = Attachment(Guid.NewGuid(), id, "b.txt");
        backend.NextAttachment = first;
        var controller = new ChatSpaceController(backend);
        await controller.InitializeAsync(id);
        await controller.AddAttachmentAsync("/tmp/a.txt");
        backend.NextAttachment = second;
        await controller.AddAttachmentAsync("/tmp/b.txt");
        Assert.Equal(2, controller.State.Composer.Attachments.Count);

        await controller.RemoveAttachmentAsync(first.Id);

        Assert.DoesNotContain(controller.State.Composer.Attachments, item => item.Id == first.Id);
        Assert.Equal([first.Id], backend.RemovedAttachments);
        Assert.Equal([second.Id], backend.SavedDraftAttachments);
    }

    [Fact]
    public async Task The_saved_draft_survives_a_reload_of_the_same_conversation()
    {
        var backend = new RecordingBackend();
        var id = backend.Seed(Guid.NewGuid(), "Existing chat").Id;
        var controller = new ChatSpaceController(backend);
        await controller.InitializeAsync(id);
        controller.UpdateDraft("half-written thought");

        await controller.SaveDraftAsync();
        await controller.InitializeAsync(id);

        Assert.Equal("half-written thought", controller.State.Composer.Draft);
    }

    [Fact]
    public async Task Branching_and_switching_reach_the_backend_with_the_selected_identities()
    {
        var conversationId = Guid.NewGuid();
        var branchId = Guid.NewGuid();
        var otherBranchId = Guid.NewGuid();
        var messageId = Guid.NewGuid();
        var messages = new[]
        {
            new ChatMessage(messageId, conversationId, MessageRole.User, "hi", null, null, null,
                DateTimeOffset.UtcNow)
        };
        var conversation = new Conversation(conversationId, HavenMode.Chat, ConversationKind.Chat,
            "Existing chat", null, null, false, false, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow,
            SpaceId: SpaceRegistry.ChatSpaceId);
        var data = new ChatSpaceConversationData(conversation, messages, [],
            [Branch(branchId, conversationId, "Root", true), Branch(otherBranchId, conversationId, "Alternative", false)], null);
        var backend = new SingleConversationBackend(data);
        var controller = new ChatSpaceController(backend);
        await controller.InitializeAsync(conversationId);

        // The projected branch list is what the document's Select control binds to.
        var branches = controller.State.Conversation!.Branches;
        Assert.Equal(2, branches.Count);
        Assert.Equal(branchId, controller.State.Conversation.CurrentBranchId);

        await controller.BranchAsync(messageId, "Alternative");
        Assert.True(controller.State.Status.Tone != ChatSpaceStatusTone.Error,
            $"branch failed: {controller.State.Status.Message}");
        Assert.Equal(messageId, backend.CreatedBranchParent);
        Assert.Contains(nameof(SingleConversationBackend.CreateBranchAsync), backend.Calls);

        // Selecting the branch that is already current is a no-op, not a redundant write.
        await controller.SwitchBranchAsync(branchId);
        Assert.Null(backend.SwitchedBranch);

        await controller.SwitchBranchAsync(otherBranchId);
        Assert.Equal(otherBranchId, backend.SwitchedBranch);
        Assert.Contains(nameof(SingleConversationBackend.SwitchBranchAsync), backend.Calls);
    }

    private sealed class SingleConversationBackend(ChatSpaceConversationData data) : IChatSpaceBackend
    {
        internal List<string> Calls { get; } = [];
        internal Guid? CreatedBranchParent { get; private set; }
        internal Guid? SwitchedBranch { get; private set; }

        public Task<IReadOnlyList<Conversation>> GetRecentChatsAsync(int limit, CancellationToken cancellationToken)
        { Calls.Add(nameof(GetRecentChatsAsync)); return Task.FromResult<IReadOnlyList<Conversation>>([data.Conversation]); }

        public Task<ChatSpaceModelInventory> GetModelInventoryAsync(CancellationToken cancellationToken)
        { Calls.Add(nameof(GetModelInventoryAsync)); return Task.FromResult(new ChatSpaceModelInventory([Model("model-a")], "model-a")); }

        public Task<Conversation> CreateChatAsync(CancellationToken cancellationToken) => Task.FromResult(data.Conversation);

        public Task<ChatSpaceConversationData> GetConversationAsync(Guid conversationId, CancellationToken cancellationToken)
        { Calls.Add(nameof(GetConversationAsync)); return Task.FromResult(data); }

        public Task SaveDraftAsync(Guid conversationId, Guid? branchId, string content, IReadOnlyList<Guid> attachmentIds, CancellationToken cancellationToken)
        { Calls.Add(nameof(SaveDraftAsync)); return Task.CompletedTask; }

        public Task<MessageAttachment> ImportAttachmentAsync(Guid conversationId, Guid? branchId, string path, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task RemoveAttachmentAsync(Guid conversationId, Guid? branchId, Guid attachmentId, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task SendAsync(ChatSpaceTurnRequest request, IProgress<ChatStreamEvent>? progress, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<ConversationBranch> CreateBranchAsync(Guid conversationId, Guid messageId, string? name, CancellationToken cancellationToken)
        {
            Calls.Add(nameof(CreateBranchAsync));
            CreatedBranchParent = messageId;
            return Task.FromResult(Branch(Guid.NewGuid(), conversationId, name ?? "Branch", true));
        }

        public Task SwitchBranchAsync(Guid conversationId, Guid branchId, CancellationToken cancellationToken)
        { Calls.Add(nameof(SwitchBranchAsync)); SwitchedBranch = branchId; return Task.CompletedTask; }

        public Task RegenerateAsync(Guid conversationId, Guid assistantMessageId, ModelDescriptor model, CancellationToken cancellationToken)
            => throw new NotSupportedException();
        public Task<ConversationSafetySnapshot> GetSafetySnapshotAsync(Guid conversationId, CancellationToken cancellationToken) =>
            Task.FromResult(new ConversationSafetySnapshot(conversationId, 0, ConversationSafetyState.Active, null, 0));
        public Task<string> GetMessageContentAsync(Guid conversationId, Guid messageId, CancellationToken cancellationToken) =>
            Task.FromResult(data.Messages.Single(message => message.Id == messageId).Content);
        public Task DeleteMessageAsync(Guid conversationId, Guid messageId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task ForgetMessageAsync(Guid conversationId, Guid messageId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task EditUserMessageAsync(Guid conversationId, Guid messageId, string content, MessageEditMode mode, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<DualModelRun> RunDualAsync(string prompt, ModelDescriptor first, string secondModelName, DuoMode mode, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
