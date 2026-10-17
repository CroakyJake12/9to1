using System.ComponentModel;
using CakeOS.Cui;
using CakeOS.Cui.Runtime;
using HavenOS.Files;

namespace NineToOne.Web.Files;

/// <summary>Presentation over the canonical provider. The host supplies its authenticated adapter and actor;
/// this controller does not establish identity, grants, file-byte transport or storage authority.</summary>
public sealed class FilesBrowserController : ICuiWritableBindingContext, ICuiActionDispatcher,
    ICuiActionAvailability, INotifyPropertyChanged, IDisposable
{
    private readonly IFilesProvider _provider;
    private readonly string _actor;
    private readonly CancellationTokenSource _lifetime = new();
    private IReadOnlyList<FilesBrowserRow> _items = [];
    private HostedItemMetadata? _folder, _selected, _editTarget;
    private FilesOperation? _pending;
    private sealed record FolderVisit(HostedItemId? Id, string Query);
    private readonly List<FolderVisit> _history = [];
    private int _historyIndex = -1;
    private string? _cursor;
    private string _name = "", _status = "";
    private string _searchDraft = "", _query = "", _sort = "NameAscending";
    private bool _busy, _disposed, _editing, _creating, _savedNeedsRefresh, _folderUnavailable, _accessDenied;

    public FilesBrowserController(IFilesProvider provider, string authenticatedActor)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentException.ThrowIfNullOrWhiteSpace(authenticatedActor);
        _provider = provider;
        _actor = authenticatedActor;
    }

    private readonly object _issuedGate = new();
    private readonly HashSet<TaskCompletionSource> _issued = [];
    private bool _lifetimeDisposed;
    internal Func<bool>? CommandAdmission { get; set; }
    public bool HasUnsavedChanges => !_disposed && (_editing || _pending is not null || _busy);
    internal Task DrainIssuedAsync()
    {
        lock (_issuedGate) return Task.WhenAll(_issued.Select(work => work.Task).ToArray());
    }
    private IDisposable EnterIssued()
    {
        lock (_issuedGate)
        {
            if (_disposed) throw new OperationCanceledException("Private presentation closed.");
            var work = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _issued.Add(work); return new IssuedLease(this, work);
        }
    }
    private sealed class IssuedLease(FilesBrowserController owner, TaskCompletionSource work) : IDisposable
    {
        public void Dispose()
        {
            lock (owner._issuedGate) { owner._issued.Remove(work); work.TrySetResult(); }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public HostedItemId? CurrentFolderId => _folder?.Id;
    public HostedItemMetadata? SelectedItem => _selected;
    public IReadOnlyList<FilesBrowserRow> Items => _items;

    public async Task InitializeAsync(HostedItemId? folderId, CancellationToken cancellationToken)
    {
        using var issued = EnterIssued();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        if (!_provider.Location.IsAvailable || !_provider.Location.Capabilities.Supports(FilesProviderCapabilities.Read))
            throw new InvalidOperationException("Files location is unavailable.");
        await NavigateAsync(folderId, "", linked.Token);
        Changed();
    }

    public bool TryGetValue(string path, out object? value)
    {
        value = path switch
        {
            "Items" => _items,
            "LocationName" => _accessDenied || _disposed ? "Files" : _provider.Location.Name,
            "FolderName" => _accessDenied || _disposed ? "Files" : _folder?.Name ?? _provider.Location.Name,
            "Status" => _status,
            "Empty" => !_busy && _items.Count == 0,
            "EmptyMessage" => _query.Length == 0 ? "This folder is empty." : "No names match this search in this folder.",
            "SearchText" => _searchDraft,
            "SearchSummary" => _query.Length == 0 ? "All items in this folder" : $"Names containing: {_query}",
            "SortDescription" => _sort switch { "NameDescending" => "Loaded items: name Z–A", "Newest" => "Loaded items: newest first", _ => "Loaded items: name A–Z" },
            "Editing" => _editing,
            "EditTitle" => _creating ? "New folder" : "Rename selected item",
            "Name" => _name,
            "SelectedName" => _selected?.Name ?? "Select an item to see its details.",
            "SelectedDetails" => _selected is null ? "" : $"{_selected.Kind} · {_selected.Availability}",
            "CanBrowse" => Available("Refresh"),
            "CanCreate" => Available("NewFolder"),
            "CanRename" => Available("Rename"),
            "CanOpen" => Available("Open"),
            "CanUp" => Available("Up"),
            "CanBack" => Available("Back"),
            "CanForward" => Available("Forward"),
            "CanSearch" => Available("Search"),
            "CanClearSearch" => Available("ClearSearch"),
            "CanMore" => Available("More"),
            "CanSave" => Available("Save"),
            "CanCancel" => Available("Cancel"),
            "CanEditName" => !_disposed && !_busy && _editing && _pending is null,
            _ => null
        };
        return path is "Items" or "LocationName" or "FolderName" or "Status" or "Empty" or "EmptyMessage" or "Editing"
            or "SearchText" or "SearchSummary" or "SortDescription" or "CanBack" or "CanForward" or "CanSearch" or "CanClearSearch"
            or "EditTitle" or "Name" or "SelectedName" or "SelectedDetails" or "CanBrowse" or "CanCreate"
            or "CanRename" or "CanOpen" or "CanUp" or "CanMore" or "CanSave" or "CanCancel" or "CanEditName";
    }

    public bool TrySetValue(string path, object? value)
    {
        if (CommandAdmission?.Invoke() == false) return false;
        if (path == "SearchText" && value is string search && search.Length <= 256 && Available("Search"))
        { _searchDraft = search; Changed(); return true; }
        if (path != "Name" || value is not string name || _disposed || _busy || !_editing || _pending is not null)
            return false;
        _name = name;
        Changed();
        return true;
    }

    public bool? IsActionAvailable(string command) => CommandAdmission?.Invoke() == false ? false : Available(command);

    private bool Available(string command)
    {
        if (_disposed || _accessDenied || _busy || !_provider.Location.IsAvailable) return false;
        var capabilities = _provider.Location.Capabilities;
        return command switch
        {
            "Select" or "Refresh" => !_editing && capabilities.Supports(FilesProviderCapabilities.Read),
            "Open" => !_editing && _selected?.Kind == HostedItemKind.Folder,
            "Up" => !_editing && _folder is not null,
            "Home" => !_editing && capabilities.Supports(FilesProviderCapabilities.Read),
            "Back" => !_editing && _historyIndex > 0 && capabilities.Supports(FilesProviderCapabilities.Read),
            "Forward" => !_editing && _historyIndex + 1 < _history.Count && capabilities.Supports(FilesProviderCapabilities.Read),
            "Search" => !_editing && !_folderUnavailable && capabilities.Supports(FilesProviderCapabilities.Read | FilesProviderCapabilities.Search),
            "ClearSearch" => !_editing && (_query.Length != 0 || _searchDraft.Length != 0) && capabilities.Supports(FilesProviderCapabilities.Read),
            "SortNameAscending" or "SortNameDescending" or "SortNewest" => !_editing && capabilities.Supports(FilesProviderCapabilities.Read),
            "More" => !_editing && _cursor is not null,
            "NewFolder" => !_editing && !_folderUnavailable && capabilities.Supports(FilesProviderCapabilities.Write),
            "Rename" => !_editing && _selected is not null && capabilities.Supports(FilesProviderCapabilities.Rename),
            "Save" => _editing && !string.IsNullOrWhiteSpace(_name),
            // An uncertain commit must be retried with its original operation key before discarding it.
            "Cancel" => _editing && _pending is null,
            _ => false
        };
    }

    public async ValueTask DispatchAsync(string command, object? parameter, CancellationToken cancellationToken = default)
    {
        if (CommandAdmission?.Invoke() == false || !Available(command)) return;
        using var issued = EnterIssued();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        var ct = linked.Token;
        _busy = true;
        _savedNeedsRefresh = false;
        try
        {
            Changed();
            ct.ThrowIfCancellationRequested();
            switch (command)
            {
                case "Select":
                    if (parameter is FilesBrowserRow row && _items.Any(item => ReferenceEquals(item, row)))
                    {
                        var result = await _provider.GetAsync(row.Metadata.Id, ct);
                        ct.ThrowIfCancellationRequested();
                        if (result.IsSuccess && result.Value is { } current && IsCurrentRow(current)) _selected = current;
                        else if (result.IsSuccess) { _selected = null; _status = "This item moved or changed. Refresh the folder."; }
                        else if (IsDenied(result.Error!)) ClearDeniedPresentation();
                        else { _selected = null; _status = Describe(result.Error!); }
                    }
                    break;
                case "Refresh": await ReadPageAsync(false, ct); break;
                case "More": await ReadPageAsync(true, ct); break;
                case "Open": await NavigateAsync(_selected!.Id, "", ct); break;
                case "Up": await NavigateAsync(_folder!.ParentId, "", ct); break;
                case "Home": await NavigateAsync(null, "", ct); break;
                case "Back": await NavigateHistoryAsync(_historyIndex - 1, ct); break;
                case "Forward": await NavigateHistoryAsync(_historyIndex + 1, ct); break;
                case "Search": await NavigateAsync(_folder?.Id, _searchDraft.Trim(), ct); break;
                case "ClearSearch": await NavigateAsync(_folder?.Id, "", ct); break;
                case "SortNameAscending": _sort = "NameAscending"; SortItems(); break;
                case "SortNameDescending": _sort = "NameDescending"; SortItems(); break;
                case "SortNewest": _sort = "Newest"; SortItems(); break;
                case "NewFolder":
                    _editing = _creating = true; _name = ""; _editTarget = null; _status = "Choose a folder name."; break;
                case "Rename":
                    _editing = true; _creating = false; _editTarget = _selected; _name = _selected!.Name;
                    _status = "Edit the name, then save."; break;
                case "Cancel": EndEdit(); _status = "Edits discarded."; break;
                case "Save": await SaveAsync(ct); break;
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            if (!_disposed) _status = _savedNeedsRefresh ? "Saved. Refresh to reload the folder." : _pending is null ? "Operation cancelled." : "The save outcome is unknown. Retry Save to check the same operation.";
        }
        catch (UnauthorizedAccessException)
        {
            ClearDeniedPresentation();
        }
        catch
        {
            if (!_disposed) _status = _savedNeedsRefresh ? "Saved. Refresh to reload the folder." : _pending is null ? "Files is unavailable. Retry the action." : "The save outcome is unknown. Retry Save to check the same operation.";
        }
        finally { _busy = false; if (!_disposed) Changed(); }
    }

    private async Task SaveAsync(CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        _pending ??= new FilesOperation(new(Guid.NewGuid()), _actor,
            _creating ? HostedItemId.New() : _editTarget!.Id,
            _creating ? null : _editTarget!.ParentId,
            _creating ? _folder?.Id : null,
            _creating ? "CreateFolder" : "Rename", _editTarget?.CurrentRevisionId, null,
            FilesOperationState.Pending, now, now, null, null, Payload: new(NewName: _name));
        var result = await _provider.MutateAsync(_pending, _pending.Payload!.NewName, ct);
        ct.ThrowIfCancellationRequested();
        if (!result.IsSuccess)
        {
            var error = result.Error!;
            if (IsDenied(error)) { ClearDeniedPresentation(); return; }
            if (IsAuthoritativeRejection(error))
            { _pending = null; _status = Describe(error); }
            else _status = "The save outcome is unknown. Retry Save to check the same operation.";
            return;
        }
        var committed = result.Value;
        if (committed is null || committed.State != FilesOperationState.Committed || committed.ResultRevisionId is null || committed.ResultRevisionId.Value.Value == Guid.Empty ||
            committed.Id != _pending.Id || committed.ItemId != _pending.ItemId || committed.Operation != _pending.Operation ||
            committed.ActorId != _pending.ActorId || committed.Payload?.NewName != _pending.Payload?.NewName || committed.Error is not null ||
            committed.BaseRevisionId != _pending.BaseRevisionId || committed.DestinationParentId != _pending.DestinationParentId)
            throw new InvalidOperationException("Provider did not acknowledge a committed revision.");
        var id = committed.ItemId;
        EndEdit();
        _savedNeedsRefresh = true;
        // A committed operation is never presented as unsaved because its follow-up list failed.
        _status = "Saved. Refresh to reload the folder if needed.";
        await ReadPageAsync(false, ct);
        if (_accessDenied) return;
        var saved = await _provider.GetAsync(id, ct);
        ct.ThrowIfCancellationRequested();
        if (saved.Error is { } savedError && IsDenied(savedError)) { ClearDeniedPresentation(); return; }
        _selected = saved.IsSuccess && saved.Value is { } item && IsCurrentRow(item) ? item : null;
        _status = saved.IsSuccess ? "Saved." : "Saved. " + Describe(saved.Error!);
        _savedNeedsRefresh = false;
    }

    private async Task NavigateAsync(HostedItemId? id, string query, CancellationToken ct)
    {
        await OpenFolderAsync(id, query, ct);
        var visit = new FolderVisit(id, query);
        if (_historyIndex >= 0 && _history[_historyIndex] == visit) return;
        _history.RemoveRange(_historyIndex + 1, _history.Count - _historyIndex - 1);
        if (_history.Count == 128) _history.RemoveAt(0);
        _history.Add(visit);
        _historyIndex = _history.Count - 1;
    }

    private async Task NavigateHistoryAsync(int index, CancellationToken ct)
    {
        var visit = _history[index];
        await OpenFolderAsync(visit.Id, visit.Query, ct);
        _historyIndex = index;
    }

    private async Task OpenFolderAsync(HostedItemId? id, string query, CancellationToken ct)
    {
        if (query.Length != 0 && !_provider.Location.Capabilities.Supports(FilesProviderCapabilities.Search))
            throw new InvalidOperationException("This provider no longer supports search.");
        HostedItemMetadata? folder = null;
        if (id is { } folderId)
        {
            var result = await _provider.GetAsync(folderId, ct);
            ct.ThrowIfCancellationRequested();
            if (!result.IsSuccess)
            {
                if (IsDenied(result.Error!)) throw new UnauthorizedAccessException();
                throw new InvalidOperationException(result.Error!.Code.ToString());
            }
            folder = result.Value;
            if (folder?.Kind != HostedItemKind.Folder) throw new InvalidOperationException("Destination is not a folder.");
        }
        var page = await _provider.ListAsync(id, SearchQuery(query, id), null, ct);
        ct.ThrowIfCancellationRequested();
        if (page.Items.Count > 500) throw new InvalidOperationException("The provider returned an oversized Files page.");
        _folder = folder; _folderUnavailable = false; _selected = null;
        _query = _searchDraft = query;
        _items = page.Items.Select(item => new FilesBrowserRow(item)).ToArray(); _cursor = page.NextPageToken;
        SortItems();
        _status = "Folder opened.";
    }

    private async Task ReadPageAsync(bool append, CancellationToken ct)
    {
        if (_folder is not null)
        {
            var current = await _provider.GetAsync(_folder.Id, ct);
            ct.ThrowIfCancellationRequested();
            if (!current.IsSuccess || current.Value?.Kind != HostedItemKind.Folder)
            {
                if (current.Error is { } errorDenied && IsDenied(errorDenied)) { ClearDeniedPresentation(); return; }
                _folderUnavailable = true; _items = []; _selected = null; _cursor = null;
                _status = current.Error is { } error ? Describe(error) : "The open folder is unavailable.";
                return;
            }
            _folder = current.Value; _folderUnavailable = false;
        }
        // Keep rendering bounded; each explicit page replaces the previous page beyond 500 items.
        var page = await _provider.ListAsync(_folder?.Id, SearchQuery(_query, _folder?.Id), append ? _cursor : null, ct);
        ct.ThrowIfCancellationRequested();
        if (page.Items.Count > 500) throw new InvalidOperationException("The provider returned an oversized Files page.");
        var rows = page.Items.Select(item => new FilesBrowserRow(item));
        _items = (append && _items.Count + page.Items.Count <= 500 ? _items.Concat(rows) : rows)
            .DistinctBy(row => row.Metadata.Id).Take(500).ToArray();
        SortItems();
        _cursor = page.NextPageToken;
        if (_selected is not null)
        {
            var selected = await _provider.GetAsync(_selected.Id, ct);
            ct.ThrowIfCancellationRequested();
            if (selected.Error is { } selectedError && IsDenied(selectedError)) { ClearDeniedPresentation(); return; }
            _selected = selected.IsSuccess && selected.Value is { } item && IsCurrentRow(item) ? item : null;
        }
        _status = "Folder refreshed.";
    }

    private FilesSearchQuery? SearchQuery(string query, HostedItemId? parentId)
    {
        if (query.Length == 0) return null;
        if (!_provider.Location.Capabilities.Supports(FilesProviderCapabilities.Search))
            throw new InvalidOperationException("This provider no longer supports search.");
        return new(query, _provider.Location.Id, parentId, Limit: 100);
    }
    private bool IsCurrentRow(HostedItemMetadata item) => item.LocationId == _provider.Location.Id &&
        item.ParentId == _folder?.Id && _items.Any(row => row.Metadata.Id == item.Id) &&
        (_query.Length == 0 || item.Name.Contains(_query, StringComparison.OrdinalIgnoreCase));
    private void SortItems()
    {
        _items = (_sort switch
        {
            "NameDescending" => _items.OrderByDescending(row => row.Metadata.Name, StringComparer.OrdinalIgnoreCase),
            "Newest" => _items.OrderByDescending(row => row.Metadata.ModifiedAt),
            _ => _items.OrderBy(row => row.Metadata.Name, StringComparer.OrdinalIgnoreCase)
        }).ThenBy(row => row.Metadata.Id.Value).ToArray();
    }

    private void EndEdit() { _editing = _creating = false; _editTarget = null; _pending = null; _name = ""; }
    private static bool IsDenied(FilesError error) => error.Code is FilesErrorCode.PermissionDenied or FilesErrorCode.PermissionRequired;
    private static bool IsAuthoritativeRejection(FilesError error) => error.Code is
        FilesErrorCode.ItemNotFound or FilesErrorCode.RevisionConflict or FilesErrorCode.NameConflict or
        FilesErrorCode.InvalidName or FilesErrorCode.InvalidState or FilesErrorCode.ProviderCapabilityUnsupported;
    private void ClearDeniedPresentation()
    {
        _accessDenied = true; _items = []; _selected = _folder = null; _cursor = null; EndEdit();
        ClearNavigation();
        _status = "Files access was denied. Reopen Files after signing in.";
    }
    private static string Describe(FilesError error) => error.Code == FilesErrorCode.RevisionConflict
        ? "This item changed. Your name is retained. Cancel, refresh, then rename the latest item."
        : $"{error.Code}: {error.Message}";
    private void Changed()
    {
        // Presentation failures cannot change an owner commit or stop later observers.
        if (PropertyChanged is not { } observers) return;
        foreach (PropertyChangedEventHandler observer in observers.GetInvocationList())
        {
            try { observer(this, new PropertyChangedEventArgs(null)); }
            catch (Exception error) { System.Diagnostics.Trace.TraceWarning("Files presentation observer failed: {0}", error.GetType().Name); }
        }
    }

    internal void ClearPrivatePresentation()
    {
        _disposed = true; _items = []; _selected = _folder = null; _cursor = null; EndEdit(); _status = "";
        ClearNavigation();
    }
    private void ClearNavigation() { _history.Clear(); _historyIndex = -1; _query = _searchDraft = ""; }
    public void Dispose()
    {
        lock (_issuedGate)
        {
            if (_lifetimeDisposed) return;
            _lifetimeDisposed = true;
        }
        ClearPrivatePresentation();
        try { _lifetime.Cancel(); }
        catch (Exception error) { System.Diagnostics.Trace.TraceWarning("Files owner cancellation callback failed: {0}", error.GetType().Name); }
        finally { _lifetime.Dispose(); }
    }
}

public sealed record FilesBrowserRow(HostedItemMetadata Metadata) : ICuiBindingContext
{
    public bool TryGetValue(string path, out object? value)
    {
        value = path switch { "Id" => Metadata.Id.ToString(), "Name" => Metadata.Name,
            "Details" => $"{Metadata.Kind} · {Metadata.Availability}", _ => null };
        return path is "Id" or "Name" or "Details";
    }
}
