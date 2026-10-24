using System.Globalization;
using Avalonia.Threading;
using Haven.Application;
using Haven.Core;
using Haven.Infrastructure;
using HavenOS.Apps.Spaces.Builder;
using HavenOS.Home.Core;
using HavenOS.Home.NativeUI;
using HavenOS.Home.PermissionsTrustNotifications;
using Microsoft.Extensions.DependencyInjection;

namespace Haven.Desktop.Services;

internal sealed partial class OriginalSpaceRequestPolicySource
{
    private sealed record BoardsChoiceCatalogue(Owners Owners, Authority Authority, Guid SpaceId,
        long SpaceRevision, ContextCustody Custody, IBoardsNotebookSummarySource Witness);
    private sealed record OriginalBoardsChoice(OriginalSpaceRequestPolicySource Issuer,
        BoardsChoiceCatalogue Catalogue, NotesDocumentSummary Summary) : ISpaceBuilderOriginalBoardsSourceChoice;

    internal bool HasOriginalContextRegistry(SpaceRegistry registry) =>
        ReferenceEquals(_services.GetRequiredService<SpaceRegistry>(), registry);

    internal Task<IReadOnlyList<SpaceBuilderBoardsSourceOption>> GetOriginalBoardsContextChoicesAsync(
        SpaceRegistry registry, Guid spaceId, long expectedRevision, Action<Action> acquire, CancellationToken token) =>
        Run(acquire, async originals =>
        {
            var owners = originals.Invoke(CurrentOwners);
            if (!ReferenceEquals(owners.Spaces, registry)) throw Block("SPACE_CHOOSER_REGISTRY_CHANGED", "The chooser belongs to another Space registry.");
            var authority = await ReadAuthorityAsync(owners, originals, token).ConfigureAwait(false);
            await DemandChooserSpaceAsync(owners, spaceId, expectedRevision, originals, token).ConfigureAwait(false);
            lock (_contextFailureGate)
                if (_contextFailures.Count >= 64)
                    throw Block("SPACE_CHOOSER_RECOVERY_REQUIRED", "Retained original source failures need recovery before another protected read.");
            var custody = new ContextCustody();
            try { return await custody.WithinAsync(originals, async () =>
            {
                var (witness, rows) = await ReadOriginalBoardsChoicesAsync(owners, authority, spaceId, custody, originals, token).ConfigureAwait(false);
                await DemandChooserSpaceAsync(owners, spaceId, expectedRevision, originals, token).ConfigureAwait(false);
                await DemandAuthorityAsync(owners, authority, originals, token).ConfigureAwait(false);
                var catalogue = new BoardsChoiceCatalogue(owners, authority, spaceId, expectedRevision, custody, witness);
                // A bounded view, no copied store or invented entity. The actual read witness
                // still revalidates the entire original Notes snapshot at selection time.
                return (IReadOnlyList<SpaceBuilderBoardsSourceOption>)Array.AsReadOnly(rows
                    .OrderByDescending(row => row.UpdatedAt).ThenBy(row => row.Id).Take(128)
                    .Select(row => new SpaceBuilderBoardsSourceOption(row.Id.ToString("D"), BoundLabel(row.Title),
                        row.Version.ToString(CultureInfo.InvariantCulture), new OriginalBoardsChoice(this, catalogue, row))).ToArray());
            }, token).ConfigureAwait(false); }
            catch (Exception failure)
            {
                lock (_contextFailureGate) _contextFailures.Add((custody, failure));
                throw;
            }
        });

