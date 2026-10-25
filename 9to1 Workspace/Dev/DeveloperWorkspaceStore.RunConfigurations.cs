using System.Security.Cryptography;
using System.Text.Json;

namespace HavenOS.Apps.Dev;

public sealed record DeveloperRunConfigurationEditResult(DeveloperResolvedProject Project,
    DeveloperRunConfiguration Configuration, DeveloperDraftRecoveryScope Scope, long RecoveryGeneration,
    long? CleanSessionGeneration, bool MigratedRecovery);

public sealed partial class FileDeveloperWorkspaceStore
{
    /// <summary>One reviewed Run declaration and its compatible recovery/layout migration under
    /// the SAME cross-process lock and atomic document writer. Only the actual manual partition's
    /// protected key may enter. A foreign/old affected recovery refuses the entire update. No
    /// plaintext fallback, separate store, source write or execution grant is manufactured.</summary>
    public async Task<DeveloperOperationResult<DeveloperRunConfigurationEditResult>> SaveReviewedRunConfigurationAsync(
        DeveloperResolvedProject project, DeveloperRunConfigurationEditReview reviewed, DeveloperDraftRecoveryScope actualManualScope,
        long expectedRecoveryGeneration, Func<CancellationToken, Task<byte[]>> readRequiredOriginalProtectedKey,
        Func<CancellationToken, Task> revalidateOriginalSource, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(reviewed); ArgumentNullException.ThrowIfNull(readRequiredOriginalProtectedKey);
        ArgumentNullException.ThrowIfNull(revalidateOriginalSource);
        if (!ValidEditorProject(project) || !ValidRecoveryScope(actualManualScope) || !actualManualScope.IsManual || expectedRecoveryGeneration < 0 ||
            !DeveloperRunConfigurationEdit.IsCurrent(project, reviewed))
            return EditorFailure<DeveloperRunConfigurationEditResult>(DeveloperOperationErrorCode.InvalidInput,
                "Review the exact current saved project and console declaration before saving metadata.", project.Reference.WorkspaceId);
        var acquired = await AcquireWriteLockAsync(project.Reference.WorkspaceId, token).ConfigureAwait(false);
        if (acquired.Error is not null) return CopyEditorError<DeveloperRunConfigurationEditResult>(acquired.Error);
        await using var writeLock = acquired.Lock!;
        try
        {
            var loaded = await ReadEditorDocumentAsync(project.Reference.WorkspaceId, token).ConfigureAwait(false);
            if (!loaded.Succeeded) return CopyEditorError<DeveloperRunConfigurationEditResult>(loaded.Error!);
            var document = loaded.Value!;
            if (!SameEditorWorkspace(document.Workspace, project) || !DeveloperRunConfigurationEdit.IsCurrent(project, reviewed))
                return EditorConflict<DeveloperRunConfigurationEditResult>(project.Reference.WorkspaceId);
            // Derive the write from this privately deserialized canonical document, not
            // caller-owned IReadOnlyList implementations that could change during key IO.
            var canonical = new DeveloperResolvedProject(project.Reference, document.Workspace,
                document.Workspace.Projects.Single(value => value.ProjectId == project.Reference.ProjectId),
                document.Workspace.Roots.Single(value => value.RootId == project.Reference.RootId),
                project.Reference.RepositoryBindingId is { } repositoryId
                    ? document.Workspace.SourceControlBindings.Single(value => value.BindingId == repositoryId) : null);
            if (!DeveloperRunConfigurationEdit.IsCurrent(canonical, reviewed))
                return EditorConflict<DeveloperRunConfigurationEditResult>(project.Reference.WorkspaceId);
            var recoveries = (document.DraftRecoveries ?? []).ToArray();
            var affected = recoveries.Where(value => value.ProjectReference.ProjectId == project.Reference.ProjectId).ToArray();
            if (affected.Any(value => !SameRecoveryKey(value, project.Reference, actualManualScope) ||
                value.ProjectStamp != EditorProjectStamp(canonical)))
                return EditorFailure<DeveloperRunConfigurationEditResult>(DeveloperOperationErrorCode.RevisionConflict,
                    "Another profile, Task, root, repository or earlier project has protected drafts here. No declaration or recovery was changed. Open that recovery in its owning context before changing this project's targets.", project.Reference.WorkspaceId);
            var old = affected.SingleOrDefault();
            if ((old?.Generation ?? 0) != expectedRecoveryGeneration || old?.Generation == long.MaxValue)
                return EditorFailure<DeveloperRunConfigurationEditResult>(DeveloperOperationErrorCode.RevisionConflict,
                    "Protected recovery changed after this page observed it. The exact prior document was preserved; reopen before saving the target.", project.Reference.WorkspaceId);
            await revalidateOriginalSource(token).ConfigureAwait(false);
            var configuration = DeveloperRunConfigurationSelection.Capture(reviewed.Configuration);
            var ids = canonical.Project.RunConfigurationIds.Contains(configuration.Id, StringComparer.Ordinal)
                ? canonical.Project.RunConfigurationIds.ToArray() : canonical.Project.RunConfigurationIds.Append(configuration.Id).ToArray();
            var nextProject = canonical.Project with { RunConfigurationIds = Array.AsReadOnly(ids), Revision = checked(canonical.Project.Revision + 1) };
            var declarations = canonical.Workspace.RunConfigurations.ToList(); var position = declarations.FindIndex(value => value.Id == configuration.Id);
            if (position < 0) declarations.Add(configuration); else declarations[position] = configuration;
            var nextWorkspace = canonical.Workspace with { Revision = checked(canonical.Workspace.Revision + 1), ModifiedAt = DateTimeOffset.UtcNow,
                Projects = Array.AsReadOnly(canonical.Workspace.Projects.Select(value => value.ProjectId == nextProject.ProjectId ? nextProject : value).ToArray()),
                RunConfigurations = Array.AsReadOnly(declarations.ToArray()) };
            var nextReference = project.Reference with { WorkspaceRevision = nextWorkspace.Revision, ProjectRevision = nextProject.Revision };
            var next = new DeveloperResolvedProject(nextReference, nextWorkspace, nextProject, canonical.Root, canonical.Repository);
            if (!ValidEditorProject(next) || nextWorkspace.Validate() is { })
                return EditorFailure<DeveloperRunConfigurationEditResult>(DeveloperOperationErrorCode.InvalidInput,
                    "The reviewed console target did not produce valid canonical project metadata.", project.Reference.WorkspaceId);
            DeveloperDraftRecoverySnapshot? migrated = null;
            if (old is not null)
            {
                byte[] key = [];
                try
                {
                    key = await readRequiredOriginalProtectedKey(token).ConfigureAwait(false);
                    var payload = UnprotectDraftRecovery(canonical, actualManualScope, old, key);
                    migrated = ProtectDraftRecovery(next, actualManualScope, checked(old.Generation + 1), payload, key);
                    // Keep all original payload fields. Only authenticated header, fresh nonce and
                    // generation change. Existing strict helpers authenticate the old AAD intact.
                    recoveries[Array.IndexOf(recoveries, old)] = migrated;
                }
                finally { CryptographicOperations.ZeroMemory(key); }
            }
            var sessions = (document.EditorSessions ?? []).Select(session => MigrateRunEditSession(canonical, next, session)).ToArray();
            var updated = document with { Workspace = nextWorkspace, EditorSessions = Array.AsReadOnly(sessions),
                DraftRecoveries = Array.AsReadOnly(recoveries) };
            if (JsonSerializer.SerializeToUtf8Bytes(updated, JsonOptions).Length > 1024 * 1024)
                return EditorFailure<DeveloperRunConfigurationEditResult>(DeveloperOperationErrorCode.CapabilityUnavailable,
                    "The complete target and migrated recovery exceed the native document bound. No prior state was removed.", project.Reference.WorkspaceId);
            token.ThrowIfCancellationRequested(); await revalidateOriginalSource(token).ConfigureAwait(false);
            // Preserve exact CAS semantics even if a foreign/raw writer ignored our lock during
            // key-owner I/O: compare the actual full document again before its atomic replacement.
            var current = await ReadEditorDocumentAsync(project.Reference.WorkspaceId, token).ConfigureAwait(false);
            if (!current.Succeeded || JsonSerializer.Serialize(current.Value, JsonOptions) != JsonSerializer.Serialize(document, JsonOptions))
                return EditorConflict<DeveloperRunConfigurationEditResult>(project.Reference.WorkspaceId);
            await WriteEditorDocumentAsync(GetWorkspacePath(project.Reference.WorkspaceId), updated, token).ConfigureAwait(false);
            var clean = sessions.SingleOrDefault(value => SameEditorKey(value, actualManualScope.ProfileId, nextReference));
            return DeveloperOperationResult<DeveloperRunConfigurationEditResult>.Success(new(next, configuration, actualManualScope,
                migrated?.Generation ?? 0, clean?.Generation, migrated is not null));
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception error) when (error is CryptographicException or FormatException or InvalidDataException)
        {
            return EditorFailure<DeveloperRunConfigurationEditResult>(DeveloperOperationErrorCode.StorageUnavailable,
                "The original protected recovery key/header/payload could not be verified. No target or recovery was replaced; keep the live draft and recover its original context/key before retrying.", project.Reference.WorkspaceId);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or CryptographicException or FormatException or ArgumentException or OverflowException)
        { return StorageFailure<DeveloperRunConfigurationEditResult>(project.Reference.WorkspaceId, error); }
    }

