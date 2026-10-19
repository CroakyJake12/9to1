using Haven.Application;
using Haven.Core;

namespace HavenOS.Apps.Spaces;

/// <summary>
/// Host-side contract for opening the protected built-in Events &amp; Extensions editor and the
/// full user-created Space graph editor. These surfaces are owned by Home's Node Graph Framework;
/// the Spaces Home controller only requests them through this typed adapter so the authored CUI
/// never reaches into another app's domain.
/// </summary>
public interface ISpacesExtensionEditorHost
{
    /// <summary>Opens the limited Events &amp; Extensions editor for a built-in Space.</summary>
    Task OpenEventsAndExtensionsAsync(Guid spaceId, CancellationToken cancellationToken = default);

    /// <summary>Opens the full Space graph editor for a user-created Space.</summary>
    Task OpenSpaceGraphEditorAsync(Guid spaceId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Optional admission contract for a <see cref="ISpacesActionHost"/> that queues navigation
/// separately from the page action. When present, the controller reports an accepted queue
/// admission as "Opening" rather than the completed "Opened" used by direct hosts.
/// </summary>
public interface ISpacesActionAdmissionHost
{
    /// <summary>True when the host returns admission immediately and navigation is owned elsewhere.</summary>
    bool IsQueueAdmission { get; }
}

/// <summary>
/// A single row in the Spaces home sidebar. Stable identity is the registered Space ID;
/// display names and positions are mutable.
/// </summary>
public sealed record SpacesHomeItem(
    Guid Id,
    string Name,
    string Summary,
    string ScopeKey,
    bool IsBuiltIn,
    Guid? ParentSpaceId,
    int Position,
    long Revision,
    bool CanEdit)
{
    /// <summary>
    /// Returns the Space name so native selector controls without an explicit display-member
    /// binding render the intended label instead of the record's CLR format.
    /// </summary>
    public override string ToString() => Name;

    // Canonical registry/catalog identity; names never choose a Space's icon.
    public string IconKey { get; init; } = "sparkles";
}

/// <summary>
/// One recent conversation in the currently selected Space, projected for the contextual
/// sidebar. The owning native surface supplies these rows from the real conversation store;
/// they are scoped to one Space and never form a cross-Space history list.
/// </summary>
public sealed record SpacesHomeConversationRow(
    Guid Id,
    string Title,
    string UpdatedLabel,
    bool IsCurrent)
{
    public override string ToString() => Title;
}

public sealed record SpacesHomeContextReference(
    Guid ContextId,
    string Label,
    string PermissionLabel,
    string IndexStateLabel);

/// <summary>
/// One declared custom sidebar element for the selected Space, projected from the extension's stable
/// <c>SidebarItemId</c>. The identity is what the sidebar keys and any visibility rule address; it is
/// never a generated per-render value.
/// </summary>
public sealed record SpacesHomeSidebarRow(
    string SidebarItemId,
    string Label,
    string IconKey,
    string TargetKind,
    string TargetId,
    bool IsActionable,
    string UnavailableReason = "")
{
    public string OpenLabel => IsActionable
        ? $"{Label} ({TargetKind})"
        : $"{Label} ({TargetKind}) - {UnavailableReason}";
    public override string ToString() => Label;
}

public sealed record SpacesHomeCapability(
    string Id,
    string Label);

public sealed record SpacesHomeSelection(
    Guid Id,
    string Name,
    string Description,
    bool IsBuiltIn,
    bool CanEdit,
    bool IsRenaming,
    Guid? ParentSpaceId,
    string EffectiveModelLabel,
    string WebAccessLabel,
    string MemoryScopeLabel,
    IReadOnlyList<SpacesHomeContextReference> ContextReferences,
    IReadOnlyList<SpacesHomeCapability> Capabilities,
    bool HasExtensionEditor,
    bool CanEditExtensions,
    bool CanEditGraph)
{
    public bool CanEditDetails { get; init; }
}

public sealed record SpacesHomeMoveCandidate(Guid? Id, string Name)
{
    public override string ToString() => Name;
}

public sealed record SpacesHomeViewState(
    string Query,
    IReadOnlyList<SpacesHomeItem> BuiltIns,
    IReadOnlyList<SpacesHomeItem> CustomSpaces,
    IReadOnlyList<SpacesHomeMoveCandidate> MoveParentOptions,
    SpacesHomeSelection? Selection,
    string StatusMessage,
    bool IsLoading,
    int Revision,
    Guid? PendingRenameSpaceId = null,
    string RenameDraft = "",
    int SelectedMoveParentIndex = 0,
    // The main sidebar is contextual to the selected Space and collapsible, and its header owns the
    // Space switcher and the adjacent quick-action overflow. These are presentation identities only;
    // they never change which Space is selected or what a conversation contains.
    bool SidebarCollapsed = false,
    bool SpaceSwitcherOpen = false,
    bool SpaceOverflowOpen = false)
{
    public static SpacesHomeViewState Empty { get; } = new(
        string.Empty,
        [],
        [],
        [new SpacesHomeMoveCandidate(null, "(no parent)")],
        null,
        "Ready",
        false,
        0);
}

/// <summary>
/// Platform-neutral controller for the Spaces home surface. It does not own persistence;
/// every mutation delegates to the canonical <see cref="SpaceRegistry"/> through
/// <see cref="SpacesModel"/>.
/// </summary>
public sealed class SpacesHomeController
{
    private readonly SpaceRegistry _registry;
    private readonly SpacesModel _model;
    private readonly ISpacesActionHost? _actionHost;
    private readonly ISpacesExtensionEditorHost? _extensionEditorHost;
    private readonly Func<string, CancellationToken, Task<string>>? _pickFileAsync;
    private readonly Func<CancellationToken, Task>? _afterRegistryRead;
    private long _requestVersion;
    private Guid? _selectedId;
    private Guid? _pendingRenameSpaceId;
    private string _renameDraft = string.Empty;
    private int _selectedMoveParentIndex;
    private IReadOnlyList<SpacesHomeItem> _fullBuiltIns = [];
    private IReadOnlyList<SpacesHomeItem> _fullCustomSpaces = [];

    public SpacesHomeController(
        SpaceRegistry registry,
        ISpacesActionHost? actionHost = null,
        ISpacesExtensionEditorHost? extensionEditorHost = null,
        Func<string, CancellationToken, Task<string>>? pickFileAsync = null)
        : this(registry, actionHost, extensionEditorHost, pickFileAsync, null)
    {
    }

    // Tests can hold an already-returned response without holding the registry's
    // persistence lock. Normal hosts retain the public constructor and no callback.
    internal SpacesHomeController(
        SpaceRegistry registry,
        ISpacesActionHost? actionHost,
        ISpacesExtensionEditorHost? extensionEditorHost,
        Func<string, CancellationToken, Task<string>>? pickFileAsync,
        Func<CancellationToken, Task>? afterRegistryRead)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _model = new SpacesModel(registry);
        _actionHost = actionHost;
        _extensionEditorHost = extensionEditorHost;
        _pickFileAsync = pickFileAsync;
        _afterRegistryRead = afterRegistryRead;
    }

    public SpacesHomeViewState State { get; private set; } = SpacesHomeViewState.Empty;

    public event Action<SpacesHomeViewState>? StateChanged;

    public Task LoadAsync(CancellationToken cancellationToken = default)
        => LoadCoreAsync(new LoadIntent(), cancellationToken);

    private async Task LoadCoreAsync(LoadIntent intent, CancellationToken cancellationToken)
    {
        var request = Interlocked.Increment(ref _requestVersion);
        var admittedBase = State.Revision;

        // Admission: publish the loading state. If another operation already advanced the view,
        // this load is stale and must not mutate backing fields or publish a newer snapshot.
        if (!TryPublish(State with { IsLoading = true, StatusMessage = "Loading Spaces…" }, admittedBase))
            return;

        // The revision we admit against is the one after the loading publish. The final publish
        // must match exactly this admitted revision so a normal load succeeds, while any
        // intervening mutation/query still causes a stale-load drop.
        var publishBase = State.Revision;

        try
        {
            var registered = await _registry.GetAllAsync(includeArchived: false, cancellationToken).ConfigureAwait(false);
            if (_afterRegistryRead is not null)
                await _afterRegistryRead(cancellationToken).ConfigureAwait(false);
            if (request != Volatile.Read(ref _requestVersion)) return;

            var builtIns = SpacesModel.BuiltIns
                .Select(def => ToHomeItem(def, registered))
                .ToArray();
            var custom = registered
                .Where(space => !space.IsBuiltIn && !space.IsArchived)
                .Select(ToHomeItem)
                .ToArray();

            var selectedId = intent.SelectedId ?? _selectedId;
            if (selectedId is null)
            {
                var currentSpaceId = await _registry.GetCurrentSpaceIdAsync(cancellationToken).ConfigureAwait(false);
                if (currentSpaceId is not null && registered.Any(space => space.Id == currentSpaceId))
                    selectedId = currentSpaceId;
            }
            if (selectedId is null || !registered.Any(space => space.Id == selectedId))
                selectedId = builtIns.FirstOrDefault()?.Id ?? custom.FirstOrDefault()?.Id;

            // An explicit rename intent (RenameDraft provided) clears or sets the pending rename;
            // a refresh intent (RenameDraft omitted) preserves the existing pending rename.
            var pendingRenameSpaceId = intent.RenameDraft is not null ? intent.PendingRenameSpaceId : _pendingRenameSpaceId;
            var renameDraft = intent.RenameDraft ?? _renameDraft;

            var selection = BuildSelection(registered, selectedId, pendingRenameSpaceId);
            var moveOptions = BuildMoveParentOptions(registered, selectedId);
            var moveIndex = ResolveMoveParentIndex(moveOptions, selection?.ParentSpaceId);

            var filteredBuiltIns = Filter(builtIns, State.Query);
            var filteredCustom = Filter(custom, State.Query);

            var published = TryPublish(new SpacesHomeViewState(
                State.Query,
                filteredBuiltIns,
                filteredCustom,
                moveOptions,
                selection,
                "Spaces ready",
                false,
                publishBase + 1,
                pendingRenameSpaceId,
                renameDraft,
                moveIndex), publishBase);

            if (published)
            {
                _fullBuiltIns = builtIns;
                _fullCustomSpaces = custom;
                _selectedId = selectedId;
                _pendingRenameSpaceId = pendingRenameSpaceId;
                _renameDraft = renameDraft;
                _selectedMoveParentIndex = moveIndex;
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            if (request == Volatile.Read(ref _requestVersion))
                TryPublish(State with { IsLoading = false, StatusMessage = $"Spaces could not load: {exception.Message}" }, publishBase);
        }
    }

    private readonly record struct LoadIntent(Guid? SelectedId = null, Guid? PendingRenameSpaceId = null, string? RenameDraft = null);

    public void SetQuery(string? query)
    {
        var normalized = query?.Trim() ?? string.Empty;
        var baseRevision = State.Revision;
        var filteredBuiltIns = Filter(_fullBuiltIns, normalized);
        var filteredCustom = Filter(_fullCustomSpaces, normalized);
        TryPublish(State with
        {
            Query = normalized,
            BuiltIns = filteredBuiltIns,
            CustomSpaces = filteredCustom
        }, baseRevision);
    }

    public async Task SelectSpaceAsync(Guid spaceId, CancellationToken cancellationToken = default)
    {
        _pendingRenameSpaceId = null;
        _renameDraft = string.Empty;
        await LoadCoreAsync(new LoadIntent(SelectedId: spaceId, PendingRenameSpaceId: null, RenameDraft: string.Empty), cancellationToken).ConfigureAwait(false);
        try { await _registry.SetCurrentSpaceIdAsync(spaceId, cancellationToken).ConfigureAwait(false); }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            PublishStatus($"Could not persist selected Space: {exception.Message}");
        }
    }

    public async Task CreateCustomSpaceAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var created = await _model.CreateCustomSpaceAsync("New Space", null, cancellationToken).ConfigureAwait(false);
            var id = created.Scope.RegisteredSpaceId ?? throw new InvalidOperationException("Created Space did not receive an identifier.");
            await LoadCoreAsync(
                new LoadIntent(SelectedId: id, PendingRenameSpaceId: null, RenameDraft: string.Empty),
                cancellationToken).ConfigureAwait(false);
            try { await _registry.SetCurrentSpaceIdAsync(id, cancellationToken).ConfigureAwait(false); }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                PublishStatus($"Could not persist selected Space: {exception.Message}");
            }
            if (_actionHost is not null)
                await EditDetailsAsync(id, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            PublishStatus($"Could not create Space: {exception.Message}");
        }
    }

    public async Task RenameSpaceAsync(Guid spaceId, string newName, CancellationToken cancellationToken = default)
    {
        if (_fullBuiltIns.Any(item => item.Id == spaceId))
        {
            PublishStatus("Built-in Spaces cannot be renamed.");
            return;
        }
        var item = _fullCustomSpaces.FirstOrDefault(space => space.Id == spaceId);
        if (item is null)
        {
            PublishStatus("Space was not found.");
            return;
        }
        await RenameWithExpectedRevisionAsync(spaceId, newName, item.Revision, cancellationToken).ConfigureAwait(false);
    }

    private async Task RenameWithExpectedRevisionAsync(Guid spaceId, string newName, long expectedRevision, CancellationToken cancellationToken)
    {
        try
        {
            var scope = SpaceScope.ForCustom(spaceId);
            var registered = await _registry.GetAsync(spaceId, cancellationToken).ConfigureAwait(false);
            if (registered is null) throw new KeyNotFoundException("Space was not found.");
            if (registered.IsBuiltIn) throw new InvalidOperationException("Built-in Spaces cannot be renamed.");
            if (registered.Revision != expectedRevision)
                throw new SpaceRevisionConflictException(spaceId, expectedRevision, registered.Revision);
            await _model.RenameCustomSpaceAsync(scope, newName, expectedRevision, cancellationToken).ConfigureAwait(false);
            _pendingRenameSpaceId = null;
            _renameDraft = string.Empty;
            await LoadCoreAsync(
                new LoadIntent(PendingRenameSpaceId: null, RenameDraft: string.Empty),
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            PublishStatus($"Could not rename Space: {exception.Message}");
        }
    }

    public async Task StartRenameAsync(Guid spaceId, CancellationToken cancellationToken = default)
    {
        if (!_fullCustomSpaces.Any(space => space.Id == spaceId)) return;
        // A row command can target a different Space from the details pane. Select it
        // through the canonical registry before exposing an editor for that resource.
        if (State.Selection?.Id != spaceId)
            await SelectSpaceAsync(spaceId, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        StartRename(spaceId);
    }

    public void StartRename(Guid spaceId)
    {
        var item = _fullCustomSpaces.FirstOrDefault(space => space.Id == spaceId);
        var selection = State.Selection;
        if (item is null || selection?.Id != spaceId || !selection.CanEdit) return;
        var baseRevision = State.Revision;
        if (TryPublish(State with
        {
            PendingRenameSpaceId = spaceId,
            Selection = selection with { IsRenaming = true },
            RenameDraft = item.Name,
            StatusMessage = "Rename the Space and confirm."
        }, baseRevision))
        {
            _pendingRenameSpaceId = spaceId;
            _renameDraft = item.Name;
        }
    }

    /// <summary>Collapses or expands the contextual main sidebar. Collapsing also closes the Space
    /// switcher and the quick-action overflow so no hidden control stays reachable.</summary>
    public void ToggleSidebar()
    {
        var baseRevision = State.Revision;
        var collapsed = !State.SidebarCollapsed;
        TryPublish(State with
        {
            SidebarCollapsed = collapsed,
            SpaceSwitcherOpen = false,
            SpaceOverflowOpen = false,
            StatusMessage = collapsed ? "Sidebar collapsed." : "Sidebar expanded."
        }, baseRevision);
    }

    /// <summary>Activates the header control that switches between Spaces. Only one header popover is
    /// open at a time, so the surface stays deterministic and inspectable.</summary>
    public void ToggleSpaceSwitcher()
    {
        var baseRevision = State.Revision;
        if (State.SidebarCollapsed) return;
        var open = !State.SpaceSwitcherOpen;
        TryPublish(State with
        {
            SpaceSwitcherOpen = open,
            SpaceOverflowOpen = false,
            StatusMessage = open ? "Choose a Space." : "Space switcher closed."
        }, baseRevision);
    }

    /// <summary>Activates the overflow control beside the header, holding the quick actions for the
    /// currently selected Space.</summary>
    public void ToggleSpaceOverflow()
    {
        var baseRevision = State.Revision;
        if (State.SidebarCollapsed) return;
        var open = !State.SpaceOverflowOpen;
        TryPublish(State with
        {
            SpaceOverflowOpen = open,
            SpaceSwitcherOpen = false,
            StatusMessage = open ? "Quick actions for this Space." : "Quick actions closed."
        }, baseRevision);
    }

    public void SetRenameDraft(string? draft)
    {
        var normalized = draft?.Trim() ?? string.Empty;
        var baseRevision = State.Revision;
        if (TryPublish(State with { RenameDraft = normalized }, baseRevision))
            _renameDraft = normalized;
    }

    public async Task ConfirmRenameAsync(CancellationToken cancellationToken = default)
    {
        if (_pendingRenameSpaceId is not { } id || string.IsNullOrWhiteSpace(_renameDraft)) return;
        var item = _fullCustomSpaces.FirstOrDefault(space => space.Id == id);
        if (item is null) return;
        await RenameWithExpectedRevisionAsync(id, _renameDraft, item.Revision, cancellationToken).ConfigureAwait(false);
    }

    public void CancelRename()
    {
        var baseRevision = State.Revision;
        if (TryPublish(State with
        {
            PendingRenameSpaceId = null,
            Selection = State.Selection is { } selection ? selection with { IsRenaming = false } : null,
            RenameDraft = string.Empty,
            StatusMessage = "Rename cancelled"
        }, baseRevision))
        {
            _pendingRenameSpaceId = null;
            _renameDraft = string.Empty;
        }
    }

    public async Task MoveSpaceAsync(Guid spaceId, CancellationToken cancellationToken = default)
    {
        var moving = _fullCustomSpaces.FirstOrDefault(space => space.Id == spaceId)
            ?? _fullBuiltIns.FirstOrDefault(space => space.Id == spaceId);
        if (moving is null)
        {
            PublishStatus("Space was not found.");
            return;
        }
        if (moving.IsBuiltIn)
        {
            PublishStatus("Built-in Spaces cannot be moved.");
            return;
        }

        // Capture the intended parent and the revision we are reviewing *before* any async work.
        // Resolving a stale index against a newer list would let the user confirm a move they did
        // not review.
        var candidate = State.MoveParentOptions.ElementAtOrDefault(State.SelectedMoveParentIndex)
            ?? State.MoveParentOptions.FirstOrDefault()
            ?? new SpacesHomeMoveCandidate(null, "(no parent)");
        var expectedRevision = moving.Revision;

        try
        {
            var scope = SpaceScope.ForCustom(spaceId);
            var registered = await _registry.GetAsync(spaceId, cancellationToken).ConfigureAwait(false);
            if (registered is null) throw new KeyNotFoundException("Space was not found.");
            if (registered.Revision != expectedRevision)
                throw new SpaceRevisionConflictException(spaceId, expectedRevision, registered.Revision);
            await _model.MoveCustomSpaceAsync(scope, candidate.Id, null, cancellationToken).ConfigureAwait(false);
            await LoadAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            PublishStatus($"Could not move Space: {exception.Message}");
        }
    }

    public void SelectMoveParentIndex(int index)
    {
        var baseRevision = State.Revision;
        if (TryPublish(State with { SelectedMoveParentIndex = index }, baseRevision))
            _selectedMoveParentIndex = index;
    }

    public async Task ForkSpaceAsync(Guid spaceId, CancellationToken cancellationToken = default)
    {
        try
        {
            var registered = await _registry.GetAsync(spaceId, cancellationToken).ConfigureAwait(false)
                ?? throw new KeyNotFoundException("Space was not found.");
            var fork = await _model.ForkSpaceAsync(SpaceScope.ForCustom(spaceId), cancellationToken: cancellationToken).ConfigureAwait(false);
            await SelectSpaceAsync(fork.Scope.RegisteredSpaceId!.Value, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            PublishStatus($"Could not copy Space: {exception.Message}");
        }
    }

    public async Task ArchiveSpaceAsync(Guid spaceId, CancellationToken cancellationToken = default)
    {
        var item = _fullCustomSpaces.FirstOrDefault(space => space.Id == spaceId)
            ?? _fullBuiltIns.FirstOrDefault(space => space.Id == spaceId);
        if (item is null)
        {
            PublishStatus("Space was not found.");
            return;
        }
        if (item.IsBuiltIn)
        {
            PublishStatus("Built-in Spaces cannot be archived.");
            return;
        }

        var expectedRevision = item.Revision;
        try
        {
            var registered = await _registry.GetAsync(spaceId, cancellationToken).ConfigureAwait(false)
                ?? throw new KeyNotFoundException("Space was not found.");
            if (registered.Revision != expectedRevision)
                throw new SpaceRevisionConflictException(spaceId, expectedRevision, registered.Revision);
            await _model.ArchiveCustomSpaceAsync(SpaceScope.ForCustom(spaceId), cancellationToken).ConfigureAwait(false);
            await LoadCoreAsync(
                new LoadIntent(SelectedId: _selectedId == spaceId ? null : _selectedId),
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            PublishStatus($"Could not archive Space: {exception.Message}");
        }
    }

    /// <summary>Observes a separately retained queued action failure only while the exact
    /// admitted selection is still current. Publishing status does not settle or waive that task.</summary>
    public bool ReportQueuedActionFailure(SpacesHomeSelection expectedSelection, SpacesAction action, Exception failure)
    {
        ArgumentNullException.ThrowIfNull(expectedSelection);
        ArgumentNullException.ThrowIfNull(action);
        ArgumentNullException.ThrowIfNull(failure);
        if (!ReferenceEquals(State.Selection, expectedSelection) || expectedSelection.Id != action.Context.Scope.RegisteredSpaceId)
            return false;
        PublishStatus($"The queued Space action failed: {failure.Message}");
        return true;
    }

    public async Task EditDetailsAsync(Guid spaceId, CancellationToken cancellationToken = default)
    {
        var selection = State;
        var request = Interlocked.Read(ref _requestVersion);
        DemandCurrentSelection();

        var action = await _model.CreateEditDetailsActionAsync(
            SpaceScope.ForCustom(spaceId), cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        DemandCurrentSelection();
        await _actionHost!.ExecuteAsync(action, cancellationToken).ConfigureAwait(false);
        PublishStatus(_actionHost is ISpacesActionAdmissionHost { IsQueueAdmission: true }
            ? $"Opening details for {action.Context.Definition.Label}."
            : $"Opened details for {action.Context.Definition.Label}.");

        void DemandCurrentSelection()
        {
            if (!ReferenceEquals(State, selection) || Interlocked.Read(ref _requestVersion) != request ||
                selection.IsLoading || selection.Selection is not { CanEditDetails: true, IsBuiltIn: false } current ||
                current.Id != spaceId ||
                _actionHost is not ISpacesActionSupport support || !support.SupportsAction(SpacesActionKind.EditDetails))
                throw new InvalidOperationException("Editing details is unavailable for the current Space selection.");
        }
    }

    public async Task OpenSpaceAsync(Guid spaceId, CancellationToken cancellationToken = default)
    {
        if (_actionHost is null)
        {
            PublishStatus("Space launch is not available in this environment.");
            return;
        }
        try
        {
            var scope = ResolveScope(spaceId);
            var action = await _model.CreateOpenActionAsync(scope, cancellationToken).ConfigureAwait(false);
            await _actionHost.ExecuteAsync(action, cancellationToken).ConfigureAwait(false);
            var isQueuedAdmission = _actionHost is ISpacesActionAdmissionHost { IsQueueAdmission: true };
            PublishStatus(isQueuedAdmission
                ? $"Opening {action.Context.Definition.Label}…"
                : $"Opened {action.Context.Definition.Label}.");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            PublishStatus($"Could not open Space: {exception.Message}");
        }
    }

    public async Task AddContextAsync(Guid spaceId, string? path, CancellationToken cancellationToken = default)
    {
        var item = _fullCustomSpaces.FirstOrDefault(space => space.Id == spaceId)
            ?? _fullBuiltIns.FirstOrDefault(space => space.Id == spaceId);
        if (item is null)
        {
            PublishStatus("Space was not found.");
            return;
        }

        if (string.IsNullOrWhiteSpace(path))
        {
            if (_pickFileAsync is null)
            {
                PublishStatus("File picker is not available.");
                return;
            }
            path = await _pickFileAsync("Add a source to this Space", cancellationToken).ConfigureAwait(false);
        }
        if (string.IsNullOrWhiteSpace(path)) return;

        var expectedRevision = item.Revision;
        try
        {
            var registered = await _registry.GetAsync(spaceId, cancellationToken).ConfigureAwait(false)
                ?? throw new KeyNotFoundException("Space was not found.");
            if (registered.Revision != expectedRevision)
                throw new SpaceRevisionConflictException(spaceId, expectedRevision, registered.Revision);
            await _registry.AddFileAsync(spaceId, path, SpaceFilePermission.ReadOnly, cancellationToken).ConfigureAwait(false);
            await LoadAsync(cancellationToken).ConfigureAwait(false);
            PublishStatus($"Added source {System.IO.Path.GetFileName(path)}.");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            PublishStatus($"Could not add source: {exception.Message}");
        }
    }

    public async Task EditExtensionsAsync(Guid spaceId, CancellationToken cancellationToken = default)
    {
        if (_extensionEditorHost is null)
        {
            PublishStatus("Extension editor is not available.");
            return;
        }
        try
        {
            await _extensionEditorHost.OpenEventsAndExtensionsAsync(spaceId, cancellationToken).ConfigureAwait(false);
            PublishStatus("Opened Events & Extensions editor.");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            PublishStatus($"Could not open extension editor: {exception.Message}");
        }
    }

    public async Task EditGraphAsync(Guid spaceId, CancellationToken cancellationToken = default)
    {
        if (_extensionEditorHost is null)
        {
            PublishStatus("Space graph editor is not available.");
            return;
        }
        try
        {
            await _extensionEditorHost.OpenSpaceGraphEditorAsync(spaceId, cancellationToken).ConfigureAwait(false);
            PublishStatus("Opened Space graph editor.");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            PublishStatus($"Could not open Space graph editor: {exception.Message}");
        }
    }

    private SpaceScope ResolveScope(Guid spaceId)
    {
        var builtIn = SpacesModel.BuiltIns.FirstOrDefault(def => def.Scope.RegisteredSpaceId == spaceId);
        if (builtIn is not null) return builtIn.Scope;
        return SpaceScope.ForCustom(spaceId);
    }

    private static SpacesHomeItem ToHomeItem(SpacesSpaceDefinition def, IReadOnlyList<SpaceDefinition> registered)
    {
        var registeredSpace = registered.FirstOrDefault(space => space.Id == def.Scope.RegisteredSpaceId);
        return new SpacesHomeItem(
            def.Scope.RegisteredSpaceId ?? Guid.Empty,
            def.Label,
            def.Description,
            def.Scope.Key,
            def.IsBuiltIn,
            registeredSpace?.ParentSpaceId ?? def.ParentSpaceId,
            registeredSpace?.Position ?? def.Position,
            registeredSpace?.Revision ?? def.Revision,
            !def.IsBuiltIn) { IconKey = registeredSpace?.IconKey ?? def.IconKey };
    }

    private static SpacesHomeItem ToHomeItem(SpaceDefinition space) =>
        new(
            space.Id,
            space.Name,
            space.Description,
            SpaceScope.ForCustom(space.Id).Key,
            space.IsBuiltIn,
            space.ParentSpaceId,
            space.Position,
            space.Revision,
            !space.IsBuiltIn) { IconKey = space.IconKey };

    private static IReadOnlyList<SpacesHomeMoveCandidate> BuildMoveParentOptions(
        IReadOnlyList<SpaceDefinition> registered,
        Guid? selectedId)
    {
        if (selectedId is null) return [new SpacesHomeMoveCandidate(null, "(no parent)")];

        var excluded = new HashSet<Guid> { selectedId.Value };

        bool changed;
        do
        {
            changed = false;
            foreach (var space in registered)
            {
                if (space.ParentSpaceId is { } parentId && excluded.Contains(parentId) && excluded.Add(space.Id))
                    changed = true;
            }
        } while (changed);

        var options = new List<SpacesHomeMoveCandidate> { new(null, "(no parent)") };
        options.AddRange(registered
            .Where(space => !excluded.Contains(space.Id))
            .OrderBy(space => space.IsBuiltIn ? 0 : 1)
            .ThenBy(space => space.Name, StringComparer.OrdinalIgnoreCase)
            .Select(space => new SpacesHomeMoveCandidate(space.Id, space.Name)));
        return options;
    }

    private static int ResolveMoveParentIndex(IReadOnlyList<SpacesHomeMoveCandidate> options, Guid? parentId)
    {
        if (parentId is null) return 0;
        for (var index = 0; index < options.Count; index++)
        {
            if (options[index].Id == parentId) return index;
        }
        return 0;
    }

    private SpacesHomeSelection? BuildSelection(IReadOnlyList<SpaceDefinition> registered, Guid? selectedId, Guid? pendingRenameSpaceId = null)
    {
        if (selectedId is not { } id) return null;
        var space = registered.FirstOrDefault(s => s.Id == id);
        if (space is null) return null;

        var effectiveModel = space.ModelPolicy?.DefaultModelId ?? space.ModelName ?? "Default";
        var webAccess = space.ContextPolicy?.WebAccess ?? SpaceWebAccessPolicy.Ask;
        var memoryScope = space.MemoryPolicy?.DefaultScope ?? SpaceMemoryScope.Conversation;

        var contextReferences = (space.ContextReferences ?? [])
            .Select(reference => new SpacesHomeContextReference(
                reference.ContextId,
                reference.CanonicalEntityId,
                reference.Permission.ToString(),
                reference.IndexState.ToString()))
            .ToArray();

        var capabilities = (space.CapabilityBindings ?? [])
            .Select(binding => new SpacesHomeCapability(binding.CapabilityId, $"{binding.Kind}:{binding.CapabilityId}"))
            .ToArray();

        var hasEditor = _extensionEditorHost is not null;
        return new SpacesHomeSelection(
            space.Id,
            space.Name,
            space.Description,
            space.IsBuiltIn,
            !space.IsBuiltIn,
            selectedId == (pendingRenameSpaceId ?? _pendingRenameSpaceId),
            space.ParentSpaceId,
            effectiveModel,
            webAccess.ToString(),
            memoryScope.ToString(),
            contextReferences,
            capabilities,
            hasEditor,
            space.IsBuiltIn && hasEditor,
            !space.IsBuiltIn && hasEditor)
        {
            CanEditDetails = !space.IsBuiltIn && space.Origin == SpaceOrigin.UserCreated && !space.IsArchived &&
                _actionHost is ISpacesActionSupport support && support.SupportsAction(SpacesActionKind.EditDetails)
        };
    }

    private static IReadOnlyList<SpacesHomeItem> Filter(IReadOnlyList<SpacesHomeItem> source, string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return source;
        return source
            .Where(item =>
                item.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                item.Summary.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                item.ScopeKey.Contains(query, StringComparison.OrdinalIgnoreCase))
            .ToArray();
    }

    /// <summary>Reports a Space workspace action that the owning native surface refused before it
    /// could change anything, so the refusal is visible in the sidebar that raised it. This changes
    /// only the status line: it never alters the selected Space or any conversation.</summary>
    public void ReportWorkspaceActionRefusal(string message)
    {
        if (string.IsNullOrWhiteSpace(message)) return;
        PublishStatus(message);
    }

    private void PublishStatus(string message)
    {
        Publish(State with { StatusMessage = message });
    }

    private void Publish(SpacesHomeViewState state)
    {
        var baseRevision = State.Revision;
        TryPublish(state, baseRevision);
    }

    /// <summary>
    /// Publishes <paramref name="state"/> only if the current published revision still matches
    /// <paramref name="expectedBaseRevision"/>. This prevents stale async results from overwriting
    /// a newer selection, draft or query made while the async work was in flight.
    /// </summary>
    private bool TryPublish(SpacesHomeViewState state, int expectedBaseRevision)
    {
        if (State.Revision != expectedBaseRevision) return false;
        State = state.Revision > expectedBaseRevision ? state : state with { Revision = expectedBaseRevision + 1 };
        StateChanged?.Invoke(State);
        return true;
    }
}
