using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CakeOS.Cui.Runtime;
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
    public async Task Accent_palette_address_opens_the_dedicated_surface_and_uses_the_original_colour_owner(int width, int height)
    {
        using var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var paths = new LayoutPaths();
        var file = Path.Combine(paths.DataDirectory, "preferences.json");
        var bytes = """{"havenUiThemeName":"Professional","havenUiAppearance":2,"defaultModel":"retained"}"""u8.ToArray();
        Directory.CreateDirectory(paths.DataDirectory);
        await File.WriteAllBytesAsync(file, bytes);
        var scope = new HomeThemeScope(HomeDataScope.DeviceLocal, "original-home-profile");
        await using var source = new NativeHomeThemePreferences(file, scope, UserPreferencesService.ProjectLegacyHomeTheme,
            _ => ValueTask.FromResult(true), callback => Dispatcher.UIThread.Post(callback));
        var session = Assert.IsAssignableFrom<IBrowseNativeAppContentSession>(Factory(new(), lifetime.Token,
            settings: _ => new(source, _ => Task.CompletedTask)).CreateSession(Request("browse://home/settings/appearance-palette")));
        var window = new Window { Width = width, Height = height };
        try
        {
            await session.InitializeAsync(lifetime.Token);
            window.Content = session.OriginalNativeView;
            window.Show();
            SettleFixtureLayout(window);
            var settings = Assert.Single(window.GetVisualDescendants().OfType<HomeSettingsDestinationPage>());
            var heading = SettingsControl<TextBlock>(settings, "home-palette-title");
            var card = SettingsControl<CuiContainerControl>(settings, "home-accent-settings");
            var back = SettingsControl<Button>(settings, "home-palette-back");
            Assert.Equal("Accent palette", settings.CategoryTitle);
            Assert.Equal(settings.CategoryTitle, heading.Text);
            Assert.True(heading.IsEffectivelyVisible && back.IsEffectivelyVisible && card.IsEffectivelyVisible);
            Assert.True(SettingsCategorySelected(SettingsControl<Button>(settings, "home-settings-theme")));
            Assert.DoesNotContain(settings.GetVisualDescendants().OfType<CuiContainerControl>(), panel =>
                panel.Name is "home-theme-gallery" or "home-display-settings" && panel.IsEffectivelyVisible && panel.IsAttachedToVisualTree());
            Assert.Equal(bytes, await File.ReadAllBytesAsync(file));
            var start = card.TranslatePoint(default, window)!.Value;
            Assert.InRange(start.X, 0, window.Bounds.Width);
            Assert.InRange(start.X + card.Bounds.Width, 1, window.Bounds.Width);
            if (width == 1440)
            {
                Assert.InRange(start.X, 515, 545);
                Assert.InRange(card.Bounds.Width, 635, 677);
            }
            CaptureSettingsIfRequested(window, "appearance-palette", source);
            var iris = Assert.Single(card.GetVisualDescendants().OfType<Button>(),
                button => button.Name == "home-global-accent" && AutomationProperties.GetName(button) == "Iris");
            iris.BringIntoView();
            SettleFixtureLayout(window);
            Assert.True(iris.Focus(NavigationMethod.Tab));
            PressSettingsKey(window, PhysicalKey.Enter);
            await settings.WhenActionsIdleAsync().WaitAsync(lifetime.Token);
            SettleFixtureLayout(window);
            var saved = HomeNativePreferenceFile.Read(file);
            var choice = HomeNativePreferenceFile.Resolve(saved, scope, UserPreferencesService.ProjectLegacyHomeTheme);
            Assert.Equal("#7145FF", choice.Accent!.AllAppsColour);
            Assert.Equal(HomeThemeAccentOverrideMode.None, choice.Accent.Mode);
            Assert.Equal("retained", saved["defaultModel"]!.GetValue<string>());
            Assert.True(settings.LastWrite?.ConfirmedWrite?.Succeeded);
            var feedback = SettingsControl<TextBlock>(settings, "home-palette-status");
            Assert.True(feedback.IsEffectivelyVisible);
            Assert.Equal("Appearance saved.", feedback.Text);
            var afterChoice = await File.ReadAllBytesAsync(file);
            back.BringIntoView();
            SettleFixtureLayout(window);
            Assert.True(back.Focus(NavigationMethod.Tab));
            PressSettingsKey(window, PhysicalKey.Enter);
            await settings.WhenActionsIdleAsync().WaitAsync(lifetime.Token);
            SettleFixtureLayout(window);
            Assert.Equal("Theme and accent preferences", settings.CategoryTitle);
            Assert.True(SettingsControl<CuiContainerControl>(settings, "home-theme-gallery").IsEffectivelyVisible);
            Assert.True(SettingsControl<Button>(settings, "home-settings-theme").IsFocused);
            Assert.Equal(afterChoice, await File.ReadAllBytesAsync(file));
        }
        finally
        {
            await session.CloseAndDrainAsync();
            window.Close();
            Directory.Delete(paths.DataDirectory, recursive: true);
        }
    }

    [AvaloniaTheory]
    [InlineData(520, 650)]
    [InlineData(1280, 900)]
    public async Task Theme_Options_navigation_reuses_the_live_appearance_controls_and_saved_preferences(int width, int height)
    {
        var limit = TimeSpan.FromSeconds(20);
        var directory = Directory.CreateTempSubdirectory("home-theme-options-");
        var file = Path.Combine(directory.FullName, "preferences.json");
        await File.WriteAllTextAsync(file, "{\"havenUiThemeName\":\"Professional\",\"havenUiAppearance\":2,\"defaultModel\":\"retained-model\"}");
        var scope = new HomeThemeScope(HomeDataScope.DeviceLocal, "original-home-profile");
        var source = new NativeHomeThemePreferences(file, scope, UserPreferencesService.ProjectLegacyHomeTheme,
            _ => ValueTask.FromResult(true), callback => Dispatcher.UIThread.Post(callback));
        using var lifetime = new CancellationTokenSource();
        var session = Assert.IsAssignableFrom<IBrowseNativeAppContentSession>(Factory(new(), lifetime.Token,
            settings: _ => new(source, _ => Task.CompletedTask)).CreateSession(Request()));
        var window = new Window { Width = width, Height = height };
        try
        {
            await session.InitializeAsync().WaitAsync(limit);
            var presentation = Assert.IsType<OriginalTabTransientPresentation>(session.OriginalNativeView);
            var home = Assert.IsType<UnifiedHomePage>(presentation.OriginalContent);
            window.Content = presentation;
            window.Show();
            SettleFixtureLayout(window);
            var gear = HomeRailButton(home, "home-settings", "Settings");
            ClickSettingsButton(window, gear);
            await home.WhenActionsIdleAsync().WaitAsync(limit);
            SettleFixtureLayout(window);
            var settings = Assert.Single(home.GetVisualDescendants().OfType<HomeSettingsDestinationPage>());
            var search = SettingsControl<TextBox>(settings, "home-settings-search");
            var gallery = SettingsControl<CuiContainerControl>(settings, "home-theme-gallery");
            var display = SettingsControl<CuiContainerControl>(settings, "home-display-settings");
            var accents = SettingsControl<CuiContainerControl>(settings, "home-accent-settings");
            var brightness = SettingsControl<Slider>(settings, "home-brightness");
            brightness.BringIntoView();
            SettleFixtureLayout(window);
            Assert.True(brightness.Focus());
            var savedLevel = brightness.Value;
            PressSettingsKey(window, PhysicalKey.ArrowRight);
            var pendingLevel = brightness.Value;
            Assert.True(pendingLevel > savedLevel);

            await SearchAsync("Theme Options");
            var options = SettingsControl<Button>(settings, "home-settings-theme-options");
            Assert.True(options.IsFocused);
            Assert.Equal("Current page", AutomationProperties.GetItemStatus(options));
            Assert.True(SettingsCategorySelected(options));
            var optionsPage = SettingsControl<CuiContainerControl>(settings, "home-settings-theme-options-page");
            Assert.Equal("Theme Options", SettingsControl<TextBlock>(optionsPage, "home-settings-theme-options-title").Text);
            Assert.False(gallery.IsEffectivelyVisible && gallery.IsAttachedToVisualTree());
            Assert.False(display.IsEffectivelyVisible && display.IsAttachedToVisualTree());
            Assert.False(accents.IsEffectivelyVisible && accents.IsAttachedToVisualTree());
            Assert.Equal(pendingLevel, brightness.Value);
            CaptureSettingsIfRequested(window, "theme-options", source);

            var light = Assert.Single(optionsPage.GetVisualDescendants().OfType<Button>(), button =>
                button.Name == "home-theme-options-mode-choice" && AutomationProperties.GetName(button) == "Light");
            await ActivateAsync(light);
            Assert.True(settings.LastWrite?.ConfirmedWrite?.Succeeded == true);
            var saved = HomeNativePreferenceFile.Read(file);
            var displaySettings = HomeNativePreferenceFile.Resolve(saved, scope,
                UserPreferencesService.ProjectLegacyHomeTheme).EffectiveDisplay();
            Assert.Equal(HomeDisplayMode.Light, displaySettings.Mode);
            Assert.Equal((int)savedLevel, displaySettings.Brightness);
            Assert.Equal("retained-model", saved["defaultModel"]!.GetValue<string>());
            Assert.Equal(pendingLevel, brightness.Value);

            await ActivateAsync(SettingsControl<Button>(optionsPage, "home-theme-options-palette"));
            var theme = SettingsControl<Button>(settings, "home-settings-theme");
            var header = SettingsControl<CuiContainerControl>(settings, "home-palette-header");
            var back = SettingsControl<Button>(settings, "home-palette-back");
            Assert.True(header.IsVisible && back.IsFocused);
            Assert.True(SettingsCategorySelected(theme));
            Assert.Equal("Accent palette", settings.CategoryTitle);
            Assert.Equal(string.Empty, search.Text);
            Assert.False(gallery.IsEffectivelyVisible);
            Assert.False(display.IsEffectivelyVisible);
            Assert.True(accents.IsEffectivelyVisible);
            Assert.Same(accents, SettingsControl<CuiContainerControl>(settings, "home-accent-settings"));
            var savedBeforeBack = await File.ReadAllBytesAsync(file);
            await ActivateAsync(back);
            Assert.True(theme.IsFocused);
            Assert.True(gallery.IsEffectivelyVisible);
            Assert.True(display.IsEffectivelyVisible);
            Assert.False(header.IsVisible);
            Assert.DoesNotContain(settings.GetVisualDescendants().OfType<Button>(),
                item => item.Name == "home-palette-back" && item.IsEffectivelyVisible && item.IsAttachedToVisualTree());
            Assert.False(settings.IsActionAvailable("SettingsPaletteBack"));
            Assert.Same(brightness, SettingsControl<Slider>(settings, "home-brightness"));
            Assert.Equal(pendingLevel, brightness.Value);
            Assert.Equal(savedBeforeBack, await File.ReadAllBytesAsync(file));

            await SearchAsync("Theme Options");
            await ActivateAsync(SettingsControl<Button>(settings, "home-theme-options-accessibility"));
            var accessibility = SettingsControl<Button>(settings, "home-settings-accessibility");
            Assert.True(accessibility.IsFocused);
            Assert.True(SettingsCategorySelected(accessibility));
            Assert.Equal(string.Empty, search.Text);
            Assert.True(SettingsControl<CuiContainerControl>(settings, "home-appearance-accessibility").IsEffectivelyVisible);

            await SearchAsync("Accent palette");
            Assert.True(SettingsCategorySelected(SettingsControl<Button>(settings, "home-settings-theme-options")));
            await ActivateAsync(SettingsControl<Button>(settings, "home-settings-back"));
            await home.WhenActionsIdleAsync().WaitAsync(limit);
            SettleFixtureLayout(window);
            Assert.True(gear.IsFocused);
            PressSettingsKey(window, PhysicalKey.Enter);
            await home.WhenActionsIdleAsync().WaitAsync(limit);
            SettleFixtureLayout(window);
            Assert.Same(settings, Assert.Single(home.GetVisualDescendants().OfType<HomeSettingsDestinationPage>()));
            Assert.Same(gallery, SettingsControl<CuiContainerControl>(settings, "home-theme-gallery"));
            Assert.True(SettingsCategorySelected(SettingsControl<Button>(settings, "home-settings-theme")));
            Assert.Equal(pendingLevel, brightness.Value);

            async Task SearchAsync(string query)
            {
                Assert.True(search.Focus());
                search.Text = query;
                SettleFixtureLayout(window);
                PressSettingsKey(window, PhysicalKey.Enter);
                await settings.WhenActionsIdleAsync().WaitAsync(limit);
                SettleFixtureLayout(window);
            }

            async Task ActivateAsync(Button button)
            {
                button.BringIntoView();
                SettleFixtureLayout(window);
                Assert.True(button.Focus(NavigationMethod.Tab));
                PressSettingsKey(window, PhysicalKey.Enter);
                await settings.WhenActionsIdleAsync().WaitAsync(limit);
                SettleFixtureLayout(window);
            }
        }
        finally
        {
            try { await session.CloseAndDrainAsync().WaitAsync(limit); }
            finally
            {
                window.Close();
                await source.DisposeAsync();
                directory.Delete(recursive: true);
            }
        }
    }
}
