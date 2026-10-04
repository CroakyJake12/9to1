using System.ComponentModel;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CakeOS.Cui;
using CakeOS.Cui.Language;
using CakeOS.Cui.Runtime;
using HavenOS.Home.Core;
using NineToOne.Web.Media;

namespace NineToOne.Web.Picture;

public sealed class PictureBrowserFeature : IHomeFeatureRouteHandler, IBrowserCloseParticipant, IDisposable
{
    private readonly PictureBrowserSession _session;
    private readonly CuiDocument _document;
    private PictureViewLifetime? _view;
    public string RouteId => "app.picture";
    public bool HasUnsavedChanges => _session.IsDirty || _session.IsBusy;
    public Task<HomeCoreOperationResult<bool>> PrepareToCloseAsync(CancellationToken cancellationToken = default)
        => _session.PrepareToCloseAsync(cancellationToken);
    public PictureBrowserFeature(IPictureBrowserMedia media, string markup)
    {
        _session = new(media); var parser = new CuiRichParser(); _document = parser.Parse(markup, "Picture.cui");
        if (parser.Diagnostics.Diagnostics.Any(d => d.Severity == CuiDiagnosticSeverity.Error))
            throw new InvalidDataException("The browser Picture CUI is invalid.");
    }
    public static HomeCoreOperationResult<bool> Register(BrowserSurfaceRegistry registry)
    {
        using var stream = typeof(PictureBrowserFeature).Assembly.GetManifestResourceStream("NineToOne.Web.Picture.cui")
            ?? throw new InvalidOperationException("The Picture browser resource is unavailable.");
        using var reader = new StreamReader(stream);
        var feature = new PictureBrowserFeature(new BrowserPictureMedia(), reader.ReadToEnd());
        var result = registry.Register(feature, feature.Render, BrowserSurfaceScope.DeviceLocal);
        if (!result.Succeeded) feature.Dispose(); return result;
    }
    public async Task<HomeFeatureNavigationResult> OpenAsync(HomeFeatureNavigationRequest request, CancellationToken cancellationToken = default)
    {
        if (request.RouteId != RouteId || request.DeepLink is not null || request.Action is not null || request.ModelPickerTarget is not null
            || request.EntityType is not null && request.EntityType != "PictureDocument")
            return new(false, "InvalidArgument", "This link is not a supported local Picture document route.", request);
        if (request.EntityId is not null && !await _session.OpenAsync(request.EntityId, cancellationToken))
            return new(false, _session.ErrorCode ?? "DocumentNotFound", _session.Status, request);
        if (request.EntityId is null)
        {
            try { await _session.RefreshAsync(cancellationToken); }
            catch (PictureBrowserException error) { return new(false, error.Code, error.Message, request); }
        }
        return new(true, "Succeeded", "Local Picture editor opened.", request,
            ViewState: new(RouteId, "picture.local-raster", _session.Document?.Revision ?? 0,
                JsonSerializer.SerializeToElement(new { storage = "this-browser", documentId = _session.Document?.DocumentId })));
    }
    public BrowserCuiSurface Render(HomeFeatureViewState view)
    {
        if (view.RouteId != RouteId || view.ViewId != "picture.local-raster") throw new ArgumentException("Invalid Picture surface.");
        _view?.Dispose(); _view = new(_session);
        var registry = new CuiControlRegistry(); registry.RegisterObjectRenderer("PictureRasterPreview", _ => _view.Control);
        return new(_document, _session, _session, _view, registry);
    }
    public void Dispose()
    {
        var failures = new List<Exception>();
        try { _view?.Dispose(); } catch (Exception error) { failures.Add(error); }
        finally { _view = null; }
        try { _session.Dispose(); } catch (Exception error) { failures.Add(error); }
        if (failures.Count > 0) throw new AggregateException("Picture cleanup failed after retained document state was cleared.", failures);
    }

    private sealed class PictureViewLifetime : IDisposable
    {
        private readonly PictureBrowserSession _session;
        private Bitmap? _bitmap;
        private HavenOS.Images.PictureDocument? _renderedDocument;
        private bool _renderedOriginal, _disposed;
        public Image Control { get; } = new() { Stretch = Stretch.Uniform, MaxHeight = 520, MinHeight = 120 };
        public PictureViewLifetime(PictureBrowserSession session)
        { _session = session; session.PropertyChanged += Changed; Refresh(); }
        private void Changed(object? sender, PropertyChangedEventArgs args)
        { if (Dispatcher.UIThread.CheckAccess()) Refresh(); else Dispatcher.UIThread.Post(Refresh); }
        private void Refresh()
        {
            if (_disposed || _session.IsBusy || ReferenceEquals(_renderedDocument, _session.Document) && _renderedOriginal == _session.ShowOriginal) return;
            // Native bitmap comes directly from the canonical Picture renderer. No JS canvas or derived pixel authority.
            var replacement = _session.RenderPreview();
            var previous = _bitmap; _bitmap = replacement; Control.Source = replacement;
            _renderedDocument = _session.Document; _renderedOriginal = _session.ShowOriginal;
            previous?.Dispose();
        }
        public void Dispose()
        {
            if (_disposed) return; _disposed = true; _session.PropertyChanged -= Changed;
            Control.Source = null; _bitmap?.Dispose(); _bitmap = null; _renderedDocument = null;
        }
    }
}
