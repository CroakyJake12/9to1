using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Haven.Application;
using HavenOS.Files;
using HavenOS.Files.NativeHost;
using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;

namespace Haven.Desktop.Tests;

public sealed class FilesOriginalCanonicalReadTests
{
    [Fact]
    public async Task Genuine_original_read_requires_same_registered_issuer_actor_file_action_revision_and_private_workspace_composition()
    {
        using var f = await Fixture.CreateAsync(); var ct = TestContext.Current.CancellationToken;
        var original = await f.Resolver.CaptureOriginalCanvasReadAsync(f.Workspace, f.File, () => true, ct);
        Assert.Equal(f.Workspace.Configuration.AppFolders["canvas"].Value, original.OriginalMaterializationFolderId);
        var before = await File.ReadAllBytesAsync(f.HomeFile, ct); var drive = await File.ReadAllBytesAsync(f.DriveFile, ct);
        Assert.True(f.Resources.IsIssuedOriginalReadOwnerBinding(f.Workspace.Actor, original, "canvas.file.open", f.Scope, f.Workspace.Provider, f.Workspace.Directories, f.Workspace.Configuration.StoreId));
        Assert.False(f.Resources.IsIssuedOriginalReadOwnerBinding(f.Workspace.Actor, original, "canvas.file.open", f.Scope, new object(), f.Workspace.Directories, f.Workspace.Configuration.StoreId));
        Assert.False(f.Resources.IsIssuedOriginalReadOwnerBinding(f.Workspace.Actor, original, "canvas.file.open", f.Scope, f.Workspace.Provider, new object(), f.Workspace.Configuration.StoreId));
        Assert.Equal(f.Workspace.Actor, await f.Resources.AuthorizeOriginalReadForActorAsync(f.Workspace.Actor, original, "canvas.file.open", [f.Scope], ct));
        Assert.Null(await f.Resources.AuthorizeOriginalReadForActorAsync(f.Workspace.Actor, new ForeignContext(), "canvas.file.open", [f.Scope], ct));
        Assert.Null(await f.Resources.AuthorizeOriginalReadForActorAsync(f.Workspace.Actor with { ActorId = "foreign" }, original, "canvas.file.open", [f.Scope], ct));
        Assert.Null(await f.Resources.AuthorizeOriginalReadForActorAsync(f.Workspace.Actor, original, "canvas.file.save", [f.Scope], ct));
        Assert.Null(await f.Resources.AuthorizeOriginalReadForActorAsync(f.Workspace.Actor, original, "canvas.file.open", [f.Scope with { Access = ResourceAccess.Write }], ct));
        Assert.Null(await f.Resources.AuthorizeOriginalReadForActorAsync(f.Workspace.Actor, original, "canvas.file.open", [f.Scope with { Id = Guid.NewGuid().ToString("D") }], ct));
        Assert.Null(await f.Resources.AuthorizeOriginalReadForActorAsync(f.Workspace.Actor, original, "canvas.file.open", [f.Scope with { Revision = Guid.NewGuid().ToString("D") }], ct));
        var replacementIssuer = new FilesArtifactResourceResolver(f.Authority);
        Assert.Null(await new ResourceAuthorizationService(f.Profiles, [replacementIssuer]).AuthorizeOriginalReadForActorAsync(f.Workspace.Actor, original, "canvas.file.open", [f.Scope], ct));
        var forged = f.Workspace with { Directories = new FilesWorkspaceDirectoryResolver(Path.Combine(f.Root, "foreign.json"), _ => null, _ => null) };
        await Assert.ThrowsAsync<UnauthorizedAccessException>(async () => await f.Resolver.CaptureOriginalCanvasReadAsync(forged, f.File, () => true, ct));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(async () => await new FilesArtifactResourceResolver(_ => f.Workspace.Provider).CaptureOriginalCanvasReadAsync(f.Workspace, f.File, () => true, ct));
        Assert.False(File.Exists(Path.Combine(f.Root, "foreign.json")));
        Assert.Equal(before, await File.ReadAllBytesAsync(f.HomeFile, ct)); Assert.Equal(drive, await File.ReadAllBytesAsync(f.DriveFile, ct));
    }

