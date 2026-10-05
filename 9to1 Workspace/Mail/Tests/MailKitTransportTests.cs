using HavenOS.Mail;
using HavenOS.Mail.Providers;
using Xunit;

namespace HavenOS.Mail.Tests;

public sealed class MailKitSubmissionValidationTests
{
    [Fact]
    public async Task Attachment_draft_is_refused_before_credentials_or_connection_instead_of_sending_without_files()
    {
        var credentials = new NeverResolveCredentials();
        var provider = new MailKitImapSmtpProvider(credentials);
        Assert.False(provider.Capabilities.HasFlag(MailCapability.Attachments));
        var account = MailKitTransportFixture.Account("localhost", 1, 1, "fixture@example.test");
        var draft = MailKitTransportFixture.Draft(account) with { AttachmentIds = [Guid.NewGuid()] };
        var originalAttachments = draft.AttachmentIds.ToArray();

        var error = await Assert.ThrowsAsync<MailProviderException>(() => provider.SendAsync(account, draft));

        Assert.Equal(MailErrorCode.AttachmentUnavailable, error.Code);
        Assert.False(error.IsRetryable);
        Assert.Equal(0, credentials.Calls);
        Assert.Equal(originalAttachments, draft.AttachmentIds);
        Assert.Equal("fixture body", draft.PlainBody);
    }

    private sealed class NeverResolveCredentials : IMailCredentialResolver
    {
        public int Calls { get; private set; }
        public ValueTask<MailProviderCredential> ResolveAsync(string reference, CancellationToken token = default)
        {
            Calls++;
            throw new InvalidOperationException("Attachment refusal must precede credential resolution.");
        }
    }
}

/// <summary>Dedicated transport lane: requires a real isolated local SMTP/IMAP server and trusted TLS.</summary>
public sealed class MailKitRealTransportTests
{
    [Fact]
    public async Task Actual_TLS_SMTP_submission_is_retrieved_by_actual_IMAP_with_identity_body_and_incremental_cursor()
    {
        var host = Required("HAVEN_MAIL_FIXTURE_HOST");
        Assert.True(host is "localhost" or "127.0.0.1" or "::1", "The transport fixture must be isolated on loopback.");
        var address = Required("HAVEN_MAIL_FIXTURE_ADDRESS");
        var account = MailKitTransportFixture.Account(host,
            int.Parse(Required("HAVEN_MAIL_FIXTURE_IMAP_TLS_PORT"), System.Globalization.CultureInfo.InvariantCulture),
            int.Parse(Required("HAVEN_MAIL_FIXTURE_SMTP_TLS_PORT"), System.Globalization.CultureInfo.InvariantCulture), address);
        var secret = Required("HAVEN_MAIL_FIXTURE_PASSWORD");
        using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        var token = lifetime.Token;
        var provider = new MailKitImapSmtpProvider(new FixtureCredentials(secret));
        var before = await provider.SynchronizeAsync(account, null, token);
        Assert.False(string.IsNullOrWhiteSpace(before.NewChangeCursor));
        var draft = MailKitTransportFixture.Draft(account) with { Subject = "Haven real transport " + Guid.NewGuid().ToString("N") };

        var accepted = await provider.SendAsync(account, draft, token);
        Assert.False(string.IsNullOrWhiteSpace(accepted.ProviderMessageId));
        Assert.True(accepted.AcceptedAt > DateTimeOffset.MinValue);
        MailMessage? received = null;
        var cursor = before.NewChangeCursor;
        while (received is null)
        {
            var after = await provider.SynchronizeAsync(account, cursor, token);
            // An empty original Inbox has UID 0; its first delivery is correctly a full initial sync.
            var priorUid = uint.Parse(cursor!.Split(':')[1], System.Globalization.CultureInfo.InvariantCulture);
            Assert.Equal(priorUid > 0, after.WasIncremental);
            received = after.Messages.SingleOrDefault(item => item.Subject == draft.Subject);
            cursor = after.NewChangeCursor;
            if (received is null) await Task.Delay(TimeSpan.FromMilliseconds(100), token);
        }
        Assert.Equal(account.AccountId, received.AccountId);
        Assert.Equal(address, received.Sender!.Address);
        Assert.Equal(address, Assert.Single(received.To).Address);
        Assert.Contains(draft.PlainBody, received.PlainBody!);
        Assert.Contains("fixture signature", received.PlainBody!);
        Assert.Empty(received.AttachmentIds);
        Assert.NotEqual(before.NewChangeCursor, cursor);
        var repeated = await provider.SynchronizeAsync(account, cursor, token);
        Assert.True(repeated.WasIncremental);
        Assert.DoesNotContain(repeated.Messages, item => item.Subject == draft.Subject);
        Assert.Equal(cursor, repeated.NewChangeCursor);
    }

    private static string Required(string name) => Environment.GetEnvironmentVariable(name) is { Length: > 0 } value
        ? value : throw new InvalidOperationException("The required real transport fixture is not configured: " + name);

    private sealed class FixtureCredentials(string secret) : IMailCredentialResolver
    {
        public ValueTask<MailProviderCredential> ResolveAsync(string reference, CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            if (reference != "fixture-local-mail") throw new InvalidOperationException("Unexpected credential reference.");
            return ValueTask.FromResult(new MailProviderCredential(secret, false));
        }
    }
}

internal static class MailKitTransportFixture
{
    public static MailAccount Account(string host, int imapPort, int smtpPort, string address)
    {
        var identity = new MailFromIdentity(Guid.NewGuid(), address, "Haven fixture", address, null, "fixture signature");
        return new MailAccount(Guid.NewGuid(), MailProviderKind.ImapSmtp, "isolated-mail-transport", address,
            "Local transport fixture", MailCapability.Folders | MailCapability.ChangeTokens, "fixture-local-mail",
            new(new(host, imapPort, MailTransportSecurity.Tls), new(host, smtpPort, MailTransportSecurity.Tls), address, "fixture-local-mail"),
            new(true, true, 1, false, [], TimeSpan.FromSeconds(1)), new(TimeSpan.Zero, true), [identity],
            MailSyncState.Never, null, null, null, true);
    }

    public static MailDraft Draft(MailAccount account) => new(Guid.NewGuid(), account.AccountId,
        account.FromIdentities[0].Id, [new(account.PrimaryAddress)], [], [], "fixture subject", null,
        "fixture body", [], null, 1, DateTimeOffset.UtcNow, null, false);
}
