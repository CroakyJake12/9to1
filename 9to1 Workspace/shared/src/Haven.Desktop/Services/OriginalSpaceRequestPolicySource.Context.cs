using System.Globalization;
using System.Text;
using Avalonia.Threading;
using Haven.Application;
using Haven.Core;
using Haven.Infrastructure;
using HavenOS.Home.Core;
using HavenOS.Home.NativeUI;
using HavenOS.Home.PermissionsTrustNotifications;
using HavenOS.Files;
using HavenOS.Files.NativeHost;
using Microsoft.Extensions.DependencyInjection;

namespace Haven.Desktop.Services;

internal sealed partial class OriginalSpaceRequestPolicySource
{
    // Failed original requests/reviews/admissions remain retained for explicit recovery,
    // including an audit failure after a successful body read. No read or action is replayed.
    private readonly object _contextFailureGate = new();
    private readonly List<(ContextCustody Custody, Exception Failure)> _contextFailures = [];

    // Stable callbacks outlive the initial read ledger. Every later revalidation borrows the
    // SAME current request original, serialized here; a settled capture can never acquire IO.
    private sealed class ContextCustody
    {
        private readonly List<object> _receipts = [];
        internal void Keep(object actual) => _receipts.Add(actual);
        private readonly SemaphoreSlim _gate = new(1, 1);
        private CloudflareOriginalTaskLedger? _current;
        private readonly List<(IBoardsNotebookSummarySource Witness, Guid Id, long Version)> _witnesses = [];
        private readonly List<(FilesNativeBrowserService Browser, FilesOriginalTextPreview Preview)> _filePreviews = [];
        internal bool IsBorrowed => _current is not null;
        internal void Own(Action body) => (_current ?? throw new InvalidOperationException("No original Space context operation is active."))
            .Invoke(() => { body(); return true; });
        internal void Retain(Task task) => (_current ?? throw new InvalidOperationException("No original Space context operation is active.")).Track(task);
        internal void Add(IBoardsNotebookSummarySource witness, Guid id, long version) => _witnesses.Add((witness, id, version));
        internal void Add(FilesNativeBrowserService browser, FilesOriginalTextPreview preview) => _filePreviews.Add((browser, preview));
        internal async Task<T> WithinAsync<T>(CloudflareOriginalTaskLedger originals, Func<Task<T>> body, CancellationToken token)
        {
            await originals.AwaitAsync(originals.Invoke(() => _gate.WaitAsync(token))).ConfigureAwait(false);
            try
            {
                if (_current is not null) throw new InvalidOperationException("The original context callback is already borrowed.");
                _current = originals;
                return await body().ConfigureAwait(false);
            }
            finally { _current = null; _gate.Release(); }
        }
        internal Task RevalidateAsync(CloudflareOriginalTaskLedger originals, CancellationToken token) =>
            WithinAsync(originals, async () =>
            {
                foreach (var row in _witnesses)
                {
                    // This is NOT a body read from metadata. It revalidates the SAME Notes
                    // physical body-read witness and the actual completed Home admission.
                    var current = await originals.AwaitAsync(originals.Invoke(() => row.Witness.ListAsync(false, token))).ConfigureAwait(false);
                    if (!current.Any(value => value.Id == row.Id && value.Version == row.Version))
                        throw Block("SPACE_CONTEXT_SOURCE_CHANGED", "A reviewed Boards source changed or is no longer available.");
                }
                foreach (var row in _filePreviews)
                    await originals.AwaitAsync(originals.Invoke(() => row.Browser.RevalidateOriginalTextPreviewWithinSourceAsync(
                        row.Preview, () => IsBorrowed, Own, Retain, token))).ConfigureAwait(false);
                return true;
            }, token);
    }

