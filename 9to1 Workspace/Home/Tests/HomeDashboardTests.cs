using NineToOne.Cui.Markup;
using HavenOS.Home;
using Xunit;

namespace HavenOS.Home.Tests;

public sealed class HomeDashboardTests
{
    [Fact]
    public async Task Default_backend_is_observably_unavailable_and_cannot_install()
    {
        using var dashboard = new HomeDashboard();

        var refreshed = await dashboard.RefreshAsync();
        var installed = await dashboard.InstallAllAsync();

        Assert.Equal(HomeSectionState.Unavailable, refreshed.Catalog.Status.State);
        Assert.Equal(HomeSectionState.Unavailable, refreshed.InstalledApps.Status.State);
        Assert.Equal(HomeSectionState.Unavailable, refreshed.Updates.Status.State);
        Assert.False(refreshed.CanInstallAll);
        Assert.Equal(HomeOperationState.Unavailable, installed.LastOperation.State);
        Assert.Contains("not configured", installed.LastOperation.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Refresh_and_install_all_use_the_backend_and_only_observed_inventory()
    {
        var afterInstall = AvailableInventory(updates: []);
        var backend = new FakePackageBackend(AvailableInventory(), new HomePackageOperationResult(
            new HomeOperationStatus(HomeOperationState.Succeeded, "Backend confirmed two updates."),
            afterInstall));
        using var dashboard = new HomeDashboard(backend, ReadySettings(), ReadyRuntime());

        var refreshed = await dashboard.RefreshAsync();
        var installed = await dashboard.InstallAllAsync();

        Assert.Single(refreshed.Catalog.Apps);
        Assert.Single(refreshed.InstalledApps.Apps);
        Assert.Equal(2, refreshed.Updates.Items.Count);
        Assert.Equal(new[] { "haven.write", "haven.wave" }, backend.InstallRequests);
        Assert.Equal(HomeOperationState.Succeeded, installed.LastOperation.State);
        Assert.Empty(installed.Updates.Items);
        Assert.Equal(HomeCapabilityState.Ready, installed.Runtime.Model.State);
        Assert.True(installed.Settings.AutomaticUpdates);
    }

    [Fact]
    public async Task Backend_refresh_failure_is_exposed_as_an_error_without_claiming_inventory()
    {
        var backend = new FakePackageBackend(AvailableInventory(), null)
        {
            GetException = new InvalidOperationException("service is offline"),
        };
        using var dashboard = new HomeDashboard(backend);

        var snapshot = await dashboard.RefreshAsync();

        Assert.Equal(HomeSectionState.Error, snapshot.Catalog.Status.State);
        Assert.Equal(HomeSectionState.Error, snapshot.InstalledApps.Status.State);
        Assert.Equal(HomeSectionState.Error, snapshot.Updates.Status.State);
        Assert.Equal(HomeOperationState.Failed, snapshot.LastOperation.State);
        Assert.Contains("service is offline", snapshot.LastOperation.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Backend_install_failure_preserves_the_last_observed_updates()
    {
        var backend = new FakePackageBackend(AvailableInventory(), null);
        using var dashboard = new HomeDashboard(backend);
        await dashboard.RefreshAsync();

        var snapshot = await dashboard.InstallAllAsync();

        Assert.Equal(HomeOperationState.Failed, snapshot.LastOperation.State);
        Assert.Equal(2, snapshot.Updates.Items.Count);
        Assert.Contains("No install result", snapshot.LastOperation.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CUI_loads_the_authored_surface_and_routes_typed_intent_to_the_domain()
    {
        var backend = new FakePackageBackend(AvailableInventory(), new HomePackageOperationResult(
            new HomeOperationStatus(HomeOperationState.Succeeded, "Backend confirmed two updates."),
            AvailableInventory(updates: [])));
        using var dashboard = new HomeDashboard(backend, ReadySettings(), ReadyRuntime());
        var controller = new HomeCuiController(dashboard);

        await controller.RefreshAsync();
        Assert.Equal("Home.cui", Path.GetFileName(controller.Surface.Document.SourceName));
        Assert.All(
            new[] { "catalog", "installed", "updates", "settings", "runtime" },
            id => Assert.Contains(Descendants(controller.Surface.Document.Root), element => element.Attributes.TryGetValue("id", out var value) && value == id));
        Assert.True(controller.Surface.CanInstallAll);
        Assert.True(controller.Surface.RequestInstallAll());
        Assert.True(controller.Surface.TryDequeueAction(out var action));

        var installed = await controller.ExecuteAsync(action);

        Assert.Equal(HomeOperationState.Succeeded, installed.LastOperation.State);
        Assert.False(controller.Surface.CanInstallAll);
        Assert.Contains("Succeeded", controller.Surface.OperationStatus, StringComparison.Ordinal);
    }

    [Fact]
    public void CUI_exposes_unavailable_backend_state_and_disables_install_all()
    {
        using var dashboard = new HomeDashboard();
        var controller = new HomeCuiController(dashboard);

        controller.ShowCurrent();

        Assert.False(controller.Surface.CanInstallAll);
        Assert.False(controller.Surface.RequestInstallAll());
        Assert.Contains("not configured", controller.Surface.OperationStatus, StringComparison.OrdinalIgnoreCase);
    }

    private static HomePackageInventory AvailableInventory(IReadOnlyList<HomeUpdate>? updates = null) => new(
        new HomeCatalogSection(
            [new HomeCatalogApp("haven.write", "Write", "1.0", "Local documents")],
            HomeSectionStatus.Available("Catalog synchronized.")),
        new HomeInstalledAppsSection(
            [new HomeInstalledApp("haven.write", "Write", "1.0")],
            HomeSectionStatus.Available("Installed apps synchronized.")),
        new HomeUpdatesSection(
            updates ??
            [
                new HomeUpdate("haven.write", "Write", "1.0", "1.1"),
                new HomeUpdate("haven.wave", "Wave", "1.0", "1.1"),
            ],
            HomeSectionStatus.Available("Updates synchronized.")));

    private static HomeSettingsSection ReadySettings() => new(
        HomeSectionStatus.Available("Settings synchronized."),
        true,
        "stable");

    private static HomeRuntimeSection ReadyRuntime() => new(
        new HomeCapabilityStatus(HomeCapabilityState.Ready, "Runtime is running."),
        new HomeCapabilityStatus(HomeCapabilityState.Ready, "Model is loaded."),
        new HomeCapabilityStatus(HomeCapabilityState.Ready, "Voice is ready."));

    private sealed class FakePackageBackend(
        HomePackageInventory inventory,
        HomePackageOperationResult? installResult) : IHomePackageBackend
    {
        public List<string> InstallRequests { get; } = [];
        public Exception? GetException { get; init; }

        public Task<HomePackageInventory> GetInventoryAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (GetException is not null) throw GetException;
            return Task.FromResult(inventory);
        }

        public Task<HomePackageOperationResult> InstallAllAsync(
            IReadOnlyList<string> packageIds,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            InstallRequests.AddRange(packageIds);
            return Task.FromResult(installResult ?? throw new InvalidOperationException("No install result was configured."));
        }
    }

    private static IEnumerable<CuiElement> Descendants(CuiElement root)
    {
        yield return root;
        foreach (var child in root.Children)
        foreach (var descendant in Descendants(child))
            yield return descendant;
    }
}
