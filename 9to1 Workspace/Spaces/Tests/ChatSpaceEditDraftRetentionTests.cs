using Haven.Application;
using Haven.Core;
using HavenOS.Apps.Spaces.Chat;
using Xunit;

namespace HavenOS.Apps.Spaces.Tests;

public sealed class ChatSpaceEditDraftRetentionTests
{
    [Fact]
    public async Task Stale_cancel_edit_leaves_ordinary_draft_and_revision_unchanged()
    {
        var (controller, backend, _) = await OpenAsync();
        controller.UpdateDraft("Unsent ordinary draft");
        var before = controller.State;
        var publications = 0;
        controller.StateChanged += _ => publications++;
        backend.AllowReads = false;

        controller.CancelEdit();

        Assert.Same(before, controller.State);
        Assert.Equal("Unsent ordinary draft", controller.State.Composer.Draft);
        Assert.Equal(before.Revision, controller.State.Revision);
        Assert.Equal(0, publications);
    }

    [Fact]
    public async Task Active_edit_cancel_clears_only_edit_composer_and_preserves_history()
    {
        var (controller, backend, message) = await OpenAsync();
        controller.StartEditMessage(message.Id);
        Assert.True(controller.State.Composer.IsEditing);
        Assert.Equal(message.Id, controller.State.Composer.EditingMessageId);
        controller.UpdateDraft("Changed recalled message");
        var before = controller.State;
        backend.AllowReads = false;

        controller.CancelEdit();

        Assert.False(controller.State.Composer.IsEditing);
        Assert.Null(controller.State.Composer.EditingMessageId);
        Assert.Equal(string.Empty, controller.State.Composer.Draft);
        Assert.Same(before.Conversation, controller.State.Conversation);
        Assert.Equal(message.Content, Assert.Single(controller.State.Conversation!.Messages).Content);
        Assert.Equal(before.Revision + 1, controller.State.Revision);

        controller.UpdateDraft("Next ordinary draft");
        var ordinary = controller.State;
        controller.CancelEdit();
        Assert.Same(ordinary, controller.State);
    }

    private static async Task<(ChatSpaceController Controller, ReadOnlyBackend Backend, ChatMessage Message)> OpenAsync()
    {
        var id = Guid.NewGuid();
        var conversation = new Conversation(id, HavenMode.Chat, ConversationKind.Chat,
            "Existing chat", null, null, false, false, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow,
            SpaceId: SpaceRegistry.ChatSpaceId);
        var message = new ChatMessage(Guid.NewGuid(), id, MessageRole.User, "Original message",
            null, null, null, DateTimeOffset.UtcNow);
        var backend = new ReadOnlyBackend(new ChatSpaceConversationData(conversation, [message], [], [], null));
        var controller = new ChatSpaceController(backend);
        await controller.InitializeAsync(id);
        Assert.Equal(id, controller.State.Conversation?.Id);
        Assert.False(controller.State.IsLoading);
        return (controller, backend, message);
    }

    // Only initial snapshot reads are permitted. All mutations deliberately fail.
    private sealed class ReadOnlyBackend(ChatSpaceConversationData data) : IChatSpaceBackend
    {
        internal bool AllowReads { get; set; } = true;
        private Task<T> Read<T>(T value) => AllowReads ? Task.FromResult(value) : throw new InvalidOperationException("Unexpected backend read");
        private static Exception Unexpected() => new InvalidOperationException("Unexpected backend mutation");
        public Task<IReadOnlyList<Conversation>> GetRecentChatsAsync(int limit, CancellationToken cancellationToken)
            => Read<IReadOnlyList<Conversation>>([data.Conversation]);
        public Task<ChatSpaceModelInventory> GetModelInventoryAsync(CancellationToken cancellationToken)
            => Read(new ChatSpaceModelInventory([], null));
        public Task<Conversation> CreateChatAsync(CancellationToken cancellationToken) => throw Unexpected();
        public Task<ChatSpaceConversationData> GetConversationAsync(Guid conversationId, CancellationToken cancellationToken)
            => Read(data);
        public Task<ConversationSafetySnapshot> GetSafetySnapshotAsync(Guid conversationId, CancellationToken cancellationToken)
            => Read(new ConversationSafetySnapshot(conversationId, 0, ConversationSafetyState.Active, null, 0));
        public Task SaveDraftAsync(Guid conversationId, Guid? branchId, string content, IReadOnlyList<Guid> attachmentIds, CancellationToken cancellationToken) => throw Unexpected();
        public Task<MessageAttachment> ImportAttachmentAsync(Guid conversationId, Guid? branchId, string path, CancellationToken cancellationToken) => throw Unexpected();
        public Task RemoveAttachmentAsync(Guid conversationId, Guid? branchId, Guid attachmentId, CancellationToken cancellationToken) => throw Unexpected();
        public Task SendAsync(ChatSpaceTurnRequest request, IProgress<ChatStreamEvent>? progress, CancellationToken cancellationToken) => throw Unexpected();
        public Task<ConversationBranch> CreateBranchAsync(Guid conversationId, Guid messageId, string? name, CancellationToken cancellationToken) => throw Unexpected();
        public Task SwitchBranchAsync(Guid conversationId, Guid branchId, CancellationToken cancellationToken) => throw Unexpected();
        public Task RegenerateAsync(Guid conversationId, Guid assistantMessageId, ModelDescriptor model, CancellationToken cancellationToken) => throw Unexpected();
        public Task<string> GetMessageContentAsync(Guid conversationId, Guid messageId, CancellationToken cancellationToken) => throw Unexpected();
        public Task DeleteMessageAsync(Guid conversationId, Guid messageId, CancellationToken cancellationToken) => throw Unexpected();
        public Task ForgetMessageAsync(Guid conversationId, Guid messageId, CancellationToken cancellationToken) => throw Unexpected();
        public Task EditUserMessageAsync(Guid conversationId, Guid messageId, string content, MessageEditMode mode, CancellationToken cancellationToken) => throw Unexpected();
        public Task<DualModelRun> RunDualAsync(string prompt, ModelDescriptor first, string secondModelName, DuoMode mode, CancellationToken cancellationToken) => throw Unexpected();
    }
}
