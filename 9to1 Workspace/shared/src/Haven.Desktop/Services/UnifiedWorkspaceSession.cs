using System.Runtime.ExceptionServices;
using Avalonia.Threading;
using Haven.Application;
using Haven.Browser;
using Haven.Core;
using HavenOS.Apps.Browse;

namespace Haven.Desktop.Services;

/// <summary>
/// Adapts each Browse window into the existing rich workspace snapshot and its
/// sole repository writer. Unopened windows and unprojectable rows stay intact.
/// Stop and join window producers, then seal/drain this owner, then ask the
/// coordinator to capture its final snapshot. This source stays registered through
/// that capture; closing a visible window does not dispose the application session.
/// </summary>
internal sealed class UnifiedWorkspaceSession : IWorkspaceSessionSource
{
    private readonly WorkspaceSessionCoordinator _coordinator;
    private readonly object _gate = new();
    private readonly Dictionary<Guid, WindowPort> _ports = [];
    private readonly List<Operation> _operations = [];
    private WorkspaceSessionSnapshot _snapshot;
    private Task _tail = Task.CompletedTask;
    private Task? _drain;

    private sealed class Operation
    {
        internal Task? Original;
        internal Task? Dispatcher;
        internal Task? Save;
        internal bool Succeeded => Original is { IsCompletedSuccessfully: true } &&
            (Dispatcher is null || Dispatcher.IsCompletedSuccessfully) &&
            (Save is null || Save.IsCompletedSuccessfully);
    }

    internal UnifiedWorkspaceSession(WorkspaceSessionCoordinator coordinator, WorkspaceSessionSnapshot? restored)
    {
        Dispatcher.UIThread.VerifyAccess();
        _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
        if (_coordinator.IsFinalSnapshotSealed)
            throw new InvalidOperationException("The existing workspace writer has already sealed its final snapshot.");
        if (restored is not null && restored.SchemaVersion != WorkspaceSessionSnapshot.CurrentSchemaVersion)
            throw new InvalidDataException("This workspace version must be migrated before it can be changed.");
        var now = DateTimeOffset.UtcNow;
        _snapshot = Detach(restored ?? new(WorkspaceSessionSnapshot.CurrentSchemaVersion, [], [], [], now));
        if (_snapshot.Windows.Count == 0)
            _snapshot = _snapshot with { Windows = [NewWindow(WorkspaceWindowKind.Main, now)] };
        MainWindowId = (_snapshot.Windows.FirstOrDefault(window => window.Kind == WorkspaceWindowKind.Main)
            ?? _snapshot.Windows[0]).Id;
        _coordinator.Register(this, WorkspaceWindowKind.Main, queueSave: false);
    }

    internal Guid MainWindowId { get; }
    internal IReadOnlyList<WorkspaceWindowSnapshot> Windows
    { get { Dispatcher.UIThread.VerifyAccess(); return _snapshot.Windows; } }

    internal IBrowseTabSessionPersistence OpenWindow(Guid windowId, string currentProfileId)
    {
        Dispatcher.UIThread.VerifyAccess();
        ArgumentException.ThrowIfNullOrWhiteSpace(currentProfileId);
        lock (_gate)
        {
            DemandAdmission();
            if (!_snapshot.Windows.Any(window => window.Id == windowId))
                throw new ArgumentException("Restore or create the canonical window first.", nameof(windowId));
            if (_ports.TryGetValue(windowId, out var existing))
            {
                if (!StringComparer.Ordinal.Equals(existing.ProfileId, currentProfileId))
                    throw new InvalidOperationException("A window session cannot replace its original profile.");
                return existing;
            }
            return _ports[windowId] = new(this, windowId, currentProfileId);
        }
    }

    internal Guid CreateWindow(WorkspaceWindowKind kind)
    {
        Dispatcher.UIThread.VerifyAccess();
        lock (_gate)
        {
            DemandAdmission();
            var window = NewWindow(kind, DateTimeOffset.UtcNow);
            _snapshot = _snapshot with { Windows = Array.AsReadOnly(_snapshot.Windows.Append(window).ToArray()) };
            return window.Id;
        }
    }

