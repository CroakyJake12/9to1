using System.Net.Sockets;
using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using Haven.Application;
using HavenOS.Home.Core;
using Xunit;
namespace HavenOS.Home.Tests;
public sealed class HomeNativePeerObservationTests
{
    [Fact]
    public async Task Accepted_socket_reports_kernel_peer_pid_and_principal_not_payload_claims()
    {
        if (!OperatingSystem.IsLinux()) return;
        var path = Path.Combine(Path.GetTempPath(), "a-peer-" + Guid.NewGuid().ToString("N"));
        using var server = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        using var client = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        try
        {
            server.Bind(new UnixDomainSocketEndPoint(path)); server.Listen(1);
            await client.ConnectAsync(new UnixDomainSocketEndPoint(path));
            using var accepted = await server.AcceptAsync();
            var observed = HomeNativePeerObservation.FromAcceptedUnixSocket(accepted);
            Assert.NotNull(observed); Assert.Equal(Environment.ProcessId, observed.ProcessId);
            Assert.StartsWith("unix-euid:", observed.OperatingSystemPrincipalId);
            Assert.Null(HomeNativePeerObservation.FromAcceptedUnixSocket(server));
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Actual_discovery_transport_denies_forged_identity_and_oversized_frame(bool oversized)
    {
        if (!OperatingSystem.IsLinux()) return;
        var path = Path.Combine(Path.GetTempPath(), "a-discovery-" + Guid.NewGuid().ToString("N"));
        using var listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        using var client = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        await using var runtime = new HomeCoreRuntime();
        try
        {
            listener.Bind(new UnixDomainSocketEndPoint(path)); listener.Listen(1);
            await client.ConnectAsync(new UnixDomainSocketEndPoint(path));
            using var accepted = await listener.AcceptAsync();
            var serving = HomeUnixDiscoveryTransport.ServeAcceptedAsync(accepted,
                new(new UnavailableHomeNativeInstalledPeerVerifier(), new Actor(), runtime));
            using var stream = new NetworkStream(client, ownsSocket: false);
            var payload = Encoding.UTF8.GetBytes("{\"AppId\":\"trusted-home\",\"Requirements\":[{\"ServiceId\":\"permissions.trust\",\"MajorVersion\":1}]}");
            var header = new byte[4]; BinaryPrimitives.WriteInt32BigEndian(header, oversized ? 65537 : payload.Length);
            await stream.WriteAsync(header);
            if (!oversized) await stream.WriteAsync(payload);
            await stream.ReadExactlyAsync(header);
            var response = new byte[BinaryPrimitives.ReadInt32BigEndian(header)];
            await stream.ReadExactlyAsync(response);
            var result = JsonSerializer.Deserialize<HomeNativeDiscoveryResponse>(response);
            Assert.NotNull(result); Assert.Null(result.Snapshot);
            Assert.Equal(oversized ? "HomeDiscoveryRequestInvalid" : "HomePeerOrServiceUnavailable", result.Code);
            await serving;
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
    private sealed class Actor : IAuthenticatedResourceActorSource
    {
        public ValueTask<AuthenticatedResourceActor?> GetCurrentAsync(CancellationToken ct) =>
            ValueTask.FromResult<AuthenticatedResourceActor?>(new("fixture", "profile", null, null, "v1"));
    }

}
