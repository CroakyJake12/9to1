using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CakeOS.Apps.Boards.App;
using CakeOS.Cui.Runtime;
using CakeOS.Cui.Themes;
using CakeOS.Cui;
using CakeOS.Cui.Language;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.Services;
using Haven.Desktop.Views.Pages.Home;
using HavenOS.Apps.Browse;
using HavenOS.Home;
using HavenOS.Home.Core;

namespace Haven.Desktop.Tests;

/// <summary>Focused component smoke cases with explicit controlled index/permission inputs.
/// These verify real canonical CUI and domain routing, not production Notes/Home/native acceptance.</summary>
public sealed class HomeLibraryDestinationTests
{
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Omnibox_search_activates_the_actual_Library_query_and_scopes_Files_without_replacing_authorization(bool filesOnly)
    {
        var index = new Index(Array.Empty<HomeArtifactReference>());
        var page = Page(index, new(), (_, _) => true);
        page.SetInitialSearch("Résumé / quarterly notes", filesOnly);
        try
        {
            await page.ActivateAsync(CancellationToken.None);
            Assert.Equal("Résumé / quarterly notes", index.LastQuery!.Text);
            Assert.Equal(filesOnly ? "files" : null, index.LastQuery.OwnerApp);
            Assert.Null(index.LastQuery.ArtifactType);
            Assert.Throws<InvalidOperationException>(() => page.SetInitialSearch("changed", false));
        }
        finally { await page.CloseAndDrainAsync(); }
    }

