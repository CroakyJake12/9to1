using System.Text.Json;
using CakeOS.Cui;
using HavenOS.Home.Core;

namespace NineToOne.Web.Sites;

public sealed class SitesBrowserRoute(SitesBrowserOperations owner, CuiDocument document, Action revokeAllIssuedOwnerWork, Func<ValueTask> drainAllIssuedOwnerWork) : IHomeFeatureRouteHandler, IBrowserCloseParticipant, IBrowserPrivateContextParticipant, IDisposable
{
    public const string Id = "app.sites";
    public string RouteId => Id;
    private readonly object _gate = new();
    private readonly Dictionary<string, PendingView> _pending = new(StringComparer.Ordinal);
    private bool _disposed;
    private readonly HashSet<SitesBrowserController> _issuedViews = [];
    private readonly HashSet<TaskCompletionSource> _issuedOpen = [];
    private bool _preparing;
    private bool _revoked;
    private Task? _revocationDrain;
    private Exception? _fenceFailure;
    // REQUIRED actual issuer primitive. No no-op/default authority and no registration supplied.
    private readonly Action _revokeOwner = revokeAllIssuedOwnerWork ?? throw new ArgumentNullException(nameof(revokeAllIssuedOwnerWork));
    private readonly Func<ValueTask> _drainOwner = drainAllIssuedOwnerWork ?? throw new ArgumentNullException(nameof(drainAllIssuedOwnerWork));
    public bool HasUnsavedChanges { get { lock (_gate) return !_revoked && (_issuedOpen.Count != 0 || _issuedViews.Any(view => view.HasUnsavedChanges)); } }
    public async Task<HomeCoreOperationResult<bool>> PrepareToCloseAsync(CancellationToken cancellationToken = default)
    {
        Task[] work;
        lock (_gate)
        {
            if (_revoked || _disposed) return new(false, "PermissionDenied", "The private owner context has closed.", false);
            if (_preparing || _issuedOpen.Count != 0) return new(false, "BrowserBusy", "Wait for the current opening to finish.", false);
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
        SitesBrowserController[] views; PendingView[] pending;
        lock (_gate)
        {
            if (_revoked) return;
            _revoked = _disposed = true; views = _issuedViews.ToArray(); pending = _pending.Values.ToArray(); _pending.Clear();
        }
        try { _revokeOwner(); }
        catch (Exception error) { _fenceFailure = error; }
        foreach (var view in views) view.ClearPrivatePresentation();
        foreach (var view in views) view.Dispose();
        ReleaseAll(pending);
        if (_fenceFailure is not null) throw new InvalidOperationException("Real owner-group revocation failed; private cleanup remains held.", _fenceFailure);
    }
    public ValueTask DisposeAsync()
    {
        lock (_gate)
        {
            if (_revoked) return new(_revocationDrain ??= DrainAfterRevocationAsync());
            return new(CloseOrdinarilyAsync());
        }
    }
    private async Task CloseOrdinarilyAsync()
    {
        var prepared = await PrepareToCloseAsync();
        if (!prepared.Succeeded || prepared.Value != true) throw new InvalidOperationException(prepared.Message);
        SitesBrowserController[] views; bool revoked;
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
    public async Task<HomeFeatureNavigationResult> OpenAsync(HomeFeatureNavigationRequest request, CancellationToken cancellationToken = default)
    {
        var prepared = await PrepareToCloseAsync(cancellationToken);
        if (!prepared.Succeeded || prepared.Value != true) return new(false, prepared.Code, prepared.Message, request);
        var issuedOpen = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_gate)
        {
            if (_revoked || _disposed || _preparing) return new(false, "PermissionDenied", "Private owner is unavailable.", request);
            _issuedOpen.Add(issuedOpen);
        }
        try
        {
        if (_disposed || request.RouteId != Id) return new(false, "SitesUnavailable", "Sites is unavailable in this account context.", request);
        if (request.EntityType is not (null or "site") || request.Action is not (null or "open") || request.DeepLink is not null || request.ModelPickerTarget is not null)
            return new(false, "SitesInvalidDestination", "This destination opens Sites websites.", request);
        Guid? siteID = null;
        if (request.EntityId is not null)
        {
            if (request.EntityType != "site" || !Guid.TryParse(request.EntityId, out var id) || id == Guid.Empty)
                return new(false, "SitesInvalidDestination", "Choose a valid Sites website.", request);
            siteID = id;
        }
        var view = new SitesBrowserController(owner);
        lock (_gate)
        {
            if (_revoked || _disposed) { view.ClearPrivatePresentation(); view.Dispose(); throw new OperationCanceledException("Private owner closed before initialization."); }
            _issuedViews.Add(view); view.CommandAdmission = () => !_revoked && !_disposed && !_preparing;
        }
        try
        {
            await view.InitializeAsync(siteID, cancellationToken); cancellationToken.ThrowIfCancellationRequested();
            var viewID = "sites.authoring." + Guid.NewGuid().ToString("N");
            Retain(viewID, view, cancellationToken);
            return new(true, "Succeeded", "Sites opened.", request, ViewState:
                new(Id, viewID, view.CurrentProject?.Revision ?? 0, JsonSerializer.SerializeToElement(new { siteID })));
        }
        catch (OperationCanceledException) { view.Dispose(); throw; }
        catch { view.Dispose(); return new(false, "SitesUnavailable", "This Sites destination could not be opened. Check access and retry.", request); }
        }
        finally { lock (_gate) { _issuedOpen.Remove(issuedOpen); issuedOpen.TrySetResult(); } }
    }
    // Cancellation owns only the pending navigation lease; the renderer consumes it once.
    private sealed class PendingView(SitesBrowserController view)
    {
        public SitesBrowserController View { get; } = view;
        public CancellationTokenRegistration Registration;
        public void Release(bool disposeView) { Registration.Unregister(); if (disposeView) View.Dispose(); }
    }
    private void Retain(string id, SitesBrowserController view, CancellationToken cancellationToken)
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
                throw new InvalidOperationException("Sites view unavailable.");
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
            catch (Exception error) { System.Diagnostics.Trace.TraceWarning("Sites pending view teardown failed: {0}", error.GetType().Name); }
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
