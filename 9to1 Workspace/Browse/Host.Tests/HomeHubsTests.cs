using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CakeOS.Cui;
using CakeOS.Cui.Runtime;
using CakeOS.Cui.Themes;
using Haven.Application;
using Haven.Browser;
using Haven.Core;
using Haven.Desktop.Services;
using Haven.Desktop.Views.Pages.Home;
using HavenOS.Apps.Browse;

namespace HavenOS.Browse.Host.Tests;

public sealed class HomeHubsTests
{
    private static readonly HomeAppItem[] Actual =
    [
        BuiltIn("spaces", "Spaces", 22), BuiltIn("terminal", "Terminal", 20), BuiltIn("dev", "Build", 28),
        new("code", "Code lookalike", "User app", "code", CanonicalId: Guid.Parse("b0000000-0000-0000-0000-000000000001"), Source: ModeSource.Created)
    ];

    [Fact]
    public void Seven_standard_hubs_keep_exact_identities_and_do_not_capture_custom_lookalikes()
    {
        var hubs = HomeAppHubCatalogue.StandardHubs(Actual);
        Assert.Equal(new[] { "coordinate", "create", "develop", "organise", "work", "experiment", "admin" }, hubs.Select(hub => hub.Key));
        var develop = Assert.Single(hubs, hub => hub.Key == "develop");
        Assert.Contains("reference:code", develop.AppIdentities);
        Assert.DoesNotContain(Actual[3].StableIdentity, develop.AppIdentities);
        Assert.Contains(Actual[3].StableIdentity, Assert.Single(hubs, hub => hub.Key == "experiment").AppIdentities);
        Assert.False(HomeAppHubCatalogue.ResolvePlacement(Actual, "reference:code").IsInstalled);
        Assert.Equal(Actual[3].CanonicalId, HomeAppHubCatalogue.ResolvePlacement(Actual, Actual[3].StableIdentity).CanonicalId);
        Assert.Equal("Connect", HomeAppHubCatalogue.ResolvePlacement(Actual, "reference:mail").Name);
        Assert.DoesNotContain(hubs.SelectMany(hub => hub.AppIdentities), identity => identity == "reference:prototype");
    }

    [Fact]
    public void Approved_unavailable_references_keep_artwork_without_becoming_installed_or_capturing_lookalikes()
    {
        var wave = HomeAppHubCatalogue.ResolvePlacement(Actual, "reference:wave");
        Assert.False(wave.IsInstalled); Assert.False(wave.IsAvailable); Assert.Null(wave.CanonicalId);
        Assert.Equal(ModeSource.BuiltIn, wave.Source);
        var bindings = new HomeAppsCuiBindings(() => HomeAppsViewState.Empty, (_, _, _) => ValueTask.CompletedTask);
        var placement = new HomeHubAppRow(wave, new HomeHubActionTarget(1, "create", "reference:wave"), 0, 0);
        Assert.True(bindings.TryGetItemValue(placement, "iconSource", out var source));
        Assert.Equal("ownerbrand:wave|wave", source);
        Assert.True(bindings.TryGetItemValue(placement, "canOpen", out var canOpen)); Assert.Equal(false, canOpen);
        var lookalike = wave with { Source = ModeSource.Created };
        Assert.True(bindings.TryGetItemValue(placement with { App = lookalike }, "iconSource", out source));
        Assert.Equal("icon:wave", source);
        var missingSavedApp = HomeAppHubCatalogue.ResolvePlacement(Actual, "mode:c0000000-0000-0000-0000-000000000001");
        Assert.Null(missingSavedApp.Source);
    }

