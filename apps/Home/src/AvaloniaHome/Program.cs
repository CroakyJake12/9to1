// CUI Home — Avalonia 12.0.1 Win32 desktop entry point.
// Loads a real .cui file and opens a native window with Skia rendering.
// Proves: bindings, events, resources, styles, keyboard/pointer input, accessibility.

using System;
using System.IO;
using System.Linq;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using CakeOS.Cui.Runtime;

namespace AvaloniaHome;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        Console.WriteLine("[CUI Home] Starting Avalonia Win32 + Skia...");

        var cuiPath = FindCuiFile();
        Console.WriteLine($"[CUI Home] .cui file: {cuiPath ?? "NOT FOUND"}");

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
            Path.Combine(baseDir, "ui", "Home.cui"),
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
            var path = Path.Combine(dir.FullName, "apps", "Home", "ui", "Home.cui");
            if (File.Exists(path))
                return path;
            dir = dir.Parent;
        }

        return null;
    }
}

/// <summary>
/// Avalonia Application that loads a .cui file into a real window.
/// Demonstrates live bindings, event handling, keyboard input, and accessibility.
/// </summary>
internal sealed class HomeApp : Application
{
    private readonly CuiViewModel _viewModel = new();
    private CuiControlLoader? _loader;

    public override void OnFrameworkInitializationCompleted()
    {
        // Set up live binding context — proves CUI bindings connect to real data
        _viewModel.Set("AppName", "CUI Home");
        _viewModel.Set("WelcomeMessage", "Welcome to 9to1 — your desktop, powered by CUI");
        _viewModel.Set("ClickCount", "0");
        _viewModel.Set("StatusText", "Ready");
        _viewModel.Set("CurrentTime", DateTime.Now.ToString("HH:mm:ss"));

        // Register action handlers — proves CUI action dispatch works
        _viewModel.On("OpenChat", _ => _viewModel.Set("StatusText", "Opening Chat..."));
        _viewModel.On("OpenTasks", _ => _viewModel.Set("StatusText", "Opening Tasks..."));
        _viewModel.On("OpenBrowse", _ => _viewModel.Set("StatusText", "Opening Browse..."));
        _viewModel.On("OpenFiles", _ => _viewModel.Set("StatusText", "Opening Files..."));
        _viewModel.On("OpenData", _ => _viewModel.Set("StatusText", "Opening Data..."));
        _viewModel.On("OpenTerminal", _ =>
        {
            var count = int.TryParse(_viewModel.Get("ClickCount")?.ToString(), out var c) ? c : 0;
            _viewModel.Set("ClickCount", (count + 1).ToString());
            _viewModel.Set("StatusText", $"Terminal opened {count + 1} time(s)");
        });

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var window = BuildWindow();
            desktop.MainWindow = window;

            // Periodic time update — proves live binding refresh works
            var timer = new Timer(_ =>
            {
                _viewModel.Set("CurrentTime", DateTime.Now.ToString("HH:mm:ss"));
                Avalonia.Threading.Dispatcher.UIThread.Post(() => _loader?.RefreshBindings());
            }, null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
        }

        base.OnFrameworkInitializationCompleted();
    }

    private Window BuildWindow()
    {
        var window = new Window
        {
            Title = "CUI Home",
            Width = 1200,
            Height = 800,
        };

        // Add keyboard handler — proves keyboard input works
        window.KeyDown += (s, e) =>
        {
            if (e.Key == Key.F5)
                _viewModel.Set("CurrentTime", DateTime.Now.ToString("HH:mm:ss"));
            else if (e.Key == Key.Escape)
                _viewModel.Set("StatusText", "Ready");
        };

        var cuiPath = Program.FindCuiFile();
        if (cuiPath != null)
        {
            Console.WriteLine($"[CUI Home] Loading .cui: {cuiPath}");
            _loader = new CuiControlLoader();
            _loader.SetBindingContext(_viewModel);
            _loader.SetActionDispatcher(_viewModel);

            var (root, diagnostics) = _loader.LoadFile(cuiPath);
            Console.WriteLine($"[CUI Home] Diagnostics: {diagnostics.Count}");

            if (root != null)
            {
                // Wire live bindings and event handlers
                _loader.WireBindings(root);
                window.Content = root;
                Console.WriteLine($"[CUI Home] Root: {root.GetType().Name} — window rendered");
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
            Text = "CUI Home",
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
}
