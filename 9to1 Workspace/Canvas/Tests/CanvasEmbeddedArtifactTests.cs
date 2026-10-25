using Haven.Application;
using HavenOS.Files;
using Xunit;

namespace HavenOS.Apps.Canvas.Tests;

public sealed class CanvasEmbeddedArtifactTests
{
    [Fact]
    public async Task Stable_owner_identity_opens_only_that_canonical_file_and_enforces_expected_app_revision()
    {
        var root = Path.Combine(Path.GetTempPath(), "canvas-embedded-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var profile = Guid.NewGuid();
            var owner = "local-profile:" + profile.ToString("D");
            var actor = new TestActorSource(new(owner, profile.ToString("D"), null, null, "fixture-auth-1"));
            var location = new FilesLocationId(Guid.NewGuid());
            var provider = new DurableDriveProvider(Path.Combine(root, "drive.json"), location, owner);
            var now = DateTimeOffset.UtcNow;
            var folder = HostedItemId.New();
            Assert.True((await provider.MutateAsync(new(new(Guid.NewGuid()), owner, folder, null, null, "CreateFolder", null, null,
                FilesOperationState.Pending, now, now, null, null), "Canvases", default)).IsSuccess);
            var directories = new FilesWorkspaceDirectoryResolver(Path.Combine(root, "bindings.json"), _ => null,
                id => id == profile ? provider : null);
            Assert.True((await directories.RegisterProfileAsync(profile, folder, "canvas", root)).IsSuccess);
            var access = new TrackingFilesResolver(() => provider);
            var authorization = new ResourceAuthorizationService(actor, [access]);
            var bridge = new CanvasFilesArtifactBridge(actor, current => current == actor.Actor ? provider : null,
                directories, authorization, () => true);

            var artifact = CanvasArtifact.Create("Referenced canvas");
            var selected = await bridge.CreateAsync(artifact);
            var unrelated = await bridge.CreateAsync(CanvasArtifact.Create("Unrelated canvas"));
            access.ReadScopes.Clear();

            var storeId = (await provider.GetStoreEvidenceAsync()).StoreId;
            var metadata = await bridge.ResolveEmbeddedArtifactMetadataAsync(selected.FileId, storeId, selected.Revision.Id);
            Assert.Equal(artifact.ArtifactId, metadata.ArtifactId);
            Assert.Equal(artifact.RevisionId, metadata.ArtifactRevisionId);
            Assert.Empty(access.ReadScopes);
            await Assert.ThrowsAsync<InvalidOperationException>(() => bridge.ResolveEmbeddedArtifactMetadataAsync(
                selected.FileId, storeId, new FilesRevisionId(Guid.NewGuid())));
            Assert.Empty(access.ReadScopes);

            access.ReadScopes.Clear();
            var opened = await bridge.OpenEmbeddedArtifactAsync(artifact.ArtifactId, artifact.RevisionId);
            Assert.Equal(selected.FileId, opened.FileId);
            Assert.Equal(artifact.ArtifactId, opened.Opened.Artifact.ArtifactId);
            Assert.Equal(artifact.RevisionId, opened.Opened.Artifact.RevisionId);
            Assert.Equal(selected.Revision.Id, opened.Opened.Revision.Id);
            Assert.Equal(selected.Revision.Id, opened.Opened.CasRevisionId);
            AssertOnlySelectedFileReadChecks(access, selected.FileId);
            Assert.DoesNotContain(unrelated.FileId.ToString(), access.ReadScopes.Select(scope => scope.Id));

            access.ReadScopes.Clear();
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                bridge.OpenEmbeddedArtifactAsync(artifact.ArtifactId, Guid.NewGuid()));
            AssertOnlySelectedFileReadChecks(access, selected.FileId);
            Assert.DoesNotContain(unrelated.FileId.ToString(), access.ReadScopes.Select(scope => scope.Id));

            var editor = new CanvasArtifactSession(opened.Opened.Artifact);
            Assert.True(editor.RenameArtifact(new(artifact.RevisionId, Guid.NewGuid(), new(owner, "Fixture actor")),
                "Updated referenced canvas").IsSuccess);
            var updated = editor.GetArtifactSnapshot();
            var saved = await bridge.SaveAsync(opened.FileId, updated, opened.Opened.CasRevisionId,
                opened.Opened.StoreId, actor.Actor);
            access.ReadScopes.Clear();
            await Assert.ThrowsAsync<InvalidOperationException>(() => bridge.ResolveEmbeddedArtifactMetadataAsync(
                selected.FileId, storeId, selected.Revision.Id));
            Assert.Empty(access.ReadScopes);
            access.ReadScopes.Clear();
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                bridge.OpenEmbeddedArtifactAsync(artifact.ArtifactId, artifact.RevisionId));
            AssertOnlySelectedFileReadChecks(access, selected.FileId);
            Assert.DoesNotContain(unrelated.FileId.ToString(), access.ReadScopes.Select(scope => scope.Id));
            access.ReadScopes.Clear();
            var reopened = await bridge.OpenEmbeddedArtifactAsync(artifact.ArtifactId, updated.RevisionId);
            Assert.Equal(opened.FileId, reopened.FileId);
            Assert.Equal(updated.RevisionId, reopened.Opened.Artifact.RevisionId);
            Assert.Equal(saved.Id, reopened.Opened.Revision.Id);
            AssertOnlySelectedFileReadChecks(access, selected.FileId);
            Assert.DoesNotContain(unrelated.FileId.ToString(), access.ReadScopes.Select(scope => scope.Id));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static void AssertOnlySelectedFileReadChecks(TrackingFilesResolver access, HostedItemId selectedFile)
    {
        Assert.Equal(2, access.ReadScopes.Count);
        Assert.All(access.ReadScopes, scope =>
        {
            Assert.Equal("files.item", scope.Kind);
            Assert.Equal(selectedFile.ToString(), scope.Id);
            Assert.Equal(ResourceAccess.Read, scope.Access);
            Assert.False(string.IsNullOrWhiteSpace(scope.Revision));
        });
        Assert.Equal(access.ReadScopes[0].Revision, access.ReadScopes[1].Revision);
    }

