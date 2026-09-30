using Haven.Application;
using System.Net.Sockets;
using HavenOS.Home.Core;
using Xunit;
namespace HavenOS.Home.Tests;
public sealed class HomeNativeDiscoverySessionTests
{
    [Fact]
    public async Task Discovery_requires_installed_attestation_service_allowlist_and_current_identity()
    {
        await using var runtime = new HomeCoreRuntime([new Service()]); await runtime.StartAsync();
        var verifier = new Verifier(); var actors = new Actor();
        var discovery = new HomeNativeDiscoverySession(verifier, actors, runtime);
        var peer = new HomeNativeObservedPeer(123, "unix-euid:1000");
        HomeServiceRequirement[] required = [new("fixture.discovery", 1)];
        Assert.NotNull(await discovery.DiscoverAsync(peer, required));
        Assert.Single((await discovery.DiscoverAsync(peer, [new("fixture.discovery", 1), new("unavailable.optional", 1, Required: false)]))!.Services);
        Assert.Null(await discovery.DiscoverAsync(peer, [new("permissions.trust", 1)]));
        verifier.ChangeOnSecondRead = true; verifier.Calls = 0;
        Assert.Null(await discovery.DiscoverAsync(peer, required));
        Assert.Null(await new HomeNativeDiscoverySession(new UnavailableHomeNativeInstalledPeerVerifier(), actors, runtime).DiscoverAsync(peer, required));
    }
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task Real_socket_client_requires_designated_host_role_and_revalidates_receipt(int mode)
    {
        if (!OperatingSystem.IsLinux()) return;
        var path = Path.Combine(Path.GetTempPath(), "a-client-" + Guid.NewGuid().ToString("N"));
        using var listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        using var client = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        await using var runtime = new HomeCoreRuntime([new Service()]); await runtime.StartAsync();
        try
        {
            listener.Bind(new UnixDomainSocketEndPoint(path)); listener.Listen(1);
            await client.ConnectAsync(new UnixDomainSocketEndPoint(path));
            using var accepted = await listener.AcceptAsync();
            var serving = mode == 0 ? Task.CompletedTask : HomeUnixDiscoveryTransport.ServeAcceptedAsync(accepted,
                new(new Verifier(), new Actor(), runtime));
            var result = await HomeUnixDiscoveryClient.DiscoverAsync(client, new HostVerifier(mode),
                new("os.shell", "trusted-fixture-entry"), [new("fixture.discovery", 1)]);
            Assert.Equal(mode == 1 ? "HomeDiscoveryReady" : "HomeHostIdentityOrServiceUnavailable", result.Code);
            if (mode == 0) Assert.Equal(0, accepted.Available);
            await serving;
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
    private sealed class HostVerifier(int mode) : IHomeNativeSessionHostVerifier
    {
        private readonly Guid _id = Guid.NewGuid(); private int _calls;
        public ValueTask<HomeNativeInstalledPeer?> VerifyHostAsync(HomeNativeObservedPeer observed,
            HomeNativeSessionHostRequirement required, CancellationToken ct)
        {
            _calls++;
            return ValueTask.FromResult<HomeNativeInstalledPeer?>(new("os.shell", _id,
                mode == 2 && _calls > 1 ? "changed" : "v1", "fixture-digest", new HashSet<string>())
            { Roles = mode == 0 ? new HashSet<string>() : new HashSet<string> { HomeNativeSessionHostRequirement.RequiredRole } });
        }
    }
    private sealed class Verifier : IHomeNativeInstalledPeerVerifier
    {
        public int Calls; public bool ChangeOnSecondRead;
        private readonly Guid _id = Guid.NewGuid();
        public ValueTask<HomeNativeInstalledPeer?> VerifyAsync(HomeNativeObservedPeer observed, CancellationToken ct)
        {
            Calls++;
            return ValueTask.FromResult<HomeNativeInstalledPeer?>(new("fixture", _id,
                ChangeOnSecondRead && Calls > 1 ? "revoked" : "v1", "fixture-digest", new HashSet<string> { "fixture.discovery" }));
        }
    }
    private sealed class Actor : IAuthenticatedResourceActorSource
    {
        public ValueTask<AuthenticatedResourceActor?> GetCurrentAsync(CancellationToken ct) =>
            ValueTask.FromResult<AuthenticatedResourceActor?>(new("fixture", "profile", null, null, "v1"));
    }
    private sealed class Service : IHomeCoreService
    {
        public HomeServiceDescriptor Descriptor { get; } = new("fixture.discovery", new(1, 0, 0), HomeServiceLifecycleState.Stopped, false);
        public IReadOnlyList<string> Dependencies => [];
        public Task StartAsync(CancellationToken ct) => Task.CompletedTask;
        public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
    }
}
