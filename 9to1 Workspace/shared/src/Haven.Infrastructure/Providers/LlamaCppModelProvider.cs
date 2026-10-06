using System.Buffers.Binary;
using System.Collections.Frozen;
using System.Collections.ObjectModel;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Haven.Application;
using Dulche.Runtime;
using Haven.Core;

namespace Haven.Infrastructure;

/// <summary>Explicit loaded-endpoint observation inputs, never a permission or capability flag.
/// This path is Linux AF_UNIX only; ordinary localhost Custom providers remain remote.</summary>
public sealed record LlamaCppLocalEndpointOptions(
    bool Enabled = false, string? SocketPath = null, string? ExpectedExecutableSha256 = null,
    string? ModelPath = null, string? ExpectedModelSha256 = null, string? ModelAlias = null);

/// <summary>One borrowed, kernel-identified local llama.cpp endpoint. It owns its HTTP work;
/// the external process owner must close this provider after Dulche/Task originals drain and
/// before terminating the server. Construction never sends, launches, loads, or grants use.</summary>
public sealed class LlamaCppModelProvider : IModelProvider, ILocalModelEndpointObservationSource, IOriginalInferenceEngineModelSource, IAsyncDisposable
{
    private const int Capacity = 128;
    private readonly object _sync = new();
    private readonly LlamaCppLocalEndpointOptions _options;
    private readonly IProviderConfigurationStore _configurations;
    private readonly HttpClient _http;
    private readonly CancellationTokenSource _stop = new();
    private readonly List<OriginalWork> _work = [];
    private readonly List<Exception> _errors = [];
    private readonly ConditionalWeakTable<LocalModelEndpointObservation, Peer> _observations = new();
    private readonly AsyncLocal<Phase?> _executing = new();
    [ThreadStatic] private static List<LlamaCppModelProvider>? _physical;
    private Peer? _peer;
    private LocalModelEndpointObservation? _current;
    private IReadOnlySet<ToolCapability> _capabilities = Array.Empty<ToolCapability>().ToFrozenSet();
    private int? _context;
    private Task? _close;
    private bool _closing;
    private Exception? _capacityRefusal;

