using Avalonia.Controls;
using Avalonia.Threading;
using CakeOS.Apps.Boards.App;
using System.ComponentModel;
using Haven.Application;
using Haven.Core;
using HavenOS.Apps.Browse;

namespace Haven.Desktop.Services;

/// <summary>An editable rich Boards surface whose only persistence action is the selected
/// Home Files revision callback. The exact file lease remains alive through tab retirement.</summary>
internal sealed class BoardsWritableRichDocumentBrowseAppContentSession : IBrowseNativeAppContentSession, IOriginalTabTransientPresentationOwner,
    IBrowseAppContentClosePreparationParticipant
{
    private OriginalTabTransientPresentation? _transientPresentation;
    public OriginalTabTransientPresentation TransientPresentation => _transientPresentation
        ?? throw new InvalidOperationException("The original tab presentation has not initialized.");
    private readonly BrowseAppContentRequest _request;
    private readonly BoardsBrowseAppContentAddress _address;
    private readonly CancellationTokenSource _lifetime;
    private readonly IBoardsEmbeddedArtifactHost? _embeddedHost;
    private readonly IBoardsHomeBoardCreationHost? _homeBoardCreationHost;
    private readonly IBoardsSectionIconGenerationHost? _sectionIconGenerationHost;
    private readonly object _gate = new();
    private NativeHomeFilesBoardWriteLease? _lease;
    private readonly Func<CancellationToken, Task<NativeHomeFilesBoardWriteLease>>? _restoreLease;
    private BoardsWritablePresentation? _presentation;
    private Task? _initialization, _close;
    private Task<IBrowseAppContentClosePreparation?>? _preparationTask;
    private ClosePreparation? _prepared;
    private IDisposable? _transientCloseReservation;
    private Task? _titleRefresh;
    private int _titleVersion;
    private string _title = "Boards — Files document";
    private volatile bool _published, _closing;
    private readonly BoardsLocalDocuments? _localDocuments;
    private readonly Func<Guid, CancellationToken, Task>? _openLocalDocument;
    private Guid _localDocumentId;

    internal BoardsWritableRichDocumentBrowseAppContentSession(BrowseAppContentRequest request,
        BoardsBrowseAppContentAddress address, BoardsLocalDocuments documents,
        Func<Guid, CancellationToken, Task> open, CancellationTokenSource lifetime, IBoardsEmbeddedArtifactHost? embeddedHost = null)
    {
        _request = request; _address = address; _localDocuments = documents;
        _openLocalDocument = open; _lifetime = lifetime; _embeddedHost = embeddedHost;
        if (address.IsFilesDocument || address.DocumentId is not null && !address.IsLocalDocument)
            throw new ArgumentException("A local Boards route is required.");
    }


    internal BoardsWritableRichDocumentBrowseAppContentSession(BrowseAppContentRequest request,
        BoardsBrowseAppContentAddress address, NativeHomeFilesBoardWriteLease lease,
        CancellationTokenSource lifetime, IBoardsEmbeddedArtifactHost? embeddedHost = null,
        IBoardsHomeBoardCreationHost? homeBoardCreationHost = null,
        IBoardsSectionIconGenerationHost? sectionIconGenerationHost = null)
    {
        _request = request ?? throw new ArgumentNullException(nameof(request));
        ArgumentNullException.ThrowIfNull(address);
        _address = address;
        _lease = lease ?? throw new ArgumentNullException(nameof(lease));
        _lifetime = lifetime ?? throw new ArgumentNullException(nameof(lifetime));
        _embeddedHost = embeddedHost;
        _homeBoardCreationHost = homeBoardCreationHost;
        _sectionIconGenerationHost = sectionIconGenerationHost;
        if (!address.IsFilesDocument || !address.Writable || address.StoreId != lease.StoreId ||
            address.ParentFolderId != lease.ParentFolderId || address.FileId != lease.FileId ||
            address.RevisionId != lease.RevisionId || !StringComparer.Ordinal.Equals(request.ProfileId, lease.ProfileId))
            throw new UnauthorizedAccessException("The prepared Home write lease does not match this canonical Boards address.");
    }

    internal BoardsWritableRichDocumentBrowseAppContentSession(BrowseAppContentRequest request,
        BoardsBrowseAppContentAddress address, Func<CancellationToken, Task<NativeHomeFilesBoardWriteLease>> restoreLease,
        CancellationTokenSource lifetime, IBoardsEmbeddedArtifactHost? embeddedHost = null,
        IBoardsHomeBoardCreationHost? homeBoardCreationHost = null,
        IBoardsSectionIconGenerationHost? sectionIconGenerationHost = null)
    {
        _request = request; _address = address; _restoreLease = restoreLease;
        _lifetime = lifetime; _embeddedHost = embeddedHost;
        _homeBoardCreationHost = homeBoardCreationHost; _sectionIconGenerationHost = sectionIconGenerationHost;
        if (!address.IsFilesDocument || address.ReadToken is not null || !address.Writable)
            throw new UnauthorizedAccessException("A restored write requires a durable Files identity without a prepared capability.");
    }

    public AppContentAddress Address => _published && _localDocumentId != Guid.Empty
        ? BoardsBrowseAppContentAddress.ForLocalDocument(_localDocumentId)
        : _published && _lease is { } lease
        ? _address.ForRestoration(lease.DocumentId, lease.RevisionId) : _request.Address;
    public string Title => _title;
    public string IconKey => "boards";
    public event EventHandler? StateChanged;
    public Task? OriginalInitialization { get { lock (_gate) return _initialization; } }
    public Task? OriginalClose { get { lock (_gate) return _close; } }
    public bool IsPublicationCurrent => _published && !_closing && !_lifetime.IsCancellationRequested &&
        (_localDocuments is not null || _lease?.IsOpen == true) && OriginalInitialization is { IsCompletedSuccessfully: true };
    public bool CanRetireOriginalView => OriginalClose is { IsCompletedSuccessfully: true };

    public Control OriginalNativeView
    {
        get
        {
            Dispatcher.UIThread.VerifyAccess();
            if (!IsPublicationCurrent) throw new InvalidOperationException("The writable rich Boards document has not completed Home admission.");
            return TransientPresentation;
        }
    }

    public Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (_initialization is not null) return _initialization;
            ObjectDisposedException.ThrowIf(_closing, this);
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _initialization = InitializeCoreAsync(start.Task, cancellationToken);
            start.SetResult();
            return _initialization;
        }
    }

    private async Task InitializeCoreAsync(Task start, CancellationToken caller)
    {
        await start.ConfigureAwait(false);
        using var phase = CloudflareOriginalExecutionGuard.EnterOriginal(this);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(caller, _lifetime.Token);
        linked.Token.ThrowIfCancellationRequested();
        if (_localDocuments is not null)
        {
            await InitializeLocalAsync(linked.Token).ConfigureAwait(false);
            return;
        }
        if (_lease is null)
            _lease = await (_restoreLease ?? throw new InvalidOperationException("The original Home Files write source is missing."))(linked.Token).ConfigureAwait(false);
        var lease = _lease ?? throw new InvalidOperationException("The fresh Home Files write returned no lease.");
        if (_address.RevisionId != lease.RevisionId)
            throw new UnauthorizedAccessException("The saved Files board revision changed. Reopen its current revision from Home Library.");
        if (!lease.IsOpen || _address.StoreId != lease.StoreId || _address.ParentFolderId != lease.ParentFolderId ||
            _address.FileId != lease.FileId ||
            _address.DocumentId is { } expectedDocument && expectedDocument != lease.DocumentId ||
            !StringComparer.Ordinal.Equals(_request.ProfileId, lease.ProfileId))
            throw new UnauthorizedAccessException("The freshly authorized Files board no longer matches the saved identity. Reopen it from Home Library.");
        await lease.DemandCurrentAsync(linked.Token).ConfigureAwait(false);
        var bytes = lease.Bytes;
        var dispatch = Dispatcher.UIThread.InvokeAsync<Task>(async () =>
        {
            Dispatcher.UIThread.VerifyAccess();
            using var uiPhase = CloudflareOriginalExecutionGuard.EnterOriginal(this);
            linked.Token.ThrowIfCancellationRequested();
            await lease.DemandCurrentAsync(linked.Token).ConfigureAwait(true);
            var presentation = await BoardsWritablePresentation.OpenWritableDocumentAsync(
                bytes, lease.SourceId, lease.RevisionId.ToString("D"), SaveAndPublishRevisionAsync, linked.Token, _embeddedHost,
                homeBoardCreationHost: _homeBoardCreationHost,
                sectionIconGenerationHost: _sectionIconGenerationHost);
            try
            {
                await lease.DemandCurrentAsync(linked.Token).ConfigureAwait(true);
                if (presentation.Document.DocumentId != lease.DocumentId)
                    throw new InvalidDataException("The writable Boards presentation changed the canonical document identity.");
                var current = await presentation.GetCurrentDocumentAsync(linked.Token).ConfigureAwait(true);
                _title = FormatTitle(current.RichNotes?.Title);
                presentation.ViewModel.PropertyChanged += OnViewModelPropertyChanged;
                _transientPresentation = new OriginalTabTransientPresentation();
                _transientPresentation.BindOriginal(presentation.Content);
                _presentation = presentation;
                _published = true;
                CloudflareOriginalExecutionGuard.InvokeOriginal(this, () => { StateChanged?.Invoke(this, EventArgs.Empty); return true; });
            }
            catch { await presentation.DisposeAsync(); throw; }
        }).GetTask();
        var actual = await dispatch.ConfigureAwait(false);
        await actual.ConfigureAwait(false);
    }

    private async Task InitializeLocalAsync(CancellationToken token)
    {
        var documents = _localDocuments!;
        // A generic Boards link names no document, so it must land on the splash itself: no board
        // reopened behind it (the old LatestOrCreateAsync picked the most recent board, or created a
        // brand-new empty one on every reload) and no board title in the tab.
        var splashOnly = _address.DocumentId is null && !_address.IsLocalDocument;
        BoardsWritablePresentation presentation;
        if (splashOnly)
        {
            presentation = await BoardsWritablePresentation.OpenLandingAsync(documents,
                (id, ct) => _openLocalDocument!(id, ct), token, _embeddedHost, _lifetime.Token).ConfigureAwait(false);
        }
        else
        {
            _localDocumentId = _address.DocumentId ?? await documents.LatestOrCreateAsync(token).ConfigureAwait(false);
            presentation = await BoardsWritablePresentation.OpenLocalDocumentAsync(documents, _localDocumentId,
                _openLocalDocument!, landing: !_address.IsLocalDocument, token: token,
                embeddedArtifactHost: _embeddedHost, lifetimeToken: _lifetime.Token).ConfigureAwait(false);
        }
        try
        {
            var dispatch = Dispatcher.UIThread.InvokeAsync(() =>
            {
                token.ThrowIfCancellationRequested();
                _title = splashOnly ? "Boards" : FormatTitle(presentation.ViewModel.Get("BoardTitle")?.ToString());
                presentation.ViewModel.PropertyChanged += OnViewModelPropertyChanged;
                _transientPresentation = new OriginalTabTransientPresentation();
                _transientPresentation.BindOriginal(presentation.Content);
                _presentation = presentation;
                _published = true;
                CloudflareOriginalExecutionGuard.InvokeOriginal(this, () => { StateChanged?.Invoke(this, EventArgs.Empty); return true; });
            }).GetTask();
            await dispatch.ConfigureAwait(false);
        }
        catch { await presentation.DisposeAsync().ConfigureAwait(false); throw; }
    }

    private async ValueTask<BoardsExternalSaveReceipt> SaveAndPublishRevisionAsync(string sourceId,
        string expectedRevision, ReadOnlyMemory<byte> document, CancellationToken token)
    {
        var lease = _lease ?? throw new InvalidOperationException("The current Home Files write lease is missing.");
        var result = await lease.SaveAsync(sourceId, expectedRevision, document, token).ConfigureAwait(false);
        if (result.Committed && !_closing && _published)
        {
            var dispatch = Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (!_closing && _published)
                    CloudflareOriginalExecutionGuard.InvokeOriginal(this, () => { StateChanged?.Invoke(this, EventArgs.Empty); return true; });
            }).GetTask();
            await dispatch.ConfigureAwait(false);
        }
        return result;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName != "BoardTitle" || _closing || _lifetime.IsCancellationRequested) return;
        lock (_gate)
        {
            _titleVersion++;
            if (_titleRefresh is null && _presentation is { } current)
                _titleRefresh = RefreshTitleLoopAsync(current, _lifetime.Token);
        }
    }

    private async Task RefreshTitleLoopAsync(BoardsWritablePresentation presentation, CancellationToken token)
    {
        await Task.Yield();
        while (true)
        {
            int version;
            lock (_gate) version = _titleVersion;
            var document = await presentation.GetCurrentDocumentAsync(token).ConfigureAwait(false);
            var title = FormatTitle(document.RichNotes?.Title);
            var dispatch = Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (_closing || !ReferenceEquals(_presentation, presentation)) return;
                _title = title;
                CloudflareOriginalExecutionGuard.InvokeOriginal(this, () => { StateChanged?.Invoke(this, EventArgs.Empty); return true; });
            });
            await dispatch.GetTask().ConfigureAwait(false);
            lock (_gate)
            {
                if (version != _titleVersion) continue;
                _titleRefresh = null;
                return;
            }
        }
    }

    private static string FormatTitle(string? title) => title is { Length: > 0 } ? title + " — Boards" : "Boards";

    public bool SupportsReversibleClosePreparation => true;

    public async Task<bool> TryPrepareToCloseAsync(CancellationToken cancellationToken = default)
    {
        var preparation = await PrepareCloseAsync(cancellationToken).ConfigureAwait(false);
        if (preparation is null) return false;
        await preparation.AbortAsync().ConfigureAwait(false);
        return true;
    }

    public Task<IBrowseAppContentClosePreparation?> PrepareCloseAsync(CancellationToken cancellationToken = default)
    {
        CloudflareOriginalExecutionGuard.DemandExternalJoin(this);
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (_close is not null || _closing) return Task.FromResult<IBrowseAppContentClosePreparation?>(null);
            if (_prepared is { } prepared) return Task.FromResult<IBrowseAppContentClosePreparation?>(prepared);
            if (_preparationTask is { IsCompleted: false }) return _preparationTask;
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _preparationTask = PrepareDriverAsync(start.Task, cancellationToken);
            start.SetResult();
            return _preparationTask;
        }
    }

    private async Task<IBrowseAppContentClosePreparation?> PrepareDriverAsync(Task start, CancellationToken token)
    {
        try { return await PrepareCoreAsync(start, token).ConfigureAwait(false); }
        finally
        {
            lock (_gate)
                if (_prepared is null) { _transientCloseReservation?.Dispose(); _transientCloseReservation = null; }
        }
    }
    private async Task<IBrowseAppContentClosePreparation?> PrepareCoreAsync(Task start, CancellationToken token)
    {
        await start.ConfigureAwait(false);
        // Stage-by-stage record for the Boards close owner. The app-wide close waits behind this
        // participant (it advertises reversible close preparation), so when a close stalls the
        // retained log now says which Boards stage it stalled in rather than nothing at all.
        App.WriteOriginalCloseDiagnostic("boards close: preparation begins");
        var initialization = OriginalInitialization;
        if (initialization is null) { App.WriteOriginalCloseDiagnostic("boards close: no original initialization; nothing to prepare"); return null; }
        try { await App.TimeOriginalCloseStageAsync("boards close: original initialization", initialization); }
        catch { App.WriteOriginalCloseDiagnostic("boards close: original initialization failed; failed-initialization preparation"); return CreateFailedInitializationPreparation(); }
        token.ThrowIfCancellationRequested();
        var presentation = _presentation;
        if (presentation is null) { App.WriteOriginalCloseDiagnostic("boards close: no presentation; failed-initialization preparation"); return CreateFailedInitializationPreparation(); }
        if (_localDocuments is not null)
            await App.TimeOriginalCloseStageAsync("boards close: document save", presentation.SaveAsync(token).AsTask()).ConfigureAwait(false);
        var dispatch = Dispatcher.UIThread.InvokeAsync<Task<IBoardsPresentationClosePreparation?>>(
            () =>
            {
                _transientCloseReservation = TransientPresentation.TryReserveClose();
                return _transientCloseReservation is null
                    ? Task.FromResult<IBoardsPresentationClosePreparation?>(null)
                    : presentation.PrepareToCloseAsync(token);
            }).GetTask();
        var actual = await App.TimeOriginalCloseStageAsync("boards close: dispatcher hand-off to the surface thread", dispatch).ConfigureAwait(false);
        var nested = await App.TimeOriginalCloseStageAsync("boards close: presentation close preparation", actual).ConfigureAwait(false);
        if (nested is null) { App.WriteOriginalCloseDiagnostic("boards close: the surface returned no close preparation (close reservation refused or already closing)"); return null; }
        if (token.IsCancellationRequested)
        {
            await AwaitOnUiAsync(nested.AbortAsync).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
        }
        var preparation = new ClosePreparation(this, presentation, nested);
        var accepted = false;
        lock (_gate)
        {
            if (!_closing && ReferenceEquals(_presentation, presentation) && nested.IsCurrent &&
                !presentation.HasUnsavedChanges)
            {
                _prepared = preparation;
                accepted = true;
            }
        }
        if (!accepted)
        {
            if (presentation.HasUnsavedChanges)
            {
                presentation.ViewModel.Set("StatusText", "Save this Files board revision before closing.");
                CloudflareOriginalExecutionGuard.InvokeOriginal(this, () => { StateChanged?.Invoke(this, EventArgs.Empty); return true; });
            }
            await AwaitOnUiAsync(nested.AbortAsync).ConfigureAwait(false);
            return null;
        }
        return preparation;
    }

    private IBrowseAppContentClosePreparation? CreateFailedInitializationPreparation()
    {
        var preparation = new ClosePreparation(this, null, null);
        lock (_gate)
        {
            if (_closing) return null;
            _prepared = preparation;
        }
        return preparation;
    }

    private Task BeginAcceptedClose(ClosePreparation preparation)
    {
        lock (_gate)
        {
            if (preparation.Accepted) return _close ?? throw new InvalidOperationException("The accepted Boards close is absent.");
            if (!preparation.IsCurrent) throw new InvalidOperationException("The original Boards close preparation is no longer current.");
            preparation.MarkAccepted();
            _closing = true;
            _transientCloseReservation?.Dispose(); _transientCloseReservation = null;
            _published = false;
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _close = CloseCoreAsync(start.Task);
            start.SetResult();
            return _close;
        }
    }

    private sealed class ClosePreparation(BoardsWritableRichDocumentBrowseAppContentSession owner,
        BoardsWritablePresentation? presentation, IBoardsPresentationClosePreparation? nested)
        : IBrowseAppContentClosePreparation
    {
        private bool _accepted, _aborted;
        private Task? _abort;
        internal bool Accepted => _accepted;
        internal IBoardsPresentationClosePreparation? Nested => nested;
        public IBrowseAppContentSession Session => owner;
        public bool IsCurrent => !_accepted && !_aborted && !owner._closing && !owner._lifetime.IsCancellationRequested &&
            ReferenceEquals(owner._prepared, this) && (nested is null || nested.IsCurrent) &&
            (presentation is null || ReferenceEquals(owner._presentation, presentation));

        public Task AbortAsync()
        {
            CloudflareOriginalExecutionGuard.DemandExternalJoin(owner);
            lock (owner._gate)
            {
                if (_abort is not null) return _abort;
                if (_accepted) return Task.FromException(new InvalidOperationException("An accepted Boards close cannot be aborted."));
                if (_aborted) return Task.CompletedTask;
                return _abort = AbortCoreAsync();
            }
        }

        private async Task AbortCoreAsync()
        {
            if (nested is not null) await AwaitOnUiAsync(nested.AbortAsync).ConfigureAwait(false);
            lock (owner._gate)
            {
                if (!IsCurrent && !ReferenceEquals(owner._prepared, this))
                    throw new InvalidOperationException("The original Boards preparation is no longer current.");
                if (ReferenceEquals(owner._prepared, this))
                {
                    owner._prepared = null;
                    owner._transientCloseReservation?.Dispose(); owner._transientCloseReservation = null;
                }
                _aborted = true;
            }
        }

        public Task BeginClose()
        {
            CloudflareOriginalExecutionGuard.DemandExternalJoin(owner);
            Dispatcher.UIThread.VerifyAccess();
            return owner.BeginAcceptedClose(this);
        }

        internal void MarkAccepted() => _accepted = true;
    }

    private static async Task AwaitOnUiAsync(Func<Task> action)
    {
        var dispatch = Dispatcher.UIThread.InvokeAsync<Task>(action).GetTask();
        var actual = await dispatch.ConfigureAwait(false);
        await actual.ConfigureAwait(false);
    }

    public Task CloseAndDrainAsync()
    {
        CloudflareOriginalExecutionGuard.DemandExternalJoin(this);
        lock (_gate)
        {
            if (_close is not null) return _close;
            if (_initialization is { IsCompletedSuccessfully: true })
                throw new InvalidOperationException("Prepare the original Boards presentation before retirement.");
            _closing = true;
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _close = CloseCoreAsync(start.Task);
            start.SetResult();
            return _close;
        }
    }

    private async Task CloseCoreAsync(Task start)
    {
        await start.ConfigureAwait(false);
        var initialization = OriginalInitialization;
        if (initialization is not null)
            try { await initialization.ConfigureAwait(false); } catch { /* Preserve OriginalInitialization; finish deterministic cleanup. */ }
        if (_prepared?.Nested is { } nested)
            await nested.CommitAsync().ConfigureAwait(false);
        Task? titleRefresh;
        lock (_gate) titleRefresh = _titleRefresh;
        if (titleRefresh is not null)
            try { await titleRefresh.ConfigureAwait(false); } catch (OperationCanceledException) { }
        var presentation = _presentation;
        if (presentation is not null)
        {
            presentation.ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
            Task dispose;
            if (Dispatcher.UIThread.CheckAccess()) dispose = presentation.DisposeAsync().AsTask();
            else
            {
                var dispatch = Dispatcher.UIThread.InvokeAsync<Task>(() => presentation.DisposeAsync().AsTask()).GetTask();
                dispose = await dispatch.ConfigureAwait(false);
            }
            await dispose.ConfigureAwait(false);
            _presentation = null;
        }
        if (_embeddedHost is IAsyncDisposable embeddedOwner)
            await embeddedOwner.DisposeAsync().ConfigureAwait(false);
        var lease = _lease;
        if (lease is not null)
        {
            await lease.DisposeAsync().ConfigureAwait(false);
            _lease = null;
        }
        _localDocuments?.Dispose();
        _published = false;
        var lifetimeFailures = new List<Exception>();
        try { _lifetime.Cancel(); } catch (Exception error) { lifetimeFailures.Add(error); }
        try { _lifetime.Dispose(); } catch (Exception error) { lifetimeFailures.Add(error); }
        if (lifetimeFailures.Count != 0)
            throw new AggregateException("The original writable Boards tab lifetime did not retire cleanly.", lifetimeFailures);
    }
}
