using System.Buffers.Binary;
using System.Net.Sockets;
using System.Text.Json;
using Haven.Application;
using HavenOS.Home.Core;
using NineToOne.Os.Shell.Authority;

namespace NineToOne.Os.Shell.Tests;

public sealed class LinuxDiscoveryServerTests
{
    [Fact]
    public async Task RealListenerRequiresLeaseAndReadinessDeniesUnsignedPeerAndRemovesRoutingOnClose()
    {
        if (!OperatingSystem.IsLinux()) return;
        var root = Path.Combine(Path.GetTempPath(), "a-ipc-" + Guid.NewGuid().ToString("N")[..10]);
        var actors = new Actors(); var paths = new Paths(root);
        await using var runtime = new HomeCoreRuntime([new Service("home.state"), new Service("apps.installed")]);
        try
        {
            using var lease = await HomeNativeSessionLease.TryAcquireAsync(actors, paths); Assert.NotNull(lease);
            var discovery = new HomeNativeDiscoverySession(new UnavailableHomeNativeInstalledPeerVerifier(), actors, runtime);
            await Assert.ThrowsAsync<InvalidOperationException>(() => LinuxSessionDiscoveryServer.StartAsync(lease, paths, actors, runtime, discovery, default));
            await runtime.StartAsync();
            using var server = await LinuxSessionDiscoveryServer.StartAsync(lease, paths, actors, runtime, discovery, default);
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(server.Location.SocketPath));
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(server.Location.LocatorPath));
            var routing = JsonSerializer.Deserialize<HomeNativeEndpointLocation>(await File.ReadAllBytesAsync(server.Location.LocatorPath));
            Assert.Equal(server.Location, routing);
            using var client = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            await client.ConnectAsync(new UnixDomainSocketEndPoint(server.Location.SocketPath));
            using var stream = new NetworkStream(client, ownsSocket: false);
            var request = JsonSerializer.SerializeToUtf8Bytes(new HomeNativeDiscoveryRequest([new("home.core", 1)]));
            var header = new byte[4]; BinaryPrimitives.WriteInt32BigEndian(header, request.Length);
            await stream.WriteAsync(header); await stream.WriteAsync(request);
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            await stream.ReadExactlyAsync(header, deadline.Token);
            var length = BinaryPrimitives.ReadInt32BigEndian(header); Assert.InRange(length, 1, 65536);
            var payload = new byte[length]; await stream.ReadExactlyAsync(payload, deadline.Token);
            var response = JsonSerializer.Deserialize<HomeNativeDiscoveryResponse>(payload);
            Assert.Equal("HomePeerOrServiceUnavailable", response?.Code); Assert.Null(response?.Snapshot);
            var socketPath = server.Location.SocketPath; var locatorPath = server.Location.LocatorPath;
            server.Dispose(); Assert.False(File.Exists(socketPath)); Assert.False(File.Exists(locatorPath)); Assert.True(lease.IsHeld);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
    private sealed class Actors : IAuthenticatedResourceActorSource
    { public ValueTask<AuthenticatedResourceActor?> GetCurrentAsync(CancellationToken ct) => ValueTask.FromResult<AuthenticatedResourceActor?>(new("actor", "profile", null, null, "1")); }
    private sealed class Paths(string root) : IAppPaths
    {
        public string DataDirectory => root; public string DatabasePath => Path.Combine(root, "db");
        public string BrowserProfileDirectory => Path.Combine(root, "browser"); public string AttachmentsDirectory => Path.Combine(root, "attachments");
        public string LogsDirectory => Path.Combine(root, "logs"); public string LegacyStatePath => Path.Combine(root, "legacy");
    }
    private sealed class Service(string id) : IHomeCoreService
    {
        public HomeServiceDescriptor Descriptor { get; } = new(id, HomeCoreServiceCatalog.CurrentContractVersion, HomeServiceLifecycleState.Stopped, false);
        public IReadOnlyList<string> Dependencies => ["home.core"];
        public Task StartAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task StopAsync(CancellationToken ct = default) => Task.CompletedTask;
    }
}
