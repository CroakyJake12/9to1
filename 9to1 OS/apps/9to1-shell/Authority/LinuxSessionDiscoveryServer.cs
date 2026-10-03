using System.Net.Sockets;
using System.Text.Json;
using Haven.Application;
using HavenOS.Home.Core;

namespace NineToOne.Os.Shell.Authority;

/// <summary>Leased central-session discovery listener. No approvals, resource mutations or arbitrary app calls.</summary>
public sealed class LinuxSessionDiscoveryServer : IDisposable
{
    private readonly Socket _listener;
    private readonly HomeNativeSessionLease _lease;
    private readonly IAuthenticatedResourceActorSource _actors;
    private readonly HomeCoreRuntime _runtime;
    private readonly HomeNativeDiscoverySession _discovery;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly Task[] _workers;
    public HomeNativeEndpointLocation Location { get; }
    private LinuxSessionDiscoveryServer(Socket listener, HomeNativeEndpointLocation location, HomeNativeSessionLease lease,
        IAuthenticatedResourceActorSource actors, HomeCoreRuntime runtime, HomeNativeDiscoverySession discovery)
    {
        _listener = listener; Location = location; _lease = lease; _actors = actors; _runtime = runtime; _discovery = discovery;
        // Four requests maximum in flight; each shared transport has a five-second deadline and a bounded frame.
        _workers = Enumerable.Range(0, 4).Select(_ => Task.Run(ServeLoopAsync)).ToArray();
    }
    public static async Task<LinuxSessionDiscoveryServer> StartAsync(HomeNativeSessionLease heldLease, IAppPaths trustedHomePaths,
        IAuthenticatedResourceActorSource actors, HomeCoreRuntime runtime, HomeNativeDiscoverySession discovery, CancellationToken ct)
    {
        if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException();
        var location = await HomeNativeEndpointLocations.CreateForLeasedHostAsync(heldLease, trustedHomePaths, actors, ct).ConfigureAwait(false);
        if (!Ready(runtime)) throw new InvalidOperationException("The actual required Home services must be ready before discovery can listen.");
        var listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        var temporary = location.LocatorPath + "." + location.Epoch.ToString("N") + ".tmp";
        try
        {
            listener.Bind(new UnixDomainSocketEndPoint(location.SocketPath));
            File.SetUnixFileMode(location.SocketPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            listener.Listen(16);
            var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None,
                UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite };
            await using (var file = new FileStream(temporary, options))
            {
                await JsonSerializer.SerializeAsync(file, location, cancellationToken: ct).ConfigureAwait(false);
                await file.FlushAsync(ct).ConfigureAwait(false); file.Flush(flushToDisk: true);
            }
            var actor = await actors.GetCurrentAsync(ct).ConfigureAwait(false);
            if (!heldLease.IsHeld || actor?.ProfileId != location.ProfileId || !Ready(runtime))
                throw new UnauthorizedAccessException("The leased Home session changed before endpoint publication.");
            File.Move(temporary, location.LocatorPath, overwrite: true);
            return new(listener, location, heldLease, actors, runtime, discovery);
        }
        catch
        {
            listener.Dispose(); TryDelete(temporary); TryDelete(location.SocketPath); throw;
        }
    }
    private async Task ServeLoopAsync()
    {
        while (!_shutdown.IsCancellationRequested)
        {
            try
            {
                using var accepted = await _listener.AcceptAsync(_shutdown.Token).ConfigureAwait(false);
                if (!_lease.IsHeld || !Ready(_runtime) || (await _actors.GetCurrentAsync(_shutdown.Token).ConfigureAwait(false))?.ProfileId != Location.ProfileId) continue;
                await HomeUnixDiscoveryTransport.ServeAcceptedAsync(accepted, _discovery, _shutdown.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (_shutdown.IsCancellationRequested) { break; }
            catch (ObjectDisposedException) when (_shutdown.IsCancellationRequested) { break; }
            catch (Exception ex) when (ex is IOException or SocketException or UnauthorizedAccessException or OperationCanceledException or JsonException)
            { /* A bounded failed/disconnected peer does not stop discovery for other peers. */ }
        }
    }
    private static bool Ready(HomeCoreRuntime runtime) => new[] { "home.core", "home.state", "apps.installed" }.All(id =>
        runtime.Current.Services.Any(s => s.ServiceId == id && s.IsAvailable && s.State == HomeServiceLifecycleState.Ready &&
            s.ContractVersion.Major == HomeCoreServiceCatalog.CurrentContractVersion.Major && s.ContractVersion.Minor >= HomeCoreServiceCatalog.CurrentContractVersion.Minor));
    public void Dispose()
    {
        if (_shutdown.IsCancellationRequested) return;
        _shutdown.Cancel(); _listener.Dispose();
        Task.WhenAll(_workers).GetAwaiter().GetResult();
        try
        {
            var info = new FileInfo(Location.LocatorPath);
            if (info.Exists && info.Length <= 16 * 1024 && (info.Attributes & FileAttributes.ReparsePoint) == 0 &&
                JsonSerializer.Deserialize<HomeNativeEndpointLocation>(File.ReadAllBytes(Location.LocatorPath))?.Epoch == Location.Epoch) TryDelete(Location.LocatorPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { }
        TryDelete(Location.SocketPath); _shutdown.Dispose();
    }
    private static void TryDelete(string path)
    { try { File.Delete(path); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { } }
}
