using Avalonia.Threading;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.Services;
using Haven.Desktop.Views.Pages.Tasks;
using HavenOS.Apps.Spaces;
using HavenOS.Apps.Spaces.Tasks;

namespace Haven.Desktop.Views.Pages.Spaces;

public sealed partial class SpacesHomePage
{
    private SpaceTaskWorkspaceService? _taskSource;
    private TaskExecutionCoordinator? _taskCoordinator;
    private UserPreferencesService? _taskPreferences;
    private Func<Action, CancellationToken, Task>? _publishTaskRows;
    private Func<bool>? _taskOwnerCurrent;
    private IDisposable? _taskPermissionSubscription;
    private bool _taskSubscribed;
    private long _taskReadSequence;
    private IReadOnlyList<AuthorizedTaskContextObservation> _taskRows = [];
    private readonly Dictionary<Guid, TaskOpenIntent> _taskOpenIntents = [];
    private TaskShownScope _taskScope;
    private TaskObservationPageCursor? _taskCursor, _taskNext;
    private readonly Stack<TaskObservationPageCursor?> _taskPrevious = new();
    private string _taskNotice = "The Task observation owner is unavailable in this host.";
    private TaskRunOriginalTaskPermissionReview? _taskReview;
    private TaskRunOriginalTaskPermissionResponse? _taskReviewReply;
    private bool _taskReviewOpen, _taskReviewBusy;
    private long _taskReviewSequence;
    private string _taskReviewTitle = "", _taskReviewStatus = "";
    private bool IsTasksSelection => _controller.State.Selection?.Id == SpaceRegistry.TasksSpaceId;
    internal IReadOnlyList<AuthorizedTaskContextObservation> OriginalSidebarTasks => _taskRows;
    internal TaskRunOriginalTaskPermissionResponse? AcknowledgedSidebarPermissionResponse { get; private set; }
    internal Task CaptureTaskSidebarActionsAsync() => _exactOriginalShow is { } shown
        ? _host.CaptureOriginalActionSettlement(shown) : Task.CompletedTask;

    /// <summary>Exact presentation identity. Only the SAME source-issued selection may
    /// open the original Task; a title, ConversationID or copied row is insufficient.</summary>
    public sealed class TaskOpenIntent
    {
        internal TaskOpenIntent(SpacesHomePage owner, SpacesHomeSelection selection,
            AuthorizedTaskContextObservation row, long generation, long sequence)
        { Owner = owner; Selection = selection; Row = row; Generation = generation; Sequence = sequence; }
        internal readonly SpacesHomePage Owner;
        internal readonly SpacesHomeSelection Selection;
        internal readonly AuthorizedTaskContextObservation Row;
        internal readonly long Generation, Sequence;
        public TaskContextSelection TaskSelection => Row.Selection;
    }

