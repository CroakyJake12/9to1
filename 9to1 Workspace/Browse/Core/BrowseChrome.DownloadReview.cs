using Haven.Core;

namespace HavenOS.Apps.Browse;

/// <summary>Restrictive observation by the SAME live native engine that admitted a download.
/// Implementations compare the exact successfully queued action with their own paused native
/// request and current engine/profile lifetime. IDs/URLs/record copies never establish ownership.
/// This observation issues no approval, transfer, Home or Files authority.</summary>
public interface IBrowseOriginalPendingDownloadSource
{
    bool IsOriginalPendingDownload(BrowserPendingAction samePendingAction);
}

/// <summary>A source-issued view observation, not an action grant or persisted download manager.</summary>
public sealed class BrowsePendingDownloadObservation
{
    internal BrowsePendingDownloadObservation(BrowseChrome owner, IBrowseEngineTab originalEngine,
        Guid tabId, string profileId, BrowserTabPrivacy privacy, BrowserPendingAction action)
        => (Owner, OriginalEngine, TabId, ProfileId, Privacy, Action) = (owner, originalEngine, tabId, profileId, privacy, action);
    internal BrowseChrome Owner { get; }
    internal IBrowseEngineTab OriginalEngine { get; }
    public Guid TabId { get; }
    public string ProfileId { get; }
    public BrowserTabPrivacy Privacy { get; }
    public BrowserPendingAction Action { get; }
}

public sealed partial class BrowseChrome
{
    private IReadOnlyList<BrowsePendingDownloadObservation> _pendingDownloads = [];
    private string _downloadReviewStatus = string.Empty;
    private string? _downloadReviewProfile;
    private BrowserTabPrivacy _downloadReviewPrivacy;

    public Task<BrowseChromeSnapshot> ApprovePendingDownloadAsync(BrowsePendingDownloadObservation observation,
        CancellationToken token = default) =>
        RetainContentTransition(() => DecidePendingDownloadCoreAsync(observation, approve: true, token), token, blocksRetirement: true);
    public Task<BrowseChromeSnapshot> RejectPendingDownloadAsync(BrowsePendingDownloadObservation observation,
        CancellationToken token = default) =>
        RetainContentTransition(() => DecidePendingDownloadCoreAsync(observation, approve: false, token), token, blocksRetirement: true);

    /// <summary>Exact current source/profile/action observation. Expired requests may still be
    /// rejected to discard their paused native transfer; they can never be approved here.</summary>
    public bool IsCurrentPendingDownload(BrowsePendingDownloadObservation? observation, bool approving)
    {
        if (_nativeRetiring || _disposed || observation is null || !ReferenceEquals(observation.Owner, this) ||
            !_pendingDownloads.Any(value => ReferenceEquals(value, observation))) return false;
        var selected = SelectedRuntime();
        if (!StringComparer.Ordinal.Equals(selected.ProfileId, observation.ProfileId) || selected.Privacy != observation.Privacy ||
            observation.Action.Kind != BrowserActionKind.Download || observation.Action.State != BrowserActionState.Pending ||
            approving && observation.Action.ExpiresAt <= DateTimeOffset.UtcNow) return false;
        var tab = _tabs.SingleOrDefault(value => value.Id == observation.TabId);
        return tab is not null && tab.ProfileId == observation.ProfileId && tab.Privacy == observation.Privacy &&
            tab.EngineState == BrowseEngineState.Ready && ReferenceEquals(tab.Host, observation.OriginalEngine) &&
            tab.Host is IBrowseOriginalPendingDownloadSource source && source.IsOriginalPendingDownload(observation.Action);
    }

    private IReadOnlyList<BrowsePendingDownloadObservation> ObserveCurrentPendingDownloads() =>
        Array.AsReadOnly(_pendingDownloads.Where(value => IsCurrentPendingDownload(value, approving: false)).ToArray());
    private string CurrentDownloadReviewStatus()
    {
        var selected = SelectedRuntime();
        return selected.ProfileId == _downloadReviewProfile && selected.Privacy == _downloadReviewPrivacy
            ? _downloadReviewStatus : string.Empty;
    }
    private void SetDownloadReviewStatus(string message, string profileId, BrowserTabPrivacy privacy)
    { _downloadReviewStatus = message; _downloadReviewProfile = profileId; _downloadReviewPrivacy = privacy; }

