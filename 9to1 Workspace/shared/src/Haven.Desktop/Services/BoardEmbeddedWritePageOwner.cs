using Avalonia.Controls;
using Avalonia.Threading;
using CakeOS.Apps.Boards.App;
using CakeOS.Cui.Runtime;
using CakeOS.Cui.Themes;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.Events;
using Haven.Desktop.Views.Pages.Home;
using Haven.Desktop.Views.Pages.Write;
using Haven.Infrastructure;
using HavenOS.Home;
using HavenOS.Home.Core;
using HavenOS.Home.NativeUI;
using HavenOS.Home.PermissionsTrustNotifications;
using Microsoft.Extensions.DependencyInjection;
using System.Runtime.ExceptionServices;

namespace Haven.Desktop.Services;

/// <summary>Home-owned bridge to the existing Write page and Notes repository. Embedded
/// selection carries metadata only; opening and every save remain under the original Home/Notes owner.</summary>
internal sealed class BoardEmbeddedWritePageOwner : IBoardsEmbeddedArtifactHost, IAsyncDisposable
{
    private readonly IServiceProvider _provider;
    private readonly HomeNativeWindowsComposition _home;
    private readonly string _profileId;
    private readonly Guid _tabId;
    private readonly string _sessionId;
    private readonly CancellationToken _appLifetime;
    private readonly CancellationToken _tabLifetime;
    private readonly Func<bool> _tabIsCurrent;
    private readonly CuiThemePreferenceContext? _theme;
    private readonly CloudflareOriginalTaskLedger _retainedSources = new();
    private readonly object _gate = new();
    private readonly List<Task> _operations = [];
    private readonly List<EmbeddedWriteSession> _sessions = [];
    private readonly List<OpeningCleanup> _openingCleanups = [];
    private Control? _owningControl;
    private AuthenticatedResourceActor? _actor;
    private CuiSceneAvailability? _observed;
    private bool _retiring;
    private Task? _close;

