using Avalonia.Controls;
using Avalonia.Threading;
using CakeOS.Cui.Runtime;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.Events;
using Haven.Desktop.ViewModels;
using Haven.Desktop.Views.Pages.Mesh;
using HavenOS.Apps.Browse;
using HavenOS.Home.Core;
using Microsoft.Extensions.DependencyInjection;

namespace Haven.Desktop.Services;

/// <summary>One tab's SAME MeshPage/editor/repository, not another Mesh implementation.
/// The page owns its writes and native children; the tab borrows global Home and services.</summary>
internal sealed class MeshBrowseAppContentSession : IBrowseNativeAppContentSession
{
    private readonly BrowseAppContentRequest _request;
    private readonly IServiceProvider _provider;
    private readonly HomeNativeWindowsComposition _home;
    private readonly CancellationToken _appLifetime, _hostLifetime, _token;
    private readonly CancellationTokenSource _lifetime;
    private readonly object _gate = new();
    private readonly List<Task> _preparations = [], _homeObservations = [];
    private Task? _initialization, _close, _initializationWork, _closeWork, _actualPageInitialization, _actualPageClose;
    private Task<Task>? _initializationDispatch, _closeDispatch;
    private Task<bool>? _preparation;
    private MeshPage? _page;
    private object? _preparedFence;
    private AuthenticatedResourceActor? _actor;
    private CuiSceneAvailability? _homeObservation;
    private volatile bool _published, _closing, _closed;

    internal MeshBrowseAppContentSession(BrowseAppContentRequest request, IServiceProvider provider,
        HomeNativeWindowsComposition home, CancellationToken appLifetime, CancellationToken hostLifetime,
        CancellationTokenSource lifetime)
    {
        _request = request; _provider = provider; _home = home;
        _appLifetime = appLifetime; _hostLifetime = hostLifetime; _lifetime = lifetime; _token = lifetime.Token;
    }
    public AppContentAddress Address => _request.Address;
    public string Title => "Mesh";
    public string IconKey => "mesh";
    public event EventHandler? StateChanged;
    public Task? OriginalInitialization { get { lock (_gate) return _initialization; } }
    public Task? OriginalClose { get { lock (_gate) return _close; } }
    public bool IsPublicationCurrent => _published && CanAdmitPageSource() && _page is not null &&
        OriginalInitialization is { IsCompletedSuccessfully: true };
    public bool CanRetireOriginalView => OriginalClose is { IsCompletedSuccessfully: true } && _closed;
    public Control OriginalNativeView
    {
        get
        {
            Dispatcher.UIThread.VerifyAccess();
            if (!IsPublicationCurrent) throw new InvalidOperationException("The original native Mesh workspace has not completed admission.");
            DemandPublicationCurrent();
            return _page!;
        }
    }
    private CloudflareOriginalTaskLedger Ledger()
    { var originals = new CloudflareOriginalTaskLedger(); originals.BindOriginalOwner(this); return originals; }
    private void RunWithinOriginalSource(Action source) =>
        CloudflareOriginalExecutionGuard.InvokeOriginal(this, () => { source(); return true; });

