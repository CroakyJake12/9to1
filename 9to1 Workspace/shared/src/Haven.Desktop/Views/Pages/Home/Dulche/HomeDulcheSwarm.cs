using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Avalonia.Controls;
using CakeOS.Cui;
using CakeOS.Cui.Language;
using CakeOS.Cui.Runtime;
using Haven.Desktop.Services;

namespace Haven.Desktop.Views.Pages.Home;

internal sealed partial class HomeDulcheSwarm : UserControl, IDisposable
{
    private sealed record Choice(string Id, string Label, JsonNode? Record = null)
    {
        public override string ToString() => Label;
    }

    private readonly DulcheLocalClient _client;
    private readonly Func<Func<CancellationToken, Task>, CancellationToken, Task> _run;
    private readonly CuiControlLoader _loader = new();
    private readonly CuiViewModel _bindings = new();
    private readonly Dictionary<string, Control> _ui = new();
    private readonly Dictionary<string, string> _profilesFor = new();
    private readonly List<Choice> _workers = [];
    private readonly CancellationTokenSource _lifetime = new();
    private string? _createPayload, _createKey;
    private JsonNode? _presets, _observed, _reasoningModes;
    private string? _editingSchedule;
    private bool _assigning, _creating, _configuring;

    internal HomeDulcheSwarm(DulcheLocalClient client, Func<Func<CancellationToken, Task>, CancellationToken, Task> run)
    {
        _client = client;
        _run = run;
        using var resource = GetType().Assembly.GetManifestResourceStream("Haven.Desktop.Home.DulcheSwarm.cui")
            ?? throw new InvalidDataException("Swarm CUI resource is missing.");
        using var reader = new StreamReader(resource);
        var parser = new CuiRichParser();
        var document = parser.Parse(reader.ReadToEnd(), "DulcheSwarm.cui");
        foreach (var key in "runs tasks mentor-model mentor-profile mentor-reasoning partial-profile worker-model worker-profile worker-reasoning roles autonomy strategy preset milestones on-error on-conflict".Split(' '))
            Mount(key, new ComboBox());
        foreach (var key in "objective working-directory criteria references review-note mentor-context worker-context role specialization recipes verify seconds task-count token-count concurrency task-limit time-limit output review-limit min-ram min-disk max-gpu".Split(' '))
            Mount(key, new TextBox());
        foreach (var key in "status progress rates resources task-detail comparisons decisions mentor-budget worker-budget worker-plan limits-readout".Split(' '))
            Mount(key, new TextBlock());
        foreach (var key in "monitor configure new refresh benchmarks pause resume review stop stop-worker add-role remove-role create apply".Split(' '))
        {
            var button = new Button();
            Mount(key, button);
            button.Click += async (_, _) => await ObserveAsync(token => ActAsync(key, token));
        }
        _loader.SetBindingContext(_bindings);
        var view = _loader.TryLoad(document);
        if (view.Root is null || parser.Diagnostics.Diagnostics.Concat(view.Diagnostics).Any(d => d.Severity == CuiDiagnosticSeverity.Error))
            throw new InvalidDataException("Swarm CUI could not be rendered.");
        _loader.WireBindings(view.Root);
        Content = view.Root;
        foreach (var prefix in new[] { "mentor", "worker" })
        {
            var captured = prefix;
            Select(prefix + "-model").SelectionChanged += async (_, _) =>
            {
                if (!_assigning)
                    await ObserveAsync(token => ReadProfilesAsync(captured, token));
            };
            Select(prefix + "-profile").SelectionChanged += (_, _) => UpdateBudgets();
            Input(prefix + "-context").TextChanged += (_, _) => UpdateBudgets();
        }
        Select("preset").SelectionChanged += (_, _) => ApplyPreset();
        Select("runs").SelectionChanged += async (_, _) =>
        {
            if (!_assigning)
                await ObserveAsync(ReadRunAsync);
        };
        Select("tasks").SelectionChanged += (_, _) => ShowTask();
        Select("strategy").SelectionChanged += (_, _) => UpdateBudgets();
        Select("autonomy").SelectionChanged += (_, _) => UpdateBudgets();
        foreach (var key in new[] { "output", "concurrency" })
            Input(key).TextChanged += (_, _) => UpdateBudgets();
        ConfigureDefaults();
    }

