using Haven.Application;

namespace HavenOS.Apps.Assistants.Contracts;

/// <summary>Optional original issuer/retirement projection. Selected paths and
/// observation strings do not authorize Files content or conversation writes.</summary>
public interface IAssistantOriginalAttachmentCommandSource : IAssistantOriginalAttachmentOwner
{
    bool IsAcknowledgedOriginalCommandRefusal(Task actual);
    bool HasOriginalComposition(HavenOS.Home.Core.HomePersonalDenFactory home, IConversationRepository conversations);
    Task? OriginalClose { get; }
    void DemandExternalOriginalRetirementJoin();
    void RequestOriginalRetirement();
    Task CloseAndDrainAsync();
}

public interface IAssistantOriginalAttachmentPicker
{
    Task<string?> PickOriginalAttachmentWithinSourceAsync(Action<Action> scope, Action<Task> retain, CancellationToken token);
}
