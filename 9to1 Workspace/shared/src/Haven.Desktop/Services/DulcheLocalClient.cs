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

    public IAsyncEnumerable<string> StreamAsync(string model, IReadOnlyList<Dictionary<string, string>> messages,
        Action<string> runStarted, CancellationToken token) =>
        StreamChatAsync(new { model, messages, stream = true }, runStarted, ownsIncompleteRun: false, token);

    internal IAsyncEnumerable<string> StreamLocalModelAsync(object payload, CancellationToken token) =>
        StreamChatAsync(payload, null, ownsIncompleteRun: true, token);

    private async IAsyncEnumerable<string> StreamChatAsync(object payload, Action<string>? runStarted,
        bool ownsIncompleteRun, [EnumeratorCancellation] CancellationToken token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "v1/chat/completions")
        {
            Content = JsonContent.Create(payload),
        };
        // A cancellation already present cannot have dispatched this request. After the
        // attempt begins, transport failure alone cannot disprove daemon job admission.
        token.ThrowIfCancellationRequested();
        HttpResponseMessage response;
        try { response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false); }
        catch (Exception failure) when (ownsIncompleteRun && (failure is HttpRequestException or IOException or OperationCanceledException))
        {
            throw new DulcheRunStopUnconfirmedException(null,
                "Dulche did not return a run identity after the completion request was attempted; its remote stop is unconfirmed.", failure);
        }
        StreamReader? reader = null;
        string? runId = null;
        var accepted = false;
        var completed = false;
        string? finishReason = null;
        Exception? streamFailure = null;
        try
        {
            try
            {
                await DemandSuccess(response, token).ConfigureAwait(false);
                accepted = true;
                runId = ReadIssuedRunId(response);
                if (runId is not null) runStarted?.Invoke(runId);
                reader = new StreamReader(await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false),
                    System.Text.Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 1024, leaveOpen: true);
            }
            catch (Exception failure) { streamFailure = failure; }
            while (streamFailure is null && !completed)
            {
                string? content = null;
                try
                {
                    var line = await reader!.ReadLineAsync(token).ConfigureAwait(false);
                    if (line is null)
                        throw new EndOfStreamException("Dulche disconnected before the response completed. Open the saved run for its outcome.");
                    if (!line.StartsWith("data:", StringComparison.Ordinal)) continue;
                    var data = line[5..].Trim();
                    if (data == "[DONE]")
                    {
                        if (ownsIncompleteRun && finishReason is not ("stop" or "length"))
                            throw new InvalidDataException("Dulche finished without confirming a text response.");
                        // daemon.py emits [DONE] after the job reaches a terminal state.
                        // A finish_reason chunk alone can precede that state transition.
                        completed = true;
                        break;
                    }
                    if (data.Length == 0) continue;
                    var chunk = JsonNode.Parse(data);
                    if (chunk?["error"] is { } error) throw new IOException(Explain(error));
                    var choice = chunk?["choices"]?.AsArray().FirstOrDefault();
                    finishReason = choice?["finish_reason"]?.GetValue<string>() ?? finishReason;
                    content = choice?["delta"]?["content"]?.GetValue<string>();
                }
                catch (Exception failure) { streamFailure = failure; break; }
                if (!string.IsNullOrEmpty(content)) yield return content;
            }
        }
        finally
        {
            var failures = new List<Exception>();
            if (streamFailure is not null) failures.Add(streamFailure);
            try { reader?.Dispose(); } catch (Exception failure) { failures.Add(failure); }
            // The response owns its stream. Close it exactly once before any stop
            // request; its failure never prevents the independent stop attempt.
            try { response.Dispose(); } catch (Exception failure) { failures.Add(failure); }
            if (ownsIncompleteRun && accepted && !completed)
            {
                try
                {
                    if (runId is null)
                        throw new DulcheRunStopUnconfirmedException(null,
                            "Dulche did not issue one unambiguous run identity; the incomplete response's remote stop is unconfirmed.");
                    await StopIncompleteRunAsync(runId).ConfigureAwait(false);
                }
                catch (Exception failure) { failures.Add(failure); }
            }
            if (failures.Count == 1)
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failures[0]).Throw();
            if (failures.Count > 1)
                throw new AggregateException("The Dulche response and its owned cleanup did not settle cleanly.", failures);
        }
    }

    private static string? ReadIssuedRunId(HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues("X-Dulche-Run-Id", out var values)) return null;
        var ids = values.ToArray();
        return ids.Length == 1 && !string.IsNullOrWhiteSpace(ids[0]) ? ids[0] : null;
    }

    private async Task StopIncompleteRunAsync(string runId)
    {
        // The caller's canceled generation token must not cancel this distinct,
        // bounded stop request. Failure remains observable after the stream closes.
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        try { await StopRunAsync(runId, timeout.Token).ConfigureAwait(false); }
        catch (DulcheRunStopUnconfirmedException) { throw; }
        catch (Exception error) when (error is HttpRequestException or IOException or OperationCanceledException or JsonException)
        {
            throw new DulcheRunStopUnconfirmedException(runId,
                "Dulche could not confirm the incomplete response's remote stop. Inspect its saved run before continuing.", error);
        }
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

/// <summary>An incomplete local response whose exact daemon run has no acknowledged terminal stop.</summary>
internal sealed class DulcheRunStopUnconfirmedException(string? runId, string message, Exception? inner = null)
    : IOException(message, inner)
{
    internal string? RunId { get; } = runId;
}
