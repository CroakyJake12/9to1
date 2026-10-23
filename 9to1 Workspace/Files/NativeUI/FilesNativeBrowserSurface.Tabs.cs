using Avalonia.Input;
using Avalonia.Threading;
using HavenOS.Files;

namespace HavenOS.Files.NativeUI;

public sealed partial class FilesNativeBrowserSurface
{
    private const int MaximumFolderTabs = 16;
    // View state only. A remembered identity, revision or selection grants no access.
    // Every activation obtains a fresh privately issued page from the SAME native owner.
    private sealed record FolderTabState(Guid StoreId,
        IReadOnlyList<(Guid? ID, string Title, bool IsTrash)> History, int HistoryIndex,
        string Query, string SearchText, int SortIndex, HostedItemId? Selection);
    private sealed class FolderTab(long id, FolderTabState state)
    {
        internal readonly long Id = id;
        internal FolderTabState State = state;
        internal string? LastError;
    }
    private readonly List<FolderTab> _folderTabs = [];
    private FolderTab? _activeFolderTab;
    // A live original Home/configuration observation, never a remembered Files access grant.
    private Func<CancellationToken, ValueTask<bool>>? _folderTabsOwnerCurrent;
    private long _nextFolderTabId;

    private static bool IsFolderTabCommand(string command) => command is "9to1.Files.Tab.New"
        or "9to1.Files.Tab.OpenFolder" or "9to1.Files.Tab.Select" or "9to1.Files.Tab.Close"
        or "9to1.Files.Tab.Next" or "9to1.Files.Tab.Previous";

    private bool CanChangeFolderTabs => Available && _boundStoreId is { } store && store != Guid.Empty
        && _activeFolderTab is not null && _pendingMutation is null && string.IsNullOrWhiteSpace(_editName.Text);

    private bool FolderTabActionAvailable(string command) => command switch
    {
        "9to1.Files.Tab.New" => CanChangeFolderTabs && _folderTabs.Count < MaximumFolderTabs,
        "9to1.Files.Tab.OpenFolder" => CanChangeFolderTabs && _folderTabs.Count < MaximumFolderTabs
            && _page is { IsTrash: false } page && _items.SelectedItem is HostedItemMetadata { Kind: HostedItemKind.Folder } item
            && page.Items.Any(row => ReferenceEquals(row, item)),
        "9to1.Files.Tab.Select" or "9to1.Files.Tab.Next" or "9to1.Files.Tab.Previous" => CanChangeFolderTabs && _folderTabs.Count > 1,
        "9to1.Files.Tab.Close" => CanChangeFolderTabs && _folderTabs.Count > 1,
        _ => false
    };

    private bool TryGetFolderTabsValue(string path, out object? value)
    {
        value = path switch
        {
            "FolderTabs" => OriginalAlive() ? _folderTabs.ToArray() : Array.Empty<FolderTab>(),
            "CanNewFolderTab" => FolderTabActionAvailable("9to1.Files.Tab.New"),
            "CanOpenFolderTab" => FolderTabActionAvailable("9to1.Files.Tab.OpenFolder"),
            "FolderTabSummary" => $"{_folderTabs.Count} of {MaximumFolderTabs} tabs",
            "FolderTabHint" => _pendingMutation is not null ? "Finish or cancel the current change before changing tabs."
                : !string.IsNullOrWhiteSpace(_editName.Text) ? "Finish or clear the item name before changing tabs."
                : "Tabs keep separate history and search. Switching reloads the first page of current items.",
            _ => null
        };
        return path is "FolderTabs" or "CanNewFolderTab" or "CanOpenFolderTab" or "FolderTabSummary" or "FolderTabHint";
    }

    private bool TryGetFolderTabItemValue(object item, string path, out object? value)
    {
        value = null;
        if (!OriginalAlive() || item is not FolderTab tab || !_folderTabs.Any(row => ReferenceEquals(row, tab))) return false;
        var active = ReferenceEquals(tab, _activeFolderTab);
        var title = active ? _page?.FolderTitle ?? CurrentFolderTitle() : TabLocation(tab.State).Title;
        value = path switch
        {
            "id" => tab.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "label" => title,
            "active" => active,
            "closeName" => $"Close {title} tab",
            "canSelect" => FolderTabActionAvailable("9to1.Files.Tab.Select") && !active,
            "canClose" => FolderTabActionAvailable("9to1.Files.Tab.Close") && !HasUnappliedTabSearch(tab),
            "locationState" => tab.LastError is { } error ? $"Unavailable: {error}" : "",
            "searchState" => HasUnappliedTabSearch(tab) ? "Run or clear this tab's search before closing it." : "",
            _ => null
        };
        return path is "id" or "label" or "active" or "closeName" or "canSelect" or "canClose" or "locationState" or "searchState";
    }

