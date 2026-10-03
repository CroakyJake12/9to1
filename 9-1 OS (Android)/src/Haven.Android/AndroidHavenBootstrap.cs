using Android.Content;
using Avalonia.Controls;
using Avalonia.Threading;
using Haven.Application;
using Haven.Browser;
using Haven.Core;
using Haven.Desktop;
using Haven.Desktop.Services;
using Haven.Desktop.Views.Shell;
using Haven.Infrastructure;
using HavenOS.Home.Core;
using Microsoft.Extensions.DependencyInjection;

namespace Haven.Android;

internal static class AndroidHavenBootstrap
{
    private static readonly SemaphoreSlim StartupGate = new(1, 1);
    private static bool _applicationStarted;
    private static IServiceProvider? _startedServices;
    private static WeakReference<MainActivity>? _activityOwner;
    private static long _mountGeneration;
    private static CancellationTokenSource? _mountLifetime;
    private static string? _pendingSurface;
    private static string? _pendingPrompt;
    private static string? _pendingHomeReview;
    private static string? HomeReviewRequest(Intent? intent)
    {
        var value = intent?.GetStringExtra("haven_home_review_request");
        return string.IsNullOrWhiteSpace(value) || value.Length > 256 || value.Any(char.IsControl) ? null : value;
    }
    private static WeakReference<MainView>? _activeMainView;
    private static bool _activeMainViewReady;
    private static Func<Task>? _activeBindingCheck;

    public static void SetLaunchRequest(MainActivity activity, Intent? intent)
    {
        InvalidateMount();
        _activityOwner = new(activity);
        _pendingSurface = intent?.GetStringExtra("haven_surface");
        _pendingPrompt = intent?.GetStringExtra("haven_prompt");
        _pendingHomeReview = HomeReviewRequest(intent);
    }

    public static void NotifyConfigurationChanged(MainActivity activity)
    {
        if (!IsActivityOwner(activity)) return;
        if (!_activeMainViewReady
            || _activeMainView is null
            || !_activeMainView.TryGetTarget(out var mainView))
            return;

        var generation = Interlocked.Read(ref _mountGeneration);
        Dispatcher.UIThread.Post(() => { if (IsActivityOwner(activity) && generation == Interlocked.Read(ref _mountGeneration)) mainView.RefreshMobileLayout(); });
    }

    public static void ApplyLaunchRequest(MainActivity activity, Intent? intent)
    {
        if (!IsActivityOwner(activity)) return;
        var surface = intent?.GetStringExtra("haven_surface");
        var prompt = intent?.GetStringExtra("haven_prompt");
        var review = HomeReviewRequest(intent);
        if (string.IsNullOrWhiteSpace(surface) && string.IsNullOrWhiteSpace(prompt) && review is null)
            return;

        if (_activeMainViewReady
            && _activeMainView is not null
            && _activeMainView.TryGetTarget(out var mainView)
            && _activeBindingCheck is { } bindingCheck)
        {
            Dispatcher.UIThread.Post(() => _ = ApplyLaunchRequestToMainViewAsync(mainView, surface, prompt, bindingCheck, review));
            return;
        }

        _pendingSurface = surface;
        _pendingPrompt = prompt;
        _pendingHomeReview = review;
    }

    private static async Task ApplyLaunchRequestToMainViewAsync(
        MainView mainView,
        string? surface,
        string? prompt,
        Func<Task> bindingCheck,
        string? review = null)
    {
        try
        {
            await bindingCheck();
            if (review is not null) await mainView.ReviewHomeRequestAsync(review);
            else await mainView.ApplyMobileLaunchRequestAsync(surface, prompt);
            await bindingCheck();
        }
        catch (OperationCanceledException) { }
        catch (Exception exception)
        {
            AndroidRuntimeDiagnostics.Record(
                exception,
                "Applying an Android launcher request to the active Haven surface",
                showDialog: false);
        }
    }

    private static (string? Surface, string? Prompt, string? HomeReview) TakeLaunchRequest()
    {
        var request = (_pendingSurface, _pendingPrompt, _pendingHomeReview);
        _pendingSurface = null;
        _pendingPrompt = null;
        _pendingHomeReview = null;
        return request;
    }

    private static bool IsActivityOwner(MainActivity activity) =>
        _activityOwner is not null && _activityOwner.TryGetTarget(out var current) && ReferenceEquals(current, activity);

