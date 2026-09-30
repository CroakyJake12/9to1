using Haven.Application;
using HavenOS.AIStudio;
using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;
using NineToOne.Dulche.Den;
using Xunit;

namespace HavenOS.AIStudio.Tests;

public sealed class StudioDenLifetimeTests
{
    [Fact]
    public async Task Selected_actual_Den_binds_in_same_Home_graph_and_replacement_retires_old_session()
    {
        await using var fixture = new Fixture();
        var firstId = await fixture.Lifetime.SelectAsync(Path.Combine(fixture.Root, "first"), true, fixture.Ownership);
        var first = await fixture.Lifetime.OpenBoundSessionAsync(fixture.Receipts);
        var agent = await first.Den.SaveAsync(new AgentDefinitionRecord
            { Id = "created-agent", NamespaceId = "personal", DisplayName = "Created", Version = "1" }, 0, "explicit-agent-create");
        Assert.Equal(agent.Id, (await first.Den.GetAsync<AgentDefinitionRecord>("personal", agent.Id))!.Id);
        Assert.False(await first.Den.AccessPolicy.IsAllowedAsync(first.Actor.ActorId, "personal", agent.Id, DenPermission.Execute));
        fixture.BeforeRetire = async () => Assert.NotNull(await fixture.Lifetime.ReadAsync(firstId, default));
        var secondId = await fixture.Lifetime.SelectAsync(Path.Combine(fixture.Root, "second"), true, fixture.Ownership);
        fixture.BeforeRetire = null;
        Assert.NotEqual(firstId, secondId);
        Assert.Equal(2, fixture.Retired);
        Assert.Null(await fixture.Lifetime.ReadAsync(firstId, default));
        Assert.Equal(secondId, (await fixture.Lifetime.OpenBoundSessionAsync(fixture.Receipts)).DenId);
        var denied = await Record.ExceptionAsync(() => first.Den.GetAsync<AgentDefinitionRecord>("personal", agent.Id));
        Assert.True(denied is ObjectDisposedException or DenException { Code: DenErrorCode.Forbidden });
    }

    [Fact]
    public async Task Existing_Den_requires_actual_Home_review_and_exact_retained_request()
    {
        await using var fixture = new Fixture();
        var existingRoot = Path.Combine(fixture.Root, "existing");
        await using (var original = await DenStore.CreateAsync(existingRoot, [new("personal", "personal")])) { }
        var id = await fixture.Lifetime.SelectAsync(existingRoot, false, fixture.Ownership);
        Assert.False((await fixture.Lifetime.ReadAsync(id, default))!.NewlyCreated);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => fixture.Lifetime.OpenBoundSessionAsync(fixture.Receipts));
        var review = await fixture.Lifetime.RequestExistingImportAsync(fixture.Ownership);
        Assert.Equal(HomePermissionRequestState.PendingApproval, review.State);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => fixture.Lifetime.CompleteExistingImportAsync(review.RequestId, fixture.Ownership));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => fixture.Lifetime.CompleteExistingImportAsync("unbound-review", fixture.Ownership));
        Assert.True((await fixture.Permissions.DecideAsync(review.RequestId, HomeApprovalChoice.Accept)).Succeeded);
        await fixture.Lifetime.CompleteExistingImportAsync(review.RequestId, fixture.Ownership);
        Assert.Equal(id, (await fixture.Lifetime.OpenBoundSessionAsync(fixture.Receipts)).DenId);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => fixture.Lifetime.CompleteExistingImportAsync(review.RequestId, fixture.Ownership));
    }

    private sealed class Fixture : IAsyncDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "astra-studio-host-" + Guid.NewGuid().ToString("N"));
        public StudioDenLifetime Lifetime { get; }
        public HomeLocalStoreOwnership Ownership { get; }
        public HomeResourceStoreOwnershipAuthority Receipts { get; }
        public HomePermissionTrustService Permissions { get; }
        public Func<Task>? BeforeRetire { get; set; }
        public int Retired { get; private set; }
        public Fixture()
        {
            var home = new FileHomeCoreStateStore(Path.Combine(Root, "home.json"));
            var profiles = new HomeLocalProfileIdentity(home, new OperatingSystemPrincipalSource());
            Lifetime = new StudioDenLifetime(profiles, async ct =>
            {
                ct.ThrowIfCancellationRequested();
                if (BeforeRetire is not null) await BeforeRetire();
                Retired++;
            });
            Permissions = new HomePermissionTrustService(home, (target, action) =>
                target == "9to1.home.local-profile" && action == "home.profile.importStore"
                    ? new(HavenOS.Home.PermissionsTrustNotifications.HomePermissionRisk.High, false, false, true) : null);
            Ownership = new HomeLocalStoreOwnership(home, profiles, new HomeLocalStoreEvidenceRegistry([Lifetime]), Permissions);
            Receipts = new HomeResourceStoreOwnershipAuthority(Ownership, profiles);
        }
        public async ValueTask DisposeAsync()
        {
            BeforeRetire = null;
            await Lifetime.DisposeAsync();
            if (Directory.Exists(Root)) Directory.Delete(Root, true);
        }
    }
}
