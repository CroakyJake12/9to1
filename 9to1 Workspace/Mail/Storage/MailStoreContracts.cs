namespace HavenOS.Mail.Storage;

public sealed record MailState(
    int SchemaVersion,
    long Revision,
    IReadOnlyList<MailAccount> Accounts,
    IReadOnlyList<MailFolder> Folders,
    IReadOnlyList<MailMessage> Messages,
    IReadOnlyList<MailThread> Threads,
    IReadOnlyList<MailAttachment> Attachments,
    IReadOnlyList<MailDraft> Drafts,
    IReadOnlyList<OutgoingMessage> Outgoing,
    IReadOnlyList<MailPendingOperation> PendingOperations,
    IReadOnlyList<MailSmartView> SmartViews,
    IReadOnlyList<MailRule> Rules,
    IReadOnlyList<MailRuleExecution> RuleExecutions,
    MailSettings Settings)
{
    public const int CurrentSchemaVersion = 1;
    public static MailState Empty { get; } = new(
        CurrentSchemaVersion, 0, [], [], [], [], [], [], [], [], [], [], [],
        new MailSettings(MailPanePosition.Right, MailDensity.Comfortable, true, 30, TimeSpan.FromSeconds(5), true));
}

public interface IMailStateStore
{
    Task<MailState> ReadAsync(CancellationToken cancellationToken = default);
    Task<MailState> TransactAsync(Func<MailState, MailState> update, long? expectedRevision = null, CancellationToken cancellationToken = default);
}

public sealed record MailEncryptionKey(string KeyId, ReadOnlyMemory<byte> KeyBytes);

/// <summary>Supplies a per-user key handle from Home's OS-backed secure credential/key service.</summary>
public interface IMailEncryptionKeyProvider
{
    ValueTask<MailEncryptionKey> GetCurrentKeyAsync(CancellationToken cancellationToken = default);
}

public sealed class MailStoreException(MailErrorCode code, string message, Exception? innerException = null)
    : Exception(message, innerException)
{
    public MailErrorCode Code { get; } = code;
}
