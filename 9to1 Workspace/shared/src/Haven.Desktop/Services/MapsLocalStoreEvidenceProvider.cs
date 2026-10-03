using System.Security.Cryptography;
using System.Text.Json;
using Haven.Application;
using HavenOS.Home.Core;

namespace Haven.Desktop.Services;

/// <summary>Evidence for the actual canonical Maps settings domain, prior to default creation.
/// This does not establish ownership of a separate legacy Data-backed saved-place workbook.</summary>
public sealed class MapsLocalStoreEvidenceProvider(IVersionedSettingsStore settings,
    IResourceStoreIdentitySource identities) : IHomeLocalStoreEvidenceProvider
{
    public string ResourceKind => "maps";
    public async ValueTask<HomeLocalStoreEvidence?> ReadAsync(string storeId, CancellationToken cancellationToken)
    {
        var identity = await identities.GetStoreIdentityAsync(cancellationToken).ConfigureAwait(false);
        if (identity.SchemaVersion != 1 || identity.StoreId == Guid.Empty || storeId != identity.StoreId.ToString("D")) return null;
        // Export carries entries and durable identity from the same owning-store critical section.
        var export = await settings.ExportAsync(cancellationToken).ConfigureAwait(false);
        if (export.StoreIdentity is not { SchemaVersion: 1 } actual || actual.StoreId != identity.StoreId) return null;
        var entries = export.Settings.Where(entry => entry.Key.StartsWith("maps.", StringComparison.OrdinalIgnoreCase))
            .OrderBy(entry => entry.Key, StringComparer.Ordinal).ToArray();
        var revision = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(entries)));
        return new(ResourceKind, storeId, revision, identity.NewlyCreated, entries.Length == 0, true);
    }
}
