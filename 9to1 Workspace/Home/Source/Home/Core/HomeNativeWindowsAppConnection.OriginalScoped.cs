using System.IO.Pipes;
using System.Security.Principal;
using Haven.Application;

namespace HavenOS.Home.Core;

public sealed partial class HomeNativeWindowsAppConnection
{
    private bool _originalScoped;
    private ScopedConnectionAttempt? _originalAttempt;
    private ScopedInitializer? _originalInitializer;
    private HomeNativeOriginalStartupScope? _originalCleanup;
    private sealed class ScopedConnectionAttempt
    {
        internal readonly HomeNativeOriginalStartupScope Source;
        internal Task Driver = null!;
        internal NamedPipeClientStream? Pipe;
        internal CancellationTokenSource? Lifetime, Active;
        internal HomeNativeWindowsStartupSession? Startup;
        internal Task<HomeNativeWindowsStartupSession?>? StartupAttachment;
        internal HomeNativeWindowsAppConnection? Result;
        internal Task? ActiveClose, StartupClose, PipeClose, LifetimeClose;
        internal ScopedConnectionAttempt(Action<Action> scope, Action<Task> retain) =>
            Source = new(this, () => Result, scope, retain);
    }
    private sealed class ScopedInitializer
    {
        internal readonly HomeNativeOriginalStartupScope Source;
        internal Task Driver = null!;
        internal CancellationTokenSource? Active;
        internal Task? ActiveClose;
        internal ScopedInitializer(object owner, Action<Action> scope, Action<Task> retain) =>
            Source = new(this, () => owner, scope, retain);
    }
    private static readonly object OriginalConnectionsGate = new();
    private static readonly List<ScopedConnectionAttempt> OriginalConnections = [];

