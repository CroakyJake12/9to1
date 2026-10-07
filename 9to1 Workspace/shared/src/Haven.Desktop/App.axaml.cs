using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Haven.Application;
using Haven.Application.Automations;
using Haven.Browser;
using Haven.Core;
using Haven.Desktop.Events;
using Haven.Desktop.Services;
using Haven.Desktop.Views.Shell;
using Haven.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Haven.Desktop;

public sealed partial class App : Avalonia.Application
{
#if DEBUG
    private static int _developerToolsAttached;
#endif
    private ServiceProvider? _services;
    private IStartupRecoveryCoordinator? _startupRecovery;
    private IProductionDiagnostics? _productionDiagnostics;
    private bool _exceptionHooksAttached;
    internal static IServiceProvider? Services { get; private set; }

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
        #if DEBUG
            // Avalonia's developer-tools service is process-wide. Headless tests create
            // multiple isolated App instances in one process, so attaching per instance
            // causes cleanup failures after otherwise successful tests.



            if (Interlocked.Exchange(ref _developerToolsAttached, 1) == 0)
                this.AttachDeveloperTools();
        #endif
    }

    public override void OnFrameworkInitializationCompleted()
    {
        var collection = new ServiceCollection();
        collection.AddHavenInfrastructure();
        collection.AddHavenPlannerInfrastructure();
        collection.AddHavenDesktopCallServices();
#if ANDROID
        global::Haven.Android.AndroidServiceRegistration.AddHavenAndroidPlatformServices(collection);
#endif
        collection.AddHavenMesh();
        collection.AddSingleton<ScheduledTaskScheduleCalculator>();
        collection.AddSingleton<ScheduledTaskRunner>();
        collection.AddHavenOriginalTaskExecutionServices();
        collection.AddSingleton<UserPreferencesService>();
        collection.AddSingleton<Services.AvatarStore>();
        collection.AddSingleton<Services.OllamaWakeService>();
        collection.AddSingleton<ProjectCreationService>();
        collection.AddSingleton<NotificationService>();
        collection.AddSingleton<ComputerUseOverlayCoordinator>();
#if !ANDROID
        collection.AddSingleton<Haven.Desktop.Overlay.OverlayWorkspaceRegistry>();
        collection.AddSingleton<Haven.Desktop.Overlay.OverlayContextActionCandidateService>();
        collection.AddSingleton<Haven.Desktop.Overlay.OverlayForegroundContextCaptureService>();
        collection.AddSingleton<Haven.Desktop.Overlay.OverlayVisualContextCaptureService>();
        collection.AddSingleton<Haven.Desktop.Overlay.OverlayRegionCaptureService>();
        collection.AddSingleton<Haven.Desktop.Overlay.OverlayChatSessionFactory>();
        collection.AddSingleton<Haven.Desktop.Overlay.OverlayGoSessionFactory>();
        collection.AddSingleton<Haven.Desktop.Overlay.OverlayGlobalHotkey>();
        collection.AddSingleton<Haven.Desktop.Overlay.OverlayWorkspaceController>();
#endif
        // Legacy automation delivery polling retired; Tasks owns execution state.
        
        collection.AddSingleton<FloatingActivityStateStore>();
        collection.AddSingleton<Haven.Desktop.Views.Pages.Imagine.VisionWorkspaceStateStore>();
#if ANDROID
        collection.AddSingleton<IFloatingActivityHost, global::Haven.Android.Compatibility.AndroidFloatingActivityHost>();
#else
        collection.AddSingleton<IFloatingActivityHost, DesktopFloatingActivityHost>();
#endif
        
        collection.AddSingleton<HavenEventBus>();
        collection.AddSingleton<Haven.Desktop.ViewModels.ProviderConnectionsViewModel>();
        collection.AddTransient<MainView>();
        collection.AddSingleton<WorkspaceSessionCoordinator>();
        collection.AddSingleton<WorkspaceWindowService>();
#if !ANDROID
        ConfigureOriginalWindowsHomeRegistrations(collection);
        ConfigureOriginalNativeDevelopmentRegistrations(collection);
        ConfigureOriginalWindowsDeveloperRegistrations(collection);
        AddNativeCakeAccountServices(collection);
        ConfigureOriginalNativeHomeApprovalServices(collection);
#endif
        _services = collection.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true
        });
        Services = _services;
