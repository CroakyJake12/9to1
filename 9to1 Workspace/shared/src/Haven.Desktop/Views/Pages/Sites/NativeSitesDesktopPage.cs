#if !ANDROID
using Avalonia.Controls;
using Avalonia.Threading;
using CakeOS.Cui.Runtime;
using Haven.Application;
using Haven.Desktop.Services;
using HavenOS.Apps.Sites.Application;
using HavenOS.Apps.Sites.NativeUI;
using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;
using Microsoft.Extensions.DependencyInjection;

namespace Haven.Desktop.Views.Pages.Sites;

/// <summary>A native tab owner borrowing the SAME started Windows Home and Sites session.
/// The maintained Sites surface/session own authoring; this page owns only mounting,
/// current native publication and joining its actual view work before Home retires.</summary>
internal sealed class NativeSitesDesktopPage : UserControl,
    IDesktopOriginalRetirementParticipant, IDesktopOriginalRetirementJoinGuard
{
    private readonly IServiceProvider _provider;
    private readonly HomeNativeWindowsComposition _home;
    private readonly SiteNativeAuthoringSession _session;
    private readonly CancellationToken _appLifetime;
    private readonly CancellationTokenSource _window;
    private readonly DesktopOriginalWorkLifetime _work;
    private readonly OriginalReadiness _readiness;
    private SitesNativeAuthoringSurface? _surface;
    private AuthenticatedResourceActor? _actor;
    private CuiSceneAvailability? _lastAvailability;
    private bool _initialized;

    private NativeSitesDesktopPage(IServiceProvider provider, HomeNativeWindowsComposition home,
        SiteNativeAuthoringSession session, CancellationToken appLifetime, CancellationToken windowLifetime)
    {
        _provider = provider; _home = home; _session = session; _appLifetime = appLifetime;
        _window = CancellationTokenSource.CreateLinkedTokenSource(appLifetime, windowLifetime);
        _work = new(StopOriginalAsync, CloseOriginalViewAsync);
        _readiness = new(this);
    }

    internal static NativeSitesDesktopPage BindOriginal(IServiceProvider provider,
        HomeNativeWindowsComposition home, CancellationToken appLifetime, CancellationToken windowLifetime)
    {
        Dispatcher.UIThread.VerifyAccess();
        WindowsHomeSameProcessRuntimeObservation.DemandOriginalBinding(provider, home);
        appLifetime.ThrowIfCancellationRequested(); windowLifetime.ThrowIfCancellationRequested();
        if (!appLifetime.CanBeCanceled || !windowLifetime.CanBeCanceled)
            throw new ArgumentException("Retain the actual App and native window work lifetimes.");
        var session = provider.GetRequiredService<SiteNativeAuthoringSession>();
        var authority = provider.GetRequiredService<ISiteNativeWorkspaceAuthority>();
        if (!ReferenceEquals(session, home.Services.GetService(typeof(SiteNativeAuthoringSession))) ||
            !ReferenceEquals(authority, home.Services.GetService(typeof(ISiteNativeWorkspaceAuthority))) ||
            !ReferenceEquals(provider.GetRequiredService<HomeLocalProfileIdentity>(), home.Profiles) ||
            !ReferenceEquals(provider.GetRequiredService<ResourceAuthorizationService>(), home.Resources) ||
            !ReferenceEquals(provider.GetRequiredService<HomeResourceOperationBroker>(), home.Broker))
            throw new UnauthorizedAccessException("Retain the SAME Home/Files-backed Sites composition.");
        return new(provider, home, session, appLifetime, windowLifetime);
    }

    internal bool IsOriginalComposition(IServiceProvider provider, HomeNativeWindowsComposition home) =>
        ReferenceEquals(_provider, provider) && ReferenceEquals(_home, home);

    internal Task InitializeAsync(CancellationToken caller) => _work.RunAsync(async original =>
    {
        Dispatcher.UIThread.VerifyAccess();
        using var phase = CloudflareOriginalExecutionGuard.EnterOriginal(this);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(caller, original.Token, _window.Token);
        var token = linked.Token;
        if (_initialized) throw new InvalidOperationException("Retain the original Sites mount once.");
        _initialized = true;
        _actor = await original.AwaitAsync(ReadActualActorAsync(token))
            ?? throw new UnauthorizedAccessException("The original Windows Sites actor is unavailable.");
        await RequireReadyAsync(original, token);
        // Capture the actual product before any CUI/native callback or await.
        var surface = _surface = new SitesNativeAuthoringSurface(_session, _actor, _readiness, _window.Token);
        await original.AwaitAsync(surface.InitializeAsync(token));
        await original.AwaitAsync(surface.RevalidateOriginalOwnerAsync(token));
        await RequireReadyAsync(original, token);
        DemandPublication(surface);
        original.DemandPublication(); Content = surface; DemandPublication(surface);
    });

    internal Task RevalidateAsync(CancellationToken caller) => _work.RunAsync(async original =>
    {
        Dispatcher.UIThread.VerifyAccess();
        using var phase = CloudflareOriginalExecutionGuard.EnterOriginal(this);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(caller, original.Token, _window.Token);
        var surface = _surface ?? throw new InvalidOperationException("The original Sites view is unavailable.");
        await original.AwaitAsync(surface.RevalidateOriginalOwnerAsync(linked.Token));
        await RequireReadyAsync(original, linked.Token);
        DemandPublication(surface); original.DemandPublication();
    });

    internal void PublishOriginalTab(Action callback)
    {
        Dispatcher.UIThread.VerifyAccess();
        ArgumentNullException.ThrowIfNull(callback);
        using var phase = CloudflareOriginalExecutionGuard.EnterOriginal(this);
        _work.RunSynchronous(original =>
        {
            var surface = _surface ?? throw new InvalidOperationException("The original Sites view is unavailable.");
            DemandPublication(surface); original.DemandPublication(); callback();
            DemandPublication(surface); original.DemandPublication();
        });
    }

    private void DemandAlive()
    {
        _window.Token.ThrowIfCancellationRequested();
        _work.DemandAdmission();
        WindowsHomeSameProcessRuntimeObservation.DemandOriginalBinding(_provider, _home);
        if (_home.OriginalCloseTask is not null || _home.OriginalProcessRetirementRequestTask is not null)
            throw new ObjectDisposedException("The original Windows Home is retiring.");
    }

    private void DemandPublication(SitesNativeAuthoringSurface surface)
    {
        DemandAlive();
        if (!ReferenceEquals(surface, _surface) || _actor is null || _lastAvailability is null)
            throw new UnauthorizedAccessException("Retain the actual Sites view and current Home observation.");
        var current = WindowsHomeSameProcessRuntimeObservation.RevalidateBeforePublication(
            _provider, _home, _lastAvailability);
        if (current.State != CuiSceneAvailabilityState.Ready)
            throw new UnauthorizedAccessException(current.Message);
        surface.CheckOriginalPublicationAlive(); DemandAlive();
    }

    private Task<AuthenticatedResourceActor?> ReadActualActorAsync(CancellationToken token)
    {
        var originals = new CloudflareOriginalTaskLedger(); originals.BindOriginalOwner(this);
        originals.BindOriginalCallerCallback(body => CloudflareOriginalExecutionGuard.InvokeOriginal(_home,
            () => { DemandAlive(); token.ThrowIfCancellationRequested(); body(); return true; }));
        return originals.RunToOriginalSettlementAsync(async () =>
        {
            using var phase = CloudflareOriginalExecutionGuard.EnterOriginal(this);
            using var homePhase = CloudflareOriginalExecutionGuard.EnterOriginal(_home);
            void Source(Action body) => originals.Invoke(() =>
            { DemandAlive(); token.ThrowIfCancellationRequested(); body(); return true; });
            void Retain(Task actual) { _ = originals.Track(actual); }
            var actor = await originals.AwaitAsync(originals.Invoke(() =>
                _home.Profiles.GetCurrentAsync(Source, Retain, token).AsTask()));
            DemandAlive(); token.ThrowIfCancellationRequested(); return actor;
        });
    }

    private Task<CuiSceneAvailability> CheckReadyAsync(CancellationToken caller) =>
        _work.RunAsync(async original =>
        {
            using var phase = CloudflareOriginalExecutionGuard.EnterOriginal(this);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(caller, original.Token, _window.Token);
            var token = linked.Token; DemandAlive();
            if (_actor is null || await original.AwaitAsync(ReadActualActorAsync(token)) != _actor)
                throw new UnauthorizedAccessException("The original Sites actor changed.");
            void Source(Action body) => CloudflareOriginalExecutionGuard.InvokeOriginal(this,
                () => { DemandAlive(); token.ThrowIfCancellationRequested(); body(); return true; });
            var observed = await original.AwaitAsync(WindowsHomeSameProcessRuntimeObservation.CheckAsync(
                _provider, _home, _appLifetime, _window.Token, Source, DemandAlive, token));
            if (await original.AwaitAsync(ReadActualActorAsync(token)) != _actor)
                throw new UnauthorizedAccessException("The original Sites actor changed during Home observation.");
            DemandAlive(); original.DemandPublication();
            return WindowsHomeSameProcessRuntimeObservation.RevalidateBeforePublication(_provider, _home, observed);
        });

    private async Task RequireReadyAsync(DesktopOriginalWorkLifetime.Original original, CancellationToken token)
    {
        var current = await original.AwaitAsync(CheckReadyAsync(token));
        if (current.State != CuiSceneAvailabilityState.Ready) throw new UnauthorizedAccessException(current.Message);
        _lastAvailability = current; DemandAlive(); original.DemandPublication();
    }

    public void DemandExternalOriginalRetirementJoin()
    {
        CloudflareOriginalExecutionGuard.DemandExternalJoin(this);
        _work.DemandExternalClose();
        _surface?.DemandExternalOriginalRetirementJoin();
    }

    public void RequestRetirement() => _work.RequestRetirement();
    public Task CloseAndDrainAsync()
    {
        DemandExternalOriginalRetirementJoin();
        return _work.CloseAndDrainAsync();
    }

    private Task StopOriginalAsync()
    {
        _window.Cancel(); return Task.CompletedTask;
    }

    private async Task CloseOriginalViewAsync()
    {
        var failures = new List<Exception>(); Task? actual = null;
        if (_surface is { } surface)
        {
            try { actual = surface.CloseAndDrainAsync(); }
            catch (Exception error) { failures.Add(error); }
            // Without an acquired terminal task this page cannot claim the
            // original view drained or destroy the owner's remaining custody.
            if (actual is null)
                throw new AggregateException("Original Sites view close was not acquired; retain its owner.", failures);
            if (actual is not null)
                try { await actual; }
                catch (Exception error) { failures.Add(actual.IsFaulted ? actual.Exception! : error); }
        }
        try
        {
            if (Dispatcher.UIThread.CheckAccess()) Content = null;
            else await Dispatcher.UIThread.InvokeAsync(() => Content = null);
        }
        catch (Exception error) { failures.Add(error); }
        try { _window.Dispose(); } catch (Exception error) { failures.Add(error); }
        // Home/provider/session remain borrowed. Retain every actual view/cleanup cause.
        if (failures.Count != 0) throw new AggregateException("Original Sites native page close retained failures.", failures);
    }

    private sealed class OriginalReadiness(NativeSitesDesktopPage owner) : ICuiSceneReadiness
    {
        public ValueTask<CuiSceneAvailability> CheckAsync(CancellationToken token) => new(owner.CheckReadyAsync(token));
    }
}
#endif
