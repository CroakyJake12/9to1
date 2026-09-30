using System.Text.Json;
using Haven.Application;
using Xunit;

namespace HavenOS.Apps.Canvas.Tests;

public sealed class CanvasAppAiContextTests
{
    [Fact]
    public async Task Authorized_semantic_capture_retains_target_and_excludes_engine_and_extension_blobs()
    {
        var artifact = CanvasArtifact.Create("Private drawing");
        artifact.DocumentSettings.Properties["private-engine"] = JsonSerializer.SerializeToElement("DO-NOT-SEND");
        artifact.ExtensionData = new() { ["untrusted"] = JsonSerializer.SerializeToElement("DO-NOT-SEND") };
        var page = artifact.Pages[0];
        var item = new CanvasObject
        {
            LayerId = page.LayerOrder[0], ObjectTypeId = "fixture-accessible-shape",
            Accessibility = new()
            {
                Name = "Accessible object", Description = "Authored description", AltText = "Authored alternative",
                ExtensionData = new() { ["hidden-engine"] = JsonSerializer.SerializeToElement("DO-NOT-SEND") }
            }
        };
        page.Objects.Add(item);
        page.ObjectOrder.Add(item.ObjectId);
        var stroke = new CanvasInkStroke { LayerId = page.LayerOrder[0], Samples = [new(1, 2), new(3, 4)] };
        page.Strokes.Add(stroke);
        page.StrokeOrder.Add(stroke.StrokeId);
        var target = new CanvasAiTarget(artifact, Guid.NewGuid(), Guid.NewGuid(), page.PageId, [stroke.StrokeId], "Pen", false);
        var fixture = new ScopeFixture(target);
        var context = new CanvasAppAiContext(_ => ValueTask.FromResult<CanvasAiTarget?>(target), fixture.Authorization);
        var snapshot = await context.CaptureAsync(default);
        Assert.Equal(artifact.ArtifactId.ToString(), snapshot.DocumentId);
        Assert.Equal(artifact.RevisionId.ToString(), snapshot.Revision);
        Assert.Equal("ReadOnly", snapshot.HostState);
        Assert.Equal(stroke.StrokeId.ToString(), Assert.Single(snapshot.SelectionIds!));
        var semantic = snapshot.SemanticState["Canvas"].GetRawText();
        Assert.Contains(stroke.StrokeId.ToString(), semantic);
        Assert.DoesNotContain("DO-NOT-SEND", semantic);
        Assert.DoesNotContain("private-engine", semantic);
        Assert.DoesNotContain("hidden-engine", semantic);
        Assert.Contains("Accessible object", semantic);
        Assert.Contains("Authored alternative", semantic);
        Assert.Equal(2, fixture.Checks);
    }

    [Fact]
    public async Task Revocation_or_target_revision_change_during_capture_rejects_context()
    {
        var artifact = CanvasArtifact.Create();
        var target = new CanvasAiTarget(artifact, Guid.NewGuid(), Guid.NewGuid(), artifact.Pages[0].PageId, [], "Select", true);
        var fixture = new ScopeFixture(target) { Deny = true };
        var context = new CanvasAppAiContext(_ => ValueTask.FromResult<CanvasAiTarget?>(target), fixture.Authorization);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => context.CaptureAsync(default).AsTask());
        fixture.Deny = false;
        var reads = 0;
        context = new CanvasAppAiContext(_ =>
        {
            if (++reads > 1) artifact.RevisionId = Guid.NewGuid();
            return ValueTask.FromResult<CanvasAiTarget?>(target);
        }, fixture.Authorization);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => context.CaptureAsync(default).AsTask());
    }

    [Fact]
    public async Task Typed_new_stroke_options_are_semantic_editor_state_and_concurrent_style_changes_reject_capture()
    {
        var artifact = CanvasArtifact.Create();
        var style = new CanvasRnoteInkStyle(CanvasRnoteInkKind.Marker, "#FF0000FF", 12, 0.25);
        var target = new CanvasAiTarget(artifact, Guid.NewGuid(), Guid.NewGuid(), artifact.Pages[0].PageId, [], "Pen", true, style);
        var fixture = new ScopeFixture(target);
        var context = new CanvasAppAiContext(_ => ValueTask.FromResult<CanvasAiTarget?>(target), fixture.Authorization);
        var snapshot = await context.CaptureAsync(default);
        var options = snapshot.SemanticState["Canvas"].GetProperty("InkOptions");
        Assert.Equal("Marker", options.GetProperty("Kind").GetString());
        Assert.Equal("#FF0000FF", options.GetProperty("Color").GetString());
        Assert.Equal(12, options.GetProperty("BaseWidth").GetDouble());
        Assert.Contains("new strokes only", options.GetProperty("Scope").GetString());
        var reads = 0;
        context = new CanvasAppAiContext(_ => ValueTask.FromResult<CanvasAiTarget?>(++reads == 1 ? target : target with { InkStyle = style with { BaseWidth = 18 } }), fixture.Authorization);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => context.CaptureAsync(default).AsTask());
    }

    [Fact]
    public async Task Invalid_native_ink_properties_cannot_be_presented_as_supported_model_context()
    {
        var artifact = CanvasArtifact.Create();
        var target = new CanvasAiTarget(artifact, Guid.NewGuid(), Guid.NewGuid(), artifact.Pages[0].PageId, [], "Pen", true,
            new(BaseWidth: double.NaN));
        var fixture = new ScopeFixture(target);
        var context = new CanvasAppAiContext(_ => ValueTask.FromResult<CanvasAiTarget?>(target), fixture.Authorization);
        await Assert.ThrowsAsync<ArgumentException>(() => context.CaptureAsync(default).AsTask());
    }

    private sealed class ScopeFixture : IAuthenticatedResourceActorSource, ICanonicalResourceAccessResolver
    {
        private readonly CanvasAiTarget _target;
        private readonly AuthenticatedResourceActor _actor = new("fixture-os-actor", Guid.NewGuid().ToString(), null, null, "fixture-auth-revision");
        public ScopeFixture(CanvasAiTarget target) { _target = target; Authorization = new(this, [this]); }
        public ResourceAuthorizationService Authorization { get; }
        public string ResourceKind => "files.item";
        public bool Deny { get; set; }
        public int Checks { get; private set; }
        public ValueTask<AuthenticatedResourceActor?> GetCurrentAsync(CancellationToken cancellationToken) => ValueTask.FromResult<AuthenticatedResourceActor?>(_actor);
        public ValueTask<ResourceAccessDecision> EvaluateAsync(AuthenticatedResourceActor actor, string actionId, ResourceScope scope, CancellationToken cancellationToken)
        {
            Checks++;
            return ValueTask.FromResult(new ResourceAccessDecision(!Deny && actionId == "canvas.file.open" && scope.Access == ResourceAccess.Read
                && scope.Id == _target.FileId.ToString() && scope.Revision == _target.FilesRevisionId.ToString(), "fixture", actor.ActorId, scope.Revision, null));
        }
    }
}
