using Haven.Application;
using Haven.Core;

namespace Haven.Infrastructure;

public sealed record MapsJourneyWorkspaceReview(string RequestID, bool OriginAvailable,
    MapsLibraryCommit? LastObservation);

/// <summary>Journey library caller over the actual Home owner. Saved-place Data
/// workbooks, legacy imports and Planner attach/start remain separate workflows.</summary>
public sealed class MapsJourneyLibraryWorkspace : IDisposable
{
    private readonly HomeMapsLibraryOwner _owner;
    private MapsLibraryDisplay _display;
    private readonly List<IMapsLibraryDisplay> _retainedDisplays = new();
    private readonly List<Entry> _reviews = new();
    private readonly object _gate = new();
    private int _closed;
    private sealed class Entry(IMapsLibraryReview review)
    {
        public IMapsLibraryReview Review { get; } = review;
        public MapsLibraryCommit? LastObservation { get; set; }
    }
    private MapsJourneyLibraryWorkspace(HomeMapsLibraryOwner owner, MapsLibraryDisplay display)
    { _owner = owner; _display = display; }
    public MapsJourneyLibrary Snapshot { get { lock (_gate) return _display.Snapshot; } }
    public bool IsClosed => Volatile.Read(ref _closed) != 0;
    public IReadOnlyList<MapsJourneyWorkspaceReview> Reviews
    {
        get
        {
            lock (_gate) return _reviews.Select(entry => new MapsJourneyWorkspaceReview(
                entry.Review.RequestID, entry.Review.OriginAvailable, entry.LastObservation)).ToArray();
        }
    }
    public static async Task<MapsJourneyLibraryWorkspace> OpenAsync(HomeMapsLibraryOwner owner,
        AuthenticatedResourceActor originalClickActor, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(originalClickActor);
        var displayed = await owner.LoadForDisplayAsync(originalClickActor, token).ConfigureAwait(false);
        return new(owner, displayed);
    }
    /// <summary>Retains a host-captured original owner display across queued UI construction.
    /// Reload verifies its original actor/root; a fresh identity is never adopted as a substitute.</summary>
    public static async Task<MapsJourneyLibraryWorkspace> OpenForOriginalDisplayAsync(HomeMapsLibraryOwner actualOwner,
        IMapsLibraryDisplay original, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(actualOwner); ArgumentNullException.ThrowIfNull(original);
        var displayed = await actualOwner.ReloadForOriginalDisplayAsync(original, token).ConfigureAwait(false);
        try
        {
            token.ThrowIfCancellationRequested();
            var workspace = new MapsJourneyLibraryWorkspace(actualOwner, displayed);
            workspace._retainedDisplays.Add(original);
            return workspace;
        }
        catch { displayed.Selection.Dispose(); throw; }
    }
    public async Task ReloadAsync(CancellationToken token = default)
    {
        ObjectDisposedException.ThrowIf(IsClosed, this);
        IMapsLibraryDisplay original;
        lock (_gate) original = _display.Selection;
        var refreshed = await _owner.ReloadForOriginalDisplayAsync(original, token).ConfigureAwait(false);
        lock (_gate)
        {
            if (IsClosed) { refreshed.Selection.Dispose(); throw new ObjectDisposedException(nameof(MapsJourneyLibraryWorkspace)); }
            // Earlier exact review contexts remain usable only under their own revision,
            // original actor/root/final admission; reload never reauthorizes a request.
            _retainedDisplays.Add(_display.Selection); _display = refreshed;
        }
    }
    public async Task<string> ReviewSaveAsync(MapSavedJourney proposed,
        long? expectedObjectRevision = null, CancellationToken token = default)
    {
        ObjectDisposedException.ThrowIf(IsClosed, this);
        // The actual owner detaches route proposals synchronously before its first await.
        // Do not put a caller's mutable proposal behind a separate caller semaphore.
        IMapsLibraryDisplay selected;
        lock (_gate) selected = _display.Selection;
        var issued = await _owner.ReviewAsync(selected, proposed,
            expectedObjectRevision, token).ConfigureAwait(false);
        // Retain an actually published request even if panel close raced its return.
        lock (_gate) _reviews.Add(new(issued));
        return issued.RequestID;
    }
    public async Task<MapsLibraryCommit> ApplyOrRecoverAsync(string requestID, CancellationToken token = default)
    {
        Entry entry;
        lock (_gate) entry = _reviews.SingleOrDefault(value => value.Review.RequestID == requestID)
            ?? throw new UnauthorizedAccessException("This Journey workspace did not issue that review.");
        // Owner independently verifies private origin before a new Begin/write. A closed
        // display cannot grant a write, while already-known outcome audit recovery remains
        // reachable. Unknown receipt observation remains constrained to the original owner.
        var observed = await _owner.CommitAsync(entry.Review, token).ConfigureAwait(false);
        lock (_gate) entry.LastObservation = observed;
        return observed;
    }
    public async Task<MapsLibraryCommit> FinishAsync(string requestID, CancellationToken token = default)
    {
        Entry entry;
        lock (_gate) entry = _reviews.SingleOrDefault(value => value.Review.RequestID == requestID)
            ?? throw new UnauthorizedAccessException("This Journey workspace did not issue that review.");
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
