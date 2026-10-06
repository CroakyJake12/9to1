using Haven.Core;
namespace Haven.Application;
/// <summary>Fresh SAME configured Files container lookup for authenticated restoration
/// expectations. No captured path/DTO or old Save/ACK substitutes for the current row.</summary>
public interface IDeveloperOriginalCurrentProjectRestorationSelectionSource
{
    Task<IDeveloperOriginalCurrentProjectSelection> SelectOriginalRestorationWithinSourceAsync(
        Conversation sameConversation, Guid actualContainerId, string expectedContainerSha256,
        string exactProjectReferenceJson, string expectedWorkspaceDocumentSha256,
        string expectedRegisteredRootFingerprint, Action<Action> originalSynchronousScope,
        Action<Task> retainOriginalTask, CancellationToken token);
}
