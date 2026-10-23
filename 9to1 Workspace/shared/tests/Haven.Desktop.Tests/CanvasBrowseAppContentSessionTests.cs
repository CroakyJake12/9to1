using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using CakeOS.Cui;
using CakeOS.Cui.Runtime;
using Haven.Application;
using Haven.Browser;
using Haven.Core;
using Haven.Desktop.Services;
using Haven.Infrastructure;
using HavenOS.Apps.Browse;
using HavenOS.Apps.Canvas;
using HavenOS.Files.NativeHost;
using HavenOS.Home.Core;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Haven.Desktop.Tests;

/// <summary>Actual shared empty Canvas CUI/Home/Files with a typed route-observation seam.
/// These controls do not attest Rnote, installed admission, drawing or resource grants.</summary>
public sealed class CanvasBrowseAppContentSessionTests
{
    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(15);

    [Fact]
    public void Address_and_compiled_entry_select_only_the_canonical_Canvas_root()
    {
        _ = CanvasBrowseAppContentAddress.Parse(AppContentAddress.Parse("9to1://canvas"));
        Assert.Throws<UnauthorizedAccessException>(() => CanvasBrowseAppContentAddress.Parse(AppContentAddress.Parse("9to1://files")));
        Assert.Throws<NotSupportedException>(() => CanvasBrowseAppContentAddress.Parse(AppContentAddress.Parse("9to1://canvas/foreign")));
        Assert.Equal("canvas", App.ResolveOriginalInitialProductDeclaration([], true));
        Assert.Equal("canvas", App.ResolveOriginalInitialProductDeclaration([new("9to1.InitialApp", "canvas")], false));
        Assert.Null(App.ResolveOriginalInitialProductDeclaration([], false));
        Assert.Throws<InvalidDataException>(() => App.ResolveOriginalInitialProductDeclaration([new("9to1.InitialApp", "write")], true));
        Assert.Throws<InvalidDataException>(() => App.ResolveOriginalInitialProductDeclaration(
            [new("9to1.InitialApp", "canvas"), new("9to1.InitialApp", "canvas")], true));
    }

    [AvaloniaFact]
    public async Task Actual_shared_content_retains_original_tasks_and_reversible_presentation_only_close()
    {
        await using var rig = await Rig.CreateAsync();
        var session = rig.Create();
        Assert.Null(session.OriginalInitialization); Assert.False(session.IsPublicationCurrent);
        Assert.Throws<InvalidOperationException>(() => session.OriginalNativeView);
        var initialization = session.InitializeAsync();
        Assert.Same(initialization, session.InitializeAsync()); Assert.Same(initialization, session.OriginalInitialization);
        await initialization.WaitAsync(Bound);
        var view = Assert.IsType<CanvasHostSurface>(session.OriginalNativeView);
        Assert.NotNull(view.Content); Assert.IsNotType<Window>(view);
        Assert.Same(view, rig.Route.Surface); Assert.Equal(1, rig.Route.Acquisitions);
        Assert.True(await session.TryPrepareToCloseAsync().WaitAsync(Bound));
        Assert.Null(session.OriginalClose); Assert.Same(view, session.OriginalNativeView);
        var first = Assert.IsAssignableFrom<IBrowseAppContentClosePreparation>(await session.PrepareCloseAsync().WaitAsync(Bound));
        Assert.Same(session, first.Session); Assert.True(first.IsCurrent);
        await first.AbortAsync().WaitAsync(Bound); Assert.False(first.IsCurrent);
        Assert.Throws<InvalidOperationException>(() => { _ = first.BeginClose(); });
        var accepted = Assert.IsAssignableFrom<IBrowseAppContentClosePreparation>(await session.PrepareCloseAsync().WaitAsync(Bound));
        var close = accepted.BeginClose();
        Assert.Same(close, accepted.BeginClose()); Assert.Same(close, session.CloseAndDrainAsync()); Assert.Same(close, session.OriginalClose);
        await close.WaitAsync(Bound);
        Assert.True(session.CanRetireOriginalView); Assert.False(session.IsPublicationCurrent);
        Assert.True(view.OriginalCloseTask!.IsCompletedSuccessfully); Assert.True(rig.Route.Closed);
        Assert.Null(rig.Home.OriginalCloseTask); Assert.True(rig.Home.OriginalStartTask!.IsCompletedSuccessfully);
    }

