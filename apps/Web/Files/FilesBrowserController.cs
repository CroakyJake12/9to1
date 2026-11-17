using System.ComponentModel;
using CakeOS.Cui;
using CakeOS.Cui.Runtime;
using HavenOS.Files;

namespace NineToOne.Web.Files;

// Optional controls for a connected cloud owner. Local provider fixtures retain the original Files path.
internal interface IFilesBrowserDrive
{
    FilesDriveContext Context { get; }
    Task<FilesDriveContext> RefreshContextAsync(CancellationToken cancellationToken);
    Task<FilesDriveUsage> ReadUsageAsync(CancellationToken cancellationToken);
    Task ConnectAsync(CancellationToken cancellationToken);
    Task<FilesDriveDisconnectOutcome> DisconnectAsync(CancellationToken cancellationToken);
    void OpenPrivacyNotice();
    Task<FilesPage<HostedItemMetadata>> ListTrashAsync(string? pageToken, CancellationToken cancellationToken);
    Task<FilesBrowserUploadFile?> PickUploadAsync(CancellationToken cancellationToken);
    Task<FilesBrowserUploadProgress> BeginUploadAsync(FilesOperationId operationId, HostedItemId? parentId,
        FilesBrowserUploadFile file, CancellationToken cancellationToken);
    Task<FilesBrowserUploadProgress> ReadUploadAsync(FilesTransferId transferId, CancellationToken cancellationToken);
    Task<FilesBrowserUploadProgress?> FindUploadAsync(FilesOperationId operationId, CancellationToken cancellationToken);
    Task<FilesBrowserUploadProgress> WriteUploadChunkAsync(FilesTransferId transferId, string handle, long offset, int length,
        CancellationToken cancellationToken);
    Task<FilesBrowserUploadProgress> CancelUploadAsync(FilesTransferId transferId, CancellationToken cancellationToken);
    void ReleaseUpload(string handle);
    Task<FilesBrowserDownload> PrepareDownloadAsync(HostedItemId itemId, string? exportMime, CancellationToken cancellationToken);
    void OpenDownload(FilesBrowserDownload download);
}

internal enum FilesDriveDisconnectOutcome { Disconnected, GoogleRevocationUnconfirmed }

internal sealed record FilesDriveContext(string AccountId, string DisplayName, bool Connected,
    FilesLocation Location, FilesDriveUsage? Usage)
{
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(AccountId) || Location is null || Location.Id.Value == Guid.Empty ||
            Location.Kind != FilesLocationKind.ConnectedCloud ||
            (Connected && !Location.Capabilities.Supports(FilesProviderCapabilities.Read)))
            throw new InvalidOperationException("Files returned an invalid account or Drive location.");
    }
}

internal sealed record FilesDriveBudget(long Limit, long Used, long Remaining, long? ResetAt);

internal sealed record FilesBrowserUploadFile(string Handle, string Name, string ContentType, long SizeBytes);

internal sealed record FilesBrowserUploadProgress(FilesTransferId TransferId, HostedItemId ItemId,
    int ChunkBytes, long UploadedBytes, long TotalBytes, FilesTransferState State);

internal sealed record FilesBrowserDownload(FilesTransferId TransferId, string Url);

internal sealed record FilesDriveUsage(long AsOf, FilesDriveBudget UnitsPerMinute, FilesDriveBudget UnitsPerDay,
    FilesDriveBudget UploadBytesPerDay, FilesDriveBudget DownloadBytesPerDay, bool Paused = false)
{
    public static DateTimeOffset? AsTime(long? epochMilliseconds) =>
        epochMilliseconds is > 0 and <= 253402300799999 ? DateTimeOffset.FromUnixTimeMilliseconds(epochMilliseconds.Value) : null;
}

internal sealed class FilesBrowserRequestException(FilesError error) : Exception(error.Message)
{
    public FilesError Error => error;
    public string? ServiceCode { get; init; }

    public static FilesBrowserRequestException PermissionDenied() => new(new(
        FilesErrorCode.PermissionDenied, "Sign in to your 9to1 Account to open Files.", "Access", null, false, false));
}

