using System.Buffers.Binary;
using System.Net.Sockets;
using System.Text.Json;

namespace HavenOS.Home.Core;

public sealed record HomeNativeDiscoveryRequest(IReadOnlyList<HomeServiceRequirement> Requirements);
public sealed record HomeNativeDiscoveryResponse(string Code, HomeCoreStateSnapshot? Snapshot);

/// <summary>Bounded, discovery-only Linux local IPC. No permission decisions or resource mutations are exposed.</summary>
public static class HomeUnixDiscoveryTransport
{
    private const int MaximumMessageBytes = 64 * 1024;
    private static readonly JsonSerializerOptions Json = new() { MaxDepth = 24 };

    /// <summary>The designated leased host supplies the actual accepted socket; peer identity never comes from request fields.</summary>
    public static async Task ServeAcceptedAsync(Socket accepted, HomeNativeDiscoverySession discovery,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(accepted); ArgumentNullException.ThrowIfNull(discovery);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(5));
        var peer = HomeNativePeerObservation.FromAcceptedUnixSocket(accepted);
        if (peer is null) return;
        using var stream = new NetworkStream(accepted, ownsSocket: false);
        HomeNativeDiscoveryResponse response;
        try
        {
            var request = JsonSerializer.Deserialize<HomeNativeDiscoveryRequest>(await ReadFrameAsync(stream, deadline.Token).ConfigureAwait(false), Json);
            var snapshot = request is null ? null : await discovery.DiscoverAsync(peer, request.Requirements, deadline.Token).ConfigureAwait(false);
            response = new(snapshot is null ? "HomePeerOrServiceUnavailable" : "HomeDiscoveryReady", snapshot);
        }
        catch (JsonException) { response = new("HomeDiscoveryRequestInvalid", null); }
        catch (InvalidDataException) { response = new("HomeDiscoveryRequestInvalid", null); }
        var payload = JsonSerializer.SerializeToUtf8Bytes(response, Json);
        if (payload.Length > MaximumMessageBytes) payload = JsonSerializer.SerializeToUtf8Bytes(new HomeNativeDiscoveryResponse("HomeDiscoveryResponseTooLarge", null), Json);
        await WriteFrameAsync(stream, payload, deadline.Token).ConfigureAwait(false);
    }

    internal static async Task<byte[]> ReadFrameAsync(Stream stream, CancellationToken cancellationToken)
    {
        var header = new byte[4]; await stream.ReadExactlyAsync(header, cancellationToken).ConfigureAwait(false);
        var length = BinaryPrimitives.ReadInt32BigEndian(header);
        if (length <= 0 || length > MaximumMessageBytes) throw new InvalidDataException("Home discovery frame exceeds the bounded protocol.");
        var payload = new byte[length]; await stream.ReadExactlyAsync(payload, cancellationToken).ConfigureAwait(false); return payload;
    }
    internal static async Task WriteFrameAsync(Stream stream, byte[] payload, CancellationToken cancellationToken)
    {
        var header = new byte[4]; BinaryPrimitives.WriteInt32BigEndian(header, payload.Length);
        await stream.WriteAsync(header, cancellationToken).ConfigureAwait(false);
        await stream.WriteAsync(payload, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }
}
