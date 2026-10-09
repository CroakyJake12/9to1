using Haven.Application;
using Haven.Infrastructure.Native.Windows;
using HavenOS.Home.Apps;
using HavenOS.Home.Core;
using Xunit;

namespace Haven.Infrastructure.Tests;

// These genuine owning-source controls prove absence, private issuer refusal and
// cached product operations. Authentic signed Windows installed-child execution
// requires the real enrolled/published candidate and is a separate runtime gate.
public sealed class NativeWindowsHomeSelectedApplicationSourceTests
{
    [Fact]
    public async Task Actual_unconfigured_root_apps_source_is_unavailable_and_never_seeds_machine_state()
    {
        await WithActualUnconfiguredClient(async (actual, file) =>
        {
            var snapshot = await actual.GetSnapshotAsync(new(), CancellationToken.None);
            Assert.Equal(HomeAppsDataState.Unavailable, snapshot.State);
            Assert.Equal("HomeApps.InstalledRootRequired", snapshot.Error?.Code);
            Assert.Empty(snapshot.Packages); Assert.False(File.Exists(file));
        });
    }
    [Fact]
    public async Task Public_intent_metadata_and_foreign_raw_ack_cannot_issue_launch_or_read_metadata()
    {
        await WithActualUnconfiguredClient((actual, file) =>
        {
            var foreign = new UnissuedIntent();
            Assert.False(actual.IsIssuedOriginalLaunchIntent(foreign));
            Assert.Throws<UnauthorizedAccessException>(() => { _ = actual.GetOriginalLaunchIntentDigest(foreign); });
            Assert.Throws<UnauthorizedAccessException>(() => actual.DemandOriginalPinnedLaunchIntent(foreign));
            var acknowledgment = new UnissuedAcknowledgment(foreign);
            var raw = Task.FromResult<ICanonicalInstalledApplicationLaunchAcknowledgment>(acknowledgment);
            Assert.False(actual.IsOriginalLaunchTask(foreign, raw));
            Assert.False(actual.IsOwnedOriginalLaunchAcknowledgment(foreign, acknowledgment, raw));
            Assert.Equal(0, foreign.MetadataReads); Assert.False(File.Exists(file));
            return Task.CompletedTask;
        });
    }
    [Fact]
    public async Task Canonical_apps_operation_reuses_same_original_task_and_rejects_request_rebinding_without_installation()
    {
        await WithActualUnconfiguredClient(async (actual, file) =>
        {
            var request = new HomePackageActionRequest("unissued-package", HomePackageAction.Install, "same-source-operation");
            var raw = actual.ExecuteAsync(request, CancellationToken.None);
            var decision = await raw;
            Assert.Equal(HomePackageOperationState.Rejected, decision.State);
            Assert.Equal("HomeApps.SingleInstallerRequired", decision.Code);
            Assert.Same(raw, actual.ExecuteAsync(request, CancellationToken.None));
            Assert.Throws<UnauthorizedAccessException>(() => { _ = actual.ExecuteAsync(request with { Action = HomePackageAction.Launch }, CancellationToken.None); });
            Assert.False(File.Exists(file));
        });
    }
    [Fact]
    public async Task Launch_business_drain_is_cached_and_leaves_actual_host_reads_live_until_whole_root_retirement()
    {
        await WithActualUnconfiguredClient(async (actual, file) =>
        {
            var original = actual.DrainOriginalApplicationLaunchesBeforeHomeCloseAsync(); await original;
            Assert.Same(original, actual.OriginalApplicationLaunchClose);
            Assert.Same(original, actual.DrainOriginalApplicationLaunchesBeforeHomeCloseAsync());
            var snapshot = await actual.GetSnapshotAsync(new(), CancellationToken.None);
            Assert.Equal(HomeAppsDataState.Unavailable, snapshot.State); Assert.False(File.Exists(file));
            Assert.Throws<ObjectDisposedException>(() =>
            { _ = actual.ExecuteAsync(new("unissued-package", HomePackageAction.Launch, "sealed-launch-admission"), CancellationToken.None); });
            Assert.Null(actual.OriginalClose);
        });
    }
    [Fact]
    public void Launch_policy_is_individual_home_only_and_never_a_service_or_other_app_action_grant()
    {
        var actual = new HomeInstalledApplicationLaunchActionPolicySource();
        var launch = actual.TryGet("home", HomeCanonicalInstalledApplicationLaunchSource.WriteAction);
        Assert.NotNull(launch); Assert.True(launch.RequiresPerActionApproval);
        Assert.Equal(HavenOS.Home.PermissionsTrustNotifications.HomePermissionRisk.Elevated, launch.Risk);
        Assert.Null(actual.TryGet("maps", HomeCanonicalInstalledApplicationLaunchSource.WriteAction));
        Assert.Null(actual.TryGet("home", "maps.library.read")); Assert.Null(actual.TryGet("home", "home.profile.importStore"));
    }
    private static async Task WithActualUnconfiguredClient(Func<NativeWindowsHomeRootClient, string, Task> body)
    {
        var file = Path.Combine(Path.GetTempPath(), "actual-selected-root-uncreated-" + Guid.NewGuid().ToString("N"), "home.json");
        using var lifetime = new CancellationTokenSource();
        var actual = new NativeWindowsHomeRootClient(file, lifetime.Token);
        var errors = new List<Exception>(); Task? close = null;
        try { await body(actual, file); } catch (Exception cause) { errors.Add(cause); }
        finally
        {
            try { close = actual.CloseAndDrainOriginalAsync(); } catch (Exception cause) { errors.Add(cause); }
            if (close is not null) try { await close; } catch (Exception cause) { errors.Add(close.Exception ?? cause); }
        }
        if (errors.Count != 0) throw new AggregateException("Actual selected source/body/independently joined cleanup failed.", errors);
        Assert.Same(close, actual.OriginalClose); Assert.Same(close, actual.CloseAndDrainOriginalAsync());
        Assert.False(File.Exists(file)); Assert.False(Directory.Exists(Path.GetDirectoryName(file)!));
    }
    private sealed class UnissuedIntent : ICanonicalInstalledApplicationLaunchIntent
    {
        internal int MetadataReads;
        private T Foreign<T>() { MetadataReads++; throw new InvalidOperationException("Unissued metadata must remain unread."); }
        public AuthenticatedResourceActor Actor => Foreign<AuthenticatedResourceActor>();
        public Guid OperationId => Foreign<Guid>(); public string AppId => Foreign<string>(); public string PackageId => Foreign<string>();
        public Guid InstalledApplicationId => Foreign<Guid>(); public long InstalledApplicationRevision => Foreign<long>();
        public string ActivationSha256 => Foreign<string>(); public string DescriptorSha256 => Foreign<string>();
        public string OriginalHomeLeaseIdentity => Foreign<string>();
    }
    private sealed class UnissuedAcknowledgment(ICanonicalInstalledApplicationLaunchIntent intent) : ICanonicalInstalledApplicationLaunchAcknowledgment
    {
        public ICanonicalInstalledApplicationLaunchIntent OriginalIntent => intent;
        public bool Applied => throw new InvalidOperationException("No private acknowledgment.");
        public int ProcessId => throw new InvalidOperationException("No private process.");
        public string ProcessStartIdentity => throw new InvalidOperationException("No private start.");
        public string ExecutableIdentity => throw new InvalidOperationException("No private image.");
        public string Reason => throw new InvalidOperationException("No private decision.");
    }
}