/// <summary>Presentation over the canonical provider. The host supplies its authenticated adapter and actor;
/// this controller does not establish identity, grants, file-byte transport or storage authority.</summary>
public sealed class FilesBrowserController : ICuiWritableBindingContext, ICuiActionDispatcher,
    ICuiActionAvailability, INotifyPropertyChanged, IDisposable
{
    private readonly IFilesProvider _provider;
    private readonly IFilesBrowserDrive? _drive;
    private readonly string _actor;
    private readonly CancellationTokenSource _lifetime = new();
    private IReadOnlyList<FilesBrowserRow> _items = [];
    private HostedItemMetadata? _folder, _selected, _editTarget;
    private FilesOperation? _pending;
    private sealed record FolderVisit(HostedItemId? Id, string Query);
    private readonly List<FolderVisit> _history = [];
    private readonly Dictionary<HostedItemId, HostedItemMetadata> _visitedFolders = [];
    private int _historyIndex = -1;
    private string? _cursor;
    private string _name = "", _status = "";
    private string _searchDraft = "", _query = "", _sort = "NameAscending";
    private bool _listView;
    private string _operation = "";
    private HostedItemMetadata? _moveTarget;
    private bool _inTrash;
    private UploadIntent? _upload;
    private CancellationTokenSource? _uploadRequest;
    private FilesBrowserDownload? _preparedDownload;
    private HostedItemId? _preparedDownloadItem;
    private bool _busy, _disposed, _editing, _creating, _savedNeedsRefresh, _folderUnavailable, _accessDenied;
    private FilesDriveUsage? _usage;
    private bool _driveConnectionUnverified;

    private sealed record UploadIntent(FilesOperationId OperationId, HostedItemId? ParentId, FilesBrowserUploadFile File)
    {
        public FilesBrowserUploadProgress? Progress { get; set; }
    }

    public FilesBrowserController(IFilesProvider provider, string authenticatedActor)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentException.ThrowIfNullOrWhiteSpace(authenticatedActor);
        _provider = provider;
        _drive = provider as IFilesBrowserDrive;
        _actor = authenticatedActor;
    }

    private readonly object _issuedGate = new();
    private readonly HashSet<TaskCompletionSource> _issued = [];
    private bool _lifetimeDisposed;
    internal Func<bool>? CommandAdmission { get; set; }
    public bool HasUnsavedChanges => !_disposed && (_editing || _moveTarget is not null || _pending is not null || _upload is not null || _busy);
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
        if (_drive is not null)
        {
            try
            {
                var context = await _drive.RefreshContextAsync(linked.Token);
                _usage = context.Usage;
                if (!context.Connected)
                {
                    _status = "Connect Google Drive to browse your files.";
                    Changed();
                    return;
                }
                if (!context.Location.IsAvailable)
                {
                    _status = "Google Drive is unavailable. Refresh the connection to try again.";
                    Changed();
                    return;
                }
                await NavigateAsync(folderId, "", linked.Token);
            }
            catch (UnauthorizedAccessException) { ClearDeniedPresentation(); }
            catch (FilesBrowserRequestException error) { await ShowRequestErrorAsync(error.Error, linked.Token); }
            Changed();
            return;
        }
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
            "Breadcrumb" => Breadcrumb(),
            "ShowDriveConnection" => _drive is not null && !_accessDenied && !_disposed,
            "DriveConnected" => !_driveConnectionUnverified && _drive?.Context.Connected == true,
            "DriveDisconnected" => !_driveConnectionUnverified && _drive?.Context.Connected == false,
            "DriveConnection" => _drive is null || _accessDenied || _disposed ? "" : _driveConnectionUnverified
                ? "Google Drive connection status is unknown." : _drive.Context.Connected
                ? $"Google Drive connected for {_drive.Context.DisplayName}."
                : "Google Drive is disconnected.",
            "ShowBrowser" => !_accessDenied && !_disposed && !_driveConnectionUnverified && (_drive?.Context.Connected ?? true),
            "CanConnect" => Available("ConnectDrive"),
            "CanDisconnect" => Available("DisconnectDrive"),
            "CanRefreshConnection" => Available("RefreshDriveConnection"),
            "CanRefreshUsage" => Available("RefreshDriveUsage"),
            "CanOpenFilesPrivacy" => Available("OpenFilesPrivacy"),
            "UsageSummary" => UsageSummary(),
            "Empty" => !_busy && !_folderUnavailable && (_inTrash || _historyIndex >= 0) && _items.Count == 0,
            "EmptyMessage" => _inTrash ? "Trash is empty." :
                _query.Length == 0 ? "This folder is empty." :
                _cursor is not null ? "No matches in this page. Continue to check more items." : "No names match this search in this folder.",
            "SearchText" => _searchDraft,
            "SearchSummary" => _query.Length == 0 ? "All items in this folder" : $"Names containing: {_query}",
            "ShowFolderSearch" => !_inTrash,
            "SortDescription" => _sort switch { "NameDescending" => "Loaded items: name Z–A", "Newest" => "Loaded items: newest first", _ => "Loaded items: name A–Z" },
            "ShowAsGrid" => !_listView,
            "ShowAsList" => _listView,
            "ViewToggleLabel" => _listView ? "Grid" : "Details",
            "ItemCount" => _items.Count == 1 ? "1 item" : $"{_items.Count} items",
            "Editing" => _editing,
            "EditTitle" => _operation switch
            {
                "CreateFolder" => "New folder", "Delete" => $"Move {_editTarget?.Name} to Trash?",
                "Restore" => $"Restore {_editTarget?.Name} to its original folder?",
                "Move" => $"Move {_editTarget?.Name} to {_folder?.Name ?? _provider.Location.Name}?",
                _ => "Rename selected item",
            },
            "EditNotice" => _operation == "Delete" ? "You can recover this item from Trash." : "",
            "ShowEditNotice" => _operation == "Delete",
            "EditingName" => _editing && UsesName,
            "SaveLabel" => _pending is not null ? "Retry" : _operation switch
            {
                "Delete" => "Move to Trash", "Restore" => "Restore", "Move" => "Move here", _ => "Save",
            },
            "InTrash" => _inTrash,
            "ShowTrashEntry" => _drive is not null && !_inTrash && _provider.Location.Capabilities.Supports(FilesProviderCapabilities.Trash),
            "CanEnterTrash" => Available("ShowTrash"),
            "CanExitTrash" => Available("ExitTrash"),
            "CanTrash" => Available("Trash"),
            "CanRestore" => Available("Restore"),
            "ShowMove" => _drive is not null && !_inTrash && _provider.Location.Capabilities.Supports(FilesProviderCapabilities.Move),
            "CanMove" => Available("Move"),
            "CanMoveHere" => Available("MoveHere"),
            "CanCancelMove" => Available("CancelMove"),
            "ChoosingMoveDestination" => _moveTarget is not null && !_editing,
            "MovePrompt" => $"Choose the destination for {_moveTarget?.Name}.",
            "Name" => _name,
            "SelectedName" => _selected?.Name ?? "Select an item to see its details.",
            "SelectedDetails" => _selected is null ? "" : ItemDetails(_selected),
            "CanBrowse" => Available("Refresh"),
            "CanCreate" => Available("NewFolder"),
            "CanRename" => Available("Rename"),
            "CanOpen" => Available("Open"),
            "ShowUpload" => _drive is not null && !_inTrash && _provider.Location.Capabilities
                .Supports(FilesProviderCapabilities.Write | FilesProviderCapabilities.ResumableTransfers),
            "CanUpload" => Available("Upload"),
            "UploadPending" => _upload is not null,
            "UploadSummary" => _upload is { } upload
                ? $"{upload.File.Name} · {FormatBytes(upload.Progress?.UploadedBytes ?? 0)} of {FormatBytes(upload.File.SizeBytes)} acknowledged"
                : "",
            "CanPauseUpload" => Available("PauseUpload"),
            "ShowUploadRunning" => _uploadRequest is not null,
            "ShowUploadRecovery" => _upload is not null && _uploadRequest is null,
            "CanResumeUpload" => Available("ResumeUpload"),
            "CanCancelUpload" => Available("CancelUpload"),
            "ShowDownload" => _drive is not null && !_inTrash && _provider.Location.Capabilities.Supports(FilesProviderCapabilities.Streaming),
            "CanPrepareDownload" => Available("PrepareDownload"),
            "DownloadLabel" => TryDownloadFormat(_selected, out var exportMime) && exportMime is not null
                ? exportMime == PdfMime ? "Prepare PDF export" : "Prepare Excel export" : "Prepare download",
            "DownloadPrepared" => _preparedDownload is not null && _selected?.Id == _preparedDownloadItem,
            "CanSaveDownload" => Available("SaveDownload"),
            "CanUp" => Available("Up"),
            "CanBack" => Available("Back"),
            "CanForward" => Available("Forward"),
            "CanSearch" => Available("Search"),
            "CanClearSearch" => Available("ClearSearch"),
            "CanMore" => Available("More"),
            "CanSave" => Available("Save"),
            "CanCancel" => Available("Cancel"),
            "CanEditName" => !_disposed && !_busy && _editing && UsesName && _pending is null,
            _ => null
        };
        return path is "Items" or "LocationName" or "FolderName" or "Status" or "Empty" or "EmptyMessage" or "Editing"
            or "Breadcrumb" or "ShowDriveConnection" or "DriveConnected" or "DriveDisconnected" or "DriveConnection" or "ShowBrowser" or "CanConnect"
            or "CanDisconnect" or "CanRefreshConnection" or "CanRefreshUsage" or "CanOpenFilesPrivacy" or "UsageSummary"
            or "SearchText" or "SearchSummary" or "SortDescription" or "CanBack" or "CanForward" or "CanSearch" or "CanClearSearch"
            or "EditTitle" or "EditNotice" or "ShowEditNotice" or "EditingName" or "SaveLabel" or "InTrash" or "ShowTrashEntry"
            or "CanEnterTrash" or "CanExitTrash" or "CanTrash" or "CanRestore" or "ShowMove" or "CanMove" or "CanMoveHere"
            or "CanCancelMove" or "ChoosingMoveDestination" or "MovePrompt" or "ShowFolderSearch"
            or "ShowUpload" or "CanUpload" or "UploadPending" or "UploadSummary" or "CanPauseUpload" or "CanResumeUpload"
            or "ShowUploadRunning" or "ShowUploadRecovery"
            or "ShowAsGrid" or "ShowAsList" or "ViewToggleLabel" or "ItemCount"
            or "CanCancelUpload" or "ShowDownload" or "CanPrepareDownload" or "DownloadLabel" or "DownloadPrepared" or "CanSaveDownload"
            or "Name" or "SelectedName" or "SelectedDetails" or "CanBrowse" or "CanCreate"
            or "CanRename" or "CanOpen" or "CanUp" or "CanMore" or "CanSave" or "CanCancel" or "CanEditName";
    }

    public bool TrySetValue(string path, object? value)
    {
        if (CommandAdmission?.Invoke() == false) return false;
        if (path == "SearchText" && value is string search && search.Length <= 256 && Available("Search"))
        { _searchDraft = search; Changed(); return true; }
        if (path != "Name" || value is not string name || _disposed || _busy || !_editing || !UsesName || _pending is not null)
            return false;
        _name = name;
        Changed();
        return true;
    }

    public bool? IsActionAvailable(string command) => CommandAdmission?.Invoke() == false ? false : Available(command);

    private bool Available(string command)
    {
        if (_disposed || _accessDenied) return false;
        if (command == "PauseUpload") return _uploadRequest is { IsCancellationRequested: false };
        if (_busy) return false;
        if (_drive is not null)
        {
            switch (command)
            {
                case "ConnectDrive": return !_driveConnectionUnverified && !_drive.Context.Connected && !_editing;
                case "DisconnectDrive": return !_driveConnectionUnverified && _drive.Context.Connected && CanStartEdit;
                case "RefreshDriveConnection": return CanStartEdit;
                case "RefreshDriveUsage": return true;
                case "OpenFilesPrivacy": return true;
            }
            if (_driveConnectionUnverified || !_drive.Context.Connected) return false;
        }
        if (!_provider.Location.IsAvailable) return false;
        var capabilities = _provider.Location.Capabilities;
        return command switch
        {
            "Select" or "Refresh" => !_editing && capabilities.Supports(FilesProviderCapabilities.Read),
            "Open" => !_editing && !_inTrash && _selected?.Kind == HostedItemKind.Folder,
            "Up" => !_editing && !_inTrash && _folder is not null,
            "Home" => !_editing && !_inTrash && capabilities.Supports(FilesProviderCapabilities.Read),
            "Back" => !_editing && !_inTrash && _historyIndex > 0 && capabilities.Supports(FilesProviderCapabilities.Read),
            "Forward" => !_editing && !_inTrash && _historyIndex + 1 < _history.Count && capabilities.Supports(FilesProviderCapabilities.Read),
            "Search" => !_editing && !_inTrash && !_folderUnavailable && capabilities.Supports(FilesProviderCapabilities.Read | FilesProviderCapabilities.Search),
            "ClearSearch" => !_editing && !_inTrash && (_query.Length != 0 || _searchDraft.Length != 0) && capabilities.Supports(FilesProviderCapabilities.Read),
            "SortNameAscending" or "SortNameDescending" or "SortNewest" => !_editing && capabilities.Supports(FilesProviderCapabilities.Read),
            "ToggleView" => !_editing,
            "More" => !_editing && _cursor is not null,
            "NewFolder" => CanStartEdit && !_inTrash && !_folderUnavailable && capabilities.Supports(FilesProviderCapabilities.Write),
            "Rename" => CanStartEdit && !_inTrash && _selected is not null && capabilities.Supports(FilesProviderCapabilities.Rename),
            "Trash" => _drive is not null && CanStartEdit && !_inTrash && _selected?.CurrentRevisionId is not null &&
                capabilities.Supports(FilesProviderCapabilities.Trash),
            "Restore" => _drive is not null && CanStartEdit && _inTrash && _selected?.CurrentRevisionId is not null &&
                capabilities.Supports(FilesProviderCapabilities.Trash),
            "ShowTrash" => _drive is not null && CanStartEdit && !_inTrash && capabilities.Supports(FilesProviderCapabilities.Trash),
            "ExitTrash" => CanStartEdit && _inTrash,
            "Move" => _drive is not null && CanStartEdit && !_inTrash && _selected?.CurrentRevisionId is not null &&
                capabilities.Supports(FilesProviderCapabilities.Move),
            "MoveHere" => !_editing && !_inTrash && !_folderUnavailable && _moveTarget is { } item &&
                _folder?.Id != item.ParentId && _folder?.Id != item.Id && capabilities.Supports(FilesProviderCapabilities.Move),
            "CancelMove" => !_editing && _moveTarget is not null,
            "Upload" => _drive is not null && CanStartEdit && !_inTrash && !_folderUnavailable && _historyIndex >= 0 &&
                capabilities.Supports(FilesProviderCapabilities.Write | FilesProviderCapabilities.ResumableTransfers),
            "ResumeUpload" or "CancelUpload" => _drive is not null && _upload is not null,
            "PrepareDownload" => _drive is not null && CanStartEdit && !_inTrash &&
                capabilities.Supports(FilesProviderCapabilities.Read | FilesProviderCapabilities.Streaming) &&
                TryDownloadFormat(_selected, out _),
            "SaveDownload" => _drive is not null && CanStartEdit && !_inTrash && _preparedDownload is not null &&
                _selected?.Id == _preparedDownloadItem && capabilities.Supports(FilesProviderCapabilities.Streaming),
            "Save" => _editing && (!UsesName || !string.IsNullOrWhiteSpace(_name)),
            // An uncertain commit must be retried with its original operation key before discarding it.
            "Cancel" => _editing && _pending is null,
            _ => false
        };
    }
    private bool CanStartEdit => !_editing && _moveTarget is null && _upload is null;
    private bool UsesName => _operation is "CreateFolder" or "Rename";

    public async ValueTask DispatchAsync(string command, object? parameter, CancellationToken cancellationToken = default)
    {
        if (CommandAdmission?.Invoke() == false || !Available(command)) return;
        if (command == "PauseUpload")
        {
            _uploadRequest!.Cancel();
            Changed();
            return;
        }
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
                case "ConnectDrive":
                    _status = "Opening Google authorization…";
                    Changed();
                    await _drive!.ConnectAsync(ct);
                    break;
                case "DisconnectDrive":
                    await DisconnectDriveAsync(ct);
                    break;
                case "OpenFilesPrivacy":
                    _drive!.OpenPrivacyNotice();
                    break;
                case "RefreshDriveConnection":
                    await RefreshDriveConnectionAsync(ct);
                    break;
                case "RefreshDriveUsage":
                    _usage = await _drive!.ReadUsageAsync(ct);
                    break;
                case "Select":
                    if (parameter is FilesBrowserRow row && _items.Any(item => ReferenceEquals(item, row)))
                    {
                        var result = await _provider.GetAsync(row.Metadata.Id, ct);
                        ct.ThrowIfCancellationRequested();
                        if (result.IsSuccess && result.Value is { } current && IsCurrentRow(current)) _selected = current;
                        else if (result.IsSuccess) { _selected = null; _status = "This item moved or changed. Refresh the folder."; }
                        else if (IsDenied(result.Error!)) ClearDeniedPresentation();
                        else { _selected = null; _status = Describe(result.Error!); }
                        if (_selected?.Id != _preparedDownloadItem) ClearPreparedDownload();
                    }
                    break;
                case "Refresh": await ReadPageAsync(false, ct); break;
                case "More": await ReadPageAsync(true, ct); break;
                case "Open": await NavigateAsync(_selected!.Id, "", ct); break;
                case "Up": await NavigateAsync(_folder!.ParentId, "", ct); break;
                case "Home": await NavigateAsync(null, "", ct); break;
                case "ShowTrash": await ReadTrashPageAsync(false, ct); break;
                case "ExitTrash":
                    if (_historyIndex >= 0) await NavigateHistoryAsync(_historyIndex, ct);
                    else await NavigateAsync(null, "", ct);
                    break;
                case "Back": await NavigateHistoryAsync(_historyIndex - 1, ct); break;
                case "Forward": await NavigateHistoryAsync(_historyIndex + 1, ct); break;
                case "Search": await NavigateAsync(_folder?.Id, _searchDraft.Trim(), ct); break;
                case "ClearSearch": await NavigateAsync(_folder?.Id, "", ct); break;
                case "SortNameAscending": _sort = "NameAscending"; SortItems(); break;
                case "SortNameDescending": _sort = "NameDescending"; SortItems(); break;
                case "SortNewest": _sort = "Newest"; SortItems(); break;
                case "ToggleView": _listView = !_listView; break;
                case "NewFolder":
                    BeginEdit("CreateFolder", null, "", "Choose a folder name."); break;
                case "Rename":
                    BeginEdit("Rename", _selected, _selected!.Name, "Edit the name, then save."); break;
                case "Trash":
                    BeginEdit("Delete", _selected, "", "Review the move to Trash."); break;
                case "Restore":
                    BeginEdit("Restore", _selected, "", "Review the restore."); break;
                case "Move":
                    _moveTarget = _selected;
                    _status = "Open the destination folder, then choose Move here.";
                    break;
                case "MoveHere":
                    BeginEdit("Move", _moveTarget, "", "Review the move.");
                    break;
                case "CancelMove":
                    EndEdit(); _status = "Move cancelled.";
                    break;
                case "Upload":
                    await StartUploadAsync(ct);
                    break;
                case "ResumeUpload":
                    await ContinueUploadAsync(recheck: true, ct);
                    break;
                case "CancelUpload":
                    await CancelUploadAsync(ct);
                    break;
                case "PrepareDownload":
                    await PrepareDownloadAsync(ct);
                    break;
                case "SaveDownload":
                    _drive!.OpenDownload(_preparedDownload!);
                    ClearPreparedDownload();
                    _status = "Your browser is handling the download.";
                    break;
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
        catch (FilesBrowserRequestException error)
        {
            if (!_disposed) await ShowRequestErrorAsync(error.Error, ct);
        }
        catch
        {
            if (!_disposed) _status = _savedNeedsRefresh ? "Saved. Refresh to reload the folder." : _pending is null ? "Files is unavailable. Retry the action." : "The save outcome is unknown. Retry Save to check the same operation.";
        }
        finally { _busy = false; if (!_disposed) Changed(); }
    }

    private void BeginEdit(string operation, HostedItemMetadata? target, string name, string status)
    {
        ClearPreparedDownload();
        _operation = operation;
        _creating = operation == "CreateFolder";
        _editing = true;
        _editTarget = target;
        _name = name;
        _status = status;
    }

    private const int MaximumUploadChunkBytes = 8 * 1024 * 1024;
    private const string GoogleMimePrefix = "application/vnd.google-apps.";
    private const string PdfMime = "application/pdf";
    private const string ExcelMime = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    private async Task StartUploadAsync(CancellationToken ct)
    {
        var file = await _drive!.PickUploadAsync(ct);
        ct.ThrowIfCancellationRequested();
        if (file is null) return;
        if (string.IsNullOrWhiteSpace(file.Handle) || string.IsNullOrWhiteSpace(file.Name) || file.SizeBytes < 0)
        {
            _drive.ReleaseUpload(file.Handle);
            throw new InvalidOperationException("The selected file is invalid.");
        }
        ClearPreparedDownload();
        _upload = new(new(Guid.NewGuid()), _folder?.Id, file);
        await ContinueUploadAsync(recheck: false, ct);
    }

    private async Task ContinueUploadAsync(bool recheck, CancellationToken ct)
    {
        var upload = _upload!;
        var drive = _drive!;
        using var request = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _uploadRequest = request;
        var transferToken = request.Token;
        try
        {
            _status = recheck ? "Checking the upload…" : "Starting the upload…";
            Changed();
            FilesBrowserUploadProgress progress;
            if (upload.Progress is { } previous)
                progress = await drive.ReadUploadAsync(previous.TransferId, transferToken);
            else
                progress = await drive.BeginUploadAsync(upload.OperationId, upload.ParentId, upload.File, transferToken);
            transferToken.ThrowIfCancellationRequested();
            AcceptUploadProgress(upload, progress);
            while (progress.State == FilesTransferState.Running && progress.UploadedBytes < progress.TotalBytes)
            {
                _status = "Uploading to Google Drive…";
                Changed();
                var offset = progress.UploadedBytes;
                var length = (int)Math.Min(progress.ChunkBytes, progress.TotalBytes - offset);
                progress = await drive.WriteUploadChunkAsync(progress.TransferId, upload.File.Handle, offset, length, transferToken);
                transferToken.ThrowIfCancellationRequested();
                if (progress.UploadedBytes <= offset || progress.UploadedBytes > offset + length)
                    throw new InvalidOperationException("The provider did not acknowledge the submitted upload chunk.");
                AcceptUploadProgress(upload, progress);
            }
            if (progress.State == FilesTransferState.Completed)
            {
                ReleaseUpload();
                _status = "Upload saved to Google Drive.";
                _savedNeedsRefresh = true;
                await ReadPageAsync(false, ct);
                if (!_accessDenied) _status = "Upload saved to Google Drive.";
                _savedNeedsRefresh = false;
            }
            else if (progress.State == FilesTransferState.Cancelled)
            {
                ReleaseUpload();
                _status = "Upload was cancelled.";
            }
            else
            {
                _status = "Upload is waiting for Google Drive. Resume to check progress.";
            }
        }
        catch (OperationCanceledException) when (request.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            _status = "Upload paused. Resume to check what Google Drive received.";
        }
        finally { _uploadRequest = null; }
    }

    private static void AcceptUploadProgress(UploadIntent upload, FilesBrowserUploadProgress progress)
    {
        if (progress.TransferId.Value == Guid.Empty || progress.ItemId.Value == Guid.Empty ||
            progress.TotalBytes != upload.File.SizeBytes || progress.UploadedBytes < 0 || progress.UploadedBytes > progress.TotalBytes ||
            progress.ChunkBytes is < 1 or > MaximumUploadChunkBytes ||
            (progress.State == FilesTransferState.Completed && progress.UploadedBytes != progress.TotalBytes) ||
            (upload.Progress is { } prior && (progress.TransferId != prior.TransferId || progress.ItemId != prior.ItemId ||
                progress.UploadedBytes < prior.UploadedBytes)))
            throw new InvalidOperationException("The provider returned invalid upload progress.");
        upload.Progress = progress;
    }

    private async Task CancelUploadAsync(CancellationToken ct)
    {
        var upload = _upload!;
        var current = upload.Progress ?? await _drive!.FindUploadAsync(upload.OperationId, ct);
        ct.ThrowIfCancellationRequested();
        if (current is not null)
        {
            AcceptUploadProgress(upload, current);
            var cancelled = await _drive!.CancelUploadAsync(current.TransferId, ct);
            ct.ThrowIfCancellationRequested();
            AcceptUploadProgress(upload, cancelled);
            if (cancelled.State == FilesTransferState.Completed)
            {
                ReleaseUpload();
                _status = "Google Drive already completed this upload.";
                return;
            }
            if (cancelled.State != FilesTransferState.Cancelled)
                throw new InvalidOperationException("Google Drive did not acknowledge the cancellation.");
        }
        ReleaseUpload();
        _status = "Upload cancelled.";
    }

    private void ReleaseUpload()
    {
        var upload = _upload;
        _upload = null;
        if (upload is not null) _drive!.ReleaseUpload(upload.File.Handle);
    }

    private async Task PrepareDownloadAsync(CancellationToken ct)
    {
        var item = _selected!;
        if (!TryDownloadFormat(item, out var exportMime)) return;
        ClearPreparedDownload();
        var prepared = await _drive!.PrepareDownloadAsync(item.Id, exportMime, ct);
        ct.ThrowIfCancellationRequested();
        _preparedDownload = prepared;
        _preparedDownloadItem = item.Id;
        _status = "Download is ready to save.";
    }

    private static bool TryDownloadFormat(HostedItemMetadata? item, out string? exportMime)
    {
        exportMime = null;
        if (item is null || item.Kind is not (HostedItemKind.File or HostedItemKind.Artifact)) return false;
        if (item.ContentType?.StartsWith(GoogleMimePrefix, StringComparison.Ordinal) != true) return true;
        exportMime = item.ContentType switch
        {
            GoogleMimePrefix + "document" or GoogleMimePrefix + "presentation" or GoogleMimePrefix + "drawing" => PdfMime,
            GoogleMimePrefix + "spreadsheet" => ExcelMime,
            _ => null,
        };
        return exportMime is not null;
    }

    private void ClearPreparedDownload()
    {
        _preparedDownload = null;
        _preparedDownloadItem = null;
    }

    private async Task RefreshDriveConnectionAsync(CancellationToken ct)
    {
        var wasConnected = _drive!.Context.Connected;
        var context = await _drive.RefreshContextAsync(ct);
        _driveConnectionUnverified = false;
        _usage = context.Usage;
        if (!context.Connected)
        {
            ResetFolderPresentation();
            _status = "Google Drive is disconnected.";
            return;
        }
        if (!context.Location.IsAvailable)
        {
            ResetFolderPresentation();
            _folderUnavailable = true;
            _status = "Google Drive is unavailable. Refresh the connection to try again.";
            return;
        }
        if (!wasConnected || _historyIndex < 0) await NavigateAsync(null, "", ct);
        else _status = "Google Drive is connected.";
    }

    private async Task DisconnectDriveAsync(CancellationToken ct)
    {
        var drive = _drive!;
        FilesDriveDisconnectOutcome outcome;
        try { outcome = await drive.DisconnectAsync(ct); }
        catch (FilesBrowserRequestException error) when (IsDenied(error.Error)) { throw; }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception error)
        {
            if (_disposed) return;
            _driveConnectionUnverified = true;
            ResetFolderPresentation();
            _status = "Could not confirm whether Google Drive disconnected. Refresh the connection to check.";
            System.Diagnostics.Trace.TraceWarning("Files disconnect could not be confirmed: {0}", error.GetType().Name);
            return;
        }
        if (_disposed) return;
        ResetFolderPresentation();
        if (outcome == FilesDriveDisconnectOutcome.Disconnected)
        {
            _status = "Google Drive disconnected.";
            return;
        }
        _status = "Disconnected in 9to1. Google did not confirm that it removed the permission. Check your Google Account connections.";
        Changed();
        try
        {
            var context = await drive.RefreshContextAsync(ct);
            if (!_disposed && !ct.IsCancellationRequested) _usage = context.Usage;
        }
        catch (FilesBrowserRequestException error) when (IsDenied(error.Error)) { ClearDeniedPresentation(); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception error)
        {
            System.Diagnostics.Trace.TraceWarning("Files could not refresh its disconnected state: {0}", error.GetType().Name);
        }
    }

    private async Task ShowRequestErrorAsync(FilesError error, CancellationToken ct)
    {
        if (IsDenied(error)) { ClearDeniedPresentation(); return; }
        if (error.Code != FilesErrorCode.QuotaExceeded)
        {
            _status = _savedNeedsRefresh ? "Saved. Refresh to reload the folder." :
                _pending is not null ? "The save outcome is unknown. Retry Save to check the same operation." : Describe(error);
            return;
        }
        _status = _pending is not null
            ? "The save outcome is unknown. Wait for the allowance to reset, then retry the same operation."
            : _savedNeedsRefresh ? "Saved. The allowance paused the folder refresh." : Describe(error);
        if (_drive is null || ct.IsCancellationRequested) return;
        try { _usage = await _drive.ReadUsageAsync(ct); }
        catch (FilesBrowserRequestException usageFailure) when (IsDenied(usageFailure.Error)) { ClearDeniedPresentation(); }
        catch (Exception failure)
        {
            System.Diagnostics.Trace.TraceWarning("Files allowance refresh failed: {0}", failure.GetType().Name);
        }
    }

    private async Task SaveAsync(CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var firstAttempt = _pending is null;
        _pending ??= new FilesOperation(new(Guid.NewGuid()), _actor,
            _creating ? HostedItemId.New() : _editTarget!.Id,
            _creating ? null : _editTarget!.ParentId,
            _operation is "CreateFolder" or "Move" ? _folder?.Id : null,
            _operation, _editTarget?.CurrentRevisionId, null,
            FilesOperationState.Pending, now, now, null, null, Payload: UsesName ? new(NewName: _name) : null);
        var result = await _provider.MutateAsync(_pending, _pending.Payload?.NewName, ct);
        ct.ThrowIfCancellationRequested();
        if (!result.IsSuccess)
        {
            var error = result.Error!;
            if (IsDenied(error)) { ClearDeniedPresentation(); return; }
            if (error.Code == FilesErrorCode.QuotaExceeded)
            {
                if (firstAttempt && error.Details?.TryGetValue("notStarted", out var notStarted) == true && notStarted == "true")
                    _pending = null;
                await ShowRequestErrorAsync(error, ct);
                return;
            }
            if (IsAuthoritativeRejection(error))
            { _pending = null; _status = Describe(error); }
            else _status = "The save outcome is unknown. Retry Save to check the same operation.";
            return;
        }
        var committed = result.Value;
        if (committed is null || committed.State != FilesOperationState.Committed || committed.ResultRevisionId is null || committed.ResultRevisionId.Value.Value == Guid.Empty ||
            committed.Id != _pending.Id || committed.ItemId != _pending.ItemId || committed.Operation != _pending.Operation ||
            committed.ActorId != _pending.ActorId || committed.Payload?.NewName != _pending.Payload?.NewName || committed.Error is not null ||
            committed.BaseRevisionId != _pending.BaseRevisionId || committed.SourceParentId != _pending.SourceParentId ||
            committed.DestinationParentId != _pending.DestinationParentId)
            throw new InvalidOperationException("Provider did not acknowledge a committed revision.");
        var id = committed.ItemId;
        EndEdit();
        _savedNeedsRefresh = true;
        // A committed operation is never presented as unsaved because its follow-up list failed.
        _status = "Saved. Refresh to reload the folder if needed.";
        if (committed.Operation is "Delete" or "Restore") _selected = null;
        await ReadPageAsync(false, ct);
        if (_accessDenied) return;
        if (committed.Operation is "Delete" or "Restore")
        {
            _selected = null;
            _status = committed.Operation == "Delete" ? "Moved to Trash." : "Restored to the original folder.";
            _savedNeedsRefresh = false;
            return;
        }
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
        ClearPreparedDownload();
        _inTrash = false;
        RememberFolder(folder);
        _query = _searchDraft = query;
        _items = page.Items.Select(item => new FilesBrowserRow(item)).ToArray(); _cursor = page.NextPageToken;
        SortItems();
        _status = "Folder opened.";
    }

    private async Task ReadPageAsync(bool append, CancellationToken ct)
    {
        if (_inTrash) { await ReadTrashPageAsync(append, ct); return; }
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
            RememberFolder(_folder);
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
            if (_selected?.Id != _preparedDownloadItem) ClearPreparedDownload();
        }
        _status = "Folder refreshed.";
    }

    private async Task ReadTrashPageAsync(bool append, CancellationToken ct)
    {
        var page = await _drive!.ListTrashAsync(append ? _cursor : null, ct);
        ct.ThrowIfCancellationRequested();
        if (page.Items.Count > 500) throw new InvalidOperationException("The provider returned an oversized Trash page.");
        var rows = page.Items.Select(item => new FilesBrowserRow(item));
        _inTrash = true;
        _folder = null;
        _selected = null;
        ClearPreparedDownload();
        _query = _searchDraft = "";
        _items = (append && _items.Count + page.Items.Count <= 500 ? _items.Concat(rows) : rows)
            .DistinctBy(row => row.Metadata.Id).Take(500).ToArray();
        _cursor = page.NextPageToken;
        _folderUnavailable = false;
        SortItems();
        _status = "Trash loaded.";
    }

    private FilesSearchQuery? SearchQuery(string query, HostedItemId? parentId)
    {
        if (query.Length == 0) return null;
        if (!_provider.Location.Capabilities.Supports(FilesProviderCapabilities.Search))
            throw new InvalidOperationException("This provider no longer supports search.");
        return new(query, _provider.Location.Id, parentId, Limit: 100);
    }
    private bool IsCurrentRow(HostedItemMetadata item) => item.LocationId == _provider.Location.Id &&
        (_inTrash || item.ParentId == _folder?.Id) && _items.Any(row => row.Metadata.Id == item.Id) &&
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

    private void EndEdit()
    {
        _editing = _creating = false;
        _editTarget = _moveTarget = null;
        _pending = null;
        _operation = _name = "";
    }
    private static bool IsDenied(FilesError error) => error.Code is FilesErrorCode.PermissionDenied or FilesErrorCode.PermissionRequired;
    private static bool IsAuthoritativeRejection(FilesError error) => error.Code is
        FilesErrorCode.ItemNotFound or FilesErrorCode.RevisionConflict or FilesErrorCode.NameConflict or
        FilesErrorCode.InvalidName or FilesErrorCode.InvalidState or FilesErrorCode.ProviderCapabilityUnsupported;
    private void ResetFolderPresentation()
    {
        _items = []; _selected = _folder = null; _cursor = null; EndEdit();
        ReleaseUpload();
        ClearPreparedDownload();
        _folderUnavailable = _inTrash = false;
        ClearNavigation();
    }
    private void ClearDeniedPresentation()
    {
        _accessDenied = true;
        _usage = null;
        ResetFolderPresentation();
        _status = "Files access was denied. Reopen Files after signing in.";
    }
    private static string Describe(FilesError error) => error.Code switch
    {
        FilesErrorCode.RevisionConflict => "This item changed. Cancel the action and refresh to see the latest item.",
        FilesErrorCode.QuotaExceeded when error.RetryAfter is { } retryAfter =>
            $"The Files allowance paused this action. Try again after {DateTimeOffset.Now.Add(retryAfter):g}.",
        FilesErrorCode.QuotaExceeded => "The Files allowance paused this action. Check the current allowance.",
        _ => $"{error.Code}: {error.Message}",
    };

    private void RememberFolder(HostedItemMetadata? folder)
    {
        if (folder is null) return;
        if (_visitedFolders.Count >= 128 && !_visitedFolders.ContainsKey(folder.Id)) _visitedFolders.Clear();
        _visitedFolders[folder.Id] = folder;
    }

    private string Breadcrumb()
    {
        if (_accessDenied || _disposed) return "";
        if (_inTrash) return _provider.Location.Name + " / Trash";
        var parts = new Stack<string>();
        var folder = _folder;
        while (folder is not null && parts.Count < 128)
        {
            parts.Push(folder.Name);
            if (folder.ParentId is not { } parent) break;
            if (!_visitedFolders.TryGetValue(parent, out folder)) parts.Push("…");
        }
        parts.Push(_provider.Location.Name);
        return string.Join(" / ", parts);
    }

    internal static string ItemDetails(HostedItemMetadata item)
    {
        var size = item.SizeBytes is { } bytes ? $" · {FormatBytes(bytes)}" : "";
        return $"{item.Kind} · {item.Availability}{size} · {item.ModifiedAt.ToLocalTime():g}";
    }

    private string UsageSummary()
    {
        if (_drive is null || _disposed || _accessDenied) return "";
        if (_usage is not { } usage) return "Current allowance is unavailable. Refresh allowance to check it.";
        var parts = new List<string>();
        if (usage.Paused || new[] { usage.UnitsPerMinute, usage.UnitsPerDay, usage.UploadBytesPerDay, usage.DownloadBytesPerDay }
                .Any(budget => budget?.Remaining == 0))
            parts.Add("An allowance is paused.");
        AddBudget("API units this minute", usage.UnitsPerMinute, count => count.ToString("N0"));
        AddBudget("API units today", usage.UnitsPerDay, count => count.ToString("N0"));
        AddBudget("Upload today", usage.UploadBytesPerDay, FormatBytes);
        AddBudget("Download today", usage.DownloadBytesPerDay, FormatBytes);
        if (FilesDriveUsage.AsTime(usage.AsOf) is { } asOf) parts.Add($"Checked {asOf.ToLocalTime():g}.");
        return string.Join("\n", parts);

        void AddBudget(string label, FilesDriveBudget? budget, Func<long, string> format)
        {
            if (budget is null) { parts.Add($"{label}: unavailable."); return; }
            var reset = FilesDriveUsage.AsTime(budget.ResetAt) is { } time ? $" · resets {time.ToLocalTime():g}" : "";
            parts.Add($"{label}: {format(budget.Remaining)} of {format(budget.Limit)} remaining{reset}.");
        }
    }

    private static string FormatBytes(long bytes) => bytes switch
    {
        >= 1_073_741_824 => $"{bytes / 1_073_741_824d:N1} GiB",
        >= 1_048_576 => $"{bytes / 1_048_576d:N1} MiB",
        >= 1024 => $"{bytes / 1024d:N1} KiB",
        _ => $"{bytes:N0} B",
    };
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
        _disposed = true;
        ResetFolderPresentation();
        _usage = null;
        _status = "";
    }
    private void ClearNavigation() { _history.Clear(); _visitedFolders.Clear(); _historyIndex = -1; _query = _searchDraft = ""; }
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
            "Details" => FilesBrowserController.ItemDetails(Metadata), "TileIcon" => TileIcon(Metadata), _ => null };
        return path is "Id" or "Name" or "Details" or "TileIcon";
    }

    private static string TileIcon(HostedItemMetadata item)
    {
        if (item.Kind == HostedItemKind.Folder) return "icon:folder";
        var contentType = item.ContentType ?? "";
        if (contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase)) return "icon:image";
        if (contentType.StartsWith("video/", StringComparison.OrdinalIgnoreCase)) return "icon:play";
        if (contentType.StartsWith("audio/", StringComparison.OrdinalIgnoreCase)) return "icon:wave";
        if (contentType.Contains("pdf", StringComparison.OrdinalIgnoreCase)) return "icon:study";
        if (contentType.EndsWith("spreadsheet", StringComparison.OrdinalIgnoreCase) ||
            contentType.Contains("spreadsheet", StringComparison.OrdinalIgnoreCase)) return "icon:table";
        if (contentType.EndsWith("presentation", StringComparison.OrdinalIgnoreCase) ||
            contentType.Contains("presentation", StringComparison.OrdinalIgnoreCase)) return "icon:slides";
        if (contentType.EndsWith("document", StringComparison.OrdinalIgnoreCase) ||
            contentType.Contains("document", StringComparison.OrdinalIgnoreCase)) return "icon:text";
        return "icon:file";
    }
}
