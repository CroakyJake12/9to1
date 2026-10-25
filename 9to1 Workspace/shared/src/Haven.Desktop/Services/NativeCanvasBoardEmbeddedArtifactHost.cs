using Avalonia.Controls;
using Avalonia.Threading;
using CakeOS.Apps.Boards.App;
using CakeOS.Cui.Themes;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.Events;
using HavenOS.Apps.Browse;
using HavenOS.Apps.Canvas;
using HavenOS.Files.NativeHost;
using HavenOS.Home.Core;
using Microsoft.Extensions.DependencyInjection;
using System.Runtime.ExceptionServices;

namespace Haven.Desktop.Services;

/// <summary>Composite owner for Board-embedded productivity pages. Write continues to use its
/// existing Home/Notes owner. Canvas opens through the same native CanvasHostSurface and its
/// original Canvas Files/Home operations; no Files IDs or grants cross the Board reference.</summary>
internal sealed class NativeCanvasBoardEmbeddedArtifactHost : IBoardsEmbeddedArtifactHost, IAsyncDisposable
{
    private readonly BoardEmbeddedWritePageOwner _write;
    private readonly IServiceProvider _provider;
    private readonly HomeNativeWindowsComposition _home;
    private readonly string _profileId;
    private readonly CancellationToken _appLifetime, _tabLifetime;
    private readonly Func<bool> _tabIsCurrent;
    private readonly CuiThemePreferenceContext? _theme;
    private readonly object _gate = new();
    private readonly List<Task> _operations = [];
    private readonly List<CanvasEmbeddedSession> _canvasSessions = [];
    private readonly List<RetainedCanvasChooser> _retainedChoosers = [];
    private Control? _owningControl;
    private bool _retiring;
    private Task? _close;

    internal NativeCanvasBoardEmbeddedArtifactHost(BoardEmbeddedWritePageOwner write,
        IServiceProvider provider, HomeNativeWindowsComposition home, string profileId, Guid tabId,
        CancellationToken appLifetime, CancellationToken tabLifetime, Func<bool> tabIsCurrent,
        CuiThemePreferenceContext? theme = null)
    {
        _write = write ?? throw new ArgumentNullException(nameof(write));
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(home);
        ArgumentException.ThrowIfNullOrWhiteSpace(profileId);
        ArgumentNullException.ThrowIfNull(tabIsCurrent);
        if (tabId == Guid.Empty || !appLifetime.CanBeCanceled || !tabLifetime.CanBeCanceled)
            throw new ArgumentException("Retain the actual Board tab, application and host lifetimes.");
        WindowsHomeSameProcessRuntimeObservation.DemandOriginalBinding(provider, home);
        _provider = provider;
        _home = home;
        _profileId = profileId;
        _appLifetime = appLifetime;
        _tabLifetime = tabLifetime;
        _tabIsCurrent = tabIsCurrent;
        _theme = theme;
    }

