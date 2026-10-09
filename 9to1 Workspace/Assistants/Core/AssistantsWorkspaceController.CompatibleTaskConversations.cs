using HavenOS.Apps.Assistants.Contracts;

namespace HavenOS.Apps.Assistants.Core;

public sealed partial class AssistantsWorkspaceController
{
    public Task<AssistantCompatibleConversationCreationOutcome> CreateCompatibleConversationOriginalAsync(
        AssistantOriginalProjectChoice actualChoice, Guid conversationId, string title,
        Guid originalCreationOperationId, Guid commandOperationId, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(actualChoice);
        var definition = DemandSelected(); var generation = BeginNavigation();
        return CommandAsync(async () =>
        {
            var owner = DemandCompatibleCheckpointOwner();
            var outcome = await ObserveSourceAsync(() => owner.CreateOriginalCompatibleConversationAsync(
                definition.Identity, definition.Revision, conversationId, title, originalCreationOperationId,
                commandOperationId, actualChoice, token)).ConfigureAwait(false);
            await PublishCompatibleOutcomeAsync(owner, outcome, definition, generation, token).ConfigureAwait(false);
            return outcome;
        }, true, token);
    }

    public Task<IReadOnlyList<AssistantCompatibleConversationCheckpoint>> ReadOriginalDeclinedPendingAsync(
        int maximum = 32, CancellationToken token = default)
    {
        var definition = DemandSelected(); var generation = CurrentNavigation();
        return CommandAsync(async () =>
        {
            var owner = DemandCompatibleCheckpointOwner();
            var actual = await ObserveSourceAsync(() => owner.ReadOriginalDeclinedPendingAsync(
                definition.Identity, definition.Revision, maximum, token)).ConfigureAwait(false);
            IReadOnlyList<AssistantCompatibleConversationCheckpoint> observed;
            using (EnterSynchronousSource())
            {
                observed = Freeze(actual);
                foreach (var checkpoint in observed)
                    DemandActualCompatibleCheckpoint(owner, checkpoint, definition);
            }
            Publish(state => IsCurrentNavigation(generation)
                ? state with { DeclinedPendingConversations = observed, Error = null } : state);
            return observed;
        }, false, token);
    }

    public Task<AssistantCompatibleConversationCreationOutcome> ResumeCompatibleConversationOriginalAsync(
        AssistantCompatibleConversationCheckpoint sameCheckpoint, Guid newCommandOperationId,
        AssistantOriginalProjectChoice freshActualChoice, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(sameCheckpoint); ArgumentNullException.ThrowIfNull(freshActualChoice);
        var definition = DemandSelected(); var generation = BeginNavigation();
        return CommandAsync(async () =>
        {
            var owner = DemandCompatibleCheckpointOwner();
            using (EnterSynchronousSource()) DemandActualCompatibleCheckpoint(owner, sameCheckpoint, definition);
            var outcome = await ObserveSourceAsync(() => owner.ResumeOriginalCompatibleConversationAsync(
                sameCheckpoint, definition.Revision, newCommandOperationId, freshActualChoice, token)).ConfigureAwait(false);
            await PublishCompatibleOutcomeAsync(owner, outcome, definition, generation, token).ConfigureAwait(false);
            return outcome;
        }, true, token);
    }

    private IAssistantOriginalCompatibleConversationCheckpointOwner DemandCompatibleCheckpointOwner() =>
        _bridge as IAssistantOriginalCompatibleConversationCheckpointOwner ??
        throw IssueLocalRefusal("The current canonical host does not provide recoverable compatible project creation.");

    private void DemandActualCompatibleCheckpoint(IAssistantOriginalCompatibleConversationCheckpointOwner owner,
        AssistantCompatibleConversationCheckpoint actual, AssistantDefinitionSnapshot definition)
    {
        if (!owner.IsIssuedOriginalCheckpoint(actual) || actual.Identity != definition.Identity)
            throw new UnauthorizedAccessException("The actual current identity owner did not issue this pending checkpoint.");
        if (actual.DefinitionRevision != definition.Revision)
            throw IssueLocalRefusal("The configured Assistant changed. Read a fresh pending checkpoint before resuming this operation.");
    }

    private async Task PublishCompatibleOutcomeAsync(IAssistantOriginalCompatibleConversationCheckpointOwner owner,
        AssistantCompatibleConversationCreationOutcome outcome, AssistantDefinitionSnapshot definition,
        long generation, CancellationToken token)
    {
        using (EnterSynchronousSource())
        {
            if (!owner.IsIssuedOriginalCreationOutcome(outcome))
                throw new UnauthorizedAccessException("The configured canonical owner did not issue this exact creation outcome.");
            if (outcome.Checkpoint is { } checkpoint)
                DemandActualCompatibleCheckpoint(owner, checkpoint, definition);
            else if (outcome.Binding?.Definition.Identity != definition.Identity)
                throw new UnauthorizedAccessException("The actual compatible conversation belongs to another identity.");
        }
        if (outcome.Binding is { } binding)
        {
            await ReadAndPublishConversationAsync(binding, generation, token).ConfigureAwait(false);
            Publish(state => IsCurrentNavigation(generation) ? state with
            {
                DeclinedPendingConversations = Freeze(state.DeclinedPendingConversations
                    .Where(value => value.PlannedConversationId != binding.Conversation.Id)),
                LastCompatibleConversationOutcome = outcome, Error = null
            } : state);
        }
        else if (outcome.Checkpoint is { } pending)
            Publish(state => IsCurrentNavigation(generation) ? state with
            {
                // A declined WRITE has no ready canonical binding. The acknowledged
                // checkpoint is retained for explicit fresh READ/WRITE on a later attempt.
                ConversationBinding = null, Conversation = null, Work = null, Models = [],
                DeclinedPendingConversations = Freeze(state.DeclinedPendingConversations
                    .Where(value => value.DenSessionId != pending.DenSessionId).Append(pending)),
                LastCompatibleConversationOutcome = outcome, Error = null
            } : state);
    }
}
