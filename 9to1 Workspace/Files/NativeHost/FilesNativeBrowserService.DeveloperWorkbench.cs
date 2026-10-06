using System.Text.Json;
using Haven.Application;
using HavenOS.Apps.Dev;

namespace HavenOS.Files.NativeHost;

public sealed partial class FilesNativeBrowserService
{
    /// <summary>Native Dev host resolver over existing registered canonical documents. It reads
    /// only saved OpenEditors/materializations and genuine authenticated Files metadata/pages.
    /// It creates no root, file identity, mapping, source bytes, task, permission or import.
    /// Supply this method plus DemandExternalOriginalDeveloperReadJoin to the maintained workbench.
    /// The native host keeps the SAME service alive and drains its originals before provider close.</summary>
    public Task<DeveloperOperationResult<DeveloperCodeDocument>> ResolveOriginalDeveloperDocumentAsync(
        DeveloperResolvedProject actualProject, string relativePath, Func<bool> originalLifetime,
        CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(actualProject); ArgumentNullException.ThrowIfNull(originalLifetime);
        var projects = _originalDeveloperProjects ?? throw new InvalidOperationException("The genuine Dev workspace store is not configured.");
        TaskCompletionSource start; DeveloperIdentityOriginal owner; Task<DeveloperOperationResult<DeveloperCodeDocument>> actual;
        lock (_developerIdentityGate)
        {
            if (_developerIdentityRetired) throw new ObjectDisposedException("FilesOriginalDeveloperRead");
            if (_developerIdentityCapacityError is { } sticky) throw new AggregateException("Original Files Dev read custody is full.", sticky);
            _developerIdentityOriginals.RemoveAll(value => value.Task.IsCompletedSuccessfully);
            if (_developerIdentityOriginals.Count >= 128)
                throw _developerIdentityCapacityError = new InvalidOperationException("Original Files Dev read custody reached its 128-reference runtime bound.");
            start = new(TaskCreationOptions.RunContinuationsAsynchronously);
            owner = new() { Parent = _developerIdentityExecuting.Value };
            actual = DriveOriginalDeveloperDocumentAsync(start.Task, owner, projects, actualProject, relativePath, originalLifetime, token);
            owner.Task = actual; _developerIdentityOriginals.Add(owner);
        }
        start.TrySetResult(); return actual;
    }

