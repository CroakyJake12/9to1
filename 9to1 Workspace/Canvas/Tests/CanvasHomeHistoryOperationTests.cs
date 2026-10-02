using Haven.Application;
using HavenOS.Files;
using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;
using Xunit;

namespace HavenOS.Apps.Canvas.Tests;

public sealed class CanvasHomeHistoryOperationTests
{
    private static CanvasHistoryIntent Intent(HostedItemId file, CanvasFilesOpenResult source, CanvasHistoryKind kind) =>
        CanvasHistoryIntent.Capture(file, source.CasRevisionId, source.Artifact.ArtifactId, source.Artifact.RevisionId, Guid.NewGuid(), kind, source.StoreId);

    private static async Task<HostedItemId> CreateAndDraw(Fixture fixture)
    {
        using var blank = CanvasRnoteDocument.Create("Durable history Canvas");
        var create = CanvasCreateIntent.Capture(blank.Snapshot, await fixture.CreationTarget());
        var created = await new CanvasHomeCreateOperation(fixture.Files, fixture.Home, fixture.Actors)
            .ExecuteAsync(create, await fixture.Approve(create));
        var source = await fixture.Files.OpenAsync(created.FileId);
        var stroke = CanvasStrokeWriteIntent.Capture(created.FileId, source.CasRevisionId, source.Artifact.ArtifactId,
            source.Artifact.RevisionId, Guid.NewGuid(), [new(20, 30, .2), new(80, 90, .8)]);
        await new CanvasHomeStrokeOperation(fixture.Files, fixture.Home, fixture.Actors).ExecuteAsync(stroke, await fixture.Approve(stroke));
        return created.FileId;
    }

    [Fact]
    public async Task Fresh_owner_reopens_Undo_and_Redo_as_new_guarded_Files_revisions()
    {
        await using var fixture = await Fixture.Create();
        var file = await CreateAndDraw(fixture);
        var drawn = await fixture.Files.OpenAsync(file);
        var stroke = Assert.Single(drawn.Artifact.Pages[0].Strokes);
        var drawnBytes = CanvasArtifactCodec.Serialize(drawn.Artifact);
        var undo = Intent(file, drawn, CanvasHistoryKind.Undo);
        var owner = new CanvasHomeHistoryOperation(fixture.Files, fixture.Home, fixture.Actors);
        var undoCapability = await fixture.Approve(undo);
        var undone = await owner.ExecuteAsync(undo, undoCapability);
        Assert.NotEqual(drawn.CasRevisionId, undone.FilesRevision.Id);
        Assert.NotEqual(drawn.Artifact.RevisionId, undone.Artifact.RevisionId);
        Assert.Equal(drawn.Artifact.ArtifactId, undone.Artifact.ArtifactId);
        var freshUndone = await fixture.Files.OpenAsync(file);
        Assert.Empty(freshUndone.Artifact.Pages[0].Strokes);
        using (var native = CanvasRnoteDocument.Open(CanvasArtifactCodec.Serialize(freshUndone.Artifact)))
        using (var donor = RnoteCanvasEngine.Open(native.ExportRnote())) Assert.Empty(donor.ReadStrokeKeys());
        var redo = Intent(file, freshUndone, CanvasHistoryKind.Redo);
        var redone = await owner.ExecuteAsync(redo, await fixture.Approve(redo));
        var freshRedone = await fixture.Files.OpenAsync(file);
        Assert.NotEqual(freshUndone.CasRevisionId, redone.FilesRevision.Id);
        Assert.Equal(stroke.StrokeId, Assert.Single(freshRedone.Artifact.Pages[0].Strokes).StrokeId);
        Assert.Equal(stroke.Samples, freshRedone.Artifact.Pages[0].Strokes[0].Samples);
        using (var native = CanvasRnoteDocument.Open(CanvasArtifactCodec.Serialize(freshRedone.Artifact)))
        using (var donor = RnoteCanvasEngine.Open(native.ExportRnote())) Assert.Single(donor.ReadStrokeKeys());
        Assert.Equal(drawnBytes, CanvasArtifactCodec.Serialize(drawn.Artifact));
        await Assert.ThrowsAsync<InvalidOperationException>(() => owner.ExecuteAsync(undo, undoCapability));
    }