    [Fact]
    public async Task Actual_same_path_store_UUID_replacement_cannot_return_original_metadata_artifact_or_content_or_rebind_context()
    {
        using var f = await Fixture.CreateAsync(); var ct = TestContext.Current.CancellationToken;
        var prior = (await f.Workspace.Provider.GetAsync(f.File, ct)).Value!;
        Assert.True((await f.Workspace.Provider.CommitDurableRevisionAsync(new(f.File, "canvas", "original-content-fixture",
            f.Workspace.Actor.ActorId, DateTimeOffset.UtcNow, 4, "sha256:" + new string('a', 64), "original.bin", prior.CurrentRevisionId), ct)).IsSuccess);
        var store = f.Workspace.Configuration.StoreId;
        var metadata = (await f.Workspace.Provider.GetForOriginalStoreAsync(store, f.File, ct)).Value!;
        Assert.NotNull(metadata.CurrentRevisionId);
        var revision = metadata.CurrentRevisionId!.Value;
        Assert.True((await f.Workspace.Provider.GetArtifactForOriginalStoreAsync(store, f.File, revision, ct)).IsSuccess);
        var content = await f.Workspace.Provider.GetCurrentArtifactContentForOriginalStoreAsync(store, f.File, revision, ct);
        Assert.True(content.IsSuccess); Assert.Equal("original.bin", content.Value!.ProviderContentReference);
        var scope = f.Scope with { Revision = revision.ToString() };
        var original = await f.Resolver.CaptureOriginalCanvasReadAsync(f.Workspace, f.File, () => true, ct);
        Assert.False(f.Resources.IsIssuedOriginalReadOwnerBinding(f.Workspace.Actor, original, "canvas.file.open", scope,
            f.Workspace.Provider, f.Workspace.Directories, Guid.NewGuid()));
        var home = await File.ReadAllBytesAsync(f.HomeFile, ct); var originalDrive = await File.ReadAllBytesAsync(f.DriveFile, ct);
        var replacement = JsonNode.Parse(originalDrive)!.AsObject(); replacement["state"]!["storeId"] = Guid.NewGuid().ToString("D");
        await File.WriteAllTextAsync(f.DriveFile, replacement.ToJsonString(), ct);
        var replacedDrive = await File.ReadAllBytesAsync(f.DriveFile, ct);
        await Assert.ThrowsAsync<FilesOriginalStoreReadChangedException>(async () => await f.Workspace.Provider.GetForOriginalStoreAsync(store, f.File, ct));
        await Assert.ThrowsAsync<FilesOriginalStoreReadChangedException>(async () => await f.Workspace.Provider.GetArtifactForOriginalStoreAsync(store, f.File, revision, ct));
        await Assert.ThrowsAsync<FilesOriginalStoreReadChangedException>(async () => await f.Workspace.Provider.GetCurrentArtifactContentForOriginalStoreAsync(store, f.File, revision, ct));
        Assert.Null(await f.Resources.AuthorizeOriginalReadForActorAsync(f.Workspace.Actor, original, "canvas.file.open", [scope], ct));
        Assert.Equal(replacedDrive, await File.ReadAllBytesAsync(f.DriveFile, ct)); Assert.Equal(home, await File.ReadAllBytesAsync(f.HomeFile, ct));
        await File.WriteAllBytesAsync(f.DriveFile, originalDrive, ct);
        Assert.False(f.Resolver.IsIssuedOriginalRead(original, f.Workspace.Actor, "canvas.file.open", scope));
        Assert.Null(await f.Resources.AuthorizeOriginalReadForActorAsync(f.Workspace.Actor, original, "canvas.file.open", [scope], ct));
        var fresh = await f.Resolver.CaptureOriginalCanvasReadAsync(f.Workspace, f.File, () => true, ct);
        Assert.Equal(f.Workspace.Actor, await f.Resources.AuthorizeOriginalReadForActorAsync(f.Workspace.Actor, fresh, "canvas.file.open", [scope], ct));
    }

