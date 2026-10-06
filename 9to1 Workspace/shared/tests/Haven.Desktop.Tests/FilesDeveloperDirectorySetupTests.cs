using HavenOS.Files;

namespace Haven.Desktop.Tests;

/// <summary>Actual Files JSON/provider/binding with synthetic native/final guards. These
/// observations are not Home approval, trusted native handle identity or completed setup.</summary>
public sealed partial class FilesDeveloperDirectorySetupTests
{
    [Fact]
    public async Task Genuine_original_metadata_binds_once_and_second_registration_refuses_without_rewrite()
    {
        await using var rig = await Rig.Create(); var retained = new List<Task>();
        var first = await rig.Register(() => ValueTask.FromResult(true), retained.Add);
        Assert.True(first.IsSuccess); Assert.Equal(rig.Folder.Id, first.Value!.FolderId);
        Assert.Equal(rig.Root, first.Value.DirectoryPath); Assert.Equal(rig.Profile, first.Value.ProfileId);
        Assert.Equal("dev.project." + rig.Project.ToString("N"), first.Value.OwningAppId);
        Assert.NotEmpty(retained); Assert.All(retained, value => Assert.True(value.IsCompleted));
        var before = await File.ReadAllBytesAsync(rig.Bindings, TestContext.Current.CancellationToken);
        await Assert.ThrowsAnyAsync<Exception>(() => rig.Register(() => ValueTask.FromResult(true), retained.Add));
        Assert.Equal(before, await File.ReadAllBytesAsync(rig.Bindings, TestContext.Current.CancellationToken));
    }
    [Fact]
    public async Task Actual_held_final_native_check_retains_raw_task_and_no_binding_precedes_its_acknowledgement()
    {
        await using var rig = await Rig.Create(); var retained = new List<Task>();
        var raw = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); Task? actual = null;
        try
        {
            actual = rig.Register(() => { entered.TrySetResult(); return new(raw.Task); }, retained.Add);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            Assert.False(actual.IsCompleted); Assert.False(File.Exists(rig.Bindings));
            raw.TrySetResult(true); await actual.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            Assert.Contains(retained, value => ReferenceEquals(value, raw.Task)); Assert.True(File.Exists(rig.Bindings));
        }
        finally
        {
            raw.TrySetResult(true); if (actual is not null) await actual;
        }
    }
    private sealed class Rig : IAsyncDisposable
    {
        internal string Base = null!, Root = null!, Bindings = null!; internal Guid Profile = Guid.NewGuid(), Project = Guid.NewGuid(), Store;
        internal DurableDriveProvider Drive = null!; internal FilesWorkspaceDirectoryResolver Resolver = null!; internal HostedItemMetadata Folder = null!;
        internal static async Task<Rig> Create()
        {
            var rig = new Rig { Base = Path.Combine(Path.GetTempPath(), "astra-dev-directory-" + Guid.NewGuid().ToString("N")) };
            Directory.CreateDirectory(rig.Base);
            try
            {
                rig.Root = Path.Combine(rig.Base, "original-project"); Directory.CreateDirectory(rig.Root); rig.Bindings = Path.Combine(rig.Base, "bindings.json");
                rig.Drive = new(Path.Combine(rig.Base, "drive.json"), new FilesLocationId(Guid.NewGuid()), "local-profile:" + rig.Profile.ToString("D"));
                rig.Store = (await rig.Drive.GetStoreEvidenceAsync(TestContext.Current.CancellationToken)).StoreId;
                var now = DateTimeOffset.UtcNow; var create = new FilesOperation(new FilesOperationId(Guid.NewGuid()), "local-profile:" + rig.Profile.ToString("D"),
                    HostedItemId.New(), null, null, "CreateFolder", null, null, FilesOperationState.Pending, now, now, null, null);
                Assert.True((await rig.Drive.MutateAsync(create, "project", TestContext.Current.CancellationToken)).IsSuccess);
                rig.Folder = (await rig.Drive.GetAsync(create.ItemId, TestContext.Current.CancellationToken)).Value!;
                rig.Resolver = new(rig.Bindings, _ => null, id => id == rig.Profile ? rig.Drive : null); return rig;
            }
            catch { await rig.DisposeAsync(); throw; }
        }
        internal Task<FilesResult<FilesWorkspaceDirectoryBinding>> Register(Func<ValueTask<bool>> native, Action<Task> retain, Action<Action>? physicalScope = null) =>
            Resolver.RegisterOriginalDeveloperProfileAsync(Profile, Project, Folder.Id, Folder.CurrentRevisionId!.Value, Root, Drive, Store,
                new("local-profile:" + Profile.ToString("D"), _ => ValueTask.FromResult(true)), _ => native(), physicalScope ?? (action => action()), retain, TestContext.Current.CancellationToken);
        public ValueTask DisposeAsync() { Directory.Delete(Base, true); return ValueTask.CompletedTask; }
    }
}
