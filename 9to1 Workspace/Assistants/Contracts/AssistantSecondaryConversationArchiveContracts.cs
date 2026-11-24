namespace HavenOS.Apps.Assistants.Contracts;

/// <summary>Acts only on an exact issued secondary ordinary conversation; neither deletes nor stops work.</summary>
public interface IAssistantSecondaryConversationArchiveOwner
{
    Task SetSecondaryConversationArchivedAsync(AssistantConversationBinding binding, bool archived,
        CancellationToken token = default);
}
