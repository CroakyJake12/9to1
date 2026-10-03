using System.Text.Json;
using CakeOS.Cui;
using HavenOS.Home.Core;

namespace NineToOne.Web.Sites;

public sealed class SitesBrowserRoute(SitesBrowserOperations owner, CuiDocument document) : IHomeFeatureRouteHandler, IDisposable
{
    public const string Id = "app.sites";
    public string RouteId => Id;
    private readonly object _gate = new();
    private readonly Dictionary<string, PendingView> _pending = new(StringComparer.Ordinal);
    private bool _disposed;
    public async Task<HomeFeatureNavigationResult> OpenAsync(HomeFeatureNavigationRequest request, CancellationToken cancellationToken = default)
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
            _disposed = true;
            var pendingViews = _pending.Values.ToArray(); _pending.Clear();
            ReleaseAll(pendingViews);
        }
    }
}
