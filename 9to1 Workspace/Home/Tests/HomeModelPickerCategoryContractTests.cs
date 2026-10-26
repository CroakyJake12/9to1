using Dulche.Runtime;
using Haven.Application;
using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;
using Xunit;

namespace HavenOS.Home.Tests;

public sealed class HomeModelPickerCategoryContractTests
{
    [Fact]
    public async Task Real_home_snapshot_opens_with_desktop_category_spelling_and_other_identities_still_fail_closed()
    {
        var directory = Path.Combine(Path.GetTempPath(), "home-picker-contract-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var store = new FileHomeCoreStateStore(Path.Combine(directory, "home.json"));
            var profiles = new HomeLocalProfileIdentity(store, new OperatingSystemPrincipalSource());
            var routes = new HomeVersionedModelRouteRepository(store);
            var permissions = new HomePermissionTrustService(store, new HomeModelRouteActionPolicies().TryGet);
            var resources = new ResourceAuthorizationService(profiles,
                [new HomeModelRouteProfileOwner(profiles), new HomeModelRouteOwner(profiles, routes)]);
            var provider = new HomeModelPickerFeatureProvider(profiles, routes,
                new ModelProviderRegistry([]), new LocalPrivacyPreferences(), resources,
                new HomeResourceOperationBroker(resources, permissions));

            // This is the production provider's real authorized result. Its enum serialization is
            // "Chat", while the desktop passes "chat" to OpenAsync.
            var snapshotResult = await provider.GetSnapshotAsync("User", "chat");
            Assert.True(snapshotResult.Succeeded);
            Assert.Equal("User", snapshotResult.Value!.Scope);
            Assert.Equal("Chat", snapshotResult.Value.Category);

            var editor = new HomeModelPickerRouteEditor(provider);
            var opened = await editor.RefreshAsync("User", "chat");
            Assert.True(opened.Succeeded);
            Assert.Equal("Chat", editor.Current.Snapshot!.Category);
            Assert.Equal(snapshotResult.Value.Routes.Single().RouteId, editor.Current.SelectedRouteId);

            // Hold the genuine snapshot constant while exercising adversarial mismatches at the
            // editor boundary. Scope/category normalization must not accept a different identity.
            var staleSnapshotProvider = new SnapshotProvider(snapshotResult.Value);
            var mismatchEditor = new HomeModelPickerRouteEditor(staleSnapshotProvider);
            var differentScope = await mismatchEditor.RefreshAsync("App", "chat");
            Assert.False(differentScope.Succeeded);
            Assert.Equal("InvalidProviderResult", differentScope.Code);
            Assert.Null(mismatchEditor.Current.Snapshot);

            var differentCategory = await mismatchEditor.RefreshAsync("User", "image");
            Assert.False(differentCategory.Succeeded);
            Assert.Equal("InvalidProviderResult", differentCategory.Code);
            Assert.Null(mismatchEditor.Current.Snapshot);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private sealed class LocalPrivacyPreferences : IPrivacyPreferenceStore
    {
        public PrivacyPreferences Current { get; private set; } = PrivacyPreferences.Default;
        public Task UpdateAsync(PrivacyPreferences preferences, CancellationToken cancellationToken)
        {
            Current = preferences;
            return Task.CompletedTask;
        }
    }

    private sealed class SnapshotProvider(HomeModelPickerSnapshot snapshot) : IHomeModelPickerFeatureProvider
    {
        public Task<HomeCoreOperationResult<HomeModelPickerSnapshot>> GetSnapshotAsync(string scope, string category,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new HomeCoreOperationResult<HomeModelPickerSnapshot>(true, "Ready", "Retained snapshot.", snapshot));

        public Task<HomeCoreOperationResult<HomeModelCataloguePage>> GetCatalogueAsync(string? query = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new HomeCoreOperationResult<HomeModelCataloguePage>(false, "Unavailable", "Not used."));

        public Task<HomeCoreOperationResult<HomeModelPickerSnapshot>> UpdateRouteAsync(HomeModelRouteEdit edit,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new HomeCoreOperationResult<HomeModelPickerSnapshot>(false, "Unavailable", "Not used."));

        public Task<HomeCoreOperationResult<HomeModelRoutePreview>> PreviewResolutionAsync(HomeModelRoutePreviewRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new HomeCoreOperationResult<HomeModelRoutePreview>(false, "Unavailable", "Not used."));
    }
}
