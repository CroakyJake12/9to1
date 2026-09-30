using Android.App;
using Android.Appwidget;
using Android.Content;
using Android.Content.PM;
using Android.Graphics;
using Android.OS;
using Android.Views;
using Android.Widget;
using HavenOS.Home.Core;

namespace Haven.Android;

[Activity(
    Label = "9to1 Launcher",
    Theme = "@style/Theme.AppCompat.Light.NoActionBar",
    Icon = "@drawable/haven_icon",
    Exported = true,
    LaunchMode = LaunchMode.SingleTask,
    TaskAffinity = "com.cakemods.haven.launcher",
    ExcludeFromRecents = true,
    ConfigurationChanges =
        ConfigChanges.Orientation
        | ConfigChanges.ScreenSize
        | ConfigChanges.SmallestScreenSize
        | ConfigChanges.ScreenLayout
        | ConfigChanges.UiMode
        | ConfigChanges.Density)]
[IntentFilter(
    new[] { Intent.ActionMain },
    Categories = new[] { Intent.CategoryHome, Intent.CategoryDefault })]
public sealed partial class HavenLauncherActivity : Activity
{
    private const string PreferenceName = "haven_launcher";
    private const string OrderKey = "app_order";
    private const string RowsKey = "rows";
    private const string ColumnsKey = "columns";
    private const string LabelsKey = "labels";
    private const string PackagesKey = "packages";
    private const string HavenWidgetKey = "haven_widget";
    private const string WidgetIdsKey = "widget_ids";
    private const int WidgetHostId = 0x48415645;
    private const int PickWidgetRequest = 8101;
    private const int ConfigureWidgetRequest = 8102;

    private readonly List<LauncherApp> _apps = [];
    private LinearLayout? _root;
    private LinearLayout? _widgetStrip;
    private GridLayout? _grid;
    private TextView? _pageIndicator;
    private TextView? _launcherStatus;
    private AppWidgetHost? _widgetHost;
    private AppWidgetManager? _widgetManager;
    private int _page;
    private int _pendingWidgetId = AppWidgetManager.InvalidAppwidgetId;
    private readonly CancellationTokenSource _launcherLifetime = new();
    private bool _homeReady;
    private bool _activityStarted;

    private ISharedPreferences Preferences
        => GetSharedPreferences(PreferenceName, FileCreationMode.Private)!;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        _pendingWidgetId = savedInstanceState?.GetInt("pending_android_widget_id", AppWidgetManager.InvalidAppwidgetId)
            ?? AppWidgetManager.InvalidAppwidgetId;
        if (!OperatingSystem.IsAndroidVersionAtLeast(35))
        {
            Window?.SetStatusBarColor(Color.Transparent);
            Window?.SetNavigationBarColor(Color.Rgb(24, 18, 38));
        }

        _widgetHost = new AppWidgetHost(this, WidgetHostId);
        _widgetManager = AppWidgetManager.GetInstance(this);

