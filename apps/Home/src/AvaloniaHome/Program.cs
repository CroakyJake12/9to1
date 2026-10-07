// 9-1 Home native host. The canonical Home.cui surface is loaded through CUI.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
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
using NineToOne.Accounts.Native;

namespace AvaloniaHome;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        Console.WriteLine("[9-1 Home] Starting native CUI host...");

        var cuiPath = FindCuiFile();
        Console.WriteLine($"[9-1 Home] canonical .cui file: {cuiPath ?? "NOT FOUND"}");

        var exitCode = BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        if (Application.Current is not HomeApp app || app.OriginalProcessShutdown is not { } original)
        {
            Console.Error.WriteLine("[9-1 Home] The desktop exited without an original explicit Home shutdown.");
            return 1;
        }
        try { original.GetAwaiter().GetResult(); }
        catch (Exception error)
        {
            Console.Error.WriteLine($"[9-1 Home] Original shutdown failed: {error.GetType().Name}");
            return 1;
        }
        return exitCode;
    }

    public static AppBuilder BuildAvaloniaApp()
    {
        return AppBuilder.Configure<HomeApp>()
            .UseWin32()
            .UseSkia()
            .UseHarfBuzz()
            .WithInterFont();
    }

    /// <summary>The trusted native producer supplies the protected verifier and registered owners;
    /// no command-line app claim or compatibility response constructs this composition.</summary>
    internal static AppBuilder BuildOriginalWindowsApp(HomeNativeWindowsComposition originalComposition,
        INativeCakeAccountSession? borrowedAccountSession = null)
    {
        ArgumentNullException.ThrowIfNull(originalComposition);
        return AppBuilder.Configure(() => new HomeApp(originalComposition, borrowedAccountSession))
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
internal sealed partial class HomeApp : Application
{
    private readonly CuiViewModel _viewModel = new();
    private readonly HomeDashboard _dashboard = new();
    private readonly IHomeCoreStateStore _coreStateStore;
    private readonly HomeNativeWindowsComposition _nativeComposition;
    private readonly HomeProductivityEngineService _productivityEngine;
    private readonly HomeCoreRuntime _homeCore;
    private readonly HomeCoreApi _homeCoreApi;
    private readonly HomeFeatureNavigationHost _featureNavigation = new();
    private HomeCuiController _controller;
    private IDisposable? _homeCoreSubscription;
    private CuiControlLoader? _loader;
    private readonly HomeHostOriginalLifetime _originalLifetime;
    private IClassicDesktopStyleApplicationLifetime? _desktop;
    private bool _nativeInitializationFailed;

    public HomeApp() : this(HomeNativeWindowsComposition.CreateCandidate()) { }

    internal HomeApp(HomeNativeWindowsComposition originalComposition, INativeCakeAccountSession? borrowedAccountSession = null)
    {
        _nativeComposition = originalComposition ?? throw new ArgumentNullException(nameof(originalComposition));
        _coreStateStore = originalComposition.StateStore;
        _productivityEngine = originalComposition.Productivity;
        _homeCore = originalComposition.Runtime;
        _homeCoreApi = originalComposition.Api;
        _controller = new HomeCuiController(_dashboard);
        _originalLifetime = new HomeHostOriginalLifetime(CloseOriginalCoreAsync, ExitOriginalDesktopAsync);
        _nativeCakeAccount = new HomeNativeCakeAccountOwner(borrowedAccountSession);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        try
        {
            // Home Core is ready before Home presents its normal shell. State corruption leaves the
            // control plane explicitly degraded while preserving the original state for repair.
            var originalStart = _originalLifetime.TryRunOriginal(_nativeComposition.StartOriginalAsync)
                ?? throw new InvalidOperationException("The original native Home startup was refused.");
            originalStart.GetAwaiter().GetResult();
            var workspaceStart = _originalLifetime.TryRunOriginal(InitializeWorkspaceAsync)
                ?? throw new InvalidOperationException("The original Home workspace startup was refused.");
            workspaceStart.GetAwaiter().GetResult();
            if (!_nativeComposition.InstalledPeerAdmissionConfigured)
                Console.WriteLine("[9-1 Home] Protected installed-peer verification is not configured; app admission is unavailable.");
            ApplySnapshot(_controller.ShowCurrent());
            _homeCoreSubscription = _homeCore.Subscribe(OnHomeCoreChanged);
            _viewModel.On("InstallAllUpdates", _ => _ = RunOriginalNativeWork(InstallAllAsync));
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
            RegisterWorkspaceActions();
            RegisterNativeCakeAccountActions();

            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                // Closing the visible shell must not tear down the shared service lifetime. The core
                // stops only when the process receives an explicit application shutdown request.
                desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                _desktop = desktop;
                desktop.ShutdownRequested += (_, request) =>
                {
                    request.Cancel = true;
                    _ = RequestProcessShutdownAsync();
                };
                var window = BuildWindow();
                desktop.MainWindow = window;
                ConfigureOriginalShellTray();
            }

            base.OnFrameworkInitializationCompleted();
        }
        catch (Exception original)
        {
            // No native loop has started. Drain the original Home owner without queuing an
            // exit callback to a dispatcher whose initialization just failed.
            _nativeInitializationFailed = true;
            Exception? trayFailure = null;
            try { RetireOriginalShellTray(); } catch (Exception error) { trayFailure = error; }
            Exception? cleanup = null;
            try { RequestOriginalHostShutdownAsync().GetAwaiter().GetResult(); }
            catch (Exception error) { cleanup = error; }
            var failures = new List<Exception> { original };
            if (trayFailure is not null && !ReferenceEquals(original, trayFailure)) failures.Add(trayFailure);
            if (cleanup is not null && !failures.Any(error => ReferenceEquals(error, cleanup))) failures.Add(cleanup);
            if (failures.Count > 1)
                throw new AggregateException("Original native Home initialization and shutdown failed.", failures);
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(original).Throw();
            throw;
        }
    }

    internal Window BuildWindow()
    {
        if (_workspaceWindow?.IsVisible == true)
            throw new InvalidOperationException("The original Home shell is already open.");
        _loader?.Dispose();
        CuiThemeScopeApplier.ApplyGlobalTheme(CuiThemePreferenceReader.Read());
        var window = new Window
        {
            Title = "9-1 Home",
            Width = 1200,
            Height = 800,
        };

        window.KeyDown += (s, e) =>
        {
            if (e.Key == Key.Q && e.KeyModifiers == (KeyModifiers.Control | KeyModifiers.Shift))
            {
                e.Handled = true;
                _ = RequestProcessShutdownAsync();
            }
            else if (e.Key == Key.F5)
                _ = RunOriginalNativeWork(RefreshAsync);
        };

        var cuiPath = Program.FindCuiFile();
        if (cuiPath != null)
        {
            Console.WriteLine($"[9-1 Home] Loading canonical .cui: {cuiPath}");
            _loader = new CuiControlLoader();
            RegisterWorkspaceControls(_loader, window);
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
        _viewModel.Set(nameof(HomeCuiSurface.IsDashboard), _controller.Surface.IsDashboard);
        _viewModel.Set(nameof(HomeCuiSurface.IsLibrary), _controller.Surface.IsLibrary);
        _viewModel.Set(nameof(HomeCuiSurface.IsEvents), _controller.Surface.IsEvents);
        _viewModel.Set("CatalogSummary", snapshot.Catalog.Status.Message);
        _viewModel.Set("RuntimeSummary", snapshot.Runtime.Runtime.Message);
        _viewModel.Set("EventsSummary", "An events provider is not configured in this host.");
        _viewModel.Set("OperationSummary", $"{snapshot.LastOperation.State}: {snapshot.LastOperation.Message}");
        ApplyWorkspaceBindings();
        _loader?.RefreshBindings();
    }

    private void ReportUnavailable(string message) => _ = RunOriginalNativeWork(() =>
    {
        _viewModel.Set("OperationSummary", message);
        _loader?.RefreshBindings();
        return Task.CompletedTask;
    });

    private void OnHomeCoreChanged(HomeCoreDependencySignal signal) => _ = RunOriginalNativeWork(() =>
    {
        var ready = signal.Services.Count(service => service.IsAvailable);
        var unavailable = signal.Services.Count - ready;
        _viewModel.Set("HomeCoreSummary", $"Home Core {ready} services available; {unavailable} unavailable.");
        _loader?.RefreshBindings();
        return Task.CompletedTask;
    });

    private Task? RunOriginalNativeWork(Func<Task> callback) =>
        _originalLifetime.TryRunOriginal(() => Avalonia.Threading.Dispatcher.UIThread.CheckAccess()
            ? callback()
            : Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(callback));

    internal Task? OriginalProcessShutdown => _originalLifetime.OriginalShutdown;

    /// <summary>Explicit Quit Home (Ctrl+Shift+Q), distinct from closing the visible window.</summary>
    internal Task RequestProcessShutdownAsync()
    {
        if (_desktop is null)
            throw new InvalidOperationException("The original native desktop lifetime is not registered.");
        return RequestOriginalHostShutdownAsync();
    }

    private Task RequestOriginalHostShutdownAsync()
    {
        // The account owner publishes/seals its close and stops actual auth/helper sources
        // BEFORE the parent waits for admitted native actions. A self-join refuses first.
        _ = PrepareOriginalNativeAccountClose(); // SAME task remains retained by its owning field.
        return _originalLifetime.RequestShutdownAsync();
    }

    private async Task CloseOriginalCoreAsync()
    {
        var failures = new List<Exception>();
        try { _homeCoreSubscription?.Dispose(); }
        catch (Exception error) { HomeNativeCakeCauses.Add(failures, error); }
        finally { _homeCoreSubscription = null; }

        Task? accountClose = null, coreClose = null;
        try { accountClose = _nativeCakeAccount.CloseAndDrainAsync(); }
        catch (Exception error) { HomeNativeCakeCauses.Add(failures, error); }
        try { coreClose = _nativeComposition.CloseAndDrainAsync(); }
        catch (Exception error) { HomeNativeCakeCauses.Add(failures, error); }
        // Start both original owners independently, then preserve every original fault.
        if (accountClose is not null) await HomeNativeCakeCauses.JoinAsync(accountClose, failures).ConfigureAwait(false);
        if (coreClose is not null) await HomeNativeCakeCauses.JoinAsync(coreClose, failures).ConfigureAwait(false);
        HomeNativeCakeCauses.Throw(failures);
    }

    internal Task CloseAndDrainOriginalHostAsync() => RequestOriginalHostShutdownAsync();

    private Task ExitOriginalDesktopAsync(int exitCode) =>
        _nativeInitializationFailed || (_desktop is null && ApplicationLifetime is null) ? Task.CompletedTask :
            Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                var failures = new List<Exception>();
                try { RetireOriginalShellTray(); } catch (Exception error) { failures.Add(error); }
                try { _desktop!.Shutdown(exitCode); } catch (Exception error) { failures.Add(error); }
                if (failures.Count == 1) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failures[0]).Throw();
                if (failures.Count > 1) throw new AggregateException("Original Home tray and desktop retirement failed.", failures);
            }).GetTask();

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
