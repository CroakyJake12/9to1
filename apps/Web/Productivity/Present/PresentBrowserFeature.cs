using System.Text.Json;
using CakeOS.Cui;
using CakeOS.Cui.Language;
using Haven.Application;
using HavenOS.Home.Core;

namespace NineToOne.Web.Productivity.Present;

/// <summary>Device-local route over actual Present sessions and one injected canonical repository.</summary>
public sealed class PresentBrowserFeature : IHomeFeatureRouteHandler, IBrowserCloseParticipant, IAsyncDisposable
{
    private readonly IPresentRepository _repository;
    private readonly Func<Exception, bool>? _unknown;
    private readonly CuiDocument _document;
    private readonly Func<bool> _reduceMotion;
    private readonly HashSet<TrackedView> _views = [];
    private readonly List<PresentBrowserSession> _retired = [];
    private PresentBrowserSession _session;
    private PresentationOffer? _offer;
    private Task _retirement = Task.CompletedTask;
    private long _presentationGeneration;
    private bool _disposed;
    public string RouteId => "app.present";
    public Exception? LastRetirementError { get; private set; }
    public bool HasUnsavedChanges => NeedsPreparation(_session) || _retired.Any(NeedsPreparation) || !_retirement.IsCompletedSuccessfully;
    private static bool NeedsPreparation(PresentBrowserSession session) => session.IsDirty || session.IsBusy || session.HasUnknownCommitOutcome;

    public PresentBrowserFeature(IPresentRepository authorisedRepository, Func<bool> reduceMotion, string markup,
        Func<Exception, bool>? isUnknownCommitOutcome = null)
    {
        _repository = authorisedRepository ?? throw new ArgumentNullException(nameof(authorisedRepository));
        _unknown = isUnknownCommitOutcome; _session = NewSession();
        _reduceMotion = reduceMotion ?? throw new ArgumentNullException(nameof(reduceMotion));
        var parser = new CuiRichParser(); _document = parser.Parse(markup, "Present.cui");
        if (parser.Diagnostics.Diagnostics.Any(diagnostic => diagnostic.Severity == CuiDiagnosticSeverity.Error))
            throw new InvalidDataException("The browser Present CUI document is invalid.");
    }
    private PresentBrowserSession NewSession() => new(_repository, _unknown);

    public static HomeCoreOperationResult<bool> Register(BrowserSurfaceRegistry registry, IPresentRepository authorisedRepository,
        Func<bool> reduceMotion, Func<Exception, bool>? isUnknownCommitOutcome = null)
    {
        using var stream = typeof(PresentBrowserFeature).Assembly.GetManifestResourceStream("NineToOne.Web.Present.cui")
            ?? throw new InvalidOperationException("The Present browser CUI resource is unavailable.");
        using var reader = new StreamReader(stream);
        var feature = new PresentBrowserFeature(authorisedRepository, reduceMotion, reader.ReadToEnd(), isUnknownCommitOutcome);
        return registry.Register(feature, feature.Render, BrowserSurfaceScope.DeviceLocal);
    }

    public async Task<HomeFeatureNavigationResult> OpenAsync(HomeFeatureNavigationRequest request, CancellationToken cancellationToken = default)
    {
        if (_disposed || request.RouteId != RouteId || request.Action is not (null or "open") || request.ModelPickerTarget is not null ||
            request.DeepLink is not null || request.EntityType is not (null or "PresentDocument"))
            return new(false, "InvalidArgument", "This link is not a supported local Present document route.", request);
        Guid? target = null;
        if (request.EntityId is not null)
        {
            if (request.EntityType != "PresentDocument" || !Guid.TryParse(request.EntityId, out var id) || id == Guid.Empty)
                return new(false, "InvalidArgument", "Choose a valid saved Present document.", request);
            target = id;
        }
        var generation = ++_presentationGeneration;
        _offer?.Reject(); _offer = null;
        var prepared = await PrepareToCloseAsync(cancellationToken);
        if (!prepared.Succeeded) return new(false, prepared.Code, prepared.Message, request);
        var previous = _session;
        var fence = new SessionFence(previous.DocumentId, previous.DurableRevision, previous.EditGeneration);
        var candidate = target is null ? previous : NewSession();
        if (!ReferenceEquals(candidate, previous)) candidate.PrepareUnadmittedPresentation();
        try
        {
            // List admission completes before any target is attached; the visible
            // session is never the target's temporary loading session.
            var refreshed = await candidate.RefreshAsync(cancellationToken);
            if (!refreshed.Succeeded) return new(false, refreshed.Code, refreshed.Message, request);
            if (target is { } targetId)
            {
                var opened = await candidate.OpenAsync(targetId, token: cancellationToken);
                if (!opened.Succeeded) return new(false, opened.Code, opened.Message, request);
            }
            cancellationToken.ThrowIfCancellationRequested();
            if (generation != _presentationGeneration || !ReferenceEquals(previous, _session))
                return new(false, "Cancelled", "A newer Present navigation superseded this prepared view.", request);
            var state = new HomeFeatureViewState(RouteId, "present.browser-editor", candidate.DurableRevision,
                JsonSerializer.SerializeToElement(new { documentId = candidate.DocumentId, storage = "this-browser" }));
            _offer = new(this, previous, candidate, fence, generation, state, cancellationToken);
            return new(true, "Succeeded", "Local browser Present prepared.", request, ViewState: state);
        }
        finally
        {
            if (!ReferenceEquals(candidate, previous) && !ReferenceEquals(_offer?.Candidate, candidate))
                candidate.AbandonUnadmittedPresentation();
        }
    }

