using Haven.Core;
using HavenOS.Apps.Assistants.Contracts;
using HavenOS.Apps.Assistants.Core;

namespace HavenOS.Apps.Assistants.NativeUI;

/// <summary>Local custody of one explicit project creation. IDs are correlation only;
/// the SAME source-issued choice and canonical owner remain the command authority.</summary>
internal sealed class OriginalProjectTaskSubmission
{
    private bool _factoryEntered;
    private bool _acknowledged;
    private Task? _settledOriginalPreEffectRefusal;
    private AssistantCompatibleConversationCheckpoint? _settledDeclinedCheckpoint;
    private Task? SameOriginalCommand => (Task?)OriginalCompatibleCommand ?? OriginalCommand;

    internal OriginalProjectTaskSubmission(AssistantOriginalProjectChoice choice,
        AssistantIdentity identity, long definitionRevision, string title)
        : this(choice, identity, definitionRevision, title, Guid.NewGuid(), Guid.NewGuid(), null) { }

    private OriginalProjectTaskSubmission(AssistantOriginalProjectChoice choice, AssistantIdentity identity,
        long definitionRevision, string title, Guid conversationId, Guid creationOperationId,
        AssistantCompatibleConversationCheckpoint? resumeCheckpoint)
    {
        OriginalChoice = choice ?? throw new ArgumentNullException(nameof(choice));
        Identity = identity ?? throw new ArgumentNullException(nameof(identity));
        if (definitionRevision <= 0 || string.IsNullOrWhiteSpace(title))
            throw new ArgumentException("Retain the current definition and project Task title.");
        DefinitionRevision = definitionRevision; Title = title;
        ConversationId = conversationId; OperationId = creationOperationId; CommandOperationId = Guid.NewGuid();
        ResumeCheckpoint = resumeCheckpoint;
    }

    internal static OriginalProjectTaskSubmission Resume(AssistantCompatibleConversationCheckpoint checkpoint,
        AssistantOriginalProjectChoice freshChoice) => new(freshChoice, checkpoint.Identity, checkpoint.DefinitionRevision,
            checkpoint.Title, checkpoint.PlannedConversationId, checkpoint.OriginalCreationOperationId, checkpoint);
    internal AssistantCompatibleConversationCheckpoint? ResumeCheckpoint { get; }
    internal AssistantCompatibleConversationCheckpoint? DeclinedCheckpoint => _settledDeclinedCheckpoint;
    internal Guid CommandOperationId { get; }
    internal Task<AssistantCompatibleConversationCreationOutcome>? OriginalCompatibleCommand { get; private set; }
    internal AssistantOriginalProjectChoice OriginalChoice { get; }
    internal AssistantIdentity Identity { get; }
    internal long DefinitionRevision { get; }
    internal string Title { get; }
    internal Guid ConversationId { get; }
    internal Guid OperationId { get; }
    internal Task<AssistantsWorkspaceSnapshot>? OriginalCommand { get; private set; }
    internal bool IsAcknowledged => _acknowledged;
    internal bool CanSubmit => !_acknowledged && _settledDeclinedCheckpoint is null && (!_factoryEntered || SameOriginalCommand?.IsCompleted == true);
    internal bool CanRetry => _factoryEntered && CanSubmit;
    internal bool IsOriginalPreEffectRefused => SameOriginalCommand is not null &&
        ReferenceEquals(_settledOriginalPreEffectRefusal, SameOriginalCommand);
    internal bool HasUnresolvedAttempt => _factoryEntered && !_acknowledged &&
        _settledDeclinedCheckpoint is null && !IsOriginalPreEffectRefused;

    // Called only from the native SourceAsync branch that already queried the SAME
    // controller task's private issuer receipt. This settles no-effect presentation
    // custody; it is not success, recovery, permission, or a write acknowledgement.
    internal bool ObserveAcknowledgedOriginalPreEffectRefusal(Task actual)
    {
        if (!ReferenceEquals(SameOriginalCommand, actual) || !actual.IsFaulted) return false;
        _settledOriginalPreEffectRefusal = actual; return true;
    }

    internal void EnterOriginalFactory()
    {
        if (!CanSubmit) throw new InvalidOperationException("Join the SAME accepted project creation before another attempt.");
        _factoryEntered = true; OriginalCommand = null; OriginalCompatibleCommand = null; _settledOriginalPreEffectRefusal = null;
    }

