using System.Runtime.ExceptionServices;
using Avalonia.Controls;
using Avalonia.Threading;
using CakeOS.Cui.Runtime;
using Haven.Application;
using Haven.Desktop.Views.Shell;
using HavenOS.Files.NativeHost;
using HavenOS.Files.NativeUI;
using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;
using Microsoft.Extensions.DependencyInjection;

namespace Haven.Desktop.Services;

/// <summary>Native host-only route over the SAME owning Home graph. This borrows the provider
/// and original startup connection; it never creates/disposes either or installs/grants an app.</summary>
internal sealed class NativeFilesDesktopRoute : IAsyncDisposable
{
    private readonly IServiceProvider _originalProvider;
    private readonly IHomeNativeStartupSession _originalStartup;
    private readonly HomeNativeWindowsComposition? _sameProcessHome;
    private readonly HashSet<Task> _originalSameProcessChecks = [];
    private readonly CancellationToken _originalConnectionLifetime;
    private readonly CancellationTokenSource _windowLifetime;
    private readonly HomeLocalProfileIdentity _profiles;
    private readonly IAuthenticatedResourceActorSource _actors;
    private readonly FilesNativeBrowserService _browser;
    private readonly FilesCompatibilityPackageOpenCoordinator? _packages;
    private readonly OriginalReadiness _readiness;
    private readonly object _sync = new();
    private Task<FilesNativeBrowserSurface>? _open;
    private Task? _close;
    private FilesNativeBrowserSurface? _surface;
    private AuthenticatedResourceActor? _originalActor;
    private bool _closing;
    private readonly HashSet<Task> _originalPublications = [];
    private readonly List<Exception> _publicationFailures = [];

    private NativeFilesDesktopRoute(IServiceProvider provider, IHomeNativeStartupSession startup,
        CancellationToken originalConnectionLifetime, CancellationToken originalWindowLifetime)
    {
        _originalProvider = provider; _originalStartup = startup;
        _originalConnectionLifetime = originalConnectionLifetime;
        _profiles = provider.GetRequiredService<HomeLocalProfileIdentity>();
        _actors = provider.GetRequiredService<IAuthenticatedResourceActorSource>();
        var store = provider.GetRequiredService<IHomeCoreStateStore>() as FileHomeCoreStateStore;
        var ownership = provider.GetRequiredService<IResourceStoreOwnershipAuthority>()
            as HomeResourceStoreOwnershipAuthority;
        var resources = provider.GetRequiredService<ResourceAuthorizationService>();
        var permissions = provider.GetRequiredService<HomePermissionTrustService>();
        var manualBroker = provider.GetRequiredService<HomeResourceOperationBroker>();
        var workspaces = provider.GetRequiredService<NativeFilesWorkspaceService>();
        var authority = provider.GetRequiredService<NativeFilesWorkspaceAuthority>();
        _browser = provider.GetRequiredService<FilesNativeBrowserService>();
        var content = provider.GetRequiredService<Haven.Application.Compatibility.ICompatibilityPackageContentSource>();
        if (!ReferenceEquals(_actors, _profiles) || !HomeLocalReadComposition.IsBound(store, _profiles, ownership)
            || !resources.IsBoundToActorSource(_actors)
            || !manualBroker.IsBoundToOriginalComposition(resources, permissions)
            || !workspaces.IsBoundToOriginalLocalComposition(_profiles, ownership!)
            || !authority.IsBoundToOriginalComposition(workspaces, _profiles, ownership!)
            || !_browser.IsBoundToOriginalComposition(authority, _actors, resources, content))
            throw new UnauthorizedAccessException("The owning Files/Home service composition is unavailable.");
        // Inspection is enabled only by an actual registered host handler. No default execution,
        // installation, path opener or private compatibility service is supplied by this route.
        var handler = provider.GetService<Haven.Application.Compatibility.ICompatibilityPackageOpenHandler>();
        _packages = handler is null ? null : new FilesCompatibilityPackageOpenCoordinator(content, _actors, handler);
        _readiness = new OriginalReadiness(this);
        _windowLifetime = CancellationTokenSource.CreateLinkedTokenSource(originalWindowLifetime,
            originalConnectionLifetime);
    }

