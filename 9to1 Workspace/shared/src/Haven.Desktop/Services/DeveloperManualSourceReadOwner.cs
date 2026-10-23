using System.IO;
using System.Text;
using Haven.Application;
using HavenOS.Apps.Dev;
using HavenOS.Files;
using HavenOS.Files.NativeHost;

namespace Haven.Desktop.Services;

/// <summary>Complete freshly authorised clean content for this restore only; never durable text.</summary>
public sealed record DeveloperRestoredCleanEditorTab(DeveloperCleanEditorTab Observation, string Text);
public sealed record DeveloperRestoredCleanEditorSession(DeveloperCleanEditorSession Session,
    IReadOnlyList<DeveloperRestoredCleanEditorTab> Tabs);

/// <summary>Observed entries within an actually captured project, never Files IDs or grants.</summary>
public sealed record DeveloperProjectTreeEntry(string RelativePath, bool IsDirectory, long? SizeBytes)
{
    public string SelectionKey => (IsDirectory ? "folder:" : "file:") + RelativePath;
    public string Name => RelativePath[(RelativePath.LastIndexOf('/') + 1)..];
    public string ParentPath
    {
        get { var separator = RelativePath.LastIndexOf('/'); return separator >= 0 ? RelativePath[..separator] : ""; }
    }
}

