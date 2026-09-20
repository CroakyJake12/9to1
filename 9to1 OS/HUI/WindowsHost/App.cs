using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using CakeOS.Canvas.App;
using CakeOS.Platform;
using Microsoft.Extensions.DependencyInjection;
using System.Runtime.InteropServices;

namespace CakeOS.HuiWindowsHost;

public sealed class App : Application
{
    internal static IHuiRootProvider? RootProvider { get; set; }
    internal static IServiceProvider? Services { get; private set; }

    public App()
    {
        Styles.Add(new FluentTheme());
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var serviceCollection = new ServiceCollection();
            ConfigureServices(serviceCollection);
            Services = serviceCollection.BuildServiceProvider();

            var canvasMode = Environment.GetEnvironmentVariable("CAKEOS_HUI_CANVAS_PREVIEW") == "1";
            if (canvasMode)
                CanvasBoundaryProof.Run();

            PreviewWindow window;
            if (canvasMode)
            {
                var controller = new CanvasController(() => new CanvasNativeSession());
                window = new PreviewWindow(controller);
            }
            else
            {
                var root = RootProvider is null
                    ? null
                    : RootProvider.CreateRoot(Services) ?? throw new InvalidOperationException("HUI root provider returned null.");
                window = new PreviewWindow(root);
            }
            desktop.MainWindow = window;

            window.Opened += async (_, _) =>
            {
                if (RootProvider is not null)
                {
                    var initState = await RootProvider.InitializeAsync(Services).ConfigureAwait(false);
                    if (initState != HuiRootLifecycleState.Active)
                    {
                        await RootProvider.ActivateAsync().ConfigureAwait(false);
                    }
                }

                if (Environment.GetEnvironmentVariable("CAKEOS_HUI_PREVIEW_SELF_TEST") == "1")
                    Dispatcher.UIThread.Post(window.RunInputSelfTest, DispatcherPriority.Background);

                if (int.TryParse(Environment.GetEnvironmentVariable("CAKEOS_HUI_PREVIEW_AUTO_EXIT_MS"), out var ms) && ms > 0)
                {
                    var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(ms) };
                    timer.Tick += (_, _) => { timer.Stop(); window.Close(); };
                    timer.Start();
                }
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<IVersionedSettingsStore>(_ =>
        {
            var layout = new XdgPlatformStorageLayout(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CakeOS", "windows-host"));
            return new VersionedSettingsStore(layout);
        });
        services.AddSingleton<IPermissionService, PermissionService>();
        services.AddSingleton<INotificationService, NotificationService>();
        services.AddSingleton<ISettingsUpdatesModel>(_ => new SettingsUpdatesModel(
            new CakeUpdateBundleValidator(new StaticCakeOsSystemInfoProvider(
                new CakeOsSystemInfo("0.0.0-windows-preview", RuntimeInformation.OSArchitecture switch
                {
                    Architecture.X64 => "amd64",
                    Architecture.Arm64 => "arm64",
                    _ => throw new PlatformNotSupportedException("This CPU architecture is not supported by local CakeOS updates.")
                }))),
            new PkexecCakeUpdateInstaller(),
            new CakeUpdateHistoryStore(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CakeOS", "windows-host", "updates"))));
        services.AddSingleton<IProviderRegistry, ProviderRegistry>();
        services.AddSingleton<IModelGovernance, ModelGovernance>();
        services.AddSingleton<IProductRegistry, ProductRegistry>();
        services.AddSingleton<IProductRouter, RegistryBackedRouter>(sp =>
            new RegistryBackedRouter(sp.GetRequiredService<IProductRegistry>()));
    }
}