    internal static NativeFilesDesktopRoute BindOriginal(IServiceProvider originalHomeProvider,
        IHomeNativeStartupSession originalWindowsStartup, CancellationToken originalConnectionLifetime,
        CancellationToken originalWindowLifetime)
    {
        ArgumentNullException.ThrowIfNull(originalHomeProvider);
        ArgumentNullException.ThrowIfNull(originalWindowsStartup);
        originalConnectionLifetime.ThrowIfCancellationRequested();
        originalWindowLifetime.ThrowIfCancellationRequested();
        // Supplied ONLY by the original native owning composition, never a request/DTO or default DI.
        // Checking cancelability is no substitute for that platform provenance.
        if (!originalConnectionLifetime.CanBeCanceled || !originalWindowLifetime.CanBeCanceled)
            throw new UnauthorizedAccessException("Retain the original native connection and window lifetimes.");
        return new(originalHomeProvider, originalWindowsStartup, originalConnectionLifetime,
            originalWindowLifetime);
    }

    // This separate factory is called only by the trusted owning App over its actual
    // Home/provider. The installed-IPC factory and its original checks are unchanged.
    internal static NativeFilesDesktopRoute BindOriginalSameProcess(IServiceProvider originalHomeProvider,
        HomeNativeWindowsComposition sameHome, CancellationToken originalAppLifetime,
        CancellationToken originalWindowLifetime)
    {
        WindowsHomeSameProcessRuntimeObservation.DemandOriginalBinding(originalHomeProvider, sameHome);
        originalAppLifetime.ThrowIfCancellationRequested(); originalWindowLifetime.ThrowIfCancellationRequested();
        if (!originalAppLifetime.CanBeCanceled || !originalWindowLifetime.CanBeCanceled)
            throw new ArgumentException("Retain the actual App and native window work lifetimes.");
        return new(originalHomeProvider, sameHome, originalAppLifetime, originalWindowLifetime);
    }

    private NativeFilesDesktopRoute(IServiceProvider provider, HomeNativeWindowsComposition sameHome,
        CancellationToken originalAppLifetime, CancellationToken originalWindowLifetime)
        : this(provider, (IHomeNativeStartupSession)null!, originalAppLifetime, originalWindowLifetime)
    {
        // No installed startup is fabricated. Only the distinct same-process branch
        // below can read this route's real process-owned Home runtime.
        _sameProcessHome = sameHome;
    }

    internal bool IsOriginalProvider(IServiceProvider candidate) => ReferenceEquals(_originalProvider, candidate);

    // Borrow the SAME live observation source; this reference is neither Ready nor an action grant.
    internal ICuiSceneReadiness BorrowOriginalHomeReadinessForMetadata()
    {
        Dispatcher.UIThread.VerifyAccess();
        lock (_sync)
        {
            ThrowIfRetired();
            return _readiness;
        }
    }

    internal Task<FilesNativeBrowserSurface> OpenAsync(CancellationToken cancellationToken)
    {
        Dispatcher.UIThread.VerifyAccess();
        cancellationToken.ThrowIfCancellationRequested();
        lock (_sync)
        {
            ThrowIfRetired();
            if (_open is { IsCompleted: false }) return _open;
            // A completed native surface is never returned as a completed startup/read receipt.
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _open = RunOpenAttemptAsync(start.Task, cancellationToken);
            start.SetResult();
            return _open;
        }
    }

    private async Task<FilesNativeBrowserSurface> RunOpenAttemptAsync(Task start, CancellationToken caller)
    {
        using var phase = _sameProcessHome is null ? null : CloudflareOriginalExecutionGuard.EnterOriginal(this);
        try { return await OpenOriginalAsync(start, caller); }
        catch
        {
            // Retain this exact failed Task for close; a failed scope cannot restart or replay bytes.
            lock (_sync) _closing = true;
            throw;
        }
    }

