using HavenOS.Files;

namespace Haven.Desktop.Tests;

public sealed class FilesStoreEvidenceTests
{
    [Fact]
    public async Task Actual_state_UUID_creation_proof_is_stable_and_never_claims_reopened_legacy_or_foreign_data()
    {
        var root = Path.Combine(Path.GetTempPath(), "astra-files-identity-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var token = TestContext.Current.CancellationToken;
            var path = Path.Combine(root, "drive.json");
            var location = new FilesLocationId(Guid.NewGuid());
            const string owner = "actual-trusted-fixture-principal";
            var first = new DurableDriveProvider(path, location, owner);
            var original = await first.GetStoreEvidenceAsync(token);
            Assert.NotEqual(Guid.Empty, original.StoreId);
            Assert.True(original.NewlyCreated);
            Assert.True(original.IsEmpty);
            Assert.Equal(original, await first.GetStoreEvidenceAsync(token));
            var reopened = new DurableDriveProvider(path, location, owner);
            var evidence = await reopened.GetStoreEvidenceAsync(token);
            Assert.Equal(original.StoreId, evidence.StoreId);
            Assert.Equal(original.Revision, evidence.Revision);
            Assert.False(evidence.NewlyCreated);
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => new DurableDriveProvider(path, location, "foreign").GetStoreEvidenceAsync(token));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => new DurableDriveProvider(path, new(Guid.NewGuid()), owner).GetStoreEvidenceAsync(token));
            var now = DateTimeOffset.UtcNow;
            var create = new FilesOperation(new(Guid.NewGuid()), owner, HostedItemId.New(), null, null, "CreateFolder", null, null,
                FilesOperationState.Pending, now, now, null, null);
            Assert.True((await reopened.MutateAsync(create, "User folder", token)).IsSuccess);
            var changed = await reopened.GetStoreEvidenceAsync(token);
            Assert.Equal(original.StoreId, changed.StoreId);
            Assert.False(changed.IsEmpty);
            Assert.NotEqual(original.Revision, changed.Revision);
            var legacyPath = Path.Combine(root, "legacy.json");
            await new VersionedJsonStateStore<DurableDriveProvider.State>(legacyPath, 1, () => new([], [], [])).UpdateAsync(state => state, token);
            var legacy = await new DurableDriveProvider(legacyPath, location, owner).GetStoreEvidenceAsync(token);
            Assert.False(legacy.NewlyCreated);
            Assert.True(legacy.IsEmpty);
            Assert.NotEqual(original.StoreId, legacy.StoreId);
            Assert.Equal(legacy.StoreId, (await new DurableDriveProvider(legacyPath, location, owner).GetStoreEvidenceAsync(token)).StoreId);
        }
        finally { Directory.Delete(root, true); }
    }
}
