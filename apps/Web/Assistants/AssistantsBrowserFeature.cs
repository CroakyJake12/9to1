using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Threading;
using CakeOS.Cui.Language;
using CakeOS.Cui.Runtime;
using HavenOS.Apps.Assistants.NativeUI;
using HavenOS.Home.Core;

namespace NineToOne.Web.Assistants;

/// <summary>Dedicated Assistants route. Missing host services present setup only; configured
/// presentation is the SAME portable canonical native CUI owner over the actual supplied bridge.</summary>
public sealed class AssistantsBrowserFeature : IHomeFeatureRouteHandler, IBrowserPrivateContextParticipant, IBrowserCloseParticipant
{
    private readonly IBrowserAssistantsCanonicalOwner? _owner;
    private readonly BrowserAssistantsOwnerAdapter? _acquisition;
    private readonly BrowserAssistantsBootstrap _bootstrap = new();
    private readonly object _gate = new();
    private readonly List<Task> _opens = [], _preparations = [], _sources = [];
    private readonly List<Exception> _failures = [];
    private readonly AsyncLocal<Invocation?> _current = new();
    [ThreadStatic] private static Dictionary<AssistantsBrowserFeature, int>? _physicalSources;
    private AssistantsNativeCuiSurface? _native;
    private Task? _initialization, _close;
    private ContentControl? _mount;
    private Offer? _offer;
    private long _generation;
    private bool _revoked;
    private bool _revocationRequested;
    private sealed class Invocation { internal bool Live = true; }

    public AssistantsBrowserFeature(IBrowserAssistantsCanonicalOwner? actualOwner = null)
    { _owner = actualOwner; if (actualOwner is not null) _acquisition = new(actualOwner); }
    public string RouteId => "app.assistants";
    public bool HasUnsavedChanges => _native?.HasUnsavedChanges == true;
    public Task? OriginalClose { get { lock (_gate) return _close; } }
    public static HomeCoreOperationResult<bool> Register(BrowserSurfaceRegistry registry,
        IBrowserAssistantsCanonicalOwner? actualOwner = null)
    {
        var feature = new AssistantsBrowserFeature(actualOwner);
        return registry.Register(feature, feature.Render, BrowserSurfaceScope.PrivateContext);
    }

    public Task<HomeFeatureNavigationResult> OpenAsync(HomeFeatureNavigationRequest request, CancellationToken cancellationToken = default)
    {
        TaskCompletionSource start; Task<HomeFeatureNavigationResult> actual;
        lock (_gate)
        {
            if (_revoked) return Task.FromResult(new HomeFeatureNavigationResult(false, "PermissionDenied", "The Assistants context has changed.", request));
            _opens.RemoveAll(task => task.IsCompletedSuccessfully);
            if (_opens.Count >= 64) throw new InvalidOperationException("Inspect retained Assistants navigation failures before new requests.");
            start = new(TaskCreationOptions.RunContinuationsAsynchronously);
            actual = OpenOriginalAsync(start.Task, request, cancellationToken); _opens.Add(actual);
        }
        start.TrySetResult(); return actual;
    }
    private async Task<HomeFeatureNavigationResult> OpenOriginalAsync(Task start, HomeFeatureNavigationRequest request, CancellationToken token)
    {
        await start; var invocation = new Invocation(); var previous = _current.Value; _current.Value = invocation;
        try
        {
            if (request.RouteId != RouteId || request.Action is not (null or "open") || request.EntityId is not null ||
                request.EntityType is not null || request.ModelPickerTarget is not null || request.DeepLink is not null)
                return new(false, "InvalidArgument", "Open Assistants from its dedicated product destination.", request);
            var generation = Interlocked.Increment(ref _generation);
            if (_owner is not null)
            {
                await InitializeOriginalAsync(token);
                var prepared = await PrepareToCloseAsync(token);
                if (!prepared.Succeeded || prepared.Value != true) return new(false, prepared.Code, prepared.Message, request);
            }
            token.ThrowIfCancellationRequested();
            lock (_gate)
            {
                if (_revoked || generation != _generation) return new(false, "PermissionDenied", "The Assistants context has changed.", request);
                var state = new HomeFeatureViewState(RouteId, "assistants.browser-product", 0,
                    JsonSerializer.SerializeToElement(new { readiness = _owner is null ? "setup-required" : "canonical-presentation-prepared" }));
                _offer = new(this, generation, state, token);
                // Success here means UI navigation preparation only. No definition, AI or effect outcome is asserted.
                return new(true, _owner is null ? "SetupRequired" : "PresentationPrepared",
                    _owner is null ? BrowserAssistantsBootstrap.MissingOwnerMessage : "The canonical Assistants presentation is prepared.", request, ViewState: state);
            }
        }
        finally { invocation.Live = false; _current.Value = previous; }
    }

