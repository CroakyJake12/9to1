using System.Globalization;
using System.Text.Json.Nodes;
using Haven.Desktop.Services;

namespace Haven.Desktop.Views.Pages.Home;

internal sealed partial class HomeDulchePanel
{
    private static readonly string[] CatalogueQueryFields = ["search", "filter-architecture", "filter-size", "filter-capability", "filter-quant"];
    private bool _catalogueObserved, _transfersObserved;
    private int _catalogueSelectionUpdateDepth;

    private void UpdateCatalogueSelection(Action update)
    {
        // Changing ItemsSource can synchronously raise SelectionChanged while Avalonia
        // still owns its selection batch. Hiding that same authored control then would
        // detach it and try to change the selection source a second time. Finish the
        // actual source/selection update before publishing catalogue visibility.
        _catalogueSelectionUpdateDepth++;
        try { update(); }
        finally { _catalogueSelectionUpdateDepth--; }
    }

    private void ClearRepositoryFiles()
    {
        _removeConfirmation = null;
        _inspectedRevision = null;
        UpdateCatalogueSelection(() =>
        {
            Select("files").ItemsSource = Array.Empty<Item>();
            Select("files").SelectedItem = null;
        });
    }

    private void InvalidateCatalogueQuery()
    {
        // Editing a query prepares another read. Its previous repository/file choices
        // cannot become targets under the newly displayed filters.
        _catalogueObserved = false;
        UpdateCatalogueSelection(() =>
        {
            ClearRepositoryFiles();
            Select("results").ItemsSource = Array.Empty<Item>();
            Select("results").SelectedItem = null;
        });
        ShowDetails();
        RenderCataloguePresentation();
    }

    private bool IsCurrentCatalogueQuery(CommandIntent intent) => CatalogueQueryFields.All(field =>
        string.Equals(intent.Text(field), Input(field).Text, StringComparison.Ordinal));

    private Item? CurrentCatalogueSelection(string key, bool allowEmptyId = false) =>
        Select(key).SelectedItem is Item item && (allowEmptyId || !string.IsNullOrWhiteSpace(item.Id)) &&
        Select(key).ItemsSource is Item[] items && items.Any(current => ReferenceEquals(current, item)) ? item : null;

