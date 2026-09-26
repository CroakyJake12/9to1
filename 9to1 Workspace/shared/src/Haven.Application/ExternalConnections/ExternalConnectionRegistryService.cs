using System.Text.Json;
using System.Net;
using Haven.Core;

namespace Haven.Application;

/// <summary>Single source of truth for persisted protocol/API connections owned by Haven.</summary>
public sealed class ExternalConnectionRegistryService(IExternalConnectionRepository repository, IMcpConnectionClient mcp, IProviderSecretStore? secrets = null)
{
    public Task<IReadOnlyList<ExternalConnection>> GetAllAsync(CancellationToken cancellationToken) => repository.GetAllAsync(cancellationToken);

    public async Task<ExternalConnection> ConnectUefnAsync(string? endpoint, CancellationToken cancellationToken)
    {
        var config = McpConnectionConfiguration.UefnDefault with
        {
            Endpoint = string.IsNullOrWhiteSpace(endpoint) ? McpConnectionConfiguration.UefnDefault.Endpoint : endpoint.Trim()
        };
        ValidateMcpConfiguration(config, requireLoopback: true);
        var existing = (await repository.GetAllAsync(cancellationToken).ConfigureAwait(false))
            .FirstOrDefault(item => item.Kind == ExternalConnectionKind.Mcp && item.PresetKey.Equals("uefn", StringComparison.OrdinalIgnoreCase));
        var now = DateTimeOffset.UtcNow;
        var candidate = existing is null
            ? new ExternalConnection(Guid.NewGuid(), "UEFN", "epic.uefn", ExternalConnectionKind.Mcp, "uefn", true, ExternalConnectionState.Connecting,
                "Checking Unreal MCP...", JsonSerializer.Serialize(config), null, null, null, now, now)
            : existing with { IsEnabled = true, State = ExternalConnectionState.Connecting, Status = "Checking Unreal MCP...", ConfigurationJson = JsonSerializer.Serialize(config), UpdatedAt = now };
        await repository.UpsertAsync(candidate, cancellationToken).ConfigureAwait(false);
        return await RefreshMcpAsync(candidate, cancellationToken).ConfigureAwait(false);
    }

    public async Task<ExternalConnection> AddMcpAsync(string name, McpConnectionConfiguration configuration, CancellationToken cancellationToken)
    {
        ValidateMcpConfiguration(configuration, requireLoopback: false);
        var now = DateTimeOffset.UtcNow;
        var connection = new ExternalConnection(Guid.NewGuid(), name.Trim(), "mcp.custom", ExternalConnectionKind.Mcp, "custom-mcp", true, ExternalConnectionState.Connecting,
            "Connecting to MCP server...", JsonSerializer.Serialize(configuration), null, null, null, now, now);
        await repository.UpsertAsync(connection, cancellationToken).ConfigureAwait(false);
        return await RefreshMcpAsync(connection, cancellationToken).ConfigureAwait(false);
    }

