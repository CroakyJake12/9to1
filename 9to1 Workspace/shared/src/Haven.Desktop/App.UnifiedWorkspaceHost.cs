#if !ANDROID
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using CakeOS.Cui.Language;
using CakeOS.Cui.Runtime;
using CakeOS.Cui.Themes;
using Haven.Application;
using Haven.Application.Compatibility;
using Haven.Browser;
using Haven.Core;
using Haven.Desktop.Events;
using Haven.Desktop.Dashboard;
using Haven.Desktop.Services;
using Haven.Desktop.HavenUI.Backend;
using Haven.Desktop.Views.Pages.Home;
using Haven.Desktop.Views.Shell;
using HavenOS.Apps.Browse;
using HavenOS.Home;
using HavenOS.Home.Core;
using HavenOS.Home.NativeUI;
using HavenOS.Home.PermissionsTrustNotifications;
using HavenOS.Home.Discover;
using Microsoft.Extensions.DependencyInjection;

namespace Haven.Desktop;

public sealed partial class App
{
    private UnifiedWorkspaceHost? _actualUnifiedWorkspace;

    /// <summary>The normal Windows product borrows the existing App/Home graph.
    /// Selected standalone products retain their own existing entry admission.</summary>
    private UnifiedWorkspaceHost CreateOriginalUnifiedWorkspace(MainWindow window)
    {
        UnifiedWorkspaceHost? host = null;
        _originalAppWork.RunSynchronous(original => AcquireOriginalAppSynchronous(original, () =>
        {
            original.DemandPublication();
            var services = _services ?? throw new InvalidOperationException("The original provider is unavailable.");
            var home = _actualWindowsHome ?? throw new InvalidOperationException("The original Home owner is unavailable.");
            WindowsHomeSameProcessRuntimeObservation.DemandOriginalBinding(services, home);
            if (_actualUnifiedWorkspace is not null) throw new InvalidOperationException("The original unified host already exists.");
            host = _actualUnifiedWorkspace = new(this, services, home, original.Token, window.AcquireOriginalWindowLifetime());
            return true;
        }));
        return host ?? throw new InvalidOperationException("No original unified host was retained.");
    }

    /// <summary>Source-owned device appearance observation over the SAME actual Home graph.
    /// No Chrome/tab state, cache, readiness flag or caller-supplied grant is consulted.</summary>
    internal static Task<bool> ObserveOriginalHomeAppearanceAuthorityAsync(IServiceProvider services,
        HomeNativeWindowsComposition home, AuthenticatedResourceActor expectedActor,
        CancellationToken appLifetime, CancellationToken windowLifetime,
        Action<Action> runWithinOriginalSource, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(expectedActor); ArgumentNullException.ThrowIfNull(runWithinOriginalSource);
        if (!appLifetime.CanBeCanceled || !windowLifetime.CanBeCanceled)
            throw new ArgumentException("Borrow the actual App and window work lifetimes.");
        var originals = new CloudflareOriginalTaskLedger();
        originals.BindOriginalOwner(home); originals.BindOriginalCallerCallback(runWithinOriginalSource);
        return originals.RunToOriginalSettlementAsync(async () =>
        {
            using var phase = CloudflareOriginalExecutionGuard.EnterOriginal(home);
            // Source cancellation belongs to the async source body and actual principal tasks.
            // Do not manufacture a linked token or turn private cancellation into a finite
            // synchronous caller-scope fault. App/window/Home remain the original owners.
            void DemandOwner()
            {
                appLifetime.ThrowIfCancellationRequested(); windowLifetime.ThrowIfCancellationRequested();
                runWithinOriginalSource(() => WindowsHomeSameProcessRuntimeObservation.DemandOriginalBinding(services, home));
                if (home.OriginalCloseTask is not null || home.OriginalProcessRetirementRequestTask is not null)
                    throw new ObjectDisposedException("The original Home appearance owner is retiring.");
            }
            void Demand() { token.ThrowIfCancellationRequested(); DemandOwner(); }
            void Own(Action body) => originals.Invoke(() => { DemandOwner(); body(); return true; });
            void Retain(Task actual) { _ = originals.Track(actual); }
            bool ServicesCurrent(HomeCoreStateSnapshot snapshot)
            {
                // Same actual three required Home services as standalone Appearance; no Ready
                // flag, peer grant, cached service result or additional settings provider.
                if (snapshot.Revision < 0 || snapshot.Services is null || snapshot.Services.Count > 128 ||
                    snapshot.Services.Any(service => service is null || service.Revision < 0 ||
                        string.IsNullOrWhiteSpace(service.ServiceId) || !Enum.IsDefined(service.State)) ||
                    snapshot.Services.Select(service => service.ServiceId).Distinct(StringComparer.Ordinal).Count() != snapshot.Services.Count)
                    throw new InvalidDataException("The original Home appearance services supplied invalid observations.");
                return new[] { "home.core", "home.state", "permissions.trust" }.All(id =>
                    snapshot.Services.Any(service => service.ServiceId == id &&
                        new HomeServiceRequirement(id, 1).Accepts(service.ContractVersion) && service.IsAvailable &&
                        service.State == HomeServiceLifecycleState.Ready));
            }
            async Task<AuthenticatedResourceActor?> ReadActorAsync() =>
                await originals.AwaitAsync(originals.Invoke(() =>
                    home.Profiles.GetCurrentAsync(Own, Retain, token).AsTask())).ConfigureAwait(false);
            Demand();
            var start = home.OriginalStartTask;
            if (start is not { IsCompletedSuccessfully: true }) return false;
            var before = originals.Invoke(() => { DemandOwner(); return home.Runtime.Current; });
            if (!ServicesCurrent(before)) return false;
            var actor = await ReadActorAsync().ConfigureAwait(false);
            Demand();
            if (actor != expectedActor) return false;
            var after = originals.Invoke(() => { DemandOwner(); return home.Runtime.Current; });
            if (after.Revision != before.Revision || !ServicesCurrent(after)) return false;
            var finalActor = await ReadActorAsync().ConfigureAwait(false);
            Demand();
            var final = originals.Invoke(() => { DemandOwner(); return home.Runtime.Current; });
            return ReferenceEquals(start, home.OriginalStartTask) && start.IsCompletedSuccessfully &&
                finalActor == expectedActor && final.Revision == after.Revision && ServicesCurrent(final);
        });
    }