    [AvaloniaFact]
    public async Task Empty_filtered_Boards_Library_uses_the_existing_create_and_audit_retry_host()
    {
        var index = new Index(Array.Empty<HomeArtifactReference>());
        var page = Page(index, new(), (_, _) => true);
        var host = new BoardCreationHost();
        byte[] draft = [1, 2, 3, 4];
        page.SetInitialQuery("files", "9to1 board document");
        page.SetBoardCreationHost(host, () => draft);
        Window? window = null;
        try
        {
            await page.ActivateAsync(CancellationToken.None); window = Show(page);
            Assert.Empty(page.Current!.Items);
            var create = FindAuthoredButton(page, "library-create-board");
            create.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await page.WhenActionsIdleAsync().WaitAsync(TimeSpan.FromSeconds(10));

            Assert.Equal(1, host.CreateCalls);
            Assert.Equal(draft, host.CreatedDraft);
            Assert.Contains("audit is still pending", Assert.Single(page.GetVisualDescendants().OfType<TextBlock>(),
                control => CuiRuntimeIdentity.GetStableId(control) == "id:library-status").Text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(page.GetVisualDescendants().OfType<Button>(), button =>
                CuiRuntimeIdentity.GetStableId(button) == "id:library-create-board");

            FindAuthoredButton(page, "library-retry-board-audit").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await page.WhenActionsIdleAsync().WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(1, host.CreateCalls); // Recovery settled the same Home-owned attempt.
            Assert.Equal(1, host.SettlementRetries);
            Assert.Contains("created and opened", Assert.Single(page.GetVisualDescendants().OfType<TextBlock>(),
                control => CuiRuntimeIdentity.GetStableId(control) == "id:library-status").Text, StringComparison.OrdinalIgnoreCase);
        }
        finally { try { await page.CloseAndDrainAsync(); } finally { window?.Close(); } }
    }

    [AvaloniaFact]
    public async Task Native_dialog_background_tracks_the_loaded_Home_theme_scope()
    {
        var source = new ThemeSource();
        using var theme = new CuiThemePreferenceContext(source);
        var primitiveBackground = new SolidColorBrush(Color.FromRgb(1, 2, 3));
        var application = global::Avalonia.Application.Current!;
        var hadPrimitiveBackground = application.Resources.TryGetValue("CuiBackgroundBrush", out var originalPrimitiveBackground);
        application.Resources["CuiBackgroundBrush"] = primitiveBackground;
        var page = Page(new Index(0), new(), (_, _) => true, theme: theme);
        var window = new Window { Width = 800, Height = 620, Content = page };
        IDisposable? backgroundBinding = null;
        try
        {
            await page.ActivateAsync(CancellationToken.None);
            window.Show(); window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
            backgroundBinding = page.BindWindowBackground(window);

            var root = Assert.IsAssignableFrom<Control>(page.Content);
            AssertHomeThemeBackground(root, window, primitiveBackground, CuiAppearance.Bright);

            source.SetAppearance(CuiAppearance.SuperDark);
            window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
            AssertHomeThemeBackground(root, window, primitiveBackground, CuiAppearance.SuperDark);
        }
        finally
        {
            backgroundBinding?.Dispose();
            try { await page.CloseAndDrainAsync(); }
            finally
            {
                window.Close();
                if (hadPrimitiveBackground) application.Resources["CuiBackgroundBrush"] = originalPrimitiveBackground;
                else application.Resources.Remove("CuiBackgroundBrush");
            }
        }
    }

    private static void AssertHomeThemeBackground(Control root, Window window, IBrush primitiveBackground,
        CuiAppearance appearance)
    {
        Assert.True(root.Resources.TryGetResource("CuiBackgroundBrush", null, out var rootResource));
        var homeBackground = Assert.IsAssignableFrom<ISolidColorBrush>(rootResource);
        var expected = CuiSurfacePaletteCatalog.For("Home", appearance, CuiTheme.Glow).TideBase;
        Assert.Equal(expected, homeBackground.Color);
        var dialogBackground = Assert.IsAssignableFrom<ISolidColorBrush>(window.Background);
        Assert.Equal(expected, dialogBackground.Color);
        Assert.Equal(byte.MaxValue, dialogBackground.Color.A);
        Assert.NotEqual(Assert.IsAssignableFrom<ISolidColorBrush>(primitiveBackground).Color, dialogBackground.Color);
    }

    [AvaloniaFact]
    public async Task Folder_selection_requires_explicit_confirmation_and_revalidates_the_same_current_Home_link()
    {
        var store = Guid.NewGuid(); var folder = Guid.NewGuid();
        var link = new HomeDeepLink(HomeRoute.Library, "files", store.ToString("D") + "/" + folder.ToString("D"),
            "open", "files.folder");
        var row = new HomeArtifactReference("files", store.ToString("D") + "/" + folder.ToString("D"),
            "Boards", "Files folder", "Files", "fixture-profile", null, DateTimeOffset.UtcNow,
            false, false, false, false, [], link);
        var index = new Index([row]); var page = Page(index, new(), (_, _) => true);
        var checks = 0;
        page.SetFolderSelectionMode((selected, _) => { checks++; return Task.FromResult<HomeDeepLink?>(selected); });
        Window? window = null;
        try
        {
            await page.ActivateAsync(CancellationToken.None); window = Show(page);
            Assert.True(page.IsSelectionMode);
            Assert.False(page.IsActionAvailable("ToggleHomeLibraryPin"));
            var name = Assert.Single(page.GetVisualDescendants().OfType<TextBox>(), control =>
                CuiRuntimeIdentity.GetStableId(control) == "id:library-board-create-name");
            name.Text = "  Q4 planning  ";
            await page.DispatchAsync("OpenHomeLibraryArtifact", row.OpenLink);
            await page.WhenActionsIdleAsync().WaitAsync(TimeSpan.FromSeconds(10));
            Assert.False(page.FolderSelection.IsCompleted);
            Assert.Null(page.SelectedBoardCreateDisplayName);
            Assert.Equal(1, checks);
            Assert.True(FindAuthoredButton(page, "library-confirm-board-folder").IsEnabled);
            Assert.True(FindAuthoredButton(page, "library-cancel-board-folder").IsEnabled);
            await page.DispatchAsync("ConfirmBoardFolder", null);
            Assert.Equal(2, checks);
            Assert.Same(link, await page.FolderSelection);
            Assert.Equal("Q4 planning", page.SelectedBoardCreateDisplayName);
            name.Text = "changed after selection";
            Assert.Equal("Q4 planning", page.SelectedBoardCreateDisplayName);
        }
        finally { try { await page.CloseAndDrainAsync(); } finally { window?.Close(); } }
    }

    [AvaloniaFact]
    public async Task Canonical_Library_row_opens_the_same_authorised_identity_through_the_actual_queue_callback()
    {
        var index = new Index(1); var authorization = new Authorization();
        HomeDeepLink? queued = null; Task? settlement = null;
        var page = Page(index, authorization, (link, original) => { queued = link; settlement = original; return true; });
        Window? window = null;
        try
        {
            await page.ActivateAsync(CancellationToken.None); window = Show(page);
            var row = FindLibraryButton(page, "home-library-item");
            Assert.Equal("Notebook 00", global::Avalonia.Automation.AutomationProperties.GetName(row));
            row.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await page.WhenActionsIdleAsync().WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Same(index.Rows[0].OpenLink, queued); Assert.NotNull(settlement);
            await settlement!.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(index.Rows[0].ArtifactId, queued!.ObjectId);
            Assert.Equal("boards", queued.OwnerApp); Assert.Equal("boards.notebook", queued.TargetRouteId);
            Assert.Single(index.Rows); // Home navigation did not create a second artifact.
            Assert.Equal(1, authorization.Opens);
        }
        finally { try { await page.CloseAndDrainAsync(); } finally { window?.Close(); } }
    }
    [AvaloniaFact]
    public async Task Library_row_discloses_actual_recent_shared_and_generated_metadata()
    {
        var id = Guid.NewGuid().ToString("D");
        var link = new HomeDeepLink(HomeRoute.Library, "boards", id, "open", "boards.notebook");
        var row = new HomeArtifactReference("boards", id, "Shared notebook", "Boards notebook", "Team space",
            "fixture-profile", "Morgan", DateTimeOffset.UnixEpoch, IsRecent: true, IsPinned: true,
            IsShared: true, IsGenerated: true, Tags: ["team"], OpenLink: link);
        var preferences = new Preferences();
        await preferences.SetPinnedAsync("boards", id, true, CancellationToken.None);
        var page = Page(new Index([row]), new(), (_, _) => true, preferences); Window? window = null;
        try
        {
            await page.ActivateAsync(CancellationToken.None); window = Show(page);
            Assert.Equal("Recent", Assert.Single(page.GetVisualDescendants().OfType<TextBlock>(),
                control => HasAuthoredIdentity(control, "home-library-recent")).Text);
            Assert.Equal("Favourite", Assert.Single(page.GetVisualDescendants().OfType<TextBlock>(),
                control => HasAuthoredIdentity(control, "home-library-pinned")).Text);
            Assert.Equal("Shared by", Assert.Single(page.GetVisualDescendants().OfType<TextBlock>(),
                control => HasAuthoredIdentity(control, "home-library-shared")).Text);
            Assert.Equal("Morgan", Assert.Single(page.GetVisualDescendants().OfType<TextBlock>(),
                control => HasAuthoredIdentity(control, "home-library-shared-by")).Text);
            Assert.Equal("Generated", Assert.Single(page.GetVisualDescendants().OfType<TextBlock>(),
                control => HasAuthoredIdentity(control, "home-library-generated")).Text);
        }
        finally { try { await page.CloseAndDrainAsync(); } finally { window?.Close(); } }
    }
    [AvaloniaFact]
    public async Task Library_rechecks_permission_before_open_and_removes_the_now_unavailable_projection()
    {
        var index = new Index(1); var authorization = new Authorization(); var queued = false;
        var page = Page(index, authorization, (_, _) => queued = true); Window? window = null;
        try
        {
            await page.ActivateAsync(CancellationToken.None); window = Show(page);
            var row = FindLibraryButton(page, "home-library-item");
            authorization.Allowed = false;
            row.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await page.WhenActionsIdleAsync().WaitAsync(TimeSpan.FromSeconds(10));
            Assert.False(queued); Assert.Null(page.Current);
            Assert.DoesNotContain(page.GetVisualDescendants().OfType<Button>(), item => HasAuthoredIdentity(item, "home-library-item"));
            await page.CloseAndDrainAsync(); Assert.True(page.OriginalClose!.IsCompletedSuccessfully);
        }
        finally { try { await page.CloseAndDrainAsync(); } finally { window?.Close(); } }
    }
    [AvaloniaFact]
    public async Task Library_search_and_page_buttons_use_shared_domain_paging_and_actual_input()
    {
        var index = new Index(31); var page = Page(index, new(), (_, _) => true); Window? window = null;
        try
        {
            await page.ActivateAsync(CancellationToken.None); window = Show(page);
            Assert.Equal(25, page.Current!.Items.Count); Assert.True(page.Current.HasMore);
            await page.DispatchAsync("NextLibraryPage", null);
            Assert.Equal(6, page.Current!.Items.Count); Assert.False(page.Current.HasMore);
            await page.DispatchAsync("PreviousLibraryPage", null);
            Assert.Equal(25, page.Current!.Items.Count);
            Assert.Single(page.GetVisualDescendants().OfType<TextBox>(), item => item.Name == "library-search").Text = "Notebook 30";
            await page.DispatchAsync("SearchLibrary", null);
            Assert.Equal("Notebook 30", Assert.Single(page.Current!.Items).DisplayName);
            Assert.Equal("Notebook 30", index.LastQuery!.Text); Assert.Equal(0, index.LastQuery.Offset);
        }
        finally { try { await page.CloseAndDrainAsync(); } finally { window?.Close(); } }
    }
    [AvaloniaFact]
    public async Task Library_filters_sort_and_page_resume_are_sent_through_the_shared_authorized_query()
    {
        var index = new Index(31); var page = Page(index, new(), (_, _) => true); Window? window = null;
        try
        {
            await page.ActivateAsync(CancellationToken.None); window = Show(page);
            await page.DispatchAsync("ToggleLibraryFilters", null);
            var apps = Assert.Single(page.GetVisualDescendants().OfType<ComboBox>(), item => item.Name == "library-owner-filter");
            apps.SelectedItem = apps.ItemsSource!.Cast<object>().Single(item => item.ToString() == "Boards");
            Assert.Single(page.GetVisualDescendants().OfType<TextBox>(), item => item.Name == "library-type-filter").Text = "Boards notebook";
            Assert.Single(page.GetVisualDescendants().OfType<ComboBox>(), item => item.Name == "library-sort").SelectedIndex = 1;
            Assert.Single(page.GetVisualDescendants().OfType<ComboBox>(), item => item.Name == "library-pinned-filter").SelectedIndex = 2;
            Assert.Single(page.GetVisualDescendants().OfType<ComboBox>(), item => item.Name == "library-shared-filter").SelectedIndex = 2;
            Assert.Single(page.GetVisualDescendants().OfType<ComboBox>(), item => item.Name == "library-generated-filter").SelectedIndex = 2;
            await page.DispatchAsync("SearchLibrary", null);
            Assert.Equal("boards", index.LastQuery!.OwnerApp); Assert.Equal("Boards notebook", index.LastQuery.ArtifactType);
            Assert.Equal(false, index.LastQuery.Pinned); Assert.Equal(false, index.LastQuery.Shared); Assert.Equal(false, index.LastQuery.Generated);
            Assert.Equal("oldest", index.LastQuery.Sort); Assert.Equal(0, index.LastQuery.Offset);
            Assert.Equal("Notebook 30", page.Current!.Items[0].DisplayName);
            await page.DispatchAsync("NextLibraryPage", null);
            Assert.Equal(25, index.LastQuery.Offset); Assert.Equal("oldest", index.LastQuery.Sort);
            page.Deactivate(); await page.ActivateAsync(CancellationToken.None);
            Assert.Equal(25, index.LastQuery!.Offset); Assert.Equal("boards", index.LastQuery.OwnerApp);
            Assert.Equal("Boards notebook", index.LastQuery.ArtifactType); Assert.Equal("oldest", index.LastQuery.Sort);
        }
        finally { try { await page.CloseAndDrainAsync(); } finally { window?.Close(); } }
    }
    [AvaloniaFact]
    public async Task Library_pin_toggle_uses_the_same_current_artifact_identity_and_refreshes_projection()
    {
        var index = new Index(1); var authorization = new Authorization(); var preferences = new Preferences();
        var page = Page(index, authorization, (_, _) => true, preferences); Window? window = null;
        try
        {
            await page.ActivateAsync(CancellationToken.None); window = Show(page);
            var pin = FindLibraryButton(page, "home-library-toggle-pin");
            pin.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await page.WhenActionsIdleAsync().WaitAsync(TimeSpan.FromSeconds(10));
            var pinned = Assert.Single(page.Current!.Items);
            Assert.Equal(index.Rows.Single().ArtifactId, pinned.ArtifactId);
            Assert.True(pinned.IsPinned); Assert.True(preferences.IsPinned("boards", pinned.ArtifactId));
            Assert.True(authorization.Reads >= 2); // Search and the fresh pin action both recheck current read access.
            Assert.Single(index.Rows); // Pinning a reference never creates a copied artifact.
            pin = FindLibraryButton(page, "home-library-toggle-pin");
            pin.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await page.WhenActionsIdleAsync().WaitAsync(TimeSpan.FromSeconds(10));
            Assert.False(Assert.Single(page.Current!.Items).IsPinned);
            Assert.False(preferences.IsPinned("boards", pinned.ArtifactId));
        }
        finally { try { await page.CloseAndDrainAsync(); } finally { window?.Close(); } }
    }

    [AvaloniaFact]
    public async Task Library_views_and_named_app_filter_use_canonical_membership_without_copying_artifacts()
    {
        static HomeArtifactReference Row(string app, string name, bool shared)
        {
            var id = Guid.NewGuid().ToString("D");
            return new(app, id, name, app == "boards" ? "Boards notebook" : "Files document", "Work", "fixture", shared ? "Morgan" : null,
                DateTimeOffset.UtcNow, false, false, shared, false, [], new(HomeRoute.Library, app, id, "open", app + ".document"));
        }
        var rows = new[] { Row("boards", "Shared research", true), Row("files", "Local outline", false), Row("boards", "Private notes", false) };
        var preferences = new Preferences();
        foreach (var row in rows.Take(2)) await preferences.SetPinnedAsync(row.OwnerApp, row.ArtifactId, true, CancellationToken.None);
        var index = new Index(rows); var page = Page(index, new(), (_, _) => true, preferences);
        Window? window = null;
        try
        {
            await page.ActivateAsync(CancellationToken.None); window = Show(page);
            FindAuthoredButton(page, "library-view-favourites").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await page.WhenActionsIdleAsync().WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(2, page.Current!.Items.Count); Assert.True(index.LastQuery!.Pinned);
            var apps = Assert.Single(page.GetVisualDescendants().OfType<ComboBox>(), control => control.Name == "library-owner-filter");
            Assert.Equal(new[] { "All apps", "Boards", "Files" }, apps.ItemsSource!.Cast<object>().Select(item => item.ToString()));
            apps.SelectedItem = apps.ItemsSource!.Cast<object>().Single(item => item.ToString() == "Boards");
            await page.DispatchAsync("SearchLibrary", null);
            Assert.Same(rows[0].OpenLink, Assert.Single(page.Current!.Items).OpenLink);
            Assert.Equal("boards", index.LastQuery!.OwnerApp);
            var star = FindLibraryButton(page, "home-library-toggle-pin");
            Assert.Equal("Remove Shared research from favourites", AutomationProperties.GetName(star));
            star.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await page.WhenActionsIdleAsync().WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Empty(page.Current!.Items); Assert.Equal(3, index.Rows.Count);
            FindAuthoredButton(page, "library-view-shared").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await page.WhenActionsIdleAsync().WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Same(rows[0].OpenLink, Assert.Single(page.Current!.Items).OpenLink);
            Assert.True(index.LastQuery!.Shared); Assert.Null(index.LastQuery.Pinned);
            FindAuthoredButton(page, "library-view-recent").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await page.WhenActionsIdleAsync().WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(2, page.Current!.Items.Count); Assert.Equal("recent", index.LastQuery!.Sort);
            Assert.Null(index.LastQuery.Pinned); Assert.Null(index.LastQuery.Shared);
        }
        finally { try { await page.CloseAndDrainAsync(); } finally { window?.Close(); } }
    }

    [AvaloniaTheory]
    [InlineData("Glow", 1440, 900)]
    [InlineData("Glow", 1280, 720)]
    [InlineData("Glow", 720, 600)]
    [InlineData("Professional", 1440, 900)]
    [InlineData("Professional", 1280, 720)]
    [InlineData("Professional", 720, 600)]
    public async Task Library_columns_align_on_wide_windows_and_stack_inside_narrow_rows(string themeName, int width, int height)
    {
        using var theme = new CuiThemePreferenceContext(new ThemeSource(themeName, CuiAppearance.Dark));
        var page = Page(new Index(2), new(), (_, _) => true, theme: theme);
        var window = new Window { Content = page, Width = width, Height = height };
        using var background = page.BindWindowBackground(window);
        try
        {
            await page.ActivateAsync(CancellationToken.None); window.Show();
            window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            var controls = page.GetVisualDescendants().OfType<Control>().ToArray();
            var rows = controls.Where(control => HasAuthoredIdentity(control, "home-library-row")).ToArray();
            Assert.Equal(2, rows.Length);
            var search = controls.OfType<TextBox>().Single(box => box.Name == "library-search");
            Assert.True(search.TryFindResource("CuiEffectiveThemeName", out var effectiveTheme));
            Assert.Equal(themeName, effectiveTheme);
            var tabs = controls.Single(control => control.Name == "library-view-recent");
            var note = controls.OfType<TextBlock>().Single(text => text.Text ==
                "Recent shows the most recently modified items. Access is checked again when you open an item.");
            var status = controls.Single(control => control.Name == "library-status");
            AssertBelow(tabs, search); AssertBelow(search, note); AssertBelow(note, status);
            AssertBelow(status, rows[0]); AssertBelow(rows[0], rows[1]);
            Assert.DoesNotContain(controls.OfType<TextBox>(), box => box.IsEffectivelyVisible && box.Name == "library-type-filter");
            foreach (var row in rows)
            {
                var position = row.TranslatePoint(default, page)!.Value;
                Assert.True(position.X >= 0 && position.X + row.Bounds.Width <= page.Bounds.Width + 1);
            }
            var name = controls.First(control => HasAuthoredIdentity(control, "home-library-name-cell"));
            var app = controls.First(control => HasAuthoredIdentity(control, "home-library-app-cell"));
            var modified = controls.First(control => HasAuthoredIdentity(control, "home-library-modified-cell"));
            if (width >= 780)
            {
                var headerApp = controls.Single(control => control.Name == "library-heading-app");
                var headerDate = controls.Single(control => control.Name == "library-heading-modified");
                Assert.Equal(headerApp.TranslatePoint(default, page)!.Value.X, app.TranslatePoint(default, page)!.Value.X, 1);
                Assert.Equal(headerDate.TranslatePoint(default, page)!.Value.X, modified.TranslatePoint(default, page)!.Value.X, 1);
                AssertBelow(headerApp, rows[0]); AssertBelow(headerDate, rows[0]);
            }
            else
            {
                Assert.True(app.TranslatePoint(default, page)!.Value.Y >= name.TranslatePoint(default, page)!.Value.Y + name.Bounds.Height);
                Assert.True(modified.TranslatePoint(default, page)!.Value.Y > app.TranslatePoint(default, page)!.Value.Y);
            }
            Assert.Contains(controls.OfType<TextBlock>(), text => text.IsEffectivelyVisible && text.Text == "Checked on open");
            Assert.DoesNotContain(controls.OfType<TextBlock>(), text => text.IsEffectivelyVisible && text.Text is "Synced" or "Saved locally");
            if (Environment.GetEnvironmentVariable("HAVEN_VISUAL_CAPTURE_DIR") is { Length: > 0 } captures)
            {
                Directory.CreateDirectory(captures);
                using var frame = window.CaptureRenderedFrame(); Assert.NotNull(frame);
                frame.Save(Path.Combine(captures, $"home-library-{themeName.ToLowerInvariant()}-{width}x{height}.png"));
            }
        }
        finally { try { await page.CloseAndDrainAsync(); } finally { window.Close(); } }

        void AssertBelow(Control above, Control below)
        {
            var upper = above.TranslatePoint(default, page)!.Value;
            var lower = below.TranslatePoint(default, page)!.Value;
            Assert.True(lower.Y >= upper.Y + above.Bounds.Height - .5,
                $"{below.Name} at {lower} must follow {above.Name} at {upper}, height {above.Bounds.Height}.");
        }
    }

    [AvaloniaFact]
    public async Task Library_preflight_joins_delayed_original_and_cancelling_its_wait_does_not_retire_the_page()
    {
        var index = new Index(1) { Held = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        var page = Page(index, new(), (_, _) => true);
        var initialization = page.ActivateAsync(CancellationToken.None);
        await index.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        using var waiter = new CancellationTokenSource();
        var first = page.TryPrepareToCloseAsync(waiter.Token);
        Assert.False(first.IsCompleted); waiter.Cancel();
        Assert.IsAssignableFrom<OperationCanceledException>(await Record.ExceptionAsync(() => first));
        Assert.True(page.IsAlive); Assert.False(initialization.IsCompleted);
        index.Held.TrySetResult(index.Rows);
        await initialization.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.True(await page.TryPrepareToCloseAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.Single(page.Current!.Items);
        await page.CloseAndDrainAsync();
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Shared_Library_fragment_keeps_legacy_canonical_rows_and_status_without_new_native_bindings(bool standaloneProjection)
    {
        var id = Guid.NewGuid().ToString("D");
        var link = new HomeDeepLink(HomeRoute.Library, "boards", id, "open", "boards.notebook");
        var entry = new HomeArtifactReference("boards", id, "Existing notebook", "Boards notebook", "Shared folder", "fixture", "Morgan",
            DateTimeOffset.UtcNow, true, true, true, true, [], link);
        var observed = new HomeLibraryPage([entry], 0, 25, false);
        var surface = HomeCuiSurface.LoadDefault(); surface.ApplyLibraryPage(observed);
        var document = surface.Document;
        static IEnumerable<CuiComponent> Nodes(CuiComponent node) => new[] { node }
            .Concat(node.Children.SelectMany(Nodes)).Concat(node.ElseChildren.SelectMany(Nodes));
        var scope = document.Components.Single(component => component.IsThemeScope);
        var library = Nodes(scope).Single(component => component.AuthoredId == "library-page");
        var isolatedScope = new CuiComponent(scope.Type, scope.Name, scope.Classes, scope.Properties, scope.Actions,
            [library], scope.Condition, scope.List, scope.Span, scope.DefaultTheme, scope.Repeat,
            scope.ElseChildren, scope.Groups, scope.IsDefinition, scope.PropertyRegion);
        var legacy = new CuiViewModel(); object? opened = null;
        legacy.Set("LibraryRows", NativeHomeArtifactLibrarySource.ProjectRows(observed));
        legacy.Set("LibrarySummary", "Observed legacy Library results.");
        legacy.Set("CanOpenHomeLibraryArtifact", true);
        legacy.On("OpenHomeLibraryArtifact", parameter => opened = parameter);
        using var loader = new CuiControlLoader(HomeAppsCuiDocument.CreateControlRegistry());
        loader.SetBindingContext(standaloneProjection ? legacy : surface);
        if (standaloneProjection) loader.SetActionDispatcher(legacy);
        var loaded = loader.TryLoad(document with { Components = [isolatedScope] });
        Assert.DoesNotContain(loaded.Diagnostics, value => value.Severity == CuiDiagnosticSeverity.Error);
        var root = Assert.IsAssignableFrom<Control>(loaded.Root); loader.WireBindings(root);
        var window = new Window { Content = root, Width = 620, Height = 600 };
        try
        {
            window.Show(); window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            var row = Assert.Single(root.GetVisualDescendants().OfType<Button>(), button => HasAuthoredIdentity(button, "home-library-item"));
            Assert.Equal(entry.DisplayName, AutomationProperties.GetName(row));
            var status = root.GetVisualDescendants().OfType<TextBlock>().Single(text => text.Name == "library-status");
            Assert.True(status.IsEffectivelyVisible); Assert.False(string.IsNullOrWhiteSpace(status.Text));
            var star = Assert.Single(root.GetVisualDescendants().OfType<Button>(), button => HasAuthoredIdentity(button, "home-library-toggle-pin"));
            Assert.Equal("★", star.Content); Assert.False(star.IsEnabled); // No new pin authority is invented.
            if (standaloneProjection)
            {
                row.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await loader.WhenActionsIdleAsync().WaitAsync(TimeSpan.FromSeconds(10));
                Assert.Same(link, opened);
                legacy.Set("LibraryRows", NativeHomeArtifactLibrarySource.ProjectRows(null));
            }
            else
            {
                Assert.False(row.IsEnabled); // Read-only domain projection has no native router grant.
                surface.ApplyLibraryPage(new([entry], 0, 25, false, "Unavailable", "The current source is unavailable."));
            }
            loader.RefreshBindings(); window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
            Assert.DoesNotContain(root.GetVisualDescendants().OfType<Button>(), button => HasAuthoredIdentity(button, "home-library-item"));
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task Library_preflight_preserves_genuine_admission_failure_as_a_close_veto()
    {
        var index = new Index(1);
        var failure = new InvalidOperationException("original library admission failed");
        var page = new HomeLibraryDestinationPage(router => new(index, new Authorization(), router, new Preferences()),
            _ => Task.FromException(failure), (_, _) => throw new InvalidOperationException("No navigation was admitted."));
        Assert.Contains(failure.Message, (await Record.ExceptionAsync(() => page.ActivateAsync(CancellationToken.None)))!.ToString());
        Assert.Contains(failure.Message, (await Record.ExceptionAsync(() => page.TryPrepareToCloseAsync(CancellationToken.None)))!.ToString());
        Assert.Contains(failure.Message, (await Record.ExceptionAsync(() => page.CloseAndDrainAsync()))!.ToString());
    }
    [AvaloniaFact]
    public async Task Library_close_waits_for_its_actual_delayed_query_and_does_not_publish_retired_rows()
    {
        var index = new Index(1) { Held = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        var page = Page(index, new(), (_, _) => throw new InvalidOperationException("No navigation was requested."));
        var initialization = page.ActivateAsync(CancellationToken.None);
        await index.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var close = page.CloseAndDrainAsync(); Assert.False(close.IsCompleted);
        index.Held.TrySetResult(index.Rows);
        var canceled = await Record.ExceptionAsync(() => initialization);
        Assert.IsAssignableFrom<OperationCanceledException>(canceled);
        await close.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.True(close.IsCompletedSuccessfully); Assert.Null(page.Current); Assert.Null(page.Content);
    }
    [AvaloniaFact]
    public async Task Library_route_is_composition_gated_and_preserves_private_NewTab_and_nested_refusals()
    {
        using var lifetime = new CancellationTokenSource();
        UnifiedHomeContentFactory Factory(bool composed) => new(new Registry(),
            () => throw new InvalidOperationException("Library cannot create Dashboard."),
            () => throw new InvalidOperationException("Library cannot create Models."), (_, _) => true,
            (_, _, _) => throw new InvalidOperationException("Library cannot create an AI draft."),
            (_, _) => Task.CompletedTask, () => true, lifetime.Token, false,
            library: composed ? _ => Page(new Index(1), new(), (_, _) => true) : null);
        BrowseAppContentRequest Request(string address) => new(Guid.NewGuid(), AppContentAddress.Parse(address), "fixture-profile", BrowserTabPrivacy.Standard);
        Assert.Throws<NotSupportedException>(() => Factory(false).CreateSession(Request("browse://home/library")));
        Assert.Throws<NotSupportedException>(() => Factory(true).CreateSession(Request("browse://newtab/library")));
        Assert.Throws<NotSupportedException>(() => Factory(true).CreateSession(Request("browse://home/library/nested")));
        Assert.Throws<NotSupportedException>(() => Factory(true).CreateSession(Request("browse://home/library") with { Privacy = BrowserTabPrivacy.Private }));
        var searchRoute = BrowseOmniboxSearchRoute.Create("quarterly notes", true).Value;
        Assert.Throws<NotSupportedException>(() => Factory(false).CreateSession(Request(searchRoute)));
        Assert.Throws<NotSupportedException>(() => Factory(true).CreateSession(Request(searchRoute) with { Privacy = BrowserTabPrivacy.Private }));
        var session = Assert.IsAssignableFrom<IBrowseNativeAppContentSession>(Factory(true).CreateSession(Request("browse://home/library")));
        Window? window = null;
        try
        {
            await session.InitializeAsync(); window = Show(session.OriginalNativeView);
            Assert.Single(session.OriginalNativeView.GetVisualDescendants().OfType<HomeLibraryDestinationPage>());
        }
        finally { try { await session.CloseAndDrainAsync(); } finally { window?.Close(); } }
        var search = Assert.IsAssignableFrom<IBrowseNativeAppContentSession>(Factory(true).CreateSession(Request(searchRoute)));
        try
        {
            await search.InitializeAsync(); window = Show(search.OriginalNativeView);
            Assert.Single(search.OriginalNativeView.GetVisualDescendants().OfType<HomeLibraryDestinationPage>());
            Assert.Equal(searchRoute, search.Address.Value);
        }
        finally { try { await search.CloseAndDrainAsync(); } finally { window?.Close(); } }
    }
    [AvaloniaFact]
    public async Task Folder_picker_cancel_completes_without_a_folder_or_name()
    {
        var page = Page(new Index([]), new(), (_, _) => true);
        var checks = 0;
        page.SetFolderSelectionMode((link, _) => { checks++; return Task.FromResult<HomeDeepLink?>(link); });
        try
        {
            await page.ActivateAsync(CancellationToken.None);
            await page.DispatchAsync("CancelBoardFolder", null);
            Assert.Null(await page.FolderSelection);
            Assert.Null(page.SelectedBoardCreateDisplayName);
            Assert.Equal(0, checks);
            Assert.False(page.IsActionAvailable("ConfirmBoardFolder"));
        }
        finally { await page.CloseAndDrainAsync(); }
    }

    private static HomeLibraryDestinationPage Page(Index index, Authorization authorization, Func<HomeDeepLink, Task, bool> queue,
        IHomeLibraryPreferences? preferences = null, CuiThemePreferenceContext? theme = null) =>
        new(router => new(index, authorization, router, preferences ?? new Preferences()), _ => Task.CompletedTask, queue,
            theme: theme);
    private static Button FindAuthoredButton(HomeLibraryDestinationPage page, string authoredId) =>
        Assert.Single(page.GetVisualDescendants().OfType<Button>(), item =>
            CuiRuntimeIdentity.GetStableId(item) == "id:" + authoredId);
    // CUI Repeat instances have a keyed runtime identity below the authored ID; querying
    // Control.Name loses that distinction and is not the repeated-item identity contract.
    private static bool HasAuthoredIdentity(Control control, string authoredId) =>
        CuiRuntimeIdentity.GetStableId(control) is { } identity &&
        identity.Contains("id:home-library-rows|key:", StringComparison.Ordinal) &&
        identity.EndsWith("/id:" + authoredId, StringComparison.Ordinal);
    private static Button FindLibraryButton(HomeLibraryDestinationPage page, string authoredId)
    {
        var visual = page.GetVisualDescendants().OfType<Control>().ToArray();
        var controls = visual.OfType<Button>().ToArray();
        var repeatTree = string.Join("; ", visual.Take(160).Where(control =>
                CuiRuntimeIdentity.GetStableId(control) is { } id &&
                (id.Contains("home-library-rows", StringComparison.Ordinal) || id.Contains("library-results", StringComparison.Ordinal)))
            .Select(control =>
            {
                var id = CuiRuntimeIdentity.GetStableId(control);
                var childCount = control is Panel panel ? panel.Children.Count : -1;
                return $"{control.GetType().Name}:stable='{id}',children={childCount}";
            }));
        var matches = controls.Where(item => HasAuthoredIdentity(item, authoredId)).ToArray();
        Assert.True(matches.Length == 1,
            $"Expected one {authoredId}; page rows={page.Current?.Items.Count ?? 0} " +
            $"artifact IDs=[{string.Join(",", page.Current?.Items.Select(item => item.ArtifactId) ?? [])}]; " +
            $"repeat source=[{RepeatSourceDiagnostic(page)}]; " +
            $"repeat tree=[{repeatTree}]; " +
            $"repeat child tree=[{RepeatChildTreeDiagnostic(page)}]; " +
            $"runtime diagnostics=[{RepeatRuntimeDiagnosticSummary(page)}]; " +
            $"buttons=[{string.Join("; ", controls.Take(80).Select(item =>
                $"stable='{CuiRuntimeIdentity.GetStableId(item)}' automation='{AutomationProperties.GetAutomationId(item)}' name='{item.Name}' content='{item.Content}'"))}]");
        return matches[0];
    }
    private static string RepeatChildTreeDiagnostic(HomeLibraryDestinationPage page)
    {
        var host = page.GetVisualDescendants().OfType<StackPanel>().FirstOrDefault(control =>
            CuiRuntimeIdentity.GetStableId(control) == "id:home-library-rows");
        if (host is null) return "repeat host is not in the visual tree";
        var visited = new HashSet<Control>(ReferenceEqualityComparer.Instance);
        var entries = new List<string>();
        void Visit(Control control, int depth)
        {
            if (entries.Count >= 40 || depth > 6 || !visited.Add(control)) return;
            Control[] children = control switch
            {
                CuiContainerControl container => container.AuthoredChildren.ToArray(),
                Panel panel => panel.Children.OfType<Control>().ToArray(),
                Decorator { Child: Control child } => [child],
                ContentControl { Content: Control child } => [child],
                ItemsControl items => items.Items.OfType<Control>().ToArray(),
                _ => Array.Empty<Control>()
            };
            entries.Add($"{new string(' ', depth * 2)}{control.GetType().Name}:stable='{CuiRuntimeIdentity.GetStableId(control)}',visible={control.IsVisible},bounds={control.Bounds},children={children.Length}");
            foreach (var child in children) Visit(child, depth + 1);
        }
        foreach (var child in host.Children.OfType<Control>().Take(4)) Visit(child, 0);
        return string.Join(" | ", entries);
    }
    private static string RepeatRuntimeDiagnosticSummary(HomeLibraryDestinationPage page)
    {
        try
        {
            const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.NonPublic;
            var loader = typeof(HomeLibraryDestinationPage).GetField("_loader", flags)?.GetValue(page);
            var diagnostics = loader?.GetType().GetProperty("RuntimeDiagnostics", flags)?.GetValue(loader)
                as System.Collections.IEnumerable;
            if (diagnostics is null) return "loader/runtime diagnostic accessor unavailable";
            return string.Join("; ", diagnostics.Cast<object>().Take(8).Select(diagnostic =>
            {
                string Read(string name) => diagnostic.GetType().GetProperty(name,
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public |
                    System.Reflection.BindingFlags.NonPublic)?.GetValue(diagnostic)?.ToString() ?? "<null>";
                var message = Read("Message");
                if (message.Length > 240) message = message[..240] + "…";
                return $"{Read("Code")}/{Read("Severity")}: {message}";
            }));
        }
        catch (Exception error)
        {
            return $"diagnostic extraction failed ({error.GetType().Name})";
        }
    }
    private static string RepeatSourceDiagnostic(HomeLibraryDestinationPage page)
    {
        if (!page.TryGetValue("LibraryRows", out var source)) return "binding-missing";
        if (source is not System.Collections.IEnumerable rows) return source?.GetType().FullName ?? "null";
        var items = rows.Cast<object>().Take(4).ToArray();
        return $"type={source.GetType().FullName},count={items.Length},items=[{string.Join(";", items.Select(item =>
        {
            var key = page.TryGetItemValue(item, "Key", out var resolvedKey) ? Convert.ToString(resolvedKey) : "<unresolved>";
            var name = page.TryGetItemValue(item, "DisplayName", out var displayName) ? Convert.ToString(displayName) : "<unresolved>";
            var target = page.TryGetItemValue(item, "Target", out var resolvedTarget) ? resolvedTarget : null;
            return $"key={key},name={name},target={target?.GetType().Name ?? "<unresolved>"}";
        }))}]";
    }
    private static Window Show(Control page)
    { var window = new Window { Width = 800, Height = 620, Content = page }; window.Show(); window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); return window; }
    private sealed class Registry : IModeRegistry
    {
        public Task<IReadOnlyList<ModeDefinition>> GetModesAsync(CancellationToken token) => Task.FromResult<IReadOnlyList<ModeDefinition>>([]);
        public Task<ModeDefinition?> GetModeByKeyAsync(string key, CancellationToken token) => Task.FromResult<ModeDefinition?>(null);
        public Task<ModeDefinition?> GetModeByIdAsync(Guid id, CancellationToken token) => Task.FromResult<ModeDefinition?>(null);
        public Task UpsertModeAsync(ModeDefinition mode, CancellationToken token) => throw new NotSupportedException();
        public Task<IReadOnlyList<ModeVersion>> GetVersionsAsync(Guid id, CancellationToken token) => throw new NotSupportedException();
        public Task AddVersionAsync(ModeVersion version, CancellationToken token) => throw new NotSupportedException();
        public Task<IReadOnlyList<ModePermissionGrant>> GetGrantsAsync(Guid id, CancellationToken token) => throw new NotSupportedException();
        public Task UpsertGrantAsync(ModePermissionGrant grant, CancellationToken token) => throw new NotSupportedException();
    }
    private sealed class BoardCreationHost : IBoardsHomeBoardCreationHost
    {
        internal int CreateCalls { get; private set; }
        internal int SettlementRetries { get; private set; }
        internal byte[] CreatedDraft { get; private set; } = [];
        public bool CanRetryOpenCreatedBoard { get; private set; }
        public bool CanRetryCreationSettlement { get; private set; }
        public bool CanInspectUnknownCreation { get; private set; }
        public bool CanOpenRecordedBoard { get; private set; }
        public bool HasUnresolvedCreation { get; private set; }

        public Task<BoardsHomeBoardCreationResult> CreateAndOpenAsync(ReadOnlyMemory<byte> validatedDraft,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CreateCalls++;
            CreatedDraft = validatedDraft.ToArray();
            CanRetryCreationSettlement = true;
            HasUnresolvedCreation = true;
            return Task.FromResult(new BoardsHomeBoardCreationResult(BoardsHomeBoardCreationOutcome.HomeAuditPending,
                "HOME_AUDIT_PENDING", "The create audit is still pending."));
        }

        public Task<BoardsHomeBoardCreationResult> RetryCreationSettlementAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SettlementRetries++;
            CanRetryCreationSettlement = false;
            HasUnresolvedCreation = false;
            return Task.FromResult(new BoardsHomeBoardCreationResult(BoardsHomeBoardCreationOutcome.Opened,
                "OPENED", "The Board is ready."));
        }

        public Task<BoardsHomeBoardCreationResult> RetryOpenCreatedBoardAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new BoardsHomeBoardCreationResult(BoardsHomeBoardCreationOutcome.CreatedOpenFailed));
        public Task<BoardsHomeBoardCreationResult> InspectUnknownCreationAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new BoardsHomeBoardCreationResult(BoardsHomeBoardCreationOutcome.OutcomeUnknown));
        public Task<BoardsHomeBoardCreationResult> OpenRecordedBoardAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new BoardsHomeBoardCreationResult(BoardsHomeBoardCreationOutcome.OpenedWithAuditWarning));
    }
    private sealed class Index : IHomeArtifactSearchIndex
    {
        internal readonly IReadOnlyList<HomeArtifactReference> Rows;
        internal HomeLibraryQuery? LastQuery;
        internal TaskCompletionSource<IReadOnlyList<HomeArtifactReference>>? Held;
        internal readonly TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal Index(int count) => Rows = Enumerable.Range(0, count).Select(number =>
        {
            var id = Guid.NewGuid().ToString("D");
            return new HomeArtifactReference("boards", id, $"Notebook {number:D2}", "Boards notebook", "Fixture library", "fixture-profile", null,
                DateTimeOffset.UnixEpoch.AddMinutes(-number), false, false, false, false, [], new(HomeRoute.Library, "boards", id, "open", "boards.notebook"));
        }).ToArray();
        internal Index(IReadOnlyList<HomeArtifactReference> rows) => Rows = rows;
        public async Task<IReadOnlyList<HomeArtifactReference>> SearchAsync(HomeLibraryQuery query, CancellationToken token)
        {
            LastQuery = query; Entered.TrySetResult();
            var rows = Held is { } held ? await held.Task : Rows;
            return rows.Where(row => query.Text is null || row.DisplayName.Contains(query.Text, StringComparison.OrdinalIgnoreCase)).ToArray();
        }
        public Task<HomeArtifactReference?> GetByIdAsync(string app, string id, CancellationToken token) =>
            Task.FromResult(Rows.SingleOrDefault(row => row.OwnerApp == app && row.ArtifactId == id));
    }
    private sealed class Authorization : IHomeArtifactAuthorization
    {
        internal bool Allowed = true; internal int Opens, Reads;
        public Task<bool> CanReadAsync(string app, string id, CancellationToken token) { Reads++; return Task.FromResult(Allowed); }
        public Task<bool> CanOpenAsync(HomeDeepLink link, CancellationToken token) { Opens++; return Task.FromResult(Allowed); }
    }
    private sealed class Preferences : IHomeLibraryPreferences
    {
        private readonly HashSet<string> _pinned = new(StringComparer.Ordinal);
        internal bool IsPinned(string app, string id) => _pinned.Contains(app + "/" + id);
        public Task<bool> IsPinnedAsync(string app, string id, CancellationToken token) => Task.FromResult(IsPinned(app, id));
        public Task SetPinnedAsync(string app, string id, bool pinned, CancellationToken token)
        { var key = app + "/" + id; if (pinned) _pinned.Add(key); else _pinned.Remove(key); return Task.CompletedTask; }
    }
    private sealed class ThemeSource(string name = "Glow", CuiAppearance appearance = CuiAppearance.Bright) : ICuiThemePreferenceSource
    {
        public CuiThemePreferenceSnapshot Current { get; private set; } =
            new(name, 0, "home-library-theme-test", false, CuiAccessibilitySettings.Default, appearance);
        public event EventHandler? Changed;
        internal void SetAppearance(CuiAppearance appearance)
        {
            Current = Current with { Revision = Current.Revision + 1, Appearance = appearance };
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }
}