    internal void BindOriginalTaskSidebar(SpaceTaskWorkspaceService source, TaskExecutionCoordinator coordinator,
        UserPreferencesService preferences, Func<Action, CancellationToken, Task> publish,
        Func<bool> ownerCurrent)
    {
        Dispatcher.UIThread.VerifyAccess();
        lock (_gate)
        {
            _work.DemandAdmission();
            if (_active || _taskSource is not null) throw new InvalidOperationException("Bind the original Tasks sidebar once before activation.");
            _taskSource = source ?? throw new ArgumentNullException(nameof(source));
            _taskCoordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
            _taskPreferences = preferences ?? throw new ArgumentNullException(nameof(preferences));
            _publishTaskRows = publish ?? throw new ArgumentNullException(nameof(publish));
            _taskOwnerCurrent = ownerCurrent ?? throw new ArgumentNullException(nameof(ownerCurrent));
            _taskScope = preferences.ShownTasks;
        }
    }
    private void SubscribeTaskSidebar()
    {
        if (_taskSubscribed || _taskCoordinator is not { } coordinator) return;
        coordinator.SnapshotChanged += OnSidebarTaskChanged;
        coordinator.RunActivityInvalidated += OnSidebarTaskActivity;
        _taskPermissionSubscription = InvokeOriginalSource(() => coordinator.WatchOriginalTaskPermissionChanges(OnSidebarTaskPermission));
        _taskSubscribed = true;
    }
    private void RetireTaskSidebar()
    {
        if (_taskSubscribed && _taskCoordinator is { } coordinator)
        { coordinator.SnapshotChanged -= OnSidebarTaskChanged; coordinator.RunActivityInvalidated -= OnSidebarTaskActivity; }
        _taskSubscribed = false;
        Interlocked.Exchange(ref _taskPermissionSubscription, null)?.Dispose();
    }
    private void OnSidebarTaskChanged(object? sender, TaskExecutionSnapshot snapshot) => RefreshSidebarTaskHint();
    private void OnSidebarTaskActivity(object? sender, TaskRunActivityInvalidation hint) => RefreshSidebarTaskHint();
    private void OnSidebarTaskPermission(TaskPermissionReviewInvalidation hint) => RefreshSidebarTaskHint();
    private void RefreshSidebarTaskHint()
    {
        lock (_gate)
            if (!CanAdmitHomeMutation() || !IsTasksSelection || _taskOwnerCurrent?.Invoke() != true) return;
        _ = RefreshTaskSidebarAsync(); // The page retains this original; hints convey no state or read grant.
    }
    private void ClearSidebarTasks()
    { _taskReadSequence++; _taskRows = []; _taskOpenIntents.Clear(); _taskNext = null; }
    private void ResetSidebarTasks()
    {
        CloseSidebarTaskReview();
        ClearSidebarTasks(); _taskCursor = null; _taskPrevious.Clear();
        if (_taskPreferences is not null) _taskScope = _taskPreferences.ShownTasks;
    }
    internal void RefuseTaskSidebarAccess()
    {
        Dispatcher.UIThread.VerifyAccess();
        lock (_gate)
        {
            if (!CanAdmitHomeMutation()) return;
            ResetSidebarTasks(); _taskNotice = "Task access or context changed. Reload this Spaces tab.";
            _bindings.Refresh();
        }
    }
    internal bool IsCurrentTaskOpenIntent(TaskOpenIntent intent)
    {
        lock (_gate) return CanAdmitHomeMutation() && IsTasksSelection && ReferenceEquals(intent.Owner, this) &&
            ReferenceEquals(intent.Selection, _controller.State.Selection) && intent.Generation == _generation &&
            intent.Sequence == _taskReadSequence && _taskRows.Any(row => ReferenceEquals(row, intent.Row)) &&
            _taskOpenIntents.TryGetValue(intent.TaskSelection.TaskId, out var issued) && ReferenceEquals(issued, intent) &&
            _taskSource?.IsIssuedSelection(intent.TaskSelection) == true;
    }
    private bool TryGetTaskSidebarValue(string path, out object? value)
    {
        lock (_gate)
        {
            value = path switch
            {
                "spaces.isTasksSpace" => IsTasksSelection,
                "spaces.newConversationLabel" => IsTasksSelection ? "New task" : "New chat",
                "spaces.newConversationAccessibleLabel" => IsTasksSelection ? "Start a new explicit task in this Space" : "Start a new chat in this Space",
                "spaces.taskRows" => _taskRows,
                "spaces.hasTaskRows" => _taskRows.Count > 0,
                "spaces.taskScope" => _taskScope == TaskShownScope.AllTasks ? "All Tasks" : "Task Space Only",
                "spaces.taskSpaceOnly" => _taskScope == TaskShownScope.TaskSpaceOnly,
                "spaces.taskAll" => _taskScope == TaskShownScope.AllTasks,
                "spaces.canReadTasks" => IsTasksSelection && CanAdmitHomeMutation() && _taskSource?.HasAuthorizedTaskCensus == true,
                "spaces.taskNotice" => _taskNotice,
                "spaces.taskReviewOpen" => _taskReviewOpen,
                "spaces.taskReviewTitle" => _taskReviewTitle,
                "spaces.taskReviewQuestion" => _taskReview?.Request.Title ?? "Loading the actual permission request…",
                "spaces.taskReviewRequester" => _taskReview is null ? "" : "Requested by " + _taskReview.Request.RequestingComponentName,
                "spaces.taskReviewProvider" => _taskReview?.Request.ProviderName ?? "",
                "spaces.taskReviewExplanation" => _taskReview?.Request.Explanation ?? "",
                "spaces.taskReviewStatus" => _taskReviewStatus,
                "spaces.taskCanRespond" => _taskReviewOpen && !_taskReviewBusy && _taskReviewReply is null && _taskReview?.CanRespond == true && CanAdmitHomeMutation(),
                "spaces.taskCanNext" => _taskNext is not null && CanAdmitHomeMutation(),
                "spaces.taskCanPrevious" => _taskPrevious.Count > 0 && CanAdmitHomeMutation(),
                _ => null
            };
            return value is not null;
        }
    }
    private bool TryGetTaskSidebarItem(object item, string path, out object? value)
    {
        value = null;
        if (item is not AuthorizedTaskContextObservation row) return false;
        lock (_gate)
        {
            if (!CanAdmitHomeMutation() || !IsTasksSelection || !_taskRows.Any(current => ReferenceEquals(current, row)) ||
                _controller.State.Selection is not { } selection) return false;
            if (path == "openIntent")
            {
                if (!_taskOpenIntents.TryGetValue(row.Snapshot.TaskId, out var issued))
                    _taskOpenIntents[row.Snapshot.TaskId] = issued = new(this, selection, row, _generation, _taskReadSequence);
                value = issued; return true;
            }
            value = path switch
            {
                "id" => row.Snapshot.TaskId,
                "title" => row.Title,
                "openLabel" => "Open task " + row.Title,
                "isCurrent" => SpaceWorkspaceContent is SpaceTaskWidgetPage { OriginalTaskSelection: { } selectedTask } && selectedTask.TaskId == row.Snapshot.TaskId && selectedTask.ExecutionId == row.Snapshot.ExecutionId,
                "summary" => row.Snapshot.PromptSummary,
                "status" => SpaceTasksDashboardCuiScene.Status(row),
                "completed" => row.Snapshot.State == TaskExecutionLifecycle.Completed,
                "permission" => row.PermissionReview?.CanRespond == true,
                "origin" => row.OriginSpaceName is { Length: > 0 } name ? "From " + name : row.OriginalConversation is null ? "App task" : "Original conversation",
                "progress" => row.Snapshot.Plan.Count == 0 ? "" : $"{row.Projection.AcceptedActions.Count} action(s) completed.",
                "detail" => row.ExecutionObservation,
                _ => null
            };
            return value is not null;
        }
    }
    private bool IsTaskSidebarActionAvailable(string command) => IsTasksSelection && CanAdmitHomeMutation() &&
        _taskSource?.HasAuthorizedTaskCensus == true && _taskOwnerCurrent?.Invoke() == true && (command switch
        {
            "spaces.tasks.next" => _taskNext is not null,
            "spaces.tasks.previous" => _taskPrevious.Count > 0,
            "spaces.tasks.review" => true,
            "spaces.tasks.close-review" => _taskReviewOpen,
            "spaces.tasks.allow" or "spaces.tasks.deny" => _taskReviewOpen && !_taskReviewBusy && _taskReviewReply is null && _taskReview?.CanRespond == true,
            "spaces.tasks.open" or "spaces.tasks.refresh" or "spaces.tasks.scope-only" or "spaces.tasks.scope-all" => true,
            _ => false
        });

