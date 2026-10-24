using Avalonia.Threading;
using Avalonia.Controls;
using System.Globalization;
using CakeOS.Cui.Runtime;
using HavenOS.Files;
using HavenOS.Files.NativeHost;
using HavenOS.Home.PermissionsTrustNotifications;

namespace HavenOS.Files.NativeUI;

public sealed partial class FilesNativeBrowserSurface : ICuiRepeatItemBindingContext, ICuiWritableBindingContext
{
    private FilesNativeBrowserSharePage? _sharingPage;
    private long _sharingSelectionVersion;
    private CancellationTokenSource? _sharingFeedCancellation;
    private bool _sharingStale, _sharingReviewInvalidated, _sharingFeedFailed, _applyingSharingRefresh;
    private HostedItemId? _sharingInvalidatedItem;
    private long _sharingRefreshGeneration;
    private FilesEmailShare? _shareScopeGrant;
    private readonly ComboBox _shareScopeRole = new() { ItemsSource = new[] { "Viewer", "Editor" } };
    private readonly TextBox _shareScopeExpiry = new() { MaxLength = 64 };
    private bool _shareScopeExpiryEnabled, _shareScopeControlsBound;
    private bool ShareScopeVisible => SharingVisible && _shareScopeGrant is { } grant &&
        _sharingPage!.Grants.Any(row => ReferenceEquals(row, grant));
    private bool CanEditShareScopeDraft => Available && _pendingMutation is null && ShareScopeVisible &&
        CanChangeExistingScope(_shareScopeGrant!);
    private static bool CanChangeExistingScope(FilesEmailShare grant) => !grant.Share.IsRevoked &&
        (grant.Share.Role is FilesAccessRole.Viewer or FilesAccessRole.Editor) &&
        (grant.Share.ExpiresAt is null || grant.Share.ExpiresAt > DateTimeOffset.UtcNow);
    private bool IsPendingShareReview => _pendingMutation is { } pending && _browser.IsNativeShareMutation(pending);
    private bool SharingVisible => _sharingPage is { } shares && ReferenceEquals(_page, shares.Page)
        && ReferenceEquals(_items.SelectedItem, shares.Item) && OriginalAlive() && !_sharingStale;
    private bool SharingStaleVisible => _sharingStale && _sharingPage is { } shares &&
        ReferenceEquals(_page, shares.Page) && ReferenceEquals(_items.SelectedItem, shares.Item) && OriginalAlive();
    private string SharingStaleMessage() => !SharingStaleVisible ? "" : _pendingMutation is not null
        ? "Access changed during a Home review. Stale grant rows remain hidden until the review is resolved; Files reloads them afterward."
        : _sharingFeedFailed
            ? "Live access updates are unavailable. Grant rows remain hidden; choose Manage access to reload them."
            : "Access changed. Grant rows are hidden while Files reloads the current access list.";
    private static bool IsSharingCommand(string command) => command is "9to1.Files.Share.Get"
        or "9to1.Files.Share.More" or "9to1.Files.Share.Close" or "9to1.Files.Share.Revoke" or "9to1.Files.Share.ChangeRole"
        or "9to1.Files.Share.EditScope" or "9to1.Files.Share.ReviewScope" or "9to1.Files.Share.CancelScope";

    private bool TryGetSharingValue(string path, out object? value)
    {
        value = path switch
        {
            "CanReviewAccess" => SharingActionAvailable("9to1.Files.Share.Get"),
            "SharingVisible" => SharingVisible,
            "SharingStale" => SharingStaleVisible,
            "SharingStaleMessage" => SharingStaleMessage(),
            "SharingTitle" => SharingVisible ? $"Access to ‘{_sharingPage!.Item.Name}’" : "",
            "SharingOwner" => SharingVisible ? _sharingPage!.Item.OwnerPrincipalId : "",
            "SharingGrants" => SharingVisible ? _sharingPage!.Grants : Array.Empty<FilesEmailShare>(),
            "SharingEmpty" => SharingVisible && _sharingPage!.Grants.Count == 0,
            "CanReadMoreGrants" => SharingActionAvailable("9to1.Files.Share.More"),
            "CanCloseAccess" => SharingActionAvailable("9to1.Files.Share.Close"),
            "ShareScopeVisible" => ShareScopeVisible,
            "ShareScopeRecipient" => ShareScopeVisible ? _shareScopeGrant!.RecipientEmail : "",
            "ShareScopeExpiryEnabled" => _shareScopeExpiryEnabled,
            "CanEditShareScopeDraft" => CanEditShareScopeDraft,
            "CanReviewShareScope" => SharingActionAvailable("9to1.Files.Share.ReviewScope"),
            "CanCancelShareScope" => SharingActionAvailable("9to1.Files.Share.CancelScope"),
            "ShareScopeHint" => ShareScopeDraftHint(),
            "PendingChange" when IsPendingShareReview => _browser.DescribeNativeShareMutation(_pendingMutation!),
            _ => null
        };
        return path is "CanReviewAccess" or "SharingVisible" or "SharingStale" or "SharingStaleMessage" or "SharingTitle" or "SharingOwner" or "SharingGrants"
            or "SharingEmpty" or "CanReadMoreGrants" or "CanCloseAccess" or "ShareScopeVisible" or "ShareScopeRecipient"
            or "ShareScopeExpiryEnabled" or "CanEditShareScopeDraft" or "CanReviewShareScope" or "CanCancelShareScope" or "ShareScopeHint" || path == "PendingChange" && IsPendingShareReview;
    }

