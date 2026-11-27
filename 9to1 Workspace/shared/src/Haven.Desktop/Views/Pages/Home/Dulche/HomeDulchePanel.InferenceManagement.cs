using System.Globalization;
using System.Text.Json.Nodes;

namespace Haven.Desktop.Views.Pages.Home;

internal sealed partial class HomeDulchePanel
{
    private JsonNode? _inferenceCapabilities;
    private DateTimeOffset _capabilitiesObservedAt;
    private bool _inferenceCapabilitiesAvailable;
    private string? _inferenceOperationId, _inferenceOperationModel;
    private string? _rollbackOperationId, _rollbackOperationModel;
    private JsonNode? _inferenceOperation;
    private string? _adapterObservationModel;
    private sealed record SpecialistChoice(Guid Id, string Name)
    {
        public override string ToString() => Name;
    }
    private readonly HashSet<Guid> _knownSpecialists = [];
    private string _specialistStatus = "Choose a saved Specialist.";
    private bool HasActiveInferenceOperation => Value(_inferenceOperation, "state") is "queued" or "running" or "validating" or "applying" or "restoring";

    private JsonNode? InferenceCapability(string capability)
    {
        if (_inferenceCapabilities?["models"] is JsonArray models)
        {
            var matching = models.OfType<JsonObject>().Where(row => Value(row, "id") == Model?.Id).ToArray();
            return matching.Length == 1 ? matching[0][capability] : null;
        }
        // Compatibility with services that expose only a global capability record.
        // Once per-model records are supplied, another model's aggregate is not authority.
        return _inferenceCapabilities?[capability];
    }

    private bool SupportsInferenceManagement(string capability) =>
        InferenceCapability(capability)?["supported"] is JsonValue value &&
        value.TryGetValue<bool>(out var supported) && supported;

    private static bool CanDetachAdapter(Item binding) =>
        !(binding.Data["detachable"] is JsonValue value && value.TryGetValue<bool>(out var detachable) && !detachable);

    private static int? ReportedContext(JsonNode? model)
    {
        foreach (var field in new[] { "context", "context_length", "context_tokens" })
            if (int.TryParse(Value(model, field), NumberStyles.Integer, CultureInfo.InvariantCulture, out var tokens) && tokens > 0)
                return tokens;
        return null;
    }

    private void ClearAdapterObservation()
    {
        _adapterObservationModel = null;
        Select("adapters").ItemsSource = Array.Empty<Item>();
        Text("adapter-status", "Refresh attached adapters for this model.");
    }

    private void RenderInferenceManagement()
    {
        var available = _connected && Model is not null && !_operating && !_generating && !_runStopUnconfirmed && !HasActiveInferenceOperation;
        State(_controls["optimise"], "IsEnabled", available && SupportsInferenceManagement("optimise") && ReportedContext(Model?.Data) is not null);
        State(_controls["optimise-restore"], "IsEnabled", available && SupportsInferenceManagement("optimise") &&
            _rollbackOperationModel == Model?.Id && _rollbackOperationId is not null);
        var specialistScope = Select("adapter-scope").SelectedItem as string == "Specialist";
        var validSpecialist = Select("adapter-specialist").SelectedItem is SpecialistChoice selectedSpecialist &&
            _knownSpecialists.Contains(selectedSpecialist.Id);
        State(_controls["adapter-attach"], "IsEnabled", available && SupportsInferenceManagement("adapters") && (!specialistScope || validSpecialist));
        State(_controls["adapter-specialist"], "IsEnabled", available && _specialists is not null);
        _bindings.Set("SpecialistAdapterStatus", _specialistStatus);
        State(_controls["adapter-detach"], "IsEnabled", available && SupportsInferenceManagement("adapters") &&
            _adapterObservationModel == Model?.Id && Select("adapters").SelectedItem is Item selected && CanDetachAdapter(selected));
        State(_controls["adapter-refresh"], "IsEnabled", _connected && !_operating && !_generating && !_runStopUnconfirmed && Model is not null && _inferenceCapabilitiesAvailable);
        _bindings.Set("SpecialistAdapterScope", specialistScope);
        var support = new List<string>();
        foreach (var (key, label) in new[] { ("optimise", "Optimisation"), ("adapters", "Adapters") })
        {
            var capability = InferenceCapability(key);
            var reason = Value(capability, "reason");
            support.Add(SupportsInferenceManagement(key) ? label + " available" :
                label + " unavailable" + (reason.Length == 0 ? ": no qualified control was reported for this model." : ": " + reason));
        }
        Text("inference-support", _inferenceCapabilities is null ? "Checking inference controls…" : string.Join(" · ", support));
        _bindings.Set("AdapterInputsAvailable", available && SupportsInferenceManagement("adapters"));
        _bindings.Set("AdapterCapability", SupportsInferenceManagement("adapters")
            ? "Attach a compatible adapter to this model or an enabled Specialist. Compatibility is checked by the local engine."
            : "Additional attachment is unavailable. " + (Value(InferenceCapability("adapters"), "reason") is { Length: > 0 } adapterReason
                ? adapterReason : "No qualified adapter backend was reported for this model."));
        RenderAdapterIdentity();
    }

