// 9-1 Home native host. The canonical Home.cui surface is loaded through CUI.

using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using CakeOS.Cui.Runtime;
using CakeOS.Cui.Themes;
using Haven.CUI.DevTools;
using HavenOS.Home;
using HavenOS.Home.Core;

namespace AvaloniaHome;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        Console.WriteLine("[9-1 Home] Starting native CUI host...");

        var cuiPath = FindCuiFile();
        Console.WriteLine($"[9-1 Home] canonical .cui file: {cuiPath ?? "NOT FOUND"}");

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
    {
        return AppBuilder.Configure<HomeApp>()
            .UseWin32()
            .UseSkia()
            .UseHarfBuzz()
            .WithInterFont();
    }

    internal static string? FindCuiFile()
    {
        var baseDir = AppDomain.CurrentDomain.BaseDirectory;
        var candidates = new[]
        {
            Path.Combine(baseDir, "UI", "Home.cui"),
            Path.Combine(baseDir, "Home.cui"),
        };

        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate))
                return candidate;
        }

        var dir = new DirectoryInfo(baseDir);
        for (var i = 0; i < 10 && dir != null; i++)
        {
            var path = Path.Combine(dir.FullName, "9to1 Workspace", "Home", "UI", "Home.cui");
            if (File.Exists(path))
                return path;
            dir = dir.Parent;
        }

        return null;
    }
}

/// <summary>
/// Avalonia Application that renders the canonical Home CUI surface and projects only
/// observed Home-domain state. Providers that are not configured remain visibly unavailable.
/// </summary>
internal sealed class HomeApp : Application
{
    private readonly CuiViewModel _viewModel = new();
    private readonly HomeDashboard _dashboard = new();
    private readonly IHomeCoreStateStore _coreStateStore;
    private readonly HomeProductivityEngineService _productivityEngine;
    private readonly HomeCoreRuntime _homeCore;
    private readonly HomeCoreApi _homeCoreApi;
    private readonly HomeFeatureNavigationHost _featureNavigation = new();
    private readonly HomeCuiController _controller;
    private IDisposable? _homeCoreSubscription;
    private CuiControlLoader? _loader;

    public HomeApp()
    {
        _coreStateStore = FileHomeCoreStateStore.CreateDefault();
        var authorization = new DenyAllHomeCoreAuthorization();
        _productivityEngine = new HomeProductivityEngineService();
        _homeCore = new HomeCoreRuntime([new HomeCoreStateService(_coreStateStore), _productivityEngine], authorization);
        _homeCoreApi = new HomeCoreApi(_homeCore, authorization);
        _controller = new HomeCuiController(_dashboard);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        // Home Core is ready before Home presents its normal shell. State corruption leaves the
        // control plane explicitly degraded while preserving the original state for repair.
        _ = _homeCore.StartAsync().GetAwaiter().GetResult();
        ApplySnapshot(_controller.ShowCurrent());
        _homeCoreSubscription = _homeCore.Subscribe(OnHomeCoreChanged);
        _viewModel.On("InstallAllUpdates", _ => _ = InstallAllAsync());
        RegisterUnavailableAction("OpenStudio", "Studio navigation is not connected in this host.");
        RegisterUnavailableAction("OpenWrite", "Write navigation is not connected in this host.");
        RegisterUnavailableAction("OpenBrowse", "Browse navigation is not connected in this host.");
        RegisterUnavailableAction("OpenData", "Data navigation is not connected in this host.");
        RegisterUnavailableAction("OpenBoards", "Boards navigation is not connected in this host.");
        _viewModel.On("NavigateHome", _ => ReportUnavailable("Home is already open."));
        _viewModel.SetActionAvailability("NavigateHome", true);
        RegisterUnavailableAction("NavigateSpaces", "Spaces navigation is not connected in this host.");
        RegisterUnavailableAction("NavigateApps", "Apps navigation is not connected in this host.");
        RegisterUnavailableAction("NavigateLibrary", "Library navigation is not connected in this host.");
        RegisterUnavailableAction("NavigateEvents", "Events navigation is not connected in this host.");
        RegisterUnavailableAction("NavigateAutomations", "Automations navigation is not connected in this host.");
        RegisterUnavailableAction("NavigateDiscover", "Discover navigation is not connected in this host.");
        RegisterUnavailableAction("NavigateSettings", "Settings navigation is not connected in this host.");

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Closing the visible shell must not tear down the shared service lifetime. The core
            // stops only when the process receives an explicit application shutdown request.
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            desktop.Exit += async (_, _) =>
            {
                _homeCoreSubscription?.Dispose();
                _homeCoreSubscription = null;
                await _homeCore.StopAsync(explicitlyRequested: true);
            };
            var window = BuildWindow();
            desktop.MainWindow = window;
        }

