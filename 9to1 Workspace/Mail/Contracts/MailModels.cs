namespace HavenOS.Mail;

public enum MailProviderKind { ImapSmtp, Google, Microsoft }
public enum MailFolderKind { Inbox, Drafts, Sent, Archive, Junk, Trash, Outbox, Custom }
public enum MailMessageState { Draft, Queued, Sending, Sent, Failed, Cancelled }
public enum MailSyncState { Never, Syncing, Succeeded, Paused, Failed, Offline }
public enum MailPanePosition { Right, Bottom, Off }
public enum MailDensity { Comfortable, Compact }
public enum MailExecutionPolicy { ProviderPreferred, DeviceOnly }
public enum ScheduledExecutionLocation { Provider, Cloud, Device }
public enum MailOperationKind { MarkRead, Star, Move, Archive, Delete, Restore, UpdateDraft, Send, CancelQueued, Snooze }
public enum MailOperationState { Pending, Applying, Applied, Conflict, Failed }
public enum MailRiskLevel { Ordinary, Elevated, High }

[Flags]
public enum MailCapability
{
    None = 0, Folders = 1, Labels = 2, Archive = 4, Snooze = 8, ScheduledSend = 16,
    NativeSearch = 32, Push = 64, UndoSend = 128, Aliases = 256, ReadReceipts = 512,
    OpenPgp = 1024, Smime = 2048, Junk = 4096, Attachments = 8192, ChangeTokens = 16384,
}

public sealed record MailServerEndpoint(string Host, int Port, MailTransportSecurity Security)
{
    public MailServerEndpoint Validate(string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(Host, parameterName);
        if (Port is < 1 or > 65535) throw new ArgumentOutOfRangeException(parameterName, "Port must be between 1 and 65535.");
        return this;
    }
}

public enum MailTransportSecurity { StartTls, Tls, OpportunisticStartTls }

public sealed record MailConnectionConfiguration(
    MailServerEndpoint Imap,
    MailServerEndpoint Smtp,
    string Username,
    string CredentialReference,
    string? OAuthAuthority = null)
{
    public MailConnectionConfiguration Validate()
    {
        ArgumentNullException.ThrowIfNull(Imap);
        ArgumentNullException.ThrowIfNull(Smtp);
        Imap.Validate(nameof(Imap));
        Smtp.Validate(nameof(Smtp));
        ArgumentException.ThrowIfNullOrWhiteSpace(Username);
        ArgumentException.ThrowIfNullOrWhiteSpace(CredentialReference);
        return this;
    }
}

public sealed record MailFromIdentity(
    Guid Id,
    string Address,
    string DisplayName,
    string? ReplyTo,
    string? RichSignature,
    string? PlainSignature);

public sealed record MailSyncConfiguration(
    bool Enabled,
    bool CacheHeaders,
    int RecentBodyDays,
    bool CacheAttachmentsOnDemand,
    IReadOnlyList<Guid> OfflineFolderIds,
    TimeSpan FallbackPollInterval);

public sealed record MailSendingConfiguration(TimeSpan UndoSendDelay, bool RequireConfirmationForExternalRecipients);

public sealed record MailAccount(
    Guid AccountId,
    MailProviderKind Provider,
    string ProviderAccountIdentity,
    string PrimaryAddress,
    string DisplayName,
    MailCapability Capabilities,
    string CredentialReference,
    MailConnectionConfiguration? Connection,
    MailSyncConfiguration Sync,
    MailSendingConfiguration Sending,
    IReadOnlyList<MailFromIdentity> FromIdentities,
    MailSyncState SyncState,
    DateTimeOffset? LastSuccessfulContact,
    string? LastSyncError,
    string? ChangeCursor,
    bool IsAuthenticated);

public sealed record MailAddress(string Address, string? DisplayName = null);

public sealed record MailAttachment(
    Guid AttachmentId,
    Guid MessageId,
    string FileName,
    string MimeType,
    long SizeBytes,
    string? ContentId,
    bool IsInline,
    bool IsAvailableOffline,
    MailAttachmentSecurity Security,
    string? ProviderPartId);

public sealed record MailAttachmentSecurity(bool IsExecutable, bool HasMalwareWarning, IReadOnlyList<string> Warnings);

public sealed record MailMessage(
    Guid MessageId,
    Guid AccountId,
    string ProviderMessageId,
    string? InternetMessageId,
    Guid ThreadId,
    string? ProviderThreadId,
    string FolderKey,
    IReadOnlyList<string> Labels,
    DateTimeOffset ReceivedAt,
    DateTimeOffset? SentAt,
    MailAddress? Sender,
    IReadOnlyList<MailAddress> To,
    IReadOnlyList<MailAddress> Cc,
    string Subject,
    string Preview,
    string? PlainBody,
    string? HtmlBody,
    bool IsRead,
    bool IsStarred,
    bool IsImportant,
    string? ProviderRevision,
    IReadOnlyList<Guid> AttachmentIds,
    MessageAuthenticationResults Authentication,
    RemoteContentPolicy RemoteContent,
    bool IsLocalThreadGrouping = false,
    bool IsDeleted = false,
    DateTimeOffset? SnoozedUntil = null);

