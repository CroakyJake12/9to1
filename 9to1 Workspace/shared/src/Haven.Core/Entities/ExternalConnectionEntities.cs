using System.Text.Json;

namespace Haven.Core;

public enum ExternalConnectionKind { Calendar = 0, Mcp = 1 }
public enum McpTransportKind { StreamableHttp = 0, Stdio = 1 }
public enum ExternalConnectionState
{
    Disconnected = 0,
    Connecting = 1,
    Ready = 2,
    Offline = 3,
    NeedsAttention = 4,
    Disabled = 5,
    Degraded = 6,
    Reconnecting = 7,
    Failed = 8,
    Stopped = 9
}

/// <summary>Non-secret persisted metadata for one external service connection.</summary>
public sealed record ExternalConnection(
    Guid Id,
    string Name,
    string ProviderKey,
    ExternalConnectionKind Kind,
    string PresetKey,
    bool IsEnabled,
    ExternalConnectionState State,
    string Status,
    string ConfigurationJson,
    string? ServerName,
    string? ServerVersion,
    string? ProtocolVersion,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    /// <summary>Stable MCP server identity for this saved connection; it is independent of the mutable name and transport endpoint.</summary>
    public Guid ServerId => Id;
}

/// <summary>MCP transport configuration. Secrets are referenced separately and never stored here.</summary>
public sealed record McpConnectionConfiguration(
    McpTransportKind Transport,
    string? Endpoint = null,
    string? Command = null,
    IReadOnlyList<string>? Arguments = null,
    string? WorkingDirectory = null,
    bool LocalOnly = false,
    bool SerializeInvocations = false,
    int TimeoutSeconds = 30,
    bool UseOAuth = false,
    string? OAuthClientId = null,
    string? OAuthClientMetadataDocumentUri = null,
    IReadOnlyList<string>? OAuthScopes = null,
    string OAuthRedirectUri = "http://127.0.0.1:52117/mcp-oauth/")
{
    public static McpConnectionConfiguration UefnDefault { get; } = new(
        McpTransportKind.StreamableHttp,
        "http://127.0.0.1:8000/mcp",
        LocalOnly: true,
        SerializeInvocations: true,
        TimeoutSeconds: 30);
}

public sealed record McpServerIdentity(string? Name, string? Version, string? ProtocolVersion, string CapabilitiesJson);

/// <summary>One advertised MCP resource. Server annotations remain untrusted data.</summary>
public sealed record McpExternalResource(string Uri, string Name, string? Description, string? MimeType, string MetadataJson = "{}");

/// <summary>One advertised MCP prompt. Server annotations remain untrusted data.</summary>
public sealed record McpExternalPrompt(string Name, string? Description, IReadOnlyList<McpExternalPromptArgument> Arguments, string MetadataJson = "{}");

public sealed record McpExternalPromptArgument(string Name, string? Description, bool Required);

/// <summary>Null discovery collections mean the negotiated server does not expose that MCP capability.</summary>
public sealed record McpServerDiscovery(
    McpServerIdentity Identity,
    IReadOnlyList<McpExternalTool> Tools,
    IReadOnlyList<McpExternalResource>? Resources,
    IReadOnlyList<McpExternalPrompt>? Prompts,
    string SnapshotVersion);

public sealed record McpResourceReadResult(string Uri, IReadOnlyList<McpResourceContent> Contents);
public sealed record McpResourceContent(string? MimeType, string? Text, string? Base64Data);
public sealed record McpPromptGetResult(string Name, IReadOnlyList<McpPromptMessage> Messages);
public sealed record McpPromptMessage(string Role, IReadOnlyList<McpPromptContent> Contents);
public sealed record McpPromptContent(string Kind, string? Text, string? MimeType, string? Uri, string? Base64Data);

public sealed record McpExternalTool(
    string Name,
    string Description,
    JsonElement InputSchema,
    JsonElement? OutputSchema = null,
    string MetadataJson = "{}");

public sealed record McpToolInvocationResult(
    bool Succeeded,
    string Text,
    JsonElement? StructuredContent,
    string ContentJson,
    string? Error = null);

public static class ExternalConnectionNaming
{
    public static string PluginName(string name)
    {
        var value = string.IsNullOrWhiteSpace(name) ? "External" : name.Trim();
        return value.EndsWith("Connection", StringComparison.OrdinalIgnoreCase) ? value : value + " Connection";
    }

    public static string CapabilityKey(Guid id) => "connection:" + id.ToString("N");
    public static string SecretProviderId(Guid id) => "mcp." + id.ToString("N");
    public const string OAuthTokenSecretName = "oauth.tokens";
    public static bool IsConnectionCapability(string key) => key.StartsWith("connection:", StringComparison.OrdinalIgnoreCase);
}
