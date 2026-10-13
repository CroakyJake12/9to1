using Haven.Application;
using Haven.Core;
using HavenOS.Apps.Spaces.Chat;
using Xunit;

namespace HavenOS.Apps.Spaces.Tests;

public sealed class ChatSpaceControllerTests : IDisposable
{
    private readonly CancellationTokenSource _testLifetime = new(TimeSpan.FromSeconds(30));
    public void Dispose() => _testLifetime.Dispose();
    [Fact]
    public async Task Only_original_ready_read_marks_status_idle_and_model_acknowledgement_remains_meaningful()
    {
        var backend = new Backend();
        var controller = new ChatSpaceController(backend);
        await controller.InitializeAsync(backend.Conversation.Id, _testLifetime.Token);
        Assert.True(controller.State.Status.IsIdle);
        controller.SelectModel(controller.State.Models[0].Name);
        Assert.False(controller.State.Status.IsIdle);
        Assert.Equal(ChatSpaceStatusTone.Neutral, controller.State.Status.Tone);
        Assert.True(controller.State.Status.Announce);
        backend.UnavailableReason = "Original catalogue temporarily unavailable";
        await controller.InitializeAsync(backend.Conversation.Id, _testLifetime.Token);
        Assert.False(controller.State.Status.IsIdle);
        Assert.Equal(backend.UnavailableReason, controller.State.Status.Message);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Draft_faulted_original_cancellation_remains_a_fault_even_when_same_owner_token_cancels(bool synchronous)
    {
        var backend = new Backend();
        var controller = new ChatSpaceController(backend);
        await controller.InitializeAsync(backend.Conversation.Id, _testLifetime.Token);
        controller.UpdateDraft("do not lose my draft");
        using var owner = new CancellationTokenSource();
        var originalFailure = new OperationCanceledException("foreign fault with original token", owner.Token);
        var held = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        backend.DraftWrite = () =>
        {
            if (synchronous) { owner.Cancel(); throw originalFailure; }
            return held.Task;
        };
        var save = controller.SaveDraftAsync(owner.Token);
        if (!synchronous)
        {
            Assert.False(save.IsCompleted);
            owner.Cancel();
            held.SetException(originalFailure); // faulted, never SetCanceled
        }
        var error = await Assert.ThrowsAsync<AggregateException>(() => save);
        Assert.True(save.IsFaulted);
        Assert.Same(originalFailure, Assert.Single(error.InnerExceptions));
        Assert.Equal("do not lose my draft", controller.State.Composer.Draft);
        Assert.False(controller.IsBusy);
    }

    [Fact]
    public async Task Initialization_joins_held_read_and_preserves_every_original_read_failure()
    {
        var backend = new Backend();
        var held = new TaskCompletionSource<IReadOnlyList<Conversation>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var late = new IOException("held recent read failed");
        var model = new UnauthorizedAccessException("model account refused");
        var foreign = new AggregateException();
        backend.RecentRead = () => held.Task;
        backend.ModelRead = () => Task.FromException<ChatSpaceModelInventory>(model);
        backend.ConversationRead = _ => throw foreign;
        var controller = new ChatSpaceController(backend);
        var read = controller.InitializeAsync(backend.Conversation.Id, _testLifetime.Token);
        Assert.False(read.IsCompleted);
        Assert.True(controller.State.IsLoading);
        held.SetException(late);
        var error = await Assert.ThrowsAsync<AggregateException>(() => read);
        Assert.Equal(3, error.InnerExceptions.Count);
        Assert.Single(error.InnerExceptions, cause => ReferenceEquals(cause, late));
        Assert.Single(error.InnerExceptions, cause => ReferenceEquals(cause, model));
        Assert.Single(error.InnerExceptions, cause => ReferenceEquals(cause, foreign));
        Assert.Null(controller.State.Conversation);
        Assert.False(controller.State.IsLoading);
        Assert.False(controller.IsBusy);
    }

    [Fact]
    public async Task Faulted_read_cancellation_is_not_laundered_by_a_genuinely_canceled_sibling()
    {
        var backend = new Backend();
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        var originalFault = new OperationCanceledException("faulted original history", cancelled.Token);
        backend.RecentRead = () => Task.FromCanceled<IReadOnlyList<Conversation>>(cancelled.Token);
        backend.ConversationRead = _ => Task.FromException<ChatSpaceConversationData>(originalFault);
        var controller = new ChatSpaceController(backend);
        var read = controller.InitializeAsync(backend.Conversation.Id, cancelled.Token);
        var error = await Assert.ThrowsAsync<AggregateException>(() => read);
        Assert.True(read.IsFaulted);
        Assert.Same(originalFault, Assert.Single(error.InnerExceptions));
        Assert.False(controller.State.IsLoading);
    }

    [Fact]
    public async Task Unavailable_model_catalog_preserves_real_saved_chat_and_unsent_draft_without_enabling_send()
    {
        var backend = new Backend
        {
            UnavailableReason = "Models are temporarily unavailable.",
            Draft = null
        };
        backend.Draft = new(backend.Conversation.Id, null, "saved original draft", "[]", DateTimeOffset.UtcNow);
        var controller = new ChatSpaceController(backend);
        await controller.InitializeAsync(backend.Conversation.Id, _testLifetime.Token);
        Assert.Equal(backend.Conversation.Id, controller.State.Conversation!.Id);
        Assert.Equal(backend.Conversation.Id, Assert.Single(controller.State.RecentChats).Id);
        Assert.Equal("saved original draft", controller.State.Composer.Draft);
        Assert.Empty(controller.State.Models);
        Assert.Null(controller.State.Composer.SelectedModelName);
        Assert.False(controller.State.Composer.CanSend);
        Assert.Equal(backend.UnavailableReason, controller.State.Status.Message);
        Assert.Equal(ChatSpaceStatusTone.Neutral, controller.State.Status.Tone);
    }

    [Fact]
    public async Task Initial_draft_first_load_failure_retries_the_same_created_identity()
    {
        var backend = new Backend();
        var controller = new ChatSpaceController(backend);
        await controller.InitializeAsync(cancellationToken: _testLifetime.Token);
        controller.UpdateDraft("keep this initial draft");
        backend.ReadFailure = new IOException("first load failed after creation committed");
        Assert.Same(backend.ReadFailure, await Assert.ThrowsAsync<IOException>(() => controller.SaveDraftAsync(_testLifetime.Token)));
        Assert.Equal(1, backend.Creates);
        backend.ReadFailure = null;
        await controller.SaveDraftAsync(_testLifetime.Token);
        Assert.Equal(1, backend.Creates);
        Assert.Equal(backend.Conversation.Id, controller.State.Conversation!.Id);
        Assert.Equal("keep this initial draft", backend.Draft!.Content);
    }

    [Fact]
    public async Task Completed_turn_with_failed_draft_cleanup_retries_only_cleanup_and_never_replays_provider()
    {
        var backend = new Backend { CleanupFailure = new IOException("lost cleanup acknowledgement") };
        backend.Release.TrySetResult();
        var token = _testLifetime.Token;
        var controller = new ChatSpaceController(backend);
        await controller.InitializeAsync(backend.Conversation.Id, token);
        controller.UpdateDraft("execute exactly once");
        Assert.Same(backend.CleanupFailure, await Assert.ThrowsAsync<IOException>(() => controller.SendAsync(token)));
        Assert.True(controller.HasPendingTurnCleanup);
        await Assert.ThrowsAsync<InvalidOperationException>(() => controller.SendAsync(token));
        Assert.Equal(1, backend.Sends);
        backend.CleanupFailure = null;
        await controller.RetryPendingTurnCleanupAsync(token);
        Assert.Equal(1, backend.Sends);
        Assert.Empty(controller.State.Composer.Draft);
        Assert.False(controller.RequiresTurnReconciliation);
    }

    [Fact]
    public async Task Initial_unsent_draft_is_saved_to_an_actual_conversation_and_reopened()
    {
        var backend = new Backend();
        var token = _testLifetime.Token;
        var first = new ChatSpaceController(backend);
        await first.InitializeAsync(cancellationToken: token);
        first.UpdateDraft("initial unsent draft");
        await first.SaveDraftAsync(token);
        Assert.Equal(backend.Conversation.Id, first.State.Conversation!.Id);
        Assert.Equal("initial unsent draft", backend.Draft!.Content);
        var reopened = new ChatSpaceController(backend);
        await reopened.InitializeAsync(backend.Conversation.Id, token);
        Assert.Equal("initial unsent draft", reopened.State.Composer.Draft);
    }

    [Fact]
    public async Task Actual_conversation_stream_and_newer_draft_survive_held_send_without_destination_race()
    {
        var backend = new Backend();
        var controller = new ChatSpaceController(backend);
        var token = _testLifetime.Token;
        await controller.InitializeAsync(backend.Conversation.Id, token);
        controller.UpdateDraft("first instruction");
        var send = controller.SendAsync(token);
        await backend.Started.Task.WaitAsync(token);
        Assert.Same(backend.Conversation, backend.Request!.Conversation);
        Assert.Equal("Actual streamed text", Assert.Single(controller.State.Conversation!.Messages).Content);
        Assert.True(controller.State.Composer.IsSending);
        await Assert.ThrowsAsync<InvalidOperationException>(() => controller.OpenChatAsync(Guid.NewGuid(), token));
        controller.UpdateDraft("next draft");
        backend.Release.TrySetResult();
        await send;
        Assert.Equal("next draft", controller.State.Composer.Draft);
        Assert.Equal("next draft", backend.Draft!.Content);
        Assert.False(controller.State.Composer.IsSending);
        Assert.False(controller.IsBusy);
        Assert.Equal(backend.Conversation.Id, controller.State.Conversation.Id);
    }

    [Fact]
    public async Task New_chat_send_retains_original_composer_instead_of_sending_an_empty_new_draft()
    {
        var backend = new Backend(); backend.Release.TrySetResult();
        var controller = new ChatSpaceController(backend);
        var token = _testLifetime.Token;
        await controller.InitializeAsync(cancellationToken: token);
        controller.UpdateDraft("create and send");
        await controller.SendAsync(token);
        Assert.Equal("create and send", backend.Request!.Prompt);
        Assert.Same(backend.Conversation, backend.Request.Conversation);
        Assert.Empty(controller.State.Composer.Draft);
    }

    [Fact]
    public async Task Actual_provider_failure_is_not_success_and_does_not_clear_the_draft()
    {
        var backend = new Backend { Failure = new IOException("original provider failed") };
        backend.Release.TrySetResult();
        var controller = new ChatSpaceController(backend);
        var token = _testLifetime.Token;
        await controller.InitializeAsync(backend.Conversation.Id, token);
        controller.UpdateDraft("retain this");
        Assert.Same(backend.Failure, await Assert.ThrowsAsync<IOException>(() => controller.SendAsync(token)));
        Assert.Equal("retain this", controller.State.Composer.Draft);
        Assert.Equal(ChatSpaceStatusTone.Error, controller.State.Status.Tone);
        Assert.False(controller.IsBusy);
        Assert.False(controller.State.Composer.IsSending);
    }

    [Fact]
    public async Task Opening_another_chat_waits_for_actual_original_draft_save()
    {
        var backend = new Backend();
        var controller = new ChatSpaceController(backend);
        var token = _testLifetime.Token;
        await controller.InitializeAsync(backend.Conversation.Id, token);
        controller.UpdateDraft("preserve before navigation");
        backend.DraftFailure = new IOException("draft write refused");
        Assert.Same(backend.DraftFailure, await Assert.ThrowsAsync<IOException>(() => controller.OpenChatAsync(Guid.NewGuid(), token)));
        Assert.Equal(backend.Conversation.Id, controller.State.Conversation!.Id);
        Assert.Equal("preserve before navigation", controller.State.Composer.Draft);
    }

    private sealed class Backend : IChatSpaceStreamingBackend
    {
        internal readonly Conversation Conversation = new(Guid.NewGuid(), HavenMode.Chat, ConversationKind.Chat,
            "Actual identity", null, null, true, false, DateTimeOffset.UtcNow.AddDays(-3), DateTimeOffset.UtcNow,
            ParentConversationId: Guid.NewGuid(), SpaceId: SpaceRegistry.ChatSpaceId);
        private readonly ModelDescriptor _model = new("controlled", 1, "test", "test", "test", new HashSet<ToolCapability> { ToolCapability.Text }, DateTimeOffset.UtcNow);
        internal readonly TaskCompletionSource Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal ChatSpaceTurnRequest? Request;
        internal ConversationDraft? Draft;
        internal Exception? Failure;
        internal Exception? DraftFailure;
        internal Exception? CleanupFailure;
        internal int Sends;
        internal int Creates;
        internal Exception? ReadFailure;
        internal string? UnavailableReason;
        internal Func<Task>? DraftWrite;
        internal Func<Task<IReadOnlyList<Conversation>>>? RecentRead;
        internal Func<Task<ChatSpaceModelInventory>>? ModelRead;
        internal Func<Guid, Task<ChatSpaceConversationData>>? ConversationRead;
        private ChatMessage? _response;
        public Task<IReadOnlyList<Conversation>> GetRecentChatsAsync(int limit, CancellationToken token) => RecentRead is { } read ? read() : Task.FromResult<IReadOnlyList<Conversation>>([Conversation]);
        public Task<ChatSpaceModelInventory> GetModelInventoryAsync(CancellationToken token) => ModelRead is { } read ? read() : Task.FromResult(new ChatSpaceModelInventory(UnavailableReason is null ? [_model] : []) { UnavailableReason = UnavailableReason });
        public Task<Conversation> CreateChatAsync(CancellationToken token) { Creates++; return Task.FromResult(Conversation); }
        public Task<ChatSpaceConversationData> GetConversationAsync(Guid id, CancellationToken token)
        {
            if (ConversationRead is { } read) return read(id);
            if (ReadFailure is { } failure) return Task.FromException<ChatSpaceConversationData>(failure);
            if (id != Conversation.Id) throw new InvalidOperationException("Different owner read.");
            return Task.FromResult(new ChatSpaceConversationData(Conversation, _response is null ? [] : [_response], [], [], Draft));
        }
        public Task SaveDraftAsync(Guid id, Guid? branch, string content, IReadOnlyList<Guid> attachments, CancellationToken token)
        {
            if (DraftWrite is { } write) return write();
            if (DraftFailure is { } failure) return Task.FromException(failure);
            if (_response is not null && CleanupFailure is { } cleanup) return Task.FromException(cleanup);
            Draft = new(id, branch, content, "[]", DateTimeOffset.UtcNow); return Task.CompletedTask;
        }
        public async Task SendAsync(ChatSpaceTurnRequest request, Func<ChatStreamEvent,CancellationToken,Task> observe, CancellationToken token)
        {
            Sends++; Request = request; var id = Guid.NewGuid();
            await observe(ChatStreamEvent.AssistantStarted(id, _model.Name, "Controlled"), token);
            await observe(ChatStreamEvent.AssistantDelta(id, "Actual streamed text"), token);
            Started.TrySetResult();
            await Release.Task.WaitAsync(token);
            if (Failure is { } error) throw error;
            _response = new(id, Conversation.Id, MessageRole.Assistant, "Actual streamed text", "Controlled", _model.Name, null, DateTimeOffset.UtcNow);
            await observe(ChatStreamEvent.AssistantCompleted(_response), token);
        }
        public Task SendAsync(ChatSpaceTurnRequest request, CancellationToken token) => throw new InvalidOperationException("Expected actual streaming path.");
        public Task<MessageAttachment> ImportAttachmentAsync(Guid id, Guid? branch, string path, CancellationToken token) => throw new NotSupportedException();
        public Task RemoveAttachmentAsync(Guid id, CancellationToken token) => throw new NotSupportedException();
        public Task<ConversationBranch> CreateBranchAsync(Guid id, Guid messageId, string? name, CancellationToken token) => throw new NotSupportedException();
        public Task SwitchBranchAsync(Guid id, Guid branch, CancellationToken token) => throw new NotSupportedException();
        public Task RegenerateAsync(Guid id, Guid message, ModelDescriptor model, CancellationToken token) => throw new NotSupportedException();
    }
}