    public Task RefreshTaskSidebarAsync(CancellationToken cancellationToken = default)
    {
        long generation;
        lock (_gate)
        {
            if (!CanAdmitHomeMutation() || !IsTasksSelection || _taskSource is null || _taskOwnerCurrent?.Invoke() != true) return Task.CompletedTask;
            generation = _generation;
        }
        return RunHomeOriginal(async original =>
        {
            original.BindPublicationGuard(() => IsCurrent(generation));
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(original.Token, cancellationToken);
            await ReadSidebarTasksAsync(original, generation, linked.Token).ConfigureAwait(false);
        });
    }
    private async Task ReadSidebarTasksAsync(DesktopOriginalWorkLifetime.Original original, long generation, CancellationToken token)
    {
        long sequence; TaskShownScope scope; TaskObservationPageCursor? cursor; SpacesHomeSelection selection;
        lock (_gate)
        {
            if (!IsTasksSelection || _controller.State.Selection is not { } selected) return;
            if (_taskPreferences is { } preferences && preferences.ShownTasks != _taskScope) ResetSidebarTasks();
            selection = selected; sequence = ++_taskReadSequence; scope = _taskScope; cursor = _taskCursor;
        }
        bool Current() => IsCurrent(generation) && IsTasksSelection && ReferenceEquals(_controller.State.Selection, selection) &&
            sequence == _taskReadSequence && scope == _taskScope && ReferenceEquals(cursor, _taskCursor);
        try
        {
            var page = await original.AwaitAsync(InvokeOriginalSource(() => _taskSource!.ReadAllTasksPageAsync(40, cursor, token, OwnNestedOriginalSource))).ConfigureAwait(false);
            if (!original.IsAcceptedPublicationCurrent) return;
            await original.AwaitAsync(Dispatcher.UIThread.InvokeAsync<Task>(() => _publishTaskRows!(() =>
            {
                lock (_gate)
                {
                    if (!Current()) return;
                    _taskRows = scope == TaskShownScope.AllTasks ? page.Tasks : page.Tasks.Where(row => row.OriginSpaceId == SpaceRegistry.TasksSpaceId).ToArray();
                    _taskOpenIntents.Clear(); _taskNext = page.Next;
                    if (_taskReviewOpen && !_taskReviewBusy && _taskReview is { } reviewing)
                    {
                        _taskReview = page.Tasks.SingleOrDefault(row => row.Snapshot.TaskId == reviewing.CanonicalTaskContext.TaskId &&
                            row.Snapshot.ExecutionId == reviewing.CanonicalTaskContext.ExecutionId)?.PermissionReview;
                        if (_taskReview?.CanRespond != true) _taskReviewStatus = "This permission request has been answered or is no longer available.";
                    }
                    _taskNotice = "Showing authorized tasks on this page." + (page.HasUnavailableContexts ? " Some tasks could not be shown." : "");
                    _bindings.Refresh();
                }
            }, token)).GetTask().Unwrap()).ConfigureAwait(false);
        }
        catch (Exception error)
        {
            await original.AwaitAsync(Dispatcher.UIThread.InvokeAsync(() => original.RunAcceptedPublicationCallback(() =>
            {
                lock (_gate)
                {
                    if (!Current()) return;
                    _taskRows = []; _taskOpenIntents.Clear(); _taskNext = null; CloseSidebarTaskReview();
                    _taskNotice = error is UnauthorizedAccessException or SpacesHistoryAuthorityRefusal ? "Task access changed. Reload this Spaces tab." : "Tasks couldn’t refresh. Try again.";
                    _bindings.Refresh();
                }
            })).GetTask()).ConfigureAwait(false);
            if (error is not (UnauthorizedAccessException or SpacesHistoryAuthorityRefusal)) throw;
        }
    }
    private async Task DispatchTaskSidebarAsync(DesktopOriginalWorkLifetime.Original original, long generation,
        string command, object? parameter, CancellationToken token)
    {
        if (command is "spaces.tasks.review" or "spaces.tasks.allow" or "spaces.tasks.deny" or "spaces.tasks.close-review")
        { await DispatchSidebarTaskReviewAsync(original, command, parameter, token).ConfigureAwait(false); return; }
        if (command == "spaces.tasks.open")
        {
            if (parameter is not TaskOpenIntent intent || !IsCurrentTaskOpenIntent(intent))
                throw new InvalidOperationException("This task tile changed. Refresh before opening it.");
            var host = _workspaceCommandHost ?? throw new InvalidOperationException("The actual Task opener is unavailable.");
            await original.AwaitAsync(InvokeOriginalSource(() => host(command, intent, token, null))).ConfigureAwait(false);
            return;
        }
        if (command is "spaces.tasks.scope-only" or "spaces.tasks.scope-all")
        {
            await original.AwaitAsync(Dispatcher.UIThread.InvokeAsync<Task>(() => _publishTaskRows!(() =>
            {
                lock (_gate)
                {
                    var chosen = command == "spaces.tasks.scope-all" ? TaskShownScope.AllTasks : TaskShownScope.TaskSpaceOnly;
                    InvokeOriginalSource(() => { _taskPreferences!.SetShownTasks(chosen); return true; });
                    _taskScope = chosen; _taskCursor = null; _taskPrevious.Clear(); CloseSidebarTaskReview(); ClearSidebarTasks();
                }
            }, token)).GetTask().Unwrap()).ConfigureAwait(false);
        }
        else lock (_gate)
        {
            if (command == "spaces.tasks.next") { _taskPrevious.Push(_taskCursor); _taskCursor = _taskNext; }
            else if (command == "spaces.tasks.previous") _taskCursor = _taskPrevious.Pop();
            else { _taskCursor = null; _taskPrevious.Clear(); }
            ClearSidebarTasks();
        }
        await ReadSidebarTasksAsync(original, generation, token).ConfigureAwait(false);
    }
    private void CloseSidebarTaskReview()
    { _taskReviewSequence++; _taskReviewOpen = false; _taskReview = null; _taskReviewReply = null; _taskReviewBusy = false; }
    private async Task DispatchSidebarTaskReviewAsync(DesktopOriginalWorkLifetime.Original original,
        string command, object? parameter, CancellationToken token)
    {
        if (command == "spaces.tasks.close-review")
        {
            await original.AwaitAsync(Dispatcher.UIThread.InvokeAsync(() => { lock (_gate) { CloseSidebarTaskReview(); _bindings.Refresh(); } }).GetTask()).ConfigureAwait(false);
            return;
        }
        if (command == "spaces.tasks.review")
        {
            if (parameter is not TaskOpenIntent intent || !IsCurrentTaskOpenIntent(intent))
                throw new InvalidOperationException("This task tile changed before permission review.");
            var current = await original.AwaitAsync(InvokeOriginalSource(() => _taskSource!.ReadSelectionAsync(intent.TaskSelection, token, OwnNestedOriginalSource))).ConfigureAwait(false);
            var review = current.PermissionReview ?? throw new InvalidOperationException("The actual original permission request is no longer available.");
            await original.AwaitAsync(Dispatcher.UIThread.InvokeAsync<Task>(() => _publishTaskRows!(() =>
            {
                lock (_gate)
                {
                    if (!IsCurrentTaskOpenIntent(intent)) return;
                    _taskReviewSequence++; _taskReview = review; _taskReviewReply = null; _taskReviewOpen = true; _taskReviewBusy = false;
                    _taskReviewTitle = current.Title; _taskReviewStatus = review.CanRespond ? "Review the scope and consequences before choosing." : "This request has already been answered.";
                    _bindings.Refresh();
                }
            }, token)).GetTask().Unwrap()).ConfigureAwait(false);
            return;
        }
        TaskRunOriginalTaskPermissionReview captured; long sequence;
        lock (_gate)
        {
            captured = _taskReview ?? throw new InvalidOperationException("The exact original permission review is unavailable.");
            sequence = _taskReviewSequence; _taskReviewBusy = true;
        }
        Task<TaskRunOriginalTaskPermissionResponse>? response = null;
        try
        {
            await original.AwaitAsync(Dispatcher.UIThread.InvokeAsync<Task>(() => _publishTaskRows!(() =>
                response = InvokeOriginalSource(() => _taskCoordinator!.RespondOriginalTaskPermissionAsync(captured, command == "spaces.tasks.allow", token)), token)).GetTask().Unwrap()).ConfigureAwait(false);
            if (response is null) return; // Actual Home refusal admitted no permission decision.
            var reply = await original.AwaitAsync(response).ConfigureAwait(false);
            AcknowledgedSidebarPermissionResponse = reply; // Keep the real owner ACK before optional view publication.
            lock (_gate) if (_taskReviewSequence == sequence) _taskReviewReply = reply;
            await original.AwaitAsync(Dispatcher.UIThread.InvokeAsync(() => original.RunAcceptedPublicationCallback(() =>
            {
                lock (_gate)
                {
                    if (_taskReviewSequence != sequence) return;
                    _taskReviewBusy = false; _taskReviewStatus = command == "spaces.tasks.allow" ? "Allowed for this request. The task was not resumed." : "Denied for this request. The task was not resumed.";
                    _bindings.Refresh();
                }
            })).GetTask()).ConfigureAwait(false);
        }
        finally
        {
            lock (_gate) if (_taskReviewSequence == sequence) _taskReviewBusy = false;
        }
        if (original.IsAcceptedPublicationCurrent)
            await ReadSidebarTasksAsync(original, _generation, token).ConfigureAwait(false);
    }

}