    private void RenderCataloguePresentation()
    {
        if (_catalogueSelectionUpdateDepth != 0) return;
        var available = _connected && !_operating && !_generating && !_runStopUnconfirmed;
        var result = _catalogueObserved ? CurrentCatalogueSelection("results") : null;
        var file = CurrentCatalogueSelection("files", allowEmptyId: true);
        var job = _transfersObserved ? CurrentCatalogueSelection("jobs") : null;
        var resultCount = (Select("results").ItemsSource as Item[])?.Length ?? 0;
        var fileCount = (Select("files").ItemsSource as Item[])?.Length ?? 0;
        var jobCount = (Select("jobs").ItemsSource as Item[])?.Length ?? 0;
        var ownership = Value(result?.Data, "ownership");
        var transferState = Value(job?.Data, "state");
        foreach (var command in new[] { "find", "installed" }) State(_controls[command], "IsEnabled", available);
        State(_controls["inspect"], "IsEnabled", available && result is not null);
        State(_controls["download"], "IsEnabled", available && _projects && result is not null && file is not null &&
            !string.IsNullOrWhiteSpace(_inspectedRevision));
        State(_controls["remove"], "IsEnabled", available && !_projects && result is not null && ownership is ("managed" or "external"));
        State(_controls["import"], "IsEnabled", available && Path.IsPathFullyQualified(Input("import-path").Text?.Trim() ?? ""));
        State(_controls["pause"], "IsEnabled", available && job is not null && transferState is ("queued" or "running"));
        State(_controls["resume"], "IsEnabled", available && job is not null && transferState is ("paused" or "paused_storage" or "failed"));
        State(_controls["cancel"], "IsEnabled", available && job is not null && transferState is ("queued" or "running" or "paused" or "paused_storage" or "failed"));
        State(_controls["results"], "IsEnabled", available && _catalogueObserved && resultCount > 0);
        State(_controls["files"], "IsEnabled", available && _projects && result is not null && fileCount > 0);
        State(_controls["jobs"], "IsEnabled", available && _transfersObserved && jobCount > 0);
        _bindings.Set("CatalogueHasResults", _catalogueObserved && resultCount > 0);
        _bindings.Set("CatalogueHasFiles", _projects && result is not null && fileCount > 0);
        _bindings.Set("CatalogueHasTransfers", _transfersObserved && jobCount > 0);
        _bindings.Set("CatalogueResultsStatus", !_catalogueObserved ? "Search the catalogue or choose Installed files to read available records." :
            resultCount == 0 ? (_projects ? "No catalogue results match these filters." : "No installed model files were reported.") :
            $"{resultCount} {(_projects ? "catalogue result" : "installed file")}{(resultCount == 1 ? "" : "s")} · Choose a result to inspect it.");
        _bindings.Set("CatalogueFilesStatus", !_projects || result is null ? "Choose a catalogue result, then open Details and files to read its weights." :
            _inspectedRevision is null ? "Open Details and files to read this repository's available weights." :
            string.IsNullOrWhiteSpace(_inspectedRevision) ? "The repository did not report a revision. Download is unavailable." :
            fileCount == 0 ? "This repository has no supported GGUF or SafeTensors weights." :
            file is null ? $"{fileCount} file option{(fileCount == 1 ? "" : "s")} · Choose the weights to download." :
            "Download uses the inspected version of this repository.");
        var removal = _projects || result is null ? "" : ownership switch
        {
            "managed" => "Uninstall removes this catalogue entry and its managed files.",
            "external" => "Uninstall removes the catalogue entry; external files stay on this device.",
            _ => "Removal is unavailable because file ownership has not been reported."
        };
        _bindings.Set("CatalogueRemovalStatus", removal);
        _bindings.Set("CatalogueHasRemovalStatus", removal.Length > 0);
        _bindings.Set("CatalogueTransfersStatus", !_transfersObserved ? "Transfer status has not been read." :
            jobCount == 0 ? "No model transfers were reported." : job is null ? $"{jobCount} transfer{(jobCount == 1 ? "" : "s")} · Choose a transfer to manage it." :
            $"Selected transfer: {transferState.Replace('_', ' ')}" + (Value(job.Data, "error") is { Length: > 0 } error ? " · " + error : ""));
    }

