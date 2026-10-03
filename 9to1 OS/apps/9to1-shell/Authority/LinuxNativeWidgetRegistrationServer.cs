using System.Net.Sockets;
using System.Text.Json;
using Haven.Application;
using HavenOS.Home.Core;

namespace NineToOne.Os.Shell.Authority;

/// <summary>Distinct leased owner-registration listener. Locator is routing only; installed peer verification remains mandatory.
/// This does not advertise an unavailable widget service as ready or supply controlled-launch evidence.</summary>
public sealed class LinuxNativeWidgetRegistrationServer : IDisposable
{
    private readonly Socket _listener;
    private readonly HomeNativeSessionLease _lease;
    private readonly IAuthenticatedResourceActorSource _actors;
    private readonly HomeCoreRuntime _runtime;
    private readonly HomeNativeWidgetRegistry _registry;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly Task[] _workers;
    public HomeNativeEndpointLocation Location { get; }
    private LinuxNativeWidgetRegistrationServer(Socket listener, HomeNativeEndpointLocation location, HomeNativeSessionLease lease,
        IAuthenticatedResourceActorSource actors, HomeCoreRuntime runtime, HomeNativeWidgetRegistry registry)
    {
        _listener = listener; Location = location; _lease = lease; _actors = actors; _runtime = runtime; _registry = registry;
        // Four actual connections maximum; bounded enrollment/capture and original cancellation/EOF lifetime.
        _workers = Enumerable.Range(0, 4).Select(_ => Task.Run(ServeLoopAsync)).ToArray();
    }
    public static async Task<LinuxNativeWidgetRegistrationServer> StartAsync(HomeNativeSessionLease heldLease, IAppPaths trustedHomePaths,
        IAuthenticatedResourceActorSource actors, HomeCoreRuntime runtime, HomeNativeWidgetRegistry registry, CancellationToken ct)
    {
        if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException();
        var original = await actors.GetCurrentAsync(ct).ConfigureAwait(false);
        if (original is null || !heldLease.IsHeld || original.ProfileId != heldLease.ProfileId)
            throw new UnauthorizedAccessException("The original leased Home actor is required.");
        var shared = await HomeNativeEndpointLocations.CreateForLeasedHostAsync(heldLease, trustedHomePaths, actors, ct).ConfigureAwait(false);
        var location = shared with { LocatorPath = shared.LocatorPath.Replace(".endpoint.json", ".widgets.endpoint.json", StringComparison.Ordinal) };
        if (!Ready(runtime)) throw new InvalidOperationException("The actual required Home services must be ready before registry can listen.");
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
            if (!heldLease.IsHeld || actor is null || actor != original || actor.ProfileId != location.ProfileId || !Ready(runtime))
                throw new UnauthorizedAccessException("The leased Home session changed before endpoint publication.");
            File.Move(temporary, location.LocatorPath, overwrite: true);
            return new(listener, location, heldLease, actors, runtime, registry);
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
                await LinuxNativeWidgetAcceptedTransport.ServeAcceptedAsync(accepted, _registry, _actors,
                    () => _lease.IsHeld && Ready(_runtime), _shutdown.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (_shutdown.IsCancellationRequested) { break; }
            catch (ObjectDisposedException) when (_shutdown.IsCancellationRequested) { break; }
            catch (Exception ex) when (ex is IOException or SocketException or UnauthorizedAccessException or OperationCanceledException or JsonException or InvalidDataException)
            { /* A bounded failed/disconnected peer does not stop registry for other peers. */ }
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
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException) { }
        TryDelete(Location.SocketPath); _shutdown.Dispose();
    }
    private static void TryDelete(string path)
    { try { File.Delete(path); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { } }
}
