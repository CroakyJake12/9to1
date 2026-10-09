using Avalonia.Headless.XUnit;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.Events;
using Haven.Desktop.Services;
using Haven.Desktop.Views.Pages.Write;
using Haven.UI;
using Haven.UI.Components;

namespace Haven.Desktop.Tests;

/// <summary>Real Write host and scene with held original storage tasks. These tests
/// establish local custody and draft safety, not file-format or live speech acceptance.</summary>
public sealed class WritePageOriginalLifetimeTests
{
    [AvaloniaFact]
    public async Task Constructor_capture_precedes_native_publication_and_partial_owner_can_retire()
    {
        WritePage? captured = null;
        Assert.Throws<ObjectDisposedException>(() =>
        {
            _ = new WritePage(new HavenEventBus(), new Repository(), new Formats(), captureOriginalOwner: page =>
            {
                captured = page;
                Assert.Null(page.Content);
                page.RequestRetirement();
                Assert.Throws<InvalidOperationException>(() => { _ = page.CloseAndDrainAsync(); });
            });
        });
        Assert.NotNull(captured);
        await captured!.CloseAndDrainAsync();
        Assert.Null(captured.Content);
    }

    [AvaloniaFact]
    public async Task Native_constructor_publication_cannot_join_partial_owner_under_restored_context()
    {
        WritePage? captured = null;
        var neutral = ExecutionContext.Capture();
        Exception? refused = null;
        var attempted = false;
        Assert.Throws<ObjectDisposedException>(() =>
        {
            _ = new WritePage(new HavenEventBus(), new Repository(), new Formats(), captureOriginalOwner: page =>
            {
                captured = page;
                page.PropertyChanged += (_, change) =>
                {
                    if (change.Property != Avalonia.Controls.ContentControl.ContentProperty || page.Content is null || attempted) return;
                    attempted = true;
                    page.RequestRetirement();
                    ExecutionContext.Run(neutral!, state => refused = Record.Exception(() => { _ = page.CloseAndDrainAsync(); }), null);
                };
            });
        });
        Assert.True(attempted);
        Assert.IsType<InvalidOperationException>(refused);
        await captured!.CloseAndDrainAsync();
        Assert.Null(captured.Content);
    }

    [AvaloniaFact]
    public async Task Held_original_load_is_joined_and_cannot_publish_after_retirement()
    {
        var document = NotesDocument.Create("Held original load");
        var raw = NewSource<NotesDocument?>();
        var entered = NewSource<bool>();
        var repository = new Repository(document) { Load = () => { entered.TrySetResult(true); return raw.Task; } };
        var page = CreatePage(repository, document.Id);
        try
        {
            var initialize = page.InitializeAsync();
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var root = page.SceneHost.Root;
            page.RequestRetirement();
            var close = page.CloseAndDrainAsync();
            Assert.Same(close, page.CloseAndDrainAsync());
            Assert.False(close.IsCompleted);
            Assert.Same(root, page.SceneHost.Root);
            raw.SetResult(document);
            await initialize;
            await close;
            Assert.Null(page.Document);
            Assert.Null(page.SceneHost.Root);
            Assert.Null(page.Content);
            Assert.Throws<ObjectDisposedException>(() => { _ = page.InitializeAsync(); });
        }
        finally { raw.TrySetResult(document); _ = await Record.ExceptionAsync(page.CloseAndDrainAsync); }
    }

