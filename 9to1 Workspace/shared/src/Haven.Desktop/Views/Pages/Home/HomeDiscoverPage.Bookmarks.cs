using Avalonia.Threading;
using Haven.Desktop.Services;
using HavenOS.Home.Discover;

namespace Haven.Desktop.Views.Pages.Home;

public sealed record HomeDiscoverModelUseResult(bool Applied, string? Code, string Message);

public sealed partial class HomeDiscoverPage
{
    private sealed record SavedModelRow(HomeSavedModel Saved, HomeDiscoverModel? Current);
    private HomeSavedModelPreferences? _savedModels;
    private HomeSavedModelPreferences.Snapshot? _savedModelsSnapshot;
    private SavedModelRow[] _savedModelRows = [];
    private Func<HomeDiscoverIdentity, CancellationToken, Task<HomeDiscoverModelUseResult>>? _useModel;
    private bool _modelPreferenceBusy;
    private long _savedModelReadVersion, _modelDetailsEpoch;
    private string? _savedModelsError;

    internal void BindSavedModels(HomeSavedModelPreferences? preferences)
    {
        if (_active || _generation != 0) throw new InvalidOperationException("Bind saved models before activating Models.");
        _savedModels = preferences;
        RefreshModelPreferences();
    }

    private async Task ReadSavedModelsAsync(long catalogueGeneration, CancellationToken token)
    {
        if (_savedModels is not { } preferences || _modelPreferenceBusy) return;
        var version = _savedModelReadVersion;
        try
        {
            var snapshot = await AwaitDiscoverSourceAsync(preferences.ReadAsync(token));
            if (!Current(catalogueGeneration, token) || version != _savedModelReadVersion) return;
            _savedModelsSnapshot = snapshot; _savedModelsError = null;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception error)
        {
            if (Current(catalogueGeneration, token) && version == _savedModelReadVersion)
            {
                _savedModelsSnapshot = null;
                _savedModelsError = "Saved models could not be read. " + error.Message;
            }
        }
        if (Current(catalogueGeneration, token)) RefreshModelPreferences();
    }

    private bool IsCurrentInspectedModel(HomeDiscoverModel? model) => model is not null &&
        ReferenceEquals(_inspectedModel, model) && _catalogueModels.Any(current => ReferenceEquals(current, model)) &&
        (_view != "local-details" || _dulche?.CurrentModelId == model.Identity.ModelId);

    private bool CanUseInspectedModel => _useModel is not null && IsCurrentInspectedModel(_inspectedModel) &&
        _inspectedModel!.Categories?.Contains(HomeDiscoverCategory.Chat) == true;

    private bool? ModelPreferenceActionAvailable(string command) => command switch
    {
        "models.save" => CanEditModelBookmarks && IsCurrentInspectedModel(_inspectedModel),
        "models.remove-saved" => CanEditModelBookmarks,
        "models.saved-details" => !_disposed && !_work.IsRetiring && _active && !_modelPreferenceBusy,
        "models.use" => !_disposed && !_work.IsRetiring && _active && !_modelPreferenceBusy && CanUseInspectedModel,
        _ => null
    };

    private bool CanEditModelBookmarks => !_disposed && !_work.IsRetiring && _active && !_modelPreferenceBusy &&
        _savedModels?.CanEdit == true && _savedModelsSnapshot is not null;

