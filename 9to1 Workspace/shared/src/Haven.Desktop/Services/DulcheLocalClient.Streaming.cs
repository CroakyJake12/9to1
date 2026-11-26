using System.Diagnostics;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Nodes;
using HavenOS.Apps.Dev.Autonomous;

namespace Haven.Desktop.Services;

public sealed partial class DulcheLocalClient
{
    public async Task<DeveloperDulcheStatus> GetStructuredStatusAsync(CancellationToken token)
    {
        var raw = await GetAsync("status", token).ConfigureAwait(false) as JsonObject ?? new JsonObject();
        return DulcheUnixSocketGateway.ParseStatus(raw);
    }

    public async Task<int> CountTokensAsync(
        string modelId,
        IReadOnlyList<DeveloperChatMessage> messages,
        CancellationToken token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelId);
        ArgumentNullException.ThrowIfNull(messages);
        var payload = new
        {
            messages = messages.Select(m => new { role = m.Role, content = m.Content }).ToArray()
        };
        var result = await PostAsync($"models/{Uri.EscapeDataString(modelId)}/count-tokens", payload, token).ConfigureAwait(false);
        var count = result["input_tokens"]?.GetValue<int>() ?? -1;
        if (count < 0)
            throw new InvalidDataException("Dulche count-tokens did not return input_tokens.");
        return count;
    }

    public async Task<DeveloperCompletionResult> StreamDetailedAsync(
        DeveloperCompletionRequest request,
        Action<DeveloperStreamChunk>? onChunk,
        Action<string>? onRunStarted,
        CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(request);
        var sw = Stopwatch.StartNew();
        var body = DulcheUnixSocketGateway.BuildCompletionPayload(request);
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "v1/chat/completions")
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json")
        };
        if (!string.IsNullOrWhiteSpace(request.IdempotencyKey))
            httpRequest.Headers.Add("Idempotency-Key", request.IdempotencyKey);

        using var response = await _http.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
        await DemandSuccess(response, token).ConfigureAwait(false);

        var runId = ReadIssuedRunId(response) ?? string.Empty;
        if (runId.Length != 0) onRunStarted?.Invoke(runId);

        var content = new StringBuilder();
        var reasoning = new StringBuilder();
        string? finishReason = null;
        int promptTokens = 0;
        int completionTokens = 0;
        double? tps = null;

        await using var stream = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var done = false;
        while (await reader.ReadLineAsync(token).ConfigureAwait(false) is { } line)
        {
            if (!line.StartsWith("data:", StringComparison.Ordinal))
                continue;
            var raw = line[5..].Trim();
            if (raw.Length == 0) continue;
            if (raw == "[DONE]")
            {
                done = true;
                break;
            }
            var (chunk, error) = DulcheUnixSocketGateway.ParseSseChunk(raw, runId);
            if (error is not null)
                throw new IOException(error);
            if (!string.IsNullOrEmpty(chunk.ContentDelta)) content.Append(chunk.ContentDelta);
            if (!string.IsNullOrEmpty(chunk.ReasoningDelta)) reasoning.Append(chunk.ReasoningDelta);
            if (!string.IsNullOrEmpty(chunk.FinishReason)) finishReason = chunk.FinishReason;
            if (chunk.PromptTokens is > 0) promptTokens = chunk.PromptTokens.Value;
            if (chunk.CompletionTokens is > 0) completionTokens = chunk.CompletionTokens.Value;
            if (chunk.TokensPerSecond is > 0) tps = chunk.TokensPerSecond;

            if (!string.IsNullOrEmpty(chunk.ContentDelta)
                || !string.IsNullOrEmpty(chunk.ReasoningDelta)
                || chunk.FinishReason is not null
                || chunk.TokensPerSecond is not null)
            {
                onChunk?.Invoke(chunk);
            }
        }
        if (!done || string.IsNullOrWhiteSpace(finishReason))
            throw new EndOfStreamException("Dulche disconnected before completing generation. Inspect the saved run.");
        sw.Stop();
        return new DeveloperCompletionResult(
            runId,
            request.ModelId,
            content.ToString(),
            reasoning.ToString(),
            finishReason ?? "stop",
            promptTokens,
            completionTokens,
            tps,
            sw.Elapsed);
    }

    public async Task StopRunAsync(string runId, CancellationToken token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        var response = await PostAsync($"agents/{Uri.EscapeDataString(runId)}/stop", new { }, token).ConfigureAwait(false);
        // A terminal state can be persisted before a stopped worker exits. The daemon
        // must also acknowledge that this exact run's admitted execution has settled.
        var state = response as JsonObject;
        var returnedId = state?["id"] is JsonValue id && id.TryGetValue<string>(out var identity) ? identity : null;
        var terminal = state?["state"] is JsonValue value && value.TryGetValue<string>(out var status) ? status : null;
        var settled = state?["settled"] is JsonValue proof && proof.TryGetValue<bool>(out var acknowledged) && acknowledged;
        if (!StringComparer.Ordinal.Equals(returnedId, runId) || !settled ||
            terminal is not ("stopped" or "completed" or "failed" or "cancelled" or "review"))
            throw new DulcheRunStopUnconfirmedException(runId,
                "Dulche did not acknowledge a settled terminal state for the exact requested run; its remote stop is unconfirmed.");
    }

    /// <summary>Exposes this SAME singleton DulcheLocalClient as an IDeveloperModelGateway for 9to1 Build.</summary>
    public IDeveloperModelGateway AsAutonomousGateway() => new DulcheLocalClientModelGateway(this);

    private sealed class DulcheLocalClientModelGateway(DulcheLocalClient client) : IDeveloperModelGateway
    {
        public Task<DeveloperDulcheStatus> GetStatusAsync(CancellationToken cancellationToken = default) =>
            client.GetStructuredStatusAsync(cancellationToken);

        public Task<int> CountTokensAsync(
            string modelId,
            IReadOnlyList<DeveloperChatMessage> messages,
            CancellationToken cancellationToken = default) =>
            client.CountTokensAsync(modelId, messages, cancellationToken);

        public Task<DeveloperCompletionResult> StreamCompletionAsync(
            DeveloperCompletionRequest request,
            Action<DeveloperStreamChunk>? onChunk = null,
            Action<string>? onRunStarted = null,
            CancellationToken cancellationToken = default) =>
            client.StreamDetailedAsync(request, onChunk, onRunStarted, cancellationToken);

        public Task StopRunAsync(string dulcheRunId, CancellationToken cancellationToken = default) =>
            client.StopRunAsync(dulcheRunId, cancellationToken);
    }
}
