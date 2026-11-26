using System.ComponentModel;
using System.Text.Json;
using CakeOS.Cui;
using CakeOS.Cui.Language;
using Dulche.Runtime;
using Haven.Application;
using Haven.Application.Haven;
using HavenOS.Home.Core;

namespace HavenOS.Home;

public enum HomeModelPickerAction
{
    Refresh,
    RefreshCatalogue,
    AddCatalogueCandidate,
    SelectCategory,
    SelectRoute,
    SetCandidateEnabled,
    MoveCandidateUp,
    MoveCandidateDown,
    SaveRoute,
    FinishAudit,
    PreviewResolution,
    SetReasoningLevel,
    CommitReasoningLevel,
    ToggleFamily,
    DiscardRoute,
    SelectCompactRole,
    ToggleCompactSection,
    ChooseCompactModel,
}

public sealed record HomeModelPickerActionRequest(
    HomeModelPickerAction Action,
    string? Scope = null,
    string? Category = null,
    string? RouteId = null,
    string? ProviderId = null,
    string? ModelId = null,
    string? ArtifactRevision = null,
    bool? Enabled = null,
    string? Capability = null,
    string? AppId = null,
    string? AgentId = null,
    string? ReasoningLevelId = null,
    JsonElement? Context = null,
    string? FamilyKey = null);

/// <summary>Owns the authored Home model-picker CUI document and the typed action queue for its host.</summary>
public sealed class HomeModelPickerCuiSurface : ICuiBindingContext, INotifyPropertyChanged
{
    private readonly Queue<HomeModelPickerActionRequest> _actions = new();
    private readonly HashSet<string> _expandedFamilies = new(StringComparer.Ordinal);
    private HomeModelFamilyReleaseContext _familyRelease = new();
    private bool _catalogueObserved;
    private bool _catalogueReadFailed;
    private HavenModelProjectionSnapshot? _compactModel;
    private HomeModelRouteContract? _mainRoute;
    private HomeModelRouteContract? _voiceRoute;
    private readonly HashSet<(string Role, string Section)> _compactSections = new();
    private string? _compactStatus;

    internal const string MainRole = "chat";
    internal const string VoiceRole = "voice";
    internal const string VisionRole = "vision";
    public string CompactRole { get; private set; } = MainRole;
    internal bool CanEditCompactRoute => MatchesRole(Category) &&
        State is { DraftRoute: not null, IsBusy: false, RouteRefreshRequired: false };
    private bool MatchesRole(string category) => CompactRole switch
    {
        MainRole => IsMainCategory(category),
        VoiceRole => string.Equals(category, VoiceRole, StringComparison.OrdinalIgnoreCase),
        _ => false,
    };

    public HomeModelPickerCuiSurface(CuiDocument document)
    {
        Document = document ?? throw new ArgumentNullException(nameof(document));
    }

    public CuiDocument Document { get; }
    public HomeModelPickerEditorState State { get; private set; } = HomeModelPickerEditorState.Initial;
    public string Scope { get; private set; } = "global";
    public string Category { get; private set; } = "chat";
    public string? ScopeId { get; private set; }
    public IReadOnlyList<string> EffectiveAdvertisedReasoningIds { get; private set; } = [];
    public string? EffectiveProviderId { get; private set; }
    public string? EffectiveModelId { get; private set; }
    public string? EffectiveArtifactRevision { get; private set; }
    public string? EffectiveSelectedReasoningId { get; private set; }
    public IReadOnlyList<HomeModelPickerCatalogueEntry> Catalogue { get; private set; } = [];
    public string CatalogueStatus { get; private set; } = "Refresh the model list to add a model to your route.";
    public string? PendingApprovalRequestId { get; set; }
    public string? PendingAuditRequestId { get; set; }
    public bool IsSettingsOpen { get; private set; }
    public HomeModelPickerCuiProjection Projection { get; private set; } =
        new(HomeModelPickerEditorState.Initial, "global", "chat");
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>True once a real route snapshot has been applied to this surface.</summary>
    public bool HasSnapshot => Projection.HasSnapshot;

