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
using HavenOS.Home;

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
    private readonly HomeCuiController _controller;
    private CuiControlLoader? _loader;

    public HomeApp()
    {
        _controller = new HomeCuiController(_dashboard);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        ApplySnapshot(_controller.ShowCurrent());
        _viewModel.On("InstallAllUpdates", _ => _ = InstallAllAsync());
        _viewModel.On("OpenStudio", _ => ReportUnavailable("Studio navigation is not connected in this host."));
        _viewModel.On("OpenWrite", _ => ReportUnavailable("Write navigation is not connected in this host."));
        _viewModel.On("OpenBrowse", _ => ReportUnavailable("Browse navigation is not connected in this host."));
        _viewModel.On("OpenData", _ => ReportUnavailable("Data navigation is not connected in this host."));
        _viewModel.On("OpenBoards", _ => ReportUnavailable("Boards navigation is not connected in this host."));
        _viewModel.On("NavigateHome", _ => ReportUnavailable("Home is already open."));
        _viewModel.On("NavigateSpaces", _ => ReportUnavailable("Spaces navigation is not connected in this host."));
        _viewModel.On("NavigateApps", _ => ReportUnavailable("Apps navigation is not connected in this host."));
        _viewModel.On("NavigateLibrary", _ => ReportUnavailable("Library navigation is not connected in this host."));
        _viewModel.On("NavigateEvents", _ => ReportUnavailable("Events navigation is not connected in this host."));
        _viewModel.On("NavigateAutomations", _ => ReportUnavailable("Automations navigation is not connected in this host."));
        _viewModel.On("NavigateDiscover", _ => ReportUnavailable("Discover navigation is not connected in this host."));
        _viewModel.On("NavigateSettings", _ => ReportUnavailable("Settings navigation is not connected in this host."));

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var window = BuildWindow();
            desktop.MainWindow = window;
        }

        base.OnFrameworkInitializationCompleted();
    }

    internal Window BuildWindow()
    {
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
}
