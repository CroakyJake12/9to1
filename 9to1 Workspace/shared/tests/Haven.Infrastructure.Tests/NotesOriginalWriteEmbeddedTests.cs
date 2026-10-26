using Haven.Core;
using Haven.Infrastructure;
using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;
using Xunit;

namespace Haven.Infrastructure.Tests;

public sealed partial class NotesOriginalStoreTests
{
    [LinuxNotesFact]
    public async Task Write_body_read_retains_its_reviewed_version_wrapper_and_original_selected_request()
    {
        await using var home = await HomeRig.CreateAsync();
        var selected = NotesDocument.Create("Reviewed Write body");
        selected.Version = (await home.Verified.SaveAsync(selected, "Create selected test document", home.Token)).Version;
        var other = NotesDocument.Create("Unselected Write body");
        other.Version = (await home.Verified.SaveAsync(other, "Create unselected test document", home.Token)).Version;
        var before = await home.CaptureNotesFilesAsync();
        var request = await home.Notes.Owner.PrepareOriginalLibraryReadAsync(selected.Id, home.Token);
        var review = await home.Admission.RequestWriteDocumentReadAsync(request, selected.Version, home.SessionId, home.Token);
        Assert.Equal(HomePermissionRequestState.PendingApproval, review.Authorization.State);
        Assert.True((await home.Permissions.DecideAsync(review.Authorization.RequestId,
            HomeApprovalChoice.Accept, cancellationToken: home.Token)).Succeeded);
        var admission = await home.Admission.BeginLibrarySnapshotAsync(review, home.Token);
        Assert.NotNull(admission);

        // Approval of the private wrapper never authorises a different owner-issued selector.
        var differentRequest = await home.Notes.Owner.PrepareOriginalLibraryReadAsync(other.Id, home.Token);
        Assert.False(admission!.IsBoundToOriginalNotes(home.Notes.Owner, differentRequest));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => home.Notes.Owner.ReadOriginalLibraryWithHomeAsync(
            differentRequest, admission, home.Verified, home.Token));

