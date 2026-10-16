using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NineToOne.Dulche.Context;

/// <summary>An identifier, never a key or bearer capability. Possession grants no access.</summary>
public sealed record UcmlKeyReference(string OwnerId, Guid KeyId);
public sealed record UcmlCiphertext(byte[] Nonce, byte[] Ciphertext, byte[] Tag);

/// <summary>Trusted-process-only cryptographic boundary; never expose this as a model tool.
/// The host independently authenticates the actual actor and authorizes context disclosure.</summary>
public interface IUcmlKeyCustody
{
    Task<UcmlCiphertext> EncryptAsync(UcmlKeyReference key, string day, byte[] salt,
        byte[] plaintext, byte[] associatedData, CancellationToken cancellationToken);
    Task<byte[]> DecryptAsync(UcmlKeyReference key, string day, byte[] salt,
        UcmlCiphertext ciphertext, byte[] associatedData, CancellationToken cancellationToken);
}

/// <summary>AES-GCM packaging of UCML interchange. This neither changes canonical storage
/// encryption nor grants permission to read memory. Only authenticated hosts may call it.</summary>
public sealed class UcmlProtection(IUcmlKeyCustody custody)
{
    public const int MaximumEnvelopeBytes = 768 * 1024;
    private const string Algorithm = "A256GCM-HKDF-SHA256";
    private static readonly UTF8Encoding Utf8 = new(false, true);
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        RespectNullableAnnotations = true,
        RespectRequiredConstructorParameters = true,
        MaxDepth = 8
    };
    private sealed record Header(int Version, string Algorithm, string OwnerId, string ScopeKind,
        string ScopeId, Guid DocumentId, long Revision, Guid KeyId, string KeyDay, string Salt);
    private sealed record Envelope(Header Header, string Nonce, string Ciphertext, string Tag);

    public async Task<string> SealAsync(UcmlDocument document, string expectedOwnerId,
        UcmlKeyReference key, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(custody);
        UcmlAesGcmKeyCustody.ValidateReference(key);
        UcmlCodec.Validate(document);
        if (document.Metadata.OwnerId != expectedOwnerId || key.OwnerId != expectedOwnerId)
            throw new UnauthorizedAccessException("The authenticated owner does not match this UCML document/key.");
        cancellationToken.ThrowIfCancellationRequested();
        var salt = RandomNumberGenerator.GetBytes(32);
        var header = new Header(1, Algorithm, expectedOwnerId, document.Metadata.Scope.Kind,
            document.Metadata.Scope.Id, document.Metadata.Id, document.Metadata.Revision, key.KeyId,
            now.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), Convert.ToBase64String(salt));
        var plaintext = Utf8.GetBytes(UcmlCodec.Serialize(document));
        try
        {
            var encrypted = await custody.EncryptAsync(key, header.KeyDay, salt, plaintext,
                JsonSerializer.SerializeToUtf8Bytes(header, Json), cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            return JsonSerializer.Serialize(new Envelope(header, Convert.ToBase64String(encrypted.Nonce),
                Convert.ToBase64String(encrypted.Ciphertext), Convert.ToBase64String(encrypted.Tag)), Json);
        }
        finally { CryptographicOperations.ZeroMemory(plaintext); }
    }

    public async Task<UcmlDocument> UnsealAsync(string envelopeText, string expectedOwnerId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(envelopeText);
        ArgumentNullException.ThrowIfNull(custody);
        cancellationToken.ThrowIfCancellationRequested();
        if (Utf8.GetByteCount(envelopeText) > MaximumEnvelopeBytes)
            throw new FormatException("UCML ciphertext exceeds its bounded envelope size.");
        Envelope envelope;
        try
        {
            using var parsed = JsonDocument.Parse(envelopeText, new() { MaxDepth = 8 });
            RejectDuplicateProperties(parsed.RootElement);
            envelope = JsonSerializer.Deserialize<Envelope>(envelopeText, Json)
                ?? throw new FormatException("A UCML ciphertext object is required.");
        }
        catch (JsonException error) { throw new FormatException("Invalid UCML ciphertext envelope.", error); }
        var header = envelope.Header;
        if (header is null || header.Version != 1 || header.Algorithm != Algorithm ||
            header.DocumentId == Guid.Empty || header.Revision < 1 ||
            string.IsNullOrWhiteSpace(header.ScopeKind) || header.ScopeKind.Length > 32 ||
            string.IsNullOrWhiteSpace(header.ScopeId) || header.ScopeId.Length > 256)
            throw new FormatException("Unsupported or invalid UCML ciphertext header.");
        var key = new UcmlKeyReference(header.OwnerId, header.KeyId);
        UcmlAesGcmKeyCustody.ValidateReference(key);
        if (header.OwnerId != expectedOwnerId)
            throw new UnauthorizedAccessException("The authenticated owner does not match this UCML ciphertext.");
        var salt = Decode(header.Salt, 32, 32);
        var encrypted = new UcmlCiphertext(Decode(envelope.Nonce, 12, 12),
            Decode(envelope.Ciphertext, 1, UcmlCodec.MaximumDocumentBytes), Decode(envelope.Tag, 16, 16));
        var plaintext = await custody.DecryptAsync(key, header.KeyDay, salt, encrypted,
            JsonSerializer.SerializeToUtf8Bytes(header, Json), cancellationToken).ConfigureAwait(false);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var document = UcmlCodec.Parse(Utf8.GetString(plaintext));
            if (document.Metadata.OwnerId != expectedOwnerId || document.Metadata.Id != header.DocumentId ||
                document.Metadata.Revision != header.Revision || document.Metadata.Scope.Kind != header.ScopeKind ||
                document.Metadata.Scope.Id != header.ScopeId)
                throw new CryptographicException("UCML plaintext does not match its authenticated envelope.");
            return document;
        }
        finally { CryptographicOperations.ZeroMemory(plaintext); }
    }

    private static byte[] Decode(string text, int minimum, int maximum)
    {
        if (text is null || text.Length > ((maximum + 2) / 3) * 4)
            throw new FormatException("Invalid UCML ciphertext field size.");
        var bytes = Convert.FromBase64String(text);
        if (bytes.Length < minimum || bytes.Length > maximum || Convert.ToBase64String(bytes) != text)
            throw new FormatException("Invalid UCML ciphertext field encoding.");
        return bytes;
    }
    private static void RejectDuplicateProperties(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Object) return;
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in value.EnumerateObject())
        {
            if (!names.Add(item.Name)) throw new FormatException("Duplicate UCML ciphertext field.");
            RejectDuplicateProperties(item.Value);
        }
    }
}

