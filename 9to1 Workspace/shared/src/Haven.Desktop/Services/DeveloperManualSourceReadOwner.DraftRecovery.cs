using System.IO;
using System.Security.Cryptography;
using Haven.Application;
using Haven.Infrastructure;
using HavenOS.Apps.Dev;
using HavenOS.Files.NativeHost;

namespace Haven.Desktop.Services;

/// <summary>Fresh authorised source plus its unreviewed protected draft; never an apply grant.</summary>
public sealed record DeveloperRestoredDirtyEditorTab(DeveloperDraftRecoveryTab Draft, string FreshBaselineText)
{
    public string FreshSourceSha256 => DeveloperManualSourceEditOwner.Sha256(FreshBaselineText);
    public bool BaselineMatches => StringComparer.OrdinalIgnoreCase.Equals(FreshSourceSha256, Draft.OriginalSha256);
}
public sealed record DeveloperManualRecoveryState(DeveloperDraftRecoveryScope Scope, long Generation,
    IReadOnlyList<DeveloperRestoredDirtyEditorTab> Tabs, string? SelectedRelativePath, bool HasSavedSnapshot);

public sealed partial class DeveloperManualSourceReadOwner
{
    /// <summary>Only the actual existing platform credential owner is usable. A substitute
    /// IProviderSecretStore, passphrase or settings-encryption fallback is never accepted.</summary>
    private static void DemandOriginalRecoveryKeys(IProviderSecretStore keys, IAppPaths actualPaths)
    {
        ArgumentNullException.ThrowIfNull(keys); ArgumentNullException.ThrowIfNull(actualPaths);
        if (OperatingSystem.IsLinux() && keys is LinuxProviderSecretStore linux && linux.HasOriginalComposition(actualPaths)) return;
        if (OperatingSystem.IsWindows() && keys is WindowsProviderSecretStore) return;
        throw new UnauthorizedAccessException("The actual protected platform credential owner is unavailable for draft recovery.");
    }

    /// <summary>Fresh original Home/Files/kernel admission before any protected draft is
    /// disclosed. Every source is freshly read before returning the complete observation.
    /// Changed baselines remain explicit conflicts, never automatic editor enrollments.
    /// Damaged/missing keys, unreadable source or denied actors preserve encrypted evidence.</summary>
    public async Task<DeveloperOperationResult<DeveloperManualRecoveryState>> RestoreManualDraftRecoveryAsync(
        FileDeveloperWorkspaceStore store, IProviderSecretStore keys, IAppPaths actualPaths,
        DeveloperResolvedProject project, CancellationToken token)
    {
        try
        {
            DemandOriginalRecoveryKeys(keys, actualPaths);
            await using var lease = await AcquireAsync(project, token).ConfigureAwait(false);
            var profile = DemandEditorSessionStore(store, lease);
            var scope = new DeveloperDraftRecoveryScope(profile, Guid.Empty, Guid.Empty, Guid.Empty, IsManual: true);
            var loaded = await store.GetDraftRecoveryAsync(project, scope, token).ConfigureAwait(false);
            if (!loaded.Succeeded) return SessionError<DeveloperManualRecoveryState>(loaded.Error!);
            if (loaded.Value is not { } snapshot)
            {
                await lease.Admission.RevalidateOriginalAsync(token).ConfigureAwait(false);
                return DeveloperOperationResult<DeveloperManualRecoveryState>.Success(new(scope, 0, [], null, false));
            }
            byte[] key = [];
            DeveloperDraftRecoveryPayload payload;
            try
            {
                key = await ReadRecoveryKeyAsync(keys, profile, project.Reference.WorkspaceId, required: true, token).ConfigureAwait(false);
                await lease.Admission.RevalidateOriginalAsync(token).ConfigureAwait(false);
                payload = FileDeveloperWorkspaceStore.UnprotectDraftRecovery(project, scope, snapshot, key);
            }
            finally { CryptographicOperations.ZeroMemory(key); }
            var restored = new List<DeveloperRestoredDirtyEditorTab>();
            foreach (var tab in payload.Tabs)
            {
                var source = await capture.ReadOriginalCapturedFileTextAsync(lease.Capture, lease.Admission, tab.RelativePath, token).ConfigureAwait(false);
                if (source is null)
                    return DeveloperOperationResult<DeveloperManualRecoveryState>.Failure(DeveloperOperationErrorCode.RevisionConflict,
                        "A protected draft's source is missing or unreadable. No partial recovery was published; the encrypted draft remains saved.", project.Reference.ProjectId.ToString("D"));
                // A changed readable baseline is a conflict observation, never permission to
                // replace it automatically. The shared workbench requires fresh READ again and
                // an explicit user reconciliation before enrolling this unreviewed draft.
                restored.Add(new(tab, source));
            }
            await lease.Admission.RevalidateOriginalAsync(token).ConfigureAwait(false);
            return DeveloperOperationResult<DeveloperManualRecoveryState>.Success(new(scope, snapshot.Generation,
                Array.AsReadOnly(restored.ToArray()), payload.SelectedRelativePath, true));
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception error) when (error is UnauthorizedAccessException or IOException or InvalidOperationException or AggregateException or CryptographicException or FormatException)
        {
            return RecoveryFailure<DeveloperManualRecoveryState>(project, error,
                "Protected draft recovery could not obtain the current source and original key. Saved recovery was preserved; source files remain usable.");
        }
    }