    internal Task<SpaceContextReference> ResolveOriginalBoardsContextChoiceAsync(
        ISpaceBuilderOriginalBoardsSourceChoice choice, SpaceRegistry registry, Guid spaceId, long expectedRevision,
        Action<Action> acquire, CancellationToken token) => Run(acquire, async originals =>
        {
            if (choice is not OriginalBoardsChoice actual || !ReferenceEquals(actual.Issuer, this) ||
                !ReferenceEquals(actual.Catalogue.Owners.Spaces, registry) || actual.Catalogue.SpaceId != spaceId ||
                actual.Catalogue.SpaceRevision != expectedRevision)
                throw Block("SPACE_CHOOSER_INPUT_NOT_ORIGINAL", "Select the original current Home/Notes source option for this Space revision.");
            var capture = actual.Catalogue;
            await DemandAuthorityAsync(capture.Owners, capture.Authority, originals, token).ConfigureAwait(false);
            await DemandChooserSpaceAsync(capture.Owners, spaceId, expectedRevision, originals, token).ConfigureAwait(false);
            return await capture.Custody.WithinAsync(originals, async () =>
            {
                var rows = await originals.AwaitAsync(originals.Invoke(() => capture.Witness.ListAsync(false, token))).ConfigureAwait(false);
                if (!rows.Any(row => row == actual.Summary))
                    throw Block("SPACE_CHOOSER_NOTEBOOK_CHANGED", "This original notebook/version changed. Refresh the source list.");
                await DemandChooserSpaceAsync(capture.Owners, spaceId, expectedRevision, originals, token).ConfigureAwait(false);
                await DemandAuthorityAsync(capture.Owners, capture.Authority, originals, token).ConfigureAwait(false);
                // A read REFERENCE only. Actual source body access needs its own reviewed
                // Notes request at model dispatch; the metadata snapshot grants no body IO.
                return new SpaceContextReference(Guid.NewGuid(), SpaceContextReferenceKind.BoardPage, "boards",
                    actual.Summary.Id.ToString("D"), actual.Summary.Version.ToString(CultureInfo.InvariantCulture),
                    SpaceContextPermission.Read, SpaceContextIndexState.NotRequired, true, DateTimeOffset.UtcNow);
            }, token).ConfigureAwait(false);
        });

    private static async Task DemandChooserSpaceAsync(Owners owners, Guid spaceId, long revision,
        CloudflareOriginalTaskLedger originals, CancellationToken token)
    {
        var space = await originals.AwaitAsync(originals.Invoke(() => owners.Spaces.ReadExistingAsync(spaceId, token))).ConfigureAwait(false);
        if (space is null || space.IsArchived || space.IsBuiltIn || space.Origin != SpaceOrigin.UserCreated || space.Revision != revision)
            throw Block("SPACE_CHOOSER_SPACE_CHANGED", "The current editable user Space/revision is unavailable. The draft is preserved.");
    }

