using System.Collections.Generic;
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
        Func<string, CancellationToken, Task<DeveloperProjectReference>>? createManualProject = null)
    {
        ArgumentNullException.ThrowIfNull(originalCatalog); ArgumentNullException.ThrowIfNull(originalSpaces);
        ArgumentNullException.ThrowIfNull(originalHomeReadiness); ArgumentNullException.ThrowIfNull(demandOriginalHomeJoin);
        ArgumentNullException.ThrowIfNull(openExistingTask); ArgumentNullException.ThrowIfNull(openExistingSpace);
        ArgumentNullException.ThrowIfNull(retainActualPartial);
        if (manualWorkspaces is null != (openExistingManual is null))
            throw new ArgumentException("Manual saved-project enumeration and open must be supplied together.");
        if (!originalConnectionLifetime.CanBeCanceled || !originalWindowLifetime.CanBeCanceled)
            throw new ArgumentException("Retain the actual Home connection and native window lifetimes.");
        _catalog = originalCatalog; _spaces = originalSpaces; _originalHomeReadiness = originalHomeReadiness;
        _demandOriginalHomeJoin = demandOriginalHomeJoin; _openExistingTask = openExistingTask; _openExistingSpace = openExistingSpace;
        _manualWorkspaces = manualWorkspaces; _openExistingManual = openExistingManual; _createManualProject = createManualProject;
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
        await PublishAsync(original, () => { _page = null; _manualRows = []; _metadataAvailable = false; _manualMetadataAvailable = false; _projectPage = 0; _manualCatalogStatus = "Reading saved manual project roots…"; _status = "Reading saved projects…"; }).ConfigureAwait(false);
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
        var after = await ObserveOriginalHomeAsync(original, token).ConfigureAwait(false);
        await PublishAsync(original, () =>
        {
            if (_readSequence != sequence) return;
            if (after.State != CuiSceneAvailabilityState.Ready) { _status = after.Message; _manualRows = []; _manualMetadataAvailable = false; return; }
            _page = page; _manualRows = manualRows; _metadataAvailable = true; _manualMetadataAvailable = manualAvailable; _manualCatalogStatus = manualCoverage;
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
                _ => null
            };
        }
        return path is "Projects" or "Coverage" or "Status" or "CanRefresh" or "CanOpenSpace" or "CanCreateManual" or
            "ManualFolderPath" or "ManualStatus" or "RetainedAttempt" or "ManualBusy" or "ProjectQuery" or "ProjectFilterStatus" or "CanPreviousProjectPage" or "CanNextProjectPage";
    }

    /// <summary>Writable metadata inputs from the canonical landing document. A typed folder path
    /// is only a candidate; the existing setup adapter still performs actual Files selection and
    /// Home admission. Filter writeback changes the visible page and withdraws hidden row keys.</summary>
    public bool TrySetValue(string path, object? value)
    {
        if (value is not string text || path is not ("ManualFolderPath" or "ProjectQuery")) return false;
        long generation; lock (_gate) generation = _generation;
        _work.RunSynchronous(original =>
        {
            original.BindPublicationGuard(() => IsCurrent(generation)); original.DemandPublication();
            lock (_gate)
            {
                if (path == "ManualFolderPath") _manualFolderPath = text;
                else { _projectQuery = text; _projectPage = 0; }
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
                _ => false
            };
        }
    }
    public ValueTask DispatchAsync(string command, object? parameter, CancellationToken cancellationToken = default)
    {
        if (IsActionAvailable(command) != true) throw new InvalidOperationException("This saved-project action is unavailable.");
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
