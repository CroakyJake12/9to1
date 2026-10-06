namespace Haven.Application;

/// <summary>Opaque evidence from the SAME private Files/saved-step issuer. Its public
/// observations do not grant execution. The configured kernel must recognise this exact
/// evidence and binding, plus its own exact metadata preparation/write/cleanup result.</summary>
public interface IDeveloperWorkspaceOriginalExecutionDescriptorEvidence
{
    IDeveloperProjectOriginalWorkspaceMetadataPreparation OriginalMetadataPreparation { get; }
    Task OriginalMetadataWriteTask { get; }
    IDeveloperProjectOriginalWorkspaceMetadataObservation OriginalMetadataObservation { get; }
    string OriginalFilesRoot { get; }
    string OriginalRegistrationStatePath { get; }
    string OriginalRegistrationStateJson { get; }
}

public interface IDeveloperWorkspaceOriginalExecutionDescriptorBindingSource
    : IDeveloperWorkspaceOriginalExecutionBindingSource
{
    IDeveloperWorkspaceOriginalExecutionDescriptorEvidence GetOriginalDescriptorEvidence(
        IDeveloperWorkspaceOriginalExecutionBinding sameBinding);
    bool IsIssuedOriginalDescriptorEvidence(IDeveloperWorkspaceOriginalExecutionBinding sameBinding,
        IDeveloperWorkspaceOriginalExecutionDescriptorEvidence sameEvidence);
}