    private Task InitializeOriginalAsync(CancellationToken token)
    {
        TaskCompletionSource start; Task actual;
        lock (_gate)
        {
            if (_initialization is not null) return _initialization;
            start = new(TaskCreationOptions.RunContinuationsAsynchronously);
            actual = InitializeCoreAsync(start.Task, token); _initialization = actual;
        }
        start.TrySetResult(); return actual;
    }
    private async Task InitializeCoreAsync(Task start, CancellationToken token)
    {
        await start;
        var acquisition = _acquisition ?? throw new InvalidOperationException("The canonical acquisition owner is unavailable.");
        var acquired = Source(() => acquisition.InitializeOriginalAsync(token));
        if (!await acquired) throw new UnauthorizedAccessException("The Assistants context was revoked during acquisition.");
        var controller = acquisition.DemandCurrentController();
        await Source(() => Dispatcher.UIThread.InvokeAsync(() =>
        {
            lock (_gate) if (_revoked) throw new UnauthorizedAccessException("The Assistants context was revoked before presentation.");
            Invoke(() => new AssistantsNativeCuiSurface(controller, _owner!.OriginalReadiness,
                captureOriginalOwner: actual => { lock (_gate) _native = actual; }));
        }).GetTask());
        var native = _native ?? throw new InvalidOperationException("The actual canonical CUI owner was not captured.");
        Task? initialization = null;
        await Source(() => Dispatcher.UIThread.InvokeAsync(() => initialization = Source(() => native.InitializeAsync(token))).GetTask());
        await (initialization ?? throw new InvalidOperationException("The actual canonical initialization Task was not acquired."));
    }

    public BrowserCuiSurface Render(HomeFeatureViewState state)
    {
        Offer offer;
        lock (_gate) offer = _offer is { } current && ReferenceEquals(current.State, state) && current.CanAccept
            ? current : throw new InvalidOperationException("This Assistants presentation is no longer current.");
        if (_native is null) return new(_bootstrap.Document, _bootstrap, _bootstrap, Admission: offer);
        var parser = new CuiRichParser();
        var document = parser.Parse("<Cui><Object Type=\"AssistantsCanonicalSurface\" AccessibleName=\"Assistants\" /></Cui>", "AssistantsBrowserHost.cui");
        var registry = new CuiControlRegistry();
        // This holder performs a real accepted transfer of the SAME canonical retained control.
        // The candidate loader cannot attach that control to two parents before acceptance.
        var holder = new ContentControl(); offer.Mount = holder;
        registry.RegisterObjectRenderer("AssistantsCanonicalSurface", _ => holder);
        return new(document, _native.Bindings, _native.Bindings, ControlRegistry: registry, Admission: offer);
    }

