using Dulche.Runtime;
using Haven.Application;
using Haven.Core;
using HavenOS.Home;
using NineToOne.Dulche.Den;
using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;
using Xunit;

namespace HavenOS.Home.Tests;

public sealed class HomeModelPickerMetadataProviderTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PrincipalSwitchAfterMetadataReadStopsBeforeAnyLaterLookup(bool sourceThrows)
    {
        var directory = Path.Combine(Path.GetTempPath(), "home-catalogue-profile-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var store = new FileHomeCoreStateStore(Path.Combine(directory, "home.json"));
        var principal = new MutablePrincipal();
        var profiles = new HomeLocalProfileIdentity(store, principal);
        var routes = new HomeVersionedModelRouteRepository(store);
        var permissions = new HomePermissionTrustService(store, new HomeModelRouteActionPolicies().TryGet);
        var authorization = new ResourceAuthorizationService(profiles,
            [new HomeModelRouteProfileOwner(profiles), new HomeModelRouteOwner(profiles, routes)]);
        var registry = new Registry();
        var metadata = new SwitchingSource(principal, sourceThrows);
        var provider = new HomeModelPickerFeatureProvider(profiles, routes, registry, new Privacy(), authorization,
            new HomeResourceOperationBroker(authorization, permissions), metadata);

        var result = await provider.GetCatalogueAsync();

        Assert.False(result.Succeeded);
        Assert.Null(result.Value);
        Assert.Single(metadata.Reads);
        Assert.Equal("first", metadata.Reads[0].ModelId);
        Assert.NotNull(registry.Policy);
        Assert.False(registry.Policy.AllowRemote);
        // The second model was enumerated by the real provider path but never
        // disclosed to the metadata owner after the principal changed.
        Assert.DoesNotContain(metadata.Reads, identity => identity.ModelId == "second");
    }

    [Theory]
    [InlineData(8589934592L, " Q4_K_M ", "8 GiB", "Reported quantization: Q4_K_M")]
    [InlineData(0L, "", "unknown", "Quantization unknown")]
    [InlineData(-12L, "   ", "unknown", "Quantization unknown")]
    [InlineData(1024L, "https://private.invalid/?token=secret", "<0.01 GiB", "Quantization unknown")]
    public async Task ActualProviderReportsReachPickerWithoutBecomingVerifiedFacts(
        long bytes, string quantisation, string sizeLabel, string quantLabel)
    {
        var directory = Path.Combine(Path.GetTempPath(), "home-provider-report-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var store = new FileHomeCoreStateStore(Path.Combine(directory, "home.json"));
        var profiles = new HomeLocalProfileIdentity(store, new MutablePrincipal());
        var routes = new HomeVersionedModelRouteRepository(store);
        var permissions = new HomePermissionTrustService(store, new HomeModelRouteActionPolicies().TryGet);
        var authorization = new ResourceAuthorizationService(profiles,
            [new HomeModelRouteProfileOwner(profiles), new HomeModelRouteOwner(profiles, routes)]);
        var registry = new Registry(bytes, quantisation);
        var provider = new HomeModelPickerFeatureProvider(profiles, routes, registry, new Privacy(), authorization,
            new HomeResourceOperationBroker(authorization, permissions)); // No metadata service or fake Den record.

        var result = await provider.GetCatalogueAsync();
        Assert.True(result.Succeeded);
        Assert.False(registry.Policy!.AllowRemote);
        var entry = result.Value!.Items[0];
        Assert.Null(entry.ArtifactRevision);
        Assert.Null(entry.Presentation);
        Assert.NotNull(entry.ReportedMetadata);
        Assert.Equal(bytes > 0 ? (long?)bytes : null, entry.ReportedMetadata.ReportedSizeBytes);
        var rich = HomeModelCatalogueEntry.From(entry);
        Assert.Equal(entry.ReportedMetadata, rich.ReportedMetadata);
        Assert.Equal(MetricAvailability.Empty, rich.StorageBytes.Availability);
        Assert.Equal(MetricAvailability.Empty, rich.HardwareMemoryBytes.Availability);
        Assert.Null(rich.ExternalModelId);
        var projection = new HomeModelPickerCuiProjection(HomeModelPickerEditorState.Initial, "User", "chat", catalogue: result.Value.Items);
        Assert.Empty(Assert.Single(projection.Families).Children);
        Assert.Equal(2, projection.UngroupedCatalogue.Count);
        var row = projection.UngroupedCatalogue[0];
        Assert.True(row.TryGetValue("ResourceSummary", out var size));
        Assert.Equal($"Reported model file: {sizeLabel} · Required memory: unknown", size);
        Assert.True(row.TryGetValue("Quantisation", out var quant));
        Assert.Equal(quantLabel, quant);
        Assert.True(row.TryGetValue("Selection", out var selection));
        Assert.Equal(new HomeModelPickerCatalogueSelection(entry.ProviderId, entry.ModelId, entry.ArtifactRevision), selection);
    }

    private sealed class MutablePrincipal : ITrustedHostPrincipalSource
    {
        public string Value { get; set; } = "fixture-principal-before";
        public ValueTask<string?> GetPrincipalAsync(CancellationToken cancellationToken) => ValueTask.FromResult<string?>(Value);
    }

    private sealed class SwitchingSource(MutablePrincipal principal, bool fail) : IHomeModelCatalogueMetadataSource
    {
        public List<ModelIdentity> Reads { get; } = [];
        public async Task<HomeModelCatalogueMetadataBinding?> FindAsync(AuthenticatedResourceActor actor,
            ModelIdentity exactProviderIdentity, CancellationToken cancellationToken)
        {
            Reads.Add(exactProviderIdentity);
            await Task.Yield();
            principal.Value = "fixture-principal-after";
            if (fail) throw new IOException("Metadata read failed after the profile switched.");
            return null;
        }
    }

    private sealed class Privacy : IPrivacyPreferenceStore
    {
        public PrivacyPreferences Current => PrivacyPreferences.Default with { LocalOnlyMode = true };
        public Task UpdateAsync(PrivacyPreferences preferences, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class Registry(long reportedSize = 0, string reportedQuantisation = "") : IModelProviderRegistry
    {
        public ModelCataloguePolicy? Policy { get; private set; }
        public IReadOnlyList<IModelProvider> Providers => [];
        public IModelProvider? Find(string id) => null;
        public IModelProvider GetRequired(string id) => throw new NotSupportedException();
        public Task<IReadOnlyList<ProviderModelDescriptor>> GetModelsAsync(CancellationToken cancellationToken) =>
            throw new InvalidOperationException("The privacy-aware overload is required.");
        public Task<IReadOnlyList<ProviderModelDescriptor>> GetModelsAsync(ModelCataloguePolicy policy, CancellationToken cancellationToken)
        {
            Policy = policy;
            IReadOnlyList<ProviderModelDescriptor> models = new[] { "first", "second" }.Select(name =>
                new ProviderModelDescriptor("fixture-provider", true,
                    new ModelDescriptor(name, reportedSize, "Qwen4-looking-family-is-not-identity", "", reportedQuantisation, new HashSet<ToolCapability>(), DateTimeOffset.UnixEpoch))).ToArray();
            return Task.FromResult(models);
        }
    }
}
