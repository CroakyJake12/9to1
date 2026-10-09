using Avalonia.Headless.XUnit;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.Events;
using Haven.Desktop.Views.Pages.Present;
using Haven.Infrastructure;
using Haven.UI;

namespace Haven.Desktop.Tests;

public sealed class PresentPageOriginalLifetimeTests
{
    [AvaloniaFact]
    public async Task Close_preflight_saves_the_actual_unacknowledged_recovered_backup()
    {
        using var paths = new TemporaryPaths();
        var durable = new PresentRepository(paths);
        var document = PresentDocument.Create();
        await durable.SaveAsync(document, "Initial durable presentation", CancellationToken.None);
        document.Title = "Newer durable presentation";
        var newer = await durable.SaveAsync(document, "Second durable presentation", CancellationToken.None);
        await File.WriteAllTextAsync(newer.CurrentPath, "unreadable current presentation");
        var page = new PresentPage(new HavenEventBus(), durable, new PresentPptxExportService());
        await page.InitializeAsync();
        Assert.True(page.Document!.Recovery.RecoveredFromBackup);
        Assert.False(page.IsOriginalDocumentClosePrepared);
        Assert.True(await page.PrepareToCloseAsync());
        Assert.False(page.Document.Recovery.RecoveredFromBackup);
        Assert.True(page.IsOriginalDocumentClosePrepared);
        await page.CloseAndDrainAsync();
        Assert.False((await durable.LoadAsync(document.Id, CancellationToken.None))!.Recovery.RecoveredFromBackup);
    }

    [AvaloniaFact]
    public async Task Retirement_joins_the_accepted_real_save_before_destroying_the_scene()
    {
        using var paths = new TemporaryPaths();
        var durable = new PresentRepository(paths);
        var repository = new ControlledRepository(durable);
        var page = new PresentPage(new HavenEventBus(), repository, new PresentPptxExportService());
        await page.InitializeAsync();
        var id = page.Document!.Id;
        page.Route.DeckTitleInput.Text = "Retained original save";
        repository.PauseSave = true;
        var actualSave = page.SaveAsync("Lifetime regression");
        await repository.SaveStarted.Task;
        Assert.False(page.IsOriginalDocumentClosePrepared);
        Assert.False(await page.PrepareToCloseAsync());
        var actualClose = page.CloseAndDrainAsync();
        Assert.False(actualClose.IsCompleted);
        Assert.Same(page.SceneRoot, page.SceneHost.Root);
        repository.SaveRelease.SetResult();
        Assert.True(await actualSave);
        await actualClose;
        Assert.Same(actualClose, page.CloseAndDrainAsync());
        Assert.Null(page.SceneHost.Root);
        Assert.Equal("Retained original save", (await durable.LoadAsync(id, CancellationToken.None))!.Title);
        Assert.False(page.IsDirty);
    }

    [AvaloniaFact]
    public async Task A_handled_save_failure_remains_on_the_same_close_and_keeps_the_dirty_document()
    {
        using var paths = new TemporaryPaths();
        var durable = new PresentRepository(paths);
        var repository = new ControlledRepository(durable);
        var page = new PresentPage(new HavenEventBus(), repository, new PresentPptxExportService());
        await page.InitializeAsync();
        var id = page.Document!.Id;
        page.Route.DeckTitleInput.Text = "Keep failed write";
        repository.SaveFailure = new IOException("Actual injected repository write failure");
        Assert.False(await page.SaveAsync());
        Assert.False(await page.PrepareToCloseAsync());
        var observed = await Assert.ThrowsAsync<AggregateException>(() => page.CloseAndDrainAsync());
        Assert.Contains(observed.Flatten().InnerExceptions, cause => ReferenceEquals(cause, repository.SaveFailure));
        Assert.Same(page.SceneRoot, page.SceneHost.Root);
        Assert.True(page.IsDirty);
        Assert.Equal("Keep failed write", page.Document!.Title);
        Assert.Equal("Untitled presentation", (await durable.LoadAsync(id, CancellationToken.None))!.Title);
    }

    [AvaloniaFact]
    public async Task A_handled_initialization_failure_cannot_become_a_normal_initial_product()
    {
        using var paths = new TemporaryPaths();
        var repository = new ControlledRepository(new PresentRepository(paths))
        { ListFailure = new IOException("Actual injected initial library failure") };
        var page = new PresentPage(new HavenEventBus(), repository, new PresentPptxExportService());
        await page.InitializeAsync();
        Assert.False(await page.PrepareToCloseAsync());
        var failure = Assert.Throws<IOException>(page.DemandOriginalInitializedDocument);
        Assert.Same(repository.ListFailure, failure);
        var closeFailure = await Assert.ThrowsAsync<IOException>(() => page.CloseAndDrainAsync());
        Assert.Same(failure, closeFailure);
    }

