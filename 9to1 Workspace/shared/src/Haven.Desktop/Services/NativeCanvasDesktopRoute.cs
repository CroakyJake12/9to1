using CakeOS.Cui.Runtime;
using CakeOS.Cui.Themes;
using Haven.Application;
using HavenOS.Apps.Canvas;
using HavenOS.Files.NativeHost;
using HavenOS.Home.Core;

namespace Haven.Desktop.Services;

/// <summary>Typed native presentation seam. Test routes do not establish real Home or Rnote readiness.</summary>
internal interface ICanvasOriginalDesktopRoute
{
    void BindOriginalPresentationOwner(object owner);
    bool IsAlive { get; }
    Task RevalidateAsync(CancellationToken token);
    CanvasHostSurface AcquireOriginalSurface(CuiThemePreferenceContext? theme);
    void DemandPublicationCurrent();
    void RunWithinOriginalSource(Action source);
    Task CloseAndDrainAsync();
}

/// <summary>Same-process Home/Files observer, never a service owner or permission issuer.</summary>
internal sealed class NativeCanvasDesktopRoute : ICanvasOriginalDesktopRoute
{
    private readonly IServiceProvider _provider;
    private readonly HomeNativeWindowsComposition _home;
    private readonly CanvasHomeWindowFactory _factory;
    private readonly string _profile;
    private readonly CancellationToken _appLifetime, _lifetime;
    private readonly object _gate = new();
    private readonly List<Task> _operations = [];
    private object? _presentation;
    private AuthenticatedResourceActor? _actor;
    private CuiSceneAvailability? _observed;
    private volatile bool _retiring;
    private bool _acquired;
    private Task? _close;

