using NineToOne.Launcher;
using Android.App;
using Android.Appwidget;
using Android.Content;
using Android.Graphics;
using Android.Views;
using Android.Widget;

namespace Haven.Android;

public sealed partial class HavenLauncherActivity
{
    private LauncherPresentation CurrentPresentation => _layout?.Current.Presentation ?? new(
        ShowLabels: Preferences.GetBoolean(LabelsKey, true), ShowPackages: Preferences.GetBoolean(PackagesKey, false));
    private int MinimumTileWidth => Dp(Math.Max(64, CurrentPresentation.IconSizeDp + CurrentPresentation.HorizontalSpacingDp * 2));
    private int TileHeight => Dp(Math.Max(78, CurrentPresentation.IconSizeDp +
        (CurrentPresentation.ShowLabels ? CurrentPresentation.LabelSizeSp * 2 : 0) +
        (CurrentPresentation.ShowPackages ? 16 : 0) + CurrentPresentation.VerticalSpacingDp * 2));

    private void ShowLauncherSettings()
    {
        var expected = _layout;
        if (expected is null) return;
        var appearance = CurrentPresentation;
        var container = new LinearLayout(this) { Orientation = Orientation.Vertical };
        container.SetPadding(Dp(18), Dp(8), Dp(18), 0);
        var rows = new NumberPicker(this) { MinValue = 3, MaxValue = 8, Value = expected.Current.Rows };
        var columns = new NumberPicker(this) { MinValue = 3, MaxValue = 7, Value = expected.Current.Columns };
        var icon = new NumberPicker(this) { MinValue = 24, MaxValue = 96, Value = appearance.IconSizeDp };
        var labelSize = new NumberPicker(this) { MinValue = 10, MaxValue = 24, Value = appearance.LabelSizeSp };
        var horizontal = new NumberPicker(this) { MinValue = 0, MaxValue = 24, Value = appearance.HorizontalSpacingDp };
        var vertical = new NumberPicker(this) { MinValue = 0, MaxValue = 24, Value = appearance.VerticalSpacingDp };
        var labels = new HavenNativeCheckBox(this) { Text = "Show app labels", Checked = appearance.ShowLabels };
        var packages = new HavenNativeCheckBox(this) { Text = "Show package names", Checked = appearance.ShowPackages };
        void Preset(LauncherPresentation preset)
        {
            icon.Value = preset.IconSizeDp; labelSize.Value = preset.LabelSizeSp;
            horizontal.Value = preset.HorizontalSpacingDp; vertical.Value = preset.VerticalSpacingDp;
            labels.Checked = preset.ShowLabels; packages.Checked = preset.ShowPackages;
        }
        var presets = new LinearLayout(this) { Orientation = Orientation.Horizontal };
        foreach (var (name, value) in new[] { ("Compact", LauncherPresentation.Compact), ("Comfortable", LauncherPresentation.Comfortable), ("Large", LauncherPresentation.Large) })
        {
            var button = new Button(this) { Text = name }; button.Click += (_, _) => Preset(value); presets.AddView(button);
        }
        var presetScroll = new HorizontalScrollView(this); presetScroll.AddView(presets); container.AddView(presetScroll);
        container.AddView(LabeledControl("Home rows", rows)); container.AddView(LabeledControl("Home columns", columns));
        container.AddView(LabeledControl("Icon size", icon)); container.AddView(LabeledControl("Label size", labelSize));
        container.AddView(LabeledControl("Horizontal spacing", horizontal)); container.AddView(LabeledControl("Vertical spacing", vertical));
        container.AddView(labels); container.AddView(packages);
        var scroll = new ScrollView(this); scroll.AddView(container);
        var dialog = new AlertDialog.Builder(this); dialog.SetTitle("9to1 Launcher"); dialog.SetView(scroll);
        dialog.SetPositiveButton("Save", (_, _) =>
        {
            var selected = new LauncherPresentation(icon.Value, labelSize.Value, horizontal.Value, vertical.Value, labels.Checked, packages.Checked);
            _ = EditLayoutAsync(layout => LauncherLayoutEdits.SetPresentation(LauncherLayoutEdits.Reflow(layout, rows.Value, columns.Value), selected), expected);
        });
        dialog.SetNeutralButton("Wallpaper", (_, _) => ChooseWallpaper());
        dialog.SetNegativeButton("Widgets", (_, _) => ShowWidgetMenu()); dialog.Show();
    }