    [AvaloniaFact]
    public async Task Held_original_observation_must_settle_before_retirement_can_finish()
    {
        await using var rig = await Rig.CreateAsync();
        var held = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        rig.Route.Observation = held.Task;
        var session = rig.Create(); var initialization = session.InitializeAsync();
        Task close;
        try
        {
            await rig.Route.Entered.Task.WaitAsync(Bound);
            close = session.CloseAndDrainAsync();
            Assert.Same(close, session.CloseAndDrainAsync()); Assert.False(close.IsCompleted);
            Assert.False(initialization.IsCompleted); Assert.Null(rig.Route.Surface);
        }
        finally { held.TrySetResult(); }
        Assert.NotNull(await Record.ExceptionAsync(() => initialization.WaitAsync(Bound)));
        Assert.NotNull(await Record.ExceptionAsync(() => close.WaitAsync(Bound)));
        Assert.True(rig.Route.Closed); Assert.False(session.CanRetireOriginalView);
        Assert.False(session.IsPublicationCurrent); Assert.Null(rig.Route.Surface); Assert.Null(rig.Home.OriginalCloseTask);
    }

    [AvaloniaFact]
    public async Task Faulted_observation_keeps_every_original_cause_and_cannot_certify_clean_retirement()
    {
        await using var rig = await Rig.CreateAsync();
        var cancellation = new OperationCanceledException("Faulted original; this is not a canceled Task.");
        var sibling = new IOException("Original independent source failure.");
        var raw = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        raw.SetException([cancellation, sibling]); rig.Route.Observation = raw.Task;
        var session = rig.Create(); var initialization = session.InitializeAsync();
        var failure = Assert.IsAssignableFrom<Exception>(await Record.ExceptionAsync(() => initialization.WaitAsync(Bound)));
        Assert.True(raw.Task.IsFaulted); Assert.True(initialization.IsFaulted);
        Assert.Contains(Causes(failure), cause => ReferenceEquals(cause, cancellation));
        Assert.Contains(Causes(failure), cause => ReferenceEquals(cause, sibling));
        var closeFailure = Assert.IsAssignableFrom<Exception>(await Record.ExceptionAsync(() => session.CloseAndDrainAsync().WaitAsync(Bound)));
        Assert.Contains(Causes(closeFailure), cause => ReferenceEquals(cause, cancellation));
        Assert.Contains(Causes(closeFailure), cause => ReferenceEquals(cause, sibling));
        Assert.False(session.CanRetireOriginalView); Assert.Null(rig.Home.OriginalCloseTask);
    }

    [AvaloniaFact]
    public async Task Publication_and_actual_lifetime_callbacks_cannot_join_their_original_owners_under_restored_context()
    {
        var before = ExecutionContext.Capture()!;
        await using var rig = await Rig.CreateAsync();
        var session = rig.Create(); var publications = 0; var cancellations = 0;
        session.StateChanged += (_, _) => ExecutionContext.Run(before.CreateCopy(), _ =>
        {
            Assert.Throws<InvalidOperationException>(() => { _ = session.PrepareCloseAsync(); });
            Assert.Throws<InvalidOperationException>(() => { _ = session.TryPrepareToCloseAsync(); });
            Assert.Throws<InvalidOperationException>(() => { _ = session.CloseAndDrainAsync(); });
            Assert.Null(session.OriginalClose); publications++;
        }, null);
        await session.InitializeAsync().WaitAsync(Bound);
        using var registration = rig.Presentation!.Token.Register(() => ExecutionContext.Run(before.CreateCopy(), _ =>
        {
            Assert.Throws<InvalidOperationException>(() => { _ = session.PrepareCloseAsync(); });
            Assert.Throws<InvalidOperationException>(() => { _ = session.CloseAndDrainAsync(); });
            Assert.Throws<InvalidOperationException>(() => { _ = rig.Route.CloseAndDrainAsync(); });
            Assert.Throws<InvalidOperationException>(() => { _ = rig.Route.Surface!.CloseAndDrainAsync(); });
            cancellations++;
        }, null));
        await session.CloseAndDrainAsync().WaitAsync(Bound);
        Assert.Equal(1, publications); Assert.Equal(1, cancellations); Assert.True(session.CanRetireOriginalView);
        Assert.Null(rig.Home.OriginalCloseTask);
    }

