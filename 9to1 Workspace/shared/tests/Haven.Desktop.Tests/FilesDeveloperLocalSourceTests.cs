using System.Security.Cryptography;
using System.Text.Json;
using HavenOS.Files;

namespace Haven.Desktop.Tests;

public sealed partial class FilesDeveloperImportGuardTests
{
    [Fact]
    public async Task Faulted_OCE_first_final_guard_retains_exact_sibling_causes_and_refuses_folder()
    {
        await using var rig = await Rig.Create();
        var first = new OperationCanceledException("Faulted authority callback, not original cancellation.");
        var second = new IOException("Actual sibling authority fault.");
        var original = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        original.SetException([first, second]);
        var operation = rig.Folder();
        var actual = rig.Drive.CreateOriginalDeveloperFolderAsync(operation, "project", rig.Store,
            [new(rig.Parent.Id, rig.Parent.CurrentRevisionId)], new("test-actor", _ => new(original.Task)), TestContext.Current.CancellationToken);
        var error = await Assert.ThrowsAnyAsync<Exception>(() => actual);
        Assert.True(original.Task.IsFaulted); Assert.True(actual.IsFaulted); Assert.False(actual.IsCanceled);
        Assert.Contains(Causes(error), value => ReferenceEquals(value, first));
        Assert.Contains(Causes(error), value => ReferenceEquals(value, second));
        Assert.False((await rig.Drive.GetAsync(operation.ItemId, TestContext.Current.CancellationToken)).IsSuccess);
    }
    [Fact]
    public async Task Genuine_canceled_final_guard_task_remains_canceled_and_publishes_no_folder()
    {
        await using var rig = await Rig.Create(); using var stopped = new CancellationTokenSource(); stopped.Cancel();
        var original = Task.FromCanceled<bool>(stopped.Token); var operation = rig.Folder();
        var actual = rig.Drive.CreateOriginalDeveloperFolderAsync(operation, "project", rig.Store,
            [new(rig.Parent.Id, rig.Parent.CurrentRevisionId)], new("test-actor", _ => new(original)), TestContext.Current.CancellationToken);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => actual);
        Assert.True(original.IsCanceled); Assert.True(actual.IsCanceled); Assert.False(actual.IsFaulted);
        Assert.False((await rig.Drive.GetAsync(operation.ItemId, TestContext.Current.CancellationToken)).IsSuccess);
    }
    [Fact]
    public async Task Existing_local_metadata_never_publishes_immutable_upload_or_revision_content()
    {
        await using var rig = await Rig.Create(); var source = LocalSource(rig); var beforeSource = await File.ReadAllBytesAsync(rig.ContentPath, TestContext.Current.CancellationToken);
        var actual = await rig.Drive.RegisterOriginalLocalDeveloperFileAsync(source,
            [new(rig.Parent.Id, rig.Parent.CurrentRevisionId)], rig.Store, rig.Guard(true), TestContext.Current.CancellationToken);
        Assert.True(actual.IsSuccess); Assert.Equal(source.RevisionId, actual.Value!.Id);
        var item = (await rig.Drive.GetAsync(source.FileId, TestContext.Current.CancellationToken)).Value!;
        Assert.Equal(source.SizeBytes, item.SizeBytes); Assert.Equal("sha256:" + source.ContentSha256, item.ContentHash);
        var physical = await ReadActualDriveState(rig);
        Assert.Empty(physical.UploadedContents); Assert.Empty(physical.RevisionContentReferences);
        Assert.Equal("local-source-observation", Assert.Single(physical.Revisions).Source);
        Assert.Equal(beforeSource, await File.ReadAllBytesAsync(rig.ContentPath, TestContext.Current.CancellationToken));
        var metadata = await File.ReadAllBytesAsync(rig.DrivePath, TestContext.Current.CancellationToken);
        var repeated = await rig.Drive.RegisterOriginalLocalDeveloperFileAsync(source,
            [new(rig.Parent.Id, rig.Parent.CurrentRevisionId)], rig.Store, rig.Guard(true), TestContext.Current.CancellationToken);
        Assert.False(repeated.IsSuccess); Assert.Equal(FilesErrorCode.InvalidState, repeated.Error!.Code);
        Assert.Equal(metadata, await File.ReadAllBytesAsync(rig.DrivePath, TestContext.Current.CancellationToken));
    }
    [Fact]
    public async Task Changed_final_authority_or_parent_refuses_local_source_without_file_metadata()
    {
        await using var rig = await Rig.Create(); var source = LocalSource(rig);
        var before = await File.ReadAllBytesAsync(rig.DrivePath, TestContext.Current.CancellationToken);
        var authority = await rig.Drive.RegisterOriginalLocalDeveloperFileAsync(source,
            [new(rig.Parent.Id, rig.Parent.CurrentRevisionId)], rig.Store, rig.Guard(false), TestContext.Current.CancellationToken);
        Assert.Equal(FilesErrorCode.PermissionDenied, authority.Error!.Code);
        var parent = await rig.Drive.RegisterOriginalLocalDeveloperFileAsync(source,
            [new(rig.Parent.Id, new FilesRevisionId(Guid.NewGuid()))], rig.Store, rig.Guard(true), TestContext.Current.CancellationToken);
        Assert.Equal(FilesErrorCode.RevisionConflict, parent.Error!.Code);
        Assert.Equal(before, await File.ReadAllBytesAsync(rig.DrivePath, TestContext.Current.CancellationToken));
        Assert.False((await rig.Drive.GetAsync(source.FileId, TestContext.Current.CancellationToken)).IsSuccess);
    }
    private static FilesOriginalLocalDeveloperSource LocalSource(Rig rig) => new(HostedItemId.New(), rig.Parent.Id,
        "main.cs", "text/plain", new FilesRevisionId(Guid.NewGuid()), "test-actor", DateTimeOffset.UtcNow,
        rig.Bytes.Length, Convert.ToHexString(SHA256.HashData(rig.Bytes)).ToLowerInvariant());
    private static async Task<DurableDriveProvider.State> ReadActualDriveState(Rig rig)
    {
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(rig.DrivePath, TestContext.Current.CancellationToken));
        return document.RootElement.GetProperty("state").Deserialize<DurableDriveProvider.State>(new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
    }
}