    public Task<HomeCoreOperationResult<bool>> PrepareToCloseAsync(CancellationToken cancellationToken = default)
    {
        TaskCompletionSource start; Task<HomeCoreOperationResult<bool>> actual;
        lock (_gate)
        {
            if (_revoked) return Task.FromResult(new HomeCoreOperationResult<bool>(false, "PermissionDenied", "The Assistants context has changed."));
            _preparations.RemoveAll(task => task.IsCompletedSuccessfully);
            if (_preparations.Count >= 64) throw new InvalidOperationException("Inspect retained Assistants preparation failures before new requests.");
            start = new(TaskCreationOptions.RunContinuationsAsynchronously);
            actual = PrepareOriginalAsync(start.Task, cancellationToken); _preparations.Add(actual);
        }
        start.TrySetResult(); return actual;
    }
    private async Task<HomeCoreOperationResult<bool>> PrepareOriginalAsync(Task start, CancellationToken token)
    {
        await start; var invocation = new Invocation(); var previous = _current.Value; _current.Value = invocation;
        try
        {
            AssistantsNativeCuiSurface? native;
            lock (_gate) { if (_revoked) return new(false, "PermissionDenied", "The Assistants context has changed."); native = _native; }
            if (native is null) return new(true, "PresentationPrepared", "The setup surface has no private draft.", true);
            Task<bool>? preparation = null;
            await Source(() => Dispatcher.UIThread.InvokeAsync(() => preparation = Source(() => native.PrepareToCloseAsync(token))).GetTask());
            var prepared = await (preparation ?? throw new InvalidOperationException("The actual canonical preflight Task was not captured."));
            prepared = await Source(() => Dispatcher.UIThread.InvokeAsync(() => Invoke(() =>
            {
                lock (_gate) return prepared && !_revoked && ReferenceEquals(native, _native) && native.IsOriginalClosePrepared;
            })).GetTask());
            return prepared ? new(true, "PresentationPrepared", "Actual Assistants drafts were prepared.", true)
                : new(false, "UnsavedChanges", "Save or review the actual Assistants draft before leaving.");
        }
        finally { invocation.Live = false; _current.Value = previous; }
    }

