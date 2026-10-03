namespace HavenOS.Mail.Providers;

public sealed record MailProviderCredential(string Secret, bool IsOAuthAccessToken);

/// <summary>Credential references resolve through Home's protected credential broker; account data never carries secrets.</summary>
public interface IMailCredentialResolver
{
    ValueTask<MailProviderCredential> ResolveAsync(string credentialReference, CancellationToken cancellationToken = default);
}

public sealed record MailProviderSyncBatch(
    Guid AccountId,
    IReadOnlyList<MailFolder> Folders,
    IReadOnlyList<MailMessage> Messages,
    string? NewChangeCursor,
    bool WasIncremental,
    DateTimeOffset ContactedAt,
    IReadOnlyList<MailProviderMessageState>? MessageStates = null,
    IReadOnlyList<MailProviderFolderInventory>? FolderInventories = null);

/// <summary>Observed server flags for an existing canonical message; cached body/attachments are not replaced.</summary>
public sealed record MailProviderMessageState(Guid AccountId, Guid MessageId, string ProviderMessageId,
    string FolderKey, bool IsRead, bool IsStarred, bool IsImportant, bool IsDeleted, string ProviderRevision);

/// <summary>
/// Complete UID membership observed for one selected account/folder. It is not an atomic server snapshot.
/// Absence can retire an old canonical message; folders outside this inventory remain untouched.
/// </summary>
public sealed record MailProviderFolderInventory(Guid AccountId, string FolderKey, IReadOnlyList<Guid> MessageIds);

public sealed record MailProviderSendResult(string ProviderMessageId, DateTimeOffset AcceptedAt);

public interface IMailProviderAdapter
{
    MailProviderKind Provider { get; }
    MailCapability Capabilities { get; }
    Task<MailProviderSyncBatch> SynchronizeAsync(MailAccount account, string? changeCursor, CancellationToken cancellationToken = default);
    Task<MailProviderSendResult> SendAsync(MailAccount account, MailDraft draft, CancellationToken cancellationToken = default);
    Task WaitForChangesAsync(MailAccount account, CancellationToken cancellationToken = default);
}

/// <summary>Home-mediated permission gate. A missing or failed authorizer must never submit a message.</summary>
public interface IMailExternalActionAuthorizer
{
    Task<MailResult<bool>> AuthorizeSendAsync(MailAccount account, MailDraft draft, CancellationToken cancellationToken = default);
}

public sealed class MailProviderException(MailErrorCode code, string message, bool isRetryable, Exception? innerException = null)
    : Exception(message, innerException)
{
    public MailErrorCode Code { get; } = code;
    public bool IsRetryable { get; } = isRetryable;
}
