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
/// Dev/coordinator/Files owners. It admits the genuine same-process Home/Files shell (no Files UI),
/// retains/joins its ORIGINAL task, and revalidates the ACTUAL authenticated profile before/after
/// acquisitions and on live access. Missing or foreign identity refuses publication before any effect.
///
/// Every acquired workbench (published or a failed candidate) is retained as an owner and drained at
/// close with a final acquisition fence; only the successfully published page is the live view. The
/// borrowed native route is attached last through a non-throwing <see cref="AttachBorrowed"/> so the
/// session is its retained external owner and settles its cleanup asynchronously.
/// </summary>
internal sealed class BuildBrowseAppContentSession : IBrowseNativeAppContentSession
{
    private readonly BrowseAppContentRequest _request;
    private readonly BuildBrowseAppContentAddress _location;
    private readonly IDeveloperWorkspaceStore _workspaces;
    private readonly DeveloperTaskWorkspaceService _development;
    private readonly TaskExecutionCoordinator _canonical;
    private readonly DeveloperProjectWorkbenchPageFactory _factory;
    private readonly SpaceRegistry _spaces;
    private readonly SpaceDevelopmentReferenceCatalog _catalog;
    private readonly Func<BrowseNavigationIntent, Task, bool>? _queueNavigation;
    private readonly CancellationTokenSource _lifetime;
    private readonly CancellationToken _token;
    private Func<string, CancellationToken, Task<DeveloperProjectReference>>? _manualEntryAgent;
    private Func<string, DeveloperProjectTemplateMaterialization, CancellationToken, Task<DeveloperProjectReference>>? _templateEntryAgent;
    private Func<DeveloperProjectReference, DeveloperProjectTemplateMaterialization, Task>? _templateOpenAgent;
    private DeveloperProjectCatalog? _templateCatalog;
    private DeveloperProjectTemplateMaterializer? _templateMaterializer;
    private readonly object _gate = new();
    private readonly Border _view = new();
    private readonly List<DeveloperProjectWorkbenchPage> _workbenches = [];
    private IBuildBorrowedResources? _borrowed;
    private Task? _initialization, _close, _admission;
    private DeveloperProjectWorkbenchPage? _page;
    private DeveloperSavedProjectsPage? _landing;
    private volatile bool _published, _closing, _workbenchMode;

    internal BuildBrowseAppContentSession(BrowseAppContentRequest request, BuildBrowseAppContentAddress location,
        IDeveloperWorkspaceStore workspaces, DeveloperTaskWorkspaceService development,
        TaskExecutionCoordinator canonical, DeveloperProjectWorkbenchPageFactory factory,
        SpaceRegistry spaces, SpaceDevelopmentReferenceCatalog catalog,
        CancellationTokenSource lifetime, Func<BrowseNavigationIntent, Task, bool>? queueNavigation)
    {
        _request = request; _location = location;
        _workspaces = workspaces; _development = development; _canonical = canonical; _factory = factory;
        _spaces = spaces ?? throw new ArgumentNullException(nameof(spaces));
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _queueNavigation = queueNavigation;
        _lifetime = lifetime; _token = lifetime.Token;
    }

    /// <summary>Attaches the borrowed native route resources LAST, after session construction. This
    /// is non-throwing so a route acquired after the session is always retained by this session, the
    /// external owner that drains its cleanup on close (no leak, no block/detach).</summary>
    internal void AttachBorrowed(IBuildBorrowedResources borrowed)
    {
        ArgumentNullException.ThrowIfNull(borrowed);
        lock (_gate) { if (_borrowed is not null && !ReferenceEquals(_borrowed, borrowed)) throw new InvalidOperationException("Build borrowed resources were already attached."); _borrowed = borrowed; }
    }

    /// <summary>Attaches the genuine manual (no-AI) project creation agent. It registers a fresh
    /// canonical workspace/project from a selected existing Files folder through the real setup
    /// owners and returns the saved reference for the SAME manual-open path. The landing exposes a
    /// typed create action only when this agent is present.</summary>
    internal void AttachManualEntryAgent(Func<string, CancellationToken, Task<DeveloperProjectReference>> agent)
    {
        ArgumentNullException.ThrowIfNull(agent);
        lock (_gate) _manualEntryAgent = agent;
    }