        var snapshot = await home.Notes.Owner.ReadOriginalLibraryWithHomeAsync(request, admission, home.Verified, home.Token);
        Assert.NotNull(snapshot.SelectedDocument);
        Assert.Equal(selected.Id, snapshot.SelectedDocument!.Id);
        Assert.Equal(selected.Version, snapshot.SelectedDocument.Version);
        Assert.Null(snapshot.AuditWarning);
        Assert.Equal(HomePermissionRequestState.Succeeded,
            (await home.Admission.GetLibrarySnapshotAuthorizationAsync(review, home.Token)).State);
        await home.AssertNotesFilesUnchangedAsync(before);
    }

    [LinuxNotesFact]
    public async Task Embedded_Write_catalogue_review_returns_metadata_only_without_reading_a_document_body()
    {
        await using var home = await HomeRig.CreateAsync();
        var document = NotesDocument.Create("Write catalogue entry");
        document.Version = (await home.Verified.SaveAsync(document, "Create test document", home.Token)).Version;
        var request = await home.Notes.Owner.PrepareOriginalLibraryReadAsync(null, home.Token);
        var review = await home.Admission.RequestWriteLibrarySnapshotAsync(request, home.SessionId, home.Token);
        Assert.Equal(HomePermissionRequestState.PendingApproval, review.Authorization.State);
        Assert.True((await home.Permissions.DecideAsync(review.Authorization.RequestId,
            HomeApprovalChoice.Accept, cancellationToken: home.Token)).Succeeded);
        var admission = await home.Admission.BeginLibrarySnapshotAsync(review, home.Token);
        Assert.NotNull(admission);
        var snapshot = await home.Notes.Owner.ReadOriginalLibraryWithHomeAsync(request, admission!, home.Verified, home.Token);
        Assert.Null(snapshot.SelectedDocument);
        Assert.Single(snapshot.Metadata, row => row.Summary.Id == document.Id && row.Summary.Version == document.Version);
        // Outcome is private to the real owner. Home rejects a terminal audit that
        // reports DocumentWasRead for this metadata-only request; inspect that public receipt.
        Assert.Null(snapshot.AuditWarning);
        Assert.Equal(HomePermissionRequestState.Succeeded,
            (await home.Admission.GetLibrarySnapshotAuthorizationAsync(review, home.Token)).State);
    }

    [LinuxNotesFact]
    public async Task Embedded_Write_uses_reviewed_metadata_and_exact_body_read_then_repeats_save_from_receipt_revision()
    {
        await using var home = await HomeRig.CreateAsync();
        var initial = NotesDocument.Create("Original Write document");
        initial.Version = (await home.Verified.SaveAsync(initial, "Create test document", home.Token)).Version;
        var retained = new List<Task>();
        void Scope(Action body) => body();
        void Retain(Task task) => retained.Add(task);

        async Task AcceptRead(HomeNotesLibraryReadReview review, CancellationToken token)
        {
            var decision = await home.Permissions.DecideAsync(review.Authorization.RequestId,
                HomeApprovalChoice.Accept, cancellationToken: token);
            Assert.True(decision.Succeeded);
        }
        async Task AcceptSave(HomeNotesSaveReview review, CancellationToken token)
        {
            var decision = await home.Permissions.DecideAsync(review.Authorization.RequestId,
                HomeApprovalChoice.Accept, cancellationToken: token);
            Assert.True(decision.Succeeded);
        }

        await using var repository = NotesOriginalWriteWorkspaceRepository.BindOriginal(
            home.Notes.Owner, home.Verified, home.Admission, initial.Id, initial.Version,
            "embedded-write-test", AcceptRead, AcceptSave, Scope, Retain, home.Token);
        var summaries = await repository.ListAsync(home.Token);
        Assert.Collection(summaries, row =>
        {
            Assert.Equal(initial.Id, row.Id);
            Assert.Equal(initial.Version, row.Version);
        });
        var document = await repository.LoadAsync(initial.Id, home.Token);
        Assert.NotNull(document);
        Assert.Equal(initial.Id, document!.Id);
        Assert.Equal(initial.Version, document.Version);

        document.Title = "First reviewed Write save";
        var first = await repository.SaveAsync(document, "Embedded Write edit", home.Token);
        Assert.Equal(initial.Id, first.DocumentId);
        Assert.Equal(initial.Version + 1, first.Version);

        document.Title = "Second reviewed Write save";
        var second = await repository.SaveAsync(document, "Embedded Write second edit", home.Token);
        Assert.Equal(initial.Id, second.DocumentId);
        Assert.Equal(first.Version + 1, second.Version);
        Assert.Equal("Second reviewed Write save", (await home.Notes.Owner.LoadAsync(initial.Id, home.Token))!.Title);
        Assert.All(retained, task => Assert.True(task.IsCompleted));
    }

    [LinuxNotesFact]
    public async Task Embedded_Write_refuses_unapproved_save_and_read_only_mode_never_publishes()
    {
        await using var home = await HomeRig.CreateAsync();
        var initial = NotesDocument.Create("Read-only Write document");
        initial.Version = (await home.Verified.SaveAsync(initial, "Create test document", home.Token)).Version;
        void Scope(Action body) => body();
        void Retain(Task _) { }

        async Task AcceptRead(HomeNotesLibraryReadReview review, CancellationToken token)
        {
            var decision = await home.Permissions.DecideAsync(review.Authorization.RequestId,
                HomeApprovalChoice.Accept, cancellationToken: token);
            Assert.True(decision.Succeeded);
        }
        Task DenySave(HomeNotesSaveReview review, CancellationToken token) => Task.CompletedTask;

        await using var repository = NotesOriginalWriteWorkspaceRepository.BindOriginal(
            home.Notes.Owner, home.Verified, home.Admission, initial.Id, initial.Version,
            "embedded-write-read-only-test", AcceptRead, DenySave, Scope, Retain, home.Token, allowWrites: false);
        var document = await repository.LoadAsync(initial.Id, home.Token);
        Assert.NotNull(document);
        document!.Title = "Must not publish";
        var error = await Record.ExceptionAsync(() => repository.SaveAsync(document, "Unexpected view write", home.Token));
        Assert.IsType<UnauthorizedAccessException>(error);
        Assert.Equal("Read-only Write document", (await home.Notes.Owner.LoadAsync(initial.Id, home.Token))!.Title);
    }
}
