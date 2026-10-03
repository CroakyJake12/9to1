using System.Net.Sockets;
using System.Runtime.ExceptionServices;
using Haven.Application;
using HavenOS.Home.Core;
using NineToOne.Os.Shell.Authority;
namespace NineToOne.Os.Shell;

/// <summary>Explicit installed-owner composition. No Home lease acquisition or actor alias.</summary>
public sealed class LinuxOriginalInstalledOwnerStartup : IDisposable, IAsyncDisposable
{
    private readonly LinuxOriginalInstalledOwnerRuntime _runtime;
    private readonly LinuxSupervisedWidgetOwnerConnection _connection;
    private LinuxOriginalInstalledOwnerStartup(LinuxOriginalInstalledOwnerRuntime runtime, LinuxSupervisedWidgetOwnerConnection connection)
    { _runtime = runtime; _connection = connection; }
    private readonly object _gate = new();
    private Task? _serve;
    private bool _disposed;
    public Task ServeCapturesAsync()
    {
        lock (_gate)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(LinuxOriginalInstalledOwnerStartup));
            if (_serve is not null) throw new InvalidOperationException("One original owner serve task only.");
            return _serve = _connection.ServeCapturesAsync();
        }
    }
    public void Dispose()
    {
        lock (_gate) { if (_disposed) return; _disposed = true; }
        try { _connection.Dispose(); } finally { _runtime.Dispose(); }
    }
    public async ValueTask DisposeAsync()
    {
        Task? original;
        lock (_gate) { _disposed = true; original = _serve; }
        Exception? cleanupFailure = null;
        try { _connection.Dispose(); }
        catch (Exception error) { cleanupFailure = error; }
        finally { _runtime.Dispose(); }
        if (original is not null)
            try { await original.ConfigureAwait(false); }
            catch (OperationCanceledException) { /* Original shutdown cancellation; task has been observed. */ }
            catch (Exception) when (cleanupFailure is not null) { /* Preserve original cleanup failure after observing task. */ }
        if (cleanupFailure is not null) ExceptionDispatchInfo.Capture(cleanupFailure).Throw();
    }
    public static async Task<LinuxOriginalInstalledOwnerStartup?> ConnectAsync(Socket actualConnectedHomeSocket,
        IHomeNativeControlledLaunchOriginalHomeActorObservationSource administrator,
        IAuthenticatedResourceActorSource localActors, ITrustedHostPrincipalSource principals,
        IHomeNativeInstalledPeerVerifier installedVerifier, IHomeNativeSessionHostVerifier installedHostVerifier,
        HomeNativeSessionHostRequirement trustedHost, IInstalledApplicationRegistry registry, CancellationToken ct)
    {
        var runtime = await LinuxOriginalInstalledOwnerRuntime.CreateAsync(administrator, localActors,
            principals, installedVerifier, registry, ct).ConfigureAwait(false);
        if (runtime is null) return null;
        try
        {
            var resources = new ResourceAuthorizationService(localActors, [new InstalledApplicationResourceResolver(registry)]);
            var pinned = new OriginalSessionSource(administrator, runtime.OriginalSession);
            var connection = await LinuxSupervisedWidgetOwnerConnection.ConnectAsync(actualConnectedHomeSocket, trustedHost,
                installedHostVerifier, installedVerifier, principals, localActors, resources, pinned,
                [runtime.Declaration()], runtime, ct).ConfigureAwait(false);
            if (connection is null) { runtime.Dispose(); return null; }
            return new(runtime, connection);
        }
        catch { runtime.Dispose(); throw; }
    }
    private sealed class OriginalSessionSource(IHomeNativeControlledLaunchOriginalHomeActorObservationSource issuer,
        HomeNativeControlledLaunchSessionContext original) : IHomeNativeControlledLaunchOriginalHomeActorObservationSource
    {
        public async ValueTask<HomeNativeControlledLaunchSessionContext?> GetForActorAsync(AuthenticatedResourceActor actor, CancellationToken ct)
            => await issuer.IsCurrentForActorAsync(original, actor, ct).ConfigureAwait(false) ? original : null;
        public ValueTask<bool> IsCurrentForActorAsync(HomeNativeControlledLaunchSessionContext context, AuthenticatedResourceActor actor, CancellationToken ct)
            => ReferenceEquals(context, original) ? issuer.IsCurrentForActorAsync(original, actor, ct) : ValueTask.FromResult(false);
        public ValueTask<AuthenticatedResourceActor?> ReadOriginalHomeActorForActorAsync(HomeNativeControlledLaunchSessionContext context,
            AuthenticatedResourceActor actor, CancellationToken ct)
            => ReferenceEquals(context, original) ? issuer.ReadOriginalHomeActorForActorAsync(original, actor, ct) : ValueTask.FromResult<AuthenticatedResourceActor?>(null);
    }
}
