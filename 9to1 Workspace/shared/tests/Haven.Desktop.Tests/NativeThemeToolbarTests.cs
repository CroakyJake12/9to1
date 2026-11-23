using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Haven.Browser;
using Haven.Core;
using Haven.Desktop.Services;
using Haven.Desktop.Views.Pages.Home;
using HavenOS.Apps.Browse;
using HavenOS.Home.Core;

namespace Haven.Desktop.Tests;

public sealed partial class UnifiedHomeContentTests
{
    [AvaloniaTheory]
    [InlineData(1440, 900)]
    [InlineData(720, 520)]
    public async Task Shared_theme_save_retains_the_admitted_writer_without_stealing_another_tab(int width, int height)
    {
        var token = TestContext.Current.CancellationToken;
        var limit = TimeSpan.FromSeconds(20);
        var paths = new LayoutPaths();
        var file = Path.Combine(paths.DataDirectory, "preferences.json");
        const string profile = "original-home-profile";
        const string routeA = "browse://home/settings/appearance";
        const string routeB = "browse://home/settings/settings-theme-options";
        const string draftName = "Shared toolbar draft";
        const string addressDraft = "https://example.invalid/unsent-editor-address";
        var scope = new HomeThemeScope(HomeDataScope.DeviceLocal, profile);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var armSave = 0;

        // Pause the genuine owner after the real Save action reaches authorization. All later
        // observations remain live, so selecting an independent tab never waits on this test gate.
        ValueTask<bool> Authorize(CancellationToken requested)
        {
            if (Interlocked.Exchange(ref armSave, 0) == 0) return ValueTask.FromResult(true);
            entered.TrySetResult();
            return new(release.Task.WaitAsync(requested));
        }

        var source = new NativeHomeThemePreferences(file, scope, UserPreferencesService.ProjectLegacyHomeTheme,
            Authorize, update => Dispatcher.UIThread.Post(update));
        var factory = Factory(new Registry(), token, settings: _ => new(source, _ => Task.CompletedTask));
        var chrome = await BrowseChrome.CreateAsync(paths, internalContentFactory: factory,
            homeProfileId: profile, cancellationToken: token);
        var workspace = new BrowseNativeWorkspace(chrome, _ => true);
        var window = BrowseNativeSurface.CreateWindow(workspace, new LayoutReadiness());
        window.Width = width;
        window.Height = height;

        try
        {
            await File.WriteAllTextAsync(file, """{"defaultModel":"retained-model"}""", token);
            var before = await File.ReadAllBytesAsync(file, token);
            RegisterProductionImageResolver(window);
            window.Show();
            await window.InitializeAsync(token).WaitAsync(limit);

            var (tabA, settingsA) = await OpenEditor(routeA, "home-create-theme");
            var firstTargetA = PresentedTarget();
            var (tabB, settingsB) = await OpenEditor(routeB, "home-theme-options-create");
            Assert.NotEqual(tabA, tabB);
            Assert.NotSame(settingsA, settingsB);
            var targetB = PresentedTarget();
            Assert.NotSame(firstTargetA, targetB);
            Assert.Equal(draftName, NameField(settingsB).Text);
            await Replay(firstTargetA);
            Assert.Equal(before, await File.ReadAllBytesAsync(file, token));

            await Select(tabA);
            Assert.Equal(draftName, NameField(settingsA).Text);
            var targetA = PresentedTarget();
            Assert.NotSame(firstTargetA, targetA);
            await Replay(firstTargetA);
            Assert.Equal(before, await File.ReadAllBytesAsync(file, token));

            var save = await SaveButtonAsync();
            Interlocked.Exchange(ref armSave, 1);
            Press(save);
            await entered.Task.WaitAsync(limit, token);
            Assert.NotNull(settingsA.LastWrite);
            Assert.Null(settingsA.LastWrite!.ConfirmedWrite);
            var writerDrain = settingsA.WhenActionsIdleAsync();
            Assert.False(writerDrain.IsCompleted);
            SettleFixtureLayout(window);
            Assert.True(!save.IsEffectivelyVisible || !save.IsEffectivelyEnabled);

            // The currently admitted draft is busy. A second command with the exact token
            // cannot queue another write, and the actual tab selection remains reachable.
            await Replay(targetA);
            await Select(tabB);
            Assert.Equal("Create theme", chrome.State.SelectedTab.Title);
            Assert.Equal(routeB, chrome.State.SelectedTab.Address.Value);
            Assert.Equal(draftName, NameField(settingsB).Text);
            var address = Assert.Single(window.GetVisualDescendants().OfType<TextBox>(), box => box.Name == "BrowseAddressBox");
            Assert.True(address.Focus(NavigationMethod.Tab));
            address.Text = addressDraft;
            await Replay(targetA);
            Assert.Equal(before, await File.ReadAllBytesAsync(file, token));

            release.TrySetResult(true);
            await writerDrain.WaitAsync(limit, token);
            await Task.WhenAll(workspace.OriginalOperations).WaitAsync(limit, token);
            await AwaitAppliedTheme();
            await Render();
            Assert.True(settingsA.LastWrite!.ConfirmedWrite?.Succeeded);
            Assert.Null(settingsB.LastWrite);
            Assert.Equal(tabB, chrome.State.SelectedTabId);
            Assert.Equal("Create theme", chrome.State.SelectedTab.Title);
            Assert.Equal(routeB, chrome.State.SelectedTab.Address.Value);
            Assert.True(address.IsFocused);
            Assert.Equal(addressDraft, address.Text);
            Assert.Equal(draftName, NameField(settingsB).Text);

            var document = HomeNativePreferenceFile.Read(file);
            var stored = HomeNativePreferenceFile.Resolve(document, scope, UserPreferencesService.ProjectLegacyHomeTheme);
            Assert.Equal(1, stored.Revision);
            Assert.Equal(draftName, stored.ThemeName);
            Assert.Equal("retained-model", document["defaultModel"]!.GetValue<string>());
            Assert.Equal(draftName, Assert.Single(CakeOS.Cui.Themes.CuiCustomThemeContract.DeserializeLibrary(stored.CustomThemesJson!)).Name);
            var after = await File.ReadAllBytesAsync(file, token);

            await Select(tabA);
            Assert.Equal(routeA, chrome.State.SelectedTab.Address.Value);
            Assert.Equal("Theme and accent preferences", chrome.State.SelectedTab.Title);
            Assert.DoesNotContain(settingsA.GetVisualDescendants().OfType<TextBox>(), field =>
                field.IsEffectivelyVisible && AutomationProperties.GetName(field) == "Theme name");
            await ActivateCreator(settingsA, "home-create-theme");
            NameField(settingsA).Text = "A replacement draft";
            await Render();
            Assert.NotSame(targetA, PresentedTarget());
            await Replay(targetA);
            Assert.Equal("A replacement draft", NameField(settingsA).Text);
            Assert.Equal(after, await File.ReadAllBytesAsync(file, token));
        }
        finally
        {
            release.TrySetResult(true);
            try
            {
                window.Close();
                if (window.OriginalClosePreparation is { } preparation)
                    Assert.True(await preparation.WaitAsync(limit));
                Dispatcher.UIThread.RunJobs();
                Assert.NotNull(window.OriginalClose);
                await window.OriginalClose!.WaitAsync(limit);
            }
            finally { await source.DisposeAsync(); Directory.Delete(paths.DataDirectory, recursive: true); }
        }

        async Task AwaitAppliedTheme()
        {
            var applied = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            void Observe(object? sender, EventArgs args)
            {
                if (source.Context.Current.Theme.Name == draftName) applied.TrySetResult();
            }
            source.Context.Changed += Observe;
            try
            {
                Observe(null, EventArgs.Empty);
                await applied.Task.WaitAsync(limit, token);
            }
            finally { source.Context.Changed -= Observe; }
        }

        async Task Render()
        {
            await Task.WhenAll(workspace.OriginalNotifications).WaitAsync(limit, token);
            SettleFixtureLayout(window);
        }

        async Task Select(Guid id)
        {
            await chrome.SelectTabAsync(id, token).WaitAsync(limit, token);
            await Render();
            Assert.Equal(id, chrome.State.SelectedTabId);
        }

        async Task<(Guid Id, HomeSettingsDestinationPage View)> OpenEditor(string route, string triggerId)
        {
            await chrome.OpenContentAsync(AppContentAddress.Parse(route), cancellationToken: token).WaitAsync(limit, token);
            await Render();
            var view = Assert.Single(window.GetVisualDescendants().OfType<HomeSettingsDestinationPage>(),
                settings => settings.IsEffectivelyVisible);
            await ActivateCreator(view, triggerId);
            var tab = chrome.State.SelectedTab;
            var selectedSource = Assert.IsAssignableFrom<IBrowseAppContentSession>(chrome.ObserveOriginalAppContent(tab.Id));
            var appPublications = 0;
            var chromePublications = 0;
            EventHandler observeApp = (_, _) => appPublications++;
            EventHandler<BrowseChromeSnapshot> observeChrome = (_, _) => chromePublications++;
            selectedSource.StateChanged += observeApp;
            chrome.StateChanged += observeChrome;
            try
            {
                var name = NameField(view);
                Assert.NotEqual(draftName, name.Text);
                name.Text = draftName;
                await Render();
                Assert.Equal(draftName, name.Text);
                Assert.Equal(0, appPublications);
                Assert.Equal(0, chromePublications);
            }
            finally
            {
                selectedSource.StateChanged -= observeApp;
                chrome.StateChanged -= observeChrome;
            }
            Assert.Equal("Create theme", chrome.State.SelectedTab.Title);
            Assert.Equal(route, chrome.State.SelectedTab.Address.Value);
            return (chrome.State.SelectedTabId, view);
        }

        async Task ActivateCreator(HomeSettingsDestinationPage view, string triggerId)
        {
            Press(SettingsControl<Button>(view, triggerId));
            await view.WhenActionsIdleAsync().WaitAsync(limit, token);
            await Render();
            Assert.DoesNotContain(view.GetVisualDescendants().OfType<Button>(), button =>
                button.IsEffectivelyVisible && AutomationProperties.GetName(button) == "Save custom theme");
        }

        static TextBox NameField(HomeSettingsDestinationPage view) =>
            Assert.Single(view.GetVisualDescendants().OfType<TextBox>(), field =>
                field.IsEffectivelyVisible && AutomationProperties.GetName(field) == "Theme name");

        void Press(Button button)
        {
            button.BringIntoView();
            SettleFixtureLayout(window);
            Assert.True(button.IsEffectivelyEnabled);
            Assert.True(button.Focus(NavigationMethod.Tab));
            PressSettingsKey(window, PhysicalKey.Enter);
        }

        object PresentedTarget()
        {
            Assert.True(workspace.TryGetValue("PageCommandTarget", out var current));
            Assert.NotNull(current);
            return current;
        }

        Task Replay(object target) =>
            workspace.DispatchAsync("9to1.Browse.RunPageCommand", target, token).AsTask().WaitAsync(limit, token);

        async Task<Button> SaveButtonAsync()
        {
            if (width < 800)
            {
                Press(ShellButton("HavenProfileButton"));
                await Render();
            }
            var button = ShellButton(width < 800 ? "BrowseOverflowPageCommand" : "BrowsePrimaryPageCommand");
            Assert.Equal("Save theme", AutomationProperties.GetName(button));
            return button;
        }

        Button ShellButton(string id) =>
            Assert.Single(window.GetVisualDescendants().OfType<Button>(), button =>
                button.IsEffectivelyVisible && button.Name == id);
    }
}