/// <summary>Trusted AES-256-GCM implementation using retained random root keys, HKDF-SHA256
/// daily epochs and a fresh per-envelope derivation salt. Raw keys never leave this boundary.
/// Subclasses use the already composed credential owner; they must not expose storage callbacks
/// to a model. Creating a key is explicit and always creates a NEW opaque ID, not key recovery.</summary>
public abstract class UcmlAesGcmKeyCustody : IUcmlKeyCustody
{
    protected abstract Task<byte[]?> ReadRootKeyAsync(UcmlKeyReference reference, CancellationToken cancellationToken);
    protected abstract Task WriteNewRootKeyAsync(UcmlKeyReference reference, byte[] rootKey, CancellationToken cancellationToken);

    public async Task<UcmlKeyReference> CreateKeyAsync(string authenticatedOwnerId,
        CancellationToken cancellationToken = default)
    {
        var reference = new UcmlKeyReference(authenticatedOwnerId, Guid.NewGuid());
        ValidateReference(reference);
        cancellationToken.ThrowIfCancellationRequested();
        var root = RandomNumberGenerator.GetBytes(32);
        byte[]? existing = null, verified = null;
        try
        {
            existing = await ReadRootKeyAsync(reference, cancellationToken).ConfigureAwait(false);
            if (existing is not null) throw new CryptographicException("The new UCML key identifier already exists.");
            await WriteNewRootKeyAsync(reference, root, cancellationToken).ConfigureAwait(false);
            verified = await ReadRootKeyAsync(reference, cancellationToken).ConfigureAwait(false);
            if (verified is null || !CryptographicOperations.FixedTimeEquals(root, verified))
                throw new CryptographicException("UCML key custody could not verify the newly retained key.");
            return reference;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(root);
            if (existing is not null) CryptographicOperations.ZeroMemory(existing);
            if (verified is not null) CryptographicOperations.ZeroMemory(verified);
        }
    }