    public bool TryGetValue(string path, out object? value)
    {
        switch (path)
        {
            case "IsSettingsOpen": value = IsSettingsOpen; return true;
            case "IsPickerOpen": value = !IsSettingsOpen; return true;
            case "CanDiscardRoute": value = CanDiscardRoute(State); return true;
            case "RouteRefreshRequired": value = State.RouteRefreshRequired; return true;
            case "CanCommitReasoningLevel":
                value = State is { HasUnsavedChanges: false, RouteRefreshRequired: false, PendingAuditRequestId: null };
                return true;
            case "CompactMainAccent": value = CompactRole == MainRole ? "Secondary" : "None"; return true;
            case "CompactVoiceAccent": value = CompactRole == VoiceRole ? "Secondary" : "None"; return true;
            case "CompactVisionAccent": value = CompactRole == VisionRole ? "Secondary" : "None"; return true;
            case "CompactContextOpen": value = IsSectionOpen("context"); return true;
            case "CompactModelOpen": value = IsSectionOpen("model"); return true;
            case "CompactParticipantOpen": value = IsSectionOpen("participant"); return true;
            case "CompactContextChevron": value = SectionChevron("context"); return true;
            case "CompactModelChevron": value = SectionChevron("model"); return true;
            case "CompactParticipantChevron": value = SectionChevron("participant"); return true;
            case "CompactContextDisclosureName": value = SectionName("context", "Context"); return true;
            case "CompactModelDisclosureName": value = SectionName("model", "Model"); return true;
            case "CompactParticipantDisclosureName": value = SectionName("participant", "Assistant or Specialist"); return true;
            case "CompactModelName": value = CompactCatalogueModel?.DisplayName ?? CompactIdentity?.ModelId ?? "Choose a model"; return true;
            case "CompactModelSource":
                value = CompactCatalogueModel is { IsLocal: true } || IsCompactModelResolved && _compactModel is { LocalityKnown: true, IsLocal: true }
                    ? "This device" : CompactCatalogueModel?.ProviderName ?? CompactIdentity?.ProviderId;
                return true;
            case "HasCompactModelSource": value = CompactIdentity is not null; return true;
            case "CompactModelState":
                value = CompactRouteIsDraft ? "Route preference pending"
                    : IsCompactModelResolved ? "Current route choice"
                    : CompactIdentity is null ? ""
                    : CompactCatalogueModel is null && _catalogueObserved ? "Saved preference unavailable in the catalogue"
                    : "Saved route preference";
                return true;
            case "CompactReasoning":
                value = CompactReasoningChoices.Count > 0 && _compactModel is { } model
                    ? HomeModelPickerReasoningLabels.LabelForLevel(model.AdvertisedReasoningIds, model.SelectedReasoningId)
                        ?? model.SelectedReasoningId ?? "Provider default"
                    : "Unavailable";
                return true;
            case "CompactReasoningChoices": value = CompactReasoningChoices; return true;
            case "HasCompactReasoningChoices": value = CompactReasoningChoices.Count > 0; return true;
            case "CompactContextCapacity":
                value = CompactCatalogueModel?.ContextWindow is { } capacity && capacity > 0 ? $"{capacity:N0} tokens" : "Capacity unknown";
                return true;
            case "CompactContextDetails":
                value = CompactCatalogueModel?.ContextWindow is { } reported && reported > 0
                    ? $"Reported model context capacity: {reported:N0} tokens."
                    : "The selected model reports no context capacity.";
                return true;
            case "CompactCatalogue":
                value = Catalogue.Where(entry => MatchesCompactCapability(entry, CompactRole))
                    .Select(entry => new HomeModelPickerCatalogueRow(entry, CompactRole)).ToArray();
                return true;
            case "HasCompactCatalogueMessage": value = CompactCatalogueMessage.Length > 0; return true;
            case "CompactCatalogueMessage": value = CompactCatalogueMessage; return true;
            case "HasCompactRouteMessage": value = CompactRouteMessage.Length > 0; return true;
            case "CompactRouteMessage": value = CompactRouteMessage; return true;
            case "CanChooseCompactModel": value = CanEditCompactRoute; return true;
            case "CanSaveCompactRoute":
                value = CanEditCompactRoute && State.HasUnsavedChanges && State.PendingAuditRequestId is null;
                return true;
            case "HasCompactPendingApproval":
                value = State.PendingApprovalRequestId is not null || PendingApprovalRequestId is not null;
                return true;
            case "HasCompactPendingAudit":
                value = State.PendingAuditRequestId is not null || PendingAuditRequestId is not null;
                return true;
            case "HasCompactStatus": value = !string.IsNullOrWhiteSpace(_compactStatus); return true;
            case "CompactStatus": value = _compactStatus; return true;
            default: return Projection.TryGetValue(path, out value);
        }
    }

    private string CompactCatalogueMessage => _catalogueReadFailed ? CatalogueStatus : !_catalogueObserved
        ? "Refresh to discover models."
        : Catalogue.Any(entry => MatchesCompactCapability(entry, CompactRole)) ? ""
        : CompactRole == VoiceRole ? "No catalogue models advertise conversational voice."
        : "No models are available for this role.";

