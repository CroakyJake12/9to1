#if !ANDROID
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using CakeOS.Cui.Language;
using CakeOS.Cui.Runtime;
using Haven.Application;
using Haven.Browser;
using Haven.Core;
using Haven.Desktop.Events;
using Haven.Desktop.Dashboard;
using Haven.Desktop.Services;
using Haven.Desktop.Views.Pages.Home;
using Haven.Desktop.Views.Shell;
using HavenOS.Apps.Browse;
using HavenOS.Home.Core;
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
        private Task<CuiSceneAvailability>? _sceneInitialization;
        private Task<BrowseChrome>? _chromeCreation;
        private BrowseChrome? _chrome;
        private BrowseNativeWorkspace? _workspace;
        private UnifiedWorkspaceSession? _session;
        private AuthenticatedResourceActor? _actor;
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

        internal Task InitializeAsync(CancellationToken token)
        {
            Dispatcher.UIThread.VerifyAccess();
            lock (_gate)
            {
                if (_initialization is not null) return _initialization;
                if (OriginalClose is not null) throw new ObjectDisposedException(nameof(UnifiedWorkspaceHost));
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
            using var source = CloudflareOriginalExecutionGuard.EnterOriginal(this);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, _appLifetime, _windowLifetime, _startupCancellation.Token);
            var currentToken = linked.Token;
            _actor = await ObserveActorAsync(currentToken);
            var availability = await CheckAsync(currentToken);
            if (availability.State != CuiSceneAvailabilityState.Ready) throw new InvalidOperationException(availability.Message);
            DemandCurrent();
            _homeTheme = new NativeHomeThemePreferences(_services.GetRequiredService<UserPreferencesService>().HomeThemePreferencePath,
                new HomeThemeScope(HomeDataScope.DeviceLocal, _actor.ProfileId), UserPreferencesService.ProjectLegacyHomeTheme,
                async lifetime => IsCurrent && await ObserveActorAsync(lifetime).ConfigureAwait(false) == _actor,
                callback => Dispatcher.UIThread.Post(callback));
            await Track(_homeTheme.RefreshAsync(currentToken));
            currentToken.ThrowIfCancellationRequested(); DemandCurrent();
            var coordinator = _services.GetRequiredService<WorkspaceSessionCoordinator>();
            var restored = await Track(coordinator.LoadAsync(currentToken));
            currentToken.ThrowIfCancellationRequested(); DemandCurrent();
            _session = new UnifiedWorkspaceSession(coordinator, restored);
            var persistence = _session.OpenWindow(_session.MainWindowId, _actor.ProfileId);
            var files = FilesBrowseAppContentFactory.BindOriginalSameProcess(_services, _home, _appLifetime, _windowLifetime);
            var spaces = SpacesBrowseAppContentFactory.BindOriginalSameProcess(_services, _home, _appLifetime, _windowLifetime,
                (intent, settlement) => OriginalClose is null && _workspace is { } workspace && workspace.QueueNavigation(intent, settlement));
            var modes = _services.GetRequiredService<IModeRegistry>();
            var apps = new NativeAppContentRegistry(modes,
                [new NativeAppContentRegistration(Guid.Parse("a0000000-0000-0000-0000-000000000030"),
                    "files", HavenAppRouteKind.Files, files),
                 new NativeAppContentRegistration(Guid.Parse("a0000000-0000-0000-0000-000000000022"),
                    "spaces", HavenAppRouteKind.Spaces, spaces)], () => IsCurrent);
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
                AdmitHomeAsync, () => IsCurrent, _windowLifetime, aiAvailable: false);
            IBrowseEngineHostFactory? engines = null;
            if (!OperatingSystem.IsWindows())
                engines = new LinuxWebKitEngineFactory();
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
            _workspace = new BrowseNativeWorkspace(_chrome, _ => IsCurrent);
            var scene = BrowseNativeSurface.CreateScene(_workspace, this, appearance: null, _homeTheme.Context);
            _startupLoadingLoader?.Dispose(); _startupLoadingLoader = null;
            _scene = new CuiSceneHost(scene.ControlRegistry); Content = _scene;
            _sceneInitialization = Track(_scene.ShowAsync(scene, currentToken));
            var ready = await _sceneInitialization;
            _workspace.ObserveReadiness(ready);
            if (ready.State != CuiSceneAvailabilityState.Ready) throw new InvalidOperationException(ready.Message);
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
                if (producersStopped)
                {
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
