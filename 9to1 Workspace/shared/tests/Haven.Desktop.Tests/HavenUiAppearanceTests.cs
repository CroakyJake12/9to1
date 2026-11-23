using Avalonia.Headless.XUnit;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Styling;
using CakeOS.Cui.Themes;
using Avalonia.VisualTree;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.Controls;
using Haven.Desktop.Views.Pages.Settings;
using HavenOS.Home.Core;

namespace Haven.Desktop.Tests;

public sealed class HavenUiAppearanceTests
{
    [Fact]
    public void Appearance_enum_matches_the_four_slider_positions()
    {
        Assert.Equal(
            [HavenUiAppearance.SuperBright, HavenUiAppearance.Bright, HavenUiAppearance.Dark, HavenUiAppearance.SuperDark],
            Enum.GetValues<HavenUiAppearance>());
    }

    [Fact]
    public void Fresh_install_reads_and_persists_professional_dark()
    {
        using var paths = new TemporaryPaths();
        var path = Path.Combine(paths.DataDirectory, "preferences.json");
        var scope = new HomeThemeScope(HomeDataScope.DeviceLocal, "fresh-appearance");
        var projected = HomeNativePreferenceFile.Resolve(HomeNativePreferenceFile.Read(path), scope,
            UserPreferencesService.ProjectLegacyHomeTheme);

        var preferences = new UserPreferencesService(paths);
        var saved = HomeNativePreferenceFile.Read(path);

        Assert.Equal("Professional", projected.ThemeName);
        Assert.Equal("Dark", projected.Appearance);
        Assert.Equal(HomeDisplayMode.Dark, projected.EffectiveDisplay().Mode);
        Assert.Equal(HavenUiAppearance.Dark, preferences.Appearance);
        Assert.Equal("haven-ui", preferences.ThemeId);
        Assert.Equal("Professional", saved["havenUiThemeName"]!.GetValue<string>());
        Assert.Equal(2, saved["havenUiAppearance"]!.GetValue<int>());
        Assert.Equal(HavenUiAppearance.Dark, new UserPreferencesService(paths).Appearance);
    }

    [Theory]
    [InlineData(HavenUiAppearance.SuperBright, 0)]
    [InlineData(HavenUiAppearance.Bright, 1)]
    [InlineData(HavenUiAppearance.Dark, 2)]
    [InlineData(HavenUiAppearance.SuperDark, 3)]
    public void Explicit_appearance_survives_versionless_legacy_theme_migration(HavenUiAppearance expected, int ordinal)
    {
        using var paths = new TemporaryPaths();
        var path = Path.Combine(paths.DataDirectory, "preferences.json");
        File.WriteAllText(path, $$"""
            { "themeId": "light", "havenUiThemeName": "Glow", "havenUiAppearance": {{ordinal}}, "extra": "keep" }
            """);
        var scope = new HomeThemeScope(HomeDataScope.DeviceLocal, "saved-appearance");

        var preferences = new UserPreferencesService(paths);
        var saved = HomeNativePreferenceFile.Read(path);
        var projected = HomeNativePreferenceFile.Resolve(saved, scope, UserPreferencesService.ProjectLegacyHomeTheme);

        Assert.Equal(expected, preferences.Appearance);
        Assert.Equal(expected, new UserPreferencesService(paths).Appearance);
        Assert.Equal(expected.ToString(), projected.Appearance);
        Assert.Equal("Glow", projected.ThemeName);
        Assert.Equal(ordinal, saved["havenUiAppearance"]!.GetValue<int>());
        Assert.Equal("keep", saved["extra"]!.GetValue<string>());
    }

