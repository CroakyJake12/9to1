using HavenOS.Mail.Storage;

namespace HavenOS.Mail.Services;

/// <summary>Mail UI and API operations share this domain service; network actions require Home-mediated permissions.</summary>
public sealed class MailDomainService(IMailStateStore store)
{
    private readonly IMailStateStore _store = store ?? throw new ArgumentNullException(nameof(store));

    public async Task<IReadOnlyList<MailAccount>> ListAccountsAsync(CancellationToken cancellationToken = default) =>
        (await _store.ReadAsync(cancellationToken).ConfigureAwait(false)).Accounts;

    public async Task<MailResult<MailAccount>> GetAccountAsync(Guid accountId, CancellationToken cancellationToken = default)
    {
        var account = (await _store.ReadAsync(cancellationToken).ConfigureAwait(false)).Accounts.FirstOrDefault(item => item.AccountId == accountId);
        return account is null ? Fail<MailAccount>(MailErrorCode.AccountNotFound, "The mailbox account was not found.", "account", "9to1.Mail.GetAccount") : MailResult<MailAccount>.Success(account);
    }

    public async Task<MailSearchResult> SearchAsync(MailSearchQuery query, CancellationToken cancellationToken = default) =>
        MailSearchEngine.Search(await _store.ReadAsync(cancellationToken).ConfigureAwait(false), query);

    public async Task<MailResult<MailDraft>> UpdateDraftAsync(
        Guid draftId,
        long? expectedRevision,
        Func<MailDraft, MailDraft> update,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(update);
        try
        {
            MailDraft? result = null;
            await _store.TransactAsync(state =>
            {
                var current = state.Drafts.FirstOrDefault(draft => draft.DraftId == draftId);
                if (current is null) throw new MailStoreException(MailErrorCode.DraftNotFound, "The draft was not found.");
                var proposed = update(current);
                if (proposed.DraftId != current.DraftId || proposed.AccountId != current.AccountId)
                    throw new MailStoreException(MailErrorCode.InvalidInput, "A draft edit cannot change its stable draft or account identity.");
                if (expectedRevision is { } expected && current.Revision != expected)
                {
                    var conflict = current with { IsConflict = true, ConflictingRevision = proposed with { Revision = current.Revision + 1, IsConflict = false, ConflictingRevision = null } };
                    result = conflict;
                    return state with { Drafts = Replace(state.Drafts, conflict, current.DraftId) };
                }
                result = proposed with { Revision = current.Revision + 1, UpdatedAt = DateTimeOffset.UtcNow, IsConflict = false, ConflictingRevision = null };
                return state with { Drafts = Replace(state.Drafts, result, current.DraftId) };
            }, cancellationToken: cancellationToken).ConfigureAwait(false);
            return result is { IsConflict: true }
                ? Fail<MailDraft>(MailErrorCode.DraftConflict, "The draft changed elsewhere; both revisions were preserved for review.", $"draft:{draftId}", "9to1.Mail.Draft.Update", recoverable: true, details: new Dictionary<string, string> { ["currentRevision"] = result.Revision.ToString(), ["conflictRevision"] = result.ConflictingRevision!.Revision.ToString() })
                : MailResult<MailDraft>.Success(result!);
        }
        catch (MailStoreException ex)
        {
            return Fail<MailDraft>(ex.Code, ex.Message, $"draft:{draftId}", "9to1.Mail.Draft.Update", recoverable: ex.Code is MailErrorCode.Conflict);
        }
    }

    public async Task<MailResult<MailPendingOperation>> QueueOfflineOperationAsync(
        Guid accountId,
        MailOperationKind kind,
        Guid targetId,
        string idempotencyKey,
        string? expectedProviderRevision = null,
        IReadOnlyDictionary<string, string>? arguments = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(idempotencyKey);
        try
        {
            MailPendingOperation? operation = null;
            await _store.TransactAsync(state =>
            {
                if (state.Accounts.All(account => account.AccountId != accountId))
                    throw new MailStoreException(MailErrorCode.AccountNotFound, "The mailbox account was not found.");
                var existing = state.PendingOperations.FirstOrDefault(item => item.AccountId == accountId && item.IdempotencyKey == idempotencyKey);
                if (existing is not null) { operation = existing; return state; }
                operation = new MailPendingOperation(Guid.NewGuid(), accountId, kind, targetId, expectedProviderRevision,
                    idempotencyKey, DateTimeOffset.UtcNow, MailOperationState.Pending, null, null,
                    arguments is null ? new Dictionary<string, string>() : new Dictionary<string, string>(arguments));
                return state with { PendingOperations = [.. state.PendingOperations, operation] };
            }, cancellationToken: cancellationToken).ConfigureAwait(false);
            return MailResult<MailPendingOperation>.Success(operation!);
        }
        catch (MailStoreException ex)
        {
            return Fail<MailPendingOperation>(ex.Code, ex.Message, $"account:{accountId}", "9to1.Mail.OfflineOperation", recoverable: true);
        }
    }

