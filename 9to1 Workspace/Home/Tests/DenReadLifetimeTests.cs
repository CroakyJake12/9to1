using NineToOne.Dulche.Den;
using Xunit;

namespace HavenOS.Home.Tests;

[CollectionDefinition("Den writer lifetime", DisableParallelization = true)]
public sealed class DenWriterLifetimeCollection { }

[Collection("Den writer lifetime")]
public sealed class DenReadLifetimeTests
{
    [Fact]
    public async Task List_filters_an_earlier_object_revoked_while_later_objects_are_read()
    {
        var root = NewRoot();
        try
        {
            await using var store = await Seed(root, "a", "b");
            var records = await new DulcheDen(store, new RevokeEarlier(), "owner").ListAsync<AgentDefinitionRecord>("personal");
            Assert.Equal("b", Assert.Single(records).Id);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public async Task Denied_object_is_not_materialized_even_when_its_private_file_is_corrupt()
    {
        var root = NewRoot();
        try
        {
            await using var store = await Seed(root, "a", "b");
            var path = Assert.Single(Directory.GetFiles(Path.Combine(root, "records"), "b.json", SearchOption.AllDirectories));
            await File.WriteAllTextAsync(path, "not authorized or valid JSON");
            var policy = new NamespaceAccessPolicy([new("owner", "personal", DenPermission.Read, new HashSet<string> { "a" })]);
            var records = await new DulcheDen(store, policy, "owner").ListAsync<AgentDefinitionRecord>("personal");
            Assert.Equal("a", Assert.Single(records).Id);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public async Task Export_rechecks_current_read_after_snapshot_materialization()
    {
        var root = NewRoot();
        try
        {
            await using var store = await Seed(root, "a");
            var policy = new RevokeAtDelivery();
            var failure = await Assert.ThrowsAsync<DenException>(() => new DulcheDen(store, policy, "owner").ExportAsync(["personal"]));
            Assert.Equal(DenErrorCode.Forbidden, failure.Code);
            Assert.Equal(4, policy.Reads);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(int.MaxValue)]
    public async Task Shared_blob_collection_requires_current_Den_admin_through_publication(int denyAt)
    {
        var root = NewRoot();
        try
        {
            await using var store = await Seed(root, "a");
            var path = Path.Combine(root, "blobs", new string('a', 64) + ".blob");
            await File.WriteAllBytesAsync(path, [1, 2, 3]);
            var den = new DulcheDen(store, new RevokeCollection(store.Manifest.DenId, denyAt), "owner");
            if (denyAt == int.MaxValue)
            {
                Assert.Equal(3, await den.CollectUnreferencedBlobsAsync("personal", true));
                Assert.False(File.Exists(path));
            }
            else
            {
                var failure = await Assert.ThrowsAsync<DenException>(() => den.CollectUnreferencedBlobsAsync("personal", true));
                Assert.Equal(DenErrorCode.Forbidden, failure.Code);
                Assert.Equal(new byte[] { 1, 2, 3 }, await File.ReadAllBytesAsync(path));
            }
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    private sealed class RevokeCollection(string denId, int denyAt) : IDenAccessPolicy
    {
        private int _checks;
        public ValueTask<bool> IsAllowedAsync(string principalId, string namespaceId, string objectId, DenPermission permission,
            CancellationToken cancellationToken = default) => ValueTask.FromResult(namespaceId != denId || ++_checks < denyAt);
    }

    [Fact]
    public async Task Cancellation_while_file_lease_is_held_releases_process_writer_gate()
    {
        var root = NewRoot();
        try
        {
            await using var store = await Seed(root, "a");
            var den = new DulcheDen(store, new NamespaceAccessPolicy([new("owner", "personal", DenPermission.Administer)]), "owner");
            using var cancellation = new CancellationTokenSource();
            await using (var held = new FileStream(Path.Combine(root, ".den-write-lock"), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                var blocked = den.SaveAsync(new AgentDefinitionRecord { Id = "b", NamespaceId = "personal", DisplayName = "B", Version = "1" },
                    0, "cancelled", cancellation.Token);
                // Save reaches the actual contended file lease synchronously before its retry delay.
                cancellation.Cancel();
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => blocked);
            }
            var saved = await den.SaveAsync(new AgentDefinitionRecord { Id = "b", NamespaceId = "personal", DisplayName = "B", Version = "1" },
                0, "retry").WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(1, saved.Revision);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    private static async Task<DenStore> Seed(string root, params string[] ids)
    {
        var store = await DenStore.CreateAsync(root, [new("personal", "personal")]);
        var owner = new DulcheDen(store, new NamespaceAccessPolicy([new("owner", "personal", DenPermission.Administer)]), "owner");
        foreach (var id in ids)
            await owner.SaveAsync(new AgentDefinitionRecord { Id = id, NamespaceId = "personal", DisplayName = id, Version = "1" }, 0, "seed-" + id);
        return store;
    }
    private static string NewRoot() => Path.Combine(Path.GetTempPath(), "astra-den-read-" + Guid.NewGuid().ToString("N"));
    private sealed class RevokeEarlier : IDenAccessPolicy
    {
        private bool _revoked;
        public ValueTask<bool> IsAllowedAsync(string principalId, string namespaceId, string objectId, DenPermission permission,
            CancellationToken cancellationToken = default)
        {
            if (objectId == "b") _revoked = true;
            return ValueTask.FromResult(objectId != "a" || !_revoked);
        }
    }
    private sealed class RevokeAtDelivery : IDenAccessPolicy
    {
        public int Reads { get; private set; }
        public ValueTask<bool> IsAllowedAsync(string principalId, string namespaceId, string objectId, DenPermission permission,
            CancellationToken cancellationToken = default) => ValueTask.FromResult(permission != DenPermission.Read || ++Reads < 4);
    }
}
