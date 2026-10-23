using System.Globalization;
using Haven.Core;

namespace HavenOS.Apps.Browse;

public sealed partial class BrowseNativeWorkspace
{
    private long _pendingDownloadEpoch;
    private PendingDownloadRow[] _pendingDownloadRows = [];
    private sealed record PendingDownloadSelection(BrowseNativeWorkspace Owner, long Epoch,
        BrowsePendingDownloadObservation OriginalObservation);
    public sealed record PendingDownloadRow(object Target, string Key);

    private bool TryGetPendingDownloadBinding(string path, out object? value)
    {
        if (path is not ("PendingDownloads" or "EmptyPendingDownloads" or "PendingDownloadSummary" or "DownloadReviewStatus"))
        { value = null; return false; }
        var current = _pendingDownloadRows.Where(row => IsCurrentPendingDownloadTarget(row.Target, approving: false)).ToArray();
        value = path switch
        {
            "PendingDownloads" => current,
            "EmptyPendingDownloads" => current.Length == 0,
            "PendingDownloadSummary" => current.Length == 0
                ? "No active download requests awaiting review in this profile."
                : $"{current.Length} download request{(current.Length == 1 ? string.Empty : "s")} awaiting review.",
            "DownloadReviewStatus" => _chrome.State.Downloads.ReviewStatus,
            _ => null
        };
        return value is not null;
    }

    private bool TryGetPendingDownloadItem(object item, string path, out object? value)
    {
        value = null;
        if (item is not PendingDownloadRow row || !_pendingDownloadRows.Any(current => ReferenceEquals(current, row)) ||
            row.Target is not PendingDownloadSelection selection || !IsCurrentPendingDownloadTarget(selection, approving: false)) return false;
        var observation = selection.OriginalObservation; var action = observation.Action;
        var label = string.IsNullOrWhiteSpace(action.SuggestedFileName) ? "Download" : action.SuggestedFileName;
        var context = observation.Privacy == BrowserTabPrivacy.Private ? "Private browsing" : "Current profile";
        var expiry = action.ExpiresAt <= DateTimeOffset.UtcNow
            ? "Approval expired. Reject this request and start the download again."
            : "Approval expires " + action.ExpiresAt.ToLocalTime().ToString("g", CultureInfo.CurrentCulture);
        value = path switch
        {
            "Key" => row.Key, "Target" => row.Target, "Label" => label,
            "Origin" => action.Origin, "Summary" => action.Summary, "Context" => context, "Expiry" => expiry,
            "ApproveLabel" => "Approve download " + label,
            "RejectLabel" => "Reject pending download " + label,
            "CanApprove" => IsActionAvailable("9to1.Browse.ApprovePendingDownload") == true && IsCurrentPendingDownloadTarget(row.Target, approving: true),
            "CanReject" => IsActionAvailable("9to1.Browse.RejectPendingDownload") == true,
            _ => null
        };
        return value is not null;
    }

    private void IssuePendingDownloadRows()
    {
        var current = _snapshot.Downloads.Pending;
        if (_pendingDownloadRows.Length == current.Count && _pendingDownloadRows.Zip(current).All(pair =>
            pair.First.Target is PendingDownloadSelection selection && ReferenceEquals(selection.OriginalObservation, pair.Second))) return;
        var epoch = checked(++_pendingDownloadEpoch);
        _pendingDownloadRows = current.Select(observation => new PendingDownloadRow(
            new PendingDownloadSelection(this, epoch, observation), observation.Action.Id.ToString("D"))).ToArray();
    }
    private bool IsCurrentPendingDownloadTarget(object? target, bool approving) =>
        target is PendingDownloadSelection selection && ReferenceEquals(selection.Owner, this) &&
        selection.Epoch == _pendingDownloadEpoch && _pendingDownloadRows.Any(row => ReferenceEquals(row.Target, selection)) &&
        _chrome.IsCurrentPendingDownload(selection.OriginalObservation, approving);

    private async Task DecideOriginalPendingDownloadAsync(object target, bool approve, CancellationToken token)
    {
        // Repeat the current issued-row check inside the retained command driver. A row copied
        // from another workspace/profile, a replaced queue observation or an expired approval
        // can never be turned into a service call merely by retaining its visible action ID.
        if (!IsCurrentPendingDownloadTarget(target, approve))
        { _status = "This pending download changed or expired. Refresh Downloads before deciding."; return; }
        var observation = ((PendingDownloadSelection)target).OriginalObservation;
        await Operation(() => approve ? _chrome.ApprovePendingDownloadAsync(observation, token)
            : _chrome.RejectPendingDownloadAsync(observation, token));
    }
}
