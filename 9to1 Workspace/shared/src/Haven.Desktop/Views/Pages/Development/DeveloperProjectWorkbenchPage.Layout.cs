using Avalonia.Controls;

namespace Haven.Desktop.Views.Pages.Development;

/// <summary>Presentation only. Project, editor, draft, process, mission and close owners remain
/// the existing workbench. Switching panels never submits work or changes a source selection.</summary>
public sealed partial class DeveloperProjectWorkbenchPage
{
    private string _workbenchPanel = "code", _workbenchExplorer = "files", _workbenchOutput = "output";
    private bool _workbenchOutputVisible, _workbenchFind, _workbenchLineEndings, _workbenchReview;
    private double _workbenchWidth = 1440, _workbenchHeight = 900;

    private bool TryGetWorkbenchLayoutValue(string path, out object? value)
    {
        var code = _workbenchPanel == "code";
        var wide = _workbenchWidth >= 1180;
        var explorer = _workbenchWidth >= 800;
        // Keep a failed/older preview inspectable for a selected CUI draft; compilation
        // success is not the source-selection authority. Explicit Preview also stays available.
        var preview = code && wide && CurrentSourceTab()?.Path.EndsWith(".cui", StringComparison.OrdinalIgnoreCase) == true;
        var details = _workbenchFind || _workbenchLineEndings || _workbenchReview || _manualRecoveryCandidates.Count != 0;
        var detailsBesideEditor = details && _workbenchWidth - (explorer ? 214 : 0) - (preview ? 300 : 0) >= 900;
        var outputVisible = _workbenchOutputVisible || _manualRunStop is not null;
        value = path switch
        {
            "LayoutProjectLocation" => _project.Root.Location + " · " + _project.Root.EnvironmentId,
            "LayoutMainColumns" => code ? preview ? "214 1fr 300" : explorer ? "214 1fr" : "1fr" : "1fr",
            "LayoutEditorColumn" => explorer ? 1 : 0,
            "LayoutPreviewColumn" => preview ? 2 : 0,
            "LayoutExplorerVisible" => _workbenchPanel == "files" || code && explorer,
            "LayoutEditorVisible" => code,
            "LayoutPreviewVisible" => _workbenchPanel == "preview" || preview,
            "LayoutMissionVisible" => _workbenchPanel == "mission",
            "LayoutProjectVisible" => _workbenchPanel == "project",
            "LayoutCodeSelected" => code,
            "LayoutFilesSelected" => _workbenchPanel == "files",
            "LayoutPreviewSelected" => _workbenchPanel == "preview",
            "LayoutMissionSelected" => _workbenchPanel == "mission",
            "LayoutProjectSelected" => _workbenchPanel == "project",
            "LayoutExplorerFiles" => _workbenchExplorer == "files",
            "LayoutExplorerSearch" => _workbenchExplorer == "search",
            "LayoutExplorerCreate" => _workbenchExplorer == "create",
            "LayoutOutputVisible" => outputVisible,
            "LayoutCanHideOutput" => outputVisible && !_pending && _manualRunStop is null,
            "LayoutOutputHeight" => _workbenchHeight <= 650 ? 150d : 210d,
            "LayoutOutputSelected" => outputVisible && _workbenchOutput == "output",
            "LayoutProblemsSelected" => outputVisible && _workbenchOutput == "problems",
            "LayoutTerminalSelected" => outputVisible && _workbenchOutput == "terminal",
            "LayoutTestsSelected" => outputVisible && _workbenchOutput == "tests",
            "LayoutTasksSelected" => outputVisible && _workbenchOutput == "tasks",
            "LayoutProcessVisible" => _workbenchOutput is "output" or "terminal" or "tests",
            "LayoutFindVisible" => _workbenchFind,
            "LayoutLineEndingsVisible" => _workbenchLineEndings,
            "LayoutReviewVisible" => _workbenchReview,
            "LayoutEditorBodyColumns" => detailsBesideEditor ? "1fr 320" : "1fr",
            "LayoutEditorBodyRows" => details && !detailsBesideEditor ? "1fr 1fr" : "1fr",
            "LayoutEditorDetailsVisible" => details,
            "LayoutEditorDetailsColumn" => detailsBesideEditor ? 1 : 0,
            "LayoutEditorDetailsRow" => detailsBesideEditor ? 0 : 1,
            "LayoutEmptyEditor" => CurrentSourceTab() is null,
            _ => null
        };
        return value is not null;
    }

