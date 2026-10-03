using Haven.Application;
using Haven.Core;
using Haven.Infrastructure;
using Microsoft.Data.Sqlite;

namespace Haven.Infrastructure.Tests;

public sealed class ExternalConnectionPublicationTests : IDisposable
{
    private readonly Paths _paths = new();

    [Theory]
    [InlineData("removed", "success")]
    [InlineData("disabled", "success")]
    [InlineData("reconfigured", "success")]
    [InlineData("removed", "failure")]
    [InlineData("disabled", "failure")]
    [InlineData("reconfigured", "failure")]
    [InlineData("removed", "cancelled")]
    [InlineData("disabled", "cancelled")]
    [InlineData("reconfigured", "cancelled")]
    public async Task LateDiscoveryCannotRestoreRemovedDisabledOrReconfiguredConnection(string change, string outcome)
    {
        var database = new SqliteDatabase(_paths);
        await database.InitializeAsync(CancellationToken.None);
        var repository = new ExternalConnectionRepository(database);
        var current = Connection();
        await repository.UpsertAsync(current, CancellationToken.None);
        var client = new PausedClient();
        var refresh = new ExternalConnectionRegistryService(repository, client).RefreshMcpAsync(current, CancellationToken.None);
        await client.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var checking = Assert.IsType<ExternalConnection>(await repository.GetAsync(current.Id, CancellationToken.None));
        ExternalConnection? changed = null;
        if (change == "removed") await repository.DeleteAsync(current.Id, CancellationToken.None);
        else
        {
            // Keep the exact timestamp: publication compares the record, not only time.
            changed = change == "disabled" ? checking with { IsEnabled = false } : checking with { ConfigurationJson = "{\"endpoint\":\"https://changed.example/mcp\"}" };
            await repository.UpsertAsync(changed, CancellationToken.None);
        }
        if (outcome == "success") client.Result.SetResult(new(new("observed", "1", "v", "{}"), [], null, null, "version"));
        else if (outcome == "failure") client.Result.SetException(new HttpRequestException("private-provider-message"));
        else client.Result.SetException(new OperationCanceledException("controlled cancellation"));
        if (outcome == "cancelled") await Assert.ThrowsAnyAsync<OperationCanceledException>(() => refresh);
        else
        {
            var result = await refresh;
            if (changed is null) Assert.Equal(ExternalConnectionState.Disconnected, result.State);
            else Assert.Equal(changed, result);
        }
        Assert.Equal(changed, await repository.GetAsync(current.Id, CancellationToken.None));
        Assert.Equal(1, client.Calls);
    }

    [Fact]
    public async Task IndependentRepositoryRejectsStaleRecordEvenWhenTimestampMatches()
    {
        var database = new SqliteDatabase(_paths);
        await database.InitializeAsync(CancellationToken.None);
        var first = new ExternalConnectionRepository(database);
        var second = new ExternalConnectionRepository(new SqliteDatabase(_paths));
        var original = Connection();
        Assert.True(await first.CompareExchangeAsync(null, original, CancellationToken.None));
        var changed = original with { IsEnabled = false, CapabilitySnapshotVersion = "new-version" };
        Assert.True(await second.CompareExchangeAsync(original, changed, CancellationToken.None));
        Assert.False(await first.CompareExchangeAsync(original, original with { State = ExternalConnectionState.Ready }, CancellationToken.None));
        Assert.False(await first.CompareExchangeAsync(null, original, CancellationToken.None));
        Assert.Equal(changed, await second.GetAsync(original.Id, CancellationToken.None));
        await second.DeleteAsync(original.Id, CancellationToken.None);
        Assert.False(await first.CompareExchangeAsync(changed, original, CancellationToken.None));
        Assert.Null(await first.GetAsync(original.Id, CancellationToken.None));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CredentialCleanupFailureCannotLeaveCanonicalConnectionActive(bool cancellation)
    {
        var database = new SqliteDatabase(_paths);
        await database.InitializeAsync(CancellationToken.None);
        var repository = new ExternalConnectionRepository(database);
        var connection = Connection();
        await repository.UpsertAsync(connection, CancellationToken.None);
        var secrets = new FailedCleanup(cancellation);
        var service = new ExternalConnectionRegistryService(repository, new PausedClient(), secrets);
        if (cancellation) await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.RemoveAsync(connection.Id, CancellationToken.None));
        else await Assert.ThrowsAsync<IOException>(() => service.RemoveAsync(connection.Id, CancellationToken.None));
        Assert.Equal(ExternalConnectionNaming.SecretProviderId(connection.Id), secrets.AttemptedProvider);
        Assert.Null(await repository.GetAsync(connection.Id, CancellationToken.None));
        Assert.False(await repository.CompareExchangeAsync(connection, connection with { State = ExternalConnectionState.Ready }, CancellationToken.None));
    }

    private sealed class FailedCleanup(bool cancellation) : IProviderSecretStore
    {
        public string? AttemptedProvider { get; private set; }
        public Task SetAsync(string providerId, string secretName, string secret, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<string?> GetAsync(string providerId, string secretName, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task DeleteAsync(string providerId, string secretName, CancellationToken cancellationToken)
        {
            AttemptedProvider = providerId;
            Assert.Equal(ExternalConnectionNaming.OAuthTokenSecretName, secretName);
            return Task.FromException(cancellation ? new OperationCanceledException("controlled cleanup cancellation") : new IOException("controlled cleanup failure"));
        }
    }

    private static ExternalConnection Connection()
    {
        var now = DateTimeOffset.UtcNow;
        return new(Guid.NewGuid(), "Controlled connection", "mcp.custom", ExternalConnectionKind.Mcp, "custom-mcp", true,
            ExternalConnectionState.Connecting, "Pending", "{}", null, null, null, now, now);
    }

    private sealed class PausedClient : IMcpConnectionClient
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<McpServerDiscovery> Result { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Calls { get; private set; }
        public Task<McpServerDiscovery> DiscoverCapabilitiesAsync(ExternalConnection connection, CancellationToken cancellationToken)
        { Calls++; Entered.TrySetResult(); return Result.Task; }
        public Task<(McpServerIdentity Identity, IReadOnlyList<McpExternalTool> Tools)> DiscoverAsync(ExternalConnection connection, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<McpToolInvocationResult> InvokeAsync(ExternalConnection connection, string toolName, IReadOnlyDictionary<string, System.Text.Json.JsonElement> arguments, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    public void Dispose() { SqliteConnection.ClearAllPools(); Directory.Delete(_paths.DataDirectory, true); }
    private sealed class Paths : IAppPaths
    {
        public Paths() { Directory.CreateDirectory(DataDirectory); }
        public string DataDirectory { get; } = Path.Combine(Path.GetTempPath(), "haven-external-publication-" + Guid.NewGuid().ToString("N"));
        public string DatabasePath => Path.Combine(DataDirectory, "test.db");
        public string BrowserProfileDirectory => Path.Combine(DataDirectory, "browser");
        public string AttachmentsDirectory => Path.Combine(DataDirectory, "attachments");
        public string LogsDirectory => Path.Combine(DataDirectory, "logs");
        public string LegacyStatePath => Path.Combine(DataDirectory, "missing.json");
    }
}
