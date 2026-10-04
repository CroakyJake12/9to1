using System.Security.Cryptography;
using HavenOS.Mail.Providers;
using HavenOS.Mail.Services;
using HavenOS.Mail.Storage;
using Xunit;

namespace HavenOS.Mail.Tests;

public sealed class MailDomainRegressionTests
{
    [Fact]
    public async Task LocalMutations_ReopenWithReplayPayloadsAndCurrentThreadUnreadCount()
    {
        using var fixture = new Fixture();
        var message = Message(fixture.Account.AccountId);
        await fixture.Store.TransactAsync(state => state with { Messages = [message] });
        var service = new MailDomainService(fixture.Store);
        var until = DateTimeOffset.UtcNow.AddHours(3);

        Assert.True((await service.MarkReadAsync(message.MessageId, true, "read")).IsSuccess);
        Assert.True((await service.StarAsync(message.MessageId, true, "star")).IsSuccess);
        Assert.True((await service.SnoozeAsync(message.MessageId, until, "snooze")).IsSuccess);

        var restored = await fixture.Reopen().ReadAsync();
        Assert.Equal("true", restored.PendingOperations.Single(op => op.Kind == MailOperationKind.MarkRead).Arguments["isRead"]);
        Assert.Equal("true", restored.PendingOperations.Single(op => op.Kind == MailOperationKind.Star).Arguments["isStarred"]);
        Assert.Equal(until, DateTimeOffset.Parse(restored.PendingOperations.Single(op => op.Kind == MailOperationKind.Snooze).Arguments["snoozedUntil"]));
        Assert.All(restored.PendingOperations, op => Assert.Equal(message.ProviderRevision, op.ExpectedProviderRevision));
        Assert.Equal(0, Assert.Single(restored.Threads).UnreadCount);
        Assert.Equal(message.MessageId, Assert.Single(Assert.Single(restored.Threads).OrderedMessageIds));
    }

    [Theory]
    [InlineData("different-value")]
    [InlineData("different-action")]
    [InlineData("different-message")]
    public async Task LocalMutation_IdempotencyKeyCannotChangeItsIntent(string change)
    {
        using var fixture = new Fixture();
        var first = Message(fixture.Account.AccountId);
        var second = Message(fixture.Account.AccountId);
        await fixture.Store.TransactAsync(state => state with { Messages = [first, second] });
        var service = new MailDomainService(fixture.Store);
        Assert.True((await service.MarkReadAsync(first.MessageId, true, "same-key")).IsSuccess);
        var before = await File.ReadAllBytesAsync(fixture.Path);

        var result = change switch
        {
            "different-action" => await service.StarAsync(first.MessageId, true, "same-key"),
            "different-message" => await service.MarkReadAsync(second.MessageId, true, "same-key"),
            _ => await service.MarkReadAsync(first.MessageId, false, "same-key"),
        };

        Assert.Equal(MailErrorCode.Conflict, result.Error?.Code);
        Assert.Equal(before, await File.ReadAllBytesAsync(fixture.Path));
        Assert.Single((await fixture.Store.ReadAsync()).PendingOperations);
    }

