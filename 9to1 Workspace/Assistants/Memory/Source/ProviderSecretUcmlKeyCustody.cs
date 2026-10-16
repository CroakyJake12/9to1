using System.Security.Cryptography;
using System.Text;
using Haven.Application;
using NineToOne.Dulche.Context;

namespace HavenOS.Apps.Assistants.Memory;

/// <summary>Process-only UCML root key custody using the host's existing platform credential
/// owner. This grants no Home/app/context permission. Compose the real platform store; never
/// expose this adapter, its credential store, or provisioning as model-callable tools.</summary>
public sealed class ProviderSecretUcmlKeyCustody(IProviderSecretStore secrets) : UcmlAesGcmKeyCustody
{
    private const string Provider = "dulche.ucml";

    protected override async Task<byte[]?> ReadRootKeyAsync(UcmlKeyReference reference,
        CancellationToken cancellationToken)
    {
        var encoded = await secrets.GetAsync(Provider, Name(reference), cancellationToken).ConfigureAwait(false);
        if (encoded is null) return null;
        byte[] key;
        try { key = Convert.FromBase64String(encoded); }
        catch (FormatException error) { throw new CryptographicException("The retained UCML key is corrupt; explicit recovery is required.", error); }
        if (key.Length == 32) return key;
        CryptographicOperations.ZeroMemory(key);
        throw new CryptographicException("The retained UCML key has an invalid size; it will not be reset.");
    }

    protected override async Task WriteNewRootKeyAsync(UcmlKeyReference reference, byte[] rootKey,
        CancellationToken cancellationToken)
    {
        // IDs are unguessable, freshly generated inside CreateKeyAsync, never caller-chosen.
        // The platform API has no compare-and-swap; refuse an observed collision and never
        // reuse an ID for rotation or recovery. Daily rotation only derives new data keys.
        var name = Name(reference);
        if (await secrets.GetAsync(Provider, name, cancellationToken).ConfigureAwait(false) is not null)
            throw new CryptographicException("The new UCML key identifier already exists; it will not be overwritten.");
        await secrets.SetAsync(Provider, name, Convert.ToBase64String(rootKey), cancellationToken).ConfigureAwait(false);
    }

    private static string Name(UcmlKeyReference reference) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(reference.OwnerId))).ToLowerInvariant()[..32]
        + "." + reference.KeyId.ToString("N");
}