    // Called immediately with the actual controller Task, before awaiting it. No
    // callbacks, authorization, local receipt or alternate success task is issued.
    internal void CaptureOriginalCommand(Task<AssistantsWorkspaceSnapshot> actual) => OriginalCommand = actual;

    internal void CaptureOriginalCompatibleCommand(Task<AssistantCompatibleConversationCreationOutcome> actual) => OriginalCompatibleCommand = actual;

    // The controller has already checked the SAME backend issuer. Local settlement
    // additionally requires the exact returned actual Task/result and full original
    // correlation. A DeclinedPending checkpoint is not a canonical binding or Task.
    internal void AcknowledgeCompatible(AssistantCompatibleConversationCreationOutcome actual)
    {
        if (OriginalCompatibleCommand?.IsCompletedSuccessfully != true ||
            !ReferenceEquals(OriginalCompatibleCommand.Result, actual))
            throw new InvalidOperationException("Retain the SAME completed canonical compatible-creation outcome.");
        if (actual.State == AssistantCompatibleConversationCreationState.Ready && actual.Checkpoint is null &&
            actual.Binding is { } binding && binding.Definition.Identity == Identity &&
            binding.Definition.Revision == DefinitionRevision && binding.Conversation.Id == ConversationId &&
            binding.Conversation.Mode == HavenMode.Tasks && binding.Conversation.Kind == ConversationKind.Task &&
            binding.Conversation.ContainerId is not null)
        {
            if (ResumeCheckpoint is { } priorReady && binding.Conversation != priorReady.OriginalTaskConversation)
                throw new InvalidOperationException("The ready resumed binding changed its original allocated conversation.");
            _acknowledged = true; return;
        }
        if (actual.State != AssistantCompatibleConversationCreationState.DeclinedPending || actual.Binding is not null ||
            actual.Checkpoint is not { } checkpoint || checkpoint.Identity != Identity ||
            checkpoint.DefinitionRevision != DefinitionRevision || checkpoint.PlannedConversationId != ConversationId ||
            checkpoint.OriginalCreationOperationId != OperationId || checkpoint.LastCommandOperationId != CommandOperationId ||
            checkpoint.Title != Title || checkpoint.Project != OriginalChoice.Project.Reference ||
            checkpoint.OriginalTaskConversation.Id != ConversationId ||
            checkpoint.OriginalTaskConversation.ContainerId != checkpoint.OriginalTaskContainer.Id ||
            checkpoint.OriginalTaskConversation.Mode != HavenMode.Tasks || checkpoint.OriginalTaskContainer.Mode != HavenMode.Tasks)
            throw new InvalidOperationException("No SAME durable pending checkpoint or ready binding was acknowledged.");
        if (ResumeCheckpoint is { } prior && (checkpoint.DenSessionId != prior.DenSessionId ||
            checkpoint.MembershipRevision <= prior.MembershipRevision || checkpoint.OriginalStoreIdentity != prior.OriginalStoreIdentity ||
            checkpoint.OriginalTaskConversation != prior.OriginalTaskConversation || checkpoint.OriginalTaskContainer != prior.OriginalTaskContainer ||
            checkpoint.OriginalStudioConversationId != prior.OriginalStudioConversationId ||
            checkpoint.OriginalStudioContainerId != prior.OriginalStudioContainerId))
            throw new InvalidOperationException("The acknowledged resume checkpoint changed its original allocated tuple.");
        _settledDeclinedCheckpoint = checkpoint; // Settles this finite attempt only after durable source acknowledgement.
    }

    internal void Acknowledge(AssistantsWorkspaceSnapshot actual)
    {
        if (OriginalCommand?.IsCompletedSuccessfully != true || !ReferenceEquals(OriginalCommand.Result, actual) ||
            actual.SelectedAssistant?.Identity != Identity || actual.ConversationBinding is not { } binding ||
            binding.Definition.Identity != Identity || binding.Conversation.Id != ConversationId ||
            binding.Conversation.Mode != HavenMode.Tasks || binding.Conversation.Kind != ConversationKind.Task ||
            actual.Conversation is not { Conversation: var conversation } || conversation.Id != ConversationId ||
            conversation.Mode != HavenMode.Tasks || conversation.Kind != ConversationKind.Task)
            throw new InvalidOperationException("No SAME completed canonical project Task conversation receipt was returned.");
        _acknowledged = true;
    }
}