    internal BoardEmbeddedWritePageOwner(IServiceProvider provider, HomeNativeWindowsComposition home,
        string profileId, Guid tabId, CancellationToken appLifetime, CancellationToken tabLifetime,
        Func<bool> tabIsCurrent, CuiThemePreferenceContext? theme = null)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(home);
        ArgumentException.ThrowIfNullOrWhiteSpace(profileId);
        ArgumentNullException.ThrowIfNull(tabIsCurrent);
        if (tabId == Guid.Empty || !appLifetime.CanBeCanceled || !tabLifetime.CanBeCanceled)
            throw new ArgumentException("Retain the actual Boards tab, App and host lifetimes.");
        WindowsHomeSameProcessRuntimeObservation.DemandOriginalBinding(provider, home);
        _provider = provider; _home = home; _profileId = profileId; _tabId = tabId;
        _sessionId = "boards-tab:" + tabId.ToString("D"); _appLifetime = appLifetime;
        _tabLifetime = tabLifetime; _tabIsCurrent = tabIsCurrent; _theme = theme;
        _retainedSources.BindOriginalOwner(this);
    }

    public void BindOwningControl(Control control)
    {
        ArgumentNullException.ThrowIfNull(control);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_retiring || _close is not null, this);
            if (_owningControl is not null && !ReferenceEquals(_owningControl, control))
                throw new InvalidOperationException("Bind the actual composed Boards root once.");
            _owningControl = control;
        }
    }

    public Task<IBoardsEmbeddedArtifactSession> OpenAsync(BoardEmbeddedArtifactReference reference,
        BoardEmbeddedArtifactMode mode, CancellationToken ct = default) => RunAsync(async (originals, token) =>
    {
        ArgumentNullException.ThrowIfNull(reference);
        reference.Validate();
        if (reference.ApplicationId != "write" || reference.ExpectedRevision.Sequence is not { } expectedVersion ||
            expectedVersion < 1 || reference.ExpectedRevision.VersionId is not null || !Enum.IsDefined(mode))
            throw new UnauthorizedAccessException("Only a canonical Write document with its exact Notes version can be embedded.");
        await RevalidateAsync(originals, token).ConfigureAwait(false);
        var notes = _provider.GetRequiredService<NotesRepository>();
        var verified = _provider.GetRequiredService<VerifiedNotesRepository>();
        var admission = _provider.GetRequiredService<HomeNotesResourceAdmissionSource>();
        var approvals = _provider.GetRequiredService<HomeNativeApprovalWindowOwner>();
        DemandOriginalOwners(notes, verified, admission, approvals);

        var lifetime = CancellationTokenSource.CreateLinkedTokenSource(_appLifetime, _tabLifetime, token);
        NotesOriginalWriteWorkspaceRepository? repository = null;
        WritePage? page = null;
        try
        {
            repository = NotesOriginalWriteWorkspaceRepository.BindOriginal(notes, verified, admission,
                reference.ArtifactId, expectedVersion, _sessionId,
                (review, reviewToken) => PresentReviewAsync(approvals, review.Authorization, reviewToken),
                (review, reviewToken) => PresentReviewAsync(approvals, review.Authorization, reviewToken),
                OriginalScope, RetainOriginalTask, lifetime.Token, allowWrites: mode == BoardEmbeddedArtifactMode.Edit);
            var openedPage = await CreateWritePageAsync(repository, reference.ArtifactId, token).ConfigureAwait(false);
            page = openedPage;
            await AwaitOnUiAsync(() => openedPage.InitializeAsync(token)).ConfigureAwait(false);
            if (openedPage.Document is not { } opened || opened.Id != reference.ArtifactId || opened.Version != expectedVersion)
                throw new IOException("The selected Write document changed from its pinned Notes version.");
            await RevalidateAsync(originals, token).ConfigureAwait(false);
            await Dispatcher.UIThread.InvokeAsync(() => openedPage.SetEmbeddedReadOnly(mode == BoardEmbeddedArtifactMode.View)).GetTask().ConfigureAwait(false);
            DemandOwnerCurrent();
            var session = new EmbeddedWriteSession(this, openedPage, repository, reference, lifetime);
            lock (_gate) _sessions.Add(session);
            page = null; repository = null; lifetime = null!;
            return (IBoardsEmbeddedArtifactSession)session;
        }
        catch (Exception openError)
        {
            var cleanup = new OpeningCleanup(page, repository, lifetime);
            lock (_gate) _openingCleanups.Add(cleanup);
            page = null; repository = null; lifetime = null!;
            try
            {
                await cleanup.DrainAsync().ConfigureAwait(false);
                lock (_gate) _openingCleanups.Remove(cleanup);
            }
            catch (Exception cleanupError)
            {
                throw new AggregateException("The embedded Write open failed and its original page/repository cleanup remains retained.",
                    openError, cleanupError);
            }
            ExceptionDispatchInfo.Capture(openError).Throw();
            throw;
        }
    }, ct);

    public Task<BoardEmbeddedArtifactReference?> SelectAsync(string applicationId, CancellationToken ct = default) =>
        RunAsync(async (originals, token) =>
        {
            if (applicationId != "write") throw new NotSupportedException("The native selector is currently backed only by Write's existing Home Library source.");
            var actor = await RevalidateAsync(originals, token).ConfigureAwait(false);
            var notes = _provider.GetRequiredService<NotesRepository>();
            var verified = _provider.GetRequiredService<VerifiedNotesRepository>();
            var admission = _provider.GetRequiredService<HomeNotesResourceAdmissionSource>();
            var approvals = _provider.GetRequiredService<HomeNativeApprovalWindowOwner>();
            DemandOriginalOwners(notes, verified, admission, approvals);
            HomeLibraryDestinationPage? composedPage = null;
            Window? composedDialog = null;
            var composition = await Dispatcher.UIThread.InvokeAsync(() =>
            {
                var source = new NativeHomeArtifactLibrarySource(_home, notes, verified, admission, actor,
                    _sessionId + ":write-library-picker", (authorization, reviewToken) => PresentReviewAsync(approvals, authorization, reviewToken),
                    reviewToken => RevalidateAsync(originals, reviewToken), ParentScope, IsAlive, writeSelectionMode: true);
                composedPage = new HomeLibraryDestinationPage(router => source.CreateService(router),
                    reviewToken => RevalidateAsync(originals, reviewToken), (_, _) => false, source, _theme,
                    selectArtifact: (selected, selectToken) => source.SelectWriteDocumentAsync(selected, selectToken));
                composedDialog = new Window
                {
                    Title = "Select a Write document",
                    Width = 900,
                    Height = 720,
                    MinWidth = 640,
                    MinHeight = 480,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                    Content = composedPage
                };
                return (Page: composedPage, Dialog: composedDialog, Owner: GetActualOwnerWindow());
            }).GetTask().ConfigureAwait(false);
            var page = composition.Page ?? throw new InvalidOperationException("The Home Library selector page was not constructed.");
            var dialog = composition.Dialog ?? throw new InvalidOperationException("The Home Library selector window was not constructed.");
            var windowOwner = composition.Owner;
            var shown = false;
            Task? dialogTask = null;
            try
            {
                var activationDispatch = Dispatcher.UIThread.InvokeAsync<Task>(() =>
                {
                    dialogTask = dialog.ShowDialog(windowOwner);
                    shown = true;
                    return page.ActivateAsync(token);
                }).GetTask();
                var activation = await activationDispatch.ConfigureAwait(false);
                await activation.ConfigureAwait(false);
                var selectedTask = page.Selection;
                var completed = await Task.WhenAny(selectedTask, dialogTask!).WaitAsync(token).ConfigureAwait(false);
                if (!ReferenceEquals(completed, selectedTask))
                {
                    await page.CloseAndDrainAsync().ConfigureAwait(false);
                    return null;
                }
                var selected = await selectedTask.ConfigureAwait(false);
                await RevalidateAsync(originals, token).ConfigureAwait(false);
                return selected;
            }
            finally
            {
                await Dispatcher.UIThread.InvokeAsync(page.Deactivate).GetTask().ConfigureAwait(false);
                if (shown)
                    await Dispatcher.UIThread.InvokeAsync(() => dialog.Close()).GetTask().ConfigureAwait(false);
                if (dialogTask is not null)
                    try { await dialogTask.ConfigureAwait(false); } catch (OperationCanceledException) when (token.IsCancellationRequested) { }
                await page.CloseAndDrainAsync().ConfigureAwait(false);
            }
        }, ct);

    public Task<BoardEmbeddedArtifactReference?> CreateAsync(string applicationId, CancellationToken ct = default) =>
        RunAsync<BoardEmbeddedArtifactReference?>(async (originals, token) =>
        {
            if (applicationId != "write") throw new NotSupportedException("The original Notes creation owner currently creates Write documents only.");
            await RevalidateAsync(originals, token).ConfigureAwait(false);
            var notes = _provider.GetRequiredService<NotesRepository>();
            var verified = _provider.GetRequiredService<VerifiedNotesRepository>();
            var admission = _provider.GetRequiredService<HomeNotesResourceAdmissionSource>();
            var approvals = _provider.GetRequiredService<HomeNativeApprovalWindowOwner>();
            DemandOriginalOwners(notes, verified, admission, approvals);
            var document = NotesDocument.Create("Untitled document");
            await using var repository = NotesOriginalWriteWorkspaceRepository.BindOriginal(notes, verified, admission,
                document.Id, document.Version, _sessionId + ":write-create",
                (_, _) => Task.CompletedTask,
                (review, reviewToken) => PresentReviewAsync(approvals, review.Authorization, reviewToken),
                OriginalScope, RetainOriginalTask, _appLifetime, allowWrites: true);
            var receipt = await repository.SaveAsync(document, "Write document created", token).ConfigureAwait(false);
            await RevalidateAsync(originals, token).ConfigureAwait(false);
            var created = new BoardEmbeddedArtifactReference("write", receipt.DocumentId,
                new BoardEmbeddedArtifactRevision(receipt.Version, null));
            created.Validate();
            return created;
        }, ct);

    public async ValueTask DisposeAsync()
    {
        CloudflareOriginalExecutionGuard.DemandExternalJoin(this);
        Task close;
        lock (_gate)
        {
            if (_close is not null) { close = _close; }
            else
            {
                _retiring = true;
                close = _close = CloseCoreAsync();
            }
        }
        try { await close.ConfigureAwait(false); }
        catch
        {
            lock (_gate)
                if (ReferenceEquals(_close, close)) { _close = null; _retiring = false; }
            throw;
        }
    }

    private async Task<WritePage> CreateWritePageAsync(NotesOriginalWriteWorkspaceRepository repository,
        Guid documentId, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        return await Dispatcher.UIThread.InvokeAsync(() => new WritePage(_home.Productivity.Engine,
            _provider.GetRequiredService<HavenEventBus>(), repository,
            _provider.GetRequiredService<INotesImportExportService>(),
            _provider.GetService<INotesAttachmentStore>(), documentId,
            _provider.GetService<INotesAiService>(), _provider.GetService<IOllamaClient>(),
            _provider.GetService<NotesReadAloudController>(), _provider.GetService<IWriteNativeDocumentPackageStore>())).GetTask().ConfigureAwait(false);
    }

    private async Task<AuthenticatedResourceActor> RevalidateAsync(CloudflareOriginalTaskLedger originals,
        CancellationToken token)
    {
        DemandOwnerCurrent();
        var availability = await originals.AwaitAsync(WindowsHomeSameProcessRuntimeObservation.CheckAsync(
            _provider, _home, _appLifetime, _tabLifetime,
            body => originals.Invoke(() => { body(); return true; }), DemandOwnerCurrent, token)).ConfigureAwait(false);
        if (availability.State != CuiSceneAvailabilityState.Ready)
            throw new UnauthorizedAccessException(availability.Message);
        var actual = await originals.AwaitAsync(originals.Invoke(() =>
            _home.Profiles.GetCurrentAsync(token).AsTask())).ConfigureAwait(false)
            ?? throw new UnauthorizedAccessException("The original Home local profile is unavailable.");
        if (actual.ProfileId != _profileId || actual.AccountId is not null || actual.OrganisationId is not null ||
            string.IsNullOrWhiteSpace(actual.ActorId) || string.IsNullOrWhiteSpace(actual.AuthenticationRevision))
            throw new UnauthorizedAccessException("The embedded Write page no longer belongs to the current Home profile.");
        lock (_gate)
        {
            if (_actor is not null && _actor != actual)
                throw new UnauthorizedAccessException("The original Home actor changed; reopen the Boards tab.");
            _actor ??= actual;
            _observed = WindowsHomeSameProcessRuntimeObservation.RevalidateBeforePublication(_provider, _home, availability);
        }
        DemandOwnerCurrent();
        return actual;
    }

    private async Task PresentReviewAsync(HomeNativeApprovalWindowOwner approvals,
        HomePermissionAuthorization authorization, CancellationToken token)
    {
        var dispatch = await Dispatcher.UIThread.InvokeAsync<Task<HomeNativeApprovalDecisionObservation>>(
            () => approvals.OpenAndObserveDecisionOriginalAsync(authorization.RequestId, token)).GetTask().ConfigureAwait(false);
        var decision = await dispatch.ConfigureAwait(false);
        if (decision.RequestId != authorization.RequestId || !decision.DecisionRecorded ||
            decision.State != HomePermissionRequestState.Approved)
            throw new UnauthorizedAccessException(decision.Message);
    }

    private void DemandOriginalOwners(NotesRepository notes, VerifiedNotesRepository verified, HomeNotesResourceAdmissionSource admission,
        HomeNativeApprovalWindowOwner approvals)
    {
        WindowsHomeSameProcessRuntimeObservation.DemandOriginalBinding(_provider, _home);
        if (!admission.IsBoundToOriginalNotes(notes) ||
            !ReferenceEquals(_home.Services.GetService(typeof(NotesRepository)), notes) ||
            !ReferenceEquals(_home.Services.GetService(typeof(VerifiedNotesRepository)), verified) ||
            !ReferenceEquals(_home.Services.GetService(typeof(HomeNotesResourceAdmissionSource)), admission) ||
            !approvals.IsBoundToOriginalComposition(_home))
            throw new UnauthorizedAccessException("Retain the SAME original Home, Notes, review and Write admission owners.");
    }

    private Window GetActualOwnerWindow()
    {
        Control? control;
        lock (_gate) control = _owningControl;
        if (control is null || TopLevel.GetTopLevel(control) is not Window owner || !owner.IsVisible || !IsAlive())
            throw new UnauthorizedAccessException("The current Boards root is not attached to its owning Home window.");
        return owner;
    }

    private void ParentScope(Action body) => CloudflareOriginalExecutionGuard.InvokeOriginal(this, () =>
    { DemandOwnerCurrent(); body(); return true; });
    private void OriginalScope(Action body)
    {
        ArgumentNullException.ThrowIfNull(body);
        CloudflareOriginalExecutionGuard.InvokeOriginal(this, () => { DemandOwnerCurrent(); body(); return true; });
    }

    private static async Task AwaitOnUiAsync(Func<Task> action)
    {
        var dispatch = Dispatcher.UIThread.InvokeAsync<Task>(action).GetTask();
        var actual = await dispatch.ConfigureAwait(false);
        await actual.ConfigureAwait(false);
    }

    private static async Task<T> AwaitOnUiAsync<T>(Func<Task<T>> action)
    {
        var dispatch = Dispatcher.UIThread.InvokeAsync<Task<T>>(action).GetTask();
        var actual = await dispatch.ConfigureAwait(false);
        return await actual.ConfigureAwait(false);
    }
    private void RetainOriginalTask(Task actual)
    {
        ArgumentNullException.ThrowIfNull(actual);
        lock (_gate)
        {
            _retainedSources.ReleaseSuccessfullyObservedOriginals();
            if (_retainedSources.OriginalTasks.Count >= 512)
                throw new InvalidOperationException("Join original embedded Write Home tasks before admitting more source work.");
            _ = _retainedSources.Track(actual);
        }
    }

    private bool IsAlive() => !_appLifetime.IsCancellationRequested && !_tabLifetime.IsCancellationRequested && _tabIsCurrent();

    private void DemandOwnerCurrent()
    {
        _appLifetime.ThrowIfCancellationRequested(); _tabLifetime.ThrowIfCancellationRequested();
        if (!IsAlive() || _home.OriginalCloseTask is not null || _home.OriginalProcessRetirementRequestTask is not null)
            throw new ObjectDisposedException(nameof(BoardEmbeddedWritePageOwner));
        WindowsHomeSameProcessRuntimeObservation.DemandOriginalBinding(_provider, _home);
    }

    private bool IsCurrentPublication()
    {
        if (!IsAlive()) return false;
        CuiSceneAvailability? observed;
        lock (_gate) observed = _observed;
        if (observed is not { State: CuiSceneAvailabilityState.Ready } current) return false;
        try
        {
            return WindowsHomeSameProcessRuntimeObservation.RevalidateBeforePublication(_provider, _home, current).State ==
                CuiSceneAvailabilityState.Ready;
        }
        catch { return false; }
    }

    private Task<T> RunAsync<T>(Func<CloudflareOriginalTaskLedger, CancellationToken, Task<T>> body,
        CancellationToken caller)
    {
        ArgumentNullException.ThrowIfNull(body);
        caller.ThrowIfCancellationRequested();
        CloudflareOriginalTaskLedger originals;
        Task<T> actual;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_retiring || _close is not null, this);
            if (_operations.Count >= 128) throw new InvalidOperationException("Settle original embedded Write operations before retrying.");
            originals = new(); originals.BindOriginalOwner(this);
            originals.BindOriginalCallerCallback(action => CloudflareOriginalExecutionGuard.InvokeOriginal(this,
                () => { DemandOwnerCurrent(); action(); return true; }));
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            actual = Run(start.Task, originals); _operations.Add(actual); start.SetResult();
        }
        return actual;

        async Task<T> Run(Task start, CloudflareOriginalTaskLedger ledger)
        {
            await start.ConfigureAwait(false);
            using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(caller, _appLifetime, _tabLifetime);
            using var phase = CloudflareOriginalExecutionGuard.EnterOriginal(this);
            try
            {
                DemandOwnerCurrent();
                return await ledger.RunToOriginalSettlementAsync(() => body(ledger, lifetime.Token)).ConfigureAwait(false);
            }
            finally
            {
                lock (_gate) _operations.RemoveAll(task => task.IsCompletedSuccessfully);
            }
        }
    }

    private async Task CloseCoreAsync()
    {
        Task[] operations;
        lock (_gate) operations = _operations.ToArray();
        var failures = new List<Exception>();
        foreach (var operation in operations)
            try { await operation.ConfigureAwait(false); } catch (Exception error) { failures.Add(operation.Exception ?? error); }
        EmbeddedWriteSession[] sessions;
        lock (_gate) sessions = _sessions.ToArray();
        foreach (var session in sessions)
        {
            try
            {
                if (!await session.TryPrepareToCloseAsync(CancellationToken.None).ConfigureAwait(false))
                    throw new InvalidOperationException("An embedded Write page refused close; preserve its draft and keep the host alive.");
                await session.CloseAndDrainAsync().ConfigureAwait(false);
            }
            catch (Exception error) { failures.Add(error); }
        }
        OpeningCleanup[] openingCleanups;
        lock (_gate) openingCleanups = _openingCleanups.ToArray();
        foreach (var cleanup in openingCleanups)
        {
            try
            {
                await cleanup.DrainAsync().ConfigureAwait(false);
                lock (_gate) _openingCleanups.Remove(cleanup);
            }
            catch (Exception error) { failures.Add(error); }
        }
        try { await _retainedSources.ObserveAllOriginalTasksAsync().ConfigureAwait(false); }
        catch (Exception error) { failures.Add(error); }
        failures.AddRange(_retainedSources.OriginalErrors);
        if (failures.Count > 0) throw new AggregateException("Original embedded Write owner work did not settle cleanly.", failures);
    }

    private void Remove(EmbeddedWriteSession session)
    { lock (_gate) _sessions.Remove(session); }

    private sealed class OpeningCleanup(WritePage? page, NotesOriginalWriteWorkspaceRepository? repository,
        CancellationTokenSource lifetime)
    {
        private WritePage? _page = page;
        private NotesOriginalWriteWorkspaceRepository? _repository = repository;
        private CancellationTokenSource? _lifetime = lifetime;
        private bool _pageDrained;
        private readonly SemaphoreSlim _gate = new(1, 1);

        public async Task DrainAsync()
        {
            await _gate.WaitAsync().ConfigureAwait(false);
            try
            {
                if (!_pageDrained && _page is not null)
                {
                    var page = _page;
                    await AwaitOnUiAsync(() => page.CloseAndDrainAsync()).ConfigureAwait(false);
                    _pageDrained = true;
                    _page = null;
                }
                if (_repository is not null)
                {
                    await _repository.DisposeAsync().ConfigureAwait(false);
                    _repository = null;
                }
                if (_lifetime is not null)
                {
                    _lifetime.Cancel();
                    _lifetime.Dispose();
                    _lifetime = null;
                }
            }
            finally { _gate.Release(); }
        }
    }

    private sealed class EmbeddedWriteSession(BoardEmbeddedWritePageOwner owner, WritePage page,
        NotesOriginalWriteWorkspaceRepository repository, BoardEmbeddedArtifactReference openedReference,
        CancellationTokenSource lifetime) : IBoardsEmbeddedArtifactSession
    {
        private readonly object _sync = new();
        private bool _prepared, _closed;
        private bool _pageDrained, _repositoryDisposed, _lifetimeDisposed;
        private Task? _close;

        public Control Content => page;
        public BoardEmbeddedArtifactReference CurrentReference
        {
            get
            {
                owner.DemandOwnerCurrent();
                if (page.Document is not { } current || current.Id != openedReference.ArtifactId || current.Version < 1)
                    throw new UnauthorizedAccessException("The original Write page lost its selected Notes identity.");
                return new("write", current.Id, new BoardEmbeddedArtifactRevision(current.Version, null));
            }
        }
        public bool IsPublicationCurrent
        {
            get
            {
                lock (_sync) if (_closed) return false;
                try { return owner.IsCurrentPublication() && !lifetime.IsCancellationRequested && page.Document?.Id == openedReference.ArtifactId; }
                catch { return false; }
            }
        }

        public async Task<bool> TryPrepareToCloseAsync(CancellationToken ct = default)
        {
            lock (_sync) { if (_closed) return true; _prepared = false; }
            if (!IsPublicationCurrent) return false;
            var prepared = await AwaitOnUiAsync(() => page.PrepareToCloseAsync(
                "Save embedded Write page before closing", ct)).ConfigureAwait(false);
            lock (_sync) _prepared = prepared;
            return prepared;
        }

        public Task CloseAndDrainAsync()
        {
            CloudflareOriginalExecutionGuard.DemandExternalJoin(this);
            lock (_sync)
            {
                if (_close is not null) return _close;
                if (_closed) return Task.CompletedTask;
                if (!_prepared) throw new InvalidOperationException("The embedded Write close gate must accept before draining its owner.");
                return _close = CloseCoreAsync();
            }
        }

        private async Task CloseCoreAsync()
        {
            try
            {
                if (!_pageDrained)
                {
                    await AwaitOnUiAsync(() => page.CloseAndDrainAsync()).ConfigureAwait(false);
                    _pageDrained = true;
                }
                if (!_repositoryDisposed)
                {
                    await repository.DisposeAsync().ConfigureAwait(false);
                    _repositoryDisposed = true;
                }
                if (!_lifetimeDisposed)
                {
                    lifetime.Cancel();
                    lifetime.Dispose();
                    _lifetimeDisposed = true;
                }
                lock (_sync) _closed = true;
                owner.Remove(this);
            }
            catch
            {
                lock (_sync) _close = null;
                throw;
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (!await TryPrepareToCloseAsync().ConfigureAwait(false))
                throw new InvalidOperationException("The embedded Write page refused disposal; preserve its draft.");
            await CloseAndDrainAsync().ConfigureAwait(false);
        }
    }
}