/// <summary>
/// Genuine manual (no-AI) source-read owner over a resolved saved project. It composes the REAL
/// configured Files selection, Home read-admission broker and kernel capture owners: every read
/// selects the SAME existing project (strictly beneath the configured Files root), acquires the
/// actual Home READ admission, captures under the kernel owner, and reads captured text through
/// <see cref="IDeveloperProjectOriginalReadAdmission.RunOriginalRead"/> on the retained kernel
/// handle — never raw <c>File</c>/<c>Directory</c> IO. Tree/search use the genuine captured manifest;
/// file text uses the kernel one-file read surface. No process execution, restore/build, terminal or
/// inference occurs here.
/// </summary>
public sealed class DeveloperManualSourceReadOwner(
    NativeFilesWorkspaceService files,
    FilesDeveloperOriginalSourceSelection selections,
    IDeveloperProjectOriginalReadAdmissionSource reads,
    IDeveloperProjectOriginalPhysicalCaptureSource capture)
{
    private const int MaximumSearchResults = 200;

    /// <summary>The genuine manual READ lease holds the SAME issued selection, admission and kernel
    /// capture for the whole read. Disposing it closes the exact Home read admission (joining its raw
    /// child tasks and audit); the Files selection and kernel capture drain with their global issuing
    /// owners at host close — this owner never retires shared owner providers.</summary>
    private sealed record ManualReadLease(IDeveloperProjectOriginalReadSelection Selection,
        IDeveloperProjectOriginalReadAdmission Admission, IDeveloperProjectOriginalExistingSourceCapture Capture)
        : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => Admission.DisposeAsync();
    }

    /// <summary>Actual metadata from the same issued Home/Files/kernel source capture. These
    /// entries are navigation observations, not durable Files IDs or a read/write grant.</summary>
    public async Task<IReadOnlyList<DeveloperProjectTreeEntry>> ListEntriesAsync(DeveloperResolvedProject project, CancellationToken token)
    {
        await using var lease = await AcquireAsync(project, token).ConfigureAwait(false);
        var entries = new List<DeveloperProjectTreeEntry>();
        foreach (var folder in lease.Capture.OriginalFolderPaths)
        {
            var relative = folder.Replace('\\', '/').TrimEnd('/');
            if (relative is "" or ".") continue;
            DemandSafeProjectRelativePath(relative); entries.Add(new(relative, true, null));
        }
        foreach (var file in lease.Capture.OriginalFiles)
        {
            var relative = file.RelativePath.Replace('\\', '/');
            DemandSafeProjectRelativePath(relative);
            if (file.SizeBytes < 0) throw new InvalidDataException("The captured project file has an invalid length.");
            entries.Add(new(relative, false, file.SizeBytes));
        }
        if (entries.Select(value => value.RelativePath).Distinct(StringComparer.Ordinal).Count() != entries.Count)
            throw new InvalidDataException("The captured project tree contains ambiguous paths.");
        return entries.OrderBy(value => value.RelativePath, StringComparer.Ordinal).ToArray();
    }

    public async Task<string> ListAsync(DeveloperResolvedProject project, CancellationToken token)
    {
        await using var lease = await AcquireAsync(project, token).ConfigureAwait(false);
        var sb = new StringBuilder();
        foreach (var folder in lease.Capture.OriginalFolderPaths.Order(StringComparer.Ordinal))
            sb.Append(folder).Append('/').Append('\n');
        foreach (var file in lease.Capture.OriginalFiles.OrderBy(value => value.RelativePath, StringComparer.Ordinal))
            sb.Append(file.RelativePath).Append('\n');
        return sb.Length == 0 ? "(empty project folder)" : sb.ToString().TrimEnd();
    }

    public async Task<string> SearchAsync(DeveloperResolvedProject project, string query, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(query)) throw new ArgumentException("A literal search query is required.", nameof(query));
        if (query.Length > 4000) throw new ArgumentOutOfRangeException(nameof(query));
        await using var lease = await AcquireAsync(project, token).ConfigureAwait(false);
        var sb = new StringBuilder(); var count = 0;
        foreach (var file in lease.Capture.OriginalFiles.OrderBy(value => value.RelativePath, StringComparer.Ordinal))
        {
            if (count >= MaximumSearchResults) break;
            var text = await capture.ReadOriginalCapturedFileTextAsync(lease.Capture, lease.Admission,
                file.RelativePath, token).ConfigureAwait(false);
            if (text is null) continue;
            var lineNumber = 0;
            using var reader = new StringReader(text);
            while (reader.ReadLine() is { } current)
            {
                lineNumber++;
                if (!current.Contains(query, StringComparison.Ordinal)) continue;
                var row = $"{file.RelativePath}:{lineNumber}:{current}";
                if (row.Length > 4096) row = row[..4096];
                sb.Append(row).Append('\n'); count++;
                if (count >= MaximumSearchResults) break;
            }
        }
        return count == 0 ? "(no literal matches)" : sb.ToString().TrimEnd();
    }

    public async Task<string> ReadFileAsync(DeveloperResolvedProject project, string relativePath, CancellationToken token)
    {
        DemandSafeProjectRelativePath(relativePath);
        await using var lease = await AcquireAsync(project, token).ConfigureAwait(false);
        var text = await capture.ReadOriginalCapturedFileTextAsync(lease.Capture, lease.Admission,
            relativePath, token).ConfigureAwait(false)
            ?? throw new FileNotFoundException("The requested source file is not part of the genuine saved-project capture.", relativePath);
        return text;
    }

    /// <summary>Loads metadata from the SAME store recognised by a privately issued registered
    /// selection, then freshly reads every file through its actual Home/kernel admission. Paths
    /// and hashes are comparisons, never grants. Any missing, changed or denied file refuses the
    /// entire restore, preserving the prior session rather than returning a truncated success.</summary>
    public async Task<DeveloperOperationResult<DeveloperRestoredCleanEditorSession?>> RestoreCleanEditorSessionAsync(
        FileDeveloperWorkspaceStore store, DeveloperResolvedProject project, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(store);
        try
        {
            await using var lease = await AcquireAsync(project, token).ConfigureAwait(false);
            var profile = DemandEditorSessionStore(store, lease);
            var loaded = await store.GetEditorSessionAsync(project, profile, token).ConfigureAwait(false);
            if (!loaded.Succeeded) return SessionError<DeveloperRestoredCleanEditorSession?>(loaded.Error!);
            if (loaded.Value is not { } session)
                return DeveloperOperationResult<DeveloperRestoredCleanEditorSession?>.Success(null);
            var tabs = new List<DeveloperRestoredCleanEditorTab>();
            foreach (var tab in session.Tabs)
            {
                var text = await capture.ReadOriginalCapturedFileTextAsync(lease.Capture, lease.Admission,
                    tab.RelativePath, token).ConfigureAwait(false);
                if (text is null || !StringComparer.OrdinalIgnoreCase.Equals(DeveloperManualSourceEditOwner.Sha256(text), tab.ContentSha256))
                    return DeveloperOperationResult<DeveloperRestoredCleanEditorSession?>.Failure(
                        DeveloperOperationErrorCode.RevisionConflict,
                        "A saved tab is missing or changed. The previous session was preserved; open current project files explicitly.", project.Reference.ProjectId.ToString("D"));
                tabs.Add(new(tab, text));
            }
            await lease.Admission.RevalidateOriginalAsync(token).ConfigureAwait(false);
            return DeveloperOperationResult<DeveloperRestoredCleanEditorSession?>.Success(new(session, Array.AsReadOnly(tabs.ToArray())));
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception error) when (error is UnauthorizedAccessException or IOException or InvalidOperationException or AggregateException)
        {
            // The actual configured owners retain their original read/close failures. Returning
            // a structured refused restoration keeps ordinary manual open usable; no source text
            // from a failed capture is returned and the prior canonical session is not rewritten.
            return DeveloperOperationResult<DeveloperRestoredCleanEditorSession?>.Failure(
                error is UnauthorizedAccessException ? DeveloperOperationErrorCode.PermissionRequired :
                    error is IOException or AggregateException ? DeveloperOperationErrorCode.StorageUnavailable : DeveloperOperationErrorCode.RevisionConflict,
                "Clean tab restoration could not obtain a current authorised source observation. Open files explicitly; the previous session remains saved.",
                project.Reference.ProjectId.ToString("D"));
        }
    }

    /// <summary>Re-observes the complete bounded clean session under real READ, closes that
    /// observation, then CAS-updates its presentation metadata in the canonical workspace. No
    /// draft text leaves the page, and no source write or project revision change occurs.</summary>
    public async Task<DeveloperOperationResult<DeveloperCleanEditorSession>> SaveCleanEditorSessionAsync(
        FileDeveloperWorkspaceStore store, FilesDeveloperOriginalCurrentProjectExecutionBridge? execution,
        DeveloperResolvedProject project, long expectedGeneration,
        IReadOnlyList<DeveloperCleanEditorTab> cleanTabs, string? selectedRelativePath, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(store); ArgumentNullException.ThrowIfNull(cleanTabs);
        if (execution is null)
            return DeveloperOperationResult<DeveloperCleanEditorSession>.Failure(DeveloperOperationErrorCode.CapabilityUnavailable,
                "The genuine command/pin reservation owner is unavailable. Clean tab state was not changed.", project.Reference.ProjectId.ToString("D"));
        if (cleanTabs.Count > DeveloperSourceReviewSession.MaximumOpenDocuments)
            throw new ArgumentOutOfRangeException(nameof(cleanTabs));
        var tabs = Array.AsReadOnly(cleanTabs.ToArray());
        IDisposable? reservation = null;
        try
        {
            Guid profile; IDeveloperProjectOriginalReadSelection selection; AuthenticatedResourceActor actor;
            await using (var lease = await AcquireAsync(project, token).ConfigureAwait(false))
            {
                profile = DemandEditorSessionStore(store, lease);
                selection = lease.Selection;
                actor = selections.GetOriginalRegisteredDescriptor(selection).OriginalActor;
                // Actual admission fence precedes the physical metadata write and is held across
                // every await, including exact READ close and the canonical CAS/atomic rename.
                reservation = execution.AcquireOriginalEditorSessionUpdate(project.Reference.WorkspaceId, actor);
                foreach (var tab in tabs)
                {
                    DemandSafeProjectRelativePath(tab.RelativePath);
                    var text = await capture.ReadOriginalCapturedFileTextAsync(lease.Capture, lease.Admission,
                        tab.RelativePath, token).ConfigureAwait(false);
                    if (text is null || !StringComparer.OrdinalIgnoreCase.Equals(DeveloperManualSourceEditOwner.Sha256(text), tab.ContentSha256))
                        return DeveloperOperationResult<DeveloperCleanEditorSession>.Failure(DeveloperOperationErrorCode.RevisionConflict,
                            "A clean tab changed before its session could be saved. The previous session was preserved.", project.Reference.ProjectId.ToString("D"));
                }
                await lease.Admission.RevalidateOriginalAsync(token).ConfigureAwait(false);
            }
            // Do not mutate the workspace document while this metadata-read witness is held.
            await selections.RevalidateOriginalAsync(selection, actor, token).ConfigureAwait(false);
            return await store.SaveEditorSessionAsync(project, profile, expectedGeneration, tabs, selectedRelativePath, token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception error) when (error is UnauthorizedAccessException or IOException or InvalidOperationException or AggregateException)
        {
            var code = error is UnauthorizedAccessException ? DeveloperOperationErrorCode.PermissionRequired :
                error is IOException or AggregateException ? DeveloperOperationErrorCode.StorageUnavailable : DeveloperOperationErrorCode.RevisionConflict;
            return DeveloperOperationResult<DeveloperCleanEditorSession>.Failure(code,
                "Clean tab state could not be saved under the current source/execution owners. Its previous session was preserved; reopen the project to retry.",
                project.Reference.ProjectId.ToString("D"));
        }
        finally { reservation?.Dispose(); }
    }

    private Guid DemandEditorSessionStore(FileDeveloperWorkspaceStore store, ManualReadLease lease)
    {
        var descriptor = selections.GetOriginalRegisteredDescriptor(lease.Selection);
        if (!selections.IsIssuedOriginalRegisteredDescriptor(lease.Selection, descriptor) ||
            !ReferenceEquals(descriptor.OriginalStore, store) || descriptor.OriginalActor.AccountId is not null ||
            descriptor.OriginalActor.OrganisationId is not null ||
            !Guid.TryParse(descriptor.OriginalActor.ProfileId, out var profile) || profile == Guid.Empty)
            throw new UnauthorizedAccessException("The actual registered source/profile must recognise the SAME canonical editor-session store.");
        return profile;
    }

    private static DeveloperOperationResult<T> SessionError<T>(DeveloperOperationError error) =>
        DeveloperOperationResult<T>.Failure(error.Code, error.Message, error.TargetId, error.Recoverable, error.Retryable);

    private async Task<ManualReadLease> AcquireAsync(DeveloperResolvedProject project, CancellationToken token)
    {
        var configuration = await files.GetConfigurationAsync(token).ConfigureAwait(false)
            ?? throw new UnauthorizedAccessException("The original configured Files workspace is unavailable.");
        var selection = await selections.SelectOriginalRegisteredProjectAsync(configuration.StoreId, project, token).ConfigureAwait(false);
        var admission = await reads.AcquireOriginalAsync(selection, token).ConfigureAwait(false);
        try
        {
            var capture = await selections.CaptureOriginalAsync(selection, admission, token).ConfigureAwait(false);
            return new(selection, admission, capture);
        }
        catch (Exception error)
        {
            // Close the SAME acquired admission before surfacing the acquisition fault. Preserve
            // BOTH the original acquisition fault and any close fault; never let a close fault
            // replace or swallow the acquisition failure.
            try { await admission.DisposeAsync().ConfigureAwait(false); }
            catch (Exception disposeError)
            {
                throw new AggregateException("Manual source acquisition failed and its genuine READ admission did not close cleanly.", error, disposeError);
            }
            throw;
        }
    }

    private static void DemandSafeProjectRelativePath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (Path.IsPathRooted(path) || path.Length > 2048 || path.IndexOf('\0') >= 0 ||
            path.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries).Any(value => value is "." or ".."))
            throw new ArgumentException("A safe project-relative source path is required.", nameof(path));
    }
}
