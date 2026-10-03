using Haven.Application;
using Haven.Application.Shelf;
using Haven.Core.Shelf;
namespace Haven.Infrastructure.Tests;
/// <summary>Actual physical transaction seam; the controlled admission is not a Home grant.</summary>
public sealed class ShelfItemEditCommitAdmissionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Actual_pin_tags_and_order_edit_requires_admission_and_keeps_original_target(bool allowed)
    {
        using var paths = new Paths();
        var settings = new VersionedAtomicSettingsStore(paths);
        var service = new ShelfLibraryService(settings);
        var item = new ShelfLaunchItem(Guid.NewGuid(), "Original", new(ShelfTargetKind.InstalledApplication, "test.retained"));
        Assert.True((await service.ImportAsync(0, new(ShelfLibrary.Empty with { Items = [item] }, [], []))).Success);
        var identity = await settings.GetStoreIdentityAsync(default);
        var receipt = new ShelfOwnedMutationReceipt(1, Guid.NewGuid(), identity.StoreId, 1, new string('B', 64));
        var file = Path.Combine(paths.DataDirectory, "settings.json");
        var before = await File.ReadAllBytesAsync(file);
        var admission = new Admission(allowed);
        var result = await service.EditItemAsync(1, item.Id, "Pinned original", ["work", "reference"], true, 7,
            ShelfLaunchBehaviour.Open, admission, receipt, default);
        Assert.Equal(allowed, result.Success); Assert.True(admission.Checks > 0);
        var reopened = await new ShelfLibraryService(new VersionedAtomicSettingsStore(paths)).ReadAsync();
        var actual = Assert.Single(reopened.Library.Items);
        Assert.Equal(item.Id, actual.Id); Assert.Equal(item.Target, actual.Target);
        if (!allowed)
        {
            Assert.Equal("CommitDenied", result.ErrorCode); Assert.Equal(before, await File.ReadAllBytesAsync(file));
            Assert.Equal(1, reopened.Library.Revision); Assert.Null(reopened.LastOwnedMutation);
            Assert.Equal(item.Name, actual.Name); Assert.False(actual.IsFavourite); Assert.Equal(item.Order, actual.Order);
        }
        else
        {
            Assert.Equal(2, reopened.Library.Revision); Assert.Equal(receipt, reopened.LastOwnedMutation);
            Assert.Equal("Pinned original", actual.Name); Assert.True(actual.IsFavourite);
            Assert.Equal(new[] { "work", "reference" }, actual.Tags); Assert.Equal(7, actual.Order);
            var committed = await File.ReadAllBytesAsync(file);
            var stale = await service.EditItemAsync(1, item.Id, "Stale", [], false, 0,
                ShelfLaunchBehaviour.Open, admission, receipt, default);
            Assert.False(stale.Success); Assert.Equal("RevisionConflict", stale.ErrorCode);
            Assert.Equal(committed, await File.ReadAllBytesAsync(file));
        }
    }
    [Fact]
    public async Task Tags_are_captured_before_actual_publication_wait_and_later_caller_mutation_is_not_saved()
    {
        using var paths = new Paths(); var settings = new VersionedAtomicSettingsStore(paths);
        var service = new ShelfLibraryService(settings);
        var item = new ShelfLaunchItem(Guid.NewGuid(), "Original", new(ShelfTargetKind.InstalledApplication, "test.retained"));
        Assert.True((await service.ImportAsync(0, new(ShelfLibrary.Empty with { Items = [item] }, [], []))).Success);
        var identity = await settings.GetStoreIdentityAsync(default);
        var receipt = new ShelfOwnedMutationReceipt(1, Guid.NewGuid(), identity.StoreId, 1, new string('C', 64));
        var tags = new[] { "original" }; var admission = new HeldAdmission();
        var pending = service.EditItemAsync(1, item.Id, "Pinned", tags, true, 3, ShelfLaunchBehaviour.Open, admission, receipt, default);
        try
        {
            await admission.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            tags[0] = "replacement"; admission.Release.TrySetResult();
            Assert.True((await pending).Success);
            var reopened = await new ShelfLibraryService(new VersionedAtomicSettingsStore(paths)).ReadAsync();
            Assert.Equal(new[] { "original" }, Assert.Single(reopened.Library.Items).Tags);
            Assert.Equal(receipt, reopened.LastOwnedMutation);
        }
        catch
        {
            admission.Release.TrySetResult(); try { await pending; } catch { }
            throw;
        }
        finally { admission.Release.TrySetResult(); }
    }
    private sealed class HeldAdmission : ISettingsCommitAdmission
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async ValueTask<bool> CheckAsync(SettingsCommitContext context, CancellationToken token)
        {
            if (context.Phase == SettingsCommitPhase.Publication)
            { Entered.TrySetResult(); await Release.Task.WaitAsync(token); }
            return true;
        }
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
