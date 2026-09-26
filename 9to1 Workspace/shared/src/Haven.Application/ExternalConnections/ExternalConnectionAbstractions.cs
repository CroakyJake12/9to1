using System.Text.Json;
using Haven.Core;

namespace Haven.Application;

public interface IExternalConnectionRepository
{
    Task<IReadOnlyList<ExternalConnection>> GetAllAsync(CancellationToken cancellationToken);
    Task<ExternalConnection?> GetAsync(Guid id, CancellationToken cancellationToken);
    Task UpsertAsync(ExternalConnection connection, CancellationToken cancellationToken);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken);
}

public interface IMcpConnectionClient
{
    Task<(McpServerIdentity Identity, IReadOnlyList<McpExternalTool> Tools)> DiscoverAsync(ExternalConnection connection, CancellationToken cancellationToken);
    Task<McpToolInvocationResult> InvokeAsync(ExternalConnection connection, string toolName, IReadOnlyDictionary<string, JsonElement> arguments, CancellationToken cancellationToken);

    async Task<McpServerDiscovery> DiscoverCapabilitiesAsync(ExternalConnection connection, CancellationToken cancellationToken)
    {
        var (identity, tools) = await DiscoverAsync(connection, cancellationToken).ConfigureAwait(false);
        var snapshot = JsonSerializer.Serialize(new { identity, tools });
        return new McpServerDiscovery(identity, tools, null, null, Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(snapshot))).ToLowerInvariant());
    }

    Task<McpResourceReadResult> ReadResourceAsync(ExternalConnection connection, string resourceUri, CancellationToken cancellationToken) =>
        Task.FromException<McpResourceReadResult>(new NotSupportedException("This MCP client does not support resources."));

    Task<McpPromptGetResult> GetPromptAsync(ExternalConnection connection, string promptName, IReadOnlyDictionary<string, string>? arguments, CancellationToken cancellationToken) =>
        Task.FromException<McpPromptGetResult>(new NotSupportedException("This MCP client does not support prompts."));
}

/// <summary>Produces transient catalogue capabilities backed by current runtime state.</summary>
public interface IDynamicCapabilityProvider
{
    Task<IReadOnlyList<CapabilityDefinition>> GetCapabilitiesAsync(CapabilityPlatform platform, CancellationToken cancellationToken);
}
