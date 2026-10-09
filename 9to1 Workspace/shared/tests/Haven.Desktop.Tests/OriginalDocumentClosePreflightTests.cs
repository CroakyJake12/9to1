using Avalonia.Headless.XUnit;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.Events;
using Haven.Desktop.Services;
using Haven.Desktop.ViewModels;
using Haven.Desktop.Views.Pages.Present;
using Haven.Desktop.Views.Shell;
using Haven.Infrastructure;

namespace Haven.Desktop.Tests;

public sealed class OriginalDocumentClosePreflightTests
{
    [AvaloniaFact]
    public async Task Held_actual_save_leaves_the_tab_usable_and_later_edits_invalidate_preparation()
    {
        using var paths = new TemporaryPaths();
        var durable = new PresentRepository(paths);
        var repository = new ControlledRepository(durable);
        var page = new PresentPage(new HavenEventBus(), repository, new PresentPptxExportService());
        var tab = new WorkspaceTabViewModel("app-present", "Present", page, true, HavenSurface.Present);
        Task<bool>? preparation = null;
        Exception? primary = null;
        List<Exception> cleanup = [];
        try
        {
            await page.InitializeAsync();
            var candidate = ForTab(tab);
            page.Route.DeckTitleInput.Text = "Save before retirement";
            repository.PauseSave = true;
            preparation = candidate.PrepareAsync(CancellationToken.None);
            await repository.SaveStarted.Task;
            Assert.False(preparation.IsCompleted);
            Assert.Null(page.OriginalClose);
            Assert.False(tab.LifetimeToken.IsCancellationRequested);
            Assert.Same(page.SceneRoot, page.SceneHost.Root);
            repository.SaveRelease.SetResult();
            Assert.True(await preparation);
            Assert.True(candidate.IsCurrentAndPrepared);
            page.Route.DeckTitleInput.Text = "New edit after preparation";
            Assert.False(candidate.IsCurrentAndPrepared);
            Assert.Null(page.OriginalClose);
            Assert.True(await candidate.PrepareAsync(CancellationToken.None));
            Assert.True(candidate.IsCurrentAndPrepared);
        }
        catch (Exception failure) { primary = failure; }
        finally
        {
            repository.SaveRelease.TrySetResult();
            cleanup = await JoinTestOriginalsAsync(paths, preparation is null ? [] : [preparation], tab);
        }
        ThrowUnexpectedTestCauses(primary, cleanup);
        Assert.Equal("New edit after preparation", (await durable.LoadAsync(page.Document!.Id, CancellationToken.None))!.Title);
    }

    [AvaloniaFact]
    public async Task Declined_actual_save_allows_a_later_save_and_preserves_the_same_failure()
    {
        using var paths = new TemporaryPaths();
        var durable = new PresentRepository(paths);
        var repository = new ControlledRepository(durable);
        var page = new PresentPage(new HavenEventBus(), repository, new PresentPptxExportService());
        var tab = new WorkspaceTabViewModel("app-present", "Present", page, true, HavenSurface.Present);
        var actualFailure = new IOException("Actual pre-close repository failure");
        Task<bool>? preparation = null;
        Exception? primary = null;
        List<Exception> cleanup = [];
        try
        {
            await page.InitializeAsync();
            var candidate = ForTab(tab);
            page.Route.DeckTitleInput.Text = "Failed pre-close save";
            repository.SaveFailure = actualFailure;
            preparation = candidate.PrepareAsync(CancellationToken.None);
            Assert.False(await preparation);
            Assert.Null(page.OriginalClose);
            Assert.False(tab.LifetimeToken.IsCancellationRequested);
            Assert.Same(page.SceneRoot, page.SceneHost.Root);
            repository.SaveFailure = null;
            page.Route.DeckTitleInput.Text = "Later save remains admitted";
            Assert.True(await page.SaveAsync());
            Assert.True(await candidate.PrepareAsync(CancellationToken.None));
            Assert.True(candidate.IsCurrentAndPrepared);
            Assert.Equal("Later save remains admitted", (await durable.LoadAsync(page.Document!.Id, CancellationToken.None))!.Title);
        }
        catch (Exception failure) { primary = failure; }
        finally
        {
            repository.SaveRelease.TrySetResult();
            cleanup = await JoinTestOriginalsAsync(paths, preparation is null ? [] : [preparation], tab);
        }
        // Acknowledged retry is not a no-fault drain receipt. The exact injected
        // cause remains observed on the SAME encompassing tab retirement.
        ThrowUnexpectedTestCauses(primary, cleanup, actualFailure);
        Assert.Contains(cleanup, cause => ReferenceEquals(cause, actualFailure));
    }

