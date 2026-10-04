using HavenOS.Home;
using Xunit;

namespace HavenOS.Home.Tests;

public sealed class HomeDashboardRecoveryTests
{
    [Fact]
    public async Task Refresh_failure_keeps_last_observed_rows_but_marks_them_stale_and_blocks_install()
    {
        var backend = new RecoveringPackageBackend(AvailableInventory());
        using var dashboard = new HomeDashboard(backend);

        var fresh = await dashboard.RefreshAsync();
        var afterFailure = await dashboard.RefreshAsync();
        var installAttempt = await dashboard.InstallAllAsync();

        Assert.Equal(HomeSectionState.Available, fresh.Updates.Status.State);
        Assert.True(fresh.CanInstallAll);
        Assert.Equal(new[] { "app.write", "app.browse" },
            afterFailure.Catalog.Apps.Select(app => app.PackageId));
        Assert.Equal(new[] { "app.write" },
            afterFailure.InstalledApps.Apps.Select(app => app.PackageId));
        Assert.Equal(new[] { "app.write" },
            afterFailure.Updates.Items.Select(update => update.PackageId));
        Assert.Equal(HomeSectionState.Error, afterFailure.Catalog.Status.State);
        Assert.Equal(HomeSectionState.Error, afterFailure.InstalledApps.Status.State);
        Assert.Equal(HomeSectionState.Error, afterFailure.Updates.Status.State);
        Assert.Contains("stale", afterFailure.Updates.Status.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(HomeOperationState.Failed, afterFailure.LastOperation.State);
        Assert.False(afterFailure.CanInstallAll);
        Assert.Equal(HomeOperationState.Unavailable, installAttempt.LastOperation.State);
        Assert.Equal(0, backend.InstallRequests);
    }

    [Fact]
    public async Task Install_failure_preserves_rows_marks_state_stale_and_requires_refresh_before_retry()
    {
        var backend = new InstallFailureBackend(AvailableInventory());
        using var dashboard = new HomeDashboard(backend);

        var fresh = await dashboard.RefreshAsync();
        var afterFailure = await dashboard.InstallAllAsync();
        var retry = await dashboard.InstallAllAsync();
        var refreshed = await dashboard.RefreshAsync();

        Assert.True(fresh.CanInstallAll);
        Assert.Equal(HomeOperationState.Failed, afterFailure.LastOperation.State);
        Assert.Equal(new[] { "app.write" }, afterFailure.InstalledApps.Apps.Select(app => app.PackageId));
        Assert.Equal(new[] { "app.write" }, afterFailure.Updates.Items.Select(update => update.PackageId));
        Assert.Equal(HomeSectionState.Available, afterFailure.Catalog.Status.State);
        Assert.Equal(HomeSectionState.Error, afterFailure.InstalledApps.Status.State);
        Assert.Equal(HomeSectionState.Error, afterFailure.Updates.Status.State);
        Assert.Contains("stale", afterFailure.Updates.Status.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(afterFailure.CanInstallAll);
        Assert.Equal(HomeOperationState.Unavailable, retry.LastOperation.State);
        Assert.Equal(1, backend.InstallRequests);
        Assert.Equal(HomeSectionState.Available, refreshed.Updates.Status.State);
        Assert.True(refreshed.CanInstallAll);
    }
    private static HomePackageInventory AvailableInventory() => new(
        new HomeCatalogSection(
            [
                new HomeCatalogApp("app.write", "Write", "1.0", "Documents"),
                new HomeCatalogApp("app.browse", "Browse", "1.0", "Web"),
            ],
            HomeSectionStatus.Available("App catalogue refreshed.")),
        new HomeInstalledAppsSection(
            [new HomeInstalledApp("app.write", "Write", "1.0")],
            HomeSectionStatus.Available("Installed apps refreshed.")),
        new HomeUpdatesSection(
            [new HomeUpdate("app.write", "Write", "1.0", "1.1")],
            HomeSectionStatus.Available("Updates refreshed.")));

    private sealed class InstallFailureBackend(HomePackageInventory inventory) : IHomePackageBackend
    {
        public int InstallRequests { get; private set; }

        public Task<HomePackageInventory> GetInventoryAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(inventory);
        }

        public Task<HomePackageOperationResult> InstallAllAsync(
            IReadOnlyList<string> packageIds,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            InstallRequests++;
            return Task.FromException<HomePackageOperationResult>(
                new InvalidOperationException("service disconnected during install"));
        }
    }
    private sealed class RecoveringPackageBackend(HomePackageInventory inventory) : IHomePackageBackend
    {
        private int _refreshCount;
        public int InstallRequests { get; private set; }

        public Task<HomePackageInventory> GetInventoryAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Interlocked.Increment(ref _refreshCount) == 1)
                return Task.FromResult(inventory);
            return Task.FromException<HomePackageInventory>(new InvalidOperationException("service offline"));
        }

        public Task<HomePackageOperationResult> InstallAllAsync(
            IReadOnlyList<string> packageIds,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            InstallRequests++;
            return Task.FromResult(new HomePackageOperationResult(
                new HomeOperationStatus(HomeOperationState.Unavailable, "Not exercised by this test.")));
        }
    }
}
