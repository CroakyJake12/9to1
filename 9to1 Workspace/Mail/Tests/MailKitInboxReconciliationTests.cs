using System.Security.Cryptography;
using System.Text.Json;
using HavenOS.Mail.Providers;
using HavenOS.Mail.Services;
using HavenOS.Mail.Storage;
using MailKit;
using MailKit.Net.Imap;
using MailKit.Security;
using MimeKit;
using Xunit;

namespace HavenOS.Mail.Tests;

/// <summary>Uses the same maintained, normally trusted, loopback-only GreenMail lane as the existing transport tests.</summary>
public sealed class MailKitInboxReconciliationTests
{
    [Fact]
    public async Task Actual_TLS_existing_flags_and_expunge_reconcile_without_rehydrating_cached_body_or_overwriting_pending_local_edit()
    {
        var host = Required("HAVEN_MAIL_FIXTURE_HOST");
        Assert.True(host is "localhost" or "127.0.0.1" or "::1");
        var account = MailKitTransportFixture.Account(host,
            int.Parse(Required("HAVEN_MAIL_FIXTURE_IMAP_TLS_PORT"), System.Globalization.CultureInfo.InvariantCulture),
            int.Parse(Required("HAVEN_MAIL_FIXTURE_SMTP_TLS_PORT"), System.Globalization.CultureInfo.InvariantCulture),
            Required("HAVEN_MAIL_FIXTURE_ADDRESS"));
        var secret = Required("HAVEN_MAIL_FIXTURE_PASSWORD");
        using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        var token = lifetime.Token;
        var root = Path.Combine(Path.GetTempPath(), "astra-mail-inbox-" + Guid.NewGuid().ToString("N"));
        var cachePath = Path.Combine(root, "mail-state.json");
        var keyBytes = RandomNumberGenerator.GetBytes(32);
        var keys = new FixtureKey(keyBytes);
        var attachmentId = Guid.NewGuid();
        var attachmentPath = Path.Combine(root, "original-synthetic-attachment.bin");
        var draft = MailKitTransportFixture.Draft(account) with
        {
            Subject = "Canonical inbox reconciliation " + Guid.NewGuid().ToString("N"),
            AttachmentIds = [attachmentId],
        };
        var provider = new MailKitImapSmtpProvider(new FixtureCredentials(secret),
            new OriginalSyntheticAttachment(account.AccountId, draft.DraftId, attachmentId, attachmentPath));
        var store = new EncryptedJsonMailStateStore(cachePath, keys);
        var service = new MailDomainService(store, [provider]);
        Exception? primary = null;
        try
        {
            Directory.CreateDirectory(root);
            await File.WriteAllBytesAsync(attachmentPath, RandomNumberGenerator.GetBytes(64), token);
            Assert.True((await service.AddAccountAsync(account, token)).IsSuccess);
            Assert.True((await service.SyncAccountAsync(account.AccountId, token)).IsSuccess);
            var sent = await provider.SendAsync(account, draft, token);
            Assert.False(string.IsNullOrWhiteSpace(sent.ProviderMessageId));
            MailMessage? received = null;
            while (received is null)
            {
                Assert.True((await service.SyncAccountAsync(account.AccountId, token)).IsSuccess);
                received = (await store.ReadAsync(token)).Messages.SingleOrDefault(message => message.Subject == draft.Subject);
                if (received is null) await Task.Delay(TimeSpan.FromMilliseconds(100), token);
            }
            var original = received;
            Assert.False(original.IsRead);
            Assert.Single(original.AttachmentIds);
            Assert.False(string.IsNullOrWhiteSpace(original.PlainBody));
            var persistedCursor = (await store.ReadAsync(token)).Accounts.Single(item => item.AccountId == account.AccountId).ChangeCursor;
            var unchangedObservation = await provider.SynchronizeAsync(account, persistedCursor, token);
            Assert.Equal(original.ProviderRevision,
                Assert.Single(unchangedObservation.MessageStates!, message => message.MessageId == original.MessageId).ProviderRevision);
            Assert.DoesNotContain(unchangedObservation.Messages, message => message.MessageId == original.MessageId);
            var uid = new UniqueId(uint.Parse(original.ProviderMessageId, System.Globalization.CultureInfo.InvariantCulture));

            // Same folder/provider UID in another canonical account cannot be changed by this account's inventory.
            var otherAccount = account with { AccountId = Guid.NewGuid(), ProviderAccountIdentity = "isolated-scope-control" };
            Assert.True((await service.AddAccountAsync(otherAccount, token)).IsSuccess);
            var control = original with { MessageId = Guid.NewGuid(), AccountId = otherAccount.AccountId, ThreadId = Guid.NewGuid(), PlainBody = "Unrelated cached scope" };
            await store.TransactAsync(state => state with { Messages = [.. state.Messages, control] }, cancellationToken: token);
            var controlJson = JsonSerializer.Serialize(control);

            await MutateActualServerAsync(MessageFlags.Seen | MessageFlags.Flagged, expunge: false);
            var flags = await service.SyncAccountAsync(account.AccountId, token);
            Assert.True(flags.IsSuccess);
            Assert.True(flags.Value!.WasIncremental);
            Assert.DoesNotContain(flags.Value.Messages, message => message.MessageId == original.MessageId);
            var observed = Assert.Single(flags.Value.MessageStates!, message => message.MessageId == original.MessageId);
            Assert.True(observed.IsRead); Assert.True(observed.IsStarred);
            var reopened = await new EncryptedJsonMailStateStore(cachePath, keys).ReadAsync(token);
            var current = Assert.Single(reopened.Messages, message => message.MessageId == original.MessageId);
            Assert.True(current.IsRead); Assert.True(current.IsStarred);
            Assert.Equal(0, Assert.Single(reopened.Threads, thread => thread.ThreadId == original.ThreadId).UnreadCount);
            Assert.Equal(original.AccountId, current.AccountId);
            Assert.Equal(original.ThreadId, current.ThreadId);
            Assert.Equal(original.PlainBody, current.PlainBody);
            Assert.Equal(original.HtmlBody, current.HtmlBody);
            Assert.Equal(original.AttachmentIds, current.AttachmentIds);
            Assert.Equal(controlJson, JsonSerializer.Serialize(Assert.Single(reopened.Messages, message => message.MessageId == control.MessageId)));

            var idempotency = "pending-read-" + Guid.NewGuid().ToString("N");
            var local = await service.MarkReadAsync(original.MessageId, false, idempotency, token);
            Assert.True(local.IsSuccess); Assert.False(local.Value!.IsRead);
            var starIdempotency = "pending-star-" + Guid.NewGuid().ToString("N");
            Assert.True((await service.StarAsync(original.MessageId, false, starIdempotency, token)).IsSuccess);
            var originalLocal = await new EncryptedJsonMailStateStore(cachePath, keys).ReadAsync(token);
            Assert.Equal(1, Assert.Single(originalLocal.Threads, thread => thread.ThreadId == original.ThreadId).UnreadCount);
            Assert.Equal("false", Assert.Single(originalLocal.PendingOperations, item => item.IdempotencyKey == idempotency).Arguments["isRead"]);
            Assert.Equal("false", Assert.Single(originalLocal.PendingOperations, item => item.IdempotencyKey == starIdempotency).Arguments["isStarred"]);
            var expectedProviderRevision = current.ProviderRevision;
            Assert.False(string.IsNullOrWhiteSpace(expectedProviderRevision));
            await MutateActualServerAsync(MessageFlags.Answered, expunge: false);
            var changed = await service.SyncAccountAsync(account.AccountId, token);
            Assert.True(changed.IsSuccess);
            Assert.DoesNotContain(changed.Value!.Messages, message => message.MessageId == original.MessageId);
            Assert.True(Assert.Single(changed.Value.MessageStates!, message => message.MessageId == original.MessageId).IsRead);
            reopened = await new EncryptedJsonMailStateStore(cachePath, keys).ReadAsync(token);
            current = Assert.Single(reopened.Messages, message => message.MessageId == original.MessageId);
            Assert.False(current.IsRead); Assert.False(current.IsStarred); Assert.True(current.IsImportant);
            Assert.NotEqual(expectedProviderRevision, current.ProviderRevision);
            var operation = Assert.Single(reopened.PendingOperations, item => item.IdempotencyKey == idempotency);
            Assert.Equal(MailOperationState.Conflict, operation.State);
            Assert.Equal("ProviderRevisionConflict", operation.ErrorCode);
            Assert.Equal(expectedProviderRevision, operation.ExpectedProviderRevision);
            Assert.Equal("false", operation.Arguments["isRead"]);
            var starOperation = Assert.Single(reopened.PendingOperations, item => item.IdempotencyKey == starIdempotency);
            Assert.Equal(MailOperationState.Conflict, starOperation.State);
            Assert.Equal("ProviderRevisionConflict", starOperation.ErrorCode);
            Assert.Equal(expectedProviderRevision, starOperation.ExpectedProviderRevision);
            Assert.Equal("false", starOperation.Arguments["isStarred"]);

            // A genuine Deleted flag is distinct from actual expunge: preserve
            // the original body/attachment IDs, and restore the same canonical
            // identity if the server removes its flag before expunging.
            await MutateActualServerAsync(MessageFlags.Deleted, expunge: false);
            var flaggedDeleted = await service.SyncAccountAsync(account.AccountId, token);
            Assert.True(flaggedDeleted.IsSuccess);
            Assert.True(Assert.Single(flaggedDeleted.Value!.MessageStates!, message => message.MessageId == original.MessageId).IsDeleted);
            reopened = await new EncryptedJsonMailStateStore(cachePath, keys).ReadAsync(token);
            current = Assert.Single(reopened.Messages, message => message.MessageId == original.MessageId);
            Assert.True(current.IsDeleted); Assert.Equal(original.PlainBody, current.PlainBody);
            Assert.Equal(original.AttachmentIds, current.AttachmentIds);
            Assert.DoesNotContain(reopened.Threads, thread => thread.OrderedMessageIds.Contains(original.MessageId));
            await MutateActualServerAsync(MessageFlags.None, expunge: false, flagsToRemove: MessageFlags.Deleted);
            var restored = await service.SyncAccountAsync(account.AccountId, token);
            Assert.True(restored.IsSuccess);
            Assert.False(Assert.Single(restored.Value!.MessageStates!, message => message.MessageId == original.MessageId).IsDeleted);
            reopened = await new EncryptedJsonMailStateStore(cachePath, keys).ReadAsync(token);
            current = Assert.Single(reopened.Messages, message => message.MessageId == original.MessageId);
            Assert.False(current.IsDeleted); Assert.False(current.IsRead); Assert.False(current.IsStarred);
            Assert.Equal(original.PlainBody, current.PlainBody); Assert.Equal(original.AttachmentIds, current.AttachmentIds);
            Assert.Contains(reopened.Threads, thread => thread.ThreadId == original.ThreadId && thread.OrderedMessageIds.Contains(original.MessageId));

            await MutateActualServerAsync(MessageFlags.Deleted, expunge: true);
            var deleted = await service.SyncAccountAsync(account.AccountId, token);
            Assert.True(deleted.IsSuccess);
            Assert.DoesNotContain(deleted.Value!.Messages, message => message.MessageId == original.MessageId);
            Assert.DoesNotContain(Assert.Single(deleted.Value.FolderInventories!).MessageIds, id => id == original.MessageId);
            reopened = await new EncryptedJsonMailStateStore(cachePath, keys).ReadAsync(token);
            var retired = Assert.Single(reopened.Messages, message => message.MessageId == original.MessageId);
            Assert.True(retired.IsDeleted);
            Assert.Equal(original.PlainBody, retired.PlainBody);
            Assert.Equal(original.HtmlBody, retired.HtmlBody);
            Assert.Equal(original.AttachmentIds, retired.AttachmentIds);
            Assert.DoesNotContain(reopened.Threads, thread => thread.OrderedMessageIds.Contains(original.MessageId));
            operation = Assert.Single(reopened.PendingOperations, item => item.IdempotencyKey == idempotency);
            Assert.Equal(MailOperationState.Conflict, operation.State);
            Assert.Equal("ProviderMessageGone", operation.ErrorCode);
            Assert.Equal(expectedProviderRevision, operation.ExpectedProviderRevision);
            Assert.Equal("false", operation.Arguments["isRead"]);
            starOperation = Assert.Single(reopened.PendingOperations, item => item.IdempotencyKey == starIdempotency);
            Assert.Equal(MailOperationState.Conflict, starOperation.State);
            Assert.Equal("ProviderMessageGone", starOperation.ErrorCode);
            Assert.Equal(expectedProviderRevision, starOperation.ExpectedProviderRevision);
            Assert.Equal("false", starOperation.Arguments["isStarred"]);
            Assert.Equal(controlJson, JsonSerializer.Serialize(Assert.Single(reopened.Messages, message => message.MessageId == control.MessageId)));
            Assert.DoesNotContain(draft.Subject, await File.ReadAllTextAsync(cachePath, token));

            // Repeating a real empty-membership observation cannot duplicate canonical state or erase the conflict.
            Assert.True((await service.SyncAccountAsync(account.AccountId, token)).IsSuccess);
            reopened = await new EncryptedJsonMailStateStore(cachePath, keys).ReadAsync(token);
            Assert.Single(reopened.Messages, message => message.MessageId == original.MessageId);
            Assert.Equal("ProviderMessageGone", Assert.Single(reopened.PendingOperations, item => item.IdempotencyKey == idempotency).ErrorCode);

            async Task MutateActualServerAsync(MessageFlags flagsToAdd, bool expunge, MessageFlags flagsToRemove = MessageFlags.None)
            {
                using var client = new ImapClient();
                Exception? mutatorFailure = null;
                try
                {
                    await client.ConnectAsync(host, account.Connection!.Imap.Port, SecureSocketOptions.SslOnConnect, token);
                    await client.AuthenticateAsync(account.Connection.Username, secret, token);
                    await client.Inbox.OpenAsync(FolderAccess.ReadWrite, token);
                    var actualMime = await client.Inbox.GetMessageAsync(uid, token);
                    var part = Assert.IsType<MimePart>(Assert.Single(actualMime.Attachments));
                    var content = part.Content ?? throw new InvalidOperationException("The actual submitted MIME attachment content is missing.");
                    using var actualBytes = new MemoryStream();
                    await content.DecodeToAsync(actualBytes, token);
                    Assert.Equal(await File.ReadAllBytesAsync(attachmentPath, token), actualBytes.ToArray());
                    if (flagsToRemove != MessageFlags.None)
                        await client.Inbox.RemoveFlagsAsync(new[] { uid }, flagsToRemove, silent: true, cancellationToken: token);
                    if (flagsToAdd != MessageFlags.None)
                        await client.Inbox.AddFlagsAsync(new[] { uid }, flagsToAdd, silent: true, cancellationToken: token);
                    if (expunge) await client.Inbox.ExpungeAsync(token);
                }
                catch (Exception error)
                {
                    mutatorFailure = error;
                    throw;
                }
                finally
                {
                    var cleanupFailures = new List<Exception>();
                    try { if (client.IsConnected) await client.DisconnectAsync(true, CancellationToken.None); }
                    catch (Exception error) { CollectCleanupFailure(cleanupFailures, mutatorFailure, error); }
                    RethrowCleanupFailures("Original server mutation or disconnect failed.", mutatorFailure, cleanupFailures);
                }
            }
        }
        catch (Exception error)
        {
            primary = error;
            throw;
        }
        finally
        {
            var cleanupFailures = new List<Exception>();
            try { CryptographicOperations.ZeroMemory(keyBytes); }
            catch (Exception error) { CollectCleanupFailure(cleanupFailures, primary, error); }
            try { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
            catch (Exception error) { CollectCleanupFailure(cleanupFailures, primary, error); }
            RethrowCleanupFailures("Original Inbox fixture or cleanup failed.", primary, cleanupFailures);
        }
    }

    [Fact]
    public async Task Actual_TLS_warm_cursor_recovers_removed_cache_and_held_original_batch_refuses_intervening_canonical_edit()
    {
        var host = Required("HAVEN_MAIL_FIXTURE_HOST");
        Assert.True(host is "localhost" or "127.0.0.1" or "::1");
        var account = MailKitTransportFixture.Account(host,
            int.Parse(Required("HAVEN_MAIL_FIXTURE_IMAP_TLS_PORT"), System.Globalization.CultureInfo.InvariantCulture),
            int.Parse(Required("HAVEN_MAIL_FIXTURE_SMTP_TLS_PORT"), System.Globalization.CultureInfo.InvariantCulture),
            Required("HAVEN_MAIL_FIXTURE_ADDRESS"));
        var secret = Required("HAVEN_MAIL_FIXTURE_PASSWORD");
        using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        var token = lifetime.Token;
        var root = Path.Combine(Path.GetTempPath(), "astra-mail-held-inbox-" + Guid.NewGuid().ToString("N"));
        var cachePath = Path.Combine(root, "mail-state.json");
        var keyBytes = RandomNumberGenerator.GetBytes(32);
        var keys = new FixtureKey(keyBytes);
        var provider = new MailKitImapSmtpProvider(new FixtureCredentials(secret));
        var store = new EncryptedJsonMailStateStore(cachePath, keys);
        var service = new MailDomainService(store, [provider]);
        var held = new HeldActualProvider(provider);
        Task<MailResult<MailProviderSyncBatch>>? syncing = null;
        Exception? primary = null;
        try
        {
            Assert.True((await service.AddAccountAsync(account, token)).IsSuccess);
            Assert.True((await service.SyncAccountAsync(account.AccountId, token)).IsSuccess);
            var draft = MailKitTransportFixture.Draft(account) with { Subject = "Original held Inbox " + Guid.NewGuid().ToString("N") };
            await provider.SendAsync(account, draft, token);
            MailMessage? original = null;
            while (original is null)
            {
                Assert.True((await service.SyncAccountAsync(account.AccountId, token)).IsSuccess);
                original = (await store.ReadAsync(token)).Messages.SingleOrDefault(message => message.Subject == draft.Subject);
                if (original is null) await Task.Delay(TimeSpan.FromMilliseconds(100), token);
            }

            // Real cache eviction retains the warm server cursor. Recover the same
            // UID's original MIME body instead of treating a live UID as deletion.
            await store.TransactAsync(state => state with
            {
                Messages = state.Messages.Where(message => message.MessageId != original.MessageId).ToArray(),
                Threads = state.Threads.Where(thread => thread.ThreadId != original.ThreadId).ToArray(),
            }, cancellationToken: token);
            var recovered = await service.SyncAccountAsync(account.AccountId, token);
            Assert.True(recovered.IsSuccess); Assert.False(recovered.Value!.WasIncremental);
            var recoveredBody = Assert.Single(recovered.Value.Messages, message => message.MessageId == original.MessageId);
            Assert.Equal(original.PlainBody, recoveredBody.PlainBody); Assert.Equal(original.ThreadId, recoveredBody.ThreadId);
            var reopened = await new EncryptedJsonMailStateStore(cachePath, keys).ReadAsync(token);
            Assert.Equal(original.PlainBody, Assert.Single(reopened.Messages, message => message.MessageId == original.MessageId).PlainBody);

            // This is a real server mutation. The scheduling wrapper below only
            // holds the actual completed MailKit batch; it does not fabricate it.
            using (var client = new ImapClient())
            {
                await client.ConnectAsync(host, account.Connection!.Imap.Port, SecureSocketOptions.SslOnConnect, token);
                await client.AuthenticateAsync(account.Connection.Username, secret, token);
                await client.Inbox.OpenAsync(FolderAccess.ReadWrite, token);
                await client.Inbox.AddFlagsAsync(new[] { new UniqueId(uint.Parse(original.ProviderMessageId, System.Globalization.CultureInfo.InvariantCulture)) },
                    MessageFlags.Seen, silent: true, cancellationToken: token);
                await client.DisconnectAsync(true, token);
            }
            var heldService = new MailDomainService(store, [held]);
            syncing = heldService.SyncAccountAsync(account.AccountId, token);
            var realBatch = await held.Entered.Task.WaitAsync(TimeSpan.FromSeconds(15), token);
            Assert.True(Assert.Single(realBatch.MessageStates!, message => message.MessageId == original.MessageId).IsRead);
            var idempotency = "original-held-read-" + Guid.NewGuid().ToString("N");
            Assert.True((await service.MarkReadAsync(original.MessageId, false, idempotency, token)).IsSuccess);
            var originalPhysicalEdit = await File.ReadAllBytesAsync(cachePath, token);
            held.Release.TrySetResult();
            var refused = await syncing;
            Assert.False(refused.IsSuccess); Assert.Equal(MailErrorCode.Conflict, refused.Error!.Code);
            Assert.Equal(originalPhysicalEdit, await File.ReadAllBytesAsync(cachePath, token));
            reopened = await new EncryptedJsonMailStateStore(cachePath, keys).ReadAsync(token);
            var preserved = Assert.Single(reopened.Messages, message => message.MessageId == original.MessageId);
            Assert.False(preserved.IsRead); Assert.Equal(original.PlainBody, preserved.PlainBody);
            var pending = Assert.Single(reopened.PendingOperations, operation => operation.IdempotencyKey == idempotency);
            Assert.Equal(MailOperationState.Pending, pending.State);

            // A fresh actual observation may apply other provider fields, but the
            // original local value and expected provider revision remain explicit.
            Assert.True((await service.SyncAccountAsync(account.AccountId, token)).IsSuccess);
            reopened = await new EncryptedJsonMailStateStore(cachePath, keys).ReadAsync(token);
            Assert.False(Assert.Single(reopened.Messages, message => message.MessageId == original.MessageId).IsRead);
            var conflict = Assert.Single(reopened.PendingOperations, operation => operation.IdempotencyKey == idempotency);
            Assert.Equal(MailOperationState.Conflict, conflict.State); Assert.Equal("ProviderRevisionConflict", conflict.ErrorCode);
            Assert.Equal(pending.ExpectedProviderRevision, conflict.ExpectedProviderRevision);
            Assert.Equal(pending.OperationId, conflict.OperationId);
        }
        catch (Exception error)
        {
            primary = error;
            throw;
        }
        finally
        {
            var cleanupFailures = new List<Exception>();
            held.Release.TrySetResult();
            if (syncing is not null)
            {
                try { await syncing; }
                catch (Exception error) { CollectCleanupFailure(cleanupFailures, primary, error); }
            }
            // Every cleanup is attempted after the original task is released
            // and awaited, even if it reveals a distinct underlying failure.
            try { CryptographicOperations.ZeroMemory(keyBytes); }
            catch (Exception error) { CollectCleanupFailure(cleanupFailures, primary, error); }
            try { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
            catch (Exception error) { CollectCleanupFailure(cleanupFailures, primary, error); }
            RethrowCleanupFailures("Original held Inbox task or cleanup failed.", primary, cleanupFailures);
        }
    }

    private static void CollectCleanupFailure(ICollection<Exception> failures, Exception? primary, Exception error)
    {
        // The same original task exception may already be the primary failure;
        // its original rethrow remains active. Preserve every distinct failure.
        if (!object.ReferenceEquals(error, primary)
            && !failures.Any(existing => object.ReferenceEquals(existing, error))) failures.Add(error);
    }

    private static void RethrowCleanupFailures(string context, Exception? primary, IReadOnlyCollection<Exception> cleanupFailures)
    {
        if (cleanupFailures.Count == 0) return;
        var failures = new List<Exception>(cleanupFailures);
        if (primary is not null) failures.Insert(0, primary);
        throw new AggregateException(context, failures);
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

    private sealed class FixtureKey(byte[] key) : IMailEncryptionKeyProvider
    {
        public ValueTask<MailEncryptionKey> GetCurrentKeyAsync(CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            return ValueTask.FromResult(new MailEncryptionKey("isolated-inbox-fixture", key));
        }
    }

    // An owned synthetic physical input to the maintained real SMTP/IMAP
    // fixture only. It does not claim Files/Home read or Send permission.
    private sealed class OriginalSyntheticAttachment(Guid accountId, Guid draftId, Guid attachmentId, string path)
        : IMailAttachmentContentSource
    {
        public async ValueTask<MailAttachmentContent> ReadAsync(Guid requestedAccount, Guid requestedDraft,
            Guid requestedAttachment, CancellationToken cancellationToken = default)
        {
            if (requestedAccount != accountId || requestedDraft != draftId || requestedAttachment != attachmentId)
                throw new UnauthorizedAccessException("The original synthetic fixture attachment scope changed.");
            return new(attachmentId, "original-synthetic-attachment.bin", "application/octet-stream",
                await File.ReadAllBytesAsync(path, cancellationToken));
        }
    }

    private sealed class HeldActualProvider(IMailProviderAdapter actual) : IMailProviderAdapter
    {
        public MailProviderKind Provider => actual.Provider;
        public MailCapability Capabilities => actual.Capabilities;
        public TaskCompletionSource<MailProviderSyncBatch> Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<MailProviderSyncBatch> SynchronizeAsync(MailAccount account, string? cursor, CancellationToken token = default)
        {
            var batch = await actual.SynchronizeAsync(account, cursor, token);
            Entered.TrySetResult(batch);
            await Release.Task.WaitAsync(token);
            return batch;
        }
        public Task<MailProviderSendResult> SendAsync(MailAccount account, MailDraft draft, CancellationToken token = default) => actual.SendAsync(account, draft, token);
        public Task WaitForChangesAsync(MailAccount account, CancellationToken token = default) => actual.WaitForChangesAsync(account, token);
    }
}
