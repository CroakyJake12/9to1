using Avalonia.Threading;
using CakeOS.Cui.Runtime;
using Haven.Application;
using HavenOS.Files;
using HavenOS.Files.NativeHost;
using HavenOS.Files.NativeUI;

namespace Haven.Desktop.Services;

internal sealed partial class NativeFilesDesktopRoute
{
    private readonly HashSet<FilesNativeBrowserSurface> _originalRegisteredSurfaces = [];

    private async Task DrainOriginalRegisteredSurfacesAsync(List<Exception> failures)
    {
        FilesNativeBrowserSurface[] acquired;
        lock (_sync) acquired = _originalRegisteredSurfaces.ToArray();
        foreach (var original in acquired)
        {
            try
            {
                await original.CloseAndDrainAsync();
                lock (_sync) _originalRegisteredSurfaces.Remove(original);
            }
            catch (Exception error) { Add(failures, error); }
        }
        // Failed originals remain in this route's custody for its full lifetime.
    }

    private Task<CuiSceneAvailability> CheckRegisteredRevealReadyWithinSourceAsync(
        Action<Action> scope, Action<Task> retain, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(retain);
        lock (_sync)
        {
            ThrowIfRetired(); token.ThrowIfCancellationRequested();
            if (_sameProcessHome is null)
                throw new PlatformNotSupportedException("Retain the actual same-process Home source for this Files reveal.");
            if (_originalSameProcessChecks.Count >= 128)
                throw new InvalidOperationException("The original Files checks require external route retirement.");
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var actual = CheckRegisteredRevealReadyCoreAsync(start.Task, scope, retain, token);
            _originalSameProcessChecks.Add(actual);
            start.SetResult();
            return actual;
        }
    }

    private async Task<CuiSceneAvailability> CheckRegisteredRevealReadyCoreAsync(Task start,
        Action<Action> scope, Action<Task> retain, CancellationToken caller)
    {
        await start;
        var home = _sameProcessHome ?? throw new InvalidOperationException("The actual Home source is absent.");
        using var phase = CloudflareOriginalExecutionGuard.EnterOriginal(this);
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(caller, _windowLifetime.Token);
        var token = lifetime.Token;
        var originals = new CloudflareOriginalTaskLedger();
        originals.BindOriginalOwner(this);
        originals.BindOriginalCallerCallback(body => scope(() =>
            CloudflareOriginalExecutionGuard.InvokeOriginal(home, () =>
            { token.ThrowIfCancellationRequested(); ThrowIfRetired(); body(); return true; })));
        return await originals.RunToOriginalSettlementAsync(async () =>
        {
            void OwnSource(Action body) => originals.Invoke(() =>
            { token.ThrowIfCancellationRequested(); ThrowIfRetired(); body(); return true; });
            void Retain(Task actual)
            {
                _ = originals.Track(actual); // Before a borrowed retainer can refuse.
                OwnSource(() => retain(actual));
            }
            async Task<AuthenticatedResourceActor?> ReadActorAsync()
                => await originals.AwaitAsync(originals.Invoke(() =>
                    home.Profiles.GetCurrentAsync(OwnSource, Retain, token).AsTask()));
            token.ThrowIfCancellationRequested(); ThrowIfRetired();
            var actor = _originalActor;
            if (actor is null || await ReadActorAsync() != actor)
                throw new UnauthorizedAccessException("The original Files actor retired before Home readiness.");
            var observed = await originals.AwaitAsync(originals.Invoke(() =>
                WindowsHomeSameProcessRuntimeObservation.CheckAsync(_originalProvider, home,
                    _originalConnectionLifetime, _windowLifetime.Token, OwnSource, ThrowIfRetired, token)));
            token.ThrowIfCancellationRequested(); ThrowIfRetired();
            if (await ReadActorAsync() != actor)
                throw new UnauthorizedAccessException("The original Files actor changed during Home readiness.");
            return originals.Invoke(() =>
            {
                token.ThrowIfCancellationRequested(); ThrowIfRetired();
                var current = WindowsHomeSameProcessRuntimeObservation.RevalidateBeforePublication(
                    _originalProvider, home, observed);
                token.ThrowIfCancellationRequested(); ThrowIfRetired();
                return current;
            });
        });
    }

