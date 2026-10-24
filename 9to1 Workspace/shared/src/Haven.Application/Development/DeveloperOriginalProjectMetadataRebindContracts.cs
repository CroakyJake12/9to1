namespace Haven.Application;

/// <summary>Opaque SAME-Home preparation after actual before-native READ and healthy close.
/// Public observations, copied implementations and serialized IDs grant no rebind or effect.</summary>
public interface IDeveloperOriginalProjectMetadataRebindPreparation
{
    IDeveloperOriginalProjectCommandRead OriginalCommandRead { get; }
    IDeveloperProjectOriginalWorkspaceMetadataStore OriginalStore { get; }
    TaskRunColdProjectIdentity BeforeIdentity { get; }
}

/// <summary>New separately approved Home READ, not an old approval retargeted to another
/// selection. The receiver must retain/own the new read before any post-publication check.
/// Old READ/permission leases remain with their original owners; no task/capsule is changed.</summary>
public interface IDeveloperOriginalProjectMetadataRebindResult
{
    IDeveloperOriginalProjectMetadataRebindPreparation OriginalPreparation { get; }
    IDeveloperProjectOriginalMetadataTransition OriginalTransition { get; }
    IDeveloperProjectOriginalMetadataCommitValue OriginalCommittedValue { get; }
    IDeveloperOriginalProjectCommandRead NewOriginalCommandRead { get; }
    TaskRunColdProjectIdentity AfterIdentity { get; }
}

public interface IDeveloperOriginalProjectMetadataRebindSource
{
    Task<IDeveloperOriginalProjectMetadataRebindPreparation> PrepareOriginalMetadataRebindWithinSourceAsync(
        IDeveloperOriginalProjectCommandRead sameOldRead, Action<Action> originalSynchronousScope,
        Action<Task> retainOriginalTask, CancellationToken token);
    bool IsIssuedOriginalMetadataRebindPreparation(IDeveloperOriginalProjectMetadataRebindPreparation samePreparation);
    Task<IDeveloperOriginalProjectMetadataRebindResult> CompleteOriginalMetadataRebindWithinSourceAsync(
        IDeveloperOriginalProjectMetadataRebindPreparation samePreparation,
        IDeveloperProjectOriginalMetadataTransition sameTransition,
        IDeveloperProjectOriginalMetadataCommitValue sameActuallyCommittedValue,
        Action<Action> originalSynchronousScope, Action<Task> retainOriginalTask, CancellationToken token);
    bool IsIssuedOriginalMetadataRebindResult(IDeveloperOriginalProjectMetadataRebindResult sameResult);
    // Historical custody only, including after currentness failure: no authority to use the read.
    bool IsOwnedOriginalMetadataRebindResult(IDeveloperOriginalProjectMetadataRebindResult sameResult);
}
