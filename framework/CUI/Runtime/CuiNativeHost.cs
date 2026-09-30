using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using CakeOS.Cui.Language;
using CakeOS.Cui.Themes;
using Avalonia.Themes.Fluent;
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

    public static AppBuilder ConfigureFonts(AppBuilder builder) => builder.WithInterFont()
        .With(new FontManagerOptions { DefaultFamilyName = "fonts:Inter#Inter" });

    public static void InitialisePrimitiveTheme(Application application)
    {
        ArgumentNullException.ThrowIfNull(application);
        application.Styles.Add(new FluentTheme());
        var paletteResources = new ResourceDictionary();
        CuiThemeResourceApplier.ApplyToResources(paletteResources, CuiSurfacePaletteCatalog.For("Home", CuiAppearance.Dark));
        application.Resources.MergedDictionaries.Add(paletteResources);
    }

    private sealed class NativeApplication(CuiNativeScene scene) : Application
    {
        private readonly CancellationTokenSource _shutdown = new();
        public override void Initialize() => InitialisePrimitiveTheme(this);
        public override void OnFrameworkInitializationCompleted()
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                var host = new CuiSceneHost();
                var window = new Window { Title = scene.Title, Width = 1100, Height = 760, Content = host };
                window.Closed += (_, _) => { _shutdown.Cancel(); host.Dispose(); };
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
                Document = new CuiRichParser().Parse("<Cui><StackPanel><TextBlock text=\"{Binding StartupStatus}\" accessible-name=\"Application startup status\" /></StackPanel></Cui>"),
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
