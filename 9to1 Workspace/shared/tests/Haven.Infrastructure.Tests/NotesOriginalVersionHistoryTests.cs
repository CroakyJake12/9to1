using System.Text.Json;
using Haven.Application;
using Haven.Core;
using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;
using Xunit;

namespace Haven.Infrastructure.Tests;

// Same physical Notes store, real Home actor/ownership/broker and original adapter.
// These focused component tests are not Boards UI/native/platform acceptance.
public sealed partial class NotesOriginalStoreTests
{
    [LinuxNotesFact]
    public async Task Admitted_original_workspace_lists_and_loads_saved_versions_without_changing_current_or_history()
    {
        await using var rig = await HomeRig.CreateAsync();
        var (document, firstId, _, adapter) = await SeedOriginalHistoryAsync(rig);
        var before = await rig.CaptureNotesFilesAsync();
        var versions = await adapter.GetVersionsAsync(document.Id, rig.Token);
        Assert.Equal(new long[] { 2, 1 }, versions.Select(value => value.Version));
        Assert.All(versions, value => Assert.True(NotesOriginalVersionIdentity.IsVersionId(value.VersionId)));
        var previous = Assert.IsType<NotesDocument>(await adapter.LoadVersionAsync(document.Id, firstId, rig.Token));
        Assert.Equal(document.Id, previous.Id); Assert.Equal(1, previous.Version);
        Assert.Equal("First durable body", previous.Sections[0].Pages[0].Blocks[0].PlainText);
        Assert.Equal("Second durable body", document.Sections[0].Pages[0].Blocks[0].PlainText);
        Assert.Equal(2, document.Version);
        Assert.Null(await adapter.LoadVersionAsync(document.Id, "v0000000009-20260101-000000000", rig.Token));
        Assert.Empty(await adapter.GetVersionsAsync(Guid.NewGuid(), rig.Token));
        await rig.AssertNotesFilesUnchangedAsync(before);
    }

