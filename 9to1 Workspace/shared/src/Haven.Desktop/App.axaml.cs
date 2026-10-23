using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using CakeOS.Cui.Runtime;
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
        // CUI owns the primitive shell presentation; do not load donor Fluent theme
        // or application AXAML resources.
        CuiNativeHost.InitialisePrimitiveTheme(this, "Home");
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
        // The borrower exists before the native window opens; storage migration
        // runs in the owned startup original, so defer its first repository read.
        collection.AddSingleton(services => new NotificationService(
            services.GetRequiredService<HavenNotificationService>(), refreshOnConstruction: false));
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
        ConfigureOriginalOnboardingRegistrations(collection);
        ConfigureOriginalNativeDevelopmentRegistrations(collection);
        ConfigureOriginalWindowsDeveloperRegistrations(collection);
        ConfigureOriginalAssistantDevelopmentOwnerRegistrations(collection);
        ConfigureOriginalAssistantMemoryOwnerRegistrations(collection);
        ConfigureOriginalAssistantMiniComputerOwnerRegistrations(collection);
        ConfigureOriginalAssistantCapabilityOwnerRegistrations(collection);
        ConfigureOriginalAutomationLibraryReadRegistrations(collection);
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
        CaptureOriginalAssistantDevelopmentOwners(_services);
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
            if (preferences.RecoveryState != UserPreferenceRecoveryState.None)
            {
                var recoveryWindow = new Window
                {
                    Title = "9to1 Haven",
                    Width = 640,
                    Height = 320,
                    MinWidth = 480,
                    MinHeight = 240
                };
                UnifiedWorkspaceHost.ShowPreferenceRecovery(recoveryWindow, preferences.Diagnostic ??
                    "Preference recovery was required; a safe Professional fallback is in use.");
                recoveryWindow.Show();
            }
            preferences.ApplyAppearance(preferences.Appearance, save: false);
            // Linux uses the Browse-based unified host now that the Home composition runs.
            if (OriginalInitialProduct is null or "canvas")
            {
                var window = new MainWindow(preferences) { Title = "9to1", PreserveWorkspaceSessionOnClose = true };
                UnifiedWorkspaceHost? workspace = null;
                try
                {
                    workspace = CreateOriginalUnifiedWorkspace(window);
                }
                catch (Exception failure)
                {
                    // A failed unified startup must not inherit MainWindow's original
                    // window-work veto or retained close delivery. Use a plain host window
                    // whose canonical CUI failure content can close and drain normally.
                    var failureWindow = new Window
                    {
                        Title = "9to1 Haven",
                        Width = 900,
                        Height = 560,
                        MinWidth = 480,
                        MinHeight = 320
                    };
                    var correlationId = Guid.NewGuid().ToString("N");
                    UnifiedWorkspaceHost.ShowStartupFailure(failureWindow, correlationId, failure,
                        "Startup stopped before safe window shutdown was configured. This diagnostic window will remain open for recovery.");
                    // No full window/borrower ownership graph was configured. Keep the
                    // diagnostic visible; closing it cannot certify resource cleanup.
                    failureWindow.Closing += (_, args) => args.Cancel = true;
                    desktop.MainWindow = failureWindow;
                    base.OnFrameworkInitializationCompleted();
                    return;
                }
                window.DataContext = workspace;
                ConfigureOriginalUnifiedNativeRoutes(window, workspace);
                ConfigureOriginalDesktopShutdown(desktop, window, null);
                window.Opened += (_, _) => { _ = InitialiseUnifiedHavenSafely(); };
                desktop.MainWindow = window;
            }
            else
            {
                var mainView = _services.GetRequiredService<MainView>();
                mainView.ApplyEdition(HavenStartupExperiencePolicy.Edition);
                _services.GetRequiredService<WorkspaceSessionCoordinator>().Register(mainView, WorkspaceWindowKind.Main, queueSave: false);
                var window = new MainWindow(preferences) { DataContext = mainView, PreserveWorkspaceSessionOnClose = true };
                ConfigureOriginalWindowsNativeRoutes(window, mainView);
                ConfigureOriginalAutomationLibraryShell(window, mainView);
                ConfigureOriginalDesktopShutdown(desktop, window, mainView);
                window.Opened += (_, _) => { _ = InitialiseHaven(mainView); };
                desktop.MainWindow = window;
            }
        }
#endif

        TryRunDiagnosticSnapshot();
        base.OnFrameworkInitializationCompleted();
    }

    private Task InitialiseHaven(MainView shell) => _originalAppWork.RunAsync(
        original => InitialiseOriginalHavenAsync(original, shell), actual => _actualStartupOriginal = actual);

#if !ANDROID
    private Task InitialiseUnifiedHaven() => _originalAppWork.RunAsync(
        original => InitialiseOriginalHavenAsync(original, null), actual => _actualStartupOriginal = actual);

    private async Task InitialiseUnifiedHavenSafely()
    {
        try { await InitialiseUnifiedHaven(); }
        catch (Exception failure)
        {
            if (_actualUnifiedWorkspace is not null)
            {
                _actualUnifiedWorkspace.ShowStartupFailureForHost(Guid.NewGuid().ToString("N"), failure);
            }
        }
    }