    [AvaloniaFact]
    public async Task Newer_edits_remain_dirty_when_submitted_save_finishes_during_close()
    {
        var document = NotesDocument.Create("Original title");
        var raw = NewSource<NotesSaveResult>();
        var repository = new Repository(document) { Save = _ => raw.Task };
        var page = CreatePage(repository, document.Id);
        try
        {
            await page.InitializeAsync();
            page.Route.TitleInput.Text = "Submitted title";
            var save = page.SaveAsync();
            Assert.Equal("Submitted title", repository.Submitted!.Title);
            page.Route.TitleInput.Text = "Newer retained title";
            page.RequestRetirement();
            var close = page.CloseAndDrainAsync();
            Assert.False(close.IsCompleted);
            Assert.Throws<ObjectDisposedException>(() => { _ = page.PrepareToCloseAsync(); });
            raw.SetResult(Saved(repository.Submitted));
            Assert.False(await save);
            Assert.NotNull(await Record.ExceptionAsync(() => close));
            Assert.True(page.IsDirty);
            Assert.Equal("Newer retained title", page.Document!.Title);
            Assert.NotNull(page.SceneHost.Root);
            Assert.NotNull(page.Content);
            Assert.False(page.Route.DocumentSurface.TextInput("late edit"));
        }
        finally { raw.TrySetResult(Saved(document)); _ = await Record.ExceptionAsync(page.CloseAndDrainAsync); }
    }

    [AvaloniaFact]
    public async Task Handled_save_failure_retains_every_original_task_cause_and_the_draft()
    {
        var document = NotesDocument.Create("Failure custody");
        var raw = NewSource<NotesSaveResult>();
        var repository = new Repository(document) { Save = _ => raw.Task };
        var page = CreatePage(repository, document.Id);
        var io = new IOException("original storage failure");
        var canceledPayload = new OperationCanceledException("faulted original payload");
        try
        {
            await page.InitializeAsync();
            page.Route.TitleInput.Text = "Retained unsaved title";
            var save = page.SaveAsync();
            raw.SetException([io, canceledPayload]);
            Assert.False(await save);
            var close = page.CloseAndDrainAsync();
            var failure = Assert.IsType<AggregateException>(await Record.ExceptionAsync(() => close));
            Assert.Contains(failure.InnerExceptions, error => ReferenceEquals(error, io));
            Assert.Contains(failure.InnerExceptions, error => ReferenceEquals(error, canceledPayload));
            Assert.True(page.IsDirty);
            Assert.Equal("Retained unsaved title", page.Document!.Title);
            Assert.NotNull(page.SceneHost.Root);
            Assert.NotNull(page.Content);
        }
        finally { raw.TrySetResult(Saved(document)); _ = await Record.ExceptionAsync(page.CloseAndDrainAsync); }
    }

    [AvaloniaFact]
    public async Task Close_preflight_allows_the_initialized_library_and_refuses_failed_initialization()
    {
        var library = CreatePage(new Repository(), null);
        var originalFailure = new IOException("original library read failure");
        var failed = CreatePage(new Repository { List = () => Task.FromException<IReadOnlyList<NotesDocumentSummary>>(originalFailure) }, null);
        try
        {
            Assert.False(await library.PrepareToCloseAsync());
            Assert.Throws<InvalidOperationException>(library.DemandOriginalPreparedClose);
            await library.InitializeAsync();
            Assert.Null(library.Document);
            Assert.True(await library.PrepareToCloseAsync());
            library.DemandOriginalPreparedClose();
            library.DemandOriginalInitializedDocument();
            await library.CloseAndDrainAsync();
            await failed.InitializeAsync();
            Assert.False(await failed.PrepareToCloseAsync());
            Assert.Same(originalFailure, Record.Exception(failed.DemandOriginalInitializedDocument));
        }
        finally
        {
            _ = await Record.ExceptionAsync(library.CloseAndDrainAsync);
            _ = await Record.ExceptionAsync(failed.CloseAndDrainAsync);
        }
    }

