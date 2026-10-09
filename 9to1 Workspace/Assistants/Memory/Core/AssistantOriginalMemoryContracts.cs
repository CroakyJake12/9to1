using Haven.Application;
using HavenOS.Apps.Assistants.Contracts;
using HavenOS.Home.Core;

namespace HavenOS.Apps.Assistants.Memory;

public interface IAssistantOriginalMemoryCapabilitySource
{
    // Pure composition observation only. It performs no content query and grants no access.
    AssistantCapabilityObservation ObserveOriginalMemoryCapability();
}

/// <summary>The SAME trusted source injected into canonical Chat. A saved preference or
/// serialized definition can request preparation but cannot construct a live source input.</summary>
public interface IAssistantOriginalPersistentMemoryInputOwner : IChatOriginalPersistentMemorySource
{
    bool HasOriginalComposition(HomePersonalDenFactory home, IConversationRepository conversations);
    Task<AssistantOriginalMemoryPreparation> PrepareOriginalAssistantMemoryInputWithinSourceAsync(
        AssistantConversationBinding sameBinding, AssistantDefinitionSnapshot actualDefinition,
        Action<Action> originalSynchronousScope, Action<Task> retainOriginalTask,
        CancellationToken token);
}

/// <summary>A source-issued pre-query outcome. Declining preparation grants nothing and
/// performs no model, memory-content or schema operation. Accepted inputs remain live only.
/// Consumers still verify the SAME source's IsIssuedOriginalInput before passing to Chat.</summary>
public sealed class AssistantOriginalMemoryPreparation
{
    internal AssistantOriginalMemoryPreparation(IChatOriginalPersistentMemoryInput? input, string reason, string? originalStorageScope = null)
    { Input = input; Reason = reason; OriginalStorageScope = originalStorageScope; }
    public bool IsPrepared => Input is not null;
    public IChatOriginalPersistentMemoryInput? Input { get; }
    public string Reason { get; }
    // Observational persistence metadata only; it does not authorize a memory write.
    public string? OriginalStorageScope { get; }
}
