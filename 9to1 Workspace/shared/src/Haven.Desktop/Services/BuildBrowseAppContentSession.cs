using Avalonia.Controls;
using Avalonia.Threading;
using CakeOS.Cui.Runtime;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.Views.Pages.Development;
using HavenOS.Apps.Browse;
using HavenOS.Apps.Dev;
using HavenOS.Apps.Spaces.Development;

namespace Haven.Desktop.Services;

/// <summary>
/// A distinct Build (Dev) source-workspace presentation per tab. The normal Build landing
/// (<c>9to1://dev</c>) presents the real <see cref="DeveloperSavedProjectsPage"/> saved-project
/// picker; selecting a project opens the REAL <see cref="DeveloperProjectWorkbenchPage"/> for the
/// exact resolved canonical project + Task/Run/context. A project-open address opens the workbench
/// directly. It creates no project, task, grant or readiness, and borrows the shared canonical
/// Dev/coordinator/Files owners. Missing or foreign identity refuses publication before any effect.
/// </summary>
internal sealed class BuildBrowseAppContentSession : IBrowseNativeAppContentSession
{
    private readonly BrowseAppContentRequest _request;
    private readonly BuildBrowseAppContentAddress _location;
    private readonly IDeveloperWorkspaceStore _workspaces;
    private readonly DeveloperTaskWorkspaceService _development;
    private readonly TaskExecutionCoordinator _canonical;
    private readonly DeveloperProjectWorkbenchPageFactory _factory;
    private readonly BuildLandingComposition _composition;
    private readonly CancellationTokenSource _lifetime;
    private readonly CancellationToken _token;
    private readonly object _gate = new();
    private readonly Border _view = new();
    private Task? _initialization, _close;
    private DeveloperProjectWorkbenchPage? _page;
    private DeveloperSavedProjectsPage? _landing;
    private volatile bool _published, _closing, _workbenchMode;

    internal BuildBrowseAppContentSession(BrowseAppContentRequest request, BuildBrowseAppContentAddress location,
        IDeveloperWorkspaceStore workspaces, DeveloperTaskWorkspaceService development,
        TaskExecutionCoordinator canonical, DeveloperProjectWorkbenchPageFactory factory,
        BuildLandingComposition composition, CancellationTokenSource lifetime)
    {
        _request = request; _location = location;
        _workspaces = workspaces; _development = development; _canonical = canonical; _factory = factory;
        _composition = composition; _lifetime = lifetime; _token = lifetime.Token;
    }

    public AppContentAddress Address => _request.Address;
    public string Title => "Build";
    public string IconKey => "code";
    public event EventHandler? StateChanged;

    public bool IsPublicationCurrent
    {
        get
        {
            lock (_gate)
            {
                return _published && !_closing && !_token.IsCancellationRequested
                    && OriginalInitialization is { IsCompletedSuccessfully: true }
                    && (_workbenchMode
                        ? _page is { } page && page.OriginalHost is not null
                        : _landing is not null);
            }
        }
    }
    public bool CanRetireOriginalView => OriginalClose is { IsCompletedSuccessfully: true };

    public Task? OriginalInitialization { get { lock (_gate) return _initialization; } }
    public Task? OriginalClose { get { lock (_gate) return _close; } }