    [AvaloniaFact]
    public async Task Constructor_capture_retains_the_partial_and_refuses_a_reentrant_join()
    {
        using var paths = new TemporaryPaths();
        PresentPage? partial = null;
        Assert.Throws<ObjectDisposedException>(() => new PresentPage(new HavenEventBus(),
            new PresentRepository(paths), new PresentPptxExportService(), captureOriginalOwner: page =>
            {
                partial = page;
                Assert.Throws<InvalidOperationException>(() => { _ = page.CloseAndDrainAsync(); });
                page.RequestRetirement();
            }));
        Assert.NotNull(partial);
        await partial.CloseAndDrainAsync();
    }

    [AvaloniaFact]
    public async Task Raw_save_failure_preserves_every_original_fault_sibling()
    {
        using var paths = new TemporaryPaths();
        var repository = new ControlledRepository(new PresentRepository(paths));
        var page = new PresentPage(new HavenEventBus(), repository, new PresentPptxExportService());
        await page.InitializeAsync();
        page.Route.DeckTitleInput.Text = "Preserve mixed fault evidence";
        var canceledPayload = new OperationCanceledException("Actual raw fault payload");
        var writeFailure = new IOException("Independent actual raw write sibling");
        var raw = new TaskCompletionSource<PresentSaveResult>();
        raw.SetException([canceledPayload, writeFailure]);
        repository.RawSave = raw.Task;
        Assert.False(await page.SaveAsync());
        var closeFailure = await Assert.ThrowsAsync<AggregateException>(() => page.CloseAndDrainAsync());
        Assert.Contains(closeFailure.Flatten().InnerExceptions, cause => ReferenceEquals(cause, canceledPayload));
        Assert.Contains(closeFailure.Flatten().InnerExceptions, cause => ReferenceEquals(cause, writeFailure));
        Assert.Same(page.SceneRoot, page.SceneHost.Root);
    }

    [AvaloniaFact]
    public async Task Resumed_repository_source_refuses_a_join_under_a_restored_execution_context()
    {
        using var paths = new TemporaryPaths();
        var repository = new ControlledRepository(new PresentRepository(paths));
        var initial = new TaskCompletionSource<IReadOnlyList<PresentDocumentSummary>>(TaskCreationOptions.RunContinuationsAsynchronously);
        repository.NextList = initial.Task;
        var priorContext = ExecutionContext.Capture()!;
        var page = new PresentPage(new HavenEventBus(), repository, new PresentPptxExportService());
        var refused = false;
        repository.SaveSource = () => ExecutionContext.Run(priorContext, _ =>
        {
            Assert.Throws<InvalidOperationException>(() => { _ = page.CloseAndDrainAsync(); });
            refused = true;
        }, null);
        var actualInitialize = page.InitializeAsync();
        Assert.False(actualInitialize.IsCompleted);
        initial.SetResult([]);
        await actualInitialize;
        Assert.True(refused);
        page.DemandOriginalInitializedDocument();
        await page.CloseAndDrainAsync();
    }

    [AvaloniaFact]
    public async Task Resumed_native_status_publication_refuses_a_join_under_a_restored_context()
    {
        using var paths = new TemporaryPaths();
        var repository = new ControlledRepository(new PresentRepository(paths));
        var page = new PresentPage(new HavenEventBus(), repository, new PresentPptxExportService());
        await page.InitializeAsync();
        page.Route.DeckTitleInput.Text = "Save continuation guard";
        var priorContext = ExecutionContext.Capture()!;
        var refused = false;
        page.Route.StatusText.Invalidated += (_, _) => ExecutionContext.Run(priorContext, _ =>
        {
            Assert.Throws<InvalidOperationException>(() => { _ = page.CloseAndDrainAsync(); });
            refused = true;
        }, null);
        repository.PauseSave = true;
        var actualSave = page.SaveAsync();
        await repository.SaveStarted.Task;
        repository.SaveRelease.SetResult();
        Assert.True(await actualSave);
        Assert.True(refused);
        Assert.True(await page.PrepareToCloseAsync());
        Assert.True(page.IsOriginalDocumentClosePrepared);
        await page.CloseAndDrainAsync();
    }

