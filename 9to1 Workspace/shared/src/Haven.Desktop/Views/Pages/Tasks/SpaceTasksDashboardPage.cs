using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Threading;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.Services;
using Haven.Desktop.ViewModels;
using HavenOS.Apps.Spaces.Tasks;

namespace Haven.Desktop.Views.Pages.Tasks;

/// <summary>
/// The authored CUI Tasks scene projected from actual conversations in one existing Space.
/// The coordinator/repositories and genuine new-work/open factories are borrowed. This
/// page never creates a context, Task, Run, route, permission or provider. Closing withdraws
/// its own admission and joins actual reads/open callbacks; it does not cancel business work.
/// Accepted CUI action and loader-terminal cohorts are joined before detaching the actual
/// root. These joins do not claim native frame, input or independent acceptance.
/// Constructor-before-return acquisition remains the shell owner's responsibility.
/// </summary>
public sealed class SpaceTasksDashboardPage : UserControl, IActivatablePage, IDisposable,
    IAsyncDisposable, IDesktopOriginalRetirementParticipant, IDesktopOriginalRetirementJoinGuard
{
    private readonly SpaceTaskWorkspaceService _source;
    private readonly TaskExecutionCoordinator _canonical;
    private readonly Guid _spaceId;
    private readonly Func<SpaceTaskObservation, Task> _openExisting;
    private readonly Func<string, Task>? _startNew;
    private readonly Func<Task>? _openBlank;
    private readonly UserPreferencesService? _preferences;
    private readonly Func<AuthorizedTaskContextObservation, Task>? _openAuthorized;
    private TaskShownScope _shownScope;
    private TaskObservationPageCursor? _pageCursor, _nextCursor;
    private readonly List<TaskObservationPageCursor?> _previousCursors = [];
    private IReadOnlyList<ITaskContextObservation> _currentObservations = [];
    private bool _navigationPending;
    private ITaskContextObservation? _permissionSelection;
    private TaskRunOriginalTaskPermissionReview? _permissionReview;
    private bool _permissionOpen, _permissionWorkPending, _permissionResponded;
    private long _permissionSequence;
    private string _permissionTitle = "", _permissionStatus = "";
    private IDisposable? _permissionChanges;
    internal Task? ActualPermissionInvalidation { get; private set; }
    internal Task? ActualPermissionReview { get; private set; }
    internal Task? ActualPermissionResponse { get; private set; }
    public TaskRunOriginalTaskPermissionResponse? AcknowledgedPermissionResponse { get; private set; }
    internal Task? ActualScopeChange { get; private set; }
    internal Task? ActualPageNavigation { get; private set; }
    private readonly DesktopOriginalWorkLifetime _work;
    private readonly SpaceTasksDashboardCuiScene _scene;
    private readonly object _gate = new();
    [ThreadStatic] private static List<SpaceTasksDashboardPage>? _physicalSources;
    private IReadOnlyList<SpaceTaskObservation> _observations = [];
    private bool _active, _subscribed, _newWorkPending;
    private long _generation, _readSequence;
    internal Task? ActualNewWork { get; private set; }

    public SpaceTasksDashboardPage(SpaceTaskWorkspaceService source, TaskExecutionCoordinator canonical,
        Guid actualSpaceId, Func<SpaceTaskObservation, Task> openExistingTask,
        Func<string, Task>? startExplicitNewTask = null, Func<Task>? openBlankTask = null)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _canonical = canonical ?? throw new ArgumentNullException(nameof(canonical));
        if (actualSpaceId == Guid.Empty) throw new ArgumentException("An existing Space ID is required.", nameof(actualSpaceId));
        _spaceId = actualSpaceId;
        _openExisting = openExistingTask ?? throw new ArgumentNullException(nameof(openExistingTask));
        _startNew = startExplicitNewTask; _openBlank = openBlankTask;
        _work = new(StopOriginalPresentationAsync, DetachOriginalPresentationAsync);
        _scene = new(actualSpaceId, OwnSceneCallback, DemandOriginalSceneWrite);
        Scene = _scene;
        AutomationProperties.SetAutomationId(this, "SpaceCanonicalTaskDashboard");
        AutomationProperties.SetName(this, "Existing Space tasks");
        AutomationProperties.SetAutomationId(Scene, "SpaceCanonicalTaskDashboardScene");
        AutomationProperties.SetName(Scene, "Tasks, recorded progress and recovery");
        Content = Scene;
        _scene.DelegateRequested += OnDelegateRequested;
        _scene.NewBlankTaskRequested += OnBlankRequested;
        _scene.RecentTaskRequested += OnOpenRequested;
        _scene.RefreshRequested += OnRefreshRequested;
        _scene.ScopeRequested += OnScopeRequested;
        _scene.PageRequested += OnPageRequested;
        _scene.PermissionReviewRequested += OnPermissionReviewRequested;
        _scene.PermissionResponseRequested += OnPermissionResponseRequested;
        _scene.PermissionReviewCloseRequested += OnPermissionReviewCloseRequested;
    }

    public SpaceTasksDashboardPage(SpaceTaskWorkspaceService source, TaskExecutionCoordinator canonical,
        Guid actualSpaceId, Func<SpaceTaskObservation, Task> openExistingTask,
        Func<string, Task>? startExplicitNewTask, Func<Task>? openBlankTask,
        UserPreferencesService actualPreferences, Func<AuthorizedTaskContextObservation, Task> openAuthorizedTask)
        : this(source, canonical, actualSpaceId, openExistingTask, startExplicitNewTask, openBlankTask)
    {
        _preferences = actualPreferences ?? throw new ArgumentNullException(nameof(actualPreferences));
        _openAuthorized = openAuthorizedTask ?? throw new ArgumentNullException(nameof(openAuthorizedTask));
        _shownScope = actualSpaceId == SpaceRegistry.TasksSpaceId ? actualPreferences.ShownTasks : TaskShownScope.TaskSpaceOnly;
    }

    public IReadOnlyList<ITaskContextObservation> CurrentObservations => _currentObservations;
    public TaskShownScope ShownScope => _shownScope;
    private bool CanChangeScope => _spaceId == SpaceRegistry.TasksSpaceId && _preferences is not null && _openAuthorized is not null;
    private void PublishScope(Action? guard = null) => _scene.SetScope(_shownScope, CanChangeScope,
        _source.HasAuthorizedTaskCensus, !_navigationPending && _nextCursor is not null,
        !_navigationPending && _previousCursors.Count != 0, guard);

    public SpaceTasksDashboardCuiScene Scene { get; }
    internal SpaceTasksDashboardCuiScene OriginalScene => _scene;
    public IReadOnlyList<SpaceTaskObservation> OriginalObservations => _observations;
    private bool IsCurrent(long generation)
    { lock (_gate) return _active && !_work.IsRetiring && _generation == generation; }
    private void DemandOriginalSceneWrite()
    {
        var original = _work.Executing ?? throw new InvalidOperationException("No actual dashboard original owns this scene write.");
        original.DemandPublication();
        if (!IsCurrent(_generation)) throw new InvalidOperationException("The original Space task presentation was withdrawn.");
    }
    private void OwnSceneCallback(Action callback)
    {
        long generation; lock (_gate) generation = _generation;
        InvokePhysicalSource(() =>
        {
            _work.RunSynchronous(original =>
            {
                original.BindPublicationGuard(() => IsCurrent(generation)); original.DemandPublication(); callback();
            });
            return true;
        });
    }

    public Task ActivateAsync(CancellationToken cancellationToken)
    {
        long generation;
        lock (_gate) { _work.DemandAdmission(); _active = true; generation = ++_generation; }
        return _work.RunAsync(async original =>
        {
            original.BindPublicationGuard(() => IsCurrent(generation));
            using var scope = CancellationTokenSource.CreateLinkedTokenSource(original.Token, cancellationToken);
            await DispatchOriginalAsync(original, _scene.Initialize).ConfigureAwait(false);
            original.DemandPublication();
            lock (_gate)
            {
                original.DemandPublication();
                if (!_subscribed)
                { _canonical.SnapshotChanged += OnSnapshotChanged; _canonical.RunActivityInvalidated += OnRunActivityInvalidated; _subscribed = true;
                    _permissionChanges = InvokePhysicalSource(() => _canonical.WatchOriginalTaskPermissionChanges(OnPermissionInvalidated)); }
            }
            await ReadOriginalPageAsync(original, generation, scope.Token).ConfigureAwait(false);
        });
    }
    public void Deactivate() { lock (_gate) { _active = false; _generation++; } }
    internal Task RefreshOriginalAsync(CancellationToken cancellationToken = default)
    {
        long generation; lock (_gate) generation = _generation;
        return _work.RunAsync(async original =>
        {
            original.BindPublicationGuard(() => IsCurrent(generation));
            using var scope = CancellationTokenSource.CreateLinkedTokenSource(original.Token, cancellationToken);
            await ReadOriginalPageAsync(original, generation, scope.Token).ConfigureAwait(false);
        });
    }
    private async Task ReadOriginalPageAsync(DesktopOriginalWorkLifetime.Original original, long generation, CancellationToken token)
    {
        long sequence; TaskShownScope shown; TaskObservationPageCursor? cursor;
        lock (_gate) { sequence = ++_readSequence; shown = _shownScope; cursor = _pageCursor; }
        try
        {
            original.DemandPublication();
            IReadOnlyList<ITaskContextObservation> rows;
            TaskObservationPageCursor? next = null;
            bool unavailable = false;
            if (shown == TaskShownScope.AllTasks)
            {
                if (!CanChangeScope) throw new InvalidOperationException("The actual All Tasks opening owner is unavailable.");
                var page = await original.AwaitAsync(InvokePhysicalSource(() => _source.ReadAllTasksPageAsync(40, cursor, token, OwnNestedOriginalSource))).ConfigureAwait(false);
                rows = page.Tasks; next = page.Next; unavailable = page.HasUnavailableContexts;
            }
            else rows = await original.AwaitAsync(InvokePhysicalSource(() => _source.ReadPageAsync(_spaceId, 40, token, OwnNestedOriginalSource))).ConfigureAwait(false);
            await DispatchOriginalAsync(original, () =>
            {
                original.DemandPublication();
                lock (_gate) { if (_readSequence != sequence || shown != _shownScope) return; }
                _currentObservations = rows;
                _observations = rows.OfType<SpaceTaskObservation>().ToArray(); // Preserve the legacy Space-only API.
                _nextCursor = next;
                void DemandRead()
                {
                    original.DemandPublication();
                    lock (_gate) if (_readSequence != sequence || shown != _shownScope) throw new InvalidOperationException("The original dashboard read was superseded.");
                }
                _scene.SetRecent(rows, DemandRead); PublishScope(DemandRead);
                _scene.SetStartAvailability(!_newWorkPending && _startNew is not null, !_newWorkPending && _openBlank is not null, DemandRead);
                _scene.SetStatus(shown == TaskShownScope.AllTasks
                    ? "Showing authorized tasks on this page." + (unavailable ? " Some tasks could not be shown." : "")
                    : _startNew is null || _openBlank is null
                        ? "Some new-task actions are unavailable. Existing tasks can still be opened."
                        : "Showing up to 40 task conversations in this Space.", DemandRead);
            }).ConfigureAwait(false);
        }
        catch (Exception error)
        {
            original.Retain(error);
            if (IsCurrent(generation))
            {
                try { await DispatchOriginalAsync(original, () =>
                    {
                        lock (_gate) { if (_readSequence != sequence || shown != _shownScope) return; }
                        _currentObservations = []; _observations = []; _nextCursor = null;
                        ClosePermissionReview();
                        _scene.SetRecent([]); PublishScope();
                        _scene.SetStatus(error is UnauthorizedAccessException ? "The current profile cannot show this task context. Refresh after checking your profile."
                            : "Tasks couldn’t refresh. Try again.");
                    }).ConfigureAwait(false); }
                catch (Exception publicationError) { original.Retain(publicationError); }
            }
            original.ThrowRetained();
        }
    }
    private void OnScopeRequested(object? sender, TaskShownScope shown)
    {
        if (!CanChangeScope || !Enum.IsDefined(shown) || shown == TaskShownScope.AllTasks && !_source.HasAuthorizedTaskCensus)
            throw new InvalidOperationException("This actual task display scope is unavailable.");
        long generation; lock (_gate) generation = _generation;
        ActualScopeChange = _work.RunAsync(async original =>
        {
            original.BindPublicationGuard(() => IsCurrent(generation)); original.DemandPublication();
            // The SAME profile preference owner saves a display choice only.
            InvokePhysicalSource(() => { _preferences!.SetShownTasks(shown); return true; });
            await DispatchOriginalAsync(original, () =>
            {
                original.DemandPublication();
                lock (_gate) { _shownScope = shown; _pageCursor = null; _nextCursor = null; _previousCursors.Clear(); _readSequence++; }
                _currentObservations = []; _observations = []; ClosePermissionReview(); _scene.SetRecent([]); PublishScope();
            }).ConfigureAwait(false);
            await ReadOriginalPageAsync(original, generation, original.Token).ConfigureAwait(false);
        });
    }
    private void OnPageRequested(object? sender, bool next)
    {
        long generation; lock (_gate) generation = _generation;
        ActualPageNavigation = _work.RunAsync(async original =>
        {
            original.BindPublicationGuard(() => IsCurrent(generation)); original.DemandPublication();
            lock (_gate)
            {
                if (_navigationPending || _shownScope != TaskShownScope.AllTasks || next && _nextCursor is null || !next && _previousCursors.Count == 0)
                    throw new InvalidOperationException("This actual task page is unavailable.");
                _navigationPending = true;
                if (next) { _previousCursors.Add(_pageCursor); _pageCursor = _nextCursor; }
                else { _pageCursor = _previousCursors[^1]; _previousCursors.RemoveAt(_previousCursors.Count - 1); }
            }
            try
            {
                await DispatchOriginalAsync(original, () => { ClosePermissionReview(); PublishScope(); }).ConfigureAwait(false);
                await ReadOriginalPageAsync(original, generation, original.Token).ConfigureAwait(false);
            }
            finally
            {
                lock (_gate) _navigationPending = false;
                if (IsCurrent(generation)) await DispatchOriginalAsync(original, () => PublishScope()).ConfigureAwait(false);
            }
        });
    }
    private void OnSnapshotChanged(object? sender, TaskExecutionSnapshot snapshot)
    {
        long generation;
        lock (_gate) { if (!_active || _work.IsRetiring) return; generation = _generation; }
        // A canonical ACK is an observation. Fresh Space membership comes from the real read.
        _ = _work.RunAsync(async original =>
        {
            original.BindPublicationGuard(() => IsCurrent(generation));
            await ReadOriginalPageAsync(original, generation, original.Token).ConfigureAwait(false);
        });
    }
    private void OnRunActivityInvalidated(object? sender, TaskRunActivityInvalidation hint)
    {
        lock (_gate) if (!_active || _work.IsRetiring || !_currentObservations.Any(row =>
            row.Snapshot is { } task && task.TaskId == hint.TaskId && task.ExecutionId == hint.ExecutionId && row.ContextId == hint.ContextId)) return;
        _ = RefreshOriginalAsync(); // Hint is not state; the same source freshly authorizes and reads.
    }
    private void OnPermissionInvalidated(TaskPermissionReviewInvalidation hint)
    {
        long generation;
        lock (_gate)
        {
            if (!_active || _work.IsRetiring || !_currentObservations.Any(row =>
                row.Snapshot is { } task && task.TaskId == hint.TaskId && task.ExecutionId == hint.ExecutionId)) return;
            generation = _generation;
        }
        ActualPermissionInvalidation = _work.RunAsync(async original =>
        {
            original.BindPublicationGuard(() => IsCurrent(generation));
            await ReadOriginalPageAsync(original, generation, original.Token).ConfigureAwait(false);
            await DispatchOriginalAsync(original, () =>
            {
                if (!_permissionOpen || _permissionReview?.Request.Id != hint.RequestId ||
                    _permissionSelection?.Snapshot is not { } selectedTask || selectedTask.TaskId != hint.TaskId || selectedTask.ExecutionId != hint.ExecutionId) return;
                // Published is NOT durable or permission acceptance. Only the newly
                // read current original owner can reconcile an already open pop-out.
                var row = _currentObservations.SingleOrDefault(row => row.Snapshot is { } task && task.TaskId == hint.TaskId && task.ExecutionId == hint.ExecutionId);
                if (row?.PermissionReview is not { } reviewed) { ClosePermissionReview(); return; }
                _permissionSelection = row; _permissionReview = reviewed;
                _permissionResponded = !reviewed.CanRespond;
                _permissionStatus = reviewed.AcknowledgedDecision?.Kind switch
                {
                    PermissionDecisionKind.Allowed => "Permission allowed. Resume the task separately when ready.",
                    PermissionDecisionKind.Denied => "Permission denied. The task remains suspended.",
                    _ => reviewed.CanRespond ? "Review the scope and consequences before choosing." : "This request cannot accept another response."
                };
                PublishPermissionReview();
            }).ConfigureAwait(false);
        });
    }
    private void OnRefreshRequested(object? sender, EventArgs args)
    {
        // An explicit refresh starts a new census and discards any previous actor's
        // opaque paging positions. Snapshot-driven refreshes keep the current page.
        lock (_gate) { _pageCursor = null; _nextCursor = null; _previousCursors.Clear(); _readSequence++; }
        PublishScope();
        _ = RefreshOriginalAsync();
    }
    private void OnOpenRequested(object? sender, ITaskContextObservation captured)
    {
        if (!_currentObservations.Any(row => ReferenceEquals(row, captured))) throw new InvalidOperationException("This task tile changed before opening.");
        var contextId = captured.ContextId;
        long generation; lock (_gate) generation = _generation;
        _ = _work.RunAsync(async original =>
        {
            original.BindPublicationGuard(() => IsCurrent(generation)); original.DemandPublication();
            try
            {
                if (captured is AuthorizedTaskContextObservation selected)
                {
                    var current = await original.AwaitAsync(InvokePhysicalSource(() => _source.ReadSelectionAsync(selected.Selection, original.Token, OwnNestedOriginalSource))).ConfigureAwait(false);
                    original.DemandPublication();
                    var open = _openAuthorized ?? throw new InvalidOperationException("The genuine same-context task opener is unavailable.");
                    await original.AwaitAsync(InvokePhysicalSource(() => open(current))).ConfigureAwait(false);
                }
                else
                {
                    var current = await original.AwaitAsync(InvokePhysicalSource(() => _source.ReadAsync(_spaceId, contextId, original.Token, OwnNestedOriginalSource))).ConfigureAwait(false);
                    if (current.Snapshot?.TaskId != captured.Snapshot?.TaskId || current.Snapshot?.ExecutionId != captured.Snapshot?.ExecutionId)
                        throw new InvalidOperationException("The recorded Task/Run changed; refresh this dashboard before opening it.");
                    original.DemandPublication();
                    await original.AwaitAsync(InvokePhysicalSource(() => _openExisting(current))).ConfigureAwait(false);
                }
            }
            catch (Exception error)
            {
                original.Retain(error);
                if (IsCurrent(generation))
                {
                    try { await DispatchOriginalAsync(original, () => _scene.SetStatus("Couldn’t open this task. Refresh and try again.")).ConfigureAwait(false); }
                    catch (Exception publicationError) { original.Retain(publicationError); }
                }
                original.ThrowRetained();
            }
        });
    }
    private void PublishPermissionReview() => _scene.SetPermissionReview(_permissionReview, _permissionTitle,
        _permissionOpen, _permissionWorkPending, _permissionStatus, _permissionResponded);
    private void ClosePermissionReview()
    {
        lock (_gate) _permissionSequence++;
        _permissionOpen = false; _permissionReview = null; _permissionSelection = null;
        _permissionTitle = ""; _permissionStatus = ""; _permissionResponded = false;
        PublishPermissionReview();
    }
    private void OnPermissionReviewCloseRequested(object? sender, EventArgs args) => ClosePermissionReview();
    private void OnPermissionReviewRequested(object? sender, ITaskContextObservation selected)
    {
        if (!_currentObservations.Any(row => ReferenceEquals(row, selected)) || selected.PermissionReview is not { CanRespond: true })
            throw new InvalidOperationException("This task permission tile changed before review.");
        long generation, sequence;
        lock (_gate)
        {
            if (_permissionWorkPending) throw new InvalidOperationException("An original permission review or response is pending.");
            _permissionWorkPending = true; generation = _generation; sequence = ++_permissionSequence;
        }
        try { ActualPermissionReview = _work.RunAsync(async original =>
        {
            original.BindPublicationGuard(() => IsCurrent(generation));
            try
            {
                await DispatchOriginalAsync(original, () =>
                {
                    if (_permissionSequence != sequence) return;
                    _permissionSelection = selected; _permissionReview = null; _permissionResponded = false;
                    _permissionOpen = true; _permissionTitle = selected.Title; _permissionStatus = "Loading the actual permission request…";
                    PublishPermissionReview();
                }).ConfigureAwait(false);
                var current = await original.AwaitAsync(InvokePhysicalSource(() =>
                    _source.ReadPermissionReviewAsync(selected, original.Token, OwnNestedOriginalSource))).ConfigureAwait(false);
                await DispatchOriginalAsync(original, () =>
                {
                    if (_permissionSequence != sequence) return;
                    _permissionReview = current;
                    _permissionStatus = current.CanRespond ? "Review the scope and consequences before choosing."
                        : "This request cannot accept another response.";
                    PublishPermissionReview();
                }).ConfigureAwait(false);
            }
            catch (Exception error)
            {
                original.Retain(error);
                if (IsCurrent(generation))
                    try { await DispatchOriginalAsync(original, () =>
                    { if (_permissionSequence == sequence) { _permissionStatus = "The permission request couldn’t be reviewed. Close and try again."; _permissionReview = null; PublishPermissionReview(); } }).ConfigureAwait(false); }
                    catch (Exception projectionError) { original.Retain(projectionError); }
            }
            finally
            {
                lock (_gate) _permissionWorkPending = false;
                if (IsCurrent(generation))
                    try { await DispatchOriginalAsync(original, PublishPermissionReview).ConfigureAwait(false); }
                    catch (Exception projectionError) { original.Retain(projectionError); }
            }
            original.ThrowRetained();
        }); }
        catch { lock (_gate) _permissionWorkPending = false; throw; }
    }
    private void OnPermissionResponseRequested(object? sender, bool approve)
    {
        var selected = _permissionSelection;
        var reviewed = _permissionReview;
        long generation, sequence;
        lock (_gate)
        {
            if (_permissionWorkPending || !_permissionOpen || _permissionResponded || selected is null || reviewed?.CanRespond != true)
                throw new InvalidOperationException("No current reviewed permission response is available.");
            _permissionWorkPending = true; generation = _generation; sequence = _permissionSequence;
        }
        try { ActualPermissionResponse = _work.RunAsync(async original =>
        {
            original.BindPublicationGuard(() => IsCurrent(generation));
            try
            {
                await DispatchOriginalAsync(original, PublishPermissionReview).ConfigureAwait(false);
                var reply = await original.AwaitAsync(InvokePhysicalSource(() => _source.RespondPermissionReviewAsync(
                    selected!, reviewed!, approve, original.Token, OwnNestedOriginalSource))).ConfigureAwait(false);
                AcknowledgedPermissionResponse = reply; // The genuine source ACK survives optional UI withdrawal.
                await DispatchOriginalAsync(original, () =>
                {
                    if (_permissionSequence != sequence) return;
                    _permissionResponded = true;
                    _permissionStatus = reply.Decision.Kind == PermissionDecisionKind.Allowed
                        ? "Permission allowed. Resume the task separately when ready."
                        : "Permission denied. The task remains suspended.";
                    PublishPermissionReview();
                }).ConfigureAwait(false);
                await ReadOriginalPageAsync(original, generation, original.Token).ConfigureAwait(false);
            }
            catch (Exception error)
            {
                original.Retain(error);
                if (IsCurrent(generation))
                    try { await DispatchOriginalAsync(original, () =>
                    { if (_permissionSequence == sequence) { _permissionResponded = true; _permissionStatus = "The response couldn’t be confirmed. Close and review the current request."; PublishPermissionReview(); } }).ConfigureAwait(false); }
                    catch (Exception projectionError) { original.Retain(projectionError); }
            }
            finally
            {
                lock (_gate) _permissionWorkPending = false;
                if (IsCurrent(generation))
                    try { await DispatchOriginalAsync(original, PublishPermissionReview).ConfigureAwait(false); }
                    catch (Exception projectionError) { original.Retain(projectionError); }
            }
            original.ThrowRetained();
        }); }
        catch { lock (_gate) _permissionWorkPending = false; throw; }
    }

    private void OnDelegateRequested(object? sender, string instruction)
    {
        if (_startNew is null) throw new InvalidOperationException("The genuine explicit new-task owner is unconfigured.");
        StartOriginalNewWork(() => _startNew(instruction), true);
    }
    private void OnBlankRequested(object? sender, EventArgs e)
    {
        if (_openBlank is null) throw new InvalidOperationException("The genuine blank-task opener is unconfigured.");
        StartOriginalNewWork(_openBlank, false);
    }
    private void StartOriginalNewWork(Func<Task> source, bool clearInstruction)
    {
        var instructionVersion = _scene.InstructionVersion;
        long generation; lock (_gate) generation = _generation;
        _ = _work.RunAsync(async original =>
        {
            original.BindPublicationGuard(() => IsCurrent(generation)); original.DemandPublication();
            ActualNewWork = original.Task; // Same prepublished owner driver, never an ACK proxy.
            lock (_gate)
            {
                if (_newWorkPending) throw new InvalidOperationException("An original new-work opener is already pending.");
                _newWorkPending = true;
            }
            try
            {
                await DispatchOriginalAsync(original, () => _scene.SetBusy(true)).ConfigureAwait(false);
                original.DemandPublication();
                // The original callback is caller/business owned, with its original token.
                // This page does not substitute a view token or claim provider completion.
                await original.AwaitAsync(InvokePhysicalSource(source)).ConfigureAwait(false);
                await DispatchOriginalAsync(original, () =>
                {
                    original.DemandPublication();
                    if (clearInstruction) _scene.ClearInstruction(instructionVersion);
                    _scene.SetStatus("Request handed to the task workspace.");
                }).ConfigureAwait(false);
                await ReadOriginalPageAsync(original, generation, original.Token).ConfigureAwait(false);
            }
            catch (Exception error)
            {
                original.Retain(error);
                if (IsCurrent(generation))
                {
                    try { await DispatchOriginalAsync(original, () => _scene.SetStatus("Couldn’t open a task workspace.")).ConfigureAwait(false); }
                    catch (Exception publicationError) { original.Retain(publicationError); }
                }
            }
            finally
            {
                lock (_gate) _newWorkPending = false;
                if (IsCurrent(generation))
                {
                    try
                    {
                        await DispatchOriginalAsync(original, () =>
                        {
                            original.DemandPublication(); _scene.SetBusy(false);
                            _scene.SetStartAvailability(_startNew is not null, _openBlank is not null);
                        }).ConfigureAwait(false);
                    }
                    catch (Exception error) { original.Retain(error); }
                }
            }
            original.ThrowRetained();
        });
    }
    private Task DispatchOriginalAsync(DesktopOriginalWorkLifetime.Original original, Action callback)
    {
        var actual = InvokePhysicalSource(() => Dispatcher.UIThread.InvokeAsync(() =>
            InvokePhysicalSource(() => { callback(); return true; })).GetTask());
        return original.AwaitAsync(actual);
    }
    private void OwnNestedOriginalSource(Action callback) =>
        InvokePhysicalSource(() => { callback(); return true; });
    private T InvokePhysicalSource<T>(Func<T> source)
    {
        var stack = _physicalSources ??= []; stack.Add(this);
        try { return source(); } finally { stack.RemoveAt(stack.Count - 1); }
    }
    public void DemandExternalOriginalRetirementJoin()
    {
        if (_physicalSources?.Any(owner => ReferenceEquals(owner, this)) == true)
            throw new InvalidOperationException("The actual dashboard source callback cannot join its encompassing close.");
        _work.DemandExternalClose();
    }
    public void RequestRetirement() => _work.RequestRetirement();
    public Task CloseAndDrainAsync() { DemandExternalOriginalRetirementJoin(); return _work.CloseAndDrainAsync(); }
    private Task StopOriginalPresentationAsync()
    {
        lock (_gate) { _active = false; _generation++; }
        if (_subscribed) { _canonical.SnapshotChanged -= OnSnapshotChanged; _canonical.RunActivityInvalidated -= OnRunActivityInvalidated; _subscribed = false; }
        var permissionChanges = Interlocked.Exchange(ref _permissionChanges, null);
        InvokePhysicalSource(() => { permissionChanges?.Dispose(); return true; });
        _scene.DelegateRequested -= OnDelegateRequested;
        _scene.NewBlankTaskRequested -= OnBlankRequested;
        _scene.RecentTaskRequested -= OnOpenRequested;
        _scene.RefreshRequested -= OnRefreshRequested;
        _scene.ScopeRequested -= OnScopeRequested;
        _scene.PageRequested -= OnPageRequested;
        _scene.PermissionReviewRequested -= OnPermissionReviewRequested;
        _scene.PermissionResponseRequested -= OnPermissionResponseRequested;
        _scene.PermissionReviewCloseRequested -= OnPermissionReviewCloseRequested;
        return InvokePhysicalSource(() => Dispatcher.UIThread.InvokeAsync(() =>
            InvokePhysicalSource(() => { _scene.RequestRetirement(); return true; })).GetTask());
    }
    private async Task DetachOriginalPresentationAsync()
    {
        await InvokePhysicalSource(_scene.CloseAndDrainAsync).ConfigureAwait(false);
        var actual = InvokePhysicalSource(() => Dispatcher.UIThread.InvokeAsync(() => InvokePhysicalSource(() =>
        {
            // Factor cleanup runs after independently joining all actual source/dispatcher tasks.
            if (ReferenceEquals(Content, Scene)) Content = null;
            return true;
        })).GetTask());
        try { await actual.ConfigureAwait(false); }
        catch (Exception error) { if (actual.Exception is { InnerExceptions.Count: > 0 } causes) throw causes; System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error).Throw(); throw; }
    }
    public void Dispose() => RequestRetirement();
    public ValueTask DisposeAsync() => new(CloseAndDrainAsync());
}