    public Control OriginalNativeView
    {
        get
        {
            Dispatcher.UIThread.VerifyAccess();
            if (!IsPublicationCurrent) throw new InvalidOperationException("The original Build workbench has not completed admission.");
            return _view;
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

        // Normal Build landing: present the real saved-project picker. Selecting a saved project
        // transitions this session to the real compiled-CUI workbench via OpenExistingProjectAsync.
        if (_location.ProjectReference is null)
        {
            var readiness = _composition.Borrowed.BorrowOriginalHomeReadiness();
            var demandJoin = () => _composition.Borrowed.DemandOriginalHomeJoin();
            DeveloperSavedProjectsPage? retained = null;
            var landing = new DeveloperSavedProjectsPage(_composition.Catalog, _composition.Spaces, readiness, demandJoin,
                OpenExistingProjectAsync,
                _ => throw new NotSupportedException("Opening a Space is a Spaces surface action, not the Build landing."),
                _token, _token,
                actual => { if (retained is not null && !ReferenceEquals(retained, actual)) throw new InvalidOperationException("The Build landing factory acquired multiple different partial pages."); retained = actual; _landing = actual; });
            if (!ReferenceEquals(retained, landing))
                throw new InvalidOperationException("The SAME Build landing page was not retained before publication.");
            _view.Content = landing;
            await landing.ActivateAsync(token);
            DemandAlive(); token.ThrowIfCancellationRequested();
            _published = true;
            CloudflareOriginalExecutionGuard.InvokeOriginal(this, () => { StateChanged?.Invoke(this, EventArgs.Empty); return true; });
            return;
        }

        if (_location.TaskId == Guid.Empty || _location.ContextId == Guid.Empty)
            throw new UnauthorizedAccessException("Build project-open requires the exact canonical Task/Run/context.");

        var reference = _location.ProjectReference;
        var resolved = await ResolveAsync(reference, token);
        DemandAlive(); token.ThrowIfCancellationRequested();

        var snapshot = await _canonical.GetAsync(_location.TaskId, token);
        DemandAlive(); token.ThrowIfCancellationRequested();
        if (snapshot is null)
            throw new UnauthorizedAccessException("The canonical Task for this Build project is unavailable.");
        if (snapshot.ExecutionId != _location.ExecutionId || snapshot.ContextId != _location.ContextId)
            throw new UnauthorizedAccessException("The requested Task/Run/context does not match the current canonical Task.");

        await AcquireWorkbenchAsync(resolved, snapshot, token);
    }

    /// <summary>Acquires the real compiled-CUI workbench for the exact resolved canonical project and
    /// Task/Run/context, retaining the same partial page at constructor capture and replacing the
    /// session's presented view only after success.</summary>
    private async Task AcquireWorkbenchAsync(DeveloperResolvedProject resolved, TaskExecutionSnapshot snapshot, CancellationToken token)
    {
        var readiness = _composition.Borrowed.BorrowOriginalHomeReadiness();
        DeveloperProjectWorkbenchPage? retained = null;
        var page = _factory.CreateOriginal(resolved, snapshot,
            p => new BuildWorkbenchReadiness(p, readiness),
            actual =>
            {
                if (retained is not null && !ReferenceEquals(retained, actual))
                    throw new InvalidOperationException("The Build workbench factory acquired multiple different partial pages.");
                retained = actual; _page = actual; // Held at constructor capture, before later publication can throw.
            },
            token);
        if (!ReferenceEquals(retained, page))
            throw new InvalidOperationException("The SAME Build workbench page was not retained before publication.");
        _view.Content = page;
        _workbenchMode = true;
        await page.ActivateAsync(token);
        DemandAlive(); token.ThrowIfCancellationRequested();
        _published = true;
        CloudflareOriginalExecutionGuard.InvokeOriginal(this, () => { StateChanged?.Invoke(this, EventArgs.Empty); return true; });
    }

    private Task OpenExistingProjectAsync(SpaceDeveloperView view)
    {
        DemandAlive();
        ArgumentNullException.ThrowIfNull(view);
        return Dispatcher.UIThread.InvokeAsync<Task>(() => OpenExistingProjectCoreAsync(view, _token)).GetTask().Unwrap();
    }

    private async Task OpenExistingProjectCoreAsync(SpaceDeveloperView view, CancellationToken caller)
    {
        Dispatcher.UIThread.VerifyAccess();
        DemandAlive();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(caller, _token);
        var token = linked.Token;
        var snapshot = view.Task.Snapshot ?? throw new InvalidOperationException("The saved task snapshot is required.");
        if (snapshot.TaskId != view.Link.TaskId || snapshot.ExecutionId != view.Link.ExecutionId ||
            snapshot.ContextId != view.ContextReferenceId)
            throw new InvalidDataException("The saved task context does not match its recorded reference.");
        // Revalidate the real coordinator still agrees before opening the workbench.
        var current = await _canonical.GetAsync(view.Link.TaskId, token);
        DemandAlive(); token.ThrowIfCancellationRequested();
        if (current is null || current.ExecutionId != view.Link.ExecutionId || current.ContextId != view.ContextReferenceId)
            throw new UnauthorizedAccessException("The canonical Task for this saved project changed; refresh the saved entry.");

        // Swap to the real workbench. Deactivate the picker here; its full retirement happens at
        // session close (never from inside its own callback, which would self-join).
        await AcquireWorkbenchAsync(view.Project, snapshot, token);
        if (_landing is { } landing) landing.Deactivate();
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
            // Real workbench unsaved-work contract: veto only when there is unsaved/pending work.
            // Otherwise report a clean close, leaving the live view in place for the tab owner.
            if (_workbenchMode && _page is { } page && page.HasUnsavedWork) return Task.FromResult(false);
            return Task.FromResult(true);
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

        // Acquired and late originals, each with a final fence. Borrowed global services stay alive.
        if (_landing is { } landing)
        {
            Task close; try { close = landing.CloseAndDrainAsync(); } catch (Exception error) { failures.Add(error); close = Task.CompletedTask; }
            await Join(close);
        }
        if (_page is { } page)
        {
            Task close; try { close = page.CloseAndDrainAsync(); } catch (Exception error) { failures.Add(error); close = Task.CompletedTask; }
            await Join(close);
        }
        // Independently settle a faulted/canceled initialization, preserving its original errors.
        await Join(OriginalInitialization);
        try { var close = _composition.Borrowed.CloseAndDrainAsync(); await Join(close); }
        catch (Exception error) { failures.Add(error); }
        try { _lifetime.Dispose(); } catch (Exception error) { failures.Add(error); }
        if (failures.Count > 0) throw new AggregateException("The Build presentation did not retire cleanly.", failures);
    }

    /// <summary>Genuine readiness for the exact acquired workbench page. It observes the borrowed
    /// native Home readiness and the page's real presentation/generation currency, and owns a finite
    /// retirement port independent of the page/session (no delegation back, no self-join).</summary>
    private sealed class BuildWorkbenchReadiness : ICuiSceneReadiness,
        IDesktopOriginalRetirementParticipant, IDesktopOriginalRetirementJoinGuard
    {
        private readonly DeveloperProjectWorkbenchPage _page;
        private readonly CuiSceneHost _host;
        private readonly ICuiSceneReadiness _borrowedHome;
        private readonly DesktopOriginalWorkLifetime _work = new(
            static () => Task.CompletedTask, static () => Task.CompletedTask);
        [ThreadStatic] private static List<BuildWorkbenchReadiness>? _sourceCallbacks;

        internal BuildWorkbenchReadiness(DeveloperProjectWorkbenchPage page, ICuiSceneReadiness borrowedHome)
        {
            ArgumentNullException.ThrowIfNull(page); ArgumentNullException.ThrowIfNull(borrowedHome);
            _page = page; _host = page.OriginalHost; _borrowedHome = borrowedHome;
        }

        public ValueTask<CuiSceneAvailability> CheckAsync(CancellationToken cancellationToken) =>
            new(_work.RunAsync(original => CheckOriginalAsync(original, cancellationToken)));

        private async Task<CuiSceneAvailability> CheckOriginalAsync(DesktopOriginalWorkLifetime.Original original, CancellationToken caller)
        {
            var generation = _page.OriginalReadinessGeneration;
            original.BindPublicationGuard(() => _page.IsOriginalReadinessPresentationCurrent(_host, generation));
            using var scope = CancellationTokenSource.CreateLinkedTokenSource(original.Token, caller);
            var token = scope.Token;
            DemandOriginal(original, token);
            // Genuine borrowed Home readiness first; never a fabricated receipt.
            var home = await original.AwaitAsync(InvokeOriginalSource(() => _borrowedHome.CheckAsync(token).AsTask()));
            DemandOriginal(original, token);
            if (!Enum.IsDefined(home.State) || string.IsNullOrWhiteSpace(home.Code) || string.IsNullOrWhiteSpace(home.Message))
                throw new InvalidDataException("The borrowed Home readiness returned invalid observation metadata.");
            if (home.State != CuiSceneAvailabilityState.Ready)
                return home;
            // Real page presentation/generation currency is also required.
            if (!_page.IsOriginalReadinessPresentationCurrent(_host, generation))
                return new(CuiSceneAvailabilityState.Unavailable, "BuildWorkbenchNotCurrent", "The Build workbench presentation is not current.");
            return new(CuiSceneAvailabilityState.Ready, "BuildWorkbenchReady", "The Build workbench source presentation is current.");
        }

        private void DemandOriginal(DesktopOriginalWorkLifetime.Original original, CancellationToken token)
        {
            token.ThrowIfCancellationRequested(); original.DemandPublication();
            if (!ReferenceEquals(_host, _page.OriginalHost)) throw new InvalidOperationException("The Build workbench host was replaced.");
            token.ThrowIfCancellationRequested(); original.DemandPublication();
        }

        private T InvokeOriginalSource<T>(Func<T> callback)
        {
            var stack = _sourceCallbacks ??= []; stack.Add(this);
            try { return callback(); }
            catch (Exception error)
            {
                if (error is OperationCanceledException) throw new AggregateException("An actual Build readiness callback faulted.", error);
                throw;
            }
            finally { stack.RemoveAt(stack.Count - 1); }
        }

        public void DemandExternalOriginalRetirementJoin()
        {
            if (_sourceCallbacks?.Any(owner => ReferenceEquals(owner, this)) == true)
                throw new InvalidOperationException("An actual Build readiness callback cannot join its encompassing retirement.");
            _work.DemandExternalClose();
        }
        public void RequestRetirement() => _work.RequestRetirement();
        public Task CloseAndDrainAsync() { DemandExternalOriginalRetirementJoin(); return _work.CloseAndDrainAsync(); }
    }

    internal void RequestRetirement() => DemandExternalJoin();
    private void DemandAlive() { ObjectDisposedException.ThrowIf(_closing, this); _token.ThrowIfCancellationRequested(); }
    private void DemandExternalJoin() => CloudflareOriginalExecutionGuard.DemandExternalJoin(this);
}
