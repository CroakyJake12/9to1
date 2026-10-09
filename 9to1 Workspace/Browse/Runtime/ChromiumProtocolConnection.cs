using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text.Json;
using System.Threading.Channels;

namespace HavenOS.Apps.Browse.Runtime;

/// <summary>Private, bounded CDP transport to a Browse-owned Chromium process, never a page-exposed API.</summary>
internal sealed class ChromiumProtocolConnection : IAsyncDisposable
{
    private readonly ClientWebSocket _socket = new();
    private readonly SemaphoreSlim _send = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private readonly ConcurrentDictionary<long, TaskCompletionSource<JsonElement>> _pending = new();
    private readonly Channel<JsonElement> _events = Channel.CreateBounded<JsonElement>(new BoundedChannelOptions(2048)
    {
        FullMode = BoundedChannelFullMode.Wait, SingleReader = true, SingleWriter = true
    });
    private Task _reader = Task.CompletedTask;
    private Task _dispatcher = Task.CompletedTask;
    private long _nextId;
    private int _disposed;
    public event Func<JsonElement, Task>? EventReceived;
    public event Action<Exception>? Disconnected;

    public static async Task<ChromiumProtocolConnection> ConnectAsync(Uri endpoint, CancellationToken cancellationToken)
    {
        if (endpoint.Scheme != "ws" || endpoint.Host != "127.0.0.1" ||
            !endpoint.AbsolutePath.StartsWith("/devtools/browser/", StringComparison.Ordinal) ||
            endpoint.UserInfo.Length != 0 || endpoint.Query.Length != 0 || endpoint.Fragment.Length != 0)
            throw new ArgumentException("Only the owned process's loopback browser endpoint is accepted.", nameof(endpoint));
        var connection = new ChromiumProtocolConnection();
        connection._socket.Options.Proxy = null;
        try
        {
            await connection._socket.ConnectAsync(endpoint, cancellationToken).ConfigureAwait(false);
            connection._reader = connection.ReadAsync();
            connection._dispatcher = connection.DispatchAsync();
            return connection;
        }
        catch { await connection.DisposeAsync().ConfigureAwait(false); throw; }
    }

    public async Task<JsonElement> CallAsync(string method, object? parameters = null, string? sessionId = null,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        var id = Interlocked.Increment(ref _nextId);
        var promise = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_pending.TryAdd(id, promise)) throw new InvalidOperationException("Duplicate protocol request.");
        try
        {
            var payload = new Dictionary<string, object?> { ["id"] = id, ["method"] = method, ["params"] = parameters ?? new { } };
            if (sessionId is not null) payload["sessionId"] = sessionId;
            var bytes = JsonSerializer.SerializeToUtf8Bytes(payload);
            if (bytes.Length > 4 * 1024 * 1024) throw new ArgumentException("Protocol request exceeds 4 MiB.", nameof(parameters));
            await _send.WaitAsync(timeout.Token).ConfigureAwait(false);
            try { await _socket.SendAsync(bytes.AsMemory(), WebSocketMessageType.Text, true, timeout.Token).ConfigureAwait(false); }
            finally { _send.Release(); }
            return await promise.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
        }
        finally { _pending.TryRemove(id, out _); }
    }

    private async Task ReadAsync()
    {
        Exception failure = new IOException("Chromium's protocol connection closed.");
        try
        {
            var buffer = new byte[32 * 1024];
            while (!_lifetime.IsCancellationRequested)
            {
                using var message = new MemoryStream();
                ValueWebSocketReceiveResult chunk;
                do
                {
                    chunk = await _socket.ReceiveAsync(buffer.AsMemory(), _lifetime.Token).ConfigureAwait(false);
                    if (chunk.MessageType != WebSocketMessageType.Text) throw new IOException("Non-text or closed Chromium protocol stream.");
                    if (message.Length + chunk.Count > 16 * 1024 * 1024) throw new IOException("Chromium protocol response exceeds 16 MiB.");
                    message.Write(buffer, 0, chunk.Count);
                } while (!chunk.EndOfMessage);
                using var document = JsonDocument.Parse(message.ToArray());
                var root = document.RootElement.Clone();
                if (root.TryGetProperty("id", out var requestId))
                {
                    if (!_pending.TryRemove(requestId.GetInt64(), out var promise)) continue;
                    if (root.TryGetProperty("error", out var error))
                        promise.TrySetException(new InvalidOperationException("Chromium protocol error: " + error.GetRawText()));
                    else promise.TrySetResult(root.GetProperty("result"));
                }
                else if (!_events.Writer.TryWrite(root))
                    throw new IOException("Chromium protocol event queue exceeded its limit.");
            }
        }
        catch (Exception exception) when (exception is not OutOfMemoryException) { failure = exception; }
        finally
        {
            foreach (var entry in _pending) entry.Value.TrySetException(failure);
            _pending.Clear();
            _events.Writer.TryComplete();
            if (!_lifetime.IsCancellationRequested) Disconnected?.Invoke(failure);
        }
    }

    private async Task DispatchAsync()
    {
        try
        {
            await foreach (var message in _events.Reader.ReadAllAsync(_lifetime.Token).ConfigureAwait(false))
            {
                if (EventReceived is not { } handlers) continue;
                foreach (var handler in handlers.GetInvocationList().Cast<Func<JsonElement, Task>>())
                    await handler(message).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            if (!_lifetime.IsCancellationRequested) Disconnected?.Invoke(exception);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _lifetime.Cancel();
        _socket.Abort();
        await _reader.ConfigureAwait(false);
        // A callback may itself trigger disposal. Never wait on the dispatcher from that callback.
        _ = _dispatcher;
        _socket.Dispose();
        // In-flight calls still own their cancellation registrations and semaphore leases.
    }
}