    [Fact]
    public async Task Legacy_startup_preserves_existing_native_display_and_accent()
    {
        using var paths = new TemporaryPaths();
        var path = Path.Combine(paths.DataDirectory, "preferences.json");
        var scope = new HomeThemeScope(HomeDataScope.DeviceLocal, "native-display");
        var display = new HomeDisplaySettings(HomeDisplayMode.FollowSystem, 61, 47);
        var accent = new HomeThemeAccentPreference(HomeThemeAccentOverrideMode.AllApps, "#A1B2C3")
            .WithAppColour("app.write", "#2455FF");
        var committed = await HomeNativePreferenceFile.CompareExchangeAsync(path, scope, 0,
            HomeThemePreference.Default(scope) with { Revision = 1, Display = display, Accent = accent },
            UserPreferencesService.ProjectLegacyHomeTheme, _ => ValueTask.FromResult(true), CancellationToken.None);
        Assert.True(committed.Succeeded);

        var preferences = new UserPreferencesService(paths);
        preferences.SetDefaultTabAppKey("personal.mode");
        var saved = HomeNativePreferenceFile.Read(path);
        var projected = HomeNativePreferenceFile.Resolve(saved, scope, UserPreferencesService.ProjectLegacyHomeTheme);

        Assert.False(saved.ContainsKey("havenUiAppearance"));
        Assert.Null(projected.Appearance);
        Assert.Equal(display, projected.Display);
        Assert.Equal(accent, projected.Accent);

        preferences.ApplyAppearance(HavenUiAppearance.SuperDark);
        saved = HomeNativePreferenceFile.Read(path);
        projected = HomeNativePreferenceFile.Resolve(saved, scope, UserPreferencesService.ProjectLegacyHomeTheme);
        Assert.Equal(3, saved["havenUiAppearance"]!.GetValue<int>());
        Assert.Equal("SuperDark", projected.Appearance);
        Assert.Equal(display with { Mode = HomeDisplayMode.Dark }, projected.Display);
        Assert.Equal(accent, projected.Accent);
    }

    [Fact]
    public void Default_tab_preference_round_trips_without_rewriting_an_unavailable_mode()
    {
        using var paths = new TemporaryPaths();
        var preferences = new UserPreferencesService(paths);

        preferences.SetDefaultTabAppKey("personal.mode.which.may.sync.later");

        var reloaded = new UserPreferencesService(paths);
        Assert.Equal("personal.mode.which.may.sync.later", reloaded.DefaultTabAppKey);
    }

    [AvaloniaFact]
    public void Appearance_persists_and_updates_the_canonical_semantic_resources()
    {
        using var paths = new TemporaryPaths();
        var preferences = new UserPreferencesService(paths);

        preferences.ApplyAppearance(HavenUiAppearance.SuperDark);

        Assert.Equal(HavenUiAppearance.SuperDark, preferences.Appearance);
        Assert.Equal(ThemeVariant.Dark, Avalonia.Application.Current!.RequestedThemeVariant);
        AssertBrush("HavenBackgroundPrimaryBrush");
        AssertBrush("HavenTextPrimaryBrush");
        AssertBrush("HavenTextDisabledBrush");
        AssertBrush("HavenBorderSubtleBrush");
        AssertBrush("HavenSuccessBrush");
        AssertBrush("HavenInformationBrush");
        AssertBrush("HavenAccentForegroundBrush");

        var reloaded = new UserPreferencesService(paths);
        Assert.Equal(HavenUiAppearance.SuperDark, reloaded.Appearance);
        Assert.Equal("haven-ui", reloaded.ThemeId);
    }

    [AvaloniaFact]
    public void Preference_changes_republish_live_CUI_palette_without_losing_selected_theme()
    {
        using var paths = new TemporaryPaths();
        var preferences = new UserPreferencesService(paths);
        preferences.ApplyAccentOverride(true, "Blue", save: false);
        var steps = new[]
        {
            (HavenUiAppearance.Bright, "Glow", CuiTheme.Glow),
            (HavenUiAppearance.Dark, "Professional", CuiTheme.Professional),
            (HavenUiAppearance.Bright, "Glow", CuiTheme.Glow)
        };

        foreach (var (appearance, themeName, theme) in steps)
        {
            preferences.ApplyThemeChoice(themeName, save: false);
            preferences.ApplyAppearance(appearance, save: false);

            var cuiAppearance = (CuiAppearance)appearance;
            Assert.Equal(cuiAppearance.ToString(), Avalonia.Application.Current!.Resources["CuiAppearance"]);
            var expectedBackground = CuiSurfacePaletteCatalog.For("Home", cuiAppearance, theme).TideBase;
            Assert.Equal(expectedBackground,
                Assert.IsAssignableFrom<ISolidColorBrush>(Avalonia.Application.Current.Resources["CuiBackgroundBrush"]).Color);
            Assert.Equal(expectedBackground,
                Assert.IsAssignableFrom<ISolidColorBrush>(Avalonia.Application.Current.Resources["HavenBackgroundPrimaryBrush"]).Color);
            Assert.Equal(theme, CuiSurfacePaletteCatalog.ActiveTheme);
            Assert.True(CuiSurfacePaletteCatalog.OverrideAccent);
            Assert.Equal(CuiAccentColour.Blue, CuiSurfacePaletteCatalog.AccentOverride);
        }
    }