#endif

    private async Task InitialiseOriginalHavenAsync(DesktopOriginalWorkLifetime.Original original, MainView? shell)
    {
        var correlationId = Guid.NewGuid().ToString("N");
        try
        {
            var services = _services ?? throw new InvalidOperationException("9to1 services have not been initialized.");
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
            var storageStartup = AcquireOriginalAppSynchronous(original, () => lifecycle.StartupAsync(CancellationToken.None));
            if (!await original.AwaitAsync(AcquireOriginalAppSynchronous(original,
                () => services.GetRequiredService<NotificationService>().RefreshAfterStorageStartupAsync(original, storageStartup)))) return;
#if !ANDROID
            await original.AwaitAsync(AcquireOriginalAppSynchronous(original, StartOriginalWindowsHomeAsync));
#endif
            await original.AwaitAsync(AcquireOriginalAppSynchronous(original, () => services.GetRequiredService<ModeSeedService>().SeedBuiltInModesAsync(CancellationToken.None)));
            var migration = await original.AwaitAsync(AcquireOriginalAppSynchronous(original,
                () => services.GetRequiredService<ILegacyStateMigrator>().MigrateIfNeededAsync(CancellationToken.None)));
            // The verified local preview child bootstrap/readiness runs under an OWNED TRACKED
            // background original (BeginPreviewModelBootstrapBackground), NOT on the App startup
            // critical path: normal Home/browser navigation and unrelated local app use are not
            // blocked waiting for model readiness. The background original joins actual children at
            // retirement and treats expected cooperative shutdown cancellation as benign; a genuine
            // startup/model fault is retained and surfaces via recovery/close while unrelated Home
            // stays usable. The routed provider still observes the genuine verified/configured
            // endpoint state when the verified provider becomes available later.
            BeginPreviewModelBootstrapBackground(services);
            // Cooperative close may cancel the App owner while startup is still progressing. Stop
            // sourcing further shell/host/UI work once the owner token is canceled or the App is
            // retiring, so the startup original joins cleanly instead of continuing past close.
            if (original.Token.IsCancellationRequested || _originalAppWork.IsRetiring) return;
            if (shell is not null)
            {
                await original.AwaitAsync(AcquireOriginalAppSynchronous(original, () => shell.InitializeAsync(migration, CancellationToken.None)));
                await original.AwaitAsync(AcquireOriginalAppSynchronous(original, () => shell.RestoreWorkspaceSessionAsync(CancellationToken.None)));
#if !ANDROID
                await original.AwaitAsync(AcquireOriginalAppSynchronous(original,
                    () => OpenOriginalInitialProductRouteAsync(shell, original.Token)));
#endif
            }
#if !ANDROID
            else
            {
                var workspace = _actualUnifiedWorkspace ?? throw new InvalidOperationException("The original unified host is unavailable.");
                // Gate late host/UI source admission on the Unified host's OWN admission/retirement state,
                // inside the SAME App source-acquisition physical guard. The host accepts a cooperative
                // startup retirement while the App owner is still open and its token is NOT yet canceled
                // (JoinOriginalAppProducers drains the host before the App lifetime). An expected accepted
                // retirement yields no task and no fault — the startup original joins cleanly. Genuine
                // faults inside real initialization are retained by the guard and propagate.
                var initialization = AcquireOriginalAppSynchronous(original, () => workspace.TryAcquireInitialization(original.Token));
                if (initialization is null) return;
                await original.AwaitAsync(AcquireOriginalAppSynchronous(original, () => initialization!));
                if (original.Token.IsCancellationRequested || _originalAppWork.IsRetiring) return;
                var initialRoute = AcquireOriginalAppSynchronous(original,
                    () => OpenOriginalInitialUnifiedProductRouteAsync(workspace, original.Token));
                if (initialRoute is null) return;
                await original.AwaitAsync(AcquireOriginalAppSynchronous(original, () => initialRoute));
            }
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
                        "9to1 recovery safe mode",
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
            // A cooperative early close cancels the owner token while startup is still progressing. That
            // is a benign stop: settle without treating the cancellation as a startup fault and without
            // presenting recovery UI. Genuine faults keep their exact identity and stay retained below.
            if (IsOwnerTokenCancellation(original.Token, ex)
                && (original.Token.IsCancellationRequested || _originalAppWork.IsRetiring))
            {
                // The owning AwaitAsync already captured this qualified owner-token cancellation as a
                // retained cause BEFORE this catch ran. Clear ONLY that pure cooperative cancellation so
                // the physical close does not fail on its own benign stop; every genuine/sibling fault in
                // the original stays retained and surfaces at close/recovery exactly as before.
                original.ClearCooperativeOwnerCancellation();
                return;
            }
            original.Retain(ex);
            // Do not spawn a late LogExceptionAsync diagnostic after the App original work has begun
            // retiring/disposing: RunAsync would throw ObjectDisposedException and only obscure the
            // real cause. Known cooperative shutdown/cancellation is benign and needs no diagnostic.
            if (!_originalAppWork.IsRetiring)
            {
                try
                {
                    await original.AwaitAsync(AcquireOriginalAppSynchronous(original, () => LogExceptionAsync("startup-failed", ex, correlationId)));
                }
                catch (Exception diagnosticFailure)
                {
                    original.Retain(diagnosticFailure); // Never hide the primary or actual sink failure.
                }
            }

            var userMessage = $"9to1 could not finish starting. Diagnostic reference: {correlationId}.";
            AcquireOriginalAppSynchronous(original, () => { shell?.SetStartupError(userMessage); return true; });
            if (shell is null && _actualUnifiedWorkspace is not null)
                _actualUnifiedWorkspace.ShowStartupFailureForHost(correlationId, ex);
            try
            {
                AcquireOriginalAppSynchronous(original, () =>
                {
                    _services?.GetService<NotificationService>()?.Show(
                        "9to1 startup problem", userMessage, ToastKind.Error, TimeSpan.FromSeconds(30));
                    return true;
                });
            }
            catch (Exception toastFailure)
            {
                original.Retain(toastFailure); // Persistent UI status still exists; failed original remains inspectable.
            }
        }
    }

    /// <summary>Runs the verified bundled-model bootstrap/readiness under an OWNED TRACKED background
    /// original so normal Home startup and unrelated local app use are not blocked on model readiness.
    /// Only a QUALIFIED same-owner cooperative close cancellation (owner lifetime token) is normalized to
    /// benign; every genuine cause (IOException, unrelated OCE, sibling/cleanup failure) stays retained
    /// and observable via close/recovery. No late LogExceptionAsync work after retirement.</summary>
    private void BeginPreviewModelBootstrapBackground(IServiceProvider services)
    {
        if (_originalAppWork.IsRetiring) return; // Retirement admits no new original; no late RunAsync.
        _originalAppWork.RunAsync(async background =>
        {
            try
            {
                await background.AwaitAsync(RunOwnedSourceAsync(background,
                    token => AcquireOriginalAppSynchronous(background,
                        () => services.GetRequiredService<LlamaCppPreviewLifecycle>().StartIfReadyAsync(token))));
            }
            catch (Exception failure)
            {
                background.Retain(failure); // Genuine startup/model fault stays retained/observable.
            }
        }, actual => _actualPreviewBootstrap = actual);
    }

    /// <summary>Genuinely awaits/joins the owned startup source (which settles its own children/drain on
    /// cancellation) and normalizes ONLY a qualified same-owner cooperative close cancellation. The owner
    /// lifetime token must be requested AND every surfaced cause must be a cancellation pinned to that exact
    /// token; otherwise the cause propagates so the awaiter retains the exact fault. This is the SAME
    /// source-backed helper used by production and by the focused lifecycle tests.</summary>
    internal static async Task RunOwnedSourceAsync(DesktopOriginalWorkLifetime.Original original, Func<CancellationToken, Task> start)
    {
        // Capture the SAME raw source Task OUTSIDE the await/try so its complete terminal state
        // (canceled vs faulted, and the ENTIRE fault sibling set) stays inspectable.
        var source = start(original.Token);

        try { await source; }
        catch (Exception failure)
        {
            // Normalize ONLY a genuinely CANCELED raw source with verified same-owner token provenance
            // and current retirement; a faulted source is never accepted as raw-canceled.
            if (source.IsCanceled)
            {
                if (failure is OperationCanceledException oce && IsSameOwnerCooperativeClose(original, oce))
                    return;
            }

            // Genuine terminal state: propagate the ENTIRE raw fault sibling set (e.g. OCE + IOException),
            // never only the exception that `await` surfaced first, so every original cause reference is kept.
            if (source.IsFaulted && source.Exception is { InnerExceptions.Count: > 1 } fullFault)
                throw fullFault;

            throw;
        }
    }

    private static bool IsSameOwnerCooperativeClose(DesktopOriginalWorkLifetime.Original original, Exception failure)
    {
        if (!original.Token.IsCancellationRequested) return false;
        return IsOwnerTokenCancellation(original.Token, failure);
    }

    private static bool IsOwnerTokenCancellation(CancellationToken ownerToken, Exception failure)
    {
        if (failure is OperationCanceledException oce && oce.CancellationToken.Equals(ownerToken)) return true;
        if (failure is AggregateException aggregate && aggregate.InnerExceptions.Count > 0)
        {
            foreach (var inner in aggregate.InnerExceptions)
                if (!IsOwnerTokenCancellation(ownerToken, inner)) return false;
            return true;
        }
        return false;
    }

    private void OnDesktopExit(object? sender, ControlledApplicationLifetimeExitEventArgs e)
    {
        // Exit cannot hold Avalonia's dispatcher alive. The actual pre-close owner
        // must already have joined originals/provider and issued its final writer.
        if (_actualShutdownSequence?.OriginalShutdown is not { IsCompletedSuccessfully: true } ||
            _actualWindowClosure is not { IsCompletedSuccessfully: true })
            ReportOriginalShutdownFailure(_actualShutdownFailure ??= new InvalidOperationException(
                "Desktop exited without the actual original pre-close drain; clean shutdown was not acknowledged."));
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
