using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using Haven.Application;
using Haven.Core;

namespace Haven.Infrastructure;

/// <summary>Explicit connection to an existing Strata server on this device. Owns no
/// server, weights or private-context authority. Passive discovery never loads a model.</summary>
public sealed class StrataLoopbackModelProvider(
    IHttpClientFactory clients, IProviderConfigurationStore configurations,
    IProviderSecretStore secrets, ProviderUsageCaptureBuffer usageCapture)
    : OpenAiCompatibleModelProviderBase(clients, configurations, secrets, usageCapture)
{
    public const string ProviderId = "strata-local";
    public const string DefaultUrl = "http://127.0.0.1:8081/v1/";
    private string? _toolsFingerprint;
    public override string Id => ProviderId;
    public override string DisplayName => "Strata on this device";
    public override ModelProviderKind Kind => ModelProviderKind.OpenAICompatible;
    public override bool IsLocal => true;
    protected override string DefaultEndpoint => DefaultUrl;
    protected override bool RequiresApiKey => false;

    public static bool IsAllowedEndpoint(string? endpoint) =>
        Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) &&
        uri.Scheme is "http" or "https" && string.IsNullOrEmpty(uri.UserInfo) &&
        string.IsNullOrEmpty(uri.Query) && string.IsNullOrEmpty(uri.Fragment) &&
        IPAddress.TryParse(uri.DnsSafeHost, out var ip) && (ip.Equals(IPAddress.Loopback) || ip.Equals(IPAddress.IPv6Loopback)) &&
        uri.AbsolutePath.TrimEnd('/') == "/v1";

    protected override void ValidateConfiguration(ProviderConfiguration configuration)
    {
        if (configuration.Id != ProviderId || !configuration.IsLocal || configuration.AllowCloudFallback ||
            !IsAllowedEndpoint(configuration.Endpoint))
            throw new InvalidOperationException("Strata requires an explicitly local literal-loopback /v1 endpoint with cloud fallback disabled.");
    }

    public static SocketsHttpHandler CreateTransportHandler() => new()
    {
        AllowAutoRedirect = false, UseProxy = false, UseCookies = false,
        ConnectCallback = async (context, token) =>
        {
            if (!IPAddress.TryParse(context.DnsEndPoint.Host, out var address) || !IPAddress.IsLoopback(address))
                throw new HttpRequestException("Strata transport refused a non-loopback destination.");
            var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
            try
            {
                await socket.ConnectAsync(new IPEndPoint(address, context.DnsEndPoint.Port), token).ConfigureAwait(false);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch { socket.Dispose(); throw; }
        }
    };

    public override async Task<ProviderHealthStatus> CheckHealthAsync(CancellationToken cancellationToken)
    {
        var started = Stopwatch.GetTimestamp();
        try
        {
            using var client = await CreateClientAsync(cancellationToken).ConfigureAwait(false);
            var status = await ReadStatusAsync(client, cancellationToken).ConfigureAwait(false);
            if (!status.Loaded) Volatile.Write(ref _toolsFingerprint, null);
            return new(Id, true, status.Loaded
                ? $"Strata is connected; configured model {status.Model} is loaded. Generation and tools require their own checks."
                : $"Strata is connected; configured model {status.Model} is unloaded. No ready model is offered; passive discovery does not load weights.",
                Stopwatch.GetElapsedTime(started), DateTimeOffset.UtcNow);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception error) when (error is HttpRequestException or InvalidOperationException or JsonException or TaskCanceledException)
        {
            Volatile.Write(ref _toolsFingerprint, null);
            return new(Id, false, error.Message, Stopwatch.GetElapsedTime(started), DateTimeOffset.UtcNow);
        }
    }

    public override async Task<IReadOnlyList<ProviderModelDescriptor>> GetModelsAsync(CancellationToken cancellationToken)
    {
        var configuration = await ReadConfigurationAsync(cancellationToken).ConfigureAwait(false);
        if (configuration is null || !configuration.IsEnabled) return [];
        using var client = await CreateClientAsync(cancellationToken).ConfigureAwait(false);
        var status = await ReadStatusAsync(client, cancellationToken).ConfigureAwait(false);
        if (!status.Loaded) { Volatile.Write(ref _toolsFingerprint, null); return []; }
        using var response = await client.GetAsync("models", cancellationToken).ConfigureAwait(false);
        await ProviderHttp.EnsureSuccessAsync(response, DisplayName, cancellationToken).ConfigureAwait(false);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
        if (!document.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array) return [];
        var models = new List<ProviderModelDescriptor>();
        foreach (var item in data.EnumerateArray())
        {
            var id = Text(item, "id");
            if (string.IsNullOrWhiteSpace(id) || id.Length > 1024 ||
                id != status.Model && Text(item, "alias_of") != status.Model) continue;
            if (item.TryGetProperty("status", out var state) && Text(state, "value") is { } value && value != "loaded") continue;
            int? context = null;
            if (item.TryGetProperty("meta", out var meta) && meta.TryGetProperty("n_ctx", out var window) && window.ValueKind == JsonValueKind.Number && window.TryGetInt32(out var actual) && actual > 0) context = actual;
            context ??= status.Context;
            var capabilities = new HashSet<ToolCapability> { ToolCapability.Text, ToolCapability.Streaming };
            if (status.Fingerprint is { } fingerprint && Volatile.Read(ref _toolsFingerprint) == fingerprint) capabilities.Add(ToolCapability.Tools);
            // ModelDescriptor's legacy zero-size sentinel means not reported by this HTTP catalogue.
            models.Add(new(Id, true, new(id, 0, "Strata", "", "", capabilities, DateTimeOffset.UtcNow), context, id));
        }
        return models;
    }

    /// <summary>Explicit, bounded read-only generation probe. It executes no returned tool.
    /// Never called from passive connection/catalogue refresh. Busy/unloaded models are preserved.</summary>
    public async Task<ProviderHealthStatus> ProbeCapabilitiesAsync(CancellationToken cancellationToken)
    {
        var started = Stopwatch.GetTimestamp();
        using var client = await CreateClientAsync(cancellationToken).ConfigureAwait(false);
        var before = await ReadStatusAsync(client, cancellationToken).ConfigureAwait(false);
        Volatile.Write(ref _toolsFingerprint, null);
        if (!before.Loaded || before.Fingerprint is null || before.InFlight != 0)
            return new(Id, false, "Strata must be loaded, idle and report a stable service identity before an explicit capability test.", Stopwatch.GetElapsedTime(started), DateTimeOffset.UtcNow);
        var nonce = Guid.NewGuid().ToString("N");
        var request = new
        {
            model = before.Model,
            messages = new[] { new { role = "user", content = "Call echo_probe exactly once with marker equal to " + nonce + ". Do not invent a result." } },
            tools = new[] { new { type = "function", function = new { name = "echo_probe", description = "Read-only protocol probe; no effects are executed.",
                parameters = new { type = "object", properties = new { marker = new { type = "string" } }, required = new[] { "marker" } } } } },
            tool_choice = "auto", max_tokens = 128, temperature = 0, reasoning_effort = "none", reasoning_budget_tokens = 0, stream = false
        };
        using var response = await client.PostAsJsonAsync("chat/completions", request, ProviderHttp.Json, cancellationToken).ConfigureAwait(false);
        await ProviderHttp.EnsureSuccessAsync(response, DisplayName, cancellationToken).ConfigureAwait(false);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
        var valid = MatchesProbe(document.RootElement, nonce);
        var after = await ReadStatusAsync(client, cancellationToken).ConfigureAwait(false);
        valid = valid && after.Loaded && after.Fingerprint == before.Fingerprint;
        if (valid) Volatile.Write(ref _toolsFingerprint, before.Fingerprint);
        return new(Id, valid, valid ? "Strata returned the exact correlated tool-call probe. No tool effect was executed; project permissions and development checks remain separate."
            : "Strata did not return the exact tool-call probe, or its service/model changed. Tools remain unavailable.", Stopwatch.GetElapsedTime(started), DateTimeOffset.UtcNow);
    }

    protected override object BuildChatPayload(OllamaChatRequest request, bool stream) => Bounded(base.BuildChatPayload(request, stream));
    protected override object BuildToolPayload(OllamaToolRequest request) => Bounded(base.BuildToolPayload(request));
    private static object Bounded(object payload)
    {
        var value = JsonSerializer.SerializeToElement(payload, ProviderHttp.Json).EnumerateObject().ToDictionary(x => x.Name, x => (object?)x.Value.Clone());
        value["max_tokens"] = 2048; value["reasoning_effort"] = "none"; value["reasoning_budget_tokens"] = 0;
        return value;
    }

    private static bool MatchesProbe(JsonElement root, string nonce)
    {
        if (!root.TryGetProperty("choices", out var choices) || choices.ValueKind != JsonValueKind.Array || choices.GetArrayLength() != 1) return false;
        var choice = choices[0];
        if (Text(choice, "finish_reason") != "tool_calls" || !choice.TryGetProperty("message", out var message) ||
            !message.TryGetProperty("tool_calls", out var calls) || calls.ValueKind != JsonValueKind.Array || calls.GetArrayLength() != 1) return false;
        var call = calls[0];
        if (!call.TryGetProperty("function", out var function) || Text(function, "name") != "echo_probe" || Text(function, "arguments") is not { } arguments) return false;
        try
        {
            using var parsed = JsonDocument.Parse(arguments); var args = parsed.RootElement;
            return args.ValueKind == JsonValueKind.Object && args.EnumerateObject().Count() == 1 && Text(args, "marker") == nonce;
        }
        catch (JsonException) { return false; }
    }

    private sealed record Status(string Model, bool Loaded, long Started, int? Context, int? InFlight)
    { public string? Fingerprint => Started > 0 ? Started + ":" + Model : null; }
    private static async Task<Status> ReadStatusAsync(HttpClient client, CancellationToken token)
    {
        using var response = await client.GetAsync("status", token).ConfigureAwait(false);
        await ProviderHttp.EnsureSuccessAsync(response, "Strata", token).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token).ConfigureAwait(false)); var root = doc.RootElement;
        if (Text(root, "service") != "strata" || Text(root, "model") is not { Length: > 0 and <= 1024 } model ||
            !root.TryGetProperty("loaded", out var loaded) || loaded.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            throw new InvalidOperationException("The endpoint did not return a genuine Strata status shape.");
        long started = root.TryGetProperty("started", out var start) && start.ValueKind == JsonValueKind.Number && start.TryGetInt64(out var time) ? time : 0;
        int? context = root.TryGetProperty("context", out var ctx) && ctx.TryGetProperty("native", out var native) && native.ValueKind == JsonValueKind.Number && native.TryGetInt32(out var count) && count > 0 ? count : null;
        int? inFlight = root.TryGetProperty("activity", out var activity) && activity.TryGetProperty("in_flight", out var flight) && flight.ValueKind == JsonValueKind.Number && flight.TryGetInt32(out var requests) && requests >= 0 ? requests : null;
        return new(model, loaded.GetBoolean(), started, context, inFlight);
    }
    private static string? Text(JsonElement element, string property) => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