    public async Task<UcmlCiphertext> EncryptAsync(UcmlKeyReference key, string day, byte[] salt,
        byte[] plaintext, byte[] associatedData, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(plaintext); ArgumentNullException.ThrowIfNull(associatedData);
        if (plaintext.Length > UcmlCodec.MaximumDocumentBytes) throw new FormatException("UCML plaintext is too large.");
        var derived = await DeriveAsync(key, day, salt, cancellationToken).ConfigureAwait(false);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var nonce = RandomNumberGenerator.GetBytes(12);
            var cipher = new byte[plaintext.Length]; var tag = new byte[16];
            using var aes = new AesGcm(derived, 16);
            aes.Encrypt(nonce, plaintext, cipher, tag, associatedData);
            return new(nonce, cipher, tag);
        }
        finally { CryptographicOperations.ZeroMemory(derived); }
    }

    public async Task<byte[]> DecryptAsync(UcmlKeyReference key, string day, byte[] salt,
        UcmlCiphertext ciphertext, byte[] associatedData, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ciphertext); ArgumentNullException.ThrowIfNull(associatedData);
        if (ciphertext.Ciphertext is null || ciphertext.Ciphertext.Length > UcmlCodec.MaximumDocumentBytes ||
            ciphertext.Nonce is not { Length: 12 } || ciphertext.Tag is not { Length: 16 })
            throw new FormatException("UCML ciphertext has invalid bounds.");
        var derived = await DeriveAsync(key, day, salt, cancellationToken).ConfigureAwait(false);
        var plaintext = new byte[ciphertext.Ciphertext.Length];
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var aes = new AesGcm(derived, 16);
            aes.Decrypt(ciphertext.Nonce, ciphertext.Ciphertext, ciphertext.Tag, plaintext, associatedData);
            return plaintext;
        }
        catch { CryptographicOperations.ZeroMemory(plaintext); throw; }
        finally { CryptographicOperations.ZeroMemory(derived); }
    }

    private async Task<byte[]> DeriveAsync(UcmlKeyReference reference, string day, byte[] salt,
        CancellationToken cancellationToken)
    {
        ValidateReference(reference);
        if (!DateOnly.TryParseExact(day, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _) || salt is not { Length: 32 })
            throw new FormatException("UCML key derivation metadata is invalid.");
        cancellationToken.ThrowIfCancellationRequested();
        var root = await ReadRootKeyAsync(reference, cancellationToken).ConfigureAwait(false);
        if (root is null) throw new CryptographicException("The retained UCML key is unavailable; explicit key recovery is required.");
        try
        {
            if (root.Length != 32) throw new CryptographicException("The retained UCML key is invalid; it will not be replaced.");
            var context = JsonSerializer.SerializeToUtf8Bytes(new[] { "9to1.ucml.envelope.v1", reference.OwnerId, reference.KeyId.ToString("D"), day });
            return HKDF.DeriveKey(HashAlgorithmName.SHA256, root, 32, salt, context);
        }
        finally { CryptographicOperations.ZeroMemory(root); }
    }

    internal static void ValidateReference(UcmlKeyReference reference)
    {
        ArgumentNullException.ThrowIfNull(reference);
        if (reference.KeyId == Guid.Empty || string.IsNullOrWhiteSpace(reference.OwnerId) ||
            reference.OwnerId.Length > 256 || reference.OwnerId.Any(char.IsControl))
            throw new FormatException("A bounded owner and nonempty UCML key identifier are required.");
    }
}