public sealed record MessageAuthenticationResults(string? Spf, string? Dkim, string? Dmarc, string? ProviderSummary);
public enum RemoteContentPolicy { Blocked, AllowedForMessage, AllowedForSender }

public sealed record MailThread(
    Guid ThreadId,
    Guid AccountId,
    string? ProviderThreadId,
    bool IsLocalGrouping,
    IReadOnlyList<Guid> OrderedMessageIds,
    IReadOnlyList<MailAddress> Participants,
    string Subject,
    int UnreadCount,
    DateTimeOffset LatestMessageAt);

public sealed record MailFolder(Guid FolderId, Guid AccountId, string ProviderKey, string Name, MailFolderKind Kind, bool IsLabel, bool CanSelect, int? UnreadCount);

public sealed record MailDraft(
    Guid DraftId,
    Guid AccountId,
    Guid FromIdentityId,
    IReadOnlyList<MailAddress> To,
    IReadOnlyList<MailAddress> Cc,
    IReadOnlyList<MailAddress> Bcc,
    string Subject,
    string? RichBody,
    string PlainBody,
    IReadOnlyList<Guid> AttachmentIds,
    string? InReplyToMessageId,
    long Revision,
    DateTimeOffset UpdatedAt,
    string? ProviderDraftId,
    bool IsConflict,
    MailDraft? ConflictingRevision = null,
    bool IsDeleted = false);

public sealed record MailScheduledSend(DateTimeOffset SendAt, ScheduledExecutionLocation Location, string Guarantee);

public sealed record OutgoingMessage(
    Guid MessageId,
    Guid DraftId,
    Guid AccountId,
    MailMessageState State,
    DateTimeOffset CreatedAt,
    DateTimeOffset? HoldUntil,
    DateTimeOffset? ScheduledAt,
    MailScheduledSend? Schedule,
    int AttemptCount,
    string? LastError,
    string? ProviderMessageId,
    bool IsUndoAvailable);

public sealed record MailPendingOperation(
    Guid OperationId,
    Guid AccountId,
    MailOperationKind Kind,
    Guid TargetId,
    string? ExpectedProviderRevision,
    string IdempotencyKey,
    DateTimeOffset CreatedAt,
    MailOperationState State,
    string? ErrorCode,
    string? ErrorMessage,
    IReadOnlyDictionary<string, string> Arguments);

public sealed record MailSyncDiagnostics(MailSyncState State, DateTimeOffset? LastSuccessfulContact, int PendingOperations, string? LastError);

public sealed record MailSearchQuery(
    string? Text = null,
    IReadOnlyList<string>? Sender = null,
    IReadOnlyList<string>? Recipient = null,
    string? Subject = null,
    string? Body = null,
    string? AttachmentName = null,
    DateTimeOffset? From = null,
    DateTimeOffset? To = null,
    IReadOnlyList<Guid>? AccountIds = null,
    IReadOnlyList<string>? FolderKeys = null,
    IReadOnlyList<string>? Labels = null,
    bool? IsRead = null,
    bool? HasAttachments = null,
    bool? IsStarred = null,
    IReadOnlyList<string>? Categories = null,
    int Page = 0,
    int PageSize = 50)
{
    public MailSearchQuery Validate()
    {
        if (Page < 0) throw new ArgumentOutOfRangeException(nameof(Page));
        if (PageSize is < 1 or > 500) throw new ArgumentOutOfRangeException(nameof(PageSize), "Page size must be between 1 and 500.");
        return this;
    }
}

public sealed record MailSearchResult(IReadOnlyList<MailMessage> Messages, int TotalCount, int Page, int PageSize);
public sealed record MailSmartView(Guid Id, string Name, MailSearchQuery Query, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

public sealed record MailRuleCondition(string Field, string Operator, string Value);
public sealed record MailRuleAction(string Kind, string? Value = null);
public sealed record MailRule(Guid Id, string Name, Guid? AccountId, bool Enabled, IReadOnlyList<MailRuleCondition> Conditions, IReadOnlyList<MailRuleAction> Actions, int Priority, long Revision);
public sealed record MailRuleExecution(Guid ExecutionId, Guid RuleId, Guid MessageId, string IdempotencyKey, DateTimeOffset ExecutedAt, IReadOnlyList<string> AppliedActions, IReadOnlyList<string> Errors);

public sealed record MailSettings(MailPanePosition ReadingPane, MailDensity Density, bool BlockRemoteContent, int CacheRecentBodyDays, TimeSpan UndoSendDelay, bool RequireExternalRecipientConfirmation);

public sealed record MailContextSnapshot(Guid? AccountId, string ViewKey, IReadOnlyList<Guid> MessageIds, IReadOnlyList<Guid> ThreadIds, Guid? DraftId, IReadOnlyList<MailAddress> SelectedRecipients, long Revision, IReadOnlyList<string> AllowedActions);
