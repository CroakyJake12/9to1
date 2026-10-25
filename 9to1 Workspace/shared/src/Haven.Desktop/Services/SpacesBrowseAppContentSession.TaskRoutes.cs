using Avalonia.Threading;
using CakeOS.Cui.Runtime;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.Views.Pages.Spaces;
using Haven.Desktop.Views.Pages.Tasks;
using HavenOS.Apps.Spaces.Tasks;
using Microsoft.Extensions.DependencyInjection;

namespace Haven.Desktop.Services;

internal sealed partial class SpacesBrowseAppContentSession
{
    private Func<SpaceTaskWidgetPage, NativeCanonicalTaskSceneReadiness>? _bindTaskFrame;
    private SpaceTaskWorkspaceService? _taskSource;
    private TaskExecutionCoordinator? _taskCanonical;
    private SpaceRegistry? _taskRegistry;
    private IConversationRepository? _taskConversations;
    private UserPreferencesService? _taskPreferencesOwner;
    private readonly List<SpaceTaskWidgetPage> _ownedTaskViews = [];
    private readonly Dictionary<(Guid Task, Guid Run), SpaceTaskWidgetPage> _taskViews = [];
    private readonly Dictionary<SpaceTaskWidgetPage, Task> _taskViewCloses = [];
    private sealed record PublishedTaskRoute(SpaceTaskWidgetPage Page, AppContentAddress Address);
    private PublishedTaskRoute? _publishedTaskRoute;
    private sealed record TaskOpenTicket(SpacesHomePage Owner, SpacesHomePage.TaskOpenIntent Intent, long Epoch);
    internal IReadOnlyList<SpaceTaskWidgetPage> OriginalTaskViews { get { lock (_gate) return _ownedTaskViews.ToArray(); } }

    private void BindTaskSidebar(SpacesHomePage page)
    {
        if (_bindTaskFrame is null || _services.GetService<TaskExecutionCoordinator>() is not { } coordinator ||
            _services.GetService<UserPreferencesService>() is not { } preferences) return;
        var registry = _services.GetRequiredService<SpaceRegistry>();
        var conversations = _services.GetRequiredService<IConversationRepository>();
        _taskCanonical = coordinator; _taskRegistry = registry; _taskConversations = conversations; _taskPreferencesOwner = preferences;
        _taskSource ??= new(registry, conversations, coordinator);
        page.BindOriginalTaskSidebar(_taskSource, coordinator, preferences, PublishTaskRowsAsync, () => IsPublicationCurrent);
    }
    private void DemandTaskComposition()
    {
        DemandAlive();
        if (_taskSource is null || _taskCanonical is null || _taskRegistry is null || _taskConversations is null || _taskPreferencesOwner is null ||
            !ReferenceEquals(_services.GetService<TaskExecutionCoordinator>(), _taskCanonical) ||
            !ReferenceEquals(_services.GetService<SpaceRegistry>(), _taskRegistry) ||
            !ReferenceEquals(_services.GetService<IConversationRepository>(), _taskConversations) ||
            !ReferenceEquals(_services.GetService<UserPreferencesService>(), _taskPreferencesOwner))
            throw new UnauthorizedAccessException("The original Task services or same-profile preference owner changed.");
    }
    private async Task PublishTaskRowsAsync(Action commit, CancellationToken token)
    {
        Dispatcher.UIThread.VerifyAccess(); DemandTaskComposition();
        var selection = _spacesPage?.Controller.State.Selection;
        var epoch = _routeEpoch;
        try { _ = await ReadHistoryHomeAuthorityAsync(token); }
        catch (SpacesHistoryAuthorityRefusal) { _spacesPage?.RefuseTaskSidebarAccess(); return; }
        Dispatcher.UIThread.VerifyAccess();
        if (!IsPublicationCurrent || selection?.Id != SpaceRegistry.TasksSpaceId ||
            !ReferenceEquals(_spacesPage?.Controller.State.Selection, selection) || epoch != _routeEpoch) return;
        try { DemandHistoryServicesCurrent(_services.GetRequiredService<HavenOS.Home.Core.HomeNativeWindowsComposition>()); }
        catch (SpacesHistoryAuthorityRefusal) { _spacesPage?.RefuseTaskSidebarAccess(); return; }
        DemandTaskComposition();
        commit(); // SAME actual Home observation continuation; no scheduling hop before UI publication.
    }
    private void OwnTaskSource(Action callback) => CloudflareOriginalExecutionGuard.InvokeOriginal(this, () => { callback(); return true; });

