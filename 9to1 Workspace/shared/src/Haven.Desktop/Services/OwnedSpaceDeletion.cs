using Haven.Application;

namespace Haven.Desktop.Services;

/// <summary>Resumes canonical settings/SQL work without representing those separate stores as one transaction.
/// Production use requires all Space membership writers to honor the archived Space admission.</summary>
internal sealed class OwnedSpaceDeletion(SpaceRegistry spaces, OwnedSpaceConversations conversations)
{
    public async Task<SpaceDeletionOperation> BeginAsync(Guid spaceId, long expectedRevision, Guid operationId,
        CancellationToken token)
    {
        var pending = await spaces.BeginDeletionAsync(spaceId, expectedRevision, operationId, token).ConfigureAwait(false);
        return await ResumeAsync(pending.OperationId, token).ConfigureAwait(false);
    }

    public async Task<SpaceDeletionOperation> ResumeAsync(Guid operationId, CancellationToken token)
    {
        var operation = await spaces.ReadDeletionAsync(operationId, token).ConfigureAwait(false)
            ?? throw new KeyNotFoundException("This Space deletion is unavailable.");
        if (operation.Stage == SpaceDeletionStage.Complete)
        {
            await spaces.ClearDeletedSelectionAsync(operationId, token).ConfigureAwait(false);
            return operation;
        }
        // Each acknowledged batch is durable. A cancellation or denial leaves its existing journal
        // entry pending; the next invocation reads remaining memberships, never replays old rows.
        for (var attempt = 0; attempt < 32; attempt++)
        {
            token.ThrowIfCancellationRequested();
            var page = await conversations.ReadDeletionMembershipAsync(operation, token).ConfigureAwait(false);
            if (page.Status != ConversationSpaceReadStatus.Available || page.SpaceId != operation.SpaceId)
                throw new UnauthorizedAccessException("The canonical conversation store changed during deletion recovery.");
            if (page.Rows.Count == 0)
            {
                if (page.HasMore) throw new InvalidDataException("Conversation membership pagination is inconsistent.");
                var completed = await spaces.CompleteDeletionAsync(operationId, operation.ArchivedRevision, token).ConfigureAwait(false);
                await spaces.ClearDeletedSelectionAsync(operationId, token).ConfigureAwait(false);
                return completed;
            }
            var result = await conversations.DetachForDeletionAsync(operation, page.Rows, token).ConfigureAwait(false);
            if (result.Status is ConversationSpaceCommitStatus.AdmissionRejected or ConversationSpaceCommitStatus.StoreMismatch)
                throw new UnauthorizedAccessException("Deletion remains pending because current ownership changed.");
            // Conflicts reread the first remaining page. No cursor skips a concurrently changed row.
        }
        return operation; // Explicit pending progress; the caller offers Resume instead of claiming completion.
    }
}
