using System.Security.Cryptography;
using System.Text.Json;
using Haven.Application;
using HavenOS.Apps.Dev;

namespace HavenOS.Files.NativeHost;

public sealed partial class FilesDeveloperOriginalSourceSelection
{
    private static readonly JsonSerializerOptions RegisteredJson = new(JsonSerializerDefaults.Web);
    private readonly Dictionary<IDeveloperProjectOriginalSourceCapture, RegisteredWitness> _registeredCaptures =
        new(ReferenceEqualityComparer.Instance);

    private sealed class RegisteredDescriptor(FileDeveloperWorkspaceStore store, DeveloperResolvedProject project,
        NativeFilesWorkspace workspace, FilesWorkspaceDirectoryResolver.OriginalExecutionRegistrationSnapshot registration)
        : IDeveloperOriginalRegisteredProjectSnapshotDescriptor
    {
        public IDeveloperProjectOriginalWorkspaceMetadataStore OriginalStore => store;
        public Guid WorkspaceId => project.Reference.WorkspaceId;
        public Guid ProjectId => project.Reference.ProjectId;
        public Guid RootId => project.Reference.RootId;
        public long WorkspaceRevision => project.Reference.WorkspaceRevision;
        public long ProjectRevision => project.Reference.ProjectRevision;
        public string? RepositoryBindingId => project.Reference.RepositoryBindingId;
        public string ExactProjectReferenceJson { get; } = RegisteredSerialize(project.Reference);
        public AuthenticatedResourceActor OriginalActor => workspace.Actor;
        public string ConfiguredFilesRoot => workspace.Configuration.RootDirectory;
        public string RegisteredProjectRoot => registration.Binding.DirectoryPath;
        public string OriginalRegistrationStatePath => registration.StatePath;
        public string OriginalRegistrationStateJson => registration.StateJson;
        public string OriginalSelectedRegistrationJson { get; } = RegisteredSerialize(registration.Binding);
        public string? ExpectedWorkspaceDocumentSha256 => null;
        public string? ExpectedRegisteredRootFingerprint => null;
        public string ExactSelectedProjectJson { get; } = RegisteredSerialize(project.Project);
        public string ExactSelectedRootJson { get; } = RegisteredSerialize(project.Root);
        public string? ExactSelectedRepositoryJson { get; } = project.Repository is null ? null : RegisteredSerialize(project.Repository);
        internal FilesWorkspaceDirectoryResolver.OriginalExecutionRegistrationSnapshot Registration => registration;
    }

    private sealed record RegisteredWitness(Selection Selection, IDeveloperOriginalRegisteredProjectNativeSource Native,
        Task ActualCapture, IDeveloperOriginalCurrentProjectNativeRead Read);

    /// <summary>Selects the actual registered saved project, never whichever directory an input
    /// path names. Public saved records are exact comparison inputs only. No workspace document
    /// content is opened here; the existing native descriptor engine reads it after genuine Home READ.</summary>
    public Task<IDeveloperProjectOriginalReadSelection> SelectOriginalRegisteredProjectAsync(Guid originalStoreId,
        DeveloperResolvedProject project, CancellationToken token)
        => Start<IDeveloperProjectOriginalReadSelection>(async sources =>
    {
        var store = _registeredStore ?? throw new NotSupportedException("The SAME configured saved Build store is unavailable; path-only selection is not a fallback.");
        DemandSelectedSavedRecords(project);
        if (originalStoreId == Guid.Empty) throw new ArgumentException("The actual configured Files store UUID is required.", nameof(originalStoreId));
        var workspace = await sources.Observe(() => workspaces.GetOriginalCurrentAsync(originalStoreId, sources, token)).ConfigureAwait(false)
            ?? throw new UnauthorizedAccessException("The original profile has no verified Files configuration.");
        var registration = await ObserveRegisteredProjectAsync(workspace, project.Reference.ProjectId, sources, token).ConfigureAwait(false);
        if (project.Root.Location != registration.Binding.DirectoryPath)
            throw new UnauthorizedAccessException("The exact saved root does not name the SAME registered project root.");
        lock (_gate) if (_selections.Count >= 128) throw new InvalidOperationException("Original project selections are full; no physical root was opened.");
        var root = await sources.Observe(() => _physical.OpenOriginalSelectionAsync(workspace.Configuration.RootDirectory,
            registration.Binding.DirectoryPath, token)).ConfigureAwait(false);
        var descriptor = new RegisteredDescriptor(store, project, workspace, registration);
        var selection = new Selection(this, workspace, root, descriptor);
        lock (_gate) _selections.Add(selection);
        selection.Check = await sources.Observe(() => workspaces.CaptureOriginalReadCheckAsync(workspace,
            () => !_retiring && _physical.IsIssuedOriginalSelection(root), sources, token).AsTask()).ConfigureAwait(false);
        await RevalidateBody(selection, workspace.Actor, sources, token).ConfigureAwait(false);
        return selection;
    });

