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

    // BEGIN widget-only closest-test region. These are actual sockets, not authority fixtures.
    private static readonly System.Text.Json.JsonSerializerOptions WidgetJson = new() { MaxDepth = 20 };

    [Fact]
    public async Task Widget_capture_and_surface_idle_survive_sixty_five_seconds()
    {
        using var capture = await Pair.CreateAsync(); using var surface = await Pair.CreateAsync();
        using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(85));
        var captureRead = LinuxOriginalControlFrameReader.ReadWidgetAsync<Packet>(capture.Input, 64 * 1024, WidgetJson, lifetime.Token);
        var surfaceRead = LinuxOriginalControlFrameReader.ReadWidgetAsync<Packet>(surface.Input, 384 * 1024, WidgetJson, lifetime.Token);
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(65), lifetime.Token);
            Assert.False(captureRead.IsCompleted); Assert.False(surfaceRead.IsCompleted);
            var expected = new Packet(3, Guid.NewGuid());
            await WriteWidget(capture.Output, System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(expected, WidgetJson), lifetime.Token);
            await WriteWidget(surface.Output, System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(expected, WidgetJson), lifetime.Token);
            Assert.Equal(expected, await captureRead.WaitAsync(lifetime.Token));
            Assert.Equal(expected, await surfaceRead.WaitAsync(lifetime.Token));
        }
        finally { lifetime.Cancel(); try { await captureRead; } catch { } try { await surfaceRead; } catch { } }
    }

    [Fact]
    public async Task Widget_partial_header_and_payload_keep_one_five_second_deadline()
    {
        using var headerPair = await Pair.CreateAsync(); using var payloadPair = await Pair.CreateAsync();
        using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(12));
        var headerRead = LinuxOriginalControlFrameReader.ReadWidgetAsync<Packet>(headerPair.Input, 64 * 1024, WidgetJson, lifetime.Token);
        var payloadRead = LinuxOriginalControlFrameReader.ReadWidgetAsync<Packet>(payloadPair.Input, 384 * 1024, WidgetJson, lifetime.Token);
        try
        {
            await headerPair.Output.WriteAsync(new byte[] { 0 }, lifetime.Token);
            var header = new byte[4]; BinaryPrimitives.WriteInt32BigEndian(header, 40);
            await payloadPair.Output.WriteAsync(header, lifetime.Token);
            await payloadPair.Output.WriteAsync(new byte[] { (byte)'{' }, lifetime.Token);
            await Task.Delay(TimeSpan.FromSeconds(3), lifetime.Token);
            await headerPair.Output.WriteAsync(new byte[] { 0 }, lifetime.Token);
            await payloadPair.Output.WriteAsync(new byte[] { (byte)' ' }, lifetime.Token);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => headerRead.WaitAsync(TimeSpan.FromSeconds(3.5), lifetime.Token));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => payloadRead.WaitAsync(TimeSpan.FromSeconds(3.5), lifetime.Token));
            Assert.False(lifetime.IsCancellationRequested);
        }
        finally { lifetime.Cancel(); try { await headerRead; } catch { } try { await payloadRead; } catch { } }
    }

    [Fact]
    public async Task Widget_idle_cancellation_and_eof_obey_original_lifetime()
    {
        using var sockets = await Pair.CreateAsync(); using var lifetime = new CancellationTokenSource();
        var pending = LinuxOriginalControlFrameReader.ReadWidgetAsync<Packet>(sockets.Input, 64 * 1024, WidgetJson, lifetime.Token);
        lifetime.Cancel(); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        sockets.Output.Dispose();
        await Assert.ThrowsAsync<EndOfStreamException>(() => LinuxOriginalControlFrameReader.ReadWidgetAsync<Packet>(sockets.Input, 384 * 1024, WidgetJson, default));
    }

    [Fact]
    public async Task Widget_protocol_limits_refuse_header_without_reading_payload()
    {
        using var sockets = await Pair.CreateAsync(); using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => LinuxOriginalControlFrameReader.ReadWidgetAsync<Packet>(sockets.Input, 16384, WidgetJson, lifetime.Token));
        foreach (var limit in new[] { 64 * 1024, 384 * 1024 })
        {
            var header = new byte[4]; BinaryPrimitives.WriteInt32BigEndian(header, limit + 1);
            await sockets.Output.WriteAsync(header, lifetime.Token);
            await Assert.ThrowsAsync<InvalidDataException>(() => LinuxOriginalControlFrameReader.ReadWidgetAsync<Packet>(sockets.Input, limit, WidgetJson, lifetime.Token));
        }
        var zero = new byte[4]; await sockets.Output.WriteAsync(zero, lifetime.Token);
        await Assert.ThrowsAsync<InvalidDataException>(() => LinuxOriginalControlFrameReader.ReadWidgetAsync<Packet>(sockets.Input, 64 * 1024, WidgetJson, lifetime.Token));
    }

    [Fact]
    public async Task Widget_null_depth_and_large_surface_preserve_protocol_json_rules()
    {
        using var sockets = await Pair.CreateAsync(); using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await WriteWidget(sockets.Output, "null"u8.ToArray(), lifetime.Token);
        await Assert.ThrowsAsync<InvalidDataException>(() => LinuxOriginalControlFrameReader.ReadWidgetAsync<Packet>(sockets.Input, 64 * 1024, WidgetJson, lifetime.Token));
        var validDepth = new string('[', 12) + "1" + new string(']', 12);
        await WriteWidget(sockets.Output, System.Text.Encoding.UTF8.GetBytes(validDepth), lifetime.Token);
        var nested = await LinuxOriginalControlFrameReader.ReadWidgetAsync<System.Text.Json.JsonElement>(sockets.Input, 64 * 1024, WidgetJson, lifetime.Token);
        Assert.Equal(System.Text.Json.JsonValueKind.Array, nested.ValueKind);
        var refusedDepth = new string('[', 21) + "1" + new string(']', 21);
        await WriteWidget(sockets.Output, System.Text.Encoding.UTF8.GetBytes(refusedDepth), lifetime.Token);
        await Assert.ThrowsAsync<System.Text.Json.JsonException>(() => LinuxOriginalControlFrameReader.ReadWidgetAsync<System.Text.Json.JsonElement>(sockets.Input, 64 * 1024, WidgetJson, lifetime.Token));
        // Actual surface payload exceeds the capture ceiling but stays below the unchanged surface ceiling.
        var expected = new string('x', 70000);
        var payload = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(expected, WidgetJson);
        var reading = LinuxOriginalControlFrameReader.ReadWidgetAsync<string>(sockets.Input, 384 * 1024, WidgetJson, lifetime.Token);
        await WriteWidget(sockets.Output, payload, lifetime.Token);
        Assert.Equal(expected, await reading.WaitAsync(lifetime.Token));
    }

    private static async Task WriteWidget(Stream stream, byte[] payload, CancellationToken ct)
    {
        var header = new byte[4]; BinaryPrimitives.WriteInt32BigEndian(header, payload.Length);
        await stream.WriteAsync(header, ct); await stream.WriteAsync(payload, ct); await stream.FlushAsync(ct);
    }
    // END widget-only closest-test region.

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
