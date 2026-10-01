using System.Text;
using System.Text.Json;
using Android.Content;
using Android.Security.Keystore;
using Haven.Application;
using Java.Security;
using Java.Interop;
using Javax.Crypto;
using Javax.Crypto.Interfaces;
using Javax.Crypto.Spec;

namespace Haven.Android;

/// <summary>
/// Encrypts small credential values with a non-exportable Android Keystore AES-GCM key.
/// Only authenticated ciphertext and its random IV are stored in private app preferences.
/// </summary>
public sealed class AndroidEncryptedPreferenceStore
{
    private static readonly object KeyGate = new();
    private const string KeyAlias = "haven.credentials.aes-gcm.v1";
    private const string PreferenceName = "haven.encrypted.credentials.v1";

    public Task SetAsync(string key, string value, CancellationToken cancellationToken) => Task.Run(() =>
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            using var cipher = Cipher.GetInstance("AES/GCM/NoPadding")
                               ?? throw new InvalidOperationException("AES-GCM is not available on this Android device.");
            cipher.Init(CipherMode.EncryptMode, GetOrCreateKey());
            var encrypted = cipher.DoFinal(Encoding.UTF8.GetBytes(value))
                            ?? throw new InvalidOperationException("Android Keystore returned no ciphertext.");
            var iv = cipher.GetIV() ?? throw new InvalidOperationException("Android Keystore returned no initialization vector.");
            var encoded = Convert.ToBase64String(iv) + ":" + Convert.ToBase64String(encrypted);
            using var editor = Preferences.Edit() ?? throw new InvalidOperationException("Android secure preferences are unavailable.");
            var pending = editor.PutString(key, encoded) ?? throw new InvalidOperationException("Android secure preferences rejected the credential value.");
            if (!pending.Commit())
                throw new IOException("Android could not persist the encrypted credential.");
        }
        catch (Exception exception) when (exception is Java.Security.GeneralSecurityException or Java.IO.IOException or Java.Lang.SecurityException)
        {
            throw new AndroidCredentialRecoveryRequiredException("CredentialWriteUnavailable", exception);
        }
    }, cancellationToken);

    public Task<string?> GetAsync(string key, CancellationToken cancellationToken) => Task.Run(() =>
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var encoded = Preferences.GetString(key, null);
            if (encoded is null) return null;
            if (string.IsNullOrWhiteSpace(encoded))
                throw new FormatException("A saved encrypted credential is empty or malformed.");
            var separator = encoded.IndexOf(':');
            if (separator <= 0 || separator == encoded.Length - 1)
                throw new FormatException("The encrypted credential payload is malformed.");
            var iv = Convert.FromBase64String(encoded[..separator]);
            var encrypted = Convert.FromBase64String(encoded[(separator + 1)..]);
            if (iv.Length != 12 || encrypted.Length < 16)
                throw new FormatException("The encrypted credential payload has invalid AES-GCM parameters.");
            using var cipher = Cipher.GetInstance("AES/GCM/NoPadding")
                               ?? throw new InvalidOperationException("AES-GCM is not available on this Android device.");
            using var parameters = new GCMParameterSpec(128, iv);
            // Reading saved ciphertext must never generate or replace its encryption key.
            cipher.Init(CipherMode.DecryptMode, GetExistingKey(), parameters);
            var plaintext = cipher.DoFinal(encrypted)
                            ?? throw new InvalidOperationException("Android Keystore returned no plaintext.");
            return Encoding.UTF8.GetString(plaintext);
        }
        catch (AEADBadTagException exception)
        {
            throw new AndroidCredentialRecoveryRequiredException("CredentialAuthenticationFailed", exception);
        }
        catch (Exception exception) when (exception is Java.Security.GeneralSecurityException or Java.IO.IOException or Java.Lang.ClassCastException or Java.Lang.SecurityException or InvalidCastException or FormatException)
        {
            throw new AndroidCredentialRecoveryRequiredException("CredentialUnreadable", exception);
        }
    }, cancellationToken);

    public Task DeleteAsync(string key, CancellationToken cancellationToken) => Task.Run(() =>
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var editor = Preferences.Edit() ?? throw new InvalidOperationException("Android secure preferences are unavailable.");
        var pending = editor.Remove(key) ?? throw new InvalidOperationException("Android secure preferences rejected the credential deletion.");
        if (!pending.Commit())
            throw new IOException("Android could not delete the encrypted credential.");
    }, cancellationToken);

    private static ISharedPreferences Preferences =>
        global::Android.App.Application.Context.GetSharedPreferences(PreferenceName, FileCreationMode.Private)
        ?? throw new InvalidOperationException("Android secure preferences are unavailable.");

    private static ISecretKey GetExistingKey()
    {
        lock (KeyGate)
        {
            using var keyStore = OpenKeyStore();
            return ReadSecretKey(keyStore);
        }
    }

    private static KeyStore OpenKeyStore()
    {
        var keyStore = KeyStore.GetInstance("AndroidKeyStore")
                       ?? throw new InvalidOperationException("Android Keystore is unavailable.");
        try { keyStore.Load(null); return keyStore; }
        catch (Java.IO.IOException exception)
        {
            keyStore.Dispose();
            throw new AndroidCredentialRecoveryRequiredException("CredentialKeyStoreUnavailable", exception);
        }
        catch { keyStore.Dispose(); throw; }
    }

    private static ISecretKey ReadSecretKey(KeyStore keyStore)
    {
        var key = keyStore.GetKey(KeyAlias, null)
                  ?? throw new AndroidCredentialRecoveryRequiredException("CredentialKeyUnavailable");
        // GetKey is bound as IKey. A hidden Java SecretKey can be wrapped as IKeyInvoker;
        // CLR 'is ISecretKey' is not Java interface identity and must not trigger key replacement.
        try
        {
            var secret = key.JavaCast<ISecretKey>()
                         ?? throw new AndroidCredentialRecoveryRequiredException("CredentialKeyUnavailable");
            if (!string.Equals(secret.Algorithm, KeyProperties.KeyAlgorithmAes, StringComparison.Ordinal))
                throw new AndroidCredentialRecoveryRequiredException("CredentialKeyTypeMismatch");
            return secret;
        }
        catch (InvalidCastException exception)
        {
            throw new AndroidCredentialRecoveryRequiredException("CredentialKeyTypeMismatch", exception);
        }
    }

    private static ISecretKey GetOrCreateKey()
    {
        lock (KeyGate)
        {
            using var keyStore = OpenKeyStore();
            if (keyStore.ContainsAlias(KeyAlias)) return ReadSecretKey(keyStore);
            var savedValues = Preferences.All
                              ?? throw new AndroidCredentialRecoveryRequiredException("CredentialStoreUnavailable");
            if (savedValues.Count > 0)
                throw new AndroidCredentialRecoveryRequiredException("CredentialKeyUnavailable");
            // Only a genuinely empty credential store may create its first key. All instances
            // serialize alias admission so concurrent first writes cannot overwrite one another.
            using var generator = KeyGenerator.GetInstance(KeyProperties.KeyAlgorithmAes, "AndroidKeyStore")
                                  ?? throw new InvalidOperationException("Android Keystore AES key generation is unavailable.");
            using var builder = new KeyGenParameterSpec.Builder(KeyAlias, KeyStorePurpose.Encrypt | KeyStorePurpose.Decrypt);
            using var specification = builder
                .SetKeySize(256)
                .SetBlockModes(KeyProperties.BlockModeGcm)
                .SetEncryptionPaddings(KeyProperties.EncryptionPaddingNone)
                .SetRandomizedEncryptionRequired(true)
                .Build();
            generator.Init(specification);
            return generator.GenerateKey()
                   ?? throw new InvalidOperationException("Android Keystore did not create an AES key.");
        }
    }

}

