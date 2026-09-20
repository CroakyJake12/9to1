using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using CakeOS.Apps.Boards.Contract;
using CakeOS.Apps.Boards.Hui;
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
                desktop.MainWindow = window;
            }
            else
            {
                HavenBoardsHuiSession? boardsSession = null;
                if (Environment.GetEnvironmentVariable("CAKEOS_HUI_BOARDS_PREVIEW") == "1")
                {
                    boardsSession = OpenBoardsSession();
                    window = new PreviewWindow(boardsSession.Scene.Root, "CakeOS Boards (Windows)");
                    var captured = boardsSession;
                    window.Closed += async (_, _) => await captured.DisposeAsync().ConfigureAwait(false);
                }
                else
                {
                    var root = RootProvider is null
                        ? null
                        : RootProvider.CreateRoot(Services) ?? throw new InvalidOperationException("HUI root provider returned null.");
                    window = new PreviewWindow(root);
                }
                desktop.MainWindow = window;
            }

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

    private static HavenBoardsHuiSession OpenBoardsSession()
    {
        var storeRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "CakeOS", "Boards");
        var store = new JsonFileHavenBoardStore(storeRoot);
        try
        {
            // Local file IO only; blocking briefly keeps startup ordering explicit.
            var session = HavenBoardsHuiSession.OpenAsync(store).GetAwaiter().GetResult();
            Console.WriteLine(
                $"CAKEOS_BOARDS_WINDOWS_SESSION_READY board={session.Snapshot.Id} version={session.Snapshot.Version} store={storeRoot}");
            return session;
        }
        catch
        {
            store.Dispose();
            throw;
        }
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