    // New page work is gated; already admitted page saves keep their original caller token and drain normally.
    private bool CanAdmitPageSource() => !_closed && !_closing && !_token.IsCancellationRequested &&
        !_appLifetime.IsCancellationRequested && !_hostLifetime.IsCancellationRequested &&
        _actor?.ProfileId == _request.ProfileId && _homeObservation is { State: CuiSceneAvailabilityState.Ready } &&
        _home.OriginalStartTask is { IsCompletedSuccessfully: true } && _home.OriginalCloseTask is null &&
        _home.OriginalProcessRetirementRequestTask is null;
    private void DemandAlive()
    {
        ObjectDisposedException.ThrowIf(_closed || _closing, this);
        _appLifetime.ThrowIfCancellationRequested(); _hostLifetime.ThrowIfCancellationRequested(); _token.ThrowIfCancellationRequested();
    }
    private void DemandPublicationCurrent()
    {
        DemandAlive();
        if (!CanAdmitPageSource() || _homeObservation is not { } observed ||
            WindowsHomeSameProcessRuntimeObservation.RevalidateBeforePublication(_provider, _home, observed).State != CuiSceneAvailabilityState.Ready)
            throw new UnauthorizedAccessException("The original Mesh Home runtime/profile observation is unavailable.");
        DemandAlive();
    }
    private void RetainHomeSource(Task actual)
    {
        lock (_gate)
        {
            _homeObservations.RemoveAll(task => task.IsCompletedSuccessfully);
            if (_homeObservations.Any(task => ReferenceEquals(task, actual))) return;
            if (_homeObservations.Count >= 128) throw new InvalidOperationException("Settle retained Mesh Home observations before further work.");
            _homeObservations.Add(actual);
        }
    }
    private async Task RevalidateHomeAsync(CloudflareOriginalTaskLedger originals, CancellationToken token)
    {
        try
        {
            void Source(Action body) => originals.Invoke(() => { DemandAlive(); token.ThrowIfCancellationRequested(); body(); return true; });
            async Task<AuthenticatedResourceActor> ReadActor()
            {
                var actual = originals.Invoke(() => _home.Profiles.GetCurrentAsync(Source,
                    task => { originals.Track(task); RetainHomeSource(task); }, token).AsTask());
                RetainHomeSource(actual);
                var actor = await originals.AwaitAsync(actual) ?? throw new UnauthorizedAccessException("The actual Mesh Home profile is unavailable.");
                DemandAlive(); token.ThrowIfCancellationRequested();
                if (actor.ProfileId != _request.ProfileId || actor.AccountId is not null || actor.OrganisationId is not null ||
                    string.IsNullOrWhiteSpace(actor.ActorId) || string.IsNullOrWhiteSpace(actor.AuthenticationRevision) ||
                    _actor is not null && _actor != actor)
                    throw new UnauthorizedAccessException("Mesh must retain the same genuine OS-local Home profile.");
                return actor;
            }
            DemandAlive(); token.ThrowIfCancellationRequested();
            var actor = await ReadActor();
            var observation = originals.Invoke(() => WindowsHomeSameProcessRuntimeObservation.CheckAsync(_provider, _home,
                _appLifetime, _hostLifetime, Source, DemandAlive, token));
            RetainHomeSource(observation);
            var observed = await originals.AwaitAsync(observation);
            if (await ReadActor() != actor) throw new UnauthorizedAccessException("The Mesh Home profile changed before publication.");
            observed = originals.Invoke(() => WindowsHomeSameProcessRuntimeObservation.RevalidateBeforePublication(_provider, _home, observed));
            if (observed.State != CuiSceneAvailabilityState.Ready) throw new InvalidOperationException(observed.Message);
            DemandAlive(); token.ThrowIfCancellationRequested();
            _actor = actor; _homeObservation = observed;
        }
        catch
        {
            // A failed refresh withdraws old Ready evidence; an in-flight check does not discard valid user input.
            _homeObservation = null;
            throw;
        }
    }

