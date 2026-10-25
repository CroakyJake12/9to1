using System.ComponentModel;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CakeOS.Cui;
using CakeOS.Cui.Compiler;
using CakeOS.Cui.Language;
using CakeOS.Cui.Runtime;
using CakeOS.Cui.Themes;
using Haven.Core;
using Haven.Application;
using HavenOS.Apps.Spaces.Tasks;

namespace Haven.Desktop.Views.Pages.Tasks;

/// <summary>Mounts only authored CUI. Data, callbacks and exact draft revision remain owned
/// by the same task page; no native readiness or business completion is manufactured.</summary>
public sealed class SpaceTasksDashboardCuiScene : UserControl, ICuiWritableBindingContext,
    ICuiActionDispatcher, ICuiActionAvailability, ICuiRepeatItemBindingContext, INotifyPropertyChanged
{
    private CuiControlLoader? _loader;
    private readonly Action<Action> _ownCallback;
    private readonly Action _demandWrite;
    private readonly Guid _spaceId;
    private IReadOnlyList<ITaskContextObservation> _rows = [];
    private TaskShownScope _scope;
    private bool _canChangeScope, _canReadAll, _canNext, _canPrevious;
    private string _instruction = "", _status = "";
    private long _instructionVersion;
    private bool _busy, _canDelegate, _canBlank, _retired;
    private Task? _close;
    private Exception? _loadFailure;
    public SpaceTasksDashboardCuiScene(Guid spaceId, Action<Action> ownCallback, Action demandWrite)
    {
        _spaceId = spaceId; _ownCallback = ownCallback; _demandWrite = demandWrite;
    }
    internal void Initialize()
    {
        Dispatcher.UIThread.VerifyAccess(); _demandWrite();
        if (_loadFailure is { } prior) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(prior).Throw();
        if (Root is not null) return;
        try { LoadOriginal(); }
        catch (Exception cause) { _loadFailure = cause; throw; }
    }
    private void LoadOriginal()
    {
        // The authored details Toggle makes this a stateful document. The canonical
        // build deliberately excludes stateful raw resources; consume its admitted V2
        // semantics exactly like the shared composer, with no raw/V1 fallback.
        const string source = "UI/Tasks/SpaceTasksDashboard.cui";
        using var stream = typeof(SpaceTaskWorkspaceService).Assembly.GetManifestResourceStream(CuiCompiledArtifactCodec.ResourceLogicalName)
            ?? throw new InvalidDataException("The compiled Tasks dashboard CUI bundle is missing.");
        using var buffer = new MemoryStream(); stream.CopyTo(buffer);
        var artifact = CuiCompiledArtifactCodec.ReadArtifact(buffer.ToArray());
        CuiRuntimeCompatibility.ValidateArtifactHostSupport(artifact, CuiSourceSpan.At(source, 1, 1));
        var compiled = artifact.Documents.SingleOrDefault(document => document.Source == source)
            ?? throw new InvalidDataException("The admitted CUI bundle has no Tasks dashboard document.");
        var document = compiled.Semantics.ToCuiDocument();
        if (document.Components.Count == 0) throw new InvalidDataException("The compiled Tasks dashboard CUI is empty.");
        var appearance = CuiThemeScopeApplier.DetectAppearance();
        Resources.MergedDictionaries.Add(CuiSceneVisualResources.Create("Tasks", appearance));
        _loader = new CuiControlLoader();
        _loader.SetSurface("Tasks"); _loader.SetAppearance(appearance);
        _loader.SetBindingContext(this); _loader.SetActionDispatcher(this);
        var loaded = _loader.TryLoad(document);
        Root = loaded.Root;
        if (Root is null || loaded.Diagnostics.Any(item => item.Severity == CuiDiagnosticSeverity.Error))
            throw new InvalidDataException("The authored Tasks dashboard CUI could not be loaded.");
        _loader.WireBindings(Root); Content = Root;
    }
    public new event PropertyChangedEventHandler? PropertyChanged;
    public Control? Root { get; private set; }
    public TextBox Instruction => Find<TextBox>("tasks-new-instruction");
    public Button DelegateTask => Find<Button>("tasks-delegate");
    public Button DetailsToggle => Find<Button>("tasks-details-toggle");
    public Control RecentRows => Find<Control>("tasks-recent-rows");
    public long InstructionVersion => _instructionVersion;
    internal Task WhenActionsIdleAsync() => _loader?.WhenActionsIdleAsync() ?? Task.CompletedTask;
    public event EventHandler<string>? DelegateRequested;
    public event EventHandler? NewBlankTaskRequested;
    public event EventHandler<ITaskContextObservation>? RecentTaskRequested;
    public event EventHandler<TaskShownScope>? ScopeRequested;
    public event EventHandler<bool>? PageRequested;
    public event EventHandler? RefreshRequested;
    private T Find<T>(string id) where T : Control => Root?.GetVisualDescendants().OfType<T>()
        .Single(control => AutomationProperties.GetAutomationId(control) == id)
        ?? throw new InvalidOperationException("The authored dashboard control is unavailable.");
    private void Notify() => PropertyChanged?.Invoke(this, new(null));
    private void Write(Action effect, Action? guard = null)
    { _demandWrite(); guard?.Invoke(); effect(); _demandWrite(); guard?.Invoke(); Notify(); _demandWrite(); guard?.Invoke(); }
    public void SetRecent(IReadOnlyList<ITaskContextObservation> rows, Action? guard = null) => Write(() => _rows = rows.ToArray(), guard);
    public void SetScope(TaskShownScope scope, bool canChange, bool canReadAll, bool canNext, bool canPrevious, Action? guard = null) =>
        Write(() => { _scope = scope; _canChangeScope = canChange; _canReadAll = canReadAll; _canNext = canNext; _canPrevious = canPrevious; }, guard);
    public void SetBusy(bool value) => Write(() => _busy = value);
    public void SetStartAvailability(bool canDelegate, bool canBlank, Action? guard = null) =>
        Write(() => { _canDelegate = canDelegate; _canBlank = canBlank; }, guard);
    public void SetStatus(string value, Action? guard = null) => Write(() => _status = value, guard);
    public void ClearInstruction(long expectedVersion) => Write(() =>
    { if (_instructionVersion == expectedVersion) { _instruction = ""; _instructionVersion++; } });
    internal void RequestRetirement()
    { Dispatcher.UIThread.VerifyAccess(); _retired = true; if (Root is not null) Root.IsEnabled = false; Notify(); }
    public bool TryGetValue(string path, out object? value)
    {
        value = path switch
        {
            "Instruction" => _instruction, "Rows" => _rows, "HasRows" => _rows.Count != 0,
            "ScopeLabel" => _spaceId == SpaceRegistry.TasksSpaceId ? _scope == TaskShownScope.AllTasks ? "All Tasks" : "Task Space Only" : "Current Space",
            "CanChangeScope" => _canChangeScope && !_retired,
            "CanReadAllTasks" => _canChangeScope && _canReadAll && !_retired,
            "IsTaskSpaceOnly" => _scope == TaskShownScope.TaskSpaceOnly,
            "IsAllTasks" => _scope == TaskShownScope.AllTasks,
            "CanNextPage" => _canNext && !_busy && !_retired,
            "CanPreviousPage" => _canPrevious && !_busy && !_retired,
            "EmptyLabel" => _scope == TaskShownScope.AllTasks ? "No authorized tasks are available on this page." : "No task conversations are available in this Space. Start a task above.",
            "Status" => _status, "CanEditInstruction" => !_busy && !_retired,
            "CanDelegate" => !_busy && !_retired && _canDelegate && !string.IsNullOrWhiteSpace(_instruction),
            "CanOpenBlank" => !_busy && !_retired && _canBlank, "CanRefresh" => !_retired && !_busy,
            _ => null
        };
        return path is "Instruction" or "Rows" or "HasRows" or "ScopeLabel" or "Status" or "CanEditInstruction" or "CanDelegate" or "CanOpenBlank" or "CanRefresh" or "CanChangeScope" or "CanReadAllTasks" or "IsTaskSpaceOnly" or "IsAllTasks" or "CanNextPage" or "CanPreviousPage" or "EmptyLabel";
    }
    public bool TrySetValue(string path, object? value)
    {
        if (path != "Instruction" || value is not string text || _retired) return false;
        if (_instruction == text) return true;
        _ownCallback(() => { if (_instruction != text) { _instruction = text; _instructionVersion++; Notify(); } });
        return true;
    }
    public bool TryGetItemValue(object item, string path, out object? value)
    {
        value = null;
        if (item is not ITaskContextObservation row) return false;
        var task = row.Snapshot;
        value = path switch
        {
            "id" => task is null ? "context-" + row.ContextId.ToString("D") : "task-" + task.TaskId.ToString("D"), "observation" => row, "title" => row.Title,
            "openLabel" => "Open existing task " + row.Title,
            "accessibleName" => row.Title + "; " + Status(row),
            "status" => Status(row), "completed" => task?.State == TaskExecutionLifecycle.Completed,
            "summary" => task is null ? "No canonical task has been started." : task.PromptSummary,
            "origin" => row is AuthorizedTaskContextObservation external
                ? external.OriginSpaceName is { Length: > 0 } name ? "From " + name
                    : external.OriginalConversation is not null ? "Original conversation" : "Original task context"
                : "This Space",
            "progress" => task?.Plan.Count > 0 ? $"{row.Projection?.AcceptedActions.Count ?? 0} action(s) completed." : "",
            "observationText" => row.ExecutionObservation + " " + row.RecoveryObservation,
            "checkpoint" => task is null ? "" : $"Accepted {row.Projection?.AcceptedActions.Count ?? 0} of {task.Plan.Count} recorded action(s). " +
                (task.CheckpointId is { } checkpoint ? $"Acknowledged checkpoint {checkpoint:D}." : "No acknowledged checkpoint is recorded."),
            "identity" => task is null ? $"Conversation {row.ContextId:D}" : $"Task {task.TaskId:D}; run {task.ExecutionId:D}",
            _ => null
        };
        return path is "id" or "observation" or "title" or "openLabel" or "accessibleName" or "status" or "completed" or "summary" or "progress" or "observationText" or "checkpoint" or "identity" or "origin";
    }
    private static string Status(ITaskContextObservation row) => row.HasObservedLiveExecution ? $"Working · observed {row.Activity!.ObservedAt.LocalDateTime:g}" : row.Snapshot?.State switch
    {
        TaskExecutionLifecycle.Completed => "Completed · not verified",
        TaskExecutionLifecycle.Blocked => "Blocked · details available",
        TaskExecutionLifecycle.Suspended => "Suspended",
        TaskExecutionLifecycle.WaitingSafeBoundary => "Waiting for a safe boundary",
        TaskExecutionLifecycle.Failed => "Failed · details available",
        TaskExecutionLifecycle.Cancelled => "Cancelled",
        TaskExecutionLifecycle.Running => "Activity unconfirmed",
        _ => "Not started"
    };
    public bool TrySetItemValue(object item, string path, object? value) => false;
    public bool? IsActionAvailable(string command) => command switch
    {
        "tasks.open" => !_retired,
        "tasks.scope-only" => !_retired && _canChangeScope,
        "tasks.scope-all" => !_retired && _canChangeScope && _canReadAll,
        "tasks.next" => !_retired && !_busy && _canNext,
        "tasks.previous" => !_retired && !_busy && _canPrevious, "tasks.delegate" or "tasks.submit" => !_retired && !_busy && _canDelegate && !string.IsNullOrWhiteSpace(_instruction),
        "tasks.blank" => !_retired && !_busy && _canBlank, "tasks.refresh" => !_retired && !_busy, _ => false
    };
    public ValueTask DispatchAsync(string command, object? parameter, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (IsActionAvailable(command) != true) throw new InvalidOperationException("This Tasks action is unavailable.");
        _ownCallback(() =>
        {
            if (command == "tasks.open")
            {
                if (parameter is not ITaskContextObservation captured || !_rows.Any(row => ReferenceEquals(row, captured)))
                    throw new InvalidOperationException("This task tile changed; refresh before opening it.");
                RecentTaskRequested?.Invoke(this, captured);
            }
            else if (command is "tasks.scope-only" or "tasks.scope-all") ScopeRequested?.Invoke(this,
                command == "tasks.scope-all" ? TaskShownScope.AllTasks : TaskShownScope.TaskSpaceOnly);
            else if (command is "tasks.next" or "tasks.previous") PageRequested?.Invoke(this, command == "tasks.next");
            else if (command == "tasks.blank") NewBlankTaskRequested?.Invoke(this, EventArgs.Empty);
            else if (command == "tasks.refresh") RefreshRequested?.Invoke(this, EventArgs.Empty);
            else
            {
                if (command == "tasks.submit" && (parameter is not CuiInputSubmitRequest request || !ReferenceEquals(request.Sender, Instruction) || request.Modifiers != CuiInputSubmitModifiers.None)) return;
                DelegateRequested?.Invoke(this, _instruction.Trim());
            }
        });
        return ValueTask.CompletedTask;
    }
    internal Task CloseAndDrainAsync() => _close ??= CloseOriginalAsync();
    private async Task CloseOriginalAsync()
    {
        // Page-owned work has already joined. Retain both the accepted action cohort and
        // the loader's actual terminal cohort before releasing the authored root.
        if (_loader is { } loader)
        {
            await loader.WhenActionsIdleAsync().ConfigureAwait(false);
            await Dispatcher.UIThread.InvokeAsync(loader.Dispose).GetTask().ConfigureAwait(false);
            await loader.WhenActionsIdleAsync().ConfigureAwait(false);
        }
        await Dispatcher.UIThread.InvokeAsync(() => { Content = null; Root = null; }).GetTask().ConfigureAwait(false);
    }
}