    /// <summary>One real Browse content host; no hidden MainView, second provider,
    /// app catalogue, or workspace repository. The App retains this owner before startup.</summary>
    internal sealed partial class UnifiedWorkspaceHost : ContentControl, IDesktopOriginalRetirementParticipant,
        IDesktopOriginalRetirementJoinGuard, ICuiSceneReadiness
    {
        private readonly App _app;
        private readonly IServiceProvider _services;
        private readonly HomeNativeWindowsComposition _home;
        private readonly CancellationToken _appLifetime, _windowLifetime;
        private readonly CancellationTokenSource _startupCancellation = new();
        private readonly object _gate = new();
        private readonly HashSet<Task> _sources = new(ReferenceEqualityComparer.Instance);
        private CuiSceneHost? _scene;
        private NativeHomeThemePreferences? _homeTheme;
        private CuiControlLoader? _startupLoadingLoader;
        private Task? _initialization, _close, _pendingClose, _workspaceClose, _sceneClose, _sessionClose;
        private readonly List<Task> _closeAttempts = [];
        private sealed record PreparedBoardRead(AppContentAddress Address, Guid Token, bool Writable);
        private Task<CuiSceneAvailability>? _sceneInitialization;
        private Task<BrowseChrome>? _chromeCreation;
        private BrowseChrome? _chrome;
        private BrowseNativeWorkspace? _workspace;
        private UnifiedWorkspaceSession? _session;
        private AuthenticatedResourceActor? _actor;
        private NativeHomeFilesBoardReadSource? _boardReadSource;
        private NativeHomeFilesBoardWriteSource? _boardWriteSource;
        private HavenOS.Files.NativeHost.FilesNativeBrowserService.BoardDocumentWriteOwner? _boardDocumentWriteOwner;
        private volatile bool _closed;
        private volatile bool _startupRetirement;

        internal UnifiedWorkspaceHost(App app, IServiceProvider services, HomeNativeWindowsComposition home,
            CancellationToken appLifetime, CancellationToken windowLifetime)
        {
            Dispatcher.UIThread.VerifyAccess();
            _app = app; _services = services; _home = home;
            _appLifetime = appLifetime; _windowLifetime = windowLifetime;
            KeyDown += OnWorkspaceKeyDown;
            ShowStartupLoading();
        }

        internal Task? OriginalInitialization => _initialization;
        internal Task? OriginalClose => _close ?? (_chrome?.OriginalClose is not null ? _pendingClose : null);
        internal IReadOnlyList<Task> OriginalCloseAttempts => _closeAttempts.ToArray();
        // Close preparation must still see the real current owners. Publication ends
        // when Browse accepts retirement, not when its outer close request is allocated.
        private bool IsCurrent => !_closed && !_startupRetirement && !_appLifetime.IsCancellationRequested &&
            !_windowLifetime.IsCancellationRequested && _chrome?.OriginalClose is null;


        internal static void ShowStartupFailure(Window window, string correlationId, Exception exception,
            string? recoveryGuidance = null)
        {
            ArgumentNullException.ThrowIfNull(window);
            ArgumentNullException.ThrowIfNull(exception);
            if (!Dispatcher.UIThread.CheckAccess()) throw new InvalidOperationException("Startup failure presentation requires the UI thread.");
            var application = Avalonia.Application.Current ?? throw new InvalidOperationException("Avalonia application is unavailable.");
            if (!application.Resources.TryGetResource("CuiBackgroundBrush", application.ActualThemeVariant, out _))
                CuiNativeHost.InitialisePrimitiveTheme(application, "Home", CakeOS.Cui.Themes.CuiAppearance.Dark);
            var model = new CuiViewModel();
            model.Set("Message", exception.Message);
            model.Set("CorrelationId", correlationId);
            model.Set("DiagnosticReference", "Diagnostic reference: " + correlationId);
            model.Set("Guidance", recoveryGuidance ?? "Close this window, resolve the reported issue, and restart 9to1 Haven.");
            var handlerAlreadyAttached = window.Tag is CuiControlLoader;
            if (window.Tag is CuiControlLoader existingLoader) existingLoader.Dispose();
            window.Tag = null;
            var loader = new CuiControlLoader();
            const string markup = "<Cui><Theme Name=\"user_preference\"><Page AccessibleName=\"9to1 Haven startup failure\"><Container Type=\"Vertical Stack\" CellGap=\"12\" Padding=\"24\"><Text Text=\"9to1 Haven could not start\" FontSize=\"24\" FontWeight=\"Bold\" Foreground=\"{Resource CuiTextBrush}\" /><Text Text=\"{Binding Message}\" TextWrapping=\"Wrap\" Foreground=\"{Resource CuiTextBrush}\" /><Text Text=\"{Binding DiagnosticReference}\" Foreground=\"{Resource CuiTextSoftBrush}\" /><Text Text=\"{Binding Guidance}\" Foreground=\"{Resource CuiTextSoftBrush}\" /></Container></Page></Theme></Cui>";
            loader.SetBindingContext(model);
            loader.SetActionDispatcher(model);
            var (root, diagnostics) = loader.LoadMarkup(markup, "StartupFailure.cui");
            if (root is null || diagnostics.Any(diagnostic => diagnostic.Severity == CuiDiagnosticSeverity.Error))
            {
                loader.Dispose();
                throw new InvalidDataException("The canonical startup-failure CUI did not produce a usable control: " +
                    string.Join("; ", diagnostics));
            }
            loader.WireBindings(root);
            window.Tag = loader;
            window.Content = root;
            if (application.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
                desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            if (!handlerAlreadyAttached)
                window.Closed += (_, _) =>
                {
                    if (window.Tag is CuiControlLoader ownedLoader)
                    {
                        ownedLoader.Dispose();
                        window.Tag = null;
                    }
                };
        }

        internal static void ShowPreferenceRecovery(Window window, string diagnostic)
        {
            ArgumentNullException.ThrowIfNull(window);
            if (!Dispatcher.UIThread.CheckAccess()) throw new InvalidOperationException("Preference recovery presentation requires the UI thread.");
            var application = Avalonia.Application.Current ?? throw new InvalidOperationException("Avalonia application is unavailable.");
            if (!application.Resources.TryGetResource("CuiBackgroundBrush", application.ActualThemeVariant, out _))
                CuiNativeHost.InitialisePrimitiveTheme(application, "Home", CakeOS.Cui.Themes.CuiAppearance.Dark);
            var model = new CuiViewModel();
            model.Set("Message", diagnostic);
            model.Set("Guidance", "The original preferences file was preserved. 9to1 is using a safe Professional fallback; unrelated workflows remain available.");
            var handlerAlreadyAttached = window.Tag is CuiControlLoader;
            if (window.Tag is CuiControlLoader existingLoader) existingLoader.Dispose();
            window.Tag = null;
            using var stream = typeof(UnifiedWorkspaceHost).Assembly.GetManifestResourceStream("Haven.Desktop.PreferenceRecovery.cui")
                ?? throw new InvalidDataException("The preference recovery CUI document is missing.");
            using var reader = new StreamReader(stream);
            var parser = new CuiRichParser();
            var document = parser.Parse(reader.ReadToEnd(), "PreferenceRecovery.cui");
            if (parser.Diagnostics.Diagnostics.Any(item => item.Severity == CuiDiagnosticSeverity.Error))
            {
                throw new InvalidDataException("The preference recovery CUI document is invalid: " +
                    string.Join("; ", parser.Diagnostics.Diagnostics));
            }
            var loader = new CuiControlLoader();
            loader.SetBindingContext(model);
            loader.SetActionDispatcher(model);
            var loaded = loader.TryLoad(document);
            if (loaded.Root is null || loaded.Diagnostics.Any(item => item.Severity == CuiDiagnosticSeverity.Error))
            {
                loader.Dispose();
                throw new InvalidDataException("The canonical preference-recovery CUI did not produce a usable control: " +
                    string.Join("; ", loaded.Diagnostics));
            }
            loader.WireBindings(loaded.Root);
            window.Tag = loader;
            window.Content = loaded.Root;
            if (!handlerAlreadyAttached)
                window.Closed += (_, _) =>
                {
                    if (window.Tag is CuiControlLoader ownedLoader)
                    {
                        ownedLoader.Dispose();
                        window.Tag = null;
                    }
                };
        }

        internal void ShowStartupFailureForHost(string correlationId, Exception exception)
        {
            Dispatcher.UIThread.VerifyAccess();
            if (_closed) return;
            _startupLoadingLoader?.Dispose(); _startupLoadingLoader = null;
            var window = TopLevel.GetTopLevel(this) as Window;
            if (window is not null)
                ShowStartupFailure(window, correlationId, exception);
            // Presentation does not retire App or bypass the configured native
            // Closing/preflight/shutdown owner. That graph owns all borrowers.
        }

        /// <summary>Compatibility admission wrapper preserving the prior genuine-invalid-admission
        /// semantics (throws <see cref="ObjectDisposedException"/> when the host is no longer admitting).
        /// The App startup path uses <see cref="TryAcquireInitialization"/> instead so an EXPECTED accepted
        /// retirement can be distinguished from a genuine unexpected fault; this wrapper keeps the prior
        /// contract available for any existing caller.</summary>
        internal Task InitializeAsync(CancellationToken token)
        {
            var initialization = TryAcquireInitialization(token);
            if (initialization is null) throw new ObjectDisposedException(nameof(UnifiedWorkspaceHost));
            return initialization;
        }

        /// <summary>Gates late startup source admission on this host's OWN admission/retirement state,
        /// not just the App parent token. The host can accept a cooperative startup retirement (with
        /// <c>_initialization</c> still null) while the App owner remains open and its token is NOT yet
        /// canceled — <c>JoinOriginalAppProducers</c> drains the host before the App lifetime. In that
        /// window a late initialization must not fault into the disposed host. This returns null for
        /// exactly that expected accepted retirement (benign, no task issued, no fabricated completion
        /// witness); genuine unexpected faults inside real initialization still propagate through the
        /// returned task.</summary>
        internal Task? TryAcquireInitialization(CancellationToken token)
        {
            Dispatcher.UIThread.VerifyAccess();
            lock (_gate)
            {
                if (_initialization is not null) return _initialization;
                // Expected accepted retirement (startup retirement accepted, close pending, or closed)
                // → no source is issued and no completion is fabricated.
                if (OriginalClose is not null || _pendingClose is not null || _startupRetirement || _closed)
                    return null;
                ShowStartupLoading();
                var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                _initialization = InitializeCoreAsync(start.Task, token);
                start.SetResult(); return _initialization;
            }
        }

        private void ShowStartupLoading()
        {
            Dispatcher.UIThread.VerifyAccess();
            if (Content is not null) return;
            var application = Avalonia.Application.Current;
            if (application is not null && !application.Resources.TryGetResource("CuiBackgroundBrush", application.ActualThemeVariant, out _))
                CuiNativeHost.InitialisePrimitiveTheme(application, "Home", CakeOS.Cui.Themes.CuiAppearance.Dark);
            var model = new CuiViewModel();
            model.Set("Status", "Loading 9to1 Haven…");
            var loader = new CuiControlLoader();
            const string markup = "<Cui><Theme Name=\"user_preference\"><Page AccessibleName=\"9to1 Haven loading\"><Container Type=\"Vertical Stack\" CellGap=\"12\" Padding=\"24\"><Text Text=\"{Binding Status}\" FontSize=\"24\" FontWeight=\"Bold\" Foreground=\"{Resource CuiTextBrush}\" /><Text Text=\"Checking required Home services and authentication…\" Foreground=\"{Resource CuiTextSoftBrush}\" /></Container></Page></Theme></Cui>";
            loader.SetBindingContext(model);
            loader.SetActionDispatcher(model);
            var (root, diagnostics) = loader.LoadMarkup(markup, "StartupLoading.cui");
            if (root is null || diagnostics.Any(diagnostic => diagnostic.Severity == CuiDiagnosticSeverity.Error))
            {
                loader.Dispose();
                throw new InvalidDataException("The canonical startup-loading CUI did not produce a usable control: " +
                    string.Join("; ", diagnostics));
            }
            loader.WireBindings(root);
            _startupLoadingLoader = loader;
            Content = root;
        }

        private async Task InitializeCoreAsync(Task start, CancellationToken token)
        {
            await start;
            // The cooperative retirement control flow lives INSIDE the body checkpoints (raw task status
            // + accepted-retirement qualification). There is intentionally NO outer catch-all waiver here:
            // a genuine FAULTED source carrying a same-token OCE, a mixed/foreign fault, or an independent
            // disposal/drain failure must propagate and stay retained, never erased by token/type alone.
            await InitializeCoreBodyAsync(token);
        }

        private async Task InitializeCoreBodyAsync(CancellationToken token)
        {
            using var source = CloudflareOriginalExecutionGuard.EnterOriginal(this);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, _appLifetime, _windowLifetime, _startupCancellation.Token);
            var currentToken = linked.Token;
            _actor = await ObserveActorAsync(currentToken);
            _boardReadSource = new NativeHomeFilesBoardReadSource(
                _services.GetRequiredService<HavenOS.Files.NativeHost.FilesNativeBrowserService>(), _actor);
            var filesBrowser = _services.GetRequiredService<HavenOS.Files.NativeHost.FilesNativeBrowserService>();
            if (OperatingSystem.IsLinux() && filesBrowser.HasNativeMutationOwner &&
                _services.GetService<IFilesOriginalBoardDocumentPhysicalOwner>() is { } boardPhysicalOwner)
            {
                _boardDocumentWriteOwner = filesBrowser.CreateOriginalBoardDocumentWriteOwner(boardPhysicalOwner);
                _boardWriteSource = new NativeHomeFilesBoardWriteSource(filesBrowser, _boardDocumentWriteOwner, _actor);
            }
            var availability = await CheckAsync(currentToken);
            if (availability.State != CuiSceneAvailabilityState.Ready) throw new InvalidOperationException(availability.Message);
            DemandCurrent();
            _homeTheme = new NativeHomeThemePreferences(_services.GetRequiredService<UserPreferencesService>().HomeThemePreferencePath,
                new HomeThemeScope(HomeDataScope.DeviceLocal, _actor.ProfileId), UserPreferencesService.ProjectLegacyHomeTheme,
                lifetime => new ValueTask<bool>(ObserveAppearanceAuthorityAsync(lifetime)),
                callback => Dispatcher.UIThread.Post(callback));
            await Track(_homeTheme.RefreshAsync(currentToken));
            if (IsAcceptedStartupRetirement(currentToken)) return;
            currentToken.ThrowIfCancellationRequested(); DemandCurrent();
            // First-run gate: a fresh profile must complete (or recover) onboarding before normal
            // Home/Browse publication. Returning completed profiles bypass directly.
            await RunFirstRunOnboardingGateAsync(_actor.ProfileId, currentToken);
            if (IsAcceptedStartupRetirement(currentToken)) return;
            currentToken.ThrowIfCancellationRequested(); DemandCurrent();
            var coordinator = _services.GetRequiredService<WorkspaceSessionCoordinator>();
            var restored = await Track(coordinator.LoadAsync(currentToken));
            if (IsAcceptedStartupRetirement(currentToken)) return;
            currentToken.ThrowIfCancellationRequested(); DemandCurrent();
            _session = new UnifiedWorkspaceSession(coordinator, restored);
            var persistence = _session.OpenWindow(_session.MainWindowId, _actor.ProfileId);
            var files = FilesBrowseAppContentFactory.BindOriginalSameProcess(_services, _home, _appLifetime, _windowLifetime);
            var spaces = SpacesBrowseAppContentFactory.BindOriginalSameProcess(_services, _home, _appLifetime, _windowLifetime,
                (intent, settlement) => OriginalClose is null && _workspace is { } workspace && workspace.QueueNavigation(intent, settlement));
            var build = BuildBrowseAppContentFactory.BindOriginalSameProcess(_services, _home, _appLifetime, _windowLifetime,
                (intent, settlement) => OriginalClose is null && _workspace is { } workspace && workspace.QueueNavigation(intent, settlement));
            var actualTheme = _homeTheme
                ?? throw new InvalidOperationException("The original Home theme owner is unavailable.");
            var boards = BoardsBrowseAppContentFactory.BindOriginalSameProcess(_services, _home, _appLifetime, _windowLifetime,
                () => actualTheme.Current.Accessibility.ReduceMotion, actualTheme.Context,
                (token, store, folder, file, revision) => (_boardReadSource ?? throw new UnauthorizedAccessException(
                    "The original Home Files board reader is unavailable."))
                    .TakePrepared(token, store, folder, file, revision),
                (token, store, folder, file, revision) => (_boardWriteSource ?? throw new UnauthorizedAccessException(
                    "The original Home Files board writer is unavailable."))
                    .TakePrepared(token, store, folder, file, revision));
            var canvas = CanvasBrowseAppContentFactory.BindOriginalSameProcess(_services, _home, _appLifetime, _windowLifetime, actualTheme.Context);
            var present = PresentBrowseAppContentFactory.BindOriginalSameProcess(_services, _home, _appLifetime, _windowLifetime);
            var modes = _services.GetRequiredService<IModeRegistry>();
            // The SAME production registration helper builds the genuine native-app set (files, spaces,
            // dev, boards, canvas, present); legacy callers retain their existing registration set.
            // Chat is canonicalized to the Spaces Chat resource by HomeLaunchRouteResolver before admission.
            var apps = new NativeAppContentRegistry(modes,
                NativeAppContentRegistry.NativeAppContentRegistrations.Build(files, spaces, build, boards, canvas, present), () => IsCurrent);
            var catalogue = _app.RetainOriginalModelCatalogueRegistry(_services.GetRequiredService<IServiceProvider>());
            var privacy = _services.GetRequiredService<IPrivacyPreferenceStore>();
            var home = new UnifiedHomeContentFactory(modes, CreateDashboard,
                () => new Haven.Desktop.Views.Pages.Home.HomeDiscoverPage(new HomeDiscoverCatalog(catalogue.Providers.Select(provider =>
                    new HomeRegisteredModelCatalogueSource(catalogue, provider.Id, () => !privacy.Current.LocalOnlyMode)))),
                (intent, settlement) => OriginalClose is null && _workspace is { } workspace && workspace.QueueNavigation(intent, settlement),
                // Spaces owns the canonical draft. Create it through the shared conversation
                // repository, then open the registered Spaces destination in the same tab.
                async (text, token) =>
                {
                    var conversations = _services.GetRequiredService<IConversationRepository>();
                    var now = DateTimeOffset.UtcNow;
                    var conversation = new Conversation(
                        Guid.NewGuid(), HavenMode.Chat, ConversationKind.Chat, text, null, null,
                        false, true, now, now, SpaceId: SpaceRegistry.ChatSpaceId);
                    await conversations.UpsertConversationAsync(conversation, token).ConfigureAwait(false);
                    return AppContentAddress.Parse("9to1://spaces");
                },
                AdmitHomeAsync, () => IsCurrent, _windowLifetime, aiAvailable: false,
                events: request =>
                {
                    DemandCurrent();
                    var actor = _actor ?? throw new UnauthorizedAccessException("The original Home profile is unavailable.");
                    var activity = new HavenOS.Home.PermissionsTrustNotifications.HomePermissionActivitySource(
                        _home.Permissions, _home.Profiles, actor);
                    return new HomeEventsDestinationPage(new HomeEventsService([activity], activity),
                        token => AdmitHomeAsync(request, token));
                },
                settings: request => new HomeSettingsDestinationPage(actualTheme,
                    token => AdmitHomeSettingsAsync(request, token)),
                library: request => CreateHomeLibraryPage(request, actualTheme.Context));
            IBrowseEngineHostFactory? engines = null;
            if (!OperatingSystem.IsWindows())
                engines = new ChromiumEngineFactory(
                    _services.GetRequiredService<BrowserDownloadTransport>(),
                    _services.GetRequiredService<IBrowserNativeDownloadService>());
#if HAVEN_WINDOWS_DESKTOP
            engines = new WindowsChromiumEngineFactory((tab, engine, origin, kind) =>
                (_chrome ?? throw new InvalidOperationException("The original Browse owner is not published."))
                    .ObserveOriginPermission(tab, engine, origin, kind),
                _services.GetRequiredService<BrowserDownloadTransport>(), _services.GetRequiredService<IBrowserNativeDownloadService>());
#endif
            _chromeCreation = Track(BrowseChrome.CreateAsync(_services.GetRequiredService<IAppPaths>(), engines,
                _services.GetRequiredService<IBrowserAutomationService>(), currentToken, apps, home, _actor.ProfileId, persistence));
            _chrome = await _chromeCreation;
            DemandCurrent();
            _sharedGateway = new HavenGateway(this);
            _workspace = new BrowseNativeWorkspace(_chrome, _ => IsCurrent,
                CaptureOriginalDownloadFilesService(), sharedContext: new SharedHostContext(this));
            var theme = _homeTheme?.Context
                ?? throw new InvalidOperationException("The original Home theme owner is unavailable.");
            var scene = BrowseNativeSurface.CreateScene(_workspace, this, appearance: null, theme);
            var controlRegistry = scene.ControlRegistry
                ?? throw new InvalidOperationException("The original Browse CUI control registry is unavailable.");
            controlRegistry.RegisterImageSourceResolver(HavenPackagedSvgResolver.Resolve);
            _startupLoadingLoader?.Dispose(); _startupLoadingLoader = null;
            _scene = new CuiSceneHost(controlRegistry);
            ComposeSharedFrame();
            _sceneInitialization = Track(_scene.ShowAsync(scene, currentToken));
            var ready = await _sceneInitialization;
            _workspace.ObserveReadiness(ready);
            if (ready.State != CuiSceneAvailabilityState.Ready) throw new InvalidOperationException(ready.Message);
        }

        /// <summary>
        /// First-run gate. A fresh profile (or one with structured damaged/unavailable completion)
        /// must complete or recover the authored onboarding flow before Home/Browse is published.
        /// Returning completed profiles bypass directly. The durable read runs off the UI thread;
        /// all loader/control/Content work is marshaled back onto the UI thread. The loader is owned
        /// by this host before any callback/await, action admission is closed on retirement, and the
        /// same actual origin/disposal task pipeline is joined from outside dispatch (the host
        /// shutdown awaits it via <see cref="_sources"/>). No Home is published for a damaged or
        /// failed state.
        /// </summary>
        private async Task RunFirstRunOnboardingGateAsync(string profileId, CancellationToken token)
        {
            Dispatcher.UIThread.VerifyAccess();
            var onboarding = _services.GetRequiredService<HomeOnboardingService>();

            // Genuine durable I/O reads off the UI thread; marshal back before touching controls.
            var read = await onboarding.ReadCompletionAsync(profileId, token).ConfigureAwait(false);
            if (read.IsCompleted) return; // healthy returning profile bypasses.
            token.ThrowIfCancellationRequested();

            // Marshal the ENTIRE mount + wait + retirement onto the UI thread and await the SAME
            // mount+retirement task (never a void/async-void/two-level-yield surrogate). Passing the
            // method group directly makes InvokeAsync's returned task represent the full mount,
            // including the loader drain, so the gate cannot publish Home before real retirement.
            await Dispatcher.UIThread.InvokeAsync(() => MountOnboardingUiAsync(onboarding, read, token));
        }

        /// <summary>Mounts the authored onboarding CUI on the UI thread and awaits durable completion
        /// (or cancellation). Structured damaged/unavailable reads project a recovery page; bytes are
        /// preserved, never auto-completed and never published as fresh/complete. It uses the SAME
        /// thin production helper (<see cref="FirstRunOnboardingFlow.Mount"/>) as the normal Desktop
        /// owning tests, and retains+awaits the SAME real drain before returning.</summary>
        private async Task MountOnboardingUiAsync(HomeOnboardingService onboarding,
            HomeOnboardingCompletionRead read, CancellationToken token)
        {
            Dispatcher.UIThread.VerifyAccess(); DemandCurrent();

            var recovery = read.State switch
            {
                HomeOnboardingReadState.Corrupt or HomeOnboardingReadState.IncompatibleSchema
                    or HomeOnboardingReadState.Conflict or HomeOnboardingReadState.Unavailable =>
                    new HomeOnboardingRecoveryInfo(read.Code ?? "DamagedState",
                        read.Message ?? "The local onboarding state could not be read safely."),
                _ => null,
            };

            var actor = _actor ?? throw new InvalidOperationException("The current Home profile is unavailable.");
            var session = FirstRunOnboardingFlow.Mount(onboarding, actor.ProfileId, recovery,
                onQuit: () => Dispatcher.UIThread.Post(RequestRetirement));

            // Retain ownership IMMEDIATELY after acquisition, before any fallible operation (the
            // startup-loader dispose below or any fault in DemandCurrent/registration). If retention
            // fails, retire the freshly mounted session so it is never leaked, then propagate.
            Task run;
            try
            {
                lock (_gate)
                {
                    DemandCurrent();
                    run = RunSessionToPublicationAsync(session, token);
                    _sources.Add(run);
                }
            }
            catch
            {
                try { await session.RetireAsync(); }
                catch { /* best-effort cleanup; the original fault propagates */ }
                throw;
            }

            // Unconditional cleanup after retirement: the run task itself retires the session in its
            // own finally and propagates wait/cleanup faults. The startup-loader dispose must not
            // abort the run (which would abandon the already-mounted session); its faults are
            // retained and aggregated with the run's.
            var failures = new List<Exception>();
            try
            {
                try { _startupLoadingLoader?.Dispose(); _startupLoadingLoader = null; }
                catch (Exception exception) { failures.Add(exception); }
                try { await run; }
                catch (Exception exception) { failures.Add(exception); }
            }
            finally
            {
                if (!run.IsCompleted)
                {
                    try { await run; }
                    catch (Exception exception) { failures.Add(exception); }
                }
            }
            if (failures.Count > 0)
            {
                if (failures.Count == 1)
                    System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failures[0]).Throw();
                throw new AggregateException("Onboarding mount/cleanup failed.", failures);
            }
        }

