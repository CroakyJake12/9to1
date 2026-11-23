using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CakeOS.Cui.Runtime;
using Haven.Application;
using Haven.Application.Haven;
using Haven.Core;
using Haven.Desktop.Services;
using Haven.Desktop.Views.Pages.Home;
using HavenOS.Apps.Browse;
using HavenOS.Home.Core;

namespace Haven.Desktop.Tests;

public sealed partial class UnifiedHomeContentTests
{
    [AvaloniaFact]
    public async Task Settings_catalog_opens_all_groups_and_retains_current_data_and_real_owner()
    {
        var limit = TimeSpan.FromSeconds(20);
        var directory = Directory.CreateTempSubdirectory("home-settings-catalog-");
        var file = Path.Combine(directory.FullName, "preferences.json");
        var bytes = "{\"havenUiThemeName\":\"Professional\",\"havenUiAppearance\":2,\"defaultModel\":\"keep\"}"u8.ToArray();
        await File.WriteAllBytesAsync(file, bytes);
        var source = new NativeHomeThemePreferences(file, new(HomeDataScope.DeviceLocal, "original-home-profile"),
            UserPreferencesService.ProjectLegacyHomeTheme, _ => ValueTask.FromResult(true), callback => Dispatcher.UIThread.Post(callback));
        using var lifetime = new CancellationTokenSource();
        var owners = new List<HomeSettingsOwner>();
        var session = Assert.IsAssignableFrom<IBrowseNativeAppContentSession>(Factory(new(), lifetime.Token,
            settings: _ => new(source, _ => Task.CompletedTask)
            {
                OpenOwnerAsync = (owner, _) =>
                {
                    owners.Add(owner);
                    return Task.FromResult(HavenSharedOpenResult.Succeeded());
                }
            }).CreateSession(Request("browse://home/settings")));
        var window = new Window { Width = 720, Height = 650 };
        try
        {
            await session.InitializeAsync().WaitAsync(limit);
            window.Content = session.OriginalNativeView;
            window.Show();
            SettleFixtureLayout(window);
            var settings = Assert.Single(window.GetVisualDescendants().OfType<HomeSettingsDestinationPage>());
            var search = SettingsControl<TextBox>(settings, "home-settings-search");
            var local = SettingsControl<Button>(settings, "home-settings-local");
            var apps = SettingsControl<Button>(settings, "home-settings-apps");
            Assert.Equal("Expanded", AutomationProperties.GetItemStatus(local));
            Assert.Equal("Collapsed", AutomationProperties.GetItemStatus(apps));
            Assert.True(local.Focus());
            PressSettingsKey(window, PhysicalKey.ArrowLeft);
            SettleFixtureLayout(window);
            Assert.Equal("Collapsed", AutomationProperties.GetItemStatus(local));

            await SearchAsync("quiet hours", "local-notifications", "This device");
            var action = Assert.Single(settings.GetVisualDescendants().OfType<Button>(), button =>
                button.Name == "home-settings-catalog-link" && AutomationProperties.GetName(button) == "Open Notifications");
            action.BringIntoView();
            SettleFixtureLayout(window);
            ClickSettingsButton(window, action);
            await settings.WhenActionsIdleAsync().WaitAsync(limit);
            SettleFixtureLayout(window);
            Assert.Equal(new[] { HomeSettingsOwner.Notifications }, owners);
            Assert.Equal(bytes, await File.ReadAllBytesAsync(file));
            var clear = SettingsControl<Button>(settings, "home-settings-clear-search");
            Assert.True(clear.IsEffectivelyVisible);
            ClickSettingsButton(window, clear);
            await settings.WhenActionsIdleAsync().WaitAsync(limit);
            SettleFixtureLayout(window);
            Assert.Equal(string.Empty, search.Text);
            Assert.True(search.IsFocused);

            await SearchAsync("account quota", "account-storage", "Global");
            var availability = SettingsControl<TextBlock>(settings, "home-settings-catalog-availability");
            Assert.Contains("Account usage and quotas", availability.Text);
            Assert.Equal("Storage", session.Title);

            await SearchAsync("shortcut order", "haven-shortcut-order", "Haven");
            var result = SettingsControl<Button>(settings, "home-settings-haven-shortcut-order");
            Assert.True(result.IsFocused);
            PressSettingsKey(window, PhysicalKey.ArrowLeft);
            Assert.True(SettingsControl<Button>(settings, "home-settings-haven-bookmarks").IsFocused);
            PressSettingsKey(window, PhysicalKey.Escape);
            SettleFixtureLayout(window);
            Assert.Equal(string.Empty, search.Text);
            Assert.Equal("Expanded", AutomationProperties.GetItemStatus(SettingsControl<Button>(settings, "home-settings-haven")));
            Assert.Equal("Expanded", AutomationProperties.GetItemStatus(SettingsControl<Button>(settings, "home-settings-local")));

            await SearchAsync("Launcher", "app-launcher", "App");
            Assert.Contains(settings.GetVisualDescendants().OfType<TextBlock>(), text =>
                text.Text == "Unavailable" && text.IsEffectivelyVisible);
            Assert.DoesNotContain(settings.GetVisualDescendants().OfType<Button>(), button =>
                AutomationProperties.GetName(button) == "Open app Launcher" && button.IsEffectivelyVisible);
            Assert.Equal(bytes, await File.ReadAllBytesAsync(file));

            async Task SearchAsync(string value, string key, string scope)
            {
                Assert.True(search.Focus());
                search.Text = value;
                SettleFixtureLayout(window);
                PressSettingsKey(window, PhysicalKey.Enter);
                await settings.WhenActionsIdleAsync().WaitAsync(limit);
                SettleFixtureLayout(window);
                Assert.True(SettingsCategorySelected(SettingsControl<Button>(settings, "home-settings-" + key)));
                Assert.Equal(scope, SettingsControl<TextBlock>(settings, "home-settings-catalog-scope").Text);
            }
        }
        finally
        {
            try { await session.CloseAndDrainAsync(); }
            finally { window.Close(); await source.DisposeAsync(); directory.Delete(recursive: true); }
        }
    }

