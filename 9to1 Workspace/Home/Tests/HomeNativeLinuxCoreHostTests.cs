using System.Net.Sockets;
using System.Runtime.Versioning;
using System.Text.Json;
using Haven.Application;
using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;
using Xunit;

namespace HavenOS.Home.Tests;

/// <summary>Actual Linux OS profile/lease, canonical Core/session services and Unix
/// sockets. Default unavailable installed identity is deliberately retained: no test
/// double manufactures a trusted caller, publisher or successful protected read.</summary>
[SupportedOSPlatform("linux")]
public sealed class HomeNativeLinuxCoreHostTests
{
    private sealed class LinuxFactAttribute : FactAttribute
    { public LinuxFactAttribute() { if (!OperatingSystem.IsLinux()) Skip = "Requires actual Linux descriptor/socket ownership."; } }

    [LinuxFact]
    public async Task Actual_private_listener_returns_no_discovery_state_to_an_unverified_OS_peer()
    {
        await using var rig = await Rig.CreateAsync();
        var host = await rig.StartAsync();
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(host.Endpoint.SocketPath));
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(host.Endpoint.LocatorPath));
        Assert.Equal(host.Endpoint, JsonSerializer.Deserialize<HomeNativeEndpointLocation>(File.ReadAllBytes(host.Endpoint.LocatorPath)));
        using var client = await rig.ConnectAsync();
        using var stream = new NetworkStream(client, ownsSocket: false);
        await HomeUnixDiscoveryTransport.WriteFrameAsync(stream, JsonSerializer.SerializeToUtf8Bytes(
            new HomeNativeDiscoveryRequest([new("home.core", 1, 0)])), rig.Token);
        var bytes = await HomeUnixDiscoveryTransport.ReadFrameAsync(stream, rig.Token);
        var reply = JsonSerializer.Deserialize<HomeNativeDiscoveryResponse>(bytes)!;
        Assert.Equal("HomePeerOrServiceUnavailable", reply.Code);
        Assert.Null(reply.Snapshot);
        await host.CloseAndDrainAsync().WaitAsync(rig.Token);
        Assert.False(File.Exists(host.Endpoint.SocketPath));
        Assert.False(File.Exists(host.Endpoint.LocatorPath));
        Assert.True(rig.Lease.IsHeld); // socket host never owns/releases the canonical process lease
    }

    [LinuxFact]
    public async Task Real_Core_request_is_closed_without_a_reply_when_original_installed_identity_is_unavailable()
    {
        await using var rig = await Rig.CreateAsync();
        var host = await rig.StartAsync();
        using var client = await rig.ConnectAsync();
        using var stream = new NetworkStream(client, ownsSocket: false);
        var request = new HomeUnixCoreRequest(HomeUnixCoreProtocol.Version, Guid.NewGuid().ToString("N"), "GetServices");
        await HomeUnixDiscoveryTransport.WriteFrameAsync(stream, HomeUnixCoreProtocol.Payload(request), rig.Token);
        Assert.Equal(0, await stream.ReadAsync(new byte[1], rig.Token));
        Assert.Equal(1, host.RejectedConnectionCount);
        Assert.False(host.OriginalAcceptTask.IsCompleted);
    }

    [LinuxFact]
    public async Task Idle_actual_accepted_connection_drains_on_close_using_the_owned_first_frame_token()
    {
        await using var rig = await Rig.CreateAsync();
        var host = await rig.StartAsync();
        using var client = await rig.ConnectAsync();
        // Discovery reply on a second connection proves the accept loop has admitted
        // and begun serving the earlier idle socket, without a mock reader or sleep.
        using (var fence = await rig.ConnectAsync())
        using (var stream = new NetworkStream(fence, ownsSocket: false))
        {
            await HomeUnixDiscoveryTransport.WriteFrameAsync(stream, JsonSerializer.SerializeToUtf8Bytes(
                new HomeNativeDiscoveryRequest([new("home.core", 1, 0)])), rig.Token);
            _ = await HomeUnixDiscoveryTransport.ReadFrameAsync(stream, rig.Token);
        }
        var close = host.CloseAndDrainAsync();
        Assert.Same(close, host.CloseAndDrainAsync());
        await close.WaitAsync(rig.Token);
        Assert.True(host.OriginalAcceptTask.IsCompletedSuccessfully);
        Assert.False(File.Exists(host.Endpoint.SocketPath));
    }

    [LinuxFact]
    public async Task Existing_endpoint_collision_is_preserved_and_other_listener_stays_usable()
    {
        await using var rig = await Rig.CreateAsync();
        var original = await rig.StartAsync();
        var mode = File.GetUnixFileMode(original.Endpoint.SocketPath);
        var collision = HomeNativeLinuxCoreHost.Start(rig.Discovery, rig.Sessions, rig.Lease, rig.Endpoint, rig.Lifetime);
        await Assert.ThrowsAnyAsync<SocketException>(() => collision.OriginalListeningTask.WaitAsync(rig.Token));
        await Assert.ThrowsAnyAsync<Exception>(() => collision.CloseAndDrainAsync().WaitAsync(rig.Token));
        Assert.Equal(mode, File.GetUnixFileMode(original.Endpoint.SocketPath));
        using var client = await rig.ConnectAsync();
        using var stream = new NetworkStream(client, ownsSocket: false);
        await HomeUnixDiscoveryTransport.WriteFrameAsync(stream, JsonSerializer.SerializeToUtf8Bytes(
            new HomeNativeDiscoveryRequest([new("home.core", 1, 0)])), rig.Token);
        var reply = JsonSerializer.Deserialize<HomeNativeDiscoveryResponse>(await HomeUnixDiscoveryTransport.ReadFrameAsync(stream, rig.Token))!;
        Assert.Null(reply.Snapshot);
    }

    [LinuxFact]
    public async Task Replaced_socket_name_is_preserved_even_when_managed_listener_is_disposed()
    {
        await using var rig = await Rig.CreateAsync();
        var host = await rig.StartAsync();
        var path = host.Endpoint.SocketPath;
        File.Move(path, path + ".retained");
        File.WriteAllText(path, "foreign replacement must survive shutdown");
        rig.ExpectCloseFailure = true;
        var error = await Assert.ThrowsAnyAsync<Exception>(() => host.CloseAndDrainAsync().WaitAsync(rig.Token));
        Assert.Contains(Flatten(error), cause => cause is UnauthorizedAccessException);
        Assert.Equal("foreign replacement must survive shutdown", File.ReadAllText(path));
        Assert.True(File.Exists(path + ".retained"));
    }

    [LinuxFact]
    public async Task Existing_symlink_endpoint_and_public_parent_are_refused_without_adoption_or_chmod()
    {
        await using var rig = await Rig.CreateAsync();
        var target = Path.Combine(rig.Root, "foreign.txt"); File.WriteAllText(target, "preserve");
        File.CreateSymbolicLink(rig.Endpoint.SocketPath, target);
        rig.ExpectCloseFailure = true;
        var host = HomeNativeLinuxCoreHost.Start(rig.Discovery, rig.Sessions, rig.Lease, rig.Endpoint, rig.Lifetime);
        rig.Host = host;
        await Assert.ThrowsAnyAsync<SocketException>(() => host.OriginalListeningTask.WaitAsync(rig.Token));
        await Assert.ThrowsAnyAsync<Exception>(() => host.CloseAndDrainAsync().WaitAsync(rig.Token));
        Assert.Equal("preserve", File.ReadAllText(target));
        Assert.NotNull(new FileInfo(rig.Endpoint.SocketPath).LinkTarget);

        File.Delete(rig.Endpoint.SocketPath);
        var parent = Path.GetDirectoryName(rig.Endpoint.SocketPath)!;
        File.SetUnixFileMode(parent, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.GroupRead);
        var publicHost = HomeNativeLinuxCoreHost.Start(rig.Discovery, rig.Sessions, rig.Lease, rig.Endpoint, rig.Lifetime);
        await Assert.ThrowsAnyAsync<UnauthorizedAccessException>(() => publicHost.OriginalListeningTask.WaitAsync(rig.Token));
        await Assert.ThrowsAnyAsync<Exception>(() => publicHost.CloseAndDrainAsync().WaitAsync(rig.Token));
        Assert.True(File.GetUnixFileMode(parent).HasFlag(UnixFileMode.GroupRead));
        Assert.False(File.Exists(rig.Endpoint.SocketPath));
    }

    [LinuxFact]
    public async Task Existing_locator_is_never_replaced_and_failed_start_removes_only_its_own_socket_stage()
    {
        await using var rig = await Rig.CreateAsync();
        const string retained = "retained routing metadata from a prior owner";
        File.WriteAllText(rig.Endpoint.LocatorPath, retained);
        File.SetUnixFileMode(rig.Endpoint.LocatorPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        rig.ExpectCloseFailure = true;
        var host = HomeNativeLinuxCoreHost.Start(rig.Discovery, rig.Sessions, rig.Lease, rig.Endpoint, rig.Lifetime);
        rig.Host = host;
        await Assert.ThrowsAnyAsync<System.ComponentModel.Win32Exception>(() => host.OriginalListeningTask.WaitAsync(rig.Token));
        await Assert.ThrowsAnyAsync<Exception>(() => host.CloseAndDrainAsync().WaitAsync(rig.Token));
        Assert.Equal(retained, File.ReadAllText(rig.Endpoint.LocatorPath));
        Assert.False(File.Exists(rig.Endpoint.SocketPath));
        Assert.Empty(Directory.EnumerateFiles(Path.GetDirectoryName(rig.Endpoint.SocketPath)!, "*.endpoint-stage"));
    }

    [LinuxFact]
    public async Task Foreign_replacement_of_locator_is_preserved_and_original_socket_is_still_cleaned()
    {
        await using var rig = await Rig.CreateAsync();
        var host = await rig.StartAsync();
        File.Move(host.Endpoint.LocatorPath, host.Endpoint.LocatorPath + ".retained");
        File.WriteAllText(host.Endpoint.LocatorPath, "foreign locator");
        rig.ExpectCloseFailure = true;
        var error = await Assert.ThrowsAnyAsync<Exception>(() => host.CloseAndDrainAsync().WaitAsync(rig.Token));
        Assert.Contains(Flatten(error), cause => cause is UnauthorizedAccessException);
        Assert.Equal("foreign locator", File.ReadAllText(host.Endpoint.LocatorPath));
        Assert.False(File.Exists(host.Endpoint.SocketPath));
    }

    [LinuxFact]
    public async Task Explicit_bootstrap_hook_uses_same_canonical_graph_and_retains_denied_peer_semantics()
    {
        // This fixture implements the original-actor port only to DECLINE every
        // request. A listening socket is never asserted to be installed authority.
        var verifier = new DecliningOriginalActorVerifier();
        await using var rig = await Rig.CreateAsync(verifier);
        rig.Lease.Dispose(); // transfer the actual process lease acquisition to bootstrap
        await using var bootstrap = HomeNativeLinuxProcessBootstrap.Start(rig.Canonical(verifier), new Paths(rig.Root), rig.Lifetime);
        await bootstrap.OriginalStartTask.WaitAsync(rig.Token);
        var endpoint = JsonSerializer.Deserialize<HomeNativeEndpointLocation>(File.ReadAllBytes(rig.Endpoint.LocatorPath))!;
        using var client = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        await client.ConnectAsync(new UnixDomainSocketEndPoint(endpoint.SocketPath), rig.Token);
        using var stream = new NetworkStream(client, ownsSocket: false);
        await HomeUnixDiscoveryTransport.WriteFrameAsync(stream, JsonSerializer.SerializeToUtf8Bytes(
            new HomeNativeDiscoveryRequest([new("home.core", 1, 0)])), rig.Token);
        var reply = JsonSerializer.Deserialize<HomeNativeDiscoveryResponse>(await HomeUnixDiscoveryTransport.ReadFrameAsync(stream, rig.Token))!;
        Assert.Null(reply.Snapshot);
        Assert.Equal("HomePeerOrServiceUnavailable", reply.Code);
        await bootstrap.CloseAndDrainAsync().WaitAsync(rig.Token);
        Assert.False(File.Exists(endpoint.SocketPath));
        Assert.False(File.Exists(endpoint.LocatorPath));
        using var reacquired = await HomeNativeSessionLease.TryAcquireAsync(rig.Profiles, new Paths(rig.Root), rig.Token);
        Assert.NotNull(reacquired);
    }

    [LinuxFact]
    public async Task Failed_configured_bootstrap_preserves_existing_locator_and_releases_the_actual_process_lease()
    {
        var verifier = new DecliningOriginalActorVerifier();
        await using var rig = await Rig.CreateAsync(verifier);
        rig.Lease.Dispose();
        File.WriteAllText(rig.Endpoint.LocatorPath, "prior owner retained");
        File.SetUnixFileMode(rig.Endpoint.LocatorPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        var bootstrap = HomeNativeLinuxProcessBootstrap.Start(rig.Canonical(verifier), new Paths(rig.Root), rig.Lifetime);
        await Assert.ThrowsAnyAsync<Exception>(() => bootstrap.OriginalStartTask.WaitAsync(rig.Token));
        await Assert.ThrowsAnyAsync<Exception>(() => bootstrap.CloseAndDrainAsync().WaitAsync(rig.Token));
        Assert.Equal("prior owner retained", File.ReadAllText(rig.Endpoint.LocatorPath));
        Assert.Empty(Directory.EnumerateFiles(Path.GetDirectoryName(rig.Endpoint.SocketPath)!, "*.sock"));
        using var reacquired = await HomeNativeSessionLease.TryAcquireAsync(rig.Profiles, new Paths(rig.Root), rig.Token);
        Assert.NotNull(reacquired);
    }

    [LinuxFact]
    public async Task Default_bootstrap_remains_process_local_and_rejects_a_different_configured_verifier()
    {
        var unavailable = new UnavailableHomeNativeInstalledPeerVerifier();
        await using var rig = await Rig.CreateAsync(unavailable);
        Assert.Throws<UnauthorizedAccessException>(() => HomeNativeLinuxProcessBootstrap.Start(
            rig.Canonical(new DecliningOriginalActorVerifier()), new Paths(rig.Root), rig.Lifetime));
        Assert.True(rig.Lease.IsHeld);
        Assert.False(File.Exists(rig.Endpoint.LocatorPath));
        rig.Lease.Dispose();
        await using var bootstrap = HomeNativeLinuxProcessBootstrap.Start(rig.Canonical(unavailable), new Paths(rig.Root), rig.Lifetime);
        await bootstrap.OriginalStartTask.WaitAsync(rig.Token);
        Assert.False(File.Exists(rig.Endpoint.LocatorPath));
        Assert.Empty(Directory.EnumerateFiles(Path.GetDirectoryName(rig.Endpoint.SocketPath)!, "*.sock"));
    }

    [LinuxFact]
    public async Task Original_owner_cancellation_drains_the_listener_and_does_not_dispose_sessions_or_lease()
    {
        await using var rig = await Rig.CreateAsync();
        var host = await rig.StartAsync();
        rig.CancelLifetime();
        await host.OriginalAcceptTask.WaitAsync(rig.Token);
        await host.CloseAndDrainAsync().WaitAsync(rig.Token);
        Assert.True(rig.Lease.IsHeld);
        Assert.False(File.Exists(host.Endpoint.SocketPath));
        var actor = await rig.Profiles.GetCurrentAsync(rig.Token);
        Assert.NotNull(actor);
        Assert.Null(actor.AccountId);
    }

    private static IEnumerable<Exception> Flatten(Exception error) => error is AggregateException aggregate
        ? aggregate.InnerExceptions.SelectMany(Flatten) : [error];

    private sealed class Rig : IAsyncDisposable
    {
        private readonly CancellationTokenSource _deadline = new(TimeSpan.FromSeconds(20));
        private readonly CancellationTokenSource _lifetime = new();
        // Unix sockaddr_un has a byte bound. Keep real fixture paths short even
        // when an owning SDK runner redirects TMPDIR into a long evidence path.
        internal string Root { get; } = CreateRoot();
        internal CancellationToken Token => _deadline.Token;
        internal CancellationToken Lifetime => _lifetime.Token;
        internal HomeLocalProfileIdentity Profiles = null!;
        internal HomeNativeSessionLease Lease = null!;
        internal HomeNativeCoreApiSessions Sessions = null!;
        internal HomeNativeDiscoverySession Discovery = null!;
        internal HomeCoreRuntime Runtime = null!;
        internal HomeNativeEndpointLocation Endpoint = null!;
        internal HomeNativeLinuxCoreHost? Host;
        internal bool ExpectCloseFailure;
        private static string CreateRoot()
        {
            var path = Path.Combine("/tmp", "hln-" + Guid.NewGuid().ToString("N")[..12]);
            return Directory.CreateDirectory(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute).FullName;
        }
        internal HomeNativeServiceSession Canonical(IHomeNativeInstalledPeerVerifier registeredVerifier) =>
            new(new Provider(new Dictionary<Type, object>
            {
                [typeof(HomeCoreRuntime)] = Runtime,
                [typeof(IAuthenticatedResourceActorSource)] = Profiles,
                [typeof(HomeNativeCoreApiSessions)] = Sessions,
                [typeof(IHomeCoreAuthorization)] = Sessions,
                [typeof(IHomeNativeInstalledPeerVerifier)] = registeredVerifier
            }), Runtime, Profiles);
        internal static async Task<Rig> CreateAsync(IHomeNativeInstalledPeerVerifier? configuredVerifier = null)
        {
            var rig = new Rig();
            try
            {
                File.SetUnixFileMode(rig.Root, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
                var store = new FileHomeCoreStateStore(Path.Combine(rig.Root, "Home/state.json"));
                rig.Profiles = new(store, new OperatingSystemPrincipalSource());
                var permissions = new HomePermissionTrustService(store, (_, _) => null);
                HomeCoreApi? api = null;
                var verifier = configuredVerifier ?? new UnavailableHomeNativeInstalledPeerVerifier();
                rig.Sessions = new(permissions, rig.Profiles, verifier, () => api!);
                rig.Runtime = new([new HomeCoreStateService(store), new HomePermissionsCoreService(permissions, rig.Profiles)], rig.Sessions);
                api = new(rig.Runtime, rig.Sessions, rig.Profiles);
                rig.Discovery = new(verifier, rig.Profiles, rig.Runtime);
                var paths = new Paths(rig.Root);
                rig.Lease = await HomeNativeSessionLease.TryAcquireAsync(rig.Profiles, paths, rig.Token)
                    ?? throw new InvalidOperationException("The actual OS-local fixture lease was unavailable.");
                await rig.Runtime.StartAsync(rig.Token);
                rig.Endpoint = await HomeNativeEndpointLocations.CreateForLeasedHostAsync(rig.Lease, paths, rig.Profiles, rig.Token);
                return rig;
            }
            catch { await rig.DisposeAsync(); throw; }
        }
        internal async Task<HomeNativeLinuxCoreHost> StartAsync()
        {
            Host = HomeNativeLinuxCoreHost.Start(Discovery, Sessions, Lease, Endpoint, Lifetime);
            await Host.OriginalListeningTask.WaitAsync(Token);
            return Host;
        }
        internal async Task<Socket> ConnectAsync()
        {
            var client = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            try { await client.ConnectAsync(new UnixDomainSocketEndPoint(Endpoint.SocketPath), Token); return client; }
            catch { client.Dispose(); throw; }
        }
        internal void CancelLifetime() => _lifetime.Cancel();
        public async ValueTask DisposeAsync()
        {
            List<Exception> failures = [];
            if (Host is not null)
                try { await Host.CloseAndDrainAsync().WaitAsync(TimeSpan.FromSeconds(10)); }
                catch (Exception cause) when (ExpectCloseFailure) { _ = cause; }
                catch (Exception cause) { failures.Add(cause); }
            try { if (Sessions is not null) await Sessions.DisposeAsync(); } catch (Exception cause) { failures.Add(cause); }
            try { if (Runtime is not null) await Runtime.DisposeAsync(); } catch (Exception cause) { failures.Add(cause); }
            Lease?.Dispose(); _lifetime.Dispose(); _deadline.Dispose();
            try { Directory.Delete(Root, recursive: true); } catch (Exception cause) { failures.Add(cause); }
            HomeUnixCoreTransport.Throw(failures);
        }
    }
    private sealed record Paths(string DataDirectory) : IAppPaths
    {
        public string DatabasePath => Path.Combine(DataDirectory, "haven.db");
        public string BrowserProfileDirectory => Path.Combine(DataDirectory, "browser");
        public string AttachmentsDirectory => Path.Combine(DataDirectory, "attachments");
        public string LogsDirectory => Path.Combine(DataDirectory, "logs");
        public string LegacyStatePath => Path.Combine(DataDirectory, "legacy");
    }
    private sealed class Provider(Dictionary<Type, object> services) : IServiceProvider
    { public object? GetService(Type type) => services.GetValueOrDefault(type); }
    private sealed class DecliningOriginalActorVerifier : IHomeNativeInstalledPeerOriginalActorVerifier
    {
        public ValueTask<HomeNativeInstalledPeer?> VerifyAsync(HomeNativeObservedPeer observed, CancellationToken token)
        { token.ThrowIfCancellationRequested(); return ValueTask.FromResult<HomeNativeInstalledPeer?>(null); }
        public ValueTask<HomeNativeInstalledPeer?> VerifyForActorAsync(HomeNativeObservedPeer observed,
            AuthenticatedResourceActor expectedActor, CancellationToken token) => VerifyAsync(observed, token);
    }
}
