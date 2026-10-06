using System.Text.Json;
using HavenOS.Apps.Dev;
using Haven.Application;
using Haven.Application.Compatibility;

namespace HavenOS.Files.NativeHost;

/// <summary>Original Files selection to existing Dev identity metadata only. It neither reads
/// source bytes nor lends Files read permission to a Dev command. Current Task/actor/model/
/// physical authority remains the separate maintained Dev action owner.</summary>
public sealed partial class FilesNativeBrowserService
{
    private readonly IDeveloperWorkspaceStore? _originalDeveloperProjects;
    public FilesNativeBrowserService(NativeFilesWorkspaceAuthority workspaces, IAuthenticatedResourceActorSource actors,
        ResourceAuthorizationService resources, ICompatibilityPackageContentSource packages,
        FilesOriginalChildFolderReadSource originalFolders, IDeveloperWorkspaceStore originalDeveloperProjects)
        : this(workspaces, actors, resources, packages, originalFolders)
    { _originalDeveloperProjects = originalDeveloperProjects ?? throw new ArgumentNullException(nameof(originalDeveloperProjects)); }

    private sealed class DeveloperIdentityOriginal
    {
        internal Task<DeveloperOperationResult<DeveloperCodeDocument>> Task = null!;
        internal readonly List<Task> Sources = [];
        internal readonly List<Exception> CallbackCauses = [];
        internal DeveloperIdentityOriginal? Parent;
        internal bool Live;
    }
    private readonly object _developerIdentityGate = new();
    private readonly List<DeveloperIdentityOriginal> _developerIdentityOriginals = [];
    private readonly AsyncLocal<DeveloperIdentityOriginal?> _developerIdentityExecuting = new();
    [ThreadStatic] private static List<FilesNativeBrowserService>? _developerIdentityCallbacks;
    private bool _developerIdentityRetired;
    private Task? _developerIdentityClose;
    private Exception? _developerIdentityCapacityError;

    /// <summary>The host retains this exact partial's close before provider teardown. Other
    /// Files/browser borrowers are separate owners and are not certified by this close.</summary>
    public void RequestOriginalDeveloperReadRetirement()
    { lock (_developerIdentityGate) _developerIdentityRetired = true; }
    public void DemandExternalOriginalDeveloperReadJoin()
    {
        if (_developerIdentityCallbacks?.Any(value => ReferenceEquals(value, this)) == true)
            throw new InvalidOperationException("An actual Files Dev identity callback cannot join its encompassing read.");
        for (var value = _developerIdentityExecuting.Value; value is not null; value = value.Parent)
            if (Volatile.Read(ref value.Live)) throw new InvalidOperationException("An original Files Dev identity read must return before external drain.");
    }
    public Task CloseOriginalDeveloperReadsAsync()
    {
        DemandExternalOriginalDeveloperReadJoin();
        TaskCompletionSource start; Task actual; DeveloperIdentityOriginal[] owners;
        lock (_developerIdentityGate)
        {
            if (_developerIdentityClose is not null) return _developerIdentityClose;
            _developerIdentityRetired = true; owners = _developerIdentityOriginals.ToArray();
            start = new(TaskCreationOptions.RunContinuationsAsynchronously);
            actual = DrainDeveloperIdentityAsync(start.Task, owners); _developerIdentityClose = actual;
        }
        start.TrySetResult(); return actual;
    }
    private async Task DrainDeveloperIdentityAsync(Task start, DeveloperIdentityOriginal[] owners)
    {
        await start.ConfigureAwait(false); var errors = new List<Exception>();
        if (_developerIdentityCapacityError is { } refusal) errors.Add(refusal);
        foreach (var owner in owners)
        {
            try { await owner.Task.ConfigureAwait(false); } catch (Exception error) { AddDeveloperIdentityCauses(errors, owner.Task, error); }
            Task[] sources; lock (_developerIdentityGate)
            {
                sources = owner.Sources.ToArray();
                foreach (var cause in owner.CallbackCauses)
                    if (!errors.Any(value => ReferenceEquals(value, cause))) errors.Add(cause);
            }
            foreach (var source in sources)
                try { await source.ConfigureAwait(false); } catch (Exception error) { AddDeveloperIdentityCauses(errors, source, error); }
        }
        if (errors.Count != 0) throw new AggregateException("Original Files Dev identity reads did not drain cleanly.", errors);
    }
    private static void AddDeveloperIdentityCauses(List<Exception> errors, Task actual, Exception observed)
    {
        foreach (var cause in actual.Exception?.InnerExceptions ?? new[] { observed }.AsEnumerable())
            if (!errors.Any(value => ReferenceEquals(value, cause))) errors.Add(cause);
    }

