using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Threading;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.Services;
using Haven.Desktop.HavenUI.Backend;
using CakeOS.Cui.Runtime;
using CakeOS.Cui.Themes;

namespace Haven.Desktop.Views.Shell.NativePresentation;

/// <summary>
/// Production coordinator for the existing conversation and Space owners. The canonical
/// CUI document owns visible controls; the platform supplies only its host.
/// </summary>
internal sealed class NativeChatSidebar : UserControl, IDisposable, IAsyncDisposable,
    IDesktopOriginalRetirementParticipant, IDesktopOriginalRetirementJoinGuard
{
    private readonly IConversationRepository _conversations;
    private readonly IContainerRepository _containers;
    private readonly Func<Conversation, Task> _openConversation;
    private readonly Func<HavenMode, Guid?, Task> _startChat;
    private readonly Func<HavenMode, Guid?, Guid?, Task>? _startChatInSpace;
    private readonly Func<ContainerDefinition, Task> _openGroup;
    private readonly NativeChatUiStateStore _stateStore;
    private readonly IConversationProductionRepository? _production;
    private readonly SpaceRegistry? _spaces;
    private readonly Func<SpaceDefinition, Task>? _openSpace;
    private readonly Func<SpaceDefinition, DestinationTransition, Task>? _openSpaceInTransition;
    private readonly object _destinationGate = new();
    private DestinationTransition? _destinationTransition;

    internal sealed class DestinationTransition(NativeChatSidebar owner, Guid? requestedSpaceId)
    {
        internal NativeChatSidebar Owner { get; } = owner;
        internal Guid? RequestedSpaceId { get; } = requestedSpaceId;
        internal bool AcceptsScopeSelection = true;
        internal bool ScopeSelectionIssued;
        internal Task? ScopeSelection;
    }
    private readonly CancellationTokenSource _lifetime = new();
    private readonly NativeChatSidebarCuiAdapter _scene;
    // Study's existing assignment/revision owners inject Haven.UI sections into this
    // root. Keep that real surface until its complete CUI migration is delivered.
    private readonly ChatSidebarHavenScene _studyScene;
    internal ChatSidebarHavenScene Scene => _studyScene;
    internal HavenSceneControl SceneHost { get; }
    private readonly DesktopOriginalWorkLifetime _originalWork;
    private readonly Func<Task>? _manageSpaces;
    private Task? _refreshOriginal;
    private SpaceNavigationPreferenceSnapshot? _navigationPreferences;

    private IReadOnlyList<Conversation> _conversationRows = [];
    private IReadOnlyList<ContainerDefinition> _groupRows = [];
    private IReadOnlyList<MessageAttachment> _fileRows = [];
    private IReadOnlyList<SpaceDefinition> _spaceRows = [];
    private IReadOnlyDictionary<Guid, NativeChatItemState> _states = new Dictionary<Guid, NativeChatItemState>();
    private Guid? _activeConversationId;
    private Guid? _activeGroupId;
    private Guid? _currentSpaceId;
    private HavenMode _currentMode = HavenMode.Chat;
    private string _query = string.Empty;
    private bool _refreshPending;
    private bool _disposed;

    public NativeChatSidebar(
        IConversationRepository conversations,
        IContainerRepository containers,
        Func<Conversation, Task> openConversation,
        Func<HavenMode, Guid?, Task> startChat,
        Func<ContainerDefinition, Task> openGroup,
        NativeChatUiStateStore? stateStore = null,
        IConversationProductionRepository? production = null,
        SpaceRegistry? spaces = null,
        Func<SpaceDefinition, Task>? openSpace = null,
        Func<HavenMode, Guid?, Guid?, Task>? startChatInSpace = null,
        Func<SpaceDefinition, DestinationTransition, Task>? openSpaceInTransition = null,
        Func<Task>? manageSpaces = null,
        Func<bool, Task>? setSidebarCollapsed = null,
        CuiThemePreferenceContext? currentHomeTheme = null,
        Func<Task>? configureNavigation = null)
    {
        _conversations = conversations ?? throw new ArgumentNullException(nameof(conversations));
        _containers = containers ?? throw new ArgumentNullException(nameof(containers));
        _openConversation = openConversation ?? throw new ArgumentNullException(nameof(openConversation));
        _startChat = startChat ?? throw new ArgumentNullException(nameof(startChat));
        _startChatInSpace = startChatInSpace;
        _openGroup = openGroup ?? throw new ArgumentNullException(nameof(openGroup));
        _stateStore = stateStore ?? new NativeChatUiStateStore();
        _production = production;
        _spaces = spaces;
        _openSpace = openSpace;
        _openSpaceInTransition = openSpaceInTransition;

        _manageSpaces = manageSpaces;
        _originalWork = new DesktopOriginalWorkLifetime(StopOriginalAsync, CleanupOriginalAsync);
        _scene = new NativeChatSidebarCuiAdapter(new NativeChatSidebarCuiActions(
            query => _originalWork.RunSynchronous(_ => { _query = query; Render(); }),
            () => StartChatAsync(null),
            () => _originalWork.RunSynchronous(_ => ShowCreateGroupPrompt()),
            ShowSpacePickerAsync, HandleConversationActionAsync, HandleGroupActionAsync, OpenFileAsync,
            OpenSpaceFromPickerAsync, ManageSpacesAsync,
            collapsed => RunSidebarAsync(() => setSidebarCollapsed?.Invoke(collapsed)
                ?? throw new InvalidOperationException("The current host cannot save sidebar preferences.")))
        {
            ConfigureNavigation = configureNavigation,
            CanManageSpaces = manageSpaces is not null,
            CanChangeSidebarLayout = setSidebarCollapsed is not null
        }, currentHomeTheme);
        _studyScene = new ChatSidebarHavenScene();
        SceneHost = new HavenSceneControl { Root = _studyScene.Root };
        _studyScene.SearchChanged += OnStudySearchChanged;
        _studyScene.NewChatRequested += OnStudyNewChat;
        _studyScene.NewGroupRequested += OnStudyNewGroup;
        _studyScene.SpacePickerRequested += OnStudySpacePicker;
        _studyScene.ConversationActionRequested += OnStudyConversation;
        _studyScene.GroupActionRequested += OnStudyGroup;
        _studyScene.FileRequested += OnStudyFile;
        _scene.LayoutChanged += OnNavigationLayoutChanged;
        AutomationProperties.SetAutomationId(this, "HavenNativeChatSidebar");
        AutomationProperties.SetName(this, "Spaces navigation and contextual sidebar");
        Content = _scene.Root;
        _ = RunSidebarAsync(async () => { await LoadSpaceScopeAsync(); await RefreshAsync(); });
    }

    internal NativeChatSidebarCuiAdapter Navigation => _scene;
    internal double PreferredWidth => _currentMode == HavenMode.Study ? 280 : _scene.PreferredWidth;
    internal event EventHandler? NavigationLayoutChanged;
    private void OnNavigationLayoutChanged(object? sender, EventArgs args) => NavigationLayoutChanged?.Invoke(this, args);
    internal Task? OriginalClose => _originalWork.OriginalClose;

    private void SetSidebarStatus(string? status)
    { _scene.SetStatus(status); if (_currentMode == HavenMode.Study) _studyScene.SetStatus(status); }
    private void SetNewChatBusy(bool busy)
    { _scene.SetNewChatBusy(busy); _studyScene.SetNewChatBusy(busy); }
    private void ShowTextPrompt(string title, string value, string confirmLabel, Func<string, Task> confirm)
    {
        if (_currentMode == HavenMode.Study) _studyScene.ShowTextPrompt(title, value, confirmLabel, text => RunSidebarAsync(() => confirm(text)));
        else _scene.ShowTextPrompt(title, value, confirmLabel, text => RunSidebarAsync(() => confirm(text)));
    }
    private void ShowChoices(string title, IReadOnlyList<(string Label, Func<Task> Action)> choices)
    {
        if (_currentMode == HavenMode.Study)
            _studyScene.ShowChoices(title, choices.Select(choice => (choice.Label, (Action)(() => _ = ObserveStudyEventAsync(choice.Action)))).ToArray());
        else _scene.ShowChoices(title, choices);
    }
    private async Task ObserveStudyEventAsync(Func<Task> command)
    {
        if (_disposed) return;
        try { await RunSidebarAsync(command); }
        catch (Exception error) { if (!_disposed) _studyScene.SetStatus(error.Message); }
    }
    private void OnStudySearchChanged(object? sender, string query)
    { if (!_disposed) _originalWork.RunSynchronous(_ => { _query = query; Render(); }); }
    private void OnStudyNewChat(object? sender, EventArgs args) => _ = ObserveStudyEventAsync(() => StartChatAsync(null));
    private void OnStudyNewGroup(object? sender, EventArgs args) { if (!_disposed) _originalWork.RunSynchronous(_ => ShowCreateGroupPrompt()); }
    private void OnStudySpacePicker(object? sender, EventArgs args) => _ = ObserveStudyEventAsync(ShowSpacePickerAsync);
    private void OnStudyConversation(object? sender, ChatSidebarConversationRequest request) => _ = ObserveStudyEventAsync(() => HandleConversationActionAsync(request));
    private void OnStudyGroup(object? sender, ChatSidebarGroupRequest request) => _ = ObserveStudyEventAsync(() => HandleGroupActionAsync(request));
    private void OnStudyFile(object? sender, Guid id) => _ = ObserveStudyEventAsync(() => OpenFileAsync(id));

    private Task RunSidebarAsync(Func<Task> body) => _originalWork.RunAsync(async original =>
    {
        original.DemandPublication();
        await original.AwaitAsync(body());
    });

    private Task ManageSpacesAsync() => RunSidebarAsync(() => _manageSpaces?.Invoke()
        ?? throw new InvalidOperationException("The current host has no Spaces manager owner."));

    internal void SetNavigationPreferences(SpaceNavigationPreferenceSnapshot preferences)
    {
        _originalWork.DemandAdmission();
        _navigationPreferences = preferences ?? throw new ArgumentNullException(nameof(preferences));
        PublishNavigationPreferences();
    }

    private void PublishNavigationPreferences()
    {
        if (_navigationPreferences is not { } preferences || _disposed) return;
        var rows = preferences.PinnedSpaceIds.Select(id =>
        {
            var row = _spaceRows.FirstOrDefault(space => space.Id == id);
            return row is null
                ? new NativeSpaceNavigationDestination(id, "Unavailable Space " + id.ToString("D"), "Spaces", false, "This pinned Space is missing or archived.")
                : new NativeSpaceNavigationDestination(row.Id, SpaceChoiceLabel(row, _spaceRows), row.IconKey,
                    !row.IsArchived && (_openSpace is not null || _openSpaceInTransition is not null),
                    row.IsArchived ? "This Space is archived." : "Open this Space through its existing workspace owner.");
        }).ToArray();
        _scene.SetNavigationSnapshot(rows, preferences.Layout == SpaceSwitcherLayout.Rail ? NativeSpaceSwitcherLayout.Rail : NativeSpaceSwitcherLayout.Dropdown,
            preferences.IsCollapsed(_currentSpaceId));
    }

    /// <summary>The Space the sidebar is currently scoped to, or null for unscoped Chat.</summary>
    internal Guid? CurrentSpaceId => _currentSpaceId;


    private Task LoadSpaceScopeAsync() => RunSidebarAsync(LoadSpaceScopeCoreAsync);

    private async Task LoadSpaceScopeCoreAsync()
    {
        if (_spaces is null || _disposed) return;
        try
        {
            _spaceRows = await _spaces.GetAllAsync(false, _lifetime.Token);
            _currentSpaceId = await _spaces.GetCurrentSpaceIdAsync(_lifetime.Token);
            if (_disposed) return;
            ApplySpaceScopeToScene();
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            _originalWork.Executing?.Retain(exception);
            if (!_disposed) SetSidebarStatus("Spaces could not be loaded: " + exception.Message);
            throw;
        }
    }

    private void ApplySpaceScopeToScene()
    {
        var name = _currentSpaceId is { } id
            ? _spaceRows.FirstOrDefault(space => space.Id == id)?.Name
            : null;
        if (_currentSpaceId is { } && name is null) _currentSpaceId = null;
        _scene.SetSpaceScope(name, _currentSpaceId, IsAssistantSpace(_currentSpaceId),
            _spaceRows.FirstOrDefault(space => space.Id == _currentSpaceId)?.IconKey);
        _studyScene.SetSpaceScope(name, _currentSpaceId, IsAssistantSpace(_currentSpaceId));
        PublishNavigationPreferences();
    }

    private Task ShowSpacePickerAsync() => RunSidebarAsync(ShowSpacePickerCoreAsync);

    private async Task ShowSpacePickerCoreAsync()
    {
        if (_spaces is null) return;
        try
        {
            _spaceRows = await _spaces.GetAllAsync(false, _lifetime.Token);
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            _originalWork.Executing?.Retain(exception);
            if (!_disposed) SetSidebarStatus("Spaces could not be loaded: " + exception.Message);
            throw;
        }
        if (_disposed) return;

        var choices = new List<(string Label, Func<Task> Action)>
        {
            ("General (no Space)", () => SelectSpaceAsync(null))
        };
        choices.AddRange(_spaceRows
            .OrderBy(space => space.Name, StringComparer.OrdinalIgnoreCase)
            .Select(space => (SpaceChoiceLabel(space, _spaceRows), (Func<Task>)(() => OpenSpaceFromPickerAsync(space.Id)))));
        if (_manageSpaces is not null) choices.Add(("Manage Spaces…", ManageSpacesAsync));
        ShowChoices("Spaces", choices);
    }

    internal Task SelectSpaceFromShellAsync(Guid? spaceId, HavenMode? mode = null) => SelectSpaceAsync(spaceId, mode);

    internal Task OpenSpaceFromPickerAsync(Guid spaceId) => RunSidebarAsync(() => OpenSpaceFromPickerCoreEntryAsync(spaceId));

    private async Task OpenSpaceFromPickerCoreEntryAsync(Guid spaceId)
    {
        try { await OpenDestinationAsync(transition => OpenSpaceFromPickerCoreAsync(spaceId, transition), spaceId); }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception error) when (error is IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            _originalWork.Executing?.Retain(error);
            if (!_disposed) SetSidebarStatus("The Space could not be opened: " + error.Message);
            throw;
        }
    }

    private async Task OpenSpaceFromPickerCoreAsync(Guid spaceId, DestinationTransition transition)
    {
        var space = _spaces is null ? null : await _spaces.GetAsync(spaceId, _lifetime.Token);
        if (_disposed) return;
        if (space is null || space.IsArchived)
            throw new InvalidOperationException("The selected Space is unavailable. Refresh the Space list.");
        if (_openSpaceInTransition is not null) await _openSpaceInTransition(space, transition);
        else if (_openSpace is not null) await _openSpace(space);
        else if (space.Kind == SpaceKind.Assistants)
            throw new InvalidOperationException("Assistants needs its configured Assistants workspace on this host.");
        else await SelectSpaceUnderTransitionAsync(space.Id, null, transition);
    }

    internal Task SelectSpaceUnderTransitionAsync(Guid? spaceId, HavenMode? mode, DestinationTransition transition)
    {
        lock (_destinationGate)
        {
            if (_disposed || !ReferenceEquals(transition.Owner, this) || !ReferenceEquals(_destinationTransition, transition)
                || !transition.AcceptsScopeSelection || transition.RequestedSpaceId != spaceId || transition.ScopeSelectionIssued)
                throw new InvalidOperationException("This Space selection no longer belongs to the active destination request.");
            transition.ScopeSelectionIssued = true;
            return transition.ScopeSelection = SelectSpaceCoreAsync(spaceId, mode, transition);
        }
    }

    internal Task ReloadSpaceScopeAsync() => RunSidebarAsync(() => ReloadSpaceScopeCoreAsync());

    private async Task ReloadSpaceScopeCoreAsync()
    {
        await LoadSpaceScopeAsync();
        await RefreshAsync();
    }

    private Task SelectSpaceAsync(Guid? spaceId, HavenMode? requestedMode = null) =>
        OpenDestinationAsync(transition => SelectSpaceCoreAsync(spaceId, requestedMode, transition), spaceId);

    private async Task SelectSpaceCoreAsync(Guid? spaceId, HavenMode? requestedMode, DestinationTransition transition)
    {
        if (spaceId is { } selected && await RequiresAssistantOwnerAsync(selected))
        {
            await OpenSpaceFromPickerCoreAsync(selected, transition);
            return;
        }
        var mode = requestedMode ?? _currentMode;
        var candidates = await _conversations.GetRecentAsync(mode, int.MaxValue, _lifetime.Token);
        var existing = candidates
            .Where(chat => !chat.IsArchived && chat.Kind != ConversationKind.Call)
            .Where(chat => IsConversationInSpace(chat, spaceId, mode))
            .OrderByDescending(chat => chat.UpdatedAt)
            .FirstOrDefault();
        if (existing is not null)
        {
            await _openConversation(existing);
            if (_disposed) return;
            await _stateStore.MarkReadAsync(existing.Id, DateTimeOffset.UtcNow, _lifetime.Token);
        }
        else
        {
            await StartChatCoreAsync(null, transition, spaceId, mode, explicitSpace: true);
        }
        if (_disposed) return;
        // A requested scope is only published after its real workspace owner accepts.
        if (_spaces is not null)
            await _spaces.SetCurrentSpaceIdAsync(spaceId, _lifetime.Token);
        _currentSpaceId = spaceId;
        SetMode(mode);
        if (existing is not null)
        {
            _activeConversationId = existing.Id;
            _activeGroupId = existing.ContainerId;
        }
        ApplySpaceScopeToScene();
        await RefreshAsync();
    }

    public Task RefreshAsync()
    {
        _originalWork.DemandAdmission();
        if (_refreshOriginal is { IsCompleted: false } current)
        {
            _refreshPending = true;
            return current;
        }
        return _originalWork.RunAsync(_ => RefreshCoreAsync(), actual => _refreshOriginal = actual);
    }

    private async Task RefreshCoreAsync()
    {
        try
        {
            do
            {
                _refreshPending = false;
                var mode = _currentMode;
                var conversationTask = _conversations.GetRecentAsync(mode, 500, _lifetime.Token);
                var groupTask = _containers.GetByModeAsync(mode, _lifetime.Token);
                var stateTask = _stateStore.GetAllAsync(_lifetime.Token);
                var fileTask = _production is null || _currentMode != HavenMode.Chat
                    ? Task.FromResult<IReadOnlyList<MessageAttachment>>([])
                    : _production.GetRecentAttachmentsAsync(100, _lifetime.Token);
                await _originalWork.Executing!.AwaitAsync(Task.WhenAll(conversationTask, groupTask, stateTask, fileTask));

                if (_disposed) return;
                if (mode != _currentMode) { _refreshPending = true; continue; }
                _conversationRows = conversationTask.Result
                    .Where(item => !item.IsArchived && item.Kind != ConversationKind.Call)
                    .ToArray();
                _groupRows = groupTask.Result.Where(item => !item.IsArchived).ToArray();
                _fileRows = fileTask.Result;
                _states = stateTask.Result;

                if (Dispatcher.UIThread.CheckAccess()) Render();
                else await Dispatcher.UIThread.InvokeAsync(Render);
            }
            while (_refreshPending && !_disposed);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            _originalWork.Executing?.Retain(exception);
            if (!_disposed) await Dispatcher.UIThread.InvokeAsync(() => { if (!_disposed) SetSidebarStatus("Chat history could not be refreshed: " + exception.Message); });
            throw;
        }
    }

    internal HavenMode CurrentMode => _currentMode;
    internal Guid? ActiveGroupId => _activeGroupId;
    internal event EventHandler<NativeChatSidebarContext>? ContextChanged;

    public void SetActiveConversation(Guid? conversationId, Guid? groupId)
    {
        _originalWork.DemandAdmission();
        _activeConversationId = conversationId;
        _activeGroupId = groupId;
        if (_disposed) return;
        Render();
        ContextChanged?.Invoke(this, new NativeChatSidebarContext(_currentMode, _activeGroupId));
    }

    public void SetMode(HavenMode mode)
    {
        _originalWork.DemandAdmission();
        if (mode == HavenMode.Studio || _currentMode == mode) return;
        _currentMode = mode;
        _activeConversationId = null;
        _activeGroupId = null;
        _scene.SetMode(mode);
        _studyScene.SetMode(mode);
        Content = mode == HavenMode.Study ? SceneHost : _scene.Root;
        NavigationLayoutChanged?.Invoke(this, EventArgs.Empty);
        ContextChanged?.Invoke(this, new NativeChatSidebarContext(_currentMode, _activeGroupId));
        _ = RefreshAsync();
    }

    private void Render()
    {
        if (_disposed) return;
        bool Matches(string value) => _query.Length == 0 || value.Contains(_query, StringComparison.OrdinalIgnoreCase);
        bool InScope(Conversation chat) => IsConversationInCurrentSpace(chat);

        var scoped = _currentSpaceId is not null &&
            !(_currentMode == HavenMode.Chat && _currentSpaceId == SpaceRegistry.ChatSpaceId);
        var groups = scoped
            ? []
            : _groupRows
                .Where(group => Matches(group.Name))
                .OrderByDescending(GroupUpdatedAt)
                .ThenBy(group => group.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        var conversations = _conversationRows
            .Where(chat => InScope(chat) && Matches(chat.Title))
            .OrderByDescending(chat => chat.UpdatedAt)
            .ThenBy(chat => chat.Title, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var pinned = new List<ChatSidebarEntry>();
        pinned.AddRange(groups.Where(IsGroupPinned).Select(GroupEntry));
        pinned.AddRange(conversations.Where(chat => chat.IsPinned).Select(chat => ConversationEntry(chat, false)));

        var unread = conversations
            .Where(chat => !chat.IsPinned && IsUnread(chat))
            .Select(chat => ConversationEntry(chat, false))
            .ToArray();

        var groupEntries = new List<ChatSidebarEntry>();
        foreach (var group in groups.Where(group => !IsGroupPinned(group)))
        {
            var state = State(group.Id);
            groupEntries.Add(GroupEntry(group));
            if (!state.IsExpanded) continue;
            groupEntries.AddRange(_conversationRows
                .Where(chat => chat.ContainerId == group.Id && !chat.IsArchived && chat.Kind != ConversationKind.Call && InScope(chat))
                .OrderByDescending(chat => chat.UpdatedAt)
                .Select(chat => ConversationEntry(chat, true)));
        }

        var sourceConversationIds = conversations.Select(chat => chat.Id).ToHashSet();
        var files = _currentMode == HavenMode.Chat
            ? _fileRows
                .Where(file => sourceConversationIds.Contains(file.ConversationId) && Matches(file.OriginalName))
                .Take(20)
                .Select(FileEntry)
                .ToArray()
            : [];

        var chats = conversations
            .Where(chat => chat.ContainerId is null && !chat.IsPinned && !IsUnread(chat))
            .Select(chat => ConversationEntry(chat, false))
            .ToArray();

        _scene.SetRows(pinned, unread, groupEntries, files, chats);
        if (_currentMode == HavenMode.Study) _studyScene.SetRows(pinned, unread, groupEntries, files, chats);
        SetSidebarStatus(IsAssistantSpace(_currentSpaceId)
            ? "Open Assistants to continue in your Assistants workspace."
            : conversations.Length == 0 && groups.Length == 0 && files.Length == 0
                ? $"No saved {ModeName(_currentMode)} chats or {GroupName(_currentMode, plural: true)} yet."
                : null);
    }

    private bool IsConversationInCurrentSpace(Conversation chat) => IsConversationInSpace(chat, _currentSpaceId, _currentMode);

    private bool IsConversationInSpace(Conversation chat, Guid? spaceId, HavenMode mode) =>
        !IsAssistantSpace(spaceId) &&
        (chat.SpaceId == spaceId ||
         (mode == HavenMode.Chat && spaceId == SpaceRegistry.ChatSpaceId && chat.SpaceId is null));

    private bool IsAssistantSpace(Guid? spaceId) => spaceId == SpaceRegistry.AssistantsSpaceId ||
        (spaceId is { } id && _spaceRows.Any(space => space.Id == id && space.Kind == SpaceKind.Assistants));

    internal static string SpaceChoiceLabel(SpaceDefinition space, IReadOnlyList<SpaceDefinition> spaces) =>
        spaces.Any(other => other.Id != space.Id && other.Name.Equals(space.Name, StringComparison.OrdinalIgnoreCase))
            ? $"{space.Name} ({(space.IsBuiltIn ? "built-in" : "custom")})"
            : space.Name;

    private async Task<bool> RequiresAssistantOwnerAsync(Guid spaceId) =>
        spaceId == SpaceRegistry.AssistantsSpaceId ||
        (_spaces is not null && (await _spaces.GetAsync(spaceId, _lifetime.Token))?.Kind == SpaceKind.Assistants);

    private ChatSidebarEntry ConversationEntry(Conversation chat, bool indented) => new(
        ChatSidebarEntryKind.Conversation,
        chat.Id,
        chat.Title,
        _activeConversationId == chat.Id,
        IsUnread(chat),
        chat.IsPinned,
        false,
        indented);

    private ChatSidebarEntry FileEntry(MessageAttachment file) => new(
        ChatSidebarEntryKind.File,
        file.Id,
        file.OriginalName,
        _activeConversationId == file.ConversationId,
        false,
        false);

    private ChatSidebarEntry GroupEntry(ContainerDefinition group)
    {
        var state = State(group.Id);
        return new ChatSidebarEntry(
            ChatSidebarEntryKind.Group,
            group.Id,
            group.Name,
            _activeGroupId == group.Id,
            IsGroupUnread(group),
            state.IsPinned,
            state.IsExpanded);
    }

    internal Task OpenFileAsync(Guid attachmentId) => RunSidebarAsync(() => OpenFileCoreAsync(attachmentId));

    private async Task OpenFileCoreAsync(Guid attachmentId)
    {
        var file = _fileRows.FirstOrDefault(item => item.Id == attachmentId);
        if (file is null) return;
        var chat = _conversationRows.FirstOrDefault(item => item.Id == file.ConversationId);
        if (chat is null)
        {
            SetSidebarStatus("The chat that owns this file is no longer available.");
            return;
        }

        try
        {
            await OpenConversationAsync(chat);
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            _originalWork.Executing?.Retain(exception);
            if (!_disposed) SetSidebarStatus(exception.Message);
            throw;
        }
    }

    internal Task HandleConversationActionAsync(ChatSidebarConversationRequest request) => RunSidebarAsync(() => HandleConversationActionCoreAsync(request));

    private async Task HandleConversationActionCoreAsync(ChatSidebarConversationRequest request)
    {
        var chat = _conversationRows.FirstOrDefault(item => item.Id == request.ConversationId);
        if (chat is null) return;
        try
        {
            switch (request.Action)
            {
                case ChatSidebarConversationAction.Open:
                    await OpenConversationAsync(chat);
                    break;
                case ChatSidebarConversationAction.Rename:
                    ShowTextPrompt("Rename chat", chat.Title, "Save", async title =>
                    {
                        await _conversations.UpsertConversationAsync(chat with { Title = title, UpdatedAt = DateTimeOffset.UtcNow }, _lifetime.Token);
                        await RefreshAsync();
                    });
                    break;
                case ChatSidebarConversationAction.TogglePin:
                    await ToggleConversationPinAsync(chat);
                    break;
                case ChatSidebarConversationAction.ToggleRead:
                    await ToggleConversationReadAsync(chat);
                    break;
                case ChatSidebarConversationAction.Move:
                    ShowMoveChoices(chat);
                    break;
                case ChatSidebarConversationAction.MoveToSpace:
                    ShowSpaceChoices(chat);
                    break;
                case ChatSidebarConversationAction.Archive:
                    await ArchiveConversationAsync(chat);
                    break;
                case ChatSidebarConversationAction.Delete:
                    await DeleteConversationAsync(chat);
                    break;
            }
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            _originalWork.Executing?.Retain(exception);
            if (!_disposed) SetSidebarStatus(exception.Message);
            throw;
        }
    }

    internal Task HandleGroupActionAsync(ChatSidebarGroupRequest request) => RunSidebarAsync(() => HandleGroupActionCoreAsync(request));

    private async Task HandleGroupActionCoreAsync(ChatSidebarGroupRequest request)
    {
        var group = _groupRows.FirstOrDefault(item => item.Id == request.GroupId);
        if (group is null) return;
        var state = State(group.Id);
        try
        {
            switch (request.Action)
            {
                case ChatSidebarGroupAction.Open:
                    await OpenDestinationAsync(async () =>
                    {
                        await _openGroup(group);
                        if (_disposed) return;
                        await _stateStore.MarkReadAsync(group.Id, DateTimeOffset.UtcNow, _lifetime.Token);
                        _activeGroupId = group.Id;
                        await RefreshAsync();
                    });
                    break;
                case ChatSidebarGroupAction.Toggle:
                case ChatSidebarGroupAction.ToggleExpand:
                    await _stateStore.SetExpandedAsync(group.Id, !state.IsExpanded, _lifetime.Token);
                    await RefreshAsync();
                    break;
                case ChatSidebarGroupAction.Rename:
                    ShowTextPrompt($"Rename {GroupName(_currentMode, false)}", group.Name, "Save", async name =>
                    {
                        await _containers.UpsertAsync(group with { Name = name, UpdatedAt = DateTimeOffset.UtcNow }, _lifetime.Token);
                        await RefreshAsync();
                    });
                    break;
                case ChatSidebarGroupAction.TogglePin:
                    await _stateStore.SetPinnedAsync(group.Id, !state.IsPinned, _lifetime.Token);
                    await RefreshAsync();
                    break;
                case ChatSidebarGroupAction.NewChat:
                    await StartChatAsync(group.Id);
                    break;
                case ChatSidebarGroupAction.Archive:
                    await ArchiveGroupAsync(group);
                    break;
                case ChatSidebarGroupAction.Delete:
                    await DeleteGroupAsync(group);
                    break;
            }
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            _originalWork.Executing?.Retain(exception);
            if (!_disposed) SetSidebarStatus(exception.Message);
            throw;
        }
    }

    private void ShowCreateGroupPrompt()
    {
        var groupName = GroupName(_currentMode, false);
        ShowTextPrompt($"New {groupName}", string.Empty, $"Create {groupName}", async name =>
        {
            var now = DateTimeOffset.UtcNow;
            var group = new ContainerDefinition(Guid.NewGuid(), _currentMode, name, null, string.Empty, string.Empty, now, now);
            if (_currentMode == HavenMode.Study) await _containers.CreateSubjectAsync(group, _lifetime.Token);
            else await _containers.UpsertAsync(group, _lifetime.Token);
            await _stateStore.SetExpandedAsync(group.Id, true, _lifetime.Token);
            await OpenDestinationAsync(async () =>
            {
                await _openGroup(group);
                if (_disposed) return;
                _activeGroupId = group.Id;
                await RefreshAsync();
            });
        });
    }

    private void ShowMoveChoices(Conversation chat)
    {
        var choices = new List<(string Label, Func<Task> Action)>
        {
            ("No group", () => RunSidebarAsync(() => MoveConversationAsync(chat, null)))
        };
        choices.AddRange(_groupRows
            .OrderBy(group => group.Name, StringComparer.OrdinalIgnoreCase)
            .Select(group => (group.Name, (Func<Task>)(() => RunSidebarAsync(() => MoveConversationAsync(chat, group.Id))))));
        ShowChoices($"Move to {GroupName(_currentMode, false)}", choices);
    }

    private void ShowSpaceChoices(Conversation chat)
    {
        var current = chat.SpaceId;
        var choices = new List<(string Label, Func<Task> Action)>
        {
            ("No Space", () => RunSidebarAsync(() => MoveConversationToSpaceAsync(chat, null)))
        };
        choices.AddRange(_spaceRows
            .OrderBy(space => space.Name, StringComparer.OrdinalIgnoreCase)
            .Where(space => space.Id != current && space.Kind != SpaceKind.Assistants)
            .Select(space => (SpaceChoiceLabel(space, _spaceRows), (Func<Task>)(() => RunSidebarAsync(() => MoveConversationToSpaceAsync(chat, space.Id))))));
        ShowChoices("Move to Space", choices);
    }

    private async Task MoveConversationToSpaceAsync(Conversation chat, Guid? spaceId)
    {
        if (spaceId is { } selected && await RequiresAssistantOwnerAsync(selected))
        {
            throw new InvalidOperationException("Open Assistants to create or organise conversations with their canonical Assistant identity.");
        }
        await _conversations.UpsertConversationAsync(chat with { SpaceId = spaceId, UpdatedAt = DateTimeOffset.UtcNow }, _lifetime.Token);
        await RefreshAsync();
    }

    private Task OpenDestinationAsync(Func<Task> open) => OpenDestinationAsync(_ => open(), requestedSpaceId: null);

    private Task OpenDestinationAsync(Func<DestinationTransition, Task> open, Guid? requestedSpaceId) =>
        RunSidebarAsync(() => OpenDestinationCoreAsync(open, requestedSpaceId));

    private async Task OpenDestinationCoreAsync(Func<DestinationTransition, Task> open, Guid? requestedSpaceId)
    {
        DestinationTransition transition;
        lock (_destinationGate)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(NativeChatSidebar));
            if (_destinationTransition is not null)
                throw new InvalidOperationException("Wait for the current destination to finish opening before choosing another.");
            _destinationTransition = transition = new(this, requestedSpaceId);
        }
        var failures = new List<Exception>();
        var observed = new HashSet<Exception>(ReferenceEqualityComparer.Instance);
        var faultedCancellation = false;
        void Retain(Exception error, Task? source = null)
        {
            if (error is OperationCanceledException && source?.IsCanceled != true) faultedCancellation = true;
            if (source?.Exception is { } group) { faultedCancellation = true; Retain(group); return; }
            if (error is AggregateException { InnerExceptions.Count: > 0 } aggregate) foreach (var inner in aggregate.InnerExceptions) Retain(inner);
            else if (observed.Add(error)) failures.Add(error);
        }
        Task? original = null;
        try { SetNewChatBusy(true); original = open(transition); await original; }
        catch (Exception error) { Retain(error, original); }
        Task? selected;
        lock (_destinationGate)
        {
            transition.AcceptsScopeSelection = false;
            selected = transition.ScopeSelection;
        }
        // Retain a scope callback even if its owner accidentally returns before awaiting it.
        if (selected is not null)
        {
            try { await selected; }
            catch (Exception error) { Retain(error, selected); }
        }
        lock (_destinationGate) if (ReferenceEquals(_destinationTransition, transition)) _destinationTransition = null;
        try { if (!_disposed) SetNewChatBusy(false); }
        catch (Exception error) { Retain(error); }
        if (failures.Count == 1 && (!faultedCancellation || failures[0] is not OperationCanceledException))
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failures[0]).Throw();
        if (failures.Count > 0) throw new AggregateException(failures);
    }

    private Task OpenConversationAsync(Conversation chat) => OpenDestinationAsync(async () =>
    {
        await _openConversation(chat);
        if (_disposed) return;
        await _stateStore.MarkReadAsync(chat.Id, DateTimeOffset.UtcNow, _lifetime.Token);
        _activeConversationId = chat.Id;
        _activeGroupId = chat.ContainerId;
        await RefreshAsync();
    });

    private Task StartChatAsync(Guid? groupId) => OpenDestinationAsync(transition => StartChatCoreAsync(groupId, transition), _currentSpaceId);

    private async Task StartChatCoreAsync(Guid? groupId, DestinationTransition transition, Guid? targetSpaceId = null, HavenMode? requestedMode = null, bool explicitSpace = false)
    {
        var spaceId = explicitSpace ? targetSpaceId : _currentSpaceId;
        var mode = requestedMode ?? _currentMode;
        if (spaceId is { } selected && await RequiresAssistantOwnerAsync(selected))
        {
            await OpenSpaceFromPickerCoreAsync(selected, transition);
            return;
        }
        if (_startChatInSpace is not null) await _startChatInSpace(mode, groupId, spaceId);
        else if (spaceId == _currentSpaceId) await _startChat(mode, groupId);
        else throw new InvalidOperationException("This host has no configured owner for starting a conversation in another Space.");
        if (_disposed) return;
        if (groupId is Guid id) await _stateStore.SetExpandedAsync(id, true, _lifetime.Token);
        _activeGroupId = groupId;
        await RefreshAsync();
    }

    private async Task ToggleConversationPinAsync(Conversation chat)
    {
        await _conversations.UpsertConversationAsync(chat with { IsPinned = !chat.IsPinned, UpdatedAt = DateTimeOffset.UtcNow }, _lifetime.Token);
        await RefreshAsync();
    }

    private async Task ToggleConversationReadAsync(Conversation chat)
    {
        if (IsUnread(chat)) await _stateStore.MarkReadAsync(chat.Id, DateTimeOffset.UtcNow, _lifetime.Token);
        else await _stateStore.MarkUnreadAsync(chat.Id, _lifetime.Token);
        await RefreshAsync();
    }

    private async Task MoveConversationAsync(Conversation chat, Guid? groupId)
    {
        await _conversations.UpsertConversationAsync(chat with { ContainerId = groupId, UpdatedAt = DateTimeOffset.UtcNow }, _lifetime.Token);
        await RefreshAsync();
    }

    private async Task ArchiveConversationAsync(Conversation chat)
    {
        await _conversations.UpsertConversationAsync(chat with { IsArchived = true, UpdatedAt = DateTimeOffset.UtcNow }, _lifetime.Token);
        await RefreshAsync();
    }

    private async Task DeleteConversationAsync(Conversation chat)
    {
        var wasActive = _activeConversationId == chat.Id;
        await _conversations.DeleteConversationAsync(chat.Id, _lifetime.Token);
        if (wasActive) await StartChatAsync(null);
        else await RefreshAsync();
    }

    private async Task ArchiveGroupAsync(ContainerDefinition group)
    {
        await _containers.UpsertAsync(group with { IsArchived = true, UpdatedAt = DateTimeOffset.UtcNow }, _lifetime.Token);
        await RefreshAsync();
    }

    private async Task DeleteGroupAsync(ContainerDefinition group)
    {
        await _containers.DeleteAndDetachConversationsAsync(group.Id, _lifetime.Token);
        if (_activeGroupId == group.Id) _activeGroupId = null;
        await RefreshAsync();
    }

    private NativeChatItemState State(Guid id) => _states.TryGetValue(id, out var state) ? state : NativeChatItemState.Empty;
    private bool IsUnread(Conversation chat) => _activeConversationId != chat.Id && State(chat.Id).IsUnread(chat.UpdatedAt);
    private bool IsGroupPinned(ContainerDefinition group) => State(group.Id).IsPinned;
    private bool IsGroupUnread(ContainerDefinition group) => _activeGroupId != group.Id && State(group.Id).IsUnread(GroupUpdatedAt(group));

    private DateTimeOffset GroupUpdatedAt(ContainerDefinition group)
    {
        var childUpdate = _conversationRows
            .Where(chat => chat.ContainerId == group.Id)
            .Select(chat => chat.UpdatedAt)
            .DefaultIfEmpty(group.UpdatedAt)
            .Max();
        return childUpdate > group.UpdatedAt ? childUpdate : group.UpdatedAt;
    }

    private static string ModeName(HavenMode mode) => mode switch
    {
        HavenMode.Study => "Study",
        HavenMode.Tasks => "Tasks",
        _ => "Chat"
    };

    private static string GroupName(HavenMode mode, bool plural) => mode switch
    {
        HavenMode.Study => plural ? "Subjects" : "Subject",
        HavenMode.Tasks => plural ? "Task Groups" : "Task Group",
        _ => plural ? "Chat Groups" : "Chat Group"
    };

    public void RequestRetirement() => _originalWork.RequestRetirement();
    public Task CloseAndDrainAsync() => _originalWork.CloseAndDrainAsync();
    public void DemandExternalOriginalRetirementJoin() => _originalWork.DemandExternalClose();
    public void Dispose() => RequestRetirement();
    public ValueTask DisposeAsync() => new(CloseAndDrainAsync());

    private Task StopOriginalAsync()
    {
        _disposed = true;
        _lifetime.Cancel();
        return Task.CompletedTask;
    }

    private async Task CleanupOriginalAsync()
    {
        try { await _scene.CloseAsync(); }
        finally
        {
            _studyScene.SearchChanged -= OnStudySearchChanged;
            _studyScene.NewChatRequested -= OnStudyNewChat;
            _studyScene.NewGroupRequested -= OnStudyNewGroup;
            _studyScene.SpacePickerRequested -= OnStudySpacePicker;
            _studyScene.ConversationActionRequested -= OnStudyConversation;
            _studyScene.GroupActionRequested -= OnStudyGroup;
            _studyScene.FileRequested -= OnStudyFile;
            _scene.LayoutChanged -= OnNavigationLayoutChanged;
            _studyScene.Dispose(); _lifetime.Dispose(); Content = null;
        }
    }
}