        ShowHomeBootstrap("Preparing 9-1 Home…");
        _ = InitializeHomeAsync();
    }

    protected override void OnSaveInstanceState(Bundle outState)
    {
        outState.PutInt("pending_android_widget_id", _pendingWidgetId);
        base.OnSaveInstanceState(outState);
    }

    protected override void OnStart()
    {
        base.OnStart();
        _activityStarted = true;
        StartWidgetListening();
    }

    private void StartWidgetListening()
    {
        if (!_homeReady || !_activityStarted) return;
        try
        {
            _widgetHost?.StartListening();
        }
        catch
        {
            // Keep the launcher usable when a widget provider rejects hosting.
        }
    }

    protected override void OnStop()
    {
        _activityStarted = false;
        try
        {
            _widgetHost?.StopListening();
        }
        finally
        {
            base.OnStop();
        }
    }

    protected override void OnResume()
    {
        base.OnResume();
        if (!_homeReady) return;
        ApplyWallpaper();
        RenderWidgets();
        LoadAppsAsync(showLoading: _apps.Count == 0);
    }

    public override void OnConfigurationChanged(global::Android.Content.Res.Configuration newConfig)
    {
        base.OnConfigurationChanged(newConfig);
        if (!_homeReady) return;

        // This activity handles orientation/screen/density changes itself, so rebuild the
        // native surface to recalculate all dp-derived dimensions against current metrics.
        BuildSurface();
        _grid?.Post(RenderPage);
    }

    protected override void OnDestroy()
    {
        _folderDialog?.Dismiss();
        _launcherLifetime.Cancel();
        _launcherLifetime.Dispose();
        Interlocked.Increment(ref _appLoadGeneration);
        base.OnDestroy();
    }

    private async Task InitializeHomeAsync()
    {
        try
        {
            var home = await AndroidHomeServiceHost.EnsureAsync(installedApplications: true, _launcherLifetime.Token);
            if (_launcherLifetime.IsCancellationRequested) return;
            if (home.State != HomeNativeHostState.Ready)
            {
                ShowHomeBootstrap(home.Message);
                return;
            }
            _homeReady = true;
            StartWidgetListening();
            BuildSurface();
            ApplyWallpaper();
            RenderWidgets();
            LoadAppsAsync(showLoading: true);
        }
        catch (System.OperationCanceledException) when (_launcherLifetime.IsCancellationRequested) { }
        catch
        {
            if (!_launcherLifetime.IsCancellationRequested)
                ShowHomeBootstrap("9-1 Home needs repair before Launcher can open your applications. Existing state was preserved.");
        }
    }

    private void ShowHomeBootstrap(string message)
    {
        var panel = new LinearLayout(this) { Orientation = Orientation.Vertical };
        panel.SetPadding(Dp(24), Dp(40), Dp(24), Dp(24));
        var title = new TextView(this) { Text = "9-1 Home", TextSize = 24 };
        var detail = new TextView(this) { Text = message, TextSize = 16 };
        var repair = new Button(this) { Text = "Open Home" };
        repair.Click += (_, _) => StartActivity(new Intent(this, typeof(AndroidBootstrapActivity)));
        panel.AddView(title);
        panel.AddView(detail);
        panel.AddView(repair);
        SetContentView(panel);
        AndroidTypography.ApplyTree(panel);
    }

    public override void OnBackPressed()
    {
        if (_page != 0)
        {
            ChangePage(-_page);
            return;
        }

        // The launcher owns the root back destination.
        return;
    }

    private void BuildSurface()
    {
        _root = new LinearLayout(this)
        {
            Orientation = Orientation.Vertical,
            LayoutParameters = new ViewGroup.LayoutParams(
                ViewGroup.LayoutParams.MatchParent,
                ViewGroup.LayoutParams.MatchParent)
        };
        _root.SetPadding(Dp(12), Dp(10), Dp(12), Dp(10));
        _root.SetOnTouchListener(new SwipeTouchListener(
            swipeThresholdPixels: Dp(80),
            onSwipeUp: ShowAppDrawer,
            onSwipeLeft: () => ChangePage(1),
            onSwipeRight: () => ChangePage(-1)));

        _widgetStrip = new LinearLayout(this)
        {
            Orientation = Orientation.Horizontal,
            LayoutParameters = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MatchParent,
                ViewGroup.LayoutParams.WrapContent)
        };
        var widgetScroll = new HorizontalScrollView(this)
        {
            HorizontalScrollBarEnabled = false,
            LayoutParameters = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MatchParent,
                ViewGroup.LayoutParams.WrapContent)
        };
        widgetScroll.AddView(_widgetStrip);
        _root.AddView(widgetScroll);

        _pageIndicator = new Button(this)
        {
            Gravity = GravityFlags.Center,
            TextSize = 12,
            LayoutParameters = new LinearLayout.LayoutParams(0, Dp(48), 1f)
        };
        _pageIndicator.SetTextColor(Color.White);
        _pageIndicator.Click += (_, _) => ShowPagesMenu();
        _pageIndicator.ContentDescription = "Manage launcher pages";
        var pageNavigation = new LinearLayout(this) { Orientation = Orientation.Horizontal };
        var previousPage = new Button(this) { Text = "‹", ContentDescription = "Previous launcher page", LayoutParameters = new LinearLayout.LayoutParams(Dp(48), Dp(48)) };
        var nextPage = new Button(this) { Text = "›", ContentDescription = "Next launcher page", LayoutParameters = new LinearLayout.LayoutParams(Dp(48), Dp(48)) };
        previousPage.Click += (_, _) => ChangePage(-1); nextPage.Click += (_, _) => ChangePage(1);
        pageNavigation.AddView(previousPage); pageNavigation.AddView(_pageIndicator); pageNavigation.AddView(nextPage);
        _root.AddView(pageNavigation);
        _launcherStatus = new TextView(this)
        {
            Text = "Loading apps…",
            TextSize = 12,
            Gravity = GravityFlags.Center,
            LayoutParameters = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MatchParent,
                ViewGroup.LayoutParams.WrapContent)
        };
        _launcherStatus.SetTextColor(Color.Argb(220, 235, 225, 255));
        _launcherStatus.SetPadding(0, 0, 0, Dp(4));
        _root.AddView(_launcherStatus);

        _grid = new GridLayout(this)
        {
            UseDefaultMargins = false,
            AlignmentMode = GridAlign.Bounds,
            LayoutParameters = new ViewGroup.LayoutParams(ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.WrapContent)
        };
        // Keep every configured cell reachable when large grids or accessibility scaling exceed the viewport.
        var gridVertical = new ScrollView(this) { FillViewport = true };
        gridVertical.AddView(_grid);
        var gridHorizontal = new HorizontalScrollView(this)
        {
            FillViewport = true,
            LayoutParameters = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, 0, 1f)
        };
        gridHorizontal.AddView(gridVertical);
        _root.AddView(gridHorizontal);

        _dockHost = new LinearLayout(this) { Orientation = Orientation.Vertical };
        _root.AddView(_dockHost);
        _root.AddView(BuildBottomBar());
        SetContentView(_root);
        AndroidTypography.ApplyTree(_root);
        ApplyWallpaper();
        RenderWidgets();
    }

    private View BuildBottomBar()
    {
        var row = new LinearLayout(this)
        {
            Orientation = Orientation.Horizontal,
            LayoutParameters = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MatchParent,
                Dp(66))
        };
        row.SetGravity(GravityFlags.CenterVertical);
        row.SetPadding(Dp(7), Dp(7), Dp(7), Dp(7));
        row.Background = MagicalBackground(Dp(28));

        row.AddView(IconButton(
            Resource.Drawable.ic_apps,
            "All apps",
            ShowAppDrawer));
        row.AddView(IconButton(
            Resource.Drawable.ic_haven_go,
            "Open Haven Go",
            OpenHavenDashboard));

        var assistantButton = IconButton(
            Resource.Drawable.ic_haven_go,
            "Open Haven Assistant. Long press to pin or unpin.",
            OpenHavenAssistant);
        assistantButton.LongClick += (_, args) =>
        {
            ToggleHavenAssistantWidgetPin();
            if (args is not null)
                args.Handled = true;
        };
        row.AddView(assistantButton);

        row.AddView(IconButton(
            SystemDrawable("ic_menu_preferences"),
            "Launcher settings",
            ShowLauncherSettings));

        var go = new EditText(this)
        {
            Hint = "Go — ask Haven",
            TextSize = 15,
            LayoutParameters = new LinearLayout.LayoutParams(0, Dp(50), 1f)
            {
                LeftMargin = Dp(6),
                RightMargin = Dp(6)
            }
        };
        go.SetSingleLine(true);
        go.SetTextColor(Color.White);
        go.SetHintTextColor(Color.Argb(190, 255, 255, 255));
        go.SetPadding(Dp(16), 0, Dp(12), 0);
        go.Background = RoundedBackground(Color.Argb(90, 255, 255, 255), Dp(24));
        go.EditorAction += (_, args) =>
        {
            if (!string.IsNullOrWhiteSpace(go.Text))
            {
                OpenHavenChat(go.Text!);
                go.Text = string.Empty;
                args.Handled = true;
            }
        };
        row.AddView(go);

        row.AddView(IconButton(
            SystemDrawable("ic_menu_send"),
            "Send to Haven",
            () =>
            {
                if (!string.IsNullOrWhiteSpace(go.Text))
                {
                    OpenHavenChat(go.Text!);
                    go.Text = string.Empty;
                }
            }));

        return row;
    }

    private int SystemDrawable(string name)
        => Resources?.GetIdentifier(name, "drawable", "android") ?? 0;

    private ImageButton IconButton(int resource, string description, Action action)
    {
        var button = new ImageButton(this)
        {
            ContentDescription = description,
            LayoutParameters = new LinearLayout.LayoutParams(Dp(48), Dp(48))
            {
                LeftMargin = Dp(2),
                RightMargin = Dp(2)
            }
        };
        button.SetImageResource(resource);
        button.SetColorFilter(Color.White);
        button.SetBackgroundColor(Color.Transparent);
        button.Click += (_, _) => action();
        return button;
    }
}
