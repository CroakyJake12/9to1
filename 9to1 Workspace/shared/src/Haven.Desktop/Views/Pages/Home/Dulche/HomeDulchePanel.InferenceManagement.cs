using System.Globalization;
using System.Text.Json.Nodes;

namespace Haven.Desktop.Views.Pages.Home;

internal sealed partial class HomeDulchePanel
{
    private JsonNode? _inferenceCapabilities;
    private DateTimeOffset _capabilitiesObservedAt;
    private bool _inferenceCapabilitiesAvailable;
    private string? _inferenceOperationId, _inferenceOperationModel;
    private JsonNode? _inferenceOperation;
    private string? _adapterObservationModel;
    private bool HasActiveInferenceOperation => Value(_inferenceOperation, "state") is "queued" or "running" or "validating" or "applying" or "restoring";

    private JsonNode? InferenceCapability(string capability)
    {
        var model = Rows(_inferenceCapabilities?["models"]).FirstOrDefault(row => Value(row, "id") == Model?.Id);
        return model?[capability] ?? _inferenceCapabilities?[capability];
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
        var available = _connected && Model is not null && !_operating && !_generating && !HasActiveInferenceOperation;
        State(_controls["optimise"], "IsEnabled", available && SupportsInferenceManagement("optimise") && ReportedContext(Model?.Data) is not null);
        State(_controls["optimise-restore"], "IsEnabled", available && SupportsInferenceManagement("optimise") &&
            _inferenceOperationModel == Model?.Id && _inferenceOperation?["rollback_available"] is JsonValue rollback &&
            rollback.TryGetValue<bool>(out var canRestore) && canRestore);
        State(_controls["adapter-attach"], "IsEnabled", available && SupportsInferenceManagement("adapters"));
        State(_controls["adapter-detach"], "IsEnabled", available && SupportsInferenceManagement("adapters") &&
            _adapterObservationModel == Model?.Id && Select("adapters").SelectedItem is Item selected && CanDetachAdapter(selected));
        State(_controls["adapter-refresh"], "IsEnabled", _connected && !_operating && Model is not null && _inferenceCapabilitiesAvailable);
        _bindings.Set("SpecialistAdapterScope", Select("adapter-scope").SelectedItem as string == "Specialist");
        var support = new List<string>();
        foreach (var (key, label) in new[] { ("optimise", "Optimisation"), ("adapters", "Adapters") })
        {
            var capability = InferenceCapability(key);
            var reason = Value(capability, "reason");
            support.Add(SupportsInferenceManagement(key) ? label + " available" :
                label + " unavailable" + (reason.Length == 0 ? "" : ": " + reason));
        }
        Text("inference-support", _inferenceCapabilities is null ? "Checking inference controls…" : string.Join(" · ", support));
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
        if (_managing && !_operating && Model is { } model && _inferenceCapabilitiesAvailable)
            await RefreshAdaptersAsync(model.Id, token);
        if (_active) RenderInferenceManagement();
    }

    private void PublishInferenceOperation(JsonNode operation, string? modelId)
    {
        var id = Value(operation, "id");
        if (id.Length == 0) throw new InvalidDataException("Dulche did not return the inference operation identity.");
        if (Value(operation, "state") is not ("queued" or "running" or "validating" or "applying" or "restoring" or "restored" or "completed" or "failed" or "cancelled" or "interrupted"))
            throw new InvalidDataException("Dulche did not return a recognised inference operation state.");
        _inferenceOperationId = id;
        _inferenceOperationModel = modelId;
        _inferenceOperation = operation;
        var parts = new List<string> { (modelId ?? "Inference") + " · " + Value(operation, "state") };
        foreach (var key in new[] { "message", "error" })
            if (Value(operation, key) is { Length: > 0 } detail) parts.Add(detail);
        if (ReportedContext(operation) is { } context) parts.Add($"Context: {context:N0} tokens");
        if (Value(operation, "state") == "completed" && Value(operation, "accepted_output_tps") is { Length: > 0 } speed)
            parts.Add(speed + " accepted tokens/s");
        Text("inference-operation", string.Join("\n", parts));
        if (!HasActiveInferenceOperation) _loadedProfiles = null;
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
                $"{Path.GetFileName(Value(row, "path"))} · {Value(row, "scale")} · {Value(row, "state")}" +
                (Value(row, "specialist_id") is { Length: > 0 } specialist ? " · Specialist " + specialist : " · Model"), row)),
                (Select("adapters").SelectedItem as Item)?.Id);
            Text("adapter-status", bindings.Length == 0 ? "No adapters are attached to this model." :
                string.Join("\n", bindings.Select(row =>
                    $"{Value(row, "binding_id")} · {Value(row, "state")}" +
                    (row["active"] is JsonValue active && active.TryGetValue<bool>(out var isActive) && isActive ? " · Active in inference" : "") +
                    (Value(row, "reason") is { Length: > 0 } reason ? " · " + reason : "") +
                    (Value(row, "message") is { Length: > 0 } message ? " · " + message : ""))));
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

    private async Task ManageInferenceAsync(string command, CancellationToken token)
    {
        var model = Model ?? throw new InvalidOperationException("Select a model first.");
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
                Text("message", "Inference optimisation requested with the current context preserved.");
                return;
            case "optimise-restore":
                if (_inferenceOperationId is null || _inferenceOperationModel != model.Id ||
                    _inferenceOperation?["rollback_available"] is not JsonValue rollback ||
                    !rollback.TryGetValue<bool>(out var canRestore) || !canRestore)
                    throw new InvalidOperationException("No restorable inference profile is available for this model.");
                var restored = await _client.PostAsync("models/" + Esc(model.Id) + "/optimise",
                    new { action = "restore", operation_id = _inferenceOperationId }, token);
                PublishInferenceOperation(restored, model.Id);
                Text("message", "Restore requested; the operation status reports when it finishes.");
                return;
            case "adapter-attach":
                var path = Need("adapter-path");
                if (!Path.IsPathFullyQualified(path)) throw new InvalidOperationException("Enter the adapter's absolute local path.");
                if (!double.TryParse(Input("adapter-scale").Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var scale) || !double.IsFinite(scale))
                    throw new InvalidOperationException("Enter a finite adapter scale, such as 1.");
                var specialist = Select("adapter-scope").SelectedItem as string == "Specialist" ? Need("adapter-specialist") : null;
                await _client.PostAsync("models/" + Esc(model.Id) + "/adapters", new { path, scale, specialist_id = specialist }, token);
                await RefreshAdaptersAsync(model.Id, token);
                Text("message", "Adapter binding saved. Its observed state shows whether it is active.");
                return;
            case "adapter-detach":
                if (_adapterObservationModel != model.Id || Select("adapters").SelectedItem is not Item binding || !CanDetachAdapter(binding))
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