    private async Task<FilesNativeBrowserSurface> OpenOriginalAsync(Task start, CancellationToken caller)
    {
        await start;
        using var token = CancellationTokenSource.CreateLinkedTokenSource(caller, _windowLifetime.Token);
        var actor = await ReadOriginalRouteActorAsync(token.Token)
            ?? throw new UnauthorizedAccessException("The original Files native actor is unavailable.");
        lock (_sync)
        {
            ThrowIfRetired();
            if (_originalActor is not null && _originalActor != actor)
                throw new UnauthorizedAccessException("The original Files native actor changed.");
            _originalActor = actor;
        }
        await RequireReadyAsync(token.Token);
        if (_surface is { } retained)
        {
            await retained.RefreshAsync(token.Token);
            await RequireReadyAsync(token.Token);
            await retained.RevalidateOriginalOwnerAsync(token.Token);
            ThrowIfRetired(); token.Token.ThrowIfCancellationRequested();
            retained.CheckOriginalPublicationAlive();
            return retained;
        }
        FilesNativeBrowserSurface? candidate = null;
        Exception? primary = null;
        List<Exception> cleanup = [];
        try
        {
            candidate = new(_browser, _packages, actor, _readiness, _windowLifetime.Token);
            await candidate.InitializeAsync(token.Token);
            await RequireReadyAsync(token.Token);
            await candidate.RevalidateOriginalOwnerAsync(token.Token);
            candidate.CheckOriginalPublicationAlive();
            lock (_sync)
            {
                ThrowIfRetired(); token.Token.ThrowIfCancellationRequested();
                _surface = candidate;
            }
            return candidate;
        }
        catch (Exception error) { primary = error; }
        if (candidate is not null)
            try { await candidate.CloseAndDrainAsync(); }
            catch (Exception error) { Add(cleanup, error); }
        Rethrow(primary, cleanup, "Original native Files opening and cleanup failed.");
        throw new InvalidOperationException("Unreachable native Files opening result.");
    }

    internal Task AdmitOriginalShellInitializationAsync(CancellationToken cancellationToken)
    {
        Dispatcher.UIThread.VerifyAccess();
        lock (_sync)
        {
            ThrowIfRetired(); cancellationToken.ThrowIfCancellationRequested();
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var original = AdmitOriginalShellInitializationCoreAsync(start.Task, cancellationToken);
            _originalPublications.RemoveWhere(row => row.IsCompleted);
            _originalPublications.Add(original);
            start.SetResult();
            return original;
        }
    }

    private async Task AdmitOriginalShellInitializationCoreAsync(Task start, CancellationToken token)
    {
        await start;
        using var phase = _sameProcessHome is null ? null : CloudflareOriginalExecutionGuard.EnterOriginal(this);
        try
        {
            var actor = await ReadOriginalRouteActorAsync(token)
                ?? throw new UnauthorizedAccessException("The original Files actor is unavailable.");
            lock (_sync)
            {
                ThrowIfRetired();
                if (_originalActor is not null && _originalActor != actor)
                    throw new UnauthorizedAccessException("The original Files actor changed.");
                _originalActor = actor;
            }
            await RequireReadyAsync(token);
            ThrowIfRetired(); token.ThrowIfCancellationRequested();
        }
        catch (Exception error)
        {
            lock (_sync) { Add(_publicationFailures, error); _closing = true; }
            throw;
        }
    }

    internal Task RevalidateBeforePublicationAsync(FilesNativeBrowserSurface originalSurface,
        CancellationToken cancellationToken)
    {
        Dispatcher.UIThread.VerifyAccess();
        lock (_sync)
        {
            ThrowIfRetired(); cancellationToken.ThrowIfCancellationRequested();
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var original = RevalidateOriginalAsync(start.Task, originalSurface, cancellationToken);
            _originalPublications.RemoveWhere(row => row.IsCompleted);
            _originalPublications.Add(original);
            start.SetResult();
            return original;
        }
    }

    private async Task RevalidateOriginalAsync(Task start, FilesNativeBrowserSurface originalSurface,
        CancellationToken cancellationToken)
    {
        await start;
        using var phase = _sameProcessHome is null ? null : CloudflareOriginalExecutionGuard.EnterOriginal(this);
        try
        {
            if (!ReferenceEquals(originalSurface, _surface))
                throw new UnauthorizedAccessException("Retain this route's original Files view.");
            await originalSurface.RefreshAsync(cancellationToken);
            await RequireReadyAsync(cancellationToken);
            await originalSurface.RevalidateOriginalOwnerAsync(cancellationToken);
            ThrowIfRetired(); cancellationToken.ThrowIfCancellationRequested();
            originalSurface.CheckOriginalPublicationAlive();
            if (!ReferenceEquals(originalSurface, _surface))
                throw new UnauthorizedAccessException("The original native Files view retired.");
        }
        catch (Exception error)
        {
            lock (_sync) { Add(_publicationFailures, error); _closing = true; }
            throw;
        }
    }

