using Avalonia.Controls;
using Avalonia.Platform.Storage;
using CakeOS.Cui.Runtime;
using Haven.Desktop.Services;
using Haven.Desktop.Views.Pages.Home;

namespace Haven.Desktop.Views.Pages.Development;

/// <summary>Atlas 284–287 presentation over the existing project/setup owners. Switching these
/// views preserves the candidate folder and issued template review; it creates no project or task.</summary>
public sealed partial class DeveloperSavedProjectsPage
{
    private string _startSurface = "projects";
    private string? _expandedProject;
    private bool _showStartDiagnostics, _showTemplateFilters, _startFolderPicking;
    private double _startWidth = 1440;

    private bool TryGetStartValue(string path, out object? value)
    {
        var rail = _startWidth <= 520 ? 0 : _startWidth <= 850 ? 72 : 210;
        var content = Math.Max(0, _startWidth - rail);
        var compactCards = content <= 660;
        var editing = _startSurface == "new";
        value = path switch
        {
            "StartColumns" => rail + " 1fr",
            "StartSidebarVisible" => rail != 0,
            "StartSidebarLabels" => _startWidth > 850,
            "StartSidebarPadding" => _startWidth <= 850 ? "11 22 11 16" : "16 25 16 16",
            "StartMobileNavigation" => rail == 0,
            "StartPagePadding" => _startWidth <= 520 ? "16 18 16 26" : content <= 900 ? "28" : "38 34 38 36",
            "StartTitleSize" => _startWidth <= 520 ? 28d : 34d,
            "StartTitle" => _startSurface switch { "new" => _editingSetupDraftWasSaved ? "Setup draft" : "New project", "open" => "Open project", "extension" => "9to1 Extension", _ => "Build" },
            "StartDescription" => _startSurface switch
            {
                "new" => "Name the project, choose the location that will contain it and pick a starting point.",
                "open" => "Choose a saved project or open existing source.",
                "extension" => "An extension needs a target SDK and a compatible contribution.",
                _ => "Your development workspace"
            },
            "StartProjectsSelected" => _startSurface == "projects",
            "StartNewSelected" => editing || _startSurface == "extension",
            "StartOpenSelected" => _startSurface == "open",
            "StartIsProjects" => _startSurface == "projects",
            "StartIsOpen" => _startSurface == "open",
            "StartIsNew" => editing,
            "StartIsExtension" => _startSurface == "extension",
            "StartShowsProjects" => _startSurface is "projects" or "open",
            "StartShowsFolder" => editing || _startSurface == "open",
            "StartShowsBack" => _startSurface != "projects",
            "StartCardColumns" => compactCards ? "1fr" : "1fr 1fr 1fr",
            "StartCardRows" => compactCards ? "Auto Auto Auto" : "Auto",
            "StartOpenCardColumn" => compactCards ? 0 : 1,
            "StartOpenCardRow" => compactCards ? 1 : 0,
            "StartExtensionCardColumn" => compactCards ? 0 : 2,
            "StartExtensionCardRow" => compactCards ? 2 : 0,
            "StartSetupColumns" => !editing || compactCards ? "1fr" : content <= 900 ? "1fr 240" : "1fr 290",
            "StartSetupRows" => editing && compactCards ? "Auto Auto Auto" : "Auto Auto",
            "StartSummaryColumn" => compactCards ? 0 : 1,
            "StartSummaryRow" => compactCards ? 2 : 0,
            "StartSummaryRowSpan" => compactCards ? 1 : 2,
            "StartProjectRowColumns" => compactCards ? "1fr Auto" : "1fr 150 Auto",
            "StartProjectRowRows" => compactCards ? "Auto Auto Auto" : "Auto Auto",
            "StartProjectStateColumn" => compactCards ? 0 : 1,
            "StartProjectStateRow" => compactCards ? 1 : 0,
            "StartProjectToolsColumn" => compactCards ? 1 : 2,
            "StartProjectDetailsRow" => compactCards ? 2 : 1,
            "StartProjectDetailsSpan" => compactCards ? 2 : 3,
            "StartHasProjects" => MergeProjectItems().Length != 0,
            "StartEmptyProjects" => _projectView != "drafts" && MergeProjectItems().Length == 0 && FilteredSetupDrafts().Length == 0,
            "StartEmptyMessage" => !_metadataAvailable ? _status : !_manualMetadataAvailable ? _manualCatalogStatus :
                _projectView == "pinned" ? "No current projects or setup drafts match this pinned view." :
                _projectQuery.Length > 0 ? "No projects match this search." : "Open an existing folder or choose a template to start.",
            "StartDiagnosticsVisible" => _showStartDiagnostics,
            "StartDiagnosticsLabel" => _showStartDiagnostics ? "Hide project details" : "Project details",
            "StartTemplateFiltersVisible" => _showTemplateFilters,
            "StartTemplateFiltersLabel" => _showTemplateFilters ? "Hide filters" : "Template filters",
            "StartInputsEnabled" => !_manualBusy && !_templateBusy && !_startFolderPicking,
            "StartCanNavigate" => !_manualBusy && !_templateBusy && !_startFolderPicking,
            "StartCanBrowseFolder" => IsStartActionAvailable("dev.start.browse-folder"),
            "StartFolderTitle" => editing ? "Project details" : "Open an existing folder or repository",
            "StartFolderHelp" => editing
                ? "Enter a project name and choose the location that will contain it."
                : "Keep the existing source, layout and project identity. No model is needed to open it.",
            "StartProjectName" => _templateProjectName.Length > 0 ? _templateProjectName : "Your new project",
            "TemplateProjectName" => _templateProjectName,
            "TemplateDestinationParent" => _templateDestinationParent,
            "StartCreationHint" => ValidateCreationTarget().Message,
            "CanCheckAndCreate" => IsActionAvailable("dev.templates.review") == true,
            "StartDestination" => _manualFolderPath.Length > 0
                ? (Path.IsPathFullyQualified(_manualFolderPath) ? _manualFolderPath : "(choose a destination location) \u00b7 " + _manualFolderPath)
                : "No destination selected",
            "StartTemplateReadiness" => StartTemplateReadiness(),
            "StartTemplateColumns" => StartTemplateColumnCount() == 2 ? "1fr 1fr" : "1fr",
            "StartTemplateRows" => string.Join(" ", Enumerable.Repeat("Auto", Math.Max(1,
                (int)Math.Ceiling(FilteredTemplateItems().Length / (double)StartTemplateColumnCount())))),
            _ => null
        };
        return value is not null;
    }