    [Fact]
    public async Task QueueSend_ReplayedKeyReturnsItsExactRecordedOutgoingMessage()
    {
        using var fixture = new Fixture();
        var draft = Draft(fixture.Account.AccountId);
        await fixture.SeedDraftsAsync(draft);
        var service = new MailDomainService(fixture.Store);
        var now = DateTimeOffset.UtcNow;
        var first = await service.QueueSendAsync(draft, false, now, "first-send");
        var second = await service.QueueSendAsync(draft, false, now, "second-send");
        var replay = await service.QueueSendAsync(draft, false, now, "second-send");

        Assert.True(first.IsSuccess);
        Assert.True(second.IsSuccess);
        Assert.NotEqual(first.Value!.MessageId, second.Value!.MessageId);
        Assert.Equal(second.Value.MessageId, replay.Value?.MessageId);
        Assert.Equal(2, (await fixture.Store.ReadAsync()).Outgoing.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task QueueSend_IdempotencyKeyCannotBeReusedForAnotherDraft(bool scheduled)
    {
        using var fixture = new Fixture();
        var first = Draft(fixture.Account.AccountId);
        var second = Draft(fixture.Account.AccountId);
        await fixture.SeedDraftsAsync(first, second);
        var service = new MailDomainService(fixture.Store);
        var now = DateTimeOffset.UtcNow;
        var when = now.AddHours(1);
        Assert.True((scheduled
            ? await service.QueueScheduledSendAsync(first.DraftId, when, MailExecutionPolicy.DeviceOnly, now, "same-key")
            : await service.QueueSendAsync(first, false, now, "same-key")).IsSuccess);
        var before = await File.ReadAllBytesAsync(fixture.Path);

        var collision = scheduled
            ? await service.QueueScheduledSendAsync(second.DraftId, when, MailExecutionPolicy.DeviceOnly, now, "same-key")
            : await service.QueueSendAsync(second, false, now, "same-key");

        Assert.Equal(MailErrorCode.Conflict, collision.Error?.Code);
        Assert.Equal(before, await File.ReadAllBytesAsync(fixture.Path));
        Assert.Single((await fixture.Store.ReadAsync()).Outgoing);
    }

    [Fact]
    public async Task QueueSend_IdempotencyKeyCannotConsumeAnUnrelatedMutation()
    {
        using var fixture = new Fixture();
        var draft = Draft(fixture.Account.AccountId);
        var message = Message(fixture.Account.AccountId);
        await fixture.Store.TransactAsync(state => state with { Drafts = [draft], Messages = [message] });
        var service = new MailDomainService(fixture.Store);
        Assert.True((await service.MarkReadAsync(message.MessageId, true, "same-key")).IsSuccess);

        var collision = await service.QueueSendAsync(draft, false, DateTimeOffset.UtcNow, "same-key");

        Assert.Equal(MailErrorCode.Conflict, collision.Error?.Code);
        Assert.Empty((await fixture.Store.ReadAsync()).Outgoing);
        Assert.Single((await fixture.Store.ReadAsync()).PendingOperations);
    }

    [Theory]
    [InlineData("regular-send")]
    [InlineData("different-time")]
    [InlineData("different-policy")]
    public async Task ScheduledSend_IdempotencyKeyCannotChangeScheduleSemantics(string change)
    {
        using var fixture = new Fixture();
        var draft = Draft(fixture.Account.AccountId);
        await fixture.SeedDraftsAsync(draft);
        var service = new MailDomainService(fixture.Store);
        var now = DateTimeOffset.UtcNow;
        var when = now.AddHours(1);
        Assert.True((await service.QueueScheduledSendAsync(draft.DraftId, when, MailExecutionPolicy.DeviceOnly, now, "same-key")).IsSuccess);
        var before = await File.ReadAllBytesAsync(fixture.Path);

        var collision = change == "regular-send"
            ? await service.QueueSendAsync(draft, false, now, "same-key")
            : await service.QueueScheduledSendAsync(draft.DraftId, change == "different-time" ? when.AddHours(1) : when,
                change == "different-policy" ? MailExecutionPolicy.ProviderPreferred : MailExecutionPolicy.DeviceOnly, now, "same-key");

        Assert.Equal(MailErrorCode.Conflict, collision.Error?.Code);
        Assert.Equal(before, await File.ReadAllBytesAsync(fixture.Path));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task QueueSend_RefusesDeletedAndConflictedStoredDrafts(bool scheduled, bool conflict)
    {
        using var fixture = new Fixture();
        var draft = Draft(fixture.Account.AccountId);
        var stored = conflict ? draft with { IsConflict = true, ConflictingRevision = draft with { PlainBody = "other revision" } }
            : draft with { IsDeleted = true };
        await fixture.SeedDraftsAsync(stored);
        var service = new MailDomainService(fixture.Store);
        var now = DateTimeOffset.UtcNow;
        var before = await File.ReadAllBytesAsync(fixture.Path);

        var result = scheduled
            ? await service.QueueScheduledSendAsync(draft.DraftId, now.AddHours(1), MailExecutionPolicy.DeviceOnly, now, "send")
            : await service.QueueSendAsync(draft, false, now, "send");

        Assert.Equal(conflict ? MailErrorCode.DraftConflict : MailErrorCode.DraftNotFound, result.Error?.Code);
        Assert.Empty((await fixture.Store.ReadAsync()).Outgoing);
        Assert.Equal(before, await File.ReadAllBytesAsync(fixture.Path));
    }

    [Theory]
    [InlineData("draft-edit")]
    [InlineData("draft-delete")]
    [InlineData("account-revoked")]
    [InlineData("account-credential")]
    public async Task SendDue_StateChangeDuringAuthorizationDoesNotSubmitStaleContext(string change)
    {
        using var fixture = new Fixture();
        var draft = Draft(fixture.Account.AccountId);
        await fixture.SeedDraftsAsync(draft);
        var provider = new RecordingProvider();
        var authorizer = new CallbackAuthorizer(async () =>
        {
            await fixture.Store.TransactAsync(state => change switch
            {
                "draft-edit" => state with { Drafts = [draft with { PlainBody = "new content", Revision = draft.Revision + 1 }] },
                "draft-delete" => state with { Drafts = [draft with { IsDeleted = true, Revision = draft.Revision + 1 }] },
                "account-revoked" => state with { Accounts = [fixture.Account with { IsAuthenticated = false }] },
                _ => state with { Accounts = [fixture.Account with { CredentialReference = "credential:new" }] },
            });
        });
        var service = new MailDomainService(fixture.Store, [provider], authorizer);
        var now = DateTimeOffset.UtcNow;
        var outgoing = await service.QueueSendAsync(draft, false, now, "send");

        var result = await service.SendDueAsync(outgoing.Value!.MessageId, now.AddSeconds(6));

        Assert.Equal(MailErrorCode.Conflict, result.Error?.Code);
        Assert.Equal(0, provider.SendCalls);
        var restored = await fixture.Reopen().ReadAsync();
        Assert.Equal(MailMessageState.Queued, Assert.Single(restored.Outgoing).State);
        Assert.Equal(MailOperationState.Pending, Assert.Single(restored.PendingOperations).State);
        if (change == "draft-edit") Assert.Equal("new content", Assert.Single(restored.Drafts).PlainBody);
        if (change == "account-revoked") Assert.False(Assert.Single(restored.Accounts).IsAuthenticated);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SendDue_RefusesUnresolvedDraftOrRevokedAccountBeforeAuthorization(bool revoked)
    {
        using var fixture = new Fixture();
        var draft = Draft(fixture.Account.AccountId);
        await fixture.SeedDraftsAsync(draft);
        var provider = new RecordingProvider();
        var authorizer = new CallbackAuthorizer(() => Task.CompletedTask);
        var service = new MailDomainService(fixture.Store, [provider], authorizer);
        var now = DateTimeOffset.UtcNow;
        var outgoing = await service.QueueSendAsync(draft, false, now, "send");
        await fixture.Store.TransactAsync(state => revoked
            ? state with { Accounts = [fixture.Account with { IsAuthenticated = false }] }
            : state with { Drafts = [draft with { IsConflict = true, ConflictingRevision = draft }] });

        var result = await service.SendDueAsync(outgoing.Value!.MessageId, now.AddSeconds(6));

        Assert.Equal(revoked ? MailErrorCode.AuthenticationRequired : MailErrorCode.DraftConflict, result.Error?.Code);
        Assert.Equal(0, provider.SendCalls);
        Assert.Equal(0, authorizer.Calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SendDue_ProviderAcceptanceCommitsSentAndCompletedJournalDespiteLateCancellation(bool cancelAfterAcceptance)
    {
        using var fixture = new Fixture();
        using var cancellation = new CancellationTokenSource();
        var draft = Draft(fixture.Account.AccountId);
        await fixture.SeedDraftsAsync(draft);
        var provider = new RecordingProvider(() => { if (cancelAfterAcceptance) cancellation.Cancel(); });
        var service = new MailDomainService(fixture.Store, [provider], new CallbackAuthorizer(() => Task.CompletedTask));
        var now = DateTimeOffset.UtcNow;
        var outgoing = await service.QueueSendAsync(draft, false, now, "send");

        var sent = await service.SendDueAsync(outgoing.Value!.MessageId, now.AddSeconds(6), cancellation.Token);

        Assert.True(sent.IsSuccess);
        var restored = await fixture.Reopen().ReadAsync();
        var recorded = Assert.Single(restored.Outgoing);
        Assert.Equal(MailMessageState.Sent, recorded.State);
        Assert.Equal("provider-accepted", recorded.ProviderMessageId);
        Assert.False(recorded.IsUndoAvailable);
        Assert.Equal(MailOperationState.Applied, Assert.Single(restored.PendingOperations).State);
        var repeated = await service.SendDueAsync(recorded.MessageId, now.AddMinutes(1));
        Assert.Equal(MailErrorCode.SendRejected, repeated.Error?.Code);
        Assert.Equal(1, provider.SendCalls);
    }

    private static MailDraft Draft(Guid accountId) => new(Guid.NewGuid(), accountId, Guid.NewGuid(),
        [new MailAddress("recipient@example.test")], [], [], "Subject", null, "body", [], null, 1,
        DateTimeOffset.UtcNow, null, false);

    private static MailMessage Message(Guid accountId) => new(Guid.NewGuid(), accountId, Guid.NewGuid().ToString("N"),
        null, Guid.NewGuid(), null, "INBOX", [], DateTimeOffset.UtcNow, null, new MailAddress("sender@example.test"),
        [new MailAddress("reader@example.test")], [], "Subject", "preview", "body", null, false, false, false,
        "provider-revision", [], new(null, null, null, null), RemoteContentPolicy.Blocked, true);

    private sealed class Fixture : IDisposable
    {
        private readonly string _directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "mail-domain-regressions-" + Guid.NewGuid().ToString("N"));
        private readonly KeyProvider _keys = new();
        public string Path => System.IO.Path.Combine(_directory, "state.json");
        public EncryptedJsonMailStateStore Store { get; }
        public MailAccount Account { get; } = new(Guid.NewGuid(), MailProviderKind.ImapSmtp, "provider-account", "reader@example.test", "Reader",
            MailCapability.Folders, "credential:account", null, new(true, true, 30, true, [], TimeSpan.FromMinutes(5)),
            new(TimeSpan.FromSeconds(5), true), [], MailSyncState.Never, null, null, null, true);

        public Fixture()
        {
            Directory.CreateDirectory(_directory);
            Store = Reopen();
            Store.TransactAsync(state => state with { Accounts = [Account] }).GetAwaiter().GetResult();
        }
        public EncryptedJsonMailStateStore Reopen() => new(Path, _keys);
        public Task<MailState> SeedDraftsAsync(params MailDraft[] drafts) => Store.TransactAsync(state => state with { Drafts = drafts });
        public void Dispose() => Directory.Delete(_directory, true);
    }

    private sealed class KeyProvider : IMailEncryptionKeyProvider
    {
        private readonly byte[] _key = RandomNumberGenerator.GetBytes(32);
        public ValueTask<MailEncryptionKey> GetCurrentKeyAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new MailEncryptionKey("test-key", _key));
    }

    private sealed class CallbackAuthorizer(Func<Task> callback) : IMailExternalActionAuthorizer
    {
        public int Calls { get; private set; }
        public async Task<MailResult<bool>> AuthorizeSendAsync(MailAccount account, MailDraft draft, CancellationToken cancellationToken = default)
        {
            Calls++;
            await callback();
            return MailResult<bool>.Success(true);
        }
    }

    private sealed class RecordingProvider(Action? afterAcceptance = null) : IMailProviderAdapter
    {
        public MailProviderKind Provider => MailProviderKind.ImapSmtp;
        public MailCapability Capabilities => MailCapability.Folders;
        public int SendCalls { get; private set; }
        public Task<MailProviderSendResult> SendAsync(MailAccount account, MailDraft draft, CancellationToken cancellationToken = default)
        {
            SendCalls++;
            afterAcceptance?.Invoke();
            return Task.FromResult(new MailProviderSendResult("provider-accepted", DateTimeOffset.UtcNow));
        }
        public Task<MailProviderSyncBatch> SynchronizeAsync(MailAccount account, string? changeCursor, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("These regressions do not contact a provider.");
        public Task WaitForChangesAsync(MailAccount account, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("These regressions do not contact a provider.");
    }
}