    [AvaloniaFact]
    public async Task Failed_current_pin_write_retains_the_unacknowledged_live_draft_and_scene()
    {
        using var paths = new TemporaryPaths();
        var durable = new PresentRepository(paths);
        var repository = new ControlledRepository(durable);
        var page = new PresentPage(new HavenEventBus(), repository, new PresentPptxExportService());
        await page.InitializeAsync();
        var id = page.Document!.Id;
        page.Route.SetLibrary(await durable.ListAsync(CancellationToken.None));
        var pin = Assert.Single(page.SceneRoot.DescendantsAndSelf().OfType<Haven.UI.Components.Button>(),
            button => button.Name == "Present.Library.Pin." + id.ToString("N"));
        repository.SaveFailure = new IOException("Actual rejected current pin write");
        var previous = page.LatestOriginalAction;
        Assert.True(pin.KeyDown(new HavenKeyInput(HavenKey.Enter, HavenKeyModifiers.None)));
        Assert.True(pin.KeyUp(new HavenKeyInput(HavenKey.Enter, HavenKeyModifiers.None)));
        var actualPin = Assert.IsAssignableFrom<Task>(page.LatestOriginalAction);
        Assert.NotSame(previous, actualPin);
        await actualPin;
        Assert.True(page.IsDirty);
        Assert.Equal("True", page.Document!.Metadata["pinned"]);
        var persisted = (await durable.LoadAsync(id, CancellationToken.None))!;
        Assert.False(persisted.Metadata.TryGetValue("pinned", out var raw) && bool.TryParse(raw, out var pinned) && pinned);
        var closeFailure = await Assert.ThrowsAsync<AggregateException>(() => page.CloseAndDrainAsync());
        Assert.Contains(closeFailure.Flatten().InnerExceptions, cause => ReferenceEquals(cause, repository.SaveFailure));
        Assert.Same(page.SceneRoot, page.SceneHost.Root);
    }

    private sealed class ControlledRepository(IPresentRepository inner) : IPresentRepository
    {
        internal Task<IReadOnlyList<PresentDocumentSummary>>? NextList { get; set; }
        internal Task<PresentSaveResult>? RawSave { get; set; }
        internal Action? SaveSource { get; set; }
        internal IOException? ListFailure { get; set; }
        internal bool PauseSave { get; set; }
        internal IOException? SaveFailure { get; set; }
        internal TaskCompletionSource SaveStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource SaveRelease { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<IReadOnlyList<PresentDocumentSummary>> ListAsync(CancellationToken token)
        {
            if (NextList is { } next) { NextList = null; return next; }
            return ListFailure is { } failure ? Task.FromException<IReadOnlyList<PresentDocumentSummary>>(failure) : inner.ListAsync(token);
        }
        public Task<PresentDocument?> LoadAsync(Guid id, CancellationToken token) => inner.LoadAsync(id, token);
        public Task DeleteAsync(Guid id, CancellationToken token) => inner.DeleteAsync(id, token);
        public Task<PresentSaveResult> SaveAsync(PresentDocument document, string reason, CancellationToken token)
        {
            SaveSource?.Invoke();
            if (RawSave is { } raw) return raw;
            if (SaveFailure is { } failure) return Task.FromException<PresentSaveResult>(failure);
            return PauseSave ? SavePausedAsync(document, reason, token) : inner.SaveAsync(document, reason, token);
        }
        private async Task<PresentSaveResult> SavePausedAsync(PresentDocument document, string reason, CancellationToken token)
        {
            SaveStarted.TrySetResult(); await SaveRelease.Task;
            return await inner.SaveAsync(document, reason, token);
        }
    }
    private sealed class TemporaryPaths : IAppPaths, IDisposable
    {
        public string DataDirectory { get; } = Path.Combine(Path.GetTempPath(), "present-original-lifetime", Guid.NewGuid().ToString("N"));
        public string DatabasePath => Path.Combine(DataDirectory, "haven.db");
        public string BrowserProfileDirectory => Path.Combine(DataDirectory, "BrowserProfile");
        public string AttachmentsDirectory => Path.Combine(DataDirectory, "Attachments");
        public string LogsDirectory => Path.Combine(DataDirectory, "Logs");
        public string LegacyStatePath => Path.Combine(DataDirectory, "legacy.json");
        public void Dispose() { if (Directory.Exists(DataDirectory)) Directory.Delete(DataDirectory, true); }
    }
}
