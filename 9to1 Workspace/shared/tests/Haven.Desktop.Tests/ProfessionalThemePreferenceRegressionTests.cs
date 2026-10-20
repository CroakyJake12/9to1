using Avalonia;
using Avalonia.Headless.XUnit;
using CakeOS.Cui.Themes;
using Haven.Core;
using Haven.Application;
using System.Text.Json.Nodes;
using Haven.Desktop;
using Haven.Desktop.HavenUI.Tokens;
using Haven.Desktop.Services;
using Xunit;

namespace Haven.Desktop.Tests;

/// <summary>
/// Focused, executable regression for the actual shipping preference path. It does
/// not pull in the broader PersonalisationTests baseline or unrelated donor
/// assumptions; it only proves canonical CUI-only theme choices survive a reload
/// and that an actual Glow choice remains Glow.
/// </summary>
public sealed class ProfessionalThemePreferenceRegressionTests
{
    [AvaloniaFact]
    public void Cui_only_theme_choices_survive_reload_and_glow_stays_glow()
    {
        var previousActiveTheme = CuiSurfacePaletteCatalog.ActiveTheme;
        var previousOverrideAccent = CuiSurfacePaletteCatalog.OverrideAccent;
        var previousAccentOverride = CuiSurfacePaletteCatalog.AccentOverride;

        var previousHavenTheme = HavenPersonalisation.Theme;
        var previousHavenOverrideAccent = HavenPersonalisation.OverrideAccent;
        var previousHavenAccent = HavenPersonalisation.Accent;
        var previousFont = HavenPersonalisation.FontFamilyName;
        var previousUserAvatar = HavenPersonalisation.UserAvatarEnabled;
        var previousHavenAvatar = HavenPersonalisation.HavenAvatarEnabled;

        var app = Avalonia.Application.Current;
        var previousResources = app?.Resources.ToDictionary(pair => pair.Key, pair => pair.Value);
        var previousVariant = app?.RequestedThemeVariant;

        try
        {
            foreach (var themeName in new[] { "Professional", "Frosted Glass", "Metallic", "Western" })
            {
                using var paths = new TemporaryPaths();
                {
                    var preferences = new UserPreferencesService(paths);
                    preferences.ApplyThemeChoice(themeName);
                    Assert.Equal(CuiThemeCatalog.Parse(themeName), CuiSurfacePaletteCatalog.ActiveTheme);
                    preferences.ApplyAppearance(HavenUiAppearance.Dark);
                    preferences.ApplyAccentOverride(true, "Cyan");
                    preferences.SetFontPreference(null);
                    Assert.Equal(CuiThemeCatalog.Parse(themeName), CuiSurfacePaletteCatalog.ActiveTheme);
                    var saved = JsonNode.Parse(File.ReadAllText(Path.Combine(paths.DataDirectory, "preferences.json")))!;
                    Assert.Equal(themeName, saved["havenUiThemeName"]!.GetValue<string>());
                    CuiSurfacePaletteCatalog.ActiveTheme = CuiTheme.Retro;
                }
                {
                    var reloaded = new UserPreferencesService(paths);
                    Assert.Equal(CuiThemeCatalog.Parse(themeName), CuiSurfacePaletteCatalog.ActiveTheme);
                }
            }

            using var glowPaths = new TemporaryPaths();
            {
                var preferences = new UserPreferencesService(glowPaths);
                preferences.ApplyThemeChoice("Glow");
                Assert.Equal(CuiTheme.Glow, CuiSurfacePaletteCatalog.ActiveTheme);
            }
            {
                var reloaded = new UserPreferencesService(glowPaths);
                Assert.Equal(CuiTheme.Glow, CuiSurfacePaletteCatalog.ActiveTheme);
            }
        }
        finally
        {
            CuiSurfacePaletteCatalog.ActiveTheme = previousActiveTheme;
            CuiSurfacePaletteCatalog.OverrideAccent = previousOverrideAccent;
            CuiSurfacePaletteCatalog.AccentOverride = previousAccentOverride;

            HavenPersonalisation.Theme = previousHavenTheme;
            HavenPersonalisation.OverrideAccent = previousHavenOverrideAccent;
            HavenPersonalisation.Accent = previousHavenAccent;
            HavenPersonalisation.FontFamilyName = previousFont;
            HavenPersonalisation.UserAvatarEnabled = previousUserAvatar;
            HavenPersonalisation.HavenAvatarEnabled = previousHavenAvatar;

            HavenUiFont.SetUserFamily(previousFont);
            if (app is not null && previousResources is not null)
            {
                foreach (var key in app.Resources.Keys.ToArray())
                    if (!previousResources.ContainsKey(key)) app.Resources.Remove(key);
                foreach (var pair in previousResources) app.Resources[pair.Key] = pair.Value;
                app.RequestedThemeVariant = previousVariant;
            }
            // Last: the historical setter in the baseline writes through. Cleanup must not
            // leave that baseline mutation behind when this regression intentionally fails.
            CuiSurfacePaletteCatalog.ActiveTheme = previousActiveTheme;
        }
    }

    private sealed class TemporaryPaths : IAppPaths, IDisposable
    {
        public TemporaryPaths()
        {
            DataDirectory = Path.Combine(Path.GetTempPath(), "haven-theme-regression-tests", Guid.NewGuid().ToString("N"));
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