    [AvaloniaFact]
    public async Task Changed_actual_history_requires_a_new_whole_cohort_preparation()
    {
        using var paths = new TemporaryPaths();
        var repository = new PresentRepository(paths);
        var first = new PresentPage(new HavenEventBus(), repository, new PresentPptxExportService());
        var second = new PresentPage(new HavenEventBus(), repository, new PresentPptxExportService());
        var tab = new WorkspaceTabViewModel("first", "First", first, true, HavenSurface.Present);
        var secondOwner = new WorkspaceTabViewModel("retained-second", "Second", second, true, HavenSurface.Present);
        Exception? primary = null;
        List<Exception> cleanup = [];
        try
        {
            await first.InitializeAsync();
            await second.InitializeAsync();
            var old = ForTab(tab);
            Assert.True(await old.PrepareAsync(CancellationToken.None));
            tab.NavigateTo("second", "Second", second, true, HavenSurface.Present);
            Assert.False(old.IsCurrentAndPrepared);
            Assert.Null(first.OriginalClose);
            Assert.Null(second.OriginalClose);
            Assert.Contains(first, tab.CaptureOriginalPageCohort());
            Assert.Contains(second, tab.CaptureOriginalPageCohort());
            var current = ForTab(tab);
            Assert.True(await current.PrepareAsync(CancellationToken.None));
            Assert.True(current.IsCurrentAndPrepared);
        }
        catch (Exception failure) { primary = failure; }
        finally { cleanup = await JoinTestOriginalsAsync(paths, [], tab, secondOwner); }
        ThrowUnexpectedTestCauses(primary, cleanup);
    }

    [AvaloniaFact]
    public async Task A_retained_page_is_skipped_only_after_its_same_successful_original_close()
    {
        using var paths = new TemporaryPaths();
        var page = new PresentPage(new HavenEventBus(), new PresentRepository(paths), new PresentPptxExportService());
        var tab = new WorkspaceTabViewModel("app-present", "Present", page, true, HavenSurface.Present);
        Task? actualClose = null;
        Exception? primary = null;
        List<Exception> cleanup = [];
        try
        {
            await page.InitializeAsync();
            actualClose = page.CloseAndDrainAsync();
            await actualClose;
            var retained = new OriginalDocumentClosePreflight(() => [page], () => true);
            Assert.True(await retained.PrepareAsync(CancellationToken.None));
            Assert.True(retained.IsCurrentAndPrepared);
            Assert.Same(actualClose, page.OriginalClose);
        }
        catch (Exception failure) { primary = failure; }
        finally { cleanup = await JoinTestOriginalsAsync(paths, actualClose is null ? [] : [actualClose], tab); }
        ThrowUnexpectedTestCauses(primary, cleanup);
    }

