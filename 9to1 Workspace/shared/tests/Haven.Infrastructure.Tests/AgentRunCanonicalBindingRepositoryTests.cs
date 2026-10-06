using System.Text.Json;
using Haven.Application;
using Haven.Core;
using Haven.Infrastructure;
using Xunit;

namespace Haven.Infrastructure.Tests;

public sealed class AgentRunCanonicalBindingRepositoryTests : IDisposable
{
    private readonly BindingPaths _paths = new();

    [Fact]
    public async Task Canonical_display_binding_round_trip_preserves_legacy_array_entries_and_existing_ids()
    {
        var database = new SqliteDatabase(_paths);
        await database.InitializeAsync(default);
        var now = DateTimeOffset.UtcNow;
        var agent = new AgentDefinition(Guid.NewGuid(), "Binding fixture", "Actual SQLite metadata", "Observe", "agent",
            "model", null, "[]", "{}", false, true, now);
        await new CatalogRepository(database).UpsertAgentAsync(agent, default);
        var binding = new AgentRunCanonicalBinding(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 7,
            TaskExecutionLifecycle.Suspended);
        const string oldActivity = "[{\"title\":\"Original activity\",\"unknown\":{\"retained\":true}}]";
        var original = new AgentRun(Guid.NewGuid(), agent.Id, agent.Name, "Same task", AgentRunStatus.Suspended, "model",
            "partial", "waiting", "[]", oldActivity, now, now, null, ProgressPercent: 40)
            { CanonicalTask = binding };
        var repository = new AgentRunRepository(database);
        await repository.UpsertAsync(original, default);
        var stored = Assert.IsType<AgentRun>(await repository.GetAsync(original.Id, default));
        Assert.Equal(original.Id, stored.Id);
        Assert.Equal(binding, stored.CanonicalTask);
        Assert.Equal(AgentRunStatus.Suspended, stored.Status);
        Assert.Null(stored.CompletedAt);
        using var envelope = JsonDocument.Parse(stored.ActivityJson);
        Assert.Equal(1, envelope.RootElement.GetProperty("CanonicalBindingVersion").GetInt32());
        Assert.Equal("array", envelope.RootElement.GetProperty("LegacyActivitySchema").GetString());
        Assert.Equal(oldActivity, envelope.RootElement.GetProperty("Activities").GetRawText());
        Assert.Single(envelope.RootElement.GetProperty("Activities").EnumerateArray());
        Assert.False(envelope.RootElement.TryGetProperty("ObservationComplete", out _));
    }

    [Fact]
    public async Task Unbound_legacy_array_stays_exact_and_unknown_binding_version_is_unavailable_observation()
    {
        var database = new SqliteDatabase(_paths);
        await database.InitializeAsync(default);
        var now = DateTimeOffset.UtcNow;
        var agent = new AgentDefinition(Guid.NewGuid(), "Legacy fixture", "Actual SQLite", "Observe", "agent",
            "model", null, "[]", "{}", false, true, now);
        await new CatalogRepository(database).UpsertAgentAsync(agent, default);
        var legacy = new AgentRun(Guid.NewGuid(), agent.Id, agent.Name, "Original", AgentRunStatus.Failed,
            "model", "", "old error", "[]", "[{\"title\":\"Original\"}]", now, null, now);
        var repository = new AgentRunRepository(database);
        await repository.UpsertAsync(legacy, default);
        var stored = Assert.IsType<AgentRun>(await repository.GetAsync(legacy.Id, default));
        Assert.Equal(legacy.ActivityJson, stored.ActivityJson);
        Assert.Null(stored.CanonicalTask);
        var future = legacy with { ActivityJson = "{\"CanonicalBindingVersion\":99,\"CanonicalTask\":{}}" };
        await repository.UpsertAsync(future, default);
        var unknown = Assert.IsType<AgentRun>(await repository.GetAsync(legacy.Id, default));
        Assert.Null(unknown.CanonicalTask);
        Assert.Equal(future.ActivityJson, unknown.ActivityJson);
    }

    [Theory]
    [InlineData("\"one\"")]
    [InlineData("null")]
    [InlineData("true")]
    public async Task Malformed_binding_version_kind_keeps_existing_row_and_activity_readable(string literal)
    {
        var database = new SqliteDatabase(_paths);
        await database.InitializeAsync(default);
        var now = DateTimeOffset.UtcNow;
        var agent = new AgentDefinition(Guid.NewGuid(), "Malformed metadata fixture", "Actual SQLite", "Observe", "agent",
            "model", null, "[]", "{}", false, true, now);
        await new CatalogRepository(database).UpsertAgentAsync(agent, default);
        var json = "{\"CanonicalBindingVersion\":" + literal + ",\"CanonicalTask\":{},\"UnknownOriginal\":[1,2]}";
        var original = new AgentRun(Guid.NewGuid(), agent.Id, agent.Name, "Actual legacy task", AgentRunStatus.Failed,
            "model", "kept", "legacy", "[]", json, now, null, now);
        var repository = new AgentRunRepository(database);
        await repository.UpsertAsync(original, default);
        var stored = Assert.IsType<AgentRun>(await repository.GetAsync(original.Id, default));
        Assert.Null(stored.CanonicalTask);
        Assert.Equal(json, stored.ActivityJson);
        Assert.Equal(original.Id, stored.Id);
        Assert.Equal(original.Task, stored.Task);
        Assert.Null(Assert.Single(await repository.GetRecentAsync(10, default)).CanonicalTask);
    }

    public void Dispose() => _paths.Dispose();

    private sealed class BindingPaths : IAppPaths, IDisposable
    {
        public string DataDirectory { get; } = Path.Combine(Path.GetTempPath(), "astra-agent-binding-" + Guid.NewGuid().ToString("N"));
        public BindingPaths() { Directory.CreateDirectory(DataDirectory); }
        public string DatabasePath => Path.Combine(DataDirectory, "actual.db");
        public string BrowserProfileDirectory => Path.Combine(DataDirectory, "browser");
        public string AttachmentsDirectory => Path.Combine(DataDirectory, "attachments");
        public string LogsDirectory => Path.Combine(DataDirectory, "logs");
        public string LegacyStatePath => Path.Combine(DataDirectory, "missing.json");
        public void Dispose() => Directory.Delete(DataDirectory, true);
    }
}
