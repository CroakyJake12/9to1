using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.VisualTree;
using CakeOS.Cui.Runtime;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.Services;
using Haven.Desktop.Views.Pages.Home;
using HavenOS.Apps.Browse;

namespace Haven.Desktop.Tests;

public sealed partial class UnifiedHomeContentTests
{
    [AvaloniaTheory]
    [InlineData(1440, 900)]
    [InlineData(720, 520)]
    public async Task New_Tab_shortcuts_add_validate_open_rename_reorder_and_remove_with_the_native_controls(int width, int height)
    {
        var paths = new LayoutPaths();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(35));
        var token = timeout.Token;
        var request = Request("browse://newtab");
        var store = new VersionedAtomicSettingsStore(paths);
        var preferences = new NewTabLayoutPreferences(store, request.ProfileId, _ => Task.CompletedTask);
        var receipts = new List<BrowseNavigationIntent>();
        var page = Create(preferences);
        var window = new Window { Width = width, Height = height, Content = page };
        const string dashboardKey = "dashboard.widget-layouts.v1";
        try
        {
            await store.SetAsync(dashboardKey, "Dashboard untouched", token);
            var before = (await store.ExportAsync(token)).Settings[dashboardKey];
            await page.InitializeAsync(token);
            window.Show(); SettleFixtureLayout(window);
            Assert.Equal(new[] { "Apps", "Library", "Dashboard" }, Tiles().Select(AutomationProperties.GetName));

            await Activate("home-newtab-shortcut-add");
            await Activate("home-newtab-shortcut-picker-custom");
            Target().Text = "file:///tmp/unsupported";
            Name().Text = "Blocked";
            await Activate("home-newtab-shortcut-save");
            Assert.Contains("complete http or https address", SettingsControl<TextBlock>(page, "home-newtab-shortcut-status").Text);
            Assert.Null((await preferences.ReadAsync(token)).Serialized);
            await Activate("home-newtab-shortcut-cancel");
            Assert.True(SettingsControl<Button>(page, "home-newtab-shortcut-picker-custom").IsFocused);
            PressSettingsKey(window, PhysicalKey.Escape);
            SettleFixtureLayout(window);
            Assert.True(SettingsControl<Button>(page, "home-newtab-shortcut-add").IsFocused);
            Assert.Null((await preferences.ReadAsync(token)).Serialized);

            await Activate("home-newtab-shortcut-add");
            await Activate("home-newtab-shortcut-picker-custom");
            Name().Text = "Docs";
            Target().Text = "https://example.test/docs";
            await Activate("home-newtab-shortcut-save");
            var stored = (await preferences.ReadAsync(token)).Layout;
            var added = Assert.Single(stored.Shortcuts, shortcut => shortcut.Label == "Docs");
            Assert.Equal(1, stored.Revision);
            await Activate("home-newtab-shortcut", "Docs");
            Assert.Equal(new BrowseNavigationIntent(request.TabId, AppContentAddress.Parse(added.Target), false), Assert.Single(receipts));

            await Activate("home-newtab-customise-open");
            await Activate("home-newtab-shortcut-edit", "Edit Apps");
            Assert.False(SettingsControl<Button>(page, "home-newtab-shortcut-move-up").IsEnabled);
            await Activate("home-newtab-shortcut-cancel");
            await Activate("home-newtab-shortcut-edit", "Edit Docs");
            Name().Text = "Reference";
            await Activate("home-newtab-shortcut-save");
            await Activate("home-newtab-shortcut-edit", "Edit Reference");
            await Activate("home-newtab-shortcut-move-up");
            await Activate("home-newtab-shortcut-cancel");
            var moved = Tile("Reference");
            Assert.True(moved.Focus(NavigationMethod.Tab));
            window.KeyPressQwerty(PhysicalKey.ArrowUp, RawInputModifiers.Alt);
            window.KeyReleaseQwerty(PhysicalKey.ArrowUp, RawInputModifiers.Alt);
            await page.WhenActionsIdleAsync().WaitAsync(token);
            SettleFixtureLayout(window);
            Assert.True(Tile("Reference").IsFocused);
            var revised = (await preferences.ReadAsync(token)).Layout;
            Assert.Equal(new[] { "Apps", "Reference", "Library", "Dashboard" }, revised.Shortcuts.Select(shortcut => shortcut.Label));
            Assert.Equal(added with { Label = "Reference" }, revised.Shortcuts[1]);
            Assert.Equal(4, revised.Revision);

            await page.CloseAndDrainAsync();
            var reopened = new NewTabLayoutPreferences(new VersionedAtomicSettingsStore(paths), request.ProfileId, _ => Task.CompletedTask);
            page = Create(reopened);
            window.Content = page;
            await page.InitializeAsync(token);
            SettleFixtureLayout(window);
            Assert.Equal(revised.Shortcuts.Select(shortcut => shortcut.Label), Tiles().Select(AutomationProperties.GetName));
            await Activate("home-newtab-customise-open");
            await Activate("home-newtab-reset");
            await Activate("home-newtab-reset-confirm");
            var reset = (await reopened.ReadAsync(token)).Layout;
            Assert.Equal(new[] { "Apps", "Library", "Dashboard", "Reference" }, reset.Shortcuts.Select(shortcut => shortcut.Label));
            Assert.Equal(added with { Label = "Reference" }, reset.Shortcuts[^1]);
            await Activate("home-newtab-shortcut-edit", "Edit Reference");
            await Activate("home-newtab-shortcut-remove");
            Assert.Equal(NewTabShortcutPlacement.Defaults.AsEnumerable(), (await reopened.ReadAsync(token)).Layout.Shortcuts.AsEnumerable());
            Assert.Equal(before, (await store.ExportAsync(token)).Settings[dashboardKey]);
        }
        finally
        {
            try { await page.CloseAndDrainAsync(); }
            finally { window.Close(); Directory.Delete(paths.DataDirectory, recursive: true); }
        }

