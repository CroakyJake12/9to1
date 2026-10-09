using Haven.Application;
using Haven.Core;

namespace HavenOS.Apps.Assistants.Contracts;

// PRIVATE PROPOSAL. Not applied, compiled or execution-qualified.
// A selection is issued only by the configured Core owner after actual source observation.
// Its public values are observations; canonical Chat/Task/resource admission remains required.
public sealed class AssistantOriginalCapabilitySelection
{
    internal AssistantOriginalCapabilitySelection(object issuer, AssistantConversationBinding binding,
        AssistantDefinitionSnapshot definition, AuthenticatedResourceActor actor,
        IReadOnlyList<CapabilityDefinition> originalCatalogue, ToolAvailabilityPlan originalPlan,
        IReadOnlyList<ActiveCapability> activeCapabilities, IReadOnlyList<ToolCapability> requiredModelCapabilities,
        IReadOnlyList<AssistantCapabilityObservation> observations,
        PermissionMode filePermission, PermissionMode commandPermission, PermissionMode browserPermission,
        string? originalWorkspaceRoot, ITaskRunColdOriginalProjectInput? originalProjectInput,
        object originalContext)
    {
        Issuer = issuer; Binding = binding; Definition = definition; Actor = actor;
        OriginalCatalogue = originalCatalogue; OriginalPlan = originalPlan;
        ActiveCapabilities = activeCapabilities; RequiredModelCapabilities = requiredModelCapabilities;
        Observations = observations; FilePermission = filePermission; CommandPermission = commandPermission;
        BrowserPermission = browserPermission; OriginalWorkspaceRoot = originalWorkspaceRoot;
        OriginalProjectInput = originalProjectInput; OriginalContext = originalContext;
    }
    internal object Issuer { get; }
    internal object OriginalContext { get; }
    public AssistantConversationBinding Binding { get; }
    public AssistantDefinitionSnapshot Definition { get; }
    public AuthenticatedResourceActor Actor { get; }
    public IReadOnlyList<CapabilityDefinition> OriginalCatalogue { get; }
    public ToolAvailabilityPlan OriginalPlan { get; }
    public IReadOnlyList<ActiveCapability> ActiveCapabilities { get; }
    public IReadOnlyList<ToolCapability> RequiredModelCapabilities { get; }
    public IReadOnlyList<AssistantCapabilityObservation> Observations { get; }
    public PermissionMode FilePermission { get; }
    public PermissionMode CommandPermission { get; }
    public PermissionMode BrowserPermission { get; }
    public string? OriginalWorkspaceRoot { get; }
    // Borrowed SAME process-owned input only. This owner never issues or closes it.
    public ITaskRunColdOriginalProjectInput? OriginalProjectInput { get; }
}

public interface IAssistantOriginalCapabilityOwner
{
    Task<AssistantOriginalCapabilitySelection> ReadOriginalConfiguredCapabilitiesWithinSourceAsync(
        AssistantConversationBinding binding, AssistantDefinitionSnapshot actualDefinition,
        ModelDescriptor actualSelectedModel, Action<Action> originalSynchronousScope,
        Action<Task> retainOriginalTask, CancellationToken token,
        ITaskRunColdOriginalProjectInput? actualProjectInput = null);
    bool IsIssuedOriginalSelection(AssistantOriginalCapabilitySelection actualSelection);
    ModelRequestToolSelectionConstraints GetOriginalDispatchToolConstraints(AssistantOriginalCapabilitySelection actualSelection) =>
        throw new AssistantCommandRefusedException("The actual configured capability owner does not provide a concrete dispatch restriction.");
    Task RevalidateOriginalSelectionWithinSourceAsync(AssistantOriginalCapabilitySelection actualSelection,
        Action<Action> originalSynchronousScope, Action<Task> retainOriginalTask, CancellationToken token,
        ITaskRunColdOriginalProjectInput? actualProjectInput = null);
}
