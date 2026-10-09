using System.Security.Cryptography;
using System.Text.Json;
using Haven.Application;
using HavenOS.Home.Core;

namespace Haven.Infrastructure;

/// <summary>Actual protected SQLite evidence for ONE explicitly configured Home kind.
/// The shared logical identity grants no cross-kind content access. Evidence inspection
/// never seeds identity, adopts a store, reads product rows or binds ownership.</summary>
public sealed class CanonicalSqliteOriginalStoreEvidenceProvider : IHomeOriginalScopedLocalStoreEvidenceProvider
{
    private readonly CanonicalSqliteOriginalStoreOwner _store;
    public string ResourceKind { get; }
    public CanonicalSqliteOriginalStoreEvidenceProvider(CanonicalSqliteOriginalStoreOwner actualStore,
        string originalResourceKind)
    {
        ArgumentNullException.ThrowIfNull(actualStore);
        if (originalResourceKind is not ("canonical.sqlite" or "legacy.saved-agents" or "assistant.memory"))
            throw new ArgumentException("This configured owner has no evidence producer for that resource kind.", nameof(originalResourceKind));
        _store = actualStore; ResourceKind = originalResourceKind;
    }
    public bool HasOriginalComposition(CanonicalSqliteOriginalStoreOwner sameStore, string sameResourceKind) =>
        ReferenceEquals(_store, sameStore) && ResourceKind == sameResourceKind;
    public ValueTask<HomeLocalStoreEvidence?> ReadAsync(string storeId, CancellationToken token) =>
        ReadWithinOriginalSourceAsync(storeId, body => body(), _ => { }, token);
    public ValueTask<HomeLocalStoreEvidence?> ReadWithinOriginalSourceAsync(string storeId,
        Action<Action> scope, Action<Task> retain, CancellationToken token)
    {
        var source = new CanonicalSqliteOriginalSourceScope(_store.OriginalSourceOwner, scope, retain);
        return new(_store.RetainOriginalReader<HomeLocalStoreEvidence?>(source, async () =>
        {
            var actor = await source.Read(() => _store.OriginalProfiles.GetCurrentAsync(source.Run, source.Retain, token).AsTask()).ConfigureAwait(false)
                ?? throw new UnauthorizedAccessException();
            var identity = await source.Read(() => _store.GetStoreIdentityWithinOriginalSourceAsync(actor,
                source.Run, source.Retain, token)).ConfigureAwait(false);
            await source.JoinAllAsync().ConfigureAwait(false);
            if (identity.StoreId.ToString("D") != storeId) return null;
            var revision = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(identity)));
            // NewlyCreated/IsEmpty are deliberately false: this read supplies no new-empty
            // lifecycle proof. Existing stores require explicit manual Home import.
            return new(ResourceKind, storeId, revision, false, false, true);
        }));
    }
}
