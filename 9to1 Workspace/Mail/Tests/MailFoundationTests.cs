using System.Security.Cryptography;
using CakeOS.Cui.Language;
using HavenOS.Mail;
using HavenOS.Mail.Services;
using HavenOS.Mail.Storage;
using Xunit;

namespace HavenOS.Mail.Tests;

public sealed class MailFoundationTests
{
    [Fact]
    public void WorkspaceCui_ParsesWithoutMarkupDiagnostics()
    {
        var source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "MailWorkspace.cui"));
        var parser = new CuiRichParser();

        var document = parser.Parse(source, "MailWorkspace.cui");

        Assert.Empty(parser.Diagnostics.Diagnostics);
        Assert.Contains(document.Components, component => component.Type == "Page");
    }

    [Fact]
    public async Task EncryptedCache_RestoresStateWithoutWritingMailboxDataInPlainText()
    {
        using var temp = new TempDirectory();
        var path = Path.Combine(temp.Path, "mail-state.json");
        var key = new TestKeyProvider("user-key-1", RandomNumberGenerator.GetBytes(32));
        var store = new EncryptedJsonMailStateStore(path, key);
        var account = Account();
        await store.TransactAsync(state => state with { Accounts = [account] });

        var persisted = await File.ReadAllTextAsync(path);
        Assert.DoesNotContain(account.PrimaryAddress, persisted, StringComparison.Ordinal);
        var reopened = new EncryptedJsonMailStateStore(path, key);
        var restored = await reopened.ReadAsync();

        Assert.Equal(account.AccountId, Assert.Single(restored.Accounts).AccountId);
        Assert.Equal(1, restored.Revision);
    }

    [Fact]
    public async Task EncryptedCache_RejectsUnavailableKeyAndPreservesOriginalBytes()
    {
        using var temp = new TempDirectory();
        var path = Path.Combine(temp.Path, "mail-state.json");
        await new EncryptedJsonMailStateStore(path, new TestKeyProvider("first", RandomNumberGenerator.GetBytes(32)))
            .TransactAsync(state => state with { Accounts = [Account()] });
        var original = await File.ReadAllBytesAsync(path);

        var error = await Assert.ThrowsAsync<MailStoreException>(() =>
            new EncryptedJsonMailStateStore(path, new TestKeyProvider("rotated", RandomNumberGenerator.GetBytes(32))).ReadAsync());

        Assert.Equal(MailErrorCode.EncryptionKeyUnavailable, error.Code);
        Assert.Equal(original, await File.ReadAllBytesAsync(path));
    }

    [Fact]
    public async Task Store_UsesRevisionCompareAndSwap()
    {
        using var temp = new TempDirectory();
        var store = Store(temp.Path);
        await store.TransactAsync(state => state);

        var error = await Assert.ThrowsAsync<MailStoreException>(() => store.TransactAsync(state => state, expectedRevision: 0));

        Assert.Equal(MailErrorCode.Conflict, error.Code);
        Assert.Equal(1, (await store.ReadAsync()).Revision);
    }

    [Fact]
    public async Task Search_FiltersBySenderRecipientSubjectBodyAttachmentAndPaging()
    {
        using var temp = new TempDirectory();
        var store = Store(temp.Path);
        var account = Account();
        var message1 = Message(account.AccountId, "invoice from Acme", "paid invoice", "billing@acme.test", "INV-420");
        var message2 = Message(account.AccountId, "weekend", "hiking notes", "friend@example.test", "Plan");
        var attachment = new MailAttachment(Guid.NewGuid(), message1.MessageId, "receipt-420.pdf", "application/pdf", 23, null, false, true, new(false, false, []), "1");
        await store.TransactAsync(state => state with { Accounts = [account], Messages = [message1, message2], Attachments = [attachment] });
        var service = new MailDomainService(store);

        var sender = await service.SearchAsync(new MailSearchQuery(Sender: ["ACME.TEST"]));
        var recipient = await service.SearchAsync(new MailSearchQuery(Recipient: ["reader@example.test"]));
        var body = await service.SearchAsync(new MailSearchQuery(Body: "paid invoice"));
        var attachmentSearch = await service.SearchAsync(new MailSearchQuery(AttachmentName: "420"));
        var page = await service.SearchAsync(new MailSearchQuery(PageSize: 1));

        Assert.Equal(message1.MessageId, Assert.Single(sender.Messages).MessageId);
        Assert.Equal(2, recipient.TotalCount);
        Assert.Equal(message1.MessageId, Assert.Single(body.Messages).MessageId);
        Assert.Equal(message1.MessageId, Assert.Single(attachmentSearch.Messages).MessageId);
        Assert.Equal(2, page.TotalCount);
        Assert.Single(page.Messages);
    }

    [Fact]
    public async Task DraftUpdate_StaleRevisionPreservesBothVersionsAsConflict()
    {
        using var temp = new TempDirectory();
        var store = Store(temp.Path);
        var account = Account();
        var draft = Draft(account.AccountId);
        await store.TransactAsync(state => state with { Accounts = [account], Drafts = [draft] });
        var service = new MailDomainService(store);

        var current = await service.UpdateDraftAsync(draft.DraftId, 1, old => old with { PlainBody = "newer local body" });
        var stale = await service.UpdateDraftAsync(draft.DraftId, 1, old => old with { PlainBody = "offline edited body" });
        var saved = Assert.Single((await store.ReadAsync()).Drafts);

        Assert.True(current.IsSuccess);
        Assert.Equal(MailErrorCode.DraftConflict, stale.Error?.Code);
        Assert.Equal("newer local body", saved.PlainBody);
        Assert.Equal("offline edited body", saved.ConflictingRevision?.PlainBody);
    }

    [Fact]
    public async Task OfflineSend_IsQueuedIdempotentlyAndUndoStopsAtDeadline()
    {
        using var temp = new TempDirectory();
        var store = Store(temp.Path);
        var account = Account();
        var draft = Draft(account.AccountId);
        await store.TransactAsync(state => state with { Accounts = [account], Drafts = [draft] });
        var service = new MailDomainService(store);
        var now = DateTimeOffset.UtcNow;

        var first = await service.QueueSendAsync(draft, false, now, "send-key-1");
        var second = await service.QueueSendAsync(draft, false, now, "send-key-1");
        var queued = Assert.Single((await store.ReadAsync()).Outgoing);
        var cancelled = await service.CancelQueuedSendAsync(queued.MessageId, now.AddSeconds(4));
        var retryCancel = await service.CancelQueuedSendAsync(queued.MessageId, now.AddSeconds(6));

        Assert.True(first.IsSuccess);
        Assert.Equal(first.Value!.MessageId, second.Value!.MessageId);
        Assert.Equal(MailMessageState.Queued, queued.State);
        Assert.Equal(MailMessageState.Cancelled, cancelled.Value?.State);
        Assert.Equal(MailErrorCode.SendRejected, retryCancel.Error?.Code);
    }

    [Fact]
    public async Task ScheduledSend_DisclosesDeviceOnlyExecutionGuarantee()
    {
        using var temp = new TempDirectory();
        var store = Store(temp.Path);
        var account = Account();
        var draft = Draft(account.AccountId);
        await store.TransactAsync(state => state with { Accounts = [account], Drafts = [draft] });
        var service = new MailDomainService(store);
        var now = DateTimeOffset.UtcNow;

        var scheduled = await service.QueueScheduledSendAsync(draft.DraftId, now.AddHours(2), MailExecutionPolicy.ProviderPreferred, now, "scheduled-send-1");

        Assert.True(scheduled.IsSuccess);
        Assert.Equal(ScheduledExecutionLocation.Device, scheduled.Value!.Schedule!.Location);
        Assert.Contains("Provider scheduling is unavailable", scheduled.Value.Schedule.Guarantee, StringComparison.Ordinal);
    }

    [Fact]
    public void HtmlSanitizer_RemovesActiveContentAndBlocksRemoteRequestsByDefault()
    {
        var sanitizer = new MailHtmlSanitizer();
        const string html = "<p onclick=\"steal()\">Hello</p><script>steal()</script><img src=\"https://tracker.test/pixel\"><a href=\"javascript:steal()\">open</a>";

        var safe = sanitizer.Sanitize(html, allowRemoteContent: false);
        var optedIn = sanitizer.Sanitize(html, allowRemoteContent: true);

        Assert.DoesNotContain("<script", safe, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("onclick", safe, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("https://tracker.test", safe, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("javascript:", safe, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("https://tracker.test/pixel", optedIn, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void HtmlSanitizer_ProducesPlainTextWithoutMarkupOrScript()
    {
        var text = new MailHtmlSanitizer().ToPlainText("<p>Hello&nbsp;<b>world</b></p><script>secret()</script>");

        Assert.Equal("Hello world", text);
        Assert.DoesNotContain("secret", text, StringComparison.OrdinalIgnoreCase);
    }

    private static EncryptedJsonMailStateStore Store(string directory) =>
        new(Path.Combine(directory, "mail-state.json"), new TestKeyProvider("key", RandomNumberGenerator.GetBytes(32)));

    private static MailAccount Account()
    {
        var id = Guid.NewGuid();
        return new MailAccount(id, MailProviderKind.ImapSmtp, "imap-user", "reader@example.test", "Reader",
            MailCapability.Folders | MailCapability.Attachments, "credential:account-1", null,
            new MailSyncConfiguration(true, true, 30, true, [], TimeSpan.FromMinutes(5)),
            new MailSendingConfiguration(TimeSpan.FromSeconds(5), true), [], MailSyncState.Never, null, null, null, true);
    }

    private static MailDraft Draft(Guid accountId) => new(Guid.NewGuid(), accountId, Guid.NewGuid(),
        [new MailAddress("recipient@example.test")], [], [], "Subject", null, "body", [], null, 1,
        DateTimeOffset.UtcNow, null, false);

    private static MailMessage Message(Guid accountId, string subject, string body, string sender, string providerId)
    {
        var id = Guid.NewGuid();
        return new MailMessage(id, accountId, providerId, $"<{providerId}@example.test>", Guid.NewGuid(), null,
            "INBOX", [], DateTimeOffset.UtcNow, null, new MailAddress(sender),
            [new MailAddress("reader@example.test")], [], subject, body, body, null, false, false, false,
            "1", [], new(null, null, null, null), RemoteContentPolicy.Blocked, true);
    }

    private sealed class TestKeyProvider(string id, byte[] bytes) : IMailEncryptionKeyProvider
    {
        public ValueTask<MailEncryptionKey> GetCurrentKeyAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new MailEncryptionKey(id, bytes));
    }

    private sealed class TempDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "mail-tests-" + Guid.NewGuid().ToString("N"));
        public TempDirectory() => Directory.CreateDirectory(Path);
        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
