using Avalonia.Controls;
using Avalonia.VisualTree;
using Avalonia.Headless.XUnit;
using CakeOS.Cui.Runtime;
using Haven.Application;
using Haven.Browser;
using Haven.Core;
using Haven.Desktop.Services;
using Haven.Infrastructure;
using HavenOS.Home.Core;
using HavenOS.Home.NativeUI;
using Microsoft.Extensions.DependencyInjection;
using HavenOS.Apps.Browse;
using NineToOne.Web.Productivity.Boards;

namespace Haven.Desktop.Tests;

/// <summary>Actual shared Boards core/CUI/retained renderer and canonical Notes store with
/// a deterministic HOST-observation seam. These do not claim real Home grants or native acceptance.</summary>
public sealed class BoardsBrowseAppContentSessionTests
{
    public static bool UsesLinuxOriginalNotesFixture => BoardsOriginalStoreSetupTests.UsesLinuxOriginalNotesFixture;

    [Fact]
    public void Address_requires_the_canonical_registered_notebook_identity()
    {
        Assert.Null(BoardsBrowseAppContentAddress.Parse(AppContentAddress.Parse("9to1://boards")).DocumentId);
        var id = Guid.NewGuid();
        Assert.Equal(id, BoardsBrowseAppContentAddress.Parse(AppContentAddress.Parse($"9to1://boards/notebooks/{id:D}")).DocumentId);
        Assert.Throws<UnauthorizedAccessException>(() => BoardsBrowseAppContentAddress.Parse(AppContentAddress.Parse("9to1://files")));
        foreach (var suffix in new[] { Guid.Empty.ToString("D"), id.ToString("N"), id.ToString("D") + "/foreign" })
            Assert.Throws<FormatException>(() => BoardsBrowseAppContentAddress.Parse(AppContentAddress.Parse("9to1://boards/notebooks/" + suffix)));
    }

    [AvaloniaFact]
    public async Task Shared_registry_loads_original_CUI_and_resolves_canonical_icons_without_browser_host()
    {
        await using var rig = new Rig();
        await using var core = new BoardsBrowserCore(rig.Notes);
        using var presentation = new BoardsCuiWorkspacePresentation(core, () => true);
        using var stream = typeof(BoardsBrowseAppContentSession).Assembly.GetManifestResourceStream(BoardsBrowseAppContentSession.CuiResourceName)
            ?? throw new InvalidOperationException("The actual shared Boards CUI resource is unavailable.");
        using var reader = new StreamReader(stream);
        // Parse the actual embedded app with its canonical parser before trying the renderer.
        // CuiDocumentSource intentionally has a terse exception; retain exact source diagnostics here.
        var parser = new CakeOS.Cui.Language.CuiRichParser();
        var document = parser.Parse(reader.ReadToEnd(), "Boards.cui");
        Assert.False(parser.Diagnostics.Diagnostics.Any(diagnostic => diagnostic.Severity == CakeOS.Cui.Language.CuiDiagnosticSeverity.Error),
            "BOARDS_CANONICAL_CUI_PARSE: " + string.Join(Environment.NewLine, parser.Diagnostics.Diagnostics.Select(diagnostic => diagnostic.ToString())));
        using var loader = new CuiControlLoader(presentation.ControlRegistry);
        loader.SetBindingContext(core); loader.SetActionDispatcher(core);
        var loaded = loader.TryLoad(document);
        Assert.True(loaded.Root is not null && !loaded.Diagnostics.Any(diagnostic => diagnostic.Severity == CakeOS.Cui.Language.CuiDiagnosticSeverity.Error),
            string.Join(Environment.NewLine, loaded.Diagnostics.Select(diagnostic => diagnostic.ToString())));
        loader.WireBindings(loaded.Root!);
    }

