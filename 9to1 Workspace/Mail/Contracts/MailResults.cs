namespace HavenOS.Mail;

public enum MailErrorCode
{
    AccountNotFound,
    AuthenticationRequired,
    ProviderUnavailable,
    ProviderCapabilityUnsupported,
    Offline,
    SyncFailed,
    MessageNotFound,
    ThreadNotFound,
    DraftNotFound,
    DraftConflict,
    SendFailed,
    SendRejected,
    AttachmentUnavailable,
    MailboxQuotaExceeded,
    RateLimited,
    PermissionRequired,
    PermissionDenied,
    InvalidRecipient,
    EncryptionKeyUnavailable,
    CertificateError,
    InvalidInput,
    DataCorrupt,
    UnsupportedSchemaVersion,
    Conflict,
}

public sealed record MailError(
    MailErrorCode Code,
    string Message,
    string Target,
    string Action,
    bool IsRetryable,
    string? RetryAfter = null,
    IReadOnlyDictionary<string, string>? Details = null);

public sealed record MailResult<T>(T? Value, MailError? Error)
{
    public bool IsSuccess => Error is null;
    public static MailResult<T> Success(T value) => new(value, null);
    public static MailResult<T> Failure(MailError error) => new(default, error ?? throw new ArgumentNullException(nameof(error)));
}

public enum MailApiRisk { Read, Ordinary, ExternalSideEffect, Destructive, CredentialSensitive }

public sealed record MailApiActionDefinition(
    string Name,
    string ArgumentsSchema,
    string ResultSchema,
    IReadOnlyList<string> RequiredPermissions,
    MailApiRisk Risk,
    bool Reversible,
    bool HasExternalSideEffects,
    string AffectedObjects,
    bool RequiresPerActionApproval = false);

