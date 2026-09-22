// CUI Boards — Avalonia 12.0.1 Win32 desktop entry point.
// Loads Boards.cui and opens a native window with Skia rendering.
// Mirrors apps/Home/src/AvaloniaHome packaging: .cui discovery, fallback UI,
// diagnostics printed to console. No AXAML.

using System;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Media;
using CakeOS.Apps.Boards.Contract;
using CakeOS.Cui.Runtime;

namespace CakeOS.Apps.Boards.App;

internal static class Program
{
    internal static string? OpenPath;

    [STAThread]
    public static void Main(string[] args)
    {
        Console.WriteLine("[CUI Boards] Starting Avalonia Win32 + Skia...");
        if (args.Length > 0 && !string.IsNullOrWhiteSpace(args[0]))
        {
            OpenPath = args[0];
            Console.WriteLine($"[CUI Boards] Board argument: {OpenPath}");
        }

        var cuiPath = FindCuiFile();
        Console.WriteLine($"[CUI Boards] .cui file: {cuiPath ?? "NOT FOUND"}");

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
    {
        return AppBuilder.Configure<BoardsApp>()
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
            Path.Combine(baseDir, "Boards.cui"),
            Path.Combine(baseDir, "ui", "Boards.cui"),
        };

        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate))
                return candidate;
        }

        var dir = new DirectoryInfo(baseDir);
        for (var i = 0; i < 12 && dir != null; i++)
        {
            var path = Path.Combine(dir.FullName, "9to1 Workspace", "Boards", "app", "Boards.cui");
            if (File.Exists(path))
                return path;
            var alt = Path.Combine(dir.FullName, "Boards", "app", "Boards.cui");
            if (File.Exists(alt))
                return alt;
            dir = dir.Parent;
        }

        return null;
    }
}

/// <summary>
/// Avalonia Application hosting the Boards CUI surface with a live
/// BoardsViewModel bound to an IRichBoardSession.
/// </summary>
internal sealed class BoardsApp : Application
{
    private IRichBoardSession? _session;
    private BoardsViewModel? _viewModel;
    private CuiControlLoader? _loader;

    public override void OnFrameworkInitializationCompleted()
    {
        // Real durable session: edits persist to a physical .9to1board file.
        // Optional CLI arg opens/creates that board; otherwise the default board opens.
        var storeRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "9to1", "Boards");
        var store = new JsonFileHavenBoardStore(storeRoot);
        _session = ContractSessionAdapter.OpenAsync(store, Program.OpenPath).GetAwaiter().GetResult();
        _viewModel = new BoardsViewModel(_session);

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = BuildWindow();
        }

        base.OnFrameworkInitializationCompleted();
    }

    private Window BuildWindow()
    {
        var window = new Window
        {
            Title = "Boards",
            Width = 1200,
            Height = 800,
        };

        window.KeyDown += (s, e) =>
        {
            if (_viewModel is null)
                return;
            if (e.Key == Key.S && e.KeyModifiers.HasFlag(KeyModifiers.Control))
                _ = _viewModel.DispatchAsync("SaveBoard", null);
            else if (e.Key == Key.Escape)
                _viewModel.Set("StatusText", "Ready");
        };

        // Flush pending edits before the window closes so close never loses notes.
        window.Closing += (_, _) =>
        {
            try { _session?.SaveAsync().AsTask().GetAwaiter().GetResult(); }
            catch (Exception error) { Console.WriteLine($"[CUI Boards] Save on close failed: {error.Message}"); }
        };
        window.Closed += (_, _) =>
        {
            try { _session?.DisposeAsync().AsTask().GetAwaiter().GetResult(); }
            catch (Exception error) { Console.WriteLine($"[CUI Boards] Dispose failed: {error.Message}"); }
        };

        var cuiPath = Program.FindCuiFile();
        if (cuiPath != null && _viewModel is not null)
        {
            Console.WriteLine($"[CUI Boards] Loading .cui: {cuiPath}");
            _loader = new CuiControlLoader();
            _loader.SetBindingContext(_viewModel);
            _loader.SetActionDispatcher(_viewModel);

            var (root, diagnostics) = _loader.LoadFile(cuiPath);
            Console.WriteLine($"[CUI Boards] Diagnostics: {diagnostics.Count}");
            foreach (var diagnostic in diagnostics)
                Console.WriteLine($"[CUI Boards] Diagnostic: {diagnostic}");

            if (root != null)
            {
                _loader.WireBindings(root);
                _viewModel.Attach(root);
                WireInkCanvas(root);
                window.Content = root;
                Console.WriteLine($"[CUI Boards] Root: {root.GetType().Name} — window rendered");
            }
            else
            {
                window.Content = CreateFallbackUI(diagnostics.Any()
                    ? string.Join("\n", diagnostics.Select(d => d.ToString()))
                    : "Failed to load CUI");
            }
        }
        else
        {
            window.Content = CreateFallbackUI("No .cui file found");
        }

        return window;
    }

    private void WireInkCanvas(Control root)
    {
        if (_viewModel is null)
            return;
        var canvas = FindByName<Canvas>(root, "InkCanvas");
        if (canvas is null)
            return;

        // One press-drag-release gesture commits ONE ink stroke so the persisted
        // document holds real drawing data, not a bare stroke counter.
        var drawing = false;
        var points = new System.Collections.Generic.List<(double X, double Y)>();
        canvas.PointerPressed += (s, e) =>
        {
            drawing = true;
            points.Clear();
            var position = e.GetPosition(canvas);
            points.Add((position.X, position.Y));
            DrawDot(canvas, position);
        };
        canvas.PointerMoved += (s, e) =>
        {
            if (!drawing)
                return;
            var position = e.GetPosition(canvas);
            points.Add((position.X, position.Y));
            DrawDot(canvas, position);
        };
        canvas.PointerReleased += (s, e) =>
        {
            drawing = false;
            if (points.Count > 0 && _viewModel is not null)
                _ = _viewModel.CommitInkStrokeAsync(points.ToArray());
        };
    }

    private static void DrawDot(Canvas canvas, Avalonia.Point position)
    {
        // Visual feedback only; the stroke is committed once per gesture on release.
        canvas.Children.Add(new Avalonia.Controls.Shapes.Ellipse
        {
            Width = 6,
            Height = 6,
            Fill = Brushes.Black,
        });
        var dot = canvas.Children[^1];
        Canvas.SetLeft(dot, position.X - 3);
        Canvas.SetTop(dot, position.Y - 3);
    }

    private static T? FindByName<T>(Control root, string name) where T : Control
    {
        if (root is T match && string.Equals(root.Name, name, StringComparison.Ordinal))
            return match;
        foreach (var child in LogicalChildren(root))
        {
            var found = FindByName<T>(child, name);
            if (found is not null)
                return found;
        }
        return null;
    }

    private static System.Collections.Generic.IEnumerable<Control> LogicalChildren(Control control)
    {
        if (control is Panel panel)
        {
            foreach (var child in panel.Children)
                if (child is Control c)
                    yield return c;
        }
        else if (control is Decorator decorator && decorator.Child is Control decChild)
        {
            yield return decChild;
        }
        else if (control is ContentControl cc && cc.Content is Control ccChild)
        {
            yield return ccChild;
        }
        else if (control is ItemsControl ic)
        {
            foreach (var item in ic.Items)
                if (item is Control icChild)
                    yield return icChild;
        }
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
            Text = "Boards",
            FontSize = 32,
            FontWeight = FontWeight.Bold,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 16),
        });

        panel.Children.Add(new TextBlock
        {
            Text = message,
            FontSize = 14,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 600,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
        });

        return panel;
    }
}