    [AvaloniaFact]
    public async Task Close_preflight_refuses_a_pending_save_without_submitting_a_second_snapshot()
    {
        var document = NotesDocument.Create("Preflight");
        var raw = NewSource<NotesSaveResult>();
        var repository = new Repository(document) { Save = _ => raw.Task };
        var page = CreatePage(repository, document.Id);
        try
        {
            await page.InitializeAsync();
            page.Route.TitleInput.Text = "Pending snapshot";
            var save = page.SaveAsync();
            Assert.False(await page.PrepareToCloseAsync());
            Assert.Throws<InvalidOperationException>(page.DemandOriginalPreparedClose);
            Assert.Equal(1, repository.SaveCalls);
            Assert.True(page.IsDirty);
            raw.SetResult(Saved(repository.Submitted!));
            Assert.True(await save);
            Assert.True(await page.PrepareToCloseAsync());
            // The final pure guard also observes accepted statistics publication.
            // Join that SAME fixture task before asserting the workspace is ready.
            if (page.LatestOriginalAction is { } statistics) await statistics;
            page.DemandOriginalPreparedClose();
            await page.CloseAndDrainAsync();
            Assert.Null(page.SceneHost.Root);
        }
        finally { raw.TrySetResult(Saved(document)); _ = await Record.ExceptionAsync(page.CloseAndDrainAsync); }
    }

    [AvaloniaFact]
    public async Task Resumed_source_callback_cannot_join_its_own_close_under_restored_execution_context()
    {
        var document = NotesDocument.Create("Physical source guard");
        var list = NewSource<IReadOnlyList<NotesDocumentSummary>>();
        var repository = new Repository(document) { List = () => list.Task };
        WritePage? page = null;
        var neutral = ExecutionContext.Capture();
        Exception? refused = null;
        repository.Load = () =>
        {
            page!.RequestRetirement();
            ExecutionContext.Run(neutral!, state => refused = Record.Exception(() => { _ = page.CloseAndDrainAsync(); }), null);
            return Task.FromResult<NotesDocument?>(document);
        };
        page = CreatePage(repository, document.Id);
        try
        {
            var initialize = page.InitializeAsync();
            list.SetResult([Summary(document)]);
            await initialize;
            Assert.IsType<InvalidOperationException>(refused);
            await page.CloseAndDrainAsync();
            Assert.Null(page.SceneHost.Root);
        }
        finally { list.TrySetResult([Summary(document)]); _ = await Record.ExceptionAsync(page.CloseAndDrainAsync); }
    }

    [AvaloniaFact]
    public async Task Retiring_a_clean_document_joins_the_actual_deferred_stats_work()
    {
        var document = NotesDocument.Create("Deferred stats custody");
        var page = CreatePage(new Repository(document), document.Id);
        var rawDelay = NewSource<bool>();
        page.StatsDelaySource = () => rawDelay.Task;
        try
        {
            await page.InitializeAsync();
            page.Route.TitleInput.Text = "Saved current title";
            Assert.True(await page.SaveAsync());
            var root = page.SceneHost.Root;
            var close = page.CloseAndDrainAsync();
            Assert.False(close.IsCompleted);
            Assert.Same(root, page.SceneHost.Root);
            Assert.False(rawDelay.Task.IsCompleted);
            rawDelay.SetResult(true);
            await close;
            Assert.Null(page.SceneHost.Root);
        }
        finally { rawDelay.TrySetResult(true); _ = await Record.ExceptionAsync(page.CloseAndDrainAsync); }
    }

    private static WritePage CreatePage(Repository repository, Guid? id) =>
        new(new HavenEventBus(), repository, new Formats(), initialDocumentId: id) { StatsDelaySource = () => Task.CompletedTask };