        /// <summary>Runs a mounted onboarding session to durable completion (or cancellation) and
        /// retires it via the helper's real drain. Content is published only on genuine completion;
        /// cleanup runs unconditionally even after retirement (no DemandCurrent gate) and every
        /// wait/cleanup fault is retained and propagated, never swallowed.</summary>
        private async Task RunSessionToPublicationAsync(FirstRunOnboardingSession session, CancellationToken token)
        {
            var root = session.Root;
            Content = root;
            var completed = false;
            var failures = new List<Exception>();
            try
            {
                // Stay on the UI thread through the wait and retirement: ConfigureAwait(default)
                // re-captures the UI SynchronizationContext, so dispose/drain/Content below never
                // run off-UI.
                var wait = session.CompletionTask.WaitAsync(token);
                try { await wait; completed = true; }
                catch (Exception exception)
                {
                    // FULL raw-source custody at the publication boundary: a FAULTED wait retains its
                    // entire original cause group (wait.Exception), never just the first exception surfaced
                    // by await — so a sibling IOException next to an owner OCE is never lost, and a sole
                    // faulted owner OCE remains a genuine fault (never a TaskCanceled, never clean). Only a
                    // genuinely CANCELED raw wait plus pure owner-token accepted-retirement qualification is
                    // benign here.
                    ObserveWaitFailure(failures, wait, exception, token,
                        _startupRetirement || _closed || OriginalClose is not null);
                }
            }
            finally
            {
                try { await session.RetireAsync(); }
                catch (Exception exception) { failures.Add(exception); }
                if (!completed && ReferenceEquals(Content, root)) Content = null;
            }
            if (failures.Count > 0)
            {
                if (failures.Count == 1)
                    System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failures[0]).Throw();
                throw new AggregateException("Onboarding mount/cleanup failed.", failures);
            }
        }

        /// <summary>True ONLY for a genuinely CANCELED raw wait observation (raw-task status, not type/type)
        /// caused by a PURE owner-token cancellation while the host is actually in (or past) its own
        /// cooperative retirement. This is the sole benign cooperative-stop qualification at the onboarding
        /// wait boundary. A FAULTED wait (underlying session fault carrying even the same owner-token OCE),
        /// foreign/mixed cancellation and any independent cleanup/retirement fault never qualify and stay
        /// genuine retained failures. Awaiting a faulted same-token OCE through an async wrapper can present
        /// the INTERMEDIATE task as canceled, so production passes the RAW wait task captured before await.</summary>
        internal static bool IsBenignCooperativeWaitCancellation(Task wait, CancellationToken token, Exception failure, bool retiring)
        {
            if (!wait.IsCanceled || !retiring || !token.IsCancellationRequested) return false;
            return IsOwnerTokenCancellation(token, failure);
        }

        /// <summary>Captures a wait-observation failure with FULL raw-source custody at the onboarding
        /// publication boundary. A FAULTED raw wait retains its ENTIRE original cause group
        /// (<see cref="Task.Exception"/>), never just the first exception surfaced by await, and is NEVER
        /// normalized — even for a same-token OCE or an all-owner-OCE aggregate. Only a genuinely CANCELED
        /// raw wait (<see cref="Task.IsCanceled"/>) that additionally satisfies the pure owner-token
        /// accepted-retirement qualification is benign and contributes no failure.</summary>
        internal static void ObserveWaitFailure(IList<Exception> failures, Task wait, Exception surfaced,
            CancellationToken token, bool retiring)
        {
            if (wait.IsFaulted)
            {
                failures.Add(wait.Exception ?? surfaced);
                return;
            }
            if (!IsBenignCooperativeWaitCancellation(wait, token, surfaced, retiring))
                failures.Add(surfaced);
        }

        /// <summary>True when the host is committed to (or past) its own cooperative retirement AND the
        /// effective startup token is canceled. Used at the startup checkpoints to STOP progress before late
        /// Home/publication via a benign control-flow return (never by erasing a genuine faulted source).</summary>
        private bool IsAcceptedStartupRetirement(CancellationToken token)
            => (_startupRetirement || _closed || OriginalClose is not null) && token.IsCancellationRequested;

        /// <summary>Compiled Canvas/Boards entry after the SAME Home/onboarding/workspace startup.
        /// Selection is not authority; the existing Browse registration and owning app admit it.</summary>
        internal Task? TryAcquireInitialUnifiedProductRoute(OriginalUnifiedProduct product, CancellationToken token)
        {
            var route = product == OriginalUnifiedProduct.Boards
                ? "browse://home/library/boards" : OriginalUnifiedProductRoute(product);
            Dispatcher.UIThread.VerifyAccess(); DemandExternalOriginalRetirementJoin();
            lock (_gate)
            {
                if (_startupRetirement || _closed || OriginalClose is not null) return null;
                if (_initialization is not { IsCompletedSuccessfully: true } || _chrome is not { } chrome || _workspace is not { IsReady: true })
                    throw new InvalidOperationException("The actual unified workspace must complete startup before native product navigation.");
                return RunObservation(async ledger =>
                {
                    var dispatch = ledger.Invoke(() => Dispatcher.UIThread.InvokeAsync<Task>(() =>
                    {
                        DemandCurrent(); token.ThrowIfCancellationRequested();
                        return ledger.Invoke(() => chrome.NavigateAsync(route, token));
                    }).GetTask());
                    var original = await ledger.AwaitAsync(dispatch).ConfigureAwait(false);
                    await ledger.AwaitAsync(original).ConfigureAwait(false);
                    // Navigation settlement cannot certify a host that retired while it was awaited.
                    // Re-enter the same finite physical owner before startup consumes this result.
                    return ledger.Invoke(() =>
                    {
                        DemandCurrent(); token.ThrowIfCancellationRequested();
                        return true;
                    });
                });
            }
        }

        private NewDashboardPage CreateDashboard()
        {
            Dispatcher.UIThread.VerifyAccess(); DemandCurrent();
            var page = new NewDashboardPage(_services.GetRequiredService<HavenEventBus>(),
                _services.GetRequiredService<IModeRegistry>(), _services.GetRequiredService<IModeUsageRepository>(),
                _services.GetRequiredService<IPinRepository>(), _services.GetRequiredService<IConversationRepository>(),
                _services.GetRequiredService<IVersionedSettingsStore>(), _services.GetRequiredService<IDashboardRepository>(),
                _services.GetRequiredService<IDashboardLayoutRepository>(), _services.GetServices<IDashboardTileProvider>());
            page.EnableDashboardAssistant(new DashboardEditPlanner(_services.GetRequiredService<IOllamaClient>(),
                () => _services.GetRequiredService<UserPreferencesService>().DefaultModel));
            return page;
        }

        private async Task AdmitHomeAsync(BrowseAppContentRequest request, CancellationToken token)
        {
            var actor = await ObserveActorAsync(token).ConfigureAwait(false);
            if (_actor is null || actor != _actor || !StringComparer.Ordinal.Equals(actor.ProfileId, request.ProfileId))
                throw new UnauthorizedAccessException("The Home tab does not belong to the current authenticated profile.");
            var ready = await CheckAsync(token).ConfigureAwait(false);
            if (ready.State != CuiSceneAvailabilityState.Ready) throw new UnauthorizedAccessException(ready.Message);
        }

        private HomeLibraryDestinationPage CreateHomeLibraryPage(BrowseAppContentRequest request, CuiThemePreferenceContext theme)
        {
            DemandCurrent();
            var actor = _actor ?? throw new UnauthorizedAccessException("The original Library profile is unavailable.");
            if (request.Privacy != BrowserTabPrivacy.Standard || request.ProfileId != actor.ProfileId)
                throw new UnauthorizedAccessException("Library belongs to the actual standard Home profile.");
            var approvals = _services.GetRequiredService<HomeNativeApprovalWindowOwner>();
            if (!approvals.IsBoundToOriginalComposition(_home))
                throw new UnauthorizedAccessException("Retain the original Home review-window owner.");
            HomeLibraryDestinationPage? page = null;
            NativeCompositeHomeLibrarySource? source = null;
            Task Admit(CancellationToken token)
            { token.ThrowIfCancellationRequested(); DemandAppearanceOwner(); return Task.CompletedTask; }
            bool Alive() => !_closed && !_appLifetime.IsCancellationRequested && !_windowLifetime.IsCancellationRequested;
            void Parent(Action body) => (page ?? throw new InvalidOperationException("Retain the original Library page before source work."))
                .InvokeOriginalSource(() => CloudflareOriginalExecutionGuard.InvokeOriginal(
                    source ?? throw new InvalidOperationException("Retain the original source composition before source work."),
                    () => { body(); return true; }));
            void WriteScope(Action body) => CloudflareOriginalExecutionGuard.InvokeOriginal(this, () =>
            { DemandCurrent(); body(); return true; });
            void WriteRetain(Task actual) { _ = Track(actual); }
            var boardsSource = new NativeHomeArtifactLibrarySource(_home,
                _services.GetRequiredService<Haven.Infrastructure.NotesRepository>(),
                _services.GetRequiredService<Haven.Infrastructure.VerifiedNotesRepository>(),
                _services.GetRequiredService<HomeNotesResourceAdmissionSource>(), actor,
                "home-library:" + request.TabId.ToString("D"),
                (authorization, token) => PresentHomeLibraryReviewAsync(approvals, authorization, token), Admit, Parent, Alive);
            var browser = _services.GetRequiredService<HavenOS.Files.NativeHost.FilesNativeBrowserService>();
            var boardRead = _boardReadSource ?? throw new UnauthorizedAccessException("The original Home Files board reader is unavailable.");
            if (!browser.IsBoundToOriginalComposition(
                _services.GetRequiredService<HavenOS.Files.NativeHost.NativeFilesWorkspaceAuthority>(),
                _home.Profiles, _home.Resources, _services.GetRequiredService<ICompatibilityPackageContentSource>()))
                throw new UnauthorizedAccessException("Library must borrow the actual Files/Home metadata owner tuple.");
            var filesSource = new NativeHomeFilesLibrarySource(browser, actor, _home.StateStore,
                ObserveAppearanceAuthorityAsync, Parent, Alive, boardRead, _boardWriteSource,
                (authorization, token) => PresentHomeLibraryReviewAsync(approvals, authorization, token),
                WriteScope, WriteRetain);
            source = new(_home.StateStore, new("boards", "Boards", boardsSource), new("files", "Files", filesSource));
            var preparedFiles = new System.Runtime.CompilerServices.ConditionalWeakTable<HomeDeepLink, AppContentAddress>();
            var preparedBoards = new System.Runtime.CompilerServices.ConditionalWeakTable<HomeDeepLink, PreparedBoardRead>();
            async Task PrepareFilesNavigation(HomeDeepLink link, CancellationToken token)
            {
                if (link.OwnerApp != "files" || link.TargetRouteId is not ("files.folder" or "files.preview" or "files.board")) return;
                lock (preparedFiles) preparedFiles.Remove(link);
                lock (preparedBoards)
                    if (preparedBoards.TryGetValue(link, out var previous))
                    {
                        preparedBoards.Remove(link);
                        _ = Track(previous.Writable
                            ? (_boardWriteSource?.DiscardPreparedAsync(previous.Token) ?? Task.CompletedTask)
                            : boardRead.DiscardPreparedAsync(previous.Token));
                    }
                var target = await filesSource.ResolveOriginalOpenTargetAsync(link, token).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                if (target is null) return;
                if (link.TargetRouteId == "files.board")
                {
                    var writable = filesSource.HasWritableBoardSource;
                    var prepared = writable
                        ? await filesSource.PrepareOriginalBoardWriteAsync(link, token).ConfigureAwait(false)
                        : await filesSource.PrepareOriginalBoardReadAsync(link, token).ConfigureAwait(false);
                    if (prepared is not { } tokenId) return;
                    try
                    {
                        token.ThrowIfCancellationRequested();
                        var boardAddress = writable
                            ? BoardsBrowseAppContentAddress.ForWritableFile(target.StoreId, target.FolderId,
                                target.FileId!.Value, target.RevisionId!.Value, tokenId)
                            : BoardsBrowseAppContentAddress.ForFile(target.StoreId, target.FolderId,
                                target.FileId!.Value, target.RevisionId!.Value, tokenId);
                        lock (preparedBoards) { preparedBoards.Remove(link); preparedBoards.Add(link, new(boardAddress, tokenId, writable)); }
                    }
                    catch
                    {
                        _ = Track(writable
                            ? (_boardWriteSource?.DiscardPreparedAsync(tokenId) ?? Task.CompletedTask)
                            : boardRead.DiscardPreparedAsync(tokenId));
                        throw;
                    }
                    return;
                }
                var address = target.FileId is { } file && target.RevisionId is { } revision
                    ? FilesBrowseAppContentAddress.ForTextPreview(target.StoreId, target.FolderId, file, revision)
                    : FilesBrowseAppContentAddress.For(target.StoreId, target.FolderId);
                lock (preparedFiles) { preparedFiles.Remove(link); preparedFiles.Add(link, address); }
            }
            page = new(source.CreateService, source.RevalidateAsync, (link, settlement) =>
            {
                AppContentAddress address;
                PreparedBoardRead? queuedBoard = null;
                if (link.OwnerApp == "boards" && link.TargetRouteId == "boards.notebook" &&
                    Guid.TryParseExact(link.ObjectId, "D", out var id) && id != Guid.Empty && link.ObjectId == id.ToString("D"))
                    address = AppContentAddress.Parse("9to1://boards/notebooks/" + id.ToString("D"));
                else if (link.OwnerApp == "files" && link.TargetRouteId == "files.board")
                {
                    if (!preparedBoards.TryGetValue(link, out var prepared)) return false;
                    queuedBoard = prepared;
                    address = prepared.Address;
                    preparedBoards.Remove(link);
                }
                else if (link.OwnerApp == "files" && (link.TargetRouteId is "files.folder" or "files.preview"))
                {
                    // Issued-link resolution already rechecked this exact row through the actual
                    // source. The queued destination still independently obtains its fresh row/READ.
                    if (!preparedFiles.TryGetValue(link, out var issuedAddress)) return false;
                    address = issuedAddress;
                }
                else return false;
                var accepted = IsCurrent && _workspace is { } workspace && workspace.QueueNavigation(
                    new(request.TabId, address, true), settlement);
                if (!accepted && queuedBoard is not null) Track(queuedBoard.Writable
                    ? (_boardWriteSource?.DiscardPreparedAsync(queuedBoard.Token) ?? Task.CompletedTask)
                    : boardRead.DiscardPreparedAsync(queuedBoard.Token));
                return accepted;
            }, source, theme, PrepareFilesNavigation);
            if (request.Address.ResourceSegments.SequenceEqual(new[] { "library", "boards" }, StringComparer.Ordinal))
                page.SetInitialQuery("files", "9to1 board document");
            return page;
        }
        private async Task PresentHomeLibraryReviewAsync(HomeNativeApprovalWindowOwner approvals,
            HomePermissionAuthorization authorization, CancellationToken token)
        {
            var originals = new CloudflareOriginalTaskLedger(); originals.BindOriginalOwner(this);
            originals.BindOriginalCallerCallback(body => CloudflareOriginalExecutionGuard.InvokeOriginal(this,
                () => { DemandAppearanceOwner(); body(); return true; }));
            await originals.RunToOriginalSettlementAsync(async () =>
            {
                var dispatch = originals.Invoke(() => Dispatcher.UIThread.InvokeAsync<Task<HomeNativeApprovalDecisionObservation>>(
                    () => approvals.OpenAndObserveDecisionOriginalAsync(authorization.RequestId, token)).GetTask());
                var actual = await originals.AwaitAsync(dispatch).ConfigureAwait(false);
                var decision = await originals.AwaitAsync(actual).ConfigureAwait(false);
                if (decision.RequestId != authorization.RequestId)
                    throw new InvalidDataException("The actual Home review observation belongs to another request.");
                // This records no grant: the original Notes admission still checks the broker
                // decision through BeginLibrarySnapshot, including dismissal or explicit denial.
                return true;
            }).ConfigureAwait(false);
        }

        private async Task AdmitHomeSettingsAsync(BrowseAppContentRequest request, CancellationToken token)
        {
            if (request.Privacy != BrowserTabPrivacy.Standard || _actor is not { } actor ||
                !StringComparer.Ordinal.Equals(actor.ProfileId, request.ProfileId))
                throw new HomeSettingsAuthorityRefusal("Settings belongs to the original authenticated Home profile.");
            if (!await ObserveAppearanceAuthorityAsync(token).ConfigureAwait(false))
                throw new HomeSettingsAuthorityRefusal("Home settings is unavailable for the current profile or services.");
        }

        // Appearance is owned by this actual App/window/Home composition, not by browser-tab
        // publication. Chrome drains first; admitted preference work remains owned until its
        // SAME source is explicitly sealed and joined below. UI guards keep DemandCurrent.
        private void DemandAppearanceOwner()
        {
            _appLifetime.ThrowIfCancellationRequested(); _windowLifetime.ThrowIfCancellationRequested();
            if (_closed) throw new ObjectDisposedException(nameof(UnifiedWorkspaceHost));
            WindowsHomeSameProcessRuntimeObservation.DemandOriginalBinding(_services, _home);
        }

        private Task<bool> ObserveAppearanceAuthorityAsync(CancellationToken token)
        {
            var actor = _actor ?? throw new UnauthorizedAccessException("The original Home appearance actor is unavailable.");
            // The actual preference source/page owns and joins this authority Task. Do not also
            // retain its cooperative private cancellation as an independent host failure.
            return ObserveOriginalHomeAppearanceAuthorityAsync(_services, _home, actor, _appLifetime, _windowLifetime,
                body => CloudflareOriginalExecutionGuard.InvokeOriginal(this,
                    () => { DemandAppearanceOwner(); body(); return true; }), token);
        }

        private Task<AuthenticatedResourceActor> ObserveActorAsync(CancellationToken token) => RunObservation(async ledger =>
        {
            using var homeSource = CloudflareOriginalExecutionGuard.EnterOriginal(_home);
            void Scope(Action callback) => ledger.Invoke(() => { DemandCurrent(); callback(); return true; });
            void Retain(Task actual) { _ = ledger.Track(actual); }
            var actor = await ledger.AwaitAsync(ledger.Invoke(() =>
                _home.Profiles.GetCurrentAsync(Scope, Retain, token).AsTask())).ConfigureAwait(false)
                ?? throw new UnauthorizedAccessException("The authenticated Home profile is unavailable.");
            DemandCurrent();
            if (_actor is { } original && actor != original)
                throw new UnauthorizedAccessException("The original Home profile changed.");
            return actor;
        });

        public ValueTask<CuiSceneAvailability> CheckAsync(CancellationToken token) => new(RunObservation(async ledger =>
        {
            var ready = await ledger.AwaitAsync(ledger.Invoke(() => WindowsHomeSameProcessRuntimeObservation.CheckAsync(
                _services, _home, _appLifetime, _windowLifetime,
                callback => ledger.Invoke(() => { DemandCurrent(); callback(); return true; }), DemandCurrent, token))).ConfigureAwait(false);
            var actor = await ObserveActorAsync(token).ConfigureAwait(false);
            if (_actor is { } original && actor != original) throw new UnauthorizedAccessException("The original Home profile changed.");
            DemandCurrent();
            return WindowsHomeSameProcessRuntimeObservation.RevalidateBeforePublication(_services, _home, ready);
        }));

        private Task<T> RunObservation<T>(Func<CloudflareOriginalTaskLedger, Task<T>> body)
        {
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Task<T> result;
            lock (_gate)
            {
                DemandCurrent();
                _sources.RemoveWhere(task => task.IsCompletedSuccessfully);
                if (_sources.Count >= 128) throw new InvalidOperationException("Original host observations must settle before more are admitted.");
                result = Run(start.Task); _sources.Add(result);
            }
            start.SetResult(); return result;
            async Task<T> Run(Task gate)
            {
                await gate.ConfigureAwait(false);
                using var phase = CloudflareOriginalExecutionGuard.EnterOriginal(this);
                var ledger = new CloudflareOriginalTaskLedger(); ledger.BindOriginalOwner(this);
                ledger.BindOriginalCallerCallback(callback => CloudflareOriginalExecutionGuard.InvokeOriginal(this,
                    () => { DemandCurrent(); callback(); return true; }));
                return await ledger.RunToOriginalSettlementAsync(() => body(ledger)).ConfigureAwait(false);
            }
        }

        private void OnWorkspaceKeyDown(object? sender, KeyEventArgs args)
        {
            if (OriginalClose is not null || _workspace is not { IsReady: true } workspace || args.Handled) return;
            using var callback = CloudflareOriginalExecutionGuard.EnterOriginal(this);
            if (args.KeyModifiers.HasFlag(KeyModifiers.Control) && args.Key == Key.L)
            {
                var address = this.GetLogicalDescendants().OfType<TextBox>().SingleOrDefault(control => control.Name == "BrowseAddressBox");
                address?.Focus(); address?.SelectAll(); args.Handled = true; return;
            }
            var command = args.Key switch
            {
                Key.T when args.KeyModifiers.HasFlag(KeyModifiers.Control) && !args.KeyModifiers.HasFlag(KeyModifiers.Alt) => args.KeyModifiers.HasFlag(KeyModifiers.Shift) ? "9to1.Browse.ReopenClosedTab" : "9to1.Browse.NewTab",
                Key.P when args.KeyModifiers == (KeyModifiers.Control | KeyModifiers.Shift) => "9to1.Browse.NewPrivateTab",
                Key.W when args.KeyModifiers.HasFlag(KeyModifiers.Control) => "9to1.Browse.CloseTab",
                Key.R when args.KeyModifiers.HasFlag(KeyModifiers.Control) => "9to1.Browse.Reload",
                Key.Left when args.KeyModifiers.HasFlag(KeyModifiers.Alt) => "9to1.Browse.Back",
                Key.Right when args.KeyModifiers.HasFlag(KeyModifiers.Alt) => "9to1.Browse.Forward",
                Key.Enter when this.GetLogicalDescendants().OfType<TextBox>().SingleOrDefault(control => control.Name == "BrowseAddressBox")?.IsFocused == true => "9to1.Browse.Navigate",
                _ => null
            };
            if (command is null || workspace.IsActionAvailable(command) != true) return;
            args.Handled = true;
            var actual = CloudflareOriginalExecutionGuard.InvokeOriginal(this, () => workspace.DispatchAsync(command, null).AsTask());
            lock (_gate) _sources.Add(actual);
            ObserveKeyboardSource(actual);
        }
        private static async void ObserveKeyboardSource(Task actual)
        {
            try { await actual; }
            catch (Exception failure) { System.Diagnostics.Trace.TraceError("Unified Browse retained keyboard failure: {0}", failure); }
        }

        private Task Track(Task actual) { lock (_gate) _sources.Add(actual); return actual; }
        private Task<T> Track<T>(Task<T> actual) { lock (_gate) _sources.Add(actual); return actual; }
        private void DemandCurrent()
        {
            if (!IsCurrent) throw new ObjectDisposedException(nameof(UnifiedWorkspaceHost));
            WindowsHomeSameProcessRuntimeObservation.DemandOriginalBinding(_services, _home);
        }

        internal async Task<bool> TryPrepareToCloseAsync(CancellationToken token)
        {
            Dispatcher.UIThread.VerifyAccess(); DemandExternalOriginalRetirementJoin();
            if (OriginalClose is not null) return true;
            var preparation = await PrepareCloseAsync(token);
            if (preparation is null) return false;
            await preparation.AbortAsync(); // A bool caller cannot retain a strong lease.
            return true;
        }

        public void DemandExternalOriginalRetirementJoin()
        {
            CloudflareOriginalExecutionGuard.DemandExternalJoin(this);
            _originalDownloadFiles?.DemandExternalOriginalRetirementJoin();
            if (_workspace is { } workspace) workspace.DemandExternalOriginalRetirementJoin();
            else _chrome?.DemandExternalOriginalRetirementJoin();
        }
        public void RequestRetirement() { DemandExternalOriginalRetirementJoin(); _ = CloseAndDrainAsync(); }
        public Task CloseAndDrainAsync()
        {
            Dispatcher.UIThread.VerifyAccess(); DemandExternalOriginalRetirementJoin();
            lock (_gate)
            {
                if (OriginalClose is { } accepted) return accepted;
                if (_pendingClose is not null) return _pendingClose;
                var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                _pendingClose = CloseCoreAsync(start.Task); _closeAttempts.Add(_pendingClose);
                start.SetResult(); return _pendingClose;
            }
        }
        private async Task CloseCoreAsync(Task start)
        {
            await start;
            using var phase = CloudflareOriginalExecutionGuard.EnterOriginal(this);
            var failures = new List<Exception>();
            async Task Join(Task? source) { if (source is not null) await JoinOriginalAppTaskAsync(source, failures); }
            try
            {
                if (_initialization is null or { IsCompleted: false })
                {
                    // Retain the exact accepted owner before cancellation callbacks
                    // run. App/window authority remains alive for late acquisitions.
                    _startupRetirement = true; _close = _pendingClose;
                    lock (_gate) AcceptPreparation(_activePreparation ?? new ClosePreparation(this, captureOwners: false));
                    KeyDown -= OnWorkspaceKeyDown;
                    try { _startupCancellation.Cancel(); } catch (Exception error) { AddAppCause(failures, error); }
                    try { _sceneClose = _scene?.CloseOriginalAsync(); } catch (Exception error) { AddAppCause(failures, error); }
                }
                if (!_startupRetirement && _close is null && HasStrongOwner(_chrome))
                {
                    var preparation = await AcquirePreparation(CancellationToken.None);
                    if (preparation is null) throw new CloseVetoException();
                    _ = BeginPreparedClose(preparation); // SAME pending host driver; never join itself.
                }
                else if (!_startupRetirement && _close is null && _activePreparation is { } weak && !weak.IsCurrent)
                    throw new CloseVetoException();
                await Join(_initialization);
                try
                {
                    if (_workspace is not null) _workspaceClose = _workspace.DisposeAsync().AsTask();
                    else if (_chrome is not null) _workspaceClose = _chrome.DisposeAsync().AsTask();
                    if (_workspaceClose is not null) await _workspaceClose;
                }
                catch (BrowseCloseVetoException) when (!_startupRetirement && _chrome?.OriginalClose is null)
                {
                    // A genuine declined save is an attempt, not an accepted close.
                    // Keep the original task inspectable without poisoning later work.
                    throw;
                }
                catch (Exception error)
                {
                    if (!_startupRetirement && _chrome is not null && _chrome.OriginalClose is null)
                    {
                        if (_workspaceClose is not null) lock (_gate) _sources.Add(_workspaceClose);
                        throw; // A real preflight fault is never reclassified as a veto.
                    }
                    if (_workspaceClose is not null) await Join(_workspaceClose);
                    else AddAppCause(failures, error);
                }
                _close ??= _pendingClose;
                lock (_gate) AcceptPreparation(_activePreparation ?? new ClosePreparation(this, captureOwners: false));
                foreach (var cause in _acceptedPreparationFailures) AddAppCause(failures, cause);
                foreach (var actual in _acceptedChromeSources.Distinct()) await Join(actual);
                KeyDown -= OnWorkspaceKeyDown;
                try { _startupLoadingLoader?.Dispose(); _startupLoadingLoader = null; }
                catch (Exception error) { AddAppCause(failures, error); }
                // A failed CreateAsync can retain an owner whose own cleanup failed.
                // A null returned owner is therefore not proof that producers stopped.
                var producersStopped = _workspaceClose is { IsCompletedSuccessfully: true } ||
                    (_chromeCreation is null && _chrome is null);
                await JoinSharedPanelSettlementAsync(failures);
                if (producersStopped)
                {
                    // Browser admission/commands and native engines have drained. Only
                    // then retire this Files registration cohort; Home/Files are borrowed.
                    try { await JoinOriginalDownloadFilesAsync(failures); }
                    catch (Exception error) { AddAppCause(failures, error); }
                    try { if (_boardReadSource is not null) await _boardReadSource.DrainPreparedAsync(); }
                    catch (Exception error) { AddAppCause(failures, error); }
                    try { if (_boardWriteSource is not null) await _boardWriteSource.DrainPreparedAsync(); }
                    catch (Exception error) { AddAppCause(failures, error); }
                    try { if (_boardDocumentWriteOwner is not null) await _boardDocumentWriteOwner.CloseAndDrainAsync(); }
                    catch (Exception error) { AddAppCause(failures, error); }
                    try { _sceneClose ??= _scene?.CloseOriginalAsync(); } catch (Exception error) { AddAppCause(failures, error); }
                    await Join(_sceneClose);
                    if (_sceneClose is null or { IsCompletedSuccessfully: true })
                        try { if (_homeTheme is not null) await _homeTheme.DisposeAsync(); }
                        catch (Exception error) { AddAppCause(failures, error); }
                    try { if (_session is not null) _sessionClose = _session.SealAndDrainAsync(); }
                    catch (Exception error) { AddAppCause(failures, error); }
                    await Join(_sessionClose);
                }
                Task[] originals; lock (_gate) originals = _sources.ToArray();
                foreach (var original in originals) await Join(original);
                ThrowAppCauses(failures);
                _closed = true;
            }
            catch (Exception failure) { AddAppCause(failures, failure); }
            finally
            {
                if (OriginalClose is null)
                {
                    if (_activePreparation is { Accepted: false } unaccepted)
                    {
                        try { await Join(AbortPreparation(unaccepted)); }
                        catch (Exception cleanup) { AddAppCause(failures, cleanup); }
                    }
                    _pendingClose = null; _workspaceClose = null;
                }
            }
            ThrowAppCauses(failures);
        }
    }
}
#endif
