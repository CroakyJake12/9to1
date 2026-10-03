using System.Security.Cryptography;
using System.Text.Json;
using Haven.Application;
using Haven.Application.Compatibility;
using HavenOS.Files;
using HavenOS.Files.NativeHost;
using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;
using Microsoft.Extensions.DependencyInjection;

namespace Haven.Desktop.Tests;

public sealed class FilesCompatibilityPackageContentSourceTests
{
    [Fact]
    public async Task Actual_native_Files_binding_bounds_immutable_package_bytes_and_rechecks_revision_and_Home_ownership()
    {
        var token = TestContext.Current.CancellationToken;
        var root = Path.Combine(Path.GetTempPath(), "astra-files-package-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var home = new FileHomeCoreStateStore(Path.Combine(root, "home.json"));
            var profiles = new HomeLocalProfileIdentity(home, new OperatingSystemPrincipalSource());
            var services = new ServiceCollection();
            services.AddSingleton<IHomeCoreStateStore>(home);
            services.AddSingleton(profiles);
            services.AddSingleton<IAuthenticatedResourceActorSource>(profiles);
            services.AddSingleton(new HomePermissionTrustService(home, (_, _) => null));
            services.AddSingleton<IHomeLocalStoreEvidenceSource, HomeLocalStoreEvidenceRegistry>();
            services.AddSingleton<HomeLocalStoreOwnership>();
            services.AddSingleton<IResourceStoreOwnershipAuthority, HomeResourceStoreOwnershipAuthority>();
            services.AddSingleton<ResourceAuthorizationService>();
            services.AddFilesNativeHost();
            using var graph = services.BuildServiceProvider();
            var source = graph.GetRequiredService<ICompatibilityPackageContentSource>();
            Assert.Same(graph.GetRequiredService<FilesCompatibilityPackageContentSource>(), source);
            var initialActor = await profiles.GetCurrentAsync(token); Assert.NotNull(initialActor);
            await Assert.ThrowsAsync<UnauthorizedAccessException>(async () =>
                await source.ReadAsync(Guid.NewGuid(), initialActor, Guid.NewGuid(), Guid.NewGuid().ToString("N"), 100, token));
            var chosen = Path.Combine(root, "chosen"); Directory.CreateDirectory(chosen);
            await graph.GetRequiredService<NativeFilesWorkspaceService>().ConfigureNewAsync(chosen,
                graph.GetRequiredService<HomeLocalStoreOwnership>(), token);
            var authority = graph.GetRequiredService<NativeFilesWorkspaceAuthority>();
            var workspace = Assert.IsType<NativeFilesWorkspace>(await authority.GetCurrentAsync(token));
            var folder = (await workspace.Provider.GetAsync(workspace.Configuration.AppFolders["picture"], token)).Value!;
            var directory = (await workspace.Directories.ResolveProfileAsync(Guid.Parse(workspace.Actor.ProfileId), "picture", token)).Value!.DirectoryPath;
            byte[] bytes = [77, 90, 1, 2, 3, 4, 5, 6]; // source-lease fixture; does not establish installability/publisher trust
            var originalPath = Path.Combine(directory, "original.exe");
            await File.WriteAllBytesAsync(originalPath, bytes, token);
            var id = HostedItemId.New(); var contentRevision = new FilesRevisionId(Guid.NewGuid());
            var hash = Convert.ToHexString(SHA256.HashData(bytes));
            var upload = new FilesUploadedContent(id, folder.Id, "original.exe", "application/octet-stream", contentRevision,
                null, workspace.Actor.ActorId, DateTimeOffset.UtcNow, bytes.Length, hash, "original.exe");
            var guard = await authority.CaptureCommitAuthorityAsync(workspace.Actor, workspace.Provider, () => true, token);
            Assert.True((await workspace.Provider.CommitUploadedContentAsync(upload, [new(folder.Id, folder.CurrentRevisionId)], guard, token)).IsSuccess);
            await Assert.ThrowsAsync<InvalidDataException>(async () => await source.ReadAsync(workspace.Configuration.StoreId, workspace.Actor, id.Value, contentRevision.ToString(), bytes.Length - 1, token));
            await Assert.ThrowsAsync<InvalidDataException>(async () => await source.ReadAsync(workspace.Configuration.StoreId, workspace.Actor, id.Value, Guid.NewGuid().ToString("N"), 100, token));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(async () => await source.ReadAsync(workspace.Configuration.StoreId,
                workspace.Actor with { ActorId = "foreign-selection" }, id.Value, contentRevision.ToString(), 100, token));
            Assert.False(Directory.Exists(Path.Combine(directory, ".9to1-package-leases")));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(async () =>
                await source.ReadAsync(Guid.NewGuid(), workspace.Actor, id.Value, contentRevision.ToString(), 100, token));
            await using var lease = await source.ReadAsync(workspace.Configuration.StoreId, workspace.Actor, id.Value, contentRevision.Value.ToString("D"), 100, token);
            var metadata = (await workspace.Provider.GetAsync(id, token)).Value!;
            var resources = graph.GetRequiredService<ResourceAuthorizationService>();
            var readScope = new ResourceScope("files.item", id.ToString(), metadata.CurrentRevisionId!.Value.ToString(), ResourceAccess.Read);
            Assert.Equal(workspace.Actor, await resources.AuthorizeAsync("os.compatibility.package.read", [readScope], token));
            Assert.Null(await resources.AuthorizeAsync("os.compatibility.package.read", [readScope with { Access = ResourceAccess.Write }], token));
            Assert.Null(await resources.AuthorizeAsync("os.compatibility.install", [readScope], token));
            Assert.Equal(workspace.Configuration.StoreId, lease.Source.StoreId);
            Assert.Equal(id.Value, lease.Source.FileId); Assert.Equal(workspace.Actor, lease.Source.ObservedActor);
            Assert.Equal("original.exe", lease.Source.Name); Assert.Equal(metadata.CurrentRevisionId!.Value.ToString(), lease.Source.MetadataRevision);
            Assert.Equal(bytes.Length, lease.Source.Length); Assert.Equal(hash, lease.Source.Sha256);
            await using (var read = await lease.OpenReadAsync(token))
            {
                Assert.True(read.CanRead); Assert.True(read.CanSeek); Assert.False(read.CanWrite);
                using var copy = new MemoryStream(); await read.CopyToAsync(copy, token); Assert.Equal(bytes, copy.ToArray());
            }
            var navigation = new CapturedPackageOpen();
            var route = new FilesCompatibilityPackageOpenCoordinator(source,
                graph.GetRequiredService<IAuthenticatedResourceActorSource>(), navigation);
            await route.OpenAsync(lease.Source, token);
            Assert.Equal(lease.Source, Assert.Single(navigation.Selections));
            await Assert.ThrowsAsync<InvalidOperationException>(() => route.OpenAsync(lease.Source with { Name = "wrong.exe" }, token));
            await Assert.ThrowsAsync<InvalidOperationException>(() => route.OpenAsync(lease.Source with { Sha256 = new string('0', 64) }, token));
            Assert.Single(navigation.Selections);
            await File.WriteAllBytesAsync(originalPath, new byte[bytes.Length], token);
            await using (var read = await lease.OpenReadAsync(token))
            { using var copy = new MemoryStream(); await read.CopyToAsync(copy, token); Assert.Equal(bytes, copy.ToArray()); }
            await Assert.ThrowsAsync<InvalidDataException>(async () => await source.ReadAsync(workspace.Configuration.StoreId, workspace.Actor, id.Value, contentRevision.ToString(), 100, token));
            await File.WriteAllBytesAsync(originalPath, bytes, token);
            var now = DateTimeOffset.UtcNow;
            var rename = new FilesOperation(new(Guid.NewGuid()), workspace.Actor.ActorId, id, folder.Id, folder.Id,
                "Rename", metadata.CurrentRevisionId, null, FilesOperationState.Pending, now, now, null, null);
            Assert.True((await workspace.Provider.MutateAsync(rename, "renamed.exe", token)).IsSuccess);
            await Assert.ThrowsAsync<UnauthorizedAccessException>(async () => await lease.RevalidateAsync(token));
            await Assert.ThrowsAsync<InvalidOperationException>(() => route.OpenAsync(lease.Source, token));
            Assert.Single(navigation.Selections);
            await using var renamed = await source.ReadAsync(workspace.Configuration.StoreId, workspace.Actor, id.Value, contentRevision.ToString(), 100, token);
            Assert.Equal("renamed.exe", renamed.Source.Name); Assert.Equal(contentRevision.ToString(), renamed.Source.ContentRevision);
            var renamedMetadata = (await workspace.Provider.GetAsync(id, token)).Value!;
            var moved = rename with { Id = new(Guid.NewGuid()), Operation = "Move", SourceParentId = folder.Id,
                DestinationParentId = workspace.Configuration.AppFolders["canvas"], BaseRevisionId = renamedMetadata.CurrentRevisionId };
            Assert.True((await workspace.Provider.MutateAsync(moved, null, token)).IsSuccess);
            await Assert.ThrowsAsync<UnauthorizedAccessException>(async () => await renamed.RevalidateAsync(token));
            await using var movedLease = await source.ReadAsync(workspace.Configuration.StoreId, workspace.Actor, id.Value, contentRevision.ToString(), 100, token);
            await using (var read = await movedLease.OpenReadAsync(token))
            { using var copy = new MemoryStream(); await read.CopyToAsync(copy, token); Assert.Equal(bytes, copy.ToArray()); }
            Assert.True(File.Exists(originalPath)); // logical move did not invent a second materialisation
            // Suspend after the real actor read while replacing actual persistent store identity.
            // All file/content/metadata identities remain unchanged; configuration A cannot adopt store B.
            var leaseCountBefore = Directory.EnumerateDirectories(Path.Combine(directory, ".9to1-package-leases")).Count();
            var pausedActor = new SuspendedOriginalActor(profiles);
            var pausedSource = new FilesCompatibilityPackageContentSource(authority, pausedActor, resources);
            var statePath = Path.Combine(chosen, ".9to1-files", "drive.json");
            var originalStateBytes = await File.ReadAllBytesAsync(statePath, token);
            var pendingRead = pausedSource.ReadAsync(workspace.Configuration.StoreId, workspace.Actor, id.Value,
                contentRevision.ToString(), 100, token).AsTask();
            await pausedActor.Entered.Task.WaitAsync(token);
            try
            {
                var changedEnvelope = System.Text.Json.Nodes.JsonNode.Parse(originalStateBytes)!.AsObject();
                var changedState = changedEnvelope["state"]!.AsObject();
                var storeProperty = changedState.Select(property => property.Key)
                    .Single(key => string.Equals(key, "StoreId", StringComparison.OrdinalIgnoreCase));
                changedState[storeProperty] = Guid.NewGuid();
                changedEnvelope["opaqueOriginalSelectionFixture"] = "preserve-original-selection";
                var replacedStateBytes = JsonSerializer.SerializeToUtf8Bytes(changedEnvelope);
                await File.WriteAllBytesAsync(statePath, replacedStateBytes, token);
                pausedActor.Release.TrySetResult();
                await Assert.ThrowsAsync<UnauthorizedAccessException>(async () => await pendingRead);
                Assert.True(Enumerable.SequenceEqual(replacedStateBytes, await File.ReadAllBytesAsync(statePath, token)));
            }
            finally
            {
                pausedActor.Release.TrySetResult();
                await File.WriteAllBytesAsync(statePath, originalStateBytes, token);
            }
            Assert.Equal(leaseCountBefore, Directory.EnumerateDirectories(Path.Combine(directory, ".9to1-package-leases")).Count());
            // Change the real persisted profile while the original actor read is suspended. No later actor may be adopted.
            var originalProfile = Assert.Single((await home.ReadAsync(token)).State!.Records, record => record.RecordType == "home.local-profile");
            var changedProfile = originalProfile with { Revision = originalProfile.Revision + 1,
                Payload = JsonSerializer.SerializeToElement(originalProfile.Payload.Deserialize<HomeLocalProfile>()! with { ProfileId = Guid.NewGuid() }) };
            var pausedSelectionActor = new SuspendedOriginalActor(profiles);
            var actorBoundSource = new FilesCompatibilityPackageContentSource(authority, pausedSelectionActor, resources);
            var actorBoundRead = actorBoundSource.ReadAsync(workspace.Configuration.StoreId, workspace.Actor, id.Value,
                contentRevision.ToString(), 100, token).AsTask();
            await pausedSelectionActor.Entered.Task.WaitAsync(token);
            try
            {
                Assert.True((await home.WriteAsync(changedProfile, originalProfile.Revision, token)).IsSuccess);
                pausedSelectionActor.Release.TrySetResult();
                await Assert.ThrowsAsync<UnauthorizedAccessException>(async () => await actorBoundRead);
                Assert.True(Enumerable.SequenceEqual(originalStateBytes, await File.ReadAllBytesAsync(statePath, token)));
                Assert.Equal(leaseCountBefore, Directory.EnumerateDirectories(Path.Combine(directory, ".9to1-package-leases")).Count());
            }
            finally
            {
                pausedSelectionActor.Release.TrySetResult();
                var currentProfile = Assert.Single((await home.ReadAsync(token)).State!.Records, record => record.RecordType == "home.local-profile");
                Assert.True((await home.WriteAsync(currentProfile with { Revision = currentProfile.Revision + 1, Payload = originalProfile.Payload },
                    currentProfile.Revision, token)).IsSuccess);
            }
            var bindingRecord = Assert.Single((await home.ReadAsync(token)).State!.Records, record => record.RecordType == "home.local-store-ownership");
            var binding = bindingRecord.Payload.Deserialize<HomeLocalStoreBinding>()!;
            Assert.True((await home.WriteAsync(bindingRecord with { Revision = bindingRecord.Revision + 1,
                Payload = JsonSerializer.SerializeToElement(binding with { ProfileId = "revoked-profile" }) }, bindingRecord.Revision, token)).IsSuccess);
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => route.OpenAsync(movedLease.Source, token));
            Assert.Single(navigation.Selections);
            await Assert.ThrowsAsync<UnauthorizedAccessException>(async () => await movedLease.OpenReadAsync(token));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(async () => await source.ReadAsync(workspace.Configuration.StoreId, workspace.Actor, id.Value, contentRevision.ToString(), 100, token));
            await movedLease.DisposeAsync(); await movedLease.DisposeAsync();
            await renamed.DisposeAsync(); await renamed.DisposeAsync();
            await lease.DisposeAsync(); await lease.DisposeAsync();
            Assert.Empty(Directory.EnumerateDirectories(Path.Combine(directory, ".9to1-package-leases")));
        }
        finally { Directory.Delete(root, true); }
    }
    private sealed class SuspendedOriginalActor(IAuthenticatedResourceActorSource actual) : IAuthenticatedResourceActorSource
    {
        private int _reads;
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async ValueTask<AuthenticatedResourceActor?> GetCurrentAsync(CancellationToken token)
        {
            var actor = await actual.GetCurrentAsync(token);
            if (Interlocked.Increment(ref _reads) == 1)
            { Entered.TrySetResult(); await Release.Task.WaitAsync(token); }
            return actor;
        }
    }

    private sealed class CapturedPackageOpen : ICompatibilityPackageOpenHandler
    {
        public List<CompatibilityPackageSource> Selections { get; } = [];
        public Task OpenAsync(CompatibilityPackageSource selection, CancellationToken token)
        { token.ThrowIfCancellationRequested(); Selections.Add(selection); return Task.CompletedTask; }
    }
}