    private async Task RefreshInferenceManagementAsync(CancellationToken token)
    {
        if (_inferenceCapabilities is null || DateTimeOffset.UtcNow - _capabilitiesObservedAt > TimeSpan.FromSeconds(20))
        {
            try
            {
                var capabilities = await _client.GetAsync("inference/capabilities", token);
                if (!_active) return;
                _inferenceCapabilities = capabilities;
                _inferenceCapabilitiesAvailable = true;
                _capabilitiesObservedAt = DateTimeOffset.UtcNow;
                RediscoverInferenceOperation();
            }
            catch (Exception error) when (error is IOException or HttpRequestException || error is OperationCanceledException && !token.IsCancellationRequested)
            {
                _inferenceCapabilitiesAvailable = false;
                _inferenceCapabilities = new JsonObject
                {
                    ["optimise"] = new JsonObject { ["supported"] = false, ["reason"] = error.Message },
                    ["adapters"] = new JsonObject { ["supported"] = false, ["reason"] = error.Message }
                };
                _capabilitiesObservedAt = DateTimeOffset.UtcNow;
            }
        }
        if (_inferenceOperationId is { } operationId && HasActiveInferenceOperation)
        {
            try
            {
                var observed = await _client.GetAsync("inference/operations/" + Esc(operationId), token);
                if (_active && _inferenceOperationId == operationId)
                {
                    if (Value(observed, "id") != operationId)
                        throw new InvalidDataException("Dulche returned a different inference operation identity.");
                    PublishInferenceOperation(observed, _inferenceOperationModel);
                }
            }
            catch (Exception error) when (error is IOException or HttpRequestException || error is OperationCanceledException && !token.IsCancellationRequested)
            {
                Text("inference-operation", "Operation status unavailable. " + error.Message);
            }
        }
        if (_modelSection == "adapters" && !_operating && Model is { } model && _inferenceCapabilitiesAvailable)
        {
            await RefreshSpecialistsAsync(token);
            await RefreshAdaptersAsync(model.Id, token);
        }
        if (_active) RenderInferenceManagement();
    }

