using System.Security.Cryptography;
using Avalonia.Headless.XUnit;
using Haven.Application;
using Haven.Core.Media;
using HavenOS.Files;
using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;
using Xunit;

namespace HavenOS.Images.Tests;

public sealed class PictureHomePngExportTests
{
    [AvaloniaFact]
    public async Task Initial_Picture_creation_rejects_actor_change_without_registering_an_empty_artifact()
    {
        await using var fixture = await Fixture.Create();
        var ct = TestContext.Current.CancellationToken;
        var actor = (await fixture.GetCurrentAsync(ct))!;
        await using var lease = new CommitLeaseHold(Path.Combine(fixture.Root, "drive.json"), ct);
        fixture.OnFinalCommitAdmission = lease.Acquire;
        var creation = fixture.Files.CreateAsync(new PictureDocument { CanvasWidth = 2, CanvasHeight = 2, DisplayName = "Guarded new picture" }, cancellationToken: ct);
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

    [AvaloniaFact]
    public async Task Exact_Home_approved_non_destructive_edit_preserves_source_and_rejects_replay_and_stale_target()
    {
        await using var fixture = await Fixture.Create();
        var ct = TestContext.Current.CancellationToken;
        var original = await fixture.Files.OpenAsync(fixture.FileId, ct);
        var intent = PictureEditIntent.Capture(original, new RotateOperation(1));
        var capability = await fixture.ApproveEdit(intent);
        var unchanged = await fixture.Files.OpenAsync(fixture.FileId, ct);
        Assert.Equal(original.CasRevisionId, unchanged.CasRevisionId);
        var committed = await fixture.Edits.ExecuteAsync(intent, capability, ct);
        Assert.Equal(original.Artifact.Document.DocumentId, committed.Artifact.Document.DocumentId);
        Assert.Equal(original.Artifact.BackingFileId, committed.Artifact.BackingFileId);
        Assert.Equal(original.Artifact.SourceAsset, committed.Artifact.SourceAsset);
        Assert.Equal(original.Artifact.Document.Revision + 1, committed.Artifact.Document.Revision);
        Assert.IsType<RotateOperation>(committed.Artifact.Document.Operations.Last());
        Assert.Equal(original.CasRevisionId, committed.Revision.ParentRevisionId);
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Edits.ExecuteAsync(intent, capability, ct));
        var next = PictureEditIntent.Capture(committed, new FlipOperation(true));
        var nextCapability = await fixture.ApproveEdit(next);
        var concurrent = PictureEditIntent.Capture(committed,new ResizeOperation(2,2));
        var concurrentCapability=await fixture.ApproveEdit(concurrent);
        var concurrentCommit=await fixture.Edits.ExecuteAsync(concurrent,concurrentCapability,ct);
        Assert.True((await fixture.Home.CompleteExecutionAsync(concurrentCapability,new(HomePermissionRequestState.Succeeded,
            "PICTURE_RESIZED","Actual competing resize revision acknowledged.",[new("files.item",fixture.FileId.ToString())]),ct)).Succeeded);
        Assert.NotEqual(committed.CasRevisionId,concurrentCommit.CasRevisionId);
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Edits.ExecuteAsync(next, nextCapability, ct));
        var reopened = await fixture.Files.OpenAsync(fixture.FileId, ct);
        Assert.DoesNotContain(reopened.Artifact.Document.Operations, operation => operation is FlipOperation);
        Assert.Equal(original.Artifact.SourceAsset, reopened.Artifact.SourceAsset);
    }

    [AvaloniaFact]
    public async Task Editable_Picture_save_rejects_actor_refresh_while_final_commit_waits_for_the_state_lease()
    {
        await using var fixture = await Fixture.Create();
        var ct = TestContext.Current.CancellationToken;
        var actor = (await fixture.GetCurrentAsync(ct))!;
        var source = await fixture.Files.OpenAsync(fixture.FileId, ct);
        await using var lease = new CommitLeaseHold(Path.Combine(fixture.Root, "drive.json"), ct);
        fixture.OnFinalCommitAdmission = lease.Acquire;
        var execution = fixture.Files.SaveAsync(source.Artifact with { Document = source.Artifact.Document.Resize(2, 2) }, source.CasRevisionId, actor, ct);
        await Task.WhenAny(lease.Entered, execution).WaitAsync(TimeSpan.FromSeconds(10), ct);
        Assert.True(lease.Entered.IsCompletedSuccessfully);
        Assert.False(execution.IsCompleted);
        fixture.ActorOverride = actor with { AuthenticationRevision = actor.AuthenticationRevision + ":changed-during-save-wait" };
        await lease.ReleaseAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => execution);
        var after = await lease.Store.ReadAsync(ct);
        Assert.Equal(lease.Snapshot!.Items.Count, after.Items.Count);
        Assert.Equal(lease.Snapshot.Revisions.Count, after.Revisions.Count);
        Assert.Equal(lease.Snapshot.Events.Count, after.Events.Count);
        fixture.ActorOverride = null;
        var unchanged = await fixture.Files.OpenAsync(fixture.FileId, ct);
        Assert.Equal(source.CasRevisionId, unchanged.CasRevisionId);
        Assert.Equal(source.Artifact.Document.Revision, unchanged.Artifact.Document.Revision);
    }

    [AvaloniaFact]
    public Task Import_rejects_actor_refresh_while_final_Files_commit_waits_for_the_state_lease() =>
        RejectActorRefreshDuringCommit(importing: true);

    [AvaloniaFact]
    public Task Export_rejects_actor_refresh_while_final_Files_commit_waits_for_the_state_lease() =>
        RejectActorRefreshDuringCommit(importing: false);

    private static async Task RejectActorRefreshDuringCommit(bool importing)
    {
        await using var fixture = await Fixture.Create();
        var ct = TestContext.Current.CancellationToken;
        var actor = (await fixture.GetCurrentAsync(ct))!;
        var source = await fixture.Files.OpenAsync(fixture.FileId, ct);
        PictureImportIntent? import = null;
        Func<Task> execute;
        HostedItemId[] newIds;
        if (importing)
        {
            using var picked = await PicturePickedImage.PickAsync(new PicturePickerFixture(
                await File.ReadAllBytesAsync(Path.Combine(fixture.Root, "source.gif"), ct)).Provider, new(), ct);
            import = await fixture.Import.PrepareAsync(picked!, fixture.StoreId, ct);
            var capability = await fixture.ApproveImport(import);
            execute = () => fixture.Import.ExecuteAsync(import, capability, ct);
            newIds = [import.RawFileId, import.BackingFileId];
        }
        else
        {
            var export = await fixture.Owner.PrepareAsync(fixture.FileId, source.CasRevisionId,
                source.Artifact.Document.DocumentId, source.Artifact.Document.Revision, "guarded.png", true, source.StoreId, ct);
            var capability = await fixture.Approve(export);
            execute = () => fixture.Owner.ExecuteAsync(export, capability, ct);
            newIds = [export.OutputFileId];
        }
        await using var lease = new CommitLeaseHold(Path.Combine(fixture.Root, "drive.json"), ct);
        fixture.OnFinalCommitAdmission = lease.Acquire;
        try
        {
            var execution = execute();
            await Task.WhenAny(lease.Entered, execution).WaitAsync(TimeSpan.FromSeconds(10), ct);
            Assert.True(lease.Entered.IsCompletedSuccessfully, "The owner must reach its final validated provider before changing actors.");
            Assert.False(execution.IsCompleted);
            fixture.ActorOverride = actor with { AuthenticationRevision = actor.AuthenticationRevision + ":changed-during-commit-wait" };
            await lease.ReleaseAsync();
            await Assert.ThrowsAsync<InvalidOperationException>(() => execution);
            foreach (var id in newIds) Assert.False((await fixture.Provider.GetAsync(id, ct)).IsSuccess);
            var after = await lease.Store.ReadAsync(ct);
            Assert.Equal(lease.Snapshot!.Items.Count, after.Items.Count);
            Assert.Equal(lease.Snapshot.Revisions.Count, after.Revisions.Count);
            Assert.Equal(lease.Snapshot.Events.Count, after.Events.Count);
            fixture.ActorOverride = null;
            Assert.Equal(source.CasRevisionId, (await fixture.Files.OpenAsync(fixture.FileId, ct)).CasRevisionId);
        }
        finally { import?.Dispose(); }
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
                Snapshot = state; _acquired.Set();
                _release.Wait(token);
                return state;
            }, token), token);
            if (!_acquired.Wait(TimeSpan.FromSeconds(10), token)) throw new TimeoutException("The actual Files state lease was not acquired.");
            _entered.TrySetResult();
        }
        public async Task ReleaseAsync() { _release.Set(); if (_holding is not null) await _holding; }
        public async ValueTask DisposeAsync() { await ReleaseAsync(); _acquired.Dispose(); _release.Dispose(); }
    }

    [AvaloniaFact]
    public async Task Owning_import_CUI_selects_real_bytes_and_requests_pending_Home_approval_without_publication()
    {
        await using var fixture = await Fixture.Create();
        var ct = TestContext.Current.CancellationToken;
        var picker = new PicturePickerFixture(await File.ReadAllBytesAsync(Path.Combine(fixture.Root, "source.gif"), ct));
        PictureImportIntent? requested = null;
        using var form = new PictureImportCuiRequest(fixture.Import, picker.Provider, new(), fixture.StoreId, () => fixture.WritesAllowed,
            async (intent, token) => { requested = intent; return await fixture.RequestPendingImport(intent, token); });
        try
        {
            Assert.Equal("picture-import", Assert.Single(PictureImportCuiRequest.LoadDocument().Components).AuthoredId);
            Assert.False(form.IsActionAvailable("9to1.Picture.Import.RequestApproval"));
            await form.DispatchAsync("9to1.Picture.Import.Pick", null, ct);
            Assert.True(picker.Disposed);
            Assert.True(form.IsActionAvailable("9to1.Picture.Import.RequestApproval"));
            await form.DispatchAsync("9to1.Picture.Import.RequestApproval", null, ct);
            Assert.NotNull(requested);
            Assert.False((await fixture.Provider.GetAsync(requested.RawFileId, ct)).IsSuccess);
            Assert.False((await fixture.Provider.GetAsync(requested.BackingFileId, ct)).IsSuccess);
            Assert.False(form.IsActionAvailable("9to1.Picture.Import.Pick"));
        }
        finally { requested?.Dispose(); }
    }

    [AvaloniaFact]
    public async Task Native_picker_import_preserves_encoded_animation_and_atomically_creates_editable_backing_after_Home_approval()
    {
        await using var fixture = await Fixture.Create();
        var ct = TestContext.Current.CancellationToken;
        var original = await File.ReadAllBytesAsync(Path.Combine(fixture.Root, "source.gif"), ct);
        var picker = new PicturePickerFixture(original, "misleading-extension.png");
        using var picked = await PicturePickedImage.PickAsync(picker.Provider, new(), ct);
        Assert.NotNull(picked); Assert.True(picker.Disposed);
        using var intent = await fixture.Import.PrepareAsync(picked, fixture.StoreId, ct);
        Assert.Equal("image/gif", picked.MimeType);
        Assert.Equal("image/gif", intent.Arguments.GetProperty("mimeType").GetString());
        Array.Clear(original); picked.Dispose(); // Approval owns the exact capture, not a mutable picker stream.
        Assert.False((await fixture.Provider.GetAsync(intent.RawFileId, ct)).IsSuccess);
        Assert.False((await fixture.Provider.GetAsync(intent.BackingFileId, ct)).IsSuccess);
        var capability = await fixture.ApproveImport(intent);
        var imported = await fixture.Import.ExecuteAsync(intent, capability, ct);
        Assert.Equal("image/gif", (await fixture.Provider.GetAsync(intent.RawFileId, ct)).Value!.ContentType);
        Assert.Equal(intent.BackingFileId.Value, imported.Artifact.BackingFileId);
        Assert.Equal(intent.RawFileId.Value, imported.Artifact.SourceAsset!.FileId);
        Assert.NotEqual(imported.Artifact.BackingFileId, imported.Artifact.SourceAsset.FileId);
        Assert.Null(imported.Artifact.Document.SourcePath);
        var opened = await fixture.Files.OpenAsync(intent.BackingFileId, ct);
        Assert.Equal(imported.CasRevisionId, opened.CasRevisionId);
        var content = await fixture.Provider.GetArtifactRevisionContentAsync(intent.RawFileId, intent.RawRevision, ct);
        Assert.True(content.IsSuccess);
        var bytes = await File.ReadAllBytesAsync(Path.Combine(fixture.Root, content.Value!.ProviderContentReference!), ct);
        Assert.Equal(intent.SourceHash, Convert.ToHexString(SHA256.HashData(bytes)));
        using var frames = new PictureGlycinDecoder().OpenFrames(bytes);
        var first = frames.NextFrame(ct); var second = frames.NextFrame(ct);
        Assert.Equal(new byte[] { 0, 0, 255, 255 }, first.BgraPremultipliedPixels[..4]);
        Assert.Equal(new byte[] { 255, 0, 0, 255 }, second.BgraPremultipliedPixels[..4]);
        Array.Clear(first.BgraPremultipliedPixels); Array.Clear(second.BgraPremultipliedPixels);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => fixture.Import.ExecuteAsync(intent, capability, ct));
    }

    [AvaloniaFact]
    public async Task Picker_import_denied_after_folder_revision_change_publishes_neither_identity()
    {
        await using var fixture = await Fixture.Create();
        var ct = TestContext.Current.CancellationToken;
        using var picked = await PicturePickedImage.PickAsync(new PicturePickerFixture(
            await File.ReadAllBytesAsync(Path.Combine(fixture.Root, "source.gif"), ct)).Provider, new(), ct);
        using var intent = await fixture.Import.PrepareAsync(picked!, fixture.StoreId, ct);
        var capability = await fixture.ApproveImport(intent);
        var folder = (await fixture.Provider.GetAsync(fixture.FolderId, ct)).Value!;
        var now = DateTimeOffset.UtcNow;
        Assert.True((await fixture.Provider.MutateAsync(new(new(Guid.NewGuid()), folder.OwnerPrincipalId, fixture.FolderId,
            null, null, "Rename", folder.CurrentRevisionId, null, FilesOperationState.Pending, now, now, null, null), "Changed pictures", ct)).IsSuccess);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => fixture.Import.ExecuteAsync(intent, capability, ct));
        Assert.False((await fixture.Provider.GetAsync(intent.RawFileId, ct)).IsSuccess);
        Assert.False((await fixture.Provider.GetAsync(intent.BackingFileId, ct)).IsSuccess);
    }

    [AvaloniaFact]
    public async Task Approved_native_snapshot_publishes_new_raw_PNG_without_changing_source_and_rejects_replay()
    {
        await using var fixture = await Fixture.Create();
        var source = await fixture.Files.OpenAsync(fixture.FileId, TestContext.Current.CancellationToken);
        var intent = await fixture.Owner.PrepareAsync(fixture.FileId, source.CasRevisionId, source.Artifact.Document.DocumentId,
            source.Artifact.Document.Revision, "snapshot.png", true, source.StoreId, TestContext.Current.CancellationToken);
        var capability = await fixture.Approve(intent);
        var result = await fixture.Owner.ExecuteAsync(intent, capability, TestContext.Current.CancellationToken);
        Assert.NotEqual(fixture.FileId, result.FileId);
        Assert.NotEqual(source.Artifact.SourceAsset!.FileId, result.FileId.Value);
        Assert.Equal("files", result.Revision.OwningAppId);
        var retained = await fixture.Provider.GetArtifactRevisionContentAsync(result.FileId, result.Revision.Id, TestContext.Current.CancellationToken);
        Assert.True(retained.IsSuccess);
        Assert.Equal(fixture.FolderId, retained.Value!.UploadAnchorFolderId);
        var png = await File.ReadAllBytesAsync(Path.Combine(fixture.Root, retained.Value.ProviderContentReference!), TestContext.Current.CancellationToken);
        var decoded = new PictureGlycinDecoder().DecodeFirstFrame(png);
        Assert.Equal(1u, decoded.Width);
        Assert.Equal(new byte[] { 0, 0, 255, 255 }, decoded.BgraPremultipliedPixels[..4]);
        Assert.Equal(source.CasRevisionId, (await fixture.Files.OpenAsync(fixture.FileId, TestContext.Current.CancellationToken)).CasRevisionId);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => fixture.Owner.ExecuteAsync(intent, capability, TestContext.Current.CancellationToken));
        Assert.Equal(result.Revision.Id, (await fixture.Provider.GetAsync(result.FileId, TestContext.Current.CancellationToken)).Value!.CurrentRevisionId);
    }

    [AvaloniaFact]
    public async Task Explicit_flatten_acknowledgement_current_source_and_live_raw_access_are_required()
    {
        await using var fixture = await Fixture.Create();
        var source = await fixture.Files.OpenAsync(fixture.FileId, TestContext.Current.CancellationToken);
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Owner.PrepareAsync(fixture.FileId, source.CasRevisionId,
            source.Artifact.Document.DocumentId, source.Artifact.Document.Revision, "snapshot.png", false, source.StoreId, TestContext.Current.CancellationToken));
        var intent = await fixture.Owner.PrepareAsync(fixture.FileId, source.CasRevisionId, source.Artifact.Document.DocumentId,
            source.Artifact.Document.Revision, "snapshot.png", true, source.StoreId, TestContext.Current.CancellationToken);
        var capability = await fixture.Approve(intent);
        fixture.DenyRaw = true;
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => fixture.Owner.ExecuteAsync(intent, capability, TestContext.Current.CancellationToken));
        Assert.False((await fixture.Provider.GetAsync(intent.OutputFileId, TestContext.Current.CancellationToken)).IsSuccess);
        fixture.DenyRaw = false;
        fixture.WritesAllowed = false;
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => fixture.Owner.ExecuteAsync(intent, capability, TestContext.Current.CancellationToken));
        fixture.WritesAllowed = true;
        await fixture.Files.SaveAsync(source.Artifact with { Document = source.Artifact.Document.Resize(2, 2) }, source.CasRevisionId, TestContext.Current.CancellationToken);
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Owner.ExecuteAsync(intent, capability, TestContext.Current.CancellationToken));
        Assert.False((await fixture.Provider.GetAsync(intent.OutputFileId, TestContext.Current.CancellationToken)).IsSuccess);
    }

    [AvaloniaFact]
    public async Task Owning_CUI_requires_explicit_flatten_choice_and_requests_actual_Home_approval_without_publishing()
    {
        await using var fixture = await Fixture.Create();
        var ct = TestContext.Current.CancellationToken;
        var source = await fixture.Files.OpenAsync(fixture.FileId, ct);
        PicturePngExportIntent? requested = null;
        var bindings = new PicturePngExportCuiRequest(fixture.Owner, fixture.FileId, source.CasRevisionId,
            source.Artifact.Document.DocumentId, source.Artifact.Document.Revision, source.StoreId, () => fixture.WritesAllowed,
            async (intent, token) => { requested = intent; return await fixture.RequestPending(intent, token); });
        var scene = PicturePngExportCuiRequest.LoadDocument();
        Assert.Equal("picture-png-export", Assert.Single(scene.Components).AuthoredId);
        Assert.False(bindings.IsActionAvailable("9to1.Picture.Export.Prepare"));
        Assert.True(bindings.TrySetValue("FileName", "explicit-copy.png"));
        Assert.True(bindings.TrySetValue("FlattenAcknowledged", true));
        await bindings.DispatchAsync("9to1.Picture.Export.Prepare", null, ct);
        Assert.True(bindings.IsActionAvailable("9to1.Picture.Export.RequestApproval"));
        Assert.True(bindings.TryGetValue("PreparedSummary", out var preview));
        Assert.Contains("explicit-copy.png", Assert.IsType<string>(preview));
        Assert.True(bindings.TrySetValue("FileName", "changed-copy.png"));
        Assert.False(bindings.IsActionAvailable("9to1.Picture.Export.RequestApproval"));
        await bindings.DispatchAsync("9to1.Picture.Export.Prepare", null, ct);
        await bindings.DispatchAsync("9to1.Picture.Export.RequestApproval", null, ct);
        Assert.NotNull(requested);
        Assert.Equal("changed-copy.png", requested.FileName);
        Assert.False((await fixture.Provider.GetAsync(requested.OutputFileId, ct)).IsSuccess);
        Assert.False(bindings.IsActionAvailable("9to1.Picture.Export.RequestApproval"));
        Assert.False(bindings.TrySetValue("FileName", "retarget.png"));
        Assert.Equal(source.CasRevisionId, (await fixture.Files.OpenAsync(fixture.FileId, ct)).CasRevisionId);
    }

    [AvaloniaFact]
    public async Task Shared_media_image_projects_exact_owner_revision_and_raster_without_another_identity_or_store()
    {
        await using var fixture = await Fixture.Create();
        var ct = TestContext.Current.CancellationToken;
        var opened = await fixture.Files.OpenAsync(fixture.FileId, ct);
        var reference = new PictureSharedImageReference(fixture.FileId.Value, opened.Artifact.Document.DocumentId,
            opened.Artifact.Document.Revision, opened.Artifact.SourceAsset!);
        using var prepared = await fixture.Projector.PrepareAsync(reference, opened.CasRevisionId, ct);
        var handler = new PictureSharedImageObjectHandler(prepared);
        var engine = new HomeProductivityEngine(handlers: [handler]);
        var content = System.Text.Json.Nodes.JsonNode.Parse(handler.ReferenceContent.GetRawText())!.AsObject();
        content["futureOwnerField"] = "retained";
        var value = engine.CreateObject("media.image", reference.DocumentId, System.Text.Json.JsonSerializer.SerializeToElement(content));
        Assert.Equal(reference.SourceAsset.AssetId.ToString("D"), Assert.Single(value.AssetReferences));
        var rendered = engine.RenderObject(value);
        var raster = Assert.Single(rendered.RasterBindings);
        Assert.Equal(reference.DocumentId, raster.ObjectId);
        Assert.Equal(new byte[] { 0, 0, 255, 255 }, raster.Frame.CopyPixels()[..4]);
        Assert.Contains("content.futureOwnerField", rendered.RetainedUnsupportedProperties);
        Assert.Equal("retained", value.Content.GetProperty("futureOwnerField").GetString());
        Assert.Throws<InvalidDataException>(() => handler.Create(Guid.NewGuid(), value.Content));
        Assert.Throws<NotSupportedException>(() => handler.CloneForPaste(value, Guid.NewGuid()));
        var wrong = reference with { SourceAsset = reference.SourceAsset with { AssetId = Guid.NewGuid() } };
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Projector.PrepareAsync(wrong, opened.CasRevisionId, ct));
        prepared.Dispose();
        Assert.Throws<ObjectDisposedException>(() => handler.Render(value));
        Assert.Equal(opened.CasRevisionId, (await fixture.Files.OpenAsync(fixture.FileId, ct)).CasRevisionId);
    }

    [AvaloniaFact]
    public async Task Prepared_image_batch_uses_existing_distinct_document_ids_and_rejects_unprepared_or_retired_members()
    {
        await using var fixture = await Fixture.Create();
        var ct = TestContext.Current.CancellationToken;
        var first = await fixture.Files.OpenAsync(fixture.FileId, ct);
        var second = await fixture.Files.CreateAsync(new PictureDocument
        {
            DisplayName = "Second retained-source picture", CanvasWidth = 2, CanvasHeight = 1,
            FileId = first.Artifact.Document.FileId, SourceRevision = first.Artifact.Document.SourceRevision
        }, first.Artifact.SourceAsset, ct);
        PictureSharedImageReference Reference(PictureFilesOpenResult value) => new(value.Artifact.BackingFileId,
            value.Artifact.Document.DocumentId, value.Artifact.Document.Revision, value.Artifact.SourceAsset!);
        using var a = await fixture.Projector.PrepareAsync(Reference(first), first.CasRevisionId, ct);
        using var b = await fixture.Projector.PrepareAsync(Reference(second), second.CasRevisionId, ct);
        var handler = new PictureSharedImageObjectHandler(new[] { a, b });
        var engine = new HomeProductivityEngine(handlers: [handler]);
        var one = engine.CreateObject("media.image", a.Reference.DocumentId, handler.ReferenceContentFor(a.Reference.DocumentId));
        var two = engine.CreateObject("media.image", b.Reference.DocumentId, handler.ReferenceContentFor(b.Reference.DocumentId));
        var firstFrame = Assert.Single(engine.RenderObject(one).RasterBindings);
        var secondFrame = Assert.Single(engine.RenderObject(two).RasterBindings);
        Assert.Equal(a.Reference.DocumentId, firstFrame.ObjectId); Assert.Equal(1, firstFrame.Frame.Width);
        Assert.Equal(b.Reference.DocumentId, secondFrame.ObjectId); Assert.Equal(2, secondFrame.Frame.Width);
        Assert.Equal(first.Artifact.SourceAsset!.AssetId.ToString("D"), Assert.Single(one.AssetReferences));
        Assert.Equal(one.AssetReferences, two.AssetReferences);
        Assert.Throws<InvalidDataException>(() => handler.ReferenceContentFor(Guid.NewGuid()));
        Assert.Throws<ArgumentException>(() => new PictureSharedImageObjectHandler(new[] { a, a }));
        Assert.Throws<InvalidOperationException>(() => handler.ReferenceContent);
        a.Dispose();
        Assert.Throws<ObjectDisposedException>(() => engine.RenderObject(one));
        Assert.Equal(2, Assert.Single(engine.RenderObject(two).RasterBindings).Frame.Width);
        Assert.Equal(first.CasRevisionId, (await fixture.Files.OpenAsync(fixture.FileId, ct)).CasRevisionId);
        Assert.Equal(second.CasRevisionId, (await fixture.Files.OpenAsync(new(second.Artifact.BackingFileId), ct)).CasRevisionId);
    }

    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Import_and_export_refuse_original_store_replacement_after_approval_preserving_foreign_envelope(bool import)
    {
        await using var fixture = await Fixture.Create();
        var ct = TestContext.Current.CancellationToken;
        PictureImportIntent? importIntent = null;
        PicturePngExportIntent? exportIntent = null;
        HomeResourceExecutionCapability capability;
        if (import)
        {
            using var picked = await PicturePickedImage.PickAsync(new PicturePickerFixture(
                await File.ReadAllBytesAsync(Path.Combine(fixture.Root, "source.gif"), ct)).Provider, new(), ct);
            importIntent = await fixture.Import.PrepareAsync(picked!, fixture.StoreId, ct);
            capability = await fixture.ApproveImport(importIntent);
        }
        else
        {
            var source = await fixture.Files.OpenAsync(fixture.FileId, fixture.StoreId, ct);
            exportIntent = await fixture.Owner.PrepareAsync(fixture.FileId, source.CasRevisionId,
                source.Artifact.Document.DocumentId, source.Artifact.Document.Revision, "original-store.png", true, source.StoreId, ct);
            capability = await fixture.Approve(exportIntent);
        }
        try
        {
            var path = Path.Combine(fixture.Root, "drive.json");
            var envelope = System.Text.Json.Nodes.JsonNode.Parse(await File.ReadAllBytesAsync(path, ct))!.AsObject();
            envelope["state"]!.AsObject()["storeId"] = Guid.NewGuid().ToString();
            var foreign = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(envelope);
            await File.WriteAllBytesAsync(path, foreign, ct);
            if (import) await Assert.ThrowsAsync<UnauthorizedAccessException>(() => fixture.Import.ExecuteAsync(importIntent!, capability, ct));
            else await Assert.ThrowsAsync<UnauthorizedAccessException>(() => fixture.Owner.ExecuteAsync(exportIntent!, capability, ct));
            Assert.Equal(foreign, await File.ReadAllBytesAsync(path, ct));
        }
        finally { importIntent?.Dispose(); }
    }

    private sealed class Fixture : IAsyncDisposable, ICanonicalResourceAccessResolver, IAuthenticatedResourceActorSource
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "picture-export-" + Guid.NewGuid().ToString("N"));
        public DurableDriveProvider Provider { get; private set; } = null!;
        public Guid StoreId { get; private set; }
        public PictureFilesArtifactBridge Files { get; private set; } = null!;
        public PictureHomePngExportOperation Owner { get; private set; } = null!;
        public PictureHomeImportOperation Import { get; private set; } = null!;
        public PictureHomeEditOperation Edits { get; private set; } = null!;
        public PictureSharedImageProjector Projector { get; private set; } = null!;
        private HomeResourceOperationBroker _home = null!;
        private HomePermissionTrustService _permissions = null!;
        private HomeLocalProfileIdentity _actors = null!;
        private int _ownerProviderResolutions;
        private int _bridgeProviderResolutions;
        public AuthenticatedResourceActor? ActorOverride { get; set; }
        public Action? OnFinalCommitAdmission { get; set; }
        public ValueTask<FilesCommitAuthorityGuard> CaptureCommitAuthority(AuthenticatedResourceActor original, DurableDriveProvider provider, CancellationToken ct)
        {
            if (!ReferenceEquals(provider, Provider)) throw new UnauthorizedAccessException("Fixture owner provider changed.");
            var callback = OnFinalCommitAdmission; OnFinalCommitAdmission = null; callback?.Invoke();
            return ValueTask.FromResult(new FilesCommitAuthorityGuard(original.ActorId, async token =>
                await GetCurrentAsync(token) == original && WritesAllowed));
        }
        public Action<int>? OnOwnerProviderResolution { get; set; }
        public Action<int>? OnBridgeProviderResolution { get; set; }
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
        public static async Task<Fixture> Create()
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
            fixture.Files = new(actors, current => current == actor ? fixture.ResolveBridgeProvider() : null, directories, resources, () => fixture.WritesAllowed, fixture.CaptureCommitAuthority);
            var bytes = Convert.FromBase64String("R0lGODlhAgABAIEAAP8AAAAAAAAAAAAAACH/C05FVFNDQVBFMi4wAwEAAAAh+QQACAAAACwAAAAAAgABAAAIBQABAAgIACH5BAAMAAAALAAAAAACAAEAgQAA/wAAAAAAAAAAAAgFAAEACAgAOw==");
            var rawId = HostedItemId.New(); var rawRevision = new FilesRevisionId(Guid.NewGuid());
            var path = Path.Combine(fixture.Root, "source.gif");
            await File.WriteAllBytesAsync(path, bytes, ct);
            var hash = Convert.ToHexString(SHA256.HashData(bytes));
            Assert.True((await fixture.Provider.CommitUploadedContentAsync(new(rawId, fixture.FolderId, "source.gif", "image/gif",
                rawRevision, null, actor.ActorId, now, bytes.Length, hash, "source.gif"), ct)).IsSuccess);
            var reference = new PictureSourceAssetReference(rawId.Value, rawRevision.Value, hash, bytes.Length, Guid.NewGuid());
            var opened = await fixture.Files.CreateAsync(PictureDocument.Create(2, 1, rawId.ToString(), rawRevision.ToString()).Crop(0, 0, 1, 1), reference, ct);
            fixture.FileId = new(opened.Artifact.BackingFileId);
            var renderer = new PictureFilesSourceRenderer((_, _) => Task.FromResult(MediaEngineResult<MediaAssetReadLease>.Success(
                new(new(new(reference.AssetId), rawId.Value, new Uri(path), rawRevision.ToString()), () => ValueTask.CompletedTask))), resources);
            fixture.Projector = new(fixture.Files, renderer, new());
            fixture._permissions = new(store, new PictureNativeActionPolicies().TryGet);
            fixture._home = new(resources, fixture._permissions);
            fixture.Edits = new(fixture.Files, fixture._home, actors);
            fixture.Owner = new(fixture.Files, renderer, new(), new(), fixture._home, actors,
                current => current == actor ? fixture.ResolveOwnerProvider() : null, directories, resources, () => fixture.WritesAllowed, fixture.CaptureCommitAuthority);
            fixture.Import = new(fixture._home, actors, current => current == actor ? fixture.ResolveOwnerProvider() : null,
                directories, resources, () => fixture.WritesAllowed, fixture.CaptureCommitAuthority);
            fixture.StoreId = (await fixture.Provider.GetStoreEvidenceAsync()).StoreId;
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
        public async Task<HomeResourceExecutionCapability> ApproveEdit(PictureEditIntent intent)
        {
            var ct = TestContext.Current.CancellationToken;
            var pending = await _home.AuthorizeAsync("picture", PictureEditIntent.ActionId, intent.Scopes, intent.Arguments,
                "Apply this exact non-destructive Picture edit", null, "picture-edit-test", ct);
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
                ("picture.file.create", ResourceAccess.Write) or ("picture.file.import", ResourceAccess.Write) or ("picture.file.export", ResourceAccess.Read) or ("picture.file.export", ResourceAccess.Write) or ("media.asset.read", ResourceAccess.Read);
            return new(allowed, "fixture-canonical-files", actor.ActorId, scope.Revision, null);
        }
        public ValueTask DisposeAsync() { Directory.Delete(Root, true); return ValueTask.CompletedTask; }
    }
}
