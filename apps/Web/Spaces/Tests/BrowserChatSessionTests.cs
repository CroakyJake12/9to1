using Haven.Application;
using Haven.Core;
using HavenOS.Apps.Spaces.Chat;
using HavenOS.Home.Core;
using NineToOne.Web.Spaces.Chat;
using NineToOne.Web.Spaces.Storage;
using Xunit;

namespace NineToOne.Web.Spaces.Tests;

public sealed class BrowserChatSessionTests
{
    [Fact]
    public async Task Faulted_presentation_during_account_close_is_not_laundered_as_owned_save_cancellation()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var backend = new RecoveryBackend();
        BrowserChatSession? chat = null;
        Task? close = null;
        OperationCanceledException? original = null;
        var armed = true;
        chat = new BrowserChatSession(() => backend, () => { }, action =>
        {
            if (armed && backend.Draft is not null)
            {
                armed = false;
                original = new OperationCanceledException("Presentation failed during retirement", backend.OriginalSaveToken);
                close = chat!.DisposeAsync().AsTask();
                return Task.FromException(original);
            }
            action(); return Task.CompletedTask;
        }, _ => { });
        await chat.PrepareAsync(timeout.Token); chat.Admit();
        Assert.True(chat.TrySetValue("composer.text", "Keep the committed original draft"));
        while (close is null) await Task.Delay(10, timeout.Token);
        await Assert.ThrowsAnyAsync<Exception>(() => close.WaitAsync(timeout.Token));
        Assert.True(close.IsFaulted);
        Assert.Contains(original!, close.Exception!.Flatten().InnerExceptions);
        Assert.Equal("Keep the committed original draft", backend.Draft!.Content);
    }

    [Fact]
    public async Task Newer_typing_during_held_original_save_is_saved_after_original_acknowledgement()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var backend = new RecoveryBackend { HoldFirstDraft = true };
        await using var chat = new BrowserChatSession(() => backend, () => { },
            action => { action(); return Task.CompletedTask; }, _ => { });
        await chat.PrepareAsync(timeout.Token); chat.Admit();
        Assert.True(chat.TrySetValue("composer.text", "Older draft"));
        await backend.DraftStarted.Task.WaitAsync(timeout.Token);
        Assert.True(chat.TrySetValue("composer.text", "Newer draft while save is pending"));
        backend.ReleaseDraft.TrySetResult();
        while (chat.HasUnsavedChanges) await Task.Delay(10, timeout.Token);
        Assert.Equal("Newer draft while save is pending", backend.Draft!.Content);
        Assert.Equal(2, backend.DraftWrites);
        Assert.Equal(0, backend.Sends);
    }

    [Fact]
    public async Task Foreign_faulted_save_cancellation_is_visible_and_retained_at_original_close()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var unrelated = new CancellationTokenSource();
        var original = new OperationCanceledException("Foreign failed write", unrelated.Token);
        var backend = new RecoveryBackend { DraftFailure = original };
        var chat = new BrowserChatSession(() => backend, () => { },
            action => { action(); return Task.CompletedTask; }, _ => { });
        await chat.PrepareAsync(timeout.Token); chat.Admit();
        Assert.True(chat.TrySetValue("composer.text", "Unsaved original input"));
        while (true)
        {
            timeout.Token.ThrowIfCancellationRequested();
            chat.TryGetValue("chat.status", out var status);
            if (status?.ToString()?.Contains("The draft could not be saved") == true) break;
            await Task.Delay(10, timeout.Token);
        }
        Assert.True(chat.HasUnsavedChanges);
        Assert.True(chat.IsActionAvailable("chat.resolve"));
        var close = chat.DisposeAsync().AsTask();
        await Assert.ThrowsAnyAsync<Exception>(() => close);
        Assert.True(close.IsFaulted);
        Assert.Contains(original, close.Exception!.Flatten().InnerExceptions);
        Assert.Equal(0, backend.Sends);
    }

    [Fact]
    public async Task Paused_typing_saves_original_draft_and_new_session_reopens_it_without_sending()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var backend = new RecoveryBackend();
        await using (var first = new BrowserChatSession(() => backend, () => { },
            action => { action(); return Task.CompletedTask; }, _ => { }))
        {
            await first.PrepareAsync(timeout.Token); first.Admit();
            Assert.True(first.TrySetValue("composer.text", "Draft before reload"));
            while (first.HasUnsavedChanges) await Task.Delay(10, timeout.Token);
            Assert.Equal("Draft before reload", backend.Draft!.Content);
        }
        await using var reopened = new BrowserChatSession(() => backend, () => { },
            action => { action(); return Task.CompletedTask; }, _ => { });
        await reopened.PrepareAsync(timeout.Token); reopened.Admit();
        await reopened.DispatchAsync("nav.open-entry", backend.Draft!.ConversationId.ToString("D"), timeout.Token);
        Assert.True(reopened.TryGetValue("composer.text", out var draft));
        Assert.Equal("Draft before reload", draft);
        Assert.Equal(1, backend.Creates);
        Assert.Equal(0, backend.Sends);
    }

    [Fact]
    public async Task Actual_catalog_recovery_preserves_unsent_draft_and_never_dispatches_provider_or_tools()
    {
        var backend = new RecoveryBackend { UnavailableReason = "The runtime is temporarily unavailable." };
        await using var chat = new BrowserChatSession(() => backend, () => { },
            action => { action(); return Task.CompletedTask; }, _ => { });
        await chat.PrepareAsync(default); chat.Admit();
        Assert.True(chat.TrySetValue("composer.text", "Keep my original unsent instructions"));
        Assert.True(chat.IsActionAvailable("chat.resolve"));
        await chat.DispatchAsync("chat.resolve", null);
        Assert.True(chat.TryGetValue("dialog.description", out var description));
        Assert.Contains(backend.UnavailableReason, description!.ToString());
        Assert.True(chat.TryGetValue("composer.text", out var draft));
        Assert.Equal("Keep my original unsent instructions", draft);
        backend.UnavailableReason = null;
        await chat.DispatchAsync("chat.choose-dialog", "refresh");
        Assert.Equal("Keep my original unsent instructions", backend.Draft!.Content);
        Assert.True(chat.TryGetValue("composer.text", out draft));
        Assert.Equal(backend.Draft.Content, draft);
        Assert.False(chat.IsActionAvailable("chat.resolve"));
        Assert.True(chat.IsActionAvailable("chat.send"));
        Assert.Equal(0, backend.Sends);
        Assert.Equal(1, backend.Creates);
    }

    [Fact]
    public async Task Default_product_route_renders_shared_Chat_while_account_is_absent_and_manager_requires_explicit_toolbox_action()
    {
        HomeFeatureNavigationRequest? navigation = null;
        await using var feature = new BrowserSpacesFeature(
            () => throw new InvalidOperationException("Configuration must not load as the default Chat product."),
            () => { }, action => { action(); return Task.CompletedTask; }, request => navigation = request,
            () => throw new BrowserTaskContextUnavailableException("No authenticated actor in this controlled test."));
        var result = await feature.OpenAsync(new(HomeFeatureRouteIds.Spaces));
        Assert.True(result.Succeeded);
        Assert.Equal("spaces.chat", result.ViewState!.ViewId);
        var surface = feature.Render(result.ViewState);
        var chat = Assert.IsType<BrowserChatSession>(surface.Bindings);
        surface.Admission!.Accept();
        Assert.True(chat.TryGetValue("sidebar.name", out var name)); Assert.Equal("Chat", name);
        Assert.True(chat.TryGetValue("chat.emptyDescription", out var description)); Assert.Contains("Sign in", description!.ToString());
        Assert.False(chat.IsActionAvailable("chat.send"));
        Assert.False(chat.TrySetValue("composer.text", "A private draft needs its actual owner."));
        Assert.True(chat.IsActionAvailable("shell.account"));
        await chat.DispatchAsync("nav.manage-spaces", null);
        Assert.Equal(new(HomeFeatureRouteIds.Spaces, Action: "manage"), navigation);
        surface.Lifetime!.Dispose();
    }

    [Fact]
    public async Task Revoked_chat_does_not_project_previously_captured_private_repeat_items()
    {
        await using var chat = new BrowserChatSession(
            () => throw new BrowserTaskContextUnavailableException("Controlled signed-out host"),
            () => { }, action => { action(); return Task.CompletedTask; }, _ => { });
        await chat.PrepareAsync(default); chat.Admit();
        var captured = new HavenOS.Apps.Spaces.Chat.ChatSpaceRecentChat(Guid.NewGuid(), "Private retained title", "Today", DateTimeOffset.UtcNow, true);
        Assert.True(chat.TryGetItemValue(captured, "title", out _));
        chat.RevokePrivateContext();
        Assert.False(chat.TryGetItemValue(captured, "title", out var value)); Assert.Null(value);
        Assert.False(chat.IsActionAvailable("shell.account"));
    }

    private sealed class RecoveryBackend : IChatSpaceBackend
    {
        private readonly Conversation _conversation = new(Guid.NewGuid(), HavenMode.Chat, ConversationKind.Chat,
            "Controlled recovery", null, null, false, false, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow,
            SpaceId: SpaceRegistry.ChatSpaceId);
        private readonly ModelDescriptor _model = new("controlled", 1, "test", "test", "test",
            new HashSet<ToolCapability> { ToolCapability.Text }, DateTimeOffset.UtcNow);
        public ConversationDraft? Draft;
        public string? UnavailableReason;
        public int Creates, Sends, DraftWrites;
        public bool HoldFirstDraft;
        public Exception? DraftFailure;
        public CancellationToken OriginalSaveToken;
        public readonly TaskCompletionSource DraftStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource ReleaseDraft = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<IReadOnlyList<Conversation>> GetRecentChatsAsync(int limit, CancellationToken token) =>
            Task.FromResult<IReadOnlyList<Conversation>>(Creates == 0 ? [] : [_conversation]);
        public Task<ChatSpaceModelInventory> GetModelInventoryAsync(CancellationToken token) =>
            Task.FromResult(new ChatSpaceModelInventory(UnavailableReason is null ? [_model] : []) { UnavailableReason = UnavailableReason });
        public Task<Conversation> CreateChatAsync(CancellationToken token) { Creates++; return Task.FromResult(_conversation); }
        public Task<ChatSpaceConversationData> GetConversationAsync(Guid id, CancellationToken token)
        {
            Assert.Equal(_conversation.Id, id);
            return Task.FromResult(new ChatSpaceConversationData(_conversation, [], [], [], Draft));
        }
        public Task SaveDraftAsync(Guid id, Guid? branch, string content, IReadOnlyList<Guid> attachments, CancellationToken token)
        {
            OriginalSaveToken = token;
            if (DraftFailure is { } failure) return Task.FromException(failure);
            DraftWrites++;
            if (HoldFirstDraft && DraftWrites == 1) return HeldDraftAsync(id, branch, content, token);
            Draft = new(id, branch, content, "[]", DateTimeOffset.UtcNow); return Task.CompletedTask;
        }
        private async Task HeldDraftAsync(Guid id, Guid? branch, string content, CancellationToken token)
        {
            DraftStarted.TrySetResult(); await ReleaseDraft.Task.WaitAsync(token);
            Draft = new(id, branch, content, "[]", DateTimeOffset.UtcNow);
        }
        public Task SendAsync(ChatSpaceTurnRequest request, CancellationToken token)
        { Sends++; throw new InvalidOperationException("Recovery must never dispatch inference."); }
        public Task<MessageAttachment> ImportAttachmentAsync(Guid id, Guid? branch, string path, CancellationToken token) => throw new NotSupportedException();
        public Task RemoveAttachmentAsync(Guid id, CancellationToken token) => throw new NotSupportedException();
        public Task<ConversationBranch> CreateBranchAsync(Guid id, Guid message, string? name, CancellationToken token) => throw new NotSupportedException();
        public Task SwitchBranchAsync(Guid id, Guid branch, CancellationToken token) => throw new NotSupportedException();
        public Task RegenerateAsync(Guid id, Guid message, ModelDescriptor model, CancellationToken token) => throw new NotSupportedException();
    }
}
