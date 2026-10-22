using Avalonia.Headless.XUnit;
using CakeOS.Cui.Runtime;
using Haven.Application;
using Haven.Browser;
using Haven.Core;
using Haven.Desktop.Services;
using Haven.Infrastructure;
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
    public async Task Real_shared_CUI_has_original_tasks_and_reversible_clean_close()
    {
        using var rig = new Rig(); var session = rig.Create();
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
        using var rig = new Rig(); var session = rig.Create(); await session.InitializeAsync();
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
        using var rig = new Rig(); var held = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
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
        using var rig = new Rig(); rig.Route.FailObservationNumber = 2; var session = rig.Create();
        Assert.NotNull(await Record.ExceptionAsync(() => session.InitializeAsync()));
        Assert.False(session.IsPublicationCurrent); Assert.Throws<InvalidOperationException>(() => session.OriginalNativeView);
        var close = session.CloseAndDrainAsync(); Assert.NotNull(await Record.ExceptionAsync(() => close));
        Assert.True(rig.Route.Closed); Assert.False(session.CanRetireOriginalView);
        Assert.Empty(await rig.Notes.ListAsync(default));
    }

    [AvaloniaFact]
    public async Task Stale_aborted_and_accepted_preparations_cannot_restore_or_retire_another_snapshot()
    {
        using var rig = new Rig(); var session = rig.Create(); await session.InitializeAsync();
        var stale = Assert.IsAssignableFrom<IBrowseAppContentClosePreparation>(await session.PrepareCloseAsync());
        await stale.AbortAsync(); Assert.Throws<InvalidOperationException>(() => stale.BeginClose());
        var current = Assert.IsAssignableFrom<IBrowseAppContentClosePreparation>(await session.PrepareCloseAsync());
        Assert.NotSame(stale, current); var close = current.BeginClose(); Assert.Same(close, current.BeginClose()); await close;
        Assert.NotNull(await Record.ExceptionAsync(() => current.AbortAsync()));
    }

    private sealed class Rig : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "9to1-boards-native-" + Guid.NewGuid().ToString("N"));
        internal NotesRepository Notes { get; }
        internal Saves Saves { get; }
        internal Route Route { get; }
        internal Rig()
        {
            Directory.CreateDirectory(_root); var paths = new Paths(_root);
            Notes = new(paths, new NotesDocumentValidator(), new ProductionDiagnostics(paths));
            Saves = new(Notes); Route = new(Saves);
        }
        internal BoardsBrowseAppContentSession Create()
        {
            var request = new BrowseAppContentRequest(Guid.NewGuid(), AppContentAddress.Parse("9to1://boards"), "fixture-local-profile", BrowserTabPrivacy.Standard);
            return new(request, BoardsBrowseAppContentAddress.Parse(request.Address), Route, new CancellationTokenSource(), () => true);
        }
        public void Dispose() => Directory.Delete(_root, recursive: true);
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
        internal TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal int FailObservationNumber;
        private int _observations;
        public bool IsAlive => !Closed;
        public BoardsBrowserCore AcquireOriginalCore() => Core = new(notes);
        public async Task RevalidateAsync(CancellationToken token)
        {
            Entered.TrySetResult(); await Observation; token.ThrowIfCancellationRequested();
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
