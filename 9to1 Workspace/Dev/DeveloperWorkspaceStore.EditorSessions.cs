using System.Text.Json;
using System.Security.Cryptography;

namespace HavenOS.Apps.Dev;

/// <summary>A clean source observation, never a Files identity or permission. No draft text,
/// baseline text, review decision or executable command is persisted in editor-session metadata.</summary>
public sealed record DeveloperCleanEditorTab(string RelativePath, string ContentSha256, bool IsPinned,
    bool IsPreview = false, string GroupId = "default");

/// <summary>Restorable clean presentation state. ProjectReference is the saved observation;
/// only the current configured source owner may rebind and read it. Session generation is a
/// separate optimistic-concurrency token and confers no project or source authority.</summary>
public sealed record DeveloperCleanEditorSession(Guid ProfileId, DeveloperProjectReference ProjectReference,
    string ProjectStamp, long Generation, IReadOnlyList<DeveloperCleanEditorTab> Tabs,
    string? SelectedRelativePath, DateTimeOffset SavedAt);

public sealed partial class FileDeveloperWorkspaceStore
{
    private const int MaximumEditorSessions = 64;

    /// <summary>Metadata only. The native owner supplies the real current project/profile and
    /// must perform fresh source authorisation before any source content is exposed. A matching
    /// unchanged project/root/repository can rebind a session after an unrelated workspace edit.</summary>
    public async Task<DeveloperOperationResult<DeveloperCleanEditorSession?>> GetEditorSessionAsync(
        DeveloperResolvedProject project, Guid profileId, CancellationToken cancellationToken = default)
    {
        if (!ValidEditorProject(project) || profileId == Guid.Empty)
            return EditorFailure<DeveloperCleanEditorSession?>(DeveloperOperationErrorCode.InvalidInput,
                "A current saved project and real Home profile are required.", project?.Reference.WorkspaceId ?? Guid.Empty);
        try
        {
            var loaded = await ReadEditorDocumentAsync(project.Reference.WorkspaceId, cancellationToken).ConfigureAwait(false);
            if (!loaded.Succeeded) return CopyEditorError<DeveloperCleanEditorSession?>(loaded.Error!);
            var document = loaded.Value!;
            if (!SameEditorWorkspace(document.Workspace, project))
                return EditorConflict<DeveloperCleanEditorSession?>(project.Reference.WorkspaceId);
            var saved = (document.EditorSessions ?? []).SingleOrDefault(value => SameEditorKey(value, profileId, project.Reference));
            if (saved is null) return DeveloperOperationResult<DeveloperCleanEditorSession?>.Success(null);
            if (!StringComparer.Ordinal.Equals(saved.ProjectStamp, EditorProjectStamp(project)))
                return EditorFailure<DeveloperCleanEditorSession?>(DeveloperOperationErrorCode.RevisionConflict,
                    "The saved editor session belongs to an earlier project/root/repository. It was preserved; open the current source explicitly.", project.Reference.WorkspaceId);
            return DeveloperOperationResult<DeveloperCleanEditorSession?>.Success(saved with
            {
                ProjectReference = project.Reference,
                Tabs = Array.AsReadOnly(saved.Tabs.ToArray())
            });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        { return StorageFailure<DeveloperCleanEditorSession?>(project.Reference.WorkspaceId, error); }
    }

    /// <summary>CAS update in the SAME canonical workspace document/lock/atomic rename as Save.
    /// This changes clean presentation metadata only. It never edits source, changes authority
    /// revisions, creates Files IDs, serializes drafts, or acknowledges a source mutation.</summary>
    public async Task<DeveloperOperationResult<DeveloperCleanEditorSession>> SaveEditorSessionAsync(
        DeveloperResolvedProject project, Guid profileId, long expectedGeneration,
        IReadOnlyList<DeveloperCleanEditorTab> tabs, string? selectedRelativePath,
        CancellationToken cancellationToken = default)
    {
        if (!ValidEditorProject(project) || profileId == Guid.Empty || expectedGeneration < 0 ||
            expectedGeneration == long.MaxValue || tabs is null)
            return EditorFailure<DeveloperCleanEditorSession>(DeveloperOperationErrorCode.InvalidInput,
                "Select at most eight distinct clean source observations and a valid session generation.", project?.Reference.WorkspaceId ?? Guid.Empty);
        // Detach before awaiting the lock; the caller cannot mutate an admitted queued write.
        var captured = Array.AsReadOnly(tabs.ToArray());
        if (!ValidCleanTabs(captured, selectedRelativePath))
            return EditorFailure<DeveloperCleanEditorSession>(DeveloperOperationErrorCode.InvalidInput,
                "Select at most eight distinct clean source observations and a valid session generation.", project.Reference.WorkspaceId);
        var capturedSelection = selectedRelativePath is null ? null :
            captured.Single(value => EditorPathComparer.Equals(value.RelativePath, selectedRelativePath)).RelativePath;
        var acquired = await AcquireWriteLockAsync(project.Reference.WorkspaceId, cancellationToken).ConfigureAwait(false);
        if (acquired.Error is not null) return CopyEditorError<DeveloperCleanEditorSession>(acquired.Error);
        await using var writeLock = acquired.Lock!;
        try
        {
            var loaded = await ReadEditorDocumentAsync(project.Reference.WorkspaceId, cancellationToken).ConfigureAwait(false);
            if (!loaded.Succeeded) return CopyEditorError<DeveloperCleanEditorSession>(loaded.Error!);
            var document = loaded.Value!;
            if (!SameEditorWorkspace(document.Workspace, project)) return EditorConflict<DeveloperCleanEditorSession>(project.Reference.WorkspaceId);
            var sessions = (document.EditorSessions ?? []).ToList();
            var old = sessions.SingleOrDefault(value => SameEditorKey(value, profileId, project.Reference));
            if ((old?.Generation ?? 0) != expectedGeneration ||
                old is not null && !StringComparer.Ordinal.Equals(old.ProjectStamp, EditorProjectStamp(project)))
                return EditorFailure<DeveloperCleanEditorSession>(DeveloperOperationErrorCode.RevisionConflict,
                    "Editor session state changed in another window or project revision. The saved session was preserved; reopen before saving again.", project.Reference.WorkspaceId);
            if (old is null && sessions.Count >= MaximumEditorSessions)
                return EditorFailure<DeveloperCleanEditorSession>(DeveloperOperationErrorCode.CapabilityUnavailable,
                    "The bounded workspace editor-session catalogue is full; no previous session was removed.", project.Reference.WorkspaceId);
            var next = new DeveloperCleanEditorSession(profileId, project.Reference, EditorProjectStamp(project),
                checked(expectedGeneration + 1), captured, capturedSelection, DateTimeOffset.UtcNow);
            if (old is not null) sessions[sessions.IndexOf(old)] = next; else sessions.Add(next);
            var nextDocument = document with { EditorSessions = Array.AsReadOnly(sessions.ToArray()) };
            // The genuine native project reader has the same finite one-MiB metadata limit.
            // Refuse before replacing an otherwise readable canonical document.
            if (JsonSerializer.SerializeToUtf8Bytes(nextDocument, JsonOptions).Length > 1024 * 1024)
                return EditorFailure<DeveloperCleanEditorSession>(DeveloperOperationErrorCode.CapabilityUnavailable,
                    "The clean session would exceed the native workspace document limit; the previous state was preserved.", project.Reference.WorkspaceId);
            await WriteEditorDocumentAsync(GetWorkspacePath(project.Reference.WorkspaceId), nextDocument, cancellationToken).ConfigureAwait(false);
            return DeveloperOperationResult<DeveloperCleanEditorSession>.Success(next);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or OverflowException)
        { return StorageFailure<DeveloperCleanEditorSession>(project.Reference.WorkspaceId, error); }
    }

    private async Task<DeveloperOperationResult<WorkspaceDocument>> ReadEditorDocumentAsync(Guid workspaceId, CancellationToken token)
    {
        var path = GetWorkspacePath(workspaceId);
        if (!File.Exists(path)) return EditorFailure<WorkspaceDocument>(DeveloperOperationErrorCode.WorkspaceNotFound,
            "The requested workspace does not exist.", workspaceId);
        await using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            16 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var json = await JsonDocument.ParseAsync(input, cancellationToken: token).ConfigureAwait(false);
        var decoded = DecodeOriginalDocument(json.RootElement, workspaceId);
        if (!decoded.Succeeded) return CopyEditorError<WorkspaceDocument>(decoded.Error!);
        var document = json.RootElement.Deserialize<WorkspaceDocument>(JsonOptions)!;
        var sessions = document.EditorSessions;
        if (sessions is not null && (sessions.Count > MaximumEditorSessions ||
            sessions.Any(value => value is null || value.ProfileId == Guid.Empty || value.Generation < 1 ||
                value.SavedAt == default || value.SavedAt.Offset != TimeSpan.Zero ||
                value.ProjectReference is null || value.ProjectReference.WorkspaceId != workspaceId ||
                value.ProjectReference.ProjectId == Guid.Empty || value.ProjectReference.RootId == Guid.Empty ||
                value.ProjectReference.WorkspaceRevision < 1 || value.ProjectReference.ProjectRevision < 1 ||
                value.ProjectStamp is not { Length: 64 } || !value.ProjectStamp.All(Uri.IsHexDigit) ||
                value.Tabs is null || !ValidCleanTabs(value.Tabs, value.SelectedRelativePath)) ||
            sessions.Select(value => (value.ProfileId, value.ProjectReference.ProjectId, value.ProjectReference.RootId,
                value.ProjectReference.RepositoryBindingId)).Distinct().Count() != sessions.Count))
            return EditorFailure<WorkspaceDocument>(DeveloperOperationErrorCode.InvalidStoredData,
                "The saved clean editor-session metadata is invalid and was left unchanged.", workspaceId);
        return DeveloperOperationResult<WorkspaceDocument>.Success(document);
    }

    private static bool ValidCleanTabs(IReadOnlyList<DeveloperCleanEditorTab> tabs, string? selected) =>
        tabs.Count <= DeveloperSourceReviewSession.MaximumOpenDocuments &&
        tabs.All(value => value is not null && SafeEditorPath(value.RelativePath) &&
            value.ContentSha256 is { Length: 64 } && value.ContentSha256.All(Uri.IsHexDigit) &&
            !string.IsNullOrWhiteSpace(value.GroupId)) &&
        tabs.Select(value => value.RelativePath).Distinct(EditorPathComparer).Count() == tabs.Count &&
        (selected is null || tabs.Any(value => EditorPathComparer.Equals(value.RelativePath, selected)));

    private static StringComparer EditorPathComparer => OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    private static bool SafeEditorPath(string path) => !string.IsNullOrWhiteSpace(path) && path.Length <= 2048 &&
        !Path.IsPathRooted(path) && !path.Contains('\\') && !path.Any(value => value < 32 || value == ':') &&
        path.Split('/') is { Length: > 0 and <= 64 } parts && parts.All(value => value is not ("" or "." or ".."));

    private static bool ValidEditorProject(DeveloperResolvedProject? value) => value is not null &&
        value.Reference is not null && value.Workspace is not null && value.Project is not null && value.Root is not null &&
        value.Workspace.Validate() is null && value.Reference.WorkspaceId == value.Workspace.WorkspaceId &&
        value.Reference.WorkspaceRevision == value.Workspace.Revision && value.Reference.ProjectId == value.Project.ProjectId &&
        value.Reference.ProjectRevision == value.Project.Revision && value.Reference.RootId == value.Root.RootId &&
        value.Project.RootIds.Contains(value.Root.RootId) &&
        value.Workspace.Projects.Count(project => project.ProjectId == value.Project.ProjectId && JsonSerializer.Serialize(project) == JsonSerializer.Serialize(value.Project)) == 1 &&
        value.Workspace.Roots.Count(root => root.RootId == value.Root.RootId && JsonSerializer.Serialize(root) == JsonSerializer.Serialize(value.Root)) == 1 &&
        (value.Reference.RepositoryBindingId is null ? value.Repository is null : value.Repository is not null &&
            value.Repository.BindingId == value.Reference.RepositoryBindingId && value.Repository.RootId == value.Root.RootId &&
            value.Workspace.SourceControlBindings.Count(repository => repository.BindingId == value.Repository.BindingId &&
                JsonSerializer.Serialize(repository) == JsonSerializer.Serialize(value.Repository)) == 1);

    private static bool SameEditorWorkspace(DeveloperWorkspace current, DeveloperResolvedProject project) =>
        current.Revision == project.Reference.WorkspaceRevision && JsonSerializer.Serialize(current) == JsonSerializer.Serialize(project.Workspace);
    private static string EditorProjectStamp(DeveloperResolvedProject project) =>
        Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new { project.Project, project.Root, project.Repository })));
    private static bool SameEditorKey(DeveloperCleanEditorSession value, Guid profile, DeveloperProjectReference project) =>
        value.ProfileId == profile && value.ProjectReference.ProjectId == project.ProjectId &&
        value.ProjectReference.RootId == project.RootId && value.ProjectReference.RepositoryBindingId == project.RepositoryBindingId;
    private static DeveloperOperationResult<T> EditorConflict<T>(Guid id) => EditorFailure<T>(DeveloperOperationErrorCode.RevisionConflict,
        "The current canonical workspace differs from this selected revision. Reload the project before changing editor state.", id);
    private static DeveloperOperationResult<T> EditorFailure<T>(DeveloperOperationErrorCode code, string message, Guid id) =>
        DeveloperOperationResult<T>.Failure(code, message, id.ToString("D"), retryable: code == DeveloperOperationErrorCode.RevisionConflict);
    private static DeveloperOperationResult<T> CopyEditorError<T>(DeveloperOperationError error) =>
        DeveloperOperationResult<T>.Failure(error.Code, error.Message, error.TargetId, error.Recoverable, error.Retryable);
}
