using Haven.Application;
using Haven.Core;
using HavenOS.Apps.Spaces.Chat;
using Xunit;

namespace HavenOS.Apps.Spaces.Tests;

/// <summary>Independent acceptance for navigation while canonical operations are outstanding.</summary>
public sealed class ChatSpaceConcurrencyAcceptanceTests
{
    [Fact]
    public async Task Canonical_preflight_refusal_is_not_reported_as_complete_and_does_not_start_the_queued_turn()
    {
        var backend = new ControlledBackend { EmitPreflightFailure = true, AcceptBeforeFailure = true };
        var id = backend.Seed("Chat"); var controller = new ChatSpaceController(backend);
        await controller.OpenChatAsync(id); controller.UpdateDraft("Accepted input");
        var sending = controller.SendAsync();
        await backend.SendEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        controller.UpdateDraft("Queued next question"); await controller.SendAsync();
        backend.ReleaseSend.TrySetResult(); await sending;
        Assert.Single(backend.Sent);
        Assert.Equal(ChatSpaceStatusTone.Error, controller.State.Status.Tone);
        Assert.Contains("Vision support is required", controller.State.Status.Message);
        Assert.DoesNotContain("Response complete", controller.State.Status.Message);
        Assert.Equal("Queued next question", controller.State.Composer.Draft);
        Assert.False(controller.State.Composer.IsSending);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Only_committed_input_acknowledgement_prevents_recovery_after_failed_generation(bool accepted)
    {
        var backend = new ControlledBackend { FailGeneration = true, AcceptBeforeFailure = accepted };
        var id = backend.Seed("Chat");
        var controller = new ChatSpaceController(backend);
        await controller.OpenChatAsync(id); controller.UpdateDraft("Original question");
        var sending = controller.SendAsync();
        await backend.SendEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        controller.UpdateDraft("Queued follow-up"); await controller.SendAsync();
        controller.UpdateDraft("Unsubmitted text");
        backend.ReleaseSend.TrySetResult(); await sending;
        Assert.Single(backend.Sent);
        Assert.Contains("Queued follow-up", controller.State.Composer.Draft);
        Assert.Contains("Unsubmitted text", controller.State.Composer.Draft);
        Assert.Equal(!accepted, controller.State.Composer.Draft.Contains("Original question", StringComparison.Ordinal));
        Assert.False(controller.State.Composer.IsSending);
        Assert.Equal(ChatSpaceStatusTone.Error, controller.State.Status.Tone);
    }

    [Fact]
    public async Task Creating_a_new_chat_preserves_the_previous_unsent_draft()
    {
        var backend = new ControlledBackend(); var first = backend.Seed("First");
        var controller = new ChatSpaceController(backend);
        await controller.OpenChatAsync(first);
        controller.UpdateDraft("Keep this original draft");
        await controller.NewChatAsync();
        Assert.NotEqual(first, controller.State.Conversation?.Id);
        Assert.Equal("Keep this original draft", backend.Data[first].Draft?.Content);
        await controller.OpenChatAsync(first);
        Assert.Equal("Keep this original draft", controller.State.Composer.Draft);
    }

    [Fact]
    public async Task Concurrent_first_send_creates_one_chat_and_preserves_the_submitted_draft()
    {
        var backend = new ControlledBackend { HoldCreation = true };
        var controller = new ChatSpaceController(backend);
        await controller.InitializeAsync();
        controller.UpdateDraft("First question from an empty Chat Space");
        var first = controller.SendAsync();
        await backend.CreateEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var second = controller.SendAsync();
        backend.ReleaseCreate.TrySetResult();
        backend.ReleaseSend.TrySetResult();
        await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(1, backend.Creates);
        Assert.Equal("First question from an empty Chat Space", Assert.Single(backend.Sent).Prompt);
        Assert.Empty(controller.State.Composer.Draft);
    }

    [Fact]
    public async Task Acknowledged_send_is_not_recovered_for_replay_when_the_following_refresh_fails()
    {
        var backend = new ControlledBackend { FailReadAfterSend = true };
        var id = backend.Seed("Chat");
        var controller = new ChatSpaceController(backend);
        await controller.OpenChatAsync(id); controller.UpdateDraft("Already submitted question");
        var sending = controller.SendAsync();
        await backend.SendEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        controller.UpdateDraft("New unsent draft");
        backend.ReleaseSend.TrySetResult();
        await sending;
        Assert.Single(backend.Sent);
        Assert.Equal("New unsent draft", controller.State.Composer.Draft);
        Assert.False(controller.State.Composer.IsSending);
        Assert.Equal(ChatSpaceStatusTone.Error, controller.State.Status.Tone);
    }

    [Fact]
    public async Task Starting_a_queued_turn_does_not_clear_a_newer_persisted_draft()
    {
        var backend = new ControlledBackend(); var id = backend.Seed("Chat");
        var controller = new ChatSpaceController(backend);
        await controller.OpenChatAsync(id); controller.UpdateDraft("First submitted question");
        var sending = controller.SendAsync();
        await backend.SendEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        controller.UpdateDraft("Queued follow-up"); await controller.SendAsync();
        controller.UpdateDraft("Not submitted yet"); await controller.SaveDraftAsync();
        backend.ReleaseSend.TrySetResult(); await sending;
        Assert.Equal(2, backend.Sent.Count);
        Assert.Equal("Not submitted yet", backend.DraftAtSend[1]);
        Assert.Equal("Not submitted yet", backend.Data[id].Draft?.Content);
    }

    [Fact]
    public async Task Regeneration_owns_the_conversation_until_its_canonical_operation_completes()
    {
        var backend = new ControlledBackend(); var id = backend.Seed("Chat");
        var message = new ChatMessage(Guid.NewGuid(), id, MessageRole.Assistant, "Previous answer", "Haven", "model-a", null, DateTimeOffset.UtcNow);
        backend.Data[id] = backend.Data[id] with { Messages = [message] };
        var controller = new ChatSpaceController(backend); await controller.OpenChatAsync(id);
        var regenerating = controller.RegenerateAsync(message.Id);
        await backend.RegenerateEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        try
        {
            controller.UpdateDraft("Next question");
            backend.ReleaseSend.TrySetResult();
            await controller.SendAsync();
            Assert.Empty(backend.Sent);
            Assert.Equal("Next question", controller.State.Composer.Draft);
        }
        finally { backend.ReleaseRegenerate.TrySetResult(); await regenerating; }
        Assert.False(controller.State.Composer.IsSending);
    }

    [Fact]
    public async Task Delayed_safety_result_cannot_replace_the_newer_open_conversation()
    {
        var backend = new ControlledBackend();
        var first = backend.Seed("First"); var second = backend.Seed("Second");
        var entered = Signal(); var release = new TaskCompletionSource<ConversationSafetySnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        backend.Safety = id =>
        {
            if (id != first) return Task.FromResult(Active(id));
            entered.TrySetResult(); return release.Task;
        };
        var controller = new ChatSpaceController(backend);
        var openingFirst = controller.OpenChatAsync(first);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        try
        {
            await controller.OpenChatAsync(second);
            controller.UpdateDraft("Keep the second conversation draft");
            release.TrySetResult(Active(first));
            await openingFirst;
            Assert.Equal(second, controller.State.Conversation?.Id);
            Assert.Equal("Keep the second conversation draft", controller.State.Composer.Draft);
        }
        finally { release.TrySetResult(Active(first)); await openingFirst; }
    }

    [Fact]
    public async Task Completing_an_old_turn_preserves_the_new_conversation_and_its_draft()
    {
        var backend = new ControlledBackend();
        var first = backend.Seed("First"); var second = backend.Seed("Second");
        var controller = new ChatSpaceController(backend);
        await controller.OpenChatAsync(first); controller.UpdateDraft("First question");
        var sending = controller.SendAsync();
        await backend.SendEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        try
        {
            await controller.OpenChatAsync(second);
            controller.UpdateDraft("A private draft in second");
            backend.ReleaseSend.TrySetResult(); await sending;
            Assert.Equal(first, Assert.Single(backend.Sent).Conversation.Id);
            Assert.Equal(second, controller.State.Conversation?.Id);
            Assert.Equal("A private draft in second", controller.State.Composer.Draft);
        }
        finally { backend.ReleaseSend.TrySetResult(); await sending; }
    }

    [Fact]
    public async Task Queued_message_cannot_be_retargeted_to_a_newly_opened_conversation()
    {
        var backend = new ControlledBackend();
        var first = backend.Seed("First"); var second = backend.Seed("Second");
        var controller = new ChatSpaceController(backend);
        await controller.OpenChatAsync(first); controller.UpdateDraft("First question");
        var sending = controller.SendAsync();
        await backend.SendEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        try
        {
            controller.UpdateDraft("Follow-up intended only for first");
            await controller.SendAsync();
            await controller.OpenChatAsync(second);
            controller.UpdateDraft("Keep second draft");
            backend.ReleaseSend.TrySetResult(); await sending;
            Assert.All(backend.Sent, request => Assert.Equal(first, request.Conversation.Id));
            Assert.Equal(second, controller.State.Conversation?.Id);
            Assert.Equal("Keep second draft", controller.State.Composer.Draft);
        }
        finally { backend.ReleaseSend.TrySetResult(); await sending; }
    }

    [Fact]
    public async Task Finishing_a_turn_does_not_erase_text_typed_while_it_was_running()
    {
        var backend = new ControlledBackend(); var id = backend.Seed("First");
        var controller = new ChatSpaceController(backend);
        await controller.OpenChatAsync(id); controller.UpdateDraft("Submitted question");
        var sending = controller.SendAsync();
        await backend.SendEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        try
        {
            controller.UpdateDraft("Unsubmitted next question");
            backend.ReleaseSend.TrySetResult(); await sending;
            Assert.Equal("Submitted question", Assert.Single(backend.Sent).Prompt);
            Assert.Equal("Unsubmitted next question", controller.State.Composer.Draft);
            await controller.SaveDraftAsync();
            Assert.Equal("Unsubmitted next question", backend.Data[id].Draft?.Content);
        }
        finally { backend.ReleaseSend.TrySetResult(); await sending; }
    }

    [Fact]
    public async Task Delayed_attachment_import_cannot_attach_first_chat_content_to_second()
    {
        var backend = new ControlledBackend(); var first = backend.Seed("First"); var second = backend.Seed("Second");
        var entered = Signal(); var release = new TaskCompletionSource<MessageAttachment>(TaskCreationOptions.RunContinuationsAsynchronously);
        backend.Import = (_, _, _) => { entered.TrySetResult(); return release.Task; };
        var controller = new ChatSpaceController(backend); await controller.OpenChatAsync(first);
        var importing = controller.AddAttachmentAsync("/fixture/private-notes.txt");
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var now = DateTimeOffset.UtcNow;
        var attachment = new MessageAttachment(Guid.NewGuid(), first, null, null, "private-notes.txt", "stored/private-notes.txt", "text/plain",
            MessageAttachmentKind.PlainText, 10, new string('a', 64), AttachmentProcessingState.Ready, AttachmentAnalysisMethod.None,
            string.Empty, "{}", now, now);
        try
        {
            await controller.OpenChatAsync(second); controller.UpdateDraft("Second draft");
            release.TrySetResult(attachment); await importing;
            Assert.Equal(second, controller.State.Conversation?.Id);
            Assert.Empty(controller.State.Composer.Attachments);
            Assert.Equal("Second draft", controller.State.Composer.Draft);
            Assert.DoesNotContain(backend.DraftWrites, write => write.Conversation == second && write.Attachments.Contains(attachment.Id));
        }
        finally { release.TrySetResult(attachment); await importing; }
    }

    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static ConversationSafetySnapshot Active(Guid id) => new(id, 0, ConversationSafetyState.Active, null, 0);

    private sealed class ControlledBackend : IChatSpaceBackend
    {
        public Dictionary<Guid, ChatSpaceConversationData> Data { get; } = [];
        public List<ChatSpaceTurnRequest> Sent { get; } = [];
        public List<string?> DraftAtSend { get; } = [];
        public bool HoldCreation { get; init; }
        public bool FailReadAfterSend { get; init; }
        public bool FailGeneration { get; init; }
        public bool AcceptBeforeFailure { get; init; }
        public bool EmitPreflightFailure { get; init; }
        public int Creates { get; private set; }
        private bool _sent;
        public TaskCompletionSource CreateEntered { get; } = Signal();
        public TaskCompletionSource ReleaseCreate { get; } = Signal();
        public TaskCompletionSource RegenerateEntered { get; } = Signal();
        public TaskCompletionSource ReleaseRegenerate { get; } = Signal();
        public List<(Guid Conversation, IReadOnlyList<Guid> Attachments)> DraftWrites { get; } = [];
        public TaskCompletionSource SendEntered { get; } = Signal();
        public TaskCompletionSource ReleaseSend { get; } = Signal();
        public Func<Guid, Task<ConversationSafetySnapshot>> Safety { get; set; } = id => Task.FromResult(Active(id));
        public Func<Guid, Guid?, string, Task<MessageAttachment>> Import { get; set; } = (_, _, _) => throw new NotSupportedException();
        public Guid Seed(string title)
        {
            var now = DateTimeOffset.UtcNow; var id = Guid.NewGuid();
            Data[id] = new(new(id, HavenMode.Chat, ConversationKind.Chat, title, null, null, false, false, now, now,
                SpaceId: SpaceRegistry.ChatSpaceId), [], [], [], null);
            return id;
        }
        public Task<IReadOnlyList<Conversation>> GetRecentChatsAsync(int limit, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Conversation>>(Data.Values.Select(value => value.Conversation).ToArray());
        public Task<ChatSpaceModelInventory> GetModelInventoryAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new ChatSpaceModelInventory([new("model-a", 1_000_000, "llama", "7B", "Q4", new HashSet<ToolCapability>(), DateTimeOffset.UtcNow)], "model-a"));
        public async Task<Conversation> CreateChatAsync(CancellationToken cancellationToken)
        {
            Creates++; CreateEntered.TrySetResult();
            if (HoldCreation) await ReleaseCreate.Task.WaitAsync(cancellationToken);
            return Data[Seed("New")].Conversation;
        }
        public Task<ChatSpaceConversationData> GetConversationAsync(Guid conversationId, CancellationToken cancellationToken) =>
            _sent && FailReadAfterSend ? Task.FromException<ChatSpaceConversationData>(new IOException("Refresh unavailable")) : Task.FromResult(Data[conversationId]);
        public Task<ConversationSafetySnapshot> GetSafetySnapshotAsync(Guid conversationId, CancellationToken cancellationToken) => Safety(conversationId);
        public Task SaveDraftAsync(Guid conversationId, Guid? branchId, string content, IReadOnlyList<Guid> attachmentIds, CancellationToken cancellationToken)
        {
            DraftWrites.Add((conversationId, attachmentIds.ToArray()));
            Data[conversationId] = Data[conversationId] with { Draft = new(conversationId, branchId, content, "[]", DateTimeOffset.UtcNow) };
            return Task.CompletedTask;
        }
        public Task<MessageAttachment> ImportAttachmentAsync(Guid conversationId, Guid? branchId, string path, CancellationToken cancellationToken) => Import(conversationId, branchId, path);
        public Task RemoveAttachmentAsync(Guid conversationId, Guid? branchId, Guid attachmentId, CancellationToken cancellationToken) => Task.CompletedTask;
        public async Task SendAsync(ChatSpaceTurnRequest request, IProgress<ChatStreamEvent>? progress, CancellationToken cancellationToken)
        {
            Sent.Add(request); DraftAtSend.Add(Data[request.Conversation.Id].Draft?.Content);
            var user = new ChatMessage(Guid.NewGuid(), request.Conversation.Id, MessageRole.User, request.Prompt, null, null, null, DateTimeOffset.UtcNow);
            // The presentation event alone never asserts that the original store accepted input.
            progress?.Report(ChatStreamEvent.User(user));
            SendEntered.TrySetResult(); await ReleaseSend.Task.WaitAsync(cancellationToken);
            if (AcceptBeforeFailure && request.InputAccepted is { } accepted) await accepted(user, cancellationToken);
            if (FailGeneration) throw new IOException("Generation failed after the input event");
            if (EmitPreflightFailure) progress?.Report(ChatStreamEvent.Preflight(new(false, [], [new(ToolCapability.Vision, "Vision support is required")], null)));
            _sent = true;
        }
        public Task<ConversationBranch> CreateBranchAsync(Guid conversationId, Guid messageId, string? name, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task SwitchBranchAsync(Guid conversationId, Guid branchId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public async Task RegenerateAsync(Guid conversationId, Guid assistantMessageId, ModelDescriptor model, CancellationToken cancellationToken)
        { RegenerateEntered.TrySetResult(); await ReleaseRegenerate.Task.WaitAsync(cancellationToken); }
        public Task<string> GetMessageContentAsync(Guid conversationId, Guid messageId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task DeleteMessageAsync(Guid conversationId, Guid messageId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task ForgetMessageAsync(Guid conversationId, Guid messageId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task EditUserMessageAsync(Guid conversationId, Guid messageId, string content, MessageEditMode mode, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<DualModelRun> RunDualAsync(string prompt, ModelDescriptor first, string secondModelName, DuoMode mode, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