/// <summary>Canonical, typed automation action metadata. Dispatch must still pass through Home.</summary>
public static class MailApiCatalog
{
    public static IReadOnlyList<MailApiActionDefinition> Actions { get; } =
    [
        Read("9to1.Mail.ListAccounts", "{}", "MailAccount[]", "accounts"),
        Read("9to1.Mail.GetAccount", "{accountID:guid}", "MailAccount", "account"),
        Write("9to1.Mail.Account.Add", "MailAccountConfig", "MailAccount", "Mail.Accounts.Write", "account", MailApiRisk.CredentialSensitive, true),
        Write("9to1.Mail.Account.Update", "{accountID:guid,patch,expectedRevision:long}", "MailAccount", "Mail.Accounts.Write", "account", MailApiRisk.CredentialSensitive, true),
        Write("9to1.Mail.Account.Remove", "{accountID:guid,keepLocalData:boolean}", "MailResult", "Mail.Accounts.Delete", "account+cache", MailApiRisk.Destructive, true),
        Write("9to1.Mail.Sync", "{accountID?:guid}", "MailSyncJob", "Mail.Sync", "account messages", MailApiRisk.Ordinary, true),
        Write("9to1.Mail.SyncFolder", "{accountID:guid,folderID:guid}", "MailSyncJob", "Mail.Sync", "folder messages", MailApiRisk.Ordinary, true),
        Read("9to1.Mail.ListFolders", "{accountID:guid}", "MailFolder[]", "folders"),
        Read("9to1.Mail.ListMessages", "MailSearchQuery", "MailSearchResult", "messages"),
        Read("9to1.Mail.GetMessage", "{messageID:guid}", "MailMessage", "message"),
        Read("9to1.Mail.GetThread", "{threadID:guid}", "MailThread+messages", "thread"),
        Write("9to1.Mail.MarkRead", "{messageID:guid,value:boolean}", "MailMessage", "Mail.Messages.Update", "message", MailApiRisk.Ordinary, true),
        Write("9to1.Mail.Star", "{messageID:guid,value:boolean}", "MailMessage", "Mail.Messages.Update", "message", MailApiRisk.Ordinary, true),
        Write("9to1.Mail.Move", "{messageID:guid,destination}", "MailMessage", "Mail.Messages.Move", "message", MailApiRisk.Ordinary, true),
        Write("9to1.Mail.Archive", "{messageID:guid}", "MailMessage", "Mail.Messages.Archive", "message", MailApiRisk.Ordinary, true),
        Write("9to1.Mail.Delete", "{messageID:guid,mode:DeleteMode}", "MailMessage", "Mail.Messages.Delete", "message", MailApiRisk.Destructive, true),
        Write("9to1.Mail.Restore", "{messageID:guid}", "MailMessage", "Mail.Messages.Restore", "message", MailApiRisk.Ordinary, true),
        Write("9to1.Mail.Draft.Create", "MailDraftConfig", "MailDraft", "Mail.Drafts.Write", "draft", MailApiRisk.Ordinary, false),
        Write("9to1.Mail.Draft.Update", "{draftID:guid,patch,expectedRevision?:long}", "MailDraft", "Mail.Drafts.Write", "draft", MailApiRisk.Ordinary, false),
        Write("9to1.Mail.Draft.Delete", "{draftID:guid}", "MailResult", "Mail.Drafts.Delete", "draft", MailApiRisk.Destructive, true),
        Write("9to1.Mail.Send", "{draftID:guid,idempotencyKey:string}", "OutgoingMessage", "Mail.Send", "outgoing message+recipients", MailApiRisk.ExternalSideEffect, true, true),
        Write("9to1.Mail.ScheduleSend", "{draftID:guid,time:datetime,executionPolicy?}", "OutgoingMessage", "Mail.Send", "outgoing message+schedule", MailApiRisk.ExternalSideEffect, true, true),
        Write("9to1.Mail.CancelQueued", "{messageID:guid}", "OutgoingMessage", "Mail.Send.Cancel", "outgoing message", MailApiRisk.Ordinary, true),
        Write("9to1.Mail.Reply", "{messageID:guid,config?}", "MailDraft", "Mail.Drafts.Write", "draft", MailApiRisk.Ordinary, false),
        Write("9to1.Mail.Forward", "{messageID:guid,config?}", "MailDraft", "Mail.Drafts.Write", "draft", MailApiRisk.Ordinary, false),
        Read("9to1.Mail.Attachment.Get", "{attachmentID:guid}", "MailAttachmentStream", "attachment bytes"),
        Write("9to1.Mail.Attachment.SaveToFiles", "{attachmentID:guid,destination}", "FileItem", "Mail.Attachments.Read+Files.Write", "file", MailApiRisk.Ordinary, true),
        Read("9to1.Mail.Search", "MailSearchQuery", "MailSearchResult", "messages"),
        Read("9to1.Mail.SmartViews.List", "{}", "MailSmartView[]", "smart views"),
        Write("9to1.Mail.SmartViews.Create", "{name,query}", "MailSmartView", "Mail.Views.Write", "smart view", MailApiRisk.Ordinary, false),
        Read("9to1.Mail.Rules.List", "{}", "MailRule[]", "rules"),
        Write("9to1.Mail.Rules.Create", "MailRule", "MailRule", "Mail.Rules.Write", "rule", MailApiRisk.Ordinary, false),
        Read("9to1.Mail.Changes.Subscribe", "{accountID?:guid,cursor?:string}", "paged MailChange[]", "changes"),
    ];

    private static MailApiActionDefinition Read(string name, string args, string result, string scope) =>
        new(name, args, result, ["Mail.Read"], MailApiRisk.Read, true, false, scope);

    private static MailApiActionDefinition Write(string name, string args, string result, string permission, string scope, MailApiRisk risk, bool reversible, bool external = false) =>
        new(name, args, result, [permission], risk, reversible, external, scope, risk is MailApiRisk.Destructive or MailApiRisk.CredentialSensitive || external);
}