    /// <summary>
    /// Consume SAME privately issued browser page/reference-identical row, existing project
    /// registration and materialization. Arbitrary local files require reviewed registration/import;
    /// absence cannot mint a FileID, path registry, root, mapping, Home grant or remote revision.
    /// </summary>
    public Task<DeveloperOperationResult<DeveloperCodeDocument>> ResolveOriginalDeveloperSelectionAsync(
        FilesNativeBrowserPage originalPage, HostedItemMetadata originalRow, AuthenticatedResourceActor originalActor,
        DeveloperResolvedProject originalProject, FilesWorkspaceDirectoryBinding originalRegisteredProjectRoot,
        string requestedRelativePath, Func<bool> originalLifetime,
        CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(originalLifetime); ArgumentException.ThrowIfNullOrWhiteSpace(requestedRelativePath);
        var originalProjects = _originalDeveloperProjects ?? throw new InvalidOperationException("The genuine canonical Dev workspace store is not configured for Files identity resolution.");
        TaskCompletionSource start; DeveloperIdentityOriginal owner; Task<DeveloperOperationResult<DeveloperCodeDocument>> actual;
        lock (_developerIdentityGate)
        {
            if (_developerIdentityRetired) throw new ObjectDisposedException("FilesOriginalDeveloperRead");
            if (_developerIdentityCapacityError is { } sticky) throw new AggregateException("Original Files Dev read custody is full.", sticky);
            _developerIdentityOriginals.RemoveAll(value => value.Task.IsCompletedSuccessfully);
            if (_developerIdentityOriginals.Count >= 128)
            {
                _developerIdentityCapacityError = new InvalidOperationException("Original Files Dev identity read custody reached its 128-reference runtime limit.");
                throw _developerIdentityCapacityError;
            }
            start = new(TaskCreationOptions.RunContinuationsAsynchronously);
            owner = new() { Parent = _developerIdentityExecuting.Value };
            actual = DriveDeveloperIdentityAsync(start.Task, owner, originalPage, originalRow, originalActor,
                originalProject, originalRegisteredProjectRoot, originalProjects, requestedRelativePath, originalLifetime, token);
            owner.Task = actual; _developerIdentityOriginals.Add(owner);
        }
        start.TrySetResult(); return actual;
    }
    private async Task<DeveloperOperationResult<DeveloperCodeDocument>> DriveDeveloperIdentityAsync(Task start, DeveloperIdentityOriginal owner,
        FilesNativeBrowserPage page, HostedItemMetadata row, AuthenticatedResourceActor actor, DeveloperResolvedProject selected,
        FilesWorkspaceDirectoryBinding expectedRoot, IDeveloperWorkspaceStore projects, string relative, Func<bool> lifetime, CancellationToken token)
    {
        await start.ConfigureAwait(false); var previous = _developerIdentityExecuting.Value;
        _developerIdentityExecuting.Value = owner; Volatile.Write(ref owner.Live, true);
        try
        {
            return await ObserveDeveloperIdentityAsync(owner, () => ResolveDeveloperIdentityBodyAsync(owner, page, row, actor,
                selected, expectedRoot, projects, relative, lifetime, token)).ConfigureAwait(false);
        }
        finally { Volatile.Write(ref owner.Live, false); _developerIdentityExecuting.Value = previous; }
    }
    private async Task<DeveloperOperationResult<DeveloperCodeDocument>> ResolveDeveloperIdentityBodyAsync(DeveloperIdentityOriginal owner,
        FilesNativeBrowserPage page, HostedItemMetadata row, AuthenticatedResourceActor actor, DeveloperResolvedProject selected,
        FilesWorkspaceDirectoryBinding expectedRoot, IDeveloperWorkspaceStore projects, string relative, Func<bool> lifetime, CancellationToken token)
    {
            var sources = DeveloperIdentityReadSources(owner);
            if (selected is null || row is not { Kind: HostedItemKind.File, CurrentRevisionId: { } } ||
                page is null || !_originalPages.TryGetValue(page, out var retained) || retained.Actor != actor ||
                !page.Items.Any(value => ReferenceEquals(value, row)) || retained.Workspace.Configuration.StoreId != page.StoreID)
                throw new UnauthorizedAccessException("Retain the exact original Files file selection and project.");
            var workspace = retained.Workspace;
            var guardedLifetime = WrapDeveloperIdentityLifetime(owner, lifetime);
            var originalCheck = await ObserveDeveloperIdentityAsync(owner, () => CaptureOriginalDeveloperPageReadCheckAsync(page, actor, guardedLifetime, sources, token).AsTask()).ConfigureAwait(false);
            DemandDeveloperIdentityCallbackHealth(owner);
            async Task Require(CancellationToken ct)
            {
                ct.ThrowIfCancellationRequested();
                var current = guardedLifetime();
                DemandDeveloperIdentityCallbackHealth(owner);
                if (current) current = await ObserveDeveloperIdentityAsync(owner, () => originalCheck(ct).AsTask()).ConfigureAwait(false);
                DemandDeveloperIdentityCallbackHealth(owner);
                if (!current) throw new UnauthorizedAccessException("The original Files Dev selection was withdrawn.");
            }
            await Require(token).ConfigureAwait(false);
            await ObserveDeveloperIdentityAsync(owner, () => RevalidateOriginalDeveloperAsync(page, actor, sources, token)).ConfigureAwait(false);
            await Require(token).ConfigureAwait(false);
            var saved = await ObserveDeveloperIdentityAsync(owner, () => projects.GetAsync(selected.Reference.WorkspaceId, token)).ConfigureAwait(false);
            if (!saved.Succeeded || saved.Value is null || saved.Value.Revision != selected.Reference.WorkspaceRevision)
                throw new InvalidOperationException("The actual saved Dev workspace revision changed.");
            var savedWorkspace = saved.Value;
            var project = savedWorkspace.Projects.SingleOrDefault(value => value.ProjectId == selected.Reference.ProjectId);
            var root = savedWorkspace.Roots.SingleOrDefault(value => value.RootId == selected.Reference.RootId);
            var editor = savedWorkspace.OpenEditors.SingleOrDefault(value => value.FileId == row.Id.Value && value.ProjectId == selected.Reference.ProjectId);
            if (project is null || project.Revision != selected.Reference.ProjectRevision || !project.RootIds.Contains(selected.Reference.RootId) ||
                root is null || root != selected.Root || editor is null || string.IsNullOrWhiteSpace(editor.CanonicalResourceId) ||
                expectedRoot is null || expectedRoot.OwningAppId != "dev.project." + project.ProjectId.ToString("N") ||
                !Guid.TryParse(actor.ProfileId, out var profile) || expectedRoot.ProfileId != profile || expectedRoot.AccountId != Guid.Empty ||
                expectedRoot.LocationId != workspace.Provider.Location.Id || !SameDeveloperPath(expectedRoot.DirectoryPath, root.Location))
                throw new UnauthorizedAccessException("No current canonical Dev editor/project/registered Files root pair exists.");
            await Require(token).ConfigureAwait(false);
            var binding = await ObserveDeveloperIdentityAsync(owner, () => workspace.Directories.ResolveOriginalRegisteredFolderAsync(profile,
                expectedRoot.OwningAppId, expectedRoot.FolderId, workspace.Provider, page.StoreID, true,
                ct => new ValueTask(Require(ct)), token)).ConfigureAwait(false);
            if (!binding.IsSuccess || binding.Value != expectedRoot) throw new UnauthorizedAccessException("The actual existing Dev root registration changed.");
            await Require(token).ConfigureAwait(false);
            var materialized = await ObserveDeveloperIdentityAsync(owner, () => workspace.Materializations.GetExistingByItemIdAsync(row.Id, token)).ConfigureAwait(false);
            if (materialized is null || materialized.ItemId != row.Id || materialized.CurrentRemoteRevisionId != row.CurrentRevisionId ||
                materialized.LocalRevisionId is not null || materialized.State is not (SyncAvailability.AvailableOffline or SyncAvailability.AlwaysAvailable) ||
                materialized.SizeBytes != row.SizeBytes || !SameDeveloperHash(materialized.ContentHash, row.ContentHash))
                throw new InvalidOperationException("The selected source has no unchanged existing canonical materialization.");
            var actualRelative = Path.GetRelativePath(Path.GetFullPath(root.Location), Path.GetFullPath(materialized.LocalPath));
            if (!StringComparer.Ordinal.Equals(actualRelative.Replace('\\', '/'), relative.Replace('\\', '/')))
                throw new UnauthorizedAccessException("The requested file path differs from the original selected materialization.");
            var document = new DeveloperCodeDocument(savedWorkspace.WorkspaceId, project.ProjectId, row.Id.Value,
                editor.CanonicalResourceId, root.Location, actualRelative, savedWorkspace.Revision);
            if (document.Validate() is { } invalid) throw new InvalidDataException(invalid);
            // Trace actual hosted ancestry back to this registered folder. A same physical path
            // without canonical parenthood is not a Dev project selection.
            var ancestors = new List<HostedItemMetadata>(); var seen = new HashSet<Guid>(); var next = row.ParentId;
            for (var depth = 0; next is { } ancestor && depth < 128; depth++)
            {
                if (!seen.Add(ancestor.Value)) throw new InvalidDataException("The selected file has a cyclic canonical ancestry.");
                await Require(token).ConfigureAwait(false);
                var found = await ObserveDeveloperIdentityAsync(owner, () => workspace.Provider.GetForOriginalStoreAsync(page.StoreID, ancestor, token)).ConfigureAwait(false);
                await Require(token).ConfigureAwait(false);
                if (!found.IsSuccess || found.Value is not { Kind: HostedItemKind.Folder } folder)
                    throw new UnauthorizedAccessException("The source project ancestor is unavailable.");
                await ObserveDeveloperIdentityAsync(owner, () => RequireOriginalDeveloperMetadataAsync(folder, actor, page.StoreID, sources, token)).ConfigureAwait(false);
                ancestors.Add(folder); if (folder.Id == expectedRoot.FolderId) break; next = folder.ParentId;
            }
            if (ancestors.Count == 0 || ancestors[^1].Id != expectedRoot.FolderId)
                throw new UnauthorizedAccessException("The bounded Files ancestry does not reach the registered Dev root.");
            await Require(token).ConfigureAwait(false);
            var currentSaved = await ObserveDeveloperIdentityAsync(owner, () => projects.GetAsync(savedWorkspace.WorkspaceId, token)).ConfigureAwait(false);
            if (!currentSaved.Succeeded || currentSaved.Value is null || JsonSerializer.Serialize(currentSaved.Value) != JsonSerializer.Serialize(savedWorkspace))
                throw new InvalidOperationException("The actual Dev document/project state changed during identity observation.");
            foreach (var ancestor in ancestors)
            {
                var current = await ObserveDeveloperIdentityAsync(owner, () => workspace.Provider.GetForOriginalStoreAsync(page.StoreID, ancestor.Id, token)).ConfigureAwait(false);
                if (!current.IsSuccess || current.Value != ancestor) throw new InvalidOperationException("The original source ancestry changed.");
            }
            var currentMaterialization = await ObserveDeveloperIdentityAsync(owner, () => workspace.Materializations.GetExistingByItemIdAsync(row.Id, token)).ConfigureAwait(false);
            var currentBinding = await ObserveDeveloperIdentityAsync(owner, () => workspace.Directories.ResolveOriginalRegisteredFolderAsync(profile,
                expectedRoot.OwningAppId, expectedRoot.FolderId, workspace.Provider, page.StoreID, true, ct => new ValueTask(Require(ct)), token)).ConfigureAwait(false);
            await ObserveDeveloperIdentityAsync(owner, () => RevalidateOriginalDeveloperAsync(page, actor, sources, token)).ConfigureAwait(false);
            await Require(token).ConfigureAwait(false);
            if (currentMaterialization != materialized || !currentBinding.IsSuccess || currentBinding.Value != expectedRoot)
                throw new InvalidOperationException("The original Files source/root mapping changed during observation.");
            DemandDeveloperIdentityCallbackHealth(owner);
            return DeveloperOperationResult<DeveloperCodeDocument>.Success(document);
    }

