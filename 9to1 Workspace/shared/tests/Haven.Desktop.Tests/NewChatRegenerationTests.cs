using System.Reflection;
using System.Text.Json;
using Avalonia.Headless.XUnit;
using Haven.Application;
using Haven.Application.Haven;
using Haven.Core;
using Haven.Desktop.Events;
using Haven.Desktop.Services;
using Haven.Desktop.Views.Pages.Chat;
using Xunit;

namespace Haven.Desktop.Tests;

public sealed partial class ChatCloudPermissionCallerTests
{
    [AvaloniaTheory]
    [InlineData(true, ResponseRegenerationMode.Here)]
    [InlineData(true, ResponseRegenerationMode.NewBranch)]
    [InlineData(false, ResponseRegenerationMode.Here)]
    public async Task Native_regeneration_reuses_original_input_and_keeps_drafts_on_their_own_branch(
        bool latest, ResponseRegenerationMode requestedMode)
    {
        var token = TestContext.Current.CancellationToken;
        await using var rig = new Rig(temporary: false);
        var model = rig.Provider.Model.Model with { Name = "dulche-local:regeneration", Capabilities = new HashSet<ToolCapability> { ToolCapability.Text } };
        rig.Client.AvailableModels = [model];
        var requests = new List<OllamaChatRequest>();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        rig.Client.OrdinaryProviderBody = async (request, sourceToken) =>
        {
            requests.Add(request);
            if (requests.Count == 1) { entered.TrySetResult(); await release.Task.WaitAsync(sourceToken); }
            return false;
        };
        var store = new RegenerationStore(latest);
        var directory = Directory.CreateTempSubdirectory("native-regeneration-").FullName;
        var page = CreateRegenerationPage(rig, store, directory);
        Task? regenerate = null;
        Task? secondRequest = null;
        try
        {
            await page.OriginalInitialization.WaitAsync(TimeSpan.FromSeconds(10), token);
            await page.LoadConversationAsync(store.Conversation);
            await page.RefreshModelsAsync();
            page.SelectModel(model);
            page.ConfigureRegisteredContext("REGISTRATION_CONTEXT", EffortLevel.High);
            var prompts = (List<PromptDefinition>)typeof(NewChatPage).GetField("_activeInstructions", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(page)!;
            prompts.Add(new(Guid.NewGuid(), "Preserved prompt", "", "prompt", "REGENERATION_PROMPT", true, false, true, DateTimeOffset.UtcNow));
            Assert.Equal("NEXT_DRAFT", page.ComposerInput.Text);
            Assert.Equal(store.PendingAttachment.Id, Assert.Single(page.PendingComposerAttachments).Id);

            regenerate = InvokeNativeRegeneration(page, store.Response, requestedMode);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10), token);
            secondRequest = InvokeNativeRegeneration(page, store.Response, requestedMode);
            Assert.Same(regenerate, secondRequest);
            Assert.Equal(1, store.Preparations);
            Assert.True(page.IsSending);
            var invocation = Assert.Single(store.Invocations);
            Assert.Equal(store.PreparedBranch.Id, invocation.BranchId);
            Assert.Same(store.Input, invocation.ExistingInput);
            Assert.Empty(store.AssociatedAttachments);
            Assert.DoesNotContain(store.MessageWrites, item => item.Message.Role == MessageRole.User);
            var request = Assert.Single(requests);
            Assert.Equal(EffortLevel.High, request.Effort);
            Assert.Single(request.Messages, item => item.Role == "user" && item.Content == store.Input.Content);
            Assert.Contains("REGENERATION_PROMPT", request.SystemPrompt, StringComparison.Ordinal);
            Assert.Contains("REGISTRATION_CONTEXT", request.SystemPrompt, StringComparison.Ordinal);
            Assert.Contains("ORIGINAL_ATTACHMENT", request.SystemPrompt, StringComparison.Ordinal);
            Assert.DoesNotContain("PENDING_ATTACHMENT", request.SystemPrompt ?? "", StringComparison.Ordinal);
            Assert.DoesNotContain(request.Messages, item => item.Content.Contains("PENDING_ATTACHMENT", StringComparison.Ordinal));
            Assert.DoesNotContain(request.Messages, item => item.Content.Contains("NEXT_DRAFT", StringComparison.Ordinal));

            release.TrySetResult();
            await regenerate.WaitAsync(TimeSpan.FromSeconds(15), token);
            await secondRequest;
            await page.JoinAdmittedOriginalWorkAsync().WaitAsync(TimeSpan.FromSeconds(10), token);
            Assert.True(invocation.Closed);
            Assert.False(page.IsSending);
            Assert.Equal(MessageRole.Assistant, Assert.Single(store.MessageWrites).Message.Role);
            Assert.Equal(store.PreparedBranch.Id, store.MessageWrites[0].Branch);
            Assert.Empty(store.AssociatedAttachments);
            var outgoingDraft = Assert.IsType<ConversationDraft>(store.Drafts.GetValueOrDefault(store.OriginalBranch.Id));
            Assert.Equal("NEXT_DRAFT", outgoingDraft.Content);
            Assert.Contains(store.PendingAttachment.Id, JsonSerializer.Deserialize<Guid[]>(outgoingDraft.AttachmentIdsJson)!);
            Assert.Null(store.Attachments.Single(item => item.Id == store.PendingAttachment.Id).MessageId);

            var changedBranch = !latest || requestedMode == ResponseRegenerationMode.NewBranch;
            Assert.Equal(changedBranch ? ResponseRegenerationMode.NewBranch : ResponseRegenerationMode.Here, store.PreparedMode);
            if (changedBranch)
            {
                Assert.Empty(page.ComposerInput.Text ?? "");
                Assert.Empty(page.PendingComposerAttachments);
                page.SetDraft("NEW_BRANCH_FOLLOWUP");
            }
            else
            {
                Assert.Equal("NEXT_DRAFT", page.ComposerInput.Text);
                Assert.Equal(store.PendingAttachment.Id, Assert.Single(page.PendingComposerAttachments).Id);
            }
            page.RequestComposerSend();
            var followup = (Task)typeof(NewChatPage).GetField("_originalSend", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(page)!;
            await followup.WaitAsync(TimeSpan.FromSeconds(15), token);
            await page.JoinAdmittedOriginalWorkAsync().WaitAsync(TimeSpan.FromSeconds(10), token);
            var userWrite = Assert.Single(store.MessageWrites, item => item.Message.Role == MessageRole.User);
            Assert.Equal(store.PreparedBranch.Id, userWrite.Branch);
            Assert.Equal(changedBranch ? "NEW_BRANCH_FOLLOWUP" : "NEXT_DRAFT", userWrite.Message.Content);
            Assert.Null(store.Drafts.GetValueOrDefault(store.PreparedBranch.Id));
            Assert.Equal(changedBranch ? 0 : 1, store.AssociatedAttachments.Count);
            if (changedBranch)
            {
                Assert.Equal("NEXT_DRAFT", store.Drafts[store.OriginalBranch.Id].Content);
                Assert.Null(store.Attachments.Single(item => item.Id == store.PendingAttachment.Id).MessageId);
            }
            else Assert.Equal(userWrite.Message.Id, store.AssociatedAttachments[0].MessageId);
        }
        finally
        {
            release.TrySetResult();
            if (regenerate is not null) _ = await Record.ExceptionAsync(() => regenerate);
            if (secondRequest is not null) _ = await Record.ExceptionAsync(() => secondRequest);
            page.RequestRetirement();
            await page.CloseAndDrainAsync().WaitAsync(TimeSpan.FromSeconds(10), token);
            Directory.Delete(directory, true);
        }
    }

