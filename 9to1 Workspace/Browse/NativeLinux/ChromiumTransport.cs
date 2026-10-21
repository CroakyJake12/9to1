using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace HavenOS.Apps.Browse;

/// <summary>
/// Minimal serialized transport over one DevTools WebSocket. It is abstracted so
/// the connection mux (see <see cref="ChromiumClient"/>) can be exercised with a
/// fake transport without launching a Chromium process. The production transport
/// wraps a <see cref="ClientWebSocket"/> and carries whole text messages.
/// </summary>
internal interface IChromiumTransport : IAsyncDisposable
{
    /// <summary>Opens the WebSocket to the validated DevTools endpoint.</summary>
    Task ConnectAsync(Uri endpoint, CancellationToken cancellationToken);

    /// <summary>Sends one whole text message.</summary>
    Task SendAsync(ReadOnlyMemory<byte> message, CancellationToken cancellationToken);

    /// <summary>Receives the next message fragment; null when the peer closed or
    /// the socket failed.</summary>
    Task<WebSocketReceiveResult?> ReceiveAsync(Memory<byte> buffer, CancellationToken cancellationToken);
}

/// <summary>Production <see cref="IChromiumTransport"/> over ClientWebSocket.</summary>
internal sealed class ClientWebSocketTransport : IChromiumTransport
{
    private readonly ClientWebSocket _socket = new();

    public Task ConnectAsync(Uri endpoint, CancellationToken cancellationToken) =>
        _socket.ConnectAsync(endpoint, cancellationToken);

    public async Task SendAsync(ReadOnlyMemory<byte> message, CancellationToken cancellationToken)
    {
        await _socket.SendAsync(message, WebSocketMessageType.Text, endOfMessage: true, cancellationToken).ConfigureAwait(false);
    }

    public async Task<WebSocketReceiveResult?> ReceiveAsync(Memory<byte> buffer, CancellationToken cancellationToken)
    {
        try
        {
            // ClientWebSocket.ReceiveAsync(Memory<byte>, …) yields a
            // ValueWebSocketReceiveResult; adapt it to WebSocketReceiveResult so the
            // transport contract stays a stable class type (avoids a literal type
            // mismatch that would otherwise fail compilation).
            var result = await _socket.ReceiveAsync(buffer, cancellationToken).ConfigureAwait(false);
            return new WebSocketReceiveResult(result.Count, result.MessageType, result.EndOfMessage);
        }
        catch (WebSocketException) { return null; }
    }

    public ValueTask DisposeAsync()
    {
        _socket.Dispose();
        return ValueTask.CompletedTask;
    }
}