    [AvaloniaFact]
    public async Task Real_shared_CUI_has_original_tasks_and_reversible_clean_close()
    {
        await using var rig = new Rig(); var session = rig.Create();
        Assert.False(session.IsPublicationCurrent); Assert.Null(session.OriginalInitialization);
        Assert.Throws<InvalidOperationException>(() => session.OriginalNativeView);
        var original = session.InitializeAsync(); Assert.Same(original, session.InitializeAsync());
        Assert.Same(original, session.OriginalInitialization); await original;
        var view = Assert.IsType<CuiSceneHost>(session.OriginalNativeView); Assert.NotNull(view.Content);
        var preparation = Assert.IsAssignableFrom<IBrowseAppContentClosePreparation>(await session.PrepareCloseAsync());
        Assert.Same(session, preparation.Session); Assert.True(preparation.IsCurrent);
        Assert.False(rig.Route.Core!.IsPresentationAdmitted);
        await preparation.AbortAsync(); Assert.False(preparation.IsCurrent);
        Assert.True(rig.Route.Core.IsPresentationAdmitted); Assert.Same(view, session.OriginalNativeView);
        preparation = Assert.IsAssignableFrom<IBrowseAppContentClosePreparation>(await session.PrepareCloseAsync());
        var close = preparation.BeginClose(); Assert.Same(close, session.CloseAndDrainAsync()); Assert.Same(close, session.OriginalClose);
        await close; Assert.True(session.CanRetireOriginalView); Assert.False(session.IsPublicationCurrent);
        Assert.True(rig.Route.Closed); Assert.Empty(await rig.Notes.ListAsync(default));
    }