    private async Task<bool> TryRestoreRequestedTaskAsync(CancellationToken token)
    {
        var segments = _request.Address.ResourceSegments;
        if (segments.Count == 0 || segments[0] != "task") return false;
        if (segments.Count != 3 || !Guid.TryParseExact(segments[1], "D", out var taskId) || taskId == Guid.Empty ||
            !Guid.TryParseExact(segments[2], "D", out var runId) || runId == Guid.Empty)
            throw new FormatException("The saved Task address requires its exact Task and Run identities.");
        DemandTaskComposition();
        var source = _taskSource ?? throw new InvalidOperationException("The original Task observation owner is unavailable.");
        var authority = await ReadHistoryHomeAuthorityAsync(token);
        var current = await source.ReadTaskAsync(taskId, token, OwnTaskSource);
        if (current.Snapshot.ExecutionId != runId) throw new InvalidOperationException("The saved original Run is no longer current. No replacement task was opened.");
        if (await ReadHistoryHomeAuthorityAsync(token) != authority) throw new SpacesHistoryAuthorityRefusal("Home authority changed while restoring this Task.");
        if (_spacesPage is { } home) await home.Controller.SelectSpaceAsync(SpaceRegistry.TasksSpaceId, token);
        await OpenTaskOnUiAsync(current, token);
        return true;
    }
    private async Task OpenSidebarTaskOnUiAsync(TaskOpenTicket ticket, CancellationToken token)
    {
        Dispatcher.UIThread.VerifyAccess();
        void DemandCurrent()
        {
            DemandAlive(); token.ThrowIfCancellationRequested();
            if (!IsPublicationCurrent || !ReferenceEquals(_spacesPage, ticket.Owner) || _routeEpoch != ticket.Epoch ||
                !ticket.Owner.IsCurrentTaskOpenIntent(ticket.Intent))
                throw new SpacesWorkspaceActionRefusal("This task tile changed. Refresh before opening it.");
        }
        DemandCurrent(); DemandTaskComposition();
        var authority = await ReadHistoryHomeAuthorityAsync(token);
        DemandCurrent();
        var current = await (_taskSource ?? throw new InvalidOperationException("The original Task source is unavailable."))
            .ReadSelectionAsync(ticket.Intent.TaskSelection, token, OwnTaskSource);
        DemandCurrent();
        if (await ReadHistoryHomeAuthorityAsync(token) != authority) throw new SpacesHistoryAuthorityRefusal("The actual Home profile changed before task open.");
        DemandCurrent();
        await OpenTaskOnUiAsync(current, token);
    }
    private async Task OpenTaskOnUiAsync(AuthorizedTaskContextObservation current, CancellationToken token)
    {
        Dispatcher.UIThread.VerifyAccess(); DemandTaskComposition(); token.ThrowIfCancellationRequested();
        var source = _taskSource ?? throw new InvalidOperationException("The original Task source is unavailable.");
        if (!source.IsIssuedSelection(current.Selection)) throw new UnauthorizedAccessException("The same actual source must issue this Task selection.");
        var key = (current.Snapshot.TaskId, current.Snapshot.ExecutionId);
        if (!_taskViews.TryGetValue(key, out var page) || page.OriginalClose is not null)
        {
            lock (_gate) if (_ownedTaskViews.Count >= 128)
                throw new SpacesWorkspaceActionRefusal("This tab has reached its retained Task-view limit. Close the tab before opening another task.");
            var frame = new TaskFrameObservation();
            page = new SpaceTaskWidgetPage(source, _taskCanonical!, current.Selection, frame);
            lock (_gate) _ownedTaskViews.Add(page); // Retain before preferences, frame factory or native activation callbacks.
            page.BindOriginalComposerPreferences(_taskPreferencesOwner!);
            var actual = await page.AcquireOriginalNativeReadinessAsync(_bindTaskFrame ?? throw new InvalidOperationException("The native Task frame owner is unavailable."));
            frame.Bind(actual);
            token.ThrowIfCancellationRequested(); DemandAlive();
            await page.ActivateAsync(token);
            token.ThrowIfCancellationRequested(); DemandAlive();
            // A displayed task is revalidated after actual native admission, including its nullable origin.
            _ = await source.ReadSelectionAsync(current.Selection, token, OwnTaskSource);
            lock (_gate) _taskViews[key] = page;
        }
        _ = await ReadHistoryHomeAuthorityAsync(token);
        token.ThrowIfCancellationRequested(); DemandTaskComposition();
        lock (_gate)
        {
            if (_spacesPage is not { } home || !home.TryMountSpaceWorkspace(page))
                throw new InvalidOperationException("The authored Space workspace is unavailable for this actual Task.");
            SetRootContent(home);
            _publishedChatRoute = null; _publishedDetailsRoute = null; _publishedBuilderRoute = null;
            _publishedTaskRoute = new(page, AppContentAddress.Parse($"9to1://spaces/task/{key.TaskId:D}/{key.ExecutionId:D}"));
            _address = _publishedTaskRoute.Address; _routeEpoch++;
            home.NotifyNavigationPublished();
        }
        PublishStateChanged();
        await RefreshSpacesConversationsAsync(token);
    }
    private sealed class TaskFrameObservation : ICuiSceneReadiness
    {
        private ICuiSceneReadiness? _actual;
        internal void Bind(ICuiSceneReadiness actual) => _actual = actual ?? throw new ArgumentNullException(nameof(actual));
        public ValueTask<CuiSceneAvailability> CheckAsync(CancellationToken token) =>
            (_actual ?? throw new InvalidOperationException("The actual native Task readiness owner is unbound.")).CheckAsync(token);
    }
    private void BeginTaskViewRetirement(List<Exception> failures)
    {
        SpaceTaskWidgetPage[] pages; lock (_gate) pages = _ownedTaskViews.ToArray();
        foreach (var page in pages)
            try { page.RequestRetirement(); }
            catch (Exception error) { failures.Add(error); }
    }
    private async Task JoinTaskViewRetirementAsync(List<Exception> failures)
    {
        SpaceTaskWidgetPage[] pages; lock (_gate) pages = _ownedTaskViews.ToArray();
        foreach (var page in pages)
        {
            Task? original = null;
            try
            {
                lock (_gate)
                    if (!_taskViewCloses.TryGetValue(page, out original)) _taskViewCloses[page] = original = page.CloseAndDrainAsync();
                await (original ?? throw new InvalidOperationException("The actual Task view did not return its retirement original.")).ConfigureAwait(false);
            }
            catch (Exception error) { if (original is null) failures.Add(error); else AddActualCauses(failures, original, error); }
        }
    }
}
