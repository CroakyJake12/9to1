using Avalonia.Headless.XUnit;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Avalonia;
using Haven.Application;
using Haven.Core;
using Haven.Desktop.Events;
using Haven.Desktop.HavenUI.Backend;
using Haven.Desktop.Views.Pages.Write;
using Haven.Infrastructure;
using Haven.UI.Components;
using HavenOS.Apps.Canvas;
using HavenOS.Home.Core;
using System.Text.Json;

namespace HavenOS.Apps.Write.Tests;

public sealed class WriteAppWorkflowTests
{
    [AvaloniaFact]
    public async Task Close_persists_real_local_document_and_a_new_host_reopens_its_identity()
    {
        using var paths = new TemporaryWritePaths();
        var services = new DocumentServices(paths);
        var document = NotesDocument.Create("Before close");
        await services.Repository.SaveAsync(document, "Seed original identity", default);

        await using (var host = WriteAppHost.Create(services.Repository, services.Formats, document.Id))
        {
            await host.InitializeAsync();
            Title(host).Text = "Saved by standalone Write";
            Assert.True(host.Page.IsDirty);
            Assert.True(await host.TrySaveBeforeCloseAsync());
            Assert.False(host.Page.IsDirty);
        }

        var reopenedServices = new DocumentServices(paths);
        await using var reopened = WriteAppHost.Create(reopenedServices.Repository, reopenedServices.Formats, document.Id);
        await reopened.InitializeAsync();
        Assert.Equal(document.Id, reopened.Document!.Id);
        Assert.Equal("Saved by standalone Write", reopened.Document.Title);
        Assert.Equal(2, reopened.Document.Version);
    }

    [AvaloniaFact]
    public async Task Host_body_edit_saves_and_reopens_with_shared_drawing_document_and_object_identity()
    {
        using var paths = new TemporaryWritePaths();
        var services = new DocumentServices(paths);
        var engine = InkEngine();
        var seed = new WriteDocumentEditor(NotesDocument.Create("Identity roundtrip"));
        var paragraph = seed.InsertBlock(NotesBlockKind.Paragraph);
        paragraph.PlainText = "Original body paragraph.";
        var drawing = seed.InsertSharedInk();
        var bundle = InkBundle(engine, seed, drawing.Id, 80);
        Assert.True(seed.AppendSharedInk(seed.CaptureSharedInk(drawing.Id)!, bundle,
            source => CanvasSharedInkStrokeFactory.CaptureBundle(engine, InkContext(seed), source)));
        await services.Repository.SaveAsync(seed.Document, "Seed host identity roundtrip", default);
        var savedSeed = Assert.IsType<NotesDocument>(await services.Repository.LoadAsync(seed.Document.Id, default));
        var seedVersion = savedSeed.Version;
        var objectId = engine.ParseBundle(bundle.GetRawText()).Objects.Single().ObjectId;
        const string changedBody = "Edited through the real Write document surface.";

        await using (var host = WriteAppHost.Create(services.Repository, services.Formats, savedSeed.Id))
        {
            Assert.Equal(WriteDocumentEngineNegotiation.Unavailable, host.DocumentEngineBootstrap.Negotiation);
            Assert.False(host.DocumentEngineBootstrap.IsConnectedToEditor);
            await host.InitializeAsync();
            var window = new Window { Content = host.Page, Width = 1400, Height = 1200 };
            try
            {
                window.Show(); window.UpdateLayout();
                var inputName = $"Write.Word.Block.{paragraph.Id:N}.Input";
                var body = Assert.Single(window.GetVisualDescendants().OfType<Input>(), value => value.Name == inputName);
                Assert.Equal("Original body paragraph.", body.Text);
                body.Text = changedBody;
                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => { }, Avalonia.Threading.DispatcherPriority.Background);
                Assert.Equal(changedBody, host.Document!.Sections.SelectMany(section => section.Pages).SelectMany(page => page.Blocks)
                    .Single(block => block.Id == paragraph.Id).PlainText);
                Assert.True(host.Page.IsDirty);
                Assert.True(await host.TrySaveBeforeCloseAsync());
                Assert.False(host.Page.IsDirty);
                Assert.Equal(seedVersion + 1, host.Document.Version);
            }
            finally { window.Content = null; window.Close(); }
        }