    [Fact]
    public async Task Observed_original_lifetime_retirement_cannot_resurrect_when_callback_becomes_true_again()
    {
        using var f = await Fixture.CreateAsync(); var ct = TestContext.Current.CancellationToken; var alive = true;
        var original = await f.Resolver.CaptureOriginalCanvasReadAsync(f.Workspace, f.File, () => alive, ct);
        Assert.Equal(f.Workspace.Actor, await f.Resources.AuthorizeOriginalReadForActorAsync(f.Workspace.Actor, original, "canvas.file.open", [f.Scope], ct));
        var before = await File.ReadAllBytesAsync(f.HomeFile, ct); var drive = await File.ReadAllBytesAsync(f.DriveFile, ct);
        alive = false; Assert.Null(await f.Resources.AuthorizeOriginalReadForActorAsync(f.Workspace.Actor, original, "canvas.file.open", [f.Scope], ct));
        alive = true; Assert.False(f.Resolver.IsIssuedOriginalRead(original, f.Workspace.Actor, "canvas.file.open", f.Scope));
        Assert.False(f.Resources.IsIssuedOriginalReadOwnerBinding(f.Workspace.Actor, original, "canvas.file.open", f.Scope, f.Workspace.Provider, f.Workspace.Directories, f.Workspace.Configuration.StoreId));
        Assert.Null(await f.Resources.AuthorizeOriginalReadForActorAsync(f.Workspace.Actor, original, "canvas.file.open", [f.Scope], ct));
        var fresh = await f.Resolver.CaptureOriginalCanvasReadAsync(f.Workspace, f.File, () => alive, ct);
        Assert.Equal(f.Workspace.Actor, await f.Resources.AuthorizeOriginalReadForActorAsync(f.Workspace.Actor, fresh, "canvas.file.open", [f.Scope], ct));
        Assert.Equal(before, await File.ReadAllBytesAsync(f.HomeFile, ct)); Assert.Equal(drive, await File.ReadAllBytesAsync(f.DriveFile, ct));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Foreign_Home_or_same_store_replacement_profile_instance_cannot_borrow_original_workspace_read_provenance(bool foreignStore)
    {
        using var f = await Fixture.CreateAsync(); var ct = TestContext.Current.CancellationToken;
        var foreignPath = Path.Combine(f.Root, "foreign-home.json");
        var home = foreignStore ? new FileHomeCoreStateStore(foreignPath) : f.Home;
        var profiles = new HomeLocalProfileIdentity(home, new OperatingSystemPrincipalSource());
        var source = new FilesArtifactResourceResolver(new NativeFilesWorkspaceAuthority(f.Files, profiles, f.OwnershipAuthority));
        var before = await File.ReadAllBytesAsync(f.HomeFile, ct); var drive = await File.ReadAllBytesAsync(f.DriveFile, ct);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(async () => await source.CaptureOriginalCanvasReadAsync(f.Workspace, f.File, () => true, ct));
        Assert.False(File.Exists(foreignPath)); Assert.Equal(before, await File.ReadAllBytesAsync(f.HomeFile, ct));
        Assert.Equal(drive, await File.ReadAllBytesAsync(f.DriveFile, ct));
        var original = await f.Resolver.CaptureOriginalCanvasReadAsync(f.Workspace, f.File, () => true, ct);
        Assert.Equal(f.Workspace.Actor, await f.Resources.AuthorizeOriginalReadForActorAsync(f.Workspace.Actor, original, "canvas.file.open", [f.Scope], ct));
    }

    [Fact]
    public async Task Actual_committed_revision_requires_fresh_original_context_without_rebinding_a_retained_observation()
    {
        using var f = await Fixture.CreateAsync(); var ct = TestContext.Current.CancellationToken;
        var original = await f.Resolver.CaptureOriginalCanvasReadAsync(f.Workspace, f.File, () => true, ct);
        var metadata = (await f.Workspace.Provider.GetAsync(f.File, ct)).Value!;
        // This is an actual Files metadata owner publication, not a native Canvas payload or Home grant proof.
        var committed = await f.Workspace.Provider.CommitDurableRevisionAsync(new(f.File, "canvas", "fixture-original-read-revision",
            f.Workspace.Actor.ActorId, DateTimeOffset.UtcNow, 0, null, null, metadata.CurrentRevisionId), ct);
        Assert.True(committed.IsSuccess);
        var current = (await f.Workspace.Provider.GetAsync(f.File, ct)).Value!;
        var changed = f.Scope with { Revision = current.CurrentRevisionId!.Value.ToString() };
        Assert.NotEqual(f.Scope.Revision, changed.Revision);
        var before = await File.ReadAllBytesAsync(f.DriveFile, ct);
        Assert.False(f.Resolver.IsIssuedOriginalRead(original, f.Workspace.Actor, "canvas.file.open", changed));
        Assert.Null(await f.Resources.AuthorizeOriginalReadForActorAsync(f.Workspace.Actor, original, "canvas.file.open", [changed], ct));
        Assert.Null(await f.Resources.AuthorizeOriginalReadForActorAsync(f.Workspace.Actor, original, "canvas.file.open", [f.Scope], ct));
        var fresh = await f.Resolver.CaptureOriginalCanvasReadAsync(f.Workspace, f.File, () => true, ct);
        Assert.Equal(f.Workspace.Actor, await f.Resources.AuthorizeOriginalReadForActorAsync(f.Workspace.Actor, fresh, "canvas.file.open", [changed], ct));
        Assert.Equal(before, await File.ReadAllBytesAsync(f.DriveFile, ct));
    }

    [Fact]
    public async Task Genuine_metadata_read_wait_uses_original_provider_and_refuses_changed_configuration_without_opening_replacement()
    {
        using var f = await Fixture.CreateAsync(); var ct = TestContext.Current.CancellationToken;
        var armed = false; var checks = 0; var checkedOriginal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        bool Lifetime() { if (armed && Interlocked.Increment(ref checks) == 2) checkedOriginal.TrySetResult(); return true; }
        var original = await f.Resolver.CaptureOriginalCanvasReadAsync(f.Workspace, f.File, Lifetime, ct);
        // Retain the actual canonical metadata semaphore, not a substitute provider/result or permission.
        var type = typeof(DurableDriveProvider).Assembly.GetType("HavenOS.Files.FilesStatePathLocks", throwOnError: true)!;
        var gate = Assert.IsType<SemaphoreSlim>(type.GetMethod("Get", BindingFlags.Public | BindingFlags.Static)!.Invoke(null, [Path.GetFullPath(f.DriveFile)]));
        await gate.WaitAsync(ct); var held = true; Task<ResourceAccessDecision>? pending = null;
        var beforeDrive = await File.ReadAllBytesAsync(f.DriveFile, ct);
        try
        {
            armed = true;
            pending = f.Resolver.EvaluateOriginalReadAsync(original, f.Workspace.Actor, "canvas.file.open", f.Scope, ct).AsTask();
            await checkedOriginal.Task.WaitAsync(TimeSpan.FromSeconds(10), ct); Assert.False(pending.IsCompleted);
            // Observe the real metadata WaitAsync queue BEFORE changing configuration.
            // This is runtime timing introspection of the actual owning gate, not an invented owner result.
            var queue = typeof(SemaphoreSlim).GetField("m_asyncHead", BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException("Actual runtime metadata semaphore queue observer unavailable.");
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
            while (queue.GetValue(gate) is null)
            {
                Assert.True(DateTime.UtcNow < deadline, "Actual original metadata read did not enter its owning gate.");
                await Task.Delay(1, ct);
            }
            Assert.False(pending.IsCompleted);
            var read = await f.Home.ReadAsync(ct); Assert.True(read.IsSuccess);
            var config = Assert.Single(read.State!.Records, x => x.RecordType == "files.native-workspace");
            var replacement = f.Workspace.Configuration with { RootDirectory = Path.Combine(f.Root, "replacement-must-not-open") };
            Assert.True((await f.Home.WriteAsync(config with { Revision = config.Revision + 1, Payload = JsonSerializer.SerializeToElement(replacement) }, config.Revision, ct)).IsSuccess);
            var afterConfig = await File.ReadAllBytesAsync(f.HomeFile, ct);
            gate.Release(); held = false;
            var result = await pending.WaitAsync(TimeSpan.FromSeconds(10), ct);
            Assert.False(result.Allowed); Assert.Equal("FilesOriginalReadChanged", result.Code);
            Assert.False(Directory.Exists(replacement.RootDirectory));
            Assert.Equal(beforeDrive, await File.ReadAllBytesAsync(f.DriveFile, ct));
            Assert.Equal(afterConfig, await File.ReadAllBytesAsync(f.HomeFile, ct));
        }
        finally
        {
            if (held) gate.Release();
            if (pending is not null) { try { await pending; } catch { } } // Observe actual released owner task before fixture deletion; preserve primary failure.
        }
    }

    [Fact]
    public async Task Original_folder_context_uses_exact_registered_issuer_action_and_revision_without_publishing_or_granting_a_project()
    {
        using var f = await Fixture.CreateAsync(); var ct = TestContext.Current.CancellationToken;
        var folder = f.Workspace.Configuration.AppFolders["sites"];
        var home = await File.ReadAllBytesAsync(f.HomeFile, ct); var drive = await File.ReadAllBytesAsync(f.DriveFile, ct);
        var original = await f.Resolver.CaptureOriginalFolderReadAsync(f.Workspace, folder, () => true, ct);
        var scope = original.OriginalScope;
        Assert.Null(original.OriginalMaterializationFolderId);
        Assert.Equal(ResourceAccess.Read, scope.Access); Assert.Equal(folder.ToString(), scope.Id);
        Assert.Equal(f.Workspace.Actor, await f.Resources.AuthorizeOriginalReadForActorAsync(f.Workspace.Actor,
            original, "files.folder.native-root.read", [scope], ct));
        Assert.Null(await f.Resources.AuthorizeOriginalReadForActorAsync(f.Workspace.Actor, original, "canvas.file.open", [scope], ct));
        Assert.Null(await f.Resources.AuthorizeOriginalReadForActorAsync(f.Workspace.Actor, original,
            "files.folder.native-root.read", [scope with { Access = ResourceAccess.Write }], ct));
        Assert.Null(await f.Resources.AuthorizeOriginalReadForActorAsync(f.Workspace.Actor, original,
            "files.folder.native-root.read", [scope with { Revision = Guid.NewGuid().ToString("D") }], ct));
        Assert.False(new FilesArtifactResourceResolver(f.Authority).IsIssuedOriginalRead(original,
            f.Workspace.Actor, "files.folder.native-root.read", scope));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(async () =>
            await f.Resolver.CaptureOriginalFolderReadAsync(f.Workspace, f.File, () => true, ct));
        Assert.Equal(home, await File.ReadAllBytesAsync(f.HomeFile, ct)); Assert.Equal(drive, await File.ReadAllBytesAsync(f.DriveFile, ct));
    }

    [Fact]
    public async Task Original_registered_child_lease_reads_only_the_existing_mapping_and_permanently_refuses_retirement()
    {
        using var f = await Fixture.CreateAsync(); var ct = TestContext.Current.CancellationToken;
        var profile = Guid.Parse(f.Workspace.Actor.ProfileId); var root = f.Workspace.Configuration.AppFolders["sites"];
        var directory = (await f.Workspace.Directories.ResolveProfileAsync(profile, "sites", ct)).Value!.DirectoryPath;
        Assert.True((await f.Workspace.Directories.RegisterProfileAsync(profile, root, "stacks", directory, ct)).IsSuccess);
        var child = HostedItemId.New(); var now = DateTimeOffset.UtcNow;
        Assert.True((await f.Workspace.Provider.MutateAsync(new FilesOperation(new(Guid.NewGuid()), f.Workspace.Actor.ActorId,
            child, null, root, "CreateFolder", null, null, FilesOperationState.Pending, now, now, null, null), "Existing child", ct)).IsSuccess);
        var childDirectory = Path.Combine(directory, "already-mapped-child"); Directory.CreateDirectory(childDirectory);
        Assert.True((await f.Workspace.Directories.RegisterProfileAsync(profile, child, "stacks.project." + child.Value.ToString("N"), childDirectory, ct)).IsSuccess);
        var rootMetadata = (await f.Workspace.Provider.GetAsync(root, ct)).Value!;
        var childMetadata = (await f.Workspace.Provider.GetAsync(child, ct)).Value!;
        var home = await File.ReadAllBytesAsync(f.HomeFile, ct); var drive = await File.ReadAllBytesAsync(f.DriveFile, ct); var alive = true;
        var source = new FilesOriginalChildFolderReadSource(f.Resolver, f.Resources);
        using var lease = await source.ReadAsync(f.Workspace, "stacks", root.Value, rootMetadata.CurrentRevisionId!.Value.ToString(),
            child.Value, childMetadata.CurrentRevisionId!.Value.ToString(), () => alive, ct);
        Assert.Equal(childDirectory, lease.DirectoryPath); Assert.Equal(child.Value, lease.Source.FolderID);
        await lease.RevalidateAsync(ct);
        alive = false; await Assert.ThrowsAsync<UnauthorizedAccessException>(() => lease.RevalidateAsync(ct));
        alive = true; await Assert.ThrowsAsync<UnauthorizedAccessException>(() => lease.RevalidateAsync(ct));
        Assert.Equal(home, await File.ReadAllBytesAsync(f.HomeFile, ct)); Assert.Equal(drive, await File.ReadAllBytesAsync(f.DriveFile, ct));
    }

    [Fact]
    public async Task Original_directory_observation_checks_same_locked_store_before_returning_registered_binding()
    {
        using var f = await Fixture.CreateAsync(); var ct = TestContext.Current.CancellationToken;
        var profile = Guid.Parse(f.Workspace.Actor.ProfileId); var folder = f.Workspace.Configuration.AppFolders["canvas"];
        var home = await File.ReadAllBytesAsync(f.HomeFile, ct); var drive = await File.ReadAllBytesAsync(f.DriveFile, ct);
        var observed = await f.Workspace.Directories.ResolveProfileForOriginalStoreAsync(profile, "canvas", folder,
            f.Workspace.Provider, f.Workspace.Configuration.StoreId, ct);
        Assert.True(observed.IsSuccess); Assert.Equal(folder, observed.Value!.FolderId);
        var wrongFolder = await f.Workspace.Directories.ResolveProfileForOriginalStoreAsync(profile, "canvas", f.Workspace.Configuration.AppFolders["sites"],
            f.Workspace.Provider, f.Workspace.Configuration.StoreId, ct);
        Assert.False(wrongFolder.IsSuccess);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => f.Workspace.Directories.ResolveProfileForOriginalStoreAsync(Guid.NewGuid(),
            "canvas", folder, f.Workspace.Provider, f.Workspace.Configuration.StoreId, ct));
        var json = JsonNode.Parse(drive)!.AsObject(); json["state"]!["storeId"] = Guid.NewGuid();
        await File.WriteAllTextAsync(f.DriveFile, json.ToJsonString(), ct); var replaced = await File.ReadAllBytesAsync(f.DriveFile, ct);
        await Assert.ThrowsAsync<FilesOriginalStoreReadChangedException>(() => f.Workspace.Directories.ResolveProfileForOriginalStoreAsync(profile,
            "canvas", folder, f.Workspace.Provider, f.Workspace.Configuration.StoreId, ct));
        Assert.Equal(replaced, await File.ReadAllBytesAsync(f.DriveFile, ct)); Assert.Equal(home, await File.ReadAllBytesAsync(f.HomeFile, ct));
    }

    [Fact]
    public async Task Original_write_observation_is_separate_exact_private_issuer_scope_and_action_and_never_publishes_a_mutation()
    {
        using var f = await Fixture.CreateAsync(); var ct = TestContext.Current.CancellationToken; var alive = true;
        var read = await f.Resolver.CaptureOriginalCanvasReadAsync(f.Workspace, f.File, () => alive, ct);
        var original = await f.Resolver.CaptureOriginalCanvasWriteAsync(read, ct); var scope = original.OriginalScope;
        var home = await File.ReadAllBytesAsync(f.HomeFile, ct); var drive = await File.ReadAllBytesAsync(f.DriveFile, ct);
        Assert.Equal(ResourceAccess.Write, scope.Access); Assert.Equal(read.OriginalScope.Id, scope.Id); Assert.Equal(read.OriginalScope.Revision, scope.Revision);
        Assert.True(f.Resources.IsOriginalWriteContextForActor(f.Workspace.Actor, original, "canvas.file.save", [scope]));
        Assert.Equal(f.Workspace.Actor, await f.Resources.AuthorizeOriginalWriteForActorAsync(f.Workspace.Actor, original, "canvas.file.save", [scope], ct));
        Assert.False(f.Resources.IsOriginalWriteContextForActor(f.Workspace.Actor, original, "canvas.file.open", [scope]));
        Assert.False(f.Resources.IsOriginalWriteContextForActor(f.Workspace.Actor, original, "canvas.file.save", [read.OriginalScope]));
        Assert.False(f.Resources.IsOriginalWriteContextForActor(f.Workspace.Actor, original, "canvas.file.save", [scope with { Revision = Guid.NewGuid().ToString("D") }]));
        Assert.False(f.Resources.IsOriginalWriteContextForActor(f.Workspace.Actor, new ForeignWriteContext(scope), "canvas.file.save", [scope]));
        Assert.False(new FilesArtifactResourceResolver(f.Authority).IsIssuedOriginalWrite(original, f.Workspace.Actor, "canvas.file.save", scope));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(async () => await f.Resolver.CaptureOriginalCanvasWriteAsync(new ForeignContext(), ct));
        alive = false; Assert.Null(await f.Resources.AuthorizeOriginalWriteForActorAsync(f.Workspace.Actor, original, "canvas.file.save", [scope], ct));
        alive = true; Assert.False(f.Resources.IsOriginalWriteContextForActor(f.Workspace.Actor, original, "canvas.file.save", [scope]));
        Assert.Equal(home, await File.ReadAllBytesAsync(f.HomeFile, ct)); Assert.Equal(drive, await File.ReadAllBytesAsync(f.DriveFile, ct));
    }

    private sealed class ForeignWriteContext(ResourceScope scope) : IOriginalCanonicalWriteContext
    { public ResourceScope OriginalScope { get; } = scope; }

    private sealed class ForeignContext : IOriginalCanonicalReadContext
    { public ResourceScope OriginalScope { get; } = new("files.item", Guid.NewGuid().ToString("D"), "uncommitted", ResourceAccess.Read); }
    [Fact]
    public async Task Genuine_original_binding_lease_retains_exact_mapping_until_disposal_and_stale_capture_cannot_reenter()
    {
        using var f = await Fixture.CreateAsync(); var ct = TestContext.Current.CancellationToken;
        var profile = Guid.Parse(f.Workspace.Actor.ProfileId);
        var binding = (await f.Workspace.Directories.ResolveProfileForOriginalStoreAsync(profile, "canvas",
            f.Workspace.Configuration.AppFolders["canvas"], f.Workspace.Provider, f.Workspace.Configuration.StoreId, ct)).Value!;
        var home = await File.ReadAllBytesAsync(f.HomeFile, ct); var drive = await File.ReadAllBytesAsync(f.DriveFile, ct);
        var original = await f.Workspace.Directories.AcquireOriginalBindingCommitLeaseAsync(binding, f.Workspace.Provider, ct);
        Assert.NotNull(original); Assert.True(original.IsHeld);
        Task<FilesResult<FilesWorkspaceDirectoryBinding>>? registration = null;
        var replacement = Path.Combine(f.Root, "replacement-binding"); Directory.CreateDirectory(replacement);
        try
        {
            registration = f.Workspace.Directories.RegisterProfileAsync(profile, binding.FolderId, "canvas", replacement, ct);
            var store = typeof(FilesWorkspaceDirectoryResolver).GetField("_store", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(f.Workspace.Directories)!;
            var gate = Assert.IsType<SemaphoreSlim>(store.GetType().GetField("_gate", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(store));
            var queue = typeof(SemaphoreSlim).GetField("m_asyncHead", BindingFlags.NonPublic | BindingFlags.Instance)
                ?? throw new InvalidOperationException("Actual binding semaphore queue observer unavailable.");
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
            while (queue.GetValue(gate) is null)
            {
                Assert.True(DateTime.UtcNow < deadline, "Actual mapping registration did not reach its owner lease.");
                await Task.Delay(1, ct);
            }
            Assert.False(registration.IsCompleted); Assert.True(original.IsHeld);
            Assert.Equal(home, await File.ReadAllBytesAsync(f.HomeFile, ct));
            Assert.Equal(drive, await File.ReadAllBytesAsync(f.DriveFile, ct));
            await original.DisposeAsync(); Assert.False(original.IsHeld);
            Assert.True((await registration.WaitAsync(TimeSpan.FromSeconds(10), ct)).IsSuccess);
            var changed = (await f.Workspace.Directories.ResolveProfileForOriginalStoreAsync(profile, "canvas",
                binding.FolderId, f.Workspace.Provider, f.Workspace.Configuration.StoreId, ct)).Value!;
            Assert.Equal(replacement, changed.DirectoryPath); Assert.NotEqual(binding, changed);
            Assert.Null(await f.Workspace.Directories.AcquireOriginalBindingCommitLeaseAsync(binding, f.Workspace.Provider, ct));
            await using var fresh = await f.Workspace.Directories.AcquireOriginalBindingCommitLeaseAsync(changed, f.Workspace.Provider, ct);
            Assert.NotNull(fresh); Assert.True(fresh.IsHeld);
            Assert.Equal(home, await File.ReadAllBytesAsync(f.HomeFile, ct));
            Assert.Equal(drive, await File.ReadAllBytesAsync(f.DriveFile, ct));
        }
        finally
        {
            await original.DisposeAsync();
            if (registration is not null) { try { await registration; } catch { } }
        }
    }

    [Fact]
    public async Task Genuine_original_write_Home_route_preserves_approval_and_capability_on_foreign_context_then_claims_exact_original_tuple_once()
    {
        using var f = await Fixture.CreateAsync(); var ct = TestContext.Current.CancellationToken;
        var policy = new HavenOS.Apps.Canvas.CanvasNativeActionPolicies();
        var permissions = new HomePermissionTrustService(f.Home, policy.TryGet);
        var broker = new HomeResourceOperationBroker(f.Resources, permissions);
        var read = await f.Resolver.CaptureOriginalCanvasReadAsync(f.Workspace, f.File, () => true, ct);
        var write = await f.Resolver.CaptureOriginalCanvasWriteAsync(read, ct);
        var otherRead = await f.Resolver.CaptureOriginalCanvasReadAsync(f.Workspace, f.File, () => true, ct);
        var otherWrite = await f.Resolver.CaptureOriginalCanvasWriteAsync(otherRead, ct);
        var arguments = JsonSerializer.SerializeToElement(new { fileId = f.File.Value, expectedRevision = f.Scope.Revision, operation = "original-write-protocol-only" });
        var scopes = new[] { write.OriginalScope };
        var drive = await File.ReadAllBytesAsync(f.DriveFile, ct);
        var pending = await broker.AuthorizeWithOriginalWriteForActorAsync(f.Workspace.Actor, write, "canvas", "canvas.file.save",
            scopes, arguments, "Review original write observation protocol; no payload is changed", null, "actual-original-files-owner", ct);
        Assert.Equal(HomePermissionRequestState.PendingApproval, pending.State);
        Assert.True((await permissions.DecideAsync(pending.RequestId, HomeApprovalChoice.Accept, cancellationToken: ct)).Succeeded);
        var approved = await File.ReadAllBytesAsync(f.HomeFile, ct);
        Assert.Null(await broker.BeginExecutionWithOriginalWriteCapabilityAsync(pending.RequestId, arguments, otherWrite, ct));
        Assert.Equal(approved, await File.ReadAllBytesAsync(f.HomeFile, ct));
        var capability = Assert.IsType<HomeResourceExecutionCapability>(await broker.BeginExecutionWithOriginalWriteCapabilityAsync(pending.RequestId, arguments, write, ct));
        var executing = await File.ReadAllBytesAsync(f.HomeFile, ct);
        foreach (var substituted in new[] { ("foreign", "canvas.file.save", scopes, arguments, write),
            ("canvas", "canvas.file.save", scopes, arguments, otherWrite),
            ("canvas", "canvas.file.save", new[] { write.OriginalScope with { Revision = "substituted" } }, arguments, write) })
        {
            var refused = await broker.ClaimExecutionWithOriginalWriteObservedAsync(capability, substituted.Item1,
                substituted.Item2, substituted.Item3, substituted.Item4, substituted.Item5, ct);
            Assert.Equal(HomeResourceClaimDisposition.InputNotConsumed, refused.Disposition); Assert.Null(refused.Actor);
            Assert.Equal(executing, await File.ReadAllBytesAsync(f.HomeFile, ct));
            Assert.Equal(drive, await File.ReadAllBytesAsync(f.DriveFile, ct));
        }
        var claimed = await broker.ClaimExecutionWithOriginalWriteObservedAsync(capability, "canvas", "canvas.file.save", scopes, arguments, write, ct);
        Assert.Equal(HomeResourceClaimDisposition.Claimed, claimed.Disposition); Assert.Equal(f.Workspace.Actor, claimed.Actor);
        Assert.NotNull(broker.CaptureClaimedAttestation(capability));
        Assert.True((await broker.CompleteExecutionAsync(capability, new(HomePermissionRequestState.Succeeded,
            "ORIGINAL_OBSERVATION_PROTOCOL_COMPLETE", "No Files mutation was attempted.", []), ct)).Succeeded);
        Assert.Equal(HomeResourceClaimDisposition.Unavailable, (await broker.ClaimExecutionWithOriginalWriteObservedAsync(
            capability, "canvas", "canvas.file.save", scopes, arguments, write, ct)).Disposition);
        Assert.Equal(drive, await File.ReadAllBytesAsync(f.DriveFile, ct));
    }

    [Fact]
    public async Task Original_resource_actor_composition_is_exact_instance_identity_without_Home_or_Files_observations()
    {
        using var f = await Fixture.CreateAsync(); var ct = TestContext.Current.CancellationToken;
        var home = await File.ReadAllBytesAsync(f.HomeFile, ct); var drive = await File.ReadAllBytesAsync(f.DriveFile, ct);
        var sameStoreDifferentProfiles = new HomeLocalProfileIdentity(f.Home, new OperatingSystemPrincipalSource());
        Assert.True(f.Resources.IsBoundToActorSource(f.Profiles));
        Assert.False(f.Resources.IsBoundToActorSource(sameStoreDifferentProfiles));
        Assert.Equal(home, await File.ReadAllBytesAsync(f.HomeFile, ct));
        Assert.Equal(drive, await File.ReadAllBytesAsync(f.DriveFile, ct));
    }

    private sealed class Fixture : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "astra-files-original-read-" + Guid.NewGuid().ToString("N"));
        public string HomeFile => Path.Combine(Root, "home.json");
        public string DriveFile => Path.Combine(Workspace.Configuration.RootDirectory, ".9to1-files", "drive.json");
        public FileHomeCoreStateStore Home { get; private set; } = null!;
        public HomeLocalProfileIdentity Profiles { get; private set; } = null!;
        public NativeFilesWorkspaceService Files { get; private set; } = null!;
        public HomeResourceStoreOwnershipAuthority OwnershipAuthority { get; private set; } = null!;
        public NativeFilesWorkspaceAuthority Authority { get; private set; } = null!;
        public NativeFilesWorkspace Workspace { get; private set; } = null!;
        public FilesArtifactResourceResolver Resolver { get; private set; } = null!;
        public ResourceAuthorizationService Resources { get; private set; } = null!;
        public HostedItemId File { get; private set; }
        public ResourceScope Scope { get; private set; } = null!;
        public static async Task<Fixture> CreateAsync()
        {
            var f = new Fixture(); Directory.CreateDirectory(f.Root);
            try
            {
                var ct = TestContext.Current.CancellationToken;
                f.Home = new(f.HomeFile); f.Profiles = new(f.Home, new OperatingSystemPrincipalSource());
                var files = new NativeFilesWorkspaceService(f.Home, f.Profiles); f.Files = files;
                var ownership = new HomeLocalStoreOwnership(f.Home, f.Profiles, new HomeLocalStoreEvidenceRegistry([files]), new HomePermissionTrustService(f.Home, (_, _) => null));
                var chosen = Path.Combine(f.Root, "chosen"); Directory.CreateDirectory(chosen);
                var created = await files.ConfigureNewAsync(chosen, ownership, ct);
                f.OwnershipAuthority = new HomeResourceStoreOwnershipAuthority(ownership, f.Profiles);
                f.Authority = new(files, f.Profiles, f.OwnershipAuthority);
                f.Workspace = await f.Authority.GetCurrentAsync(created.Configuration.StoreId, ct) ?? throw new InvalidOperationException("Actual configured Files required.");
                f.Resolver = new(f.Authority); f.Resources = new(f.Profiles, [f.Resolver]); f.File = HostedItemId.New();
                Assert.True((await f.Workspace.Provider.RegisterArtifactAsync(new("canvas", Guid.NewGuid().ToString("N"), f.File,
                    f.Workspace.Configuration.AppFolders["canvas"], nameof(FilesArtifactType.Canvas), "Original.9to1c"), f.Workspace.Actor.ActorId, ct)).IsSuccess);
                var metadata = (await f.Workspace.Provider.GetAsync(f.File, ct)).Value!;
                f.Scope = new("files.item", f.File.ToString(), metadata.CurrentRevisionId?.ToString() ?? "uncommitted", ResourceAccess.Read);
                return f;
            }
            catch { f.Dispose(); throw; }
        }
        public void Dispose() { if (Directory.Exists(Root)) Directory.Delete(Root, true); }
    }
}