    private static void InvalidateMount()
    {
        Interlocked.Increment(ref _mountGeneration);
        var previous = _mountLifetime; _mountLifetime = null;
        previous?.Cancel(); previous?.Dispose();
        _activeMainView = null; _activeMainViewReady = false; _activeBindingCheck = null;
    }
    public static void DetachActivity(MainActivity activity)
    {
        if (!IsActivityOwner(activity)) return;
        InvalidateMount(); _activityOwner = null;
    }
    public static Control CreateMainView()
    {
        var services = App.Services ?? throw new InvalidOperationException("Haven services were not created before Android requested its main view.");
        if (_activityOwner is null || !_activityOwner.TryGetTarget(out var activity))
            throw new InvalidOperationException("The Android main view has no owning activity.");
        InvalidateMount();
        var generation = Interlocked.Read(ref _mountGeneration);
        var lifetime = new CancellationTokenSource(); _mountLifetime = lifetime;
        var token = lifetime.Token;
        var placeholder = new ContentControl { Content = new TextBlock { Text = "Starting Haven…" } };
        bool Current() => generation == Interlocked.Read(ref _mountGeneration) && IsActivityOwner(activity) &&
            ReferenceEquals(App.Services, services) && !activity.IsFinishing && !activity.IsDestroyed && !token.IsCancellationRequested;
        placeholder.DetachedFromVisualTree += (_, _) => { if (Current()) InvalidateMount(); };
        Dispatcher.UIThread.Post(() => _ = InitializeMainViewAsync(placeholder, services, Current, token));
        return placeholder;
    }

