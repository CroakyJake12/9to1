using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CakeOS.Cui.Runtime;
using CakeOS.Cui.Themes;
using Haven.Browser;
using Haven.Core;
using Haven.Desktop.Services;
using Haven.Desktop.Views.Pages.Home;
using HavenOS.Apps.Browse;
using HavenOS.Home.Core;

namespace Haven.Desktop.Tests;

public sealed partial class UnifiedHomeContentTests
{
    private static async Task WaitForAppliedThemeAsync(NativeHomeThemePreferences owner, string name, CancellationToken token)
    {
        var applied = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void Observe(object? sender, EventArgs args)
        {
            if (owner.Context.Current.Theme.Name == name) applied.TrySetResult();
        }
        owner.Context.Changed += Observe;
        try
        {
            Observe(null, EventArgs.Empty);
            await applied.Task.WaitAsync(TimeSpan.FromSeconds(20), token);
        }
        finally { owner.Context.Changed -= Observe; }
    }

    [AvaloniaFact]
    public async Task Native_theme_creator_rejects_stale_scope_revision_and_unadmitted_drafts_without_saving()
    {
        var directory = Directory.CreateTempSubdirectory("native-theme-draft-");
        var file = Path.Combine(directory.FullName, "preferences.json");
        var scope = new HomeThemeScope(HomeDataScope.DeviceLocal, "creator-profile");
        var token = TestContext.Current.CancellationToken;
        try
        {
            await using var owner = new NativeHomeThemePreferences(file, scope, UserPreferencesService.ProjectLegacyHomeTheme,
                _ => ValueTask.FromResult(true), update => Dispatcher.UIThread.Post(update));
            await owner.RefreshAsync(token);
            var opening = owner.Current;
            Assert.True((await owner.ApplySettingsAsync(ThemeTarget(owner, "Bubble"), token)).Succeeded);
            var before = await File.ReadAllTextAsync(file, token);
            var draft = CuiCustomThemeContract.CreateDefault("Review before saving");
            int dispatches = 0;
            Task Decline(object target, CancellationToken requested) { dispatches++; return Task.CompletedTask; }
            var stale = await owner.SaveThemeDraftAsync(draft, opening, Decline, token);
            Assert.Equal("RevisionConflict", stale.Code);
            await owner.RefreshAsync(token);
            Assert.Equal(opening.Revision + 1, owner.Current.Revision);
            var wrongScope = await owner.SaveThemeDraftAsync(draft, owner.Current with { ScopeId = "device:other" }, Decline, token);
            Assert.Equal("RevisionConflict", wrongScope.Code);
            Assert.Equal(0, dispatches);
            var unadmitted = await owner.SaveThemeDraftAsync(draft, owner.Current, Decline, token);
            Assert.Equal("SettingsUnavailable", unadmitted.Code);
            Assert.Equal(1, dispatches);
            Assert.Equal(before, await File.ReadAllTextAsync(file, token));
        }
        finally { directory.Delete(recursive: true); }
    }

