using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CakeOS.Cui.Runtime;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.Views.Pages.Chat;
using HavenOS.Apps.Spaces.Chat;

namespace Haven.Desktop.Tests;

/// <summary>Actual Desktop page and authored CUI dispatch over a controlled canonical-backend boundary.</summary>
public sealed class ChatSpacePageAcceptanceTests
{
    [AvaloniaTheory]
    [InlineData(390, 844)]
    [InlineData(1280, 800)]
    public async Task Actual_page_dispatches_selectors_and_streaming_send_from_rendered_controls(int width, int height)
    {
        var backend = new Backend();
        var first = backend.Seed("Original chat");
        var page = new ChatSpacePage(backend, ChatSpacePage.ImmediateReadinessInstance, first);
        var window = new Window { Content = page, Width = width, Height = height };
        window.Show();
        try
        {
            await page.ActivateAsync(CancellationToken.None);
            Render(window);
            Assert.Contains(page.GetVisualDescendants().OfType<CuiMarkdownView>(), view => view.Text == "**Original answer**");
            var models = Find<ComboBox>(page, "model-choice");
            Assert.Equal(2, models.ItemCount);
            models.SelectedIndex = 1;
            await UntilAsync(() => page.Controller.State.Composer.SelectedModelName == "model-b");
            Render(window);
            Find<TextBox>(page, "composer-input").Text = "Question entered through the actual input";
            await UntilAsync(() => page.Controller.State.Composer.Draft == "Question entered through the actual input");
            Render(window);
            Find<Button>(page, "send").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await backend.SendEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Render(window);
            var submitted = Assert.Single(backend.Sent);
            Assert.Equal(first, submitted.Conversation.Id);
            Assert.Equal("model-b", submitted.Model.Name);
            Assert.Equal("Question entered through the actual input", submitted.Prompt);
            Assert.Contains(page.GetVisualDescendants().OfType<CuiMarkdownView>(), view => view.Text == "**Streamed response**");
            var input = Find<TextBox>(page, "composer-input");
            var point = input.TranslatePoint(default, page)!.Value;
            Assert.InRange(point.Y + input.Bounds.Height, 1, height);
            backend.ReleaseSend.TrySetResult();
            await UntilAsync(() => !page.Controller.State.Composer.IsSending);
            Render(window);
            Assert.Empty(page.Controller.State.Composer.Draft);
        }
        finally
        {
            backend.ReleaseSend.TrySetResult();
            await page.CloseAndDrainAsync();
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task Reactivating_same_page_retains_selected_chat_and_its_original_draft()
    {
        var backend = new Backend(); var first = backend.Seed("First"); var second = backend.Seed("Second");
        var page = new ChatSpacePage(backend, ChatSpacePage.ImmediateReadinessInstance, first);
        var window = new Window { Content = page, Width = 1000, Height = 800 }; window.Show();
        try
        {
            await page.ActivateAsync(CancellationToken.None);
            await page.DispatchAsync("chat.open", second);
            await UntilAsync(() => page.IsActionAvailable("chat.update-draft") == true);
            await page.DispatchAsync("chat.update-draft", "Draft in the second chat");
            page.Deactivate();
            await page.ActivateAsync(CancellationToken.None);
            Render(window);
            Assert.Equal(second, page.Controller.State.Conversation?.Id);
            Assert.Equal("Draft in the second chat", Find<TextBox>(page, "composer-input").Text);
        }
        finally { await page.CloseAndDrainAsync(); window.Close(); }
    }

    [AvaloniaFact]
    public async Task Authored_composer_Stop_cancels_the_active_chat_and_keeps_the_partial_message()
    {
        var backend = new Backend { WaitForCancellation = true, PersistAcceptedInputAtAdmission = true };
        var conversationId = backend.Seed("Assistant Main");
        var page = new ChatSpacePage(backend, ChatSpacePage.ImmediateReadinessInstance, conversationId);
        var window = new Window { Content = page, Width = 1000, Height = 800 }; window.Show();
        try
        {
            await page.ActivateAsync(CancellationToken.None);
            await page.DispatchAsync("chat.update-draft", "Keep this stopped partial");
            var sending = page.DispatchAsync("chat.send", null).AsTask();
            await backend.SendEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await UntilAsync(() => page.IsActionAvailable("chat.stop") == true);
            Render(window);

            Find<Button>(page, "stop-button").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await UntilAsync(() => !page.Controller.State.Composer.IsSending);
            await sending;
            Render(window);

            Assert.Single(page.Controller.State.Conversation!.Messages,
                message => message.Role == MessageRole.Assistant && message.ProgressLabel == "Stopped · partial response");
            Assert.Contains(backend.Data[conversationId].Messages, message => message.Role == MessageRole.User && message.Content == "Keep this stopped partial");
            Assert.Contains(backend.Data[conversationId].Messages, message => message.Role == MessageRole.Assistant && message.Content == "**Streamed response**");
            Assert.False(page.Controller.State.Composer.IsSending);
            Assert.False(page.IsActionAvailable("chat.stop"));
        }
        finally { backend.ReleaseSend.TrySetResult(); await page.CloseAndDrainAsync(); window.Close(); }
    }

    [AvaloniaFact]
    public async Task Actual_page_close_joins_held_send_before_detaching_its_scene()
    {
        var backend = new Backend(); var first = backend.Seed("First");
        var page = new ChatSpacePage(backend, ChatSpacePage.ImmediateReadinessInstance, first);
        var window = new Window { Content = page, Width = 1000, Height = 800 }; window.Show();
        try
        {
            await page.ActivateAsync(CancellationToken.None);
            await page.DispatchAsync("chat.update-draft", "Held original send");
            await UntilAsync(() => page.IsActionAvailable("chat.send") == true);
            var send = page.DispatchAsync("chat.send", null).AsTask();
            await backend.SendEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var close = page.CloseAndDrainAsync();
            Assert.Same(close, page.CloseAndDrainAsync());
            Assert.False(close.IsCompleted);
            Assert.NotNull(page.Content);
            backend.ReleaseSend.TrySetResult();
            await Record.ExceptionAsync(() => send);
            await Record.ExceptionAsync(() => close);
            Assert.True(backend.SendCompleted);
            Assert.Null(page.Content);
            Assert.NotNull(await Record.ExceptionAsync(() => page.ActivateAsync(CancellationToken.None)));
        }
        finally { backend.ReleaseSend.TrySetResult(); await Record.ExceptionAsync(page.CloseAndDrainAsync); window.Close(); }
    }

    private static T Find<T>(Control root, string id) where T : Control =>
        root.GetVisualDescendants().OfType<T>().Single(control => AutomationProperties.GetAutomationId(control) == id);
    private static void Render(Window window)
    {
        for (var pass = 0; pass < 3; pass++)
        { Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); using var frame = window.CaptureRenderedFrame(); }
    }
    private static async Task UntilAsync(Func<bool> ready)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!ready()) { timeout.Token.ThrowIfCancellationRequested(); Dispatcher.UIThread.RunJobs(); await Task.Delay(1, timeout.Token); }
        Dispatcher.UIThread.RunJobs();
    }

    private sealed class Backend : IChatSpaceBackend
    {
        internal readonly Dictionary<Guid, ChatSpaceConversationData> Data = [];
        internal readonly List<ChatSpaceTurnRequest> Sent = [];
        internal readonly TaskCompletionSource SendEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource ReleaseSend = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal bool SendCompleted;
        internal bool WaitForCancellation;
        internal bool PersistAcceptedInputAtAdmission;
        internal Guid Seed(string title)
        {
            var now = DateTimeOffset.UtcNow; var id = Guid.NewGuid(); var branch = Guid.NewGuid();
            var conversation = new Conversation(id, HavenMode.Chat, ConversationKind.Chat, title, null, null, false, false, now, now, SpaceId: SpaceRegistry.ChatSpaceId);
            var input = new ChatMessage(Guid.NewGuid(), id, MessageRole.User, "Original question", null, null, null, now);
            var answer = new ChatMessage(Guid.NewGuid(), id, MessageRole.Assistant, "**Original answer**", "Haven", "model-a", null, now);
            Data[id] = new(conversation, [input, answer], [], [new(branch, id, null, null, "Main", ConversationBranchReason.Root, true, now, now)], null);
            return id;
        }
        public Task<IReadOnlyList<Conversation>> GetRecentChatsAsync(int limit, CancellationToken token) => Task.FromResult<IReadOnlyList<Conversation>>(Data.Values.Select(value => value.Conversation).ToArray());
        public Task<ChatSpaceModelInventory> GetModelInventoryAsync(CancellationToken token) => Task.FromResult(new ChatSpaceModelInventory([Model("model-a"), Model("model-b")], "model-a"));
        private static ModelDescriptor Model(string name) => new(name, 1, "controlled", "controlled", "controlled", new HashSet<ToolCapability> { ToolCapability.Text }, DateTimeOffset.UtcNow);
        public Task<Conversation> CreateChatAsync(CancellationToken token) => Task.FromResult(Data[Seed("New chat")].Conversation);
        public Task<ChatSpaceConversationData> GetConversationAsync(Guid id, CancellationToken token) => Task.FromResult(Data[id]);
        public Task SaveDraftAsync(Guid id, Guid? branch, string content, IReadOnlyList<Guid> attachments, CancellationToken token)
        { Data[id] = Data[id] with { Draft = new(id, branch, content, "[]", DateTimeOffset.UtcNow) }; return Task.CompletedTask; }
        public async Task SendAsync(ChatSpaceTurnRequest request, IProgress<ChatStreamEvent>? progress, CancellationToken token)
        {
            Sent.Add(request);
            var now = DateTimeOffset.UtcNow;
            var user = new ChatMessage(Guid.NewGuid(), request.Conversation.Id, MessageRole.User, request.Prompt, null, null, null, now);
            var assistant = new ChatMessage(Guid.NewGuid(), request.Conversation.Id, MessageRole.Assistant, "**Streamed response**", "Haven", request.Model.Name, null, now);
            progress?.Report(ChatStreamEvent.User(user));
            var acceptedAndStored = false;
            if (request.InputAccepted is { } accepted)
            {
                await accepted(user, token);
                if (PersistAcceptedInputAtAdmission)
                {
                    Data[user.ConversationId] = Data[user.ConversationId] with { Messages = [.. Data[user.ConversationId].Messages, user] };
                    acceptedAndStored = true;
                }
            }
            progress?.Report(ChatStreamEvent.AssistantStarted(assistant.Id, request.Model.Name, "Haven"));
            progress?.Report(ChatStreamEvent.AssistantDelta(assistant.Id, assistant.Content));
            SendEntered.TrySetResult();
            if (WaitForCancellation) await ReleaseSend.Task.WaitAsync(token);
            else await ReleaseSend.Task; // Some owner-close tests deliberately hold the source independent of view cancellation.
            Data[user.ConversationId] = Data[user.ConversationId] with
                { Messages = acceptedAndStored ? [.. Data[user.ConversationId].Messages, assistant] : [.. Data[user.ConversationId].Messages, user, assistant] };
            SendCompleted = true;
        }
        public Task PersistStoppedPartialAsync(Guid id, Guid branchId, ChatMessage acceptedInput, ChatMessage message, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            var actual = Data[id];
            if (acceptedInput.ConversationId != id || acceptedInput.Role != MessageRole.User ||
                actual.Messages.LastOrDefault(item => item.Role == MessageRole.User) != acceptedInput ||
                message.ConversationId != id || message.Role != MessageRole.Assistant || string.IsNullOrWhiteSpace(message.Content) ||
                actual.Branches.All(branch => branch.Id != branchId || !branch.IsCurrent) ||
                actual.Messages.Any(existing => existing.Id == message.Id))
                throw new InvalidOperationException("Stopped partial does not match a unique current branch message.");
            Data[id] = actual with { Messages = [.. actual.Messages, message] };
            return Task.CompletedTask;
        }
        public Task<ConversationSafetySnapshot> GetSafetySnapshotAsync(Guid id, CancellationToken token) => Task.FromResult(new ConversationSafetySnapshot(id, 0, ConversationSafetyState.Active, null, 0));
        public Task<MessageAttachment> ImportAttachmentAsync(Guid id, Guid? branch, string path, CancellationToken token) => throw new NotSupportedException();
        public Task RemoveAttachmentAsync(Guid id, Guid? branch, Guid attachment, CancellationToken token) => throw new NotSupportedException();
        public Task<ConversationBranch> CreateBranchAsync(Guid id, Guid message, string? name, CancellationToken token) => throw new NotSupportedException();
        public Task SwitchBranchAsync(Guid id, Guid branch, CancellationToken token) => throw new NotSupportedException();
        public Task RegenerateAsync(Guid id, Guid message, ModelDescriptor model, CancellationToken token) => throw new NotSupportedException();
        public Task<string> GetMessageContentAsync(Guid id, Guid message, CancellationToken token) => Task.FromResult(Data[id].Messages.Single(item => item.Id == message).Content);
        public Task DeleteMessageAsync(Guid id, Guid message, CancellationToken token) => throw new NotSupportedException();
        public Task ForgetMessageAsync(Guid id, Guid message, CancellationToken token) => throw new NotSupportedException();
        public Task EditUserMessageAsync(Guid id, Guid message, string content, MessageEditMode mode, CancellationToken token) => throw new NotSupportedException();
        public Task<DualModelRun> RunDualAsync(string prompt, ModelDescriptor first, string second, DuoMode mode, CancellationToken token) => throw new NotSupportedException();
    }
}