    [AvaloniaTheory]
    [InlineData("settings-general", "global-general", "Global", "General")]
    [InlineData("settings-storage", "local-general", "This device", "General")]
    [InlineData("settings-account-storage", "account-storage", "Global", "Storage")]
    [InlineData("settings-app-boards", "app-boards", "App", "Boards")]
    [InlineData("settings-haven-newtab", "haven-newtab", "Haven", "New Tab")]
    public async Task Settings_catalog_routes_admit_known_destinations_and_deny_unknown_routes(
        string route, string category, string scope, string heading)
    {
        var limit = TimeSpan.FromSeconds(20);
        var paths = new LayoutPaths();
        const string profile = "original-home-profile";
        var file = Path.Combine(paths.DataDirectory, "preferences.json");
        var source = new NativeHomeThemePreferences(file, new(HomeDataScope.DeviceLocal, profile),
            UserPreferencesService.ProjectLegacyHomeTheme, _ => ValueTask.FromResult(true), callback => Dispatcher.UIThread.Post(callback));
        using var lifetime = new CancellationTokenSource();
        const string unknown = "browse://home/settings/unknown-atlas-route";
        var factory = Factory(new(), lifetime.Token, settings: _ => new(source, _ => Task.CompletedTask));
        Assert.Throws<NotSupportedException>(() => factory.CreateSession(Request(unknown)));
        Assert.Throws<NotSupportedException>(() => factory.CreateSession(Request("browse://newtab/settings/settings-general")));
        var chrome = await BrowseChrome.CreateAsync(paths, internalContentFactory: factory,
            homeProfileId: profile, cancellationToken: lifetime.Token);
        var workspace = new BrowseNativeWorkspace(chrome, _ => true);
        var window = BrowseNativeSurface.CreateWindow(workspace, new LayoutReadiness());
        window.Width = 1440;
        window.Height = 900;
        try
        {
            RegisterProductionImageResolver(window);
            window.Show();
            await window.InitializeAsync(lifetime.Token).WaitAsync(limit);
            var pinned = Assert.Single(chrome.State.Tabs, tab => tab.IsHome && !tab.IsCloseable && tab.Address.Value == "browse://home");
            var address = "browse://home/settings/" + route;
            await chrome.NavigateAsync(address, lifetime.Token).WaitAsync(limit);
            await Task.WhenAll(workspace.OriginalNotifications).WaitAsync(limit);
            SettleFixtureLayout(window);
            var settings = Assert.Single(window.GetVisualDescendants().OfType<HomeSettingsDestinationPage>());
            Assert.Equal(address, chrome.State.SelectedTab.Address.Value);
            Assert.Equal(heading, chrome.State.SelectedTab.Title);
            Assert.Contains(chrome.State.Tabs, tab => tab.Id == pinned.Id && tab.IsHome && !tab.IsCloseable && tab.Address.Value == "browse://home");
            Assert.Equal($"{scope} settings, {heading}", AutomationProperties.GetName(settings));
            Assert.True(SettingsCategorySelected(SettingsControl<Button>(settings, "home-settings-" + category)));
            Assert.Equal(scope, SettingsControl<TextBlock>(settings, "home-settings-catalog-scope").Text);
            if (category == "global-general")
            {
                var section = SettingsControl<TextBlock>(settings, "home-settings-global-general-section");
                Assert.True(section.IsEffectivelyVisible);
                Assert.Equal("Across 9to1", section.Text);
                Assert.Contains("Language and regional formatting",
                    SettingsControl<TextBlock>(settings, "home-settings-catalog-availability").Text);
            }
            CaptureSettingsIfRequested(window, route, source);
            if (category == "haven-newtab")
            {
                var search = SettingsControl<TextBox>(settings, "home-settings-search");
                Assert.True(search.Focus());
                window.Width = 720;
                SettleFixtureLayout(window);
                var selected = SettingsControl<Button>(settings, "home-settings-" + category);
                var rail = SettingsControl<CuiContainerControl>(settings, "home-settings-categories");
                var scroll = Assert.IsType<ScrollViewer>(rail.Child);
                var origin = selected.TranslatePoint(default, scroll)!.Value;
                Assert.InRange(origin.Y, -1, scroll.Viewport.Height - selected.Bounds.Height + 1);
                Assert.True(search.IsFocused);
                CaptureSettingsIfRequested(window, route + "-compact", source);

                scroll.Offset = default;
                SettleFixtureLayout(window);
                window.Width = 710;
                SettleFixtureLayout(window);
                Assert.Equal(0, scroll.Offset.Y);
                Assert.True(search.IsFocused);
            }
            await Assert.ThrowsAsync<NotSupportedException>(() => chrome.OpenContentAsync(AppContentAddress.Parse(unknown)));
            await chrome.NavigateAsync(unknown, lifetime.Token).WaitAsync(limit);
            Assert.Equal(address, chrome.State.SelectedTab.Address.Value);
            Assert.Same(settings, Assert.Single(window.GetVisualDescendants().OfType<HomeSettingsDestinationPage>()));
        }
        finally
        {
            try
            {
                window.Close();
                if (window.OriginalClosePreparation is { } preparation) Assert.True(await preparation.WaitAsync(limit));
                Dispatcher.UIThread.RunJobs();
                Assert.NotNull(window.OriginalClose);
                await window.OriginalClose!.WaitAsync(limit);
            }
            finally
            {
                await source.DisposeAsync();
                Directory.Delete(paths.DataDirectory, recursive: true);
            }
        }
    }
}