    private void Mount(string key, Control control)
    {
        _ui[key] = control;
        _loader.BindAuthoredControl("sw-" + key, _ => control);
    }
    private ComboBox Select(string key) => (ComboBox)_ui[key];
    private TextBox Input(string key) => (TextBox)_ui[key];
    private Choice? Selected(string key) => Select(key).SelectedItem as Choice;
    private static string Str(JsonNode? row, string key) => row?[key]?.ToString() ?? "";
    private static string Pretty(JsonNode? row, string key) => Str(row, key).Replace('_', ' ');
    private static IEnumerable<JsonNode> Rows(JsonNode? rows) => rows is JsonArray a ? a.OfType<JsonNode>() : [];
    private static string Rate(JsonNode? node) => double.TryParse(node?.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var n) ? n.ToString("0.0", CultureInfo.CurrentCulture) : "unavailable";
    private void Label(string key, string value) => CuiPropertyPresentation.Set(_ui[key], "Text", value, CuiPropertyLevel.Instance, "Dulche Swarm", order: 2);
    private void Enabled(string key, bool value) => CuiPropertyPresentation.Set(_ui[key], "IsEnabled", value, CuiPropertyLevel.Instance, "Dulche Swarm", order: 2);
    private void SetPage(bool configure)
    {
        _configuring = configure;
        _bindings.Set("Configuration", configure);
        _bindings.Set("Monitor", !configure);
    }

    private void Choices(string key, IEnumerable<Choice> values, string? preferred = null)
    {
        var box = Select(key);
        var next = values.ToArray();
        var id = preferred ?? Selected(key)?.Id;
        if (box.ItemsSource is Choice[] old && old.Length == next.Length &&
            old.Zip(next).All(pair => pair.First.Id == pair.Second.Id && pair.First.Label == pair.Second.Label &&
                JsonNode.DeepEquals(pair.First.Record, pair.Second.Record)))
        {
            if (preferred is not null)
                box.SelectedItem = old.FirstOrDefault(x => x.Id == preferred) ?? old.FirstOrDefault();
            return;
        }
        var wasAssigning = _assigning;
        try
        {
            _assigning = true;
            box.ItemsSource = next;
            box.SelectedItem = next.FirstOrDefault(x => x.Id == id) ?? next.FirstOrDefault();
        }
        finally { _assigning = wasAssigning; }
    }

