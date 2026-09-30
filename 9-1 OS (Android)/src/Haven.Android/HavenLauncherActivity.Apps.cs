using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Graphics;
using Android.Views;
using Android.Widget;
using Haven.Application;
using Haven.Application.Go;
using Haven.Desktop;
using Microsoft.Extensions.DependencyInjection;

namespace Haven.Android;

public sealed partial class HavenLauncherActivity
{
    private int _appLoadGeneration;

    private async void LoadAppsAsync(bool showLoading = false)
    {
        var generation = Interlocked.Increment(ref _appLoadGeneration);
        if (showLoading && _launcherStatus is not null)
            _launcherStatus.Text = "Loading apps…";
        try
        {
            var apps = await QueryAppsAsync();
            if (generation != Volatile.Read(ref _appLoadGeneration))
                return;

            _apps.Clear();
            _apps.AddRange(ApplySavedOrder(apps));
            var savedOrder = (Preferences.GetString(OrderKey, string.Empty) ?? string.Empty)
                .Split('|', StringSplitOptions.RemoveEmptyEntries);
            if (savedOrder.Any(key => !Guid.TryParse(key, out _)))
                SaveOrder(); // One-time migration of personal-profile placement to canonical IDs.
            _page = Math.Clamp(_page, 0, Math.Max(0, PageCount - 1));
            if (_launcherStatus is not null)
                _launcherStatus.Text = _apps.Count == 0
                    ? "No launchable apps were returned by Android. Open launcher settings or retry."
                    : $"{_apps.Count} apps";
            if (_grid is not null)
                _grid.Post(RenderPage);
            else
                RenderPage();
        }
        catch (Exception ex)
        {
            if (generation != Volatile.Read(ref _appLoadGeneration))
                return;

            if (_launcherStatus is not null)
                _launcherStatus.Text = "Could not load apps: " + ex.Message;
            Toast.MakeText(this, "Could not load apps", ToastLength.Long)?.Show();
        }
    }
    private async Task<IReadOnlyList<LauncherApp>> QueryAppsAsync(CancellationToken cancellationToken = default)
    {
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(_launcherLifetime.Token, cancellationToken);
        var services = App.Services ?? throw new InvalidOperationException("9-1 Home is unavailable.");
        var registry = services.GetRequiredService<IInstalledApplicationRegistry>();
        var catalog = services.GetRequiredService<AndroidLauncherPlatformCatalog>();
        var references = await registry.RefreshAsync(lifetime.Token);
        var profiles = await Task.Run(() => catalog.Observe(loadIcons: true), lifetime.Token);
        var apps = new List<LauncherApp>();
        foreach (var reference in references.Where(item => item.ProviderId == AndroidLauncherPlatformCatalog.ProviderId))
        {
            var component = ComponentName.UnflattenFromString(reference.Entrypoint);
            if (component?.PackageName is null || component.ClassName is null) continue;
            var profile = profiles.SingleOrDefault(item => item.PlatformUserSerial == reference.PlatformProfileId);
            var platformActivity = profile?.Activities.SingleOrDefault(item => item.Entrypoint == reference.Entrypoint);
            apps.Add(new(reference.Label, component.PackageName, component.ClassName,
                platformActivity?.BadgedIcon, reference.ApplicationId, reference.Revision,
                reference.PlatformProfileId, profile?.Label ?? $"Android profile {reference.PlatformProfileId}",
                profile?.IsCurrentProfile == true, reference.Enabled && reference.ProfileAccessible));
        }
        return apps;
    }

