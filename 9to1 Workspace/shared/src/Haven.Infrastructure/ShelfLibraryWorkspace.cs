using Haven.Application;
using Haven.Core;
using Haven.Core.Shelf;
using Haven.Application.Shelf;

namespace Haven.Infrastructure;

public sealed record ShelfWorkspaceReview(string RequestID, bool OriginAvailable,
    ShelfLibraryCommit? LastObservation);

/// <summary>Shelf library caller over the actual Home owner. Launch/open remains the actual target owner's responsibility; this caller never emits a platform launch.</summary>
public sealed class ShelfLibraryWorkspace : IDisposable
{
    private readonly HomeShelfLibraryOwner _owner;
    private ShelfLibraryDisplay _display;
    private readonly List<IShelfLibraryDisplay> _retainedDisplays = new();
    private readonly List<Entry> _reviews = new();
    private readonly object _gate = new();
    private int _closed;
    private sealed class Entry(IShelfLibraryReview review)
    {
        public IShelfLibraryReview Review { get; } = review;
        public ShelfLibraryCommit? LastObservation { get; set; }
    }
    private ShelfLibraryWorkspace(HomeShelfLibraryOwner owner, ShelfLibraryDisplay display)
    { _owner = owner; _display = display; }
    public ShelfSnapshot Snapshot { get { lock (_gate) return _display.Snapshot; } }
    public bool IsClosed => Volatile.Read(ref _closed) != 0;
    public IReadOnlyList<ShelfWorkspaceReview> Reviews
    {
        get
        {
            lock (_gate) return _reviews.Select(entry => new ShelfWorkspaceReview(
                entry.Review.RequestID, entry.Review.OriginAvailable, entry.LastObservation)).ToArray();
        }
    }
    public static async Task<ShelfLibraryWorkspace> OpenAsync(HomeShelfLibraryOwner owner,
        AuthenticatedResourceActor originalClickActor, CancellationToken token = default,
        Func<bool>? originalLifetime = null)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(originalClickActor);
        var displayed = await owner.LoadForDisplayAsync(originalClickActor, token, originalLifetime).ConfigureAwait(false);
        try { token.ThrowIfCancellationRequested(); return new(owner, displayed); }
        catch { displayed.Selection.Dispose(); throw; }
    }
    public static async Task<ShelfLibraryWorkspace> OpenForOriginalDisplayAsync(HomeShelfLibraryOwner owner,
        IShelfLibraryDisplay originalDisplay, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(owner); ArgumentNullException.ThrowIfNull(originalDisplay);
        var displayed = await owner.ReloadForOriginalDisplayAsync(originalDisplay, token).ConfigureAwait(false);
        try { token.ThrowIfCancellationRequested(); return new(owner, displayed); }
        catch { displayed.Selection.Dispose(); throw; }
    }
    public async Task ReloadAsync(CancellationToken token = default)
    {
        ObjectDisposedException.ThrowIf(IsClosed, this);
        IShelfLibraryDisplay original;
        lock (_gate) original = _display.Selection;
        var refreshed = await _owner.ReloadForOriginalDisplayAsync(original, token).ConfigureAwait(false);
        lock (_gate)
        {
            if (IsClosed) { refreshed.Selection.Dispose(); throw new ObjectDisposedException(nameof(ShelfLibraryWorkspace)); }
            // Earlier exact review contexts remain usable only under their own revision,
            // original actor/root/final admission; reload never reauthorizes a request.
            _retainedDisplays.Add(_display.Selection); _display = refreshed;
        }
    }
    public async Task<string> ReviewSaveAsync(ShelfLaunchItem proposed,
        long? expectedObjectRevision = null, CancellationToken token = default)
    {
        ObjectDisposedException.ThrowIf(IsClosed, this);
        // The actual owner detaches route proposals synchronously before its first await.
        // Do not put a caller's mutable proposal behind a separate caller semaphore.
        IShelfLibraryDisplay selected;
        lock (_gate) selected = _display.Selection;
        var issued = await _owner.ReviewAsync(selected, proposed,
            expectedObjectRevision, token).ConfigureAwait(false);
        // Retain an actually published request even if panel close raced its return.
        lock (_gate) _reviews.Add(new(issued));
        return issued.RequestID;
    }
    public async Task<string> ReviewEditItemAsync(Haven.Application.Shelf.ShelfItemEdit proposed, CancellationToken token = default)
    {
        ObjectDisposedException.ThrowIf(IsClosed, this);
        IShelfLibraryDisplay selected; lock (_gate) selected = _display.Selection;
        var issued = await _owner.ReviewEditItemAsync(selected, proposed, token).ConfigureAwait(false);
        lock (_gate) _reviews.Add(new(issued)); return issued.RequestID;
    }
    public async Task<string> ReviewCreateCollectionAsync(ShelfCollection proposed, CancellationToken token = default)
    {
        ObjectDisposedException.ThrowIf(IsClosed, this);
        IShelfLibraryDisplay selected; lock (_gate) selected = _display.Selection;
        var issued = await _owner.ReviewCreateCollectionAsync(selected, proposed, token).ConfigureAwait(false);
        lock (_gate) _reviews.Add(new(issued)); return issued.RequestID;
    }
    public async Task<string> ReviewCreateSmartCollectionAsync(Haven.Core.Shelf.ShelfCollection proposed, CancellationToken token = default)
    {
        ObjectDisposedException.ThrowIf(IsClosed, this);
        IShelfLibraryDisplay selected; lock (_gate) selected = _display.Selection;
        var issued = await _owner.ReviewCreateSmartCollectionAsync(selected, proposed, token).ConfigureAwait(false);
        lock (_gate) _reviews.Add(new(issued)); return issued.RequestID;
    }
    public async Task<string> ReviewAddMembershipAsync(Guid collectionID, Guid itemID, int order = 0, CancellationToken token = default)
    {
        ObjectDisposedException.ThrowIf(IsClosed, this);
        IShelfLibraryDisplay selected; lock (_gate) selected = _display.Selection;
        var issued = await _owner.ReviewAddMembershipAsync(selected, collectionID, itemID, order, token).ConfigureAwait(false);
        lock (_gate) _reviews.Add(new(issued)); return issued.RequestID;
    }
    public async Task<ShelfLibraryCommit> ApplyOrRecoverAsync(string requestID, CancellationToken token = default)
    {
        Entry entry;
        lock (_gate) entry = _reviews.SingleOrDefault(value => value.Review.RequestID == requestID)
            ?? throw new UnauthorizedAccessException("This Shelf workspace did not issue that review.");
        // Owner independently verifies private origin before a new Begin/write. A closed
        // display cannot grant a write, while already-known outcome audit recovery remains
        // reachable. Unknown receipt observation remains constrained to the original owner.
        var observed = await _owner.CommitAsync(entry.Review, token).ConfigureAwait(false);
        lock (_gate) entry.LastObservation = observed;
        return observed;
    }
    public async Task<ShelfLibraryCommit> FinishAsync(string requestID, CancellationToken token = default)
    {
        Entry entry;
        lock (_gate) entry = _reviews.SingleOrDefault(value => value.Review.RequestID == requestID)
            ?? throw new UnauthorizedAccessException("This Shelf workspace did not issue that review.");
        var observed = await _owner.FinishAsync(entry.Review, token).ConfigureAwait(false);
        lock (_gate) entry.LastObservation = observed;
        return observed;
    }
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _closed, 1) == 0)
        {
            lock (_gate)
            {
                _display.Selection.Dispose();
                foreach (var original in _retainedDisplays) original.Dispose();
            }
        }
        // Do not discard original pending IDs or infer that closing withdrew Home review.
    }
}
