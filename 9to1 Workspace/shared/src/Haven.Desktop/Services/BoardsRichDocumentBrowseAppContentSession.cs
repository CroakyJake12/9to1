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
internal sealed class BoardsRichDocumentBrowseAppContentSession : IBrowseNativeAppContentSession
{
    private readonly BrowseAppContentRequest _request;
    private readonly BoardsBrowseAppContentAddress _address;
    private readonly CancellationTokenSource _lifetime;
    private readonly object _gate = new();
    private NativeHomeFilesBoardReadLease? _lease;
    private BoardsReadOnlyPresentation? _presentation;
    private Task? _initialization, _close;
    private volatile bool _published, _closing;

    internal BoardsRichDocumentBrowseAppContentSession(BrowseAppContentRequest request,
        BoardsBrowseAppContentAddress address, NativeHomeFilesBoardReadLease lease,
        CancellationTokenSource lifetime)
    {
        _request = request ?? throw new ArgumentNullException(nameof(request));
        _address = address ?? throw new ArgumentNullException(nameof(address));
        _lease = lease ?? throw new ArgumentNullException(nameof(lease));
        _lifetime = lifetime ?? throw new ArgumentNullException(nameof(lifetime));
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
            var presentation = await BoardsReadOnlyPresentation.OpenReadOnlyDocumentAsync(bytes, linked.Token);
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

    public Task<bool> TryPrepareToCloseAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (_close is not null) return Task.FromResult(_close.IsCompletedSuccessfully);
            if (_initialization is { IsCompleted: false }) return Task.FromResult(false);
            return Task.FromResult(true); // This surface is read-only and owns no save work.
        }
    }

    public Task CloseAndDrainAsync()
    {
        CloudflareOriginalExecutionGuard.DemandExternalJoin(this);
        lock (_gate)
        {
            if (_close is not null) return _close;
            _closing = true;
            try { _lifetime.Cancel(); }
            catch { /* Cancellation is advisory; the actual initialization remains joined. */ }
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
        var lease = _lease;
        if (lease is not null)
        {
            await lease.DisposeAsync().ConfigureAwait(false);
            _lease = null;
        }
        _published = false;
        _lifetime.Dispose();
    }
}
