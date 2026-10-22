using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using Haven.Application;
using Haven.Core;
using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;
using Xunit;

namespace Haven.Infrastructure.Tests;

// Physical Linux identity and genuine same-host Home-reviewed save controls.
// These are not native app, packaged platform or full Boards acceptance.
public sealed class NotesOriginalStoreTests
{
    [LinuxNotesFact]
    public async Task Observation_does_not_create_a_missing_identity_or_Notes_directory()
    {
        await using var rig = new Rig();
        await Assert.ThrowsAsync<IOException>(() => rig.Owner.GetStoreIdentityAsync(default).AsTask());
        Assert.False(Directory.Exists(rig.Notes));
    }

    [LinuxNotesFact]
    public async Task New_empty_store_has_a_durable_UUID_and_a_new_owner_does_not_claim_new_creation()
    {
        await using var rig = new Rig();
        var created = await rig.Owner.InitialiseOriginalStoreIdentityAsync();
        Assert.Equal(1, created.SchemaVersion);
        Assert.NotEqual(Guid.Empty, created.StoreId);
        Assert.True(created.NewlyCreated);
        var bytes = await File.ReadAllBytesAsync(rig.Identity);
        var fresh = rig.NewOwner();
        var reopened = await fresh.GetStoreIdentityAsync(default);
        Assert.Equal(created.StoreId, reopened.StoreId);
        Assert.Equal(created.CreatedAtUtc, reopened.CreatedAtUtc);
        Assert.False(reopened.NewlyCreated);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(rig.Identity));
        Assert.True((await fresh.ObserveOriginalStoreAsync()).IsEmpty);
    }

    [LinuxNotesFact]
    public async Task Independent_owners_initialise_one_UUID_under_the_canonical_process_lock()
    {
        await using var rig = new Rig();
        var second = rig.NewOwner();
        var observations = await Task.WhenAll(rig.Owner.InitialiseOriginalStoreIdentityAsync().AsTask(),
            second.InitialiseOriginalStoreIdentityAsync().AsTask());
        Assert.Equal(observations[0].StoreId, observations[1].StoreId);
        Assert.Single(observations, identity => identity.NewlyCreated);
    }

    [LinuxNotesFact]
    public async Task Original_identity_reads_respect_the_existing_FileShare_None_repository_lock()
    {
        await using var rig = new Rig();
        await rig.Owner.InitialiseOriginalStoreIdentityAsync();
        using var original = new FileStream(Path.Combine(rig.Notes, ".repository.lock"),
            FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        using var cancelled = new CancellationTokenSource(TimeSpan.FromMilliseconds(150));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            rig.Owner.GetStoreIdentityAsync(cancelled.Token).AsTask().WaitAsync(TimeSpan.FromSeconds(3)));
    }

    [LinuxNotesFact]
    public async Task Existing_Notes_content_is_preserved_and_is_never_marked_new_empty()
    {
        await using var rig = new Rig();
        var document = NotesDocument.Create("Computer science revision");
        document.Sections[0].Pages[0].Blocks[0].PlainText = "A stack is last in, first out.";
        var saved = await rig.Owner.SaveAsync(document, "Revision notes", default);
        var current = Path.Combine(rig.Notes, "Documents", document.Id.ToString("D"), "current.haven-notes.json");
        var preserved = await File.ReadAllBytesAsync(current);
        var identity = await rig.Owner.InitialiseOriginalStoreIdentityAsync();
        Assert.False(identity.NewlyCreated);
        Assert.False((await rig.Owner.ObserveOriginalStoreAsync()).IsEmpty);
        Assert.Equal(preserved, await File.ReadAllBytesAsync(current));
        Assert.Equal(saved.Version, (await rig.NewOwner().LoadAsync(document.Id, default))!.Version);
    }

    [LinuxNotesFact]
    public async Task A_changed_paths_provider_cannot_repoint_the_existing_canonical_repository()
    {
        await using var original = new Rig();
        await using var foreign = new Rig();
        var paths = new MutablePaths(original.DataDirectory);
        var owner = new NotesRepository(paths, new NotesDocumentValidator(), original.Diagnostics);
        paths.DataDirectory = foreign.DataDirectory;
        await owner.InitialiseOriginalStoreIdentityAsync();
        Assert.True(File.Exists(original.Identity));
        Assert.False(File.Exists(foreign.Identity));
    }

    [LinuxNotesFact]
    public async Task A_copied_observation_does_not_become_an_original_owner_observation()
    {
        await using var rig = new Rig();
        var identity = await rig.Owner.InitialiseOriginalStoreIdentityAsync();
        var actual = await rig.Owner.ObserveOriginalStoreAsync();
        var copied = new CopiedObservation(identity, actual.Revision, actual.IsEmpty);
        Assert.True(rig.Owner.IsIssuedOriginalStoreObservation(actual));
        Assert.False(rig.Owner.IsIssuedOriginalStoreObservation(copied));
        Assert.False(await rig.Owner.IsOriginalStoreObservationCurrentAsync(copied));
        Assert.False(rig.NewOwner().IsIssuedOriginalStoreObservation(actual));
    }

    [LinuxNotesFact]
    public async Task Replacing_the_identity_with_identical_bytes_invalidates_the_original_owner()
    {
        await using var rig = new Rig();
        await rig.Owner.InitialiseOriginalStoreIdentityAsync();
        var actual = await rig.Owner.ObserveOriginalStoreAsync();
        var bytes = await File.ReadAllBytesAsync(rig.Identity);
        var replacement = Path.Combine(rig.Notes, "replacement");
        await File.WriteAllBytesAsync(replacement, bytes);
        if (OperatingSystem.IsLinux()) File.SetUnixFileMode(replacement, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        File.Move(replacement, rig.Identity, overwrite: true);
        await Assert.ThrowsAsync<IOException>(() => rig.Owner.IsOriginalStoreObservationCurrentAsync(actual).AsTask());
        Assert.Equal(bytes, await File.ReadAllBytesAsync(rig.Identity));
    }

    [LinuxNotesFact]
    public async Task Corrupt_identity_is_preserved_and_never_reinitialised()
    {
        await using var rig = new Rig();
        await rig.Owner.InitialiseOriginalStoreIdentityAsync();
        await File.WriteAllTextAsync(rig.Identity, "{broken");
        var fresh = rig.NewOwner();
        await Assert.ThrowsAsync<InvalidDataException>(() => fresh.InitialiseOriginalStoreIdentityAsync().AsTask());
        Assert.Equal("{broken", await File.ReadAllTextAsync(rig.Identity));
    }

    [LinuxNotesFact]
    public async Task Missing_previously_observed_identity_is_not_silently_recreated()
    {
        await using var rig = new Rig();
        await rig.Owner.InitialiseOriginalStoreIdentityAsync();
        File.Delete(rig.Identity);
        await Assert.ThrowsAsync<IOException>(() => rig.Owner.InitialiseOriginalStoreIdentityAsync().AsTask());
        Assert.False(File.Exists(rig.Identity));
    }

    [LinuxNotesFact]
    public async Task A_symbolic_link_identity_is_refused_without_reading_or_replacing_the_target()
    {
        await using var rig = new Rig();
        await rig.Owner.InitialiseOriginalStoreIdentityAsync();
        var preserved = Path.Combine(rig.DataDirectory, "original-identity-preserved");
        File.Move(rig.Identity, preserved);
        var bytes = await File.ReadAllBytesAsync(preserved);
        File.CreateSymbolicLink(rig.Identity, preserved);
        await Assert.ThrowsAsync<IOException>(() => rig.NewOwner().ObserveOriginalStoreAsync().AsTask());
        Assert.Equal(bytes, await File.ReadAllBytesAsync(preserved));
        Assert.NotNull(new FileInfo(rig.Identity).LinkTarget);
    }

    [LinuxNotesFact]
    public async Task A_symbolic_link_Notes_directory_cannot_repoint_the_original_store()
    {
        await using var rig = new Rig();
        await rig.Owner.InitialiseOriginalStoreIdentityAsync();
        var original = Path.Combine(rig.DataDirectory, "original-notes-preserved");
        Directory.Move(rig.Notes, original);
        var bytes = await File.ReadAllBytesAsync(Path.Combine(original, ".store-identity.json"));
        Directory.CreateSymbolicLink(rig.Notes, original);
        await Assert.ThrowsAsync<IOException>(() => rig.Owner.ObserveOriginalStoreAsync().AsTask());
        Assert.Equal(bytes, await File.ReadAllBytesAsync(Path.Combine(original, ".store-identity.json")));
    }

    [LinuxNotesFact]
    public async Task A_FIFO_identity_is_rejected_before_any_blocking_read()
    {
        await using var rig = new Rig();
        await rig.Owner.InitialiseOriginalStoreIdentityAsync();
        File.Delete(rig.Identity);
        Assert.Equal(0, NativeMakeFifo(rig.Identity, 384));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            rig.NewOwner().ObserveOriginalStoreAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(3)));
    }

    [LinuxNotesFact]
    public async Task A_hard_link_identity_is_not_private_original_metadata()
    {
        await using var rig = new Rig();
        await rig.Owner.InitialiseOriginalStoreIdentityAsync();
        Assert.Equal(0, NativeLink(rig.Identity, Path.Combine(rig.DataDirectory, "identity-alias")));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => rig.NewOwner().ObserveOriginalStoreAsync().AsTask());
    }

    [LinuxNotesFact]
    public async Task Home_evidence_reports_real_metadata_without_creating_an_ownership_binding()
    {
        await using var rig = new Rig();
        var identity = await rig.Owner.InitialiseOriginalStoreIdentityAsync();
        var provider = new NotesLocalStoreEvidenceProvider(rig.Owner);
        Assert.Null(await provider.ReadAsync(Guid.NewGuid().ToString("D"), default));
        var actual = await provider.ReadAsync(identity.StoreId.ToString("D"), default);
        Assert.NotNull(actual);
        Assert.Equal("notes", actual.ResourceKind);
        Assert.True(actual.NewlyCreated);
        Assert.True(actual.IsEmpty);
        Assert.True(actual.AccessibleToCurrentOsPrincipal);
        var document = NotesDocument.Create("Maths revision");
        await rig.Owner.SaveAsync(document, "An actual local note", default);
        Assert.False((await provider.ReadAsync(identity.StoreId.ToString("D"), default))!.IsEmpty);
        Assert.DoesNotContain(Directory.EnumerateFiles(rig.DataDirectory, "*", SearchOption.AllDirectories),
            path => Path.GetFileName(path).Contains("ownership", StringComparison.OrdinalIgnoreCase));
    }

    [LinuxNotesFact]
    public async Task Home_evidence_rejects_an_uppercase_alias_of_the_canonical_UUID()
    {
        await using var rig = new Rig();
        var identity = await rig.Owner.InitialiseOriginalStoreIdentityAsync();
        // Deterministic physical fixture: preserve the actual owner-created shape and mode,
        // then open a fresh owner over a known UUID that contains hexadecimal letters.
        const string canonical = "abcdefab-cdef-4def-8def-abcdefabcdef";
        var bytes = await File.ReadAllTextAsync(rig.Identity);
        await File.WriteAllTextAsync(rig.Identity, bytes.Replace(identity.StoreId.ToString("D"), canonical,
            StringComparison.Ordinal));
        var provider = new NotesLocalStoreEvidenceProvider(rig.NewOwner());
        Assert.Null(await provider.ReadAsync(canonical.ToUpperInvariant(), default));
        Assert.NotNull(await provider.ReadAsync(canonical, default));
    }

    [LinuxNotesFact]
    public async Task Non_object_identity_JSON_is_classified_as_invalid_and_preserved()
    {
        await using var rig = new Rig();
        await rig.Owner.InitialiseOriginalStoreIdentityAsync();
        await File.WriteAllTextAsync(rig.Identity, "[1,2,3]");
        await Assert.ThrowsAsync<InvalidDataException>(() => rig.NewOwner().ObserveOriginalStoreAsync().AsTask());
        Assert.Equal("[1,2,3]", await File.ReadAllTextAsync(rig.Identity));
    }

    [LinuxNotesFact]
    public async Task Empty_original_observation_becomes_stale_after_an_actual_Notes_save()
    {
        await using var rig = new Rig();
        await rig.Owner.InitialiseOriginalStoreIdentityAsync();
        var empty = await rig.Owner.ObserveOriginalStoreAsync();
        Assert.True(empty.IsEmpty);
        await rig.Owner.SaveAsync(NotesDocument.Create("A real revision note"), "Save", default);
        Assert.False(await rig.Owner.IsOriginalStoreObservationCurrentAsync(empty));
    }

    [LinuxNotesFact]
    public async Task Replaced_repository_lock_cannot_become_the_captured_owners_canonical_lock()
    {
        await using var rig = new Rig();
        await rig.Owner.InitialiseOriginalStoreIdentityAsync();
        var lockPath = Path.Combine(rig.Notes, ".repository.lock");
        using var original = new FileStream(lockPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        File.Move(lockPath, Path.Combine(rig.DataDirectory, "old-original-lock"));
        await File.WriteAllBytesAsync(lockPath, []);
        if (OperatingSystem.IsLinux()) File.SetUnixFileMode(lockPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        await Assert.ThrowsAsync<IOException>(() =>
            rig.Owner.ObserveOriginalStoreAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(3)));
    }

    [LinuxNotesFact]
    public async Task Save_intent_keeps_the_original_candidate_when_the_editor_changes_during_review()
    {
        await using var rig = new Rig();
        await rig.Owner.InitialiseOriginalStoreIdentityAsync();
        var document = NotesDocument.Create("Binary trees revision");
        document.Sections[0].Pages[0].Blocks[0].PlainText = "A node can have at most two children.";
        var intent = await rig.Owner.PrepareOriginalSaveAsync(document, "  Revision draft  ");
        var originalArguments = intent.Arguments.GetRawText();
        var originalId = intent.DocumentId;
        var originalVersion = intent.ExpectedVersion;
        document.Title = "Changed after review";
        document.Sections[0].Pages[0].Blocks[0].PlainText = "A different candidate";
        document.Version = checked(originalVersion + 4);
        Assert.True(rig.Owner.IsIssuedOriginalSaveIntent(intent));
        Assert.False(rig.NewOwner().IsIssuedOriginalSaveIntent(intent));
        Assert.Equal(originalArguments, intent.Arguments.GetRawText());
        Assert.Equal(originalId, intent.DocumentId);
        Assert.Equal(originalVersion, intent.ExpectedVersion);
        Assert.Equal("Revision draft", intent.Reason);
        Assert.Equal(intent.ContentSha256, intent.Arguments.GetProperty("contentSha256").GetString());
        Assert.False(Directory.Exists(Path.Combine(rig.Notes, "Documents")));
    }

    [LinuxNotesFact]
    public async Task Copied_save_metadata_does_not_mint_an_original_save_intent()
    {
        await using var rig = new Rig();
        await rig.Owner.InitialiseOriginalStoreIdentityAsync();
        var actual = await rig.Owner.PrepareOriginalSaveAsync(NotesDocument.Create("Queues revision"), "Save");
        var copied = new CopiedSaveIntent(actual.OperationId, actual.DocumentId, actual.ExpectedVersion,
            actual.ContentSha256, actual.Reason, actual.StoreObservation, actual.Arguments.Clone());
        Assert.False(rig.Owner.IsIssuedOriginalSaveIntent(copied));
        Assert.True(await rig.Owner.IsOriginalStoreObservationCurrentAsync(actual.StoreObservation));
    }

    [Fact]
    public void Save_requires_individual_review_and_create_delete_remain_unregistered()
    {
        var policies = new HomeNotesActionPolicies();
        Assert.NotNull(policies.TryGet("boards", "boards.library.read"));
        Assert.True(policies.TryGet("boards", "boards.document.save")!.RequiresPerActionApproval);
        Assert.Null(policies.TryGet("boards", "boards.document.create"));
        Assert.Null(policies.TryGet("boards", "boards.document.delete"));
        Assert.Null(policies.TryGet("foreign-app", "boards.library.read"));
    }

    [LinuxNotesFact]
    public async Task Actual_Home_review_commits_original_candidate_integrity_and_fresh_owner_reopen()
    {
        await using var rig = await HomeRig.CreateAsync();
        var document = NotesDocument.Create("Computer science revision");
        document.Sections[0].Pages[0].Blocks[0].PlainText = "A queue is first in, first out.";
        var intent = await rig.Notes.Owner.PrepareOriginalSaveAsync(document, "Revision draft", rig.Token);
        var review = await rig.Admission.RequestSaveAsync(intent, rig.SessionId, rig.Token);
        Assert.Equal(HomePermissionRequestState.PendingApproval, review.Authorization.State);
        Assert.Null(await rig.Admission.BeginSaveAsync(review, rig.Token));
        Assert.False(Directory.Exists(rig.Documents));
        Assert.True((await rig.Permissions.DecideAsync(review.Authorization.RequestId,
            HomeApprovalChoice.Accept, cancellationToken: rig.Token)).Succeeded);
        var admission = await rig.Admission.BeginSaveAsync(review, rig.Token);
        Assert.NotNull(admission);
        // The public editor keeps working while the original detached candidate is reviewed.
        document.Title = "A later unreviewed title";
        document.Sections[0].Pages[0].Blocks[0].PlainText = "A different unreviewed candidate";
        var receipt = await rig.Notes.Owner.SaveOriginalWithHomeAsync(intent, admission!, rig.Verified, rig.Token);
        Assert.Equal(1, receipt.Version);
        Assert.True(receipt.VersionHistoryComplete);
        Assert.Null(receipt.PostCommitWarning);
        var directory = Path.Combine(rig.Documents, intent.DocumentId.ToString("D"));
        Assert.Equal(Path.Combine(directory, "current.haven-notes.json"), receipt.CurrentPath);
        Assert.True(File.Exists(receipt.VersionPath));
        Assert.False(File.Exists(Path.Combine(directory, "current.publication.json")));
        Assert.False(File.Exists(Path.Combine(directory, "current.integrity.pending.json")));
        using var manifest = JsonDocument.Parse(await File.ReadAllBytesAsync(Path.Combine(directory, "current.integrity.json"), rig.Token));
        Assert.Equal(receipt.Version, manifest.RootElement.GetProperty("documentVersion").GetInt64());
        Assert.Equal(receipt.Sha256, manifest.RootElement.GetProperty("sha256").GetString(), ignoreCase: true);
        Assert.Equal(receipt.Sha256, Convert.ToHexString(SHA256.HashData(
            await File.ReadAllBytesAsync(receipt.CurrentPath, rig.Token))), ignoreCase: true);
        var fresh = new VerifiedNotesRepository(rig.Notes.NewOwner(), rig.Notes, rig.Notes.Diagnostics);
        var reopened = await fresh.LoadAsync(intent.DocumentId, rig.Token);
        Assert.NotNull(reopened);
        Assert.Equal("Computer science revision", reopened!.Title);
        Assert.Equal("A queue is first in, first out.", reopened.Sections[0].Pages[0].Blocks[0].PlainText);
        Assert.Equal(receipt.Version, reopened.Version);
        Assert.Equal(HomePermissionRequestState.Succeeded,
            (await rig.Admission.GetSaveAuthorizationAsync(review, rig.Token)).State);
    }

    [LinuxNotesFact]
    public async Task Duplicate_original_intent_cannot_commit_or_complete_a_second_execution()
    {
        await using var rig = await HomeRig.CreateAsync();
        var intent = await rig.Notes.Owner.PrepareOriginalSaveAsync(NotesDocument.Create("Revision plan"), "Save", rig.Token);
        var (review, admission) = await rig.AcceptAsync(intent);
        var receipt = await rig.Notes.Owner.SaveOriginalWithHomeAsync(intent, admission, rig.Verified, rig.Token);
        var current = await File.ReadAllBytesAsync(receipt.CurrentPath, rig.Token);
        var auditsBefore = await rig.CompletionCountAsync(review);
        Assert.Equal(1, auditsBefore);
        var refusal = await Record.ExceptionAsync(() => rig.Notes.Owner.SaveOriginalWithHomeAsync(intent, admission, rig.Verified, rig.Token));
        Assert.NotNull(refusal);
        Assert.True(refusal is UnauthorizedAccessException or InvalidOperationException, refusal!.ToString());
        Assert.Equal(current, await File.ReadAllBytesAsync(receipt.CurrentPath, rig.Token));
        Assert.Equal(auditsBefore, await rig.CompletionCountAsync(review));
        Assert.True((await rig.Notes.Owner.RetryOriginalSaveAuditAsync(intent, admission, rig.Token)).Succeeded);
        Assert.Equal(auditsBefore, await rig.CompletionCountAsync(review));
        Assert.Equal(receipt.Version, (await rig.Verified.LoadAsync(intent.DocumentId, rig.Token))!.Version);
    }

    [LinuxNotesFact]
    public async Task Copied_or_foreign_authority_cannot_publish_or_fabricate_a_successful_Home_outcome()
    {
        await using var rig = await HomeRig.CreateAsync();
        await using var foreign = new Rig();
        var intent = await rig.Notes.Owner.PrepareOriginalSaveAsync(NotesDocument.Create("Trees revision"), "Save", rig.Token);
        var copied = new CopiedSaveIntent(intent.OperationId, intent.DocumentId, intent.ExpectedVersion,
            intent.ContentSha256, intent.Reason, intent.StoreObservation, intent.Arguments.Clone());
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => rig.Admission.RequestSaveAsync(copied, rig.SessionId, rig.Token));
        var (review, admission) = await rig.AcceptAsync(intent);
        var invented = new CopiedSaveOutcome(intent, NotesOriginalSaveDisposition.Committed, 1, new string('a', 64), "INVENTED");
        Assert.False((await admission.CompleteObservedAsync(rig.Notes.Owner, invented, rig.Token)).Succeeded);
        Assert.False((await admission.CompleteObservedAsync(foreign.Owner, invented, rig.Token)).Succeeded);
        var foreignVerified = new VerifiedNotesRepository(foreign.Owner, foreign, foreign.Diagnostics);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            foreign.Owner.SaveOriginalWithHomeAsync(intent, admission, foreignVerified, rig.Token));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            rig.Notes.Owner.SaveOriginalWithHomeAsync(intent, admission, foreignVerified, rig.Token));
        Assert.False(Directory.Exists(rig.Documents));
        Assert.False(Directory.Exists(Path.Combine(foreign.Notes, "Documents")));
        Assert.Equal(HomePermissionRequestState.Executing,
            (await rig.Admission.GetSaveAuthorizationAsync(review, rig.Token)).State);
        // The genuine original remains usable after the counterfeit attempts are refused.
        var receipt = await rig.Notes.Owner.SaveOriginalWithHomeAsync(intent, admission, rig.Verified, rig.Token);
        Assert.Equal(1, receipt.Version);
        Assert.Equal(1, await rig.CompletionCountAsync(review));
    }

    [LinuxNotesFact]
    public async Task Actual_Home_ownership_revoke_after_claim_refuses_before_document_publication()
    {
        await using var rig = await HomeRig.CreateAsync();
        var intent = await rig.Notes.Owner.PrepareOriginalSaveAsync(NotesDocument.Create("Ownership revision"), "Save", rig.Token);
        var (_, admission) = await rig.AcceptAsync(intent);
        var state = await rig.Store.ReadAsync(rig.Token);
        Assert.True(state.IsSuccess);
        var record = Assert.Single(state.State!.Records, value => value.RecordType == "home.local-store-ownership");
        var binding = record.Payload.Deserialize<HomeLocalStoreBinding>()!;
        var revoked = record with { Revision = checked(record.Revision + 1),
            Payload = JsonSerializer.SerializeToElement(binding with { ProfileId = Guid.NewGuid().ToString("D") }) };
        Assert.True((await rig.Store.WriteAsync(revoked, record.Revision, rig.Token)).IsSuccess);
        var error = await Record.ExceptionAsync(() => rig.Notes.Owner.SaveOriginalWithHomeAsync(intent, admission, rig.Verified, rig.Token));
        Assert.NotNull(error);
        Assert.Contains(Leaves(error!), cause => cause is UnauthorizedAccessException);
        Assert.False(Directory.Exists(rig.Documents));
    }

    [LinuxNotesFact]
    public async Task Actual_Home_profile_change_after_claim_refuses_the_original_actor()
    {
        await using var rig = await HomeRig.CreateAsync();
        var intent = await rig.Notes.Owner.PrepareOriginalSaveAsync(NotesDocument.Create("Profile revision"), "Save", rig.Token);
        var (_, admission) = await rig.AcceptAsync(intent);
        var state = await rig.Store.ReadAsync(rig.Token);
        Assert.True(state.IsSuccess);
        var record = Assert.Single(state.State!.Records, value => value.RecordId == "home.local-profile");
        var profile = record.Payload.Deserialize<HomeLocalProfile>()!;
        var changed = record with { Revision = checked(record.Revision + 1),
            Payload = JsonSerializer.SerializeToElement(profile with { ProfileId = Guid.NewGuid() }) };
        Assert.True((await rig.Store.WriteAsync(changed, record.Revision, rig.Token)).IsSuccess);
        var error = await Record.ExceptionAsync(() => rig.Notes.Owner.SaveOriginalWithHomeAsync(intent, admission, rig.Verified, rig.Token));
        Assert.NotNull(error);
        Assert.Contains(Leaves(error!), cause => cause is UnauthorizedAccessException);
        Assert.False(Directory.Exists(rig.Documents));
    }

    [LinuxNotesFact]
    public async Task Genuine_Home_caller_block_after_claim_is_seen_by_the_final_owning_fence()
    {
        await using var rig = await HomeRig.CreateAsync();
        var intent = await rig.Notes.Owner.PrepareOriginalSaveAsync(NotesDocument.Create("Blocked revision"), "Save", rig.Token);
        var (review, admission) = await rig.AcceptAsync(intent);
        var actor = await rig.Profiles.GetCurrentAsync(rig.Token);
        Assert.NotNull(actor);
        Assert.True((await rig.Permissions.BlockCallerAsync(actor!.ActorId, rig.Token)).Succeeded);
        var error = await Record.ExceptionAsync(() => rig.Notes.Owner.SaveOriginalWithHomeAsync(intent, admission, rig.Verified, rig.Token));
        Assert.NotNull(error);
        Assert.Contains(Leaves(error!), cause => cause is UnauthorizedAccessException);
        Assert.False(Directory.Exists(rig.Documents));
        var audit = await rig.Permissions.GetSnapshotAsync(cancellationToken: rig.Token);
        Assert.DoesNotContain(audit.RecentAuditEvents, value => value.RequestId == review.Authorization.RequestId &&
            value.Kind == HomePermissionAuditKind.ExecutionCompleted && value.RequestState == HomePermissionRequestState.Succeeded);
    }

    [LinuxNotesFact]
    public async Task Protected_save_preserves_the_canonical_competing_writer_and_reports_CAS_conflict()
    {
        await using var rig = await HomeRig.CreateAsync();
        var document = NotesDocument.Create("Algorithms revision");
        await rig.Verified.SaveAsync(document, "First actual version", rig.Token);
        var reviewed = await rig.Verified.LoadAsync(document.Id, rig.Token);
        Assert.NotNull(reviewed);
        reviewed!.Sections[0].Pages[0].Blocks[0].PlainText = "The reviewed older version";
        var intent = await rig.Notes.Owner.PrepareOriginalSaveAsync(reviewed, "Save", rig.Token);
        var (_, admission) = await rig.AcceptAsync(intent);
        var competing = await rig.Verified.LoadAsync(document.Id, rig.Token);
        competing!.Sections[0].Pages[0].Blocks[0].PlainText = "The newer canonical writer";
        var current = await rig.Verified.SaveAsync(competing, "Competing actual writer", rig.Token);
        var preserved = await File.ReadAllBytesAsync(current.CurrentPath, rig.Token);
        var error = await Record.ExceptionAsync(() => rig.Notes.Owner.SaveOriginalWithHomeAsync(intent, admission, rig.Verified, rig.Token));
        Assert.NotNull(error);
        Assert.Contains(Leaves(error!), cause => cause is NotesRevisionConflictException);
        Assert.Equal(preserved, await File.ReadAllBytesAsync(current.CurrentPath, rig.Token));
        Assert.Equal(current.Version, (await rig.Verified.LoadAsync(document.Id, rig.Token))!.Version);
    }

    [LinuxNotesFact]
    public async Task Protected_save_refuses_a_symlink_document_directory_and_preserves_its_target()
    {
        await using var rig = await HomeRig.CreateAsync();
        var document = NotesDocument.Create("Symlink revision");
        var target = Path.Combine(rig.Notes.DataDirectory, "unrelated-original-directory");
        Directory.CreateDirectory(target);
        var sentinel = Path.Combine(target, "preserve.txt");
        await File.WriteAllTextAsync(sentinel, "Unrelated original bytes", rig.Token);
        Directory.CreateDirectory(rig.Documents);
        Directory.CreateSymbolicLink(Path.Combine(rig.Documents, document.Id.ToString("D")), target);
        var intent = await rig.Notes.Owner.PrepareOriginalSaveAsync(document, "Save", rig.Token);
        var (_, admission) = await rig.AcceptAsync(intent);
        var error = await Record.ExceptionAsync(() => rig.Notes.Owner.SaveOriginalWithHomeAsync(intent, admission, rig.Verified, rig.Token));
        Assert.NotNull(error);
        Assert.Contains(Leaves(error!), cause => cause is IOException or UnauthorizedAccessException);
        Assert.Equal("Unrelated original bytes", await File.ReadAllTextAsync(sentinel, rig.Token));
        Assert.Single(Directory.EnumerateFileSystemEntries(target));
    }

    [LinuxNotesFact]
    public async Task Protected_save_refuses_hardlinked_current_without_changing_either_name()
    {
        await using var rig = await HomeRig.CreateAsync();
        var document = NotesDocument.Create("Hardlink revision");
        var prior = await rig.Verified.SaveAsync(document, "Existing actual version", rig.Token);
        var alias = Path.Combine(rig.Notes.DataDirectory, "current-preserved-alias");
        Assert.Equal(0, NativeLink(prior.CurrentPath, alias));
        var preserved = await File.ReadAllBytesAsync(alias, rig.Token);
        document.Sections[0].Pages[0].Blocks[0].PlainText = "The replacement must be refused";
        var intent = await rig.Notes.Owner.PrepareOriginalSaveAsync(document, "Save", rig.Token);
        var (_, admission) = await rig.AcceptAsync(intent);
        var error = await Record.ExceptionAsync(() => rig.Notes.Owner.SaveOriginalWithHomeAsync(intent, admission, rig.Verified, rig.Token));
        Assert.NotNull(error);
        Assert.Contains(Leaves(error!), cause => cause is UnauthorizedAccessException);
        Assert.Equal(preserved, await File.ReadAllBytesAsync(prior.CurrentPath, rig.Token));
        Assert.Equal(preserved, await File.ReadAllBytesAsync(alias, rig.Token));
    }

    [LinuxNotesFact]
    public async Task Actual_Home_prompt_identifies_the_reviewed_document_version_and_reason_before_approval()
    {
        await using var rig = await HomeRig.CreateAsync();
        var document = NotesDocument.Create("Human reviewed revision");
        const string reason = "Save my revision answers";
        var intent = await rig.Notes.Owner.PrepareOriginalSaveAsync(document, reason, rig.Token);
        var review = await rig.Admission.RequestSaveAsync(intent, rig.SessionId, rig.Token);
        var home = await rig.Permissions.GetSnapshotAsync(cancellationToken: rig.Token);
        var pending = Assert.Single(home.PendingRequests, value => value.RequestId == review.Authorization.RequestId);
        Assert.Equal(HomePermissionRequestState.PendingApproval, pending.State);
        Assert.True(pending.Policy.RequiresPerActionApproval);
        var preview = pending.Impact.ChangePreview;
        Assert.NotNull(preview);
        Assert.Contains(intent.DocumentId.ToString("D"), preview!);
        Assert.Contains(reason, preview!);
        Assert.Matches(@"\b1\s+Boards document\b", preview!);
        Assert.Matches(@"\bVersion:\s*0\s*→\s*1\b", preview!);
        Assert.Equal(intent.StoreObservation.StoreIdentity.StoreId.ToString("D"),
            Assert.Single(pending.Impact.ResourceBinding!.Scopes).Id);
        Assert.False(Directory.Exists(rig.Documents));
    }

    [LinuxNotesFact]
    public async Task Actual_Home_read_returns_saved_summaries_and_only_selected_document_without_changing_Notes_bytes()
    {
        await using var rig = await HomeRig.CreateAsync();
        var selected = NotesDocument.Create("Networks revision");
        selected.Sections[0].Pages[0].Blocks[0].PlainText = "A router forwards packets between networks.";
        await rig.Verified.SaveAsync(selected, "Existing selected note", rig.Token);
        var other = NotesDocument.Create("Other revision");
        other.Sections[0].Pages[0].Blocks[0].PlainText = "A separate document body";
        await rig.Verified.SaveAsync(other, "Existing other note", rig.Token);
        var before = await rig.CaptureNotesFilesAsync();
        var request = await rig.Notes.Owner.PrepareOriginalLibraryReadAsync(selected.Id, rig.Token);
        var review = await rig.Admission.RequestLibrarySnapshotAsync(request, rig.SessionId, rig.Token);
        Assert.Equal(HomePermissionRequestState.PendingApproval, review.Authorization.State);
        var home = await rig.Permissions.GetSnapshotAsync(cancellationToken: rig.Token);
        var pending = Assert.Single(home.PendingRequests, value => value.RequestId == review.Authorization.RequestId);
        Assert.Contains(selected.Id.ToString("D"), pending.Impact.ChangePreview!);
        Assert.Contains("summaries", pending.Impact.ChangePreview!);
        Assert.Null(await rig.Admission.BeginLibrarySnapshotAsync(review, rig.Token));
        await rig.AssertNotesFilesUnchangedAsync(before);
        Assert.True((await rig.Permissions.DecideAsync(review.Authorization.RequestId,
            HomeApprovalChoice.Accept, cancellationToken: rig.Token)).Succeeded);
        var admission = await rig.Admission.BeginLibrarySnapshotAsync(review, rig.Token);
        Assert.NotNull(admission);
        var snapshot = await rig.Notes.Owner.ReadOriginalLibraryWithHomeAsync(request, admission!, rig.Verified, rig.Token);
        Assert.Null(snapshot.AuditWarning);
        Assert.Equal(request.StoreObservation.StoreIdentity.StoreId, snapshot.StoreIdentity.StoreId);
        Assert.Equal(new[] { selected.Id, other.Id }.Order(), snapshot.Documents.Select(item => item.Id).Order());
        Assert.Contains(snapshot.Documents, item => item.Id == selected.Id && item.Title == selected.Title);
        Assert.Contains(snapshot.Documents, item => item.Id == other.Id && item.Title == other.Title);
        Assert.NotNull(snapshot.SelectedDocument);
        Assert.Equal(selected.Id, snapshot.SelectedDocument!.Id);
        Assert.Equal("A router forwards packets between networks.", snapshot.SelectedDocument.Sections[0].Pages[0].Blocks[0].PlainText);
        Assert.Equal(HomePermissionRequestState.Succeeded,
            (await rig.Admission.GetLibrarySnapshotAuthorizationAsync(review, rig.Token)).State);
        await rig.AssertNotesFilesUnchangedAsync(before);
    }

    [LinuxNotesFact]
    public async Task Summary_only_request_returns_no_document_body_and_preserves_all_existing_files()
    {
        await using var rig = await HomeRig.CreateAsync();
        var document = NotesDocument.Create("Database revision");
        document.Sections[0].Pages[0].Blocks[0].PlainText = "The selected body was not requested.";
        await rig.Verified.SaveAsync(document, "Existing actual note", rig.Token);
        var before = await rig.CaptureNotesFilesAsync();
        var request = await rig.Notes.Owner.PrepareOriginalLibraryReadAsync(null, rig.Token);
        var (_, admission) = await rig.AcceptReadAsync(request);
        var snapshot = await rig.Notes.Owner.ReadOriginalLibraryWithHomeAsync(request, admission, rig.Verified, rig.Token);
        Assert.Null(snapshot.AuditWarning);
        Assert.Equal(document.Id, Assert.Single(snapshot.Documents).Id);
        Assert.Null(snapshot.SelectedDocument);
        await rig.AssertNotesFilesUnchangedAsync(before);
    }

    [LinuxNotesFact]
    public async Task Copied_or_foreign_read_request_and_outcome_do_not_replace_actual_read_authority()
    {
        await using var rig = await HomeRig.CreateAsync();
        await using var foreign = new Rig();
        var document = NotesDocument.Create("Operating systems revision");
        await rig.Verified.SaveAsync(document, "Existing actual note", rig.Token);
        var request = await rig.Notes.Owner.PrepareOriginalLibraryReadAsync(document.Id, rig.Token);
        var copied = new CopiedLibraryReadRequest(request.OperationId, request.DocumentId,
            request.StoreObservation, request.Arguments.Clone());
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            rig.Admission.RequestLibrarySnapshotAsync(copied, rig.SessionId, rig.Token));
        var (review, admission) = await rig.AcceptReadAsync(request);
        var before = await rig.CaptureNotesFilesAsync();
        var invented = new CopiedLibraryReadOutcome(request, true, 1, true, new string('a', 64), "INVENTED");
        Assert.False((await admission.CompleteObservedAsync(rig.Notes.Owner, invented, rig.Token)).Succeeded);
        Assert.False((await admission.CompleteObservedAsync(foreign.Owner, invented, rig.Token)).Succeeded);
        var foreignVerified = new VerifiedNotesRepository(foreign.Owner, foreign, foreign.Diagnostics);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            foreign.Owner.ReadOriginalLibraryWithHomeAsync(request, admission, foreignVerified, rig.Token));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            rig.Notes.Owner.ReadOriginalLibraryWithHomeAsync(request, admission, foreignVerified, rig.Token));
        Assert.Equal(HomePermissionRequestState.Executing,
            (await rig.Admission.GetLibrarySnapshotAuthorizationAsync(review, rig.Token)).State);
        var actual = await rig.Notes.Owner.ReadOriginalLibraryWithHomeAsync(request, admission, rig.Verified, rig.Token);
        Assert.Equal(document.Id, actual.SelectedDocument!.Id);
        Assert.Equal(1, await rig.CompletionCountAsync(review.Authorization.RequestId));
        await rig.AssertNotesFilesUnchangedAsync(before);
    }

    [LinuxNotesFact]
    public async Task Actual_Home_block_after_read_claim_refuses_snapshot_without_Notes_recovery_writes()
    {
        await using var rig = await HomeRig.CreateAsync();
        var document = NotesDocument.Create("Security revision");
        await rig.Verified.SaveAsync(document, "Existing actual note", rig.Token);
        var request = await rig.Notes.Owner.PrepareOriginalLibraryReadAsync(document.Id, rig.Token);
        var (review, admission) = await rig.AcceptReadAsync(request);
        var actor = await rig.Profiles.GetCurrentAsync(rig.Token);
        Assert.True((await rig.Permissions.BlockCallerAsync(actor!.ActorId, rig.Token)).Succeeded);
        var before = await rig.CaptureNotesFilesAsync();
        var error = await Record.ExceptionAsync(() =>
            rig.Notes.Owner.ReadOriginalLibraryWithHomeAsync(request, admission, rig.Verified, rig.Token));
        Assert.NotNull(error);
        Assert.Contains(Leaves(error!), cause => cause is UnauthorizedAccessException);
        await rig.AssertNotesFilesUnchangedAsync(before);
        var audit = await rig.Permissions.GetSnapshotAsync(cancellationToken: rig.Token);
        Assert.DoesNotContain(audit.RecentAuditEvents, value => value.RequestId == review.Authorization.RequestId &&
            value.Kind == HomePermissionAuditKind.ExecutionCompleted && value.RequestState == HomePermissionRequestState.Succeeded);
    }

    [LinuxNotesFact]
    public async Task Corrupt_integrity_manifest_requires_recovery_and_is_preserved_by_protected_read()
    {
        await using var rig = await HomeRig.CreateAsync();
        var document = NotesDocument.Create("Memory revision");
        var saved = await rig.Verified.SaveAsync(document, "Existing actual note", rig.Token);
        var manifest = Path.Combine(Path.GetDirectoryName(saved.CurrentPath)!, "current.integrity.json");
        await File.WriteAllTextAsync(manifest, "{preserve-corrupt-original-manifest", rig.Token);
        var before = await rig.CaptureNotesFilesAsync();
        var request = await rig.Notes.Owner.PrepareOriginalLibraryReadAsync(document.Id, rig.Token);
        var (review, admission) = await rig.AcceptReadAsync(request);
        var error = await Record.ExceptionAsync(() =>
            rig.Notes.Owner.ReadOriginalLibraryWithHomeAsync(request, admission, rig.Verified, rig.Token));
        Assert.NotNull(error);
        Assert.Contains(Leaves(error!), cause => cause is NotesOriginalReadRecoveryRequiredException);
        await rig.AssertNotesFilesUnchangedAsync(before);
        Assert.Equal(HomePermissionRequestState.Failed,
            (await rig.Admission.GetLibrarySnapshotAuthorizationAsync(review, rig.Token)).State);
    }

    [LinuxNotesFact]
    public async Task Unresolved_publication_journal_requires_separate_recovery_and_is_never_replayed_by_read()
    {
        await using var rig = await HomeRig.CreateAsync();
        var document = NotesDocument.Create("Concurrency revision");
        var saved = await rig.Verified.SaveAsync(document, "Existing actual note", rig.Token);
        var journal = Path.Combine(Path.GetDirectoryName(saved.CurrentPath)!, "current.publication.json");
        await File.WriteAllTextAsync(journal, "{\"preserve\":\"unresolved-original-publication\"}", rig.Token);
        var before = await rig.CaptureNotesFilesAsync();
        var request = await rig.Notes.Owner.PrepareOriginalLibraryReadAsync(document.Id, rig.Token);
        var (review, admission) = await rig.AcceptReadAsync(request);
        var error = await Record.ExceptionAsync(() =>
            rig.Notes.Owner.ReadOriginalLibraryWithHomeAsync(request, admission, rig.Verified, rig.Token));
        Assert.NotNull(error);
        Assert.Contains(Leaves(error!), cause => cause is NotesOriginalReadRecoveryRequiredException);
        await rig.AssertNotesFilesUnchangedAsync(before);
        Assert.Equal(HomePermissionRequestState.Failed,
            (await rig.Admission.GetLibrarySnapshotAuthorizationAsync(review, rig.Token)).State);
    }

    [LinuxNotesFact]
    public async Task Duplicate_original_read_cannot_create_another_read_completion_audit()
    {
        await using var rig = await HomeRig.CreateAsync();
        var document = NotesDocument.Create("Data types revision");
        await rig.Verified.SaveAsync(document, "Existing actual note", rig.Token);
        var request = await rig.Notes.Owner.PrepareOriginalLibraryReadAsync(document.Id, rig.Token);
        var (review, admission) = await rig.AcceptReadAsync(request);
        await rig.Notes.Owner.ReadOriginalLibraryWithHomeAsync(request, admission, rig.Verified, rig.Token);
        var before = await rig.CaptureNotesFilesAsync();
        Assert.Equal(1, await rig.CompletionCountAsync(review.Authorization.RequestId));
        var refusal = await Record.ExceptionAsync(() =>
            rig.Notes.Owner.ReadOriginalLibraryWithHomeAsync(request, admission, rig.Verified, rig.Token));
        Assert.NotNull(refusal);
        Assert.True(refusal is UnauthorizedAccessException or InvalidOperationException, refusal!.ToString());
        Assert.True((await admission.RetryCompletionAuditAsync(rig.Notes.Owner, rig.Token)).Succeeded);
        Assert.Equal(1, await rig.CompletionCountAsync(review.Authorization.RequestId));
        await rig.AssertNotesFilesUnchangedAsync(before);
    }

    [LinuxNotesFact]
    public async Task Dangling_pending_metadata_is_present_and_requires_preserved_recovery()
    {
        foreach (var leaf in new[] { "current.integrity.pending.json", "current.recovery.json" })
        {
            await using var rig = await HomeRig.CreateAsync();
            var document = NotesDocument.Create("Pending-link revision");
            var saved = await rig.Verified.SaveAsync(document, "Existing actual note", rig.Token);
            var directory = Path.GetDirectoryName(saved.CurrentPath)!;
            var manifest = Path.Combine(directory, "current.integrity.json");
            var currentBytes = await File.ReadAllBytesAsync(saved.CurrentPath, rig.Token);
            var manifestBytes = await File.ReadAllBytesAsync(manifest, rig.Token);
            var pending = Path.Combine(directory, leaf);
            var missing = Path.Combine(rig.Notes.DataDirectory, "missing-pending-target");
            File.CreateSymbolicLink(pending, missing);
            var names = Directory.EnumerateFileSystemEntries(directory).Select(Path.GetFileName).Order().ToArray();
            var request = await rig.Notes.Owner.PrepareOriginalLibraryReadAsync(document.Id, rig.Token);
            var (review, admission) = await rig.AcceptReadAsync(request);
            var error = await Record.ExceptionAsync(() => rig.Notes.Owner.ReadOriginalLibraryWithHomeAsync(request,
                admission, rig.Verified, rig.Token));
            Assert.NotNull(error);
            Assert.Contains(Leaves(error!), cause => cause is NotesOriginalReadRecoveryRequiredException);
            Assert.Equal(missing, new FileInfo(pending).LinkTarget);
            Assert.False(File.Exists(missing));
            Assert.Equal(names, Directory.EnumerateFileSystemEntries(directory).Select(Path.GetFileName).Order());
            Assert.Equal(currentBytes, await File.ReadAllBytesAsync(saved.CurrentPath, rig.Token));
            Assert.Equal(manifestBytes, await File.ReadAllBytesAsync(manifest, rig.Token));
            Assert.Equal(HomePermissionRequestState.Failed,
                (await rig.Admission.GetLibrarySnapshotAuthorizationAsync(review, rig.Token)).State);
        }
    }

    [LinuxNotesFact]
    public async Task Canonical_UUID_entries_that_are_not_directories_cannot_be_silently_omitted()
    {
        foreach (var danglingLink in new[] { false, true })
        {
            await using var rig = await HomeRig.CreateAsync();
            var document = NotesDocument.Create("Canonical-entry revision");
            var saved = await rig.Verified.SaveAsync(document, "Existing actual note", rig.Token);
            var canonical = Path.GetDirectoryName(saved.CurrentPath)!;
            var preserved = Path.Combine(rig.Notes.DataDirectory, "preserved-original-directory");
            Directory.Move(canonical, preserved);
            var preservedCurrent = Path.Combine(preserved, "current.haven-notes.json");
            var originalBytes = await File.ReadAllBytesAsync(preservedCurrent, rig.Token);
            var missing = Path.Combine(rig.Notes.DataDirectory, "missing-document-directory");
            if (danglingLink) File.CreateSymbolicLink(canonical, missing);
            else await File.WriteAllTextAsync(canonical, "Preserve this canonical non-directory entry", rig.Token);
            var request = await rig.Notes.Owner.PrepareOriginalLibraryReadAsync(document.Id, rig.Token);
            var (review, admission) = await rig.AcceptReadAsync(request);
            var error = await Record.ExceptionAsync(() => rig.Notes.Owner.ReadOriginalLibraryWithHomeAsync(request,
                admission, rig.Verified, rig.Token));
            Assert.NotNull(error);
            Assert.Contains(Leaves(error!), cause => cause is NotesOriginalReadRecoveryRequiredException);
            Assert.Equal(originalBytes, await File.ReadAllBytesAsync(preservedCurrent, rig.Token));
            if (danglingLink) Assert.Equal(missing, new FileInfo(canonical).LinkTarget);
            else Assert.Equal("Preserve this canonical non-directory entry", await File.ReadAllTextAsync(canonical, rig.Token));
            Assert.False(Directory.Exists(missing));
            Assert.Equal(HomePermissionRequestState.Failed,
                (await rig.Admission.GetLibrarySnapshotAuthorizationAsync(review, rig.Token)).State);
        }
    }

    private static IEnumerable<Exception> Leaves(Exception error) => error is AggregateException aggregate
        ? aggregate.InnerExceptions.SelectMany(Leaves) : [error];

    [LinuxNotesFact]
    public async Task Actual_Boards_metadata_uses_same_product_and_trash_filter_without_loading_all_bodies()
    {
        await using var rig = await HomeRig.CreateAsync();
        var active = NotesDocument.Create("Binary search revision");
        active.Metadata[BoardsWorkspaceService.ProductKey] = "BoArDs";
        active.Metadata[BoardsWorkspaceService.DeletedNotebookKey] = "not-a-boolean";
        active.Sections[0].Pages[0].Blocks[0].PlainText = "Selected actual revision content";
        var trash = NotesDocument.Create("Recoverable board");
        trash.Metadata[BoardsWorkspaceService.ProductKey] = "boards";
        trash.Metadata[BoardsWorkspaceService.DeletedNotebookKey] = "TrUe";
        var write = NotesDocument.Create("A Write document");
        write.Metadata[BoardsWorkspaceService.ProductKey] = "write";
        var unspecified = NotesDocument.Create("An untagged note");
        foreach (var document in new[] { active, trash, write, unspecified })
            await rig.Verified.SaveAsync(document, "Existing actual document", rig.Token);
        var before = await rig.CaptureNotesFilesAsync();
        var request = await rig.Notes.Owner.PrepareOriginalLibraryReadAsync(active.Id, rig.Token);
        var (review, admission) = await rig.AcceptReadAsync(request);
        var snapshot = await rig.Notes.Owner.ReadOriginalLibraryWithHomeAsync(request, admission, rig.Verified, rig.Token);
        Assert.Equal(4, snapshot.Metadata.Count);
        Assert.Equal(4, snapshot.Documents.Count);
        Assert.Equal(active.Id, snapshot.SelectedDocument!.Id);
        var observed = Assert.Single(snapshot.Metadata, value => value.Summary.Id == active.Id);
        Assert.Equal(active.Version, observed.Summary.Version);
        Assert.Equal("BoArDs", observed.Product);
        Assert.False(observed.IsDeleted);
        Assert.True(Assert.Single(snapshot.Metadata, value => value.Summary.Id == trash.Id).IsDeleted);
        var source = rig.Notes.Owner.CreateOriginalBoardsSummarySource(request, snapshot, admission);
        var sameService = new BoardsWorkspaceService(rig.Verified, summarySource: source);
        var browserDefault = new BoardsWorkspaceService(rig.Verified);
        Assert.Equal((await browserDefault.ListNotebooksAsync(rig.Token)).Select(value => value.Id).Order(),
            (await sameService.ListNotebooksAsync(rig.Token)).Select(value => value.Id).Order());
        Assert.Equal(active.Id, Assert.Single(await sameService.ListNotebooksAsync(rig.Token)).Id);
        Assert.Equal((await browserDefault.ListDeletedNotebooksAsync(rig.Token)).Select(value => value.Id).Order(),
            (await sameService.ListDeletedNotebooksAsync(rig.Token)).Select(value => value.Id).Order());
        Assert.Equal(trash.Id, Assert.Single(await sameService.ListDeletedNotebooksAsync(rig.Token)).Id);
        Assert.Equal(1, await rig.CompletionCountAsync(review.Authorization.RequestId));
        await rig.AssertNotesFilesUnchangedAsync(before);
    }

    [LinuxNotesFact]
    public async Task Boards_summary_source_refuses_copied_snapshot_and_foreign_request_or_Home_owner()
    {
        await using var rig = await HomeRig.CreateAsync();
        var document = NotesDocument.Create("Original revision board");
        document.Metadata[BoardsWorkspaceService.ProductKey] = "boards";
        await rig.Verified.SaveAsync(document, "Existing actual board", rig.Token);
        var request = await rig.Notes.Owner.PrepareOriginalLibraryReadAsync(document.Id, rig.Token);
        var (_, admission) = await rig.AcceptReadAsync(request);
        var invented = new NotesOriginalLibrarySnapshot(request.StoreObservation.StoreIdentity, [], null);
        Assert.Throws<UnauthorizedAccessException>(() => rig.Notes.Owner.CreateOriginalBoardsSummarySource(request, invented, admission));
        var snapshot = await rig.Notes.Owner.ReadOriginalLibraryWithHomeAsync(request, admission, rig.Verified, rig.Token);
        Assert.Throws<UnauthorizedAccessException>(() => rig.Notes.Owner.CreateOriginalBoardsSummarySource(request, snapshot with { }, admission));
        var copied = new CopiedLibraryReadRequest(request.OperationId, request.DocumentId, request.StoreObservation, request.Arguments.Clone());
        Assert.Throws<UnauthorizedAccessException>(() => rig.Notes.Owner.CreateOriginalBoardsSummarySource(copied, snapshot, admission));
        await using var foreign = await HomeRig.CreateAsync();
        var otherRequest = await foreign.Notes.Owner.PrepareOriginalLibraryReadAsync(null, foreign.Token);
        var (_, otherHome) = await foreign.AcceptReadAsync(otherRequest);
        Assert.Throws<UnauthorizedAccessException>(() => rig.Notes.Owner.CreateOriginalBoardsSummarySource(request, snapshot, otherHome));
        Assert.Throws<UnauthorizedAccessException>(() => foreign.Notes.Owner.CreateOriginalBoardsSummarySource(request, snapshot, admission));
        Assert.Equal(document.Id, Assert.Single(await rig.Notes.Owner.CreateOriginalBoardsSummarySource(request, snapshot, admission)
            .ListAsync(false, rig.Token)).Id);
    }

    [LinuxNotesFact]
    public async Task Completed_snapshot_summaries_refuse_actual_Home_profile_ownership_or_caller_revocation()
    {
        foreach (var change in new[] { "profile", "ownership", "caller" })
        {
            await using var rig = await HomeRig.CreateAsync();
            var document = NotesDocument.Create("Home-protected board");
            document.Metadata[BoardsWorkspaceService.ProductKey] = "boards";
            await rig.Verified.SaveAsync(document, "Existing actual board", rig.Token);
            var request = await rig.Notes.Owner.PrepareOriginalLibraryReadAsync(null, rig.Token);
            var (_, admission) = await rig.AcceptReadAsync(request);
            var snapshot = await rig.Notes.Owner.ReadOriginalLibraryWithHomeAsync(request, admission, rig.Verified, rig.Token);
            var source = rig.Notes.Owner.CreateOriginalBoardsSummarySource(request, snapshot, admission);
            Assert.Equal(document.Id, Assert.Single(await source.ListAsync(false, rig.Token)).Id);
            var before = await rig.CaptureNotesFilesAsync();
            if (change == "caller")
            {
                var actor = await rig.Profiles.GetCurrentAsync(rig.Token);
                Assert.True((await rig.Permissions.BlockCallerAsync(actor!.ActorId, rig.Token)).Succeeded);
            }
            else
            {
                var state = await rig.Store.ReadAsync(rig.Token);
                Assert.True(state.IsSuccess);
                var record = Assert.Single(state.State!.Records, value => change == "profile"
                    ? value.RecordId == "home.local-profile" : value.RecordType == "home.local-store-ownership");
                var payload = change == "profile"
                    ? JsonSerializer.SerializeToElement(record.Payload.Deserialize<HomeLocalProfile>()! with { ProfileId = Guid.NewGuid() })
                    : JsonSerializer.SerializeToElement(record.Payload.Deserialize<HomeLocalStoreBinding>()! with { ProfileId = Guid.NewGuid().ToString("D") });
                Assert.True((await rig.Store.WriteAsync(record with { Revision = checked(record.Revision + 1), Payload = payload },
                    record.Revision, rig.Token)).IsSuccess);
            }
            var error = await Record.ExceptionAsync(() => source.ListAsync(false, rig.Token));
            Assert.NotNull(error);
            Assert.Contains(Leaves(error!), cause => cause is UnauthorizedAccessException);
            await rig.AssertNotesFilesUnchangedAsync(before);
        }
    }

    [LinuxNotesFact]
    public async Task Boards_summary_snapshot_refuses_actual_version_inventory_and_pending_state_changes_without_refreshing()
    {
        foreach (var change in new[] { "same-owner-version", "other-owner-version", "new-document", "dangling-pending" })
        {
            await using var rig = await HomeRig.CreateAsync();
            var document = NotesDocument.Create("A cached original board");
            document.Metadata[BoardsWorkspaceService.ProductKey] = "boards";
            var saved = await rig.Verified.SaveAsync(document, "Existing actual board", rig.Token);
            var request = await rig.Notes.Owner.PrepareOriginalLibraryReadAsync(null, rig.Token);
            var (_, admission) = await rig.AcceptReadAsync(request);
            var snapshot = await rig.Notes.Owner.ReadOriginalLibraryWithHomeAsync(request, admission, rig.Verified, rig.Token);
            var source = rig.Notes.Owner.CreateOriginalBoardsSummarySource(request, snapshot, admission);
            Assert.Equal(document.Id, Assert.Single(await source.ListAsync(false, rig.Token)).Id);
            var originalCurrent = await File.ReadAllBytesAsync(saved.CurrentPath, rig.Token);
            var manifestPath = Path.Combine(Path.GetDirectoryName(saved.CurrentPath)!, "current.integrity.json");
            var originalManifest = await File.ReadAllBytesAsync(manifestPath, rig.Token);
            if (change == "dangling-pending")
                File.CreateSymbolicLink(Path.Combine(Path.GetDirectoryName(saved.CurrentPath)!, "current.integrity.pending.json"),
                    Path.Combine(rig.Notes.DataDirectory, "never-created-summary-target.json"));
            else if (change == "new-document")
            {
                var other = NotesDocument.Create("New board absent from the authorised snapshot");
                other.Metadata[BoardsWorkspaceService.ProductKey] = "boards";
                await rig.Verified.SaveAsync(other, "Actual later library change", rig.Token);
            }
            else
            {
                document.Title = "Changed after the authorised snapshot";
                document.Metadata[BoardsWorkspaceService.DeletedNotebookKey] = "true";
                if (change == "same-owner-version")
                    await rig.Verified.SaveAsync(document, "Actual later version", rig.Token);
                else
                {
                    var owner = rig.Notes.NewOwner();
                    var verified = new VerifiedNotesRepository(owner, rig.Notes, rig.Notes.Diagnostics);
                    await verified.SaveAsync(document, "Actual competing owner version", rig.Token);
                }
            }
            var before = change == "dangling-pending" ? null : await rig.CaptureNotesFilesAsync();
            var error = await Record.ExceptionAsync(() => source.ListAsync(false, rig.Token));
            Assert.NotNull(error);
            Assert.Contains(Leaves(error!), cause => cause is IOException);
            Assert.Equal("A cached original board", Assert.Single(snapshot.Metadata).Summary.Title);
            // The dangling link itself is asserted without following its absent target.
            if (change == "dangling-pending")
            {
                var target = Path.Combine(rig.Notes.DataDirectory, "never-created-summary-target.json");
                Assert.False(File.Exists(target));
                Assert.Equal(target, new FileInfo(Path.Combine(Path.GetDirectoryName(saved.CurrentPath)!, "current.integrity.pending.json")).LinkTarget);
                Assert.Equal(originalCurrent, await File.ReadAllBytesAsync(saved.CurrentPath, rig.Token));
                Assert.Equal(originalManifest, await File.ReadAllBytesAsync(manifestPath, rig.Token));
            }
            else await rig.AssertNotesFilesUnchangedAsync(before!);
        }
    }

    [LinuxNotesFact]
    public async Task Already_read_Boards_metadata_is_immutable_and_cancellation_never_refreshes_or_consumes_it()
    {
        await using var rig = await HomeRig.CreateAsync();
        var document = NotesDocument.Create("Actual saved title");
        document.Metadata[BoardsWorkspaceService.ProductKey] = "boards";
        await rig.Verified.SaveAsync(document, "Existing actual board", rig.Token);
        var request = await rig.Notes.Owner.PrepareOriginalLibraryReadAsync(document.Id, rig.Token);
        var (_, admission) = await rig.AcceptReadAsync(request);
        var snapshot = await rig.Notes.Owner.ReadOriginalLibraryWithHomeAsync(request, admission, rig.Verified, rig.Token);
        var source = rig.Notes.Owner.CreateOriginalBoardsSummarySource(request, snapshot, admission);
        var before = await rig.CaptureNotesFilesAsync();
        snapshot.SelectedDocument!.Title = "Unsaved editor title";
        snapshot.SelectedDocument.Metadata[BoardsWorkspaceService.ProductKey] = "write";
        var editable = Assert.IsAssignableFrom<IList<NotesOriginalDocumentMetadata>>(snapshot.Metadata);
        Assert.Throws<NotSupportedException>(() => editable[0] = editable[0] with { Product = "write", IsDeleted = true });
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => source.ListAsync(false, cancelled.Token));
        Assert.Equal("Actual saved title", Assert.Single(await source.ListAsync(false, rig.Token)).Title);
        Assert.Empty(await source.ListAsync(true, rig.Token));
        await rig.AssertNotesFilesUnchangedAsync(before);
    }

    private sealed record CopiedSaveOutcome(INotesOriginalSaveIntent Intent, NotesOriginalSaveDisposition Disposition,
        long? CommittedVersion, string? CommittedSha256, string DiagnosticCode) : INotesOriginalSaveOutcome;

    private sealed record CopiedLibraryReadRequest(Guid OperationId, Guid? DocumentId,
        INotesOriginalStoreObservation StoreObservation, JsonElement Arguments) : INotesOriginalLibraryReadRequest;
    private sealed record CopiedLibraryReadOutcome(INotesOriginalLibraryReadRequest Request, bool Succeeded,
        int DocumentCount, bool DocumentWasRead, string? SnapshotSha256, string DiagnosticCode) : INotesOriginalLibraryReadOutcome;
    private sealed record NotesFileSnapshot(string[] Directories, IReadOnlyDictionary<string, byte[]> Files);

    // Real local Home composition: actual OS principal, durable Home state, genuine Notes
    // evidence/ownership, canonical resolver, permission catalogue and broker. No approval mock.
    private sealed class HomeRig : IAsyncDisposable
    {
        private readonly CancellationTokenSource _lifetime = new(TimeSpan.FromSeconds(30));
        internal Rig Notes { get; } = new();
        internal FileHomeCoreStateStore Store { get; }
        internal HomeLocalProfileIdentity Profiles { get; }
        internal HomePermissionTrustService Permissions { get; }
        internal HomeNotesResourceAdmissionSource Admission { get; }
        internal VerifiedNotesRepository Verified { get; }
        internal string SessionId { get; } = Guid.NewGuid().ToString("N");
        internal CancellationToken Token => _lifetime.Token;
        internal string Documents => Path.Combine(Notes.Notes, "Documents");
        private HomeRig()
        {
            Store = new(Path.Combine(Notes.DataDirectory, "home-core-state.json"));
            Profiles = new(Store, new OperatingSystemPrincipalSource());
            Permissions = new(Store, new HomeNotesActionPolicies().TryGet);
            var provider = new NotesLocalStoreEvidenceProvider(Notes.Owner);
            var ownership = new HomeLocalStoreOwnership(Store, Profiles, new HomeLocalStoreEvidenceRegistry([provider]), Permissions);
            var authority = new HomeResourceStoreOwnershipAuthority(ownership, Profiles);
            HomeNotesResourceAdmissionSource? actual = null;
            var resources = new ResourceAuthorizationService(Profiles, [new HomeNotesResourceResolver(() => actual!)]);
            var broker = new HomeResourceOperationBroker(resources, Permissions);
            Admission = actual = new(Notes.Owner, Store, Profiles, authority, resources, Permissions, broker);
            Verified = new(Notes.Owner, Notes, Notes.Diagnostics);
            Setup = new("notes", Notes.Owner, Profiles, ownership, provider);
        }
        private HomeLocalStoreSetupSession Setup { get; }
        internal static async Task<HomeRig> CreateAsync()
        {
            var rig = new HomeRig();
            try
            {
                await rig.Notes.Owner.InitialiseOriginalStoreIdentityAsync(rig.Token);
                var inspection = await rig.Setup.InspectAsync(rig.Token);
                Assert.False(inspection.IsOwned);
                Assert.True(inspection.CanBindEmpty);
                await rig.Setup.BindEmptyAsync(rig.Token);
                Assert.True((await rig.Setup.InspectAsync(rig.Token)).IsOwned);
                return rig;
            }
            catch { await rig.DisposeAsync(); throw; }
        }
        internal async Task<(HomeNotesSaveReview Review, HomeNotesSaveAdmission Admission)> AcceptAsync(INotesOriginalSaveIntent intent)
        {
            var review = await Admission.RequestSaveAsync(intent, SessionId, Token);
            Assert.Equal(HomePermissionRequestState.PendingApproval, review.Authorization.State);
            Assert.True((await Permissions.DecideAsync(review.Authorization.RequestId,
                HomeApprovalChoice.Accept, cancellationToken: Token)).Succeeded);
            var admission = await Admission.BeginSaveAsync(review, Token);
            Assert.NotNull(admission);
            return (review, admission!);
        }
        internal async Task<int> CompletionCountAsync(HomeNotesSaveReview review)
            => await CompletionCountAsync(review.Authorization.RequestId);
        internal async Task<int> CompletionCountAsync(string requestId)
        {
            var audit = await Permissions.GetSnapshotAsync(cancellationToken: Token);
            return audit.RecentAuditEvents.Count(value => value.RequestId == requestId &&
                value.Kind == HomePermissionAuditKind.ExecutionCompleted);
        }
        internal async Task<(HomeNotesLibraryReadReview Review, HomeNotesLibraryReadAdmission Admission)> AcceptReadAsync(
            INotesOriginalLibraryReadRequest request)
        {
            var review = await Admission.RequestLibrarySnapshotAsync(request, SessionId, Token);
            Assert.Equal(HomePermissionRequestState.PendingApproval, review.Authorization.State);
            Assert.True((await Permissions.DecideAsync(review.Authorization.RequestId,
                HomeApprovalChoice.Accept, cancellationToken: Token)).Succeeded);
            var admission = await Admission.BeginLibrarySnapshotAsync(review, Token);
            Assert.NotNull(admission);
            return (review, admission!);
        }
        internal async Task<NotesFileSnapshot> CaptureNotesFilesAsync()
        {
            var directories = Directory.EnumerateDirectories(Notes.Notes, "*", SearchOption.AllDirectories)
                .Select(path => Path.GetRelativePath(Notes.Notes, path)).Order(StringComparer.Ordinal).ToArray();
            var files = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            foreach (var path in Directory.EnumerateFiles(Notes.Notes, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
                files.Add(Path.GetRelativePath(Notes.Notes, path), await File.ReadAllBytesAsync(path, Token));
            return new(directories, files);
        }
        internal async Task AssertNotesFilesUnchangedAsync(NotesFileSnapshot before)
        {
            var after = await CaptureNotesFilesAsync();
            Assert.Equal(before.Directories, after.Directories);
            Assert.Equal(before.Files.Keys.Order(StringComparer.Ordinal), after.Files.Keys.Order(StringComparer.Ordinal));
            foreach (var item in before.Files) Assert.Equal(item.Value, after.Files[item.Key]);
        }
        public async ValueTask DisposeAsync()
        {
            _lifetime.Cancel();
            try { await Notes.DisposeAsync(); }
            finally { _lifetime.Dispose(); }
        }
    }

    private sealed record CopiedSaveIntent(Guid OperationId, Guid DocumentId, long ExpectedVersion,
        string ContentSha256, string Reason, INotesOriginalStoreObservation StoreObservation,
        System.Text.Json.JsonElement Arguments) : INotesOriginalSaveIntent;

    private sealed record CopiedObservation(ResourceStoreIdentity StoreIdentity, string Revision,
        bool IsEmpty) : INotesOriginalStoreObservation;

    private sealed class LinuxNotesFactAttribute : FactAttribute
    {
        public LinuxNotesFactAttribute()
        { if (!OperatingSystem.IsLinux()) Skip = "The original Notes identity backend requires Linux. Windows native identity/Notes acceptance is NOT RUN."; }
    }

    private sealed class MutablePaths(string root) : IAppPaths
    {
        public string DataDirectory { get; set; } = root;
        public string DatabasePath => Path.Combine(DataDirectory, "haven.db");
        public string BrowserProfileDirectory => Path.Combine(DataDirectory, "browser");
        public string AttachmentsDirectory => Path.Combine(DataDirectory, "attachments");
        public string LogsDirectory => Path.Combine(DataDirectory, "logs");
        public string LegacyStatePath => Path.Combine(DataDirectory, "legacy.json");
    }

    private sealed class Rig : IAppPaths, IAsyncDisposable
    {
        internal Rig()
        {
            DataDirectory = Path.Combine(Path.GetTempPath(), "9to1-notes-original-" + Guid.NewGuid().ToString("N"));
            if (OperatingSystem.IsLinux()) Directory.CreateDirectory(DataDirectory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            else Directory.CreateDirectory(DataDirectory);
            Diagnostics = new ProductionDiagnostics(this);
            Owner = NewOwner();
        }
        internal NotesRepository Owner { get; }
        internal ProductionDiagnostics Diagnostics { get; }
        internal string Notes => Path.Combine(DataDirectory, "Notes");
        internal string Identity => Path.Combine(Notes, ".store-identity.json");
        internal NotesRepository NewOwner() => new(this, new NotesDocumentValidator(), Diagnostics);
        public string DataDirectory { get; }
        public string DatabasePath => Path.Combine(DataDirectory, "haven.db");
        public string BrowserProfileDirectory => Path.Combine(DataDirectory, "browser");
        public string AttachmentsDirectory => Path.Combine(DataDirectory, "attachments");
        public string LogsDirectory => Path.Combine(DataDirectory, "logs");
        public string LegacyStatePath => Path.Combine(DataDirectory, "legacy.json");
        public async ValueTask DisposeAsync()
        {
            await Diagnostics.DisposeAsync();
            try { Directory.Delete(DataDirectory, recursive: true); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
        }
    }

    [DllImport("libc", EntryPoint = "mkfifo", SetLastError = true, CharSet = CharSet.Ansi)]
    private static extern int NativeMakeFifo(string path, uint mode);
    [DllImport("libc", EntryPoint = "link", SetLastError = true, CharSet = CharSet.Ansi)]
    private static extern int NativeLink(string source, string destination);
}
