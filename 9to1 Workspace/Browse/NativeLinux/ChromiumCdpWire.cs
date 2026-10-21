using System.Text.Json;

namespace HavenOS.Apps.Browse;

/// <summary>A decoded Chrome DevTools Protocol event. <see cref="SessionId"/>
/// is present for events delivered inside an attached target session.</summary>
public sealed record CdpEvent(string Method, JsonElement Params, string? SessionId);

/// <summary>A CDP endpoint rejected a correlated request.</summary>
public sealed class ChromiumCdpException : Exception
{
    public ChromiumCdpException(string message) : base(message) { }
}

/// <summary>
/// Pure CDP wire framing: builds request JSON and classifies incoming frames.
/// It contains no sockets and no async state, so transport contracts are testable
/// with synthetic fragmented/out-of-order frames without a Chromium process.
/// </summary>
public static class ChromiumCdpWire
{
    /// <summary>Serializes one JSON-RPC request for the given id/method.</summary>
    public static string CreateRequest(int id, string method, object? parameters, string? sessionId)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(id, 1);
        if (string.IsNullOrWhiteSpace(method)) throw new ArgumentException("A CDP method is required.", nameof(method));

        if (parameters is null && sessionId is null)
            return "{\"id\":" + id + ",\"method\":" + JsonSerializer.Serialize(method) + "}";

        var payload = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["id"] = id,
            ["method"] = method
        };
        if (parameters is not null) payload["params"] = parameters;
        if (sessionId is not null) payload["sessionId"] = sessionId;
        return JsonSerializer.Serialize(payload);
    }

    /// <summary>Reads the correlated request id from a response frame, or null
    /// when the frame is an event.</summary>
    public static int? TryGetResponseId(JsonDocument frame)
    {
        if (frame.RootElement.ValueKind == JsonValueKind.Object &&
            frame.RootElement.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.Number)
            return id.GetInt32();
        return null;
    }

    /// <summary>Reads the event method from a frame, or null when the frame is a
    /// response (or malformed).</summary>
    public static string? TryGetMethod(JsonDocument frame)
    {
        if (frame.RootElement.ValueKind == JsonValueKind.Object &&
            frame.RootElement.TryGetProperty("method", out var method) && method.ValueKind == JsonValueKind.String)
            return method.GetString();
        return null;
    }

    /// <summary>Reads the session id from a frame, or null.</summary>
    public static string? TryGetSessionId(JsonDocument frame)
    {
        if (frame.RootElement.ValueKind == JsonValueKind.Object &&
            frame.RootElement.TryGetProperty("sessionId", out var session) && session.ValueKind == JsonValueKind.String)
            return session.GetString();
        return null;
    }

    /// <summary>Reads the result object from a response frame, or null.</summary>
    public static JsonElement? TryGetResult(JsonDocument frame)
    {
        if (frame.RootElement.ValueKind == JsonValueKind.Object &&
            frame.RootElement.TryGetProperty("result", out var result))
            return result.Clone();
        return null;
    }

    /// <summary>Reads the error object from a response frame, or null.</summary>
    public static JsonElement? TryGetError(JsonDocument frame)
    {
        if (frame.RootElement.ValueKind == JsonValueKind.Object &&
            frame.RootElement.TryGetProperty("error", out var error))
            return error.Clone();
        return null;
    }

    /// <summary>Reads the event params object from an event frame, or empty.</summary>
    public static JsonElement TryGetParams(JsonDocument frame)
    {
        if (frame.RootElement.ValueKind == JsonValueKind.Object &&
            frame.RootElement.TryGetProperty("params", out var parameters))
            return parameters.Clone();
        return default;
    }
}
