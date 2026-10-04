using System.Text.Json;
using CakeOS.Cui;
using HavenOS.Files;
using HavenOS.Home.Core;

namespace NineToOne.Web.Files;

/// <summary>Actual Files CUI destination. Registration is explicit and requires a host-supplied owner adapter.</summary>
public sealed class FilesBrowserRoute(IFilesProvider provider, string authenticatedActor, CuiDocument document, Action revokeAllIssuedOwnerWork, Func<ValueTask> drainAllIssuedOwnerWork)
    : IHomeFeatureRouteHandler, IBrowserCloseParticipant, IBrowserPrivateContextParticipant, IDisposable
{
    private readonly object _gate = new();
    private readonly Dictionary<string, PendingView> _pending = new(StringComparer.Ordinal);
    private bool _disposed;
    private readonly HashSet<FilesBrowserController> _issuedViews = [];
    private readonly HashSet<TaskCompletionSource> _issuedOpen = [];
    private bool _preparing;
    private bool _revoked;
    private Task? _revocationDrain;
    private Exception? _fenceFailure;
    // REQUIRED actual issuer primitive. No no-op/default authority and no registration supplied.
    private readonly Action _revokeOwner = revokeAllIssuedOwnerWork ?? throw new ArgumentNullException(nameof(revokeAllIssuedOwnerWork));
    private readonly Func<ValueTask> _drainOwner = drainAllIssuedOwnerWork ?? throw new ArgumentNullException(nameof(drainAllIssuedOwnerWork));
    public bool HasUnsavedChanges { get { lock (_gate) return !_revoked && (_issuedOpen.Count != 0 || _issuedViews.Any(view => view.HasUnsavedChanges)); } }
    public Task<HomeCoreOperationResult<bool>> PrepareToCloseAsync(CancellationToken cancellationToken = default)
        => PrepareToCloseCoreAsync(null, cancellationToken);
    private async Task<HomeCoreOperationResult<bool>> PrepareToCloseCoreAsync(TaskCompletionSource? ownOpening, CancellationToken cancellationToken)
    {
        Task[] work;
        lock (_gate)
        {
            if (_revoked || _disposed) return new(false, "PermissionDenied", "The private owner context has closed.", false);
            if (_preparing || _issuedOpen.Any(open => !ReferenceEquals(open, ownOpening))) return new(false, "BrowserBusy", "Wait for the current opening to finish.", false);
            _preparing = true; work = _issuedViews.Select(view => view.DrainIssuedAsync()).ToArray();
        }
        try
        {
            await Task.WhenAll(work).WaitAsync(cancellationToken);
            lock (_gate)
            {
                if (_revoked) return new(false, "PermissionDenied", "The private owner context has closed.", false);
                if (_issuedViews.Any(view => view.HasUnsavedChanges)) return new(false, "UnsavedChanges", "Save or discard the current draft before closing.", false);
                return new(true, "Succeeded", "The private views are ready to close.", true);
            }
        }
        finally { lock (_gate) _preparing = false; }
    }
    public void RevokePrivateContext()
    {
        FilesBrowserController[] views; PendingView[] pending;
        lock (_gate)
        {
            if (_revoked)
            {
                if (_fenceFailure is not null) throw new InvalidOperationException("Real owner-group revocation failed; private cleanup remains held.", _fenceFailure);
                return;
            }
            _revoked = _disposed = true; views = _issuedViews.ToArray(); pending = _pending.Values.ToArray(); _pending.Clear();
        }
        try { _revokeOwner(); }
        catch (Exception error) { lock (_gate) _fenceFailure = error; }
        foreach (var view in views) view.ClearPrivatePresentation();
        foreach (var view in views) view.Dispose();
        ReleaseAll(pending);
        if (_fenceFailure is not null) throw new InvalidOperationException("Real owner-group revocation failed; private cleanup remains held.", _fenceFailure);
    }
    public ValueTask DisposeAsync()
    {
        TaskCompletionSource completion;
        lock (_gate)
        {
            if (!_revoked) return new(CloseOrdinarilyAsync());
            if (_revocationDrain is not null) return new(_revocationDrain);
            completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
            _revocationDrain = completion.Task;
        }
        _ = SettleRevocationDrainAsync(completion);
        return new(completion.Task);
    }
    private async Task SettleRevocationDrainAsync(TaskCompletionSource completion)
    {
        try { await DrainAfterRevocationAsync(); completion.TrySetResult(); }
        catch (Exception error) { completion.TrySetException(error); }
    }
    private async Task CloseOrdinarilyAsync()
    {
        var prepared = await PrepareToCloseAsync();
        if (!prepared.Succeeded || prepared.Value != true) throw new InvalidOperationException(prepared.Message);
        FilesBrowserController[] views; bool revoked;
        lock (_gate)
        {
            revoked = _revoked;
            if (!revoked && (_issuedOpen.Count != 0 || _issuedViews.Any(view => view.HasUnsavedChanges)))
                throw new InvalidOperationException("A new draft or operation requires preparation again.");
            if (!revoked) _disposed = true;
            views = _issuedViews.ToArray();
        }
        if (revoked) { await DisposeAsync(); return; }
        foreach (var view in views) view.Dispose();
        await DrainIssuedWorkAsync();
        ReleaseRetainedViews();
    }
    private async Task DrainAfterRevocationAsync()
    {
        await DrainIssuedWorkAsync();
        Exception? ownerDrainFailure = null;
        try { await _drainOwner(); }
        catch (Exception error) { ownerDrainFailure = error; }
        ReleaseRetainedViews();
        if (_fenceFailure is not null || ownerDrainFailure is not null)
        {
            var failures = new List<Exception>();
            if (_fenceFailure is not null) failures.Add(_fenceFailure);
            if (ownerDrainFailure is not null) failures.Add(ownerDrainFailure);
            throw new InvalidOperationException("Real owner-group revocation/drain was not established.", new AggregateException(failures));
        }
    }
    private async Task DrainIssuedWorkAsync()
    {
        while (true)
        {
            Task[] work;
            lock (_gate) work = _issuedOpen.Select(open => open.Task).Concat(_issuedViews.Select(view => view.DrainIssuedAsync())).ToArray();
            await Task.WhenAll(work);
            lock (_gate) if (_issuedOpen.Count == 0) break;
        }
    }
    private void ReleaseRetainedViews()
    {
        PendingView[] pending;
        lock (_gate) { pending = _pending.Values.ToArray(); _pending.Clear(); _issuedViews.Clear(); }
        ReleaseAll(pending);
    }
    public const string Id = "app.files";
    public string RouteId => Id;

    public async Task<HomeFeatureNavigationResult> OpenAsync(HomeFeatureNavigationRequest request,
        CancellationToken cancellationToken = default)
    {
        var issuedOpen = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_gate)
        {
            if (_revoked || _disposed || _preparing) return new(false, "PermissionDenied", "Private owner is unavailable.", request);
            _issuedOpen.Add(issuedOpen);
        }
        try
        {
        var prepared = await PrepareToCloseCoreAsync(issuedOpen, cancellationToken);
        if (!prepared.Succeeded || prepared.Value != true) return new(false, prepared.Code, prepared.Message, request);
        if (_disposed || request.RouteId != Id)
            return new(false, "FilesUnavailable", "Files is unavailable in this account context.", request);
        HostedItemId? folder = null;
        if (request.EntityType is not (null or "folder") || request.DeepLink is not null || request.ModelPickerTarget is not null)
            return new(false, "FilesInvalidDestination", "This destination opens Files folders.", request);
        if (request.EntityId is not null)
        {
            if (request.EntityType != "folder" || !Guid.TryParse(request.EntityId, out var id) || id == Guid.Empty)
                return new(false, "FilesInvalidDestination", "Choose a valid Files folder.", request);
            folder = new(id);
        }
        if (request.Action is not (null or "open"))
            return new(false, "FilesUnsupportedAction", "This Files destination supports opening folders.", request);
        var controller = new FilesBrowserController(provider, authenticatedActor);
        lock (_gate)
        {
            if (_revoked || _disposed) { controller.ClearPrivatePresentation(); controller.Dispose(); throw new OperationCanceledException("Private owner closed before initialization."); }
            _issuedViews.Add(controller); controller.CommandAdmission = () => !_revoked && !_disposed && !_preparing;
        }
        try
        {
            await controller.InitializeAsync(folder, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            var viewId = "files.folder." + Guid.NewGuid().ToString("N");
            Retain(viewId, controller, cancellationToken);
            return new(true, "Succeeded", "Files folder opened.", request, ViewState:
                new(Id, viewId, 0, JsonSerializer.SerializeToElement(new { folderID = folder?.Value })));
        }
        catch (OperationCanceledException) { controller.Dispose(); throw; }
        catch
        {
            controller.Dispose();
            return new(false, "FilesUnavailable", "This Files folder could not be opened. Check access and retry.", request);
        }
        }
        finally { lock (_gate) { _issuedOpen.Remove(issuedOpen); issuedOpen.TrySetResult(); } }
    }

    // Cancellation owns only the pending navigation lease; the renderer consumes it once.
    private sealed class PendingView(FilesBrowserController view)
    {
        public FilesBrowserController View { get; } = view;
        public CancellationTokenRegistration Registration;
        public void Release(bool disposeView) { Registration.Unregister(); if (disposeView) View.Dispose(); }
    }
    private void Retain(string id, FilesBrowserController view, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (_disposed) throw new OperationCanceledException("Account context closed.");
            var previousViews = _pending.Values.ToArray(); _pending.Clear();
            ReleaseAll(previousViews);
            var pending = new PendingView(view); _pending.Add(id, pending);
            pending.Registration = cancellationToken.Register(() =>
            {
                lock (_gate) { if (_pending.Remove(id, out var cancelled)) cancelled.Release(true); }
            });
            cancellationToken.ThrowIfCancellationRequested();
        }
    }
    public BrowserCuiSurface CreateSurface(HomeFeatureViewState state)
    {
        lock (_gate)
        {
            if (_disposed || state.RouteId != Id || !_pending.Remove(state.ViewId, out var pending))
                throw new InvalidOperationException("Files view unavailable.");
            if (_issuedViews.Any(view => !ReferenceEquals(view, pending.View) && view.HasUnsavedChanges))
            { pending.Release(true); throw new InvalidOperationException("Current draft must be saved or discarded before replacement."); }
            pending.Release(false);
            return new(document, pending.View, pending.View, pending.View);
        }
    }
    private static void ReleaseAll(IEnumerable<PendingView> views)
    {
        foreach (var pending in views)
        {
            try { pending.Release(true); }
            catch (Exception error) { System.Diagnostics.Trace.TraceWarning("Files pending view teardown failed: {0}", error.GetType().Name); }
        }
    }
    public void Dispose()
    {
        lock (_gate)
        {
            if (_revoked) return;
            if (_issuedOpen.Count != 0 || _issuedViews.Any(view => view.HasUnsavedChanges || !view.DrainIssuedAsync().IsCompleted))
                throw new InvalidOperationException("Private issued work or drafts require awaited close.");
            _disposed = true;
            var pending = _pending.Values.ToArray(); _pending.Clear();
            foreach (var view in _issuedViews) view.Dispose();
            ReleaseAll(pending);
        }
    }
}
