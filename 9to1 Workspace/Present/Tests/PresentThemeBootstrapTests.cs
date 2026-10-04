using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Haven.Application;
using Haven.Core;
using Haven.Desktop;
using Haven.Infrastructure;

namespace HavenOS.Apps.Present.Tests;

public sealed class PresentThemeBootstrapTests
{
    [AvaloniaFact]
    public void Startup_supplies_canonical_semantic_resources_and_bundled_font_without_preferences()
    {
        // A fresh application's own styles must work before any preferences
        // service can populate Application.Current.Resources.
        var application = new PresentApplication();
        application.Initialize();
        foreach (var key in new[]
        {
            "HavenTextPrimaryBrush", "HavenTextSecondaryBrush", "HavenTextMutedBrush",
            "HavenPanelBrush", "HavenPanel2Brush", "HavenCardSurfaceBrush",
            "HavenAccentPrimaryBrush", "HavenAccentSecondaryBrush", "HavenBorderSubtleBrush"
        })
        {
            Assert.True(application.TryGetResource(key, null, out var resource), key);
            Assert.IsAssignableFrom<IBrush>(resource);
        }

        Assert.True(application.TryGetResource("HavenFontFamily", null, out var value));
        var font = Assert.IsType<FontFamily>(value);
        Assert.Equal("Montserrat", font.Name);
        Assert.NotNull(font.Key);
        Assert.Equal(new Uri("avares://Haven/Assets/Fonts/MontserratStatic"), font.Key.Source);
        Assert.Contains("Montserrat", new Typeface(font, weight: FontWeight.SemiBold).GlyphTypeface.FamilyName,
            StringComparison.Ordinal);
    }

    [AvaloniaTheory]
    [InlineData(HavenUiAppearance.SuperBright, 1440, 900)]
    [InlineData(HavenUiAppearance.Bright, 1440, 900)]
    [InlineData(HavenUiAppearance.Dark, 1440, 900)]
    [InlineData(HavenUiAppearance.SuperDark, 1440, 900)]
    [InlineData(HavenUiAppearance.SuperBright, 430, 860)]
    [InlineData(HavenUiAppearance.Bright, 430, 860)]
    [InlineData(HavenUiAppearance.Dark, 430, 860)]
    [InlineData(HavenUiAppearance.SuperDark, 430, 860)]
    public async Task Existing_present_route_initializes_and_renders_in_each_appearance(
        HavenUiAppearance appearance, int width, int height)
    {
        using var paths = new TemporaryPresentPaths();
        var preferences = new UserPreferencesService(paths);
        preferences.ApplyAppearance(appearance, save: false);
        Assert.Equal(appearance, Application.Current!.Resources["HavenUiAppearance"]);

        using var host = PresentAppHost.Create(
            new PresentRepository(paths), new PresentPptxExportService(), new PresentPptxImportService(paths));
        await host.InitializeAsync();
        Assert.Single(Assert.IsType<PresentDocument>(host.Document).Slides);

        var window = new Window { Width = width, Height = height, Content = host.Page };
        try
        {
            window.Show();
            window.UpdateLayout();
            Assert.True(host.Page.Bounds.Width > 0 && host.Page.Bounds.Height > 0);
            using var frame = window.CaptureRenderedFrame();
            Assert.NotNull(frame);
            Assert.True(frame.PixelSize.Width > 0 && frame.PixelSize.Height > 0);
            using var png = new MemoryStream();
            frame.Save(png);
            Assert.True(png.Length > 0);

            var destination = Environment.GetEnvironmentVariable("HAVEN_VISUAL_CAPTURE_DIR");
            if (!string.IsNullOrWhiteSpace(destination))
            {
                Directory.CreateDirectory(destination);
                File.WriteAllBytes(Path.Combine(destination, $"present-{appearance}-{width}x{height}.png"), png.ToArray());
            }
        }
        finally
        {
            window.Content = null;
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Canonical_preferences_keep_appearance_and_bundled_font_when_accent_changes()
    {
        using var paths = new TemporaryPresentPaths();
        var preferences = new UserPreferencesService(paths);
        preferences.ApplyAppearance(HavenUiAppearance.Dark, save: false);
        var application = Application.Current!;
        var before = Assert.IsAssignableFrom<IGradientBrush>(application.Resources["HavenAccentPrimaryBrush"])
            .GradientStops.Select(stop => (stop.Offset, stop.Color)).ToArray();
        preferences.ApplyAccentOverride(true, nameof(HavenAccentColour.Blue), save: false);

        Assert.Equal(HavenUiAppearance.Dark, application.Resources["HavenUiAppearance"]);
        var after = Assert.IsAssignableFrom<IGradientBrush>(application.Resources["HavenAccentPrimaryBrush"])
            .GradientStops.Select(stop => (stop.Offset, stop.Color)).ToArray();
        Assert.False(before.SequenceEqual(after));
        var font = Assert.IsType<FontFamily>(application.Resources["HavenFontFamily"]);
        Assert.Contains("Montserrat", new Typeface(font, weight: FontWeight.SemiBold).GlyphTypeface.FamilyName,
            StringComparison.Ordinal);
        preferences.ApplyAccentOverride(false, null, save: false);
    }

    private sealed class TemporaryPresentPaths : IAppPaths, IDisposable
    {
        public TemporaryPresentPaths()
        {
            DataDirectory = Path.Combine(Path.GetTempPath(), "haven-present-theme-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(DataDirectory);
        }

        public string DataDirectory { get; }
        public string DatabasePath => Path.Combine(DataDirectory, "haven.db");
        public string BrowserProfileDirectory => Path.Combine(DataDirectory, "BrowserProfile");
        public string AttachmentsDirectory => Path.Combine(DataDirectory, "Attachments");
        public string LogsDirectory => Path.Combine(DataDirectory, "Logs");
        public string LegacyStatePath => Path.Combine(DataDirectory, "legacy.json");
        public void Dispose() => Directory.Delete(DataDirectory, recursive: true);
    }
}