    public void BindOwningControl(Control control)
    {
        ArgumentNullException.ThrowIfNull(control);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_retiring || _close is not null, this);
            if (_owningControl is not null && !ReferenceEquals(_owningControl, control))
                throw new InvalidOperationException("Bind the actual composed Board root once.");
            _owningControl = control;
        }
        _write.BindOwningControl(control);
    }

    public Task<IBoardsEmbeddedArtifactSession> OpenAsync(BoardEmbeddedArtifactReference reference,
        BoardEmbeddedArtifactMode mode, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(reference);
        reference.Validate();
        return reference.ApplicationId switch
        {
            "write" => _write.OpenAsync(reference, mode, ct),
            "canvas" => OpenCanvasAsync(reference, mode, ct),
            _ => Task.FromException<IBoardsEmbeddedArtifactSession>(
                new NotSupportedException("Only the original Write and Canvas artifact owners can be embedded.")),
        };
    }

    public Task<BoardEmbeddedArtifactReference?> SelectAsync(string applicationId, CancellationToken ct = default) =>
        applicationId switch
        {
            "write" => _write.SelectAsync(applicationId, ct),
            "canvas" => ChooseCanvasAsync(CanvasEmbeddedArtifactChoiceMode.SelectExisting, ct),
            _ => Task.FromException<BoardEmbeddedArtifactReference?>(new NotSupportedException("This artifact owner has no Home selector.")),
        };

    public Task<BoardEmbeddedArtifactReference?> CreateAsync(string applicationId, CancellationToken ct = default) =>
        applicationId switch
        {
            "write" => _write.CreateAsync(applicationId, ct),
            "canvas" => ChooseCanvasAsync(CanvasEmbeddedArtifactChoiceMode.CreateNew, ct),
            _ => Task.FromException<BoardEmbeddedArtifactReference?>(new NotSupportedException("This artifact owner has no Home create action.")),
        };

    private Task<BoardEmbeddedArtifactReference?> ChooseCanvasAsync(CanvasEmbeddedArtifactChoiceMode mode,
        CancellationToken caller) => RunCanvasAsync(async token =>
    {
        token.ThrowIfCancellationRequested();
        DemandAlive();
        var files = _provider.GetRequiredService<NativeFilesWorkspaceService>();
        var authority = _provider.GetRequiredService<NativeFilesWorkspaceAuthority>();
        if (!ReferenceEquals(_home.Services.GetService(typeof(NativeFilesWorkspaceService)), files) ||
            !ReferenceEquals(_home.Services.GetService(typeof(NativeFilesWorkspaceAuthority)), authority))
            throw new UnauthorizedAccessException("Canvas must use the same original Home Files owners.");

        // Cancellation of the one-shot Board action must not revoke a still-visible owner
        // chooser while it has a Home/native operation that can refuse close.
        var lifetime = CancellationTokenSource.CreateLinkedTokenSource(_appLifetime, _tabLifetime);
        var route = new NativeCanvasDesktopRoute(_provider, _home, files, authority,
            _profileId, _appLifetime, lifetime.Token);
        route.BindOriginalPresentationOwner(this);
        CanvasHostSurface? surface = null;
        CanvasEmbeddedArtifactChoiceSession? choice = null;
        CanvasHostSurface.ClosePreparation? preparation = null;
        Window? dialog = null;
        Task? dialogTask = null;
        var accepted = false;
        Exception? primary = null;
        var failures = new List<Exception>();
        try
        {
            await route.RevalidateAsync(token).ConfigureAwait(false);
            surface = await OnUiAsync(() => route.AcquireOriginalSurface(_theme)).ConfigureAwait(false);
            choice = await AwaitOnUiAsync(() => CanvasEmbeddedArtifactChoiceSession.OpenAsync(surface, mode, token)).ConfigureAwait(false);

            var shown = await OnUiAsync(() =>
            {
                Control? root;
                lock (_gate) root = _owningControl;
                if (root is null || TopLevel.GetTopLevel(root) is not Window owner || !owner.IsVisible)
                    throw new UnauthorizedAccessException("The actual Boards root is not attached to its owning Home window.");
                var window = new Window
                {
                    Title = mode == CanvasEmbeddedArtifactChoiceMode.CreateNew ? "Create a Canvas for this Board" : "Select a Canvas for this Board",
                    Width = 900,
                    Height = 720,
                    MinWidth = 640,
                    MinHeight = 480,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                    Content = choice.View
                };
                var task = window.ShowDialog(owner);
                return (Window: window, Task: task);
            }).ConfigureAwait(false);
            dialog = shown.Window;
            dialogTask = shown.Task;

            var selectedTask = choice.Choice;
            var completed = await Task.WhenAny(selectedTask, dialogTask).WaitAsync(token).ConfigureAwait(false);
            if (!ReferenceEquals(completed, selectedTask)) return null;
            var selected = await selectedTask.ConfigureAwait(false);
            if (selected is null) return null;

            await AwaitOnUiAsync(() => choice.RevalidateAsync(selected, token)).ConfigureAwait(false);
            await route.RevalidateAsync(token).ConfigureAwait(false);
            DemandAlive();
            preparation = await AwaitOnUiAsync(() => choice.PrepareCloseAsync(token)).ConfigureAwait(false)
                ?? throw new InvalidOperationException("Canvas refused to close its owner chooser; preserve the actual view.");
            await CloseDialogAsync(dialog, dialogTask).ConfigureAwait(false);
            dialog = null;
            await AwaitOnUiAsync(preparation.BeginClose).ConfigureAwait(false);
            accepted = true;
            await AwaitOnUiAsync(choice.CloseAndDrainAsync).ConfigureAwait(false);
            await route.CloseAndDrainAsync().ConfigureAwait(false);
            var reference = new BoardEmbeddedArtifactReference("canvas", selected.ArtifactId,
                new BoardEmbeddedArtifactRevision(null, selected.ExpectedRevisionId));
            return reference.Validate();
        }
        catch (Exception error)
        {
            primary = error;
            throw;
        }
        finally
        {
            if (choice is not null && !accepted)
            {
                try
                {
                    if (preparation is null)
                        preparation = await AwaitOnUiAsync(() => choice.PrepareCloseAsync(CancellationToken.None)).ConfigureAwait(false);
                    if (preparation is null)
                        throw new InvalidOperationException("Canvas chooser still has original Home/native work; retain its visible owner and refuse cleanup.");
                    if (dialog is not null)
                    {
                        await CloseDialogAsync(dialog, dialogTask).ConfigureAwait(false);
                        dialog = null;
                    }
                    await AwaitOnUiAsync(preparation.BeginClose).ConfigureAwait(false);
                    accepted = true;
                    await AwaitOnUiAsync(choice.CloseAndDrainAsync).ConfigureAwait(false);
                }
                catch (Exception error) { failures.Add(error); }
            }

            if (choice is null && surface is not null)
            {
                try { await AwaitOnUiAsync(() => surface.CloseAndDrainAsync()).ConfigureAwait(false); }
                catch (Exception error) { failures.Add(error); }
            }

            if (choice is null || accepted)
            {
                if (dialog is not null)
                    try { await CloseDialogAsync(dialog, dialogTask).ConfigureAwait(false); }
                    catch (Exception error) { failures.Add(error); }
                try { await route.CloseAndDrainAsync().ConfigureAwait(false); }
                catch (Exception error) { failures.Add(error); }
                try { lifetime.Cancel(); }
                catch (Exception error) { failures.Add(error); }
                try { lifetime.Dispose(); }
                catch (Exception error) { failures.Add(error); }
            }

            if (failures.Count > 0)
            {
                if (primary is not null) failures.Insert(0, primary);
                if (choice is not null && !accepted)
                {
                    var retained = new RetainedCanvasChooser(route, lifetime, choice, surface!, dialog, dialogTask, preparation);
                    lock (_gate) _retainedChoosers.Add(retained);
                }
                throw new AggregateException("Canvas chooser operation or its original owner cleanup failed.", failures);
            }
        }
    }, caller);

    private static async Task CloseDialogAsync(Window dialog, Task? dialogTask)
    {
        await Dispatcher.UIThread.InvokeAsync(() => { if (dialog.IsVisible) dialog.Close(); }).GetTask().ConfigureAwait(false);
        if (dialogTask is not null) await dialogTask.ConfigureAwait(false);
    }

    private Task<IBoardsEmbeddedArtifactSession> OpenCanvasAsync(BoardEmbeddedArtifactReference reference,
        BoardEmbeddedArtifactMode mode, CancellationToken caller)
    {
        if (!StringComparer.Ordinal.Equals(reference.ApplicationId, "canvas") ||
            reference.ExpectedRevision.Sequence is not null || reference.ExpectedRevision.VersionId is not { } revision ||
            revision == Guid.Empty || reference.ArtifactId == Guid.Empty || !Enum.IsDefined(mode))
            return Task.FromException<IBoardsEmbeddedArtifactSession>(
                new InvalidDataException("A pinned Canvas artifact ID and owning revision are required."));
        return RunCanvasAsync(async token =>
        {
            token.ThrowIfCancellationRequested();
            DemandAlive();
            var files = _provider.GetRequiredService<NativeFilesWorkspaceService>();
            var authority = _provider.GetRequiredService<NativeFilesWorkspaceAuthority>();
            if (!ReferenceEquals(_home.Services.GetService(typeof(NativeFilesWorkspaceService)), files) ||
                !ReferenceEquals(_home.Services.GetService(typeof(NativeFilesWorkspaceAuthority)), authority))
                throw new UnauthorizedAccessException("Canvas must use the same original Home Files owners.");

            var lifetime = CancellationTokenSource.CreateLinkedTokenSource(_appLifetime, _tabLifetime, token);
            NativeCanvasDesktopRoute route;
            try
            {
                route = new NativeCanvasDesktopRoute(_provider, _home, files, authority,
                    _profileId, _appLifetime, lifetime.Token);
            }
            catch
            {
                lifetime.Cancel();
                lifetime.Dispose();
                throw;
            }
            var session = new CanvasEmbeddedSession(this, route, lifetime);
            lock (_gate) _canvasSessions.Add(session);
            try
            {
                route.BindOriginalPresentationOwner(this);
                await session.OpenAsync(new(reference.ArtifactId, revision),
                    mode == BoardEmbeddedArtifactMode.Edit ? CanvasEmbeddedArtifactMode.Edit : CanvasEmbeddedArtifactMode.View,
                    token).ConfigureAwait(false);
                if (!Equals(session.CurrentReference, reference))
                    throw new InvalidOperationException("Canvas opened a different artifact or owning revision than the Board reference.");
                if (!await session.RevalidateAsync(token).ConfigureAwait(false))
                    throw new UnauthorizedAccessException("Canvas source authority changed before Board publication.");
                return (IBoardsEmbeddedArtifactSession)session;
            }
            catch (Exception openError)
            {
                try
                {
                    await session.DrainFailedOpenAsync().ConfigureAwait(false);
                    lock (_gate) _canvasSessions.Remove(session);
                }
                catch (Exception cleanupError)
                {
                    throw new AggregateException("Canvas embedded open failed and its original host cleanup remains retained.",
                        openError, cleanupError);
                }
                ExceptionDispatchInfo.Capture(openError).Throw();
                throw;
            }
        }, caller);
    }

    private Task<T> RunCanvasAsync<T>(Func<CancellationToken, Task<T>> body, CancellationToken caller)
    {
        ArgumentNullException.ThrowIfNull(body);
        caller.ThrowIfCancellationRequested();
        Task<T>? actual = null;
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_retiring || _close is not null, this);
            if (_operations.Count >= 128)
                throw new InvalidOperationException("Settle the retained original Canvas operations before retrying.");
            actual = RunAsync(start.Task);
            _operations.Add(actual);
        }
        start.SetResult();
        return actual!;

        async Task<T> RunAsync(Task gate)
        {
            await gate.ConfigureAwait(false);
            try
            {
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(caller, _appLifetime, _tabLifetime);
                using var phase = CloudflareOriginalExecutionGuard.EnterOriginal(this);
                DemandAlive();
                return await body(linked.Token).ConfigureAwait(false);
            }
            finally
            {
                // Retained chooser/session collections own cleanup that refused. Keep the
                // task joined until it settles, then remove its faulted observation so a
                // later successful owner drain can be retried cleanly.
                lock (_gate) _operations.Remove(actual!);
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        CloudflareOriginalExecutionGuard.DemandExternalJoin(this);
        Task close;
        lock (_gate)
        {
            if (_close is not null) close = _close;
            else
            {
                _retiring = true;
                close = _close = CloseCoreAsync();
            }
        }
        try { await close.ConfigureAwait(false); }
        catch
        {
            lock (_gate)
                if (ReferenceEquals(_close, close)) { _close = null; _retiring = false; }
            throw;
        }
    }

    private async Task CloseCoreAsync()
    {
        Task[] operations;
        lock (_gate) operations = _operations.ToArray();
        var failures = new List<Exception>();
        foreach (var operation in operations)
            try { await operation.ConfigureAwait(false); }
            catch (Exception error) { failures.Add(operation.Exception ?? error); }

        CanvasEmbeddedSession[] sessions;
        lock (_gate) sessions = _canvasSessions.ToArray();
        foreach (var session in sessions)
        {
            try
            {
                if (!session.IsOpened)
                    await session.DrainFailedOpenAsync().ConfigureAwait(false);
                else
                {
                    var preparation = await session.PrepareToCloseAsync(CancellationToken.None).ConfigureAwait(false);
                    if (preparation is null)
                        throw new InvalidOperationException("Canvas refused close; preserve its native view and owner tasks.");
                    await preparation.BeginCloseAsync(CancellationToken.None).ConfigureAwait(false);
                    await session.CloseAndDrainAsync().ConfigureAwait(false);
                }
                await session.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception error) { failures.Add(error); }
        }
        RetainedCanvasChooser[] choosers;
        lock (_gate) choosers = _retainedChoosers.ToArray();
        foreach (var chooser in choosers)
        {
            try
            {
                await chooser.CloseAndDrainAsync().ConfigureAwait(false);
                lock (_gate) _retainedChoosers.Remove(chooser);
            }
            catch (Exception error) { failures.Add(error); }
        }
        try { await _write.DisposeAsync().ConfigureAwait(false); }
        catch (Exception error) { failures.Add(error); }
        if (failures.Count > 0)
            throw new AggregateException("Original embedded Canvas and Write owners did not drain cleanly.", failures);
    }

    private bool IsAlive => !_appLifetime.IsCancellationRequested && !_tabLifetime.IsCancellationRequested &&
        _tabIsCurrent() && _home.OriginalCloseTask is null && _home.OriginalProcessRetirementRequestTask is null;

    private void DemandAlive()
    {
        _appLifetime.ThrowIfCancellationRequested();
        _tabLifetime.ThrowIfCancellationRequested();
        Control? owningControl;
        lock (_gate) owningControl = _owningControl;
        if (owningControl is null || !IsAlive || _home.OriginalCloseTask is not null ||
            _home.OriginalProcessRetirementRequestTask is not null)
            throw new ObjectDisposedException(nameof(NativeCanvasBoardEmbeddedArtifactHost));
        WindowsHomeSameProcessRuntimeObservation.DemandOriginalBinding(_provider, _home);
    }

    private static async Task<T> OnUiAsync<T>(Func<T> action)
    {
        var dispatch = Dispatcher.UIThread.InvokeAsync(action).GetTask();
        return await dispatch.ConfigureAwait(false);
    }

    private static async Task AwaitOnUiAsync(Func<Task> action)
    {
        var dispatch = Dispatcher.UIThread.InvokeAsync<Task>(action).GetTask();
        var actual = await dispatch.ConfigureAwait(false);
        await actual.ConfigureAwait(false);
    }

    private static async Task<T> AwaitOnUiAsync<T>(Func<Task<T>> action)
    {
        var dispatch = Dispatcher.UIThread.InvokeAsync<Task<T>>(action).GetTask();
        var actual = await dispatch.ConfigureAwait(false);
        return await actual.ConfigureAwait(false);
    }

    private sealed class CanvasEmbeddedSession(NativeCanvasBoardEmbeddedArtifactHost owner,
        NativeCanvasDesktopRoute route, CancellationTokenSource lifetime) : IBoardsEmbeddedArtifactSession
    {
        private readonly object _gate = new();
        private CanvasHostSurface? _surface;
        private CanvasEmbeddedArtifactSession? _canvas;
        private IBoardsEmbeddedArtifactClosePreparation? _preparation;
        private bool _closed;
        private Task? _close;

        public bool IsOpened => _canvas is not null;
        public Control Content => _canvas?.View ?? throw new InvalidOperationException("The pinned Canvas view is not open.");
        public BoardEmbeddedArtifactReference CurrentReference
        {
            get
            {
                owner.DemandAlive();
                var target = _canvas?.CurrentTarget ?? throw new InvalidOperationException("The pinned Canvas target is absent.");
                if (target.ArtifactId == Guid.Empty || target.ExpectedRevisionId == Guid.Empty)
                    throw new InvalidDataException("Canvas has no stable current owner revision.");
                return new("canvas", target.ArtifactId,
                    new BoardEmbeddedArtifactRevision(null, target.ExpectedRevisionId));
            }
        }
        public bool IsPublicationCurrent
        {
            get
            {
                lock (_gate) return !_closed && owner.IsAlive && route.IsAlive && _canvas?.IsAlive == true;
            }
        }

        public async Task OpenAsync(CanvasEmbeddedArtifactTarget target, CanvasEmbeddedArtifactMode mode,
            CancellationToken token)
        {
            await route.RevalidateAsync(token).ConfigureAwait(false);
            owner.DemandAlive();
            var surface = await OnUiAsync(() => route.AcquireOriginalSurface(owner._theme)).ConfigureAwait(false);
            _surface = surface;
            var canvas = await AwaitOnUiAsync(() => CanvasEmbeddedArtifactSession.OpenAsync(surface, target, mode, token)).ConfigureAwait(false);
            _canvas = canvas;
            if (!await RevalidateAsync(token).ConfigureAwait(false))
                throw new UnauthorizedAccessException("Canvas authorization or current Files revision changed during open.");
        }

        public async Task<bool> RevalidateAsync(CancellationToken ct = default)
        {
            if (!IsPublicationCurrent) return false;
            try
            {
                var canvas = _canvas ?? throw new InvalidOperationException("The pinned Canvas session is absent.");
                await AwaitOnUiAsync(() => canvas.RevalidateAsync(ct)).ConfigureAwait(false);
                await route.RevalidateAsync(ct).ConfigureAwait(false);
                return IsPublicationCurrent;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch { return false; }
        }

        public async Task<IBoardsEmbeddedArtifactClosePreparation?> PrepareToCloseAsync(CancellationToken ct = default)
        {
            if (!IsPublicationCurrent) return null;
            var canvas = _canvas ?? throw new InvalidOperationException("The pinned Canvas session is absent.");
            var original = await AwaitOnUiAsync(() => canvas.PrepareCloseAsync(ct)).ConfigureAwait(false);
            if (original is null) return null;
            var preparation = new CanvasClosePreparation(this, original);
            var accepted = false;
            lock (_gate)
            {
                if (!_closed && original.IsCurrent)
                {
                    _preparation = preparation;
                    accepted = true;
                }
            }
            if (!accepted)
            {
                await AwaitOnUiAsync(original.AbortAsync).ConfigureAwait(false);
                return null;
            }
            return preparation;
        }

        public Task CloseAndDrainAsync()
        {
            CloudflareOriginalExecutionGuard.DemandExternalJoin(this);
            lock (_gate)
            {
                if (_close is not null) return _close;
                if (_closed) return Task.CompletedTask;
                if (_preparation is not CanvasClosePreparation { IsCommitted: true })
                    throw new InvalidOperationException("The Canvas close preparation must be committed before owner retirement.");
                return _close = DrainAcceptedAsync();
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (_closed) return;
            if (_preparation is { } pending)
            {
                await pending.AbortAsync().ConfigureAwait(false);
                return;
            }
            var preparation = await PrepareToCloseAsync().ConfigureAwait(false)
                ?? throw new InvalidOperationException("Canvas refused disposal; preserve its draft and owner session.");
            await preparation.BeginCloseAsync(CancellationToken.None).ConfigureAwait(false);
            await CloseAndDrainAsync().ConfigureAwait(false);
        }

        public async Task DrainFailedOpenAsync()
        {
            if (_canvas is not null)
            {
                var preparation = await PrepareToCloseAsync(CancellationToken.None).ConfigureAwait(false)
                    ?? throw new InvalidOperationException("The partially opened Canvas owner refused cleanup.");
                await preparation.BeginCloseAsync(CancellationToken.None).ConfigureAwait(false);
                await CloseAndDrainAsync().ConfigureAwait(false);
                return;
            }
            var failures = new List<Exception>();
            if (_surface is { } surface)
                try { await AwaitOnUiAsync(() => surface.CloseAndDrainAsync()).ConfigureAwait(false); }
                catch (Exception error) { failures.Add(error); }
            try { await route.CloseAndDrainAsync().ConfigureAwait(false); }
            catch (Exception error) { failures.Add(error); }
            if (failures.Count > 0) throw new AggregateException("Canvas failed-open cleanup did not drain.", failures);
            lifetime.Cancel();
            lifetime.Dispose();
            lock (_gate) _closed = true;
            owner.Remove(this);
        }

        private async Task DrainAcceptedAsync()
        {
            var failures = new List<Exception>();
            var canvas = _canvas;
            if (canvas is not null)
                try { await AwaitOnUiAsync(() => canvas.CloseAndDrainAsync()).ConfigureAwait(false); }
                catch (Exception error) { failures.Add(error); }
            try { await route.CloseAndDrainAsync().ConfigureAwait(false); }
            catch (Exception error) { failures.Add(error); }
            if (failures.Count > 0) throw new AggregateException("The pinned Canvas view or its Home route failed to drain.", failures);
            lifetime.Cancel();
            lifetime.Dispose();
            lock (_gate) _closed = true;
            owner.Remove(this);
        }

        private void ClearPreparation(CanvasClosePreparation preparation)
        { lock (_gate) if (ReferenceEquals(_preparation, preparation)) _preparation = null; }

        private sealed class CanvasClosePreparation(CanvasEmbeddedSession owner,
            CanvasHostSurface.ClosePreparation original) : IBoardsEmbeddedArtifactClosePreparation
        {
            private readonly object _gate = new();
            private bool _committed, _aborted;
            private Task? _begin, _abort;
            public bool IsCommitted { get { lock (_gate) return _committed; } }
            public bool IsCurrent => !_committed && !_aborted && owner.IsPublicationCurrent && original.IsCurrent;

            public Task BeginCloseAsync(CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                lock (_gate)
                {
                    if (_begin is not null) return _begin;
                    if (!IsCurrent) return Task.FromException(new InvalidOperationException("The Canvas close preparation is stale."));
                    return _begin = BeginCloseCoreAsync();
                }
            }

            private async Task BeginCloseCoreAsync()
            {
                var accepted = await OnUiAsync(original.BeginClose).ConfigureAwait(false);
                lock (_gate) _committed = true;
                await accepted.ConfigureAwait(false);
            }

            public Task AbortAsync()
            {
                CloudflareOriginalExecutionGuard.DemandExternalJoin(this);
                lock (_gate)
                {
                    if (_abort is not null) return _abort;
                    if (_committed) return Task.FromException(new InvalidOperationException("An accepted Canvas close cannot be aborted."));
                    if (_aborted) return Task.CompletedTask;
                    return _abort = AbortCoreAsync();
                }
            }

            private async Task AbortCoreAsync()
            {
                await AwaitOnUiAsync(original.AbortAsync).ConfigureAwait(false);
                lock (_gate) _aborted = true;
                owner.ClearPreparation(this);
            }

            public async ValueTask DisposeAsync()
            {
                if (IsCurrent) await AbortAsync().ConfigureAwait(false);
            }
        }
    }

    private void Remove(CanvasEmbeddedSession session)
    { lock (_gate) _canvasSessions.Remove(session); }

    private sealed class RetainedCanvasChooser(NativeCanvasDesktopRoute route,
        CancellationTokenSource lifetime, CanvasEmbeddedArtifactChoiceSession choice,
        CanvasHostSurface surface, Window? dialog, Task? dialogTask,
        CanvasHostSurface.ClosePreparation? preparation)
    {
        private readonly SemaphoreSlim _gate = new(1, 1);
        private CanvasHostSurface.ClosePreparation? _preparation = preparation;
        private bool _closed;

        public async Task CloseAndDrainAsync()
        {
            await _gate.WaitAsync().ConfigureAwait(false);
            try
            {
                if (_closed) return;
                if (_preparation is null && choice.IsAlive)
                    _preparation = await AwaitOnUiAsync(() => choice.PrepareCloseAsync(CancellationToken.None)).ConfigureAwait(false);
                if (_preparation is null && choice.IsAlive)
                    throw new InvalidOperationException("Canvas still has pending Home/native work; keep its chooser owner retained.");
                if (dialog is not null)
                    await CloseDialogAsync(dialog, dialogTask).ConfigureAwait(false);
                if (_preparation is not null && choice.IsAlive)
                    await AwaitOnUiAsync(_preparation.BeginClose).ConfigureAwait(false);
                await AwaitOnUiAsync(choice.CloseAndDrainAsync).ConfigureAwait(false);
                await route.CloseAndDrainAsync().ConfigureAwait(false);
                lifetime.Cancel();
                lifetime.Dispose();
                _closed = true;
            }
            finally { _gate.Release(); }
        }
    }
}
