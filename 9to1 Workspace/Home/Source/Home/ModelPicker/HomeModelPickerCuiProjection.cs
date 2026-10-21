using System.Globalization;
using System.Text.Json;
using CakeOS.Cui;
using Haven.Core;
using HavenOS.Home.Core;

namespace HavenOS.Home;

/// <summary>Explicit read-only CUI values for the route editor; no runtime reflection or routing decisions.</summary>
public sealed class HomeModelPickerCuiProjection : ICuiBindingContext
{
    private readonly HomeModelPickerEditorState _state;
    private readonly string _scope;
    private readonly string _category;
    private readonly IReadOnlyList<string>? _advertisedReasoningIds;
    private readonly string? _effectiveProviderId;
    private readonly string? _effectiveModelId;
    private readonly string? _effectiveArtifactRevision;
    private readonly string? _effectiveSelectedReasoningId;
    private readonly string? _pendingApprovalRequestId;

    public HomeModelPickerCuiProjection(
        HomeModelPickerEditorState state,
        string scope,
        string category,
        IReadOnlyList<string>? advertisedReasoningIds = null,
        string? effectiveProviderId = null,
        string? effectiveModelId = null,
        string? effectiveArtifactRevision = null,
        string? effectiveSelectedReasoningId = null,
        string? pendingApprovalRequestId = null)
    {
        _state = state ?? throw new ArgumentNullException(nameof(state));
        _scope = scope;
        _category = category;
        _advertisedReasoningIds = advertisedReasoningIds;
        _effectiveProviderId = effectiveProviderId;
        _effectiveModelId = effectiveModelId;
        _effectiveArtifactRevision = effectiveArtifactRevision;
        _effectiveSelectedReasoningId = effectiveSelectedReasoningId;
        _pendingApprovalRequestId = pendingApprovalRequestId;
        Routes = Array.AsReadOnly((state.Snapshot?.Routes ?? [])
            .Select(route => new HomeModelPickerRouteRow(
                route.RouteId == state.SelectedRouteId && state.DraftRoute is { } draft ? draft : route,
                _advertisedReasoningIds, _effectiveProviderId, _effectiveModelId, _effectiveArtifactRevision,
                _effectiveSelectedReasoningId))
            .ToArray());

        var occurrences = new Dictionary<string, int>(StringComparer.Ordinal);
        Trace = Array.AsReadOnly((state.Preview?.Trace ?? []).Select(text =>
        {
            var occurrence = occurrences.GetValueOrDefault(text);
            occurrences[text] = occurrence + 1;
            return new HomeModelPickerTraceRow(text, occurrence);
        }).ToArray());
    }

    public IReadOnlyList<HomeModelPickerRouteRow> Routes { get; }
    public IReadOnlyList<HomeModelPickerTraceRow> Trace { get; }

    public bool HasSnapshot => _state.Snapshot is not null;

    /// <summary>Canonical boolean for the authored picker's <c>&lt;If&gt;</c> condition. The canonical CUI
    /// runtime requires a strict <c>bool</c> for condition tests; a nullable/string request ID would throw
    /// CUIR011 at load time, so the panel binds this boolean, never the request-ID string.</summary>
    public bool HasPendingApproval => !string.IsNullOrWhiteSpace(_pendingApprovalRequestId);

    public bool TryGetValue(string path, out object? value)
    {
        switch (path)
        {
            case "State.SelectedRouteId": value = _state.SelectedRouteId; return true;
            case "Scope": value = _scope; return true;
            case "Category": value = _category; return true;
            case "StatusMessage": value = _state.StatusMessage; return true;
            case "Snapshot.Routes": value = Routes; return true;
            case "HasSnapshot": value = HasSnapshot; return true;
            case "State.HasSnapshot": value = HasSnapshot; return true;
            case "HasRoutes": value = HasSnapshot && Routes.Count != 0; return true;
            case "HasPreview": value = _state.Preview is not null; return true;
            case "HasTrace": value = Trace.Count != 0; return true;
            case "Preview.ResolutionState": value = _state.Preview?.ResolutionState ?? string.Empty; return true;
            case "Preview.Trace": value = Trace; return true;
            case "RefreshSelection": value = new HomeModelPickerScopeSelection(_scope, _category); return true;
            case "HasPendingApproval": value = HasPendingApproval; return true;
            case "PendingApprovalRequestId": value = _pendingApprovalRequestId; return true;
            case "CurrentRouteSelection": value = string.IsNullOrWhiteSpace(_state.SelectedRouteId)
                ? null : new HomeModelPickerRouteSelection(_state.SelectedRouteId); return _state.SelectedRouteId is not null;
            default: value = null; return false;
        }
    }
}

