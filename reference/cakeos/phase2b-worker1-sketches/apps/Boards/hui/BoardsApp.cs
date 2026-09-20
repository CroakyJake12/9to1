using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using CakeOS.Apps.Boards.Contract;
using CakeOS.HuiLinuxHost;
using CakeOS.Platform;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace CakeOS.Apps.Boards.Hui;

public sealed class BoardsApp : Application
{
    internal static IHuiRootProvider? RootProvider { get; set; }
    internal static IServiceProvider? Services { get; private set; }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var serviceCollection = new ServiceCollection();
            ConfigureServices(serviceCollection);
            Services = serviceCollection.BuildServiceProvider();

            var root = RootProvider is null
                ? new BoardsMainPage()
                : RootProvider.CreateRoot(Services) ?? throw new InvalidOperationException("HUI root provider returned null.");

            var window = new PreviewWindow(root);
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
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "haven", "boards"));
            return new VersionedSettingsStore(layout);
        });
        services.AddSingleton<IPermissionService, PermissionService>();
        services.AddSingleton<INotificationService, NotificationService>();
        services.AddSingleton<IProviderRegistry, ProviderRegistry>();
        services.AddSingleton<IModelGovernance, ModelGovernance>();
        services.AddSingleton<IProductRegistry, ProductRegistry>();
        services.AddSingleton<IProductRouter, RegistryBackedRouter>(sp =>
            new RegistryBackedRouter(sp.GetRequiredService<IProductRegistry>()));

        services.AddSingleton<IHavenBoardStore, FileSystemBoardStore>();
        services.AddSingleton<IHavenBoardCommandSink, HavenBoardCommandService>();
        services.AddSingleton<BoardsViewModel>();
    }
}

public sealed class BoardsMainPage : HuiPage
{
    public BoardsMainPage()
    {
        Name = "Boards.Main";
        Layout = HavenLayout.Vertical;
        SetValue(HavenProperties.Width, HavenLength.Percent(100));
        SetValue(HavenProperties.Height, HavenLength.Percent(100));
        SetValue(HavenProperties.Gap, HavenLength.Px(0));

        var toolbar = CreateToolbar();
        var boardView = CreateBoardView();

        Add(toolbar);
        Add(boardView);
    }

    private HavenPanel CreateToolbar()
    {
        var panel = new HavenPanel
        {
            Name = "Boards.Toolbar",
            Layout = HavenLayout.Horizontal,
        };
        panel.SetValue(HavenProperties.Width, HavenLength.Percent(100));
        panel.SetValue(HavenProperties.Height, HavenLength.Px(48));
        panel.SetValue(HavenProperties.Gap, HavenLength.Px(8));
        panel.SetValue(HavenProperties.Padding, HavenThickness.Parse("8px"));
        panel.SetValue(HavenProperties.VerticalAlignment, HavenVerticalAlignment.Center);

        var title = new HavenTextBlock
        {
            Name = "Boards.Title",
            Text = "Haven Boards",
            FontSize = 18,
            FontWeight = HavenFontWeight.SemiBold,
        };
        title.SetValue(HavenProperties.VerticalAlignment, HavenVerticalAlignment.Center);

        var addGroupButton = new HavenButton
        {
            Name = "Boards.AddGroup",
            Content = "+ Group",
        };
        addGroupButton.SetValue(HavenProperties.Height, HavenLength.Px(36));
        addGroupButton.SetValue(HavenProperties.MinWidth, HavenLength.Px(96));

        var addCardButton = new HavenButton
        {
            Name = "Boards.AddCard",
            Content = "+ Card",
        };
        addCardButton.SetValue(HavenProperties.Height, HavenLength.Px(36));
        addCardButton.SetValue(HavenProperties.MinWidth, HavenLength.Px(96));

        panel.Add(title);
        panel.Add(addGroupButton);
        panel.Add(addCardButton);

        return panel;
    }

    private HavenScrollViewer CreateBoardView()
    {
        var scrollViewer = new HavenScrollViewer
        {
            Name = "Boards.ScrollViewer",
        };
        scrollViewer.SetValue(HavenProperties.Width, HavenLength.Percent(100));
        scrollViewer.SetValue(HavenProperties.Height, HavenLength.Percent(100));

        var boardSurface = new HavenPanel
        {
            Name = "Boards.Surface",
            Layout = HavenLayout.Vertical,
        };
        boardSurface.SetValue(HavenProperties.Width, HavenLength.Percent(100));
        boardSurface.SetValue(HavenProperties.Gap, HavenLength.Px(16));
        boardSurface.SetValue(HavenProperties.Padding, HavenThickness.Parse("16px"));

        scrollViewer.Content = boardSurface;

        return scrollViewer;
    }
}