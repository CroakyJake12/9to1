using NineToOne.Dulche.Den;
using Xunit;

namespace HavenOS.Home.Tests;

public sealed class DenAttachmentCommitAuthorityTests
{
    [Fact]
    public async Task Attachment_uses_bytes_captured_before_awaited_owner_authorization()
    {
        var root = NewRoot();
        try
        {
            await using var store = await DenStore.CreateAsync(root, [new("personal", "personal")]);
            await SeedAgent(store);
            var access = new BlockingOwner(); var den = new DulcheDen(store, access, "owner");
            byte[] original = [1, 2, 3];
            var saving = den.AddAttachmentAsync("personal", "agent", "agent", "application/octet-stream", original, "capture");
            await access.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            original[0] = 9; access.Release.TrySetResult();
            var reference = await saving;
            Assert.Equal(new byte[] { 1, 2, 3 }, await den.ReadAttachmentAsync("personal", reference.Id));
            Assert.Equal(9, original[0]);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public async Task Owner_revocation_at_blob_publication_leaves_no_blob_or_reference()
    {
        var root = NewRoot();
        try
        {
            await using var store = await DenStore.CreateAsync(root, [new("personal", "personal")]);
            await SeedAgent(store);
            var den = new DulcheDen(store, new RevokeOwnerAt(3), "owner");
            var error = await Assert.ThrowsAsync<DenException>(() => den.AddAttachmentAsync("personal", "agent", "agent",
                "application/octet-stream", new byte[] { 1, 2, 3 }, "deny-blob"));
            Assert.Equal(DenErrorCode.Forbidden, error.Code);
            Assert.Empty(Directory.GetFiles(Path.Combine(root, "blobs")));
            Assert.Empty(await den.ListAsync<BlobReferenceRecord>("personal"));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public async Task Owner_revocation_at_reference_publication_denies_even_when_reference_write_remains_allowed()
    {
        var root = NewRoot();
        try
        {
            await using var store = await DenStore.CreateAsync(root, [new("personal", "personal")]);
            await SeedAgent(store);
            var den = new DulcheDen(store, new RevokeOwnerAtReferenceCommit(), "owner");
            var error = await Assert.ThrowsAsync<DenException>(() => den.AddAttachmentAsync("personal", "agent", "agent",
                "application/octet-stream", new byte[] { 4, 5, 6 }, "deny-reference"));
            Assert.Equal(DenErrorCode.Forbidden, error.Code);
            Assert.Empty(await den.ListAsync<BlobReferenceRecord>("personal"));
            Assert.Empty(Directory.GetDirectories(Path.Combine(root, "transactions")));
            // Immutable unreferenced content can remain for owning garbage collection; it is not attachment access.
            Assert.Single(Directory.GetFiles(Path.Combine(root, "blobs"), "*.blob"));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public async Task Quota_requires_current_Admin_at_final_Save_not_just_Write()
    {
        var root = NewRoot();
        try
        {
            await using var store = await DenStore.CreateAsync(root, [new("personal", "personal")]);
            var den = new DulcheDen(store, new RevokeAdminAt(4), "owner");
            var error = await Assert.ThrowsAsync<DenException>(() => den.SetStorageQuotaAsync("personal",
                new StorageQuotaRecord { Id = "storage-quota", NamespaceId = "personal", AttachmentBytes = 16 }, 0, "quota"));
            Assert.Equal(DenErrorCode.Forbidden, error.Code);
            Assert.Null(await den.GetAsync<StorageQuotaRecord>("personal", "storage-quota"));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(int.MaxValue)]
    public async Task Purge_checks_current_Admin_under_lease_and_before_actual_deletion(int denyAt)
    {
        var root = NewRoot();
        try
        {
            await using var store = await DenStore.CreateAsync(root, [new("personal", "personal")]);
            var owner = new DulcheDen(store, new NamespaceAccessPolicy([new("owner", "personal", DenPermission.Administer)]), "owner");
            var memory = await owner.RememberAsync(new MemoryEntry { Id = "memory", NamespaceId = "personal", Content = "keep until purge",
                Category = "default", ScopeKind = MemoryScopeKind.User, Provenance = MemoryProvenanceKind.ExplicitUserStatement },
                false, true, 0, 0, "remember");
            var deleted = await owner.SoftDeleteMemoryAsync("personal", "memory", memory.Revision, "soft-delete");
            var before = Directory.GetFiles(root, "*.json", SearchOption.AllDirectories).ToDictionary(path => path, File.ReadAllBytes);
            var caller = new DulcheDen(store, new RevokeAdminAt(denyAt), "owner");
            if (denyAt < int.MaxValue)
            {
                var error = await Assert.ThrowsAsync<DenException>(() => caller.PurgeMemoryAsync("personal", "memory", "purge", true));
                Assert.Equal(DenErrorCode.Forbidden, error.Code);
                Assert.Equal(before.Count, Directory.GetFiles(root, "*.json", SearchOption.AllDirectories).Length);
                foreach (var pair in before) Assert.Equal(pair.Value, await File.ReadAllBytesAsync(pair.Key));
                Assert.Equal(deleted.Revision, (await owner.GetAsync<MemoryEntry>("personal", "memory"))!.Revision);
            }
            else
            {
                await caller.PurgeMemoryAsync("personal", "memory", "purge", true);
                Assert.Null(await owner.GetAsync<MemoryEntry>("personal", "memory"));
                Assert.False(Directory.Exists(Path.Combine(root, "history", "personal", "memory")));
                await caller.PurgeMemoryAsync("personal", "memory", "purge", true);
            }
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public async Task Read_revocation_after_blob_materialization_denies_delivery()
    {
        var root = NewRoot();
        try
        {
            await using var store = await DenStore.CreateAsync(root, [new("personal", "personal")]);
            await SeedAgent(store);
            var owner = new DulcheDen(store, new NamespaceAccessPolicy([new("owner", "personal", DenPermission.Administer)]), "owner");
            var reference = await owner.AddAttachmentAsync("personal", "agent", "agent", "application/octet-stream", new byte[] { 1, 2, 3 }, "attach");
            var policy = new RevokeRead();
            var error = await Assert.ThrowsAsync<DenException>(() => new DulcheDen(store, policy, "owner").ReadAttachmentAsync("personal", reference.Id));
            Assert.Equal(DenErrorCode.Forbidden, error.Code);
            Assert.Equal(3, policy.Reads);
            Assert.Equal(new byte[] { 1, 2, 3 }, await owner.ReadAttachmentAsync("personal", reference.Id));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    private sealed class RevokeRead : IDenAccessPolicy
    {
        public int Reads { get; private set; }
        public ValueTask<bool> IsAllowedAsync(string principalId, string namespaceId, string objectId, DenPermission permission,
            CancellationToken cancellationToken = default) => ValueTask.FromResult(permission != DenPermission.Read || ++Reads < 3);
    }

    [Fact]
    public async Task Newly_created_Den_metadata_and_blob_files_are_private_on_Unix()
    {
        if (OperatingSystem.IsWindows()) return;
        var root = NewRoot();
        try
        {
            await using var store = await DenStore.CreateAsync(root, [new("personal", "personal")]);
            await SeedAgent(store);
            var den = new DulcheDen(store, new NamespaceAccessPolicy([new("owner", "personal", DenPermission.Administer)]), "owner");
            await den.AddAttachmentAsync("personal", "agent", "agent", "application/octet-stream", new byte[] { 7 }, "attachment");
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute, File.GetUnixFileMode(root));
            foreach (var file in Directory.GetFiles(root, "*", SearchOption.AllDirectories))
                Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(file));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    private static Task<AgentDefinitionRecord> SeedAgent(DenStore store) => new DulcheDen(store,
        new NamespaceAccessPolicy([new("owner", "personal", DenPermission.Administer)]), "owner").SaveAsync(
            new AgentDefinitionRecord { Id = "agent", NamespaceId = "personal", DisplayName = "Agent", Version = "1" }, 0, "seed-agent");

    private static string NewRoot() => Path.Combine(Path.GetTempPath(), "astra-den-attachment-" + Guid.NewGuid().ToString("N"));
    private sealed class BlockingOwner : IDenAccessPolicy
    {
        private int _writes;
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async ValueTask<bool> IsAllowedAsync(string principalId, string namespaceId, string objectId, DenPermission permission,
            CancellationToken cancellationToken = default)
        {
            if (permission == DenPermission.Write && objectId == "agent" && ++_writes == 1)
            { Entered.TrySetResult(); await Release.Task.WaitAsync(cancellationToken); }
            return true;
        }
    }
    private sealed class RevokeOwnerAt(int denyAt) : IDenAccessPolicy
    {
        private int _writes;
        public ValueTask<bool> IsAllowedAsync(string principalId, string namespaceId, string objectId, DenPermission permission,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(permission != DenPermission.Write || objectId != "agent" || ++_writes < denyAt);
    }
    private sealed class RevokeOwnerAtReferenceCommit : IDenAccessPolicy
    {
        private int _referenceWrites;
        public ValueTask<bool> IsAllowedAsync(string principalId, string namespaceId, string objectId, DenPermission permission,
            CancellationToken cancellationToken = default)
        {
            if (permission != DenPermission.Write) return ValueTask.FromResult(true);
            if (objectId != "agent") { _referenceWrites++; return ValueTask.FromResult(true); }
            return ValueTask.FromResult(_referenceWrites < 3);
        }
    }
    private sealed class RevokeAdminAt(int denyAt) : IDenAccessPolicy
    {
        private int _checks;
        public ValueTask<bool> IsAllowedAsync(string principalId, string namespaceId, string objectId, DenPermission permission,
            CancellationToken cancellationToken = default) => ValueTask.FromResult(permission != DenPermission.Administer || ++_checks < denyAt);
    }
}
