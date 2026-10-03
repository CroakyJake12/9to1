using Haven.Application;
using Haven.Core;
using HavenOS.Home.Core;
using NineToOne.Cui.AI;
using Xunit;

namespace HavenOS.Home.Tests;

public sealed class HomeInvocationCatalogueTests
{
    [Fact]
    public async Task Actual_catalogue_requires_canonical_operability_and_rejects_game_anticheat_unknown()
    {
        var known = BuiltInModeSeed.Modes.First(mode => mode.Key == "write");
        var unknown = known with { Id = Guid.NewGuid(), Key = "future-app", Name = "Future app",
            InvocationOperability = new(AppOperabilityClassification.OrdinaryApplication, AppOperabilityPath.TypedApi) };
        var source = new Resources();
        var catalogue = new HomeInvocationCatalogue(new Modes([known, unknown]), new Extensions(), [source]);
        var visible = await catalogue.SearchAsync("", default);
        Assert.Contains(visible, item => item.CanonicalId == known.Id.ToString("D"));
        Assert.DoesNotContain(visible, item => item.CanonicalId == unknown.Id.ToString("D"));
        Assert.DoesNotContain(visible, item => item.CanonicalId is "game" or "anti-cheat" or "unknown");
        Assert.Contains(visible, item => item.CanonicalId == "native-app");
        var compose = new InvocationCompose();
        compose.Insert(InvocationCompose.ComputerUse, 0);
        compose.Insert(visible.Single(item => item.CanonicalId == "native-app"), compose.Text.Length);
        await catalogue.ResolveAsync(compose.Tokens, default);
        source.NativeEligible = false;
        await Assert.ThrowsAsync<InvalidOperationException>(() => catalogue.ResolveAsync(compose.Tokens, default).AsTask());
    }

    private sealed class Resources : IHomeInvocationResourceSource
    {
        public bool NativeEligible { get; set; } = true;
        public ValueTask<IReadOnlyList<InvocationResource>> SearchAsync(string query, CancellationToken cancellationToken) =>
            ValueTask.FromResult<IReadOnlyList<InvocationResource>>([
                new(InvocationKind.App, "game", "Ordinary looking name", "1", InteractionPath: AppInteractionPath.ComputerUseRequired, Classification: AppClassification.Game),
                new(InvocationKind.App, "anti-cheat", "Editor", "1", InteractionPath: AppInteractionPath.TypedApi, Classification: AppClassification.AntiCheatProtected),
                new(InvocationKind.App, "unknown", "Safe looking name", "1", InteractionPath: AppInteractionPath.TypedApi),
                new(InvocationKind.App, "native-app", "Native app", "1", InteractionPath: AppInteractionPath.ComputerUseRequired,
                    Classification: NativeEligible ? AppClassification.OrdinaryApplication : AppClassification.Game)]);
    }
    private sealed class Modes(IReadOnlyList<ModeDefinition> modes) : IModeRegistry
    {
        public Task<IReadOnlyList<ModeDefinition>> GetModesAsync(CancellationToken ct) => Task.FromResult(modes);
        public Task<ModeDefinition?> GetModeByKeyAsync(string key, CancellationToken ct) => Task.FromResult(modes.FirstOrDefault(mode => mode.Key == key));
        public Task<ModeDefinition?> GetModeByIdAsync(Guid id, CancellationToken ct) => Task.FromResult(modes.FirstOrDefault(mode => mode.Id == id));
        public Task UpsertModeAsync(ModeDefinition mode, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<ModeVersion>> GetVersionsAsync(Guid id, CancellationToken ct) => Task.FromResult<IReadOnlyList<ModeVersion>>([]);
        public Task AddVersionAsync(ModeVersion version, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<ModePermissionGrant>> GetGrantsAsync(Guid id, CancellationToken ct) => Task.FromResult<IReadOnlyList<ModePermissionGrant>>([]);
        public Task UpsertGrantAsync(ModePermissionGrant grant, CancellationToken ct) => throw new NotSupportedException();
    }
    private sealed class Extensions : IExtensionRepository
    {
        public Task<IReadOnlyList<ExtensionSource>> GetSourcesAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<ExtensionSource>>([]);
        public Task UpsertSourceAsync(ExtensionSource source, CancellationToken ct) => throw new NotSupportedException();
        public Task DeleteSourceAsync(Guid id, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<InstalledExtensionPackage>> GetInstalledAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<InstalledExtensionPackage>>([]);
        public Task UpsertInstalledAsync(InstalledExtensionPackage package, CancellationToken ct) => throw new NotSupportedException();
        public Task DeleteInstalledAsync(Guid id, CancellationToken ct) => throw new NotSupportedException();
    }
}
