using System.Globalization;
using System.Text.Json;
using Haven.Application;
using Haven.Core;

namespace HavenOS.Apps.Spaces.Chat;

/// <summary>Projects Chat Space state and translates CUI intent into typed backend operations.</summary>
public sealed class ChatSpaceController(IChatSpaceBackend backend)
{
    private const int RecentLimit = 24;
    private readonly IChatSpaceBackend _backend = backend ?? throw new ArgumentNullException(nameof(backend));
    private IReadOnlyList<ModelDescriptor> _modelDescriptors = [];
    private Conversation? _originalConversation;
    private Conversation? _createdAwaitingFirstLoad;
    private int _operation;
    private long _draftRevision;
    private PendingTurnCleanup? _pendingTurnCleanup;
    private bool _turnRequiresReview;
    private string? _modelUnavailableReason;
    private sealed record PendingTurnCleanup(Guid ConversationId, Guid? BranchId, long DraftRevision);

    public bool IsBusy => Volatile.Read(ref _operation) != 0;
    public bool HasPendingTurnCleanup => _pendingTurnCleanup is not null;
    public bool RequiresTurnReconciliation => _turnRequiresReview || HasPendingTurnCleanup;
    public event Action<ChatStreamEvent>? StreamEventReceived;

    public ChatSpaceViewState State { get; private set; } = ChatSpaceViewState.Empty;

    public event Action<ChatSpaceViewState>? StateChanged;

    public async Task InitializeAsync(Guid? conversationId = null, CancellationToken cancellationToken = default)
    {
        using var operation = AdmitOperation();
        if (_pendingTurnCleanup is not null)
            await CompleteTurnCleanupCoreAsync(cancellationToken).ConfigureAwait(false);
        await InitializeCoreAsync(conversationId, cancellationToken).ConfigureAwait(false);
        _turnRequiresReview = false;
    }

    private async Task InitializeCoreAsync(Guid? conversationId, CancellationToken cancellationToken)
    {
        var draftRevision = _draftRevision;
        Publish(State with
        {
            IsLoading = true,
            Status = new("Loading Chat Space", ChatSpaceStatusTone.Progress, true)
        });
        try
        {
            var reads = new ChatSpaceReadSettlement();
            var recentTask = reads.Capture(() => _backend.GetRecentChatsAsync(RecentLimit, cancellationToken));
            var modelsTask = reads.Capture(() => _backend.GetModelInventoryAsync(cancellationToken));
            var conversationTask = conversationId is { } id
                ? reads.Capture(() => _backend.GetConversationAsync(id, cancellationToken))
                : null;
            await reads.JoinAsync(cancellationToken).ConfigureAwait(false);

            var inventory = modelsTask.Result;
            _modelDescriptors = inventory.Models;
            _modelUnavailableReason = inventory.UnavailableReason;
            var selected = ResolveSelectedModel(State.Composer.SelectedModelName, inventory);
            ApplyLoaded(recentTask.Result, inventory, conversationTask?.Result, selected, "Chat Space ready",
                preserveComposer: draftRevision != _draftRevision, isIdle: true);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            Fail(exception);
            throw;
        }
        finally
        {
            if (State.IsLoading) Publish(State with { IsLoading = false });
        }
    }

