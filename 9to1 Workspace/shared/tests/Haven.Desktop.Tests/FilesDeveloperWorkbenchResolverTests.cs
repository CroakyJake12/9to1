using System.Security.Cryptography;
using HavenOS.Apps.Dev;
using HavenOS.Files;
using Microsoft.Extensions.DependencyInjection;

namespace Haven.Desktop.Tests;

public sealed partial class FilesDeveloperIdentityOriginalCallbackTests
{
    [Fact]
    public async Task Registered_workbench_factory_read_is_the_same_global_original_during_close_and_preserves_file_ids()
    {
        await using var rig = await Rig.CreateAsync();
        var actual = rig.Browser.ResolveOriginalDeveloperDocumentAsync(rig.Project, "main.cs", () => true);
        rig._actualReads.Add(actual);
        await rig.Projects.Entered.Task.WaitAsync(Bound, TestContext.Current.CancellationToken);
        var close = rig.Browser.CloseOriginalDeveloperReadsAsync();
        Assert.Same(close, rig.Browser.CloseOriginalDeveloperReadsAsync());
        Assert.False(close.IsCompleted);
        rig.Projects.Release.TrySetResult();
        var observed = await actual.WaitAsync(Bound, TestContext.Current.CancellationToken);
        await close.WaitAsync(Bound, TestContext.Current.CancellationToken);
        Assert.True(observed.Succeeded);
        Assert.Equal(rig.Row.Id.Value, observed.Value!.FileId);
        Assert.Equal(rig.Project.Reference.ProjectId, observed.Value.ProjectId);
        Assert.Equal(rig.Project.Reference.WorkspaceId, observed.Value.WorkspaceId);
        Assert.Equal("main.cs", observed.Value.RelativePath);
        Assert.Equal(rig.Root.DirectoryPath, observed.Value.WorkspaceRoot);
    }

    [Fact]
    public async Task Unknown_workbench_path_does_not_create_a_mapping_file_identity_or_an_import()
    {
        await using var rig = await Rig.CreateAsync();
        var files = System.IO.Directory.GetFiles(rig.Directory, "*", SearchOption.AllDirectories)
            .Where(value => value.EndsWith(".json", StringComparison.Ordinal)).ToDictionary(value => value, File.ReadAllBytes);
        var actual = rig.Browser.ResolveOriginalDeveloperDocumentAsync(rig.Project, "missing.cs", () => true);
        rig._actualReads.Add(actual); rig.ExpectedReadFault = true;
        await rig.Projects.Entered.Task.WaitAsync(Bound, TestContext.Current.CancellationToken);
        rig.Projects.Release.TrySetResult();
        await Assert.ThrowsAnyAsync<Exception>(() => actual);
        foreach (var file in files)
            Assert.Equal(file.Value, await File.ReadAllBytesAsync(file.Key, TestContext.Current.CancellationToken));
        Assert.False(File.Exists(Path.Combine(rig.Root.DirectoryPath, "missing.cs")));
        Assert.Equal(rig.Row.Id.Value, rig.Project.Workspace.OpenEditors.Single().FileId);
    }

    [Fact]
    public async Task Held_factory_read_retains_exact_restored_context_callback_fault_and_refuses_private_disclosure()
    {
        await using var rig = await Rig.CreateAsync();
        var previous = ExecutionContext.Capture()!;
        var exact = new OperationCanceledException("Actual factory view predicate; no canceled repository Task.");
        var fail = false; var guarded = 0; Task? wronglyReturned = null;
        var actual = rig.Browser.ResolveOriginalDeveloperDocumentAsync(rig.Project, "main.cs", () =>
        {
            if (!fail) return true;
            ExecutionContext.Run(previous, _ =>
            {
                try { wronglyReturned = rig.Browser.CloseOriginalDeveloperReadsAsync(); }
                catch (InvalidOperationException) { guarded++; }
            }, null);
            throw exact;
        });
        rig._actualReads.Add(actual); rig.ExpectedReadFault = true;
        await rig.Projects.Entered.Task.WaitAsync(Bound, TestContext.Current.CancellationToken);
        fail = true; rig.Projects.Release.TrySetResult();
        await Assert.ThrowsAnyAsync<Exception>(() => actual);
        var observed = await Assert.ThrowsAnyAsync<Exception>(() => rig.Browser.CloseOriginalDeveloperReadsAsync());
        Assert.Contains(AllCauses(observed), value => ReferenceEquals(value, exact));
        Assert.True(actual.IsFaulted);
        Assert.Null(wronglyReturned);
        Assert.True(guarded > 0);
    }

    [Fact]
    public async Task Registered_canonical_subfolder_is_resolved_from_saved_file_ids_without_path_created_identity()
    {
        await using var rig = await Rig.CreateAsync();
        rig.Projects.Release.TrySetResult();
        var workspace = (await rig.Graph.GetRequiredService<HavenOS.Files.NativeHost.NativeFilesWorkspaceAuthority>().GetCurrentAsync())!;
        var now = DateTimeOffset.UtcNow; var child = new HostedItemId(Guid.NewGuid());
        var folder = new FilesOperation(new(Guid.NewGuid()), rig.Actor.ActorId, child, null, rig.Root.FolderId,
            "CreateFolder", null, null, FilesOperationState.Pending, now, now, null, null);
        Assert.True((await workspace.Provider.MutateAsync(folder, "src", default)).IsSuccess);
        var directory = Path.Combine(rig.Root.DirectoryPath, "src"); System.IO.Directory.CreateDirectory(directory);
        var bytes = System.Text.Encoding.UTF8.GetBytes("actual nested source\n");
        var id = new HostedItemId(Guid.NewGuid()); var revision = new FilesRevisionId(Guid.NewGuid());
        var hash = "sha256:" + Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        await File.WriteAllBytesAsync(Path.Combine(directory, "nested.cs"), bytes, TestContext.Current.CancellationToken);
        Assert.True((await workspace.Provider.CommitUploadedContentAsync(new(id, child, "nested.cs", "text/plain", revision,
            null, rig.Actor.ActorId, now, bytes.Length, hash, "fixtures/nested-source"))).IsSuccess);
        await workspace.Materializations.RegisterValidatedAsync(Path.Combine(directory, "nested.cs"), new(id, revision, hash,
            bytes.Length, now), SyncAvailability.AvailableOffline, TestContext.Current.CancellationToken);
        var old = rig.Project.Workspace;
        var updated = (await rig.Projects.SaveAsync(old with
        { OpenEditors = [.. old.OpenEditors, new DeveloperOpenEditor(id.Value, rig.Project.Reference.ProjectId, "files:" + id, "main", 0, false, false)] }, old.Revision,
            TestContext.Current.CancellationToken)).Value!;
        var reference = rig.Project.Reference with { WorkspaceRevision = updated.Revision };
        rig.Project = rig.Project with { Reference = reference, Workspace = updated };
        var actual = rig.Browser.ResolveOriginalDeveloperDocumentAsync(rig.Project, "src/nested.cs", () => true);
        rig._actualReads.Add(actual);
        var observed = await actual.WaitAsync(Bound, TestContext.Current.CancellationToken);
        Assert.True(observed.Succeeded);
        Assert.Equal(id.Value, observed.Value!.FileId);
        Assert.Equal("src/nested.cs", observed.Value.RelativePath.Replace('\\', '/'));
        Assert.Equal(updated.Revision, observed.Value.WorkspaceRevision);
        Assert.Equal(id.Value, updated.OpenEditors.Single(value => value.FileId == id.Value).FileId);
    }
}
