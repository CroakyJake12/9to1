using Haven.Application;
using HavenOS.Files;
using HavenOS.Files.NativeHost;
using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;
using Xunit;
namespace HavenOS.Apps.Canvas.Tests;
public sealed partial class CanvasHomeNativeInsertionOperationTests
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
    public async Task Exact_preapproval_native_order_is_immutable_and_real_Home_Files_history_restores_it()
    {
        await using var fixture=await Fixture.Create();await Add(fixture,[new(100,100,.5),new(300,100,.5)]);
        var opened=await fixture.Files.OpenAsync(fixture.FileId);var actor=(await fixture.Actors.GetCurrentAsync(default))!;
        var solid=Assert.Single(opened.Artifact.Pages[0].StrokeOrder);
        var samples=new List<RnotePointerSample>{new(100,100,.5),new(300,100,.5)};
        var before=await File.ReadAllBytesAsync(fixture.StatePath);
        using var originalDisplay=await fixture.CaptureDisplay();
        var intent=CanvasNativeInsertionIntent.Capture(originalDisplay,samples,new(CanvasRnoteInkKind.Marker));
        Assert.Equal(new[]{intent.StrokeId,solid},intent.StrokeOrder);Assert.Equal(before,await File.ReadAllBytesAsync(fixture.StatePath));
        samples[0]=new(9999,9999,.1);
        var capability=await fixture.Approve(intent);
        var committed=await new CanvasHomeNativeInsertionOperation(fixture.Files,fixture.Home,fixture.Actors,fixture.ClaimedStore).ExecuteAsync(intent,capability);
        Assert.Equal(intent.StrokeOrder,committed.Artifact.Pages[0].StrokeOrder);Assert.Equal(opened.CasRevisionId,committed.FilesRevision.ParentRevisionId);
        Assert.Equal(100,committed.Artifact.Pages[0].Strokes.Single(s=>s.StrokeId==intent.StrokeId).Samples[0].X);
        Assert.True((await fixture.Home.CompleteExecutionAsync(capability,new(HomePermissionRequestState.Succeeded,"CANVAS_INSERTED",
            "Exact original donor insertion acknowledged.",[new("files.item",fixture.FileId.ToString())]))).Succeeded);
        var current=await fixture.Files.OpenAsync(fixture.FileId,opened.StoreId);
        var undo=CanvasHistoryIntent.Capture(fixture.FileId,current.CasRevisionId,current.Artifact.ArtifactId,current.Artifact.RevisionId,
            Guid.NewGuid(),CanvasHistoryKind.Undo,opened.StoreId);
        var undoCap=await fixture.Approve(undo);var history=new CanvasHomeHistoryOperation(fixture.Files,fixture.Home,fixture.Actors);
        var undone=await history.ExecuteAsync(undo,undoCap);Assert.Equal(solid,Assert.Single(undone.Artifact.Pages[0].StrokeOrder));
        Assert.True((await fixture.Home.CompleteExecutionAsync(undoCap,new(HomePermissionRequestState.Succeeded,"CANVAS_UNDONE","Acknowledged undo.",[]))).Succeeded);
        current=await fixture.Files.OpenAsync(fixture.FileId,opened.StoreId);
        var redo=CanvasHistoryIntent.Capture(fixture.FileId,current.CasRevisionId,current.Artifact.ArtifactId,current.Artifact.RevisionId,
            Guid.NewGuid(),CanvasHistoryKind.Redo,opened.StoreId);
        var redoCap=await fixture.Approve(redo);var restored=await history.ExecuteAsync(redo,redoCap);
        Assert.Equal(intent.StrokeOrder,restored.Artifact.Pages[0].StrokeOrder);
        Assert.True((await fixture.Home.CompleteExecutionAsync(redoCap,new(HomePermissionRequestState.Succeeded,"CANVAS_REDONE","Acknowledged redo.",[]))).Succeeded);
        using var native=CanvasRnoteDocument.Open(CanvasArtifactCodec.Serialize((await fixture.Files.OpenAsync(fixture.FileId,opened.StoreId)).Artifact));
        Assert.NotEmpty(native.PreviewSelection(CanvasSelectionStyle.Single,[new(200,100,.5)],native.Identity.RevisionId));
        var final=await File.ReadAllBytesAsync(fixture.StatePath);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>new CanvasHomeNativeInsertionOperation(fixture.Files,fixture.Home,fixture.Actors,fixture.ClaimedStore).ExecuteAsync(intent,capability));
        Assert.Equal(final,await File.ReadAllBytesAsync(fixture.StatePath));
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Original_native_insertion_denies_actor_or_store_substitution_without_publication(bool foreignStore)
    {
        await using var fixture=await Fixture.Create();var opened=await fixture.Files.OpenAsync(fixture.FileId);var actor=(await fixture.Actors.GetCurrentAsync(default))!;
        using var originalDisplay=await fixture.CaptureDisplay();
        var intent=CanvasNativeInsertionIntent.Capture(originalDisplay,[new(100,100,.5),new(300,100,.5)]);
        var cap=await fixture.Approve(intent);
        if(foreignStore)await fixture.SwitchActualWorkspace();
        else fixture.ActorOverride=actor with{AuthenticationRevision="changed-original-insertion"};
        var before=await File.ReadAllBytesAsync(fixture.StatePath);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>new CanvasHomeNativeInsertionOperation(fixture.Files,fixture.Home,fixture.Actors,fixture.ClaimedStore).ExecuteAsync(intent,cap));
        Assert.Equal(before,await File.ReadAllBytesAsync(fixture.StatePath));
    }
    [Fact]
    public async Task Original_preapproval_configuration_fingerprint_denies_same_UUID_record_revision_change()
    {
        await using var fixture=await Fixture.Create();using var display=await fixture.CaptureDisplay();
        var intent=CanvasNativeInsertionIntent.Capture(display,[new(100,100,.5),new(300,100,.5)]);
        var cap=await fixture.Approve(intent);await fixture.ChangeOriginalConfiguration();
        var before=await File.ReadAllBytesAsync(fixture.StatePath);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>new CanvasHomeNativeInsertionOperation(
            fixture.Files,fixture.Home,fixture.Actors,fixture.ClaimedStore).ExecuteAsync(intent,cap));
        Assert.Equal(before,await File.ReadAllBytesAsync(fixture.StatePath));
    }
    [Fact]
    public async Task Substituted_claimed_port_cannot_adopt_original_private_display_or_consume_its_capability()
    {
        await using var fixture=await Fixture.Create();using var display=await fixture.CaptureDisplay();
        var intent=CanvasNativeInsertionIntent.Capture(display,[new(100,100,.5),new(300,100,.5)]);
        var cap=await fixture.Approve(intent);var before=await File.ReadAllBytesAsync(fixture.StatePath);
        var malformed=fixture.UnboundClaimedStore();var originalHome=await fixture.HomeBytes();
        await Assert.ThrowsAsync<NotSupportedException>(()=>fixture.CaptureUsing(malformed.Store));
        Assert.False(File.Exists(malformed.Path));Assert.Equal(originalHome,await fixture.HomeBytes());
        await Assert.ThrowsAsync<NotSupportedException>(()=>new CanvasHomeNativeInsertionOperation(
            fixture.Files,fixture.Home,fixture.Actors,fixture.SubstitutedClaimedStore()).ExecuteAsync(intent,cap));
        Assert.Equal(before,await File.ReadAllBytesAsync(fixture.StatePath));
        var committed=await new CanvasHomeNativeInsertionOperation(fixture.Files,fixture.Home,fixture.Actors,fixture.ClaimedStore).ExecuteAsync(intent,cap);
        Assert.NotEqual(intent.FilesRevision,committed.FilesRevision.Id);
        Assert.True((await fixture.Home.CompleteExecutionAsync(cap,new(HomePermissionRequestState.Succeeded,"CANVAS_INSERTED",
            "Original exact owning insertion acknowledged after substituted port refusal.",[new("files.item",fixture.FileId.ToString())]))).Succeeded);
    }
    private static void AssertStroke(byte[] expected,byte[] actual,bool same)
    {
        static System.Text.Json.JsonElement Stroke(byte[] bytes)
        {
            using var input=new MemoryStream(bytes);using var gzip=new System.IO.Compression.GZipStream(input,System.IO.Compression.CompressionMode.Decompress);
            using var json=System.Text.Json.JsonDocument.Parse(gzip);
            return Assert.Single(json.RootElement.GetProperty("data").GetProperty("engine_snapshot").GetProperty("stroke_components").EnumerateArray()
                .Select(slot=>slot.GetProperty("value")).Where(value=>value.ValueKind!=System.Text.Json.JsonValueKind.Null)).Clone();
        }
        Assert.Equal(same,System.Text.Json.JsonElement.DeepEquals(Stroke(expected),Stroke(actual)));
        using var before=RnoteCanvasEngine.Open(expected);using var after=RnoteCanvasEngine.Open(actual);
        Assert.Equal(before.ReadStrokeKeys().ToArray(),after.ReadStrokeKeys().ToArray());
    }
    private sealed partial class Fixture : IAsyncDisposable, ICanonicalResourceAccessResolver, IAuthenticatedResourceActorSource
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "canvas-history-owner-" + Guid.NewGuid().ToString("N"));
        private DurableDriveProvider _provider = null!;
        private HomeLocalProfileIdentity _actors = null!;
        public IAuthenticatedResourceActorSource Actors => this;
        public AuthenticatedResourceActor? ActorOverride { get; set; }
        public int ResourceProviderReads { get; private set; } // Historical legacy-only observation; not an original-owner read witness.
        private FilesArtifactResourceResolver _originalReadOwner=null!;
        public Action<int>? OnActorObservation { get; set; }
        private int _actorObservations;
        public Action? OnWriteAdmission { get; set; }
        public Action<int>? OnProviderResolution { get; set; }
        private int _providerResolutions;
        private string _statePath=null!;
        public string StatePath => _statePath;
        // Narrow test-fixture accessors; the containing Fixture type remains private.
        public string RootDirectory=>_root;
        public NativeFilesWorkspace OriginalWorkspace=>_workspace;
        public DurableDriveProvider ActualProvider=>_provider;
        public FileHomeCoreStateStore HomeStore=>_homeStore;
        public HomeLocalProfileIdentity ActualProfiles=>_actors;
        public HomeResourceStoreOwnershipAuthority OwnershipAuthority=>_ownershipAuthority;
        public NativeFilesWorkspaceAuthority NativeAuthority=>_authority;
        private FileHomeCoreStateStore _homeStore=null!;
        private NativeFilesWorkspaceService _nativeFiles=null!;
        private NativeFilesWorkspaceAuthority _authority=null!;
        private HomeLocalStoreOwnership _ownership=null!;
        private HomeResourceStoreOwnershipAuthority _ownershipAuthority=null!;
        private NativeFilesWorkspace _workspace=null!;
        public CanvasHomeClaimedNativeInsertionStore ClaimedStore {get;private set;}=null!;
        public Task<CanvasHomeOriginalInsertionDisplay> CaptureDisplay()=>ClaimedStore.CaptureDisplayAsync(
            _authority,FileId,_workspace.Configuration.StoreId,_workspace.Actor);
        public Task<byte[]> HomeBytes()=>File.ReadAllBytesAsync(Path.Combine(_root,"home.json"));
        public (CanvasHomeClaimedNativeInsertionStore Store,string Path) UnboundClaimedStore()
        {
            var path=Path.Combine(_root,"foreign-home.json");
            return (new(Files,Home,new FileHomeCoreStateStore(path),_actors,_ownershipAuthority,()=>WritesAllowed),path);
        }
        public Task<CanvasHomeOriginalInsertionDisplay> CaptureUsing(CanvasHomeClaimedNativeInsertionStore store)=>
            store.CaptureDisplayAsync(_authority,FileId,_workspace.Configuration.StoreId,_workspace.Actor);
        public CanvasHomeClaimedNativeInsertionStore SubstitutedClaimedStore()=>new(Files,Home,_homeStore,_actors,
            _ownershipAuthority,()=>WritesAllowed);
        public async Task SwitchActualWorkspace()
        {
            var chosen=Path.Combine(_root,"replacement-empty");Directory.CreateDirectory(chosen);
            var replacement=await _nativeFiles.ConfigureNewAsync(chosen,_ownership);
            _provider=replacement.Provider;
        }
        public async Task ChangeOriginalConfiguration()
        {
            var state=(await _homeStore.ReadAsync()).State!;
            var record=Assert.Single(state.Records,item=>item.RecordType=="files.native-workspace");
            Assert.True((await _homeStore.WriteAsync(record with {Revision=record.Revision+1},record.Revision)).IsSuccess);
        }
        private DurableDriveProvider ResolveProvider()
        {
            if (OnProviderResolution is { } callback) callback(Interlocked.Increment(ref _providerResolutions));
            return _provider;
        }
        public ValueTask<AuthenticatedResourceActor?> GetCurrentAsync(CancellationToken cancellationToken)
        {
            OnActorObservation?.Invoke(Interlocked.Increment(ref _actorObservations));
            return ActorOverride is { } actor ? ValueTask.FromResult<AuthenticatedResourceActor?>(actor) : _actors.GetCurrentAsync(cancellationToken);
        }
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
            var store = new FileHomeCoreStateStore(Path.Combine(fixture._root, "home.json"));fixture._homeStore=store;
            fixture._actors = new(store, new OperatingSystemPrincipalSource());
            var actor = await fixture.Actors.GetCurrentAsync(default) ?? throw new InvalidOperationException("Actual OS profile unavailable.");
            fixture._nativeFiles=new(store,fixture._actors);
            fixture._ownership=new(store,fixture._actors,new HomeLocalStoreEvidenceRegistry([fixture._nativeFiles]),
                new HomePermissionTrustService(store,(_,_)=>null));
            fixture._ownershipAuthority=new(fixture._ownership,fixture._actors);
            fixture._authority=new(fixture._nativeFiles,fixture._actors,fixture._ownershipAuthority);
            var chosen=Path.Combine(fixture._root,"chosen-empty");Directory.CreateDirectory(chosen);
            fixture._workspace=await fixture._nativeFiles.ConfigureNewAsync(chosen,fixture._ownership);
            fixture._provider=fixture._workspace.Provider;
            fixture._statePath=Path.Combine(chosen,".9to1-files","drive.json");
            var directories=fixture._workspace.Directories;
            fixture._folderId=fixture._workspace.Configuration.AppFolders["canvas"];
            fixture._originalReadOwner=new FilesArtifactResourceResolver(fixture._authority);
            fixture.Resources = new(fixture._actors, [fixture._originalReadOwner]);
            fixture.Files = new(fixture.Actors, current => current.ActorId == actor.ActorId && current.ProfileId == actor.ProfileId ? fixture.ResolveProvider() : null,
                directories, fixture.Resources, () => {
                    var admission = fixture.OnWriteAdmission; fixture.OnWriteAdmission = null; admission?.Invoke();
                    return fixture.WritesAllowed;
                });
            fixture.Permissions = new(store, new CanvasNativeActionPolicies().TryGet);
            fixture.Home = new(fixture.Resources, fixture.Permissions);
            fixture.ClaimedStore=new CanvasHomeClaimedNativeInsertionStore(fixture.Files,fixture.Home,store,fixture._actors,
                fixture._ownershipAuthority,()=>fixture.WritesAllowed,fixture._originalReadOwner);
            using var document = CanvasRnoteDocument.Create();
            fixture.FileId = (await fixture.Files.CreateAsync(document.Snapshot)).FileId;
            return fixture;
        }

        public async Task<HomeResourceExecutionCapability> Approve(CanvasNativeInsertionIntent intent)
        {
            var pending=await intent.OriginalClaimedStore.AuthorizeAsync(intent,"Insert exact donor-prepared stroke and native order",null,"eraser-owner-test-session");
            Assert.Equal(HomePermissionRequestState.PendingApproval,pending.State);
            Assert.True((await Permissions.DecideAsync(pending.RequestId,HomeApprovalChoice.Accept)).Succeeded);
            return Assert.IsType<HomeResourceExecutionCapability>(await intent.OriginalClaimedStore.BeginExecutionAsync(intent,pending.RequestId));
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
