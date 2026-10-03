using System.Buffers.Binary;
using System.Net.Sockets;
using System.Text.Json;
using Haven.Application;
using HavenOS.Home.Core;

namespace NineToOne.Os.Shell.Authority;

public sealed record LinuxNativeWidgetRegistration(int Schema, IReadOnlyList<HomeNativeWidgetDefinition> Definitions);
/// <summary>OriginalActor is observational context only. Owner backend must independently match its actual original
/// authenticated actor and authorize data scopes; received actor fields cannot create authority.</summary>
public sealed record LinuxNativeWidgetCapture(Guid RequestId, HomeNativeWidgetCaptureRequest Request,
    AuthenticatedResourceActor OriginalActor);
public sealed record LinuxNativeWidgetSurface(Guid RequestId, HomeNativeWidgetReference Reference,
    string SurfaceReference, string AuthoredCui, Dictionary<string, JsonElement> Data);
public sealed record LinuxNativeWidgetRegistrationResult(int Schema, string Code);

/// <summary>Actual accepted kernel peer and connected endpoint share one lifetime. Wire data never supplies
/// a PID, principal, actor, installed owner or execution authority. Capture/display only.</summary>
public static class LinuxNativeWidgetAcceptedTransport
{
    private const int RegistrationLimit = 64 * 1024;
    private const int SurfaceLimit = 384 * 1024;
    private static readonly JsonSerializerOptions Json = new() { MaxDepth = 20 };

    public static async Task ServeAcceptedAsync(Socket accepted, HomeNativeWidgetRegistry registry,
        IAuthenticatedResourceActorSource actors, Func<bool> originalHostCurrent, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(accepted); ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(actors); ArgumentNullException.ThrowIfNull(originalHostCurrent);
        var observed = HomeNativePeerObservation.FromAcceptedUnixSocket(accepted);
        if (observed is null || !originalHostCurrent()) return;
        var original = await actors.GetCurrentAsync(ct).ConfigureAwait(false);
        if (original is null || !originalHostCurrent()) return;
        using var stream = new NetworkStream(accepted, ownsSocket: false);
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(ct);
        using var registrationDeadline = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        registrationDeadline.CancelAfter(TimeSpan.FromSeconds(5));
        var request = JsonSerializer.Deserialize<LinuxNativeWidgetRegistration>(
            await ReadAsync(stream, RegistrationLimit, registrationDeadline.Token).ConfigureAwait(false), Json);
        if (request is not { Schema: 1 } || request.Definitions is null || !originalHostCurrent() ||
            original != await actors.GetCurrentAsync(registrationDeadline.Token).ConfigureAwait(false)) return;
        if (!originalHostCurrent()) return;
        using var endpoint = new Endpoint(stream, lifetime, originalHostCurrent, original);
        using var registration = await registry.RegisterRuntimeForActorAsync(observed, request.Definitions,
            endpoint, original, registrationDeadline.Token).ConfigureAwait(false);
        if (registration is null || !originalHostCurrent() ||
            original != await actors.GetCurrentAsync(registrationDeadline.Token).ConfigureAwait(false)) return;
        if (!originalHostCurrent()) return;
        await endpoint.SendEnrollmentAsync(registrationDeadline.Token).ConfigureAwait(false);
        // Exactly one reader owns framing. EOF/invalid unsolicited response tears down this registration.
        await endpoint.ReadResponsesAsync().ConfigureAwait(false);
    }

