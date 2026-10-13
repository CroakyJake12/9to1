using System.Globalization;
using Haven.Application;
using HavenOS.Home.Core;

namespace Haven.Infrastructure;

/// <summary>Observes the original durable Spaces settings root for Home's existing
/// ownership import/setup flow. Registration neither adopts existing data nor
/// grants graph publication. The native composition supplies the same instance
/// used by SpaceRegistry; a path or copied UUID cannot replace that instance.</summary>
public sealed class SpaceLocalStoreEvidenceProvider : IHomeLocalStoreEvidenceProvider
{
    private readonly VersionedAtomicSettingsStore _settings;
    public SpaceLocalStoreEvidenceProvider(VersionedAtomicSettingsStore originalSettings) =>
        _settings = originalSettings ?? throw new ArgumentNullException(nameof(originalSettings));
    public string ResourceKind => "spaces";
    public bool IsBoundToOriginalSettings(IVersionedSettingsStore settings) => ReferenceEquals(_settings, settings);

    public async ValueTask<HomeLocalStoreEvidence?> ReadAsync(string storeId, CancellationToken cancellationToken)
    {
        if (!Guid.TryParseExact(storeId, "D", out var requestedId) || requestedId == Guid.Empty) return null;
        // Read the actual durable manifest first. Do not infer accessibility from
        // metadata, reset a failed store, or create a UUID just to match a request.
        SettingsExportManifest manifest;
        try { manifest = await _settings.ExportAsync(cancellationToken).ConfigureAwait(false); }
        catch (Exception error) when (error is IOException or InvalidDataException) { return null; }
        if (manifest.StoreIdentity is not { SchemaVersion: 1 } stored || stored.StoreId != requestedId) return null;
        var identity = await _settings.GetStoreIdentityAsync(cancellationToken).ConfigureAwait(false);
        if (identity.SchemaVersion != 1 || identity.StoreId != stored.StoreId) return null;
        var current = await _settings.ExportAsync(cancellationToken).ConfigureAwait(false);
        if (current.StoreIdentity != stored || current.Version != manifest.Version) return null;
        // "Empty" means this entire owning settings root is empty. An absent
        // spaces.registry in an existing populated profile is never new-store proof.
        return new(ResourceKind, requestedId.ToString("D"), current.Version.ToString(CultureInfo.InvariantCulture),
            identity.NewlyCreated, current.Settings.Count == 0, true);
    }
}
