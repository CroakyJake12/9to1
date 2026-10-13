using System.Runtime.Versioning;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Threading;
using CakeOS.Cui;
using Haven.Application;
using HavenOS.Apps.Spaces;
using HavenOS.Apps.Spaces.Rendering;
using HavenOS.Home.Core;
using NineToOne.Web.Spaces.Storage;
using NineToOne.Web.Spaces.Chat;
using HavenOS.Apps.Spaces.Chat;
using HavenOS.Apps.Spaces.Chat.Rendering;

namespace NineToOne.Web.Spaces;

/// <summary>Spaces product entry: canonical Chat by default, with explicit management
/// and custom-builder utilities. Every operation retains its real private service owner.</summary>
public sealed partial class BrowserSpacesFeature(Func<SpaceRegistry> registry, Action demandCurrent,
    Func<Action, Task> present, Action<HomeFeatureNavigationRequest> navigate,
    Func<IChatSpaceBackend>? chatBackend = null, Func<CancellationToken, Task<string?>>? selectFile = null)
    : IHomeFeatureRouteHandler, IBrowserPrivateContextParticipant, IBrowserCloseParticipant
{
    private readonly object _gate = new();
    private readonly Dictionary<string, IBrowserSpacesPresentationSession> _sessions = [];
    private readonly HashSet<Task> _factories = [];
    private Offer? _pending, _accepted;
    private bool _revoked;
    private long _generation;
    private Task? _close;
    public string RouteId => HomeFeatureRouteIds.Spaces;
    public bool HasUnsavedChanges { get { lock (_gate) return _sessions.Values.Any(session => session.HasUnsavedChanges); } }

    public Task<HomeFeatureNavigationResult> OpenAsync(HomeFeatureNavigationRequest request, CancellationToken cancellationToken = default)
    {
        var completion = new TaskCompletionSource<HomeFeatureNavigationResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_gate)
        {
            if (_revoked) return Task.FromResult(new HomeFeatureNavigationResult(false, "HomeServiceUnavailable", "Spaces is closed.", request));
            _factories.Add(completion.Task);
        }
        _ = SettleOpenAsync(request, cancellationToken, completion); return completion.Task;
    }
    private async Task SettleOpenAsync(HomeFeatureNavigationRequest request, CancellationToken token,
        TaskCompletionSource<HomeFeatureNavigationResult> completion)
    {
        Task<HomeFeatureNavigationResult>? source = null;
        try { source = OpenCoreAsync(request, token); completion.TrySetResult(await source); }
        catch (OperationCanceledException error) when (source?.IsCanceled == true) { completion.TrySetCanceled(error.CancellationToken); }
        catch (Exception error)
        {
            if (source?.Exception is { } failure) completion.TrySetException(failure.InnerExceptions);
            else completion.TrySetException(error);
        }
        finally { lock (_gate) if (completion.Task.IsCompletedSuccessfully) _factories.Remove(completion.Task); }
    }
    private async Task<HomeFeatureNavigationResult> OpenCoreAsync(HomeFeatureNavigationRequest request, CancellationToken token)
    {
        var emptyTarget = request.EntityType is null && request.EntityId is null;
        var chat = emptyTarget && request.Action is null;
        var manager = emptyTarget && request.Action == "manage";
        Guid builderId = default;
        var builder = request.EntityType == "Space" && request.Action == "edit" &&
            Guid.TryParseExact(request.EntityId, "D", out builderId) && builderId != Guid.Empty;
        if (request.RouteId != RouteId || request.ModelPickerTarget is not null || request.DeepLink is not null || !(chat || manager || builder))
            return new(false, "HomeFeatureTargetUnsupported", "This Space link is unsupported. Your link has been preserved.", request);
        IBrowserSpacesPresentationSession session;
        long generation;
        lock (_gate)
        {
            Demand(); generation = ++_generation; _pending?.Reject(); _pending = null;
            var key = chat ? "chat" : manager ? "manager" : builderId.ToString("D");
            if (!_sessions.TryGetValue(key, out session!))
            {
                session = chat
                    ? new BrowserChatSession(chatBackend ?? (() => throw new InvalidOperationException("Chat services have not been composed by this host.")),
                        demandCurrent, present, navigate, selectFile)
                    : manager
                    ? new BrowserSpacesSession(registry, demandCurrent, present,
                        id => navigate(new(RouteId, "Space", id.ToString("D"), "edit")))
                    : new BrowserSpaceBuilderSession(registry, demandCurrent, present, builderId);
                _sessions.Add(key, session);
            }
        }
        await session.PrepareAsync(token);
        token.ThrowIfCancellationRequested();
        lock (_gate)
        {
            Demand();
            if (_generation != generation) return new(false, "Cancelled", "A newer Spaces navigation superseded this view.", request);
            var state = new HomeFeatureViewState(RouteId, chat ? "spaces.chat" : manager ? "spaces.manager" : "spaces.builder", generation,
                JsonSerializer.SerializeToElement(new { generation }));
            _pending = new(this, session, state, token, manager);
            return new(true, chat ? "SpacesChatReady" : "SpacesConfigurationReady", chat ? "Chat is ready." : "Space configuration is ready.", request, ViewState: state);
        }
    }

    public BrowserCuiSurface Render(HomeFeatureViewState state)
    {
        lock (_gate)
        {
            Demand();
            var offer = _pending;
            if (offer is null || !ReferenceEquals(offer.State, state) || !offer.CanAccept)
                throw new InvalidOperationException("The prepared Spaces view has expired.");
            if (offer.Session is BrowserChatSession chatSession)
            {
                var chatDocument = SpacesShellCuiDocument.Load(includeOriginalGeneratedChildren: false);
                return new(chatDocument, chatSession, chatSession, offer,
                    ChatSpaceCuiRuntime.CreateRegistry(), offer,
                    root => { chatSession.AttachRoot(root); return SpacesShellCuiRuntime.Attach(root, chatSession.SetViewportWidth); });
            }
            var document = offer.Manager ? SpacesHomeCuiDocument.Load() : SpaceBuilderCuiDocument.Load();
            return new(document, offer.Session, offer.Session, offer, Admission: offer,
                Attach: offer.Session is BrowserSpacesSession managerSession
                    ? root => SpacesHomeCuiRuntime.AttachEditor(root, managerSession.SetViewportWidth) : null);
        }
    }
    private void Demand()
    {
        if (_revoked) throw new ObjectDisposedException(nameof(BrowserSpacesFeature));
        demandCurrent();
    }
    public Task<HomeCoreOperationResult<bool>> PrepareToCloseAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(HasUnsavedChanges
            ? new HomeCoreOperationResult<bool>(false, "SpacesUnsavedChanges", "Save or cancel your Space edits before closing.", false)
            : new(true, "Succeeded", "Spaces is ready to close.", true));
    }
    public void RevokePrivateContext()
    {
        lock (_gate)
        {
            _revoked = true; _generation++; _pending = null; _accepted = null;
            foreach (var session in _sessions.Values) session.RevokePrivateContext();
        }
    }
    public ValueTask DisposeAsync()
    {
        RevokePrivateContext();
        TaskCompletionSource? completion = null;
        IBrowserSpacesPresentationSession[] sessions; Task[] factories;
        lock (_gate)
        {
            if (_close is not null) return new(_close);
            completion = new(TaskCreationOptions.RunContinuationsAsynchronously); _close = completion.Task;
            sessions = _sessions.Values.ToArray(); factories = _factories.ToArray();
        }
        _ = DrainAsync(sessions, factories, completion); return new(completion.Task);
    }
    private static async Task DrainAsync(IBrowserSpacesPresentationSession[] sessions, Task[] factories, TaskCompletionSource completion)
    {
        try
        {
            await BrowserSpacesDrain.CompleteAsync(sessions.Select(session => (Func<Task>)(() => session.DisposeAsync().AsTask()))
                .Concat(factories.Select(task => (Func<Task>)(() => task))));
            completion.TrySetResult();
        }
        catch (Exception error) { completion.TrySetException(error); }
    }
    private sealed class Offer(BrowserSpacesFeature owner, IBrowserSpacesPresentationSession session,
        HomeFeatureViewState state, CancellationToken cancellation, bool manager) : IBrowserPresentationAdmission, IDisposable
    {
        private bool _rejected, _accepted;
        public IBrowserSpacesPresentationSession Session => session;
        public HomeFeatureViewState State => state;
        public bool Manager => manager;
        public bool CanAccept => !_rejected && !owner._revoked && owner._generation == state.Revision && !cancellation.IsCancellationRequested;
        public void Accept()
        {
            lock (owner._gate)
            {
                owner.Demand();
                if (!CanAccept) throw new InvalidOperationException("The Spaces presentation expired before installation.");
                if (owner._accepted is { } previous && !ReferenceEquals(previous.Session, session)) previous.Session.Deactivate();
                session.Admit(); owner._accepted = this; owner._pending = null; _accepted = true;
            }
        }
        public void Reject()
        {
            lock (owner._gate)
            {
                _rejected = true;
                if (ReferenceEquals(owner._pending, this)) owner._pending = null;
                if (ReferenceEquals(owner._accepted, this)) { owner._accepted = null; session.Deactivate(); }
            }
        }
        public void Dispose()
        {
            lock (owner._gate)
            {
                if (ReferenceEquals(owner._accepted, this)) { owner._accepted = null; session.Deactivate(); }
                if (!_accepted) Reject();
            }
        }
    }
}