    private static WorkspaceWindowSnapshot NewWindow(WorkspaceWindowKind kind, DateTimeOffset now) => new(
        Guid.NewGuid(), kind, new(Guid.NewGuid(), WorkspaceLayoutKind.Single,
            SplitOrientation.Horizontal, 0.5, []), [], null, null, now);

    private sealed class WindowPort(UnifiedWorkspaceSession owner, Guid windowId, string profileId)
        : IBrowseTabSessionPersistence
    {
        internal string ProfileId { get; } = profileId;
        internal HashSet<Guid> ManagedIds { get; set; } = [];

        public Task<BrowseTabSessionProjection?> LoadAsync(CancellationToken cancellationToken) =>
            owner.Admit<BrowseTabSessionProjection?>(operation => owner.OnUiAsync(operation, () =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                var window = owner._snapshot.Windows.Single(item => item.Id == windowId);
                var rows = owner._snapshot.Tabs.ToDictionary(tab => tab.Id);
                var otherWindowReferences = owner._snapshot.Windows.Where(item => item.Id != windowId)
                    .SelectMany(item => item.OrderedTabIds.Concat(item.Layout.Panes.Select(pane => pane.TabId))
                        .Concat(item.SelectedTabId is { } selected ? [selected] : Array.Empty<Guid>())
                        .Concat(item.RecentlyClosedTabs.Select(tab => tab.Id))).ToHashSet();
                var projected = new List<BrowserTabState>();
                foreach (var id in window.OrderedTabIds)
                    if (rows.TryGetValue(id, out var row) &&
                        UnifiedWorkspaceTabState.TryProject(row, ProfileId, out var tab)) projected.Add(tab!);
                var openIds = projected.Select(tab => tab.Id).ToHashSet();
                var closed = window.RecentlyClosedTabs
                    .Where(tab => tab.Id != Guid.Empty && !window.OrderedTabIds.Contains(tab.Id) &&
                        !openIds.Contains(tab.Id) && !otherWindowReferences.Contains(tab.Id))
                    .TakeLast(25)
                    .Select(tab =>
                    {
                        var profile = string.IsNullOrEmpty(tab.ProfileId) ? ProfileId : tab.ProfileId;
                        var browserTab = new BrowserTabState(tab.Id, tab.Title, tab.Address,
                            BrowserTabPrivacy.Standard, tab.GroupId?.ToString("D") ?? string.Empty, tab.ClosedAt)
                        { ProfileId = profile, IsPinned = tab.IsPinned };
                        var engine = Enum.TryParse<BrowseEngineKind>(tab.EngineOverride, ignoreCase: true, out var parsed) &&
                            Enum.IsDefined(parsed) ? parsed : (BrowseEngineKind?)null;
                        return new BrowseClosedTabProjection(browserTab, tab.Index, engine);
                    }).ToArray();
                var panes = window.Layout.Kind == WorkspaceLayoutKind.Split && window.Layout.Panes.Count == 2
                    ? window.Layout.Panes.OrderBy(pane => pane.Order).Select(pane => pane.TabId).ToArray()
                    : [];
                if (panes.Length != 2 || panes[0] == panes[1] ||
                    window.SelectedTabId is not { } focused || !panes.Contains(focused) ||
                    !Enum.IsDefined(window.Layout.Orientation) || !double.IsFinite(window.Layout.PrimaryRatio) ||
                    window.Layout.PrimaryRatio < WorkspaceLayoutSnapshot.MinimumPaneRatio ||
                    window.Layout.PrimaryRatio > WorkspaceLayoutSnapshot.MaximumPaneRatio ||
                    panes.Any(id => !projected.Any(tab => tab.Id == id && tab.Privacy == BrowserTabPrivacy.Standard)))
                    panes = [];
                var groupIds = projected.Select(tab => Guid.TryParse(tab.Group, out var groupId) ? groupId : Guid.Empty)
                    .Where(id => id != Guid.Empty).Concat(closed.Select(tab => Guid.TryParse(tab.Tab.Group, out var groupId)
                        ? groupId : Guid.Empty)).Where(id => id != Guid.Empty).ToHashSet();
                var groups = owner._snapshot.Groups.Where(group => groupIds.Contains(group.Id))
                    .Select(group => new BrowseTabGroupProjection(group.Id, group.Name, group.IsCollapsed,
                        Array.AsReadOnly(group.OrderedTabIds.Where(openIds.Contains).ToArray())))
                    .ToArray();
                // Load does not authorize deletion. Browse can refuse a restored
                // row; only tabs it actually publishes in Save become managed.
                return (BrowseTabSessionProjection?)new BrowseTabSessionProjection(Array.AsReadOnly(projected.ToArray()),
                    projected.Any(tab => tab.Id == window.SelectedTabId) ? window.SelectedTabId : null)
                {
                    ClosedTabs = Array.AsReadOnly(closed), PaneTabIds = Array.AsReadOnly(panes),
                    SplitOrientation = panes.Length == 2 ? window.Layout.Orientation : null,
                    PrimaryPaneRatio = panes.Length == 2 ? window.Layout.PrimaryRatio : null,
                    Groups = Array.AsReadOnly(groups)
                };
            }));

