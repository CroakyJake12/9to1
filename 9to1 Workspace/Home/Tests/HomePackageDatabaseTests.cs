using System.Text.Json;
using HavenOS.Home.Apps;
using HavenOS.Home.Core;
using Xunit;

namespace HavenOS.Home.Tests;

public sealed class HomePackageDatabaseTests
{
    [Fact]
    public async Task Save_and_read_use_versioned_canonical_device_local_record()
    {
        var store = new FakeStateStore();
        var database = new HomePackageDatabase(store);
        var package = Package("sample.app");

        var saved = await database.SaveAsync(new HomePackageDatabaseSnapshot(1, 0,
            DateTimeOffset.UnixEpoch, [package], []), expectedRevision: 0);
        var read = await database.ReadAsync();

        Assert.True(saved.Succeeded);
        Assert.Equal(1, saved.Snapshot!.Revision);
        Assert.True(read.Succeeded);
        Assert.Equal("sample.app", Assert.Single(read.Snapshot!.Packages).PackageId);
        var record = Assert.Single(store.State!.Records);
        Assert.Equal("Home.PackageRegistry", record.RecordType);
        Assert.Equal(HomeDataScope.DeviceLocal, record.Scope);
        Assert.Equal(HomeRecordAuthority.LocalCanonical, record.Authority);
        Assert.Equal(1, record.SchemaVersion);
    }

    [Fact]
    public async Task Compare_and_write_rejects_stale_revision_without_overwriting()
    {
        var store = new FakeStateStore();
        var database = new HomePackageDatabase(store);
        var initial = new HomePackageDatabaseSnapshot(1, 0, DateTimeOffset.UnixEpoch, [], []);
        Assert.True((await database.SaveAsync(initial, 0)).Succeeded);

        var stale = await database.SaveAsync(initial with { Packages = [Package("stale")] }, expectedRevision: 0);
        var current = await database.ReadAsync();

        Assert.False(stale.Succeeded);
        Assert.Equal($"HomeCore.{HomeCoreErrorCode.HomeStateConflict}", stale.Failure!.Code);
        Assert.Empty(current.Snapshot!.Packages);
    }

    [Fact]
    public async Task Duplicate_package_ids_and_journal_keys_are_rejected_before_write()
    {
        var store = new FakeStateStore();
        var database = new HomePackageDatabase(store);
        var package = Package("duplicate");

        var duplicatePackages = await database.SaveAsync(new(1, 0, DateTimeOffset.UnixEpoch,
            [package, package], []), expectedRevision: 0);
        var operation = new HomePackageJournalEntry("same-key", "op-1", package.PackageId, "Install",
            HomePackageJournalState.Pending, DateTimeOffset.UtcNow, null, "Pending", true, true, [], [], [], []);
        var duplicateKeys = await database.SaveAsync(new(1, 0, DateTimeOffset.UnixEpoch,
            [package], [operation, operation with { OperationId = "op-2" }]), expectedRevision: 0);

        Assert.Equal("HomePackages.DuplicatePackageId", duplicatePackages.Failure!.Code);
        Assert.Equal("HomePackages.DuplicateIdempotencyKey", duplicateKeys.Failure!.Code);
        Assert.Null(store.State);
    }

    [Fact]
    public async Task Stale_snapshot_and_oversized_journal_are_rejected_without_truncation()
    {
        var store = new FakeStateStore();
        var database = new HomePackageDatabase(store);
        var stale = await database.SaveAsync(new(1, 4, DateTimeOffset.UnixEpoch, [], []), expectedRevision: 0);
        var tooManyOperations = Enumerable.Range(0, HomePackageDatabase.MaximumJournalEntries + 1)
            .Select(index => new HomePackageJournalEntry($"key-{index}", $"op-{index}", "package", "Install",
                HomePackageJournalState.Succeeded, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch,
                "Succeeded", false, true, [], [], [], []))
            .ToArray();
        var oversized = await database.SaveAsync(new(1, 0, DateTimeOffset.UnixEpoch, [], tooManyOperations), 0);

        Assert.Equal("HomePackages.StaleSnapshot", stale.Failure!.Code);
        Assert.Equal("HomePackages.OperationJournalLimit", oversized.Failure!.Code);
        Assert.Null(store.State);
    }

    private static HomePackageDatabaseEntry Package(string id) => new(id, null, id, null,
        null, "1.0", "stable", HomePackageInstallState.Available, HomePackageCompatibility.Unknown,
        null, [], [], null, [], PreservesOwnedArtifactsOnUninstall: true, Revision: 0);

    private sealed class FakeStateStore : IHomeCoreStateStore
    {
        public HomeCoreStoredState? State { get; private set; }

        public Task<HomeStateReadResult> ReadAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(HomeStateReadResult.Success(State ?? new HomeCoreStoredState(1, 0, [])));
        }

        public Task<HomeStateWriteResult> WriteAsync(HomeCoreStateRecord record, long expectedRecordRevision,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var current = State?.Records.SingleOrDefault(item => item.RecordId == record.RecordId);
            var currentRevision = current?.Revision ?? 0;
            if (currentRevision != expectedRecordRevision)
                return Task.FromResult(HomeStateWriteResult.Failed(new(HomeCoreErrorCode.HomeStateConflict,
                    "The expected record revision is stale.", record.RecordId, false)));

            var records = State?.Records.Where(item => item.RecordId != record.RecordId).ToList() ?? [];
            records.Add(record);
            State = new HomeCoreStoredState(1, (State?.Revision ?? 0) + 1, records);
            return Task.FromResult(HomeStateWriteResult.Success(State));
        }
    }
}
