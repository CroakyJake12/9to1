using Haven.Application;
using Xunit;

namespace HavenOS.Images.Tests;

public sealed class PictureAppAiContextTests
{
    [Fact]
    public async Task Semantic_context_keeps_document_and_source_identity_without_exporting_a_machine_path()
    {
        var document = PictureDocument.Create(20, 30, "canonical-source-asset", "canonical-source-revision", "/private/person/source.png").Rotate();
        var target = new PictureAiTarget(document, Guid.NewGuid(), Guid.NewGuid(), "Crop", "Automatic", false);
        var fixture = new Authority(target);
        var context = new PictureAppAiContext(_ => ValueTask.FromResult<PictureAiTarget?>(target), fixture.Service);
        var snapshot = await context.CaptureAsync(TestContext.Current.CancellationToken);
        Assert.Equal(document.DocumentId.ToString(), snapshot.DocumentId);
        Assert.Equal("1", snapshot.Revision);
        Assert.Equal("ReadOnly", snapshot.HostState);
        var semantic = snapshot.SemanticState["Picture"].GetRawText();
        Assert.Contains("canonical-source-asset", semantic);
        Assert.Contains("canonical-source-revision", semantic);
        Assert.Contains("rotate", semantic);
        Assert.DoesNotContain("/private/person", semantic);
        Assert.DoesNotContain("SourcePath", semantic);
        Assert.Equal(2, fixture.Checks);
    }

    [Fact]
    public async Task Current_acl_revocation_or_revision_change_prevents_context_release()
    {
        var target = new PictureAiTarget(PictureDocument.Create(10, 10), Guid.NewGuid(), Guid.NewGuid(), "Select", "Automatic", true);
        var fixture = new Authority(target) { Deny = true };
        var context = new PictureAppAiContext(_ => ValueTask.FromResult<PictureAiTarget?>(target), fixture.Service);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => context.CaptureAsync(TestContext.Current.CancellationToken).AsTask());
        fixture.Deny = false;
        var reads = 0;
        context = new PictureAppAiContext(_ => ValueTask.FromResult<PictureAiTarget?>(++reads > 1 ? target with { Document = target.Document.Rotate() } : target), fixture.Service);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => context.CaptureAsync(TestContext.Current.CancellationToken).AsTask());
    }

    private sealed class Authority : IAuthenticatedResourceActorSource, ICanonicalResourceAccessResolver
    {
        private readonly PictureAiTarget _target;
        private readonly AuthenticatedResourceActor _actor = new("trusted-fixture", Guid.NewGuid().ToString(), null, null, "auth-fixture");
        public Authority(PictureAiTarget target) { _target = target; Service = new(this, [this]); }
        public ResourceAuthorizationService Service { get; }
        public string ResourceKind => "files.item";
        public bool Deny { get; set; }
        public int Checks { get; private set; }
        public ValueTask<AuthenticatedResourceActor?> GetCurrentAsync(CancellationToken cancellationToken) => ValueTask.FromResult<AuthenticatedResourceActor?>(_actor);
        public ValueTask<ResourceAccessDecision> EvaluateAsync(AuthenticatedResourceActor actor, string actionId, ResourceScope scope, CancellationToken cancellationToken)
        {
            Checks++;
            return ValueTask.FromResult(new ResourceAccessDecision(!Deny && actionId == "picture.file.open" && scope.Access == ResourceAccess.Read
                && scope.Id == _target.BackingFileId.ToString() && scope.Revision == _target.FilesRevisionId.ToString(), "fixture", actor.ActorId, scope.Revision, null));
        }
    }
}