        public Task SaveAsync(BrowseTabSessionProjection projection, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(projection);
            // Detach at admission so a producer cannot alter a queued save's IDs.
            var captured = projection with
            {
                Tabs = Array.AsReadOnly(projection.Tabs.ToArray()),
                ClosedTabs = Array.AsReadOnly(projection.ClosedTabs.ToArray()),
                PaneTabIds = projection.PaneTabIds is null ? null : Array.AsReadOnly(projection.PaneTabIds.ToArray()),
                Groups = projection.Groups is null ? null : Array.AsReadOnly(projection.Groups.Select(group => group with
                    { OrderedTabIds = Array.AsReadOnly(group.OrderedTabIds.ToArray()) }).ToArray())
            };
            return owner.Admit(async operation =>
            {
                await owner.OnUiAsync(operation, () =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (owner._coordinator.IsFinalSnapshotSealed)
                        throw new InvalidOperationException("A projection cannot change after the final workspace capture.");
                    var next = UnifiedWorkspaceSessionProjection.Merge(owner._snapshot, windowId, ProfileId, captured, ManagedIds);
                    owner._snapshot = Detach(next);
                    ManagedIds = captured.Tabs.Select(tab => tab.Id).ToHashSet();
                    operation.Save = CloudflareOriginalExecutionGuard.InvokeOriginal(owner,
                        () => owner._coordinator.SaveNowAsync(cancellationToken))
                        ?? throw new InvalidOperationException("The workspace coordinator supplied no original save task.");
                    return true;
                }).ConfigureAwait(false);
                await operation.Save!.ConfigureAwait(false);
                return true;
            });
        }
    }

    private Task<T> Admit<T>(Func<Operation, Task<T>> body)
    {
        CloudflareOriginalExecutionGuard.DemandExternalJoin(this);
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<T> returned;
        lock (_gate)
        {
            DemandAdmission();
            _operations.RemoveAll(operation => operation.Succeeded);
            if (_operations.Count >= 128)
                throw new InvalidOperationException("Outstanding workspace operations must settle before more can be admitted.");
            var original = new Operation();
            returned = RunAsync(start.Task, _tail, original, body);
            original.Original = returned;
            _operations.Add(original);
            _tail = returned;
        }
        start.SetResult();
        return returned;
    }

    private async Task<T> RunAsync<T>(Task start, Task predecessor, Operation operation, Func<Operation, Task<T>> body)
    {
        await start.ConfigureAwait(false);
        // Failure of a previous write remains retained for drain. A subsequent
        // explicit save can still write the user's newer state, but cannot report
        // the failed earlier operation as successful.
        try { await predecessor.ConfigureAwait(false); } catch { }
        using var phase = CloudflareOriginalExecutionGuard.EnterOriginal(this);
        return await body(operation).ConfigureAwait(false);
    }

    private Task<T> OnUiAsync<T>(Operation operation, Func<T> body)
    {
        var actual = Dispatcher.UIThread.InvokeAsync(() =>
            CloudflareOriginalExecutionGuard.InvokeOriginal(this, body)).GetTask();
        operation.Dispatcher = actual;
        return actual;
    }

    private void DemandAdmission()
    {
        if (_drain is not null || _coordinator.IsFinalSnapshotSealed)
            throw new InvalidOperationException("Workspace projection admission is sealed.");
    }

    internal Task SealAndDrainAsync()
    {
        CloudflareOriginalExecutionGuard.DemandExternalJoin(this);
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task result;
        lock (_gate)
        {
            if (_drain is not null) return _drain;
            result = _drain = DrainAsync(start.Task, _operations.ToArray());
        }
        // This seal does not cancel admitted saves. Final snapshot capture waits
        // for every queued mutation and actual coordinator write to settle.
        start.SetResult();
        return result;
    }

    private static async Task DrainAsync(Task start, Operation[] operations)
    {
        await start.ConfigureAwait(false);
        var failures = new List<Exception>();
        var joined = new HashSet<Task>(ReferenceEqualityComparer.Instance);
        foreach (var operation in operations)
        {
            // Awaiting Original first permits it to publish its actual stages.
            await Join(operation.Original!);
            if (operation.Dispatcher is { } dispatch) await Join(dispatch);
            if (operation.Save is { } save) await Join(save);
        }
        if (failures.Count == 1 && failures[0] is not OperationCanceledException)
            ExceptionDispatchInfo.Capture(failures[0]).Throw();
        if (failures.Count > 0) throw new AggregateException("Workspace operation failures remain unresolved.", failures);
        async Task Join(Task actual)
        {
            if (!joined.Add(actual)) return;
            try { await actual.ConfigureAwait(false); }
            catch (Exception error)
            {
                IEnumerable<Exception> causes = actual.Exception is { } group ? group.InnerExceptions : [error];
                foreach (var cause in causes)
                    if (!failures.Any(existing => ReferenceEquals(existing, cause))) failures.Add(cause);
            }
        }
    }

    bool IWorkspaceSessionSource.IsDisposed => false;
    IReadOnlyList<TabSessionSnapshot> IWorkspaceSessionSource.CreateTabSnapshots()
    { Dispatcher.UIThread.VerifyAccess(); return _snapshot.Tabs; }
    IReadOnlyList<TabGroupSnapshot> IWorkspaceSessionSource.CreateGroupSnapshots()
    { Dispatcher.UIThread.VerifyAccess(); return _snapshot.Groups; }
    IReadOnlyList<WorkspaceWindowSnapshot> IWorkspaceSessionSource.CreateWindowSnapshots(WorkspaceWindowKind kind)
    { Dispatcher.UIThread.VerifyAccess(); return _snapshot.Windows; }

    private static WorkspaceSessionSnapshot Detach(WorkspaceSessionSnapshot snapshot) => snapshot with
    {
        Tabs = Array.AsReadOnly(snapshot.Tabs.ToArray()),
        Groups = Array.AsReadOnly(snapshot.Groups.Select(group => group with
        { OrderedTabIds = Array.AsReadOnly(group.OrderedTabIds.ToArray()) }).ToArray()),
        Windows = Array.AsReadOnly(snapshot.Windows.Select(window => window with
        {
            OrderedTabIds = Array.AsReadOnly(window.OrderedTabIds.ToArray()),
            Layout = window.Layout with { Panes = Array.AsReadOnly(window.Layout.Panes.ToArray()) },
            RecentlyClosedTabs = Array.AsReadOnly(window.RecentlyClosedTabs.ToArray())
        }).ToArray())
    };
}
