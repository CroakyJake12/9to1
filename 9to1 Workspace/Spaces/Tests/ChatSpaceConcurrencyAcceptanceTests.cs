using System.Text.Json;
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

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task User_stop_marks_assistant_activity_stopped_and_retains_any_partial_text(bool emitPartial)
    {
        var backend = new ControlledBackend { AcceptInputBeforeStream = true, EmitAssistantBeforeWait = true, EmitPartialBeforeWait = emitPartial };
        var id = backend.Seed("Assistant Main", withBranch: true);
        var controller = new ChatSpaceController(backend);
        await controller.OpenChatAsync(id);
        controller.UpdateDraft("Continue the saved work");
        var sending = controller.SendAsync();
        await backend.SendEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(controller.StopActiveResponse());
        await sending;

        var stopped = Assert.Single(controller.State.Conversation!.Messages.Where(message => message.Role == MessageRole.Assistant));
        Assert.Equal(emitPartial ? "A real streamed partial" : string.Empty, stopped.Content);
        Assert.False(stopped.IsStreaming);
        Assert.Equal(emitPartial ? "Stopped · partial response" : "Stopped", stopped.ProgressLabel);
        Assert.Contains(emitPartial ? "partial text was saved" : "Response stopped", controller.State.Status.Message);
        Assert.Equal(ChatSpaceStatusTone.Neutral, controller.State.Status.Tone);
        if (emitPartial) Assert.Contains("accepted user message remains saved", controller.State.Status.Message);
        Assert.False(controller.State.Composer.IsSending);
        var reopened = new ChatSpaceController(backend);
        await reopened.OpenChatAsync(id);
        Assert.Equal(id, reopened.State.Conversation?.Id);
        if (emitPartial)
        {
            var persisted = Assert.Single(backend.PartialWrites);
            Assert.Equal(id, persisted.Conversation);
            Assert.Equal(Assert.Single(backend.Data[id].Branches).Id, persisted.Branch);
            Assert.Equal(JsonValueKind.True, persisted.Message.Metadata["isPartial"].ValueKind);
            Assert.Equal("Stopped", persisted.Message.Metadata["responseState"].GetString());
            var restored = Assert.Single(reopened.State.Conversation!.Messages.Where(message => message.Role == MessageRole.Assistant));
            Assert.Equal(stopped.Id, restored.Id);
            Assert.Equal("A real streamed partial", restored.Content);
            Assert.Equal("model-a", restored.ModelName);
            Assert.Equal("Assistant Main", restored.SpeakerLabel);
            Assert.False(restored.IsStreaming);
            Assert.Equal("Stopped · partial response", restored.ProgressLabel);
        }
        else
        {
            Assert.Empty(backend.PartialWrites);
            Assert.Empty(reopened.State.Conversation!.Messages.Where(message => message.Role == MessageRole.Assistant));
        }
    }

    [Fact]
    public async Task Explicit_stop_before_input_acceptance_recovers_the_draft_without_an_orphan_assistant_message()
    {
        var backend = new ControlledBackend { EmitAssistantBeforeWait = true, EmitPartialBeforeWait = true };
        var id = backend.Seed("Assistant Main", withBranch: true);
        var controller = new ChatSpaceController(backend);
        await controller.OpenChatAsync(id);
        controller.UpdateDraft("Continue the saved work");
        var sending = controller.SendAsync();
        await backend.SendEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(controller.StopActiveResponse());
        await sending;

        var partial = Assert.Single(controller.State.Conversation!.Messages.Where(message => message.Role == MessageRole.Assistant));
        Assert.Equal("A real streamed partial", partial.Content);
        Assert.False(partial.IsStreaming);
        Assert.Equal("Stopped · partial response", partial.ProgressLabel);
        Assert.Contains("Response stopped before the user input was accepted", controller.State.Status.Message);
        Assert.Equal(ChatSpaceStatusTone.Neutral, controller.State.Status.Tone);
        Assert.Contains("Draft content was recovered", controller.State.Status.Message);
        Assert.Equal("Continue the saved work", controller.State.Composer.Draft);
        Assert.Empty(backend.Data[id].Messages);
        Assert.Empty(backend.PartialWrites);
        Assert.Equal(0, backend.PartialPersistenceAttempts);
    }

    [Fact]
    public async Task Accepted_input_with_partial_store_failure_reports_both_truths_without_replay_or_draft_recovery()
    {
        var backend = new ControlledBackend { AcceptInputBeforeStream = true, EmitAssistantBeforeWait = true,
            EmitPartialBeforeWait = true, FailPartialPersistence = true };
        var id = backend.Seed("Assistant Main", withBranch: true);
        var controller = new ChatSpaceController(backend);
        await controller.OpenChatAsync(id);
        controller.UpdateDraft("Accepted once");
        using var stop = new CancellationTokenSource();
        var sending = controller.SendAsync(stop.Token);
        await backend.SendEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(backend.AcceptedBeforeStreaming);
        controller.UpdateDraft("Queued follow-up");
        await controller.SendAsync();
        controller.UpdateDraft("A newer unsent draft");

        stop.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => sending);

        Assert.Single(backend.Sent);
        Assert.Contains(backend.Data[id].Messages, message => message.Role == MessageRole.User && message.Content == "Accepted once");
        Assert.Empty(backend.PartialWrites);
        Assert.Equal(1, backend.PartialPersistenceAttempts);
        Assert.Equal("A newer unsent draft\n\nQueued follow-up", controller.State.Composer.Draft);
        Assert.Equal("A newer unsent draft\n\nQueued follow-up", backend.Data[id].Draft?.Content);
        Assert.DoesNotContain("Accepted once", controller.State.Composer.Draft);
        Assert.Contains("partial text remains visible here but could not be saved", controller.State.Status.Message);
        Assert.Equal("Interrupted · partial response", Assert.Single(controller.State.Conversation!.Messages,
            message => message.Role == MessageRole.Assistant).ProgressLabel);
        Assert.Equal(ChatSpaceStatusTone.Error, controller.State.Status.Tone);
        Assert.Contains("accepted user message remains saved and will not be replayed automatically", controller.State.Status.Message);
        Assert.DoesNotContain("draft content was recovered", controller.State.Status.Message);
        Assert.Equal("A real streamed partial", Assert.Single(controller.State.Conversation!.Messages.Where(message => message.Role == MessageRole.Assistant)).Content);
    }

    [Fact]
    public async Task Accepted_input_with_successful_partial_store_reopens_once_without_replaying_the_user_message()
    {
        var backend = new ControlledBackend { AcceptInputBeforeStream = true, EmitAssistantBeforeWait = true, EmitPartialBeforeWait = true };
        var id = backend.Seed("Assistant Main", withBranch: true);
        var controller = new ChatSpaceController(backend);
        await controller.OpenChatAsync(id);
        controller.UpdateDraft("Accepted once");
        using var stop = new CancellationTokenSource();
        var sending = controller.SendAsync(stop.Token);
        await backend.SendEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        stop.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => sending);

        Assert.Single(backend.Sent);
        Assert.Equal(1, backend.PartialPersistenceAttempts);
        Assert.Equal("Interrupted", Assert.Single(backend.PartialWrites).Message.Metadata["responseState"].GetString());
        Assert.Equal(1, backend.Data[id].Messages.Count(message => message.Role == MessageRole.User && message.Content == "Accepted once"));
        Assert.Equal(1, backend.Data[id].Messages.Count(message => message.Role == MessageRole.Assistant && message.Content == "A real streamed partial"));
        Assert.Contains("partial text was saved", controller.State.Status.Message);
        Assert.Contains("accepted user message remains saved and will not be replayed automatically", controller.State.Status.Message);
        Assert.Equal(ChatSpaceStatusTone.Neutral, controller.State.Status.Tone);
        Assert.DoesNotContain("Accepted once", controller.State.Composer.Draft);

        var reopened = new ChatSpaceController(backend);
        await reopened.OpenChatAsync(id);
        Assert.Equal(id, reopened.State.Conversation?.Id);
        Assert.Equal(2, reopened.State.Conversation!.Messages.Count);
        Assert.Single(reopened.State.Conversation.Messages, message => message.Role == MessageRole.Assistant && message.ProgressLabel == "Interrupted · partial response");
    }

    [Fact]
    public async Task User_stop_action_cancels_the_active_request_and_persists_its_partial_reply()
    {
        var backend = new ControlledBackend { AcceptInputBeforeStream = true, EmitAssistantBeforeWait = true, EmitPartialBeforeWait = true };
        var id = backend.Seed("Assistant Main", withBranch: true);
        var controller = new ChatSpaceController(backend);
        await controller.OpenChatAsync(id);
        controller.UpdateDraft("Stop me after the first delta");
        var sending = controller.SendAsync();
        await backend.SendEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(controller.StopActiveResponse());
        await sending;

        Assert.Single(backend.Sent);
        Assert.Single(backend.PartialWrites);
        Assert.Equal("Stopped · partial response", Assert.Single(controller.State.Conversation!.Messages,
            message => message.Role == MessageRole.Assistant).ProgressLabel);
        Assert.False(controller.State.Composer.IsSending);
    }

    [Fact]
    public async Task Stop_before_backend_dispatch_recovers_the_original_draft_without_sending()
    {
        var backend = new ControlledBackend { HoldFirstDraftSave = true };
        var id = backend.Seed("Assistant Main", withBranch: true);
        var controller = new ChatSpaceController(backend);
        await controller.OpenChatAsync(id);
        controller.UpdateDraft("Keep this unsubmitted input");
        var sending = controller.SendAsync();
        await backend.FirstDraftSaveEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(controller.StopActiveResponse());
        await sending;

        Assert.Empty(backend.Sent);
        Assert.Equal("Keep this unsubmitted input", controller.State.Composer.Draft);
        Assert.Equal("Keep this unsubmitted input", backend.Data[id].Draft?.Content);
        Assert.Contains("Response stopped before the user input was accepted", controller.State.Status.Message);
        Assert.Empty(backend.PartialWrites);
        Assert.False(controller.State.Composer.IsSending);
    }

    [Fact]
    public async Task Cancellation_during_post_completion_refresh_does_not_persist_a_completed_reply_as_partial()
    {
        using var cancelOnCompletion = new CancellationTokenSource();
        var backend = new ControlledBackend { EmitAssistantBeforeWait = true, EmitPartialBeforeWait = true,
            CompleteResponseBeforeReturn = true, CancelOnCompletion = cancelOnCompletion };
        var id = backend.Seed("Assistant Main", withBranch: true);
        var controller = new ChatSpaceController(backend);
        await controller.OpenChatAsync(id);
        controller.UpdateDraft("Question");
        var sending = controller.SendAsync(cancelOnCompletion.Token);
        await backend.SendEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        backend.ReleaseSend.TrySetResult();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => sending);

        Assert.Empty(backend.PartialWrites);
        var visible = Assert.Single(controller.State.Conversation!.Messages.Where(message => message.Role == MessageRole.Assistant));
        Assert.Equal("The complete answer", visible.Content);
        Assert.False(visible.IsStreaming);
        Assert.Contains("response completed", controller.State.Status.Message);
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
        public bool EmitAssistantBeforeWait { get; init; }
        public bool EmitPartialBeforeWait { get; init; }
        public bool FailPartialPersistence { get; init; }
        public bool HoldFirstDraftSave { get; init; }
        public bool AcceptInputBeforeStream { get; init; }
        public bool AcceptedBeforeStreaming { get; private set; }
        public bool CompleteResponseBeforeReturn { get; init; }
        public CancellationTokenSource? CancelOnCompletion { get; init; }
        public int Creates { get; private set; }
        private bool _sent;
        public TaskCompletionSource CreateEntered { get; } = Signal();
        public TaskCompletionSource ReleaseCreate { get; } = Signal();
        public TaskCompletionSource RegenerateEntered { get; } = Signal();
        public TaskCompletionSource ReleaseRegenerate { get; } = Signal();
        public List<(Guid Conversation, IReadOnlyList<Guid> Attachments)> DraftWrites { get; } = [];
        public List<(Guid Conversation, Guid Branch, ChatMessage Message)> PartialWrites { get; } = [];
        public int PartialPersistenceAttempts { get; private set; }
        public TaskCompletionSource SendEntered { get; } = Signal();
        public TaskCompletionSource ReleaseSend { get; } = Signal();
        public TaskCompletionSource FirstDraftSaveEntered { get; } = Signal();
        public TaskCompletionSource ReleaseFirstDraftSave { get; } = Signal();
        private int _draftSaveCalls;
        public Func<Guid, Task<ConversationSafetySnapshot>> Safety { get; set; } = id => Task.FromResult(Active(id));
        public Func<Guid, Guid?, string, Task<MessageAttachment>> Import { get; set; } = (_, _, _) => throw new NotSupportedException();
        public Guid Seed(string title, bool withBranch = false)
        {
            var now = DateTimeOffset.UtcNow; var id = Guid.NewGuid();
            ConversationBranch[] branches = withBranch
                ? [new ConversationBranch(Guid.NewGuid(), id, null, null, "Main", ConversationBranchReason.Root, true, now, now)]
                : [];
            Data[id] = new(new(id, HavenMode.Chat, ConversationKind.Chat, title, null, null, false, false, now, now,
                SpaceId: SpaceRegistry.ChatSpaceId), [], [], branches, null);
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
        public Task<ChatSpaceConversationData> GetConversationAsync(Guid conversationId, CancellationToken cancellationToken)
        {
            if (cancellationToken.IsCancellationRequested) return Task.FromCanceled<ChatSpaceConversationData>(cancellationToken);
            return _sent && FailReadAfterSend ? Task.FromException<ChatSpaceConversationData>(new IOException("Refresh unavailable")) : Task.FromResult(Data[conversationId]);
        }
        public Task<ConversationSafetySnapshot> GetSafetySnapshotAsync(Guid conversationId, CancellationToken cancellationToken) => Safety(conversationId);
        public async Task SaveDraftAsync(Guid conversationId, Guid? branchId, string content, IReadOnlyList<Guid> attachmentIds, CancellationToken cancellationToken)
        {
            var call = Interlocked.Increment(ref _draftSaveCalls);
            if (HoldFirstDraftSave && call == 1)
            {
                FirstDraftSaveEntered.TrySetResult();
                await ReleaseFirstDraftSave.Task.WaitAsync(cancellationToken);
            }
            DraftWrites.Add((conversationId, attachmentIds.ToArray()));
            Data[conversationId] = Data[conversationId] with { Draft = new(conversationId, branchId, content, "[]", DateTimeOffset.UtcNow) };
        }
        public Task<MessageAttachment> ImportAttachmentAsync(Guid conversationId, Guid? branchId, string path, CancellationToken cancellationToken) => Import(conversationId, branchId, path);
        public Task RemoveAttachmentAsync(Guid conversationId, Guid? branchId, Guid attachmentId, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task PersistStoppedPartialAsync(Guid conversationId, Guid branchId, ChatMessage acceptedInput, ChatMessage message, CancellationToken cancellationToken)
        {
            PartialPersistenceAttempts++;
            if (FailPartialPersistence) return Task.FromException(new IOException("Controlled persistence failure"));
            var current = Data[conversationId];
            if (acceptedInput.ConversationId != conversationId || acceptedInput.Role != MessageRole.User ||
                current.Messages.LastOrDefault(item => item.Role == MessageRole.User) != acceptedInput ||
                !current.Branches.Any(branch => branch.Id == branchId && branch.IsCurrent))
                throw new InvalidOperationException("The original branch is no longer current.");
            if (current.Messages.Any(existing => existing.Id == message.Id))
                throw new InvalidOperationException("The message ID already exists.");
            PartialWrites.Add((conversationId, branchId, message));
            Data[conversationId] = current with { Messages = current.Messages.Append(message).ToArray() };
            return Task.CompletedTask;
        }
        public async Task SendAsync(ChatSpaceTurnRequest request, IProgress<ChatStreamEvent>? progress, CancellationToken cancellationToken)
        {
            Sent.Add(request); DraftAtSend.Add(Data[request.Conversation.Id].Draft?.Content);
            var user = new ChatMessage(Guid.NewGuid(), request.Conversation.Id, MessageRole.User, request.Prompt, null, null, null, DateTimeOffset.UtcNow);
            // The presentation event alone never asserts that the original store accepted input.
            progress?.Report(ChatStreamEvent.User(user));
            if (AcceptInputBeforeStream && request.InputAccepted is { } acceptBeforeStream)
            {
                await acceptBeforeStream(user, cancellationToken);
                AcceptedBeforeStreaming = true;
                Data[request.Conversation.Id] = Data[request.Conversation.Id] with
                    { Messages = Data[request.Conversation.Id].Messages.Append(user).ToArray() };
            }
            if (EmitAssistantBeforeWait)
            {
                var responseId = Guid.NewGuid();
                progress?.Report(ChatStreamEvent.AssistantStarted(responseId, request.Model.Name, "Assistant Main"));
                if (EmitPartialBeforeWait)
                    progress?.Report(ChatStreamEvent.AssistantDelta(responseId, "A real streamed partial"));
            }
            SendEntered.TrySetResult(); await ReleaseSend.Task.WaitAsync(cancellationToken);
            if (AcceptBeforeFailure && request.InputAccepted is { } accepted) await accepted(user, cancellationToken);
            if (FailGeneration) throw new IOException("Generation failed after the input event");
            if (CompleteResponseBeforeReturn)
            {
                var completed = new ChatMessage(Guid.NewGuid(), request.Conversation.Id, MessageRole.Assistant,
                    "The complete answer", "Assistant Main", request.Model.Name, null, DateTimeOffset.UtcNow);
                progress?.Report(ChatStreamEvent.AssistantCompleted(completed));
                _sent = true;
                CancelOnCompletion?.Cancel();
                return;
            }
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
