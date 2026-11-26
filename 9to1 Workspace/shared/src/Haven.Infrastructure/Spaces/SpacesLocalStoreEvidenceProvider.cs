using Haven.Application;
using HavenOS.Home.Core;

namespace Haven.Infrastructure;

/// <summary>Read-only Spaces evidence from the SAME configured settings owner. Inspection
/// neither initialises identity nor claims a new empty store or Home ownership.</summary>
public sealed class SpacesLocalStoreEvidenceProvider(VersionedAtomicSettingsStore original)
    : IHomeOriginalScopedLocalStoreEvidenceProvider, IHomeOriginalScopedResourceStoreIdentitySource
{
    public const string Kind = "spaces";
    private readonly VersionedAtomicSettingsStore _original = original ?? throw new ArgumentNullException(nameof(original));
    private readonly object _gate = new();
    private SettingsStoreIdentity? _observedIdentity;
    public string ResourceKind => Kind;

    public bool IsBoundToOriginalSettings(IVersionedSettingsStore same) => ReferenceEquals(_original, same);

    public Task<ExistingSettingsStoreMetadata?> InspectWithinOriginalSourceAsync(
        Action<Action> scope, Action<Task> retain, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(scope); ArgumentNullException.ThrowIfNull(retain);
        var sources = new CloudflareOriginalTaskLedger(); sources.BindOriginalOwner(this);
        sources.BindOriginalCallerCallback(scope);
        return sources.RunToOriginalSettlementAsync(async () =>
        {
            var raw = sources.Invoke(() =>
            {
                var actual = _original.InspectExistingStoreMetadataAsync(token);
                sources.Track(actual); retain(actual); return actual;
            });
            var metadata = await sources.AwaitAsync(raw).ConfigureAwait(false);
            if (metadata is not null)
                sources.Invoke(() =>
                {
                    token.ThrowIfCancellationRequested();
                    lock (_gate)
                    {
                        if (_observedIdentity is not null && _observedIdentity != metadata.StoreIdentity)
                            throw new UnauthorizedAccessException("The configured Spaces store identity changed. Preserve the original store and reopen its setup.");
                        _observedIdentity ??= metadata.StoreIdentity;
                    }
                    return true;
                });
            return metadata;
        });
    }

    public ValueTask<ResourceStoreIdentity> GetStoreIdentityAsync(CancellationToken token) =>
        GetStoreIdentityWithinOriginalSourceAsync(body => body(), _ => { }, token);

    public async ValueTask<ResourceStoreIdentity> GetStoreIdentityWithinOriginalSourceAsync(
        Action<Action> scope, Action<Task> retain, CancellationToken token)
    {
        var metadata = await InspectWithinOriginalSourceAsync(scope, retain, token).ConfigureAwait(false)
            ?? throw new InvalidDataException("The existing Spaces store has no durable identity. Its owning setup or recovery is required.");
        var identity = metadata.StoreIdentity;
        return new(identity.SchemaVersion, identity.StoreId, identity.CreatedAtUtc, NewlyCreated: false);
    }

    public ValueTask<HomeLocalStoreEvidence?> ReadAsync(string storeId, CancellationToken token) =>
        ReadWithinOriginalSourceAsync(storeId, body => body(), _ => { }, token);

    public async ValueTask<HomeLocalStoreEvidence?> ReadWithinOriginalSourceAsync(string storeId,
        Action<Action> scope, Action<Task> retain, CancellationToken token)
    {
        if (!Guid.TryParseExact(storeId, "D", out var requested) || requested == Guid.Empty ||
            storeId != requested.ToString("D")) return null;
        var metadata = await InspectWithinOriginalSourceAsync(scope, retain, token).ConfigureAwait(false);
        if (metadata is null || metadata.StoreIdentity.StoreId != requested) return null;
        // A successful strict read proves current OS access. No new-empty lifecycle is
        // inferred from file existence, path, timestamps or a UUID in the document.
        return new(Kind, storeId, metadata.Revision, NewlyCreated: false, metadata.IsEmpty,
            AccessibleToCurrentOsPrincipal: true);
    }
}
