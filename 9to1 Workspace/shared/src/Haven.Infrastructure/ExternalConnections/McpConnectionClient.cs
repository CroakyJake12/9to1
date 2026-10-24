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
public sealed partial class McpConnectionClient(IProviderSecretStore secrets) : IMcpConnectionClient, ICloudflareMcpInvocationClient
{
    private const int MaxSchemaCharacters = McpCapabilitySnapshotSerializer.MaximumCharacters;
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
        var identity = new McpServerIdentity(client.ServerInfo?.Name, client.ServerInfo?.Version, client.NegotiatedProtocolVersion,
            SerializeCapabilityMetadata(client.ServerCapabilities, MaxSchemaCharacters, cancellationToken));
        long mappedTextCharacters = 0;
        AddCapabilitySnapshotText(ref mappedTextCharacters, identity.Name);
        AddCapabilitySnapshotText(ref mappedTextCharacters, identity.Version);
        AddCapabilitySnapshotText(ref mappedTextCharacters, identity.ProtocolVersion);
        AddCapabilitySnapshotText(ref mappedTextCharacters, identity.CapabilitiesJson);

        var tools = await client.ListToolsAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        long capabilityCount = tools.Count;
        McpCapabilitySnapshotSerializer.EnsureCollectionCountWithinLimit(capabilityCount);
        var mapped = new McpExternalTool[tools.Count];
        for (var index = 0; index < tools.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var tool = tools[index];
            var name = Bound(tool.Name, 200);
            var description = Bound(tool.Description ?? string.Empty, 2_000);
            AddCapabilitySnapshotText(ref mappedTextCharacters, name);
            AddCapabilitySnapshotText(ref mappedTextCharacters, description);
            // Canonical serialized characters (not source whitespace) use the same bound as persisted snapshots;
            // this avoids copying an unbounded raw JsonElement merely to measure its text.
            var schemaBudget = (int)Math.Min(MaxSchemaCharacters, McpCapabilitySnapshotSerializer.MaximumCharacters - mappedTextCharacters);
            if (schemaBudget <= 0 || !McpCapabilitySnapshotSerializer.TrySerializeBounded(tool.JsonSchema, schemaBudget, out var schemaJson, cancellationToken))
                throw new InvalidOperationException("The MCP provider returned an invalid capability snapshot.");
            AddCapabilitySnapshotText(ref mappedTextCharacters, schemaJson);
            using var schemaDocument = JsonDocument.Parse(schemaJson);
            mapped[index] = new McpExternalTool(name, description, schemaDocument.RootElement.Clone());
        }
        IReadOnlyList<McpExternalResource>? resources = null;
        IReadOnlyList<McpExternalPrompt>? prompts = null;
        if (client.ServerCapabilities.Resources is not null)
        {
            var discovered = await client.ListResourcesAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
            capabilityCount += discovered.Count;
            McpCapabilitySnapshotSerializer.EnsureCollectionCountWithinLimit(capabilityCount);
            var mappedResources = new McpExternalResource[discovered.Count];
            for (var index = 0; index < discovered.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var item = discovered[index];
                var uri = Bound(item.Uri, 2_000);
                var name = Bound(item.Name, 200);
                var description = Bound(item.Description ?? string.Empty, 2_000);
                var mimeType = Bound(item.MimeType ?? string.Empty, 200);
                var metadata = SerializeCapabilityMetadata(item.ProtocolResource, 64_000, cancellationToken);
                AddCapabilitySnapshotText(ref mappedTextCharacters, uri);
                AddCapabilitySnapshotText(ref mappedTextCharacters, name);
                AddCapabilitySnapshotText(ref mappedTextCharacters, description);
                AddCapabilitySnapshotText(ref mappedTextCharacters, mimeType);
                AddCapabilitySnapshotText(ref mappedTextCharacters, metadata);
                mappedResources[index] = new McpExternalResource(uri, name, description, mimeType, metadata);
            }
            resources = mappedResources;
        }
        if (client.ServerCapabilities.Prompts is not null)
        {
            var discovered = await client.ListPromptsAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
            capabilityCount += discovered.Count;
            McpCapabilitySnapshotSerializer.EnsureCollectionCountWithinLimit(capabilityCount);
            var mappedPrompts = new McpExternalPrompt[discovered.Count];
            for (var index = 0; index < discovered.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var item = discovered[index];
                var sourceArguments = item.ProtocolPrompt.Arguments ?? [];
                capabilityCount += sourceArguments.Count;
                McpCapabilitySnapshotSerializer.EnsureCollectionCountWithinLimit(capabilityCount);
                var arguments = new McpExternalPromptArgument[sourceArguments.Count];
                for (var argumentIndex = 0; argumentIndex < sourceArguments.Count; argumentIndex++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var argument = sourceArguments[argumentIndex];
                    var argumentName = Bound(argument.Name, 200);
                    var argumentDescription = Bound(argument.Description ?? string.Empty, 2_000);
                    AddCapabilitySnapshotText(ref mappedTextCharacters, argumentName);
                    AddCapabilitySnapshotText(ref mappedTextCharacters, argumentDescription);
                    arguments[argumentIndex] = new McpExternalPromptArgument(argumentName, argumentDescription, argument.Required ?? false);
                }
                var name = Bound(item.Name, 200);
                var description = Bound(item.Description ?? string.Empty, 2_000);
                var metadata = SerializeCapabilityMetadata(item.ProtocolPrompt, 64_000, cancellationToken);
                AddCapabilitySnapshotText(ref mappedTextCharacters, name);
                AddCapabilitySnapshotText(ref mappedTextCharacters, description);
                AddCapabilitySnapshotText(ref mappedTextCharacters, metadata);
                mappedPrompts[index] = new McpExternalPrompt(name, description, arguments, metadata);
            }
            prompts = mappedPrompts;
        }
        cancellationToken.ThrowIfCancellationRequested();
        var snapshot = McpCapabilitySnapshotSerializer.Serialize(identity, mapped, resources, prompts, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        var snapshotVersion = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(snapshot))).ToLowerInvariant();
        cancellationToken.ThrowIfCancellationRequested();
        return new McpServerDiscovery(identity, mapped, resources, prompts, snapshotVersion);
    }

    private static void AddCapabilitySnapshotText(ref long current, string? value)
    {
        if (value is null) return;
        current += value.Length;
        if (current > McpCapabilitySnapshotSerializer.MaximumCharacters)
            throw new InvalidOperationException("The MCP provider returned an invalid capability snapshot.");
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
            McpCapabilitySnapshotSerializer.EnsureCollectionCountWithinLimit(tools.Count);
            var tool = tools.FirstOrDefault(item => item.Name.Equals(toolName, StringComparison.Ordinal))
                ?? throw new InvalidOperationException($"MCP tool '{toolName}' is no longer advertised by this server. Refresh the connection.");
            var values = arguments.ToDictionary(pair => pair.Key, pair => (object?)JsonSerializer.Deserialize<object>(pair.Value.GetRawText()), StringComparer.Ordinal);
            var result = await tool.CallAsync(values, cancellationToken: cancellationToken).ConfigureAwait(false);
            var text = JoinBoundedText(result.Content.OfType<TextContentBlock>(), MaxResultCharacters, cancellationToken);
            var contentJson = BoundedJson(result.Content, MaxResultCharacters, cancellationToken);
            JsonElement? structured = result.StructuredContent is { } value ? BoundElement(value, MaxResultCharacters, cancellationToken) : null;
            if (string.IsNullOrWhiteSpace(text) && structured is { } structuredValue) text = structuredValue.GetRawText();
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
        McpCapabilitySnapshotSerializer.EnsureCollectionCountWithinLimit(resources.Count);
        if (!resources.Any(item => string.Equals(item.Uri, resourceUri, StringComparison.Ordinal)))
            throw new KeyNotFoundException("The requested MCP resource is no longer advertised by this server.");
        var result = await client.ReadResourceAsync(resourceUri, cancellationToken: cancellationToken).ConfigureAwait(false);
        McpCapabilitySnapshotSerializer.EnsureCollectionCountWithinLimit(result.Contents.Count, MaxResultCharacters,
            "MCP resource content exceeded 9to1's safety size limit.");
        var contents = new McpResourceContent[result.Contents.Count];
        long payloadCharacters = 0;
        for (var index = 0; index < result.Contents.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var content = result.Contents[index];
            contents[index] = content switch
            {
                TextResourceContents text => MapTextResource(text, ref payloadCharacters),
                BlobResourceContents blob => MapBlobResource(blob, ref payloadCharacters),
                _ => throw new NotSupportedException("The MCP resource returned an unsupported content block.")
            };
        }
        if (!McpCapabilitySnapshotSerializer.TrySerializeBounded(contents, MaxResultCharacters, out _, cancellationToken))
            throw new InvalidOperationException("MCP resource content exceeded 9to1's safety size limit.");
        return new McpResourceReadResult(Bound(resourceUri, 2_000), contents);
    }

    public async Task<McpPromptGetResult> GetPromptAsync(ExternalConnection connection, string promptName, IReadOnlyDictionary<string, string>? arguments, CancellationToken cancellationToken)
    {
        var configuration = ReadConfiguration(connection);
        await using var client = await CreateClientAsync(connection, configuration, cancellationToken).ConfigureAwait(false);
        if (client.ServerCapabilities.Prompts is null) throw new NotSupportedException("The negotiated MCP server does not expose prompts.");
        var prompts = await client.ListPromptsAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        McpCapabilitySnapshotSerializer.EnsureCollectionCountWithinLimit(prompts.Count);
        var prompt = prompts.FirstOrDefault(item => string.Equals(item.Name, promptName, StringComparison.Ordinal))
            ?? throw new KeyNotFoundException("The requested MCP prompt is no longer advertised by this server.");
        ValidatePromptArguments(prompt, arguments);
        var result = await client.GetPromptAsync(promptName, arguments?.ToDictionary(pair => pair.Key, pair => (object?)pair.Value, StringComparer.Ordinal), cancellationToken: cancellationToken).ConfigureAwait(false);
        McpCapabilitySnapshotSerializer.EnsureCollectionCountWithinLimit(result.Messages.Count, MaxResultCharacters,
            "MCP prompt content exceeded 9to1's safety size limit.");
        var messages = new List<McpPromptMessage>(result.Messages.Count);
        long payloadCharacters = 0;
        foreach (var message in result.Messages)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var content = message.Content switch
            {
                TextContentBlock text => MapTextPrompt(text, ref payloadCharacters),
                ImageContentBlock image => MapImagePrompt(image, ref payloadCharacters),
                EmbeddedResourceBlock resource => new McpPromptContent("resource", null, Bound(resource.Resource?.MimeType ?? string.Empty, 200), Bound(resource.Resource?.Uri ?? string.Empty, 2_000), null),
                _ => throw new NotSupportedException("The MCP prompt returned an unsupported content block.")
            };
            messages.Add(new McpPromptMessage(message.Role.ToString(), [content]));
        }
        if (!McpCapabilitySnapshotSerializer.TrySerializeBounded(messages, MaxResultCharacters, out _, cancellationToken))
            throw new InvalidOperationException("MCP prompt content exceeded 9to1's safety size limit.");
        return new McpPromptGetResult(Bound(prompt.Name, 200), messages);
    }

    private static McpResourceContent MapTextResource(TextResourceContents text, ref long payloadCharacters)
    {
        var boundedLength = Math.Min(text.Text.Length, MaxResultCharacters);
        AddPayloadCharacters(ref payloadCharacters, boundedLength, "MCP resource content exceeded 9to1's safety size limit.");
        return new McpResourceContent(Bound(text.MimeType ?? string.Empty, 200), Bound(text.Text, MaxResultCharacters), null);
    }

    private static McpResourceContent MapBlobResource(BlobResourceContents blob, ref long payloadCharacters)
    {
        var base64Length = ((long)blob.Blob.Length + 2) / 3 * 4;
        AddPayloadCharacters(ref payloadCharacters, base64Length, "MCP resource content exceeded 9to1's safety size limit.");
        return new McpResourceContent(Bound(blob.MimeType ?? string.Empty, 200), null, Convert.ToBase64String(blob.Blob.ToArray()));
    }

    private static McpPromptContent MapTextPrompt(TextContentBlock text, ref long payloadCharacters)
    {
        var boundedLength = Math.Min(text.Text.Length, MaxResultCharacters);
        AddPayloadCharacters(ref payloadCharacters, boundedLength, "MCP prompt content exceeded 9to1's safety size limit.");
        return new McpPromptContent("text", Bound(text.Text, MaxResultCharacters), null, null, null);
    }

    private static McpPromptContent MapImagePrompt(ImageContentBlock image, ref long payloadCharacters)
    {
        var base64Length = ((long)image.Data.Length + 2) / 3 * 4;
        AddPayloadCharacters(ref payloadCharacters, base64Length, "MCP prompt content exceeded 9to1's safety size limit.");
        return new McpPromptContent("image", null, Bound(image.MimeType ?? string.Empty, 200), null, Convert.ToBase64String(image.Data.ToArray()));
    }

    private static void AddPayloadCharacters(ref long current, long additional, string message)
    {
        if (additional > MaxResultCharacters - current)
            throw new InvalidOperationException(message);
        current += additional;
    }

    private static string JoinBoundedText(IEnumerable<TextContentBlock> blocks, int maximumCharacters, CancellationToken cancellationToken)
    {
        var builder = new StringBuilder(Math.Min(maximumCharacters, 4_096));
        var hasBlock = false;
        var truncated = false;
        foreach (var block in blocks)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (hasBlock)
            {
                if (builder.Length == maximumCharacters) { truncated = true; break; }
                builder.Append('\n');
            }
            hasBlock = true;
            var blockText = block.Text ?? string.Empty;
            var copyLength = Math.Min(blockText.Length, maximumCharacters - builder.Length);
            builder.Append(blockText.AsSpan(0, copyLength));
            if (copyLength < blockText.Length) { truncated = true; break; }
        }
        if (truncated) builder.Append("... [truncated by 9to1]");
        return builder.ToString();
    }

    private static void ValidatePromptArguments(McpClientPrompt prompt, IReadOnlyDictionary<string, string>? arguments)
    {
        var supplied = arguments ?? new Dictionary<string, string>();
        var declaredArguments = prompt.ProtocolPrompt.Arguments ?? [];
        McpCapabilitySnapshotSerializer.EnsureCollectionCountWithinLimit(declaredArguments.Count,
            refusalMessage: "MCP prompt argument catalogue exceeded 9to1's safety size limit.");
        var declared = declaredArguments.ToDictionary(item => item.Name, StringComparer.Ordinal);
        var unknown = supplied.Keys.FirstOrDefault(key => !declared.ContainsKey(key));
        if (unknown is not null) throw new ArgumentException($"MCP prompt argument '{unknown}' is not declared by the server.", nameof(arguments));
        var missing = declared.Values.FirstOrDefault(item => item.Required == true && !supplied.ContainsKey(item.Name));
        if (missing is not null) throw new ArgumentException($"Required MCP prompt argument '{missing.Name}' is missing.", nameof(arguments));
    }

    private async Task<McpClient> CreateClientAsync(ExternalConnection connection, McpConnectionConfiguration configuration, CancellationToken cancellationToken,
        CloudflareOriginalTaskLedger? originalStages = null, Action<McpClient>? retainActualClient = null)
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
        if (originalStages is null) return await McpClient.CreateAsync(transport, cancellationToken: timeout.Token).ConfigureAwait(false);
        return await originalStages.CaptureOriginalAcquisitionAsync(() => McpClient.CreateAsync(transport, cancellationToken: timeout.Token),
            actual => retainActualClient?.Invoke(actual)).ConfigureAwait(false);
    }

    private static McpConnectionConfiguration ReadConfiguration(ExternalConnection connection) =>
        JsonSerializer.Deserialize<McpConnectionConfiguration>(connection.ConfigurationJson) ?? throw new InvalidOperationException("The MCP connection configuration is invalid.");

    private SemaphoreSlim GetGate(Guid connectionId)
    {
        lock (_serializedGates) return _serializedGates.TryGetValue(connectionId, out var existing) ? existing : (_serializedGates[connectionId] = new SemaphoreSlim(1, 1));
    }

    private static JsonElement BoundElement(JsonElement element, int maxCharacters, CancellationToken cancellationToken)
    {
        if (!McpCapabilitySnapshotSerializer.TrySerializeBounded(element, maxCharacters, out var json, cancellationToken))
            throw new InvalidOperationException("MCP schema or structured result exceeded 9to1's safety size limit.");
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private static string SerializeCapabilityMetadata<T>(T value, int maxCharacters, CancellationToken cancellationToken)
    {
        if (McpCapabilitySnapshotSerializer.TrySerializeBounded(value, maxCharacters, out var json, cancellationToken)) return json;
        throw new InvalidOperationException("The MCP provider returned an invalid capability snapshot.");
    }

    private static string BoundedJson<T>(T value, int maxCharacters, CancellationToken cancellationToken)
    {
        if (McpCapabilitySnapshotSerializer.TrySerializeBounded(value, maxCharacters, out var json, cancellationToken)) return json;
        cancellationToken.ThrowIfCancellationRequested();
        return JsonSerializer.Serialize(new
        {
            source = "untrusted external MCP JSON",
            truncated = true,
            reason = "MCP JSON exceeded 9to1's safety size limit."
        });
    }

    private static string Bound(string value, int max) => value.Length <= max ? value : value[..max];
}
