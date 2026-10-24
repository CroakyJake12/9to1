using Avalonia;
using Avalonia.Headless.XUnit;
using CakeOS.Cui.Themes;
using Haven.Core;
using Haven.Application;
using System.Text.Json.Nodes;
using Haven.Desktop;
using Haven.Desktop.HavenUI.Tokens;
using Haven.Desktop.Services;
using HavenOS.Home.Core;
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
    [Fact]
    public void Motion_preference_load_distinguishes_absence_from_failed_or_oversized_reads()
    {
        var directory = Path.Combine(Path.GetTempPath(), "haven-motion-preference-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var absent = new MotionPreferencesService(Path.Combine(directory, "absent", "prefs.json"));
            Assert.False(absent.ReduceAnimations);

            var writablePath = Path.Combine(directory, "writable", "prefs.json");
            var writable = new MotionPreferencesService(writablePath);
            var successfulWrites = 0;
            writable.Changed += (_, _) => successfulWrites++;
            writable.SetReduceAnimations(true);
            Assert.True(writable.ReduceAnimations);
            Assert.Equal(1, successfulWrites);
            Assert.True(new MotionPreferencesService(writablePath).ReduceAnimations);

            var malformedPath = Path.Combine(directory, "malformed.json");
            File.WriteAllText(malformedPath, "{");
            Assert.True(new MotionPreferencesService(malformedPath).ReduceAnimations);

            var nullPath = Path.Combine(directory, "null.json");
            File.WriteAllText(nullPath, "null");
            Assert.True(new MotionPreferencesService(nullPath).ReduceAnimations);

            var oversizedPath = Path.Combine(directory, "oversized.json");
            File.WriteAllText(oversizedPath, new string('x', 65_537));
            Assert.True(new MotionPreferencesService(oversizedPath).ReduceAnimations);

            var inaccessiblePath = Path.Combine(directory, "unreadable-directory");
            Directory.CreateDirectory(inaccessiblePath);
            var failedSave = new MotionPreferencesService(inaccessiblePath);
            Assert.True(failedSave.ReduceAnimations);
            var notifications = 0;
            failedSave.Changed += (_, _) => notifications++;
            failedSave.SetReduceAnimations(false);
            Assert.True(failedSave.ReduceAnimations);
            Assert.Equal(0, notifications);
        }
        finally
        {
            try { Directory.Delete(directory, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    [AvaloniaFact]
    public async Task Home_individual_accent_effective_fallback_reaches_the_shared_cui_context()
    {
        using var paths = new TemporaryPaths();
        var preferencePath = Path.Combine(paths.DataDirectory, "preferences.json");
        var scope = new HomeThemeScope(HomeDataScope.DeviceLocal, "fixture-profile");
        var accent = new HomeThemeAccentPreference(HomeThemeAccentOverrideMode.IndividualApps,
            individualApps: new Dictionary<string, string>
            { ["app.present"] = "#DB3545", ["app.custom"] = "#37A169" });
        var saved = new HomeThemePreference(HomeThemePreference.CurrentSchemaVersion, scope,
            "Professional", 1, new()) { Accent = accent };

        var receipt = await HomeNativePreferenceFile.CompareExchangeAsync(preferencePath, scope, 0, saved,
            UserPreferencesService.ProjectLegacyHomeTheme,
            _ => ValueTask.FromResult(true), CancellationToken.None);
        Assert.True(receipt.Succeeded);

        await using var source = new NativeHomeThemePreferences(preferencePath, scope,
            UserPreferencesService.ProjectLegacyHomeTheme,
            _ => ValueTask.FromResult(true), callback => callback());
        await source.RefreshAsync();

        Assert.Equal("#DB3545", source.Current.EffectiveAccent.ResolveColour("app.present"));
        Assert.Equal("#37A169", source.Current.EffectiveAccent.ResolveColour("app.custom"));
        Assert.Equal("#2563EB", source.Current.EffectiveAccent.ResolveColour("app.write"));
        Assert.Equal(source.Current.EffectiveAccent.ResolveColour("app.write"),
            source.Context.Current.EffectiveAccent.ResolveColour("app.write"));
    }

    [AvaloniaFact]
    public async Task Home_to_cui_adapter_preserves_all_32_explicit_individual_accent_choices()
    {
        using var paths = new TemporaryPaths();
        var preferencePath = Path.Combine(paths.DataDirectory, "preferences.json");
        var scope = new HomeThemeScope(HomeDataScope.DeviceLocal, "fixture-full-accent-profile");
        var choices = Enumerable.Range(1, HomeThemeAccentPreference.MaximumIndividualAppChoices)
            .ToDictionary(index => $"app.custom-{index:D2}", index => "#" + index.ToString("X6"));
        var saved = new HomeThemePreference(HomeThemePreference.CurrentSchemaVersion, scope,
            "Professional", 1, new())
        { Accent = new(HomeThemeAccentOverrideMode.IndividualApps, individualApps: choices) };
        var receipt = await HomeNativePreferenceFile.CompareExchangeAsync(preferencePath, scope, 0, saved,
            UserPreferencesService.ProjectLegacyHomeTheme,
            _ => ValueTask.FromResult(true), CancellationToken.None);
        Assert.True(receipt.Succeeded);

        await using var source = new NativeHomeThemePreferences(preferencePath, scope,
            UserPreferencesService.ProjectLegacyHomeTheme,
            _ => ValueTask.FromResult(true), callback => callback());
        await source.RefreshAsync();

        var projected = source.Context.Current.EffectiveAccent.IndividualApps;
        Assert.Equal(HomeThemeAccentPreference.MaximumIndividualAppChoices, projected.Count);
        foreach (var (appId, colour) in choices) Assert.Equal(colour, projected[appId]);
    }

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
