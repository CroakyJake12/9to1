using System.Security.Cryptography;
using Haven.Application;
using HavenOS.Apps.Dev;
using HavenOS.Files.NativeHost;

namespace Haven.Desktop.Services;

public sealed record DeveloperRunConfigurationEditObservation(DeveloperOperationResult<DeveloperRunConfigurationEditResult>? OriginalStoreResult,
    IReadOnlyList<Exception> OriginalErrors);

public sealed partial class DeveloperManualSourceReadOwner
{
    /// <summary>Deliberate canonical declaration update after genuine registered Home READ.
    /// The SAME source/store/profile/key owner and reciprocal manual/Task start exclusion remain
    /// in custody through atomic metadata/recovery migration. No source or process is written.</summary>
    public async Task<DeveloperRunConfigurationEditObservation> SaveReviewedRunConfigurationAsync(
        FileDeveloperWorkspaceStore store, FilesDeveloperOriginalCurrentProjectExecutionBridge? execution,
        IProviderSecretStore keys, IAppPaths paths, DeveloperResolvedProject project,
        DeveloperRunConfigurationEditReview reviewed, long expectedRecoveryGeneration,
        Action<Action> originalCallerCallback, CancellationToken token)
    {
        var sources = new CloudflareOriginalTaskLedger(); sources.BindOriginalOwner(sources);
        sources.BindOriginalCallerCallback(originalCallerCallback);
        IDisposable? manualReservation = null, taskReservation = null;
        ManualReadLease? lease = null;
        var retainedKeys = new List<byte[]>();
        DeveloperOperationResult<DeveloperRunConfigurationEditResult>? result = null;
        try
        {
            await sources.RunToOriginalSettlementAsync(async () =>
            {
                DemandOriginalRecoveryKeys(keys, paths);
                if (!DeveloperRunConfigurationEdit.IsCurrent(project, reviewed))
                    throw new UnauthorizedAccessException("The exact reviewed console declaration/project changed before source admission.");
                lease = await sources.CaptureOriginalAcquisitionAsync(() => AcquireAsync(project, token), actual => lease = actual).ConfigureAwait(false);
                var profile = sources.Invoke(() => DemandEditorSessionStore(store, lease));
                var scope = new DeveloperDraftRecoveryScope(profile, Guid.Empty, Guid.Empty, Guid.Empty, IsManual: true);
                var selection = lease.Selection;
                var actor = sources.Invoke(() => selections.GetOriginalRegisteredDescriptor(selection).OriginalActor);
                _ = sources.Invoke(() => manualReservation = selections.AcquireOriginalManualMetadataUpdate(selection, lease.Capture, actor));
                if (execution is not null) _ = sources.Invoke(() => taskReservation = execution.AcquireOriginalEditorSessionUpdate(project.Reference.WorkspaceId, actor));
                await sources.AwaitAsync(sources.Invoke(() => lease.Admission.RevalidateOriginalAsync(token))).ConfigureAwait(false);
                await sources.ObserveOriginalCloseAsync(lease.DisposeAsync).ConfigureAwait(false); lease = null;
                // Close the actual metadata witness READ before its inode changes. Retain both
                // exclusion reservations through key custody, all CAS checks and atomic rename.
                var saved = await sources.CaptureOriginalAcquisitionAsync(() => store.SaveReviewedRunConfigurationAsync(project, reviewed, scope,
                    expectedRecoveryGeneration, async ct =>
                    {
                        await sources.AwaitAsync(sources.Invoke(() => selections.RevalidateOriginalAsync(selection, actor, ct))).ConfigureAwait(false);
                        var key = await sources.CaptureOriginalAcquisitionAsync(() => ReadRecoveryKeyAsync(keys, profile,
                            project.Reference.WorkspaceId, required: true, ct), actual => retainedKeys.Add(actual)).ConfigureAwait(false);
                        return key;
                    }, ct => sources.AwaitAsync(sources.Invoke(() => selections.RevalidateOriginalAsync(selection, actor, ct))), token),
                    actual => result = actual).ConfigureAwait(false);
                return saved;
            }).ConfigureAwait(false);
        }
        catch (Exception error) { sources.Retain(error); }
        finally
        {
            if (lease is not null)
                try { await sources.ObserveOriginalCloseAsync(lease.DisposeAsync).ConfigureAwait(false); } catch (Exception error) { sources.Retain(error); }
            // Independent releases even after a saved metadata result. A later close fault does
            // not authorize replay; the page must inspect the canonical revision before retrying.
            foreach (var reservation in new[] { taskReservation, manualReservation })
                if (reservation is not null)
                    try { sources.Invoke(() => { reservation.Dispose(); return true; }); } catch (Exception error) { sources.Retain(error); }
            foreach (var key in retainedKeys) CryptographicOperations.ZeroMemory(key);
            await sources.ObserveAllOriginalTasksAsync().ConfigureAwait(false);
        }
        return new(result, Array.AsReadOnly(sources.OriginalErrors.ToArray()));
    }
}
