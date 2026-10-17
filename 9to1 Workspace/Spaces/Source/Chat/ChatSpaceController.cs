using System.Globalization;
using Haven.Application;
using Haven.Core;

namespace HavenOS.Apps.Spaces.Chat;

/// <summary>Owns a Chat presentation. Canonical records and execution remain in the backend.</summary>
public sealed partial class ChatSpaceController
{
    private const int RecentLimit = 24;
    private readonly IChatSpaceBackend _backend;
    private readonly IPlatformShellService? _clipboard;
    private readonly object _gate = new();
    private ChatSpaceViewState _state = ChatSpaceViewState.Empty;
    private IReadOnlyList<ModelDescriptor> _modelDescriptors = [];
    private readonly Dictionary<Guid, Conversation> _conversations = [];
    private long _navigationVersion, _composerVersion;
    private Task<View?>? _composerCreation;
    private ChatSpaceThreadSettings _threadSettings = new(ChatActionMode.AllowBasicActions, GenerativeUiResponseMode.Auto);
    private ChatSpaceDualModelState _dualModel = new(false, null, false, DuoMode.Solo);
    private readonly record struct View(long Version, Guid? ConversationId, Guid? BranchId);

    public ChatSpaceController(IChatSpaceBackend backend, IPlatformShellService? clipboard = null)
    { _backend = backend ?? throw new ArgumentNullException(nameof(backend)); _clipboard = clipboard; }
    public ChatSpaceViewState State => Volatile.Read(ref _state);
    public event Action<ChatSpaceViewState>? StateChanged;
    private View CurrentView() { lock (_gate) return new(_navigationVersion, _state.Conversation?.Id, _state.Conversation?.CurrentBranchId); }
    private bool Matches(View view) => view.Version == _navigationVersion && view.ConversationId == _state.Conversation?.Id
        && view.BranchId == _state.Conversation?.CurrentBranchId;
    private void Change(Func<ChatSpaceViewState, ChatSpaceViewState> update, View? view = null)
    {
        ChatSpaceViewState next;
        lock (_gate)
        {
            if (view is { } expected && !Matches(expected)) return;
            next = update(_state) with { Revision = _state.Revision + 1 };
            Volatile.Write(ref _state, next);
        }
        StateChanged?.Invoke(next);
    }
    private long BeginNavigation()
    {
        long version;
        lock (_gate) { version = ++_navigationVersion; _composerVersion++; }
        Change(state => state with { IsLoading = true, Status = new("Loading Chat Space", ChatSpaceStatusTone.Progress, true) });
        return version;
    }
    public async Task InitializeAsync(Guid? conversationId = null, CancellationToken cancellationToken = default)
    {
        var previous = State;
        var version = BeginNavigation();
        try
        {
            if (previous.Conversation is { } conversation)
                await SaveDraftSnapshotAsync(conversation.Id, conversation.CurrentBranchId, previous.Composer, cancellationToken).ConfigureAwait(false);
            await LoadAsync(conversationId, version, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException) { FailNavigation(exception, version); }
    }
    public Task OpenChatAsync(Guid conversationId, CancellationToken cancellationToken = default) => InitializeAsync(conversationId, cancellationToken);
    public async Task NewChatAsync(CancellationToken cancellationToken = default)
    {
        var previous = State;
        var version = BeginNavigation();
        try
        {
            if (previous.Conversation is { } current)
                await SaveDraftSnapshotAsync(current.Id, current.CurrentBranchId, previous.Composer, cancellationToken).ConfigureAwait(false);
            var conversation = await _backend.CreateChatAsync(cancellationToken).ConfigureAwait(false);
            lock (_gate) if (_navigationVersion != version) return;
            await LoadAsync(conversation.Id, version, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException) { FailNavigation(exception, version); }
    }
    private async Task<View?> EnsureComposerConversationAsync(CancellationToken token)
    {
        Task<View?> creation;
        lock (_gate)
        {
            if (_state.Conversation is not null) return CurrentView();
            if (_composerCreation is null && _state.IsLoading) return null;
            creation = _composerCreation ??= CreateForComposerAsync(token);
        }
        try { return await creation.ConfigureAwait(false); }
        finally { lock (_gate) if (ReferenceEquals(_composerCreation, creation)) _composerCreation = null; }
    }
    private async Task<View?> CreateForComposerAsync(CancellationToken token)
    {
        var version = BeginNavigation();
        try
        {
            var conversation = await _backend.CreateChatAsync(token).ConfigureAwait(false);
            lock (_gate) if (_navigationVersion != version) return null;
            await LoadAsync(conversation.Id, version, token, preserveComposer: true).ConfigureAwait(false);
            lock (_gate) return _navigationVersion == version && _state.Conversation?.Id == conversation.Id
                ? CurrentView() : null;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        { FailNavigation(exception, version); return null; }
    }
    private async Task LoadAsync(Guid? conversationId, long version, CancellationToken token, bool preserveComposer = false)
    {
        try
        {
            var recent = _backend.GetRecentChatsAsync(RecentLimit, token);
            var models = _backend.GetModelInventoryAsync(token);
            var data = conversationId is { } id ? await _backend.GetConversationAsync(id, token).ConfigureAwait(false) : null;
            var safety = data is null ? null : await _backend.GetSafetySnapshotAsync(data.Conversation.Id, token).ConfigureAwait(false);
            await Task.WhenAll(recent, models).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            ChatSpaceViewState next;
            lock (_gate)
            {
                if (_navigationVersion != version) return;
                _modelDescriptors = models.Result.Models;
                if (data is not null) _conversations[data.Conversation.Id] = data.Conversation;
                var selected = ResolveSelectedModel(_state.Composer.SelectedModelName, models.Result);
                next = Loaded(recent.Result, data, selected, "Chat Space ready", safety?.State == ConversationSafetyState.Locked);
                if (preserveComposer)
                    next = next with { Composer = _state.Composer with
                        { SelectedModelName = next.Composer.SelectedModelName, SelectedModelIndex = next.Composer.SelectedModelIndex } };
                Volatile.Write(ref _state, next);
            }
            StateChanged?.Invoke(next);
        }
        catch (Exception exception) when (exception is not OperationCanceledException) { FailNavigation(exception, version); }
        catch (OperationCanceledException)
        { FailNavigation(new OperationCanceledException("Opening Chat Space was cancelled."), version); throw; }
    }
    private void FailNavigation(Exception exception, long version)
    {
        ChatSpaceViewState next;
        lock (_gate)
        {
            if (_navigationVersion != version) return;
            next = _state with { IsLoading = false, Revision = _state.Revision + 1, Status = new(exception.Message, ChatSpaceStatusTone.Error, true) };
            Volatile.Write(ref _state, next);
        }
        StateChanged?.Invoke(next);
    }
    public void UpdateDraft(string? draft) => Change(state =>
    { _composerVersion++; return state with { Composer = state.Composer with { Draft = draft ?? string.Empty } }; });
    public void SelectModel(string modelName) => Change(state =>
    {
        var index = Array.FindIndex(_modelDescriptors.ToArray(), item => item.Name.Equals(modelName, StringComparison.OrdinalIgnoreCase));
        if (index < 0) throw new ArgumentException("Choose a model in the current governed inventory.", nameof(modelName));
        return state with { Composer = state.Composer with { SelectedModelName = _modelDescriptors[index].Name, SelectedModelIndex = index } };
    });
    public Task SaveDraftAsync(CancellationToken cancellationToken = default) => SaveCurrentDraftAsync(CurrentView(), cancellationToken);
    public async Task AddAttachmentAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var ensured = await EnsureComposerConversationAsync(cancellationToken).ConfigureAwait(false);
        if (ensured is not { } view) return;
        if (view.ConversationId is not { } id || State.IsLoading || State.IsSafetyLocked) return;
        try
        {
            var attachment = await _backend.ImportAttachmentAsync(id, view.BranchId, path, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (attachment.ConversationId != id) throw new InvalidOperationException("The imported attachment belongs to a different conversation.");
            Change(state =>
            {
                _composerVersion++;
                return state with { Composer = state.Composer with { Attachments = [.. state.Composer.Attachments, attachment] },
                    Status = new($"Attached {attachment.OriginalName}", ChatSpaceStatusTone.Success, true) };
            }, view);
            await SaveCurrentDraftAsync(view, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException) { Fail(exception, view); }
    }
    public async Task RemoveAttachmentAsync(Guid attachmentId, CancellationToken cancellationToken = default)
    {
        var view = CurrentView();
        if (view.ConversationId is not { } conversationId || State.IsSafetyLocked || !State.Composer.Attachments.Any(item => item.Id == attachmentId)) return;
        try
        {
            await _backend.RemoveAttachmentAsync(conversationId, view.BranchId, attachmentId, cancellationToken).ConfigureAwait(false);
            Change(state => { _composerVersion++; return state with { Composer = state.Composer with
                { Attachments = state.Composer.Attachments.Where(item => item.Id != attachmentId).ToArray() } }; }, view);
            await SaveCurrentDraftAsync(view, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException) { Fail(exception, view); }
    }
    private Task SaveCurrentDraftAsync(View view, CancellationToken token)
    {
        ChatSpaceComposer composer;
        lock (_gate)
        {
            if (!Matches(view) || view.ConversationId is null) return Task.CompletedTask;
            composer = _state.Composer;
        }
        return SaveDraftSnapshotAsync(view.ConversationId.Value, view.BranchId, composer, token);
    }
    private readonly SemaphoreSlim _draftSaveGate = new(1, 1);
    private sealed record DraftWrite(ChatSpaceComposer Composer);
    private readonly Dictionary<(Guid Conversation, Guid? Branch), DraftWrite> _draftWrites = [];
    private async Task SaveDraftSnapshotAsync(Guid conversationId, Guid? branchId, ChatSpaceComposer composer, CancellationToken token)
    {
        var key = (conversationId, branchId);
        var write = new DraftWrite(composer);
        lock (_gate) _draftWrites[key] = write;
        await _draftSaveGate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            lock (_gate) if (!ReferenceEquals(_draftWrites[key], write)) return;
            await _backend.SaveDraftAsync(conversationId, branchId, composer.Draft,
                composer.Attachments.Select(item => item.Id).ToArray(), token).ConfigureAwait(false);
        }
        finally { _draftSaveGate.Release(); }
    }

    private async Task RefreshAsync(View view, string status, CancellationToken token, bool preserveComposer = true)
    {
        if (view.ConversationId is not { } id) return;
        var data = _backend.GetConversationAsync(id, token);
        var recent = _backend.GetRecentChatsAsync(RecentLimit, token);
        var safety = _backend.GetSafetySnapshotAsync(id, token);
        await Task.WhenAll(data, recent, safety).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        Change(state =>
        {
            _conversations[id] = data.Result.Conversation;
            var next = Loaded(recent.Result, data.Result, state.Composer.SelectedModelName, status, safety.Result.State == ConversationSafetyState.Locked);
            return preserveComposer ? next with { Composer = state.Composer with { IsSending = _turns.ContainsKey(id) } } : next;
        }, view);
    }
    private ChatSpaceViewState Loaded(IReadOnlyList<Conversation> recent, ChatSpaceConversationData? data, string? model, string status, bool locked)
    {
        var conversation = data is null ? null : ChatSpaceProjection.Project(data);
        var pending = data?.Attachments.Where(item => item.MessageId is null && (item.BranchId is null || item.BranchId == conversation?.CurrentBranchId)).ToArray() ?? [];
        var modelIndex = Array.FindIndex(_modelDescriptors.ToArray(), item => item.Name.Equals(model, StringComparison.OrdinalIgnoreCase));
        var branchIndex = conversation is null ? -1 : Array.FindIndex(conversation.Branches.ToArray(), item => item.Id == conversation.CurrentBranchId);
        return new(recent.Select(item => new ChatSpaceRecentChat(item.Id, string.IsNullOrWhiteSpace(item.Title) ? "Untitled chat" : item.Title,
                item.UpdatedAt.LocalDateTime.ToString("g", CultureInfo.CurrentCulture), item.UpdatedAt, item.Id == conversation?.Id)).ToArray(),
            conversation, _modelDescriptors.Select(item => new ChatSpaceModelChoice(item.Name, item.Name,
                $"{item.Family} · {item.ParameterSize} · {item.SizeLabel}", item.Capabilities)).ToArray(),
            new(data?.Draft?.Content ?? string.Empty, pending, model, conversation is not null && _turns.ContainsKey(conversation.Id), modelIndex),
            new(status, ChatSpaceStatusTone.Success, true), false, _state.Revision + 1, locked, _threadSettings, _dualModel, branchIndex);
    }
    private static string? ResolveSelectedModel(string? current, ChatSpaceModelInventory inventory) =>
        inventory.Models.FirstOrDefault(item => item.Name.Equals(current, StringComparison.OrdinalIgnoreCase))?.Name
        ?? inventory.Models.FirstOrDefault(item => item.Name.Equals(inventory.PreferredModelName, StringComparison.OrdinalIgnoreCase))?.Name
        ?? inventory.Models.FirstOrDefault()?.Name;
    private void Fail(Exception exception, View view) => Change(state => state with
    { IsLoading = false, Status = new(exception.Message, ChatSpaceStatusTone.Error, true) }, view);
}
