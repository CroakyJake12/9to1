using Avalonia.Controls;
using Avalonia.Threading;
using CakeOS.Apps.Boards.App;
using System.ComponentModel;
using Haven.Core;
using HavenOS.Apps.Browse;

namespace Haven.Desktop.Services;

/// <summary>An editable rich Boards surface whose only persistence action is the selected
/// Home Files revision callback. The exact file lease remains alive through tab retirement.</summary>
internal sealed class BoardsWritableRichDocumentBrowseAppContentSession : IBrowseNativeAppContentSession
{
    private readonly BrowseAppContentRequest _request;
    private readonly CancellationTokenSource _lifetime;
    private readonly object _gate = new();
    private NativeHomeFilesBoardWriteLease? _lease;
    private BoardsWritablePresentation? _presentation;
    private Task? _initialization, _close;
    private Task? _titleRefresh;
    private int _titleVersion;
    private string _title = "Boards — Files document";
    private volatile bool _published, _closing;

    internal BoardsWritableRichDocumentBrowseAppContentSession(BrowseAppContentRequest request,
        BoardsBrowseAppContentAddress address, NativeHomeFilesBoardWriteLease lease,
        CancellationTokenSource lifetime)
    {
        _request = request ?? throw new ArgumentNullException(nameof(request));
        ArgumentNullException.ThrowIfNull(address);
        _lease = lease ?? throw new ArgumentNullException(nameof(lease));
        _lifetime = lifetime ?? throw new ArgumentNullException(nameof(lifetime));
        if (!address.IsFilesDocument || !address.Writable || address.StoreId != lease.StoreId ||
            address.ParentFolderId != lease.ParentFolderId || address.FileId != lease.FileId ||
            address.RevisionId != lease.RevisionId || !StringComparer.Ordinal.Equals(request.ProfileId, lease.ProfileId))
            throw new UnauthorizedAccessException("The prepared Home write lease does not match this canonical Boards address.");
    }

    public AppContentAddress Address => _request.Address;
    public string Title => _title;
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
            if (!IsPublicationCurrent) throw new InvalidOperationException("The writable rich Boards document has not completed Home admission.");
            return _presentation?.Content ?? throw new InvalidOperationException("The writable Boards presentation is unavailable.");
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
        var lease = _lease ?? throw new InvalidOperationException("The original Home Files write lease is missing.");
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(caller, _lifetime.Token);
        linked.Token.ThrowIfCancellationRequested();
        await lease.DemandCurrentAsync(linked.Token).ConfigureAwait(false);
        var bytes = lease.Bytes;
        var dispatch = Dispatcher.UIThread.InvokeAsync<Task>(async () =>
        {
            Dispatcher.UIThread.VerifyAccess();
            using var uiPhase = CloudflareOriginalExecutionGuard.EnterOriginal(this);
            linked.Token.ThrowIfCancellationRequested();
            await lease.DemandCurrentAsync(linked.Token).ConfigureAwait(true);
            var presentation = await BoardsWritablePresentation.OpenWritableDocumentAsync(
                bytes, lease.SourceId, lease.RevisionId.ToString("D"), lease.SaveAsync, linked.Token);
            try
            {
                await lease.DemandCurrentAsync(linked.Token).ConfigureAwait(true);
                if (presentation.Document.DocumentId != lease.DocumentId)
                    throw new InvalidDataException("The writable Boards presentation changed the canonical document identity.");
                var current = await presentation.GetCurrentDocumentAsync(linked.Token).ConfigureAwait(true);
                _title = FormatTitle(current.RichNotes?.Title);
                presentation.ViewModel.PropertyChanged += OnViewModelPropertyChanged;
                _presentation = presentation;
                _published = true;
                CloudflareOriginalExecutionGuard.InvokeOriginal(this, () => { StateChanged?.Invoke(this, EventArgs.Empty); return true; });
            }
            catch { await presentation.DisposeAsync(); throw; }
        }).GetTask();
        var actual = await dispatch.ConfigureAwait(false);
        await actual.ConfigureAwait(false);
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

    public Task<bool> TryPrepareToCloseAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (_close is not null) return Task.FromResult(_close.IsCompletedSuccessfully);
            if (_initialization is { IsCompleted: false }) return Task.FromResult(false);
            if (_presentation?.HasUnsavedChanges == true)
            {
                _presentation.ViewModel.Set("StatusText", "Save this Files board revision before closing.");
                _presentation.ViewModel.RefreshCuiContext();
                CloudflareOriginalExecutionGuard.InvokeOriginal(this, () => { StateChanged?.Invoke(this, EventArgs.Empty); return true; });
                return Task.FromResult(false);
            }
            return Task.FromResult(true);
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
            catch { /* Cancellation is advisory; join the actual initialization below. */ }
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
