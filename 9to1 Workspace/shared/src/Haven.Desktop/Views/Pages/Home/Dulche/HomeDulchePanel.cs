using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using Avalonia.Controls;
using Avalonia.Threading;
using CakeOS.Cui;
using CakeOS.Cui.Language;
using CakeOS.Cui.Runtime;
using Haven.Desktop.Services;

namespace Haven.Desktop.Views.Pages.Home;

internal sealed partial class HomeDulchePanel : UserControl, IDisposable
{
    private sealed record Item(string Id, string Label, JsonNode Data)
    {
        public override string ToString() => Label;
    }

    private readonly DulcheLocalClient _client;
    private readonly HomeDulcheSwarm _swarm;
    private bool _showSwarm;
    private readonly Func<Func<CancellationToken, Task>, CancellationToken, Task> _run;
    private readonly CuiControlLoader _loader = new();
    private readonly CuiViewModel _bindings = new();
    private readonly Dictionary<string, Control> _controls = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(4) };
    private readonly Dictionary<string, List<Dictionary<string, string>>> _histories = new();
    private readonly CancellationTokenSource _lifetime = new();
    private bool _active, _connected, _polling, _generating, _managing, _operating, _projects;
    private string? _loadedProfiles, _removeConfirmation, _inspectedRevision;
    private int _reads;
    private Item? Model => Select("model").SelectedItem as Item;
    private SelectionSnapshot Epoch => new(Model?.Id, _reads);
    private readonly record struct SelectionSnapshot(string? Model, int Generation);

    internal HomeDulchePanel(DulcheLocalClient client, Func<Func<CancellationToken, Task>, CancellationToken, Task> run)
    {
        _client = client;
        _run = run;
        _swarm = new HomeDulcheSwarm(client,run);
        _loader.BindAuthoredControl("dulche-swarm-mount",_=>_swarm);
        using var stream = typeof(HomeDulchePanel).Assembly.GetManifestResourceStream("Haven.Desktop.Home.Dulche.cui")
            ?? throw new InvalidDataException("Missing Dulche CUI resource.");
        using var reader = new StreamReader(stream);
        var parser = new CuiRichParser();
        var document = parser.Parse(reader.ReadToEnd(), "Dulche.cui");
        foreach (var id in new[] { "model", "results", "files", "profiles", "jobs", "agents", "candidate" })
            Mount(id, new ComboBox());
        foreach (var id in new[] { "prompt", "search", "import-path", "import-base", "profile-name", "profile-settings", "profile-device", "profile-context", "filter-architecture", "filter-size", "filter-capability", "filter-quant" })
            Mount(id, new TextBox());
        foreach (var id in new[] { "status", "detail", "output", "message", "catalog-detail", "resources", "benchmark-results" })
            Mount(id, new TextBlock());
        foreach (var id in new[] { "toggle", "unload", "generate", "new", "manage", "swarm", "find", "installed", "inspect", "remove", "download", "import", "pause", "resume", "cancel", "profile-select", "profile-save", "profile-rename", "recover", "stop", "stop-all", "agent-resume", "allow", "benchmarks", "benchmark-start", "candidates", "training" })
        {
            var button = new Button();
            Mount(id, button);
            button.Click += async (_, _) => await RunActionAsync(id);
        }
        _bindings.Set("Manage", false);
        _loader.SetBindingContext(_bindings);
        var result = _loader.TryLoad(document);
        if (result.Root is null || parser.Diagnostics.Diagnostics.Concat(result.Diagnostics).Any(d => d.Severity == CuiDiagnosticSeverity.Error))
            throw new InvalidDataException("Dulche CUI failed to render: " + string.Join("; ", result.Diagnostics.Select(d => d.Message)));
        Content = result.Root;
        _loader.WireBindings(result.Root);
        Select("candidate").SelectionChanged += (_, _) => ShowCandidate();
        Select("model").SelectionChanged += (_, _) => { _reads++; _loadedProfiles = null; Render(); };
        Select("results").SelectionChanged += (_, _) => { _removeConfirmation = null; _inspectedRevision = null; Select("files").ItemsSource = Array.Empty<Item>(); ShowDetails(); };
        Select("profiles").SelectionChanged += (_, _) =>
        {
            if (Select("profiles").SelectedItem is not Item profile)
                return;
            var settings = profile.Data["settings"];
            Input("profile-settings").Text = settings?.ToJsonString() ?? "{}";
            Input("profile-device").Text = settings?["gpu"]?.ToString() ?? Value(Model?.Data, "gpu");
            Input("profile-context").Text = (settings?["context"] ?? settings?["context_length"])?.ToString() ?? Value(Model?.Data, "context");
        };
        _timer.Tick += async (_, _) => await PollAsync();
        Render();
    }
    private void Mount(string name, Control control)
    {
        _controls.Add(name, control);
        _loader.BindAuthoredControl("dulche-" + name, _ => control);
    }
    private ComboBox Select(string key) => (ComboBox)_controls[key];
    private TextBox Input(string key) => (TextBox)_controls[key];
    private void Text(string key, string text) => State(_controls[key], "Text", text);
    private static void State(Control control, string property, object? value) =>
        CuiPropertyPresentation.Set(control, property, value, CuiPropertyLevel.Instance, "Dulche runtime", order: 2);
    private static string Value(JsonNode? row, string key) => row?[key]?.ToString() ?? "";
    private static IEnumerable<JsonNode> Rows(JsonNode? node) => node is JsonArray array ? array.OfType<JsonNode>() : [];
    private static string ModelState(Item? item) => Value(item?.Data, "state").ToLowerInvariant();
    private static bool CanGenerate(string state) => state is "ready" or "loaded" or "running" or "active";
    private static (string Label, string? Action) Toggle(string state) => state switch
    {
        "ready" or "loaded" or "running" or "active" => ("Suspend", "suspend"),
        "suspended" or "manual_suspended" or "manually_suspended" or "suspended_manual" => ("Resume", "resume"),
        "resource_suspended" or "suspended_resource" or "suspended_resources" or "waiting_resources" or "waiting_for_resources" => ("Keep suspended", "suspend"),
        "loading" or "resuming" or "suspending" or "unloading" => (state + "…", null),
        "unloaded" or "failed" or "stopped" or "offline" => ("Load", "load"),
        _ => ("Unavailable", null)
    };
    private void Render()
    {
        var selected = Model;
        var state = ModelState(selected);
        var toggle = Toggle(state);
        State(_controls["toggle"], "Content", toggle.Label);
        State(_controls["benchmark-start"], "IsEnabled", _connected && !_operating && CanGenerate(state));
        State(_controls["toggle"], "IsEnabled", _connected && !_operating && toggle.Action is not null);
        State(_controls["generate"], "IsEnabled", _connected && !_generating && CanGenerate(state));
        State(_controls["unload"], "IsEnabled", _connected && !_operating && CanGenerate(state));
        State(_controls["new"], "IsEnabled", !_generating);
        State(_controls["model"], "IsEnabled", !_generating && !_operating);
        var data = selected?.Data;
        Text("detail", data is null ? "" : string.Join(" · ", new[] { Value(data, "engine"), Value(data, "gpu"), Value(data, "quantization"), state == "waiting_for_resources" ? "Suspended — Waiting for Resources" : state }.Where(v => v.Length > 0)));
    }

    internal void Activate()
    {
        _active = true;
        _timer.Start();
        _ = PollAsync();
    }
    internal void Deactivate()
    {
        _active = false;
        _timer.Stop();
    }
    private Task RunObservedAsync(Func<CancellationToken, Task> operation, string statusField, Action? failed = null) =>
        _run(async token =>
        {
            try
            {
                await operation(token);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
            catch (Exception error)
            {
                failed?.Invoke();
                if (!_lifetime.IsCancellationRequested && _active)
                    Text(statusField, error.Message);
            }
        }, _lifetime.Token);

    private async Task PollAsync()
    {
        if (!_active || _polling || _lifetime.IsCancellationRequested)
            return;
        _polling = true;
        try
        {
            await RunObservedAsync(async token =>
            {
                if (!_connected)
                    await _client.ConnectAsync(token);
                var status = await _client.GetAsync("status", token);
                if (!_active || token.IsCancellationRequested)
                    return;
                _connected = true;
                var address = Model?.Id;
                var models = Rows(status["models"]).Select(x => new Item(Value(x, "id"), Value(x, "name") is { Length: > 0 } name ? name : Value(x, "id"), x)).ToArray();
                Replace("model", models, address);
                State(_controls["allow"], "IsVisible", status["stop_all"]?.GetValue<bool>() == true);
                var agents = Rows(status["agents"]).Select(x => new Item(Value(x, "id"), $"{Value(x, "id")} · {Value(x, "state")}", x));
                Replace("agents", agents, (Select("agents").SelectedItem as Item)?.Id);
                var resources = status["resources"];
                var free = Value(resources, "host_available_mib");
                var devices = Rows(resources?["gpus"]).Select(gpu =>
                    $"{Value(gpu, "name")} {Value(gpu, "used_mib")}/{Value(gpu, "total_mib")} MiB · {Value(gpu, "temperature_c")} °C");
                Text("resources", string.Join("\n", devices));
                Text("status", $"Connected · {models.Length} configured model{(models.Length == 1 ? "" : "s")}" + (free.Length > 0 ? $" · {free} MiB RAM available" : ""));
                if (_showSwarm)
                {
                    try { await _swarm.RefreshAsync(status,token); }
                    catch(Exception error) when(error is IOException or HttpRequestException)
                    { _swarm.ReportFailure(error.Message); }
                }
                if (_managing && !_operating)
                {
                    try { await RefreshManagementAsync(token); }
                    catch (Exception error) when (error is IOException or HttpRequestException)
                    { Text("catalog-detail", error.Message); }
                }
            }, "status", () => _connected = false);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested || !_active) { }
        catch (Exception error) { _connected = false; if (_active) Text("status", "Dulche unavailable · " + error.Message); }
        finally { _polling = false; if (!_lifetime.IsCancellationRequested) Render(); }
    }
    private void Replace(string key, IEnumerable<Item> source, string? preferred)
    {
        var rows = source.ToArray();
        var select = Select(key);
        var prior = select.ItemsSource as Item[];
        if (prior is not null && prior.Length == rows.Length && prior.Zip(rows).All(pair => pair.First.Id == pair.Second.Id && pair.First.Label == pair.Second.Label && JsonNode.DeepEquals(pair.First.Data, pair.Second.Data)))
            return;
        select.ItemsSource = rows;
        select.SelectedItem = rows.FirstOrDefault(item => item.Id == preferred) ?? rows.FirstOrDefault();
    }
    private async Task RunActionAsync(string command)
    {
        if (!_active || _lifetime.IsCancellationRequested)
            return;
        if (command == "swarm")
        {
            _showSwarm=!_showSwarm;
            _managing=false;
            _bindings.Set("Manage",false);
            _bindings.Set("SwarmVisible",_showSwarm);
            _bindings.Set("SingleVisible",!_showSwarm);
            State(_controls["swarm"],"Content",_showSwarm ? "Back to chat" : "Swarm Mode");
            if (_showSwarm) await PollAsync();
            return;
        }
        if (command == "manage")
        {
            _showSwarm=false;
            _bindings.Set("SwarmVisible",false);
            _bindings.Set("SingleVisible",true);
            State(_controls["swarm"],"Content","Swarm Mode");
            _managing = !_managing;
            _bindings.Set("Manage", _managing);
            if (_managing)
                await PollAsync();
            return;
        }
        if (command == "new")
        {
            if (Model is { } model && !_generating)
            {
                _histories.Remove(model.Id);
                Text("output", "");
                Text("message", "");
            }
            return;
        }
        if (command is "stop" or "stop-all")
        {
            try
            {
                await RunObservedAsync(token => RunCoreAsync(command, token), "message");
            }
            catch (Exception error) { if (!_lifetime.IsCancellationRequested) Text("message", error.Message); }
            return;
        }
        if (command == "generate" ? _generating : _operating)
            return;
        if (command == "generate")
            _generating = true;
        else
            _operating = true;
        Render();
        try
        {
            await RunObservedAsync(token => RunCoreAsync(command, token), "message");
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception error) { if (!_lifetime.IsCancellationRequested) Text("message", error.Message); }
        finally
        {
            if (command == "generate")
                _generating = false;
            else
                _operating = false;
            if (!_lifetime.IsCancellationRequested)
            {
                Render();
                await PollAsync();
            }
        }
    }
    private async Task RunCoreAsync(string command, CancellationToken token)
    {
        var model = Model;
        var agent = (Select("agents").SelectedItem as Item)?.Id;
        string modelPath() => $"models/{Esc(model?.Id)}";
        Text("message", "");
        switch (command)
        {
            case "generate":
                if (model is null || !CanGenerate(ModelState(model)))
                    return;
                var prompt = Need("prompt");
                var history = _histories.GetValueOrDefault(model.Id) ?? [];
                var messages = history.Append(new Dictionary<string, string> { { "role", "user" }, { "content", prompt } }).ToList();
                var answer = new StringBuilder();
                Text("output", "");
                Text("message", "Generating…");
                await foreach (var delta in _client.StreamAsync(model.Id, messages, id => Text("message", "Run " + id), token))
                {
                    answer.Append(delta);
                    Text("output", answer.ToString());
                }
                if (answer.Length == 0)
                    throw new IOException("The model returned no answer text.");
                messages.Add(new() { { "role", "assistant" }, { "content", answer.ToString() } });
                _histories[model.Id] = messages;
                Input("prompt").Text = "";
                Text("message", "Completed.");
                return;
            case "toggle":
            case "unload":
                var action = command == "unload" ? "unload" : Toggle(ModelState(model)).Action;
                if (action is null)
                    return;
                await _client.PostAsync($"{modelPath()}/{action}", new { }, token);
                break;
            case "stop":
                await _client.PostAsync($"agents/{Esc(agent)}/stop", new { }, token);
                break;
            case "stop-all":
                await _client.PostAsync("agents/stop-all", new { }, token);
                break;
            case "recover":
                var run = await _client.GetAsync($"agents/{Esc(agent)}", token);
                RestoreRun(run);
                return;
            default:
                await ManageAsync(command, token);
                return;
        }
        Text("message", "Dulche accepted the operation.");
    }
    private static string Esc(string? value) => Uri.EscapeDataString(!string.IsNullOrWhiteSpace(value)
        ? value : throw new InvalidOperationException("Select an item first."));
    private string Need(string field) => !string.IsNullOrWhiteSpace(Input(field).Text)
        ? Input(field).Text!.Trim() : throw new InvalidOperationException("Enter " + field.Replace('-', ' ') + ".");

    private void RestoreRun(JsonNode run)
    {
        var completed = Rows(run["completed_steps"]).LastOrDefault();
        var lastChoice = Rows(completed?["choices"]).FirstOrDefault();
        var answer = Value(lastChoice?["message"], "content");
        var status = Value(run, "state");
        if (answer.Length == 0)
        {
            Text("message", $"Run {Value(run, "id")} · {status}. {Value(run, "reason")}");
            return;
        }
        var model = Value(run, "model");
        var previous = Rows(run["request"]?["steps"]).FirstOrDefault()?["payload"]?["messages"];
        var history = Rows(previous).Select(message => new Dictionary<string, string>
        {
            ["role"] = Value(message, "role"),
            ["content"] = Value(message, "content")
        }).ToList();
        history.Add(new()
        {
            ["role"] = "assistant",
            ["content"] = answer
        });
        _histories[model] = history;
        if (Select("model").ItemsSource is Item[] models && models.FirstOrDefault(item => item.Id == model) is { } selected)
            Select("model").SelectedItem = selected;
        Text("output", answer);
        Text("message", $"Restored run {Value(run, "id")} · {status}.");
    }

    public void Dispose()
    {
        Deactivate();
        _lifetime.Cancel();
        _swarm.Dispose();
        _loader.Dispose();
        _lifetime.Dispose();
        Content = null;
    }
}
