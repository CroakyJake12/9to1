using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Haven.Application;
using Haven.Core;
using Haven.Infrastructure;

namespace Haven.Infrastructure.Tests;

/// <summary>
/// Protects conditional publication in the canonical connection store, which the registry service depends on.
/// </summary>
public sealed class ExternalConnectionRepositoryGuardedPublicationTests : IDisposable
{
    private readonly TestPaths _paths = new();

    [Fact]
    public async Task CanonicalStorePublishesOnlyWhileTheExpectedRecordStillMatches()
    {
        var database = new SqliteDatabase(_paths);
        await new ConversationProductionDatabase(database).InitializeAsync(CancellationToken.None);
        var repository = new ExternalConnectionRepository(database);
        var now = DateTimeOffset.UtcNow;
        var connection = new ExternalConnection(Guid.NewGuid(), "Remote MCP", "mcp.test", ExternalConnectionKind.Mcp, "custom-mcp", true,
            ExternalConnectionState.Ready, "Connected", "{}", null, null, null, now, now);

        Assert.True(repository.SupportsGuardedPublication);
        Assert.True(await repository.CompareExchangeAsync(null, connection, CancellationToken.None));

        Assert.False(await repository.CompareExchangeAsync(null, connection with { Status = "Duplicate publish" }, CancellationToken.None));
        Assert.Equal("Connected", (await repository.GetAsync(connection.Id, CancellationToken.None))?.Status);

        var stale = connection with { State = ExternalConnectionState.Degraded, Status = "Stale writer", UpdatedAt = now.AddMinutes(1) };
        Assert.False(await repository.CompareExchangeAsync(stale, connection with { Status = "Guarded publish" }, CancellationToken.None));
        Assert.Equal("Connected", (await repository.GetAsync(connection.Id, CancellationToken.None))?.Status);

        Assert.True(await repository.CompareExchangeAsync(connection, connection with { Status = "Guarded publish", UpdatedAt = DateTimeOffset.UtcNow }, CancellationToken.None));
        var published = await repository.GetAsync(connection.Id, CancellationToken.None);
        Assert.Equal("Guarded publish", published?.Status);
        Assert.Equal(ExternalConnectionState.Ready, published?.State);
    }

    [Fact]
    public async Task CapabilitySnapshotColumnsSurviveGuardedPublicationRoundTrip()
    {
        var database = new SqliteDatabase(_paths);
        await new ConversationProductionDatabase(database).InitializeAsync(CancellationToken.None);
        var repository = new ExternalConnectionRepository(database);
        var now = DateTimeOffset.UtcNow;
        var connection = new ExternalConnection(Guid.NewGuid(), "Snapshot MCP", "mcp.test", ExternalConnectionKind.Mcp, "custom-mcp", true,
            ExternalConnectionState.Ready, "Connected", "{}", "unreal-mcp", "1.0", "2026-07-28", now, now)
        {
            CapabilitySnapshotJson = "{\"tools\":[\"read_verse\"]}",
            CapabilitySnapshotVersion = new string('a', 64)
        };

        Assert.True(await repository.CompareExchangeAsync(null, connection, CancellationToken.None));
        var stored = await repository.GetAsync(connection.Id, CancellationToken.None);
        Assert.Equal("{\"tools\":[\"read_verse\"]}", stored?.CapabilitySnapshotJson);
        Assert.Equal(new string('a', 64), stored?.CapabilitySnapshotVersion);
        Assert.Equal(connection, stored);
    }