    [AvaloniaFact]
    public async Task Raw_faulted_speech_cancellation_and_sibling_are_retained_when_retirement_cancels_the_session()
    {
        var document = ReadableDocument();
        var rawSpeech = NewSource<bool>();
        var speech = new Speech { Playback = _ => rawSpeech.Task };
        var controller = new NotesReadAloudController(speech, new Calls(), new Diagnostics());
        var page = CreateSpeechPage(document, controller);
        var cancellationPayload = new OperationCanceledException("faulted native speech payload");
        var sibling = new IOException("second native speech failure");
        try
        {
            await page.InitializeAsync();
            StartReadAloud(page);
            await speech.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var close = page.CloseAndDrainAsync();
            await speech.StopStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(speech.PlaybackToken.IsCancellationRequested);
            Assert.False(close.IsCompleted);
            rawSpeech.SetException([cancellationPayload, sibling]);
            var failure = await Record.ExceptionAsync(() => close);
            Assert.NotNull(failure);
            Assert.Contains(OriginalCauses(failure!), cause => ReferenceEquals(cause, cancellationPayload));
            Assert.Contains(OriginalCauses(failure!), cause => ReferenceEquals(cause, sibling));
            Assert.Null(page.SceneHost.Root);
            Assert.Equal(1, speech.StopCalls);
            // The page borrowed this controller. Its own host can still use it after
            // the page joined its originals, even though those originals failed.
            speech.Playback = _ => Task.CompletedTask;
            await controller.SpeakLongFormAsync("Borrowed speech remains usable.", null, CancellationToken.None);
            Assert.Equal(2, speech.SpeakCalls);
        }
        finally
        {
            rawSpeech.TrySetResult(true);
            _ = await Record.ExceptionAsync(page.CloseAndDrainAsync);
            await controller.DisposeAsync();
        }
    }

    [AvaloniaFact]
    public async Task Correctly_canceled_native_speech_with_acknowledged_stop_closes_the_clean_document()
    {
        var document = ReadableDocument();
        var rawSpeech = NewSource<bool>();
        var speech = new Speech { Playback = _ => rawSpeech.Task };
        var controller = new NotesReadAloudController(speech, new Calls(), new Diagnostics());
        var page = CreateSpeechPage(document, controller);
        Exception? primary = null;
        List<Exception> cleanup = [];
        try
        {
            await page.InitializeAsync();
            StartReadAloud(page);
            await speech.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var root = page.SceneHost.Root;
            var close = page.CloseAndDrainAsync();
            await speech.StopStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(speech.PlaybackToken.IsCancellationRequested);
            Assert.False(close.IsCompleted);
            Assert.Same(root, page.SceneHost.Root);
            Assert.False(page.IsDirty);
            rawSpeech.SetCanceled(speech.PlaybackToken);
            Assert.True(rawSpeech.Task.IsCanceled);
            Assert.Null(rawSpeech.Task.Exception);
            await close;
            Assert.Same(close, page.CloseAndDrainAsync());
            Assert.Null(page.SceneHost.Root);
            Assert.Null(page.Content);
            Assert.False(controller.IsActive);
            Assert.Equal(1, speech.StopCalls);
            speech.Playback = _ => Task.CompletedTask;
            await controller.SpeakLongFormAsync("Borrowed speech remains usable.", null, CancellationToken.None);
            Assert.Equal(2, speech.SpeakCalls);
        }
        catch (Exception failure) { primary = failure; }
        finally
        {
            rawSpeech.TrySetResult(true);
            Task? pageClose = null, controllerClose = null;
            try { pageClose = page.CloseAndDrainAsync(); await pageClose; }
            catch (Exception failure) { RetainTestCleanupCauses(cleanup, pageClose, failure); }
            try { controllerClose = controller.DisposeAsync().AsTask(); await controllerClose; }
            catch (Exception failure) { RetainTestCleanupCauses(cleanup, controllerClose, failure); }
        }
        if (primary is not null) cleanup.Insert(0, primary);
        var distinct = cleanup.Distinct<Exception>(ReferenceEqualityComparer.Instance).ToArray();
        if (distinct.Length == 1) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(distinct[0]).Throw();
        if (distinct.Length > 1) throw new AggregateException("Actual canceled speech control and independent fixture joins failed.", distinct);
    }

    private static void RetainTestCleanupCauses(List<Exception> destination, Task? actual, Exception caught)
    {
        if (actual?.Exception is { InnerExceptions.Count: > 0 } group) destination.AddRange(group.InnerExceptions);
        else destination.Add(caught);
    }