    public async Task NewChatAsync(CancellationToken cancellationToken = default)
    {
        using var operation = AdmitOperation();
        DemandReconciledTurn();
        await SaveDraftCoreAsync(cancellationToken).ConfigureAwait(false);
        await NewChatCoreAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task NewChatCoreAsync(CancellationToken cancellationToken, bool preserveComposer = false)
    {
        var revision = _draftRevision;
        var conversation = _createdAwaitingFirstLoad
            ?? await ChatSpaceOriginalTask.Run(() => _backend.CreateChatAsync(cancellationToken)).ConfigureAwait(false);
        _createdAwaitingFirstLoad = conversation;
        var composer = State.Composer;
        var loadRevision = _draftRevision;
        await InitializeCoreAsync(conversation.Id, cancellationToken).ConfigureAwait(false);
        _createdAwaitingFirstLoad = null;
        if ((preserveComposer || revision != loadRevision) && loadRevision == _draftRevision)
            Publish(State with { Composer = composer with { SelectedModelName = State.Composer.SelectedModelName } });
    }

    public async Task OpenChatAsync(Guid conversationId, CancellationToken cancellationToken = default)
    {
        using var operation = AdmitOperation();
        DemandReconciledTurn();
        await SaveDraftCoreAsync(cancellationToken).ConfigureAwait(false);
        await InitializeCoreAsync(conversationId, cancellationToken).ConfigureAwait(false);
    }

    public void UpdateDraft(string? draft)
    {
        _draftRevision++;
        Publish(State with { Composer = State.Composer with { Draft = draft ?? string.Empty } });
    }

    public void SelectModel(string modelName)
    {
        if (!_modelDescriptors.Any(item => item.Name.Equals(modelName, StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException($"Model '{modelName}' is not in the current governed inventory.", nameof(modelName));
        Publish(State with
        {
            Composer = State.Composer with { SelectedModelName = modelName },
            Status = new($"Model selected: {modelName}", ChatSpaceStatusTone.Neutral, true)
        });
    }

    public async Task SaveDraftAsync(CancellationToken cancellationToken = default)
    {
        using var operation = AdmitOperation();
        await SaveDraftCoreAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task SaveDraftCoreAsync(CancellationToken cancellationToken)
    {
        if (State.Conversation is null)
        {
            if (State.Composer.Draft.Length == 0 && State.Composer.Attachments.Count == 0) return;
            await NewChatCoreAsync(cancellationToken, preserveComposer: true).ConfigureAwait(false);
        }
        var conversation = State.Conversation
            ?? throw new InvalidOperationException("The original draft conversation is unavailable.");
        await ChatSpaceOriginalTask.Run(() => _backend.SaveDraftAsync(
            conversation.Id,
            conversation.CurrentBranchId,
            State.Composer.Draft,
            State.Composer.Attachments.Select(item => item.Id).ToArray(),
            cancellationToken)).ConfigureAwait(false);
    }

    public async Task AddAttachmentAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        using var operation = AdmitOperation();
        if (State.Conversation is null) await NewChatCoreAsync(cancellationToken, true).ConfigureAwait(false);
        var conversation = State.Conversation!;
        try
        {
            var attachment = await _backend.ImportAttachmentAsync(
                conversation.Id,
                conversation.CurrentBranchId,
                path,
                cancellationToken).ConfigureAwait(false);
            Publish(State with
            {
                Composer = State.Composer with { Attachments = [.. State.Composer.Attachments, attachment] },
                Status = new($"Attached {attachment.OriginalName}", ChatSpaceStatusTone.Success, true)
            });
            await SaveDraftCoreAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            Fail(exception);
            throw;
        }
    }

    public async Task RemoveAttachmentAsync(Guid attachmentId, CancellationToken cancellationToken = default)
    {
        using var operation = AdmitOperation();
        if (!State.Composer.Attachments.Any(item => item.Id == attachmentId)) return;
        try
        {
            await _backend.RemoveAttachmentAsync(attachmentId, cancellationToken).ConfigureAwait(false);
            Publish(State with
            {
                Composer = State.Composer with
                {
                    Attachments = State.Composer.Attachments.Where(item => item.Id != attachmentId).ToArray()
                },
                Status = new("Attachment removed", ChatSpaceStatusTone.Neutral, true)
            });
            await SaveDraftCoreAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            Fail(exception);
            throw;
        }
    }

    public async Task SendAsync(CancellationToken cancellationToken = default)
    {
        using var operation = AdmitOperation();
        DemandReconciledTurn();
        var acceptedComposer = State.Composer;
        var draftRevision = _draftRevision;
        if (State.Conversation is null) await NewChatCoreAsync(cancellationToken, true).ConfigureAwait(false);
        var conversation = State.Conversation!;
        var model = ResolveModel(acceptedComposer.SelectedModelName ?? State.Composer.SelectedModelName)
            ?? throw new InvalidOperationException("Choose an available model before sending.");
        var prompt = acceptedComposer.Draft.Trim();
        var attachments = acceptedComposer.Attachments;
        var originalConversation = _originalConversation
            ?? throw new InvalidOperationException("The actual Chat conversation has not been loaded.");
        if (prompt.Length == 0 && attachments.Count == 0) return;

        Publish(State with
        {
            Composer = State.Composer with { IsSending = true },
            Status = new("Sending message", ChatSpaceStatusTone.Progress, true)
        });
        try
        {
            var attachmentIds = attachments.Select(item => item.Id).ToArray();
            await ChatSpaceOriginalTask.Run(() => _backend.SaveDraftAsync(conversation.Id, conversation.CurrentBranchId,
                prompt, attachmentIds, cancellationToken)).ConfigureAwait(false);
            var request = new ChatSpaceTurnRequest(originalConversation, prompt, model, attachmentIds);
            // Once dispatch begins an error cannot authorize automatic replay of tool/model work.
            _turnRequiresReview = true;
            if (_backend is IChatSpaceStreamingBackend streaming)
                await streaming.SendAsync(request, ObserveStreamAsync, cancellationToken).ConfigureAwait(false);
            else
                await _backend.SendAsync(request, cancellationToken).ConfigureAwait(false);
            _pendingTurnCleanup = new(conversation.Id, conversation.CurrentBranchId, draftRevision);
            await CompleteTurnCleanupCoreAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            Fail(exception);
            throw;
        }
        catch (OperationCanceledException)
        {
            // SaveDraftAsync runs before dispatch, so interruption must leave the user's
            // prompt/attachments available for retry and must not strand the composer.
            Publish(State with
            {
                Composer = State.Composer with { IsSending = false },
                Status = new("Sending was interrupted. Your draft is still available.", ChatSpaceStatusTone.Neutral, true)
            });
            throw;
        }
    }

    public async Task BranchAsync(Guid messageId, string? name = null, CancellationToken cancellationToken = default)
    {
        using var operation = AdmitOperation();
        DemandReconciledTurn();
        if (State.Conversation is not { } conversation) return;
        try
        {
            await SaveDraftCoreAsync(cancellationToken).ConfigureAwait(false);
            await _backend.CreateBranchAsync(conversation.Id, messageId, name, cancellationToken).ConfigureAwait(false);
            await RefreshConversationAsync("Branch created", cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            Fail(exception);
            throw;
        }
    }

    public async Task SwitchBranchAsync(Guid branchId, CancellationToken cancellationToken = default)
    {
        using var operation = AdmitOperation();
        DemandReconciledTurn();
        if (State.Conversation is not { } conversation || conversation.CurrentBranchId == branchId) return;
        try
        {
            await SaveDraftCoreAsync(cancellationToken).ConfigureAwait(false);
            await _backend.SwitchBranchAsync(conversation.Id, branchId, cancellationToken).ConfigureAwait(false);
            await RefreshConversationAsync("Branch selected", cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            Fail(exception);
            throw;
        }
    }

    public async Task RegenerateAsync(Guid assistantMessageId, CancellationToken cancellationToken = default)
    {
        using var operation = AdmitOperation();
        DemandReconciledTurn();
        if (State.Conversation is not { } conversation) return;
        var model = ResolveModel(State.Composer.SelectedModelName)
            ?? throw new InvalidOperationException("Choose an available model before regenerating.");
        Publish(State with
        {
            Composer = State.Composer with { IsSending = true },
            Status = new("Regenerating response", ChatSpaceStatusTone.Progress, true)
        });
        try
        {
            _turnRequiresReview = true;
            await _backend.RegenerateAsync(conversation.Id, assistantMessageId, model, cancellationToken).ConfigureAwait(false);
            await RefreshConversationAsync("Response regenerated", cancellationToken).ConfigureAwait(false);
            _turnRequiresReview = false;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            Fail(exception);
            throw;
        }
        finally
        {
            if (State.Composer.IsSending)
                Publish(State with { Composer = State.Composer with { IsSending = false } });
        }
    }

    public async Task RetryPendingTurnCleanupAsync(CancellationToken cancellationToken = default)
    {
        using var operation = AdmitOperation();
        if (_pendingTurnCleanup is null)
            throw new InvalidOperationException("No completed Chat turn is waiting for cleanup.");
        await CompleteTurnCleanupCoreAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task CompleteTurnCleanupCoreAsync(CancellationToken cancellationToken)
    {
        var pending = _pendingTurnCleanup
            ?? throw new InvalidOperationException("The original completed turn is unavailable.");
        if (State.Conversation?.Id != pending.ConversationId)
            throw new InvalidOperationException("The original completed Chat must be reconciled before navigating.");
        var remainder = pending.DraftRevision == _draftRevision ? string.Empty : State.Composer.Draft;
        await ChatSpaceOriginalTask.Run(() => _backend.SaveDraftAsync(pending.ConversationId, pending.BranchId, remainder, [], cancellationToken)).ConfigureAwait(false);
        // A newer draft remains owned by the composer, even if it changed during this write.
        Publish(State with { Composer = State.Composer with
        {
            Draft = pending.DraftRevision == _draftRevision ? string.Empty : State.Composer.Draft,
            Attachments = []
        } });
        await RefreshConversationAsync("Response complete", cancellationToken, preserveComposer: true).ConfigureAwait(false);
        _pendingTurnCleanup = null;
        _turnRequiresReview = false;
    }

    private void DemandReconciledTurn()
    {
        if (RequiresTurnReconciliation)
            throw new InvalidOperationException("Refresh the original Chat to reconcile its previous turn before starting another operation.");
    }

    private Task ObserveStreamAsync(ChatStreamEvent item, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var current = State.Conversation ?? throw new InvalidOperationException("The original Chat presentation is unavailable.");
        var messages = current.Messages.ToList();
        if (item.Message is { } message)
        {
            if (message.ConversationId != current.Id)
                throw new InvalidOperationException("A Chat stream cannot publish another conversation's message.");
            var projected = ChatSpaceProjection.ProjectMessage(message, []);
            var index = messages.FindIndex(value => value.Id == message.Id);
            if (index < 0) messages.Add(projected); else messages[index] = projected;
        }
        else if (item.MessageId is { } id)
        {
            var index = messages.FindIndex(value => value.Id == id);
            if (item.Kind == ChatStreamEventKind.AssistantStarted && index < 0)
                messages.Add(new(id, MessageRole.Assistant, string.Empty, item.Agent ?? "Assistant",
                    DateTimeOffset.Now.ToString("g", CultureInfo.CurrentCulture), item.Model, [], [], [], false));
            else if (index >= 0)
            {
                var previous = messages[index];
                messages[index] = item.Kind switch
                {
                    ChatStreamEventKind.AssistantDelta => previous with { Content = previous.Content + item.Delta },
                    ChatStreamEventKind.ThinkingDelta => previous with { Thinking = previous.Thinking + item.Thinking },
                    ChatStreamEventKind.ToolActivity when item.ToolActivity is { } activity =>
                        previous with { ToolActivities = [.. previous.ToolActivities, activity] },
                    _ => previous
                };
            }
        }
        Publish(State with { Conversation = current with { Messages = messages.ToArray() } });
        StreamEventReceived?.Invoke(item);
        return Task.CompletedTask;
    }

    private IDisposable AdmitOperation()
    {
        if (Interlocked.CompareExchange(ref _operation, 1, 0) != 0)
            throw new InvalidOperationException("The current Chat operation must settle before another operation starts.");
        return new Operation(this);
    }
    private sealed class Operation(ChatSpaceController owner) : IDisposable
    {
        public void Dispose() => Volatile.Write(ref owner._operation, 0);
    }

    private async Task RefreshConversationAsync(string status, CancellationToken cancellationToken, bool preserveComposer = false)
    {
        var id = State.Conversation?.Id ?? throw new InvalidOperationException("No Chat is open.");
        var reads = new ChatSpaceReadSettlement();
        var conversationTask = reads.Capture(() => _backend.GetConversationAsync(id, cancellationToken));
        var recentTask = reads.Capture(() => _backend.GetRecentChatsAsync(RecentLimit, cancellationToken));
        await reads.JoinAsync(cancellationToken).ConfigureAwait(false);
        ApplyLoaded(
            recentTask.Result,
            new ChatSpaceModelInventory(_modelDescriptors, State.Composer.SelectedModelName)
            { UnavailableReason = _modelUnavailableReason },
            conversationTask.Result,
            State.Composer.SelectedModelName,
            status, preserveComposer);
    }

    private void ApplyLoaded(
        IReadOnlyList<Conversation> recent,
        ChatSpaceModelInventory inventory,
        ChatSpaceConversationData? data,
        string? selectedModel,
        string status, bool preserveComposer = false, bool isIdle = false)
    {
        _originalConversation = data?.Conversation;
        var retainedComposer = State.Composer;
        var conversation = data is null ? null : ChatSpaceProjection.Project(data);
        var pending = data?.Attachments
            .Where(item => item.MessageId is null && (item.BranchId is null || item.BranchId == conversation?.CurrentBranchId))
            .ToArray() ?? [];
        var draft = data?.Draft?.Content ?? (data is null ? State.Composer.Draft : string.Empty);
        Publish(new ChatSpaceViewState(
            recent.Select(item => new ChatSpaceRecentChat(
                item.Id,
                string.IsNullOrWhiteSpace(item.Title) ? "Untitled chat" : item.Title,
                item.UpdatedAt.LocalDateTime.ToString("g", CultureInfo.CurrentCulture),
                item.UpdatedAt,
                item.Id == conversation?.Id)).ToArray(),
            conversation,
            inventory.Models.Select(item => new ChatSpaceModelChoice(
                item.Name,
                item.Name,
                $"{item.Family} · {item.ParameterSize} · {item.SizeLabel}",
                item.Capabilities)).ToArray(),
            preserveComposer ? retainedComposer with { IsSending = false, SelectedModelName = selectedModel } : new ChatSpaceComposer(draft, pending, selectedModel, false),
            new ChatSpaceStatus(inventory.UnavailableReason ?? status,
                inventory.UnavailableReason is null ? ChatSpaceStatusTone.Success : ChatSpaceStatusTone.Neutral, true)
            { IsIdle = isIdle && inventory.UnavailableReason is null },
            false,
            State.Revision + 1));
    }

    private static string? ResolveSelectedModel(string? current, ChatSpaceModelInventory inventory)
    {
        if (inventory.Models.Any(item => item.Name.Equals(current, StringComparison.OrdinalIgnoreCase))) return current;
        if (inventory.Models.Any(item => item.Name.Equals(inventory.PreferredModelName, StringComparison.OrdinalIgnoreCase)))
            return inventory.PreferredModelName;
        return inventory.Models.FirstOrDefault()?.Name;
    }

    private ModelDescriptor? ResolveModel(string? name) =>
        _modelDescriptors.FirstOrDefault(item => item.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    private void Fail(Exception exception)
    {
        try
        {
            Publish(State with
            {
                IsLoading = false,
                Composer = State.Composer with { IsSending = false },
                Status = new(exception.Message, ChatSpaceStatusTone.Error, true)
            });
        }
        catch (Exception presentationFailure)
        {
            if (ReferenceEquals(exception, presentationFailure)) throw;
            throw new AggregateException("The Chat original and its error presentation failed.", exception, presentationFailure);
        }
    }

    private void Publish(ChatSpaceViewState state)
    {
        State = state.Revision > State.Revision ? state : state with { Revision = State.Revision + 1 };
        StateChanged?.Invoke(State);
    }
}
