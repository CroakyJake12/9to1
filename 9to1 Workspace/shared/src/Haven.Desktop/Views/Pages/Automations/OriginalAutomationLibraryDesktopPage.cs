#if !ANDROID
using System.Runtime.ExceptionServices;
using Avalonia.Controls;
using Avalonia.Threading;
using CakeOS.Cui.Language;
using CakeOS.Cui.Runtime;
using CakeOS.Cui.Themes;
using Haven.Application;
using Haven.Application.Automations;
using Haven.Desktop.Services;
using HavenOS.Home.Core;

namespace Haven.Desktop.Views.Pages.Automations;

internal sealed class OriginalAutomationLibraryDesktopPage : UserControl,
    IDesktopOriginalRetirementParticipant, IDesktopOriginalRetirementJoinGuard
{
    private readonly Window _window;
    private readonly CancellationToken _appLifetime, _windowLifetime;
    private readonly CancellationTokenSource _viewLifetime;
    private readonly DesktopOriginalWorkLifetime _work;
    private readonly TaskCompletionSource _construction = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly List<Exception> _presentationFailures = [];
    private OriginalAutomationLibraryBindings? _bindings;
    private CuiControlLoader? _loader;
    private Control? _root;
    private Avalonia.Controls.ResourceDictionary? _resources;
    private Exception? _constructionFailure;
    private Task? _initialization, _bindingsClose, _loaderIdle, _loaderDispose, _loaderTerminal, _release;
    private CancellationTokenRegistration _retirement;
    private bool _subscribed;
    internal OriginalAutomationLibraryBindings Bindings => _bindings ?? throw new InvalidOperationException("The actual saved-library binding was not acquired.");
    internal Task? OriginalInitialization => _initialization;
    internal Task? OriginalClose => _work.OriginalClose;
    internal Task? OriginalBindingsClose => _bindingsClose;
    internal Task? OriginalLoaderIdle => _loaderIdle;
    internal Task? OriginalLoaderDispose => _loaderDispose;
    internal Task? OriginalLoaderTerminal => _loaderTerminal;

    private OriginalAutomationLibraryDesktopPage(ICanonicalAutomationLibraryOriginalReadSource? source,
        HomeLocalProfileIdentity? profiles, Window window, CancellationToken appLifetime,
        CancellationToken windowLifetime, Action<OriginalAutomationLibraryDesktopPage> capture, ICanonicalAutomationDefinitionOriginalProcessSource? writer = null)
    {
        _window = window; _appLifetime = appLifetime; _windowLifetime = windowLifetime;
        _viewLifetime = CancellationTokenSource.CreateLinkedTokenSource(appLifetime, windowLifetime);
        _work = new(StopAsync, CloseViewAsync);
        try
        {
            CloudflareOriginalExecutionGuard.InvokeOriginal(this, () =>
            {
                capture(this); // Retain the actual partial page before any constructor callbacks.
                DemandPublication();
                _bindings = new(source, profiles, writer);
                _window.Closed += OnWindowClosed; _subscribed = true;
                _retirement = _viewLifetime.Token.Register(static value =>
                    ((OriginalAutomationLibraryDesktopPage)value!).RequestRetirement(), this);
                DemandPublication(); return true;
            });
        }
        catch (Exception cause) { _constructionFailure = cause; _bindings?.RequestRetirement(); throw; }
        finally { _construction.TrySetResult(); }
    }
    internal static OriginalAutomationLibraryDesktopPage BindOriginal(ICanonicalAutomationLibraryOriginalReadSource? source,
        HomeLocalProfileIdentity? profiles, Window window, CancellationToken appLifetime,
        CancellationToken windowLifetime, Action<OriginalAutomationLibraryDesktopPage> capture, ICanonicalAutomationDefinitionOriginalProcessSource? writer = null)
    {
        Dispatcher.UIThread.VerifyAccess(); ArgumentNullException.ThrowIfNull(window); ArgumentNullException.ThrowIfNull(capture);
        if (!appLifetime.CanBeCanceled || !windowLifetime.CanBeCanceled)
            throw new ArgumentException("Retain the actual App and window lifetimes.");
        appLifetime.ThrowIfCancellationRequested(); windowLifetime.ThrowIfCancellationRequested();
        if (!window.IsVisible) throw new ObjectDisposedException("The original window is closed.");
        return new(source, profiles, window, appLifetime, windowLifetime, capture, writer);
    }
    internal bool HasOriginalBinding(ICanonicalAutomationLibraryOriginalReadSource? source,
        HomeLocalProfileIdentity? profiles, Window window, CancellationToken appLifetime, CancellationToken windowLifetime, ICanonicalAutomationDefinitionOriginalProcessSource? writer = null) =>
        ReferenceEquals(_bindings?.OriginalSource, source) && ReferenceEquals(_bindings?.OriginalProfiles, profiles) &&
        ReferenceEquals(_bindings?.OriginalWriter, writer) &&
        ReferenceEquals(_window, window) && _appLifetime == appLifetime && _windowLifetime == windowLifetime;
    internal Task InitializeAsync(CancellationToken caller)
    {
        Dispatcher.UIThread.VerifyAccess();
        if (_initialization is not null) return _initialization;
        return _work.RunAsync(async original =>
        {
            caller.ThrowIfCancellationRequested();
            Acquire(original, () =>
            {
                DemandPublication();
                using var stream = typeof(OriginalAutomationLibraryDesktopPage).Assembly.GetManifestResourceStream(
                    "Haven.Desktop.OriginalAutomationLibrary.cui") ?? throw new InvalidDataException("The authored saved-library CUI is missing.");
                using var reader = new StreamReader(stream); var document = reader.ReadToEnd();
                var appearance = CuiThemeScopeApplier.DetectAppearance();
                _resources = CuiSceneVisualResources.Create("Tasks", appearance);
                var loader = _loader = new CuiControlLoader();
                loader.SetSurface("Tasks"); loader.SetAppearance(appearance);
                loader.SetBindingContext(Bindings); loader.SetActionDispatcher(Bindings);
                var parser = new CuiRichParser(); var parsed = parser.Parse(document, "OriginalAutomationLibrary.cui");
                if (parser.Diagnostics.Diagnostics.Any(item => item.Severity == CuiDiagnosticSeverity.Error))
                    throw new InvalidDataException("The authored saved-library CUI is invalid.");
                var loaded = loader.TryLoad(parsed); _root = loaded.Root;
                if (_root is null || loaded.Diagnostics.Any(item => item.Severity == CuiDiagnosticSeverity.Error))
                    throw new InvalidDataException("The authored saved-library CUI could not be loaded.");
                DemandPublication(); loader.WireBindings(_root);
                Resources.MergedDictionaries.Add(_resources);
                SetValue(ThemeVariantScope.RequestedThemeVariantProperty, CuiSceneVisualResources.Variant(appearance));
                Content = _root; DemandPublication(); return true;
            });
            await original.AwaitAsync(Acquire(original, Bindings.InitializeAsync));
            if (_work.IsRetiring) return; // Join accepted reads without publishing into a retired view.
            original.DemandPublication(); Acquire(original, () => { DemandPublication(); return true; });
        }, actual => _initialization = actual);
    }
    internal void PublishOriginalTab(Action callback)
    {
        Dispatcher.UIThread.VerifyAccess();
        if (_initialization?.IsCompletedSuccessfully != true) throw new InvalidOperationException("Await the actual saved-library initialization.");
        _work.RunSynchronous(original => Acquire(original, () =>
        { DemandPublication(); original.DemandPublication(); callback(); DemandPublication(); original.DemandPublication(); return true; }));
    }
    private T Acquire<T>(DesktopOriginalWorkLifetime.Original original, Func<T> callback)
    {
        try { return CloudflareOriginalExecutionGuard.InvokeOriginal(this, callback); }
        catch (Exception cause)
        {
            lock (_presentationFailures)
                if (!_presentationFailures.Any(prior => ReferenceEquals(prior, cause))) _presentationFailures.Add(cause);
            original.Retain(cause);
            if (cause is OperationCanceledException) throw new AggregateException("The saved-library callback returned no canceled source Task.", cause);
            throw;
        }
    }
    private void DemandPublication()
    {
        Dispatcher.UIThread.VerifyAccess(); _work.DemandAdmission(); _viewLifetime.Token.ThrowIfCancellationRequested();
        if (!_window.IsVisible) throw new ObjectDisposedException("The original saved-library window is closed.");
    }
    public void DemandExternalOriginalRetirementJoin()
    { CloudflareOriginalExecutionGuard.DemandExternalJoin(this); _work.DemandExternalClose(); _bindings?.DemandExternalOriginalRetirementJoin(); }
    public void RequestRetirement() { _bindings?.RequestRetirement(); _work.RequestRetirement(); }
    public Task CloseAndDrainAsync() { DemandExternalOriginalRetirementJoin(); return _work.CloseAndDrainAsync(); }
    private Task StopAsync()
    { _bindings?.RequestRetirement(); _viewLifetime.Cancel(); return Task.CompletedTask; }
    private void OnWindowClosed(object? sender, EventArgs args) => RequestRetirement();
    private static async Task JoinActual(Task actual)
    {
        try { await actual; }
        catch (Exception cause)
        {
            if (actual.Exception is { InnerExceptions.Count: > 0 } payload)
            { if (payload.InnerExceptions.Count == 1) ExceptionDispatchInfo.Capture(payload.InnerExceptions[0]).Throw(); throw payload; }
            if (cause is OperationCanceledException) throw new AggregateException("The saved-library source canceled without a release receipt.", cause);
            throw;
        }
    }
    private async Task CloseViewAsync()
    {
        await _construction.Task;
        var failures = new List<Exception>();
        if (_initialization is not null) await Observe(_initialization);
        // Enroll both already-owned cohorts before joining either. A failed binding
        // close cannot release or hide accepted loader actions and their failures.
        if (_bindings is not null)
            try { _bindingsClose ??= _bindings.CloseAndDrainAsync(); }
            catch (Exception cause) { Remember(cause); }
        if (_loader is { } loader)
        {
            try { _loaderIdle ??= CloudflareOriginalExecutionGuard.InvokeOriginal(this, loader.WhenActionsIdleAsync); }
            catch (Exception cause) { Remember(cause); }
            if (_bindingsClose is not null) await Observe(_bindingsClose);
            var idleHealthy = _loaderIdle is not null && await Observe(_loaderIdle);
            if (idleHealthy)
            {
                try
                {
                    _loaderDispose ??= Dispatcher.UIThread.InvokeAsync(() => CloudflareOriginalExecutionGuard.InvokeOriginal(this,
                        () => { loader.Dispose(); return true; })).GetTask();
                }
                catch (Exception cause) { Remember(cause); }
                if (_loaderDispose is not null) await Observe(_loaderDispose);
            }
            // Observe the loader's actual terminal cohort even when binding close,
            // initial idle or disposal failed. Disposal itself requires healthy idle.
            try { _loaderTerminal ??= CloudflareOriginalExecutionGuard.InvokeOriginal(this, loader.WhenActionsIdleAsync); }
            catch (Exception cause) { Remember(cause); }
            if (_loaderTerminal is not null) await Observe(_loaderTerminal);
        }
        else if (_bindingsClose is not null) await Observe(_bindingsClose);
        if (_constructionFailure is { } construction) Remember(construction);
        lock (_presentationFailures) foreach (var presentation in _presentationFailures) Remember(presentation);
        ThrowFailures(); // Keep the actual tree/resources on every unknown release failure.
        _release ??= Dispatcher.UIThread.InvokeAsync(() => CloudflareOriginalExecutionGuard.InvokeOriginal(this, () =>
        {
            if (ReferenceEquals(Content, _root)) Content = null;
            if (_resources is { } resources) Resources.MergedDictionaries.Remove(resources);
            if (_subscribed) { _window.Closed -= OnWindowClosed; _subscribed = false; }
            _retirement.Dispose(); _viewLifetime.Dispose(); return true;
        })).GetTask();
        await JoinActual(_release);

        void Remember(Exception cause)
        { if (!failures.Any(prior => ReferenceEquals(prior, cause))) failures.Add(cause); }
        async Task<bool> Observe(Task actual)
        {
            try { await actual; return true; }
            catch (Exception cause)
            {
                if (actual.Exception is { InnerExceptions.Count: > 0 } payload)
                    foreach (var direct in payload.InnerExceptions) Remember(direct);
                else Remember(cause);
                return false;
            }
        }
        void ThrowFailures()
        {
            if (failures.Count == 0) return;
            if (failures.Count > 1 || failures[0] is OperationCanceledException)
                throw new AggregateException("Actual saved-library binding and loader release failed.", failures);
            ExceptionDispatchInfo.Capture(failures[0]).Throw();
        }
    }
}
#endif
