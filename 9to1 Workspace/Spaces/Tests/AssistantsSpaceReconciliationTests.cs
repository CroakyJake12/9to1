using System.Text.Json;
using Haven.Application;
using Haven.Core;
using Xunit;

namespace HavenOS.Apps.Spaces.Tests;

public sealed class AssistantsSpaceReconciliationTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task New_assistants_seed_preserves_legacy_agent_and_same_named_user_space_after_reopen(int priorVersion)
    {
        var directory = Path.Combine(Path.GetTempPath(), "astra-spaces-reconcile-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var paths = new TestPaths(directory);
            var store = new VersionedAtomicSettingsStore(paths);
            var registry = new SpaceRegistry(store);
            var agent = (await registry.GetAsync(SpaceRegistry.AgentSpaceId))!;
            agent = await registry.UpdateAsync(agent with { ModelName = "preserved-provider-model" }, agent.Revision);
            var custom = await registry.CreateAsync("Existing user Space", "Retain this user's work");
            custom = custom with { Name = "Assistants", Instructions = "User-owned instructions", Revision = 7 };
            var oldSpaces = (await registry.GetAllAsync(includeArchived: true))
                .Where(space => space.Id != SpaceRegistry.AssistantsSpaceId)
                .Select(space => space.Id == custom.Id ? custom : space).ToArray();
            await store.SetAsync("spaces.registry", new { Version = priorVersion, Spaces = oldSpaces }, CancellationToken.None);
            await store.SetAsync("spaces.current", SpaceRegistry.AgentSpaceId.ToString(), CancellationToken.None);

            var reconciled = new SpaceRegistry(new VersionedAtomicSettingsStore(paths));
            var rows = await reconciled.GetAllAsync();
            var assistants = Assert.Single(rows, space => space.Id == SpaceRegistry.AssistantsSpaceId);
            Assert.Equal(SpaceKind.Assistants, assistants.Kind);
            Assert.True(assistants.IsBuiltIn);
            Assert.Equal("Assistants", assistants.Name);
            Assert.Equal(JsonSerializer.Serialize(agent), JsonSerializer.Serialize(Assert.Single(rows, space => space.Id == agent.Id)));
            Assert.Equal(JsonSerializer.Serialize(custom), JsonSerializer.Serialize(Assert.Single(rows, space => space.Id == custom.Id)));
            Assert.Equal(SpaceRegistry.AgentSpaceId, await reconciled.GetCurrentSpaceIdAsync());

            var fresh = new SpaceRegistry(new VersionedAtomicSettingsStore(paths));
            Assert.Equal(rows.Count, (await fresh.GetAllAsync()).Count);
            var edited = await fresh.UpdateAsync(custom with { Description = "Still editable" }, custom.Revision);
            Assert.Equal(custom.Id, edited.Id);
            Assert.Equal("Assistants", edited.Name);
            Assert.Equal("User-owned instructions", edited.Instructions);
            Assert.Equal(8, edited.Revision);
            await Assert.ThrowsAsync<InvalidOperationException>(() => fresh.CreateAsync("Assistants"));
            Assert.Equal("Still editable", (await new SpaceRegistry(new VersionedAtomicSettingsStore(paths)).GetAsync(custom.Id))!.Description);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private sealed record TestPaths(string DataDirectory) : IAppPaths
    {
        public string DatabasePath => Path.Combine(DataDirectory, "unused-test.db");
        public string BrowserProfileDirectory => Path.Combine(DataDirectory, "browser");
        public string AttachmentsDirectory => Path.Combine(DataDirectory, "attachments");
        public string LogsDirectory => Path.Combine(DataDirectory, "logs");
        public string LegacyStatePath => Path.Combine(DataDirectory, "unused-legacy.json");
    }
}
