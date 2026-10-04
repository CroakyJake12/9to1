using System.IO.Pipes;
using System.Security.Principal;
using Haven.Application;

namespace HavenOS.Home.Core;

/// <summary>The app owns one physical connection. The supplied host requirement and app/service
/// descriptor must come from trusted composition; routing names or public reply fields confer no trust.</summary>
public sealed partial class HomeNativeWindowsAppConnection : IAsyncDisposable
{
    private readonly NamedPipeClientStream _pipe;
    private readonly CancellationTokenSource _lifetime;
    private readonly HomeNativeWindowsStartupSession _startup;
    private readonly object _sync = new();
    private Task? _initialization;
    private Task? _close;
    private bool _closing;

    private HomeNativeWindowsAppConnection(NamedPipeClientStream pipe, CancellationTokenSource lifetime,
        HomeNativeWindowsStartupSession startup)
    { _pipe = pipe; _lifetime = lifetime; _startup = startup; }

    public IHomeNativeStartupSession Startup => _startup;

    public static async Task<HomeNativeWindowsAppConnection> ConnectAsync(HomeNativeWindowsEndpoint configuredEndpoint,
        IHomeNativeSessionHostVerifier originalVerifier, HomeNativeSessionHostRequirement trustedHomeHost,
        HomeCompatibilityRequest trustedAppRequirements, CancellationToken originalAppLifetime,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(configuredEndpoint);
        ArgumentNullException.ThrowIfNull(originalVerifier);
        cancellationToken.ThrowIfCancellationRequested();
        originalAppLifetime.ThrowIfCancellationRequested();
        if (!OperatingSystem.IsWindows() || !originalAppLifetime.CanBeCanceled)
            throw new UnauthorizedAccessException("An actual Windows app owning lifetime is required.");
        var endpoint = configuredEndpoint.Capture();
        ArgumentNullException.ThrowIfNull(trustedHomeHost);
        if (string.IsNullOrWhiteSpace(trustedHomeHost.AppId) || trustedHomeHost.AppId.Length > 128 ||
            trustedHomeHost.AppId != trustedHomeHost.AppId.Trim() ||
            string.IsNullOrWhiteSpace(trustedHomeHost.OperatingSystemApplicationId) ||
            trustedHomeHost.OperatingSystemApplicationId.Length > 4096 ||
            trustedHomeHost.OperatingSystemApplicationId != trustedHomeHost.OperatingSystemApplicationId.Trim())
            throw new ArgumentException("The trusted Home host requirement tuple is absent.", nameof(trustedHomeHost));
        var originalHost = trustedHomeHost with { };
        var originalRequirements = HomeNativeWindowsStartupSession.CaptureOriginalRequirements(trustedAppRequirements);
        NamedPipeClientStream? pipe = null;
        CancellationTokenSource? lifetime = null;
        CancellationTokenSource? active = null;
        HomeNativeWindowsStartupSession? startup = null;
        List<Exception> failures = [];
        HomeNativeWindowsAppConnection? result = null;
        try
        {
            lifetime = CancellationTokenSource.CreateLinkedTokenSource(originalAppLifetime);
            active = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token, cancellationToken);
            active.CancelAfter(TimeSpan.FromSeconds(5));
            pipe = new NamedPipeClientStream(".", endpoint.PipeName, PipeDirection.InOut,
                PipeOptions.Asynchronous, TokenImpersonationLevel.Impersonation);
            await pipe.ConnectAsync(active.Token).ConfigureAwait(false);
            startup = await HomeNativeWindowsStartupSession.AttachAsync(pipe, originalVerifier, originalHost,
                originalRequirements, lifetime.Token, active.Token).ConfigureAwait(false)
                ?? throw new UnauthorizedAccessException("The actual connected Home host did not authenticate.");
            active.Token.ThrowIfCancellationRequested();
            result = new(pipe, lifetime, startup);
        }
        catch (Exception error) { HomeUnixCoreTransport.Add(failures, error); }
        finally
        {
            try { active?.Dispose(); } catch (Exception error) { HomeUnixCoreTransport.Add(failures, error); }
            if (result is null || failures.Count != 0)
            {
                try { lifetime?.Cancel(); } catch (Exception error) { HomeUnixCoreTransport.Add(failures, error); }
                try { if (startup is not null) await startup.CloseAndDrainAsync().ConfigureAwait(false); }
                catch (Exception error) { HomeUnixCoreTransport.Add(failures, error); }
                try { if (pipe is not null) await pipe.DisposeAsync().ConfigureAwait(false); }
                catch (Exception error) { HomeUnixCoreTransport.Add(failures, error); }
                try { lifetime?.Dispose(); } catch (Exception error) { HomeUnixCoreTransport.Add(failures, error); }
            }
        }
        HomeUnixCoreTransport.Throw(failures);
        return result!;
    }

    /// <summary>The owning Desktop callback closes over its SAME trusted provider and window.
    /// No callback is run until genuine Home compatibility settles; the SAME callback is drained
    /// before this object's physical connection closes.</summary>
    public Task InitializeOriginalAsync(Func<CancellationToken, Task> originalInitializer,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(originalInitializer);
        cancellationToken.ThrowIfCancellationRequested();
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_closing, this);
            if (_initialization is not null)
                throw new InvalidOperationException("This app's original initializer is already retained.");
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _initialization = InitializeOriginalCoreAsync(start.Task, originalInitializer, cancellationToken);
            start.SetResult();
            return _initialization;
        }
    }
    private async Task InitializeOriginalCoreAsync(Task start, Func<CancellationToken, Task> originalInitializer,
        CancellationToken caller)
    {
        await start.ConfigureAwait(false);
        CancellationTokenSource? active = null;
        Task? originalCallback = null;
        List<Exception> failures = [];
        try
        {
            active = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token, caller);
            var observation = await _startup.CheckAsync(active.Token).ConfigureAwait(false);
            if (!observation.CanStartNormally)
                throw new UnauthorizedAccessException("Home startup remains unready or awaits its actual manual service-read review.");
            active.Token.ThrowIfCancellationRequested();
            originalCallback = originalInitializer(active.Token)
                ?? throw new InvalidOperationException("The owning initializer returned no original task.");
            await originalCallback.ConfigureAwait(false);
            active.Token.ThrowIfCancellationRequested();
            observation = await _startup.CheckAsync(active.Token).ConfigureAwait(false);
            if (!observation.CanStartNormally)
                throw new UnauthorizedAccessException("Home compatibility retired during the original app initialization.");
            active.Token.ThrowIfCancellationRequested();
            lock (_sync) ObjectDisposedException.ThrowIf(_closing, this);
        }
        catch (Exception error) { HomeUnixCoreTransport.Add(failures, error); }
        finally
        {
            try { if (originalCallback is not null) await originalCallback.ConfigureAwait(false); }
            catch (Exception error) { HomeUnixCoreTransport.Add(failures, error); }
            try { active?.Dispose(); } catch (Exception error) { HomeUnixCoreTransport.Add(failures, error); }
        }
        HomeUnixCoreTransport.Throw(failures);
    }

    public Task CloseAndDrainAsync()
    {
        lock (_sync)
        {
            if (_close is not null) return _close;
            _closing = true;
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _close = CloseOriginalAsync(start.Task, _initialization);
            start.SetResult();
            return _close;
        }
    }
    public ValueTask DisposeAsync() => new(CloseAndDrainAsync());
    private async Task CloseOriginalAsync(Task start, Task? originalInitialization)
    {
        await start.ConfigureAwait(false);
        List<Exception> failures = [];
        Task? originalStartupClose = null;
        try { _lifetime.Cancel(); } catch (Exception error) { HomeUnixCoreTransport.Add(failures, error); }
        try { originalStartupClose = _startup.CloseAndDrainAsync(); }
        catch (Exception error) { HomeUnixCoreTransport.Add(failures, error); }
        try { if (originalInitialization is not null) await originalInitialization.ConfigureAwait(false); }
        catch (Exception error) { HomeUnixCoreTransport.Add(failures, error); }
        try { if (originalStartupClose is not null) await originalStartupClose.ConfigureAwait(false); }
        catch (Exception error) { HomeUnixCoreTransport.Add(failures, error); }
        foreach (var originalFiles in CaptureOriginalFilesRequests())
            try { await originalFiles.ConfigureAwait(false); }
            catch (Exception error) { HomeUnixCoreTransport.Add(failures, error); }
        try { await _pipe.DisposeAsync().ConfigureAwait(false); }
        catch (Exception error) { HomeUnixCoreTransport.Add(failures, error); }
        try { _lifetime.Dispose(); } catch (Exception error) { HomeUnixCoreTransport.Add(failures, error); }
        HomeUnixCoreTransport.Throw(failures);
    }
}