    internal void PublishOriginalTabMutation(FilesNativeBrowserSurface originalSurface, Action originalPublication)
    {
        Dispatcher.UIThread.VerifyAccess();
        ArgumentNullException.ThrowIfNull(originalPublication);
        using var phase = _sameProcessHome is null ? null : CloudflareOriginalExecutionGuard.EnterOriginal(this);
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_sync)
        {
            CheckPublicationCurrent(originalSurface);
            _originalPublications.RemoveWhere(row => row.IsCompletedSuccessfully);
            _originalPublications.Add(completion.Task);
        }
        try
        {
            originalPublication();
            CheckPublicationCurrent(originalSurface);
            completion.SetResult();
        }
        catch (Exception error)
        {
            lock (_sync) { Add(_publicationFailures, error); _closing = true; }
            completion.SetException(error);
            throw;
        }
    }

    internal void CheckPublicationCurrent(FilesNativeBrowserSurface originalSurface)
    {
        Dispatcher.UIThread.VerifyAccess();
        ThrowIfRetired();
        if (!ReferenceEquals(originalSurface, _surface))
            throw new UnauthorizedAccessException("Retain the original native Files view at publication.");
        originalSurface.CheckOriginalPublicationAlive();
        ThrowIfRetired();
    }

    private async ValueTask<CuiSceneAvailability> CheckReadyAsync(CancellationToken token)
    {
        if (_sameProcessHome is not null) return await CheckSameProcessReadyAsync(token);
        token.ThrowIfCancellationRequested(); ThrowIfRetired();
        var actor = _originalActor;
        if (actor is null || await _actors.GetCurrentAsync(token) != actor)
            throw new UnauthorizedAccessException("The original native Files actor retired.");
        // This is another live check on the SAME original connection, not cached Ready metadata.
        var observed = await _originalStartup.CheckAsync(token);
        token.ThrowIfCancellationRequested(); ThrowIfRetired();
        if (await _actors.GetCurrentAsync(token) != actor)
            throw new UnauthorizedAccessException("The original native Files actor changed during Home readiness.");
        token.ThrowIfCancellationRequested(); ThrowIfRetired();
        return new(observed.CanStartNormally ? CuiSceneAvailabilityState.Ready
            : CuiSceneAvailabilityState.Unavailable, observed.Code, observed.Message);
    }

    private ValueTask<AuthenticatedResourceActor?> ReadOriginalRouteActorAsync(CancellationToken token) =>
        _sameProcessHome is null ? _actors.GetCurrentAsync(token) : new(ReadOriginalSameProcessActorAsync(token));

    private Task<AuthenticatedResourceActor?> ReadOriginalSameProcessActorAsync(CancellationToken token)
    {
        var home = _sameProcessHome ?? throw new InvalidOperationException("The actual same-process Home is absent.");
        var originals = new CloudflareOriginalTaskLedger(); originals.BindOriginalOwner(this);
        originals.BindOriginalCallerCallback(body => CloudflareOriginalExecutionGuard.InvokeOriginal(home, () =>
        { token.ThrowIfCancellationRequested(); ThrowIfRetired(); body(); return true; }));
        return originals.RunToOriginalSettlementAsync(async () =>
        {
            using var phase = CloudflareOriginalExecutionGuard.EnterOriginal(this);
            using var homePhase = CloudflareOriginalExecutionGuard.EnterOriginal(home);
            void OwnSource(Action body) => originals.Invoke(() => { body(); return true; });
            void Retain(Task actual) { _ = originals.Track(actual); }
            var actor = await originals.AwaitAsync(originals.Invoke(() =>
                home.Profiles.GetCurrentAsync(OwnSource, Retain, token).AsTask()));
            token.ThrowIfCancellationRequested(); ThrowIfRetired();
            return actor;
        });
    }

    private Task<CuiSceneAvailability> CheckSameProcessReadyAsync(CancellationToken token)
    {
        lock (_sync)
        {
            ThrowIfRetired(); token.ThrowIfCancellationRequested();
            _originalSameProcessChecks.RemoveWhere(actual => actual.IsCompletedSuccessfully);
            if (_originalSameProcessChecks.Count >= 128)
                throw new InvalidOperationException("The original same-process Files checks require external route retirement.");
            var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var actual = CheckOriginalSameProcessReadyAsync(gate.Task, token);
            _originalSameProcessChecks.Add(actual); gate.SetResult();
            return actual;
        }
    }

    private async Task<CuiSceneAvailability> CheckOriginalSameProcessReadyAsync(Task gate, CancellationToken caller)
    {
        await gate;
        using var phase = CloudflareOriginalExecutionGuard.EnterOriginal(this);
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(caller, _windowLifetime.Token);
        var token = lifetime.Token;
        token.ThrowIfCancellationRequested(); ThrowIfRetired();
        var actor = _originalActor;
        if (actor is null || await ReadOriginalRouteActorAsync(token) != actor)
            throw new UnauthorizedAccessException("The original same-process Files actor retired.");
        void OwnSource(Action body) => CloudflareOriginalExecutionGuard.InvokeOriginal(this, () =>
        { token.ThrowIfCancellationRequested(); ThrowIfRetired(); body(); return true; });
        var observed = await WindowsHomeSameProcessRuntimeObservation.CheckAsync(_originalProvider,
            _sameProcessHome!, _originalConnectionLifetime, _windowLifetime.Token, OwnSource, ThrowIfRetired, token);
        token.ThrowIfCancellationRequested(); ThrowIfRetired();
        if (await ReadOriginalRouteActorAsync(token) != actor)
            throw new UnauthorizedAccessException("The original Files actor changed during same-process Home observation.");
        token.ThrowIfCancellationRequested(); ThrowIfRetired();
        var final = WindowsHomeSameProcessRuntimeObservation.RevalidateBeforePublication(
            _originalProvider, _sameProcessHome!, observed);
        token.ThrowIfCancellationRequested(); ThrowIfRetired();
        return final;
    }

    internal void DemandExternalOriginalRetirementJoin()
    {
        if (_sameProcessHome is not null) CloudflareOriginalExecutionGuard.DemandExternalJoin(this);
    }

    private async Task RequireReadyAsync(CancellationToken token)
    {
        var observed = await CheckReadyAsync(token);
        if (observed.State != CuiSceneAvailabilityState.Ready)
            throw new UnauthorizedAccessException(observed.Message);
    }

    private sealed class OriginalReadiness(NativeFilesDesktopRoute owner) : ICuiSceneReadiness
    {
        public ValueTask<CuiSceneAvailability> CheckAsync(CancellationToken token) => owner.CheckReadyAsync(token);
    }

    internal Task CloseAndDrainAsync()
    {
        DemandExternalOriginalRetirementJoin();
        lock (_sync)
        {
            if (_close is not null) return _close;
            _closing = true;
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _close = CloseOriginalAsync(start.Task, _open, _originalPublications.Concat(_originalSameProcessChecks).ToArray());
            start.SetResult();
            return _close;
        }
    }

    private async Task CloseOriginalAsync(Task start, Task? originalOpen, Task[] originalPublications)
    {
        await start;
        List<Exception> failures = [];
        try { _windowLifetime.Cancel(); } catch (Exception error) { Add(failures, error); }
        // Begin view cancellation before waiting for an opening/refresh that may own its I/O.
        Task? originalSurfaceClose = null;
        try { if (_surface is not null) originalSurfaceClose = _surface.CloseAndDrainAsync(); }
        catch (Exception error) { Add(failures, error); }
        try { if (originalOpen is not null) await originalOpen; }
        catch (Exception error) { Add(failures, error); }
        foreach (var original in originalPublications)
            try { await original; } catch (Exception error) { Add(failures, error); }
        lock (_sync)
            foreach (var error in _publicationFailures) Add(failures, error);
        try { if (originalSurfaceClose is not null) await originalSurfaceClose; }
        catch (Exception error) { Add(failures, error); }
        // Candidate opening cleanup is itself retained by originalOpen when no view was published.
        try { _windowLifetime.Dispose(); } catch (Exception error) { Add(failures, error); }
        Rethrow(null, failures, "Original native Files work and close failed.");
        // Borrowed startup session/provider remain owned by native Home. Its owner drains the
        // session after all routes settle; closing this window never stops the Home service.
    }

    public ValueTask DisposeAsync() => new(CloseAndDrainAsync());
    private void ThrowIfRetired()
    {
        lock (_sync) ObjectDisposedException.ThrowIf(_closing, this);
        _originalConnectionLifetime.ThrowIfCancellationRequested();
        _windowLifetime.Token.ThrowIfCancellationRequested();
    }
    private static void Add(List<Exception> errors, Exception error)
    { if (!errors.Any(row => ReferenceEquals(row, error))) errors.Add(error); }
    private static void Rethrow(Exception? primary, List<Exception> additional, string message)
    {
        List<Exception> errors = [];
        if (primary is not null) Add(errors, primary);
        foreach (var error in additional) Add(errors, error);
        if (errors.Count == 1) ExceptionDispatchInfo.Capture(errors[0]).Throw();
        if (errors.Count > 1) throw new AggregateException(message, errors);
    }
}