    private IReadOnlyList<LauncherApp> ApplySavedOrder(IReadOnlyList<LauncherApp> apps)
    {
        var order = (Preferences.GetString(OrderKey, string.Empty) ?? string.Empty)
            .Split('|', StringSplitOptions.RemoveEmptyEntries)
            .Select((key, index) => (key, index))
            .GroupBy(item => item.key, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First().index, StringComparer.Ordinal);

        return apps
            .OrderBy(app => order.TryGetValue(app.Key, out var index) ? index
                : app.IsCurrentProfile && order.TryGetValue(app.LegacyPersonalKey, out var legacyIndex) ? legacyIndex : int.MaxValue)
            .ThenBy(app => app.Label, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    private void RenderPage()
    {
        if (_grid is null || _pageIndicator is null)
            return;

        var (rows, columns) = ResolveGridShape(
            Math.Clamp(Preferences.GetInt(RowsKey, 5), 3, 8),
            Math.Clamp(Preferences.GetInt(ColumnsKey, 4), 3, 7));
        var perPage = rows * columns;
        var pageCount = Math.Max(1, (int)Math.Ceiling(_apps.Count / (double)perPage));
        _page = Math.Clamp(_page, 0, pageCount - 1);
        var visible = _apps.Skip(_page * perPage).Take(perPage).ToArray();

        _grid.RemoveAllViews();
        if (_apps.Count == 0)
        {
            _pageIndicator.Text = "Tap All apps to retry";
            return;
        }

        _grid.RowCount = rows;
        _grid.ColumnCount = columns;

        var metrics = Resources?.DisplayMetrics;
        var gridWidth = _grid.Width > 0
            ? _grid.Width
            : Math.Max(Dp(64), (metrics?.WidthPixels ?? Dp(360)) - Dp(24));
        var gridHeight = _grid.Height > 0
            ? _grid.Height
            : Math.Max(Dp(78), (metrics?.HeightPixels ?? Dp(640)) - Dp(180));
        var cellWidth = Math.Max(Dp(64), gridWidth / columns);
        var cellHeight = Math.Max(Dp(78), gridHeight / rows);

        foreach (var app in visible)
            _grid.AddView(BuildAppTile(app, cellWidth, cellHeight));

        _pageIndicator.Text = pageCount <= 1
            ? "Swipe up for apps"
            : $"{_page + 1} / {pageCount}  \u2022  Swipe up for apps";
        AndroidTypography.ApplyTree(_grid);
    }

    private View BuildAppTile(LauncherApp app, int width, int height)
    {
        var tile = new LinearLayout(this)
        {
            Orientation = Orientation.Vertical,
            ContentDescription = $"{app.Label}, {app.ProfileLabel}" + (app.Available ? string.Empty : ", unavailable"),
            LayoutParameters = new ViewGroup.LayoutParams(width, height)
        };
        tile.SetGravity(GravityFlags.Center);
        tile.SetPadding(Dp(4), Dp(4), Dp(4), Dp(4));
        if (!app.Available) tile.Alpha = 0.5f;

        var icon = new ImageView(this)
        {
            LayoutParameters = new LinearLayout.LayoutParams(Dp(52), Dp(52))
        };
        icon.SetImageDrawable(app.Icon);
        tile.AddView(icon);

        if (Preferences.GetBoolean(LabelsKey, true))
        {
            var label = new TextView(this)
            {
                Text = app.Label,
                Gravity = GravityFlags.Center,
                TextSize = 12
            };
            label.SetMaxLines(1);
            label.Ellipsize = global::Android.Text.TextUtils.TruncateAt.End;
            label.SetTextColor(Color.White);
            tile.AddView(label);
        }

        if (Preferences.GetBoolean(PackagesKey, false))
        {
            var package = new TextView(this)
            {
                Text = app.PackageName,
                Gravity = GravityFlags.Center,
                TextSize = 8
            };
            package.SetMaxLines(1);
            package.Ellipsize = global::Android.Text.TextUtils.TruncateAt.Middle;
            package.SetTextColor(Color.Argb(210, 230, 220, 255));
            tile.AddView(package);
        }
        if (!app.IsCurrentProfile)
        {
            var profile = new TextView(this) { Text = app.ProfileLabel, TextSize = 9, Gravity = GravityFlags.Center };
            profile.SetTextColor(Color.Argb(220, 235, 225, 255));
            profile.SetMaxLines(1);
            tile.AddView(profile);
        }

        tile.LongClick += (_, args) =>
        {
            _movingKey = app.Key;
            tile.Background = RoundedBackground(Color.Argb(120, 176, 116, 255), Dp(18));
            Toast.MakeText(this, "Tap another app to move it here", ToastLength.Short)?.Show();
            args.Handled = true;
        };
        tile.Click += (_, _) =>
        {
            if (_movingKey is not null)
            {
                MoveApp(_movingKey, app.Key);
                _movingKey = null;
                return;
            }

            LaunchApp(app);
        };
        return tile;
    }

    private void MoveApp(string sourceKey, string destinationKey)
    {
        var source = _apps.FindIndex(app => app.Key == sourceKey);
        var destination = _apps.FindIndex(app => app.Key == destinationKey);
        if (source < 0 || destination < 0 || source == destination)
            return;

        var moving = _apps[source];
        _apps.RemoveAt(source);
        if (source < destination)
            destination--;
        _apps.Insert(destination, moving);
        SaveOrder();
        RenderPage();
    }

    private void SaveOrder()
    {
        Preferences.Edit()?
            .PutString(OrderKey, string.Join('|', _apps.Select(app => app.Key)))?
            .Apply();
    }

    private int PageCount
    {
        get
        {
            var rows = Math.Clamp(Preferences.GetInt(RowsKey, 5), 3, 8);
            var columns = Math.Clamp(Preferences.GetInt(ColumnsKey, 4), 3, 7);
            var (effectiveRows, effectiveColumns) = ResolveGridShape(rows, columns);
            return Math.Max(
                1,
                (int)Math.Ceiling(_apps.Count / (double)(effectiveRows * effectiveColumns)));
        }
    }

    private (int Rows, int Columns) ResolveGridShape(int requestedRows, int requestedColumns)
    {
        var metrics = Resources?.DisplayMetrics;
        var availableWidth = _grid?.Width > 0
            ? _grid.Width
            : Math.Max(Dp(64), (metrics?.WidthPixels ?? Dp(360)) - Dp(24));
        var availableHeight = _grid?.Height > 0
            ? _grid.Height
            : Math.Max(Dp(78), (metrics?.HeightPixels ?? Dp(640)) - Dp(180));

        var maxColumns = Math.Max(1, availableWidth / Dp(64));
        var maxRows = Math.Max(1, availableHeight / Dp(78));
        return (
            Math.Max(1, Math.Min(requestedRows, maxRows)),
            Math.Max(1, Math.Min(requestedColumns, maxColumns)));
    }

    private int ResolveColumnCount(int requestedColumns, int availableWidth)
    {
        var maxColumns = Math.Max(1, availableWidth / Dp(64));
        return Math.Max(1, Math.Min(requestedColumns, maxColumns));
    }

    private void ChangePage(int delta)
    {
        var next = Math.Clamp(_page + delta, 0, Math.Max(0, PageCount - 1));
        if (next == _page)
            return;
        _page = next;
        RenderPage();
    }

    private void ShowAppDrawer()
    {
        var dialog = new Dialog(this);
        var shell = new LinearLayout(this)
        {
            Orientation = Orientation.Vertical,
            LayoutParameters = new ViewGroup.LayoutParams(
                ViewGroup.LayoutParams.MatchParent,
                ViewGroup.LayoutParams.MatchParent)
        };
        shell.SetPadding(Dp(12), Dp(16), Dp(12), Dp(12));
        shell.Background = HavenNativeSurface.Page();

        var header = new LinearLayout(this)
        {
            Orientation = Orientation.Horizontal
        };
        header.SetGravity(GravityFlags.CenterVertical);
        var title = new TextView(this)
        {
            Text = "All apps",
            TextSize = 22,
            Typeface = Typeface.DefaultBold,
            LayoutParameters = new LinearLayout.LayoutParams(0, Dp(52), 1f),
            Gravity = GravityFlags.CenterVertical
        };
        title.SetTextColor(Color.White);
        header.AddView(title);
        header.AddView(IconButton(
            SystemDrawable("ic_menu_preferences"),
            "Launcher settings",
            () =>
            {
                dialog.Dismiss();
                ShowLauncherSettings();
            }));
        header.AddView(IconButton(
            SystemDrawable("ic_menu_close_clear_cancel"),
            "Close app drawer",
            dialog.Dismiss));
        shell.AddView(header);

        var search = new EditText(this)
        {
            Hint = "Search installed apps",
            TextSize = 15,
            LayoutParameters = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MatchParent,
                Dp(48))
        };
        search.SetSingleLine(true);
        search.SetTextColor(Color.White);
        search.SetHintTextColor(Color.Argb(180, 235, 225, 255));
        search.Background = RoundedBackground(Color.Argb(70, 255, 255, 255), Dp(18));
        search.SetPadding(Dp(16), 0, Dp(16), 0);
        shell.AddView(search);

        var scroll = new ScrollView(this)
        {
            LayoutParameters = new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MatchParent,
                0,
                1f)
        };
        var drawerWidth = Math.Max(
            Dp(64),
            (Resources?.DisplayMetrics?.WidthPixels ?? Dp(360)) - Dp(24));
        var grid = new GridLayout(this)
        {
            ColumnCount = ResolveColumnCount(
                Math.Clamp(Preferences.GetInt(ColumnsKey, 4), 3, 7),
                drawerWidth)
        };
        var width = Math.Max(Dp(64), drawerWidth / grid.ColumnCount);
        var engine = (App.Services ?? throw new InvalidOperationException("9-1 Home is unavailable.")).GetRequiredService<GoService>();
        var dialogLifetime = CancellationTokenSource.CreateLinkedTokenSource(_launcherLifetime.Token);
        var dialogToken = dialogLifetime.Token;
        CancellationTokenSource? currentSearch = null;
        var searchGeneration = 0;
        dialog.DismissEvent += (_, _) => { dialogLifetime.Cancel(); currentSearch?.Cancel(); dialogLifetime.Dispose(); };
        async Task RenderMatchesAsync(string? query)
        {
            currentSearch?.Cancel();
            using var request = CancellationTokenSource.CreateLinkedTokenSource(dialogToken);
            currentSearch = request; var generation = Interlocked.Increment(ref searchGeneration);
            try
            {
                await Task.Delay(120, request.Token);
                var presentation = (await QueryAppsAsync(request.Token)).ToDictionary(app => app.ApplicationId);
                var scope = new GoScope(new HashSet<string>(StringComparer.Ordinal) { AndroidInstalledApplicationsGoProvider.Id },
                    new HashSet<string>(StringComparer.Ordinal) { "Home" }, new HashSet<string>(StringComparer.Ordinal) { "os.installed-application" },
                    new HashSet<string>(StringComparer.Ordinal) { "Open" });
                RunOnUiThread(() => { if (!request.IsCancellationRequested && generation == searchGeneration) grid.RemoveAllViews(); });
                var count = 0; var failed = false;
                await foreach (var update in engine.QueryAsync(new(query?.Trim() ?? "", "Apps", 1000, scope), request.Token))
                {
                    if (update.Failure is not null) failed = true;
                    if (update.Result is not { } result || !Guid.TryParse(result.Reference.Id, out var id) ||
                        !long.TryParse(result.Reference.Revision, out var revision) || !presentation.TryGetValue(id, out var app)) continue;
                    var current = app with { Label = result.Label, RegistryRevision = revision, Available = true };
                    count++;
                    RunOnUiThread(() => { if (!request.IsCancellationRequested && generation == searchGeneration) grid.AddView(BuildAppTile(current, width, Dp(96))); });
                }
                if (count == 0)
                    RunOnUiThread(() =>
                    {
                        if (request.IsCancellationRequested || generation != searchGeneration) return;
                        var empty = new TextView(this) { Text = failed ? "Applications are temporarily unavailable. Retry or repair Home." : "No installed apps match this search.",
                            Gravity = GravityFlags.Center, LayoutParameters = new ViewGroup.LayoutParams(ViewGroup.LayoutParams.MatchParent, Dp(72)) };
                        empty.SetTextColor(Color.Argb(220, 235, 225, 255)); grid.AddView(empty);
                    });
            }
            catch (OperationCanceledException) when (request.IsCancellationRequested) { }
            catch (Exception)
            {
                RunOnUiThread(() =>
                {
                    if (dialogToken.IsCancellationRequested || generation != searchGeneration) return;
                    Toast.MakeText(this, "Applications could not be queried. Retry or repair Home.", ToastLength.Long)?.Show();
                });
            }
            finally { if (ReferenceEquals(currentSearch, request)) currentSearch = null; }
        }
        search.TextChanged += (_, args) => _ = RenderMatchesAsync(args.Text?.ToString());
        _ = RenderMatchesAsync(string.Empty);
        scroll.AddView(grid);
        shell.AddView(scroll);

        dialog.SetContentView(shell);
        AndroidTypography.ApplyTree(shell);
        dialog.Show();
        dialog.Window?.SetLayout(
            ViewGroup.LayoutParams.MatchParent,
            ViewGroup.LayoutParams.MatchParent);
    }

    private async void LaunchApp(LauncherApp app)
    {
        try
        {
            var services = App.Services ?? throw new InvalidOperationException("9-1 Home is unavailable.");
            await services.GetRequiredService<AndroidInstalledApplicationsGoProvider>().InvokeAsync(
                new("Home", "os.installed-application", app.ApplicationId.ToString("D"), app.RegistryRevision.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                "Open", _launcherLifetime.Token);
        }
        catch (OperationCanceledException) when (_launcherLifetime.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            Toast.MakeText(this, $"Could not open {app.Label}: {exception.Message}", ToastLength.Long)?.Show();
        }
    }
}
