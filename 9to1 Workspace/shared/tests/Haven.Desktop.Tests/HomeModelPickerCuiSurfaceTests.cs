using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Dulche.Runtime;
using Haven.Application;
using Haven.Core;
using HavenOS.Home.Core;
using HavenOS.Home.NativeUI;
using HavenOS.Home.PermissionsTrustNotifications;

namespace Haven.Desktop.Tests;

public sealed class HomeModelPickerCuiSurfaceTests
{
    [AvaloniaFact]
    public async Task Real_OS_profile_model_manager_requires_actual_Home_decision_before_same_route_save_retry()
    {
        var root = Path.Combine(Path.GetTempPath(), "astra-model-manager-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var token = TestContext.Current.CancellationToken;
            var store = new FileHomeCoreStateStore(Path.Combine(root, "home.json"));
            var profiles = new HomeLocalProfileIdentity(store, new OperatingSystemPrincipalSource());
            var actor = (await profiles.GetCurrentAsync(token))!;
            var routes = new HomeVersionedModelRouteRepository(store);
            var resources = new ResourceAuthorizationService(profiles,
                [new HomeModelRouteOwner(profiles, routes), new HomeModelRouteProfileOwner(profiles)]);
            var permissions = new HomePermissionTrustService(store, new HomeModelRouteActionPolicies().TryGet);
            var provider = new HomeModelPickerFeatureProvider(profiles, routes, new LocalCatalogue(), new Privacy(), resources,
                new HomeResourceOperationBroker(resources, permissions));
            await using var runtime = new HomeCoreRuntime([new HomeCoreStateService(store),
                new HomePermissionsCoreService(permissions, profiles), new HomeModelPickerCoreService(provider)]);
            var window = new Window { Width = 1000, Height = 900 };
            HomeApprovalCuiSurface? review = null;
            var shown = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            var closeReview = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            HomeModelPickerCuiSurface? manager = null;
            using var ownedManager = manager = new(runtime, profiles, provider, async (requestId, ct) =>
            {
                using var approvals = new HomeApprovalCuiSurface(runtime, profiles, permissions);
                await approvals.InitializeAsync(ct);
                Assert.True(await approvals.FocusRequestAsync(requestId, ct));
                Assert.Equal(HomePermissionRequestState.PendingApproval, (await permissions.GetAuthorizationAsync(requestId, ct)).State);
                review = approvals;
                window.Content = approvals;
                shown.TrySetResult(requestId);
                try { await closeReview.Task.WaitAsync(ct); }
                finally { review = null; window.Content = manager; }
            });
            window.Content = manager;
            await manager.InitializeAsync(token);
            window.Show();
            try
            {
                Assert.Empty(await routes.ListAsync(token));
                Click(manager, "Add to route");
                await Eventually(() => Task.FromResult(Button(manager, "Save route").IsEnabled), token);
                Click(manager, "Save route");
                await Eventually(async () => (await permissions.GetSnapshotAsync(cancellationToken: token)).PendingRequests.Count == 1, token);
                Assert.Empty(await routes.ListAsync(token));
                await Eventually(() => Task.FromResult(Button(manager, "Home permissions").IsEnabled), token);
                Click(manager, "Home permissions");
                var requestId = await shown.Task.WaitAsync(TimeSpan.FromSeconds(5), token);
                Assert.NotNull(review);
                Assert.Empty(await routes.ListAsync(token));
                Assert.Empty((await permissions.GetSnapshotAsync(cancellationToken: token)).Grants);
                Click(review, "Accept once");
                await Eventually(async () => (await permissions.GetAuthorizationAsync(requestId, token)).State == HomePermissionRequestState.Approved, token);
                Assert.Empty(await routes.ListAsync(token)); // The decision alone cannot persist a route.
                closeReview.TrySetResult();
                await Eventually(async () => (await routes.ListAsync(token)).Count == 1, token);
                var persisted = Assert.Single(await routes.ListAsync(token));
                Assert.Equal(actor.ProfileId, persisted.ScopeId);
                Assert.Equal("local", Assert.Single(persisted.Candidates).Model.ProviderId);
                Assert.Equal("text", Assert.Single(persisted.Candidates).Model.ModelId);
                Assert.Equal(1, persisted.Revision);
                Assert.Empty((await permissions.GetSnapshotAsync(cancellationToken: token)).Grants);
                var personal = new HomePersonalModelRoutes(profiles, routes, resources);
                Assert.Equal(persisted.RouteId, (await personal.GetAsync(ModelCapabilityCategory.Active, token))!.RouteId);
                Assert.False((await permissions.BeginExecutionAsync(requestId, token)).IsAllowed);
            }
            finally { closeReview.TrySetCanceled(); window.Close(); }
        }
        finally { Directory.Delete(root, true); }
    }

    private static Button Button(Control surface, string caption) =>
        Assert.Single(surface.GetVisualDescendants().OfType<Button>(), button => Equals(button.Content, caption));
    private static void Click(Control surface, string caption)
    {
        var button = Button(surface, caption);
        Assert.True(button.IsEnabled, caption);
        button.RaiseEvent(new RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
    }
    private static async Task Eventually(Func<Task<bool>> predicate, CancellationToken token)
    {
        for (var attempt = 0; attempt < 250; attempt++)
        {
            if (await predicate()) return;
            await Task.Delay(20, token);
        }
        Assert.Fail("The real native model/approval operation did not reach its expected state.");
    }
    private sealed class Privacy : IPrivacyPreferenceStore
    {
        public PrivacyPreferences Current { get; private set; } = PrivacyPreferences.Default with { LocalOnlyMode = true };
        public Task UpdateAsync(PrivacyPreferences preferences, CancellationToken cancellationToken)
        { Current = preferences; return Task.CompletedTask; }
    }
    private sealed class LocalCatalogue : IModelProviderRegistry
    {
        public IReadOnlyList<IModelProvider> Providers => [];
        public IModelProvider? Find(string providerId) => null;
        public IModelProvider GetRequired(string providerId) => throw new NotSupportedException();
        public Task<IReadOnlyList<ProviderModelDescriptor>> GetModelsAsync(CancellationToken cancellationToken)
            => throw new InvalidOperationException("The permission-filtered catalogue policy is required.");
        public Task<IReadOnlyList<ProviderModelDescriptor>> GetModelsAsync(ModelCataloguePolicy policy, CancellationToken cancellationToken)
        {
            Assert.True(policy.AllowLocal); Assert.False(policy.AllowRemote);
            return Task.FromResult<IReadOnlyList<ProviderModelDescriptor>>([new("local", true,
                new("text", 0, "controlled-local-catalogue", "unknown", "unknown", new HashSet<ToolCapability> { ToolCapability.Text }, DateTimeOffset.UnixEpoch))]);
        }
    }
}