    private bool TryGetStartItemValue(object item, string path, out object? value)
    {
        value = path switch
        {
            "DisplaySummary" when item is ManualProjectItem manual => manual.Location,
            "DisplaySummary" when item is ProjectItem task => task.Original.SpaceName,
            "StateLabel" when item is ManualProjectItem => "Saved project",
            "StateLabel" when item is ProjectItem => "Existing task",
            "EnvironmentLabel" when item is ManualProjectItem manual => manual.Environment,
            "EnvironmentLabel" when item is ProjectItem => "Continue in the same project",
            "DetailsVisible" when item is ManualProjectItem or ProjectItem => SelectionKey(item) == _expandedProject,
            "DetailsLabel" when item is ManualProjectItem or ProjectItem => SelectionKey(item) == _expandedProject ? "Hide details" : "Details",
            "CardRow" when item is TemplateItem template => template.Index / StartTemplateColumnCount(),
            "CardColumn" when item is TemplateItem template => template.Index % StartTemplateColumnCount(),
            "Selected" when item is TemplateItem template => template.ProviderId == _selectedTemplateProviderId && template.TemplateId == _selectedTemplateId,
            "SelectLabel" when item is TemplateItem template => template.ProviderId == _selectedTemplateProviderId && template.TemplateId == _selectedTemplateId ? "Selected" : "Select template",
            _ => null
        };
        return value is not null;
    }

    private int StartTemplateColumnCount()
    {
        var content = _startWidth - (_startWidth <= 520 ? 0 : _startWidth <= 850 ? 72 : 210);
        var available = content - (_startWidth <= 520 ? 32 : content <= 900 ? 56 : 76);
        if (_startSurface == "new" && content > 660) available -= 24 + (content <= 900 ? 240 : 290);
        return available >= 500 ? 2 : 1;
    }

    private string StartTemplateReadiness()
    {
        if (!_templateCatalogAvailable) return "Template discovery is unavailable. Refresh to check the installed providers.";
        var option = _templateOptions.SingleOrDefault(IsSelectedTemplate);
        if (option is null) return "Select a template to inspect its required toolchains.";
        if (option.CanCreate) return option.Template.Requirements.Count == 0
            ? "This template declares no required compiler or runtime. Destination access is checked before creation."
            : "The provider reports the required toolchains ready. Destination access is checked before creation.";
        return string.Join("\n", option.Readiness.Where(item => item.State != HavenOS.Apps.Dev.DeveloperToolchainState.Ready)
            .Select(item => item.Message ?? item.RequirementId + ": " + item.State));
    }

    private static bool IsStartCommand(string command) => command is "dev.start.projects" or "dev.start.new" or
        "dev.start.open" or "dev.start.extension" or "dev.start.details" or "dev.start.diagnostics" or
        "dev.start.template-filters" or "dev.start.browse-folder";

    private bool IsStartActionAvailable(string command) => !_manualBusy && !_templateBusy && !_startFolderPicking && (command switch
    {
        "dev.start.details" => MergeProjectItems().Length > 0,
        "dev.start.browse-folder" => TopLevel.GetTopLevel(this)?.StorageProvider.CanPickFolder == true,
        _ => IsStartCommand(command)
    });

