using System.Text.Json;
using Haven.Application;
using HavenOS.Forms;
using HavenOS.Home.Core;

namespace NineToOne.Web.Forms;

/// <summary>Registered original Forms designer. Missing authenticated owner services stay unavailable.
/// The route retains drafts across presentation detachment and admits a new owner only after CUI loading.</summary>
public sealed class FormsBrowserRoute(Func<CancellationToken, Task<FormHostSession>>? openOwner,
    Func<bool> presentationCurrent, Func<FormNativePreview, CancellationToken, Task>? showPreview = null)
    : IHomeFeatureRouteHandler, IDisposable, IBrowserCloseParticipant
{
    public const string Id = "app.forms";
    public string RouteId => Id;
    private readonly object _gate = new();
    private readonly SemaphoreSlim _navigation = new(1, 1);
    private readonly Dictionary<string, Pending> _pending = new(StringComparer.Ordinal);
    private FormsBrowserSession? _active;
    private bool _disposed;
    public bool HasUnsavedChanges { get { lock (_gate) return _active?.HasUnsavedChanges == true || _pending.Values.Any(item => item.Session.HasUnsavedChanges); } }
    private sealed class Pending(FormsBrowserSession session, bool reused, CancellationToken token)
    {
        public FormsBrowserSession Session { get; } = session;
        public bool Reused { get; } = reused;
        public CancellationToken Token { get; } = token;
        public CancellationTokenRegistration Registration;
        public bool Prepared;
        public void Release() { Registration.Unregister(); if (!Reused) Session.Dispose(); }
    }
    public async Task<HomeFeatureNavigationResult> OpenAsync(HomeFeatureNavigationRequest request, CancellationToken cancellationToken = default)
    {
        if (request.RouteId != Id || request.EntityType is not (null or "form") || request.Action is not (null or "open")
            || request.DeepLink is not null || request.ModelPickerTarget is not null)
            return new(false, "FormsInvalidDestination", "This route opens the canonical Forms designer.", request);
        Guid? selected = null;
        if (request.EntityId is not null)
        {
            if (request.EntityType != "form" || !Guid.TryParse(request.EntityId, out var id) || id == Guid.Empty)
                return new(false, "FormsInvalidDestination", "Choose a valid canonical FormID.", request);
            selected = id;
        }
        if (openOwner is null) return new(false, "HomeServiceUnavailable", "The authenticated Forms owner service is unavailable.", request);
        await _navigation.WaitAsync(cancellationToken);
        FormsBrowserSession? candidate = null; var reused = false;
        try
        {
            lock (_gate)
            {
                if (_disposed) return new(false, "FormsUnavailable", "Forms context is closed.", request);
                if (_active is { } active && (selected is null || active.FormID == selected)) { candidate = active; reused = true; }
                else if (_active?.HasUnsavedChanges == true)
                    return new(false, "FormsUnsavedChanges", "Save or discard the current Forms inspector before opening another form.", request);
            }
            if (candidate is null)
            {
                var owner = await openOwner(cancellationToken);
                try { candidate = new(owner, selected, presentationCurrent, showPreview); }
                catch { owner.Dispose(); throw; }
                await candidate.InitializeAsync(cancellationToken);
            }
            else await candidate.ValidateAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            var viewID = "forms.designer." + Guid.NewGuid().ToString("N");
            lock (_gate)
            {
                if (_disposed) throw new OperationCanceledException("Forms context closed.");
                var old = _pending.Values.ToArray(); _pending.Clear(); foreach (var pending in old) pending.Release();
                var pendingView = new Pending(candidate, reused, cancellationToken); _pending.Add(viewID, pendingView);
                pendingView.Registration = cancellationToken.Register(() => Reject(viewID));
                if (!_pending.ContainsKey(viewID)) pendingView.Registration.Unregister();
                cancellationToken.ThrowIfCancellationRequested();
            }
            return new(true, "Succeeded", "Canonical Forms designer prepared.", request, ViewState:
                new(Id, viewID, 0, JsonSerializer.SerializeToElement(new { formID = candidate.FormID })));
        }
        catch (OperationCanceledException) { if (!reused) candidate?.Dispose(); throw; }
        catch (Exception error) when (error is UnauthorizedAccessException or InvalidOperationException or IOException
            or InvalidDataException or ArgumentException or NotSupportedException or JsonException)
        {
            if (!reused) candidate?.Dispose();
            return new(false, "FormsUnavailable", "The canonical Forms owner could not open this destination.", request);
        }
        finally { _navigation.Release(); }
    }
    public BrowserCuiSurface CreateSurface(HomeFeatureViewState state)
    {
        lock (_gate)
        {
            if (_disposed || state.RouteId != Id || !_pending.TryGetValue(state.ViewId, out var pending) || pending.Prepared || pending.Token.IsCancellationRequested)
                throw new InvalidOperationException("Forms presentation is unavailable.");
            pending.Prepared = true;
            var admission = new Admission(this, state.ViewId);
            // Original owner resource and binding/action implementation; no reduced browser designer.
            try { return new(FormsCuiWorkspace.LoadDocument(), pending.Session, pending.Session, admission, Admission: admission); }
            catch { admission.Reject(); throw; }
        }
    }
    private void Accept(string id)
    {
        lock (_gate)
        {
            if (_disposed || !_pending.TryGetValue(id, out var pending) || pending.Token.IsCancellationRequested)
                throw new InvalidOperationException("Forms presentation expired.");
            if (!pending.Reused && _active?.HasUnsavedChanges == true)
                throw new InvalidOperationException("Forms has a retained unsaved inspector.");
            _pending.Remove(id); pending.Registration.Unregister();
            var previous = _active; _active = pending.Session; _active.AdmitPresentation();
            if (previous is not null && !ReferenceEquals(previous, _active)) previous.Dispose();
        }
    }
    private void Reject(string id)
    { lock (_gate) { if (_pending.Remove(id, out var pending)) pending.Release(); } }
    private sealed class Admission(FormsBrowserRoute route, string id) : IBrowserPresentationAdmission, IDisposable
    {
        private bool _decided;
        public void Accept() { if (_decided) return; route.Accept(id); _decided = true; }
        public void Reject() { if (_decided) return; route.Reject(id); _decided = true; }
        public void Dispose() { if (!_decided) Reject(); }
    }
    public Task<HomeCoreOperationResult<bool>> PrepareToCloseAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate) return _active?.PrepareToCloseAsync(cancellationToken)
            ?? Task.FromResult(new HomeCoreOperationResult<bool>(true, "Succeeded", "Forms has no active owner.", true));
    }
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return; _disposed = true;
            var active = _active; _active = null; var pending = _pending.Values.ToArray(); _pending.Clear();
            active?.Dispose(); foreach (var item in pending) item.Release();
        }
    }
}
