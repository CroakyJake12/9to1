using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using Haven.Application;
using Haven.Core;
using ModelContextProtocol.Authentication;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Haven.Infrastructure;

/// <summary>Adapter over the maintained MCP C# SDK; SDK types do not escape Infrastructure.</summary>
public sealed class McpConnectionClient(IProviderSecretStore secrets) : IMcpConnectionClient
{
    private const int MaxSchemaCharacters = 256_000;
    private const int MaxResultCharacters = 2_000_000;
    private readonly Dictionary<Guid, SemaphoreSlim> _serializedGates = [];

    public async Task<(McpServerIdentity Identity, IReadOnlyList<McpExternalTool> Tools)> DiscoverAsync(ExternalConnection connection, CancellationToken cancellationToken)
    {
        var discovery = await DiscoverCapabilitiesAsync(connection, cancellationToken).ConfigureAwait(false);
        return (discovery.Identity, discovery.Tools);
    }

    public async Task<McpServerDiscovery> DiscoverCapabilitiesAsync(ExternalConnection connection, CancellationToken cancellationToken)
    {
        var configuration = ReadConfiguration(connection);
        await using var client = await CreateClientAsync(connection, configuration, cancellationToken).ConfigureAwait(false);
        var tools = await client.ListToolsAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        var mapped = tools.Select(tool => MapTool(tool.Name, tool.Description, tool.JsonSchema)).ToArray();
        IReadOnlyList<McpExternalResource>? resources = null;
        IReadOnlyList<McpExternalPrompt>? prompts = null;
        if (client.ServerCapabilities.Resources is not null)
        {
            var discovered = await client.ListResourcesAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
            resources = discovered.Select(item => new McpExternalResource(
                Bound(item.Uri, 2_000), Bound(item.Name, 200), Bound(item.Description ?? string.Empty, 2_000),
                Bound(item.MimeType ?? string.Empty, 200), BoundedJson(item.ProtocolResource, 64_000))).ToArray();
        }
        if (client.ServerCapabilities.Prompts is not null)
        {
            var discovered = await client.ListPromptsAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
            prompts = discovered.Select(item => new McpExternalPrompt(
                Bound(item.Name, 200), Bound(item.Description ?? string.Empty, 2_000),
                (item.ProtocolPrompt.Arguments ?? []).Select(argument => new McpExternalPromptArgument(
                    Bound(argument.Name, 200), Bound(argument.Description ?? string.Empty, 2_000), argument.Required ?? false)).ToArray(),
                BoundedJson(item.ProtocolPrompt, 64_000))).ToArray();
        }
        var identity = new McpServerIdentity(client.ServerInfo?.Name, client.ServerInfo?.Version, client.NegotiatedProtocolVersion,
            BoundedJson(client.ServerCapabilities, MaxSchemaCharacters));
        var snapshot = JsonSerializer.Serialize(new { identity, tools = mapped, resources, prompts });
        if (snapshot.Length > MaxSchemaCharacters) throw new InvalidOperationException("MCP capability snapshot exceeded Haven's safety size limit.");
        var snapshotVersion = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(snapshot))).ToLowerInvariant();
        return new McpServerDiscovery(identity, mapped, resources, prompts, snapshotVersion);
    }

    public async Task<McpToolInvocationResult> InvokeAsync(ExternalConnection connection, string toolName, IReadOnlyDictionary<string, JsonElement> arguments, CancellationToken cancellationToken)
    {
        var configuration = ReadConfiguration(connection);
        var gate = configuration.SerializeInvocations ? GetGate(connection.Id) : null;
        if (gate is not null) await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var client = await CreateClientAsync(connection, configuration, cancellationToken).ConfigureAwait(false);
            var tools = await client.ListToolsAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
            var tool = tools.FirstOrDefault(item => item.Name.Equals(toolName, StringComparison.Ordinal))
                ?? throw new InvalidOperationException($"MCP tool '{toolName}' is no longer advertised by this server. Refresh the connection.");
            var values = arguments.ToDictionary(pair => pair.Key, pair => (object?)JsonSerializer.Deserialize<object>(pair.Value.GetRawText()), StringComparer.Ordinal);
            var result = await tool.CallAsync(values, cancellationToken: cancellationToken).ConfigureAwait(false);
            var text = string.Join("\n", result.Content.OfType<TextContentBlock>().Select(block => block.Text));
            var contentJson = BoundedJson(result.Content, MaxResultCharacters);
            JsonElement? structured = result.StructuredContent is { } value ? BoundElement(value, MaxResultCharacters) : null;
            if (string.IsNullOrWhiteSpace(text) && structured is { } structuredValue) text = structuredValue.GetRawText();
            if (text.Length > MaxResultCharacters) text = text[..MaxResultCharacters] + "... [truncated by Haven]";
            return new McpToolInvocationResult(result.IsError is not true, text, structured, contentJson, result.IsError is true ? text : null);
        }
        finally { gate?.Release(); }
    }

    public async Task<McpResourceReadResult> ReadResourceAsync(ExternalConnection connection, string resourceUri, CancellationToken cancellationToken)
    {
        var configuration = ReadConfiguration(connection);
        await using var client = await CreateClientAsync(connection, configuration, cancellationToken).ConfigureAwait(false);
        if (client.ServerCapabilities.Resources is null) throw new NotSupportedException("The negotiated MCP server does not expose resources.");
        var resources = await client.ListResourcesAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!resources.Any(item => string.Equals(item.Uri, resourceUri, StringComparison.Ordinal)))
            throw new KeyNotFoundException("The requested MCP resource is no longer advertised by this server.");
        var result = await client.ReadResourceAsync(resourceUri, cancellationToken: cancellationToken).ConfigureAwait(false);
        var contents = result.Contents.Select(content => content switch
        {
            TextResourceContents text => new McpResourceContent(Bound(text.MimeType ?? string.Empty, 200), Bound(text.Text, MaxResultCharacters), null),
            BlobResourceContents blob => new McpResourceContent(Bound(blob.MimeType ?? string.Empty, 200), null, Convert.ToBase64String(blob.Blob.ToArray())),
            _ => throw new NotSupportedException("The MCP resource returned an unsupported content block.")
        }).ToArray();
        if (JsonSerializer.Serialize(contents).Length > MaxResultCharacters)
            throw new InvalidOperationException("MCP resource content exceeded Haven's safety size limit.");
        return new McpResourceReadResult(Bound(resourceUri, 2_000), contents);
    }

    public async Task<McpPromptGetResult> GetPromptAsync(ExternalConnection connection, string promptName, IReadOnlyDictionary<string, string>? arguments, CancellationToken cancellationToken)
    {
        var configuration = ReadConfiguration(connection);
        await using var client = await CreateClientAsync(connection, configuration, cancellationToken).ConfigureAwait(false);
        if (client.ServerCapabilities.Prompts is null) throw new NotSupportedException("The negotiated MCP server does not expose prompts.");
        var prompts = await client.ListPromptsAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        var prompt = prompts.FirstOrDefault(item => string.Equals(item.Name, promptName, StringComparison.Ordinal))
            ?? throw new KeyNotFoundException("The requested MCP prompt is no longer advertised by this server.");
        ValidatePromptArguments(prompt, arguments);
        var result = await client.GetPromptAsync(promptName, arguments?.ToDictionary(pair => pair.Key, pair => (object?)pair.Value, StringComparer.Ordinal), cancellationToken: cancellationToken).ConfigureAwait(false);
        var messages = new List<McpPromptMessage>(result.Messages.Count);
        foreach (var message in result.Messages)
        {
            var contents = new List<McpPromptContent>(message.Content.Length);
            foreach (var content in message.Content)
            {
                if (content is TextContentBlock text)
                    contents.Add(new McpPromptContent("text", Bound(text.Text, MaxResultCharacters), null, null, null));
                else if (content is ImageContentBlock image)
                    contents.Add(new McpPromptContent("image", null, Bound(image.MimeType ?? string.Empty, 200), null, Convert.ToBase64String(image.Data.ToArray())));
                else if (content is EmbeddedResourceBlock resource)
                    contents.Add(new McpPromptContent("resource", null, Bound(resource.Resource?.MimeType ?? string.Empty, 200), Bound(resource.Resource?.Uri ?? string.Empty, 2_000), null));
                else
                    throw new NotSupportedException("The MCP prompt returned an unsupported content block.");
            }
            messages.Add(new McpPromptMessage(message.Role.ToString(), contents));
        }
        if (JsonSerializer.Serialize(messages).Length > MaxResultCharacters)
            throw new InvalidOperationException("MCP prompt content exceeded Haven's safety size limit.");
        return new McpPromptGetResult(Bound(prompt.Name, 200), messages);
    }

    private static void ValidatePromptArguments(McpClientPrompt prompt, IReadOnlyDictionary<string, string>? arguments)
    {
        var supplied = arguments ?? new Dictionary<string, string>();
        var declared = (prompt.ProtocolPrompt.Arguments ?? []).ToDictionary(item => item.Name, StringComparer.Ordinal);
        var unknown = supplied.Keys.FirstOrDefault(key => !declared.ContainsKey(key));
        if (unknown is not null) throw new ArgumentException($"MCP prompt argument '{unknown}' is not declared by the server.", nameof(arguments));
        var missing = declared.Values.FirstOrDefault(item => item.Required == true && !supplied.ContainsKey(item.Name));
        if (missing is not null) throw new ArgumentException($"Required MCP prompt argument '{missing.Name}' is missing.", nameof(arguments));
    }

    private async Task<McpClient> CreateClientAsync(ExternalConnection connection, McpConnectionConfiguration configuration, CancellationToken cancellationToken)
    {
        ExternalConnectionRegistryService.ValidateMcpConfiguration(configuration, connection.PresetKey.Equals("uefn", StringComparison.OrdinalIgnoreCase));
        IClientTransport transport;
        if (configuration.Transport == McpTransportKind.StreamableHttp)
        {
            var options = new HttpClientTransportOptions
            {
                Name = ExternalConnectionNaming.PluginName(connection.Name),
                Endpoint = new Uri(configuration.Endpoint!, UriKind.Absolute),
                TransportMode = HttpTransportMode.AutoDetect,
                ConnectionTimeout = TimeSpan.FromSeconds(configuration.TimeoutSeconds),
                EnableStandaloneGetStream = false
            };
            if (configuration.UseOAuth)
            {
                options.OAuth = new ClientOAuthOptions
                {
                    RedirectUri = new Uri(configuration.OAuthRedirectUri, UriKind.Absolute),
                    ClientId = string.IsNullOrWhiteSpace(configuration.OAuthClientId) ? null : configuration.OAuthClientId.Trim(),
                    ClientMetadataDocumentUri = string.IsNullOrWhiteSpace(configuration.OAuthClientMetadataDocumentUri) ? null : new Uri(configuration.OAuthClientMetadataDocumentUri, UriKind.Absolute),
                    Scopes = configuration.OAuthScopes,
                    TokenCache = new McpOAuthTokenCache(secrets, connection.Id),
                    AuthorizationCallbackHandler = McpOAuthBrowserAuthorization.AuthorizeAsync
                };
            }
            transport = new HttpClientTransport(options);
        }
        else if (configuration.Transport == McpTransportKind.Stdio)
        {
            transport = new StdioClientTransport(new StdioClientTransportOptions
            {
                Name = ExternalConnectionNaming.PluginName(connection.Name), Command = configuration.Command!, Arguments = configuration.Arguments?.ToArray() ?? [],
                WorkingDirectory = configuration.WorkingDirectory, InheritEnvironmentVariables = false,
                EnvironmentVariables = StdioClientTransportOptions.GetDefaultEnvironmentVariables(), ShutdownTimeout = TimeSpan.FromSeconds(Math.Min(10, configuration.TimeoutSeconds))
            });
        }
        else throw new InvalidOperationException("Unsupported MCP transport.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(configuration.UseOAuth
            ? TimeSpan.FromMinutes(5)
            : TimeSpan.FromSeconds(configuration.TimeoutSeconds));
        return await McpClient.CreateAsync(transport, cancellationToken: timeout.Token).ConfigureAwait(false);
    }

    private static McpConnectionConfiguration ReadConfiguration(ExternalConnection connection) =>
        JsonSerializer.Deserialize<McpConnectionConfiguration>(connection.ConfigurationJson) ?? throw new InvalidOperationException("The MCP connection configuration is invalid.");

    private static McpExternalTool MapTool(string name, string? description, JsonElement schema) =>
        new(Bound(name, 200), Bound(description ?? string.Empty, 2_000), BoundElement(schema, MaxSchemaCharacters));

    private SemaphoreSlim GetGate(Guid connectionId)
    {
        lock (_serializedGates) return _serializedGates.TryGetValue(connectionId, out var existing) ? existing : (_serializedGates[connectionId] = new SemaphoreSlim(1, 1));
    }

    private static JsonElement BoundElement(JsonElement element, int maxCharacters)
    {
        if (element.GetRawText().Length > maxCharacters) throw new InvalidOperationException("MCP schema or structured result exceeded Haven's safety size limit.");
        return element.Clone();
    }

    private static string BoundedJson<T>(T value, int maxCharacters)
    {
        var json = JsonSerializer.Serialize(value);
        if (json.Length <= maxCharacters) return json;
        return JsonSerializer.Serialize(new
        {
            source = "untrusted external MCP JSON",
            truncated = true,
            reason = "MCP JSON exceeded Haven's safety size limit."
        });
    }

    private static string Bound(string value, int max) => value.Length <= max ? value : value[..max];
}