    // Lower original Files pairing intentionally classifies callback failures as a withdrawn
    // pair. This wrapper preserves the exact producer cause BEFORE that classification and
    // establishes the physical callback guard on EVERY invocation, including after awaits.
    private Func<bool> WrapDeveloperIdentityLifetime(DeveloperIdentityOriginal owner, Func<bool> actualLifetime)
        => () => AcquireDeveloperIdentitySource(() =>
        {
            try { return actualLifetime(); }
            catch (Exception error)
            {
                lock (_developerIdentityGate)
                {
                    if (!owner.CallbackCauses.Any(value => ReferenceEquals(value, error))) owner.CallbackCauses.Add(error);
                    if (error is AggregateException group)
                        foreach (var cause in group.InnerExceptions)
                            if (!owner.CallbackCauses.Any(value => ReferenceEquals(value, cause))) owner.CallbackCauses.Add(cause);
                }
                throw;
            }
        });
    private void DemandDeveloperIdentityCallbackHealth(DeveloperIdentityOriginal owner)
    {
        Exception[] causes;
        lock (_developerIdentityGate) causes = owner.CallbackCauses.ToArray();
        if (causes.Length != 0) throw new AggregateException("The actual Files Dev lifetime callback failed; pairing cannot certify a healthy read.", causes);
    }
    private async Task<T> ObserveDeveloperIdentityAsync<T>(DeveloperIdentityOriginal owner, Func<Task<T>> source)
    {
        var actual = AcquireDeveloperIdentitySource(source);
        lock (_developerIdentityGate) if (!owner.Sources.Any(value => ReferenceEquals(value, actual))) owner.Sources.Add(actual);
        try { return await actual.ConfigureAwait(false); } catch (Exception) when (actual.IsFaulted) { throw actual.Exception!; }
    }
    private async Task ObserveDeveloperIdentityAsync(DeveloperIdentityOriginal owner, Func<Task> source)
    {
        var actual = AcquireDeveloperIdentitySource(source);
        lock (_developerIdentityGate) if (!owner.Sources.Any(value => ReferenceEquals(value, actual))) owner.Sources.Add(actual);
        try { await actual.ConfigureAwait(false); } catch (Exception) when (actual.IsFaulted) { throw actual.Exception!; }
    }
    private T AcquireDeveloperIdentitySource<T>(Func<T> source)
    {
        var stack = _developerIdentityCallbacks ??= []; stack.Add(this);
        try { return source(); }
        catch (OperationCanceledException error) { throw new AggregateException("A synchronous Files Dev identity callback returned no canceled original Task.", error); }
        finally { stack.RemoveAt(stack.Count - 1); }
    }
    private static bool SameDeveloperPath(string left, string right) => string.Equals(
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)), Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)),
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    private static bool SameDeveloperHash(string? left, string? right)
    {
        static string? Normalize(string? value) => value?.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) == true ? value[7..] : value;
        var a = Normalize(left); var b = Normalize(right);
        return a is { Length: 64 } && b is { Length: 64 } && a.All(Uri.IsHexDigit) && b.All(Uri.IsHexDigit) && string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    }
}
