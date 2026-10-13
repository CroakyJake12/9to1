using Avalonia.Headless.XUnit;
using CakeOS.Cui.Themes;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.Services;

namespace Haven.Desktop.Tests;

public sealed class NativeCuiThemePreferencesTests
{
    [AvaloniaFact]
    public void Same_native_owner_publishes_saved_theme_appearance_and_accent_without_a_second_store()
    {
        using var paths = new Paths();
        var owner = new UserPreferencesService(paths);
        using var source = new NativeCuiThemePreferences(owner, paths);
        using var context = new CuiThemePreferenceContext(source);
        var changes = 0; context.Changed += (_, _) => changes++;
        owner.ApplyThemeChoice("Professional"); owner.ApplyAppearance(HavenUiAppearance.Dark);
        Assert.Equal("Professional", context.Current.RequestedName);
        Assert.Equal(CuiAppearance.Dark, context.Current.Appearance);
        Assert.StartsWith("native-local-profile:", context.Current.ScopeId);
        var revision = context.Current.Revision;
        owner.ApplyThemeChoice("Glow"); owner.ApplyAccentOverride(true, "Orange");
        Assert.Equal("Glow", context.Current.RequestedName);
        Assert.Equal(CuiAccentOverrideMode.AllApps, context.Current.EffectiveAccent.Mode);
        Assert.True(context.Current.Revision > revision); Assert.True(changes >= 3);
        var saved = File.ReadAllText(Path.Combine(paths.DataDirectory, "preferences.json"));
        using var reopened = new NativeCuiThemePreferences(new UserPreferencesService(paths), paths);
        Assert.Equal(source.Current.Name, reopened.Current.Name);
        Assert.Equal(source.Current.EffectiveAccent, reopened.Current.EffectiveAccent);
        Assert.Equal(source.Current.ScopeId, reopened.Current.ScopeId);
        Assert.Equal(saved, File.ReadAllText(Path.Combine(paths.DataDirectory, "preferences.json")));
        source.Dispose(); var detached = source.Current;
        owner.ApplyThemeChoice("Professional"); Assert.Same(detached, source.Current);
    }

    private sealed class Paths : IAppPaths, IDisposable
    {
        public string DataDirectory { get; } = Path.Combine(Path.GetTempPath(), "native-cui-theme-" + Guid.NewGuid().ToString("N"));
        public Paths() => Directory.CreateDirectory(DataDirectory);
        public string DatabasePath => Path.Combine(DataDirectory, "test.db");
        public string BrowserProfileDirectory => Path.Combine(DataDirectory, "browser");
        public string AttachmentsDirectory => Path.Combine(DataDirectory, "attachments");
        public string LogsDirectory => Path.Combine(DataDirectory, "logs");
        public string LegacyStatePath => Path.Combine(DataDirectory, "legacy.json");
        public void Dispose() => Directory.Delete(DataDirectory, true);
    }
}