#if !ANDROID
        CaptureOriginalCanonicalProcessOwner(_services);
        CaptureOriginalWindowsDeveloperBorrowers(_services);
#endif
        _actualComputerUseOverlay = ResolveOriginalComputerUseOverlay(_services);
        Subscribe.EventBus = _services.GetRequiredService<HavenEventBus>();
        _startupRecovery = _services.GetRequiredService<IStartupRecoveryCoordinator>();
        _productionDiagnostics = _services.GetRequiredService<IProductionDiagnostics>();
        AttachExceptionHooks();
        AttachUpdateServices();

#if !ANDROID
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            desktop.Exit += OnDesktopExit;

            var preferences = _services.GetRequiredService<UserPreferencesService>();
            preferences.ApplyAppearance(preferences.Appearance, save: false);
            var mainView = _services.GetRequiredService<MainView>();
            mainView.ApplyEdition(HavenStartupExperiencePolicy.Edition);
            _services.GetRequiredService<WorkspaceSessionCoordinator>().Register(mainView, WorkspaceWindowKind.Main, queueSave: false);
            var window = new MainWindow(preferences) { DataContext = mainView, PreserveWorkspaceSessionOnClose = true };
            ConfigureOriginalWindowsNativeRoutes(window, mainView);
            ConfigureOriginalDesktopShutdown(desktop, window, mainView);
            window.Opened += (_, _) => { _ = InitialiseHaven(mainView); };
            desktop.MainWindow = window;
        }