/// <summary>Canonical typed CUI action parameters for the model picker. These are the only
/// objects routed through named &lt;Action parameter=...&gt; declarations; no ad-hoc metadata
/// attributes are transported.</summary>
/// <summary>Canonical label projection for an advertised reasoning-level ID list. The label derives
/// only from the level's ordinal and the model's advertised level count via
/// <see cref="ReasoningScale.TierPercentages"/> (3=35/70/100, 4=25/50/75/100, 5=20/40/60/80/100).
/// Unknown counts and out-of-range ordinals stay a truthful unknown (null), never a fake 100 or an
/// invented on/off or enum mapping.</summary>
public static class HomeModelPickerReasoningLabels
{
    public static string? LabelForLevel(IReadOnlyList<string> advertisedIds, string? levelId)
    {
        if (advertisedIds is null || string.IsNullOrWhiteSpace(levelId)) return null;
        var ordinal = -1;
        for (var i = 0; i < advertisedIds.Count; i++)
        {
            if (StringComparer.Ordinal.Equals(advertisedIds[i], levelId)) { ordinal = i; break; }
        }
        if (ordinal < 0 || !ReasoningScale.TryTierPercentage(advertisedIds.Count, ordinal, out var percentage)) return null;
        return $"{percentage}%";
    }
}

public sealed record HomeModelPickerScopeSelection(string Scope, string Category);
public sealed record HomeModelPickerRouteSelection(string RouteId);
public sealed record HomeModelPickerCandidateSelection(string ProviderId, string ModelId, string? ArtifactRevision);
public sealed record HomeModelPickerCandidateToggleSelection(string ProviderId, string ModelId, string? ArtifactRevision, bool Enabled);
public sealed record HomeModelPickerReasoningLevelSelection(string ProviderId, string ModelId, string? ArtifactRevision, string? ReasoningLevelId);

public sealed class HomeModelPickerRouteRow : ICuiBindingContext
{
    private readonly HomeModelRouteContract _route;

    internal HomeModelPickerRouteRow(
        HomeModelRouteContract route,
        IReadOnlyList<string>? advertisedReasoningIds = null,
        string? effectiveProviderId = null,
        string? effectiveModelId = null,
        string? effectiveArtifactRevision = null,
        string? effectiveSelectedReasoningId = null)
    {
        _route = route;
        Candidates = Array.AsReadOnly(route.Candidates.OrderBy(candidate => candidate.Order)
            .Select(candidate => new HomeModelPickerCandidateRow(candidate,
                advertisedReasoningIds, effectiveProviderId, effectiveModelId, effectiveArtifactRevision,
                effectiveSelectedReasoningId)).ToArray());
    }

    public IReadOnlyList<HomeModelPickerCandidateRow> Candidates { get; }

    public bool TryGetValue(string path, out object? value)
    {
        switch (path)
        {
            case "RouteId": value = _route.RouteId; return true;
            case "Scope": value = _route.Scope; return true;
            case "Category": value = _route.Category; return true;
            case "Candidates": value = Candidates; return true;
            case "HasCandidates": value = Candidates.Count != 0; return true;
            case "Selection": value = new HomeModelPickerRouteSelection(_route.RouteId); return true;
            default: value = null; return false;
        }
    }
}

public sealed class HomeModelPickerCandidateRow : ICuiBindingContext
{
    private readonly HomeModelRouteCandidate _candidate;
    private readonly IReadOnlyList<string>? _advertisedReasoningIds;
    private readonly bool _isEffective;

    internal HomeModelPickerCandidateRow(
        HomeModelRouteCandidate candidate,
        IReadOnlyList<string>? advertisedReasoningIds = null,
        string? effectiveProviderId = null,
        string? effectiveModelId = null,
        string? effectiveArtifactRevision = null,
        string? effectiveSelectedReasoningId = null)
    {
        _candidate = candidate;
        _advertisedReasoningIds = advertisedReasoningIds;
        // View identity excludes mutable order/enabled state and keeps provider/revision boundaries unambiguous.
        Identity = JsonSerializer.Serialize(new[] { candidate.ProviderId, candidate.ModelId, candidate.ArtifactRevision });
        _isEffective = !string.IsNullOrWhiteSpace(effectiveProviderId) && !string.IsNullOrWhiteSpace(effectiveModelId)
            && StringComparer.OrdinalIgnoreCase.Equals(candidate.ProviderId, effectiveProviderId)
            && StringComparer.OrdinalIgnoreCase.Equals(candidate.ModelId, effectiveModelId)
            && (effectiveArtifactRevision is null
                || string.Equals(candidate.ArtifactRevision, effectiveArtifactRevision, StringComparison.Ordinal));

        // The selected tier is the effective candidate's LIVE selected reasoning id from the shared
        // source (verbatim), not the raw persisted field — the live label and the selection state must
        // agree with what the picker shows as the current effective choice. Non-effective candidates
        // never expose advertised choices.
        var selectedReasoningId = _isEffective ? effectiveSelectedReasoningId : null;
        if (HasAdvertisedReasoningLevels)
        {
            ReasoningLevelChoices = Array.AsReadOnly(_advertisedReasoningIds!.Select(id => new HomeModelPickerReasoningChoice(
                candidate, id, HomeModelPickerReasoningLabels.LabelForLevel(_advertisedReasoningIds!, id)
                    ?? id, string.Equals(id, selectedReasoningId, StringComparison.Ordinal))).ToArray());
            SelectedReasoningLabel = HomeModelPickerReasoningLabels.LabelForLevel(
                _advertisedReasoningIds!, selectedReasoningId);
        }
        else
        {
            ReasoningLevelChoices = Array.Empty<HomeModelPickerReasoningChoice>();
            SelectedReasoningLabel = null;
        }
    }