    [Fact]
    public void Hub_projection_filters_installed_searches_members_and_keeps_missing_saved_references()
    {
        var hubs = HomeAppHubCatalogue.StandardHubs(Actual).ToArray();
        var index = Array.FindIndex(hubs, hub => hub.Key == "develop");
        hubs[index] = hubs[index] with { AppIdentities = hubs[index].AppIdentities.Append("mode:c0000000-0000-0000-0000-000000000001").ToArray() };
        var state = View(hubs);
        var installed = HomeAppHubCatalogue.ProjectHubs(Actual, state, HomeAppsFilter.Installed, [], 1100);
        Assert.Equal(7, installed.Navigation.Count);
        Assert.Equal(Actual[2].CanonicalId, installed.BuildProjects?.App.CanonicalId);
        Assert.Equal(Actual[1].CanonicalId, Assert.Single(installed.Apps).App.CanonicalId);
        var all = HomeAppHubCatalogue.ProjectHubs(Actual, state, HomeAppsFilter.All, [], 1100);
        Assert.Contains(all.Apps, app => app.Target.AppIdentity == "reference:code" && !app.App.IsInstalled);
        Assert.Contains(all.Apps, app => app.Target.AppIdentity!.StartsWith("mode:c000", StringComparison.Ordinal) && !app.App.IsAvailable);
        var searched = HomeAppHubCatalogue.ProjectHubs(Actual, state with { Query = " terminal " }, HomeAppsFilter.All, [], 600);
        Assert.Equal(Actual[1].CanonicalId, Assert.Single(searched.Apps).App.CanonicalId);
        Assert.Equal(1, searched.AppColumns);
        Assert.Equal(7, searched.Navigation.Count); // Search never deletes navigation hubs.
    }

    [Fact]
    public void All_apps_keeps_unplaced_installed_apps_when_the_custom_hubs_are_empty()
    {
        var all = HomeAppHubCatalogue.Project(Actual, [], string.Empty, HomeAppsFilter.Installed, false, 1000, arrangement: []);
        Assert.Equal(Actual.Length, all.VisibleCount);
        Assert.Equal("Other apps", Assert.Single(all.Hubs).Name);
        Assert.Equal(Actual.Select(app => app.StableIdentity).Order(), all.Hubs.SelectMany(hub => hub.Apps).Select(app => app.Identity).Order());
        var searched = HomeAppHubCatalogue.Project(Actual, [], "terminal", HomeAppsFilter.Installed, false, 1000, arrangement: []);
        Assert.Equal(Actual[1].CanonicalId, Assert.Single(Assert.Single(searched.Hubs).Apps).App.CanonicalId);
    }

    [Fact]
    public void Editing_preserves_hub_identity_cross_listed_apps_and_one_step_undo()
    {
        var layout = HomeHubEdits.Toggle(HomeAppHubLayout.Default, HomeAppHubCatalogue.StandardHubs(Actual));
        var develop = new HomeHubActionTarget(layout.Revision, "develop");
        layout = HomeHubEdits.Rename(layout, develop, "Workshop");
        Assert.Equal("develop", Assert.Single(layout.CustomHubs, hub => hub.Name == "Workshop").Key);
        layout = HomeHubEdits.MoveHub(layout, develop, -1);
        Assert.Equal("develop", layout.CustomHubs[1].Key);
        layout = HomeHubEdits.AddApp(layout, new(layout.Revision, "coordinate", Actual[1].StableIdentity));
        Assert.Equal(2, layout.CustomHubs.Count(hub => hub.AppIdentities.Contains(Actual[1].StableIdentity)));
        var before = JsonSerializer.Serialize(layout.CustomHubs);
        layout = HomeHubEdits.RemoveApp(layout, develop with { AppIdentity = Actual[1].StableIdentity });
        Assert.Equal(1, layout.CustomHubs.Count(hub => hub.AppIdentities.Contains(Actual[1].StableIdentity)));
        layout = HomeHubEdits.Undo(layout);
        Assert.Equal(before, JsonSerializer.Serialize(layout.CustomHubs));
        Assert.Null(layout.Undo);
        Assert.Throws<InvalidOperationException>(() => HomeHubEdits.Rename(layout, develop with { Revision = 99 }, "Stale"));
        Assert.Throws<InvalidOperationException>(() => HomeHubEdits.RemoveHub(layout with { Customise = false }, develop));
    }

