using Haven.Application;
using HavenOS.Home.Core;

namespace Haven.Infrastructure;

/// <summary>Metadata evidence from the exact canonical Notes owner. It neither initialises
/// identity nor binds Home ownership or exposes document content.</summary>
public sealed class NotesLocalStoreEvidenceProvider(INotesOriginalStoreSource original) : IHomeLocalStoreEvidenceProvider
{
    private readonly INotesOriginalStoreSource _original = original ?? throw new ArgumentNullException(nameof(original));
    public const string Kind = "notes";
    public string ResourceKind => Kind;

    public async ValueTask<HomeLocalStoreEvidence?> ReadAsync(string storeId, CancellationToken cancellationToken)
    {
        if (!Guid.TryParseExact(storeId, "D", out var requested) || requested == Guid.Empty ||
            storeId != requested.ToString("D")) return null;
        var observed = await _original.ObserveOriginalStoreAsync(cancellationToken).ConfigureAwait(false);
        if (!_original.IsIssuedOriginalStoreObservation(observed) || observed.StoreIdentity.SchemaVersion != 1 ||
            observed.StoreIdentity.StoreId != requested ||
            !await _original.IsOriginalStoreObservationCurrentAsync(observed, cancellationToken).ConfigureAwait(false)) return null;
        return new(ResourceKind, storeId, observed.Revision, observed.StoreIdentity.NewlyCreated,
            observed.IsEmpty, AccessibleToCurrentOsPrincipal: true);
    }
}
