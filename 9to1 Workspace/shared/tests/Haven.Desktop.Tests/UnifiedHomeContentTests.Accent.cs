using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CakeOS.Cui.Runtime;
using Haven.Desktop.Services;
using Haven.Desktop.Views.Pages.Home;
using HavenOS.Home.Core;

namespace Haven.Desktop.Tests;

public sealed partial class UnifiedHomeContentTests
{
    [AvaloniaFact]
    public async Task Exact_accent_picker_cancels_and_commits_only_its_selected_app()
    {
        var directory = Directory.CreateTempSubdirectory("home-accent-picker-");
        var path = Path.Combine(directory.FullName, "preferences.json");
        var scope = new HomeThemeScope(HomeDataScope.DeviceLocal, "accent-picker");
        var token = TestContext.Current.CancellationToken;
        var original = new HomeThemeAccentPreference(HomeThemeAccentOverrideMode.IndividualApps, "#2455FF")
            .WithAppColour("app.write", "#123456").WithAppColour("app.present", "#ABCDEF");
        var seed = HomeThemePreference.Default(scope) with { Revision = 1, Appearance = "Dark", Accent = original };
        Assert.True((await HomeNativePreferenceFile.CompareExchangeAsync(path, scope, 0, seed,
            UserPreferencesService.ProjectLegacyHomeTheme, _ => ValueTask.FromResult(true), token)).Succeeded);
        await using var source = new NativeHomeThemePreferences(path, scope, UserPreferencesService.ProjectLegacyHomeTheme,
            _ => ValueTask.FromResult(true), callback => Dispatcher.UIThread.Post(callback));
        var settings = new HomeSettingsDestinationPage(source, _ => Task.CompletedTask);
        Window? window = null;
        try
        {
            await settings.ActivateAsync(token);
            window = Show(settings);
            var fragment = SettingsControl<CuiContainerControl>(settings, "home-appearance-settings");
            Assert.IsType<ScrollViewer>(fragment.Child).ScrollToEnd();
            SettleFixtureLayout(window);
            var scopeSelection = SettingsControl<ComboBox>(settings, "home-accent-app");
            Assert.Equal("Write", scopeSelection.SelectedItem);
            scopeSelection.SelectedIndex = scopeSelection.Items.IndexOf("Present");
            Assert.Equal("Present", scopeSelection.SelectedItem);
            var trigger = SettingsControl<Button>(settings, "home-edit-accent");
            var flyout = Assert.IsType<Flyout>(trigger.Flyout);
            Assert.True(trigger.Focus());
            PressSettingsKey(window, PhysicalKey.Enter);
            Dispatcher.UIThread.RunJobs();
            Assert.True(flyout.IsOpen);
            var panel = Assert.IsAssignableFrom<Control>(flyout.Content);
            var hex = Assert.Single(panel.GetVisualDescendants().OfType<TextBox>());
            Assert.Equal("#ABCDEF", hex.Text);
            Assert.True(hex.IsFocused);
            var beforeCancel = await File.ReadAllBytesAsync(path, token);
            hex.Text = "#001122";
            hex.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape });
            Dispatcher.UIThread.RunJobs();
            Assert.False(flyout.IsOpen);
            Assert.True(trigger.IsFocused);
            Assert.Equal(beforeCancel, await File.ReadAllBytesAsync(path, token));

            PressSettingsKey(window, PhysicalKey.Enter);
            Dispatcher.UIThread.RunJobs();
            var apply = Assert.Single(panel.GetVisualDescendants().OfType<Button>(),
                button => AutomationProperties.GetName(button) == "Apply colour" || Equals(button.Content, "Apply colour"));
            hex.Text = "invalid";
            Assert.False(apply.IsEnabled);
            hex.Text = "#11aacc";
            Assert.True(apply.IsEnabled);
            Assert.True(apply.Focus());
            apply.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await settings.WhenActionsIdleAsync().WaitAsync(TimeSpan.FromSeconds(20), token);
            SettleFixtureLayout(window);
            Assert.False(flyout.IsOpen);
            Assert.Equal(original.WithAppColour("app.present", "#11AACC"), settings.LastWrite!.ConfirmedWrite!.Value!.Preference.Accent);
            Assert.Equal(2, settings.LastWrite.ConfirmedWrite!.Value!.Preference.Revision);

            var inherit = SettingsControl<Button>(settings, "home-accent-reset");
            Assert.True(inherit.Focus());
            PressSettingsKey(window, PhysicalKey.Enter);
            await settings.WhenActionsIdleAsync().WaitAsync(TimeSpan.FromSeconds(20), token);
            Assert.Equal(original.WithAppColour("app.present", null), settings.LastWrite!.ConfirmedWrite!.Value!.Preference.Accent);
        }
        finally
        {
            try { window?.Close(); await settings.CloseAndDrainAsync(); }
            finally { directory.Delete(recursive: true); }
        }
    }
}