    public async Task<ExternalConnection> RefreshMcpAsync(ExternalConnection connection, CancellationToken cancellationToken)
    {
        var current = await repository.GetAsync(connection.Id, cancellationToken).ConfigureAwait(false);
        if (current is null)
            return connection with { IsEnabled = false, State = ExternalConnectionState.Disconnected, Status = "This connection was removed.", UpdatedAt = DateTimeOffset.UtcNow };
        if (!current.IsEnabled) return current;

        var previousState = current.State;
        var checking = current with
        {
            State = previousState is ExternalConnectionState.Ready or ExternalConnectionState.Offline or ExternalConnectionState.NeedsAttention or ExternalConnectionState.Degraded
                ? ExternalConnectionState.Reconnecting
                : ExternalConnectionState.Connecting,
            Status = "Checking MCP connection...",
            UpdatedAt = DateTimeOffset.UtcNow
        };
        await repository.UpsertAsync(checking, cancellationToken).ConfigureAwait(false);
        try
        {
            var discovery = await mcp.DiscoverCapabilitiesAsync(checking, cancellationToken).ConfigureAwait(false);
            if (checking.PresetKey.Equals("uefn", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(discovery.Identity.Name, "unreal-mcp", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("The endpoint responded, but it is not the UEFN Unreal MCP server (expected server identity 'unreal-mcp').");
            if (string.IsNullOrWhiteSpace(discovery.SnapshotVersion))
                throw new InvalidOperationException("The MCP provider returned an invalid capability snapshot.");
            var updated = checking with
            {
                State = ExternalConnectionState.Ready,
                Status = DiscoveryStatus(discovery),
                ServerName = discovery.Identity.Name,
                ServerVersion = discovery.Identity.Version,
                ProtocolVersion = discovery.Identity.ProtocolVersion,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            await repository.UpsertAsync(updated, cancellationToken).ConfigureAwait(false);
            return updated;
        }
        catch (OperationCanceledException)
        {
            var updated = checking with { State = ExternalConnectionState.Offline, Status = "The connection check was cancelled. Retry when ready.", UpdatedAt = DateTimeOffset.UtcNow };
            await repository.UpsertAsync(updated, CancellationToken.None).ConfigureAwait(false);
            throw;
        }
        catch (Exception ex)
        {
            var message = Diagnose(checking, ex);
            var updated = checking with { State = FailureState(ex), Status = message, UpdatedAt = DateTimeOffset.UtcNow };
            await repository.UpsertAsync(updated, cancellationToken).ConfigureAwait(false);
            return updated;
        }
    }

    public async Task RemoveAsync(Guid id, CancellationToken cancellationToken)
    {
        if (secrets is not null)
            await secrets.DeleteAsync(ExternalConnectionNaming.SecretProviderId(id), ExternalConnectionNaming.OAuthTokenSecretName, cancellationToken).ConfigureAwait(false);
        await repository.DeleteAsync(id, cancellationToken).ConfigureAwait(false);
    }

    public static void ValidateMcpConfiguration(McpConnectionConfiguration configuration, bool requireLoopback)
    {
        if (configuration.TimeoutSeconds is < 1 or > 300) throw new ArgumentOutOfRangeException(nameof(configuration), "MCP timeout must be between 1 and 300 seconds.");
        if (configuration.Transport == McpTransportKind.StreamableHttp)
        {
            if (!Uri.TryCreate(configuration.Endpoint, UriKind.Absolute, out var endpoint) || endpoint.Scheme is not ("http" or "https"))
                throw new InvalidOperationException("Enter an absolute HTTP or HTTPS MCP endpoint.");
            var loopback = endpoint.IsLoopback;
            if ((configuration.LocalOnly || requireLoopback) && !loopback) throw new InvalidOperationException("This connection is local-only and must use a loopback address.");
            if (!loopback && endpoint.Scheme != Uri.UriSchemeHttps) throw new InvalidOperationException("Remote MCP servers must use HTTPS.");
            if (requireLoopback && configuration.UseOAuth) throw new InvalidOperationException("The UEFN Unreal MCP preset is loopback-only and does not use OAuth.");
            if (configuration.UseOAuth)
            {
                if (!Uri.TryCreate(configuration.OAuthRedirectUri, UriKind.Absolute, out var redirect) || !redirect.IsLoopback || redirect.Scheme != Uri.UriSchemeHttp)
                    throw new InvalidOperationException("MCP OAuth requires an HTTP loopback redirect URI.");
                if (!string.IsNullOrWhiteSpace(configuration.OAuthClientMetadataDocumentUri) &&
                    (!Uri.TryCreate(configuration.OAuthClientMetadataDocumentUri, UriKind.Absolute, out var metadata) || metadata.Scheme != Uri.UriSchemeHttps))
                    throw new InvalidOperationException("MCP OAuth client metadata must use an absolute HTTPS URI.");
            }
        }
        else if (string.IsNullOrWhiteSpace(configuration.Command))
            throw new InvalidOperationException("A stdio MCP connection requires an executable command.");
    }

    private static string Diagnose(ExternalConnection connection, Exception exception)
    {
        var text = exception.Message;
        if (!connection.PresetKey.Equals("uefn", StringComparison.OrdinalIgnoreCase))
            return exception is HttpRequestException { StatusCode: HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden }
                ? "MCP authentication is required. Reconnect the account and review granted scopes."
                : exception is HttpRequestException { StatusCode: HttpStatusCode.TooManyRequests }
                    ? "MCP provider rate limit reached. Retry after the provider's cooldown."
                    : "MCP connection unavailable. Check the endpoint and authentication, then retry.";
        if (text.Contains("refused", StringComparison.OrdinalIgnoreCase) || text.Contains("actively refused", StringComparison.OrdinalIgnoreCase))
            return "UEFN MCP is not reachable. Make sure UEFN is running, Python Editor Scripting and UEFN MCP Toolsets are enabled, and the Unreal MCP server has started.";
        if (text.Contains("timed out", StringComparison.OrdinalIgnoreCase) || text.Contains("timeout", StringComparison.OrdinalIgnoreCase))
            return "UEFN MCP timed out. Check that the editor and MCP server are responsive, then verify the host, port and path in Advanced settings.";
        if (text.Contains("unreal-mcp", StringComparison.OrdinalIgnoreCase))
            return "The endpoint responded, but it is not the UEFN Unreal MCP server (expected server identity 'unreal-mcp').";
        return "UEFN MCP could not connect. Check the editor and server status, then retry.";
    }

    private static ExternalConnectionState FailureState(Exception exception) => exception switch
    {
        HttpRequestException { StatusCode: HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden } => ExternalConnectionState.NeedsAttention,
        HttpRequestException { StatusCode: HttpStatusCode.TooManyRequests } => ExternalConnectionState.Degraded,
        InvalidOperationException invalid when invalid.Message.Contains("unreal-mcp", StringComparison.OrdinalIgnoreCase) ||
            invalid.Message.Contains("invalid capability snapshot", StringComparison.OrdinalIgnoreCase) => ExternalConnectionState.Failed,
        _ => ExternalConnectionState.Offline
    };

    private static string DiscoveryStatus(McpServerDiscovery discovery)
    {
        static string Describe<T>(IReadOnlyList<T>? values, string itemName) => values is null
            ? $"{itemName}s unsupported"
            : $"{values.Count} {itemName}{(values.Count == 1 ? string.Empty : "s")}";
        return $"Connected - {Describe(discovery.Tools, "tool")}, {Describe(discovery.Resources, "resource")}, {Describe(discovery.Prompts, "prompt")}.";
    }
}
