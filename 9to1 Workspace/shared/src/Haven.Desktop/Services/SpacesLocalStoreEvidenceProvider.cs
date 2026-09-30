using System.Security.Cryptography;
using System.Text.Json;
using Haven.Application;
using HavenOS.Home.Core;

namespace Haven.Desktop.Services;

public sealed class SpacesLocalStoreEvidenceProvider(IVersionedSettingsStore settings,
    IResourceStoreIdentitySource identities) : IHomeLocalStoreEvidenceProvider
{
    public string ResourceKind => "spaces";
    public async ValueTask<HomeLocalStoreEvidence?> ReadAsync(string storeId, CancellationToken cancellationToken)
    {
        var identity = await identities.GetStoreIdentityAsync(cancellationToken).ConfigureAwait(false);
        if (identity.SchemaVersion != 1 || storeId != identity.StoreId.ToString("D")) return null;
        var export = await settings.ExportAsync(cancellationToken).ConfigureAwait(false);
        if (export.StoreIdentity?.StoreId != identity.StoreId || export.StoreIdentity.SchemaVersion != 1) return null;
        var entries = export.Settings.Where(entry => entry.Key.StartsWith("spaces.", StringComparison.OrdinalIgnoreCase))
            .OrderBy(entry => entry.Key, StringComparer.Ordinal).ToArray();
        var revision = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(entries)));
        return new(ResourceKind, storeId, revision, identity.NewlyCreated, entries.Length == 0, true);
    }
}
