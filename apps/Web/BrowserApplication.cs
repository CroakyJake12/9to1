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

public sealed class BrowserApplication : Application, IDisposable
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

    /// <summary>Registration is supplied by the composition root after obtaining real authenticated owner adapters.</summary>
    public BrowserSurfaceRegistry Surfaces => _surfaces;

    internal string ReadAccessibility()
    {
        _availability?.Refresh();
        return _accessibility.ReadSnapshot();
    }
    internal bool PerformAccessibility(string id, string operation, string? value)
    {
        _availability?.Refresh();
        return _accessibility.Perform(id, operation, value);
    }

    public override void Initialize() => CuiNativeHost.InitialisePrimitiveTheme(this, "Home");

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
        if (_disposed) return;
        _navigationCancellation?.Cancel();
        _navigationCancellation?.Dispose();
        _navigationCancellation = new();
        var cancellation = _navigationCancellation.Token;
        var version = ++_navigationVersion;
        if (!BrowserRouteCodec.TryDecode(fragment, out var request, out var code))
        {
            Program.ShowStatus(code!, "This link is invalid. Open a valid application link to continue.");
            return;
        }

        Program.ShowStatus("Loading", "Opening your destination…");
        var dispatch = await BrowserRouteDispatcher.OpenAsync(_surfaces, request!, target =>
        {
            EnsureHome();
            return _home!.Open(target) ? new(_homeDocument!, _home, _home) : null;
        }, cancellation);
        var (result, surface, isUnavailableHome) = dispatch;
        if (_disposed || version != _navigationVersion || cancellation.IsCancellationRequested)
        {
            surface?.Lifetime?.Dispose();
            return;
        }
        if (!result.Succeeded || surface is null)
        {
            // The requested address stays intact; a failed deep link never opens a different artifact.
            Program.ShowStatus(result.Code, "This destination is unavailable. Your link has been preserved so you can try again.");
            return;
        }
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (_disposed || version != _navigationVersion || cancellation.IsCancellationRequested)
                surface.Lifetime?.Dispose();
            else if (Render(surface, BrowserRouteCodec.Encode(request!)))
                Program.ShowStatus(isUnavailableHome ? "HomeServiceUnavailable" : "Ready",
                    isUnavailableHome ? "Account services are unavailable. Your files and activity have not been loaded." : "");
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
        _home = new(document, request =>
        {
            var fragment = BrowserRouteCodec.Encode(request);
            Program.WriteFragment(fragment, false);
            QueueNavigation(fragment);
        });
    }

    private bool Render(BrowserCuiSurface surface, string address)
    {
        var languageVersion = surface.Document.RootProperties.GetValueOrDefault("version") as CuiLiteralValue;
        if (!CuiRuntimeCompatibility.IsLanguageVersionCompatible(languageVersion?.Value))
        {
            surface.Lifetime?.Dispose();
            Program.ShowStatus("HomeServiceIncompatible", "This view requires an incompatible runtime. Your previous view has been preserved.");
            return false;
        }
        var candidate = surface.ControlRegistry is null ? new CuiControlLoader() : new CuiControlLoader(surface.ControlRegistry);
        BrowserActionAvailability.Observation? candidateAvailability = null;
        var transferred = false;
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
                if (control is TextBox input && ReferenceEquals(surface.Bindings, _home))
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
            var scroll = new ScrollViewer { Content = root, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto };
            if (_scrollOffsets.TryGetValue(address, out var offset))
                scroll.Loaded += (_, _) => scroll.Offset = offset;
            _view.Content = scroll;
            _accessibility.Bind(root);
            _loader = candidate;
            _surfaceLifetime = surface.Lifetime;
            _availability = candidateAvailability;
            _currentAddress = address;
            transferred = true;
            try { previousAvailability?.Dispose(); }
            finally
            {
                try { previousLoader?.Dispose(); }
                finally { previousLifetime?.Dispose(); }
            }
            return true;
        }
        finally
        {
            if (!transferred)
            {
                try { candidateAvailability?.Dispose(); }
                finally
                {
                    try { candidate.Dispose(); }
                    finally { surface.Lifetime?.Dispose(); }
                }
            }
        }
    }

    /// <summary>Account/session/organisation changes must clear prior private presentation before new adapters load.</summary>
    public void ResetPrivateContext()
    {
        ++_navigationVersion;
        var loader = _loader;
        var lifetime = _surfaceLifetime;
        var availability = _availability;
        _loader = null;
        _surfaceLifetime = null;
        _availability = null;
        _home = null;
        _homeDocument = null;
        _scrollOffsets.Clear();
        _currentAddress = null;
        List<Exception>? errors = null;
        Remove(() => _navigationCancellation?.Cancel());
        Remove(_accessibility.Clear);
        Remove(() => _view.Content = null);
        Remove(() => availability?.Dispose());
        Remove(() => loader?.Dispose());
        Remove(() => lifetime?.Dispose());
        Remove(_surfaces.ClearPrivateContext);
        if (errors is not null) throw new AggregateException("Private browser presentation was removed with teardown failures.", errors);

        void Remove(Action cleanup)
        {
            try { cleanup(); }
            catch (Exception error) { (errors ??= []).Add(error); }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { ResetPrivateContext(); }
        finally
        {
            _navigationCancellation?.Dispose();
            _navigationCancellation = null;
            _surfaces.Clear();
        }
    }
}