    [AvaloniaFact]
    public async Task Actual_Home_profile_callback_physically_guards_the_concrete_route_and_parent_after_await()
    {
        var before = ExecutionContext.Capture()!;
        var principal = new Principal();
        await using var rig = await Rig.CreateAsync(principal);
        using var hostLifetime = new CancellationTokenSource(Bound);
        var registrations = new ServiceCollection();
        registrations.AddSingleton<IAppPaths>(rig.OriginalPaths);
        registrations.AddSingleton<IProviderSecretStore, WindowsProviderSecretStore>();
        registrations.AddHavenOwnedWindowsHomeDomain(rig.Home, rig.OriginalPaths, principal);
        await using var provider = registrations.BuildServiceProvider();
        var actor = await rig.Home.Profiles.GetCurrentAsync(default) ?? throw new InvalidOperationException("Actual Home actor unavailable.");
        var route = new NativeCanvasDesktopRoute(provider, rig.Home, rig.Files, rig.Authority,
            actor.ProfileId, rig.Process.Token, hostLifetime.Token);
        var request = new BrowseAppContentRequest(Guid.NewGuid(), AppContentAddress.Parse("9to1://canvas"), actor.ProfileId, BrowserTabPrivacy.Standard);
        var session = rig.Own(new CanvasBrowseAppContentSession(request, route, CancellationTokenSource.CreateLinkedTokenSource(hostLifetime.Token)));
        var callbacks = 0;
        principal.HoldNext = true;
        principal.Next = () => ExecutionContext.Run(before.CreateCopy(), _ =>
        {
            Assert.Throws<InvalidOperationException>(() => { _ = session.PrepareCloseAsync(); });
            Assert.Throws<InvalidOperationException>(() => { _ = session.CloseAndDrainAsync(); });
            Assert.Throws<InvalidOperationException>(() => { _ = route.CloseAndDrainAsync(); });
            Assert.Null(session.OriginalClose); callbacks++;
        }, null);
        var actual = route.RevalidateAsync(hostLifetime.Token);
        try { await principal.Entered.Task.WaitAsync(Bound); Assert.False(actual.IsCompleted); }
        finally { principal.Release.TrySetResult(); }
        try { await actual.WaitAsync(Bound); Assert.Equal(1, callbacks); Assert.Null(session.OriginalInitialization); }
        finally { principal.Next = null; await session.CloseAndDrainAsync().WaitAsync(Bound); }
        Assert.Null(rig.Home.OriginalCloseTask); // The observation did not acquire an engine or native document.
    }

    [AvaloniaFact]
    public async Task Actual_WorkOwner_action_token_cancellation_physically_guards_content_concrete_route_and_parent()
    {
        var before = ExecutionContext.Capture()!;
        var principal = new Principal();
        await using var rig = await Rig.CreateAsync(principal);
        using var routeLifetime = new CancellationTokenSource();
        var actor = await rig.Home.Profiles.GetCurrentAsync(default)
            ?? throw new InvalidOperationException("Actual Home actor unavailable.");
        var concreteRoute = new NativeCanvasDesktopRoute(rig.Home.Services, rig.Home, rig.Files, rig.Authority,
            actor.ProfileId, rig.Process.Token, routeLifetime.Token);
        // The existing typed route controls acquisition only. Physical source custody uses
        // the real concrete route and its same parent; this does not attest Rnote admission.
        rig.Route.ConcreteSource = concreteRoute;
        var session = rig.Create();
        await session.InitializeAsync().WaitAsync(Bound);
        var surface = Assert.IsType<CanvasHostSurface>(session.OriginalNativeView);
        var window = new Window { Content = surface, Width = 1200, Height = 800 };
        window.Show();
        var canceled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken dependencyToken = default;
        CancellationTokenRegistration dependencyRegistration = default;
        Exception? contentReentry = null, routeReentry = null, parentReentry = null;
        var callbacks = 0;
        Task? action = null, close = null;
        principal.HoldNext = true;
        principal.Held = token =>
        {
            dependencyToken = token;
            dependencyRegistration = token.Register(() =>
            {
                ExecutionContext.Run(before.CreateCopy(), _ =>
                {
                    contentReentry = Record.Exception(() => { _ = surface.CloseAndDrainAsync(); });
                    routeReentry = Record.Exception(() => { _ = concreteRoute.CloseAndDrainAsync(); });
                    parentReentry = Record.Exception(() => { _ = session.CloseAndDrainAsync(); });
                    Interlocked.Increment(ref callbacks);
                }, null);
                canceled.TrySetResult();
            });
        };
        try
        {
            var search = Assert.IsType<Button>(Assert.Single(AuthoredTree(surface), control =>
                CuiRuntimeIdentity.GetStableId(control) == "id:canvas-search"));
            search.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await principal.Entered.Task.WaitAsync(Bound);
            action = Assert.IsAssignableFrom<Task>(surface.LatestOriginalCommand);
            var raw = Assert.IsAssignableFrom<Task>(principal.HeldOriginal);
            Assert.True(dependencyToken.CanBeCanceled); Assert.False(dependencyToken.IsCancellationRequested);
            Assert.False(action.IsCompleted); Assert.False(raw.IsCompleted);
            close = session.CloseAndDrainAsync();
            await canceled.Task.WaitAsync(Bound);
            Assert.Equal(1, callbacks); Assert.True(dependencyToken.IsCancellationRequested);
            Assert.IsType<InvalidOperationException>(contentReentry);
            Assert.IsType<InvalidOperationException>(routeReentry);
            Assert.IsType<InvalidOperationException>(parentReentry);
            Assert.Same(close, session.OriginalClose); Assert.NotNull(surface.OriginalCloseTask);
            Assert.False(raw.IsCompleted); Assert.False(action.IsCompleted); Assert.False(close.IsCompleted);
        }
        finally
        {
            principal.Held = null; principal.Release.TrySetResult();
            if (action is not null) _ = await Record.ExceptionAsync(() => action.WaitAsync(Bound));
            close ??= session.CloseAndDrainAsync();
            _ = await Record.ExceptionAsync(() => close.WaitAsync(Bound));
            dependencyRegistration.Dispose(); window.Close();
        }
        var original = Assert.IsAssignableFrom<Task>(principal.HeldOriginal);
        Assert.True(original.IsCanceled); Assert.True(action!.IsCompleted); Assert.True(close!.IsCompleted);
        Assert.False(close.IsCompletedSuccessfully); Assert.False(session.CanRetireOriginalView);
        Assert.Null(rig.Home.OriginalCloseTask);
    }

