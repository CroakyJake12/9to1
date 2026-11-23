using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using CakeOS.Cui.Runtime;
using Haven.Core;
using Haven.Desktop.Services;
using Haven.Desktop.Views.Pages.Home;
using HavenOS.Home.Core;

namespace Haven.Desktop.Tests;

public sealed partial class UnifiedHomeContentTests
{
    [AvaloniaTheory]
    [InlineData(1440, 900)]
    [InlineData(720, 520)]
    public async Task Accessibility_interface_scale_previews_reverts_and_commits_once_with_keyboard(int width, int height)
    {
        using var lifetime = new CancellationTokenSource();
        var token = lifetime.Token;
        var directory = Directory.CreateTempSubdirectory("home-interface-scale-");
        var path = Path.Combine(directory.FullName, "preferences.json");
        var scope = new HomeThemeScope(HomeDataScope.DeviceLocal, "interface-scale");
        var seed = HomeThemePreference.Default(scope) with
        {
            Revision = 1,
            Appearance = "Dark",
            Accessibility = new(ReduceMotion: true)
        };
        Assert.True((await HomeNativePreferenceFile.CompareExchangeAsync(path, scope, 0, seed,
            UserPreferencesService.ProjectLegacyHomeTheme, _ => ValueTask.FromResult(true), token)).Succeeded);
        var initial = await File.ReadAllBytesAsync(path, token);
        await using var source = new NativeHomeThemePreferences(path, scope, UserPreferencesService.ProjectLegacyHomeTheme,
            _ => ValueTask.FromResult(true), callback => Avalonia.Threading.Dispatcher.UIThread.Post(callback));
        var settings = new HomeSettingsDestinationPage(source, _ => Task.CompletedTask);
        settings.SetInitialRoute(AppContentAddress.Parse("browse://home/settings/settings-accessibility"));
        var window = new Window { Width = width, Height = height, Content = settings };
        try
        {
            await settings.ActivateAsync(token);
            window.Show();
            SettleFixtureLayout(window);
            var picker = SettingsControl<ComboBox>(settings, "home-settings-interface-scale");
            Assert.Equal("100%", picker.SelectedItem);
            Assert.False(settings.IsActionAvailable("SettingsInterfaceScaleApply"));
            Assert.False(settings.IsActionAvailable("SettingsInterfaceScaleRevert"));
            Assert.True(picker.Focus());

            PressSettingsKey(window, PhysicalKey.ArrowDown);
            await settings.WhenActionsIdleAsync().WaitAsync(TimeSpan.FromSeconds(20), token);
            SettleFixtureLayout(window);
            Assert.Equal("125%", picker.SelectedItem);
            Assert.Equal(1, source.Current.Accessibility.DisplayScale);
            Assert.Equal(1.25, source.Context.Current.Accessibility.DisplayScale);
            Assert.Equal(initial, await File.ReadAllBytesAsync(path, token));
            Assert.Equal(1.25, CuiInterfaceScale.For(picker));
            Assert.True(picker.IsFocused);
            var revert = SettingsControl<Button>(settings, "home-settings-interface-scale-revert");
            PressSettingsKey(window, PhysicalKey.Tab);
            Assert.True(revert.IsFocused);
            PressSettingsKey(window, PhysicalKey.Enter);
            SettleFixtureLayout(window);
            Assert.True(picker.IsFocused);
            Assert.Equal("100%", picker.SelectedItem);
            Assert.Equal(1, source.Context.Current.Accessibility.DisplayScale);
            Assert.Equal(initial, await File.ReadAllBytesAsync(path, token));

            PressSettingsKey(window, PhysicalKey.ArrowDown);
            await settings.WhenActionsIdleAsync().WaitAsync(TimeSpan.FromSeconds(20), token);
            PressSettingsKey(window, PhysicalKey.ArrowDown);
            await settings.WhenActionsIdleAsync().WaitAsync(TimeSpan.FromSeconds(20), token);
            PressSettingsKey(window, PhysicalKey.ArrowDown);
            await settings.WhenActionsIdleAsync().WaitAsync(TimeSpan.FromSeconds(20), token);
            SettleFixtureLayout(window);
            Assert.Equal("175%", picker.SelectedItem);
            Assert.Equal(1.75, CuiInterfaceScale.For(picker));
            if (width == 720)
            {
                var directoryView = SettingsControl<CuiContainerControl>(settings, "home-settings-categories");
                var page = SettingsControl<CuiContainerControl>(settings, "home-settings-accessibility-page");
                Assert.True(Assert.IsType<ScrollViewer>(directoryView.Child).Viewport.Height > 0);
                Assert.True(Assert.IsType<ScrollViewer>(page.Child).Viewport.Height > 0);
            }
            Assert.True(picker.IsFocused);
            PressSettingsKey(window, PhysicalKey.Escape);
            SettleFixtureLayout(window);
            Assert.Equal("100%", picker.SelectedItem);
            Assert.Equal(initial, await File.ReadAllBytesAsync(path, token));
            Assert.True(picker.IsFocused);

            PressSettingsKey(window, PhysicalKey.ArrowDown);
            await settings.WhenActionsIdleAsync().WaitAsync(TimeSpan.FromSeconds(20), token);
            SettleFixtureLayout(window);
            var apply = SettingsControl<Button>(settings, "home-settings-interface-scale-apply");
            PressSettingsKey(window, PhysicalKey.Tab);
            PressSettingsKey(window, PhysicalKey.Tab);
            Assert.True(apply.IsFocused);
            PressSettingsKey(window, PhysicalKey.Enter);
            await settings.WhenActionsIdleAsync().WaitAsync(TimeSpan.FromSeconds(20), token);
            await source.RefreshAsync(token);
            SettleFixtureLayout(window);
            var saved = settings.LastWrite?.ConfirmedWrite?.Value?.Preference;
            Assert.NotNull(saved);
            Assert.Equal(2, saved.Revision);
            Assert.Equal(seed.Accessibility with { DisplayScale = 1.25 }, saved.Accessibility);
            Assert.Equal("125%", picker.SelectedItem);
            Assert.Equal(1.25, source.Current.Accessibility.DisplayScale);
            Assert.Equal(1.25, source.Context.Current.Accessibility.DisplayScale);
            Assert.True(picker.IsFocused);
            Assert.False(settings.IsActionAvailable("SettingsInterfaceScaleApply"));
            Assert.False(settings.IsActionAvailable("SettingsInterfaceScaleRevert"));
        }
        finally
        {
            try { window.Close(); await settings.CloseAndDrainAsync(); }
            finally { directory.Delete(recursive: true); }
        }
    }
}