    [LinuxNotesFact]
    public async Task Version_requests_keep_the_old_library_tuple_and_bind_the_exact_new_kind_and_version()
    {
        await using var rig = await HomeRig.CreateAsync();
        var (document, firstId, _, _) = await SeedOriginalHistoryAsync(rig);
        var library = await rig.Notes.Owner.PrepareOriginalLibraryReadAsync(document.Id, rig.Token);
        Assert.Equal(NotesOriginalReadKind.LibrarySnapshot, library.Kind); Assert.Null(library.VersionId);
        Assert.Equal(new[] { "documentId", "operationId", "storeId", "storeRevision" },
            library.Arguments.EnumerateObject().Select(value => value.Name).Order(StringComparer.Ordinal));
        var history = await rig.Notes.Owner.PrepareOriginalVersionReadAsync(document.Id, cancellationToken: rig.Token);
        Assert.Equal(NotesOriginalReadKind.VersionHistory, history.Kind);
        Assert.Equal("history", history.Arguments.GetProperty("kind").GetString());
        Assert.Equal(JsonValueKind.Null, history.Arguments.GetProperty("versionId").ValueKind);
        var version = await rig.Notes.Owner.PrepareOriginalVersionReadAsync(document.Id, firstId, rig.Token);
        Assert.Equal(NotesOriginalReadKind.Version, version.Kind); Assert.Equal(firstId, version.VersionId);
        Assert.Equal("saved-version", version.Arguments.GetProperty("kind").GetString());
        Assert.Equal(firstId, version.Arguments.GetProperty("versionId").GetString());
        Assert.Equal(new[] { "documentId", "kind", "operationId", "storeId", "storeRevision", "versionId" },
            version.Arguments.EnumerateObject().Select(value => value.Name).Order(StringComparer.Ordinal));
        var copied = new CopiedHistoryRead(version.OperationId, version.DocumentId, version.StoreObservation,
            version.Arguments.Clone(), version.Kind, version.VersionId);
        var before = await rig.CaptureNotesFilesAsync();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => rig.Admission.RequestLibrarySnapshotAsync(copied, rig.SessionId, rig.Token));
        await rig.AssertNotesFilesUnchangedAsync(before);
    }

    [LinuxNotesFact]
    public async Task A_history_admission_cannot_read_a_saved_body_or_another_owner_and_is_consumed_only_once()
    {
        await using var rig = await HomeRig.CreateAsync(); await using var foreign = new Rig();
        var (document, firstId, _, _) = await SeedOriginalHistoryAsync(rig);
        var history = await rig.Notes.Owner.PrepareOriginalVersionReadAsync(document.Id, cancellationToken: rig.Token);
        var saved = await rig.Notes.Owner.PrepareOriginalVersionReadAsync(document.Id, firstId, rig.Token);
        var (review, admitted) = await rig.AcceptReadAsync(history);
        Assert.True(admitted.IsBoundToOriginalNotes(rig.Notes.Owner, history));
        var before = await rig.CaptureNotesFilesAsync();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => rig.Notes.Owner.ReadOriginalLibraryWithHomeAsync(saved, admitted, rig.Verified, rig.Token));
        var foreignVerified = new VerifiedNotesRepository(foreign.Owner, foreign, foreign.Diagnostics);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => rig.Notes.Owner.ReadOriginalLibraryWithHomeAsync(history, admitted, foreignVerified, rig.Token));
        var snapshot = await rig.Notes.Owner.ReadOriginalLibraryWithHomeAsync(history, admitted, rig.Verified, rig.Token);
        Assert.Equal(2, snapshot.Versions.Count); Assert.Null(snapshot.SelectedDocument); Assert.Empty(snapshot.Documents);
        Assert.Throws<UnauthorizedAccessException>(() => rig.Notes.Owner.CreateOriginalBoardsSummarySource(history, snapshot, admitted));
        // Successful Home completion retires the claimed attestation. Reuse is refused
        // by that original authority boundary before the repository's execution counter.
        Assert.Equal(HomePermissionRequestState.Succeeded,
            (await rig.Admission.GetLibrarySnapshotAuthorizationAsync(review, rig.Token)).State);
        Assert.False(admitted.IsBoundToOriginalNotes(rig.Notes.Owner, history));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => rig.Notes.Owner.ReadOriginalLibraryWithHomeAsync(history, admitted, rig.Verified, rig.Token));
        Assert.Equal(1, await rig.CompletionCountAsync(review.Authorization.RequestId));
        await rig.AssertNotesFilesUnchangedAsync(before);
    }

    [LinuxNotesFact]
    public async Task Pending_history_review_cannot_be_repointed_and_a_real_decline_returns_no_content_or_IO()
    {
        await using var rig = await HomeRig.CreateAsync();
        var (document, firstId, _, _) = await SeedOriginalHistoryAsync(rig);
        HomeNotesLibraryReadReview? pending = null; var calls = 0; var decline = false;
        var adapter = NotesOriginalWorkspaceRepository.BindOriginal(rig.Notes.Owner, rig.Verified, rig.Admission,
            rig.SessionId, async (review, token) =>
            {
                ++calls; pending = review;
                if (decline) Assert.True((await rig.Permissions.DecideAsync(review.Authorization.RequestId,
                    HomeApprovalChoice.Decline, cancellationToken: token)).Succeeded);
            },
            (_, _) => throw new InvalidOperationException("No save was requested."), rig.Token);
        var before = await rig.CaptureNotesFilesAsync();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => adapter.GetVersionsAsync(document.Id, rig.Token));
        Assert.NotNull(pending); Assert.Equal(1, calls);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => adapter.LoadVersionAsync(document.Id, firstId, rig.Token));
        Assert.Equal(1, calls);
        Assert.True((await rig.Permissions.DecideAsync(pending!.Authorization.RequestId, HomeApprovalChoice.Decline,
            cancellationToken: rig.Token)).Succeeded);
        Assert.Equal(HomePermissionRequestState.Denied, (await rig.Admission.GetLibrarySnapshotAuthorizationAsync(pending!, rig.Token)).State);
        decline = true;
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => adapter.GetVersionsAsync(document.Id, rig.Token));
        Assert.Equal(2, calls);
        Assert.Equal(HomePermissionRequestState.Denied, (await rig.Admission.GetLibrarySnapshotAuthorizationAsync(pending!, rig.Token)).State);
        await rig.AssertNotesFilesUnchangedAsync(before);
    }

    [LinuxNotesFact]
    public async Task Corrupt_or_incomplete_version_metadata_is_preserved_and_never_silently_omitted()
    {
        foreach (var damage in new[] { "corrupt", "missing-pair", "wrong-hash" })
        {
            await using var rig = await HomeRig.CreateAsync();
            var (document, firstId, _, adapter) = await SeedOriginalHistoryAsync(rig);
            var directory = Path.Combine(rig.Documents, document.Id.ToString("D"), "Versions");
            var metadata = Path.Combine(directory, firstId + ".meta.json");
            if (damage == "corrupt") await File.WriteAllTextAsync(metadata, "{preserve-corrupt-history", rig.Token);
            else if (damage == "missing-pair") File.Delete(Path.Combine(directory, firstId + ".haven-notes.json"));
            else
            {
                var fields = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(await File.ReadAllTextAsync(metadata, rig.Token))!;
                fields["sha256"] = JsonSerializer.SerializeToElement(new string('0', 64));
                await File.WriteAllTextAsync(metadata, JsonSerializer.Serialize(fields), rig.Token);
            }
            var before = await rig.CaptureNotesFilesAsync();
            var error = await Record.ExceptionAsync(() => adapter.GetVersionsAsync(document.Id, rig.Token));
            Assert.NotNull(error); Assert.Contains(ExceptionGraph(error!), cause => cause is NotesOriginalReadRecoveryRequiredException);
            await rig.AssertNotesFilesUnchangedAsync(before);
        }
    }

    [LinuxNotesFact]
    public async Task Saved_snapshot_identity_and_symlink_substitution_are_refused_without_repair_or_target_changes()
    {
        foreach (var damage in new[] { "foreign-document", "symlink" })
        {
            await using var rig = await HomeRig.CreateAsync();
            var (document, firstId, _, adapter) = await SeedOriginalHistoryAsync(rig);
            var versionPath = Path.Combine(rig.Documents, document.Id.ToString("D"), "Versions", firstId + ".haven-notes.json");
            string? target = null;
            if (damage == "foreign-document")
            {
                var fields = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(await File.ReadAllTextAsync(versionPath, rig.Token))!;
                fields["id"] = JsonSerializer.SerializeToElement(Guid.NewGuid());
                await File.WriteAllTextAsync(versionPath, JsonSerializer.Serialize(fields), rig.Token);
            }
            else
            {
                target = Path.Combine(rig.Notes.DataDirectory, "preserved-version-target.json");
                File.Move(versionPath, target); File.CreateSymbolicLink(versionPath, target);
            }
            var before = await rig.CaptureNotesFilesAsync(); var targetBytes = target is null ? null : await File.ReadAllBytesAsync(target, rig.Token);
            var error = await Record.ExceptionAsync(() => adapter.LoadVersionAsync(document.Id, firstId, rig.Token));
            Assert.NotNull(error); Assert.Contains(ExceptionGraph(error!), cause => cause is NotesOriginalReadRecoveryRequiredException);
            await rig.AssertNotesFilesUnchangedAsync(before);
            if (target is not null) { Assert.NotNull(new FileInfo(versionPath).LinkTarget); Assert.Equal(targetBytes, await File.ReadAllBytesAsync(target, rig.Token)); }
        }
    }

    [LinuxNotesFact]
    public async Task Pending_current_publication_is_not_replayed_by_a_saved_version_read()
    {
        await using var rig = await HomeRig.CreateAsync();
        var (document, firstId, _, adapter) = await SeedOriginalHistoryAsync(rig);
        var journal = Path.Combine(rig.Documents, document.Id.ToString("D"), "current.publication.json");
        await File.WriteAllTextAsync(journal, "{\"preserve\":\"separate-recovery-required\"}", rig.Token);
        var before = await rig.CaptureNotesFilesAsync();
        var error = await Record.ExceptionAsync(() => adapter.LoadVersionAsync(document.Id, firstId, rig.Token));
        Assert.NotNull(error); Assert.Contains(ExceptionGraph(error!), cause => cause is NotesOriginalReadRecoveryRequiredException);
        await rig.AssertNotesFilesUnchangedAsync(before);
    }

    [LinuxNotesFact]
    public async Task Actual_Home_ownership_revocation_after_history_claim_refuses_read_and_preserves_every_version()
    {
        await using var rig = await HomeRig.CreateAsync();
        var (document, _, _, _) = await SeedOriginalHistoryAsync(rig);
        var request = await rig.Notes.Owner.PrepareOriginalVersionReadAsync(document.Id, cancellationToken: rig.Token);
        var (_, admitted) = await rig.AcceptReadAsync(request);
        var state = await rig.Store.ReadAsync(rig.Token);
        var record = Assert.Single(state.State!.Records, value => value.RecordType == "home.local-store-ownership");
        var binding = record.Payload.Deserialize<HomeLocalStoreBinding>()!;
        Assert.True((await rig.Store.WriteAsync(record with { Revision = checked(record.Revision + 1),
            Payload = JsonSerializer.SerializeToElement(binding with { ProfileId = Guid.NewGuid().ToString("D") }) }, record.Revision, rig.Token)).IsSuccess);
        var before = await rig.CaptureNotesFilesAsync();
        var error = await Record.ExceptionAsync(() => rig.Notes.Owner.ReadOriginalLibraryWithHomeAsync(request, admitted, rig.Verified, rig.Token));
        Assert.NotNull(error); Assert.Contains(ExceptionGraph(error!), cause => cause is UnauthorizedAccessException);
        await rig.AssertNotesFilesUnchangedAsync(before);
    }

    private static async Task<(NotesDocument Document, string FirstId, string SecondId, NotesOriginalWorkspaceRepository Adapter)>
        SeedOriginalHistoryAsync(HomeRig rig)
    {
        var adapter = NotesOriginalWorkspaceRepository.BindOriginal(rig.Notes.Owner, rig.Verified, rig.Admission, rig.SessionId,
            async (review, token) => Assert.True((await rig.Permissions.DecideAsync(review.Authorization.RequestId,
                HomeApprovalChoice.Accept, cancellationToken: token)).Succeeded),
            async (review, token) => Assert.True((await rig.Permissions.DecideAsync(review.Authorization.RequestId,
                HomeApprovalChoice.Accept, cancellationToken: token)).Succeeded), rig.Token, body => body());
        var document = NotesDocument.Create("Original saved-version notebook");
        document.Metadata[BoardsWorkspaceService.ProductKey] = "boards";
        document.Sections[0].Pages[0].Blocks[0].PlainText = "First durable body";
        var first = await adapter.SaveAsync(document, "First actual Home-reviewed version", rig.Token);
        document.Sections[0].Pages[0].Blocks[0].PlainText = "Second durable body";
        var second = await adapter.SaveAsync(document, "Second actual Home-reviewed version", rig.Token);
        return (document, Path.GetFileName(first.VersionPath)[..^".haven-notes.json".Length],
            Path.GetFileName(second.VersionPath)[..^".haven-notes.json".Length], adapter);
    }

    private sealed record CopiedHistoryRead(Guid OperationId, Guid? DocumentId, INotesOriginalStoreObservation StoreObservation,
        JsonElement Arguments, NotesOriginalReadKind Kind, string? VersionId) : INotesOriginalLibraryReadRequest;
}
