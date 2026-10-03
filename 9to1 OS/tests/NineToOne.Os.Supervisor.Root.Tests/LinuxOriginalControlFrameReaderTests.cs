using System.Buffers.Binary;
using System.Net.Sockets;
using System.Runtime.Versioning;
using NineToOne.Os.Shell.Authority;

namespace NineToOne.Os.Supervisor.Root.Tests;

// Actual Unix sockets and framing; no actor, installed process or launch grant.
[SupportedOSPlatform("linux")]
public sealed class LinuxOriginalControlFrameReaderTests
{
    private sealed record Packet(int Schema, Guid Correlation);

    [Fact]
    public async Task Healthy_idle_original_socket_survives_old_sixty_second_deadline()
    {
        using var sockets = await Pair.CreateAsync();
        using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(85));
        var pending = LinuxOriginalControlFrameReader.ReadAsync<Packet>(sockets.Input, lifetime.Token);
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(65), lifetime.Token);
            Assert.False(pending.IsCompleted);
            var expected = new Packet(3, Guid.NewGuid());
            await LinuxHomeChildLeaseChannel.WriteAsync(sockets.Output, expected, lifetime.Token);
            Assert.Equal(expected, await pending.WaitAsync(lifetime.Token));
        }
        finally { lifetime.Cancel(); try { await pending; } catch { } }
    }

    [Fact]
    public async Task Partial_frame_does_not_renew_absolute_ingress_deadline()
    {
        using var sockets = await Pair.CreateAsync();
        using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(85));
        var pending = LinuxOriginalControlFrameReader.ReadAsync<Packet>(sockets.Input, lifetime.Token);
        try
        {
            await sockets.Output.WriteAsync(new byte[] { 0 }, lifetime.Token);
            await sockets.Output.FlushAsync(lifetime.Token);
            await Task.Delay(TimeSpan.FromSeconds(35), lifetime.Token);
            await sockets.Output.WriteAsync(new byte[] { 0 }, lifetime.Token);
            await sockets.Output.FlushAsync(lifetime.Token);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending.WaitAsync(TimeSpan.FromSeconds(32), lifetime.Token));
            Assert.False(lifetime.IsCancellationRequested);
        }
        finally { lifetime.Cancel(); try { await pending; } catch { } }
    }

    [Fact]
    public async Task Idle_lifetime_cancellation_and_real_eof_are_observed()
    {
        using var sockets = await Pair.CreateAsync();
        using var lifetime = new CancellationTokenSource();
        var pending = LinuxOriginalControlFrameReader.ReadAsync<Packet>(sockets.Input, lifetime.Token);
        lifetime.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        sockets.Output.Dispose();
        await Assert.ThrowsAsync<EndOfStreamException>(() => LinuxOriginalControlFrameReader.ReadAsync<Packet>(sockets.Input, default));
    }

    [Fact]
    public async Task Oversized_header_is_refused_before_payload_and_null_json_is_refused()
    {
        using var sockets = await Pair.CreateAsync();
        using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var header = new byte[4]; BinaryPrimitives.WriteInt32BigEndian(header, 16385);
        await sockets.Output.WriteAsync(header, lifetime.Token);
        await sockets.Output.FlushAsync(lifetime.Token);
        await Assert.ThrowsAsync<InvalidDataException>(() => LinuxOriginalControlFrameReader.ReadAsync<Packet>(sockets.Input, lifetime.Token));
        await LinuxHomeChildLeaseChannel.WriteAsync<object?>(sockets.Output, null, lifetime.Token);
        await Assert.ThrowsAsync<InvalidDataException>(() => LinuxOriginalControlFrameReader.ReadAsync<Packet>(sockets.Input, lifetime.Token));
    }

    private sealed class Pair : IDisposable
    {
        private readonly string _root;
        private Pair(string root, Socket input, Socket output)
        { _root = root; Input = new(input, ownsSocket: true); Output = new(output, ownsSocket: true); }
        internal NetworkStream Input { get; }
        internal NetworkStream Output { get; }
        internal static async Task<Pair> CreateAsync()
        {
            var root = Path.Combine(Path.GetTempPath(), "astra-frame-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            using var listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            Socket? output = null; Socket? input = null;
            try
            {
                var endpoint = new UnixDomainSocketEndPoint(Path.Combine(root, "control.sock"));
                listener.Bind(endpoint); listener.Listen(1);
                output = new(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
                using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                await output.ConnectAsync(endpoint, deadline.Token);
                input = await listener.AcceptAsync(deadline.Token);
                return new(root, input, output);
            }
            catch { input?.Dispose(); output?.Dispose(); Directory.Delete(root, true); throw; }
        }
        public void Dispose()
        { try { Input.Dispose(); } finally { try { Output.Dispose(); } finally { Directory.Delete(_root, true); } } }
    }
}