    [AvaloniaFact]
    public void CUI_owned_window_surface_navigation_does_not_republish_legacy_Haven_palette_globally()
    {
        using var paths = new TemporaryPaths();
        var preferences = new UserPreferencesService(paths);
        preferences.ApplyThemeChoice("Glow", save: false);
        preferences.ApplyAppearance(HavenUiAppearance.Bright, save: false);

        var app = Avalonia.Application.Current!;
        var homeBackground = CuiSurfacePaletteCatalog.For("Home", CuiAppearance.Bright, CuiTheme.Glow).TideBase;
        var browseBackground = SurfacePaletteCatalog.For(HavenSurface.Browse, HavenUiAppearance.Dark).TideBase;
        Assert.NotEqual(homeBackground, browseBackground);
        var cUIBrush = Assert.IsAssignableFrom<ISolidColorBrush>(app.Resources["CuiBackgroundBrush"]);
        Assert.Equal(homeBackground, cUIBrush.Color);
        Assert.Same(cUIBrush, app.Resources["HavenBackgroundPrimaryBrush"]);

        var window = new Window();
        using var tidal = new TidalBackground(window, HavenUiAppearance.Dark, publishHavenPalette: false);
        tidal.SetSurface(HavenSurface.Browse);

        // Browse has its own legacy HUI palette. The CUI-owned MainWindow uses
        // TidalBackground only for its animated gradient, so navigation cannot
        // recolor the globally aliased CUI Home background through a shared brush.
        Assert.Equal(homeBackground, cUIBrush.Color);
        Assert.Equal(homeBackground,
            Assert.IsAssignableFrom<ISolidColorBrush>(app.Resources["CuiBackgroundBrush"]).Color);
    }

    [AvaloniaFact]
    public void Legacy_dark_theme_preference_migrates_to_dark_HavenUi_without_data_loss()
    {
        using var paths = new TemporaryPaths();
        File.WriteAllText(
            Path.Combine(paths.DataDirectory, "preferences.json"),
            """
            {
              "themeId": "obsidian",
              "defaultModel": "qwen3:4b",
              "temperature": 0.55
            }
            """);

        var migrated = new UserPreferencesService(paths);

        Assert.Equal(HavenUiAppearance.SuperDark, migrated.Appearance);
        Assert.Equal("haven-ui", migrated.ThemeId);
        Assert.Equal("qwen3:4b", migrated.DefaultModel);
        Assert.Equal(0.55d, migrated.GenerationOptions.Temperature, precision: 2);
    }

    [AvaloniaFact]
    public void Settings_control_exposes_exactly_four_snap_positions_and_applies_selection()
    {
        using var paths = new TemporaryPaths();
        var preferences = new UserPreferencesService(paths);
        var view = new HavenAppearanceSettingsView(preferences);
        var window = new Window { Content = view };
        try
        {
            window.Show();
            var slider = Assert.Single(view.GetVisualDescendants().OfType<Slider>());
            Assert.Equal(0, slider.Minimum);
            Assert.Equal(3, slider.Maximum);
            Assert.Equal(1, slider.TickFrequency);
            Assert.True(slider.IsSnapToTickEnabled);

            var expectedLabels = new[] { "Super Bright", "Bright", "Dark", "Super Dark" };
            var buttons = view.GetVisualDescendants()
                .OfType<Button>()
                .Where(item => item.Content is string label && expectedLabels.Contains(label, StringComparer.Ordinal))
                .ToArray();
            Assert.Equal(expectedLabels, buttons.Select(item => item.Content));
            buttons[3].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(HavenUiAppearance.SuperDark, preferences.Appearance);
        }
        finally
        {
            window.Close();
        }
    }

    private static void AssertBrush(string key)
    {
        Assert.True(Avalonia.Application.Current!.Resources.TryGetValue(key, out var value));
        Assert.IsType<SolidColorBrush>(value);
    }

    private sealed class TemporaryPaths : IAppPaths, IDisposable
    {
        public TemporaryPaths()
        {
            DataDirectory = Path.Combine(Path.GetTempPath(), "haven-ui-appearance-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(DataDirectory);
        }

        public string DataDirectory { get; }
        public string DatabasePath => Path.Combine(DataDirectory, "haven.db");
        public string BrowserProfileDirectory => Path.Combine(DataDirectory, "browser");
        public string AttachmentsDirectory => Path.Combine(DataDirectory, "attachments");
        public string LogsDirectory => Path.Combine(DataDirectory, "logs");
        public string LegacyStatePath => Path.Combine(DataDirectory, "legacy.json");

        public void Dispose()
        {
            try { Directory.Delete(DataDirectory, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