    /// <summary>Same canonical document and original workspace reservation as clean state.
    /// Key get/create is serialized inside that document's cross-process CAS lock; missing keys
    /// are never regenerated while any recovery for this profile exists. No source write occurs.</summary>
    public async Task<DeveloperOperationResult<DeveloperDraftRecoverySnapshot>> SaveManualDraftRecoveryAsync(
        FileDeveloperWorkspaceStore store, FilesDeveloperOriginalCurrentProjectExecutionBridge execution,
        IProviderSecretStore keys, IAppPaths actualPaths, DeveloperResolvedProject project,
        DeveloperDraftRecoveryScope scope, long expectedGeneration, DeveloperDraftRecoveryPayload payload, CancellationToken token)
    {
        IDisposable? reservation = null;
        try
        {
            DemandOriginalRecoveryKeys(keys, actualPaths);
            ArgumentNullException.ThrowIfNull(scope); ArgumentNullException.ThrowIfNull(payload);
            if (payload.Tabs is null || payload.Tabs.Count > DeveloperSourceReviewSession.MaximumOpenDocuments)
                throw new ArgumentException("A bounded complete recovery payload is required.", nameof(payload));
            foreach (var tab in payload.Tabs)
            {
                ArgumentNullException.ThrowIfNull(tab);
                DemandSafeProjectRelativePath(tab.RelativePath);
            }
            // A refusal here grants nothing and performs no source mutation.
            if (!scope.IsManual || scope.TaskId != Guid.Empty || scope.ExecutionId != Guid.Empty || scope.ContextId != Guid.Empty)
                throw new UnauthorizedAccessException("Manual recovery cannot borrow a Task/Run or another mode's partition.");
            IDeveloperProjectOriginalReadSelection selection;
            AuthenticatedResourceActor actor;
            await using (var lease = await AcquireAsync(project, token).ConfigureAwait(false))
            {
                var profile = DemandEditorSessionStore(store, lease);
                if (profile != scope.ProfileId) throw new UnauthorizedAccessException("The original Home profile changed before saving its draft.");
                selection = lease.Selection;
                actor = selections.GetOriginalRegisteredDescriptor(selection).OriginalActor;
                reservation = execution.AcquireOriginalEditorSessionUpdate(project.Reference.WorkspaceId, actor);
                foreach (var tab in payload.Tabs)
                {
                    DemandSafeProjectRelativePath(tab.RelativePath);
                    var text = await capture.ReadOriginalCapturedFileTextAsync(lease.Capture, lease.Admission, tab.RelativePath, token).ConfigureAwait(false);
                    if (text is null || !StringComparer.OrdinalIgnoreCase.Equals(DeveloperManualSourceEditOwner.Sha256(text), tab.OriginalSha256))
                        return DeveloperOperationResult<DeveloperDraftRecoverySnapshot>.Failure(DeveloperOperationErrorCode.RevisionConflict,
                            "Source changed before protected recovery could be saved. The live draft and prior encrypted recovery were preserved.", project.Reference.ProjectId.ToString("D"));
                }
                await lease.Admission.RevalidateOriginalAsync(token).ConfigureAwait(false);
            }
            // The exact metadata READ is closed before replacing its document/inode. Keep
            // original execution exclusion through the key operation, CAS and atomic rename.
            await selections.RevalidateOriginalAsync(selection, actor, token).ConfigureAwait(false);
            return await store.PrepareAndSaveDraftRecoveryAsync(project, scope, expectedGeneration, async (recordsExist, ct) =>
            {
                await selections.RevalidateOriginalAsync(selection, actor, ct).ConfigureAwait(false);
                byte[] key = [];
                try
                {
                    key = await ReadRecoveryKeyAsync(keys, scope.ProfileId, project.Reference.WorkspaceId, recordsExist, ct).ConfigureAwait(false);
                    var prepared = FileDeveloperWorkspaceStore.ProtectDraftRecovery(project, scope, checked(expectedGeneration + 1), payload, key);
                    await selections.RevalidateOriginalAsync(selection, actor, ct).ConfigureAwait(false);
                    return prepared;
                }
                finally { CryptographicOperations.ZeroMemory(key); }
            }, token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception error) when (error is UnauthorizedAccessException or IOException or InvalidOperationException or AggregateException or CryptographicException or FormatException or ArgumentException)
        {
            return RecoveryFailure<DeveloperDraftRecoverySnapshot>(project, error,
                "Protected recovery was not saved. Keep this draft open or apply it through its normal Home write approval.");
        }
        finally { reservation?.Dispose(); }
    }

    private static async Task<byte[]> ReadRecoveryKeyAsync(IProviderSecretStore keys, Guid profile,
        Guid workspace, bool required, CancellationToken token)
    {
        var provider = "9to1.build.recovery." + profile.ToString("N");
        var name = workspace.ToString("N");
        var encoded = await keys.GetAsync(provider, name, token).ConfigureAwait(false);
        if (encoded is not null)
        {
            var existing = Convert.FromBase64String(encoded);
            if (existing.Length == 32) return existing;
            CryptographicOperations.ZeroMemory(existing);
            throw new InvalidDataException("The protected recovery key requires owner recovery; it was not reset.");
        }
        if (required) throw new InvalidDataException("The protected recovery key is missing; existing encrypted drafts were preserved.");
        // Only called while the SAME canonical workspace CAS lock is held. No unlocked
        // Get/null/Set and no key reset/rotation can strand another accepted snapshot.
        var created = RandomNumberGenerator.GetBytes(32);
        try
        {
            await keys.SetAsync(provider, name, Convert.ToBase64String(created), token).ConfigureAwait(false);
            return created;
        }
        catch { CryptographicOperations.ZeroMemory(created); throw; }
    }

    private static DeveloperOperationResult<T> RecoveryFailure<T>(DeveloperResolvedProject project, Exception error, string message) =>
        DeveloperOperationResult<T>.Failure(error is UnauthorizedAccessException ? DeveloperOperationErrorCode.PermissionRequired :
            error is ArgumentException ? DeveloperOperationErrorCode.InvalidInput :
            error is IOException or AggregateException or CryptographicException or FormatException ? DeveloperOperationErrorCode.StorageUnavailable : DeveloperOperationErrorCode.RevisionConflict,
            message, project.Reference.ProjectId.ToString("D"));
}
