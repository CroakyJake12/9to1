using System.Text.Json;
using Avalonia.Threading;
using CakeOS.Cui;
using CakeOS.Cui.Language;
using HavenOS.Home.Core;
using NineToOne.Web.Media;

namespace NineToOne.Web.Wave;

public sealed class WaveBrowserFeature : IHomeFeatureRouteHandler, IBrowserCloseParticipant, IDisposable
{
    private readonly WaveBrowserSession _session;
    private readonly CuiDocument _document;
    private WaveViewLifetime? _view;
    public string RouteId => "app.wave";
    // A missing/unacknowledged save receipt leaves IsDirty true; no guessed receipt changes readiness.
    public bool HasUnsavedChanges => _session.IsDirty || _session.IsBusy;

    public Task<HomeCoreOperationResult<bool>> PrepareToCloseAsync(CancellationToken cancellationToken = default) =>
        _session.PrepareToCloseAsync(cancellationToken);

    public WaveBrowserFeature(IWaveBrowserMedia media, string markup)
    {
        _session = new(media);
        var parser = new CuiRichParser(); _document = parser.Parse(markup, "Wave.cui");
        if (parser.Diagnostics.Diagnostics.Any(d => d.Severity == CuiDiagnosticSeverity.Error))
            throw new InvalidDataException("The browser Wave CUI document is invalid.");
    }

    public static HomeCoreOperationResult<bool> Register(BrowserSurfaceRegistry registry)
    {
        using var stream = typeof(WaveBrowserFeature).Assembly.GetManifestResourceStream("NineToOne.Web.Wave.cui")
            ?? throw new InvalidOperationException("The Wave browser CUI resource is unavailable.");
        using var reader = new StreamReader(stream);
        var feature = new WaveBrowserFeature(new BrowserWaveMedia(), reader.ReadToEnd());
        var result = registry.Register(feature, feature.Render, BrowserSurfaceScope.DeviceLocal);
        if (!result.Succeeded) feature.Dispose();
        return result;
    }

    public async Task<HomeFeatureNavigationResult> OpenAsync(HomeFeatureNavigationRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.RouteId != RouteId || request.DeepLink is not null || request.Action is not null || request.ModelPickerTarget is not null
            || request.EntityType is not null && request.EntityType != "WaveProject")
            return new(false, "InvalidArgument", "This link is not a supported local Wave project route.", request);
        if (request.EntityId is not null && !await _session.OpenAsync(request.EntityId, cancellationToken))
            return new(false, _session.ErrorCode ?? "ProjectNotFound", _session.Status, request);
        if (request.EntityId is null)
        {
            try { await _session.RefreshAsync(cancellationToken); }
            catch (WaveBrowserException error) { return new(false, error.Code, error.Message, request); }
        }
        return new(true, "Succeeded", "Local browser Wave editor opened.", request,
            ViewState: new(RouteId, "wave.local-editor", _session.Project?.Revision ?? 0,
                JsonSerializer.SerializeToElement(new { storage = "this-browser", projectId = _session.Project?.ProjectId })));
    }

    public BrowserCuiSurface Render(HomeFeatureViewState view)
    {
        if (view.RouteId != RouteId || view.ViewId != "wave.local-editor") throw new ArgumentException("Invalid Wave surface.");
        _view?.Dispose(); _view = new(_session);
        return new(_document, _session, _session, _view);
    }

    public void Dispose()
    {
        List<Exception>? errors = null;
        try { _view?.Dispose(); } catch (Exception error) { (errors ??= []).Add(error); }
        _view = null;
        // A failing external playback cleanup must not skip clearing this owner session.
        try { _session.Dispose(); } catch (Exception error) { (errors ??= []).Add(error); }
        if (errors is not null) throw new AggregateException("Wave teardown reported browser cleanup failures.", errors);
    }

    private sealed class WaveViewLifetime : IDisposable
    {
        private readonly WaveBrowserSession _session;
        private readonly DispatcherTimer _timer;
        private bool _polling;
        private bool _disposed;
        public WaveViewLifetime(WaveBrowserSession session)
        {
            _session = session; _timer = new() { Interval = TimeSpan.FromMilliseconds(250) };
            _timer.Tick += Poll; _timer.Start();
        }
        private async void Poll(object? sender, EventArgs args)
        {
            if (_polling || _disposed) return;
            _polling = true;
            try { await _session.PollTransportAsync(); }
            finally { _polling = false; }
        }
        public void Dispose()
        {
            if (_disposed) return; _disposed = true;
            _timer.Stop(); _timer.Tick -= Poll; _session.SuspendPlayback();
        }
    }
}
