using NineToOne.Launcher;
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

            await _layoutEdits.WaitAsync(_launcherLifetime.Token);
            try
            {
                var layout = await LayoutStore.GetAsync(ApplySavedOrder(apps).Select(a => a.ApplicationId).ToArray(),
                    Math.Clamp(Preferences.GetInt(RowsKey, 5), 3, 8), Math.Clamp(Preferences.GetInt(ColumnsKey, 4), 3, 7), _launcherLifetime.Token);
                if (generation != Volatile.Read(ref _appLoadGeneration)) return;
                _apps.Clear(); _apps.AddRange(apps); _layout = layout;
                _page = layout.Current.Pages.ToList().FindIndex(p => p.Id == layout.Current.ActivePageId);
            }
            finally { _layoutEdits.Release(); }
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

        if (_layout is null) return;
        var layout = _layout.Current; var rows = layout.Rows; var columns = layout.Columns;
        var page = layout.ActivePage;
        _grid.RemoveAllViews(); _grid.RowCount = rows; _grid.ColumnCount = columns;

        var metrics = Resources?.DisplayMetrics;
        var gridWidth = _grid.Width > 0
            ? _grid.Width
            : Math.Max(Dp(64), (metrics?.WidthPixels ?? Dp(360)) - Dp(24));
        var gridHeight = _grid.Height > 0
            ? _grid.Height
            : Math.Max(Dp(78), (metrics?.HeightPixels ?? Dp(640)) - Dp(180));
        var cellWidth = Math.Max(MinimumTileWidth, gridWidth / columns);
        var cellHeight = Math.Max(TileHeight, gridHeight / rows);

        for (var row = 0; row < rows; row++) for (var column = 0; column < columns; column++)
        {
            var placement = page.Items.SingleOrDefault(item => item.Column == column && item.Row == row);
            if (placement?.FolderId is not null) _grid.AddView(BuildFolderTile(placement, cellWidth, cellHeight));
            else if (placement is not null)
            {
                var app = _apps.SingleOrDefault(item => item.ApplicationId == placement.ApplicationId)
                    ?? new LauncherApp("Unavailable application", "", "", null, placement.ApplicationId, 0, "", "Unavailable profile", false, false);
                _grid.AddView(BuildAppTile(app, cellWidth, cellHeight, placement));
            }
            else
            {
                var targetColumn = column; var targetRow = row;
                var empty = new Button(this) { Text = _movingPlacementId is null ? "" : "+", Enabled = _movingPlacementId is not null,
                    ContentDescription = $"Empty slot, row {row + 1}, column {column + 1}", LayoutParameters = new ViewGroup.LayoutParams(cellWidth, cellHeight) };
                empty.SetBackgroundColor(Color.Transparent);
                empty.Click += (_, _) => { if (_movingPlacementId is { } moving) _ = EditLayoutAsync(current => LauncherLayoutEdits.MovePlacement(current, moving, page.Id, targetColumn, targetRow)); };
                _grid.AddView(empty);
            }
        }
        _pageIndicator.Text = $"{page.Name} · {_page + 1} / {layout.Pages.Count} · Manage pages";
        AndroidTypography.ApplyTree(_grid);
        RenderDock();
        RefreshOpenFolder();
    }

    private View BuildAppTile(LauncherApp app, int width, int height, LauncherPlacement? placement = null)
    {
        var appearance = CurrentPresentation;
        height = Math.Max(height, TileHeight);
        var tile = new LinearLayout(this)
        {
            Orientation = Orientation.Vertical,
            Focusable = true,
            Clickable = true,
            ContentDescription = $"{app.Label}, {app.ProfileLabel}" + (app.Available ? string.Empty : ", unavailable"),
            LayoutParameters = new ViewGroup.LayoutParams(width, height)
        };
        tile.SetGravity(GravityFlags.Center);
        tile.SetPadding(Dp(appearance.HorizontalSpacingDp), Dp(appearance.VerticalSpacingDp), Dp(appearance.HorizontalSpacingDp), Dp(appearance.VerticalSpacingDp));
        if (!app.Available) tile.Alpha = 0.5f;

        var iconSize = Math.Min(Dp(appearance.IconSizeDp), Math.Max(Dp(24), width - Dp(appearance.HorizontalSpacingDp * 2)));
        var icon = new ImageView(this)
        {
            LayoutParameters = new LinearLayout.LayoutParams(iconSize, iconSize)
        };
        icon.SetImageDrawable(app.Icon);
        tile.AddView(icon);

        if (appearance.ShowLabels)
        {
            var label = new TextView(this)
            {
                Text = app.Label,
                Gravity = GravityFlags.Center,
                TextSize = appearance.LabelSizeSp
            };
            label.SetMaxLines(1);
            label.Ellipsize = global::Android.Text.TextUtils.TruncateAt.End;
            label.SetTextColor(Color.White);
            tile.AddView(label);
        }

        if (appearance.ShowPackages)
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

        tile.LongClick += (_, args) => { ShowPlacementMenu(app, placement); args.Handled = true; };
        tile.Click += (_, _) =>
        {
            if (_movingPlacementId is { } moving && placement is not null && _layout is not null)
            { _ = EditLayoutAsync(layout => LauncherLayoutEdits.MovePlacement(layout, moving, LauncherLayoutEdits.ContainerForPlacement(layout, placement.Id), placement.Column, placement.Row)); return; }
            if (app.Available) LaunchApp(app);
            else Toast.MakeText(this, "This application's owning profile or package is currently unavailable. Its shortcut was preserved.", ToastLength.Long)?.Show();
        };
        return tile;
    }

    private int PageCount => _layout?.Current.Pages.Count ?? 1;

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
        if (_layout is null) return;
        var next = Math.Clamp(_page + delta, 0, _layout.Current.Pages.Count - 1);
        if (next == _page) return;
        var id = _layout.Current.Pages[next].Id;
        var moving = _movingPlacementId;
        _ = SelectPageAsync(id, moving);
    }
    private async Task SelectPageAsync(Guid id, Guid? moving)
    {
        await EditLayoutAsync(layout => LauncherLayoutEdits.SelectPage(layout, id));
        _movingPlacementId = moving; RenderPage();
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
        var pageSearch = false;
        var categories = new LinearLayout(this) { Orientation = Orientation.Horizontal };
        var appsCategory = new Button(this) { Text = "Apps" }; var pagesCategory = new Button(this) { Text = "Launcher pages" };
        categories.AddView(appsCategory); categories.AddView(pagesCategory);
        var organize = new Button(this) { Text = "Categories / sort" };
        categories.AddView(organize);
        var categoryScroll = new HorizontalScrollView(this); categoryScroll.AddView(categories); shell.AddView(categoryScroll);

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
            ColumnCount = Math.Min(_layout?.Current.Columns ?? 4, Math.Max(1, drawerWidth / MinimumTileWidth))
        };
        var width = Math.Max(MinimumTileWidth, drawerWidth / grid.ColumnCount);
        var engine = (App.Services ?? throw new InvalidOperationException("9-1 Home is unavailable.")).GetRequiredService<GoService>();
        var dialogLifetime = CancellationTokenSource.CreateLinkedTokenSource(_launcherLifetime.Token);
        var dialogToken = dialogLifetime.Token;
        CancellationTokenSource? currentSearch = null;
        var searchGeneration = 0;
        dialog.DismissEvent += (_, _) => { _refreshDrawer = null; dialogLifetime.Cancel(); currentSearch?.Cancel(); dialogLifetime.Dispose(); };
        async Task RenderMatchesAsync(string? query)
        {
            currentSearch?.Cancel();
            using var request = CancellationTokenSource.CreateLinkedTokenSource(dialogToken);
            currentSearch = request; var generation = Interlocked.Increment(ref searchGeneration);
            try
            {
                await Task.Delay(120, request.Token);
                var searchingPages = pageSearch;
                var presentation = searchingPages ? new Dictionary<Guid, LauncherApp>() : (await QueryAppsAsync(request.Token)).ToDictionary(app => app.ApplicationId);
                var scope = searchingPages ? new GoScope(new HashSet<string>(StringComparer.Ordinal) { LauncherNavigationGoProvider.Id },
                    new HashSet<string>(StringComparer.Ordinal) { "Launcher" }, new HashSet<string>(StringComparer.Ordinal) { "launcher.page" },
                    new HashSet<string>(StringComparer.Ordinal) { "OpenPage" }) : new GoScope(new HashSet<string>(StringComparer.Ordinal) { AndroidInstalledApplicationsGoProvider.Id },
                    new HashSet<string>(StringComparer.Ordinal) { "Home" }, new HashSet<string>(StringComparer.Ordinal) { "os.installed-application" },
                    new HashSet<string>(StringComparer.Ordinal) { "Open" });
                RunOnUiThread(() => { if (!request.IsCancellationRequested && generation == searchGeneration) grid.RemoveAllViews(); });
                var drawer = _layout?.Current.Drawer ?? LauncherDrawer.Empty;
                var selectedCategory = drawer.Categories.SingleOrDefault(c => c.Id == _drawerCategoryId);
                if (selectedCategory is null) _drawerCategoryId = null;
                var appMatches = new List<LauncherApp>();
                var count = 0; var failed = false;
                await foreach (var update in engine.QueryAsync(new(query?.Trim() ?? "", searchingPages ? "Launcher Pages" : "Apps", 1000, scope), request.Token))
                {
                    if (update.Failure is not null) failed = true;
                    if (update.Result is not { } result) continue;
                    if (searchingPages)
                    {
                        count++;
                        RunOnUiThread(() =>
                        {
                            if (request.IsCancellationRequested || generation != searchGeneration) return;
                            var pageButton = new Button(this) { Text = result.Label, ContentDescription = "Open launcher page " + result.Label,
                                LayoutParameters = new ViewGroup.LayoutParams(width, Dp(96)) };
                            pageButton.Click += async (_, _) =>
                            {
                                try { await engine.InvokeAsync(result, "OpenPage", scope, dialogToken); dialog.Dismiss(); LoadAppsAsync(); }
                                catch (OperationCanceledException) when (dialogToken.IsCancellationRequested) { }
                                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
                                { Toast.MakeText(this, ex.Message, ToastLength.Long)?.Show(); }
                            };
                            grid.AddView(pageButton);
                        });
                        continue;
                    }
                    if (!Guid.TryParse(result.Reference.Id, out var id) ||
                        !long.TryParse(result.Reference.Revision, out var revision) || !presentation.TryGetValue(id, out var app) || _layout?.Current.HiddenApplications.Contains(id) == true) continue;
                    if (selectedCategory is not null && !selectedCategory.Applications.Contains(id)) continue;
                    var current = app with { Label = result.Label, RegistryRevision = revision, Available = true };
                    count++;
                    appMatches.Add(current);
                }
                if (!searchingPages)
                {
                    var order = selectedCategory?.Applications.Select((id, index) => (id, index)).ToDictionary(pair => pair.id, pair => pair.index);
                    IEnumerable<LauncherApp> sorted = drawer.Sort switch
                    {
                        LauncherDrawerSort.ReverseAlphabetical => appMatches.OrderByDescending(app => app.Label, StringComparer.CurrentCultureIgnoreCase).ThenBy(app => app.ApplicationId),
                        LauncherDrawerSort.CategoryOrder when order is not null => appMatches.OrderBy(app => order.GetValueOrDefault(app.ApplicationId, int.MaxValue)).ThenBy(app => app.ApplicationId),
                        _ => appMatches.OrderBy(app => app.Label, StringComparer.CurrentCultureIgnoreCase).ThenBy(app => app.ApplicationId)
                    };
                    var visible = sorted.ToArray();
                    RunOnUiThread(() =>
                    {
                        if (request.IsCancellationRequested || generation != searchGeneration) return;
                        title.Text = selectedCategory?.Name ?? "All apps";
                        foreach (var app in visible) grid.AddView(BuildAppTile(app, width, Dp(96)));
                    });
                }
                if (count == 0)
                    RunOnUiThread(() =>
                    {
                        if (request.IsCancellationRequested || generation != searchGeneration) return;
                        var empty = new TextView(this) { Text = failed ? "Search is temporarily unavailable. Retry or repair Home." : searchingPages ? "No launcher pages match this search." : "No installed apps match this search.",
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
        organize.Click += (_, _) =>
        {
            pageSearch = false; title.Text = "All apps"; search.Hint = "Search installed apps";
            _ = RenderMatchesAsync(search.Text); ShowDrawerOrganization();
        };
        appsCategory.Click += (_, _) => { pageSearch = false; title.Text = "All apps"; search.Hint = "Search installed apps"; _ = RenderMatchesAsync(search.Text); };
        pagesCategory.Click += (_, _) => { pageSearch = true; title.Text = "Launcher pages"; search.Hint = "Search launcher pages"; _ = RenderMatchesAsync(search.Text); };
        _refreshDrawer = () => _ = RenderMatchesAsync(search.Text);
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