    private string CompactRouteMessage => CompactRole == VisionRole ? "A separate Vision route is unavailable."
        : CanEditCompactRoute || State.RouteRefreshRequired ? "" : "Select this role again to open its route.";

    private bool CompactRouteIsDraft => MatchesRole(Category) && State.HasUnsavedChanges;
    private HomeModelRouteContract? CompactRoute => CompactRouteIsDraft ? State.DraftRoute : CompactRole switch
    {
        MainRole => _mainRoute,
        VoiceRole => _voiceRoute,
        _ => null,
    };

    private HomeModelRouteCandidate? CompactIdentity => CompactRoute?.Candidates.Where(candidate => candidate.Enabled)
        .OrderBy(candidate => candidate.Order).FirstOrDefault()
        ?? (CompactRole == MainRole && !CompactRouteIsDraft && _compactModel is { ProviderId: { } provider, ModelId: { } model }
            ? new(provider, model, null, true, 0) : null);

    private bool IsCompactModelResolved => CompactRole == MainRole && CompactIdentity is { } candidate &&
        _compactModel is { } model && string.Equals(candidate.ProviderId, model.ProviderId, StringComparison.OrdinalIgnoreCase)
        && string.Equals(candidate.ModelId, model.ModelId, StringComparison.OrdinalIgnoreCase);

    private HomeModelPickerCatalogueEntry? CompactCatalogueModel => CompactIdentity is { } identity
        ? Catalogue.FirstOrDefault(entry => StringComparer.Ordinal.Equals(entry.ProviderId, identity.ProviderId) &&
            StringComparer.Ordinal.Equals(entry.ModelId, identity.ModelId) &&
            StringComparer.Ordinal.Equals(entry.ArtifactRevision, identity.ArtifactRevision))
        : null;

    private IReadOnlyList<HomeModelPickerReasoningChoice> CompactReasoningChoices =>
        IsCompactModelResolved && MatchesRole(Category) && !CompactRouteIsDraft &&
        CompactIdentity is { } candidate && _compactModel is { } model
            ? new HomeModelPickerCandidateRow(candidate, model.AdvertisedReasoningIds, model.ProviderId, model.ModelId,
                candidate.ArtifactRevision, model.SelectedReasoningId).ReasoningLevelChoices
            : [];

    internal static bool MatchesCompactCapability(HomeModelPickerCatalogueEntry entry, string role)
    {
        var required = role switch
        {
            MainRole => nameof(Haven.Core.ToolCapability.Text),
            // Audio input or output alone does not establish conversational voice support.
            VoiceRole => "Voice",
            VisionRole => nameof(Haven.Core.ToolCapability.Vision),
            _ => null,
        };
        return required is not null && entry.Capabilities?.Any(value =>
            string.Equals(value, required, StringComparison.OrdinalIgnoreCase)) == true;
    }

    internal void SetCompactRole(string role)
    {
        CompactRole = role;
        NotifyChanged();
    }

    private bool IsSectionOpen(string section) => _compactSections.Contains((CompactRole, section));
    private string SectionChevron(string section) => IsSectionOpen(section) ? "⌃" : "⌄";
    private string SectionName(string section, string label) => $"{(IsSectionOpen(section) ? "Collapse" : "Expand")} {label}";

    internal bool ToggleCompactSection(string? section)
    {
        if (section is not ("model" or "context" or "participant")) return false;
        var key = (CompactRole, section);
        if (!_compactSections.Add(key)) _compactSections.Remove(key);
        NotifyChanged();
        return true;
    }

    internal void SetCompactStatus(string? status)
    {
        if (string.Equals(_compactStatus, status, StringComparison.Ordinal)) return;
        _compactStatus = status;
        NotifyChanged();
    }

