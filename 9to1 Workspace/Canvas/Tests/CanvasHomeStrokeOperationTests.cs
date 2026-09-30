using Haven.Application;
using HavenOS.Files;
using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;
using Xunit;

namespace HavenOS.Apps.Canvas.Tests;

public sealed class CanvasHomeStrokeOperationTests
{
    [Fact]
    public async Task Actual_os_profile_Home_capability_commits_one_native_stroke_with_Files_CAS_and_cannot_replay()
    {
        await using var fixture = await Fixture.Create();
        var opened = await fixture.Files.OpenAsync(fixture.FileId);
        var callerSamples = new List<RnotePointerSample> { new(10, 20, 0.2), new(40, 60, 0.7) };
        var intent = CanvasStrokeWriteIntent.Capture(fixture.FileId, opened.CasRevisionId, opened.Artifact.ArtifactId,
            opened.Artifact.RevisionId, Guid.NewGuid(), callerSamples);
        var capability = await fixture.Approve(intent);
        callerSamples[0] = new(900, 950, 0.9);
        var operation = new CanvasHomeStrokeOperation(fixture.Files, fixture.Home, fixture.Actors);
        var committed = await operation.ExecuteAsync(intent, capability);
        var stroke = Assert.Single(committed.Artifact.Pages[0].Strokes);
        Assert.Equal(intent.OperationId, stroke.StrokeId);
        Assert.Equal(10, stroke.Samples[0].X);
        Assert.Equal((await fixture.Actors.GetCurrentAsync(default))!.ActorId, committed.FilesRevision.ActorId);
        Assert.Equal(opened.CasRevisionId, committed.FilesRevision.ParentRevisionId);
        var after = await fixture.Files.OpenAsync(fixture.FileId);
        Assert.Equal(committed.FilesRevision.Id, after.CasRevisionId);
        Assert.Equal(committed.Artifact.RevisionId, after.Artifact.RevisionId);
        using var reopened = CanvasRnoteDocument.Open(CanvasArtifactCodec.Serialize(after.Artifact));
        using var selected = RnoteCanvasEngine.Open(reopened.ExportCanonicalStrokeSelection([stroke.StrokeId], after.Artifact.RevisionId));
        Assert.Single(selected.ReadStrokeKeys());
        await Assert.ThrowsAsync<InvalidOperationException>(() => operation.ExecuteAsync(intent, capability));
        Assert.Single((await fixture.Files.OpenAsync(fixture.FileId)).Artifact.Pages[0].Strokes);
    }

