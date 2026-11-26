using System.Net.Http.Json;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Haven.Desktop.Services;

/// <summary>Device-local IPC to the independently supervised Dulche process.</summary>
public sealed partial class DulcheLocalClient
{
    private readonly HttpClient _http;
    private readonly Guid _session = Guid.NewGuid();
    private readonly SemaphoreSlim _connection = new(1);
    private readonly SemaphoreSlim _policyWrites = new(1);
    private readonly UserPreferencesService _preferences;
    private volatile bool _registered;

    public DulcheLocalClient(UserPreferencesService preferences) : this(preferences, (string?)null) { }

    internal DulcheLocalClient(UserPreferencesService preferences, string? socketPath)
    {
        _preferences = preferences;
        var handler = new SocketsHttpHandler
        {
            UseProxy = false,
            AllowAutoRedirect = false,
            ConnectTimeout = TimeSpan.FromSeconds(2),
            ConnectCallback = async (_, token) =>
            {
                if (!OperatingSystem.IsLinux())
                    throw new PlatformNotSupportedException("Dulche requires its local Linux service.");
                var path = socketPath ?? Environment.GetEnvironmentVariable("DULCHE_SOCKET");
                if (string.IsNullOrWhiteSpace(path))
                {
                    var runtime = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR");
                    if (string.IsNullOrWhiteSpace(runtime))
                        throw new IOException("The user runtime directory is unavailable.");
                    path = Path.Combine(runtime, "9to1-dulche", "api.sock");
                }
                if (!Path.IsPathFullyQualified(path))
                    throw new IOException("Dulche requires an absolute Unix socket path.");
                var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
                try
                {
                    await socket.ConnectAsync(new UnixDomainSocketEndPoint(path), token);
                    return new NetworkStream(socket, ownsSocket: true);
                }
                catch { socket.Dispose(); throw; }
            }
        };
        _http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost/"), Timeout = Timeout.InfiniteTimeSpan };
    }

    internal DulcheLocalClient(UserPreferencesService preferences, HttpMessageHandler handler)
    {
        _preferences = preferences;
        _http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost/"), Timeout = Timeout.InfiniteTimeSpan };
    }

    public static readonly string[] Policies = ["always", "resources", "active", "never"];
    public static readonly string[] PolicyNames = ["Always", "While Resources are Free", "While Active", "Never"];

    public async Task ObserveInitialConnectionAsync()
    {
        try
        {
            await ConnectAsync(CancellationToken.None);
        }
        catch (Exception e) when (e is IOException or HttpRequestException or OperationCanceledException or PlatformNotSupportedException)
        {
            System.Diagnostics.Trace.TraceInformation("Dulche connection unavailable: {0}", e.Message);
        }
    }

    public string SavedPolicy => _preferences.DulchePolicy;

    public async Task ConnectAsync(CancellationToken token)
    {
        await _connection.WaitAsync(token);
        try
        {
            await PostAsync("frontend/connect", new
            {
                session_id = _session,
                pid = Environment.ProcessId
            }, token);
            _registered = true;
        }
        finally { _connection.Release(); }
    }

    public async Task RecordAcceptedUserExitAsync()
    {
        if (!_registered)
            return;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        try
        {
            await ConnectAsync(timeout.Token);
            await PostAsync("frontend/close", new
            {
                session_id = _session,
                reason = "user"
            }, timeout.Token);
        }
        catch (Exception e) when (e is IOException or HttpRequestException or OperationCanceledException or PlatformNotSupportedException)
        {
            System.Diagnostics.Trace.TraceWarning("Dulche close receipt unavailable: {0}", e.Message);
        }
    }

    public async Task SelectPolicyAsync(string policy, CancellationToken token)
    {
        if (!Policies.Contains(policy))
            throw new ArgumentOutOfRangeException(nameof(policy));
        await _policyWrites.WaitAsync(token);
        try
        {
            if (!_registered) await ConnectAsync(token);
            await PostAsync("settings", new { policy }, token);
            _preferences.SetDulchePolicy(policy);
        }
        finally { _policyWrites.Release(); }
    }

    public async Task<JsonNode> GetAsync(string route, CancellationToken token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "v1/" + route);
        return await ReadAsync(request, token);
    }

    public Task<JsonNode> PostAsync(string route, object body, CancellationToken token) =>
        PostAsync(route, body, token, null);

    public async Task<JsonNode> PostAsync(string route, object body, CancellationToken token, string? idempotencyKey)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "v1/" + route)
        {
            Content = new StringContent(JsonSerializer.Serialize(body), System.Text.Encoding.UTF8, "application/json")
        };
        if (idempotencyKey is not null) request.Headers.Add("Idempotency-Key", idempotencyKey);
        return await ReadAsync(request, token);
    }

    private async Task<JsonNode> ReadAsync(HttpRequestMessage request, CancellationToken token)
    {
        using var bounded = CancellationTokenSource.CreateLinkedTokenSource(token);
        var modelTransition = request.Method == HttpMethod.Post &&
            request.RequestUri?.OriginalString.StartsWith("v1/models/", StringComparison.Ordinal) == true;
        bounded.CancelAfter(modelTransition ? TimeSpan.FromMinutes(6) : TimeSpan.FromSeconds(15));
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseContentRead, bounded.Token);
        await DemandSuccess(response, bounded.Token);
        return JsonNode.Parse(await response.Content.ReadAsStringAsync(bounded.Token)) ?? new JsonObject();
    }

    public async IAsyncEnumerable<string> StreamAsync(string model, IReadOnlyList<Dictionary<string, string>> messages,
        Action<string> runStarted, [EnumeratorCancellation] CancellationToken token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "v1/chat/completions")
        {
            Content = JsonContent.Create(new { model, messages, stream = true })
        };
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
        await DemandSuccess(response, token);
        if (response.Headers.TryGetValues("X-Dulche-Run-Id", out var ids) && ids.FirstOrDefault() is { } id)
            runStarted(id);
        using var reader = new StreamReader(await response.Content.ReadAsStreamAsync(token));
        while (await reader.ReadLineAsync(token) is { } line)
        {
            if (!line.StartsWith("data:", StringComparison.Ordinal))
                continue;
            var data = line[5..].Trim();
            if (data == "[DONE]")
                yield break;
            if (data.Length == 0)
                continue;
            var chunk = JsonNode.Parse(data);
            if (chunk?["error"] is { } error)
                throw new IOException(Explain(error));
            var content = chunk?["choices"]?.AsArray().FirstOrDefault()?["delta"]?["content"]?.GetValue<string>();
            if (!string.IsNullOrEmpty(content))
                yield return content;
        }
        throw new EndOfStreamException("Dulche disconnected before the response completed. Open the saved run for its outcome.");
    }

    private static async Task DemandSuccess(HttpResponseMessage response, CancellationToken token)
    {
        if (response.IsSuccessStatusCode)
            return;
        var text = await response.Content.ReadAsStringAsync(token);
        try
        {
            if (JsonNode.Parse(text) is { } document)
                text = Explain(document["error"] ?? document);
        }
        catch (JsonException) { }
        throw new HttpRequestException($"{(int)response.StatusCode} {text[..Math.Min(text.Length, 350)]}", null, response.StatusCode);
    }
    private static string Explain(JsonNode error) => error is JsonObject obj ? (obj["message"] ?? obj["detail"] ?? obj["code"])?.ToString() ?? error.ToString() : error.ToString();
}
