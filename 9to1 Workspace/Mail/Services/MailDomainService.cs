using HavenOS.Mail.Storage;
using HavenOS.Mail.Providers;

namespace HavenOS.Mail.Services;

/// <summary>Mail UI and API operations share this domain service; network actions require Home-mediated permissions.</summary>
public sealed class MailDomainService(IMailStateStore store, IEnumerable<IMailProviderAdapter>? providers = null, IMailExternalActionAuthorizer? authorizer = null)
{
    private readonly IMailStateStore _store = store ?? throw new ArgumentNullException(nameof(store));
    private readonly IReadOnlyDictionary<MailProviderKind, IMailProviderAdapter> _providers = (providers ?? []).ToDictionary(provider => provider.Provider);
    private readonly IMailExternalActionAuthorizer? _authorizer = authorizer;

    public async Task<IReadOnlyList<MailAccount>> ListAccountsAsync(CancellationToken cancellationToken = default) =>
        (await _store.ReadAsync(cancellationToken).ConfigureAwait(false)).Accounts;

    public async Task<MailResult<MailAccount>> AddAccountAsync(MailAccount account, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(account);
        try
        {
            if (account.AccountId == Guid.Empty || string.IsNullOrWhiteSpace(account.ProviderAccountIdentity) || string.IsNullOrWhiteSpace(account.PrimaryAddress))
                throw new MailStoreException(MailErrorCode.InvalidInput, "Account ID, provider account identity and primary address are required.");
            account.Connection?.Validate();
            await _store.TransactAsync(state =>
            {
                if (state.Accounts.Any(existing => existing.AccountId == account.AccountId))
                    throw new MailStoreException(MailErrorCode.Conflict, "This account identity is already connected.");
                return state with { Accounts = [.. state.Accounts, account] };
            }, cancellationToken: cancellationToken).ConfigureAwait(false);
            return MailResult<MailAccount>.Success(account);
        }
        catch (MailStoreException ex)
        {
            return Fail<MailAccount>(ex.Code, ex.Message, $"account:{account.AccountId}", "9to1.Mail.Account.Add", recoverable: true);
        }
    }

    public async Task<IReadOnlyList<MailFolder>> ListFoldersAsync(Guid accountId, CancellationToken cancellationToken = default) =>
        (await _store.ReadAsync(cancellationToken).ConfigureAwait(false)).Folders.Where(folder => folder.AccountId == accountId).OrderBy(folder => folder.Name, StringComparer.OrdinalIgnoreCase).ToArray();

    public async Task<MailSearchResult> ListMessagesAsync(MailSearchQuery query, CancellationToken cancellationToken = default) =>
        await SearchAsync(query, cancellationToken).ConfigureAwait(false);

    public async Task<MailResult<MailMessage>> GetMessageAsync(Guid messageId, CancellationToken cancellationToken = default)
    {
        var message = (await _store.ReadAsync(cancellationToken).ConfigureAwait(false)).Messages.FirstOrDefault(item => item.MessageId == messageId && !item.IsDeleted);
        return message is null ? Fail<MailMessage>(MailErrorCode.MessageNotFound, "The message was not found.", $"message:{messageId}", "9to1.Mail.GetMessage") : MailResult<MailMessage>.Success(message);
    }

    public async Task<MailResult<MailThread>> GetThreadAsync(Guid threadId, CancellationToken cancellationToken = default)
    {
        var thread = (await _store.ReadAsync(cancellationToken).ConfigureAwait(false)).Threads.FirstOrDefault(item => item.ThreadId == threadId);
        return thread is null ? Fail<MailThread>(MailErrorCode.ThreadNotFound, "The conversation was not found.", $"thread:{threadId}", "9to1.Mail.GetThread") : MailResult<MailThread>.Success(thread);
    }