    private View LabeledControl(string label, View control)
    {
        var row = new LinearLayout(this)
        {
            Orientation = Orientation.Horizontal
        };
        row.SetGravity(GravityFlags.CenterVertical);
        var text = new TextView(this)
        {
            Text = label,
            LayoutParameters = new LinearLayout.LayoutParams(0, Dp(56), 1f),
            Gravity = GravityFlags.CenterVertical
        };
        control.ContentDescription = label;
        row.AddView(text);
        row.AddView(control);
        return row;
    }

    private void ChooseWallpaper()
    {
        var intent = new Intent(Intent.ActionSetWallpaper);
        StartActivity(Intent.CreateChooser(intent, "Choose launcher wallpaper"));
    }

    private void ShowWidgetMenu()
    {
        var dialog = new AlertDialog.Builder(this);
        dialog.SetTitle("Add widget");
        dialog.SetItems(
            new[] { "Android widget", "Haven clock widget" },
            (_, args) =>
            {
                if (args.Which == 0)
                    PickAndroidWidget();
                else
                    AddHavenWidget();
            });
        dialog.Show();
    }

    private void PickAndroidWidget()
    {
        if (_pendingWidgetId != AppWidgetManager.InvalidAppwidgetId)
        {
            Toast.MakeText(this, "Finish or cancel the current widget selection first.", ToastLength.Short)?.Show();
            return;
        }
        var widgetHost = _widgetHost;
        if (widgetHost is null)
        {
            Toast.MakeText(this, "Android widgets are unavailable right now.", ToastLength.Long)?.Show();
            return;
        }

        try
        {
            _pendingWidgetId = widgetHost.AllocateAppWidgetId();
            var intent = new Intent(AppWidgetManager.ActionAppwidgetPick);
            intent.PutExtra(AppWidgetManager.ExtraAppwidgetId, _pendingWidgetId);
            StartActivityForResult(intent, PickWidgetRequest);
        }
        catch (Exception exception)
        {
            var failedWidgetId = _pendingWidgetId;
            _pendingWidgetId = AppWidgetManager.InvalidAppwidgetId;
            DeleteWidgetId(failedWidgetId);
            global::Android.Util.Log.Warn(
                "HavenLauncher",
                "Could not open the Android widget picker: " + exception.Message);
            Toast.MakeText(this, "Could not open the Android widget picker.", ToastLength.Long)?.Show();
        }
    }

    protected override void OnActivityResult(int requestCode, Result resultCode, Intent? data)
    {
        base.OnActivityResult(requestCode, resultCode, data);
        if (requestCode is ExportLayoutRequest or ImportLayoutRequest)
        {
            _ = CompleteLayoutDocumentAsync(requestCode, resultCode, data?.Data);
            return;
        }

        if (requestCode is not PickWidgetRequest and not ConfigureWidgetRequest)
            return;

        var widgetId = data?.GetIntExtra(
            AppWidgetManager.ExtraAppwidgetId,
            _pendingWidgetId) ?? _pendingWidgetId;

        // A platform result may only complete the binding this activity allocated.
        // Never release a binding ID supplied by an unrelated or stale result.
        if (_pendingWidgetId == AppWidgetManager.InvalidAppwidgetId) return;
        if (widgetId != _pendingWidgetId)
        {
            CompletePendingWidget(_pendingWidgetId, keep: false);
            Toast.MakeText(this, "Widget selection changed. Please choose the widget again.", ToastLength.Long)?.Show();
            return;
        }

        if (requestCode == PickWidgetRequest)
        {
            if (resultCode != Result.Ok)
            {
                CompletePendingWidget(widgetId, keep: false);
                return;
            }

            try
            {
                var info = _widgetManager?.GetAppWidgetInfo(widgetId)
                    ?? throw new InvalidOperationException("The selected widget provider is unavailable.");
                if (info.Configure is not null)
                {
                    _pendingWidgetId = widgetId;
                    var configure = new Intent(AppWidgetManager.ActionAppwidgetConfigure);
                    configure.SetComponent(info.Configure);
                    configure.PutExtra(AppWidgetManager.ExtraAppwidgetId, widgetId);
                    StartActivityForResult(configure, ConfigureWidgetRequest);
                    return;
                }

                CompletePendingWidget(widgetId, keep: true);
            }
            catch (Exception exception)
            {
                FailPendingWidget(widgetId, "Could not configure the selected Android widget.", exception);
            }
            return;
        }

        CompletePendingWidget(widgetId, keep: resultCode == Result.Ok);
    }