    private async Task<(OriginalSpacePolicySnapshot Snapshot, ContextCustody? Custody)> CaptureContextAsync(
        Owners owners, Authority authority, Conversation conversation, OriginalSpacePolicySnapshot policy,
        OriginalSpaceContextBudget budget, CloudflareOriginalTaskLedger originals, CancellationToken token)
    {
        if (!SpaceRequestPolicyConstraints.RequiresTypedContext(policy.Policy)) return (policy, null);
        var current = await originals.AwaitAsync(originals.Invoke(() => owners.Spaces.ReadExistingAsync(policy.Lineage.SpaceId, token))).ConfigureAwait(false);
        if (current is null || current.IsArchived || current.Revision != policy.Lineage.Ancestors[^1].Revision)
            throw Block("SPACE_CONTEXT_SPACE_CHANGED", "The configured Space changed before context capture.");
        if (current.Files.Count != 0)
            throw Block("SPACE_CONTEXT_LEGACY_FILE_CAPTURE_UNAVAILABLE", "This restricted Space still has legacy file paths. They remain saved but cannot be treated as reviewed source bodies; use canonical owning-app sources instead.");
        lock (_contextFailureGate)
            if (_contextFailures.Count >= 64)
                throw Block("SPACE_CONTEXT_RECOVERY_REQUIRED", "Retained original context failures need recovery before another protected read.");
        var configuration = BuildCanonicalConfiguration(current);
        // The configured retrieval limit and the request's measured byte allocation are
        // separate. All selection decisions retain their original stable source identity.
        var collection = new OriginalSpaceContextCollection(current.Id, configuration, policy.Policy.Sources,
            policy.Policy.Context.MaximumRetrievedItems, budget);
        var custody = new ContextCustody();
        try { return await custody.WithinAsync(originals, async () =>
        {
            foreach (var reference in collection.RequestedSources)
            {
                await DemandAuthorityAsync(owners, authority, originals, token).ConfigureAwait(false);
                if (reference.Kind == SpaceContextReferenceKind.File && reference.OwnerAppId == "files")
                {
                    var item = await ReadFilesContextAsync(owners, authority, reference, collection, custody, originals, token).ConfigureAwait(false);
                    if (item is not null && collection.TryInclude(item.Item)) custody.Add(item.Browser, item.Preview);
                    continue;
                }
                if (reference.Kind != SpaceContextReferenceKind.BoardPage || reference.OwnerAppId != "boards" ||
                    !Guid.TryParseExact(reference.CanonicalEntityId, "D", out var id) || id == Guid.Empty ||
                    reference.CanonicalEntityId != id.ToString("D") ||
                    reference.Permission is not (SpaceContextPermission.Read or SpaceContextPermission.ReadWrite))
                {
                    // No IO or metadata-as-content substitute for an unsupported owner.
                    // The typed omission remains inspectable and the model is told that
                    // the source set is incomplete. Permission/authority read failures
                    // below still block the whole dispatch rather than becoming omissions.
                    collection.OmitUnavailable(reference);
                    continue;
                }
                var item = await ReadBoardsContextAsync(owners, authority, conversation, reference, id, custody, originals, token).ConfigureAwait(false);
                collection.TryInclude(item); // Whole body or an explicit byte-budget omission; never an unlabeled excerpt.
            }
            // Current selection has no qualified explicit input on this route yet. In
            // particular, DO NOT query the foreground OS while the composer has focus.
            var capture = collection.Complete();
            var snapshot = new OriginalSpacePolicySnapshot(policy.Policy, policy.Lineage, capture, policy.UserMessageGraph);
            capture.ValidateAgainst(snapshot);
            await DemandAuthorityAsync(owners, authority, originals, token).ConfigureAwait(false);
            return (snapshot, (ContextCustody?)custody);
        }, token).ConfigureAwait(false); }
        catch (Exception failure)
        {
            lock (_contextFailureGate) _contextFailures.Add((custody, failure));
            throw; // Preserve the exact original failure and its actual objects; never fake success.
        }
    }

    private sealed record FilesContextPreview(FilesNativeBrowserService Browser,
        FilesOriginalTextPreview Preview, OriginalSpaceContextItem Item);

