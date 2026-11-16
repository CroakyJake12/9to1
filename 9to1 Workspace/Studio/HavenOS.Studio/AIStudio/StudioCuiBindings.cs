using System.ComponentModel;
using System.Text.Json;
using CakeOS.Cui;
using CakeOS.Cui.Runtime;
using NineToOne.Dulche.Training;

namespace HavenOS.AIStudio;

public interface IStudioFileDialogs
{
    Task<byte[]?> ImportAsync(CancellationToken cancellationToken);
    Task<bool> ExportAsync(string suggestedName, byte[] bytes, CancellationToken cancellationToken);
}

public static class StudioProjectExport
{
    public static async Task WriteAsync(Stream destination, ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (destination.CanSeek) { destination.Position = 0; destination.SetLength(0); }
        await destination.WriteAsync(bytes, cancellationToken);
    }
}

public sealed record StudioCuiRow(string Id, string Kind, string Title, string Detail, object Identity) : ICuiBindingContext
{
    public bool TryGetValue(string path, out object? value)
    {
        value = path switch { "Id" => Id, "Title" => Title, "Detail" => Detail, _ => null };
        return path is "Id" or "Title" or "Detail";
    }
}

/// <summary>Typed Studio operations and projections. All controls/layout are authored in Studio.cui.</summary>
public sealed class StudioCuiBindings : ICuiWritableBindingContext, ICuiActionDispatcher, ICuiActionAvailability,
    INotifyPropertyChanged, IAsyncDisposable
{
    private readonly AIStudioApi _api;
    private readonly StudioEngineCheckController _engine;
    private readonly IStudioFileDialogs _files;
    private readonly Action<Action> _publish;
    private string _page = "Home";
    private StudioProject? _project;
    private StudioResource? _resource;
    private IReadOnlyList<StudioCuiRow> _rows = [];
    private string _search = "", _name = "", _definition = "", _description = "", _instructions = "", _input = "";
    private string _noticeTitle = "", _notice = "";
    private bool _busy, _closed;
    private Task? _engineAction;
    private static readonly Dictionary<string, StudioResourceKind> ResourcePages = new(StringComparer.Ordinal)
    {
        ["Evaluations"] = StudioResourceKind.EvaluationSuite, ["Test Suites"] = StudioResourceKind.TestSuite,
        ["Schemas"] = StudioResourceKind.Schema, ["Model Routers"] = StudioResourceKind.ModelRouter,
        ["Generative UI"] = StudioResourceKind.GenerativeUi, ["Run Profiles"] = StudioResourceKind.RunProfile
    };
    private static readonly string[] Pages = ["Home", "Projects", "Harnesses", "Tools", "Agents", "Playground",
        "Evaluations", "Test Suites", "Replay", "Schemas", "Model Routers", "Generative UI", "Run Profiles", "Templates", "Engine self-check"];

    public StudioCuiBindings(AIStudioApi api, StudioEngineCheckController engine, IStudioFileDialogs files, Action<Action> publish)
    { _api = api; _engine = engine; _files = files; _publish = publish; _engine.Changed += EngineChanged; }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Notify() { if (!_closed) PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty)); }
    private void EngineChanged(StudioEngineCheckState state) => _publish(Notify);
    private void Notice(string title, string message) { _noticeTitle = title; _notice = message; Notify(); }
    public void ReportHostNotice(string title, string message) => Notice(title, message);
    private bool Success<T>(StudioResult<T> result)
    { if (result.IsSuccess) return true; Notice(result.Error!.Code, result.Error.Message); return false; }

    public bool TryGetValue(string path, out object? value)
    {
        var engine = _engine.State;
        value = path switch
        {
            "Page" => _page, "Title" => _project?.Name ?? _resource?.Name ?? (_page == "Home" ? "Build with Dulche" : _page),
            "Subtitle" => _project is { } project ? $"{project.ProjectType} · version {project.Version} · {project.TemplateId} v{project.TemplateVersion}"
                : _resource is { } resource ? $"{resource.Kind} · version {resource.Version} · {resource.OwnerScope}" : "AI Studio · shared Dulche authoring and diagnostics",
            "Search" => _search, "Name" => _name, "Definition" => _definition, "Description" => _description,
            "Instructions" => _instructions, "Input" => _input, "Rows" => _rows, "RowsEmpty" => _rows.Count == 0,
            "Busy" => _busy, "CanEdit" => !_busy && !_closed,
            "NoticeTitle" => _noticeTitle, "Notice" => _notice, "HasNotice" => _notice.Length != 0,
            "ShowHome" => _page == "Home", "ShowProjectEditor" => _project is not null,
            "ShowResourceEditor" => _resource is not null, "ShowResourceCreate" => _resource is null && ResourcePages.ContainsKey(_page),
            "ShowProjectList" => _project is null && _page is "Home" or "Projects" or "Harnesses" or "Tools",
            "ShowHarnessCreation" => _project is null && (_page is "Projects" or "Harnesses"),
            "ShowTemplates" => _page == "Templates", "ShowToolTypes" => _page == "Tools" && _project is null,
            "ShowAgents" => _page == "Agents", "ShowPlayground" => _page == "Playground", "ShowReplay" => _page == "Replay",
            "ShowEngine" => _page == "Engine self-check", "ShowSearch" => _project is null && _resource is null && _page is "Projects" or "Templates" or "Harnesses" or "Tools",
            "IsHarness" => _project?.ProjectType == StudioProjectType.Harness, "IsTool" => _project?.ProjectType == StudioProjectType.Tool,
            "IsEvaluation" => _resource?.Kind == StudioResourceKind.EvaluationSuite, "IsTestSuite" => _resource?.Kind == StudioResourceKind.TestSuite,
            "EngineTitle" => StudioEngineCheckState.Title, "EngineDescription" => StudioEngineCheckState.Description,
            "EngineReady" => engine.Ready is null ? "Readiness has not been checked." : engine.Ready.Value ? "Local CPU self-check is available." : "Local CPU self-check is unavailable or awaiting resources.",
            "EngineError" => engine.Error ?? "", "EngineUnknown" => engine.OutcomeUnknown,
            "EngineSummary" => engine.Selected is null ? "Choose a retained attempt or run a self-check."
                : $"{engine.Selected.Status} · updates: {engine.Selected.CompletedUpdates?.ToString() ?? "unknown"} · verified artifacts: {engine.Selected.Artifacts.Count}",
            "EngineReason" => engine.Selected?.Reason ?? "", "EnginePausePending" => engine.PauseRequested,
            "CanEngineRun" => !_closed && engine.CanLaunch, "CanEnginePause" => !_closed && engine.CanPause,
            "CanEngineRecover" => !_closed && engine.CanCheckRecovery, "CanEngineMore" => !_closed && engine.Next is not null,
            "EngineRows" => engine.History.Select(item => new StudioCuiRow(item.Identity.AttemptId.ToString("D"), "attempt",
                item.Status + " · " + item.Identity.AttemptId.ToString("D"),
                $"Updates: {item.CompletedUpdates?.ToString() ?? "unknown"} · artifacts: {item.Artifacts.Count}", item.Identity)).ToArray(),
            _ => null
        };
        return value is not null;
    }

    public bool TrySetValue(string path, object? value)
    {
        if (_closed || _busy || value is not string text || text.Length > (path == "Definition" ? 1024 * 1024 : 16384)) return false;
        switch (path)
        {
            case "Search": _search = text; break; case "Name": _name = text; break;
            case "Definition": _definition = text; break; case "Description": _description = text; break;
            case "Instructions": _instructions = text; break; case "Input": _input = text; break;
            default: return false;
        }
        return true;
    }

    public bool? IsActionAvailable(string command) => !_closed && (!_busy || command is "DismissNotice" or "EnginePause");
    public Task InitializeAsync(CancellationToken token = default) => LoadPageAsync(token);

    public async ValueTask DispatchAsync(string command, object? parameter, CancellationToken cancellationToken = default)
    {
        if (IsActionAvailable(command) != true) return;
        if (command == "DismissNotice") { _notice = ""; Notify(); return; }
        if (command == "EngineRun" || command == "EngineRecover")
        {
            if (command == "EngineRun" ? !_engine.State.CanLaunch : !_engine.State.CanCheckRecovery) return;
            _engineAction = ObserveEngineAsync(command == "EngineRun" ? _engine.LaunchAsync(cancellationToken) : _engine.CheckRecoveryAsync(cancellationToken));
            Notify(); return; // Retain the task; do not block the CUI queue's Pause action.
        }
        _busy = true; Notify();
        try
        {
            switch (command)
            {
                case "Navigate":
                    if (parameter is not string page || !Pages.Contains(page, StringComparer.Ordinal)) throw new InvalidOperationException("Unknown Studio destination.");
                    _page = page; _project = null; _resource = null; _rows = []; _name = "";
                    _description = ""; _instructions = ""; _input = "";
                    if (ResourcePages.TryGetValue(page, out var kind)) _definition = NewResourceDefinition(kind);
                    await LoadPageAsync(cancellationToken); break;
                case "Refresh": await LoadPageAsync(cancellationToken); break;
                case "OpenRow": await OpenRowAsync(parameter, cancellationToken); break;
                case "CreateHarness": await CreateTemplateAsync("harness.assistant.v1", cancellationToken); break;
                case "CreateSkill": await CreateTemplateAsync("tool.skill.v1", cancellationToken); break;
                case "CreatePlugin": await CreateTemplateAsync("tool.plugin.v1", cancellationToken); break;
                case "CreateMcp": await CreateTemplateAsync("tool.mcp-client.v1", cancellationToken); break;
                case "Import":
                    var bytes = await _files.ImportAsync(cancellationToken);
                    if (bytes is not null) { var imported = await _api.ImportProjectAsync(bytes, cancellationToken: cancellationToken); if (Success(imported)) OpenProject(imported.Value!); }
                    break;
                case "SaveProject": await SaveProjectAsync(cancellationToken); break;
                case "ValidateProject":
                    if (await SaveProjectAsync(cancellationToken))
                    { var validation = await _api.ValidateProjectAsync(_project!.ProjectId, cancellationToken: cancellationToken);
                      if (Success(validation)) Notice(validation.Value!.IsValid ? "Structural validation passed" : "Validation findings",
                          validation.Value.ValidationScope + "\n" + string.Join("\n", validation.Value.Issues.Select(issue => $"{issue.Severity} · {issue.Code} · {issue.Path}: {issue.Message}"))); }
                    break;
                case "PreviewProject": if (_project is { } preview) { var run = await _api.TestProjectAsync(preview.ProjectId, cancellationToken: cancellationToken); if (Success(run)) Notice("Harness run", run.Value!.Output); } break;
                case "InstallTool": if (_project is { } tool) { var installed = await _api.InstallToolAsync(tool.ProjectId, tool.Version, cancellationToken); if (Success(installed)) Notice("Tool install", installed.Value!.Status); } break;
                case "ProjectHistory": if (_project is { } historyProject) { var history = await _api.ProjectHistoryAsync(historyProject.ProjectId, cancellationToken); if (Success(history)) ShowHistory(history.Value!); } break;
                case "DuplicateProject": if (_project is { } duplicate) { var copied = await _api.DuplicateProjectAsync(duplicate.ProjectId, duplicate.Name + " copy", cancellationToken); if (Success(copied)) OpenProject(copied.Value!); } break;
                case "ExportProject": if (_project is { } export) { var result = await _api.ExportProjectAsync(export.ProjectId, cancellationToken: cancellationToken); if (Success(result) && await _files.ExportAsync(export.Name + ".json", result.Value!, cancellationToken)) Notice("Project exported", "The lossless project bundle was exported."); } break;
                case "RecoverProject": if (_project is { } deleted) { var result = await _api.DeleteProjectAsync(deleted.ProjectId, deleted.Version, cancellationToken); if (Success(result)) { _project = null; _page = "Projects"; await LoadPageAsync(cancellationToken); } } break;
                case "CreateAgent":
                    var agent = await _api.CreateAgentAsync(new(Guid.Empty, _name, _description, _instructions, "{}", 0, true), cancellationToken);
                    if (Success(agent)) Notice("Agent created", $"Agent {agent.Value!.AgentId} revision {agent.Value.DefinitionRevision}."); break;
                case "RunPlayground":
                    var played = await _api.RunPlaygroundAsync(new(null, null, [], [], new Dictionary<string, string>(), null, _instructions), _input, cancellationToken);
                    if (Success(played)) Notice("Playground result", played.Value!.Output); break;
                case "CreateResource":
                    if (ResourcePages.TryGetValue(_page, out var resourceKind)) { var created = await _api.CreateResourceAsync(resourceKind, _name, _definition, cancellationToken: cancellationToken); if (Success(created)) { _name = ""; _definition = NewResourceDefinition(resourceKind); await LoadPageAsync(cancellationToken); } } break;
                case "SaveResource": if (_resource is { } saved) { var result = await _api.SaveResourceAsync(saved.ResourceId, saved.Version, _name, _definition, cancellationToken); if (Success(result)) OpenResource(result.Value!); } break;
                case "ResourceHistory": if (_resource is { } historyResource) { var history = await _api.ResourceHistoryAsync(historyResource.ResourceId, cancellationToken); if (Success(history)) ShowHistory(history.Value!); } break;
                case "RecoverResource": if (_resource is { } recovered) { var result = await _api.DeleteResourceAsync(recovered.ResourceId, recovered.Version, cancellationToken); if (Success(result)) { _resource = null; await LoadPageAsync(cancellationToken); } } break;
                case "RunEvaluation": if (_resource is { } evaluation) { var result = await _api.RunEvaluationAsync(evaluation.ResourceId, cancellationToken: cancellationToken); if (Success(result)) Notice("Evaluation results", string.Join("\n", result.Value!.Results.Select(row => $"{row.Status} · {row.CaseId} · {string.Join("; ", row.DeterministicFacts)}"))); } break;
                case "RunTestSuite": if (_resource is { } suite) { var result = await _api.RunTestSuiteAsync(suite.ResourceId, cancellationToken: cancellationToken); if (Success(result)) Notice("Test results", string.Join("\n", result.Value!.Results.Select(row => $"{row.Status} · {row.CaseId} · {row.StructuredErrorCode}"))); } break;
                case "EngineRefresh": await _engine.RefreshAsync(cancellationToken); break;
                case "EnginePause": await _engine.PauseAsync(cancellationToken); break;
                case "EngineMore": await _engine.LoadMoreAsync(cancellationToken); break;
                case "SelectAttempt": if (parameter is StudioCuiRow { Identity: LocalTrainingAttempt attempt }) await _engine.SelectAsync(attempt, cancellationToken); break;
                default: throw new InvalidOperationException("Unknown Studio action.");
            }
        }
        catch (OperationCanceledException) { Notice("Cancelled", "The operation was cancelled; retained work remains available."); }
        catch (Exception error) when (error is IOException or JsonException or InvalidOperationException or UnauthorizedAccessException)
        { Notice("Operation unavailable", error.Message); }
        finally { _busy = false; Notify(); }
    }

    private async Task ObserveEngineAsync(Task operation)
    {
        try { await operation.ConfigureAwait(false); }
        catch (Exception error) { _publish(() => Notice("Self-check status unavailable", error.Message)); }
    }

    private async Task LoadPageAsync(CancellationToken token)
    {
        if (_page == "Engine self-check") { await _engine.RefreshAsync(token); return; }
        if (_page == "Templates")
            _rows = StudioTemplateCatalog.Search(_search).Select(template => new StudioCuiRow(template.TemplateId, "template", template.Name, $"v{template.Version} · {template.Description}", template.TemplateId)).ToArray();
        else if (ResourcePages.TryGetValue(_page, out var kind) && _resource is null)
        {
            var result = await _api.ListResourcesAsync(kind, cancellationToken: token);
            if (Success(result)) _rows = result.Value!.Select(resource => new StudioCuiRow(resource.ResourceId.ToString("D"), "resource", resource.Name, $"v{resource.Version} · {resource.ModifiedAt:g}", resource.ResourceId)).ToArray();
        }
        else if (_project is null && _page is "Home" or "Projects" or "Harnesses" or "Tools")
        {
            var result = await _api.ListProjectsAsync(200, search: _page == "Home" ? null : _search,
                type: _page == "Harnesses" ? StudioProjectType.Harness : _page == "Tools" ? StudioProjectType.Tool : null, cancellationToken: token);
            if (Success(result)) _rows = result.Value!.Items.Select(project => new StudioCuiRow(project.ProjectId.ToString("D"), "project", project.Name,
                $"{project.ProjectType} · v{project.Version} · {project.ModifiedAt:g}", project.ProjectId)).ToArray();
        }
        Notify();
    }

    private async Task OpenRowAsync(object? parameter, CancellationToken token)
    {
        if (parameter is not StudioCuiRow row || !_rows.Contains(row)) throw new InvalidOperationException("Select a current Studio row.");
        if (row.Kind == "template") await CreateTemplateAsync((string)row.Identity, token);
        else if (row.Kind == "project") { var result = await _api.OpenProjectAsync((Guid)row.Identity, token); if (Success(result)) OpenProject(result.Value!); }
        else if (row.Kind == "resource") { var result = await _api.OpenResourceAsync((Guid)row.Identity, cancellationToken: token); if (Success(result)) OpenResource(result.Value!); }
    }

    private async Task CreateTemplateAsync(string id, CancellationToken token)
    {
        var template = StudioTemplateCatalog.Find(id) ?? throw new InvalidOperationException("Template is unavailable.");
        var result = template.ToolType is { } type ? await _api.CreateToolAsync(type, id, "New " + template.Name, cancellationToken: token)
            : await _api.CreateProjectAsync(template.ProjectType, id, "New " + template.Name, cancellationToken: token);
        if (Success(result)) OpenProject(result.Value!);
    }

    private void OpenProject(StudioProject project)
    { _project = project; _resource = null; _page = project.ProjectType == StudioProjectType.Harness ? "Harnesses" : "Tools"; _name = project.Name; _definition = Format(project.DefinitionJson); Notify(); }
    private void OpenResource(StudioResource resource)
    { _resource = resource; _project = null; _name = resource.Name; _definition = Format(resource.DefinitionJson); Notify(); }
    private static string Format(string json) { using var document = JsonDocument.Parse(json); return JsonSerializer.Serialize(document.RootElement, StudioJson.Options); }
    private void ShowHistory(IReadOnlyList<ProjectRevision> revisions) => Notice("History", revisions.Count == 0 ? "No saved revisions yet."
        : string.Join("\n", revisions.Select(row => $"v{row.Version} · {row.SavedAt:g} · {row.Note}")));

    private async Task<bool> SaveProjectAsync(CancellationToken token)
    {
        if (_project is null) return false;
        var result = await _api.SaveProjectAsync(_project.ProjectId, _project.Version, _definition, note: "Edited in AI Studio", cancellationToken: token);
        if (!Success(result)) return false;
        _project = result.Value!;
        if (_name != _project.Name)
        {
            var renamed = await _api.RenameProjectAsync(_project.ProjectId, _project.Version, _name, token);
            if (!Success(renamed)) return false;
            _project = renamed.Value!;
        }
        OpenProject(_project); return true;
    }

    private static string NewResourceDefinition(StudioResourceKind kind)
    {
        var id = Guid.NewGuid();
        object definition = kind switch
        {
            StudioResourceKind.EvaluationSuite => new EvaluationSuite(id, "New evaluation", "replace-with-project-or-agent-id", "Harness", 1,
                [new EvaluationCase(Guid.NewGuid(), "Case 1", "input", "{}", null, null, [], [], null, null, null, "Created in AI Studio", 1)],
                [new EvaluationGrader(Guid.NewGuid(), EvaluationGraderType.ExactOutput, "{}", true)], null, null, DateTimeOffset.UtcNow, 1),
            StudioResourceKind.TestSuite => new TestSuite(id, "New tests", "replace-with-project-or-agent-id", "Harness", 1,
                [new StudioTestCase(Guid.NewGuid(), "Case 1", "input", "{}", [new TestAssertion("output-contains", "outputContains", "output", "expected")], 1, "Created in AI Studio")], null, 1),
            StudioResourceKind.Schema => new { schemaId = id.ToString(), type = "object", properties = new { } },
            StudioResourceKind.ModelRouter => new { routes = Array.Empty<object>() },
            StudioResourceKind.GenerativeUi => new { components = Array.Empty<object>(), actions = Array.Empty<object>() },
            StudioResourceKind.RunProfile => new { revision = 1, modelPolicy = "default", limits = new { maxSteps = 20 } }, _ => new { }
        };
        return JsonSerializer.Serialize(definition, StudioJson.Options);
    }

    public async ValueTask DisposeAsync()
    {
        if (_closed) return;
        _closed = true; _engine.Changed -= EngineChanged;
        await _engine.DisposeAsync();
        if (_engineAction is not null) await _engineAction;
        PropertyChanged = null;
    }
}
