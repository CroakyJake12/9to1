using System.ComponentModel;
using System.Text.Json;
using Avalonia.Threading;
using CakeOS.Cui;
using CakeOS.Cui.Runtime;
using HavenOS.Home;
using HavenOS.Home.Core;

namespace HavenOS.Home.NativeUI;

/// <summary>Presentation of the canonical personal Home route editor; no provider or approval policy is stored here.</summary>
public sealed class HomeModelPickerBindings : ICuiWritableBindingContext, ICuiRepeatItemBindingContext,
    ICuiActionDispatcher, INotifyPropertyChanged, IDisposable
{
    private static readonly string[] Categories = ["Active", "Background", "Chat", "Image", "Voice", "Audio", "Video"];
    private readonly IHomeModelPickerFeatureProvider _provider;
    private readonly HomeModelPickerRouteEditor _editor;
    private readonly Func<string, CancellationToken, Task>? _openHomePermissions;
    private readonly CuiViewModel _bindings = new();
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private readonly CancellationToken _token;
    private volatile bool _disposed;
    private volatile bool _viewUnavailable;
    public event PropertyChangedEventHandler? PropertyChanged
    { add => _bindings.PropertyChanged += value; remove => _bindings.PropertyChanged -= value; }

    public HomeModelPickerBindings(IHomeModelPickerFeatureProvider provider,
        Func<string, CancellationToken, Task>? openHomePermissions = null)
    {
        _provider = provider; _editor = new(provider); _openHomePermissions = openHomePermissions; _token = _lifetime.Token;
        _bindings.Set("Status", "Checking Home model routes…"); _bindings.Set("Selection", "No route selected");
        _bindings.Set("Category", "Active"); _bindings.Set("Query", ""); _bindings.Set("Capability", "");
        _bindings.Set("Preview", ""); _bindings.Set("Approval", ""); _bindings.Set("CatalogueStatus", "");
        foreach (var name in new[] { "CanSave", "CanDiscard", "CanPreview", "CanApprove", "CanEdit", "CanBrowse", "CanFinishAudit" }) _bindings.Set(name, false);
        _bindings.Set("AuditStatus", "");
        _bindings.GetOrCreateList<HomeModelRouteContract>("Routes");
        _bindings.GetOrCreateList<HomeModelRouteCandidate>("Candidates");
        _bindings.GetOrCreateList<HomeModelPickerCatalogueEntry>("Catalogue");
    }
    public Task OpenAsync(CancellationToken ct = default) => DispatchAsync("Refresh", null, ct).AsTask();
    public bool TryGetValue(string path, out object? value) => _bindings.TryGetValue(path, out value);
    public bool TrySetValue(string path, object? value) => !_disposed && !_viewUnavailable && (path is "Query" or "Capability") && _bindings.TrySetValue(path, value);
    private static string Identity(string provider, string model, string? revision) => JsonSerializer.Serialize(new[] { provider, model, revision });
    private static string Label(string provider, string model, string? revision) => $"{provider}/{model}" + (revision is null ? " (current provider variant)" : $"@{revision}");
    public bool TryGetItemValue(object item, string path, out object? value)
    {
        if (_disposed || _viewUnavailable) { value = null; return false; }
        value = (item, path) switch
        {
            (HomeModelRouteContract route, "RouteId") => route.RouteId,
            (HomeModelRouteContract route, "Label") => route.Category + (route.Version == 0 ? " · new personal route" : $" · revision {route.Version}"),
            (HomeModelRouteCandidate candidate, "Identity") => Identity(candidate.ProviderId, candidate.ModelId, candidate.ArtifactRevision),
            (HomeModelRouteCandidate candidate, "Label") => Label(candidate.ProviderId, candidate.ModelId, candidate.ArtifactRevision),
            (HomeModelRouteCandidate candidate, "Order") => candidate.Order + 1,
            (HomeModelRouteCandidate candidate, "Enabled") => candidate.Enabled ? "Enabled" : "Disabled",
            (HomeModelPickerCatalogueEntry model, "Identity") => Identity(model.ProviderId, model.ModelId, model.ArtifactRevision),
            (HomeModelPickerCatalogueEntry model, "DisplayName") => model.Alias ?? model.DisplayName,
            (HomeModelPickerCatalogueEntry model, "Label") => Label(model.ProviderId, model.ModelId, model.ArtifactRevision),
            (HomeModelPickerCatalogueEntry model, "Details") => $"{model.ProviderName} · {(model.IsLocal is true ? "Local" : model.IsLocal is false ? "Remote" : "Locality unreported")} · {(model.Capabilities is null ? "Capabilities unreported" : model.Capabilities.Count == 0 ? "No capabilities reported" : string.Join(", ", model.Capabilities.Order(StringComparer.Ordinal)))}",
            _ => null
        };
        return value is not null;
    }
    public bool TrySetItemValue(object item, string path, object? value) => false;
    public async ValueTask DispatchAsync(string command, object? parameter, CancellationToken cancellationToken = default)
    {
        using var request = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _token);
        await _gate.WaitAsync(request.Token);
        try
        {
            if (command != "FinishAudit" && !await OriginalSessionCurrentAsync(request.Token))
            {
                await Dispatcher.UIThread.InvokeAsync(ClearPrivatePresentation);
                return;
            }
            string? failure = null;
            if (command == "Refresh") await RefreshAsync(_editor.Current.Snapshot?.Category ?? "Active", false, request.Token);
            else if (command.StartsWith("Category.", StringComparison.Ordinal) && Categories.Contains(command[9..], StringComparer.Ordinal))
                await RefreshAsync(command[9..], false, request.Token);
            else if (command == "Discard") await RefreshAsync(_editor.Current.Snapshot?.Category ?? "Active", true, request.Token);
            else if (command == "SearchCatalogue") await CatalogueAsync(request.Token);
            else if (command == "SelectRoute" && parameter is HomeModelRouteContract route)
            { var result = _editor.SelectRoute(route.RouteId); if (!result.Succeeded) failure = result.Message; }
            else if (command == "Add" && parameter is HomeModelPickerCatalogueEntry model)
            { var result = _editor.AddCandidate(model); if (!result.Succeeded) failure = result.Message; }
            else if (parameter is HomeModelRouteCandidate candidate)
            {
                HomeCoreOperationResult<HomeModelRouteContract>? result = command switch
                {
                    "Toggle" => _editor.SetCandidateEnabled(candidate.ProviderId, candidate.ModelId, candidate.ArtifactRevision, !candidate.Enabled),
                    "Earlier" or "Later" => _editor.MoveCandidate(candidate.ProviderId, candidate.ModelId, candidate.ArtifactRevision, candidate.Order + (command == "Earlier" ? -1 : 1)),
                    "Remove" => _editor.RemoveCandidate(candidate.ProviderId, candidate.ModelId, candidate.ArtifactRevision),
                    _ => null
                };
                if (result is { Succeeded: false }) failure = result.Message;
            }
            else if (command == "FinishAudit") await _editor.FinishAuditAsync(request.Token);
            else if (command == "Save") await _editor.SaveAsync(request.Token);
            else if (command == "HomePermissions" && _editor.Current.PendingApprovalRequestId is { } pending && _openHomePermissions is not null)
            {
                await _openHomePermissions(pending, request.Token);
                // Closing Home is not approval. The same bound operation is re-authorised by the actual broker.
                await _editor.SaveAsync(request.Token);
            }
            else if (command == "Preview" && _editor.Current.DraftRoute is { Version: > 0 } && !_editor.Current.HasUnsavedChanges) await _editor.PreviewAsync(_bindings.Get("Capability")?.ToString() ?? "", cancellationToken: request.Token);
            await PopulateAsync();
            if (failure is not null) await Dispatcher.UIThread.InvokeAsync(() => { if (!_disposed && !_viewUnavailable) _bindings.Set("Status", failure); });
        }
        catch (OperationCanceledException) when (request.IsCancellationRequested) { }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
        {
            await RevalidateAsync();
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (!_disposed && !_viewUnavailable) _bindings.Set("Status", $"Home model service unavailable ({ex.GetType().Name}). Existing routes were preserved.");
            });
        }
        finally { _gate.Release(); }
    }
    private async Task RefreshAsync(string category, bool discard, CancellationToken ct)
    {
        var result = await _editor.RefreshAsync("User", category, discard, ct);
        if (result.Succeeded) await CatalogueAsync(ct);
        else await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            if (_disposed) return;
            if (!await OriginalSessionCurrentAsync(ct)) { ClearPrivatePresentation(); return; }
            if (!_disposed && !_viewUnavailable) _bindings.Set("CatalogueStatus", result.Message);
        });
    }
    private async Task CatalogueAsync(CancellationToken ct)
    {
        // Actual Home applies provider privacy policy before contacting catalogue transports.
        var result = await _provider.GetCatalogueAsync(_bindings.Get("Query")?.ToString(), ct);
        await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            if (_disposed) return;
            if (!await OriginalSessionCurrentAsync(ct)) { ClearPrivatePresentation(); return; }
            if (_disposed) return;
            var entries = _bindings.GetOrCreateList<HomeModelPickerCatalogueEntry>("Catalogue"); entries.Clear();
            if (result.Succeeded && result.Value is { } page)
            {
                foreach (var model in page.Items) entries.Add(model);
                _bindings.Set("CatalogueStatus", page.HasMore ? "More catalogue entries are available; narrow your search." : $"{page.Items.Count} catalogue entries.");
            }
            else _bindings.Set("CatalogueStatus", result.Message);
        });
    }
    private async Task PopulateAsync()
    {
        await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            if (_disposed) return;
            if (!await OriginalSessionCurrentAsync(_token)) { ClearPrivatePresentation(); return; }
            if (_disposed) return;
            var state = _editor.Current;
            _bindings.Set("CanBrowse", true);
            _bindings.Set("CanFinishAudit", state.CanFinishAudit);
            _bindings.Set("AuditStatus", state.PendingAuditRequestId is not null ? "The original route outcome has a pending audit." : "");
            _bindings.Set("Status", state.StatusMessage); _bindings.Set("Selection", state.DraftRoute is { } route ? $"Personal {route.Category} route" : "No route selected");
            _bindings.Set("Category", state.Snapshot?.Category ?? "Active");
            _bindings.Set("CanSave", state.HasUnsavedChanges && !state.IsBusy);
            _bindings.Set("CanDiscard", state.HasUnsavedChanges && !state.IsBusy);
            _bindings.Set("CanEdit", state.DraftRoute is not null && !state.IsBusy);
            _bindings.Set("CanPreview", state.DraftRoute is { Version: > 0 } && !state.HasUnsavedChanges && !state.IsBusy);
            _bindings.Set("CanApprove", state.PendingApprovalRequestId is not null && _openHomePermissions is not null && !state.IsBusy);
            _bindings.Set("Approval", state.PendingApprovalRequestId is { } pending ? $"Home approval required: {pending}. Open Home permissions, then retry this unchanged save." : "");
            _bindings.Set("Preview", state.Preview is { } preview ? $"{preview.ResolutionState}: {preview.SelectedIdentity ?? "No eligible model"}\n" + string.Join("\n", preview.Trace) : "");
            var routes = _bindings.GetOrCreateList<HomeModelRouteContract>("Routes"); routes.Clear(); foreach (var listedRoute in state.Snapshot?.Routes ?? []) routes.Add(listedRoute);
            var candidates = _bindings.GetOrCreateList<HomeModelRouteCandidate>("Candidates"); candidates.Clear(); foreach (var candidate in state.Candidates) candidates.Add(candidate);
        });
    }
    private async Task<bool> OriginalSessionCurrentAsync(CancellationToken token)
    {
        if (_disposed || _viewUnavailable) return false;
        if (_provider is not HomeModelPickerOriginalSessionProvider original) return true; // Legacy presentation compatibility; native host supplies the bound provider.
        try { if (await original.IsCurrentAsync(token)) return !_disposed && !_viewUnavailable; }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception error) when (error is not OutOfMemoryException) { /* An unreadable actor is unavailable, never an old-actor grant. */ }
        _viewUnavailable = true;
        return false;
    }
    public Task<bool> RevalidateAsync(CancellationToken token = default) => Dispatcher.UIThread.InvokeAsync(async () =>
    {
        if (_disposed) return false;
        var current = await OriginalSessionCurrentAsync(token);
        if (_disposed) return false;
        if (!current) ClearPrivatePresentation();
        return current;
    });
    private void ClearPrivatePresentation()
    {
        // Preserve the editor's original known outcome/audit handle. Only presentation is removed.
        _bindings.GetOrCreateList<HomeModelRouteContract>("Routes").Clear();
        _bindings.GetOrCreateList<HomeModelRouteCandidate>("Candidates").Clear();
        _bindings.GetOrCreateList<HomeModelPickerCatalogueEntry>("Catalogue").Clear();
        _bindings.Set("Status", "The originating model session is unavailable. Reopen this manager before a new edit.");
        _bindings.Set("Selection", "No route selected"); _bindings.Set("Category", "Active");
        foreach (var name in new[] { "Query", "Capability", "Preview", "Approval", "CatalogueStatus" }) _bindings.Set(name, "");
        foreach (var name in new[] { "CanSave", "CanDiscard", "CanPreview", "CanApprove", "CanEdit", "CanBrowse" }) _bindings.Set(name, false);
        _bindings.Set("CanFinishAudit", !_disposed && _editor.Current.CanFinishAudit);
        _bindings.Set("AuditStatus", !_disposed && _editor.Current.PendingAuditRequestId is not null ? "The original route outcome has a pending audit. Finish records that outcome only." : "");
    }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true; _lifetime.Cancel();
        if (Dispatcher.UIThread.CheckAccess()) ClearPrivatePresentation();
        else Dispatcher.UIThread.Post(ClearPrivatePresentation);
        _lifetime.Dispose();
    }
}