    private IReadOnlyList<BrowsePendingDownloadObservation> SelectOwnedPendingDownloads(IReadOnlyList<BrowserPendingAction> queue)
    {
        // Filter BEFORE projecting any shared/private queue metadata. Current canonical profile
        // placement restricts the search, and the actual native issuer still must recognise the
        // complete action. An absent source never falls back to origin, URL or action ID.
        var selected = SelectedRuntime();
        var engines = _tabs.Where(tab => tab.ProfileId == selected.ProfileId && tab.Privacy == selected.Privacy &&
            tab.EngineState == BrowseEngineState.Ready && tab.Host is IBrowseOriginalPendingDownloadSource).ToArray();
        var owned = new List<BrowsePendingDownloadObservation>();
        var occurrences = queue.GroupBy(value => value.Id).ToDictionary(group => group.Key, group => group.Count());
        foreach (var action in queue)
        {
            if (action.Id == Guid.Empty || action.Kind != BrowserActionKind.Download || action.State != BrowserActionState.Pending ||
                occurrences[action.Id] != 1) continue;
            var matches = engines.Where(tab => ((IBrowseOriginalPendingDownloadSource)tab.Host!).IsOriginalPendingDownload(action)).ToArray();
            if (matches.Length != 1) continue;
            var same = matches[0];
            owned.Add(new(this, same.Host!, same.Id, same.ProfileId, same.Privacy, action));
        }
        return Array.AsReadOnly(owned.OrderBy(value => value.Action.CreatedAt).ToArray());
    }

    private async Task RefreshDownloadDataAsync(CancellationToken token)
    {
        var automation = _automation ?? throw new NotSupportedException("The canonical browser download service is unavailable.");
        var pending = automation.GetPendingAsync(token) ?? throw new InvalidOperationException("The browser supplied no original pending-query task.");
        RetainAppSource(pending);
        var queue = await pending.ConfigureAwait(false);
        var completed = automation.GetDownloadsAsync(500, token) ?? throw new InvalidOperationException("The browser supplied no original completed-query task.");
        RetainAppSource(completed);
        var records = await completed.ConfigureAwait(false);
        ThrowIfCannotAccept(); token.ThrowIfCancellationRequested();
        _pendingDownloads = SelectOwnedPendingDownloads(queue);
        _downloads = Array.AsReadOnly(records.ToArray());
    }

    private async Task<BrowseChromeSnapshot> RefreshDownloadsCoreAsync(CancellationToken token)
    {
        ThrowIfCannotAccept(); token.ThrowIfCancellationRequested();
        if (_automation is null)
        { _status = "Downloads are unavailable because no canonical download service is registered."; return Publish(); }
        await RefreshDownloadDataAsync(token).ConfigureAwait(false);
        var pending = ObserveCurrentPendingDownloads().Count;
        _status = $"{pending} download request{(pending == 1 ? string.Empty : "s")} awaiting review in this profile; {_downloads.Count} completed.";
        return Publish();
    }

    private async Task<BrowseChromeSnapshot> DecidePendingDownloadCoreAsync(BrowsePendingDownloadObservation observation,
        bool approve, CancellationToken token)
    {
        ThrowIfCannotAccept(); token.ThrowIfCancellationRequested();
        if (!IsCurrentPendingDownload(observation, approve))
        { _status = "This download request changed, expired or belongs to a different browser context. Refresh Downloads."; return Publish(); }
        var automation = _automation ?? throw new NotSupportedException("The canonical download service is unavailable.");
        var pending = automation.GetPendingAsync(token) ?? throw new InvalidOperationException("The browser supplied no original pending-query task.");
        RetainAppSource(pending);
        var queue = await pending.ConfigureAwait(false);
        ThrowIfCannotAccept(); token.ThrowIfCancellationRequested();
        var current = queue.Where(value => value.Id == observation.Action.Id).Take(2).ToArray();
        if (current.Length != 1 || current[0] != observation.Action || !IsCurrentPendingDownload(observation, approve))
        {
            _pendingDownloads = SelectOwnedPendingDownloads(queue);
            _status = "This download request changed or expired. Review its current row before deciding.";
            SetDownloadReviewStatus(_status, observation.ProfileId, observation.Privacy); return Publish();
        }
        SetDownloadReviewStatus(approve ? "Processing this download approval…" : "Rejecting this pending download…", observation.ProfileId, observation.Privacy);
        Publish();
        // Existing Browser approval/policy/native transport owns execution; no direct network or
        // disk write is introduced. Files registration retains its separate real Home review.
        var actual = approve ? automation.ApproveAsync(current[0].Id, token) : automation.RejectAsync(current[0].Id, token);
        if (actual is null) throw new InvalidOperationException("The canonical download decision returned no original task.");
        RetainAppSource(actual);
        var result = await actual.ConfigureAwait(false);
        if (result.ActionId != observation.Action.Id) throw new InvalidOperationException("The download owner returned a result for another request.");
        SetDownloadReviewStatus(result.State + " · " + result.Message, observation.ProfileId, observation.Privacy);
        // A failed list refresh must not erase the actual settled decision/result from its owner.
        _status = CurrentDownloadReviewStatus();
        await RefreshDownloadDataAsync(token).ConfigureAwait(false);
        _status = CurrentDownloadReviewStatus(); return Publish();
    }
}
