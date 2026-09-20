using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using CakeOS.HuiLinuxHost;
using CakeOS.Images.Interop;
using CakeOS.Platform;
using Microsoft.Extensions.DependencyInjection;

namespace CakeOS.Images.Frontend;

public sealed class ImagesApp : Application
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

            if (Environment.GetEnvironmentVariable("CAKEOS_HUI_IMAGES_PREVIEW") == "1")
                ImagesManagedBoundaryProof.Run();

            var root = RootProvider is null
                ? new ImagesMainPage()
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
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "haven", "images"));
            return new VersionedSettingsStore(layout);
        });
        services.AddSingleton<IPermissionService, PermissionService>();
        services.AddSingleton<INotificationService, NotificationService>();
        services.AddSingleton<IProviderRegistry, ProviderRegistry>();
        services.AddSingleton<IModelGovernance, ModelGovernance>();
        services.AddSingleton<IProductRegistry, ProductRegistry>();
        services.AddSingleton<IProductRouter, RegistryBackedRouter>(sp =>
            new RegistryBackedRouter(sp.GetRequiredService<IProductRegistry>()));

        services.AddSingleton<ILoupeBackend, LoupeBackend>();
        services.AddSingleton<IAiVisionClient, AiVisionClient>();
        services.AddSingleton<ImagesViewModel>();
        services.AddSingleton<IPersistenceLayer, SidecarPersistenceLayer>();
    }
}

public sealed class ImagesMainPage : HuiPage
{
    private readonly ImagesViewModel _viewModel;

