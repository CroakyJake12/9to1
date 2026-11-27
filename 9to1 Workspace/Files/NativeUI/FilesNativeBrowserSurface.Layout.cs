using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using CakeOS.Cui.Runtime;
using HavenOS.Files.NativeHost;

namespace HavenOS.Files.NativeUI;

// Presentation of the same issued folder page. Switching view never performs a provider read,
// replaces an item, changes a reviewed mutation, or creates another navigation/operation owner.
public sealed partial class FilesNativeBrowserSurface
{
    private bool _gridView;
    private bool _detailsVisible = true;
    private bool _nameEditorVisible;
    private (bool Grid, bool Wide, bool Stacked)? _publishedRowLayout;
    private readonly Dictionary<ListBoxItem, FileItemPaint> _fileItemPaint = [];

    private sealed record FileItemPaint(bool Selected, bool Grid, IDisposable Background, IDisposable Border) : IDisposable
    {
        public void Dispose() { Background.Dispose(); Border.Dispose(); }
    }

    private const string FileItemPresentationSource = "Files item presentation";

    private sealed class FileItemResourceBinding : IObserver<object?>, IDisposable
    {
        private readonly ListBoxItem _row;
        private readonly string _property;
        private readonly IDisposable _subscription;

        internal FileItemResourceBinding(ListBoxItem row, string property, string resource)
        {
            _row = row; _property = property;
            _subscription = row.GetResourceObservable(resource).Subscribe(this);
        }
        public void OnNext(object? value)
        {
            if (value is IBrush)
                CuiPropertyPresentation.Set(_row, _property, value, CuiPropertyLevel.Instance, FileItemPresentationSource);
            else
                CuiPropertyPresentation.Remove(_row, _property, CuiPropertyLevel.Instance, FileItemPresentationSource);
        }
        public void OnError(Exception error) => throw new InvalidOperationException("The Files item theme resource could not be observed.", error);
        public void OnCompleted() { }
        public void Dispose()
        {
            _subscription.Dispose();
            CuiPropertyPresentation.Remove(_row, _property, CuiPropertyLevel.Instance, FileItemPresentationSource);
        }
    }

    private double RailWidth => Bounds.Width <= 750 ? 68 : 232;
    private bool HasCurrentSelection => OriginalAlive() && _page is { } page &&
        _items.SelectedItem is HostedItemMetadata row && page.Items.Any(item => ReferenceEquals(item, row));
    private HostedItemMetadata? SelectedPresentationItem => HasCurrentSelection ? (HostedItemMetadata)_items.SelectedItem! : null;
    private bool DetailsShown => _detailsVisible && HasCurrentSelection;
    private bool SideBySideDetails => Bounds.Width >= 1180;
    private double StackedDetailsHeight => Math.Min(220, Math.Max(140, Bounds.Height * .28));
    private double ItemViewportWidth => Math.Max(180, _items.Bounds.Width > 0 ? _items.Bounds.Width - 20 :
        Bounds.Width - (Bounds.Width >= 520 ? RailWidth : 0) - (SideBySideDetails && DetailsShown ? 250 : 0) - 48);
    private bool WideItemColumns => ItemViewportWidth >= 650;
    private bool StackedItemAvailability => ItemViewportWidth < 360;
    private string ListItemColumns => WideItemColumns ? "1fr 130 80 120" : StackedItemAvailability ? "1fr 0 0 0" : "1fr 0 0 115";

    private void InitializeFilesPresentation()
    {
        // One panel and one item template are retained across list/grid switches. The bounded
        // provider page remains the sole ItemsSource; no second selection projection exists.
        _items.ItemsPanel = new FuncTemplate<Panel?>(() => new WrapPanel { Orientation = Orientation.Horizontal });
        ScrollViewer.SetHorizontalScrollBarVisibility(_items, ScrollBarVisibility.Disabled);
        _items.SizeChanged += (_, change) => { if (change.WidthChanged) Changed(); };
        _items.ContainerClearing += (_, args) =>
        {
            if (args.Container is ListBoxItem row)
            {
                row.PropertyChanged -= OnFileItemPropertyChanged;
                if (_fileItemPaint.Remove(row, out var paint)) paint.Dispose();
            }
        };
    }

