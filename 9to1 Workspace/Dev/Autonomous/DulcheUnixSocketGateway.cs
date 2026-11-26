using System.Diagnostics;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace HavenOS.Apps.Dev.Autonomous;

/// <summary>
/// Direct Unix-socket client for the independently supervised local Dulche daemon.
/// Reuses Dulche's canonical /v1/status, /v1/models/{id}/count-tokens, /v1/chat/completions
/// (SSE streaming with content + reasoning_content + timings) and /v1/agents/{id}/stop endpoints.
/// Never loads, unloads, suspends, or modifies model or hardware settings.
/// </summary>
public sealed class DulcheUnixSocketGateway : IDeveloperModelGateway, IDisposable
{
    private readonly HttpClient _http;
    private readonly bool _ownsHttp;

    public DulcheUnixSocketGateway(string? socketPath = null)
    {
        var handler = new SocketsHttpHandler
        {
            UseProxy = false,
            AllowAutoRedirect = false,
            ConnectTimeout = TimeSpan.FromSeconds(3),
            ConnectCallback = async (_, token) =>
            {
                if (!OperatingSystem.IsLinux())
                    throw new PlatformNotSupportedException("Dulche Unix socket IPC requires Linux.");
                var resolved = ResolveSocketPath(socketPath);
                var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
                try
                {
                    await socket.ConnectAsync(new UnixDomainSocketEndPoint(resolved), token).ConfigureAwait(false);
                    return new NetworkStream(socket, ownsSocket: true);
                }
                catch
                {
                    socket.Dispose();
                    throw;
                }
            }
        };
        _http = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://localhost/"),
            Timeout = Timeout.InfiniteTimeSpan
        };
        _ownsHttp = true;
    }

    public DulcheUnixSocketGateway(HttpMessageHandler handler, bool disposeHandler = false)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _http = new HttpClient(handler, disposeHandler)
        {
            BaseAddress = new Uri("http://localhost/"),
            Timeout = Timeout.InfiniteTimeSpan
        };
        _ownsHttp = true;
    }

    public static string ResolveSocketPath(string? explicitSocketPath = null)
    {
        var path = explicitSocketPath ?? Environment.GetEnvironmentVariable("DULCHE_SOCKET");
        if (string.IsNullOrWhiteSpace(path))
        {
            var runtime = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR");
            if (string.IsNullOrWhiteSpace(runtime))
                throw new IOException("XDG_RUNTIME_DIR is unavailable; cannot locate Dulche socket.");
            path = Path.Combine(runtime, "9to1-dulche", "api.sock");
        }
        if (!Path.IsPathFullyQualified(path))
            throw new IOException("Dulche requires an absolute Unix socket path.");
        return path;
    }

    public async Task<DeveloperDulcheStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        using var bounded = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        bounded.CancelAfter(TimeSpan.FromSeconds(15));
        using var request = new HttpRequestMessage(HttpMethod.Get, "v1/status");
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseContentRead, bounded.Token).ConfigureAwait(false);
        await DemandSuccessAsync(response, bounded.Token).ConfigureAwait(false);
        var root = JsonNode.Parse(await response.Content.ReadAsStringAsync(bounded.Token).ConfigureAwait(false)) as JsonObject ?? new JsonObject();
        return ParseStatus(root);
    }

    public async Task<int> CountTokensAsync(
        string modelId,
        IReadOnlyList<DeveloperChatMessage> messages,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelId);
        ArgumentNullException.ThrowIfNull(messages);
        using var bounded = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        // Cold-start admission includes verified source hashes and model loading.
        bounded.CancelAfter(TimeSpan.FromMinutes(8));
        var payload = new
        {
            messages = messages.Select(m => new { role = m.Role, content = m.Content }).ToArray()
        };
        using var request = new HttpRequestMessage(HttpMethod.Post, $"v1/models/{Uri.EscapeDataString(modelId)}/count-tokens")
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
        };
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseContentRead, bounded.Token).ConfigureAwait(false);
        await DemandSuccessAsync(response, bounded.Token).ConfigureAwait(false);
        var root = JsonNode.Parse(await response.Content.ReadAsStringAsync(bounded.Token).ConfigureAwait(false)) as JsonObject;
        var count = root?["input_tokens"]?.GetValue<int>() ?? -1;
        if (count < 0)
            throw new InvalidDataException("Dulche count-tokens did not return a non-negative input_tokens value.");
        return count;
    }

    public async Task<DeveloperCompletionResult> StreamCompletionAsync(
        DeveloperCompletionRequest request,
        Action<DeveloperStreamChunk>? onChunk = null,
        Action<string>? onRunStarted = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ModelId);
        if (request.Messages.Count == 0)
            throw new ArgumentException("At least one message is required.", nameof(request));

        var started = Stopwatch.StartNew();
        var body = BuildCompletionPayload(request);
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "v1/chat/completions")
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json")
        };
        if (!string.IsNullOrWhiteSpace(request.IdempotencyKey))
            httpRequest.Headers.Add("Idempotency-Key", request.IdempotencyKey);

        using var response = await _http.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        await DemandSuccessAsync(response, cancellationToken).ConfigureAwait(false);

        var runId = response.Headers.TryGetValues("X-Dulche-Run-Id", out var ids)
            ? ids.FirstOrDefault() ?? ""
            : "";
        // Only server-issued identities confer cancellation custody.
        if (!string.IsNullOrWhiteSpace(runId)) onRunStarted?.Invoke(runId);

        var content = new StringBuilder();
        var reasoning = new StringBuilder();
        string? finishReason = null;
        int promptTokens = 0;
        int completionTokens = 0;
        double? tokensPerSecond = null;

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var reader = new StreamReader(stream, Encoding.UTF8);

        var completed = false;
        while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
        {
            if (!line.StartsWith("data:", StringComparison.Ordinal))
                continue;
            var raw = line[5..].Trim();
            if (raw.Length == 0)
                continue;
            if (raw == "[DONE]")
            {
                completed = true;
                break;
            }

            var parsed = ParseSseChunk(raw, runId);
            if (parsed.ErrorMessage is not null)
                throw new IOException(parsed.ErrorMessage);

            if (!string.IsNullOrEmpty(parsed.Chunk.ContentDelta))
                content.Append(parsed.Chunk.ContentDelta);
            if (!string.IsNullOrEmpty(parsed.Chunk.ReasoningDelta))
                reasoning.Append(parsed.Chunk.ReasoningDelta);
            if (!string.IsNullOrEmpty(parsed.Chunk.FinishReason))
                finishReason = parsed.Chunk.FinishReason;
            if (parsed.Chunk.PromptTokens is > 0)
                promptTokens = parsed.Chunk.PromptTokens.Value;
            if (parsed.Chunk.CompletionTokens is > 0)
                completionTokens = parsed.Chunk.CompletionTokens.Value;
            if (parsed.Chunk.TokensPerSecond is > 0)
                tokensPerSecond = parsed.Chunk.TokensPerSecond;

            if (!string.IsNullOrEmpty(parsed.Chunk.ContentDelta)
                || !string.IsNullOrEmpty(parsed.Chunk.ReasoningDelta)
                || parsed.Chunk.FinishReason is not null
                || parsed.Chunk.TokensPerSecond is not null)
            {
                onChunk?.Invoke(parsed.Chunk);
            }
        }

        if (!completed || string.IsNullOrWhiteSpace(finishReason))
            throw new EndOfStreamException("Dulche stream ended without a terminal choice and [DONE]. Inspect the saved run for its outcome.");

        started.Stop();
        return new DeveloperCompletionResult(
            RunId: runId,
            ModelId: request.ModelId,
            Content: content.ToString(),
            ReasoningContent: reasoning.ToString(),
            FinishReason: finishReason ?? "stop",
            PromptTokens: promptTokens,
            CompletionTokens: completionTokens,
            TokensPerSecond: tokensPerSecond,
            Elapsed: started.Elapsed);
    }

    public async Task StopRunAsync(string dulcheRunId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dulcheRunId);
        using var bounded = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        bounded.CancelAfter(TimeSpan.FromSeconds(15));
        using var request = new HttpRequestMessage(HttpMethod.Post, $"v1/agents/{Uri.EscapeDataString(dulcheRunId)}/stop")
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json")
        };
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseContentRead, bounded.Token).ConfigureAwait(false);
        await DemandSuccessAsync(response, bounded.Token).ConfigureAwait(false);
        var state = JsonNode.Parse(await response.Content.ReadAsStringAsync(bounded.Token).ConfigureAwait(false)) as JsonObject;
        var returnedId = state?["id"]?.ToString();
        var terminal = state?["state"]?.ToString();
        if (!StringComparer.Ordinal.Equals(returnedId, dulcheRunId)
            || terminal is not ("stopped" or "completed" or "failed" or "cancelled" or "review"))
            throw new IOException("Dulche did not acknowledge a terminal state for the owned run; cancellation remains unconfirmed.");
    }

    public static JsonObject BuildCompletionPayload(DeveloperCompletionRequest request)
    {
        var messagesArray = new JsonArray();
        foreach (var msg in request.Messages)
        {
            messagesArray.Add(new JsonObject
            {
                ["role"] = msg.Role,
                ["content"] = msg.Content
            });
        }

        var payload = new JsonObject
        {
            ["model"] = request.ModelId,
            ["messages"] = messagesArray,
            ["max_tokens"] = Math.Clamp(request.MaxTokens, 64, 32768),
            ["temperature"] = request.Temperature,
            ["stream"] = true,
            ["stream_options"] = new JsonObject { ["include_usage"] = true }
        };

        if (!request.EnableReasoning)
        {
            payload["reasoning_effort"] = "none";
            payload["chat_template_kwargs"] = new JsonObject { ["enable_thinking"] = false };
        }
        else
        {
            payload["chat_template_kwargs"] = new JsonObject { ["enable_thinking"] = true };
        }

        if (request.RequireJsonObject && !request.EnableReasoning)
        {
            payload["response_format"] = new JsonObject { ["type"] = "json_object" };
        }

        return payload;
    }

    public static (DeveloperStreamChunk Chunk, string? ErrorMessage) ParseSseChunk(string rawJson, string runId)
    {
        var node = JsonNode.Parse(rawJson) as JsonObject;
        if (node is null)
            return (new DeveloperStreamChunk(null, null, null, null, null, null, runId), null);

        if (node["error"] is { } error)
            return (new DeveloperStreamChunk(null, null, null, null, null, null, runId), ExplainError(error));

        string? contentDelta = null;
        string? reasoningDelta = null;
        string? finishReason = null;

        if (node["choices"] is JsonArray choices && choices.Count > 0 && choices[0] is JsonObject choice)
        {
            var delta = choice["delta"] as JsonObject ?? choice["message"] as JsonObject;
            if (delta is not null)
            {
                contentDelta = delta["content"]?.GetValue<string>();
                reasoningDelta = delta["reasoning_content"]?.GetValue<string>()
                    ?? delta["reasoning"]?.GetValue<string>();
            }
            finishReason = choice["finish_reason"]?.GetValue<string>();
        }

        int? promptTokens = node["usage"]?["prompt_tokens"]?.GetValue<int>();
        int? completionTokens = node["usage"]?["completion_tokens"]?.GetValue<int>();
        double? tps = TryReadDouble(node["timings"]?["predicted_per_second"])
            ?? TryReadDouble(node["timings"]?["tokens_per_second"]);

        return (new DeveloperStreamChunk(
            contentDelta,
            reasoningDelta,
            finishReason,
            promptTokens,
            completionTokens,
            tps,
            runId), null);
    }

    public static DeveloperDulcheStatus ParseStatus(JsonObject root)
    {
        var models = new List<DeveloperModelInfo>();
        if (root["models"] is JsonArray modelRows)
        {
            foreach (var row in modelRows.OfType<JsonObject>())
            {
                var id = row["id"]?.ToString() ?? "";
                if (string.IsNullOrWhiteSpace(id)) continue;
                var name = row["name"]?.ToString();
                var state = row["state"]?.ToString() ?? "unknown";
                var gpu = row["gpu"]?.ToString();
                var context = TryReadInt(row["context"]) ?? TryReadInt(row["settings"]?["context"]) ?? 0;
                var slots = TryReadInt(row["slots"]) ?? 1;
                var engine = row["engine"]?.ToString();
                var ownership = row["ownership"]?.ToString();
                bool? canStart = row.ContainsKey("can_start")
                    ? row["can_start"] is JsonValue canStartValue && canStartValue.TryGetValue<bool>(out var allowed) && allowed
                    : null;
                models.Add(new DeveloperModelInfo(
                    id,
                    string.IsNullOrWhiteSpace(name) ? id : name,
                    state,
                    gpu,
                    context,
                    slots,
                    engine,
                    ownership,
                    canStart));
            }
        }

        var gpus = new List<DeveloperGpuTelemetry>();
        var res = root["resources"] as JsonObject;
        if (res?["gpus"] is JsonArray gpuRows)
        {
            foreach (var gpu in gpuRows.OfType<JsonObject>())
            {
                gpus.Add(new DeveloperGpuTelemetry(
                    gpu["name"]?.ToString() ?? "GPU",
                    TryReadInt(gpu["used_mib"]) ?? 0,
                    TryReadInt(gpu["total_mib"]) ?? 0,
                    TryReadInt(gpu["free_mib"]) ?? 0,
                    TryReadDouble(gpu["temperature_c"]),
                    TryReadDouble(gpu["utilization"])));
            }
        }

        var resources = new DeveloperResourceSnapshot(
            HostAvailableMiB: TryReadLong(res?["host_available_mib"]) ?? TryReadLong(res?["available_ram_mib"]) ?? 0,
            HostTotalMiB: TryReadLong(res?["host_total_mib"]) ?? TryReadLong(res?["total_ram_mib"]) ?? 0,
            DiskFreeBytes: TryReadLong(res?["disk_free_bytes"]) ?? 0,
            CpuLoad1m: TryReadDouble(res?["cpu_load_1m"]),
            Gpus: gpus);

        var activeAgents = 0;
        if (root["agents"] is JsonArray agents)
        {
            activeAgents = agents.OfType<JsonObject>()
                .Count(a => a["state"]?.ToString() is "running" or "queued");
        }

        return new DeveloperDulcheStatus(
            Phase: root["phase"]?.ToString() ?? "unknown",
            StopAll: root["stop_all"]?.GetValue<bool>() == true,
            Models: models,
            Resources: resources,
            ActiveAgentsCount: activeAgents);
    }

    public static void DemandHardwareSafeguards(
        DeveloperDulcheStatus status,
        int memoryFloorMiB,
        long diskFloorGiB)
    {
        ArgumentNullException.ThrowIfNull(status);
        if (!status.Phase.Equals("ready", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Dulche service phase is '{status.Phase}', not ready.");
        if (status.StopAll)
            throw new InvalidOperationException("Dulche stop-all is active; autonomous admission is paused.");
        if (status.Resources.HostAvailableMiB < memoryFloorMiB)
            throw new InvalidOperationException(
                $"Host available memory ({status.Resources.HostAvailableMiB} MiB) is below the safety floor ({memoryFloorMiB} MiB).");
        var diskFloorBytes = diskFloorGiB * 1024L * 1024L * 1024L;
        if (status.Resources.DiskFreeBytes < diskFloorBytes)
            throw new InvalidOperationException(
                $"Free disk space ({status.Resources.DiskFreeBytes / (1024L * 1024L * 1024L)} GiB) is below the safety floor ({diskFloorGiB} GiB).");
    }

    private static int? TryReadInt(JsonNode? node)
    {
        if (node is null) return null;
        return int.TryParse(node.ToString(), out var v) ? v : null;
    }

    private static long? TryReadLong(JsonNode? node)
    {
        if (node is null) return null;
        return long.TryParse(node.ToString(), out var v) ? v : null;
    }

    private static double? TryReadDouble(JsonNode? node)
    {
        if (node is null) return null;
        return double.TryParse(node.ToString(), System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : null;
    }

    private static async Task DemandSuccessAsync(HttpResponseMessage response, CancellationToken token)
    {
        if (response.IsSuccessStatusCode)
            return;
        var text = await response.Content.ReadAsStringAsync(token).ConfigureAwait(false);
        try
        {
            if (JsonNode.Parse(text) is { } doc)
                text = ExplainError(doc["error"] ?? doc);
        }
        catch (JsonException) { }
        throw new HttpRequestException(
            $"Dulche returned {(int)response.StatusCode}: {text[..Math.Min(text.Length, 400)]}",
            null,
            response.StatusCode);
    }

    private static string ExplainError(JsonNode error) =>
        error is JsonObject obj
            ? (obj["message"] ?? obj["detail"] ?? obj["code"])?.ToString() ?? error.ToString()
            : error.ToString();

    public void Dispose()
    {
        if (_ownsHttp)
            _http.Dispose();
    }
}
