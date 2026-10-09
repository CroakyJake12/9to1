using Haven.Application;
using System.IO.Compression;
using System.Text.Json;
using HavenOS.Files;
using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;
using Xunit;

namespace HavenOS.Apps.Canvas.Tests;

public sealed class CanvasHomeEraserOperationTests
{
    private static async Task Add(Fixture fixture,RnotePointerSample[] samples)
    {
        var opened=await fixture.Files.OpenAsync(fixture.FileId);
        var intent=CanvasStrokeWriteIntent.Capture(fixture.FileId,opened.CasRevisionId,opened.Artifact.ArtifactId,opened.Artifact.RevisionId,Guid.NewGuid(),samples);
        var capability=await fixture.Approve(intent);
        var committed=await new CanvasHomeStrokeOperation(fixture.Files,fixture.Home,fixture.Actors).ExecuteAsync(intent,capability);
        Assert.True((await fixture.Home.CompleteExecutionAsync(capability,new(HomePermissionRequestState.Succeeded,"CANVAS_COMMITTED","Actual owning stroke revision committed.",[new("files.item",committed.FileId.ToString())]))).Succeeded);
    }
    [Fact]
    public async Task Genuine_multi_stroke_erase_is_one_exact_approved_revision_and_survives_restart_Undo()
    {
        await using var fixture=await Fixture.Create();
        await Add(fixture,[new(20,30,.2),new(80,90,.8)]);
        await Add(fixture,[new(20,32,.3),new(80,92,.7)]);
        await Add(fixture,[new(500,600,.4),new(540,660,.6)]);
        var opened=await fixture.Files.OpenAsync(fixture.FileId);
        var remaining=opened.Artifact.Pages[0].Strokes.Last().StrokeId;
        using var before=CanvasRnoteDocument.Open(CanvasArtifactCodec.Serialize(opened.Artifact));
        var retained=before.ExportCanonicalStrokeSelection([remaining],opened.Artifact.RevisionId);
        var intent=CanvasEraserIntent.Capture(fixture.FileId,opened,[new(49,59,.5),new(51,61,.5)],20);
        Assert.Equal(2,intent.StrokeIds.Count);
        var capability=await fixture.Approve(intent);
        var committed=await new CanvasHomeEraserOperation(fixture.Files,fixture.Home,fixture.Actors).ExecuteAsync(intent,capability);
        Assert.True((await fixture.Home.CompleteExecutionAsync(capability,new(HomePermissionRequestState.Succeeded,"CANVAS_ERASE_COMMITTED","Actual owning eraser revision committed.",[new("files.item",committed.FileId.ToString())]))).Succeeded);
        Assert.Equal(opened.CasRevisionId,committed.FilesRevision.ParentRevisionId);
        var fresh=await fixture.Files.OpenAsync(fixture.FileId,intent.ExpectedStoreId);
        Assert.Equal(remaining,Assert.Single(fresh.Artifact.Pages[0].Strokes).StrokeId);
        using var reopened=CanvasRnoteDocument.Open(CanvasArtifactCodec.Serialize(fresh.Artifact));
        Assert.Single(reopened.Snapshot.Pages[0].Strokes);
        NativeRnoteTrashEraserTests.AssertSameSelectedStateWithKnownChronologyAdvance(retained,reopened.ExportCanonicalStrokeSelection([remaining],fresh.Artifact.RevisionId),checked((uint)intent.StrokeIds.Count));
        var history=CanvasHistoryIntent.Capture(fixture.FileId,fresh.CasRevisionId,fresh.Artifact.ArtifactId,fresh.Artifact.RevisionId,Guid.NewGuid(),CanvasHistoryKind.Undo,fresh.StoreId);
        var historyCapability=await fixture.Approve(history);
        var restored=await new CanvasHomeHistoryOperation(fixture.Files,fixture.Home,fixture.Actors).ExecuteAsync(history,historyCapability);
        Assert.True((await fixture.Home.CompleteExecutionAsync(historyCapability,new(HomePermissionRequestState.Succeeded,"CANVAS_UNDO_COMMITTED","Actual restored revision committed.",[new("files.item",restored.FileId.ToString())]))).Succeeded);
        Assert.Equal(3,restored.Artifact.Pages[0].Strokes.Count);
        using var restoredDonor=CanvasRnoteDocument.Open(CanvasArtifactCodec.Serialize(restored.Artifact));
        using var engine=RnoteCanvasEngine.Open(restoredDonor.ExportRnote()); Assert.Equal(3,engine.ReadStrokeKeys().Length);
    }
    [Fact]
    public async Task Stale_eraser_target_denies_publication_without_replaying_a_consumed_owner()
    {
        await using var fixture=await Fixture.Create(); await Add(fixture,[new(20,30,.2),new(80,90,.8)]);
        var opened=await fixture.Files.OpenAsync(fixture.FileId);
        var intent=CanvasEraserIntent.Capture(fixture.FileId,opened,[new(49,59,.5),new(51,61,.5)],20);
        var capability=await fixture.Approve(intent);
        var owner=new CanvasHomeEraserOperation(fixture.Files,fixture.Home,fixture.Actors);
        await owner.ExecuteAsync(intent,capability);
        var bytes=await File.ReadAllBytesAsync(fixture.StatePath);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>owner.ExecuteAsync(intent,capability));
        Assert.Equal(bytes,await File.ReadAllBytesAsync(fixture.StatePath));
    }
    private static bool SameNative(byte[] left, byte[] right)
    {
        using var leftStream = new GZipStream(new MemoryStream(left), CompressionMode.Decompress);
        using var rightStream = new GZipStream(new MemoryStream(right), CompressionMode.Decompress);
        using var leftJson = JsonDocument.Parse(leftStream);
        using var rightJson = JsonDocument.Parse(rightStream);
        return JsonElement.DeepEquals(leftJson.RootElement, rightJson.RootElement);
    }
    private sealed class Fixture : IAsyncDisposable, ICanonicalResourceAccessResolver, IAuthenticatedResourceActorSource
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "canvas-history-owner-" + Guid.NewGuid().ToString("N"));
        private DurableDriveProvider _provider = null!;
        private HomeLocalProfileIdentity _actors = null!;
        public IAuthenticatedResourceActorSource Actors => this;
        public AuthenticatedResourceActor? ActorOverride { get; set; }
        public Action? OnWriteAdmission { get; set; }
        public Action<int>? OnProviderResolution { get; set; }
        private int _providerResolutions;
        public string StatePath => Path.Combine(_root, "drive.json");
        private DurableDriveProvider ResolveProvider()
        {
            if (OnProviderResolution is { } callback) callback(Interlocked.Increment(ref _providerResolutions));
            return _provider;
        }
        public ValueTask<AuthenticatedResourceActor?> GetCurrentAsync(CancellationToken cancellationToken) =>
            ActorOverride is { } actor ? ValueTask.FromResult<AuthenticatedResourceActor?>(actor) : _actors.GetCurrentAsync(cancellationToken);
        public CanvasFilesArtifactBridge Files { get; private set; } = null!;
        public HomeResourceOperationBroker Home { get; private set; } = null!;
        public HomePermissionTrustService Permissions { get; private set; } = null!;
        public ResourceAuthorizationService Resources { get; private set; } = null!;
        public HostedItemId FileId { get; private set; }
        private HostedItemId _folderId;
        public async Task<CanvasCreationTarget> CreationTarget()
        {
            var storeId = (await _provider.GetStoreEvidenceAsync(default)).StoreId;
            var revision = (await _provider.GetAsync(_folderId, default)).Value!.CurrentRevisionId;
            return new(_folderId, revision) { ExpectedStoreId = storeId };
        }
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
            fixture._folderId = folder.ItemId;
            fixture.Resources = new(fixture.Actors, [fixture]);
            fixture.Files = new(fixture.Actors, current => current.ActorId == actor.ActorId && current.ProfileId == actor.ProfileId ? fixture.ResolveProvider() : null,
                directories, fixture.Resources, () => {
                    var admission = fixture.OnWriteAdmission; fixture.OnWriteAdmission = null; admission?.Invoke();
                    return fixture.WritesAllowed;
                });
            fixture.Permissions = new(store, new CanvasNativeActionPolicies().TryGet);
            fixture.Home = new(fixture.Resources, fixture.Permissions);
            using var document = CanvasRnoteDocument.Create();
            fixture.FileId = (await fixture.Files.CreateAsync(document.Snapshot)).FileId;
            return fixture;
        }

        public async Task<HomeResourceExecutionCapability> Approve(CanvasEraserIntent intent)
        {
            var pending=await Home.AuthorizeAsync(CanvasStrokeWriteIntent.TargetAppId,CanvasStrokeWriteIntent.ActionId,intent.Scopes,
                intent.Arguments,"Erase exactly the genuine previewed canonical ink",null,"eraser-owner-test-session");
            Assert.Equal(HomePermissionRequestState.PendingApproval,pending.State);
            Assert.True((await Permissions.DecideAsync(pending.RequestId,HomeApprovalChoice.Accept)).Succeeded);
            return Assert.IsType<HomeResourceExecutionCapability>(await Home.BeginExecutionCapabilityAsync(pending.RequestId,intent.Arguments));
        }
        public async Task<HomeResourceExecutionCapability> Approve(CanvasHistoryIntent intent)
        {
            var pending = await Home.AuthorizeAsync(CanvasStrokeWriteIntent.TargetAppId, CanvasStrokeWriteIntent.ActionId, intent.Scopes,
                intent.Arguments, "Restore this exact current Canvas history revision", null, "history-owner-test-session");
            Assert.Equal(HomePermissionRequestState.PendingApproval, pending.State);
            Assert.True((await Permissions.DecideAsync(pending.RequestId, HomeApprovalChoice.Accept)).Succeeded);
            return Assert.IsType<HomeResourceExecutionCapability>(await Home.BeginExecutionCapabilityAsync(pending.RequestId, intent.Arguments));
        }

        public async Task<HomeResourceExecutionCapability> Approve(CanvasCreateIntent intent)
        {
            var pending = await Home.AuthorizeAsync(CanvasCreateIntent.TargetAppId, CanvasCreateIntent.ActionId, intent.Scopes,
                intent.Arguments, "Create the exact captured native Canvas in the configured folder", null, "native-owner-test-session");
            Assert.Equal(HomePermissionRequestState.PendingApproval, pending.State);
            Assert.True((await Permissions.DecideAsync(pending.RequestId, HomeApprovalChoice.Accept)).Succeeded);
            return Assert.IsType<HomeResourceExecutionCapability>(await Home.BeginExecutionCapabilityAsync(pending.RequestId, intent.Arguments));
        }

        public async Task<HomeResourceExecutionCapability> Approve(CanvasStrokeEditIntent intent)
        {
            var pending = await Home.AuthorizeAsync(CanvasStrokeWriteIntent.TargetAppId, CanvasStrokeWriteIntent.ActionId, intent.Scopes,
                intent.Arguments, "Edit exactly the captured native stroke in this Canvas revision", null, "native-owner-test-session");
            Assert.Equal(HomePermissionRequestState.PendingApproval, pending.State);
            Assert.True((await Permissions.DecideAsync(pending.RequestId, HomeApprovalChoice.Accept)).Succeeded);
            return Assert.IsType<HomeResourceExecutionCapability>(await Home.BeginExecutionCapabilityAsync(pending.RequestId, intent.Arguments));
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
