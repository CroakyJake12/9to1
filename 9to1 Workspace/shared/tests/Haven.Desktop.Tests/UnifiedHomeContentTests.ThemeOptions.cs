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
            Assert.True(theme.IsFocused);
            Assert.True(SettingsCategorySelected(theme));
            Assert.Equal(string.Empty, search.Text);
            Assert.Same(display, SettingsControl<CuiContainerControl>(settings, "home-display-settings"));
            Assert.Same(accents, SettingsControl<CuiContainerControl>(settings, "home-accent-settings"));
            var fragment = SettingsControl<CuiContainerControl>(settings, "home-appearance-settings");
            var scroll = Assert.IsType<ScrollViewer>(fragment.Child);
            Assert.True(scroll.Offset.Y > 0);
            var accentOrigin = accents.TranslatePoint(default, scroll)!.Value;
            Assert.InRange(accentOrigin.Y, -accents.Bounds.Height + 1, scroll.Viewport.Height - 1);
            Assert.Same(brightness, SettingsControl<Slider>(settings, "home-brightness"));
            Assert.Equal(pendingLevel, brightness.Value);

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
