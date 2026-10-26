using System.ComponentModel;
using System.Runtime.ExceptionServices;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Threading;
using CakeOS.Cui;
using CakeOS.Cui.Language;
using CakeOS.Cui.Runtime;
using Haven.Application;
using HavenOS.Files;
using HavenOS.Files.NativeHost;
using HavenOS.Home.PermissionsTrustNotifications;

namespace HavenOS.Files.NativeUI;

/// <summary>The native host supplies its same Home service graph and captures the original actor before awaiting.
/// This surface owns no provider, storage root, Home graph or installation authority.</summary>
public sealed partial class FilesNativeBrowserSurface : UserControl, IDisposable, IAsyncDisposable,
    ICuiBindingContext, ICuiActionDispatcher, ICuiActionAvailability, INotifyPropertyChanged
{
    private readonly FilesNativeBrowserService _browser;
    private readonly FilesCompatibilityPackageOpenCoordinator? _packages;
    private readonly AuthenticatedResourceActor _originalActor;
    private readonly ICuiSceneReadiness _readiness;
    private readonly CancellationTokenSource _lifetime;
    private readonly CancellationToken _originalHostLifetime;
    private CancellationTokenRegistration _originalHostRetirement;
    private readonly SemaphoreSlim _operations = new(1, 1);
    private readonly List<(Guid? ID, string Title, bool IsTrash)> _history = [(null, "Files", false)];
    private readonly ListBox _items = new();
    private readonly TextBox _search = new() { PlaceholderText = "Search this folder", Width = 280, MaxLength = 256 };
    private readonly TextBox _editName = new() { PlaceholderText = "Folder or item name", Width = 280, MaxLength = 255 };
    private readonly string _mutationSession = Guid.NewGuid().ToString("N");
    private FilesNativeBrowserService.PreparedMutation? _pendingMutation;
    private sealed record MoveSource(FilesNativeBrowserPage Page, HostedItemMetadata Item);
    private MoveSource? _moveSource;
    private bool _showTechnicalDetails;
    private readonly ComboBox _sort = new()
    {
        ItemsSource = new[] { "Name: A–Z (page)", "Name: Z–A (page)" },
        SelectedIndex = 0, Width = 200
    };
    private CuiSceneHost? _scene;
    private FilesNativeBrowserPage? _page;
    private Func<CancellationToken, ValueTask<bool>>? _originalPageCurrent;
    private Guid? _boundStoreId;
    private int _historyIndex;
    private bool _busy;
    private bool _disposed;
    private bool _initialized;
    private readonly object _originalTasksSync = new();
    private readonly HashSet<Task> _originalTasks = [];
    private readonly List<Exception> _originalFailures = [];
    private Task? _originalClose;
    private bool _closing;
    private string _status = "Opening Files";
    private string _query = "";
    public new event PropertyChangedEventHandler? PropertyChanged;

    public FilesNativeBrowserSurface(FilesNativeBrowserService browser, FilesCompatibilityPackageOpenCoordinator? packages,
        AuthenticatedResourceActor originalActor, ICuiSceneReadiness readiness, CancellationToken hostLifetime)
    {
        _browser = browser; _packages = packages; _originalActor = originalActor; _readiness = readiness;
        ArgumentNullException.ThrowIfNull(originalActor);
        _originalHostLifetime = hostLifetime;
        _lifetime = CancellationTokenSource.CreateLinkedTokenSource(hostLifetime);
        _search.Width = double.NaN;
        _sort.Width = 160;
        _items.HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Stretch;
        _items.ItemTemplate = new FuncDataTemplate<HostedItemMetadata>((item, _) => BuildDetailsRow(item));
        SizeChanged += (_, _) => Changed();
        _items.SelectionChanged += (_, _) =>
        {
            if (!OriginalAlive()) { Changed(); return; }
            RunOriginalNativeMutation(() => { ClearOriginalTextPreview(); SharingSelectionChanged(); Changed(); });
        };
        _sort.SelectionChanged += (_, _) =>
        {
            if (!Available || _page is null || _sort.SelectedIndex is < 0 or > 1) return;
            RunOriginalNativeMutation(() =>
            {
                ApplyDisplayedNameSort();
                RequireOriginalAlive(CancellationToken.None);
                Changed();
            });
        };
        _items.DoubleTapped += async (_, _) => await InvokeFromNativeEventAsync("9to1.Files.Open");
        _items.KeyDown += async (_, args) =>
        { if (args.Key == Key.Enter) { args.Handled = true; await InvokeFromNativeEventAsync("9to1.Files.Open"); } };
        _search.KeyDown += async (_, args) =>
        { if (args.Key == Key.Enter) { args.Handled = true; await InvokeFromNativeEventAsync("9to1.Files.Search"); } };
        _search.TextChanged += (_, _) => { if (OriginalAlive()) RunOriginalNativeMutation(Changed); };
        _editName.TextChanged += (_, _) => { if (OriginalAlive()) RunOriginalNativeMutation(Changed); };
        KeyDown += async (_, args) =>
        {
            if (args.Handled || !Available) return;
            if (GetOwnedFolderTabShortcut(args) is { } tabCommand)
            { args.Handled = true; await InvokeFolderTabFromNativeEventAsync(tabCommand); return; }
            if (args.Key == Key.F && args.KeyModifiers == KeyModifiers.Control)
            { args.Handled = true; _search.Focus(); return; }
            var command = args.Key switch
            {
                Key.F5 => "9to1.Files.Refresh",
                Key.Delete when _items.IsKeyboardFocusWithin => "9to1.Files.Delete",
                Key.X when args.KeyModifiers == KeyModifiers.Control && _items.IsKeyboardFocusWithin => "9to1.Files.ChooseMove",
                Key.V when args.KeyModifiers == KeyModifiers.Control && _items.IsKeyboardFocusWithin => "9to1.Files.Move",
                Key.Left when args.KeyModifiers == KeyModifiers.Alt => "9to1.Files.Back",
                Key.Right when args.KeyModifiers == KeyModifiers.Alt => "9to1.Files.Forward",
                Key.Up when args.KeyModifiers == KeyModifiers.Alt => "9to1.Files.Up",
                _ => null
            };
            if (command is null || IsActionAvailable(command) != true) return;
            args.Handled = true;
            await InvokeFromNativeEventAsync(command);
        };
    }

    public Task InitializeAsync(CancellationToken token = default)
        => TrackOriginalAsync(() => InitializeOriginalAsync(token));

    private async Task InitializeOriginalAsync(CancellationToken token)
    {
        Dispatcher.UIThread.VerifyAccess();
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_initialized) throw new InvalidOperationException("The original Files view is already mounted.");
        _initialized = true;
        // Registration occurs inside the already-published original initialization Task.
        // Close awaits that task, so the exact registration settles before it is disposed.
        _originalHostRetirement = _originalHostLifetime.Register(
            static state => _ = ((FilesNativeBrowserSurface)state!).CloseAndDrainAsync(), this);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, _lifetime.Token);
        try
        {
            await RequireReadyAsync(linked.Token);
            var page = await _browser.ListAsync(_originalActor, token: linked.Token);
            await _browser.RevalidateAsync(page, _originalActor, linked.Token);
            _scene = CreateOriginalCanonicalSceneHost();
            var available = await _scene.ShowAsync(new("files", "Files", "Browser", LoadDocument(), this, this, _readiness), linked.Token);
            if (available.State != CuiSceneAvailabilityState.Ready) throw new UnauthorizedAccessException(available.Message);
            await _browser.RevalidateAsync(page, _originalActor, linked.Token);
            var current = await _browser.CaptureOriginalPageReadCheckAsync(page, _originalActor,
                OriginalAlive, linked.Token);
            await RequireReadyAsync(linked.Token);
            await RequireOriginalOwnerAsync(current, linked.Token);
            _boundStoreId = page.StoreID; _page = page; _originalPageCurrent = current;
            ApplyDisplayedNameSort();
            RequireOriginalAlive(linked.Token);
            Content = _scene;
            RequireOriginalAlive(linked.Token);
            _status = $"{page.Items.Count} items";
            Changed();
            RequireOriginalAlive(linked.Token);
        }
        catch (Exception primary)
        {
            lock (_originalTasksSync) _closing = true;
            List<Exception> failures = [primary];
            try { ClearSources(); } catch (Exception cleanup) { Add(failures, cleanup); }
            ThrowOriginalFailures(failures, "Original Files initialization and refusal cleanup failed.");
            throw;
        }
    }

    public Task RefreshAsync(CancellationToken token = default)
        => TrackOriginalAsync(() => RefreshOriginalAsync(token));

    private async Task RefreshOriginalAsync(CancellationToken token)
    {
        Dispatcher.UIThread.VerifyAccess();
        ObjectDisposedException.ThrowIf(_disposed, this);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, _lifetime.Token);
        await _operations.WaitAsync(linked.Token);
        List<Exception> failures = [];
        try
        {
            _busy = true; Changed();
            await RequireReadyAsync(linked.Token);
            var retained = _page ?? throw new InvalidOperationException("The Files browser is not open.");
            await _browser.RevalidateAsync(retained, _originalActor, linked.Token);
            var current = _originalPageCurrent
                ?? throw new UnauthorizedAccessException("Retain the original Files page owner check.");
            await RequireOriginalOwnerAsync(current, linked.Token);
            if (!ReferenceEquals(_page, retained) || !ReferenceEquals(_originalPageCurrent, current))
                throw new UnauthorizedAccessException("The displayed Files page owner changed.");
        }
        catch (Exception error)
        {
            lock (_originalTasksSync) _closing = true;
            Add(failures, error);
        }
        if (failures.Count != 0)
            try { ClearSources(); } catch (Exception error) { Add(failures, error); }
        _busy = false;
        try { Changed(); } catch (Exception error) { Add(failures, error); }
        try { _operations.Release(); } catch (Exception error) { Add(failures, error); }
        ThrowOriginalFailures(failures, "Original Files refresh and cleanup failed.");
    }

    public bool TryGetValue(string path, out object? value)
    {
        if (TryGetFolderTabsValue(path, out value) || TryGetSharingValue(path, out value) || TryGetTextPreviewValue(path, out value)) return true;
        value = path switch
        {
            "NavigationColumns" => Bounds.Width < 540 ? "Auto 1fr" : "Auto 1fr 260",
            "SearchRow" => Bounds.Width < 540 ? 1 : 0,
            "SearchColumn" => Bounds.Width < 540 ? 0 : 2,
            "SearchColumnSpan" => Bounds.Width < 540 ? 2 : 1,
            "CompactLayout" => Bounds.Width < 641,
            "SidebarVisible" => Bounds.Width >= 641,
            "BodyColumns" => Bounds.Width < 641 ? "1fr" : Bounds.Width < 1120 ? "240 1fr" : "240 1fr Auto",
            "BodyRows" => Bounds.Width < 1120 ? "1fr Auto" : "1fr",
            "MainColumn" => Bounds.Width < 641 ? 0 : 1,
            "DetailsColumn" => Bounds.Width < 641 ? 0 : Bounds.Width < 1120 ? 1 : 2,
            "DetailsRow" => Bounds.Width < 1120 ? 1 : 0,
            "DetailsWidth" => Bounds.Width < 1120 ? Math.Max(208, Bounds.Width - (Bounds.Width < 641 ? 16 : 264)) : 280,
            "DetailsMaxHeight" => Bounds.Width < 1120 ? 260 : Math.Max(260, Bounds.Height),
            "HasSelection" => _items.SelectedItem is HostedItemMetadata selected && _page?.Items.Any(item => ReferenceEquals(item, selected)) == true,
            "WideColumns" => Bounds.Width >= 900,
            "ItemColumns" => Bounds.Width >= 900 ? "1fr 150 100 90" : "1fr 0 0 0",
            "SidebarHeading" => "Folders in " + (_page?.FolderTitle ?? "Files"),
            "SidebarFolders" => _page is { IsTrash: false } sidebarPage ? sidebarPage.Items.Where(item => item.Kind == HostedItemKind.Folder).ToArray() : Array.Empty<HostedItemMetadata>(),
            "FolderTitle" => _page?.FolderTitle ?? "Files",
            "SelectedDetails" => SelectedDetails,
            "Status" => _status,
            "CanNavigate" => IsActionAvailable("9to1.Files.Refresh"),
            "CanUp" => IsActionAvailable("9to1.Files.Up"),
            "CanBack" => IsActionAvailable("9to1.Files.Back"),
            "CanForward" => IsActionAvailable("9to1.Files.Forward"),
            "CanMore" => IsActionAvailable("9to1.Files.More"),
            "CanOpen" => IsActionAvailable("9to1.Files.Open"),
            "CanCreateFolder" => IsActionAvailable("9to1.Files.CreateFolder"),
            "CanRename" => IsActionAvailable("9to1.Files.Rename"),
            "CanDelete" => IsActionAvailable("9to1.Files.Delete"),
            "CanRestore" => IsActionAvailable("9to1.Files.Restore"),
            "CanChooseMove" => IsActionAvailable("9to1.Files.ChooseMove"),
            "CanMove" => IsActionAvailable("9to1.Files.Move"),
            "CanCancelMove" => IsActionAvailable("9to1.Files.CancelMove"),
            "CanShowDetails" => IsActionAvailable("9to1.Files.TechnicalDetails"),
            "TechnicalDetailsLabel" => _showTechnicalDetails ? "Hide technical details" : "Technical details",
            "MoveSelection" => _page is null || _moveSource is null ? "" : $"Choose a destination for ‘{_moveSource.Item.Name}’.",
            "CanApply" => IsActionAvailable("9to1.Files.Apply"),
            "CanCancelReview" => IsActionAvailable("9to1.Files.CancelReview"),
            "CanRetryAudit" => IsActionAvailable("9to1.Files.RetryAudit"),
            "PendingChange" => _page is null ? "" : _pendingMutation?.Description ?? "",
            _ => null
        };
        return path is "NavigationColumns" or "SearchRow" or "SearchColumn" or "SearchColumnSpan" or "CompactLayout" or "SidebarVisible" or "BodyColumns" or "BodyRows" or "MainColumn" or "DetailsColumn" or "DetailsRow" or "DetailsWidth" or "DetailsMaxHeight" or "HasSelection" or "WideColumns" or "ItemColumns" or "SidebarHeading" or "SidebarFolders" or "SelectedDetails" or "FolderTitle" or "Status" or "CanNavigate" or "CanUp" or "CanBack" or "CanForward" or "CanMore" or "CanOpen"
            or "CanCreateFolder" or "CanRename" or "CanDelete" or "CanRestore" or "CanApply" or "CanCancelReview" or "CanRetryAudit" or "PendingChange"
            or "CanChooseMove" or "CanMove" or "CanCancelMove" or "MoveSelection" or "CanShowDetails" or "TechnicalDetailsLabel";
    }
    private string SelectedDetails
    {
        get
        {
            if (_disposed || _lifetime.IsCancellationRequested || _page is null ||
                _items.SelectedItem is not HostedItemMetadata selected ||
                !_page.Items.Any(item => ReferenceEquals(item, selected))) return "Select an item to view its displayed details.";
            var size = selected.SizeBytes is { } bytes ? $"{bytes:N0} bytes" : "Not reported";
            var details = $"{selected.Name}\n{selected.Kind} · {size} · {selected.Availability}\n" +
                $"Owner: You · {(selected.IsShared ? "Shared" : "Private")}\nCreated: {selected.CreatedAt.ToLocalTime():g}\n" +
                $"{(_page.IsTrash ? "Deleted" : "Modified")}: {selected.ModifiedAt.ToLocalTime():g}";
            return !_showTechnicalDetails ? details : details + $"\nIdentity: {selected.Id}\nLocation: {selected.LocationId}\n" +
                $"Parent: {selected.ParentId?.ToString() ?? "Root"}\nOwner principal: {selected.OwnerPrincipalId}\n" +
                $"Revision: {selected.CurrentRevisionId?.ToString() ?? "Not reported"}\nContent hash: {selected.ContentHash ?? "Not reported"}";
        }
    }
    private bool Available => !_closing && !_disposed && !_busy && !_lifetime.IsCancellationRequested && _scene is not null;
    private bool CanOpenSelection => _page is { IsTrash: false } && _items.SelectedItem is HostedItemMetadata selected &&
        (selected.Kind == HostedItemKind.Folder || CanOpenTextSelection || selected.Kind == HostedItemKind.File && _packages is not null &&
         Path.GetExtension(selected.Name).ToLowerInvariant() is ".exe" or ".msi" or ".apk");
    public bool? IsActionAvailable(string command) => IsFolderTabCommand(command) ? FolderTabActionAvailable(command)
        : IsSharingCommand(command) ? SharingActionAvailable(command) : command switch
    {
        "9to1.Files.OpenSidebarFolder" or "9to1.Files.Home" or "9to1.Files.Trash" or "9to1.Files.Refresh" or "9to1.Files.Search" => Available && _pendingMutation is null,
        "9to1.Files.Up" => Available && _pendingMutation is null && _page?.ParentID is not null,
        "9to1.Files.Back" => Available && _pendingMutation is null && _historyIndex > 0,
        "9to1.Files.Forward" => Available && _pendingMutation is null && _historyIndex + 1 < _history.Count,
        "9to1.Files.More" => Available && _pendingMutation is null && _page?.Next is not null,
        "9to1.Files.Open" => Available && _pendingMutation is null && CanOpenSelection,
        "9to1.Files.Preview" => Available && _pendingMutation is null && CanPreviewTextSelection,
        "9to1.Files.Preview.Close" => Available && _textPreview is not null,
        "9to1.Files.CreateFolder" => Available && _pendingMutation is null && _browser.HasNativeMutationOwner
            && _page?.ParentID is not null && !string.IsNullOrWhiteSpace(_editName.Text),
        "9to1.Files.Rename" => Available && _pendingMutation is null && !string.IsNullOrWhiteSpace(_editName.Text)
            && _page is { } page && _items.SelectedItem is HostedItemMetadata row && _browser.CanRenameNativeSelection(page, row),
        "9to1.Files.Delete" => Available && _pendingMutation is null && _page is { } deletePage
            && _items.SelectedItem is HostedItemMetadata deleteRow && _browser.CanDeleteNativeSelection(deletePage, deleteRow),
        "9to1.Files.Restore" => Available && _pendingMutation is null && _page is { } restorePage
            && _items.SelectedItem is HostedItemMetadata restoreRow && _browser.CanRestoreNativeSelection(restorePage, restoreRow),
        "9to1.Files.ChooseMove" => Available && _pendingMutation is null && _page is { } movePage
            && _items.SelectedItem is HostedItemMetadata moveRow && _browser.CanMoveNativeSelection(movePage, moveRow),
        "9to1.Files.Move" => Available && _pendingMutation is null && _moveSource is { } source
            && _page is { } destination && _browser.CanPrepareNativeMove(source.Page, source.Item, destination),
        "9to1.Files.CancelMove" => Available && _pendingMutation is null && _moveSource is not null,
        "9to1.Files.TechnicalDetails" => Available && _page is { } detailsPage
            && _items.SelectedItem is HostedItemMetadata detailsRow && detailsPage.Items.Any(item => ReferenceEquals(item, detailsRow)),
        "9to1.Files.Apply" => Available && _pendingMutation is { HasObservedOutcome: false } pending
            && (pending.Approval.IsAllowed || pending.Approval.State == HomePermissionRequestState.PendingApproval),
        "9to1.Files.CancelReview" => Available && _pendingMutation is { HasObservedOutcome: false },
        "9to1.Files.RetryAudit" => Available && _pendingMutation is { HasObservedOutcome: true },
        _ => false
    };
    public ValueTask DispatchAsync(string command, object? parameter, CancellationToken cancellationToken = default)
    {
        if (command == "9to1.Files.OpenSidebarFolder")
        {
            Dispatcher.UIThread.VerifyAccess(); cancellationToken.ThrowIfCancellationRequested();
            if (!Available || _pendingMutation is not null || _page?.IsTrash != false || parameter is not HostedItemMetadata folder ||
                folder.Kind != HostedItemKind.Folder || !_page.Items.Any(item => ReferenceEquals(item, folder)))
                throw new UnauthorizedAccessException("Choose a folder issued by the current Files page.");
            RunOriginalNativeMutation(() => _items.SelectedItem = folder);
            return DispatchAsync("9to1.Files.Open", null, cancellationToken);
        }
        if (IsFolderTabCommand(command))
            return new(TrackOriginalAsync(() => InvokeFolderTabOriginalAsync(command, parameter, cancellationToken)));
        if (IsSharingCommand(command) || command == "9to1.Files.Apply" && IsPendingShareReview)
        {
            ClearOriginalTextPreview();
            if (command is not ("9to1.Files.Share.Revoke" or "9to1.Files.Share.ChangeRole" or "9to1.Files.Share.EditScope") && parameter is not null)
                throw new ArgumentException("Access commands retain the original canonical selection.");
            return new(TrackOriginalAsync(() => InvokeSharingOriginalAsync(command, parameter, cancellationToken)));
        }
        if (parameter is not null) throw new ArgumentException("Files commands use the owning canonical selection, not caller paths or identity.");
        return new(InvokeAsync(command, cancellationToken));
    }

    private Task InvokeAsync(string command, CancellationToken token)
        => TrackOriginalAsync(() => InvokeOriginalAsync(command, token));

    private async Task InvokeFromNativeEventAsync(string command)
    {
        try { await InvokeAsync(command, _lifetime.Token); }
        catch (Exception)
        {
            // The exact original operation and any UI cleanup fault remain retained by the owner.
            // Native event observation performs no additional work after the owned Task settles.
        }
    }

    private async Task InvokeOriginalAsync(string command, CancellationToken token)
    {
        Dispatcher.UIThread.VerifyAccess();
        if (IsActionAvailable(command) != true) return;
        var originalPage = _page;
        var originalSelection = _items.SelectedItem as HostedItemMetadata;
        var originalHistoryIndex = _historyIndex;
        var originalSearch = _search.Text ?? "";
        var originalName = _editName.Text ?? "";
        var originalMove = _moveSource;
        string? completedMessage = null;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, _lifetime.Token);
        await _operations.WaitAsync(linked.Token);
        Exception? handled = null;
        List<Exception> originalFailures = [];
        try
        {
            if (IsActionAvailable(command) != true) return;
            if (command == "9to1.Files.Preview.Close")
            { ClearOriginalTextPreview(); Changed(); return; } // Redaction requires no new content grant/read.
            RequireRetainedPage();
            ClearOriginalTextPreview();
            _busy = true; Changed(); RequireOriginalAlive(linked.Token);
            await RequireReadyAsync(linked.Token);
            RequireRetainedPage();
            if (command is not ("9to1.Files.Refresh" or "9to1.Files.RetryAudit" or "9to1.Files.CancelReview") && originalPage is not null)
                await _browser.RevalidateAsync(originalPage, _originalActor, linked.Token);
            RequireRetainedPage();
            if (command == "9to1.Files.Preview")
            { await PreviewOriginalTextAsync(originalPage!, originalSelection!, linked.Token); return; }
            if (command == "9to1.Files.TechnicalDetails")
            { _showTechnicalDetails = !_showTechnicalDetails; return; }
            if (command == "9to1.Files.ChooseMove")
            {
                _moveSource = new(originalPage!, originalSelection!);
                _status = $"Open the destination folder, then choose Move here for ‘{originalSelection!.Name}’.";
                return;
            }
            if (command == "9to1.Files.CancelMove")
            { _moveSource = null; _status = "Move cancelled. The item remains in its original folder."; return; }
            if (command == "9to1.Files.Move")
            {
                _pendingMutation = await _browser.PrepareNativeMoveAsync(originalMove!.Page, originalMove.Item,
                    originalPage!, _originalActor, OriginalAlive, _mutationSession, linked.Token);
                RequireRetainedPage();
                _status = "Review this move in Home, then choose Apply.";
                return;
            }
            if (command is "9to1.Files.CreateFolder" or "9to1.Files.Rename" or "9to1.Files.Delete" or "9to1.Files.Restore")
            {
                _pendingMutation = await _browser.PrepareNativeMutationAsync(originalPage!,
                    command != "9to1.Files.CreateFolder" ? originalSelection : null, _originalActor,
                    command["9to1.Files.".Length..],
                    command is "9to1.Files.Delete" or "9to1.Files.Restore" ? originalSelection!.Name : originalName,
                    OriginalAlive, _mutationSession, linked.Token);
                RequireRetainedPage();
                _status = _pendingMutation.Approval.IsAllowed ? "Ready to apply this change."
                    : _pendingMutation.Approval.State == HomePermissionRequestState.PendingApproval
                        ? "Review this change in Home, then choose Apply." : _pendingMutation.Approval.Message;
                return;
            }
            if (command == "9to1.Files.CancelReview")
            {
                _browser.RetireNativeMutationReview(_pendingMutation!);
                _pendingMutation = null; _status = "Change cancelled before execution."; return;
            }
            if (command == "9to1.Files.RetryAudit")
            {
                var audit = await _browser.RetryNativeMutationAuditAsync(_pendingMutation!, linked.Token);
                RequireRetainedPage(); _status = audit.Message;
                if (!audit.Succeeded) return;
                _pendingMutation = null; completedMessage = "Home audit recorded.";
            }
            if (command == "9to1.Files.Apply")
            {
                var outcome = await _browser.ApplyNativeMutationAsync(_pendingMutation!, linked.Token);
                RequireRetainedPage(); _status = outcome.Message;
                if (outcome.AwaitingHomeReview) return;
                if (outcome.Operation is not null) _moveSource = null;
                if (outcome.AuditRecorded) _pendingMutation = null;
                completedMessage = outcome.Message;
                _editName.Text = "";
            }
            var index = _historyIndex; var destination = _history[index];
            FilesNativeBrowserCursor? cursor = null;
            var appendHistory = false;
            if (command == "9to1.Files.Open")
            {
                var selected = originalSelection ?? throw new InvalidOperationException("Select an item.");
                if (selected.Kind == HostedItemKind.Folder)
                { destination = (selected.Id.Value, selected.Name, false); appendHistory = true; _query = ""; _search.Text = ""; RequireRetainedPage(); }
                else
                {
                    var packageSelection = _packages is not null && Path.GetExtension(selected.Name).ToLowerInvariant() is ".exe" or ".msi" or ".apk";
                    if (!packageSelection && FilesNativeBrowserService.IsTextPreviewCandidate(selected))
                    { await PreviewOriginalTextAsync(originalPage!, selected, linked.Token); return; }
                    var selection = await _browser.ReadPackageSelectionAsync(originalPage!, selected, _originalActor, 256L * 1024 * 1024, linked.Token);
                    RequireRetainedPage();
                    var packages = _packages ?? throw new NotSupportedException("No owning package inspection handler is registered.");
                    await packages.OpenAsync(selection, linked.Token);
                    RequireRetainedPage();
                    await _browser.RevalidateAsync(originalPage!, _originalActor, linked.Token);
                    var retainedCurrent = _originalPageCurrent
                        ?? throw new UnauthorizedAccessException("Retain the original Files page owner check.");
                    await RequireReadyAsync(linked.Token);
                    await RequireOriginalOwnerAsync(retainedCurrent, linked.Token);
                    RequireRetainedPage();
                    _status = "Opened package inspection"; return;
                }
            }
            else if (command == "9to1.Files.Up")
            {
                var parent = await _browser.GetParentAsync(originalPage!, _originalActor, linked.Token);
                destination = (parent.ID, parent.Title, false);
                RequireRetainedPage(); appendHistory = true; _query = ""; _search.Text = ""; RequireRetainedPage();
            }
            else if (command == "9to1.Files.Home") { destination = (null, "Files", false); appendHistory = true; _query = ""; _search.Text = ""; RequireRetainedPage(); }
            else if (command == "9to1.Files.Trash") { destination = (null, "Trash", true); appendHistory = true; _query = ""; _search.Text = ""; RequireRetainedPage(); }
            else if (command == "9to1.Files.Back") { index--; destination = _history[index]; _query = ""; _search.Text = ""; RequireRetainedPage(); }
            else if (command == "9to1.Files.Forward") { index++; destination = _history[index]; _query = ""; _search.Text = ""; RequireRetainedPage(); }
            else if (command == "9to1.Files.Search") _query = originalSearch;
            else if (command == "9to1.Files.More") cursor = _page!.Next;
            var page = await _browser.ListAsync(_originalActor, destination.ID, _query, cursor, linked.Token,
                expectedStoreId: _boundStoreId, isTrash: destination.IsTrash);
            await _browser.RevalidateAsync(page, _originalActor, linked.Token);
            var current = await _browser.CaptureOriginalPageReadCheckAsync(page, _originalActor,
                OriginalAlive, linked.Token);
            await RequireReadyAsync(linked.Token);
            await RequireOriginalOwnerAsync(current, linked.Token);
            RequireRetainedPage();
            if (appendHistory)
            {
                _history.RemoveRange(_historyIndex + 1, _history.Count - _historyIndex - 1);
                if (_history.Count >= 128) { _history.RemoveAt(0); _historyIndex--; }
                _history.Add(destination); index = _history.Count - 1;
            }
            _historyIndex = index; _page = page; _originalPageCurrent = current;
            _items.SelectedItem = null;
            RequireOriginalAlive(linked.Token);
            ApplyDisplayedNameSort();
            RequireOriginalAlive(linked.Token);
            _status = completedMessage ?? $"{page.Items.Count} items";
        }
        catch (Exception error)
        {
            // A handled selection/store-read outcome remains recoverable. Original owner
            // retirement and publication/cleanup failures refuse and retire the actual scope.
            lock (_originalTasksSync) AddOriginalFailure(error);
            var terminal = error is UnauthorizedAccessException or ObjectDisposedException ||
                !OriginalAlive() || error is OperationCanceledException && _lifetime.IsCancellationRequested;
            if (terminal)
            {
                lock (_originalTasksSync) _closing = true;
                Add(originalFailures, error);
            }
            else handled = error;
            try
            {
                ClearSources();
                _status = OriginalAlive() ? error.Message : "Files view closed";
            }
            catch (Exception cleanup)
            {
                if (handled is not null) { Add(originalFailures, handled); handled = null; }
                Add(originalFailures, cleanup);
            }
        }
        finally
        {
            _busy = false;
            try { Changed(); }
            catch (Exception error)
            {
                if (handled is not null) { Add(originalFailures, handled); handled = null; }
                Add(originalFailures, error);
            }
            try { _operations.Release(); }
            catch (Exception error)
            {
                if (handled is not null) { Add(originalFailures, handled); handled = null; }
                Add(originalFailures, error);
            }
            ThrowOriginalFailures(originalFailures, "Original Files action and cleanup failed.");
        }
        void RequireRetainedPage()
        {
            RequireOriginalAlive(linked.Token);
            if (!ReferenceEquals(_page, originalPage) || _historyIndex != originalHistoryIndex ||
                (command is "9to1.Files.Open" or "9to1.Files.Preview" or "9to1.Files.Rename" or "9to1.Files.Delete" or "9to1.Files.Restore" or "9to1.Files.ChooseMove" or "9to1.Files.TechnicalDetails")
                    && _items.SelectedItem as HostedItemMetadata != originalSelection)
                throw new InvalidOperationException("The original Files selection changed before navigation.");
            if (command == "9to1.Files.Move" && !ReferenceEquals(_moveSource, originalMove))
                throw new InvalidOperationException("The original item selected for moving changed.");
        }
    }

    // The native route invokes this AFTER its final startup/actor I/O. Only the retained
    // privately issued page's Home/configuration callback is used; no Files metadata reentry.
    public Task RevalidateOriginalOwnerAsync(CancellationToken token = default)
        => TrackOriginalAsync(() => RevalidateOriginalOwnerCoreAsync(token));

    private async Task RevalidateOriginalOwnerCoreAsync(CancellationToken token)
    {
        var page = _page ?? throw new UnauthorizedAccessException("The original Files page is unavailable.");
        var current = _originalPageCurrent
            ?? throw new UnauthorizedAccessException("The original Files owner observation is unavailable.");
        await RequireOriginalOwnerAsync(current, token);
        if (!ReferenceEquals(_page, page) || !ReferenceEquals(_originalPageCurrent, current))
            throw new UnauthorizedAccessException("The original displayed Files owner changed.");
        RequireOriginalAlive(token);
    }

    public void CheckOriginalPublicationAlive()
    {
        Dispatcher.UIThread.VerifyAccess();
        RequireOriginalAlive(CancellationToken.None);
    }

    private bool OriginalAlive()
    {
        lock (_originalTasksSync) return !_closing && !_disposed &&
            !_originalHostLifetime.IsCancellationRequested && !_lifetime.IsCancellationRequested;
    }

    private void RequireOriginalAlive(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        ObjectDisposedException.ThrowIf(!OriginalAlive(), this);
    }

    private async Task RequireOriginalOwnerAsync(Func<CancellationToken, ValueTask<bool>> current,
        CancellationToken token)
    {
        RequireOriginalAlive(token);
        if (!await current(token)) throw new UnauthorizedAccessException("The original Files Home binding retired.");
        RequireOriginalAlive(token);
    }

    private async Task RequireReadyAsync(CancellationToken token)
    { if ((await _readiness.CheckAsync(token)).State != CuiSceneAvailabilityState.Ready) throw new UnauthorizedAccessException("Home cannot authorise this Files view."); }
    private void ClearSources()
    {
        ClearOriginalTextPreview();
        ClearOriginalShareScopeEditor();
        Interlocked.Increment(ref _sharingSelectionVersion);
        _sharingPage = null;
        WithdrawFolderTabsOnRetirement();
        _page = null; _originalPageCurrent = null; _items.SelectedItem = null; _items.ItemsSource = null;
        RetireUnrepresentedDetailsRows();
        _query = ""; _search.Text = ""; _editName.Text = "";
        _moveSource = null; _showTechnicalDetails = false;
        if (_closing || _disposed || _lifetime.IsCancellationRequested)
        { _history.Clear(); _history.Add((null, "Files", false)); _historyIndex = 0; }
        Changed();
    }
    private void ApplyDisplayedNameSort()
    {
        RequireOriginalAlive(CancellationToken.None);
        if (_page is null) return;
        // Reorder retained metadata objects, never replace canonical identities or fetch a new page.
        var selected = _items.SelectedItem as HostedItemMetadata;
        var ordered = _sort.SelectedIndex == 1
            ? _page.Items.OrderByDescending(item => item.Name, StringComparer.CurrentCultureIgnoreCase)
            : _page.Items.OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase);
        _items.ItemsSource = ordered.ThenBy(item => item.Name, StringComparer.Ordinal)
            .ThenBy(item => item.Id.Value).ToArray();
        RetireUnrepresentedDetailsRows();
        RequireOriginalAlive(CancellationToken.None);
        _items.SelectedItem = selected is not null && _page.Items.Any(item => ReferenceEquals(item, selected))
            ? selected : null;
        RequireOriginalAlive(CancellationToken.None);
    }
    private void Changed()
    {
        var wideRows = Bounds.Width >= 900;
        if (OriginalAlive() && _detailsRowsWide != wideRows)
        {
            _detailsRowsWide = wideRows;
            foreach (var row in _detailsRowLoaders.ToArray())
            {
                row.Value.Set("Columns", wideRows ? "1fr 150 100 90" : "1fr 0 0 0");
                row.Value.Set("ShowMetadata", wideRows);
                row.Key.RefreshBindings();
            }
        }
        WithdrawChangedOriginalTextPreview();
        // Refused-state withdrawal remains observable after retirement. A live publication
        // rechecks its original lifetime after each synchronous native notification.
        var publishing = OriginalAlive();
        if (publishing && _page is not null && _boundStoreId is not null)
        {
            if (_activeFolderTab is null) InitializeFolderTabs();
            _folderTabsOwnerCurrent = _originalPageCurrent;
            _activeFolderTab!.LastError = null;
        }
        _sort.IsEnabled = Available && _page is not null && !IsPendingShareReview;
        _items.IsEnabled = !_busy && !IsPendingShareReview && OriginalAlive();
        _search.IsEnabled = Available && _pendingMutation is null;
        _editName.IsEnabled = Available && _pendingMutation is null;
        if (publishing) RequireOriginalAlive(CancellationToken.None);
        PropertyChanged?.Invoke(this, new(null));
        if (publishing) RequireOriginalAlive(CancellationToken.None);
    }
    private void RunOriginalNativeMutation(Action mutation)
    {
        Dispatcher.UIThread.VerifyAccess();
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_originalTasksSync)
        {
            if (!OriginalAlive()) return;
            _originalTasks.RemoveWhere(row => row.IsCompletedSuccessfully);
            _originalTasks.Add(completion.Task);
        }
        List<Exception> failures = [];
        try
        {
            mutation();
            RequireOriginalAlive(CancellationToken.None);
        }
        catch (Exception primary)
        {
            Add(failures, primary);
            lock (_originalTasksSync) _closing = true;
            try { ClearSources(); } catch (Exception cleanup) { Add(failures, cleanup); }
            lock (_originalTasksSync)
                foreach (var failure in failures) AddOriginalFailure(failure);
        }
        if (failures.Count == 0) completion.SetResult();
        else completion.SetException(failures.Count == 1 ? failures[0]
            : new AggregateException("Original Files native publication and cleanup failed.", failures));
    }

    // Publish each SAME original operation before it can enter a callback or await I/O.
    // Settled successful tasks are pruned; original failures remain until the owner drains them.
    private Task TrackOriginalAsync(Func<Task> operation)
    {
        Dispatcher.UIThread.VerifyAccess();
        lock (_originalTasksSync)
        {
            ObjectDisposedException.ThrowIf(_closing || _disposed, this);
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var original = RunOriginalAsync(start.Task, operation);
            _originalTasks.RemoveWhere(row => row.IsCompletedSuccessfully);
            _originalTasks.Add(original);
            start.SetResult();
            return original;
        }
    }
    private async Task RunOriginalAsync(Task start, Func<Task> operation)
    {
        await start;
        try { await operation(); }
        catch (Exception error)
        {
            lock (_originalTasksSync) { AddOriginalFailure(error); _closing = true; }
            // The refused state is published inside this SAME owned Task, before it can settle.
            try { Changed(); }
            catch (Exception cleanup)
            {
                lock (_originalTasksSync) AddOriginalFailure(cleanup);
                throw new AggregateException("Original Files task and refused-state notification failed.", error, cleanup);
            }
            throw;
        }
    }

    public Task CloseAndDrainAsync()
    {
        DemandExternalRegisteredRevealJoin();
        lock (_originalTasksSync)
        {
            if (_originalClose is not null) return _originalClose;
            _closing = true; _disposed = true;
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            List<Exception> withdrawal = [];
            _originalClose = CloseOriginalAsync(start.Task, _originalTasks.ToArray(), withdrawal);
            // The SAME close task is already published before native withdrawal notifications.
            // Preserve immediate UI disposal semantics without disposing resources used by I/O.
            if (Dispatcher.UIThread.CheckAccess())
            {
                try { ClearSources(); } catch (Exception error) { Add(withdrawal, error); }
                try { Content = null; } catch (Exception error) { Add(withdrawal, error); }
            }
            start.SetResult();
            return _originalClose;
        }
    }
    private async Task CloseOriginalAsync(Task start, Task[] originalTasks, List<Exception> originalWithdrawal)
    {
        await start;
        List<Exception> failures = [];
        foreach (var error in originalWithdrawal) Add(failures, error);
        try { _lifetime.Cancel(); } catch (Exception error) { Add(failures, error); }
        async Task OnUiAsync(Action action)
        {
            if (Dispatcher.UIThread.CheckAccess()) action();
            else await Dispatcher.UIThread.InvokeAsync(action);
        }
        // Retired metadata is withdrawn promptly while the original I/O remains owned and draining.
        try { await OnUiAsync(ClearSources); } catch (Exception error) { Add(failures, error); }
        try { await OnUiAsync(() => { Content = null; foreach (var loader in _detailsRowLoaders.Keys.ToArray()) loader.Dispose(); _detailsRowLoaders.Clear(); _detailsRowItems.Clear(); }); } catch (Exception error) { Add(failures, error); }
        foreach (var original in originalTasks)
            try { await original; } catch (Exception error) { Add(failures, error); }
        lock (_originalTasksSync)
            foreach (var error in _originalFailures) Add(failures, error);
        // The scene/semaphore/lifetime are disposed only after original operations settle.
        try { await OnUiAsync(() => _scene?.Dispose()); } catch (Exception error) { Add(failures, error); }
        try { _originalHostRetirement.Dispose(); } catch (Exception error) { Add(failures, error); }
        try { _folderIcon?.Dispose(); _folderIcon = null; } catch (Exception error) { Add(failures, error); }
        try { _operations.Dispose(); } catch (Exception error) { Add(failures, error); }
        try { _lifetime.Dispose(); } catch (Exception error) { Add(failures, error); }
        ThrowOriginalFailures(failures, "Original Files work and view close failed.");
    }
    private void AddOriginalFailure(Exception error)
    { if (!_originalFailures.Any(row => ReferenceEquals(row, error))) _originalFailures.Add(error); }
    private static void Add(List<Exception> failures, Exception error)
    { if (!failures.Any(row => ReferenceEquals(row, error))) failures.Add(error); }
    private static void ThrowOriginalFailures(List<Exception> failures, string message)
    {
        if (failures.Count == 1) ExceptionDispatchInfo.Capture(failures[0]).Throw();
        if (failures.Count > 1) throw new AggregateException(message, failures);
    }
    // Synchronous UI disposal begins, but never substitutes for, the owning awaited close.
    public void Dispose() => _ = CloseAndDrainAsync();
    public ValueTask DisposeAsync() => new(CloseAndDrainAsync());
    private Avalonia.Media.Imaging.Bitmap? _folderIcon;
    private Avalonia.Media.IImage? ResolveFilesImage(string source, Control target)
    {
        if (source != "files:folder") return null;
        if (_folderIcon is null)
        {
            using var stream = typeof(FilesNativeBrowserSurface).Assembly.GetManifestResourceStream("HavenOS.Files.NativeUI.Assets.FolderIcon.png")
                ?? throw new InvalidDataException("The maintained Files folder artwork is unavailable.");
            _folderIcon = new Avalonia.Media.Imaging.Bitmap(stream);
        }
        return _folderIcon;
    }

    private readonly Dictionary<CuiControlLoader, CuiViewModel> _detailsRowLoaders = [];
    private readonly Dictionary<CuiControlLoader, HostedItemMetadata> _detailsRowItems = [];
    private void RetireUnrepresentedDetailsRows()
    {
        // Only an actual ItemsSource replacement/withdrawal retires metadata rows.
        // Ordinary Browse tab detach leaves ItemsSource and every loader intact.
        var represented = _items.Items.OfType<HostedItemMetadata>().ToHashSet(ReferenceEqualityComparer.Instance);
        foreach (var row in _detailsRowItems.ToArray())
        {
            if (represented.Contains(row.Value)) continue;
            row.Key.Dispose(); // Failure stays in the existing original operation/close drain.
            _detailsRowItems.Remove(row.Key); _detailsRowLoaders.Remove(row.Key);
        }
    }
    private bool _detailsRowsWide;
    private Control BuildDetailsRow(HostedItemMetadata? item)
    {
        if (item is null) return new TextBlock();
        const string resource = "HavenOS.Files.NativeUI.UI.FilesDetailsRow.cui";
        using var stream = typeof(FilesNativeBrowserSurface).Assembly.GetManifestResourceStream(resource)
            ?? throw new InvalidDataException("The Files details row CUI is unavailable.");
        using var reader = new StreamReader(stream);
        var parser = new CuiRichParser(); var document = parser.Parse(reader.ReadToEnd(), resource);
        var registry = new CuiControlRegistry();
        registry.RegisterImageSourceResolver(ResolveFilesImage);
        var loader = new CuiControlLoader(registry);
        var values = new CuiViewModel();
        values.Set("Columns", Bounds.Width >= 900 ? "1fr 150 100 90" : "1fr 0 0 0");
        values.Set("ShowMetadata", Bounds.Width >= 900);
        values.Set("IsFolder", item.Kind == HostedItemKind.Folder);
        values.Set("Name", item.Name);
        values.Set("Modified", item.ModifiedAt.ToLocalTime().ToString("g"));
        values.Set("Kind", item.Kind == HostedItemKind.Folder ? "File folder" : string.IsNullOrEmpty(Path.GetExtension(item.Name)) ? item.Kind.ToString() : Path.GetExtension(item.Name).TrimStart('.').ToUpperInvariant() + " file");
        values.Set("Size", item.SizeBytes is { } bytes ? $"{bytes:N0} B" : "—");
        loader.SetBindingContext(values);
        var loaded = loader.TryLoad(document);
        if (loaded.Root is null || parser.Diagnostics.Diagnostics.Any(d => d.Severity == CuiDiagnosticSeverity.Error) ||
            loaded.Diagnostics.Any(d => d.Severity == CuiDiagnosticSeverity.Error))
        { loader.Dispose(); throw new InvalidDataException("The Files details row could not be rendered."); }
        _detailsRowLoaders.Add(loader, values);
        _detailsRowItems.Add(loader, item);
        var root = loaded.Root;
        // Browse may detach this same Files view when selecting another tab.
        // Keep row bindings alive until the original surface performs its owning close.
        return root;
    }

    private static CuiDocument LoadDocument()
    {
        const string name = "HavenOS.Files.NativeUI.UI.FilesBrowser.cui";
        using var stream = typeof(FilesNativeBrowserSurface).Assembly.GetManifestResourceStream(name)
            ?? throw new InvalidDataException("Owning Files browser CUI source is missing.");
        using var reader = new StreamReader(stream);
        var parser = new CuiRichParser(); var document = parser.Parse(reader.ReadToEnd(), name);
        if (parser.Diagnostics.Diagnostics.Any(diagnostic => diagnostic.Severity == CuiDiagnosticSeverity.Error))
            throw new InvalidDataException(string.Join(Environment.NewLine, parser.Diagnostics.Diagnostics));
        return document;
    }
}
