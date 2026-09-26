using System.Globalization;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Text;
using MailKit;
using MailKit.Net.Imap;
using MailKit.Net.Smtp;
using MailKit.Search;
using MailKit.Security;
using MimeKit;

namespace HavenOS.Mail.Providers;

/// <summary>Generic IMAP/SMTP adapter. OAuth access tokens are requested by reference and never logged or persisted.</summary>
public sealed class MailKitImapSmtpProvider(IMailCredentialResolver credentials) : IMailProviderAdapter
{
    private readonly IMailCredentialResolver _credentials = credentials ?? throw new ArgumentNullException(nameof(credentials));
    public MailProviderKind Provider => MailProviderKind.ImapSmtp;
    public MailCapability Capabilities => MailCapability.Folders | MailCapability.Archive | MailCapability.Push |
        MailCapability.ChangeTokens | MailCapability.Attachments | MailCapability.NativeSearch | MailCapability.Junk;

    public async Task<MailProviderSyncBatch> SynchronizeAsync(MailAccount account, string? changeCursor, CancellationToken cancellationToken = default)
    {
        ValidateAccount(account);
        var connection = account.Connection!;
        using var client = new ImapClient();
        try
        {
            await ConnectAndAuthenticateAsync(client, account, cancellationToken).ConfigureAwait(false);
            var folders = await GetFoldersAsync(client, account, cancellationToken).ConfigureAwait(false);
            var inbox = client.Inbox;
            await inbox.OpenAsync(FolderAccess.ReadOnly, cancellationToken).ConfigureAwait(false);
            var priorUid = ParseCursor(changeCursor, inbox.UidValidity);
            var uids = priorUid == 0
                ? await inbox.SearchAsync(SearchQuery.All, cancellationToken).ConfigureAwait(false)
                : await inbox.SearchAsync(SearchQuery.Uids(new UniqueIdRange(new UniqueId(priorUid + 1), UniqueId.MaxValue)), cancellationToken).ConfigureAwait(false);
            var summaries = uids.Count == 0
                ? []
                : await inbox.FetchAsync(uids, MessageSummaryItems.UniqueId | MessageSummaryItems.Envelope | MessageSummaryItems.Flags |
                    MessageSummaryItems.InternalDate | MessageSummaryItems.BodyStructure | MessageSummaryItems.ModSeq | MessageSummaryItems.Size, cancellationToken).ConfigureAwait(false);
            var messages = new List<MailMessage>(summaries.Count);
            foreach (var summary in summaries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var mime = await inbox.GetMessageAsync(summary.UniqueId, cancellationToken).ConfigureAwait(false);
                messages.Add(ConvertMessage(account, inbox, summary, mime));
            }
            var maxUid = uids.Count == 0 ? priorUid : Math.Max(priorUid, uids.Max(uid => (uint)uid.Id));
            return new MailProviderSyncBatch(account.AccountId, folders, messages,
                $"{inbox.UidValidity.ToString(CultureInfo.InvariantCulture)}:{maxUid.ToString(CultureInfo.InvariantCulture)}",
                priorUid > 0, DateTimeOffset.UtcNow);
        }
        catch (OperationCanceledException) { throw; }
        catch (MailProviderException) { throw; }
        catch (AuthenticationException ex) { throw new MailProviderException(MailErrorCode.AuthenticationRequired, "The provider rejected Mail authentication. Reconnect the account and retry sync.", false, ex); }
        catch (MailKit.Security.SslHandshakeException ex) { throw new MailProviderException(MailErrorCode.CertificateError, "The provider's TLS certificate could not be verified.", false, ex); }
        catch (MailKit.Net.Imap.ImapCommandException ex) { throw new MailProviderException(MailErrorCode.SyncFailed, "The IMAP server rejected a sync command.", true, ex); }
        catch (Exception ex) when (ex is IOException or MailKit.ServiceNotConnectedException or MailKit.ServiceUnavailableException)
        { throw new MailProviderException(MailErrorCode.ProviderUnavailable, "The mail provider could not be reached. Cached messages remain available.", true, ex); }
        finally
        {
            if (client.IsConnected)
            {
                try { await client.DisconnectAsync(true, CancellationToken.None).ConfigureAwait(false); }
                catch (Exception) { }
            }
        }
    }