    public async Task<MailResult<OutgoingMessage>> QueueSendAsync(
        MailDraft draft,
        bool isOnline,
        DateTimeOffset now,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(draft);
        ArgumentException.ThrowIfNullOrWhiteSpace(idempotencyKey);
        try
        {
            OutgoingMessage? outgoing = null;
            await _store.TransactAsync(state =>
            {
                if (state.Accounts.All(account => account.AccountId != draft.AccountId))
                    throw new MailStoreException(MailErrorCode.AccountNotFound, "The sending account was not found.");
                if (!state.Drafts.Any(item => item.DraftId == draft.DraftId && item.AccountId == draft.AccountId))
                    throw new MailStoreException(MailErrorCode.DraftNotFound, "The draft was not found.");
                var existingOp = state.PendingOperations.FirstOrDefault(item => item.AccountId == draft.AccountId && item.IdempotencyKey == idempotencyKey);
                if (existingOp is not null)
                {
                    outgoing = state.Outgoing.FirstOrDefault(item => item.DraftId == draft.DraftId);
                    if (outgoing is not null) return state;
                }
                var account = state.Accounts.Single(item => item.AccountId == draft.AccountId);
                var sendAt = now + account.Sending.UndoSendDelay;
                outgoing = new OutgoingMessage(Guid.NewGuid(), draft.DraftId, draft.AccountId,
                    isOnline ? MailMessageState.Queued : MailMessageState.Queued, now, sendAt, null, null, 0, null, null, true);
                var operation = new MailPendingOperation(Guid.NewGuid(), draft.AccountId, MailOperationKind.Send,
                    outgoing.MessageId, null, idempotencyKey, now, MailOperationState.Pending, null, null,
                    new Dictionary<string, string> { ["draftId"] = draft.DraftId.ToString("D"), ["holdUntil"] = sendAt.ToString("O"), ["onlineAtQueue"] = isOnline.ToString() });
                return state with { Outgoing = [.. state.Outgoing, outgoing], PendingOperations = [.. state.PendingOperations, operation] };
            }, cancellationToken: cancellationToken).ConfigureAwait(false);
            return MailResult<OutgoingMessage>.Success(outgoing!);
        }
        catch (MailStoreException ex)
        {
            return Fail<OutgoingMessage>(ex.Code, ex.Message, $"draft:{draft.DraftId}", "9to1.Mail.Send", recoverable: true);
        }
    }

    public async Task<MailResult<OutgoingMessage>> CancelQueuedSendAsync(Guid messageId, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        try
        {
            OutgoingMessage? result = null;
            await _store.TransactAsync(state =>
            {
                var current = state.Outgoing.FirstOrDefault(item => item.MessageId == messageId);
                if (current is null) throw new MailStoreException(MailErrorCode.MessageNotFound, "The outgoing message was not found.");
                if (current.State != MailMessageState.Queued || !current.IsUndoAvailable || current.HoldUntil is not { } until || now >= until)
                    throw new MailStoreException(MailErrorCode.SendRejected, "The send is no longer in its local undo window.");
                result = current with { State = MailMessageState.Cancelled, IsUndoAvailable = false };
                return state with
                {
                    Outgoing = Replace(state.Outgoing, result, messageId),
                    PendingOperations = state.PendingOperations.Select(op => op.TargetId == messageId && op.Kind == MailOperationKind.Send ? op with { State = MailOperationState.Applied } : op).ToArray(),
                };
            }, cancellationToken: cancellationToken).ConfigureAwait(false);
            return MailResult<OutgoingMessage>.Success(result!);
        }
        catch (MailStoreException ex)
        {
            return Fail<OutgoingMessage>(ex.Code, ex.Message, $"outgoing:{messageId}", "9to1.Mail.CancelQueued", recoverable: false);
        }
    }

