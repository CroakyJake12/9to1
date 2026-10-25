using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text.Json;
using Haven.Infrastructure.Native.Linux;
using HavenOS.Home.Apps;
using Xunit;

namespace Haven.Infrastructure.Tests;

/// <summary>Actual isolated signed-catalogue/filesystem fixtures. A passing pure
/// state predicate is not an authenticated Home review or privileged mutation.</summary>
[SupportedOSPlatform("linux")]
public sealed class LinuxHomePackageRemovalTests
{
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