    private async Task<(IBoardsNotebookSummarySource Witness, IReadOnlyList<NotesDocumentSummary> Rows)> ReadOriginalBoardsChoicesAsync(
        Owners owners, Authority authority, Guid spaceId, ContextCustody custody, CloudflareOriginalTaskLedger originals, CancellationToken token)
    {
        var notes = originals.Invoke(() => _services.GetRequiredService<NotesRepository>());
        var verified = originals.Invoke(() => _services.GetRequiredService<VerifiedNotesRepository>());
        var admission = originals.Invoke(() => _services.GetRequiredService<HomeNotesResourceAdmissionSource>());
        var approvals = originals.Invoke(() => _services.GetRequiredService<HomeNativeApprovalWindowOwner>());
        void DemandPairing()
        {
            token.ThrowIfCancellationRequested();
            if (!ReferenceEquals(_services.GetRequiredService<NotesRepository>(), notes) ||
                !ReferenceEquals(_services.GetRequiredService<HomeNotesResourceAdmissionSource>(), admission) ||
                !ReferenceEquals(_services.GetRequiredService<HomeNativeApprovalWindowOwner>(), approvals) ||
                !ReferenceEquals(owners.Home.Services.GetService(typeof(NotesRepository)), notes) ||
                !ReferenceEquals(owners.Home.Services.GetService(typeof(HomeNotesResourceAdmissionSource)), admission) ||
                !admission.IsBoundToOriginalNotes(notes) || !approvals.IsBoundToOriginalComposition(owners.Home))
                throw Block("SPACE_CHOOSER_OWNER_CHANGED", "The original Notes/Home chooser owners changed.");
        }
        originals.Invoke(() => { DemandPairing(); return true; });
        var inspection = await originals.AwaitAsync(originals.Invoke(() => notes.InspectOriginalStoreSetupWithinOriginalSourceAsync(custody.Own, custody.Retain, token).AsTask())).ConfigureAwait(false);
        if (!notes.IsIssuedOriginalSetupInspection(inspection) || inspection.State != NotesOriginalStoreSetupState.Available)
            throw Block("SPACE_CHOOSER_BOARDS_SETUP_REQUIRED", "Open Boards and complete the actual existing-store setup before choosing a notebook source.");
        var request = await originals.AwaitAsync(originals.Invoke(() => notes.PrepareOriginalLibraryReadAsync(null, token).AsTask())).ConfigureAwait(false);
        custody.Keep(request);
        var review = await originals.AwaitAsync(originals.Invoke(() => admission.RequestLibrarySnapshotWithinOriginalSourceAsync(request,
            "space-builder-source:" + spaceId.ToString("D"), custody.Own, custody.Retain, token))).ConfigureAwait(false);
        custody.Keep(review);
        var authorization = await originals.AwaitAsync(originals.Invoke(() => admission.GetLibrarySnapshotAuthorizationWithinOriginalSourceAsync(
            review, custody.Own, custody.Retain, token))).ConfigureAwait(false);
        if (!authorization.IsAllowed || authorization.State != HomePermissionRequestState.Approved)
        {
            if (authorization.State != HomePermissionRequestState.PendingApproval)
                throw Block("SPACE_CHOOSER_PERMISSION_REQUIRED", authorization.Message);
            var dispatch = originals.Invoke(() => Dispatcher.UIThread.InvokeAsync<Task<HomeNativeApprovalDecisionObservation>>(() =>
                originals.Invoke(() => { DemandPairing(); return approvals.OpenAndObserveDecisionOriginalAsync(authorization.RequestId, token); })).GetTask());
            var decisionTask = await originals.AwaitAsync(dispatch).ConfigureAwait(false);
            var decision = await originals.AwaitAsync(decisionTask).ConfigureAwait(false);
            if (decision.RequestId != authorization.RequestId || !decision.DecisionRecorded || decision.State != HomePermissionRequestState.Approved)
                throw Block("SPACE_CHOOSER_PERMISSION_REQUIRED", decision.Message);
        }
        await DemandAuthorityAsync(owners, authority, originals, token).ConfigureAwait(false);
        originals.Invoke(() => { DemandPairing(); return true; });
        var actualAdmission = await originals.AwaitAsync(originals.Invoke(() => admission.BeginLibrarySnapshotWithinOriginalSourceAsync(
            review, custody.Own, custody.Retain, token))).ConfigureAwait(false)
            ?? throw Block("SPACE_CHOOSER_PERMISSION_REQUIRED", "The actual reviewed Notes metadata request is not approved.");
        custody.Keep(actualAdmission);
        var read = await originals.AwaitAsync(originals.Invoke(() => notes.ReadOriginalLibraryWithHomeAsync(request, actualAdmission, verified, token))).ConfigureAwait(false);
        custody.Keep(read);
        if (read.AuditWarning is not null) throw Block("SPACE_CHOOSER_AUDIT_PENDING", "The actual Notes snapshot audit needs recovery. No source was added.");
        if (read.SelectedDocument is not null)
            throw Block("SPACE_CHOOSER_UNEXPECTED_BODY", "The metadata-only chooser received an unexpected selected document body.");
        var witness = originals.Invoke(() => notes.CreateOriginalBoardsSummarySource(request, read, actualAdmission));
        var rows = await originals.AwaitAsync(originals.Invoke(() => witness.ListAsync(false, token))).ConfigureAwait(false);
        if (rows.Any(row => row.Id == Guid.Empty || row.Version < 1 || string.IsNullOrWhiteSpace(row.Title)) ||
            rows.Select(row => row.Id).Distinct().Count() != rows.Count)
            throw Block("SPACE_CHOOSER_NOTEBOOK_IDENTITY_INVALID", "The original Notes owner returned invalid notebook metadata.");
        await DemandAuthorityAsync(owners, authority, originals, token).ConfigureAwait(false);
        return (witness, rows);
    }
}
