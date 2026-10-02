using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Avalonia.Headless.XUnit;
using Haven.Application;
using Haven.Core.Media;
using HavenOS.Files;
using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;
using Xunit;

namespace HavenOS.Images.Tests;

public sealed class PictureHomeHistoryOperationTests
{
    [AvaloniaFact]
    public async Task Vertical_flip_genuine_Glycin_pixels_fresh_history_and_source_identity_remain_canonical()
    {
        await using var f=await Fixture.Create(coloredSource:true);var ct=TestContext.Current.CancellationToken;
        var initial=await f.Files.OpenAsync(f.FileId,ct);
        async Task<HomeProductivityRasterFrame> Raster(PictureFilesOpenResult opened)
        {
            var reference=new PictureSharedImageReference(f.FileId.Value,opened.Artifact.Document.DocumentId,opened.Artifact.Document.Revision,opened.Artifact.SourceAsset!);
            using var prepared=await f.Projector.PrepareAsync(reference,opened.CasRevisionId,ct);
            var handler=new PictureSharedImageObjectHandler(prepared);var shared=new HomeProductivityEngine(handlers:[handler]);
            return Assert.Single(shared.RenderObject(shared.CreateObject("media.image",reference.DocumentId,handler.ReferenceContent)).RasterBindings).Frame;
        }
        var before=await Raster(initial);var originalPixels=before.CopyPixels();
        Assert.NotEqual(originalPixels.AsSpan(0,before.Stride).ToArray(),originalPixels.AsSpan((before.Height-1)*before.Stride,before.Stride).ToArray());var unchanged=await File.ReadAllBytesAsync(Path.Combine(f.Root,"drive.json"),ct);
        var intent=PictureEditIntent.Capture(initial,new FlipOperation(false));var cap=await f.ApproveEdit(intent);
        Assert.Equal(unchanged,await File.ReadAllBytesAsync(Path.Combine(f.Root,"drive.json"),ct));
        var committed=await f.Edits.ExecuteAsync(intent,cap,ct);var after=await Raster(committed);var actual=after.CopyPixels();
        Assert.Equal(before.Width,after.Width);Assert.Equal(before.Height,after.Height);Assert.Equal(before.Stride,after.Stride);
        for(var y=0;y<before.Height;y++)Assert.Equal(originalPixels.AsSpan((before.Height-1-y)*before.Stride,before.Width*4).ToArray(),actual.AsSpan(y*after.Stride,after.Width*4).ToArray());
        Assert.Equal(initial.Artifact.SourceAsset,committed.Artifact.SourceAsset);Assert.IsType<FlipOperation>(committed.Artifact.Document.Operations.Last());
        var fresh=await f.Files.OpenAsync(f.FileId,initial.StoreId,ct);var undo=PictureHistoryIntent.Capture(fresh,true);
        var restored=await f.History.ExecuteAsync(undo,await f.ApproveHistory(undo),ct);Assert.Equal(originalPixels,(await Raster(restored)).CopyPixels());
        var current=await f.Files.OpenAsync(f.FileId,initial.StoreId,ct);var redo=PictureHistoryIntent.Capture(current,false);
        var redone=await f.History.ExecuteAsync(redo,await f.ApproveHistory(redo),ct);Assert.Equal(actual,(await Raster(redone)).CopyPixels());
    }
    [AvaloniaFact]
    public async Task Vertical_flip_stale_backing_after_Home_review_denies_without_rewriting_changed_store()
    {
        await using var f=await Fixture.Create();var ct=TestContext.Current.CancellationToken;
        var original=await f.Files.OpenAsync(f.FileId,ct);var intent=PictureEditIntent.Capture(original,new FlipOperation(false));var cap=await f.ApproveEdit(intent);
        var actor=(await f.GetCurrentAsync(ct))!;var now=DateTimeOffset.UtcNow;
        Assert.True((await f.Provider.MutateAsync(new(new(Guid.NewGuid()),actor.ActorId,f.FileId,f.FolderId,null,"Rename",original.CasRevisionId,null,
            FilesOperationState.Pending,now,now,null,null),"changed-before-flip.9to1p",ct)).IsSuccess);
        var bytes=await File.ReadAllBytesAsync(Path.Combine(f.Root,"drive.json"),ct);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>f.Edits.ExecuteAsync(intent,cap,ct));
        Assert.Equal(bytes,await File.ReadAllBytesAsync(Path.Combine(f.Root,"drive.json"),ct));
        Assert.Equal(original.Artifact.Document.Operations,(await f.Files.OpenAsync(f.FileId,original.StoreId,ct)).Artifact.Document.Operations);
    }

    [AvaloniaFact]
    public async Task Committed_edit_fresh_open_Undo_fresh_open_Redo_retains_exact_primary_source()
    {
        await using var fixture = await Fixture.Create();
        var ct = TestContext.Current.CancellationToken;
        var initial = await fixture.Files.OpenAsync(fixture.FileId, ct);
        var edit = PictureEditIntent.Capture(initial, new RotateOperation(1));
        var edited = await fixture.Edits.ExecuteAsync(edit, await fixture.ApproveEdit(edit), ct);
        var reopened = await fixture.Files.OpenAsync(fixture.FileId, ct);
        Assert.NotNull(reopened.Artifact.SemanticHistory);
        var undo = PictureHistoryIntent.Capture(reopened, true);
        var capability = await fixture.ApproveHistory(undo);
        var restored = await fixture.History.ExecuteAsync(undo, capability, ct);
        Assert.Equal(initial.Artifact.Document.Operations, restored.Artifact.Document.Operations);
        Assert.Equal(initial.Artifact.SourceAsset, restored.Artifact.SourceAsset);
        Assert.Equal(edited.Artifact.Document.Revision + 1, restored.Artifact.Document.Revision);
        Assert.Equal(reopened.CasRevisionId, restored.Revision.ParentRevisionId);
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.History.ExecuteAsync(undo, capability, ct));
        var fresh = await fixture.Files.OpenAsync(fixture.FileId, ct);
        var redo = PictureHistoryIntent.Capture(fresh, false);
        var redone = await fixture.History.ExecuteAsync(redo, await fixture.ApproveHistory(redo), ct);
        Assert.Equal(edited.Artifact.Document.Operations, redone.Artifact.Document.Operations);
        Assert.Equal(initial.Artifact.SourceAsset, redone.Artifact.SourceAsset);
        Assert.Equal(fresh.StoreId, redone.StoreId);
        // Actual native decoder remains mandatory-bwrap; no fake raster acceptance.
        var reference = new PictureSharedImageReference(fixture.FileId.Value, redone.Artifact.Document.DocumentId,
            redone.Artifact.Document.Revision, redone.Artifact.SourceAsset!);
        using var prepared = await fixture.Projector.PrepareAsync(reference, redone.CasRevisionId, ct);
        var handler = new PictureSharedImageObjectHandler(prepared);
        var engine = new HomeProductivityEngine(handlers: [handler]);
        var value = engine.CreateObject("media.image", reference.DocumentId, handler.ReferenceContent);
        var raster = Assert.Single(engine.RenderObject(value).RasterBindings);
        Assert.Equal(reference.DocumentId, raster.ObjectId);
        Assert.NotEmpty(raster.Frame.CopyPixels());
    }

    [AvaloniaFact]
    public async Task Revoked_primary_source_read_denies_Undo_without_canonical_publication()
    {
        await using var fixture = await Fixture.Create();
        var ct = TestContext.Current.CancellationToken;
        var initial = await fixture.Files.OpenAsync(fixture.FileId, ct);
        var edit = PictureEditIntent.Capture(initial, new FlipOperation(true));
        await fixture.Edits.ExecuteAsync(edit, await fixture.ApproveEdit(edit), ct);
        var current = await fixture.Files.OpenAsync(fixture.FileId, ct);
        var undo = PictureHistoryIntent.Capture(current, true);
        var capability = await fixture.ApproveHistory(undo);
        var before = await File.ReadAllBytesAsync(Path.Combine(fixture.Root, "drive.json"), ct);
        fixture.DenyRaw = true;
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => fixture.History.ExecuteAsync(undo, capability, ct));
        Assert.Equal(before, await File.ReadAllBytesAsync(Path.Combine(fixture.Root, "drive.json"), ct));
        fixture.DenyRaw = false;
        Assert.Equal(current.CasRevisionId, (await fixture.Files.OpenAsync(fixture.FileId, ct)).CasRevisionId);
    }
    [AvaloniaFact]
    public async Task Substituted_actual_store_with_identical_item_ids_denies_captured_Undo()
    {
        await using var fixture = await Fixture.Create();
        var ct = TestContext.Current.CancellationToken;
        var initial = await fixture.Files.OpenAsync(fixture.FileId, ct);
        var edit = PictureEditIntent.Capture(initial, new FlipOperation(true));
        await fixture.Edits.ExecuteAsync(edit, await fixture.ApproveEdit(edit), ct);
        var opened = await fixture.Files.OpenAsync(fixture.FileId, ct);
        var undo = PictureHistoryIntent.Capture(opened, true);
        var capability = await fixture.ApproveHistory(undo);
        var path = Path.Combine(fixture.Root, "drive.json");
        var original = await File.ReadAllBytesAsync(path, ct);
        var envelope = System.Text.Json.Nodes.JsonNode.Parse(original)!.AsObject();
        var state = envelope["state"]!.AsObject();
        var key = state.Select(entry => entry.Key).Single(name => string.Equals(name, "StoreId", StringComparison.OrdinalIgnoreCase));
        state[key] = Guid.NewGuid().ToString("D");
        await File.WriteAllTextAsync(path, envelope.ToJsonString(), ct);
        var replacement = await File.ReadAllBytesAsync(path, ct);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => fixture.History.ExecuteAsync(undo, capability, ct));
        Assert.Equal(replacement, await File.ReadAllBytesAsync(path, ct));
        await File.WriteAllBytesAsync(path, original, ct);
    }

    [AvaloniaTheory]
    [InlineData("Rename")]
    [InlineData("Delete")]
    public async Task Actual_raw_source_change_after_final_read_admission_denies_Undo_publication(string mutation)
    {
        await using var fixture=await Fixture.Create(); var ct=TestContext.Current.CancellationToken;
        var initial=await fixture.Files.OpenAsync(fixture.FileId,ct);
        var edit=PictureEditIntent.Capture(initial,new RotateOperation(1));
        await fixture.Edits.ExecuteAsync(edit,await fixture.ApproveEdit(edit),ct);
        var current=await fixture.Files.OpenAsync(fixture.FileId,initial.StoreId,ct);
        var undo=PictureHistoryIntent.Capture(current,true); var capability=await fixture.ApproveHistory(undo);
        byte[]? afterSourceChange=null;
        fixture.OnFinalCommitAdmission=async () =>
        {
            var actor=(await fixture.GetCurrentAsync(ct))!;
            var other=new DurableDriveProvider(Path.Combine(fixture.Root,"drive.json"),fixture.Provider.Location,actor.ActorId);
            var rawId=new HostedItemId(current.Artifact.SourceAsset!.FileId);
            var metadata=(await other.GetAsync(rawId,ct)).Value!; var now=DateTimeOffset.UtcNow;
            Assert.True((await other.MutateAsync(new(new(Guid.NewGuid()),actor.ActorId,rawId,metadata.ParentId,mutation=="Delete"?null:metadata.ParentId,
                mutation,metadata.CurrentRevisionId,null,FilesOperationState.Pending,now,now,null,null),
                mutation=="Rename"?"renamed-primary.gif":null,ct)).IsSuccess);
            afterSourceChange=await File.ReadAllBytesAsync(Path.Combine(fixture.Root,"drive.json"),ct);
        };
        await Assert.ThrowsAsync<InvalidOperationException>(()=>fixture.History.ExecuteAsync(undo,capability,ct));
        Assert.NotNull(afterSourceChange);
        Assert.Equal(afterSourceChange,await File.ReadAllBytesAsync(Path.Combine(fixture.Root,"drive.json"),ct));
        var unchanged=await fixture.Files.OpenAsync(fixture.FileId,undo.ExpectedStoreId,ct);
        Assert.Equal(current.CasRevisionId,unchanged.CasRevisionId);
        Assert.Equal(current.Artifact.SourceAsset,unchanged.Artifact.SourceAsset);
        Assert.Equal(PictureArtifactCodec.Serialize(current.Artifact),PictureArtifactCodec.Serialize(unchanged.Artifact));
    }

    [AvaloniaFact]
    public async Task Captured_create_denies_actual_replaced_store_before_materializing_candidate()
    {
        await using var fixture = await Fixture.Create();
        var ct = TestContext.Current.CancellationToken;
        var original = await fixture.Files.OpenAsync(fixture.FileId, ct);
        var actor = (await fixture.GetCurrentAsync(ct))!;
        var path = Path.Combine(fixture.Root, "drive.json");
        var bytes = await File.ReadAllBytesAsync(path, ct);
        var envelope = System.Text.Json.Nodes.JsonNode.Parse(bytes)!.AsObject();
        var state = envelope["state"]!.AsObject();
        var key = state.Select(entry => entry.Key).Single(name => string.Equals(name, "StoreId", StringComparison.OrdinalIgnoreCase));
        state[key] = Guid.NewGuid().ToString("D");
        await File.WriteAllTextAsync(path, envelope.ToJsonString(), ct);
        var foreignBytes = await File.ReadAllBytesAsync(path, ct);
        var artifacts = Directory.GetFiles(Path.Combine(fixture.Root, ".9to1-artifacts"), "*", SearchOption.AllDirectories).Order().ToArray();
        var document = PictureDocument.Create(2, 1, original.Artifact.Document.FileId, original.Artifact.Document.SourceRevision);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => fixture.Files.CreateAsync(document,
            original.Artifact.SourceAsset, original.StoreId, actor, ct));
        Assert.Equal(foreignBytes, await File.ReadAllBytesAsync(path, ct));
        Assert.Equal(artifacts, Directory.GetFiles(Path.Combine(fixture.Root, ".9to1-artifacts"), "*", SearchOption.AllDirectories).Order().ToArray());
        await File.WriteAllBytesAsync(path, bytes, ct);
        var committed = await fixture.Files.CreateAsync(document, original.Artifact.SourceAsset, original.StoreId, actor, ct);
        Assert.Equal(original.StoreId, committed.StoreId);
        Assert.Equal(committed.Artifact.BackingFileId, (await fixture.Files.OpenAsync(new(committed.Artifact.BackingFileId), original.StoreId, ct)).Artifact.BackingFileId);
    }

    [AvaloniaFact]
    public async Task Captured_create_final_UUID_fence_preserves_actual_foreign_store_bytes_and_original_artifact()
    {
        await using var fixture = await Fixture.Create();
        var ct = TestContext.Current.CancellationToken;
        var original = await fixture.Files.OpenAsync(fixture.FileId, ct);
        var actor = (await fixture.GetCurrentAsync(ct))!;
        var path = Path.Combine(fixture.Root, "drive.json");
        var bytes = await File.ReadAllBytesAsync(path, ct);
        byte[]? foreignBytes = null;
        fixture.OnFinalCommitAdmission = async () =>
        {
            var envelope = System.Text.Json.Nodes.JsonNode.Parse(await File.ReadAllBytesAsync(path, ct))!.AsObject();
            var state = envelope["state"]!.AsObject();
            var key = state.Select(entry => entry.Key).Single(name => string.Equals(name, "StoreId", StringComparison.OrdinalIgnoreCase));
            state[key] = Guid.NewGuid().ToString("D");
            await File.WriteAllTextAsync(path, envelope.ToJsonString(), ct);
            foreignBytes = await File.ReadAllBytesAsync(path, ct);
        };
        var document = PictureDocument.Create(2, 1, original.Artifact.Document.FileId, original.Artifact.Document.SourceRevision);
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Files.CreateAsync(document,
            original.Artifact.SourceAsset, original.StoreId, actor, ct));
        Assert.NotNull(foreignBytes);
        Assert.Equal(foreignBytes, await File.ReadAllBytesAsync(path, ct));
        await File.WriteAllBytesAsync(path, bytes, ct);
        var retained = await fixture.Files.OpenAsync(fixture.FileId, original.StoreId, ct);
        Assert.Equal(original.CasRevisionId, retained.CasRevisionId);
        Assert.Equal(PictureArtifactBytes(original.Artifact), PictureArtifactBytes(retained.Artifact));
    }

    private static byte[] PictureArtifactBytes(PictureArtifactEnvelope artifact) => PictureArtifactCodec.Serialize(artifact);

    [AvaloniaFact]
    public async Task SaveCopy_requires_exact_Home_then_new_File_and_Document_ids_preserve_editable_source_without_flattening_or_replay()
    {
        await using var fixture = await Fixture.Create();
        var ct = TestContext.Current.CancellationToken;
        var original = await fixture.Files.OpenAsync(fixture.FileId, ct);
        var actor = (await fixture.GetCurrentAsync(ct))!;
        var drivePath = Path.Combine(fixture.Root, "drive.json");
        var driveBytes = await File.ReadAllBytesAsync(drivePath, ct);
        var originalBytes = PictureArtifactCodec.Serialize(original.Artifact);
        var intent = await fixture.Copy.PrepareAsync(original, actor, "Editable variant", ct);
        Assert.Equal(original.StoreId, intent.StoreId);
        Assert.Equal(actor, intent.OriginalActor);
        Assert.NotEqual(original.Artifact.BackingFileId, intent.Capture.FileId.Value);
        Assert.NotEqual(original.Artifact.Document.DocumentId, intent.NewDocumentId);
        Assert.Equal(driveBytes, await File.ReadAllBytesAsync(drivePath, ct));
        var cap = await fixture.ApproveCopy(intent);
        var copied = await fixture.Copy.ExecuteAsync(intent, cap, ct);
        Assert.Equal(intent.Capture.FileId.Value, copied.Artifact.BackingFileId);
        Assert.Equal(intent.NewDocumentId, copied.Artifact.Document.DocumentId);
        Assert.Equal(original.StoreId, copied.StoreId);
        Assert.Equal(original.Artifact.SourceAsset, copied.Artifact.SourceAsset);
        Assert.Equal(original.Artifact.Document.Operations, copied.Artifact.Document.Operations);
        Assert.Equal(original.Artifact.Document.CanvasWidth, copied.Artifact.Document.CanvasWidth);
        Assert.Equal(original.Artifact.Document.CanvasHeight, copied.Artifact.Document.CanvasHeight);
        Assert.Equal(originalBytes, PictureArtifactCodec.Serialize((await fixture.Files.OpenAsync(fixture.FileId, original.StoreId, ct)).Artifact));
        var fresh = await fixture.Files.OpenAsync(intent.Capture.FileId, original.StoreId, ct);
        Assert.Equal(PictureArtifactCodec.Serialize(copied.Artifact), PictureArtifactCodec.Serialize(fresh.Artifact));
        Assert.Equal(0, fresh.Artifact.Document.Revision);
        var after = await File.ReadAllBytesAsync(drivePath, ct);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => fixture.Copy.ExecuteAsync(intent, cap, ct));
        Assert.Equal(after, await File.ReadAllBytesAsync(drivePath, ct));
    }

    [AvaloniaTheory]
    [InlineData("Store")]
    [InlineData("Actor")]
    public async Task SaveCopy_refuses_substituted_original_store_or_actor_without_creating_artifact(string change)
    {
        await using var fixture = await Fixture.Create();
        var ct = TestContext.Current.CancellationToken;
        var original = await fixture.Files.OpenAsync(fixture.FileId, ct);
        var actor = (await fixture.GetCurrentAsync(ct))!;
        var intent = await fixture.Copy.PrepareAsync(original, actor, "Rejected variant", ct);
        var cap = await fixture.ApproveCopy(intent);
        var drivePath = Path.Combine(fixture.Root, "drive.json");
        var originalDrive = await File.ReadAllBytesAsync(drivePath, ct);
        if (change == "Store")
        {
            var envelope = System.Text.Json.Nodes.JsonNode.Parse(originalDrive)!.AsObject();
            var state = envelope["state"]!.AsObject();
            var key = state.Select(entry => entry.Key).Single(name => string.Equals(name, "StoreId", StringComparison.OrdinalIgnoreCase));
            state[key] = Guid.NewGuid().ToString("D");
            await File.WriteAllTextAsync(drivePath, envelope.ToJsonString(), ct);
        }
        else fixture.ActorOverride = actor with { AuthenticationRevision = actor.AuthenticationRevision + ":changed-before-copy" };
        var rejectedState = await File.ReadAllBytesAsync(drivePath, ct);
        var artifacts = Directory.GetFiles(Path.Combine(fixture.Root, ".9to1-artifacts"), "*", SearchOption.AllDirectories).Order().ToArray();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => fixture.Copy.ExecuteAsync(intent, cap, ct));
        Assert.Equal(rejectedState, await File.ReadAllBytesAsync(drivePath, ct));
        Assert.Equal(artifacts, Directory.GetFiles(Path.Combine(fixture.Root, ".9to1-artifacts"), "*", SearchOption.AllDirectories).Order().ToArray());
        fixture.ActorOverride = null; await File.WriteAllBytesAsync(drivePath, originalDrive, ct);
    }

    [AvaloniaTheory]
    [InlineData("Rename")]
    [InlineData("Delete")]
    public async Task SaveCopy_final_raw_source_CAS_rejects_actual_change_after_Home_claim(string mutation)
    {
        await using var fixture = await Fixture.Create();
        var ct = TestContext.Current.CancellationToken;
        var original = await fixture.Files.OpenAsync(fixture.FileId, ct);
        var actor = (await fixture.GetCurrentAsync(ct))!;
        var intent = await fixture.Copy.PrepareAsync(original, actor, "Stale raw variant", ct);
        var cap = await fixture.ApproveCopy(intent);
        byte[]? afterMutation = null;
        fixture.OnFinalCommitAdmission = async () =>
        {
            var raw = new HostedItemId(original.Artifact.SourceAsset!.FileId);
            var now = DateTimeOffset.UtcNow;
            var result = await fixture.Provider.MutateAsync(new(new(Guid.NewGuid()), actor.ActorId, raw, null, null,
                mutation, null, null, FilesOperationState.Pending, now, now, null, null), "changed-source.gif", ct);
            Assert.True(result.IsSuccess);
            afterMutation = await File.ReadAllBytesAsync(Path.Combine(fixture.Root, "drive.json"), ct);
        };
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Copy.ExecuteAsync(intent, cap, ct));
        Assert.NotNull(afterMutation);
        Assert.Equal(afterMutation, await File.ReadAllBytesAsync(Path.Combine(fixture.Root, "drive.json"), ct));
        Assert.Equal(original.CasRevisionId, (await fixture.Files.OpenAsync(fixture.FileId, original.StoreId, ct)).CasRevisionId);
        Assert.False((await fixture.Provider.GetArtifactAsync(intent.Capture.FileId, ct)).IsSuccess);
    }

    private sealed class Fixture : IAsyncDisposable, ICanonicalResourceAccessResolver, IAuthenticatedResourceActorSource
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "picture-export-" + Guid.NewGuid().ToString("N"));
        public DurableDriveProvider Provider { get; private set; } = null!;
        public PictureFilesArtifactBridge Files { get; private set; } = null!;
        public PictureHomePngExportOperation Owner { get; private set; } = null!;
        public PictureHomeImportOperation Import { get; private set; } = null!;
        public PictureHomeSaveCopyOperation Copy { get; private set; } = null!;
        public PictureHomeEditOperation Edits { get; private set; } = null!;
        public PictureSharedImageProjector Projector { get; private set; } = null!;
        private HomeResourceOperationBroker _home = null!;
        private HomePermissionTrustService _permissions = null!;
        private HomeLocalProfileIdentity _actors = null!;
        private int _ownerProviderResolutions;
        private int _bridgeProviderResolutions;
        public AuthenticatedResourceActor? ActorOverride { get; set; }
        public Action<int>? OnOwnerProviderResolution { get; set; }
        public Action<int>? OnBridgeProviderResolution { get; set; }
        public Func<Task>? OnFinalCommitAdmission { get; set; }
        public ValueTask<AuthenticatedResourceActor?> GetCurrentAsync(CancellationToken cancellationToken) =>
            ActorOverride is { } actor ? ValueTask.FromResult<AuthenticatedResourceActor?>(actor) : _actors.GetCurrentAsync(cancellationToken);
        private DurableDriveProvider ResolveOwnerProvider()
        {
            if (OnOwnerProviderResolution is { } callback) callback(Interlocked.Increment(ref _ownerProviderResolutions));
            return Provider;
        }
        private DurableDriveProvider ResolveBridgeProvider()
        {
            if (OnBridgeProviderResolution is { } callback) callback(Interlocked.Increment(ref _bridgeProviderResolutions));
            return Provider;
        }
        public HostedItemId FileId { get; private set; }
        public HostedItemId FolderId { get; private set; }
        public bool DenyRaw { get; set; }
        public bool WritesAllowed { get; set; } = true;
        public string ResourceKind => "files.item";
        public static async Task<Fixture> Create(bool coloredSource=false)
        {
            var ct = TestContext.Current.CancellationToken;
            var fixture = new Fixture();
            Directory.CreateDirectory(fixture.Root);
            var store = new FileHomeCoreStateStore(Path.Combine(fixture.Root, "home.json"));
            fixture._actors = new HomeLocalProfileIdentity(store, new OperatingSystemPrincipalSource());
            IAuthenticatedResourceActorSource actors = fixture;
            var actor = (await actors.GetCurrentAsync(ct))!;
            var profile = Guid.Parse(actor.ProfileId);
            fixture.Provider = new(Path.Combine(fixture.Root, "drive.json"), new(Guid.NewGuid()), actor.ActorId);
            var now = DateTimeOffset.UtcNow;
            fixture.FolderId = HostedItemId.New();
            Assert.True((await fixture.Provider.MutateAsync(new(new(Guid.NewGuid()), actor.ActorId, fixture.FolderId, null, null,
                "CreateFolder", null, null, FilesOperationState.Pending, now, now, null, null), "Pictures", ct)).IsSuccess);
            var directories = new FilesWorkspaceDirectoryResolver(Path.Combine(fixture.Root, "bindings.json"), _ => null,
                id => id == profile ? fixture.Provider : null);
            Assert.True((await directories.RegisterProfileAsync(profile, fixture.FolderId, "picture", fixture.Root, ct)).IsSuccess);
            var resources = new ResourceAuthorizationService(actors, [fixture]);
            fixture.Files = new(actors, current => current == actor ? fixture.ResolveBridgeProvider() : null, directories, resources, () => fixture.WritesAllowed,
                async (currentActor, provider, token) =>
                {
                    var admission = fixture.OnFinalCommitAdmission; fixture.OnFinalCommitAdmission = null;
                    if(admission is not null) await admission();
                    return new FilesCommitAuthorityGuard(currentActor.ActorId,async checkToken =>
                        await fixture.GetCurrentAsync(checkToken) == currentActor && fixture.WritesAllowed);
                });
            var bytes = Convert.FromBase64String("R0lGODlhAgABAIEAAP8AAAAAAAAAAAAAACH/C05FVFNDQVBFMi4wAwEAAAAh+QQACAAAACwAAAAAAgABAAAIBQABAAgIACH5BAAMAAAALAAAAAACAAEAgQAA/wAAAAAAAAAAAAgFAAEACAgAOw==");
            if(coloredSource)
            {
                using var bitmap=new WriteableBitmap(new PixelSize(2,2),new Vector(96,96),PixelFormat.Bgra8888,AlphaFormat.Premul);
                using(var pixels=bitmap.Lock())
                {
                    Marshal.Copy(new byte[]{0,0,255,255,0,255,0,255},0,pixels.Address,8);
                    Marshal.Copy(new byte[]{255,0,0,255,255,255,255,255},0,IntPtr.Add(pixels.Address,pixels.RowBytes),8);
                }
                using var encoded=new MemoryStream();bitmap.Save(encoded);bytes=encoded.ToArray();
            }
            var sourceName=coloredSource?"source.png":"source.gif";
            var rawId = HostedItemId.New(); var rawRevision = new FilesRevisionId(Guid.NewGuid());
            var path = Path.Combine(fixture.Root, sourceName);
            await File.WriteAllBytesAsync(path, bytes, ct);
            var hash = Convert.ToHexString(SHA256.HashData(bytes));
            Assert.True((await fixture.Provider.CommitUploadedContentAsync(new(rawId, fixture.FolderId, sourceName, coloredSource?"image/png":"image/gif",
                rawRevision, null, actor.ActorId, now, bytes.Length, hash, sourceName), ct)).IsSuccess);
            var reference = new PictureSourceAssetReference(rawId.Value, rawRevision.Value, hash, bytes.Length, Guid.NewGuid());
            var initialDocument=coloredSource?PictureDocument.Create(2,2,rawId.ToString(),rawRevision.ToString())
                :PictureDocument.Create(2,1,rawId.ToString(),rawRevision.ToString()).Crop(0,0,1,1);
            var opened = await fixture.Files.CreateAsync(initialDocument, reference, ct);
            fixture.FileId = new(opened.Artifact.BackingFileId);
            var renderer = new PictureFilesSourceRenderer((_, _) => Task.FromResult(MediaEngineResult<MediaAssetReadLease>.Success(
                new(new(new(reference.AssetId), rawId.Value, new Uri(path), rawRevision.ToString()), () => ValueTask.CompletedTask))), resources);
            fixture.Projector = new(fixture.Files, renderer, new());
            fixture._permissions = new(store, new PictureNativeActionPolicies().TryGet);
            fixture._home = new(resources, fixture._permissions);
            fixture.Edits = new(fixture.Files, fixture._home, actors);
            fixture.Owner = new(fixture.Files, renderer, new(), new(), fixture._home, actors,
                current => current == actor ? fixture.ResolveOwnerProvider() : null, directories, resources, () => fixture.WritesAllowed);
            fixture.Import = new(fixture._home, actors, current => current == actor ? fixture.ResolveOwnerProvider() : null,
                directories, resources, () => fixture.WritesAllowed);
            fixture.Copy = new(fixture.Files, fixture._home, actors, current => current == actor ? fixture.ResolveOwnerProvider() : null,
                directories, resources, () => fixture.WritesAllowed);
            return fixture;
        }
        public async Task<string> RequestPending(PicturePngExportIntent intent, CancellationToken cancellationToken)
        {
            var pending = await _home.AuthorizeAsync("picture", "picture.file.export", intent.Scopes, intent.Arguments,
                "Create the captured first-frame PNG snapshot", null, "picture-export-ui-test", cancellationToken);
            Assert.Equal(HomePermissionRequestState.PendingApproval, pending.State);
            return "Waiting for Home approval";
        }
        public async Task<HomeResourceExecutionCapability> Approve(PicturePngExportIntent intent)
        {
            var ct = TestContext.Current.CancellationToken;
            var pending = await _home.AuthorizeAsync("picture", "picture.file.export", intent.Scopes, intent.Arguments,
                "Export the approved first frame as a separate PNG without metadata", null, "picture-export-test", ct);
            Assert.Equal(HomePermissionRequestState.PendingApproval, pending.State);
            Assert.True((await _permissions.DecideAsync(pending.RequestId, HomeApprovalChoice.Accept, cancellationToken: ct)).Succeeded);
            return Assert.IsType<HomeResourceExecutionCapability>(await _home.BeginExecutionCapabilityAsync(pending.RequestId, intent.Arguments, ct));
        }
        public PictureHomeHistoryOperation History => new(Files, _home, this);
        public async Task<HomeResourceExecutionCapability> ApproveHistory(PictureHistoryIntent intent)
        {
            var ct = TestContext.Current.CancellationToken;
            var pending = await _home.AuthorizeAsync("picture", PictureHistoryIntent.ActionId, intent.Scopes, intent.Arguments,
                "Restore this exact Picture history revision", null, "picture-history-test", ct);
            Assert.Equal(HomePermissionRequestState.PendingApproval, pending.State);
            Assert.True((await _permissions.DecideAsync(pending.RequestId, HomeApprovalChoice.Accept, cancellationToken: ct)).Succeeded);
            return Assert.IsType<HomeResourceExecutionCapability>(await _home.BeginExecutionCapabilityAsync(pending.RequestId, intent.Arguments, ct));
        }
        public async Task<HomeResourceExecutionCapability> ApproveEdit(PictureEditIntent intent)
        {
            var ct = TestContext.Current.CancellationToken;
            var pending = await _home.AuthorizeAsync("picture", PictureEditIntent.ActionId, intent.Scopes, intent.Arguments,
                "Apply this exact non-destructive Picture edit", null, "picture-edit-test", ct);
            Assert.Equal(HomePermissionRequestState.PendingApproval, pending.State);
            Assert.True((await _permissions.DecideAsync(pending.RequestId, HomeApprovalChoice.Accept, cancellationToken: ct)).Succeeded);
            return Assert.IsType<HomeResourceExecutionCapability>(await _home.BeginExecutionCapabilityAsync(pending.RequestId, intent.Arguments, ct));
        }
        public async Task<HomeResourceExecutionCapability> ApproveCopy(PictureSaveCopyIntent intent)
        {
            var ct = TestContext.Current.CancellationToken;
            var pending = await _home.AuthorizeAsync("picture", PictureSaveCopyIntent.ActionId, intent.Scopes, intent.Arguments,
                "Create this exact editable Picture copy", null, "picture-copy-test", ct);
            Assert.Equal(HomePermissionRequestState.PendingApproval, pending.State);
            Assert.True((await _permissions.DecideAsync(pending.RequestId, HomeApprovalChoice.Accept, cancellationToken: ct)).Succeeded);
            return Assert.IsType<HomeResourceExecutionCapability>(await _home.BeginExecutionCapabilityAsync(pending.RequestId, intent.Arguments, ct));
        }
        public async Task<HomeResourceExecutionCapability> ApproveImport(PictureImportIntent intent)
        {
            var ct = TestContext.Current.CancellationToken;
            var pending = await _home.AuthorizeAsync("picture", PictureImportIntent.ActionId, intent.Scopes, intent.Arguments,
                "Import the selected original image and editable document", null, "picture-import-test", ct);
            Assert.Equal(HomePermissionRequestState.PendingApproval, pending.State);
            Assert.True((await _permissions.DecideAsync(pending.RequestId, HomeApprovalChoice.Accept, cancellationToken: ct)).Succeeded);
            return Assert.IsType<HomeResourceExecutionCapability>(await _home.BeginExecutionCapabilityAsync(pending.RequestId, intent.Arguments, ct));
        }
        public async Task<string> RequestPendingImport(PictureImportIntent intent, CancellationToken ct)
        {
            var pending = await _home.AuthorizeAsync("picture", PictureImportIntent.ActionId, intent.Scopes, intent.Arguments,
                "Import the selected original image and editable document", null, "picture-import-ui-test", ct);
            Assert.Equal(HomePermissionRequestState.PendingApproval, pending.State);
            return "Waiting for Home approval";
        }
        public async ValueTask<ResourceAccessDecision> EvaluateAsync(AuthenticatedResourceActor actor, string actionId, ResourceScope scope, CancellationToken cancellationToken)
        {
            var item = await Provider.GetAsync(new(Guid.Parse(scope.Id)), cancellationToken);
            var allowed = item.IsSuccess && item.Value!.OwnerPrincipalId == actor.ActorId &&
                (item.Value.CurrentRevisionId is { } current ? Guid.TryParse(scope.Revision, out var requested) && current.Value == requested : scope.Revision == "uncommitted") &&
                !(DenyRaw && actionId == "media.asset.read") &&
                (actionId, scope.Access) is ("picture.file.open", ResourceAccess.Read) or ("picture.file.save", ResourceAccess.Write) or
                ("picture.file.copy", ResourceAccess.Read) or ("picture.file.copy", ResourceAccess.Write) or ("picture.file.create", ResourceAccess.Write) or ("picture.file.import", ResourceAccess.Write) or ("picture.file.export", ResourceAccess.Read) or ("picture.file.export", ResourceAccess.Write) or ("media.asset.read", ResourceAccess.Read);
            return new(allowed, "fixture-canonical-files", actor.ActorId, scope.Revision, null);
        }
        public ValueTask DisposeAsync() { Directory.Delete(Root, true); return ValueTask.CompletedTask; }
    }
}
