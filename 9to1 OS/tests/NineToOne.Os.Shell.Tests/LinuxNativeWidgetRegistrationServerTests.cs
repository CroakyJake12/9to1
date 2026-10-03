using System.Buffers.Binary;
using System.Net.Sockets;
using System.Text.Json;
using Haven.Application;
using HavenOS.Home.Core;
using NineToOne.Os.Shell.Authority;
namespace NineToOne.Os.Shell.Tests;
public sealed class LinuxNativeWidgetRegistrationServerTests
{
    [Fact]
    public async Task ActualLeasedListenerKeepsDiscoverySeparateAndRejectsUnverifiedOwnerWithoutAdvertisingReady()
    {
        Assert.True(OperatingSystem.IsLinux(), "Actual Unix listener fixture requires Linux, no prerequisite skip.");
        var root = Path.Combine(Path.GetTempPath(), "w-ipc-" + Guid.NewGuid().ToString("N")[..10]);
        var actors = new Actors(); var paths = new Paths(root);
        await using var runtime = new HomeCoreRuntime([new Service("home.state"), new Service("apps.installed")]);
        try
        {
            using var lease = await HomeNativeSessionLease.TryAcquireAsync(actors, paths); Assert.NotNull(lease);
            var registry = new HomeNativeWidgetRegistry(new UnavailableHomeNativeInstalledPeerVerifier(), actors,
                new ResourceAuthorizationService(actors, []));
            await Assert.ThrowsAsync<InvalidOperationException>(() => LinuxNativeWidgetRegistrationServer.StartAsync(
                lease, paths, actors, runtime, registry, default));
            await runtime.StartAsync();
            var discoveryLocator = await HomeNativeEndpointLocations.GetLocatorPathAsync(paths);
            var discoveryBytes = new byte[] { 11, 22, 33, 44 }; await File.WriteAllBytesAsync(discoveryLocator, discoveryBytes);
            using var server = await LinuxNativeWidgetRegistrationServer.StartAsync(lease, paths, actors, runtime, registry, default);
            Assert.NotEqual(discoveryLocator, server.Location.LocatorPath);
            Assert.Equal(discoveryBytes, await File.ReadAllBytesAsync(discoveryLocator));
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(server.Location.SocketPath));
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(server.Location.LocatorPath));
            Assert.Equal(server.Location, JsonSerializer.Deserialize<HomeNativeEndpointLocation>(await File.ReadAllBytesAsync(server.Location.LocatorPath)));
            Assert.False(Assert.Single(runtime.Current.Services.Where(x => x.ServiceId == HomeNativeWidgetRegistry.ServiceId)).IsAvailable);
            using var client = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            await client.ConnectAsync(new UnixDomainSocketEndPoint(server.Location.SocketPath), deadline.Token);
            using var stream = new NetworkStream(client, ownsSocket: false);
            var bytes = JsonSerializer.SerializeToUtf8Bytes(new LinuxNativeWidgetRegistration(1, [new("clock", "r1", "Clock",
                new(1,1), new(2,2), new(4,4), "clock.config.v1", "clock.surface.v1", HomeNativeWidgetUpdateMode.Manual, null, [], [])]));
            var header = new byte[4]; BinaryPrimitives.WriteInt32BigEndian(header, bytes.Length);
            await stream.WriteAsync(header, deadline.Token); await stream.WriteAsync(bytes, deadline.Token);
            Assert.Equal(0, await stream.ReadAsync(new byte[1], deadline.Token));
            Assert.Empty(await registry.ListAsync());
            var socket = server.Location.SocketPath; var locator = server.Location.LocatorPath;
            server.Dispose(); Assert.False(File.Exists(socket)); Assert.False(File.Exists(locator)); Assert.True(lease.IsHeld);
            Assert.Equal(discoveryBytes, await File.ReadAllBytesAsync(discoveryLocator));
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