    private void CompletePendingWidget(int widgetId, bool keep)
    {
        _pendingWidgetId = AppWidgetManager.InvalidAppwidgetId;
        if (keep)
        {
            SaveWidgetId(widgetId);
            RenderWidgets();
            return;
        }

        DeleteWidgetId(widgetId);
    }

    private void FailPendingWidget(int widgetId, string message, Exception exception)
    {
        _pendingWidgetId = AppWidgetManager.InvalidAppwidgetId;
        DeleteWidgetId(widgetId);
        global::Android.Util.Log.Warn(
            "HavenLauncher",
            message + " " + exception.Message);
        Toast.MakeText(this, message, ToastLength.Long)?.Show();
    }

    private void AddHavenWidget()
    {
        Preferences.Edit()?.PutBoolean(HavenWidgetKey, true)?.Apply();
        RenderWidgets();
    }

    private void RenderBaseWidgets()
    {
        var widgetStrip = _widgetStrip;
        if (widgetStrip is null)
            return;

        widgetStrip.RemoveAllViews();

        if (Preferences.GetBoolean(HavenWidgetKey, false))
        {
            var clock = new TextClock(this)
            {
                Format12Hour = "EEE, MMM d  •  h:mm a",
                Format24Hour = "EEE, MMM d  •  HH:mm",
                TextSize = 18,
                Gravity = GravityFlags.Center,
                LayoutParameters = new LinearLayout.LayoutParams(Dp(260), Dp(70))
                {
                    RightMargin = Dp(8)
                }
            };
            clock.SetTextColor(Color.White);
            clock.Background = MagicalBackground(Dp(20));
            clock.LongClick += (_, args) =>
            {
                Preferences.Edit()?.PutBoolean(HavenWidgetKey, false)?.Apply();
                RenderWidgets();
                if (args is not null)
                    args.Handled = true;
            };
            widgetStrip.AddView(clock);
        }

        var widgetHost = _widgetHost;
        var widgetManager = _widgetManager;
        if (widgetHost is null || widgetManager is null)
            return;

        foreach (var widgetId in ReadWidgetIds().ToArray())
        {
            try
            {
                var info = widgetManager.GetAppWidgetInfo(widgetId);
                if (info is null)
                {
                    widgetStrip.AddView(BuildUnavailableWidget(widgetId));
                    continue;
                }

                var hostView = widgetHost.CreateView(this, widgetId, info);
                if (hostView is null)
                {
                    widgetStrip.AddView(BuildUnavailableWidget(widgetId));
                    continue;
                }
                hostView.SetAppWidget(widgetId, info);
                hostView.LayoutParameters = new LinearLayout.LayoutParams(Dp(300), Dp(160))
                {
                    RightMargin = Dp(8)
                };
                hostView.LongClick += (_, args) =>
                {
                    var dialog = new AlertDialog.Builder(this);
                    dialog.SetMessage("Remove this widget?");
                    dialog.SetPositiveButton("Remove", (_, _) =>
                    {
                        DeleteWidgetId(widgetId);
                        RenderWidgets();
                    });
                    dialog.SetNegativeButton("Cancel", (_, _) => { });
                    dialog.Show();
                    if (args is not null)
                        args.Handled = true;
                };
                widgetStrip.AddView(hostView);
            }
            catch (Exception exception)
            {
                global::Android.Util.Log.Warn(
                    "HavenLauncher",
                    $"Widget {widgetId} could not be hosted: {exception.Message}");
                widgetStrip.AddView(BuildUnavailableWidget(widgetId));
            }
        }
    }

