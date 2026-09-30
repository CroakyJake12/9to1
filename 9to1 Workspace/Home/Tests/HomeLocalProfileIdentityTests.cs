using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;
using Xunit;

namespace HavenOS.Home.Tests;

public sealed class HomeLocalProfileIdentityTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "astra-profile-" + Guid.NewGuid().ToString("N"));
    private FileHomeCoreStateStore Store => new(Path.Combine(_root, "private", "home.json"));

    [Fact]
    public async Task Stable_profile_reopens_but_new_process_session_does_not_inherit_account_or_org()
    {
        var principal = new Principal();
        var first = await new HomeLocalProfileIdentity(Store, principal).GetCurrentAsync(default);
        var reopened = await new HomeLocalProfileIdentity(Store, principal).GetCurrentAsync(default);
        Assert.Equal(first!.ProfileId, reopened!.ProfileId);
        Assert.Equal(first.ActorId, reopened.ActorId);
        Assert.NotEqual(first.AuthenticationRevision, reopened.AuthenticationRevision);
        Assert.Null(first.AccountId); Assert.Null(first.OrganisationId);
        if (!OperatingSystem.IsWindows())
        {
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(Path.Combine(_root, "private", "home.json")));
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute, File.GetUnixFileMode(Path.Combine(_root, "private")));
        }
        principal.Value = "different-process-principal";
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => new HomeLocalProfileIdentity(Store, principal).GetCurrentAsync(default).AsTask());
    }

    [Fact]
    public async Task Existing_store_needs_explicit_revision_bound_import_and_new_empty_store_can_bind()
    {
        var profile = new HomeLocalProfileIdentity(Store, new Principal());
        var evidence = new Evidence();
        var permissions = new HomePermissionTrustService(Store, (target, action) =>
            target == "9to1.home.local-profile" && action == "home.profile.importStore"
                ? new(HavenOS.Home.PermissionsTrustNotifications.HomePermissionRisk.High, false, false, true) : null);
        var ownership = new HomeLocalStoreOwnership(Store, profile, evidence, permissions);
        Assert.Null(await ownership.GetVerifiedAsync("planner", "durable-store-1"));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => ownership.BindNewEmptyAsync("planner", "durable-store-1"));
        var approval = await ownership.RequestImportAsync("planner", "durable-store-1", "host-session");
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => ownership.CompleteImportAsync(approval.RequestId));
        Assert.True((await permissions.DecideAsync(approval.RequestId, HomeApprovalChoice.Accept)).Succeeded);
        evidence.Current = evidence.Current with { Revision = "changed-after-review" };
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => ownership.CompleteImportAsync(approval.RequestId));
        evidence.Current = evidence.Current with { Revision = "revision-1" };
        var imported = await ownership.CompleteImportAsync(approval.RequestId);
        Assert.Equal(approval.RequestId, imported.ImportApprovalId);
        Assert.NotNull(await new HomeLocalStoreOwnership(Store, profile, evidence, permissions).GetVerifiedAsync("planner", "durable-store-1"));
        evidence.Current = new("planner", "new-store-2", "revision-1", true, true, true);
        var created = await ownership.BindNewEmptyAsync("planner", "new-store-2");
        Assert.Null(created.ImportApprovalId);
        Assert.Null(await ownership.GetVerifiedAsync("planner", "unknown-store"));
    }

    [Fact]
    public async Task Real_host_principal_is_observed_without_environment_username_claims()
    {
        if (!(OperatingSystem.IsWindows() || OperatingSystem.IsLinux() || OperatingSystem.IsAndroid())) return;
        var principal = await new OperatingSystemPrincipalSource().GetPrincipalAsync(default);
        Assert.False(string.IsNullOrWhiteSpace(principal));
        Assert.True(principal!.StartsWith("windows-sid:", StringComparison.Ordinal) || principal.StartsWith("unix-euid:", StringComparison.Ordinal));
    }

    private sealed class Principal : ITrustedHostPrincipalSource
    {
        public string Value { get; set; } = "actual-host-fixture-principal";
        public ValueTask<string?> GetPrincipalAsync(CancellationToken ct) => ValueTask.FromResult<string?>(Value);
    }
    private sealed class Evidence : IHomeLocalStoreEvidenceSource
    {
        public HomeLocalStoreEvidence Current { get; set; } = new("planner", "durable-store-1", "revision-1", false, false, true);
        public ValueTask<HomeLocalStoreEvidence?> ReadAsync(string kind, string id, CancellationToken ct) =>
            ValueTask.FromResult<HomeLocalStoreEvidence?>(Current.ResourceKind == kind && Current.StoreId == id ? Current : null);
    }
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
}
