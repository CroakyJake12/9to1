using System.Text.Json;
using System.Text.Json.Serialization;
using Haven.Core;
using Xunit;

namespace Haven.Application.Tests;

public sealed class CapabilityConcretePlatformTests
{
    [Fact]
    public void Historical_platform_values_and_names_remain_stable_and_new_hosts_have_distinct_identities()
    {
        Assert.Equal(0, (int)CapabilityPlatform.None);
        Assert.Equal(1, (int)CapabilityPlatform.Windows); Assert.Equal(2, (int)CapabilityPlatform.Android);
        Assert.Equal(3, (int)CapabilityPlatform.All); Assert.Equal("All", ((CapabilityPlatform)3).ToString());
        Assert.Equal("3", JsonSerializer.Serialize(CapabilityPlatform.All));
        var options = new JsonSerializerOptions(); options.Converters.Add(new JsonStringEnumConverter<CapabilityPlatform>());
        Assert.Equal("\"All\"", JsonSerializer.Serialize(CapabilityPlatform.All, options));
        Assert.Equal(4, (int)CapabilityPlatform.Linux); Assert.Equal(8, (int)CapabilityPlatform.MacOS);
        Assert.Equal(16, (int)CapabilityPlatform.iOS); Assert.Equal(31, (int)CapabilityPlatform.AllSupported);
        Assert.False(CapabilityPlatform.All.HasFlag(CapabilityPlatform.Linux));
        Assert.False(CapabilityPlatform.All.HasFlag(CapabilityPlatform.MacOS));
        Assert.False(CapabilityPlatform.All.HasFlag(CapabilityPlatform.iOS));
    }

    [Theory]
    [InlineData(CapabilityPlatform.Windows)]
    [InlineData(CapabilityPlatform.Android)]
    [InlineData(CapabilityPlatform.Linux)]
    [InlineData(CapabilityPlatform.MacOS)]
    [InlineData(CapabilityPlatform.iOS)]
    public async Task Concrete_registry_filter_preserves_actual_platform_declarations(CapabilityPlatform platform)
    {
        var specific = Definition("actual.specific", platform);
        var historical = Definition("actual.historical", CapabilityPlatform.All);
        var declaredAll = Definition("actual.declared.all", CapabilityPlatform.AllSupported);
        var result = await new CapabilityRegistryService(new Repository([specific, historical, declaredAll]))
            .DiscoverAsync(platform, default);
        Assert.Contains(result, value => value.Id == specific.Id);
        Assert.Contains(result, value => value.Id == declaredAll.Id);
        Assert.Equal(platform is CapabilityPlatform.Windows or CapabilityPlatform.Android,
            result.Any(value => value.Id == historical.Id));
        Assert.All(result, value => Assert.True(value.Platforms.HasFlag(platform)));
    }

    [Theory]
    [InlineData(CapabilityPlatform.None)]
    [InlineData(CapabilityPlatform.All)]
    [InlineData(CapabilityPlatform.AllSupported)]
    [InlineData((CapabilityPlatform)64)]
    public async Task A_union_or_unknown_host_is_not_a_concrete_discovery_identity(CapabilityPlatform platform)
    {
        var repository = new Repository([]);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => new CapabilityRegistryService(repository).DiscoverAsync(platform, default));
        Assert.Equal(0, repository.Reads);
    }

    private static CapabilityDefinition Definition(string key, CapabilityPlatform platform) => new(Guid.NewGuid(), key,
        key, "synthetic declared support", "owner", "icon", "instructions", "owner.route", "[]", platform,
        CapabilityRiskClass.ReadOnly, CapabilityAvailability.Available, "[]", "synthetic", true, true, true, true, DateTimeOffset.UnixEpoch);
    private sealed class Repository(IReadOnlyList<CapabilityDefinition> values) : ICapabilityRepository
    {
        public int Reads { get; private set; }
        public Task<IReadOnlyList<CapabilityDefinition>> GetCapabilitiesAsync(CancellationToken ct)
        { ct.ThrowIfCancellationRequested(); ++Reads; return Task.FromResult(values); }
        public Task UpsertCapabilityAsync(CapabilityDefinition value, CancellationToken ct) => throw new NotSupportedException();
        public Task SetCapabilityEnabledAsync(Guid id, bool enabled, CancellationToken ct) => throw new NotSupportedException();
        public Task DeleteCustomCapabilityAsync(Guid id, CancellationToken ct) => throw new NotSupportedException();
    }
}