    [AvaloniaFact]
    public async Task Native_theme_creator_updates_the_selected_tab_and_friendly_address_then_restores_its_origin()
    {
        var token = TestContext.Current.CancellationToken;
        var limit = TimeSpan.FromSeconds(20);
        var paths = new LayoutPaths();
        const string profile = "original-home-profile";
        var source = new NativeHomeThemePreferences(Path.Combine(paths.DataDirectory, "preferences.json"),
            new(HomeDataScope.DeviceLocal, profile), UserPreferencesService.ProjectLegacyHomeTheme,
            _ => ValueTask.FromResult(true), update => Dispatcher.UIThread.Post(update));
        var factory = Factory(new Registry(), token, settings: _ => new(source, _ => Task.CompletedTask));
        var chrome = await BrowseChrome.CreateAsync(paths, internalContentFactory: factory, homeProfileId: profile, cancellationToken: token);
        var workspace = new BrowseNativeWorkspace(chrome, _ => true);
        var window = BrowseNativeSurface.CreateWindow(workspace, new LayoutReadiness());
        window.Width = 1440; window.Height = 900;
        try
        {
            RegisterProductionImageResolver(window);
            window.Show();
            await window.InitializeAsync(token).WaitAsync(limit);
            await chrome.OpenContentAsync(AppContentAddress.Parse("browse://home/settings"), cancellationToken: token).WaitAsync(limit);
            await Render();
            var tabId = chrome.State.SelectedTabId;
            var settings = Assert.Single(window.GetVisualDescendants().OfType<HomeSettingsDestinationPage>());
            Button ButtonByName(string name) => settings.GetVisualDescendants().OfType<Button>()
                .Single(button => button.IsEffectivelyVisible && AutomationProperties.GetName(button) == name);
            async Task Activate(Button button)
            {
                button.BringIntoView();
                SettleFixtureLayout(window);
                Assert.True(button.Focus(NavigationMethod.Tab));
                PressSettingsKey(window, PhysicalKey.Enter);
                await settings.WhenActionsIdleAsync().WaitAsync(limit);
                await Render();
            }
            void Title(string title)
            {
                Assert.Equal(tabId, chrome.State.SelectedTabId);
                Assert.Equal("browse://home/settings", chrome.State.SelectedTab.Address.Value);
                Assert.Equal(title, chrome.State.SelectedTab.Title);
                Assert.True(workspace.TryGetValue("AddressDisplay", out var displayed));
                Assert.Equal(title, displayed);
                Assert.Contains(window.GetVisualDescendants().OfType<Button>(), button =>
                    button.Name == "BrowseTabButton" && AutomationProperties.GetName(button) == title);
            }
            var appearance = "Theme and accent preferences";
            Title(appearance);
            await Activate(SettingsControl<Button>(settings, "home-create-theme"));
            Title("Create theme");
            CaptureSettingsIfRequested(window, "creator-shell-title", source);
            await Activate(SettingsControl<Button>(settings, "home-settings-accessibility"));
            Title("Accessibility");
            Assert.True(SettingsControl<CuiContainerControl>(settings, "home-appearance-accessibility").IsEffectivelyVisible);
            Assert.DoesNotContain(settings.GetVisualDescendants().OfType<TextBox>(), field =>
                field.IsEffectivelyVisible && AutomationProperties.GetName(field) == "Theme name");
            await Activate(SettingsControl<Button>(settings, "home-settings-theme"));
            Title("Create theme");
            await Activate(ButtonByName("Cancel custom theme changes"));
            Title(appearance);
            await Activate(SettingsControl<Button>(settings, "home-settings-theme-options"));
            Title("Theme Options");
            await Activate(SettingsControl<Button>(settings, "home-theme-options-create"));
            Title("Create theme");
            var name = settings.GetVisualDescendants().OfType<TextBox>()
                .Single(field => field.IsEffectivelyVisible && AutomationProperties.GetName(field) == "Theme name");
            Assert.True(name.Focus());
            PressSettingsKey(window, PhysicalKey.Escape);
            await Render();
            Title("Theme Options");
            Assert.True(SettingsControl<Button>(settings, "home-theme-options-create").IsFocused);
            await Activate(SettingsControl<Button>(settings, "home-theme-options-create"));
            Title("Create theme");
            name = settings.GetVisualDescendants().OfType<TextBox>()
                .Single(field => field.IsEffectivelyVisible && AutomationProperties.GetName(field) == "Theme name");
            name.Text = "Title restoration";
            SettleFixtureLayout(window);
            await Activate(SettingsControl<Button>(settings, "home-settings-theme-options"));
            await Activate(SettingsControl<Button>(settings, "home-theme-options-palette"));
            Title("Accent palette");
            Assert.False(workspace.IsActionAvailable("9to1.Browse.RunPageCommand"));
            Assert.DoesNotContain(window.GetVisualDescendants().OfType<Button>(), button =>
                button.Name == "BrowsePrimaryPageCommand" && button.IsEffectivelyVisible);
            await Activate(SettingsControl<Button>(settings, "home-palette-back"));
            Title(appearance);
            Assert.False(workspace.IsActionAvailable("9to1.Browse.RunPageCommand"));
            Assert.DoesNotContain(window.GetVisualDescendants().OfType<Button>(), button =>
                button.Name == "BrowsePrimaryPageCommand" && button.IsEffectivelyVisible);
            await Activate(SettingsControl<Button>(settings, "home-create-theme"));
            Title("Create theme");
            Assert.True(name.IsEffectivelyVisible);
            Assert.Equal("Title restoration", name.Text);
            await Activate(SettingsControl<Button>(window, "BrowsePrimaryPageCommand"));
            Title("Theme Options");
            await WaitForAppliedThemeAsync(source, "Title restoration", token);
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
            finally { await source.DisposeAsync(); Directory.Delete(paths.DataDirectory, recursive: true); }
        }
        async Task Render()
        {
            await Task.WhenAll(workspace.OriginalNotifications).WaitAsync(limit);
            SettleFixtureLayout(window);
        }
    }