    [AvaloniaFact]
    public async Task Canceled_document_storage_remains_an_original_failure_and_retains_the_draft()
    {
        var document = NotesDocument.Create("Unqualified canceled storage");
        var rawSave = NewSource<NotesSaveResult>();
        var repository = new Repository(document) { Save = _ => rawSave.Task };
        var page = CreatePage(repository, document.Id);
        await RunWriteFixtureAndJoinAsync(page, () => rawSave.TrySetResult(Saved(document)), async () =>
        {
            await page.InitializeAsync();
            page.Route.TitleInput.Text = "Retained canceled-save draft";
            var root = page.SceneHost.Root;
            var save = page.SaveAsync();
            var close = page.CloseAndDrainAsync();
            Assert.False(close.IsCompleted);
            rawSave.SetCanceled(new CancellationToken(canceled: true));
            Assert.True(rawSave.Task.IsCanceled);
            Assert.Null(rawSave.Task.Exception);
            Assert.False(await save);
            var failure = await Record.ExceptionAsync(() => close);
            Assert.NotNull(failure);
            Assert.Contains(OriginalCauses(failure!), cause => cause is OperationCanceledException);
            Assert.True(page.IsDirty);
            Assert.Equal("Retained canceled-save draft", page.Document!.Title);
            Assert.Same(root, page.SceneHost.Root);
            Assert.Equal(1, repository.SaveCalls);
            return failure;
        });
    }

    [AvaloniaFact]
    public async Task Canceled_native_speech_with_a_failed_stop_is_not_acknowledged()
    {
        var document = ReadableDocument();
        var rawSpeech = NewSource<bool>();
        var stopFailure = new IOException("native stop rejected canceled playback");
        var speech = new Speech { Playback = _ => rawSpeech.Task, Stop = Task.FromException(stopFailure) };
        var controller = new NotesReadAloudController(speech, new Calls(), new Diagnostics());
        var page = CreateSpeechPage(document, controller);
        await RunWriteFixtureAndJoinAsync(page, () => rawSpeech.TrySetResult(true), async () =>
        {
            await page.InitializeAsync();
            StartReadAloud(page);
            await speech.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var root = page.SceneHost.Root;
            var close = page.CloseAndDrainAsync();
            await speech.StopStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(controller.IsActive);
            rawSpeech.SetCanceled(speech.PlaybackToken);
            Assert.True(rawSpeech.Task.IsCanceled);
            Assert.Null(rawSpeech.Task.Exception);
            var failure = await Record.ExceptionAsync(() => close);
            Assert.NotNull(failure);
            Assert.Contains(OriginalCauses(failure!), cause => ReferenceEquals(cause, stopFailure));
            Assert.Contains(OriginalCauses(failure!), cause => cause is OperationCanceledException);
            Assert.Same(document, page.Document);
            Assert.Same(root, page.SceneHost.Root);
            Assert.False(page.IsOriginalDocumentClosePrepared);
            return failure;
        }, controller);
    }

    private static async Task RunWriteFixtureAndJoinAsync(WritePage page, Action release,
        Func<Task<Exception?>> body, NotesReadAloudController? controller = null)
    {
        Exception? primary = null, acknowledgedPageFailure = null;
        List<Exception> cleanup = [];
        try { acknowledgedPageFailure = await body(); }
        catch (Exception failure) { primary = failure; }
        finally
        {
            release();
            Task? pageClose = null, controllerClose = null;
            try { pageClose = page.CloseAndDrainAsync(); await pageClose; }
            catch (Exception failure)
            {
                // Only the SAME terminal close cause already asserted by the body
                // is expected. Independent or unobserved cleanup causes remain failures.
                if (!ReferenceEquals(failure, acknowledgedPageFailure)) RetainTestCleanupCauses(cleanup, pageClose, failure);
            }
            if (controller is not null)
                try { controllerClose = controller.DisposeAsync().AsTask(); await controllerClose; }
                catch (Exception failure) { RetainTestCleanupCauses(cleanup, controllerClose, failure); }
        }
        if (primary is not null) cleanup.Insert(0, primary);
        var distinct = cleanup.Distinct<Exception>(ReferenceEqualityComparer.Instance).ToArray();
        if (distinct.Length == 1) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(distinct[0]).Throw();
        if (distinct.Length > 1) throw new AggregateException("Write source control and independent fixture joins failed.", distinct);
    }

