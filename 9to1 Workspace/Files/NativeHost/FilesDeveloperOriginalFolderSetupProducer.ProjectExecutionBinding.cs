using Haven.Application;
using HavenOS.Apps.Dev;

namespace HavenOS.Files.NativeHost;

public sealed partial class FilesDeveloperOriginalFolderSetupProducer
    : IDeveloperWorkspaceOriginalProjectExecutionBindingSource
{
    public Task<IDeveloperWorkspaceOriginalExecutionBinding> ResolveOriginalProjectBindingAsync(
        DeveloperResolvedProject sameProject, Action<Action> originalSynchronousScope,
        Action<Task> retainOriginalTask, CancellationToken token)
        => StartWithinOriginalSource<IDeveloperWorkspaceOriginalExecutionBinding>(async sources =>
        {
            ArgumentNullException.ThrowIfNull(sameProject);
            WorkspaceMetadataStep saved; ExecutionBinding? retained;
            lock (_gate)
            {
                if (_retiring) throw new InvalidOperationException("The actual saved-project producer is retiring.");
                var candidates = _originalSavedSteps.Keys.Where(value =>
                    value.OriginalStoreResult.Workspace.WorkspaceId == sameProject.Reference.WorkspaceId).Take(2).ToArray();
                if (candidates.Length != 1)
                    throw new UnauthorizedAccessException("No unambiguous SAME private saved-project ACK product exists. Cold metadata cannot reconstruct one.");
                saved = candidates[0];
                retained = _originalExecutionBindings.SingleOrDefault(value => ReferenceEquals(value.Saved, saved));
            }
            var physical = sources.Invoke(() => RequireOriginalSavedStep(saved));
            sources.Invoke(() => { DemandSameResolvedOriginalProject(saved, physical, sameProject); return true; });
            if (retained is not null)
            {
                await sources.ObserveVoid(() => ValidateOriginalProjectBindingAsync(sameProject, retained,
                    sources.OriginalSynchronousScope, sources.RetainOriginalTask, token)).ConfigureAwait(false);
                return retained;
            }
            var store = sources.Invoke(() => _originalWorkspaceStore?.Invoke())
                ?? throw new InvalidOperationException("The SAME actual saved workspace store is unavailable.");
            if (store is not IDeveloperOriginalWorkspaceSourceReadStore reader)
                throw new NotSupportedException("The actual saved store lacks original source read custody.");
            var native = sources.Invoke(() => _originalDirectories?.Invoke()) as IDeveloperWorkspaceOriginalExecutionCommitBindingSource
                ?? throw new PlatformNotSupportedException("The actual saved-root descriptor source is unavailable.");
            if (native is not IDeveloperWorkspaceOriginalExecutionScopedBindingSource)
                throw new NotSupportedException("The SAME native descriptor source lacks parent-source custody.");
            var destination = sources.Invoke(() => scopes.GetOriginalBoundDestination(physical.Intent, physical.Capture));
            await sources.ObserveVoid(() => scopes.RevalidateOriginalDestinationWithinSourceAsync(destination,
                sources.OriginalSynchronousScope, sources.RetainOriginalTask, token)).ConfigureAwait(false);
            var current = await sources.Observe(() => reader.GetWithinOriginalSourceAsync(sameProject.Reference.WorkspaceId,
                sources.OriginalSynchronousScope, sources.RetainOriginalTask, token)).ConfigureAwait(false);
            sources.Invoke(() => { RequireSameSavedWorkspace(saved, current); DemandSameResolvedOriginalProject(saved, physical, sameProject); return true; });
            if (!Guid.TryParse(destination.Workspace.Configuration.ProfileId, out var profile))
                throw new UnauthorizedAccessException("The actual personal Files profile is unavailable.");
            var registration = await sources.Observe(() => destination.Workspace.Directories.ObserveOriginalExecutionRegistrationAsync(
                profile, "dev.project." + physical.Intent.ProjectId.ToString("N"), new(physical.Intent.ProjectFolderId),
                destination.Workspace.Provider, physical.Intent.OriginalFilesStoreId, physical.Intent.OriginalExistingProjectRoot!,
                sources.OriginalSynchronousScope, sources.RetainOriginalTask, token)).ConfigureAwait(false);
            await sources.ObserveVoid(() => scopes.RevalidateOriginalDestinationWithinSourceAsync(destination,
                sources.OriginalSynchronousScope, sources.RetainOriginalTask, token)).ConfigureAwait(false);
            return sources.Invoke(() =>
            {
                token.ThrowIfCancellationRequested(); RequireOriginalSavedStep(saved);
                lock (_gate)
                {
                    if (_retiring || _originalExecutionBindings.Count >= 128)
                        throw new InvalidOperationException("Original saved-project binding custody is sealed or full.");
                    // Concurrent genuine callers share the private binding; no second root
                    // owner or permission is issued merely because the metadata is equal.
                    retained = _originalExecutionBindings.SingleOrDefault(value => ReferenceEquals(value.Saved, saved));
                    if (retained is not null)
                    {
                        if (!ReferenceEquals(retained.Store, store) || !ReferenceEquals(retained.Native, native) ||
                            retained.Registration.StatePath != registration.StatePath || retained.Registration.StateJson != registration.StateJson ||
                            retained.Registration.Binding != registration.Binding)
                            throw new UnauthorizedAccessException("The concurrent actual saved-root registration changed.");
                        return retained;
                    }
                    var issued = new ExecutionBinding(this, saved, physical, destination, registration, store, native);
                    _originalExecutionBindings.Add(issued); return issued;
                }
            });
        }, originalSynchronousScope, retainOriginalTask);

    public Task ValidateOriginalProjectBindingAsync(DeveloperResolvedProject sameProject,
        IDeveloperWorkspaceOriginalExecutionBinding sameBinding, Action<Action> originalSynchronousScope,
        Action<Task> retainOriginalTask, CancellationToken token) => StartWithinOriginalSource(async sources =>
    {
        var binding = sources.Invoke(() => RequireOriginalExecutionBinding(sameBinding));
        sources.Invoke(() => { DemandSameResolvedOriginalProject(binding.Saved, binding.Physical, sameProject); return true; });
        await sources.ObserveVoid(() => RevalidateOriginalWithinSourceAsync(binding, binding.OriginalActor,
            sources.OriginalSynchronousScope, sources.RetainOriginalTask, token)).ConfigureAwait(false);
        sources.Invoke(() => { token.ThrowIfCancellationRequested(); RequireOriginalExecutionBinding(binding);
            DemandSameResolvedOriginalProject(binding.Saved, binding.Physical, sameProject); return true; });
        return true;
    }, originalSynchronousScope, retainOriginalTask);

    private static void DemandSameResolvedOriginalProject(WorkspaceMetadataStep saved, Physical physical,
        DeveloperResolvedProject project)
    {
        ArgumentNullException.ThrowIfNull(project);
        var reference = project.Reference; var workspace = saved.OriginalStoreResult.Workspace;
        if (reference.WorkspaceId != physical.Intent.WorkspaceId || reference.ProjectId != physical.Intent.ProjectId ||
            reference.RootId != physical.Intent.RootId || reference.WorkspaceRevision != workspace.Revision ||
            reference.ProjectRevision != 1 || reference.RepositoryBindingId is not null || project.Repository is not null ||
            project.Workspace.WorkspaceId != reference.WorkspaceId || project.Project.ProjectId != reference.ProjectId ||
            project.Root.RootId != reference.RootId || project.Root.Location != physical.Intent.OriginalExistingProjectRoot ||
            System.Text.Json.JsonSerializer.Serialize(project.Workspace) != System.Text.Json.JsonSerializer.Serialize(workspace) ||
            System.Text.Json.JsonSerializer.Serialize(workspace.Projects.SingleOrDefault(value => value.ProjectId == reference.ProjectId)) != System.Text.Json.JsonSerializer.Serialize(project.Project) ||
            System.Text.Json.JsonSerializer.Serialize(workspace.Roots.SingleOrDefault(value => value.RootId == reference.RootId)) != System.Text.Json.JsonSerializer.Serialize(project.Root))
            throw new UnauthorizedAccessException("The actual resolved project/root/revisions differ from the privately acknowledged saved workspace.");
    }
}