    [Fact]
    public async Task ReopenedStorePreservesExpiredCredentialAttentionStateAndStableIdentity()
    {
        var database = new SqliteDatabase(_paths);
        await new ConversationProductionDatabase(database).InitializeAsync(CancellationToken.None);
        var firstRepository = new ExternalConnectionRepository(database);
        var now = DateTimeOffset.UtcNow;
        var connection = new ExternalConnection(Guid.NewGuid(), "Remote MCP", "mcp.test", ExternalConnectionKind.Mcp, "custom-mcp", true,
            ExternalConnectionState.NeedsAttention, "MCP authentication is required. Reconnect the account and review granted scopes.", "{}",
            "remote-server", "1", "2025-06-18", now, now);

        Assert.True(await firstRepository.CompareExchangeAsync(null, connection, CancellationToken.None));

        // A fresh repository instance models app/service reconstruction without
        // touching the credential store or issuing a provider request.
        var reopenedRepository = new ExternalConnectionRepository(new SqliteDatabase(_paths));
        var reopened = await reopenedRepository.GetAsync(connection.Id, CancellationToken.None);

        Assert.NotNull(reopened);
        Assert.Equal(connection.Id, reopened.Id);
        Assert.Equal(connection.ServerId, reopened.ServerId);
        Assert.Equal(ExternalConnectionState.NeedsAttention, reopened.State);
        Assert.Equal(connection.Status, reopened.Status);
        Assert.Equal(connection.ConfigurationJson, reopened.ConfigurationJson);

        var reconnected = await new ExternalConnectionRegistryService(reopenedRepository, new RecoveringMcpClient())
            .RefreshMcpAsync(reopened, CancellationToken.None);
        Assert.Equal(connection.Id, reconnected.Id);
        Assert.Equal(ExternalConnectionState.Ready, reconnected.State);
    }

