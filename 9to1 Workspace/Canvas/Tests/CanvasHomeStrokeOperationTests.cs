using Haven.Application;
using HavenOS.Files;
using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;
using Xunit;

namespace HavenOS.Apps.Canvas.Tests;

public sealed class CanvasHomeStrokeOperationTests
{
    [Fact]
    public async Task Home_approved_new_Canvas_keeps_captured_native_identity_and_rejects_replay()
    {
        await using var fixture = await Fixture.Create();
        using var native = CanvasRnoteDocument.Create("Approved blank");
        var snapshot = native.Snapshot;
        var intent = CanvasCreateIntent.Capture(snapshot, await fixture.CreationTarget());
        var capability = await fixture.Approve(intent);
        snapshot.DisplayName = "Changed after approval capture";
        var operation = new CanvasHomeCreateOperation(fixture.Files, fixture.Home, fixture.Actors);
        var committed = await operation.ExecuteAsync(intent, capability);
        Assert.NotEqual(fixture.FileId, committed.FileId);
        Assert.Equal(intent.ArtifactId, committed.Artifact.ArtifactId);
        Assert.Equal("Approved blank", committed.Artifact.DisplayName);
        var reopened = await fixture.Files.OpenAsync(committed.FileId);
        Assert.Equal(committed.FilesRevision.Id, reopened.CasRevisionId);
        Assert.Equal(intent.ArtifactId, reopened.Artifact.ArtifactId);
        using var rendered = CanvasRnoteDocument.Open(CanvasArtifactCodec.Serialize(reopened.Artifact));
        Assert.Contains("<svg", System.Text.Encoding.UTF8.GetString(rendered.Render().Svg));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => operation.ExecuteAsync(intent, capability));
    }

    [Fact]
    public async Task Home_creation_actor_change_after_claim_registers_no_artifact()
    {
        await using var fixture = await Fixture.Create();
        using var native = CanvasRnoteDocument.Create("Actor-bound blank");
        var intent = CanvasCreateIntent.Capture(native.Snapshot, await fixture.CreationTarget());
        var capability = await fixture.Approve(intent);
        var actor = (await fixture.Actors.GetCurrentAsync(default))!;
        var store = new VersionedJsonStateStore<DurableDriveProvider.State>(fixture.StatePath, 1, () => new([], [], []));
        var before = await store.ReadAsync(default);
        fixture.OnWriteAdmission = () => fixture.ActorOverride = actor with { AuthenticationRevision = actor.AuthenticationRevision + ":changed-after-create-claim" };
        var operation = new CanvasHomeCreateOperation(fixture.Files, fixture.Home, fixture.Actors);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => operation.ExecuteAsync(intent, capability));
        var after = await store.ReadAsync(default);
        Assert.Equal(before.Items.Count, after.Items.Count);
        Assert.Equal(before.Artifacts.Count, after.Artifacts.Count);
        Assert.Equal(before.Revisions.Count, after.Revisions.Count);
        Assert.Equal(before.Events.Count, after.Events.Count);
    }

    [Fact]
    public async Task Initial_Canvas_creation_rejects_actor_change_without_registering_an_empty_artifact()
    {
        var ct = CancellationToken.None;
        await using var fixture = await Fixture.Create();
        var actor = (await fixture.Actors.GetCurrentAsync(ct))!;
        await using var lease = new CommitLeaseHold(fixture.StatePath, ct);
        fixture.OnProviderResolution = count => { if (count == 2) lease.Acquire(); };
        var creation = fixture.Files.CreateAsync(CanvasArtifact.Create("Guarded new canvas"), ct);
        await Task.WhenAny(lease.Entered, creation).WaitAsync(TimeSpan.FromSeconds(10), ct);
        Assert.True(lease.Entered.IsCompletedSuccessfully);
        Assert.False(creation.IsCompleted);
        fixture.ActorOverride = actor with { AuthenticationRevision = actor.AuthenticationRevision + ":changed-during-create" };
        await lease.ReleaseAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => creation);
        var after = await lease.Store.ReadAsync(ct);
        Assert.Equal(lease.Snapshot!.Items.Count, after.Items.Count);
        Assert.Equal(lease.Snapshot.Artifacts.Count, after.Artifacts.Count);
        Assert.Equal(lease.Snapshot.Revisions.Count, after.Revisions.Count);
        Assert.Equal(lease.Snapshot.Events.Count, after.Events.Count);
    }

    [Fact]
    public async Task Claimed_actor_refresh_while_final_Files_commit_waits_publishes_no_stroke_revision_or_event()
    {
        var ct = CancellationToken.None;
        await using var fixture = await Fixture.Create();
        var actor = (await fixture.Actors.GetCurrentAsync(ct))!;
        var opened = await fixture.Files.OpenAsync(fixture.FileId, ct);
        var intent = CanvasStrokeWriteIntent.Capture(fixture.FileId, opened.CasRevisionId, opened.Artifact.ArtifactId,
            opened.Artifact.RevisionId, Guid.NewGuid(), [new(10, 20, .3), new(30, 40, .6)]);
        var capability = await fixture.Approve(intent);
        await using var lease = new CommitLeaseHold(fixture.StatePath, ct);
        fixture.OnWriteAdmission = () => fixture.OnProviderResolution = count => { if (count == 2) lease.Acquire(); };
        var owner = new CanvasHomeStrokeOperation(fixture.Files, fixture.Home, fixture.Actors);
        var execution = owner.ExecuteAsync(intent, capability, ct);
        await Task.WhenAny(lease.Entered, execution).WaitAsync(TimeSpan.FromSeconds(10), ct);
        Assert.True(lease.Entered.IsCompletedSuccessfully, "The claimed save must finish all pre-commit checks before changing actors.");
        Assert.False(execution.IsCompleted);
        fixture.ActorOverride = actor with { AuthenticationRevision = actor.AuthenticationRevision + ":changed-during-commit-wait" };
        await lease.ReleaseAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => execution);
        var after = await lease.Store.ReadAsync(ct);
        Assert.Equal(lease.Snapshot!.Items.Count, after.Items.Count);
        Assert.Equal(lease.Snapshot.Revisions.Count, after.Revisions.Count);
        Assert.Equal(lease.Snapshot.Events.Count, after.Events.Count);
        fixture.ActorOverride = null;
        var unchanged = await fixture.Files.OpenAsync(fixture.FileId, ct);
        Assert.Equal(opened.CasRevisionId, unchanged.CasRevisionId);
        Assert.Equal(opened.Artifact.RevisionId, unchanged.Artifact.RevisionId);
        Assert.Empty(unchanged.Artifact.Pages[0].Strokes);
    }

    [Fact]
    public async Task Actual_Home_keyed_edits_commit_only_exact_approved_native_candidates_and_never_replay()
    {
        await using var fixture = await Fixture.Create();
        var opened = await fixture.Files.OpenAsync(fixture.FileId);
        var draw = CanvasStrokeWriteIntent.Capture(fixture.FileId, opened.CasRevisionId, opened.Artifact.ArtifactId,
            opened.Artifact.RevisionId, Guid.NewGuid(), [new(10, 20, .2), new(40, 60, .7)]);
        await new CanvasHomeStrokeOperation(fixture.Files, fixture.Home, fixture.Actors)
            .ExecuteAsync(draw, await fixture.Approve(draw));
        opened = await fixture.Files.OpenAsync(fixture.FileId);
        var stroke = Assert.Single(opened.Artifact.Pages[0].Strokes);
        var owner = new CanvasHomeStrokeEditOperation(fixture.Files, fixture.Home, fixture.Actors);
        var edit = CanvasStrokeEditIntent.Capture(fixture.FileId, opened.CasRevisionId, opened.Artifact.ArtifactId,
            opened.Artifact.RevisionId, Guid.NewGuid(), stroke.StrokeId, CanvasStrokeEditKind.Translate, opened.StoreId, 100, 200);
        var capability = await fixture.Approve(edit);
        var changed = CanvasStrokeEditIntent.Capture(fixture.FileId, opened.CasRevisionId, opened.Artifact.ArtifactId,
            opened.Artifact.RevisionId, edit.OperationId, stroke.StrokeId, CanvasStrokeEditKind.Translate, opened.StoreId, 900, 950);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => owner.ExecuteAsync(changed, capability));
        var untouched = await fixture.Files.OpenAsync(fixture.FileId);
        Assert.Equal(opened.CasRevisionId, untouched.CasRevisionId);
        Assert.Equal(stroke.Samples, Assert.Single(untouched.Artifact.Pages[0].Strokes).Samples);
        var committed = await owner.ExecuteAsync(edit, await fixture.Approve(edit));
        Assert.Equal(opened.CasRevisionId, committed.FilesRevision.ParentRevisionId);
        Assert.Equal(stroke.StrokeId, Assert.Single(committed.Artifact.Pages[0].Strokes).StrokeId);
        Assert.Equal(stroke.Samples.Select(sample => sample with { X = sample.X + 100, Y = sample.Y + 200 }),
            Assert.Single(committed.Artifact.Pages[0].Strokes).Samples);
        await Assert.ThrowsAsync<InvalidOperationException>(() => owner.ExecuteAsync(edit, capability));
        opened = await fixture.Files.OpenAsync(fixture.FileId);
        var deletion = CanvasStrokeEditIntent.Capture(fixture.FileId, opened.CasRevisionId, opened.Artifact.ArtifactId,
            opened.Artifact.RevisionId, Guid.NewGuid(), stroke.StrokeId, CanvasStrokeEditKind.Delete, opened.StoreId);
        var deleted = await owner.ExecuteAsync(deletion, await fixture.Approve(deletion));
        Assert.Empty(deleted.Artifact.Pages[0].Strokes);
        using var reopened = CanvasRnoteDocument.Open(CanvasArtifactCodec.Serialize((await fixture.Files.OpenAsync(fixture.FileId)).Artifact));
        using var native = RnoteCanvasEngine.Open(reopened.ExportRnote());
        Assert.Empty(native.ReadStrokeKeys());
    }

    [Theory]
    [InlineData(CanvasStrokeEditKind.Delete)]
    [InlineData(CanvasStrokeEditKind.Translate)]
    public async Task Keyed_edit_rechecks_changed_actor_after_claim_and_never_publishes_or_replays(CanvasStrokeEditKind kind)
    {
        await using var fixture = await Fixture.Create();
        var opened = await fixture.Files.OpenAsync(fixture.FileId);
        var draw = CanvasStrokeWriteIntent.Capture(fixture.FileId, opened.CasRevisionId, opened.Artifact.ArtifactId,
            opened.Artifact.RevisionId, Guid.NewGuid(), [new(10, 20, .2), new(40, 60, .7)]);
        await new CanvasHomeStrokeOperation(fixture.Files, fixture.Home, fixture.Actors)
            .ExecuteAsync(draw, await fixture.Approve(draw));
        opened = await fixture.Files.OpenAsync(fixture.FileId);
        var stroke = Assert.Single(opened.Artifact.Pages[0].Strokes);
        var edit = CanvasStrokeEditIntent.Capture(fixture.FileId, opened.CasRevisionId, opened.Artifact.ArtifactId,
            opened.Artifact.RevisionId, Guid.NewGuid(), stroke.StrokeId, kind, opened.StoreId,
            kind == CanvasStrokeEditKind.Translate ? 100 : 0, kind == CanvasStrokeEditKind.Translate ? 200 : 0);
        var capability = await fixture.Approve(edit);
        var actor = (await fixture.Actors.GetCurrentAsync(default))!;
        fixture.OnWriteAdmission = () => fixture.ActorOverride = actor with { AuthenticationRevision = "changed-after-edit-claim" };
        var owner = new CanvasHomeStrokeEditOperation(fixture.Files, fixture.Home, fixture.Actors);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => owner.ExecuteAsync(edit, capability));
        fixture.ActorOverride = null;
        var current = await fixture.Files.OpenAsync(fixture.FileId);
        Assert.Equal(opened.CasRevisionId, current.CasRevisionId);
        Assert.Equal(CanvasArtifactCodec.Serialize(opened.Artifact), CanvasArtifactCodec.Serialize(current.Artifact));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => owner.ExecuteAsync(edit, capability));
        Assert.Equal(opened.CasRevisionId, (await fixture.Files.OpenAsync(fixture.FileId)).CasRevisionId);
    }

    private sealed class CommitLeaseHold(string statePath, CancellationToken token) : IAsyncDisposable
    {
        private readonly ManualResetEventSlim _acquired = new(false), _release = new(false);
        private readonly TaskCompletionSource _entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private Task? _holding;
        public VersionedJsonStateStore<DurableDriveProvider.State> Store { get; } = new(statePath, 1, () => new([], [], []));
        public DurableDriveProvider.State? Snapshot { get; private set; }
        public Task Entered => _entered.Task;
        public void Acquire()
        {
            _holding = Task.Run(async () => await Store.UpdateAsync(state =>
            {
                Snapshot = state; _acquired.Set(); _release.Wait(token); return state;
            }, token), token);
            if (!_acquired.Wait(TimeSpan.FromSeconds(10), token)) throw new TimeoutException("The actual Files state lease was not acquired.");
            _entered.TrySetResult();
        }
        public async Task ReleaseAsync() { _release.Set(); if (_holding is not null) await _holding; }
        public async ValueTask DisposeAsync() { await ReleaseAsync(); _acquired.Dispose(); _release.Dispose(); }
    }

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

    [Theory]
    [InlineData(CanvasStrokeEditKind.Delete)]
    [InlineData(CanvasStrokeEditKind.Translate)]
    public async Task Keyed_edit_refuses_replaced_store_with_same_target_identity_without_rewriting_foreign_bytes(CanvasStrokeEditKind kind)
    {
        await using var fixture = await Fixture.Create();
        var opened = await fixture.Files.OpenAsync(fixture.FileId);
        var draw = CanvasStrokeWriteIntent.Capture(fixture.FileId, opened.CasRevisionId, opened.Artifact.ArtifactId,
            opened.Artifact.RevisionId, Guid.NewGuid(), [new(10, 20, .2), new(40, 60, .7)]);
        await new CanvasHomeStrokeOperation(fixture.Files, fixture.Home, fixture.Actors)
            .ExecuteAsync(draw, await fixture.Approve(draw));
        opened = await fixture.Files.OpenAsync(fixture.FileId);
        var stroke = Assert.Single(opened.Artifact.Pages[0].Strokes);
        var intent = CanvasStrokeEditIntent.Capture(fixture.FileId, opened.CasRevisionId, opened.Artifact.ArtifactId,
            opened.Artifact.RevisionId, Guid.NewGuid(), stroke.StrokeId, kind, opened.StoreId,
            kind == CanvasStrokeEditKind.Translate ? 100 : 0, kind == CanvasStrokeEditKind.Translate ? 200 : 0);
        var capability = await fixture.Approve(intent);
        var envelope = System.Text.Json.Nodes.JsonNode.Parse(await File.ReadAllBytesAsync(fixture.StatePath))!.AsObject();
        envelope["state"]!.AsObject()["storeId"] = Guid.NewGuid().ToString();
        var foreignBytes = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(envelope);
        await File.WriteAllBytesAsync(fixture.StatePath, foreignBytes);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            new CanvasHomeStrokeEditOperation(fixture.Files, fixture.Home, fixture.Actors).ExecuteAsync(intent, capability));
        Assert.Equal(foreignBytes, await File.ReadAllBytesAsync(fixture.StatePath));
    }

    private sealed class Fixture : IAsyncDisposable, ICanonicalResourceAccessResolver, IAuthenticatedResourceActorSource
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "canvas-home-owner-" + Guid.NewGuid().ToString("N"));
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