    private static bool IsWorkbenchLayoutCommand(string command) => command is
        "dev.layout.code" or "dev.layout.files" or "dev.layout.preview" or "dev.layout.mission" or "dev.layout.project" or
        "dev.layout.explorer-files" or "dev.layout.explorer-search" or "dev.layout.explorer-create" or
        "dev.layout.output" or "dev.layout.problems" or "dev.layout.terminal" or "dev.layout.tests" or "dev.layout.tasks" or
        "dev.layout.hide-output" or "dev.layout.find" or "dev.layout.line-endings" or "dev.layout.review";

    private ValueTask DispatchWorkbenchLayout(string command, CancellationToken token)
    {
        long generation; lock (_gate) generation = _generation;
        _work.RunSynchronous(original =>
        {
            original.BindPublicationGuard(() => IsCurrent(generation)); original.DemandPublication(); token.ThrowIfCancellationRequested();
            lock (_gate)
            {
                if (IsActionAvailable(command) != true) throw new InvalidOperationException("The project view is no longer available.");
                switch (command)
                {
                    case "dev.layout.code": _workbenchPanel = "code"; break;
                    case "dev.layout.files": _workbenchPanel = "files"; break;
                    case "dev.layout.preview": _workbenchPanel = "preview"; break;
                    case "dev.layout.mission": _workbenchPanel = "mission"; break;
                    case "dev.layout.project": _workbenchPanel = "project"; break;
                    case "dev.layout.explorer-files": _workbenchExplorer = "files"; break;
                    case "dev.layout.explorer-search": _workbenchExplorer = "search"; break;
                    case "dev.layout.explorer-create": _workbenchExplorer = "create"; break;
                    // A stale Hide click cannot conceal an admitted operation or its still
                    // settling process. This changes no execution or cancellation owner.
                    case "dev.layout.hide-output":
                        if (!_pending && _manualRunStop is null) _workbenchOutputVisible = false;
                        break;
                    case "dev.layout.find": _workbenchFind = !_workbenchFind; break;
                    case "dev.layout.line-endings": _workbenchLineEndings = !_workbenchLineEndings; break;
                    case "dev.layout.review": _workbenchReview = !_workbenchReview; break;
                    default: _workbenchOutput = command["dev.layout.".Length..]; _workbenchOutputVisible = true; break;
                }
            }
            NotifyOriginal(original);
        });
        return ValueTask.CompletedTask;
    }

    private void RevealWorkbenchCommand(string command, CancellationToken token)
    {
        // Reveal the actual destination of explicit work. This creates no ready state, execution
        // receipt, selected model or source authority; the existing command still admits itself.
        if (command is not ("dev.preview-edit" or "dev.analyze-cui-draft" or "dev.preview-cui-draft" or
            "dev.run-command" or "dev.run-tests" or "dev.run-selected-target" or "dev.run-stage" or
            "dev.git-status" or "dev.git-diff" or "dev.git-branches" or "dev.open-source" or
            "dev.open-file-navigation-result" or "dev.open-search-result")) return;
        if (IsActionAvailable(command) != true) return;
        long generation; lock (_gate) generation = _generation;
        _work.RunSynchronous(original =>
        {
            original.BindPublicationGuard(() => IsCurrent(generation)); original.DemandPublication(); token.ThrowIfCancellationRequested();
            lock (_gate)
            {
                if (command == "dev.preview-edit") { _workbenchPanel = "code"; _workbenchReview = true; }
                else if (command == "dev.preview-cui-draft") { if (_workbenchWidth < 1180) _workbenchPanel = "preview"; }
                else if (command is "dev.open-source" or "dev.open-file-navigation-result" or "dev.open-search-result") _workbenchPanel = "code";
                else
                {
                    _workbenchOutputVisible = true;
                    _workbenchOutput = command switch
                    { "dev.analyze-cui-draft" => "problems", "dev.run-tests" => "tests", "dev.run-command" => "terminal", _ => "output" };
                }
            }
            NotifyOriginal(original);
        });
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        if (e.NewSize.Width <= 0 || e.NewSize.Height <= 0) return;
        long generation;
        lock (_gate)
        {
            _workbenchWidth = e.NewSize.Width; _workbenchHeight = e.NewSize.Height;
            if (!_active || _work.IsRetiring) return; generation = _generation;
        }
        _work.RunSynchronous(original =>
        {
            original.BindPublicationGuard(() => IsCurrent(generation));
            if (original.IsPublicationCurrent) NotifyOriginal(original);
        });
    }
}
