using System.Net;
using System.Text.Json;
using Haven.Application;
using Haven.Core;

namespace Haven.Core.Tests;

public sealed class ExternalConnectionRegistryLifecycleTests
{
    [Fact]
    public async Task RefreshRegistersTypedMcpSnapshotAndKeepsConnectionIdentityStable()
    {
        var repository = new MemoryConnectionRepository();
        var connection = Connection();
        await repository.UpsertAsync(connection, CancellationToken.None);
        var identity = new McpServerIdentity("remote-server", "1.2", "2025-06-18", "{}");
        var discovery = new McpServerDiscovery(
            identity,
            [new McpExternalTool("read_item", "Read item", Element("{}"))],
            [],
            [new McpExternalPrompt("summarize", "Summarize", [])],
            new string('a', 64));
        var service = new ExternalConnectionRegistryService(repository, new FakeMcpClient(discovery));

        var refreshed = await service.RefreshMcpAsync(connection, CancellationToken.None);

        Assert.Equal(connection.ServerId, refreshed.ServerId);
        Assert.Equal(ExternalConnectionState.Ready, refreshed.State);
        Assert.Equal("remote-server", refreshed.ServerName);
        Assert.Equal("Connected - 1 tool, 0 resources, 1 prompt.", refreshed.Status);
    }

    [Fact]
    public async Task RefreshMapsAuthenticationAndRateLimitFailuresToTypedHealthStatesWithoutRawErrors()
    {
        var auth = await RefreshFailure(new HttpRequestException("access_token=private-secret", null, HttpStatusCode.Unauthorized));
        Assert.Equal(ExternalConnectionState.NeedsAttention, auth.State);
        Assert.Contains("authentication is required", auth.Status, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("private-secret", auth.Status, StringComparison.Ordinal);

        var scope = await RefreshFailure(new HttpRequestException("access_token=private-secret", null, HttpStatusCode.Forbidden));
        Assert.Equal(ExternalConnectionState.NeedsAttention, scope.State);
        Assert.Contains("scope is insufficient", scope.Status, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("private-secret", scope.Status, StringComparison.Ordinal);

        var limited = await RefreshFailure(new HttpRequestException("access_token=private-secret", null, HttpStatusCode.TooManyRequests));
        Assert.Equal(ExternalConnectionState.Degraded, limited.State);
        Assert.Contains("rate limit", limited.Status, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("private-secret", limited.Status, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CancelledRefreshLeavesConnectionOutOfConnectingState()
    {
        var repository = new MemoryConnectionRepository();
        var connection = Connection();
        await repository.UpsertAsync(connection, CancellationToken.None);
        var service = new ExternalConnectionRegistryService(repository, new FakeMcpClient(exception: new OperationCanceledException("cancelled")));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.RefreshMcpAsync(connection, CancellationToken.None));
        var updated = await repository.GetAsync(connection.Id, CancellationToken.None);
        Assert.NotNull(updated);
        Assert.Equal(ExternalConnectionState.Offline, updated.State);
        Assert.Contains("cancelled", updated.Status, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RefreshDoesNotRecreateRemovedOrProbeDisabledConnections()
    {
        var repository = new MemoryConnectionRepository();
        var removed = Connection();
        var client = new FakeMcpClient(new McpServerDiscovery(new McpServerIdentity("server", "1", "v", "{}"), [], null, null, new string('b', 64)));
        var service = new ExternalConnectionRegistryService(repository, client);

        var result = await service.RefreshMcpAsync(removed, CancellationToken.None);

        Assert.Equal(ExternalConnectionState.Disconnected, result.State);
        Assert.Null(await repository.GetAsync(removed.Id, CancellationToken.None));
        Assert.Equal(0, client.DiscoveryCalls);

        var disabled = removed with { IsEnabled = false, State = ExternalConnectionState.Disabled };
        await repository.UpsertAsync(disabled, CancellationToken.None);
        var unchanged = await service.RefreshMcpAsync(disabled, CancellationToken.None);
        Assert.Equal(ExternalConnectionState.Disabled, unchanged.State);
        Assert.Equal(0, client.DiscoveryCalls);
    }

    private static async Task<ExternalConnection> RefreshFailure(Exception exception)
    {
        var repository = new MemoryConnectionRepository();
        var connection = Connection();
        await repository.UpsertAsync(connection, CancellationToken.None);
        return await new ExternalConnectionRegistryService(repository, new FakeMcpClient(exception: exception))
            .RefreshMcpAsync(connection, CancellationToken.None);
    }

    private static ExternalConnection Connection()
    {
        var now = DateTimeOffset.UtcNow;
        return new ExternalConnection(Guid.NewGuid(), "Remote MCP", "mcp.test", ExternalConnectionKind.Mcp, "custom-mcp", true,
            ExternalConnectionState.Ready, "Connected", JsonSerializer.Serialize(new McpConnectionConfiguration(McpTransportKind.StreamableHttp, "https://example.test/mcp")),
            null, null, null, now, now);
    }

    private static JsonElement Element(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private sealed class MemoryConnectionRepository : IExternalConnectionRepository
    {
        private readonly Dictionary<Guid, ExternalConnection> _items = [];
        public Task<IReadOnlyList<ExternalConnection>> GetAllAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<ExternalConnection>>(_items.Values.ToArray());
        public Task<ExternalConnection?> GetAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult(_items.GetValueOrDefault(id));
        public Task UpsertAsync(ExternalConnection connection, CancellationToken cancellationToken) { _items[connection.Id] = connection; return Task.CompletedTask; }
        public Task DeleteAsync(Guid id, CancellationToken cancellationToken) { _items.Remove(id); return Task.CompletedTask; }
    }

    private sealed class FakeMcpClient(McpServerDiscovery? discovery = null, Exception? exception = null) : IMcpConnectionClient
    {
        public int DiscoveryCalls { get; private set; }

        public Task<(McpServerIdentity Identity, IReadOnlyList<McpExternalTool> Tools)> DiscoverAsync(ExternalConnection connection, CancellationToken cancellationToken)
        {
            var identity = discovery?.Identity ?? new McpServerIdentity("server", "1", "v", "{}");
            IReadOnlyList<McpExternalTool> tools = discovery?.Tools ?? Array.Empty<McpExternalTool>();
            return Task.FromResult((identity, tools));
        }

        public Task<McpServerDiscovery> DiscoverCapabilitiesAsync(ExternalConnection connection, CancellationToken cancellationToken)
        {
            DiscoveryCalls++;
            if (exception is not null) return Task.FromException<McpServerDiscovery>(exception);
            return Task.FromResult(discovery ?? new McpServerDiscovery(new McpServerIdentity("server", "1", "v", "{}"), [], null, null, new string('c', 64)));
        }

        public Task<McpToolInvocationResult> InvokeAsync(ExternalConnection connection, string toolName, IReadOnlyDictionary<string, JsonElement> arguments, CancellationToken cancellationToken) =>
            Task.FromResult(new McpToolInvocationResult(true, "ok", null, "[]"));
    }
}