/// <summary>Saved secret bytes remain intact; callers must treat this credential as unavailable until explicit recovery.</summary>
public sealed class AndroidCredentialRecoveryRequiredException(string code, Exception? innerException = null)
    : IOException("Saved Android credentials could not be authenticated. Their encrypted data was preserved; reconnect or recover the affected connection explicitly.", innerException)
{
    public string Code { get; } = code;
}

public sealed class AndroidProviderSecretStore(AndroidEncryptedPreferenceStore store) : IProviderSecretStore
{
    public Task SetAsync(string providerId, string secretName, string secret, CancellationToken cancellationToken) =>
        store.SetAsync(ProviderKey(providerId, secretName), secret, cancellationToken);
    public Task<string?> GetAsync(string providerId, string secretName, CancellationToken cancellationToken) =>
        store.GetAsync(ProviderKey(providerId, secretName), cancellationToken);
    public Task DeleteAsync(string providerId, string secretName, CancellationToken cancellationToken) =>
        store.DeleteAsync(ProviderKey(providerId, secretName), cancellationToken);

    private static string ProviderKey(string providerId, string secretName) =>
        "provider:" + Uri.EscapeDataString(providerId) + ":" + Uri.EscapeDataString(secretName);
}

public sealed class AndroidCalendarTokenStore(AndroidEncryptedPreferenceStore store) : ICalendarTokenStore
{
    public Task SaveAsync(Guid accountId, CalendarTokenEnvelope token, CancellationToken cancellationToken) =>
        store.SetAsync(TokenKey(accountId), JsonSerializer.Serialize(token), cancellationToken);

    public async Task<CalendarTokenEnvelope?> GetAsync(Guid accountId, CancellationToken cancellationToken)
    {
        var payload = await store.GetAsync(TokenKey(accountId), cancellationToken).ConfigureAwait(false);
        return string.IsNullOrWhiteSpace(payload) ? null : JsonSerializer.Deserialize<CalendarTokenEnvelope>(payload);
    }

    public Task DeleteAsync(Guid accountId, CancellationToken cancellationToken) =>
        store.DeleteAsync(TokenKey(accountId), cancellationToken);

    private static string TokenKey(Guid accountId) => "calendar:" + accountId.ToString("N");
}
