using Avalonia.Controls;
using Avalonia.Threading;
using CakeOS.Cui.Runtime;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.Views.Pages.Development;
using HavenOS.Apps.Browse;
using HavenOS.Apps.Dev;

namespace Haven.Desktop.Services;

/// <summary>
/// A distinct Build (Dev) source-workspace presentation per tab. It borrows the shared canonical
/// Dev/coordinator/Files owners and opens the REAL <see cref="DeveloperProjectWorkbenchPage"/> for
/// the exact resolved canonical project + Task/Run/context. It creates no project, task, grant or
/// readiness. Missing or foreign identity refuses publication before any project/task effect.
/// </summary>
internal sealed class BuildBrowseAppContentSession : IBrowseNativeAppContentSession
{
    private readonly BrowseAppContentRequest _request;
    private readonly BuildBrowseAppContentAddress _location;
    private readonly IDeveloperWorkspaceStore _workspaces;
    private readonly DeveloperTaskWorkspaceService _development;
    private readonly TaskExecutionCoordinator _canonical;
    private readonly DeveloperProjectWorkbenchPageFactory _factory;
    private readonly CancellationTokenSource _lifetime;
    private readonly CancellationToken _token;
    private readonly object _gate = new();
    private Task? _initialization, _close;
    private DeveloperProjectWorkbenchPage? _page;
    private DeveloperResolvedProject? _resolved;
    private volatile bool _published, _closing;

    internal BuildBrowseAppContentSession(BrowseAppContentRequest request, BuildBrowseAppContentAddress location,
        IDeveloperWorkspaceStore workspaces, DeveloperTaskWorkspaceService development,
        TaskExecutionCoordinator canonical, DeveloperProjectWorkbenchPageFactory factory,
        CancellationTokenSource lifetime)
    {
        _request = request; _location = location;
        _workspaces = workspaces; _development = development; _canonical = canonical; _factory = factory;
        _lifetime = lifetime; _token = lifetime.Token;
    }

    public AppContentAddress Address => _request.Address;
    public string Title => "Build";
    public string IconKey => "code";
    public event EventHandler? StateChanged;
    public bool IsPublicationCurrent => _published && !_closing && !_token.IsCancellationRequested
        && OriginalInitialization is { IsCompletedSuccessfully: true }
        && _page is { } page && page.OriginalHost is not null;
    public bool CanRetireOriginalView => OriginalClose is { IsCompletedSuccessfully: true };

    public Task? OriginalInitialization { get { lock (_gate) return _initialization; } }
    public Task? OriginalClose { get { lock (_gate) return _close; } }

    public Control OriginalNativeView
    {
        get
        {
            Dispatcher.UIThread.VerifyAccess();
            if (!IsPublicationCurrent) throw new InvalidOperationException("The original Build workbench has not completed admission.");
            return _page ?? throw new InvalidOperationException("The Build workbench presentation is absent.");
        }
    }

