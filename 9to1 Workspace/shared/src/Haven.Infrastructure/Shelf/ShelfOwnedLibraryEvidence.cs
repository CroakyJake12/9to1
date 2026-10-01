using System.Security.Cryptography;
using System.Text.Json;
using Haven.Application;
using HavenOS.Home.Core;
using HavenOS.Home.PermissionsTrustNotifications;
namespace Haven.Infrastructure;

/// <summary>Evidence from the SAME actual settings root. Register exactly one provider for this kind;
/// no implicit bind/import and no global SQL UUID alias.</summary>
public sealed class ShelfOwnedLibraryEvidence(IVersionedSettingsStore settings) : IHomeLocalStoreEvidenceProvider
{
    public string ResourceKind => "shelf";
    public async ValueTask<HomeLocalStoreEvidence?> ReadAsync(string storeID, CancellationToken token)
    {
        if (settings is not IResourceStoreIdentitySource actualIdentity) return null;
        var identity = await actualIdentity.GetStoreIdentityAsync(token).ConfigureAwait(false);
        if (identity.SchemaVersion != 1 || identity.StoreId == Guid.Empty || storeID != identity.StoreId.ToString("D")) return null;
        var export = await settings.ExportAsync(token).ConfigureAwait(false);
        if (export.StoreIdentity is not { SchemaVersion: 1 } actual || actual.StoreId != identity.StoreId) return null;
        var entries = export.Settings.Where(pair => pair.Key.StartsWith("shelf.", StringComparison.OrdinalIgnoreCase))
            .OrderBy(pair => pair.Key, StringComparer.Ordinal).ToArray();
        return new(ResourceKind, storeID, Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(entries))),
            identity.NewlyCreated, entries.Length == 0, true);
    }
}
public sealed class ShelfOwnedLibraryActionPolicies : IHomeActionPolicySource
{
    public HomePermissionActionPolicy? TryGet(string appID, string actionID) =>
        appID == "shelf" && actionID == HomeShelfLibraryOwner.ActionID
            ? new(HavenOS.Home.PermissionsTrustNotifications.HomePermissionRisk.High, true, false, true) : null;
}
