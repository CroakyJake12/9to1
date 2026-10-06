using System.Security.Cryptography;
using System.Text;
using HavenOS.Files;

namespace Haven.Desktop.Tests;

/// <summary>Real Files JSON/provider and FileStream controls with a synthetic final predicate.
/// They establish final guard, stable identity and create-only metadata behavior, not Home approval,
/// trusted path/handle provenance, native source capture or completed project import.</summary>
public sealed partial class FilesDeveloperImportGuardTests
{
    [Fact]
    public async Task Folder_guard_refusal_keeps_actual_store_bytes_and_does_not_publish_item()
    {
        await using var rig = await Rig.Create();
        var before = await File.ReadAllBytesAsync(rig.DrivePath, TestContext.Current.CancellationToken);
        var operation = rig.Folder();
        var refused = await rig.Drive.CreateOriginalDeveloperFolderAsync(operation, "project", rig.Store,
            [new(rig.Parent.Id, rig.Parent.CurrentRevisionId)], rig.Guard(false), TestContext.Current.CancellationToken);
        Assert.False(refused.IsSuccess);
        Assert.Equal(FilesErrorCode.PermissionDenied, refused.Error!.Code);
        Assert.Equal(before, await File.ReadAllBytesAsync(rig.DrivePath, TestContext.Current.CancellationToken));
        Assert.False((await rig.Drive.GetAsync(operation.ItemId, TestContext.Current.CancellationToken)).IsSuccess);
    }

    [Fact]
    public async Task Wrong_original_store_and_changed_parent_refuse_without_new_folder()
    {
        await using var rig = await Rig.Create();
        var operation = rig.Folder();
        var before = await File.ReadAllBytesAsync(rig.DrivePath, TestContext.Current.CancellationToken);
        var wrongStore = await rig.Drive.CreateOriginalDeveloperFolderAsync(operation, "project", Guid.NewGuid(),
            [new(rig.Parent.Id, rig.Parent.CurrentRevisionId)], rig.Guard(true), TestContext.Current.CancellationToken);
        Assert.Equal(FilesErrorCode.RevisionConflict, wrongStore.Error!.Code);
        Assert.Equal(before, await File.ReadAllBytesAsync(rig.DrivePath, TestContext.Current.CancellationToken));
        var changedParent = await rig.Drive.CreateOriginalDeveloperFolderAsync(operation, "project", rig.Store,
            [new(rig.Parent.Id, new FilesRevisionId(Guid.NewGuid()))], rig.Guard(true), TestContext.Current.CancellationToken);
        Assert.Equal(FilesErrorCode.RevisionConflict, changedParent.Error!.Code);
        Assert.False((await rig.Drive.GetAsync(operation.ItemId, TestContext.Current.CancellationToken)).IsSuccess);
    }

    [Fact]
    public async Task Actual_folder_commit_retains_once_selected_item_and_refuses_repeated_original_step()
    {
        await using var rig = await Rig.Create();
        var operation = rig.Folder();
        var first = await rig.Drive.CreateOriginalDeveloperFolderAsync(operation, "project", rig.Store,
            [new(rig.Parent.Id, rig.Parent.CurrentRevisionId)], rig.Guard(true), TestContext.Current.CancellationToken);
        Assert.True(first.IsSuccess);
        Assert.Equal(FilesOperationState.Committed, first.Value!.State);
        Assert.Equal(operation.Id, first.Value.Id);
        var item = await rig.Drive.GetAsync(operation.ItemId, TestContext.Current.CancellationToken);
        Assert.Equal(operation.ItemId, item.Value!.Id);
        Assert.Equal(first.Value.ResultRevisionId, item.Value.CurrentRevisionId);
        var second = await rig.Drive.CreateOriginalDeveloperFolderAsync(operation, "project", rig.Store,
            [new(rig.Parent.Id, rig.Parent.CurrentRevisionId)], rig.Guard(true), TestContext.Current.CancellationToken);
        Assert.Equal(FilesErrorCode.InvalidState, second.Error!.Code);
        Assert.Equal(item.Value, (await rig.Drive.GetAsync(operation.ItemId, TestContext.Current.CancellationToken)).Value);
    }

