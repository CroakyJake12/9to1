using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CakeOS.Cui.Themes;
using CakeOS.Cui.Runtime;
using Haven.Core;
using Haven.Desktop.HavenUI.Backend;
using Haven.Desktop.Views.Pages.Chat;
using HavenOS.Home.Core;

namespace Haven.Desktop.Tests;

public sealed class ChatThemeAdaptationTests
{
    [AvaloniaFact]
    public Task Mounted_chat_follows_Home_brightness_without_recreating_or_losing_draft() => RunAsync();

    public static async Task RunAsync(string? captureDirectory = null)
    {
        var root = Path.Combine(Path.GetTempPath(), "chat-theme-probe-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var previousTheme = CuiSurfacePaletteCatalog.ActiveTheme;
        var previousAppearance = CuiThemeScopeApplier.DetectAppearance();
        var previousOverride = CuiSurfacePaletteCatalog.OverrideAccent;
        var previousAccent = CuiSurfacePaletteCatalog.AccentOverride;
        var preferences = new UserPreferencesService(new Paths(root));
        var preferenceFile = Path.Combine(root, "native-home.json");
        await File.WriteAllTextAsync(preferenceFile, "{\"havenUiThemeName\":\"Glow\",\"havenUiAppearance\":2}");
        await using var owner = new Haven.Desktop.Services.NativeHomeThemePreferences(preferenceFile,
            new(HomeDataScope.DeviceLocal, "theme-probe"), UserPreferencesService.ProjectLegacyHomeTheme,
            _ => ValueTask.FromResult(true), action => Dispatcher.UIThread.Post(action), preferences.ApplyHomeThemePresentation);
        var resolver = new ChatGenUiNativeControlResolver();
        using var scene = new ChatHavenScene(resolver);
        var host = new HavenSceneControl(new HavenDesktopImageResolver(), resolver) { Root = scene.Root };
        var window = new Window { Width = 1000, Height = 760, Content = host };
        window.Bind(Window.BackgroundProperty, window.GetResourceObservable("CuiBackgroundBrush"));
        try
        {
            await owner.RefreshAsync();
            window.Show();
            await scene.ComposerView.InitializeAsync();
            window.UpdateLayout();
            var message = Assert.Single(scene.ComposerView.GetVisualDescendants().OfType<TextBox>(),
                item => AutomationProperties.GetName(item) == "Message");
            message.Text = "Keep this draft while changing appearance.";
            var initialInput = message;
            foreach (var appearance in new[] { CuiAppearance.Bright, CuiAppearance.Dark, CuiAppearance.Bright })
            {
                var options = Assert.IsAssignableFrom<IEnumerable<Dictionary<string, object?>>>(owner.SettingsBindings()["HomeAppearanceChoices"]);
                var target = Assert.Single(options, item => Equals(item["Name"], appearance.ToString()))["Target"]!;
                Assert.True((await owner.ApplySettingsAsync(target)).Succeeded);
                await owner.RefreshAsync();
                for (var i = 0; i < 3; i++)
                {
                    Dispatcher.UIThread.RunJobs();
                    await Dispatcher.UIThread.InvokeAsync(window.UpdateLayout, DispatcherPriority.Render);
                }
                Assert.Same(initialInput, Assert.Single(scene.ComposerView.GetVisualDescendants().OfType<TextBox>(),
                    item => AutomationProperties.GetName(item) == "Message"));
                Assert.Equal("Keep this draft while changing appearance.", scene.Input.Text);
                var text = Assert.IsAssignableFrom<ISolidColorBrush>(message.Foreground).Color;
                var body = Assert.IsAssignableFrom<ISolidColorBrush>(window.Background).Color;
                var emptyHeading = HavenAvaloniaThemeResolver.EffectColor(HavenAvaloniaThemeResolver.Resolve("TextPrimary", host));
                Assert.True(appearance == CuiAppearance.Bright ? Luma(text) < .45 && Luma(body) > .6 : Luma(text) > .6 && Luma(body) < .45,
                    $"{appearance} composer text {text} and body {body} must adapt together.");
                Assert.True(appearance == CuiAppearance.Bright ? Luma(emptyHeading) < .45 : Luma(emptyHeading) > .6,
                    $"{appearance} retained Haven text {emptyHeading} must follow the Home palette.");
                if (captureDirectory is not null)
                {
                    Directory.CreateDirectory(captureDirectory);
                    using var bitmap = new RenderTargetBitmap(new PixelSize((int)window.Width, (int)window.Height), new Vector(96, 96));
                    bitmap.Render(window);
                    bitmap.Save(Path.Combine(captureDirectory, $"chat-{appearance.ToString().ToLowerInvariant()}.png"));
                }
            }
        }
        finally
        {
            window.Content = null;
            window.Close();
            await owner.DisposeAsync();
            preferences.ApplyThemeChoice(CuiThemeCatalog.Name(previousTheme), save: false);
            preferences.ApplyAppearance((HavenUiAppearance)previousAppearance, save: false);
            CuiSurfacePaletteCatalog.OverrideAccent = previousOverride;
            CuiSurfacePaletteCatalog.AccentOverride = previousAccent;
            CuiThemeScopeApplier.ApplyGlobalTheme(previousTheme, appearanceOverride: previousAppearance);
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    private static double Luma(Color colour) => (.2126 * colour.R + .7152 * colour.G + .0722 * colour.B) / 255d;
    private sealed class Paths(string root) : IAppPaths
    {
        public string DataDirectory => root;
        public string DatabasePath => Path.Combine(root, "unused.db");
        public string BrowserProfileDirectory => Path.Combine(root, "browser");
        public string AttachmentsDirectory => Path.Combine(root, "attachments");
        public string LogsDirectory => Path.Combine(root, "logs");
        public string LegacyStatePath => Path.Combine(root, "legacy.json");
    }
}