    [AvaloniaTheory]
    [InlineData(true, ResponseRegenerationMode.Here, true)]
    [InlineData(true, ResponseRegenerationMode.NewBranch, false)]
    [InlineData(false, ResponseRegenerationMode.Here, false)]
    public async Task Native_regeneration_keeps_queued_inputs_on_their_original_branch(
        bool latest, ResponseRegenerationMode mode, bool allowed)
    {
        var token = TestContext.Current.CancellationToken;
        await using var rig = new Rig(temporary: false);
        var model = rig.Provider.Model.Model with { Name = "dulche-local:queued-regeneration", Capabilities = new HashSet<ToolCapability> { ToolCapability.Text } };
        rig.Client.AvailableModels = [model];
        var requests = new List<OllamaChatRequest>();
        rig.Client.OrdinaryProviderBody = (request, _) => { requests.Add(request); return Task.FromResult(false); };
        var store = new RegenerationStore(latest);
        var directory = Directory.CreateTempSubdirectory("native-regeneration-queue-").FullName;
        var page = CreateRegenerationPage(rig, store, directory);
        try
        {
            await page.OriginalInitialization.WaitAsync(TimeSpan.FromSeconds(10), token);
            await page.LoadConversationAsync(store.Conversation);
            page.SelectModel(model);
            var scene = (ChatHavenScene)typeof(NewChatPage).GetField("_scene", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(page)!;
            // Seed the real queue through its ordinary sending-state admission. Retained
            // composer text then prevents its timer from dispatching the controlled input.
            scene.SetSending(true, true);
            page.SetDraft("QUEUED_INPUT");
            await scene.HandleComposerSendAsync();
            page.SetDraft("NEXT_DRAFT");
            scene.SetSending(false, true);
            Assert.Equal("QUEUED_INPUT", Assert.Single(scene.QueuedComposerMessages));

            await InvokeNativeRegeneration(page, store.Response, mode).WaitAsync(TimeSpan.FromSeconds(15), token);
            await page.JoinAdmittedOriginalWorkAsync().WaitAsync(TimeSpan.FromSeconds(10), token);
            Assert.Equal(allowed ? 1 : 0, store.Preparations);
            Assert.Equal(allowed ? 1 : 0, requests.Count);
            Assert.Equal(store.OriginalBranch.Id, store.PreparedBranch.Id);
            Assert.Equal("QUEUED_INPUT", Assert.Single(scene.QueuedComposerMessages));
            Assert.Equal("NEXT_DRAFT", page.ComposerInput.Text);
            Assert.Equal(store.PendingAttachment.Id, Assert.Single(page.PendingComposerAttachments).Id);
            Assert.DoesNotContain(store.MessageWrites, item => item.Message.Role == MessageRole.User);
            Assert.Empty(store.AssociatedAttachments);
            if (!allowed) Assert.Empty(store.Invocations);
        }
        finally
        {
            page.RequestRetirement();
            await page.CloseAndDrainAsync().WaitAsync(TimeSpan.FromSeconds(10), token);
            Directory.Delete(directory, true);
        }
    }

    [AvaloniaFact]
    public async Task Native_regeneration_without_available_model_preserves_draft_and_does_not_prepare_or_queue_input()
    {
        var token = TestContext.Current.CancellationToken;
        await using var rig = new Rig(temporary: false);
        var store = new RegenerationStore(latest: true);
        var directory = Directory.CreateTempSubdirectory("native-regeneration-gate-").FullName;
        var page = CreateRegenerationPage(rig, store, directory);
        try
        {
            await page.OriginalInitialization.WaitAsync(TimeSpan.FromSeconds(10), token);
            await page.LoadConversationAsync(store.Conversation);
            await InvokeNativeRegeneration(page, store.Response, ResponseRegenerationMode.Here);
            await page.JoinAdmittedOriginalWorkAsync();
            Assert.Equal(0, store.Preparations);
            Assert.Empty(store.Invocations);
            Assert.Empty(store.MessageWrites);
            Assert.Equal("NEXT_DRAFT", page.ComposerInput.Text);
            Assert.Equal(store.PendingAttachment.Id, Assert.Single(page.PendingComposerAttachments).Id);
            Assert.Null(typeof(NewChatPage).GetField("_pendingInstruction", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(page));
        }
        finally
        {
            page.RequestRetirement();
            await page.CloseAndDrainAsync().WaitAsync(TimeSpan.FromSeconds(10), token);
            Directory.Delete(directory, true);
        }
    }

    private static Task InvokeNativeRegeneration(NewChatPage page, ChatMessage response, ResponseRegenerationMode mode) =>
        (Task)typeof(NewChatPage).GetMethod("RegenerateResponseAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(page, [response, mode])!;

    private static NewChatPage CreateRegenerationPage(Rig rig, RegenerationStore store, string directory)
    {
        var paths = NativeComposerProxy<IAppPaths>((name, _) => name == "get_DataDirectory" ? directory : Path.Combine(directory, name));
        var local = new GenUiLocalActionRegistry(); var instances = new GenUiInstanceStore();
        var sessions = new ChatSessionService(store, rig.Client, new CapabilityPreflightService(), new Safety(),
            new WorkspaceToolRuntime(new Workspace()), new ComputerToolRuntime(new Computer()));
        return new(new HavenEventBus(), store, rig.Client, sessions, new Safety(), store.Versioning,
            new UserPreferencesService(paths), new GenerativeUiEventRouter([], new NativeComposerAudit(), instances), instances,
            new CalculatorTemplateRuntime(local, instances), new StructuredFormTemplateRuntime(local, instances),
            new ChoicePromptTemplateRuntime(local), new ChecklistTemplateRuntime(local, instances), new DataGridTemplateRuntime(local),
            new CardDeckTemplateRuntime(local, instances), new GraphTemplateRuntime(local), new TaskListTemplateRuntime(local),
            new DashboardTemplateRuntime(local), new AssessmentTemplateRuntime(local), new WorkflowTemplateRuntime(local),
            new CustomTemplateRuntime(local, instances), messageAttachments: store.AttachmentService, conversationProduction: store.Production);
    }

    private sealed class RegenerationStore : IConversationRepository, IConversationBranchBoundRepository
    {
        public Conversation Conversation { get; }
        public ConversationBranch OriginalBranch { get; }
        public ConversationBranch PreparedBranch { get; private set; }
        public ChatMessage Input { get; }
        public ChatMessage Response { get; }
        public MessageAttachment PendingAttachment { get; }
        public List<MessageAttachment> Attachments { get; } = [];
        public List<MessageAttachment> AssociatedAttachments { get; } = [];
        public Dictionary<Guid, ConversationDraft> Drafts { get; } = [];
        public List<(Guid Branch, ChatMessage Message)> MessageWrites { get; } = [];
        public List<Invocation> Invocations { get; } = [];
        public int Preparations { get; private set; }
        public ResponseRegenerationMode PreparedMode { get; private set; }
        public IConversationProductionRepository Production { get; }
        public IConversationVersioningService Versioning { get; }
        public IMessageAttachmentService AttachmentService { get; }
        private readonly Dictionary<Guid, List<ChatMessage>> _messages = [];
        private Guid _current;

        public RegenerationStore(bool latest)
        {
            var now = DateTimeOffset.UtcNow;
            Conversation = new(Guid.NewGuid(), HavenMode.Chat, ConversationKind.Chat, "Regeneration", null, null, false, false, now, now);
            OriginalBranch = new(Guid.NewGuid(), Conversation.Id, null, null, "Main", ConversationBranchReason.Root, true, now, now);
            PreparedBranch = OriginalBranch; _current = OriginalBranch.Id;
            Input = new(Guid.NewGuid(), Conversation.Id, MessageRole.User, "ORIGINAL_INPUT", null, null, null, now);
            Response = new(Guid.NewGuid(), Conversation.Id, MessageRole.Assistant, "OLD_RESPONSE", null, null, null, now.AddTicks(1));
            _messages[_current] = [Input, Response];
            if (!latest)
            {
                _messages[_current].Add(Input with { Id = Guid.NewGuid(), Content = "LATER_INPUT", CreatedAt = now.AddTicks(2) });
                _messages[_current].Add(Response with { Id = Guid.NewGuid(), Content = "LATER_RESPONSE", CreatedAt = now.AddTicks(3) });
            }
            MessageAttachment Attachment(Guid? message, string text) => new(Guid.NewGuid(), Conversation.Id, message,
                OriginalBranch.Id, text + ".txt", text + ".txt", "text/plain", MessageAttachmentKind.PlainText, text.Length,
                "synthetic-digest", AttachmentProcessingState.Ready, AttachmentAnalysisMethod.TextExtracted, text, "{}", now, now);
            Attachments.Add(Attachment(Input.Id, "ORIGINAL_ATTACHMENT"));
            PendingAttachment = Attachment(null, "PENDING_ATTACHMENT"); Attachments.Add(PendingAttachment);
            Drafts[_current] = new(Conversation.Id, _current, "NEXT_DRAFT", JsonSerializer.Serialize(new[] { PendingAttachment.Id }), now);
            Production = NativeComposerProxy<IConversationProductionRepository>((name, args) => name switch
            {
                "GetCurrentBranchAsync" => Task.FromResult<ConversationBranch?>((Guid)args![0]! == Conversation.Id ? CurrentBranch() : null),
                "EnsureRootBranchAsync" => Task.FromResult(CurrentBranch()),
                "GetDraftAsync" => Task.FromResult(Drafts.GetValueOrDefault(((Guid?)args![1])!.Value)),
                "SaveDraftAsync" => SaveDraft((ConversationDraft)args![0]!),
                "DeleteDraftAsync" => DeleteDraft(((Guid?)args![1])!.Value),
                "GetAttachmentsAsync" => Task.FromResult<IReadOnlyList<MessageAttachment>>(Attachments.Where(item => args![1] is not Guid id || item.MessageId == id).ToArray()),
                "UpsertAttachmentAsync" => Associate((MessageAttachment)args![0]!),
                "SetCurrentBranchAsync" => SelectBranch((Guid)args![1]!),
                _ => throw new InvalidOperationException("Unexpected regeneration production call: " + name)
            });
            Versioning = NativeComposerProxy<IConversationVersioningService>((name, args) => name == "PrepareRegenerationAsync"
                ? Prepare((ResponseRegenerationMode)args![3]!)
                : throw new InvalidOperationException("Unexpected regeneration versioning call: " + name));
            AttachmentService = NativeComposerProxy<IMessageAttachmentService>((name, args) => name == "BuildPromptContextAsync"
                ? AttachmentContext((IReadOnlyCollection<Guid>?)args![1])
                : throw new InvalidOperationException("Unexpected regeneration attachment call: " + name));
        }
        private ConversationBranch CurrentBranch() => _current == OriginalBranch.Id ? OriginalBranch : PreparedBranch;
        private Task SaveDraft(ConversationDraft draft) { Drafts[draft.BranchId!.Value] = draft; return Task.CompletedTask; }
        private Task DeleteDraft(Guid branch) { Drafts.Remove(branch); return Task.CompletedTask; }
        private Task SelectBranch(Guid branch) { if (!_messages.ContainsKey(branch)) throw new InvalidOperationException(); _current = branch; return Task.CompletedTask; }
        private Task<MessageAttachment> Associate(MessageAttachment attachment)
        {
            Assert.Equal(_current, attachment.BranchId);
            Attachments.RemoveAll(item => item.Id == attachment.Id); Attachments.Add(attachment);
            AssociatedAttachments.Add(attachment); return Task.FromResult(attachment);
        }
        private Task<AttachmentPromptContext> AttachmentContext(IReadOnlyCollection<Guid>? ids)
        {
            var selected = Attachments.Where(item => ids is null || ids.Contains(item.Id)).ToArray();
            return Task.FromResult(new AttachmentPromptContext([], string.Join("\n", selected.Select(item => item.ExtractedText)), [], selected));
        }
        private Task<ConversationBranch> Prepare(ResponseRegenerationMode mode)
        {
            Preparations++; PreparedMode = mode;
            var inputIndex = _messages[_current].FindIndex(item => item.Id == Input.Id);
            var prefix = _messages[_current].Take(inputIndex + 1).ToList();
            if (mode == ResponseRegenerationMode.NewBranch)
                PreparedBranch = new(Guid.NewGuid(), Conversation.Id, _current, Input.Id, "Regenerated", ConversationBranchReason.RegeneratedResponse, true, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
            else PreparedBranch = CurrentBranch();
            _current = PreparedBranch.Id; _messages[_current] = prefix;
            return Task.FromResult(PreparedBranch);
        }
        public Task<Conversation?> GetAsync(Guid id, CancellationToken token) => Task.FromResult<Conversation?>(id == Conversation.Id ? Conversation : null);
        public Task<IReadOnlyList<Conversation>> GetRecentAsync(HavenMode? mode, int count, CancellationToken token) => Task.FromResult<IReadOnlyList<Conversation>>([Conversation]);
        public Task<IReadOnlyList<ChatMessage>> GetMessagesAsync(Guid id, CancellationToken token) => Task.FromResult<IReadOnlyList<ChatMessage>>(id == Conversation.Id ? _messages[_current].ToArray() : []);
        public Task UpsertConversationAsync(Conversation value, CancellationToken token) => Task.CompletedTask;
        public Task AddMessageAsync(ChatMessage message, CancellationToken token) => Write(_current, message);
        public Task DeleteConversationAsync(Guid id, CancellationToken token) => throw new NotSupportedException();
        private Task Write(Guid branch, ChatMessage message) { _messages[branch].Add(message); MessageWrites.Add((branch, message)); return Task.CompletedTask; }
        public IConversationBranchInvocation BindInvocation(Guid conversationId, Guid branchId)
        {
            Assert.Equal(Conversation.Id, conversationId);
            var invocation = new Invocation(this, branchId); Invocations.Add(invocation); return invocation;
        }
        public sealed class Invocation(RegenerationStore store, Guid branch) : IConversationBranchInvocation
        {
            public Guid ConversationId => store.Conversation.Id;
            public Guid BranchId => branch;
            public ChatMessage? ExistingInput { get; private set; }
            public bool Closed { get; private set; }
            public Task AdmitExistingUserInputAsync(ChatMessage message, CancellationToken token)
            {
                Assert.Equal(store._current, branch);
                Assert.Equal(store.Input, message);
                Assert.Contains(message, store._messages[branch]);
                ExistingInput = message; return Task.CompletedTask;
            }
            public Task AddMessageAsync(ChatMessage message, CancellationToken token) => store.Write(branch, message);
            public Task AddUserMessageWithAttachmentsAsync(ChatMessage message, IReadOnlyList<Guid> ids, CancellationToken token) => throw new InvalidOperationException("Regeneration cannot claim pending attachments.");
            public Task<IReadOnlyList<ChatMessage>> GetContextMessagesAsync(CancellationToken token) => Task.FromResult<IReadOnlyList<ChatMessage>>(store._messages[branch].ToArray());
            public ValueTask DisposeAsync() { Closed = true; return ValueTask.CompletedTask; }
        }
    }
}
