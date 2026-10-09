using System.Reflection;
using Haven.Application;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
namespace Haven.Infrastructure.Tests;
public sealed partial class CanonicalDiResolutionTests08
{
    [Fact] public Task Original_Spaces_registry_resolves_once_against_same_configured_versioned_store() => InspectAsync(provider =>
    {
        var registry = provider.GetRequiredService<SpaceRegistry>();
        var store = provider.GetRequiredService<IVersionedSettingsStore>();
        Assert.Same(registry, provider.GetRequiredService<SpaceRegistry>());
        Assert.Same(store, provider.GetRequiredService<IVersionedSettingsStore>());
        var captured = typeof(SpaceRegistry).GetField("_settings", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(captured); Assert.Same(store, captured!.GetValue(registry));
        return Task.CompletedTask;
    });
}
