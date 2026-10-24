using System.Security.Cryptography;
using System.Text.Json;

namespace HavenOS.Apps.Dev;

/// <summary>Recovery partition, not authority. The host must observe the SAME actual Home
/// profile and canonical Task/Run/context before protecting, saving or disclosing a draft.</summary>
public sealed record DeveloperDraftRecoveryScope(Guid ProfileId, Guid TaskId, Guid ExecutionId, Guid ContextId, bool IsManual = false);

/// <summary>Only the unsaved text is retained. Recovery requires a new authorised source read
/// whose hash matches OriginalSha256; neither a path nor this hash confers Files permission.</summary>
public sealed record DeveloperDraftRecoveryTab(string RelativePath, string OriginalSha256, string DraftText, bool IsPinned);
public sealed record DeveloperDraftRecoveryPayload(IReadOnlyList<DeveloperDraftRecoveryTab> Tabs, string? SelectedRelativePath);

/// <summary>Authenticated ciphertext inside the SAME canonical workspace document. Header IDs
/// are observations, never grants. No draft, baseline, key, command or review approval is stored
/// in plaintext. A successful storage result does not prove that its task/actor is still current.</summary>
public sealed record DeveloperDraftRecoverySnapshot(int FormatVersion, DeveloperDraftRecoveryScope Scope,
    DeveloperProjectReference ProjectReference, string ProjectStamp, long Generation,
    DateTimeOffset SavedAtUtc, string ProtectedPayload);

public sealed partial class FileDeveloperWorkspaceStore
{
    public const int MaximumDraftRecoveryPlaintextBytes = 128 * 1024;
    public const int MaximumDraftRecoverySnapshots = 64;
    private const int DraftRecoveryFormatVersion = 1;
    private const int RecoveryNonceBytes = 12, RecoveryTagBytes = 16;
    private const int MaximumRecoveryCiphertextBytes = MaximumDraftRecoveryPlaintextBytes + RecoveryNonceBytes + RecoveryTagBytes;
    private const int MaximumRecoveryBase64Characters = ((MaximumRecoveryCiphertextBytes + 2) / 3) * 4;