    private void NotifyChanged() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));

    internal static bool CanDiscardRoute(HomeModelPickerEditorState state) => state is
        { HasUnsavedChanges: true, IsBusy: false, PendingApprovalRequestId: null, PendingAuditRequestId: null };

    public void SetSettingsOpen(bool settingsOpen)
    {
        if (IsSettingsOpen == settingsOpen) return;
        IsSettingsOpen = settingsOpen;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
    }

    public void ApplyCompactModel(HavenModelProjectionSnapshot? model)
    {
        _compactModel = model;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
    }

    public static HomeModelPickerCuiSurface LoadDefault()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "UI", "ModelPicker.cui");
        return new HomeModelPickerCuiSurface(new CuiRichParser().ParseFile(path));
    }

    public void ApplyState(HomeModelPickerEditorState state)
    {
        State = state ?? throw new ArgumentNullException(nameof(state));
        if (state.Snapshot is { } snapshot)
        {
            Scope = snapshot.Scope;
            Category = snapshot.Category;
            ScopeId = snapshot.Routes.FirstOrDefault()?.ScopeId;
            var committed = snapshot.Routes.FirstOrDefault(route => route.RouteId == state.SelectedRouteId);
            if (IsMainCategory(Category)) _mainRoute = committed;
            if (string.Equals(Category, VoiceRole, StringComparison.OrdinalIgnoreCase)) _voiceRoute = committed;
        }
        RebuildProjection();
    }

    internal static bool IsMainCategory(string category) => string.Equals(category, "chat", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(category, "active", StringComparison.OrdinalIgnoreCase);

    /// <summary>Applies the effective resolved candidate's real advertised reasoning choices and selected
    /// label (from the shared Home-owned source) for the visible personal route.</summary>
    public void ApplyEffectiveReasoning(
        IReadOnlyList<string>? advertisedReasoningIds,
        string? providerId,
        string? modelId,
        string? artifactRevision,
        string? selectedReasoningId)
    {
        EffectiveAdvertisedReasoningIds = advertisedReasoningIds ?? [];
        EffectiveProviderId = providerId;
        EffectiveModelId = modelId;
        EffectiveArtifactRevision = artifactRevision;
        EffectiveSelectedReasoningId = selectedReasoningId;
        RebuildProjection();
    }

    public void ApplyCatalogue(HomeModelCataloguePage? page, string status)
    {
        Catalogue = page?.Items ?? [];
        _catalogueObserved = page is not null;
        _catalogueReadFailed = page is null;
        if (page is not null) _familyRelease = page.FamilyRelease ?? new();
        CatalogueStatus = status;
        RebuildProjection();
    }

    private void RebuildProjection()
    {
        Projection = new HomeModelPickerCuiProjection(State, Scope, Category,
            EffectiveAdvertisedReasoningIds, EffectiveProviderId, EffectiveModelId, EffectiveArtifactRevision,
            EffectiveSelectedReasoningId, PendingApprovalRequestId, Catalogue, CatalogueStatus, _familyRelease, _expandedFamilies, _catalogueObserved, _catalogueReadFailed);
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
    }

    public bool Request(HomeModelPickerActionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Action == HomeModelPickerAction.ToggleFamily && !Projection.Families.Any(group => group.Identity == request.FamilyKey))
            return false;
        if ((request.Action is HomeModelPickerAction.SelectCategory or HomeModelPickerAction.SelectCompactRole) &&
            string.IsNullOrWhiteSpace(request.Category))
            return false;
        if (request.Action == HomeModelPickerAction.SelectRoute && string.IsNullOrWhiteSpace(request.RouteId))
            return false;
        if ((request.Action is HomeModelPickerAction.SetCandidateEnabled or HomeModelPickerAction.MoveCandidateUp or HomeModelPickerAction.MoveCandidateDown or HomeModelPickerAction.SetReasoningLevel or HomeModelPickerAction.AddCatalogueCandidate or HomeModelPickerAction.ChooseCompactModel)
            && (string.IsNullOrWhiteSpace(request.ProviderId) || string.IsNullOrWhiteSpace(request.ModelId) || (request.ArtifactRevision is not null && string.IsNullOrWhiteSpace(request.ArtifactRevision))))
            return false;
        if (request.Action == HomeModelPickerAction.SetCandidateEnabled && request.Enabled is null)
            return false;
        if (request.Action == HomeModelPickerAction.SetReasoningLevel && request.ReasoningLevelId is not null && string.IsNullOrWhiteSpace(request.ReasoningLevelId))
            return false;

        _actions.Enqueue(request);
        return true;
    }

    public bool ToggleFamily(string familyKey)
    {
        if (!Projection.Families.Any(group => group.Identity == familyKey)) return false;
        if (!_expandedFamilies.Add(familyKey)) _expandedFamilies.Remove(familyKey);
        RebuildProjection();
        return true;
    }

    public bool TryDequeueAction(out HomeModelPickerActionRequest request)
    {
        if (_actions.TryDequeue(out var action))
        {
            request = action;
            return true;
        }
        request = null!;
        return false;
    }
}