    public async Task<MailResult<MailProviderSyncBatch>> SyncAccountAsync(Guid accountId, CancellationToken cancellationToken = default)
    {
        var snapshot = await _store.ReadAsync(cancellationToken).ConfigureAwait(false);
        var account = snapshot.Accounts.FirstOrDefault(item => item.AccountId == accountId);
        if (account is null) return Fail<MailProviderSyncBatch>(MailErrorCode.AccountNotFound, "The mailbox account was not found.", $"account:{accountId}", "9to1.Mail.Sync");
        if (!_providers.TryGetValue(account.Provider, out var provider))
            return Fail<MailProviderSyncBatch>(MailErrorCode.ProviderUnavailable, "The provider adapter is unavailable. Cached mail remains available.", $"account:{accountId}", "9to1.Mail.Sync", recoverable: true);
        if (!account.IsAuthenticated)
            return Fail<MailProviderSyncBatch>(MailErrorCode.AuthenticationRequired, "Reconnect this account before syncing.", $"account:{accountId}", "9to1.Mail.Sync", recoverable: true);
        await SetSyncStatusAsync(account, MailSyncState.Syncing, null, null, cancellationToken).ConfigureAwait(false);
        try
        {
            var batch = await provider.SynchronizeAsync(account, account.ChangeCursor, cancellationToken).ConfigureAwait(false);
            var updated = await _store.TransactAsync(state =>
            {
                var folders = state.Folders.Where(item => item.AccountId != accountId).Concat(batch.Folders).GroupBy(item => item.FolderId).Select(group => group.Last()).ToArray();
                var messages = state.Messages.Where(item => item.AccountId != accountId).Concat(
                    state.Messages.Where(item => item.AccountId == accountId && batch.Messages.All(incoming => incoming.ProviderMessageId != item.ProviderMessageId || incoming.FolderKey != item.FolderKey)))
                    .Concat(batch.Messages).GroupBy(item => item.MessageId).Select(group => group.Last()).ToArray();
                var accountNow = state.Accounts.First(item => item.AccountId == accountId) with
                {
                    SyncState = MailSyncState.Succeeded,
                    LastSuccessfulContact = batch.ContactedAt,
                    LastSyncError = null,
                    ChangeCursor = batch.NewChangeCursor,
                    Capabilities = state.Accounts.First(item => item.AccountId == accountId).Capabilities | provider.Capabilities,
                };
                var threads = RebuildThreads(messages, accountId);
                return state with
                {
                    Accounts = ReplaceAccount(state.Accounts, accountNow),
                    Folders = folders,
                    Messages = messages,
                    Threads = state.Threads.Where(item => item.AccountId != accountId).Concat(threads).ToArray(),
                };
            }, cancellationToken: cancellationToken).ConfigureAwait(false);
            return MailResult<MailProviderSyncBatch>.Success(batch);
        }
        catch (OperationCanceledException) { await SetSyncStatusAsync(account, MailSyncState.Offline, "Sync was cancelled.", account.LastSuccessfulContact, CancellationToken.None).ConfigureAwait(false); throw; }
        catch (MailProviderException ex)
        {
            await SetSyncStatusAsync(account, ex.Code == MailErrorCode.AuthenticationRequired ? MailSyncState.Failed : MailSyncState.Failed, ex.Message, account.LastSuccessfulContact, CancellationToken.None).ConfigureAwait(false);
            return Fail<MailProviderSyncBatch>(ex.Code, ex.Message, $"account:{accountId}", "9to1.Mail.Sync", ex.IsRetryable);
        }
    }

    public async Task<MailResult<MailDraft>> CreateDraftAsync(MailDraft draft, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(draft);
        try
        {
            MailDraft created = draft with { DraftId = draft.DraftId == Guid.Empty ? Guid.NewGuid() : draft.DraftId, Revision = 1, UpdatedAt = DateTimeOffset.UtcNow, IsConflict = false, ConflictingRevision = null, IsDeleted = false };
            await _store.TransactAsync(state =>
            {
                if (state.Accounts.All(account => account.AccountId != created.AccountId)) throw new MailStoreException(MailErrorCode.AccountNotFound, "The mailbox account was not found.");
                if (state.Drafts.Any(item => item.DraftId == created.DraftId)) throw new MailStoreException(MailErrorCode.Conflict, "This draft identity is already in use.");
                return state with { Drafts = [.. state.Drafts, created] };
            }, cancellationToken: cancellationToken).ConfigureAwait(false);
            return MailResult<MailDraft>.Success(created);
        }
        catch (MailStoreException ex) { return Fail<MailDraft>(ex.Code, ex.Message, "draft", "9to1.Mail.Draft.Create", recoverable: true); }
    }

