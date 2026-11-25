using HavenOS.Apps.Dev;
using Xunit;

namespace HavenOS.Apps.Dev.Tests;

/// <summary>ED-BUILD-LIVE-001..003 first slice: coalesced guarded build intent, stable revision ordering, current diagnostics, failure preserving
/// the last valid preview, manual/paused behaviour and concurrent-build prevention. No process execution is performed by the coordinator.</summary>
public sealed class DeveloperLiveReloadCoordinatorTests
{
    private static readonly Guid Project = Guid.NewGuid();
    private static readonly Guid Root = Guid.NewGuid();

    private static DeveloperLiveReloadConfiguration Config(int threshold = 3) => new(Project, "target-1", "local", "dotnet-sdk", "cui-adapter", Root, threshold, TimeSpan.FromMilliseconds(200), TimeSpan.FromSeconds(2));
    private static DeveloperLiveReloadChange Change(long revision, int units = 1) => new(revision, units);

    [Fact]
    public void BelowThresholdQueuesWithoutBuilding()
    {
        var coordinator = new DeveloperLiveReloadCoordinator(Config(threshold: 3));
        var intent = coordinator.RecordChange(Change(1));
        Assert.Equal(DeveloperLiveReloadDecision.Queued, intent.Decision);
        Assert.Equal(DeveloperLiveReloadStatus.Queued, coordinator.Status);
    }

    [Fact]
    public void CrossingThresholdCoalescesIntoOneBuildOfNewestRevision()
    {
        var coordinator = new DeveloperLiveReloadCoordinator(Config(threshold: 3));
        coordinator.RecordChange(Change(1));
        coordinator.RecordChange(Change(2));
        var intent = coordinator.RecordChange(Change(4, units: 1)); // threshold reached at revision 4

        Assert.Equal(DeveloperLiveReloadDecision.BuildNow, intent.Decision);
        Assert.Equal(4, intent.BuildRevision);
        Assert.Equal(4, coordinator.BuildingRevision);
    }

    [Fact]
    public void ConcurrentBuildIsPreventedAndCoalesces()
    {
        var coordinator = new DeveloperLiveReloadCoordinator(Config(threshold: 1));
        Assert.Equal(DeveloperLiveReloadDecision.BuildNow, coordinator.RecordChange(Change(1)).Decision);
        // A second change while a build is in flight must not start another build; it coalesces.
        var second = coordinator.RecordChange(Change(2));
        Assert.Equal(DeveloperLiveReloadDecision.Queued, second.Decision);
        Assert.Equal(2, coordinator.BuildingRevision ?? 2); // still building revision 1's slot until result applied
    }

    [Fact]
    public void StaleResultNeverOverwritesNewerState()
    {
        var coordinator = new DeveloperLiveReloadCoordinator(Config(threshold: 1));
        coordinator.RecordChange(Change(5)); // building revision 5
        // A late result for an older revision is ignored.
        coordinator.ApplyBuildResult(new DeveloperBuildResult(4, true, []));
        Assert.Equal(5, coordinator.BuildingRevision);
        Assert.Null(coordinator.LastRunningRevision);

        coordinator.ApplyBuildResult(new DeveloperBuildResult(5, true, []));
        Assert.Equal(5, coordinator.LastRunningRevision);
    }

    [Fact]
    public void FailurePreservesLastValidPreviewAndMarksStale()
    {
        var coordinator = new DeveloperLiveReloadCoordinator(Config(threshold: 1));
        coordinator.RecordChange(Change(1));
        coordinator.ApplyBuildResult(new DeveloperBuildResult(1, true, []));
        Assert.Equal(DeveloperLiveReloadStatus.Running, coordinator.Status);

        // A newer authored change then a failing build keeps the last valid preview and marks it stale.
        coordinator.RecordChange(Change(2));
        coordinator.ApplyBuildResult(new DeveloperBuildResult(2, false, [new DeveloperDiagnostic("cui", "error", "bad", "CS0001", null, null, 3, 1, 3, 9)]));

        Assert.Equal(DeveloperLiveReloadStatus.Failed, coordinator.Status);
        Assert.NotNull(coordinator.LastValidPreview);
        Assert.Equal(1, coordinator.LastValidPreview!.SourceRevision);
        Assert.True(coordinator.IsPreviewStale);
    }

