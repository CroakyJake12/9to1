using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text.Json;
using Haven.Infrastructure.Native.Linux;
using HavenOS.Home.Apps;
using HavenOS.Home.Core;
using Haven.Application;
using Xunit;

namespace Haven.Infrastructure.Tests;

/// <summary>Actual isolated signed-catalogue/filesystem fixtures. A passing pure
/// state predicate is not an authenticated Home review or privileged mutation.</summary>
[SupportedOSPlatform("linux")]
public sealed class LinuxHomePackageRemovalTests
{
    private sealed class LinuxFactAttribute : FactAttribute
    { public LinuxFactAttribute() { if (!OperatingSystem.IsLinux()) Skip = "Requires actual Linux filesystem/process platform."; } }
    [LinuxFact]
    public async Task Unconnected_actual_Root_cannot_start_package_service_or_create_a_device_registry()
    {
        using var rig = new LinuxHomeBootstrapActivationOwnerTests.Rig();
        var statePath = Path.Combine(rig.Root, "unchanged-user-state.json");
        var store = new FileHomeCoreStateStore(statePath);
        var profiles = new HomeLocalProfileIdentity(store, new OperatingSystemPrincipalSource());
        var permissions = new HavenOS.Home.PermissionsTrustNotifications.HomePermissionTrustService(store, (_, _) => null);
        var resources = new ResourceAuthorizationService(profiles, []);
        var local = new HomeLocalStoreOwnership(store, profiles, new HomeLocalStoreEvidenceRegistry([]), permissions);
        var ownership = new HomeResourceStoreOwnershipAuthority(local, profiles);
        var broker = new HomeResourceOperationBroker(resources, permissions);
        await using var sessions = new HomeNativeCoreApiSessions(permissions, profiles,
            new UnavailableHomeNativeInstalledPeerVerifier(), () => throw new InvalidOperationException("No local API was installed."));
        var client = new LinuxHomeRootClient(Path.Combine(rig.Root, "unregistered-root.sock"), rig.Token);
        var service = client.CreateOriginalPackageOperationsService(new(store, profiles, permissions, resources, ownership, broker), sessions);
        Assert.Equal("packages", service.Descriptor.ServiceId);
        await Assert.ThrowsAsync<PlatformNotSupportedException>(() => service.StartAsync(rig.Token));
        Assert.False(((HomeNativePackageOperationsService)service).IsOriginalAvailable);
        await service.StopAsync(); await client.CloseAndDrainAsync();
        Assert.False(File.Exists(statePath)); Assert.False(File.Exists(rig.StatePath));
        Assert.False(File.Exists(Path.Combine(rig.Root, "unregistered-root.sock")));
    }
    private sealed class LinuxAdminFactAttribute : FactAttribute
    { public LinuxAdminFactAttribute() { if (!OperatingSystem.IsLinux() || GetEuid() != 0) Skip = "Requires actual Linux UID0 isolated signed-file fixture."; } }
    [LinuxAdminFact]
    public async Task Shared_state_policy_preserves_mandatory_and_dependent_guards_on_real_signed_installed_rows()
    {
        using var rig = new LinuxHomeBootstrapActivationOwnerTests.Rig(includeNativeHelper: true, includeOptionalApp: true);
        await rig.Enroll(); await rig.Owner.ActivateAsync(rig.Request(), rig.Token);
        var before = File.ReadAllBytes(rig.StatePath);
        var read = await rig.Database.ReadAsync(rig.Token); Assert.True(read.Succeeded);
        var registry = read.Snapshot!;
        var home = Descriptor(registry, "home.pkg"); var optional = Descriptor(registry, "optional.pkg");
        Assert.NotNull(HomePackageOriginalRemovalChecks.Evaluate(registry, home, "home.pkg"));
        Assert.Null(HomePackageOriginalRemovalChecks.Evaluate(registry, optional, "home.pkg"));
        var dependent = registry.Packages.Single(value => value.PackageId == "home.pkg");
        var blocked = registry with { Packages = registry.Packages.Select(value => value.PackageId == dependent.PackageId
            ? value with { Dependencies = [new("optional.pkg", "1.0.0")] } : value).ToArray() };
        Assert.Null(HomePackageDatabase.ValidateOriginalProtectedSnapshot(blocked));
        Assert.NotNull(HomePackageOriginalRemovalChecks.Evaluate(blocked, optional, "home.pkg"));
        // Metadata evaluation writes nothing and supplies no original mutation,
        // accepted socket, session, Home approval or Root installed authority.
        Assert.Equal(before, File.ReadAllBytes(rig.StatePath));
    }
    [LinuxAdminFact]
    public async Task Shared_state_policy_never_ignores_another_pending_optional_operation()
    {
        using var rig = new LinuxHomeBootstrapActivationOwnerTests.Rig(includeOptionalApp: true);
        await rig.Enroll(); await rig.Owner.ActivateAsync(rig.Request(), rig.Token);
        var registry = (await rig.Database.ReadAsync(rig.Token)).Snapshot!;
        var optional = Descriptor(registry, "optional.pkg");
        var pending = new HomePackageJournalEntry("fixture-unissued-idempotency", "fixture-unissued-operation", "optional.pkg", "Uninstall",
            HomePackageJournalState.Pending, DateTimeOffset.UtcNow, null, "HomePackages.OriginalReserved", false, true, [], [], [], []);
        var captured = registry with { RecentOperations = registry.RecentOperations.Append(pending).ToArray() };
        Assert.Null(HomePackageDatabase.ValidateOriginalProtectedSnapshot(captured));
        Assert.NotNull(HomePackageOriginalRemovalChecks.Evaluate(captured, optional, "home.pkg"));
        Assert.NotNull(HomePackageOriginalRemovalChecks.Evaluate(captured, optional, "home.pkg", "another-unissued-operation"));
    }
    [LinuxAdminFact]
    public async Task Real_signed_bootstrap_cannot_adopt_removed_metadata_without_original_terminal_provenance()
    {
        using var rig = new LinuxHomeBootstrapActivationOwnerTests.Rig(includeNativeHelper: true, includeOptionalApp: true);
        await rig.Enroll(); await rig.Owner.ActivateAsync(rig.Request(), rig.Token);
        var beforeBytes = File.ReadAllBytes(rig.StatePath);
        var original = (await rig.Database.ReadAsync(rig.Token)).Snapshot!;
        var operation = original.RecentOperations.First().OperationId;
        // Exact real protected baseline and retained signed cohort, observed only.
        LinuxHomePackageRemovalWire.DemandOriginalCanonicalOutcomes(original, original.Packages, "home.pkg", operation);
        var changed = original with { Packages = original.Packages.Select(value => value.PackageId == "optional.pkg"
            ? value with { InstalledVersion = null, InstallationState = HomePackageInstallState.Available, Revision = value.Revision + 1 } : value).ToArray() };
        Assert.Null(HomePackageDatabase.ValidateOriginalProtectedSnapshot(changed));
        Assert.Throws<UnauthorizedAccessException>(() => LinuxHomePackageRemovalWire.DemandOriginalCanonicalOutcomes(changed, original.Packages, "home.pkg", operation));
        var mandatoryChanged = original with { Packages = original.Packages.Select(value => value.PackageId == "home.pkg"
            ? value with { InstalledVersion = null, InstallationState = HomePackageInstallState.Available, Revision = value.Revision + 1 } : value).ToArray() };
        Assert.Throws<UnauthorizedAccessException>(() => LinuxHomePackageRemovalWire.DemandOriginalCanonicalOutcomes(mandatoryChanged, original.Packages, "home.pkg", operation));
        var forgedJournal = new HomePackageJournalEntry("copied-idempotency", "unissued-operation", "optional.pkg", "Uninstall",
            HomePackageJournalState.Succeeded, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow,
            LinuxHomePackageRemovalWire.SuccessCode, false, true, [], [], [], []);
        var terminalWithoutOriginalProof = original with { RecentOperations = original.RecentOperations.Append(forgedJournal).ToArray() };
        Assert.Throws<UnauthorizedAccessException>(() => LinuxHomePackageRemovalWire.DemandOriginalCanonicalOutcomes(terminalWithoutOriginalProof, original.Packages, "home.pkg", operation));
        Assert.Equal(beforeBytes, File.ReadAllBytes(rig.StatePath));
        await rig.Owner.WithinSettledCodeAsync(rig.Request(), async (code, token) => {
            await code.DemandPinnedCurrentAsync(token);
            Assert.Equal(original.Packages.Count, code.OriginalInstalledEntries.Count);
        }, rig.Token);
    }
    private static HomePackageArtifactDescriptor Descriptor(HomePackageDatabaseSnapshot registry, string package)
    {
        var row = registry.Packages.Single(value => value.PackageId == package);
        var activation = row.UnknownFields![HomePackageOriginalInstalledActivationRecord.Field].Deserialize<HomePackageOriginalInstalledActivationRecord>()!;
        return HomePackageOriginalArtifactDescriptorParser.Parse(Convert.FromBase64String(activation.SignedDescriptorBase64),
            Convert.FromBase64String(activation.DescriptorPayloadBase64), activation.CatalogueRevision,
            new(package, HomePackageAction.Uninstall, "pure-state-fixture", ExpectedRevision: activation.CatalogueRevision)).Descriptor;
    }
    [DllImport("libc", EntryPoint = "geteuid")] private static extern uint GetEuid();
}
