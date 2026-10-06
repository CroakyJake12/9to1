namespace Haven.Application;

/// <summary>Configuration observation of the SAME actual Dev store. Paths are not an
/// authority grant: the configured native issuer independently recognises this exact object.</summary>
public interface IDeveloperProjectOriginalWorkspaceMetadataStore
{
    string OriginalWorkspaceMetadataDirectory { get; }
    string OriginalWorkspaceMetadataAncestor { get; }
}

public interface IDeveloperProjectOriginalWorkspaceMetadataSource
{
    Task<IDeveloperProjectOriginalWorkspaceMetadataPreparation> PrepareOriginalWorkspaceMetadataAsync(
        IDeveloperProjectOriginalWorkspaceMetadataStore sameConfiguredStore,
        DeveloperProjectSetupIntent sameIntent, IDeveloperProjectOriginalSourceCapture sameCapture,
        IDeveloperProjectOriginalSetupPermission samePermission, DeveloperProjectSetupStep sameStep,
        CancellationToken cancellationToken);
    bool IsIssuedOriginalWorkspaceMetadataPreparation(IDeveloperProjectOriginalWorkspaceMetadataPreparation samePreparation,
        IDeveloperProjectOriginalWorkspaceMetadataStore sameConfiguredStore, Guid workspaceId);
    bool IsIssuedOriginalWorkspaceMetadataOutcome(IDeveloperProjectOriginalWorkspaceMetadataPreparation samePreparation,
        Task sameOriginalWriteTask, IDeveloperProjectOriginalWorkspaceMetadataObservation sameObservation);
    Task ValidateOriginalWorkspaceMetadataOutcomeAsync(IDeveloperProjectOriginalWorkspaceMetadataPreparation samePreparation,
        Task sameOriginalWriteTask, IDeveloperProjectOriginalWorkspaceMetadataObservation sameObservation,
        CancellationToken cancellationToken);
    void DemandExternalOriginalWorkspaceMetadataJoin();
}

/// <summary>Opaque native preparation; unsupported or unconfigured sources refuse before
/// strict mutation. The original native child cleanup finishes before the write Task returns;
/// whole preparation close also joins that SAME actual driver and ancestor lifetime.</summary>
public interface IDeveloperProjectOriginalWorkspaceMetadataPreparation : IAsyncDisposable
{
    bool IsBoundToOriginalStore(IDeveloperProjectOriginalWorkspaceMetadataStore sameConfiguredStore, Guid workspaceId);
    Task<IDeveloperProjectOriginalWorkspaceMetadataObservation> CreateOriginalMetadataAsync(
        ReadOnlyMemory<byte> exactDocument, IDeveloperProjectOriginalSetupStepEntry sameEntry,
        Action<Action> originalSynchronousScope, Action<Task> retainOriginalTask,
        CancellationToken cancellationToken);
    bool IsIssuedOriginalMetadata(Task sameOriginalWriteTask, IDeveloperProjectOriginalWorkspaceMetadataObservation sameObservation);
    Task CloseAndDrainAsync();
}

/// <summary>Source-issued observation only. The private issuer binds the original native
/// driver, exact committed inode/document and cleanup. Public fields never issue a setup ACK.</summary>
public interface IDeveloperProjectOriginalWorkspaceMetadataObservation
{
    string OriginalCommittedDocument { get; }
    string OriginalDocumentSha256 { get; }
}