    private async Task RefreshSpecialistsAsync(CancellationToken token)
    {
        if (_specialists is null)
        {
            _knownSpecialists.Clear();
            _specialistStatus = "The Specialist catalogue is unavailable in this view. Model attachments remain available.";
            return;
        }
        try
        {
            var definitions = await _specialists.GetAgentsAsync(token);
            if (!_active) return;
            var choices = definitions.Where(item => item.IsEnabled && item.Id != Guid.Empty)
                .GroupBy(item => item.Id).Select(group => group.First())
                .Select(item => new SpecialistChoice(item.Id, item.Name)).OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase).ToArray();
            var select = Select("adapter-specialist");
            var selected = (select.SelectedItem as SpecialistChoice)?.Id;
            _knownSpecialists.Clear();
            _knownSpecialists.UnionWith(choices.Select(item => item.Id));
            if (select.ItemsSource is not SpecialistChoice[] prior || !prior.SequenceEqual(choices))
            {
                select.ItemsSource = choices;
                select.SelectedItem = choices.FirstOrDefault(item => item.Id == selected);
            }
            _specialistStatus = choices.Length == 0 ? "No enabled Specialists are available. Create or enable a Specialist first." : "Choose the Specialist that should use this adapter.";
        }
        catch (Exception error) when (error is not OperationCanceledException || !token.IsCancellationRequested)
        {
            _knownSpecialists.Clear();
            Select("adapter-specialist").ItemsSource = Array.Empty<SpecialistChoice>();
            _specialistStatus = "Could not read Specialists: " + error.Message;
        }
    }

    private void RediscoverInferenceOperation()
    {
        var modelId = Model?.Id;
        var row = Rows(_inferenceCapabilities?["models"]).FirstOrDefault(item => Value(item, "id") == modelId) as JsonObject;
        var active = _inferenceCapabilities?["active_operation"];
        var observed = active ?? row?["latest_operation"];
        if (observed is not null)
            PublishInferenceOperation(observed, Value(observed, "model") is { Length: > 0 } owner ? owner : modelId);
        else if (row?.ContainsKey("latest_operation") == true || _inferenceOperationModel != modelId)
        {
            _inferenceOperationId = _inferenceOperationModel = null;
            _inferenceOperation = null;
            Text("inference-operation", "");
            RenderOptimisationComparison(null);
        }
        if (row?.ContainsKey("rollback_operation") == true)
        {
            var rollback = row["rollback_operation"];
            var valid = Value(rollback, "model") == modelId && Value(rollback, "id").Length > 0 &&
                rollback?["rollback_available"] is JsonValue flag && flag.TryGetValue<bool>(out var allowed) && allowed;
            _rollbackOperationId = valid ? Value(rollback, "id") : null;
            _rollbackOperationModel = valid ? modelId : null;
        }
        else if (_rollbackOperationModel != modelId)
        {
            _rollbackOperationId = _rollbackOperationModel = null;
        }
    }

    private void PublishInferenceOperation(JsonNode operation, string? modelId)
    {
        var id = Value(operation, "id");
        if (id.Length == 0) throw new InvalidDataException("Dulche did not return the inference operation identity.");
        if (Value(operation, "state") is not ("queued" or "running" or "validating" or "applying" or "restoring" or "restored" or "completed" or "failed" or "cancelled" or "interrupted"))
            throw new InvalidDataException("Dulche did not return a recognised inference operation state.");
        var changedOperationState = _inferenceOperationId != id ||
            Value(_inferenceOperation, "state") != Value(operation, "state");
        _inferenceOperationId = id;
        _inferenceOperationModel = modelId;
        _inferenceOperation = operation;
        if (modelId == Model?.Id && !HasActiveInferenceOperation &&
            operation["rollback_available"] is JsonValue rollback && rollback.TryGetValue<bool>(out var available))
        {
            if (available)
            {
                _rollbackOperationId = id;
                _rollbackOperationModel = modelId;
            }
            else if (_rollbackOperationId == id)
                _rollbackOperationId = _rollbackOperationModel = null;
        }
        var ownerName = (Select("model").ItemsSource as Item[])?.FirstOrDefault(item => item.Id == modelId)?.Label ?? "Inference operation";
        var parts = new List<string> { ownerName + " · " + Value(operation, "state") };
        foreach (var key in new[] { "message", "error" })
            if (Value(operation, key) is { Length: > 0 } detail) parts.Add(detail);
        if (ReportedContext(operation) is { } context) parts.Add($"Context: {context:N0} tokens");
        if (Value(operation, "state") == "completed" && Value(operation, "accepted_output_tps") is { Length: > 0 } speed)
            parts.Add(speed + " accepted tokens/s");
        Text("inference-operation", string.Join("\n", parts));
        RenderOptimisationComparison(operation);
        // Re-observing the same completed operation must not overwrite an edited profile
        // draft every four seconds. Refresh only when its real terminal state changes.
        if (!HasActiveInferenceOperation && changedOperationState && modelId == Model?.Id) _loadedProfiles = null;
        Render();
    }

    private async Task RefreshAdaptersAsync(string modelId, CancellationToken token)
    {
        var epoch = Epoch;
        try
        {
            var observation = await _client.GetAsync("models/" + Esc(modelId) + "/adapters", token);
            if (!_active || epoch != Epoch || Model?.Id != modelId) return;
            if (observation["items"] is not JsonArray)
                throw new InvalidDataException("Dulche did not return the model's adapter bindings.");
            var bindings = Rows(observation["items"]).Where(row => Value(row, "binding_id").Length > 0).ToArray();
            _adapterObservationModel = modelId;
            Replace("adapters", bindings.Select(row => new Item(Value(row, "binding_id"),
                AdapterName(row) + " · " + AdapterScope(row) + " · " + Value(row, "state"), row)),
                (Select("adapters").SelectedItem as Item)?.Id);
            Text("adapter-status", bindings.Length == 0 ? "No adapters are attached to this model." :
                string.Join("\n", bindings.Select(row =>
                    AdapterName(row) + " · " + AdapterScope(row) + " · " + Value(row, "state") +
                    (row["active"] is JsonValue active && active.TryGetValue<bool>(out var isActive) && isActive ? " · Active in inference" : "") +
                    (Value(row, "reason") is { Length: > 0 } reason ? " · " + reason : "") +
                    (Value(row, "message") is { Length: > 0 } message ? " · " + message : ""))));
            RenderAdapterIdentity();
        }
        catch (Exception error) when (error is IOException or HttpRequestException || error is OperationCanceledException && !token.IsCancellationRequested)
        {
            if (_active && epoch == Epoch)
            {
                ClearAdapterObservation();
                Text("adapter-status", "Adapter status unavailable. " + error.Message);
            }
        }
    }

    private async Task ManageInferenceAsync(CommandIntent intent, CancellationToken token)
    {
        var command = intent.Command;
        var model = intent.Model ?? throw new InvalidOperationException("Select a model first.");
        if (_generating) throw new InvalidOperationException("Wait for the current response before changing inference settings.");
        var capability = command.StartsWith("optimise", StringComparison.Ordinal) ? "optimise" : "adapters";
        if (command != "adapter-refresh" && !SupportsInferenceManagement(capability))
            throw new InvalidOperationException("This inference control is unavailable for the selected model. " + Value(InferenceCapability(capability), "reason"));
        if (HasActiveInferenceOperation && command != "adapter-refresh")
            throw new InvalidOperationException("Wait for the current inference operation to finish.");
        switch (command)
        {
            case "optimise":
                var context = ReportedContext(model.Data) ?? throw new InvalidOperationException("Refresh the current model context before optimising.");
                var started = await _client.PostAsync("models/" + Esc(model.Id) + "/optimise",
                    new { action = "start", context_tokens = context, goal = "speed" }, token);
                PublishInferenceOperation(started, model.Id);
                _capabilitiesObservedAt = DateTimeOffset.MinValue;
                Text("message", "Inference optimisation requested with the current context preserved.");
                return;
            case "optimise-restore":
                if (intent.RollbackOperationId is null || intent.RollbackOperationModel != model.Id ||
                    _rollbackOperationId != intent.RollbackOperationId || _rollbackOperationModel != model.Id)
                    throw new InvalidOperationException("No restorable inference profile is available for this model.");
                var restored = await _client.PostAsync("models/" + Esc(model.Id) + "/optimise",
                    new { action = "restore", operation_id = intent.RollbackOperationId }, token);
                PublishInferenceOperation(restored, model.Id);
                _capabilitiesObservedAt = DateTimeOffset.MinValue;
                Text("message", "Restore requested; the operation status reports when it finishes.");
                return;
            case "adapter-attach":
                var path = intent.Need("adapter-path");
                if (!Path.IsPathFullyQualified(path)) throw new InvalidOperationException("Enter the adapter's absolute local path.");
                if (!double.TryParse(intent.Text("adapter-scale"), NumberStyles.Float, CultureInfo.InvariantCulture, out var scale) || !double.IsFinite(scale))
                    throw new InvalidOperationException("Enter a finite adapter scale, such as 1.");
                string? specialist = null;
                if (intent.Choices.GetValueOrDefault("adapter-scope") as string == "Specialist")
                {
                    if (_specialists is null || intent.Choices.GetValueOrDefault("adapter-specialist") is not SpecialistChoice choice || !_knownSpecialists.Contains(choice.Id))
                        throw new InvalidOperationException("Choose an enabled Specialist before attaching this adapter.");
                    var specialistId = choice.Id;
                    var current = await _specialists.GetAgentsAsync(token);
                    if (!current.Any(item => item.Id == specialistId && item.IsEnabled))
                        throw new InvalidOperationException("That Specialist is no longer available. Choose an enabled Specialist and try again.");
                    specialist = specialistId.ToString("D");
                }
                await _client.PostAsync("models/" + Esc(model.Id) + "/adapters", new { path, scale, specialist_id = specialist }, token);
                await RefreshAdaptersAsync(model.Id, token);
                Text("message", "Adapter binding saved. Its observed state shows whether it is active.");
                return;
            case "adapter-detach":
                if (intent.AdapterObservationModel != model.Id || _adapterObservationModel != model.Id ||
                    intent.Selected("adapters") is not Item binding || !CanDetachAdapter(binding))
                    throw new InvalidOperationException("Refresh and select an attached adapter first.");
                await _client.PostAsync("models/" + Esc(model.Id) + "/adapters/detach", new { binding_id = binding.Id }, token);
                await RefreshAdaptersAsync(model.Id, token);
                Text("message", "Adapter detach accepted.");
                return;
            case "adapter-refresh":
                await RefreshAdaptersAsync(model.Id, token);
                return;
            default:
                throw new ArgumentOutOfRangeException(nameof(command));
        }
    }
}