    private string CurrentFolderTitle() => _historyIndex >= 0 && _historyIndex < _history.Count
        ? _history[_historyIndex].Title : "Files";
    private static (Guid? ID, string Title, bool IsTrash) TabLocation(FolderTabState state)
    {
        if (state.History.Count is < 1 or > 128 || state.HistoryIndex < 0 || state.HistoryIndex >= state.History.Count)
            throw new InvalidOperationException("The original tab location is unavailable.");
        return state.History[state.HistoryIndex];
    }
    private bool HasUnappliedTabSearch(FolderTab tab) => ReferenceEquals(tab, _activeFolderTab)
        ? (_search.Text ?? "") != _query : tab.State.SearchText != tab.State.Query;

    private FolderTabState CaptureFolderTabState()
    {
        Dispatcher.UIThread.VerifyAccess();
        if (_boundStoreId is not { } store || store == Guid.Empty || _history.Count is < 1 or > 128
            || _historyIndex < 0 || _historyIndex >= _history.Count)
            throw new InvalidOperationException("Retain the original Files tab and store.");
        var selected = _page is { } page && _items.SelectedItem is HostedItemMetadata item
            && page.Items.Any(row => ReferenceEquals(row, item)) ? item.Id : (HostedItemId?)null;
        return new(store, Array.AsReadOnly(_history.ToArray()), _historyIndex, _query, _search.Text ?? "",
            _sort.SelectedIndex == 1 ? 1 : 0, selected);
    }

    private void InitializeFolderTabs()
    {
        if (_activeFolderTab is not null || _folderTabs.Count != 0)
            throw new InvalidOperationException("The original Files tabs are already mounted.");
        var first = new FolderTab(++_nextFolderTabId, CaptureFolderTabState());
        _folderTabs.Add(first); _activeFolderTab = first;
    }

    private void WithdrawFolderTabsOnRetirement()
    {
        if (OriginalAlive()) return;
        _folderTabs.Clear(); _activeFolderTab = null; _folderTabsOwnerCurrent = null;
    }

    private void RestoreFolderTabNavigation(FolderTabState state)
    {
        if (state.StoreId != _boundStoreId) throw new UnauthorizedAccessException("The original tab store changed.");
        _ = TabLocation(state);
        _history.Clear(); _history.AddRange(state.History); _historyIndex = state.HistoryIndex;
        _query = state.Query; _search.Text = state.SearchText; _sort.SelectedIndex = state.SortIndex;
    }

    // Deliberately local to the focused Files item list. Editors and the embedded
    // browser retain their own Ctrl+T/W/Tab handling everywhere else.
    private string? GetOwnedFolderTabShortcut(KeyEventArgs args)
    {
        if (!_items.IsKeyboardFocusWithin || !IsKeyboardFocusWithin) return null;
        var command = args.Key switch
        {
            Key.T when args.KeyModifiers == KeyModifiers.Control => "9to1.Files.Tab.New",
            Key.W when args.KeyModifiers == KeyModifiers.Control => "9to1.Files.Tab.Close",
            Key.Tab when args.KeyModifiers == KeyModifiers.Control => "9to1.Files.Tab.Next",
            Key.Tab when args.KeyModifiers == (KeyModifiers.Control | KeyModifiers.Shift) => "9to1.Files.Tab.Previous",
            _ => null
        };
        if (command == "9to1.Files.Tab.Close" && _activeFolderTab is { } active && HasUnappliedTabSearch(active)) return null;
        return command is not null && FolderTabActionAvailable(command) ? command : null;
    }

    private async Task InvokeFolderTabFromNativeEventAsync(string command)
    {
        try { await DispatchAsync(command, null, _lifetime.Token); }
        catch (Exception)
        {
            // The original TrackOriginalAsync task retains faults for owner drain.
        }
    }