/// <summary>
/// Validates a stderr-discovered DevTools endpoint before any connection is made.
/// Only a loopback, random-port, owned browser endpoint (ws://127.0.0.1:PORT/devtools/browser/…)
/// is accepted; a hostile or misconfigured child cannot redirect us elsewhere.
/// </summary>
internal static class ChromiumEndpointPolicy
{
    public static Uri RequireValid(string endpointUri)
    {
        if (string.IsNullOrWhiteSpace(endpointUri) ||
            !Uri.TryCreate(endpointUri, UriKind.Absolute, out var uri) ||
            uri.Scheme != "ws")
            throw new InvalidOperationException("The DevTools endpoint is not a web-socket URL.");

        var host = uri.Host;
        if (!(host == "127.0.0.1" || host == "::1" || string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("The DevTools endpoint is not loopback.");

        if (uri.Port is < 1 or > 65535)
            throw new InvalidOperationException("The DevTools endpoint port is not valid.");

        if (!uri.AbsolutePath.StartsWith("/devtools/browser/", StringComparison.Ordinal))
            throw new InvalidOperationException("The DevTools endpoint is not an owned browser endpoint.");

        return uri;
    }
}

/// <summary>
/// The multiplexed DevTools client for one owned browser endpoint: it correlates
/// request ids, forwards real CDP events to <see cref="Recv"/>, serializes every
/// shared send, and settles every pending command when the socket closes, fails,
/// or the lifetime is cancelled.
/// </summary>
internal sealed class ChromiumClient : IChromiumEndpoint
{
    private const int MaxFrameBytes = 16 * 1024 * 1024;

    private readonly IChromiumTransport _transport;
    private readonly ChromiumCdpRouter _router = new();
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private readonly object _gate = new();
    private Task? _receiveLoop, _closeTask;

    public ChromiumClient(IChromiumTransport transport)
    {
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _router.Raised += OnRouterRaised;
    }

    public event EventHandler<CdpEvent>? Recv;

    private void OnRouterRaised(object? sender, CdpEvent e) => Recv?.Invoke(this, e);

    /// <summary>Connects to a validated endpoint and starts the receive loop.</summary>
    public async Task StartAsync(Uri endpoint, CancellationToken cancellationToken)
    {
        await _transport.ConnectAsync(endpoint, cancellationToken).ConfigureAwait(false);
        _receiveLoop = ReceiveLoopAsync(_lifetime.Token);
    }

    public async Task<JsonElement> SendAsync(string? sessionId, string method, object? parameters, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var id = _router.NextId();
        var pending = _router.WaitAsync(id, cancellationToken);
        try
        {
            var requestBytes = Encoding.UTF8.GetBytes(ChromiumCdpWire.CreateRequest(id, method, parameters, sessionId));
            await _sendLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await _transport.SendAsync(requestBytes, cancellationToken).ConfigureAwait(false);
            }
            finally { _sendLock.Release(); }
        }
        catch
        {
            // A failed/cancelled send must not leave its pending id behind.
            _router.Cancel(id);
            throw;
        }
        return await pending.ConfigureAwait(false);
    }

    public async Task<string> CreateTargetAsync(string url, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(url);
        var parameters = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["url"] = url,
            ["width"] = 1280,
            ["height"] = 800
        };
        var result = await SendAsync(null, "Target.createTarget", parameters, cancellationToken).ConfigureAwait(false);
        return result.GetProperty("targetId").GetString()!;
    }

    public async Task<string> AttachAsync(string targetId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetId);
        var parameters = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["targetId"] = targetId,
            ["flatten"] = true
        };
        var result = await SendAsync(null, "Target.attachToTarget", parameters, cancellationToken).ConfigureAwait(false);
        return result.GetProperty("sessionId").GetString()!;
    }

    private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
    {
        var buffer = new byte[64 * 1024];
        using var stream = new MemoryStream();
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                stream.SetLength(0);
                WebSocketReceiveResult? result;
                do
                {
                    result = await _transport.ReceiveAsync(buffer, cancellationToken).ConfigureAwait(false);
                    if (result is null) return;
                    if (result.MessageType == WebSocketMessageType.Close) return;
                    if (stream.Length + result.Count > MaxFrameBytes)
                        throw new InvalidOperationException("A CDP frame exceeded the 16 MB bound.");
                    stream.Write(buffer, 0, result.Count);
                }
                while (!result.EndOfMessage);

                var json = Encoding.UTF8.GetString(stream.GetBuffer(), 0, (int)stream.Length);
                try
                {
                    using var document = JsonDocument.Parse(json);
                    _router.Route(document);
                }
                catch (JsonException) { /* malformed child frame is bounded and ignored */ }
            }
        }
        catch (OperationCanceledException) { }
        catch (WebSocketException) { }
        finally
        {
            _router.CancelAll();
        }
    }

    public ValueTask DisposeAsync()
    {
        lock (_gate)
        {
            if (_closeTask is not null) return new(_closeTask);
            _closeTask = CloseCoreAsync();
        }
        return new(_closeTask);
    }

    private async Task CloseCoreAsync()
    {
        _lifetime.Cancel();
        _router.CancelAll();
        var failures = new List<Exception>();
        async Task Join(Task? task) { if (task is null) return; try { await task.ConfigureAwait(false); } catch (Exception failure) { failures.Add(failure); } }
        try { await Join(_receiveLoop); }
        finally { try { await _transport.DisposeAsync().ConfigureAwait(false); } catch (Exception failure) { failures.Add(failure); } }
        _sendLock.Dispose();
        _lifetime.Dispose();
        if (failures.Count != 0) throw new AggregateException("Chromium retained its transport source failures.", failures);
    }
}