    public ImagesMainPage(ImagesViewModel viewModel)
    {
        _viewModel = viewModel;
        Name = "Images.Main";
        Layout = HavenLayout.Vertical;
        SetValue(HavenProperties.Width, HavenLength.Percent(100));
        SetValue(HavenProperties.Height, HavenLength.Percent(100));
        SetValue(HavenProperties.Gap, HavenLength.Px(0));

        var toolbar = CreateToolbar();
        var viewport = CreateViewport();
        var filmstrip = CreateFilmstrip();

        Add(toolbar);
        Add(viewport);
        Add(filmstrip);

        _viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ImagesViewModel.CurrentImage))
            {
                UpdateViewport();
            }
        };
    }

    private HavenPanel CreateToolbar()
    {
        var panel = new HavenPanel
        {
            Name = "Images.Toolbar",
            Layout = HavenLayout.Horizontal,
        };
        panel.SetValue(HavenProperties.Width, HavenLength.Percent(100));
        panel.SetValue(HavenProperties.Height, HavenLength.Px(52));
        panel.SetValue(HavenProperties.Gap, HavenLength.Px(8));
        panel.SetValue(HavenProperties.Padding, HavenThickness.Parse("8px"));
        panel.SetValue(HavenProperties.VerticalAlignment, HavenVerticalAlignment.Center);

        var openButton = new HavenButton
        {
            Name = "Images.Open",
            Content = "Open",
        };
        openButton.SetValue(HavenProperties.Height, HavenLength.Px(40));
        openButton.SetValue(HavenProperties.MinWidth, HavenLength.Px(80));
        openButton.Invoked += (_, _) => _viewModel.OpenFileCommand.Execute(null);

        var zoomInButton = new HavenButton
        {
            Name = "Images.ZoomIn",
            Content = "Zoom In",
        };
        zoomInButton.SetValue(HavenProperties.Height, HavenLength.Px(40));
        zoomInButton.SetValue(HavenProperties.MinWidth, HavenLength.Px(96));
        zoomInButton.Invoked += (_, _) => _viewModel.ZoomInCommand.Execute(null);

        var zoomOutButton = new HavenButton
        {
            Name = "Images.ZoomOut",
            Content = "Zoom Out",
        };
        zoomOutButton.SetValue(HavenProperties.Height, HavenLength.Px(40));
        zoomOutButton.SetValue(HavenProperties.MinWidth, HavenLength.Px(96));
        zoomOutButton.Invoked += (_, _) => _viewModel.ZoomOutCommand.Execute(null);

        var fitButton = new HavenButton
        {
            Name = "Images.Fit",
            Content = "Fit",
        };
        fitButton.SetValue(HavenProperties.Height, HavenLength.Px(40));
        fitButton.SetValue(HavenProperties.MinWidth, HavenLength.Px(80));
        fitButton.Invoked += (_, _) => _viewModel.FitCommand.Execute(null);

        var rotateButton = new HavenButton
        {
            Name = "Images.Rotate",
            Content = "Rotate",
        };
        rotateButton.SetValue(HavenProperties.Height, HavenLength.Px(40));
        rotateButton.SetValue(HavenProperties.MinWidth, HavenLength.Px(96));
        rotateButton.Invoked += (_, _) => _viewModel.RotateCommand.Execute(null);

        var analyzeButton = new HavenButton
        {
            Name = "Images.Analyze",
            Content = "AI Analyze",
        };
        analyzeButton.SetValue(HavenProperties.Height, HavenLength.Px(40));
        analyzeButton.SetValue(HavenProperties.MinWidth, HavenLength.Px(112));
        analyzeButton.Invoked += (_, _) => _viewModel.AnalyzeCommand.Execute(null);

        var metadataButton = new HavenButton
        {
            Name = "Images.Metadata",
            Content = "Metadata",
        };
        metadataButton.SetValue(HavenProperties.Height, HavenLength.Px(40));
        metadataButton.SetValue(HavenProperties.MinWidth, HavenLength.Px(104));
        metadataButton.Invoked += (_, _) => _viewModel.ShowMetadataCommand.Execute(null);

        panel.Add(openButton);
        panel.Add(zoomInButton);
        panel.Add(zoomOutButton);
        panel.Add(fitButton);
        panel.Add(rotateButton);
        panel.Add(analyzeButton);
        panel.Add(metadataButton);

        return panel;
    }

    private HavenBorder CreateViewport()
    {
        var border = new HavenBorder
        {
            Name = "Images.Viewport",
            Background = HavenBrushes.Surface,
            BorderBrush = HavenBrushes.Border,
            BorderThickness = HavenThickness.Parse("1px"),
        };
        border.SetValue(HavenProperties.Width, HavenLength.Percent(100));
        border.SetValue(HavenProperties.Height, HavenLength.Percent(100));
        border.SetValue(HavenProperties.Margin, HavenThickness.Parse("8px"));

        _viewportContent = new HavenImage
        {
            Name = "Images.ViewportImage",
            Stretch = HavenStretch.Uniform,
        };
        _viewportContent.SetValue(HavenProperties.HorizontalAlignment, HavenHorizontalAlignment.Center);
        _viewportContent.SetValue(HavenProperties.VerticalAlignment, HavenVerticalAlignment.Center);

        border.Child = _viewportContent;
        return border;
    }

    private HavenScrollViewer CreateFilmstrip()
    {
        var scrollViewer = new HavenScrollViewer
        {
            Name = "Images.Filmstrip",
            HorizontalScrollBarVisibility = HavenScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = HavenScrollBarVisibility.Disabled,
        };
        scrollViewer.SetValue(HavenProperties.Width, HavenLength.Percent(100));
        scrollViewer.SetValue(HavenProperties.Height, HavenLength.Px(120));
        scrollViewer.SetValue(HavenProperties.Margin, HavenThickness.Parse("8px 0 8px 8px"));

        _filmstripPanel = new HavenPanel
        {
            Name = "Images.FilmstripPanel",
            Layout = HavenLayout.Horizontal,
        };
        _filmstripPanel.SetValue(HavenProperties.Gap, HavenLength.Px(8));
        _filmstripPanel.SetValue(HavenProperties.Padding, HavenThickness.Parse("8px"));

        scrollViewer.Content = _filmstripPanel;
        return scrollViewer;
    }

    private void UpdateViewport()
    {
        var current = _viewModel.CurrentImage;
        if (current is null || current.Handle == IntPtr.Zero)
        {
            _viewportContent.Source = null;
            return;
        }

        var result = LoupeBackendInterop.cake_images_render(
            current.Handle,
            new LoupeBackendInterop.Viewport
            {
                X = _viewModel.PanX,
                Y = _viewModel.PanY,
                Scale = _viewModel.ZoomLevel,
                TargetWidth = (int)_viewportContent.Bounds.Width,
                TargetHeight = (int)_viewportContent.Bounds.Height,
            },
            out var renderResult);

        if (result == LoupeBackendInterop.Status.Ok && renderResult.CairoSurface != IntPtr.Zero)
        {
            _viewportContent.Source = new CairoSurfaceBitmap(renderResult.CairoSurface, renderResult.Width, renderResult.Height, renderResult.Stride);
        }
    }

    private HavenImage _viewportContent = null!;
    private HavenPanel _filmstripPanel = null!;
}