    private ValueTask DispatchModelPreferenceAsync(string command, object? parameter, CancellationToken token)
    {
        Dispatcher.UIThread.VerifyAccess();
        if (ModelPreferenceActionAvailable(command) != true) return ValueTask.CompletedTask;
        var selected = _inspectedModel;
        var epoch = _modelDetailsEpoch;
        var snapshot = _savedModelsSnapshot;
        var row = parameter as SavedModelRow;
        if (command is "models.remove-saved" or "models.saved-details" &&
            (row is null || !_savedModelRows.Any(current => ReferenceEquals(current, row)))) return ValueTask.CompletedTask;
        if (command is "models.save" or "models.use" && (parameter is not null || !IsCurrentInspectedModel(selected)))
            return ValueTask.CompletedTask;
        var remove = command == "models.remove-saved" || command == "models.save" &&
            snapshot!.List.Models.Any(saved => saved.Identity == selected!.Identity);
        // Claim before Home admission: a delayed click retains its model, revision and
        // add/remove intent, and a second click cannot enqueue another mutation.
        _modelPreferenceBusy = true; _savedModelReadVersion++;
        RefreshModelPreferences();
        return new(RunDiscoverAsync(async cancellation =>
        {
            if (_disposed || !_active || _work.IsRetiring) return;
            if (command is "models.save" or "models.use" &&
                (epoch != _modelDetailsEpoch || !IsCurrentInspectedModel(selected)))
            { SetDiscoverValue(_status, "Text", "The inspected model changed. Review its current details before choosing this action."); return; }
            if (command == "models.saved-details")
            {
                var current = _catalogueModels.SingleOrDefault(model => model.Identity == row!.Saved.Identity);
                if (current is null)
                { SetDiscoverValue(_status, "Text", "This saved model is not present in the current catalogue. Refresh its provider to inspect it."); return; }
                _detailsOrigin = "mine"; _inspectedModel = current; _modelDetailsEpoch++;
                ShowView(current.Identity.ProviderId == DulcheSocketModelProvider.ProviderId &&
                    _dulche?.TryShowModel(current.Identity.ModelId) == true ? "local-details" : "details");
                return;
            }
            if (command == "models.use")
            {
                var outcome = await AwaitDiscoverSourceAsync(_useModel!(selected!.Identity, cancellation));
                if (!_disposed && _active && epoch == _modelDetailsEpoch && IsCurrentInspectedModel(selected))
                    SetDiscoverValue(_status, "Text", outcome.Message);
                return;
            }
            var preferences = _savedModels ?? throw new InvalidOperationException("The saved model owner is unavailable.");
            var expected = snapshot ?? throw new InvalidOperationException("Read the saved model list before changing it.");
            var identity = row?.Saved.Identity ?? selected!.Identity;
            var models = remove ? expected.List.Models.Where(saved => saved.Identity != identity).ToArray()
                : expected.List.Models.Append(new HomeSavedModel(identity, selected!.DisplayName ?? selected.Name, DateTimeOffset.UtcNow)).ToArray();
            var result = await AwaitDiscoverSourceAsync(preferences.SaveAsync(expected, new(expected.List.Revision, models), cancellation));
            if (_disposed || !_active || _work.IsRetiring) return;
            _savedModelsSnapshot = result.Current; _savedModelsError = null;
            SetDiscoverValue(_status, "Text", !result.Saved
                ? "Saved models changed in another view. The latest list is shown; review it and try again."
                : remove ? "Removed from saved models." : "Model saved for later.");
            RefreshModelPreferences();
        }, token, () =>
        {
            _modelPreferenceBusy = false; _savedModelReadVersion++;
            if (!_disposed && !_work.IsRetiring) RefreshModelPreferences();
        }));
    }

    private void RefreshModelPreferences()
    {
        _savedModelRows = _savedModelsSnapshot?.List.Models.OrderByDescending(saved => saved.SavedAt)
            .Select(saved => new SavedModelRow(saved, _catalogueModels.SingleOrDefault(model => model.Identity == saved.Identity)))
            .ToArray() ?? [];
        var saved = _inspectedModel is { } inspected && _savedModelsSnapshot?.List.Models.Any(row => row.Identity == inspected.Identity) == true;
        _bindings.Set("models.savedRows", _savedModelRows);
        _bindings.Set("models.savedVisible", _view == "mine");
        _bindings.Set("models.savedEmpty", _savedModelRows.Length == 0);
        _bindings.Set("models.savedStatus", _savedModelsError ?? (_savedModels is null
            ? "Saved models are unavailable in this view."
            : _savedModelsSnapshot is null ? "Reading saved models…"
            : _savedModelRows.Length == 0 ? "Save a model from its details to keep it here for later." : ""));
        _bindings.Set("models.canRemoveSaved", CanEditModelBookmarks);
        _bindings.Set("model.saveLabel", saved ? "Remove from saved" : "Save model");
        _bindings.Set("model.canSave", ModelPreferenceActionAvailable("models.save") == true);
        _bindings.Set("model.saveReason", _modelPreferenceBusy ? "Wait for the current model action."
            : _savedModelsError ?? (_savedModels?.CanEdit != true ? "Saving models is unavailable in this view."
            : !IsCurrentInspectedModel(_inspectedModel) ? "Refresh discovery to save a current model entry." : "Keep this model in My models for later."));
        _bindings.Set("model.canUse", ModelPreferenceActionAvailable("models.use") == true);
        _bindings.Set("model.useReason", _modelPreferenceBusy ? "Wait for the current model action."
            : _useModel is null ? "Model selection is unavailable in this view."
            : !IsCurrentInspectedModel(_inspectedModel) ? "Refresh discovery to choose a current model entry."
            : !CanUseInspectedModel ? "Choose this model's supported role in the model picker."
            : "Use this exact model for chat through the shared model selection.");
    }

    private static bool TryGetSavedModelValue(object item, string path, out object? value)
    {
        value = null;
        if (item is not SavedModelRow row) return false;
        value = path.ToLowerInvariant() switch
        {
            "key" => (row.Saved.Identity.ArtifactRevision is null ? "unversioned:" : "versioned:") + row.Saved.Identity.StableKey,
            "target" => row,
            "name" => row.Current?.DisplayName ?? row.Current?.Name ?? row.Saved.Name,
            "provider" => row.Saved.Identity.ProviderId,
            "availability" => row.Current is { } current ? AvailabilityLabel(current.InstallState) : "Not in the current catalogue",
            "caninspect" => row.Current is not null,
            "detailslabel" => "Details for " + (row.Current?.DisplayName ?? row.Saved.Name),
            "removelabel" => "Remove " + row.Saved.Name + " from saved models",
            _ => null
        };
        return value is not null;
    }
}