    private ValueTask DispatchStartActionAsync(string command, object? parameter, CancellationToken token)
    {
        if (command == "dev.start.browse-folder") return BrowseStartFolderAsync(token);
        long generation;
        lock (_gate)
        {
            generation = _generation;
            if (command == "dev.start.details" && (parameter is not string key || !MergeProjectItems().Any(item => SelectionKey(item) == key)))
                throw new InvalidOperationException("Choose a project from the current filtered list.");
        }
        _work.RunSynchronous(original =>
        {
            original.BindPublicationGuard(() => IsCurrent(generation)); original.DemandPublication(); token.ThrowIfCancellationRequested();
            lock (_gate)
            {
                if (command is "dev.start.projects" or "dev.start.new" or "dev.start.open" or "dev.start.extension")
                    _setupNavigationRevision = checked(_setupNavigationRevision + 1);
                switch (command)
                {
                    case "dev.start.projects": _startSurface = "projects"; break;
                    case "dev.start.new": _startSurface = "new"; _setupInputRevision = checked(_setupInputRevision + 1); break;
                    case "dev.start.open": _startSurface = "open"; break;
                    case "dev.start.extension": _startSurface = "extension"; _setupInputRevision = checked(_setupInputRevision + 1); break;
                    case "dev.start.diagnostics": _showStartDiagnostics = !_showStartDiagnostics; break;
                    case "dev.start.template-filters": _showTemplateFilters = !_showTemplateFilters; break;
                    case "dev.start.details":
                        if (parameter is not string key || !MergeProjectItems().Any(item => SelectionKey(item) == key))
                            throw new InvalidOperationException("Choose a project from the current filtered list.");
                        _expandedProject = _expandedProject == key ? null : key;
                        break;
                }
            }
            NotifyOriginal(original);
        });
        return ValueTask.CompletedTask;
    }

    private ValueTask BrowseStartFolderAsync(CancellationToken caller)
    {
        long generation, revision;
        lock (_gate) { generation = _generation; revision = _templateInputRevision; _startFolderPicking = true; }
        try { return new(_work.RunAsync(async original =>
        {
            original.BindPublicationGuard(() => IsCurrent(generation));
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(original.Token, caller, _connectionLifetime, _windowLifetime);
            try
            {
                NotifyOriginal(original);
                var readiness = await ObserveOriginalHomeAsync(original, linked.Token);
                if (readiness.State != CuiSceneAvailabilityState.Ready)
                { await PublishAsync(original, () => _manualStatus = readiness.Message); return; }
                var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
                if (storage?.CanPickFolder != true) throw new NotSupportedException("The native folder picker is unavailable. Type an existing project folder instead.");
                var selected = await original.AwaitAsync(InvokeOriginalSource(() => storage.OpenFolderPickerAsync(
                    new FolderPickerOpenOptions { Title = "Choose a project folder", AllowMultiple = false })));
                string path;
                try
                {
                    if (selected.Count == 0) return;
                    if (selected.Count != 1) throw new InvalidOperationException("Choose one project folder.");
                    path = selected[0].TryGetLocalPath() ?? throw new NotSupportedException("Choose a local folder for this workspace.");
                }
                finally { foreach (var folder in selected) folder.Dispose(); }
                var after = await ObserveOriginalHomeAsync(original, linked.Token);
                if (after.State != CuiSceneAvailabilityState.Ready)
                { await PublishAsync(original, () => _manualStatus = after.Message); return; }
                await PublishAsync(original, () =>
                {
                    if (_templateInputRevision != revision) return; // Preserve a newer typed destination.
                    if (_startSurface == "new")
                    {
                        // A285: on New project the picker selects the PARENT location; the
                        // project's own child folder is derived from the entered name.
                        _templateDestinationParent = path;
                        DeriveTemplateFolderLocked();
                    }
                    else
                    {
                        _manualFolderPath = path;
                        _templateProjectName = Path.GetFileName(Path.TrimEndingDirectorySeparator(path));
                        _templateDestinationParent = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(path)) ?? "";
                        _templateInputRevision = checked(_templateInputRevision + 1); _templateReview = null;
                        _templateReviewStatus = "The folder changed. Review the exact template files again before creation.";
                    }
                });
            }
            finally
            {
                // Retire private picker state even when the view was withdrawn. Publishing into
                // a newer activation is forbidden; the original picker itself is still joined.
                lock (_gate) _startFolderPicking = false;
                if (original.IsPublicationCurrent) await PublishAsync(original, () => { });
            }
        })); }
        catch { lock (_gate) _startFolderPicking = false; throw; }
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        if (!e.WidthChanged || e.NewSize.Width <= 0) return;
        long generation;
        lock (_gate) { _startWidth = e.NewSize.Width; if (!_active || _work.IsRetiring) return; generation = _generation; }
        _work.RunSynchronous(original =>
        {
            original.BindPublicationGuard(() => IsCurrent(generation));
            if (original.IsPublicationCurrent) NotifyOriginal(original);
        });
    }
}

/// <summary>The same bounded packaged artwork and canonical glyphs used by Home. This supplies
/// only image resolution; the Dev compiled document and original scene owner remain unchanged.</summary>
internal static class DeveloperBuildCuiControls
{
    internal static CuiControlRegistry Create() => HomeAppsCuiDocument.CreateControlRegistry();
}