    public async Task<MailResult<MailMessage>> MarkReadAsync(Guid messageId, bool value, string idempotencyKey, CancellationToken cancellationToken = default) =>
        await MutateMessageAsync(messageId, message => message with { IsRead = value }, MailOperationKind.MarkRead, idempotencyKey, cancellationToken).ConfigureAwait(false);

    public async Task<MailResult<MailMessage>> StarAsync(Guid messageId, bool value, string idempotencyKey, CancellationToken cancellationToken = default) =>
        await MutateMessageAsync(messageId, message => message with { IsStarred = value }, MailOperationKind.Star, idempotencyKey, cancellationToken).ConfigureAwait(false);

    public async Task<MailResult<MailMessage>> SnoozeAsync(Guid messageId, DateTimeOffset until, string idempotencyKey, CancellationToken cancellationToken = default) =>
        await MutateMessageAsync(message => message.MessageId == messageId ? message with { SnoozedUntil = until } : null, messageId, MailOperationKind.Snooze, idempotencyKey, cancellationToken).ConfigureAwait(false);

    private async Task<MailResult<MailMessage>> MutateMessageAsync(Guid messageId, Func<MailMessage, MailMessage> update, MailOperationKind kind, string idempotencyKey, CancellationToken cancellationToken) =>
        await MutateMessageAsync(message => message.MessageId == messageId ? update(message) : null, messageId, kind, idempotencyKey, cancellationToken).ConfigureAwait(false);

    private async Task<MailResult<MailMessage>> MutateMessageAsync(Func<MailMessage, MailMessage?> update, Guid messageId, MailOperationKind kind, string idempotencyKey, CancellationToken cancellationToken)
    {
        try
        {
            MailMessage? result = null;
            await _store.TransactAsync(state =>
            {
                var current = state.Messages.FirstOrDefault(message => message.MessageId == messageId);
                if (current is null) throw new MailStoreException(MailErrorCode.MessageNotFound, "The message was not found.");
                var currentOp = state.PendingOperations.FirstOrDefault(op => op.AccountId == current.AccountId && op.IdempotencyKey == idempotencyKey);
                if (currentOp is not null) { result = current; return state; }
                result = update(current) ?? throw new InvalidOperationException("Message update returned no message.");
                var operation = new MailPendingOperation(Guid.NewGuid(), current.AccountId, kind, messageId, current.ProviderRevision,
                    idempotencyKey, DateTimeOffset.UtcNow, MailOperationState.Pending, null, null, new Dictionary<string, string>());
                return state with { Messages = Replace(state.Messages, result, messageId), PendingOperations = [.. state.PendingOperations, operation] };
            }, cancellationToken: cancellationToken).ConfigureAwait(false);
            return MailResult<MailMessage>.Success(result!);
        }
        catch (MailStoreException ex)
        {
            return Fail<MailMessage>(ex.Code, ex.Message, $"message:{messageId}", $"9to1.Mail.{kind}", recoverable: true);
        }
    }

    private static IReadOnlyList<T> Replace<T>(IReadOnlyList<T> items, T replacement, Guid targetId) where T : notnull
    {
        var index = items switch
        {
            IReadOnlyList<MailDraft> drafts when typeof(T) == typeof(MailDraft) => drafts.ToList().FindIndex(item => item.DraftId == targetId),
            IReadOnlyList<MailMessage> messages when typeof(T) == typeof(MailMessage) => messages.ToList().FindIndex(item => item.MessageId == targetId),
            IReadOnlyList<OutgoingMessage> outgoing when typeof(T) == typeof(OutgoingMessage) => outgoing.ToList().FindIndex(item => item.MessageId == targetId),
            _ => -1,
        };
        if (index < 0) return items;
        var result = items.ToArray();
        result[index] = replacement;
        return result;
    }

    private static MailResult<T> Fail<T>(MailErrorCode code, string message, string target, string action, bool recoverable = false, IReadOnlyDictionary<string, string>? details = null) =>
        MailResult<T>.Failure(new MailError(code, message, target, action, recoverable, Details: details));
}
