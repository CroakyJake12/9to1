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

namespace Haven.Desktop.Views.Pages.Development;

/// <summary>
/// Existing-project Dev editor and commands over the SAME canonical Task/Run. A maintained
/// Files resolver supplies actual document identity; paths are never turned into new file IDs.
/// Retirement joins acquired business originals with their configured token unchanged. It
/// withdraws this view only, never the global Dev service or canonical work. The shell owns
/// constructor acquisition and the real frame/readiness producer for this exact host.
/// </summary>
public sealed class DeveloperProjectWorkbenchPage : UserControl, IActivatablePage,
    ICuiWritableBindingContext, ICuiActionDispatcher, ICuiActionAvailability, INotifyPropertyChanged,
    IDesktopOriginalRetirementParticipant, IDesktopOriginalRetirementJoinGuard, IAsyncDisposable
{
    private readonly DeveloperTaskWorkspaceService _development;
    private readonly DeveloperSourceReviewSession _review;
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
    [ThreadStatic] private static List<DeveloperProjectWorkbenchPage>? _sourceCallbacks;
    private DeveloperResolvedProject _project;
    private DeveloperEditorSnapshot? _editor;
    private DeveloperSourceChangePass? _pass;
    private bool _active, _pending;
    private long _generation;
    private string _sourcePath = "", _searchQuery = "", _command = "", _testCommand = "";
    private string _stageTaskId = "", _stageId = "", _branchName = "";
    private string _files = "", _searchResults = "", _status = "", _processStatus = "", _processOutput = "";
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
        Action<DeveloperProjectWorkbenchPage>? retainOriginalAcquisition)
        : this(development, canonical, actualProject, actualTaskId, actualExecutionId, actualContextId, readiness,
            resolveOriginalDocument, demandOriginalDocumentSourceExternalJoin, explicitBusinessCancellationToken,
            retainOriginalAcquisition, null) { }

    internal DeveloperProjectWorkbenchPage(DeveloperTaskWorkspaceService development, TaskExecutionCoordinator canonical,
        DeveloperResolvedProject actualProject, Guid actualTaskId, Guid actualExecutionId, Guid actualContextId,
        ICuiSceneReadiness? readiness,
        Func<DeveloperResolvedProject, string, CancellationToken, Task<DeveloperOperationResult<DeveloperCodeDocument>>> resolveOriginalDocument,
        Action demandOriginalDocumentSourceExternalJoin, CancellationToken explicitBusinessCancellationToken,
        Action<DeveloperProjectWorkbenchPage>? retainOriginalAcquisition,
        Func<DeveloperProjectWorkbenchPage, ICuiSceneReadiness>? createOriginalReadinessChild)
    {
        _development = development ?? throw new ArgumentNullException(nameof(development));
        _canonical = canonical ?? throw new ArgumentNullException(nameof(canonical));
        _project = actualProject ?? throw new ArgumentNullException(nameof(actualProject));
        if (actualTaskId == Guid.Empty || actualExecutionId == Guid.Empty || actualContextId == Guid.Empty)
            throw new ArgumentException("The existing canonical Task/Run/context must be supplied together.");
        _taskId = actualTaskId; _runId = actualExecutionId; _contextId = actualContextId;
        if (readiness is null && createOriginalReadinessChild is null) throw new ArgumentNullException(nameof(readiness));
        _readiness = readiness!; // Genuine child is acquired after exact Page/Host custody below.
        _resolveDocument = resolveOriginalDocument ?? throw new ArgumentNullException(nameof(resolveOriginalDocument));
        _demandOriginalDocumentSourceJoin = demandOriginalDocumentSourceExternalJoin ?? throw new ArgumentNullException(nameof(demandOriginalDocumentSourceExternalJoin));
        _businessToken = explicitBusinessCancellationToken;
        _review = new(development); Document = DeveloperWorkbenchCuiDocument.Load();
        _controls = new();
        _controls.RegisterObjectRenderer("DeveloperReadonlySource", _ => new TextBox { IsReadOnly = true, AcceptsReturn = true });
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

    public CuiDocument Document { get; }
    internal CuiSceneHost OriginalHost => _host;
    internal long OriginalReadinessGeneration { get { lock (_gate) return _generation; } }
    internal (Guid TaskId, Guid ExecutionId, Guid ContextId) OriginalReadinessContext => (_taskId, _runId, _contextId);
    internal bool IsOriginalReadinessPresentationCurrent(CuiSceneHost exactHost, long originalGeneration) =>
        ReferenceEquals(exactHost, _host) && IsCurrent(originalGeneration);
    public event PropertyChangedEventHandler? PropertyChanged;
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
            await PublishAsync(original, () => { _project = project; _status = "Existing project opened. Commands use its canonical task and permissions."; }).ConfigureAwait(false);
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
            value = path switch
            {
                "ProjectTitle" => _project.Project.Name,
                "ProjectSummary" => $"Workspace {_project.Reference.WorkspaceId:D} / revision {_project.Reference.WorkspaceRevision}\nProject {_project.Reference.ProjectId:D} / revision {_project.Reference.ProjectRevision}\nRoot {_project.Reference.RootId:D}; {_project.Root.EnvironmentId}\nRepository {_project.Repository?.CanonicalRepositoryId ?? "not configured"}\nTask {_taskId:D}; run {_runId:D}",
                "Files" => _files, "SourcePath" => _sourcePath, "SearchQuery" => _searchQuery, "SearchResults" => _searchResults,
                "EditorTitle" => _editor is null ? "Choose an existing registered source document." : $"{_editor.Document.RelativePath}; draft revision {_editor.DraftRevision}" + (_editor.IsDirty ? " — unsaved draft" : ""),
                "DraftText" => _editor?.DraftText ?? "",
                "ReviewSummary" => _pass is null ? "Review before applying. Pass history is available for this open editor." : $"Pass {_pass.ChangeSetId:D}: {_pass.State}; before {_pass.BeforeSha256}; after {_pass.AfterSha256}" + (_pass.RevertsChangeSetId is { } inverse ? $"; reverts only pass {inverse:D}" : ""),
                "BeforeText" => _pass?.BeforeText ?? "", "AfterText" => _pass?.AfterText ?? "",
                "Command" => _command, "TestCommand" => _testCommand, "StageTaskId" => _stageTaskId, "StageId" => _stageId,
                "BranchName" => _branchName, "ProcessStatus" => _processStatus, "ProcessOutput" => _processOutput, "Status" => _status,
                "CanOpenSource" => IsActionAvailable("dev.open-source"), "CanPreview" => IsActionAvailable("dev.preview-edit"),
                "CanApply" => IsActionAvailable("dev.apply-edit"), "CanRevert" => IsActionAvailable("dev.revert-pass"), _ => null
            };
        }
        return path is "ProjectTitle" or "ProjectSummary" or "Files" or "SourcePath" or "SearchQuery" or "SearchResults" or
            "EditorTitle" or "DraftText" or "ReviewSummary" or "BeforeText" or "AfterText" or "Command" or "TestCommand" or
            "StageTaskId" or "StageId" or "BranchName" or "ProcessStatus" or "ProcessOutput" or "Status" or "CanOpenSource" or "CanPreview" or "CanApply" or "CanRevert";
    }
    public bool TrySetValue(string path, object? value)
    {
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
                        case "SourcePath": _sourcePath = text; break;
                        case "SearchQuery": _searchQuery = text; break;
                        case "DraftText":
                            if (_editor is null) throw new InvalidOperationException("Open a real complete source document first.");
                            _editor = _review.UpdateDraft(_editor.EditorId, _editor.DraftRevision, text); break;
                        case "Command": _command = text; break; case "TestCommand": _testCommand = text; break;
                        case "StageTaskId": _stageTaskId = text; break; case "StageId": _stageId = text; break; case "BranchName": _branchName = text; break;
                    }
                }
                return true;
            });
            NotifyOriginal(original);
        });
        return true;
    }
    public bool? IsActionAvailable(string command)
    {
        lock (_gate)
        {
            if (!_active || _work.IsRetiring || _pending) return false;
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
        long generation; string path, query, terminal, test, stageTask, stage, branch; DeveloperEditorSnapshot? editor; DeveloperSourceChangePass? pass;
        lock (_gate)
        {
            if (IsActionAvailable(command) != true) throw new InvalidOperationException("This Dev operation is unavailable.");
            _pending = true; generation = _generation; path = _sourcePath; query = _searchQuery; terminal = _command; test = _testCommand;
            stageTask = _stageTaskId; stage = _stageId; branch = _branchName; editor = _editor; pass = _pass;
        }
        try
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
                    if (command == "dev.revert-pass")
                    {
                        original.DemandPublication();
                        var inverse = InvokeOriginalSource(() => _review.PrepareRevert(editor!.EditorId, editor.LastAppliedChangeSetId!.Value, editor.DraftRevision));
                        await PublishAsync(original, () => { _editor = inverse; _status = "Only the selected pass inverse was staged. Review it before applying."; }).ConfigureAwait(false); return;
                    }
                    var context = await CaptureContextAsync(original, cancellationToken).ConfigureAwait(false);
                    original.DemandPublication(); // Last presentation admission; business token remains explicit thereafter.
                    if (command == "dev.open-source")
                    {
                        var document = await original.AwaitAsync(InvokeOriginalSource(() => _resolveDocument(project, path, cancellationToken))).ConfigureAwait(false);
                        if (!document.Succeeded || document.Value is null) throw new InvalidOperationException(document.Error?.Message ?? "No current registered document identity was resolved.");
                        original.DemandPublication();
                        var opened = await original.AwaitAsync(InvokeOriginalSource(() => _review.OpenAsync(project.Reference, context, document.Value, _businessToken))).ConfigureAwait(false);
                        var value = DemandValue(opened);
                        await PublishAsync(original, () => { _project = project; _editor = value; _pass = null; _status = "Complete original source opened; edit its draft, then review."; }).ConfigureAwait(false); return;
                    }
                    if (command is "dev.preview-edit" or "dev.apply-edit")
                    {
                        var actual = InvokeOriginalSource(() => command == "dev.preview-edit"
                            ? _review.PreviewAsync(editor!.EditorId, editor.DraftRevision, context, _businessToken)
                            : _review.ApplyAsync(editor!.EditorId, pass!.ChangeSetId, editor.DraftRevision, context, _businessToken));
                        var reply = await original.AwaitAsync(actual).ConfigureAwait(false);
                        var value = DemandValue(reply); OriginalSourcePass = value; OriginalDeveloperObservation = value.OriginalObservation;
                        await PublishAsync(original, () => { _pass = value; _editor = _review.GetSnapshot(editor!.EditorId); _status = value.State == DeveloperSourceChangeState.Applied ? "This exact pass was acknowledged. Later repository changes were preserved." : "Exact before/after reviewed. Apply requires this current draft revision."; }).ConfigureAwait(false); return;
                    }
                    Task<DeveloperOperationResult<DeveloperActionObservation>> operation = InvokeOriginalSource(() => command switch
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
                    });
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
        _work.DemandExternalClose(); _review.DemandExternalOriginalRetirementJoin(); _development.DemandExternalOriginalRetirementJoin();
        _demandOriginalDocumentSourceJoin(); // Pure actual Files resolver guard; never global source retirement.
        _readinessChildGuard?.DemandExternalOriginalRetirementJoin();
    }
    public void RequestRetirement() => _work.RequestRetirement();
    public Task CloseAndDrainAsync() { DemandExternalOriginalRetirementJoin(); return _work.CloseAndDrainAsync(); }
    private Task StopOriginalPresentationAsync()
    {
        lock (_gate) { _active = false; _generation++; }
        // These are presentation owners only; no global Dev/canonical retirement or token cancellation.
        var errors = new List<Exception>();
        try { _review.RequestRetirement(); } catch (Exception error) { errors.Add(error); }
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
        try { _reviewClose ??= _review.CloseAndDrainAsync(); } catch (Exception error) { errors.Add(error); }
        try { if (_readinessChild is not null) _readinessChildClose ??= _readinessChild.CloseAndDrainAsync(); }
        catch (Exception error) { errors.Add(error); }
        foreach (var actual in new[] { _reviewClose, _readinessChildClose }.Where(value => value is not null))
            try { await actual!.ConfigureAwait(false); }
            catch (Exception error) { foreach (var cause in actual!.Exception?.InnerExceptions ?? new[] { error }.AsEnumerable()) if (!errors.Any(value => ReferenceEquals(value, cause))) errors.Add(cause); }
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
