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
    private long _requestVersion;

    public ChatSpaceViewState State { get; private set; } = ChatSpaceViewState.Empty;

    public event Action<ChatSpaceViewState>? StateChanged;

    public async Task InitializeAsync(Guid? conversationId = null, CancellationToken cancellationToken = default)
    {
        var request = Interlocked.Increment(ref _requestVersion);
        Publish(State with
        {
            IsLoading = true,
            Status = new("Loading Chat Space", ChatSpaceStatusTone.Progress, true)
        });
        try
        {
            var recentTask = _backend.GetRecentChatsAsync(RecentLimit, cancellationToken);
            var modelsTask = _backend.GetModelInventoryAsync(cancellationToken);
            Task<ChatSpaceConversationData?> conversationTask = conversationId is { } id
                ? GetConversationOrNullAsync(id, cancellationToken)
                : Task.FromResult<ChatSpaceConversationData?>(null);
            await Task.WhenAll(recentTask, modelsTask, conversationTask).ConfigureAwait(false);
            if (request != Volatile.Read(ref _requestVersion)) return;

            var inventory = modelsTask.Result;
            _modelDescriptors = inventory.Models;
            var selected = ResolveSelectedModel(State.Composer.SelectedModelName, inventory);
            ApplyLoaded(recentTask.Result, inventory, conversationTask.Result, selected, "Chat Space ready");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            if (request == Volatile.Read(ref _requestVersion)) Fail(exception);
        }
    }

    public async Task NewChatAsync(CancellationToken cancellationToken = default)
    {
        var conversation = await _backend.CreateChatAsync(cancellationToken).ConfigureAwait(false);
        await InitializeAsync(conversation.Id, cancellationToken).ConfigureAwait(false);
    }

    public Task OpenChatAsync(Guid conversationId, CancellationToken cancellationToken = default) =>
        InitializeAsync(conversationId, cancellationToken);

    public void UpdateDraft(string? draft) =>
        Publish(State with { Composer = State.Composer with { Draft = draft ?? string.Empty } });

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
        if (State.Conversation is not { } conversation) return;
        await _backend.SaveDraftAsync(
            conversation.Id,
            conversation.CurrentBranchId,
            State.Composer.Draft,
            State.Composer.Attachments.Select(item => item.Id).ToArray(),
            cancellationToken).ConfigureAwait(false);
    }

    public async Task AddAttachmentAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (State.Conversation is null) await NewChatAsync(cancellationToken).ConfigureAwait(false);
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
            await SaveDraftAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            Fail(exception);
        }
    }

    public async Task RemoveAttachmentAsync(Guid attachmentId, CancellationToken cancellationToken = default)
    {
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
            await SaveDraftAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            Fail(exception);
        }
    }

    public async Task SendAsync(CancellationToken cancellationToken = default)
    {
        if (State.Composer.IsSending) return;
        if (State.Conversation is null) await NewChatAsync(cancellationToken).ConfigureAwait(false);
        var conversation = State.Conversation!;
        var model = ResolveModel(State.Composer.SelectedModelName)
            ?? throw new InvalidOperationException("Choose an available model before sending.");
        var prompt = State.Composer.Draft.Trim();
        var attachments = State.Composer.Attachments;
        if (prompt.Length == 0 && attachments.Count == 0) return;

        Publish(State with
        {
            Composer = State.Composer with { IsSending = true },
            Status = new("Sending message", ChatSpaceStatusTone.Progress, true)
        });
        try
        {
            await SaveDraftAsync(cancellationToken).ConfigureAwait(false);
            await _backend.SendAsync(
                new ChatSpaceTurnRequest(
                    new Conversation(
                        conversation.Id,
                        HavenMode.Chat,
                        ConversationKind.Chat,
                        conversation.Title,
                        null,
                        null,
                        false,
                        false,
                        DateTimeOffset.MinValue,
                        DateTimeOffset.MinValue,
                        SpaceId: SpaceRegistry.ChatSpaceId),
                    prompt,
                    model,
                    attachments.Select(item => item.Id).ToArray()),
                cancellationToken).ConfigureAwait(false);
            await _backend.SaveDraftAsync(conversation.Id, conversation.CurrentBranchId, string.Empty, [], cancellationToken)
                .ConfigureAwait(false);
            Publish(State with { Composer = State.Composer with { Draft = string.Empty, Attachments = [] } });
            await RefreshConversationAsync("Response complete", cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            Publish(State with { Composer = State.Composer with { IsSending = false } });
            Fail(exception);
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
        if (State.Conversation is not { } conversation) return;
        try
        {
            await SaveDraftAsync(cancellationToken).ConfigureAwait(false);
            await _backend.CreateBranchAsync(conversation.Id, messageId, name, cancellationToken).ConfigureAwait(false);
            await RefreshConversationAsync("Branch created", cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            Fail(exception);
        }
    }

    public async Task SwitchBranchAsync(Guid branchId, CancellationToken cancellationToken = default)
    {
        if (State.Conversation is not { } conversation || conversation.CurrentBranchId == branchId) return;
        try
        {
            await SaveDraftAsync(cancellationToken).ConfigureAwait(false);
            await _backend.SwitchBranchAsync(conversation.Id, branchId, cancellationToken).ConfigureAwait(false);
            await RefreshConversationAsync("Branch selected", cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            Fail(exception);
        }
    }

    public async Task RegenerateAsync(Guid assistantMessageId, CancellationToken cancellationToken = default)
    {
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
            await _backend.RegenerateAsync(conversation.Id, assistantMessageId, model, cancellationToken).ConfigureAwait(false);
            await RefreshConversationAsync("Response regenerated", cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            Publish(State with { Composer = State.Composer with { IsSending = false } });
            Fail(exception);
        }
    }

    private async Task RefreshConversationAsync(string status, CancellationToken cancellationToken)
    {
        var id = State.Conversation?.Id ?? throw new InvalidOperationException("No Chat is open.");
        var conversationTask = _backend.GetConversationAsync(id, cancellationToken);
        var recentTask = _backend.GetRecentChatsAsync(RecentLimit, cancellationToken);
        await Task.WhenAll(conversationTask, recentTask).ConfigureAwait(false);
        ApplyLoaded(
            recentTask.Result,
            new ChatSpaceModelInventory(_modelDescriptors, State.Composer.SelectedModelName),
            conversationTask.Result,
            State.Composer.SelectedModelName,
            status);
    }

    private async Task<ChatSpaceConversationData?> GetConversationOrNullAsync(
        Guid conversationId,
        CancellationToken cancellationToken) =>
        await _backend.GetConversationAsync(conversationId, cancellationToken).ConfigureAwait(false);

    private void ApplyLoaded(
        IReadOnlyList<Conversation> recent,
        ChatSpaceModelInventory inventory,
        ChatSpaceConversationData? data,
        string? selectedModel,
        string status)
    {
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
            new ChatSpaceComposer(draft, pending, selectedModel, false),
            new ChatSpaceStatus(status, ChatSpaceStatusTone.Success, true),
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

    private void Fail(Exception exception) => Publish(State with
    {
        IsLoading = false,
        Status = new(exception.Message, ChatSpaceStatusTone.Error, true)
    });

    private void Publish(ChatSpaceViewState state)
    {
        State = state.Revision > State.Revision ? state : state with { Revision = State.Revision + 1 };
        StateChanged?.Invoke(State);
    }
}