    [AvaloniaFact(SkipUnless = nameof(UsesLinuxOriginalNotesFixture), Skip = "Original canonical Notes fixture requires supported Linux; other platform acceptance is separate.")]
    public async Task Prepare_awaits_a_genuine_direct_session_action_when_the_loader_snapshot_is_empty_without_blocking_UI()
    {
        Rig? rig = null; BoardsBrowseAppContentSession? session = null; Window? window = null;
        Task? direct = null, watchdog = null; Task<IBrowseAppContentClosePreparation?>? preparing = null;
        var held = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var prepareEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var uiCheckpoint = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var bound = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using var prepareCancellation = new CancellationTokenSource(); using var stopWatchdog = new CancellationTokenSource();
        var failures = new List<Exception>(); var blocked = 0; var checkpointQueued = 0;
        try
        {
            rig = new Rig(); session = rig.Create(); await session.InitializeAsync();
            var view = Assert.IsType<CuiSceneHost>(session.OriginalNativeView);
            window = new Window { Content = view, Width = 1000, Height = 760 }; window.Show();
            var show = session.OriginalCuiShow ?? throw new InvalidOperationException("Retain actual original Show.");
            while (true)
            {
                await view.CaptureOriginalActionSettlement(show).WaitAsync(bound.Token);
                if (view.GetVisualDescendants().OfType<Control>().Any(control => control.Name == "boards-workspace")) break;
                bound.Token.ThrowIfCancellationRequested(); await Task.Yield();
            }
            rig.Route.Observation = held.Task;
            rig.Route.Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
            direct = session.DispatchAsync("Create", null, bound.Token).AsTask();
            await rig.Route.Entered.Task.WaitAsync(bound.Token);
            Assert.False(direct.IsCompleted);
            Assert.True(view.CaptureOriginalActionSettlement(show).IsCompletedSuccessfully,
                "This control requires a genuine pending direct session action outside the loader snapshot.");
            rig.Route.OnAliveCheck = () =>
            {
                if (Interlocked.CompareExchange(ref checkpointQueued, 1, 0) != 0) return;
                prepareEntered.TrySetResult();
                Avalonia.Threading.Dispatcher.UIThread.Post(() => uiCheckpoint.TrySetResult());
            };
            // An external bound makes the broken UI spin an ordinary asserted RED. It
            // cancels only the broken preparation; production must await the actual source.
            watchdog = Task.Run(async () =>
            {
                await prepareEntered.Task.WaitAsync(stopWatchdog.Token);
                try { await uiCheckpoint.Task.WaitAsync(TimeSpan.FromSeconds(5), stopWatchdog.Token); }
                catch (TimeoutException) { Interlocked.Exchange(ref blocked, 1); prepareCancellation.Cancel(); }
            });
            preparing = session.PrepareCloseAsync(prepareCancellation.Token);
            await uiCheckpoint.Task.WaitAsync(bound.Token); await watchdog;
            Assert.True(Volatile.Read(ref blocked) == 0,
                "BOARDS_DIRECT_ACTION_PREPARE_BLOCKED_UI: preparation spun on an empty loader snapshot while a genuine direct source was pending.");
            Assert.False(direct.IsCompleted); Assert.False(preparing.IsCompleted);
            Assert.Same(preparing, session.PrepareCloseAsync(prepareCancellation.Token));
            rig.Route.OnAliveCheck = null; held.SetResult(); await direct;
            var preparation = Assert.IsAssignableFrom<IBrowseAppContentClosePreparation>(await preparing);
            var saved = Assert.Single(await rig.Notes.ListAsync(bound.Token));
            Assert.True((await rig.Notes.LoadAsync(saved.Id, bound.Token))!.Version > 0);
            await preparation.BeginClose(); Assert.True(session.CanRetireOriginalView);
        }
        catch (Exception error) { failures.Add(error); }
        finally
        {
            if (rig is not null) rig.Route.OnAliveCheck = null;
            held.TrySetResult(); stopWatchdog.Cancel();
            if (watchdog is not null)
                try { await watchdog; } catch (OperationCanceledException) when (stopWatchdog.IsCancellationRequested) { }
                catch (Exception error) { failures.Add(error); }
            if (direct is not null) try { await direct; } catch (Exception error) { failures.Add(error); }
            if (preparing is not null)
                try { var accepted = await preparing; if (accepted is { IsCurrent: true }) await accepted.BeginClose(); }
                catch (Exception error) { failures.Add(error); }
            if (session is not null)
                try { await (session.OriginalClose ?? session.CloseAndDrainAsync()); } catch (Exception error) { failures.Add(error); }
            if (window is not null)
            {
                try { window.Content = null; } catch (Exception error) { failures.Add(error); }
                try { window.Close(); } catch (Exception error) { failures.Add(error); }
            }
            if (rig is not null) try { await rig.DisposeAsync(); } catch (Exception error) { failures.Add(error); }
        }
        if (failures.Count == 1) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failures[0]).Throw();
        if (failures.Count > 1) throw new AggregateException("Direct-action retirement assertion and independent cleanup failed.", failures);
    }

    [AvaloniaFact]
    public async Task Refused_actual_save_preserves_draft_view_and_version_then_real_close_commits_once()
    {
        await using var rig = new Rig(); var session = rig.Create(); await session.InitializeAsync();
        await session.DispatchAsync("Create", null); var core = rig.Route.Core!;
        var id = core.DocumentId!.Value; var view = session.OriginalNativeView;
        Assert.Equal(1, core.DurableRevision); await core.AddSectionAsync("Revision material"); rig.Saves.Refuse = true;
        Assert.Null(await session.PrepareCloseAsync()); Assert.Null(session.OriginalClose);
        Assert.True(core.IsDirty); Assert.True(core.IsPresentationAdmitted); Assert.True(session.IsPublicationCurrent);
        Assert.Same(view, session.OriginalNativeView); Assert.Equal(1, (await rig.Notes.LoadAsync(id, default))!.Version);
        rig.Saves.Refuse = false;
        var preparation = Assert.IsAssignableFrom<IBrowseAppContentClosePreparation>(await session.PrepareCloseAsync());
        Assert.Equal(2, core.DurableRevision); Assert.False(core.IsDirty);
        Assert.False((await core.AddSectionAsync("Forbidden frozen edit")).Succeeded);
        await preparation.AbortAsync(); Assert.True((await core.AddSectionAsync("Resumed revision")).Succeeded);
        preparation = Assert.IsAssignableFrom<IBrowseAppContentClosePreparation>(await session.PrepareCloseAsync());
        await preparation.BeginClose(); var saved = (await rig.Notes.LoadAsync(id, default))!;
        Assert.Equal(3, saved.Version); Assert.Contains(saved.Sections, section => section.Title == "Resumed revision");
        Assert.DoesNotContain(saved.Sections, section => section.Title == "Forbidden frozen edit");
        Assert.Single(await rig.Notes.ListAsync(default));
    }

    [AvaloniaFact]
    public async Task Held_original_observation_cannot_publish_or_finish_close_before_source_settles()
    {
        await using var rig = new Rig(); var held = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        rig.Route.Observation = held.Task; var session = rig.Create(); var initialization = session.InitializeAsync();
        await rig.Route.Entered.Task; Assert.False(session.IsPublicationCurrent);
        Assert.Throws<InvalidOperationException>(() => session.OriginalNativeView);
        var close = session.CloseAndDrainAsync(); Assert.Same(close, session.CloseAndDrainAsync());
        Assert.False(close.IsCompleted); Assert.False(initialization.IsCompleted); Assert.Null(rig.Route.Core);
        held.SetResult(); Assert.NotNull(await Record.ExceptionAsync(() => initialization));
        Assert.NotNull(await Record.ExceptionAsync(() => close)); Assert.True(rig.Route.Closed);
        Assert.False(session.CanRetireOriginalView); Assert.False(session.IsPublicationCurrent);
        Assert.Null(rig.Route.Core); Assert.Empty(await rig.Notes.ListAsync(default));
    }

    [AvaloniaFact]
    public async Task Actor_replacement_after_library_load_refuses_before_editor_publication()
    {
        await using var rig = new Rig(); rig.Route.FailObservationNumber = 2; var session = rig.Create();
        Assert.NotNull(await Record.ExceptionAsync(() => session.InitializeAsync()));
        Assert.False(session.IsPublicationCurrent); Assert.Throws<InvalidOperationException>(() => session.OriginalNativeView);
        var close = session.CloseAndDrainAsync(); Assert.NotNull(await Record.ExceptionAsync(() => close));
        Assert.True(rig.Route.Closed); Assert.False(session.CanRetireOriginalView);
        Assert.Empty(await rig.Notes.ListAsync(default));
    }

    [AvaloniaFact]
    public async Task Stale_aborted_and_accepted_preparations_cannot_restore_or_retire_another_snapshot()
    {
        await using var rig = new Rig(); var session = rig.Create(); await session.InitializeAsync();
        var stale = Assert.IsAssignableFrom<IBrowseAppContentClosePreparation>(await session.PrepareCloseAsync());
        await stale.AbortAsync(); Assert.Throws<InvalidOperationException>(() => { _ = stale.BeginClose(); });
        var current = Assert.IsAssignableFrom<IBrowseAppContentClosePreparation>(await session.PrepareCloseAsync());
        Assert.NotSame(stale, current); var close = current.BeginClose(); Assert.Same(close, current.BeginClose()); await close;
        Assert.NotNull(await Record.ExceptionAsync(() => current.AbortAsync()));
    }

    [AvaloniaFact]
    public async Task Faulted_original_cancellation_exception_keeps_exact_cause_and_cannot_be_clean_retirement()
    {
        await using var rig = new Rig(); var cause = new OperationCanceledException("Controlled faulted original observation, not a canceled task.");
        var raw = Task.FromException(cause); rig.Route.RawObservation = raw;
        var session = rig.Create(); var initialization = session.InitializeAsync();
        var failure = await Record.ExceptionAsync(() => initialization);
        Assert.NotNull(failure); Assert.True(raw.IsFaulted); Assert.True(initialization.IsFaulted);
        Assert.Contains(Causes(failure!), error => ReferenceEquals(error, cause));
        var close = session.CloseAndDrainAsync(); var closeFailure = await Record.ExceptionAsync(() => close);
        Assert.NotNull(closeFailure); Assert.Contains(Causes(closeFailure!), error => ReferenceEquals(error, cause));
        Assert.False(session.CanRetireOriginalView); Assert.True(rig.Route.Closed);
    }
    [AvaloniaFact]
    public async Task Boolean_close_preflight_does_not_leave_a_strong_lease_or_freeze_the_live_editor()
    {
        await using var rig = new Rig(); var session = rig.Create(); await session.InitializeAsync();
        var view = session.OriginalNativeView;
        Assert.True(await session.TryPrepareToCloseAsync());
        Assert.True(rig.Route.Core!.IsPresentationAdmitted); Assert.True(session.IsActionAvailable("Create"));
        Assert.Null(session.OriginalClose); Assert.Same(view, session.OriginalNativeView);
        var retained = Assert.IsAssignableFrom<IBrowseAppContentClosePreparation>(await session.PrepareCloseAsync());
        await retained.BeginClose();
    }

    [AvaloniaFact(SkipUnless = nameof(UsesLinuxOriginalNotesFixture), Skip = "Original metadata requires little-endian Linux X64/Arm64; other platform acceptance is separate.")]
    public async Task Actual_Home_actor_callback_cannot_join_its_parent_session_under_restored_pre_owner_context()
    {
        var beforeOwner = ExecutionContext.Capture() ?? throw new InvalidOperationException("Capture genuine pre-owner context.");
        using var appLifetime = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using var hostLifetime = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var principal = new ActualPrincipal();
        Rig? rig = null; HomeNativeWindowsComposition? home = null; ServiceProvider? provider = null;
        BoardsBrowseAppContentSession? session = null;
        HomeNativeApprovalWindowOwner? approvals = null; BoardsOriginalStoreSetupBackend? setup = null; Window? window = null;
        var failures = new List<Exception>();
        try
        {
            rig = new Rig();
            var evidence = new NotesLocalStoreEvidenceProvider(rig.Notes);
            home = new HomeNativeWindowsComposition(new FileHomeCoreStateStore(Path.Combine(rig.OriginalPaths.DataDirectory, "home.json")),
                principal, rig.OriginalPaths, new("9to1.home.boards.guard-test.v1"),
                configureOriginalStores: _ => new([evidence], new Dictionary<Type, object>
                { [typeof(NotesRepository)] = rig.Notes, [typeof(NotesLocalStoreEvidenceProvider)] = evidence }));
            var registrations = new ServiceCollection();
            registrations.AddSingleton<IAppPaths>(rig.OriginalPaths);
            registrations.AddSingleton<IProviderSecretStore, WindowsProviderSecretStore>();
            registrations.AddHavenOwnedWindowsHomeDomain(home, rig.OriginalPaths, principal);
            provider = registrations.BuildServiceProvider();
            await home.StartOriginalAsync();
            var actor = await home.Profiles.GetCurrentAsync(default) ?? throw new InvalidOperationException("Actual local Home profile unavailable.");
            var admission = new HomeNotesResourceAdmissionSource(rig.Notes, home.StateStore, home.Profiles,
                home.Ownership, home.Resources, home.Permissions, home.Broker);
            approvals = new HomeNativeApprovalWindowOwner(home.Runtime, home.Profiles, home.Permissions);
            setup = new BoardsOriginalStoreSetupBackend(rig.Notes, evidence, home, approvals, appLifetime.Token);
            var route = new NativeBoardsDesktopRoute(provider, home, rig.Notes,
                new VerifiedNotesRepository(rig.Notes, rig.OriginalPaths, rig.Diagnostics), admission, approvals, setup,
                actor.ProfileId, "actual-home-parent-guard", appLifetime.Token, hostLifetime.Token);
            var request = new BrowseAppContentRequest(Guid.NewGuid(), AppContentAddress.Parse("9to1://boards"), actor.ProfileId, BrowserTabPrivacy.Standard);
            session = new(request, BoardsBrowseAppContentAddress.Parse(request.Address), route,
                CancellationTokenSource.CreateLinkedTokenSource(hostLifetime.Token), () => true);
            var callbacks = 0; Exception? prepareRefusal = null, closeRefusal = null;
            bool initializationWasPending = false; Task? closeDuringCallback = null;
            principal.HoldNextObservation = true;
            principal.NextCallback = () => ExecutionContext.Run(beforeOwner.CreateCopy(), _ =>
            {
                ++callbacks;
                initializationWasPending = session.OriginalInitialization is { IsCompleted: false };
                prepareRefusal = Record.Exception(() => { _ = session.PrepareCloseAsync(); });
                closeRefusal = Record.Exception(() => { _ = session.CloseAndDrainAsync(); });
                closeDuringCallback = session.OriginalClose;
            }, null);
            // The genuine metadata-only setup can succeed without creating a Notes identity,
            // ownership or content grant. The preceding actual Home callback remains the guard target.
            var initialization = session.InitializeAsync();
            await principal.Entered.Task.WaitAsync(appLifetime.Token);
            Assert.False(initialization.IsCompleted); principal.Release.TrySetResult();
            await initialization.WaitAsync(appLifetime.Token);
            Assert.Equal(1, callbacks); Assert.True(initializationWasPending);
            Assert.IsType<InvalidOperationException>(prepareRefusal); Assert.IsType<InvalidOperationException>(closeRefusal);
            Assert.Null(closeDuringCallback);
            var view = Assert.IsType<CuiSceneHost>(session.OriginalNativeView);
            window = new Window { Content = view, Width = 1000, Height = 760 }; window.Show();
            var show = session.OriginalCuiShow ?? throw new InvalidOperationException("Retain same setup Show.");
            while (true)
            {
                appLifetime.Token.ThrowIfCancellationRequested();
                await view.CaptureOriginalActionSettlement(show).WaitAsync(appLifetime.Token);
                if (view.GetVisualDescendants().OfType<Button>().Any(button => button.Name == "boards-storage-create")) break;
                await Task.Yield();
            }
            Assert.DoesNotContain(view.GetVisualDescendants().OfType<Control>(), control => control.Name == "boards-workspace");
            Assert.Empty((await home.Permissions.GetSnapshotAsync(cancellationToken: appLifetime.Token)).Grants);
            var preparation = Assert.IsAssignableFrom<IBrowseAppContentClosePreparation>(await session.PrepareCloseAsync());
            await preparation.BeginClose().WaitAsync(appLifetime.Token);
            Assert.True(session.CanRetireOriginalView);
            Assert.False(Directory.Exists(Path.Combine(rig.OriginalPaths.DataDirectory, "Notes")));
        }
        catch (Exception error) { failures.Add(error); }
        finally
        {
            principal.NextCallback = null; principal.Release.TrySetResult();
            if (window is not null)
            {
                try { window.Content = null; } catch (Exception error) { failures.Add(error); }
                try { window.Close(); } catch (Exception error) { failures.Add(error); }
            }
            if (session is not null)
                try { await (session.OriginalClose ?? session.CloseAndDrainAsync()); } catch (Exception error) { failures.Add(error); }
            if (setup is not null) try { await setup.CloseAndDrainAsync(); } catch (Exception error) { failures.Add(error); }
            if (approvals is not null) try { await approvals.CloseAndDrainAsync(); } catch (Exception error) { failures.Add(error); }
            if (home is not null) try { await home.CloseAndDrainAsync(); } catch (Exception error) { failures.Add(error); }
            if (provider is not null) try { await provider.DisposeAsync(); } catch (Exception error) { failures.Add(error); }
            if (rig is not null) try { await rig.DisposeAsync(); } catch (Exception error) { failures.Add(error); }
        }
        if (failures.Count == 1) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failures[0]).Throw();
        if (failures.Count > 1) throw new AggregateException("Actual ancestor assertion and independent cleanup failed.", failures);
    }

    private sealed class ActualPrincipal : ITrustedHostPrincipalSource
    {
        private readonly OperatingSystemPrincipalSource _original = new();
        internal Action? NextCallback;
        internal bool HoldNextObservation;
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async ValueTask<string?> GetPrincipalAsync(CancellationToken token)
        {
            var actual = await _original.GetPrincipalAsync(token);
            if (HoldNextObservation)
            {
                HoldNextObservation = false; Entered.TrySetResult();
                await Release.Task.ConfigureAwait(false);
                return actual; // Arm the callback for the NEXT actual scoped observation after this await.
            }
            Interlocked.Exchange(ref NextCallback, null)?.Invoke();
            return actual;
        }
    }
    private static IEnumerable<Exception> Causes(Exception error)
    {
        yield return error;
        if (error is AggregateException aggregate)
            foreach (var child in aggregate.InnerExceptions) foreach (var cause in Causes(child)) yield return cause;
        else if (error.InnerException is { } inner) foreach (var cause in Causes(inner)) yield return cause;
    }

    private sealed class Rig : IAsyncDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "9to1-boards-native-" + Guid.NewGuid().ToString("N"));
        private readonly ProductionDiagnostics _diagnostics;
        internal ProductionDiagnostics Diagnostics => _diagnostics;
        internal IAppPaths OriginalPaths { get; }
        internal NotesRepository Notes { get; }
        internal Saves Saves { get; }
        internal Route Route { get; }
        internal Rig()
        {
            Directory.CreateDirectory(_root); var paths = new Paths(_root);
            OriginalPaths = paths;
            _diagnostics = new(paths);
            Notes = new(paths, new NotesDocumentValidator(), _diagnostics);
            Saves = new(Notes); Route = new(Saves);
        }
        internal BoardsBrowseAppContentSession Create()
        {
            var request = new BrowseAppContentRequest(Guid.NewGuid(), AppContentAddress.Parse("9to1://boards"), "fixture-local-profile", BrowserTabPrivacy.Standard);
            return new(request, BoardsBrowseAppContentAddress.Parse(request.Address), Route, new CancellationTokenSource(), () => true);
        }
        public async ValueTask DisposeAsync() { await _diagnostics.DisposeAsync(); Directory.Delete(_root, recursive: true); }
    }
    private sealed class Paths(string root) : IAppPaths
    {
        public string DataDirectory => root;
        public string DatabasePath => Path.Combine(root, "fixture.sqlite");
        public string BrowserProfileDirectory => Path.Combine(root, "browser");
        public string AttachmentsDirectory => Path.Combine(root, "attachments");
        public string LogsDirectory => Path.Combine(root, "logs");
        public string LegacyStatePath => Path.Combine(root, "legacy.json");
    }
    private sealed class Route(INotesRepository notes) : IBoardsOriginalDesktopRoute
    {
        internal BoardsBrowserCore? Core;
        internal bool Closed;
        internal Task Observation = Task.CompletedTask;
        internal Task? RawObservation;
        internal TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal int FailObservationNumber;
        private int _observations;
        internal Action? OnAliveCheck;
        public bool IsAlive { get { OnAliveCheck?.Invoke(); return !Closed; } }
        public BoardsBrowserCore AcquireOriginalCore() => Core = new(notes);
        public Task RevalidateAsync(CancellationToken token)
        {
            Entered.TrySetResult(); return RawObservation ?? ObserveAsync(token);
        }
        private async Task ObserveAsync(CancellationToken token)
        {
            await Observation; token.ThrowIfCancellationRequested();
            if (++_observations == FailObservationNumber) throw new UnauthorizedAccessException("Controlled actor replacement after source await.");
        }
        public void DemandPublicationCurrent() { if (Closed) throw new ObjectDisposedException(nameof(Route)); }
        public ValueTask<CuiSceneAvailability> CheckAsync(CancellationToken token)
        { token.ThrowIfCancellationRequested(); return new(new CuiSceneAvailability(CuiSceneAvailabilityState.Ready, "FixtureHost", "Deterministic host observation only; no Home grant.")); }
        public Task CloseAndDrainAsync() { Closed = true; return Task.CompletedTask; }
    }
    private sealed class Saves(INotesRepository actual) : INotesRepository
    {
        internal bool Refuse;
        public Task<NotesSaveResult> SaveAsync(NotesDocument document, string reason, CancellationToken token) => Refuse
            ? Task.FromException<NotesSaveResult>(new UnauthorizedAccessException("Controlled save refusal before commit.")) : actual.SaveAsync(document, reason, token);
        public Task<IReadOnlyList<NotesDocumentSummary>> ListAsync(CancellationToken token) => actual.ListAsync(token);
        public Task<NotesDocument?> LoadAsync(Guid id, CancellationToken token) => actual.LoadAsync(id, token);
        public Task DeleteAsync(Guid id, CancellationToken token) => actual.DeleteAsync(id, token);
        public Task<IReadOnlyList<NotesVersionInfo>> GetVersionsAsync(Guid id, CancellationToken token) => actual.GetVersionsAsync(id, token);
        public Task<NotesDocument?> LoadVersionAsync(Guid id, string version, CancellationToken token) => actual.LoadVersionAsync(id, version, token);
        public Task<NotesDocument?> RecoverLatestAsync(Guid id, CancellationToken token) => actual.RecoverLatestAsync(id, token);
        public Task<IReadOnlyList<NotesSearchHit>> SearchAsync(string query, CancellationToken token) => actual.SearchAsync(query, token);
    }
}
