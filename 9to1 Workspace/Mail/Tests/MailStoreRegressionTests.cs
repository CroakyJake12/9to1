using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using HavenOS.Mail.Storage;
using Xunit;

namespace HavenOS.Mail.Tests;

public sealed class MailStoreRegressionTests
{
    [Fact]
    public async Task Independent_clients_preserve_both_same_cache_transactions()
    {
        using var temp = new TempDirectory();
        var keys = new PausingKeyProvider();
        var first = new EncryptedJsonMailStateStore(temp.CachePath, keys);
        var second = new EncryptedJsonMailStateStore(temp.CachePath, keys);
        var firstAccount = Account();
        var secondAccount = Account();
        var firstWrite = first.TransactAsync(state => state with { Accounts = [.. state.Accounts, firstAccount] });
        await keys.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var secondWrite = second.TransactAsync(state => state with { Accounts = [.. state.Accounts, secondAccount] });
        // Before the fix, the second client commits revision 1 while the first client is paused.
        await Task.WhenAny(secondWrite, Task.Delay(200));
        keys.Release.TrySetResult();
        await Task.WhenAll(firstWrite, secondWrite).WaitAsync(TimeSpan.FromSeconds(5));

        var restored = await new EncryptedJsonMailStateStore(temp.CachePath, keys).ReadAsync();
        Assert.Equal(2, restored.Revision);
        Assert.Equal(new[] { firstAccount.AccountId, secondAccount.AccountId }.Order(), restored.Accounts.Select(a => a.AccountId).Order());
        Assert.DoesNotContain(firstAccount.PrimaryAddress, await File.ReadAllTextAsync(temp.CachePath), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Independent_clients_compare_and_swap_allows_only_one_expected_revision_writer()
    {
        using var temp = new TempDirectory();
        var keys = new PausingKeyProvider();
        var first = new EncryptedJsonMailStateStore(temp.CachePath, keys);
        var second = new EncryptedJsonMailStateStore(temp.CachePath, keys);
        var firstWrite = first.TransactAsync(state => state, expectedRevision: 0);
        await keys.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var secondWrite = second.TransactAsync(state => state, expectedRevision: 0);
        await Task.WhenAny(secondWrite, Task.Delay(200));
        keys.Release.TrySetResult();
        await firstWrite.WaitAsync(TimeSpan.FromSeconds(5));

        var error = await Assert.ThrowsAsync<MailStoreException>(() => secondWrite);
        Assert.Equal(MailErrorCode.Conflict, error.Code);
        Assert.Equal(1, (await first.ReadAsync()).Revision);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Cancellation_while_another_client_holds_lock_preserves_committed_bytes(bool reading)
    {
        using var temp = new TempDirectory();
        var store = new EncryptedJsonMailStateStore(temp.CachePath, new TestKeyProvider());
        await store.TransactAsync(state => state);
        var before = await File.ReadAllBytesAsync(temp.CachePath);
        using var heldLock = new FileStream(temp.CachePath + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        var updated = false;

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            if (reading) await store.ReadAsync(cancellation.Token);
            else await store.TransactAsync(state => { updated = true; return state; }, cancellationToken: cancellation.Token);
        });

        Assert.False(updated);
        Assert.Equal(before, await File.ReadAllBytesAsync(temp.CachePath));
        heldLock.Dispose();
        Assert.Equal(2, (await store.TransactAsync(state => state)).Revision);
    }

    [Fact]
    public async Task Lock_contention_times_out_without_updating_or_changing_committed_bytes()
    {
        using var temp = new TempDirectory();
        var store = new EncryptedJsonMailStateStore(temp.CachePath, new TestKeyProvider());
        await store.TransactAsync(state => state);
        var before = await File.ReadAllBytesAsync(temp.CachePath);
        using var heldLock = new FileStream(temp.CachePath + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        var updated = false;

        var error = await Assert.ThrowsAsync<MailStoreException>(() =>
            store.TransactAsync(state => { updated = true; return state; }).WaitAsync(TimeSpan.FromSeconds(45)));

        Assert.Equal(MailErrorCode.ProviderUnavailable, error.Code);
        Assert.False(updated);
        Assert.Equal(before, await File.ReadAllBytesAsync(temp.CachePath));
    }

    [Fact]
    public async Task Cancellation_during_write_preserves_commit_and_releases_lock_for_another_client()
    {
        using var temp = new TempDirectory();
        var keys = new PausingKeyProvider(pauseOnCall: 2);
        // Reading consumes key call 1; pause on call 2 immediately before encryption/write.
        await File.WriteAllBytesAsync(temp.CachePath, Encrypt(JsonSerializer.Serialize(MailState.Empty), keys));
        var before = await File.ReadAllBytesAsync(temp.CachePath);
        var store = new EncryptedJsonMailStateStore(temp.CachePath, keys);
        using var cancellation = new CancellationTokenSource();
        var write = store.TransactAsync(state => state, cancellationToken: cancellation.Token);
        await keys.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => write);

        Assert.Equal(before, await File.ReadAllBytesAsync(temp.CachePath));
        Assert.Equal(1, (await new EncryptedJsonMailStateStore(temp.CachePath, keys).TransactAsync(state => state)).Revision);
    }

    [Fact]
    public async Task Unusable_lock_path_returns_structured_failure_without_changing_committed_bytes()
    {
        using var temp = new TempDirectory();
        var store = new EncryptedJsonMailStateStore(temp.CachePath, new TestKeyProvider());
        await store.TransactAsync(state => state);
        var before = await File.ReadAllBytesAsync(temp.CachePath);
        File.Delete(temp.CachePath + ".lock");
        Directory.CreateDirectory(temp.CachePath + ".lock");
        var updated = false;

        var error = await Assert.ThrowsAsync<MailStoreException>(() => store.TransactAsync(state => { updated = true; return state; }));

        Assert.Equal(MailErrorCode.ProviderUnavailable, error.Code);
        Assert.False(updated);
        Assert.Equal(before, await File.ReadAllBytesAsync(temp.CachePath));
    }

    public static IEnumerable<object[]> MalformedEnvelopeFields()
    {
        foreach (var field in new[] { "Format", "FormatVersion", "KeyId", "Nonce", "Tag", "Ciphertext" })
        {
            yield return [field, "missing"];
            yield return [field, "null"];
        }
        foreach (var field in new[] { "Format", "KeyId", "Nonce", "Tag", "Ciphertext" })
            yield return [field, "empty"];
        yield return ["Nonce", "wrong-length"];
        yield return ["Tag", "wrong-length"];
        yield return ["Nonce", "invalid-base64"];
    }

    [Theory]
    [MemberData(nameof(MalformedEnvelopeFields))]
    public async Task Malformed_envelope_returns_data_corrupt_and_preserves_original_bytes(string field, string kind)
    {
        using var temp = new TempDirectory();
        var keys = new TestKeyProvider();
        var store = new EncryptedJsonMailStateStore(temp.CachePath, keys);
        await store.TransactAsync(state => state);
        var envelope = JsonNode.Parse(await File.ReadAllBytesAsync(temp.CachePath))!.AsObject();
        if (kind == "missing") envelope.Remove(field);
        else envelope[field] = kind switch
        {
            "null" => null,
            "empty" => JsonValue.Create(""),
            "wrong-length" => JsonValue.Create(Convert.ToBase64String(new byte[1])),
            _ => JsonValue.Create("not base64!")
        };
        await File.WriteAllTextAsync(temp.CachePath, envelope.ToJsonString());
        var before = await File.ReadAllBytesAsync(temp.CachePath);

        var readError = await Assert.ThrowsAsync<MailStoreException>(() => store.ReadAsync());
        var writeError = await Assert.ThrowsAsync<MailStoreException>(() => store.TransactAsync(state => state));

        Assert.Equal(MailErrorCode.DataCorrupt, readError.Code);
        Assert.Equal(MailErrorCode.DataCorrupt, writeError.Code);
        Assert.Equal(before, await File.ReadAllBytesAsync(temp.CachePath));
    }

    [Theory]
    [InlineData("invalid-json")]
    [InlineData("null-state")]
    [InlineData("null-collection")]
    [InlineData("null-element")]
    [InlineData("null-settings")]
    public async Task Authenticated_invalid_plaintext_returns_data_corrupt_and_preserves_original_bytes(string kind)
    {
        using var temp = new TempDirectory();
        var keys = new TestKeyProvider();
        var state = JsonSerializer.SerializeToNode(MailState.Empty)!.AsObject();
        if (kind == "null-collection") state["Accounts"] = null;
        if (kind == "null-element") state["Accounts"] = new JsonArray((JsonNode?)null);
        if (kind == "null-settings") state["Settings"] = null;
        var plaintext = kind switch { "invalid-json" => "{", "null-state" => "null", _ => state.ToJsonString() };
        var bytes = Encrypt(plaintext, keys);
        await File.WriteAllBytesAsync(temp.CachePath, bytes);
        var store = new EncryptedJsonMailStateStore(temp.CachePath, keys);

        var readError = await Assert.ThrowsAsync<MailStoreException>(() => store.ReadAsync());
        var writeError = await Assert.ThrowsAsync<MailStoreException>(() => store.TransactAsync(value => value));

        Assert.Equal(MailErrorCode.DataCorrupt, readError.Code);
        Assert.Equal(MailErrorCode.DataCorrupt, writeError.Code);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(temp.CachePath));
    }

    [Fact]
    public async Task Unknown_envelope_version_preserves_unsupported_schema_failure()
    {
        using var temp = new TempDirectory();
        var store = new EncryptedJsonMailStateStore(temp.CachePath, new TestKeyProvider());
        await store.TransactAsync(state => state);
        var envelope = JsonNode.Parse(await File.ReadAllBytesAsync(temp.CachePath))!.AsObject();
        envelope["FormatVersion"] = 2;
        await File.WriteAllTextAsync(temp.CachePath, envelope.ToJsonString());
        var before = await File.ReadAllBytesAsync(temp.CachePath);

        var error = await Assert.ThrowsAsync<MailStoreException>(() => store.ReadAsync());

        Assert.Equal(MailErrorCode.UnsupportedSchemaVersion, error.Code);
        Assert.Equal(before, await File.ReadAllBytesAsync(temp.CachePath));
    }

    private static byte[] Encrypt(string plaintext, TestKeyProvider keys)
    {
        var input = Encoding.UTF8.GetBytes(plaintext);
        var nonce = RandomNumberGenerator.GetBytes(12);
        var tag = new byte[16];
        var ciphertext = new byte[input.Length];
        using var aes = new AesGcm(keys.Bytes, tag.Length);
        aes.Encrypt(nonce, input, ciphertext, tag, Encoding.UTF8.GetBytes("9to1.mail.encrypted-state\n1\nregression-key"));
        return JsonSerializer.SerializeToUtf8Bytes(new { Format = "9to1.mail.encrypted-state", FormatVersion = 1, KeyId = "regression-key", Nonce = nonce, Tag = tag, Ciphertext = ciphertext });
    }

    private static MailAccount Account() => new(Guid.NewGuid(), MailProviderKind.ImapSmtp,
        "offline-fixture", "reader@example.test", "Reader", MailCapability.None, "credential:fixture", null,
        new MailSyncConfiguration(true, true, 30, true, [], TimeSpan.FromMinutes(5)),
        new MailSendingConfiguration(TimeSpan.FromSeconds(5), true), [], MailSyncState.Never, null, null, null, true);

    private class TestKeyProvider : IMailEncryptionKeyProvider
    {
        public byte[] Bytes { get; } = RandomNumberGenerator.GetBytes(32);
        public virtual ValueTask<MailEncryptionKey> GetCurrentKeyAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new MailEncryptionKey("regression-key", Bytes));
    }

    private sealed class PausingKeyProvider(int pauseOnCall = 1) : TestKeyProvider
    {
        private int _calls;
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask<MailEncryptionKey> GetCurrentKeyAsync(CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref _calls) == pauseOnCall)
            {
                Entered.TrySetResult();
                await Release.Task.WaitAsync(cancellationToken);
            }
            return await base.GetCurrentKeyAsync(cancellationToken);
        }
    }

    private sealed class TempDirectory : IDisposable
    {
        private string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "mail-store-regression-" + Guid.NewGuid().ToString("N"));
        public string CachePath => System.IO.Path.Combine(Path, "mail-state.json");
        public TempDirectory() => Directory.CreateDirectory(Path);
        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
