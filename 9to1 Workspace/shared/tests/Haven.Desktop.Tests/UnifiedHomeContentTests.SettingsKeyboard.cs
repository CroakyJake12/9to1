using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
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
    [AvaloniaTheory]
    [InlineData(1440, 900)]
    [InlineData(720, 520)]
    public async Task Settings_keyboard_reference_opens_actual_atlas_sections_without_changing_profile(int width, int height)
    {
        using var lifetime = new CancellationTokenSource();
        var paths = new LayoutPaths();
        var file = Path.Combine(paths.DataDirectory, "preferences.json");
        var original = """{"havenUiThemeName":"Professional","havenUiAppearance":2,"defaultModel":"keep"}"""u8.ToArray();
        Directory.CreateDirectory(paths.DataDirectory);
        await File.WriteAllBytesAsync(file, original);
        await using var source = new NativeHomeThemePreferences(file, new(HomeDataScope.DeviceLocal, "original-home-profile"),
            UserPreferencesService.ProjectLegacyHomeTheme, _ => ValueTask.FromResult(true), action => Dispatcher.UIThread.Post(action));
        var session = Assert.IsAssignableFrom<IBrowseNativeAppContentSession>(Factory(new(), lifetime.Token,
            settings: _ => new(source, _ => Task.CompletedTask)).CreateSession(Request("browse://home/settings/settings-haven-shortcuts")));
        Window? window = null;
        try
        {
            await session.InitializeAsync(lifetime.Token);
            window = new Window { Width = width, Height = height, Content = session.OriginalNativeView };
            window.Show();
            SettleFixtureLayout(window);
            var settings = Assert.Single(window.GetVisualDescendants().OfType<HomeSettingsDestinationPage>());
            var search = SettingsControl<TextBox>(settings, "home-settings-search");

            VerifyReference("Keyboard Shortcuts", "Keyboard essentials", "Next control", "Tab", "Previous control", "Shift + Tab",
                "Close a popover", "Esc", "Activate", "Enter / Space");
            await OpenFromSearch("Navigation");
            VerifyReference("Navigation", "Navigation keys", "Next control", "Tab", "Previous control", "Shift + Tab");
            await OpenFromSearch("Menus & focus");
            VerifyReference("Menus & focus", "Menus and controls", "Close a popover", "Esc", "Activate", "Enter / Space");
            await OpenFromSearch("Keyboard Shortcuts");
            VerifyReference("Keyboard Shortcuts", "Keyboard essentials", "Next control", "Tab", "Previous control", "Shift + Tab",
                "Close a popover", "Esc", "Activate", "Enter / Space");
            Assert.Equal(original, await File.ReadAllBytesAsync(file));

            async Task OpenFromSearch(string value)
            {
                search.Text = value;
                Assert.True(search.Focus());
                PressSettingsKey(window, PhysicalKey.Enter);
                await settings.WhenActionsIdleAsync();
                SettleFixtureLayout(window);
            }

            void VerifyReference(string category, string section, params string[] expected)
            {
                Assert.Equal(category, settings.CategoryTitle);
                var heading = SettingsControl<TextBlock>(settings, "home-settings-keyboard-section");
                Assert.Equal(section, heading.Text);
                var map = heading.GetVisualAncestors().OfType<Control>().First(control => control.Name == "home-settings-keyboard-map");
                Assert.True(map.IsEffectivelyVisible);
                var labels = map.GetVisualDescendants().OfType<TextBlock>().Where(label => !ReferenceEquals(heading, label))
                    .Select(label => label.Text).ToArray();
                Assert.Equal(expected, labels);
                Assert.Empty(map.GetVisualDescendants().OfType<Button>());
            }
        }
        finally
        {
            await session.CloseAndDrainAsync();
            window?.Close();
            Directory.Delete(paths.DataDirectory, recursive: true);
        }
    }
}
