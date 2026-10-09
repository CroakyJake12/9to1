using Haven.Application;
using Haven.Core;

namespace HavenOS.Home.Core;

public sealed partial class HomeCanonicalAssistantAttachmentImportSource
{
    internal static bool SupportsOriginalAction(string action) => action is ImportAction or DetachAction;
    private sealed record OriginalMutation(Guid OperationId, AuthenticatedResourceActor Actor,
        CanonicalAttachmentDraftMutation Mutation, string IntentDigest,
        Conversation Conversation, ConversationDraft? OriginalDraft, ConversationDraft CandidateDraft,
        MessageAttachment Attachment, ICanonicalAttachmentOriginalSelection? Selection,
        OriginalMessageAttachmentProcessingResult? Processing,
        VerifiedResourceStoreOwnership StoreOwnership, VerifiedResourceStoreOwnership DenOwnership)
    {
        internal string Action => Mutation == CanonicalAttachmentDraftMutation.Import ? ImportAction : DetachAction;
        internal string ReviewMessage => Mutation == CanonicalAttachmentDraftMutation.Import
            ? "Attach this approved file to this Assistant conversation draft. The original file remains unchanged."
            : "Remove this attachment from this Assistant conversation draft. Keep the original file and saved attachment history.";
    }
    private static OriginalMutation CaptureOriginalMutation(ICanonicalAttachmentImportSource source,
        ICanonicalAttachmentImportIntent intent)
    {
        // The known configured producer's private issuance proof precedes ALL metadata.
        if (!source.IsIssuedOriginalImportIntent(intent))
            throw new UnauthorizedAccessException("The actual attachment producer did not issue this intent.");
        var observed = new OriginalMutation(intent.OperationId, intent.Actor, intent.Mutation,
            source.GetOriginalImportIntentDigest(intent), intent.OriginalConversation, intent.OriginalDraft,
            intent.CandidateDraft, intent.Attachment, intent.OriginalSelection, intent.OriginalProcessing,
            intent.OriginalStoreOwnership, intent.OriginalDenOwnership);
        if (observed.OperationId == Guid.Empty || observed.Actor is null || !IsDigest(observed.IntentDigest) ||
            observed.Conversation.Id == Guid.Empty || observed.Attachment.Id == Guid.Empty ||
            observed.Attachment.ConversationId != observed.Conversation.Id ||
            observed.CandidateDraft.ConversationId != observed.Conversation.Id ||
            observed.Attachment.BranchId != observed.CandidateDraft.BranchId ||
            (observed.OriginalDraft is { } prior && (prior.ConversationId != observed.Conversation.Id || prior.BranchId != observed.CandidateDraft.BranchId)) ||
            string.IsNullOrWhiteSpace(intent.DenId) || string.IsNullOrWhiteSpace(intent.NamespaceId) ||
            string.IsNullOrWhiteSpace(intent.DefinitionId) || string.IsNullOrWhiteSpace(intent.SessionId) ||
            intent.DefinitionRevision < 1 || intent.SessionRevision < 1)
            throw new UnauthorizedAccessException("The actual current conversation, draft and attachment projection is invalid.");
        if (observed.Mutation == CanonicalAttachmentDraftMutation.Import)
        {
            if (observed.Selection is null || observed.Processing is null || observed.Selection.OriginalActor != observed.Actor ||
                observed.Selection.OriginalFile != observed.Processing.OriginalFile)
                throw new UnauthorizedAccessException("Import requires the same source-issued selected file and processing observation.");
        }
        else if (observed.Mutation != CanonicalAttachmentDraftMutation.Detach || observed.Selection is not null || observed.Processing is not null)
            throw new UnauthorizedAccessException("Detach cannot borrow another file's read or processing observation.");
        return observed;
    }
    private static bool IsDigest(string? value) => value is { Length: 64 } && value.All(Uri.IsHexDigit);
    private sealed partial class Claim
    {
        private OriginalMutation _originalMutation = null!;
        internal string? OriginalAction => _originalMutation?.Action;
        private void DemandSameOriginalMutation()
        {
            var current = CaptureOriginalMutation(owner._creator, intent);
            if (_originalMutation is null || current != _originalMutation || Actor != current.Actor ||
                !ReferenceEquals(current.Conversation, _originalMutation.Conversation) ||
                !ReferenceEquals(current.OriginalDraft, _originalMutation.OriginalDraft) ||
                !ReferenceEquals(current.CandidateDraft, _originalMutation.CandidateDraft) ||
                !ReferenceEquals(current.Attachment, _originalMutation.Attachment) ||
                !ReferenceEquals(current.Selection, _originalMutation.Selection) ||
                !ReferenceEquals(current.Processing, _originalMutation.Processing) ||
                !ReferenceEquals(current.StoreOwnership, _ownership) || !ReferenceEquals(current.DenOwnership, _denOwnership))
                throw new UnauthorizedAccessException("The exact attachment action, actor, draft or producer digest changed after review.");
        }
        private (string Code, bool Applied) CaptureAcknowledgedDecisionAudit(ICanonicalAttachmentImportAcknowledgment acknowledgment)
        {
            // The caller already proved the same issuer, raw atomic Task and ACK.
            if (!ReferenceEquals(acknowledgment.OriginalIntent, intent) || !ReferenceEquals(acknowledgment.Attachment, intent.Attachment))
                throw new UnauthorizedAccessException("Another attachment acknowledgment cannot settle this WRITE.");
            var applied = acknowledgment.Applied;
            if (applied && !ReferenceEquals(acknowledgment.Draft, intent.CandidateDraft))
                throw new InvalidDataException("The applied attachment acknowledgment does not name the exact candidate draft.");
            return (applied ? _originalMutation.Mutation == CanonicalAttachmentDraftMutation.Import
                ? "HOME_ASSISTANT_ATTACHMENT_IMPORTED" : "HOME_ASSISTANT_ATTACHMENT_DETACHED"
                : "HOME_ASSISTANT_ATTACHMENT_NOT_APPLIED", applied);
        }
    }
}
