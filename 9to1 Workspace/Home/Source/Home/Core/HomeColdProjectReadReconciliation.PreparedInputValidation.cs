using Haven.Application;
using Haven.Core;

namespace HavenOS.Home.Core;

public sealed partial class HomeColdProjectReadReconciliation
{
    /// <summary>Fresh validation of the SAME live process-prepared input before a
    /// canonical Task/cold input exists. This keeps the original individual Home
    /// READ and original input alive; it creates no input, permission or Task/Run.</summary>
    public Task ValidatePreparedOriginalProjectInputWithinSourceAsync(
        ITaskRunColdOriginalProjectInput sameInput, Conversation sameOriginalConversation,
        ContainerDefinition sameOriginalContainer, string exactProjectReference,
        Action<Action> scope, Action<Task> retain, CancellationToken token)
    {
        var original = RequireInput(sameInput, true);
        return original.StartValidation(scope, retain, async sources =>
        {
            sources.Invoke(() =>
            {
                original.DemandPreparedOriginalInput(sameOriginalConversation, sameOriginalContainer, exactProjectReference);
                return true;
            });
            await original.ValidateFreshShortReadAsync(sources, token).ConfigureAwait(false);
            sources.Invoke(() =>
            {
                original.DemandPreparedOriginalInput(sameOriginalConversation, sameOriginalContainer, exactProjectReference);
                return true;
            });
        });
    }

    private sealed partial class Work
    {
        internal void DemandPreparedOriginalInput(Conversation sameConversation,
            ContainerDefinition sameContainer, string exactReference)
        {
            DemandLive();
            // Reference identity here is the actual original pair passed to StartInput.
            // A consumer separately compares fresh repository observations by value;
            // a cloned public row cannot stand in for these issuer-owned originals.
            if (Material is not null || _command is not null || Driver?.IsCompletedSuccessfully != true ||
                !ReferenceEquals(Conversation, sameConversation) || !ReferenceEquals(Container, sameContainer) ||
                Conversation.ContainerId != Container.Id || Conversation.Mode != Container.Mode ||
                Identity is null || _facts.Reference != exactReference ||
                Identity.OriginalProjectContextJson != exactReference ||
                Hash(Container) != Identity.OriginalContainerSha256 ||
                Container.Instructions != Identity.OriginalContainerInstructions ||
                _facts.Conversation != Conversation || _facts.Container != Container ||
                _facts.Actor != _actor || Identity.OriginalHomeResourceActor != _actor)
                throw new UnauthorizedAccessException("Retain the SAME prepared original project input/conversation/container/reference.");
            DemandLive();
        }
    }
}