    public Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (_initialization is not null) return _initialization;
            DemandAlive();
            var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _initialization = InitializeDriverAsync(gate.Task, cancellationToken);
            gate.SetResult(); return _initialization;
        }
    }

    private async Task InitializeDriverAsync(Task gate, CancellationToken caller)
    {
        await gate.ConfigureAwait(false);
        var dispatch = Dispatcher.UIThread.InvokeAsync<Task>(() => InitializeOnUiAsync(caller)).GetTask();
        var work = await dispatch.ConfigureAwait(false);
        await work.ConfigureAwait(false);
    }

    private async Task InitializeOnUiAsync(CancellationToken caller)
    {
        Dispatcher.UIThread.VerifyAccess();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(caller, _token);
        var token = linked.Token;
        DemandAlive(); token.ThrowIfCancellationRequested();

        if (_location.ProjectReference is null)
            throw new NotSupportedException("Build project-open requires a canonical project reference; the Build landing is presented by the saved-project host.");
        if (_location.TaskId == Guid.Empty || _location.ContextId == Guid.Empty)
            throw new UnauthorizedAccessException("Build project-open requires the exact canonical Task/Run/context.");

        var reference = _location.ProjectReference;
        // Resolve the exact existing canonical project (validates workspace/project/root revision).
        var resolved = await ResolveAsync(reference, token);
        _resolved = resolved;
        DemandAlive(); token.ThrowIfCancellationRequested();

        // Validate the canonical Task/Run/context against the real coordinator before any effect.
        var snapshot = await _canonical.GetAsync(_location.TaskId, token);
        DemandAlive(); token.ThrowIfCancellationRequested();
        if (snapshot is null)
            throw new UnauthorizedAccessException("The canonical Task for this Build project is unavailable.");
        if (snapshot.ExecutionId != _location.ExecutionId || snapshot.ContextId != _location.ContextId)
            throw new UnauthorizedAccessException("The requested Task/Run/context does not match the current canonical Task.");

        DeveloperProjectWorkbenchPage? retained = null;
        var page = _factory.CreateOriginal(resolved, snapshot, page => new BuildWorkbenchReadiness(this, page), actual =>
        {
            if (retained is not null && !ReferenceEquals(retained, actual))
                throw new InvalidOperationException("The Build workbench factory acquired multiple different partial pages.");
            retained = actual;
        }, token);
        if (!ReferenceEquals(retained, page))
            throw new InvalidOperationException("The SAME Build workbench page was not retained before publication.");
        _page = page;

        await page.ActivateAsync(token);
        DemandAlive(); token.ThrowIfCancellationRequested();
        _published = true;
        CloudflareOriginalExecutionGuard.InvokeOriginal(this, () => { StateChanged?.Invoke(this, EventArgs.Empty); return true; });
    }

    private async Task<DeveloperResolvedProject> ResolveAsync(DeveloperProjectReference reference, CancellationToken token)
    {
        var stored = await _workspaces.GetAsync(reference.WorkspaceId, token);
        if (!stored.Succeeded || stored.Value is null)
            throw new UnauthorizedAccessException("The saved workspace for this Build project is unavailable.");
        var workspace = stored.Value;
        if (workspace.WorkspaceId != reference.WorkspaceId || workspace.Revision != reference.WorkspaceRevision)
            throw new InvalidDataException("The workspace changed; reopen its current Build reference before acting.");
        var project = workspace.Projects.SingleOrDefault(value => value.ProjectId == reference.ProjectId);
        if (project is null || project.Revision != reference.ProjectRevision)
            throw new UnauthorizedAccessException("The canonical Build project is unavailable or changed.");
        var root = workspace.Roots.SingleOrDefault(value => value.RootId == reference.RootId);
        if (root is null || !project.RootIds.Contains(reference.RootId))
            throw new UnauthorizedAccessException("The selected Build root does not belong to the project.");

        var resolvedResult = await _development.ResolveAsync(reference, token);
        if (!resolvedResult.Succeeded || resolvedResult.Value is null)
            throw new UnauthorizedAccessException(resolvedResult.Error?.Message ?? "The canonical Build project could not be resolved.");
        return resolvedResult.Value;
    }

    public Task<bool> TryPrepareToCloseAsync(CancellationToken cancellationToken = default)
    {
        DemandExternalJoin();
        lock (_gate)
        {
            if (_close is not null) return Task.FromResult(false);
            if (_page is not { } page) return Task.FromResult(true);
            // The workbench retires its acquired business originals on close; there is no
            // reversible preparation, so report false (leave alive/command) to the tab owner.
            return Task.FromResult(false);
        }
    }

    public Task CloseAndDrainAsync()
    {
        DemandExternalJoin();
        lock (_gate)
        {
            if (_close is not null) return _close;
            _closing = true; _published = false;
            var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _close = CloseDriverAsync(gate.Task); gate.SetResult(); return _close;
        }
    }

    private async Task CloseDriverAsync(Task gate)
    {
        await gate.ConfigureAwait(false);
        var dispatch = Dispatcher.UIThread.InvokeAsync<Task>(CloseOnUiAsync).GetTask();
        var work = await dispatch.ConfigureAwait(false);
        await work.ConfigureAwait(false);
    }

    private async Task CloseOnUiAsync()
    {
        List<Exception> failures = [];
        async Task Join(Task? original) { if (original is null) return; try { await original; } catch (Exception error) { if (!failures.Any(cause => ReferenceEquals(cause, error))) failures.Add(error); } }
        try { CloudflareOriginalExecutionGuard.InvokeOriginal(this, () => { _lifetime.Cancel(); return true; }); }
        catch (Exception error) { failures.Add(error); }
        if (_page is { } page)
        {
            Task pageClose;
            try { pageClose = page.CloseAndDrainAsync(); }
            catch (Exception error) { failures.Add(error); pageClose = Task.CompletedTask; }
            await Join(pageClose);
        }
        await Join(OriginalInitialization);
        try { _lifetime.Dispose(); } catch (Exception error) { failures.Add(error); }
        if (failures.Count > 0) throw new AggregateException("The Build workbench presentation did not retire cleanly.", failures);
    }

    /// <summary>Genuine readiness for the exact acquired workbench page, derived from its actual
    /// host/generation currency. This hands no project/task/actor/flame authority.</summary>
    private sealed class BuildWorkbenchReadiness : ICuiSceneReadiness,
        IDesktopOriginalRetirementParticipant, IDesktopOriginalRetirementJoinGuard
    {
        private readonly BuildBrowseAppContentSession _session;
        private readonly DeveloperProjectWorkbenchPage _page;
        internal BuildWorkbenchReadiness(BuildBrowseAppContentSession session, DeveloperProjectWorkbenchPage page)
        { _session = session; _page = page; }

        public ValueTask<CuiSceneAvailability> CheckAsync(CancellationToken cancellationToken)
        {
            var generation = _page.OriginalReadinessGeneration;
            if (!_page.IsOriginalReadinessPresentationCurrent(_page.OriginalHost, generation))
                return new(new(CuiSceneAvailabilityState.Unavailable, "BuildWorkbenchNotCurrent",
                    "The Build workbench presentation is not current."));
            return new(new(CuiSceneAvailabilityState.Ready, "BuildWorkbenchReady",
                "The Build workbench source presentation is current."));
        }
        public void DemandExternalOriginalRetirementJoin() => _page.DemandExternalOriginalRetirementJoin();
        public void RequestRetirement() => _session.RequestRetirement();
        public Task CloseAndDrainAsync() => _page.CloseAndDrainAsync();
    }

    internal void RequestRetirement() => DemandExternalJoin();
    private void DemandAlive() { ObjectDisposedException.ThrowIf(_closing, this); _token.ThrowIfCancellationRequested(); }
    private void DemandExternalJoin() => CloudflareOriginalExecutionGuard.DemandExternalJoin(this);
}