    private static async Task InitializeMainViewAsync(
        ContentControl placeholder,
        IServiceProvider services,
        Func<bool> isCurrent,
        CancellationToken mountToken)
    {
        MainView? mainView = null;
        AuthenticatedResourceActor? originalActor = null;
        IAuthenticatedResourceActorSource? actors = null;
        async Task RequireOriginalAsync()
        {
            mountToken.ThrowIfCancellationRequested();
            if (!isCurrent() || originalActor is null || actors is null || await actors.GetCurrentAsync(mountToken) != originalActor || !isCurrent())
                throw new UnauthorizedAccessException("The original Android activity or Home session changed during startup.");
        }
        try
        {
            actors = services.GetRequiredService<IAuthenticatedResourceActorSource>();
            if (!isCurrent()) return;
            originalActor = await actors.GetCurrentAsync(mountToken);
            await RequireOriginalAsync();
            await StartupGate.WaitAsync(mountToken).ConfigureAwait(true);
            try
            {
                if (!isCurrent()) return;
                await RequireOriginalAsync();
                var home = await AndroidHomeServiceHost.EnsureAsync(installedApplications: false, cancellationToken: mountToken);
                await RequireOriginalAsync();
                if (home.State != HomeNativeHostState.Ready || !ReferenceEquals(home.Services, services))
                    throw new InvalidOperationException(home.Message);
                var recovery = services.GetRequiredService<IStartupRecoveryCoordinator>();
                StartupRecoveryState? recoveryState = string.IsNullOrEmpty(recovery.Current.RunId) ? null : recovery.Current;

                var lifecycle = services.GetRequiredService<IApplicationLifecycle>();
                if (!_applicationStarted || !ReferenceEquals(_startedServices, services))
                {
                    if (!lifecycle.IsStartupComplete)
                    {
                    if (recoveryState is not null)
                        throw new InvalidOperationException("Haven database startup did not finish in this process. Its preserved state requires recovery.");
                    recoveryState = await recovery.BeginStartupAsync(CancellationToken.None);

                    await RequireOriginalAsync();
                    await lifecycle.CrashRecoveryAsync(CancellationToken.None);
                    await RequireOriginalAsync();
                    await lifecycle.StartupAsync(CancellationToken.None);
                    await RequireOriginalAsync();
                    if (!lifecycle.IsStartupComplete)
                        throw new InvalidOperationException("Haven database startup did not complete. Its preserved state requires recovery.");
                    }
                    await services.GetRequiredService<ModeSeedService>()
                        .SeedBuiltInModesAsync(CancellationToken.None);

                    await RequireOriginalAsync();
                    _applicationStarted = true; _startedServices = services;
                }
                if (!lifecycle.IsStartupComplete)
                    throw new InvalidOperationException("Haven database startup is not ready.");
                await RequireOriginalAsync();
                BrowserAutomationRegistry.Register(services.GetRequiredService<BrowserSessionService>(), services.GetRequiredService<IBrowserAutomationService>());
                var preferences = services.GetRequiredService<UserPreferencesService>();
                preferences.ApplyAppearance(preferences.Appearance, save: false);
                // Keyboard AI uses normal Haven model routing and stays off unless the
                // user enables it; secure fields never reach the executor regardless.
                var keyboardSettings = new HavenKeyboardSettings(global::Android.App.Application.Context);
                HavenKeyboardAiController.Configure(new RoutedKeyboardAiExecutor(
                    services.GetRequiredService<IModelProviderRegistry>(),
                    services.GetRequiredService<HomePersonalModelRoutes>(),
                    services.GetRequiredService<IProviderConfigurationStore>(),
                    services.GetRequiredService<IPrivacyPreferenceStore>(),
                    () => keyboardSettings.CloudAiAllowed));
                _ = services.GetRequiredService<AndroidNotificationBridge>();
                _ = services.GetRequiredService<AndroidProjectorDisplayService>();

                await RequireOriginalAsync();
                // MainView constructors start repository-backed sidebar work; schema must already be ready.
                mainView = ActivatorUtilities.CreateInstance<MainView>(services);
                mainView.IsVisible = false;
                _activeMainView = new WeakReference<MainView>(mainView);
                // Apply the Android shell only after StartupAsync has created and migrated the
                // SQLite schema. Activity recreation still receives a fresh MainView instance.
                mainView.ApplyEdition(HavenShellEdition.New);
                mainView.ApplyMobileLayout();
                mainView.AttachProjectorControllerSession(
                    services.GetRequiredService<IProjectorSessionCoordinator>(),
                    services.GetRequiredService<AndroidProjectorControllerActionDispatcher>(),
                    services.GetRequiredService<IProjectorDisplayRegistry>());

                var migration = await services.GetRequiredService<ILegacyStateMigrator>()
                    .MigrateIfNeededAsync(CancellationToken.None);

                await RequireOriginalAsync();
                await mainView.InitializeAsync(migration, mountToken);
                await RequireOriginalAsync();
                var launchRequest = TakeLaunchRequest();
                await mainView.ApplyMobileLaunchRequestAsync(
                    launchRequest.Surface,
                    launchRequest.Prompt);
                await RequireOriginalAsync();
                placeholder.Content = mainView;
                mainView.IsVisible = true;
                _activeBindingCheck = RequireOriginalAsync;
                _activeMainViewReady = true;
                // Approval UI can remain open. Do not hold the startup gate while awaiting its closure.
                if (launchRequest.HomeReview is { } review)
                    Dispatcher.UIThread.Post(() => _ = ApplyLaunchRequestToMainViewAsync(mainView, null, null, RequireOriginalAsync, review));

                await RequireOriginalAsync();
                var deferredLaunchRequest = TakeLaunchRequest();
                if (deferredLaunchRequest.HomeReview is { } deferredReview)
                    Dispatcher.UIThread.Post(() => _ = ApplyLaunchRequestToMainViewAsync(mainView, null, null, RequireOriginalAsync, deferredReview));
                else if (!string.IsNullOrWhiteSpace(deferredLaunchRequest.Surface)
                    || !string.IsNullOrWhiteSpace(deferredLaunchRequest.Prompt))
                {
                    await mainView.ApplyMobileLaunchRequestAsync(
                        deferredLaunchRequest.Surface,
                        deferredLaunchRequest.Prompt);
                    await RequireOriginalAsync();
                }

                if (recoveryState?.IsSafeMode == true)
                {
                    services.GetRequiredService<NotificationService>().Show(
                        "Haven recovery safe mode",
                        recoveryState.Reason
                            + " Local chat and read-only workspace inspection remain available.",
                        ToastKind.Warning,
                        TimeSpan.FromSeconds(30));
                }

                await RequireOriginalAsync();
                await recovery.MarkStartupCompletedAsync(CancellationToken.None);
                await RequireOriginalAsync();
            }
            finally
            {
                StartupGate.Release();
            }
        }
        catch (OperationCanceledException) when (mountToken.IsCancellationRequested || !isCurrent()) { }
        catch (Exception exception)
        {
            if (!isCurrent()) return;
            _activeMainViewReady = false;
            if (mainView is not null) { mainView.IsVisible = true; mainView.SetStartupError(exception.Message); placeholder.Content = mainView; }
            else placeholder.Content = new TextBlock { Text = "Haven could not finish starting. Open recovery to repair its preserved state." };
            AndroidRuntimeDiagnostics.Record(
                exception,
                "Haven service and main-view startup",
                showDialog: true);
            System.Diagnostics.Debug.WriteLine("[Haven Android startup] " + exception);

            services.GetService<NotificationService>()?.Show(
                "Haven could not finish starting",
                exception.Message,
                ToastKind.Error,
                TimeSpan.FromSeconds(30));
        }
    }
}
