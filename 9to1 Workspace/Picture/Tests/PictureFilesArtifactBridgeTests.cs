using System.Security.Cryptography;
using Haven.Application;
using HavenOS.Files;
using Xunit;

namespace HavenOS.Images.Tests;

public sealed class PictureFilesArtifactBridgeTests
{
    [Fact]
    public async Task Canonical_editable_Files_revision_survives_restart_source_replacement_and_rejects_stale_or_foreign_save()
    {
        var root = Path.Combine(Path.GetTempPath(), "picture-files-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var profile = Guid.NewGuid();
            var owner = "local-profile:" + profile.ToString("D");
            var actor = new Authority(new(owner, profile.ToString(), null, null, "fixture-auth"));
            var location = new FilesLocationId(Guid.NewGuid());
            var providerPath = Path.Combine(root, "drive.json");
            var provider = new DurableDriveProvider(providerPath, location, owner);
            actor.Provider = provider;
            var now = DateTimeOffset.UtcNow;
            var folderId = HostedItemId.New();
            Assert.True((await provider.MutateAsync(new(new(Guid.NewGuid()), owner, folderId, null, null, "CreateFolder", null, null,
                FilesOperationState.Pending, now, now, null, null), "Pictures", TestContext.Current.CancellationToken)).IsSuccess);
            var bindingsPath = Path.Combine(root, "bindings.json");
            var directories = new FilesWorkspaceDirectoryResolver(bindingsPath, _ => null, id => id == profile ? actor.Provider : null);
            Assert.True((await directories.RegisterProfileAsync(profile, folderId, "picture", root, TestContext.Current.CancellationToken)).IsSuccess);
            var sourceId = HostedItemId.New();
            var sourceRevision = new FilesRevisionId(Guid.NewGuid());
            byte[] originalSource = [1, 2, 3, 4];
            var sourceHash = Convert.ToHexString(SHA256.HashData(originalSource));
            await File.WriteAllBytesAsync(Path.Combine(root, "original-source.bin"), originalSource, TestContext.Current.CancellationToken);
            Assert.True((await provider.CommitUploadedContentAsync(new(sourceId, folderId, "source.bin", "image/fixture", sourceRevision, null,
                owner, now, originalSource.Length, "sha256:" + sourceHash, "original-source.bin"), TestContext.Current.CancellationToken)).IsSuccess);
            var source = new PictureSourceAssetReference(sourceId.Value, sourceRevision.Value, sourceHash, originalSource.Length, Guid.NewGuid());
            var document = PictureDocument.Create(20, 30, sourceId.ToString(), sourceRevision.ToString());
            var authorization = new ResourceAuthorizationService(actor, [actor]);
            var writes = true;
            var bridge = new PictureFilesArtifactBridge(actor, current => current == actor.Actor ? actor.Provider : null, directories, authorization, () => writes);
            var created = await bridge.CreateAsync(document, source, TestContext.Current.CancellationToken);
            var backingId = new HostedItemId(created.Artifact.BackingFileId);
            Assert.NotEqual(sourceId, backingId);
            var edited = created.Artifact with { Document = created.Artifact.Document.Rotate().Resize(40, 50) };
            var saved = await bridge.SaveAsync(edited, created.Revision.Id, TestContext.Current.CancellationToken);
            Assert.Equal(saved.Id, (await bridge.SaveAsync(edited, saved.Id, TestContext.Current.CancellationToken)).Id);
            Assert.Equal(saved.Id, (await bridge.SaveAsync(edited, created.Revision.Id, TestContext.Current.CancellationToken)).Id);
            await Assert.ThrowsAsync<InvalidOperationException>(() => bridge.SaveAsync(edited with { Document = edited.Document.Flip(true) }, created.Revision.Id, TestContext.Current.CancellationToken));
            writes = false;
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => bridge.SaveAsync(edited, saved.Id, TestContext.Current.CancellationToken));
            writes = true;
            var replacement = new FilesRevisionId(Guid.NewGuid());
            byte[] replacementBytes = [8, 9, 10];
            await File.WriteAllBytesAsync(Path.Combine(root, "replacement-source.bin"), replacementBytes, TestContext.Current.CancellationToken);
            Assert.True((await provider.CommitUploadedContentAsync(new(sourceId, folderId, "source.bin", "image/fixture", replacement, sourceRevision,
                owner, now.AddSeconds(1), replacementBytes.Length, Convert.ToHexString(SHA256.HashData(replacementBytes)), "replacement-source.bin"), TestContext.Current.CancellationToken)).IsSuccess);
            provider = new DurableDriveProvider(providerPath, location, owner);
            actor.Provider = provider;
            directories = new FilesWorkspaceDirectoryResolver(bindingsPath, _ => null, _ => provider);
            bridge = new(actor, _ => provider, directories, authorization, () => writes);
            var reopened = await bridge.OpenAsync(backingId, TestContext.Current.CancellationToken);
            Assert.Equal(document.DocumentId, reopened.Artifact.Document.DocumentId);
            Assert.Equal(saved.Id, reopened.Revision.Id);
            Assert.Equal(source, reopened.Artifact.SourceAsset);
            Assert.Equal(sourceRevision.ToString(), reopened.Artifact.Document.SourceRevision);
            Assert.Equal(originalSource, await File.ReadAllBytesAsync(Path.Combine(root, "original-source.bin"), TestContext.Current.CancellationToken));
            Assert.Equal(saved.Id, reopened.CasRevisionId);
            var rename = new FilesOperation(new(Guid.NewGuid()), owner, backingId, null, null, "Rename", saved.Id, null,
                FilesOperationState.Pending, now, now, null, null);
            Assert.True((await provider.MutateAsync(rename, "Renamed in Files.9to1p", TestContext.Current.CancellationToken)).IsSuccess);
            reopened = await bridge.OpenAsync(backingId, TestContext.Current.CancellationToken);
            Assert.Equal(saved.Id, reopened.Revision.Id);
            Assert.NotEqual(saved.Id, reopened.CasRevisionId);
            Assert.Equal(source, reopened.Artifact.SourceAsset); // raw source identity stays pinned
            Assert.Equal(saved.Id, (await bridge.SaveAsync(reopened.Artifact, reopened.CasRevisionId, TestContext.Current.CancellationToken)).Id);
            var next = reopened.Artifact with { Document = reopened.Artifact.Document.Flip(true) };
            await Assert.ThrowsAsync<InvalidOperationException>(() => bridge.SaveAsync(next, saved.Id, TestContext.Current.CancellationToken));
            var afterRename = await bridge.SaveAsync(next, reopened.CasRevisionId, TestContext.Current.CancellationToken);
            Assert.NotEqual(saved.Id, afterRename.Id);
            Assert.Equal(reopened.CasRevisionId, afterRename.ParentRevisionId);
            var destinationId = HostedItemId.New();
            Assert.True((await provider.MutateAsync(new(new(Guid.NewGuid()), owner, destinationId, null, null, "CreateFolder", null, null,
                FilesOperationState.Pending, now, now, null, null), "Moved pictures", TestContext.Current.CancellationToken)).IsSuccess);
            var beforeMove = (await provider.GetCurrentArtifactContentAsync(backingId, TestContext.Current.CancellationToken)).Value!;
            Assert.True((await provider.MutateAsync(new(new(Guid.NewGuid()), owner, backingId, folderId, destinationId, "Move", afterRename.Id, null,
                FilesOperationState.Pending, now, now, null, null), null, TestContext.Current.CancellationToken)).IsSuccess);
            provider = new DurableDriveProvider(providerPath, location, owner);
            actor.Provider = provider;
            directories = new FilesWorkspaceDirectoryResolver(bindingsPath, _ => null, _ => provider);
            bridge = new(actor, _ => provider, directories, authorization, () => writes);
            var moved = await bridge.OpenAsync(backingId, TestContext.Current.CancellationToken);
            Assert.Equal(destinationId, (await provider.GetArtifactAsync(backingId, TestContext.Current.CancellationToken)).Value!.ParentFolderId);
            Assert.Equal(destinationId, (await provider.GetAsync(backingId, TestContext.Current.CancellationToken)).Value!.ParentId);
            Assert.Equal(document.DocumentId, moved.Artifact.Document.DocumentId);
            Assert.Equal(source, moved.Artifact.SourceAsset);
            Assert.Equal(sourceRevision.ToString(), moved.Artifact.Document.SourceRevision);
            Assert.Equal(afterRename.Id, moved.Revision.Id);
            Assert.NotEqual(afterRename.Id, moved.CasRevisionId);
            Assert.True(File.Exists(Path.Combine(root, beforeMove.ProviderContentReference!)));
            Assert.Equal(afterRename.Id, (await bridge.SaveAsync(moved.Artifact, moved.CasRevisionId, TestContext.Current.CancellationToken)).Id);
            var movedCandidate = moved.Artifact with { Document = moved.Artifact.Document.Rotate() };
            await Assert.ThrowsAsync<InvalidOperationException>(() => bridge.SaveAsync(movedCandidate, afterRename.Id, TestContext.Current.CancellationToken));
            writes = false;
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => bridge.SaveAsync(movedCandidate, moved.CasRevisionId, TestContext.Current.CancellationToken));
            writes = true;
            var moveSave = await bridge.SaveAsync(movedCandidate, moved.CasRevisionId, TestContext.Current.CancellationToken);
            Assert.Equal(moved.CasRevisionId, moveSave.ParentRevisionId);
            Assert.Equal(source, (await bridge.OpenAsync(backingId, TestContext.Current.CancellationToken)).Artifact.SourceAsset);
            var createdAfterMove = await bridge.CreateAsync(PictureDocument.Create(10, 10), cancellationToken: TestContext.Current.CancellationToken);
            Assert.Equal(folderId, (await provider.GetArtifactAsync(new(createdAfterMove.Artifact.BackingFileId), TestContext.Current.CancellationToken)).Value!.ParentFolderId);
            var foreign = HostedItemId.New();
            Assert.True((await provider.RegisterArtifactAsync(new("canvas", Guid.NewGuid().ToString(), foreign, folderId, nameof(FilesArtifactType.Canvas), "Canvas artifact"), owner, TestContext.Current.CancellationToken)).IsSuccess);
            await Assert.ThrowsAsync<InvalidDataException>(() => bridge.OpenAsync(foreign, TestContext.Current.CancellationToken));
            await Assert.ThrowsAsync<InvalidDataException>(() => bridge.SaveAsync(next with { BackingFileId = foreign.Value }, null, TestContext.Current.CancellationToken));
            var currentContent = (await provider.GetCurrentArtifactContentAsync(backingId, TestContext.Current.CancellationToken)).Value!;
            Assert.True((await provider.CommitDurableRevisionAsync(new(backingId, "picture", "inconsistent-owning-revision", owner, now.AddSeconds(2),
                currentContent.Revision.SizeBytes, currentContent.Revision.ContentHash, currentContent.ProviderContentReference!, currentContent.Revision.Id), TestContext.Current.CancellationToken)).IsSuccess);
            await Assert.ThrowsAsync<InvalidDataException>(() => bridge.OpenAsync(backingId, TestContext.Current.CancellationToken));
            actor.Actor = actor.Actor with { ActorId = "outsider", ProfileId = Guid.NewGuid().ToString() };
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => bridge.OpenAsync(backingId, TestContext.Current.CancellationToken));
        }
        finally { Directory.Delete(root, true); }
    }

    // Explicit trusted fixture resolves actual provider metadata/revisions. Not production OS/profile binding proof.
    private sealed class Authority(AuthenticatedResourceActor actor) : IAuthenticatedResourceActorSource, ICanonicalResourceAccessResolver
    {
        public AuthenticatedResourceActor Actor { get; set; } = actor;
        public DurableDriveProvider Provider { get; set; } = null!;
        public string ResourceKind => "files.item";
        public ValueTask<AuthenticatedResourceActor?> GetCurrentAsync(CancellationToken cancellationToken) => ValueTask.FromResult<AuthenticatedResourceActor?>(Actor);
        public async ValueTask<ResourceAccessDecision> EvaluateAsync(AuthenticatedResourceActor current, string actionId, ResourceScope scope, CancellationToken cancellationToken)
        {
            var metadata = await Provider.GetAsync(new(Guid.Parse(scope.Id)), cancellationToken);
            var allowed = metadata.IsSuccess && metadata.Value!.OwnerPrincipalId == current.ActorId
                && (metadata.Value.CurrentRevisionId?.ToString() ?? "uncommitted") == scope.Revision
                && (actionId, scope.Access) is ("picture.file.open", ResourceAccess.Read) or ("picture.file.save", ResourceAccess.Write)
                    or ("picture.file.create", ResourceAccess.Write) or ("media.asset.read", ResourceAccess.Read);
            return new(allowed, "fixture", current.ActorId, scope.Revision, null);
        }
    }
}
