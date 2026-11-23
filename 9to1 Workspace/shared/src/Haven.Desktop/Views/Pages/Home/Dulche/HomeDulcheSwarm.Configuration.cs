using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Haven.Desktop.Views.Pages.Home;

internal sealed partial class HomeDulcheSwarm
{
    private void ConfigureDefaults()
    {
        _assigning = true;
        Choices("strategy", [new("resident", "Fully resident"), new("alternating", "Alternating residency"), new("partial", "Partially resident (≤4 GiB)")]);
        Choices("autonomy", [new("draft", "Draft only"), new("approved_recipes", "Approved recipes")]);
        Choices("preset", [new("balanced", "Balanced"), new("frequent", "Frequent"), new("milestones", "Milestones only"), new("custom", "Custom")]);
        foreach (var key in new[] { "milestones", "on-error", "on-conflict" })
            Choices(key, [new("yes", "Yes"), new("no", "No")]);
        Dictionary<string, string> defaults = new()
        {
            ["mentor-context"] = "32768",
            ["worker-context"] = "32768",
            ["role"] = "developer",
            ["seconds"] = "300",
            ["task-count"] = "4",
            ["token-count"] = "8192",
            ["concurrency"] = "2",
            ["task-limit"] = "32",
            ["time-limit"] = "3600",
            ["output"] = "2048",
            ["review-limit"] = "12",
            ["min-ram"] = "1536",
            ["min-disk"] = "60",
            ["max-gpu"] = "0"
        };
        foreach (var field in defaults)
            Input(field.Key).Text = field.Value;
        _assigning = false;
        SetPage(false);
        UpdateBudgets();
    }

    private async Task LoadDraftAsync(JsonObject saved, CancellationToken token)
    {
        foreach (var field in new[] { ("objective","objective"), ("working-directory","working_directory"),
            ("criteria","completion_criteria"), ("concurrency","max_workers") })
            Input(field.Item1).Text = Str(saved, field.Item2);
        Input("references").Text = string.Join(", ", Rows(saved["references"]).Select(r => r.ToString()));
        var limits = saved["limits"];
        foreach (var field in new[] { ("task-limit","max_tasks"), ("time-limit","max_seconds"), ("output","max_output_tokens"),
            ("review-limit","max_check_ins"), ("min-ram","min_free_ram_mib"), ("min-disk","min_free_disk_gib"), ("max-gpu","max_gpu_used_mib") })
            if (limits?[field.Item2] is { } value)
                Input(field.Item1).Text = value.ToString();
        var mentor = saved["mentor"];
        Choose("mentor-model", Str(mentor, "model"));
        await ReadProfilesAsync("mentor", token);
        Choose("mentor-profile", Str(mentor, "profile"));
        Choose("mentor-reasoning", mentor?["reasoning"]?.ToString() ?? "default");
        Input("mentor-context").Text = Str(mentor, "context");
        Choose("strategy", Str(saved, "strategy"));
        Choose("partial-profile", Str(saved, "partial_profile"));
        Choose("preset", "custom");
        if (saved["check_in"] is { } schedule)
            PopulateSchedule(schedule);
        _workers.Clear();
        foreach (var worker in Rows(saved["workers"]))
            _workers.Add(new(Str(worker, "role"), $"{Str(worker, "role")} · {Str(worker, "gpu")}", worker.DeepClone()));
        Choices("roles", _workers);
        UpdateBudgets();
    }