    private sealed class Endpoint(Stream stream, CancellationTokenSource lifetime,
        Func<bool> originalHostCurrent, AuthenticatedResourceActor originalActor) : IHomeNativeWidgetRuntimeEndpoint, IDisposable
    {
        private readonly SemaphoreSlim _capture = new(1, 1);
        private readonly SemaphoreSlim _writes = new(1, 1);
        private readonly object _sync = new();
        private Guid _requestId;
        private TaskCompletionSource<LinuxNativeWidgetSurface>? _pending;
        private bool _closed, _ready;
        public async Task SendEnrollmentAsync(CancellationToken ct)
        {
            await _writes.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                lock (_sync) { if (_closed || !originalHostCurrent()) throw new OperationCanceledException(ct); _ready = true; }
                // Captures may now be admitted, but their frames wait for the actual acknowledgement write.
                await WriteAsync(stream, JsonSerializer.SerializeToUtf8Bytes(
                    new LinuxNativeWidgetRegistrationResult(1, "RegisteredLiveOwner"), Json), RegistrationLimit, ct).ConfigureAwait(false);
            }
            finally { _writes.Release(); }
        }
        public async ValueTask<HomeNativeWidgetSurface?> CaptureAsync(HomeNativeWidgetCaptureRequest request,
            CancellationToken ct)
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct, lifetime.Token);
            deadline.CancelAfter(TimeSpan.FromSeconds(5));
            await _capture.WaitAsync(deadline.Token).ConfigureAwait(false);
            try
            {
                TaskCompletionSource<LinuxNativeWidgetSurface> pending;
                Guid id;
                lock (_sync)
                {
                    if (_closed || !_ready || !originalHostCurrent()) return null;
                    id = Guid.NewGuid(); _requestId = id;
                    pending = new(TaskCreationOptions.RunContinuationsAsynchronously); _pending = pending;
                }
                await _writes.WaitAsync(deadline.Token).ConfigureAwait(false);
                try
                {
                    await WriteAsync(stream, JsonSerializer.SerializeToUtf8Bytes(new LinuxNativeWidgetCapture(id, request, originalActor), Json),
                        RegistrationLimit, deadline.Token).ConfigureAwait(false);
                }
                finally { _writes.Release(); }
                var response = await pending.Task.WaitAsync(deadline.Token).ConfigureAwait(false);
                if (!originalHostCurrent() || response.Reference != request.Reference ||
                    response.SurfaceReference != request.SurfaceReference) return null;
                return HomeNativeWidgetSurface.Capture(request.Reference, request.SurfaceReference,
                    response.AuthoredCui, response.Data);
            }
            catch
            {
                // A timed-out response cannot satisfy any later capture on this connection.
                Close(); throw;
            }
            finally
            {
                lock (_sync) { _pending = null; _requestId = Guid.Empty; }
                _capture.Release();
            }
        }
        public async Task ReadResponsesAsync()
        {
            try
            {
                while (!lifetime.IsCancellationRequested)
                {
                    var response = await LinuxOriginalControlFrameReader.ReadWidgetAsync<LinuxNativeWidgetSurface>(
                        stream, SurfaceLimit, Json, lifetime.Token).ConfigureAwait(false);
                    lock (_sync)
                    {
                        if (_closed || !originalHostCurrent() || response is null || _pending is null ||
                            response.RequestId != _requestId || !_pending.TrySetResult(response))
                            throw new InvalidDataException("Unexpected widget response does not belong to the original capture.");
                    }
                }
            }
            finally { Close(); }
        }
        private void Close()
        {
            lock (_sync)
            {
                if (_closed) return; _closed = true;
                _pending?.TrySetCanceled(lifetime.Token);
            }
            lifetime.Cancel();
        }
        public void Dispose() => Close();
    }
    private static async Task<byte[]> ReadAsync(Stream stream, int limit, CancellationToken ct)
    {
        var header = new byte[4]; await stream.ReadExactlyAsync(header, ct).ConfigureAwait(false);
        var length = BinaryPrimitives.ReadInt32BigEndian(header);
        if (length <= 0 || length > limit) throw new InvalidDataException("Widget frame exceeds the bounded protocol.");
        var bytes = new byte[length]; await stream.ReadExactlyAsync(bytes, ct).ConfigureAwait(false); return bytes;
    }
    private static async Task WriteAsync(Stream stream, byte[] bytes, int limit, CancellationToken ct)
    {
        if (bytes.Length == 0 || bytes.Length > limit) throw new InvalidDataException("Widget frame exceeds the bounded protocol.");
        var header = new byte[4]; BinaryPrimitives.WriteInt32BigEndian(header, bytes.Length);
        await stream.WriteAsync(header, ct).ConfigureAwait(false); await stream.WriteAsync(bytes, ct).ConfigureAwait(false);
        await stream.FlushAsync(ct).ConfigureAwait(false);
    }
}
