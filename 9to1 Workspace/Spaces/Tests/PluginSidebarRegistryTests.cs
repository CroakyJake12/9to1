using Haven.Application;
using Xunit;

namespace HavenOS.Apps.Spaces.Tests;

public sealed class PluginSidebarRegistryTests
{
    [Fact]
    public async Task Visibility_is_space_scoped_persistent_and_permission_checked_at_launch()
    {
        var store = new MemoryStore();
        var authority = new Authority();
        var firstSpace = Guid.NewGuid();
        var secondSpace = Guid.NewGuid();
        var registry = new PluginSidebarRegistry(store, authority);
        Assert.Empty(await registry.GetVisibleAsync(firstSpace));
        var setting = await registry.SetVisibilityAsync(firstSpace, "study", "homework", true, 0);
        Assert.Equal(1, setting.Revision);
        var restarted = new PluginSidebarRegistry(store, authority);
        Assert.Single(await restarted.GetVisibleAsync(firstSpace));
        Assert.Empty(await restarted.GetVisibleAsync(secondSpace));
        await restarted.OpenAsync(firstSpace, "study", "homework");
        Assert.Equal(1, authority.Opens);
        authority.Allowed = false;
        Assert.Empty(await restarted.GetVisibleAsync(firstSpace));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => restarted.OpenAsync(firstSpace, "study", "homework"));
        Assert.Equal(1, authority.Opens);
    }

    [Fact]
    public async Task Stable_identity_deduplicates_updates_and_rejects_stale_or_undeclared_changes()
    {
        var store = new MemoryStore();
        var authority = new Authority();
        var registry = new PluginSidebarRegistry(store, authority);
        var second = new PluginSidebarRegistry(store, authority);
        var space = Guid.NewGuid();
        await registry.SetVisibilityAsync(space, "study", "homework", true, 0);
        await Assert.ThrowsAsync<InvalidOperationException>(() => second.SetVisibilityAsync(space, "study", "homework", false, 0));
        await second.SetVisibilityAsync(space, "study", "homework", false, 1);
        Assert.Empty(await registry.GetVisibleAsync(space));
        await Assert.ThrowsAsync<InvalidOperationException>(() => registry.SetVisibilityAsync(space, "study", "unknown", true, 0));
        authority.Manage = false;
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => registry.SetVisibilityAsync(space, "study", "homework", true, 2));
    }

    private sealed class Authority : IPluginSidebarAuthority
    {
        public bool Allowed = true;
        public bool Manage = true;
        public int Opens;
        public Task<IReadOnlyList<PluginSidebarContribution>> GetDeclaredAsync(Guid spaceId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<PluginSidebarContribution>>([new("study", "homework", "My Homework", "book", PluginSidebarTargetKind.AppSurface, "planner.homework")]);
        public Task<bool> MayManageAsync(Guid spaceId, CancellationToken cancellationToken) => Task.FromResult(Manage);
        public Task<bool> MayOpenAsync(Guid spaceId, PluginSidebarContribution contribution, CancellationToken cancellationToken) => Task.FromResult(Allowed);
        public Task OpenAsync(Guid spaceId, PluginSidebarContribution contribution, CancellationToken cancellationToken)
        {
            if (!Allowed) throw new UnauthorizedAccessException();
            Opens++;
            return Task.CompletedTask;
        }
    }

    private sealed class MemoryStore : IVersionedSettingsStore
    {
        private readonly Dictionary<string, object> _values = [];
        public Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken) where T : class => Task.FromResult(_values.TryGetValue(key, out var value) ? (T)value : null);
        public Task SetAsync<T>(string key, T value, CancellationToken cancellationToken) where T : class { _values[key] = value; return Task.CompletedTask; }
        public Task RemoveAsync(string key, CancellationToken cancellationToken) { _values.Remove(key); return Task.CompletedTask; }
        public Task<SettingsExportManifest> ExportAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<SettingsImportResult> ImportAsync(SettingsExportManifest manifest, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