    [AvaloniaFact]
    public async Task A_faulted_source_reusing_the_acknowledged_speech_cancellation_object_remains_failed()
    {
        var releaseSpeech = NewSource<bool>();
        var unrelatedSource = NewSource<bool>();
        var originalCancellation = new TaskCanceledException("Actual canceled playback observation reused by a faulted source.");
        Task? rawPlayback = null, actualStop = null;
        // Async task cancellation retains the SAME thrown object, unlike an
        // unrelated faulted Task containing that very object below.
        async Task ExactPlayback(CancellationToken token)
        {
            await releaseSpeech.Task;
            Assert.True(token.IsCancellationRequested);
            throw originalCancellation;
        }
        var speech = new Speech { Playback = token => rawPlayback = ExactPlayback(token) };
        var controller = new NotesReadAloudController(speech, new Calls(), new Diagnostics());
        var lifetime = new DesktopOriginalWorkLifetime(
            () => actualStop = controller.StopOriginalAsync(CancellationToken.None, source => source()),
            () => Task.CompletedTask);
        Exception? primary = null, acknowledgedCloseFailure = null;
        List<Exception> cleanup = [];
        try
        {
            var playback = lifetime.RunAsync(original => controller.SpeakOriginalLongFormAsync(
                "Actual local playback.", null, original.Token,
                source => original.AwaitAsync(source()), original.QualifyOriginalSpeechSource));
            var unrelated = lifetime.RunAsync(original => original.AwaitAsync(unrelatedSource.Task));
            await speech.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var close = lifetime.CloseAndDrainAsync();
            await speech.StopStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await actualStop!;
            Assert.True(actualStop.IsCompletedSuccessfully);
            releaseSpeech.SetResult(true);
            await playback;
            Assert.True(rawPlayback!.IsCanceled);
            Assert.Null(rawPlayback.Exception);
            Assert.Same(originalCancellation, await Record.ExceptionAsync(() => rawPlayback));
            unrelatedSource.SetException(originalCancellation);
            Assert.True(unrelatedSource.Task.IsFaulted);
            Assert.Same(originalCancellation, Assert.Single(unrelatedSource.Task.Exception!.InnerExceptions));
            _ = await Record.ExceptionAsync(() => unrelated);
            acknowledgedCloseFailure = await Record.ExceptionAsync(() => close);
            Assert.NotNull(acknowledgedCloseFailure);
            Assert.Contains(OriginalCauses(acknowledgedCloseFailure!), cause => ReferenceEquals(cause, originalCancellation));
        }
        catch (Exception failure) { primary = failure; }
        finally
        {
            releaseSpeech.TrySetResult(true);
            unrelatedSource.TrySetResult(true);
            Task? close = null, controllerClose = null;
            try { close = lifetime.CloseAndDrainAsync(); await close; }
            catch (Exception failure)
            {
                if (!ReferenceEquals(failure, acknowledgedCloseFailure)) RetainTestCleanupCauses(cleanup, close, failure);
            }
            try { controllerClose = controller.DisposeAsync().AsTask(); await controllerClose; }
            catch (Exception failure) { RetainTestCleanupCauses(cleanup, controllerClose, failure); }
        }
        if (primary is not null) cleanup.Insert(0, primary);
        var distinct = cleanup.Distinct<Exception>(ReferenceEqualityComparer.Instance).ToArray();
        if (distinct.Length == 1) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(distinct[0]).Throw();
        if (distinct.Length > 1) throw new AggregateException("Exact canceled-speech alias control and independent joins failed.", distinct);
    }