    private async Task ObserveAsync(Func<CancellationToken, Task> action)
    {
        if (_lifetime.IsCancellationRequested)
            return;
        try
        {
            await _run(async token =>
            {
                try
                {
                    await action(token);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { }
                catch (Exception error) { if (!_lifetime.IsCancellationRequested) Label("status", error.Message); }
            }, _lifetime.Token);
        }
        catch (ObjectDisposedException) when (_lifetime.IsCancellationRequested) { }
    }

    internal async Task RefreshAsync(JsonNode status, CancellationToken token)
    {
        var options = Rows(status["models"]).Select(m =>
        {
            var id = Str(m, "id");
            var compact = new JsonObject { ["engine"] = Str(m, "engine"), ["gpu"] = Str(m, "gpu"), ["context"] = m["context"]?.DeepClone(), ["slots"] = m["slots"]?.DeepClone() };
            return new Choice(id, $"{Str(m, "name")} · {Str(m, "gpu")}", compact);
        }).ToArray();
        Choices("mentor-model", options);
        var workerDefault = Selected("worker-model")?.Id ?? options.FirstOrDefault(choice => choice.Id != Selected("mentor-model")?.Id && choice.Record?["context"]?.GetValue<int>() >= 16384)?.Id;
        Choices("worker-model", options, workerDefault);
        await ReadProfilesAsync("mentor", token);
        await ReadProfilesAsync("worker", token);
        var all = await _client.GetAsync("swarms", token);
        _presets = all["presets"];
        _reasoningModes = all["reasoning_modes"];
        RefreshReasoning("mentor");
        RefreshReasoning("worker");
        var runs = Rows(all["items"]).Select(v => RunChoice(v)).ToArray();
        Choices("runs", runs);
        if (runs.Length == 0 && !_configuring)
            Label("progress", "No Swarm runs. Select Configure to start one.");
        await ReadRunAsync(token);
        UpdateBudgets();
    }

    private async Task ReadProfilesAsync(string who, CancellationToken token)
    {
        var model = Selected(who + "-model");
        if (model is null || _profilesFor.GetValueOrDefault(who) == model.Id)
            return;
        Enabled(who + "-context", false);
        var result = await _client.GetAsync($"models/{Uri.EscapeDataString(model.Id)}/profiles", token);
        if (Selected(who + "-model")?.Id != model.Id)
            return;
        var profiles = Rows(result["items"]).Select(v => new Choice(Str(v, "id"), Str(v, "name"), v["settings"]?.DeepClone())).ToArray();
        Choices(who + "-profile", profiles, "current");
        if (who == "mentor")
            Choices("partial-profile", profiles.Where(p =>
            p.Record?["vram_ceiling_mib"]?.GetValue<int>() is > 0 and <= 4096 && p.Record?["gpu_layers"]?.GetValue<int>() > 0));
        _profilesFor[who] = model.Id;
        RefreshReasoning(who);
        Enabled(who + "-context", true);
        var limit = ContextLimit(who);
        Input(who + "-context").Text = limit < 16384 ? "" : Math.Min(32768, limit).ToString(CultureInfo.InvariantCulture);
        UpdateBudgets();
    }

    private void RefreshReasoning(string who)
    {
        var model = Selected(who + "-model");
        var allowed = model is null ? [] : Rows(_reasoningModes?[model.Id]?["modes"]).Select(mode => mode.ToString()).ToArray();
        if (allowed.Length == 0)
            allowed = ["default"];
        Choices(who + "-reasoning", allowed.Select(mode => new Choice(mode, mode switch
        {
            "enabled" => "Enabled",
            "disabled" => "Disabled",
            _ => "Model default"
        })));
        Enabled(who + "-reasoning", allowed.Length > 1);
    }

    private int ContextLimit(string who)
    {
        var profile = Selected(who + "-profile")?.Record;
        var model = Selected(who + "-model")?.Record;
        return profile?["context"]?.GetValue<int>() ?? model?["context"]?.GetValue<int>() ?? 0;
    }
    private static Choice RunChoice(JsonNode record)
    {
        var id = Str(record, "id");
        return new(id, $"{Str(record, "objective")} · {Str(record, "strategy")} · {Str(record, "state")} · {id[..Math.Min(id.Length, 8)]}");
    }

    private async Task ReadRunAsync(CancellationToken token)
    {
        var id = Selected("runs")?.Id;
        if (id is null)
        {
            _observed = null;
            foreach (var action in new[] { "pause", "resume", "review", "stop", "apply", "stop-worker" })
                Enabled(action, false);
            return;
        }
        var detail = await _client.GetAsync("swarms/" + Uri.EscapeDataString(id), token);
        if (Selected("runs")?.Id != id)
            return;
        ShowRun(detail);
    }

    private async Task ActAsync(string action, CancellationToken token)
    {
        switch (action)
        {
            case "monitor":
                SetPage(false);
                await ReadRunAsync(token);
                return;
            case "configure":
                if (_observed?["config"] is JsonObject saved && _editingSchedule != Str(_observed, "id"))
                {
                    _editingSchedule = Str(_observed, "id");
                    await LoadDraftAsync(saved, token);
                }
                SetPage(true);
                return;
            case "new":
                _editingSchedule = null;
                _workers.Clear();
                Choices("roles", _workers);
                foreach (var key in new[] { "objective", "working-directory", "criteria", "references", "specialization", "recipes", "verify", "review-note" })
                    Input(key).Text = "";
                ConfigureDefaults();
                SetPage(true);
                return;
            case "benchmarks":
                SetPage(false);
                var results = await _client.GetAsync("swarms/benchmarks", token);
                Label("comparisons", StrategyComparison(results));
                return;
            case "refresh":
                _profilesFor.Clear();
                await RefreshAsync(await _client.GetAsync("status", token), token);
                return;
            case "add-role":
                AddRole();
                return;
            case "remove-role":
                if (Selected("roles") is { } remove)
                    _workers.RemoveAll(r => r.Id == remove.Id);
                Choices("roles", _workers);
                UpdateBudgets();
                return;
            case "create":
                if (_creating)
                    return;
                _creating = true;
                Enabled("create", false);
                try
                {
                    var body = CreateConfiguration();
                    var encoded = body.ToJsonString();
                    if (_createPayload != encoded || _createKey is null)
                    {
                        _createPayload = encoded;
                        _createKey = Guid.NewGuid().ToString("N");
                    }
                    var created = await _client.PostAsync("swarms", body, token, _createKey);
                    _createKey = null;
                    _createPayload = null;
                    var id = Str(created, "id");
                    var list = await _client.GetAsync("swarms", token);
                    Choices("runs", Rows(list["items"]).Select(v => RunChoice(v)), id);
                    SetPage(false);
                    ShowRun(created);
                    Label("status", "Swarm started.");
                }
                finally { _creating = false; Enabled("create", true); }
                return;
        }
        var selected = Selected("runs") ?? throw new InvalidOperationException("Choose a swarm first.");
        var route = "swarms/" + Uri.EscapeDataString(selected.Id);
        var command = action switch
        {
            "review" => "check-in",
            "apply" => "configure",
            "stop-worker" => "tasks/" + Uri.EscapeDataString(Selected("tasks")?.Id ?? throw new InvalidOperationException("Choose a worker task.")) + "/stop",
            _ => action
        };
        var settings = action == "apply" ? ResidencyAndSchedule() : new JsonObject();
        var note = Input("review-note").Text?.Trim();
        if (action == "review" && !string.IsNullOrEmpty(note))
        {
            if (note.Length > 2048)
                throw new InvalidOperationException("The escalation note exceeds 2,048 characters.");
            settings["note"] = note;
        }
        var updated = await _client.PostAsync(route + "/" + command, settings, token);
        if (action == "review" && Input("review-note").Text?.Trim() == note)
            Input("review-note").Text = "";
        if (Selected("runs")?.Id == selected.Id)
            ShowRun(updated);
        Label("status", action == "review" ? "Mentor review requested at the next safe boundary." : "Swarm updated.");
    }

    internal void ReportFailure(string message) => Label("status", message);

    public void Dispose()
    {
        _lifetime.Cancel();
        _loader.Dispose();
        _lifetime.Dispose();
        Content = null;
    }
}
