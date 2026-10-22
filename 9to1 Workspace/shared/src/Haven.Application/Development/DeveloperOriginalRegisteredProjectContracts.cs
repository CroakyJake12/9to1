namespace Haven.Application;

/// <summary>Metadata-only descriptor from the SAME configured Files source. Selection and public
/// fields issue no READ. The configured native owner recognises its exact issuer/store before
/// opening metadata under the actual Home READ; Task-specific context belongs to derived contracts.</summary>
public interface IDeveloperOriginalRegisteredProjectDescriptor
{
    IDeveloperProjectOriginalWorkspaceMetadataStore OriginalStore { get; }
    Guid WorkspaceId { get; }
    Guid ProjectId { get; }
    Guid RootId { get; }
    long WorkspaceRevision { get; }
    long ProjectRevision { get; }
    string? RepositoryBindingId { get; }
    string ExactProjectReferenceJson { get; }
    AuthenticatedResourceActor OriginalActor { get; }
    string ConfiguredFilesRoot { get; }
    string RegisteredProjectRoot { get; }
    string OriginalRegistrationStatePath { get; }
    string OriginalRegistrationStateJson { get; }
    string OriginalSelectedRegistrationJson { get; }
    string? ExpectedWorkspaceDocumentSha256 { get; }
    string? ExpectedRegisteredRootFingerprint { get; }
}

/// <summary>Exact selected saved records, checked against descriptor-backed document bytes after
/// READ. Caller-provided records are comparison inputs, never authority or a substitute store.</summary>
public interface IDeveloperOriginalRegisteredProjectSnapshotDescriptor : IDeveloperOriginalRegisteredProjectDescriptor
{
    string ExactSelectedProjectJson { get; }
    string ExactSelectedRootJson { get; }
    string? ExactSelectedRepositoryJson { get; }
}

public interface IDeveloperOriginalRegisteredProjectSelectionSource : IDeveloperProjectOriginalReadSelectionSource
{
    IDeveloperOriginalRegisteredProjectDescriptor GetOriginalRegisteredDescriptor(IDeveloperProjectOriginalReadSelection sameSelection);
    bool IsIssuedOriginalRegisteredDescriptor(IDeveloperProjectOriginalReadSelection sameSelection,
        IDeveloperOriginalRegisteredProjectDescriptor sameDescriptor);
    Task RevalidateOriginalRegisteredWithinSourceAsync(IDeveloperProjectOriginalReadSelection sameSelection,
        AuthenticatedResourceActor sameActor, Action<Action> originalSynchronousScope,
        Action<Task> retainOriginalTask, CancellationToken token);
    void DemandExternalOriginalRegisteredJoin();
}

/// <summary>The existing descriptor engine over a genuine registered selection, without inventing
/// a Task/Conversation/container. The returned metadata-read product has no Task authority. Exact
/// actual capture/close Tasks remain part of private issuance and independent cleanup recognition.</summary>
public interface IDeveloperOriginalRegisteredProjectNativeSource
{
    Task<IDeveloperOriginalCurrentProjectNativeRead> CaptureRegisteredOriginalWithinSourceAsync(
        IDeveloperProjectOriginalReadSelection sameSelection, IDeveloperProjectOriginalReadAdmission sameReadAdmission,
        Action<Action> originalSynchronousScope, Action<Task> retainOriginalTask, CancellationToken token);
    bool IsIssuedRegisteredOriginalRead(IDeveloperProjectOriginalReadSelection sameSelection,
        Task sameActualCaptureTask, IDeveloperOriginalCurrentProjectNativeRead sameRead);
    bool IsOwnedRegisteredOriginalRead(IDeveloperProjectOriginalReadSelection sameSelection,
        Task sameActualCaptureTask, IDeveloperOriginalCurrentProjectNativeRead sameRead);
    Task ValidateRegisteredOriginalReadWithinSourceAsync(IDeveloperProjectOriginalReadSelection sameSelection,
        Task sameActualCaptureTask, IDeveloperOriginalCurrentProjectNativeRead sameRead,
        Action<Action> originalSynchronousScope, Action<Task> retainOriginalTask, CancellationToken token);
    bool IsClosedRegisteredOriginalRead(IDeveloperProjectOriginalReadSelection sameSelection,
        Task sameActualCaptureTask, IDeveloperOriginalCurrentProjectNativeRead sameRead, Task sameActualCloseTask);
    void RequestOriginalRetirement();
    void DemandExternalOriginalJoin();
    Task CloseAndDrainOriginalAsync();
}