    internal Task<FilesNativeBrowserSurface> RevealOriginalRegisteredDownloadAsync(
        FilesNativeBrowserService sameBrowser, FilesNativeBrowserPage originalPage,
        HostedItemMetadata originalRow, AuthenticatedResourceActor originalActor,
        Action<Action> scope, Action<Task> retain, CancellationToken token)
    {
        Dispatcher.UIThread.VerifyAccess();
        lock (_sync)
        {
            ThrowIfRetired();
            if (!ReferenceEquals(_browser, sameBrowser))
                throw new UnauthorizedAccessException("The native Files route belongs to another Files source.");
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var predecessor = _open;
            var actual = RevealOriginalRegisteredDownloadCoreAsync(start.Task, predecessor, originalPage,
                originalRow, originalActor, scope, retain, token);
            _open = actual; // Ordinary Open joins this SAME pending acquisition.
            _originalPublications.Add(actual); // Before Home/UI callbacks or any source I/O.
            start.SetResult();
            return actual;
        }
    }

    private async Task<FilesNativeBrowserSurface> RevealOriginalRegisteredDownloadCoreAsync(Task start,
        Task<FilesNativeBrowserSurface>? predecessor, FilesNativeBrowserPage originalPage, HostedItemMetadata originalRow,
        AuthenticatedResourceActor originalActor, Action<Action> scope, Action<Task> retain,
        CancellationToken caller)
    {
        await start;
        using var phase = _sameProcessHome is null ? null : CloudflareOriginalExecutionGuard.EnterOriginal(this);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(caller, _windowLifetime.Token);
        var token = linked.Token;
        FilesNativeBrowserSurface? acquired = null;
        var fresh = false;
        Exception? primary = null;
        List<Exception> cleanup = [];
        try
        {
            // Captured before assigning this driver to _open; never await our own reservation.
            if (predecessor is not null) await predecessor;
            var actor = await ReadOriginalRouteActorAsync(token);
            if (actor != originalActor || actor is null || _originalActor is { } prior && prior != actor)
                throw new UnauthorizedAccessException("The genuine native Files actor changed.");
            _originalActor = actor;
            await RequireReadyAsync(token);
            ThrowIfRetired();
            acquired = _surface;
            if (acquired is null)
            {
                acquired = new(_browser, _packages, actor, _readiness, _windowLifetime.Token);
                fresh = true;
            }
            // Retain the SAME acquired view before its first reveal callback, even
            // when an external retirement began during constructor acquisition.
            lock (_sync) { _originalRegisteredSurfaces.Add(acquired); ThrowIfRetired(); }
            var reveal = acquired.RevealOriginalRegisteredPageWithinSourceAsync(_browser,
                originalPage, originalRow, actor, scope, retain, token);
            lock (_sync) _originalPublications.Add(reveal);
            List<Exception> revealFailures = [];
            try { retain(reveal); }
            catch (Exception error) { Add(revealFailures, error is OperationCanceledException
                ? new AggregateException("The synchronous Files retainer supplied no canceled original Task.", error) : error); }
            try { await reveal; }
            catch (Exception caught)
            {
                if (reveal.Exception is { } group)
                    foreach (var error in group.InnerExceptions) Add(revealFailures, error);
                else Add(revealFailures, caught);
            }
            Rethrow(null, revealFailures, "Original Files selection and retainer custody failed.");
            await RequireReadyAsync(token);
            await acquired.RevalidateOriginalOwnerAsync(token);
            ThrowIfRetired(); token.ThrowIfCancellationRequested();
            acquired.CheckOriginalPublicationAlive();
            lock (_sync) { ThrowIfRetired(); _surface = acquired; }
            return acquired;
        }
        catch (Exception error)
        {
            primary = error;
            lock (_sync) { Add(_publicationFailures, error); _closing = true; }
        }
        if (fresh && acquired is not null)
        {
            Task? close = null;
            try { close = acquired.CloseAndDrainAsync(); }
            catch (Exception error) { Add(cleanup, error); }
            if (close is not null)
            {
                lock (_sync) _originalPublications.Add(close);
                try
                {
                    await close;
                    lock (_sync) _originalRegisteredSurfaces.Remove(acquired);
                }
                catch (Exception error) { Add(cleanup, error); }
            }
        }
        Rethrow(primary, cleanup, "Original Files registered-item navigation and acquired-view retirement failed.");
        throw new InvalidOperationException("Unreachable Files reveal result.");
    }
}