    [Fact]
    public async Task Foreign_issuer_changed_arguments_and_readonly_owner_do_not_publish_a_native_candidate()
    {
        await using var fixture = await Fixture.Create();
        var opened = await fixture.Files.OpenAsync(fixture.FileId);
        var intent = CanvasStrokeWriteIntent.Capture(fixture.FileId, opened.CasRevisionId, opened.Artifact.ArtifactId,
            opened.Artifact.RevisionId, Guid.NewGuid(), [new(10, 20, 0.2), new(40, 60, 0.7)]);
        var capability = await fixture.Approve(intent);
        var foreignBroker = new HomeResourceOperationBroker(fixture.Resources, fixture.Permissions);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => new CanvasHomeStrokeOperation(fixture.Files, foreignBroker, fixture.Actors).ExecuteAsync(intent, capability));
        var changed = CanvasStrokeWriteIntent.Capture(fixture.FileId, opened.CasRevisionId, opened.Artifact.ArtifactId,
            opened.Artifact.RevisionId, Guid.NewGuid(), [new(900, 950, 0.2), new(940, 960, 0.7)]);
        var owner = new CanvasHomeStrokeOperation(fixture.Files, fixture.Home, fixture.Actors);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => owner.ExecuteAsync(changed, capability));
        fixture.WritesAllowed = false;
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => owner.ExecuteAsync(intent, capability));
        Assert.Empty((await fixture.Files.OpenAsync(fixture.FileId)).Artifact.Pages[0].Strokes);
        fixture.WritesAllowed = true;
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => owner.ExecuteAsync(intent, capability));
        Assert.Equal(opened.CasRevisionId, (await fixture.Files.OpenAsync(fixture.FileId)).CasRevisionId);
    }

    [Fact]
    public async Task Shared_insertion_dispatch_commits_the_exact_approved_canonical_stroke_and_rejects_changed_content()
    {
        await using var fixture = await Fixture.Create();
        var opened = await fixture.Files.OpenAsync(fixture.FileId);
        var samples = new RnotePointerSample[] { new(10, 20, .3), new(50, 70, .8) };
        var operationId = Guid.NewGuid();
        var intent = CanvasStrokeWriteIntent.Capture(fixture.FileId, opened.CasRevisionId, opened.Artifact.ArtifactId,
            opened.Artifact.RevisionId, operationId, samples);
        var capability = await fixture.Approve(intent);
        var provider = new CanvasHomeInkInsertionProvider(fixture.Files,
            new CanvasHomeStrokeOperation(fixture.Files, fixture.Home, fixture.Actors), intent, capability);
        var engine = new HomeProductivityEngine(handlers: [new CanvasInkObjectHandler()], artifactActions: [provider]);
        var stroke = new CanvasInkStroke
        {
            StrokeId = operationId, RevisionId = operationId, LayerId = opened.Artifact.Pages[0].LayerOrder[0],
            Samples = samples.Select(sample => new CanvasStrokeSample(sample.X, sample.Y, sample.Pressure, sample.TiltX, sample.TiltY)).ToList(),
            ResolvedBrushProperties = new() { EngineParameters = new() { ["brushStyle"] = System.Text.Json.JsonSerializer.SerializeToElement("solid") } }
        };
        var value = engine.CreateObject("drawing.ink", operationId, System.Text.Json.JsonSerializer.SerializeToElement(stroke));
        var context = new HomeProductivityContext("canvas", opened.Artifact.ArtifactId.ToString(), 0, [], new HashSet<string> { "drawing.ink" })
        { ArtifactRevision = new(VersionId: opened.Artifact.RevisionId) };
        var changed = new CanvasInkObjectHandler().Transform(value, new("ink.color", 1, "drawing.ink", [operationId],
            System.Text.Json.JsonSerializer.SerializeToElement(new { color = "#FFFF0000" }), 0));
        var rejected = await engine.InsertObjectsAsync(context, [changed], operationId.ToString());
        Assert.False(rejected.Succeeded);
        Assert.Equal("InsertionContentMismatch", rejected.Code);
        Assert.Empty((await fixture.Files.OpenAsync(fixture.FileId)).Artifact.Pages[0].Strokes);
        var result = await engine.InsertObjectsAsync(context, [value], operationId.ToString());
        Assert.True(result.Succeeded, result.Message);
        Assert.Equal(HomeProductivityArtifactOutcome.Committed, result.Outcome);
        Assert.Equal(operationId, Assert.Single(result.AffectedObjectIds));
        var reopened = await fixture.Files.OpenAsync(fixture.FileId);
        Assert.Equal(reopened.Artifact.RevisionId, result.ArtifactRevision!.VersionId);
        Assert.Equal(operationId, Assert.Single(reopened.Artifact.Pages[0].Strokes).StrokeId);
        using var native = CanvasRnoteDocument.Open(CanvasArtifactCodec.Serialize(reopened.Artifact));
        using var selected = RnoteCanvasEngine.Open(native.ExportCanonicalStrokeSelection([operationId], reopened.Artifact.RevisionId));
        Assert.Single(selected.ReadStrokeKeys());
        var replay = await engine.InsertObjectsAsync(context, [value], operationId.ToString());
        Assert.False(replay.Succeeded);
        Assert.Equal("RevisionConflict", replay.Code);
        Assert.Equal(reopened.CasRevisionId, (await fixture.Files.OpenAsync(fixture.FileId)).CasRevisionId);
    }

    [Fact]
    public async Task Authentication_refresh_after_Home_claim_cannot_publish_under_a_new_actor_context()
    {
        await using var fixture = await Fixture.Create();
        var ct = CancellationToken.None;
        var opened = await fixture.Files.OpenAsync(fixture.FileId, ct);
        var intent = CanvasStrokeWriteIntent.Capture(fixture.FileId, opened.CasRevisionId, opened.Artifact.ArtifactId,
            opened.Artifact.RevisionId, Guid.NewGuid(), [new(10, 20, .3), new(30, 40, .6)]);
        var capability = await fixture.Approve(intent);
        var claimedActor = (await fixture.Actors.GetCurrentAsync(ct))!;
        fixture.OnWriteAdmission = () => fixture.ActorOverride = claimedActor with { AuthenticationRevision = "refreshed-after-claim" };
        var owner = new CanvasHomeStrokeOperation(fixture.Files, fixture.Home, fixture.Actors);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => owner.ExecuteAsync(intent, capability, ct));
        Assert.NotNull(fixture.ActorOverride);
        var current = await fixture.Files.OpenAsync(fixture.FileId, ct);
        Assert.Equal(opened.CasRevisionId, current.CasRevisionId);
        Assert.Equal(opened.Artifact.RevisionId, current.Artifact.RevisionId);
        Assert.Empty(current.Artifact.Pages[0].Strokes);
    }

    private sealed class Fixture : IAsyncDisposable, ICanonicalResourceAccessResolver, IAuthenticatedResourceActorSource
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "canvas-home-owner-" + Guid.NewGuid().ToString("N"));
        private DurableDriveProvider _provider = null!;
        private HomeLocalProfileIdentity _actors = null!;
        public IAuthenticatedResourceActorSource Actors => this;
        public AuthenticatedResourceActor? ActorOverride { get; set; }
        public Action? OnWriteAdmission { get; set; }
        public ValueTask<AuthenticatedResourceActor?> GetCurrentAsync(CancellationToken cancellationToken) =>
            ActorOverride is { } actor ? ValueTask.FromResult<AuthenticatedResourceActor?>(actor) : _actors.GetCurrentAsync(cancellationToken);
        public CanvasFilesArtifactBridge Files { get; private set; } = null!;
        public HomeResourceOperationBroker Home { get; private set; } = null!;
        public HomePermissionTrustService Permissions { get; private set; } = null!;
        public ResourceAuthorizationService Resources { get; private set; } = null!;
        public HostedItemId FileId { get; private set; }
        public bool WritesAllowed { get; set; } = true;
        public string ResourceKind => "files.item";

        public static async Task<Fixture> Create()
        {
            var fixture = new Fixture();
            Directory.CreateDirectory(fixture._root);
            var store = new FileHomeCoreStateStore(Path.Combine(fixture._root, "home.json"));
            fixture._actors = new(store, new OperatingSystemPrincipalSource());
            var actor = await fixture.Actors.GetCurrentAsync(default) ?? throw new InvalidOperationException("Actual OS profile unavailable.");
            var profile = Guid.Parse(actor.ProfileId);
            fixture._provider = new(Path.Combine(fixture._root, "drive.json"), new(Guid.NewGuid()), actor.ActorId);
            var now = DateTimeOffset.UtcNow;
            var folder = new FilesOperation(new(Guid.NewGuid()), actor.ActorId, HostedItemId.New(), null, null, "CreateFolder", null, null,
                FilesOperationState.Pending, now, now, null, null);
            Assert.True((await fixture._provider.MutateAsync(folder, "Canvases", default)).IsSuccess);
            var directories = new FilesWorkspaceDirectoryResolver(Path.Combine(fixture._root, "bindings.json"), _ => null,
                id => id == profile ? fixture._provider : null);
            Assert.True((await directories.RegisterProfileAsync(profile, folder.ItemId, "canvas", fixture._root)).IsSuccess);
            fixture.Resources = new(fixture.Actors, [fixture]);
            fixture.Files = new(fixture.Actors, current => current.ActorId == actor.ActorId && current.ProfileId == actor.ProfileId ? fixture._provider : null,
                directories, fixture.Resources, () => {
                    var admission = fixture.OnWriteAdmission; fixture.OnWriteAdmission = null; admission?.Invoke();
                    return fixture.WritesAllowed;
                });
            fixture.Permissions = new(store, (app, action) => app == "canvas" && action == "canvas.file.save"
                ? new(HavenOS.Home.PermissionsTrustNotifications.HomePermissionRisk.High, true, false, true) : null);
            fixture.Home = new(fixture.Resources, fixture.Permissions);
            using var document = CanvasRnoteDocument.Create();
            fixture.FileId = (await fixture.Files.CreateAsync(document.Snapshot)).FileId;
            return fixture;
        }

        public async Task<HomeResourceExecutionCapability> Approve(CanvasStrokeWriteIntent intent)
        {
            var pending = await Home.AuthorizeAsync(CanvasStrokeWriteIntent.TargetAppId, CanvasStrokeWriteIntent.ActionId, intent.Scopes,
                intent.Arguments, "Draw exactly the captured stroke into this Canvas revision", null, "native-owner-test-session");
            Assert.Equal(HomePermissionRequestState.PendingApproval, pending.State);
            Assert.True((await Permissions.DecideAsync(pending.RequestId, HomeApprovalChoice.Accept)).Succeeded);
            return Assert.IsType<HomeResourceExecutionCapability>(await Home.BeginExecutionCapabilityAsync(pending.RequestId, intent.Arguments));
        }

        public async ValueTask<ResourceAccessDecision> EvaluateAsync(AuthenticatedResourceActor actor, string actionId, ResourceScope scope,
            CancellationToken cancellationToken)
        {
            var item = await _provider.GetAsync(new(Guid.Parse(scope.Id)), cancellationToken);
            var allowed = item.IsSuccess && item.Value!.OwnerPrincipalId == actor.ActorId &&
                (item.Value.CurrentRevisionId?.ToString() ?? "uncommitted") == scope.Revision &&
                (actionId, scope.Access) is ("canvas.file.open", ResourceAccess.Read) or ("canvas.file.save", ResourceAccess.Write) or ("canvas.file.create", ResourceAccess.Write);
            return new(allowed, "fixture-canonical-files", actor.ActorId, scope.Revision, null);
        }
        public ValueTask DisposeAsync() { Directory.Delete(_root, true); return ValueTask.CompletedTask; }
    }
}