    [Fact]
    public async Task Disabling_and_reenabling_preserves_an_intentionally_empty_arrangement_across_reload()
    {
        using var files = new Files();
        var prefs = files.Preferences();
        var snapshot = await prefs.ReadAsync(TestContext.Current.CancellationToken);
        snapshot = (await prefs.SaveAsync(snapshot, HomeHubEdits.Toggle(snapshot.Layout, HomeAppHubCatalogue.StandardHubs(Actual)), TestContext.Current.CancellationToken)).Current;
        foreach (var key in snapshot.Layout.CustomHubs.Select(hub => hub.Key).ToArray())
            snapshot = (await prefs.SaveAsync(snapshot, HomeHubEdits.RemoveHub(snapshot.Layout, new(snapshot.Layout.Revision, key)), TestContext.Current.CancellationToken)).Current;
        Assert.Empty(snapshot.Layout.CustomHubs);
        Assert.True(snapshot.Layout.CustomLayoutInitialised);
        var undo = JsonSerializer.Serialize(snapshot.Layout.Undo);
        snapshot = (await prefs.SaveAsync(snapshot, HomeHubEdits.Toggle(snapshot.Layout, HomeAppHubCatalogue.StandardHubs(Actual)), TestContext.Current.CancellationToken)).Current;
        Assert.False(snapshot.Layout.Customise);
        var reloaded = await files.Preferences().ReadAsync(TestContext.Current.CancellationToken);
        Assert.Empty(reloaded.Layout.CustomHubs);
        var enabled = HomeHubEdits.Toggle(reloaded.Layout, HomeAppHubCatalogue.StandardHubs(Actual));
        Assert.True(enabled.Customise);
        Assert.Empty(enabled.CustomHubs);
        Assert.Equal(undo, JsonSerializer.Serialize(enabled.Undo));
        Assert.Single(HomeHubEdits.Undo(enabled).CustomHubs);
    }

