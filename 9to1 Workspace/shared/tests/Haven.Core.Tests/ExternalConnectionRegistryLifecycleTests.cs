using System.Collections;
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
        Assert.Equal(new string('a', 64), refreshed.CapabilitySnapshotVersion);
        Assert.Contains("read_item", refreshed.CapabilitySnapshotJson!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RefreshReplacesPersistedCapabilitySnapshotWhenProviderChanges()
    {
        var repository = new MemoryConnectionRepository();
        var connection = Connection();
        await repository.UpsertAsync(connection, CancellationToken.None);
        var first = new McpServerDiscovery(new McpServerIdentity("remote-server", "1", "2025-06-18", "{}"),
            [new McpExternalTool("read_item", "Read item", Element("{}"))], [], [], new string('a', 64));
        var client = new FakeMcpClient(first);
        var service = new ExternalConnectionRegistryService(repository, client);
        var connected = await service.RefreshMcpAsync(connection, CancellationToken.None);

        client.Discovery = first with
        {
            Tools = [new McpExternalTool("search_items", "Search items", Element("{}"))],
            SnapshotVersion = new string('b', 64)
        };
        var refreshed = await service.RefreshMcpAsync(connected, CancellationToken.None);

        Assert.Equal(connection.ServerId, refreshed.ServerId);
        Assert.Equal(new string('b', 64), refreshed.CapabilitySnapshotVersion);
        Assert.Contains("search_items", refreshed.CapabilitySnapshotJson!, StringComparison.Ordinal);
        Assert.DoesNotContain("read_item", refreshed.CapabilitySnapshotJson!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RefreshDoesNotMarkOversizedCapabilitySnapshotReady()
    {
        var repository = new MemoryConnectionRepository();
        var connection = Connection();
        await repository.UpsertAsync(connection, CancellationToken.None);
        var oversized = new McpServerDiscovery(new McpServerIdentity("remote-server", "1", "2025-06-18", "{}"),
            [new McpExternalTool("read_item", new string('x', McpCapabilitySnapshotSerializer.MaximumCharacters + 1), Element("{}"))], [], [], new string('a', 64));

        var refreshed = await new ExternalConnectionRegistryService(repository, new FakeMcpClient(oversized))
            .RefreshMcpAsync(connection, CancellationToken.None);

        Assert.Equal(ExternalConnectionState.Failed, refreshed.State);
        Assert.Null(refreshed.CapabilitySnapshotJson);
        Assert.Null(refreshed.CapabilitySnapshotVersion);
    }

    [Fact]
    public async Task RefreshAcceptsUnicodeCapabilitySnapshotWithinExistingCharacterLimit()
    {
        var repository = new MemoryConnectionRepository();
        var connection = Connection();
        await repository.UpsertAsync(connection, CancellationToken.None);
        var unicodeDescription = new string('é', 30_000);
        var discovery = new McpServerDiscovery(new McpServerIdentity("remote-server", "1", "2025-06-18", "{}"),
            [new McpExternalTool("read_item", unicodeDescription, Element("{}"))], [], [], new string('a', 64));

        var refreshed = await new ExternalConnectionRegistryService(repository, new FakeMcpClient(discovery))
            .RefreshMcpAsync(connection, CancellationToken.None);

        Assert.Equal(ExternalConnectionState.Ready, refreshed.State);
        Assert.True(refreshed.CapabilitySnapshotJson!.Length <= McpCapabilitySnapshotSerializer.MaximumCharacters);
        Assert.Contains(JsonSerializer.Serialize(unicodeDescription), refreshed.CapabilitySnapshotJson!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OversizedCapabilityCollectionIsRefusedBeforeEnumeration()
    {
        var repository = new MemoryConnectionRepository();
        var connection = Connection();
        await repository.UpsertAsync(connection, CancellationToken.None);
        var tools = new OversizedTools();
        var discovery = new McpServerDiscovery(new McpServerIdentity("remote-server", "1", "2025-06-18", "{}"),
            tools, null, null, new string('a', 64));

        var refreshed = await new ExternalConnectionRegistryService(repository, new FakeMcpClient(discovery))
            .RefreshMcpAsync(connection, CancellationToken.None);

        Assert.Equal(ExternalConnectionState.Failed, refreshed.State);
        Assert.Contains("invalid capability snapshot", refreshed.Status, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, tools.EnumerationAttempts);
        Assert.Null(refreshed.CapabilitySnapshotJson);
    }

    [Fact]
    public async Task OversizedSchemaRefusalRetainsPreviousValidSnapshotAsStale()
    {
        var repository = new MemoryConnectionRepository();
        var connection = Connection();
        await repository.UpsertAsync(connection, CancellationToken.None);
        var valid = new McpServerDiscovery(new McpServerIdentity("remote-server", "1", "2025-06-18", "{}"),
            [new McpExternalTool("read_item", "Read item", Element("{}"))], [], [], new string('a', 64));
        var client = new FakeMcpClient(valid);
        var service = new ExternalConnectionRegistryService(repository, client);
        var connected = await service.RefreshMcpAsync(connection, CancellationToken.None);
        var retainedSnapshot = connected.CapabilitySnapshotJson;
        var retainedVersion = connected.CapabilitySnapshotVersion;

        var veryLargeSchema = JsonSerializer.Serialize(new { type = "object", description = new string('x', 2_000_000) });
        client.Discovery = valid with
        {
            Tools = [new McpExternalTool("read_item", "Read item", Element(veryLargeSchema))],
            SnapshotVersion = new string('b', 64)
        };
        var refused = await service.RefreshMcpAsync(connected, CancellationToken.None);

        Assert.Equal(ExternalConnectionState.Failed, refused.State);
        Assert.Contains("invalid capability snapshot", refused.Status, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(retainedSnapshot, refused.CapabilitySnapshotJson);
        Assert.Equal(retainedVersion, refused.CapabilitySnapshotVersion);
    }

    [Fact]
    public async Task CancellationAtSnapshotSerializationLeavesConnectionOffline()
    {
        var repository = new MemoryConnectionRepository();
        var connection = Connection();
        await repository.UpsertAsync(connection, CancellationToken.None);
        using var cancellation = new CancellationTokenSource();
        var discovery = new McpServerDiscovery(new McpServerIdentity("remote-server", "1", "2025-06-18", "{}"),
            new CancellationTriggeredTools(cancellation.Cancel), [], [], new string('a', 64));
        var client = new FakeMcpClient(discovery);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new ExternalConnectionRegistryService(repository, client).RefreshMcpAsync(connection, cancellation.Token));

        var updated = await repository.GetAsync(connection.Id, CancellationToken.None);
        Assert.NotNull(updated);
        Assert.Equal(ExternalConnectionState.Offline, updated.State);
        Assert.Null(updated.CapabilitySnapshotJson);
    }

    [Fact]
    public async Task CapabilitySnapshotRetainsExactCharacterBoundaryAndPriorValidHistory()
    {
        var empty = new McpServerDiscovery(new McpServerIdentity("remote-server", "1", "2025-06-18", "{}"),
            [new McpExternalTool("read_item", string.Empty, Element("{}"))],
            Array.Empty<McpExternalResource>(), Array.Empty<McpExternalPrompt>(), new string('a', 64));
        var baseJson = JsonSerializer.Serialize(new
        {
            identity = empty.Identity,
            tools = empty.Tools,
            resources = empty.Resources,
            prompts = empty.Prompts
        });
        var remainingCharacters = McpCapabilitySnapshotSerializer.MaximumCharacters - baseJson.Length;
        Assert.True(remainingCharacters > 0);

        var repository = new MemoryConnectionRepository();
        var connection = Connection();
        await repository.UpsertAsync(connection, CancellationToken.None);
        var client = new FakeMcpClient(empty with
        {
            Tools = [new McpExternalTool("read_item", new string('x', remainingCharacters), Element("{}"))]
        });
        var service = new ExternalConnectionRegistryService(repository, client);
        var accepted = await service.RefreshMcpAsync(connection, CancellationToken.None);
        Assert.Equal(McpCapabilitySnapshotSerializer.MaximumCharacters, accepted.CapabilitySnapshotJson!.Length);
        var retainedSnapshot = accepted.CapabilitySnapshotJson;
        var retainedVersion = accepted.CapabilitySnapshotVersion;

        client.Discovery = empty with
        {
            Tools = [new McpExternalTool("read_item", new string('x', remainingCharacters + 1), Element("{}"))],
            SnapshotVersion = new string('b', 64)
        };
        var refused = await service.RefreshMcpAsync(accepted, CancellationToken.None);
        Assert.Equal(ExternalConnectionState.Failed, refused.State);
        Assert.Equal(retainedSnapshot, refused.CapabilitySnapshotJson);
        Assert.Equal(retainedVersion, refused.CapabilitySnapshotVersion);
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
    public async Task AuthenticationExpiryCanBeRecoveredByRefreshingTheSameSavedConnection()
    {
        var repository = new MemoryConnectionRepository();
        var connection = Connection();
        await repository.UpsertAsync(connection, CancellationToken.None);
        var client = new FakeMcpClient(exception: new HttpRequestException("expired credential", null, HttpStatusCode.Unauthorized));
        var service = new ExternalConnectionRegistryService(repository, client);

        var expired = await service.RefreshMcpAsync(connection, CancellationToken.None);
        Assert.Equal(connection.Id, expired.Id);
        Assert.Equal(ExternalConnectionState.NeedsAttention, expired.State);

        client.Exception = null;
        var reopened = await service.RefreshMcpAsync(expired, CancellationToken.None);
        Assert.Equal(connection.Id, reopened.Id);
        Assert.Equal(ExternalConnectionState.Ready, reopened.State);
    }

    [Fact]
    public async Task CancelledRefreshLeavesConnectionOutOfConnectingState()
    {
        var repository = new MemoryConnectionRepository();
        var connection = Connection();
        await repository.UpsertAsync(connection, CancellationToken.None);
        var client = new FakeMcpClient(exception: new OperationCanceledException("cancelled"));
        var service = new ExternalConnectionRegistryService(repository, client);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.RefreshMcpAsync(connection, CancellationToken.None));
        var updated = await repository.GetAsync(connection.Id, CancellationToken.None);
        Assert.NotNull(updated);
        Assert.Equal(ExternalConnectionState.Offline, updated.State);
        Assert.Contains("cancelled", updated.Status, StringComparison.OrdinalIgnoreCase);

        // A cancelled authorization/connection attempt is recoverable: a later
        // explicit refresh must be able to reconnect the same saved connection.
        client.Exception = null;
        var reconnected = await service.RefreshMcpAsync(updated, CancellationToken.None);
        Assert.Equal(connection.Id, reconnected.Id);
        Assert.Equal(ExternalConnectionState.Ready, reconnected.State);
    }

    [Fact]
    public async Task CancellationDuringFinalRefreshPublicationLeavesConnectionOffline()
    {
        var repository = new MemoryConnectionRepository
        {
            SupportsGuardedPublication = true,
            CancelOnReplacementState = ExternalConnectionState.Ready
        };
        var connection = Connection();
        await repository.UpsertAsync(connection, CancellationToken.None);
        var service = new ExternalConnectionRegistryService(repository, new FakeMcpClient());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.RefreshMcpAsync(connection, CancellationToken.None));

        var updated = await repository.GetAsync(connection.Id, CancellationToken.None);
        Assert.NotNull(updated);
        Assert.Equal(ExternalConnectionState.Offline, updated.State);
        Assert.Contains("cancelled", updated.Status, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CancellationAfterCheckingStateWasPublishedDoesNotLeaveConnectionConnecting()
    {
        var repository = new MemoryConnectionRepository
        {
            SupportsGuardedPublication = true,
            CancelAfterPublishState = ExternalConnectionState.Reconnecting
        };
        var connection = Connection();
        await repository.UpsertAsync(connection, CancellationToken.None);
        var service = new ExternalConnectionRegistryService(repository, new FakeMcpClient());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.RefreshMcpAsync(connection, CancellationToken.None));

        var updated = await repository.GetAsync(connection.Id, CancellationToken.None);
        Assert.NotNull(updated);
        Assert.Equal(ExternalConnectionState.Offline, updated.State);
    }

    [Theory]
    [InlineData(ExternalConnectionState.Connecting, ExternalConnectionState.Connecting)]
    [InlineData(ExternalConnectionState.Ready, ExternalConnectionState.Reconnecting)]
    [InlineData(ExternalConnectionState.Offline, ExternalConnectionState.Reconnecting)]
    [InlineData(ExternalConnectionState.NeedsAttention, ExternalConnectionState.Reconnecting)]
    [InlineData(ExternalConnectionState.Degraded, ExternalConnectionState.Reconnecting)]
    [InlineData(ExternalConnectionState.Failed, ExternalConnectionState.Reconnecting)]
    [InlineData(ExternalConnectionState.Stopped, ExternalConnectionState.Reconnecting)]
    [InlineData(ExternalConnectionState.Disconnected, ExternalConnectionState.Reconnecting)]
    [InlineData(ExternalConnectionState.Reconnecting, ExternalConnectionState.Reconnecting)]
    public async Task RefreshUsesConnectingOnlyForInitialConnectAndReconnectingForSavedConnections(
        ExternalConnectionState priorState, ExternalConnectionState expectedState)
    {
        var repository = new MemoryConnectionRepository();
        var connection = Connection() with { State = priorState };
        await repository.UpsertAsync(connection, CancellationToken.None);
        var client = new FakeMcpClient();

        var refreshed = await new ExternalConnectionRegistryService(repository, client).RefreshMcpAsync(connection, CancellationToken.None);

        Assert.Equal(expectedState, client.ConnectionStateDuringDiscovery);
        Assert.Equal(ExternalConnectionState.Ready, refreshed.State);
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

    [Fact]
    public async Task RemoveRevokesInFlightCredentialLeaseBeforeSecureTokenCleanup()
    {
        var repository = new MemoryConnectionRepository();
        var secrets = new MemorySecretStore();
        var connection = Connection();
        var providerId = ExternalConnectionNaming.SecretProviderId(connection.Id);
        await repository.UpsertAsync(connection, CancellationToken.None);
        await secrets.SetAsync(providerId, ExternalConnectionNaming.OAuthTokenSecretName, "synthetic-token-fixture", CancellationToken.None);
        var service = new ExternalConnectionRegistryService(repository, new FakeMcpClient(), secrets);
        var activeCredentialOperation = await ExternalConnectionCredentialLifecycle.AcquireAsync(secrets, providerId, CancellationToken.None);
        using var cancellation = new CancellationTokenSource();
        repository.AfterDelete = cancellation.Cancel;

        var removal = service.RemoveAsync(connection.Id, cancellation.Token);

        Assert.True(activeCredentialOperation.IsRevoked);
        Assert.False(removal.IsCompleted);
        activeCredentialOperation.Dispose();
        await removal;

        Assert.Null(await repository.GetAsync(connection.Id, CancellationToken.None));
        Assert.Null(await secrets.GetAsync(providerId, ExternalConnectionNaming.OAuthTokenSecretName, CancellationToken.None));
        using var lateOperation = await ExternalConnectionCredentialLifecycle.AcquireAsync(secrets, providerId, CancellationToken.None);
        Assert.True(lateOperation.IsRevoked);
    }

    [Fact]
    public async Task CancelledRemovalAfterCanonicalDeleteStillCleansOAuthCredential()
    {
        var repository = new MemoryConnectionRepository { AfterDeleteFailure = new OperationCanceledException("cancelled after commit") };
        var secrets = new MemorySecretStore();
        var connection = Connection();
        var providerId = ExternalConnectionNaming.SecretProviderId(connection.Id);
        await repository.UpsertAsync(connection, CancellationToken.None);
        await secrets.SetAsync(providerId, ExternalConnectionNaming.OAuthTokenSecretName, "synthetic-token-fixture", CancellationToken.None);
        var service = new ExternalConnectionRegistryService(repository, new FakeMcpClient(), secrets);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.RemoveAsync(connection.Id, CancellationToken.None));

        Assert.Null(await repository.GetAsync(connection.Id, CancellationToken.None));
        Assert.Null(await secrets.GetAsync(providerId, ExternalConnectionNaming.OAuthTokenSecretName, CancellationToken.None));
        using var lateOperation = await ExternalConnectionCredentialLifecycle.AcquireAsync(secrets, providerId, CancellationToken.None);
        Assert.True(lateOperation.IsRevoked);
    }

    [Fact]
    public async Task CancelledRemovalThatLeavesCanonicalRecordKeepsCredentialUsable()
    {
        var repository = new MemoryConnectionRepository
        {
            AfterDeleteFailure = new OperationCanceledException("cancelled before commit"),
            RetainRecordOnDeleteFailure = true
        };
        var secrets = new MemorySecretStore();
        var connection = Connection();
        var providerId = ExternalConnectionNaming.SecretProviderId(connection.Id);
        await repository.UpsertAsync(connection, CancellationToken.None);
        await secrets.SetAsync(providerId, ExternalConnectionNaming.OAuthTokenSecretName, "synthetic-token-fixture", CancellationToken.None);
        var service = new ExternalConnectionRegistryService(repository, new FakeMcpClient(), secrets);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.RemoveAsync(connection.Id, CancellationToken.None));

        Assert.Equal(connection, await repository.GetAsync(connection.Id, CancellationToken.None));
        Assert.Equal("synthetic-token-fixture", await secrets.GetAsync(providerId, ExternalConnectionNaming.OAuthTokenSecretName, CancellationToken.None));
        using var activeOperation = await ExternalConnectionCredentialLifecycle.AcquireAsync(secrets, providerId, CancellationToken.None);
        Assert.False(activeOperation.IsRevoked);
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

    private sealed class OversizedTools : IReadOnlyList<McpExternalTool>
    {
        public int Count => McpCapabilitySnapshotSerializer.MaximumCollectionItems + 1;
        public int EnumerationAttempts { get; private set; }
        public McpExternalTool this[int index] => throw new InvalidOperationException("Oversized provider collection must not be indexed.");

        public IEnumerator<McpExternalTool> GetEnumerator()
        {
            EnumerationAttempts++;
            throw new InvalidOperationException("Oversized provider collection must not be enumerated.");
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private sealed class CancellationTriggeredTools(Action onCount) : IReadOnlyList<McpExternalTool>
    {
        public int Count { get { onCount(); return 1; } }
        public McpExternalTool this[int index] => new("read_item", "Read item", Element("{}"));
        public IEnumerator<McpExternalTool> GetEnumerator() => ((IEnumerable<McpExternalTool>)new[] { this[0] }).GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private sealed class MemoryConnectionRepository : IExternalConnectionRepository
    {
        private readonly Dictionary<Guid, ExternalConnection> _items = [];
        public Action? AfterDelete { get; set; }
        public Exception? AfterDeleteFailure { get; set; }
        public bool RetainRecordOnDeleteFailure { get; set; }
        public bool SupportsGuardedPublication { get; set; }
        public ExternalConnectionState? CancelOnReplacementState { get; set; }
        public ExternalConnectionState? CancelAfterPublishState { get; set; }
        public Task<IReadOnlyList<ExternalConnection>> GetAllAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<ExternalConnection>>(_items.Values.ToArray());
        public Task<ExternalConnection?> GetAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult(_items.GetValueOrDefault(id));
        public Task UpsertAsync(ExternalConnection connection, CancellationToken cancellationToken) { _items[connection.Id] = connection; return Task.CompletedTask; }
        public Task<bool> CompareExchangeAsync(ExternalConnection? expected, ExternalConnection replacement, CancellationToken cancellationToken)
        {
            if (CancelOnReplacementState == replacement.State)
            {
                CancelOnReplacementState = null;
                return Task.FromException<bool>(new OperationCanceledException("cancelled while publishing final refresh state"));
            }
            var current = _items.GetValueOrDefault(replacement.Id);
            if (current != expected) return Task.FromResult(false);
            _items[replacement.Id] = replacement;
            if (CancelAfterPublishState == replacement.State)
            {
                CancelAfterPublishState = null;
                return Task.FromException<bool>(new OperationCanceledException("cancelled after publishing connection state"));
            }
            return Task.FromResult(true);
        }
        public Task DeleteAsync(Guid id, CancellationToken cancellationToken)
        {
            if (AfterDeleteFailure is null || !RetainRecordOnDeleteFailure) _items.Remove(id);
            AfterDelete?.Invoke();
            return AfterDeleteFailure is null ? Task.CompletedTask : Task.FromException(AfterDeleteFailure);
        }
    }

    private sealed class MemorySecretStore : IProviderSecretStore
    {
        private readonly Dictionary<(string ProviderId, string SecretName), string> _items = [];

        public Task SetAsync(string providerId, string secretName, string secret, CancellationToken cancellationToken)
        {
            _items[(providerId, secretName)] = secret;
            return Task.CompletedTask;
        }

        public Task<string?> GetAsync(string providerId, string secretName, CancellationToken cancellationToken) =>
            Task.FromResult(_items.GetValueOrDefault((providerId, secretName)));

        public Task DeleteAsync(string providerId, string secretName, CancellationToken cancellationToken)
        {
            _items.Remove((providerId, secretName));
            return Task.CompletedTask;
        }
    }

    private sealed class FakeMcpClient(McpServerDiscovery? discovery = null, Exception? exception = null) : IMcpConnectionClient
    {
        public int DiscoveryCalls { get; private set; }
        public Exception? Exception { get; set; } = exception;
        public McpServerDiscovery? Discovery { get; set; } = discovery;
        public ExternalConnectionState? ConnectionStateDuringDiscovery { get; private set; }

        public Task<(McpServerIdentity Identity, IReadOnlyList<McpExternalTool> Tools)> DiscoverAsync(ExternalConnection connection, CancellationToken cancellationToken)
        {
            var identity = Discovery?.Identity ?? new McpServerIdentity("server", "1", "v", "{}");
            IReadOnlyList<McpExternalTool> tools = Discovery?.Tools ?? Array.Empty<McpExternalTool>();
            return Task.FromResult((identity, tools));
        }

        public Task<McpServerDiscovery> DiscoverCapabilitiesAsync(ExternalConnection connection, CancellationToken cancellationToken)
        {
            DiscoveryCalls++;
            ConnectionStateDuringDiscovery = connection.State;
            if (Exception is not null) return Task.FromException<McpServerDiscovery>(Exception);
            return Task.FromResult(Discovery ?? new McpServerDiscovery(new McpServerIdentity("server", "1", "v", "{}"), [], null, null, new string('c', 64)));
        }

        public Task<McpToolInvocationResult> InvokeAsync(ExternalConnection connection, string toolName, IReadOnlyDictionary<string, JsonElement> arguments, CancellationToken cancellationToken) =>
            Task.FromResult(new McpToolInvocationResult(true, "ok", null, "[]"));
    }
}
