using Haven.Application;
using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;
using Xunit;

namespace HavenOS.Home.Tests;

public sealed class HomeLocalStoreSetupEvidenceTests
{
    [Theory]
    [InlineData("valid", true)]
    [InlineData("inaccessible", false)]
    [InlineData("foreign-kind", false)]
    [InlineData("foreign-store", false)]
    [InlineData("missing-revision", false)]
    public async Task Inspection_offers_empty_binding_only_for_exact_accessible_evidence_and_never_binds(string variant, bool eligible)
    {
        var root = Path.Combine(Path.GetTempPath(), "astra-setup-evidence-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new FileHomeCoreStateStore(Path.Combine(root, "home.json"));
            var profiles = new HomeLocalProfileIdentity(store, new Principal());
            var identities = new Identity();
            var evidence = new Evidence(new("planner", identities.Id.ToString("D"), "actual-revision", true, true, true));
            evidence.Current = variant switch
            {
                "inaccessible" => evidence.Current with { AccessibleToCurrentOsPrincipal = false },
                "foreign-kind" => evidence.Current with { ResourceKind = "other" },
                "foreign-store" => evidence.Current with { StoreId = Guid.NewGuid().ToString("D") },
                "missing-revision" => evidence.Current with { Revision = " " },
                _ => evidence.Current
            };
            var permissions = new HomePermissionTrustService(store, (_, _) =>
                new(HavenOS.Home.PermissionsTrustNotifications.HomePermissionRisk.High, false, false, true));
            var ownership = new HomeLocalStoreOwnership(store, profiles, new HomeLocalStoreEvidenceRegistry([evidence]), permissions);
            var session = new HomeLocalStoreSetupSession("planner", identities, profiles, ownership, evidence);
            var result = await session.InspectAsync(default);
            Assert.Equal(identities.Id, result.StoreId);
            Assert.Equal(eligible, result.CanBindEmpty);
            Assert.False(result.IsOwned);
            Assert.Null(result.PendingRequestId);
            Assert.DoesNotContain((await store.ReadAsync()).State!.Records, item => item.RecordType == "home.local-store-ownership");
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
    private sealed class Principal : ITrustedHostPrincipalSource
    {
        public ValueTask<string?> GetPrincipalAsync(CancellationToken ct) => ValueTask.FromResult<string?>("setup-test-principal");
    }
    private sealed class Identity : IResourceStoreIdentitySource
    {
        public Guid Id { get; } = Guid.NewGuid();
        public ValueTask<ResourceStoreIdentity> GetStoreIdentityAsync(CancellationToken ct) =>
            ValueTask.FromResult(new ResourceStoreIdentity(1, Id, DateTimeOffset.UnixEpoch, true));
    }
    private sealed class Evidence(HomeLocalStoreEvidence current) : IHomeLocalStoreEvidenceProvider
    {
        public HomeLocalStoreEvidence Current = current;
        public string ResourceKind => "planner";
        public ValueTask<HomeLocalStoreEvidence?> ReadAsync(string id, CancellationToken ct) => ValueTask.FromResult<HomeLocalStoreEvidence?>(Current);
    }
}
