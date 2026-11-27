using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using Avalonia.Controls;
using Avalonia.Threading;
using CakeOS.Cui;
using CakeOS.Cui.Language;
using CakeOS.Cui.Runtime;
using CakeOS.Cui.Themes;
using Haven.Application;
using Haven.Desktop.Services;

namespace Haven.Desktop.Views.Pages.Home;

internal sealed partial class HomeDulchePanel : UserControl, IDisposable
{
    private sealed record Item(string Id, string Label, JsonNode Data)
    {
        public override string ToString() => Label;
    }

    private readonly DulcheLocalClient _client;
    private readonly ICatalogRepository? _specialists;
    private readonly HomeDulcheSwarm _swarm;
    private bool _showSwarm;
    private readonly Func<Func<CancellationToken, Task>, CancellationToken, Task> _run;
    private readonly CuiControlLoader _loader;
    private readonly CuiViewModel _bindings = new();
    private readonly Dictionary<string, Control> _controls = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(4) };
    private readonly Dictionary<string, List<Dictionary<string, string>>> _histories = new();
    private readonly CancellationTokenSource _lifetime = new();
    private bool _active, _connected, _polling, _generating, _managing, _operating, _projects;
    private string? _loadedProfiles, _removeConfirmation, _inspectedRevision;
    private int _reads;
    private long _promptRevision;
    private bool _acceptingRequests;
    private CancellationTokenSource? _generationCancellation;
    private bool _generationCancelRequested;
    private string? _generationRunId;
    private string _modelSection = "chat";
    private Item? Model => Select("model").SelectedItem as Item;
    private SelectionSnapshot Epoch => new(Model?.Id, _reads);
    private readonly record struct SelectionSnapshot(string? Model, int Generation);

    internal HomeDulchePanel(DulcheLocalClient client, Func<Func<CancellationToken, Task>, CancellationToken, Task> run,
        ICatalogRepository? specialists = null, CuiThemePreferenceContext? theme = null)
    {
        _client = client;
        _specialists = specialists;
        _run = run;
        _loader = new(HomeAppsCuiDocument.CreateControlRegistry());
        if (theme is not null) _loader.SetThemePreferenceContext(theme);
        _swarm = new HomeDulcheSwarm(client,run);
        _loader.BindAuthoredControl("dulche-swarm-mount",_=>_swarm);
        using var stream = typeof(HomeDulchePanel).Assembly.GetManifestResourceStream("Haven.Desktop.Home.Dulche.cui")
            ?? throw new InvalidDataException("Missing Dulche CUI resource.");
        using var reader = new StreamReader(stream);
        var parser = new CuiRichParser();
        var document = parser.Parse(reader.ReadToEnd(), "Dulche.cui");
        foreach (var id in new[] { "model", "results", "files", "profiles", "jobs", "agents", "candidate", "adapters", "adapter-scope", "adapter-specialist", "load-policy" })
            Mount(id, new ComboBox());
        foreach (var id in new[] { "prompt", "search", "library-search", "import-path", "import-base", "profile-name", "profile-settings", "profile-device", "profile-context", "filter-architecture", "filter-size", "filter-capability", "filter-quant", "adapter-path", "adapter-scale" })
            Mount(id, new TextBox());
        foreach (var id in new[] { "status", "detail", "output", "message", "catalog-detail", "resources", "benchmark-results", "inference-support", "inference-operation", "adapter-status", "recovery", "profile-status", "metrics", "route", "adapter-identity", "optimisation-comparison", "training-status" })
            Mount(id, new TextBlock());
        foreach (var id in new[] { "toggle", "unload", "generate", "generation-cancel", "view-chat", "view-performance", "view-adapters", "new", "manage", "swarm", "swarm-return", "find", "installed", "inspect", "remove", "download", "import", "pause", "resume", "cancel", "profile-select", "profile-save", "profile-rename", "recover", "stop", "stop-all", "agent-resume", "allow", "benchmarks", "benchmark-start", "candidates", "training", "optimise", "optimise-restore", "adapter-attach", "adapter-detach", "adapter-refresh", "load-policy-apply" })
        {
            var button = new Button();
            Mount(id, button);
            button.Click += async (_, _) => await RunActionAsync(id);
        }
        _bindings.Set("Manage", false);
        _bindings.Set("ChatVisible", true);
        _bindings.Set("PerformanceVisible", false);
        _bindings.Set("AdaptersVisible", false);
        InitializeModelPresentation();
        _loader.SetBindingContext(this);
        _loader.SetActionDispatcher(this);
        var result = _loader.TryLoad(document);
        if (result.Root is null || parser.Diagnostics.Diagnostics.Concat(result.Diagnostics).Any(d => d.Severity == CuiDiagnosticSeverity.Error))
            throw new InvalidDataException("Dulche CUI failed to render: " + string.Join("; ", result.Diagnostics.Select(d => d.Message)));
        Content = result.Root;
        _loader.WireBindings(result.Root);
        InitializeProfileDrafts();
        Select("candidate").SelectionChanged += (_, _) => ShowCandidate();
        Select("model").SelectionChanged += (_, _) => HandleModelSelection();
        Input("prompt").TextChanged += (_, _) => _promptRevision++;
        Input("library-search").TextChanged += (_, _) => RefreshModelRows();
        Select("adapter-scope").ItemsSource = new[] { "Model", "Specialist" };
        Select("adapter-scope").SelectedIndex = 0;
        Select("adapter-scope").SelectionChanged += (_, _) => RenderInferenceManagement();
        Select("adapter-specialist").SelectionChanged += (_, _) => RenderInferenceManagement();
        Select("adapters").SelectionChanged += (_, _) => RenderInferenceManagement();
        Input("adapter-scale").Text = "1";
        Select("load-policy").ItemsSource = DulcheLocalClient.PolicyNames;
        Select("load-policy").SelectedIndex = Array.IndexOf(DulcheLocalClient.Policies, _client.SavedPolicy);
        Select("results").SelectionChanged += (_, _) => { ClearRepositoryFiles(); ShowDetails(); RenderCataloguePresentation(); };
        Select("files").SelectionChanged += (_, _) => RenderCataloguePresentation();
        Select("jobs").SelectionChanged += (_, _) => RenderCataloguePresentation();
        // TextChanging observes the already-updated value synchronously; a delayed
        // TextChanged event must not clear results from a newer completed read.
        foreach (var field in CatalogueQueryFields)
            Input(field).TextChanging += (_, _) => InvalidateCatalogueQuery();
        Input("import-path").TextChanging += (_, _) => RenderCataloguePresentation();
        Select("profiles").SelectionChanged += (_, _) => ApplySelectedProfileDraft();
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
    private void Text(string key, string text)
    {
        State(_controls[key], "Text", text);
        if (key == "output" && Model is { } model) _modelOutputs[model.Id] = text;
        if (key == "message") _bindings.Set("HasRunStatus", !string.IsNullOrWhiteSpace(text));
    }
    private static void State(Control control, string property, object? value) =>
        CuiPropertyPresentation.Set(control, property, value, CuiPropertyLevel.Instance, "Dulche runtime", order: 2);
    private static string Value(JsonNode? row, string key) => row is JsonObject value ? value[key]?.ToString() ?? "" : "";
    private static IEnumerable<JsonNode> Rows(JsonNode? node) => node is JsonArray array ? array.OfType<JsonNode>() : [];
    private static string ModelState(Item? item) => Value(item?.Data, "state").ToLowerInvariant();
    private static bool CanGenerate(string state) => state is "ready" or "loaded" or "running" or "active";
    private static bool CanRequestInference(Item? item)
    {
        if (item is null) return false;
        if (item.Data["serving_supported"] is JsonValue support && support.TryGetValue<bool>(out var supported) && !supported) return false;
        var state = ModelState(item);
        if (item.Data["can_generate"] is JsonValue generate && generate.TryGetValue<bool>(out var allowed) && !allowed)
            return false;
        if (CanGenerate(state)) return true;
        if (item.Data["can_start"] is JsonValue start && start.TryGetValue<bool>(out var canStart))
            return canStart;
        return state is "installed" or "unloaded" or "stopped" or "idle";
    }
    private static (string Label, string? Action) Toggle(Item? item)
    {
        var state = ModelState(item);
        // Failure does not clear the controller's manual stop barrier. Only its
        // explicit Resume operation may replace that prior user intent.
        if (state == "failed" && item?.Data["manual_suspended"] is JsonValue flag &&
            flag.TryGetValue<bool>(out var manual) && manual)
            return ("Resume", "resume");
        return Toggle(state);
    }
    private static (string Label, string? Action) Toggle(string state) => state switch
    {
        "ready" or "loaded" or "running" or "active" => ("Suspend", "suspend"),
        "suspended" or "manual_suspended" or "manually_suspended" or "suspended_manual" => ("Resume", "resume"),
        "resource_suspended" or "suspended_resource" or "suspended_resources" or "waiting_resources" or "waiting_for_resources" => ("Keep suspended", "suspend"),
        // The controller retains a manual pending suspend while its owned start or
        // recovery settles. The UI must not hide that existing stop-intent owner.
        "loading" or "resuming" or "recovering" => ("Suspend", "suspend"),
        "suspending" or "unloading" => (state + "…", null),
        "unloaded" or "failed" or "stopped" or "offline" => ("Load", "load"),
        _ => ("Unavailable", null)
    };
    private void Render()
    {
        var selected = Model;
        var state = ModelState(selected);
        var toggle = Toggle(selected);
        State(_controls["toggle"], "Content", toggle.Label);
        State(_controls["benchmark-start"], "IsEnabled", _connected && !_operating && !_generating && !_runStopUnconfirmed && !HasActiveInferenceOperation && CanGenerate(state));
        State(_controls["toggle"], "IsEnabled", _connected && !_operating && toggle.Action is not null);
        State(_controls["generate"], "IsEnabled", _connected && _acceptingRequests && !_generating && !_operating && !_runStopUnconfirmed && !HasActiveInferenceOperation && CanRequestInference(selected));
        State(_controls["generation-cancel"], "IsEnabled", _generating && !_generationCancelRequested && _generationCancellation is not null);
        _bindings.Set("GenerationActive", _generating);
        State(_controls["unload"], "IsEnabled", _connected && !_operating && CanGenerate(state));
        State(_controls["new"], "IsEnabled", !_generating && !_operating && !_runStopUnconfirmed);
        State(_controls["model"], "IsEnabled", !_generating && !_operating && !_runStopUnconfirmed);
        var data = selected?.Data;
        var unavailable = selected is null && _retainedModelChoice is not null;
        _bindings.Set("SelectedModelName", selected?.Label ?? (unavailable ? _retainedModelName : "Choose an installed model"));
        var friendlyState = unavailable ? "No longer available" : StateLabel(selected);
        _bindings.Set("SelectedModelSummary", string.Join(" · ", new[]
        {
            friendlyState, ReportedContext(data) is { } context ? $"{context:N0} tokens" : "",
            Value(data, "gpu")
        }.Where(value => value.Length > 0)));
        var modelIssue = unavailable ? "This model is no longer in the configured list. Your draft and previous output are retained; choose a model explicitly to continue."
            : Value(data, "error") is { Length: > 0 } issue ? issue : Value(data, "last_error");
        _bindings.Set("SelectedModelIssue", modelIssue);
        _bindings.Set("HasSelectedModelIssue", !string.IsNullOrWhiteSpace(modelIssue));
        Text("detail", data is null ? "" : string.Join(" · ", new[] { Value(data, "engine"), Value(data, "gpu"), Value(data, "quantization"), state == "waiting_for_resources" ? "Suspended — Waiting for Resources" : state }.Where(v => v.Length > 0)));
        RenderModelPresentation();
        RenderInferenceManagement();
        RenderCataloguePresentation();
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
            catch (Exception error) when (!HasUnconfirmedStop(error))
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
                _acceptingRequests = status["stop_all"]?.GetValue<bool>() != true &&
                    Value(status, "phase") is "" or "ready";
                var address = _retainedModelChoice ?? Model?.Id;
                var models = ReadConfiguredModels(status["models"]);
                Replace("model", models, address);
                RefreshModelRows();
                State(_controls["allow"], "IsVisible", status["stop_all"]?.GetValue<bool>() == true);
                var agents = Rows(status["agents"]).Select(x => new Item(Value(x, "id"), $"{Value(x, "id")} · {Value(x, "state")}", x));
                Replace("agents", agents, (Select("agents").SelectedItem as Item)?.Id);
                var resources = status["resources"] as JsonObject;
                var devices = Rows(resources?["gpus"]).OfType<JsonObject>().Select(DescribeDevice).ToArray();
                Text("resources", (devices.Length == 0 ? "GPU telemetry has not been reported." : string.Join("\n", devices)) +
                    (Number(resources?["host_available_mib"]) is { } available ? $"\n{available:N0} MiB RAM available" : "\nAvailable RAM has not been reported."));
                var policy = Array.IndexOf(DulcheLocalClient.Policies, Value(status, "policy"));
                _bindings.Set("ObservedLoadPolicy", policy >= 0 ? "Current service policy: " + DulcheLocalClient.PolicyNames[policy] : "The service has not reported its loading policy.");
                Text("status", $"Connected · {models.Length} configured model{(models.Length == 1 ? "" : "s")}");
                await RefreshInferenceManagementAsync(token);
                if (_showSwarm)
                {
                    try { await _swarm.RefreshAsync(status,token); }
                    catch(Exception error) when(error is IOException or HttpRequestException)
                    { _swarm.ReportFailure(error.Message); }
                }
                if ((_managing || _modelSection == "performance" || _catalogueVisible) && !_operating)
                {
                    try { await RefreshManagementAsync(token); }
                    catch (Exception error) when (error is IOException or HttpRequestException)
                    { Text(_modelSection == "performance" ? "profile-status" : "catalog-detail", error.Message); }
                }
            }, "status", () => _connected = false);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested || !_active) { }
        catch (Exception error) { _connected = false; if (_active) Text("status", "Dulche unavailable · " + error.Message); }
        finally { _polling = false; if (_active && !_lifetime.IsCancellationRequested) Render(); }
    }
    private void Replace(string key, IEnumerable<Item> source, string? preferred)
    {
        // A live request owns its selected model until it settles. A periodic status
        // refresh must not replace that identity or redirect its output to another row.
        if (key == "model" && (_generating || _operating || _runStopUnconfirmed)) return;
        var rows = source.ToArray();
        var select = Select(key);
        var prior = select.ItemsSource as Item[];
        if (prior is not null && prior.Length == rows.Length && prior.Zip(rows).All(pair => pair.First.Id == pair.Second.Id && pair.First.Label == pair.Second.Label && JsonNode.DeepEquals(pair.First.Data, pair.Second.Data)))
            return;
        if (key == "model") _replacingModels = true;
        try
        {
            void ReplaceSelection()
            {
                select.ItemsSource = rows;
                // A missing explicit selection is unavailable. It never silently becomes
                // the first unrelated model on a later status observation.
                select.SelectedItem = rows.FirstOrDefault(item => item.Id == preferred) ??
                    (key is not ("files" or "jobs") && (key != "model" || !_observedInitialModel) ? rows.FirstOrDefault() : null);
            }
            if (key is "results" or "files" or "jobs") UpdateCatalogueSelection(ReplaceSelection);
            else ReplaceSelection();
            if (key == "model" && rows.Length > 0) _observedInitialModel = true;
        }
        finally { if (key == "model") _replacingModels = false; }
        if (key is "results" or "files" or "jobs") RenderCataloguePresentation();
        if (key == "model") { _reads++; HandleModelSelection(); }
    }
    private async Task RunActionAsync(string command)
    {
        if (!_active || _lifetime.IsCancellationRequested) return;
        // Cancelling the already-owned response needs no new model admission.
        // Its original stream still owns and joins the exact backend stop receipt.
        if (command == "generation-cancel")
        {
            if (_generating && _generationCancellation is { } cancellation && !_generationCancelRequested)
            {
                _generationCancelRequested = true;
                Text("message", "Cancelling this response… Waiting for its run to stop.");
                cancellation.Cancel();
                Render();
            }
            return;
        }
        var navigation = IsNavigationCommand(command);
        var stop = command is "stop" or "stop-all";
        if (!navigation && !stop && (_generating || _operating || _runStopUnconfirmed)) return;
        var intent = CaptureCommand(command);
        // Claim synchronously: no second click or model change can race a held admission.
        if (!navigation && !stop && command == "generate")
        {
            _generating = true;
            _generationCancelRequested = false;
            _generationRunId = null;
            _generationCancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        }
        else if (!navigation && !stop)
            _operating = true;
        Render();
        var acquired = false;
        void ReleaseClaim()
        {
            if (!navigation && !stop && command == "generate")
            {
                _generating = false;
                _generationCancellation?.Dispose();
                _generationCancellation = null;
            }
            else if (!navigation && !stop)
                _operating = false;
        }
        try
        {
            await _run(async token =>
            {
                acquired = true;
                Exception? unsettled = null;
                try
                {
                    if (!_active || token.IsCancellationRequested) return;
                    if (!stop && !ReferenceEquals(intent.Model, Model))
                    { Text("message", "The selected model changed before this action started. Review it and try again."); return; }
                    await RunActionCoreAsync(intent, token);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { }
                catch (Exception error)
                {
                    ReportCommandFailure(error);
                    if (HasUnconfirmedStop(error)) { unsettled = error; throw; }
                }
                finally
                {
                    try
                    {
                        ReleaseClaim();
                        if (_active && !_lifetime.IsCancellationRequested)
                        {
                            Render();
                            await PollAsync();
                        }
                    }
                    catch (Exception cleanup) when (unsettled is not null)
                    { throw new AggregateException("The response stop and final presentation did not settle.", unsettled, cleanup); }
                }
            }, _lifetime.Token);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested || !_active) { }
        // The owner has already retained any typed unsettled stop before this UI report.
        catch (Exception error) { ReportCommandFailure(error); }
        finally
        {
            // Admission itself can fail before the body acquires the command.
            if (!acquired)
            {
                ReleaseClaim();
                if (_active && !_lifetime.IsCancellationRequested) Render();
            }
        }
    }
    private async Task RunActionCoreAsync(CommandIntent intent, CancellationToken token)
    {
        var command = intent.Command;
        if (command is "view-chat" or "view-performance" or "view-adapters" or "manage")
        {
            SelectModelSection(intent.TargetSection);
            await PollAsync();
            return;
        }
        if (command is "swarm" or "swarm-return")
        {
            SelectModelSection("chat");
            _showSwarm = intent.ShowSwarm;
            _bindings.Set("SwarmVisible", _showSwarm);
            _bindings.Set("SingleVisible", !_showSwarm);
            State(_controls["swarm"], "Content", _showSwarm ? "Back to chat" : "Swarm Mode");
            if (_showSwarm) await PollAsync();
            return;
        }
        if (command == "new")
        {
            if (intent.Model is { } model)
            {
                _histories.Remove(model.Id);
                Text("output", ""); Text("message", "");
            }
            return;
        }
        await RunCoreAsync(intent, token);
    }
    private void SelectModelSection(string section)
    {
        _modelSection = section;
        _managing = section == "advanced";
        _showSwarm = false;
        _bindings.Set("SingleVisible", true);
        _bindings.Set("SwarmVisible", false);
        _bindings.Set("ChatVisible", section == "chat");
        _bindings.Set("PerformanceVisible", section == "performance");
        _bindings.Set("AdaptersVisible", section == "adapters");
        _bindings.Set("Manage", _managing);
        RenderModelPresentation();
        State(_controls["swarm"], "Content", "Swarm Mode");
    }
    private async Task RunCoreAsync(CommandIntent intent, CancellationToken token)
    {
        var command = intent.Command;
        var model = intent.Model;
        var agent = intent.Selected("agents")?.Id;
        string modelPath() => $"models/{Esc(model?.Id)}";
        Text("message", "");
        switch (command)
        {
            case "generate":
            {
                if (model is null || !_acceptingRequests || !CanRequestInference(model) || _operating || HasActiveInferenceOperation)
                    return;
                var submittedDraft = intent.Text("prompt");
                var submittedRevision = intent.PromptRevision;
                var prompt = intent.Need("prompt");
                var history = _histories.GetValueOrDefault(model.Id) ?? [];
                var messages = history.Append(new Dictionary<string, string> { { "role", "user" }, { "content", prompt } }).ToList();
                var answer = new StringBuilder();
                using var generation = CancellationTokenSource.CreateLinkedTokenSource(token,
                    _generationCancellation?.Token ?? _lifetime.Token);
                Text("output", "");
                Text("message", CanGenerate(ModelState(model)) ? "Generating…" : "Starting inference… Your message is retained while the model loads.");
                try
                {
                    await foreach (var delta in _client.StreamAsync(model.Id, messages, id =>
                    {
                        _generationRunId = id;
                        // Admission is distinct from the first generated content.
                        Dispatcher.UIThread.Post(() =>
                        {
                            if (!_lifetime.IsCancellationRequested && _generating && !_generationCancelRequested &&
                                _generationRunId == id && answer.Length == 0)
                                Text("message", "Starting response…");
                        });
                    }, generation.Token))
                    {
                        answer.Append(delta);
                        Text("output", answer.ToString());
                        if (!_generationCancelRequested) Text("message", "Generating…");
                    }
                }
                catch (OperationCanceledException) when (_generationCancelRequested && !token.IsCancellationRequested)
                {
                    Text("message", "Cancelled. Partial output and your draft were retained.");
                    return;
                }
                if (answer.Length == 0)
                    throw new IOException("The model returned no answer text.");
                messages.Add(new() { { "role", "assistant" }, { "content", answer.ToString() } });
                _histories[model.Id] = messages;
                if (_promptRevision == submittedRevision && string.Equals(Input("prompt").Text, submittedDraft, StringComparison.Ordinal))
                    Input("prompt").Text = "";
                Text("message", "Completed.");
                return;
            }
            case "toggle":
            case "unload":
                if (model is null || _generating || HasActiveInferenceOperation ||
                    model.Data["serving_supported"] is JsonValue support && support.TryGetValue<bool>(out var supported) && !supported)
                    throw new InvalidOperationException("The selected model is not available for this operation.");
                if (command == "unload" && !CanGenerate(ModelState(model))) return;
                var action = command == "unload" ? "unload" : Toggle(model).Action;
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
                await ManageAsync(intent, token);
                return;
        }
        Text("message", "Dulche accepted the operation.");
    }
    private static string Esc(string? value) => Uri.EscapeDataString(!string.IsNullOrWhiteSpace(value)
        ? value : throw new InvalidOperationException("Select an item first."));
    private void RestoreRun(JsonNode run)
    {
        if (_generating || _runStopUnconfirmed)
            throw new InvalidOperationException("Finish and confirm the current response before opening a saved run.");
        var model = Value(run, "model");
        var matches = (Select("model").ItemsSource as Item[] ?? []).Where(item => item.Id == model).ToArray();
        if (matches.Length != 1)
            throw new InvalidOperationException("The saved run's model is unavailable. Its output has not replaced this conversation.");
        var completed = Rows(run["completed_steps"]).LastOrDefault();
        var lastChoice = Rows(completed?["choices"]).FirstOrDefault();
        var answer = Value(lastChoice?["message"], "content");
        var status = Value(run, "state");
        if (answer.Length == 0)
        {
            Text("message", $"Run {Value(run, "id")} · {status}. {Value(run, "reason")}");
            return;
        }
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
        Select("model").SelectedItem = matches[0];
        Text("output", answer);
        Text("message", $"Restored run {Value(run, "id")} · {status}.");
    }

    public void Dispose()
    {
        Deactivate();
        _lifetime.Cancel();
        _swarm.Dispose();
        _bindings.PropertyChanged -= OnModelBindingChanged;
        _loader.Dispose();
        _lifetime.Dispose();
        Content = null;
    }
}