        base.OnFrameworkInitializationCompleted();
    }

    internal Window BuildWindow()
    {
        CuiThemeScopeApplier.ApplyGlobalTheme(CuiThemePreferenceReader.Read());
        var window = new Window
        {
            Title = "9-1 Home",
            Width = 1200,
            Height = 800,
        };

        window.KeyDown += (s, e) =>
        {
            if (e.Key == Key.F5)
                _ = RefreshAsync();
        };

        var cuiPath = Program.FindCuiFile();
        if (cuiPath != null)
        {
            Console.WriteLine($"[9-1 Home] Loading canonical .cui: {cuiPath}");
            _loader = new CuiControlLoader();
            _loader.SetBindingContext(_viewModel);
            _loader.SetActionDispatcher(_viewModel);

            var (root, diagnostics) = _loader.LoadFile(cuiPath);
            Console.WriteLine($"[9-1 Home] Diagnostics: {diagnostics.Count}");

            if (root != null)
            {
                _loader.WireBindings(root);
                window.Content = root;
                Console.WriteLine($"[9-1 Home] Root: {root.GetType().Name} — CUI loaded into window");
            }
            else
            {
                window.Content = CreateFallbackUI(diagnostics.Any()
                    ? string.Join("\n", diagnostics.Select(d => d.Message))
                    : "Failed to load CUI");
            }
        }
        else
        {
            window.Content = CreateFallbackUI("No .cui file found");
        }

        return window;
    }

    /// <summary>Capture the current native CUI controls for read-only DevTools inspection.</summary>
    internal CuiLiveTreeInspector? CaptureDiagnostics(Window window) =>
        window.Content is Control root ? CuiLiveTreeInspector.Capture(root, _loader) : null;

    private void RegisterUnavailableAction(string name, string message)
    {
        _viewModel.On(name, _ => ReportUnavailable(message));
        _viewModel.SetActionAvailability(name, false);
    }

    private static StackPanel CreateFallbackUI(string message)
    {
        var panel = new StackPanel
        {
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
        };

        panel.Children.Add(new TextBlock
        {
            Text = "9-1 Home",
            FontSize = 32,
            FontWeight = Avalonia.Media.FontWeight.Bold,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 16),
        });

        panel.Children.Add(new TextBlock
        {
            Text = message,
            FontSize = 14,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            MaxWidth = 600,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
        });

        return panel;
    }

    private async Task RefreshAsync()
    {
        ApplySnapshot(await _controller.RefreshAsync());
    }

    private async Task InstallAllAsync()
    {
        if (!_controller.Surface.RequestInstallAll())
        {
            ApplySnapshot(_controller.ShowCurrent());
            return;
        }

        if (_controller.Surface.TryDequeueAction(out var action))
            ApplySnapshot(await _controller.ExecuteAsync(action));
    }

    private void ApplySnapshot(HomeDashboardSnapshot snapshot)
    {
        _viewModel.Set("CatalogSummary", snapshot.Catalog.Status.Message);
        _viewModel.Set("RuntimeSummary", snapshot.Runtime.Runtime.Message);
        _viewModel.Set("EventsSummary", "An events provider is not configured in this host.");
        _viewModel.Set("OperationSummary", $"{snapshot.LastOperation.State}: {snapshot.LastOperation.Message}");
        Avalonia.Threading.Dispatcher.UIThread.Post(() => _loader?.RefreshBindings());
    }

    private void ReportUnavailable(string message)
    {
        _viewModel.Set("OperationSummary", message);
        Avalonia.Threading.Dispatcher.UIThread.Post(() => _loader?.RefreshBindings());
    }

    private void OnHomeCoreChanged(HomeCoreDependencySignal signal)
    {
        var ready = signal.Services.Count(service => service.IsAvailable);
        var unavailable = signal.Services.Count - ready;
        _viewModel.Set("HomeCoreSummary", $"Home Core {ready} services available; {unavailable} unavailable.");
        Avalonia.Threading.Dispatcher.UIThread.Post(() => _loader?.RefreshBindings());
    }

    internal HomeCoreRuntime HomeCore => _homeCore;
    internal IHomeCoreApi HomeCoreApi => _homeCoreApi;
    internal IHomeCoreStateStore HomeCoreStateStore => _coreStateStore;
    internal IHomeProductivityEngine ProductivityEngine => _productivityEngine.Engine;
    internal IHomeFeatureNavigationHost FeatureNavigation => _featureNavigation;
}

internal sealed class DenyAllHomeCoreAuthorization : IHomeCoreAuthorization
{
    public ValueTask<bool> IsAllowedAsync(HomeCallerIdentity caller, string target, IReadOnlySet<string> scopes,
        CancellationToken cancellationToken = default) => ValueTask.FromResult(false);
}
