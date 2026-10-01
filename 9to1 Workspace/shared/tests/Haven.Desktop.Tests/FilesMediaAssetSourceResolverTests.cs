using System.Security.Cryptography;
using Haven.Application;
using Haven.Core.Media;
using Haven.Desktop.Services;
using HavenOS.Files;
using HavenOS.Home.Core;

namespace Haven.Desktop.Tests;

public sealed class FilesMediaAssetSourceResolverTests
{
    [Fact]
    public async Task Canonical_file_lease_survives_rename_restart_and_original_mutation_then_releases_its_bytes()
    {
        var root = Path.Combine(Path.GetTempPath(), "astra-files-media-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var token = TestContext.Current.CancellationToken;
            var actors = new HomeLocalProfileIdentity(new FileHomeCoreStateStore(Path.Combine(root, "home.json")), new OperatingSystemPrincipalSource());
            var actor = await actors.GetCurrentAsync(token);
            Assert.NotNull(actor);
            var profile = Guid.Parse(actor.ProfileId);
            var statePath = Path.Combine(root, "drive.json");
            var location = new FilesLocationId(Guid.NewGuid());
            var provider = new DurableDriveProvider(statePath, location, actor.ActorId);
            var now = DateTimeOffset.UtcNow;
            var folder = new FilesOperation(new(Guid.NewGuid()), actor.ActorId, HostedItemId.New(), null, null,
                "CreateFolder", null, null, FilesOperationState.Pending, now, now, null, null);
            Assert.True((await provider.MutateAsync(folder, "Media", token)).IsSuccess);
            var directories = new FilesWorkspaceDirectoryResolver(Path.Combine(root, "bindings.json"), _ => null, id => id == profile ? provider : null);
            Assert.True((await directories.RegisterProfileAsync(profile, folder.ItemId, "media", root, token)).IsSuccess);
            var sourcePath = Path.Combine(root, "original.wav");
            byte[] bytes = [82, 73, 70, 70, 1, 2, 3, 4];
            await File.WriteAllBytesAsync(sourcePath, bytes, token);
            var hash = "sha256:" + Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            var fileId = HostedItemId.New();
            var revision = new FilesRevisionId(Guid.NewGuid());
            var upload = new FilesUploadedContent(fileId, folder.ItemId, "original.wav", "audio/wav", revision,
                null, actor.ActorId, now, bytes.Length, hash, "original.wav");
            Assert.True((await provider.CommitUploadedContentAsync(upload, token)).IsSuccess);
            Assert.True((await provider.CommitUploadedContentAsync(upload, token)).IsSuccess);
            Assert.False((await provider.CommitUploadedContentAsync(upload with { SizeBytes = 100 }, token)).IsSuccess);
            Assert.False((await provider.CommitUploadedContentAsync(upload with { ActorId = "untrusted" }, token)).IsSuccess);
            var materializations = new FilesMaterializationRegistry(root, Path.Combine(root, "materializations.json"));
            await materializations.RegisterValidatedAsync(sourcePath, new(fileId, revision, hash, bytes.Length, now), SyncAvailability.AvailableOffline, token);
            var rename = new FilesOperation(new(Guid.NewGuid()), actor.ActorId, fileId, folder.ItemId, folder.ItemId,
                "Rename", revision, null, FilesOperationState.Pending, now, now, null, null);
            Assert.True((await provider.MutateAsync(rename, "renamed.wav", token)).IsSuccess);
            provider = new DurableDriveProvider(statePath, location, actor.ActorId);
            var authorization = new ResourceAuthorizationService(actors, [new FilesArtifactResourceResolver(current => current == actor ? provider : null)]);
            var resolver = new FilesMediaAssetSourceResolver(actors, current => current == actor ? new(provider, materializations) : null, directories, authorization);
            var assetId = MediaAssetId.New();
            var result = await resolver.ResolveAsync(fileId.ToString(), assetId, revision.ToString(), token);
            Assert.True(result.IsSuccess, result.Error?.Message);
            var lease = result.Value!;
            Assert.Equal(fileId.Value, lease.Source.HostedItemId);
            Assert.Equal(assetId, lease.Source.AssetId);
            Assert.Equal(revision.ToString(), lease.Source.SourceRevisionId);
            await File.WriteAllBytesAsync(sourcePath, [0, 0, 0, 0], token);
            Assert.Equal(bytes, await File.ReadAllBytesAsync(lease.Source.SourceUri.LocalPath, token));
            var immutablePath = lease.Source.SourceUri.LocalPath;
            await lease.DisposeAsync();
            await lease.DisposeAsync();
            Assert.False(File.Exists(immutablePath));
            var changed = await resolver.ResolveAsync(fileId.ToString(), assetId, revision.ToString(), token);
            Assert.False(changed.IsSuccess);
            var stale = await resolver.ResolveAsync(fileId.ToString(), assetId, Guid.NewGuid().ToString("N"), token);
            Assert.Equal(MediaEngineErrorCode.RevisionConflict, stale.Error!.Code);
            await File.WriteAllBytesAsync(sourcePath, bytes, token);
            var current = (await provider.GetAsync(fileId, token)).Value!;
            var competing = new DurableDriveProvider(statePath, location, actor.ActorId);
            byte[] replacementBytes = [82, 73, 70, 70, 8, 7, 6, 5];
            await File.WriteAllBytesAsync(Path.Combine(root, "replacement.wav"), replacementBytes, token);
            var replacement = upload with { ContentHash = "sha256:" + Convert.ToHexString(SHA256.HashData(replacementBytes)).ToLowerInvariant(),
                ProviderContentReference = "replacement.wav", SizeBytes = replacementBytes.Length };
            var results = await Task.WhenAll(
                provider.CommitUploadedContentAsync(replacement with { Name = "renamed.wav", RevisionId = new(Guid.NewGuid()), ExpectedRevision = current.CurrentRevisionId }, token),
                competing.CommitUploadedContentAsync(replacement with { Name = "renamed.wav", RevisionId = new(Guid.NewGuid()), ExpectedRevision = current.CurrentRevisionId }, token));
            Assert.Single(results, result => result.IsSuccess);
            Assert.Single(results, result => result.Error?.Code == FilesErrorCode.RevisionConflict);
            Assert.Equal(MediaEngineErrorCode.RevisionConflict,
                (await resolver.ResolveAsync(fileId.ToString(), assetId, revision.ToString(), token)).Error!.Code);
            var currentWinner = results.Single(item => item.IsSuccess).Value!;
            await materializations.MoveMappingAsync(fileId, Path.Combine(root, "replacement.wav"), token);
            await materializations.RegisterValidatedAsync(Path.Combine(root, "replacement.wav"),
                new(fileId, currentWinner.Id, replacement.ContentHash, replacementBytes.Length, now), SyncAvailability.AvailableOffline, token);
            var replacementRead = await resolver.ResolveAsync(fileId.ToString(), assetId, currentWinner.Id.ToString(), token);
            Assert.True(replacementRead.IsSuccess, replacementRead.Error?.Message);
            await using (var replacementLease = replacementRead.Value!)
                Assert.Equal(replacementBytes, await File.ReadAllBytesAsync(replacementLease.Source.SourceUri.LocalPath, token));
            IMediaRetainedAssetSourceResolver retainedResolver = resolver;
            var retainedToken = revision.Value.ToString("D");
            var retained = await retainedResolver.ResolveRetainedAsync(fileId.ToString(), assetId, retainedToken, token);
            Assert.True(retained.IsSuccess, retained.Error?.Message);
            await using (var retainedLease = retained.Value!)
            {
                Assert.Equal(retainedToken, retainedLease.Source.SourceRevisionId);
                Assert.Equal(bytes, await File.ReadAllBytesAsync(retainedLease.Source.SourceUri.LocalPath, token));
            }
            await File.WriteAllBytesAsync(sourcePath, [9, 9, 9, 9], token);
            Assert.False((await resolver.ResolveRetainedAsync(fileId.ToString(), assetId, revision.ToString(), token)).IsSuccess);
            var folderCurrent = (await provider.GetAsync(folder.ItemId, token)).Value!;
            var deleteFolder = folder with { Id = new(Guid.NewGuid()), Operation = "Delete", BaseRevisionId = folderCurrent.CurrentRevisionId };
            Assert.True((await provider.MutateAsync(deleteFolder, null, token)).IsSuccess);
            Assert.False((await provider.GetAsync(fileId, token)).IsSuccess);
            Assert.False((await resolver.ResolveRetainedAsync(fileId.ToString(), assetId, revision.ToString(), token)).IsSuccess);
            var winner = results.Single(result => result.IsSuccess).Value!;
            var hiddenSave = await provider.CommitUploadedContentAsync(upload with { Name = "renamed.wav", RevisionId = new(Guid.NewGuid()), ExpectedRevision = winner.Id }, token);
            Assert.Equal(FilesErrorCode.ItemNotFound, hiddenSave.Error!.Code);
        }
        finally { Directory.Delete(root, true); }
    }
}
