using Haven.Application;

namespace HavenOS.Apps.Assistants.Contracts;

/// <summary>A privately issued current observation of ONE SAME process-owned Home
/// project input. Saved paths/IDs, this object and a prior READ create no policy grant.
/// Its source owns the original input; capability planning only borrows it.</summary>
public sealed class AssistantOriginalPreparedProjectCapabilityContext
{
    internal AssistantOriginalPreparedProjectCapabilityContext(object issuer,
        AssistantConversationBinding binding, AssistantDefinitionSnapshot definition,
        AuthenticatedResourceActor actor, ITaskRunColdOriginalProjectInput originalInput,
        string originalWorkspaceRoot, object originalSourceState)
    {
        Issuer = issuer; Binding = binding; Definition = definition; Actor = actor;
        OriginalProjectInput = originalInput; OriginalWorkspaceRoot = originalWorkspaceRoot;
        OriginalSourceState = originalSourceState;
    }
    internal object Issuer { get; }
    internal object OriginalSourceState { get; }
    public AssistantConversationBinding Binding { get; }
    public AssistantDefinitionSnapshot Definition { get; }
    public AuthenticatedResourceActor Actor { get; }
    public ITaskRunColdOriginalProjectInput OriginalProjectInput { get; }
    public string OriginalWorkspaceRoot { get; }
}

public interface IAssistantOriginalPreparedProjectCapabilityContextOwner
{
    Task<AssistantOriginalPreparedProjectCapabilityContext> ReadOriginalPreparedProjectCapabilityContextWithinSourceAsync(
        AssistantConversationBinding binding, AssistantDefinitionSnapshot actualDefinition,
        ITaskRunColdOriginalProjectInput sameActualInput, Action<Action> originalSynchronousScope,
        Action<Task> retainOriginalTask, CancellationToken token);
    bool IsIssuedOriginalContext(AssistantOriginalPreparedProjectCapabilityContext sameActual);
    Task RevalidateOriginalContextWithinSourceAsync(AssistantOriginalPreparedProjectCapabilityContext sameActual,
        Action<Action> originalSynchronousScope, Action<Task> retainOriginalTask, CancellationToken token);
}
