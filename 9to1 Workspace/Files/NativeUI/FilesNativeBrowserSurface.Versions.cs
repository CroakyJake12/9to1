using Avalonia.Threading;
using HavenOS.Files;
using HavenOS.Files.NativeHost;

namespace HavenOS.Files.NativeUI;

public sealed partial class FilesNativeBrowserSurface
{
    private sealed record OriginalVersionHistory(FilesNativeBrowserPage Page, HostedItemMetadata Item,
        FilesPage<FilesRevision> Data);
    private OriginalVersionHistory? _versionHistory;

    private bool VersionHistoryVisible => _versionHistory is { } history && OriginalAlive()
        && ReferenceEquals(_page, history.Page) && ReferenceEquals(_items.SelectedItem, history.Item);
    private bool CanReadVersionHistory => Available && _pendingMutation is null && _page is { } page
        && _items.SelectedItem is HostedItemMetadata item && _browser.CanReadNativeVersions(page, item, _originalActor);

    private bool TryGetVersionHistoryValue(string path, out object? value)
    {
        var history = VersionHistoryVisible ? _versionHistory : null;
        value = path switch
        {
            "CanGetVersionHistory" => CanReadVersionHistory && !VersionHistoryVisible,
            "VersionHistoryVisible" => history is not null,
            "VersionHistory" => history?.Data.Items ?? Array.Empty<FilesRevision>(),
            "VersionHistoryTitle" => history is null ? "" : $"Version history for {history.Item.Name}",
            "VersionHistoryStatus" => history is null ? "" : history.Data.Items.Count == 0
                ? "No durable versions are recorded for this item."
                : $"Showing {history.Data.Items.Count} immutable revision(s). Content reads require their own permission.",
            "CanReadMoreVersions" => Available && _pendingMutation is null && history?.Data.NextPageToken is not null,
            "CanCloseVersionHistory" => Available && history is not null,
            _ => null
        };
        return path is "CanGetVersionHistory" or "VersionHistoryVisible" or "VersionHistory"
            or "VersionHistoryTitle" or "VersionHistoryStatus" or "CanReadMoreVersions" or "CanCloseVersionHistory";
    }

    private bool TryGetVersionHistoryItemValue(object item, string path, out object? value)
    {
        value = null;
        if (!VersionHistoryVisible || item is not FilesRevision revision
            || !_versionHistory!.Data.Items.Any(row => ReferenceEquals(row, revision))) return false;
        value = path switch
        {
            "id" => revision.Id.ToString(),
            "created" => revision.CreatedAt.ToLocalTime().ToString("g"),
            "actor" => revision.ActorId,
            "source" => revision.Source,
            "size" => revision.SizeBytes is { } bytes ? $"{bytes:N0} bytes" : "Size not reported",
            "hash" => revision.ContentHash ?? "Content hash not reported",
            "app" => revision.OwningAppId is { Length: > 0 } app
                ? $"{app} · {revision.OwningAppRevisionId}" : "No owning-app revision",
            "current" => revision.IsCurrent ? "Current version" : "Previous version",
            _ => null
        };
        return path is "id" or "created" or "actor" or "source" or "size" or "hash" or "app" or "current";
    }

    private async Task ReadVersionHistoryAsync(FilesNativeBrowserPage page, HostedItemMetadata item,
        string? pageToken, long selectionVersion, CancellationToken token)
    {
        Dispatcher.UIThread.VerifyAccess();
        var versions = await _browser.ReadNativeVersionsAsync(page, item, _originalActor, OriginalAlive, pageToken, token)
            .ConfigureAwait(true);
        await RequireReadyAsync(token).ConfigureAwait(true);
        var owner = _originalPageCurrent ?? throw new UnauthorizedAccessException("The original Files version owner retired.");
        await RequireOriginalOwnerAsync(owner, token).ConfigureAwait(true);
        if (selectionVersion != Volatile.Read(ref _sharingSelectionVersion) || !ReferenceEquals(_page, page)
            || !ReferenceEquals(_items.SelectedItem, item))
            throw new InvalidOperationException("The selected Files item changed before its version history was published.");
        if (pageToken is not null && VersionHistoryVisible && _versionHistory is { } previous)
            versions = new(previous.Data.Items.Concat(versions.Items).ToArray(), versions.NextPageToken);
        _versionHistory = new(page, item, versions);
        _status = versions.Items.Count == 0 ? "No durable versions are recorded for this item."
            : $"Loaded {versions.Items.Count} immutable version(s).";
        Changed();
    }

    private void ClearOriginalVersionHistory() => _versionHistory = null;
}