    private async Task ManageAsync(CommandIntent intent, CancellationToken token)
    {
        var command = intent.Command;
        var model = intent.Model;
        var repo = intent.Selected("results")?.Id;
        var file = intent.Selected("files")?.Id;
        var job = intent.Selected("jobs")?.Id;
        var agent = intent.Selected("agents")?.Id;
        var profile = intent.Selected("profiles")?.Id;
        if ((command is "profile-select" or "profile-rename" or "profile-save") &&
            (model is null || intent.LoadedProfiles != model.Id || _loadedProfiles != model.Id || profile is null || _generating || HasActiveInferenceOperation))
            throw new InvalidOperationException("Read the current model's profiles before applying a profile change.");
        string modelPath() => $"models/{Esc(model?.Id)}";
        switch (command)
        {
            case "optimise":
            case "optimise-restore":
            case "adapter-attach":
            case "adapter-detach":
            case "adapter-refresh":
                await ManageInferenceAsync(intent, token);
                return;
            case "find":
            case "installed":
                var remote = command == "find";
                var result = await _client.GetAsync(remote ? BuildSearchRoute(intent) : "catalog/installed", token);
                if (remote && !IsCurrentCatalogueQuery(intent)) return;
                _projects = remote;
                _catalogueObserved = result["items"] is JsonArray;
                ClearRepositoryFiles();
                Replace("results", Rows(result["items"]).Select(x => new Item(
                    remote ? Value(x, "repo") : Value(x, "id"),
                    remote ? Value(x, "name") : $"{Path.GetFileName(Value(x, "path"))} · {Value(x, "ownership")}", x)), null);
                ShowDetails();
                return;
            case "inspect":
                if (!intent.Projects)
                {
                    ShowDetails();
                    return;
                }
                var detail = await _client.GetAsync("catalog/detail?repo=" + Esc(repo), token);
                if ((Select("results").SelectedItem as Item)?.Id != repo) return;
                ClearRepositoryFiles();
                _inspectedRevision = Value(detail,"revision");
                var files = Rows(detail["files"]).Where(x => Value(x,"path").EndsWith(".gguf",StringComparison.OrdinalIgnoreCase))
                    .Select(x => new Item(Value(x,"path"), Value(x,"path"), x)).ToList();
                if (Rows(detail["files"]).Any(x => Value(x,"path").EndsWith(".safetensors",StringComparison.OrdinalIgnoreCase)))
                    files.Insert(0,new Item("", "Complete SafeTensors directory", detail));
                Replace("files",files,null);
                Text("catalog-detail", Describe(detail));
                return;
            case "download":
                if (!intent.Projects || file is null || string.IsNullOrWhiteSpace(intent.InspectedRevision))
                    throw new InvalidOperationException("Inspect a repository and select its weights.");
                await _client.PostAsync("catalog/download", new
                {
                    repo, filename = file.Length == 0 ? null : file, revision = intent.InspectedRevision,
                    base_model = intent.Text("import-base")
                }, token);
                break;
            case "import":
                await _client.PostAsync("catalog/import", new
                {
                    path = intent.Need("import-path"),
                    base_model = intent.Text("import-base")
                }, token);
                break;
            case "remove":
                if (intent.Projects || repo is null)
                    throw new InvalidOperationException("Select an installed file.");
                if (_removeConfirmation != repo)
                {
                    _removeConfirmation = repo;
                    Text("message", $"Select Uninstall again to confirm removal of {repo}.");
                    return;
                }
                _removeConfirmation = null;
                await _client.PostAsync("catalog/uninstall", new
                {
                    id = repo
                }, token);
                await ManageAsync(intent with { Command = "installed" }, token);
                break;
            case "pause":
            case "cancel":
            case "resume":
                await _client.PostAsync($"catalog/jobs/{Esc(job)}/{command}", new { }, token);
                break;
            case "profile-select":
            case "profile-rename":
                await _client.PostAsync($"{modelPath()}/profiles/{Esc(profile)}/{(command == "profile-select" ? "select" : "rename")}",
                    command == "profile-select" ? new JsonObject() : new JsonObject { ["name"] = intent.Need("profile-name") }, token);
                _loadedProfiles = null;
                break;
            case "profile-save":
                var props = JsonNode.Parse(intent.Text("profile-settings") is { Length: > 0 } settings ? settings : "{}") as JsonObject
                    ?? throw new InvalidOperationException("Profile settings must be a JSON object.");
                if (!string.IsNullOrWhiteSpace(intent.Text("profile-device")))
                    props["gpu"] = intent.Text("profile-device")!.Trim();
                if (!string.IsNullOrWhiteSpace(intent.Text("profile-context")))
                {
                    if (!int.TryParse(intent.Text("profile-context"), out var context) || context < 1)
                        throw new InvalidOperationException("Enter a positive context length.");
                    props.Remove("context_length");
                    props["context"] = context;
                }
                await _client.PostAsync($"{modelPath()}/profiles", new
                {
                    name = intent.Need("profile-name"),
                    settings = props,
                    source_id = profile
                }, token);
                _loadedProfiles = null;
                break;
            case "benchmark-start":
                if (model is null || !CanGenerate(ModelState(model)) || _generating || HasActiveInferenceOperation)
                    throw new InvalidOperationException("Load this model and finish its active response before starting a benchmark.");
                var benchmark = await _client.PostAsync("benchmarks", new
                {
                    model = model?.Id,
                    repeats = 1,
                    max_tokens = 1536
                }, token);
                Text("benchmark-results", "Local benchmark queued · " + Value(benchmark, "id"));
                return;
            case "candidates":
                var screen = await _client.GetAsync("benchmarks/candidates", token);
                _bindings.Set("CandidatesVisible", true);
                Replace("candidate", Rows(screen["items"]).Select(row => new Item(Value(row, "id"), Value(row, "name"), row)), null);
                ShowCandidate();
                return;
            case "training":
                var training = await _client.GetAsync("training/status", token);
                Text("training-status", TrainingSummary(training));
                return;
            case "load-policy-apply":
                if (_generating || HasActiveInferenceOperation)
                    throw new InvalidOperationException("Finish the active inference operation before changing the loading policy.");
                var index = intent.LoadPolicyIndex;
                if (index < 0 || index >= DulcheLocalClient.Policies.Length)
                    throw new InvalidOperationException("Choose a loading policy.");
                await _client.SelectPolicyAsync(DulcheLocalClient.Policies[index], token);
                Text("message", "Loading policy saved: " + DulcheLocalClient.PolicyNames[index] + ".");
                return;
            case "benchmarks":
                _bindings.Set("CandidatesVisible", false);
                var benchmarkRows = Rows((await _client.GetAsync("benchmarks", token))["items"])
                    .OfType<JsonObject>().Where(row => Value(row, "model") == model?.Id)
                    .OrderByDescending(row => Number(row["started_epoch"]) ?? 0).ToArray();
                PublishBenchmarkRows(benchmarkRows);
                return;
            case "agent-resume":
                await _client.PostAsync($"agents/{Esc(agent)}/resume", new { }, token);
                break;
            case "allow":
                await _client.PostAsync("agents/allow-new", new { }, token);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(command));
        }
        Text("message", "Dulche accepted the operation.");
    }

    private void ShowCandidate()
    {
        if (Select("candidate").SelectedItem is not Item selected)
            return;
        var row = selected.Data;
        var entries = new List<string> { $"{selected.Label} · {Value(row,"generation")} · {Value(row,"architecture")}",
            Value(row,"execution_note") };
        foreach (var reference in Rows(row["quality_evidence"]).Take(3))
            entries.Add($"Published · {Value(reference, "origin")} · {Value(reference, "benchmark")} · {Value(reference, "candidate_score")}");
        var local = Rows(row["local_measurements"]).OrderByDescending(result => Value(result, "run_utc")).ToArray();
        entries.Add(local.Length == 0 ? "No local measurement for this checkpoint." : $"Local measurements: {local.Length}.");
        foreach (var result in local.Take(4))
            entries.Add($"Local · {Value(result, "cell")} · {Value(result, "verified")}/{Value(result, "offered")} verified · {Value(result, "aggregate_generated_tps")} generated tokens/s");
        Text("benchmark-results", string.Join("\n", entries.Where(v => v.Length > 0)));
    }

    private static string TrainingSummary(JsonNode observation)
    {
        var rows = Rows(observation["jobs"] ?? observation["items"]).ToArray();
        var lines = new List<string>();
        foreach (var name in new[] { "status", "state", "message", "note", "reason" })
            if (Value(observation, name) is { Length: > 0 } value)
                lines.Add(value);
        foreach (var job in rows)
            lines.Add(string.Join(" · ", new[] { "id", "state", "model", "step", "checkpoint", "last_progress", "error" }
                .Select(key => Value(job, key)).Where(value => value.Length > 0)));
        if (lines.Count == 0)
            lines.Add(observation.ToJsonString());
        return string.Join("\n", lines);
    }

    private static string BuildSearchRoute(CommandIntent intent)
    {
        var fields = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["q"] = intent.Text("search"),
            ["architecture"] = intent.Text("filter-architecture"),
            ["capability"] = intent.Text("filter-capability"),
            ["quantization"] = intent.Text("filter-quant")
        };
        if (intent.Text("filter-size") is { Length: > 0 } max)
        {
            if (!decimal.TryParse(max, NumberStyles.Number, CultureInfo.CurrentCulture, out var billions) || billions <= 0 || billions > 500)
                throw new InvalidOperationException("Enter a model size greater than 0 and at most 500 billion parameters.");
            fields["max_parameters"] = decimal.Truncate(billions * 1_000_000_000).ToString(CultureInfo.InvariantCulture);
        }
        return "catalog/search?" + string.Join("&", fields.Where(pair => !string.IsNullOrWhiteSpace(pair.Value))
            .Select(pair => pair.Key + "=" + Uri.EscapeDataString(pair.Value!.Trim())));
    }

    private static string Describe(JsonNode? model)
    {
        if (model is null)
            return "";
        (string Key, string Label)[] keys =
        [
            ("id","Model"), ("developer","Developer"), ("created_at","Created"), ("generation","Generation"),
            ("parameters","Parameters"), ("active_parameters","Active parameters"), ("architecture","Architecture"),
            ("quantization","Quantisation"), ("format","Format"), ("context_length","Context"),
            ("capability","Capability"), ("backend","Backend"), ("compatibility","Compatibility"),
            ("license","Licence"), ("bytes","Installed bytes"), ("estimated_weight_bytes","Estimated weight bytes"),
            ("estimated_ram_bytes","Estimated RAM bytes"), ("estimated_vram_bytes","Estimated VRAM bytes"),
            ("measured_tokens_per_second","Measured tokens/s"), ("ownership","Ownership"), ("state","State"), ("error","Error")
        ];
        return string.Join(" · ", keys.Where(field => model[field.Key] is not null).Select(field => field.Label + ": " + Value(model, field.Key)));
    }
    private void ShowDetails() => Text("catalog-detail", Describe((Select("results").SelectedItem as Item)?.Data));
    private async Task RefreshManagementAsync(CancellationToken token)
    {
        if (_catalogueVisible)
        {
            var jobs = await _client.GetAsync("catalog/jobs", token);
            if (!_active || !_catalogueVisible) return;
            _transfersObserved = jobs["items"] is JsonArray;
            Replace("jobs", Rows(jobs["items"]).OfType<JsonObject>().Select(x => new Item(Value(x, "id"), $"{Value(x, "name")} {Value(x, "state")} · {Value(x, "done_bytes")}/{Value(x, "total_bytes")} bytes", x)), (Select("jobs").SelectedItem as Item)?.Id);
        }
        if (Model is not { } model || !(_managing || _modelSection == "performance")) return;
        var previous = Epoch;
        if (_modelSection == "performance")
        {
            try { await RefreshPerformanceAsync(model.Id, token); }
            catch (Exception error) when (error is IOException or HttpRequestException)
            { Text("metrics", "Recorded measurements are unavailable. " + error.Message); }
        }
        if (previous != Epoch || Model?.Id != model.Id || !_active) return;
        if (_loadedProfiles == model.Id) return;
        var profiles = await _client.GetAsync("models/" + Uri.EscapeDataString(model.Id) + "/profiles", token);
        if (previous != Epoch || !_active)
            return;
        AdmitProfileDraftCatalogue(model.Id);
        Replace("profiles", Rows(profiles["items"]).OfType<JsonObject>().Select(x => new Item(Value(x, "id"), Value(x, "name"), x)),
            Value(model.Data, "profile_id") is { Length: > 0 } profile ? profile : "current");
        ApplySelectedProfileDraft();
        _loadedProfiles = model.Id;
        RenderModelPresentation();
    }
}