    private async Task<FilesContextPreview?> ReadFilesContextAsync(Owners owners, Authority authority,
        SpaceContextReference reference, OriginalSpaceContextCollection collection, ContextCustody custody,
        CloudflareOriginalTaskLedger originals, CancellationToken token)
    {
        var parts = reference.CanonicalEntityId.Split('/', StringSplitOptions.None);
        if (parts.Length != 2 || !Guid.TryParseExact(parts[0], "D", out var storeId) || storeId == Guid.Empty ||
            parts[0] != storeId.ToString("D") || !Guid.TryParseExact(parts[1], "D", out var fileId) || fileId == Guid.Empty ||
            parts[1] != fileId.ToString("D") || reference.HostedFileId != fileId ||
            reference.ParentFolderId is not { } parentFolderId || parentFolderId == Guid.Empty ||
            !Guid.TryParseExact(reference.RevisionToken, "D", out var revisionId) || revisionId == Guid.Empty ||
            reference.Permission != SpaceContextPermission.Read || reference.IndexState != SpaceContextIndexState.NotRequired)
        {
            collection.OmitUnavailable(reference);
            return null;
        }

        var browser = originals.Invoke(() => GetOriginalFilesBrowser(owners));
        if (!browser.HasOriginalTextPreviewOwner)
        {
            collection.OmitUnavailable(reference);
            return null;
        }
        var availability = await originals.AwaitAsync(originals.Invoke(() => browser.ObserveOriginalHomeLibraryAvailabilityWithinSourceAsync(
            authority.Actor, () => custody.IsBorrowed, custody.Own, custody.Retain, token))).ConfigureAwait(false);
        if (!browser.IsIssuedOriginalHomeLibraryAvailability(availability, authority.Actor) ||
            !availability.IsAvailable || availability.StoreId != storeId)
            throw Block("SPACE_CONTEXT_FILES_UNAVAILABLE", availability.Message);

        var selected = await originals.AwaitAsync(originals.Invoke(() => browser.ReadOriginalTextPreviewSelectionWithinSourceAsync(
            authority.Actor, storeId, new HostedItemId(parentFolderId), new HostedItemId(fileId),
            new FilesRevisionId(revisionId), () => custody.IsBorrowed, custody.Own, custody.Retain, token))).ConfigureAwait(false);
        var row = selected.Row;
        if (selected.Page.StoreID != storeId || selected.Page.ParentID != parentFolderId || selected.Page.IsTrash ||
            row.Id.Value != fileId || row.ParentId?.Value != parentFolderId || row.Kind != HostedItemKind.File ||
            row.CurrentRevisionId?.Value.Value != revisionId)
            throw Block("SPACE_CONTEXT_FILES_CHANGED", "The current Files row no longer matches the attached Space reference.");

        var observedDigest = NormalizeContextFilesDigest(row.ContentHash);
        if (reference.ContentSha256 is { } expectedDigest &&
            (observedDigest is null || !string.Equals(expectedDigest, observedDigest, StringComparison.Ordinal)))
            throw Block("SPACE_CONTEXT_FILES_CHANGED", "The current Files content digest no longer matches the attached Space reference.");
        var preview = await originals.AwaitAsync(originals.Invoke(() => browser.ReadOriginalTextPreviewWithinSourceAsync(
            selected.Page, row, authority.Actor, () => custody.IsBorrowed, custody.Own, custody.Retain, token))).ConfigureAwait(false);
        if (!preview.Succeeded)
        {
            collection.OmitUnavailable(reference);
            return null;
        }
        if (preview.FileId != fileId || !Guid.TryParseExact(preview.Revision, "N", out var previewRevision) ||
            previewRevision != revisionId || preview.Name != row.Name)
            throw Block("SPACE_CONTEXT_FILES_CHANGED", "The Files text preview does not match the selected canonical revision.");

        var item = new OriginalSpaceContextItem(reference.ContextId, OriginalSpaceContextItemKind.ConfiguredSource,
            reference.OwnerAppId, reference.CanonicalEntityId, reference.RevisionToken!, BoundLabel(row.Name), preview.Text);
        return new(browser, preview, item);
    }

