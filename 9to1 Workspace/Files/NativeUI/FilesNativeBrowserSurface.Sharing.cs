using Avalonia.Threading;
using CakeOS.Cui.Runtime;
using HavenOS.Files;
using HavenOS.Files.NativeHost;
using HavenOS.Home.PermissionsTrustNotifications;

namespace HavenOS.Files.NativeUI;

public sealed partial class FilesNativeBrowserSurface : ICuiRepeatItemBindingContext
{
    private FilesNativeBrowserSharePage? _sharingPage;
    private long _sharingSelectionVersion;
    private bool IsPendingShareReview => _pendingMutation is { } pending && _browser.IsNativeShareMutation(pending);
    private bool SharingVisible => _sharingPage is { } shares && ReferenceEquals(_page, shares.Page)
        && ReferenceEquals(_items.SelectedItem, shares.Item) && OriginalAlive();
    private static bool IsSharingCommand(string command) => command is "9to1.Files.Share.Get"
        or "9to1.Files.Share.More" or "9to1.Files.Share.Close" or "9to1.Files.Share.Revoke" or "9to1.Files.Share.ChangeRole";

    private bool TryGetSharingValue(string path, out object? value)
    {
        value = path switch
        {
            "CanReviewAccess" => SharingActionAvailable("9to1.Files.Share.Get"),
            "SharingVisible" => SharingVisible,
            "SharingTitle" => SharingVisible ? $"Direct access to ‘{_sharingPage!.Item.Name}’" : "",
            "SharingGrants" => SharingVisible ? _sharingPage!.Grants : Array.Empty<FilesEmailShare>(),
            "SharingEmpty" => SharingVisible && _sharingPage!.Grants.Count == 0,
            "CanReadMoreGrants" => SharingActionAvailable("9to1.Files.Share.More"),
            "CanCloseAccess" => SharingActionAvailable("9to1.Files.Share.Close"),
            "PendingChange" when IsPendingShareReview => _browser.DescribeNativeShareMutation(_pendingMutation!),
            _ => null
        };
        return path is "CanReviewAccess" or "SharingVisible" or "SharingTitle" or "SharingGrants"
            or "SharingEmpty" or "CanReadMoreGrants" or "CanCloseAccess" || path == "PendingChange" && IsPendingShareReview;
    }

    public bool TryGetItemValue(object item, string path, out object? value)
    {
        if (TryGetFolderTabItemValue(item, path, out value)) return true;
        value = null;
        if (!SharingVisible || item is not FilesEmailShare grant
            || !_sharingPage!.Grants.Any(row => ReferenceEquals(row, grant))) return false;
        value = path switch
        {
            "id" => grant.Share.Id.ToString(),
            "email" => grant.RecipientEmail,
            "role" => grant.Share.Role.ToString(),
            "expiry" => grant.Share.ExpiresAt is { } expiry ? $"Expires {expiry.ToLocalTime():g}" : "No expiry",
            "state" => grant.Share.IsRevoked ? "Revoked" : grant.Share.ExpiresAt is { } expires && expires <= DateTimeOffset.UtcNow
                ? "Expired" : "Active",
            "CanRevoke" => SharingActionAvailable("9to1.Files.Share.Revoke") && !grant.Share.IsRevoked
                && grant.Share.Role is FilesAccessRole.Viewer or FilesAccessRole.Editor,
            "CanChangeRole" => SharingActionAvailable("9to1.Files.Share.ChangeRole") && !grant.Share.IsRevoked
                && grant.Share.Role is FilesAccessRole.Viewer or FilesAccessRole.Editor
                && (grant.Share.ExpiresAt is null || grant.Share.ExpiresAt.Value > DateTimeOffset.UtcNow),
            "changeRoleLabel" => grant.Share.Role == FilesAccessRole.Viewer ? "Make editor…" : "Make viewer…",
            _ => null
        };
        return path is "id" or "email" or "role" or "expiry" or "state" or "CanRevoke" or "CanChangeRole" or "changeRoleLabel";
    }
    public bool TrySetItemValue(object item, string path, object? value) => false;