    [Fact]
    public void ManualModeWaitsForExplicitBuild()
    {
        var coordinator = new DeveloperLiveReloadCoordinator(Config(threshold: 1)) { };
        coordinator.SetMode(DeveloperLiveReloadMode.Manual);
        Assert.Equal(DeveloperLiveReloadDecision.Queued, coordinator.RecordChange(Change(1)).Decision);

        var build = coordinator.BuildNow();
        Assert.Equal(DeveloperLiveReloadDecision.BuildNow, build.Decision);
        Assert.Equal(1, build.BuildRevision);
    }

    [Fact]
    public void PausedModeQueuesWithoutBuilding()
    {
        var coordinator = new DeveloperLiveReloadCoordinator(Config(threshold: 1));
        coordinator.SetMode(DeveloperLiveReloadMode.Paused);
        Assert.Equal(DeveloperLiveReloadDecision.Paused, coordinator.RecordChange(Change(1)).Decision);
        Assert.Equal(DeveloperLiveReloadStatus.Paused, coordinator.Status);
    }

    [Fact]
    public void CancellationRetainsLastValidPreview()
    {
        var coordinator = new DeveloperLiveReloadCoordinator(Config(threshold: 1));
        coordinator.RecordChange(Change(1));
        coordinator.ApplyBuildResult(new DeveloperBuildResult(1, true, []));
        coordinator.RecordChange(Change(2)); // building revision 2
        coordinator.CancelBuild();

        Assert.Null(coordinator.BuildingRevision);
        Assert.Equal(DeveloperLiveReloadStatus.Watching, coordinator.Status);
        Assert.Equal(1, coordinator.LastValidPreview!.SourceRevision);
    }

    [Fact]
    public void DebounceAndMaximumWaitGateTheBuild()
    {
        var coordinator = new DeveloperLiveReloadCoordinator(new DeveloperLiveReloadConfiguration(Project, "t", "local", "sdk", "a", Root, 100, TimeSpan.FromMilliseconds(200), TimeSpan.FromSeconds(1)));
        // Below threshold; nothing to build yet.
        Assert.False(coordinator.ShouldBuildNow(TimeSpan.FromSeconds(5)));
        coordinator.RecordChange(Change(1));
        Assert.False(coordinator.ShouldBuildNow(TimeSpan.FromMilliseconds(50))); // within debounce
        Assert.True(coordinator.ShouldBuildNow(TimeSpan.FromMilliseconds(250))); // debounce elapsed
    }

    [Fact]
    public void OlderOrEqualRevisionIsSuperseded()
    {
        var coordinator = new DeveloperLiveReloadCoordinator(Config(threshold: 1));
        coordinator.RecordChange(Change(3));
        Assert.Equal(DeveloperLiveReloadDecision.Superseded, coordinator.RecordChange(Change(2)).Decision);
    }

    [Fact]
    public void InvalidConfigurationAndChangeAreRejected()
    {
        Assert.Throws<InvalidDataException>(() => new DeveloperLiveReloadConfiguration(Guid.Empty, "t", "local", "sdk", "a", Root, 1, TimeSpan.Zero, TimeSpan.Zero).Capture());
        Assert.Throws<InvalidDataException>(() => new DeveloperLiveReloadConfiguration(Project, "t", "local", "sdk", "a", Root, 0, TimeSpan.Zero, TimeSpan.Zero).Capture());
        Assert.Throws<InvalidDataException>(() => new DeveloperLiveReloadChange(0, 1).Capture());
    }
}