    private static string? NormalizeContextFilesDigest(string? value)
    {
        if (value is null) return null;
        if (value.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase)) value = value[7..];
        return value.Length == 64 && value.All(Uri.IsHexDigit) ? "sha256:" + value.ToLowerInvariant() : null;
    }

    private async Task<OriginalSpaceContextItem> ReadBoardsContextAsync(Owners owners, Authority authority,
        Conversation conversation, SpaceContextReference reference, Guid documentId, ContextCustody custody,
        CloudflareOriginalTaskLedger originals, CancellationToken token)
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
                throw Block("SPACE_CONTEXT_OWNER_CHANGED", "The actual Notes, Home admission or review owner changed.");
        }
        originals.Invoke(() => { DemandPairing(); return true; });
        var inspection = await originals.AwaitAsync(originals.Invoke(() => notes.InspectOriginalStoreSetupWithinOriginalSourceAsync(custody.Own, custody.Retain, token).AsTask())).ConfigureAwait(false);
        if (!notes.IsIssuedOriginalSetupInspection(inspection) || inspection.State != NotesOriginalStoreSetupState.Available)
            throw Block("SPACE_CONTEXT_BOARDS_SETUP_REQUIRED", "Open Boards and complete the real existing-store setup before using it as a Space source.");
        var request = await originals.AwaitAsync(originals.Invoke(() => notes.PrepareOriginalLibraryReadAsync(documentId, token).AsTask())).ConfigureAwait(false);
        custody.Keep(request);
        var review = await originals.AwaitAsync(originals.Invoke(() => admission.RequestLibrarySnapshotWithinOriginalSourceAsync(request,
            "space-context:" + conversation.Id.ToString("D"), custody.Own, custody.Retain, token))).ConfigureAwait(false);
        custody.Keep(review);
        var authorization = await originals.AwaitAsync(originals.Invoke(() => admission.GetLibrarySnapshotAuthorizationWithinOriginalSourceAsync(
            review, custody.Own, custody.Retain, token))).ConfigureAwait(false);
        if (!authorization.IsAllowed || authorization.State != HomePermissionRequestState.Approved)
        {
            if (authorization.State != HomePermissionRequestState.PendingApproval)
                throw Block("SPACE_CONTEXT_PERMISSION_REQUIRED", authorization.Message);
            // Borrow the actual shared Home decision window. Both the dispatcher original and
            // returned decision original are retained. Canceling this request does not close it.
            var dispatch = originals.Invoke(() => Dispatcher.UIThread.InvokeAsync<Task<HomeNativeApprovalDecisionObservation>>(() =>
                originals.Invoke(() =>
                {
                    DemandPairing();
                    return approvals.OpenAndObserveDecisionOriginalAsync(authorization.RequestId, token);
                })).GetTask());
            var decisionTask = await originals.AwaitAsync(dispatch).ConfigureAwait(false);
            var decision = await originals.AwaitAsync(decisionTask).ConfigureAwait(false);
            if (decision.RequestId != authorization.RequestId || !decision.DecisionRecorded || decision.State != HomePermissionRequestState.Approved)
                throw Block("SPACE_CONTEXT_PERMISSION_REQUIRED", decision.Message);
        }
        await DemandAuthorityAsync(owners, authority, originals, token).ConfigureAwait(false);
        originals.Invoke(() => { DemandPairing(); return true; });
        var actualAdmission = await originals.AwaitAsync(originals.Invoke(() => admission.BeginLibrarySnapshotWithinOriginalSourceAsync(
            review, custody.Own, custody.Retain, token))).ConfigureAwait(false)
            ?? throw Block("SPACE_CONTEXT_PERMISSION_REQUIRED", "The source read still needs its actual current Home decision.");
        custody.Keep(actualAdmission);
        var read = await originals.AwaitAsync(originals.Invoke(() => notes.ReadOriginalLibraryWithHomeAsync(request, actualAdmission, verified, token))).ConfigureAwait(false);
        custody.Keep(read);
        if (read.AuditWarning is not null) throw Block("SPACE_CONTEXT_READ_AUDIT_PENDING", "The source was read but its actual Home audit needs recovery; no model request was sent.");
        var document = read.SelectedDocument;
        if (document is null || document.Id != documentId || document.Version < 1 ||
            !BoardsWorkspaceService.IsBoardsNotebook(document) || BoardsWorkspaceService.IsDeletedNotebook(document))
            throw Block("SPACE_CONTEXT_BOARDS_SOURCE_UNAVAILABLE", "The configured canonical Boards notebook is unavailable.");
        var revision = document.Version.ToString(CultureInfo.InvariantCulture);
        if (reference.RevisionToken is { Length: > 0 } expected && expected != revision)
            throw Block("SPACE_CONTEXT_SOURCE_CHANGED", "The configured Boards version no longer matches. Review and update the source reference.");
        var witness = originals.Invoke(() => notes.CreateOriginalBoardsSummarySource(request, read, actualAdmission));
        custody.Add(witness, documentId, document.Version);
        var current = await originals.AwaitAsync(originals.Invoke(() => witness.ListAsync(false, token))).ConfigureAwait(false);
        if (!current.Any(value => value.Id == documentId && value.Version == document.Version))
            throw Block("SPACE_CONTEXT_SOURCE_CHANGED", "The actual Boards source changed before selection.");
        await DemandAuthorityAsync(owners, authority, originals, token).ConfigureAwait(false);
        return new(reference.ContextId, OriginalSpaceContextItemKind.ConfiguredSource, reference.OwnerAppId,
            reference.CanonicalEntityId, revision, BoundLabel(document.Title), ReadNotebookText(document));
    }

    private static string BuildCanonicalConfiguration(SpaceDefinition space)
    {
        var text = new StringBuilder().Append("Active 9to1 Space: ").AppendLine(space.Name)
            .Append("Kind: ").AppendLine(space.Kind.ToString());
        if (!string.IsNullOrWhiteSpace(space.Description)) text.Append("Purpose: ").AppendLine(space.Description);
        if (!string.IsNullOrWhiteSpace(space.Instructions)) text.AppendLine("Space instructions:").AppendLine(space.Instructions);
        foreach (var pair in space.ExamplePairs) text.Append("Example user: ").AppendLine(pair.User).Append("Example assistant: ").AppendLine(pair.Assistant);
        if (text.Length > 65_536) throw Block("SPACE_CONTEXT_CONFIGURATION_BOUND", "The canonical Space configuration exceeds this request's explicit bound.");
        return text.ToString();
    }
    private static string BoundLabel(string value) => value.Length <= 512 ? value : value[..512];
    private static string ReadNotebookText(NotesDocument document)
    {
        var text = new StringBuilder().AppendLine(document.Title).AppendLine("Text extraction only; drawings, attachments and embedded app content are not interpreted.");
        void Append(string? value)
        {
            if (string.IsNullOrEmpty(value)) return;
            if ((long)text.Length + value.Length + 1 > 65_536)
                throw Block("SPACE_CONTEXT_TEXT_BOUND", "A configured Boards source exceeds the explicit text bound. No content was silently truncated.");
            text.AppendLine(value);
        }
        foreach (var section in document.Sections)
        foreach (var page in section.Pages.OrderBy(value => value.Order))
        {
            Append(page.Title);
            foreach (var block in page.Blocks)
            {
                Append(block.Runs.Count == 0 ? block.PlainText : string.Concat(block.Runs.Select(value => value.Text)));
                if (block.Table is { } table)
                    foreach (var row in table.Rows) Append(string.Join(" | ", row.Cells.Select(cell => cell.Text)));
            }
        }
        return text.ToString();
    }
}
