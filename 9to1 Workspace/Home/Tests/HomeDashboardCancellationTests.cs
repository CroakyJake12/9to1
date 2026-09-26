using HavenOS.Home;
using Xunit;

namespace HavenOS.Home.Tests;

public sealed class HomeDashboardCancellationTests
{
    [Fact]
    public async Task Refresh_does_not_publish_a_late_result_after_cancellation()
    {
        var inventory = AvailableInventory();
        var backend = new CancellationIgnoringBackend(inventory);
        using var dashboard = new HomeDashboard(backend);
        var baseline = await dashboard.RefreshAsync();
        backend.BlockNextRefresh = true;
        using var cancellation = new CancellationTokenSource();

        var refresh = dashboard.RefreshAsync(cancellation.Token);
        await backend.RefreshStarted.Task;
        cancellation.Cancel();
        backend.RefreshResult.SetResult(inventory with
        {
            Updates = new HomeUpdatesSection([], HomeSectionStatus.Available("Late response.")),
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => refresh);
        Assert.Same(baseline, dashboard.Current);
    }

    [Fact]
    public async Task Install_publishes_observed_success_when_cancellation_races_with_completion()
    {
        var inventory = AvailableInventory();
        var backend = new CancellationIgnoringBackend(inventory);
        using var dashboard = new HomeDashboard(backend);
        var baseline = await dashboard.RefreshAsync();
        using var cancellation = new CancellationTokenSource();

        var install = dashboard.InstallAllAsync(cancellation.Token);
        await backend.InstallStarted.Task;
        cancellation.Cancel();
        backend.InstallResult.SetResult(new HomePackageOperationResult(
            new HomeOperationStatus(HomeOperationState.Succeeded, "Late install result."),
            inventory with { Updates = new HomeUpdatesSection([], HomeSectionStatus.Available("Late response.")) }));

        var observed = await install;
        Assert.Equal(HomeOperationState.Succeeded, observed.LastOperation.State);
        Assert.Empty(observed.Updates.Items);
        Assert.Same(observed, dashboard.Current);
        Assert.NotSame(baseline, dashboard.Current);
    }

    private static HomePackageInventory AvailableInventory() => new(
        new HomeCatalogSection([], HomeSectionStatus.Available("Catalog ready.")),
        new HomeInstalledAppsSection([], HomeSectionStatus.Available("Installed apps ready.")),
        new HomeUpdatesSection(
            [new HomeUpdate("app.test", "Test", "1.0", "1.1")],
            HomeSectionStatus.Available("Updates ready.")));

    private sealed class CancellationIgnoringBackend(HomePackageInventory inventory) : IHomePackageBackend
    {
        public bool BlockNextRefresh { get; set; }
        public TaskCompletionSource RefreshStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<HomePackageInventory> RefreshResult { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource InstallStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<HomePackageOperationResult> InstallResult { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<HomePackageInventory> GetInventoryAsync(CancellationToken cancellationToken)
        {
            if (!BlockNextRefresh) return Task.FromResult(inventory);
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