    private void PrepareFileItemContainer(ListBoxItem row)
    {
        // The shared primitive theme owns the presentation cascade. Declare the Files
        // container geometry there so its template cannot replace it with menu-row padding.
        CuiPropertyPresentation.Set(row, "Padding", new Thickness(0), CuiPropertyLevel.Instance, FileItemPresentationSource);
        CuiPropertyPresentation.Set(row, "HorizontalContentAlignment", HorizontalAlignment.Stretch, CuiPropertyLevel.Instance, FileItemPresentationSource);
        CuiPropertyPresentation.Set(row, "VerticalContentAlignment", VerticalAlignment.Stretch, CuiPropertyLevel.Instance, FileItemPresentationSource);
        row.PropertyChanged -= OnFileItemPropertyChanged;
        row.PropertyChanged += OnFileItemPropertyChanged;
        PaintFileItem(row);
    }

    private void OnFileItemPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs change)
    {
        if (change.Property == ListBoxItem.IsSelectedProperty && sender is ListBoxItem row) PaintFileItem(row);
    }

    private void PaintFileItem(ListBoxItem row)
    {
        CuiPropertyPresentation.Set(row, "CornerRadius", new CornerRadius(_gridView ? 13 : 12), CuiPropertyLevel.Instance, FileItemPresentationSource);
        CuiPropertyPresentation.Set(row, "BorderWidth", new Thickness(_gridView ? 1 : 0), CuiPropertyLevel.Instance, FileItemPresentationSource);
        if (_fileItemPaint.TryGetValue(row, out var previous) && previous.Selected == row.IsSelected && previous.Grid == _gridView) return;
        if (_fileItemPaint.Remove(row, out previous)) previous.Dispose();
        // Resources remain live through the original scene/theme scope. Instance paint
        // still yields to CUI's selected/focus/disabled overlays and contrast resources.
        var background = new FileItemResourceBinding(row, "Background",
            row.IsSelected ? "CuiSelectionBrush" : _gridView ? "CuiPanelBrush" : "CuiTransparentBrush");
        var border = new FileItemResourceBinding(row, "BorderColor", row.IsSelected ? "CuiPrimaryBrush" : "CuiLineBrush");
        _fileItemPaint.Add(row, new(row.IsSelected, _gridView, background, border));
    }

    private void UpdateFilesPresentation()
    {
        if (!OriginalAlive()) return;
        if (_items.ItemsPanelRoot is WrapPanel panel)
        {
            var width = ItemViewportWidth;
            var columns = _gridView ? Math.Clamp((int)Math.Floor((width + 14) / 204), 1, 4) : 1;
            panel.ItemSpacing = _gridView ? 14 : 0;
            panel.LineSpacing = _gridView ? 14 : 0;
            panel.ItemWidth = Math.Max(1, (width - (columns - 1) * panel.ItemSpacing) / columns);
            panel.ItemHeight = _gridView ? 190 : double.NaN;
            foreach (var row in panel.Children.OfType<ListBoxItem>()) PaintFileItem(row);
        }
        var layout = (_gridView, WideItemColumns, StackedItemAvailability);
        if (_publishedRowLayout == layout) return;
        _publishedRowLayout = layout;
        foreach (var row in _detailsRowLoaders.ToArray())
        {
            ApplyFileRowPresentation(row.Value);
            row.Key.RefreshBindings();
            RequireOriginalAlive(CancellationToken.None);
        }
    }

    private void RetireFileItemPresentation()
    {
        foreach (var row in _fileItemPaint.ToArray())
        {
            row.Key.PropertyChanged -= OnFileItemPropertyChanged;
            row.Value.Dispose();
            _fileItemPaint.Remove(row.Key);
        }
    }

    private void ApplyFileRowPresentation(CuiViewModel values)
    {
        values.Set("Columns", _gridView ? "1fr" : ListItemColumns);
        values.Set("Rows", _gridView || StackedItemAvailability ? "Auto Auto" : "Auto");
        values.Set("RowPadding", _gridView ? "15 22 15 18" : "16 12");
        values.Set("RowMinimumHeight", _gridView ? 188 : 58);
        values.Set("NameColumns", _gridView ? "1fr" : "Auto 1fr");
        values.Set("NameRows", _gridView ? "70 Auto" : "Auto");
        values.Set("NameRow", _gridView ? 1 : 0);
        values.Set("NameColumn", _gridView ? 0 : 1);
        values.Set("NameAlignment", _gridView ? "Center" : "Left");
        values.Set("NameTextAlignment", _gridView ? "Center" : "Left");
        values.Set("ArtworkSize", _gridView ? 58 : 20);
        values.Set("BadgeSize", _gridView ? 12 : 8);
        values.Set("ShowMetadata", !_gridView && WideItemColumns);
        values.Set("AvailabilityRow", _gridView || StackedItemAvailability ? 1 : 0);
        values.Set("AvailabilityColumn", _gridView || StackedItemAvailability ? 0 : 3);
        values.Set("AvailabilityAlignment", _gridView ? "Center" : "Left");
    }

    private static bool IsPresentationCommand(string command) => command is
        "9to1.Files.View.List" or "9to1.Files.View.Grid" or "9to1.Files.Details.Toggle" or
        "9to1.Files.Name.Show" or "9to1.Files.Name.Cancel" or "9to1.Files.Name.Rename";

    private bool PresentationActionAvailable(string command) => Available && (command switch
    {
        "9to1.Files.View.List" or "9to1.Files.View.Grid" => _page is not null,
        "9to1.Files.Details.Toggle" => HasCurrentSelection,
        "9to1.Files.Name.Show" => _pendingMutation is null && _page is { IsTrash: false },
        "9to1.Files.Name.Cancel" => _pendingMutation is null,
        "9to1.Files.Name.Rename" => InDrive
            ? _pendingMutation is null && _page is { IsTrash: false } && HasCurrentSelection
            : CanBeginLocalRename(),
        _ => false
    });

    private Task InvokePresentationAsync(string command, CancellationToken token)
    {
        Dispatcher.UIThread.VerifyAccess();
        RequireOriginalAlive(token);
        if (!PresentationActionAvailable(command)) return Task.CompletedTask;
        switch (command)
        {
            case "9to1.Files.View.List": _gridView = false; break;
            case "9to1.Files.View.Grid": _gridView = true; break;
            case "9to1.Files.Details.Toggle": _detailsVisible = !_detailsVisible; break;
            case "9to1.Files.Name.Show": _nameEditorVisible = true; break;
            case "9to1.Files.Name.Rename":
                _nameEditorVisible = true;
                if (InDrive && SelectedPresentationItem is { } driveItem)
                {
                    // Only prepare the displayed draft here. The retained Rename control
                    // still dispatches through the existing Drive operation owner.
                    _editName.Text = driveItem.Name;
                    _editName.SelectAll();
                    _status = "Enter a new name, then choose Rename to update this Google Drive item.";
                }
                else BeginLocalRename();
                break;
            case "9to1.Files.Name.Cancel":
                _localRenameDraft = null; _editName.Text = ""; _nameEditorVisible = false;
                _status = "Name draft discarded. No file was changed."; break;
        }
        Changed();
        RequireOriginalAlive(token);
        if (command == "9to1.Files.Name.Show" || command == "9to1.Files.Name.Rename" && InDrive) _editName.Focus();
        return Task.CompletedTask;
    }

    private bool TryGetPresentationValue(string path, out object? value)
    {
        var selected = SelectedPresentationItem;
        value = path switch
        {
            "NavigationColumns" => Bounds.Width < 720 ? "Auto 1fr" : "Auto 1fr 260",
            "SearchRow" => Bounds.Width < 720 ? 1 : 0,
            "SearchColumn" => Bounds.Width < 720 ? 0 : 2,
            "SearchColumnSpan" => Bounds.Width < 720 ? 2 : 1,
            "CompactLayout" => Bounds.Width < 520,
            "SidebarVisible" => Bounds.Width >= 520,
            "SidebarWidth" => RailWidth,
            "WideSidebar" => Bounds.Width > 750,
            "DeviceLabel" => Bounds.Width > 750 ? "This device" : "PC",
            "TrashLabel" => Bounds.Width > 750 ? "Trash" : "Bin",
            "DriveLabel" => Bounds.Width > 750 ? "Google Drive" : "Web",
            "ProvidersLabel" => Bounds.Width > 750 ? "Drives" : "More",
            "DeviceSelected" => _page is { IsTrash: false } && !InDrive,
            "TrashSelected" => _page is { IsTrash: true },
            "DriveSelected" => _page is { IsTrash: false } && InDrive,
            "SidebarButtonPadding" => Bounds.Width > 750 ? "10 6" : "4",
            "SidebarButtonAlignment" => Bounds.Width > 750 ? "Left" : "Center",
            "BodyColumns" => (Bounds.Width >= 520 ? RailWidth.ToString(System.Globalization.CultureInfo.InvariantCulture) + " " : "") +
                "1fr" + (SideBySideDetails && DetailsShown ? " 250" : ""),
            // A definite compact detail row gives its retained inner scroller a finite
            // viewport, while reserving the remaining height for the original file list.
            "BodyRows" => SideBySideDetails ? "1fr" : "1fr " +
                (DetailsShown ? StackedDetailsHeight : 0).ToString(System.Globalization.CultureInfo.InvariantCulture),
            "MainColumn" => Bounds.Width >= 520 ? 1 : 0,
            "DetailsColumn" => SideBySideDetails ? 2 : Bounds.Width >= 520 ? 1 : 0,
            "DetailsRow" => SideBySideDetails ? 0 : 1,
            "DetailsWidth" => SideBySideDetails ? 250d : double.NaN,
            "DetailsMaxHeight" => SideBySideDetails ? Math.Max(200, Bounds.Height) : StackedDetailsHeight,
            "DetailsPadding" => SideBySideDetails ? 22 : 12,
            "DetailsGap" => SideBySideDetails ? 12 : 6,
            "HasSelection" => HasCurrentSelection,
            "DetailsShown" => DetailsShown,
            "ListView" => !_gridView,
            "GridView" => _gridView,
            "CanChangeView" => Available && _page is not null,
            "CanToggleDetails" => PresentationActionAvailable("9to1.Files.Details.Toggle"),
            "DetailsToggleLabel" => DetailsShown ? "Hide details" : "Details",
            "WideColumns" => WideItemColumns,
            "AvailabilityHeadingVisible" => !StackedItemAvailability,
            "ItemColumns" => ListItemColumns,
            "ItemCountLabel" => _page is { } page ? $"{page.Items.Count} items" + (HasCurrentSelection ? " · 1 selected" : "") : "",
            "EmptyFolder" => !_busy && _page is { Items.Count: 0 },
            "EmptyTitle" => _page?.IsTrash == true ? "Trash is empty" : _query.Length > 0 ? "No matching items" : "This folder is empty",
            "EmptyMessage" => _page?.IsTrash == true ? "Items moved to Trash remain recoverable here." : _query.Length > 0
                ? "Try another name or clear the search to see this folder." : "Create a folder or open another location.",
            "UnavailableFolder" => !_busy && _initialized && _page is null,
            "NameEditorVisible" => _nameEditorVisible || _localRenameDraft is not null || !string.IsNullOrWhiteSpace(_editName.Text),
            "CanShowNameEditor" => PresentationActionAvailable("9to1.Files.Name.Show"),
            "CanCancelName" => PresentationActionAvailable("9to1.Files.Name.Cancel"),
            "CanBeginRename" => PresentationActionAvailable("9to1.Files.Name.Rename"),
            "NameEditorHint" => InDrive ? "Changes here apply to your connected Google Drive account."
                : _page?.ParentID is null ? "Open a folder to create a child folder. Configured app folders keep their registered names."
                : "Enter a folder name, or select an item and enter its new name. Review the exact change before applying it.",
            "CreateAccessibleName" => InDrive ? "Create a folder in Google Drive" : "Review creating a folder in Home",
            "RenameAccessibleName" => InDrive ? "Rename the selected Google Drive item" : "Review renaming the selected item in Home",
            "TrashAccessibleName" => InDrive ? "Move the selected Google Drive item to recoverable Trash" : "Review moving the selected item to recoverable Trash in Home",
            "RestoreAccessibleName" => InDrive ? "Restore the selected item from Google Drive Trash" : "Review restoring the selected deleted item to its original folder in Home",
            "ReviewVisible" => _pendingMutation is not null || _moveSource is not null,
            "SelectedName" => selected?.Name ?? "",
            "SelectedArtworkVisible" => SideBySideDetails,
            "SelectedSummaryColumns" => SideBySideDetails ? "1fr" : "1fr Auto",
            "SelectedOpenColumn" => SideBySideDetails ? 0 : 1,
            "SelectedOpenRow" => SideBySideDetails ? 1 : 0,
            "SelectedKind" => selected is null ? "" : KindLabel(selected),
            "SelectedFolder" => selected?.Kind == HostedItemKind.Folder,
            "SelectedFile" => selected is not null && selected.Kind != HostedItemKind.Folder,
            "SelectedBadge" => selected is null ? "" : FileBadge(selected),
            "SelectedAvailability" => selected is null ? "" : AvailabilityLabel(selected.Availability),
            "SelectedLocation" => selected is null ? "" : _page!.FolderTitle,
            "SelectedOpenLabel" => selected?.Kind == HostedItemKind.Folder ? "Open folder" : InDrive ? "Open in Google Drive"
                : selected is not null && FilesNativeBrowserService.IsBoardDocumentCandidate(selected) ? "Open in Boards"
                : CanOpenTextSelection ? "Preview text" : CanOpenSelection ? "Inspect package" : "Open",
            "SelectedOpenUnavailable" => selected is not null && (_page!.IsTrash || !InDrive && !CanOpenSelection),
            "SelectedOpenReason" => _page?.IsTrash == true ? "Restore this item to its original folder before opening it."
                : selected is not null && FilesNativeBrowserService.IsBoardDocumentCandidate(selected)
                    ? BoardOpenUnavailableReason
                    : "This item does not have an available opening handler here. Its original file is unchanged.",
            _ => null
        };
        return path is "NavigationColumns" or "SearchRow" or "SearchColumn" or "SearchColumnSpan" or "CompactLayout" or
            "SidebarVisible" or "SidebarWidth" or "WideSidebar" or "DeviceLabel" or "TrashLabel" or "DriveLabel" or "ProvidersLabel" or
            "DeviceSelected" or "TrashSelected" or "DriveSelected" or
            "SidebarButtonPadding" or "SidebarButtonAlignment" or "BodyColumns" or "BodyRows" or "MainColumn" or
            "DetailsColumn" or "DetailsRow" or "DetailsWidth" or "DetailsMaxHeight" or "DetailsPadding" or "DetailsGap" or
            "HasSelection" or "DetailsShown" or
            "ListView" or "GridView" or "CanChangeView" or "CanToggleDetails" or "DetailsToggleLabel" or "WideColumns" or "AvailabilityHeadingVisible" or
            "ItemColumns" or "ItemCountLabel" or "EmptyFolder" or "EmptyTitle" or "EmptyMessage" or "UnavailableFolder" or
            "NameEditorVisible" or "CanShowNameEditor" or "CanCancelName" or "CanBeginRename" or "NameEditorHint" or "ReviewVisible" or
            "CreateAccessibleName" or "RenameAccessibleName" or "TrashAccessibleName" or "RestoreAccessibleName" or
            "SelectedName" or "SelectedArtworkVisible" or "SelectedSummaryColumns" or "SelectedOpenColumn" or "SelectedOpenRow" or "SelectedKind" or "SelectedFolder" or "SelectedFile" or "SelectedBadge" or "SelectedAvailability" or
            "SelectedLocation" or "SelectedOpenLabel" or "SelectedOpenUnavailable" or "SelectedOpenReason";
    }

    private static string FileBadge(HostedItemMetadata item) => item.Kind == HostedItemKind.Folder ? "" :
        Path.GetExtension(item.Name).TrimStart('.').ToUpperInvariant() is { Length: > 0 and <= 4 } extension ? extension : "FILE";

    private static string KindLabel(HostedItemMetadata item) => item.Kind == HostedItemKind.Folder ? "Folder" :
        FilesNativeBrowserService.IsBoardDocumentCandidate(item) ? "Boards document" :
        Path.GetExtension(item.Name) is { Length: > 1 } extension ? extension[1..].ToUpperInvariant() + " file" : item.Kind.ToString();

    private static string AvailabilityLabel(SyncAvailability value) => value switch
    {
        SyncAvailability.CloudOnly => "Cloud only",
        SyncAvailability.Hydrating => "Downloading",
        SyncAvailability.AvailableOffline => "Available offline",
        SyncAvailability.AlwaysAvailable => "On this device",
        SyncAvailability.LocalChanges => "Local changes",
        SyncAvailability.Uploading => "Uploading",
        SyncAvailability.Synced => "Synced",
        SyncAvailability.Conflict => "Sync conflict",
        SyncAvailability.Paused => "Sync paused",
        SyncAvailability.Error => "Sync error",
        _ => "Availability unknown"
    };
}
