using System.Globalization;
using System.Text.Json.Nodes;

namespace Haven.Desktop.Views.Pages.Home;

internal sealed partial class HomeDulchePanel
{
    private async Task ManageAsync(string command, CancellationToken token)
    {
        var model = Model;
        var repo = (Select("results").SelectedItem as Item)?.Id;
        var file = (Select("files").SelectedItem as Item)?.Id;
        var job = (Select("jobs").SelectedItem as Item)?.Id;
        var agent = (Select("agents").SelectedItem as Item)?.Id;
        var profile = (Select("profiles").SelectedItem as Item)?.Id;
        string modelPath() => $"models/{Esc(model?.Id)}";
        switch (command)
        {
            case "optimise":
            case "optimise-restore":
            case "adapter-attach":
            case "adapter-detach":
            case "adapter-refresh":
                await ManageInferenceAsync(command, token);
                return;
            case "find":
            case "installed":
                var remote = command == "find";
                var result = await _client.GetAsync(remote ? BuildSearchRoute() : "catalog/installed", token);
                _projects = remote;
                Replace("results", Rows(result["items"]).Select(x => new Item(
                    remote ? Value(x, "repo") : Value(x, "id"),
                    remote ? Value(x, "name") : $"{Path.GetFileName(Value(x, "path"))} · {Value(x, "ownership")}", x)), null);
                ShowDetails();
                return;
            case "inspect":
                if (!_projects)
                {
                    ShowDetails();
                    return;
                }
                var detail = await _client.GetAsync("catalog/detail?repo=" + Esc(repo), token);
                if ((Select("results").SelectedItem as Item)?.Id != repo) return;
                _inspectedRevision = Value(detail,"revision");
                var files = Rows(detail["files"]).Where(x => Value(x,"path").EndsWith(".gguf",StringComparison.OrdinalIgnoreCase))
                    .Select(x => new Item(Value(x,"path"), Value(x,"path"), x)).ToList();
                if (Rows(detail["files"]).Any(x => Value(x,"path").EndsWith(".safetensors",StringComparison.OrdinalIgnoreCase)))
                    files.Insert(0,new Item("", "Complete SafeTensors directory", detail));
                Replace("files",files,null);
                Text("catalog-detail", Describe(detail));
                return;
            case "download":
                if (!_projects || file is null || string.IsNullOrWhiteSpace(_inspectedRevision))
                    throw new InvalidOperationException("Inspect a repository and select its weights.");
                await _client.PostAsync("catalog/download", new
                {
                    repo, filename = file.Length == 0 ? null : file, revision = _inspectedRevision,
                    base_model = Input("import-base").Text
                }, token);
                break;
            case "import":
                await _client.PostAsync("catalog/import", new
                {
                    path = Need("import-path"),
                    base_model = Input("import-base").Text
                }, token);
                break;
            case "remove":
                if (_projects || repo is null)
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
                await RunCoreAsync("installed", token);
                break;
            case "pause":
            case "cancel":
            case "resume":
                await _client.PostAsync($"catalog/jobs/{Esc(job)}/{command}", new { }, token);
                break;
            case "profile-select":
            case "profile-rename":
                await _client.PostAsync($"{modelPath()}/profiles/{Esc(profile)}/{(command == "profile-select" ? "select" : "rename")}",
                    command == "profile-select" ? new JsonObject() : new JsonObject { ["name"] = Need("profile-name") }, token);
                _loadedProfiles = null;
                break;
            case "profile-save":
                var props = JsonNode.Parse(Input("profile-settings").Text is { Length: > 0 } settings ? settings : "{}") as JsonObject
                    ?? throw new InvalidOperationException("Profile settings must be a JSON object.");
                if (!string.IsNullOrWhiteSpace(Input("profile-device").Text))
                    props["gpu"] = Input("profile-device").Text!.Trim();
                if (!string.IsNullOrWhiteSpace(Input("profile-context").Text))
                {
                    if (!int.TryParse(Input("profile-context").Text, out var context) || context < 1)
                        throw new InvalidOperationException("Enter a positive context length.");
                    props.Remove("context_length");
                    props["context"] = context;
                }
                await _client.PostAsync($"{modelPath()}/profiles", new
                {
                    name = Need("profile-name"),
                    settings = props,
                    source_id = profile
                }, token);
                _loadedProfiles = null;
                break;
            case "benchmark-start":
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
                _bindings.Set("CandidatesVisible", false);
                var training = await _client.GetAsync("training/status", token);
                Text("benchmark-results", TrainingSummary(training));
                return;
            case "benchmarks":
                _bindings.Set("CandidatesVisible", false);
                var benchmarkRows = Rows((await _client.GetAsync("benchmarks", token))["items"])
                    .Where(row => Value(row, "model") == model?.Id).ToArray();
                Text("benchmark-results", benchmarkRows.Length == 0 ? "No recorded benchmarks for this model." :
                    string.Join("\n", benchmarkRows.Take(8).Select(row =>
                        $"{Value(row, "profile")} · {Value(row, "state")} · {Value(row, "passed")}/{Value(row, "issued")} verified · {Value(row, "accepted_output_tps")} accepted tokens/s")));
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

    private string BuildSearchRoute()
    {
        var fields = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["q"] = Input("search").Text,
            ["architecture"] = Input("filter-architecture").Text,
            ["capability"] = Input("filter-capability").Text,
            ["quantization"] = Input("filter-quant").Text
        };
        if (Input("filter-size").Text is { Length: > 0 } max)
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
        var jobs = await _client.GetAsync("catalog/jobs", token);
        if (!_active || !_managing)
            return;
        Replace("jobs", Rows(jobs["items"]).Select(x => new Item(Value(x, "id"), $"{Value(x, "name")} {Value(x, "state")} · {Value(x, "done_bytes")}/{Value(x, "total_bytes")} bytes", x)), (Select("jobs").SelectedItem as Item)?.Id);
        if (Model is not { } model || _loadedProfiles == model.Id)
            return;
        var previous = Epoch;
        var profiles = await _client.GetAsync("models/" + Uri.EscapeDataString(model.Id) + "/profiles", token);
        if (previous != Epoch || !_active)
            return;
        Replace("profiles", Rows(profiles["items"]).Select(x => new Item(Value(x, "id"), Value(x, "name"), x)), null);
        _loadedProfiles = model.Id;
    }
}
