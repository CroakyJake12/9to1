using Haven.Application;
using HavenOS.Apps.Assistants.Canonical;
using HavenOS.Apps.Assistants.Contracts;
using HavenOS.Apps.Assistants.Memory;
using HavenOS.Home.Core;

namespace HavenOS.Apps.Assistants.Core;

/// <summary>Creates only a dedicated presentation scope over actual host-owned services.
/// The application keeps and drains the ordinary business host independently at process exit.</summary>
public static class AssistantsWorkspaceFactory
{
    public static AssistantsWorkspaceController Create(
        HomePersonalDenFactory actualHomeDen,
        IConversationRepository sameConversations,
        IConversationProductionRepository sameConversationProduction,
        ChatSessionService sameChat,
        TaskExecutionCoordinator sameTasks,
        AssistantOriginalConversationHost applicationOwnedOrdinaryHost,
        IAssistantOriginalModelSelectionOwner? actualModels = null,
        IAssistantOriginalAttachmentOwner? actualAttachments = null,
        IAssistantOriginalDevelopmentOwner? actualDevelopment = null,
        IAssistantOriginalCapabilityOwner? actualCapabilities = null,
        IAssistantOriginalPersistentMemoryInputOwner? actualMemory = null)
    {
        ArgumentNullException.ThrowIfNull(actualHomeDen);
        ArgumentNullException.ThrowIfNull(sameConversations);
        ArgumentNullException.ThrowIfNull(sameConversationProduction);
        ArgumentNullException.ThrowIfNull(sameChat);
        ArgumentNullException.ThrowIfNull(sameTasks);
        ArgumentNullException.ThrowIfNull(applicationOwnedOrdinaryHost);
        return new(new DenAssistantCanonicalBridge(actualHomeDen, sameConversations,
            sameConversationProduction, sameChat, sameTasks, applicationOwnedOrdinaryHost,
            actualModels, actualAttachments, actualDevelopment, actualCapabilities, actualMemory));
    }
}