    public static Task<HomeNativeWindowsAppConnection> ConnectWithinOriginalSourceAsync(HomeNativeWindowsEndpoint configuredEndpoint,
        IHomeNativeSessionHostVerifier actualVerifier, HomeNativeSessionHostRequirement trustedHost,
        HomeCompatibilityRequest trustedRequirements, CancellationToken originalAppLifetime,
        Action<Action> scope, Action<Task> retain, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(configuredEndpoint); ArgumentNullException.ThrowIfNull(actualVerifier);
        ArgumentNullException.ThrowIfNull(scope); ArgumentNullException.ThrowIfNull(retain);
        token.ThrowIfCancellationRequested(); originalAppLifetime.ThrowIfCancellationRequested();
        if (!OperatingSystem.IsWindows() || !originalAppLifetime.CanBeCanceled)
            throw new UnauthorizedAccessException("An actual Windows app owning lifetime is required.");
        var endpoint = configuredEndpoint.Capture();
        ArgumentNullException.ThrowIfNull(trustedHost);
        if (string.IsNullOrWhiteSpace(trustedHost.AppId) || trustedHost.AppId.Length > 128 || trustedHost.AppId != trustedHost.AppId.Trim() ||
            string.IsNullOrWhiteSpace(trustedHost.OperatingSystemApplicationId) || trustedHost.OperatingSystemApplicationId.Length > 4096 ||
            trustedHost.OperatingSystemApplicationId != trustedHost.OperatingSystemApplicationId.Trim())
            throw new ArgumentException("The trusted Home host requirement tuple is absent.", nameof(trustedHost));
        var originalHost = trustedHost with { };
        var requirements = HomeNativeWindowsStartupSession.CaptureOriginalRequirements(trustedRequirements);
        var work = new ScopedConnectionAttempt(scope, retain);
        var begin = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); Task<HomeNativeWindowsAppConnection> actual;
        lock (OriginalConnectionsGate)
        {
            if (OriginalConnections.Count >= 128) throw new InvalidOperationException("Unconfirmed original Home connections remain retained.");
            actual = Drive(); work.Driver = actual; OriginalConnections.Add(work);
        }
        work.Source.Publish(actual); begin.SetResult(); return actual;
        async Task<HomeNativeWindowsAppConnection> Drive()
        {
            await begin.Task.ConfigureAwait(false); using var own = CloudflareOriginalExecutionGuard.EnterOriginal(work);
            var source = work.Source;
            try
            {
                source.Throw();
                source.Run(() =>
                {
                    if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
                    var sameLifetime = work.Lifetime = CancellationTokenSource.CreateLinkedTokenSource(originalAppLifetime);
                    work.Active = CancellationTokenSource.CreateLinkedTokenSource(sameLifetime.Token, token);
                    work.Active.CancelAfter(TimeSpan.FromSeconds(5));
                    work.Pipe = new(".", endpoint.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous, TokenImpersonationLevel.Impersonation);
                });
                await source.Read(() => work.Pipe!.ConnectAsync(work.Active!.Token)).ConfigureAwait(false);
                await source.Read(() => work.StartupAttachment = HomeNativeWindowsStartupSession.AttachWithinOriginalSourceAsync(work.Pipe!, actualVerifier,
                    originalHost, requirements, work.Lifetime!.Token, source.Run, source.Retain, work.Active!.Token),
                    startup => work.Startup = startup).ConfigureAwait(false);
                var sameStartup = work.Startup ?? throw new UnauthorizedAccessException("The actual connected Home host did not authenticate.");
                source.Run(() =>
                {
                    work.Active!.Token.ThrowIfCancellationRequested();
                    work.Result = new(work.Pipe!, work.Lifetime!, sameStartup) { _originalScoped = true, _originalAttempt = work };
                    sameStartup.BindOriginalConnectionOwner(work.Result);
                });
            }
            catch (Exception cause) { source.Keep(cause); }
            finally
            {
                if (work.Active is not null)
                {
                    var same = source.Close(work.Active, ref work.ActiveClose);
                    try { await same.ConfigureAwait(false); } catch (Exception cause) { source.Keep(same.Exception ?? cause); }
                }
                if (work.Result is null || source.Errors.Length != 0)
                {
                    if (work.Lifetime is not null) try { CloudflareOriginalExecutionGuard.InvokeOriginal(work, () => { work.Lifetime.Cancel(); return true; }); }
                    catch (Exception cause) { source.Keep(cause); }
                    if (work.Startup is not null) await source.Cleanup(work.Startup.CloseAndDrainAsync, raw => work.StartupClose = raw).ConfigureAwait(false);
                    // An unresolved client/session keeps its borrowed pipe and
                    // lifetime rooted. A terminal failed close is no release proof.
                    if (HasJoinedOriginalStartupClose(work))
                    {
                        if (work.Pipe is not null) await source.Cleanup(() => work.Pipe.DisposeAsync().AsTask(), raw => work.PipeClose = raw).ConfigureAwait(false);
                        if (work.Lifetime is not null && HasJoinedOriginalPipeClose(work))
                        {
                            var same = source.Close(work.Lifetime, ref work.LifetimeClose);
                            try { await same.ConfigureAwait(false); } catch (Exception cause) { source.Keep(same.Exception ?? cause); }
                        }
                    }
                }
                await source.JoinAll().ConfigureAwait(false);
            }
            source.Throw();
            lock (OriginalConnectionsGate) OriginalConnections.Remove(work); // SAME actual connection owns the transferred cohort.
            return work.Result!;
        }
    }

    public Task<HomeNativeStartupObservation> CheckStartupWithinOriginalSourceAsync(Action<Action> scope,
        Action<Task> retain, CancellationToken token = default) =>
        _startup.CheckWithinOriginalSourceAsync(scope, retain, token);

    public Task InitializeWithinOriginalSourceAsync(Func<CancellationToken, Task> initializer,
        Action<Action> scope, Action<Task> retain, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(initializer); ArgumentNullException.ThrowIfNull(scope); ArgumentNullException.ThrowIfNull(retain);
        token.ThrowIfCancellationRequested();
        var work = new ScopedInitializer(this, scope, retain);
        var begin = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); Task actual;
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_closing, this);
            if (_initialization is not null) throw new InvalidOperationException("This app's original initializer is already retained.");
            actual = Drive(); work.Driver = actual; _originalInitializer = work; _initialization = actual;
        }
        work.Source.Publish(actual); begin.SetResult(); return actual;
        async Task Drive()
        {
            await begin.Task.ConfigureAwait(false); using var own = CloudflareOriginalExecutionGuard.EnterOriginal(this);
            var source = work.Source;
            try
            {
                source.Throw(); source.Run(() => work.Active = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token, token));
                var current = await source.Read(() => _startup.CheckWithinOriginalSourceAsync(source.Run, source.Retain, work.Active!.Token)).ConfigureAwait(false);
                if (!current.CanStartNormally) throw new UnauthorizedAccessException("Home startup remains unready or awaits its actual manual service-read review.");
                source.Run(work.Active!.Token.ThrowIfCancellationRequested);
                await source.Read(() => initializer(work.Active!.Token)).ConfigureAwait(false);
                current = await source.Read(() => _startup.CheckWithinOriginalSourceAsync(source.Run, source.Retain, work.Active!.Token)).ConfigureAwait(false);
                if (!current.CanStartNormally) throw new UnauthorizedAccessException("Home compatibility retired during the original app initialization.");
                source.Run(() => { work.Active!.Token.ThrowIfCancellationRequested(); lock (_sync) ObjectDisposedException.ThrowIf(_closing, this); });
            }
            catch (Exception cause) { source.Keep(cause); }
            finally
            {
                await source.JoinAll().ConfigureAwait(false);
                if (work.Active is not null)
                {
                    var same = source.Close(work.Active, ref work.ActiveClose);
                    try { await same.ConfigureAwait(false); } catch (Exception cause) { source.Keep(same.Exception ?? cause); }
                }
            }
            source.Throw();
        }
    }
    private static bool HasJoinedOriginalStartupClose(ScopedConnectionAttempt same)
    {
        // A nested attachment can fault after admitting a client but return no
        // session. Its absent result is not proof that the borrowed pipe drained.
        if (same.Startup is null) return same.StartupAttachment is null;
        var close = same.StartupClose;
        if (close?.IsCompletedSuccessfully != true) return false;
        close.GetAwaiter().GetResult(); // Exact independently healthy receipt.
        return ReferenceEquals(close, same.StartupClose);
    }
    private static bool HasJoinedOriginalPipeClose(ScopedConnectionAttempt same)
    {
        if (same.Pipe is null) return true;
        var close = same.PipeClose;
        if (close?.IsCompletedSuccessfully != true) return false;
        close.GetAwaiter().GetResult();
        return ReferenceEquals(close, same.PipeClose);
    }
    private async Task CloseWithinOriginalSourceAsync(Task start, Task? initialization)
    {
        await start.ConfigureAwait(false); using var own = CloudflareOriginalExecutionGuard.EnterOriginal(this);
        var source = _originalCleanup = new HomeNativeOriginalStartupScope(this, () => null, body => body(), _ => { });
        try { source.Run(_lifetime.Cancel); } catch (Exception cause) { source.Keep(cause); }
        var attempt = _originalAttempt!;
        try { attempt.StartupClose = _startup.CloseAndDrainAsync(); source.Retain(attempt.StartupClose); }
        catch (Exception cause) { source.Keep(cause); }
        if (initialization is not null) await source.Cleanup(() => initialization).ConfigureAwait(false);
        if (attempt.StartupClose is { } startupClose) await source.Cleanup(() => startupClose).ConfigureAwait(false);
        if (_originalInitializer is { } original)
        {
            await original.Source.JoinAll().ConfigureAwait(false);
            foreach (var cause in original.Source.Errors) source.Keep(cause);
        }
        await source.Cleanup(() => attempt.Driver).ConfigureAwait(false);
        await attempt.Source.JoinAll().ConfigureAwait(false);
        foreach (var cause in attempt.Source.Errors) source.Keep(cause);
        if (HasJoinedOriginalStartupClose(attempt))
        {
            await source.Cleanup(() => _pipe.DisposeAsync().AsTask(), raw => attempt.PipeClose = raw).ConfigureAwait(false);
            if (HasJoinedOriginalPipeClose(attempt))
            {
                var same = source.Close(_lifetime, ref attempt.LifetimeClose);
                try { await same.ConfigureAwait(false); } catch (Exception cause) { source.Keep(same.Exception ?? cause); }
            }
        }
        await source.JoinAll().ConfigureAwait(false); source.Throw();
    }
}
