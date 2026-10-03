namespace HavenOS.Mail.Providers;

/// <summary>Byte delivery only. Implementations must retain actual original read authority;
/// this contract grants neither Files access nor Home permission to submit a message.</summary>
public interface IMailAttachmentContentSource
{
    ValueTask<MailAttachmentContent> ReadAsync(Guid accountId, Guid draftId, Guid attachmentId,
        CancellationToken cancellationToken = default);
}

public sealed record MailAttachmentContent(Guid AttachmentId, string FileName, string MimeType, ReadOnlyMemory<byte> Bytes);
