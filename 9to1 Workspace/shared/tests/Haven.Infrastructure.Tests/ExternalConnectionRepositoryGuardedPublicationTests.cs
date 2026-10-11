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
}