    internal NativeCanvasDesktopRoute(IServiceProvider provider, HomeNativeWindowsComposition home,
        NativeFilesWorkspaceService files, NativeFilesWorkspaceAuthority authority, string profile,
        CancellationToken appLifetime, CancellationToken routeLifetime)
    {
        _provider = provider; _home = home; _profile = profile;
        _appLifetime = appLifetime; _lifetime = routeLifetime;
        _factory = new(home, files, authority, appLifetime);
    }
    public void BindOriginalPresentationOwner(object owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        lock (_gate)
        {
            if (_presentation is not null || _operations.Count != 0 || _acquired || _retiring)
                throw new InvalidOperationException("Bind the original Canvas parent once, before source work.");
            _presentation = owner;
        }
    }
    public bool IsAlive { get { lock (_gate) return !_retiring && !_lifetime.IsCancellationRequested && !_appLifetime.IsCancellationRequested; } }
    public void RunWithinOriginalSource(Action source)
    {
        ArgumentNullException.ThrowIfNull(source);
        object parent; lock (_gate) parent = _presentation ?? throw new InvalidOperationException("The original Canvas parent is absent.");
        CloudflareOriginalExecutionGuard.InvokeOriginal(parent, () =>
            CloudflareOriginalExecutionGuard.InvokeOriginal(this, () => { source(); return true; }));
    }
    private CloudflareOriginalTaskLedger Ledger()
    {
        var originals = new CloudflareOriginalTaskLedger();
        originals.BindOriginalOwner(this);
        originals.BindOriginalCallerCallback(RunWithinOriginalSource);
        return originals;
    }
    public Task RevalidateAsync(CancellationToken token)
    {
        lock (_gate)
        {
            DemandAlive();
            if (_presentation is null) throw new InvalidOperationException("The actual Canvas parent has not been bound.");
            _operations.RemoveAll(task => task.IsCompletedSuccessfully);
            if (_operations.Count >= 128) throw new InvalidOperationException("Settle retained Canvas Home failures before more source work.");
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var actual = RevalidateOriginalAsync(start.Task, token); _operations.Add(actual); start.SetResult(); return actual;
        }
    }
    private async Task RevalidateOriginalAsync(Task start, CancellationToken caller)
    {
        await start.ConfigureAwait(false);
        using var phase = CloudflareOriginalExecutionGuard.EnterOriginal(this);
        var originals = Ledger();
        await originals.RunToOriginalSettlementAsync(async () =>
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(caller, _lifetime, _appLifetime);
            var token = linked.Token;
            void Demand() { token.ThrowIfCancellationRequested(); DemandAlive(); }
            void Source(Action body) => originals.Invoke(() => { Demand(); body(); return true; });
            async Task<AuthenticatedResourceActor> ReadActor()
            {
                var actor = await originals.AwaitAsync(originals.Invoke(() => _home.Profiles.GetCurrentAsync(
                    Source, task => { _ = originals.Track(task); }, token).AsTask())).ConfigureAwait(false)
                    ?? throw new UnauthorizedAccessException("The genuine Canvas Home profile is unavailable.");
                Demand();
                if (actor.ProfileId != _profile || actor.AccountId is not null || actor.OrganisationId is not null ||
                    string.IsNullOrWhiteSpace(actor.ActorId) || string.IsNullOrWhiteSpace(actor.AuthenticationRevision))
                    throw new UnauthorizedAccessException("Canvas must retain its actual local Home profile.");
                lock (_gate)
                {
                    if (_actor is not null && _actor != actor) throw new UnauthorizedAccessException("The original Canvas profile changed.");
                    _actor ??= actor;
                }
                return actor;
            }
            Demand(); var actor = await ReadActor().ConfigureAwait(false);
            var observed = await originals.AwaitAsync(originals.Invoke(() =>
                WindowsHomeSameProcessRuntimeObservation.CheckAsync(_provider, _home, _appLifetime,
                    _lifetime, Source, Demand, token))).ConfigureAwait(false);
            if (await ReadActor().ConfigureAwait(false) != actor) throw new UnauthorizedAccessException("The Canvas profile changed before publication.");
            Demand();
            observed = originals.Invoke(() => WindowsHomeSameProcessRuntimeObservation.RevalidateBeforePublication(_provider, _home, observed));
            if (observed.State != CuiSceneAvailabilityState.Ready) throw new InvalidOperationException(observed.Message);
            lock (_gate) _observed = observed;
        }).ConfigureAwait(false);
    }
    public CanvasHostSurface AcquireOriginalSurface(CuiThemePreferenceContext? theme)
    {
        DemandPublicationCurrent();
        CanvasHostSurface? surface = null;
        RunWithinOriginalSource(() =>
        {
            lock (_gate)
            {
                DemandAlive();
                if (_acquired) throw new InvalidOperationException("This Canvas route already acquired its original presentation.");
                _acquired = true;
            }
            surface = _factory.CreateOriginalSurface(_lifetime, RunWithinOriginalSource, theme);
        });
        return surface ?? throw new InvalidOperationException("The original Canvas content was not acquired.");
    }
    public void DemandPublicationCurrent()
    {
        DemandAlive();
        CuiSceneAvailability observed;
        lock (_gate) observed = _observed is { State: CuiSceneAvailabilityState.Ready } current && _actor?.ProfileId == _profile
            ? current : throw new UnauthorizedAccessException("Canvas has no genuine current Home observation.");
        RunWithinOriginalSource(() =>
        {
            if (WindowsHomeSameProcessRuntimeObservation.RevalidateBeforePublication(_provider, _home, observed).State != CuiSceneAvailabilityState.Ready)
                throw new UnauthorizedAccessException("The original Canvas Home services are unavailable.");
        });
        DemandAlive();
    }
    public Task CloseAndDrainAsync()
    {
        CloudflareOriginalExecutionGuard.DemandExternalJoin(this);
        lock (_gate)
        {
            if (_close is not null) return _close;
            _retiring = true;
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _close = DrainOriginalAsync(start.Task); start.SetResult(); return _close;
        }
    }
    private async Task DrainOriginalAsync(Task start)
    {
        await start.ConfigureAwait(false);
        Task[] work; lock (_gate) work = _operations.ToArray();
        var originals = Ledger();
        foreach (var task in work)
            try { await originals.AwaitAsync(task).ConfigureAwait(false); } catch (Exception error) { originals.Capture(task, error); }
        if (originals.OriginalErrors.Count != 0) throw new AggregateException("Original Canvas Home observations did not settle.", originals.OriginalErrors);
        // Home, Files stores and their original global services remain borrowed.
    }
    private void DemandAlive()
    { ObjectDisposedException.ThrowIf(_retiring, this); _appLifetime.ThrowIfCancellationRequested(); _lifetime.ThrowIfCancellationRequested(); }
}
