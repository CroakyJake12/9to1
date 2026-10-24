namespace Haven.Application;

/// <summary>Private Home provenance for a distinct, genuinely approved and healthily closed
/// initial-input verification. Old input identity/audit/approval is unchanged. These observations
/// are not a Task admission, attempt, execution permission or serialized recovery authority.</summary>
public interface ITaskRunOriginalProjectMetadataInputSuccessor
{
    ITaskRunColdOriginalProjectInput OriginalInput { get; }
    ITaskRunColdOriginalProjectInput VerifiedAfterInput { get; }
    IDeveloperOriginalProjectMetadataRebindResult OriginalMetadataRebind { get; }
    TaskRunColdProjectIdentity BeforeIdentity { get; }
    TaskRunColdProjectIdentity AfterIdentity { get; }
    Task OriginalAfterClose { get; }
}

public interface ITaskRunOriginalProjectMetadataInputMutation
{
    ITaskRunColdOriginalProjectInput OriginalInput { get; }
    IDeveloperOriginalProjectMetadataRebindPreparation OriginalPreparation { get; }
    Task OriginalSettlement { get; }
}

public interface ITaskRunOriginalProjectMetadataInputSource
{
    ITaskRunColdOriginalProjectInput GetOriginalInitialInputForMetadataRebindPreparation(
        IDeveloperOriginalProjectMetadataRebindPreparation sameBeforePreparation);
    ITaskRunOriginalProjectMetadataInputMutation BeginOriginalInitialInputMetadataMutation(
        ITaskRunColdOriginalProjectInput sameInput, IDeveloperOriginalProjectMetadataRebindPreparation sameBeforePreparation);
    void CompleteOriginalInitialInputMetadataMutation(ITaskRunOriginalProjectMetadataInputMutation sameMutation,
        ITaskRunOriginalProjectMetadataInputSuccessor sameSuccessfulSuccessor);
    void FailOriginalInitialInputMetadataMutation(ITaskRunOriginalProjectMetadataInputMutation sameMutation, Exception originalFailure);
    // Private original marker only, unambiguous exact context/project/actor/before identity.
    // Does not infer authority from a TaskID or select among conflicting preparations.
    ITaskRunColdOriginalProjectInput GetOriginalInitialInputForMetadataRebind(
        IDeveloperOriginalProjectMetadataRebindResult sameOriginalMetadataRebind);
    Task<ITaskRunOriginalProjectMetadataInputSuccessor> PrepareOriginalInitialInputMetadataSuccessorWithinSourceAsync(
        ITaskRunColdOriginalProjectInput sameOriginalInitialInput,
        IDeveloperOriginalProjectMetadataRebindResult sameOriginalMetadataRebind,
        Action<Action> originalSynchronousScope, Action<Task> retainOriginalTask, CancellationToken token);
    // Historical source proof only. Current resource permission requires the ordinary fresh READ.
    bool IsIssuedOriginalInitialInputMetadataSuccessor(ITaskRunOriginalProjectMetadataInputSuccessor sameSuccessor);
}