/// <summary>Routes CUI picker actions through Home's shared model-provider contract.</summary>
public sealed class HomeModelPickerCuiController(HomeModelPickerRouteEditor editor, HomeModelPickerCuiSurface? surface = null,
    HomeModelSelectionSource? source = null, AuthenticatedResourceActor? actor = null)
{
    private readonly HomeModelPickerRouteEditor _editor = editor ?? throw new ArgumentNullException(nameof(editor));
    private readonly HomeModelSelectionSource? _source = source;
    private readonly AuthenticatedResourceActor? _actor = actor;
    private readonly SemaphoreSlim _actionGate = new(1, 1);
    private long _generation;

    public HomeModelPickerCuiSurface Surface { get; } = surface ?? HomeModelPickerCuiSurface.LoadDefault();

    /// <summary>Optional currentness/publication seam. When set, it is revalidated immediately
    /// before surface state is applied so a retired panel or an actor switch can never publish a
    /// stale result. Null (standalone callers) publishes unconditionally.</summary>
    public Func<bool>? PublicationGate { get; set; }

    private bool CanPublish() => PublicationGate is null || PublicationGate();

    public async Task<HomeCoreOperationResult<HomeModelPickerSnapshot>> OpenAsync(
        string scope = "global",
        string category = "chat",
        CancellationToken cancellationToken = default)
    {
        var result = await _editor.RefreshAsync(scope, category, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (CanPublish())
        {
            Surface.ApplyState(_editor.Current);
            await ApplyEffectiveReasoningAsync(cancellationToken).ConfigureAwait(false);
            if (result.Succeeded && CanPublish()) await LoadCatalogueAsync(cancellationToken).ConfigureAwait(false);
        }
        return result;
    }

    public async Task<HomeCoreOperationResult<object>> ExecuteAsync(
        HomeModelPickerActionRequest action,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);
        await _actionGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!CanPublish()) return Failure<object>("PickerUnavailable", "The model picker is no longer current.");
            if (_editor.Current.RouteRefreshRequired && ChangesRoute(action.Action))
                return Failure<object>("RouteRefreshRequired", "Refresh the saved route before editing.");
            return await ExecuteCurrentAsync(action, cancellationToken).ConfigureAwait(false);
        }
        finally { _actionGate.Release(); }
    }

    private static bool ChangesRoute(HomeModelPickerAction action) => action is
        HomeModelPickerAction.ChooseCompactModel or HomeModelPickerAction.AddCatalogueCandidate or
        HomeModelPickerAction.SetCandidateEnabled or HomeModelPickerAction.SetReasoningLevel or
        HomeModelPickerAction.MoveCandidateUp or HomeModelPickerAction.MoveCandidateDown or
        HomeModelPickerAction.CommitReasoningLevel or HomeModelPickerAction.SaveRoute;

    private async Task<HomeCoreOperationResult<object>> ExecuteCurrentAsync(
        HomeModelPickerActionRequest action, CancellationToken cancellationToken)
    {
        HomeCoreOperationResult<object> result;
        try
        {
            if (action.Action == HomeModelPickerAction.CommitReasoningLevel)
            {
                result = await CommitReasoningLevelAsync(action, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                result = action.Action switch
                {
                    HomeModelPickerAction.RefreshCatalogue => Box(await LoadCatalogueAsync(cancellationToken).ConfigureAwait(false)),
                    HomeModelPickerAction.SelectCompactRole => await SelectCompactRoleAsync(action.Category, cancellationToken).ConfigureAwait(false),
                    HomeModelPickerAction.ToggleCompactSection => CanPublish() && Surface.ToggleCompactSection(action.FamilyKey)
                        ? new(true, "SectionToggled", "Picker section updated.")
                        : Failure<object>("SectionUnavailable", "Choose a compact picker section."),
                    HomeModelPickerAction.AddCatalogueCandidate => Box(AddCatalogueCandidate(action)),
                    HomeModelPickerAction.ChooseCompactModel => Box(ChooseCompactModel(action)),
                    HomeModelPickerAction.ToggleFamily => CanPublish() && Surface.ToggleFamily(action.FamilyKey ?? "")
                        ? new(true, "FamilyToggled", "Catalogue family expansion updated.")
                        : Failure<object>("FamilyNotFound", "Choose a current catalogue family."),
                    HomeModelPickerAction.Refresh => Box(await _editor.RefreshAsync(
                        action.Scope ?? Surface.Scope,
                        action.Category ?? Surface.Category,
                        cancellationToken: cancellationToken).ConfigureAwait(false)),
                    HomeModelPickerAction.SelectCategory => Box(await _editor.RefreshAsync(
                        action.Scope ?? Surface.Scope,
                        Required(action.Category, "capability category"),
                        cancellationToken: cancellationToken).ConfigureAwait(false)),
                    HomeModelPickerAction.SelectRoute => Box(_editor.SelectRoute(Required(action.RouteId, "route ID"))),
                    HomeModelPickerAction.SetCandidateEnabled => Box(_editor.SetCandidateEnabled(
                        Required(action.ProviderId, "provider ID"), Required(action.ModelId, "model ID"),
                        action.ArtifactRevision, action.Enabled
                        ?? throw new ArgumentException("Candidate enabled state is required.", nameof(action)))),
                    HomeModelPickerAction.SetReasoningLevel => Box(_editor.SetCandidateReasoningLevel(
                        Required(action.ProviderId, "provider ID"), Required(action.ModelId, "model ID"),
                        action.ArtifactRevision, action.ReasoningLevelId)),
                    HomeModelPickerAction.MoveCandidateUp => Box(Move(action, -1)),
                    HomeModelPickerAction.MoveCandidateDown => Box(Move(action, 1)),
                    HomeModelPickerAction.FinishAudit => await FinishAuditAsync(cancellationToken).ConfigureAwait(false),
                    HomeModelPickerAction.SaveRoute => Box(await _editor.SaveAsync(cancellationToken).ConfigureAwait(false)),
                    HomeModelPickerAction.DiscardRoute => HomeModelPickerCuiSurface.CanDiscardRoute(_editor.Current)
                        ? Box(await _editor.RefreshAsync(Surface.Scope, Surface.Category, discardUnsavedChanges: true,
                            cancellationToken: cancellationToken).ConfigureAwait(false))
                        : Failure<object>("DiscardUnavailable", "There are no editable route changes to discard."),
                    HomeModelPickerAction.PreviewResolution => Box(await _editor.PreviewAsync(
                        Required(action.Capability, "required capability"), action.AppId, action.AgentId, action.Context,
                        cancellationToken).ConfigureAwait(false)),
                    _ => Failure<object>("UnsupportedAction", "This model-picker action is not supported."),
                };
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (ArgumentException exception)
        {
            result = Failure<object>("InvalidArgument", exception.Message);
        }
        if (CanPublish() && action.Action != HomeModelPickerAction.ToggleCompactSection)
        {
            Surface.ApplyState(_editor.Current);
            if (!Surface.IsSettingsOpen)
                Surface.SetCompactStatus(result.Succeeded || action.Action == HomeModelPickerAction.RefreshCatalogue ? null : result.Message);
            if (action.Action != HomeModelPickerAction.ChooseCompactModel)
                await ApplyEffectiveReasoningAsync(cancellationToken).ConfigureAwait(false);
        }
        return result;
    }

    private async Task<HomeCoreOperationResult<object>> SelectCompactRoleAsync(string? role, CancellationToken cancellationToken)
    {
        if (role is not (HomeModelPickerCuiSurface.MainRole or HomeModelPickerCuiSurface.VoiceRole or HomeModelPickerCuiSurface.VisionRole))
            return Failure<object>("RoleUnavailable", "Choose Main, Voice or Vision.");
        if (Surface.IsSettingsOpen || !CanPublish())
            return Failure<object>("PickerUnavailable", "The compact model picker is unavailable.");
        if (_editor.Current.HasUnsavedChanges && (Surface.CompactRole != role || !Surface.CanEditCompactRoute))
            return Failure<object>("UnsavedChanges", "Save or discard the current route changes before switching roles.");

        var result = role == HomeModelPickerCuiSurface.VisionRole || Surface.CompactRole == role && Surface.CanEditCompactRoute
            ? new HomeCoreOperationResult<object>(true, "RoleSelected", "Picker role updated.")
            : Box(await _editor.RefreshAsync(Surface.Scope, role, cancellationToken: cancellationToken).ConfigureAwait(false));
        if (result.Succeeded && CanPublish()) Surface.SetCompactRole(role);
        return result;
    }

    private async Task<HomeCoreOperationResult<HomeModelCataloguePage>> LoadCatalogueAsync(CancellationToken cancellationToken)
    {
        var result = await _editor.GetCatalogueAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        if (CanPublish()) Surface.ApplyCatalogue(result.Value, result.Message);
        return result;
    }

    private HomeCoreOperationResult<HomeModelRouteContract> AddCatalogueCandidate(HomeModelPickerActionRequest action)
    {
        var entry = FindCatalogueEntry(action);
        if (entry is null)
            return Failure<HomeModelRouteContract>("CatalogueEntryNotLoaded", "Refresh the authorised catalogue and choose one of its current entries.");
        return _editor.AddCandidate(entry);
    }

    private HomeCoreOperationResult<HomeModelRouteContract> ChooseCompactModel(HomeModelPickerActionRequest action)
    {
        if (!CanPublish() || Surface.IsSettingsOpen || !Surface.CanEditCompactRoute ||
            !string.Equals(action.Category, Surface.CompactRole, StringComparison.Ordinal))
            return Failure<HomeModelRouteContract>("RouteUnavailable", "Open the corresponding route before choosing a model.");

        var entry = FindCatalogueEntry(action);
        if (entry is null || !HomeModelPickerCuiSurface.MatchesCompactCapability(entry, Surface.CompactRole))
            return Failure<HomeModelRouteContract>("CatalogueEntryUnavailable", "Choose a current catalogue model for this role.");
        return _editor.PreferCandidate(entry);
    }

    private HomeModelPickerCatalogueEntry? FindCatalogueEntry(HomeModelPickerActionRequest action) =>
        Surface.Catalogue.FirstOrDefault(item =>
            string.Equals(item.ProviderId, action.ProviderId, StringComparison.Ordinal) &&
            string.Equals(item.ModelId, action.ModelId, StringComparison.Ordinal) &&
            string.Equals(item.ArtifactRevision, action.ArtifactRevision, StringComparison.Ordinal));

    /// <summary>The canonical picker commit path: persists the chosen advertised reasoning-level default
    /// through the shared Home-owned source (guarded route CAS when wired; otherwise the retained memory
    /// API). Refusals surface as a typed non-success with no state mutation.</summary>
    private async Task<HomeCoreOperationResult<object>> CommitReasoningLevelAsync(
        HomeModelPickerActionRequest action, CancellationToken cancellationToken)
    {
        if (_source is null || _actor is null)
            return Failure<object>("SourceUnavailable", "No selected-reasoning source/actor is available to commit a level.");
        var state = _editor.Current;
        if (state.HasUnsavedChanges)
            return Failure<object>("UnsavedChanges", "Save or discard the current route changes before saving a reasoning level.");
        if (state.PendingAuditRequestId is not null)
            return Failure<object>("AuditPending", "Finish the original route audit before saving a reasoning level.");
        if (state.Snapshot is not { } routeSnapshot || !HomeModelPickerCuiSurface.IsMainCategory(routeSnapshot.Category))
            return Failure<object>("RouteUnavailable", "Open the Main route before saving a reasoning level.");

        var providerId = Required(action.ProviderId, "provider ID");
        var modelId = Required(action.ModelId, "model ID");
        var surfaceScope = $"{Surface.Scope}:{Surface.ScopeId}";
        var stableKey = new ModelIdentity(providerId, modelId).StableKey;
        var result = await _source.CommitSelectionAsync(
            _actor, modelId, stableKey, surfaceScope, Required(action.ReasoningLevelId, "reasoning level ID"), cancellationToken).ConfigureAwait(false);
        if (CanPublish())
        {
            // Reflect the retained review (or its clearance) on the surface, gated by currentness. A
            // success clears the pending line; a genuine approval-pending refusal surfaces it so the
            // authored picker shows "Change pending review in Home".
            Surface.PendingApprovalRequestId = result.Succeeded
                ? null
                : _source.GetPendingApprovalRequestId(_actor.ProfileId, stableKey, surfaceScope);
            // Surface the source-owned pending route-audit handle (SavedAuditPending / OutcomeUnconfirmed)
            // alongside the approval line so the host can finish/retry it through that SAME source owner.
            Surface.PendingAuditRequestId = _source.GetPendingAuditRequestId(_actor.ProfileId, stableKey, surfaceScope);
            if (result.Succeeded)
            {
                // The selection source writes through its own actor-scoped route owner. Reload this
                // editor's canonical baseline before any queued model or route draft can use it.
                _editor.RequireRouteRefresh();
                Surface.ApplyState(_editor.Current);
                var refreshed = await _editor.RefreshAsync(routeSnapshot.Scope, routeSnapshot.Category,
                    cancellationToken: cancellationToken).ConfigureAwait(false);
                if (!refreshed.Succeeded)
                    return new(true, "ReasoningSavedRouteRefreshRequired",
                        "Reasoning level saved. Refresh the route to continue editing.", result.Value);
            }
        }
        return result.Succeeded
            ? new HomeCoreOperationResult<object>(true, "Succeeded", "Reasoning level saved.", result.Value)
            : new HomeCoreOperationResult<object>(false, result.Error?.Code.ToString() ?? "HomeOperationFailed",
                result.Error?.Message ?? "The reasoning-level commit failed.");
    }

    /// <summary>Resolves the effective visible route candidate's advertised reasoning choices and selected
    /// label from the shared source so the authored picker renders real, ordered LevelID choices. Advertised
    /// tiers are bound only to the Chat/Active personal route's effective model; other categories/models never
    /// show Chat tiers. A truthful unknown (no source, no resolved projection) leaves the surface's advertised
    /// scale empty.</summary>
    private async Task ApplyEffectiveReasoningAsync(CancellationToken cancellationToken)
    {
        if (_source is null || _actor is null) return;
        if (!HomeModelPickerCuiSurface.IsMainCategory(Surface.Category))
        {
            // Non-Chat/Active surfaces never carry Chat-tier choices or a pending Home review; clear
            // any stale effective state so Chat choices can never leak into another category/view.
            Surface.ApplyEffectiveReasoning([], null, null, null, null);
            Surface.PendingApprovalRequestId = null;
            Surface.PendingAuditRequestId = null;
            return;
        }
        var snapshot = await _source.ResolveAsync(Interlocked.Increment(ref _generation), cancellationToken).ConfigureAwait(false);
        // Recheck currentness after the awaited projection: a retired panel or actor switch must not
        // publish a stale selected tier or stale pending review.
        if (!CanPublish()) return;
        Surface.ApplyCompactModel(snapshot);
        if (snapshot is null)
        {
            Surface.ApplyEffectiveReasoning([], null, null, null, null);
            Surface.PendingApprovalRequestId = null;
            Surface.PendingAuditRequestId = null;
            return;
        }
        var surfaceScope = $"{Surface.Scope}:{Surface.ScopeId}";
        var stableKey = new ModelIdentity(snapshot.ProviderId ?? "", snapshot.ModelId ?? "").StableKey;
        Surface.ApplyEffectiveReasoning(snapshot.AdvertisedReasoningIds,
            snapshot.ProviderId, snapshot.ModelId, null, snapshot.SelectedReasoningId);
        Surface.PendingApprovalRequestId = _source.GetPendingApprovalRequestId(_actor.ProfileId, stableKey, surfaceScope);
        Surface.PendingAuditRequestId = _source.GetPendingAuditRequestId(_actor.ProfileId, stableKey, surfaceScope);
    }

    /// <summary>Finishes a retained route audit. When the shared source is wired it is finished through
    /// that SAME owning provider/broker (never CAS), and the surface line is refreshed from the source;
    /// otherwise it falls back to the route editor's own audit finish.</summary>
    private async Task<HomeCoreOperationResult<object>> FinishAuditAsync(CancellationToken cancellationToken)
    {
        if (_source is not null && _actor is not null && !string.IsNullOrWhiteSpace(Surface.PendingAuditRequestId))
        {
            var result = await _source.FinishAuditAsync(Surface.PendingAuditRequestId, cancellationToken).ConfigureAwait(false);
            if (CanPublish())
            {
                var scope = $"{Surface.Scope}:{Surface.ScopeId}";
                var stableKey = new ModelIdentity(Surface.EffectiveProviderId ?? "", Surface.EffectiveModelId ?? "").StableKey;
                Surface.PendingAuditRequestId = _source.GetPendingAuditRequestId(_actor.ProfileId, stableKey, scope);
            }
            return Box(result);
        }
        return Box(await _editor.FinishAuditAsync(cancellationToken).ConfigureAwait(false));
    }

    private HomeCoreOperationResult<HomeModelRouteContract> Move(HomeModelPickerActionRequest action, int direction)
    {
        var candidate = _editor.Current.Candidates.FirstOrDefault(item =>
            item.ProviderId == action.ProviderId && item.ModelId == action.ModelId && item.ArtifactRevision == action.ArtifactRevision);
        if (candidate is null) return Failure<HomeModelRouteContract>("CandidateNotFound", "That model is not in the selected route.");
        return _editor.MoveCandidate(candidate.ProviderId, candidate.ModelId, candidate.ArtifactRevision, candidate.Order + direction);
    }

    private static string Required(string? value, string label) => string.IsNullOrWhiteSpace(value)
        ? throw new ArgumentException($"A {label} is required.")
        : value;

    private static HomeCoreOperationResult<object> Box<T>(HomeCoreOperationResult<T> result) =>
        new(result.Succeeded, result.Code, result.Message, result.Value, result.Recoverable, result.Revision);

    private static HomeCoreOperationResult<T> Failure<T>(string code, string message) => new(false, code, message);
}