    private async Task<DeveloperOperationResult<DeveloperCodeDocument>> DriveOriginalDeveloperDocumentAsync(Task start,
        DeveloperIdentityOriginal owner, IDeveloperWorkspaceStore projects, DeveloperResolvedProject selected,
        string relative, Func<bool> actualLifetime, CancellationToken token)
    {
        await start.ConfigureAwait(false); var previous = _developerIdentityExecuting.Value;
        _developerIdentityExecuting.Value = owner; Volatile.Write(ref owner.Live, true);
        try
        {
            var sources = DeveloperIdentityReadSources(owner);
            // This port resolves a bounded saved document, never scans arbitrary source paths.
            if (string.IsNullOrWhiteSpace(relative) || relative.Length > 2048 || relative.Contains('\\') ||
                Path.IsPathFullyQualified(relative) || relative.Split('/') is not { Length: > 0 and <= 10 } parts ||
                parts.Any(value => value is "" or "." or ".." || value.Any(c => c < 32 || c is ':' or '\0')))
                throw new ArgumentException("Select a finite canonical project-relative document.", nameof(relative));
            var lifetime = WrapDeveloperIdentityLifetime(owner, actualLifetime);
            var actor = await ObserveDeveloperIdentityAsync(owner, () => actors.GetCurrentAsync(token).AsTask()).ConfigureAwait(false);
            if (actor is null || actor.AccountId is not null || actor.OrganisationId is not null ||
                !Guid.TryParse(actor.ProfileId, out var profile) || profile == Guid.Empty)
                throw new UnauthorizedAccessException("The real local Home profile is required for the registered Dev workspace.");
            async Task Require(CancellationToken ct)
            {
                ct.ThrowIfCancellationRequested(); var live = lifetime(); DemandDeveloperIdentityCallbackHealth(owner);
                if (!live || await ObserveDeveloperIdentityAsync(owner, () => actors.GetCurrentAsync(ct).AsTask()).ConfigureAwait(false) != actor)
                    throw new UnauthorizedAccessException("The original Dev document observer or Home actor changed.");
                if (!lifetime()) throw new UnauthorizedAccessException("The original Dev document observer retired.");
                DemandDeveloperIdentityCallbackHealth(owner);
            }
            await Require(token).ConfigureAwait(false);
            var workspace = await ObserveDeveloperIdentityAsync(owner, () => RequireOriginalDeveloperWorkspaceAsync(actor, null, sources, token)).ConfigureAwait(false);
            await Require(token).ConfigureAwait(false);
            var saved = await ObserveDeveloperIdentityAsync(owner, () => projects.GetAsync(selected.Reference.WorkspaceId, token)).ConfigureAwait(false);
            await Require(token).ConfigureAwait(false);
            if (!saved.Succeeded || saved.Value is null || saved.Value.Revision != selected.Reference.WorkspaceRevision ||
                JsonSerializer.Serialize(saved.Value) != JsonSerializer.Serialize(selected.Workspace))
                throw new InvalidOperationException("The SAME current saved project must be reopened before selecting its document.");
            var editors = saved.Value.OpenEditors.Where(value => value.ProjectId == selected.Reference.ProjectId).ToArray();
            if (editors.Length is 0 or > 64 || editors.Select(value => value.FileId).Distinct().Count() != editors.Length)
                throw new InvalidDataException("This resolver requires one bounded canonical project document catalogue (maximum 64); it never truncates it.");
            var matches = new List<FilesMaterializedFile>();
            foreach (var editor in editors)
            {
                await Require(token).ConfigureAwait(false);
                if (editor.FileId == Guid.Empty || string.IsNullOrWhiteSpace(editor.CanonicalResourceId))
                    throw new InvalidDataException("A saved project document lacks its canonical Files identity.");
                var mapping = await ObserveDeveloperIdentityAsync(owner, () => workspace.Materializations.GetExistingByItemIdAsync(new(editor.FileId), token)).ConfigureAwait(false);
                await Require(token).ConfigureAwait(false);
                if (mapping is null) continue;
                var observedRelative = Path.GetRelativePath(Path.GetFullPath(selected.Root.Location), Path.GetFullPath(mapping.LocalPath)).Replace('\\', '/');
                if (StringComparer.Ordinal.Equals(observedRelative, relative)) matches.Add(mapping);
            }
            if (matches.Count != 1) throw new InvalidOperationException("No unique existing canonical materialization matches this project-relative document; reviewed registration/import is required.");
            var materialization = matches[0];
            var observed = await ObserveDeveloperIdentityAsync(owner, () => workspace.Provider.GetForOriginalStoreAsync(
                workspace.Configuration.StoreId, materialization.ItemId, token)).ConfigureAwait(false);
            await Require(token).ConfigureAwait(false);
            if (!observed.IsSuccess || observed.Value is not { Kind: HostedItemKind.File, ParentId: { } } row)
                throw new UnauthorizedAccessException("The existing canonical source file has no authenticated parent.");
            await ObserveDeveloperIdentityAsync(owner, () => RequireOriginalDeveloperMetadataAsync(row, actor, workspace.Configuration.StoreId, sources, token)).ConfigureAwait(false);
            FilesWorkspaceDirectoryBinding? binding = null; var next = row.ParentId;
            var seen = new HashSet<Guid>();
            for (var depth = 0; next is { } ancestor && depth < 10; depth++)
            {
                if (!seen.Add(ancestor.Value)) throw new InvalidDataException("The source folder hierarchy is cyclic.");
                await Require(token).ConfigureAwait(false);
                var candidate = await ObserveDeveloperIdentityAsync(owner, () => workspace.Directories.ResolveOriginalRegisteredFolderAsync(
                    profile, "dev.project." + selected.Reference.ProjectId.ToString("N"), ancestor, workspace.Provider,
                    workspace.Configuration.StoreId, false, ct => new ValueTask(Require(ct)), token)).ConfigureAwait(false);
                await Require(token).ConfigureAwait(false);
                if (candidate.IsSuccess && candidate.Value is { } current)
                {
                    if (!SameDeveloperPath(current.DirectoryPath, selected.Root.Location))
                        throw new UnauthorizedAccessException("The genuine registered Dev root differs from the selected saved root.");
                    binding = current; break;
                }
                var parent = await ObserveDeveloperIdentityAsync(owner, () => workspace.Provider.GetForOriginalStoreAsync(
                    workspace.Configuration.StoreId, ancestor, token)).ConfigureAwait(false);
                await Require(token).ConfigureAwait(false);
                if (!parent.IsSuccess || parent.Value is not { Kind: HostedItemKind.Folder } folder)
                    throw new UnauthorizedAccessException("The original canonical source ancestry is unavailable.");
                await ObserveDeveloperIdentityAsync(owner, () => RequireOriginalDeveloperMetadataAsync(folder, actor, workspace.Configuration.StoreId, sources, token)).ConfigureAwait(false);
                next = folder.ParentId;
            }
            if (binding is null) throw new UnauthorizedAccessException("The bounded original source ancestry does not reach its registered Dev root.");
            FilesNativeBrowserPage? page = null; HostedItemMetadata? originalRow = null;
            FilesNativeBrowserCursor? cursor = null;
            for (var count = 0; count < 16; count++)
            {
                await Require(token).ConfigureAwait(false);
                page = await ObserveDeveloperIdentityAsync(owner, () => ListOriginalDeveloperAsync(actor, sources, row.ParentId!.Value.Value, "", cursor, token,
                    workspace.Configuration.StoreId)).ConfigureAwait(false);
                await Require(token).ConfigureAwait(false);
                var exact = page.Items.Where(value => value.Id == materialization.ItemId).Take(2).ToArray();
                if (exact.Length > 1) throw new InvalidDataException("The genuine Files page contains duplicate source identities.");
                if (exact.Length == 1) { originalRow = exact[0]; break; }
                cursor = page.Next; if (cursor is null) break;
            }
            if (page is null || originalRow is null || originalRow != row)
                throw new InvalidOperationException("The canonical source changed or was not found within the documented 16-page navigation bound; no truncated success is returned.");
            // SAME already-admitted factory original consumes the maintained body; retirement
            // cannot require a second public admission after sealing, or drop the acquired page.
            return await ObserveDeveloperIdentityAsync(owner, () => ResolveDeveloperIdentityBodyAsync(owner, page, originalRow,
                actor, selected, binding, projects, relative, lifetime, token)).ConfigureAwait(false);
        }
        finally { Volatile.Write(ref owner.Live, false); _developerIdentityExecuting.Value = previous; }
    }
}