    public bool TryGetItemValue(object item, string path, out object? value)
    {
        if (TryGetFolderTabItemValue(item, path, out value)) return true;
        if (TryGetVersionHistoryItemValue(item, path, out value)) return true;
        value = null;
        if (!SharingVisible || item is not FilesEmailShare grant
            || !_sharingPage!.Grants.Any(row => ReferenceEquals(row, grant))) return false;
        value = path switch
        {
            "id" => grant.Share.Id.ToString(),
            "email" => grant.RecipientEmail,
            "role" => grant.Share.Role.ToString(),
            "expiry" => grant.Share.ExpiresAt is { } expiry ? $"Expires {expiry.ToLocalTime():g}" : "No expiry",
            "origin" => grant.Origin == FilesGrantOrigin.Inherited ? $"Inherited from {grant.InheritedFrom}" : "Direct",
            "state" => grant.Share.IsRevoked ? "Revoked" : grant.Share.ExpiresAt is { } expires && expires <= DateTimeOffset.UtcNow
                ? "Expired" : "Active",
            "CanRevoke" => grant.Origin == FilesGrantOrigin.Direct && SharingActionAvailable("9to1.Files.Share.Revoke") && !grant.Share.IsRevoked
                && grant.Share.Role is FilesAccessRole.Viewer or FilesAccessRole.Editor,
            "CanChangeRole" => grant.Origin == FilesGrantOrigin.Direct && SharingActionAvailable("9to1.Files.Share.ChangeRole") && !grant.Share.IsRevoked
                && grant.Share.Role is FilesAccessRole.Viewer or FilesAccessRole.Editor
                && (grant.Share.ExpiresAt is null || grant.Share.ExpiresAt.Value > DateTimeOffset.UtcNow),
            "CanEditScope" => grant.Origin == FilesGrantOrigin.Direct && SharingActionAvailable("9to1.Files.Share.EditScope") && CanChangeExistingScope(grant),
            "changeRoleLabel" => grant.Share.Role == FilesAccessRole.Viewer ? "Make editor…" : "Make viewer…",
            _ => null
        };
        return path is "id" or "email" or "role" or "expiry" or "origin" or "state" or "CanRevoke" or "CanChangeRole" or "CanEditScope" or "changeRoleLabel";
    }
    public bool TrySetItemValue(object item, string path, object? value) => false;

    private bool SharingActionAvailable(string command) => command switch
    {
        "9to1.Files.Share.Get" => Available && _pendingMutation is null && _page is { } page
            && _items.SelectedItem is HostedItemMetadata item && _browser.CanReviewNativeShares(page, item),
        "9to1.Files.Share.More" => Available && _pendingMutation is null && SharingVisible && _sharingPage!.Next is not null,
        "9to1.Files.Share.Close" => Available && _pendingMutation is null && SharingVisible,
        "9to1.Files.Share.EditScope" => Available && _pendingMutation is null && SharingVisible && _browser.HasNativeMutationOwner,
        "9to1.Files.Share.ReviewScope" => CanEditShareScopeDraft && TryReadShareScopeDraft(out _, out _, out _),
        "9to1.Files.Share.CancelScope" => Available && _pendingMutation is null && ShareScopeVisible,
        "9to1.Files.Share.Revoke" or "9to1.Files.Share.ChangeRole" => Available && _pendingMutation is null && SharingVisible && _browser.HasNativeMutationOwner,
        _ => false
    };