    private View BuildUnavailableWidget(int widgetId)
    {
        // Provider packages and work profiles may return later. Rendering must not
        // delete the retained platform binding or its provider configuration.
        var button = new Button(this)
        {
            Text = "Widget unavailable · Retry or remove",
            ContentDescription = "Unavailable Android widget. Retry loading or remove the saved widget.",
            LayoutParameters = new LinearLayout.LayoutParams(Dp(300), Dp(160)) { RightMargin = Dp(8) }
        };
        button.Click += (_, _) =>
        {
            var dialog = new AlertDialog.Builder(this);
            dialog.SetTitle("Widget unavailable");
            dialog.SetMessage("Its saved configuration has been kept. Retry when its application or profile is available, or remove this widget.");
            dialog.SetPositiveButton("Retry", (_, _) => RenderWidgets());
            dialog.SetNeutralButton("Remove", (_, _) =>
            {
                var confirm = new AlertDialog.Builder(this);
                confirm.SetMessage("Remove this widget and its saved configuration?");
                confirm.SetPositiveButton("Remove", (_, _) => { DeleteWidgetId(widgetId); RenderWidgets(); });
                confirm.SetNegativeButton("Cancel", (_, _) => { });
                confirm.Show();
            });
            dialog.SetNegativeButton("Cancel", (_, _) => { });
            dialog.Show();
        };
        return button;
    }

    private HashSet<int> ReadWidgetIds()
        => (Preferences!.GetString(WidgetIdsKey, string.Empty) ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(value => int.TryParse(value, out var id)
                ? id
                : AppWidgetManager.InvalidAppwidgetId)
            .Where(id => id != AppWidgetManager.InvalidAppwidgetId)
            .ToHashSet();

    private void SaveWidgetId(int widgetId)
    {
        var ids = ReadWidgetIds();
        ids.Add(widgetId);
        Preferences.Edit()?
            .PutString(WidgetIdsKey, string.Join(',', ids.OrderBy(id => id)))?
            .Apply();
    }

    private void DeleteWidgetId(int widgetId)
    {
        if (widgetId == AppWidgetManager.InvalidAppwidgetId)
            return;

        var ids = ReadWidgetIds();
        ids.Remove(widgetId);
        Preferences.Edit()?
            .PutString(WidgetIdsKey, string.Join(',', ids.OrderBy(id => id)))?
            .Apply();

        try
        {
            _widgetHost?.DeleteAppWidgetId(widgetId);
        }
        catch
        {
        }
    }

    private void OpenHavenDashboard()
    {
        var intent = new Intent(this, typeof(MainActivity));
        intent.AddFlags(ActivityFlags.NewTask | ActivityFlags.ClearTop);
        intent.PutExtra("haven_surface", "dashboard");
        StartActivity(intent);
    }

    private void OpenHavenChat(string prompt)
    {
        var intent = new Intent(this, typeof(MainActivity));
        intent.AddFlags(ActivityFlags.NewTask | ActivityFlags.ClearTop);
        intent.PutExtra("haven_prompt", prompt);
        StartActivity(intent);
    }

    private void ApplyWallpaper()
    {
        if (_root is null)
            return;

        try
        {
            var drawable = WallpaperManager.GetInstance(this)?.Drawable;
            _root.Background = drawable ?? RoundedBackground(Color.Rgb(31, 24, 45), 0);
        }
        catch
        {
            _root.Background = HavenNativeSurface.Page();
        }
    }
}
