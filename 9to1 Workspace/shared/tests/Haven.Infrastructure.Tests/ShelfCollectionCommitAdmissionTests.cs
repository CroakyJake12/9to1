using Haven.Application;
using Haven.Application.Shelf;
using Haven.Core.Shelf;

namespace Haven.Infrastructure.Tests;

/// <summary>Real physical Settings transaction seam coverage; controlled admission is not Home approval.</summary>
public sealed class ShelfCollectionCommitAdmissionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Collection_mutations_require_final_admission_and_preserve_physical_root_on_refusal(bool membership)
    {
        using var paths = new Paths();
        var settings = new VersionedAtomicSettingsStore(paths);
        var service = new ShelfLibraryService(settings);
        var item = new ShelfLaunchItem(Guid.NewGuid(), "Actual retained app", new(ShelfTargetKind.InstalledApplication, "test.retained"));
        var collection = new ShelfCollection(Guid.NewGuid(), "Actual manual collection", ShelfCollectionKind.Manual);
        var seed = ShelfLibrary.Empty with { Items = [item], Collections = membership ? [collection] : [] };
        Assert.True((await service.ImportAsync(0, new(seed, [], []))).Success);
        var identity = (await settings.ExportAsync(default)).StoreIdentity!;
        var receipt = new ShelfOwnedMutationReceipt(1, Guid.NewGuid(), identity.StoreId, 1, new string('A', 64));
        var file = Path.Combine(paths.DataDirectory, "settings.json"); var original = await File.ReadAllBytesAsync(file);
        var denied = new Admission(false);
        var outcome = membership
            ? await service.AddMembershipAsync(1, collection.Id, item.Id, 0, denied, receipt, default)
            : await service.CreateCollectionAsync(1, collection, denied, receipt, default);
        Assert.False(outcome.Success); Assert.Equal("CommitDenied", outcome.ErrorCode); Assert.True(denied.Checks > 0);
        Assert.Equal(original, await File.ReadAllBytesAsync(file));
        var reopened = await new ShelfLibraryService(new VersionedAtomicSettingsStore(paths)).ReadAsync();
        Assert.Equal(1, reopened.Library.Revision); Assert.Null(reopened.LastOwnedMutation);
        if (membership) Assert.Empty(reopened.Library.Memberships); else Assert.Empty(reopened.Library.Collections);
        var allowed = new Admission(true);
        var committed = membership
            ? await service.AddMembershipAsync(1, collection.Id, item.Id, 0, allowed, receipt, default)
            : await service.CreateCollectionAsync(1, collection, allowed, receipt, default);
        Assert.True(committed.Success, committed.ErrorCode); Assert.True(allowed.Checks > 0);
        reopened = await new ShelfLibraryService(new VersionedAtomicSettingsStore(paths)).ReadAsync();
        Assert.Equal(2, reopened.Library.Revision); Assert.Equal(receipt, reopened.LastOwnedMutation);
        if (membership) { var actual = Assert.Single(reopened.Library.Memberships); Assert.Equal(collection.Id, actual.CollectionId); Assert.Equal(item.Id, actual.LaunchItemId); }
        else Assert.Equal(collection.Id, Assert.Single(reopened.Library.Collections).Id);
        var after = await File.ReadAllBytesAsync(file);
        var stale = membership
            ? await service.AddMembershipAsync(1, collection.Id, item.Id, 0, allowed, receipt, default)
            : await service.CreateCollectionAsync(1, collection, allowed, receipt, default);
        Assert.False(stale.Success); Assert.Equal("RevisionConflict", stale.ErrorCode);
        Assert.Equal(after, await File.ReadAllBytesAsync(file));
    }
    private sealed class Admission(bool allowed) : ISettingsCommitAdmission
    {
        public int Checks { get; private set; }
        public ValueTask<bool> CheckAsync(SettingsCommitContext context, CancellationToken token)
        { token.ThrowIfCancellationRequested(); Checks++; return ValueTask.FromResult(allowed); }
    }
    private sealed class Paths : IAppPaths, IDisposable
    {
        public string DataDirectory { get; } = Directory.CreateTempSubdirectory("astra-shelf-collection-admission-").FullName;
        public string DatabasePath => Path.Combine(DataDirectory, "data.db");
        public string BrowserProfileDirectory => Path.Combine(DataDirectory, "browser");
        public string AttachmentsDirectory => Path.Combine(DataDirectory, "attachments");
        public string LogsDirectory => Path.Combine(DataDirectory, "logs");
        public string LegacyStatePath => Path.Combine(DataDirectory, "legacy.json");
        public void Dispose() => Directory.Delete(DataDirectory, true);
    }
}
