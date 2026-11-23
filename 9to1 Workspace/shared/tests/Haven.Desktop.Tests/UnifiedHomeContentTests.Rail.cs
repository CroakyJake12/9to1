using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Haven.Core;
using Haven.Desktop.Services;
using Haven.Desktop.Views.Pages.Home;
using HavenOS.Apps.Browse;
using HavenOS.Home.Core;

namespace Haven.Desktop.Tests;

public sealed partial class UnifiedHomeContentTests
{
    [AvaloniaFact]
    public async Task Home_rail_Account_opens_admitted_Settings_in_a_new_tab_and_keeps_Home()
    {
        var limit = TimeSpan.FromSeconds(20);
        var paths = new LayoutPaths();
        const string profile = "original-home-profile";
        var file = Path.Combine(paths.DataDirectory, "preferences.json");
        var original = "{\"havenUiThemeName\":\"Professional\",\"havenUiAppearance\":2,\"defaultModel\":\"keep\"}"u8.ToArray();
        await File.WriteAllBytesAsync(file, original);
        var source = new NativeHomeThemePreferences(file, new(HomeDataScope.DeviceLocal, profile),
            UserPreferencesService.ProjectLegacyHomeTheme, _ => ValueTask.FromResult(true), callback => Dispatcher.UIThread.Post(callback));
        using var lifetime = new CancellationTokenSource();
        BrowseNativeWorkspace? workspace = null;
        BrowseNavigationIntent? intent = null;
        Task? navigation = null;
        var factory = Factory(new(), lifetime.Token, navigation: (requested, settlement) =>
        {
            intent = requested;
            return workspace!.TryQueueNavigationOriginal(requested, settlement, out navigation);
        }, settings: _ => new(source, _ => Task.CompletedTask));
        var chrome = await BrowseChrome.CreateAsync(paths, internalContentFactory: factory,
            homeProfileId: profile, cancellationToken: lifetime.Token);
        workspace = new BrowseNativeWorkspace(chrome, _ => true);
        var window = BrowseNativeSurface.CreateWindow(workspace, new LayoutReadiness());
        window.Width = 1180;
        window.Height = 720;
        try
        {
            RegisterProductionImageResolver(window);
            window.Show();
            await window.InitializeAsync(lifetime.Token).WaitAsync(limit);
            SettleFixtureLayout(window);
            var pinned = Assert.Single(chrome.State.Tabs, tab => tab.IsHome && !tab.IsCloseable);
            var home = Assert.Single(window.GetVisualDescendants().OfType<UnifiedHomePage>());
            var account = HomeRailButton(home, "home-account", "Account");
            Assert.True(account.IsEnabled);
            ClickSettingsButton(window, account);
            await home.WhenActionsIdleAsync().WaitAsync(limit);
            Assert.NotNull(navigation);
            await navigation!.WaitAsync(limit);
            await Task.WhenAll(workspace.OriginalNotifications).WaitAsync(limit);
            SettleFixtureLayout(window);

            var address = AppContentAddress.Parse("browse://home/settings/settings-account");
            Assert.Equal(new BrowseNavigationIntent(pinned.Id, address, true), intent);
            Assert.Equal(address, chrome.State.SelectedTab.Address);
            Assert.Equal("Account", chrome.State.SelectedTab.Title);
            Assert.NotEqual(pinned.Id, chrome.State.SelectedTabId);
            var settings = Assert.Single(window.GetVisualDescendants().OfType<HomeSettingsDestinationPage>());
            Assert.True(SettingsCategorySelected(SettingsControl<Button>(settings, "home-settings-account")));
            Assert.Equal("Global", SettingsControl<TextBlock>(settings, "home-settings-catalog-scope").Text);
            Assert.Equal(original, await File.ReadAllBytesAsync(file));

            await chrome.SelectTabAsync(pinned.Id, lifetime.Token).WaitAsync(limit);
            await Task.WhenAll(workspace.OriginalNotifications).WaitAsync(limit);
            SettleFixtureLayout(window);
            Assert.Equal(pinned.Address, chrome.State.SelectedTab.Address);
            Assert.Same(account, HomeRailButton(home, "home-account", "Account"));
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

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Home_rail_Assistants_queues_the_owned_manager_route_only_with_a_composed_owner(bool assistantsComposed)
    {
        var limit = TimeSpan.FromSeconds(20);
        var request = Request("browse://newtab");
        var intents = new List<BrowseNavigationIntent>();
        var page = new UnifiedHomePage(request, new Registry(),
            () => throw new InvalidOperationException("This test does not open Dashboard."),
            () => throw new InvalidOperationException("This test does not open model discovery."),
            (intent, _) => { intents.Add(intent); return true; },
            (_, _) => throw new InvalidOperationException("Rail navigation must not prepare an AI draft."),
            aiAvailable: false, assistantsAvailable: assistantsComposed);
        var window = new Window { Width = 720, Height = 650, Content = page };
        try
        {
            await page.InitializeAsync(TestContext.Current.CancellationToken).WaitAsync(limit);
            window.Show();
            SettleFixtureLayout(window);
            Assert.False(HomeRailButton(page, "home-account", "Account").IsEnabled);
            Assert.False(page.IsActionAvailable("home.account"));
            Assert.Equal(assistantsComposed, page.IsActionAvailable("home.assistants"));
            if (!assistantsComposed)
            {
                Assert.DoesNotContain(page.GetVisualDescendants().OfType<Button>(),
                    button => button.Name == "home-assistants" && button.IsEffectivelyVisible);
                Assert.Empty(intents);
                return;
            }

            var assistants = HomeRailButton(page, "home-assistants", "Assistants");
            assistants.BringIntoView();
            SettleFixtureLayout(window);
            ClickSettingsButton(window, assistants);
            await page.WhenActionsIdleAsync().WaitAsync(limit);
            var actual = Assert.Single(intents);
            var address = SpacesBrowseAppContentSession.CreateAssistantRouteAddress(null);
            Assert.Equal(new BrowseNavigationIntent(request.TabId, address, true), actual);
            Assert.True(SpacesBrowseAppContentSession.TryReadAssistantRoute(actual.Address, out var scopedIdentity));
            Assert.Null(scopedIdentity);
            Assert.True(SettingsCategorySelected(HomeRailButton(page, "home-newtab", "New Tab")));
        }
        finally
        {
            try { await page.CloseAndDrainAsync().WaitAsync(limit); }
            finally { window.Close(); }
        }
    }
}