        UnifiedHomePage Create(NewTabLayoutPreferences owner) => new(request, new Registry(),
            () => throw new InvalidOperationException("Editing Shortcuts must not construct Dashboard."),
            () => throw new InvalidOperationException("Editing Shortcuts must not construct model discovery."),
            (intent, _) => { receipts.Add(intent); return true; },
            (_, _) => throw new InvalidOperationException("Editing Shortcuts must not acquire a model."), aiAvailable: false,
            createLibrary: () => throw new InvalidOperationException("Editing Shortcuts must not acquire Library."),
            newTabLayout: owner);
        TextBox Name() => SettingsControl<TextBox>(page, "home-newtab-shortcut-name");
        TextBox Target() => SettingsControl<TextBox>(page, "home-newtab-shortcut-target");
        Button Tile(string name) => Assert.Single(Tiles(), button => AutomationProperties.GetName(button) == name);
        IEnumerable<Button> Tiles() => page.GetVisualDescendants().OfType<Button>()
            .Where(button => button.Name == "home-newtab-shortcut" && button.IsEffectivelyVisible);

        async Task Activate(string id, string? name = null)
        {
            var button = Assert.Single(page.GetVisualDescendants().OfType<Button>(), control =>
                control.Name == id && control.IsEffectivelyVisible && (name is null || AutomationProperties.GetName(control) == name));
            Assert.True(button.IsEnabled && button.Focus(NavigationMethod.Tab), $"Shortcut control {id} was unavailable.");
            PressSettingsKey(window, PhysicalKey.Enter);
            await page.WhenActionsIdleAsync().WaitAsync(token);
            SettleFixtureLayout(window);
        }
    }

    [AvaloniaTheory]
    [InlineData(1440, 900, 3)]
    [InlineData(720, 520, 2)]
    public async Task New_Tab_shortcuts_picker_centres_filters_admitted_destinations_and_pins_once(int width, int height, int columns)
    {
        var paths = new LayoutPaths();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var token = timeout.Token;
        var request = Request("browse://newtab");
        var preferences = new NewTabLayoutPreferences(new VersionedAtomicSettingsStore(paths), request.ProfileId,
            _ => Task.CompletedTask);
        var page = new UnifiedHomePage(request, new Registry(),
            () => throw new InvalidOperationException("Adding a shortcut must not construct Dashboard."),
            () => throw new InvalidOperationException("Adding a shortcut must not construct model discovery."),
            (_, _) => throw new InvalidOperationException("Adding a shortcut must not navigate."),
            (_, _) => throw new InvalidOperationException("Adding a shortcut must not acquire a model."),
            createLibrary: () => throw new InvalidOperationException("Adding a shortcut must not construct Library."),
            newTabLayout: preferences);
        var window = new Window { Width = width, Height = height, Content = page };
        try
        {
            await page.InitializeAsync(token);
            window.Show(); SettleFixtureLayout(window);
            var add = SettingsControl<Button>(page, "home-newtab-shortcut-add");
            await Activate(add);
            var search = SettingsControl<TextBox>(page, "home-newtab-shortcut-picker-search");
            var picker = SettingsControl<CuiContainerControl>(page, "home-newtab-shortcut-picker");
            Assert.True(search.IsFocused);
            var center = picker.TranslatePoint(new Rect(picker.Bounds.Size).Center, page)!.Value;
            Assert.InRange(center.X, page.Bounds.Width / 2 - 1, page.Bounds.Width / 2 + 1);
            Assert.InRange(center.Y, page.Bounds.Height / 2 - 1, page.Bounds.Height / 2 + 1);
            Assert.Equal(columns, Candidates().GroupBy(button => Math.Round(button.TranslatePoint(default, picker)!.Value.Y))
                .OrderBy(group => group.Key).First().Count());
            var scrollHost = SettingsControl<CuiContainerControl>(picker, "home-newtab-shortcut-picker-scroll");
            Assert.Single(scrollHost.GetVisualDescendants().OfType<ScrollViewer>());
            Assert.False(Candidate("Apps, already added").IsEnabled);
            Assert.False(Candidate("Library, already added").IsEnabled);
            Assert.False(Candidate("Dashboard, already added").IsEnabled);
            Assert.DoesNotContain(Candidates(), button => AutomationProperties.GetName(button) is "Add Events" or "Add Settings");
            Assert.Null((await preferences.ReadAsync(token)).Serialized);

            search.Text = "unavailable query";
            SettleFixtureLayout(window);
            Assert.Empty(Candidates());
            Assert.True(SettingsControl<Button>(picker, "home-newtab-shortcut-picker-custom").IsEnabled);
            search.Text = "mOdElS";
            SettleFixtureLayout(window);
            var model = Assert.Single(Candidates());
            Assert.Equal("Add Models / Discover", AutomationProperties.GetName(model));
            await Activate(model, pointer: true);
            Assert.True(search.IsFocused);
            Assert.False(Candidate("Models / Discover, already added").IsEnabled);
            var layout = (await preferences.ReadAsync(token)).Layout;
            Assert.Equal(1, layout.Revision);
            Assert.Single(layout.Shortcuts, placement => placement.Target == NewTabShortcutPlacement.Models);

            var custom = SettingsControl<Button>(picker, "home-newtab-shortcut-picker-custom");
            await Activate(custom);
            Assert.True(SettingsControl<TextBox>(page, "home-newtab-shortcut-name").IsFocused);
            PressSettingsKey(window, PhysicalKey.Escape);
            SettleFixtureLayout(window);
            Assert.True(custom.IsFocused);
            Assert.Equal("mOdElS", search.Text);
            await Activate(SettingsControl<Button>(picker, "home-newtab-shortcut-picker-close"));
            Assert.True(add.IsFocused);
            Assert.Equal(1, (await preferences.ReadAsync(token)).Layout.Revision);
        }
        finally
        {
            try { await page.CloseAndDrainAsync(); }
            finally { window.Close(); Directory.Delete(paths.DataDirectory, recursive: true); }
        }

        IEnumerable<Button> Candidates() => page.GetVisualDescendants().OfType<Button>()
            .Where(button => button.Name == "home-newtab-shortcut-picker-candidate" && button.IsEffectivelyVisible);
        Button Candidate(string name) => Assert.Single(Candidates(), button => AutomationProperties.GetName(button) == name);

        async Task Activate(Button button, bool pointer = false)
        {
            Assert.True(button.IsEnabled && button.Focus(NavigationMethod.Tab));
            if (pointer && !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("HAVEN_VISUAL_CAPTURE_DIR")))
                ClickSettingsButton(window, button);
            else PressSettingsKey(window, PhysicalKey.Enter);
            await page.WhenActionsIdleAsync().WaitAsync(token);
            SettleFixtureLayout(window);
        }
    }

    [AvaloniaFact]
    public async Task New_Tab_shortcuts_admit_registered_app_identity_and_keep_the_pin_when_unavailable()
    {
        var paths = new LayoutPaths();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(40));
        var token = timeout.Token;
        var request = Request("browse://newtab");
        var preferences = new NewTabLayoutPreferences(new VersionedAtomicSettingsStore(paths), request.ProfileId, _ => Task.CompletedTask);
        var boards = BuiltInModeSeed.Modes.Single(mode => mode.Key == "boards");
        var admitted = true;
        var navigations = new List<BrowseNavigationIntent>();
        var page = new UnifiedHomePage(request, new Registry(), () => throw new InvalidOperationException(),
            () => throw new InvalidOperationException(), (intent, _) => { navigations.Add(intent); return true; },
            (_, _) => throw new InvalidOperationException(), newTabLayout: preferences,
            shortcutAppAvailable: mode => admitted && mode.Id == boards.Id);
        var window = new Window { Width = 1440, Height = 900, Content = page };
        try
        {
            await page.InitializeAsync(token);
            window.Show(); SettleFixtureLayout(window);
            await Activate(SettingsControl<Button>(page, "home-newtab-shortcut-add"));
            var candidates = page.GetVisualDescendants().OfType<Button>().Where(button =>
                button.Name == "home-newtab-shortcut-picker-candidate" && button.IsEffectivelyVisible).ToArray();
            Assert.DoesNotContain(candidates, button => AutomationProperties.GetName(button) == "Add Canvas");
            await Activate(Assert.Single(candidates, button => AutomationProperties.GetName(button) == "Add " + boards.Name));
            var saved = await preferences.ReadAsync(token);
            var pin = Assert.Single(saved.Layout.Shortcuts, item => item.AppId == boards.Id);
            Assert.Equal("9to1://boards", pin.Target);
            Assert.Empty(navigations);
            await Activate(SettingsControl<Button>(page, "home-newtab-shortcut-picker-close"));
            await Activate(Tile());
            Assert.Equal(pin.Target, Assert.Single(navigations).Address.Value);
            admitted = false;
            await Activate(Tile());
            Assert.Single(navigations);
            Assert.False(Tile().IsEnabled);
            Assert.Contains("unavailable", AutomationProperties.GetName(Tile()));
            Assert.Equal(saved.Serialized, (await preferences.ReadAsync(token)).Serialized);
            Assert.False((pin with { Target = "9to1://boards/elsewhere" }).IsValid());
            Assert.False((pin with { AppId = null }).IsValid());
        }
        finally
        {
            try { await page.CloseAndDrainAsync(); }
            finally { window.Close(); Directory.Delete(paths.DataDirectory, recursive: true); }
        }

        Button Tile() => Assert.Single(page.GetVisualDescendants().OfType<Button>(), button =>
            button.Name == "home-newtab-shortcut" && AutomationProperties.GetName(button)?.StartsWith(boards.Name, StringComparison.Ordinal) == true);
        async Task Activate(Button button)
        {
            Assert.True(button.IsEnabled && button.Focus(NavigationMethod.Tab));
            PressSettingsKey(window, PhysicalKey.Enter);
            await page.WhenActionsIdleAsync().WaitAsync(token);
            SettleFixtureLayout(window);
        }
    }

    [AvaloniaTheory]
    [InlineData(1440, 900)]
    [InlineData(720, 520)]
    public async Task New_Tab_shortcuts_icon_preview_and_reset_keep_registered_identity_and_peer_edits(int width, int height)
    {
        var paths = new LayoutPaths();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(40));
        var token = timeout.Token;
        var request = Request("browse://newtab");
        var boards = BuiltInModeSeed.Modes.Single(mode => mode.Key == "boards");
        var pin = new NewTabShortcutPlacement("registered-boards", boards.Name, "9to1://boards") { AppId = boards.Id };
        var preferences = CreatePreferences();
        var peer = CreatePreferences();
        var before = await preferences.ReadAsync(token);
        Assert.True((await preferences.SaveAsync(before, before.Layout with { Shortcuts = before.Layout.Shortcuts.Add(pin) }, token)).Saved);
        Assert.False((pin with { IconKey = "file:///not-a-catalogue-icon" }).IsValid());
        var navigations = new List<BrowseNavigationIntent>();
        var page = CreatePage(preferences);
        var window = new Window { Width = width, Height = height, Content = page };
        try
        {
            await page.InitializeAsync(token);
            window.Show(); SettleFixtureLayout(window);
            var seeded = await preferences.ReadAsync(token);
            await OpenEditor(boards.Name);
            SettingsControl<TextBox>(page, "home-newtab-shortcut-name").Text = "Uncommitted label";
            await Click("home-newtab-shortcut-icon-open");
            Assert.True(SettingsControl<Button>(page, "home-newtab-shortcut-icon-default").IsFocused);
            Assert.False(SettingsControl<Button>(page, "home-newtab-shortcut-icon-apply").IsEnabled);
            var bookmark = IconChoice("Bookmark");
            bookmark.BringIntoView();
            SettleFixtureLayout(window);
            ClickSettingsButton(window, bookmark);
            await page.WhenActionsIdleAsync().WaitAsync(token);
            SettleFixtureLayout(window);
            Assert.NotNull(SettingsControl<Image>(page, "home-newtab-shortcut-icon-preview").Source);
            Assert.True(SettingsCategorySelected(IconChoice("Bookmark")));
            Assert.True(IconChoice("Bookmark").IsFocused);
            PressSettingsKey(window, PhysicalKey.Escape);
            SettleFixtureLayout(window);
            Assert.True(SettingsControl<Button>(page, "home-newtab-shortcut-icon-open").IsFocused);
            Assert.Equal("Uncommitted label", SettingsControl<TextBox>(page, "home-newtab-shortcut-name").Text);
            Assert.Equal(seeded.Serialized, (await preferences.ReadAsync(token)).Serialized);

            await Click("home-newtab-shortcut-icon-open");
            await PickBookmark();
            var peerPin = pin with { Label = "Peer name" };
            var peerLayout = seeded.Layout with { Shortcuts = seeded.Layout.Shortcuts.SetItem(seeded.Layout.Shortcuts.Length - 1, peerPin) };
            Assert.True((await peer.SaveAsync(seeded, peerLayout, token)).Saved);
            await Click("home-newtab-shortcut-icon-apply");
            Assert.Equal(peerPin, Assert.Single((await preferences.ReadAsync(token)).Layout.Shortcuts, item => item.Id == pin.Id));
            await Click("home-newtab-shortcut-retry");
            var saved = (await preferences.ReadAsync(token)).Layout;
            Assert.Equal(3, saved.Revision);
            Assert.Equal(peerPin with { IconKey = "bookmark" }, Assert.Single(saved.Shortcuts, item => item.Id == pin.Id));
            Assert.Equal("Uncommitted label", SettingsControl<TextBox>(page, "home-newtab-shortcut-name").Text);
            await Click("home-newtab-shortcut-cancel");

            await page.CloseAndDrainAsync();
            preferences = CreatePreferences();
            page = CreatePage(preferences);
            window.Content = page;
            await page.InitializeAsync(token);
            SettleFixtureLayout(window);
            var tile = Assert.Single(page.GetVisualDescendants().OfType<Button>(), button =>
                button.Name == "home-newtab-shortcut" && AutomationProperties.GetName(button) == peerPin.Label);
            var tileIcon = Assert.Single(tile.GetVisualDescendants().OfType<Image>(), image => image.Name == "home-newtab-shortcut-tile-icon");
            Assert.NotNull(tileIcon.Source);
            await Activate(tile);
            Assert.Equal(pin.Target, Assert.Single(navigations).Address.Value);

            await OpenEditor(peerPin.Label);
            await Click("home-newtab-shortcut-icon-open");
            Assert.True(SettingsCategorySelected(IconChoice("Bookmark")));
            await Click("home-newtab-shortcut-icon-default");
            await Click("home-newtab-shortcut-icon-apply");
            var reset = (await preferences.ReadAsync(token)).Layout;
            Assert.Equal(4, reset.Revision);
            Assert.Equal(peerPin, Assert.Single(reset.Shortcuts, item => item.Id == pin.Id));
            Assert.Equal(saved.Shortcuts.Take(saved.Shortcuts.Length - 1), reset.Shortcuts.Take(reset.Shortcuts.Length - 1));
            Assert.Equal(saved.Widgets.AsEnumerable(), reset.Widgets.AsEnumerable());
        }
        finally
        {
            try { await page.CloseAndDrainAsync(); }
            finally { window.Close(); Directory.Delete(paths.DataDirectory, recursive: true); }
        }

        NewTabLayoutPreferences CreatePreferences() => new(new VersionedAtomicSettingsStore(paths), request.ProfileId, _ => Task.CompletedTask);
        UnifiedHomePage CreatePage(NewTabLayoutPreferences owner) => new(request, new Registry(),
            () => throw new InvalidOperationException("Icon editing must not construct Dashboard."),
            () => throw new InvalidOperationException("Icon editing must not construct model discovery."),
            (intent, _) => { navigations.Add(intent); return true; },
            (_, _) => throw new InvalidOperationException("Icon editing must not acquire a model."),
            newTabLayout: owner, shortcutAppAvailable: mode => mode.Id == boards.Id);
        Button IconChoice(string name) => Assert.Single(page.GetVisualDescendants().OfType<Button>(), button =>
            button.Name == "home-newtab-shortcut-icon-choice" && AutomationProperties.GetName(button) == name);
        Task PickBookmark() => Activate(IconChoice("Bookmark"));
        Task Click(string id) => Activate(SettingsControl<Button>(page, id));

        async Task OpenEditor(string name)
        {
            await Click("home-newtab-customise-open");
            var edit = Assert.Single(page.GetVisualDescendants().OfType<Button>(), button =>
                button.Name == "home-newtab-shortcut-edit" && AutomationProperties.GetName(button) == "Edit " + name);
            await Activate(edit);
        }

        async Task Activate(Button button)
        {
            Assert.True(button.IsEnabled && button.Focus(NavigationMethod.Tab), button.Name);
            PressSettingsKey(window, PhysicalKey.Enter);
            await page.WhenActionsIdleAsync().WaitAsync(token);
            SettleFixtureLayout(window);
        }
    }

    [Fact]
    public async Task New_Tab_shortcuts_legacy_defaults_and_saved_empty_are_distinct()
    {
        var paths = new LayoutPaths();
        var token = TestContext.Current.CancellationToken;
        var store = new VersionedAtomicSettingsStore(paths);
        var owner = new NewTabLayoutPreferences(store, "original-home-profile", _ => Task.CompletedTask);
        try
        {
            var initial = await owner.ReadAsync(token);
            var saved = await owner.SaveAsync(initial, initial.Layout, token);
            var raw = JsonNode.Parse(saved.Current.Serialized!)!.AsObject();
            Assert.True(raw.Remove("Shortcuts"));
            var key = Assert.Single((await store.ExportAsync(token)).Settings).Key;
            await store.SetAsync(key, raw, token);
            var old = await owner.ReadAsync(token);
            Assert.Equal(NewTabShortcutPlacement.Defaults.AsEnumerable(), old.Layout.Shortcuts.AsEnumerable());
            Assert.True((await owner.SaveAsync(old, old.Layout with { Shortcuts = [] }, token)).Saved);
            var reloaded = new NewTabLayoutPreferences(new VersionedAtomicSettingsStore(paths), "original-home-profile", _ => Task.CompletedTask);
            Assert.Empty((await reloaded.ReadAsync(token)).Layout.Shortcuts);
        }
        finally { Directory.Delete(paths.DataDirectory, recursive: true); }
    }
}
