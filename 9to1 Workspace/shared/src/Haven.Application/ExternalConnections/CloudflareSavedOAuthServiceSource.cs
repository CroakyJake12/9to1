using System.Runtime.CompilerServices;
using System.Text.Json;
using Haven.Core;
namespace Haven.Application;

/// <summary>Uses the actual saved MCP connection and maintained SDK OAuth cache. No bearer/cache value is read or returned here.</summary>
public sealed class CloudflareSavedOAuthServiceSource : ICloudflareSavedServiceSource
{
    private readonly IExternalConnectionRepository _connections;
    private readonly Guid _connectionId;
    private readonly Uri _endpoint;
    private readonly string _accountId;
    private readonly string _executeTool;
    private readonly ConditionalWeakTable<CloudflareSavedService, object> _issued = new();
    public CloudflareSavedOAuthServiceSource(IExternalConnectionRepository connections, Guid trustedConnectionId, Uri trustedEndpoint, string trustedAccountId, string trustedExecuteTool)
    {
        if (trustedConnectionId == Guid.Empty || !trustedEndpoint.IsAbsoluteUri || trustedEndpoint.Scheme != "https" ||
            !string.IsNullOrEmpty(trustedEndpoint.Query) || !string.IsNullOrEmpty(trustedEndpoint.UserInfo) || !string.IsNullOrEmpty(trustedEndpoint.Fragment) || !CloudflareTypedToolCatalogue.IsHexId(trustedAccountId) ||
            string.IsNullOrWhiteSpace(trustedExecuteTool) || trustedExecuteTool.Length > 200) throw new ArgumentException("Explicit trusted saved OAuth service binding required.");
        _connections = connections; _connectionId = trustedConnectionId; _endpoint = trustedEndpoint; _accountId = trustedAccountId; _executeTool = trustedExecuteTool;
    }
    public async Task<CloudflareSavedService> AcquireOriginalAsync(CancellationToken token)
    {
        var stages = new CloudflareOriginalTaskLedger();
        var connection = await stages.AwaitAsync(stages.Invoke(() => _connections.GetAsync(_connectionId, token))).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The configured saved Cloudflare connection is unavailable.");
        Demand(connection);
        var service = new CloudflareSavedService(connection, _accountId, _executeTool); _issued.Add(service, new()); return service;
    }
    public async Task RevalidateOriginalAsync(CloudflareSavedService sameService, CancellationToken token)
    {
        if (!_issued.TryGetValue(sameService, out _)) throw new UnauthorizedAccessException("SAME saved-service issuing reference required.");
        var stages = new CloudflareOriginalTaskLedger();
        var current = await stages.AwaitAsync(stages.Invoke(() => _connections.GetAsync(_connectionId, token))).ConfigureAwait(false);
        if (current != sameService.Connection) throw new InvalidOperationException("Saved Cloudflare service configuration changed.");
        Demand(current!);
    }
    private void Demand(ExternalConnection connection)
    {
        var configuration = JsonSerializer.Deserialize<McpConnectionConfiguration>(connection.ConfigurationJson);
        if (connection.Id != _connectionId || connection.Kind != ExternalConnectionKind.Mcp || !connection.IsEnabled || connection.State != ExternalConnectionState.Ready ||
            configuration is null || configuration.Transport != McpTransportKind.StreamableHttp || !configuration.UseOAuth || configuration.LocalOnly ||
            !Uri.TryCreate(configuration.Endpoint, UriKind.Absolute, out var endpoint) || endpoint != _endpoint)
            throw new InvalidOperationException("An enabled ready saved OAuth MCP service at the exact trusted endpoint is required.");
    }
}
