using System.Security.Cryptography;
using Avalonia.Controls;
using Avalonia.Threading;
using CakeOS.Apps.Boards.App;
using Haven.Application;
using Haven.Core;
using HavenOS.Apps.Browse;

namespace Haven.Desktop.Services;

/// <summary>A read-only standalone rich Boards surface over one Home-approved immutable Files
/// revision. The session owns both the rich surface and its byte lease through retirement.</summary>
internal sealed class BoardsRichDocumentBrowseAppContentSession : IBrowseNativeAppContentSession,
    IBrowseAppContentClosePreparationParticipant
{
    private readonly BrowseAppContentRequest _request;
    private readonly CancellationTokenSource _lifetime;
    private readonly IBoardsEmbeddedArtifactHost? _embeddedHost;
    private readonly object _gate = new();
    private NativeHomeFilesBoardReadLease? _lease;
    private BoardsReadOnlyPresentation? _presentation;
    private Task? _initialization, _close;
    private Task<IBrowseAppContentClosePreparation?>? _preparationTask;
    private ClosePreparation? _prepared;
    private volatile bool _published, _closing;

    internal BoardsRichDocumentBrowseAppContentSession(BrowseAppContentRequest request,
        BoardsBrowseAppContentAddress address, NativeHomeFilesBoardReadLease lease,
        CancellationTokenSource lifetime, IBoardsEmbeddedArtifactHost? embeddedHost = null)
    {
        _request = request ?? throw new ArgumentNullException(nameof(request));
        ArgumentNullException.ThrowIfNull(address);
        _lease = lease ?? throw new ArgumentNullException(nameof(lease));
        _lifetime = lifetime ?? throw new ArgumentNullException(nameof(lifetime));
        _embeddedHost = embeddedHost;
        if (!address.IsFilesDocument || address.StoreId != lease.StoreId || address.ParentFolderId != lease.ParentFolderId ||
            address.FileId != lease.FileId || address.RevisionId != lease.RevisionId ||
            !StringComparer.Ordinal.Equals(request.ProfileId, lease.ProfileId))
            throw new UnauthorizedAccessException("The prepared Home read lease does not match the canonical Boards address.");
    }

    public AppContentAddress Address => _request.Address;
    public string Title => _presentation?.Document.RichNotes?.Title is { Length: > 0 } title ? title + " — Boards" : "Boards";
    public string IconKey => "boards";
    public event EventHandler? StateChanged;
    public Task? OriginalInitialization { get { lock (_gate) return _initialization; } }
    public Task? OriginalClose { get { lock (_gate) return _close; } }
    public bool IsPublicationCurrent => _published && !_closing && !_lifetime.IsCancellationRequested &&
        _lease?.IsOpen == true && OriginalInitialization is { IsCompletedSuccessfully: true };
    public bool CanRetireOriginalView => OriginalClose is { IsCompletedSuccessfully: true };

    public Control OriginalNativeView
    {
        get
        {
            Dispatcher.UIThread.VerifyAccess();
            if (!IsPublicationCurrent)
                throw new InvalidOperationException("The original rich Boards document has not completed its read-only admission.");
            return _presentation?.Content
                ?? throw new InvalidOperationException("The original rich Boards presentation is unavailable.");
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
        var lease = _lease ?? throw new InvalidOperationException("The original Home Files read lease is missing.");
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(caller, _lifetime.Token);
        linked.Token.ThrowIfCancellationRequested();
        await lease.DemandCurrentAsync(linked.Token).ConfigureAwait(false);
        var bytes = lease.Bytes;
        if (bytes.IsEmpty || Convert.ToHexString(SHA256.HashData(bytes.Span)) != lease.Sha256)
            throw new InvalidDataException("The Home Files board buffer changed after its immutable revision was verified.");

        var dispatch = Dispatcher.UIThread.InvokeAsync<Task>(async () =>
        {
            Dispatcher.UIThread.VerifyAccess();
            using var uiPhase = CloudflareOriginalExecutionGuard.EnterOriginal(this);
            linked.Token.ThrowIfCancellationRequested();
            await lease.DemandCurrentAsync(linked.Token).ConfigureAwait(true);
            var presentation = await BoardsReadOnlyPresentation.OpenReadOnlyDocumentAsync(bytes, linked.Token, _embeddedHost);
            try
            {
                // The CUI host is independently validated by the Boards owner; Home publishes
                // only after rechecking its original authority and exact prepared file identity.
                await lease.DemandCurrentAsync(linked.Token).ConfigureAwait(true);
                if (presentation.Document.DocumentId == Guid.Empty)
                    throw new InvalidDataException("The selected rich Boards document has no canonical identity.");
                _presentation = presentation;
                _published = true;
                CloudflareOriginalExecutionGuard.InvokeOriginal(this, () => { StateChanged?.Invoke(this, EventArgs.Empty); return true; });
            }
            catch { await presentation.DisposeAsync(); throw; }
        }).GetTask();
        var actual = await dispatch.ConfigureAwait(false);
        await actual.ConfigureAwait(false);
    }

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
        await start.ConfigureAwait(false);
        var initialization = OriginalInitialization;
        if (initialization is null) return null;
        try { await initialization.ConfigureAwait(false); }
        catch { return CreateFailedInitializationPreparation(); }
        token.ThrowIfCancellationRequested();
        var presentation = _presentation;
        if (presentation is null) return CreateFailedInitializationPreparation();
        var dispatch = Dispatcher.UIThread.InvokeAsync<Task<IBoardsPresentationClosePreparation?>>(
            () => presentation.PrepareToCloseAsync(token)).GetTask();
        var actual = await dispatch.ConfigureAwait(false);
        var nested = await actual.ConfigureAwait(false);
        if (nested is null) return null;
        if (token.IsCancellationRequested)
        {
            await AwaitOnUiAsync(nested.AbortAsync).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
        }
        var preparation = new ClosePreparation(this, presentation, nested);
        var accepted = false;
        lock (_gate)
        {
            if (!_closing && ReferenceEquals(_presentation, presentation) && nested.IsCurrent)
            {
                _prepared = preparation;
                accepted = true;
            }
        }
        if (!accepted)
        {
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
            _published = false;
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _close = CloseCoreAsync(start.Task);
            start.SetResult();
            return _close;
        }
    }

    private sealed class ClosePreparation(BoardsRichDocumentBrowseAppContentSession owner,
        BoardsReadOnlyPresentation? presentation, IBoardsPresentationClosePreparation? nested)
        : IBrowseAppContentClosePreparation
    {
        private bool _accepted, _aborted;
        private Task? _abort;
        internal bool Accepted => _accepted;
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
            if (nested is not null)
                await AwaitOnUiAsync(nested.AbortAsync).ConfigureAwait(false);
            lock (owner._gate)
            {
                if (!IsCurrent && !ReferenceEquals(owner._prepared, this))
                    throw new InvalidOperationException("The original Boards preparation is no longer current.");
                if (ReferenceEquals(owner._prepared, this)) owner._prepared = null;
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
        {
            try { await initialization.ConfigureAwait(false); }
            catch { /* Preserve it on OriginalInitialization; continue deterministic cleanup. */ }
        }
        if (_prepared?.Nested is { } nested)
            await nested.CommitAsync().ConfigureAwait(false);
        var presentation = _presentation;
        if (presentation is not null)
        {
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
        _published = false;
        var lifetimeFailures = new List<Exception>();
        try { _lifetime.Cancel(); } catch (Exception error) { lifetimeFailures.Add(error); }
        try { _lifetime.Dispose(); } catch (Exception error) { lifetimeFailures.Add(error); }
        if (lifetimeFailures.Count != 0)
            throw new AggregateException("The original rich Boards tab lifetime did not retire cleanly.", lifetimeFailures);
    }
}
