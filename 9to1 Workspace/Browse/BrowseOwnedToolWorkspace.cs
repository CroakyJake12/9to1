using System.Text.Json;
using Haven.Application;

namespace HavenOS.Apps.Browse;

public sealed record BrowseOwnedToolReviewState(string RequestID, bool OriginAvailable,
    WebMcpPreparedReviewState SubmissionState, BrowseWebMcpOwnerResult? LastObservation);

/// <summary>Plain Browse action coordinator for one actually displayed tool. Pending
/// requests survive disconnect for explicit Home review/decline and audit recovery.
/// Request IDs and view state never confer authority.</summary>
public sealed class BrowseOwnedToolWorkspace : IDisposable
{
    private readonly BrowseOwnedWebMcpBinding _owner;
    private readonly IBrowseWebMcpDisplay _display;
    private readonly SemaphoreSlim _actions = new(1, 1);
    private readonly List<Entry> _reviews = new();
    private readonly object _stateLock = new();
    private int _disposed;
    private sealed class Entry(IBrowseWebMcpReview review)
    {
        public IBrowseWebMcpReview Review { get; } = review;
        public BrowseWebMcpOwnerResult? Observation { get; set; }
    }
    private BrowseOwnedToolWorkspace(BrowseOwnedWebMcpBinding owner, IBrowseWebMcpDisplay display)
    { _owner = owner; _display = display; }

    public static async Task<BrowseOwnedToolWorkspace> OpenAsync(BrowseOwnedWebMcpBinding owner,
        AuthenticatedResourceActor originalClickActor, WebMcpDocument actualDisplayedDocument,
        WebMcpTool actualDisplayedTool, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(originalClickActor);
        var display = await owner.OpenDisplayedToolAsync(originalClickActor, actualDisplayedDocument,
            actualDisplayedTool, token).ConfigureAwait(false);
        return new(owner, display);
    }
    public static async Task<BrowseOwnedToolWorkspace> OpenAsync(BrowseOwnedWebMcpBinding owner,
        IBrowseOwnedDocumentCatalogue originalCatalogue, string selectedToolName, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(owner);
        var displayed = await owner.OpenDisplayedToolAsync(originalCatalogue, selectedToolName, token).ConfigureAwait(false);
        return new(owner, displayed);
    }
    public IReadOnlyList<BrowseOwnedToolReviewState> Reviews
    {
        get
        {
            lock (_stateLock) return _reviews.Select(entry => new BrowseOwnedToolReviewState(
                entry.Review.RequestID, entry.Review.OriginAvailable,
                entry.Review.SubmissionState, entry.Observation)).ToArray();
        }
    }
    public async Task<string> ReviewAsync(JsonElement arguments, CancellationToken token = default)
    {
        // Owner captures scopes and JSON before its first await. Do not queue a mutable
        // proposal behind other actions here: invoke owner directly while display is current.
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        var reviewed = await _owner.ReviewAsync(_display, arguments, [_display.Scope], token).ConfigureAwait(false);
        // A delivered request remains reachable even when disconnect occurred during
        // its publication. Disposal must not discard the only original request handle.
        lock (_stateLock) _reviews.Add(new(reviewed));
        return reviewed.RequestID;
    }
    public Task<BrowseWebMcpOwnerResult> RunOrObserveAsync(string requestID, CancellationToken token = default)
        => ActAsync(requestID, auditOnly: false, token);
    public Task<BrowseWebMcpOwnerResult> FinishAuditAsync(string requestID, CancellationToken token = default)
        => ActAsync(requestID, auditOnly: true, token);
    private async Task<BrowseWebMcpOwnerResult> ActAsync(string requestID, bool auditOnly, CancellationToken token)
    {
        await _actions.WaitAsync(token).ConfigureAwait(false);
        try
        {
            Entry entry;
            lock (_stateLock) entry = _reviews.SingleOrDefault(value => value.Review.RequestID == requestID)
                ?? throw new UnauthorizedAccessException("This workspace did not issue that Home review.");
            if (!auditOnly) ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            var result = auditOnly
                ? await _owner.FinishAuditAsync(entry.Review, token).ConfigureAwait(false)
                : await _owner.CommitOrObserveAsync(entry.Review, token).ConfigureAwait(false);
            lock (_stateLock) entry.Observation = result;
            return result;
        }
        finally { _actions.Release(); }
    }
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0) _display.Dispose();
        // Retain issued reviews and action gate. Audit-only recovery may still be used;
        // close never manufactures cancellation, completion, or a new approval.
    }
}