    private static void DemandSelectedSavedRecords(DeveloperResolvedProject project)
    {
        ArgumentNullException.ThrowIfNull(project);
        var reference = project.Reference;
        if (reference is null || project.Workspace is null || project.Project is null || project.Root is null ||
            reference.WorkspaceId == Guid.Empty || reference.ProjectId == Guid.Empty || reference.RootId == Guid.Empty ||
            reference.WorkspaceRevision < 1 || reference.ProjectRevision < 1 ||
            project.Workspace.WorkspaceId != reference.WorkspaceId || project.Workspace.Revision != reference.WorkspaceRevision ||
            project.Project.ProjectId != reference.ProjectId || project.Project.Revision != reference.ProjectRevision ||
            project.Root.RootId != reference.RootId || project.Root.EnvironmentId != "local" ||
            !project.Project.RootIds.Contains(reference.RootId) || project.Workspace.Validate() is not null)
            throw new UnauthorizedAccessException("The exact selected saved workspace/project/root tuple is incomplete.");
        var projects = project.Workspace.Projects.Where(value => value.ProjectId == reference.ProjectId).Take(2).ToArray();
        var roots = project.Workspace.Roots.Where(value => value.RootId == reference.RootId).Take(2).ToArray();
        if (projects.Length != 1 || roots.Length != 1 ||
            RegisteredSerialize(projects[0]) != RegisteredSerialize(project.Project) ||
            RegisteredSerialize(roots[0]) != RegisteredSerialize(project.Root) ||
            string.IsNullOrWhiteSpace(project.Root.Location) || !Path.IsPathRooted(project.Root.Location))
            throw new UnauthorizedAccessException("The selected records are not the exact members of the saved workspace observation.");
        if (reference.RepositoryBindingId is { } binding)
        {
            var repositories = project.Workspace.SourceControlBindings.Where(value => value.BindingId == binding).Take(2).ToArray();
            if (repositories.Length != 1 || project.Repository is null || project.Repository.RootId != reference.RootId ||
                project.Repository.BindingId != binding || RegisteredSerialize(repositories[0]) != RegisteredSerialize(project.Repository))
                throw new UnauthorizedAccessException("The exact selected saved repository binding is incomplete.");
        }
        else if (project.Repository is not null)
            throw new UnauthorizedAccessException("An unselected repository record cannot be adopted as project authority.");
        if (RegisteredSerialize(project.Reference).Length > 16384 || RegisteredSerialize(project.Project).Length > 65536 ||
            RegisteredSerialize(project.Root).Length > 16384 || RegisteredSerialize(project.Repository).Length > 16384)
            throw new UnauthorizedAccessException("The exact selected saved records exceed their bounded inputs.");
    }

