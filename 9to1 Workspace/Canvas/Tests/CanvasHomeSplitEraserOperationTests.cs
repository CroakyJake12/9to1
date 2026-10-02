using Haven.Application;
using HavenOS.Files;
using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;
using Xunit;
namespace HavenOS.Apps.Canvas.Tests;
public sealed class CanvasHomeSplitEraserOperationTests
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
    public async Task Genuine_split_Home_approval_commits_one_schema2_revision_exact_fragment_IDs_and_fresh_approved_Undo()
    {
        await using var fixture = await Fixture.Create();
        await Add(fixture,NativeRnoteSplitMaterializationTests.LongStroke());
        var opened = await fixture.Files.OpenAsync(fixture.FileId);
        var source = Assert.Single(opened.Artifact.Pages[0].Strokes);
        using var donor = CanvasRnoteDocument.Open(CanvasArtifactCodec.Serialize(opened.Artifact));
        var samples = NativeRnoteSplitMaterializationTests.GenuineMiddleGesture(donor.ExportRnote());
        var intent = CanvasSplitEraseIntent.Capture(fixture.FileId,opened,samples,1);
        Assert.Single(intent.Preview.FragmentIdentities);
        var capability = await fixture.Approve(intent);
        var owner = new CanvasHomeSplitEraserOperation(fixture.Files,fixture.Home,fixture.Actors);
        var committed = await owner.ExecuteAsync(intent,capability);
        Assert.True((await fixture.Home.CompleteExecutionAsync(capability,new(HomePermissionRequestState.Succeeded,
            "CANVAS_SPLIT_COMMITTED","Actual single owning split revision committed.",[new("files.item",fixture.FileId.ToString())]))).Succeeded);
        Assert.Equal(opened.CasRevisionId,committed.FilesRevision.ParentRevisionId);
        Assert.Equal(2,committed.Artifact.SchemaVersion);
        Assert.Equal(2,committed.Artifact.Pages[0].Strokes.Count);
        Assert.Contains(committed.Artifact.Pages[0].Strokes,stroke=>stroke.StrokeId==source.StrokeId);
        Assert.All(committed.Artifact.Pages[0].Strokes,stroke=>Assert.Equal(source.Samples,stroke.Samples));
        Assert.All(committed.Artifact.Pages[0].Strokes,stroke=>Assert.NotNull(stroke.PathGeometry));
        Assert.Contains(committed.Artifact.Pages[0].Strokes,stroke=>intent.Preview.FragmentIdentities.Values.Contains(stroke.StrokeId));
        var bytes = await File.ReadAllBytesAsync(fixture.StatePath);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>owner.ExecuteAsync(intent,capability));
        Assert.Equal(bytes,await File.ReadAllBytesAsync(fixture.StatePath));
        var fresh = await fixture.Files.OpenAsync(fixture.FileId,opened.StoreId);
        var undo = CanvasHistoryIntent.Capture(fixture.FileId,fresh.CasRevisionId,fresh.Artifact.ArtifactId,
            fresh.Artifact.RevisionId,Guid.NewGuid(),CanvasHistoryKind.Undo,opened.StoreId);
        var undoCapability = await fixture.Approve(undo);
        var restored = await new CanvasHomeHistoryOperation(fixture.Files,fixture.Home,fixture.Actors).ExecuteAsync(undo,undoCapability);
        Assert.True((await fixture.Home.CompleteExecutionAsync(undoCapability,new(HomePermissionRequestState.Succeeded,
            "CANVAS_HISTORY_COMMITTED","Actual restored source revision committed.",[new("files.item",fixture.FileId.ToString())]))).Succeeded);
        Assert.Equal(source.StrokeId,Assert.Single(restored.Artifact.Pages[0].Strokes).StrokeId);
        Assert.Equal(source.Samples,restored.Artifact.Pages[0].Strokes[0].Samples);
        using var reopened = CanvasRnoteDocument.Open(CanvasArtifactCodec.Serialize(restored.Artifact));
        using var native = RnoteCanvasEngine.Open(reopened.ExportRnote()); Assert.Single(native.ReadStrokeKeys());
    }

    [Fact]
    public async Task Foreign_original_store_after_exact_split_approval_denies_without_rewriting_or_publishing()
    {
        await using var fixture = await Fixture.Create();
        await Add(fixture,NativeRnoteSplitMaterializationTests.LongStroke());
        var opened = await fixture.Files.OpenAsync(fixture.FileId);
        using var donor = CanvasRnoteDocument.Open(CanvasArtifactCodec.Serialize(opened.Artifact));
        var intent = CanvasSplitEraseIntent.Capture(fixture.FileId,opened,
            NativeRnoteSplitMaterializationTests.GenuineMiddleGesture(donor.ExportRnote()),1);
        var capability = await fixture.Approve(intent);
        var envelope = System.Text.Json.Nodes.JsonNode.Parse(await File.ReadAllBytesAsync(fixture.StatePath))!.AsObject();
        envelope["state"]!.AsObject()["storeId"] = Guid.NewGuid().ToString();
        var foreign = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(envelope);
        await File.WriteAllBytesAsync(fixture.StatePath,foreign);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>
            new CanvasHomeSplitEraserOperation(fixture.Files,fixture.Home,fixture.Actors).ExecuteAsync(intent,capability));
        Assert.Equal(foreign,await File.ReadAllBytesAsync(fixture.StatePath));
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

        public async Task<HomeResourceExecutionCapability> Approve(CanvasSplitEraseIntent intent)
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