    private static async Task<List<Exception>> JoinTestOriginalsAsync(TemporaryPaths paths,
        IEnumerable<Task> accepted, params WorkspaceTabViewModel[] tabs)
    {
        var failures = new List<Exception>();
        var pages = tabs.SelectMany(tab => tab.CaptureOriginalPageCohort())
            .OfType<IDesktopOriginalRetirementParticipant>()
            .Distinct<IDesktopOriginalRetirementParticipant>(ReferenceEqualityComparer.Instance).ToArray();
        foreach (var actual in accepted.Distinct<Task>(ReferenceEqualityComparer.Instance))
            await JoinActualAsync(actual);
        var closes = new List<Task>();
        foreach (var tab in tabs)
        {
            try { closes.Add(tab.CloseAndDrainAsync()); }
            catch (Exception refusal)
            {
                paths.Retain = true; // No joinable original was acquired: storage remains owned.
                Add(refusal);
            }
        }
        foreach (var page in pages)
        {
            try { closes.Add(page.CloseAndDrainAsync()); }
            catch (Exception refusal)
            {
                paths.Retain = true; // Independently retain the genuine page even if tab acquisition refused.
                Add(refusal);
            }
        }
        foreach (var actual in closes.Distinct<Task>(ReferenceEqualityComparer.Instance))
            await JoinActualAsync(actual);
        return failures;

        async Task JoinActualAsync(Task actual)
        {
            try { await actual; }
            catch (Exception observed)
            {
                paths.Retain = true;
                if (actual.Exception is { InnerExceptions.Count: > 0 } group)
                    foreach (var direct in group.InnerExceptions) Add(direct);
                else Add(observed);
            }
        }
        void Add(Exception cause)
        {
            if (!failures.Any(existing => ReferenceEquals(existing, cause))) failures.Add(cause);
        }
    }

    private static void ThrowUnexpectedTestCauses(Exception? primary, IReadOnlyCollection<Exception> cleanup,
        params Exception[] expected)
    {
        var failures = cleanup.Where(cause => !expected.Any(known => ReferenceEquals(known, cause))).ToList();
        if (primary is not null && !failures.Any(cause => ReferenceEquals(cause, primary))) failures.Insert(0, primary);
        if (failures.Count == 1) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failures[0]).Throw();
        if (failures.Count != 0) throw new AggregateException("Original test body and independent retirement cleanup failed.", failures);
    }

    private static OriginalDocumentClosePreflight ForTab(WorkspaceTabViewModel tab) =>
        new(() => MainView.CaptureOriginalDocumentTabCloseSubjects(tab), () => !tab.LifetimeToken.IsCancellationRequested);

    private sealed class ControlledRepository(IPresentRepository inner) : IPresentRepository
    {
        internal bool PauseSave { get; set; }
        internal IOException? SaveFailure { get; set; }
        internal TaskCompletionSource SaveStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource SaveRelease { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<IReadOnlyList<PresentDocumentSummary>> ListAsync(CancellationToken token) => inner.ListAsync(token);
        public Task<PresentDocument?> LoadAsync(Guid id, CancellationToken token) => inner.LoadAsync(id, token);
        public Task DeleteAsync(Guid id, CancellationToken token) => inner.DeleteAsync(id, token);
        public async Task<PresentSaveResult> SaveAsync(PresentDocument document, string reason, CancellationToken token)
        {
            if (SaveFailure is { } failure) throw failure;
            if (PauseSave) { SaveStarted.TrySetResult(); await SaveRelease.Task; }
            return await inner.SaveAsync(document, reason, token);
        }
    }

    private sealed class TemporaryPaths : IAppPaths, IDisposable
    {
        public string DataDirectory { get; } = Path.Combine(Path.GetTempPath(), "document-close-preflight", Guid.NewGuid().ToString("N"));
        public string DatabasePath => Path.Combine(DataDirectory, "haven.db");
        public string BrowserProfileDirectory => Path.Combine(DataDirectory, "BrowserProfile");
        public string AttachmentsDirectory => Path.Combine(DataDirectory, "Attachments");
        public string LogsDirectory => Path.Combine(DataDirectory, "Logs");
        public string LegacyStatePath => Path.Combine(DataDirectory, "legacy.json");
        internal bool Retain { get; set; }
        public void Dispose() { if (!Retain && Directory.Exists(DataDirectory)) Directory.Delete(DataDirectory, true); }
    }
}