    [Fact]
    public async Task Conflicting_tabs_profile_revocation_and_cancellation_cannot_overwrite_the_saved_arrangement()
    {
        using var files = new Files();
        var admitted = true;
        Task Admit(CancellationToken token)
        { token.ThrowIfCancellationRequested(); return admitted ? Task.CompletedTask : Task.FromException(new UnauthorizedAccessException("Profile replaced.")); }
        var first = files.Preferences(admit: Admit);
        var second = files.Preferences();
        var beforeA = await first.ReadAsync(TestContext.Current.CancellationToken);
        var beforeB = await second.ReadAsync(TestContext.Current.CancellationToken);
        var saved = await first.SaveAsync(beforeA, HomeHubEdits.Toggle(beforeA.Layout, HomeAppHubCatalogue.StandardHubs(Actual)), TestContext.Current.CancellationToken);
        Assert.True(saved.Saved);
        var conflict = await second.SaveAsync(beforeB, HomeHubEdits.Toggle(beforeB.Layout, []), TestContext.Current.CancellationToken);
        Assert.False(conflict.Saved);
        Assert.Equal(saved.Current.Serialized, conflict.Current.Serialized);
        admitted = false;
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => first.SaveAsync(saved.Current,
            HomeHubEdits.RemoveHub(saved.Current.Layout, new(saved.Current.Layout.Revision, "develop")), TestContext.Current.CancellationToken));
        Assert.Equal(saved.Current.Serialized, (await second.ReadAsync(TestContext.Current.CancellationToken)).Serialized);
        using var cancelled = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken); cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => second.SaveAsync(saved.Current,
            HomeHubEdits.Toggle(saved.Current.Layout, []), cancelled.Token));
        Assert.Equal(saved.Current.Serialized, (await second.ReadAsync(TestContext.Current.CancellationToken)).Serialized);
        Assert.Equal(HomeAppHubLayout.Default, (await files.Preferences("different-profile").ReadAsync(TestContext.Current.CancellationToken)).Layout);
    }

    [Fact]
    public async Task Unsupported_or_corrupt_arrangements_are_preserved_without_resetting_the_settings_key()
    {
        using var files = new Files();
        var store = files.Store();
        var prefs = new HomeAppHubPreferences(store, "profile-a", _ => Task.CompletedTask);
        var snapshot = await prefs.ReadAsync(TestContext.Current.CancellationToken);
        await prefs.SaveAsync(snapshot, HomeHubEdits.Toggle(snapshot.Layout, HomeAppHubCatalogue.StandardHubs(Actual)), TestContext.Current.CancellationToken);
        var key = Assert.Single((await store.ExportAsync(TestContext.Current.CancellationToken)).Settings.Keys);
        await store.SetAsync(key, new { SchemaVersion = 999, Revision = 4, Customise = true, CustomHubs = Array.Empty<HomeAppHub>() }, TestContext.Current.CancellationToken);
        var before = (await store.ExportAsync(TestContext.Current.CancellationToken)).Settings[key];
        await Assert.ThrowsAsync<InvalidDataException>(() => prefs.ReadAsync(TestContext.Current.CancellationToken));
        Assert.Equal(before, (await store.ExportAsync(TestContext.Current.CancellationToken)).Settings[key]);
    }

    [Fact]
    public void Viewport_padding_uses_host_width_and_theme_geometry_uses_the_scoped_contract()
    {
        var binding = new HomeAppsCuiBindings(() => HomeAppsViewState.Empty, (_, _, _) => ValueTask.CompletedTask);
        binding.SetViewportWidth(650, 750);
        AssertValue(binding, "apps.pagePadding", "16 20 16 40");
        binding.SetViewportWidth(457, 751);
        AssertValue(binding, "apps.pagePadding", "30 28 30 52");
        binding.SetThemeName("Glow");
        AssertValue(binding, "apps.cardRadius", new CornerRadius(22));
        AssertValue(binding, "apps.rowRadius", new CornerRadius(999));
        AssertValue(binding, "apps.buttonRadius", new CornerRadius(40));
        binding.SetThemeName("Professional");
        AssertValue(binding, "apps.cardRadius", new CornerRadius(16));
        AssertValue(binding, "apps.rowRadius", new CornerRadius(11));
        AssertValue(binding, "apps.buttonRadius", new CornerRadius(24));
        binding.SetThemeName("Custom profile theme", new CornerRadius(5, 8, 12, 6), new CornerRadius(3));
        AssertValue(binding, "apps.cardRadius", new CornerRadius(5, 8, 12, 6));
        AssertValue(binding, "apps.rowRadius", new CornerRadius(3));
    }

    [AvaloniaFact]
    public async Task Real_editor_confirms_exact_revision_and_preserves_a_concurrent_tab_change()
    {
        using var files = new Files();
        var prefs = files.Preferences();
        var seed = await prefs.ReadAsync(TestContext.Current.CancellationToken);
        var current = (await prefs.SaveAsync(seed, HomeHubEdits.Toggle(seed.Layout, HomeAppHubCatalogue.StandardHubs(Actual)), TestContext.Current.CancellationToken)).Current;
        var page = Page("browse://home/hubs/customise", prefs);
        var window = new Window { Content = page, Width = 1200, Height = 800 };
        try
        {
            window.Show(); await page.InitializeAsync(TestContext.Current.CancellationToken); window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
            var target = new HomeHubActionTarget(current.Layout.Revision, "develop");
            await page.DispatchAsync("home.hub-remove", target);
            Assert.Equal(current.Serialized, (await prefs.ReadAsync(TestContext.Current.CancellationToken)).Serialized);
            Assert.Equal("Remove hub", Find<Button>(page, "home-hub-confirm").Content);
            var other = files.Preferences();
            var replacement = await other.SaveAsync(current, HomeHubEdits.Rename(current.Layout, target, "Shared workshop"), TestContext.Current.CancellationToken);
            Assert.True(replacement.Saved);
            await page.DispatchAsync("home.hub-confirm", null);
            Assert.Equal(replacement.Current.Serialized, (await prefs.ReadAsync(TestContext.Current.CancellationToken)).Serialized);
            Assert.Contains("another tab", Find<TextBlock>(page, "home-hubs-status").Text);
            var fresh = new HomeHubActionTarget(replacement.Current.Layout.Revision, "develop");
            await page.DispatchAsync("home.hub-remove", fresh);
            await page.DispatchAsync("home.hub-cancel-confirm", null);
            Assert.Equal(replacement.Current.Serialized, (await prefs.ReadAsync(TestContext.Current.CancellationToken)).Serialized);
            await page.DispatchAsync("home.hub-remove", fresh);
            await page.DispatchAsync("home.hub-confirm", null);
            Assert.DoesNotContain((await prefs.ReadAsync(TestContext.Current.CancellationToken)).Layout.CustomHubs, hub => hub.Key == "develop");
            await page.DispatchAsync("home.hub-undo", null);
            Assert.Equal("Shared workshop", Assert.Single((await prefs.ReadAsync(TestContext.Current.CancellationToken)).Layout.CustomHubs, hub => hub.Key == "develop").Name);
        }
        finally { await page.CloseAndDrainAsync(); window.Close(); }
    }

    [AvaloniaFact]
    public async Task Actual_hub_inputs_submit_one_rename_and_one_add_through_the_original_storage_owner()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using var files = new Files();
        var prefs = files.Preferences();
        var seed = await prefs.ReadAsync(timeout.Token);
        var initial = (await prefs.SaveAsync(seed,
            HomeHubEdits.Toggle(seed.Layout, HomeAppHubCatalogue.StandardHubs(Actual)), timeout.Token)).Current;
        var page = Page("browse://home/hubs/customise", prefs);
        var window = new Window { Content = page, Width = 1200, Height = 900 };
        try
        {
            window.Show(); await page.InitializeAsync(timeout.Token);
            window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            var name = Assert.Single(page.GetVisualDescendants().OfType<TextBox>(), input =>
                input.Name == "home-hub-name" && input.DataContext is ICuiBindingContext row &&
                row.TryGetValue("hub.target", out var value) && value is HomeHubActionTarget { HubKey: "develop" });
            name.Text = "My workshop";
            Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            name.BringIntoView(); Assert.True(name.Focus());
            window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
            window.KeyReleaseQwerty(PhysicalKey.Enter, RawInputModifiers.None);
            await page.WhenActionsIdleAsync().WaitAsync(timeout.Token);
            var renamed = await prefs.ReadAsync(timeout.Token);
            Assert.Equal(initial.Layout.Revision + 1, renamed.Layout.Revision);
            Assert.Equal("My workshop", Assert.Single(renamed.Layout.CustomHubs, hub => hub.Key == "develop").Name);
            Assert.Equal(initial.Layout.CustomHubs.Count, renamed.Layout.CustomHubs.Count);

            var add = Find<TextBox>(page, "home-hub-new-name");
            add.Text = "Research";
            Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            add.BringIntoView(); Assert.True(add.Focus());
            window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
            window.KeyReleaseQwerty(PhysicalKey.Enter, RawInputModifiers.None);
            await page.WhenActionsIdleAsync().WaitAsync(timeout.Token);
            var added = await prefs.ReadAsync(timeout.Token);
            Assert.Equal(renamed.Layout.Revision + 1, added.Layout.Revision);
            Assert.Equal(renamed.Layout.CustomHubs.Count + 1, added.Layout.CustomHubs.Count);
            Assert.Single(added.Layout.CustomHubs, hub => hub.Name == "Research");
            Assert.Equal(string.Empty, Find<TextBox>(page, "home-hub-new-name").Text);
        }
        finally { await page.CloseAndDrainAsync(); window.Content = null; window.Close(); }
    }

    [AvaloniaFact]
    public async Task Authored_hub_actions_keep_typed_placements_and_queue_canonical_routes_without_constructing_other_apps()
    {
        using var files = new Files();
        var queued = new List<BrowseNavigationIntent>();
        var page = Page("browse://home/hubs", files.Preferences(), (intent, _) => { queued.Add(intent); return true; });
        var window = new Window { Content = page, Width = 1440, Height = 900 };
        try
        {
            window.Show(); await page.InitializeAsync(TestContext.Current.CancellationToken); window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
            Assert.Equal(7, page.GetVisualDescendants().OfType<Button>().Count(button => button.Name == "home-hub-card"));
            Find<Button>(page, "home-hub-open-projects").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await page.WhenActionsIdleAsync();
            Assert.Equal(AppContentAddress.Parse("9to1://dev"), Assert.Single(queued).Address);
            Assert.False(Assert.Single(queued).OpenInNewTab);
            queued.Clear();
            Find<Button>(page, "home-hubs-customise").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await page.WhenActionsIdleAsync();
            var customiseAddress = Assert.Single(queued).Address;
            Assert.Equal(AppContentAddress.Parse("browse://home/hubs/customise"), customiseAddress);
            Assert.False(Assert.Single(queued).OpenInNewTab);
            queued.Clear();
            Find<Button>(page, "home-hubs-all-apps").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await page.WhenActionsIdleAsync();
            var appsAddress = Assert.Single(queued).Address;
            Assert.Equal(AppContentAddress.Parse("browse://home"), appsAddress);
            Assert.False(Assert.Single(queued).OpenInNewTab);

            // Validate emitted Home destinations against the real composition owner, not only the queue.
            using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            var factory = new UnifiedHomeContentFactory(new Registry(),
                () => throw new InvalidOperationException("Route admission must not construct Dashboard."),
                () => throw new InvalidOperationException("Route admission must not construct Models."),
                (_, _) => true, (_, _, _) => throw new InvalidOperationException("Route admission must not start inference."),
                (_, _) => Task.CompletedTask, () => true, lifetime.Token, appHubs: _ => files.Preferences());
            foreach (var address in new[] { appsAddress, customiseAddress })
            {
                var session = factory.CreateSession(new(Guid.NewGuid(), address, "profile-a", BrowserTabPrivacy.Standard));
                await session.CloseAndDrainAsync();
            }
            Assert.Throws<NotSupportedException>(() => factory.CreateSession(new(Guid.NewGuid(),
                AppContentAddress.Parse("browse://home/apps"), "profile-a", BrowserTabPrivacy.Standard)));
        }
        finally { await page.CloseAndDrainAsync(); window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData("Glow", false)]
    [InlineData("Professional", false)]
    [InlineData("Glow", true)]
    [InlineData("Professional", true)]
    public async Task Authored_hubs_and_customisation_render_at_desktop_and_narrow_sizes(string themeName, bool editing)
    {
        using var files = new Files();
        var prefs = files.Preferences();
        var initial = await prefs.ReadAsync(TestContext.Current.CancellationToken);
        await prefs.SaveAsync(initial, HomeHubEdits.Toggle(initial.Layout, HomeAppHubCatalogue.StandardHubs(Actual)), TestContext.Current.CancellationToken);
        using var theme = new CuiThemePreferenceContext(new ThemeSource(themeName));
        var page = Page(editing ? "browse://home/hubs/customise" : "browse://home/hubs", prefs, theme: theme);
        var window = new Window { Content = page, Width = 1440, Height = 900 };
        try
        {
            window.Show(); await page.InitializeAsync(TestContext.Current.CancellationToken);
            foreach (var size in new[] { new Size(1440, 900), new Size(720, 650) })
            {
                window.Width = size.Width; window.Height = size.Height;
                window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
                var body = Find<CuiContainerControl>(page, "home-hubs-body");
                Assert.True(body.Bounds.Width > 0);
                Assert.Equal(editing, Find<CuiContainerControl>(page, "home-hubs-editor").IsVisible);
                Assert.Equal(!editing, Find<CuiContainerControl>(page, "home-hubs-browse").IsVisible);
                if (!editing)
                {
                    Assert.All(body.GetVisualDescendants().OfType<Button>().Where(button => button.Name == "home-hub-card"),
                        button => Assert.Equal(new CornerRadius(themeName == "Glow" ? 999 : 11), button.CornerRadius));
                    AssertHubFilterInk(page, "installed");
                    Find<Button>(page, "home-hub-filter-all").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    await page.WhenActionsIdleAsync();
                    window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
                    AssertHubFilterInk(page, "all");
                    Find<Button>(page, "home-hub-filter-installed").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    await page.WhenActionsIdleAsync();
                    window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
                    AssertHubFilterInk(page, "installed");
                }
                foreach (var button in body.GetVisualDescendants().OfType<Button>().Where(button => button.IsEffectivelyVisible))
                {
                    var origin = button.TranslatePoint(default, body);
                    if (origin is null) continue;
                    Assert.True(origin.Value.X >= -1 && origin.Value.X + button.Bounds.Width <= body.Bounds.Width + 1,
                        $"{button.Name} overflows the hub page at {size.Width} pixels.");
                }
                if (Environment.GetEnvironmentVariable("CUI_EVIDENCE_DIRECTORY") is { Length: > 0 } directory)
                {
                    Directory.CreateDirectory(directory);
                    using var capture = window.CaptureRenderedFrame();
                    Assert.NotNull(capture);
                    capture.Save(Path.Combine(directory, $"home-hubs-{(editing ? "customise" : "browse")}-{themeName}-{size.Width:0}x{size.Height:0}.png"));
                }
            }
        }
        finally { await page.CloseAndDrainAsync(); window.Close(); }
    }

    private static HomeAppItem BuiltIn(string key, string name, int number) => new(key, name, name + " app", key,
        CanonicalId: Guid.Parse("a0000000-0000-0000-0000-" + number.ToString("D12")), Source: ModeSource.BuiltIn);

    private static HomeHubsViewState View(IReadOnlyList<HomeAppHub> hubs) => new(1, false, false, true, hubs,
        "develop", string.Empty, string.Empty, new Dictionary<string, string>(), false, string.Empty);

    private static void AssertValue(HomeAppsCuiBindings bindings, string path, object expected)
    { Assert.True(bindings.TryGetValue(path, out var actual)); Assert.Equal(expected, actual); }

    private static T Find<T>(Control root, string name) where T : Control =>
        Assert.Single(global::Haven.Desktop.Tests.AuthoredCuiControls.Enumerate(root).OfType<T>(), control => control.Name == name);

    private static void AssertHubFilterInk(Control page, string selected)
    {
        foreach (var key in new[] { "installed", "all", "pinned" })
        {
            var button = Find<Button>(page, "home-hub-filter-" + key);
            Assert.True(button.TryFindResource(key == selected ? "CuiAccentPrimaryForegroundBrush" : "CuiTextBrush", out var expected));
            var expectedColor = Assert.IsAssignableFrom<ISolidColorBrush>(expected).Color;
            Assert.Equal(expectedColor, Assert.IsAssignableFrom<ISolidColorBrush>(button.Foreground).Color);
            var label = Assert.Single(button.GetVisualDescendants().OfType<TextBlock>(), text => Equals(text.Text, button.Content));
            Assert.Equal(expectedColor, Assert.IsAssignableFrom<ISolidColorBrush>(label.Foreground).Color);
        }
    }

    private static UnifiedHomePage Page(string address, HomeAppHubPreferences preferences,
        Func<BrowseNavigationIntent, Task, bool>? queue = null, CuiThemePreferenceContext? theme = null) =>
        new(new(Guid.NewGuid(), AppContentAddress.Parse(address), "profile-a", BrowserTabPrivacy.Standard), new Registry(),
            () => throw new InvalidOperationException("Hubs must not construct Dashboard."),
            () => throw new InvalidOperationException("Hubs must not construct model management."),
            queue ?? ((_, _) => true), (_, _) => throw new InvalidOperationException("Hubs must not start inference."),
            aiAvailable: false, shortcutAppAvailable: _ => true, interfaceTheme: theme, appHubs: preferences,
            admitAppChange: _ => Task.CompletedTask);

    private sealed class ThemeSource(string name) : ICuiThemePreferenceSource
    {
        public CuiThemePreferenceSnapshot Current { get; } = new(name, 1, "test-hubs", true, CuiAccessibilitySettings.Default, CuiAppearance.Dark);
        public event EventHandler? Changed { add { } remove { } }
    }

    private sealed class Files : IAppPaths, IDisposable
    {
        public string DataDirectory { get; } = Path.Combine(Path.GetTempPath(), "9to1-hubs-test-" + Guid.NewGuid().ToString("N"));
        public string DatabasePath => Path.Combine(DataDirectory, "unused.db");
        public string BrowserProfileDirectory => Path.Combine(DataDirectory, "unused-browser");
        public string AttachmentsDirectory => Path.Combine(DataDirectory, "unused-attachments");
        public string LogsDirectory => Path.Combine(DataDirectory, "unused-logs");
        public string LegacyStatePath => Path.Combine(DataDirectory, "unused-legacy");
        public VersionedAtomicSettingsStore Store() => new(this);
        public HomeAppHubPreferences Preferences(string profile = "profile-a", Func<CancellationToken, Task>? admit = null) =>
            new(Store(), profile, admit ?? (_ => Task.CompletedTask));
        public void Dispose() { if (Directory.Exists(DataDirectory)) Directory.Delete(DataDirectory, true); }
    }

    private sealed class Registry : IModeRegistry
    {
        public Task<IReadOnlyList<ModeDefinition>> GetModesAsync(CancellationToken token) => Task.FromResult(BuiltInModeSeed.Modes);
        public Task<ModeDefinition?> GetModeByIdAsync(Guid id, CancellationToken token) => Task.FromResult(BuiltInModeSeed.Modes.SingleOrDefault(mode => mode.Id == id));
        public Task<ModeDefinition?> GetModeByKeyAsync(string key, CancellationToken token) => Task.FromResult(BuiltInModeSeed.Modes.SingleOrDefault(mode => mode.Key == key));
        public Task UpsertModeAsync(ModeDefinition mode, CancellationToken token) => throw new NotSupportedException();
        public Task<IReadOnlyList<ModeVersion>> GetVersionsAsync(Guid id, CancellationToken token) => throw new NotSupportedException();
        public Task AddVersionAsync(ModeVersion version, CancellationToken token) => throw new NotSupportedException();
        public Task<IReadOnlyList<ModePermissionGrant>> GetGrantsAsync(Guid id, CancellationToken token) => throw new NotSupportedException();
        public Task UpsertGrantAsync(ModePermissionGrant grant, CancellationToken token) => throw new NotSupportedException();
    }
}
