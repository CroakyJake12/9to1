using System.Text.Json;
using CakeOS.Cui;
using HavenOS.Files;
using HavenOS.Home.Core;

namespace NineToOne.Web.Files;

/// <summary>Actual Files CUI destination. Registration is explicit and requires a host-supplied owner adapter.</summary>
public sealed class FilesBrowserRoute(IFilesProvider provider, string authenticatedActor, CuiDocument document)
    : IHomeFeatureRouteHandler, IDisposable
{
    private readonly object _gate = new();
    private readonly Dictionary<string, PendingView> _pending = new(StringComparer.Ordinal);
    private bool _disposed;
    public const string Id = "app.files";
    public string RouteId => Id;

    public async Task<HomeFeatureNavigationResult> OpenAsync(HomeFeatureNavigationRequest request,
        CancellationToken cancellationToken = default)
    {
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
            _disposed = true;
            var pendingViews = _pending.Values.ToArray(); _pending.Clear();
            ReleaseAll(pendingViews);
        }
    }
}