    public LlamaCppModelProvider(LlamaCppLocalEndpointOptions options, IProviderConfigurationStore configurations)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _configurations = configurations ?? throw new ArgumentNullException(nameof(configurations));
        var handler = new SocketsHttpHandler
        {
            UseProxy = false, AllowAutoRedirect = false,
            ConnectCallback = ConnectOriginalSocketAsync,
            PooledConnectionLifetime = TimeSpan.FromMinutes(1)
        };
        _http = new(handler) { BaseAddress = new Uri("http://llamacpp.local/"), Timeout = Timeout.InfiniteTimeSpan };
    }
    public string Id => "llama-cpp";
    public string DisplayName => "Local llama.cpp";
    public ModelProviderKind Kind => ModelProviderKind.OpenAICompatible;
    public bool IsLocal => true; // The only transport is a validated local Unix-domain socket.
    public bool CanManageModels => false;

    /// <summary>Initialized-model observation for the common engine bridge. Actual previous text
    /// and stream probes for this SAME still-current local process are required; this performs no load.</summary>
    public Task<OperationResult<Unit>> ObserveOriginalInitializedModelAsync(ModelIdentity sameModel, CancellationToken token)
        => StartOriginal(async ct =>
        {
            var actual = await ObserveEndpointBodyAsync(ct).ConfigureAwait(false);
            if (sameModel.ProviderId != Id || sameModel.ModelId != actual.ModelId
                || sameModel.ArtifactRevision is not null && !StringComparer.OrdinalIgnoreCase.Equals(sameModel.ArtifactRevision, actual.ConfiguredModelSha256)
                || !actual.ObservedCapabilities.Contains(ToolCapability.Text)
                || !actual.ObservedCapabilities.Contains(ToolCapability.Streaming)
                || !IsOriginalEndpointObservation(actual))
                return OperationResult<Unit>.Failure(new(DulcheErrorCode.ModelLoadFailed,
                    "No SAME live, artifact-bound and text/stream-probed llama.cpp model is initialized.", sameModel.StableKey, false));
            return OperationResult<Unit>.Success(Unit.Value);
        }, token);

    public Task<LocalModelEndpointObservation> ObserveOriginalEndpointAsync(CancellationToken token) =>
        StartOriginal(ct => ObserveEndpointBodyAsync(ct), token);

    public Task<LocalModelEndpointObservation> ProbeOriginalCapabilitiesAsync(bool includeAutomaticToolProbe, CancellationToken token) =>
        StartOriginal(async ct =>
        {
            await ObserveEndpointBodyAsync(ct).ConfigureAwait(false);
            var capabilities = new HashSet<ToolCapability>();
            var text = await CompleteWireAsync(new { model = _options.ModelAlias,
                messages = new[] { new { role = "user", content = "Reply with a short greeting." } },
                stream = false, max_tokens = 32, temperature = 0 }, ct).ConfigureAwait(false);
            if (text.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0
                && choices[0].GetProperty("message").TryGetProperty("content", out var content)
                && content.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(content.GetString()))
                capabilities.Add(ToolCapability.Text);
            var streamed = await ProbeStreamBodyAsync(ct).ConfigureAwait(false);
            if (streamed) capabilities.Add(ToolCapability.Streaming);
            if (includeAutomaticToolProbe)
            {
                var result = await CompleteWireAsync(new { model = _options.ModelAlias,
                    messages = new[] { new { role = "user", content = "Call echo_probe with marker equal to local_probe. Do not invent a result." } },
                    tools = new[] { new { type = "function", function = new { name = "echo_probe", description = "Return a marker; this is a read-only protocol probe.",
                        parameters = new { type = "object", properties = new { marker = new { type = "string" } }, required = new[] { "marker" } } } } },
                    tool_choice = "auto", stream = false, max_tokens = 128, temperature = 0 }, ct).ConfigureAwait(false);
                var calls = ParseTools(result).ToolCalls;
                if (calls.Count == 1 && calls[0].Name == "echo_probe" && calls[0].Arguments.Count == 1
                    && calls[0].Arguments.TryGetValue("marker", out var marker)
                    && marker.ValueKind == JsonValueKind.String && marker.GetString() == "local_probe")
                    capabilities.Add(ToolCapability.Tools);
            }
            lock (_sync) _capabilities = capabilities.ToFrozenSet();
            return await ObserveEndpointBodyAsync(ct).ConfigureAwait(false);
        }, token);

    public bool IsOriginalEndpointObservation(LocalModelEndpointObservation observation)
    {
        if (observation is null) return false;
        lock (_sync) return !_closing && ReferenceEquals(observation, _current)
            && _observations.TryGetValue(observation, out var issued) && ReferenceEquals(issued, _peer);
    }

    public async Task<ProviderHealthStatus> CheckHealthAsync(CancellationToken token)
    {
        var started = System.Diagnostics.Stopwatch.GetTimestamp();
        try
        {
            await ObserveOriginalEndpointAsync(token).ConfigureAwait(false);
            return new(Id, true, "Observed the actual local llama.cpp process and loaded catalogue.",
                System.Diagnostics.Stopwatch.GetElapsedTime(started), DateTimeOffset.UtcNow);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception error) when (error is IOException or HttpRequestException or InvalidOperationException or UnauthorizedAccessException or NotSupportedException)
        {
            return new(Id, false, error.Message, System.Diagnostics.Stopwatch.GetElapsedTime(started), DateTimeOffset.UtcNow);
        }
    }
    public async Task<IReadOnlyList<ProviderModelDescriptor>> GetModelsAsync(CancellationToken token)
    {
        if (!_options.Enabled) return [];
        LocalModelEndpointObservation observation;
        try { observation = await ObserveOriginalEndpointAsync(token).ConfigureAwait(false); }
        catch (Exception cause) when (!token.IsCancellationRequested && cause is IOException or SocketException)
        { throw new HttpRequestException("The actual local model catalogue is unavailable.", cause); }
        if (!observation.ObservedCapabilities.Contains(ToolCapability.Text)) return [];
        int? context; lock (_sync) context = _context;
        return [new(Id, true, new(observation.ModelId, observation.ModelBytes, "llama.cpp", "", "",
            observation.ObservedCapabilities, observation.ObservedAt), context, observation.ModelId)];
    }
    public Task<string> CompleteAsync(OllamaChatRequest request, CancellationToken token) => StartOriginal(async ct =>
    {
        await RequireGenerationAsync(request.Model, ToolCapability.Text, ct).ConfigureAwait(false);
        var result = await CompleteWireAsync(ChatPayload(request, false), ct).ConfigureAwait(false);
        return ReadText(result);
    }, token);
    public Task<OllamaToolResponse> ChatWithToolsAsync(OllamaToolRequest request, CancellationToken token) => StartOriginal(async ct =>
    {
        await RequireGenerationAsync(request.Model, ToolCapability.Tools, ct).ConfigureAwait(false);
        if (request.Messages.Any(message => message.Images is { Count: > 0 })) throw new NotSupportedException("Vision has no original local capability probe.");
        var payload = new { model = request.Model, messages = OpenAiCompatibleModelProviderBase.BuildToolMessages(request.Messages, request.SystemPrompt),
            tools = request.Tools.Select(tool => new { type = "function", function = new { name = tool.Name, description = tool.Description, parameters = ProviderHttp.ConvertToolSchema(tool) } }).ToArray(),
            tool_choice = "auto", stream = false, temperature = Math.Clamp(request.Options?.Temperature ?? 0.7, 0, 2) };
        return ParseTools(await CompleteWireAsync(payload, ct).ConfigureAwait(false));
    }, token);

    public async IAsyncEnumerable<string> StreamChatAsync(OllamaChatRequest request, [EnumeratorCancellation] CancellationToken token)
    {
        var turn = CancellationTokenSource.CreateLinkedTokenSource(token, _stop.Token);
        var channel = Channel.CreateBounded<string>(new BoundedChannelOptions(8)
        { SingleReader = true, SingleWriter = true, FullMode = BoundedChannelFullMode.Wait });
        // This is the real gated producer/whole finally, enrolled before any provider callback.
        // A notification TCS is never substituted for stream execution or cleanup.
        Task<bool> producer; OriginalWork work;
        try { producer = StartOriginalCore(async ct =>
        {
            Exception? failed = null;
            try { await ProduceOriginalStreamAsync(request, channel.Writer, ct).ConfigureAwait(false); return true; }
            catch (Exception cause) { failed = cause; throw; }
            finally { channel.Writer.TryComplete(failed); }
        }, turn.Token, externalLive: true, out work); }
        catch { turn.Dispose(); throw; }
        IAsyncEnumerator<string>? reader = null; Task<bool>? move = null;
        Task? readerClose = null; Task? cancel = null;
        var errors = new List<Exception>(); var cleanup = new List<Exception>();
        Exception? canceledReaderCause = null;
        try
        {
            try { reader = InvokePhysical(() => channel.Reader.ReadAllAsync(turn.Token).GetAsyncEnumerator(turn.Token)); }
            catch (Exception cause) { lock (_sync) Add(work.SynchronousErrors, cause); Add(errors, FaultEnvelope(cause)); }
            if (reader is null) yield break;
            while (true)
            {
                bool available;
                try
                {
                    move = AcquireOriginalTask(() => reader.MoveNextAsync().AsTask(), work);
                    available = await ObserveAcquiredAsync(move, work).ConfigureAwait(false);
                }
                catch (Exception cause)
                {
                    AddTask(errors, move, cause);
                    if (move?.IsCanceled == true) canceledReaderCause = cause;
                    break;
                }
                if (!available) break;
                // Buffered observations are finite; advance this SAME iterator to its actual
                // canceled read/terminal, without publishing post-cancellation text.
                if (turn.IsCancellationRequested) continue;
                yield return reader.Current;
            }
        }
        finally
        {
            // Publish the actual cancellation driver before invoking CTS callbacks. Already
            // owned cleanup is retained even if another cleanup source fails.
            cancel = StartOwnedCancellation(work, turn);
            try { await ObserveAcquiredAsync(cancel, work).ConfigureAwait(false); } catch (Exception cause) { AddTask(cleanup, cancel, cause); }
            if (reader is not null)
            {
                try { readerClose = AcquireOriginalTask(() => reader.DisposeAsync().AsTask(), work, cleanup: true); }
                catch (Exception cause) { Add(cleanup, cause); }
                if (readerClose is not null)
                    try { await ObserveAcquiredAsync(readerClose, work).ConfigureAwait(false); } catch (Exception cause) { AddTask(cleanup, readerClose, cause); }
            }
            try { await producer.ConfigureAwait(false); } catch (Exception cause) { AddTask(errors, producer, cause); }
            try { InvokePhysical(() => { turn.Dispose(); return true; }); } catch (Exception cause) { Add(cleanup, FaultEnvelope(cause)); }
            foreach (var cause in cleanup) Add(errors, cause);
            bool knownCanceled;
            lock (_sync)
            {
                knownCanceled = canceledReaderCause is not null && move?.IsCanceled == true && producer.IsCanceled
                    && cancel.IsCompletedSuccessfully && readerClose?.IsCompletedSuccessfully == true
                    && cleanup.Count == 0 && work.SynchronousErrors.Count == 0
                    && !work.Sources.Any(actual => actual.IsFaulted);
                work.ExternalLive = false;
                foreach (var cause in errors) Add(_errors, cause);
            }
            // Classification is based on actual original statuses and successful cleanup;
            // every independent canceled cause remains in the owner/source ledger.
            if (knownCanceled) ExceptionDispatchInfo.Capture(canceledReaderCause!).Throw();
            ThrowOriginal(errors, knownCanceled: false);
        }
    }

    private async Task ProduceOriginalStreamAsync(OllamaChatRequest request, ChannelWriter<string> writer, CancellationToken token)
    {
        StreamResource? resource = null; var errors = new List<Exception>();
        var cleanup = new List<Exception>(); Task? body = null;
        try
        {
            await RequireGenerationAsync(request.Model, ToolCapability.Streaming, token).ConfigureAwait(false);
            resource = await OpenStreamAsync(InvokePhysical(() => ChatPayload(request, true)), token).ConfigureAwait(false);
            body = AcquireOriginalTask(() => ReadOriginalSseAsync(resource.Reader, writer, token));
            await ObserveAcquiredAsync(body).ConfigureAwait(false);
        }
        catch (Exception cause) { AddTask(errors, body, cause); }
        finally
        {
            if (resource is not null)
                try { await ObserveOriginalAsync(() => resource.CloseAsync(this, cleanup), cleanup: true).ConfigureAwait(false); }
                catch (Exception cause) { Add(cleanup, cause); }
        }
        foreach (var cause in cleanup) Add(errors, cause);
        // The body is a genuine canceled original. Faulted/synchronous OCE was enveloped
        // at its source; cleanup failure never becomes provider cancellation success.
        ThrowOriginal(errors, body?.IsCanceled == true && token.IsCancellationRequested && cleanup.Count == 0);
    }

    private async Task ReadOriginalSseAsync(BoundedLineReader reader, ChannelWriter<string> writer, CancellationToken token)
    {
        while (true)
        {
            var line = await ObserveOriginalAsync(() => reader.ReadLineAsync(token)).ConfigureAwait(false);
            if (line is null) throw new InvalidDataException("The original local stream ended before its required [DONE] terminal; shown output remains accepted.");
            if (!line.StartsWith("data:", StringComparison.Ordinal)) continue;
            var data = line[5..].Trim();
            if (data == "[DONE]") return;
            using var json = JsonDocument.Parse(data); var choices = json.RootElement.GetProperty("choices");
            if (choices.GetArrayLength() > 0 && choices[0].GetProperty("delta").TryGetProperty("content", out var content)
                && content.ValueKind == JsonValueKind.String && content.GetString() is { Length: > 0 } text)
                await ObserveOriginalAsync(() => writer.WriteAsync(text, token).AsTask()).ConfigureAwait(false);
        }
    }

    private async Task<LocalModelEndpointObservation> ObserveEndpointBodyAsync(CancellationToken token)
    {
        ValidateOptions();
        var configuration = await ObserveOriginalAsync(() => _configurations.GetAsync(Id, token)).ConfigureAwait(false);
        if (configuration is not { IsEnabled: true }) throw new InvalidOperationException("The actual local provider is disabled in the configuration store.");
        // IsLocal and Endpoint values in configuration are observations only; neither controls transport.
        await ValidateActualPeerAsync(token).ConfigureAwait(false);
        var model = await HashActualModelAsync(token).ConfigureAwait(false);
        _ = await GetJsonAsync("health", token).ConfigureAwait(false);
        var catalogue = await GetJsonAsync("v1/models", token).ConfigureAwait(false);
        var available = catalogue.GetProperty("data").EnumerateArray().Any(item => item.GetProperty("id").GetString() == _options.ModelAlias
            && item.TryGetProperty("meta", out var meta) && meta.TryGetProperty("n_params", out var count) && count.GetInt64() > 0
            && meta.TryGetProperty("n_vocab", out var vocab) && vocab.GetInt64() > 0);
        if (!available) throw new InvalidOperationException("The actual loaded local model is absent or has no real vocabulary/parameters.");
        var props = await GetJsonAsync("props", token).ConfigureAwait(false);
        if (props.GetProperty("model_path").GetString() != _options.ModelPath || props.GetProperty("model_alias").GetString() != _options.ModelAlias)
            throw new InvalidOperationException("The actual local server has a different observed model path/alias.");
        var context = props.GetProperty("default_generation_settings").GetProperty("n_ctx").GetInt32();
        if (context <= 0) throw new InvalidDataException("The local context observation is invalid.");
        lock (_sync)
        {
            var peer = _peer ?? throw new InvalidOperationException("No original kernel peer observation exists.");
            var observation = new LocalModelEndpointObservation(Id, peer.Pid, peer.Start, peer.BinarySha,
                model.Sha, model.Bytes, model.Tensors, _options.ModelAlias!, _capabilities, DateTimeOffset.UtcNow);
            _observations.Add(observation, peer); _current = observation; _context = context;
            return observation;
        }
    }
    private async Task RequireGenerationAsync(string model, ToolCapability capability, CancellationToken token)
    {
        var observation = await ObserveEndpointBodyAsync(token).ConfigureAwait(false);
        if (model != observation.ModelId || !observation.ObservedCapabilities.Contains(capability))
            throw new NotSupportedException("This actual local model/capability has no original successful probe.");
    }
    private void ValidateOptions()
    {
        if (!_options.Enabled) throw new InvalidOperationException("The explicit local endpoint is disabled.");
        if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException("The verified Unix peer path requires Linux.");
        if (!Path.IsPathFullyQualified(_options.SocketPath ?? "") || Encoding.UTF8.GetByteCount(_options.SocketPath!) > 100
            || !Path.IsPathFullyQualified(_options.ModelPath ?? "") || string.IsNullOrWhiteSpace(_options.ModelAlias))
            throw new InvalidOperationException("Explicit absolute Unix socket/model paths and a model alias are required.");
        if (!IsSha(_options.ExpectedExecutableSha256) || !IsSha(_options.ExpectedModelSha256))
            throw new InvalidOperationException("Exact original executable and GGUF SHA256 pins are required.");
    }
    private static bool IsSha(string? value) => value is { Length: 64 } && value.All(Uri.IsHexDigit);

    private async ValueTask<Stream> ConnectOriginalSocketAsync(SocketsHttpConnectionContext context, CancellationToken token)
    {
        ValidateOptions();
        var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        try
        {
            await socket.ConnectAsync(new UnixDomainSocketEndPoint(_options.SocketPath!), token).ConfigureAwait(false);
            await BindActualPeerAsync(socket, token).ConfigureAwait(false);
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch { socket.Dispose(); throw; }
    }
    private async Task ValidateActualPeerAsync(CancellationToken token)
    {
        using var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        try { await socket.ConnectAsync(new UnixDomainSocketEndPoint(_options.SocketPath!), token).ConfigureAwait(false); }
        catch (SocketException cause) { throw new IOException("The original local Unix endpoint is unavailable.", cause); }
        await BindActualPeerAsync(socket, token).ConfigureAwait(false);
    }
    private async Task BindActualPeerAsync(Socket socket, CancellationToken token)
    {
        var length = (uint)Marshal.SizeOf<PeerCredentials>();
        if (getsockopt(socket.SafeHandle.DangerousGetHandle().ToInt32(), 1, 17, out var credentials, ref length) != 0
            || length != Marshal.SizeOf<PeerCredentials>() || credentials.Pid <= 0 || credentials.Uid != geteuid())
            throw new UnauthorizedAccessException("The actual Unix peer has no matching local kernel identity.");
        var stat = await File.ReadAllTextAsync($"/proc/{credentials.Pid}/stat", token).ConfigureAwait(false);
        var afterName = stat[(stat.LastIndexOf(')') + 2)..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (afterName.Length < 20) throw new InvalidDataException("The actual local process start identity is missing.");
        await using var executable = File.OpenRead($"/proc/{credentials.Pid}/exe");
        var sha = Convert.ToHexString(await SHA256.HashDataAsync(executable, token).ConfigureAwait(false)).ToLowerInvariant();
        if (!sha.Equals(_options.ExpectedExecutableSha256, StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("The actual local peer executable differs from the selected runtime bytes.");
        var peer = new Peer(credentials.Pid, afterName[19], sha);
        lock (_sync)
        {
            if (_peer is not null && _peer != peer) throw new InvalidOperationException("The actual local endpoint process changed; a new provider owner is required.");
            _peer ??= peer;
        }
    }
    private async Task<(string Sha, long Bytes, ulong Tensors)> HashActualModelAsync(CancellationToken token)
    {
        Peer peer; lock (_sync) peer = _peer ?? throw new InvalidOperationException("No original kernel peer owns this model-path observation.");
        // Read the actual peer namespace, including its private SHM path. Ordinary filesystem
        // permissions still apply; denied access remains unavailable, never a DTO/hash approval.
        var physicalPath = $"/proc/{peer.Pid}/root" + _options.ModelPath;
        await using var stream = new FileStream(physicalPath, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.Asynchronous);
        var header = new byte[24]; await stream.ReadExactlyAsync(header, token).ConfigureAwait(false);
        var version = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(4));
        var tensors = BinaryPrimitives.ReadUInt64LittleEndian(header.AsSpan(8));
        if (!header.AsSpan(0, 4).SequenceEqual("GGUF"u8) || version is not (2 or 3) || tensors == 0)
            throw new InvalidDataException("The actual file is not a GGUF with real tensors.");
        stream.Position = 0;
        var sha = Convert.ToHexString(await SHA256.HashDataAsync(stream, token).ConfigureAwait(false)).ToLowerInvariant();
        if (!sha.Equals(_options.ExpectedModelSha256, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("The actual configured GGUF differs from its exact selected SHA256.");
        return (sha, stream.Length, tensors);
    }
    private async Task<JsonElement> GetJsonAsync(string path, CancellationToken token)
    {
        using var response = await ObserveOriginalAsync(() => _http.GetAsync(path, HttpCompletionOption.ResponseHeadersRead, token)).ConfigureAwait(false);
        await ProviderHttp.EnsureSuccessAsync(response, DisplayName, token).ConfigureAwait(false);
        return await ReadBoundedJsonAsync(response, token).ConfigureAwait(false);
    }
    private async Task<JsonElement> CompleteWireAsync(object payload, CancellationToken token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "v1/chat/completions") { Content = JsonContent.Create(payload, options: ProviderHttp.Json) };
        using var response = await ObserveOriginalAsync(() => _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token)).ConfigureAwait(false);
        await ProviderHttp.EnsureSuccessAsync(response, DisplayName, token).ConfigureAwait(false);
        return await ReadBoundedJsonAsync(response, token).ConfigureAwait(false);
    }
    private async Task<JsonElement> ReadBoundedJsonAsync(HttpResponseMessage response, CancellationToken token)
    {
        var stream = await ObserveOriginalAsync(() => response.Content.ReadAsStreamAsync(token)).ConfigureAwait(false);
        var errors = new List<Exception>(); var cleanup = new List<Exception>();
        Task<int>? read = null; JsonElement result = default;
        try
        {
            using var bytes = new MemoryStream(); var buffer = new byte[8192];
            while (true)
            {
                read = AcquireOriginalTask(() => stream.ReadAsync(buffer.AsMemory(), token).AsTask());
                var n = await ObserveAcquiredAsync(read).ConfigureAwait(false); if (n == 0) break;
                if (bytes.Length + n > 262144) throw new InvalidDataException("The local JSON response exceeds its finite limit.");
                bytes.Write(buffer, 0, n);
            }
            using var document = JsonDocument.Parse(bytes.ToArray()); result = document.RootElement.Clone();
        }
        catch (Exception cause) { AddTask(errors, read, cause); }
        finally
        {
            Task? close = null;
            try { close = AcquireOriginalTask(() => stream.DisposeAsync().AsTask(), cleanup: true); }
            catch (Exception cause) { Add(cleanup, cause); }
            if (close is not null)
                try { await ObserveAcquiredAsync(close).ConfigureAwait(false); } catch (Exception cause) { AddTask(cleanup, close, cause); }
        }
        foreach (var cause in cleanup) Add(errors, cause);
        ThrowOriginal(errors, read?.IsCanceled == true && token.IsCancellationRequested && cleanup.Count == 0);
        return result;
    }
    private async Task<StreamResource> OpenStreamAsync(object payload, CancellationToken token)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "v1/chat/completions") { Content = JsonContent.Create(payload, options: ProviderHttp.Json) };
        HttpResponseMessage? response = null; Task<HttpResponseMessage>? responseTask = null;
        try
        {
            responseTask = AcquireOriginalTask(() => _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token));
            response = await ObserveAcquiredAsync(responseTask).ConfigureAwait(false);
            await ProviderHttp.EnsureSuccessAsync(response, DisplayName, token).ConfigureAwait(false);
            var stream = await ObserveOriginalAsync(() => response.Content.ReadAsStreamAsync(token)).ConfigureAwait(false);
            return new(this, request, response, stream);
        }
        catch (Exception original)
        {
            var errors = new List<Exception> { original };
            try { InvokePhysical(() => { response?.Dispose(); return true; }); } catch (Exception cleanup) { Add(errors, cleanup); }
            try { InvokePhysical(() => { request.Dispose(); return true; }); } catch (Exception cleanup) { Add(errors, cleanup); }
            ThrowOriginal(errors, responseTask?.IsCanceled == true && token.IsCancellationRequested && errors.Count == 1); throw;
        }
    }
    private async Task<bool> ProbeStreamBodyAsync(CancellationToken token)
    {
        var resource = await OpenStreamAsync(new { model = _options.ModelAlias,
            messages = new[] { new { role = "user", content = "Reply with a short greeting." } },
            stream = true, max_tokens = 32, temperature = 0 }, token).ConfigureAwait(false);
        var errors = new List<Exception>(); var text = false; var done = false;
        try
        {
            for (var lines = 0; lines < 256; lines++)
            {
                var line = await ObserveOriginalAsync(() => resource.Reader.ReadLineAsync(token)).ConfigureAwait(false);
                if (line is null) break;
                if (line == "data: [DONE]") { done = true; break; }
                if (line.StartsWith("data:", StringComparison.Ordinal))
                {
                    using var document = JsonDocument.Parse(line[5..]); var choices = document.RootElement.GetProperty("choices");
                    if (choices.GetArrayLength() > 0 && choices[0].GetProperty("delta").TryGetProperty("content", out var content)
                        && content.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(content.GetString())) text = true;
                }
            }
        }
        catch (Exception error) { Add(errors, error); }
        finally { await ObserveOriginalAsync(() => resource.CloseAsync(this, errors), cleanup: true).ConfigureAwait(false); }
        ThrowOriginal(errors, false); return text && done;
    }
    private static object ChatPayload(OllamaChatRequest request, bool stream)
    {
        if (request.Messages.Any(message => message.Images is { Count: > 0 })) throw new NotSupportedException("Vision has no original local probe.");
        var messages = new List<object>();
        if (!string.IsNullOrEmpty(request.SystemPrompt)) messages.Add(new { role = "system", content = request.SystemPrompt });
        messages.AddRange(request.Messages.Select(message => (object)new { role = message.Role, content = message.Content }));
        return new { model = request.Model, messages, stream, temperature = Math.Clamp(request.Options?.Temperature ?? 0.7, 0, 2) };
    }
    private static string ReadText(JsonElement root) => root.GetProperty("choices")[0].GetProperty("message").TryGetProperty("content", out var content)
        && content.ValueKind == JsonValueKind.String ? content.GetString() ?? "" : "";
    private static OllamaToolResponse ParseTools(JsonElement root)
    {
        var message = root.GetProperty("choices")[0].GetProperty("message"); var calls = new List<OllamaToolCall>();
        if (message.TryGetProperty("tool_calls", out var all)) foreach (var item in all.EnumerateArray())
        {
            var function = item.GetProperty("function"); using var arguments = JsonDocument.Parse(function.GetProperty("arguments").GetString() ?? "{}");
            if (arguments.RootElement.ValueKind != JsonValueKind.Object) throw new InvalidDataException("The actual local tool arguments are not an object.");
            var values = arguments.RootElement.EnumerateObject().ToDictionary(pair => pair.Name, pair => pair.Value.Clone(), StringComparer.Ordinal);
            calls.Add(new(function.GetProperty("name").GetString() ?? throw new InvalidDataException("Missing tool name"),
                new ReadOnlyDictionary<string, JsonElement>(values), item.TryGetProperty("id", out var id) ? id.GetString() : null));
        }
        return new(message.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.String ? content.GetString() ?? "" : "", calls);
    }

    private Task<T> StartOriginal<T>(Func<CancellationToken, Task<T>> body, CancellationToken token) =>
        StartOriginalCore(body, token, externalLive: false, out _);
    private Task<T> StartOriginalCore<T>(Func<CancellationToken, Task<T>> body, CancellationToken token,
        bool externalLive, out OriginalWork work)
    {
        token.ThrowIfCancellationRequested();
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); Task<T> whole;
        lock (_sync)
        {
            DemandOpen(); PrepareCapacityLocked(); work = new() { ExternalLive = externalLive };
            whole = RunOriginalAsync(gate.Task, work, body, token); work.Whole = whole; _work.Add(work);
        }
        gate.SetResult(); return whole;
    }
    private async Task<T> RunOriginalAsync<T>(Task start, OriginalWork work, Func<CancellationToken, Task<T>> body, CancellationToken token)
    {
        await start.ConfigureAwait(false); var prior = _executing.Value;
        var phase = new Phase(this, prior, work); _executing.Value = phase;
        var errors = new List<Exception>(); var cleanup = new List<Exception>();
        CancellationTokenSource? lifetime = null; Task<T>? actual = null; T? result = default;
        try
        {
            lifetime = CancellationTokenSource.CreateLinkedTokenSource(token, _stop.Token);
            actual = AcquireOriginalTask(() => body(lifetime.Token), work);
            result = await ObserveAcquiredAsync(actual, work).ConfigureAwait(false);
        }
        catch (Exception cause) { AddTask(errors, actual, cause); }
        finally
        {
            try { InvokePhysical(() => { lifetime?.Dispose(); return true; }); } catch (Exception cause) { Add(cleanup, FaultEnvelope(cause)); }
            foreach (var cause in cleanup) Add(errors, cause);
            phase.Live = false; _executing.Value = prior;
            lock (_sync) foreach (var cause in errors) Add(_errors, cause);
        }
        bool knownCanceled;
        lock (_sync) knownCanceled = actual?.IsCanceled == true && lifetime?.IsCancellationRequested == true
            && cleanup.Count == 0 && work.SynchronousErrors.Count == 0
            && work.Sources.Any(raw => !ReferenceEquals(raw, actual) && raw.IsCanceled)
            && !work.Sources.Any(raw => raw.IsFaulted);
        ThrowOriginal(errors, knownCanceled); return result!;
    }
    private OriginalWork CurrentOriginalWork() => _executing.Value is { Live: true } phase && ReferenceEquals(phase.Owner, this)
        ? phase.Work : throw new InvalidOperationException("No original local operation owns this source acquisition.");
    private Task<T> AcquireOriginalTask<T>(Func<Task<T>> factory, OriginalWork? work = null, bool cleanup = false)
    {
        work ??= CurrentOriginalWork(); ReserveSource(work, cleanup); Task<T> actual;
        try { actual = InvokePhysical(factory) ?? throw new InvalidOperationException("The original local source returned no Task."); }
        catch (Exception cause) { ReleaseReservation(work, cause); throw FaultEnvelope(cause); }
        RetainReserved(work, actual); return actual;
    }
    private Task AcquireOriginalTask(Func<Task> factory, OriginalWork? work = null, bool cleanup = false)
    {
        work ??= CurrentOriginalWork(); ReserveSource(work, cleanup); Task actual;
        try { actual = InvokePhysical(factory) ?? throw new InvalidOperationException("The original local source returned no Task."); }
        catch (Exception cause) { ReleaseReservation(work, cause); throw FaultEnvelope(cause); }
        RetainReserved(work, actual); return actual;
    }
    private void ReserveSource(OriginalWork work, bool cleanup)
    {
        lock (_sync)
        {
            work.Sources.RemoveAll(actual => actual.IsCompletedSuccessfully && work.Observed.Remove(actual));
            // Refuse before the next raw callback, never after an original was acquired.
            // Already-owned finite cleanup has reserved capacity and remains independently joinable.
            if (!cleanup && (work.Refusal is not null || work.Sources.Count + work.Reservations >= 4096))
            {
                var cause = work.Refusal ??= new InvalidOperationException("Original local raw-source custody requires external retirement.");
                Add(work.SynchronousErrors, cause); Add(_errors, cause); throw cause;
            }
            work.Reservations++;
        }
    }
    private void ReleaseReservation(OriginalWork work, Exception cause)
    { lock (_sync) { work.Reservations--; Add(work.SynchronousErrors, cause); } }
    private void RetainReserved(OriginalWork work, Task actual)
    { lock (_sync) { work.Reservations--; if (!work.Sources.Any(known => ReferenceEquals(known, actual))) work.Sources.Add(actual); } }
    private async Task<T> ObserveAcquiredAsync<T>(Task<T> actual, OriginalWork? work = null)
    {
        work ??= CurrentOriginalWork();
        try { return await AwaitOriginal(actual).ConfigureAwait(false); }
        finally { lock (_sync) if (actual.IsCompletedSuccessfully) work.Observed.Add(actual); }
    }
    private async Task ObserveAcquiredAsync(Task actual, OriginalWork? work = null)
    {
        work ??= CurrentOriginalWork();
        try { await actual.ConfigureAwait(false); }
        catch (Exception cause) { if (actual.IsFaulted) ThrowOriginal(actual.Exception!.InnerExceptions, false); ExceptionDispatchInfo.Capture(cause).Throw(); }
        finally { lock (_sync) if (actual.IsCompletedSuccessfully) work.Observed.Add(actual); }
    }
    private Task<T> ObserveOriginalAsync<T>(Func<Task<T>> factory, bool cleanup = false) => ObserveAcquiredAsync(AcquireOriginalTask(factory, cleanup: cleanup));
    private Task ObserveOriginalAsync(Func<Task> factory, bool cleanup = false) => ObserveAcquiredAsync(AcquireOriginalTask(factory, cleanup: cleanup));
    private Task StartOwnedCancellation(OriginalWork work, CancellationTokenSource source)
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        ReserveSource(work, cleanup: true);
        var actual = CancelOriginalAsync(gate.Task, source); RetainReserved(work, actual);
        gate.SetResult(); return actual;
    }
    private async Task CancelOriginalAsync(Task start, CancellationTokenSource source)
    {
        await start.ConfigureAwait(false);
        try { InvokePhysical(() => { source.Cancel(); return true; }); }
        catch (Exception cause) { throw FaultEnvelope(cause); }
    }
    private static Exception FaultEnvelope(Exception cause) => cause is OperationCanceledException
        ? new AggregateException("A synchronous local source fault returned no canceled original Task.", cause) : cause;
    private void PrepareCapacityLocked()
    {
        _work.RemoveAll(work => !work.ExternalLive && work.Whole.IsCompletedSuccessfully && work.SynchronousErrors.Count == 0
            && work.Reservations == 0 && work.Sources.All(actual => actual.IsCompletedSuccessfully));
        if (_capacityRefusal is not null || _work.Count >= Capacity)
        { var cause = _capacityRefusal ??= new InvalidOperationException("Original local provider custody is full."); Add(_errors, cause); throw cause; }
    }
    private void DemandOpen() { if (_closing) throw new ObjectDisposedException(nameof(LlamaCppModelProvider)); }
    private T InvokePhysical<T>(Func<T> callback)
    {
        (_physical ??= []).Add(this); try { return callback(); } finally { _physical.RemoveAt(_physical.Count - 1); }
    }
    public Task CloseAndDrainAsync()
    {
        TaskCompletionSource? gate = null; Task close;
        lock (_sync)
        {
            if (_physical?.Any(owner => ReferenceEquals(owner, this)) == true || PhaseContains(_executing.Value))
                throw new InvalidOperationException("An active local original cannot join its own provider.");
            if (_close is null) { gate = new(TaskCreationOptions.RunContinuationsAsynchronously); _closing = true; _close = CloseOriginalAsync(gate.Task); }
            close = _close;
        }
        gate?.SetResult(); return close;
    }
    private bool PhaseContains(Phase? phase)
    { for (; phase is not null; phase = phase.Parent) if (phase.Live && ReferenceEquals(phase.Owner, this)) return true; return false; }
    private async Task CloseOriginalAsync(Task start)
    {
        await start.ConfigureAwait(false); var errors = new List<Exception>();
        try { InvokePhysical(() => { _stop.Cancel(); return true; }); } catch (Exception error) { Add(errors, error); }
        OriginalWork[] tasks; lock (_sync) tasks = _work.ToArray();
        foreach (var work in tasks)
        {
            try { await work.Whole.ConfigureAwait(false); } catch (Exception cause) { AddTask(errors, work.Whole, cause); }
            Task[] raw; lock (_sync) { raw = work.Sources.ToArray(); foreach (var cause in work.SynchronousErrors) Add(errors, cause); }
            foreach (var actual in raw) try { await actual.ConfigureAwait(false); } catch (Exception cause) { AddTask(errors, actual, cause); }
        }
        try { InvokePhysical(() => { _http.Dispose(); return true; }); } catch (Exception error) { Add(errors, error); }
        try { InvokePhysical(() => { _stop.Dispose(); return true; }); } catch (Exception error) { Add(errors, error); }
        lock (_sync) foreach (var error in _errors) Add(errors, error);
        ThrowOriginal(errors, false);
    }
    public ValueTask DisposeAsync() => new(CloseAndDrainAsync());
    private static async Task<T> AwaitOriginal<T>(Task<T> actual)
    {
        try { return await actual.ConfigureAwait(false); }
        catch (Exception error)
        {
            if (actual.IsFaulted) ThrowOriginal(actual.Exception!.InnerExceptions, false);
            ExceptionDispatchInfo.Capture(error).Throw(); throw;
        }
    }
    private static void AddTask(List<Exception> errors, Task? actual, Exception error)
    { if (actual?.Exception is { } fault) foreach (var cause in fault.InnerExceptions) Add(errors, cause); else Add(errors, error); }
    private static void Add(List<Exception> errors, Exception error)
    { if (!errors.Any(known => ReferenceEquals(known, error))) errors.Add(error); }
    private static void ThrowOriginal(IReadOnlyCollection<Exception> errors, bool knownCanceled)
    {
        if (errors.Count == 0) return;
        if (errors.Count == 1 && (knownCanceled || errors.First() is not OperationCanceledException)) ExceptionDispatchInfo.Capture(errors.First()).Throw();
        throw new AggregateException("The original local provider work or cleanup failed.", errors);
    }
    private sealed record Peer(int Pid, string Start, string BinarySha);
    private sealed class OriginalWork
    { public Task Whole = null!; public bool ExternalLive; public int Reservations; public Exception? Refusal; public readonly List<Task> Sources = []; public readonly HashSet<Task> Observed = []; public readonly List<Exception> SynchronousErrors = []; }
    private sealed class Phase(LlamaCppModelProvider owner, Phase? parent, OriginalWork work)
    { public LlamaCppModelProvider Owner { get; } = owner; public Phase? Parent { get; } = parent; public OriginalWork Work { get; } = work; public volatile bool Live = true; }
    private sealed class StreamResource(LlamaCppModelProvider owner, HttpRequestMessage request, HttpResponseMessage response, Stream stream)
    {
        public BoundedLineReader Reader { get; } = new(owner, new StreamReader(stream, Encoding.UTF8, true, 1024, leaveOpen: true));
        public async Task CloseAsync(LlamaCppModelProvider source, List<Exception> errors)
        {
            if (!ReferenceEquals(owner, source)) throw new InvalidOperationException("The original stream resource has a different local owner.");
            try { owner.InvokePhysical(() => { Reader.Dispose(); return true; }); } catch (Exception error) { Add(errors, error); }
            Task? close = null;
            try { close = owner.AcquireOriginalTask(() => stream.DisposeAsync().AsTask(), cleanup: true); } catch (Exception error) { Add(errors, error); }
            if (close is not null) try { await owner.ObserveAcquiredAsync(close).ConfigureAwait(false); } catch (Exception error) { AddTask(errors, close, error); }
            try { owner.InvokePhysical(() => { response.Dispose(); return true; }); } catch (Exception error) { Add(errors, error); }
            try { owner.InvokePhysical(() => { request.Dispose(); return true; }); } catch (Exception error) { Add(errors, error); }
        }
    }
    private sealed class BoundedLineReader(LlamaCppModelProvider owner, StreamReader reader) : IDisposable
    {
        private readonly byte[] _bytes = new byte[4096];
        private readonly char[] _buffer = new char[4096];
        private readonly Decoder _decoder = Encoding.UTF8.GetDecoder();
        private int _count;
        private int _position;
        private bool _eof;
        public async Task<string?> ReadLineAsync(CancellationToken token)
        {
            var line = new StringBuilder();
            while (true)
            {
                if (_position == _count)
                {
                    if (_eof) return line.Length == 0 ? null : line.ToString().TrimEnd('\r');
                    // Capture the actual raw response-stream Task before any async text-reader
                    // wrapper could normalize a faulted OCE into a canceled proxy.
                    var bytes = await owner.ObserveOriginalAsync(() => reader.BaseStream.ReadAsync(_bytes.AsMemory(), token).AsTask()).ConfigureAwait(false);
                    _eof = bytes == 0;
                    _count = _decoder.GetChars(_bytes.AsSpan(0, bytes), _buffer.AsSpan(), flush: _eof);
                    _position = 0;
                    if (_count == 0)
                    {
                        if (_eof) return line.Length == 0 ? null : line.ToString().TrimEnd('\r');
                        continue;
                    }
                }
                var newline = Array.IndexOf(_buffer, '\n', _position, _count - _position);
                var end = newline < 0 ? _count : newline;
                if (line.Length + end - _position > 262144) throw new InvalidDataException("The actual local stream exceeds its finite line limit.");
                line.Append(_buffer, _position, end - _position);
                _position = newline < 0 ? _count : newline + 1;
                if (newline >= 0) return line.ToString().TrimEnd('\r');
            }
        }
        public void Dispose() => reader.Dispose();
    }
    [StructLayout(LayoutKind.Sequential)] private struct PeerCredentials
    {
        public int Pid; public uint Uid; public uint Gid;
        public PeerCredentials(int pid, uint uid, uint gid) { Pid = pid; Uid = uid; Gid = gid; }
    }
    [DllImport("libc", SetLastError = true)] private static extern int getsockopt(int socket, int level, int option, out PeerCredentials credentials, ref uint length);
    [DllImport("libc")] private static extern uint geteuid();
}
