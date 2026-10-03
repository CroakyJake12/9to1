using NineToOne.Dulche.Den;
using Xunit;

namespace HavenOS.Home.Tests;

public sealed class DenNamespaceAuthorityTests
{
    [Theory]
    [InlineData(false, 2)]
    [InlineData(false, 3)]
    [InlineData(true, 2)]
    [InlineData(true, 3)]
    public async Task Namespace_admin_revocation_preserves_manifest_and_drops_private_staging(bool sharing, int denyAt)
    {
        var root = NewRoot();
        try
        {
            await using var store = await DenStore.CreateAsync(root, [new("personal", "personal")]);
            var path = Path.Combine(root, "den.json");
            var before = await File.ReadAllBytesAsync(path);
            var den = new DulcheDen(store, new Revoking(DenPermission.Administer, denyAt), "owner");
            var error = await Assert.ThrowsAsync<DenException>(() => sharing
                ? den.SetNamespaceSharingAsync("personal", true, store.Manifest.Revision, "change-sharing")
                : den.CreateNamespaceAsync("new-space", "personal", false, store.Manifest.Revision, "create-space"));
            Assert.Equal(DenErrorCode.Forbidden, error.Code);
            Assert.Equal(before, await File.ReadAllBytesAsync(path));
            Assert.Empty(Directory.GetFiles(root, "*.tmp", SearchOption.AllDirectories));
            await using var reopened = await DenStore.OpenAsync(root);
            Assert.Single(reopened.Manifest.Namespaces);
            Assert.False(reopened.Manifest.Namespaces[0].Shared);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public async Task Independent_manifest_writer_reloads_revision_before_CAS()
    {
        var root = NewRoot();
        try
        {
            await using var first = await DenStore.CreateAsync(root, [new("personal", "personal")]);
            await using var second = await DenStore.OpenAsync(root);
            var policy = new NamespaceAccessPolicy([new("owner", first.Manifest.DenId, DenPermission.Administer)]);
            var expected = second.Manifest.Revision;
            await new DulcheDen(first, policy, "owner").CreateNamespaceAsync("added", "personal", false, expected, "create");
            var error = await Assert.ThrowsAsync<DenException>(() => new DulcheDen(second, policy, "owner")
                .SetNamespaceSharingAsync("personal", true, expected, "stale"));
            Assert.Equal(DenErrorCode.Conflict, error.Code);
            await using var reopened = await DenStore.OpenAsync(root);
            Assert.Equal(2, reopened.Manifest.Namespaces.Count);
            Assert.False(reopened.Manifest.Namespaces.Single(item => item.Id == "personal").Shared);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public async Task Read_revoked_during_materialization_does_not_return_record()
    {
        var root = NewRoot();
        try
        {
            await using var store = await DenStore.CreateAsync(root, [new("personal", "personal")]);
            var owner = new DulcheDen(store, new NamespaceAccessPolicy([new("owner", "personal", DenPermission.Administer)]), "owner");
            await owner.SaveAsync(new AgentDefinitionRecord { Id = "agent", NamespaceId = "personal", DisplayName = "Private", Version = "1" }, 0, "seed");
            var caller = new DulcheDen(store, new Revoking(DenPermission.Read, 2), "owner");
            var error = await Assert.ThrowsAsync<DenException>(() => caller.GetAsync<AgentDefinitionRecord>("personal", "agent"));
            Assert.Equal(DenErrorCode.Forbidden, error.Code);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public async Task Concurrent_first_creation_cannot_replace_the_winning_Den_identity()
    {
        var root = NewRoot();
        try
        {
            async Task<(DenStore? Store, DenException? Error)> Create()
            {
                try { return (await DenStore.CreateAsync(root, [new("personal", "personal")]), null); }
                catch (DenException error) { return (null, error); }
            }
            var results = await Task.WhenAll(Create(), Create());
            var winner = Assert.Single(results, item => item.Store is not null).Store!;
            Assert.Equal(DenErrorCode.Conflict, Assert.Single(results, item => item.Error is not null).Error!.Code);
            await using var reopened = await DenStore.OpenAsync(root);
            Assert.Equal(winner.Manifest.DenId, reopened.Manifest.DenId);
            await winner.DisposeAsync();
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    private static string NewRoot() => Path.Combine(Path.GetTempPath(), "astra-den-namespace-" + Guid.NewGuid().ToString("N"));
    private sealed class Revoking(DenPermission watched, int denyAt) : IDenAccessPolicy
    {
        private int _observations;
        public ValueTask<bool> IsAllowedAsync(string principalId, string namespaceId, string objectId, DenPermission permission,
            CancellationToken cancellationToken = default) => ValueTask.FromResult(permission != watched || ++_observations < denyAt);
    }
}
