using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using Avalonia.LogicalTree;
using CakeOS.Cui;
using CakeOS.Cui.Language;
using CakeOS.Cui.Runtime;
using HavenOS.Home.Core;

namespace NineToOne.Web;

public sealed class BrowserApplication : Application, IAsyncDisposable
{
    private readonly ContentControl _view = new();
    private readonly BrowserSurfaceRegistry _surfaces = new();
    private readonly BrowserAccessibilityBridge _accessibility = new();
    private CancellationTokenSource? _navigationCancellation;
    private CuiControlLoader? _loader;
    private IDisposable? _surfaceLifetime;
    private BrowserActionAvailability.Observation? _availability;
    private BrowserHomeContext? _home;
    private CuiDocument? _homeDocument;
    private readonly Dictionary<string, Vector> _scrollOffsets = new(StringComparer.Ordinal);
    private string? _currentAddress;
    private long _navigationVersion;
    private bool _disposed;
    private bool _closing;
    private int _privateContextResets;
    private long _privateContextVersion;
    private TaskCompletionSource? _closeCompletion;
    private readonly object _privateResetTaskGate = new();
    private readonly HashSet<Task> _privateResetTasks = [];
    private Exception? _presentationCleanupError;

    /// <summary>Registration is supplied by the composition root after obtaining real authenticated owner adapters.</summary>
    public BrowserSurfaceRegistry Surfaces => _surfaces;
    public bool HasUnsavedChanges => !_disposed && _surfaces.HasUnsavedChanges;

    internal string ReadAccessibility()
    {
        _availability?.Refresh();
        return _accessibility.ReadSnapshot();
    }
    internal bool PerformAccessibility(string id, string operation, string? value)
    {
        if (_disposed || _closing || _privateContextResets != 0) return false;
        _availability?.Refresh();
        return _accessibility.Perform(id, operation, value);
    }

    public override void Initialize()
    {
        CuiNativeHost.InitialisePrimitiveTheme(this, "Home");
        NineToOne.Web.Write.WriteRetainedSceneResources.Register(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is not ISingleViewApplicationLifetime lifetime)
            throw new InvalidOperationException("A browser single-view lifetime is required.");
        lifetime.MainView = _view;
        Program.Attach(this);
        BrowserFeatureComposition.Register(_surfaces);
        base.OnFrameworkInitializationCompleted();
    }

    internal void QueueNavigation(string fragment) => _ = NavigateObservedAsync(fragment);

    private async Task NavigateObservedAsync(string fragment)
    {
        var expectedVersion = _navigationVersion + 1;
        try { await OpenFragmentAsync(fragment); }
        catch (OperationCanceledException) { }
        catch (Exception)
        {
            if (!_disposed && _navigationVersion == expectedVersion)
                Program.ShowStatus("BrowserSurfaceFailed", "This destination could not be opened. Try opening it again.");
        }
    }

