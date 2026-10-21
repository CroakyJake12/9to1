using System.IO;
using System.Runtime.ExceptionServices;
using Haven.Application;
using Haven.Core;
using HavenOS.Apps.Dev;
using HavenOS.Files;
using HavenOS.Files.NativeHost;
using HavenOS.Home.Core;

namespace Haven.Desktop.Services;

/// <summary>
/// A genuine manual (no-AI) Build project entry adapter. It registers a real existing project
/// folder beneath the configured Files root through the SAME private setup owners the production
/// app and the reviewed register-existing flow use (genuine Files selection, Home READ admission,
/// setup scopes/journal/permission and the kernel capture + folder setup producer) and persists the
/// resulting canonical workspace/project through the real strict store. It creates no Task, model,
/// account, grant or trusted=true shortcut, and it never treats a path or root as authorization:
/// every effect is admitted by the real broker per-action decision and the setup journal ACK.
///
/// This is a reviewed backend/domain adapter over the existing real APIs; it does not author any
/// CUI/control and does not replace the landing document. The caller (composition) supplies the
/// exact selected existing project root and the canonical destination folder identity; this adapter
/// performs the genuine register-existing journey and returns the saved project reference that the
/// existing manual-open path opens (same genuine read/review/apply owners).
///
/// The adapter retains the once-created setup/workspace identity from preparation through terminal
/// disposition. A generic retry after a persisted/partially effected failure does NOT mint a new
/// setup/workspace id and does NOT re-run steps. Successful automatic recovery requires POSITIVE
/// confirmation that this exact attempt's final original permission/READ completion closed cleanly;
/// journal step ACKs alone do not prove completion (a stored row is not completion proof). Absent
/// that positive confirmation, an acknowledged-but-unconfirmed attempt (pending/in-flight or failed
/// at completion) reports an honest blocked state that must be inspected, never replayed or
/// replaced. Every acquired READ admission and setup permission is closed on success (permission
/// first, then read) and on every failure (independently, preserving all sibling faults).
/// </summary>
public sealed class DeveloperManualProjectEntryAdapter(
    FilesDeveloperOriginalSourceSelection selections,
    IDeveloperProjectOriginalReadAdmissionSource reads,
    FilesDeveloperOriginalSetupScopeSource scopes,
    HomeDeveloperProjectSetupJournal journal,
    IDeveloperProjectOriginalSetupPermissionSource permissions,
    FilesDeveloperOriginalFolderSetupProducer steps,
    IDeveloperWorkspaceStore workspaces)
{
    private readonly object _gate = new();
    private Guid? _retainedSetupId;
    private Guid? _retainedWorkspaceId;
    private string? _retainedProjectRoot;
    private bool _retainedCompletionConfirmed;   // positive terminal permission+READ completion proof for the retained attempt
    private bool _retainedCompletionUncertain;  // failure after acknowledged writes => explicit uncertain/blocked state

    /// <summary>The retained once-created setup id of the current attempt, if any. Exposed to the
    /// visible landing so a partially effected attempt can be inspected rather than replaced.</summary>
    public Guid? RetainedSetupId { get { lock (_gate) return _retainedSetupId; } }
    /// <summary>The retained once-created workspace id of the current attempt, if any.</summary>
    public Guid? RetainedWorkspaceId { get { lock (_gate) return _retainedWorkspaceId; } }
    /// <summary>The retained existing project root of the current attempt, if any.</summary>
    public string? RetainedProjectRoot { get { lock (_gate) return _retainedProjectRoot; } }
    /// <summary>Registers the exact selected existing project root beneath the configured Files
    /// store as a fresh canonical workspace/project (no source copy, no overwrite), verifies the
    /// setup journal is fully acknowledged, and returns the saved project reference for the SAME
    /// manual-open path. It uses a new once-created setup/workspace identity and never retries or
    /// replaces a pending/failed setup; a retry of the SAME folder inspects the retained journal and
    /// either recovers the completed identity or reports a blocked state for honest inspection.</summary>
    public async Task<DeveloperProjectReference> RegisterExistingFolderAsync(
        Guid filesStoreId, Guid destinationFolderId, string existingProjectRoot, CancellationToken token)
    {
        // Pure-argument validation precedes dependency null checks so an invalid request fails
        // with a precise ArgumentException/OperationCanceledException before any owner is touched.
        if (filesStoreId == Guid.Empty) throw new ArgumentException("Retain the actual configured Files store UUID.", nameof(filesStoreId));
        if (destinationFolderId == Guid.Empty) throw new ArgumentException("Select the actual configured destination folder.", nameof(destinationFolderId));
        if (!Path.IsPathFullyQualified(existingProjectRoot))
            throw new ArgumentException("Select an existing absolute project path below the configured Files root.", nameof(existingProjectRoot));
        token.ThrowIfCancellationRequested();

        ArgumentNullException.ThrowIfNull(selections);
        ArgumentNullException.ThrowIfNull(reads);
        ArgumentNullException.ThrowIfNull(scopes);
        ArgumentNullException.ThrowIfNull(journal);
        ArgumentNullException.ThrowIfNull(permissions);
        ArgumentNullException.ThrowIfNull(steps);
        ArgumentNullException.ThrowIfNull(workspaces);

        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(existingProjectRoot));

        // Once-created attempt identity retained from preparation through terminal disposition.
        // A retry of the SAME folder reuses the exact ids; a different folder starts an unrelated
        // fresh attempt (the partially effected prior attempt remains recoverable by its retained id).
        Guid setupId, workspaceId; bool isRetry;
        lock (_gate)
        {
            if (_retainedSetupId is { } sid && _retainedWorkspaceId is { } wid
                && _retainedProjectRoot is { } prior && string.Equals(prior, root, StringComparison.Ordinal))
            {
                setupId = sid; workspaceId = wid; isRetry = true;
            }
            else
            {
                setupId = Guid.NewGuid(); workspaceId = Guid.NewGuid();
                _retainedSetupId = setupId; _retainedWorkspaceId = workspaceId; _retainedProjectRoot = root;
                // A fresh unrelated attempt inherits no prior completion/fault state.
                _retainedCompletionConfirmed = false; _retainedCompletionUncertain = false;
                isRetry = false;
            }
        }

        var selection = await selections.SelectOriginalExistingProjectAsync(filesStoreId, root, token).ConfigureAwait(false);
        var destination = await scopes.CaptureOriginalDestinationAsync(filesStoreId, new HostedItemId(destinationFolderId), token).ConfigureAwait(false);

        // A generic retry of a persisted/partially effected attempt must NOT re-run steps or mint
        // new ids. Inspect the retained journal and either recover the completed identity or surface
        // an honest blocked state requiring recovery inspection (not fake success/replay).
        if (isRetry)
            return await RecoverRetainedAttemptAsync(setupId, workspaceId, root, destination.OriginalActor, token).ConfigureAwait(false);

        IDeveloperProjectOriginalReadAdmission? read = null;
        IDeveloperProjectOriginalSetupPermission? permission = null;
        bool completionMaterialized = false;
        try
        {
            read = await reads.AcquireOriginalAsync(selection, token).ConfigureAwait(false);
            var capture = await selections.CaptureOriginalAsync(selection, read, token).ConfigureAwait(false);
            var prepared = await journal.PrepareExistingOriginalAsync(setupId,
                destination.OriginalActor, destination.OriginalStoreId, destination.OriginalConfigurationDigest,
                destination.OriginalFolderId, destination.OriginalFolderRevision, capture, workspaceId, 0,
                Path.GetFileName(root), token).ConfigureAwait(false);
            await scopes.BindOriginalAsync(destination, prepared, capture, token).ConfigureAwait(false);
            permission = await permissions.AcquireOriginalAsync(prepared.Intent, capture, token).ConfigureAwait(false);

            FilesDeveloperOriginalFolderSetupProducer.WorkspaceMetadataStep? savedStep = null;
            foreach (var step in prepared.Intent.Steps)
            {
                switch (step.Kind)
                {
                    case DeveloperProjectSetupStepKind.CreateProjectFolder:
                    case DeveloperProjectSetupStepKind.CreateChildFolder:
                        await steps.ExecuteOriginalFolderStepAsync(prepared, capture, permission, step, token).ConfigureAwait(false); break;
                    case DeveloperProjectSetupStepKind.ObserveExistingProjectDirectory:
                    case DeveloperProjectSetupStepKind.ObserveExistingChildDirectory:
                        await steps.ExecuteOriginalExistingDirectoryStepAsync(prepared, capture, permission, step, token).ConfigureAwait(false); break;
                    case DeveloperProjectSetupStepKind.RegisterProjectFolder:
                        await steps.ExecuteOriginalProjectRegistrationStepAsync(prepared, capture, permission, step, token).ConfigureAwait(false); break;
                    case DeveloperProjectSetupStepKind.RegisterExistingFileMetadata:
                    case DeveloperProjectSetupStepKind.RegisterMaterialization:
                        await steps.ExecuteOriginalExistingFileStepAsync(prepared, capture, permission, step, token).ConfigureAwait(false); break;
                    case DeveloperProjectSetupStepKind.SaveDevWorkspace:
                        savedStep = await steps.ExecuteOriginalWorkspaceMetadataStepAsync(prepared, capture, permission, step, token).ConfigureAwait(false); break;
                    default:
                        throw new NotSupportedException("The genuine register-existing manual entry cannot dispatch a mutation/import step.");
                }
            }

            var checkpoint = await journal.ReadCheckpointAsync(setupId, destination.OriginalActor, token).ConfigureAwait(false)
                ?? throw new InvalidOperationException("The original setup checkpoint is unavailable.");
            if (checkpoint.Observations.Length != prepared.Intent.Steps.Length ||
                checkpoint.Observations.Any(row => row.State != DeveloperProjectSetupStepState.Acknowledged))
                throw new InvalidOperationException("The retained setup is not fully acknowledged; preserve its original IDs without replay.");
            // Every retained step is now ACKed and the strict workspace is saved; the only remaining
            // required completion is the terminal custody close below. A failure from here on makes
            // the attempt's terminal completion uncertain (a stored row is not positive completion proof).
            completionMaterialized = true;

            var actualSaved = savedStep?.OriginalStoreResult.Workspace
                ?? throw new InvalidOperationException("No actual strict workspace save and journal ACK product was returned.");

            // Independent persistence verification through the real strict store (not a Task/resolve
            // owner). The saved workspace must still be present with the SAME once-created identity
            // after the setup journal ACK; no replacement id is minted and nothing is replayed.
            var reference = await ResolveSavedReferenceAsync(prepared.Intent, actualSaved, workspaces, token).ConfigureAwait(false);

            // SUCCESS: before returning/publishing, independently await permission closure then READ
            // closure. Both are IAsyncDisposable and run the real final audit/Home finalACK work; a
            // blocked cleanup fails the whole success (never a silent leak or fake acknowledgement).
            // Only when BOTH close cleanly is this exact attempt positively confirmed. The
            // confirmation is recorded on the SAME retained ids so a later retry of the same folder
            // recovers the completed identity instead of duplicating it or fabricating success. An
            // unconfirmed (failed/pending) close leaves the attempt unconfirmed so any retry blocks.
            await CloseSuccessCustodyAsync(permission, read).ConfigureAwait(false);
            MarkCompletionConfirmed(setupId, workspaceId, root);
            return reference;
        }
        catch (Exception bodyError)
        {
            // Once the persisted steps fully acknowledged (materialized), any failure means the
            // terminal permission/READ completion is not positively confirmed. Record that on the
            // SAME retained ids so a retry blocks honestly instead of turning failure into success.
            if (completionMaterialized)
                MarkCompletionUncertain(setupId, workspaceId, root);
            // Close every acquired original independently, preserving body + permission-close +
            // read-close faults without masking unknown siblings. Single-error paths rethrow the
            // exact original fault (including cancellations) unchanged.
            await CloseFailureCustodyAsync(permission, read, bodyError).ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>Recovers or reports the SAME retained once-created attempt. A fully acknowledged
    /// journal is recovered from the real strict store with no replay and no new ids; any other
    /// retained state is surfaced as an honest blocked inspection (same id, same partial effect).</summary>
    private async Task<DeveloperProjectReference> RecoverRetainedAttemptAsync(
        Guid setupId, Guid workspaceId, string root, AuthenticatedResourceActor actor, CancellationToken token)
    {
        var checkpoint = await journal.ReadCheckpointAsync(setupId, actor, token).ConfigureAwait(false)
            ?? throw new DeveloperManualAttemptBlockedException(setupId,
                $"The retained manual setup {setupId:D} has no persisted journal record; inspect the same journal instead of re-preparing.");

        var intent = checkpoint.Intent;
        var acknowledged = checkpoint.Observations.Length == intent.Steps.Length
            && checkpoint.Observations.All(row => row.State == DeveloperProjectSetupStepState.Acknowledged);
        var sameRequest = intent.Mode == DeveloperProjectSetupMode.RegisterExisting
            && intent.WorkspaceId == workspaceId
            && intent.OriginalExistingProjectRoot is not null
            && string.Equals(intent.OriginalExistingProjectRoot, root, StringComparison.Ordinal);

        // A partially acknowledged / mismatched retained attempt is surfaced as an honest blocked
        // inspection describing its observed journal state (e.g. NotStarted). Same ids are kept.
        if (!acknowledged || !sameRequest)
        {
            var state = DescribeCheckpointState(checkpoint);
            throw new DeveloperManualAttemptBlockedException(setupId,
                $"The retained manual setup {setupId:D} is not fully acknowledged ({state}); recover the same journal and original ids - do not re-run steps or mint new ids.", state);
        }

        // Journal ACKs are NOT completion. Success requires positive confirmation that THIS EXACT
        // requested attempt's FINAL original permission/READ completion closed cleanly. The
        // confirmation is observed together under the lock and bound to the requested ids, so a
        // superseded/different attempt's state can never stand in for this one. Absent that proof,
        // an acknowledged-but-unconfirmed attempt (pending, in-flight, failed at completion, or
        // superseded) blocks honestly - never auto-recover into a fabricated success.
        DemandPositiveCompletionForRequestedAttempt(setupId, workspaceId, root);

        // Positively confirmed: genuine recovery of the SAME once-created identity (no replay, no new ids).
        var persisted = await workspaces.GetAsync(workspaceId, token).ConfigureAwait(false);
        if (!persisted.Succeeded || persisted.Value is null)
            throw new InvalidOperationException("The saved workspace for the retained manual project is unavailable after the journal ACK.");
        if (persisted.Value.WorkspaceId != workspaceId)
            throw new InvalidOperationException("The retained workspace identity changed after the journal ACK.");
        var project = persisted.Value.Projects.SingleOrDefault(value => value.ProjectId == intent.ProjectId)
            ?? throw new InvalidOperationException("The saved project for the retained manual entry is missing after the journal ACK.");
        return new DeveloperProjectReference(workspaceId, persisted.Value.Revision, project.ProjectId,
            project.Revision, intent.RootId, null);
    }

    private static string DescribeCheckpointState(DeveloperProjectSetupCheckpoint checkpoint)
    {
        var counts = checkpoint.Observations.GroupBy(row => row.State)
            .ToDictionary(group => group.Key, group => group.Count());
        return "NotStarted=" + (counts.TryGetValue(DeveloperProjectSetupStepState.NotStarted, out var ns) ? ns : 0) +
            ",Admitted=" + (counts.TryGetValue(DeveloperProjectSetupStepState.Admitted, out var ad) ? ad : 0) +
            ",Acknowledged=" + (counts.TryGetValue(DeveloperProjectSetupStepState.Acknowledged, out var ak) ? ak : 0) +
            ",Unresolved=" + (counts.TryGetValue(DeveloperProjectSetupStepState.Unresolved, out var ur) ? ur : 0);
    }

    private static async Task<DeveloperProjectReference> ResolveSavedReferenceAsync(
        DeveloperProjectSetupIntent intent, DeveloperWorkspace actualSaved,
        IDeveloperWorkspaceStore workspaces, CancellationToken token)
    {
        _ = intent;
        var actualSavedProject = actualSaved.Projects.Single(value => value.ProjectId == intent.ProjectId);
        var reference = new DeveloperProjectReference(actualSaved.WorkspaceId, actualSaved.Revision, actualSavedProject.ProjectId,
            actualSavedProject.Revision, intent.RootId,
            actualSaved.SourceControlBindings.SingleOrDefault(value => value.RootId == intent.RootId)?.BindingId);

        var persisted = await workspaces.GetAsync(reference.WorkspaceId, token).ConfigureAwait(false);
        if (!persisted.Succeeded || persisted.Value is null)
            throw new InvalidOperationException("The saved workspace for the manual project is unavailable after the journal ACK.");
        if (persisted.Value.WorkspaceId != reference.WorkspaceId || persisted.Value.Revision != reference.WorkspaceRevision)
            throw new InvalidOperationException("The retained workspace identity changed after the journal ACK.");
        if (!persisted.Value.Projects.Any(value => value.ProjectId == reference.ProjectId))
            throw new InvalidOperationException("The saved project for the manual entry is missing after the journal ACK.");
        return reference;
    }

    private static async Task CloseSuccessCustodyAsync(
        IDeveloperProjectOriginalSetupPermission? permission, IDeveloperProjectOriginalReadAdmission? read)
    {
        var errors = new List<Exception>();
        if (permission is not null)
            try { await permission.DisposeAsync().ConfigureAwait(false); } catch (Exception error) { errors.Add(error); }
        if (read is not null)
            try { await read.DisposeAsync().ConfigureAwait(false); } catch (Exception error) { errors.Add(error); }
        if (errors.Count != 0)
            throw new AggregateException("The original manual-entry permission/READ custody did not close cleanly; the success was not acknowledged.", errors);
    }

    private static async Task CloseFailureCustodyAsync(
        IDeveloperProjectOriginalSetupPermission? permission, IDeveloperProjectOriginalReadAdmission? read, Exception bodyError)
    {
        var errors = new List<Exception> { bodyError };
        if (permission is not null)
            try { await permission.DisposeAsync().ConfigureAwait(false); } catch (Exception error) { errors.Add(error); }
        if (read is not null)
            try { await read.DisposeAsync().ConfigureAwait(false); } catch (Exception error) { errors.Add(error); }
        if (errors.Count == 1)
        {
            // Preserve the exact original fault (type, message, stack) when nothing else failed.
            ExceptionDispatchInfo.Capture(errors[0]).Throw();
            return;
        }
        throw new AggregateException(
            "The original manual-entry registration failed and its acquired permission/READ custody did not all close cleanly.", errors);
    }

    private bool IsSameRetainedAttempt(Guid setupId, Guid workspaceId, string root)
        => _retainedSetupId == setupId && _retainedWorkspaceId == workspaceId
           && string.Equals(_retainedProjectRoot, root, StringComparison.Ordinal);

    private void MarkCompletionConfirmed(Guid setupId, Guid workspaceId, string root)
    {
        lock (_gate)
        {
            // Only the SAME retained attempt may be confirmed; an older in-flight attempt cannot
            // mark or clear a different newer attempt.
            if (IsSameRetainedAttempt(setupId, workspaceId, root))
            { _retainedCompletionConfirmed = true; _retainedCompletionUncertain = false; }
        }
    }

    private void MarkCompletionUncertain(Guid setupId, Guid workspaceId, string root)
    {
        lock (_gate)
        {
            // Only the SAME retained attempt may be marked uncertain; an older in-flight attempt
            // cannot poison a different newer attempt.
            if (IsSameRetainedAttempt(setupId, workspaceId, root))
                _retainedCompletionUncertain = true;
        }
    }

    /// <summary>Requires POSITIVE confirmation that the requested attempt's final permission/READ
    /// completion closed cleanly. The confirmation and uncertainty flags are observed together under
    /// the lock and bound to the exact requested setup/workspace/root, so a superseded or different
    /// attempt's state can never stand in for this one. Without that positive confirmation the
    /// requested attempt blocks honestly (no replay, remint, or fabricated success).</summary>
    private void DemandPositiveCompletionForRequestedAttempt(Guid setupId, Guid workspaceId, string root)
    {
        bool confirmed; string? reason;
        lock (_gate)
        {
            if (!IsSameRetainedAttempt(setupId, workspaceId, root))
            {
                // The retained slot now belongs to a different/superseded attempt (or none). This
                // requested attempt's completion is unknown; never read another attempt's true state.
                confirmed = false; reason = "CompletionSuperseded";
            }
            else
            {
                confirmed = _retainedCompletionConfirmed;
                reason = _retainedCompletionUncertain ? "CompletionUncertain" : "CompletionPendingUnknown";
            }
        }
        if (!confirmed)
            throw new DeveloperManualAttemptBlockedException(setupId,
                $"The retained manual setup {setupId:D} has acknowledged journal steps but its final permission/READ completion is not positively confirmed for this exact attempt ({reason}); inspect the same journal/ids - do not auto-replay or publish.",
                reason);
    }
}

/// <summary>The retained manual attempt cannot be safely re-run or replaced: it reached a partial/
/// uncertain persisted state and must be inspected under its SAME once-created setup id. This is an
/// honest blocked state, never fake success or replay.</summary>
public sealed class DeveloperManualAttemptBlockedException : Exception
{
    public DeveloperManualAttemptBlockedException(Guid setupId, string message, string? state = null)
        : base(message)
    { SetupId = setupId; State = state; }

    public Guid SetupId { get; }
    public string? State { get; }
}