    private sealed class TestActorSource(AuthenticatedResourceActor actor) : IAuthenticatedResourceActorSource
    {
        public AuthenticatedResourceActor Actor { get; } = actor;
        public ValueTask<AuthenticatedResourceActor?> GetCurrentAsync(CancellationToken cancellationToken)
            => ValueTask.FromResult<AuthenticatedResourceActor?>(Actor);
    }

    private sealed class TrackingFilesResolver(Func<DurableDriveProvider> provider) : ICanonicalResourceAccessResolver
    {
        public string ResourceKind => "files.item";
        internal List<ResourceScope> ReadScopes { get; } = [];
        public async ValueTask<ResourceAccessDecision> EvaluateAsync(AuthenticatedResourceActor actor,
            string actionId, ResourceScope scope, CancellationToken cancellationToken)
        {
            if (actionId == "canvas.file.open" && scope.Access == ResourceAccess.Read) ReadScopes.Add(scope);
            var result = await provider().GetAsync(new(Guid.Parse(scope.Id)), cancellationToken);
            var allowed = result.IsSuccess && result.Value!.OwnerPrincipalId == actor.ActorId &&
                (result.Value.CurrentRevisionId?.ToString() ?? "uncommitted") == scope.Revision &&
                (actionId, scope.Access) is ("canvas.file.open", ResourceAccess.Read) or
                    ("canvas.file.save", ResourceAccess.Write) or ("canvas.file.create", ResourceAccess.Write);
            return new(allowed, allowed ? "Allowed" : "Denied", actor.ActorId, scope.Revision, actor.OrganisationId);
        }
    }
}