    private static string RegisteredSerialize<T>(T value) => JsonSerializer.Serialize(value, RegisteredJson);
    private static string RegisteredScopeRevision(NativeFilesWorkspace workspace, RegisteredDescriptor descriptor)
        => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new
        {
            workspace.Configuration, workspace.Actor, descriptor.ExactProjectReferenceJson,
            descriptor.OriginalRegistrationStatePath, descriptor.OriginalRegistrationStateJson,
            descriptor.OriginalSelectedRegistrationJson, descriptor.ExactSelectedProjectJson,
            descriptor.ExactSelectedRootJson, descriptor.ExactSelectedRepositoryJson
        }, RegisteredJson))).ToLowerInvariant();

    private static Task<FilesWorkspaceDirectoryResolver.OriginalExecutionRegistrationSnapshot> ObserveRegisteredProjectAsync(
        NativeFilesWorkspace workspace, Guid projectId, FilesOriginalReadSourceScope sources, CancellationToken token)
        => sources.Observe(() =>
        {
            if (workspace.Actor.AccountId is not null || workspace.Actor.OrganisationId is not null ||
                !Guid.TryParse(workspace.Actor.ProfileId, out var profile) || profile == Guid.Empty)
                throw new UnauthorizedAccessException("Registered saved-project source requires the actual personal Home profile.");
            return workspace.Directories.ObserveOriginalCurrentProjectRegistrationAsync(profile,
                "dev.project." + projectId.ToString("N"), workspace.Provider, workspace.Configuration.StoreId,
                workspace.Configuration.RootDirectory, sources.OriginalSynchronousScope, sources.RetainOriginalTask, token);
        });

    private async Task RevalidateRegisteredSelectionAsync(Selection selection, FilesOriginalReadSourceScope sources, CancellationToken token)
    {
        if (selection.Registered is not { } descriptor) return;
        if (!ReferenceEquals(descriptor.OriginalStore, _registeredStore) || descriptor.OriginalActor != selection.Workspace.Actor)
            throw new UnauthorizedAccessException("The SAME saved store or registered project actor changed.");
        var current = await ObserveRegisteredProjectAsync(selection.Workspace, descriptor.ProjectId, sources, token).ConfigureAwait(false);
        if (current.Binding != descriptor.Registration.Binding || current.StatePath != descriptor.OriginalRegistrationStatePath ||
            current.StateJson != descriptor.OriginalRegistrationStateJson)
            throw new UnauthorizedAccessException("The actual saved project's Files registration changed.");
    }

    public IDeveloperOriginalRegisteredProjectDescriptor GetOriginalRegisteredDescriptor(IDeveloperProjectOriginalReadSelection selection)
        => Require(selection).Registered ?? throw new UnauthorizedAccessException("A SAME privately issued registered saved-project selection is required.");
    public bool IsIssuedOriginalRegisteredDescriptor(IDeveloperProjectOriginalReadSelection value, IDeveloperOriginalRegisteredProjectDescriptor descriptor)
        => value is Selection selection && IsIssuedOriginal(selection) && selection.Registered is not null && ReferenceEquals(selection.Registered, descriptor);
    public void DemandExternalOriginalRegisteredJoin() => DemandExternalOriginalReadSelectionJoin();
    public Task RevalidateOriginalRegisteredWithinSourceAsync(IDeveloperProjectOriginalReadSelection value, AuthenticatedResourceActor actor,
        Action<Action> scope, Action<Task> retain, CancellationToken token)
        => Start(async _ =>
        {
            var pair = new FilesOriginalParentOperation(Scope, Retain, scope, retain);
            await RevalidateBody(Require(value), actor, pair.Sources, token).ConfigureAwait(false);
            return true;
        });

    private async Task<RegisteredWitness?> CaptureRegisteredWitnessAsync(Selection selection,
        IDeveloperProjectOriginalReadAdmission admission, FilesOriginalReadSourceScope sources, CancellationToken token)
    {
        if (selection.Registered is null) return null;
        var native = sources.Invoke(() => _registeredNative.Value);
        Task<IDeveloperOriginalCurrentProjectNativeRead>? actual = null;
        var read = await sources.Observe(() => actual = native.CaptureRegisteredOriginalWithinSourceAsync(selection, admission,
            sources.OriginalSynchronousScope, sources.RetainOriginalTask, token)).ConfigureAwait(false);
        if (actual is null || !native.IsIssuedRegisteredOriginalRead(selection, actual, read))
            throw new UnauthorizedAccessException("The SAME native saved-project metadata witness was not issued.");
        return new(selection, native, actual, read);
    }
    private bool IsIssuedRegisteredCapture(Selection selection, IDeveloperProjectOriginalSourceCapture capture)
        => selection.Registered is null || _registeredCaptures.TryGetValue(capture, out var witness) &&
            ReferenceEquals(witness.Selection, selection) && _registeredNative.IsValueCreated &&
            ReferenceEquals(witness.Native, _registeredNative.Value) &&
            witness.Native.IsIssuedRegisteredOriginalRead(selection, witness.ActualCapture, witness.Read);
    private void DemandRegisteredCaptureNative(Selection selection, IDeveloperProjectOriginalSourceCapture capture)
    {
        if (selection.Registered is null) return;
        RegisteredWitness witness;
        lock (_gate)
            if (!IsIssuedRegisteredCapture(selection, capture) || !_registeredCaptures.TryGetValue(capture, out witness!))
                throw new UnauthorizedAccessException("The SAME registered saved-project capture retired.");
        // Pure retained native metadata/registration/root probes, safe beneath a held Home entry.
        // No async resolver, public store callback or reacquisition takes place here.
        witness.Read.DemandOriginalExecutionBinding();
    }
    private Task RevalidateRegisteredCaptureAsync(Selection selection, IDeveloperProjectOriginalSourceCapture capture,
        FilesOriginalReadSourceScope sources, CancellationToken token)
    {
        if (selection.Registered is null) return Task.CompletedTask;
        RegisteredWitness witness;
        lock (_gate)
            if (!IsIssuedRegisteredCapture(selection, capture) || !_registeredCaptures.TryGetValue(capture, out witness!))
                throw new UnauthorizedAccessException("The SAME registered saved-project capture is unavailable.");
        return sources.ObserveVoid(() => witness.Native.ValidateRegisteredOriginalReadWithinSourceAsync(selection,
            witness.ActualCapture, witness.Read, sources.OriginalSynchronousScope, sources.RetainOriginalTask, token));
    }
}
