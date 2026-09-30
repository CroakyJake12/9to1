using Haven.Application;
using HavenOS.Files;
using Xunit;

namespace HavenOS.Apps.Canvas.Tests;

public sealed class CanvasFilesArtifactBridgeTests
{
    [Fact]
    public async Task Canonical_Files_create_edit_restart_reopen_preserves_identity_and_rejects_stale_readonly_and_tampered_content()
    {
        var root = Path.Combine(Path.GetTempPath(), "canvas-files-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var profile = Guid.NewGuid();
            var owner = "local-profile:" + profile.ToString("D");
            var actor = new TestActorSource(new(owner, profile.ToString("D"), null, null, "fixture-auth-1"));
            var location = new FilesLocationId(Guid.NewGuid());
            var providerPath = Path.Combine(root, "drive.json");
            var provider = new DurableDriveProvider(providerPath, location, owner);
            var now = DateTimeOffset.UtcNow;
            var folder = new FilesOperation(new(Guid.NewGuid()), owner, HostedItemId.New(), null, null, "CreateFolder", null, null,
                FilesOperationState.Pending, now, now, null, null);
            Assert.True((await provider.MutateAsync(folder, "Canvases", default)).IsSuccess);
            var bindingsPath = Path.Combine(root, "bindings.json");
            var directories = new FilesWorkspaceDirectoryResolver(bindingsPath, _ => null, id => id == profile ? provider : null);
            Assert.True((await directories.RegisterProfileAsync(profile, folder.ItemId, "canvas", root)).IsSuccess);
            var authority = new TestFilesResolver(() => provider);
            var authorization = new ResourceAuthorizationService(actor, [authority]);
            var writesAllowed = true;
            var bridge = new CanvasFilesArtifactBridge(actor, current => current == actor.Actor ? provider : null, directories, authorization, () => writesAllowed);
            var artifact = CanvasArtifact.Create("Persisted canonical canvas");
            var created = await bridge.CreateAsync(artifact);
            var initial = await bridge.OpenAsync(created.FileId);
            Assert.Equal(artifact.ArtifactId, initial.Artifact.ArtifactId);
            Assert.Equal(created.Revision.Id, initial.Revision.Id);
            var foreignId = HostedItemId.New();
            Assert.True((await provider.RegisterArtifactAsync(new("write", Guid.NewGuid().ToString(), foreignId,
                folder.ItemId, nameof(FilesArtifactType.WriteDocument), "Foreign Write artifact"), owner)).IsSuccess);
            await Assert.ThrowsAsync<InvalidDataException>(() => bridge.OpenAsync(foreignId));
            await Assert.ThrowsAsync<InvalidDataException>(() => bridge.SaveAsync(foreignId, artifact, null));
            var session = new CanvasArtifactSession(initial.Artifact);
            Assert.True(session.RenameArtifact(new(initial.Artifact.RevisionId, Guid.NewGuid(), new(owner, "Fixture actor")), "Edited canonical canvas").IsSuccess);
            var edited = session.GetArtifactSnapshot();
            var saved = await bridge.SaveAsync(created.FileId, edited, created.Revision.Id);
            Assert.NotEqual(created.Revision.Id, saved.Id);
            Assert.Equal(saved.Id, (await bridge.SaveAsync(created.FileId, edited, created.Revision.Id)).Id);
            Assert.Equal(saved.Id, (await bridge.SaveAsync(created.FileId, edited, saved.Id)).Id);
            Assert.True(session.RenameArtifact(new(edited.RevisionId, Guid.NewGuid(), new(owner, "Fixture actor")), "Conflicting candidate").IsSuccess);
            await Assert.ThrowsAsync<InvalidOperationException>(() => bridge.SaveAsync(created.FileId, session.GetArtifactSnapshot(), created.Revision.Id));
            writesAllowed = false;
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => bridge.SaveAsync(created.FileId, edited, saved.Id));
            Assert.Equal(saved.Id, (await provider.GetCurrentArtifactContentAsync(created.FileId)).Value!.Revision.Id);
            writesAllowed = true;
            provider = new DurableDriveProvider(providerPath, location, owner);
            directories = new FilesWorkspaceDirectoryResolver(bindingsPath, _ => null, id => id == profile ? provider : null);
            bridge = new CanvasFilesArtifactBridge(actor, _ => provider, directories, authorization, () => writesAllowed);
            var reopened = await bridge.OpenAsync(created.FileId);
            Assert.Equal(artifact.ArtifactId, reopened.Artifact.ArtifactId);
            Assert.Equal("Edited canonical canvas", reopened.Artifact.DisplayName);
            Assert.Equal(edited.RevisionId, reopened.Artifact.RevisionId);
            Assert.Equal(saved.Id, reopened.Revision.Id);
            Assert.Equal(saved.Id, reopened.CasRevisionId);
            var rename = new FilesOperation(new(Guid.NewGuid()), owner, created.FileId, null, null, "Rename", saved.Id, null,
                FilesOperationState.Pending, now, now, null, null);
            Assert.True((await provider.MutateAsync(rename, "Renamed in Files.9to1c", default)).IsSuccess);
            var renamed = await bridge.OpenAsync(created.FileId);
            Assert.Equal(saved.Id, renamed.Revision.Id); // immutable bytes did not change
            Assert.NotEqual(saved.Id, renamed.CasRevisionId);
            Assert.Equal(renamed.CasRevisionId, (await provider.GetAsync(created.FileId, default)).Value!.CurrentRevisionId);
            Assert.Equal(saved.Id, (await bridge.SaveAsync(created.FileId, renamed.Artifact, renamed.CasRevisionId)).Id);
            var renamedSession = new CanvasArtifactSession(renamed.Artifact);
            Assert.True(renamedSession.RenameArtifact(new(renamed.Artifact.RevisionId, Guid.NewGuid(), new(owner, "Fixture actor")), "Edited after Files rename").IsSuccess);
            var afterRename = renamedSession.GetArtifactSnapshot();
            await Assert.ThrowsAsync<InvalidOperationException>(() => bridge.SaveAsync(created.FileId, afterRename, saved.Id));
            var afterRenameSave = await bridge.SaveAsync(created.FileId, afterRename, renamed.CasRevisionId);
            Assert.Equal(renamed.CasRevisionId, afterRenameSave.ParentRevisionId);
            Assert.Equal(afterRename.RevisionId, (await bridge.OpenAsync(created.FileId)).Artifact.RevisionId);
            var content = (await provider.GetCurrentArtifactContentAsync(created.FileId)).Value!;
            await File.WriteAllTextAsync(Path.Combine(root, content.ProviderContentReference!), "tampered");
            await Assert.ThrowsAsync<InvalidDataException>(() => bridge.OpenAsync(created.FileId));
            actor.Actor = actor.Actor with { ActorId = "outsider", ProfileId = Guid.NewGuid().ToString("D") };
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => bridge.OpenAsync(created.FileId));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private sealed class TestActorSource(AuthenticatedResourceActor actor) : IAuthenticatedResourceActorSource
    {
        public AuthenticatedResourceActor Actor { get; set; } = actor;
        public ValueTask<AuthenticatedResourceActor?> GetCurrentAsync(CancellationToken cancellationToken) => ValueTask.FromResult<AuthenticatedResourceActor?>(Actor);
    }

    // Trusted actor injection is a test fixture. Resource authority resolves the
    // actual durable Files record/revision; this is not a production actor source.
    private sealed class TestFilesResolver(Func<DurableDriveProvider> provider) : ICanonicalResourceAccessResolver
    {
        public string ResourceKind => "files.item";
        public async ValueTask<ResourceAccessDecision> EvaluateAsync(AuthenticatedResourceActor actor, string actionId, ResourceScope scope, CancellationToken cancellationToken)
        {
            var result = await provider().GetAsync(new(Guid.Parse(scope.Id)), cancellationToken);
            var allowed = result.IsSuccess && result.Value!.OwnerPrincipalId == actor.ActorId
                && (result.Value.CurrentRevisionId?.ToString() ?? "uncommitted") == scope.Revision
                && (actionId, scope.Access) is ("canvas.file.open", ResourceAccess.Read) or ("canvas.file.save", ResourceAccess.Write) or ("canvas.file.create", ResourceAccess.Write);
            return new(allowed, allowed ? "Allowed" : "Denied", actor.ActorId, scope.Revision, actor.OrganisationId);
        }
    }
}