        var reopenedServices = new DocumentServices(paths);
        await using var reopened = WriteAppHost.Create(reopenedServices.Repository, reopenedServices.Formats, savedSeed.Id);
        await reopened.InitializeAsync();
        var reopenedWindow = new Window { Content = reopened.Page, Width = 1400, Height = 1200 };
        try
        {
            reopenedWindow.Show(); reopenedWindow.UpdateLayout();
            Assert.Equal(savedSeed.Id, reopened.Document!.Id);
            Assert.Equal(seedVersion + 1, reopened.Document.Version);
            Assert.False(reopened.Page.IsDirty);
            var reopenedParagraph = Assert.Single(reopenedWindow.GetVisualDescendants().OfType<Input>(), value => value.Name == $"Write.Word.Block.{paragraph.Id:N}.Input");
            Assert.Equal(changedBody, reopenedParagraph.Text);
            var reopenedDrawing = Assert.Single(reopened.Document.Sections.SelectMany(section => section.Pages)
                .SelectMany(page => page.Blocks), block => block.Id == drawing.Id);
            Assert.Collection(reopenedDrawing.SharedProductivityBundles!, value => Assert.True(JsonElement.DeepEquals(bundle, value)));
            Assert.Equal(objectId, engine.ParseBundle(Assert.Single(reopenedDrawing.SharedProductivityBundles!).GetRawText()).Objects.Single().ObjectId);
            Assert.Single(reopenedWindow.GetVisualDescendants().OfType<CanvasSharedInkInput>());
        }
        finally { reopenedWindow.Content = null; reopenedWindow.Close(); }
    }

    [AvaloniaFact]
    public async Task Standalone_bootstrap_reports_engine_owner_capabilities_and_unwired_state_without_calling_candidate()
    {
        using var paths = new TemporaryWritePaths();
        var services = new DocumentServices(paths);
        var document = NotesDocument.Create("Engine bootstrap remains optional");
        await services.Repository.SaveAsync(document, "Seed bootstrap document", default);
        var complete = new BootstrapProbeWriteEngine(new("probe-only", "test", true, true, true, true, true, false));

        await using (var host = WriteAppHost.Create(services.Repository, services.Formats, document.Id,
            documentEngine: complete, documentEngineOwnerId: "test-caller"))
        {
            Assert.Same(complete, host.DocumentEngine);
            Assert.Equal("test-caller", host.DocumentEngineBootstrap.OwnerId);
            Assert.Equal(WriteDocumentEngineLifetimeOwner.Caller, host.DocumentEngineBootstrap.LifetimeOwner);
            Assert.Equal(WriteDocumentEngineNegotiation.CapabilityReadyButSurfaceUnwired,
                host.DocumentEngineBootstrap.Negotiation);
            Assert.False(host.DocumentEngineBootstrap.IsConnectedToEditor);
            await host.InitializeAsync();
            Assert.Equal(document.Id, host.Document!.Id);
            Assert.Equal(0, complete.OpenCalls);
            await host.DisposeAsync();
            Assert.Equal(0, complete.DisposeCalls); // Caller ownership is not stolen by the host.
        }
        await complete.DisposeAsync();
        Assert.Equal(1, complete.DisposeCalls);

        var incomplete = new BootstrapProbeWriteEngine(new("probe-incomplete", "test", false, true, true, true, true, false));
        await using (var host = WriteAppHost.Create(services.Repository, services.Formats, document.Id,
            documentEngine: incomplete, documentEngineOwnerId: "test-owner"))
        {
            Assert.Equal(WriteDocumentEngineNegotiation.RejectedCapabilities, host.DocumentEngineBootstrap.Negotiation);
            Assert.Contains("tiled rendering", host.DocumentEngineBootstrap.MissingCapabilities);
            Assert.False(host.DocumentEngineBootstrap.IsConnectedToEditor);
            await host.InitializeAsync();
            Assert.Equal(document.Id, host.Document!.Id); // Notes storage remains usable despite engine refusal.
            Assert.Equal(0, incomplete.OpenCalls);
        }
        await incomplete.DisposeAsync();
    }

    [AvaloniaFact]
    public async Task Storage_refusal_preserves_draft_and_retry_saves_before_close()
    {
        using var paths = new TemporaryWritePaths();
        var services = new DocumentServices(paths);
        var document = NotesDocument.Create("Stored title");
        await services.Repository.SaveAsync(document, "Seed original identity", default);
        var repository = new ControlledNotesRepository(services.Repository);
        await using var host = WriteAppHost.Create(repository, services.Formats, document.Id);
        await host.InitializeAsync();
        Title(host).Text = "Keep this draft";
        repository.FailSaves = true;

        Assert.False(await host.TrySaveBeforeCloseAsync());
        Assert.True(host.Page.IsDirty);
        Assert.Equal(document.Id, host.Document!.Id);
        Assert.Equal("Keep this draft", host.Document.Title);
        Assert.Equal("Stored title", (await services.Repository.LoadAsync(document.Id, default))!.Title);

        repository.FailSaves = false;
        Assert.True(await host.TrySaveBeforeCloseAsync());
        Assert.Equal("Keep this draft", (await services.Repository.LoadAsync(document.Id, default))!.Title);
    }

    [AvaloniaFact]
    public async Task Ink_erasure_and_undo_survive_save_refusals_and_retry_reopens_the_restored_revision()
    {
        using var paths = new TemporaryWritePaths();
        var services = new DocumentServices(paths);
        var engine = InkEngine();
        var seedEditor = new WriteDocumentEditor(NotesDocument.Create("Durable ink history"));
        var drawing = seedEditor.InsertSharedInk();
        var first = InkBundle(engine, seedEditor, drawing.Id, 40);
        var second = InkBundle(engine, seedEditor, drawing.Id, 220);
        foreach (var bundle in new[] { first, second })
            Assert.True(seedEditor.AppendSharedInk(seedEditor.CaptureSharedInk(drawing.Id)!, bundle,
                value => CanvasSharedInkStrokeFactory.CaptureBundle(engine, InkContext(seedEditor), value)));
        await services.Repository.SaveAsync(seedEditor.Document, "Seed durable ink history", default);
        var durableSeed = Assert.IsType<NotesDocument>(await services.Repository.LoadAsync(seedEditor.Document.Id, default));
        var seedVersion = durableSeed.Version;
        var firstId = engine.ParseBundle(first.GetRawText()).Objects.Single().ObjectId;
        var secondId = engine.ParseBundle(second.GetRawText()).Objects.Single().ObjectId;
        var repository = new ControlledNotesRepository(services.Repository);
        var failure = new IOException("Controlled refusal before original repository publication.");
        repository.SaveFailure = failure;

        await using (var host = WriteAppHost.Create(repository, services.Formats, durableSeed.Id,
            null, null, null, null, null, engine))
        {
            await host.InitializeAsync();
            var window = new Window { Content = host.Page, Width = 1400, Height = 1200 };
            try
            {
                window.Show(); window.UpdateLayout();
                var input = Assert.Single(window.GetVisualDescendants().OfType<CanvasSharedInkInput>());
                Avalonia.Point At(double x, double y) => input.TranslatePoint(
                    new(x / 960 * input.Bounds.Width, y / 540 * input.Bounds.Height), window)!.Value;
                window.MouseDown(At(800, 400), Avalonia.Input.MouseButton.Left);
                window.MouseUp(At(800, 400), Avalonia.Input.MouseButton.Left);
                if (input.PendingSubmission is { } selection) await selection;
                window.UpdateLayout();
                async Task Click(string label)
                {
                    var button = Assert.Single(window.GetVisualDescendants().OfType<Button>(), value => Equals(value.Content, label));
                    Assert.True(button.IsEnabled);
                    var point = button.TranslatePoint(new(button.Bounds.Width / 2, button.Bounds.Height / 2), window)!.Value;
                    window.MouseDown(point, Avalonia.Input.MouseButton.Left);
                    window.MouseUp(point, Avalonia.Input.MouseButton.Left);
                    await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => { }, Avalonia.Threading.DispatcherPriority.Background);
                }

                await Click("Eraser");
                repository.FailSaves = true;
                window.MouseDown(At(100, 20), Avalonia.Input.MouseButton.Left);
                window.MouseMove(At(100, 60), Avalonia.Input.RawInputModifiers.LeftMouseButton);
                window.MouseUp(At(100, 60), Avalonia.Input.MouseButton.Left);
                Assert.NotNull(input.PendingSubmission);
                await input.PendingSubmission;

                Assert.Same(failure, Assert.Single(repository.Failures));
                Assert.True(host.Page.IsDirty);
                Assert.Equal(seedVersion, host.Document!.Version);
                Assert.Contains("UnsavedDrawing", input.LastError!.Message, StringComparison.Ordinal);
                Assert.Contains("remains in this draft", WriteStatus(host), StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("Saved locally", WriteStatus(host), StringComparison.OrdinalIgnoreCase);
                var erased = host.Document!.Sections.SelectMany(section => section.Pages).SelectMany(page => page.Blocks)
                    .Single(block => block.Id == drawing.Id).SharedProductivityBundles!;
                Assert.Collection(erased, bundle => Assert.True(JsonElement.DeepEquals(second, bundle)));
                Assert.Equal(secondId, engine.ParseBundle(Assert.Single(erased).GetRawText()).Objects.Single().ObjectId);
                var afterEraseFailure = Assert.IsType<NotesDocument>(await services.Repository.LoadAsync(durableSeed.Id, default));
                Assert.Equal(seedVersion, afterEraseFailure.Version);
                Assert.Collection(afterEraseFailure.Sections.SelectMany(section => section.Pages).SelectMany(page => page.Blocks)
                    .Single(block => block.Id == drawing.Id).SharedProductivityBundles!,
                    bundle => Assert.True(JsonElement.DeepEquals(first, bundle)),
                    bundle => Assert.True(JsonElement.DeepEquals(second, bundle)));

                Assert.False(await host.TrySaveBeforeCloseAsync());
                Assert.True(host.Page.IsDirty);
                Assert.Equal(seedVersion, host.Document!.Version);
                Assert.Equal(2, repository.Failures.Count);
                Assert.Contains(failure.Message, WriteStatus(host), StringComparison.Ordinal);
                Assert.DoesNotContain("Saved locally", WriteStatus(host), StringComparison.OrdinalIgnoreCase);

                await Click("Undo");
                await repository.ThirdFailure.Task.WaitAsync(TimeSpan.FromSeconds(10));
                var undoneBundles = host.Document.Sections.SelectMany(section => section.Pages).SelectMany(page => page.Blocks)
                    .Single(block => block.Id == drawing.Id).SharedProductivityBundles!;
                Assert.Collection(undoneBundles,
                    bundle => Assert.True(JsonElement.DeepEquals(first, bundle)),
                    bundle => Assert.True(JsonElement.DeepEquals(second, bundle)));
                Assert.Equal(new[] { firstId, secondId }, undoneBundles.SelectMany(bundle => engine.ParseBundle(bundle.GetRawText()).Objects)
                    .Select(value => value.ObjectId).ToArray());
                Assert.True(host.Page.IsDirty);
                Assert.Equal(seedVersion, host.Document!.Version);
                Assert.Equal(3, repository.Failures.Count);
                Assert.Same(failure, repository.Failures[2]);
                var status = WriteStatus(host);
                Assert.Contains(failure.Message, status, StringComparison.Ordinal);
                Assert.DoesNotContain("Saved locally", status, StringComparison.OrdinalIgnoreCase);
                var afterUndoFailure = Assert.IsType<NotesDocument>(await services.Repository.LoadAsync(durableSeed.Id, default));
                Assert.Equal(seedVersion, afterUndoFailure.Version);
                Assert.Collection(afterUndoFailure.Sections.SelectMany(section => section.Pages).SelectMany(page => page.Blocks)
                    .Single(block => block.Id == drawing.Id).SharedProductivityBundles!,
                    bundle => Assert.True(JsonElement.DeepEquals(first, bundle)),
                    bundle => Assert.True(JsonElement.DeepEquals(second, bundle)));
                Assert.Equal(new[] { firstId, secondId }, host.Document.Sections.SelectMany(section => section.Pages)
                    .SelectMany(page => page.Blocks).Single(block => block.Id == drawing.Id).SharedProductivityBundles!
                    .SelectMany(bundle => engine.ParseBundle(bundle.GetRawText()).Objects).Select(value => value.ObjectId).ToArray());

                repository.FailSaves = false;
                Assert.True(await host.Page.SaveAsync("Retry restored ink after refusal"));
                Assert.False(host.Page.IsDirty);
                Assert.Equal(seedVersion + 1, host.Document!.Version);
                Assert.Equal(seedVersion + 1, (await services.Repository.LoadAsync(durableSeed.Id, default))!.Version);
                Assert.True(await host.TrySaveBeforeCloseAsync());
            }
            finally { window.Content = null; window.Close(); }
        }

        var reopenedServices = new DocumentServices(paths);
        await using var reopened = WriteAppHost.Create(reopenedServices.Repository, reopenedServices.Formats, durableSeed.Id);
        await reopened.InitializeAsync();
        Assert.Equal(durableSeed.Id, reopened.Document!.Id);
        Assert.False(reopened.Page.IsDirty);
        Assert.Collection(reopened.Document.Sections.SelectMany(section => section.Pages).SelectMany(page => page.Blocks)
            .Single(block => block.Id == drawing.Id).SharedProductivityBundles!,
            bundle => Assert.True(JsonElement.DeepEquals(first, bundle)),
            bundle => Assert.True(JsonElement.DeepEquals(second, bundle)));
    }

    [AvaloniaFact]
    public async Task Edit_during_actual_save_remains_dirty_and_is_saved_by_later_close()
    {
        using var paths = new TemporaryWritePaths();
        var services = new DocumentServices(paths);
        var document = NotesDocument.Create("Stored title");
        await services.Repository.SaveAsync(document, "Seed original identity", default);
        var repository = new ControlledNotesRepository(services.Repository);
        await using var host = WriteAppHost.Create(repository, services.Formats, document.Id);
        await host.InitializeAsync();
        Title(host).Text = "Submitted title";
        repository.DelayNextSave();
        var save = host.Page.SaveAsync("Save submitted title");
        await repository.SaveEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        try
        {
            Title(host).Text = "Later unsaved title";
            Assert.False(await host.TrySaveBeforeCloseAsync());
            Assert.Equal(document.Id, host.Document!.Id);
            Assert.Equal("Later unsaved title", host.Document.Title);
        }
        finally { repository.ReleaseSave.TrySetResult(); }
        Assert.False(await save);

        Assert.True(host.Page.IsDirty);
        Assert.Equal("Later unsaved title", host.Document!.Title);
        Assert.Equal("Submitted title", (await services.Repository.LoadAsync(document.Id, default))!.Title);
        Assert.True(await host.TrySaveBeforeCloseAsync());
        Assert.Equal("Later unsaved title", (await services.Repository.LoadAsync(document.Id, default))!.Title);
        Assert.Equal(3, (await services.Repository.LoadAsync(document.Id, default))!.Version);
    }

    [AvaloniaFact]
    public async Task Close_refuses_while_actual_initial_library_read_is_pending()
    {
        using var paths = new TemporaryWritePaths();
        var services = new DocumentServices(paths);
        var repository = new ControlledNotesRepository(services.Repository);
        repository.DelayNextList();
        await using var host = WriteAppHost.Create(repository, services.Formats);
        var initialize = host.InitializeAsync();
        await repository.ListEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        try
        {
            Assert.Null(host.Document);
            Assert.False(await host.TrySaveBeforeCloseAsync());
        }
        finally { repository.ReleaseList.TrySetResult(); }
        await initialize;
        Assert.True(await host.TrySaveBeforeCloseAsync());
    }

    [AvaloniaFact]
    public async Task Print_hands_the_open_document_to_the_owner_print_service()
    {
        using var paths = new TemporaryWritePaths();
        var services = new DocumentServices(paths);
        var document = NotesDocument.Create("Ready for print");
        await services.Repository.SaveAsync(document, "Seed original identity", default);
        var formats = new RecordingFormats(services.Formats);

        await using (var host = WriteAppHost.Create(services.Repository, formats, document.Id))
        {
            await host.InitializeAsync();
            await host.PrintAsync();

            var printed = Assert.Single(formats.Printed);
            Assert.Equal(document.Id, printed.Id);
            Assert.Equal(document.Title, printed.Title);
        }
    }

    [AvaloniaFact]
    public async Task Print_without_an_open_document_fails_before_reaching_the_print_service()
    {
        using var paths = new TemporaryWritePaths();
        var services = new DocumentServices(paths);
        var formats = new RecordingFormats(services.Formats);

        await using (var host = WriteAppHost.Create(services.Repository, formats))
        {
            await host.InitializeAsync();
            Assert.Null(host.Document);

            await Assert.ThrowsAsync<InvalidOperationException>(() => host.PrintAsync());
        }
        Assert.Empty(formats.Printed);
    }

    private static Input Title(WriteAppHost host) => Assert.Single(
        Assert.IsType<HavenSceneControl>(host.Page.Content).Root!.DescendantsAndSelf().OfType<Input>(),
        element => element.Name == "Write.Word.Title");
    private static string WriteStatus(WriteAppHost host) => Assert.Single(
        Assert.IsType<HavenSceneControl>(host.Page.Content).Root!.DescendantsAndSelf().OfType<HavenText>(),
        text => text.Name == "Write.Status").Content?.ToString() ?? string.Empty;

    private static HomeProductivityEngine InkEngine() => new(handlers: [new CanvasInkObjectHandler()]);
    private static HomeProductivityContext InkContext(WriteDocumentEditor editor) => new("write",
        editor.Document.Id.ToString("D"), editor.Document.Version, [], new HashSet<string>(StringComparer.Ordinal) { "drawing.ink" });
    private static JsonElement InkBundle(HomeProductivityEngine engine, WriteDocumentEditor editor, Guid layer, double y) =>
        CanvasSharedInkStrokeFactory.Capture(engine, InkContext(editor), layer, [new(40, y, .4), new(160, y, .8)]);

    /// <summary>Test-only negotiation probe. It deliberately cannot provide a document engine and
    /// must never be invoked as the normal Write runtime.</summary>
    private sealed class BootstrapProbeWriteEngine(WriteEngineCapabilities capabilities) : IWriteDocumentEngine
    {
        public int OpenCalls { get; private set; }
        public int DisposeCalls { get; private set; }
        public WriteEngineCapabilities Capabilities { get; } = capabilities;
        public WriteEngineDocumentState? CurrentDocument => null;
        private static InvalidOperationException NotConnected() => new("The bootstrap probe is not a document engine implementation.");
        public Task<WriteEngineDocumentState> OpenAsync(WriteEngineOpenRequest request, CancellationToken cancellationToken = default)
        { OpenCalls++; throw NotConnected(); }
        public Task<WriteEngineTile> RenderTileAsync(WriteEngineTileRequest request, CancellationToken cancellationToken = default) => throw NotConnected();
        public Task PostTextInputAsync(string text, CancellationToken cancellationToken = default) => throw NotConnected();
        public Task PostKeyAsync(WriteEngineKeyEvent keyEvent, CancellationToken cancellationToken = default) => throw NotConnected();
        public Task PostPointerAsync(WriteEnginePointerEvent pointerEvent, CancellationToken cancellationToken = default) => throw NotConnected();
        public Task ExecuteCommandAsync(WriteEngineCommand command, CancellationToken cancellationToken = default) => throw NotConnected();
        public Task<WriteEngineAccessibilitySnapshot> GetAccessibilitySnapshotAsync(CancellationToken cancellationToken = default) => throw NotConnected();
        public Task SaveAsync(WriteEngineSaveRequest request, CancellationToken cancellationToken = default) => throw NotConnected();
        public Task CloseAsync(CancellationToken cancellationToken = default) => throw NotConnected();
        public ValueTask DisposeAsync() { DisposeCalls++; return ValueTask.CompletedTask; }
    }

    private sealed class RecordingFormats(INotesImportExportService inner) : INotesImportExportService
    {
        public List<NotesDocument> Printed { get; } = [];
        public IReadOnlyList<string> ImportExtensions => inner.ImportExtensions;
        public IReadOnlyList<string> ExportExtensions => inner.ExportExtensions;
        public Task<NotesDocument> ImportAsync(string sourcePath, CancellationToken cancellationToken) =>
            inner.ImportAsync(sourcePath, cancellationToken);
        public Task<string> ExportAsync(NotesDocument document, string destinationPath, CancellationToken cancellationToken) =>
            inner.ExportAsync(document, destinationPath, cancellationToken);
        public Task PrintAsync(NotesDocument document, CancellationToken cancellationToken)
        {
            Printed.Add(document);
            return Task.CompletedTask;
        }
    }

    private sealed class DocumentServices
    {
        public DocumentServices(IAppPaths paths)
        {
            var validator = new NotesDocumentValidator();
            var diagnostics = new ProductionDiagnostics(paths);
            Repository = new NotesRepository(paths, validator, diagnostics);
            Formats = new NotesImportExportService(validator, diagnostics);
        }
        public NotesRepository Repository { get; }
        public NotesImportExportService Formats { get; }
    }

    private sealed class ControlledNotesRepository(INotesRepository inner) : INotesRepository
    {
        private bool _delaySave, _delayList;
        public bool FailSaves { get; set; }
        public Exception? SaveFailure { get; set; }
        public List<Exception> Failures { get; } = [];
        public TaskCompletionSource ThirdFailure { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource SaveEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseSave { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ListEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseList { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void DelayNextSave() => _delaySave = true;
        public void DelayNextList() => _delayList = true;

        public async Task<IReadOnlyList<NotesDocumentSummary>> ListAsync(CancellationToken token)
        {
            if (_delayList)
            {
                _delayList = false; ListEntered.TrySetResult();
                await ReleaseList.Task.WaitAsync(token);
            }
            return await inner.ListAsync(token);
        }
        public async Task<NotesSaveResult> SaveAsync(NotesDocument document, string reason, CancellationToken token)
        {
            if (FailSaves)
            {
                var failure = SaveFailure ?? new IOException("Controlled refusal before original repository publication.");
                Failures.Add(failure);
                if (Failures.Count == 3) ThirdFailure.TrySetResult();
                throw failure;
            }
            if (_delaySave)
            {
                _delaySave = false; SaveEntered.TrySetResult();
                await ReleaseSave.Task.WaitAsync(token);
            }
            return await inner.SaveAsync(document, reason, token);
        }
        public Task<NotesDocument?> LoadAsync(Guid id, CancellationToken token) => inner.LoadAsync(id, token);
        public Task DeleteAsync(Guid id, CancellationToken token) => inner.DeleteAsync(id, token);
        public Task<IReadOnlyList<NotesVersionInfo>> GetVersionsAsync(Guid id, CancellationToken token) => inner.GetVersionsAsync(id, token);
        public Task<NotesDocument?> LoadVersionAsync(Guid id, string versionId, CancellationToken token) => inner.LoadVersionAsync(id, versionId, token);
        public Task<NotesDocument?> RecoverLatestAsync(Guid id, CancellationToken token) => inner.RecoverLatestAsync(id, token);
        public Task<IReadOnlyList<NotesSearchHit>> SearchAsync(string query, CancellationToken token) => inner.SearchAsync(query, token);
    }

    private sealed class TemporaryWritePaths : IAppPaths, IDisposable
    {
        public string DataDirectory { get; } = Path.Combine(Path.GetTempPath(), "haven-write-app-tests", Guid.NewGuid().ToString("N"));
        public string DatabasePath => Path.Combine(DataDirectory, "haven.db");
        public string BrowserProfileDirectory => Path.Combine(DataDirectory, "BrowserProfile");
        public string AttachmentsDirectory => Path.Combine(DataDirectory, "Attachments");
        public string LogsDirectory => Path.Combine(DataDirectory, "Logs");
        public string LegacyStatePath => Path.Combine(DataDirectory, "legacy.json");
        public void Dispose() { if (Directory.Exists(DataDirectory)) Directory.Delete(DataDirectory, true); }
    }
}
