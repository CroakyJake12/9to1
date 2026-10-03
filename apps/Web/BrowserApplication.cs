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
    private CancellationTokenSource? _navigationCancellation;
    private CuiControlLoader? _loader;
    private IDisposable? _surfaceLifetime;
    private BrowserHomeContext? _home;
    private CuiDocument? _homeDocument;
    private readonly Dictionary<string, Vector> _scrollOffsets = new(StringComparer.Ordinal);
    private string? _currentAddress;
    private long _navigationVersion;
    private bool _disposed;

    /// <summary>Registration is supplied by the composition root after obtaining real authenticated owner adapters.</summary>
    public BrowserSurfaceRegistry Surfaces => _surfaces;

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is not ISingleViewApplicationLifetime lifetime)
            throw new InvalidOperationException("A browser single-view lifetime is required.");
        lifetime.MainView = _view;
        Program.Attach(this);
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

        EnsureHome();
        if (_home!.Open(request!))
        {
            if (!Render(new(_homeDocument!, _home, _home), BrowserRouteCodec.Encode(request!))) return;
            Program.ShowStatus("HomeServiceUnavailable", "Account services are unavailable. Your files and activity have not been loaded.");
            return;
        }

        Program.ShowStatus("Loading", "Opening your destination…");
        var (result, surface) = await _surfaces.OpenAsync(request!, cancellation);
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
            else if (Render(surface, BrowserRouteCodec.Encode(request!))) Program.ShowStatus("Ready", "");
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
        var candidate = new CuiControlLoader();
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
            foreach (var control in root.GetLogicalDescendants().OfType<Control>().Prepend(root))
            {
                var descriptor = candidate.Inspect(control);
                if (control is Button button && descriptor is not null && button.Tag is string command)
                {
                    button.IsEnabled = (surface.Actions as ICuiActionAvailability)?.IsActionAvailable(command) == true;
                    if (!button.IsEnabled) ToolTip.SetTip(button, "This action requires an available account service or browser capability.");
                }
                if (control is TextBox input && ReferenceEquals(surface.Bindings, _home))
                {
                    input.IsEnabled = false;
                    ToolTip.SetTip(input, "Search requires an available account service.");
                }
            }
            var previousLoader = _loader;
            var previousLifetime = _surfaceLifetime;
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
            _loader = candidate;
            _surfaceLifetime = surface.Lifetime;
            _currentAddress = address;
            transferred = true;
            previousLoader?.Dispose();
            previousLifetime?.Dispose();
            return true;
        }
        finally
        {
            if (!transferred)
            {
                candidate.Dispose();
                surface.Lifetime?.Dispose();
            }
        }
    }

    /// <summary>Account/session/organisation changes must clear prior private presentation before new adapters load.</summary>
    public void ResetPrivateContext()
    {
        _navigationCancellation?.Cancel();
        ++_navigationVersion;
        _view.Content = null;
        _loader?.Dispose();
        _loader = null;
        _surfaceLifetime?.Dispose();
        _surfaceLifetime = null;
        _home = null;
        _homeDocument = null;
        _surfaces.Clear();
        _scrollOffsets.Clear();
        _currentAddress = null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        ResetPrivateContext();
        _navigationCancellation?.Dispose();
        _navigationCancellation = null;
    }
}
