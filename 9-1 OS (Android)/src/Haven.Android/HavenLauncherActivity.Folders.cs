using Android.App;
using Android.Views;
using Android.Widget;
using NineToOne.Launcher;

namespace Haven.Android;

public sealed partial class HavenLauncherActivity
{
    private Dialog? _folderDialog;
    private Guid? _openFolderId;
    private LauncherStoredLayout? _openFolderLayout;
    private long _folderOpenGeneration;
    private View BuildFolderTile(LauncherPlacement icon, int width, int height)
    {
        var expected = _layout;
        var folder = expected?.Current.Folders.SingleOrDefault(f => f.Id == icon.FolderId);
        var button = new Button(this) { Text = folder is null ? "Unavailable folder" : $"{folder.Name}\n{folder.Items.Count} apps",
            ContentDescription = "Launcher folder " + folder?.Name, LayoutParameters = new ViewGroup.LayoutParams(width, height) };
        var originalRoot = _root; var originalEpoch = _widgetRenderEpoch;
        bool OriginalTileCurrent() => expected is not null && _activityStarted && _homeReady && !_launcherLifetime.IsCancellationRequested &&
            !IsDestroyed && !IsFinishing && originalRoot?.IsAttachedToWindow == true && ReferenceEquals(originalRoot, _root) &&
            ReferenceEquals(expected, _layout) && originalEpoch == _widgetRenderEpoch && button.IsAttachedToWindow && IsCurrentLauncherDescendant(button);
        button.Click += (_, _) =>
        {
            if (!OriginalTileCurrent() || folder is null || expected is null) return;
            if (_movingPlacementId is { } item) _ = EditLayoutAsync(layout => LauncherLayoutEdits.MoveToContainer(layout, item, folder.Id), expected);
            else _ = ShowFolderAsync(folder.Id, expected);
        };
        button.LongClick += (_, e) => { if (OriginalTileCurrent() && folder is not null) ShowFolderMenu(folder, icon, expected); e.Handled = true; };
        return button;
    }
    private async Task ShowFolderAsync(Guid id, LauncherStoredLayout expected)
    {
        var epoch = _widgetRenderEpoch; var generation = ++_folderOpenGeneration; var ct = _launcherLifetime.Token;
        var returnFocus = CurrentFocus;
        bool Current() => _activityStarted && !ct.IsCancellationRequested && epoch == _widgetRenderEpoch &&
            generation == _folderOpenGeneration && ReferenceEquals(_layout, expected);
        try
        {
            var original = DisplayedLayouts.Require(expected);
            await new AndroidLauncherFolderNavigation(WidgetSessions).ReadAsync(original, id, Current, ct);
            if (!Current()) return;
            _folderDialog?.Dismiss();
            var dialog = new Dialog(this); _folderDialog = dialog; _openFolderId = id; _openFolderLayout = expected;
            dialog.DismissEvent += (_, _) =>
            {
                if (ReferenceEquals(_folderDialog, dialog))
                {
                    _folderDialog = null; _openFolderId = null; _openFolderLayout = null;
                    if (Current() && returnFocus is not null && returnFocus.IsAttachedToWindow && IsCurrentLauncherDescendant(returnFocus))
                        returnFocus.RequestFocus();
                }
            };
            RefreshOpenFolder();
            if (!ReferenceEquals(_folderDialog, dialog)) return;
            dialog.Show(); dialog.Window?.SetLayout(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
        { if (Current()) Toast.MakeText(this, "The original folder is unavailable. Reopen the current launcher layout.", ToastLength.Short)?.Show(); }
    }
    private void RefreshOpenFolder()
    {
        if (_folderDialog is not { } dialog || _openFolderId is not { } id) return;
        var expected = _openFolderLayout;
        if (!_activityStarted || _launcherLifetime.IsCancellationRequested || !ReferenceEquals(_layout, expected))
        { dialog.Dismiss(); return; }
        var folder = expected?.Current.Folders.SingleOrDefault(f => f.Id == id);
        if (expected is null || folder is null) { dialog.Dismiss(); return; }
        var panel = new LinearLayout(this) { Orientation = Orientation.Vertical }; panel.SetPadding(Dp(12), Dp(12), Dp(12), Dp(12));
        panel.Background = HavenNativeSurface.Page();
        var header = new Button(this) { Text = folder.Name + " · Close", ContentDescription = "Close folder " + folder.Name };
        header.Click += (_, _) => dialog.Dismiss(); panel.AddView(header);
        var grid = new GridLayout(this) { ColumnCount = folder.Columns };
        var width = Math.Max(MinimumTileWidth, ((Resources?.DisplayMetrics?.WidthPixels ?? Dp(360)) - Dp(24)) / folder.Columns);
        var lastRow = folder.Items.Count == 0 ? 0 : folder.Items.Max(i => i.Row);
        var rows = Math.Min((1024 + folder.Columns - 1) / folder.Columns,
            lastRow + 1 + (_movingPlacementId is null ? 0 : 1));
        grid.RowCount = rows;
        for (var row = 0; row < rows; row++) for (var column = 0; column < folder.Columns; column++)
        {
            var item = folder.Items.SingleOrDefault(i => i.Column == column && i.Row == row);
            if (item is not null)
            {
                var app = _apps.SingleOrDefault(a => a.ApplicationId == item.ApplicationId)
                    ?? new LauncherApp("Unavailable application", "", "", null, item.ApplicationId, 0, "", "Unavailable profile", false, false);
                grid.AddView(BuildAppTile(app, width, Math.Max(Dp(96), TileHeight), item));
            }
            else
            {
                var targetColumn = column; var targetRow = row;
                var slot = new Button(this)
                {
                    Text = _movingPlacementId is null ? "" : "+", Enabled = _movingPlacementId is not null,
                    ContentDescription = $"Empty folder slot, row {row + 1}, column {column + 1}",
                    LayoutParameters = new ViewGroup.LayoutParams(width, Math.Max(Dp(96), TileHeight))
                };
                slot.SetBackgroundColor(global::Android.Graphics.Color.Transparent);
                var originalRoot = _root; var originalEpoch = _widgetRenderEpoch;
                var originalMoving = _movingPlacementId;
                slot.Click += (_, _) =>
                {
                    if (!_activityStarted || !_homeReady || _launcherLifetime.IsCancellationRequested || IsDestroyed || IsFinishing ||
                        originalRoot?.IsAttachedToWindow != true || !ReferenceEquals(originalRoot, _root) ||
                        originalEpoch != _widgetRenderEpoch || !ReferenceEquals(expected, _layout) ||
                        !ReferenceEquals(dialog, _folderDialog) || !dialog.IsShowing || !slot.IsAttachedToWindow) return;
                    if (originalMoving is { } moving)
                        _ = EditLayoutAsync(layout => LauncherLayoutEdits.MovePlacement(layout, moving, folder.Id, targetColumn, targetRow), expected);
                };
                grid.AddView(slot);
            }
        }
        if (folder.Items.Count == 0)
        {
            var empty = new TextView(this) { Text = "Choose Move shortcut on an app, then select this folder to place it here." };
            empty.SetTextColor(global::Android.Graphics.Color.White); panel.AddView(empty);
        }
        var vertical = new ScrollView(this); vertical.AddView(grid);
        var horizontal = new HorizontalScrollView(this); horizontal.AddView(vertical); panel.AddView(horizontal);
        dialog.SetContentView(panel); AndroidTypography.ApplyTree(panel);
        panel.Post(() =>
        {
            if (ReferenceEquals(_folderDialog, dialog) && dialog.Window?.DecorView?.FindFocus() is null &&
                ReferenceEquals(_layout, expected) && _activityStarted && !_launcherLifetime.IsCancellationRequested)
                header.RequestFocus();
        });
    }
    private void ShowFolderMenu(LauncherFolder folder, LauncherPlacement icon, LauncherStoredLayout? expected)
    {
        if (expected is null) return;
        var currentFolder = expected.Current.Folders.SingleOrDefault(f => f.Id == folder.Id);
        var currentIcon = LauncherLayoutEdits.Placements(expected.Current).SingleOrDefault(i => i.Id == icon.Id);
        if (currentFolder is null || currentIcon is null || currentIcon.FolderId != folder.Id) return;
        folder = currentFolder; icon = currentIcon;
        var choice = CaptureOriginalDrawerChoice(expected); if (!choice.Current()) return;
        var dialog = new AlertDialog.Builder(this); dialog.SetTitle(folder.Name);
        dialog.SetItems(new[] { "Open folder", "Rename folder", "Folder columns", "Move folder", "Move selected shortcut here", "Remove empty folder" }, (_, e) =>
        {
            var selected = e.Which;
            var originalMovingPlacement = _movingPlacementId;
            _ = choice.Accept(() =>
            {
            switch (selected)
            {
                case 0: return ShowFolderAsync(folder.Id, expected);
                case 1: AskPageName("Rename folder", folder.Name, name => EditLayoutAsync(layout => LauncherLayoutEdits.ConfigureFolder(layout, folder.Id, name, layout.Folders.Single(f => f.Id == folder.Id).Columns), expected)); break;
                case 2: ShowFolderColumns(folder, expected); break;
                case 3: _movingPlacementId = icon.Id; RenderPage(); break;
                case 4:
                    if (originalMovingPlacement is { } item) return EditLayoutAsync(layout => LauncherLayoutEdits.MoveToContainer(layout, item, folder.Id), expected);
                    else Toast.MakeText(this, "Choose Move shortcut on an app first.", ToastLength.Short)?.Show();
                    break;
                case 5: return EditLayoutAsync(layout => LauncherLayoutEdits.RemovePlacement(layout, icon.Id), expected);
            }
            return Task.CompletedTask;
            });
        });
        dialog.SetNegativeButton("Cancel", (_, _) => { }); ShowOriginalDrawerDialog(dialog, choice);
    }
    private void ShowFolderColumns(LauncherFolder folder, LauncherStoredLayout expected)
    {
        var choice = CaptureOriginalDrawerChoice(expected); if (!choice.Current()) return;
        var panel = new LinearLayout(this) { Orientation = Orientation.Vertical };
        var columns = new SeekBar(this) { Max = 4, Progress = folder.Columns - 3, ContentDescription = "Folder columns, three to seven" };
        var label = new TextView(this) { Text = $"{folder.Columns} columns" };
        columns.ProgressChanged += (_, _) => label.Text = $"{columns.Progress + 3} columns"; panel.AddView(label); panel.AddView(columns);
        var dialog = new AlertDialog.Builder(this); dialog.SetTitle(folder.Name); dialog.SetView(panel);
        dialog.SetPositiveButton("Save", (_, _) =>
        {
            var originalColumns = columns.Progress + 3; // Freeze scalar native command before owner await.
            _ = choice.Accept(() => EditLayoutAsync(layout => LauncherLayoutEdits.ConfigureFolder(layout, folder.Id,
                layout.Folders.Single(f => f.Id == folder.Id).Name, originalColumns), expected));
        });
        dialog.SetNegativeButton("Cancel", (_, _) => { }); ShowOriginalDrawerDialog(dialog, choice);
    }
}