    public void RevokePrivateContext()
    {
        lock (_gate) { if (_revocationRequested) return; _revocationRequested = true; _revoked = true; }
        _bootstrap.RevokePrivateContext();
        try { Invoke(() => { _acquisition?.RevokePrivateContext(); return true; }); } catch (Exception error) { Record(error); }
        try { Invoke(() => { _native?.RequestRetirement(); return true; }); } catch (Exception error) { Record(error); }
        Exception[] errors; lock (_gate) errors = _failures.ToArray();
        if (errors.Length != 0) throw new AggregateException("Assistants private revocation failed.", errors);
    }
    public void DemandExternalOriginalRetirementJoin()
    {
        if (_physicalSources?.ContainsKey(this) == true || _current.Value is { Live: true })
            throw new InvalidOperationException("An Assistants browser source must return before its owning retirement join.");
        _native?.DemandExternalOriginalRetirementJoin(); _acquisition?.DemandExternalOriginalRetirementJoin();
    }
    public Task CloseAndDrainAsync()
    {
        DemandExternalOriginalRetirementJoin();
        TaskCompletionSource start; Task actual;
        lock (_gate)
        {
            if (_close is not null) return _close;
            _revoked = true;
            start = new(TaskCreationOptions.RunContinuationsAsynchronously); actual = DrainAsync(start.Task); _close = actual;
        }
        try { RevokePrivateContext(); } catch { /* Exact revocation failures are retained for the SAME close. */ }
        start.TrySetResult(); return actual;
    }
    public ValueTask DisposeAsync() => new(CloseAndDrainAsync());
    private async Task DrainAsync(Task start)
    {
        await start; var errors = new List<Exception>(); Task[] opens, preparations;
        lock (_gate) { opens = _opens.ToArray(); preparations = _preparations.ToArray(); }
        foreach (var actual in opens) await Join(actual, errors);
        foreach (var actual in preparations) await Join(actual, errors);
        Task? initialization; lock (_gate) initialization = _initialization;
        if (initialization is not null) await Join(initialization, errors);
        Task[] sources; lock (_gate) sources = _sources.ToArray();
        foreach (var actual in sources) await Join(actual, errors);
        Task? nativeClose = null, acquisitionClose = null;
        try { if (_native is not null) nativeClose = Invoke(_native.CloseAndDrainAsync); } catch (Exception error) { Add(errors, error); }
        if (nativeClose is not null) await Join(nativeClose, errors);
        try { if (_acquisition is not null) acquisitionClose = Invoke(_acquisition.CloseAndDrainAsync); } catch (Exception error) { Add(errors, error); }
        if (acquisitionClose is not null) await Join(acquisitionClose, errors);
        lock (_gate) foreach (var error in _failures) Add(errors, error);
        if (errors.Count != 0) throw new AggregateException("Actual Assistants browser originals failed to drain.", errors);
    }
    private sealed class Offer(AssistantsBrowserFeature owner, long generation, HomeFeatureViewState state, CancellationToken token) : IBrowserPresentationAdmission
    {
        private bool _accepted, _rejected;
        private ContentControl? _previous;
        public HomeFeatureViewState State { get; } = state;
        public ContentControl? Mount;
        public bool CanAccept => !_accepted && !_rejected && !owner._revoked && generation == owner._generation && !token.IsCancellationRequested
            && (owner._native is null || (owner._native.OriginalInitialization?.IsCompletedSuccessfully == true && !owner._native.IsRetiring));
        public void Accept() => owner.Invoke(() =>
        {
            if (!CanAccept) throw new InvalidOperationException("The Assistants context changed before presentation.");
            if (owner._native is { } native)
            {
                var mount = Mount ?? throw new InvalidOperationException("The actual candidate mount was not acquired.");
                _previous = owner._mount;
                try
                {
                    if (_previous is not null) _previous.Content = null;
                    if (!CanAccept) throw new UnauthorizedAccessException("The Assistants context changed during actual control transfer.");
                    mount.Content = native;
                    if (!CanAccept) throw new UnauthorizedAccessException("The Assistants context changed during actual control publication.");
                    owner._mount = mount;
                }
                catch (Exception cause)
                {
                    owner.Record(cause);
                    try
                    {
                        if (ReferenceEquals(mount.Content, native)) mount.Content = null;
                        if (_previous is not null && !owner._revoked) _previous.Content = native;
                        owner._mount = _previous;
                    }
                    catch (Exception cleanup) { owner.Record(cleanup); throw new AggregateException("Actual Assistants control transfer and rollback failed.", cause, cleanup); }
                    throw;
                }
            }
            _accepted = true; return true;
        });
        public void Reject() => owner.Invoke(() =>
        {
            if (_rejected) return true;
            if (_accepted && owner._native is { } native && ReferenceEquals(owner._mount, Mount))
            {
                Mount!.Content = null;
                if (_previous is not null && !owner._revoked) _previous.Content = native;
                owner._mount = _previous;
            }
            _rejected = true; return true;
        });
    }
    private T Invoke<T>(Func<T> callback)
    {
        var sources = _physicalSources ??= []; sources[this] = sources.GetValueOrDefault(this) + 1;
        try { return callback(); } finally { if (sources[this] == 1) sources.Remove(this); else sources[this]--; }
    }
    private T Source<T>(Func<T> factory) where T : Task
    { var actual = Invoke(factory) ?? throw new InvalidOperationException("The actual Assistants source returned no Task."); lock (_gate) { _sources.RemoveAll(task => task.IsCompletedSuccessfully); _sources.Add(actual); } return actual; }
    private void Record(Exception error) { lock (_gate) Add(_failures, error); }
    private static async Task Join(Task actual, List<Exception> errors)
    { try { await actual; } catch (Exception error) { foreach (var cause in actual.Exception?.InnerExceptions ?? new[] { error }.AsEnumerable()) Add(errors, cause); } }
    private static void Add(List<Exception> errors, Exception error)
    {
        if (!errors.Any(known => ReferenceEquals(known, error))) errors.Add(error);
        if (error is AggregateException group && group.InnerExceptions.Count != 0)
            foreach (var cause in group.InnerExceptions) Add(errors, cause);
    }
}
