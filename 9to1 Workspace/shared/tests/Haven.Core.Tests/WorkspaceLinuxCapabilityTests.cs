using System.Text.Json;
using Haven.Application;
using Haven.Core;
namespace Haven.Core.Tests;

public sealed class WorkspaceLinuxCapabilityTests
{
    [Fact]
    public void Persisted_platform_values_and_legacy_All_are_exact_and_do_not_gain_Linux()
    {
        Assert.Equal(1, (int)CapabilityPlatform.Windows); Assert.Equal(2, (int)CapabilityPlatform.Android);
        Assert.Equal(3, (int)CapabilityPlatform.All); Assert.Equal(4, (int)CapabilityPlatform.Linux);
        Assert.Equal("3", JsonSerializer.Serialize(CapabilityPlatform.All));
        Assert.Equal(CapabilityPlatform.All, JsonSerializer.Deserialize<CapabilityPlatform>("3"));
        Assert.False(CapabilityPlatform.All.HasFlag(CapabilityPlatform.Linux));
        var mask = CapabilityPlatform.Windows | CapabilityPlatform.Linux;
        Assert.Equal(5, (int)mask); Assert.Equal(mask, JsonSerializer.Deserialize<CapabilityPlatform>(JsonSerializer.Serialize(mask)));
        Assert.False(Enum.IsDefined(mask)); // Combined metadata is a valid mask, never a single host/legacy alias.
        Assert.Equal(mask, CapabilityRegistryCatalog.BuiltIns.Single(x => x.Key == "run-command").Platforms);
    }
    [Fact]
    public async Task Real_discovery_exposes_only_four_declared_workspace_implementations_on_Linux()
    {
        var registry = new CapabilityRegistryService(new ActualCatalogue());
        var linux = await registry.DiscoverAsync(CapabilityPlatform.Linux, default);
        Assert.Equal(new[] { "read-file", "run-command", "run-tests", "write-file" }, linux.Select(x => x.Key).OrderBy(x => x, StringComparer.Ordinal));
        Assert.All(linux, x => Assert.Equal("haven.workspace", x.ProviderId));
        Assert.DoesNotContain(linux, x => x.Key is "powershell" or "run-script" or "computer-device-use" or "browser-use");
        var windows = await registry.DiscoverAsync(CapabilityPlatform.Windows, default);
        Assert.Contains(windows, x => x.Key == "powershell"); Assert.Contains(windows, x => x.Key == "run-script");
        var android = await registry.DiscoverAsync(CapabilityPlatform.Android, default);
        Assert.DoesNotContain(android, x => x.Key is "run-command" or "run-tests");
    }
    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(5)]
    [InlineData(8)]
    public async Task Current_host_selection_refuses_none_legacyAll_combined_or_unknown_values(int value)
    {
        var registry = new CapabilityRegistryService(new ActualCatalogue());
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => registry.DiscoverAsync((CapabilityPlatform)value, default));
    }
    private sealed class ActualCatalogue : ICapabilityRepository
    {
        public Task<IReadOnlyList<CapabilityDefinition>> GetCapabilitiesAsync(CancellationToken token) => Task.FromResult(CapabilityRegistryCatalog.BuiltIns);
        public Task UpsertCapabilityAsync(CapabilityDefinition value,CancellationToken token)=>throw new NotSupportedException();
        public Task SetCapabilityEnabledAsync(Guid id,bool enabled,CancellationToken token)=>throw new NotSupportedException();
        public Task DeleteCustomCapabilityAsync(Guid id,CancellationToken token)=>throw new NotSupportedException();
    }
}