    public Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (_initialization is not null) return _initialization;
            DemandAlive();
            if (_close is not null) throw new InvalidOperationException("The original Mesh retirement already exists.");
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _initialization = InitializeDriverAsync(start.Task, cancellationToken); start.SetResult(); return _initialization;
        }
    }
    private async Task InitializeDriverAsync(Task start, CancellationToken caller)
    {
        await start.ConfigureAwait(false);
        var originals = Ledger();
        await originals.RunToOriginalSettlementAsync(async () =>
        {
            _initializationDispatch = originals.Invoke(() => Dispatcher.UIThread.InvokeAsync<Task>(() => InitializeOnUiAsync(caller)).GetTask());
            _initializationWork = await originals.AwaitAsync(_initializationDispatch).ConfigureAwait(false);
            await originals.AwaitAsync(_initializationWork).ConfigureAwait(false);
        }).ConfigureAwait(false);
    }
    private async Task InitializeOnUiAsync(CancellationToken caller)
    {
        Dispatcher.UIThread.VerifyAccess();
        using var phase = CloudflareOriginalExecutionGuard.EnterOriginal(this);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(caller, _token);
        var originals = Ledger();
        await originals.RunToOriginalSettlementAsync(async () =>
        {
            await RevalidateHomeAsync(originals, linked.Token);
            originals.Invoke(() =>
            {
                _ = new MeshPage(new MeshPageViewModel(_provider.GetRequiredService<MeshCoordinator>()),
                    captureOriginalOwner: page =>
                    {
                        _page = page;
                        page.BindOriginalBrowsePresentation(RunWithinOriginalSource, CanAdmitPageSource);
                    });
                return true;
            });
            var original = _page ?? throw new InvalidOperationException("The actual Mesh page was not retained.");
            _actualPageInitialization = originals.Invoke(() => original.InitializeAsync(linked.Token));
            await originals.AwaitAsync(_actualPageInitialization);
            originals.Invoke(() => { original.DemandOriginalInitializedDocument(); return true; });
            await RevalidateHomeAsync(originals, linked.Token);
            DemandPublicationCurrent(); linked.Token.ThrowIfCancellationRequested();
            originals.Invoke(() =>
            {
                original.PublishOriginalDocumentTab(() =>
                {
                    _published = true;
                    try { StateChanged?.Invoke(this, EventArgs.Empty); }
                    catch { _published = false; throw; }
                });
                return true;
            });
            DemandPublicationCurrent();
        });
    }

    // Legacy bool preparation is intentional: this does not advertise a reversible/frozen edit lease.
    public Task<bool> TryPrepareToCloseAsync(CancellationToken cancellationToken = default)
    {
        CloudflareOriginalExecutionGuard.DemandExternalJoin(this);
        lock (_gate)
        {
            if (_close is not null || _closing || _closed) return Task.FromResult(false);
            if (_preparation is { IsCompleted: false }) return _preparation;
            _preparations.RemoveAll(task => task.IsCompletedSuccessfully);
            if (_preparations.Count >= 128) throw new InvalidOperationException("Settle retained Mesh preparations before further work.");
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _preparation = PrepareDriverAsync(start.Task, cancellationToken); _preparations.Add(_preparation); start.SetResult(); return _preparation;
        }
    }
    private async Task<bool> PrepareDriverAsync(Task start, CancellationToken caller)
    {
        await start.ConfigureAwait(false);
        var originals = Ledger();
        return await originals.RunToOriginalSettlementAsync(async () =>
        {
            var dispatch = originals.Invoke(() => Dispatcher.UIThread.InvokeAsync<Task<bool>>(() => PrepareOnUiAsync(caller)).GetTask());
            var actual = await originals.AwaitAsync(dispatch).ConfigureAwait(false);
            return await originals.AwaitAsync(actual).ConfigureAwait(false);
        }).ConfigureAwait(false);
    }
    private async Task<bool> PrepareOnUiAsync(CancellationToken caller)
    {
        using var phase = CloudflareOriginalExecutionGuard.EnterOriginal(this);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(caller, _token);
        var originals = Ledger();
        return await originals.RunToOriginalSettlementAsync(async () =>
        {
            _preparedFence = null;
            if (_initialization is null) return false;
            await originals.AwaitAsync(_initialization);
            DemandAlive(); linked.Token.ThrowIfCancellationRequested();
            await RevalidateHomeAsync(originals, linked.Token);
            var page = _page ?? throw new InvalidOperationException("The original Mesh page is absent.");
            var actual = originals.Invoke(() => page.PrepareToCloseAsync("Finish operations before closing Mesh tab", linked.Token));
            if (!await originals.AwaitAsync(actual)) return false;
            await RevalidateHomeAsync(originals, linked.Token);
            DemandPublicationCurrent(); linked.Token.ThrowIfCancellationRequested();
            _preparedFence = originals.Invoke(page.CaptureOriginalBrowseCloseFence);
            return _preparedFence is not null;
        });
    }

    public Task CloseAndDrainAsync()
    {
        CloudflareOriginalExecutionGuard.DemandExternalJoin(this);
        lock (_gate)
        {
            if (_close is not null) return _close;
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _close = CloseDriverAsync(start.Task); start.SetResult(); return _close;
        }
    }
    private async Task CloseDriverAsync(Task start)
    {
        await start.ConfigureAwait(false);
        var originals = Ledger();
        await originals.RunToOriginalSettlementAsync(async () =>
        {
            if (_initialization is { IsCompleted: false })
            {
                _closing = true; _published = false;
                var cancellation = originals.Invoke(_lifetime.CancelAsync);
                await originals.AwaitAsync(cancellation).ConfigureAwait(false);
                var retirement = originals.Invoke(() => Dispatcher.UIThread.InvokeAsync(() => _page?.RequestRetirement()).GetTask());
                await originals.AwaitAsync(retirement).ConfigureAwait(false);
            }
            Task[] pending;
            lock (_gate) pending = new[] { _initialization }.OfType<Task>().Concat(_preparations).Distinct().ToArray();
            foreach (var actual in pending)
                try { await originals.AwaitAsync(actual).ConfigureAwait(false); }
                catch (Exception error) { originals.Capture(actual, error); }
            _closeDispatch = originals.Invoke(() => Dispatcher.UIThread.InvokeAsync<Task>(() => CloseOnUiAsync()).GetTask());
            _closeWork = await originals.AwaitAsync(_closeDispatch).ConfigureAwait(false);
            await originals.AwaitAsync(_closeWork).ConfigureAwait(false);
            Task[] observations; lock (_gate) observations = _homeObservations.ToArray();
            foreach (var actual in observations)
                try { await originals.AwaitAsync(actual).ConfigureAwait(false); }
                catch (Exception error) { originals.Capture(actual, error); }
            if (originals.OriginalErrors.Count == 0)
            { _closed = true; _lifetime.Cancel(); _lifetime.Dispose(); StateChanged = null; }
        }).ConfigureAwait(false);
    }
    private async Task CloseOnUiAsync()
    {
        Dispatcher.UIThread.VerifyAccess();
        using var phase = CloudflareOriginalExecutionGuard.EnterOriginal(this);
        var originals = Ledger();
        await originals.RunToOriginalSettlementAsync(async () =>
        {
            if (_page is { } page)
            {
                Task? returned = null, reported = null;
                try
                {
                    if (_published)
                    {
                        // The bool preflight did not freeze edits. Refuse before sealing a newer page draft.
                        var fence = _preparedFence;
                        if (fence is null || !page.IsOriginalBrowseCloseFenceCurrent(fence))
                            throw new InvalidOperationException("The original Mesh draft changed after preparation; its live page and unsaved work are retained.");
                        DemandPublicationCurrent();
                        returned = originals.Invoke(() => page.BeginOriginalPreparedBrowseClose(fence));
                    }
                    else
                    {
                        // Failed/unpublished construction still owns its actual children and must drain them.
                        originals.Invoke(() => { page.RequestRetirement(); return true; });
                        returned = originals.Invoke(page.CloseAndDrainAsync);
                    }
                }
                catch (Exception failure) { originals.Retain(failure); }
                // An acquisition callback may throw after starting retirement. Observe and drain the SAME reported original anyway.
                try { reported = originals.Invoke(() => page.OriginalClose); }
                catch (Exception failure)
                {
                    originals.Retain(failure);
                    try { reported = originals.Invoke(() => page.OriginalClose); }
                    catch (Exception observation) { originals.Retain(observation); }
                }
                if (returned is not null && !ReferenceEquals(returned, reported))
                    originals.Retain(new InvalidOperationException("Mesh did not retain the same actual page close task."));
                _actualPageClose = returned ?? reported;
                if (_actualPageClose is not null) { _closing = true; _published = false; }
                foreach (var actual in new[] { returned, reported }.OfType<Task>().Distinct())
                    try { await originals.AwaitAsync(actual); }
                    catch (Exception failure) { originals.Capture(actual, failure); }
            }
            else { _closing = true; _published = false; }
            // Never dispose the shared Home, repository, event bus or provider.
        });
    }
}
