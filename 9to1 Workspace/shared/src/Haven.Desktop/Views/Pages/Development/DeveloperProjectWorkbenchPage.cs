using System.ComponentModel;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Threading;
using CakeOS.Cui;
using CakeOS.Cui.Runtime;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.Services;
using Haven.Desktop.ViewModels;
using HavenOS.Apps.Dev;
using HavenOS.Files.NativeHost;

namespace Haven.Desktop.Views.Pages.Development;

/// <summary>
/// Existing-project Dev editor and commands over the SAME canonical Task/Run. A maintained
/// Files resolver supplies actual document identity; paths are never turned into new file IDs.
/// Retirement joins acquired business originals with their configured token unchanged. It
/// withdraws this view only, never the global Dev service or canonical work. The shell owns
/// constructor acquisition and the real frame/readiness producer for this exact host.
/// </summary>
public sealed partial class DeveloperProjectWorkbenchPage : UserControl, IActivatablePage,
    ICuiWritableBindingContext, ICuiActionDispatcher, ICuiActionAvailability, INotifyPropertyChanged,
    IDesktopOriginalRetirementParticipant, IDesktopOriginalRetirementJoinGuard, IAsyncDisposable
{
    private readonly DeveloperTaskWorkspaceService _development;
    private readonly DeveloperSourceReviewSession? _review;
    private readonly TaskExecutionCoordinator _canonical;
    private readonly Func<DeveloperResolvedProject, string, CancellationToken, Task<DeveloperOperationResult<DeveloperCodeDocument>>> _resolveDocument;
    private readonly Action _demandOriginalDocumentSourceJoin;
    private readonly Guid _taskId, _runId, _contextId;
    private readonly CancellationToken _businessToken;
    private readonly ICuiSceneReadiness _readiness;
    private readonly CuiControlRegistry _controls;
    private readonly CuiSceneHost _host;
    private readonly DesktopOriginalWorkLifetime _work;
    private readonly object _gate = new();
    // Manual (no-AI) open: admits a resolved saved project by its real stored identity without a
    // Task/Run/context. Reads/search are performed by a confined file reader; edits and process
    // execution require a separate current authorized Task/execution authority and fail closed.
    private readonly bool _manual;
    private readonly DeveloperManualSourceReadOwner? _manualReader;
    private readonly DeveloperManualSourceEditOwner? _manualEditor;
    private readonly FileDeveloperWorkspaceStore? _editorSessionStore;
    private readonly FilesDeveloperOriginalCurrentProjectExecutionBridge? _editorSessionExecution;
    private bool _cleanSessionLoaded, _cleanSessionBlocked;
    private long _cleanSessionGeneration;
    private string _manualDraftText = "";
    private string _manualDraftBaseline = "";
    private string _manualBeforeText = "", _manualAfterText = "", _manualBeforeSha256 = "", _manualAfterSha256 = "";
    private long _manualDraftRevision;
    private long _manualReviewRevision = -1;
    private bool _manualReviewed;
    // View IDs select existing owned documents; they never mint Files resource identity or grants.
    private sealed class SourceTab(Guid id, string path, DeveloperEditorSnapshot? editor, ManualTabState? manual)
    {
        public Guid Id { get; } = id;
        public string Path { get; } = path;
        public DeveloperEditorSnapshot? Editor = editor;
        public ManualTabState? Manual = manual;
        public DeveloperSourceChangePass? Pass;
        public bool IsPinned;
    }
    private sealed record ManualTabState(string Draft, string Baseline, string Before, string After,
        string BeforeSha256, string AfterSha256, long DraftRevision, long ReviewRevision, bool Reviewed);
    private sealed record SourceTabItem(string SelectionKey, string Label, string AccessibleName,
        string CloseAccessibleName, bool CanSelect, bool CanClose);
    private readonly List<SourceTab> _sourceTabs = [];
    private Guid? _selectedSourceTab;
    private const int ProjectExplorerPageSize = 100;
    private IReadOnlyList<DeveloperProjectTreeEntry>? _projectEntries;
    private IReadOnlyList<DeveloperProjectTreeEntry> _projectDirectoryEntries = [];
    private string _projectFolder = "";
    private int _projectEntryPage;
    private sealed record ProjectEntryItem(string SelectionKey, string Name, string Details, string AccessibleName, bool CanOpen);
    [ThreadStatic] private static List<DeveloperProjectWorkbenchPage>? _sourceCallbacks;
    private DeveloperResolvedProject _project;
    private DeveloperEditorSnapshot? _editor;
    private DeveloperSourceChangePass? _pass;
    private bool _active, _pending;
    private long _generation;
    private string _sourcePath = "", _searchQuery = "", _command = "", _testCommand = "";
    private string _stageTaskId = "", _stageId = "", _branchName = "";
    private string _files = "", _searchResults = "", _status = "", _processStatus = "", _processOutput = "";
    private bool _searchUseRegex, _searchMatchCase = true;
    private long _searchGeneration;
    private DeveloperProjectSearchResult? _structuredSearch;
    private readonly Dictionary<string, DeveloperProjectSearchMatch> _searchMatches = [];
    private sealed record SearchResultItem(string SelectionKey, string Label, string AccessibleName, bool CanOpen);
    private sealed record SearchSelection(DeveloperProjectSearchResult Observation, DeveloperProjectSearchMatch Match, long Generation);
    private string _saveReviewedStatus = "";
    internal IReadOnlyList<DeveloperManualSaveOutcome> OriginalManualSaveBatch { get; private set; } = [];
    private readonly List<IReadOnlyList<DeveloperManualSaveOutcome>> _originalManualSaveBatches = [];
    private Task? _hostClose, _reviewClose, _readinessChildClose, _detachOriginalDispatcher;
    private IDesktopOriginalRetirementParticipant? _readinessChild;
    private IDesktopOriginalRetirementJoinGuard? _readinessChildGuard;
    private ICuiSceneReadiness? _unresolvedReadinessChild;

    public DeveloperProjectWorkbenchPage(DeveloperTaskWorkspaceService development, TaskExecutionCoordinator canonical,
        DeveloperResolvedProject actualProject, Guid actualTaskId, Guid actualExecutionId, Guid actualContextId,
        ICuiSceneReadiness readiness,
        Func<DeveloperResolvedProject, string, CancellationToken, Task<DeveloperOperationResult<DeveloperCodeDocument>>> resolveOriginalDocument,
        Action demandOriginalDocumentSourceExternalJoin,
        CancellationToken explicitBusinessCancellationToken = default)
        : this(development, canonical, actualProject, actualTaskId, actualExecutionId, actualContextId, readiness,
            resolveOriginalDocument, demandOriginalDocumentSourceExternalJoin, explicitBusinessCancellationToken, null) { }

    internal DeveloperProjectWorkbenchPage(DeveloperTaskWorkspaceService development, TaskExecutionCoordinator canonical,
        DeveloperResolvedProject actualProject, Guid actualTaskId, Guid actualExecutionId, Guid actualContextId,
        ICuiSceneReadiness readiness,
        Func<DeveloperResolvedProject, string, CancellationToken, Task<DeveloperOperationResult<DeveloperCodeDocument>>> resolveOriginalDocument,
        Action demandOriginalDocumentSourceExternalJoin, CancellationToken explicitBusinessCancellationToken,
        Action<DeveloperProjectWorkbenchPage>? retainOriginalAcquisition,
        DeveloperManualSourceReadOwner? sourceExplorerReader = null,
        FileDeveloperWorkspaceStore? editorSessionStore = null,
        FilesDeveloperOriginalCurrentProjectExecutionBridge? editorSessionExecution = null,
        DeveloperTaskDraftRecoveryOwner? taskRecoveryOwner = null)
        : this(development, canonical, actualProject, actualTaskId, actualExecutionId, actualContextId, readiness,
            resolveOriginalDocument, demandOriginalDocumentSourceExternalJoin, explicitBusinessCancellationToken,
            retainOriginalAcquisition, null, sourceExplorerReader, editorSessionStore, editorSessionExecution, taskRecoveryOwner) { }

    internal DeveloperProjectWorkbenchPage(DeveloperTaskWorkspaceService development, TaskExecutionCoordinator canonical,
        DeveloperResolvedProject actualProject, Guid actualTaskId, Guid actualExecutionId, Guid actualContextId,
        ICuiSceneReadiness? readiness,
        Func<DeveloperResolvedProject, string, CancellationToken, Task<DeveloperOperationResult<DeveloperCodeDocument>>> resolveOriginalDocument,
        Action demandOriginalDocumentSourceExternalJoin, CancellationToken explicitBusinessCancellationToken,
        Action<DeveloperProjectWorkbenchPage>? retainOriginalAcquisition,
        Func<DeveloperProjectWorkbenchPage, ICuiSceneReadiness>? createOriginalReadinessChild,
        DeveloperManualSourceReadOwner? sourceExplorerReader = null,
        FileDeveloperWorkspaceStore? editorSessionStore = null,
        FilesDeveloperOriginalCurrentProjectExecutionBridge? editorSessionExecution = null,
        DeveloperTaskDraftRecoveryOwner? taskRecoveryOwner = null)
    {
        _development = development ?? throw new ArgumentNullException(nameof(development));
        _canonical = canonical ?? throw new ArgumentNullException(nameof(canonical));
        _project = actualProject ?? throw new ArgumentNullException(nameof(actualProject));
        _manualReader = sourceExplorerReader; // Optional genuine user-navigation reader; no new source authority.
        _editorSessionStore = editorSessionStore;
        _editorSessionExecution = editorSessionExecution;
        _taskRecoveryOwner = taskRecoveryOwner;
        if (actualTaskId == Guid.Empty || actualExecutionId == Guid.Empty || actualContextId == Guid.Empty)
            throw new ArgumentException("The existing canonical Task/Run/context must be supplied together.");
        _taskId = actualTaskId; _runId = actualExecutionId; _contextId = actualContextId;
        if (readiness is null && createOriginalReadinessChild is null) throw new ArgumentNullException(nameof(readiness));
        _readiness = readiness!; // Genuine child is acquired after exact Page/Host custody below.
        _resolveDocument = resolveOriginalDocument ?? throw new ArgumentNullException(nameof(resolveOriginalDocument));
        _demandOriginalDocumentSourceJoin = demandOriginalDocumentSourceExternalJoin ?? throw new ArgumentNullException(nameof(demandOriginalDocumentSourceExternalJoin));
        _businessToken = explicitBusinessCancellationToken;
        _review = new(development); Document = DeveloperWorkbenchCuiDocument.Load();
        // Each host gets its own registry with the canonical built-in ReadOnlyText contract.
        // The normal compiler admits the same typed properties without an app-specific factory.
        _controls = new();
        _host = new(_controls);
        _work = new(StopOriginalPresentationAsync, DetachOriginalPresentationAsync);
        // Host custody precedes actual property/content publication callbacks. If a later
        // constructor setter fails, the host still owns this SAME partial acquisition.
        if (retainOriginalAcquisition is not null)
            InvokeOriginalSource(() => { retainOriginalAcquisition(this); return true; });
        if (createOriginalReadinessChild is not null)
        {
            // SAME partial Page is already retained. Capture a returned product before any
            // typed validation/publication: failed acquisition remains an explicit obligation.
            var actual = InvokeOriginalSource(() => { _work.DemandAdmission(); return createOriginalReadinessChild(this); });
            _unresolvedReadinessChild = actual;
            _readiness = actual ?? throw new InvalidOperationException("The genuine readiness child was not returned.");
            _readinessChild = actual as IDesktopOriginalRetirementParticipant;
            _readinessChildGuard = actual as IDesktopOriginalRetirementJoinGuard;
            if (_readinessChild is null || _readinessChildGuard is null)
                throw new InvalidOperationException("The original readiness child must supply its genuine typed retirement and pure join guard.");
            _unresolvedReadinessChild = null;
        }
        InvokeOriginalSource(() =>
        {
            _work.DemandAdmission();
            AutomationProperties.SetAutomationId(this, "DeveloperProjectWorkbench");
            _work.DemandAdmission();
            AutomationProperties.SetName(this, "Development project source and commands");
            _work.DemandAdmission();
            Content = _host;
            _work.DemandAdmission();
            return true;
        });
    }

    /// <summary>Manual (no-AI) workbench admission. It opens a resolved saved project by its real
    /// stored identity with no Task/Run/context/model and no generation. Reads/search are confined
    /// file observations over the actual project root; source edits and process execution are NOT
    /// offered here (they require a separate current authorized Task/execution authority and fail
    /// closed). The same compiled-CUI workbench document and host are used.</summary>
    internal DeveloperProjectWorkbenchPage(DeveloperTaskWorkspaceService development, TaskExecutionCoordinator canonical,
        DeveloperResolvedProject actualProject, ICuiSceneReadiness? readiness,
        Func<DeveloperResolvedProject, string, CancellationToken, Task<DeveloperOperationResult<DeveloperCodeDocument>>> resolveOriginalDocument,
        Action demandOriginalDocumentSourceExternalJoin, CancellationToken explicitBusinessCancellationToken,
        Action<DeveloperProjectWorkbenchPage>? retainOriginalAcquisition,
        Func<DeveloperProjectWorkbenchPage, ICuiSceneReadiness>? createOriginalReadinessChild,
        DeveloperManualSourceReadOwner manualReader,
        DeveloperManualSourceEditOwner? manualEditor = null,
        FileDeveloperWorkspaceStore? editorSessionStore = null,
        FilesDeveloperOriginalCurrentProjectExecutionBridge? editorSessionExecution = null,
        IProviderSecretStore? originalRecoveryKeys = null,
        IAppPaths? originalRecoveryPaths = null)
    {
        _development = development ?? throw new ArgumentNullException(nameof(development));
        _canonical = canonical ?? throw new ArgumentNullException(nameof(canonical));
        _project = actualProject ?? throw new ArgumentNullException(nameof(actualProject));
        // Manual admission: no Task/Run/context. The real saved project identity IS the admission.
        _taskId = Guid.Empty; _runId = Guid.Empty; _contextId = Guid.Empty;
        if (readiness is null && createOriginalReadinessChild is null) throw new ArgumentNullException(nameof(readiness));
        _readiness = readiness!;
        _resolveDocument = resolveOriginalDocument ?? throw new ArgumentNullException(nameof(resolveOriginalDocument));
        _demandOriginalDocumentSourceJoin = demandOriginalDocumentSourceExternalJoin ?? throw new ArgumentNullException(nameof(demandOriginalDocumentSourceExternalJoin));
        _businessToken = explicitBusinessCancellationToken;
        _manual = true;
        _manualReader = manualReader ?? throw new ArgumentNullException(nameof(manualReader));
        _manualEditor = manualEditor; // Optional: without it manual preview/apply fail closed (genuine owners unconfigured).
        _manualRecoveryKeys = originalRecoveryKeys;
        _manualRecoveryPaths = originalRecoveryPaths;
        _editorSessionStore = editorSessionStore;
        _editorSessionExecution = editorSessionExecution;
        _review = null; // No AI Task-backed review session; manual edits route through the genuine edit admission + kernel.
        Document = DeveloperWorkbenchCuiDocument.Load();
        _controls = new();
        _host = new(_controls);
        _work = new(StopOriginalPresentationAsync, DetachOriginalPresentationAsync);
        if (retainOriginalAcquisition is not null)
            InvokeOriginalSource(() => { retainOriginalAcquisition(this); return true; });
        if (createOriginalReadinessChild is not null)
        {
            var actual = InvokeOriginalSource(() => { _work.DemandAdmission(); return createOriginalReadinessChild(this); });
            _unresolvedReadinessChild = actual;
            _readiness = actual ?? throw new InvalidOperationException("The genuine readiness child was not returned.");
            _readinessChild = actual as IDesktopOriginalRetirementParticipant;
            _readinessChildGuard = actual as IDesktopOriginalRetirementJoinGuard;
            if (_readinessChild is null || _readinessChildGuard is null)
                throw new InvalidOperationException("The original readiness child must supply its genuine typed retirement and pure join guard.");
            _unresolvedReadinessChild = null;
        }
        InvokeOriginalSource(() =>
        {
            _work.DemandAdmission();
            AutomationProperties.SetAutomationId(this, "DeveloperProjectWorkbench");
            _work.DemandAdmission();
            AutomationProperties.SetName(this, "Development project source and commands");
            _work.DemandAdmission();
            Content = _host;
            _work.DemandAdmission();
            return true;
        });
    }

    public CuiDocument Document { get; }
    internal CuiSceneHost OriginalHost => _host;
    internal long OriginalReadinessGeneration { get { lock (_gate) return _generation; } }
    internal (Guid TaskId, Guid ExecutionId, Guid ContextId) OriginalReadinessContext => (_taskId, _runId, _contextId);
    internal bool IsOriginalReadinessPresentationCurrent(CuiSceneHost exactHost, long originalGeneration) =>
        ReferenceEquals(exactHost, _host) && IsCurrent(originalGeneration);
    /// <summary>True while the workbench holds unsaved authored work or an in-flight Dev command,
    /// so a tab owner can veto retirement instead of silently discarding it. A manual (no-AI)
    /// draft vetoes close until applied, reverted, or its exact latest complete protected
    /// recovery is acknowledged. An older acknowledged snapshot never clears a newer draft.</summary>
    internal bool HasUnsavedWork { get { lock (_gate) return _pending ||
        (_sourceTabs.Any(IsTabDirty) || _editor is { IsDirty: true } ||
            (_manual && !StringComparer.Ordinal.Equals(_manualDraftText, _manualDraftBaseline))) &&
        !HasExactProtectedManualRecovery() && !HasExactProtectedTaskRecovery(); } }
    public new event PropertyChangedEventHandler? PropertyChanged;
    public DeveloperActionObservation? OriginalDeveloperObservation { get; private set; }
    public DeveloperSourceChangePass? OriginalSourcePass { get; private set; }
    // The Files resolver consumes this exact generation observer only to refuse stale
    // identity publication; it never grants content/resource/native readiness authority.
    internal Func<bool> CaptureOriginalDocumentObservationCurrentness()
    {
        long generation;
        lock (_gate)
        {
            if (!_active || _work.IsRetiring) throw new ObjectDisposedException(nameof(DeveloperProjectWorkbenchPage));
            generation = _generation;
        }
        return () => IsCurrent(generation);
    }
    private bool IsCurrent(long generation)
    { lock (_gate) return !_work.IsRetiring && _active && _generation == generation; }

    public Task ActivateAsync(CancellationToken cancellationToken)
    {
        long generation;
        lock (_gate) { _work.DemandAdmission(); _active = true; generation = ++_generation; }
        return _work.RunAsync(async original =>
        {
            original.BindPublicationGuard(() => IsCurrent(generation));
            using var scope = CancellationTokenSource.CreateLinkedTokenSource(original.Token, cancellationToken);
            var project = await ResolveProjectAsync(original, scope.Token).ConfigureAwait(false);
            await PublishAsync(original, () => { _project = project; _status = "Existing project opened. Source reads use its current Home permissions."; }).ConfigureAwait(false);
            if (_manual)
            {
                await RestoreCleanSessionAsync(original, project, scope.Token).ConfigureAwait(false);
                await RestoreManualRecoveryAsync(original, project, scope.Token).ConfigureAwait(false);
            }
            else
            {
                await RestoreTaskRecoveryAsync(original, project, scope.Token).ConfigureAwait(false);
                if (!_taskRecoveryHasSavedSnapshot) await RestoreCleanSessionAsync(original, project, scope.Token).ConfigureAwait(false);
            }
            Task? show = null;
            var dispatcher = Dispatcher.UIThread.InvokeAsync(() =>
            {
                original.DemandPublication();
                show = InvokeOriginalSource(() => _host.ShowAsync(new("dev.project-workbench", project.Project.Name, "Dev",
                    Document, this, this, _readiness) { ControlRegistry = _controls, IsPublicationCurrent = () => IsCurrent(generation) }, scope.Token));
            }).GetTask();
            try { await original.AwaitAsync(dispatcher).ConfigureAwait(false); }
            catch (Exception error) { original.Capture(dispatcher, error); }
            if (show is not null)
                try { await original.AwaitAsync(show).ConfigureAwait(false); }
                catch (Exception error) { original.Capture(show, error); }
            else original.Retain(new InvalidOperationException("The original Dev host Show was not acquired."));
            original.ThrowRetained();
        });
    }
    public void Deactivate() { lock (_gate) { _active = false; _generation++; } }

    public bool TryGetValue(string path, out object? value)
    {
        lock (_gate)
        {
            // Preserve source/outcome evidence in its actual owner; a withdrawn binding cannot disclose it.
            if (!_active || _work.IsRetiring) { value = null; return false; }
            if (TryGetManualRecoveryConflictValue(path, out value)) return true;
            value = path switch
            {
                "ProjectTitle" => _project.Project.Name,
                "ProjectSummary" => _manual
                    ? $"Workspace {_project.Reference.WorkspaceId:D} / revision {_project.Reference.WorkspaceRevision}\nProject {_project.Reference.ProjectId:D} / revision {_project.Reference.ProjectRevision}\nRoot {_project.Reference.RootId:D}; {_project.Root.EnvironmentId}\nRepository {_project.Repository?.CanonicalRepositoryId ?? "not configured"}\nManual open — no AI task or model. Reads and reviewed edits use the current Home permissions; process execution requires separate execution authority."
                    : $"Workspace {_project.Reference.WorkspaceId:D} / revision {_project.Reference.WorkspaceRevision}\nProject {_project.Reference.ProjectId:D} / revision {_project.Reference.ProjectRevision}\nRoot {_project.Reference.RootId:D}; {_project.Root.EnvironmentId}\nRepository {_project.Repository?.CanonicalRepositoryId ?? "not configured"}\nTask {_taskId:D}; run {_runId:D}",
                "Files" => _files, "SourcePath" => _sourcePath, "SearchQuery" => _searchQuery, "SearchResults" => _searchResults,
                "CanStructuredSearch" => !_pending && _manualReader is not null,
                "SearchUseRegex" => _searchUseRegex, "SearchMatchCase" => _searchMatchCase,
                "ProjectSearchMatches" => (_structuredSearch?.Project == _project.Reference ? _searchMatches : new Dictionary<string, DeveloperProjectSearchMatch>()).Select(pair => new SearchResultItem(pair.Key,
                    $"{pair.Value.RelativePath}:{pair.Value.LineNumber}:{pair.Value.ColumnNumber} — {pair.Value.Preview}",
                    $"Open matched saved source {pair.Value.RelativePath}, line {pair.Value.LineNumber}, column {pair.Value.ColumnNumber}", !_pending)).ToArray(),
                "EditorTitle" => _manual
                    ? CurrentSourceTab() is not { } manualSource ? "Choose an existing registered source document to read it."
                        : $"{manualSource.Path}; manual source tab. Reviewed edits use the current Home write permission."
                    : _editor is null ? "Choose an existing registered source document." : $"{_editor.Document.RelativePath}; draft revision {_editor.DraftRevision}" + (_editor.IsDirty ? " — unsaved draft" : ""),
                "DraftText" => _manual && _editor is null ? _manualDraftText : _editor?.DraftText ?? "",
                "ReviewSummary" => _pass is null ? "Review before applying. Pass history is available for this open editor." : $"Pass {_pass.ChangeSetId:D}: {_pass.State}; before {_pass.BeforeSha256}; after {_pass.AfterSha256}" + (_pass.RevertsChangeSetId is { } inverse ? $"; reverts only pass {inverse:D}" : ""),
                "BeforeText" => _manual ? _manualBeforeText : (_pass?.BeforeText ?? ""),
                "AfterText" => _manual ? _manualAfterText : (_pass?.AfterText ?? ""),
                "Command" => _command, "TestCommand" => _testCommand, "StageTaskId" => _stageTaskId, "StageId" => _stageId,
                "BranchName" => _branchName, "ProcessStatus" => _processStatus, "ProcessOutput" => _processOutput, "Status" => _status,
                "SourceTabs" => _sourceTabs.Select(tab => new SourceTabItem(tab.Id.ToString("D"),
                    (tab.Id == _selectedSourceTab ? "● " : "") + System.IO.Path.GetFileName(tab.Path) +
                        (IsTabDirty(tab) ? " *" : "") + (tab.IsPinned ? " (pinned)" : " (preview)"),
                    tab.Path + (IsTabDirty(tab) ? ", unsaved changes" : ", saved") + (tab.IsPinned ? ", pinned" : ", preview"),
                    "Close " + tab.Path + (IsTabDirty(tab) ? "; apply or revert its changes first" : ""),
                    !_pending, !_pending && !IsTabDirty(tab))).ToArray(),
                "SourceTabPinLabel" => CurrentSourceTab() is { IsPinned: true } ? "Unpin tab" : "Pin tab",
                "CanPinSourceTab" => IsActionAvailable("dev.toggle-source-tab-pin"),
                "CanEditSource" => CurrentSourceTab() is not null,
                "SourceTabsStatus" => $"{_sourceTabs.Count} / {DeveloperSourceReviewSession.MaximumOpenDocuments} open documents. Unsaved tabs are retained; closing never discards a draft.",
                "ProjectEntries" => ProjectFolderEntries().Skip(_projectEntryPage * ProjectExplorerPageSize).Take(ProjectExplorerPageSize)
                    .Select(entry => new ProjectEntryItem(entry.SelectionKey, entry.Name,
                        entry.IsDirectory ? "Folder" : $"{entry.SizeBytes:N0} bytes", entry.RelativePath + (entry.IsDirectory ? ", open folder" : ", open source"), !_pending)).ToArray(),
                "ProjectFolder" => string.IsNullOrEmpty(_projectFolder) ? "Project files" : "Project / " + _projectFolder,
                "ProjectExplorerStatus" => _projectEntries is null ? "Refresh project files to browse the genuine captured tree."
                    : $"{ProjectFolderEntries().Count()} entries in this folder; showing page {_projectEntryPage + 1} ({ProjectExplorerPageSize} per page).",
                "ShowProjectExplorer" => _manualReader is not null,
                "ShowLegacyFiles" => _manualReader is null,
                "CanProjectFolderUp" => IsActionAvailable("dev.project-folder-up"),
                "CanProjectEntriesPrevious" => IsActionAvailable("dev.project-entries-previous"),
                "CanProjectEntriesNext" => IsActionAvailable("dev.project-entries-next"),
                "CanOpenSource" => IsActionAvailable("dev.open-source"), "CanPreview" => IsActionAvailable("dev.preview-edit"),
                "CanApply" => IsActionAvailable("dev.apply-edit"), "CanRevert" => IsActionAvailable("dev.revert-pass"),
                "CanSaveReviewed" => IsActionAvailable("dev.save-reviewed"), "SaveReviewedStatus" => _saveReviewedStatus, _ => null
            };
        }
        return path is "CanStructuredSearch" or "SearchUseRegex" or "SearchMatchCase" or "ProjectSearchMatches" or
            "ProjectTitle" or "ProjectSummary" or "Files" or "SourcePath" or "SearchQuery" or "SearchResults" or
            "EditorTitle" or "DraftText" or "ReviewSummary" or "BeforeText" or "AfterText" or "Command" or "TestCommand" or
            "StageTaskId" or "StageId" or "BranchName" or "ProcessStatus" or "ProcessOutput" or "Status" or "CanOpenSource" or "CanPreview" or "CanApply" or "CanRevert" or
            "SourceTabs" or "SourceTabPinLabel" or "CanPinSourceTab" or "CanEditSource" or "SourceTabsStatus" or
            "ProjectEntries" or "ProjectFolder" or "ProjectExplorerStatus" or "ShowProjectExplorer" or "ShowLegacyFiles" or
            "CanProjectFolderUp" or "CanProjectEntriesPrevious" or "CanProjectEntriesNext" or "CanSaveReviewed" or "SaveReviewedStatus";
    }
    public bool TrySetValue(string path, object? value)
    {
        if (path is "SearchUseRegex" or "SearchMatchCase")
        {
            if (value is not bool enabled || _manualReader is null) return false;
            long optionGeneration; lock (_gate) optionGeneration = _generation;
            _work.RunSynchronous(original =>
            {
                original.BindPublicationGuard(() => IsCurrent(optionGeneration)); original.DemandPublication();
                lock (_gate)
                {
                    if (path == "SearchUseRegex") _searchUseRegex = enabled; else _searchMatchCase = enabled;
                    InvalidateProjectSearch();
                }
                NotifyOriginal(original);
            });
            return true;
        }
        if (value is not string text || path is not ("SourcePath" or "SearchQuery" or "DraftText" or "Command" or "TestCommand" or "StageTaskId" or "StageId" or "BranchName")) return false;
        long generation; lock (_gate) generation = _generation;
        _work.RunSynchronous(original =>
        {
            original.BindPublicationGuard(() => IsCurrent(generation)); original.DemandPublication();
            InvokeOriginalSource(() =>
            {
                lock (_gate)
                {
                    switch (path)
                    {
                        case "SourcePath":
                            // Typing another path is not permission to discard the current document.
                            // Its tab retains the exact draft/review; manual apply is unavailable
                            // until the path once again names the actually open source.
                            StoreSelectedTab(); _sourcePath = text; break;
                        case "SearchQuery": if (!StringComparer.Ordinal.Equals(_searchQuery, text)) { _searchQuery = text; InvalidateProjectSearch(); } break;
                        case "DraftText":
                            if (_manual && _editor is null)
                            {
                                if (CurrentSourceTab() is null && text.Length != 0)
                                    throw new InvalidOperationException("Open an authorized source document before editing a draft.");
                                _manualDraftText = text; _manualDraftRevision++; _manualReviewed = false;
                                _manualAfterText = ""; _manualAfterSha256 = ""; _manualReviewRevision = -1;
                                if (CurrentSourceTab() is { } manualTab) manualTab.IsPinned = true;
                                StoreSelectedTab(); break;
                            }
                            if (_editor is null) throw new InvalidOperationException("Open a real complete source document first.");
                            _editor = _review!.UpdateDraft(_editor.EditorId, _editor.DraftRevision, text);
                            if (CurrentSourceTab() is { } editedTab) editedTab.IsPinned = true;
                            StoreSelectedTab(); break;
                        case "Command": _command = text; break; case "TestCommand": _testCommand = text; break;
                        case "StageTaskId": _stageTaskId = text; break; case "StageId": _stageId = text; break; case "BranchName": _branchName = text; break;
                    }
                }
                return true;
            });
            NotifyOriginal(original);
        });
        if (path == "DraftText") { ScheduleManualRecoverySave(); ScheduleTaskRecoverySave(); }
        return true;
    }
    public bool? IsActionAvailable(string command)
    {
        lock (_gate)
        {
            if (!_active || _work.IsRetiring || _pending) return false;
            if (command == "dev.inspect-recovered-draft") return _manual && _manualReader is not null && _manualRecoveryCandidates.Count > 0;
            if (command == "dev.reconcile-recovered-draft") return _manual && _manualReader is not null && SelectedManualRecoveryCandidate() is not null;
            if (command is "dev.select-source-tab" or "dev.close-source-tab") return _sourceTabs.Count > 0;
            if (command == "dev.toggle-source-tab-pin") return CurrentSourceTab() is not null;
            if (command == "dev.open-project-entry") return _manualReader is not null && _projectEntries is not null;
            if (command == "dev.open-search-result") return _manualReader is not null && _structuredSearch?.Project == _project.Reference && _searchMatches.Count > 0;
            if (command == "dev.project-folder-up") return _projectEntries is not null && !string.IsNullOrEmpty(_projectFolder);
            if (command == "dev.project-entries-previous") return _projectEntries is not null && _projectEntryPage > 0;
            if (command == "dev.project-entries-next") return _projectEntries is not null &&
                ProjectFolderEntries().Count() > (_projectEntryPage + 1) * ProjectExplorerPageSize;
            if (_manual)
            {
                // Manual (no-AI) open offers only confined read/tree/search. Every mutation and
                // process-execution action is unavailable here: it requires a separate current
                // authorized Task/execution authority and fails closed with no effects.
                return command switch
                {
                    "dev.refresh" => true,
                    "dev.tree" => true,
                    "dev.search" => !string.IsNullOrWhiteSpace(_searchQuery),
                    "dev.open-source" => !string.IsNullOrWhiteSpace(_sourcePath),
                    "dev.preview-edit" => _manualEditor is not null && CurrentSourceTab() is { } previewTab && StringComparer.Ordinal.Equals(previewTab.Path, _sourcePath) &&
                        !StringComparer.Ordinal.Equals(_manualDraftText, _manualDraftBaseline),
                    "dev.apply-edit" => _manualEditor is not null && _manualReviewed && CurrentSourceTab() is { } applyTab && StringComparer.Ordinal.Equals(applyTab.Path, _sourcePath),
                    "dev.save-reviewed" => _manualEditor is not null && _originalManualSaveBatches.Count < 64 && _sourceTabs.Any(IsTabDirty),
                    _ => false
                };
            }
            return command switch
            {
                "dev.refresh" or "dev.tree" or "dev.git-status" or "dev.git-diff" or "dev.git-branches" => true,
                "dev.open-source" => !string.IsNullOrWhiteSpace(_sourcePath),
                "dev.search" => !string.IsNullOrWhiteSpace(_searchQuery),
                "dev.preview-edit" => _editor is { IsDirty: true },
                "dev.apply-edit" => _editor?.PreparedChangeSetId is { } id && _pass?.ChangeSetId == id && _pass.State == DeveloperSourceChangeState.Reviewed,
                "dev.revert-pass" => _editor is { IsDirty: false, LastAppliedChangeSetId: not null },
                "dev.run-command" => !string.IsNullOrWhiteSpace(_command), "dev.run-tests" => !string.IsNullOrWhiteSpace(_testCommand),
                "dev.run-stage" => !string.IsNullOrWhiteSpace(_stageTaskId) && !string.IsNullOrWhiteSpace(_stageId),
                "dev.create-branch" or "dev.switch-branch" => !string.IsNullOrWhiteSpace(_branchName), _ => false
            };
        }
    }

    public ValueTask DispatchAsync(string command, object? parameter, CancellationToken cancellationToken = default)
    {
        if (command is "dev.inspect-recovered-draft" or "dev.reconcile-recovered-draft")
            return DispatchManualRecoveryConflictAsync(command, parameter, cancellationToken);
        long generation; string path, query, terminal, test, stageTask, stage, branch; DeveloperEditorSnapshot? editor; DeveloperSourceChangePass? pass;
        string manualBeforeText, manualDraftText, manualBeforeSha256, manualAfterSha256; bool manualReviewed; long manualReviewRevision;
        Guid? requestedTab = null;
        string? projectFolder = null, entryPath = null;
        IReadOnlyList<DeveloperManualSaveRequest>? saveReviewed = null;
        SearchSelection? searchSelection = null;
        DeveloperProjectSearchOptions searchOptions; long searchGeneration;
        lock (_gate)
        {
            if (IsActionAvailable(command) != true) throw new InvalidOperationException("This Dev operation is unavailable.");
            if (command is "dev.select-source-tab" or "dev.close-source-tab")
            {
                if (parameter is not string key || !Guid.TryParse(key, out var tabId) || _sourceTabs.All(tab => tab.Id != tabId))
                    throw new InvalidOperationException("The source tab is not owned by this open project.");
                requestedTab = tabId;
            }
            if (command == "dev.open-project-entry")
            {
                if (parameter is not string entryKey || _projectEntries?.SingleOrDefault(entry => entry.SelectionKey == entryKey) is not { } entry ||
                    !StringComparer.Ordinal.Equals(entry.ParentPath, _projectFolder))
                    throw new InvalidOperationException("The selected entry is not in this captured project folder. Refresh the project files.");
                if (entry.IsDirectory) projectFolder = entry.RelativePath;
                else { entryPath = entry.RelativePath; command = "dev.open-source"; }
            }
            if (command == "dev.open-search-result")
            {
                if (parameter is not string searchKey || _structuredSearch is not { } observation ||
                    !_searchMatches.TryGetValue(searchKey, out var match))
                    throw new InvalidOperationException("Choose an original result from the current project search.");
                searchSelection = new(observation, match, _searchGeneration);
                entryPath = match.RelativePath; command = "dev.open-source";
            }
            searchOptions = new(_searchUseRegex, _searchMatchCase); searchGeneration = _searchGeneration;
            if (command == "dev.project-folder-up")
            {
                var separator = _projectFolder.LastIndexOf('/'); projectFolder = separator < 0 ? "" : _projectFolder[..separator];
            }
            if (command == "dev.save-reviewed")
            {
                StoreSelectedTab();
                saveReviewed = Array.AsReadOnly(_sourceTabs.Where(IsTabDirty).Select(tab =>
                {
                    var state = tab.Manual ?? throw new InvalidOperationException("This open source tab has no manual draft owner.");
                    return new DeveloperManualSaveRequest(tab.Id, tab.Path, state.Baseline, state.BeforeSha256,
                        state.Draft, state.AfterSha256, state.DraftRevision, state.ReviewRevision, state.Reviewed);
                }).ToArray());
            }
            _pending = true; generation = _generation; path = _sourcePath; query = _searchQuery; terminal = _command; test = _testCommand;
            stageTask = _stageTaskId; stage = _stageId; branch = _branchName; editor = _editor; pass = _pass;
            if (command == "dev.select-source-tab") path = _sourceTabs.Single(tab => tab.Id == requestedTab).Path;
            if (entryPath is not null) path = entryPath; // Normal Open below still performs the genuine fresh authorized read.
            manualBeforeText = _manualBeforeText; manualDraftText = _manualDraftText; manualBeforeSha256 = _manualBeforeSha256;
            manualAfterSha256 = _manualAfterSha256; manualReviewed = _manualReviewed; manualReviewRevision = _manualReviewRevision;
        }
        try
        {
            if (projectFolder is not null || command is "dev.project-entries-previous" or "dev.project-entries-next")
                return DispatchProjectFolderAsync(projectFolder, command, generation);
            if (command is "dev.close-source-tab" or "dev.toggle-source-tab-pin")
                return DispatchTabPresentationAsync(command, requestedTab, generation);
            if (_manual)
                return DispatchManualAsync(command, path, query, manualBeforeText, manualDraftText, manualBeforeSha256,
                    manualAfterSha256, manualReviewed, manualReviewRevision, saveReviewed, generation,
                    searchOptions, searchGeneration, searchSelection, cancellationToken);
            return new(_work.RunAsync(async original =>
            {
                original.BindPublicationGuard(() => IsCurrent(generation));
                try
                {
                    original.DemandPublication(); cancellationToken.ThrowIfCancellationRequested();
                    var project = await ResolveProjectAsync(original, cancellationToken).ConfigureAwait(false);
                    if (command == "dev.refresh")
                    { await PublishAsync(original, () => { _project = project; _status = "Existing project descriptors refreshed."; }).ConfigureAwait(false); return; }
                    if (command == "dev.tree" && _manualReader is not null)
                    {
                        var entries = await original.AwaitAsync(InvokeOriginalSource(() => _manualReader.ListEntriesAsync(project, cancellationToken))).ConfigureAwait(false);
                        await PublishAsync(original, () => { _project = project; PublishProjectTree(entries); }).ConfigureAwait(false); return;
                    }
                    if (command == "dev.search" && _manualReader is not null)
                    {
                        var results = await original.AwaitAsync(InvokeOriginalSource(() => _manualReader.SearchMatchesAsync(project, query, searchOptions, cancellationToken))).ConfigureAwait(false);
                        await PublishAsync(original, () => PublishProjectSearch(project, results, searchGeneration)).ConfigureAwait(false); return;
                    }
                    if (command == "dev.revert-pass")
                    {
                        original.DemandPublication();
                        var inverse = InvokeOriginalSource(() => _review!.PrepareRevert(editor!.EditorId, editor.LastAppliedChangeSetId!.Value, editor.DraftRevision));
                        await PublishAsync(original, () => { _editor = inverse; StoreSelectedTab(); _status = "Only the selected pass inverse was staged. Review it before applying."; }).ConfigureAwait(false); return;
                    }
                    var context = await CaptureContextAsync(original, cancellationToken).ConfigureAwait(false);
                    original.DemandPublication(); // Last presentation admission; business token remains explicit thereafter.
                    if (command is "dev.open-source" or "dev.select-source-tab")
                    {
                        // A tab switch performs the same genuine authorized fresh read as Open;
                        // it never discloses a cached document merely from a copied view ID.
                        if (searchSelection is not null) DemandSearchSelection(searchSelection, project);
                        var document = await original.AwaitAsync(InvokeOriginalSource(() => _resolveDocument(project, path, cancellationToken))).ConfigureAwait(false);
                        if (!document.Succeeded || document.Value is null) throw new InvalidOperationException(document.Error?.Message ?? "No current registered document identity was resolved.");
                        original.DemandPublication();
                        var preparedReply = await RunTaskSourceAsync(original, project, context,
                            () => _review!.PrepareOpenAsync(project.Reference, context, document.Value, _businessToken), _businessToken).ConfigureAwait(false);
                        var prepared = DemandValue(preparedReply); Exception? openFailure = null;
                        try
                        {
                            if (searchSelection is not null) DemandSearchOpenedContent(searchSelection, project, prepared.Snapshot.OriginalText);
                            var published = false;
                            await PublishAsync(original, () =>
                            {
                                original.DemandPublication();
                                if (searchSelection is not null) DemandSearchOpenedContent(searchSelection, project, prepared.Snapshot.OriginalText);
                                StoreSelectedTab();
                                var tab = _sourceTabs.SingleOrDefault(item => item.Editor is { } owned && owned.Project == project.Reference &&
                                    owned.Document == prepared.Snapshot.Document);
                                var preview = tab is null ? _sourceTabs.FirstOrDefault(item => !item.IsPinned && !IsTabDirty(item)) : null;
                                if (tab is null && _sourceTabs.Count - (preview is null ? 0 : 1) >= DeveloperSourceReviewSession.MaximumOpenDocuments)
                                    throw new InvalidOperationException("Close a saved source tab before opening another document.");
                                if (preview is not null && preview.Editor is null)
                                    throw new InvalidOperationException("The preview has no current original source editor owner.");
                                // All view/query/hash/capacity checks precede enrollment. The owner
                                // atomically exchanges only this exact clean, settled preview.
                                var value = DemandValue(InvokeOriginalSource(() => _review!.AcceptPreparedOpen(prepared, preview?.Editor)));
                                if (tab is null)
                                {
                                    tab = new(value.EditorId, value.Document.RelativePath, value, null);
                                    if (preview is not null) _sourceTabs.Remove(preview);
                                    _sourceTabs.Add(tab);
                                }
                                else tab.Editor = value;
                                _project = project; SelectTab(tab); published = true;
                                _status = searchSelection is null ? "Complete source opened through its current owner. Other tabs retain their drafts and review state."
                                    : $"Opened matched saved source at line {searchSelection.Match.LineNumber}; any existing unsaved draft is preserved and may differ from the saved match.";
                            }).ConfigureAwait(false);
                            if (published) await SaveCleanSessionAsync(original, project, cancellationToken).ConfigureAwait(false);
                            return;
                        }
                        catch (Exception error) { openFailure = error; throw; }
                        finally
                        {
                            try { InvokeOriginalSource(() => { _review!.AbandonPreparedOpen(prepared); return true; }); }
                            catch (Exception cleanup) when (openFailure is not null)
                            { throw new AggregateException("Source open refusal and pending observation cleanup both failed.", openFailure, cleanup); }
                        }
                    }
                    if (command is "dev.preview-edit" or "dev.apply-edit")
                    {
                        var actual = RunTaskSourceAsync(original, project, context, () => command == "dev.preview-edit"
                            ? _review!.PreviewAsync(editor!.EditorId, editor.DraftRevision, context, _businessToken)
                            : _review!.ApplyAsync(editor!.EditorId, pass!.ChangeSetId, editor.DraftRevision, context, _businessToken), _businessToken);
                        var reply = await original.AwaitAsync(actual).ConfigureAwait(false);
                        var value = DemandValue(reply); OriginalSourcePass = value; OriginalDeveloperObservation = value.OriginalObservation;
                        await PublishAsync(original, () => { _pass = value; _editor = _review!.GetSnapshot(editor!.EditorId); StoreSelectedTab(); _status = value.State == DeveloperSourceChangeState.Applied ? "This exact pass was acknowledged. Later repository changes were preserved." : "Exact before/after reviewed. Apply requires this current draft revision."; }).ConfigureAwait(false);
                        if (value.State == DeveloperSourceChangeState.Applied)
                            await SaveCleanSessionAsync(original, project, cancellationToken).ConfigureAwait(false);
                        return;
                    }
                    Task<DeveloperOperationResult<DeveloperActionObservation>> operation = RunTaskSourceAsync(original, project, context, () => command switch
                    {
                        "dev.tree" => _development.ListFilesAsync(project.Reference, context, cancellationToken: _businessToken),
                        "dev.search" => _development.SearchFilesAsync(project.Reference, context, query, cancellationToken: _businessToken),
                        "dev.run-command" => _development.RunTerminalAsync(project.Reference, context, terminal, cancellationToken: _businessToken),
                        "dev.run-tests" => _development.RunTestsAsync(project.Reference, context, test, cancellationToken: _businessToken),
                        "dev.run-stage" => _development.RunStageAsync(project.Reference, context, stageTask,
                            project.Workspace.Tasks.Single(value => value.TaskId == stageTask).Revision, stage, _businessToken),
                        "dev.git-status" => _development.GitAsync(project.Reference, context, DeveloperGitOperation.Status, cancellationToken: _businessToken),
                        "dev.git-diff" => _development.GitAsync(project.Reference, context, DeveloperGitOperation.Diff, cancellationToken: _businessToken),
                        "dev.git-branches" => _development.GitAsync(project.Reference, context, DeveloperGitOperation.Branches, cancellationToken: _businessToken),
                        "dev.create-branch" => _development.GitAsync(project.Reference, context, DeveloperGitOperation.CreateBranch, branch, _businessToken),
                        "dev.switch-branch" => _development.GitAsync(project.Reference, context, DeveloperGitOperation.SwitchBranch, branch, _businessToken),
                        _ => throw new InvalidOperationException("Unknown maintained Dev action.")
                    }, _businessToken);
                    var observed = DemandValue(await original.AwaitAsync(operation).ConfigureAwait(false));
                    OriginalDeveloperObservation = observed;
                    await PublishAsync(original, () =>
                    {
                        _project = project;
                        if (command == "dev.tree") _files = observed.OriginalToolResult?.Output ?? "No original tree output was returned.";
                        else if (command == "dev.search") _searchResults = observed.OriginalToolResult?.Output ?? "No original search output was returned.";
                        else SetProcessObservation(observed);
                        _status = observed.RequiresOutcomeInspection ? "The original outcome requires inspection; it will not be automatically replayed." : "Original operation recorded in the same task.";
                    }).ConfigureAwait(false);
                }
                catch (Exception error) { original.Retain(error); }
                finally
                {
                    lock (_gate) _pending = false;
                    // Preserve the body's causes if a later notification also fails.
                    if (IsCurrent(generation))
                    {
                        Task? notification = null;
                        try { notification = PublishAsync(original, () => { }); await notification.ConfigureAwait(false); }
                        catch (Exception error) { original.Capture(notification, error); }
                    }
                    original.ThrowRetained();
                }
            }));
        }
        catch { lock (_gate) _pending = false; throw; }
    }
    private void InvalidateProjectSearch()
    {
        ++_searchGeneration; _structuredSearch = null; _searchMatches.Clear();
        _searchResults = "Search changed. Run Search files to observe current saved source.";
    }
    private void PublishProjectSearch(DeveloperResolvedProject project, DeveloperProjectSearchResult result, long queryGeneration)
    {
        if (_searchGeneration != queryGeneration || !StringComparer.Ordinal.Equals(_searchQuery, result.Query) ||
            result.Options != new DeveloperProjectSearchOptions(_searchUseRegex, _searchMatchCase) ||
            project.Reference != result.Project || _project.Reference != project.Reference)
            throw new InvalidOperationException("The project or search changed while reading. Previous source tabs are preserved; search again.");
        _project = project; _structuredSearch = result; _searchMatches.Clear();
        for (var i = 0; i < result.Matches.Count; i++)
            _searchMatches.Add(result.ObservationId.ToString("D") + ":" + i, result.Matches[i]);
        _searchResults = (result.HasMore ? "Showing the first 200 matching lines; narrow the query." : $"{result.Matches.Count} matching saved-source lines.") +
            $" Read {result.ReadableFiles} text files; {result.SkippedFiles} non-text/unavailable captured files skipped. " +
            (result.Options.UseRegex ? "Per-line regular expression. " : "Literal text. ") + "Open a match to read its current saved source.";
        _status = "Searched saved project files. Your unsaved drafts are preserved.";
    }
    private void DemandSearchSelection(SearchSelection selection, DeveloperResolvedProject project)
    {
        lock (_gate)
        {
            if (!ReferenceEquals(_structuredSearch, selection.Observation) || _searchGeneration != selection.Generation ||
                selection.Observation.Project != project.Reference || _project.Reference != project.Reference ||
                !_searchMatches.Values.Any(value => ReferenceEquals(value, selection.Match)))
                throw new InvalidOperationException("This search result is stale. Search again before opening its source.");
        }
    }
    private void DemandSearchOpenedContent(SearchSelection selection, DeveloperResolvedProject project, string text)
    {
        DemandSearchSelection(selection, project);
        if (!StringComparer.Ordinal.Equals(DeveloperManualSourceEditOwner.Sha256(text), selection.Match.ContentSha256))
            throw new InvalidOperationException("The matched saved source changed. Its existing draft is preserved; search again.");
    }

    /// <summary>Manual (no-AI) action dispatch: confined read/tree/search over the saved project root
    /// plus refresh. Every other action is unavailable and fails closed with no effects — mutations
    /// and process execution require a separate current authorized Task/execution authority.</summary>
    private ValueTask DispatchManualAsync(string command, string path, string query,
        string manualBeforeText, string manualDraftText, string manualBeforeSha256, string manualAfterSha256,
        bool manualReviewed, long manualReviewRevision, IReadOnlyList<DeveloperManualSaveRequest>? saveReviewed,
        long generation, DeveloperProjectSearchOptions searchOptions, long searchGeneration,
        SearchSelection? searchSelection, CancellationToken cancellationToken)
    {
        return new(_work.RunAsync(async original =>
        {
            original.BindPublicationGuard(() => IsCurrent(generation));
            try
            {
                original.DemandPublication(); cancellationToken.ThrowIfCancellationRequested();
                var project = await ResolveProjectAsync(original, cancellationToken).ConfigureAwait(false);
                if (command == "dev.refresh")
                { await PublishAsync(original, () => { _project = project; _status = "Existing project descriptors refreshed."; }).ConfigureAwait(false); return; }
                if (command == "dev.tree")
                {
                    var entries = await original.AwaitAsync(InvokeOriginalSource(() => _manualReader!.ListEntriesAsync(project, cancellationToken))).ConfigureAwait(false);
                    await PublishAsync(original, () => { _project = project; PublishProjectTree(entries); }).ConfigureAwait(false); return;
                }
                if (command == "dev.search")
                {
                    var results = await original.AwaitAsync(InvokeOriginalSource(() => _manualReader!.SearchMatchesAsync(project, query, searchOptions, cancellationToken))).ConfigureAwait(false);
                    await PublishAsync(original, () => PublishProjectSearch(project, results, searchGeneration)).ConfigureAwait(false); return;
                }
                if (command is "dev.open-source" or "dev.select-source-tab")
                {
                    if (searchSelection is not null) DemandSearchSelection(searchSelection, project);
                    var text = await original.AwaitAsync(InvokeOriginalSource(() => _manualReader!.ReadFileAsync(project, path, cancellationToken))).ConfigureAwait(false);
                    if (searchSelection is not null) DemandSearchOpenedContent(searchSelection, project, text);
                    var beforeSha = DeveloperManualSourceEditOwner.Sha256(text);
                    await PublishAsync(original, () =>
                    {
                        if (searchSelection is not null) DemandSearchSelection(searchSelection, project);
                        StoreSelectedTab();
                        var tab = _sourceTabs.SingleOrDefault(item => StringComparer.Ordinal.Equals(item.Path, path));
                        if (tab?.Manual is { } retained && !StringComparer.Ordinal.Equals(retained.Baseline, text))
                            throw new InvalidOperationException("The source changed outside this tab. Its retained draft and review were preserved; reopen after resolving the conflict.");
                        if (tab is null)
                        {
                            ReplaceCleanPreview();
                            if (_sourceTabs.Count >= DeveloperSourceReviewSession.MaximumOpenDocuments)
                                throw new InvalidOperationException("Close a saved source tab before opening another document.");
                            tab = new(Guid.NewGuid(), path, null, new(text, text, text, "", beforeSha, "", 1, -1, false));
                            _sourceTabs.Add(tab);
                        }
                        _project = project; SelectTab(tab);
                        _status = searchSelection is null ? "Manual source opened through the genuine Home read owner. Other tabs retain their drafts and review state."
                            : $"Opened matched saved source at line {searchSelection.Match.LineNumber}; any existing unsaved draft is preserved and may differ from the saved match.";
                    }).ConfigureAwait(false);
                    await SaveCleanSessionAsync(original, project, cancellationToken).ConfigureAwait(false); return;
                }
                if (command == "dev.save-reviewed")
                {
                    var editor = _manualEditor ?? throw new InvalidOperationException("The genuine manual edit owners are not configured.");
                    var requests = saveReviewed ?? throw new InvalidOperationException("No owned dirty-tab snapshot was captured.");
                    original.DemandPublication(); // Last UI admission; accepted business token is unchanged thereafter.
                    var batch = await original.AwaitAsync(InvokeOriginalSource(() => editor.SaveReviewedAsync(project, requests,
                        body => InvokeOriginalSource(() => { body(); return true; }), _businessToken))).ConfigureAwait(false);
                    // Private custody/reconciliation must survive presentation withdrawal.
                    // PublishAsync alone may skip an inactive view after a real commit.
                    lock (_gate)
                    {
                        _originalManualSaveBatches.RemoveAll(prior => prior.All(value => value.OriginalFailures.Count == 0 &&
                            value.OriginalSources.All(source => source.IsCompletedSuccessfully)));
                        _originalManualSaveBatches.Add(batch); // Failed raw sources survive later user actions.
                        OriginalManualSaveBatch = batch;
                        ReconcileOriginalManualSaves(batch);
                        _saveReviewedStatus = string.Join("\n", batch.Select(value =>
                            value.Request.RelativePath + ": " + (value.State == DeveloperManualSaveState.NeedsReview ? "Needs review" : value.State.ToString()) + " — " + value.Detail));
                        var saved = batch.Count(value => value.State == DeveloperManualSaveState.Saved);
                        var dirty = _sourceTabs.Count(IsTabDirty);
                        _status = $"Save reviewed: {saved} / {batch.Count} source writes verified; {dirty} open drafts remain unsaved.";
                    }
                    foreach (var failure in batch.SelectMany(value => value.OriginalFailures)) original.Retain(failure);
                    await PublishAsync(original, () => { }).ConfigureAwait(false);
                    // Existing canonical CAS/execution fence remains the sole layout
                    // persistence path and refuses while any live draft is dirty.
                    await SaveCleanSessionAsync(original, project, _businessToken).ConfigureAwait(false);
                    return;
                }
                if (command == "dev.preview-edit")
                {
                    var afterSha = DeveloperManualSourceEditOwner.Sha256(manualDraftText);
                    await PublishAsync(original, () =>
                    {
                        _manualAfterText = manualDraftText; _manualAfterSha256 = afterSha; _manualReviewed = true;
                        // Bind the review to the exact draft revision it observed, so any later
                        // source/file/project/draft/revision change invalidates it.
                        _manualReviewRevision = _manualDraftRevision;
                        StoreSelectedTab();
                        _status = $"Exact before/after reviewed for {path}; expected current revision {manualBeforeSha256}, after revision {afterSha}.";
                    }).ConfigureAwait(false); return;
                }
                if (command == "dev.apply-edit")
                {
                    // Dirty/review correctness: the review is bound to the exact before hash and the
                    // draft revision it observed. Refuse if the file/draft/project changed after review.
                    if (!manualReviewed || manualReviewRevision < 0 || manualReviewRevision != _manualDraftRevision ||
                        !StringComparer.Ordinal.Equals(DeveloperManualSourceEditOwner.Sha256(manualDraftText), manualAfterSha256))
                        throw new InvalidOperationException("The reviewed edit is stale; source or draft changed after review — preview again before applying.");
                    var editor = _manualEditor ?? throw new InvalidOperationException("The genuine manual edit owners are not configured.");
                    var after = await original.AwaitAsync(InvokeOriginalSource(() => editor.ApplyAsync(project, path, manualBeforeSha256, manualDraftText, cancellationToken))).ConfigureAwait(false);
                    // Reopen verification: read the committed inode again through the genuine owner and
                    // confirm the exact reviewed text persisted with the same canonical identities.
                    var reopened = await original.AwaitAsync(InvokeOriginalSource(() => editor.ReadAsync(project, path, cancellationToken))).ConfigureAwait(false);
                    if (!StringComparer.Ordinal.Equals(reopened.Text, manualDraftText))
                        throw new InvalidOperationException("The applied edit did not persist exactly; reopen verification failed.");
                    await PublishAsync(original, () =>
                    {
                        // Typing can continue while the original write settles. The confirmed
                        // baseline advances, but a newer unsaved draft stays in its own tab.
                        var newerDraft = _manualDraftRevision != manualReviewRevision;
                        var retainedDraft = _manualDraftText;
                        _manualDraftBaseline = reopened.Text; _manualBeforeText = reopened.Text; _manualBeforeSha256 = after;
                        _manualDraftText = newerDraft ? retainedDraft : reopened.Text;
                        _manualAfterText = ""; _manualAfterSha256 = ""; _manualReviewed = false; _manualDraftRevision++; _manualReviewRevision = -1;
                        StoreSelectedTab();
                        _status = $"Reviewed edit applied and verified (post-write revision {after}); unrelated content preserved.";
                    }).ConfigureAwait(false);
                    await SaveCleanSessionAsync(original, project, cancellationToken).ConfigureAwait(false); return;
                }
                throw new InvalidOperationException("This operation requires a current authorized Build Task/execution authority; not available in a manual no-AI open.");
            }
            catch (Exception error) { original.Retain(error); }
            finally
            {
                lock (_gate) _pending = false;
                if (IsCurrent(generation))
                {
                    Task? notification = null;
                    try { notification = PublishAsync(original, () => { }); await notification.ConfigureAwait(false); }
                    catch (Exception error) { original.Capture(notification, error); }
                }
                original.ThrowRetained();
            }
        }));
    }

    private IEnumerable<DeveloperProjectTreeEntry> ProjectFolderEntries() => _projectDirectoryEntries;
    private void RebuildProjectFolderEntries() => _projectDirectoryEntries =
        (_projectEntries ?? []).Where(entry => StringComparer.Ordinal.Equals(entry.ParentPath, _projectFolder))
            .OrderByDescending(entry => entry.IsDirectory).ThenBy(entry => entry.Name, StringComparer.Ordinal).ToArray();
    private void PublishProjectTree(IReadOnlyList<DeveloperProjectTreeEntry> entries)
    {
        _projectEntries = entries;
        if (!string.IsNullOrEmpty(_projectFolder) && !entries.Any(entry => entry.IsDirectory && entry.RelativePath == _projectFolder)) _projectFolder = "";
        _projectEntryPage = 0; RebuildProjectFolderEntries();
        _status = "Project explorer refreshed through the genuine Home READ and captured Files root. Opening a file reads it again with current authorization.";
    }
    private ValueTask DispatchProjectFolderAsync(string? folder, string command, long generation) =>
        new(_work.RunAsync(async original =>
        {
            original.BindPublicationGuard(() => IsCurrent(generation));
            try
            {
                await PublishAsync(original, () =>
                {
                    if (folder is not null)
                    {
                        if (folder.Length != 0 && _projectEntries?.Any(entry => entry.IsDirectory && entry.RelativePath == folder) != true)
                            throw new InvalidOperationException("The project folder is no longer in this captured tree. Refresh project files.");
                        _projectFolder = folder; _projectEntryPage = 0; RebuildProjectFolderEntries();
                    }
                    else if (command == "dev.project-entries-previous") _projectEntryPage = Math.Max(0, _projectEntryPage - 1);
                    else if (command == "dev.project-entries-next" && ProjectFolderEntries().Count() > (_projectEntryPage + 1) * ProjectExplorerPageSize) _projectEntryPage++;
                    _status = "Browsing the captured project tree; source tabs and drafts remain unchanged.";
                }).ConfigureAwait(false);
            }
            catch (Exception error) { original.Retain(error); }
            finally
            {
                lock (_gate) _pending = false;
                if (IsCurrent(generation))
                    try { await PublishAsync(original, () => { }).ConfigureAwait(false); }
                    catch (Exception error) { original.Retain(error); }
                original.ThrowRetained();
            }
        }));

    private SourceTab? CurrentSourceTab() => _sourceTabs.SingleOrDefault(tab => tab.Id == _selectedSourceTab);
    private void ReconcileOriginalManualSaves(IReadOnlyList<DeveloperManualSaveOutcome> batch)
    {
        StoreSelectedTab();
        foreach (var outcome in batch.Where(value => value.State == DeveloperManualSaveState.Saved))
        {
            var request = outcome.Request;
            var tab = _sourceTabs.SingleOrDefault(value => value.Id == request.TabId && StringComparer.Ordinal.Equals(value.Path, request.RelativePath));
            if (tab?.Manual is not { } live || DeveloperManualSourceEditOwner.ReconcileSavedDraft(outcome,
                live.Baseline, live.BeforeSha256, live.Draft, live.DraftRevision) is not { } confirmed) continue;
            // Only actual exact reopened commits advance the baseline. Typing
            // during another file's approval preserves the newer draft verbatim.
            tab.Manual = live with
            {
                Baseline = confirmed.Baseline, Before = confirmed.Baseline, BeforeSha256 = confirmed.BeforeSha256,
                Draft = confirmed.Draft, DraftRevision = confirmed.DraftRevision,
                After = "", AfterSha256 = "", Reviewed = false, ReviewRevision = -1
            };
        }
        if (CurrentSourceTab() is { } selected)
        {
            var typedPath = _sourcePath;
            SelectTab(selected); _sourcePath = typedPath; // Save never switches the user's current tab or path query.
        }
    }

    private bool IsTabDirty(SourceTab tab) => tab.Id == _selectedSourceTab
        ? _manual ? !StringComparer.Ordinal.Equals(_manualDraftText, _manualDraftBaseline) : _editor is { IsDirty: true }
        : tab.Editor?.IsDirty == true || tab.Manual is { } state && !StringComparer.Ordinal.Equals(state.Draft, state.Baseline);
    private void StoreSelectedTab()
    {
        if (CurrentSourceTab() is not { } tab) return;
        tab.Pass = _pass;
        if (_manual) tab.Manual = new(_manualDraftText, _manualDraftBaseline, _manualBeforeText, _manualAfterText,
            _manualBeforeSha256, _manualAfterSha256, _manualDraftRevision, _manualReviewRevision, _manualReviewed);
        else tab.Editor = _editor;
    }
    private void SelectTab(SourceTab tab)
    {
        _selectedSourceTab = tab.Id; _sourcePath = tab.Path; _pass = tab.Pass;
        _editor = tab.Editor;
        if (tab.Manual is { } state)
        {
            _manualDraftText = state.Draft; _manualDraftBaseline = state.Baseline;
            _manualBeforeText = state.Before; _manualAfterText = state.After;
            _manualBeforeSha256 = state.BeforeSha256; _manualAfterSha256 = state.AfterSha256;
            _manualDraftRevision = state.DraftRevision; _manualReviewRevision = state.ReviewRevision; _manualReviewed = state.Reviewed;
        }
    }
    private void ReplaceCleanPreview(SourceTab? replacement = null)
    {
        var preview = _sourceTabs.FirstOrDefault(tab => !ReferenceEquals(tab, replacement) && !tab.IsPinned && !IsTabDirty(tab));
        if (preview is not null) CloseTab(preview);
    }
    private void CloseTab(SourceTab tab)
    {
        if (IsTabDirty(tab)) throw new InvalidOperationException("Apply or revert this tab's unsaved draft before closing it.");
        if (tab.Editor is { } snapshot) _review!.CloseEditor(snapshot.EditorId, snapshot.DraftRevision);
        _sourceTabs.Remove(tab);
        if (_selectedSourceTab != tab.Id) return;
        _selectedSourceTab = null; _editor = null; _pass = null; _sourcePath = "";
        _manualDraftText = ""; _manualDraftBaseline = ""; _manualBeforeText = ""; _manualAfterText = "";
        _manualBeforeSha256 = ""; _manualAfterSha256 = ""; _manualReviewed = false; _manualDraftRevision++; _manualReviewRevision = -1;
    }
    private ValueTask DispatchTabPresentationAsync(string command, Guid? requestedTab, long generation) =>
        new(_work.RunAsync(async original =>
        {
            original.BindPublicationGuard(() => IsCurrent(generation));
            try
            {
                await PublishAsync(original, () =>
                {
                    StoreSelectedTab();
                    if (command == "dev.toggle-source-tab-pin")
                    {
                        var tab = CurrentSourceTab() ?? throw new InvalidOperationException("Select an open source tab first.");
                        tab.IsPinned = !tab.IsPinned;
                        _status = tab.IsPinned ? "Source tab pinned." : "Source tab unpinned; unsaved drafts are still protected.";
                    }
                    else
                    {
                        var tab = _sourceTabs.Single(item => item.Id == requestedTab);
                        CloseTab(tab); _status = "Saved tab closed. Select another tab to read it through its current owner.";
                    }
                }).ConfigureAwait(false);
                await SaveCleanSessionAsync(original, _project, _businessToken).ConfigureAwait(false);
            }
            catch (Exception error) { original.Retain(error); }
            finally
            {
                lock (_gate) _pending = false;
                if (IsCurrent(generation))
                    try { await PublishAsync(original, () => { }).ConfigureAwait(false); }
                    catch (Exception error) { original.Retain(error); }
                original.ThrowRetained();
            }
        }));

    private async Task RestoreCleanSessionAsync(DesktopOriginalWorkLifetime.Original original,
        DeveloperResolvedProject project, CancellationToken token)
    {
        lock (_gate)
        {
            if (_cleanSessionLoaded || _sourceTabs.Count != 0 || _editorSessionStore is null || _manualReader is null) return;
        }
        var loaded = await original.AwaitAsync(InvokeOriginalSource(() =>
            _manualReader.RestoreCleanEditorSessionAsync(_editorSessionStore, project, token))).ConfigureAwait(false);
        if (!loaded.Succeeded)
        {
            await PublishAsync(original, () =>
            {
                _cleanSessionLoaded = true; _cleanSessionBlocked = true;
                _status = loaded.Error!.Message + " You can still open files and edit. Apply unsaved changes before closing.";
            }).ConfigureAwait(false);
            return;
        }
        if (loaded.Value is not { } restored)
        {
            await PublishAsync(original, () => { _cleanSessionLoaded = true; _cleanSessionGeneration = 0; }).ConfigureAwait(false);
            return;
        }
        var prepared = new List<SourceTab>();
        string? refused = null;
        DeveloperCanonicalActionContext? context = null;
        foreach (var item in restored.Tabs)
        {
            original.DemandPublication();
            if (!_manual) context = await CaptureContextAsync(original, token).ConfigureAwait(false);
            if (_manual)
            {
                prepared.Add(new(Guid.NewGuid(), item.Observation.RelativePath, null,
                    new(item.Text, item.Text, item.Text, "", item.Observation.ContentSha256, "", 1, -1, false))
                { IsPinned = item.Observation.IsPinned });
                continue;
            }
            // Task-backed restoration re-resolves genuine Files identity and opens through the
            // SAME canonical Task source owner. The manual capture is not a substitute grant.
            var document = await original.AwaitAsync(InvokeOriginalSource(() =>
                _resolveDocument(project, item.Observation.RelativePath, token))).ConfigureAwait(false);
            if (!document.Succeeded || document.Value is null) { refused = document.Error?.Message ?? "A restored canonical document is unavailable."; break; }
            var opened = await RunTaskSourceAsync(original, project, context!,
                () => _review!.OpenAsync(project.Reference, context!, document.Value, _businessToken), _businessToken).ConfigureAwait(false);
            if (!opened.Succeeded || opened.Value is null) { refused = opened.Error?.Message ?? "A restored canonical source read was refused."; break; }
            var editor = opened.Value;
            prepared.Add(new(editor.EditorId, editor.Document.RelativePath, editor, null) { IsPinned = item.Observation.IsPinned });
            if (!StringComparer.OrdinalIgnoreCase.Equals(editor.OriginalSha256, item.Observation.ContentSha256))
            { refused = "A saved tab changed during its canonical source rebind. The previous session was preserved."; break; }
        }
        if (refused is not null)
        {
            // These are clean, fully awaited products, never dirty user buffers. Release their
            // exact review slots when a later document refuses the all-or-nothing restoration.
            foreach (var tab in prepared)
                if (tab.Editor is { } editor) InvokeOriginalSource(() => { _review!.CloseEditor(editor.EditorId, editor.DraftRevision); return true; });
            await PublishAsync(original, () =>
            {
                _cleanSessionLoaded = true; _cleanSessionBlocked = true; _status = refused + " Open project files explicitly.";
            }).ConfigureAwait(false);
            return;
        }
        await PublishAsync(original, () =>
        {
            if (_sourceTabs.Count != 0) throw new InvalidOperationException("Live source tabs appeared during restoration; they were preserved.");
            _sourceTabs.AddRange(prepared);
            if (prepared.SingleOrDefault(tab => StringComparer.Ordinal.Equals(tab.Path, restored.Session.SelectedRelativePath)) is { } selected)
                SelectTab(selected);
            _cleanSessionGeneration = restored.Session.Generation; _cleanSessionLoaded = true;
            _status = $"Restored {prepared.Count} clean source tabs. Apply unsaved changes before closing.";
        }).ConfigureAwait(false);
    }

    private async Task SaveCleanSessionAsync(DesktopOriginalWorkLifetime.Original original,
        DeveloperResolvedProject project, CancellationToken token)
    {
        // Coalesce the same current tab payload after open/pin/close/apply. Its driver
        // waits for this command fence, so the two canonical metadata writes cannot race.
        ScheduleManualRecoverySave(); ScheduleTaskRecoverySave();
        IReadOnlyList<DeveloperCleanEditorTab>? tabs = null;
        string? selected = null; long expected = 0;
        await PublishAsync(original, () =>
        {
            // Task-backed source observations may retain execution bindings. They can restore
            // through fresh owners, but session writes require a future source-issued rebind.
            if (!_manual || !_cleanSessionLoaded || _cleanSessionBlocked || _editorSessionStore is null || _manualReader is null) return;
            StoreSelectedTab();
            // Persist no dirty payload, and do not replace the previous restart state while
            // live drafts exist. Normal close still refuses through HasUnsavedWork.
            if (_sourceTabs.Any(IsTabDirty)) return;
            tabs = Array.AsReadOnly(_sourceTabs.Select(tab => new DeveloperCleanEditorTab(tab.Path,
                tab.Manual?.BeforeSha256 ?? tab.Editor!.OriginalSha256, tab.IsPinned)).ToArray());
            selected = CurrentSourceTab()?.Path; expected = _cleanSessionGeneration;
        }).ConfigureAwait(false);
        if (tabs is null || !original.IsPublicationCurrent) return;
        var saved = await original.AwaitAsync(InvokeOriginalSource(() =>
            _manualReader!.SaveCleanEditorSessionAsync(_editorSessionStore!, _editorSessionExecution, project, expected, tabs, selected, token))).ConfigureAwait(false);
        // A completed physical metadata update remains recorded even if the visible page was
        // withdrawn. This is private owner state, not publication to an inactive presentation.
        if (saved.Succeeded)
        { lock (_gate) _cleanSessionGeneration = saved.Value!.Generation; }
        else await PublishAsync(original, () =>
        {
            _cleanSessionBlocked = true;
            _status += " Tab layout was not saved: " + saved.Error!.Message;
        }).ConfigureAwait(false);
    }

    private async Task<DeveloperResolvedProject> ResolveProjectAsync(DesktopOriginalWorkLifetime.Original original, CancellationToken token)
    {
        var value = DemandValue(await original.AwaitAsync(InvokeOriginalSource(() => _development.ResolveAsync(_project.Reference, token))).ConfigureAwait(false));
        if (value.Reference != _project.Reference) throw new InvalidDataException("A different project revision was returned.");
        return value;
    }
    private async Task<DeveloperCanonicalActionContext> CaptureContextAsync(DesktopOriginalWorkLifetime.Original original, CancellationToken token)
    {
        var before = await original.AwaitAsync(InvokeOriginalSource(() => _canonical.GetAsync(_taskId, token))).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The original task is missing.");
        DemandSameContext(before);
        var attemptId = before.Attempts.LastOrDefault()?.Id ?? throw new InvalidOperationException("No actual task attempt is issued.");
        var admission = await original.AwaitAsync(InvokeOriginalSource(() => _canonical.GetIssuedAttemptAsync(_taskId, _runId, attemptId, token))).ConfigureAwait(false)
            ?? throw new UnauthorizedAccessException("The actual task attempt issuer is unavailable.");
        await original.AwaitAsync(InvokeOriginalSource(() => admission.Lease.RevalidateAsync(token).AsTask())).ConfigureAwait(false);
        var current = await original.AwaitAsync(InvokeOriginalSource(() => _canonical.GetAsync(_taskId, token))).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The original task was withdrawn.");
        DemandSameContext(current);
        if (current.PersistenceRevision != before.PersistenceRevision || current.Attempts.LastOrDefault()?.Id != attemptId)
            throw new InvalidOperationException("The task revision changed during command admission. Refresh its current observation.");
        await original.AwaitAsync(InvokeOriginalSource(() => admission.Lease.RevalidateAsync(token).AsTask())).ConfigureAwait(false);
        return new(_taskId, _runId, _contextId, attemptId, current.PersistenceRevision, Guid.NewGuid());
    }
    private void DemandSameContext(TaskExecutionSnapshot snapshot)
    {
        if (snapshot.TaskId != _taskId || snapshot.ExecutionId != _runId || snapshot.ContextId != _contextId)
            throw new InvalidDataException("The original Task/Run/context was replaced; this view cannot open its replacement implicitly.");
    }
    private static T DemandValue<T>(DeveloperOperationResult<T> result) where T : class => result.Succeeded && result.Value is { } value
        ? value : throw new InvalidOperationException(result.Error?.Message ?? "No original Dev result was returned.");
    private void SetProcessObservation(DeveloperActionObservation observed)
    {
        if (observed.OriginalProcessResult is not { } process)
        { _processStatus = "No original process result is available; no build or test success was inferred."; _processOutput = observed.OriginalToolResult?.Output ?? ""; return; }
        _processStatus = process.TimedOut ? "Original process timed out; outcome remains unresolved." : process.ExitCode == 0 ? "Original process exited 0." : $"Original process exited {process.ExitCode}; command/test failed.";
        _processOutput = process.StandardOutput + (string.IsNullOrEmpty(process.StandardError) ? "" : "\n[standard error]\n" + process.StandardError);
    }
    private Task PublishAsync(DesktopOriginalWorkLifetime.Original original, Action publication)
    {
        if (!original.IsPublicationCurrent) return Task.CompletedTask; // Actual observation already retained; presentation was withdrawn.
        var actual = Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (!original.IsPublicationCurrent) return;
            InvokeOriginalSource(() => { lock (_gate) publication(); return true; });
            NotifyOriginal(original);
        }).GetTask();
        return original.AwaitAsync(actual);
    }
    private void NotifyOriginal(DesktopOriginalWorkLifetime.Original original)
    {
        foreach (var callback in PropertyChanged?.GetInvocationList() ?? [])
        {
            if (!original.IsPublicationCurrent) break;
            try { InvokeOriginalSource(() => { ((PropertyChangedEventHandler)callback)(this, new(null)); return true; }); }
            catch (Exception error) { original.Retain(error); }
            if (!original.IsPublicationCurrent) break;
        }
        original.ThrowRetained();
    }
    private T InvokeOriginalSource<T>(Func<T> callback)
    {
        var stack = _sourceCallbacks ??= []; stack.Add(this);
        try { return callback(); }
        catch (OperationCanceledException original) { throw new AggregateException("An actual synchronous Dev callback returned no canceled original Task.", original); }
        finally { stack.RemoveAt(stack.Count - 1); }
    }
    public void DemandExternalOriginalRetirementJoin()
    {
        if (_sourceCallbacks?.Any(value => ReferenceEquals(value, this)) == true)
            throw new InvalidOperationException("An actual Dev page callback cannot join its encompassing close.");
        _work.DemandExternalClose(); _review?.DemandExternalOriginalRetirementJoin(); _development.DemandExternalOriginalRetirementJoin();
        _demandOriginalDocumentSourceJoin(); // Pure actual Files resolver guard; never global source retirement.
        _readinessChildGuard?.DemandExternalOriginalRetirementJoin();
        _taskRecoveryOwner?.DemandExternalOriginalJoin();
    }
    public void RequestRetirement() => _work.RequestRetirement();
    public Task CloseAndDrainAsync() { DemandExternalOriginalRetirementJoin(); return _work.CloseAndDrainAsync(); }
    private Task StopOriginalPresentationAsync()
    {
        lock (_gate) { _active = false; _generation++; }
        // These are presentation owners only; no global Dev/canonical retirement or token cancellation.
        var errors = new List<Exception>();
        if (_review is not null) try { _review.RequestRetirement(); } catch (Exception error) { errors.Add(error); }
        try { InvokeOriginalSource(() => { _readinessChild?.RequestRetirement(); return true; }); } catch (Exception error) { errors.Add(error); }
        if (_unresolvedReadinessChild is not null)
            errors.Add(new InvalidOperationException("An acquired readiness child has no validated original retirement port; teardown remains unresolved."));
        if (errors.Count != 0) throw new AggregateException("Dev presentation stop did not acknowledge all original owners.", errors);
        return Task.CompletedTask;
    }
    private async Task DetachOriginalPresentationAsync()
    {
        var errors = new List<Exception>();
        // Acquired business originals settled before this cleanup callback. Join the SAME
        // readiness and review children independently BEFORE the host/dispatcher resources.
        try { if (_review is not null) _reviewClose ??= _review.CloseAndDrainAsync(); } catch (Exception error) { errors.Add(error); }
        try { if (_readinessChild is not null) _readinessChildClose ??= _readinessChild.CloseAndDrainAsync(); }
        catch (Exception error) { errors.Add(error); }
        foreach (var actual in new[] { _reviewClose, _readinessChildClose }.Where(value => value is not null))
            try { await actual!.ConfigureAwait(false); }
            catch (Exception error) { foreach (var cause in actual!.Exception?.InnerExceptions ?? new[] { error }.AsEnumerable()) if (!errors.Any(value => ReferenceEquals(value, cause))) errors.Add(cause); }
        if (_taskRecoveryOwner is not null)
        {
            Task? close = null;
            try { close = _taskRecoveryOwner.CloseAndDrainAsync(); } catch (Exception error) { errors.Add(error); }
            if (close is not null) try { await close.ConfigureAwait(false); } catch (Exception error) { errors.Add(close.Exception ?? error); }
        }
        if (_unresolvedReadinessChild is not null) errors.Add(new InvalidOperationException("The original readiness child cannot certify its own terminal cleanup."));
        if (errors.Count != 0) throw new AggregateException("Dev child originals did not drain cleanly; the actual host is retained.", errors);
        try { _hostClose ??= _host.CloseOriginalAsync(); } catch (Exception error) { errors.Add(error); }
        if (_hostClose is not null)
            try { await _hostClose.ConfigureAwait(false); }
            catch (Exception error) { foreach (var cause in _hostClose.Exception?.InnerExceptions ?? new[] { error }.AsEnumerable()) if (!errors.Any(value => ReferenceEquals(value, cause))) errors.Add(cause); }
        if (errors.Count != 0) throw new AggregateException("Dev presentation originals did not drain cleanly.", errors);
        _detachOriginalDispatcher = InvokeOriginalSource(() => Dispatcher.UIThread.InvokeAsync(() =>
            InvokeOriginalSource(() => { if (ReferenceEquals(Content, _host)) Content = null; return true; })).GetTask());
        try { await _detachOriginalDispatcher.ConfigureAwait(false); }
        catch when (_detachOriginalDispatcher.IsFaulted) { throw _detachOriginalDispatcher.Exception!; }
    }
    public ValueTask DisposeAsync() => new(CloseAndDrainAsync());
}