    public async Task OpenFragmentAsync(string fragment)
    {
        if (_disposed || _closing || _privateContextResets != 0) return;
        var previousCancellation = _navigationCancellation;
        _navigationCancellation = new();
        var cancellation = _navigationCancellation.Token;
        var version = ++_navigationVersion;
        try { previousCancellation?.Cancel(); }
        finally { previousCancellation?.Dispose(); }
        // Cancellation invokes owner callbacks synchronously. A callback may
        // have started a newer request or revoked/closed this context.
        if (_disposed || _closing || _privateContextResets != 0
            || version != _navigationVersion || cancellation.IsCancellationRequested) return;
        if (!BrowserRouteCodec.TryDecode(fragment, out var request, out var code))
        {
            Program.ShowStatus(code!, "This link is invalid. Open a valid application link to continue.");
            return;
        }

        Program.ShowStatus("Loading", "Opening your destination…");
        var dispatch = await BrowserRouteDispatcher.PrepareAsync(_surfaces, request!, BrowserHomeContext.CanOpen, target =>
        {
            EnsureHome();
            var home = new BrowserHomeContext(_homeDocument!, NavigateHome);
            return home.Open(target) ? new(_homeDocument!, home, home, Admission: new HomeAdmission(this, home)) : null;
        }, cancellation);
        var (result, present, isUnavailableHome) = dispatch;
        if (_disposed || _closing || version != _navigationVersion || cancellation.IsCancellationRequested)
        {
            return;
        }
        if (!result.Succeeded || present is null)
        {
            // The requested address stays intact; a failed deep link never opens a different artifact.
            Program.ShowStatus(result.Code, "This destination is unavailable. Your link has been preserved so you can try again.");
            return;
        }
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (_disposed || _closing || version != _navigationVersion || cancellation.IsCancellationRequested)
                return;
            var surface = present();
            if (surface is null)
            {
                Program.ShowStatus("BrowserPresentationExpired", "This destination changed before it could open. Try opening it again.");
                return;
            }
            if (Render(surface, BrowserRouteCodec.Encode(request!)))
                Program.ShowStatus(_presentationCleanupError is not null ? "BrowserPresentationCleanupFailed"
                        : isUnavailableHome ? "HomeServiceUnavailable" : "Ready",
                    _presentationCleanupError is not null ? "Your destination opened, but the previous view could not fully close."
                        : isUnavailableHome ? "Account services are unavailable. Your files and activity have not been loaded." : "");
        });
    }

    private void EnsureHome()
    {
        if (_homeDocument is not null) return;
        using var stream = typeof(BrowserApplication).Assembly.GetManifestResourceStream("NineToOne.Web.Home.cui")
            ?? throw new InvalidOperationException("The canonical Home CUI resource is unavailable.");
        using var reader = new StreamReader(stream);
        var parser = new CuiRichParser();
        var document = parser.Parse(reader.ReadToEnd(), "Home.cui");
        if (parser.Diagnostics.Diagnostics.Any(diagnostic => diagnostic.Severity == CuiDiagnosticSeverity.Error))
            throw new InvalidOperationException("The canonical Home CUI resource is invalid.");
        _homeDocument = document;
    }

    private void NavigateHome(HomeFeatureNavigationRequest request)
    {
        var fragment = BrowserRouteCodec.Encode(request);
        Program.WriteFragment(fragment, false);
        QueueNavigation(fragment);
    }

    private bool Render(BrowserCuiSurface surface, string address)
    {
        var languageVersion = surface.Document.RootProperties.GetValueOrDefault("version") as CuiLiteralValue;
        if (!CuiRuntimeCompatibility.IsLanguageVersionCompatible(languageVersion?.Value))
        {
            try { surface.Lifetime?.Dispose(); }
            finally { surface.Admission?.Reject(); }
            Program.ShowStatus("HomeServiceIncompatible", "This view requires an incompatible runtime. Your previous view has been preserved.");
            return false;
        }
        var candidate = surface.ControlRegistry is null ? new CuiControlLoader() : new CuiControlLoader(surface.ControlRegistry);
        BrowserActionAvailability.Observation? candidateAvailability = null;
        var transferred = false;
        var previousContent = _view.Content;
        var previousRoot = (previousContent as ScrollViewer)?.Content as Control;
        try
        {
            candidate.SetBindingContext(surface.Bindings);
            candidate.SetActionDispatcher(surface.Actions);
            var (root, diagnostics) = candidate.TryLoad(surface.Document);
            if (root is null || diagnostics.Any(diagnostic => diagnostic.Severity == CuiDiagnosticSeverity.Error))
            {
                Program.ShowStatus("HomeServiceIncompatible", "This view is incompatible with the browser runtime. Your previous view has been preserved.");
                return false;
            }
            candidate.WireBindings(root);
            candidateAvailability = BrowserActionAvailability.Observe(root, candidate, surface.Document, surface.Actions, surface.Bindings);
            foreach (var control in root.GetLogicalDescendants().OfType<Control>().Prepend(root))
            {
                if (control is TextBox input && surface.Bindings is BrowserHomeContext)
                {
                    input.IsEnabled = false;
                    ToolTip.SetTip(input, "Search requires an available account service.");
                }
            }
            var previousLoader = _loader;
            var previousLifetime = _surfaceLifetime;
            var previousAvailability = _availability;
            if (_currentAddress is not null && _view.Content is ScrollViewer previousScroll)
            {
                if (!_scrollOffsets.ContainsKey(_currentAddress) && _scrollOffsets.Count >= 256)
                    _scrollOffsets.Remove(_scrollOffsets.Keys.First());
                _scrollOffsets[_currentAddress] = previousScroll.Offset;
            }
            // The surface owner may opt into finite-width reflow; other surfaces retain their authored extent.
            var scroll = new ScrollViewer { Content = root, HorizontalScrollBarVisibility = surface.ConstrainHorizontalLayout
                ? Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled
                : Avalonia.Controls.Primitives.ScrollBarVisibility.Auto };
            if (_scrollOffsets.TryGetValue(address, out var offset))
                scroll.Loaded += (_, _) => scroll.Offset = offset;
            _view.Content = scroll;
            _accessibility.Bind(root);
            // The independent owner commits only after reversible native installation.
            surface.Admission?.Accept();
            _loader = candidate;
            _surfaceLifetime = surface.Lifetime;
            _availability = candidateAvailability;
            _currentAddress = address;
            transferred = true;
            List<Exception>? cleanupErrors = null;
            Retire(() => previousAvailability?.Dispose());
            Retire(() => previousLoader?.Dispose());
            Retire(() => previousLifetime?.Dispose());
            _presentationCleanupError = cleanupErrors is null ? null
                : new AggregateException("The prior browser presentation had teardown failures.", cleanupErrors);
            return true;

            void Retire(Action cleanup)
            {
                try { cleanup(); }
                catch (Exception error) { (cleanupErrors ??= []).Add(error); }
            }
        }
        finally
        {
            if (!transferred)
            {
                try
                {
                    if (!ReferenceEquals(_view.Content, previousContent))
                    {
                        try { _view.Content = previousContent; }
                        finally
                        {
                            if (previousRoot is not null) _accessibility.Bind(previousRoot);
                            else _accessibility.Clear();
                        }
                    }
                }
                finally
                {
                    try { candidateAvailability?.Dispose(); }
                    finally
                    {
                        try { candidate.Dispose(); }
                        finally
                        {
                            try { surface.Lifetime?.Dispose(); }
                            finally { surface.Admission?.Reject(); }
                        }
                    }
                }
            }
        }
    }

    /// <summary>Account/session/organisation changes must clear prior private presentation before new adapters load.</summary>
    public void ResetPrivateContext()
    {
        if (!_surfaces.CanResetPrivateContextSynchronously || _privateContextResets != 0)
            throw new InvalidOperationException("Private asynchronous owners require awaited ResetPrivateContextAsync.");
        ResetPrivateContextAsync().GetAwaiter().GetResult();
    }

    public Task ResetPrivateContextAsync()
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_privateResetTaskGate) _privateResetTasks.Add(completion.Task);
        _ = CompletePrivateContextResetAsync(completion);
        return completion.Task;
    }

    private async Task CompletePrivateContextResetAsync(TaskCompletionSource completion)
    {
        Exception? failure = null;
        try { await ResetPrivateContextCoreAsync(); }
        catch (Exception error) { failure = error; }
        if (failure is null)
        {
            completion.TrySetResult();
            lock (_privateResetTaskGate) _privateResetTasks.Remove(completion.Task);
        }
        else
        {
            _surfaces.HoldPrivateContextFailure(failure);
            completion.TrySetException(failure);
        }
    }

    private async Task DrainPrivateContextResetTasksAsync()
    {
        List<Exception>? errors = null;
        var observed = new HashSet<Task>();
        while (true)
        {
            Task[] tasks;
            lock (_privateResetTaskGate) tasks = _privateResetTasks.Where(task => observed.Add(task)).ToArray();
            if (tasks.Length == 0) break;
            try { await Task.WhenAll(tasks); }
            catch
            {
                foreach (var task in tasks)
                    if (task.Exception is { } failure) (errors ??= []).AddRange(failure.InnerExceptions);
            }
        }
        if (errors is not null) throw new AggregateException("Issued private presentation resets failed.", errors);
    }

    private async Task ResetPrivateContextCoreAsync()
    {
        ++_privateContextResets;
        ++_privateContextVersion;
        ++_navigationVersion;
        var loader = _loader;
        var lifetime = _surfaceLifetime;
        var availability = _availability;
        _loader = null;
        _surfaceLifetime = null;
        _availability = null;
        _home = null;
        _homeDocument = null;
        _presentationCleanupError = null;
        _scrollOffsets.Clear();
        _currentAddress = null;
        List<Exception>? errors = null;
        // Every route and original authority is revoked before cancellation or
        // presentation cleanup can execute user/provider callbacks.
        var reset = _surfaces.BeginPrivateContextReset();
        Remove(_accessibility.Clear);
        Remove(() => _view.Content = null);
        Remove(() => _navigationCancellation?.Cancel());
        Remove(() => availability?.Dispose());
        Remove(() => loader?.Dispose());
        Remove(() => lifetime?.Dispose());
        try { await reset.DrainAsync(); }
        catch (Exception error) { (errors ??= []).Add(error); }
        try { await _surfaces.DrainPrivateContextResetsAsync(); }
        catch (Exception error) { (errors ??= []).Add(error); }
        finally { --_privateContextResets; }
        if (errors is not null) throw new AggregateException("Private browser presentation was removed with teardown failures.", errors);

        void Remove(Action cleanup)
        {
            try { cleanup(); }
            catch (Exception error) { (errors ??= []).Add(error); }
        }
    }

    public async Task<bool> CloseAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed) return true;
        if (_closing) return false;
        _closeCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        _closing = true;
        try
        {
            _view.IsEnabled = false;
            await DrainPrivateContextResetTasksAsync();
            ++_navigationVersion;
            _navigationCancellation?.Cancel();
            var closed = await _surfaces.ClearAsync(cancellationToken);
            if (!closed.Succeeded || closed.Value != true)
            {
                Program.ShowStatus(closed.Code, closed.Message);
                return false;
            }
            _disposed = true;
            await ResetPrivateContextAsync();
            return true;
        }
        catch
        {
            // Preparation failures retain owners; teardown failures occur after
            // all prepared owners have been detached and must clear their view.
            if (_surfaces.AvailableRoutes.Count == 0)
            {
                _disposed = true;
                await ResetPrivateContextAsync();
            }
            throw;
        }
        finally
        {
            try { await DrainPrivateContextResetTasksAsync(); }
            finally
            {
                _closing = false;
                try
                {
                    if (_disposed)
                    {
                        _navigationCancellation?.Dispose();
                        _navigationCancellation = null;
                    }
                    else _view.IsEnabled = true;
                }
                finally
                {
                    _closeCompletion?.TrySetResult();
                    _closeCompletion = null;
                }
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (!await CloseAsync()) throw new InvalidOperationException("Browser owners could not close. Their drafts remain open.");
    }

    internal async Task ReplacePrivateAccountSettingsAsync()
    {
        var reset = ResetPrivateContextAsync();
        var version = _privateContextVersion;
        await reset;
        await DrainPrivateContextResetTasksAsync();
        if (_closeCompletion is { } closing) await closing.Task;
        await DrainPrivateContextResetTasksAsync();
        if (_disposed || _closing || version != _privateContextVersion || _privateContextResets != 0) return;
        BrowserFeatureComposition.RegisterPrivateAccountSettings(_surfaces);
        QueueNavigation(Program.ReadFragment());
    }

    private sealed class HomeAdmission(BrowserApplication application, BrowserHomeContext home) : IBrowserPresentationAdmission
    {
        public void Accept() => application._home = home;
        public void Reject() { }
    }
}
