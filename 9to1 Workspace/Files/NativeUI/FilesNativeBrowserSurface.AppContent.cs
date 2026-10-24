using Avalonia.Threading;
using Haven.Application;

namespace HavenOS.Files.NativeUI;

public sealed partial class FilesNativeBrowserSurface
{
    /// <summary>Thread-safe lifetime observation only; this is not a readiness or access grant.</summary>
    public bool OriginalPublicationIsAlive => OriginalAlive();

    /// <summary>UI-only metadata from the same privately issued displayed page.</summary>
    public bool TryGetOriginalLocation(out Guid storeId, out Guid? folderId, out string title)
    {
        Dispatcher.UIThread.VerifyAccess();
        storeId = Guid.Empty; folderId = null; title = "Files";
        if (!OriginalAlive() || _page is not { } page || _boundStoreId != page.StoreID) return false;
        storeId = page.StoreID; folderId = page.ParentID; title = page.FolderTitle;
        return true;
    }

    /// <summary>Leaves drafts, reviewed mutations and active work untouched when retirement is refused.</summary>
    public bool CanPrepareOriginalClose()
    {
        Dispatcher.UIThread.VerifyAccess();
        lock (_originalTasksSync)
            return _initialized && OriginalAlive() && !_busy && _pendingMutation is null && _moveSource is null
                && string.IsNullOrWhiteSpace(_editName.Text) && _originalTasks.All(actual => actual.IsCompleted);
    }

    /// <summary>Opens an explicit canonical location in this already admitted original store.</summary>
    public Task OpenOriginalLocationAsync(Guid expectedStoreId, Guid? folderId, CancellationToken token = default)
        => TrackOriginalAsync(() => OpenOriginalLocationCoreAsync(expectedStoreId, folderId, token));

    private async Task OpenOriginalLocationCoreAsync(Guid expectedStoreId, Guid? folderId, CancellationToken token)
    {
        Dispatcher.UIThread.VerifyAccess();
        if (expectedStoreId == Guid.Empty || folderId == Guid.Empty)
            throw new ArgumentException("Retain the canonical Files store and folder identities.");
        if (!_initialized || _page is null || _boundStoreId != expectedStoreId)
            throw new UnauthorizedAccessException("This location belongs to a different original Files store.");
        if (_busy || _pendingMutation is not null || _moveSource is not null || !string.IsNullOrWhiteSpace(_editName.Text))
            throw new InvalidOperationException("Finish the current Files change before opening another location.");
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, _lifetime.Token);
        await _operations.WaitAsync(linked.Token);
        try
        {
            _busy = true; Changed();
            await RequireReadyAsync(linked.Token);
            var page = await _browser.ListAsync(_originalActor, folderId, token: linked.Token, expectedStoreId: expectedStoreId);
            await _browser.RevalidateAsync(page, _originalActor, linked.Token);
            var current = await _browser.CaptureOriginalPageReadCheckAsync(page, _originalActor, OriginalAlive, linked.Token);
            await RequireReadyAsync(linked.Token);
            await RequireOriginalOwnerAsync(current, linked.Token);
            RequireOriginalAlive(linked.Token);
            _page = page; _originalPageCurrent = current; _query = ""; _search.Text = "";
            _history.Clear(); _history.Add((null, "Files", false));
            if (folderId is not null) _history.Add((folderId, page.FolderTitle, false));
            _historyIndex = _history.Count - 1;
            _items.SelectedItem = null; ApplyDisplayedNameSort();
            _status = $"{page.Items.Count} items";
            RequireOriginalAlive(linked.Token);
        }
        finally { _busy = false; _operations.Release(); Changed(); }
    }

    /// <summary>Descriptive current successful preview identity, not a resource or readiness grant.</summary>
    public bool TryGetOriginalTextPreviewLocation(out Guid fileId, out Guid revisionId, out string name)
    {
        Dispatcher.UIThread.VerifyAccess();
        fileId = revisionId = Guid.Empty; name = "";
        if (!OriginalAlive() || _textPreview is not { Succeeded: true } preview ||
            _textPreviewPage is not { } page || !ReferenceEquals(_page, page) ||
            _textPreviewRow is not { } row || !ReferenceEquals(_items.SelectedItem, row) ||
            !page.Items.Any(item => ReferenceEquals(item, row)) || preview.FileId != row.Id.Value ||
            !Guid.TryParseExact(preview.Revision, "D", out var revision) || revision == Guid.Empty ||
            row.CurrentRevisionId?.Value != revision) return false;
        fileId = row.Id.Value; revisionId = revision; name = row.Name; return true;
    }

    /// <summary>Restores an explicit immutable text preview through a fresh privately issued Files
    /// selection. Saved address fields are targets only; they never recreate a page or read grant.</summary>
    public Task OpenOriginalTextPreviewLocationAsync(Guid store, Guid parent, Guid file, Guid revision,
        CancellationToken token = default) => TrackOriginalAsync(() => OpenTextPreviewLocationCoreAsync(store, parent, file, revision, token));

    private async Task OpenTextPreviewLocationCoreAsync(Guid store, Guid parent, Guid file, Guid revision, CancellationToken token)
    {
        Dispatcher.UIThread.VerifyAccess();
        if (store == Guid.Empty || parent == Guid.Empty || file == Guid.Empty || revision == Guid.Empty)
            throw new ArgumentException("Retain the actual Files store, parent, file and immutable revision.");
        RequireOriginalAlive(token);
        if (_busy || _pendingMutation is not null || _moveSource is not null || !string.IsNullOrWhiteSpace(_editName.Text))
            throw new InvalidOperationException("Finish the current Files change before restoring a preview.");
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, _lifetime.Token);
        var originals = new CloudflareOriginalTaskLedger(); originals.BindOriginalOwner(this);
        await originals.RunToOriginalSettlementAsync(async () =>
        {
            Action<Action> Own = body => originals.Invoke(() => { body(); return true; });
            Action<Task> Retain = actual => originals.Track(actual);
            await originals.AwaitAsync(originals.Invoke(() => RequireReadyAsync(linked.Token)));
            var selected = await originals.AwaitAsync(originals.Invoke(() => _browser.ReadOriginalTextPreviewSelectionWithinSourceAsync(
                _originalActor, store, new(parent), new(file), new(revision), OriginalAlive, Own, Retain, linked.Token)));
            await originals.AwaitAsync(originals.Invoke(() => RevealOriginalRegisteredPageWithinSourceAsync(_browser,
                selected.Page, selected.Row, _originalActor, Own, Retain, linked.Token)));
            await originals.AwaitAsync(originals.Invoke(() => DispatchAsync("9to1.Files.Preview", null, linked.Token).AsTask()));
            await originals.AwaitAsync(originals.Invoke(() => RevalidateOriginalOwnerAsync(linked.Token)));
            await originals.AwaitAsync(originals.Invoke(() => RequireReadyAsync(linked.Token)));
            RequireOriginalAlive(linked.Token);
            if (!TryGetOriginalLocation(out var actualStore, out var actualFolder, out _) || actualStore != store || actualFolder != parent ||
                !TryGetOriginalTextPreviewLocation(out var actualFile, out var actualRevision, out _) || actualFile != file || actualRevision != revision)
                throw new NotSupportedException(_textPreview?.Message ?? "This file has no current supported text preview.");
        });
    }

}