    private static DeveloperCleanEditorSession MigrateRunEditSession(DeveloperResolvedProject before, DeveloperResolvedProject after,
        DeveloperCleanEditorSession session)
    {
        if (session.ProjectReference.ProjectId != before.Project.ProjectId) return session;
        var root = before.Workspace.Roots.SingleOrDefault(value => value.RootId == session.ProjectReference.RootId);
        var repository = session.ProjectReference.RepositoryBindingId is { } id
            ? before.Workspace.SourceControlBindings.SingleOrDefault(value => value.BindingId == id && value.RootId == session.ProjectReference.RootId) : null;
        if (root is null || !before.Project.RootIds.Contains(root.RootId) ||
            session.ProjectReference.RepositoryBindingId is not null && repository is null) return session;
        var original = before with { Reference = before.Reference with { RootId = root.RootId, RepositoryBindingId = session.ProjectReference.RepositoryBindingId }, Root = root, Repository = repository };
        if (session.ProjectStamp != EditorProjectStamp(original)) return session; // Already-stale layout stays preserved and unadopted.
        var current = after with { Reference = after.Reference with { RootId = root.RootId, RepositoryBindingId = session.ProjectReference.RepositoryBindingId }, Root = root, Repository = repository };
        return session with { ProjectReference = current.Reference, ProjectStamp = EditorProjectStamp(current),
            Generation = checked(session.Generation + 1), SavedAt = DateTimeOffset.UtcNow,
            Tabs = Array.AsReadOnly(session.Tabs.ToArray()) };
    }
}