    [Fact]
    public async Task Changed_history_arguments_reject_claim_without_publishing_a_candidate()
    {
        await using var fixture = await Fixture.Create();
        var file = await CreateAndDraw(fixture);
        var source = await fixture.Files.OpenAsync(file);
        var intent = Intent(file, source, CanvasHistoryKind.Undo);
        var capability = await fixture.Approve(intent);
        var changed = Intent(file, source, CanvasHistoryKind.Undo);
        var owner = new CanvasHomeHistoryOperation(fixture.Files, fixture.Home, fixture.Actors);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => owner.ExecuteAsync(changed, capability));
        var unchanged = await fixture.Files.OpenAsync(file);
        Assert.Equal(source.CasRevisionId, unchanged.CasRevisionId);
        Assert.Equal(CanvasArtifactCodec.Serialize(source.Artifact), CanvasArtifactCodec.Serialize(unchanged.Artifact));
    }

    [Fact]
    public async Task Actor_refresh_after_history_claim_publishes_no_restored_source()
    {
        await using var fixture = await Fixture.Create();
        var file = await CreateAndDraw(fixture);
        var source = await fixture.Files.OpenAsync(file);
        var intent = Intent(file, source, CanvasHistoryKind.Undo);
        var capability = await fixture.Approve(intent);
        var actor = (await fixture.Actors.GetCurrentAsync(default))!;
        fixture.OnWriteAdmission = () => fixture.ActorOverride = actor with { AuthenticationRevision = actor.AuthenticationRevision + ":history-refreshed" };
        var owner = new CanvasHomeHistoryOperation(fixture.Files, fixture.Home, fixture.Actors);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => owner.ExecuteAsync(intent, capability));
        fixture.ActorOverride = null;
        var unchanged = await fixture.Files.OpenAsync(file);
        Assert.Equal(source.CasRevisionId, unchanged.CasRevisionId);
        Assert.Equal(CanvasArtifactCodec.Serialize(source.Artifact), CanvasArtifactCodec.Serialize(unchanged.Artifact));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => owner.ExecuteAsync(intent, capability));
    }

    [Fact]
    public async Task Actual_store_replacement_preserving_all_document_ids_denies_original_Undo()
    {
        await using var fixture = await Fixture.Create();
        var file = await CreateAndDraw(fixture);
        var source = await fixture.Files.OpenAsync(file);
        var intent = Intent(file, source, CanvasHistoryKind.Undo);
        var capability = await fixture.Approve(intent);
        var original = await File.ReadAllBytesAsync(fixture.StatePath);
        var envelope = System.Text.Json.Nodes.JsonNode.Parse(original)!.AsObject();
        var state = envelope["state"]!.AsObject();
        var key = state.Select(entry => entry.Key).Single(name => string.Equals(name, "StoreId", StringComparison.OrdinalIgnoreCase));
        state[key] = Guid.NewGuid().ToString("D");
        await File.WriteAllTextAsync(fixture.StatePath, envelope.ToJsonString());
        var substituted = await File.ReadAllBytesAsync(fixture.StatePath);
        var owner = new CanvasHomeHistoryOperation(fixture.Files, fixture.Home, fixture.Actors);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => owner.ExecuteAsync(intent, capability));
        Assert.Equal(substituted, await File.ReadAllBytesAsync(fixture.StatePath));
        await File.WriteAllBytesAsync(fixture.StatePath, original);
    }

    [Fact]
    public async Task Approved_creation_refuses_substituted_destination_store_without_publication()
    {
        await using var fixture = await Fixture.Create();
        using var blank = CanvasRnoteDocument.Create("Original destination");
        var intent = CanvasCreateIntent.Capture(blank.Snapshot, await fixture.CreationTarget());
        var capability = await fixture.Approve(intent);
        var original = await File.ReadAllBytesAsync(fixture.StatePath);
        var envelope = System.Text.Json.Nodes.JsonNode.Parse(original)!.AsObject();
        var state = envelope["state"]!.AsObject();
        var key = state.Select(entry => entry.Key).Single(name => string.Equals(name, "StoreId", StringComparison.OrdinalIgnoreCase));
        state[key] = Guid.NewGuid().ToString("D");
        await File.WriteAllTextAsync(fixture.StatePath, envelope.ToJsonString());
        var substituted = await File.ReadAllBytesAsync(fixture.StatePath);
        var owner = new CanvasHomeCreateOperation(fixture.Files, fixture.Home, fixture.Actors);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => owner.ExecuteAsync(intent, capability));
        Assert.Equal(substituted, await File.ReadAllBytesAsync(fixture.StatePath));
        await File.WriteAllBytesAsync(fixture.StatePath, original);
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