    public async Task<MailProviderSendResult> SendAsync(MailAccount account, MailDraft draft, CancellationToken cancellationToken = default)
    {
        ValidateAccount(account);
        ArgumentNullException.ThrowIfNull(draft);
        if (account.AccountId != draft.AccountId) throw new MailProviderException(MailErrorCode.InvalidInput, "The draft belongs to a different account.", false);
        var identity = account.FromIdentities.FirstOrDefault(item => item.Id == draft.FromIdentityId)
            ?? throw new MailProviderException(MailErrorCode.InvalidInput, "The selected From identity is not available on this account.", false);
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(identity.DisplayName, identity.Address));
        if (!string.IsNullOrWhiteSpace(identity.ReplyTo)) message.ReplyTo.Add(MailboxAddress.Parse(identity.ReplyTo));
        AddRecipients(message.To, draft.To);
        AddRecipients(message.Cc, draft.Cc);
        AddRecipients(message.Bcc, draft.Bcc);
        if (message.To.Count + message.Cc.Count + message.Bcc.Count == 0)
            throw new MailProviderException(MailErrorCode.InvalidRecipient, "Add at least one recipient before sending.", false);
        message.Subject = draft.Subject;
        var builder = new BodyBuilder { TextBody = draft.PlainBody };
        if (!string.IsNullOrWhiteSpace(draft.RichBody)) builder.HtmlBody = draft.RichBody;
        if (!string.IsNullOrWhiteSpace(identity.PlainSignature)) builder.TextBody = JoinBody(builder.TextBody, identity.PlainSignature);
        if (!string.IsNullOrWhiteSpace(identity.RichSignature)) builder.HtmlBody = JoinBody(builder.HtmlBody, identity.RichSignature);
        message.Body = builder.ToMessageBody();
        var config = account.Connection!;
        using var client = new SmtpClient();
        try
        {
            await client.ConnectAsync(config.Smtp.Host, config.Smtp.Port, ToSocketOptions(config.Smtp.Security), cancellationToken).ConfigureAwait(false);
            var credential = await _credentials.ResolveAsync(account.CredentialReference, cancellationToken).ConfigureAwait(false);
            if (credential.IsOAuthAccessToken)
                await client.AuthenticateAsync(new SaslMechanismOAuth2(config.Username, credential.Secret), cancellationToken).ConfigureAwait(false);
            else
                await client.AuthenticateAsync(config.Username, credential.Secret, cancellationToken).ConfigureAwait(false);
            var response = await client.SendAsync(message, cancellationToken).ConfigureAwait(false);
            return new MailProviderSendResult(response, DateTimeOffset.UtcNow);
        }
        catch (OperationCanceledException) { throw; }
        catch (AuthenticationException ex) { throw new MailProviderException(MailErrorCode.AuthenticationRequired, "The provider rejected the sending credentials. Reconnect the account and retry.", false, ex); }
        catch (MailKit.Net.Smtp.SmtpCommandException ex) { throw new MailProviderException(MailErrorCode.SendRejected, "The provider rejected the message. Review the server response and retry from Outbox.", false, ex); }
        catch (Exception ex) when (ex is IOException or MailKit.ServiceNotConnectedException or MailKit.ServiceUnavailableException)
        { throw new MailProviderException(MailErrorCode.SendFailed, "The provider could not accept the message. It remains in Outbox for retry.", true, ex); }
        finally
        {
            if (client.IsConnected)
            {
                try { await client.DisconnectAsync(true, CancellationToken.None).ConfigureAwait(false); }
                catch (Exception) { }
            }
        }
    }

    public async Task WaitForChangesAsync(MailAccount account, CancellationToken cancellationToken = default)
    {
        ValidateAccount(account);
        using var client = new ImapClient();
        try
        {
            await ConnectAndAuthenticateAsync(client, account, cancellationToken).ConfigureAwait(false);
            await client.Inbox.OpenAsync(FolderAccess.ReadOnly, cancellationToken).ConfigureAwait(false);
            if (!client.Capabilities.HasFlag(ImapCapabilities.Idle))
                throw new MailProviderException(MailErrorCode.ProviderCapabilityUnsupported, "This IMAP server does not support IDLE; use the configured incremental polling fallback.", false);
            using var done = new CancellationTokenSource(TimeSpan.FromMinutes(25));
            await client.IdleAsync(done.Token, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { throw; }
        catch (MailProviderException) { throw; }
        catch (AuthenticationException ex) { throw new MailProviderException(MailErrorCode.AuthenticationRequired, "The provider rejected Mail authentication.", false, ex); }
        catch (Exception ex) when (ex is IOException or MailKit.ServiceNotConnectedException or MailKit.ServiceUnavailableException)
        { throw new MailProviderException(MailErrorCode.ProviderUnavailable, "The provider change stream disconnected; reconnect and resume incremental sync.", true, ex); }
        finally
        {
            if (client.IsConnected)
            {
                try { await client.DisconnectAsync(true, CancellationToken.None).ConfigureAwait(false); }
                catch (Exception) { }
            }
        }
    }

    private async Task ConnectAndAuthenticateAsync(ImapClient client, MailAccount account, CancellationToken cancellationToken)
    {
        var config = account.Connection!;
        await client.ConnectAsync(config.Imap.Host, config.Imap.Port, ToSocketOptions(config.Imap.Security), cancellationToken).ConfigureAwait(false);
        var credential = await _credentials.ResolveAsync(account.CredentialReference, cancellationToken).ConfigureAwait(false);
        if (credential.IsOAuthAccessToken)
            await client.AuthenticateAsync(new SaslMechanismOAuth2(config.Username, credential.Secret), cancellationToken).ConfigureAwait(false);
        else
            await client.AuthenticateAsync(config.Username, credential.Secret, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<IReadOnlyList<MailFolder>> GetFoldersAsync(ImapClient client, MailAccount account, CancellationToken cancellationToken)
    {
        var folders = new List<MailFolder>();
        foreach (var ns in client.PersonalNamespaces)
        {
            var results = await client.GetFoldersAsync(ns, statusItems: StatusItems.Unread | StatusItems.Count, cancellationToken).ConfigureAwait(false);
            foreach (var folder in results)
            {
                var kind = folder.Attributes.HasFlag(FolderAttributes.Inbox) ? MailFolderKind.Inbox :
                    folder.Attributes.HasFlag(FolderAttributes.Drafts) ? MailFolderKind.Drafts :
                    folder.Attributes.HasFlag(FolderAttributes.Sent) ? MailFolderKind.Sent :
                    folder.Attributes.HasFlag(FolderAttributes.Archive) ? MailFolderKind.Archive :
                    folder.Attributes.HasFlag(FolderAttributes.Junk) ? MailFolderKind.Junk :
                    folder.Attributes.HasFlag(FolderAttributes.Trash) ? MailFolderKind.Trash : MailFolderKind.Custom;
                folders.Add(new MailFolder(StableId(account.AccountId, "folder:" + folder.FullName), account.AccountId,
                    folder.FullName, folder.Name, kind, false, folder.CanOpen, folder.Unread));
            }
        }
        return folders;
    }

    private static MailMessage ConvertMessage(MailAccount account, IMailFolder folder, IMessageSummary summary, MimeMessage message)
    {
        var providerId = summary.UniqueId.Id.ToString(CultureInfo.InvariantCulture);
        var messageId = StableId(account.AccountId, $"imap:{folder.FullName}:{summary.UniqueIdValidity}:{providerId}");
        var threadIdentity = message.References.FirstOrDefault() ?? message.InReplyTo ?? message.MessageId ?? providerId;
        var threadId = StableId(account.AccountId, "thread:" + threadIdentity);
        var attachments = new List<Guid>();
        foreach (var part in message.Attachments)
            attachments.Add(StableId(messageId, part.ContentId ?? part.ContentType.MimeType + ":" + part.ContentDisposition?.FileName));
        var textPart = message.BodyParts.OfType<TextPart>().FirstOrDefault(part => part.IsPlain);
        var htmlPart = message.BodyParts.OfType<TextPart>().FirstOrDefault(part => part.IsHtml);
        var sender = message.From.Mailboxes.FirstOrDefault();
        var received = summary.InternalDate ?? message.Date;
        return new MailMessage(messageId, account.AccountId, providerId, message.MessageId, threadId, null,
            folder.FullName, new HashSet<string>(), received, message.Date, ToAddress(sender), ToAddresses(message.To), ToAddresses(message.Cc),
            message.Subject ?? string.Empty, MakePreview(textPart?.Text ?? htmlPart?.Text ?? string.Empty), textPart?.Text, htmlPart?.Text,
            summary.Flags?.HasFlag(MessageFlags.Seen) == true, summary.Flags?.HasFlag(MessageFlags.Flagged) == true,
            summary.Flags?.HasFlag(MessageFlags.Answered) == true, summary.ModSeq?.ToString(CultureInfo.InvariantCulture), attachments,
            ReadAuthenticationHeaders(message), RemoteContentPolicy.Blocked, IsLocalThreadGrouping: true);
    }

    private static MessageAuthenticationResults ReadAuthenticationHeaders(MimeMessage message)
    {
        var results = message.Headers["Authentication-Results"];
        return new MessageAuthenticationResults(
            FindAuthValue(results, "spf"), FindAuthValue(results, "dkim"), FindAuthValue(results, "dmarc"),
            string.IsNullOrWhiteSpace(results) ? null : "Provider authentication results are present.");
    }

    private static string? FindAuthValue(string? header, string mechanism)
    {
        if (string.IsNullOrWhiteSpace(header)) return null;
        var start = header.IndexOf(mechanism + "=", StringComparison.OrdinalIgnoreCase);
        if (start < 0) return null;
        start += mechanism.Length + 1;
        var end = header.IndexOfAny([';', ' ', '\t'], start);
        return header[start..(end < 0 ? header.Length : end)].Trim();
    }

    private static void AddRecipients(InternetAddressList target, IReadOnlyList<MailAddress> recipients)
    {
        foreach (var item in recipients)
        {
            try { target.Add(new MailboxAddress(item.DisplayName ?? string.Empty, item.Address)); }
            catch (FormatException ex) { throw new MailProviderException(MailErrorCode.InvalidRecipient, "One or more recipient addresses are invalid.", false, ex); }
        }
    }

    private static IReadOnlyList<MailAddress> ToAddresses(InternetAddressList items) => items.Mailboxes.Select(ToAddress).ToArray();
    private static MailAddress? ToAddress(MailboxAddress? item) => item is null ? null : new MailAddress(item.Address, item.Name);

    private static string MakePreview(string value)
    {
        var text = string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return text.Length <= 240 ? text : text[..240];
    }

    private static string JoinBody(string? body, string signature) => string.IsNullOrWhiteSpace(body) ? signature : body + "\n\n" + signature;

    private static SecureSocketOptions ToSocketOptions(MailTransportSecurity security) => security switch
    {
        MailTransportSecurity.StartTls => SecureSocketOptions.StartTls,
        MailTransportSecurity.Tls => SecureSocketOptions.SslOnConnect,
        MailTransportSecurity.OpportunisticStartTls => SecureSocketOptions.StartTlsWhenAvailable,
        _ => throw new ArgumentOutOfRangeException(nameof(security)),
    };

    private static uint ParseCursor(string? cursor, uint uidValidity)
    {
        if (string.IsNullOrWhiteSpace(cursor)) return 0;
        var parts = cursor.Split(':', 2);
        if (parts.Length != 2 || !uint.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var priorValidity) ||
            !uint.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var lastUid) || priorValidity != uidValidity) return 0;
        return lastUid;
    }

    private static Guid StableId(Guid scopeId, string providerScopedId) => StableId(scopeId.ToString("N"), providerScopedId);

    private static Guid StableId(string scope, string providerScopedId)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(scope + "\0" + providerScopedId));
        return new Guid(bytes.AsSpan(0, 16));
    }

    private static void ValidateAccount(MailAccount account)
    {
        ArgumentNullException.ThrowIfNull(account);
        if (account.Provider != MailProviderKind.ImapSmtp || account.Connection is null)
            throw new MailProviderException(MailErrorCode.ProviderCapabilityUnsupported, "The generic IMAP/SMTP adapter requires an IMAP/SMTP account configuration.", false);
        account.Connection.Validate();
    }
}