#endif

        base.OnFrameworkInitializationCompleted();
    }

    private Task InitialiseHaven(MainView shell) => _originalAppWork.RunAsync(
        original => InitialiseOriginalHavenAsync(original, shell), actual => _actualStartupOriginal = actual);

    private async Task InitialiseOriginalHavenAsync(DesktopOriginalWorkLifetime.Original original, MainView shell)
    {
        var correlationId = Guid.NewGuid().ToString("N");
        try
        {
            var services = _services ?? throw new InvalidOperationException("Haven services have not been initialized.");
            var recovery = _startupRecovery ?? services.GetRequiredService<IStartupRecoveryCoordinator>();
            var recoveryState = await original.AwaitAsync(AcquireOriginalAppSynchronous(original,
                () => recovery.BeginStartupAsync(CancellationToken.None)));
            AcquireOriginalAppSynchronous(original, () =>
            {
                BrowserAutomationRegistry.Register(
                    services.GetRequiredService<BrowserSessionService>(),
                    services.GetRequiredService<IBrowserAutomationService>());
                return true;
            });

            var lifecycle = services.GetRequiredService<IApplicationLifecycle>();
            await original.AwaitAsync(AcquireOriginalAppSynchronous(original, () => lifecycle.CrashRecoveryAsync(CancellationToken.None)));
            await original.AwaitAsync(AcquireOriginalAppSynchronous(original, () => lifecycle.StartupAsync(CancellationToken.None)));
#if !ANDROID
            await original.AwaitAsync(AcquireOriginalAppSynchronous(original, StartOriginalWindowsHomeAsync));
#endif
            await original.AwaitAsync(AcquireOriginalAppSynchronous(original, () => services.GetRequiredService<ModeSeedService>().SeedBuiltInModesAsync(CancellationToken.None)));
            var migration = await original.AwaitAsync(AcquireOriginalAppSynchronous(original,
                () => services.GetRequiredService<ILegacyStateMigrator>().MigrateIfNeededAsync(CancellationToken.None)));
            await original.AwaitAsync(AcquireOriginalAppSynchronous(original, () => shell.InitializeAsync(migration, CancellationToken.None)));
            await original.AwaitAsync(AcquireOriginalAppSynchronous(original, () => shell.RestoreWorkspaceSessionAsync(CancellationToken.None)));

#if !ANDROID
            await original.AwaitAsync(AcquireOriginalAppSynchronous(original,
                () => OpenOriginalInitialProductRouteAsync(shell, original.Token)));
#endif

#if !ANDROID
            await original.AwaitAsync(AcquireOriginalAppSynchronous(original, () => services.GetRequiredService<Haven.Desktop.Overlay.OverlayWorkspaceController>().InitializeAsync(CancellationToken.None)));
#endif
            // Scheduled Tasks have no parallel automation delivery loop.

            if (recoveryState.IsSafeMode)
            {
                AcquireOriginalAppSynchronous(original, () =>
                {
                    services.GetRequiredService<NotificationService>().Show(
                        "Haven recovery safe mode",
                        recoveryState.Reason + " Local Ollama chat and read-only workspace inspection remain available.",
                        ToastKind.Warning, TimeSpan.FromSeconds(30));
                    return true;
                });
            }

            await original.AwaitAsync(AcquireOriginalAppSynchronous(original, () => recovery.MarkStartupCompletedAsync(CancellationToken.None)));

            // Optional Mesh warmup must not delay interactive readiness; data-safety
            // work above stays on the critical path, Mesh does not.
            if (!recoveryState.IsSafeMode)
            {
                try
                {
                    await original.AwaitAsync(AcquireOriginalAppSynchronous(original, () => services.GetRequiredService<MeshCoordinator>().InitialiseAsync(CancellationToken.None)));
                }
                catch (Exception meshException)
                {
                    original.Retain(meshException); // Optional UI startup tolerance is not a clean original drain.
                    try
                    {
                        await original.AwaitAsync(AcquireOriginalAppSynchronous(original,
                            () => (_productionDiagnostics ?? services.GetRequiredService<IProductionDiagnostics>()).WriteAsync(
                                ReliabilitySeverity.Warning,
                                "mesh",
                                "startup-unavailable",
                                meshException.ToString(),
                                cancellationToken: CancellationToken.None).AsTask()));
                    }
                    catch (Exception diagnosticFailure)
                    {
                        original.Retain(diagnosticFailure); // UI tolerance keeps both real causes inspectable.
                    }
                }
            }
        }
        catch (Exception ex)
        {
            original.Retain(ex);
            try
            {
                await original.AwaitAsync(AcquireOriginalAppSynchronous(original, () => LogExceptionAsync("startup-failed", ex, correlationId)));
            }
            catch (Exception diagnosticFailure)
            {
                original.Retain(diagnosticFailure); // Never hide the primary or actual sink failure.
            }

            var userMessage = $"Haven could not finish starting. Diagnostic reference: {correlationId}.";
            AcquireOriginalAppSynchronous(original, () => { shell.SetStartupError(userMessage); return true; });
            try
            {
                AcquireOriginalAppSynchronous(original, () =>
                {
                    _services?.GetService<NotificationService>()?.Show(
                        "Haven startup problem", userMessage, ToastKind.Error, TimeSpan.FromSeconds(30));
                    return true;
                });
            }
            catch (Exception toastFailure)
            {
                original.Retain(toastFailure); // Persistent UI status still exists; failed original remains inspectable.
            }
        }
    }

    private void OnDesktopExit(object? sender, ControlledApplicationLifetimeExitEventArgs e)
    {
        // Exit cannot hold Avalonia's dispatcher alive. The actual pre-close owner
        // must already have joined originals/provider and issued its final writer.
        if (_actualShutdownSequence?.OriginalShutdown is not { IsCompletedSuccessfully: true } ||
            _actualWindowClosure is not { IsCompletedSuccessfully: true })
            _actualShutdownFailure ??= new InvalidOperationException(
                "Desktop exited without the actual original pre-close drain; clean shutdown was not acknowledged.");
    }

    private void AttachUpdateServices()
    {
        try
        {
            var services = _services;
            var updates = services?.GetService<IUpdateService>();
            if (services is null || updates is null) return;

            // Every lifecycle transition is recorded so Settings/About can show honest state even
            // when it changed before any surface existed. Failures arrive here as Failed reports.
            _actualSubscribedUpdates = updates;
            updates.StatusChanged += OnUpdateStatusChanged;
            UpdateOrchestrator.PendingUpdateDetectedOnStartup += OnPendingStartupUpdateDetected;

            var paths = services.GetService<IAppPaths>();
            var version = services.GetService<Func<string>>();
            if (paths is not null && version is not null)
            {
                try
                {
                    UpdateOrchestrator.ApplyOnStartupCheck(
                        paths.DataDirectory,
                        version(),
                        WindowsInstallationDetector.DetectInstallationSource().Source);
                }
                catch (Exception detectionFailure)
                {
                    _ = detectionFailure;
                    // Staged-update detection is best-effort; absence of a result must never block startup.
                }
            }

            _ = _originalAppWork.RunAsync(async original =>
            {
                var actualWorker = AcquireOriginalAppSynchronous(original, () => Task.Run(async () =>
                {
                    try
                    {
                        await original.AwaitAsync(AcquireOriginalAppSynchronous(original,
                            () => updates.CheckInBackgroundAsync(CancellationToken.None)));
                    }
                    catch (Exception error) { original.Retain(error); } // Optional UI tolerance; original fault remains owned.
                }));
                await original.AwaitAsync(actualWorker);
            });
        }
        catch
        {
            // Update plumbing is optional to process start; problems resurface through the Settings Updates section.
        }
    }

    private void OnUpdateStatusChanged(UpdateStatusReport report)
    {
        if (_originalAppWork.IsRetiring) return;
        _originalAppWork.RunSynchronous(original => AcquireOriginalAppSynchronous(original, () =>
        { UpdateStatusSnapshot.Record(report); return true; }));
    }

    private void OnPendingStartupUpdateDetected(UpdateStatusReport report)
    {
        if (_originalAppWork.IsRetiring) return; // Detached event cannot admit new borrowed work after the permanent seal.
        _ = _originalAppWork.RunAsync(async original =>
        {
            try
            {
                AcquireOriginalAppSynchronous(original, () => { UpdateStatusSnapshot.Record(report); return true; });
                var notification = AcquireOriginalAppSynchronous(original, () => _services?.GetService<NotificationService>());
                var actualDispatch = AcquireOriginalAppSynchronous(original,
                    () => Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        try
                        {
                            AcquireOriginalAppSynchronous(original, () =>
                            {
                                notification?.Show("Staged update waiting", report.Message ??
                                    "An update staged in a previous session is waiting for the external installer to apply it on the next start.",
                                    ToastKind.Info, TimeSpan.FromSeconds(12));
                                return true;
                            });
                        }
                        catch (Exception error) { original.Retain(error); } // Existing toast tolerance, never a clean receipt.
                    }).GetTask());
                await original.AwaitAsync(actualDispatch);
            }
            catch (Exception error) { original.Retain(error); }
        });
    }

    private void AttachExceptionHooks()
    {
        if (_exceptionHooksAttached) return;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
        _exceptionHooksAttached = true;
    }

    private void DetachExceptionHooks()
    {
        if (!_exceptionHooksAttached) return;
        AppDomain.CurrentDomain.UnhandledException -= OnUnhandledException;
        TaskScheduler.UnobservedTaskException -= OnUnobservedTaskException;
        _exceptionHooksAttached = false;
    }

    private void OnUnhandledException(object sender, UnhandledExceptionEventArgs eventArgs)
    {
        var exception = eventArgs.ExceptionObject as Exception
                        ?? new InvalidOperationException("The runtime reported a non-Exception unhandled failure.");
        try { _ = LogExceptionAsync(eventArgs.IsTerminating ? "unhandled-terminating" : "unhandled", exception, Guid.NewGuid().ToString("N")); }
        catch { }
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs eventArgs)
    {
        try { _ = LogExceptionAsync("unobserved-task", eventArgs.Exception, Guid.NewGuid().ToString("N")); }
        catch { }
        finally { eventArgs.SetObserved(); }
    }

    private Task LogExceptionAsync(string eventName, Exception exception, string correlationId) =>
        _originalAppWork.RunAsync(original => LogOriginalExceptionAsync(original, eventName, exception, correlationId));

    private async Task LogOriginalExceptionAsync(DesktopOriginalWorkLifetime.Original original, string eventName, Exception exception, string correlationId)
    {
        if (_productionDiagnostics is null) return;
        await original.AwaitAsync(AcquireOriginalAppSynchronous(original, () => _productionDiagnostics.WriteAsync(
            ReliabilitySeverity.Critical,
            "desktop",
            eventName,
            exception.ToString(),
            new Dictionary<string, string>
            {
                ["exceptionType"] = exception.GetType().FullName ?? exception.GetType().Name,
                ["hResult"] = exception.HResult.ToString(System.Globalization.CultureInfo.InvariantCulture)
            },
            correlationId,
            CancellationToken.None).AsTask()));
    }
}
