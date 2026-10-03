using System.Security.Cryptography;
using NineToOne.Dulche.Den;
using Xunit;

namespace HavenOS.Home.Tests;

public sealed class DenModelInstallationAuthorityTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Final_registration_authority_denial_preserves_existing_evidence_and_removes_staging(bool existing)
    {
        var root = NewRoot(); Directory.CreateDirectory(root);
        try
        {
            var artifact = Path.Combine(root, "model.bin"); await File.WriteAllBytesAsync(artifact, [1, 2, 3]);
            var hash = Convert.ToHexString(SHA256.HashData(new byte[] { 1, 2, 3 }));
            var registry = new LocalModelRegistry(root, "device");
            if (existing) await registry.RegisterAsync("model", artifact, hash);
            var record = Path.Combine(root, "local", "models", "device", "model.json");
            var before = File.Exists(record) ? await File.ReadAllBytesAsync(record) : null;
            var checks = 0;
            var error = await Assert.ThrowsAsync<DenException>(() => registry.RegisterGuardedAsync("model", artifact, hash,
                _ => ValueTask.FromResult(++checks < 3)));
            Assert.Equal(DenErrorCode.Forbidden, error.Code);
            Assert.Equal(3, checks);
            if (before is null) Assert.False(File.Exists(record)); else Assert.Equal(before, await File.ReadAllBytesAsync(record));
            Assert.Empty(Directory.GetFiles(root, "*.tmp", SearchOption.AllDirectories));
        }
        finally { Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task Den_rechecks_model_write_catalogue_revision_and_quota_at_actual_registration_publication(int change)
    {
        var root = NewRoot();
        try
        {
            await using var store = await DenStore.CreateAsync(root, [new("personal", "personal")]);
            var admin = new DulcheDen(store, new NamespaceAccessPolicy([new("owner", "personal", DenPermission.Administer)]), "owner");
            var artifact = Path.Combine(root, "model.bin"); await File.WriteAllBytesAsync(artifact, [7, 8, 9]);
            var hash = Convert.ToHexString(SHA256.HashData(new byte[] { 7, 8, 9 }));
            var model = await admin.SaveAsync(new ModelRecord { Id = "model", NamespaceId = "personal", DisplayName = "Model",
                ArtifactRevision = "artifact1", ArtifactSha256 = hash }, 0, "seed-model");
            var access = new PublicationPolicy(root);
            access.AtPublication = async () =>
            {
                if (change == 0) access.Allowed = false;
                if (change == 1) await admin.SaveAsync(model with { DisplayName = "Changed catalogue" }, model.Revision, "replace-model");
                if (change == 2) await admin.SetStorageQuotaAsync("personal", new StorageQuotaRecord { Id = "storage-quota",
                    NamespaceId = "personal", ModelWeightBytes = 2 }, 0, "quota-change");
            };
            var den = new DulcheDen(store, access, "owner");
            var error = await Assert.ThrowsAsync<DenException>(() => den.RegisterLocalModelAsync("personal", "model", artifact, hash));
            Assert.Equal(DenErrorCode.Forbidden, error.Code);
            Assert.True(access.Changed);
            var local = Path.Combine(root, "local", "models", store.Manifest.DeviceId);
            Assert.Empty(Directory.GetFiles(local, "*.json"));
            Assert.Empty(Directory.GetFiles(local, "*.tmp"));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public async Task Current_Den_model_registration_remains_verifiable_device_local_evidence()
    {
        var root = NewRoot();
        try
        {
            await using var store = await DenStore.CreateAsync(root, [new("personal", "personal")]);
            var den = new DulcheDen(store, new NamespaceAccessPolicy([new("owner", "personal", DenPermission.Administer)]), "owner");
            var artifact = Path.Combine(root, "model.bin"); await File.WriteAllBytesAsync(artifact, [4]);
            var hash = Convert.ToHexString(SHA256.HashData(new byte[] { 4 }));
            await den.SaveAsync(new ModelRecord { Id = "model", NamespaceId = "personal", DisplayName = "Model",
                ArtifactRevision = "a1", ArtifactSha256 = hash }, 0, "seed");
            var actual = await den.RegisterLocalModelAsync("personal", "model", artifact, hash);
            Assert.Equal(1, actual.Length);
            Assert.NotNull(await new LocalModelRegistry(root, store.Manifest.DeviceId).GetVerifiedAsync("model"));
            Assert.Empty((await den.GetModelByID("model"))!.InstallationReferences);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    private sealed class PublicationPolicy(string root) : IDenAccessPolicy
    {
        public bool Allowed = true; public bool Changed; public Func<Task>? AtPublication;
        public async ValueTask<bool> IsAllowedAsync(string principalId, string namespaceId, string objectId,
            DenPermission permission, CancellationToken ct)
        {
            if (permission != DenPermission.Write || objectId != "model") return true;
            if (!Changed && Directory.GetFiles(root, "*.tmp", SearchOption.AllDirectories).Length > 0)
            { Changed = true; await AtPublication!(); }
            return Allowed;
        }
    }
    private static string NewRoot() => Path.Combine(Path.GetTempPath(), "astra-den-model-" + Guid.NewGuid().ToString("N"));
}
