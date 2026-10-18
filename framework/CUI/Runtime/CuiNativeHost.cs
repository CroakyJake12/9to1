using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using CakeOS.Cui.Language;
using CakeOS.Cui.Themes;
using Avalonia.Media;

namespace CakeOS.Cui.Runtime;

/// <summary>Shared desktop platform bootstrap for canonical CUI apps. Authentication and Home readiness remain the supplied scene's authority.</summary>
public static class CuiNativeHost
{
    [STAThread]
    public static int Run(CuiNativeScene scene, string[] arguments) =>
        Build(scene).StartWithClassicDesktopLifetime(arguments);

    public static AppBuilder Build(CuiNativeScene scene)
    {
        ArgumentNullException.ThrowIfNull(scene);
        return ConfigureFonts(AppBuilder.Configure(() => new NativeApplication(scene)).UsePlatformDetect());
    }

    public const string BundledInterfaceFontFamily =
        "avares://CakeOS.Cui.Runtime/Assets/Fonts/MontserratStatic#Montserrat";
    public const string BundledEmojiFontFamily =
        "avares://CakeOS.Cui.Runtime/Assets/Fonts/NotoEmoji#Noto Emoji";
    public const string BundledSymbolFontFamily =
        "avares://CakeOS.Cui.Runtime/Assets/Fonts/DejaVuSans#DejaVu Sans";

    public static AppBuilder ConfigureFonts(AppBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.With(new FontManagerOptions
        {
            DefaultFamilyName = BundledInterfaceFontFamily,
            // Browser renderers have no OS font collection. Resolve missing
            // glyphs through bundled, licensed faces without changing the
            // authored font family or replacing the interface's Montserrat.
            FontFallbacks =
            [
                new FontFallback { FontFamily = new(BundledEmojiFontFamily) },
                new FontFallback { FontFamily = new(BundledSymbolFontFamily) }
            ],
            FontFamilyMappings = new Dictionary<string, FontFamily>(StringComparer.OrdinalIgnoreCase)
            {
                ["Montserrat"] = new(BundledInterfaceFontFamily)
            }
        });
    }

    public static void InitialisePrimitiveTheme(Application application, string surface = "Home", CuiAppearance? appearance = null)
    {
        ArgumentNullException.ThrowIfNull(application);
        var effective = appearance ?? CuiThemeScopeApplier.DetectAppearance();
        application.RequestedThemeVariant = CuiSceneVisualResources.Variant(effective);
        var paletteResources = CuiSceneVisualResources.Create(surface, effective);
        application.Resources.MergedDictionaries.Add(paletteResources);
        application.Styles.Add(new CuiComponentStyles());
    }

    private sealed class NativeApplication(CuiNativeScene scene) : Application
    {
        private readonly CancellationTokenSource _shutdown = new();
        private CuiSceneThemeBinding? _themeBinding;
        public override void Initialize()
        {
            InitialisePrimitiveTheme(this, scene.Surface, scene.Appearance);
            _themeBinding = new CuiSceneThemeBinding(scene);
            Resources.MergedDictionaries.Add(_themeBinding.Resources);
            this.Bind(RequestedThemeVariantProperty, this.GetResourceObservable("CuiSceneThemeVariant"));
        }
        public override void OnFrameworkInitializationCompleted()
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                var host = new CuiSceneHost(scene.ControlRegistry);
                var window = new Window { Title = scene.Title, Width = 1100, Height = 760, Content = host };
                // CUI owns the same opaque surface on every backend. A native
                // platform material must not become Professional's background.
                window.Bind(Window.BackgroundProperty, window.GetResourceObservable("CuiBackgroundBrush"));
                window.Closed += (_, _) => { _themeBinding?.Dispose(); _shutdown.Cancel(); host.Dispose(); };
                desktop.MainWindow = window;
                _ = InitialiseAsync(host);
            }
            base.OnFrameworkInitializationCompleted();
        }

        private async Task InitialiseAsync(CuiSceneHost host)
        {
            var status = new CuiViewModel();
            status.Set("StartupStatus", "Checking required Home services and authentication…");
            var loading = scene with
            {
                Document = new CuiRichParser().Parse("<Page><Container Type=\"Vertical Stack\"><Text Text=\"{Binding StartupStatus}\" AccessibleName=\"Application startup status\" /></Container></Page>"),
                Bindings = status, Actions = status,
                Readiness = new ImmediateReadiness(new(CuiSceneAvailabilityState.Ready, "host-starting", "Checking required services"))
            };
            try
            {
                await host.ShowAsync(loading, _shutdown.Token);
                await host.ShowAsync(scene, _shutdown.Token);
            }
            catch (OperationCanceledException) when (_shutdown.IsCancellationRequested) { }
            catch (Exception)
            {
                if (_shutdown.IsCancellationRequested) return;
                // The original scene's actions are never mounted after a failed real handshake.
                var unavailable = scene with { Readiness = new ImmediateReadiness(new(CuiSceneAvailabilityState.Unavailable,
                    "host-startup-failed", "The required application services could not be started. Existing data was preserved.")) };
                await host.ShowAsync(unavailable, _shutdown.Token);
            }
        }
    }

    private sealed class ImmediateReadiness(CuiSceneAvailability result) : ICuiSceneReadiness
    {
        public ValueTask<CuiSceneAvailability> CheckAsync(CancellationToken cancellationToken) => ValueTask.FromResult(result);
    }
}