    public string Identity { get; }
    public bool HasAdvertisedReasoningLevels => _advertisedReasoningIds is { Count: > 0 } && _isEffective;
    public IReadOnlyList<HomeModelPickerReasoningChoice> ReasoningLevelChoices { get; }
    public string? SelectedReasoningLabel { get; }

    public bool TryGetValue(string path, out object? value)
    {
        switch (path)
        {
            case "Identity": value = Identity; return true;
            case "ProviderId": value = _candidate.ProviderId; return true;
            case "ModelId": value = _candidate.ModelId; return true;
            case "ArtifactRevision": value = _candidate.ArtifactRevision; return true;
            case "Enabled": value = _candidate.Enabled; return true;
            case "Order": value = _candidate.Order; return true;
            case "ReasoningLevelId": value = _candidate.ReasoningLevelId; return true;
            case "HasReasoningLevel": value = !string.IsNullOrWhiteSpace(_candidate.ReasoningLevelId); return true;
            case "HasAdvertisedReasoningLevels": value = HasAdvertisedReasoningLevels; return true;
            case "ReasoningLevelChoices": value = ReasoningLevelChoices; return true;
            case "SelectedReasoningLabel": value = SelectedReasoningLabel; return true;
            case "Selection": value = new HomeModelPickerCandidateSelection(_candidate.ProviderId, _candidate.ModelId, _candidate.ArtifactRevision); return true;
            case "EnableSelection": value = new HomeModelPickerCandidateToggleSelection(_candidate.ProviderId, _candidate.ModelId, _candidate.ArtifactRevision, true); return true;
            case "DisableSelection": value = new HomeModelPickerCandidateToggleSelection(_candidate.ProviderId, _candidate.ModelId, _candidate.ArtifactRevision, false); return true;
            case "SetReasoningLevelSelection": value = new HomeModelPickerReasoningLevelSelection(_candidate.ProviderId, _candidate.ModelId, _candidate.ArtifactRevision, _candidate.ReasoningLevelId); return true;
            case "ClearReasoningLevelSelection": value = new HomeModelPickerReasoningLevelSelection(_candidate.ProviderId, _candidate.ModelId, _candidate.ArtifactRevision, null); return true;
            default: value = null; return false;
        }
    }
}

/// <summary>One real advertised reasoning-level choice for the effective route candidate, carrying the
/// exact advertised LevelID and its canonical tier label plus a typed selection action.</summary>
public sealed class HomeModelPickerReasoningChoice : ICuiBindingContext
{
    private readonly HomeModelPickerReasoningLevelSelection _selection;

    internal HomeModelPickerReasoningChoice(HomeModelRouteCandidate candidate, string id, string label, bool isSelected)
    {
        Identity = JsonSerializer.Serialize(new[] { candidate.ProviderId, candidate.ModelId, id });
        Id = id;
        Label = label;
        IsSelected = isSelected;
        _selection = new HomeModelPickerReasoningLevelSelection(candidate.ProviderId, candidate.ModelId, candidate.ArtifactRevision, id);
    }

    public string Identity { get; }
    public string Id { get; }
    public string Label { get; }
    public bool IsSelected { get; }

    public bool TryGetValue(string path, out object? value)
    {
        switch (path)
        {
            case "Identity": value = Identity; return true;
            case "Id": value = Id; return true;
            case "Label": value = Label; return true;
            case "IsSelected": value = IsSelected; return true;
            case "Selection": value = _selection; return true;
            default: value = null; return false;
        }
    }
}

public sealed class HomeModelPickerTraceRow : ICuiBindingContext
{
    internal HomeModelPickerTraceRow(string text, int occurrence)
    {
        Text = text;
        Identity = JsonSerializer.Serialize(new[] { text, occurrence.ToString(CultureInfo.InvariantCulture) });
    }

    public string Text { get; }
    public string Identity { get; }

    public bool TryGetValue(string path, out object? value)
    {
        switch (path)
        {
            case "Identity": value = Identity; return true;
            case "Text": value = Text; return true;
            default: value = null; return false;
        }
    }
}
