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
        var document = new CuiDocumentSource(reader.ReadToEnd(), "Boards.cui").Document;
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

    [AvaloniaFact]
    public async Task Actual_Home_actor_callback_cannot_join_its_parent_session_under_restored_pre_owner_context()
    {
        var beforeOwner = ExecutionContext.Capture() ?? throw new InvalidOperationException("Capture genuine pre-owner context.");
        await using var rig = new Rig();
        using var appLifetime = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using var hostLifetime = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var principal = new ActualPrincipal();
        var home = new HomeNativeWindowsComposition(new FileHomeCoreStateStore(Path.Combine(rig.OriginalPaths.DataDirectory, "home.json")),
            principal, rig.OriginalPaths, new("9to1.home.boards.guard-test.v1"));
        var registrations = new ServiceCollection();
        registrations.AddSingleton<IAppPaths>(rig.OriginalPaths);
        registrations.AddSingleton<IProviderSecretStore, WindowsProviderSecretStore>();
        registrations.AddHavenOwnedWindowsHomeDomain(home, rig.OriginalPaths, principal);
        await using var provider = registrations.BuildServiceProvider();
        BoardsBrowseAppContentSession? session = null;
        try
        {
            await home.StartOriginalAsync();
            var actor = await home.Profiles.GetCurrentAsync(default) ?? throw new InvalidOperationException("Actual local Home profile unavailable.");
            var admission = new HomeNotesResourceAdmissionSource(rig.Notes, home.StateStore, home.Profiles,
                home.Ownership, home.Resources, home.Permissions, home.Broker);
            var approvals = new HomeNativeApprovalWindowOwner(home.Runtime, home.Profiles, home.Permissions);
            var route = new NativeBoardsDesktopRoute(provider, home, rig.Notes,
                new VerifiedNotesRepository(rig.Notes, rig.OriginalPaths, rig.Diagnostics), admission, approvals,
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
            // Missing Notes ownership/identity remains an authentic admission refusal.
            // The control targets the preceding actual Home actor callback, not a fake Ready editor.
            var initialization = session.InitializeAsync();
            await principal.Entered.Task.WaitAsync(appLifetime.Token);
            Assert.False(initialization.IsCompleted); principal.Release.TrySetResult();
            var failure = await Record.ExceptionAsync(() => initialization.WaitAsync(appLifetime.Token));
            Assert.Equal(1, callbacks); Assert.True(initializationWasPending);
            Assert.IsType<InvalidOperationException>(prepareRefusal); Assert.IsType<InvalidOperationException>(closeRefusal);
            Assert.Null(closeDuringCallback);
            if (failure is not null)
            {
                Assert.False(session.IsPublicationCurrent);
                Assert.NotNull(await Record.ExceptionAsync(() => session.CloseAndDrainAsync().WaitAsync(appLifetime.Token)));
            }
            else
            {
                // A later genuine setup surface may mount without reading content.
                // This guard control does not accept that surface or grant ownership.
                var preparation = Assert.IsAssignableFrom<IBrowseAppContentClosePreparation>(await session.PrepareCloseAsync());
                await preparation.BeginClose().WaitAsync(appLifetime.Token);
            }
            Assert.False(Directory.Exists(Path.Combine(rig.OriginalPaths.DataDirectory, "Notes")));
            await approvals.CloseAndDrainAsync();
        }
        finally
        {
            principal.NextCallback = null;
            await home.CloseAndDrainAsync();
        }
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
        public bool IsAlive => !Closed;
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