    public BrowserCuiSurface Render(HomeFeatureViewState state)
    {
        var offer = _offer;
        if (_disposed || state.RouteId != RouteId || state.ViewId != "present.browser-editor" || offer is null ||
            !ReferenceEquals(offer.State, state) || !offer.CanAccept)
            throw new InvalidOperationException("The prepared Present view is no longer valid.");
        var surface = PresentBrowserSurface.Create(_document, offer.Candidate, _reduceMotion);
        if (surface.Lifetime is { } lifetime)
        {
            var tracked = new TrackedView(this, lifetime);
            _views.Add(tracked); surface = surface with { Lifetime = tracked };
        }
        // Root owns old UI retirement after accepted handoff. Never dispose the
        // currently displayed retained editor merely because a candidate exists.
        return surface with { Admission = offer };
    }

    public async Task<HomeCoreOperationResult<bool>> PrepareToCloseAsync(CancellationToken cancellationToken = default)
    {
        try { await _retirement.WaitAsync(cancellationToken); }
        catch (OperationCanceledException) { throw; }
        catch (Exception error)
        {
            LastRetirementError = error;
            return new(false, "PresentationRetirementFailed", "A previous Present session could not close safely; its lifetime is retained.");
        }
        return await _session.PrepareToCloseAsync(cancellationToken);
    }
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        var prepared = await PrepareToCloseAsync();
        if (!prepared.Succeeded) throw new InvalidOperationException(prepared.Message, LastRetirementError);
        _offer?.Reject(); _offer = null;
        var closed = await _session.CloseAsync();
        if (!closed.Succeeded) throw new InvalidOperationException(closed.Message);
        await _session.DisposeAsync();
        foreach (var view in _views.ToArray()) view.Dispose();
        _views.Clear(); _disposed = true;
    }
    private async Task RetireAfterTransfer(PresentationOffer offer)
    {
        // Allow the root's synchronous CUI/view handoff to complete (or call
        // Reject to roll back) before closing the previously saved session.
        await Task.Yield();
        if (!offer.Accepted || ReferenceEquals(offer.Previous, offer.Candidate)) return;
        _retired.Add(offer.Previous);
        try
        {
            var closed = await offer.Previous.CloseAsync();
            if (!closed.Succeeded) throw new InvalidOperationException(closed.Message);
            await offer.Previous.DisposeAsync(); _retired.Remove(offer.Previous);
        }
        catch (Exception error) { LastRetirementError = error; throw; }
    }
    private sealed record SessionFence(Guid? Id, long Revision, long Generation);
    private sealed class TrackedView(PresentBrowserFeature owner, IDisposable lifetime) : IDisposable
    {
        private bool _disposed;
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try { lifetime.Dispose(); }
            finally { owner._views.Remove(this); }
        }
    }
    private sealed class PresentationOffer(PresentBrowserFeature owner, PresentBrowserSession previous,
        PresentBrowserSession candidate, SessionFence fence, long generation, HomeFeatureViewState state, CancellationToken cancellation)
        : IBrowserPresentationAdmission
    {
        private bool _rejected;
        private readonly SessionFence _candidateFence = new(candidate.DocumentId, candidate.DurableRevision, candidate.EditGeneration);
        public PresentBrowserSession Previous { get; } = previous;
        public PresentBrowserSession Candidate { get; } = candidate;
        public HomeFeatureViewState State { get; } = state;
        public bool Accepted { get; private set; }
        public bool CanAccept => !_rejected && !Accepted && !owner._disposed && !cancellation.IsCancellationRequested &&
            generation == owner._presentationGeneration && ReferenceEquals(owner._session, Previous) &&
            Previous.DocumentId == fence.Id && Previous.DurableRevision == fence.Revision && Previous.EditGeneration == fence.Generation &&
            !NeedsPreparation(Previous) && !Candidate.IsBusy && Candidate.DocumentId == _candidateFence.Id &&
            Candidate.DurableRevision == _candidateFence.Revision && Candidate.EditGeneration == _candidateFence.Generation;
        public void Accept()
        {
            if (!CanAccept) throw new InvalidOperationException("Present changed while loading; its current draft is preserved.");
            if (!ReferenceEquals(Candidate, Previous)) Candidate.AdmitPresentation();
            owner._session = Candidate; Accepted = true; owner._offer = null;
            owner._retirement = Task.WhenAll(owner._retirement, owner.RetireAfterTransfer(this));
        }
        public void Reject()
        {
            if (_rejected) return;
            if (Accepted)
            {
                if (!ReferenceEquals(owner._session, Candidate) || Previous.DocumentId != fence.Id || Previous.DurableRevision != fence.Revision ||
                    Previous.EditGeneration != fence.Generation)
                    throw new InvalidOperationException("An already transferred or changed Present session cannot be rolled back as an unaccepted view.");
                if (!ReferenceEquals(Candidate, Previous))
                    Candidate.WithdrawUntransferredPresentation(_candidateFence.Id, _candidateFence.Revision, _candidateFence.Generation);
                owner._session = Previous; Accepted = false;
            }
            if (!ReferenceEquals(Candidate, Previous)) Candidate.AbandonUnadmittedPresentation();
            _rejected = true;
            if (ReferenceEquals(owner._offer, this)) owner._offer = null;
        }
    }
}