    [Fact]
    public async Task Materialization_final_guard_refuses_mapping_and_keeps_borrowed_actual_handle_open()
    {
        await using var rig = await Rig.Create();
        await using var content = rig.OpenContent();
        var proof = rig.Proof();
        var refusal = await Assert.ThrowsAnyAsync<Exception>(() => rig.Mappings.RegisterOriginalDeveloperMaterializationAsync(
            rig.ContentPath, content, proof, SyncAvailability.AvailableOffline, rig.Guard(false), TestContext.Current.CancellationToken));
        Assert.Contains(Causes(refusal), value => value is UnauthorizedAccessException);
        Assert.True(content.CanRead);
        Assert.False(File.Exists(rig.MappingPath));
        Assert.Equal(rig.Bytes, await File.ReadAllBytesAsync(rig.ContentPath, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Materialization_commits_exact_item_revision_hash_and_create_only_refuses_second_mapping()
    {
        await using var rig = await Rig.Create();
        await using var content = rig.OpenContent();
        var proof = rig.Proof();
        var actual = await rig.Mappings.RegisterOriginalDeveloperMaterializationAsync(rig.ContentPath, content, proof,
            SyncAvailability.AvailableOffline, rig.Guard(true), TestContext.Current.CancellationToken);
        Assert.Same(proof, actual);
        var reopened = new FilesMaterializationRegistry(rig.Root, rig.MappingPath);
        var mapping = await reopened.GetExistingByItemIdAsync(proof.ItemId, TestContext.Current.CancellationToken);
        Assert.Equal(proof.RemoteRevisionId, mapping!.BaseRemoteRevisionId);
        Assert.Equal(proof.ContentHash, mapping.ContentHash);
        Assert.Equal(rig.ContentPath, mapping.LocalPath);
        var before = await File.ReadAllBytesAsync(rig.MappingPath, TestContext.Current.CancellationToken);
        await Assert.ThrowsAnyAsync<Exception>(() => rig.Mappings.RegisterOriginalDeveloperMaterializationAsync(rig.ContentPath,
            content, proof, SyncAvailability.AvailableOffline, rig.Guard(true), TestContext.Current.CancellationToken));
        Assert.Equal(before, await File.ReadAllBytesAsync(rig.MappingPath, TestContext.Current.CancellationToken));
        Assert.True(content.CanRead);
    }

    [Fact]
    public async Task Materialization_actual_hash_mismatch_refuses_before_registry_publication()
    {
        await using var rig = await Rig.Create();
        await using var content = rig.OpenContent();
        var proof = rig.Proof() with { ContentHash = "sha256:" + new string('a', 64) };
        await Assert.ThrowsAsync<InvalidDataException>(() => rig.Mappings.RegisterOriginalDeveloperMaterializationAsync(
            rig.ContentPath, content, proof, SyncAvailability.AvailableOffline, rig.Guard(true), TestContext.Current.CancellationToken));
        Assert.False(File.Exists(rig.MappingPath));
        Assert.True(content.CanRead);
    }

    private static IEnumerable<Exception> Causes(Exception observed)
    {
        yield return observed;
        if (observed is AggregateException group)
            foreach (var original in group.InnerExceptions)
                foreach (var cause in Causes(original)) yield return cause;
    }

    private sealed class Rig : IAsyncDisposable
    {
        internal string Directory = null!;
        internal string Root = null!;
        internal string DrivePath = null!;
        internal string MappingPath = null!;
        internal string ContentPath = null!;
        internal readonly byte[] Bytes = Encoding.UTF8.GetBytes("actual source file\n");
        internal DurableDriveProvider Drive = null!;
        internal FilesMaterializationRegistry Mappings = null!;
        internal HostedItemMetadata Parent = null!;
        internal Guid Store;
        internal static async Task<Rig> Create()
        {
            var rig = new Rig { Directory = Path.Combine(Path.GetTempPath(), "astra-files-dev-import-" + Guid.NewGuid().ToString("N")) };
            System.IO.Directory.CreateDirectory(rig.Directory);
            try
            {
                rig.Root = Path.Combine(rig.Directory, "root"); System.IO.Directory.CreateDirectory(rig.Root);
                rig.DrivePath = Path.Combine(rig.Directory, "drive.json"); rig.MappingPath = Path.Combine(rig.Directory, "mapping.json");
                rig.ContentPath = Path.Combine(rig.Root, "main.cs");
                await File.WriteAllBytesAsync(rig.ContentPath, rig.Bytes, TestContext.Current.CancellationToken);
                rig.Drive = new(rig.DrivePath, new FilesLocationId(Guid.NewGuid()), "test-actor");
                rig.Store = (await rig.Drive.GetStoreEvidenceAsync(TestContext.Current.CancellationToken)).StoreId;
                var initial = rig.Folder() with { DestinationParentId = null };
                Assert.True((await rig.Drive.MutateAsync(initial, "destination", TestContext.Current.CancellationToken)).IsSuccess);
                rig.Parent = (await rig.Drive.GetAsync(initial.ItemId, TestContext.Current.CancellationToken)).Value!;
                rig.Mappings = new(rig.Root, rig.MappingPath); return rig;
            }
            catch { await rig.DisposeAsync(); throw; }
        }
        internal FilesOperation Folder()
        {
            var now = DateTimeOffset.UtcNow;
            return new(new FilesOperationId(Guid.NewGuid()), "test-actor", HostedItemId.New(), null, Parent?.Id,
                "CreateFolder", null, null, FilesOperationState.Pending, now, now, null, null);
        }
        internal FilesCommitAuthorityGuard Guard(bool current) => new("test-actor", _ => new(current));
        internal FileStream OpenContent() => new(ContentPath, FileMode.Open, FileAccess.Read, FileShare.Read,
            4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
        internal FilesMaterializationProof Proof() => new(HostedItemId.New(), new FilesRevisionId(Guid.NewGuid()),
            "sha256:" + Convert.ToHexString(SHA256.HashData(Bytes)).ToLowerInvariant(), Bytes.Length, DateTimeOffset.UtcNow);
        public ValueTask DisposeAsync() { System.IO.Directory.Delete(Directory, true); return ValueTask.CompletedTask; }
    }
}
