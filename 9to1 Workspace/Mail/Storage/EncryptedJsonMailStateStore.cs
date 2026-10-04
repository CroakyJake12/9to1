using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HavenOS.Mail.Storage;

/// <summary>
/// Encrypted, versioned local Mail cache. The encryption key is never generated or persisted here;
/// Home must supply it from the user's OS-backed key store.
/// </summary>
public sealed class EncryptedJsonMailStateStore : IMailStateStore
{
    private const string Format = "9to1.mail.encrypted-state";
    private const int FormatVersion = 1;
    private const int MaximumCiphertextBytes = 512 * 1024 * 1024;
    private static readonly TimeSpan LockTimeout = TimeSpan.FromSeconds(30);
    private static readonly JsonSerializerOptions Json = CreateJsonOptions();
    private readonly string _path;
    private readonly IMailEncryptionKeyProvider _keys;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public EncryptedJsonMailStateStore(string path, IMailEncryptionKeyProvider keys)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _path = Path.GetFullPath(path);
        _keys = keys ?? throw new ArgumentNullException(nameof(keys));
    }

    public async Task<MailState> ReadAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var fileLock = await AcquireFileLockAsync(cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            return await ReadUnlockedAsync(cancellationToken).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    public async Task<MailState> TransactAsync(Func<MailState, MailState> update, long? expectedRevision = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(update);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var fileLock = await AcquireFileLockAsync(cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            var current = await ReadUnlockedAsync(cancellationToken).ConfigureAwait(false);
            if (expectedRevision is { } expected && current.Revision != expected)
                throw new MailStoreException(MailErrorCode.Conflict, $"Mail state changed from revision {expected} to {current.Revision}.");
            var proposed = update(current) ?? throw new InvalidOperationException("A Mail state transaction cannot return null.");
            ValidateState(proposed);
            var next = proposed with { SchemaVersion = MailState.CurrentSchemaVersion, Revision = checked(current.Revision + 1) };
            await WriteUnlockedAsync(next, cancellationToken).ConfigureAwait(false);
            return next;
        }
        finally { _gate.Release(); }
    }

    private async Task<FileStream> AcquireFileLockAsync(CancellationToken cancellationToken)
    {
        try { Directory.CreateDirectory(Path.GetDirectoryName(_path)!); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new MailStoreException(MailErrorCode.ProviderUnavailable, "The local Mail cache lock directory could not be opened.", ex);
        }
        var started = Stopwatch.GetTimestamp();
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                // Keep the sidecar permanently: deleting it can create competing locks on different
                // files while another process still owns the old handle. It contains no Mail data.
                return new FileStream(_path + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException ex) when (IsLockContention(ex))
            {
                if (Stopwatch.GetElapsedTime(started) >= LockTimeout)
                    throw new MailStoreException(MailErrorCode.ProviderUnavailable, "The local Mail cache is busy; retry the operation.", ex);
                await Task.Delay(TimeSpan.FromMilliseconds(25), cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                throw new MailStoreException(MailErrorCode.ProviderUnavailable, "The local Mail cache lock could not be opened.", ex);
            }
        }
    }

    private static bool IsLockContention(IOException exception) =>
        (exception.HResult & 0xffff) is 32 or 33 || (!OperatingSystem.IsWindows() && exception.HResult is 11 or 35);

    private async Task<MailState> ReadUnlockedAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_path)) return MailState.Empty;
        byte[] bytes;
        try
        {
            var info = new FileInfo(_path);
            if (info.Length > MaximumCiphertextBytes) throw new MailStoreException(MailErrorCode.DataCorrupt, "Encrypted Mail cache exceeds its safety size limit.");
            bytes = await File.ReadAllBytesAsync(_path, cancellationToken).ConfigureAwait(false);
        }
        catch (MailStoreException) { throw; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new MailStoreException(MailErrorCode.ProviderUnavailable, "The local Mail cache could not be read.", ex);
        }

        Envelope? envelope;
        try { envelope = JsonSerializer.Deserialize<Envelope>(bytes, Json); }
        catch (JsonException ex) { throw new MailStoreException(MailErrorCode.DataCorrupt, "The local Mail cache envelope is invalid.", ex); }
        if (envelope is null || string.IsNullOrWhiteSpace(envelope.Format) || string.IsNullOrWhiteSpace(envelope.KeyId) ||
            envelope.Nonce is null || envelope.Nonce.Length != 12 || envelope.Tag is null || envelope.Tag.Length != 16 ||
            envelope.Ciphertext is null || envelope.Ciphertext.Length == 0)
            throw new MailStoreException(MailErrorCode.DataCorrupt, "The local Mail cache envelope is invalid; the original file was preserved.");
        if (envelope.Format != Format || envelope.FormatVersion != FormatVersion)
            throw new MailStoreException(MailErrorCode.UnsupportedSchemaVersion, "The local Mail cache format is not supported; the original file was preserved.");
        if (envelope.Ciphertext.Length > MaximumCiphertextBytes)
            throw new MailStoreException(MailErrorCode.DataCorrupt, "Encrypted Mail cache exceeds its safety size limit.");

        var key = await GetKeyAsync(cancellationToken).ConfigureAwait(false);
        if (!string.Equals(key.KeyId, envelope.KeyId, StringComparison.Ordinal))
            throw new MailStoreException(MailErrorCode.EncryptionKeyUnavailable, "The key that protects the local Mail cache is not available.");
        var plaintext = new byte[envelope.Ciphertext.Length];
        try
        {
            using var aes = new AesGcm(key.KeyBytes.Span, envelope.Tag.Length);
            aes.Decrypt(envelope.Nonce, envelope.Ciphertext, envelope.Tag, plaintext, GetAssociatedData(envelope.Format, envelope.FormatVersion, envelope.KeyId));
            var state = JsonSerializer.Deserialize<MailState>(plaintext, Json)
                ?? throw new MailStoreException(MailErrorCode.DataCorrupt, "The decrypted Mail cache has no state.");
            ValidateState(state);
            if (state.SchemaVersion != MailState.CurrentSchemaVersion)
                throw new MailStoreException(MailErrorCode.UnsupportedSchemaVersion, "The local Mail cache requires a supported migration; the original file was preserved.");
            return state;
        }
        catch (CryptographicException ex)
        {
            throw new MailStoreException(MailErrorCode.EncryptionKeyUnavailable, "The Mail cache could not be authenticated with the current encryption key.", ex);
        }
        catch (JsonException ex)
        {
            throw new MailStoreException(MailErrorCode.DataCorrupt, "The decrypted Mail cache state is invalid; the original file was preserved.", ex);
        }
        finally { CryptographicOperations.ZeroMemory(plaintext); }
    }

    private async Task WriteUnlockedAsync(MailState state, CancellationToken cancellationToken)
    {
        var key = await GetKeyAsync(cancellationToken).ConfigureAwait(false);
        var plaintext = JsonSerializer.SerializeToUtf8Bytes(state, Json);
        try
        {
            if (plaintext.Length > MaximumCiphertextBytes)
                throw new MailStoreException(MailErrorCode.MailboxQuotaExceeded, "The local Mail cache exceeds its configured safety size limit.");
            var nonce = RandomNumberGenerator.GetBytes(12);
            var tag = new byte[16];
            var ciphertext = new byte[plaintext.Length];
            using (var aes = new AesGcm(key.KeyBytes.Span, tag.Length))
                aes.Encrypt(nonce, plaintext, ciphertext, tag, GetAssociatedData(Format, FormatVersion, key.KeyId));
            var envelope = new Envelope(Format, FormatVersion, key.KeyId, nonce, tag, ciphertext);
            var bytes = JsonSerializer.SerializeToUtf8Bytes(envelope, Json);
            // The reader bounds the serialized envelope, including its base64 expansion.
            if (bytes.Length > MaximumCiphertextBytes)
                throw new MailStoreException(MailErrorCode.MailboxQuotaExceeded, "The encrypted Mail cache exceeds its configured safety size limit.");
            var directory = Path.GetDirectoryName(_path)!;
            Directory.CreateDirectory(directory);
            var temp = Path.Combine(directory, $".{Path.GetFileName(_path)}.{Guid.NewGuid():N}.tmp");
            try
            {
                await using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 64 * 1024, FileOptions.Asynchronous | FileOptions.WriteThrough))
                {
                    await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
                    await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                    stream.Flush(flushToDisk: true);
                }
                cancellationToken.ThrowIfCancellationRequested();
                File.Move(temp, _path, overwrite: true);
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
            CryptographicOperations.ZeroMemory(bytes);
        }
        catch (MailStoreException) { throw; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException)
        {
            throw new MailStoreException(MailErrorCode.ProviderUnavailable, "The encrypted Mail cache could not be saved; the last committed state remains intact.", ex);
        }
        finally { CryptographicOperations.ZeroMemory(plaintext); }
    }

    private async ValueTask<MailEncryptionKey> GetKeyAsync(CancellationToken cancellationToken)
    {
        MailEncryptionKey key;
        try { key = await _keys.GetCurrentKeyAsync(cancellationToken).ConfigureAwait(false); }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { throw new MailStoreException(MailErrorCode.EncryptionKeyUnavailable, "Home could not provide the Mail cache encryption key.", ex); }
        if (key is null || string.IsNullOrWhiteSpace(key.KeyId) || key.KeyBytes.Length != 32)
            throw new MailStoreException(MailErrorCode.EncryptionKeyUnavailable, "Home returned an invalid Mail cache key handle.");
        return key;
    }

    private static byte[] GetAssociatedData(string format, int version, string keyId) =>
        System.Text.Encoding.UTF8.GetBytes($"{format}\n{version}\n{keyId}");

    private static void ValidateState(MailState state)
    {
        if (state.SchemaVersion is < 1 or > MailState.CurrentSchemaVersion)
            throw new MailStoreException(MailErrorCode.UnsupportedSchemaVersion, $"Mail state schema version {state.SchemaVersion} is not supported.");
        EnsureItems(state.Accounts);
        EnsureItems(state.Folders);
        EnsureItems(state.Messages);
        EnsureItems(state.Threads);
        EnsureItems(state.Attachments);
        EnsureItems(state.Drafts);
        EnsureItems(state.Outgoing);
        EnsureItems(state.PendingOperations);
        EnsureItems(state.SmartViews);
        EnsureItems(state.Rules);
        EnsureItems(state.RuleExecutions);
        if (state.Settings is null || state.Threads.Any(t => t.OrderedMessageIds is null))
            throw new MailStoreException(MailErrorCode.DataCorrupt, "Mail state is missing required settings or thread references.");
        EnsureUnique(state.Accounts.Select(x => x.AccountId), "account");
        EnsureUnique(state.Messages.Select(x => x.MessageId), "message");
        EnsureUnique(state.Threads.Select(x => x.ThreadId), "thread");
        EnsureUnique(state.Drafts.Select(x => x.DraftId), "draft");
        EnsureUnique(state.Outgoing.Select(x => x.MessageId), "outgoing message");
        EnsureUnique(state.PendingOperations.Select(x => x.OperationId), "pending operation");
        var accounts = state.Accounts.Select(x => x.AccountId).ToHashSet();
        if (state.Messages.Any(x => !accounts.Contains(x.AccountId)) || state.Folders.Any(x => !accounts.Contains(x.AccountId)) || state.Drafts.Any(x => !accounts.Contains(x.AccountId)))
            throw new MailStoreException(MailErrorCode.DataCorrupt, "Mail state contains an object whose owning account is missing.");
        if (state.Threads.Any(t => t.OrderedMessageIds.Any(id => state.Messages.All(m => m.MessageId != id || m.AccountId != t.AccountId))))
            throw new MailStoreException(MailErrorCode.DataCorrupt, "Mail thread references a missing or cross-account message.");
    }

    private static void EnsureItems<T>(IReadOnlyList<T>? items) where T : class
    {
        if (items is null || items.Any(item => item is null))
            throw new MailStoreException(MailErrorCode.DataCorrupt, "Mail state contains a missing collection or item.");
    }

    private static void EnsureUnique(IEnumerable<Guid> ids, string kind)
    {
        if (ids.Count() != ids.Distinct().Count()) throw new MailStoreException(MailErrorCode.DataCorrupt, $"Mail state contains duplicate {kind} identities.");
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.General) { MaxDepth = 64 };
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }

    private sealed record Envelope(
        [property: JsonRequired] string Format,
        [property: JsonRequired] int FormatVersion,
        [property: JsonRequired] string KeyId,
        [property: JsonRequired] byte[] Nonce,
        [property: JsonRequired] byte[] Tag,
        [property: JsonRequired] byte[] Ciphertext);
}