    private int Number(string key, int minimum, int maximum)
    {
        if (!int.TryParse(Input(key).Text, NumberStyles.Integer, CultureInfo.CurrentCulture, out var value) || value < minimum || value > maximum)
            throw new InvalidOperationException($"{FieldLabel(key)} must be between {minimum:N0} and {maximum:N0}.");
        return value;
    }
    private static string FieldLabel(string key) => key switch
    {
        "mentor-context" => "Mentor context",
        "worker-context" => "Worker context",
        "output" => "Output allowance",
        "concurrency" => "Concurrency",
        "min-ram" => "Free RAM",
        "min-disk" => "Free storage",
        "max-gpu" => "GPU usage limit",
        "criteria" => "Completion criteria",
        _ => key.Replace('-', ' ')
    };
    private string Required(string key) => Input(key).Text?.Trim() is { Length: > 0 } text ? text : throw new InvalidOperationException("Enter " + FieldLabel(key) + ".");
    private string[] Terms(string key) => (Input(key).Text ?? "").Split([',', '\n'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
    private bool Yes(string key) => Selected(key)?.Id == "yes";
    private void ApplyPreset()
    {
        if (_assigning || Selected("preset")?.Id is not { } key || key == "custom")
            return;
        var preset = _presets?[key];
        if (preset is not null)
            PopulateSchedule(preset);
    }
    private void PopulateSchedule(JsonNode schedule)
    {
        foreach (var pair in new[] { ("seconds", "seconds"), ("task-count", "tasks"), ("token-count", "tokens") })
            Input(pair.Item1).Text = Str(schedule, pair.Item2);
        foreach (var pair in new[] { ("milestones", "milestones"), ("on-error", "on_error"), ("on-conflict", "on_conflict") })
            Choose(pair.Item1, schedule[pair.Item2]?.GetValue<bool>() == true ? "yes" : "no");
    }
    private void Choose(string key, string id)
    {
        var choice = Select(key).Items.OfType<Choice>().FirstOrDefault(a => a.Id == id);
        if (choice is not null)
            Select(key).SelectedItem = choice;
    }
    private void UpdateBudgets()
    {
        if (_assigning)
            return;
        int.TryParse(Input("output").Text, out var reserved);
        int.TryParse(Input("concurrency").Text, out var concurrent);
        foreach (var who in new[] { "mentor", "worker" })
        {
            var model = Selected(who + "-model");
            if (model is null)
            {
                Label(who + "-budget", "Choose a registered model.");
                continue;
            }
            var limit = ContextLimit(who);
            var available = int.TryParse(Input(who + "-context").Text, out var context) && context >= 16384 && context <= limit;
            var device = Selected(who + "-profile")?.Record?["gpu"]?.ToString() ?? Str(model.Record, "gpu");
            var engine = Selected(who + "-profile")?.Record?["engine"]?.ToString() ?? Str(model.Record, "engine");
            var slots = model.Record?["slots"]?.GetValue<int>() ?? 0;
            Label(who + "-budget", available
                ? $"{engine} · {device} · {Math.Max(0, context - reserved):N0} input tokens including instructions, after reserving {reserved:N0} output · {limit:N0} per-slot ceiling."
                : $"{engine} · {device} · confirmed per-slot limit {limit:N0}. {(limit < 16384 ? "This model cannot provide the 16,384-token minimum." : $"Choose 16,384 to {limit:N0} tokens.")}");
            if (who == "worker")
            {
                var conflict = slots > 0 && concurrent > slots ? $" Up to {concurrent - slots} additional requests may queue on this {slots}-slot model." : "";
                var mentor = Selected("mentor-model");
                var potentialConflict = Selected("strategy")?.Id == "resident" && mentor is not null &&
                    mentor.Id != model.Id && Str(mentor.Record, "gpu") == device
                    ? " Distinct mentor and worker checkpoints share this device; simultaneous VRAM fit is unverified." : "";
                var unknownSlots = slots == 0 ? " The service has not reported this model's slot capacity." : "";
                var noPartial = Selected("strategy")?.Id == "partial" && Selected("partial-profile") is null
                    ? " The mentor has no eligible measured partial profile." : "";
                Label("limits-readout", $"Shared weights, independent worker histories.{conflict}{potentialConflict}{unknownSlots}{noPartial}");
            }
        }
        var partial = Selected("strategy")?.Id == "partial";
        Enabled("partial-profile", partial);
        Enabled("recipes", Selected("autonomy")?.Id == "approved_recipes");
        Enabled("verify", Selected("autonomy")?.Id == "approved_recipes");
        var plans = _workers.Select(r => $"{r.Id} · {Str(r.Record, "model")} · {Str(r.Record, "gpu")} · {Str(r.Record, "profile")} · {Str(r.Record, "context")} tokens");
        Label("worker-plan", string.Join("\n", plans));
    }

    private JsonObject Participant(string who)
    {
        var model = Selected(who + "-model") ?? throw new InvalidOperationException("Choose the " + who + " model.");
        var profile = Selected(who + "-profile") ?? throw new InvalidOperationException("Choose the " + who + " profile.");
        if (_profilesFor.GetValueOrDefault(who) != model.Id)
            throw new InvalidOperationException("Wait for the selected model’s profiles.");
        if (ContextLimit(who) < 16384)
            throw new InvalidOperationException("The selected model profile cannot supply a 16,384-token context.");
        var context = Number(who + "-context", 16384, ContextLimit(who));
        var cap = Number("output", 64, 8192);
        if (cap >= context)
            throw new InvalidOperationException("The output allowance leaves no input context.");
        var participant = new JsonObject
        {
            ["model"] = model.Id,
            ["context"] = context,
            ["profile"] = profile.Id,
            ["engine"] = profile.Record?["engine"]?.ToString() ?? Str(model.Record, "engine"),
            ["gpu"] = profile.Record?["gpu"]?.ToString() ?? Str(model.Record, "gpu")
        };
        if (Selected(who + "-reasoning") is { Id: not "default" } reasoning)
            participant["reasoning"] = reasoning.Id;
        return participant;
    }
    private void AddRole()
    {
        var row = Participant("worker");
        var role = Required("role");
        if (_workers.Count >= 12 || _workers.Any(item => item.Id == role))
            throw new InvalidOperationException("Use a unique role ID and at most twelve roles.");
        var draft = Selected("autonomy")?.Id != "approved_recipes";
        row["role"] = role;
        row["specialization"] = string.IsNullOrWhiteSpace(Input("specialization").Text) ? role : Input("specialization").Text?.Trim();
        row["autonomy"] = draft ? "draft" : "approved_recipes";
        row["recipes"] = JsonSerializer.SerializeToNode(draft ? Array.Empty<string>() : Terms("recipes"));
        row["verify_recipe"] = draft || string.IsNullOrWhiteSpace(Input("verify").Text) ? null : Input("verify").Text?.Trim();
        _workers.Add(new(role, $"{role} · {Str(row, "gpu")}", row));
        Choices("roles", _workers, role);
        UpdateBudgets();
        Label("status", "Worker role added to this draft.");
    }
    private JsonObject ResidencyAndSchedule()
    {
        var strategy = Selected("strategy")?.Id ?? "resident";
        var partial = strategy == "partial" ? Selected("partial-profile")?.Id ?? throw new InvalidOperationException("A measured partial mentor profile with a 4 GiB or lower limit is required.") : null;
        return new JsonObject
        {
            ["strategy"] = strategy,
            ["partial_profile"] = partial,
            ["check_in"] = new JsonObject
            {
                ["seconds"] = Number("seconds", 0, 86400),
                ["tasks"] = Number("task-count", 0, 128),
                ["tokens"] = Number("token-count", 0, 10000000),
                ["milestones"] = Yes("milestones"),
                ["on_error"] = Yes("on-error"),
                ["on_conflict"] = Yes("on-conflict")
            }
        };
    }
    internal JsonObject CreateConfiguration()
    {
        var config = ResidencyAndSchedule();
        if (_workers.Count == 0)
            throw new InvalidOperationException("Add at least one worker role.");
        var mentor = Participant("mentor");
        if (Str(config, "strategy") != "resident" && _workers.Any(w => Str(w.Record, "model") == Str(mentor, "model")))
            throw new InvalidOperationException("This residency strategy needs separate mentor and worker models.");
        config["objective"] = Required("objective");
        config["completion_criteria"] = Required("criteria");
        config["working_directory"] = Required("working-directory");
        config["references"] = JsonSerializer.SerializeToNode(Terms("references"));
        config["mentor"] = mentor;
        config["workers"] = new JsonArray(_workers.Select(w => w.Record!.DeepClone()).ToArray());
        config["max_workers"] = Number("concurrency", 1, 12);
        config["limits"] = new JsonObject
        {
            ["max_tasks"] = Number("task-limit", 1, 128),
            ["max_seconds"] = Number("time-limit", 1, 86400),
            ["max_output_tokens"] = Number("output", 64, 8192),
            ["max_check_ins"] = Number("review-limit", 0, 64),
            ["min_free_ram_mib"] = Number("min-ram", 1536, 1048576),
            ["min_free_disk_gib"] = Number("min-disk", 60, 65536),
            ["max_gpu_used_mib"] = Number("max-gpu", 0, 1048576)
        };
        return config;
    }
}