    private bool SharingActionAvailable(string command) => command switch
    {
        "9to1.Files.Share.Get" => Available && _pendingMutation is null && _page is { } page
            && _items.SelectedItem is HostedItemMetadata item && _browser.CanReviewNativeShares(page, item),
        "9to1.Files.Share.More" => Available && _pendingMutation is null && SharingVisible && _sharingPage!.Next is not null,
        "9to1.Files.Share.Close" => Available && _pendingMutation is null && SharingVisible,
        "9to1.Files.Share.Revoke" or "9to1.Files.Share.ChangeRole" => Available && _pendingMutation is null && SharingVisible && _browser.HasNativeMutationOwner,
        _ => false
    };

    private void SharingSelectionChanged()
    {
        Interlocked.Increment(ref _sharingSelectionVersion);
        if (_sharingPage is { } shares && (!ReferenceEquals(_page, shares.Page) || !ReferenceEquals(_items.SelectedItem, shares.Item)))
            _sharingPage = null;
    }

    private async Task InvokeSharingOriginalAsync(string command, object? parameter, CancellationToken token)
    {
        Dispatcher.UIThread.VerifyAccess();
        if (IsActionAvailable(command) != true) return;
        var originalPage = _page;
        var originalItem = _items.SelectedItem as HostedItemMetadata;
        var originalShares = _sharingPage;
        var originalReview = _pendingMutation;
        var originalSelectionVersion = Volatile.Read(ref _sharingSelectionVersion);
        var grant = parameter as FilesEmailShare;
        if ((command is "9to1.Files.Share.Revoke" or "9to1.Files.Share.ChangeRole") && (grant is null || originalShares is null
            || !originalShares.Grants.Any(row => ReferenceEquals(row, grant))))
            throw new ArgumentException("Choose a grant from the original displayed access page.");
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, _lifetime.Token);
        await _operations.WaitAsync(linked.Token);
        Exception? handled = null;
        List<Exception> failures = [];
        // The provider/fence invokes this on background threads: never read Avalonia controls there.
        bool OwnerCurrent() => OriginalAlive() && Volatile.Read(ref _sharingSelectionVersion) == originalSelectionVersion;
        bool OwnsSelection() => OwnerCurrent() && ReferenceEquals(_page, originalPage)
            && ReferenceEquals(_items.SelectedItem, originalItem);
        void RequireOriginalSelection()
        {
            RequireOriginalAlive(linked.Token);
            if (!OwnsSelection() || !ReferenceEquals(_sharingPage, originalShares) || !ReferenceEquals(_pendingMutation, originalReview))
                throw new InvalidOperationException("The original Files access selection changed.");
        }
        try
        {
            if (IsActionAvailable(command) != true) return;
            RequireOriginalSelection();
            _busy = true; Changed(); RequireOriginalAlive(linked.Token);
            await RequireReadyAsync(linked.Token);
            RequireOriginalSelection();
            if (command == "9to1.Files.Share.Close") { _sharingPage = null; return; }
            if (command is "9to1.Files.Share.Revoke" or "9to1.Files.Share.ChangeRole")
            {
                var prepared = command == "9to1.Files.Share.Revoke"
                    ? await _browser.PrepareNativeShareRevocationAsync(originalShares!, grant!, _originalActor,
                        OwnerCurrent, _mutationSession, linked.Token)
                    : await _browser.PrepareNativeShareRoleChangeAsync(originalShares!, grant!,
                        grant!.Share.Role == FilesAccessRole.Viewer ? FilesAccessRole.Editor : FilesAccessRole.Viewer,
                        _originalActor, OwnerCurrent, _mutationSession, linked.Token);
                RequireOriginalSelection();
                _pendingMutation = prepared;
                _status = prepared.Approval.IsAllowed ? command == "9to1.Files.Share.Revoke"
                    ? "Ready to revoke this access grant. Choose Apply." : "Ready to change this recipient’s role. Choose Apply."
                    : prepared.Approval.State == HomePermissionRequestState.PendingApproval
                        ? "Review this access change in Home, then choose Apply." : prepared.Approval.Message;
                return;
            }
            if (command == "9to1.Files.Apply")
            {
                var outcome = await _browser.ApplyNativeShareMutationAsync(originalReview!, linked.Token);
                // A provider ACK is not permission to publish into a replaced Home/profile.
                // This retained callback checks Home/configuration only, not the now-advanced Files revision.
                await RequireReadyAsync(linked.Token);
                var publicationOwner = _originalPageCurrent ?? throw new UnauthorizedAccessException("The original Files owner observation retired.");
                await RequireOriginalOwnerAsync(publicationOwner, linked.Token);
                RequireOriginalSelection();
                _status = outcome.Message;
                if (outcome.AwaitingHomeReview) return;
                if (outcome.AuditRecorded) _pendingMutation = null;
                // Permission publication advances the item revision. Withdraw the old grant rows,
                // then reload canonical metadata; never keep stale permission-change buttons enabled.
                _sharingPage = null;
                var destination = _history[_historyIndex];
                var page = await _browser.ListAsync(_originalActor, destination.ID, _query, token: linked.Token,
                    expectedStoreId: _boundStoreId, isTrash: destination.IsTrash);
                await _browser.RevalidateAsync(page, _originalActor, linked.Token);
                var current = await _browser.CaptureOriginalPageReadCheckAsync(page, _originalActor, OriginalAlive, linked.Token);
                await RequireReadyAsync(linked.Token);
                await RequireOriginalOwnerAsync(current, linked.Token);
                RequireOriginalAlive(linked.Token);
                if (!OwnsSelection()) throw new InvalidOperationException("The original access selection changed during refresh.");
                _page = page; _originalPageCurrent = current; _items.SelectedItem = null;
                ApplyDisplayedNameSort();
                _status = outcome.Message;
                return;
            }
            var shares = await _browser.ReadNativeSharesAsync(originalPage!, originalItem!, _originalActor,
                OwnerCurrent, command == "9to1.Files.Share.More" ? originalShares : null, linked.Token);
            await RequireReadyAsync(linked.Token);
            var owner = _originalPageCurrent ?? throw new UnauthorizedAccessException("The original Files owner observation retired.");
            await RequireOriginalOwnerAsync(owner, linked.Token);
            RequireOriginalSelection();
            _sharingPage = shares;
            _status = shares.Grants.Count == 0 ? "No direct grants on this item. Parent folder access may still apply."
                : $"{shares.Grants.Count} direct grants on this access page. Parent folder access may also apply.";
        }
        catch (Exception error)
        {
            lock (_originalTasksSync) AddOriginalFailure(error);
            var terminal = error is UnauthorizedAccessException or ObjectDisposedException || !OriginalAlive()
                || error is OperationCanceledException && _lifetime.IsCancellationRequested;
            if (terminal) { lock (_originalTasksSync) _closing = true; Add(failures, error); }
            else handled = error;
            try
            {
                ClearSources();
                var observed = IsPendingShareReview ? _browser.ReadObservedNativeShareMutation(_pendingMutation!) : null;
                var committed = IsPendingShareReview && _browser.WasNativeShareMutationCommitted(_pendingMutation!);
                _status = !OriginalAlive() ? "Files view closed" : committed
                    ? observed!.Value!.Share.IsRevoked
                        ? "Access was revoked. Its original Home audit or cleanup needs attention; the revoke will not repeat."
                        : "The access role changed. Its original Home audit or cleanup needs attention; the grant will not repeat."
                    : error.Message;
            }
            catch (Exception cleanup)
            {
                if (handled is not null) { Add(failures, handled); handled = null; }
                Add(failures, cleanup);
            }
        }
        finally
        {
            _busy = false;
            try { Changed(); }
            catch (Exception error)
            {
                if (handled is not null) { Add(failures, handled); handled = null; }
                Add(failures, error);
            }
            try { _operations.Release(); }
            catch (Exception error)
            {
                if (handled is not null) { Add(failures, handled); handled = null; }
                Add(failures, error);
            }
            ThrowOriginalFailures(failures, "Original access review and cleanup failed.");
        }
    }
}