    private async Task InvokeFolderTabOriginalAsync(string command, object? parameter, CancellationToken token)
    {
        Dispatcher.UIThread.VerifyAccess();
        if (!FolderTabActionAvailable(command)) return;
        var originalTab = _activeFolderTab ?? throw new InvalidOperationException("The original Files tab is unavailable.");
        var originalPage = _page;
        var originalSelection = _items.SelectedItem as HostedItemMetadata;
        var originalState = CaptureFolderTabState();
        var originalRows = _folderTabs.ToArray();
        var originalMove = _moveSource;
        FolderTab? requested = null;
        if (command is "9to1.Files.Tab.Select" or "9to1.Files.Tab.Close")
        {
            requested = parameter is null && command == "9to1.Files.Tab.Close" ? originalTab : parameter as FolderTab;
            if (requested is null || !_folderTabs.Any(row => ReferenceEquals(row, requested)))
                throw new ArgumentException("Choose a tab from the same displayed Files surface.");
            if (command == "9to1.Files.Tab.Select" && ReferenceEquals(requested, originalTab)) return;
            if (command == "9to1.Files.Tab.Close" && HasUnappliedTabSearch(requested))
            { _status = "Run or clear this tab's search before closing it."; Changed(); return; }
        }
        else if (parameter is not null) throw new ArgumentException("This tab action uses its original Files view.");
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, _lifetime.Token);
        await _operations.WaitAsync(linked.Token);
        List<Exception> failures = [];
        Exception? handled = null;
        var focusItems = false;
        FolderTab? attemptedTab = null;
        var ownerWasCurrent = false;
        void RequireOriginalTab()
        {
            RequireOriginalAlive(linked.Token);
            if (!ReferenceEquals(_activeFolderTab, originalTab) || !ReferenceEquals(_page, originalPage)
                || !ReferenceEquals(_items.SelectedItem, originalSelection) || _pendingMutation is not null
                || !string.IsNullOrWhiteSpace(_editName.Text) || _folderTabs.Count != originalRows.Length
                || !_folderTabs.SequenceEqual(originalRows) || _query != originalState.Query
                || (_search.Text ?? "") != originalState.SearchText || _sort.SelectedIndex != originalState.SortIndex
                || _historyIndex != originalState.HistoryIndex || !_history.SequenceEqual(originalState.History)
                || !ReferenceEquals(_moveSource, originalMove))
                throw new InvalidOperationException("The original Files tab changed before navigation.");
        }
        try
        {
            if (!FolderTabActionAvailable(command)) return;
            RequireOriginalTab();
            _busy = true; Changed(); RequireOriginalAlive(linked.Token);
            await RequireReadyAsync(linked.Token);
            var originalOwner = _originalPageCurrent ?? _folderTabsOwnerCurrent
                ?? throw new UnauthorizedAccessException("The original Files tab owner is unavailable.");
            await RequireOriginalOwnerAsync(originalOwner, linked.Token);
            ownerWasCurrent = true;
            RequireOriginalTab();
            if (command == "9to1.Files.Tab.Close" && !ReferenceEquals(requested, originalTab))
            {
                // Removing an inactive view performs no resource read, activation or Files mutation.
                _folderTabs.Remove(requested!); _status = "Tab closed. Its files are unchanged."; return;
            }
            var adding = command is "9to1.Files.Tab.New" or "9to1.Files.Tab.OpenFolder";
            FolderTab? target = requested;
            FolderTabState targetState;
            if (adding)
            {
                var history = new List<(Guid? ID, string Title, bool IsTrash)> { (null, "Files", false) };
                if (command == "9to1.Files.Tab.OpenFolder")
                {
                    if (originalPage is not { IsTrash: false } || originalSelection is not { Kind: HostedItemKind.Folder }
                        || !originalPage.Items.Any(row => ReferenceEquals(row, originalSelection)))
                        throw new InvalidOperationException("Select an original folder to open in a new tab.");
                    await _browser.RevalidateAsync(originalPage, _originalActor, linked.Token);
                    RequireOriginalTab();
                    history.Add((originalSelection.Id.Value, originalSelection.Name, false));
                }
                targetState = new(originalState.StoreId, Array.AsReadOnly(history.ToArray()), history.Count - 1, "", "", originalState.SortIndex, null);
            }
            else
            {
                if (command is "9to1.Files.Tab.Next" or "9to1.Files.Tab.Previous")
                {
                    var step = command == "9to1.Files.Tab.Next" ? 1 : _folderTabs.Count - 1;
                    target = _folderTabs[(_folderTabs.IndexOf(originalTab) + step) % _folderTabs.Count];
                }
                else if (command == "9to1.Files.Tab.Close")
                {
                    var index = _folderTabs.IndexOf(originalTab);
                    target = _folderTabs[index + 1 < _folderTabs.Count ? index + 1 : index - 1];
                }
                targetState = target?.State ?? throw new InvalidOperationException("The original destination tab is unavailable.");
            }
            if (targetState.StoreId != originalState.StoreId || targetState.StoreId != _boundStoreId)
                throw new UnauthorizedAccessException("This tab belongs to a different original Files store.");
            attemptedTab = target;
            var destination = TabLocation(targetState);
            var page = await _browser.ListAsync(_originalActor, destination.ID, targetState.Query, token: linked.Token,
                expectedStoreId: targetState.StoreId, isTrash: destination.IsTrash);
            await _browser.RevalidateAsync(page, _originalActor, linked.Token);
            var current = await _browser.CaptureOriginalPageReadCheckAsync(page, _originalActor, OriginalAlive, linked.Token);
            await RequireReadyAsync(linked.Token);
            await RequireOriginalOwnerAsync(current, linked.Token);
            RequireOriginalTab();
            // Publish only after all owning reads settle. No remembered page/grant is reactivated.
            originalTab.State = originalState;
            if (adding)
            {
                target = new FolderTab(++_nextFolderTabId, targetState);
                _folderTabs.Add(target);
            }
            else if (command == "9to1.Files.Tab.Close") _folderTabs.Remove(originalTab);
            var activated = target ?? throw new InvalidOperationException("The destination tab was not retained.");
            _activeFolderTab = activated;
            activated.LastError = null;
            _sharingPage = null;
            Interlocked.Increment(ref _sharingSelectionVersion);
            _page = page; _originalPageCurrent = current;
            _history.Clear(); _history.AddRange(targetState.History); _historyIndex = targetState.HistoryIndex;
            _history[_historyIndex] = (destination.ID, page.FolderTitle, destination.IsTrash);
            _query = targetState.Query; _search.Text = targetState.SearchText; _sort.SelectedIndex = targetState.SortIndex;
            _items.SelectedItem = null; ApplyDisplayedNameSort();
            _items.SelectedItem = targetState.Selection is { } selection
                ? page.Items.SingleOrDefault(row => row.Id == selection) : null;
            RequireOriginalAlive(linked.Token);
            if (!ReferenceEquals(_activeFolderTab, activated)) throw new InvalidOperationException("The activated tab changed during publication.");
            activated.State = CaptureFolderTabState();
            _status = $"{page.FolderTitle}: {page.Items.Count} items on the first page. {_folderTabs.Count} tabs open.";
            focusItems = true;
        }
        catch (Exception error)
        {
            lock (_originalTasksSync) AddOriginalFailure(error);
            var terminal = error is UnauthorizedAccessException or ObjectDisposedException || !OriginalAlive()
                || error is OperationCanceledException && _lifetime.IsCancellationRequested;
            if (terminal) { lock (_originalTasksSync) _closing = true; Add(failures, error); }
            else handled = error;
            if (!terminal)
            {
                try
                {
                    if (!ownerWasCurrent) throw new UnauthorizedAccessException("The original Files tab owner could not be checked.");
                    var owner = _originalPageCurrent ?? _folderTabsOwnerCurrent
                        ?? throw new UnauthorizedAccessException("The original Files tab owner retired.");
                    await RequireReadyAsync(linked.Token);
                    await RequireOriginalOwnerAsync(owner, linked.Token);
                }
                catch (Exception ownerError)
                {
                    terminal = true;
                    lock (_originalTasksSync) _closing = true;
                    Add(failures, error); handled = null; Add(failures, ownerError);
                }
            }
            try
            {
                // Refused reads withdraw all old metadata/grants, while retaining real
                // navigation/search/name input and the original pending move intent.
                var retained = terminal ? null : CaptureFolderTabState();
                var retainedName = _editName.Text;
                var retainedMove = _moveSource;
                if (attemptedTab is not null && _folderTabs.Contains(attemptedTab)) attemptedTab.LastError = error.Message;
                ClearSources();
                if (!terminal && retained is not null && _activeFolderTab is { } active)
                {
                    active.State = retained; RestoreFolderTabNavigation(retained);
                    _editName.Text = retainedName; _moveSource = retainedMove;
                }
                _status = OriginalAlive() ? error.Message : "Files view closed";
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
            try
            {
                Changed();
                if (focusItems) { RequireOriginalAlive(linked.Token); _items.Focus(); RequireOriginalAlive(linked.Token); }
            }
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
            ThrowOriginalFailures(failures, "Original Files tab navigation and cleanup failed.");
        }
    }
}
