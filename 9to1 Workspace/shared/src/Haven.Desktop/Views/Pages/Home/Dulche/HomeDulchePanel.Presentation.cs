using System.ComponentModel;
using System.Globalization;
using System.Text.Json.Nodes;
using Avalonia.Controls;
using CakeOS.Cui;
using CakeOS.Cui.Runtime;

namespace Haven.Desktop.Views.Pages.Home;

internal sealed partial class HomeDulchePanel : ICuiWritableBindingContext, ICuiRepeatItemBindingContext,
    ICuiActionDispatcher, ICuiActionAvailability, INotifyPropertyChanged
{
    private bool _libraryVisible = true, _detailsVisible, _catalogueVisible, _replacingModels;
    private string? _selectedModelId, _performanceModel;
    private readonly Dictionary<string, string> _modelOutputs = new(StringComparer.Ordinal);
    private Item[] _modelRows = [];
    private event PropertyChangedEventHandler? _presentationChanged;
    event PropertyChangedEventHandler? INotifyPropertyChanged.PropertyChanged
    { add => _presentationChanged += value; remove => _presentationChanged -= value; }
    internal event EventHandler? ModelDetailsRequested;
    internal string? CurrentModelId => Model?.Id;

    private void InitializeModelPresentation()
    {
        _bindings.PropertyChanged += OnModelBindingChanged;
        _bindings.Set("ObservedLoadPolicy", "The service has not reported its loading policy.");
    }
    private void OnModelBindingChanged(object? sender, PropertyChangedEventArgs args) => _presentationChanged?.Invoke(this, args);
    public bool TryGetValue(string path, out object? value) => _bindings.TryGetValue(path, out value);
    public bool TrySetValue(string path, object? value) => false;

    public bool TryGetItemValue(object item, string path, out object? value)
    {
        value = null;
        if (item is not Item model) return false;
        value = path.ToLowerInvariant() switch
        {
            "id" => model.Id, "target" => model, "name" => model.Label,
            "state" => StateLabel(model),
            "route" => string.Join(" · ", new[] { Value(model.Data, "engine"), Value(model.Data, "gpu"), Value(model.Data, "quantization") }.Where(text => text.Length > 0)),
            "context" => ReportedContext(model.Data) is { } context ? $"{context:N0} tokens configured" : "Context not reported",
            "detailslabel" => "Details for " + model.Label,
            "issue" => Value(model.Data, "error") is { Length: > 0 } error ? error : Value(model.Data, "last_error"),
            _ => null
        };
        return value is not null;
    }

    public bool TrySetItemValue(object item, string path, object? value) => false;

    public bool? IsActionAvailable(string command) => command == "dulche.inspect"
        ? _active && !_lifetime.IsCancellationRequested && !_generating && !_operating && !_runStopUnconfirmed : null;

    public ValueTask DispatchAsync(string command, object? parameter, CancellationToken cancellationToken = default)
    {
        if (IsActionAvailable(command) != true) return ValueTask.CompletedTask;
        return new(_run(_ =>
        {
            if (!_active || _lifetime.IsCancellationRequested || _generating || _operating || _runStopUnconfirmed) return Task.CompletedTask;
            if (parameter is not Item row || Select("model").ItemsSource is not Item[] models ||
                !models.Any(model => ReferenceEquals(model, row)))
            { Text("message", "The model list changed. Choose its current Details action."); return Task.CompletedTask; }
            Select("model").SelectedItem = row;
            ShowDetailsView();
            ModelDetailsRequested?.Invoke(this, EventArgs.Empty);
            return Task.CompletedTask;
        }, cancellationToken));
    }

    internal void ShowLibrary()
    {
        _libraryVisible = true; _detailsVisible = _catalogueVisible = false;
        RenderModelPresentation();
    }

    internal void ShowCatalogue()
    {
        _catalogueVisible = true; _libraryVisible = _detailsVisible = false;
        RenderModelPresentation();
    }

    internal bool TryShowModel(string modelId)
    {
        if (_generating || _operating || _runStopUnconfirmed || Select("model").ItemsSource is not Item[] models) return false;
        var matches = models.Where(model => model.Id == modelId).ToArray();
        if (matches.Length != 1) return false;
        Select("model").SelectedItem = matches[0];
        ShowDetailsView();
        return true;
    }

    private void ShowDetailsView()
    {
        _detailsVisible = true; _libraryVisible = _catalogueVisible = false;
        RenderModelPresentation();
    }

    private static Item[] ReadConfiguredModels(JsonNode? models) => Rows(models).OfType<JsonObject>()
        .Where(model => !string.IsNullOrWhiteSpace(Value(model, "id")))
        .GroupBy(model => Value(model, "id"), StringComparer.Ordinal)
        .Where(group => group.Count() == 1)
        .Select(group => group.Single())
        .Select(model => new Item(Value(model, "id"), Value(model, "name") is { Length: > 0 } name ? name : Value(model, "id"), model)).ToArray();

    private void RefreshModelRows()
    {
        var query = Input("library-search").Text?.Trim() ?? string.Empty;
        _modelRows = (Select("model").ItemsSource as Item[] ?? []).Where(model =>
            query.Length == 0 || string.Join(' ', model.Label, model.Id, Value(model.Data, "engine"),
                Value(model.Data, "gpu"), Value(model.Data, "state")).Contains(query, StringComparison.OrdinalIgnoreCase)).ToArray();
        _bindings.Set("LocalModels", _modelRows);
        _bindings.Set("LocalModelCount", $"{_modelRows.Length} configured model{(_modelRows.Length == 1 ? "" : "s")}");
        _bindings.Set("LocalModelsEmpty", _modelRows.Length == 0);
    }

    private void HandleModelSelection()
    {
        if (_replacingModels) return;
        var selectedId = Model?.Id;
        if (selectedId is not null) { _retainedModelChoice = selectedId; _retainedModelName = Model!.Label; }
        if (_selectedModelId != selectedId)
        {
            ChangeProfileDraftModel(selectedId);
            _selectedModelId = selectedId;
            _reads++;
            _loadedProfiles = null;
            _performanceModel = null;
            ClearAdapterObservation();
            Text("output", (selectedId ?? _retainedModelChoice) is { } outputOwner ? _modelOutputs.GetValueOrDefault(outputOwner, "") : "");
            Text("message", "");
            Text("metrics", "No recorded timing observation has been read for this model.");
            Text("benchmark-results", "");
            RediscoverInferenceOperation();
        }
        Render();
    }

    private static string StateLabel(Item? model)
    {
        var state = ModelState(model);
        if (model?.Data["serving_supported"] is JsonValue flag && flag.TryGetValue<bool>(out var supported) && !supported)
            return "Installed · serving unavailable";
        return state switch
        {
            "loaded" or "ready" or "running" or "active" => "Loaded",
            "failed" when CanRequestInference(model) => "Stopped after an error · Send retries startup",
            "installed" or "unloaded" or "stopped" or "idle" when CanRequestInference(model) => "Starts when you send",
            "loading" or "resuming" => "Starting model…", "recovering" => "Recovering after an engine failure…",
            "suspended" or "manual_suspended" or "manually_suspended" or "suspended_manual" => "Paused · Resume to use this model",
            "waiting_for_resources" or "resource_suspended" or "suspended_resource" => "Waiting for resources",
            "failed" => "Stopped after an error", "" => "State not reported", _ => state.Replace('_', ' ')
        };
    }

    private void RenderModelPresentation()
    {
        _bindings.Set("LibraryVisible", _libraryVisible);
        _bindings.Set("DetailsVisible", _detailsVisible);
        _bindings.Set("CatalogueVisible", _catalogueVisible);
        _bindings.Set("SelectedModelIdentity", Model?.Id ?? (_retainedModelChoice is { } missing ? missing + " (unavailable)" : "No model selected"));
        _bindings.Set("ChatSelected", _modelSection == "chat");
        _bindings.Set("PerformanceSelected", _modelSection == "performance");
        _bindings.Set("AdaptersSelected", _modelSection == "adapters");
        _bindings.Set("AdvancedSelected", _modelSection == "advanced");
        var data = Model?.Data;
        Text("route", data is null ? "Choose a configured model." :
            string.Join(" · ", new[] { Value(data, "engine") is { Length: > 0 } engine ? engine : "Engine not reported",
                Value(data, "gpu") is { Length: > 0 } device ? device : "Device not reported",
                Value(data, "quantization"), Value(data, "format") }.Where(text => text.Length > 0)));
        var selectedProfile = Value(data, "profile_id");
        var activeProfile = Value(data, "active_profile_id");
        string ProfileName(string id) => (Select("profiles").ItemsSource as Item[])?.FirstOrDefault(profile => profile.Id == id)?.Label
            ?? (id == "current" ? "Current profile" : id.Length == 0 ? "Not reported" : "Saved profile");
        Text("profile-status", $"Selected: {ProfileName(selectedProfile)} · Active: {ProfileName(activeProfile)}" +
            (ReportedContext(data) is { } context ? $"\nConfigured context: {context:N0} tokens. Full-context capacity requires a separate measured run." : "\nConfigured context not reported."));
        Text("recovery", DescribeRecovery(data));
        var profileAvailable = _connected && !_operating && !_generating && !_runStopUnconfirmed && !HasActiveInferenceOperation && Model is not null && _loadedProfiles == Model.Id;
        foreach (var action in new[] { "profile-select", "profile-save", "profile-rename" })
            State(_controls[action], "IsEnabled", profileAvailable && Select("profiles").SelectedItem is Item);
        foreach (var control in new[] { "profiles", "profile-settings", "profile-device", "profile-context", "profile-name" })
            State(_controls[control], "IsEnabled", profileAvailable);
        State(_controls["profile-rename"], "IsEnabled", profileAvailable && Select("profiles").SelectedItem is Item selectedProfileRow &&
            !(selectedProfileRow.Data["protected"] is JsonValue protectedValue && protectedValue.TryGetValue<bool>(out var protectedProfile) && protectedProfile));
        var servingSupported = !(data?["serving_supported"] is JsonValue serving && serving.TryGetValue<bool>(out var canServe) && !canServe);
        State(_controls["toggle"], "IsEnabled", _connected && !_operating && !_generating && !_runStopUnconfirmed && !HasActiveInferenceOperation && servingSupported && Toggle(ModelState(Model)).Action is not null);
        State(_controls["unload"], "IsEnabled", _connected && !_operating && !_generating && !_runStopUnconfirmed && !HasActiveInferenceOperation && CanGenerate(ModelState(Model)));
        State(_controls["recover"], "IsEnabled", !_generating && !_operating && !_runStopUnconfirmed);
        foreach (var control in new[] { "load-policy", "load-policy-apply" })
            State(_controls[control], "IsEnabled", _connected && !_operating && !_generating && !_runStopUnconfirmed && !HasActiveInferenceOperation);
    }

    private static string DescribeDevice(JsonObject gpu)
    {
        var name = Value(gpu, "name") is { Length: > 0 } reportedName ? reportedName : "GPU";
        double? used = Number(gpu["used_mib"]), total = Number(gpu["total_mib"]), temperature = Number(gpu["temperature_c"]);
        return name + " · " + (used is not null && total is not null ? $"{used:N0} / {total:N0} MiB" : "Memory not reported") +
            " · " + (temperature is not null ? $"{temperature:0.#} °C" : "Temperature not reported");
    }

    private static string DescribeRecovery(JsonNode? model)
    {
        if (model?["recovery"] is not JsonObject recovery) return "The installed service has not reported its recovery state.";
        if (model["manual_suspended"] is JsonValue manual && manual.TryGetValue<bool>(out var manuallyPaused) && manuallyPaused)
            return "Manually paused. Automatic recovery will not resume this model until you choose Resume.";
        if (model["pressure_suspended"] is JsonValue pressure && pressure.TryGetValue<bool>(out var resourcePaused) && resourcePaused)
            return "Paused for resource pressure. Resource and thermal guards remain in control.";
        var attempts = Number(recovery["attempts"]); var limit = Number(recovery["limit"]);
        var count = attempts is not null && limit is not null ? $"{attempts:N0} of {limit:N0} attempts in the current recovery window." : "Recovery attempt count not reported.";
        var exhausted = recovery["exhausted"] is JsonValue exhaustedFlag && exhaustedFlag.TryGetValue<bool>(out var exhaustedValue) && exhaustedValue;
        var pending = recovery["pending"] is JsonValue pendingFlag && pendingFlag.TryGetValue<bool>(out var pendingValue) && pendingValue;
        return (exhausted ? "Automatic recovery paused after repeated failures. Inspect the error, then explicitly retry Load. " :
            pending ? "Automatic recovery is pending. " : "No recovery is pending. ") + count +
            (Value(recovery, "last_error") is { Length: > 0 } error ? "\nLast engine error: " + error : "");
    }

    private static double? Number(JsonNode? value) => double.TryParse(value?.ToString(), NumberStyles.Float,
        CultureInfo.InvariantCulture, out var number) && double.IsFinite(number) && number >= 0 ? number : null;

    private static string AdapterName(JsonNode row) => Path.GetFileName(Value(row, "path")) is { Length: > 0 } name ? name : "Adapter";
    private string AdapterScope(JsonNode row)
    {
        if (row["detachable"] is JsonValue fixedFlag && fixedFlag.TryGetValue<bool>(out var detachable) && !detachable)
            return "Included in model profile";
        var specialistId = Value(row, "specialist_id");
        if (specialistId.Length == 0) return "Model";
        return Guid.TryParse(specialistId, out var id) && Select("adapter-specialist").ItemsSource is SpecialistChoice[] specialists &&
            specialists.SingleOrDefault(item => item.Id == id) is { } specialist ? "Specialist: " + specialist.Name : "Specialist binding";
    }

    private void RenderAdapterIdentity()
    {
        if (Select("adapters").SelectedItem is not Item selected)
        { Text("adapter-identity", "Select an attached adapter to inspect its scope and base model."); return; }
        var baseModel = Value(selected.Data, "base_model");
        Text("adapter-identity", AdapterName(selected.Data) + "\nScope: " + AdapterScope(selected.Data) +
            "\nBase model: " + (baseModel.Length > 0 ? baseModel : Model?.Label ?? "Not reported") +
            "\nScale: " + Value(selected.Data, "scale"));
    }

    private async Task RefreshPerformanceAsync(string modelId, CancellationToken token)
    {
        if (_performanceModel == modelId) return;
        var epoch = Epoch;
        var results = await _client.GetAsync("benchmarks", token);
        if (!_active || epoch != Epoch || Model?.Id != modelId) return;
        var rows = Rows(results["items"]).OfType<JsonObject>().Where(row => Value(row, "model") == modelId)
            .OrderByDescending(row => Number(row["started_epoch"]) ?? 0).ToArray();
        PublishBenchmarkRows(rows);
        _performanceModel = modelId;
    }

    private void PublishBenchmarkRows(IReadOnlyList<JsonObject> rows)
    {
        Text("benchmark-results", rows.Count == 0 ? "No recorded benchmarks for this model." : string.Join("\n", rows.Take(8).Select(row =>
            $"{Value(row, "state")} · {Value(row, "passed")}/{Value(row, "issued")} verified · " +
            (Number(row["accepted_output_tps"]) is { } rate ? $"{rate:0.##} accepted tokens/s" : "Accepted throughput not reported"))));
        var latest = rows.FirstOrDefault();
        var ticket = Rows(latest?["tickets"]).OfType<JsonObject>().LastOrDefault();
        var timings = ticket?["server_timings"] as JsonObject;
        var prefill = Number(timings?["prompt_per_second"]);
        var generation = Number(timings?["predicted_per_second"]);
        var firstToken = Number(ticket?["first_token_s"]);
        Text("metrics", latest is null ? "No recorded timing observation for this model." :
            "Most recent recorded benchmark · " + Value(latest, "state") + "\n" +
            "Prefill: " + (prefill is { } p ? $"{p:0.##} tokens/s" : "Not reported") + " · " +
            "Generation: " + (generation is { } g ? $"{g:0.##} tokens/s" : "Not reported") + " · " +
            "First token: " + (firstToken is { } t ? $"{t:0.###} s" : "Not reported") +
            "\nThese timings describe the last recorded ticket, not a full-context capacity check.");
    }

    private void RenderOptimisationComparison(JsonNode? operation)
    {
        if (operation?["result"] is not JsonObject result)
        { Text("optimisation-comparison", "A measured comparison will appear when the service reports it."); return; }
        var baseline = Number((result["base"] as JsonObject)?["decode_tok_s"]);
        var candidate = Number((result["chosen"] as JsonObject)?["decode_tok_s"]);
        double? gain = double.TryParse(result["gain_pct"]?.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture,
            out var reportedGain) && double.IsFinite(reportedGain) ? reportedGain : null;
        var measuredModel = _inferenceOperationModel ?? Value(operation, "model");
        Text("optimisation-comparison", "Measured generation comparison" +
            (measuredModel.Length > 0 && measuredModel != Model?.Id ? " for another model" : "") + "\n" +
            "Baseline: " + (baseline is { } b ? $"{b:0.##} tokens/s" : "Not reported") + " · " +
            "Chosen settings: " + (candidate is { } c ? $"{c:0.##} tokens/s" : "Not reported") +
            (gain is { } value ? $" · Reported gain: {value:0.##}%" : "") +
            (Value(result, "message") is { Length: > 0 } message ? "\n" + message : ""));
    }
}
