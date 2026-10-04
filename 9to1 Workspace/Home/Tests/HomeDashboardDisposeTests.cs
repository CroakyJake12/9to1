using HavenOS.Home;
using Xunit;

namespace HavenOS.Home.Tests;

public sealed class HomeDashboardDisposeTests
{
    [Fact]
    public async Task Dispose_during_refresh_preserves_the_backend_outcome()
    {
        var inventory = AvailableInventory();
        var backend = new InFlightBackend(inventory, blockRefresh: true);
        var dashboard = new HomeDashboard(backend);

        var refresh = dashboard.RefreshAsync();
        await backend.RefreshStarted.Task;
        dashboard.Dispose();
        backend.RefreshResult.SetResult(inventory);

        var snapshot = await refresh;

        Assert.Equal(HomeSectionState.Available, snapshot.Updates.Status.State);
        Assert.Equal(HomeOperationState.Idle, snapshot.LastOperation.State);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => dashboard.RefreshAsync());
    }

    [Fact]
    public async Task Dispose_during_install_preserves_the_backend_outcome()
    {
        var inventory = AvailableInventory();
        var backend = new InFlightBackend(inventory);
        var dashboard = new HomeDashboard(backend);
        await dashboard.RefreshAsync();

        var install = dashboard.InstallAllAsync();
        await backend.InstallStarted.Task;
        dashboard.Dispose();
        backend.InstallResult.SetResult(new HomePackageOperationResult(
            new HomeOperationStatus(HomeOperationState.Succeeded, "Install completed.")));

        var snapshot = await install;

        Assert.Equal(HomeOperationState.Succeeded, snapshot.LastOperation.State);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => dashboard.InstallAllAsync());
    }

    private static HomePackageInventory AvailableInventory() => new(
        new HomeCatalogSection([], HomeSectionStatus.Available("Catalog ready.")),
        new HomeInstalledAppsSection([], HomeSectionStatus.Available("Installed apps ready.")),
        new HomeUpdatesSection(
            [new HomeUpdate("app.test", "Test", "1.0", "1.1")],
            HomeSectionStatus.Available("Updates ready.")));

    private sealed class InFlightBackend(HomePackageInventory inventory, bool blockRefresh = false) : IHomePackageBackend
    {
        public TaskCompletionSource RefreshStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<HomePackageInventory> RefreshResult { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource InstallStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<HomePackageOperationResult> InstallResult { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<HomePackageInventory> GetInventoryAsync(CancellationToken cancellationToken)
        {
            if (!blockRefresh) return Task.FromResult(inventory);
            RefreshStarted.TrySetResult();
            return RefreshResult.Task;
        }

        public Task<HomePackageOperationResult> InstallAllAsync(
            IReadOnlyList<string> packageIds,
            CancellationToken cancellationToken)
        {
            InstallStarted.TrySetResult();
            return InstallResult.Task;
        }
    }
}
