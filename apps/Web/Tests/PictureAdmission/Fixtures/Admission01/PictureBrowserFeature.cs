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

/// <summary>Prepare an independent canonical raster session; transfer only after native admission.</summary>
public sealed class PictureBrowserFeature : IHomeFeatureRouteHandler, IBrowserCloseParticipant, IDisposable
{
    private readonly IPictureBrowserMedia _media;
    private readonly CuiDocument _document;
    private readonly HashSet<PictureViewLifetime> _views = [];
    private readonly HashSet<PictureBrowserSession> _preparingCandidates = [];
    private int _preparations;
    private PictureBrowserSession _session;
    private PresentationOffer? _offer;
    private Task _retirement = Task.CompletedTask;
    private long _generation;
    private bool _disposed;
    public string RouteId => "app.picture";
    public Exception? LastRetirementError { get; private set; }
    public bool HasUnsavedChanges => _session.IsDirty || _session.IsBusy || _session.HasPendingInput || _preparations != 0 || !_retirement.IsCompletedSuccessfully;
    public PictureBrowserFeature(IPictureBrowserMedia media, string markup)
    {
        _media = media ?? throw new ArgumentNullException(nameof(media)); _session = new(media);
        var parser = new CuiRichParser(); _document = parser.Parse(markup, "Picture.cui");
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
        if (_disposed || request.RouteId != RouteId || request.DeepLink is not null || request.Action is not null || request.ModelPickerTarget is not null
            || request.EntityType is not null && request.EntityType != "PictureDocument"
            || request.EntityId is not null && (!Guid.TryParse(request.EntityId, out var id) || id == Guid.Empty))
            return new(false, "InvalidArgument", "This link is not a supported local Picture document route.", request);
        var generation = ++_generation; _offer?.Reject(); _offer = null;
        var prepared = await PrepareToCloseAsync(cancellationToken);
        if (!prepared.Succeeded || prepared.Value != true) return new(false, prepared.Code, prepared.Message, request);
        if (_disposed || generation != _generation) return new(false, "Cancelled", "A newer Picture route superseded this request.", request);
        var previous = _session; var fence = Fence.Capture(previous);
        // An app-root presentation keeps its actual current session and original bytes.
        var candidate = request.EntityId is null ? previous : new PictureBrowserSession(_media);
        if (!ReferenceEquals(previous, candidate)) { candidate.PrepareUnadmittedPresentation(); _preparingCandidates.Add(candidate); }
        ++_preparations;
        try
        {
            if (ReferenceEquals(previous, candidate)) await candidate.RefreshAsync(cancellationToken);
            else
            {
                var opened = await candidate.PrepareCandidateAsync(request.EntityId!, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                if (!opened) return new(false, candidate.ErrorCode ?? "DocumentNotFound", candidate.Status, request);
            }
            cancellationToken.ThrowIfCancellationRequested();
            // Refreshing the same visible metadata is complete before capturing its final fence.
            if (ReferenceEquals(previous, candidate)) fence = Fence.Capture(previous);
            if (_disposed || generation != _generation || !ReferenceEquals(previous, _session) || !fence.Matches(previous))
                return new(false, "Cancelled", "Picture changed while preparing this view; its current document is retained.", request);
            var state = new HomeFeatureViewState(RouteId, "picture.local-raster", candidate.Document?.Revision ?? 0,
                JsonSerializer.SerializeToElement(new { storage = "this-browser", documentId = candidate.Document?.DocumentId }));
            _offer = new(this, previous, candidate, fence, generation, state, cancellationToken);
            return new(true, "Succeeded", "Local Picture editor prepared.", request, ViewState: state);
        }
        finally
        {
            --_preparations; _preparingCandidates.Remove(candidate);
            if (!ReferenceEquals(previous, candidate) && !ReferenceEquals(_offer?.Candidate, candidate)) candidate.Dispose();
        }
    }
    public BrowserCuiSurface Render(HomeFeatureViewState state)
    {
        var offer = _offer;
        if (offer is null || !ReferenceEquals(state, offer.State) || state.RouteId != RouteId || state.ViewId != "picture.local-raster" || !offer.CanAccept)
            throw new InvalidOperationException("The prepared Picture view is no longer current.");
        var view = new PictureViewLifetime(this, offer.Candidate); _views.Add(view);
        var registry = new CuiControlRegistry(); registry.RegisterObjectRenderer("PictureRasterPreview", _ => view.Control);
        // Previous view disposal belongs to the shell's successful retained-CUI handoff.
        return new(_document, offer.Candidate, offer.Candidate, view, registry, offer);
    }
    public async Task<HomeCoreOperationResult<bool>> PrepareToCloseAsync(CancellationToken cancellationToken = default)
    {
        if (_preparations != 0) return new(false, "OperationBusy", "A Picture presentation is still preparing. Its current document and input are retained.", false);
        try { await _retirement.WaitAsync(cancellationToken); }
        catch (OperationCanceledException) { throw; }
        catch (Exception error)
        {
            LastRetirementError = error;
            return new(false, "PresentationRetirementFailed", "A prior Picture session did not retire safely.", false);
        }
        return await _session.PrepareToCloseAsync(cancellationToken);
    }
    private async Task RetireAfterTransfer(PresentationOffer offer)
    {
        await Task.Yield(); // Let the shell finish its reversible native handoff first.
        if (!offer.Accepted || ReferenceEquals(offer.Previous, offer.Candidate)) return;
        try { offer.Previous.Dispose(); }
        catch (Exception error) { LastRetirementError = error; throw; }
    }
    public void Dispose()
    {
        if (_disposed) return; _disposed = true; ++_generation;
        var errors = new List<Exception>();
        try { _offer?.Reject(); } catch (Exception error) { errors.Add(error); }
        finally { _offer = null; }
        foreach (var candidate in _preparingCandidates.ToArray()) try { candidate.Dispose(); } catch (Exception error) { errors.Add(error); }
        _preparingCandidates.Clear();
        foreach (var view in _views.ToArray()) try { view.Dispose(); } catch (Exception error) { errors.Add(error); }
        try { _session.Dispose(); } catch (Exception error) { errors.Add(error); }
        if (errors.Count > 0) throw new AggregateException("Picture cleanup attempted every owned lifetime.", errors);
    }
    private sealed record Fence(Guid? Id, long Revision, long SavedRevision, long Generation)
    {
        public static Fence Capture(PictureBrowserSession session) => new(session.Document?.DocumentId, session.Document?.Revision ?? -1, session.SavedRevision, session.StateGeneration);
        public bool Matches(PictureBrowserSession session) => session.Document?.DocumentId == Id && (session.Document?.Revision ?? -1) == Revision
            && session.SavedRevision == SavedRevision && session.StateGeneration == Generation && !session.IsBusy && !session.IsDirty && !session.HasPendingInput;
    }
    private sealed class PresentationOffer(PictureBrowserFeature owner, PictureBrowserSession previous, PictureBrowserSession candidate,
        Fence fence, long generation, HomeFeatureViewState state, CancellationToken token) : IBrowserPresentationAdmission
    {
        private readonly Fence _candidateFence = Fence.Capture(candidate);
        private bool _rejected;
        public PictureBrowserSession Previous { get; } = previous;
        public PictureBrowserSession Candidate { get; } = candidate;
        public HomeFeatureViewState State { get; } = state;
        public bool Accepted { get; private set; }
        public bool CanAccept => !_rejected && !Accepted && !owner._disposed && !token.IsCancellationRequested
            && owner._generation == generation && ReferenceEquals(owner._offer, this) && ReferenceEquals(owner._session, Previous)
            && fence.Matches(Previous) && _candidateFence.Matches(Candidate);
        public void Accept()
        {
            if (!CanAccept) throw new InvalidOperationException("Picture changed while loading; its current document and typed input are retained.");
            if (!ReferenceEquals(Previous, Candidate))
            {
                Candidate.AdmitPresentation(); // Real original source cache; no IDB/model revision mutation.
                Previous.DetachPresentation();
            }
            owner._session = Candidate; Accepted = true; owner._offer = null;
            owner._retirement = Task.WhenAll(owner._retirement, owner.RetireAfterTransfer(this));
        }
        public void Reject()
        {
            if (_rejected) return;
            if (Accepted)
            {
                if (!ReferenceEquals(owner._session, Candidate) || !fence.Matches(Previous) || !_candidateFence.Matches(Candidate))
                    throw new InvalidOperationException("Changed or retired Picture sessions cannot roll back an admitted presentation.");
                if (!ReferenceEquals(Previous, Candidate)) { Candidate.DetachPresentation(); Previous.AdmitPresentation(); }
                owner._session = Previous; Accepted = false;
            }
            if (!ReferenceEquals(Previous, Candidate)) Candidate.Dispose();
            _rejected = true;
            if (ReferenceEquals(owner._offer, this)) owner._offer = null;
        }
    }
    private sealed class PictureViewLifetime : IDisposable
    {
        private readonly PictureBrowserFeature _owner;
        private readonly PictureBrowserSession _session;
        private Bitmap? _bitmap;
        private HavenOS.Images.PictureDocument? _renderedDocument;
        private bool _renderedOriginal, _disposed;
        public Image Control { get; } = new() { Stretch = Stretch.Uniform, MaxHeight = 520, MinHeight = 120 };
        public PictureViewLifetime(PictureBrowserFeature owner, PictureBrowserSession session)
        {
            _owner = owner; _session = session;
            // Build the actual Bitmap before subscribing; a failed candidate never leaks an observer.
            Refresh(); session.PropertyChanged += Changed;
        }
        private void Changed(object? sender, PropertyChangedEventArgs args)
        { if (Dispatcher.UIThread.CheckAccess()) Refresh(); else Dispatcher.UIThread.Post(Refresh); }
        private void Refresh()
        {
            if (_disposed || _session.IsBusy || ReferenceEquals(_renderedDocument, _session.Document) && _renderedOriginal == _session.ShowOriginal) return;
            var replacement = _session.RenderPreview();
            var previous = _bitmap; _bitmap = replacement; Control.Source = replacement;
            _renderedDocument = _session.Document; _renderedOriginal = _session.ShowOriginal;
            previous?.Dispose();
        }
        public void Dispose()
        {
            if (_disposed) return; _disposed = true; _session.PropertyChanged -= Changed;
            try { Control.Source = null; }
            finally { try { _bitmap?.Dispose(); } finally { _bitmap = null; _owner._views.Remove(this); } }
        }
    }
}