    /// <summary>Attaches the template route to the same manual registration and workbench owners.
    /// The template payload supplies source text only; registration uses the existing Home/Files
    /// setup journal and every file is later created by the workbench's distinct reviewed CREATE.</summary>
    internal void AttachTemplateProjectAgents(DeveloperProjectCatalog catalog,
        DeveloperProjectTemplateMaterializer materializer,
        Func<string, DeveloperProjectTemplateMaterialization, CancellationToken, Task<DeveloperProjectReference>> register,
        Func<DeveloperProjectReference, DeveloperProjectTemplateMaterialization, Task> open)
    {
        ArgumentNullException.ThrowIfNull(catalog); ArgumentNullException.ThrowIfNull(materializer);
        ArgumentNullException.ThrowIfNull(register); ArgumentNullException.ThrowIfNull(open);
        lock (_gate)
        {
            _templateCatalog = catalog; _templateMaterializer = materializer;
            _templateEntryAgent = register; _templateOpenAgent = open;
        }
    }

    private IBuildBorrowedResources Borrowed
    {
        get { lock (_gate) return _borrowed ?? throw new InvalidOperationException("The Build borrowed native resources are not attached."); }
    }

    private Func<string, CancellationToken, Task<DeveloperProjectReference>>? ManualEntryAgent
    {
        get { lock (_gate) return _manualEntryAgent; }
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
            // Fast shallow live-view guard gated by current publication. The genuine retained async
            // revalidation contract is used around awaited acquisition/publication (not here, where
            // a synchronous accessor cannot await).
            Borrowed.CheckOriginalAppContentProfile(_request.ProfileId);
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

        // Admit the genuine same-process Home/Files shell (no Files UI) and retain/join its
        // ORIGINAL task. A non-blank profile label is never authorization by itself.
        _admission = Borrowed.AdmitOriginalShellInitializationAsync(token);
        await _admission;
        await Borrowed.RevalidateOriginalAppContentAsync(_request.ProfileId, token);
        DemandAlive(); token.ThrowIfCancellationRequested();

        // Normal Build landing: present the real saved-project picker. Selecting a saved project
        // transitions this session to the real compiled-CUI workbench via OpenExistingProjectAsync.
        if (_location.ProjectReference is null)
        {
            var readiness = Borrowed.BorrowOriginalHomeReadiness();
            var demandJoin = () => Borrowed.DemandOriginalHomeJoin();
            DeveloperSavedProjectsPage? retained = null;
            var landing = new DeveloperSavedProjectsPage(_catalog, _spaces, readiness, demandJoin,
                OpenExistingProjectAsync,
                OpenExistingSpaceAsync,
                _workspaces,
                OpenExistingManualProjectAsync,
                _token, _token,
                actual => { if (retained is not null && !ReferenceEquals(retained, actual)) throw new InvalidOperationException("The Build landing factory acquired multiple different partial pages."); retained = actual; _landing = actual; },
                ManualEntryAgent, _templateCatalog, _templateMaterializer,
                _templateEntryAgent, _templateOpenAgent);
            if (!ReferenceEquals(retained, landing))
                throw new InvalidOperationException("The SAME Build landing page was not retained before publication.");
            await Borrowed.RevalidateOriginalAppContentAsync(_request.ProfileId, token);
            _view.Child = landing;
            await landing.ActivateAsync(token);
            await Borrowed.RevalidateOriginalAppContentAsync(_request.ProfileId, token);
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
        await Borrowed.RevalidateOriginalAppContentAsync(_request.ProfileId, token);

        var snapshot = await _canonical.GetAsync(_location.TaskId, token);
        DemandAlive(); token.ThrowIfCancellationRequested();
        if (snapshot is null)
            throw new UnauthorizedAccessException("The canonical Task for this Build project is unavailable.");
        if (snapshot.ExecutionId != _location.ExecutionId || snapshot.ContextId != _location.ContextId)
            throw new UnauthorizedAccessException("The requested Task/Run/context does not match the current canonical Task.");
        await Borrowed.RevalidateOriginalAppContentAsync(_request.ProfileId, token);

        await AcquireWorkbenchAsync(resolved, snapshot, token);
    }

    /// <summary>Acquires the real compiled-CUI workbench for the exact resolved canonical project and
    /// Task/Run/context. EVERY acquired candidate (including one that fails activation) is retained
    /// as an owner and drained at close; the published live page is set only after successful
    /// activation and the visible swap, so a failed activation keeps the prior usable landing.</summary>
    private async Task AcquireWorkbenchAsync(DeveloperResolvedProject resolved, TaskExecutionSnapshot snapshot, CancellationToken token)
    {
        var readiness = Borrowed.BorrowOriginalHomeReadiness();
        DeveloperProjectWorkbenchPage? retained = null;
        var page = _factory.CreateOriginal(resolved, snapshot,
            p => new BuildWorkbenchReadiness(p, readiness),
            actual =>
            {
                if (retained is not null && !ReferenceEquals(retained, actual))
                    throw new InvalidOperationException("The Build workbench factory acquired multiple different partial pages.");
                retained = actual; RetainWorkbench(actual); // Retained at constructor capture, before later publication can throw.
            },
            token);
        if (!ReferenceEquals(retained, page))
            throw new InvalidOperationException("The SAME Build workbench page was not retained before publication.");
        await page.ActivateAsync(token);
        await Borrowed.RevalidateOriginalAppContentAsync(_request.ProfileId, token);
        DemandAlive(); token.ThrowIfCancellationRequested();
        _view.Child = page;
        _workbenchMode = true;
        lock (_gate) _page = page; // Published only after successful activation/swap.
        _published = true;
        CloudflareOriginalExecutionGuard.InvokeOriginal(this, () => { StateChanged?.Invoke(this, EventArgs.Empty); return true; });
    }

    private void RetainWorkbench(DeveloperProjectWorkbenchPage page)
    {
        lock (_gate) { if (!_workbenches.Contains(page)) _workbenches.Add(page); }
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
        await Borrowed.RevalidateOriginalAppContentAsync(_request.ProfileId, token);

        // Swap to the real workbench only after successful activation. Deactivate the picker here;
        // its full retirement happens at session close (never from inside its own callback).
        await AcquireWorkbenchAsync(view.Project, snapshot, token);
        if (_landing is { } landing) landing.Deactivate();
    }

    /// <summary>Manual (no-AI) open of a normally saved project. It resolves the REAL workspace-
    /// store identity (no Task/Run/context/model, no generation, no fabricated context) and opens the
    /// SAME compiled-CUI workbench in manual mode. Read/tree/search operate on the actual saved root;
    /// edits and process execution fail closed (they require a separate current authorized Task/
    /// execution authority).</summary>
    private Task OpenExistingManualProjectAsync(DeveloperProjectReference reference)
    {
        DemandAlive();
        ArgumentNullException.ThrowIfNull(reference);
        return Dispatcher.UIThread.InvokeAsync<Task>(() => OpenExistingManualProjectCoreAsync(reference, _token)).GetTask().Unwrap();
    }

    private async Task OpenExistingManualProjectCoreAsync(DeveloperProjectReference reference, CancellationToken caller,
        DeveloperProjectTemplateMaterialization? template = null)
    {
        Dispatcher.UIThread.VerifyAccess();
        DemandAlive();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(caller, _token);
        var token = linked.Token;
        await Borrowed.RevalidateOriginalAppContentAsync(_request.ProfileId, token);
        var resolved = await ResolveAsync(reference, token);
        DemandAlive(); token.ThrowIfCancellationRequested();
        await Borrowed.RevalidateOriginalAppContentAsync(_request.ProfileId, token);
        await AcquireManualWorkbenchAsync(resolved, token, template);
        if (_landing is { } landing) landing.Deactivate();
    }

    /// <summary>Acquires the SAME compiled-CUI workbench in manual mode for the exact resolved saved
    /// project. Every acquired candidate (including a failed activation) is retained and drained at
    /// close; the published live page is set only after successful activation and swap.</summary>
    private async Task AcquireManualWorkbenchAsync(DeveloperResolvedProject resolved, CancellationToken token,
        DeveloperProjectTemplateMaterialization? template = null)
    {
        var readiness = Borrowed.BorrowOriginalHomeReadiness();
        DeveloperProjectWorkbenchPage? retained = null;
        var page = _factory.CreateManual(resolved,
            p => new BuildWorkbenchReadiness(p, readiness),
            actual =>
            {
                if (retained is not null && !ReferenceEquals(retained, actual))
                    throw new InvalidOperationException("The Build manual workbench factory acquired multiple different partial pages.");
                retained = actual; RetainWorkbench(actual);
            },
            token);
        if (!ReferenceEquals(retained, page))
            throw new InvalidOperationException("The SAME Build manual workbench page was not retained before publication.");
        await page.ActivateAsync(token);
        if (template is not null)
        {
            try { await page.CreateTemplateProjectFilesAsync(template, token); }
            catch (OperationCanceledException) { throw; }
            catch (Exception) when (page.HasRetainedTemplateImportFailure)
            {
                // Keep the UI open on the same saved project so its exact partial CREATE result
                // is visible. The page's retained original import task owns and surfaces this
                // exception again during close/drain; unretained startup errors propagate.
            }
        }
        await Borrowed.RevalidateOriginalAppContentAsync(_request.ProfileId, token);
        DemandAlive(); token.ThrowIfCancellationRequested();
        _view.Child = page;
        _workbenchMode = true;
        lock (_gate) _page = page;
        _published = true;
        CloudflareOriginalExecutionGuard.InvokeOriginal(this, () => { StateChanged?.Invoke(this, EventArgs.Empty); return true; });
    }

    internal Task OpenExistingManualTemplateProjectAsync(DeveloperProjectReference reference,
        DeveloperProjectTemplateMaterialization materialization)
    {
        DemandAlive(); ArgumentNullException.ThrowIfNull(reference); ArgumentNullException.ThrowIfNull(materialization);
        return Dispatcher.UIThread.InvokeAsync<Task>(() =>
            OpenExistingManualProjectCoreAsync(reference, _token, materialization)).GetTask().Unwrap();
    }

    /// <summary>Genuine host Spaces navigation for the saved-reference picker's Open Space action.
    /// The <paramref name="settlement"/> is the picker's ACTUAL admitted original action settlement
    /// (completed only when the real open action resolves), passed UNCHANGED to the host driver,
    /// which awaits it. This action returns immediately after the host accepts and never awaits its
    /// own encompassing navigation settlement.</summary>
    private Task OpenExistingSpaceAsync(Guid spaceId, Task settlement)
    {
        DemandAlive();
        if (spaceId == Guid.Empty) throw new ArgumentException("A real Space id is required.");
        ArgumentNullException.ThrowIfNull(settlement);
        return Dispatcher.UIThread.InvokeAsync<Task>(() => OpenExistingSpaceCoreAsync(spaceId, settlement, _token)).GetTask().Unwrap();
    }

    private async Task OpenExistingSpaceCoreAsync(Guid spaceId, Task settlement, CancellationToken caller)
    {
        Dispatcher.UIThread.VerifyAccess();
        DemandAlive();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(caller, _token);
        var token = linked.Token;
        await Borrowed.RevalidateOriginalAppContentAsync(_request.ProfileId, token);
        token.ThrowIfCancellationRequested();
        if (_queueNavigation is null)
            throw new InvalidOperationException("The current host cannot open a Space from the Build landing.");
        var address = AppContentAddress.Parse("9to1://spaces/selected/" + spaceId.ToString("D"));
        // The settlement is the genuine originating open action's resolution, never a synthesized
        // completed task. The host driver retains and awaits it; we only forward it unchanged.
        bool accepted;
        try { accepted = _queueNavigation(new BrowseNavigationIntent(_request.TabId, address, OpenInNewTab: true), settlement); }
        catch { throw; }
        if (!accepted)
            throw new InvalidOperationException("The host could not accept navigation to that Space.");
        DemandAlive();
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

        // Settle the genuine admission ORIGINAL and the landing's original actions first, then drain
        // EVERY acquired workbench owner (published plus failed candidates) and re-scan after joining
        // initialization for late acquisitions. Borrowed global services stay alive.
        await Join(_admission);
        if (_landing is { } landing)
        {
            Task close; try { close = landing.CloseAndDrainAsync(); } catch (Exception error) { failures.Add(error); close = Task.CompletedTask; }
            await Join(close);
        }
        await DrainWorkbenchesAsync(failures);
        // Independently settle a faulted/canceled initialization, preserving its original errors, and
        // re-scan for workbenches acquired during settlement (final acquisition fence).
        await Join(OriginalInitialization);
        await DrainWorkbenchesAsync(failures);
        try { var close = Borrowed.CloseAndDrainAsync(); await Join(close); }
        catch (Exception error) { failures.Add(error); }
        try { _lifetime.Dispose(); } catch (Exception error) { failures.Add(error); }
        if (failures.Count > 0) throw new AggregateException("The Build presentation did not retire cleanly.", failures);
    }

    /// <summary>Drains every acquired workbench (published live page plus retained candidates) exactly
    /// once. Idempotent per page: each page's close task is joined on first encounter and the page is
    /// marked drained so a re-scan never double-joins it.</summary>
    private async Task DrainWorkbenchesAsync(List<Exception> failures)
    {
        List<DeveloperProjectWorkbenchPage> drained = [];
        while (true)
        {
            DeveloperProjectWorkbenchPage[] batch;
            lock (_gate) batch = _workbenches.Where(page => !drained.Any(done => ReferenceEquals(done, page))).ToArray();
            if (batch.Length == 0) break;
            foreach (var page in batch)
            {
                Task close; try { close = page.CloseAndDrainAsync(); } catch (Exception error) { failures.Add(error); close = Task.CompletedTask; }
                await JoinClose(failures, close);
                drained.Add(page);
            }
        }
    }

    private static async Task JoinClose(List<Exception> failures, Task close)
    {
        try { await close; }
        catch (Exception error) { if (!failures.Any(cause => ReferenceEquals(cause, error))) failures.Add(error); }
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
