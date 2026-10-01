using Android.App;
using Android.Appwidget;
using Android.Views;
using Android.Widget;
using Haven.Desktop;
using Microsoft.Extensions.DependencyInjection;
using NineToOne.Launcher;

namespace Haven.Android;

public sealed partial class HavenLauncherActivity
{
    private LauncherSessionSnapshot? _pendingWidgetSession;
    private string? _pendingWidgetAuthority;
    private long _widgetRenderEpoch;
    private AndroidLauncherWidgetBindings? _widgetBindings;
    private readonly List<AppWidgetHostView> _mountedWidgetViews = [];
    private readonly List<AlertDialog> _widgetDialogs = [];
    private void ShowWidgetDialog(AlertDialog.Builder builder)
    {
        var dialog = builder.Create(); if (dialog is null) return;
        _widgetDialogs.Add(dialog); dialog.DismissEvent += (_, _) => _widgetDialogs.Remove(dialog); dialog.Show();
    }
    private void CloseWidgetDialogs()
    {
        foreach (var dialog in _widgetDialogs.ToArray()) dialog.Dismiss();
        _widgetDialogs.Clear();
    }
    private HomeLauncherSession WidgetSessions => (App.Services ?? throw new InvalidOperationException("Home is unavailable.")).GetRequiredService<HomeLauncherSession>();
    private AndroidLauncherWidgetBindings WidgetBindings => _widgetBindings ??= new(new AndroidLauncherWidgetPreferenceStorage(Preferences));
    private AndroidLauncherWidgetPlatform WidgetPlatform => new(this,
        _widgetHost ?? throw new InvalidOperationException("Android widget host is unavailable."),
        _widgetManager ?? throw new InvalidOperationException("Android widget manager is unavailable."));

    private void ClearMountedWidgets()
    {
        _widgetRenderEpoch++;
        _widgetHost?.ReleaseHostedViews();
        foreach (var view in _mountedWidgetViews)
        {
            if (view.Parent is ViewGroup parent) parent.RemoveView(view);
            view.Dispose();
        }
        _mountedWidgetViews.Clear();
    }

