using System.Buffers.Binary;
using System.Text.Json;

namespace NineToOne.Os.Shell.Authority;

// Framing only. Idle connections acquire no actor, process or lease authority.
// Every request still needs its original-context checks in the owning channel.
internal static class LinuxOriginalControlFrameReader
{
    private const int MaximumFrame = 16384;
    private static readonly JsonSerializerOptions Json = new() { MaxDepth = 8 };

    internal static async Task<T> ReadAsync<T>(Stream stream, CancellationToken lifetime)
    {
        ArgumentNullException.ThrowIfNull(stream);
        var header = new byte[4];
        // A healthy idle channel is cancellation/EOF bound, not timer bound.
        await stream.ReadExactlyAsync(header.AsMemory(0, 1), lifetime).ConfigureAwait(false);
        using var frame = CancellationTokenSource.CreateLinkedTokenSource(lifetime);
        // One absolute deadline after first ingress; further bytes never renew it.
        frame.CancelAfter(TimeSpan.FromSeconds(60));
        await stream.ReadExactlyAsync(header.AsMemory(1, 3), frame.Token).ConfigureAwait(false);
        var size = BinaryPrimitives.ReadInt32BigEndian(header);
        if (size is < 1 or > MaximumFrame)
            throw new InvalidDataException("Bounded original control frame required.");
        var bytes = new byte[size];
        await stream.ReadExactlyAsync(bytes, frame.Token).ConfigureAwait(false);
        frame.Token.ThrowIfCancellationRequested();
        var parsed = JsonSerializer.Deserialize<T>(bytes, Json)
            ?? throw new InvalidDataException("Null original control frame denied.");
        frame.Token.ThrowIfCancellationRequested();
        return parsed;
    }
}