    private static IEnumerable<Control> AuthoredTree(Control root)
    {
        yield return root;
        var children = root is CuiContainerControl container
            ? container.AuthoredChildren : root.GetVisualChildren().OfType<Control>();
        foreach (var child in children)
            foreach (var descendant in AuthoredTree(child)) yield return descendant;
    }

    private static IEnumerable<Exception> Causes(Exception error)
    {
        yield return error;
        if (error is AggregateException group)
            foreach (var child in group.InnerExceptions) foreach (var cause in Causes(child)) yield return cause;
        else if (error.InnerException is { } inner) foreach (var cause in Causes(inner)) yield return cause;
    }
    private sealed class Principal : ITrustedHostPrincipalSource
    {
        private readonly OperatingSystemPrincipalSource _actual = new();
        internal bool HoldNext; internal Action? Next;
        internal Action<CancellationToken>? Held;
        internal Task? HeldOriginal;
        internal readonly TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ValueTask<string?> GetPrincipalAsync(CancellationToken token)
        {
            var hold = HoldNext; if (hold) HoldNext = false;
            var actual = ReadOriginalAsync(token, hold);
            if (hold) HeldOriginal = actual;
            return new(actual);
        }
        private async Task<string?> ReadOriginalAsync(CancellationToken token, bool hold)
        {
            var actual = await _actual.GetPrincipalAsync(token);
            if (hold)
            {
                Held?.Invoke(token); Entered.TrySetResult();
                await Release.Task.ConfigureAwait(false); token.ThrowIfCancellationRequested(); return actual;
            }
            Interlocked.Exchange(ref Next, null)?.Invoke(); return actual;
        }
    }
    private sealed class Rig : IAsyncDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "canvas-borrowed-tab-" + Guid.NewGuid().ToString("N"));
        internal readonly CancellationTokenSource Process = new();
        internal HomeNativeWindowsComposition Home = null!;
        internal NativeFilesWorkspaceService Files = null!;
        internal NativeFilesWorkspaceAuthority Authority = null!;
        internal IAppPaths OriginalPaths = null!;
        internal Route Route = null!;
        internal CancellationTokenSource? Presentation;
        private readonly List<CanvasBrowseAppContentSession> _sessions = [];
        internal static async Task<Rig> CreateAsync(ITrustedHostPrincipalSource? principal = null)
        {
            var rig = new Rig(); rig.OriginalPaths = new Paths(rig._root);
            try
            {
                rig.Home = new(new FileHomeCoreStateStore(Path.Combine(rig._root, "Home", "state.json")),
                    principal ?? new OperatingSystemPrincipalSource(), rig.OriginalPaths,
                    new("canvas.tab.control." + Guid.NewGuid().ToString("N")),
                    originalActionPolicies: [new CanvasNativeActionPolicies()],
                    configureOriginalStores: identity =>
                    {
                        rig.Files = new(identity.StateStore, identity.Profiles);
                        return new([rig.Files], new Dictionary<Type, object> { [typeof(NativeFilesWorkspaceService)] = rig.Files });
                    },
                    configureOriginalResolvers: ownership =>
                    {
                        rig.Authority = new(rig.Files, ownership.Identity.Profiles, ownership.Ownership);
                        return new([], new Dictionary<Type, object> { [typeof(NativeFilesWorkspaceAuthority)] = rig.Authority });
                    });
                await rig.Home.StartOriginalAsync();
                var empty = Path.Combine(rig._root, "explicit-empty-Files"); Directory.CreateDirectory(empty);
                await rig.Files.ConfigureNewAsync(empty, rig.Home.LocalStoreOwnership, rig.Process.Token);
                rig.Route = new(rig); return rig;
            }
            catch { await rig.DisposeAsync(); throw; }
        }
        internal CanvasBrowseAppContentSession Create()
        {
            Presentation = CancellationTokenSource.CreateLinkedTokenSource(Process.Token);
            var request = new BrowseAppContentRequest(Guid.NewGuid(), AppContentAddress.Parse("9to1://canvas"), "typed-observation-control", BrowserTabPrivacy.Standard);
            return Own(new(request, Route, Presentation));
        }
        internal CanvasBrowseAppContentSession Own(CanvasBrowseAppContentSession session)
        { _sessions.Add(session); return session; }
        public async ValueTask DisposeAsync()
        {
            try
            {
                foreach (var session in _sessions)
                    if (session.OriginalClose is not { IsCompleted: true })
                        await (session.OriginalClose ?? session.CloseAndDrainAsync()).WaitAsync(Bound);
            }
            finally
            {
              Process.Cancel();
              try { if (Home is not null) await Home.CloseAndDrainAsync(); }
              finally { Process.Dispose(); if (Directory.Exists(_root)) Directory.Delete(_root, true); }
            }
        }
    }
    private sealed class Route(Rig rig) : ICanvasOriginalDesktopRoute
    {
        private object? _parent;
        internal CanvasHostSurface? Surface;
        internal Task Observation = Task.CompletedTask;
        internal readonly TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal bool Closed; internal int Acquisitions;
        internal NativeCanvasDesktopRoute? ConcreteSource;
        public void BindOriginalPresentationOwner(object owner)
        {
            if (_parent is not null) throw new InvalidOperationException("Parent already bound.");
            _parent = owner; ConcreteSource?.BindOriginalPresentationOwner(owner);
        }
        public bool IsAlive => !Closed;
        public void RunWithinOriginalSource(Action source)
        {
            void Invoke() => CloudflareOriginalExecutionGuard.InvokeOriginal(_parent!, () =>
                CloudflareOriginalExecutionGuard.InvokeOriginal(this, () => { source(); return true; }));
            if (ConcreteSource is { } concrete) concrete.RunWithinOriginalSource(Invoke); else Invoke();
        }
        public Task RevalidateAsync(CancellationToken token)
        { Entered.TrySetResult(); return ObserveAsync(token); }
        private async Task ObserveAsync(CancellationToken token)
        {
            try { await Observation; }
            catch when (Observation.IsFaulted) { throw Observation.Exception!; }
            token.ThrowIfCancellationRequested();
        }
        public CanvasHostSurface AcquireOriginalSurface(CakeOS.Cui.Themes.CuiThemePreferenceContext? theme)
        { Acquisitions++; return Surface = new(rig.Home.Services, rig.Process.Token, null, RunWithinOriginalSource, theme, rig.Presentation!.Token); }
        public void DemandPublicationCurrent() { if (Closed) throw new ObjectDisposedException(nameof(Route)); }
        public Task CloseAndDrainAsync()
        {
            CloudflareOriginalExecutionGuard.DemandExternalJoin(this); Closed = true;
            return ConcreteSource?.CloseAndDrainAsync() ?? Task.CompletedTask;
        }
    }
    private sealed class Paths(string root) : IAppPaths
    {
        public string DataDirectory => root; public string DatabasePath => Path.Combine(root, "database");
        public string BrowserProfileDirectory => Path.Combine(root, "browser"); public string AttachmentsDirectory => Path.Combine(root, "attachments");
        public string LogsDirectory => Path.Combine(root, "logs"); public string LegacyStatePath => Path.Combine(root, "legacy");
    }
}