    private View BuildWidgetCell(LauncherWidgetPlacement widget, int width, int height, LauncherStoredLayout expected)
    {
        var box = new LinearLayout(this) { Orientation = Orientation.Vertical };
        var controls = new Button(this) { Text = widget.Label + " · Manage", ContentDescription = "Manage " + widget.Label };
        controls.Click += (_, _) => ShowWidgetPlacementMenu(widget, expected);
        box.AddView(controls, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, Dp(42)));
        var body = new FrameLayout(this);
        body.AddView(new TextView(this) { Text = "Widget unavailable · Saved placement retained", Gravity = GravityFlags.Center });
        box.AddView(body, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, 0, 1));
        box.LayoutParameters = new GridLayout.LayoutParams(GridLayout.InvokeSpec(widget.Row, widget.RowSpan), GridLayout.InvokeSpec(widget.Column, widget.ColumnSpan))
        { Width = width * widget.ColumnSpan, Height = height * widget.RowSpan };
        _ = MountWidgetAsync(body, widget.Id, expected, _widgetRenderEpoch);
        return box;
    }

    private async Task MountWidgetAsync(FrameLayout body, Guid id, LauncherStoredLayout expected, long epoch)
    {
        AppWidgetHostView? view = null;
        try
        {
            var session = await WidgetSessions.ReadAsync(_launcherLifetime.Token);
            if (session is null || session.Layout.AuthorityId != expected.AuthorityId || session.Layout.Revision != expected.Revision) return;
            var platform = WidgetPlatform;
            view = await new AndroidLauncherWidgetViews<AppWidgetHostView>(WidgetSessions, WidgetBindings, platform).CreateAsync(session, id, _launcherLifetime.Token);
            if (view is null) return;
            if (epoch != _widgetRenderEpoch || !_activityStarted || !await WidgetSessions.IsCurrentAsync(session, _launcherLifetime.Token)) return;
            body.RemoveAllViews(); body.AddView(view, new FrameLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent));
            _mountedWidgetViews.Add(view); view = null;
        }
        catch (OperationCanceledException) when (_launcherLifetime.IsCancellationRequested) { }
        catch (Exception error) { global::Android.Util.Log.Warn("HavenLauncher", "Widget mount unavailable: " + error.Message); }
        finally { if (view is not null) WidgetPlatform.ReleaseView(view); }
    }

    private async Task BeginWidgetPickAsync()
    {
        try
        {
            if (!OperatingSystem.IsAndroidVersionAtLeast(26)) throw new InvalidOperationException("Android widget ownership verification requires Android 8 or later.");
            var session = await WidgetSessions.ReadAsync(_launcherLifetime.Token);
            if (session is null || _pendingWidgetId != AppWidgetManager.InvalidAppwidgetId) return;
            _pendingWidgetSession = session; _pendingWidgetAuthority = session.Layout.AuthorityId;
            PickAndroidWidgetCore();
        }
        catch (Exception error) { Toast.MakeText(this, error.Message, ToastLength.Long)?.Show(); }
    }

    private async Task SaveConfiguredWidgetAsync(int widgetId, LauncherSessionSnapshot? expected, string? authority, Guid? existingPlacement = null)
    {
        var entered = false;
        try
        {
            await _layoutEdits.WaitAsync(_launcherLifetime.Token); entered = true;
            var provider = WidgetPlatform.ReadOwnedProvider(widgetId) ?? throw new InvalidOperationException("The Android widget owner is unavailable; its binding has been retained.");
            if (string.IsNullOrWhiteSpace(authority))
            { SaveWidgetId(widgetId); throw new InvalidOperationException("Choose a Home profile for this retained widget from Widgets → Associate retained widget."); }
            var priorBinding = WidgetBindings.Read().SingleOrDefault(value => value.AppWidgetId == widgetId);
            if (priorBinding?.PlacementId is { } priorPlacement)
            {
                var currentOwner = await WidgetSessions.ReadAsync(_launcherLifetime.Token);
                if (currentOwner is null || currentOwner.Layout.AuthorityId != authority || currentOwner.Layout.Current.Widgets.Any(value => value.Id == priorPlacement))
                    throw new InvalidOperationException("This widget is already associated, or its Home profile is unavailable.");
            }
            var binding = new AndroidLauncherWidgetBinding(widgetId, authority, null, provider);
            WidgetBindings.Associate(binding); // Recoverable if Home persistence cannot complete.
            if (expected is null || !await WidgetSessions.IsCurrentAsync(expected, _launcherLifetime.Token))
                throw new InvalidOperationException("Home changed during widget selection. The configured widget is retained for its original profile.");
            if (existingPlacement is { } target)
            {
                var owned = await WidgetSessions.ReadWidgetAsync(expected, target, _launcherLifetime.Token);
                if (owned?.Placement.Android != provider) throw new UnauthorizedAccessException("The saved widget owner changed. Refresh before associating it.");
                WidgetBindings.Associate(binding with { PlacementId = target });
                if (await WidgetSessions.IsCurrentAsync(expected, _launcherLifetime.Token)) { _layout = expected.Layout; RenderPage(); }
                return;
            }
            var label = _widgetManager?.GetAppWidgetInfo(widgetId)?.LoadLabel(PackageManager ?? throw new InvalidOperationException("Android package manager is unavailable.")) ?? "Android widget";
            Guid placement = default;
            var saved = await WidgetSessions.EditAsync(expected, layout =>
            {
                var added = LauncherLayoutEdits.AddWidget(layout, layout.ActivePageId, label, Math.Min(2, layout.Columns), Math.Min(2, layout.Rows), android: provider);
                placement = added.Widgets[^1].Id; return added;
            }, _launcherLifetime.Token);
            WidgetBindings.Associate(binding with { PlacementId = placement });
            var current = await WidgetSessions.ReadAsync(_launcherLifetime.Token);
            if (current is not null && current.Layout.AuthorityId == saved.AuthorityId && current.Layout.Revision == saved.Revision) _layout = saved;
            RenderPage();
        }
        catch (Exception error) { Toast.MakeText(this, error.Message, ToastLength.Long)?.Show(); }
        finally { if (entered) _layoutEdits.Release(); }
    }

    private async Task ShowRetainedWidgetsAsync()
    {
        try
        {
            var session = await WidgetSessions.ReadAsync(_launcherLifetime.Token); if (session is null) return;
            var bindings = WidgetBindings.Read();
            var candidates = bindings.Where(value => value.HomeAuthorityId == session.Layout.AuthorityId && (value.PlacementId is null || !session.Layout.Current.Widgets.Any(widget => widget.Id == value.PlacementId))).Select(value => value.AppWidgetId)
                .Concat(ReadWidgetIds().Where(id => bindings.All(value => value.AppWidgetId != id))).Distinct()
                .Where(id => WidgetPlatform.ReadOwnedProvider(id) is not null).ToArray();
            var dialog = new AlertDialog.Builder(this); dialog.SetTitle("Associate retained widget with this Home profile");
            if (candidates.Length == 0) dialog.SetMessage("No available unassigned widgets for this profile.");
            else dialog.SetItems(candidates.Select(id => _widgetManager?.GetAppWidgetInfo(id)?.LoadLabel(PackageManager ?? throw new InvalidOperationException("Android package manager is unavailable.")) ?? "Android widget").ToArray(),
                (_, args) => { if (args.Which >= 0 && args.Which < candidates.Length) ShowWidgetAssociationTargets(candidates[args.Which], session); });
            dialog.SetNegativeButton("Cancel", (_, _) => { }); ShowWidgetDialog(dialog);
        }
        catch (Exception error) { Toast.MakeText(this, error.Message, ToastLength.Long)?.Show(); }
    }

    private void ShowWidgetAssociationTargets(int widgetId, LauncherSessionSnapshot session)
    {
        try
        {
            var provider = WidgetPlatform.ReadOwnedProvider(widgetId) ?? throw new InvalidOperationException("The widget owner is unavailable. Its configuration was retained.");
            var bindings = WidgetBindings.Read();
            var saved = session.Layout.Current.Widgets.Where(widget => widget.Android == provider &&
                bindings.All(binding => binding.HomeAuthorityId != session.Layout.AuthorityId || binding.PlacementId != widget.Id)).ToArray();
            if (saved.Length == 0) { _ = SaveConfiguredWidgetAsync(widgetId, session, session.Layout.AuthorityId); return; }
            var choices = saved.Select(widget => $"{widget.Label} · saved row {widget.Row + 1}, column {widget.Column + 1}").Append("Add a new placement").ToArray();
            var dialog = new AlertDialog.Builder(this); dialog.SetTitle("Choose saved widget placement");
            dialog.SetItems(choices, (_, args) =>
            {
                if (args.Which >= 0 && args.Which <= saved.Length)
                    _ = SaveConfiguredWidgetAsync(widgetId, session, session.Layout.AuthorityId, args.Which < saved.Length ? saved[args.Which].Id : null);
            });
            dialog.SetNegativeButton("Cancel", (_, _) => { }); ShowWidgetDialog(dialog);
        }
        catch (Exception error) { Toast.MakeText(this, error.Message, ToastLength.Long)?.Show(); }
    }

    private void ShowWidgetPlacementMenu(LauncherWidgetPlacement widget, LauncherStoredLayout expected)
    {
        var dialog = new AlertDialog.Builder(this); dialog.SetTitle(widget.Label);
        dialog.SetItems(new[] { "Move or resize", "Remove placement" }, (_, args) =>
        {
            if (args.Which == 0) ShowWidgetGeometry(widget, expected);
            else if (args.Which == 1) _ = EditLayoutAsync(layout => LauncherLayoutEdits.RemoveWidget(layout, widget.Id), expected);
        });
        dialog.SetNegativeButton("Cancel", (_, _) => { }); ShowWidgetDialog(dialog);
    }
    private void ShowWidgetGeometry(LauncherWidgetPlacement widget, LauncherStoredLayout expected)
    {
        var layout = expected.Current; var form = new LinearLayout(this) { Orientation = Orientation.Vertical };
        NumberPicker Picker(string label, int max, int value)
        { var picker = new NumberPicker(this) { MinValue = 1, MaxValue = max, Value = value }; form.AddView(LabeledControl(label, picker)); return picker; }
        var page = Picker("Page", layout.Pages.Count, layout.Pages.ToList().FindIndex(item => item.Id == widget.PageId) + 1);
        var column = Picker("Column", layout.Columns, widget.Column + 1); var row = Picker("Row", layout.Rows, widget.Row + 1);
        var columns = Picker("Width", layout.Columns, widget.ColumnSpan); var rows = Picker("Height", layout.Rows, widget.RowSpan);
        var scroll = new ScrollView(this); scroll.AddView(form);
        var dialog = new AlertDialog.Builder(this); dialog.SetTitle("Widget position and size"); dialog.SetView(scroll);
        dialog.SetPositiveButton("Save", (_, _) => _ = EditLayoutAsync(current => LauncherLayoutEdits.MoveWidget(current, widget.Id,
            layout.Pages[page.Value - 1].Id, column.Value - 1, row.Value - 1, columns.Value, rows.Value), expected));
        dialog.SetNegativeButton("Cancel", (_, _) => { }); ShowWidgetDialog(dialog);
    }
}