    private void SharingSelectionChanged()
    {
        Interlocked.Increment(ref _sharingSelectionVersion);
        ClearOriginalProperties();
        ClearOriginalVersionHistory();
        StopOriginalSharingFeed();
        if (_sharingInvalidatedItem is { } invalidated && _items.SelectedItem is HostedItemMetadata selected
            && selected.Id != invalidated)
        {
            Interlocked.Increment(ref _sharingRefreshGeneration);
            _sharingInvalidatedItem = null; _sharingReviewInvalidated = false; _sharingStale = false; _sharingFeedFailed = false;
        }
        ClearOriginalShareScopeEditor();
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
        if ((command is "9to1.Files.Share.Revoke" or "9to1.Files.Share.ChangeRole" or "9to1.Files.Share.EditScope") && (grant is null || originalShares is null
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
            if (command == "9to1.Files.Share.Close")
            {
                Interlocked.Increment(ref _sharingRefreshGeneration);
                StopOriginalSharingFeed(); ClearOriginalShareScopeEditor(); _sharingPage = null;
                _sharingStale = false; _sharingReviewInvalidated = false; _sharingFeedFailed = false; _sharingInvalidatedItem = null;
                return;
            }
            if (command == "9to1.Files.Share.CancelScope") { ClearOriginalShareScopeEditor(); return; }
            if (command == "9to1.Files.Share.EditScope")
            {
                if (!CanChangeExistingScope(grant!)) throw new InvalidOperationException("Choose an active original Viewer or Editor grant.");
                _shareScopeGrant = grant;
                _shareScopeRole.SelectedIndex = grant!.Share.Role == FilesAccessRole.Viewer ? 0 : 1;
                _shareScopeExpiryEnabled = grant.Share.ExpiresAt is not null;
                _shareScopeExpiry.Text = grant.Share.ExpiresAt is { } expires
                    ? expires.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'", CultureInfo.InvariantCulture) : "";
                _status = "Edit this existing recipient’s role or expiry, then review the exact change in Home.";
                return;
            }
            if (command == "9to1.Files.Share.ReviewScope")
            {
                var originalGrant = _shareScopeGrant;
                if (originalGrant is null || !TryReadShareScopeDraft(out var role, out var expires, out _))
                    throw new InvalidOperationException(ShareScopeDraftHint());
                var prepared = await _browser.PrepareNativeShareScopeChangeAsync(originalShares!, originalGrant, role, expires,
                    _originalActor, OwnerCurrent, _mutationSession, linked.Token);
                RequireOriginalSelection();
                if (!ReferenceEquals(_shareScopeGrant, originalGrant))
                    throw new InvalidOperationException("The original recipient scope editor changed.");
                _pendingMutation = prepared; ClearOriginalShareScopeEditor();
                _status = prepared.Approval.IsAllowed ? "Ready to apply this recipient scope change. Choose Apply."
                    : prepared.Approval.State == HomePermissionRequestState.PendingApproval
                        ? "Review this recipient’s role and expiry in Home, then choose Apply." : prepared.Approval.Message;
                return;
            }
            if (command is "9to1.Files.Share.Revoke" or "9to1.Files.Share.ChangeRole")
            {
                var prepared = command == "9to1.Files.Share.Revoke"
                    ? await _browser.PrepareNativeShareRevocationAsync(originalShares!, grant!, _originalActor,
                        OwnerCurrent, _mutationSession, linked.Token)
                    : await _browser.PrepareNativeShareRoleChangeAsync(originalShares!, grant!,
                        grant!.Share.Role == FilesAccessRole.Viewer ? FilesAccessRole.Editor : FilesAccessRole.Viewer,
                        _originalActor, OwnerCurrent, _mutationSession, linked.Token);
                RequireOriginalSelection();
                _pendingMutation = prepared; ClearOriginalShareScopeEditor();
                _status = prepared.Approval.IsAllowed ? command == "9to1.Files.Share.Revoke"
                    ? "Ready to revoke this access grant. Choose Apply." : "Ready to change this recipient’s role. Choose Apply."
                    : prepared.Approval.State == HomePermissionRequestState.PendingApproval
                        ? "Review this access change in Home, then choose Apply." : prepared.Approval.Message;
                return;
            }
            if (command == "9to1.Files.Apply")
            {
                var outcome = await _browser.ApplyNativeShareMutationAsync(originalReview!, linked.Token);
                var shareCommitted = _browser.WasNativeShareMutationCommitted(originalReview!);
                // A provider ACK is not permission to publish into a replaced Home/profile.
                // This retained callback checks Home/configuration only, not the now-advanced Files revision.
                await RequireReadyAsync(linked.Token);
                var publicationOwner = _originalPageCurrent ?? throw new UnauthorizedAccessException("The original Files owner observation retired.");
                await RequireOriginalOwnerAsync(publicationOwner, linked.Token);
                RequireOriginalSelection();
                _status = outcome.Message;
                if (outcome.AwaitingHomeReview) return;
                if (outcome.AuditRecorded) _pendingMutation = null;
                if (shareCommitted)
                {
                    _sharingStale = true; _sharingReviewInvalidated = true;
                    _sharingFeedFailed = false;
                    _sharingInvalidatedItem = originalItem?.Id;
                }
                // Permission publication advances the item revision. Withdraw the old grant rows,
                // then reload canonical metadata; never keep stale permission-change buttons enabled.
                _sharingPage = null;
                var destination = _history[_historyIndex];
                var searchParent = destination.IsSearch && destination.IsProviderWideSearch ? null : destination.ID;
                var page = await _browser.ListAsync(_originalActor, searchParent, destination.Query, token: linked.Token,
                    expectedStoreId: _boundStoreId, isTrash: destination.IsTrash, isSearch: destination.IsSearch);
                await _browser.RevalidateAsync(page, _originalActor, linked.Token);
                var current = await _browser.CaptureOriginalPageReadCheckAsync(page, _originalActor, OriginalAlive, linked.Token);
                await RequireReadyAsync(linked.Token);
                await RequireOriginalOwnerAsync(current, linked.Token);
                RequireOriginalAlive(linked.Token);
                if (!OwnsSelection()) throw new InvalidOperationException("The original access selection changed during refresh.");
                _page = page; _originalPageCurrent = current; _items.SelectedItem = null;
                ApplyDisplayedNameSort();
                _status = outcome.Message;
                if (_pendingMutation is null && _sharingReviewInvalidated)
                    await ResumeStaleSharingReviewAsync(page, linked.Token);
                return;
            }
            var shares = await _browser.ReadNativeSharesAsync(originalPage!, originalItem!, _originalActor,
                OwnerCurrent, command == "9to1.Files.Share.More" ? originalShares : null, linked.Token);
            await RequireReadyAsync(linked.Token);
            var owner = _originalPageCurrent ?? throw new UnauthorizedAccessException("The original Files owner observation retired.");
            await RequireOriginalOwnerAsync(owner, linked.Token);
            RequireOriginalSelection();
            _sharingPage = shares;
            _sharingStale = false; _sharingReviewInvalidated = false; _sharingFeedFailed = false; _sharingInvalidatedItem = null;
            _status = shares.Grants.Count == 0 ? "No direct or inherited grants were found for this item."
                : $"{shares.Grants.Count} direct and inherited access grants on this page.";
            StartOriginalSharingFeed(shares, originalSelectionVersion);
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

    private void BindOriginalShareScopeControls(CuiControlRegistry registry)
    {
        registry.BindAuthoredControl("files-share-scope-role", _ => _shareScopeRole);
        registry.BindAuthoredControl("files-share-scope-expiry", _ => _shareScopeExpiry);
        if (_shareScopeControlsBound) return;
        _shareScopeControlsBound = true;
        _shareScopeRole.SelectionChanged += (_, _) =>
        { if (ShareScopeVisible && !_busy) RunOriginalNativeMutation(Changed); };
        _shareScopeExpiry.TextChanged += (_, _) =>
        { if (ShareScopeVisible && !_busy) RunOriginalNativeMutation(Changed); };
    }
    public bool TrySetValue(string path, object? value)
    {
        Dispatcher.UIThread.VerifyAccess();
        if (path != "ShareScopeExpiryEnabled" || value is not bool enabled || !CanEditShareScopeDraft) return false;
        RunOriginalNativeMutation(() => { _shareScopeExpiryEnabled = enabled; Changed(); });
        return true; // Draft only; actual grant requires Home's separate review/claim/fence.
    }
    private void ClearOriginalShareScopeEditor()
    {
        _shareScopeGrant = null; _shareScopeExpiryEnabled = false;
        _shareScopeRole.SelectedIndex = -1; _shareScopeExpiry.Text = "";
    }
    private bool TryReadShareScopeDraft(out FilesAccessRole role, out DateTimeOffset? expiry, out string hint)
    {
        role = FilesAccessRole.Viewer; expiry = null; hint = "Choose an active original recipient grant.";
        if (!ShareScopeVisible || !CanChangeExistingScope(_shareScopeGrant!)) return false;
        if (_shareScopeRole.SelectedIndex is not (0 or 1)) { hint = "Choose Viewer or Editor."; return false; }
        role = _shareScopeRole.SelectedIndex == 0 ? FilesAccessRole.Viewer : FilesAccessRole.Editor;
        if (_shareScopeExpiryEnabled)
        {
            var formats = new[] { "yyyy-MM-dd'T'HH:mm:ss'Z'", "yyyy-MM-dd'T'HH:mm'Z'", "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'" };
            if (!DateTimeOffset.TryParseExact(_shareScopeExpiry.Text?.Trim(), formats, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var instant) || instant <= DateTimeOffset.UtcNow)
            { hint = "Enter a future UTC date/time, for example YYYY-MM-DDTHH:mm:ssZ."; return false; }
            expiry = instant;
        }
        if (role == _shareScopeGrant!.Share.Role && expiry == _shareScopeGrant.Share.ExpiresAt)
        { hint = "Change the role or expiry to continue."; return false; }
        hint = "Review this exact direct-grant change in Home. The recipient stays the same; inherited access may still apply.";
        return true;
    }
    private string ShareScopeDraftHint() { _ = TryReadShareScopeDraft(out _, out _, out var hint); return hint; }

    private void StartOriginalSharingFeed(FilesNativeBrowserSharePage page, long selectionVersion)
    {
        Dispatcher.UIThread.VerifyAccess();
        StopOriginalSharingFeed();
        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _sharingFeedCancellation = cancellation;
        try
        {
            TrackOriginalAsync(() => WatchOriginalSharingFeedAsync(page, selectionVersion, cancellation));
        }
        catch
        {
            if (ReferenceEquals(Interlocked.CompareExchange(ref _sharingFeedCancellation, null, cancellation), cancellation))
                cancellation.Dispose();
            throw;
        }
    }

    private void StopOriginalSharingFeed()
    {
        var current = Interlocked.Exchange(ref _sharingFeedCancellation, null);
        if (current is null) return;
        try { current.Cancel(); } catch (ObjectDisposedException) { }
    }

    private async Task WatchOriginalSharingFeedAsync(FilesNativeBrowserSharePage page, long selectionVersion,
        CancellationTokenSource cancellation)
    {
        var token = cancellation.Token;
        try
        {
            await foreach (var change in _browser.SubscribeOriginalChangesAsync(page.Page, _originalActor,
                () => !_lifetime.IsCancellationRequested && selectionVersion == Volatile.Read(ref _sharingSelectionVersion), token)
                .WithCancellation(token).ConfigureAwait(false))
            {
                var scopeChange = change.Kind is "Share.Grant" or "Share.Revoke" or "Move" or "Delete" or "Restore";
                var selectedItemChange = change.ItemId == page.Item.Id &&
                    change.Kind is "Rename" or "FileContentCommitted" or "OwningAppRevisionCommitted";
                if (!scopeChange && !selectedItemChange) continue;
                if (selectionVersion != Volatile.Read(ref _sharingSelectionVersion)) return;
                Task? refresh = null;
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (selectionVersion != Volatile.Read(ref _sharingSelectionVersion) || !ReferenceEquals(_sharingPage, page)) return;
                    if (_pendingMutation is not null)
                    {
                        _sharingStale = true; _sharingReviewInvalidated = true; _sharingFeedFailed = false; _sharingInvalidatedItem = page.Item.Id;
                        ClearOriginalShareScopeEditor();
                        _status = "Access changed elsewhere. Resolve the current Home review, then reopen Manage access.";
                        Changed();
                        return;
                    }
                    _sharingStale = true; _sharingReviewInvalidated = true; _sharingFeedFailed = false; _sharingInvalidatedItem = page.Item.Id;
                    var refreshGeneration = Interlocked.Increment(ref _sharingRefreshGeneration);
                    refresh = TrackOriginalAsync(() => RefreshSharingAfterExternalChangeAsync(page, selectionVersion, refreshGeneration));
                });
                if (refresh is not null) await refresh.ConfigureAwait(false);
                return; // Refresh captures a new durable cursor and installs the next watcher.
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested || _lifetime.IsCancellationRequested) { }
        catch (Exception)
        {
            try
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (selectionVersion == Volatile.Read(ref _sharingSelectionVersion) && ReferenceEquals(_sharingPage, page))
                    {
                        _sharingStale = true; _sharingReviewInvalidated = false; _sharingFeedFailed = true; _sharingInvalidatedItem = page.Item.Id;
                        _status = "Live access updates are unavailable. Reopen Manage access to review current grants.";
                        Changed();
                    }
                });
            }
            catch (Exception) { /* The original view's close path retains its own failures. */ }
        }
        finally
        {
            Interlocked.CompareExchange(ref _sharingFeedCancellation, null, cancellation);
            try { cancellation.Dispose(); } catch (ObjectDisposedException) { }
        }
    }

    private async Task RefreshSharingAfterExternalChangeAsync(FilesNativeBrowserSharePage originalShares,
        long selectionVersion, long refreshGeneration)
    {
        Dispatcher.UIThread.VerifyAccess();
        if (refreshGeneration != Volatile.Read(ref _sharingRefreshGeneration)
            || selectionVersion != Volatile.Read(ref _sharingSelectionVersion)
            || !ReferenceEquals(_sharingPage, originalShares)) return;
        _applyingSharingRefresh = true;
        try { await InvokeOriginalAsync("9to1.Files.Refresh", _lifetime.Token).ConfigureAwait(true); }
        finally { _applyingSharingRefresh = false; }
        if (_lifetime.IsCancellationRequested || _closing
            || refreshGeneration != Volatile.Read(ref _sharingRefreshGeneration)) return;
        if (_page is not { } refreshedPage || refreshedPage.ParentID != originalShares.Page.ParentID
            || refreshedPage.IsTrash != originalShares.Page.IsTrash) return;
        if (refreshedPage.StoreRevision == originalShares.Page.StoreRevision)
        {
            _status = "Access changed elsewhere; finish the current Files operation, then reopen Manage access.";
            Changed();
            return;
        }
        if (_items.SelectedItem is HostedItemMetadata anotherSelection && anotherSelection.Id != originalShares.Item.Id) return;
        var currentItem = refreshedPage.Items.FirstOrDefault(item => item.Id == originalShares.Item.Id);
        if (currentItem is null)
        {
            StopOriginalSharingFeed();
            _sharingPage = null;
            _status = "The selected item moved out of this folder. Navigate to it again to review access.";
            Changed();
            return;
        }
        _items.SelectedItem = currentItem;
        await InvokeSharingOriginalAsync("9to1.Files.Share.Get", null, _lifetime.Token).ConfigureAwait(true);
    }

    private async Task ResumeStaleSharingReviewAsync(FilesNativeBrowserPage page, CancellationToken token)
    {
        Dispatcher.UIThread.VerifyAccess();
        if (!_sharingReviewInvalidated || _pendingMutation is not null || _sharingInvalidatedItem is not { } itemId
            || !ReferenceEquals(_page, page)) return;
        var row = page.Items.FirstOrDefault(item => item.Id == itemId);
        if (row is null)
        {
            _sharingPage = null; _sharingStale = false; _sharingReviewInvalidated = false; _sharingFeedFailed = false; _sharingInvalidatedItem = null;
            _status = "The selected item is no longer in this folder. Navigate to it again to review access.";
            Changed();
            return;
        }
        _items.SelectedItem = row;
        var selectionVersion = Volatile.Read(ref _sharingSelectionVersion);
        var shares = await _browser.ReadNativeSharesAsync(page, row, _originalActor, OriginalAlive, token).ConfigureAwait(true);
        await RequireReadyAsync(token).ConfigureAwait(true);
        var owner = _originalPageCurrent ?? throw new UnauthorizedAccessException("The original Files owner observation retired.");
        await RequireOriginalOwnerAsync(owner, token).ConfigureAwait(true);
        if (selectionVersion != Volatile.Read(ref _sharingSelectionVersion) || !ReferenceEquals(_page, page)
            || !ReferenceEquals(_items.SelectedItem, row)) return;
        _sharingPage = shares; _sharingStale = false; _sharingReviewInvalidated = false; _sharingFeedFailed = false; _sharingInvalidatedItem = null;
        _status = shares.Grants.Count == 0 ? "No direct or inherited grants were found for this item."
            : $"{shares.Grants.Count} direct and inherited access grants on this page.";
        StartOriginalSharingFeed(shares, selectionVersion);
        Changed();
    }

}