    public async Task<MailResult<MailDraft>> DeleteDraftAsync(Guid draftId, long expectedRevision, CancellationToken cancellationToken = default)
    {
        try
        {
            MailDraft? deleted = null;
            await _store.TransactAsync(state =>
            {
                var draft = state.Drafts.FirstOrDefault(item => item.DraftId == draftId && !item.IsDeleted)
                    ?? throw new MailStoreException(MailErrorCode.DraftNotFound, "The draft was not found.");
                if (draft.Revision != expectedRevision) throw new MailStoreException(MailErrorCode.DraftConflict, "The draft changed before it could be deleted.");
                deleted = draft with { IsDeleted = true, Revision = draft.Revision + 1, UpdatedAt = DateTimeOffset.UtcNow };
                return state with { Drafts = ReplaceDraft(state.Drafts, deleted) };
            }, cancellationToken: cancellationToken).ConfigureAwait(false);
            return MailResult<MailDraft>.Success(deleted!);
        }
        catch (MailStoreException ex) { return Fail<MailDraft>(ex.Code, ex.Message, $"draft:{draftId}", "9to1.Mail.Draft.Delete", recoverable: ex.Code == MailErrorCode.DraftConflict); }
    }

    public async Task<MailResult<MailSmartView>> CreateSmartViewAsync(string name, MailSearchQuery query, CancellationToken cancellationToken = default)
    {
        try
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(name);
            query.Validate();
            var now = DateTimeOffset.UtcNow;
            var view = new MailSmartView(Guid.NewGuid(), name.Trim(), query, now, now);
            await _store.TransactAsync(state => state with { SmartViews = [.. state.SmartViews, view] }, cancellationToken: cancellationToken).ConfigureAwait(false);
            return MailResult<MailSmartView>.Success(view);
        }
        catch (ArgumentException ex) { return Fail<MailSmartView>(MailErrorCode.InvalidInput, ex.Message, "smart-view", "9to1.Mail.SmartViews.Create"); }
    }

    public async Task<IReadOnlyList<MailSmartView>> ListSmartViewsAsync(CancellationToken cancellationToken = default) =>
        (await _store.ReadAsync(cancellationToken).ConfigureAwait(false)).SmartViews;

    public async Task<MailResult<MailSettings>> UpdateSettingsAsync(MailSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (settings.CacheRecentBodyDays is < 0 or > 3650 || settings.UndoSendDelay < TimeSpan.Zero || settings.UndoSendDelay > TimeSpan.FromMinutes(2))
            return Fail<MailSettings>(MailErrorCode.InvalidInput, "Mail cache days or undo-send delay is outside the supported range.", "settings", "9to1.Mail.Settings.Update");
        await _store.TransactAsync(state => state with { Settings = settings }, cancellationToken: cancellationToken).ConfigureAwait(false);
        return MailResult<MailSettings>.Success(settings);
    }

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
                    return state with { Drafts = ReplaceDraft(state.Drafts, conflict) };
                }
                result = proposed with { Revision = current.Revision + 1, UpdatedAt = DateTimeOffset.UtcNow, IsConflict = false, ConflictingRevision = null };
                return state with { Drafts = ReplaceDraft(state.Drafts, result) };
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

    /// <summary>Schedules a send locally. Provider-side scheduling is advertised only by an adapter with a native scheduling implementation.</summary>
    public async Task<MailResult<OutgoingMessage>> QueueScheduledSendAsync(
        Guid draftId,
        DateTimeOffset sendAt,
        MailExecutionPolicy policy,
        DateTimeOffset now,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(idempotencyKey);
        if (!Enum.IsDefined(policy) || sendAt <= now)
            return Fail<OutgoingMessage>(MailErrorCode.InvalidInput, "Choose a valid future send time and execution policy.", $"draft:{draftId}", "9to1.Mail.ScheduleSend");

        try
        {
            OutgoingMessage? outgoing = null;
            await _store.TransactAsync(state =>
            {
                var draft = state.Drafts.FirstOrDefault(item => item.DraftId == draftId && !item.IsDeleted)
                    ?? throw new MailStoreException(MailErrorCode.DraftNotFound, "The draft was not found.");
                var existing = state.PendingOperations.FirstOrDefault(item => item.AccountId == draft.AccountId && item.IdempotencyKey == idempotencyKey);
                if (existing is not null)
                {
                    outgoing = state.Outgoing.FirstOrDefault(item => item.MessageId == existing.TargetId);
                    return state;
                }
                var account = state.Accounts.FirstOrDefault(item => item.AccountId == draft.AccountId)
                    ?? throw new MailStoreException(MailErrorCode.AccountNotFound, "The sending account was not found.");
                // No current Mail adapter implements provider-side scheduling. ProviderPreferred therefore
                // uses an explicitly disclosed device schedule instead of promising a server-side send.
                var schedule = new MailScheduledSend(sendAt, ScheduledExecutionLocation.Device,
                    policy == MailExecutionPolicy.DeviceOnly
                        ? "Runs on this device while 9to1 Mail is available and online."
                        : "Provider scheduling is unavailable; runs on this device while 9to1 Mail is available and online.");
                var holdUntil = now + account.Sending.UndoSendDelay;
                outgoing = new OutgoingMessage(Guid.NewGuid(), draft.DraftId, draft.AccountId, MailMessageState.Queued,
                    now, holdUntil, sendAt, schedule, 0, null, null, account.Sending.UndoSendDelay > TimeSpan.Zero);
                var operation = new MailPendingOperation(Guid.NewGuid(), draft.AccountId, MailOperationKind.Send,
                    outgoing.MessageId, null, idempotencyKey, now, MailOperationState.Pending, null, null,
                    new Dictionary<string, string>
                    {
                        ["draftId"] = draft.DraftId.ToString("D"),
                        ["scheduledAt"] = sendAt.ToUniversalTime().ToString("O"),
                        ["executionLocation"] = schedule.Location.ToString(),
                        ["executionGuarantee"] = schedule.Guarantee,
                    });
                return state with { Outgoing = [.. state.Outgoing, outgoing], PendingOperations = [.. state.PendingOperations, operation] };
            }, cancellationToken: cancellationToken).ConfigureAwait(false);
            return outgoing is null
                ? Fail<OutgoingMessage>(MailErrorCode.Conflict, "The existing schedule could not be found.", $"draft:{draftId}", "9to1.Mail.ScheduleSend", recoverable: true)
                : MailResult<OutgoingMessage>.Success(outgoing);
        }
        catch (MailStoreException ex)
        {
            return Fail<OutgoingMessage>(ex.Code, ex.Message, $"draft:{draftId}", "9to1.Mail.ScheduleSend", recoverable: true);
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
                    Outgoing = ReplaceOutgoing(state.Outgoing, result),
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

    /// <summary>Advances a due queued message through Home authorization and provider acceptance.</summary>
    public async Task<MailResult<OutgoingMessage>> SendDueAsync(Guid messageId, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        var snapshot = await _store.ReadAsync(cancellationToken).ConfigureAwait(false);
        var current = snapshot.Outgoing.FirstOrDefault(item => item.MessageId == messageId);
        if (current is null) return Fail<OutgoingMessage>(MailErrorCode.MessageNotFound, "The outgoing message was not found.", $"outgoing:{messageId}", "9to1.Mail.Send");
        if (current.State != MailMessageState.Queued || current.HoldUntil is { } hold && hold > now || current.ScheduledAt is { } scheduled && scheduled > now)
            return Fail<OutgoingMessage>(MailErrorCode.SendRejected, "This message is not ready to send.", $"outgoing:{messageId}", "9to1.Mail.Send");
        var account = snapshot.Accounts.FirstOrDefault(item => item.AccountId == current.AccountId);
        if (account is null) return Fail<OutgoingMessage>(MailErrorCode.AccountNotFound, "The sending account was removed.", $"account:{current.AccountId}", "9to1.Mail.Send");
        var draft = snapshot.Drafts.FirstOrDefault(item => item.DraftId == current.DraftId && !item.IsDeleted);
        if (draft is null) return Fail<OutgoingMessage>(MailErrorCode.DraftNotFound, "The send draft is no longer available.", $"draft:{current.DraftId}", "9to1.Mail.Send");
        if (_authorizer is null)
            return Fail<OutgoingMessage>(MailErrorCode.PermissionRequired, "Home authorization is unavailable; the message remains queued.", $"outgoing:{messageId}", "9to1.Mail.Send", recoverable: true);
        if (!_providers.TryGetValue(account.Provider, out var provider))
            return Fail<OutgoingMessage>(MailErrorCode.ProviderUnavailable, "The sending provider is unavailable; the message remains queued.", $"account:{account.AccountId}", "9to1.Mail.Send", recoverable: true);

        var permission = await _authorizer.AuthorizeSendAsync(account, draft, cancellationToken).ConfigureAwait(false);
        if (!permission.IsSuccess) return MailResult<OutgoingMessage>.Failure(permission.Error!);
        if (permission.Value != true)
            return Fail<OutgoingMessage>(MailErrorCode.PermissionDenied, "Home did not authorize this send; the message remains queued.", $"outgoing:{messageId}", "9to1.Mail.Send");

        OutgoingMessage? sending = null;
        try
        {
            await _store.TransactAsync(state =>
            {
                var fresh = state.Outgoing.FirstOrDefault(item => item.MessageId == messageId);
                if (fresh is null || fresh.State != MailMessageState.Queued)
                    throw new MailStoreException(MailErrorCode.Conflict, "The outgoing message changed before submission.");
                sending = fresh with { State = MailMessageState.Sending, IsUndoAvailable = false, AttemptCount = fresh.AttemptCount + 1, LastError = null };
                return state with { Outgoing = ReplaceOutgoing(state.Outgoing, sending) };
            }, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (MailStoreException ex) { return Fail<OutgoingMessage>(ex.Code, ex.Message, $"outgoing:{messageId}", "9to1.Mail.Send", recoverable: true); }

        try
        {
            var accepted = await provider.SendAsync(account, draft, cancellationToken).ConfigureAwait(false);
            var sent = sending! with { State = MailMessageState.Sent, ProviderMessageId = accepted.ProviderMessageId, LastError = null };
            await _store.TransactAsync(state => state with { Outgoing = ReplaceOutgoing(state.Outgoing, sent) }, cancellationToken: cancellationToken).ConfigureAwait(false);
            return MailResult<OutgoingMessage>.Success(sent);
        }
        catch (OperationCanceledException)
        {
            await MarkInterruptedSendAsync(messageId, "Submission was interrupted. Check Sent before retrying to avoid a duplicate.").ConfigureAwait(false);
            throw;
        }
        catch (MailProviderException ex)
        {
            var failed = sending! with { State = MailMessageState.Failed, LastError = ex.Message, IsUndoAvailable = false };
            await _store.TransactAsync(state => state with { Outgoing = ReplaceOutgoing(state.Outgoing, failed) }, cancellationToken: CancellationToken.None).ConfigureAwait(false);
            return Fail<OutgoingMessage>(ex.Code is MailErrorCode.SendRejected or MailErrorCode.InvalidRecipient ? ex.Code : MailErrorCode.SendFailed,
                ex.Message, $"outgoing:{messageId}", "9to1.Mail.Send", ex.IsRetryable,
                new Dictionary<string, string> { ["provider"] = account.Provider.ToString(), ["attempt"] = failed.AttemptCount.ToString() });
        }
    }

    public async Task<int> RecoverInterruptedSendsAsync(CancellationToken cancellationToken = default)
    {
        var recovered = 0;
        await _store.TransactAsync(state =>
        {
            var outgoing = state.Outgoing.Select(item =>
            {
                if (item.State != MailMessageState.Sending) return item;
                recovered++;
                return item with { State = MailMessageState.Failed, IsUndoAvailable = false,
                    LastError = "The app stopped during provider submission. Check Sent before retrying to avoid a duplicate." };
            }).ToArray();
            return recovered == 0 ? state : state with { Outgoing = outgoing };
        }, cancellationToken: cancellationToken).ConfigureAwait(false);
        return recovered;
    }

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
                return state with { Messages = ReplaceMessage(state.Messages, result), PendingOperations = [.. state.PendingOperations, operation] };
            }, cancellationToken: cancellationToken).ConfigureAwait(false);
            return MailResult<MailMessage>.Success(result!);
        }
        catch (MailStoreException ex)
        {
            return Fail<MailMessage>(ex.Code, ex.Message, $"message:{messageId}", $"9to1.Mail.{kind}", recoverable: true);
        }
    }

    private static IReadOnlyList<T> Replace<T>(IReadOnlyList<T> items, T replacement, Func<T, Guid> key, Guid targetId)
    {
        var index = -1;
        for (var i = 0; i < items.Count; i++) if (key(items[i]) == targetId) { index = i; break; }
        if (index < 0) return items;
        var result = items.ToArray();
        result[index] = replacement;
        return result;
    }

    private Task MarkInterruptedSendAsync(Guid messageId, string reason) => _store.TransactAsync(state =>
    {
        var item = state.Outgoing.FirstOrDefault(entry => entry.MessageId == messageId);
        if (item is null || item.State != MailMessageState.Sending) return state;
        return state with { Outgoing = ReplaceOutgoing(state.Outgoing, item with { State = MailMessageState.Failed, LastError = reason, IsUndoAvailable = false }) };
    }, cancellationToken: CancellationToken.None);

    private Task SetSyncStatusAsync(MailAccount account, MailSyncState status, string? error, DateTimeOffset? lastContact, CancellationToken cancellationToken) =>
        _store.TransactAsync(state =>
        {
            var current = state.Accounts.FirstOrDefault(item => item.AccountId == account.AccountId);
            if (current is null) return state;
            var updated = current with { SyncState = status, LastSyncError = error, LastSuccessfulContact = lastContact ?? current.LastSuccessfulContact };
            return state with { Accounts = ReplaceAccount(state.Accounts, updated) };
        }, cancellationToken: cancellationToken);

    private static IReadOnlyList<MailAccount> ReplaceAccount(IReadOnlyList<MailAccount> accounts, MailAccount replacement) =>
        Replace(accounts, replacement, item => item.AccountId, replacement.AccountId);
    private static IReadOnlyList<MailDraft> ReplaceDraft(IReadOnlyList<MailDraft> drafts, MailDraft replacement) =>
        Replace(drafts, replacement, item => item.DraftId, replacement.DraftId);
    private static IReadOnlyList<OutgoingMessage> ReplaceOutgoing(IReadOnlyList<OutgoingMessage> messages, OutgoingMessage replacement) =>
        Replace(messages, replacement, item => item.MessageId, replacement.MessageId);
    private static IReadOnlyList<MailMessage> ReplaceMessage(IReadOnlyList<MailMessage> messages, MailMessage replacement) =>
        Replace(messages, replacement, item => item.MessageId, replacement.MessageId);

    private static IReadOnlyList<MailThread> RebuildThreads(IReadOnlyList<MailMessage> messages, Guid accountId) =>
        messages.Where(item => item.AccountId == accountId && !item.IsDeleted)
            .GroupBy(item => item.ThreadId)
            .Select(group =>
            {
                var ordered = group.OrderBy(item => item.ReceivedAt).ThenBy(item => item.MessageId).ToArray();
                var latest = ordered[^1];
                var participants = ordered.SelectMany(item => (item.Sender is null ? [] : new[] { item.Sender }).Concat(item.To).Concat(item.Cc))
                    .DistinctBy(item => item.Address, StringComparer.OrdinalIgnoreCase).ToArray();
                return new MailThread(group.Key, accountId, latest.ProviderThreadId, ordered.All(item => item.IsLocalThreadGrouping),
                    ordered.Select(item => item.MessageId).ToArray(), participants, latest.Subject,
                    ordered.Count(item => !item.IsRead), latest.ReceivedAt);
            }).ToArray();

    private static MailResult<T> Fail<T>(MailErrorCode code, string message, string target, string action, bool recoverable = false, IReadOnlyDictionary<string, string>? details = null) =>
        MailResult<T>.Failure(new MailError(code, message, target, action, recoverable, Details: details));
}
