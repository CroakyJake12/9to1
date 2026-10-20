using System.Globalization;
using System.Text.Json;
using CakeOS.Cui;
using HavenOS.Home.Core;

namespace HavenOS.Home;

/// <summary>Explicit read-only CUI values for the route editor; no runtime reflection or routing decisions.</summary>
public sealed class HomeModelPickerCuiProjection : ICuiBindingContext
{
    private readonly HomeModelPickerEditorState _state;
    private readonly string _scope;
    private readonly string _category;

    public HomeModelPickerCuiProjection(HomeModelPickerEditorState state, string scope, string category)
    {
        _state = state ?? throw new ArgumentNullException(nameof(state));
        _scope = scope;
        _category = category;
        Routes = Array.AsReadOnly((state.Snapshot?.Routes ?? [])
            .Select(route => new HomeModelPickerRouteRow(
                route.RouteId == state.SelectedRouteId && state.DraftRoute is { } draft ? draft : route))
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
            case "CurrentRouteSelection": value = string.IsNullOrWhiteSpace(_state.SelectedRouteId)
                ? null : new HomeModelPickerRouteSelection(_state.SelectedRouteId); return _state.SelectedRouteId is not null;
            default: value = null; return false;
        }
    }
}

/// <summary>Canonical typed CUI action parameters for the model picker. These are the only
/// objects routed through named &lt;Action parameter=...&gt; declarations; no ad-hoc metadata
/// attributes are transported.</summary>
public sealed record HomeModelPickerScopeSelection(string Scope, string Category);
public sealed record HomeModelPickerRouteSelection(string RouteId);
public sealed record HomeModelPickerCandidateSelection(string ProviderId, string ModelId, string? ArtifactRevision);
public sealed record HomeModelPickerCandidateToggleSelection(string ProviderId, string ModelId, string? ArtifactRevision, bool Enabled);

public sealed class HomeModelPickerRouteRow : ICuiBindingContext
{
    private readonly HomeModelRouteContract _route;

    internal HomeModelPickerRouteRow(HomeModelRouteContract route)
    {
        _route = route;
        Candidates = Array.AsReadOnly(route.Candidates.OrderBy(candidate => candidate.Order)
            .Select(candidate => new HomeModelPickerCandidateRow(candidate)).ToArray());
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

    internal HomeModelPickerCandidateRow(HomeModelRouteCandidate candidate)
    {
        _candidate = candidate;
        // View identity excludes mutable order/enabled state and keeps provider/revision boundaries unambiguous.
        Identity = JsonSerializer.Serialize(new[] { candidate.ProviderId, candidate.ModelId, candidate.ArtifactRevision });
    }

    public string Identity { get; }

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
            case "Selection": value = new HomeModelPickerCandidateSelection(_candidate.ProviderId, _candidate.ModelId, _candidate.ArtifactRevision); return true;
            case "EnableSelection": value = new HomeModelPickerCandidateToggleSelection(_candidate.ProviderId, _candidate.ModelId, _candidate.ArtifactRevision, true); return true;
            case "DisableSelection": value = new HomeModelPickerCandidateToggleSelection(_candidate.ProviderId, _candidate.ModelId, _candidate.ArtifactRevision, false); return true;
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