    [AvaloniaTheory]
    [InlineData(1440, 900)]
    [InlineData(720, 520)]
    public async Task Native_theme_creator_previews_cancels_saves_and_reopens_the_original_profile(int width, int height)
    {
        var directory = Directory.CreateTempSubdirectory("native-create-theme-");
        var file = Path.Combine(directory.FullName, "preferences.json");
        var scope = new HomeThemeScope(HomeDataScope.DeviceLocal, "custom-theme-profile");
        var token = TestContext.Current.CancellationToken;
        var existing = CuiCustomThemeContract.CreateDefault("Earlier theme");
        var original = HomeThemePreference.Default(scope) with
        {
            Revision = 1, CustomThemesJson = CuiCustomThemeContract.SerializeLibrary([existing]),
            Display = new(HomeDisplayMode.Dark, 40, 60), Appearance = "Dark",
            Accent = new(HomeThemeAccentOverrideMode.None, "#123ABC",
                new Dictionary<string, string> { ["app.write"] = "#11AA77" })
        };
        NativeHomeThemePreferences OpenOwner() => new(file, scope,
            UserPreferencesService.ProjectLegacyHomeTheme, _ => ValueTask.FromResult(true), action => Dispatcher.UIThread.Post(action));
        try
        {
            await File.WriteAllTextAsync(file, """{"defaultModel":"retained-model"}""", token);
            Assert.True((await HomeNativePreferenceFile.CompareExchangeAsync(file, scope, 0, original,
                UserPreferencesService.ProjectLegacyHomeTheme, _ => ValueTask.FromResult(true), token)).Succeeded);
            var before = await File.ReadAllTextAsync(file, token);
            await using (var owner = OpenOwner())
            {
                var settings = new HomeSettingsDestinationPage(owner, _ => Task.CompletedTask);
                var window = new Window { Width = width, Height = height, Content = settings };
                try
                {
                    await settings.ActivateAsync(token);
                    window.Show();
                    SettleFixtureLayout(window);
                    var trigger = SettingsControl<Button>(settings, "home-create-theme");
                    T Control<T>(string name) where T : Control => settings.GetVisualDescendants()
                        .OfType<T>().Single(item => item.IsEffectivelyVisible && AutomationProperties.GetName(item) == name);
                    async Task Activate(Button button)
                    {
                        button.BringIntoView();
                        SettleFixtureLayout(window);
                        Assert.True(button.Focus(NavigationMethod.Tab));
                        PressSettingsKey(window, PhysicalKey.Enter);
                        await settings.WhenActionsIdleAsync().WaitAsync(TimeSpan.FromSeconds(20), token);
                        SettleFixtureLayout(window);
                    }
                    await Activate(trigger);
                    var name = Control<TextBox>("Theme name");
                    Assert.True(name.IsFocused);
                    name.Text = "A discarded draft";
                    PressSettingsKey(window, PhysicalKey.Escape);
                    SettleFixtureLayout(window);
                    Assert.True(trigger.IsFocused);
                    Assert.Equal(before, await File.ReadAllTextAsync(file, token));

                    await Activate(trigger);
                    name = Control<TextBox>("Theme name");
                    Assert.Equal("My theme", name.Text);
                    name.Text = "Cobalt workshop";
                    SettleFixtureLayout(window);
                    Control<ComboBox>("Base theme").SelectedIndex = (int)CuiTheme.Metallic;
                    await Activate(Control<Button>("Edit material details"));
                    Control<TextBox>("Colour hexadecimal RGB").Text = "#35728D";
                    await Activate(Control<Button>("Edit secondary accent material"));
                    await Activate(Control<Button>("Edit material details"));
                    Control<TextBox>("Colour hexadecimal RGB").Text = "#458674";
                    Control<Slider>("Button roundness percent").Value = 64;
                    await Activate(Control<Button>("Edit primary accent material"));
                    await Activate(Control<Button>("Edit material details"));
                    Assert.Equal("#35728D", Control<TextBox>("Colour hexadecimal RGB").Text);
                    await Activate(Control<Button>("Edit material details"));
                    Assert.Equal(before, await File.ReadAllTextAsync(file, token));
                    var fragment = SettingsControl<CuiContainerControl>(settings, "home-appearance-settings");
                    Assert.IsType<ScrollViewer>(fragment.Child).Offset = default;
                    SettleFixtureLayout(window);
                    CaptureSettingsIfRequested(window, "custom-theme", owner);
                    await Activate(Control<Button>("Save custom theme"));
                    Assert.True(settings.LastWrite?.ConfirmedWrite?.Succeeded, settings.LastWrite?.ConfirmedWrite?.Diagnostic);
                    Assert.True(trigger.IsEffectivelyVisible);
                    await WaitForAppliedThemeAsync(owner, "Cobalt workshop", token);
                    SettleFixtureLayout(window);
                    Assert.Equal("#35728D", owner.Context.Current.Theme.CustomTheme!.PrimaryAccent.Colour.Hex);
                    Assert.Equal(CuiTheme.Metallic, owner.Context.Current.Theme.BaseTheme);
                    var card = Control<Button>("Cobalt workshop");
                    card.BringIntoView();
                    SettleFixtureLayout(window);
                    var samples = card.GetVisualDescendants().OfType<CuiContainerControl>().ToArray();
                    var primary = Assert.Single(samples, control => control.Name == "home-theme-primary-sample");
                    var secondary = Assert.Single(samples, control => control.Name == "home-theme-secondary-sample");
                    Assert.Equal(Color.Parse("#35728D"), Assert.IsAssignableFrom<ISolidColorBrush>(primary.Background).Color);
                    Assert.Equal(Color.Parse("#458674"), Assert.IsAssignableFrom<ISolidColorBrush>(secondary.Background).Color);
                    Assert.Equal(5.12, primary.CornerRadius.TopLeft, 2);
                    CaptureSettingsIfRequested(window, "custom-theme-saved-gallery", owner);
                }
                finally { window.Close(); await settings.CloseAndDrainAsync(); }
            }
            await using var reopened = OpenOwner();
            await reopened.RefreshAsync(token);
            var applied = reopened.Context.Current.Theme.CustomTheme!;
            Assert.Equal("Cobalt workshop", applied.Name);
            Assert.Equal("#458674", applied.SecondaryAccent.Colour.Hex);
            Assert.Equal(64, applied.ButtonRoundnessPercent);
            var saved = HomeNativePreferenceFile.Read(file);
            var now = HomeNativePreferenceFile.Resolve(saved, scope, UserPreferencesService.ProjectLegacyHomeTheme);
            Assert.Equal(2, now.Revision);
            Assert.Equal(original.Display, now.Display);
            Assert.Equal("#11AA77", now.Accent!.IndividualApps["app.write"]);
            Assert.Equal("retained-model", saved["defaultModel"]!.GetValue<string>());
            var library = CuiCustomThemeContract.DeserializeLibrary(now.CustomThemesJson!);
            Assert.Equal(2, library.Count);
            Assert.Equal(CuiCustomThemeContract.Serialize(existing), CuiCustomThemeContract.Serialize(library.Single(t => t.Id == existing.Id)));
        }
        finally { directory.Delete(recursive: true); }
    }
}