    [AvaloniaFact]
    public async Task Unacknowledged_native_stop_retains_the_clean_document_scene_after_controller_active_flag_clears()
    {
        var document = ReadableDocument();
        var rawSpeech = NewSource<bool>();
        var stopFailure = new IOException("original native stop was not acknowledged");
        var speech = new Speech { Playback = _ => rawSpeech.Task, Stop = Task.FromException(stopFailure) };
        var controller = new NotesReadAloudController(speech, new Calls(), new Diagnostics());
        var page = CreateSpeechPage(document, controller);
        try
        {
            await page.InitializeAsync();
            StartReadAloud(page);
            await speech.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var root = page.SceneHost.Root;
            var close = page.CloseAndDrainAsync();
            await speech.StopStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(controller.IsActive);
            Assert.False(close.IsCompleted);
            rawSpeech.SetResult(true);
            var failure = await Record.ExceptionAsync(() => close);
            Assert.NotNull(failure);
            Assert.Contains(OriginalCauses(failure!), cause => ReferenceEquals(cause, stopFailure));
            Assert.False(page.IsDirty);
            Assert.Same(document, page.Document);
            Assert.Same(root, page.SceneHost.Root);
            Assert.NotNull(page.Content);
            Assert.False(page.IsOriginalDocumentClosePrepared);
        }
        finally
        {
            rawSpeech.TrySetResult(true);
            _ = await Record.ExceptionAsync(page.CloseAndDrainAsync);
            await controller.DisposeAsync();
        }
    }

    private static NotesDocument ReadableDocument()
    {
        var document = NotesDocument.Create("Original speech custody");
        document.Sections[0].Pages[0].Blocks = [NotesBlock.CreateParagraph("Read this retained original paragraph aloud.")];
        return document;
    }

    private static WritePage CreateSpeechPage(NotesDocument document, NotesReadAloudController controller) =>
        new(new HavenEventBus(), new Repository(document), new Formats(), initialDocumentId: document.Id, readAloud: controller)
        { StatsDelaySource = () => Task.CompletedTask };

    private static void StartReadAloud(WritePage page)
    {
        Press(page.Route.ReviewTab);
        var start = Assert.Single(page.SceneRoot.DescendantsAndSelf().OfType<Button>(), button => button.Name == "Write.Review.ReadAloud.Start");
        Press(start);
    }

    private static void Press(Button button)
    {
        var key = new HavenKeyInput(HavenKey.Enter, HavenKeyModifiers.None);
        Assert.True(button.KeyDown(key));
        Assert.True(button.KeyUp(key));
    }

    private static IEnumerable<Exception> OriginalCauses(Exception failure)
    {
        yield return failure;
        if (failure is AggregateException group)
            foreach (var nested in group.InnerExceptions)
                foreach (var cause in OriginalCauses(nested)) yield return cause;
    }

    private static TaskCompletionSource<T> NewSource<T>() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static NotesSaveResult Saved(NotesDocument document) => new(document.Id, document.Version + 1,
        DateTimeOffset.UtcNow, "snapshot-hash", "current.json", "version.json");
    private static NotesDocumentSummary Summary(NotesDocument document) => new(document.Id, document.Title,
        document.UpdatedAt, document.Version, document.Sections.Count, 1, 0, document.Recovery.HasUnsavedRecovery);

