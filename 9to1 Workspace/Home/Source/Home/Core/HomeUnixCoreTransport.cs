using System.Buffers.Binary;
using System.Net.Sockets;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using System.Threading.Channels;
using Haven.Application;

namespace HavenOS.Home.Core;

/// <summary>Additive accepting-host seam, not a listener or a default DI registration.
/// One original reader owns every frame on the SAME accepted socket. Legacy discovery
/// remains the default. Core authority is issued only by the canonical original-session issuer.</summary>
public static class HomeUnixCoreTransport
{
    public static async Task ServeAcceptedAsync(Socket originalAcceptedSocket,
        HomeNativeDiscoverySession legacyDiscovery, HomeNativeCoreApiSessions sessions,
        HomeNativeSessionLease originalHeldLease, CancellationToken originalConnectionLifetime)
    {
        ArgumentNullException.ThrowIfNull(originalAcceptedSocket);
        ArgumentNullException.ThrowIfNull(legacyDiscovery);
        ArgumentNullException.ThrowIfNull(sessions);
        ArgumentNullException.ThrowIfNull(originalHeldLease);
        if (!originalConnectionLifetime.CanBeCanceled)
            throw new ArgumentException("An original connection lifetime is required.", nameof(originalConnectionLifetime));
        var lifetime = CancellationTokenSource.CreateLinkedTokenSource(originalConnectionLifetime);
        NetworkStream? stream = null;
        HomeNativeCoreApiSessions.Session? session = null;
        Task? originalReader = null;
        Task? originalPublication = null;
        List<Exception> failures = [];
        try
        {
            stream = new NetworkStream(originalAcceptedSocket, ownsSocket: false);
            using var firstDeadline = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
            firstDeadline.CancelAfter(TimeSpan.FromSeconds(5));
            var first = await HomeUnixDiscoveryTransport.ReadFrameAsync(stream, firstDeadline.Token).ConfigureAwait(false);
            bool core;
            try { core = HomeUnixCoreProtocol.HasProtocol(first); }
            catch (JsonException) { core = false; } // Same legacy invalid-request response.
            if (!core)
            {
                await HomeUnixDiscoveryTransport.ServeAcceptedFirstFrameAsync(originalAcceptedSocket,
                    legacyDiscovery, first, firstDeadline.Token).ConfigureAwait(false);
            }
            else
            {
            var initial = HomeUnixCoreProtocol.ReadRequest(first);
            session = await sessions.AcceptUnixAsync(originalAcceptedSocket, originalHeldLease,
                lifetime.Token, lifetime.Token).ConfigureAwait(false)
                ?? throw new UnauthorizedAccessException("Original installed Home Core session is unavailable.");
            var frames = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(1)
            {
                SingleReader = true, SingleWriter = true, FullMode = BoundedChannelFullMode.Wait,
            });
            // Admission consumes no socket data. This is the sole continuing socket reader.
            originalReader = ReadOriginalAsync(stream, frames.Writer, lifetime);
            var request = initial;
            while (true)
            {
                var payload = await DispatchAsync(session, request, lifetime.Token).ConfigureAwait(false);
                originalPublication = PublishOriginalAsync(stream, session, payload, lifetime.Token);
                await originalPublication.ConfigureAwait(false);
                if (!await frames.Reader.WaitToReadAsync(lifetime.Token).ConfigureAwait(false)) break;
                request = HomeUnixCoreProtocol.ReadRequest(await frames.Reader.ReadAsync(lifetime.Token).ConfigureAwait(false));
            }
            }
        }
        catch (OperationCanceledException error) when (originalConnectionLifetime.IsCancellationRequested &&
            lifetime.IsCancellationRequested && error.CancellationToken == lifetime.Token)
        {
            // Only this exact private transport token is normalized to its original owner.
            // Foreign/API cancellation retains its original identity and token.
            Add(failures, new OperationCanceledException("Original Home connection ended.", error, originalConnectionLifetime));
        }
        catch (Exception error) { Add(failures, error); }
        finally
        {
            try { lifetime.Cancel(); } catch (Exception error) { Add(failures, error); }
            // Start retirement before reader await; both exact originals stay in custody.
            Task? sessionDrain = null;
            try { if (session is not null) sessionDrain = session.DisposeAsync().AsTask(); }
            catch (Exception error) { Add(failures, error); }
            try { if (originalReader is not null) await originalReader.ConfigureAwait(false); }
            catch (Exception error) { Add(failures, error); }
            try { if (sessionDrain is not null) await sessionDrain.ConfigureAwait(false); }
            catch (Exception error) { Add(failures, error); }
            try { await OriginalPublicationDrain(originalPublication).ConfigureAwait(false); }
            catch (Exception error) { Add(failures, error); }
            try { stream?.Dispose(); } catch (Exception error) { Add(failures, error); }
            try { lifetime.Dispose(); } catch (Exception error) { Add(failures, error); }
        }
        Throw(failures);
    }

    // Actual accepting path invokes this after bounded serialization. The separate helper
    // lets real-socket fixtures observe a failed fence without inventing a wire identity.
    internal static async Task PublishOriginalAsync(Stream originalStream,
        HomeNativeCoreApiSessions.Session sameSession, byte[] serializedPayload, CancellationToken lifetime)
    {
        if (serializedPayload.Length is 0 or > HomeUnixCoreProtocol.MaximumMessageBytes)
            throw new InvalidDataException("Home Core reply exceeds the transport bound.");
        // Currentness linearizes at this completed check. Later revocation may race with
        // bytes already admitted to a write. The SAME Task stays connection-owned/drained.
        await sameSession.DemandOriginalCurrentAsync(lifetime).ConfigureAwait(false);
        await HomeUnixDiscoveryTransport.WriteFrameAsync(originalStream, serializedPayload, lifetime).ConfigureAwait(false);
    }
    internal static Task OriginalPublicationDrain(Task? sameOriginalPublication) => sameOriginalPublication ?? Task.CompletedTask;

    private static async Task<byte[]> DispatchAsync(HomeNativeCoreApiSessions.Session session,
        HomeUnixCoreRequest request, CancellationToken token) => request.Operation switch
    {
        "GetState" => HomeUnixCoreProtocol.Payload(request, await session.GetStateAsync(token).ConfigureAwait(false)),
        "GetServices" => HomeUnixCoreProtocol.Payload(request, await session.GetServicesAsync(token).ConfigureAwait(false)),
        "GetService" => HomeUnixCoreProtocol.Payload(request, await session.GetServiceAsync(request.ServiceId!, token).ConfigureAwait(false)),
        "GetCompatibility" => HomeUnixCoreProtocol.Payload(request, await session.GetCompatibilityAsync(request.Compatibility!, token).ConfigureAwait(false)),
        _ => throw new InvalidDataException("Unsupported Home Core operation."),
    };

    private static async Task ReadOriginalAsync(Stream originalStream, ChannelWriter<byte[]> frames,
        CancellationTokenSource lifetime)
    {
        Exception? primary = null;
        try
        {
            while (true)
            {
                var payload = await ReadAfterFirstFrameAsync(originalStream, lifetime.Token).ConfigureAwait(false);
                if (payload is null) return;
                if (!frames.TryWrite(payload))
                    throw new InvalidDataException("Home Core pipeline exceeds one retained frame.");
            }
        }
        catch (OperationCanceledException error) when (lifetime.IsCancellationRequested && error.CancellationToken == lifetime.Token)
        { /* Exact reader-owned cancellation; original read already settled. */ }
        catch (Exception error) { primary = error; throw; }
        finally
        {
            frames.TryComplete(primary);
            try { lifetime.Cancel(); }
            catch (Exception cleanup) when (primary is not null && !ReferenceEquals(primary, cleanup))
            { throw new AggregateException("Original frame reader and cancellation both failed.", primary, cleanup); }
        }
    }

    /// <summary>Idle waits use the original lifetime. A non-empty frame has one absolute
    /// five-second bound after its first byte, with the original big-endian/64 KiB contract.</summary>
    internal static async ValueTask<byte[]?> ReadAfterFirstFrameAsync(Stream stream, CancellationToken lifetime)
    {
        var header = new byte[4];
        if (await stream.ReadAsync(header.AsMemory(0, 1), lifetime).ConfigureAwait(false) == 0) return null;
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var active = CancellationTokenSource.CreateLinkedTokenSource(lifetime, deadline.Token);
        try
        {
            await stream.ReadExactlyAsync(header.AsMemory(1, 3), active.Token).ConfigureAwait(false);
            var length = BinaryPrimitives.ReadInt32BigEndian(header);
            if (length is <= 0 or > HomeUnixCoreProtocol.MaximumMessageBytes)
                throw new InvalidDataException("Home Core frame length is invalid.");
            var payload = new byte[length];
            await stream.ReadExactlyAsync(payload, active.Token).ConfigureAwait(false);
            lifetime.ThrowIfCancellationRequested();
            return payload;
        }
        // An elapsed real deadline remains a timeout even if owner cancellation occurs
        // while the original read continuation is held. Both requested is conservatively
        // a timeout; late token booleans do not prove which cancellation happened first.
        catch (OperationCanceledException error) when (deadline.IsCancellationRequested && active.IsCancellationRequested && error.CancellationToken == active.Token)
        { throw new TimeoutException("Home Core partial frame deadline exceeded.", error); }
        catch (OperationCanceledException error) when (!deadline.IsCancellationRequested && lifetime.IsCancellationRequested && active.IsCancellationRequested && error.CancellationToken == active.Token)
        { throw new OperationCanceledException("Original Home frame lifetime ended.", error, lifetime); }
    }
    internal static void Add(List<Exception> failures, Exception error)
    {
        if (!failures.Any(row => ReferenceEquals(row, error))) failures.Add(error);
    }
    internal static void Throw(List<Exception> failures)
    {
        if (failures.Count == 1) ExceptionDispatchInfo.Capture(failures[0]).Throw();
        if (failures.Count > 1) throw new AggregateException("Original Home transport and cleanup failures retained.", failures);
    }
}
