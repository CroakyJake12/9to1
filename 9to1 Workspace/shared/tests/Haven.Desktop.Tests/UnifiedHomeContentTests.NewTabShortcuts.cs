using System.Text.Json.Nodes;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.VisualTree;
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
            Target().Text = "file:///tmp/unsupported";
            Name().Text = "Blocked";
            await Activate("home-newtab-shortcut-save");
            Assert.Contains("complete http or https address", SettingsControl<TextBlock>(page, "home-newtab-shortcut-status").Text);
            Assert.Null((await preferences.ReadAsync(token)).Serialized);
            await Activate("home-newtab-shortcut-cancel");
            Assert.True(SettingsControl<Button>(page, "home-newtab-shortcut-add").IsFocused);
            Assert.Null((await preferences.ReadAsync(token)).Serialized);

            await Activate("home-newtab-shortcut-add");
            await Activate("home-newtab-shortcut-choose", "Use Models / Discover");
            Assert.Equal(NewTabShortcutPlacement.Models, Target().Text);
            await Activate("home-newtab-shortcut-cancel");
            Assert.Null((await preferences.ReadAsync(token)).Serialized);

            await Activate("home-newtab-shortcut-add");
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