    /// <summary>Pure authenticated encryption with the actual host-provided 256-bit protected
    /// key. This method never creates, stores or looks up a key and offers no plaintext fallback.
    /// The trusted host must acquire it through its original protected-key owner after actual
    /// Home/source admission, minimise its lifetime and clear its own key buffer afterwards.</summary>
    public static DeveloperDraftRecoverySnapshot ProtectDraftRecovery(DeveloperResolvedProject project,
        DeveloperDraftRecoveryScope scope, long generation, DeveloperDraftRecoveryPayload draft,
        ReadOnlySpan<byte> actualHomeProtectedKey)
    {
        ArgumentNullException.ThrowIfNull(draft);
        if (!ValidEditorProject(project) || !ValidRecoveryScope(scope) || generation < 1 || !ValidRecoveryPayload(draft))
            throw new ArgumentException("A current saved project, actual task partition and bounded valid draft are required.");
        if (actualHomeProtectedKey.Length != 32)
            throw new ArgumentException("The original protected-key owner must supply a 256-bit key.", nameof(actualHomeProtectedKey));
        // Snapshot caller-owned collections before any serialisation; source writes and review
        // passes remain exclusively with their existing owning services.
        var captured = draft with { Tabs = Array.AsReadOnly(draft.Tabs.ToArray()) };
        byte[] plain = [], associated = [], encrypted = [];
        try
        {
            plain = JsonSerializer.SerializeToUtf8Bytes(captured, JsonOptions);
            if (plain.Length > MaximumDraftRecoveryPlaintextBytes)
                throw new ArgumentOutOfRangeException(nameof(draft), "The complete recovery payload exceeds its bound; no truncated draft is saved.");
            var snapshot = new DeveloperDraftRecoverySnapshot(DraftRecoveryFormatVersion, scope, project.Reference,
                EditorProjectStamp(project), generation, DateTimeOffset.UtcNow, "");
            associated = RecoveryAssociatedData(snapshot);
            encrypted = new byte[plain.Length + RecoveryNonceBytes + RecoveryTagBytes];
            RandomNumberGenerator.Fill(encrypted.AsSpan(0, RecoveryNonceBytes));
            using (var cipher = new AesGcm(actualHomeProtectedKey, RecoveryTagBytes))
                cipher.Encrypt(encrypted.AsSpan(0, RecoveryNonceBytes), plain,
                    encrypted.AsSpan(RecoveryNonceBytes, plain.Length), encrypted.AsSpan(RecoveryNonceBytes + plain.Length), associated);
            return snapshot with { ProtectedPayload = Convert.ToBase64String(encrypted) };
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plain);
            CryptographicOperations.ZeroMemory(associated);
            CryptographicOperations.ZeroMemory(encrypted);
        }
    }

    /// <summary>Pure decryption only after the host has revalidated its actual profile, SAME
    /// Task/Run/context and current project/source authority. All header fields authenticate with
    /// the payload. A different profile/task/project/key or damaged ciphertext cannot disclose it.
    /// Caller must freshly read every source and compare its baseline hash before publishing any
    /// recovered tab. This neither opens an editor nor restores a review/write approval.</summary>
    public static DeveloperDraftRecoveryPayload UnprotectDraftRecovery(DeveloperResolvedProject currentProject,
        DeveloperDraftRecoveryScope actualScope, DeveloperDraftRecoverySnapshot snapshot,
        ReadOnlySpan<byte> actualHomeProtectedKey)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (!ValidEditorProject(currentProject) || !ValidRecoveryScope(actualScope) ||
            snapshot.Scope != actualScope || !SameRecoveryProject(snapshot.ProjectReference, currentProject.Reference) ||
            !StringComparer.Ordinal.Equals(snapshot.ProjectStamp, EditorProjectStamp(currentProject)))
            throw new UnauthorizedAccessException("The recovery snapshot does not match the current project/profile/task partition.");
        if (!ValidRecoverySnapshot(snapshot, currentProject.Reference.WorkspaceId))
            throw new InvalidDataException("The protected recovery snapshot is invalid; its stored bytes were preserved.");
        if (actualHomeProtectedKey.Length != 32)
            throw new ArgumentException("The original protected-key owner must supply a 256-bit key.", nameof(actualHomeProtectedKey));
        byte[] encrypted = [], plain = [], associated = [];
        try
        {
            encrypted = Convert.FromBase64String(snapshot.ProtectedPayload);
            plain = new byte[encrypted.Length - RecoveryNonceBytes - RecoveryTagBytes];
            associated = RecoveryAssociatedData(snapshot);
            using (var cipher = new AesGcm(actualHomeProtectedKey, RecoveryTagBytes))
                cipher.Decrypt(encrypted.AsSpan(0, RecoveryNonceBytes),
                    encrypted.AsSpan(RecoveryNonceBytes, plain.Length), encrypted.AsSpan(RecoveryNonceBytes + plain.Length), plain, associated);
            var draft = JsonSerializer.Deserialize<DeveloperDraftRecoveryPayload>(plain, JsonOptions);
            if (draft is null || !ValidRecoveryPayload(draft))
                throw new InvalidDataException("The protected recovery payload has an invalid shape; no tabs were restored.");
            return draft with { Tabs = Array.AsReadOnly(draft.Tabs.ToArray()) };
        }
        finally
        {
            CryptographicOperations.ZeroMemory(encrypted);
            CryptographicOperations.ZeroMemory(plain);
            CryptographicOperations.ZeroMemory(associated);
        }
    }

    /// <summary>Observes only ciphertext for the exact partition. The actual Home/Files host
    /// must supply its current original actors/source/task observations; copied IDs are no grant.
    /// An unchanged project can recover after an unrelated workspace settings update, while its
    /// original authenticated header stays intact. Missing/foreign partitions disclose no draft.</summary>
    public async Task<DeveloperOperationResult<DeveloperDraftRecoverySnapshot?>> GetDraftRecoveryAsync(
        DeveloperResolvedProject project, DeveloperDraftRecoveryScope actualScope, CancellationToken cancellationToken = default)
    {
        if (!ValidEditorProject(project) || !ValidRecoveryScope(actualScope))
            return EditorFailure<DeveloperDraftRecoverySnapshot?>(DeveloperOperationErrorCode.InvalidInput,
                "A current saved project and actual profile/task partition are required.", project?.Reference.WorkspaceId ?? Guid.Empty);
        try
        {
            var loaded = await ReadEditorDocumentAsync(project.Reference.WorkspaceId, cancellationToken).ConfigureAwait(false);
            if (!loaded.Succeeded) return CopyEditorError<DeveloperDraftRecoverySnapshot?>(loaded.Error!);
            if (!SameEditorWorkspace(loaded.Value!.Workspace, project)) return EditorConflict<DeveloperDraftRecoverySnapshot?>(project.Reference.WorkspaceId);
            var snapshot = (loaded.Value.DraftRecoveries ?? []).SingleOrDefault(value =>
                SameRecoveryKey(value, project.Reference, actualScope));
            if (snapshot is not null && !StringComparer.Ordinal.Equals(snapshot.ProjectStamp, EditorProjectStamp(project)))
                return EditorFailure<DeveloperDraftRecoverySnapshot?>(DeveloperOperationErrorCode.RevisionConflict,
                    "The protected draft belongs to an earlier project/root/repository. It was preserved; reopen current source explicitly.", project.Reference.WorkspaceId);
            return DeveloperOperationResult<DeveloperDraftRecoverySnapshot?>.Success(snapshot);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        { return StorageFailure<DeveloperDraftRecoverySnapshot?>(project.Reference.WorkspaceId, error); }
    }

    /// <summary>Stores opaque authenticated ciphertext through the SAME workspace lock/CAS/
    /// atomic rename as normal saves. Generation is independent of workspace/project authority.
    /// The host must retain its original source/execution reservation and verify actual task and
    /// protected-key admission around this call. This primitive grants neither and never invokes
    /// Files writes. No old snapshot is evicted; an oversized/conflicting save preserves it.</summary>
    public Task<DeveloperOperationResult<DeveloperDraftRecoverySnapshot>> SaveDraftRecoveryAsync(
        DeveloperResolvedProject project, DeveloperDraftRecoverySnapshot snapshot, long expectedGeneration,
        CancellationToken cancellationToken = default)
    {
        if (!ValidEditorProject(project) || snapshot is null || expectedGeneration < 0 ||
            expectedGeneration == long.MaxValue || snapshot.Generation != expectedGeneration + 1 ||
            snapshot.ProjectReference != project.Reference ||
            !StringComparer.Ordinal.Equals(snapshot.ProjectStamp, EditorProjectStamp(project)) ||
            !ValidRecoverySnapshot(snapshot, project.Reference.WorkspaceId))
            return Task.FromResult(EditorFailure<DeveloperDraftRecoverySnapshot>(DeveloperOperationErrorCode.InvalidInput,
                "An exact current project and valid protected next-generation recovery snapshot are required.", project?.Reference.WorkspaceId ?? Guid.Empty));
        return PrepareAndSaveDraftRecoveryAsync(project, snapshot.Scope, expectedGeneration,
            (_, _) => Task.FromResult(snapshot), cancellationToken);
    }

    /// <summary>Trusted-host preparation under the SAME cross-process canonical workspace lock.
    /// The Boolean reports whether this profile already has recovery records and is NOT authority.
    /// It lets the actual OS credential owner refuse missing-key recovery instead of resetting a
    /// key, and prevents racing Get/null/Set from stranding an acknowledged ciphertext. The host
    /// still owns original Home/source/execution admission, all raw child tasks and key lifetime.
    /// This method never invokes a model, writes source, or creates its own credential store.</summary>
    public async Task<DeveloperOperationResult<DeveloperDraftRecoverySnapshot>> PrepareAndSaveDraftRecoveryAsync(
        DeveloperResolvedProject project, DeveloperDraftRecoveryScope actualScope, long expectedGeneration,
        Func<bool, CancellationToken, Task<DeveloperDraftRecoverySnapshot>> prepareWithinOriginalKeyOwner,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(prepareWithinOriginalKeyOwner);
        if (!ValidEditorProject(project) || !ValidRecoveryScope(actualScope) || expectedGeneration < 0 || expectedGeneration == long.MaxValue)
            return EditorFailure<DeveloperDraftRecoverySnapshot>(DeveloperOperationErrorCode.InvalidInput,
                "A current project, actual recovery partition and expected generation are required.", project?.Reference.WorkspaceId ?? Guid.Empty);
        var acquired = await AcquireWriteLockAsync(project.Reference.WorkspaceId, cancellationToken).ConfigureAwait(false);
        if (acquired.Error is not null) return CopyEditorError<DeveloperDraftRecoverySnapshot>(acquired.Error);
        await using var writeLock = acquired.Lock!;
        try
        {
            var loaded = await ReadEditorDocumentAsync(project.Reference.WorkspaceId, cancellationToken).ConfigureAwait(false);
            if (!loaded.Succeeded) return CopyEditorError<DeveloperDraftRecoverySnapshot>(loaded.Error!);
            var document = loaded.Value!;
            if (!SameEditorWorkspace(document.Workspace, project)) return EditorConflict<DeveloperDraftRecoverySnapshot>(project.Reference.WorkspaceId);
            var recoveries = (document.DraftRecoveries ?? []).ToList();
            var old = recoveries.SingleOrDefault(value => SameRecoveryKey(value, project.Reference, actualScope));
            if ((old?.Generation ?? 0) != expectedGeneration ||
                old is not null && !StringComparer.Ordinal.Equals(old.ProjectStamp, EditorProjectStamp(project)))
                return EditorFailure<DeveloperDraftRecoverySnapshot>(DeveloperOperationErrorCode.RevisionConflict,
                    "Another editor changed this recovery generation or project. The saved draft was preserved; reload before retrying.", project.Reference.WorkspaceId);
            if (old is null && recoveries.Count >= MaximumDraftRecoverySnapshots)
                return EditorFailure<DeveloperDraftRecoverySnapshot>(DeveloperOperationErrorCode.CapabilityUnavailable,
                    "The bounded protected recovery catalogue is full. No prior draft was removed.", project.Reference.WorkspaceId);
            cancellationToken.ThrowIfCancellationRequested();
            var snapshot = await prepareWithinOriginalKeyOwner(recoveries.Any(value => value.Scope.ProfileId == actualScope.ProfileId), cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (snapshot is null || snapshot.Scope != actualScope || snapshot.ProjectReference != project.Reference ||
                snapshot.Generation != expectedGeneration + 1 || !StringComparer.Ordinal.Equals(snapshot.ProjectStamp, EditorProjectStamp(project)) ||
                !ValidRecoverySnapshot(snapshot, project.Reference.WorkspaceId))
                return EditorFailure<DeveloperDraftRecoverySnapshot>(DeveloperOperationErrorCode.InvalidInput,
                    "The original key owner did not return the exact protected next-generation snapshot. Prior state was preserved.", project.Reference.WorkspaceId);
            if (old is null) recoveries.Add(snapshot); else recoveries[recoveries.IndexOf(old)] = snapshot;
            var next = document with { DraftRecoveries = Array.AsReadOnly(recoveries.ToArray()) };
            if (JsonSerializer.SerializeToUtf8Bytes(next, JsonOptions).Length > 1024 * 1024)
                return EditorFailure<DeveloperDraftRecoverySnapshot>(DeveloperOperationErrorCode.CapabilityUnavailable,
                    "The protected draft would exceed the native workspace document limit. The prior state was preserved.", project.Reference.WorkspaceId);
            await WriteEditorDocumentAsync(GetWorkspacePath(project.Reference.WorkspaceId), next, cancellationToken).ConfigureAwait(false);
            return DeveloperOperationResult<DeveloperDraftRecoverySnapshot>.Success(snapshot);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        { return StorageFailure<DeveloperDraftRecoverySnapshot>(project.Reference.WorkspaceId, error); }
    }

    private static bool ValidRecoveryScope(DeveloperDraftRecoveryScope? value) => value is not null &&
        value.ProfileId != Guid.Empty && (value.IsManual
            ? value.TaskId == Guid.Empty && value.ExecutionId == Guid.Empty && value.ContextId == Guid.Empty
            : value.TaskId != Guid.Empty && value.ExecutionId != Guid.Empty && value.ContextId != Guid.Empty);
    private static bool SameRecoveryProject(DeveloperProjectReference? left, DeveloperProjectReference right) => left is not null &&
        left.WorkspaceId == right.WorkspaceId && left.ProjectId == right.ProjectId &&
        left.RootId == right.RootId && left.RepositoryBindingId == right.RepositoryBindingId;
    private static bool SameRecoveryKey(DeveloperDraftRecoverySnapshot value, DeveloperProjectReference project,
        DeveloperDraftRecoveryScope scope) => SameRecoveryProject(value.ProjectReference, project) && value.Scope == scope;
    private static bool ValidRecoveryPayload(DeveloperDraftRecoveryPayload value) => value.Tabs is not null &&
        value.Tabs.Count <= DeveloperSourceReviewSession.MaximumOpenDocuments &&
        value.Tabs.All(tab => tab is not null && SafeEditorPath(tab.RelativePath) && tab.DraftText is not null &&
            tab.DraftText.Length <= DeveloperSourceReviewSession.MaximumTextCharacters &&
            tab.OriginalSha256 is { Length: 64 } && tab.OriginalSha256.All(Uri.IsHexDigit)) &&
        value.Tabs.Select(tab => tab.RelativePath).Distinct(StringComparer.Ordinal).Count() == value.Tabs.Count &&
        (value.SelectedRelativePath is null || value.Tabs.Any(tab => StringComparer.Ordinal.Equals(tab.RelativePath, value.SelectedRelativePath)));
    private static bool ValidRecoverySnapshot(DeveloperDraftRecoverySnapshot? value, Guid workspaceId) => value is not null &&
        value.FormatVersion == DraftRecoveryFormatVersion && ValidRecoveryScope(value.Scope) &&
        value.ProjectReference is { WorkspaceRevision: >= 1, ProjectRevision: >= 1 } project &&
        project.WorkspaceId == workspaceId && project.ProjectId != Guid.Empty && project.RootId != Guid.Empty &&
        value.Generation > 0 && value.SavedAtUtc != default && value.SavedAtUtc.Offset == TimeSpan.Zero &&
        value.ProjectStamp is { Length: 64 } && value.ProjectStamp.All(Uri.IsHexDigit) && ValidRecoveryCiphertext(value.ProtectedPayload);
    private static bool ValidRecoveryCiphertext(string? value)
    {
        if (value is null || value.Length > MaximumRecoveryBase64Characters) return false;
        byte[] decoded = [];
        try { decoded = Convert.FromBase64String(value); return decoded.Length > RecoveryNonceBytes + RecoveryTagBytes && decoded.Length <= MaximumRecoveryCiphertextBytes; }
        catch (FormatException) { return false; }
        finally { CryptographicOperations.ZeroMemory(decoded); }
    }
    private static bool ValidDraftRecoveryCatalogue(IReadOnlyList<DeveloperDraftRecoverySnapshot>? values, Guid workspaceId) => values is null ||
        values.Count <= MaximumDraftRecoverySnapshots && values.All(value => ValidRecoverySnapshot(value, workspaceId)) &&
        values.Select(value => (value.Scope, value.ProjectReference.ProjectId, value.ProjectReference.RootId, value.ProjectReference.RepositoryBindingId)).Distinct().Count() == values.Count;
    private static byte[] RecoveryAssociatedData(DeveloperDraftRecoverySnapshot snapshot) =>
        JsonSerializer.SerializeToUtf8Bytes(new
        {
            Domain = "9to1.build.draft-recovery", snapshot.FormatVersion, snapshot.Scope,
            snapshot.ProjectReference, snapshot.ProjectStamp, snapshot.Generation, snapshot.SavedAtUtc
        }, JsonOptions);
}
