using System.ComponentModel;
using System.Text.Json;
using CakeOS.Cui;
using CakeOS.Cui.Language;
using Dulche.Runtime;
using Haven.Application;
using HavenOS.Home.Core;

namespace HavenOS.Home;

public enum HomeModelPickerAction
{
    Refresh,
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
    JsonElement? Context = null);

/// <summary>Owns the authored Home model-picker CUI document and the typed action queue for its host.</summary>
public sealed class HomeModelPickerCuiSurface : ICuiBindingContext, INotifyPropertyChanged
{
    private readonly Queue<HomeModelPickerActionRequest> _actions = new();

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
    public string? PendingApprovalRequestId { get; set; }
    public string? PendingAuditRequestId { get; set; }
    public HomeModelPickerCuiProjection Projection { get; private set; } =
        new(HomeModelPickerEditorState.Initial, "global", "chat");
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>True once a real route snapshot has been applied to this surface.</summary>
    public bool HasSnapshot => Projection.HasSnapshot;

    public bool TryGetValue(string path, out object? value) => Projection.TryGetValue(path, out value);

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
        }
        RebuildProjection();
    }

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

    private void RebuildProjection()
    {
        Projection = new HomeModelPickerCuiProjection(State, Scope, Category,
            EffectiveAdvertisedReasoningIds, EffectiveProviderId, EffectiveModelId, EffectiveArtifactRevision,
            EffectiveSelectedReasoningId, PendingApprovalRequestId);
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
    }

    public bool Request(HomeModelPickerActionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Action == HomeModelPickerAction.SelectCategory && string.IsNullOrWhiteSpace(request.Category))
            return false;
        if (request.Action == HomeModelPickerAction.SelectRoute && string.IsNullOrWhiteSpace(request.RouteId))
            return false;
        if ((request.Action is HomeModelPickerAction.SetCandidateEnabled or HomeModelPickerAction.MoveCandidateUp or HomeModelPickerAction.MoveCandidateDown or HomeModelPickerAction.SetReasoningLevel)
            && (string.IsNullOrWhiteSpace(request.ProviderId) || string.IsNullOrWhiteSpace(request.ModelId) || (request.ArtifactRevision is not null && string.IsNullOrWhiteSpace(request.ArtifactRevision))))
            return false;
        if (request.Action == HomeModelPickerAction.SetCandidateEnabled && request.Enabled is null)
            return false;
        if (request.Action == HomeModelPickerAction.SetReasoningLevel && request.ReasoningLevelId is not null && string.IsNullOrWhiteSpace(request.ReasoningLevelId))
            return false;

        _actions.Enqueue(request);
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
        }
        return result;
    }

    public async Task<HomeCoreOperationResult<object>> ExecuteAsync(
        HomeModelPickerActionRequest action,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);
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
        if (CanPublish())
        {
            Surface.ApplyState(_editor.Current);
            await ApplyEffectiveReasoningAsync(cancellationToken).ConfigureAwait(false);
        }
        return result;
    }

    /// <summary>The canonical picker commit path: persists the chosen advertised reasoning-level default
    /// through the shared Home-owned source (guarded route CAS when wired; otherwise the retained memory
    /// API). Refusals surface as a typed non-success with no state mutation.</summary>
    private async Task<HomeCoreOperationResult<object>> CommitReasoningLevelAsync(
        HomeModelPickerActionRequest action, CancellationToken cancellationToken)
    {
        if (_source is null || _actor is null)
            return Failure<object>("SourceUnavailable", "No selected-reasoning source/actor is available to commit a level.");
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
        var chatOrActive = string.Equals(Surface.Category, "chat", StringComparison.OrdinalIgnoreCase)
            || string.Equals(Surface.Category, "active", StringComparison.OrdinalIgnoreCase);
        if (!chatOrActive)
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
