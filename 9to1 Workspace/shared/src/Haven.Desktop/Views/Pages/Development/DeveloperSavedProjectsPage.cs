using System.Collections.Generic;
using System.Collections.Immutable;
using System.ComponentModel;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using CakeOS.Cui;
using CakeOS.Cui.Runtime;
using Haven.Application;
using Haven.Desktop.Services;
using Haven.Desktop.ViewModels;
using HavenOS.Apps.Dev;
using HavenOS.Apps.Spaces.Development;

namespace Haven.Desktop.Views.Pages.Development;

/// <summary>Native existing-reference picker. Borrowed Home readiness and Task/Dev services
/// remain global owners; this page owns only its original metadata/open tasks and CUI host.
/// It creates no task, project, actor, permission, provider or Ready/frame certificate.</summary>
public sealed class DeveloperSavedProjectsPage : UserControl, IActivatablePage, ICuiBindingContext,
    ICuiWritableBindingContext, ICuiRepeatItemBindingContext, ICuiActionDispatcher, ICuiActionAvailability, INotifyPropertyChanged,
    IDesktopOriginalRetirementParticipant, IDesktopOriginalRetirementJoinGuard, IAsyncDisposable
{
    private readonly SpaceDevelopmentReferenceCatalog? _catalog;
    private readonly SpaceRegistry? _spaces;
    private readonly ICuiSceneReadiness? _originalHomeReadiness;
    private readonly Action? _demandOriginalHomeJoin;
    private readonly Func<SpaceDeveloperView, Task>? _openExistingTask;
    private readonly Func<Guid, Task, Task>? _openExistingSpace;
    private readonly IDeveloperWorkspaceStore? _manualWorkspaces;
    private readonly Func<DeveloperProjectReference, Task>? _openExistingManual;
    private readonly Func<string, CancellationToken, Task<DeveloperProjectReference>>? _createManualProject;
    private readonly DeveloperProjectCatalog? _templateCatalog;
    private readonly DeveloperProjectTemplateMaterializer? _templateMaterializer;
    private readonly Func<string, DeveloperProjectTemplateMaterialization, CancellationToken, Task<DeveloperProjectReference>>? _registerTemplateProject;
    private readonly Func<DeveloperProjectReference, DeveloperProjectTemplateMaterialization, Task>? _openTemplateProject;
    private DeveloperProjectTemplateOption[] _templateOptions = [];
    private string _templateQuery = "";
    private string[] _templateProviderChoices = ["All providers"];
    private string?[] _templateProviderValues = [null];
    private string[] _templateProjectTypeChoices = ["All project types"];
    private string?[] _templateProjectTypeValues = [null];
    private string[] _templateLanguageChoices = ["All languages"];
    private string?[] _templateLanguageValues = [null];
    private string[] _templateFrameworkChoices = ["All frameworks"];
    private string?[] _templateFrameworkValues = [null];
    private string[] _templateRuntimeChoices = ["All target runtimes"];
    private string?[] _templateRuntimeValues = [null];
    private string[] _templatePlatformChoices = ["All platforms"];
    private string?[] _templatePlatformValues = [null];
    private string[] _templateSurfaceChoices = ["All surfaces"];
    private string?[] _templateSurfaceValues = [null];
    private string[] _templateOrganisationChoices = ["All organisation templates"];
    private string?[] _templateOrganisationValues = [null];
    private int _templateProviderIndex, _templateProjectTypeIndex, _templateLanguageIndex, _templateFrameworkIndex, _templateRuntimeIndex, _templatePlatformIndex,
        _templateSurfaceIndex, _templateOrganisationIndex, _templateReadinessIndex;
    private string _templateProjectName = "";
    private string _templateReviewStatus = "Select a provider template and review its exact files before creating a project.";
    private string _templateProjectStatus = "";
    private string _selectedTemplateId = "";
    private string _selectedTemplateProviderId = "";
    private long _templateInputRevision;
    private bool _templateBusy, _templateCatalogAvailable;
    private TemplateReview? _templateReview;
    private IReadOnlyList<ManualProjectItem> _manualRows = [];
    private readonly CancellationToken _connectionLifetime, _windowLifetime;
    private readonly DesktopOriginalWorkLifetime _work;
    private readonly object _gate = new();
    [ThreadStatic] private static List<DeveloperSavedProjectsPage>? _sourceCallbacks;
    private CuiSceneHost? _host;
    private Task? _hostClose, _detach;
    private SpaceDevelopmentReferencePage? _page;
    private bool _active, _metadataAvailable;
    private long _generation, _readSequence;
    private string _status = "Refresh to observe saved projects.";
    private string _manualFolderPath = "";
    private string _manualStatus = "";
    private string _retainedAttemptState = "";
    private bool _manualBusy, _manualMetadataAvailable;
    private string _manualCatalogStatus = "Refresh to observe saved manual project roots.";
    private string _projectQuery = "";
    private int _projectPage;
    private const int ProjectPageSize = 100;

    public DeveloperSavedProjectsPage(SpaceDevelopmentReferenceCatalog originalCatalog, SpaceRegistry originalSpaces,
        ICuiSceneReadiness originalHomeReadiness, Action demandOriginalHomeJoin,
        Func<SpaceDeveloperView, Task> openExistingTask, Func<Guid, Task, Task> openExistingSpace,
        IDeveloperWorkspaceStore? manualWorkspaces, Func<DeveloperProjectReference, Task>? openExistingManual,
        CancellationToken originalConnectionLifetime, CancellationToken originalWindowLifetime,
        Action<DeveloperSavedProjectsPage> retainActualPartial,
        Func<string, CancellationToken, Task<DeveloperProjectReference>>? createManualProject = null,
        DeveloperProjectCatalog? templateCatalog = null,
        DeveloperProjectTemplateMaterializer? templateMaterializer = null,
        Func<string, DeveloperProjectTemplateMaterialization, CancellationToken, Task<DeveloperProjectReference>>? registerTemplateProject = null,
        Func<DeveloperProjectReference, DeveloperProjectTemplateMaterialization, Task>? openTemplateProject = null)
    {
        ArgumentNullException.ThrowIfNull(originalCatalog); ArgumentNullException.ThrowIfNull(originalSpaces);
        ArgumentNullException.ThrowIfNull(originalHomeReadiness); ArgumentNullException.ThrowIfNull(demandOriginalHomeJoin);
        ArgumentNullException.ThrowIfNull(openExistingTask); ArgumentNullException.ThrowIfNull(openExistingSpace);
        ArgumentNullException.ThrowIfNull(retainActualPartial);
        if (manualWorkspaces is null != (openExistingManual is null))
            throw new ArgumentException("Manual saved-project enumeration and open must be supplied together.");
        if ((templateCatalog is null) != (templateMaterializer is null) ||
            (templateCatalog is null) != (registerTemplateProject is null) ||
            (templateCatalog is null) != (openTemplateProject is null))
            throw new ArgumentException("Template discovery, materialization, registration and open must be supplied as one existing-owner route.");
        if (!originalConnectionLifetime.CanBeCanceled || !originalWindowLifetime.CanBeCanceled)
            throw new ArgumentException("Retain the actual Home connection and native window lifetimes.");
        _catalog = originalCatalog; _spaces = originalSpaces; _originalHomeReadiness = originalHomeReadiness;
        _demandOriginalHomeJoin = demandOriginalHomeJoin; _openExistingTask = openExistingTask; _openExistingSpace = openExistingSpace;
        _manualWorkspaces = manualWorkspaces; _openExistingManual = openExistingManual; _createManualProject = createManualProject;
        _templateCatalog = templateCatalog; _templateMaterializer = templateMaterializer;
        _registerTemplateProject = registerTemplateProject; _openTemplateProject = openTemplateProject;
        _connectionLifetime = originalConnectionLifetime; _windowLifetime = originalWindowLifetime;
        _work = new(StopOriginalPresentationAsync, DetachOriginalPresentationAsync);
        retainActualPartial(this); // Original shell captures this SAME page before any host/document/publication.
        _work.DemandAdmission();
        _host = new CuiSceneHost();
        _work.DemandAdmission();
        Document = DeveloperLandingCuiDocument.Load();
        AutomationProperties.SetAutomationId(this, "DeveloperSavedProjects");
        AutomationProperties.SetName(this, "Saved development projects and existing tasks");
        Content = _host;
    }

    private DeveloperSavedProjectsPage(string setupRequired, Action<DeveloperSavedProjectsPage> retainActualPartial)
    {
        ArgumentNullException.ThrowIfNull(retainActualPartial);
        _status = setupRequired;
        _work = new(StopOriginalPresentationAsync, DetachOriginalPresentationAsync);
        retainActualPartial(this);
        _work.DemandAdmission();
        AutomationProperties.SetAutomationId(this, "DeveloperSavedProjectsSetupRequired");
        AutomationProperties.SetName(this, "Dev setup required");
        Content = new StackPanel
        {
            Margin = new Thickness(24), Spacing = 12,
            Children =
            {
                new TextBlock { Text = "Dev setup required", FontFamily = new FontFamily("Montserrat"), FontSize = 24 },
                new TextBlock { Text = setupRequired, FontFamily = new FontFamily("Montserrat"), TextWrapping = TextWrapping.Wrap }
            }
        };
    }

    public static DeveloperSavedProjectsPage CreateSetupRequired(Action<DeveloperSavedProjectsPage> retainActualPartial) =>
        new("Connect Home and sign in to open saved development projects. Your existing tasks and project access will be checked when you open a project.", retainActualPartial);

    public CuiDocument? Document { get; }
    public new event PropertyChangedEventHandler? PropertyChanged;
    private bool IsCurrent(long generation)
    { lock (_gate) return _active && !_work.IsRetiring && _generation == generation && !_windowLifetime.IsCancellationRequested && !_connectionLifetime.IsCancellationRequested; }

    public Task ActivateAsync(CancellationToken cancellationToken)
    {
        long generation; lock (_gate) { _work.DemandAdmission(); _active = true; generation = ++_generation; }
        return _work.RunAsync(async original =>
        {
            original.BindPublicationGuard(() => IsCurrent(generation));
            if (_catalog is null) { original.DemandPublication(); return; } // Honest setup state, no producer or Ready fallback.
            using var scope = CancellationTokenSource.CreateLinkedTokenSource(original.Token, cancellationToken, _connectionLifetime, _windowLifetime);
            var refresh = RefreshOriginalAsync(original, scope.Token);
            try { await original.AwaitAsync(refresh).ConfigureAwait(false); }
            catch (Exception error) { original.Capture(refresh, error); }
            Task? show = null;
            var dispatcher = InvokeOriginalSource(() => Dispatcher.UIThread.InvokeAsync(() =>
            {
                original.DemandPublication();
                show = InvokeOriginalSource(() => _host!.ShowAsync(new("dev.saved-projects", "Dev", "Dev",
                    Document!, this, this, _originalHomeReadiness!) { IsPublicationCurrent = () => IsCurrent(generation) }, scope.Token));
            }).GetTask());
            try { await original.AwaitAsync(dispatcher).ConfigureAwait(false); }
            catch (Exception error) { original.Capture(dispatcher, error); }
            if (show is not null)
                try { await original.AwaitAsync(show).ConfigureAwait(false); }
                catch (Exception error) { original.Capture(show, error); }
            else original.Retain(new InvalidOperationException("No original saved-project host Show was acquired."));
            original.ThrowRetained();
        });
    }
    public void Deactivate() { lock (_gate) { _active = false; _generation++; _metadataAvailable = false; } }

    private async Task<CuiSceneAvailability> ObserveOriginalHomeAsync(DesktopOriginalWorkLifetime.Original original, CancellationToken token)
    {
        original.DemandPublication(); token.ThrowIfCancellationRequested();
        var actual = InvokeOriginalSource(() => _originalHomeReadiness!.CheckAsync(token).AsTask()); // SAME actual ValueTask once.
        var observed = await original.AwaitAsync(actual).ConfigureAwait(false);
        original.DemandPublication(); token.ThrowIfCancellationRequested();
        if (!Enum.IsDefined(observed.State) || string.IsNullOrWhiteSpace(observed.Code) || string.IsNullOrWhiteSpace(observed.Message))
            throw new InvalidDataException("The actual borrowed Home readiness returned invalid observation metadata.");
        return observed; // SAME actual observation; successful unrelated tasks never become Ready.
    }

    private async Task RefreshOriginalAsync(DesktopOriginalWorkLifetime.Original original, CancellationToken token)
    {
        var read = ReadAndPublishOriginalAsync(original, token);
        try { await original.AwaitAsync(read).ConfigureAwait(false); }
        catch (Exception error)
        {
            original.Capture(read, error);
            // Preserve the failed original and show an honest unavailable state if this
            // page is still current. The host continues to consult actual Home readiness.
            Task? unavailable = null;
            try
            {
                unavailable = PublishAsync(original, () =>
                {
                    _page = null; _manualRows = []; _metadataAvailable = false; _manualMetadataAvailable = false;
                    _status = "Saved projects could not be read. Refresh to try again.";
                });
                await original.AwaitAsync(unavailable).ConfigureAwait(false);
            }
            catch (Exception publicationError) { original.Capture(unavailable, publicationError); }
            original.ThrowRetained();
        }
    }

    private async Task ReadAndPublishOriginalAsync(DesktopOriginalWorkLifetime.Original original, CancellationToken token)
    {
        long sequence; lock (_gate) sequence = ++_readSequence;
        await PublishAsync(original, () => { _page = null; _manualRows = []; _metadataAvailable = false; _manualMetadataAvailable = false; _templateCatalogAvailable = false; _templateOptions = []; _templateReview = null; _projectPage = 0; _manualCatalogStatus = "Reading saved manual project roots…"; _status = "Reading saved projects…"; }).ConfigureAwait(false);
        var before = await ObserveOriginalHomeAsync(original, token).ConfigureAwait(false);
        if (before.State != CuiSceneAvailabilityState.Ready)
        { await PublishAsync(original, () => _status = before.Message).ConfigureAwait(false); return; }
        var stored = await original.AwaitAsync(InvokeOriginalSource(() => _spaces!.ReadExistingPageAsync(
            SpaceDevelopmentReferenceCatalog.MaximumSelectedSpaces, token))).ConfigureAwait(false);
        var page = await original.AwaitAsync(InvokeOriginalSource(() => _catalog!.ReadAsync(stored.Select(space => space.Id).ToArray(),
            SpaceDevelopmentReferenceCatalog.MaximumSavedReferences, token, OwnNestedOriginalSource))).ConfigureAwait(false);
        // Manual (no-AI) saved projects: real workspace-store identity, resolved by the same Dev
        // service when opened. This enumerates no Task/model; it grants no READ/execution authority.
        var manualRows = Array.Empty<ManualProjectItem>(); var manualAvailable = false;
        var manualCoverage = "Saved manual project enumeration is unavailable in this host.";
        if (_manualWorkspaces is not null)
        {
            var listed = await original.AwaitAsync(InvokeOriginalSource(() => _manualWorkspaces.ListAsync(token))).ConfigureAwait(false);
            if (listed.Succeeded && listed.Value is not null)
            {
                var choices = DeveloperSavedProjectChoices.Build(listed.Value, token);
                if (choices.Succeeded && choices.Value is not null)
                {
                    manualRows = choices.Value.Select(choice => new ManualProjectItem(choice.SelectionKey, choice.Title,
                        choice.Summary, "Manual open — no AI task or model required", choice.RootSummary, choice.Reference, true)).ToArray();
                    manualAvailable = true; manualCoverage = $"{manualRows.Length} saved manual project/root/repository choices. Opening rechecks current source access.";
                }
                else manualCoverage = "Saved manual project roots are unavailable: " + choices.Error?.Message;
            }
            else manualCoverage = "Saved manual project roots are unavailable: " + (listed.Error?.Message ?? "The store did not return a complete census.");
        }
        var templates = Array.Empty<DeveloperProjectTemplateOption>();
        var templateAvailable = false;
        if (_templateCatalog is not null)
        {
            var result = await original.AwaitAsync(InvokeOriginalSource(() =>
                _templateCatalog.SearchAsync(new(), token))).ConfigureAwait(false);
            if (result.Succeeded && result.Value is not null)
            { templates = result.Value.ToArray(); templateAvailable = true; }
        }
        var after = await ObserveOriginalHomeAsync(original, token).ConfigureAwait(false);
        await PublishAsync(original, () =>
        {
            if (_readSequence != sequence) return;
            if (after.State != CuiSceneAvailabilityState.Ready) { _status = after.Message; _manualRows = []; _manualMetadataAvailable = false; _templateOptions = []; _templateCatalogAvailable = false; return; }
            _page = page; _manualRows = manualRows; _metadataAvailable = true; _manualMetadataAvailable = manualAvailable;
            RebuildTemplateFacetsLocked(templates);
            _templateOptions = templates; _templateCatalogAvailable = templateAvailable; _manualCatalogStatus = manualCoverage;
            if (!templates.Any(IsSelectedTemplate)) ClearTemplateSelectionLocked("Template catalogue refreshed. Select and review a current provider template.");
            else _templateReview = null;
            _templateReviewStatus = templateAvailable
                ? "Select a provider template and review its exact files before creating a project."
                : "The project-template provider catalogue is unavailable. Refresh before creating from a template.";
            _status = manualRows.Length > 0
                ? "Choose an existing task, or choose a saved project root and repository to open manually. Each open checks the current saved reference again."
                : page.Rows.Count > 0 ? "Choose an existing task to open its saved project. Each open checks the current saved reference again."
                : manualAvailable ? "No saved project references were found. Open an existing project folder as a manual project below."
                : "Saved manual projects are unavailable. See the coverage message and refresh before creating or opening a manual project.";
        }).ConfigureAwait(false);
    }

    public bool TryGetValue(string path, out object? value)
    {
        lock (_gate)
        {
            value = path switch
            {
                "Projects" => MergeProjectItems(),
                "Coverage" => (_page?.Coverage ?? "Connect Home and refresh to view your saved projects.") + "\n" + _manualCatalogStatus,
                "ProjectQuery" => _projectQuery, "ProjectFilterStatus" => ProjectFilterStatus(),
                "CanPreviousProjectPage" => IsActionAvailable("dev.references.previous-projects"),
                "CanNextProjectPage" => IsActionAvailable("dev.references.next-projects"),
                "Status" => _status,
                "CanRefresh" => IsActionAvailable("dev.references.refresh"),
                "CanOpenSpace" => IsActionAvailable("dev.references.open-space"),
                "CanCreateManual" => IsActionAvailable("dev.references.create-manual-project"),
                "ManualFolderPath" => _manualFolderPath,
                "ManualStatus" => _manualStatus,
                "RetainedAttempt" => _retainedAttemptState,
                "ManualBusy" => _manualBusy,
                "Templates" => FilteredTemplateItems(),
                "TemplateQuery" => _templateQuery,
                "TemplateFilterStatus" => TemplateFilterStatus(),
                "TemplateProviderOptions" => _templateProviderChoices.ToArray(),
                "TemplateProviderIndex" => _templateProviderIndex,
                "TemplateProjectTypeOptions" => _templateProjectTypeChoices.ToArray(),
                "TemplateProjectTypeIndex" => _templateProjectTypeIndex,
                "TemplateLanguageOptions" => _templateLanguageChoices.ToArray(),
                "TemplateLanguageIndex" => _templateLanguageIndex,
                "TemplateFrameworkOptions" => _templateFrameworkChoices.ToArray(),
                "TemplateFrameworkIndex" => _templateFrameworkIndex,
                "TemplateRuntimeOptions" => _templateRuntimeChoices.ToArray(),
                "TemplateRuntimeIndex" => _templateRuntimeIndex,
                "TemplatePlatformOptions" => _templatePlatformChoices.ToArray(),
                "TemplatePlatformIndex" => _templatePlatformIndex,
                "TemplateSurfaceOptions" => _templateSurfaceChoices.ToArray(),
                "TemplateSurfaceIndex" => _templateSurfaceIndex,
                "TemplateOrganisationOptions" => _templateOrganisationChoices.ToArray(),
                "TemplateOrganisationIndex" => _templateOrganisationIndex,
                "TemplateReadinessOptions" => new[] { "Any toolchain readiness", "Ready to create", "Required toolchain unavailable" },
                "TemplateReadinessIndex" => _templateReadinessIndex,
                "TemplateProjectName" => _templateProjectName,
                "SelectedTemplate" => SelectedTemplateStatus(),
                "CanReviewTemplate" => IsActionAvailable("dev.templates.review"),
                "CanCreateTemplateProject" => IsActionAvailable("dev.templates.create"),
                "TemplateReviewStatus" => TemplateReviewStatus(),
                "TemplateFiles" => _templateReview is { } review ? review.Files : Array.Empty<TemplateFileReviewItem>(),
                "TemplateProjectStatus" => _templateProjectStatus,
                _ => null
            };
        }
        return path is "Projects" or "Coverage" or "Status" or "CanRefresh" or "CanOpenSpace" or "CanCreateManual" or
            "ManualFolderPath" or "ManualStatus" or "RetainedAttempt" or "ManualBusy" or "ProjectQuery" or "ProjectFilterStatus" or "CanPreviousProjectPage" or "CanNextProjectPage" or
            "Templates" or "TemplateQuery" or "TemplateFilterStatus" or
            "TemplateProviderOptions" or "TemplateProviderIndex" or "TemplateProjectTypeOptions" or "TemplateProjectTypeIndex" or
            "TemplateLanguageOptions" or "TemplateLanguageIndex" or "TemplateFrameworkOptions" or "TemplateFrameworkIndex" or
            "TemplateRuntimeOptions" or "TemplateRuntimeIndex" or "TemplatePlatformOptions" or "TemplatePlatformIndex" or
            "TemplateSurfaceOptions" or "TemplateSurfaceIndex" or "TemplateOrganisationOptions" or "TemplateOrganisationIndex" or
            "TemplateReadinessOptions" or "TemplateReadinessIndex" or "TemplateProjectName" or "SelectedTemplate" or
            "CanReviewTemplate" or "CanCreateTemplateProject" or "TemplateReviewStatus" or "TemplateFiles" or "TemplateProjectStatus";
    }

    /// <summary>Writable metadata inputs from the canonical landing document. A typed folder path
    /// is only a candidate; the existing setup adapter still performs actual Files selection and
    /// Home admission. Filter writeback changes the visible page and withdraws hidden row keys.</summary>
    public bool TrySetValue(string path, object? value)
    {
        var indexCount = path switch
        {
            "TemplateProviderIndex" => _templateProviderChoices.Length,
            "TemplateProjectTypeIndex" => _templateProjectTypeChoices.Length,
            "TemplateLanguageIndex" => _templateLanguageChoices.Length,
            "TemplateFrameworkIndex" => _templateFrameworkChoices.Length,
            "TemplateRuntimeIndex" => _templateRuntimeChoices.Length,
            "TemplatePlatformIndex" => _templatePlatformChoices.Length,
            "TemplateSurfaceIndex" => _templateSurfaceChoices.Length,
            "TemplateOrganisationIndex" => _templateOrganisationChoices.Length,
            "TemplateReadinessIndex" => 3,
            _ => 0
        };
        var stringInput = value is string && (path is "ManualFolderPath" or "ProjectQuery" or "TemplateQuery");
        var filterInput = value is int selectedIndex && indexCount > 0 && selectedIndex >= 0 && selectedIndex < indexCount;
        if (!stringInput && !filterInput) return false;
        var text = value as string;
        var index = value is int number ? number : 0;
        long generation; lock (_gate) generation = _generation;
        _work.RunSynchronous(original =>
        {
            original.BindPublicationGuard(() => IsCurrent(generation)); original.DemandPublication();
            lock (_gate)
            {
                if (path == "ManualFolderPath")
                {
                    _manualFolderPath = text!;
                    _templateProjectName = Path.GetFileName(Path.TrimEndingDirectorySeparator(text!));
                    _templateInputRevision = checked(_templateInputRevision + 1);
                    _templateReview = null;
                    _templateReviewStatus = "The target folder changed. Review the exact files for this folder again.";
                }
                else if (path == "ProjectQuery") { _projectQuery = text!; _projectPage = 0; }
                else if (path == "TemplateQuery")
                {
                    _templateQuery = text!;
                    _templateInputRevision = checked(_templateInputRevision + 1);
                    ClearTemplateSelectionLocked("Template input changed. Select and review the current filtered template again.");
                }
                else
                {
                    _templateInputRevision = checked(_templateInputRevision + 1);
                    SetTemplateFilterIndexLocked(path, index);
                    ClearTemplateSelectionLocked("Template filters changed. Select and review a template from the current results.");
                }
            }
            NotifyOriginal(original);
        });
        return true;
    }
    bool ICuiRepeatItemBindingContext.TryGetItemValue(object item, string path, out object? value)
    {
        value = null;
        var matched = false;
        if (item is ProjectItem projectItem)
        {
            (matched, value) = path switch
            {
                nameof(ProjectItem.SelectionKey) => (true, (object?)projectItem.SelectionKey),
                nameof(ProjectItem.Title) => (true, projectItem.Title),
                nameof(ProjectItem.Summary) => (true, projectItem.Summary),
                nameof(ProjectItem.TaskLabel) => (true, projectItem.TaskLabel),
                nameof(ProjectItem.CheckpointLabel) => (true, projectItem.CheckpointLabel),
                nameof(ProjectItem.CanOpen) => (true, projectItem.CanOpen),
                nameof(ProjectItem.OpenLabel) => (true, projectItem.OpenLabel),
                nameof(ProjectItem.OpenAccessibleName) => (true, projectItem.OpenAccessibleName),
                _ => (false, (object?)null)
            };
        }
        else if (item is ManualProjectItem manualItem)
        {
            (matched, value) = path switch
            {
                nameof(ManualProjectItem.SelectionKey) => (true, (object?)manualItem.SelectionKey),
                nameof(ManualProjectItem.Title) => (true, manualItem.Title),
                nameof(ManualProjectItem.Summary) => (true, manualItem.Summary),
                nameof(ManualProjectItem.TaskLabel) => (true, manualItem.TaskLabel),
                nameof(ManualProjectItem.CheckpointLabel) => (true, manualItem.CheckpointLabel),
                nameof(ManualProjectItem.CanOpen) => (true, manualItem.CanOpen),
                nameof(ManualProjectItem.OpenLabel) => (true, manualItem.OpenLabel),
                nameof(ManualProjectItem.OpenAccessibleName) => (true, manualItem.OpenAccessibleName),
                _ => (false, (object?)null)
            };
        }
        else if (item is TemplateItem templateItem)
        {
            (matched, value) = path switch
            {
                nameof(TemplateItem.SelectionKey) => (true, (object?)templateItem.SelectionKey),
                nameof(TemplateItem.TemplateId) => (true, (object?)templateItem.TemplateId),
                nameof(TemplateItem.ProviderId) => (true, templateItem.ProviderId),
                nameof(TemplateItem.DisplayName) => (true, templateItem.DisplayName),
                nameof(TemplateItem.Summary) => (true, templateItem.Summary),
                _ => (false, (object?)null)
            };
        }
        else if (item is TemplateFileReviewItem fileItem)
        {
            (matched, value) = path switch
            {
                nameof(TemplateFileReviewItem.RelativePath) => (true, (object?)fileItem.RelativePath),
                nameof(TemplateFileReviewItem.Metadata) => (true, fileItem.Metadata),
                nameof(TemplateFileReviewItem.Text) => (true, fileItem.Text),
                _ => (false, (object?)null)
            };
        }
        return matched;
    }
    bool ICuiRepeatItemBindingContext.TrySetItemValue(object item, string path, object? value) => false;
    public bool? IsActionAvailable(string command)
    {
        lock (_gate)
        {
            if (!_active || _work.IsRetiring || _catalog is null || !IsCurrent(_generation)) return false;
            return command switch
            {
                "dev.references.refresh" => true,
                "dev.references.open-task" => _metadataAvailable && MergeProjectItems().Length > 0,
                "dev.references.previous-projects" => _metadataAvailable && _projectPage > 0,
                "dev.references.next-projects" => _metadataAvailable && (_projectPage + 1) * ProjectPageSize < FilteredProjectItems().Length,
                "dev.references.open-space" => _metadataAvailable && _page?.Rows.Select(row => row.SpaceId).Distinct().Count() == 1,
                "dev.references.create-manual-project" => _metadataAvailable && _manualMetadataAvailable && !_manualBusy && _createManualProject is not null && _openExistingManual is not null && !string.IsNullOrWhiteSpace(_manualFolderPath),
                "dev.templates.select" => _metadataAvailable && _templateCatalogAvailable && FilteredTemplateItems().Length > 0 && !_templateBusy,
                "dev.templates.review" => _metadataAvailable && _templateCatalogAvailable && !_templateBusy &&
                    _templateOptions.Any(option => IsSelectedTemplate(option) && CurrentTemplateQuery().Matches(option)) && SafeProjectName(_templateProjectName),
                "dev.templates.create" => _metadataAvailable && _templateCatalogAvailable && !_templateBusy && _registerTemplateProject is not null &&
                    _openTemplateProject is not null && !string.IsNullOrWhiteSpace(_manualFolderPath) && Path.IsPathFullyQualified(_manualFolderPath) && IsCurrentTemplateReview(),
                _ => false
            };
        }
    }
    public ValueTask DispatchAsync(string command, object? parameter, CancellationToken cancellationToken = default)
    {
        if (IsActionAvailable(command) != true) throw new InvalidOperationException("This saved-project action is unavailable.");
        if (command.StartsWith("dev.templates.", StringComparison.Ordinal))
            return DispatchTemplateActionAsync(command, parameter, cancellationToken);
        if (command is "dev.references.previous-projects" or "dev.references.next-projects") return ChangeProjectPage(command);
        long generation; SpaceDevelopmentReferenceRow? captured = null; DeveloperProjectReference? manualReference = null;
        lock (_gate)
        {
            generation = _generation;
            if (command == "dev.references.open-task")
            {
                var key = parameter as string;
                var selected = MergeProjectItems().SingleOrDefault(item => SelectionKey(item) == key);
                if (selected is ManualProjectItem manual) manualReference = manual.Reference;
                else if (selected is ProjectItem task) captured = task.Original;
                else throw new InvalidOperationException("Select an actual reference from the current filtered project page.");
            }
            if (command == "dev.references.open-space") captured = _page!.Rows[0];
        }
        return new(_work.RunAsync(async original =>
        {
            original.BindPublicationGuard(() => IsCurrent(generation));
            using var scope = CancellationTokenSource.CreateLinkedTokenSource(original.Token, cancellationToken, _connectionLifetime, _windowLifetime);
            if (command == "dev.references.refresh")
            { await RefreshOriginalAsync(original, scope.Token).ConfigureAwait(false); return; }
            var home = await ObserveOriginalHomeAsync(original, scope.Token).ConfigureAwait(false);
            if (home.State != CuiSceneAvailabilityState.Ready)
            { await PublishAsync(original, () => { _page = null; _metadataAvailable = false; _status = home.Message; }).ConfigureAwait(false); return; }
            original.DemandPublication();
            if (command == "dev.references.create-manual-project")
            {
                // The genuine typed folder selection is the live writable binding value carried by the
                // canonical landing document (folder Input -> ICuiWritableBindingContext.TrySetValue),
                // not a hidden host code-behind path.
                var root = _manualFolderPath;
                if (string.IsNullOrWhiteSpace(root))
                {
                    await PublishAsync(original, () => _manualStatus = "Select or type an actual existing Files folder to open as a manual project.").ConfigureAwait(false);
                    return;
                }
                await PublishAsync(original, () => { _manualBusy = true; _manualStatus = "Creating and registering the manual project…"; }).ConfigureAwait(false);
                try
                {
                    // Create a fresh canonical manual workspace/project from the genuine selected Files
                    // folder (real broker per-action decisions + setup journal), then open it through
                    // the SAME manual-open workbench path with no Task/model.
                    var created = InvokeOriginalSource(() => _createManualProject!(root, scope.Token));
                    var reference = await original.AwaitAsync(created).ConfigureAwait(false);
                    var opened = InvokeOriginalSource(() => _openExistingManual!(reference));
                    await original.AwaitAsync(opened).ConfigureAwait(false);
                    return;
                }
                catch (OperationCanceledException) { throw; } // Cancellation never replays effects or replaces setup/workspace ids.
                catch (Exception error)
                {
                    // Preserve the original fault and publish an honest recoverable state before the
                    // retained fault is surfaced once. No effect is replayed and no id is replaced.
                    original.Retain(error);
                    Task? status = null;
                    try
                    {
                        status = PublishAsync(original, () =>
                        {
                            _manualBusy = false;
                            if (error is DeveloperManualAttemptBlockedException blocked)
                            {
                                _retainedAttemptState = "Retained setup " + blocked.SetupId.ToString("D") +
                                    " has a partial/uncertain persisted effect (" + (blocked.State ?? "unknown") +
                                    ") and was not re-run or replaced. Inspect the same manual entry before creating another.";
                                _manualStatus = "The retained manual setup needs recovery inspection (setup " + blocked.SetupId.ToString("D") +
                                    ", " + (blocked.State ?? "unknown") + "). It was not re-run or replaced.";
                            }
                            else
                            {
                                _manualStatus = "The manual project could not be created: " + error.Message + " No project or workspace was published.";
                            }
                        });
                        await original.AwaitAsync(status).ConfigureAwait(false);
                    }
                    catch (Exception publicationError) { original.Capture(status, publicationError); }
                    original.ThrowRetained();
                    throw; // unreachable; the retained rethrow above propagates the original fault.
                }
            }
            if (command == "dev.references.open-task" && manualReference is { } manual)
            {
                var opened = InvokeOriginalSource(() => _openExistingManual!(manual));
                await original.AwaitAsync(opened).ConfigureAwait(false); // Opens the SAME compiled-CUI workbench with no Task/model.
                return;
            }
            var current = await original.AwaitAsync(InvokeOriginalSource(() => _catalog!.OpenAsync(captured!, scope.Token, OwnNestedOriginalSource))).ConfigureAwait(false);
            var after = await ObserveOriginalHomeAsync(original, scope.Token).ConfigureAwait(false);
            if (after.State != CuiSceneAvailabilityState.Ready)
            { await PublishAsync(original, () => { _page = null; _metadataAvailable = false; _status = after.Message; }).ConfigureAwait(false); return; }
            original.DemandPublication();
            if (command == "dev.references.open-task")
            {
                var opened = InvokeOriginalSource(() => _openExistingTask!(current));
                await original.AwaitAsync(opened).ConfigureAwait(false); // Borrowed shell joins its genuine existing Task frame acquisition.
                return;
            }
            // Open Space: supply the GENUINE admitted original action settlement to the host driver
            // unchanged. The external host driver awaits it; this included action never awaits its
            // encompassing navigation settlement. The settlement completes only when this real open
            // action resolves (success, cancellation or fault), never independently.
            var enqueued = InvokeOriginalSource(() => _openExistingSpace!(current.Space.Id, original.Task));
            await original.AwaitAsync(enqueued).ConfigureAwait(false);
        }));
    }

    private sealed record ProjectItem(SpaceDevelopmentReferenceRow Original, bool CanOpen)
    {
        public string SelectionKey => Original.SelectionKey;
        public string Title => Original.Title;
        public string Summary => Original.Summary;
        public string TaskLabel => Original.TaskLabel;
        public string CheckpointLabel => Original.CheckpointLabel;
        public string OpenLabel => "Open task and project";
        public string OpenAccessibleName => "Open the existing task and saved project " + Title;
    }
    /// <summary>A manual (no-AI) saved-project row: real workspace-store identity, no Task/link.
    /// It satisfies the same typed CUI members as a Task row so the shared compiled picker renders it.</summary>
    private sealed record ManualProjectItem(string SelectionKey, string Title, string Summary, string TaskLabel, string CheckpointLabel, DeveloperProjectReference Reference, bool CanOpen)
    {
        public string OpenLabel => "Open this root manually";
        public string OpenAccessibleName => "Open saved project " + Title + "; " + CheckpointLabel;
    }
    private sealed record ProjectDisplay(object Value, string Title, string Key, string SearchText);
    private object[] FilteredProjectItems()
    {
        if (!_metadataAvailable) return [];
        var items = new List<ProjectDisplay>();
        if (_page is not null)
            foreach (var row in _page.Rows) items.Add(new(new ProjectItem(row, true), row.Title, row.SelectionKey,
                row.Title + "\n" + row.Summary + "\n" + row.TaskLabel + "\n" + row.CheckpointLabel));
        if (_manualMetadataAvailable)
            foreach (var manual in _manualRows) items.Add(new(manual, manual.Title, manual.SelectionKey,
                manual.Title + "\n" + manual.Summary + "\n" + manual.CheckpointLabel));
        var query = _projectQuery.Trim();
        return items.Where(item => query.Length == 0 || item.SearchText.Contains(query, StringComparison.OrdinalIgnoreCase))
            .OrderBy(item => item.Title, StringComparer.OrdinalIgnoreCase).ThenBy(item => item.Key, StringComparer.Ordinal)
            .Select(item => item.Value).ToArray();
    }
    private object[] MergeProjectItems() => FilteredProjectItems().Skip(_projectPage * ProjectPageSize).Take(ProjectPageSize).ToArray();
    private static string SelectionKey(object item) => item switch { ProjectItem task => task.SelectionKey, ManualProjectItem manual => manual.SelectionKey, _ => "" };
    private string ProjectFilterStatus()
    {
        var count = FilteredProjectItems().Length;
        return count == 0 ? "No saved project references match this filter. Refresh coverage below distinguishes an empty list from an unavailable store."
            : $"Showing {_projectPage * ProjectPageSize + 1}–{Math.Min((long)count, (_projectPage + 1L) * ProjectPageSize)} of {count} project/root references.";
    }

    private TemplateItem[] FilteredTemplateItems()
    {
        if (!_templateCatalogAvailable) return [];
        var query = CurrentTemplateQuery();
        return _templateOptions.Where(query.Matches)
            .OrderBy(option => option.Template.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(option => option.Template.ProviderId, StringComparer.Ordinal)
            .ThenBy(option => option.Template.TemplateId, StringComparer.Ordinal)
            .Select(option => new TemplateItem(TemplateSelectionKey(option.Template), option.Template.ProviderId,
                option.Template.TemplateId, option.Template.DisplayName, TemplateSummary(option)))
            .ToArray();
    }

    private string TemplateFilterStatus()
    {
        if (!_templateCatalogAvailable) return "Project-template catalogue unavailable; refresh after connecting providers.";
        var visible = FilteredTemplateItems().Length;
        return visible == 0
            ? $"No templates match the current filters ({_templateOptions.Length} provider templates loaded)."
            : $"Showing {visible} of {_templateOptions.Length} templates from the current provider catalogue.";
    }

    private static string TemplateSummary(DeveloperProjectTemplateOption option)
    {
        var template = option.Template;
        var metadata = new List<string> { "Provider " + template.ProviderId, "Project type " + template.ProjectTypeId };
        if (template.TargetRuntimes is { Count: > 0 }) metadata.Add("Runtime " + string.Join(", ", template.TargetRuntimes));
        if (template.Platforms.Count > 0) metadata.Add("Platform " + string.Join(", ", template.Platforms));
        if (template.ProductSurfaces.Count > 0) metadata.Add("Surface " + string.Join(", ", template.ProductSurfaces));
        if (!string.IsNullOrWhiteSpace(template.OrganisationTemplateId)) metadata.Add("Organisation template " + template.OrganisationTemplateId);
        if (template.Languages.Count > 0) metadata.Add("Language " + string.Join(", ", template.Languages));
        metadata.Add(option.CanCreate ? "required toolchains ready" : "required toolchain unavailable");
        return string.Join(" · ", metadata);
    }

    private DeveloperProjectTemplateQuery CurrentTemplateQuery() => new(
        SearchText: _templateQuery,
        Platform: SelectedFacet(_templatePlatformValues, _templatePlatformIndex),
        ProductSurface: SelectedFacet(_templateSurfaceValues, _templateSurfaceIndex),
        ToolchainsReady: _templateReadinessIndex switch { 1 => true, 2 => false, _ => null },
        Language: SelectedFacet(_templateLanguageValues, _templateLanguageIndex),
        Framework: SelectedFacet(_templateFrameworkValues, _templateFrameworkIndex),
        ProjectType: SelectedFacet(_templateProjectTypeValues, _templateProjectTypeIndex),
        TargetRuntime: SelectedFacet(_templateRuntimeValues, _templateRuntimeIndex),
        ProviderId: SelectedFacet(_templateProviderValues, _templateProviderIndex),
        OrganisationTemplateId: SelectedFacet(_templateOrganisationValues, _templateOrganisationIndex));

    private static string? SelectedFacet(string?[] values, int index) =>
        index > 0 && index < values.Length ? values[index] : null;

    private void RebuildTemplateFacetsLocked(IReadOnlyList<DeveloperProjectTemplateOption> templates)
    {
        var provider = SelectedFacet(_templateProviderValues, _templateProviderIndex);
        var projectType = SelectedFacet(_templateProjectTypeValues, _templateProjectTypeIndex);
        var language = SelectedFacet(_templateLanguageValues, _templateLanguageIndex);
        var framework = SelectedFacet(_templateFrameworkValues, _templateFrameworkIndex);
        var runtime = SelectedFacet(_templateRuntimeValues, _templateRuntimeIndex);
        var platform = SelectedFacet(_templatePlatformValues, _templatePlatformIndex);
        var surface = SelectedFacet(_templateSurfaceValues, _templateSurfaceIndex);
        var organisation = SelectedFacet(_templateOrganisationValues, _templateOrganisationIndex);
        (_templateProviderChoices, _templateProviderValues) = BuildTemplateFacet(templates.Select(option => option.Template.ProviderId), "All providers");
        (_templateProjectTypeChoices, _templateProjectTypeValues) = BuildTemplateFacet(templates.Select(option => option.Template.ProjectTypeId), "All project types");
        (_templateLanguageChoices, _templateLanguageValues) = BuildTemplateFacet(templates.SelectMany(option => option.Template.Languages), "All languages");
        (_templateFrameworkChoices, _templateFrameworkValues) = BuildTemplateFacet(templates.SelectMany(option => option.Template.Frameworks), "All frameworks");
        (_templateRuntimeChoices, _templateRuntimeValues) = BuildTemplateFacet(
            templates.SelectMany(option => option.Template.TargetRuntimes ?? Array.Empty<string>()), "All target runtimes");
        (_templatePlatformChoices, _templatePlatformValues) = BuildTemplateFacet(templates.SelectMany(option => option.Template.Platforms), "All platforms");
        (_templateSurfaceChoices, _templateSurfaceValues) = BuildTemplateFacet(templates.SelectMany(option => option.Template.ProductSurfaces), "All surfaces");
        (_templateOrganisationChoices, _templateOrganisationValues) = BuildTemplateFacet(
            templates.Select(option => option.Template.OrganisationTemplateId), "All organisation templates");
        _templateProviderIndex = FindFacetIndex(_templateProviderValues, provider);
        _templateProjectTypeIndex = FindFacetIndex(_templateProjectTypeValues, projectType);
        _templateLanguageIndex = FindFacetIndex(_templateLanguageValues, language);
        _templateFrameworkIndex = FindFacetIndex(_templateFrameworkValues, framework);
        _templateRuntimeIndex = FindFacetIndex(_templateRuntimeValues, runtime);
        _templatePlatformIndex = FindFacetIndex(_templatePlatformValues, platform);
        _templateSurfaceIndex = FindFacetIndex(_templateSurfaceValues, surface);
        _templateOrganisationIndex = FindFacetIndex(_templateOrganisationValues, organisation);
    }

    private static (string[] Choices, string?[] Values) BuildTemplateFacet(IEnumerable<string?> source, string allLabel)
    {
        var values = source.Where(value => !string.IsNullOrWhiteSpace(value)).Cast<string>()
            .Distinct(StringComparer.Ordinal).OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToArray();
        return ([allLabel, .. values], [null, .. values]);
    }

    private static int FindFacetIndex(string?[] values, string? selected)
    {
        if (selected is null) return 0;
        var index = Array.FindIndex(values, value => StringComparer.Ordinal.Equals(value, selected));
        return index < 0 ? 0 : index;
    }

    private void SetTemplateFilterIndexLocked(string path, int index)
    {
        switch (path)
        {
            case "TemplateProviderIndex": _templateProviderIndex = index; break;
            case "TemplateProjectTypeIndex": _templateProjectTypeIndex = index; break;
            case "TemplateLanguageIndex": _templateLanguageIndex = index; break;
            case "TemplateFrameworkIndex": _templateFrameworkIndex = index; break;
            case "TemplateRuntimeIndex": _templateRuntimeIndex = index; break;
            case "TemplatePlatformIndex": _templatePlatformIndex = index; break;
            case "TemplateSurfaceIndex": _templateSurfaceIndex = index; break;
            case "TemplateOrganisationIndex": _templateOrganisationIndex = index; break;
            case "TemplateReadinessIndex": _templateReadinessIndex = index; break;
            default: throw new InvalidOperationException("The project-template filter is not a writable facet.");
        }
    }

    private void ClearTemplateSelectionLocked(string status)
    {
        _selectedTemplateId = "";
        _selectedTemplateProviderId = "";
        _templateReview = null;
        _templateReviewStatus = status;
    }

    private bool IsSelectedTemplate(DeveloperProjectTemplateOption option) =>
        StringComparer.Ordinal.Equals(option.Template.ProviderId, _selectedTemplateProviderId) &&
        StringComparer.Ordinal.Equals(option.Template.TemplateId, _selectedTemplateId);

    private static string TemplateSelectionKey(DeveloperProjectTemplate template) =>
        template.ProviderId.Length.ToString(System.Globalization.CultureInfo.InvariantCulture) + ":" +
        template.ProviderId + template.TemplateId;

    private string SelectedTemplateStatus()
    {
        var option = _templateOptions.SingleOrDefault(IsSelectedTemplate);
        return option is null ? "No project template selected."
            : option.Template.DisplayName + " · provider " + option.Template.ProviderId +
              (option.CanCreate ? " · ready" : " · required toolchain unavailable");
    }

    private string TemplateReviewStatus()
    {
        if (!_templateCatalogAvailable)
            return "Template catalogue unavailable. Refresh after connecting the project-template provider.";
        if (_templateReview is not { } review) return _templateReviewStatus;
        if (!IsCurrentTemplateReview()) return "The template review is stale. Review the current template, name and folder again.";
        return $"Reviewed {review.Files.Length} exact files ({review.Materialization.TotalBytes} UTF-8 bytes) for '{review.ProjectName}'. Each file will receive its own Home CREATE approval; existing files are never overwritten.";
    }

    private bool IsCurrentTemplateReview() => _templateReview is { } review &&
        _templateMaterializer?.IsIssued(review.Materialization) == true &&
        review.InputRevision == _templateInputRevision &&
        StringComparer.Ordinal.Equals(review.TemplateId, _selectedTemplateId) &&
        StringComparer.Ordinal.Equals(review.ProviderId, _selectedTemplateProviderId) &&
        StringComparer.Ordinal.Equals(review.ProjectName, _templateProjectName) &&
        StringComparer.Ordinal.Equals(review.FolderPath, _manualFolderPath) &&
        review.Files.Length > 0 && review.Files.All(file => file.RelativePath.Length > 0 && !file.RelativePath.Contains('/') && !file.RelativePath.Contains('\\'));

    private static bool SafeProjectName(string? name) => !string.IsNullOrWhiteSpace(name) && name.Length <= 120 &&
        name == name.Trim() && name is not ("." or "..") && !name.EndsWith('.') && !name.EndsWith(' ') &&
        name.All(character => !char.IsControl(character) && character is not ('/' or '\\' or ':' or '<' or '>' or '"' or '|' or '?' or '*' or '\0'));

    private ValueTask DispatchTemplateActionAsync(string command, object? parameter, CancellationToken cancellationToken)
    {
        if (command == "dev.templates.select")
        {
            var selectedOption = parameter is string selectionKey
                ? _templateOptions.SingleOrDefault(option =>
                    StringComparer.Ordinal.Equals(TemplateSelectionKey(option.Template), selectionKey) &&
                    CurrentTemplateQuery().Matches(option))
                : null;
            if (selectedOption is null)
                throw new InvalidOperationException("Select a template from the current provider catalogue.");
            long generation; lock (_gate) generation = _generation;
            _work.RunSynchronous(original =>
            {
                original.BindPublicationGuard(() => IsCurrent(generation)); original.DemandPublication();
                lock (_gate)
                {
                    _selectedTemplateId = selectedOption.Template.TemplateId;
                    _selectedTemplateProviderId = selectedOption.Template.ProviderId;
                    _templateReview = null;
                    _templateInputRevision = checked(_templateInputRevision + 1);
                    _templateReviewStatus = "Template selection changed. Review its exact files before creation.";
                }
                NotifyOriginal(original);
            });
            return ValueTask.CompletedTask;
        }

        if (command is not ("dev.templates.review" or "dev.templates.create"))
            throw new InvalidOperationException("Unknown project-template action.");
        long actionGeneration;
        DeveloperProjectTemplateOption? selected;
        TemplateReview? reviewed;
        string projectName, folderPath;
        long inputRevision;
        lock (_gate)
        {
            actionGeneration = _generation;
            selected = _templateOptions.SingleOrDefault(option => IsSelectedTemplate(option) && CurrentTemplateQuery().Matches(option));
            reviewed = _templateReview;
            projectName = _templateProjectName;
            folderPath = _manualFolderPath;
            inputRevision = _templateInputRevision;
            if (command == "dev.templates.create")
            {
                if (!IsCurrentTemplateReview()) throw new InvalidOperationException("Review the current template files and target folder before creating the project.");
                _templateReview = null; // Once creation starts, an uncertain setup or file CREATE cannot be replayed from this review.
            }
            _templateBusy = true;
            _templateProjectStatus = command == "dev.templates.review" ? "Preparing exact provider files for review…"
                : "Registering the selected folder, creating each reviewed source file through Home and opening the project…";
        }

        return new(_work.RunAsync(async original =>
        {
            original.BindPublicationGuard(() => IsCurrent(actionGeneration));
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(original.Token,
                cancellationToken, _connectionLifetime, _windowLifetime);
            var token = linked.Token;
            try
            {
                if (selected is null || _templateMaterializer is null)
                    throw new InvalidOperationException("The selected provider template is no longer available.");
                if (command == "dev.templates.review")
                {
                    var prepared = await original.AwaitAsync(InvokeOriginalSource(() =>
                        _templateMaterializer.PrepareAsync(selected, projectName, token))).ConfigureAwait(false);
                    await PublishAsync(original, () =>
                    {
                        _templateBusy = false;
                        if (!prepared.Succeeded || prepared.Value is null)
                        {
                            _templateReview = null;
                            _templateReviewStatus = prepared.Error?.Message ?? "The template provider returned no reviewable file set.";
                            return;
                        }
                        if (prepared.Value.Files.Any(file => file.RelativePath.Contains('/') || file.RelativePath.Contains('\\')))
                        {
                            _templateReview = null;
                            _templateReviewStatus = "This route currently supports direct project-root files only. The template was not registered or written.";
                            return;
                        }
                        if (!StringComparer.Ordinal.Equals(prepared.Value.ProviderId, selected.Template.ProviderId) ||
                            !StringComparer.Ordinal.Equals(prepared.Value.TemplateId, selected.Template.TemplateId) ||
                            _templateInputRevision != inputRevision ||
                            !StringComparer.Ordinal.Equals(_selectedTemplateId, selected.Template.TemplateId) ||
                            !StringComparer.Ordinal.Equals(_selectedTemplateProviderId, selected.Template.ProviderId) ||
                            !StringComparer.Ordinal.Equals(_templateProjectName, projectName))
                        {
                            _templateReview = null;
                            _templateReviewStatus = "The project inputs changed while the provider was preparing files. Review again.";
                            return;
                        }
                        var files = prepared.Value.Files.Select(file => new TemplateFileReviewItem(file.RelativePath,
                            $"{file.SizeBytes} bytes · SHA-256 {file.ContentSha256}", file.Text)).ToImmutableArray();
                        _templateReview = new(prepared.Value.ProviderId, prepared.Value.TemplateId, projectName, folderPath,
                            _templateInputRevision, prepared.Value, files);
                        _templateReviewStatus = "Review every listed source file and the existing empty project folder before Create.";
                    }).ConfigureAwait(false);
                    return;
                }

                if (reviewed is null || _templateMaterializer.IsIssued(reviewed.Materialization) != true ||
                    !StringComparer.Ordinal.Equals(reviewed.FolderPath, folderPath) ||
                    !StringComparer.Ordinal.Equals(reviewed.ProjectName, projectName))
                    throw new InvalidOperationException("The template review changed before setup. Review the exact files again.");
                var reference = await original.AwaitAsync(InvokeOriginalSource(() =>
                    _registerTemplateProject!(folderPath, reviewed.Materialization, token))).ConfigureAwait(false);
                await original.AwaitAsync(InvokeOriginalSource(() =>
                    _openTemplateProject!(reference, reviewed.Materialization))).ConfigureAwait(false);
                await PublishAsync(original, () =>
                {
                    _templateBusy = false;
                    _templateProjectStatus = "The project setup completed and opened in Build. Check the Workbench template import status for each file's Home CREATE and cold-read outcome.";
                }).ConfigureAwait(false);
            }
            catch (OperationCanceledException error)
            {
                original.Retain(error);
                Task? status = null;
                try
                {
                    status = PublishAsync(original, () =>
                    {
                        _templateBusy = false;
                        _templateProjectStatus = command == "dev.templates.create"
                            ? "Template creation was canceled. Inspect the same saved project and setup state before attempting another creation."
                            : "Template review was canceled. Review the current template inputs again before creating a project.";
                        if (command == "dev.templates.review") _templateReview = null;
                    });
                    await original.AwaitAsync(status).ConfigureAwait(false);
                }
                catch (Exception publicationError) { original.Capture(status, publicationError); }
                original.ThrowRetained();
            }
            catch (Exception error)
            {
                original.Retain(error);
                Task? status = null;
                try
                {
                    status = PublishAsync(original, () =>
                    {
                        _templateBusy = false;
                        if (error is DeveloperManualAttemptBlockedException blocked)
                        {
                            _retainedAttemptState = "Retained setup " + blocked.SetupId.ToString("D") +
                                " has a partial/uncertain persisted effect (" + (blocked.State ?? "unknown") +
                                ") and was not re-run or replaced. Inspect the same template setup before creating another project.";
                            _manualStatus = "The template setup needs recovery inspection (setup " + blocked.SetupId.ToString("D") +
                                ", " + (blocked.State ?? "unknown") + "). It was not re-run or replaced.";
                            _templateProjectStatus = _retainedAttemptState;
                        }
                        else
                            _templateProjectStatus = "Template project creation stopped: " + error.Message +
                                " Inspect the same saved project and setup state before attempting another creation.";
                        if (command == "dev.templates.review") _templateReview = null;
                    });
                    await original.AwaitAsync(status).ConfigureAwait(false);
                }
                catch (Exception publicationError) { original.Capture(status, publicationError); }
                original.ThrowRetained();
            }
        }));
    }

    private sealed record TemplateItem(string SelectionKey, string ProviderId, string TemplateId, string DisplayName, string Summary);
    private sealed record TemplateFileReviewItem(string RelativePath, string Metadata, string Text);
    private sealed record TemplateReview(string ProviderId, string TemplateId, string ProjectName, string FolderPath,
        long InputRevision, DeveloperProjectTemplateMaterialization Materialization,
        ImmutableArray<TemplateFileReviewItem> Files);
    private ValueTask ChangeProjectPage(string command)
    {
        long generation; lock (_gate) generation = _generation;
        _work.RunSynchronous(original =>
        {
            original.BindPublicationGuard(() => IsCurrent(generation)); original.DemandPublication();
            lock (_gate)
            {
                if (IsActionAvailable(command) != true) throw new InvalidOperationException("This filtered project page is unavailable.");
                _projectPage += command == "dev.references.next-projects" ? 1 : -1;
            }
            NotifyOriginal(original);
        });
        return ValueTask.CompletedTask;
    }
    private Task PublishAsync(DesktopOriginalWorkLifetime.Original original, Action publish)
    {
        var actual = InvokeOriginalSource(() => Dispatcher.UIThread.InvokeAsync(() =>
        {
            original.DemandPublication();
            InvokeOriginalSource(() => { lock (_gate) publish(); return true; });
            NotifyOriginal(original);
        }).GetTask());
        return original.AwaitAsync(actual);
    }
    private void NotifyOriginal(DesktopOriginalWorkLifetime.Original original)
    {
        foreach (var callback in PropertyChanged?.GetInvocationList() ?? [])
        {
            original.DemandPublication();
            try { InvokeOriginalSource(() => { ((PropertyChangedEventHandler)callback)(this, new(null)); return true; }); }
            catch (Exception error) { original.Retain(error); }
        }
        original.ThrowRetained();
    }
    private void OwnNestedOriginalSource(Action callback) => InvokeOriginalSource(() => { callback(); return true; });
    private T InvokeOriginalSource<T>(Func<T> callback)
    {
        var owners = _sourceCallbacks ??= []; owners.Add(this);
        try { return callback(); }
        catch (OperationCanceledException fault) { throw new AggregateException("A synchronous saved-project source callback faulted.", fault); }
        finally { owners.RemoveAt(owners.Count - 1); }
    }
    public void DemandExternalOriginalRetirementJoin()
    {
        if (_sourceCallbacks?.Any(owner => ReferenceEquals(owner, this)) == true)
            throw new InvalidOperationException("An actual saved-project callback cannot join its encompassing page close.");
        _work.DemandExternalClose(); _catalog?.DemandExternalOriginalRetirementJoin(); _demandOriginalHomeJoin?.Invoke();
    }
    public void RequestRetirement() => _work.RequestRetirement();
    public Task CloseAndDrainAsync() { DemandExternalOriginalRetirementJoin(); return _work.CloseAndDrainAsync(); }
    private Task StopOriginalPresentationAsync()
    {
        lock (_gate) { _active = false; _generation++; _metadataAvailable = false; }
        return _hostClose ??= _host?.CloseOriginalAsync() ?? Task.CompletedTask;
    }
    private async Task DetachOriginalPresentationAsync()
    {
        if (_hostClose is not { IsCompletedSuccessfully: true })
            throw new InvalidOperationException("The actual saved-project host has not acknowledged close.");
        _detach ??= InvokeOriginalSource(() => Dispatcher.UIThread.InvokeAsync(() =>
            InvokeOriginalSource(() => { Content = null; return true; })).GetTask());
        try { await _detach.ConfigureAwait(false); }
        catch { if (_detach.IsFaulted) throw _detach.Exception!; throw; }
    }
    public ValueTask DisposeAsync() => new(CloseAndDrainAsync());
}