    [Fact]
    public async Task SuccessfulRegistryRefreshPersistsDiscoveredSnapshotAndVersion()
    {
        var database = new SqliteDatabase(_paths);
        await new ConversationProductionDatabase(database).InitializeAsync(CancellationToken.None);
        var repository = new ExternalConnectionRepository(database);
        var now = DateTimeOffset.UtcNow;
        var connection = new ExternalConnection(Guid.NewGuid(), "Remote MCP", "mcp.test", ExternalConnectionKind.Mcp, "custom-mcp", true,
            ExternalConnectionState.Offline, "Retry connection", "{}", null, null, null, now, now);
        Assert.True(await repository.CompareExchangeAsync(null, connection, CancellationToken.None));

        var refreshed = await new ExternalConnectionRegistryService(repository, new RecoveringMcpClient())
            .RefreshMcpAsync(connection, CancellationToken.None);
        var persisted = await repository.GetAsync(connection.Id, CancellationToken.None);

        Assert.Equal(ExternalConnectionState.Ready, refreshed.State);
        Assert.Equal(refreshed.CapabilitySnapshotJson, persisted?.CapabilitySnapshotJson);
        Assert.Equal(refreshed.CapabilitySnapshotVersion, persisted?.CapabilitySnapshotVersion);
        Assert.NotNull(persisted?.CapabilitySnapshotJson);
        Assert.False(string.IsNullOrWhiteSpace(persisted?.CapabilitySnapshotVersion));
        var expectedVersion = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(persisted!.CapabilitySnapshotJson!))).ToLowerInvariant();
        Assert.Equal(expectedVersion, persisted.CapabilitySnapshotVersion);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("{not-json")]
    public async Task OAuthCacheRemovesMalformedOrNullTokenEnvelopes(string cachedValue)
    {
        var id = Guid.NewGuid();
        var secretStore = new FakeSecretStore(cachedValue);
        var cache = new McpOAuthTokenCache(secretStore, id);

        var tokens = await cache.GetTokensAsync(CancellationToken.None);

        Assert.Null(tokens);
        Assert.Equal((ExternalConnectionNaming.SecretProviderId(id), ExternalConnectionNaming.OAuthTokenSecretName), secretStore.Deleted);
    }

    [Fact]
    public async Task RevokedConnectionDoesNotReadOAuthTokensFromTheStore()
    {
        var id = Guid.NewGuid();
        var secretStore = new FakeSecretStore("sensitive-value-is-only-a-test-fixture");
        var providerId = ExternalConnectionNaming.SecretProviderId(id);
        ExternalConnectionCredentialLifecycle.Revoke(secretStore, providerId);
        var cache = new McpOAuthTokenCache(secretStore, id);

        var tokens = await cache.GetTokensAsync(CancellationToken.None);

        Assert.Null(tokens);
        Assert.Equal(0, secretStore.ReadCalls);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            cache.StoreTokenEnvelopeAsync(() => "synthetic-revoked-payload", CancellationToken.None).AsTask());
        Assert.Equal(0, secretStore.WriteCalls);
    }

    [Fact]
    public async Task RevokingOneConnectionDoesNotRevokeAnotherConnectionsCredentialScope()
    {
        var secretStore = new FakeSecretStore("null");
        var revokedId = Guid.NewGuid();
        var activeId = Guid.NewGuid();
        ExternalConnectionCredentialLifecycle.Revoke(secretStore, ExternalConnectionNaming.SecretProviderId(revokedId));
        var revokedCache = new McpOAuthTokenCache(secretStore, revokedId);
        var activeCache = new McpOAuthTokenCache(secretStore, activeId);

        Assert.Null(await revokedCache.GetTokensAsync(CancellationToken.None));
        Assert.Null(await activeCache.GetTokensAsync(CancellationToken.None)); // Synthetic `null` envelope is read and discarded.

        Assert.Equal(1, secretStore.ReadCalls);
        Assert.Equal((ExternalConnectionNaming.SecretProviderId(activeId), ExternalConnectionNaming.OAuthTokenSecretName), secretStore.Deleted);
    }

    [Fact]
    public async Task OAuthCacheSerializesSameStoreObjectReadCleanupAgainstConcurrentDeletion()
    {
        var secretStore = new BlockingSecretStore();
        var connectionId = Guid.NewGuid();
        var cache = new McpOAuthTokenCache(secretStore, connectionId);
        var parallelClientCache = new McpOAuthTokenCache(secretStore, connectionId);
        var read = cache.GetTokensAsync(CancellationToken.None).AsTask();
        await secretStore.ReadStarted.Task;

        var delete = parallelClientCache.DeleteAsync(CancellationToken.None);
        Assert.False(secretStore.DeleteStarted.Task.IsCompleted);

        secretStore.ReleaseRead.TrySetResult(true);
        await read;
        await delete;

        Assert.Equal(1, secretStore.MaximumConcurrentOperations);
    }

    [Fact]
    public async Task MalformedTokenCleanupCannotEraseRefreshWrittenByAnotherCacheAdapter()
    {
        var secretStore = new BlockingSecretStore();
        var connectionId = Guid.NewGuid();
        var firstCache = new McpOAuthTokenCache(secretStore, connectionId);
        var refreshCache = new McpOAuthTokenCache(secretStore, connectionId);
        var read = firstCache.GetTokensAsync(CancellationToken.None).AsTask();
        await secretStore.ReadStarted.Task;

        const string syntheticRefreshPayload = "synthetic-refresh-payload-v2";
        var write = refreshCache.StoreTokenEnvelopeAsync(() => syntheticRefreshPayload, CancellationToken.None).AsTask();
        Assert.False(secretStore.WriteStarted.Task.IsCompleted);

        secretStore.ReleaseRead.TrySetResult(true);
        await read;
        Assert.True(secretStore.DeleteStarted.Task.IsCompleted);
        await write;

        Assert.Equal(syntheticRefreshPayload, secretStore.Value);
        Assert.Equal(1, secretStore.MaximumConcurrentOperations);
    }

    [Fact]
    public async Task OAuthCacheWritesAndReadsBackSyntheticSerializedPayloadAtStoreBoundary()
    {
        var connectionId = Guid.NewGuid();
        var secretStore = new FakeSecretStore("previous-synthetic-payload");
        var cache = new McpOAuthTokenCache(secretStore, connectionId);
        // Opaque test payload only: this does not assert ModelContextProtocol.TokenContainer semantics.
        const string payload = "synthetic-sdk-serialized-payload";

        await cache.StoreTokenEnvelopeAsync(() => payload, CancellationToken.None);
        var stored = await secretStore.GetAsync(ExternalConnectionNaming.SecretProviderId(connectionId),
            ExternalConnectionNaming.OAuthTokenSecretName, CancellationToken.None);

        Assert.Equal(payload, stored);
    }

    [Fact]
    public async Task OAuthCacheKeepsDifferentConnectionIdsInDistinctSecretKeys()
    {
        var secretStore = new FakeSecretStore("unused-synthetic-default");
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();
        var firstCache = new McpOAuthTokenCache(secretStore, firstId);
        var secondCache = new McpOAuthTokenCache(secretStore, secondId);

        await firstCache.StoreTokenEnvelopeAsync(() => "synthetic-payload-first", CancellationToken.None);
        await secondCache.StoreTokenEnvelopeAsync(() => "synthetic-payload-second", CancellationToken.None);

        Assert.Equal("synthetic-payload-first", await secretStore.GetAsync(ExternalConnectionNaming.SecretProviderId(firstId),
            ExternalConnectionNaming.OAuthTokenSecretName, CancellationToken.None));
        Assert.Equal("synthetic-payload-second", await secretStore.GetAsync(ExternalConnectionNaming.SecretProviderId(secondId),
            ExternalConnectionNaming.OAuthTokenSecretName, CancellationToken.None));
        Assert.Contains((ExternalConnectionNaming.SecretProviderId(firstId), ExternalConnectionNaming.OAuthTokenSecretName), secretStore.Written);
        Assert.Contains((ExternalConnectionNaming.SecretProviderId(secondId), ExternalConnectionNaming.OAuthTokenSecretName), secretStore.Written);
    }

    [Fact]
    public async Task CancelledOAuthCacheReadReleasesGateForRefreshWrite()
    {
        var secretStore = new BlockingSecretStore();
        var connectionId = Guid.NewGuid();
        var cache = new McpOAuthTokenCache(secretStore, connectionId);
        var parallelClientCache = new McpOAuthTokenCache(secretStore, connectionId);
        using var cancellation = new CancellationTokenSource();
        var read = cache.GetTokensAsync(cancellation.Token).AsTask();
        await secretStore.ReadStarted.Task;
        const string syntheticRefreshPayload = "synthetic-refresh-payload-after-cancel";
        var write = parallelClientCache.StoreTokenEnvelopeAsync(() => syntheticRefreshPayload, CancellationToken.None).AsTask();

        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await read);
        await write;

        Assert.True(secretStore.WriteStarted.Task.IsCompleted);
        Assert.Equal(syntheticRefreshPayload, secretStore.Value);
        Assert.Equal(1, secretStore.MaximumConcurrentOperations);
    }

    [Fact]
    public void OAuthCallbackListenerMatchesExactRedirectPathWithOrWithoutTrailingSlash()
    {
        var redirectWithoutSlash = new Uri("http://127.0.0.1:52117/mcp-oauth");
        var redirectWithSlash = new Uri("http://127.0.0.1:52117/mcp-oauth/");

        Assert.Equal("http://127.0.0.1:52117/", McpOAuthBrowserAuthorization.Prefix(redirectWithoutSlash));
        Assert.Equal("http://127.0.0.1:52117/mcp-oauth/", McpOAuthBrowserAuthorization.Prefix(redirectWithSlash));
        Assert.Equal("http://127.0.0.1:52117/mcp-oauth/", McpOAuthBrowserAuthorization.Prefix(new Uri("http://127.0.0.1:52117/mcp-oauth/?tenant=school#fragment")));
        Assert.True(McpOAuthBrowserAuthorization.MatchesRedirectPath(new Uri("http://127.0.0.1:52117/mcp-oauth?code=sample&state=sample"), redirectWithoutSlash));
        Assert.True(McpOAuthBrowserAuthorization.MatchesRedirectPath(new Uri("http://127.0.0.1:52117/mcp-oauth/"), redirectWithSlash));
        Assert.False(McpOAuthBrowserAuthorization.MatchesRedirectPath(new Uri("http://127.0.0.1:52117/other"), redirectWithoutSlash));
        Assert.False(McpOAuthBrowserAuthorization.MatchesRedirectPath(new Uri("http://127.0.0.1:52117/mcp-oauth/extra"), redirectWithSlash));
        Assert.False(McpOAuthBrowserAuthorization.MatchesRedirectPath(new Uri("http://127.0.0.1:52118/mcp-oauth"), redirectWithoutSlash));
        Assert.True(McpOAuthBrowserAuthorization.HasCompleteAuthorizationResponse("code", "state"));
        Assert.False(McpOAuthBrowserAuthorization.HasCompleteAuthorizationResponse("code", null));
        Assert.False(McpOAuthBrowserAuthorization.HasCompleteAuthorizationResponse(null, "state"));
        Assert.True(McpOAuthBrowserAuthorization.IsSuccessfulResponse(null, "code", "state", cancelled: false));
        Assert.False(McpOAuthBrowserAuthorization.IsSuccessfulResponse("access_denied", null, "state", cancelled: false));
        Assert.False(McpOAuthBrowserAuthorization.IsSuccessfulResponse(null, "code", "state", cancelled: true));
        Assert.Contains("Authorization response received", McpOAuthBrowserAuthorization.ResponseText(success: true), StringComparison.Ordinal);
        Assert.DoesNotContain("Connected", McpOAuthBrowserAuthorization.ResponseText(success: true), StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("access_denied", "code", "state")]
    [InlineData(null, null, "state")]
    [InlineData(null, "code", null)]
    public async Task OAuthCallbackErrorsOrMissingFieldsUseFailureResponse(string? error, string? code, string? state)
    {
        var responseWasSuccessful = true;

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            McpOAuthBrowserAuthorization.CompleteAuthorizationResponseAsync(error, code, state, "issuer", CancellationToken.None,
                success => { responseWasSuccessful = success; return Task.CompletedTask; }));

        Assert.False(responseWasSuccessful);
        Assert.DoesNotContain("access_denied", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CancellationAfterOAuthCallbackPreservesCancellationAndResponseCleanupFailures()
    {
        using var cancellation = new CancellationTokenSource();
        var stalledWrite = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var closed = false;
        var aborted = false;
        var responseWasSuccessful = true;

        var failure = await Assert.ThrowsAsync<AggregateException>(() =>
            McpOAuthBrowserAuthorization.CompleteAuthorizationResponseAsync(null, "code", "state", "issuer", cancellation.Token,
                success =>
                {
                    responseWasSuccessful = success;
                    cancellation.Cancel(); // Cancellation arrives after the callback has been accepted.
                    return McpOAuthBrowserAuthorization.CompleteResponseAsync(
                        _ => stalledWrite.Task,
                        () => { closed = true; throw new IOException("close failed"); },
                        () => { aborted = true; stalledWrite.TrySetCanceled(); throw new InvalidOperationException("abort failed"); },
                        TimeSpan.FromMilliseconds(25));
                }));

        Assert.True(responseWasSuccessful);
        Assert.True(closed);
        Assert.True(aborted);
        Assert.IsType<OperationCanceledException>(failure.InnerExceptions[0]);
        var responseFailures = Assert.IsType<AggregateException>(failure.InnerExceptions[1]);
        Assert.Equal(3, responseFailures.InnerExceptions.Count);
        Assert.IsType<OperationCanceledException>(responseFailures.InnerExceptions[0]);
        Assert.IsType<IOException>(responseFailures.InnerExceptions[1]);
        Assert.IsType<InvalidOperationException>(responseFailures.InnerExceptions[2]);
    }

    [Fact]
    public async Task OAuthResponseWriteFailureStillClosesAndAborts()
    {
        var closed = false;
        var aborted = false;

        var failure = await Assert.ThrowsAsync<AggregateException>(() =>
            McpOAuthBrowserAuthorization.CompleteResponseAsync(
                _ => Task.FromException(new IOException("write failed")),
                () => closed = true,
                () => aborted = true,
                TimeSpan.FromSeconds(1)));

        Assert.True(closed);
        Assert.True(aborted);
        Assert.IsType<IOException>(Assert.Single(failure.InnerExceptions));
    }

    [Theory]
    [InlineData("close")]
    [InlineData("abort")]
    public async Task OAuthWriteFaultRaisedDuringCleanupIsRetainedAndObservedAfterTimeout(string faultPhase)
    {
        var pendingWrite = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        Task? lateFaultObservation = null;
        var closed = false;
        var aborted = false;

        var failure = await Assert.ThrowsAsync<AggregateException>(() =>
            McpOAuthBrowserAuthorization.CompleteResponseAsync(
                _ => pendingWrite.Task,
                () => { closed = true; if (faultPhase == "close") pendingWrite.TrySetException(new IOException("write faulted during close")); },
                () => { aborted = true; if (faultPhase == "abort") pendingWrite.TrySetException(new IOException("write faulted during abort")); },
                TimeSpan.FromMilliseconds(25),
                observation => lateFaultObservation = observation));

        Assert.True(closed);
        Assert.True(aborted);
        Assert.Equal(2, failure.InnerExceptions.Count);
        Assert.IsType<OperationCanceledException>(failure.InnerExceptions[0]);
        Assert.IsType<IOException>(failure.InnerExceptions[1]);
        var observation = Assert.IsType<Task>(lateFaultObservation);
        await observation;
        Assert.IsType<IOException>(pendingWrite.Task.Exception?.InnerException);
    }

    public void Dispose()
    {
        try { Directory.Delete(_paths.DataDirectory, true); } catch { }
    }

    private sealed class TestPaths : Haven.Application.IAppPaths, IDisposable
    {
        public TestPaths()
        {
            DataDirectory = Path.Combine(Path.GetTempPath(), "haven-connection-guarded-publication-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(DataDirectory);
            DatabasePath = Path.Combine(DataDirectory, "test.db");
            BrowserProfileDirectory = Path.Combine(DataDirectory, "browser");
            AttachmentsDirectory = Path.Combine(DataDirectory, "attachments");
            LogsDirectory = Path.Combine(DataDirectory, "logs");
            LegacyStatePath = Path.Combine(DataDirectory, "missing.json");
        }

        public string DataDirectory { get; }
        public string DatabasePath { get; }
        public string BrowserProfileDirectory { get; }
        public string AttachmentsDirectory { get; }
        public string LogsDirectory { get; }
        public string LegacyStatePath { get; }

        public void Dispose()
        {
            try { Directory.Delete(DataDirectory, true); } catch { }
        }
    }

    private sealed class RecoveringMcpClient : IMcpConnectionClient
    {
        public Task<(McpServerIdentity Identity, IReadOnlyList<McpExternalTool> Tools)> DiscoverAsync(ExternalConnection connection, CancellationToken cancellationToken) =>
            Task.FromResult((new McpServerIdentity("remote-server", "1", "2025-06-18", "{}"), (IReadOnlyList<McpExternalTool>)Array.Empty<McpExternalTool>()));

        public Task<McpToolInvocationResult> InvokeAsync(ExternalConnection connection, string toolName,
            IReadOnlyDictionary<string, JsonElement> arguments, CancellationToken cancellationToken) =>
            Task.FromResult(new McpToolInvocationResult(true, string.Empty, null, "[]"));
    }

    private sealed class FakeSecretStore(string value) : IProviderSecretStore
    {
        private readonly Dictionary<(string ProviderId, string SecretName), string> _values = [];
        public (string ProviderId, string SecretName)? Deleted { get; private set; }
        public int ReadCalls { get; private set; }
        public int WriteCalls { get; private set; }
        public List<(string ProviderId, string SecretName)> Written { get; } = [];

        public Task SetAsync(string providerId, string secretName, string secret, CancellationToken cancellationToken)
        {
            WriteCalls++;
            _values[(providerId, secretName)] = secret;
            Written.Add((providerId, secretName));
            return Task.CompletedTask;
        }

        public Task<string?> GetAsync(string providerId, string secretName, CancellationToken cancellationToken)
        {
            ReadCalls++;
            return Task.FromResult(_values.TryGetValue((providerId, secretName), out var stored) ? stored : value);
        }

        public Task DeleteAsync(string providerId, string secretName, CancellationToken cancellationToken)
        {
            Deleted = (providerId, secretName);
            _values.Remove((providerId, secretName));
            return Task.CompletedTask;
        }
    }

    private sealed class BlockingSecretStore : IProviderSecretStore
    {
        private int _activeOperations;
        private int _maximumConcurrentOperations;
        private string? _value = "null";
        public TaskCompletionSource<bool> ReadStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> ReleaseRead { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> DeleteStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> WriteStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int MaximumConcurrentOperations => Volatile.Read(ref _maximumConcurrentOperations);
        public string? Value => _value;

        public Task SetAsync(string providerId, string secretName, string secret, CancellationToken cancellationToken)
        {
            Enter();
            _value = secret;
            WriteStarted.TrySetResult(true);
            Exit();
            return Task.CompletedTask;
        }

        public async Task<string?> GetAsync(string providerId, string secretName, CancellationToken cancellationToken)
        {
            Enter();
            var valueAtReadStart = _value;
            ReadStarted.TrySetResult(true);
            try
            {
                await ReleaseRead.Task.WaitAsync(cancellationToken);
                return valueAtReadStart;
            }
            finally { Exit(); }
        }

        public Task DeleteAsync(string providerId, string secretName, CancellationToken cancellationToken)
        {
            Enter();
            DeleteStarted.TrySetResult(true);
            _value = null;
            Exit();
            return Task.CompletedTask;
        }

        private void Enter()
        {
            var active = Interlocked.Increment(ref _activeOperations);
            while (true)
            {
                var maximum = Volatile.Read(ref _maximumConcurrentOperations);
                if (active <= maximum || Interlocked.CompareExchange(ref _maximumConcurrentOperations, active, maximum) == maximum) break;
            }
        }

        private void Exit() => Interlocked.Decrement(ref _activeOperations);
    }
}