    private sealed class Repository(params NotesDocument[] documents) : INotesRepository
    {
        public Func<Task<IReadOnlyList<NotesDocumentSummary>>>? List { get; init; }
        public Func<Task<NotesDocument?>>? Load { get; set; }
        public Func<NotesDocument, Task<NotesSaveResult>>? Save { get; init; }
        public NotesDocument? Submitted { get; private set; }
        public int SaveCalls { get; private set; }
        public Task<IReadOnlyList<NotesDocumentSummary>> ListAsync(CancellationToken cancellationToken) =>
            List?.Invoke() ?? Task.FromResult<IReadOnlyList<NotesDocumentSummary>>(documents.Select(Summary).ToArray());
        public Task<NotesDocument?> LoadAsync(Guid id, CancellationToken cancellationToken) =>
            Load?.Invoke() ?? Task.FromResult(documents.FirstOrDefault(document => document.Id == id));
        public Task<NotesSaveResult> SaveAsync(NotesDocument document, string reason, CancellationToken cancellationToken)
        { ++SaveCalls; Submitted = document; return Save?.Invoke(document) ?? Task.FromResult(Saved(document)); }
        public Task DeleteAsync(Guid id, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<IReadOnlyList<NotesVersionInfo>> GetVersionsAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<NotesVersionInfo>>([]);
        public Task<NotesDocument?> LoadVersionAsync(Guid id, string version, CancellationToken cancellationToken) => LoadAsync(id, cancellationToken);
        public Task<NotesDocument?> RecoverLatestAsync(Guid id, CancellationToken cancellationToken) => LoadAsync(id, cancellationToken);
        public Task<IReadOnlyList<NotesSearchHit>> SearchAsync(string query, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<NotesSearchHit>>([]);
    }

    private sealed class Formats : INotesImportExportService
    {
        public IReadOnlyList<string> ImportExtensions => [".md"];
        public IReadOnlyList<string> ExportExtensions => [".md"];
        public Task<NotesDocument> ImportAsync(string path, CancellationToken cancellationToken) =>
            Task.FromResult(NotesDocument.Create("Imported"));
        public Task<string> ExportAsync(NotesDocument document, string path, CancellationToken cancellationToken) => Task.FromResult(path);
        public Task PrintAsync(NotesDocument document, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class Speech : ISpeechOutputService
    {
        public Func<CancellationToken, Task> Playback { get; set; } = _ => Task.CompletedTask;
        public Task Stop { get; init; } = Task.CompletedTask;
        public TaskCompletionSource<bool> Started { get; } = NewSource<bool>();
        public TaskCompletionSource<bool> StopStarted { get; } = NewSource<bool>();
        public CancellationToken PlaybackToken { get; private set; }
        public int SpeakCalls { get; private set; }
        public int StopCalls { get; private set; }
        public bool IsAvailable => true;
        public string? UnavailableReason => null;
        public IReadOnlyList<CallVoice> Voices => [];
        public IReadOnlyList<CallAudioDevice> Devices => [new("default-output", "Local test output", true)];
        public Task SpeakAsync(string text, string? voiceName, string? outputDeviceId, CancellationToken cancellationToken)
        {
            ++SpeakCalls;
            PlaybackToken = cancellationToken;
            Started.TrySetResult(true);
            return Playback(cancellationToken);
        }
        public Task StopAsync(CancellationToken cancellationToken)
        { ++StopCalls; StopStarted.TrySetResult(true); return Stop; }
    }

    private sealed class Calls : ICallCoordinator
    {
        public CallState State => CallState.Idle;
        public CallSession? CurrentSession => null;
        public Conversation? CurrentConversation => null;
        public CallCapabilities Capabilities { get; } = new(false, false, false, null, null, null, [], [], []);
        public bool IsActive => false;
        public bool IsMuted => false;
        public bool IsScreenSharing => false;
        public event EventHandler<CallStateChangedEventArgs>? StateChanged { add { } remove { } }
        public event EventHandler<CallTranscriptEventArgs>? TranscriptChanged { add { } remove { } }
        public event EventHandler<CallAudioLevelEventArgs>? AudioLevelChanged { add { } remove { } }
        public event EventHandler<ScreenShareSnapshotEventArgs>? ScreenPreviewChanged { add { } remove { } }
        public Task<CallSession> StartAsync(CallStartOptions options, SpeechModelInfo? model, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task SubmitTextAsync(string text, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task BeginPushToTalkAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task EndPushToTalkAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task SetMutedAsync(bool muted, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task PauseAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task ResumeAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task StartScreenShareAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task StopScreenShareAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task InterruptAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task EndAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class Diagnostics : IProductionDiagnostics
    {
        public ValueTask WriteAsync(ReliabilitySeverity severity, string component, string eventName, string message,
            IReadOnlyDictionary<string, string>? data = null, string? correlationId = null, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public Task<IReadOnlyList<ReliabilityEvent>> ReadRecentAsync(int limit, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ReliabilityEvent>>([]);
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
