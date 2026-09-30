using Android.App;
using Android.Views;
using Android.Widget;
using NineToOne.Launcher;

namespace Haven.Android;

public sealed partial class HavenLauncherActivity
{
    private LinearLayout? _dockHost;
    private void RenderDock()
    {
        if (_dockHost is null) return;
        _dockHost.RemoveAllViews();
        if (_layout?.Current.Dock is not { } dock) { _dockHost.Visibility = ViewStates.Gone; return; }
        _dockHost.Visibility = ViewStates.Visible;
        var grid = new GridLayout(this) { RowCount = dock.Rows, ColumnCount = dock.Columns, ContentDescription = "Launcher dock" };
        var width = Math.Max(MinimumTileWidth, ((Resources?.DisplayMetrics?.WidthPixels ?? Dp(360)) - Dp(24)) / dock.Columns);
        for (var row = 0; row < dock.Rows; row++) for (var column = 0; column < dock.Columns; column++)
        {
            var placement = dock.Items.SingleOrDefault(i => i.Column == column && i.Row == row);
            if (placement?.FolderId is not null) grid.AddView(BuildFolderTile(placement, width, TileHeight));
            else if (placement is not null)
            {
                var app = _apps.SingleOrDefault(a => a.ApplicationId == placement.ApplicationId)
                    ?? new LauncherApp("Unavailable application", "", "", null, placement.ApplicationId, 0, "", "Unavailable profile", false, false);
                grid.AddView(BuildAppTile(app, width, TileHeight, placement));
            }
            else
            {
                var targetColumn = column; var targetRow = row;
                var slot = new Button(this) { Text = _movingPlacementId is null ? "" : "+", Enabled = _movingPlacementId is not null,
                    ContentDescription = $"Empty dock slot, row {row + 1}, column {column + 1}", LayoutParameters = new ViewGroup.LayoutParams(width, TileHeight) };
                slot.Click += (_, _) => { if (_movingPlacementId is { } id) _ = EditLayoutAsync(layout => LauncherLayoutEdits.MovePlacement(layout, id, dock.Id, targetColumn, targetRow)); };
                grid.AddView(slot);
            }
        }
        var scroll = new HorizontalScrollView(this) { FillViewport = true }; scroll.AddView(grid); _dockHost.AddView(scroll);
        AndroidTypography.ApplyTree(_dockHost);
    }
    private void ShowDockSettings()
    {
        if (_layout is null) return;
        var panel = new LinearLayout(this) { Orientation = Orientation.Vertical };
        var rows = new SeekBar(this) { Max = 2, Progress = (_layout.Current.Dock?.Rows ?? 1) - 1, ContentDescription = "Dock rows, one to three" };
        var columns = new SeekBar(this) { Max = 4, Progress = (_layout.Current.Dock?.Columns ?? 4) - 3, ContentDescription = "Dock columns, three to seven" };
        var label = new TextView(this);
        void Update() => label.Text = $"Dock: {rows.Progress + 1} rows × {columns.Progress + 3} columns";
        rows.ProgressChanged += (_, _) => Update(); columns.ProgressChanged += (_, _) => Update(); Update();
        panel.AddView(label); panel.AddView(rows); panel.AddView(columns);
        var dialog = new AlertDialog.Builder(this); dialog.SetTitle("Dock"); dialog.SetView(panel);
        dialog.SetPositiveButton("Save", (_, _) => _ = EditLayoutAsync(layout => LauncherLayoutEdits.ConfigureDock(layout, rows.Progress + 1, columns.Progress + 3)));
        dialog.SetNeutralButton("Hide empty dock", (_, _) => _ = EditLayoutAsync(LauncherLayoutEdits.RemoveDock));
        dialog.SetNegativeButton("Cancel", (_, _) => { }); dialog.Show();
    }
    private Task AddOrMoveToDockAsync(Guid applicationId, LauncherPlacement? placement) => EditLayoutAsync(layout =>
    {
        var configured = layout.Dock is null ? LauncherLayoutEdits.ConfigureDock(layout, 1, 4) : layout;
        return placement is null ? LauncherLayoutEdits.AddDockApplication(configured, applicationId)
            : LauncherLayoutEdits.MoveToContainer(configured, placement.Id, configured.Dock!.Id);
    });
}
