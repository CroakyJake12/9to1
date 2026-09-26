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
    DateTimeOffset ContactedAt);

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
